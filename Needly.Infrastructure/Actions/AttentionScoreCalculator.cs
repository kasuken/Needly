using Microsoft.Extensions.Options;
using Needly.Application.Actions;
using Needly.Application.GitHub;
using Needly.Domain;

namespace Needly.Infrastructure.Actions;

/// <summary>
/// Computes an explainable attention score from signals already present on the authorized inbox
/// projection. Deliberately reads only <see cref="VisibleAction"/> -- no direct database or GitHub API
/// access -- so it stays a fast, pure, easily-testable calculation callable from the UI layer.
/// </summary>
public sealed class AttentionScoreCalculator(IOptions<AttentionScoreOptions> options) : IAttentionScoreCalculator
{
    /// <inheritdoc />
    public AttentionScoreResult Calculate(VisibleAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var settings = options.Value;
        var reasons = new List<AttentionScoreReason>();

        var basePoints = settings.TypeBasePoints.TryGetValue(action.Type.ToString(), out var points)
            ? points
            : 0;
        if (basePoints != 0)
        {
            reasons.Add(new AttentionScoreReason(basePoints, DescribeType(action.Type)));
        }

        if (action.IsAtRisk && settings.AtRiskBonus != 0)
        {
            reasons.Add(new AttentionScoreReason(
                settings.AtRiskBonus,
                string.IsNullOrWhiteSpace(action.RiskReason) ? "at risk" : action.RiskReason));
        }

        if (action.IsPinned && settings.PinnedBonus != 0)
        {
            reasons.Add(new AttentionScoreReason(settings.PinnedBonus, "pinned"));
        }

        if (action.AssigneeScope == ActionAssigneeScope.Me && settings.DirectAssignmentBonus != 0)
        {
            reasons.Add(new AttentionScoreReason(settings.DirectAssignmentBonus, "assigned directly to you"));
        }

        if (action.IsDraft == true && settings.DraftPenalty != 0)
        {
            reasons.Add(new AttentionScoreReason(settings.DraftPenalty, "still a draft"));
        }

        if (action.SizeBucket is ActionSizeBucket.L or ActionSizeBucket.XL && settings.LargeDiffBonus != 0)
        {
            reasons.Add(new AttentionScoreReason(settings.LargeDiffBonus, $"large change ({action.SizeBucket})"));
        }

        var urgentLabel = action.Labels.FirstOrDefault(label =>
            settings.UrgentLabels.Contains(label, StringComparer.OrdinalIgnoreCase));
        if (urgentLabel is not null && settings.UrgentLabelBonus != 0)
        {
            reasons.Add(new AttentionScoreReason(settings.UrgentLabelBonus, $"labeled \"{urgentLabel}\""));
        }

        var waitingTier = settings.WaitingTiers
            .Where(tier => action.WaitingDuration >= tier.AtLeast)
            .OrderByDescending(tier => tier.AtLeast)
            .FirstOrDefault();
        if (waitingTier is not null && waitingTier.Points != 0)
        {
            reasons.Add(new AttentionScoreReason(waitingTier.Points, $"waiting {DescribeDuration(action.WaitingDuration)}"));
        }

        var total = Math.Clamp(reasons.Sum(reason => reason.Points), 0, 100);
        var ordered = reasons
            .OrderByDescending(reason => reason.Points)
            .ToArray();
        return new AttentionScoreResult(total, ordered);
    }

    private static string DescribeType(ActionType type) => type switch
    {
        ActionType.Review => "explicitly requested review",
        ActionType.Fix => "CI is failing",
        ActionType.Merge => "ready to merge",
        ActionType.Resolve => "unresolved feedback",
        ActionType.Decide => "a decision is blocking this",
        ActionType.Respond => "a response is needed",
        ActionType.FollowUp => "stalled, needs a follow-up",
        ActionType.Monitor => "being monitored",
        ActionType.FYI => "informational",
        _ => type.ToString()
    };

    private static string DescribeDuration(TimeSpan duration) => duration.TotalDays switch
    {
        >= 2 => $"{(int)duration.TotalDays} days",
        >= 1 => "1 day",
        _ when duration.TotalHours >= 2 => $"{(int)duration.TotalHours} hours",
        _ when duration.TotalHours >= 1 => "1 hour",
        _ => $"{Math.Max(1, (int)duration.TotalMinutes)} minutes"
    };
}
