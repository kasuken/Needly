using Needly.Application.GitHub;
using Needly.Domain;

namespace Needly.Infrastructure.GitHub;

internal sealed class CiFailureActionDetector : IGitHubActionDetector
{
    private static readonly HashSet<string> FailureConclusions = new(StringComparer.OrdinalIgnoreCase)
    {
        "failure",
        "timed_out",
        "cancelled",
        "action_required",
        "stale"
    };

    public string Key => "github.ci-failure.v1";

    public int Order => 300;

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

        var payload = GitHubActionWebhookParser.Parse(context);
        var check = Normalize(context, payload);
        if (check is null || check.PullRequestNumbers.Count == 0 ||
            string.IsNullOrWhiteSpace(check.Conclusion) ||
            !string.Equals(check.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var operations = new List<GitHubActionOperation>();
        foreach (var pullRequestNumber in check.PullRequestNumbers.Distinct().Order())
        {
            var pullRequest = await context.State.GetPullRequestAsync(pullRequestNumber, cancellationToken)
                .ConfigureAwait(false);
            if (pullRequest is null || FindAuthor(context, pullRequest) is null)
            {
                continue;
            }

            await context.State.UpsertCheckFailureAsync(
                new GitHubCheckFailureState(
                    pullRequestNumber,
                    check.HeadSha,
                    check.Key,
                    check.Name,
                    check.Url,
                    FailureConclusions.Contains(check.Conclusion),
                    check.OccurredAt),
                cancellationToken).ConfigureAwait(false);
            if (string.Equals(pullRequest.HeadSha, check.HeadSha, StringComparison.Ordinal))
            {
                operations.AddRange(await RecalculateAsync(
                    context,
                    pullRequest,
                    check.OccurredAt,
                    cancellationToken).ConfigureAwait(false));
            }
        }

        return operations;
    }

    private static async Task<IReadOnlyList<GitHubActionOperation>> HandlePullRequestAsync(
        GitHubActionDetectionContext context,
        CancellationToken cancellationToken)
    {
        if (context.Event.Action is not ("synchronize" or "closed"))
        {
            return [];
        }

        var payload = GitHubActionWebhookParser.Parse(context);
        var pullRequestPayload = GitHubActionWebhookParser.RequirePullRequest(payload);
        var pullRequest = GitHubActionWebhookParser.ToState(
            payload,
            pullRequestPayload,
            context.Event.ReceivedAt,
            context.Event.Action);
        await context.State.UpsertPullRequestAsync(pullRequest, cancellationToken).ConfigureAwait(false);
        if (FindAuthor(context, pullRequest) is null)
        {
            return [];
        }

        var occurredAt = pullRequestPayload.MergedAt ?? pullRequestPayload.ClosedAt ?? pullRequest.UpdatedAt;
        return context.Event.Action == "closed"
            ? [new ResolveGitHubActionOperation(CreateTarget(pullRequest), occurredAt)]
            : await RecalculateAsync(context, pullRequest, occurredAt, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<GitHubActionOperation>> RecalculateAsync(
        GitHubActionDetectionContext context,
        GitHubPullRequestState pullRequest,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var failures = (await context.State.GetCheckFailuresAsync(
            pullRequest.PullRequestNumber,
            cancellationToken).ConfigureAwait(false))
            .Where(state => state.IsFailing && string.Equals(state.HeadSha, pullRequest.HeadSha, StringComparison.Ordinal))
            .OrderBy(state => state.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(state => state.CheckKey, StringComparer.Ordinal)
            .ToArray();
        var target = CreateTarget(pullRequest);
        if (failures.Length == 0)
        {
            return [new ResolveGitHubActionOperation(target, occurredAt)];
        }

        var checks = string.Join(", ", failures.Select(failure =>
            string.IsNullOrWhiteSpace(failure.Url) ? failure.Name : $"[{failure.Name}]({failure.Url})"));
        var checkLabel = failures.Length == 1 ? "check is" : "checks are";
        return
        [
            new CreateGitHubActionOperation(
                target,
                pullRequest.Url,
                $"Fix CI on PR #{pullRequest.PullRequestNumber}: {pullRequest.Title}",
                $"Failing checks for {pullRequest.HeadSha}: {checks}",
                $"{failures.Length} CI {checkLabel} failing for the current head.",
                occurredAt,
                ReactivateTerminal: true,
                Significance: ActionEventSignificance.Significant)
        ];
    }

    private static NormalizedCheck? Normalize(
        GitHubActionDetectionContext context,
        GitHubActionWebhookPayload payload) =>
        context.Event.EventName switch
        {
            "check_suite" when payload.CheckSuite is { } suite => new NormalizedCheck(
                suite.HeadSha,
                $"check-suite:{suite.App?.Name ?? suite.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                suite.App?.Name ?? "Check suite",
                suite.Url,
                suite.Status,
                suite.Conclusion,
                suite.UpdatedAt ?? context.Event.ReceivedAt,
                suite.PullRequests?.Select(pullRequest => pullRequest.Number).ToArray() ?? []),
            "check_run" when payload.CheckRun is { } run => new NormalizedCheck(
                run.CheckSuite.HeadSha,
                $"check-run:{run.Name}",
                run.Name,
                run.DetailsUrl ?? run.HtmlUrl,
                run.Status,
                run.Conclusion,
                run.CompletedAt ?? context.Event.ReceivedAt,
                (run.PullRequests?.Count > 0 ? run.PullRequests : run.CheckSuite.PullRequests)?
                    .Select(pullRequest => pullRequest.Number).ToArray() ?? []),
            "workflow_run" when payload.WorkflowRun is { } workflow => new NormalizedCheck(
                workflow.HeadSha,
                $"workflow:{workflow.Name}",
                workflow.Name,
                workflow.HtmlUrl,
                workflow.Status,
                workflow.Conclusion,
                workflow.UpdatedAt ?? context.Event.ReceivedAt,
                workflow.PullRequests?.Select(pullRequest => pullRequest.Number).ToArray() ?? []),
            _ => null
        };

    private static GitHubActionIdentity? FindAuthor(
        GitHubActionDetectionContext context,
        GitHubPullRequestState pullRequest) =>
        context.Identities.SingleOrDefault(identity =>
            identity.Type == ActionAssigneeType.User &&
            identity.GitHubId == pullRequest.AuthorGitHubUserId);

    private static GitHubActionTarget CreateTarget(GitHubPullRequestState pullRequest) =>
        new(
            ActionType.Fix,
            GitHubSubjectType.PullRequest,
            pullRequest.PullRequestNumber,
            ActionAssigneeType.User,
            pullRequest.AuthorGitHubUserId);

    private sealed record NormalizedCheck(
        string HeadSha,
        string Key,
        string Name,
        string? Url,
        string? Status,
        string? Conclusion,
        DateTimeOffset OccurredAt,
        IReadOnlyList<int> PullRequestNumbers);
}