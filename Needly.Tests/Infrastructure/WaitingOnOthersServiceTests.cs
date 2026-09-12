using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Needly.Application.GitHub;
using Needly.Domain;
using Needly.Infrastructure;
using Needly.Infrastructure.GitHub;
using Xunit;

namespace Needly.Tests.Infrastructure;

public sealed class WaitingOnOthersServiceTests
{
    private const long GitHubInstallationId = 601;
    private const long GitHubRepositoryId = 101;
    private const long AuthorGitHubId = 701;
    private const long ReviewerGitHubId = 702;
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetWaitingAsync_OpenPullRequestWithNoReviewHistory_ReturnsUnassignedItem()
    {
        await using var database = await WaitingTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "opened",
            PullRequestPayload(false, BaseTime.AddMinutes(1), []),
            BaseTime.AddMinutes(1));

        var service = CreateService(database, BaseTime.AddHours(3));
        var items = await service.GetWaitingAsync(seed.NeedlyUser.Id, CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Equal(WaitingOnOthersReason.NoReviewerAssigned, item.Reason);
        Assert.Equal("Unassigned", item.BlockingParty);
        Assert.Equal(42, item.SubjectNumber);
        Assert.Equal("octocat", item.RepositoryOwner);
        Assert.Equal(TimeSpan.FromHours(3) - TimeSpan.FromMinutes(1), item.WaitingDuration);
    }

    [Fact]
    public async Task GetWaitingAsync_ReviewRequestedAndNotYetReviewed_ReturnsAwaitingReviewWithReviewerAndDuration()
    {
        await using var database = await WaitingTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "review_requested",
            PullRequestPayload(false, BaseTime.AddMinutes(1), [ReviewerGitHubId]),
            BaseTime.AddMinutes(1));

        var service = CreateService(database, BaseTime.AddHours(5));
        var items = await service.GetWaitingAsync(seed.NeedlyUser.Id, CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Equal(WaitingOnOthersReason.AwaitingReview, item.Reason);
        Assert.Equal("@reviewer", item.BlockingParty);
        Assert.Equal(BaseTime.AddMinutes(1), item.WaitingSince);
        Assert.Equal(TimeSpan.FromHours(5) - TimeSpan.FromMinutes(1), item.WaitingDuration);
    }

    [Fact]
    public async Task GetWaitingAsync_ReviewApproved_ExcludesPullRequest()
    {
        await using var database = await WaitingTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "review_requested",
            PullRequestPayload(false, BaseTime.AddMinutes(1), [ReviewerGitHubId]),
            BaseTime.AddMinutes(1));
        await HandleAsync(
            database.Context,
            handler,
            "pull_request_review",
            "submitted",
            PullRequestReviewPayload(ReviewerGitHubId, "reviewer", "approved", BaseTime.AddMinutes(2)),
            BaseTime.AddMinutes(2));

        var service = CreateService(database, BaseTime.AddHours(1));
        var items = await service.GetWaitingAsync(seed.NeedlyUser.Id, CancellationToken.None);

        Assert.Empty(items);
    }

    [Fact]
    public async Task GetWaitingAsync_ChangesRequestedAndNotYetPushed_ExcludesPullRequest()
    {
        // The pull request author still owes a fix here; that is the author's own problem and is already
        // surfaced in their Inbox as a Resolve action, not in the Waiting-on-others queue.
        await using var database = await WaitingTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "review_requested",
            PullRequestPayload(false, BaseTime.AddMinutes(1), [ReviewerGitHubId]),
            BaseTime.AddMinutes(1));
        await HandleAsync(
            database.Context,
            handler,
            "pull_request_review",
            "submitted",
            PullRequestReviewPayload(ReviewerGitHubId, "reviewer", "changes_requested", BaseTime.AddMinutes(2)),
            BaseTime.AddMinutes(2));

        var service = CreateService(database, BaseTime.AddHours(1));
        var items = await service.GetWaitingAsync(seed.NeedlyUser.Id, CancellationToken.None);

        Assert.Empty(items);
    }

    [Fact]
    public async Task GetWaitingAsync_ChangesRequestedThenPushed_ReturnsAwaitingReReview()
    {
        await using var database = await WaitingTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "review_requested",
            PullRequestPayload(false, BaseTime.AddMinutes(1), [ReviewerGitHubId]),
            BaseTime.AddMinutes(1));
        await HandleAsync(
            database.Context,
            handler,
            "pull_request_review",
            "submitted",
            PullRequestReviewPayload(ReviewerGitHubId, "reviewer", "changes_requested", BaseTime.AddMinutes(2)),
            BaseTime.AddMinutes(2));
        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "synchronize",
            PullRequestPayload(false, BaseTime.AddMinutes(30), [], headSha: "head-2"),
            BaseTime.AddMinutes(30));

        var service = CreateService(database, BaseTime.AddHours(2));
        var items = await service.GetWaitingAsync(seed.NeedlyUser.Id, CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Equal(WaitingOnOthersReason.AwaitingReReview, item.Reason);
        Assert.Equal("@reviewer", item.BlockingParty);
        Assert.Equal(BaseTime.AddMinutes(30), item.WaitingSince);
    }

    [Fact]
    public async Task GetWaitingAsync_PullRequestClosed_IsExcluded()
    {
        await using var database = await WaitingTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "opened",
            PullRequestPayload(false, BaseTime.AddMinutes(1), []),
            BaseTime.AddMinutes(1));
        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "closed",
            PullRequestPayload(false, BaseTime.AddMinutes(2), [], merged: true),
            BaseTime.AddMinutes(2));

        var service = CreateService(database, BaseTime.AddHours(1));
        var items = await service.GetWaitingAsync(seed.NeedlyUser.Id, CancellationToken.None);

        Assert.Empty(items);
    }

    [Fact]
    public async Task GetWaitingAsync_InstallationSuspended_ExcludesPullRequest()
    {
        await using var database = await WaitingTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "opened",
            PullRequestPayload(false, BaseTime.AddMinutes(1), []),
            BaseTime.AddMinutes(1));

        await using (var suspend = database.CreateContext())
        {
            var installation = await suspend.Installations.SingleAsync();
            installation.Suspend(BaseTime.AddMinutes(2));
            await suspend.SaveChangesAsync();
        }

        var service = CreateService(database, BaseTime.AddHours(1));
        var items = await service.GetWaitingAsync(seed.NeedlyUser.Id, CancellationToken.None);

        Assert.Empty(items);
    }

    [Fact]
    public async Task GetWaitingAsync_UnknownNeedlyUser_ReturnsEmpty()
    {
        await using var database = await WaitingTestDatabase.CreateAsync();
        await SeedAsync(database.Context);

        var service = CreateService(database, BaseTime.AddHours(1));
        var items = await service.GetWaitingAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(items);
    }

    private static WaitingOnOthersService CreateService(WaitingTestDatabase database, DateTimeOffset now) =>
        new(database.CreateContext(), new FixedTimeProvider(now));

    private static GitHubActionEventHandler CreateHandler(WaitingTestDatabase database) =>
        new(database, GetDetectors(), NullLogger<GitHubActionEventHandler>.Instance);

    private static IReadOnlyList<IGitHubActionDetector> GetDetectors()
    {
        var services = new ServiceCollection();
        services.AddNeedlyGitHubIntegration();
        services.AddSingleton<IGitHubPullRequestLookup>(new FakePullRequestLookup());
        return services.BuildServiceProvider().GetServices<IGitHubActionDetector>().ToArray();
    }

    private static async Task<SeedResult> SeedAsync(NeedlyDbContext context)
    {
        var installation = Installation.Create(
            TestData.InstallationId,
            GitHubInstallationId,
            "octocat",
            BaseTime,
            GitHubAccountType.Organization);
        var repository = TestData.CreateRepository();
        var author = GitHubUser.Create(
            Guid.Parse("81000000-0000-0000-0000-000000000001"), AuthorGitHubId, "author", "Author", null, BaseTime);
        var reviewer = GitHubUser.Create(
            Guid.Parse("81000000-0000-0000-0000-000000000002"), ReviewerGitHubId, "reviewer", "Reviewer", null, BaseTime);
        var needlyUser = NeedlyUser.Create(
            Guid.Parse("81000000-0000-0000-0000-000000000003"),
            author.Id,
            "author@example.com",
            "Author",
            BaseTime);
        context.AddRange(installation, repository, author, reviewer, needlyUser);
        context.InstallationMembers.AddRange(
            InstallationMember.Create(Guid.NewGuid(), installation.Id, author.Id, BaseTime),
            InstallationMember.Create(Guid.NewGuid(), installation.Id, reviewer.Id, BaseTime));
        await context.SaveChangesAsync();
        return new SeedResult(author, reviewer, needlyUser);
    }

    private sealed record SeedResult(GitHubUser Author, GitHubUser Reviewer, NeedlyUser NeedlyUser);

    private static object PullRequestPayload(
        bool draft,
        DateTimeOffset updatedAt,
        long[] requestedReviewerIds,
        string headSha = "head-1",
        bool merged = false) =>
        new
        {
            action = string.Empty,
            number = 42,
            pull_request = new
            {
                html_url = "https://github.com/octocat/needly/pull/42",
                title = "Improve action detection",
                draft,
                merged,
                closed_at = merged ? updatedAt : (DateTimeOffset?)null,
                merged_at = merged ? updatedAt : (DateTimeOffset?)null,
                updated_at = updatedAt,
                user = User(AuthorGitHubId, "author"),
                head = new { sha = headSha },
                requested_reviewers = requestedReviewerIds.Select(id => User(id, "reviewer")),
                requested_teams = Array.Empty<object>()
            },
            requested_reviewer = requestedReviewerIds.Length == 1
                ? User(requestedReviewerIds[0], "reviewer")
                : null,
            requested_team = (object?)null
        };

    private static object PullRequestReviewPayload(
        long reviewerId,
        string reviewerLogin,
        string state,
        DateTimeOffset submittedAt,
        long reviewId = 9001) =>
        new
        {
            action = "submitted",
            number = 42,
            pull_request = new
            {
                html_url = "https://github.com/octocat/needly/pull/42",
                title = "Improve action detection",
                draft = false,
                merged = false,
                updated_at = submittedAt,
                user = User(AuthorGitHubId, "author"),
                head = new { sha = "head-1" },
                requested_reviewers = Array.Empty<object>(),
                requested_teams = Array.Empty<object>()
            },
            review = new
            {
                id = reviewId,
                state,
                user = User(reviewerId, reviewerLogin),
                submitted_at = submittedAt,
                html_url = $"https://github.com/octocat/needly/pull/42#pullrequestreview-{reviewId}"
            }
        };

    private static object User(long id, string login) => new { id, login, type = (string?)null };

    private static PendingStoredEvent CreateStoredEvent(
        NeedlyDbContext context,
        string eventName,
        string action,
        object payload,
        DateTimeOffset receivedAt)
    {
        var eventId = Guid.NewGuid();
        var rawEvent = RawEvent.CreateDelivery(
            eventId,
            TestData.InstallationId,
            GitHubInstallationId,
            TestData.RepositoryId,
            GitHubRepositoryId,
            $"delivery-{eventId:N}",
            eventName,
            action,
            JsonSerializer.Serialize(payload),
            receivedAt);
        return new PendingStoredEvent(
            context,
            rawEvent,
            new GitHubStoredEvent(
                rawEvent.Id,
                rawEvent.GitHubInstallationId,
                rawEvent.GitHubRepositoryId,
                rawEvent.EventName,
                rawEvent.EventAction,
                rawEvent.PayloadJson,
                rawEvent.ReceivedAt));
    }

    private static async Task HandleAsync(
        NeedlyDbContext context,
        GitHubActionEventHandler handler,
        string eventName,
        string action,
        object payload,
        DateTimeOffset receivedAt)
    {
        var pendingEvent = CreateStoredEvent(context, eventName, action, payload, receivedAt);
        await pendingEvent.PersistAsync();
        await handler.HandleAsync(pendingEvent.StoredEvent, CancellationToken.None);
    }

    private sealed record PendingStoredEvent(
        NeedlyDbContext Context,
        RawEvent RawEvent,
        GitHubStoredEvent StoredEvent)
    {
        internal async Task PersistAsync()
        {
            Context.RawEvents.Add(RawEvent);
            await Context.SaveChangesAsync();
        }
    }

    private sealed class FakePullRequestLookup : IGitHubPullRequestLookup
    {
        public Task<GitHubPullRequestReadiness?> GetAsync(
            long gitHubInstallationId,
            string repositoryOwner,
            string repositoryName,
            int pullRequestNumber,
            CancellationToken cancellationToken) =>
            Task.FromResult<GitHubPullRequestReadiness?>(null);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class WaitingTestDatabase : IDbContextFactory<NeedlyDbContext>, IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly DbContextOptions<NeedlyDbContext> options;

        private WaitingTestDatabase(
            SqliteConnection connection,
            DbContextOptions<NeedlyDbContext> options,
            NeedlyDbContext context)
        {
            this.connection = connection;
            this.options = options;
            Context = context;
        }

        internal NeedlyDbContext Context { get; }

        internal static async Task<WaitingTestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<NeedlyDbContext>().UseSqlite(connection).Options;
            var context = new NeedlyDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new WaitingTestDatabase(connection, options, context);
        }

        internal NeedlyDbContext CreateContext() => new(options);

        public NeedlyDbContext CreateDbContext() => CreateContext();

        public Task<NeedlyDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateContext());

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
