using System.Net;
using System.Text;
using Needly.Application.GitHub;
using Needly.Infrastructure.GitHub;
using Xunit;

namespace Needly.Tests.Infrastructure;

public sealed class GitHubPullRequestLookupTests
{
    [Fact]
    public async Task GetAsync_TypedApiResponses_AggregatesLatestReviewsChecksAndMergeability()
    {
        var client = new FakeGitHubApiClient(new Dictionary<string, string>
        {
            ["repos/octocat/needly/pulls/42"] = """
                {"number":42,"html_url":"https://github.com/octocat/needly/pull/42","title":"Ready change","state":"open","draft":false,"mergeable":true,"mergeable_state":"clean","updated_at":"2026-09-03T10:00:00Z","user":{"id":201,"login":"author"},"head":{"sha":"head-1"}}
                """,
            ["repos/octocat/needly/pulls/42/reviews?per_page=100"] = """
                [{"id":1,"state":"changes_requested","submitted_at":"2026-09-03T08:00:00Z","user":{"id":202,"login":"reviewer"}},{"id":2,"state":"approved","submitted_at":"2026-09-03T09:00:00Z","user":{"id":202,"login":"reviewer"}},{"id":3,"state":"approved","submitted_at":"2026-09-03T09:30:00Z","user":{"id":203,"login":"other-reviewer"}}]
                """,
            ["repos/octocat/needly/commits/head-1/status?per_page=100"] = """
                {"state":"success","statuses":[{"state":"success"}]}
                """,
            ["repos/octocat/needly/commits/head-1/check-runs?per_page=100"] = """
                {"check_runs":[{"status":"completed","conclusion":"success"}]}
                """
        });
        var factory = new FakeGitHubApiClientFactory(client);
        var lookup = new GitHubPullRequestLookup(factory);

        var result = await lookup.GetAsync(501, "octocat", "needly", 42, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result.ApprovalCount);
        Assert.False(result.HasChangesRequested);
        Assert.Equal(GitHubCheckState.Passing, result.CheckState);
        Assert.True(result.IsMergeable);
        Assert.False(result.HasConflicts);
        Assert.Equal(501, factory.InstallationId);
        Assert.Equal(4, client.RequestedPaths.Count);
    }

    [Fact]
    public async Task GetAsync_PreCanceledToken_StopsBeforeApiRequest()
    {
        var client = new FakeGitHubApiClient(new Dictionary<string, string>());
        var lookup = new GitHubPullRequestLookup(new FakeGitHubApiClientFactory(client));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            lookup.GetAsync(501, "octocat", "needly", 42, cancellation.Token));

        Assert.Empty(client.RequestedPaths);
    }

    [Fact]
    public async Task GetAsync_CheckRunsOnlyWithEmptyPendingCombinedStatus_IsPassing()
    {
        var client = new FakeGitHubApiClient(new Dictionary<string, string>
        {
            ["repos/octocat/needly/pulls/42"] = """
                {"number":42,"html_url":"https://github.com/octocat/needly/pull/42","title":"Ready change","state":"open","draft":false,"mergeable":true,"mergeable_state":"clean","updated_at":"2026-09-03T10:00:00Z","user":{"id":201,"login":"author"},"head":{"sha":"head-1"}}
                """,
            ["repos/octocat/needly/pulls/42/reviews?per_page=100"] = """
                [{"id":1,"state":"approved","submitted_at":"2026-09-03T09:00:00Z","user":{"id":202,"login":"reviewer"}}]
                """,
            ["repos/octocat/needly/commits/head-1/status?per_page=100"] = """
                {"state":"pending","statuses":[]}
                """,
            ["repos/octocat/needly/commits/head-1/check-runs?per_page=100"] = """
                {"check_runs":[{"status":"completed","conclusion":"success"}]}
                """
        });
        var lookup = new GitHubPullRequestLookup(new FakeGitHubApiClientFactory(client));

        var result = await lookup.GetAsync(501, "octocat", "needly", 42, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(GitHubCheckState.Passing, result.CheckState);
    }

    private sealed class FakeGitHubApiClientFactory(FakeGitHubApiClient client) : IGitHubApiClientFactory
    {
        internal long InstallationId { get; private set; }

        public Task<IGitHubApiClient> CreateAsync(long gitHubInstallationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InstallationId = gitHubInstallationId;
            return Task.FromResult<IGitHubApiClient>(client);
        }
    }

    private sealed class FakeGitHubApiClient(IReadOnlyDictionary<string, string> responses) : IGitHubApiClient
    {
        internal List<string> RequestedPaths { get; } = [];

        public Task<HttpResponseMessage> SendAsync(
            HttpMethod method,
            string relativePath,
            HttpContent? content,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedPaths.Add(relativePath);
            var response = responses.TryGetValue(relativePath, out var json)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
            return Task.FromResult(response);
        }
    }
}