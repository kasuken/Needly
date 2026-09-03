using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Needly.Application.GitHub;

namespace Needly.Infrastructure.GitHub;

/// <summary>Loads merge readiness through an installation-authenticated GitHub API client.</summary>
public sealed class GitHubPullRequestLookup(IGitHubApiClientFactory clientFactory) : IGitHubPullRequestLookup
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<GitHubPullRequestReadiness?> GetAsync(
        long gitHubInstallationId,
        string repositoryOwner,
        string repositoryName,
        int pullRequestNumber,
        CancellationToken cancellationToken)
    {
        var client = await clientFactory.CreateAsync(gitHubInstallationId, cancellationToken).ConfigureAwait(false);
        var repositoryPath = $"repos/{Uri.EscapeDataString(repositoryOwner)}/{Uri.EscapeDataString(repositoryName)}";
        var pullRequest = await GetAsync<PullRequestResponse>(
            client,
            $"{repositoryPath}/pulls/{pullRequestNumber}",
            cancellationToken).ConfigureAwait(false);
        if (pullRequest is null)
        {
            return null;
        }

        var reviews = await GetAsync<IReadOnlyList<ReviewResponse>>(
            client,
            $"{repositoryPath}/pulls/{pullRequestNumber}/reviews?per_page=100",
            cancellationToken).ConfigureAwait(false);
        var status = await GetAsync<CombinedStatusResponse>(
            client,
            $"{repositoryPath}/commits/{Uri.EscapeDataString(pullRequest.Head.Sha)}/status?per_page=100",
            cancellationToken).ConfigureAwait(false);
        var checkRuns = await GetAsync<CheckRunsResponse>(
            client,
            $"{repositoryPath}/commits/{Uri.EscapeDataString(pullRequest.Head.Sha)}/check-runs?per_page=100",
            cancellationToken).ConfigureAwait(false);
        if (reviews is null || status is null || checkRuns is null)
        {
            return null;
        }

        var latestReviews = reviews
            .Where(review => review.User.Id > 0 && review.SubmittedAt is not null)
            .GroupBy(review => review.User.Id)
            .Select(group => group.OrderByDescending(review => review.SubmittedAt).ThenByDescending(review => review.Id).First())
            .ToArray();
        var checkState = GetCheckState(status, checkRuns);
        var hasConflicts = string.Equals(pullRequest.MergeableState, "dirty", StringComparison.OrdinalIgnoreCase) ||
            pullRequest.Mergeable == false;

        return new GitHubPullRequestReadiness(
            pullRequest.Number,
            pullRequest.User.Id,
            pullRequest.User.Login,
            pullRequest.Head.Sha,
            pullRequest.Title,
            pullRequest.HtmlUrl,
            string.Equals(pullRequest.State, "open", StringComparison.OrdinalIgnoreCase),
            pullRequest.Draft,
            latestReviews.Count(review => string.Equals(review.State, "approved", StringComparison.OrdinalIgnoreCase)),
            latestReviews.Any(review => string.Equals(review.State, "changes_requested", StringComparison.OrdinalIgnoreCase)),
            checkState,
            pullRequest.Mergeable,
            hasConflicts,
            pullRequest.UpdatedAt);
    }

    private static GitHubCheckState GetCheckState(CombinedStatusResponse status, CheckRunsResponse checkRuns)
    {
        if (status.Statuses.Count == 0 && checkRuns.CheckRuns.Count == 0)
        {
            return GitHubCheckState.Unknown;
        }

        if (checkRuns.CheckRuns.Any(check => !string.Equals(check.Status, "completed", StringComparison.OrdinalIgnoreCase)) ||
            (status.Statuses.Count > 0 && string.Equals(status.State, "pending", StringComparison.OrdinalIgnoreCase)))
        {
            return GitHubCheckState.Pending;
        }

        var acceptedConclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "success", "neutral", "skipped"
        };
        return (status.Statuses.Count > 0 &&
            (string.Equals(status.State, "failure", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(status.State, "error", StringComparison.OrdinalIgnoreCase))) ||
            checkRuns.CheckRuns.Any(check => check.Conclusion is null || !acceptedConclusions.Contains(check.Conclusion))
            ? GitHubCheckState.Failing
            : GitHubCheckState.Passing;
    }

    private static async Task<T?> GetAsync<T>(
        IGitHubApiClient client,
        string path,
        CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new JsonException($"GitHub API response for '{path}' was empty.");
    }

    private sealed record PullRequestResponse(
        [property: JsonPropertyName("number")] int Number,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("mergeable")] bool? Mergeable,
        [property: JsonPropertyName("mergeable_state")] string? MergeableState,
        [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
        [property: JsonPropertyName("user")] ApiUser User,
        [property: JsonPropertyName("head")] ApiHead Head);

    private sealed record ApiUser(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("login")] string Login);

    private sealed record ApiHead([property: JsonPropertyName("sha")] string Sha);

    private sealed record ReviewResponse(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("submitted_at")] DateTimeOffset? SubmittedAt,
        [property: JsonPropertyName("user")] ApiUser User);

    private sealed record CombinedStatusResponse(
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("statuses")] IReadOnlyList<StatusResponse> Statuses);

    private sealed record StatusResponse([property: JsonPropertyName("state")] string State);

    private sealed record CheckRunsResponse(
        [property: JsonPropertyName("check_runs")] IReadOnlyList<CheckRunResponse> CheckRuns);

    private sealed record CheckRunResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("conclusion")] string? Conclusion);
}