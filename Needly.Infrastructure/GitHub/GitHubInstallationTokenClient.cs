using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Needly.Application.GitHub;

namespace Needly.Infrastructure.GitHub;

/// <summary>Exchanges a GitHub App JWT for an installation access token.</summary>
public sealed class GitHubInstallationTokenClient(
    HttpClient httpClient,
    IGitHubAppJwtProvider jwtProvider)
{
    private readonly HttpClient httpClient = httpClient
        ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly IGitHubAppJwtProvider jwtProvider = jwtProvider
        ?? throw new ArgumentNullException(nameof(jwtProvider));

    /// <summary>Creates a token for a GitHub App installation.</summary>
    public async Task<GitHubInstallationAccessToken> CreateAsync(
        long gitHubInstallationId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"app/installations/{gitHubInstallationId}/access_tokens");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwtProvider.CreateToken());
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content
            .ReadFromJsonAsync<InstallationTokenResponse>(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("GitHub returned an empty installation token response.");
        if (string.IsNullOrWhiteSpace(payload.Token))
        {
            throw new InvalidOperationException("GitHub returned an empty installation access token.");
        }

        return new GitHubInstallationAccessToken(payload.Token, payload.ExpiresAt);
    }

    private sealed record InstallationTokenResponse(
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);
}