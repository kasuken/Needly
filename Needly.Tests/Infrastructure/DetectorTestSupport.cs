using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Needly.Application.GitHub;
using Needly.Domain;
using Needly.Infrastructure;
using Needly.Infrastructure.GitHub;

namespace Needly.Tests.Infrastructure;

/// <summary>
/// Shared detector-test scaffolding used by <see cref="DecideActionDetectorTests"/>,
/// <see cref="FollowUpActionDetectorTests"/>, and <see cref="MonitorActionDetectorTests"/> so each
/// new-detector test file does not need to re-derive the harness already established by
/// <c>GitHubActionDetectorTests</c>.
/// </summary>
internal static class DetectorTestSupport
{
    internal const long GitHubInstallationId = 501;
    internal const long GitHubRepositoryId = 101;
    internal const long AuthorGitHubId = 201;
    internal const long ReviewerGitHubId = 202;
    internal const long OtherReviewerGitHubId = 203;
    internal static readonly DateTimeOffset BaseTime = new(2026, 9, 3, 8, 0, 0, TimeSpan.Zero);

    internal static GitHubActionEventHandler CreateHandler(DetectorTestDatabase database, int requiredApprovals = 1)
    {
        return new GitHubActionEventHandler(
            database,
            GetDetectors(requiredApprovals),
            NullLogger<GitHubActionEventHandler>.Instance);
    }

    internal static IReadOnlyList<IGitHubActionDetector> GetDetectors(int requiredApprovals = 1)
    {
        var services = new ServiceCollection();
        services.AddNeedlyGitHubIntegration();
        services.Configure<GitHubActionOptions>(options => options.RequiredApprovals = requiredApprovals);
        services.AddSingleton<IGitHubPullRequestLookup>(new FakePullRequestLookup());
        return services.BuildServiceProvider().GetServices<IGitHubActionDetector>().ToArray();
    }

    internal static async Task HandleAsync(
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

    internal static PendingStoredEvent CreateStoredEvent(
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

    internal static object User(long id, string login, string? type = null) => new { id, login, type };

    internal static object PullRequestPayload(
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
                requested_reviewers = requestedReviewerIds.Select(id =>
                    User(id, id == ReviewerGitHubId ? "reviewer" : "other-reviewer")),
                requested_teams = Array.Empty<object>()
            },
            requested_reviewer = requestedReviewerIds.Length == 1
                ? User(requestedReviewerIds[0], "reviewer")
                : null,
            requested_team = (object?)null
        };

    internal static object PullRequestReviewPayload(
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

    internal static object PullRequestReviewCommentPayload(
        string action,
        DateTimeOffset occurredAt,
        string body = "",
        long commenterId = AuthorGitHubId,
        string commenterLogin = "author",
        long commentId = 8001) =>
        new
        {
            action,
            pull_request = new
            {
                number = 42,
                html_url = "https://github.com/octocat/needly/pull/42",
                title = "Improve action detection",
                draft = false,
                merged = false,
                updated_at = occurredAt,
                user = User(AuthorGitHubId, "author"),
                head = new { sha = "head-1" },
                requested_reviewers = Array.Empty<object>(),
                requested_teams = Array.Empty<object>()
            },
            comment = new
            {
                id = commentId,
                user = User(commenterId, commenterLogin),
                created_at = occurredAt,
                updated_at = occurredAt,
                pull_request_review_id = 9001,
                body,
                html_url = $"https://github.com/octocat/needly/pull/42#discussion_r{commentId}"
            }
        };

    internal static object CheckRunPayload(
        string name,
        string conclusion,
        string headSha,
        DateTimeOffset occurredAt,
        bool associatedPullRequest = true) =>
        new
        {
            action = "completed",
            check_run = new
            {
                id = 6101,
                name,
                status = "completed",
                conclusion,
                completed_at = occurredAt,
                details_url = $"https://github.com/octocat/needly/actions/runs/6101/jobs/{name}",
                check_suite = new
                {
                    id = 6001,
                    head_sha = headSha,
                    status = "completed",
                    conclusion,
                    updated_at = occurredAt,
                    app = new { name = "GitHub Actions" },
                    pull_requests = associatedPullRequest ? new[] { new { number = 42 } } : Array.Empty<object>()
                },
                pull_requests = associatedPullRequest ? new[] { new { number = 42 } } : Array.Empty<object>()
            }
        };

    internal static object IssuePayload(
        string action,
        DateTimeOffset occurredAt,
        string[]? labels = null,
        long[]? assigneeIds = null,
        string? body = null,
        int number = 77) =>
        new
        {
            action,
            issue = new
            {
                number,
                html_url = $"https://github.com/octocat/needly/issues/{number}",
                title = "Ship the v2 pricing model",
                user = User(AuthorGitHubId, "author"),
                pull_request = (object?)null,
                updated_at = occurredAt,
                body,
                labels = (labels ?? []).Select(name => new { id = name.GetHashCode(), name }),
                assignees = (assigneeIds ?? []).Select(id => User(id, id == ReviewerGitHubId ? "reviewer" : "other-reviewer"))
            }
        };

    internal static async Task<SeedResult> SeedAsync(NeedlyDbContext context)
    {
        var installation = Installation.Create(
            TestData.InstallationId,
            GitHubInstallationId,
            "octocat",
            BaseTime,
            GitHubAccountType.Organization);
        var repository = TestData.CreateRepository();
        var author = TestData.CreateGitHubUser(Guid.Parse("81000000-0000-0000-0000-000000000001"), AuthorGitHubId);
        var reviewer = TestData.CreateGitHubUser(Guid.Parse("81000000-0000-0000-0000-000000000002"), ReviewerGitHubId);
        var otherReviewer = TestData.CreateGitHubUser(Guid.Parse("81000000-0000-0000-0000-000000000003"), OtherReviewerGitHubId);
        context.AddRange(installation, repository, author, reviewer, otherReviewer);
        context.InstallationMembers.AddRange(
            InstallationMember.Create(Guid.NewGuid(), installation.Id, author.Id, BaseTime),
            InstallationMember.Create(Guid.NewGuid(), installation.Id, reviewer.Id, BaseTime),
            InstallationMember.Create(Guid.NewGuid(), installation.Id, otherReviewer.Id, BaseTime));
        await context.SaveChangesAsync();
        return new SeedResult(author, reviewer, otherReviewer, repository);
    }

    internal sealed record SeedResult(GitHubUser Author, GitHubUser Reviewer, GitHubUser OtherReviewer, Repository Repository);

    internal sealed record PendingStoredEvent(NeedlyDbContext Context, RawEvent RawEvent, GitHubStoredEvent StoredEvent)
    {
        internal async Task PersistAsync()
        {
            Context.RawEvents.Add(RawEvent);
            await Context.SaveChangesAsync();
        }
    }

    internal sealed class FakePullRequestLookup : IGitHubPullRequestLookup
    {
        internal GitHubPullRequestReadiness? Result { get; set; }

        public Task<GitHubPullRequestReadiness?> GetAsync(
            long gitHubInstallationId,
            string repositoryOwner,
            string repositoryName,
            int pullRequestNumber,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Result);
        }
    }

    internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    internal sealed class DetectorTestDatabase : IDbContextFactory<NeedlyDbContext>, IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly DbContextOptions<NeedlyDbContext> options;

        private DetectorTestDatabase(
            SqliteConnection connection,
            DbContextOptions<NeedlyDbContext> options,
            NeedlyDbContext context)
        {
            this.connection = connection;
            this.options = options;
            Context = context;
        }

        internal NeedlyDbContext Context { get; }

        internal static async Task<DetectorTestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<NeedlyDbContext>().UseSqlite(connection).Options;
            var context = new NeedlyDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new DetectorTestDatabase(connection, options, context);
        }

        internal NeedlyDbContext CreateContext() => new(options);

        public NeedlyDbContext CreateDbContext() => CreateContext();

        public Task<NeedlyDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateContext());
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
