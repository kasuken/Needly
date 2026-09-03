using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Needly.Application.GitHub;

namespace Needly.Infrastructure.GitHub;

/// <summary>Recovers and processes durable webhook events in repository order.</summary>
public sealed class GitHubWebhookBackgroundService(
    IGitHubWebhookQueue queue,
    IServiceScopeFactory scopeFactory) : BackgroundService
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> orderingLocks = new(StringComparer.Ordinal);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumer = ConsumeAsync(stoppingToken);
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IGitHubWebhookRecoveryService>()
                .RecoverAsync(stoppingToken).ConfigureAwait(false);
        }

        await consumer.ConfigureAwait(false);
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        await foreach (var eventId in queue.ReadAllAsync(cancellationToken).ConfigureAwait(false))
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
                continue;
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
}