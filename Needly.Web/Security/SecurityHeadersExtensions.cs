using System.Globalization;
using System.Security.Cryptography;

namespace Needly.Web.Security;

/// <summary>Adds the baseline browser security headers to every response (issue #94).</summary>
public static class SecurityHeadersExtensions
{
    private const string NonceItemKey = "Needly.CspNonce";

    /// <summary>
    /// The Content-Security-Policy template; <c>{nonce}</c> is replaced per request. Blazor Server needs
    /// same-origin WebSockets (covered by <c>connect-src 'self'</c>) and renders an inline import map,
    /// which is allowed by nonce. MudBlazor writes inline styles, so styles allow <c>'unsafe-inline'</c>.
    /// Scripts are otherwise same-origin only. Fonts still come from Google Fonts until self-hosted.
    /// </summary>
    internal const string ContentSecurityPolicyTemplate =
        "default-src 'self'; " +
        "script-src 'self' 'nonce-{nonce}'; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' data: https://fonts.gstatic.com; " +
        "img-src 'self' data: https://avatars.githubusercontent.com; " +
        "connect-src 'self'; " +
        "manifest-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    /// <summary>HSTS max-age: one year.</summary>
    internal static readonly TimeSpan HstsMaxAge = TimeSpan.FromDays(365);

    internal static string HstsHeaderValue =>
        "max-age=" + ((long)HstsMaxAge.TotalSeconds).ToString(CultureInfo.InvariantCulture);

    /// <summary>Gets the CSP nonce for the current request, for inline scripts that must be allowed.</summary>
    public static string? GetCspNonce(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Items.TryGetValue(NonceItemKey, out var nonce) ? nonce as string : null;
    }

    internal static string BuildContentSecurityPolicy(string nonce) =>
        ContentSecurityPolicyTemplate.Replace("{nonce}", nonce, StringComparison.Ordinal);

    /// <summary>Adds CSP, nosniff, framing, referrer, permissions and cross-origin opener headers.</summary>
    public static IApplicationBuilder UseNeedlySecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Use(async (context, next) =>
        {
            context.Items[NonceItemKey] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            context.Response.OnStarting(static state =>
            {
                // Assigned (not added): ASP.NET Core components set a weaker frame-ancestors policy of their own.
                var httpContext = (HttpContext)state;
                var headers = httpContext.Response.Headers;
                headers.ContentSecurityPolicy = BuildContentSecurityPolicy(httpContext.GetCspNonce() ?? string.Empty);
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] =
                    "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                return Task.CompletedTask;
            }, context);
            await next(context).ConfigureAwait(false);
        });
    }
}
