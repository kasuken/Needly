using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Needly.Application.GitHub;
using Needly.Domain;

namespace Needly.Infrastructure.GitHub;

/// <summary>Maintains durable installation-scoped organization and team memberships.</summary>
public sealed class GitHubOrganizationMembershipService(
    NeedlyDbContext dbContext,
    IGitHubApiClientFactory apiClientFactory,
    TimeProvider timeProvider,
    ILogger<GitHubOrganizationMembershipService> logger) : IGitHubOrganizationMembershipService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task SyncAsync(long gitHubInstallationId, CancellationToken cancellationToken)
    {
        var installation = await RequireInstallationAsync(gitHubInstallationId, cancellationToken)
            .ConfigureAwait(false);
        if (installation.AccountType != GitHubAccountType.Organization)
        {
            return;
        }

        var client = await apiClientFactory.CreateAsync(gitHubInstallationId, cancellationToken)
            .ConfigureAwait(false);
        var members = await GetAllAsync<GitHubUserPayload>(
            client,
            $"orgs/{Uri.EscapeDataString(installation.AccountLogin)}/members?per_page=100",
            cancellationToken).ConfigureAwait(false);
        var teams = await GetAllAsync<GitHubTeamPayload>(
            client,
            $"orgs/{Uri.EscapeDataString(installation.AccountLogin)}/teams?per_page=100",
            cancellationToken).ConfigureAwait(false);
        var occurredAt = timeProvider.GetUtcNow();

        var activeUserIds = new HashSet<Guid>();
        foreach (var member in members)
        {
            var user = await UpsertUserAsync(member, occurredAt, cancellationToken).ConfigureAwait(false);
            activeUserIds.Add(user.Id);
            await SetInstallationMembershipAsync(
                installation.Id,
                user.Id,
                true,
                occurredAt,
                cancellationToken).ConfigureAwait(false);
        }

        var existingMemberships = await dbContext.InstallationMembers
            .Where(item => item.InstallationId == installation.Id && item.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var membership in existingMemberships.Where(item => !activeUserIds.Contains(item.GitHubUserId)))
        {
            membership.Deactivate(occurredAt);
        }

        var activeTeamIds = new HashSet<Guid>();
        foreach (var teamPayload in teams)
        {
            var team = await UpsertTeamAsync(installation.Id, teamPayload, occurredAt, cancellationToken)
                .ConfigureAwait(false);
            activeTeamIds.Add(team.Id);
            var teamMembers = await GetAllAsync<GitHubUserPayload>(
                client,
                $"teams/{teamPayload.Id}/members?per_page=100",
                cancellationToken).ConfigureAwait(false);
            var activeTeamUserIds = new HashSet<Guid>();
            foreach (var member in teamMembers)
            {
                var user = await UpsertUserAsync(member, occurredAt, cancellationToken).ConfigureAwait(false);
                activeTeamUserIds.Add(user.Id);
                await SetTeamMembershipAsync(team.Id, user.Id, true, occurredAt, cancellationToken)
                    .ConfigureAwait(false);
            }

            var staleTeamMembers = await dbContext.TeamMembers
                .Where(item => item.TeamId == team.Id && item.IsActive)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var membership in staleTeamMembers.Where(item => !activeTeamUserIds.Contains(item.GitHubUserId)))
            {
                membership.Deactivate(occurredAt);
            }
        }

        var staleTeams = await dbContext.Teams
            .Where(item => item.InstallationId == installation.Id && item.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var team in staleTeams.Where(item => !activeTeamIds.Contains(item.Id)))
        {
            team.Deactivate(occurredAt);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Synchronized GitHub organization membership for installation {GitHubInstallationId}: {MemberCount} members and {TeamCount} teams",
            gitHubInstallationId,
            members.Count,
            teams.Count);
    }

    /// <inheritdoc />
    public async Task HandleMemberAsync(
        GitHubMemberEvent memberEvent,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(memberEvent);
        var installation = await RequireInstallationAsync(memberEvent.Installation.Id, cancellationToken)
            .ConfigureAwait(false);
        var user = await UpsertUserAsync(memberEvent.Member, occurredAt, cancellationToken).ConfigureAwait(false);
        var isActive = memberEvent.Action switch
        {
            "added" or "edited" => true,
            "removed" => false,
            _ => throw new ArgumentException($"Unsupported member action '{memberEvent.Action}'.", nameof(memberEvent))
        };
        await SetInstallationMembershipAsync(
            installation.Id,
            user.Id,
            isActive,
            occurredAt,
            cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task HandleTeamAsync(
        GitHubTeamEvent teamEvent,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(teamEvent);
        var installation = await RequireInstallationAsync(teamEvent.Installation.Id, cancellationToken)
            .ConfigureAwait(false);
        var team = await UpsertTeamAsync(
            installation.Id,
            teamEvent.Team,
            occurredAt,
            cancellationToken).ConfigureAwait(false);
        switch (teamEvent.Action)
        {
            case "created":
            case "edited":
                break;
            case "deleted":
                team.Deactivate(occurredAt);
                break;
            default:
                throw new ArgumentException($"Unsupported team action '{teamEvent.Action}'.", nameof(teamEvent));
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task HandleMembershipAsync(
        GitHubMembershipEvent membershipEvent,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(membershipEvent);
        var installation = await RequireInstallationAsync(membershipEvent.Installation.Id, cancellationToken)
            .ConfigureAwait(false);
        var team = await UpsertTeamAsync(
            installation.Id,
            membershipEvent.Team,
            occurredAt,
            cancellationToken).ConfigureAwait(false);
        var user = await UpsertUserAsync(membershipEvent.Member, occurredAt, cancellationToken).ConfigureAwait(false);
        var isActive = membershipEvent.Action switch
        {
            "added" => true,
            "removed" => false,
            _ => throw new ArgumentException(
                $"Unsupported membership action '{membershipEvent.Action}'.",
                nameof(membershipEvent))
        };
        await SetTeamMembershipAsync(team.Id, user.Id, isActive, occurredAt, cancellationToken)
            .ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<Installation> RequireInstallationAsync(
        long gitHubInstallationId,
        CancellationToken cancellationToken) =>
        await dbContext.Installations.SingleOrDefaultAsync(
            item => item.GitHubInstallationId == gitHubInstallationId &&
                    item.State == InstallationState.Active,
            cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"Active GitHub installation {gitHubInstallationId} was not found.");

    private async Task<GitHubUser> UpsertUserAsync(
        GitHubUserPayload payload,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var user = dbContext.GitHubUsers.Local.SingleOrDefault(item => item.GitHubUserId == payload.Id)
            ?? await dbContext.GitHubUsers.SingleOrDefaultAsync(
                item => item.GitHubUserId == payload.Id,
                cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            user = GitHubUser.Create(
                Guid.NewGuid(),
                payload.Id,
                payload.Login,
                payload.Name,
                payload.AvatarUrl,
                occurredAt);
            dbContext.GitHubUsers.Add(user);
        }
        else
        {
            user.Update(payload.Login, payload.Name, payload.AvatarUrl, occurredAt);
        }

        return user;
    }

    private async Task<Team> UpsertTeamAsync(
        Guid installationId,
        GitHubTeamPayload payload,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var team = dbContext.Teams.Local.SingleOrDefault(item =>
                item.InstallationId == installationId && item.GitHubTeamId == payload.Id)
            ?? await dbContext.Teams.SingleOrDefaultAsync(
                item => item.InstallationId == installationId && item.GitHubTeamId == payload.Id,
                cancellationToken).ConfigureAwait(false);
        if (team is null)
        {
            team = Team.Create(Guid.NewGuid(), installationId, payload.Id, payload.Slug, payload.Name, occurredAt);
            dbContext.Teams.Add(team);
        }
        else
        {
            team.Update(payload.Slug, payload.Name, occurredAt);
        }

        return team;
    }

    private async Task SetInstallationMembershipAsync(
        Guid installationId,
        Guid gitHubUserId,
        bool isActive,
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
            if (isActive)
            {
                dbContext.InstallationMembers.Add(
                    InstallationMember.Create(Guid.NewGuid(), installationId, gitHubUserId, occurredAt));
            }

            return;
        }

        if (isActive)
        {
            membership.Activate(occurredAt);
        }
        else
        {
            membership.Deactivate(occurredAt);
        }
    }

    private async Task SetTeamMembershipAsync(
        Guid teamId,
        Guid gitHubUserId,
        bool isActive,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var membership = dbContext.TeamMembers.Local.SingleOrDefault(item =>
                item.TeamId == teamId && item.GitHubUserId == gitHubUserId)
            ?? await dbContext.TeamMembers.SingleOrDefaultAsync(
                item => item.TeamId == teamId && item.GitHubUserId == gitHubUserId,
                cancellationToken).ConfigureAwait(false);
        if (membership is null)
        {
            if (isActive)
            {
                dbContext.TeamMembers.Add(TeamMember.Create(Guid.NewGuid(), teamId, gitHubUserId, occurredAt));
            }

            return;
        }

        if (isActive)
        {
            membership.Activate(occurredAt);
        }
        else
        {
            membership.Deactivate(occurredAt);
        }
    }

    private static async Task<IReadOnlyList<T>> GetAllAsync<T>(
        IGitHubApiClient client,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var items = new List<T>();
        var page = 1;
        while (true)
        {
            var pagePath = page == 1 ? relativePath : $"{relativePath}&page={page}";
            using var response = await client.SendAsync(HttpMethod.Get, pagePath, null, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var pageItems = await response.Content
                .ReadFromJsonAsync<IReadOnlyList<T>>(JsonOptions, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new JsonException($"GitHub returned an empty response for '{pagePath}'.");
            items.AddRange(pageItems);

            var hasNextPage = response.Headers.TryGetValues("Link", out var links) &&
                links.Any(link => link.Contains("rel=\"next\"", StringComparison.Ordinal));
            if (!hasNextPage)
            {
                return items;
            }

            page++;
        }
    }
}