using Microsoft.Extensions.Options;
using Needly.Application.GitHub;
using Needly.Domain;

namespace Needly.Infrastructure.GitHub;

internal sealed class MergeReadyActionDetector(
    IGitHubPullRequestLookup pullRequestLookup,
    IOptions<GitHubActionOptions> options) : IGitHubActionDetector
{
    private readonly GitHubActionOptions options = options.Value;

    public string Key => "github.merge-ready.v1";

    public int Order => 400;

    public async Task<IReadOnlyList<GitHubActionOperation>> DetectAsync(
        GitHubActionDetectionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Event.EventName is not ("pull_request" or "pull_request_review" or "pull_request_review_comment" or
            "check_suite" or "check_run" or "workflow_run") &&
            context.Event.EventName != GitHubHistoricalEventNames.PullRequest &&
            context.Event.EventName != GitHubHistoricalEventNames.PullRequestReview &&
            context.Event.EventName != GitHubHistoricalEventNames.PullRequestReviewComment &&
            context.Event.EventName != GitHubHistoricalEventNames.CheckRun)
        {
            return [];
        }

        var pullRequestNumbers = await GetPullRequestNumbersAsync(context, cancellationToken).ConfigureAwait(false);
        var operations = new List<GitHubActionOperation>();
        foreach (var pullRequestNumber in pullRequestNumbers)
        {
            var existing = await context.State.GetPullRequestAsync(pullRequestNumber, cancellationToken).ConfigureAwait(false);
            if (context.Event.EventName is "pull_request" or GitHubHistoricalEventNames.PullRequest &&
                context.Event.Action == "closed")
            {
                operations.AddRange(ResolveExisting(context, pullRequestNumber, context.Event.ReceivedAt));
                continue;
            }

            var readiness = await pullRequestLookup.GetAsync(
                context.Installation.GitHubInstallationId,
                context.Repository.Owner,
                context.Repository.Name,
                pullRequestNumber,
                cancellationToken).ConfigureAwait(false);
            if (readiness is null)
            {
                operations.AddRange(ResolveExisting(context, pullRequestNumber, context.Event.ReceivedAt));
                continue;
            }

            var author = context.Identities.SingleOrDefault(identity =>
                identity.Type == ActionAssigneeType.User && identity.GitHubId == readiness.AuthorGitHubUserId);
            if (author is null)
            {
                continue;
            }

            await context.State.UpsertPullRequestAsync(
                new GitHubPullRequestState(
                    readiness.PullRequestNumber,
                    readiness.AuthorGitHubUserId,
                    readiness.AuthorLogin,
                    readiness.HeadSha,
                    readiness.Title,
                    readiness.Url,
                    readiness.IsDraft,
                    readiness.ObservedAt,
                    readiness.IsOpen,
                    readiness.ApprovalCount,
                    readiness.HasChangesRequested,
                    readiness.CheckState,
                    readiness.IsMergeable,
                    readiness.HasConflicts,
                    context.Event.ReceivedAt),
                cancellationToken).ConfigureAwait(false);
            var occurredAt = readiness.ObservedAt > context.Event.ReceivedAt
                ? readiness.ObservedAt
                : context.Event.ReceivedAt;
            var target = CreateTarget(readiness.PullRequestNumber, readiness.AuthorGitHubUserId);

            // A pull request an author opened in their own personal namespace, with nobody asked to
            // review it and no review submitted, has no reviewer who could ever supply an approval.
            // Holding it below RequiredApprovals would keep it permanently invisible rather than
            // merely unapproved, so the approval requirement does not apply to it. Requesting a
            // reviewer, or any submitted review, puts the configured requirement back in force.
            var isUnreviewable = RepositoryOwnership.IsSelfOwned(context.Repository.Owner, readiness.AuthorLogin) &&
                readiness.RequestedReviewerCount == 0 && readiness.ReviewCount == 0;
            var requiredApprovals = isUnreviewable ? 0 : options.RequiredApprovals;

            // Unknown means the head commit reported no statuses and no check runs at all, which is
            // what a repository without CI looks like — that is not a failure, and treating it as one
            // made merge readiness unreachable for every such repository.
            var checksBlock = readiness.CheckState is GitHubCheckState.Pending or GitHubCheckState.Failing;
            if (!readiness.IsOpen || readiness.IsDraft || readiness.ApprovalCount < requiredApprovals ||
                readiness.HasChangesRequested || checksBlock ||
                readiness.IsMergeable != true || readiness.HasConflicts)
            {
                operations.Add(new ResolveGitHubActionOperation(target, occurredAt));
                continue;
            }

            var approvalSummary = isUnreviewable
                ? "No review requested; you own the repository"
                : $"{readiness.ApprovalCount} approval(s)";
            var checkSummary = readiness.CheckState == GitHubCheckState.Passing
                ? "all latest-head checks passed."
                : "no checks are configured for the head commit.";
            var reason = isUnreviewable
                ? "The pull request is mergeable without conflicts and nobody else can review it."
                : "The pull request is approved, green, and mergeable without conflicts.";
            operations.Add(new CreateGitHubActionOperation(
                target,
                readiness.Url,
                $"Merge PR #{readiness.PullRequestNumber}: {readiness.Title}",
                $"{approvalSummary}; {checkSummary}",
                reason,
                occurredAt,
                ReactivateTerminal: true,
                Significance: ActionEventSignificance.Significant));
        }

        return operations;
    }

    private static async Task<IReadOnlyList<int>> GetPullRequestNumbersAsync(
        GitHubActionDetectionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = GitHubActionWebhookParser.Parse(context);
        if (payload.PullRequest is not null)
        {
            var pullRequest = GitHubActionWebhookParser.ToState(
                payload,
                payload.PullRequest,
                context.Event.ReceivedAt,
                context.Event.Action);
            return [pullRequest.PullRequestNumber];
        }

        return context.Event.EventName switch
        {
            "check_suite" => payload.CheckSuite?.PullRequests?.Select(item => item.Number).Distinct().ToArray() ?? [],
            "check_run" or GitHubHistoricalEventNames.CheckRun => (payload.CheckRun?.PullRequests?.Count > 0
                    ? payload.CheckRun.PullRequests
                    : payload.CheckRun?.CheckSuite.PullRequests)?
                .Select(item => item.Number).Distinct().ToArray() ?? [],
            "workflow_run" => payload.WorkflowRun?.PullRequests?.Select(item => item.Number).Distinct().ToArray() ?? [],
            _ => []
        };
    }

    private static IReadOnlyList<GitHubActionOperation> ResolveExisting(
        GitHubActionDetectionContext context,
        int pullRequestNumber,
        DateTimeOffset occurredAt) =>
        context.Actions
            .Where(action => action.Target.Type == ActionType.Merge &&
                action.Target.SubjectType == GitHubSubjectType.PullRequest &&
                action.Target.SubjectNumber == pullRequestNumber &&
                action.State is ActionState.Open or ActionState.Snoozed)
            .Select(action => (GitHubActionOperation)new ResolveGitHubActionOperation(action.Target, occurredAt))
            .ToArray();

    private static GitHubActionTarget CreateTarget(int pullRequestNumber, long authorGitHubUserId) =>
        new(
            ActionType.Merge,
            GitHubSubjectType.PullRequest,
            pullRequestNumber,
            ActionAssigneeType.User,
            authorGitHubUserId);
}