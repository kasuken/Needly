using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Needly.Web.Authentication;
using Xunit;

namespace Needly.Tests.Web.Authentication;

public sealed class GitHubAuthenticationExtensionsTests
{
    [Fact]
    public async Task GetIdentityProfileAsync_PublicProfileEmail_SkipsEmailEndpointAndSendsRequiredHeaders()
    {
        var handler = new GitHubProfileHandler(request =>
            request.RequestUri?.AbsolutePath == "/user"
                ? JsonResponse(new
                {
                    id = 42,
                    login = "octocat",
                    email = "octocat@example.com",
                    name = "The Octocat",
                    avatar_url = "https://avatars.example/octocat"
                })
                : throw new InvalidOperationException("The email endpoint must not be requested."));
        using var client = new HttpClient(handler);

        var profile = await GitHubAuthenticationExtensions.GetIdentityProfileAsync(
            client,
            "https://api.github.com/user",
            "test-token",
            CancellationToken.None);

        Assert.Equal(42, profile.GitHubUserId);
        Assert.Equal("octocat", profile.Login);
        Assert.Equal("octocat@example.com", profile.Email);
        Assert.Equal("The Octocat", profile.DisplayName);
        Assert.Equal("https://avatars.example/octocat", profile.AvatarUrl);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/user", request.Path);
        Assert.Equal("Bearer test-token", request.Authorization);
        Assert.Contains("application/vnd.github+json", request.Accept);
        Assert.Contains("Needly/1.0", request.UserAgent);
        Assert.Equal("2022-11-28", request.ApiVersion);
    }

    [Fact]
    public async Task GetIdentityProfileAsync_MissingPublicEmail_UsesVerifiedPrimaryEmail()
    {
        var handler = new GitHubProfileHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/user" => JsonResponse(new
            {
                id = 42,
                login = "octocat",
                email = (string?)null,
                name = "The Octocat",
                avatar_url = (string?)null
            }),
            "/user/emails" => JsonResponse(new[]
            {
                new { email = "secondary@example.com", primary = false, verified = true },
                new { email = "primary@example.com", primary = true, verified = true }
            }),
            _ => throw new InvalidOperationException("Unexpected GitHub endpoint.")
        });
        using var client = new HttpClient(handler);

        var profile = await GitHubAuthenticationExtensions.GetIdentityProfileAsync(
            client,
            "https://api.github.com/user",
            "test-token",
            CancellationToken.None);

        Assert.Equal("primary@example.com", profile.Email);
        Assert.Equal(["/user", "/user/emails"], handler.Requests.Select(request => request.Path));
    }

    [Fact]
    public async Task GetIdentityProfileAsync_EmailEndpointForbidden_ExplainsPermissionAndReauthorization()
    {
        var handler = new GitHubProfileHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/user" => JsonResponse(new
            {
                id = 42,
                login = "octocat",
                email = (string?)null,
                name = (string?)null,
                avatar_url = (string?)null
            }),
            "/user/emails" => ForbiddenEmailResponse(),
            _ => throw new InvalidOperationException("Unexpected GitHub endpoint.")
        });
        using var client = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<AuthenticationFailureException>(() =>
            GitHubAuthenticationExtensions.GetIdentityProfileAsync(
                client,
                "https://api.github.com/user",
                "test-token",
                CancellationToken.None));

        Assert.Contains("/user/emails", exception.Message, StringComparison.Ordinal);
        Assert.Contains("HTTP status 403", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Account permissions -> Email addresses: Read", exception.Message, StringComparison.Ordinal);
        Assert.Contains("reauthorize", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("user:email=read", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetIdentityProfileAsync_NoVerifiedEmail_ThrowsControlledFailure()
    {
        var handler = new GitHubProfileHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/user" => JsonResponse(new
            {
                id = 42,
                login = "octocat",
                email = "",
                name = (string?)null,
                avatar_url = (string?)null
            }),
            "/user/emails" => JsonResponse(new[]
            {
                new { email = "unverified@example.com", primary = true, verified = false }
            }),
            _ => throw new InvalidOperationException("Unexpected GitHub endpoint.")
        });
        using var client = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<AuthenticationFailureException>(() =>
            GitHubAuthenticationExtensions.GetIdentityProfileAsync(
                client,
                "https://api.github.com/user",
                "test-token",
                CancellationToken.None));

        Assert.Equal(
            "GitHub did not provide a verified email address for this account.",
            exception.Message);
    }

    private static HttpResponseMessage JsonResponse<T>(T value) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private static HttpResponseMessage ForbiddenEmailResponse()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.Add("X-Accepted-GitHub-Permissions", "user:email=read");
        return response;
    }

    private sealed class GitHubProfileHandler(
        Func<HttpRequestMessage, HttpResponseMessage> createResponse) : HttpMessageHandler
    {
        internal List<CapturedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.RequestUri?.AbsolutePath ?? string.Empty,
                request.Headers.Authorization?.ToString(),
                request.Headers.Accept.Select(value => value.MediaType ?? string.Empty).ToArray(),
                request.Headers.UserAgent.Select(value => value.ToString()).ToArray(),
                request.Headers.GetValues("X-GitHub-Api-Version").Single()));
            return Task.FromResult(createResponse(request));
        }
    }

    private sealed record CapturedRequest(
        string Path,
        string? Authorization,
        IReadOnlyList<string> Accept,
        IReadOnlyList<string> UserAgent,
        string ApiVersion);
}