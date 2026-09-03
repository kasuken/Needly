using Needly.Application.GitHub;
using Needly.Domain;

namespace Needly.Infrastructure.GitHub;

internal sealed class ResolveFeedbackActionDetector : IGitHubActionDetector
{
    public string Key => "github.resolve-feedback.v1";

    public int Order => 200;

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
        var pullRequest = GitHubActionWebhookParser.ToState(
            payload,
            pullRequestPayload,
            context.Event.ReceivedAt,
            context.Event.Action);
        await context.State.UpsertPullRequestAsync(pullRequest, cancellationToken).ConfigureAwait(false);

        return context.Event.EventName switch
        {
            "pull_request" when context.Event.Action == "synchronize" => await RecalculateAsync(
                context,
                pullRequest,
                pullRequest.UpdatedAt,
                cancellationToken).ConfigureAwait(false),
            "pull_request" when context.Event.Action == "closed" =>
                ResolveAuthor(context, pullRequest, pullRequestPayload.MergedAt ?? pullRequestPayload.ClosedAt ?? pullRequest.UpdatedAt),
            "pull_request_review" => await HandleReviewAsync(
                context,
                pullRequest,
                payload,
                cancellationToken).ConfigureAwait(false),
            "pull_request_review_comment" => await HandleCommentAsync(
                context,
                pullRequest,
                payload,
                cancellationToken).ConfigureAwait(false),
            _ => []
        };
    }

    private static async Task<IReadOnlyList<GitHubActionOperation>> HandleReviewAsync(
        GitHubActionDetectionContext context,
        GitHubPullRequestState pullRequest,
        GitHubActionWebhookPayload payload,
        CancellationToken cancellationToken)
    {
        var review = payload.Review;
        if (review is null || review.Id <= 0 || review.User.Id <= 0 || string.IsNullOrWhiteSpace(review.User.Login))
        {
            throw new System.Text.Json.JsonException("The GitHub pull_request_review payload was missing review identity fields.");
        }

        var occurredAt = review.SubmittedAt ?? pullRequest.UpdatedAt;
        var existing = (await context.State.GetReviewerFeedbackAsync(
            pullRequest.PullRequestNumber,
            cancellationToken).ConfigureAwait(false))
            .SingleOrDefault(state => state.ReviewerGitHubUserId == review.User.Id);
        var changesRequested = context.Event.Action == "submitted" &&
            string.Equals(review.State, "changes_requested", StringComparison.OrdinalIgnoreCase);
        var clearsFeedback = context.Event.Action == "dismissed" ||
            (context.Event.Action == "submitted" &&
             string.Equals(review.State, "approved", StringComparison.OrdinalIgnoreCase));
        if (!changesRequested && !clearsFeedback)
        {
            return [];
        }

        await context.State.UpsertReviewerFeedbackAsync(
            new GitHubReviewerFeedbackState(
                pullRequest.PullRequestNumber,
                review.User.Id,
                review.User.Login,
                review.Id,
                changesRequested,
                changesRequested ? existing?.ApproximateUnresolvedCommentCount ?? 0 : 0,
                occurredAt),
            cancellationToken).ConfigureAwait(false);
        return await RecalculateAsync(context, pullRequest, occurredAt, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<GitHubActionOperation>> HandleCommentAsync(
        GitHubActionDetectionContext context,
        GitHubPullRequestState pullRequest,
        GitHubActionWebhookPayload payload,
        CancellationToken cancellationToken)
    {
        var comment = payload.Comment;
        if (comment is null || comment.Id <= 0 || comment.User.Id <= 0 || string.IsNullOrWhiteSpace(comment.User.Login))
        {
            throw new System.Text.Json.JsonException(
                "The GitHub pull_request_review_comment payload was missing comment identity fields.");
        }

        var feedbackStates = await context.State.GetReviewerFeedbackAsync(
            pullRequest.PullRequestNumber,
            cancellationToken).ConfigureAwait(false);
        var feedback = comment.PullRequestReviewId is { } reviewId
            ? feedbackStates.SingleOrDefault(state => state.ReviewId == reviewId)
            : null;
        feedback ??= feedbackStates.SingleOrDefault(state => state.ReviewerGitHubUserId == comment.User.Id);
        if (feedback is null || context.Event.Action is not ("created" or "deleted"))
        {
            return [];
        }

        var count = context.Event.Action == "created"
            ? feedback.ApproximateUnresolvedCommentCount + 1
            : Math.Max(0, feedback.ApproximateUnresolvedCommentCount - 1);
        var occurredAt = comment.UpdatedAt ?? comment.CreatedAt ?? context.Event.ReceivedAt;
        await context.State.UpsertReviewerFeedbackAsync(
            feedback with { ApproximateUnresolvedCommentCount = count, UpdatedAt = occurredAt },
            cancellationToken).ConfigureAwait(false);
        return await RecalculateAsync(context, pullRequest, occurredAt, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<GitHubActionOperation>> RecalculateAsync(
        GitHubActionDetectionContext context,
        GitHubPullRequestState pullRequest,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var author = context.Identities.SingleOrDefault(identity =>
            identity.Type == ActionAssigneeType.User &&
            identity.GitHubId == pullRequest.AuthorGitHubUserId);
        if (author is null)
        {
            return [];
        }

        var target = CreateTarget(pullRequest.PullRequestNumber, author.GitHubId);
        var outstanding = (await context.State.GetReviewerFeedbackAsync(
            pullRequest.PullRequestNumber,
            cancellationToken).ConfigureAwait(false))
            .Where(state => state.HasOutstandingChanges)
            .OrderBy(state => state.ReviewerLogin, StringComparer.OrdinalIgnoreCase)
            .ThenBy(state => state.ReviewerGitHubUserId)
            .ToArray();
        if (outstanding.Length == 0)
        {
            return [new ResolveGitHubActionOperation(target, occurredAt)];
        }

        var reviewerNames = string.Join(", ", outstanding.Select(state => $"@{state.ReviewerLogin}"));
        var commentCount = outstanding.Sum(state => state.ApproximateUnresolvedCommentCount);
        var reviewerLabel = outstanding.Length == 1 ? "reviewer" : "reviewers";
        var commentLabel = commentCount == 1 ? "comment" : "comments";
        return
        [
            new CreateGitHubActionOperation(
                target,
                pullRequest.Url,
                $"Resolve feedback on PR #{pullRequest.PullRequestNumber}: {pullRequest.Title}",
                $"Changes requested by {reviewerNames} ({outstanding.Length} {reviewerLabel}; approximately {commentCount} unresolved review {commentLabel}).",
                "The pull request has outstanding requested changes.",
                occurredAt,
                ReactivateTerminal: true,
                Significance: ActionEventSignificance.Significant)
        ];
    }

    private static IReadOnlyList<GitHubActionOperation> ResolveAuthor(
        GitHubActionDetectionContext context,
        GitHubPullRequestState pullRequest,
        DateTimeOffset occurredAt)
    {
        var author = context.Identities.SingleOrDefault(identity =>
            identity.Type == ActionAssigneeType.User && identity.GitHubId == pullRequest.AuthorGitHubUserId);
        return author is null
            ? []
            : [new ResolveGitHubActionOperation(CreateTarget(pullRequest.PullRequestNumber, author.GitHubId), occurredAt)];
    }

    private static GitHubActionTarget CreateTarget(int pullRequestNumber, long authorGitHubUserId) =>
        new(
            ActionType.Resolve,
            GitHubSubjectType.PullRequest,
            pullRequestNumber,
            ActionAssigneeType.User,
            authorGitHubUserId);
}