using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Needly.Application.GitHub;
using Needly.Domain;
using Needly.Infrastructure;
using Needly.Infrastructure.GitHub;
using Xunit;

namespace Needly.Tests.Infrastructure;

public sealed class GitHubHistoricalBootstrapServiceTests
{
    private const string RepositoryPath = "repos/octo-org/repo";
    private static readonly DateTimeOffset BootstrapAt = TestData.CreatedAt.AddHours(1);

    [Fact]
    public async Task BootstrapNextBatchAsync_OpenRepository_PersistsCurrentActionEventsInProcessingOrder()
    {
        await using var database = await BootstrapTestDatabase.CreateAsync(includeMembership: true);
        var responses = CreateEmptyResponses();
        responses[$"{RepositoryPath}/pulls?state=open&sort=updated&direction=asc&per_page=100"] = new(
            "[{\"number\":42,\"html_url\":\"https://github.com/octo-org/repo/pull/42\",\"title\":\"Ship history\",\"draft\":false,\"created_at\":\"2026-09-02T10:05:00Z\",\"updated_at\":\"2026-09-02T10:30:00Z\",\"user\":{\"id\":202,\"login\":\"author\",\"type\":\"User\"},\"head\":{\"sha\":\"abc123\"},\"requested_reviewers\":[{\"id\":201,\"login\":\"reviewer\",\"type\":\"User\"}],\"requested_teams\":[]}]",
            "<https://api.github.com/repos/octo-org/repo/pulls?state=open&sort=updated&direction=asc&per_page=100&page=2>; rel=\"next\"");
        responses[$"{RepositoryPath}/pulls?state=open&sort=updated&direction=asc&per_page=100&page=2"] = new("[]");
        responses[$"{RepositoryPath}/pulls/42/reviews?per_page=100"] = new(
            "[{\"id\":301,\"state\":\"changes_requested\",\"submitted_at\":\"2026-09-02T10:20:00Z\",\"html_url\":\"https://github.com/review/301\",\"user\":{\"id\":203,\"login\":\"maintainer\",\"type\":\"User\"}}]");
        responses[$"{RepositoryPath}/pulls/42/comments?per_page=100"] = new(
            "[{\"id\":302,\"body\":\"Please adjust this.\",\"created_at\":\"2026-09-02T10:21:00Z\",\"updated_at\":\"2026-09-02T10:21:00Z\",\"html_url\":\"https://github.com/comment/302\",\"pull_request_review_id\":301,\"user\":{\"id\":203,\"login\":\"maintainer\",\"type\":\"User\"}}]");
        responses[$"{RepositoryPath}/issues/42/comments?per_page=100"] = new("[]");
        responses[$"{RepositoryPath}/commits/abc123/check-runs?per_page=100"] = new(
            "{\"check_runs\":[{\"id\":401,\"name\":\"build\",\"status\":\"completed\",\"conclusion\":\"failure\",\"completed_at\":\"2026-09-02T10:25:00Z\",\"details_url\":\"https://github.com/check/401\",\"html_url\":\"https://github.com/check/401\",\"check_suite\":{\"id\":400}}]}");
        responses[$"{RepositoryPath}/commits/abc123/statuses?per_page=100"] = new(
            "[{\"id\":501,\"context\":\"policy\",\"state\":\"error\",\"target_url\":\"https://github.com/status/501\",\"updated_at\":\"2026-09-02T10:26:00Z\"}]");
        responses[$"{RepositoryPath}/issues?state=open&sort=updated&direction=asc&per_page=100"] = new(
            "[{\"number\":9,\"html_url\":\"https://github.com/octo-org/repo/issues/9\",\"title\":\"Investigate\",\"updated_at\":\"2026-09-02T10:40:00Z\",\"user\":{\"id\":202,\"login\":\"author\",\"type\":\"User\"},\"pull_request\":null}]");
        responses[$"{RepositoryPath}/issues/9/comments?per_page=100"] = new(
            "[{\"id\":601,\"body\":\"@reviewer can you check this?\",\"created_at\":\"2026-09-02T10:41:00Z\",\"updated_at\":\"2026-09-02T10:41:00Z\",\"html_url\":\"https://github.com/comment/601\",\"user\":{\"id\":203,\"login\":\"maintainer\",\"type\":\"User\"}}]");
        var factory = new RecordingApiClientFactory(responses);
        var queue = new RecordingQueue();
        var service = CreateService(database.Context, factory, queue);

        var claimed = await service.BootstrapNextBatchAsync(CancellationToken.None);

        var repository = await database.Context.Repositories.SingleAsync();
        var events = (await database.Context.RawEvents.ToListAsync())
            .OrderBy(item => item.ReceivedAt)
            .ToList();
        Assert.Equal(1, claimed);
        Assert.Equal(BootstrapAt, repository.HistoricalBootstrapStartedAt);
        Assert.Equal(BootstrapAt, repository.HistoricalBootstrapCompletedAt);
        Assert.Equal(
            [
                "needly_historical_pull_request",
                "needly_historical_pull_request_review",
                "needly_historical_pull_request_review_comment",
                "pull_request",
                "needly_historical_check_run",
                "needly_historical_check_run",
                "needly_historical_issue_comment"
            ],
            events.Select(item => item.EventName));
        Assert.Equal(
            ["opened", "submitted", "created", "ready_for_review", "completed", "completed", "created"],
            events.Select(item => item.EventAction));
        Assert.Equal(events.Select(item => item.Id), queue.EventIds);
        Assert.Equal(events.Count, events.Select(item => item.DeliveryId).Distinct().Count());
        Assert.Contains(
            $"{RepositoryPath}/pulls?state=open&sort=updated&direction=asc&per_page=100&page=2",
            factory.RequestPaths);
    }

    [Fact]
    public async Task BootstrapNextBatchAsync_CompletedRepository_DoesNotFetchOrDuplicateEvents()
    {
        await using var database = await BootstrapTestDatabase.CreateAsync(includeMembership: true);
        var factory = new RecordingApiClientFactory(CreateEmptyResponses());
        var queue = new RecordingQueue();
        var service = CreateService(database.Context, factory, queue);

        var firstClaimed = await service.BootstrapNextBatchAsync(CancellationToken.None);
        var secondClaimed = await service.BootstrapNextBatchAsync(CancellationToken.None);

        Assert.Equal(1, firstClaimed);
        Assert.Equal(0, secondClaimed);
        Assert.Single(factory.InstallationIds);
        Assert.Empty(await database.Context.RawEvents.ToListAsync());
    }

    [Fact]
    public async Task BootstrapNextBatchAsync_WithoutActiveInstallationMember_DoesNotClaimRepository()
    {
        await using var database = await BootstrapTestDatabase.CreateAsync(includeMembership: false);
        var factory = new RecordingApiClientFactory(CreateEmptyResponses());
        var service = CreateService(database.Context, factory, new RecordingQueue());

        var claimed = await service.BootstrapNextBatchAsync(CancellationToken.None);

        Assert.Equal(0, claimed);
        Assert.Empty(factory.InstallationIds);
        Assert.Null((await database.Context.Repositories.SingleAsync()).HistoricalBootstrapStartedAt);
    }

    private static Dictionary<string, ApiResponse> CreateEmptyResponses() => new()
    {
        [$"{RepositoryPath}/pulls?state=open&sort=updated&direction=asc&per_page=100"] = new("[]"),
        [$"{RepositoryPath}/issues?state=open&sort=updated&direction=asc&per_page=100"] = new("[]")
    };

    private static GitHubHistoricalBootstrapService CreateService(
        NeedlyDbContext context,
        RecordingApiClientFactory factory,
        RecordingQueue queue) =>
        new(
            context,
            factory,
            queue,
            new FixedTimeProvider(BootstrapAt),
            Options.Create(new GitHubHistoricalBootstrapOptions
            {
                MaxRepositoriesPerBatch = 5,
                MaxPagesPerEndpoint = 2,
                ClaimTimeout = TimeSpan.FromMinutes(15),
                BatchInterval = TimeSpan.FromMinutes(1)
            }),
            NullLogger<GitHubHistoricalBootstrapService>.Instance);

    private sealed record ApiResponse(string Json, string? Link = null);

    private sealed class RecordingApiClientFactory(IReadOnlyDictionary<string, ApiResponse> responses)
        : IGitHubApiClientFactory
    {
        private readonly IReadOnlyDictionary<string, ApiResponse> responses = responses;

        internal List<long> InstallationIds { get; } = [];
        internal List<string> RequestPaths { get; } = [];

        public Task<IGitHubApiClient> CreateAsync(
            long gitHubInstallationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InstallationIds.Add(gitHubInstallationId);
            return Task.FromResult<IGitHubApiClient>(new RecordingApiClient(this));
        }

        private sealed class RecordingApiClient(RecordingApiClientFactory factory) : IGitHubApiClient
        {
            public Task<HttpResponseMessage> SendAsync(
                HttpMethod method,
                string relativePath,
                HttpContent? content,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.Equal(HttpMethod.Get, method);
                Assert.Null(content);
                factory.RequestPaths.Add(relativePath);
                Assert.True(factory.responses.TryGetValue(relativePath, out var configured));
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(configured.Json, Encoding.UTF8, "application/json")
                };
                if (configured.Link is not null)
                {
                    response.Headers.TryAddWithoutValidation("Link", configured.Link);
                }

                return Task.FromResult(response);
            }
        }
    }

    private sealed class RecordingQueue : IGitHubWebhookQueue
    {
        internal List<Guid> EventIds { get; } = [];

        public ValueTask EnqueueAsync(Guid eventId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EventIds.Add(eventId);
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<Guid> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            cancellationToken.ThrowIfCancellationRequested();
            yield break;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class BootstrapTestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private BootstrapTestDatabase(SqliteConnection connection, NeedlyDbContext context)
        {
            this.connection = connection;
            Context = context;
        }

        internal NeedlyDbContext Context { get; }

        internal static async Task<BootstrapTestDatabase> CreateAsync(bool includeMembership)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new NeedlyDbContext(
                new DbContextOptionsBuilder<NeedlyDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await context.Database.EnsureCreatedAsync();
            var installation = Installation.Create(
                TestData.InstallationId,
                501,
                "octo-org",
                TestData.CreatedAt,
                GitHubAccountType.Organization,
                601);
            var repository = Repository.Create(
                TestData.RepositoryId,
                installation.Id,
                701,
                "octo-org",
                "repo",
                TestData.CreatedAt);
            context.AddRange(installation, repository);
            if (includeMembership)
            {
                var user = TestData.CreateGitHubUser(gitHubUserId: 201);
                context.AddRange(
                    user,
                    InstallationMember.Create(
                        Guid.NewGuid(),
                        installation.Id,
                        user.Id,
                        TestData.CreatedAt));
            }

            await context.SaveChangesAsync();
            return new BootstrapTestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
