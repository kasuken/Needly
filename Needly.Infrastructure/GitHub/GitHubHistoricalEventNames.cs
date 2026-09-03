namespace Needly.Infrastructure.GitHub;

internal static class GitHubHistoricalEventNames
{
    internal const string PullRequest = "needly_historical_pull_request";
    internal const string PullRequestReview = "needly_historical_pull_request_review";
    internal const string PullRequestReviewComment = "needly_historical_pull_request_review_comment";
    internal const string IssueComment = "needly_historical_issue_comment";
    internal const string CheckRun = "needly_historical_check_run";
}