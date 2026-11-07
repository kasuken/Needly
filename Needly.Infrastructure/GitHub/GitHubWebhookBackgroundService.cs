using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Needly.Application.GitHub;

namespace Needly.Infrastructure.GitHub;

/// <summary>Recovers and processes durable webhook events in repository order.</summary>
public sealed class GitHubWebhookBackgroundService(
    IGitHubWebhookQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<GitHubWebhookBackgroundService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> orderingLocks = new(StringComparer.Ordinal);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumer = ConsumeAsync(stoppingToken);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IGitHubWebhookRecoveryService>()
                .RecoverAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Never stop the host over recovery; the consumer keeps draining the queue.
            logger.LogError(exception, "Recovering durable GitHub webhook events failed");
        }

        await consumer.ConfigureAwait(false);
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        await foreach (var eventId in queue.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await ProcessAsync(eventId, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // One bad event must not stop the host: the unhandled exception would terminate the
                // process, and recovery would replay the same event on the next start.
                logger.LogError(exception, "Processing GitHub webhook event {EventId} failed", eventId);
            }
        }
    }

    private async Task ProcessAsync(Guid eventId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NeedlyDbContext>();
        var orderingIdentity = await dbContext.RawEvents
            .AsNoTracking()
            .Where(rawEvent => rawEvent.Id == eventId)
            .Select(rawEvent => rawEvent.GitHubRepositoryId == null
                ? $"installation:{rawEvent.GitHubInstallationId}"
                : $"repository:{rawEvent.GitHubRepositoryId}")
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (orderingIdentity is null)
        {
            return;
        }

        var orderingLock = orderingLocks.GetOrAdd(orderingIdentity, static _ => new SemaphoreSlim(1, 1));
        await orderingLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await scope.ServiceProvider.GetRequiredService<IGitHubWebhookDispatcher>()
                .DispatchAsync(eventId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            orderingLock.Release();
        }
    }
}
