namespace Needly.Application.GitHub;

/// <summary>Identifies why one of the user's own pull requests is waiting on somebody else.</summary>
public enum WaitingOnOthersReason
{
    /// <summary>The pull request has no reviewer currently requested and was never reviewed.</summary>
    NoReviewerAssigned,

    /// <summary>A review was requested and the reviewer has not yet responded.</summary>
    AwaitingReview,

    /// <summary>The author addressed requested changes and the reviewer has not re-reviewed since.</summary>
    AwaitingReReview
}

/// <summary>Contains one of the user's open pull requests that is blocked on somebody else.</summary>
public sealed record WaitingOnOthersItem(
    Guid RepositoryId,
    string RepositoryOwner,
    string RepositoryName,
    int SubjectNumber,
    string SubjectTitle,
    string SubjectUrl,
    bool IsDraft,
    WaitingOnOthersReason Reason,
    string BlockingParty,
    DateTimeOffset WaitingSince,
    TimeSpan WaitingDuration);

/// <summary>Queries the authenticated user's own open pull requests that are blocked on others.</summary>
public interface IWaitingOnOthersService
{
    /// <summary>Gets the user's open pull requests waiting on a reviewer, across active installations.</summary>
    Task<IReadOnlyList<WaitingOnOthersItem>> GetWaitingAsync(
        Guid needlyUserId,
        CancellationToken cancellationToken);
}
