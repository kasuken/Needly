using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Needly.Application.GitHub;
using Needly.Domain;

namespace Needly.Infrastructure.GitHub;

/// <summary>Dispatches durable GitHub events and persists terminal or retry status.</summary>
public sealed class GitHubWebhookDispatcher(
    NeedlyDbContext dbContext,
    IInstallationInventoryService installationInventory,
    IGitHubOrganizationMembershipService membershipService,
    IGitHubActionEventHandler actionEventHandler,
    IGitHubWebhookQueue queue,
    IOptions<GitHubAppOptions> options,
    TimeProvider timeProvider,
    ILogger<GitHubWebhookDispatcher> logger) : IGitHubWebhookDispatcher
{
    private static readonly HashSet<string> KnownActionEvents = new(StringComparer.Ordinal)
    {
        "pull_request",
        "pull_request_review",
        "pull_request_review_comment",
        "issue_comment",
        "issues",
        "check_suite",
        "check_run",
        "workflow_run"
    };
    private readonly GitHubAppOptions options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public async Task DispatchAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var rawEvent = await dbContext.RawEvents.SingleOrDefaultAsync(
            item => item.Id == eventId,
            cancellationToken).ConfigureAwait(false);
        if (rawEvent is null || rawEvent.Status is RawEventStatus.Processed or RawEventStatus.Skipped or RawEventStatus.Failed)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        if (rawEvent.NextAttemptAt is { } nextAttemptAt && nextAttemptAt > now)
        {
            await Task.Delay(nextAttemptAt - now, timeProvider, cancellationToken).ConfigureAwait(false);
        }

        rawEvent.MarkProcessing();
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var handled = await DispatchKnownAsync(rawEvent, cancellationToken).ConfigureAwait(false);
            var completedAt = timeProvider.GetUtcNow();
            if (handled)
            {
                rawEvent.MarkProcessed(completedAt);
            }
            else
            {
                rawEvent.MarkSkipped(completedAt);
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "Completed GitHub event {EventId} of type {EventName} with status {Status}",
                rawEvent.Id,
                rawEvent.EventName,
                rawEvent.Status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsTransient(exception) && rawEvent.AttemptCount < options.WebhookMaxAttempts)
        {
            var delay = GetRetryDelay(rawEvent.AttemptCount);
            rawEvent.MarkFailed(
                $"{exception.GetType().Name}: transient webhook processing failure.",
                timeProvider.GetUtcNow().Add(delay));
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogWarning(
                exception,
                "GitHub event {EventId} failed transiently on attempt {AttemptCount}; retrying after {RetryDelay}",
                rawEvent.Id,
                rawEvent.AttemptCount,
                delay);
            await queue.EnqueueAsync(rawEvent.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            rawEvent.MarkFailed($"{exception.GetType().Name}: webhook processing failed.", null);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogError(
                exception,
                "GitHub event {EventId} failed permanently on attempt {AttemptCount}",
                rawEvent.Id,
                rawEvent.AttemptCount);
        }
    }

    private async Task<bool> DispatchKnownAsync(RawEvent rawEvent, CancellationToken cancellationToken)
    {
        switch (rawEvent.EventName)
        {
            case "installation":
            {
                var payload = Deserialize<GitHubInstallationEvent>(rawEvent.PayloadJson);
                await installationInventory.HandleInstallationAsync(
                    payload,
                    rawEvent.ReceivedAt,
                    cancellationToken).ConfigureAwait(false);
                if (payload.Action == "created" && payload.Installation.Account.Type == "Organization")
                {
                    await membershipService.SyncAsync(payload.Installation.Id, cancellationToken).ConfigureAwait(false);
                }

                return true;
            }
            case "installation_repositories":
                await installationInventory.HandleRepositoriesAsync(
                    Deserialize<GitHubInstallationRepositoriesEvent>(rawEvent.PayloadJson),
                    rawEvent.ReceivedAt,
                    cancellationToken).ConfigureAwait(false);
                return true;
            case "member":
                await membershipService.HandleMemberAsync(
                    Deserialize<GitHubMemberEvent>(rawEvent.PayloadJson),
                    rawEvent.ReceivedAt,
                    cancellationToken).ConfigureAwait(false);
                return true;
            case "team":
                await membershipService.HandleTeamAsync(
                    Deserialize<GitHubTeamEvent>(rawEvent.PayloadJson),
                    rawEvent.ReceivedAt,
                    cancellationToken).ConfigureAwait(false);
                return true;
            case "membership":
                await membershipService.HandleMembershipAsync(
                    Deserialize<GitHubMembershipEvent>(rawEvent.PayloadJson),
                    rawEvent.ReceivedAt,
                    cancellationToken).ConfigureAwait(false);
                return true;
            default:
                if (!KnownActionEvents.Contains(rawEvent.EventName))
                {
                    return false;
                }

                await actionEventHandler.HandleAsync(
                    new GitHubStoredEvent(
                        rawEvent.Id,
                        rawEvent.GitHubInstallationId,
                        rawEvent.GitHubRepositoryId,
                        rawEvent.EventName,
                        rawEvent.EventAction,
                        rawEvent.PayloadJson,
                        rawEvent.ReceivedAt),
                    cancellationToken).ConfigureAwait(false);
                return true;
        }
    }

    private static T Deserialize<T>(string payload) =>
        JsonSerializer.Deserialize<T>(payload)
        ?? throw new JsonException($"The GitHub {typeof(T).Name} payload was empty.");

    private static bool IsTransient(Exception exception) =>
        exception is HttpRequestException or TimeoutException;

    private static TimeSpan GetRetryDelay(int attemptCount) =>
        TimeSpan.FromSeconds(Math.Min(Math.Pow(2, Math.Max(0, attemptCount - 1)), 300));
}