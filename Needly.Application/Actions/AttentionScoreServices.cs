using Needly.Application.GitHub;
using Needly.Domain;

namespace Needly.Application.Actions;

/// <summary>Contains one named, weighted contribution to an <see cref="AttentionScoreResult"/>.</summary>
/// <param name="Points">The points this signal contributed. Negative values lower the score.</param>
/// <param name="Description">A short, user-facing explanation of the signal, e.g. "waiting 11h".</param>
public sealed record AttentionScoreReason(int Points, string Description);

/// <summary>
/// Contains an action's computed attention score and the ordered, named reasons that produced it.
/// </summary>
/// <param name="Score">The final score, clamped to the 0-100 range.</param>
/// <param name="Reasons">
/// Every signal that contributed, ordered from the largest positive contribution to the largest
/// negative one. Every point in <see cref="Score"/> must be traceable to an entry here.
/// </param>
public sealed record AttentionScoreResult(int Score, IReadOnlyList<AttentionScoreReason> Reasons)
{
    /// <summary>Gets a short label for the score band, used for compact display.</summary>
    public string Band => Score switch
    {
        >= 80 => "Urgent",
        >= 55 => "High",
        >= 30 => "Medium",
        _ => "Low"
    };
}

/// <summary>
/// Computes an explainable attention score for a visible action from signals already available on
/// its authorized inbox projection. See issue #33: the score must never be an opaque number -- every
/// point is traceable to a named, configurable rule.
/// </summary>
public interface IAttentionScoreCalculator
{
    /// <summary>Computes the attention score and its explanation for one visible action.</summary>
    AttentionScoreResult Calculate(VisibleAction action);
}
