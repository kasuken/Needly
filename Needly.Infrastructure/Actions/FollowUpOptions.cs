namespace Needly.Infrastructure.Actions;

/// <summary>Configures how long outstanding requested-changes feedback may go unaddressed before Needly escalates it to a Follow up action.</summary>
public sealed class FollowUpOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "FollowUp";

    /// <summary>Gets or sets how long a reviewer's outstanding requested changes may go unaddressed before follow-up is needed.</summary>
    public TimeSpan StaleFeedbackThreshold { get; set; } = TimeSpan.FromDays(2);

    /// <summary>Gets or sets how often outstanding reviewer feedback is evaluated for staleness.</summary>
    public TimeSpan EvaluationInterval { get; set; } = TimeSpan.FromMinutes(15);
}
