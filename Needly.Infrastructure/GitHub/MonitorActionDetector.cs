using Needly.Application.GitHub;
using Needly.Domain;

namespace Needly.Infrastructure.GitHub;

/// <summary>
/// Auto-resolves Monitor actions once the watched pull request reaches a final state. Monitor
/// actions are created elsewhere (a future user opt-in affordance); this detector only retires
/// them once one of the two GitHub-deprecated-thread-subscription use cases is satisfied:
/// <list type="bullet">
/// <item><description>the pull request closes (merged or abandoned), or</description></item>
/// <item><description>
/// the pull request's current head has no known-failing checks after a check run, check suite, or
/// workflow run completes. This reuses the durable check-failure state already recalculated by
/// <see cref="CiFailureActionDetector"/> (<see cref="Order"/> runs after it), so "green" means no
/// currently known failing check for the head — the same bar that detector already uses to resolve
/// its own Fix action.
/// </description></item>
/// </list>
/// </summary>
internal sealed class MonitorActionDetector : IGitHubActionDetector
{
    public string Key => "github.monitor.v1";

    public int Order => 450;

    public async Task<IReadOnlyList<GitHubActionOperation>> DetectAsync(
        GitHubActionDetectionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Event.EventName == "pull_request")
        {
            return await HandlePullRequestAsync(context, cancellationToken).ConfigureAwait(false);
        }

        if (context.Event.EventName is not ("check_suite" or "check_run" or "workflow_run"))
        {
            return [];
        }

        return await HandleCheckCompletionAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private static Task<IReadOnlyList<GitHubActionOperation>> HandlePullRequestAsync(
        GitHubActionDetectionContext context,
        CancellationToken cancellationToken)
    {
        if (context.Event.Action != "closed")
        {
            return Task.FromResult<IReadOnlyList<GitHubActionOperation>>([]);
        }

        var payload = GitHubActionWebhookParser.Parse(context);
        var pullRequest = GitHubActionWebhookParser.RequirePullRequest(payload);
        var pullRequestNumber = payload.Number > 0 ? payload.Number : pullRequest.Number;
        var occurredAt = pullRequest.MergedAt ?? pullRequest.ClosedAt ?? context.Event.ReceivedAt;
        return Task.FromResult(ResolveOpen(context, pullRequestNumber, occurredAt));
    }

    private static async Task<IReadOnlyList<GitHubActionOperation>> HandleCheckCompletionAsync(
        GitHubActionDetectionContext context,
        CancellationToken cancellationToken)
    {
        var payload = GitHubActionWebhookParser.Parse(context);
        var check = Normalize(context, payload);
        if (check is null || !string.Equals(check.Status, "completed", StringComparison.OrdinalIgnoreCase) ||
            check.PullRequestNumbers.Count == 0)
        {
            return [];
        }

        var operations = new List<GitHubActionOperation>();
        foreach (var pullRequestNumber in check.PullRequestNumbers.Distinct().Order())
        {
            var pullRequest = await context.State.GetPullRequestAsync(pullRequestNumber, cancellationToken)
                .ConfigureAwait(false);
            if (pullRequest is null ||
                !string.Equals(pullRequest.HeadSha, check.HeadSha, StringComparison.Ordinal))
            {
                continue;
            }

            var failures = await context.State.GetCheckFailuresAsync(pullRequestNumber, cancellationToken)
                .ConfigureAwait(false);
            var isGreen = failures
                .Where(failure => string.Equals(failure.HeadSha, check.HeadSha, StringComparison.Ordinal))
                .All(failure => !failure.IsFailing);
            if (isGreen)
            {
                operations.AddRange(ResolveOpen(context, pullRequestNumber, context.Event.ReceivedAt));
            }
        }

        return operations;
    }

    private static IReadOnlyList<GitHubActionOperation> ResolveOpen(
        GitHubActionDetectionContext context,
        int pullRequestNumber,
        DateTimeOffset occurredAt) =>
        context.Actions
            .Where(action => action.Target.Type == ActionType.Monitor &&
                action.Target.SubjectType == GitHubSubjectType.PullRequest &&
                action.Target.SubjectNumber == pullRequestNumber &&
                action.State is ActionState.Open or ActionState.Snoozed)
            .Select(action => (GitHubActionOperation)new ResolveGitHubActionOperation(action.Target, occurredAt))
            .ToArray();

    private static NormalizedCheck? Normalize(
        GitHubActionDetectionContext context,
        GitHubActionWebhookPayload payload) =>
        context.Event.EventName switch
        {
            "check_suite" when payload.CheckSuite is { } suite => new NormalizedCheck(
                suite.HeadSha,
                suite.Status,
                suite.PullRequests?.Select(pullRequest => pullRequest.Number).ToArray() ?? []),
            "check_run" when payload.CheckRun is { } run => new NormalizedCheck(
                run.CheckSuite.HeadSha,
                run.Status,
                (run.PullRequests?.Count > 0 ? run.PullRequests : run.CheckSuite.PullRequests)?
                    .Select(pullRequest => pullRequest.Number).ToArray() ?? []),
            "workflow_run" when payload.WorkflowRun is { } workflow => new NormalizedCheck(
                workflow.HeadSha,
                workflow.Status,
                workflow.PullRequests?.Select(pullRequest => pullRequest.Number).ToArray() ?? []),
            _ => null
        };

    private sealed record NormalizedCheck(string HeadSha, string? Status, IReadOnlyList<int> PullRequestNumbers);
}
