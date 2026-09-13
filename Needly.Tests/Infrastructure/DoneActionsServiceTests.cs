using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Needly.Application.Actions;
using Needly.Domain;
using Needly.Infrastructure;
using Needly.Infrastructure.Actions;
using Xunit;

namespace Needly.Tests.Infrastructure;

public sealed class DoneActionsServiceTests
{
    [Fact]
    public async Task GetAsync_ManuallyArchivedAction_UsesUndoRecordForClosedByAndClosedAt()
    {
        await using var database = await DoneTestDatabase.CreateAsync();
        var seed = await SeedAsync(database);
        var archivedAt = TestData.CreatedAt.AddDays(1);
        await using (var context = database.CreateContext())
        {
            var action = await context.Actions.SingleAsync();
            var undo = ActionLifecycleUndo.Create(
                Guid.NewGuid(), seed.NeedlyUser.Id, action, ActionState.Archived, null, archivedAt);
            action.ChangeState(ActionState.Archived, archivedAt);
            context.Add(undo);
            await context.SaveChangesAsync();
        }
        var service = CreateService(database, TestData.CreatedAt.AddDays(2));

        var results = await service.GetAsync(seed.NeedlyUser.Id, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(ActionState.Archived, result.State);
        Assert.Equal("Manually archived", result.ClosedReason);
        Assert.Equal(archivedAt, result.ClosedAt);
        Assert.Equal(seed.NeedlyUser.DisplayName, result.ClosedBy);
        Assert.True(result.CanRestore);
    }

    [Fact]
    public async Task GetAsync_ResolvedAction_ReportsCompletedOnGitHubWithNoAttribution()
    {
        await using var database = await DoneTestDatabase.CreateAsync();
        var seed = await SeedAsync(database);
        var resolvedAt = TestData.CreatedAt.AddDays(1);
        await using (var context = database.CreateContext())
        {
            var action = await context.Actions.SingleAsync();
            action.ChangeState(ActionState.Done, resolvedAt);
            await context.SaveChangesAsync();
        }
        var service = CreateService(database, TestData.CreatedAt.AddDays(2));

        var results = await service.GetAsync(seed.NeedlyUser.Id, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(ActionState.Done, result.State);
        Assert.Equal("Completed on GitHub", result.ClosedReason);
        Assert.Equal(resolvedAt, result.ClosedAt);
        Assert.Null(result.ClosedBy);
        Assert.True(result.CanRestore);
    }

    [Fact]
    public async Task GetAsync_AutomationArchivedAction_UsesRuleExecutionAndCannotBeRestored()
    {
        await using var database = await DoneTestDatabase.CreateAsync();
        var seed = await SeedAsync(database);
        var archivedAt = TestData.CreatedAt.AddDays(1);
        const string explanation = "Rule 'Stale reviews' matched Review in octocat/needly and archived it for you.";
        await using (var context = database.CreateContext())
        {
            var action = await context.Actions.SingleAsync();
            var disposition = ActionDisposition.Create(Guid.NewGuid(), seed.NeedlyUser.Id, action.Id, archivedAt);
            disposition.Apply(RuleEffect.AutoArchive, null, archivedAt);
            context.Add(disposition);
            context.Add(RuleExecution.Create(
                Guid.NewGuid(),
                seed.NeedlyUser.Id,
                Guid.NewGuid(),
                "Stale reviews",
                action.Id,
                Guid.NewGuid(),
                RuleEffect.AutoArchive,
                0,
                explanation,
                archivedAt));
            await context.SaveChangesAsync();
        }
        var service = CreateService(database, TestData.CreatedAt.AddDays(2));

        var results = await service.GetAsync(seed.NeedlyUser.Id, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(ActionState.Open, result.State);
        Assert.Equal(explanation, result.ClosedReason);
        Assert.Equal(archivedAt, result.ClosedAt);
        Assert.Equal("Rule: Stale reviews", result.ClosedBy);
        Assert.False(result.CanRestore);
    }

    [Fact]
    public async Task GetAsync_ActionsOlderThanRetentionWindow_AreExcluded()
    {
        await using var database = await DoneTestDatabase.CreateAsync();
        var seed = await SeedAsync(database);
        var now = TestData.CreatedAt.AddDays(200);
        await using (var context = database.CreateContext())
        {
            var action = await context.Actions.SingleAsync();
            action.ChangeState(ActionState.Archived, TestData.CreatedAt.AddDays(1));
            await context.SaveChangesAsync();
        }
        var service = CreateService(database, now);

        var results = await service.GetAsync(seed.NeedlyUser.Id, CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetAsync_ActionVisibleToAnotherInstallationMember_IsNotReturned()
    {
        await using var database = await DoneTestDatabase.CreateAsync();
        var seed = await SeedAsync(database);
        var otherGitHubUser = TestData.CreateGitHubUser(Guid.NewGuid(), 902);
        var otherNeedlyUser = NeedlyUser.Create(
            Guid.NewGuid(), otherGitHubUser.Id, "other@example.test", "Other user", TestData.CreatedAt);
        await using (var context = database.CreateContext())
        {
            context.AddRange(
                otherGitHubUser,
                otherNeedlyUser,
                InstallationMember.Create(Guid.NewGuid(), seed.Installation.Id, otherGitHubUser.Id, TestData.CreatedAt));
            var action = await context.Actions.SingleAsync();
            action.ChangeState(ActionState.Archived, TestData.CreatedAt.AddDays(1));
            await context.SaveChangesAsync();
        }
        var service = CreateService(database, TestData.CreatedAt.AddDays(2));

        var results = await service.GetAsync(otherNeedlyUser.Id, CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetAsync_WithFilter_AppliesSharedActionFilterContract()
    {
        await using var database = await DoneTestDatabase.CreateAsync();
        var seed = await SeedAsync(database);
        await using (var context = database.CreateContext())
        {
            var action = await context.Actions.SingleAsync();
            action.ChangeState(ActionState.Done, TestData.CreatedAt.AddDays(1));
            await context.SaveChangesAsync();
        }
        var service = CreateService(database, TestData.CreatedAt.AddDays(2));

        var matching = await service.GetAsync(
            seed.NeedlyUser.Id, new ActionFilter { Types = [ActionType.Review] }, CancellationToken.None);
        var nonMatching = await service.GetAsync(
            seed.NeedlyUser.Id, new ActionFilter { Types = [ActionType.Fix] }, CancellationToken.None);

        Assert.Single(matching);
        Assert.Empty(nonMatching);
    }

    [Fact]
    public async Task GetWeeklyClearedCountAsync_CountsArchivedMutedDoneAndAutomationClearedWithinSevenDays()
    {
        await using var database = await DoneTestDatabase.CreateAsync();
        var seed = await SeedAsync(database);
        var now = TestData.CreatedAt.AddDays(30);
        await using (var context = database.CreateContext())
        {
            var repository = TestData.CreateRepository();
            var archived = TestData.CreateAction(
                Guid.NewGuid(), repository, seed.GitHubUser, subjectNumber: 43, createdAt: TestData.CreatedAt);
            var muted = TestData.CreateAction(
                Guid.NewGuid(), repository, seed.GitHubUser, subjectNumber: 44, createdAt: TestData.CreatedAt);
            var doneOutsideWindow = TestData.CreateAction(
                Guid.NewGuid(), repository, seed.GitHubUser, subjectNumber: 45, createdAt: TestData.CreatedAt);
            var automationCleared = TestData.CreateAction(
                Guid.NewGuid(), repository, seed.GitHubUser, subjectNumber: 46, createdAt: TestData.CreatedAt);
            archived.ChangeState(ActionState.Archived, now.AddDays(-3));
            muted.ChangeState(ActionState.Muted, now.AddDays(-1));
            doneOutsideWindow.ChangeState(ActionState.Done, now.AddDays(-10));
            var disposition = ActionDisposition.Create(
                Guid.NewGuid(), seed.NeedlyUser.Id, automationCleared.Id, now.AddDays(-2));
            disposition.Apply(RuleEffect.AutoArchive, null, now.AddDays(-2));
            context.AddRange(archived, muted, doneOutsideWindow, automationCleared, disposition);
            await context.SaveChangesAsync();
        }
        var service = CreateService(database, now);

        var count = await service.GetWeeklyClearedCountAsync(seed.NeedlyUser.Id, CancellationToken.None);

        Assert.Equal(3, count);
    }

    [Fact]
    public async Task RestoreAsync_ArchivedAction_ReturnsToOpenAndResetsWaitingSince()
    {
        await using var database = await DoneTestDatabase.CreateAsync();
        var seed = await SeedAsync(database);
        await using (var context = database.CreateContext())
        {
            var action = await context.Actions.SingleAsync();
            action.ChangeState(ActionState.Archived, TestData.CreatedAt.AddDays(1));
            await context.SaveChangesAsync();
        }
        var broadcaster = new RecordingBroadcaster();
        var restoreAt = TestData.CreatedAt.AddDays(5);
        var service = new DoneActionsService(database, new MutableTimeProvider(restoreAt), broadcaster);

        var restored = await service.RestoreAsync(seed.NeedlyUser.Id, seed.Action.Id, CancellationToken.None);

        Assert.True(restored);
        await using var verification = database.CreateContext();
        var persisted = await verification.Actions.AsNoTracking().SingleAsync();
        Assert.Equal(ActionState.Open, persisted.State);
        Assert.Equal(restoreAt, persisted.WaitingSince);
        Assert.Equal(1, broadcaster.PublishCount);
    }

    [Fact]
    public async Task RestoreAsync_AutomationArchivedAction_CannotBeRestoredThroughThisFlow()
    {
        await using var database = await DoneTestDatabase.CreateAsync();
        var seed = await SeedAsync(database);
        await using (var context = database.CreateContext())
        {
            var action = await context.Actions.SingleAsync();
            var disposition = ActionDisposition.Create(
                Guid.NewGuid(), seed.NeedlyUser.Id, action.Id, TestData.CreatedAt.AddDays(1));
            disposition.Apply(RuleEffect.AutoArchive, null, TestData.CreatedAt.AddDays(1));
            context.Add(disposition);
            await context.SaveChangesAsync();
        }
        var service = CreateService(database, TestData.CreatedAt.AddDays(2));

        var restored = await service.RestoreAsync(seed.NeedlyUser.Id, seed.Action.Id, CancellationToken.None);

        Assert.False(restored);
        await using var verification = database.CreateContext();
        Assert.Equal(ActionState.Open, (await verification.Actions.SingleAsync()).State);
    }

    [Fact]
    public async Task RestoreAsync_AnotherInstallationMember_DoesNotRestoreAction()
    {
        await using var database = await DoneTestDatabase.CreateAsync();
        var seed = await SeedAsync(database);
        var otherGitHubUser = TestData.CreateGitHubUser(Guid.NewGuid(), 902);
        var otherNeedlyUser = NeedlyUser.Create(
            Guid.NewGuid(), otherGitHubUser.Id, "other@example.test", "Other user", TestData.CreatedAt);
        await using (var context = database.CreateContext())
        {
            context.AddRange(otherGitHubUser, otherNeedlyUser);
            var action = await context.Actions.SingleAsync();
            action.ChangeState(ActionState.Archived, TestData.CreatedAt.AddDays(1));
            await context.SaveChangesAsync();
        }
        var service = CreateService(database, TestData.CreatedAt.AddDays(2));

        var restored = await service.RestoreAsync(otherNeedlyUser.Id, seed.Action.Id, CancellationToken.None);

        Assert.False(restored);
        await using var verification = database.CreateContext();
        Assert.Equal(ActionState.Archived, (await verification.Actions.SingleAsync()).State);
    }

    private static DoneActionsService CreateService(DoneTestDatabase database, DateTimeOffset now) =>
        new(database, new MutableTimeProvider(now), new RecordingBroadcaster());

    private static async Task<DoneSeed> SeedAsync(DoneTestDatabase database)
    {
        var installation = Installation.Create(TestData.InstallationId, 501, "octocat", TestData.CreatedAt);
        var repository = TestData.CreateRepository();
        var gitHubUser = TestData.CreateGitHubUser();
        var needlyUser = NeedlyUser.Create(
            Guid.NewGuid(), gitHubUser.Id, "octocat@example.test", "Octocat", TestData.CreatedAt);
        var action = TestData.CreateAction(repository: repository, assignee: gitHubUser);
        await using var context = database.CreateContext();
        context.AddRange(
            installation,
            repository,
            gitHubUser,
            needlyUser,
            InstallationMember.Create(Guid.NewGuid(), installation.Id, gitHubUser.Id, TestData.CreatedAt),
            action);
        await context.SaveChangesAsync();
        return new DoneSeed(installation, needlyUser, gitHubUser, action);
    }

    private sealed record DoneSeed(
        Installation Installation,
        NeedlyUser NeedlyUser,
        GitHubUser GitHubUser,
        NeedlyAction Action);

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

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class DoneTestDatabase : IDbContextFactory<NeedlyDbContext>, IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly DbContextOptions<NeedlyDbContext> options;

        private DoneTestDatabase(SqliteConnection connection, DbContextOptions<NeedlyDbContext> options)
        {
            this.connection = connection;
            this.options = options;
        }

        public static async Task<DoneTestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<NeedlyDbContext>().UseSqlite(connection).Options;
            await using var context = new NeedlyDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new DoneTestDatabase(connection, options);
        }

        public NeedlyDbContext CreateContext() => new(options);

        public NeedlyDbContext CreateDbContext() => CreateContext();

        public Task<NeedlyDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateContext());

        public async ValueTask DisposeAsync() => await connection.DisposeAsync();
    }
}
