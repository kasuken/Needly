using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Needly.Application.GitHub;
using Needly.Domain;
using Needly.Infrastructure;
using Needly.Infrastructure.GitHub;
using Xunit;

namespace Needly.Tests.Infrastructure;

public sealed class GitHubOrganizationMembershipServiceTests
{
    [Fact]
    public async Task WebhookHandlers_MemberTeamAndMembership_PersistActiveAndInactiveTransitions()
    {
        await using var database = await MembershipTestDatabase.CreateAsync();
        database.Context.Installations.Add(CreateInstallation());
        await database.Context.SaveChangesAsync();
        var service = CreateService(database.Context, new FakeGitHubApiClientFactory(new FakeGitHubHandler()));
        var installation = CreateInstallationPayload();
        var user = new GitHubUserPayload(9001, "octocat", "Octo Cat", null);
        var team = new GitHubTeamPayload(801, "maintainers", "Maintainers");

        await service.HandleMemberAsync(
            new GitHubMemberEvent("added", user, installation),
            TestData.CreatedAt,
            CancellationToken.None);
        Assert.True((await database.Context.InstallationMembers.SingleAsync()).IsActive);
        await service.HandleMemberAsync(
            new GitHubMemberEvent("removed", user, installation),
            TestData.CreatedAt.AddMinutes(1),
            CancellationToken.None);
        Assert.False((await database.Context.InstallationMembers.SingleAsync()).IsActive);

        await service.HandleTeamAsync(
            new GitHubTeamEvent("created", team, installation),
            TestData.CreatedAt,
            CancellationToken.None);
        await service.HandleMembershipAsync(
            new GitHubMembershipEvent("added", user, team, installation),
            TestData.CreatedAt.AddMinutes(1),
            CancellationToken.None);
        Assert.True((await database.Context.TeamMembers.SingleAsync()).IsActive);
        await service.HandleMembershipAsync(
            new GitHubMembershipEvent("removed", user, team, installation),
            TestData.CreatedAt.AddMinutes(2),
            CancellationToken.None);
        Assert.False((await database.Context.TeamMembers.SingleAsync()).IsActive);
        await service.HandleTeamAsync(
            new GitHubTeamEvent("deleted", team, installation),
            TestData.CreatedAt.AddMinutes(3),
            CancellationToken.None);
        Assert.False((await database.Context.Teams.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task SyncAsync_InstallationScopedApi_PersistsMembersTeamsAndTeamMembersWithoutNetwork()
    {
        await using var database = await MembershipTestDatabase.CreateAsync();
        database.Context.Installations.Add(CreateInstallation());
        await database.Context.SaveChangesAsync();
        var handler = new FakeGitHubHandler(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["orgs/octo-org/members?per_page=100"] = "[{\"id\":9001,\"login\":\"octocat\",\"name\":\"Octo Cat\"}]",
            ["orgs/octo-org/teams?per_page=100"] = "[{\"id\":801,\"slug\":\"maintainers\",\"name\":\"Maintainers\"}]",
            ["teams/801/members?per_page=100"] = "[{\"id\":9001,\"login\":\"octocat\",\"name\":\"Octo Cat\"}]"
        });
        var factory = new FakeGitHubApiClientFactory(handler);
        var service = CreateService(database.Context, factory);

        await service.SyncAsync(501, CancellationToken.None);

        Assert.Equal(501, Assert.Single(factory.InstallationIds));
        Assert.Equal(3, handler.RequestPaths.Count);
        Assert.Equal("octocat", (await database.Context.GitHubUsers.SingleAsync()).Login);
        Assert.True((await database.Context.InstallationMembers.SingleAsync()).IsActive);
        Assert.Equal("maintainers", (await database.Context.Teams.SingleAsync()).Slug);
        Assert.True((await database.Context.TeamMembers.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task SyncAsync_MembersLinkHasNextPage_FetchesAndPersistsEveryPage()
    {
        await using var database = await MembershipTestDatabase.CreateAsync();
        database.Context.Installations.Add(CreateInstallation());
        await database.Context.SaveChangesAsync();
        const string MembersPath = "orgs/octo-org/members?per_page=100";
        var handler = new FakeGitHubHandler(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [MembersPath] = "[{\"id\":9001,\"login\":\"octocat\"}]",
                [$"{MembersPath}&page=2"] = "[{\"id\":9002,\"login\":\"hubot\"}]",
                ["orgs/octo-org/teams?per_page=100"] = "[]"
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [MembersPath] = "<https://api.github.com/orgs/octo-org/members?per_page=100&page=2>; rel=\"next\""
            });
        var service = CreateService(database.Context, new FakeGitHubApiClientFactory(handler));

        await service.SyncAsync(501, CancellationToken.None);

        Assert.Equal(2, await database.Context.InstallationMembers.CountAsync());
        Assert.Equal(["hubot", "octocat"], await database.Context.GitHubUsers
            .OrderBy(user => user.Login)
            .Select(user => user.Login)
            .ToListAsync());
        Assert.Contains($"{MembersPath}&page=2", handler.RequestPaths);
    }

    [Fact]
    public async Task ResolveAsync_RequestedInstallationTeam_ReturnsOnlyActiveMembers()
    {
        await using var database = await MembershipTestDatabase.CreateAsync();
        var installation = CreateInstallation();
        var otherInstallation = Installation.Create(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            502,
            "other-org",
            TestData.CreatedAt,
            GitHubAccountType.Organization);
        var activeUser = TestData.CreateGitHubUser(gitHubUserId: 9001);
        var inactiveUser = TestData.CreateGitHubUser(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            9002);
        var team = Team.Create(
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            installation.Id,
            801,
            "maintainers",
            "Maintainers",
            TestData.CreatedAt);
        var otherTeam = Team.Create(
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            otherInstallation.Id,
            801,
            "maintainers",
            "Other Maintainers",
            TestData.CreatedAt);
        var activeMembership = TeamMember.Create(
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            team.Id,
            activeUser.Id,
            TestData.CreatedAt);
        var inactiveMembership = TeamMember.Create(
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            team.Id,
            inactiveUser.Id,
            TestData.CreatedAt);
        inactiveMembership.Deactivate(TestData.CreatedAt.AddMinutes(1));
        database.Context.AddRange(
            installation,
            otherInstallation,
            activeUser,
            inactiveUser,
            team,
            otherTeam,
            activeMembership,
            inactiveMembership);
        await database.Context.SaveChangesAsync();

        var target = await new TeamReviewResolver(database.Context)
            .ResolveAsync(501, 801, CancellationToken.None);

        Assert.NotNull(target);
        Assert.Equal(team.Id, target.TeamId);
        Assert.Equal([activeUser.Id], target.GitHubUserIds);
    }

    [Fact]
    public async Task GetVisibleAsync_ActiveMembership_ReturnsDirectAndTeamActionsOnlyFromThatInstallation()
    {
        await using var database = await MembershipTestDatabase.CreateAsync();
        var installation = CreateInstallation();
        var otherInstallation = Installation.Create(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            502,
            "other-org",
            TestData.CreatedAt,
            GitHubAccountType.Organization);
        var user = TestData.CreateGitHubUser(gitHubUserId: 9001);
        var needlyUser = NeedlyUser.Create(
            Guid.Parse("10000000-0000-0000-0000-000000000002"),
            user.Id,
            "octocat@example.test",
            "Octocat",
            TestData.CreatedAt);
        var repository = TestData.CreateRepository(installationId: installation.Id);
        var otherRepository = Repository.Create(
            Guid.Parse("10000000-0000-0000-0000-000000000003"),
            otherInstallation.Id,
            102,
            "other-org",
            "other-repo",
            TestData.CreatedAt);
        var team = Team.Create(
            Guid.Parse("10000000-0000-0000-0000-000000000004"),
            installation.Id,
            801,
            "maintainers",
            "Maintainers",
            TestData.CreatedAt);
        var directAction = TestData.CreateAction(
            Guid.Parse("10000000-0000-0000-0000-000000000005"),
            repository,
            user,
            subjectNumber: 10);
        var teamAction = NeedlyAction.CreateForTeam(
            Guid.Parse("10000000-0000-0000-0000-000000000006"),
            ActionType.Review,
            repository,
            team,
            GitHubSubjectType.PullRequest,
            11,
            "https://github.com/octocat/needly/pull/11",
            "Review team request",
            null,
            "Team review requested",
            TestData.CreatedAt);
        var isolatedAction = TestData.CreateAction(
            Guid.Parse("10000000-0000-0000-0000-000000000007"),
            otherRepository,
            user,
            subjectNumber: 12);
        database.Context.AddRange(
            installation,
            otherInstallation,
            user,
            needlyUser,
            repository,
            otherRepository,
            team,
            InstallationMember.Create(Guid.NewGuid(), installation.Id, user.Id, TestData.CreatedAt),
            TeamMember.Create(Guid.NewGuid(), team.Id, user.Id, TestData.CreatedAt),
            directAction,
            teamAction,
            isolatedAction);
        await database.Context.SaveChangesAsync();

        var visible = await new InboxVisibilityService(
            database.Context,
                new FixedTimeProvider(TestData.CreatedAt.AddHours(2)))
            .GetVisibleAsync(needlyUser.Id, CancellationToken.None);

        Assert.Equal(2, visible.Count);
        Assert.All(visible, item => Assert.Equal(TimeSpan.FromHours(2), item.WaitingDuration));
        Assert.Contains(visible, item => item.ActionId == directAction.Id);
        Assert.Contains(visible, item => item.ActionId == teamAction.Id);
        Assert.DoesNotContain(visible, item => item.ActionId == isolatedAction.Id);
        var direct = visible.Single(item => item.ActionId == directAction.Id);
        Assert.Equal("octocat", direct.RepositoryOwner);
        Assert.Equal("needly", direct.RepositoryName);
        Assert.Equal(10, direct.SubjectNumber);
        Assert.Equal(GitHubSubjectType.PullRequest, direct.SubjectType);
        Assert.Equal(ActionType.Review, direct.Type);
        Assert.Equal(ActionState.Open, direct.State);
        Assert.Equal("User 9001 (@user-9001)", direct.AssigneeDisplay);
        Assert.Equal(TestData.CreatedAt, direct.WaitingSince);
    }

    [Fact]
    public async Task GetVisibleAsync_FilterAndPerUserDispositions_ReturnsPinnedThenFyiAndHidesDeferredActions()
    {
        await using var database = await MembershipTestDatabase.CreateAsync();
        var installation = CreateInstallation();
        var user = TestData.CreateGitHubUser(gitHubUserId: 9001);
        var otherGitHubUser = TestData.CreateGitHubUser(Guid.NewGuid(), 9002);
        var needlyUser = NeedlyUser.Create(
            Guid.NewGuid(), user.Id, "octocat@example.test", "Octocat", TestData.CreatedAt);
        var otherNeedlyUser = NeedlyUser.Create(
            Guid.NewGuid(), otherGitHubUser.Id, "other@example.test", "Other", TestData.CreatedAt);
        var repository = TestData.CreateRepository(installationId: installation.Id);
        var pinned = TestData.CreateAction(Guid.NewGuid(), repository, user, subjectNumber: 20);
        var fyi = TestData.CreateAction(
            Guid.NewGuid(), repository, user, ActionType.Fix, subjectNumber: 21);
        var archived = TestData.CreateAction(Guid.NewGuid(), repository, user, subjectNumber: 22);
        var snoozed = TestData.CreateAction(Guid.NewGuid(), repository, user, subjectNumber: 23);
        var now = TestData.CreatedAt.AddHours(2);
        var pinnedDisposition = ActionDisposition.Create(
            Guid.NewGuid(), needlyUser.Id, pinned.Id, TestData.CreatedAt);
        pinnedDisposition.Apply(RuleEffect.Pin, null, now);
        var fyiDisposition = ActionDisposition.Create(
            Guid.NewGuid(), needlyUser.Id, fyi.Id, TestData.CreatedAt);
        fyiDisposition.Apply(RuleEffect.MarkFyi, null, now);
        var archivedDisposition = ActionDisposition.Create(
            Guid.NewGuid(), needlyUser.Id, archived.Id, TestData.CreatedAt);
        archivedDisposition.Apply(RuleEffect.AutoArchive, null, now);
        var snoozedDisposition = ActionDisposition.Create(
            Guid.NewGuid(), needlyUser.Id, snoozed.Id, TestData.CreatedAt);
        snoozedDisposition.Apply(RuleEffect.Snooze, now.AddHours(1), now);
        var otherUsersDisposition = ActionDisposition.Create(
            Guid.NewGuid(), otherNeedlyUser.Id, pinned.Id, TestData.CreatedAt);
        otherUsersDisposition.Apply(RuleEffect.AutoArchive, null, now);
        database.Context.AddRange(
            installation,
            user,
            otherGitHubUser,
            needlyUser,
            otherNeedlyUser,
            repository,
            InstallationMember.Create(Guid.NewGuid(), installation.Id, user.Id, TestData.CreatedAt),
            pinned,
            fyi,
            archived,
            snoozed,
            pinnedDisposition,
            fyiDisposition,
            archivedDisposition,
            snoozedDisposition,
            otherUsersDisposition);
        await database.Context.SaveChangesAsync();

        var visible = await new InboxVisibilityService(
            database.Context,
            new FixedTimeProvider(now)).GetVisibleAsync(
                needlyUser.Id,
                new ActionFilter
                {
                    Types = [ActionType.Review, ActionType.FYI],
                    Repositories = ["octocat/needly"]
                },
                CancellationToken.None);

        Assert.Equal(2, visible.Count);
        Assert.Equal(pinned.Id, visible[0].ActionId);
        Assert.True(visible[0].IsPinned);
        Assert.Equal(fyi.Id, visible[1].ActionId);
        Assert.Equal(ActionType.FYI, visible[1].Type);
        Assert.DoesNotContain(visible, item => item.ActionId == archived.Id);
        Assert.DoesNotContain(visible, item => item.ActionId == snoozed.Id);
    }

    private static GitHubOrganizationMembershipService CreateService(
        NeedlyDbContext context,
        IGitHubApiClientFactory factory) =>
        new(
            context,
            factory,
            new FixedTimeProvider(TestData.CreatedAt),
            NullLogger<GitHubOrganizationMembershipService>.Instance);

    private static Installation CreateInstallation() =>
        Installation.Create(
            TestData.InstallationId,
            501,
            "octo-org",
            TestData.CreatedAt,
            GitHubAccountType.Organization);

    private static GitHubInstallationPayload CreateInstallationPayload() =>
        new(501, new GitHubAccountPayload(601, "octo-org", "Organization"), "selected");

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeGitHubApiClientFactory(FakeGitHubHandler handler) : IGitHubApiClientFactory
    {
        internal List<long> InstallationIds { get; } = [];

        public Task<IGitHubApiClient> CreateAsync(
            long gitHubInstallationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InstallationIds.Add(gitHubInstallationId);
            return Task.FromResult<IGitHubApiClient>(new FakeGitHubApiClient(handler));
        }
    }

    private sealed class FakeGitHubApiClient : IGitHubApiClient
    {
        private readonly HttpClient httpClient;

        internal FakeGitHubApiClient(HttpMessageHandler handler)
        {
            httpClient = new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://api.github.test/")
            };
        }

        public Task<HttpResponseMessage> SendAsync(
            HttpMethod method,
            string relativePath,
            HttpContent? content,
            CancellationToken cancellationToken) =>
            httpClient.SendAsync(new HttpRequestMessage(method, relativePath) { Content = content }, cancellationToken);
    }

    private sealed class FakeGitHubHandler(
        IReadOnlyDictionary<string, string>? responses = null,
        IReadOnlyDictionary<string, string>? links = null)
        : HttpMessageHandler
    {
        internal List<string> RequestPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = request.RequestUri?.PathAndQuery.TrimStart('/')
                ?? throw new InvalidOperationException("A request URI is required.");
            RequestPaths.Add(path);
            string? json = null;
            var status = responses?.TryGetValue(path, out json) == true
                ? HttpStatusCode.OK
                : HttpStatusCode.NotFound;
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(json ?? "{}", Encoding.UTF8, "application/json")
            };
            if (links?.TryGetValue(path, out var link) == true)
            {
                response.Headers.TryAddWithoutValidation("Link", link);
            }

            return Task.FromResult(response);
        }
    }

    private sealed class MembershipTestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private MembershipTestDatabase(SqliteConnection connection, NeedlyDbContext context)
        {
            this.connection = connection;
            Context = context;
        }

        internal NeedlyDbContext Context { get; }

        internal static async Task<MembershipTestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new NeedlyDbContext(
                new DbContextOptionsBuilder<NeedlyDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new MembershipTestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}