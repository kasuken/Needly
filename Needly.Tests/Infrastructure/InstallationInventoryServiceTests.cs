using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Needly.Application.GitHub;
using Needly.Domain;
using Needly.Infrastructure;
using Needly.Infrastructure.GitHub;
using Xunit;

namespace Needly.Tests.Infrastructure;

public sealed class InstallationInventoryServiceTests
{
    [Fact]
    public async Task HandleInstallationAsync_Created_PersistsOrganizationAndSelectedRepositories()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var installationEvent = CreateInstallationEvent("created", [CreateRepository(701)]);

        await service.HandleInstallationAsync(
            installationEvent,
            TestData.CreatedAt,
            CancellationToken.None);

        var installation = await database.Context.Installations.SingleAsync();
        var repository = await database.Context.Repositories.SingleAsync();
        Assert.Equal(501, installation.GitHubInstallationId);
        Assert.Equal("octo-org", installation.AccountLogin);
        Assert.Equal(GitHubAccountType.Organization, installation.AccountType);
        Assert.Equal(InstallationState.Active, installation.State);
        Assert.Equal(701, repository.GitHubRepositoryId);
        Assert.Equal(installation.Id, repository.InstallationId);
    }

    [Fact]
    public async Task HandleInstallationAsync_SuspendUnsuspendDelete_PersistsEveryStateTransition()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        await service.HandleInstallationAsync(
            CreateInstallationEvent("created"),
            TestData.CreatedAt,
            CancellationToken.None);

        await service.HandleInstallationAsync(
            CreateInstallationEvent("suspend"),
            TestData.CreatedAt.AddMinutes(1),
            CancellationToken.None);
        Assert.Equal(InstallationState.Suspended, (await database.Context.Installations.SingleAsync()).State);

        await service.HandleInstallationAsync(
            CreateInstallationEvent("unsuspend"),
            TestData.CreatedAt.AddMinutes(2),
            CancellationToken.None);
        Assert.Equal(InstallationState.Active, (await database.Context.Installations.SingleAsync()).State);

        await service.HandleInstallationAsync(
            CreateInstallationEvent("deleted"),
            TestData.CreatedAt.AddMinutes(3),
            CancellationToken.None);
        var installation = await database.Context.Installations.SingleAsync();
        Assert.Equal(InstallationState.Deleted, installation.State);
        Assert.False(installation.IsActive);
    }

    [Fact]
    public async Task HandleRepositoriesAsync_AddedAndRemoved_UpdatesSelectedRepositoryInventory()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        await service.HandleInstallationAsync(
            CreateInstallationEvent("created", [CreateRepository(701)]),
            TestData.CreatedAt,
            CancellationToken.None);
        var repositoriesEvent = new GitHubInstallationRepositoriesEvent(
            "added",
            CreateInstallationPayload(),
            [CreateRepository(702)],
            [CreateRepository(701)]);

        await service.HandleRepositoriesAsync(
            repositoriesEvent,
            TestData.CreatedAt.AddMinutes(1),
            CancellationToken.None);

        var repositories = await database.Context.Repositories.AsNoTracking().ToListAsync();
        var repository = Assert.Single(repositories);
        Assert.Equal(702, repository.GitHubRepositoryId);
    }

    [Fact]
    public async Task LinkUserAsync_BeforeInstallationWebhook_RemainsAvailableToSettingsAfterCreation()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var identityService = new GitHubIdentityService(
            database.Context,
            new FixedTimeProvider(TestData.CreatedAt),
            NullLogger<GitHubIdentityService>.Instance);
        var user = await identityService.UpsertAsync(
            new GitHubIdentityProfile(9001, "octocat", "octocat@example.test", null, null),
            CancellationToken.None);
        var inventoryService = CreateService(database.Context);

        await inventoryService.LinkUserAsync(
            user.NeedlyUserId,
            501,
            TestData.CreatedAt,
            CancellationToken.None);
        await inventoryService.HandleInstallationAsync(
            CreateInstallationEvent("created", [CreateRepository(701)]),
            TestData.CreatedAt.AddMinutes(1),
            CancellationToken.None);
        var settings = await new GitHubSettingsService(database.Context)
            .GetAsync(user.NeedlyUserId, CancellationToken.None);

        var installation = Assert.Single(settings.Installations);
        Assert.Equal(501, installation.GitHubInstallationId);
        Assert.Equal("octo-org", installation.AccountLogin);
        Assert.Single(installation.Repositories);
    }

    private static InstallationInventoryService CreateService(NeedlyDbContext context) =>
        new(context, NullLogger<InstallationInventoryService>.Instance);

    private static GitHubInstallationEvent CreateInstallationEvent(
        string action,
        IReadOnlyList<GitHubRepositoryPayload>? repositories = null) =>
        new(action, CreateInstallationPayload(), repositories);

    private static GitHubInstallationPayload CreateInstallationPayload() =>
        new(501, new GitHubAccountPayload(601, "octo-org", "Organization"), "selected");

    private static GitHubRepositoryPayload CreateRepository(long id) =>
        new(id, $"repo-{id}", $"octo-org/repo-{id}", new GitHubAccountPayload(601, "octo-org", "Organization"));

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class InventoryTestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private InventoryTestDatabase(SqliteConnection connection, NeedlyDbContext context)
        {
            this.connection = connection;
            Context = context;
        }

        internal NeedlyDbContext Context { get; }

        internal static async Task<InventoryTestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new NeedlyDbContext(
                new DbContextOptionsBuilder<NeedlyDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await context.Database.EnsureCreatedAsync();
            return new InventoryTestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}