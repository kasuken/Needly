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
        var linkedInstallationIds = await dbContext.UserInstallations
            .AsNoTracking()
            .Where(link => link.NeedlyUserId == needlyUserId)
            .Select(link => link.GitHubInstallationId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var installations = await dbContext.Installations
            .AsNoTracking()
            .Where(installation => linkedInstallationIds.Contains(installation.GitHubInstallationId))
            .OrderBy(installation => installation.AccountLogin)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var internalInstallationIds = installations.Select(installation => installation.Id).ToArray();
        var repositories = await dbContext.Repositories
            .AsNoTracking()
            .Where(repository => internalInstallationIds.Contains(repository.InstallationId))
            .OrderBy(repository => repository.Owner)
            .ThenBy(repository => repository.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = installations.Select(installation =>
            new InstallationSettingsItem(
                installation.GitHubInstallationId,
                installation.AccountLogin,
                installation.AccountType,
                installation.State,
                repositories
                    .Where(repository => repository.InstallationId == installation.Id)
                    .Select(repository => new RepositorySettingsItem(
                        repository.GitHubRepositoryId,
                        repository.Owner,
                        repository.Name))
                    .ToArray()))
            .ToArray();
        return new GitHubSettings(items);
    }
}