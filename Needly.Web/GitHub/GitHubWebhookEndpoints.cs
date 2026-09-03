using System.Buffers;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Needly.Application.GitHub;
using Needly.Infrastructure.GitHub;

namespace Needly.Web.GitHub;

/// <summary>Maps GitHub webhook delivery endpoints.</summary>
public static class GitHubWebhookEndpoints
{
    /// <summary>Maps the signed GitHub webhook receiver.</summary>
    public static WebApplication MapNeedlyGitHubWebhookEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapPost("/webhooks/github", HandleAsync)
            .WithName("ReceiveGitHubWebhook")
            .WithSummary("Receive a signed GitHub App webhook")
            .WithDescription("Authenticates, durably stores, and queues a GitHub webhook delivery.")
            .Produces<GitHubWebhookResponse>(StatusCodes.Status202Accepted)
            .Produces<GitHubWebhookResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .DisableAntiforgery();
        return app;
    }

    private static async Task<Results<
        Accepted<GitHubWebhookResponse>,
        Ok<GitHubWebhookResponse>,
        BadRequest<ProblemDetails>,
        UnauthorizedHttpResult,
        StatusCodeHttpResult>> HandleAsync(
        HttpRequest request,
        IGitHubWebhookIngestionService ingestionService,
        IOptions<GitHubAppOptions> options,
        CancellationToken cancellationToken)
    {
        var maximumBytes = options.Value.WebhookMaxPayloadBytes;
        if (request.ContentLength > maximumBytes)
        {
            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var payload = await ReadBoundedAsync(request.Body, maximumBytes, cancellationToken);
        if (payload is null)
        {
            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        try
        {
            var receipt = await ingestionService.IngestAsync(
                new GitHubWebhookRequest(
                    request.Headers["X-GitHub-Delivery"].ToString(),
                    request.Headers["X-GitHub-Event"].ToString(),
                    request.Headers["X-Hub-Signature-256"].ToString(),
                    payload),
                cancellationToken);
            var response = new GitHubWebhookResponse(receipt.EventId, receipt.IsDuplicate);
            return receipt.IsDuplicate
                ? TypedResults.Ok(response)
                : TypedResults.Accepted(uri: (string?)null, value: response);
        }
        catch (GitHubWebhookAuthenticationException)
        {
            return TypedResults.Unauthorized();
        }
        catch (GitHubWebhookValidationException exception)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid GitHub webhook",
                Detail = exception.Message,
                Instance = request.Path
            });
        }
    }

    private static async Task<byte[]?> ReadBoundedAsync(
        Stream body,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Min(maximumBytes + 1, 81_920));
        try
        {
            await using var payload = new MemoryStream(Math.Min(maximumBytes, 81_920));
            while (true)
            {
                var bytesRead = await body.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (bytesRead == 0)
                {
                    return payload.ToArray();
                }

                if (payload.Length + bytesRead > maximumBytes)
                {
                    return null;
                }

                await payload.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

/// <summary>Describes a durable GitHub webhook acknowledgment.</summary>
/// <param name="EventId">The stored event identifier.</param>
/// <param name="IsDuplicate">Whether this delivery had already been accepted.</param>
public sealed record GitHubWebhookResponse(Guid EventId, bool IsDuplicate);