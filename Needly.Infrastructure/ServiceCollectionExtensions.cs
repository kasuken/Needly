using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Needly.Application.Actions;
using Needly.Application.GitHub;
using Needly.Infrastructure.Actions;
using Needly.Infrastructure.GitHub;

namespace Needly.Infrastructure;

/// <summary>
/// Registers Needly infrastructure services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Needly's SQLite persistence services.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="connectionString">The SQLite connection string.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection AddNeedlyInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A database connection string is required.", nameof(connectionString));
        }

        services.AddDbContextFactory<NeedlyDbContext>(options => options.UseSqlite(connectionString));
        return services;
    }

    /// <summary>
    /// Adds GitHub App integration, inventory, identity, and API client services.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection AddNeedlyGitHubIntegration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IActionChangeBroadcaster, ActionChangeBroadcaster>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<GitHubAppOptions>, GitHubAppOptionsValidator>());
        services.AddSingleton<IGitHubAppJwtProvider, GitHubAppJwtProvider>();
        services.AddSingleton<GitHubInstallationTokenCache>();
        services.AddSingleton<IGitHubWebhookQueue, GitHubWebhookQueue>();
        services.AddScoped<IGitHubIdentityService, GitHubIdentityService>();
        services.AddScoped<IInstallationInventoryService, InstallationInventoryService>();
        services.AddScoped<IGitHubOrganizationMembershipService, GitHubOrganizationMembershipService>();
        services.AddScoped<ITeamReviewResolver, TeamReviewResolver>();
        services.AddScoped<IInboxVisibilityService, InboxVisibilityService>();
        services.AddScoped<IActionLifecycleService, ActionLifecycleService>();
        services.AddScoped<IActionSnoozeService, ActionSnoozeService>();
        services.AddScoped<IActionRiskEvaluator, ActionRiskEvaluator>();
        services.AddScoped<ISavedViewService, SavedViewService>();
        services.AddScoped<IAutomationRuleService, AutomationRuleService>();
        services.AddSingleton<AutomationRuleEvaluator>();
        services.AddOptions<ActionRiskOptions>()
            .Validate(
                options => options.ReviewWaitingThreshold > TimeSpan.Zero,
                "ActionRisk:ReviewWaitingThreshold must be positive.")
            .Validate(
                options => options.InactivityThreshold > TimeSpan.Zero,
                "ActionRisk:InactivityThreshold must be positive.")
            .Validate(
                options => options.EvaluationInterval > TimeSpan.Zero,
                "ActionRisk:EvaluationInterval must be positive.");
        services.AddScoped<IGitHubWebhookIngestionService, GitHubWebhookIngestionService>();
        services.AddOptions<GitHubActionOptions>()
            .Validate(
                options => options.RequiredApprovals is >= 1 and <= 100,
                "GitHubActions:RequiredApprovals must be between 1 and 100.");
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IGitHubActionDetector, ReviewRequestedActionDetector>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IGitHubActionDetector, ResolveFeedbackActionDetector>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IGitHubActionDetector, CiFailureActionDetector>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IGitHubActionDetector, RespondActionDetector>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IGitHubActionDetector, MergeReadyActionDetector>());
        services.TryAddScoped<IGitHubActionEventHandler, GitHubActionEventHandler>();
        services.AddScoped<IGitHubWebhookDispatcher, GitHubWebhookDispatcher>();
        services.AddScoped<IGitHubWebhookRecoveryService, GitHubWebhookRecoveryService>();
        services.AddScoped<IGitHubHistoricalBootstrapService, GitHubHistoricalBootstrapService>();
        services.AddOptions<GitHubHistoricalBootstrapOptions>()
            .Validate(
                options => options.MaxRepositoriesPerBatch is >= 1 and <= 100,
                "GitHubHistoricalBootstrap:MaxRepositoriesPerBatch must be between 1 and 100.")
            .Validate(
                options => options.MaxPagesPerEndpoint is >= 1 and <= 100,
                "GitHubHistoricalBootstrap:MaxPagesPerEndpoint must be between 1 and 100.")
            .Validate(
                options => options.ClaimTimeout > TimeSpan.Zero,
                "GitHubHistoricalBootstrap:ClaimTimeout must be positive.")
            .Validate(
                options => options.BatchInterval > TimeSpan.Zero,
                "GitHubHistoricalBootstrap:BatchInterval must be positive.");
        services.AddScoped<IGitHubSettingsService, GitHubSettingsService>();
        services.AddScoped<IGitHubInstallationTokenProvider, GitHubInstallationTokenProvider>();
        services.AddScoped<IGitHubApiClientFactory, GitHubApiClientFactory>();
        services.AddScoped<IGitHubPullRequestLookup, GitHubPullRequestLookup>();
        services.AddHttpClient<GitHubInstallationTokenClient>(ConfigureGitHubClient);
        services.AddHttpClient(GitHubApiClientFactory.ClientName, ConfigureGitHubClient);
        return services;
    }

    /// <summary>Adds durable GitHub webhook restart recovery and background processing.</summary>
    public static IServiceCollection AddNeedlyGitHubWebhookProcessing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHostedService<GitHubWebhookBackgroundService>();
        services.AddHostedService<GitHubHistoricalBootstrapBackgroundService>();
        services.AddHostedService<ActionRiskBackgroundService>();
        services.AddHostedService<ActionSnoozeBackgroundService>();
        return services;
    }

    private static void ConfigureGitHubClient(HttpClient client)
    {
        client.BaseAddress = new Uri("https://api.github.com/");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Needly/1.0");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.Timeout = TimeSpan.FromSeconds(30);
    }
}