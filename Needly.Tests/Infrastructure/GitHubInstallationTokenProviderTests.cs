using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Needly.Application.GitHub;
using Needly.Domain;
using Needly.Infrastructure;
using Needly.Infrastructure.GitHub;
using Xunit;

namespace Needly.Tests.Infrastructure;

public sealed class GitHubInstallationTokenProviderTests
{
    [Fact]
    public async Task GetAsync_CachedThenNearExpiry_ReusesAndRefreshesToken()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var timeProvider = new MutableTimeProvider(TestData.CreatedAt);
        var handler = new TokenResponseHandler(timeProvider);
        await using var serviceProvider = CreateServiceProvider(connection, timeProvider, handler);
        await SeedInstallationAsync(serviceProvider, active: true);

        var first = await GetTokenAsync(serviceProvider);
        var cached = await GetTokenAsync(serviceProvider);
        timeProvider.Advance(TimeSpan.FromMinutes(59));
        var refreshed = await GetTokenAsync(serviceProvider);

        Assert.Equal("installation-token-1", first.Token);
        Assert.Equal(first, cached);
        Assert.Equal("installation-token-2", refreshed.Token);
        Assert.Equal(2, handler.RequestCount);
        Assert.All(handler.AuthorizationHeaders, value => Assert.Equal("Bearer app-jwt", value));
        Assert.All(handler.RequestPaths, value => Assert.Equal("/app/installations/501/access_tokens", value));
    }

    [Theory]
    [InlineData(InstallationState.Suspended)]
    [InlineData(InstallationState.Deleted)]
    public async Task GetAsync_InactiveInstallation_RefusesBeforeHttpRequest(InstallationState state)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var timeProvider = new MutableTimeProvider(TestData.CreatedAt);
        var handler = new TokenResponseHandler(timeProvider);
        await using var serviceProvider = CreateServiceProvider(connection, timeProvider, handler);
        await SeedInstallationAsync(serviceProvider, active: false, state);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => GetTokenAsync(serviceProvider));

        Assert.Contains("not active", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.RequestCount);
    }

    private static async Task<GitHubInstallationAccessToken> GetTokenAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IGitHubInstallationTokenProvider>()
            .GetAsync(501, CancellationToken.None);
    }

    private static ServiceProvider CreateServiceProvider(
        SqliteConnection connection,
        TimeProvider timeProvider,
        TokenResponseHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<NeedlyDbContext>(options => options.UseSqlite(connection));
        services.AddNeedlyGitHubIntegration();
        services.AddSingleton(timeProvider);
        services.AddSingleton<IGitHubAppJwtProvider>(new StubJwtProvider());
        services.AddHttpClient<GitHubInstallationTokenClient>()
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task SeedInstallationAsync(
        ServiceProvider services,
        bool active,
        InstallationState state = InstallationState.Active)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<NeedlyDbContext>();
        await context.Database.EnsureCreatedAsync();
        var installation = Installation.Create(
            TestData.InstallationId,
            501,
            "octocat",
            TestData.CreatedAt);
        if (!active)
        {
            if (state == InstallationState.Suspended)
            {
                installation.Suspend(TestData.CreatedAt.AddMinutes(1));
            }
            else
            {
                installation.Delete(TestData.CreatedAt.AddMinutes(1));
            }
        }

        context.Installations.Add(installation);
        await context.SaveChangesAsync();
    }

    private sealed class StubJwtProvider : IGitHubAppJwtProvider
    {
        public string CreateToken() => "app-jwt";
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => utcNow;

        internal void Advance(TimeSpan value) => utcNow = utcNow.Add(value);
    }

    private sealed class TokenResponseHandler(MutableTimeProvider timeProvider) : HttpMessageHandler
    {
        internal int RequestCount { get; private set; }

        internal List<string?> AuthorizationHeaders { get; } = [];

        internal List<string> RequestPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            AuthorizationHeaders.Add(request.Headers.Authorization?.ToString());
            RequestPaths.Add(request.RequestUri?.AbsolutePath ?? string.Empty);
            var expiresAt = timeProvider.GetUtcNow().AddHours(1);
            var response = new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new
                {
                    token = $"installation-token-{RequestCount}",
                    expires_at = expiresAt
                })
            };
            return Task.FromResult(response);
        }
    }
}