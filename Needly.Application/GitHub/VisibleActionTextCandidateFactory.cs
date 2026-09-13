using Needly.Domain;

namespace Needly.Application.GitHub;

/// <summary>
/// Creates <see cref="ActionFilterCandidate"/> instances that also carry the free-text fields
/// (<see cref="VisibleAction.SubjectTitle"/>, <see cref="VisibleAction.Reason"/> and
/// <see cref="VisibleAction.Context"/>) needed to match <see cref="ActionFilter.FreeText"/>.
/// </summary>
/// <remarks>
/// Added for issue #24 (inbox search) in a separate file rather than as a change to
/// <see cref="VisibleActionFilterCandidate"/> in GitHubWebhookModels.cs, because that file is out of scope for
/// this change (it is being modified concurrently by the enriched-filter work for issue #32).
/// <see cref="ActionFilterCandidate"/>'s new Title/Reason/Context members are optional trailing parameters
/// with null defaults, so the existing <see cref="VisibleActionFilterCandidate.Create"/> call did not need to
/// change; this factory only adds the extra fields on top of it.
/// </remarks>
public static class VisibleActionTextCandidateFactory
{
    /// <summary>Creates a filter candidate that also matches free-text search against title, reason and context.</summary>
    public static ActionFilterCandidate CreateWithText(VisibleAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return VisibleActionFilterCandidate.Create(action) with
        {
            Title = action.SubjectTitle,
            Reason = action.Reason,
            Context = action.Context
        };
    }
}
