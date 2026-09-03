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
            "check_suite" or "check_run" or "workflow_run"))
        {
            return [];
        }

        var pullRequestNumbers = await GetPullRequestNumbersAsync(context, cancellationToken).ConfigureAwait(false);
        var operations = new List<GitHubActionOperation>();
        foreach (var pullRequestNumber in pullRequestNumbers)
        {
            var existing = await context.State.GetPullRequestAsync(pullRequestNumber, cancellationToken).ConfigureAwait(false);
            if (context.Event.EventName == "pull_request" && context.Event.Action == "closed")
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
            if (!readiness.IsOpen || readiness.IsDraft || readiness.ApprovalCount < options.RequiredApprovals ||
                readiness.HasChangesRequested || readiness.CheckState != GitHubCheckState.Passing ||
                readiness.IsMergeable != true || readiness.HasConflicts)
            {
                operations.Add(new ResolveGitHubActionOperation(target, occurredAt));
                continue;
            }

            operations.Add(new CreateGitHubActionOperation(
                target,
                readiness.Url,
                $"Merge PR #{readiness.PullRequestNumber}: {readiness.Title}",
                $"{readiness.ApprovalCount} approval(s); all latest-head checks passed.",
                "The pull request is approved, green, and mergeable without conflicts.",
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
            "check_run" => (payload.CheckRun?.PullRequests?.Count > 0
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