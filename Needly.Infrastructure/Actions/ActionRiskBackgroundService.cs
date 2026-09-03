using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Needly.Application.Actions;

namespace Needly.Infrastructure.Actions;

/// <summary>Periodically evaluates open actions for waiting and inactivity risk.</summary>
public sealed class ActionRiskBackgroundService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<ActionRiskOptions> options) : BackgroundService
{
    private readonly ActionRiskOptions options = options.Value;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EvaluateAsync(stoppingToken).ConfigureAwait(false);
        using var timer = new PeriodicTimer(options.EvaluationInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await EvaluateAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IActionRiskEvaluator>()
            .EvaluateAsync(cancellationToken).ConfigureAwait(false);
    }
}