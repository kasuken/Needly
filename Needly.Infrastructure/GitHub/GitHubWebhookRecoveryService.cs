using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Needly.Application.GitHub;
using Needly.Domain;

namespace Needly.Infrastructure.GitHub;

/// <summary>Repairs and requeues durable webhook work after process restart.</summary>
public sealed class GitHubWebhookRecoveryService(
    NeedlyDbContext dbContext,
    IGitHubWebhookQueue queue,
    ILogger<GitHubWebhookRecoveryService> logger) : IGitHubWebhookRecoveryService
{
    /// <inheritdoc />
    public async Task<int> RecoverAsync(CancellationToken cancellationToken)
    {
        var recoverable = await dbContext.RawEvents
            .Where(rawEvent =>
                rawEvent.Status == RawEventStatus.Pending ||
                rawEvent.Status == RawEventStatus.Processing ||
                rawEvent.Status == RawEventStatus.RetryPending)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        recoverable.Sort(static (left, right) => left.ReceivedAt.CompareTo(right.ReceivedAt));
        foreach (var rawEvent in recoverable)
        {
            rawEvent.RecoverInterruptedProcessing();
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var rawEvent in recoverable)
        {
            await queue.EnqueueAsync(rawEvent.Id, cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation("Recovered {EventCount} durable GitHub events", recoverable.Count);
        return recoverable.Count;
    }
}