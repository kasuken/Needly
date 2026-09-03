using Microsoft.EntityFrameworkCore;
using Needly.Application.GitHub;
using Needly.Domain;

namespace Needly.Infrastructure.GitHub;

/// <summary>Resolves active installation-scoped teams and their active members.</summary>
public sealed class TeamReviewResolver(NeedlyDbContext dbContext) : ITeamReviewResolver
{
    /// <inheritdoc />
    public async Task<TeamReviewTarget?> ResolveAsync(
        long gitHubInstallationId,
        long gitHubTeamId,
        CancellationToken cancellationToken)
    {
        var team = await dbContext.Teams
            .AsNoTracking()
            .Where(item =>
                item.GitHubTeamId == gitHubTeamId &&
                item.IsActive &&
                dbContext.Installations.Any(installation =>
                    installation.Id == item.InstallationId &&
                    installation.GitHubInstallationId == gitHubInstallationId &&
                    installation.State == InstallationState.Active))
            .Select(item => new { item.Id, item.GitHubTeamId, item.Slug })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (team is null)
        {
            return null;
        }

        var memberIds = await dbContext.TeamMembers
            .AsNoTracking()
            .Where(member => member.TeamId == team.Id && member.IsActive)
            .Select(member => member.GitHubUserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new TeamReviewTarget(team.Id, team.GitHubTeamId, team.Slug, memberIds);
    }
}