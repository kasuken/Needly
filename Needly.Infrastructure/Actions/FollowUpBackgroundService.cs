using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Needly.Application.Actions;

namespace Needly.Infrastructure.Actions;

/// <summary>Periodically escalates stale, outstanding reviewer feedback to Follow up actions.</summary>
public sealed class FollowUpBackgroundService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<FollowUpOptions> options) : BackgroundService
{
    private readonly FollowUpOptions options = options.Value;

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
        await scope.ServiceProvider.GetRequiredService<IFollowUpEvaluator>()
            .EvaluateAsync(cancellationToken).ConfigureAwait(false);
    }
}
