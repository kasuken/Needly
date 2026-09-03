using Microsoft.EntityFrameworkCore;
using Needly.Application.GitHub;
using Needly.Domain;

namespace Needly.Infrastructure.Actions;

/// <summary>Evaluates every enabled matching user rule in configured order inside the action transaction.</summary>
public sealed class AutomationRuleEvaluator(TimeProvider timeProvider)
{
    /// <summary>Applies ordered all-match rules to actions changed by one GitHub event.</summary>
    public async Task<int> EvaluateAsync(
        NeedlyDbContext dbContext,
        GitHubStoredEvent storedEvent,
        IReadOnlyCollection<NeedlyAction> changedActions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(storedEvent);
        ArgumentNullException.ThrowIfNull(changedActions);
        if (changedActions.Count == 0)
        {
            return 0;
        }

        var installationIds = changedActions.Select(action => action.InstallationId).Distinct().ToArray();
        var users = await dbContext.NeedlyUsers
            .Where(user => dbContext.InstallationMembers.Any(member =>
                installationIds.Contains(member.InstallationId) &&
                member.GitHubUserId == user.GitHubUserId &&
                member.IsActive))
            .Select(user => new { user.Id, user.GitHubUserId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var userIds = users.Select(user => user.Id).ToArray();
        var rules = await dbContext.AutomationRules
            .Where(rule => userIds.Contains(rule.NeedlyUserId) && rule.IsEnabled)
            .OrderBy(rule => rule.NeedlyUserId)
            .ThenBy(rule => rule.SortOrder)
            .ThenBy(rule => rule.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var repositories = await dbContext.Repositories
            .Where(repository => changedActions.Select(action => action.RepositoryId).Contains(repository.Id))
            .ToDictionaryAsync(repository => repository.Id, cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        var executionCount = 0;

        foreach (var action in changedActions.OrderBy(action => action.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var repository = repositories[action.RepositoryId];
            foreach (var user in users.OrderBy(user => user.Id))
            {
                var scope = await GetAuthorizedScopeAsync(
                    dbContext, user.GitHubUserId, action, cancellationToken).ConfigureAwait(false);
                if (scope is null)
                {
                    continue;
                }

                var candidate = new ActionFilterCandidate(
                    action.Type,
                    action.State,
                    $"{repository.Owner}/{repository.Name}",
                    repository.Owner,
                    action.AuthorLogin,
                    scope.Value,
                    now > action.WaitingSince ? now - action.WaitingSince : TimeSpan.Zero,
                    action.HasBotInvolvement);
                foreach (var rule in rules.Where(rule => rule.NeedlyUserId == user.Id))
                {
                    var filter = ActionFilterJsonSerializer.Deserialize(rule.FilterJson);
                    if (!ActionFilterMatcher.IsMatch(filter, candidate))
                    {
                        continue;
                    }

                    var idempotencyKey = $"{user.Id:N}:{rule.Id:N}:{action.Id:N}:{storedEvent.EventId:N}";
                    var alreadyExecuted = dbContext.RuleExecutions.Local.Any(
                        execution => execution.IdempotencyKey == idempotencyKey) ||
                        await dbContext.RuleExecutions.AnyAsync(
                            execution => execution.IdempotencyKey == idempotencyKey,
                            cancellationToken).ConfigureAwait(false);
                    if (alreadyExecuted)
                    {
                        continue;
                    }

                    var disposition = dbContext.ActionDispositions.Local.SingleOrDefault(item =>
                        item.NeedlyUserId == user.Id && item.ActionId == action.Id)
                        ?? await dbContext.ActionDispositions.SingleOrDefaultAsync(item =>
                            item.NeedlyUserId == user.Id && item.ActionId == action.Id,
                            cancellationToken).ConfigureAwait(false);
                    if (disposition is null)
                    {
                        disposition = ActionDisposition.Create(Guid.NewGuid(), user.Id, action.Id, now);
                        dbContext.ActionDispositions.Add(disposition);
                    }

                    var snoozedUntil = rule.Effect == RuleEffect.Snooze
                        ? now.Add(rule.SnoozeDuration!.Value)
                        : (DateTimeOffset?)null;
                    disposition.Apply(rule.Effect, snoozedUntil, now);
                    if (rule.Effect == RuleEffect.Mute)
                    {
                        await EnsureSuppressionAsync(
                            dbContext, user.Id, action, now, cancellationToken).ConfigureAwait(false);
                    }

                    var explanation = CreateExplanation(rule, candidate, snoozedUntil);
                    dbContext.RuleExecutions.Add(RuleExecution.Create(
                        Guid.NewGuid(),
                        user.Id,
                        rule.Id,
                        rule.Name,
                        action.Id,
                        storedEvent.EventId,
                        rule.Effect,
                        rule.SortOrder,
                        explanation,
                        now));
                    executionCount++;
                }
            }
        }

        return executionCount;
    }

    private static async Task<ActionAssigneeScope?> GetAuthorizedScopeAsync(
        NeedlyDbContext dbContext,
        Guid gitHubUserId,
        NeedlyAction action,
        CancellationToken cancellationToken)
    {
        var hasInstallationAccess = await dbContext.Installations.AnyAsync(installation =>
            installation.Id == action.InstallationId && installation.State == InstallationState.Active,
            cancellationToken).ConfigureAwait(false) &&
            await dbContext.InstallationMembers.AnyAsync(member =>
                member.InstallationId == action.InstallationId &&
                member.GitHubUserId == gitHubUserId &&
                member.IsActive,
                cancellationToken).ConfigureAwait(false);
        if (!hasInstallationAccess)
        {
            return null;
        }

        if (action.AssigneeType == ActionAssigneeType.User)
        {
            return action.AssigneeId == gitHubUserId ? ActionAssigneeScope.Me : null;
        }

        return await dbContext.TeamMembers.AnyAsync(member =>
            member.TeamId == action.AssigneeId &&
            member.GitHubUserId == gitHubUserId &&
            member.IsActive,
            cancellationToken).ConfigureAwait(false)
            ? ActionAssigneeScope.MyTeam
            : null;
    }

    private static async Task EnsureSuppressionAsync(
        NeedlyDbContext dbContext,
        Guid needlyUserId,
        NeedlyAction action,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var exists = dbContext.ActionSuppressions.Local.Any(suppression =>
            suppression.NeedlyUserId == needlyUserId &&
            suppression.RepositoryId == action.RepositoryId &&
            suppression.SubjectType == action.SubjectType &&
            suppression.SubjectNumber == action.SubjectNumber &&
            suppression.AssigneeType == action.AssigneeType &&
            suppression.AssigneeId == action.AssigneeId &&
            suppression.IsActive) ||
            await dbContext.ActionSuppressions.AnyAsync(suppression =>
                suppression.NeedlyUserId == needlyUserId &&
                suppression.RepositoryId == action.RepositoryId &&
                suppression.SubjectType == action.SubjectType &&
                suppression.SubjectNumber == action.SubjectNumber &&
                suppression.AssigneeType == action.AssigneeType &&
                suppression.AssigneeId == action.AssigneeId &&
                suppression.IsActive,
                cancellationToken).ConfigureAwait(false);
        if (!exists)
        {
            dbContext.ActionSuppressions.Add(ActionSuppression.Create(
                Guid.NewGuid(), needlyUserId, action, now));
        }
    }

    private static string CreateExplanation(
        AutomationRule rule,
        ActionFilterCandidate candidate,
        DateTimeOffset? snoozedUntil)
    {
        var effect = rule.Effect switch
        {
            RuleEffect.AutoArchive => "archived it for you",
            RuleEffect.Mute => "muted it for you",
            RuleEffect.Snooze => $"snoozed it until {snoozedUntil:O}",
            RuleEffect.MarkFyi => "marked it as FYI for you",
            RuleEffect.Pin => "pinned it for you",
            _ => throw new ArgumentOutOfRangeException(nameof(rule))
        };
        return $"Rule '{rule.Name}' matched {candidate.Type} in {candidate.Repository} and {effect}.";
    }
}