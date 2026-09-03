using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Needly.Application.Actions;
using Needly.Application.GitHub;
using Needly.Domain;
using Needly.Infrastructure;
using Needly.Infrastructure.GitHub;
using Xunit;

namespace Needly.Tests.Infrastructure;

public sealed class GitHubActionEventHandlerTests
{
    private const long GitHubInstallationId = 501;
    private const long GitHubRepositoryId = 101;
    private const long GitHubUserId = 201;
    private static readonly GitHubActionTarget Target = new(
        ActionType.Review,
        GitHubSubjectType.PullRequest,
        42,
        ActionAssigneeType.User,
        GitHubUserId);

    [Fact]
    public void OperationRecords_SameInputs_HaveDeterministicValueEquality()
    {
        var occurredAt = TestData.CreatedAt.AddMinutes(1);

        var first = CreateOperation("Review one", occurredAt, reactivateTerminal: true);
        var second = CreateOperation("Review one", occurredAt, reactivateTerminal: true);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public async Task HandleAsync_CreateThenUpdateThenResolve_TransitionsOneActionToDone()
    {
        await using var database = await ActionEngineTestDatabase.CreateAsync();
        await SeedDependenciesAsync(database.Context);
        var observedStates = new List<ActionState?>();
        var detector = new SyntheticDetector(
            "synthetic.lifecycle",
            10,
            (context, _) =>
            {
                observedStates.Add(context.Actions.SingleOrDefault()?.State);
                GitHubActionOperation operation = context.Event.Action switch
                {
                    "opened" => CreateOperation("Initial title", context.Event.ReceivedAt),
                    "synchronize" => new UpdateGitHubActionOperation(
                        Target,
                        "Updated title",
                        "Updated context",
                        "Updated reason",
                        context.Event.ReceivedAt),
                    "closed" => new ResolveGitHubActionOperation(Target, context.Event.ReceivedAt),
                    _ => throw new InvalidOperationException("Unexpected synthetic action.")
                };
                return Task.FromResult<IReadOnlyList<GitHubActionOperation>>([operation]);
            });
        var handler = CreateHandler(database, detector);

        foreach (var (action, minutes) in new[] { ("opened", 1), ("synchronize", 2), ("closed", 3) })
        {
            var storedEvent = await AddEventAsync(database.Context, action, minutes);
            await handler.HandleAsync(storedEvent, CancellationToken.None);
        }

        await using var verification = database.CreateContext();
        var persisted = await verification.Actions.AsNoTracking().SingleAsync();
        Assert.Equal(ActionState.Done, persisted.State);
        Assert.Equal("Updated title", persisted.Title);
        Assert.Equal(TestData.CreatedAt.AddMinutes(2), persisted.LastActivityAt);
        Assert.Equal([null, ActionState.Open, ActionState.Open], observedStates);
        Assert.Equal(3, await verification.ActionEventReceipts.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_DetectorsRegisteredOutOfOrder_InvokesByOrderThenKey()
    {
        await using var database = await ActionEngineTestDatabase.CreateAsync();
        await SeedDependenciesAsync(database.Context);
        var storedEvent = await AddEventAsync(database.Context, "opened", 1);
        var calls = new List<string>();
        var last = RecordingDetector("detector.z", 20, calls);
        var second = RecordingDetector("detector.b", 10, calls);
        var first = RecordingDetector("detector.a", 10, calls);
        var handler = CreateHandler(database, last, second, first);

        await handler.HandleAsync(storedEvent, CancellationToken.None);

        Assert.Equal(["detector.a", "detector.b", "detector.z"], calls);
        Assert.Equal(3, await database.Context.ActionEventReceipts.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_SameEventTwice_InvokesDetectorAndCreatesActionExactlyOnce()
    {
        await using var database = await ActionEngineTestDatabase.CreateAsync();
        await SeedDependenciesAsync(database.Context);
        var storedEvent = await AddEventAsync(database.Context, "opened", 1);
        var invocationCount = 0;
        var detector = new SyntheticDetector(
            "synthetic.idempotency",
            10,
            (context, _) =>
            {
                invocationCount++;
                return Task.FromResult<IReadOnlyList<GitHubActionOperation>>(
                    [CreateOperation("Initial title", context.Event.ReceivedAt)]);
            });
        var handler = CreateHandler(database, detector);

        await handler.HandleAsync(storedEvent, CancellationToken.None);
        await handler.HandleAsync(storedEvent, CancellationToken.None);

        Assert.Equal(1, invocationCount);
        Assert.Equal(1, await database.Context.Actions.CountAsync());
        Assert.Equal(1, await database.Context.ActionEventReceipts.CountAsync());
        Assert.Equal(RawEventStatus.Pending, await database.Context.RawEvents
            .Where(rawEvent => rawEvent.Id == storedEvent.EventId)
            .Select(rawEvent => rawEvent.Status)
            .SingleAsync());
    }

    [Theory]
    [InlineData(ActionState.Open)]
    [InlineData(ActionState.Snoozed)]
    public async Task HandleAsync_TwoCreateEventsForActiveTarget_UpdatesOpenOrSnoozedWithoutDuplicate(
        ActionState activeState)
    {
        await using var database = await ActionEngineTestDatabase.CreateAsync();
        await SeedDependenciesAsync(database.Context);
        var detector = new SyntheticDetector(
            "synthetic.upsert",
            10,
            (context, _) => Task.FromResult<IReadOnlyList<GitHubActionOperation>>(
                [CreateOperation(context.Event.Action!, context.Event.ReceivedAt)]));
        var handler = CreateHandler(database, detector);
        var first = await AddEventAsync(database.Context, "First title", 1);
        var second = await AddEventAsync(database.Context, "Second title", 2);

        await handler.HandleAsync(first, CancellationToken.None);
        var action = await database.Context.Actions.SingleAsync();
        action.ChangeState(activeState, TestData.CreatedAt.AddMinutes(1));
        await database.Context.SaveChangesAsync();
        await handler.HandleAsync(second, CancellationToken.None);

        await database.Context.Entry(action).ReloadAsync();
        var persisted = action;
        Assert.Equal("Second title", persisted.Title);
        Assert.Equal(TestData.CreatedAt.AddMinutes(2), persisted.LastActivityAt);
        Assert.Equal(activeState, persisted.State);
        Assert.Equal(1, await database.Context.Actions.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_DoneAction_ReactivatesOnlyWhenCreateExplicitlyRequestsIt()
    {
        await using var database = await ActionEngineTestDatabase.CreateAsync();
        var (_, repository, user) = await SeedDependenciesAsync(database.Context);
        var action = TestData.CreateAction(repository: repository, assignee: user);
        action.ChangeState(ActionState.Done, TestData.CreatedAt.AddMinutes(1));
        database.Context.Actions.Add(action);
        await database.Context.SaveChangesAsync();
        var detector = new SyntheticDetector(
            "synthetic.reactivation",
            10,
            (context, _) => Task.FromResult<IReadOnlyList<GitHubActionOperation>>(
                [CreateOperation(
                    context.Event.Action!,
                    context.Event.ReceivedAt,
                    reactivateTerminal: context.Event.Action == "requested")]));
        var handler = CreateHandler(database, detector);

        await handler.HandleAsync(
            await AddEventAsync(database.Context, "not-requested", 2),
            CancellationToken.None);
        await database.Context.Entry(action).ReloadAsync();
        Assert.Equal(ActionState.Done, action.State);

        await handler.HandleAsync(
            await AddEventAsync(database.Context, "requested", 3),
            CancellationToken.None);

        await database.Context.Entry(action).ReloadAsync();
        Assert.Equal(ActionState.Open, action.State);
        Assert.Equal("requested", action.Title);
        Assert.Equal(TestData.CreatedAt.AddMinutes(3), action.WaitingSince);
        Assert.Equal(1, await database.Context.Actions.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_ArchivedAction_OnlySignificantCreateReactivatesIt()
    {
        await using var database = await ActionEngineTestDatabase.CreateAsync();
        var (_, repository, user) = await SeedDependenciesAsync(database.Context);
        var action = TestData.CreateAction(repository: repository, assignee: user);
        action.ChangeState(ActionState.Archived, TestData.CreatedAt.AddMinutes(1));
        database.Context.Actions.Add(action);
        await database.Context.SaveChangesAsync();
        var detector = new SyntheticDetector(
            "synthetic.archived-significance",
            10,
            (context, _) => Task.FromResult<IReadOnlyList<GitHubActionOperation>>(
                [CreateOperation(
                    context.Event.Action!,
                    context.Event.ReceivedAt,
                    significance: context.Event.Action == "significant"
                        ? ActionEventSignificance.Significant
                        : ActionEventSignificance.Routine)]));
        var handler = CreateHandler(database, detector);

        await handler.HandleAsync(
            await AddEventAsync(database.Context, "routine", 2),
            CancellationToken.None);
        await database.Context.Entry(action).ReloadAsync();
        Assert.Equal(ActionState.Archived, action.State);

        await handler.HandleAsync(
            await AddEventAsync(database.Context, "significant", 3),
            CancellationToken.None);

        await database.Context.Entry(action).ReloadAsync();
        Assert.Equal(ActionState.Open, action.State);
        Assert.Equal("significant", action.Title);
        Assert.Equal(1, await database.Context.Actions.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_SnoozedAction_SignificantCreateCancelsSnoozeEarly()
    {
        await using var database = await ActionEngineTestDatabase.CreateAsync();
        var (_, repository, user) = await SeedDependenciesAsync(database.Context);
        var action = TestData.CreateAction(repository: repository, assignee: user);
        var deadline = TestData.CreatedAt.AddDays(1);
        action.Snooze(deadline, TestData.CreatedAt.AddMinutes(1));
        database.Context.Actions.Add(action);
        await database.Context.SaveChangesAsync();
        var detector = new SyntheticDetector(
            "synthetic.snooze-significance",
            10,
            (context, _) => Task.FromResult<IReadOnlyList<GitHubActionOperation>>(
                [CreateOperation(
                    context.Event.Action!,
                    context.Event.ReceivedAt,
                    significance: context.Event.Action == "significant"
                        ? ActionEventSignificance.Significant
                        : ActionEventSignificance.Routine)]));
        var handler = CreateHandler(database, detector);

        await handler.HandleAsync(
            await AddEventAsync(database.Context, "routine", 2),
            CancellationToken.None);
        await database.Context.Entry(action).ReloadAsync();
        Assert.Equal(ActionState.Snoozed, action.State);
        Assert.Equal(deadline, action.SnoozedUntil);

        await handler.HandleAsync(
            await AddEventAsync(database.Context, "significant", 3),
            CancellationToken.None);

        await database.Context.Entry(action).ReloadAsync();
        Assert.Equal(ActionState.Open, action.State);
        Assert.Null(action.SnoozedUntil);
    }

    [Fact]
    public async Task HandleAsync_ActiveMuteSuppression_PreventsFutureCreateForSubjectAndAssignee()
    {
        await using var database = await ActionEngineTestDatabase.CreateAsync();
        var (installation, repository, user) = await SeedDependenciesAsync(database.Context);
        var needlyUser = NeedlyUser.Create(
            Guid.NewGuid(),
            user.Id,
            "octocat@example.test",
            "Octocat",
            TestData.CreatedAt);
        var action = TestData.CreateAction(repository: repository, assignee: user);
        var suppression = ActionSuppression.Create(
            Guid.NewGuid(),
            needlyUser.Id,
            action,
            TestData.CreatedAt.AddMinutes(1));
        action.ChangeState(ActionState.Muted, TestData.CreatedAt.AddMinutes(1));
        database.Context.AddRange(needlyUser, action, suppression);
        await database.Context.SaveChangesAsync();
        var detector = new SyntheticDetector(
            "synthetic.suppression",
            10,
            (context, _) => Task.FromResult<IReadOnlyList<GitHubActionOperation>>(
                [CreateOperation(
                    "Suppressed create",
                    context.Event.ReceivedAt,
                    reactivateTerminal: true,
                    significance: ActionEventSignificance.Significant)]));
        var handler = CreateHandler(database, detector);

        await handler.HandleAsync(
            await AddEventAsync(database.Context, "requested", 2),
            CancellationToken.None);

        Assert.Equal(installation.Id, suppression.InstallationId);
        Assert.Equal(1, await database.Context.Actions.CountAsync());
        Assert.Equal(ActionState.Muted, (await database.Context.Actions.SingleAsync()).State);
    }

    [Fact]
    public async Task HandleAsync_TeamSuppression_CreatesActionForUnsuppressedMembersOnly()
    {
        await using var database = await ActionEngineTestDatabase.CreateAsync();
        var (installation, repository, firstUser) = await SeedDependenciesAsync(database.Context);
        var secondUser = TestData.CreateGitHubUser(Guid.NewGuid(), 202);
        var firstNeedlyUser = NeedlyUser.Create(
            Guid.NewGuid(), firstUser.Id, "first@example.test", "First", TestData.CreatedAt);
        var secondNeedlyUser = NeedlyUser.Create(
            Guid.NewGuid(), secondUser.Id, "second@example.test", "Second", TestData.CreatedAt);
        var team = Team.Create(
            Guid.NewGuid(), installation.Id, 801, "maintainers", "Maintainers", TestData.CreatedAt);
        var suppressedAction = NeedlyAction.CreateForTeam(
            Guid.NewGuid(),
            ActionType.Review,
            repository,
            team,
            GitHubSubjectType.PullRequest,
            42,
            "https://github.com/octocat/needly/pull/42",
            "Suppressed team review",
            null,
            "Team review requested",
            TestData.CreatedAt);
        var suppression = ActionSuppression.Create(
            Guid.NewGuid(), firstNeedlyUser.Id, suppressedAction, TestData.CreatedAt.AddMinutes(1));
        database.Context.AddRange(
            secondUser,
            firstNeedlyUser,
            secondNeedlyUser,
            team,
            InstallationMember.Create(Guid.NewGuid(), installation.Id, secondUser.Id, TestData.CreatedAt),
            TeamMember.Create(Guid.NewGuid(), team.Id, firstUser.Id, TestData.CreatedAt),
            TeamMember.Create(Guid.NewGuid(), team.Id, secondUser.Id, TestData.CreatedAt),
            suppression);
        await database.Context.SaveChangesAsync();
        var teamTarget = Target with
        {
            AssigneeType = ActionAssigneeType.Team,
            GitHubAssigneeId = team.GitHubTeamId
        };
        var detector = new SyntheticDetector(
            "synthetic.team-suppression",
            10,
            (context, _) => Task.FromResult<IReadOnlyList<GitHubActionOperation>>(
                [new CreateGitHubActionOperation(
                    teamTarget,
                    "https://github.com/octocat/needly/pull/42",
                    "Team review",
                    null,
                    "Team review requested",
                    context.Event.ReceivedAt,
                    ReactivateTerminal: true,
                    ActionEventSignificance.Significant)]));
        var handler = CreateHandler(database, detector);

        await handler.HandleAsync(
            await AddEventAsync(database.Context, "requested", 2),
            CancellationToken.None);

        Assert.Equal(ActionState.Open, (await database.Context.Actions.SingleAsync()).State);
        Assert.Empty(await new InboxVisibilityService(database.Context, TimeProvider.System)
            .GetVisibleAsync(firstNeedlyUser.Id, CancellationToken.None));
        Assert.Single(await new InboxVisibilityService(database.Context, TimeProvider.System)
            .GetVisibleAsync(secondNeedlyUser.Id, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ActionChange_PublishesOnlyAfterSuccessfulCommit()
    {
        await using var database = await ActionEngineTestDatabase.CreateAsync();
        await SeedDependenciesAsync(database.Context);
        var broadcaster = new RecordingBroadcaster();
        var successfulEvent = await AddEventAsync(database.Context, "opened", 1);
        var successful = new SyntheticDetector(
            "synthetic.successful-publish",
            10,
            (context, _) => Task.FromResult<IReadOnlyList<GitHubActionOperation>>(
                [CreateOperation("Initial title", context.Event.ReceivedAt)]));
        var successfulHandler = CreateHandler(database, broadcaster, successful);

        await successfulHandler.HandleAsync(successfulEvent, CancellationToken.None);

        Assert.Equal(1, broadcaster.PublishCount);
        Assert.Equal(1, await database.Context.Actions.CountAsync());

        var failingEvent = await AddEventAsync(database.Context, "failed", 2);
        var first = new SyntheticDetector(
            "synthetic.rollback-first",
            10,
            (context, _) => Task.FromResult<IReadOnlyList<GitHubActionOperation>>(
                [new UpdateGitHubActionOperation(
                    Target,
                    "Changed title",
                    null,
                    "Changed reason",
                    context.Event.ReceivedAt)]));
        var missingTarget = Target with { GitHubAssigneeId = 999 };
        var failing = new SyntheticDetector(
            "synthetic.rollback-second",
            20,
            (context, _) => Task.FromResult<IReadOnlyList<GitHubActionOperation>>(
                [new ResolveGitHubActionOperation(missingTarget, context.Event.ReceivedAt)]));
        var failingHandler = CreateHandler(database, broadcaster, first, failing);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => failingHandler.HandleAsync(failingEvent, CancellationToken.None));

        Assert.Equal(1, broadcaster.PublishCount);
        database.Context.ChangeTracker.Clear();
        Assert.Equal("Initial title", (await database.Context.Actions.SingleAsync()).Title);
    }

    [Fact]
    public async Task HandleAsync_MissingInstallation_ThrowsPreciseFailureWithoutReceipt()
    {
        await using var database = await ActionEngineTestDatabase.CreateAsync();
        var storedEvent = await AddEventAsync(database.Context, "opened", 1, includeForeignKeys: false);
        var handler = CreateHandler(database, RecordingDetector("synthetic.context", 10, []));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(storedEvent, CancellationToken.None));

        Assert.Contains($"installation {GitHubInstallationId}", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, await database.Context.ActionEventReceipts.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_MissingRepository_ThrowsPreciseFailureWithoutReceipt()
    {
        await using var database = await ActionEngineTestDatabase.CreateAsync();
        database.Context.Installations.Add(CreateInstallation());
        await database.Context.SaveChangesAsync();
        var storedEvent = await AddEventAsync(database.Context, "opened", 1, includeForeignKeys: false);
        var handler = CreateHandler(database, RecordingDetector("synthetic.context", 10, []));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(storedEvent, CancellationToken.None));

        Assert.Contains($"repository {GitHubRepositoryId}", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, await database.Context.ActionEventReceipts.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_MissingAssignee_ThrowsPreciseFailureWithoutActionOrReceipt()
    {
        await using var database = await ActionEngineTestDatabase.CreateAsync();
        await SeedDependenciesAsync(database.Context, includeAssignee: false);
        var storedEvent = await AddEventAsync(database.Context, "opened", 1);
        var detector = new SyntheticDetector(
            "synthetic.context",
            10,
            (context, _) => Task.FromResult<IReadOnlyList<GitHubActionOperation>>(
                [CreateOperation("Initial title", context.Event.ReceivedAt)]));
        var handler = CreateHandler(database, detector);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(storedEvent, CancellationToken.None));

        Assert.Contains($"assignee {GitHubUserId}", exception.Message, StringComparison.Ordinal);
        await AssertNothingAppliedAsync(database);
    }

    [Fact]
    public async Task HandleAsync_LaterDetectorOperationFails_RollsBackEarlierActionAndReceipt()
    {
        await using var database = await ActionEngineTestDatabase.CreateAsync();
        await SeedDependenciesAsync(database.Context);
        var storedEvent = await AddEventAsync(database.Context, "opened", 1);
        var first = new SyntheticDetector(
            "synthetic.first",
            10,
            (context, _) => Task.FromResult<IReadOnlyList<GitHubActionOperation>>(
                [CreateOperation("Initial title", context.Event.ReceivedAt)]));
        var missingTarget = Target with { GitHubAssigneeId = 999 };
        var failing = new SyntheticDetector(
            "synthetic.failing",
            20,
            (context, _) => Task.FromResult<IReadOnlyList<GitHubActionOperation>>(
                [new ResolveGitHubActionOperation(missingTarget, context.Event.ReceivedAt)]));
        var handler = CreateHandler(database, first, failing);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(storedEvent, CancellationToken.None));

        await AssertNothingAppliedAsync(database);
    }

    [Fact]
    public async Task HandleAsync_CancelledDuringLaterDetector_RollsBackAndPropagatesCancellation()
    {
        await using var database = await ActionEngineTestDatabase.CreateAsync();
        await SeedDependenciesAsync(database.Context);
        var storedEvent = await AddEventAsync(database.Context, "opened", 1);
        using var cancellation = new CancellationTokenSource();
        var first = new SyntheticDetector(
            "synthetic.first",
            10,
            (context, _) => Task.FromResult<IReadOnlyList<GitHubActionOperation>>(
                [CreateOperation("Initial title", context.Event.ReceivedAt)]));
        var cancelling = new SyntheticDetector(
            "synthetic.cancel",
            20,
            (_, token) =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.FromResult<IReadOnlyList<GitHubActionOperation>>([]);
            });
        var handler = CreateHandler(database, first, cancelling);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => handler.HandleAsync(storedEvent, cancellation.Token));

        await AssertNothingAppliedAsync(database);
    }

    private static CreateGitHubActionOperation CreateOperation(
        string title,
        DateTimeOffset occurredAt,
        bool reactivateTerminal = false,
        ActionEventSignificance significance = ActionEventSignificance.Routine) =>
        new(
            Target,
            "https://github.com/octocat/needly/pull/42",
            title,
            "Synthetic context",
            "Synthetic reason",
            occurredAt,
            reactivateTerminal,
            significance);

    private static GitHubActionEventHandler CreateHandler(
        ActionEngineTestDatabase database,
        params IGitHubActionDetector[] detectors) =>
        new(database, detectors, NullLogger<GitHubActionEventHandler>.Instance);

    private static GitHubActionEventHandler CreateHandler(
        ActionEngineTestDatabase database,
        IActionChangeBroadcaster broadcaster,
        params IGitHubActionDetector[] detectors) =>
        new(database, detectors, NullLogger<GitHubActionEventHandler>.Instance, broadcaster);

    private static SyntheticDetector RecordingDetector(
        string key,
        int order,
        List<string> calls) =>
        new(
            key,
            order,
            (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                calls.Add(key);
                return Task.FromResult<IReadOnlyList<GitHubActionOperation>>([]);
            });

    private static async Task<(Installation Installation, Repository Repository, GitHubUser User)>
        SeedDependenciesAsync(NeedlyDbContext context, bool includeAssignee = true)
    {
        var installation = CreateInstallation();
        var repository = TestData.CreateRepository();
        var user = TestData.CreateGitHubUser();
        context.AddRange(installation, repository);
        if (includeAssignee)
        {
            context.AddRange(
                user,
                InstallationMember.Create(
                    Guid.Parse("55555555-5555-5555-5555-555555555555"),
                    installation.Id,
                    user.Id,
                    TestData.CreatedAt));
        }

        await context.SaveChangesAsync();
        return (installation, repository, user);
    }

    private static Installation CreateInstallation() =>
        Installation.Create(
            TestData.InstallationId,
            GitHubInstallationId,
            "octocat",
            TestData.CreatedAt);

    private static async Task<GitHubStoredEvent> AddEventAsync(
        NeedlyDbContext context,
        string action,
        int minutes,
        bool includeForeignKeys = true)
    {
        var eventId = Guid.NewGuid();
        var occurredAt = TestData.CreatedAt.AddMinutes(minutes);
        var rawEvent = RawEvent.CreateDelivery(
            eventId,
            includeForeignKeys ? TestData.InstallationId : null,
            GitHubInstallationId,
            includeForeignKeys ? TestData.RepositoryId : null,
            GitHubRepositoryId,
            $"delivery-{eventId:N}",
            "pull_request",
            action,
            "{}",
            occurredAt);
        context.RawEvents.Add(rawEvent);
        await context.SaveChangesAsync();
        return new GitHubStoredEvent(
            rawEvent.Id,
            rawEvent.GitHubInstallationId,
            rawEvent.GitHubRepositoryId,
            rawEvent.EventName,
            rawEvent.EventAction,
            rawEvent.PayloadJson,
            rawEvent.ReceivedAt);
    }

    private static async Task AssertNothingAppliedAsync(ActionEngineTestDatabase database)
    {
        await using var verification = database.CreateContext();
        Assert.Equal(0, await verification.Actions.CountAsync());
        Assert.Equal(0, await verification.ActionEventReceipts.CountAsync());
    }

    private sealed class SyntheticDetector(
        string key,
        int order,
        Func<GitHubActionDetectionContext, CancellationToken, Task<IReadOnlyList<GitHubActionOperation>>> detect)
        : IGitHubActionDetector
    {
        public string Key => key;

        public int Order => order;

        public Task<IReadOnlyList<GitHubActionOperation>> DetectAsync(
            GitHubActionDetectionContext context,
            CancellationToken cancellationToken) => detect(context, cancellationToken);
    }

    private sealed class RecordingBroadcaster : IActionChangeBroadcaster
    {
        public event Action? Changed;

        public int PublishCount { get; private set; }

        public void Publish()
        {
            PublishCount++;
            Changed?.Invoke();
        }
    }

    private sealed class ActionEngineTestDatabase : IDbContextFactory<NeedlyDbContext>, IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly DbContextOptions<NeedlyDbContext> options;

        private ActionEngineTestDatabase(
            SqliteConnection connection,
            DbContextOptions<NeedlyDbContext> options,
            NeedlyDbContext context)
        {
            this.connection = connection;
            this.options = options;
            Context = context;
        }

        internal NeedlyDbContext Context { get; }

        internal static async Task<ActionEngineTestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<NeedlyDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new NeedlyDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new ActionEngineTestDatabase(connection, options, context);
        }

        internal NeedlyDbContext CreateContext() => new(options);

        public NeedlyDbContext CreateDbContext() => CreateContext();

        public Task<NeedlyDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default)
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