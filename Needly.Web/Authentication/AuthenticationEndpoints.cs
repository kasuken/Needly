using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Needly.Application.GitHub;

namespace Needly.Web.Authentication;

internal static class AuthenticationEndpoints
{
    internal static IEndpointRouteBuilder MapNeedlyAuthenticationEndpoints(
        this IEndpointRouteBuilder endpoints,
        bool gitHubIntegrationEnabled)
    {
        endpoints.MapGet("/auth/login", ([FromQuery] string? returnUrl) =>
        {
            if (!gitHubIntegrationEnabled)
            {
                return Results.Problem(
                    title: "GitHub sign-in is not configured.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var properties = new AuthenticationProperties
            {
                RedirectUri = GetLocalReturnUrl(returnUrl)
            };
            return Results.Challenge(properties, [GitHubAuthenticationDefaults.Scheme]);
        }).AllowAnonymous();

        endpoints.MapPost("/auth/logout", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            return Results.SignOut(
                new AuthenticationProperties { RedirectUri = "/" },
                [CookieAuthenticationDefaults.AuthenticationScheme]);
        }).RequireAuthorization();

        endpoints.MapGet("/github/setup", async (
            ClaimsPrincipal principal,
            IInstallationInventoryService inventoryService,
            TimeProvider timeProvider,
            [FromQuery(Name = "installation_id")] long? installationId,
            [FromQuery(Name = "setup_action")] string? setupAction,
            CancellationToken cancellationToken) =>
        {
            var userIdValue = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdValue, out var needlyUserId))
            {
                return Results.Unauthorized();
            }

            if (installationId is null or <= 0)
            {
                return Results.Redirect("/settings?status=missing-installation");
            }

            await inventoryService.LinkUserAsync(
                needlyUserId,
                installationId.Value,
                timeProvider.GetUtcNow(),
                cancellationToken);
            var status = setupAction switch
            {
                "install" => "installed",
                "update" => "updated",
                _ => "connected"
            };
            return Results.Redirect($"/settings?status={status}");
        }).RequireAuthorization();

        return endpoints;
    }

    private static string GetLocalReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) ||
            !returnUrl.StartsWith("/", StringComparison.Ordinal) ||
            returnUrl.StartsWith("//", StringComparison.Ordinal) ||
            returnUrl.StartsWith("/\\", StringComparison.Ordinal) ||
            returnUrl.Contains('\r', StringComparison.Ordinal) ||
            returnUrl.Contains('\n', StringComparison.Ordinal))
        {
            return "/settings";
        }

        return returnUrl;
    }
}