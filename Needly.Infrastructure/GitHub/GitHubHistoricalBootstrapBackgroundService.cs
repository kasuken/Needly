using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Needly.Application.GitHub;

namespace Needly.Infrastructure.GitHub;

/// <summary>Continuously bootstraps newly available repositories without blocking webhook processing.</summary>
public sealed class GitHubHistoricalBootstrapBackgroundService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<GitHubHistoricalBootstrapOptions> options,
    ILogger<GitHubHistoricalBootstrapBackgroundService> logger) : BackgroundService
{
    private readonly GitHubHistoricalBootstrapOptions options = options.Value;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var claimed = await scope.ServiceProvider
                    .GetRequiredService<IGitHubHistoricalBootstrapService>()
                    .BootstrapNextBatchAsync(stoppingToken)
                    .ConfigureAwait(false);
                if (claimed > 0)
                {
                    logger.LogInformation(
                        "Historical GitHub bootstrap processed {RepositoryCount} repositories; waiting before the next batch",
                        claimed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Historical GitHub bootstrap worker failed; retrying after the idle interval");
            }

            await Task.Delay(options.BatchInterval, timeProvider, stoppingToken).ConfigureAwait(false);
        }
    }
}