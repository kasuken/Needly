using System.Net.Http.Headers;
using Needly.Application.GitHub;

namespace Needly.Infrastructure.GitHub;

/// <summary>Creates installation-authenticated GitHub API clients.</summary>
public sealed class GitHubApiClientFactory(
    IHttpClientFactory httpClientFactory,
    IGitHubInstallationTokenProvider tokenProvider) : IGitHubApiClientFactory
{
    internal const string ClientName = "Needly.GitHubApi";
    private readonly IHttpClientFactory httpClientFactory = httpClientFactory
        ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly IGitHubInstallationTokenProvider tokenProvider = tokenProvider
        ?? throw new ArgumentNullException(nameof(tokenProvider));

    /// <inheritdoc />
    public async Task<IGitHubApiClient> CreateAsync(
        long gitHubInstallationId,
        CancellationToken cancellationToken)
    {
        var accessToken = await tokenProvider
            .GetAsync(gitHubInstallationId, cancellationToken)
            .ConfigureAwait(false);
        return new GitHubApiClient(
            httpClientFactory.CreateClient(ClientName),
            accessToken.Token);
    }

    private sealed class GitHubApiClient(HttpClient httpClient, string accessToken) : IGitHubApiClient
    {
        public async Task<HttpResponseMessage> SendAsync(
            HttpMethod method,
            string relativePath,
            HttpContent? content,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                throw new ArgumentException("A GitHub API path is required.", nameof(relativePath));
            }

            using var request = new HttpRequestMessage(method, relativePath) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
    }
}