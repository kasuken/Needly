namespace Needly.Application.Actions;

/// <summary>
/// Periodically escalates stalled, outstanding requested-changes feedback to Follow up actions for
/// the reviewer who is waiting on a reply. This mirrors <c>IActionRiskEvaluator</c>'s periodic
/// re-evaluation shape, because "unanswered past a configurable threshold" cannot be derived from a
/// single webhook event.
/// </summary>
public interface IFollowUpEvaluator
{
    /// <summary>Creates Follow up actions for outstanding reviewer feedback that has gone stale.</summary>
    /// <returns>The number of Follow up actions created.</returns>
    Task<int> EvaluateAsync(CancellationToken cancellationToken);
}
