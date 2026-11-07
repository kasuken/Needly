using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Needly.Application.Actions;

namespace Needly.Infrastructure.Actions;

/// <summary>Periodically escalates stale, outstanding reviewer feedback to Follow up actions.</summary>
public sealed class FollowUpBackgroundService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<FollowUpOptions> options,
    ILogger<FollowUpBackgroundService> logger) : BackgroundService
{
    private readonly FollowUpOptions options = options.Value;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EvaluateSafelyAsync(stoppingToken).ConfigureAwait(false);
        using var timer = new PeriodicTimer(options.EvaluationInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await EvaluateSafelyAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    // A failed pass (for example a SQL timeout) must never stop the host; the next tick retries.
    private async Task EvaluateSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await EvaluateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Follow-up evaluation failed; retrying on the next interval");
        }
    }

    private async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IFollowUpEvaluator>()
            .EvaluateAsync(cancellationToken).ConfigureAwait(false);
    }
}
