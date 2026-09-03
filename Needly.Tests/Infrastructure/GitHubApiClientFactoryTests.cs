using System.Net;
using Needly.Application.GitHub;
using Needly.Infrastructure.GitHub;
using Xunit;

namespace Needly.Tests.Infrastructure;

public sealed class GitHubApiClientFactoryTests
{
    [Fact]
    public async Task CreateAsync_Installation_ReturnsClientThatUsesInstallationBearerToken()
    {
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.github.test/")
        };
        var tokenProvider = new StubTokenProvider();
        var factory = new GitHubApiClientFactory(
            new StubHttpClientFactory(httpClient),
            tokenProvider);

        var client = await factory.CreateAsync(501, CancellationToken.None);
        using var response = await client.SendAsync(
            HttpMethod.Get,
            "repos/octocat/needly",
            null,
            CancellationToken.None);

        Assert.Equal(501, tokenProvider.RequestedInstallationId);
        Assert.Equal("Bearer installation-token", handler.AuthorizationHeader);
        Assert.Equal("/repos/octocat/needly", handler.RequestPath);
    }

    private sealed class StubTokenProvider : IGitHubInstallationTokenProvider
    {
        internal long RequestedInstallationId { get; private set; }

        public Task<GitHubInstallationAccessToken> GetAsync(
            long gitHubInstallationId,
            CancellationToken cancellationToken)
        {
            RequestedInstallationId = gitHubInstallationId;
            return Task.FromResult(new GitHubInstallationAccessToken(
                "installation-token",
                TestData.CreatedAt.AddHours(1)));
        }
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        internal string? AuthorizationHeader { get; private set; }

        internal string? RequestPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AuthorizationHeader = request.Headers.Authorization?.ToString();
            RequestPath = request.RequestUri?.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}