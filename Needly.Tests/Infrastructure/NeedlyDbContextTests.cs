using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Needly.Domain;
using Needly.Infrastructure;
using Xunit;

namespace Needly.Tests.Infrastructure;

public sealed class NeedlyDbContextTests
{
    [Fact]
    public async Task SaveChanges_DuplicateWebhookDeliveryId_ThrowsUniqueConstraintViolation()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        database.Context.Installations.Add(CreateInstallation());
        database.Context.RawEvents.AddRange(
            CreateRawEvent(Guid.Parse("10000000-0000-0000-0000-000000000001"), "delivery-duplicate"),
            CreateRawEvent(Guid.Parse("10000000-0000-0000-0000-000000000002"), "delivery-duplicate"));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => database.Context.SaveChangesAsync());

        var sqliteException = Assert.IsType<SqliteException>(exception.InnerException);
        Assert.Equal(19, sqliteException.SqliteErrorCode);
        Assert.Equal(2067, sqliteException.SqliteExtendedErrorCode);
        Assert.Contains("RawEvents.DeliveryId", sqliteException.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ActionState.Open, ActionState.Open)]
    [InlineData(ActionState.Open, ActionState.Snoozed)]
    [InlineData(ActionState.Snoozed, ActionState.Open)]
    [InlineData(ActionState.Snoozed, ActionState.Snoozed)]
    public async Task SaveChanges_DuplicateActiveActionKey_ThrowsUniqueConstraintViolation(
        ActionState firstState,
        ActionState secondState)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var (repository, assignee) = await SeedActionDependenciesAsync(database.Context);
        var first = TestData.CreateAction(
            id: Guid.Parse("20000000-0000-0000-0000-000000000001"),
            repository: repository,
            assignee: assignee);
        var second = TestData.CreateAction(
            id: Guid.Parse("20000000-0000-0000-0000-000000000002"),
            repository: repository,
            assignee: assignee);
        first.ChangeState(firstState, TestData.CreatedAt.AddMinutes(1));
        second.ChangeState(secondState, TestData.CreatedAt.AddMinutes(1));
        database.Context.Actions.AddRange(first, second);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => database.Context.SaveChangesAsync());

        var sqliteException = Assert.IsType<SqliteException>(exception.InnerException);
        Assert.Equal(first.Key, second.Key);
        Assert.Equal(19, sqliteException.SqliteErrorCode);
        Assert.Equal(2067, sqliteException.SqliteExtendedErrorCode);
        Assert.Contains("Actions", sqliteException.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ActionState.Archived)]
    [InlineData(ActionState.Muted)]
    [InlineData(ActionState.Done)]
    public async Task SaveChanges_TerminalHistoricalActionAndActiveActionWithSameKey_PersistsBoth(
        ActionState terminalState)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var (repository, assignee) = await SeedActionDependenciesAsync(database.Context);
        var historical = TestData.CreateAction(
            id: Guid.Parse("30000000-0000-0000-0000-000000000001"),
            repository: repository,
            assignee: assignee);
        historical.ChangeState(terminalState, TestData.CreatedAt.AddMinutes(1));
        var active = TestData.CreateAction(
            id: Guid.Parse("30000000-0000-0000-0000-000000000002"),
            repository: repository,
            assignee: assignee);
        database.Context.Actions.AddRange(historical, active);

        var savedCount = await database.Context.SaveChangesAsync();
        var persisted = await database.Context.Actions
            .AsNoTracking()
            .OrderBy(action => action.Id)
            .Select(action => new { action.Id, action.Key, action.State })
            .ToListAsync();

        Assert.Equal(2, savedCount);
        Assert.Equal(2, persisted.Count);
        Assert.All(persisted, action => Assert.Equal(active.Key, action.Key));
        Assert.Contains(persisted, action => action.Id == historical.Id && action.State == terminalState);
        Assert.Contains(persisted, action => action.Id == active.Id && action.State == ActionState.Open);
    }

    [Fact]
    public async Task ActionsQuery_OpenActionsForUser_ReturnsOnlyMatchingUserOpenRecords()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var (repository, targetUser) = await SeedActionDependenciesAsync(database.Context);
        var otherUser = TestData.CreateGitHubUser(
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            202);
        var team = Team.Create(
            Guid.Parse("40000000-0000-0000-0000-000000000002"),
            TestData.InstallationId,
            301,
            "maintainers",
            "Maintainers",
            TestData.CreatedAt);
        database.Context.AddRange(otherUser, team);

        var expected = TestData.CreateAction(
            id: Guid.Parse("40000000-0000-0000-0000-000000000010"),
            repository: repository,
            assignee: targetUser,
            type: ActionType.Review,
            subjectNumber: 10);
        var snoozedForTarget = TestData.CreateAction(
            id: Guid.Parse("40000000-0000-0000-0000-000000000011"),
            repository: repository,
            assignee: targetUser,
            type: ActionType.Respond,
            subjectNumber: 11);
        snoozedForTarget.ChangeState(ActionState.Snoozed, TestData.CreatedAt.AddMinutes(1));
        var archivedForTarget = TestData.CreateAction(
            id: Guid.Parse("40000000-0000-0000-0000-000000000012"),
            repository: repository,
            assignee: targetUser,
            type: ActionType.Fix,
            subjectNumber: 12);
        archivedForTarget.ChangeState(ActionState.Archived, TestData.CreatedAt.AddMinutes(1));
        var openForOtherUser = TestData.CreateAction(
            id: Guid.Parse("40000000-0000-0000-0000-000000000013"),
            repository: repository,
            assignee: otherUser,
            type: ActionType.Resolve,
            subjectNumber: 13);
        var openForTeam = NeedlyAction.CreateForTeam(
            Guid.Parse("40000000-0000-0000-0000-000000000014"),
            ActionType.Merge,
            repository,
            team,
            GitHubSubjectType.PullRequest,
            14,
            "https://github.com/octocat/needly/pull/14",
            "Merge the change",
            null,
            "The checks have passed",
            TestData.CreatedAt);
        database.Context.Actions.AddRange(
            expected,
            snoozedForTarget,
            archivedForTarget,
            openForOtherUser,
            openForTeam);
        await database.Context.SaveChangesAsync();

        var actualIds = await database.Context.Actions
            .AsNoTracking()
            .Where(action =>
                action.AssigneeType == ActionAssigneeType.User &&
                action.AssigneeId == targetUser.Id &&
                action.State == ActionState.Open)
            .Select(action => action.Id)
            .ToListAsync();

        Assert.Equal([expected.Id], actualIds);
        Assert.DoesNotContain(snoozedForTarget.Id, actualIds);
        Assert.DoesNotContain(archivedForTarget.Id, actualIds);
        Assert.DoesNotContain(openForOtherUser.Id, actualIds);
        Assert.DoesNotContain(openForTeam.Id, actualIds);
    }

    private static Installation CreateInstallation() =>
        Installation.Create(TestData.InstallationId, 501, "octocat", TestData.CreatedAt);

    private static RawEvent CreateRawEvent(Guid id, string deliveryId) =>
        RawEvent.Create(
            id,
            TestData.InstallationId,
            null,
            deliveryId,
            "pull_request",
            "opened",
            "{}",
            TestData.CreatedAt);

    private static async Task<(Repository Repository, GitHubUser Assignee)> SeedActionDependenciesAsync(
        NeedlyDbContext context)
    {
        var installation = CreateInstallation();
        var repository = TestData.CreateRepository();
        var assignee = TestData.CreateGitHubUser();
        context.AddRange(installation, repository, assignee);
        await context.SaveChangesAsync();
        return (repository, assignee);
    }

    private sealed class SqliteTestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private SqliteTestDatabase(SqliteConnection connection, NeedlyDbContext context)
        {
            this.connection = connection;
            Context = context;
        }

        internal NeedlyDbContext Context { get; }

        internal static async Task<SqliteTestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<NeedlyDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new NeedlyDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new SqliteTestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
