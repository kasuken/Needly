using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Needly.Application.GitHub;
using Needly.Domain;

namespace Needly.Infrastructure.GitHub;

/// <summary>Applies GitHub App installation inventory events to durable storage.</summary>
public sealed class InstallationInventoryService(
    NeedlyDbContext dbContext,
    IGitHubApiClientFactory apiClientFactory,
    TimeProvider timeProvider,
    ILogger<InstallationInventoryService> logger) : IInstallationInventoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NeedlyDbContext dbContext = dbContext
        ?? throw new ArgumentNullException(nameof(dbContext));
    private readonly IGitHubApiClientFactory apiClientFactory = apiClientFactory
        ?? throw new ArgumentNullException(nameof(apiClientFactory));
    private readonly TimeProvider timeProvider = timeProvider
        ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger<InstallationInventoryService> logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task HandleInstallationAsync(
        GitHubInstallationEvent installationEvent,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installationEvent);
        var payload = installationEvent.Installation;
        var installation = await dbContext.Installations
            .SingleOrDefaultAsync(
                item => item.GitHubInstallationId == payload.Id,
                cancellationToken)
            .ConfigureAwait(false);
        var accountType = ParseAccountType(payload.Account.Type);
        var synchronizeRepositories = false;

        switch (installationEvent.Action)
        {
            case "created":
                if (installation is null)
                {
                    installation = Installation.Create(
                        Guid.NewGuid(),
                        payload.Id,
                        payload.Account.Login,
                        occurredAt,
                        accountType,
                        payload.Account.Id);
                    dbContext.Installations.Add(installation);
                }
                else
                {
                    installation.Activate(
                        payload.Account.Login,
                        payload.Account.Id,
                        accountType,
                        occurredAt);
                }

                if (installationEvent.Repositories is not null)
                {
                    await UpsertRepositoriesAsync(
                        installation.Id,
                        installationEvent.Repositories,
                        occurredAt,
                        cancellationToken).ConfigureAwait(false);
                }

                synchronizeRepositories = payload.RepositorySelection == "all" ||
                    installationEvent.Repositories is null;
                await EnsurePersonalInstallationMemberAsync(
                    installation,
                    payload.Account.Id,
                    occurredAt,
                    cancellationToken).ConfigureAwait(false);

                break;
            case "new_permissions_accepted":
                installation = RequireInstallation(installation, payload.Id);
                installation.Update(
                    payload.Account.Login,
                    payload.Account.Id,
                    accountType,
                    occurredAt);
                if (installation.IsActive)
                {
                    synchronizeRepositories = true;
                    await EnsurePersonalInstallationMemberAsync(
                        installation,
                        payload.Account.Id,
                        occurredAt,
                        cancellationToken).ConfigureAwait(false);
                }

                break;
            case "deleted":
                RequireInstallation(installation, payload.Id).Delete(occurredAt);
                break;
            case "suspend":
                RequireInstallation(installation, payload.Id).Suspend(occurredAt);
                break;
            case "unsuspend":
                RequireInstallation(installation, payload.Id)
                    .Activate(
                        payload.Account.Login,
                        payload.Account.Id,
                        accountType,
                        occurredAt);
                break;
            default:
                throw new ArgumentException(
                    $"Unsupported installation action '{installationEvent.Action}'.",
                    nameof(installationEvent));
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (synchronizeRepositories)
        {
            await SynchronizeRepositoriesAsync(payload.Id, cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation(
            "Applied GitHub installation action {Action} for installation {GitHubInstallationId}",
            installationEvent.Action,
            payload.Id);
    }

    /// <inheritdoc />
    public async Task HandleRepositoriesAsync(
        GitHubInstallationRepositoriesEvent repositoriesEvent,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repositoriesEvent);
        if (repositoriesEvent.Action is not ("added" or "removed"))
        {
            throw new ArgumentException(
                $"Unsupported installation_repositories action '{repositoriesEvent.Action}'.",
                nameof(repositoriesEvent));
        }

        var installation = await dbContext.Installations
            .SingleOrDefaultAsync(
                item => item.GitHubInstallationId == repositoriesEvent.Installation.Id,
                cancellationToken)
            .ConfigureAwait(false);
        installation = RequireInstallation(installation, repositoriesEvent.Installation.Id);
        if (!installation.IsActive)
        {
            throw new InvalidOperationException(
                $"GitHub installation {installation.GitHubInstallationId} is not active.");
        }

        await UpsertRepositoriesAsync(
            installation.Id,
            repositoriesEvent.RepositoriesAdded,
            occurredAt,
            cancellationToken).ConfigureAwait(false);

        var removedIds = repositoriesEvent.RepositoriesRemoved
            .Select(repository => repository.Id)
            .ToArray();
        if (removedIds.Length > 0)
        {
            var removed = await dbContext.Repositories
                .Where(repository =>
                    repository.InstallationId == installation.Id &&
                    removedIds.Contains(repository.GitHubRepositoryId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var repository in removed)
            {
                repository.Deactivate(occurredAt);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Applied repository selection action {Action} for installation {GitHubInstallationId}: {AddedCount} added, {RemovedCount} removed",
            repositoriesEvent.Action,
            installation.GitHubInstallationId,
            repositoriesEvent.RepositoriesAdded.Count,
            repositoriesEvent.RepositoriesRemoved.Count);
    }

    /// <inheritdoc />
    public async Task LinkUserAsync(
        Guid needlyUserId,
        long gitHubInstallationId,
        DateTimeOffset linkedAt,
        CancellationToken cancellationToken)
    {
        var gitHubUser = await dbContext.NeedlyUsers
            .Where(user => user.Id == needlyUserId)
            .Join(
                dbContext.GitHubUsers,
                user => user.GitHubUserId,
                user => user.Id,
                (_, user) => user)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (gitHubUser is null)
        {
            throw new InvalidOperationException($"Needly user {needlyUserId} was not found.");
        }

        var exists = await dbContext.UserInstallations
            .AnyAsync(
                link => link.NeedlyUserId == needlyUserId &&
                        link.GitHubInstallationId == gitHubInstallationId,
                cancellationToken)
            .ConfigureAwait(false);
        if (!exists)
        {
            dbContext.UserInstallations.Add(UserInstallation.Create(
                Guid.NewGuid(),
                needlyUserId,
                gitHubInstallationId,
                linkedAt));
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "Linked Needly user {NeedlyUserId} to GitHub installation {GitHubInstallationId}",
                needlyUserId,
                gitHubInstallationId);
        }

        var activeInstallation = await dbContext.Installations
            .SingleOrDefaultAsync(
                installation => installation.GitHubInstallationId == gitHubInstallationId &&
                                installation.State == InstallationState.Active,
                cancellationToken)
            .ConfigureAwait(false);
        if (activeInstallation is not null)
        {
            if (activeInstallation.AccountType == GitHubAccountType.User &&
                (activeInstallation.GitHubAccountId == gitHubUser.GitHubUserId ||
                 (activeInstallation.GitHubAccountId is null &&
                  string.Equals(
                      activeInstallation.AccountLogin,
                      gitHubUser.Login,
                      StringComparison.OrdinalIgnoreCase))))
            {
                await SetInstallationMembershipAsync(
                    activeInstallation.Id,
                    gitHubUser.Id,
                    linkedAt,
                    cancellationToken).ConfigureAwait(false);
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            await SynchronizeRepositoriesAsync(gitHubInstallationId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnsurePersonalInstallationMemberAsync(
        Installation installation,
        long accountGitHubUserId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        if (installation.AccountType != GitHubAccountType.User)
        {
            return;
        }

        var gitHubUserId = await dbContext.GitHubUsers
            .Where(user => user.GitHubUserId == accountGitHubUserId)
            .Select(user => (Guid?)user.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (gitHubUserId is not null)
        {
            await SetInstallationMembershipAsync(
                installation.Id,
                gitHubUserId.Value,
                occurredAt,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SetInstallationMembershipAsync(
        Guid installationId,
        Guid gitHubUserId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var membership = dbContext.InstallationMembers.Local.SingleOrDefault(item =>
                item.InstallationId == installationId && item.GitHubUserId == gitHubUserId)
            ?? await dbContext.InstallationMembers.SingleOrDefaultAsync(
                item => item.InstallationId == installationId && item.GitHubUserId == gitHubUserId,
                cancellationToken).ConfigureAwait(false);
        if (membership is null)
        {
            dbContext.InstallationMembers.Add(
                InstallationMember.Create(Guid.NewGuid(), installationId, gitHubUserId, occurredAt));
        }
        else
        {
            membership.Activate(occurredAt);
        }
    }

    private async Task SynchronizeRepositoriesAsync(
        long gitHubInstallationId,
        CancellationToken cancellationToken)
    {
        var installation = await dbContext.Installations
            .SingleAsync(
                item => item.GitHubInstallationId == gitHubInstallationId &&
                        item.State == InstallationState.Active,
                cancellationToken)
            .ConfigureAwait(false);
        var client = await apiClientFactory
            .CreateAsync(gitHubInstallationId, cancellationToken)
            .ConfigureAwait(false);
        var repositories = await GetAllRepositoriesAsync(client, cancellationToken).ConfigureAwait(false);
        var distinctRepositories = repositories
            .GroupBy(repository => repository.Id)
            .Select(group => group.Last())
            .ToArray();
        var occurredAt = timeProvider.GetUtcNow();

        await UpsertRepositoriesAsync(
            installation.Id,
            distinctRepositories,
            occurredAt,
            cancellationToken).ConfigureAwait(false);

        var repositoryIds = distinctRepositories
            .Select(repository => repository.Id)
            .ToHashSet();
        var existingRepositories = await dbContext.Repositories
            .Where(repository => repository.InstallationId == installation.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var repository in existingRepositories.Where(
                     repository => !repositoryIds.Contains(repository.GitHubRepositoryId)))
        {
            repository.Deactivate(occurredAt);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Synchronized {RepositoryCount} GitHub repositories for installation {GitHubInstallationId}",
            distinctRepositories.Length,
            gitHubInstallationId);
    }

    private async Task UpsertRepositoriesAsync(
        Guid installationId,
        IReadOnlyList<GitHubRepositoryPayload> repositories,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        if (repositories.Count == 0)
        {
            return;
        }

        var repositoryIds = repositories.Select(repository => repository.Id).ToArray();
        var existing = await dbContext.Repositories
            .Where(repository =>
                repository.InstallationId == installationId &&
                repositoryIds.Contains(repository.GitHubRepositoryId))
            .ToDictionaryAsync(
                repository => repository.GitHubRepositoryId,
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var payload in repositories)
        {
            if (existing.TryGetValue(payload.Id, out var repository))
            {
                repository.Update(payload.Owner.Login, payload.Name, occurredAt);
            }
            else
            {
                dbContext.Repositories.Add(Repository.Create(
                    Guid.NewGuid(),
                    installationId,
                    payload.Id,
                    payload.Owner.Login,
                    payload.Name,
                    occurredAt));
            }
        }
    }

    private static async Task<IReadOnlyList<GitHubRepositoryPayload>> GetAllRepositoriesAsync(
        IGitHubApiClient client,
        CancellationToken cancellationToken)
    {
        const string RelativePath = "installation/repositories?per_page=100";
        var repositories = new List<GitHubRepositoryPayload>();
        var page = 1;
        while (true)
        {
            var pagePath = page == 1 ? RelativePath : $"{RelativePath}&page={page}";
            using var response = await client
                .SendAsync(HttpMethod.Get, pagePath, null, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content
                .ReadFromJsonAsync<InstallationRepositoriesResponse>(JsonOptions, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new JsonException($"GitHub returned an empty response for '{pagePath}'.");
            repositories.AddRange(payload.Repositories);

            var hasNextPage = response.Headers.TryGetValues("Link", out var links) &&
                links.Any(link => link.Contains("rel=\"next\"", StringComparison.Ordinal));
            if (!hasNextPage)
            {
                return repositories;
            }

            page++;
        }
    }

    private sealed record InstallationRepositoriesResponse(
        [property: JsonPropertyName("repositories")] IReadOnlyList<GitHubRepositoryPayload> Repositories);

    private static Installation RequireInstallation(Installation? installation, long gitHubInstallationId) =>
        installation ?? throw new InvalidOperationException(
            $"GitHub installation {gitHubInstallationId} was not found.");

    private static GitHubAccountType ParseAccountType(string accountType) => accountType switch
    {
        "User" => GitHubAccountType.User,
        "Organization" => GitHubAccountType.Organization,
        _ => throw new ArgumentException($"Unsupported GitHub account type '{accountType}'.", nameof(accountType))
    };
}