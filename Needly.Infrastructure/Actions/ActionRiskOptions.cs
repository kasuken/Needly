namespace Needly.Infrastructure.Actions;

/// <summary>Configures periodic waiting and inactivity risk evaluation.</summary>
public sealed class ActionRiskOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "ActionRisk";

    /// <summary>Gets or sets how long a review may wait before it becomes at risk.</summary>
    public TimeSpan ReviewWaitingThreshold { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Gets or sets how long any action may be inactive before it becomes at risk.</summary>
    public TimeSpan InactivityThreshold { get; set; } = TimeSpan.FromDays(3);

    /// <summary>Gets or sets how often open actions are evaluated.</summary>
    public TimeSpan EvaluationInterval { get; set; } = TimeSpan.FromMinutes(15);
}