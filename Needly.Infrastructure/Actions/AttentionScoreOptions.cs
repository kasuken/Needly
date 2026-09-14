namespace Needly.Infrastructure.Actions;

/// <summary>
/// Configures the weights used to compute the explainable attention score (issue #33). Ship as
/// configuration, not code, so the formula can be tuned without a deployment.
/// </summary>
public sealed class AttentionScoreOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "AttentionScore";

    /// <summary>Gets or sets the base points awarded per <see cref="Needly.Domain.ActionType"/>.</summary>
    public Dictionary<string, int> TypeBasePoints { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Review"] = 30,
        ["Fix"] = 28,
        ["Merge"] = 25,
        ["Resolve"] = 22,
        ["Decide"] = 20,
        ["Respond"] = 18,
        ["FollowUp"] = 12,
        ["Monitor"] = 5,
        ["FYI"] = 0
    };

    /// <summary>Gets or sets the bonus for an action already flagged at risk.</summary>
    public int AtRiskBonus { get; set; } = 20;

    /// <summary>Gets or sets the bonus for an action the user manually pinned.</summary>
    public int PinnedBonus { get; set; } = 15;

    /// <summary>Gets or sets the bonus for an action assigned directly to the user rather than their team.</summary>
    public int DirectAssignmentBonus { get; set; } = 10;

    /// <summary>Gets or sets the penalty applied while the pull request is still a draft.</summary>
    public int DraftPenalty { get; set; } = -15;

    /// <summary>Gets or sets the bonus for a large or extra-large pull request.</summary>
    public int LargeDiffBonus { get; set; } = 5;

    /// <summary>Gets or sets the bonus applied when a label in <see cref="UrgentLabels"/> is present.</summary>
    public int UrgentLabelBonus { get; set; } = 15;

    /// <summary>Gets or sets the case-insensitive labels that trigger <see cref="UrgentLabelBonus"/>.</summary>
    public string[] UrgentLabels { get; set; } = ["urgent", "critical", "security", "hotfix"];

    /// <summary>Gets or sets the ascending waiting-duration thresholds and their point bonuses.</summary>
    /// <remarks>
    /// Evaluated in order; the highest threshold the action's waiting duration meets or exceeds wins
    /// (thresholds do not stack). Defaults: 2h -&gt; +2, 8h -&gt; +6, 1d -&gt; +12, 3d -&gt; +20.
    /// </remarks>
    public List<AttentionWaitingTier> WaitingTiers { get; set; } =
    [
        new(TimeSpan.FromHours(2), 2),
        new(TimeSpan.FromHours(8), 6),
        new(TimeSpan.FromDays(1), 12),
        new(TimeSpan.FromDays(3), 20)
    ];
}

/// <summary>Associates a minimum waiting duration with the points it awards.</summary>
/// <param name="AtLeast">The minimum waiting duration for this tier to apply.</param>
/// <param name="Points">The points awarded when the action has waited at least this long.</param>
public sealed record AttentionWaitingTier(TimeSpan AtLeast, int Points);
