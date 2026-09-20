using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Needly.Domain;
using Needly.Infrastructure;
using Needly.Infrastructure.GitHub;
using Xunit;

namespace Needly.Tests.Infrastructure;

public sealed class GitHubSettingsServiceTests
{
    [Fact]
    public async Task GetAsync_UserExplicitlyLinkedThroughSetup_ReturnsInstallation()
    {
        await using var database = await SettingsTestDatabase.CreateAsync();
        var gitHubUser = TestData.CreateGitHubUser();
        var needlyUser = NeedlyUser.Create(
            Guid.NewGuid(), gitHubUser.Id, "octocat@example.test", "Octocat", TestData.CreatedAt);
        var installation = Installation.Create(
            Guid.NewGuid(), 501, "octo-org", TestData.CreatedAt, GitHubAccountType.Organization, 601);
        await using (var context = database.CreateContext())
        {
            context.AddRange(
                gitHubUser,
                needlyUser,
                installation,
                UserInstallation.Create(
                    Guid.NewGuid(), needlyUser.Id, installation.GitHubInstallationId, TestData.CreatedAt));
            await context.SaveChangesAsync();
        }

        var settings = await new GitHubSettingsService(database.CreateContext())
            .GetAsync(needlyUser.Id, CancellationToken.None);

        var item = Assert.Single(settings.Installations);
        Assert.Equal(501, item.GitHubInstallationId);
        Assert.Equal("octo-org", item.AccountLogin);
    }

    [Fact]
    public async Task GetAsync_OrganizationMemberWithoutSetupLink_StillReturnsInstallation()
    {
        // Reproduces the desync bug: an org installation is created by the webhook and the
        // member is synced through installation membership, but the /github/setup link was
        // never created — so the installation must remain visible via membership alone.
        await using var database = await SettingsTestDatabase.CreateAsync();
        var gitHubUser = TestData.CreateGitHubUser();
        var needlyUser = NeedlyUser.Create(
            Guid.NewGuid(), gitHubUser.Id, "octocat@example.test", "Octocat", TestData.CreatedAt);
        var installation = Installation.Create(
            Guid.NewGuid(), 501, "octo-org", TestData.CreatedAt, GitHubAccountType.Organization, 601);
        await using (var context = database.CreateContext())
        {
            context.AddRange(
                gitHubUser,
                needlyUser,
                installation,
                InstallationMember.Create(Guid.NewGuid(), installation.Id, gitHubUser.Id, TestData.CreatedAt));
            await context.SaveChangesAsync();
        }

        var settings = await new GitHubSettingsService(database.CreateContext())
            .GetAsync(needlyUser.Id, CancellationToken.None);

        var item = Assert.Single(settings.Installations);
        Assert.Equal(501, item.GitHubInstallationId);
        Assert.Equal("octo-org", item.AccountLogin);
    }

    [Fact]
    public async Task GetAsync_UserWithNeitherLinkNorMembership_ReturnsNothing()
    {
        await using var database = await SettingsTestDatabase.CreateAsync();
        var gitHubUser = TestData.CreateGitHubUser();
        var needlyUser = NeedlyUser.Create(
            Guid.NewGuid(), gitHubUser.Id, "octocat@example.test", "Octocat", TestData.CreatedAt);
        var otherGitHubUser = TestData.CreateGitHubUser(Guid.NewGuid(), 902);
        var installation = Installation.Create(
            Guid.NewGuid(), 501, "octo-org", TestData.CreatedAt, GitHubAccountType.Organization, 601);
        await using (var context = database.CreateContext())
        {
            context.AddRange(
                gitHubUser,
                needlyUser,
                otherGitHubUser,
                installation,
                // Membership belongs to a different GitHub user, not our Needly user.
                InstallationMember.Create(Guid.NewGuid(), installation.Id, otherGitHubUser.Id, TestData.CreatedAt));
            await context.SaveChangesAsync();
        }

        var settings = await new GitHubSettingsService(database.CreateContext())
            .GetAsync(needlyUser.Id, CancellationToken.None);

        Assert.Empty(settings.Installations);
    }

    [Fact]
    public async Task GetAsync_InactiveMembershipWithoutLink_ReturnsNothing()
    {
        await using var database = await SettingsTestDatabase.CreateAsync();
        var gitHubUser = TestData.CreateGitHubUser();
        var needlyUser = NeedlyUser.Create(
            Guid.NewGuid(), gitHubUser.Id, "octocat@example.test", "Octocat", TestData.CreatedAt);
        var installation = Installation.Create(
            Guid.NewGuid(), 501, "octo-org", TestData.CreatedAt, GitHubAccountType.Organization, 601);
        var membership = InstallationMember.Create(
            Guid.NewGuid(), installation.Id, gitHubUser.Id, TestData.CreatedAt);
        membership.Deactivate(TestData.CreatedAt.AddDays(1));
        await using (var context = database.CreateContext())
        {
            context.AddRange(gitHubUser, needlyUser, installation, membership);
            await context.SaveChangesAsync();
        }

        var settings = await new GitHubSettingsService(database.CreateContext())
            .GetAsync(needlyUser.Id, CancellationToken.None);

        Assert.Empty(settings.Installations);
    }

    private sealed class SettingsTestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly DbContextOptions<NeedlyDbContext> options;

        private SettingsTestDatabase(SqliteConnection connection, DbContextOptions<NeedlyDbContext> options)
        {
            this.connection = connection;
            this.options = options;
        }

        public static async Task<SettingsTestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<NeedlyDbContext>().UseSqlite(connection).Options;
            await using var context = new NeedlyDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new SettingsTestDatabase(connection, options);
        }

        public NeedlyDbContext CreateContext() => new(options);

        public async ValueTask DisposeAsync() => await connection.DisposeAsync();
    }
}
