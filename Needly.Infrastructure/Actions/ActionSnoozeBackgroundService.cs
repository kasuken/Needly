using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Needly.Application.Actions;

namespace Needly.Infrastructure.Actions;

/// <summary>Periodically resurfaces actions with elapsed snooze deadlines.</summary>
public sealed class ActionSnoozeBackgroundService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider) : BackgroundService
{
    private static readonly TimeSpan EvaluationInterval = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ResurfaceAsync(stoppingToken).ConfigureAwait(false);
        using var timer = new PeriodicTimer(EvaluationInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await ResurfaceAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ResurfaceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IActionSnoozeService>()
            .ResurfaceDueAsync(cancellationToken).ConfigureAwait(false);
    }
}