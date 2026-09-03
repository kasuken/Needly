using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Needly.Application.GitHub;
using Needly.Infrastructure;
using Needly.Infrastructure.GitHub;
using Xunit;

namespace Needly.Tests.Infrastructure;

public sealed class GitHubIdentityServiceTests
{
    [Fact]
    public async Task UpsertAsync_NewProfile_PersistsLinkedGitHubAndNeedlyUsers()
    {
        await using var database = await IdentityTestDatabase.CreateAsync();
        var service = CreateService(database.Context, TestData.CreatedAt);
        var profile = new GitHubIdentityProfile(
            9001,
            "octocat",
            "octocat@example.test",
            "The Octocat",
            "https://avatars.example.test/octocat");

        var result = await service.UpsertAsync(profile, CancellationToken.None);

        var gitHubUser = await database.Context.GitHubUsers.SingleAsync();
        var needlyUser = await database.Context.NeedlyUsers.SingleAsync();
        Assert.Equal(result.NeedlyUserId, needlyUser.Id);
        Assert.Equal(gitHubUser.Id, needlyUser.GitHubUserId);
        Assert.Equal(profile.GitHubUserId, gitHubUser.GitHubUserId);
        Assert.Equal(profile.Login, gitHubUser.Login);
        Assert.Equal(profile.Email, needlyUser.Email);
        Assert.Equal(profile.DisplayName, needlyUser.DisplayName);
    }

    [Fact]
    public async Task UpsertAsync_ExistingProfile_UpdatesBothRecordsWithoutDuplicates()
    {
        await using var database = await IdentityTestDatabase.CreateAsync();
        var service = CreateService(database.Context, TestData.CreatedAt);
        await service.UpsertAsync(
            new GitHubIdentityProfile(9001, "old-login", "old@example.test", null, null),
            CancellationToken.None);
        var updatedAt = TestData.CreatedAt.AddHours(1);
        service = CreateService(database.Context, updatedAt);

        var result = await service.UpsertAsync(
            new GitHubIdentityProfile(9001, "new-login", "new@example.test", "New Name", null),
            CancellationToken.None);

        var gitHubUser = await database.Context.GitHubUsers.SingleAsync();
        var needlyUser = await database.Context.NeedlyUsers.SingleAsync();
        Assert.Equal(result.NeedlyUserId, needlyUser.Id);
        Assert.Equal("new-login", gitHubUser.Login);
        Assert.Equal(updatedAt, gitHubUser.UpdatedAt);
        Assert.Equal("new@example.test", needlyUser.Email);
        Assert.Equal("New Name", needlyUser.DisplayName);
        Assert.Equal(updatedAt, needlyUser.UpdatedAt);
    }

    private static GitHubIdentityService CreateService(NeedlyDbContext context, DateTimeOffset utcNow) =>
        new(context, new FixedTimeProvider(utcNow), NullLogger<GitHubIdentityService>.Instance);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class IdentityTestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private IdentityTestDatabase(SqliteConnection connection, NeedlyDbContext context)
        {
            this.connection = connection;
            Context = context;
        }

        internal NeedlyDbContext Context { get; }

        internal static async Task<IdentityTestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new NeedlyDbContext(
                new DbContextOptionsBuilder<NeedlyDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await context.Database.EnsureCreatedAsync();
            return new IdentityTestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}