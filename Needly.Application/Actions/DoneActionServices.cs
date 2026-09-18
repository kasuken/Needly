using Needly.Domain;

namespace Needly.Application.Actions;

/// <summary>Provides the authorized, filterable view of recently cleared actions and their weekly count.</summary>
public interface IDoneActionsService
{
    /// <summary>Gets the user's recently archived or completed actions, most recently closed first.</summary>
    Task<IReadOnlyList<DoneAction>> GetAsync(Guid needlyUserId, CancellationToken cancellationToken);

    /// <summary>Gets the user's recently archived or completed actions matching a shared filter.</summary>
    Task<IReadOnlyList<DoneAction>> GetAsync(
        Guid needlyUserId,
        ActionFilter filter,
        CancellationToken cancellationToken);

    /// <summary>Counts actions the user cleared (archived, muted, or done) in roughly the last 7 days.</summary>
    Task<int> GetWeeklyClearedCountAsync(Guid needlyUserId, CancellationToken cancellationToken);

    /// <summary>Restores a cleared action back to the open inbox, resetting its waiting time.</summary>
    Task<bool> RestoreAsync(Guid needlyUserId, Guid actionId, CancellationToken cancellationToken);
}

/// <summary>Contains one authorized entry in a user's Done view.</summary>
public sealed record DoneAction(
    Guid ActionId,
    string RepositoryOwner,
    string RepositoryName,
    string SubjectTitle,
    int SubjectNumber,
    GitHubSubjectType SubjectType,
    string SubjectUrl,
    ActionType Type,
    ActionState State,
    string Reason,
    string? Context,
    string AssigneeDisplay,
    string ClosedReason,
    DateTimeOffset ClosedAt,
    string? ClosedBy,
    TimeSpan TimeSinceClosed,
    string? AuthorLogin,
    ActionAssigneeScope AssigneeScope,
    bool HasBotInvolvement,
    bool CanRestore);

/// <summary>Creates shared filter candidates from Done view entries.</summary>
public static class DoneActionFilterCandidate
{
    /// <summary>Maps a Done view entry to the persistence-neutral matcher input.</summary>
    public static ActionFilterCandidate Create(DoneAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        // Labels, draft state, size bucket, milestone, CODEOWNERS involvement and agent identity are not
        // tracked on DoneAction (that schema predates the ActionFilter v2 criteria added for issue #32 and
        // the v3 agent identity added for issue #35). Passing "no constraint" defaults here means those
        // criteria simply don't narrow Done view results yet, rather than the candidate failing to
        // construct at all.
        return new ActionFilterCandidate(
            action.Type,
            action.State,
            $"{action.RepositoryOwner}/{action.RepositoryName}",
            action.RepositoryOwner,
            action.AuthorLogin,
            action.AssigneeScope,
            action.TimeSinceClosed,
            action.HasBotInvolvement,
            Labels: [],
            IsDraft: null,
            SizeBucket: null,
            Milestone: null,
            RequestedViaCodeowners: false,
            AgentAuthor: null,
            IsSelfOwnedRepository: RepositoryOwnership.IsSelfOwned(action.RepositoryOwner, action.AuthorLogin));
    }
}
