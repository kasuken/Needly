using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Needly.Web.Security;

/// <summary>Per-client-IP rate limits for the anonymous and externally reachable endpoints (issue #94).</summary>
public static class RateLimitingExtensions
{
    /// <summary>Policy for <c>/auth/login</c>.</summary>
    public const string LoginPolicy = "needly-login";

    /// <summary>Policy for <c>/github/setup</c>.</summary>
    public const string SetupPolicy = "needly-setup";

    /// <summary>Policy for <c>/webhooks/github</c>.</summary>
    public const string WebhookPolicy = "needly-webhook";

    /// <summary>Registers fixed-window limiters keyed by client IP; excess requests receive 429.</summary>
    public static IServiceCollection AddNeedlyRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var settings = configuration.GetSection(RateLimitSettings.SectionName).Get<RateLimitSettings>()
            ?? new RateLimitSettings();
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };
            AddPolicy(limiter, LoginPolicy, settings.Login);
            AddPolicy(limiter, SetupPolicy, settings.Setup);
            AddPolicy(limiter, WebhookPolicy, settings.Webhook);
        });
        return services;
    }

    private static void AddPolicy(RateLimiterOptions limiter, string name, RateLimitRule rule) =>
        limiter.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, rule.PermitLimit),
                Window = TimeSpan.FromSeconds(Math.Max(1, rule.WindowSeconds)),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    private static string ClientKey(HttpContext context) =>
        (context.Connection.RemoteIpAddress ?? IPAddress.None).ToString();
}

/// <summary>Configurable limits, bound from the <c>RateLimiting</c> configuration section.</summary>
public sealed class RateLimitSettings
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "RateLimiting";

    /// <summary>Gets or sets the sign-in limit per client IP.</summary>
    public RateLimitRule Login { get; set; } = new() { PermitLimit = 20, WindowSeconds = 60 };

    /// <summary>Gets or sets the GitHub App setup callback limit per client IP.</summary>
    public RateLimitRule Setup { get; set; } = new() { PermitLimit = 30, WindowSeconds = 60 };

    /// <summary>Gets or sets the webhook limit per client IP; deliveries are also HMAC-verified.</summary>
    public RateLimitRule Webhook { get; set; } = new() { PermitLimit = 600, WindowSeconds = 60 };
}

/// <summary>A fixed-window rate limit.</summary>
public sealed class RateLimitRule
{
    /// <summary>Gets or sets the permits allowed per window.</summary>
    public int PermitLimit { get; set; }

    /// <summary>Gets or sets the window length in seconds.</summary>
    public int WindowSeconds { get; set; }
}
