using Microsoft.EntityFrameworkCore;
using Needly.Application.GitHub;
using Needly.Domain;

namespace Needly.Infrastructure.GitHub;

/// <summary>Returns only actions routed through active installation memberships.</summary>
public sealed class InboxVisibilityService(
    NeedlyDbContext dbContext,
    TimeProvider timeProvider) : IInboxVisibilityService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<VisibleAction>> GetVisibleAsync(
        Guid needlyUserId,
        CancellationToken cancellationToken)
    {
        var gitHubUserId = await dbContext.NeedlyUsers
            .AsNoTracking()
            .Where(user => user.Id == needlyUserId)
            .Select(user => (Guid?)user.GitHubUserId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (gitHubUserId is null)
        {
            return [];
        }

        var now = timeProvider.GetUtcNow();
        var visible = await dbContext.Actions
            .AsNoTracking()
            .Where(action =>
                action.State == ActionState.Open &&
                dbContext.Installations.Any(installation =>
                    installation.Id == action.InstallationId &&
                    installation.State == InstallationState.Active) &&
                dbContext.InstallationMembers.Any(member =>
                    member.InstallationId == action.InstallationId &&
                    member.GitHubUserId == gitHubUserId.Value &&
                    member.IsActive) &&
                !dbContext.ActionSuppressions.Any(suppression =>
                    suppression.NeedlyUserId == needlyUserId &&
                    suppression.InstallationId == action.InstallationId &&
                    suppression.RepositoryId == action.RepositoryId &&
                    suppression.SubjectType == action.SubjectType &&
                    suppression.SubjectNumber == action.SubjectNumber &&
                    suppression.AssigneeType == action.AssigneeType &&
                    suppression.AssigneeId == action.AssigneeId &&
                    suppression.IsActive) &&
                ((action.AssigneeType == ActionAssigneeType.User &&
                  action.AssigneeId == gitHubUserId.Value) ||
                 (action.AssigneeType == ActionAssigneeType.Team &&
                  dbContext.TeamMembers.Any(member =>
                      member.TeamId == action.AssigneeId &&
                      member.GitHubUserId == gitHubUserId.Value &&
                                            member.IsActive))))
            .Join(
                dbContext.Repositories.AsNoTracking(),
                action => action.RepositoryId,
                repository => repository.Id,
                (action, repository) => new
                {
                    Action = action,
                    Repository = repository,
                    UserName = action.AssigneeType == ActionAssigneeType.User
                        ? dbContext.GitHubUsers
                            .Where(user => user.Id == action.AssigneeId)
                            .Select(user => user.DisplayName)
                            .SingleOrDefault()
                        : null,
                    UserLogin = action.AssigneeType == ActionAssigneeType.User
                        ? dbContext.GitHubUsers
                            .Where(user => user.Id == action.AssigneeId)
                            .Select(user => user.Login)
                            .SingleOrDefault()
                        : null,
                    TeamName = action.AssigneeType == ActionAssigneeType.Team
                        ? dbContext.Teams
                            .Where(team => team.Id == action.AssigneeId)
                            .Select(team => team.Name)
                            .SingleOrDefault()
                        : null,
                    TeamSlug = action.AssigneeType == ActionAssigneeType.Team
                        ? dbContext.Teams
                            .Where(team => team.Id == action.AssigneeId)
                            .Select(team => team.Slug)
                            .SingleOrDefault()
                        : null
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var actionIds = visible.Select(item => item.Action.Id).ToArray();
        var dispositions = await dbContext.ActionDispositions
            .AsNoTracking()
            .Where(disposition =>
                disposition.NeedlyUserId == needlyUserId &&
                actionIds.Contains(disposition.ActionId))
            .ToDictionaryAsync(disposition => disposition.ActionId, cancellationToken)
            .ConfigureAwait(false);
        return visible
            .Where(item =>
                !dispositions.TryGetValue(item.Action.Id, out var disposition) ||
                (!disposition.IsArchived &&
                 !disposition.IsMuted &&
                 (disposition.SnoozedUntil is null || disposition.SnoozedUntil <= now)))
            .OrderByDescending(item =>
                dispositions.TryGetValue(item.Action.Id, out var disposition) &&
                disposition.IsPinned)
            .ThenByDescending(item => item.Action.IsAtRisk)
            .ThenBy(item => item.Action.WaitingSince)
            .Select(item =>
            {
                dispositions.TryGetValue(item.Action.Id, out var disposition);
                return new VisibleAction(
                    item.Action.Id,
                    item.Repository.Owner,
                    item.Repository.Name,
                    item.Action.Title,
                    item.Action.SubjectNumber,
                    item.Action.SubjectType,
                    item.Action.SubjectUrl.Value,
                    disposition?.IsFyi == true ? ActionType.FYI : item.Action.Type,
                    item.Action.State,
                    item.Action.Reason,
                    item.Action.Context,
                    FormatAssignee(
                        item.Action.AssigneeType,
                        item.UserName,
                        item.UserLogin,
                        item.TeamName,
                        item.TeamSlug),
                    item.Action.Context ?? item.Action.Reason,
                    item.Action.WaitingSince,
                    now > item.Action.WaitingSince ? now - item.Action.WaitingSince : TimeSpan.Zero,
                    item.Action.IsAtRisk,
                    item.Action.RiskReason,
                    item.Action.AuthorLogin,
                    item.Action.AssigneeType == ActionAssigneeType.User
                        ? ActionAssigneeScope.Me
                        : ActionAssigneeScope.MyTeam,
                    item.Action.HasBotInvolvement,
                    disposition?.IsPinned == true);
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VisibleAction>> GetVisibleAsync(
        Guid needlyUserId,
        ActionFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var visible = await GetVisibleAsync(needlyUserId, cancellationToken).ConfigureAwait(false);
        return visible.Where(action => ActionFilterMatcher.IsMatch(
            filter,
            VisibleActionFilterCandidate.Create(action))).ToArray();
    }

    private static string FormatAssignee(
        ActionAssigneeType assigneeType,
        string? userName,
        string? userLogin,
        string? teamName,
        string? teamSlug)
    {
        var name = assigneeType == ActionAssigneeType.User ? userName : teamName;
        var login = assigneeType == ActionAssigneeType.User ? userLogin : teamSlug;
        return string.IsNullOrWhiteSpace(name)
            ? $"@{login}"
            : $"{name} (@{login})";
    }
}