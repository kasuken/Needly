using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Needly.Application.GitHub;
using Needly.Domain;

namespace Needly.Infrastructure.GitHub;

/// <summary>Authenticates and durably accepts GitHub webhook deliveries.</summary>
public sealed class GitHubWebhookIngestionService(
    NeedlyDbContext dbContext,
    IGitHubWebhookQueue queue,
    IOptions<GitHubAppOptions> options,
    TimeProvider timeProvider,
    ILogger<GitHubWebhookIngestionService> logger) : IGitHubWebhookIngestionService
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly GitHubAppOptions options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public async Task<GitHubWebhookReceipt> IngestAsync(
        GitHubWebhookRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateHeader(request.DeliveryId, 100, nameof(request.DeliveryId));
        ValidateHeader(request.EventName, 100, nameof(request.EventName));
        if (request.Payload.Length == 0 || request.Payload.Length > options.WebhookMaxPayloadBytes)
        {
            throw new GitHubWebhookValidationException("The webhook payload size is invalid.");
        }

        ValidateSignature(request.Signature, request.Payload, options.WebhookSecret);

        var duplicate = await dbContext.RawEvents
            .AsNoTracking()
            .Where(rawEvent => rawEvent.DeliveryId == request.DeliveryId)
            .Select(rawEvent => (Guid?)rawEvent.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (duplicate is not null)
        {
            return new GitHubWebhookReceipt(duplicate.Value, true);
        }

        var metadata = ParseMetadata(request.Payload);
        var installation = await dbContext.Installations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.GitHubInstallationId == metadata.GitHubInstallationId,
                cancellationToken)
            .ConfigureAwait(false);
        var repository = metadata.GitHubRepositoryId is null || installation is null
            ? null
            : await dbContext.Repositories
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.InstallationId == installation.Id &&
                            item.GitHubRepositoryId == metadata.GitHubRepositoryId,
                    cancellationToken)
                .ConfigureAwait(false);

        var eventId = Guid.NewGuid();
        var rawEvent = RawEvent.CreateDelivery(
            eventId,
            installation?.Id,
            metadata.GitHubInstallationId,
            repository?.Id,
            metadata.GitHubRepositoryId,
            request.DeliveryId,
            request.EventName,
            metadata.Action,
            StrictUtf8.GetString(request.Payload),
            timeProvider.GetUtcNow());
        dbContext.RawEvents.Add(rawEvent);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            var concurrentDuplicate = await dbContext.RawEvents
                .AsNoTracking()
                .Where(item => item.DeliveryId == request.DeliveryId)
                .Select(item => (Guid?)item.Id)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (concurrentDuplicate is null)
            {
                throw;
            }

            logger.LogInformation(
                "Acknowledged concurrent duplicate GitHub webhook {DeliveryId} as event {EventId}",
                request.DeliveryId,
                concurrentDuplicate.Value);
            return new GitHubWebhookReceipt(concurrentDuplicate.Value, true);
        }

        await queue.EnqueueAsync(eventId, cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Accepted GitHub webhook {DeliveryId} as event {EventId} of type {EventName}",
            request.DeliveryId,
            eventId,
            request.EventName);
        return new GitHubWebhookReceipt(eventId, false);
    }

    private static void ValidateSignature(string signature, byte[] payload, string secret)
    {
        const string Prefix = "sha256=";
        if (string.IsNullOrWhiteSpace(signature) ||
            !signature.StartsWith(Prefix, StringComparison.Ordinal) ||
            signature.Length != Prefix.Length + 64)
        {
            throw new GitHubWebhookAuthenticationException();
        }

        byte[] suppliedHash;
        try
        {
            suppliedHash = Convert.FromHexString(signature[Prefix.Length..]);
        }
        catch (FormatException)
        {
            throw new GitHubWebhookAuthenticationException();
        }

        var expectedHash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload);
        if (!CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash))
        {
            throw new GitHubWebhookAuthenticationException();
        }
    }

    private static WebhookMetadata ParseMetadata(byte[] payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            var installationId = root.GetProperty("installation").GetProperty("id").GetInt64();
            var repositoryId = root.TryGetProperty("repository", out var repository)
                ? repository.GetProperty("id").GetInt64()
                : (long?)null;
            var action = root.TryGetProperty("action", out var actionElement)
                ? actionElement.GetString()
                : null;
            return new WebhookMetadata(installationId, repositoryId, action);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new GitHubWebhookValidationException("The webhook payload is missing required metadata.", exception);
        }
    }

    private static void ValidateHeader(string value, int maximumLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > maximumLength ||
            value.Any(char.IsControl) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new GitHubWebhookValidationException($"The {name} header is invalid.");
        }
    }

    private sealed record WebhookMetadata(long GitHubInstallationId, long? GitHubRepositoryId, string? Action);
}

/// <summary>Represents an unauthenticated GitHub webhook request.</summary>
public sealed class GitHubWebhookAuthenticationException : Exception
{
    /// <summary>Creates an authentication failure without exposing verification details.</summary>
    public GitHubWebhookAuthenticationException() : base("The webhook signature is invalid.")
    {
    }
}

/// <summary>Represents an invalid GitHub webhook header or payload.</summary>
public sealed class GitHubWebhookValidationException : Exception
{
    /// <summary>Creates a webhook validation failure.</summary>
    public GitHubWebhookValidationException(string message) : base(message)
    {
    }

    /// <summary>Creates a webhook validation failure with its parsing cause.</summary>
    public GitHubWebhookValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}