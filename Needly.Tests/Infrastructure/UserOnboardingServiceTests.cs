using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Needly.Domain;
using Needly.Infrastructure;
using Needly.Infrastructure.Users;
using Xunit;

namespace Needly.Tests.Infrastructure;

public sealed class UserOnboardingServiceTests
{
    [Fact]
    public async Task IsCompletedAsync_NewUser_ReturnsFalse()
    {
        await using var database = await OnboardingTestDatabase.CreateAsync();
        var user = await database.SeedUserAsync();
        var service = new UserOnboardingService(
            database,
            new FixedTimeProvider(TestData.CreatedAt.AddMinutes(1)));

        var isCompleted = await service.IsCompletedAsync(user.Id, CancellationToken.None);

        Assert.False(isCompleted);
    }

    [Fact]
    public async Task IsCompletedAsync_MissingUser_ReturnsTrue()
    {
        await using var database = await OnboardingTestDatabase.CreateAsync();
        var service = new UserOnboardingService(
            database,
            new FixedTimeProvider(TestData.CreatedAt.AddMinutes(1)));

        var isCompleted = await service.IsCompletedAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.True(isCompleted);
    }

    [Fact]
    public async Task IsCompletedAsync_ConcurrentCalls_DoNotShareDbContextOperations()
    {
        await using var database = await OnboardingTestDatabase.CreateAsync();
        var user = await database.SeedUserAsync();
        var service = new UserOnboardingService(
            database,
            new FixedTimeProvider(TestData.CreatedAt.AddMinutes(1)));

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8)
                .Select(_ => service.IsCompletedAsync(user.Id, CancellationToken.None)));

        Assert.All(results, Assert.False);
    }

    [Fact]
    public async Task CompleteAsync_NewUser_PersistsCompletion()
    {
        await using var database = await OnboardingTestDatabase.CreateAsync();
        var user = await database.SeedUserAsync();
        var completedAt = TestData.CreatedAt.AddMinutes(1);
        var service = new UserOnboardingService(
            database,
            new FixedTimeProvider(completedAt));

        await service.CompleteAsync(user.Id, CancellationToken.None);

        Assert.Equal(
            completedAt,
            await database.Context.NeedlyUsers
                .Where(item => item.Id == user.Id)
                .Select(item => item.OnboardingCompletedAt)
                .SingleAsync());
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class OnboardingTestDatabase : IDbContextFactory<NeedlyDbContext>, IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly DbContextOptions<NeedlyDbContext> options;

        private OnboardingTestDatabase(
            SqliteConnection connection,
            DbContextOptions<NeedlyDbContext> options,
            NeedlyDbContext context)
        {
            this.connection = connection;
            this.options = options;
            Context = context;
        }

        internal NeedlyDbContext Context { get; }

        internal static async Task<OnboardingTestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<NeedlyDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new NeedlyDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new OnboardingTestDatabase(connection, options, context);
        }

        public NeedlyDbContext CreateDbContext() => new(options);

        public Task<NeedlyDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());

        internal async Task<NeedlyUser> SeedUserAsync()
        {
            var gitHubUser = GitHubUser.Create(
                Guid.NewGuid(),
                9001,
                "octocat",
                "The Octocat",
                null,
                TestData.CreatedAt);
            var needlyUser = NeedlyUser.Create(
                Guid.NewGuid(),
                gitHubUser.Id,
                "octocat@example.test",
                "The Octocat",
                TestData.CreatedAt);
            Context.AddRange(gitHubUser, needlyUser);
            await Context.SaveChangesAsync();
            return needlyUser;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}