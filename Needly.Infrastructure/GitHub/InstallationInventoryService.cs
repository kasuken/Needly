using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Needly.Application.GitHub;
using Needly.Domain;

namespace Needly.Infrastructure.GitHub;

/// <summary>Applies GitHub App installation inventory events to durable storage.</summary>
public sealed class InstallationInventoryService(
    NeedlyDbContext dbContext,
    ILogger<InstallationInventoryService> logger) : IInstallationInventoryService
{
    private readonly NeedlyDbContext dbContext = dbContext
        ?? throw new ArgumentNullException(nameof(dbContext));
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
                        accountType);
                    dbContext.Installations.Add(installation);
                }
                else
                {
                    installation.Activate(payload.Account.Login, accountType, occurredAt);
                }

                if (installationEvent.Repositories is not null)
                {
                    await UpsertRepositoriesAsync(
                        installation.Id,
                        installationEvent.Repositories,
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
                    .Activate(payload.Account.Login, accountType, occurredAt);
                break;
            default:
                throw new ArgumentException(
                    $"Unsupported installation action '{installationEvent.Action}'.",
                    nameof(installationEvent));
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
            dbContext.Repositories.RemoveRange(removed);
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
        if (!await dbContext.NeedlyUsers
            .AnyAsync(user => user.Id == needlyUserId, cancellationToken)
            .ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Needly user {needlyUserId} was not found.");
        }

        var exists = await dbContext.UserInstallations
            .AnyAsync(
                link => link.NeedlyUserId == needlyUserId &&
                        link.GitHubInstallationId == gitHubInstallationId,
                cancellationToken)
            .ConfigureAwait(false);
        if (exists)
        {
            return;
        }

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