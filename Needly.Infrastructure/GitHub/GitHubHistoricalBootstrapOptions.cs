namespace Needly.Infrastructure.GitHub;

/// <summary>Controls bounded historical action bootstrap from current GitHub state.</summary>
public sealed class GitHubHistoricalBootstrapOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "GitHubHistoricalBootstrap";

    /// <summary>Gets or sets whether historical bootstrap is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the maximum repositories claimed in one worker batch.</summary>
    public int MaxRepositoriesPerBatch { get; set; } = 25;

    /// <summary>Gets or sets the maximum pages fetched from any paginated endpoint.</summary>
    public int MaxPagesPerEndpoint { get; set; } = 10;

    /// <summary>Gets or sets how long an interrupted repository claim remains exclusive.</summary>
    public TimeSpan ClaimTimeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Gets or sets how long the worker waits between repository batches.</summary>
    public TimeSpan BatchInterval { get; set; } = TimeSpan.FromSeconds(30);
}