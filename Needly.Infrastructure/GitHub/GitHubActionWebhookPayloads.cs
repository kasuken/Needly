using System.Text.Json;
using System.Text.Json.Serialization;
using Needly.Application.GitHub;

namespace Needly.Infrastructure.GitHub;

internal static class GitHubActionWebhookParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static GitHubActionWebhookPayload Parse(GitHubActionDetectionContext context) =>
        JsonSerializer.Deserialize<GitHubActionWebhookPayload>(context.Event.PayloadJson, JsonOptions)
        ?? throw new JsonException($"The GitHub {context.Event.EventName} payload was empty.");

    internal static GitHubPullRequestPayload RequirePullRequest(GitHubActionWebhookPayload payload) =>
        payload.PullRequest ?? throw new JsonException("The GitHub action payload did not contain pull_request.");

    internal static GitHubPullRequestState ToState(
        GitHubActionWebhookPayload payload,
        GitHubPullRequestPayload pullRequest,
        DateTimeOffset fallback,
        string? eventAction)
    {
        var pullRequestNumber = payload.Number > 0 ? payload.Number : pullRequest.Number;
        if (pullRequestNumber <= 0 || pullRequest.User is not { Id: > 0 } ||
            string.IsNullOrWhiteSpace(pullRequest.User.Login) || string.IsNullOrWhiteSpace(pullRequest.Head?.Sha) ||
            string.IsNullOrWhiteSpace(pullRequest.Title) || string.IsNullOrWhiteSpace(pullRequest.HtmlUrl))
        {
            throw new JsonException("The GitHub pull_request payload was missing required identity fields.");
        }

        return new GitHubPullRequestState(
            pullRequestNumber,
            pullRequest.User.Id,
            pullRequest.User.Login,
            pullRequest.Head.Sha,
            pullRequest.Title,
            pullRequest.HtmlUrl,
            pullRequest.Draft,
            pullRequest.UpdatedAt ?? fallback,
            IsOpen: !pullRequest.Merged && !string.Equals(eventAction, "closed", StringComparison.Ordinal));
    }
}

internal sealed record GitHubActionWebhookPayload(
    [property: JsonPropertyName("action")] string? Action,
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("pull_request")] GitHubPullRequestPayload? PullRequest,
    [property: JsonPropertyName("issue")] GitHubIssuePayload? Issue,
    [property: JsonPropertyName("requested_reviewer")] GitHubActionUserPayload? RequestedReviewer,
    [property: JsonPropertyName("requested_team")] GitHubActionTeamPayload? RequestedTeam,
    [property: JsonPropertyName("review")] GitHubReviewPayload? Review,
    [property: JsonPropertyName("comment")] GitHubReviewCommentPayload? Comment,
    [property: JsonPropertyName("check_suite")] GitHubCheckSuitePayload? CheckSuite,
    [property: JsonPropertyName("check_run")] GitHubCheckRunPayload? CheckRun,
    [property: JsonPropertyName("workflow_run")] GitHubWorkflowRunPayload? WorkflowRun,
    [property: JsonPropertyName("sender")] GitHubActionUserPayload? Sender);

internal sealed record GitHubPullRequestPayload(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("merged")] bool Merged,
    [property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt,
    [property: JsonPropertyName("closed_at")] DateTimeOffset? ClosedAt,
    [property: JsonPropertyName("merged_at")] DateTimeOffset? MergedAt,
    [property: JsonPropertyName("user")] GitHubActionUserPayload User,
    [property: JsonPropertyName("head")] GitHubPullRequestHeadPayload Head,
    [property: JsonPropertyName("requested_reviewers")] IReadOnlyList<GitHubActionUserPayload>? RequestedReviewers,
    [property: JsonPropertyName("requested_teams")] IReadOnlyList<GitHubActionTeamPayload>? RequestedTeams);

internal sealed record GitHubPullRequestHeadPayload(
    [property: JsonPropertyName("sha")] string Sha);

internal sealed record GitHubActionUserPayload(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("type")] string? Type = null);

internal sealed record GitHubActionTeamPayload(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("name")] string? Name);

internal sealed record GitHubReviewPayload(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("user")] GitHubActionUserPayload User,
    [property: JsonPropertyName("submitted_at")] DateTimeOffset? SubmittedAt,
    [property: JsonPropertyName("html_url")] string? HtmlUrl);

internal sealed record GitHubReviewCommentPayload(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("user")] GitHubActionUserPayload User,
    [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt,
    [property: JsonPropertyName("pull_request_review_id")] long? PullRequestReviewId,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("html_url")] string? HtmlUrl);

internal sealed record GitHubIssuePayload(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("user")] GitHubActionUserPayload User,
    [property: JsonPropertyName("pull_request")] JsonElement? PullRequest);

internal sealed record GitHubAssociatedPullRequestPayload(
    [property: JsonPropertyName("number")] int Number);

internal sealed record GitHubCheckAppPayload(
    [property: JsonPropertyName("name")] string Name);

internal sealed record GitHubCheckSuitePayload(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("head_sha")] string HeadSha,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("conclusion")] string? Conclusion,
    [property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("app")] GitHubCheckAppPayload? App,
    [property: JsonPropertyName("pull_requests")] IReadOnlyList<GitHubAssociatedPullRequestPayload>? PullRequests);

internal sealed record GitHubCheckRunPayload(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("conclusion")] string? Conclusion,
    [property: JsonPropertyName("completed_at")] DateTimeOffset? CompletedAt,
    [property: JsonPropertyName("details_url")] string? DetailsUrl,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("check_suite")] GitHubCheckSuitePayload CheckSuite,
    [property: JsonPropertyName("pull_requests")] IReadOnlyList<GitHubAssociatedPullRequestPayload>? PullRequests);

internal sealed record GitHubWorkflowRunPayload(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("display_title")] string? DisplayTitle,
    [property: JsonPropertyName("head_sha")] string HeadSha,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("conclusion")] string? Conclusion,
    [property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("pull_requests")] IReadOnlyList<GitHubAssociatedPullRequestPayload>? PullRequests);