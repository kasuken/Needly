using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Needly.Application.Actions;

namespace Needly.Infrastructure.Actions;

/// <summary>Periodically resurfaces actions with elapsed snooze deadlines.</summary>
public sealed class ActionSnoozeBackgroundService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ActionSnoozeBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan EvaluationInterval = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ResurfaceSafelyAsync(stoppingToken).ConfigureAwait(false);
        using var timer = new PeriodicTimer(EvaluationInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await ResurfaceSafelyAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    // A failed pass (for example a SQL timeout) must never stop the host; the next tick retries.
    private async Task ResurfaceSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ResurfaceAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Snoozed action resurfacing failed; retrying on the next interval");
        }
    }

    private async Task ResurfaceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IActionSnoozeService>()
            .ResurfaceDueAsync(cancellationToken).ConfigureAwait(false);
    }
}