using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Needly.Application.Actions;
using Needly.Application.GitHub;
using Needly.Domain;
using Needly.Infrastructure;
using Needly.Infrastructure.Actions;
using Xunit;

namespace Needly.Tests.Infrastructure;

public sealed class SavedViewServiceTests
{
    [Fact]
    public async Task GetAsync_BuiltInViews_AreAlwaysAvailableWithExplicitAuthorizedCounts()
    {
        await using var database = await ViewTestDatabase.CreateAsync();
        var user = await SeedUserAsync(database, "first@example.test", 201);
        var inbox = new FakeInboxVisibilityService(new Dictionary<Guid, IReadOnlyList<VisibleAction>>
        {
            [user.Id] =
            [
                Visible(ActionType.Review, ActionAssigneeScope.Me, TimeSpan.FromHours(2)),
                Visible(ActionType.Fix, ActionAssigneeScope.MyTeam, TimeSpan.FromHours(3)),
                Visible(ActionType.FollowUp, ActionAssigneeScope.Me, TimeSpan.FromDays(2)),
                Visible(ActionType.FYI, ActionAssigneeScope.Me, TimeSpan.FromHours(1))
            ]
        });
        var service = CreateService(database, inbox);

        var views = await service.GetAsync(user.Id, CancellationToken.None);

        Assert.Equal(["Needs me", "Needs my team", "Waiting on others", "FYI"],
            views.Select(view => view.Name).ToArray());
        Assert.Equal([2, 1, 1, 1], views.Select(view => view.OpenCount).ToArray());
        Assert.All(views, view => Assert.True(view.IsBuiltIn));
        Assert.Equal(ActionAssigneeScope.Me, views[0].Filter.AssigneeScope);
        Assert.Equal(ActionAssigneeScope.MyTeam, views[1].Filter.AssigneeScope);
        Assert.Equal(TimeSpan.FromDays(1), views[2].Filter.WaitingAtLeast);
        Assert.Equal([ActionType.FYI, ActionType.Monitor], views[3].Filter.Types);
    }

    [Fact]
    public async Task CreateUpdateDeleteAsync_EnforcesPerUserIsolationAndNormalizedNameUniqueness()
    {
        await using var database = await ViewTestDatabase.CreateAsync();
        var first = await SeedUserAsync(database, "first@example.test", 201);
        var second = await SeedUserAsync(database, "second@example.test", 202);
        var service = CreateService(database, new FakeInboxVisibilityService(
            new Dictionary<Guid, IReadOnlyList<VisibleAction>>()));

        var firstView = await service.CreateAsync(
            first.Id, "Review queue", new ActionFilter { Types = [ActionType.Review] }, CancellationToken.None);
        var secondView = await service.CreateAsync(
            second.Id, "Review queue", new ActionFilter { Types = [ActionType.Fix] }, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            first.Id, "  REVIEW QUEUE  ", new ActionFilter(), CancellationToken.None));
        Assert.False(await service.UpdateAsync(
            second.Id, firstView.Id!.Value, "Changed", new ActionFilter(), CancellationToken.None));
        Assert.False(await service.DeleteAsync(second.Id, firstView.Id.Value, CancellationToken.None));
        Assert.True(await service.UpdateAsync(
            first.Id,
            firstView.Id.Value,
            "My reviews",
            new ActionFilter { Organizations = ["octo-org"] },
            CancellationToken.None));
        Assert.True(await service.DeleteAsync(first.Id, firstView.Id.Value, CancellationToken.None));
        Assert.Equal("Review queue", secondView.Name);
        Assert.DoesNotContain(await service.GetAsync(first.Id, CancellationToken.None), view => !view.IsBuiltIn);
        Assert.Single(await service.GetAsync(second.Id, CancellationToken.None), view => !view.IsBuiltIn);
    }

    [Fact]
    public async Task MoveAsync_ReordersOnlyTheOwningUsersViews()
    {
        await using var database = await ViewTestDatabase.CreateAsync();
        var first = await SeedUserAsync(database, "first@example.test", 201);
        var second = await SeedUserAsync(database, "second@example.test", 202);
        var service = CreateService(database, new FakeInboxVisibilityService(
            new Dictionary<Guid, IReadOnlyList<VisibleAction>>()));
        var alpha = await service.CreateAsync(first.Id, "Alpha", new ActionFilter(), CancellationToken.None);
        var beta = await service.CreateAsync(first.Id, "Beta", new ActionFilter(), CancellationToken.None);
        var other = await service.CreateAsync(second.Id, "Other", new ActionFilter(), CancellationToken.None);

        Assert.True(await service.MoveAsync(first.Id, beta.Id!.Value, -1, CancellationToken.None));
        Assert.False(await service.MoveAsync(second.Id, beta.Id.Value, -1, CancellationToken.None));

        var firstViews = (await service.GetAsync(first.Id, CancellationToken.None)).Where(view => !view.IsBuiltIn).ToArray();
        var secondViews = (await service.GetAsync(second.Id, CancellationToken.None)).Where(view => !view.IsBuiltIn).ToArray();
        Assert.Equal(["Beta", "Alpha"], firstViews.Select(view => view.Name).ToArray());
        Assert.Equal([0, 1], firstViews.Select(view => view.SortOrder).ToArray());
        Assert.Equal(other.Id, Assert.Single(secondViews).Id);
        Assert.Equal(alpha.Id, firstViews[1].Id);
    }

    [Fact]
    public async Task GetAsync_CustomView_UsesSharedMatcherForAuthorizedCount()
    {
        await using var database = await ViewTestDatabase.CreateAsync();
        var user = await SeedUserAsync(database, "first@example.test", 201);
        var inbox = new FakeInboxVisibilityService(new Dictionary<Guid, IReadOnlyList<VisibleAction>>
        {
            [user.Id] =
            [
                Visible(ActionType.Review, ActionAssigneeScope.Me, TimeSpan.FromHours(10), "octo-org", "octocat"),
                Visible(ActionType.Review, ActionAssigneeScope.Me, TimeSpan.FromHours(2), "other-org", "octocat"),
                Visible(ActionType.Fix, ActionAssigneeScope.Me, TimeSpan.FromHours(10), "octo-org", "octocat")
            ]
        });
        var service = CreateService(database, inbox);
        await service.CreateAsync(user.Id, "Focused", new ActionFilter
        {
            Types = [ActionType.Review],
            Organizations = ["octo-org"],
            Authors = ["octocat"],
            WaitingAtLeast = TimeSpan.FromHours(8)
        }, CancellationToken.None);

        var view = Assert.Single(await service.GetAsync(user.Id, CancellationToken.None), item => !item.IsBuiltIn);

        Assert.Equal(1, view.OpenCount);
    }

    private static SavedViewService CreateService(
        ViewTestDatabase database,
        IInboxVisibilityService inbox) =>
        new(database, inbox, TimeProvider.System, new NullBroadcaster());

    private static async Task<NeedlyUser> SeedUserAsync(
        ViewTestDatabase database,
        string email,
        long gitHubId)
    {
        await using var context = database.CreateContext();
        var gitHubUser = TestData.CreateGitHubUser(Guid.NewGuid(), gitHubId);
        var user = NeedlyUser.Create(Guid.NewGuid(), gitHubUser.Id, email, email, TestData.CreatedAt);
        context.AddRange(gitHubUser, user);
        await context.SaveChangesAsync();
        return user;
    }

    private static VisibleAction Visible(
        ActionType type,
        ActionAssigneeScope scope,
        TimeSpan waiting,
        string owner = "octo-org",
        string author = "octocat") =>
        new(
            Guid.NewGuid(), owner, "needly", "Subject", 42, GitHubSubjectType.PullRequest,
            $"https://github.com/{owner}/needly/pull/42", type, ActionState.Open, "Reason", null,
            scope == ActionAssigneeScope.Me ? "@octocat" : "Maintainers (@maintainers)",
            "Trigger", TestData.CreatedAt, waiting, false, null, author, scope, false, false);

    private sealed class FakeInboxVisibilityService(
        IReadOnlyDictionary<Guid, IReadOnlyList<VisibleAction>> actions) : IInboxVisibilityService
    {
        public Task<IReadOnlyList<VisibleAction>> GetVisibleAsync(Guid needlyUserId, CancellationToken cancellationToken) =>
            Task.FromResult(actions.GetValueOrDefault(needlyUserId, []));

        public async Task<IReadOnlyList<VisibleAction>> GetVisibleAsync(
            Guid needlyUserId,
            ActionFilter filter,
            CancellationToken cancellationToken) =>
            (await GetVisibleAsync(needlyUserId, cancellationToken))
                .Where(action => ActionFilterMatcher.IsMatch(filter, VisibleActionFilterCandidate.Create(action)))
                .ToArray();
    }

    private sealed class NullBroadcaster : IActionChangeBroadcaster
    {
        public event Action? Changed { add { } remove { } }

        public void Publish()
        {
        }
    }

    private sealed class ViewTestDatabase : IDbContextFactory<NeedlyDbContext>, IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly DbContextOptions<NeedlyDbContext> options;

        private ViewTestDatabase(SqliteConnection connection, DbContextOptions<NeedlyDbContext> options)
        {
            this.connection = connection;
            this.options = options;
        }

        public static async Task<ViewTestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<NeedlyDbContext>().UseSqlite(connection).Options;
            await using var context = new NeedlyDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new ViewTestDatabase(connection, options);
        }

        public NeedlyDbContext CreateContext() => new(options);

        public NeedlyDbContext CreateDbContext() => CreateContext();

        public Task<NeedlyDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateContext());

        public async ValueTask DisposeAsync() => await connection.DisposeAsync();
    }
}