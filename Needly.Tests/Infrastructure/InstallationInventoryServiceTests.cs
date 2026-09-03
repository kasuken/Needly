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

public sealed class InstallationInventoryServiceTests
{
    private const string RepositoriesPath = "installation/repositories?per_page=100";

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
    public async Task HandleInstallationAsync_CreatedAllRepositoriesWithoutPayload_SynchronizesEveryApiPage()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var apiClientFactory = new RecordingApiClientFactory(new Dictionary<string, ApiResponse>
        {
            [RepositoriesPath] = new(
                "{\"repositories\":[{\"id\":701,\"name\":\"repo-701\",\"full_name\":\"octo-org/repo-701\",\"owner\":{\"id\":601,\"login\":\"octo-org\",\"type\":\"Organization\"}}]}",
                "<https://api.github.com/installation/repositories?per_page=100&page=2>; rel=\"next\""),
            [$"{RepositoriesPath}&page=2"] = new(
                "{\"repositories\":[{\"id\":702,\"name\":\"repo-702\",\"full_name\":\"octo-org/repo-702\",\"owner\":{\"id\":601,\"login\":\"octo-org\",\"type\":\"Organization\"}}]}")
        });
        var service = CreateService(database.Context, apiClientFactory);

        await service.HandleInstallationAsync(
            CreateInstallationEvent("created", repositorySelection: "all"),
            TestData.CreatedAt,
            CancellationToken.None);

        Assert.Equal([501], apiClientFactory.InstallationIds);
        Assert.Equal([RepositoriesPath, $"{RepositoriesPath}&page=2"], apiClientFactory.RequestPaths);
        Assert.Equal(
            [701L, 702L],
            await database.Context.Repositories
                .OrderBy(repository => repository.GitHubRepositoryId)
                .Select(repository => repository.GitHubRepositoryId)
                .ToListAsync());
    }

    [Fact]
    public async Task HandleInstallationAsync_AllRepositoriesWithNullOwner_UsesFullNameOwner()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var apiClientFactory = new RecordingApiClientFactory(new Dictionary<string, ApiResponse>
        {
            [RepositoriesPath] = new(
                "{\"repositories\":[{\"id\":701,\"name\":\"repo-701\",\"full_name\":\"octo-org/repo-701\",\"owner\":null}]}")
        });
        var service = CreateService(database.Context, apiClientFactory);

        await service.HandleInstallationAsync(
            CreateInstallationEvent("created", repositorySelection: "all"),
            TestData.CreatedAt,
            CancellationToken.None);

        var repository = await database.Context.Repositories.SingleAsync();
        Assert.Equal("octo-org", repository.Owner);
        Assert.Equal("repo-701", repository.Name);
    }

    [Fact]
    public async Task HandleInstallationAsync_PersonalOwnerAlreadySignedIn_CreatesActiveInstallationMembership()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var identityService = new GitHubIdentityService(
            database.Context,
            new FixedTimeProvider(TestData.CreatedAt),
            NullLogger<GitHubIdentityService>.Instance);
        await identityService.UpsertAsync(
            new GitHubIdentityProfile(601, "octocat", "octocat@example.test", null, null),
            CancellationToken.None);
        var service = CreateService(database.Context);
        var installationEvent = new GitHubInstallationEvent(
            "created",
            new GitHubInstallationPayload(
                501,
                new GitHubAccountPayload(601, "octocat", "User"),
                "selected"),
            []);

        await service.HandleInstallationAsync(
            installationEvent,
            TestData.CreatedAt.AddMinutes(1),
            CancellationToken.None);

        var installation = await database.Context.Installations.SingleAsync();
        var gitHubUser = await database.Context.GitHubUsers.SingleAsync();
        var membership = await database.Context.InstallationMembers.SingleAsync();
        Assert.Equal(installation.Id, membership.InstallationId);
        Assert.Equal(gitHubUser.Id, membership.GitHubUserId);
        Assert.True(membership.IsActive);
    }

    [Fact]
    public async Task HandleInstallationAsync_NewPermissionsAccepted_RefreshesRepositoryInventory()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var apiClientFactory = new RecordingApiClientFactory(new Dictionary<string, ApiResponse>
        {
            [RepositoriesPath] = new(
                "{\"repositories\":[{\"id\":702,\"name\":\"repo-702\",\"full_name\":\"octo-org/repo-702\",\"owner\":{\"id\":601,\"login\":\"octo-org\",\"type\":\"Organization\"}}]}")
        });
        var service = CreateService(database.Context, apiClientFactory);
        await service.HandleInstallationAsync(
            CreateInstallationEvent("created", [CreateRepository(701)]),
            TestData.CreatedAt,
            CancellationToken.None);
        var installation = await database.Context.Installations.SingleAsync();
        var historicalRepository = await database.Context.Repositories.SingleAsync();
        database.Context.RawEvents.Add(RawEvent.Create(
            Guid.NewGuid(),
            installation.Id,
            historicalRepository.Id,
            "delivery-701",
            "issues",
            "opened",
            "{}",
            TestData.CreatedAt));
        await database.Context.SaveChangesAsync();

        await service.HandleInstallationAsync(
            CreateInstallationEvent("new_permissions_accepted", [CreateRepository(701)]),
            TestData.CreatedAt.AddMinutes(1),
            CancellationToken.None);

        Assert.Equal(
            [702L],
            await database.Context.Repositories
                .Where(repository => repository.IsActive)
                .Select(repository => repository.GitHubRepositoryId)
                .ToListAsync());
        Assert.False((await database.Context.Repositories
            .SingleAsync(repository => repository.GitHubRepositoryId == 701)).IsActive);
        Assert.Equal(2, await database.Context.Repositories.CountAsync());
    }

    [Theory]
    [InlineData("suspend", InstallationState.Suspended)]
    [InlineData("deleted", InstallationState.Deleted)]
    public async Task HandleInstallationAsync_NewPermissionsAccepted_PreservesInactiveState(
        string inactiveAction,
        InstallationState expectedState)
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var apiClientFactory = new RecordingApiClientFactory();
        var service = CreateService(database.Context, apiClientFactory);
        await service.HandleInstallationAsync(
            CreateInstallationEvent("created", []),
            TestData.CreatedAt,
            CancellationToken.None);
        await service.HandleInstallationAsync(
            CreateInstallationEvent(inactiveAction),
            TestData.CreatedAt.AddMinutes(1),
            CancellationToken.None);

        await service.HandleInstallationAsync(
            CreateInstallationEvent("new_permissions_accepted"),
            TestData.CreatedAt.AddMinutes(2),
            CancellationToken.None);

        Assert.Equal(expectedState, (await database.Context.Installations.SingleAsync()).State);
        Assert.Empty(apiClientFactory.InstallationIds);
        Assert.Empty(apiClientFactory.RequestPaths);
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
        var repository = Assert.Single(repositories, item => item.IsActive);
        Assert.Equal(702, repository.GitHubRepositoryId);
        Assert.False(Assert.Single(repositories, item => item.GitHubRepositoryId == 701).IsActive);
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

    [Fact]
    public async Task LinkUserAsync_PersonalInstallationAlreadyExists_CreatesActiveInstallationMembership()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var identityService = new GitHubIdentityService(
            database.Context,
            new FixedTimeProvider(TestData.CreatedAt),
            NullLogger<GitHubIdentityService>.Instance);
        var user = await identityService.UpsertAsync(
            new GitHubIdentityProfile(601, "octocat", "octocat@example.test", null, null),
            CancellationToken.None);
        var installation = Installation.Create(
            Guid.NewGuid(),
            501,
            "octocat",
            TestData.CreatedAt,
            GitHubAccountType.User);
        database.Context.Installations.Add(installation);
        await database.Context.SaveChangesAsync();
        var service = CreateService(database.Context);

        await service.LinkUserAsync(
            user.NeedlyUserId,
            501,
            TestData.CreatedAt.AddMinutes(1),
            CancellationToken.None);

        var gitHubUser = await database.Context.GitHubUsers.SingleAsync();
        var membership = await database.Context.InstallationMembers.SingleAsync();
        Assert.Equal(installation.Id, membership.InstallationId);
        Assert.Equal(gitHubUser.Id, membership.GitHubUserId);
        Assert.True(membership.IsActive);
    }

    [Fact]
    public async Task LinkUserAsync_PersonalOwnerRenamedAfterInstallation_UsesStableGitHubAccountId()
    {
        await using var database = await InventoryTestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var installationEvent = new GitHubInstallationEvent(
            "created",
            new GitHubInstallationPayload(
                501,
                new GitHubAccountPayload(601, "old-octocat", "User"),
                "selected"),
            []);
        await service.HandleInstallationAsync(
            installationEvent,
            TestData.CreatedAt,
            CancellationToken.None);
        var identityService = new GitHubIdentityService(
            database.Context,
            new FixedTimeProvider(TestData.CreatedAt.AddMinutes(1)),
            NullLogger<GitHubIdentityService>.Instance);
        var user = await identityService.UpsertAsync(
            new GitHubIdentityProfile(601, "new-octocat", "octocat@example.test", null, null),
            CancellationToken.None);

        await service.LinkUserAsync(
            user.NeedlyUserId,
            501,
            TestData.CreatedAt.AddMinutes(1),
            CancellationToken.None);

        var installation = await database.Context.Installations.SingleAsync();
        var membership = await database.Context.InstallationMembers.SingleAsync();
        Assert.Equal(601, installation.GitHubAccountId);
        Assert.Equal(installation.Id, membership.InstallationId);
        Assert.True(membership.IsActive);
    }

    private static InstallationInventoryService CreateService(
        NeedlyDbContext context,
        RecordingApiClientFactory? apiClientFactory = null) =>
        new(
            context,
            apiClientFactory ?? new RecordingApiClientFactory(),
            new FixedTimeProvider(TestData.CreatedAt),
            NullLogger<InstallationInventoryService>.Instance);

    private static GitHubInstallationEvent CreateInstallationEvent(
        string action,
        IReadOnlyList<GitHubRepositoryPayload>? repositories = null,
        string repositorySelection = "selected") =>
        new(action, CreateInstallationPayload(repositorySelection), repositories);

    private static GitHubInstallationPayload CreateInstallationPayload(string repositorySelection = "selected") =>
        new(501, new GitHubAccountPayload(601, "octo-org", "Organization"), repositorySelection);

    private static GitHubRepositoryPayload CreateRepository(long id) =>
        new(id, $"repo-{id}", $"octo-org/repo-{id}", new GitHubAccountPayload(601, "octo-org", "Organization"));

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record ApiResponse(string Json, string? Link = null);

    private sealed class RecordingApiClientFactory : IGitHubApiClientFactory
    {
        private readonly IReadOnlyDictionary<string, ApiResponse> responses;

        internal RecordingApiClientFactory(IReadOnlyDictionary<string, ApiResponse>? responses = null)
        {
            this.responses = responses ?? new Dictionary<string, ApiResponse>
            {
                [RepositoriesPath] = new("{\"repositories\":[]}")
            };
        }

        internal List<long> InstallationIds { get; } = [];

        internal List<string> RequestPaths { get; } = [];

        public Task<IGitHubApiClient> CreateAsync(
            long gitHubInstallationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InstallationIds.Add(gitHubInstallationId);
            return Task.FromResult<IGitHubApiClient>(new RecordingApiClient(this));
        }

        private sealed class RecordingApiClient(RecordingApiClientFactory factory) : IGitHubApiClient
        {
            public Task<HttpResponseMessage> SendAsync(
                HttpMethod method,
                string relativePath,
                HttpContent? content,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.Equal(HttpMethod.Get, method);
                Assert.Null(content);
                factory.RequestPaths.Add(relativePath);
                Assert.True(factory.responses.TryGetValue(relativePath, out var configuredResponse));
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        configuredResponse.Json,
                        Encoding.UTF8,
                        "application/json")
                };
                if (configuredResponse.Link is not null)
                {
                    response.Headers.TryAddWithoutValidation("Link", configuredResponse.Link);
                }

                return Task.FromResult(response);
            }
        }
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