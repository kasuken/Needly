using Needly.Application.GitHub;
using Needly.Domain;

namespace Needly.Infrastructure.GitHub;

internal sealed class ReviewRequestedActionDetector : IGitHubActionDetector
{
    public string Key => "github.review-requested.v1";

    public int Order => 100;

    public async Task<IReadOnlyList<GitHubActionOperation>> DetectAsync(
        GitHubActionDetectionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Event.EventName is not ("pull_request" or "pull_request_review") &&
            context.Event.EventName != GitHubHistoricalEventNames.PullRequest &&
            context.Event.EventName != GitHubHistoricalEventNames.PullRequestReview)
        {
            return [];
        }

        var payload = GitHubActionWebhookParser.Parse(context);
        var pullRequest = GitHubActionWebhookParser.RequirePullRequest(payload);
        var pullRequestState = GitHubActionWebhookParser.ToState(
            payload,
            pullRequest,
            context.Event.ReceivedAt,
            context.Event.Action);
        await context.State.UpsertPullRequestAsync(pullRequestState, cancellationToken).ConfigureAwait(false);
        var occurredAt = payload.Review?.SubmittedAt ?? pullRequest.UpdatedAt ?? context.Event.ReceivedAt;

        if (context.Event.EventName is "pull_request_review" ||
            context.Event.EventName == GitHubHistoricalEventNames.PullRequestReview)
        {
            return context.Event.Action == "submitted" && payload.Review is not null
                ? await ResolveSubmittedReviewAsync(context, pullRequestState, payload.Review.User.Id, occurredAt, cancellationToken)
                    .ConfigureAwait(false)
                : [];
        }

        return context.Event.Action switch
        {
            "review_requested" when !pullRequest.Draft => await RequestSingleAsync(
                context,
                pullRequestState,
                payload.RequestedReviewer,
                payload.RequestedTeam,
                occurredAt,
                cancellationToken).ConfigureAwait(false),
            "ready_for_review" => await RequestCurrentAsync(
                context,
                pullRequestState,
                pullRequest.RequestedReviewers ?? [],
                pullRequest.RequestedTeams ?? [],
                occurredAt,
                cancellationToken).ConfigureAwait(false),
            "review_request_removed" => await RemoveSingleAsync(
                context,
                pullRequestState,
                payload.RequestedReviewer,
                payload.RequestedTeam,
                occurredAt,
                cancellationToken).ConfigureAwait(false),
            "closed" => await ResolveAllAsync(context, pullRequestState.PullRequestNumber, occurredAt, cancellationToken)
                .ConfigureAwait(false),
            _ => []
        };
    }

    private static async Task<IReadOnlyList<GitHubActionOperation>> RequestSingleAsync(
        GitHubActionDetectionContext context,
        GitHubPullRequestState pullRequest,
        GitHubActionUserPayload? reviewer,
        GitHubActionTeamPayload? team,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var identity = reviewer is not null
            ? FindIdentity(context, ActionAssigneeType.User, reviewer.Id)
            : team is not null
                ? FindIdentity(context, ActionAssigneeType.Team, team.Id)
                : null;
        if (identity is null)
        {
            return [];
        }

        await SetRequestStateAsync(context, pullRequest.PullRequestNumber, identity, true, occurredAt, cancellationToken)
            .ConfigureAwait(false);
        return [CreateReviewOperation(pullRequest, identity, occurredAt)];
    }

    private static async Task<IReadOnlyList<GitHubActionOperation>> RequestCurrentAsync(
        GitHubActionDetectionContext context,
        GitHubPullRequestState pullRequest,
        IReadOnlyList<GitHubActionUserPayload> reviewers,
        IReadOnlyList<GitHubActionTeamPayload> teams,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var identities = reviewers
            .Select(reviewer => FindIdentity(context, ActionAssigneeType.User, reviewer.Id))
            .Concat(teams.Select(team => FindIdentity(context, ActionAssigneeType.Team, team.Id)))
            .Where(identity => identity is not null)
            .Cast<GitHubActionIdentity>()
            .DistinctBy(identity => new { identity.Type, identity.GitHubId })
            .ToArray();
        foreach (var identity in identities)
        {
            await SetRequestStateAsync(context, pullRequest.PullRequestNumber, identity, true, occurredAt, cancellationToken)
                .ConfigureAwait(false);
        }

        return identities.Select(identity => CreateReviewOperation(pullRequest, identity, occurredAt)).ToArray();
    }

    private static async Task<IReadOnlyList<GitHubActionOperation>> RemoveSingleAsync(
        GitHubActionDetectionContext context,
        GitHubPullRequestState pullRequest,
        GitHubActionUserPayload? reviewer,
        GitHubActionTeamPayload? team,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var identity = reviewer is not null
            ? FindIdentity(context, ActionAssigneeType.User, reviewer.Id)
            : team is not null
                ? FindIdentity(context, ActionAssigneeType.Team, team.Id)
                : null;
        if (identity is null)
        {
            return [];
        }

        await SetRequestStateAsync(context, pullRequest.PullRequestNumber, identity, false, occurredAt, cancellationToken)
            .ConfigureAwait(false);
        return [new ResolveGitHubActionOperation(CreateTarget(pullRequest.PullRequestNumber, identity), occurredAt)];
    }

    private static async Task<IReadOnlyList<GitHubActionOperation>> ResolveSubmittedReviewAsync(
        GitHubActionDetectionContext context,
        GitHubPullRequestState pullRequest,
        long reviewerGitHubUserId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var requests = await context.State.GetReviewRequestsAsync(pullRequest.PullRequestNumber, cancellationToken)
            .ConfigureAwait(false);
        var identities = requests
            .Where(request => request.IsRequested)
            .Select(request => FindIdentity(context, request.AssigneeType, request.GitHubAssigneeId))
            .Where(identity => identity is not null &&
                (identity.Type == ActionAssigneeType.User
                    ? identity.GitHubId == reviewerGitHubUserId
                    : identity.MemberGitHubUserIds?.Contains(reviewerGitHubUserId) == true))
            .Cast<GitHubActionIdentity>()
            .ToArray();
        foreach (var identity in identities)
        {
            await SetRequestStateAsync(context, pullRequest.PullRequestNumber, identity, false, occurredAt, cancellationToken)
                .ConfigureAwait(false);
        }

        return identities
            .Select(identity => (GitHubActionOperation)new ResolveGitHubActionOperation(
                CreateTarget(pullRequest.PullRequestNumber, identity),
                occurredAt))
            .ToArray();
    }

    private static async Task<IReadOnlyList<GitHubActionOperation>> ResolveAllAsync(
        GitHubActionDetectionContext context,
        int pullRequestNumber,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var requests = await context.State.GetReviewRequestsAsync(pullRequestNumber, cancellationToken)
            .ConfigureAwait(false);
        foreach (var request in requests.Where(request => request.IsRequested))
        {
            await context.State.UpsertReviewRequestAsync(
                request with { IsRequested = false, UpdatedAt = occurredAt },
                cancellationToken).ConfigureAwait(false);
        }

        return context.Actions
            .Where(action => action.Target.Type == ActionType.Review &&
                action.Target.SubjectType == GitHubSubjectType.PullRequest &&
                action.Target.SubjectNumber == pullRequestNumber &&
                action.State is ActionState.Open or ActionState.Snoozed)
            .Select(action => (GitHubActionOperation)new ResolveGitHubActionOperation(action.Target, occurredAt))
            .ToArray();
    }

    private static async Task SetRequestStateAsync(
        GitHubActionDetectionContext context,
        int pullRequestNumber,
        GitHubActionIdentity identity,
        bool isRequested,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        await context.State.UpsertReviewRequestAsync(
            new GitHubReviewRequestState(
                pullRequestNumber,
                identity.Type,
                identity.GitHubId,
                identity.Login,
                isRequested,
                occurredAt),
            cancellationToken).ConfigureAwait(false);

    private static CreateGitHubActionOperation CreateReviewOperation(
        GitHubPullRequestState pullRequest,
        GitHubActionIdentity identity,
        DateTimeOffset occurredAt) =>
        new(
            CreateTarget(pullRequest.PullRequestNumber, identity),
            pullRequest.Url,
            $"Review PR #{pullRequest.PullRequestNumber}: {pullRequest.Title}",
            identity.Type == ActionAssigneeType.Team
                ? $"Requested from team @{identity.Login}"
                : $"Requested from @{identity.Login}",
            "A pull request review was requested.",
            occurredAt,
            ReactivateTerminal: true,
            Significance: ActionEventSignificance.Significant);

    private static GitHubActionTarget CreateTarget(int pullRequestNumber, GitHubActionIdentity identity) =>
        new(
            ActionType.Review,
            GitHubSubjectType.PullRequest,
            pullRequestNumber,
            identity.Type,
            identity.GitHubId);

    private static GitHubActionIdentity? FindIdentity(
        GitHubActionDetectionContext context,
        ActionAssigneeType type,
        long gitHubId) =>
        context.Identities.SingleOrDefault(identity => identity.Type == type && identity.GitHubId == gitHubId);
}