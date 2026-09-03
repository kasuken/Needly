using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Needly.Application.GitHub;
using Needly.Domain;

namespace Needly.Infrastructure.GitHub;

/// <summary>Creates durable action events from current GitHub repository state.</summary>
public sealed class GitHubHistoricalBootstrapService(
    NeedlyDbContext dbContext,
    IGitHubApiClientFactory apiClientFactory,
    IGitHubWebhookQueue queue,
    TimeProvider timeProvider,
    IOptions<GitHubHistoricalBootstrapOptions> options,
    ILogger<GitHubHistoricalBootstrapService> logger) : IGitHubHistoricalBootstrapService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> FailureConclusions = new(StringComparer.OrdinalIgnoreCase)
    {
        "failure",
        "timed_out",
        "cancelled",
        "action_required",
        "stale"
    };
    private readonly GitHubHistoricalBootstrapOptions options = options?.Value
        ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public async Task<int> BootstrapNextBatchAsync(CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return 0;
        }

        var now = timeProvider.GetUtcNow();
        var retryStartedBefore = now.Subtract(options.ClaimTimeout);
        var eligibleRepositories = await dbContext.Repositories
            .AsNoTracking()
            .Where(repository =>
                repository.IsActive &&
                repository.HistoricalBootstrapCompletedAt == null &&
                dbContext.Installations.Any(installation =>
                    installation.Id == repository.InstallationId &&
                    installation.State == InstallationState.Active) &&
                dbContext.InstallationMembers.Any(member =>
                    member.InstallationId == repository.InstallationId &&
                    member.IsActive))
            .Select(repository => new
            {
                repository.Id,
                repository.CreatedAt,
                repository.HistoricalBootstrapStartedAt
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var repositoryIds = eligibleRepositories
            .Where(repository => repository.HistoricalBootstrapStartedAt is null ||
                repository.HistoricalBootstrapStartedAt <= retryStartedBefore)
            .OrderBy(repository => repository.CreatedAt)
            .ThenBy(repository => repository.Id)
            .Take(options.MaxRepositoriesPerBatch)
            .Select(repository => repository.Id)
            .ToArray();

        var claimedCount = 0;
        foreach (var repositoryId in repositoryIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var repository = await dbContext.Repositories
                    .SingleAsync(item => item.Id == repositoryId, cancellationToken)
                    .ConfigureAwait(false);
                if (!repository.TryStartHistoricalBootstrap(now, retryStartedBefore))
                {
                    continue;
                }

                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                claimedCount++;
                await BootstrapRepositoryAsync(repository, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Historical GitHub bootstrap failed for repository {RepositoryId}; it will be retried after the claim timeout",
                    repositoryId);
                dbContext.ChangeTracker.Clear();
            }
        }

        return claimedCount;
    }

    private async Task BootstrapRepositoryAsync(
        Repository repository,
        CancellationToken cancellationToken)
    {
        var installation = await dbContext.Installations
            .SingleAsync(item => item.Id == repository.InstallationId, cancellationToken)
            .ConfigureAwait(false);
        var client = await apiClientFactory
            .CreateAsync(installation.GitHubInstallationId, cancellationToken)
            .ConfigureAwait(false);
        var candidates = await GetCurrentEventsAsync(client, repository, cancellationToken)
            .ConfigureAwait(false);
        var epoch = repository.UpdatedAt.ToUnixTimeSeconds();
        var deliveryPrefix = $"bootstrap:{repository.GitHubRepositoryId}:{epoch}:";
        var existingDeliveryIds = await dbContext.RawEvents
            .AsNoTracking()
            .Where(rawEvent => rawEvent.DeliveryId.StartsWith(deliveryPrefix))
            .Select(rawEvent => rawEvent.DeliveryId)
            .ToHashSetAsync(StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);
        var receivedAt = timeProvider.GetUtcNow();
        var createdEvents = new List<RawEvent>();
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var deliveryId = $"{deliveryPrefix}{candidate.DeliverySuffix}";
            if (existingDeliveryIds.Contains(deliveryId))
            {
                continue;
            }

            createdEvents.Add(RawEvent.CreateDelivery(
                Guid.NewGuid(),
                installation.Id,
                installation.GitHubInstallationId,
                repository.Id,
                repository.GitHubRepositoryId,
                deliveryId,
                candidate.EventName,
                candidate.Action,
                candidate.PayloadJson,
                receivedAt.AddTicks(index)));
        }

        dbContext.RawEvents.AddRange(createdEvents);
        repository.CompleteHistoricalBootstrap(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var rawEvent in createdEvents)
        {
            await queue.EnqueueAsync(rawEvent.Id, cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation(
            "Completed historical GitHub bootstrap for {Owner}/{Repository}: {EventCount} events queued",
            repository.Owner,
            repository.Name,
            createdEvents.Count);
    }

    private async Task<IReadOnlyList<HistoricalEventCandidate>> GetCurrentEventsAsync(
        IGitHubApiClient client,
        Repository repository,
        CancellationToken cancellationToken)
    {
        var repositoryPath = $"repos/{Escape(repository.Owner)}/{Escape(repository.Name)}";
        var pullRequests = await GetListAsync<HistoricalPullRequest>(
            client,
            $"{repositoryPath}/pulls?state=open&sort=updated&direction=asc&per_page=100",
            cancellationToken).ConfigureAwait(false);
        var issues = await GetListAsync<HistoricalIssue>(
            client,
            $"{repositoryPath}/issues?state=open&sort=updated&direction=asc&per_page=100",
            cancellationToken).ConfigureAwait(false);
        var candidates = new List<HistoricalEventCandidate>();
        foreach (var pullRequest in pullRequests.OrderBy(item => item.UpdatedAt).ThenBy(item => item.Number))
        {
            var pullRequestPayload = ToPullRequestPayload(pullRequest);
            candidates.Add(CreateCandidate(
                $"pr-state:{pullRequest.Number}",
                GitHubHistoricalEventNames.PullRequest,
                "opened",
                pullRequestPayload: pullRequestPayload));

            var reviews = await GetListAsync<HistoricalReview>(
                client,
                $"{repositoryPath}/pulls/{pullRequest.Number}/reviews?per_page=100",
                cancellationToken).ConfigureAwait(false);
            var timeline = new List<(DateTimeOffset OccurredAt, long Id, HistoricalEventCandidate Candidate)>();
            foreach (var review in reviews
                         .Where(item => item.Id > 0 && item.User.Id > 0))
            {
                timeline.Add((
                    review.SubmittedAt ?? pullRequest.CreatedAt,
                    review.Id,
                    CreateCandidate(
                        $"review:{review.Id}",
                        GitHubHistoricalEventNames.PullRequestReview,
                        "submitted",
                        pullRequestPayload: pullRequestPayload,
                        review: new GitHubReviewPayload(
                            review.Id,
                            review.State,
                            ToUserPayload(review.User),
                            review.SubmittedAt,
                            review.HtmlUrl))));
            }

            var reviewComments = await GetListAsync<HistoricalComment>(
                client,
                $"{repositoryPath}/pulls/{pullRequest.Number}/comments?per_page=100",
                cancellationToken).ConfigureAwait(false);
            foreach (var comment in reviewComments
                         .Where(item => item.Id > 0 && item.User.Id > 0))
            {
                timeline.Add((
                    comment.CreatedAt ?? pullRequest.CreatedAt,
                    comment.Id,
                    CreateCandidate(
                        $"review-comment:{comment.Id}",
                        GitHubHistoricalEventNames.PullRequestReviewComment,
                        "created",
                        pullRequestPayload: pullRequestPayload,
                        comment: ToCommentPayload(comment))));
            }

            var conversationComments = await GetListAsync<HistoricalComment>(
                client,
                $"{repositoryPath}/issues/{pullRequest.Number}/comments?per_page=100",
                cancellationToken).ConfigureAwait(false);
            var pullRequestIssue = new GitHubIssuePayload(
                pullRequest.Number,
                pullRequest.HtmlUrl,
                pullRequest.Title,
                ToUserPayload(pullRequest.User),
                JsonSerializer.SerializeToElement(new { }));
            foreach (var comment in conversationComments.Where(item => item.Id > 0 && item.User.Id > 0))
            {
                timeline.Add((
                    comment.CreatedAt ?? pullRequest.CreatedAt,
                    comment.Id,
                    CreateCandidate(
                        $"issue-comment:{comment.Id}",
                        GitHubHistoricalEventNames.IssueComment,
                        "created",
                        issue: pullRequestIssue,
                        comment: ToCommentPayload(comment))));
            }

            candidates.AddRange(timeline
                .OrderBy(item => item.OccurredAt)
                .ThenBy(item => item.Id)
                .Select(item => item.Candidate));

            candidates.Add(CreateCandidate(
                $"pr-current:{pullRequest.Number}",
                "pull_request",
                pullRequest.Draft ? "opened" : "ready_for_review",
                pullRequestPayload: pullRequestPayload));

            var checkRuns = await GetCheckRunsAsync(
                client,
                $"{repositoryPath}/commits/{Escape(pullRequest.Head.Sha)}/check-runs?per_page=100",
                cancellationToken).ConfigureAwait(false);
            foreach (var checkRun in checkRuns
                         .Where(item => item.Id > 0 &&
                             string.Equals(item.Status, "completed", StringComparison.OrdinalIgnoreCase) &&
                             item.Conclusion is not null &&
                             FailureConclusions.Contains(item.Conclusion))
                         .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                         .Select(group => group.OrderByDescending(item => item.CompletedAt).ThenByDescending(item => item.Id).First())
                         .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(CreateCandidate(
                    $"check:{checkRun.Id}",
                    GitHubHistoricalEventNames.CheckRun,
                    "completed",
                    checkRun: new GitHubCheckRunPayload(
                        checkRun.Id,
                        checkRun.Name,
                        checkRun.Status,
                        checkRun.Conclusion,
                        checkRun.CompletedAt,
                        checkRun.DetailsUrl,
                        checkRun.HtmlUrl,
                        new GitHubCheckSuitePayload(
                            checkRun.CheckSuite?.Id ?? checkRun.Id,
                            pullRequest.Head.Sha,
                            "completed",
                            checkRun.Conclusion,
                            checkRun.CompletedAt,
                            checkRun.DetailsUrl,
                            null,
                            [new GitHubAssociatedPullRequestPayload(pullRequest.Number)]),
                        [new GitHubAssociatedPullRequestPayload(pullRequest.Number)])));
            }

            var statuses = await GetListAsync<HistoricalStatus>(
                client,
                $"{repositoryPath}/commits/{Escape(pullRequest.Head.Sha)}/statuses?per_page=100",
                cancellationToken).ConfigureAwait(false);
            foreach (var status in statuses
                         .Where(item => item.Id > 0 &&
                             item.State is "failure" or "error")
                         .GroupBy(item => item.Context, StringComparer.OrdinalIgnoreCase)
                         .Select(group => group.OrderByDescending(item => item.UpdatedAt).ThenByDescending(item => item.Id).First())
                         .OrderBy(item => item.Context, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(CreateCandidate(
                    $"status:{status.Id}",
                    GitHubHistoricalEventNames.CheckRun,
                    "completed",
                    checkRun: new GitHubCheckRunPayload(
                        status.Id,
                        status.Context,
                        "completed",
                        "failure",
                        status.UpdatedAt,
                        status.TargetUrl,
                        status.TargetUrl,
                        new GitHubCheckSuitePayload(
                            status.Id,
                            pullRequest.Head.Sha,
                            "completed",
                            "failure",
                            status.UpdatedAt,
                            status.TargetUrl,
                            null,
                            [new GitHubAssociatedPullRequestPayload(pullRequest.Number)]),
                        [new GitHubAssociatedPullRequestPayload(pullRequest.Number)])));
            }
        }

        foreach (var issue in issues
                     .Where(item => item.PullRequest is null)
                     .OrderBy(item => item.UpdatedAt)
                     .ThenBy(item => item.Number))
        {
            var comments = await GetListAsync<HistoricalComment>(
                client,
                $"{repositoryPath}/issues/{issue.Number}/comments?per_page=100",
                cancellationToken).ConfigureAwait(false);
            foreach (var comment in comments
                         .Where(item => item.Id > 0 && item.User.Id > 0)
                         .OrderBy(item => item.CreatedAt)
                         .ThenBy(item => item.Id))
            {
                candidates.Add(CreateCandidate(
                    $"issue-comment:{comment.Id}",
                    GitHubHistoricalEventNames.IssueComment,
                    "created",
                    issue: new GitHubIssuePayload(
                        issue.Number,
                        issue.HtmlUrl,
                        issue.Title,
                        ToUserPayload(issue.User),
                        issue.PullRequest),
                    comment: ToCommentPayload(comment)));
            }
        }

        return candidates;
    }

    private async Task<IReadOnlyList<T>> GetListAsync<T>(
        IGitHubApiClient client,
        string path,
        CancellationToken cancellationToken)
    {
        var items = new List<T>();
        for (var page = 1; page <= options.MaxPagesPerEndpoint; page++)
        {
            var pagePath = page == 1 ? path : $"{path}&page={page}";
            using var response = await client.SendAsync(HttpMethod.Get, pagePath, null, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var pageItems = await response.Content.ReadFromJsonAsync<IReadOnlyList<T>>(
                JsonOptions,
                cancellationToken).ConfigureAwait(false)
                ?? throw new JsonException($"GitHub returned an empty response for '{pagePath}'.");
            items.AddRange(pageItems);
            if (!HasNextPage(response))
            {
                return items;
            }
        }

        logger.LogWarning("Historical GitHub bootstrap truncated paginated endpoint {Path}", path);
        return items;
    }

    private async Task<IReadOnlyList<HistoricalCheckRun>> GetCheckRunsAsync(
        IGitHubApiClient client,
        string path,
        CancellationToken cancellationToken)
    {
        var items = new List<HistoricalCheckRun>();
        for (var page = 1; page <= options.MaxPagesPerEndpoint; page++)
        {
            var pagePath = page == 1 ? path : $"{path}&page={page}";
            using var response = await client.SendAsync(HttpMethod.Get, pagePath, null, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var pageItems = await response.Content.ReadFromJsonAsync<HistoricalCheckRunsResponse>(
                JsonOptions,
                cancellationToken).ConfigureAwait(false)
                ?? throw new JsonException($"GitHub returned an empty response for '{pagePath}'.");
            items.AddRange(pageItems.CheckRuns);
            if (!HasNextPage(response))
            {
                return items;
            }
        }

        logger.LogWarning("Historical GitHub bootstrap truncated paginated endpoint {Path}", path);
        return items;
    }

    private static bool HasNextPage(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Link", out var links) &&
        links.Any(link => link.Contains("rel=\"next\"", StringComparison.Ordinal));

    private static HistoricalEventCandidate CreateCandidate(
        string deliverySuffix,
        string eventName,
        string action,
        GitHubPullRequestPayload? pullRequestPayload = null,
        GitHubIssuePayload? issue = null,
        GitHubReviewPayload? review = null,
        GitHubReviewCommentPayload? comment = null,
        GitHubCheckRunPayload? checkRun = null)
    {
        var payload = new GitHubActionWebhookPayload(
            action,
            pullRequestPayload?.Number ?? issue?.Number ?? 0,
            pullRequestPayload,
            issue,
            null,
            null,
            review,
            comment,
            null,
            checkRun,
            null,
            null);
        return new HistoricalEventCandidate(
            deliverySuffix,
            eventName,
            action,
            JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static GitHubPullRequestPayload ToPullRequestPayload(HistoricalPullRequest pullRequest) =>
        new(
            pullRequest.Number,
            pullRequest.HtmlUrl,
            pullRequest.Title,
            pullRequest.Draft,
            false,
            pullRequest.UpdatedAt,
            null,
            null,
            ToUserPayload(pullRequest.User),
            new GitHubPullRequestHeadPayload(pullRequest.Head.Sha),
            pullRequest.RequestedReviewers.Select(ToUserPayload).ToArray(),
            pullRequest.RequestedTeams.Select(team => new GitHubActionTeamPayload(team.Id, team.Slug, team.Name)).ToArray());

    private static GitHubActionUserPayload ToUserPayload(HistoricalUser user) =>
        new(user.Id, user.Login, user.Type);

    private static GitHubReviewCommentPayload ToCommentPayload(HistoricalComment comment) =>
        new(
            comment.Id,
            ToUserPayload(comment.User),
            comment.CreatedAt,
            comment.UpdatedAt,
            comment.PullRequestReviewId,
            comment.Body,
            comment.HtmlUrl);

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private sealed record HistoricalEventCandidate(
        string DeliverySuffix,
        string EventName,
        string Action,
        string PayloadJson);

    private sealed record HistoricalUser(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("login")] string Login,
        [property: JsonPropertyName("type")] string? Type);

    private sealed record HistoricalTeam(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("slug")] string Slug,
        [property: JsonPropertyName("name")] string? Name);

    private sealed record HistoricalHead([property: JsonPropertyName("sha")] string Sha);

    private sealed record HistoricalPullRequest(
        [property: JsonPropertyName("number")] int Number,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
        [property: JsonPropertyName("user")] HistoricalUser User,
        [property: JsonPropertyName("head")] HistoricalHead Head,
        [property: JsonPropertyName("requested_reviewers")] IReadOnlyList<HistoricalUser> RequestedReviewers,
        [property: JsonPropertyName("requested_teams")] IReadOnlyList<HistoricalTeam> RequestedTeams);

    private sealed record HistoricalReview(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("submitted_at")] DateTimeOffset? SubmittedAt,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("user")] HistoricalUser User);

    private sealed record HistoricalComment(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt,
        [property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("pull_request_review_id")] long? PullRequestReviewId,
        [property: JsonPropertyName("user")] HistoricalUser User);

    private sealed record HistoricalIssue(
        [property: JsonPropertyName("number")] int Number,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
        [property: JsonPropertyName("user")] HistoricalUser User,
        [property: JsonPropertyName("pull_request")] JsonElement? PullRequest);

    private sealed record HistoricalCheckSuite([property: JsonPropertyName("id")] long Id);

    private sealed record HistoricalCheckRun(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("conclusion")] string? Conclusion,
        [property: JsonPropertyName("completed_at")] DateTimeOffset? CompletedAt,
        [property: JsonPropertyName("details_url")] string? DetailsUrl,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("check_suite")] HistoricalCheckSuite? CheckSuite);

    private sealed record HistoricalCheckRunsResponse(
        [property: JsonPropertyName("check_runs")] IReadOnlyList<HistoricalCheckRun> CheckRuns);

    private sealed record HistoricalStatus(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("context")] string Context,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("target_url")] string? TargetUrl,
        [property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt);
}