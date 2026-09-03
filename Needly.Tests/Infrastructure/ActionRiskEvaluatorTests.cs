using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Needly.Application.Actions;
using Needly.Domain;
using Needly.Infrastructure;
using Needly.Infrastructure.Actions;
using Xunit;

namespace Needly.Tests.Infrastructure;

public sealed class ActionRiskEvaluatorTests
{
    [Theory]
    [InlineData(479, false)]
    [InlineData(480, false)]
    [InlineData(481, true)]
    public async Task ReviewWaitingThreshold_BeforeAtAndAfterEightHours_UsesStrictBoundary(
        int elapsedMinutes,
        bool expectedAtRisk)
    {
        await using var database = await RiskTestDatabase.CreateAsync(ActionType.Review);
        var evaluator = CreateEvaluator(database.Context, TestData.CreatedAt.AddMinutes(elapsedMinutes));

        await evaluator.EvaluateAsync(CancellationToken.None);

        var action = await database.Context.Actions.AsNoTracking().SingleAsync();
        Assert.Equal(expectedAtRisk, action.IsAtRisk);
    }

    [Theory]
    [InlineData(4319, false)]
    [InlineData(4320, false)]
    [InlineData(4321, true)]
    public async Task GenericInactivityThreshold_BeforeAtAndAfterThreeDays_UsesStrictBoundary(
        int elapsedMinutes,
        bool expectedAtRisk)
    {
        await using var database = await RiskTestDatabase.CreateAsync(ActionType.Fix);
        var evaluator = CreateEvaluator(database.Context, TestData.CreatedAt.AddMinutes(elapsedMinutes));

        await evaluator.EvaluateAsync(CancellationToken.None);

        var action = await database.Context.Actions.AsNoTracking().SingleAsync();
        Assert.Equal(expectedAtRisk, action.IsAtRisk);
    }

    [Theory]
    [InlineData(ActionState.Snoozed)]
    [InlineData(ActionState.Archived)]
    [InlineData(ActionState.Muted)]
    [InlineData(ActionState.Done)]
    public async Task EvaluateAsync_NonOpenActions_AreExcludedAndRiskIsCleared(ActionState state)
    {
        await using var database = await RiskTestDatabase.CreateAsync(ActionType.Review);
        var action = await database.Context.Actions.SingleAsync();
        action.MarkAtRisk("Previously stale.");
        action.ChangeState(state, TestData.CreatedAt.AddMinutes(1));
        await database.Context.SaveChangesAsync();
        var evaluator = CreateEvaluator(database.Context, TestData.CreatedAt.AddDays(4));

        var changed = await evaluator.EvaluateAsync(CancellationToken.None);

        var persisted = await database.Context.Actions.AsNoTracking().SingleAsync();
        Assert.Equal(0, changed);
        Assert.False(persisted.IsAtRisk);
        Assert.Null(persisted.RiskReason);
    }

    [Fact]
    public async Task ApplyEvent_NewGenericActivity_ClearsRiskAndEvaluatorKeepsItClear()
    {
        await using var database = await RiskTestDatabase.CreateAsync(ActionType.Fix);
        var evaluator = CreateEvaluator(database.Context, TestData.CreatedAt.AddDays(4));
        await evaluator.EvaluateAsync(CancellationToken.None);
        var action = await database.Context.Actions.SingleAsync();
        Assert.True(action.IsAtRisk);

        action.ApplyEvent(
            action.Key,
            action.Title,
            action.Context,
            action.Reason,
            TestData.CreatedAt.AddDays(4),
            TestData.CreatedAt.AddDays(4));
        await database.Context.SaveChangesAsync();
        await evaluator.EvaluateAsync(CancellationToken.None);

        var persisted = await database.Context.Actions.AsNoTracking().SingleAsync();
        Assert.False(persisted.IsAtRisk);
        Assert.Null(persisted.RiskReason);
    }

    [Fact]
    public async Task EvaluateAsync_ReviewPastEightHours_MarksExistingActionOnceWithReviewReason()
    {
        await using var database = await RiskTestDatabase.CreateAsync(ActionType.Review);
        var evaluator = CreateEvaluator(database.Context, TestData.CreatedAt.AddHours(9));

        var firstChanged = await evaluator.EvaluateAsync(CancellationToken.None);
        var secondChanged = await evaluator.EvaluateAsync(CancellationToken.None);

        var action = await database.Context.Actions.AsNoTracking().SingleAsync();
        Assert.Equal(1, firstChanged);
        Assert.Equal(0, secondChanged);
        Assert.True(action.IsAtRisk);
        Assert.Contains("Review has been waiting", action.RiskReason, StringComparison.Ordinal);
        Assert.Equal(ActionType.Review, action.Type);
        Assert.Equal(1, await database.Context.Actions.CountAsync());
    }

    [Fact]
    public async Task EvaluateAsync_GenericPastThreeDays_UsesInactivityReason()
    {
        await using var database = await RiskTestDatabase.CreateAsync(ActionType.Fix);
        var evaluator = CreateEvaluator(database.Context, TestData.CreatedAt.AddDays(4));

        await evaluator.EvaluateAsync(CancellationToken.None);

        var action = await database.Context.Actions.AsNoTracking().SingleAsync();
        Assert.Contains("no activity", action.RiskReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvaluateAsync_PreviouslyAtRiskBelowThreshold_ClearsRisk()
    {
        await using var database = await RiskTestDatabase.CreateAsync(ActionType.Fix);
        var action = await database.Context.Actions.SingleAsync();
        action.MarkAtRisk("Previously stale.");
        await database.Context.SaveChangesAsync();
        var evaluator = CreateEvaluator(database.Context, TestData.CreatedAt.AddDays(1));

        var changed = await evaluator.EvaluateAsync(CancellationToken.None);

        var persisted = await database.Context.Actions.AsNoTracking().SingleAsync();
        Assert.Equal(1, changed);
        Assert.False(persisted.IsAtRisk);
        Assert.Null(persisted.RiskReason);
    }

    [Fact]
    public async Task EvaluateAsync_RiskChange_PublishesAfterPersistenceCompletes()
    {
        await using var database = await RiskTestDatabase.CreateAsync(ActionType.Review);
        var action = await database.Context.Actions.SingleAsync();
        var broadcaster = new RecordingBroadcaster();
        EntityState? stateAtPublish = null;
        broadcaster.Changed += () => stateAtPublish = database.Context.Entry(action).State;
        var evaluator = new ActionRiskEvaluator(
            database.Context,
            new FixedTimeProvider(TestData.CreatedAt.AddHours(9)),
            Options.Create(new ActionRiskOptions()),
            broadcaster);

        var changed = await evaluator.EvaluateAsync(CancellationToken.None);
        var unchanged = await evaluator.EvaluateAsync(CancellationToken.None);

        Assert.Equal(1, changed);
        Assert.Equal(0, unchanged);
        Assert.Equal(1, broadcaster.PublishCount);
        Assert.Equal(EntityState.Unchanged, stateAtPublish);
    }

    [Fact]
    public void ActionRiskOptions_Defaults_AreEightHoursThreeDaysAndFifteenMinutes()
    {
        var options = new ActionRiskOptions();

        Assert.Equal(TimeSpan.FromHours(8), options.ReviewWaitingThreshold);
        Assert.Equal(TimeSpan.FromDays(3), options.InactivityThreshold);
        Assert.Equal(TimeSpan.FromMinutes(15), options.EvaluationInterval);
    }

    [Fact]
    public async Task BackgroundService_Stop_CancelsInProgressDeterministicEvaluation()
    {
        var evaluator = new BlockingRiskEvaluator();
        var services = new ServiceCollection();
        services.AddSingleton<IActionRiskEvaluator>(evaluator);
        await using var provider = services.BuildServiceProvider();
        var service = new ActionRiskBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            Options.Create(new ActionRiskOptions { EvaluationInterval = TimeSpan.FromDays(1) }));
        await service.StartAsync(CancellationToken.None);
        await evaluator.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await service.StopAsync(CancellationToken.None);

        Assert.True(evaluator.CancellationObserved);
    }

    private static ActionRiskEvaluator CreateEvaluator(NeedlyDbContext context, DateTimeOffset now) =>
        new(
            context,
            new FixedTimeProvider(now),
            Options.Create(new ActionRiskOptions()));

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class BlockingRiskEvaluator : IActionRiskEvaluator
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool CancellationObserved { get; private set; }

        public async Task<int> EvaluateAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                throw;
            }

            return 0;
        }
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

    private sealed class RiskTestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private RiskTestDatabase(SqliteConnection connection, NeedlyDbContext context)
        {
            this.connection = connection;
            Context = context;
        }

        internal NeedlyDbContext Context { get; }

        internal static async Task<RiskTestDatabase> CreateAsync(ActionType actionType)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<NeedlyDbContext>().UseSqlite(connection).Options;
            var context = new NeedlyDbContext(options);
            await context.Database.EnsureCreatedAsync();
            var installation = Installation.Create(TestData.InstallationId, 501, "octocat", TestData.CreatedAt);
            var repository = TestData.CreateRepository();
            var user = TestData.CreateGitHubUser();
            context.AddRange(
                installation,
                repository,
                user,
                TestData.CreateAction(repository: repository, assignee: user, type: actionType));
            await context.SaveChangesAsync();
            return new RiskTestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}