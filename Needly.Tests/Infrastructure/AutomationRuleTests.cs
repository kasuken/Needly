using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Needly.Application.Actions;
using Needly.Application.GitHub;
using Needly.Domain;
using Needly.Infrastructure;
using Needly.Infrastructure.Actions;
using Needly.Infrastructure.GitHub;
using Xunit;

namespace Needly.Tests.Infrastructure;

public sealed class AutomationRuleTests
{
    [Fact]
    public async Task RuleCrud_EnableDisableDeleteAndReorder_AreUserIsolated()
    {
        await using var database = await RuleTestDatabase.CreateAsync();
        var seed = await SeedAsync(database);
        var service = CreateService(database);
        var first = await service.CreateAsync(
            seed.User.Id, "First", new ActionFilter(), RuleEffect.Pin, null, CancellationToken.None);
        var second = await service.CreateAsync(
            seed.User.Id, "Second", new ActionFilter(), RuleEffect.MarkFyi, null, CancellationToken.None);

        Assert.True(await service.MoveAsync(seed.User.Id, second.Id, -1, CancellationToken.None));
        Assert.True(await service.SetEnabledAsync(seed.User.Id, first.Id, false, CancellationToken.None));
        Assert.False(await service.SetEnabledAsync(Guid.NewGuid(), first.Id, true, CancellationToken.None));
        var reordered = await service.GetAsync(seed.User.Id, CancellationToken.None);
        Assert.Equal(["Second", "First"], reordered.Select(rule => rule.Name).ToArray());
        Assert.False(reordered[1].IsEnabled);
        Assert.True(await service.DeleteAsync(seed.User.Id, second.Id, CancellationToken.None));
        Assert.False(await service.DeleteAsync(Guid.NewGuid(), first.Id, CancellationToken.None));
        Assert.Equal(first.Id, Assert.Single(await service.GetAsync(seed.User.Id, CancellationToken.None)).Id);
    }

    [Theory]
    [InlineData(RuleEffect.AutoArchive)]
    [InlineData(RuleEffect.Mute)]
    [InlineData(RuleEffect.Snooze)]
    [InlineData(RuleEffect.MarkFyi)]
    [InlineData(RuleEffect.Pin)]
    public async Task EvaluateAsync_EachEffect_PersistsPerUserDispositionAndExplanation(RuleEffect effect)
    {
        await using var database = await RuleTestDatabase.CreateAsync();
        var seed = await SeedAsync(database);
        var service = CreateService(database);
        await service.CreateAsync(
            seed.User.Id,
            $"Apply {effect}",
            new ActionFilter { Types = [ActionType.Review] },
            effect,
            effect == RuleEffect.Snooze ? TimeSpan.FromHours(4) : null,
            CancellationToken.None);
        var now = TestData.CreatedAt.AddDays(1);

        await using var context = database.CreateContext();
        var action = await context.Actions.SingleAsync();
        var executed = await new AutomationRuleEvaluator(new FixedTimeProvider(now)).EvaluateAsync(
            context, Event(Guid.NewGuid(), now), [action], CancellationToken.None);
        await context.SaveChangesAsync();

        var disposition = await context.ActionDispositions.SingleAsync();
        Assert.Equal(1, executed);
        Assert.Equal(effect == RuleEffect.AutoArchive, disposition.IsArchived);
        Assert.Equal(effect == RuleEffect.Mute, disposition.IsMuted);
        Assert.Equal(effect == RuleEffect.MarkFyi, disposition.IsFyi);
        Assert.Equal(effect == RuleEffect.Pin, disposition.IsPinned);
        Assert.Equal(effect == RuleEffect.Snooze ? now.AddHours(4) : null, disposition.SnoozedUntil);
        Assert.Equal(effect == RuleEffect.Mute ? 1 : 0, await context.ActionSuppressions.CountAsync());
        var execution = await context.RuleExecutions.SingleAsync();
        Assert.Equal(effect, execution.Effect);
        Assert.Contains($"Rule 'Apply {effect}' matched Review", execution.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvaluateAsync_AllMatchingRulesExecuteInOrderAndAreIdempotentPerEvent()
    {
        await using var database = await RuleTestDatabase.CreateAsync();
        var seed = await SeedAsync(database);
        var service = CreateService(database);
        await service.CreateAsync(seed.User.Id, "Pin first", new ActionFilter(), RuleEffect.Pin, null, CancellationToken.None);
        await service.CreateAsync(seed.User.Id, "FYI second", new ActionFilter(), RuleEffect.MarkFyi, null, CancellationToken.None);
        var storedEvent = Event(Guid.NewGuid(), TestData.CreatedAt.AddHours(1));

        await using var context = database.CreateContext();
        var action = await context.Actions.SingleAsync();
        var evaluator = new AutomationRuleEvaluator(new FixedTimeProvider(TestData.CreatedAt.AddHours(1)));
        Assert.Equal(2, await evaluator.EvaluateAsync(context, storedEvent, [action], CancellationToken.None));
        await context.SaveChangesAsync();
        Assert.Equal(0, await evaluator.EvaluateAsync(context, storedEvent, [action], CancellationToken.None));
        await context.SaveChangesAsync();

        var disposition = await context.ActionDispositions.SingleAsync();
        Assert.True(disposition.IsPinned);
        Assert.True(disposition.IsFyi);
        Assert.Equal(["Pin first", "FYI second"],
            (await context.RuleExecutions.OrderBy(execution => execution.RuleOrder).ToListAsync())
                .Select(execution => execution.RuleName)
                .ToArray());
        Assert.Equal(2, await context.RuleExecutions.CountAsync());
    }

    [Fact]
    public async Task EvaluateAsync_DisabledMatchingRule_DoesNotExecuteOrCreateDisposition()
    {
        await using var database = await RuleTestDatabase.CreateAsync();
        var seed = await SeedAsync(database);
        var service = CreateService(database);
        var rule = await service.CreateAsync(
            seed.User.Id,
            "Disabled pin",
            new ActionFilter { Types = [ActionType.Review] },
            RuleEffect.Pin,
            null,
            CancellationToken.None);
        Assert.True(await service.SetEnabledAsync(
            seed.User.Id, rule.Id, false, CancellationToken.None));

        await using var context = database.CreateContext();
        var action = await context.Actions.SingleAsync();
        var executed = await new AutomationRuleEvaluator(
            new FixedTimeProvider(TestData.CreatedAt.AddHours(1))).EvaluateAsync(
                context,
                Event(Guid.NewGuid(), TestData.CreatedAt.AddHours(1)),
                [action],
                CancellationToken.None);
        await context.SaveChangesAsync();

        Assert.Equal(0, executed);
        Assert.Empty(await context.ActionDispositions.ToListAsync());
        Assert.Empty(await context.RuleExecutions.ToListAsync());
    }

    [Fact]
    public async Task EvaluateAsync_BotRuleMarksFyi()
    {
        await using var database = await RuleTestDatabase.CreateAsync();
        var seed = await SeedAsync(database);
        await CreateService(database).CreateAsync(
            seed.User.Id,
            "Bots are FYI",
            new ActionFilter { BotInvolvement = BotInvolvementFilter.OnlyBots },
            RuleEffect.MarkFyi,
            null,
            CancellationToken.None);
        await using var context = database.CreateContext();
        var action = await context.Actions.SingleAsync();
        action.UpdateFilterMetadata("dependabot[bot]", true);

        await new AutomationRuleEvaluator(new FixedTimeProvider(TestData.CreatedAt.AddHours(1))).EvaluateAsync(
            context, Event(Guid.NewGuid(), TestData.CreatedAt.AddHours(1)), [action], CancellationToken.None);
        await context.SaveChangesAsync();

        Assert.True((await context.ActionDispositions.SingleAsync()).IsFyi);
    }

    [Fact]
    public async Task EvaluateAsync_TeamMembersReceiveIndependentEffectsAndOutsiderReceivesNone()
    {
        await using var database = await RuleTestDatabase.CreateAsync();
        var seed = await SeedAsync(database, teamAction: true);
        var second = await AddTeamMemberAsync(database, seed, "second@example.test", 202, includeInstallation: true);
        var outsider = await AddTeamMemberAsync(database, seed, "outsider@example.test", 203, includeInstallation: false);
        var service = CreateService(database);
        await service.CreateAsync(seed.User.Id, "Archive mine", new ActionFilter(), RuleEffect.AutoArchive, null, CancellationToken.None);
        await service.CreateAsync(second.Id, "Pin mine", new ActionFilter(), RuleEffect.Pin, null, CancellationToken.None);
        await service.CreateAsync(outsider.Id, "Mute outsider", new ActionFilter(), RuleEffect.Mute, null, CancellationToken.None);

        await using var context = database.CreateContext();
        var action = await context.Actions.SingleAsync();
        await new AutomationRuleEvaluator(new FixedTimeProvider(TestData.CreatedAt.AddHours(1))).EvaluateAsync(
            context, Event(Guid.NewGuid(), TestData.CreatedAt.AddHours(1)), [action], CancellationToken.None);
        await context.SaveChangesAsync();

        var dispositions = await context.ActionDispositions.OrderBy(item => item.NeedlyUserId).ToListAsync();
        Assert.Equal(2, dispositions.Count);
        Assert.Contains(dispositions, item => item.NeedlyUserId == seed.User.Id && item.IsArchived && !item.IsPinned);
        Assert.Contains(dispositions, item => item.NeedlyUserId == second.Id && item.IsPinned && !item.IsArchived);
        Assert.DoesNotContain(dispositions, item => item.NeedlyUserId == outsider.Id);
        Assert.Equal(ActionState.Open, action.State);
    }

    [Fact]
    public async Task HandleAsync_CreateAndUpdate_InvokesRulesInsideTransactionAndDoesNotRepeatEvent()
    {
        await using var database = await RuleTestDatabase.CreateAsync();
        var seed = await SeedAsync(database, createAction: false);
        await CreateService(database).CreateAsync(
            seed.User.Id, "Pin changes", new ActionFilter(), RuleEffect.Pin, null, CancellationToken.None);
        var detector = new CreateOrUpdateDetector();
        var evaluator = new AutomationRuleEvaluator(new FixedTimeProvider(TestData.CreatedAt.AddHours(3)));
        var handler = new GitHubActionEventHandler(
            database, [detector], NullLogger<GitHubActionEventHandler>.Instance, null, evaluator);
        var createdEvent = await AddEventAsync(database, "opened", 1);
        var updatedEvent = await AddEventAsync(database, "synchronize", 2);

        await handler.HandleAsync(createdEvent, CancellationToken.None);
        await handler.HandleAsync(createdEvent, CancellationToken.None);
        await handler.HandleAsync(updatedEvent, CancellationToken.None);

        await using var verification = database.CreateContext();
        Assert.Single(await verification.Actions.ToListAsync());
        Assert.Equal("Updated", (await verification.Actions.SingleAsync()).Title);
        Assert.Equal(2, await verification.RuleExecutions.CountAsync());
        Assert.True((await verification.ActionDispositions.SingleAsync()).IsPinned);
        Assert.Equal(2, await verification.ActionEventReceipts.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_InvalidRule_RollsBackActionAndDetectorReceipt()
    {
        await using var database = await RuleTestDatabase.CreateAsync();
        var seed = await SeedAsync(database, createAction: false);
        await using (var setup = database.CreateContext())
        {
            setup.AutomationRules.Add(AutomationRule.Create(
                Guid.NewGuid(), seed.User.Id, "Invalid", "{\"schemaVersion\":99}",
                RuleEffect.Pin, null, true, 0, TestData.CreatedAt));
            await setup.SaveChangesAsync();
        }

        var handler = new GitHubActionEventHandler(
            database,
            [new CreateOrUpdateDetector()],
            NullLogger<GitHubActionEventHandler>.Instance,
            null,
            new AutomationRuleEvaluator(new FixedTimeProvider(TestData.CreatedAt.AddHours(1))));

        var storedEvent = await AddEventAsync(database, "opened", 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => handler.HandleAsync(
            storedEvent, CancellationToken.None));

        await using var verification = database.CreateContext();
        Assert.Empty(await verification.Actions.ToListAsync());
        Assert.Empty(await verification.ActionEventReceipts.ToListAsync());
        Assert.Empty(await verification.ActionDispositions.ToListAsync());
        Assert.Empty(await verification.RuleExecutions.ToListAsync());
    }

    private static AutomationRuleService CreateService(RuleTestDatabase database) =>
        new(database, TimeProvider.System, new NullBroadcaster());

    private static async Task<RuleSeed> SeedAsync(
        RuleTestDatabase database,
        bool teamAction = false,
        bool createAction = true)
    {
        await using var context = database.CreateContext();
        var installation = Installation.Create(TestData.InstallationId, 501, "octo-org", TestData.CreatedAt);
        var repository = TestData.CreateRepository(owner: "octo-org");
        var gitHubUser = TestData.CreateGitHubUser();
        var user = NeedlyUser.Create(Guid.NewGuid(), gitHubUser.Id, "first@example.test", "First", TestData.CreatedAt);
        var team = Team.Create(Guid.NewGuid(), installation.Id, 301, "maintainers", "Maintainers", TestData.CreatedAt);
        context.AddRange(
            installation,
            repository,
            gitHubUser,
            user,
            InstallationMember.Create(Guid.NewGuid(), installation.Id, gitHubUser.Id, TestData.CreatedAt),
            team,
            TeamMember.Create(Guid.NewGuid(), team.Id, gitHubUser.Id, TestData.CreatedAt));
        if (createAction)
        {
            context.Actions.Add(teamAction
                ? NeedlyAction.CreateForTeam(
                    Guid.NewGuid(), ActionType.Review, repository, team, GitHubSubjectType.PullRequest, 42,
                    "https://github.com/octo-org/needly/pull/42", "Review", null, "Reason", TestData.CreatedAt)
                : TestData.CreateAction(repository: repository, assignee: gitHubUser));
        }

        await context.SaveChangesAsync();
        return new RuleSeed(installation, repository, gitHubUser, user, team);
    }

    private static async Task<NeedlyUser> AddTeamMemberAsync(
        RuleTestDatabase database,
        RuleSeed seed,
        string email,
        long gitHubId,
        bool includeInstallation)
    {
        await using var context = database.CreateContext();
        var gitHubUser = TestData.CreateGitHubUser(Guid.NewGuid(), gitHubId);
        var user = NeedlyUser.Create(Guid.NewGuid(), gitHubUser.Id, email, email, TestData.CreatedAt);
        context.AddRange(
            gitHubUser,
            user,
            TeamMember.Create(Guid.NewGuid(), seed.Team.Id, gitHubUser.Id, TestData.CreatedAt));
        if (includeInstallation)
        {
            context.InstallationMembers.Add(InstallationMember.Create(
                Guid.NewGuid(), seed.Installation.Id, gitHubUser.Id, TestData.CreatedAt));
        }

        await context.SaveChangesAsync();
        return user;
    }

    private static GitHubStoredEvent Event(Guid id, DateTimeOffset receivedAt) =>
        new(id, 501, 101, "pull_request", "opened", "{}", receivedAt);

    private static async Task<GitHubStoredEvent> AddEventAsync(
        RuleTestDatabase database,
        string action,
        int minutes)
    {
        await using var context = database.CreateContext();
        var id = Guid.NewGuid();
        var receivedAt = TestData.CreatedAt.AddMinutes(minutes);
        var rawEvent = RawEvent.CreateDelivery(
            id,
            TestData.InstallationId,
            501,
            TestData.RepositoryId,
            101,
            $"delivery-{id:N}",
            "pull_request",
            action,
            "{}",
            receivedAt);
        context.RawEvents.Add(rawEvent);
        await context.SaveChangesAsync();
        return Event(id, receivedAt) with { Action = action };
    }

    private sealed class CreateOrUpdateDetector : IGitHubActionDetector
    {
        public string Key => "test.rules";

        public int Order => 1;

        public Task<IReadOnlyList<GitHubActionOperation>> DetectAsync(
            GitHubActionDetectionContext context,
            CancellationToken cancellationToken)
        {
            var target = new GitHubActionTarget(
                ActionType.Review, GitHubSubjectType.PullRequest, 42, ActionAssigneeType.User, 201);
            GitHubActionOperation operation = context.Event.Action == "opened"
                ? new CreateGitHubActionOperation(
                    target,
                    "https://github.com/octo-org/needly/pull/42",
                    "Created",
                    null,
                    "Reason",
                    context.Event.ReceivedAt)
                : new UpdateGitHubActionOperation(
                    target, "Updated", null, "Updated reason", context.Event.ReceivedAt);
            return Task.FromResult<IReadOnlyList<GitHubActionOperation>>([operation]);
        }
    }

    private sealed record RuleSeed(
        Installation Installation,
        Repository Repository,
        GitHubUser GitHubUser,
        NeedlyUser User,
        Team Team);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NullBroadcaster : IActionChangeBroadcaster
    {
        public event Action? Changed { add { } remove { } }

        public void Publish()
        {
        }
    }

    private sealed class RuleTestDatabase : IDbContextFactory<NeedlyDbContext>, IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly DbContextOptions<NeedlyDbContext> options;

        private RuleTestDatabase(SqliteConnection connection, DbContextOptions<NeedlyDbContext> options)
        {
            this.connection = connection;
            this.options = options;
        }

        public static async Task<RuleTestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<NeedlyDbContext>().UseSqlite(connection).Options;
            await using var context = new NeedlyDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new RuleTestDatabase(connection, options);
        }

        public NeedlyDbContext CreateContext() => new(options);

        public NeedlyDbContext CreateDbContext() => CreateContext();

        public Task<NeedlyDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateContext());

        public async ValueTask DisposeAsync() => await connection.DisposeAsync();
    }
}