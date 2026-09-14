using Needly.Application.GitHub;
using Needly.Domain;

namespace Needly.Infrastructure.GitHub;

/// <summary>
/// Resolves Follow up actions once new activity relieves the stale requested-changes feedback that
/// caused them. Follow up actions themselves are created by the periodic <c>FollowUpEvaluator</c>
/// (see <c>Needly.Infrastructure.Actions.FollowUpEvaluator</c>) because "unanswered past a
/// configurable threshold" cannot be decided from a single webhook event; this detector only
/// reacts to activity that resolves that staleness.
/// </summary>
internal sealed class FollowUpActionDetector : IGitHubActionDetector
{
    public string Key => "github.follow-up.v1";

    public int Order => 225;

    public async Task<IReadOnlyList<GitHubActionOperation>> DetectAsync(
        GitHubActionDetectionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Event.EventName is not ("pull_request" or "pull_request_review" or "pull_request_review_comment"))
        {
            return [];
        }

        var payload = GitHubActionWebhookParser.Parse(context);
        var pullRequestPayload = GitHubActionWebhookParser.RequirePullRequest(payload);
        var pullRequestNumber = payload.Number > 0 ? payload.Number : pullRequestPayload.Number;

        if (context.Event.EventName == "pull_request" && context.Event.Action == "closed")
        {
            var occurredAt = pullRequestPayload.MergedAt ?? pullRequestPayload.ClosedAt ?? context.Event.ReceivedAt;
            return ResolveAll(context, pullRequestNumber, occurredAt);
        }

        var isRelevant = context.Event.EventName switch
        {
            "pull_request" => context.Event.Action == "synchronize",
            "pull_request_review" => context.Event.Action is "submitted" or "dismissed",
            "pull_request_review_comment" => context.Event.Action is "created" or "deleted",
            _ => false
        };
        if (!isRelevant)
        {
            return [];
        }

        var updatedAt = pullRequestPayload.UpdatedAt ?? context.Event.ReceivedAt;
        var outstandingReviewerIds = (await context.State.GetReviewerFeedbackAsync(pullRequestNumber, cancellationToken)
                .ConfigureAwait(false))
            .Where(state => state.HasOutstandingChanges)
            .Select(state => state.ReviewerGitHubUserId)
            .ToHashSet();

        return context.Actions
            .Where(action => action.Target.Type == ActionType.FollowUp &&
                action.Target.SubjectType == GitHubSubjectType.PullRequest &&
                action.Target.SubjectNumber == pullRequestNumber &&
                action.State is ActionState.Open or ActionState.Snoozed &&
                !outstandingReviewerIds.Contains(action.Target.GitHubAssigneeId))
            .Select(action => (GitHubActionOperation)new ResolveGitHubActionOperation(action.Target, updatedAt))
            .ToArray();
    }

    private static IReadOnlyList<GitHubActionOperation> ResolveAll(
        GitHubActionDetectionContext context,
        int pullRequestNumber,
        DateTimeOffset occurredAt) =>
        context.Actions
            .Where(action => action.Target.Type == ActionType.FollowUp &&
                action.Target.SubjectType == GitHubSubjectType.PullRequest &&
                action.Target.SubjectNumber == pullRequestNumber &&
                action.State is ActionState.Open or ActionState.Snoozed)
            .Select(action => (GitHubActionOperation)new ResolveGitHubActionOperation(action.Target, occurredAt))
            .ToArray();
}
