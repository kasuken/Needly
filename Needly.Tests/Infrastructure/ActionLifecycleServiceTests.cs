using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Needly.Application.Actions;
using Needly.Domain;
using Needly.Infrastructure;
using Needly.Infrastructure.Actions;
using Needly.Infrastructure.GitHub;
using Xunit;

namespace Needly.Tests.Infrastructure;

public sealed class ActionLifecycleServiceTests
{
    [Fact]
    public async Task ArchiveAsync_ActionVisibleToAnotherInstallationMember_DoesNotChangeAction()
    {
        await using var database = await LifecycleTestDatabase.CreateAsync();
        var seed = await SeedAsync(database);
        var otherGitHubUser = TestData.CreateGitHubUser(Guid.NewGuid(), 902);
        var otherNeedlyUser = NeedlyUser.Create(
            Guid.NewGuid(),
            otherGitHubUser.Id,
            "other@example.test",
            "Other user",
            TestData.CreatedAt);
        await using (var context = database.CreateContext())
        {
            context.AddRange(
                otherGitHubUser,
                otherNeedlyUser,
                InstallationMember.Create(
                    Guid.NewGuid(),
                    seed.Installation.Id,
                    otherGitHubUser.Id,
                    TestData.CreatedAt));
            await context.SaveChangesAsync();
        }
        var broadcaster = new RecordingBroadcaster();
        var service = CreateService(database, broadcaster);

        var result = await service.ArchiveAsync(
            otherNeedlyUser.Id,
            seed.Action.Id,
            CancellationToken.None);

        Assert.Null(result);
        await using var verification = database.CreateContext();
        Assert.Equal(ActionState.Open, (await verification.Actions.SingleAsync()).State);
        Assert.Equal(0, await verification.ActionLifecycleUndos.CountAsync());
        Assert.Equal(0, broadcaster.PublishCount);
    }

    [Fact]
    public async Task ArchiveAsync_VisibleAction_PersistsUndoAndUndoRestoresOpenState()
    {
        await using var database = await LifecycleTestDatabase.CreateAsync();
        var seed = await SeedAsync(database);
        var broadcaster = new RecordingBroadcaster();
        var service = CreateService(database, broadcaster);

        var change = await service.ArchiveAsync(
            seed.NeedlyUser.Id,
            seed.Action.Id,
            CancellationToken.None);

        Assert.NotNull(change);
        await using (var archivedContext = database.CreateContext())
        {
            Assert.Equal(ActionState.Archived, (await archivedContext.Actions.SingleAsync()).State);
            Assert.Null((await archivedContext.ActionLifecycleUndos.SingleAsync()).UsedAt);
        }

        var restored = await service.UndoAsync(
            seed.NeedlyUser.Id,
            change.UndoId,
            CancellationToken.None);

        Assert.True(restored);
        await using var verification = database.CreateContext();
        Assert.Equal(ActionState.Open, (await verification.Actions.SingleAsync()).State);
        Assert.NotNull((await verification.ActionLifecycleUndos.SingleAsync()).UsedAt);
        Assert.Equal(2, broadcaster.PublishCount);
    }

    [Fact]
    public async Task SnoozeAsync_BeforeAndAtDeadline_ResurfacesOnlyWhenDue()
    {
        await using var database = await LifecycleTestDatabase.CreateAsync();
        var seed = await SeedAsync(database);
        var timeProvider = new MutableTimeProvider(TestData.CreatedAt.AddHours(1));
        var broadcaster = new RecordingBroadcaster();
        var lifecycle = new ActionLifecycleService(database, timeProvider, broadcaster);
        var resurfacer = new ActionSnoozeService(database, timeProvider, broadcaster);
        var deadline = timeProvider.GetUtcNow().AddHours(2);

        var change = await lifecycle.SnoozeAsync(
            seed.NeedlyUser.Id,
            seed.Action.Id,
            deadline,
            CancellationToken.None);
        var beforeDueCount = await resurfacer.ResurfaceDueAsync(CancellationToken.None);
        timeProvider.UtcNow = deadline;
        var dueCount = await resurfacer.ResurfaceDueAsync(CancellationToken.None);

        Assert.NotNull(change);
        Assert.Equal(deadline, change.SnoozedUntil);
        Assert.Equal(0, beforeDueCount);
        Assert.Equal(1, dueCount);
        await using var verification = database.CreateContext();
        Assert.Equal(ActionState.Open, (await verification.Actions.SingleAsync()).State);
        Assert.Null((await verification.Actions.SingleAsync()).SnoozedUntil);
        Assert.Equal(2, broadcaster.PublishCount);
    }

    [Fact]
    public async Task MuteAsync_VisibleAction_PersistsSuppressionAndUndoDeactivatesIt()
    {
        await using var database = await LifecycleTestDatabase.CreateAsync();
        var seed = await SeedAsync(database);
        var broadcaster = new RecordingBroadcaster();
        var service = CreateService(database, broadcaster);

        var change = await service.MuteAsync(
            seed.NeedlyUser.Id,
            seed.Action.Id,
            CancellationToken.None);

        Assert.NotNull(change);
        await using (var mutedContext = database.CreateContext())
        {
            Assert.Equal(ActionState.Muted, (await mutedContext.Actions.SingleAsync()).State);
            var suppression = await mutedContext.ActionSuppressions.SingleAsync();
            Assert.True(suppression.IsActive);
            Assert.Equal(seed.NeedlyUser.Id, suppression.NeedlyUserId);
        }

        Assert.True(await service.UndoAsync(
            seed.NeedlyUser.Id,
            change.UndoId,
            CancellationToken.None));

        await using var verification = database.CreateContext();
        Assert.Equal(ActionState.Open, (await verification.Actions.SingleAsync()).State);
        Assert.False((await verification.ActionSuppressions.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task MuteAsync_TeamAction_HidesOnlyMutingMemberAndUndoRestoresVisibility()
    {
        await using var database = await LifecycleTestDatabase.CreateAsync();
        var installation = Installation.Create(TestData.InstallationId, 501, "octocat", TestData.CreatedAt);
        var repository = TestData.CreateRepository();
        var firstGitHubUser = TestData.CreateGitHubUser(gitHubUserId: 901);
        var secondGitHubUser = TestData.CreateGitHubUser(Guid.NewGuid(), 902);
        var firstNeedlyUser = NeedlyUser.Create(
            Guid.NewGuid(), firstGitHubUser.Id, "first@example.test", "First", TestData.CreatedAt);
        var secondNeedlyUser = NeedlyUser.Create(
            Guid.NewGuid(), secondGitHubUser.Id, "second@example.test", "Second", TestData.CreatedAt);
        var team = Team.Create(
            Guid.NewGuid(), installation.Id, 801, "maintainers", "Maintainers", TestData.CreatedAt);
        var action = NeedlyAction.CreateForTeam(
            Guid.NewGuid(),
            ActionType.Review,
            repository,
            team,
            GitHubSubjectType.PullRequest,
            42,
            "https://github.com/octocat/needly/pull/42",
            "Review team request",
            null,
            "Team review requested",
            TestData.CreatedAt);
        await using (var context = database.CreateContext())
        {
            context.AddRange(
                installation,
                repository,
                firstGitHubUser,
                secondGitHubUser,
                firstNeedlyUser,
                secondNeedlyUser,
                team,
                InstallationMember.Create(Guid.NewGuid(), installation.Id, firstGitHubUser.Id, TestData.CreatedAt),
                InstallationMember.Create(Guid.NewGuid(), installation.Id, secondGitHubUser.Id, TestData.CreatedAt),
                TeamMember.Create(Guid.NewGuid(), team.Id, firstGitHubUser.Id, TestData.CreatedAt),
                TeamMember.Create(Guid.NewGuid(), team.Id, secondGitHubUser.Id, TestData.CreatedAt),
                action);
            await context.SaveChangesAsync();
        }
        var service = CreateService(database, new RecordingBroadcaster());

        var change = await service.MuteAsync(firstNeedlyUser.Id, action.Id, CancellationToken.None);

        Assert.NotNull(change);
        await using (var mutedContext = database.CreateContext())
        {
            Assert.Equal(ActionState.Open, (await mutedContext.Actions.SingleAsync()).State);
            Assert.Empty(await new InboxVisibilityService(mutedContext, TimeProvider.System)
                .GetVisibleAsync(firstNeedlyUser.Id, CancellationToken.None));
        }
        await using (var teammateContext = database.CreateContext())
        {
            Assert.Single(await new InboxVisibilityService(teammateContext, TimeProvider.System)
                .GetVisibleAsync(secondNeedlyUser.Id, CancellationToken.None));
        }

        Assert.True(await service.UndoAsync(firstNeedlyUser.Id, change.UndoId, CancellationToken.None));
        await using var restoredContext = database.CreateContext();
        Assert.Single(await new InboxVisibilityService(restoredContext, TimeProvider.System)
            .GetVisibleAsync(firstNeedlyUser.Id, CancellationToken.None));
    }

    [Fact]
    public async Task UndoAsync_AnotherUserOwnsUndo_DoesNotRestoreAction()
    {
        await using var database = await LifecycleTestDatabase.CreateAsync();
        var seed = await SeedAsync(database);
        var service = CreateService(database, new RecordingBroadcaster());
        var change = await service.ArchiveAsync(
            seed.NeedlyUser.Id,
            seed.Action.Id,
            CancellationToken.None);

        var restored = await service.UndoAsync(Guid.NewGuid(), change!.UndoId, CancellationToken.None);

        Assert.False(restored);
        await using var verification = database.CreateContext();
        Assert.Equal(ActionState.Archived, (await verification.Actions.SingleAsync()).State);
    }

    private static ActionLifecycleService CreateService(
        LifecycleTestDatabase database,
        IActionChangeBroadcaster broadcaster) =>
        new(database, new MutableTimeProvider(TestData.CreatedAt.AddHours(1)), broadcaster);

    private static async Task<LifecycleSeed> SeedAsync(LifecycleTestDatabase database)
    {
        var installation = Installation.Create(
            TestData.InstallationId,
            501,
            "octocat",
            TestData.CreatedAt);
        var repository = TestData.CreateRepository();
        var gitHubUser = TestData.CreateGitHubUser();
        var needlyUser = NeedlyUser.Create(
            Guid.NewGuid(),
            gitHubUser.Id,
            "octocat@example.test",
            "Octocat",
            TestData.CreatedAt);
        var action = TestData.CreateAction(repository: repository, assignee: gitHubUser);
        await using var context = database.CreateContext();
        context.AddRange(
            installation,
            repository,
            gitHubUser,
            needlyUser,
            InstallationMember.Create(
                Guid.NewGuid(),
                installation.Id,
                gitHubUser.Id,
                TestData.CreatedAt),
            action);
        await context.SaveChangesAsync();
        return new LifecycleSeed(installation, needlyUser, action);
    }

    private sealed record LifecycleSeed(
        Installation Installation,
        NeedlyUser NeedlyUser,
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
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class LifecycleTestDatabase : IDbContextFactory<NeedlyDbContext>, IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly DbContextOptions<NeedlyDbContext> options;

        private LifecycleTestDatabase(
            SqliteConnection connection,
            DbContextOptions<NeedlyDbContext> options)
        {
            this.connection = connection;
            this.options = options;
        }

        public static async Task<LifecycleTestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<NeedlyDbContext>()
                .UseSqlite(connection)
                .Options;
            await using var context = new NeedlyDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new LifecycleTestDatabase(connection, options);
        }

        public NeedlyDbContext CreateContext() => new(options);

        public NeedlyDbContext CreateDbContext() => CreateContext();

        public Task<NeedlyDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateContext());

        public async ValueTask DisposeAsync() => await connection.DisposeAsync();
    }
}