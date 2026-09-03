using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Options;
using Needly.Application.GitHub;
using Needly.Infrastructure.GitHub;

namespace Needly.Web.Authentication;

internal static class GitHubAuthenticationDefaults
{
    internal const string Scheme = "GitHub";
    internal const string GitHubUserIdClaim = "urn:needly:github:user-id";
    internal const string GitHubLoginClaim = "urn:needly:github:login";
}

internal static class GitHubAuthenticationExtensions
{
    internal static AuthenticationBuilder AddNeedlyGitHubOAuth(
        this AuthenticationBuilder authenticationBuilder,
        IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(authenticationBuilder);
        ArgumentNullException.ThrowIfNull(services);

        authenticationBuilder.AddOAuth(GitHubAuthenticationDefaults.Scheme, _ => { });
        services.AddOptions<OAuthOptions>(GitHubAuthenticationDefaults.Scheme)
            .Configure<IOptions<GitHubAppOptions>>((oauth, configuredOptions) =>
            {
                var options = configuredOptions.Value;
                oauth.ClientId = options.ClientId;
                oauth.ClientSecret = options.ClientSecret;
                oauth.CallbackPath = "/auth/github/callback";
                oauth.AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
                oauth.TokenEndpoint = "https://github.com/login/oauth/access_token";
                oauth.UserInformationEndpoint = "https://api.github.com/user";
                oauth.SaveTokens = false;
                oauth.Events.OnCreatingTicket = CreateTicketAsync;
            });
        return authenticationBuilder;
    }

    private static async Task CreateTicketAsync(OAuthCreatingTicketContext context)
    {
        if (string.IsNullOrWhiteSpace(context.AccessToken))
        {
            throw new AuthenticationFailureException("GitHub did not return an OAuth access token.");
        }

        var profile = await GetIdentityProfileAsync(
            context.Backchannel,
            context.Options.UserInformationEndpoint,
            context.AccessToken,
            context.HttpContext.RequestAborted).ConfigureAwait(false);

        var identityService = context.HttpContext.RequestServices
            .GetRequiredService<IGitHubIdentityService>();
        var user = await identityService.UpsertAsync(
            profile,
            context.HttpContext.RequestAborted).ConfigureAwait(false);
        var identity = context.Identity
            ?? throw new AuthenticationFailureException("GitHub did not create an authenticated identity.");
        identity.AddClaim(new Claim(
            ClaimTypes.NameIdentifier,
            user.NeedlyUserId.ToString("D"),
            ClaimValueTypes.String,
            GitHubAuthenticationDefaults.Scheme));
        identity.AddClaim(new Claim(
            ClaimTypes.Name,
            user.DisplayName,
            ClaimValueTypes.String,
            GitHubAuthenticationDefaults.Scheme));
        identity.AddClaim(new Claim(
            ClaimTypes.Email,
            profile.Email,
            ClaimValueTypes.Email,
            GitHubAuthenticationDefaults.Scheme));
        identity.AddClaim(new Claim(
            GitHubAuthenticationDefaults.GitHubUserIdClaim,
            user.GitHubUserId.ToString(CultureInfo.InvariantCulture),
            ClaimValueTypes.Integer64,
            GitHubAuthenticationDefaults.Scheme));
        identity.AddClaim(new Claim(
            GitHubAuthenticationDefaults.GitHubLoginClaim,
            user.Login,
            ClaimValueTypes.String,
            GitHubAuthenticationDefaults.Scheme));
    }

    internal static async Task<GitHubIdentityProfile> GetIdentityProfileAsync(
        HttpClient client,
        string userInformationEndpoint,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var profile = await GetAsync<GitHubOAuthUser>(
            client,
            userInformationEndpoint,
            accessToken,
            cancellationToken).ConfigureAwait(false);
        var email = profile.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            var emails = await GetAsync<IReadOnlyList<GitHubOAuthEmail>>(
                client,
                "https://api.github.com/user/emails",
                accessToken,
                cancellationToken).ConfigureAwait(false);
            email = emails.FirstOrDefault(candidate => candidate.Primary && candidate.Verified)?.Email
                ?? emails.FirstOrDefault(candidate => candidate.Verified)?.Email;
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new AuthenticationFailureException(
                "GitHub did not provide a verified email address for this account.");
        }

        return new GitHubIdentityProfile(
            profile.Id,
            profile.Login,
            email,
            profile.Name,
            profile.AvatarUrl);
    }

    private static async Task<T> GetAsync<T>(
        HttpClient client,
        string requestUri,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("Needly/1.0");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var endpoint = request.RequestUri?.AbsolutePath ?? requestUri;
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden
                && string.Equals(endpoint, "/user/emails", StringComparison.Ordinal))
            {
                var acceptedPermissions = response.Headers.TryGetValues(
                    "X-Accepted-GitHub-Permissions",
                    out var values)
                    ? $" GitHub reports accepted permissions: {string.Join(", ", values)}."
                    : string.Empty;
                throw new AuthenticationFailureException(
                    $"GitHub API request to {endpoint} failed with HTTP status 403. "
                    + "The GitHub App requires Account permissions -> Email addresses: Read. "
                    + "Approve any pending permission update, then reauthorize the App."
                    + acceptedPermissions);
            }

            throw new AuthenticationFailureException(
                $"GitHub API request to {endpoint} failed with HTTP status {(int)response.StatusCode}.");
        }

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false)
            ?? throw new AuthenticationFailureException("GitHub returned an empty profile response.");
    }

    private sealed record GitHubOAuthUser(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("login")] string Login,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("avatar_url")] string? AvatarUrl);

    private sealed record GitHubOAuthEmail(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("primary")] bool Primary,
        [property: JsonPropertyName("verified")] bool Verified);
}