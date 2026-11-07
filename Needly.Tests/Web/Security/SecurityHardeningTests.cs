using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Needly.Web.Security;
using Xunit;

namespace Needly.Tests.Web.Security;

public sealed class SecurityHardeningTests
{
    [Fact]
    public async Task SecurityHeaders_AreAddedToEveryResponse()
    {
        await using var app = await StartAsync(new Dictionary<string, string?>());
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/ok");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", Single(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Single(response, "X-Frame-Options"));
        Assert.Equal("strict-origin-when-cross-origin", Single(response, "Referrer-Policy"));
        Assert.Contains("camera=()", Single(response, "Permissions-Policy"));
        Assert.Equal("same-origin", Single(response, "Cross-Origin-Opener-Policy"));
        var csp = Single(response, "Content-Security-Policy");
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.Matches("script-src 'self' 'nonce-[A-Za-z0-9+/=]{24}';", csp);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", csp);
        Assert.DoesNotContain("unsafe-eval", csp);
    }

    [Fact]
    public async Task SecurityHeaders_AreAddedToErrorResponses()
    {
        await using var app = await StartAsync(new Dictionary<string, string?>());
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/missing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("nosniff", Single(response, "X-Content-Type-Options"));
    }

    [Fact]
    public void Hsts_MaxAgeIsOneYear() =>
        Assert.Equal("max-age=31536000", SecurityHeadersExtensions.HstsHeaderValue);

    [Fact]
    public async Task RateLimit_ExcessRequestsReceive429WithRetryAfter_OtherPoliciesUnaffected()
    {
        await using var app = await StartAsync(new Dictionary<string, string?>
        {
            ["RateLimiting:Login:PermitLimit"] = "2",
            ["RateLimiting:Login:WindowSeconds"] = "60"
        });
        using var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/login")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/login")).StatusCode);
        using var limited = await client.GetAsync("/login");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
        Assert.Equal("nosniff", Single(limited, "X-Content-Type-Options"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/setup")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/ok")).StatusCode);
    }

    [Fact]
    public async Task RateLimit_DefaultsAllowNormalUse()
    {
        await using var app = await StartAsync(new Dictionary<string, string?>());
        using var client = app.GetTestClient();

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/login")).StatusCode);
        }
    }

    private static string Single(HttpResponseMessage response, string name) =>
        Assert.Single(response.Headers.GetValues(name));

    private static async Task<WebApplication> StartAsync(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddNeedlyRateLimiting(builder.Configuration);
        var app = builder.Build();
        app.UseNeedlySecurityHeaders();
        app.UseRateLimiter();
        app.MapGet("/ok", () => "ok");
        app.MapGet("/login", () => "login").RequireRateLimiting(RateLimitingExtensions.LoginPolicy);
        app.MapGet("/setup", () => "setup").RequireRateLimiting(RateLimitingExtensions.SetupPolicy);
        await app.StartAsync();
        return app;
    }
}
