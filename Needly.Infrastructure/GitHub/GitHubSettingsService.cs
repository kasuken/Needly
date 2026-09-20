using Microsoft.EntityFrameworkCore;
using Needly.Application.GitHub;

namespace Needly.Infrastructure.GitHub;

/// <summary>Reads installation and repository data for the authenticated Settings page.</summary>
public sealed class GitHubSettingsService(NeedlyDbContext dbContext) : IGitHubSettingsService
{
    private readonly NeedlyDbContext dbContext = dbContext
        ?? throw new ArgumentNullException(nameof(dbContext));

    /// <inheritdoc />
    public async Task<GitHubSettings> GetAsync(
        Guid needlyUserId,
        CancellationToken cancellationToken)
    {
        // Surface every installation the user can see: those they explicitly connected
        // through /github/setup (UserInstallations) and those they belong to via an active
        // installation membership (InstallationMembers). Membership is populated by the
        // installation webhook and org membership sync, so an org installation stays visible
        // here even when the /github/setup link was never created — matching how the inbox,
        // done, and waiting-on-others views already scope visibility.
        var gitHubUserId = await dbContext.NeedlyUsers
            .AsNoTracking()
            .Where(user => user.Id == needlyUserId)
            .Select(user => (Guid?)user.GitHubUserId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var installations = await dbContext.Installations
            .AsNoTracking()
            .Where(installation =>
                dbContext.UserInstallations.Any(link =>
                    link.NeedlyUserId == needlyUserId &&
                    link.GitHubInstallationId == installation.GitHubInstallationId) ||
                (gitHubUserId != null && dbContext.InstallationMembers.Any(member =>
                    member.InstallationId == installation.Id &&
                    member.GitHubUserId == gitHubUserId.Value &&
                    member.IsActive)))
            .OrderBy(installation => installation.AccountLogin)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var internalInstallationIds = installations.Select(installation => installation.Id).ToArray();
        var repositories = await dbContext.Repositories
            .AsNoTracking()
            .Where(repository =>
                repository.IsActive &&
                internalInstallationIds.Contains(repository.InstallationId))
            .OrderBy(repository => repository.Owner)
            .ThenBy(repository => repository.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = installations.Select(installation =>
        {
            var installationRepositories = repositories
                .Where(repository => repository.InstallationId == installation.Id)
                .ToArray();
            return new InstallationSettingsItem(
                installation.GitHubInstallationId,
                installation.AccountLogin,
                installation.AccountType,
                installation.State,
                installationRepositories
                    .Select(repository => new RepositorySettingsItem(
                        repository.GitHubRepositoryId,
                        repository.Owner,
                        repository.Name))
                    .ToArray(),
                installationRepositories.Count(repository =>
                    repository.HistoricalBootstrapCompletedAt is not null));
        })
            .ToArray();
        return new GitHubSettings(items);
    }
}