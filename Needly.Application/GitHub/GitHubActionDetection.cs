using Needly.Domain;

namespace Needly.Application.GitHub;

/// <summary>Detects action changes from one stored GitHub event and the current action state.</summary>
public interface IGitHubActionDetector
{
    /// <summary>Gets the durable detector identity used for idempotency.</summary>
    string Key { get; }

    /// <summary>Gets the detector's deterministic execution order.</summary>
    int Order { get; }

    /// <summary>Produces persistence-agnostic operations for one stored event.</summary>
    Task<IReadOnlyList<GitHubActionOperation>> DetectAsync(
        GitHubActionDetectionContext context,
        CancellationToken cancellationToken);
}

/// <summary>Contains the installation, repository, identities, and actions visible to a detector.</summary>
public sealed record GitHubActionDetectionContext(
    GitHubStoredEvent Event,
    GitHubActionInstallation Installation,
    GitHubActionRepository Repository,
    IReadOnlyList<GitHubActionIdentity> Identities,
    IReadOnlyList<GitHubActionSnapshot> Actions,
    IGitHubActionStateStore State);

/// <summary>Provides durable, repository-scoped state to action detectors.</summary>
public interface IGitHubActionStateStore
{
    /// <summary>Gets durable pull-request identity and head state.</summary>
    Task<GitHubPullRequestState?> GetPullRequestAsync(int pullRequestNumber, CancellationToken cancellationToken);

    /// <summary>Creates or updates durable pull-request identity and head state.</summary>
    Task UpsertPullRequestAsync(GitHubPullRequestState state, CancellationToken cancellationToken);

    /// <summary>Gets durable review-request state for a pull request.</summary>
    Task<IReadOnlyList<GitHubReviewRequestState>> GetReviewRequestsAsync(
        int pullRequestNumber,
        CancellationToken cancellationToken);

    /// <summary>Creates or updates durable review-request state.</summary>
    Task UpsertReviewRequestAsync(GitHubReviewRequestState state, CancellationToken cancellationToken);

    /// <summary>Gets durable reviewer-feedback state for a pull request.</summary>
    Task<IReadOnlyList<GitHubReviewerFeedbackState>> GetReviewerFeedbackAsync(
        int pullRequestNumber,
        CancellationToken cancellationToken);

    /// <summary>Creates or updates durable reviewer-feedback state.</summary>
    Task UpsertReviewerFeedbackAsync(GitHubReviewerFeedbackState state, CancellationToken cancellationToken);

    /// <summary>Gets durable CI state for a pull request.</summary>
    Task<IReadOnlyList<GitHubCheckFailureState>> GetCheckFailuresAsync(
        int pullRequestNumber,
        CancellationToken cancellationToken);

    /// <summary>Creates or updates durable CI state.</summary>
    Task UpsertCheckFailureAsync(GitHubCheckFailureState state, CancellationToken cancellationToken);

    /// <summary>Gets durable response state for a GitHub subject.</summary>
    Task<IReadOnlyList<GitHubResponseState>> GetResponsesAsync(
        GitHubSubjectType subjectType,
        int subjectNumber,
        CancellationToken cancellationToken);

    /// <summary>Creates or updates durable response state for a subject and user.</summary>
    Task UpsertResponseAsync(GitHubResponseState state, CancellationToken cancellationToken);
}

/// <summary>Describes the installation that owns an action event.</summary>
public sealed record GitHubActionInstallation(Guid Id, long GitHubInstallationId);

/// <summary>Describes the repository that owns an action subject.</summary>
public sealed record GitHubActionRepository(
    Guid Id,
    long GitHubRepositoryId,
    string Owner,
    string Name);

/// <summary>Describes a user or team identity available to action detectors.</summary>
public sealed record GitHubActionIdentity(
    ActionAssigneeType Type,
    Guid Id,
    long GitHubId,
    string Login,
    IReadOnlyList<long>? MemberGitHubUserIds = null);

/// <summary>Contains durable identity and current-head state for one pull request.</summary>
public sealed record GitHubPullRequestState(
    int PullRequestNumber,
    long AuthorGitHubUserId,
    string AuthorLogin,
    string HeadSha,
    string Title,
    string Url,
    bool IsDraft,
    DateTimeOffset UpdatedAt,
    bool IsOpen = true,
    int? ApprovalCount = null,
    bool? HasChangesRequested = null,
    GitHubCheckState CheckState = GitHubCheckState.Unknown,
    bool? IsMergeable = null,
    bool? HasConflicts = null,
    DateTimeOffset? ReadinessCheckedAt = null);

/// <summary>Contains durable requested-reviewer state for one pull request and assignee.</summary>
public sealed record GitHubReviewRequestState(
    int PullRequestNumber,
    ActionAssigneeType AssigneeType,
    long GitHubAssigneeId,
    string AssigneeLogin,
    bool IsRequested,
    DateTimeOffset UpdatedAt);

/// <summary>Contains durable outstanding-feedback state for one pull request and reviewer.</summary>
public sealed record GitHubReviewerFeedbackState(
    int PullRequestNumber,
    long ReviewerGitHubUserId,
    string ReviewerLogin,
    long ReviewId,
    bool HasOutstandingChanges,
    int ApproximateUnresolvedCommentCount,
    DateTimeOffset UpdatedAt);

/// <summary>Contains durable failing-check state for one pull request head.</summary>
public sealed record GitHubCheckFailureState(
    int PullRequestNumber,
    string HeadSha,
    string CheckKey,
    string Name,
    string? Url,
    bool IsFailing,
    DateTimeOffset UpdatedAt);

/// <summary>Contains durable coalesced response activity for one subject and user.</summary>
public sealed record GitHubResponseState(
    GitHubSubjectType SubjectType,
    int SubjectNumber,
    long GitHubAssigneeId,
    bool IsPending,
    int TriggerCount,
    long LastTriggerCommentId,
    DateTimeOffset LastTriggeredAt,
    DateTimeOffset UpdatedAt);

/// <summary>Describes the aggregate latest-head check state used for merge readiness.</summary>
public enum GitHubCheckState
{
    /// <summary>The API did not provide enough check data.</summary>
    Unknown,

    /// <summary>At least one check has not completed.</summary>
    Pending,

    /// <summary>At least one completed check failed.</summary>
    Failing,

    /// <summary>All reported checks completed successfully.</summary>
    Passing
}

/// <summary>Contains an authoritative installation API snapshot used to decide merge readiness.</summary>
public sealed record GitHubPullRequestReadiness(
    int PullRequestNumber,
    long AuthorGitHubUserId,
    string AuthorLogin,
    string HeadSha,
    string Title,
    string Url,
    bool IsOpen,
    bool IsDraft,
    int ApprovalCount,
    bool HasChangesRequested,
    GitHubCheckState CheckState,
    bool? IsMergeable,
    bool HasConflicts,
    DateTimeOffset ObservedAt);

/// <summary>Identifies an action independently of its persistence representation.</summary>
public sealed record GitHubActionTarget(
    ActionType Type,
    GitHubSubjectType SubjectType,
    int SubjectNumber,
    ActionAssigneeType AssigneeType,
    long GitHubAssigneeId);

/// <summary>Describes current action state available to a detector.</summary>
public sealed record GitHubActionSnapshot(
    Guid Id,
    GitHubActionTarget Target,
    ActionState State,
    string Title,
    string? Context,
    string Reason,
    DateTimeOffset LastActivityAt);

/// <summary>Describes whether GitHub activity may wake a deferred action.</summary>
public enum ActionEventSignificance
{
    /// <summary>Updates an already active action without changing a user's lifecycle choice.</summary>
    Routine,

    /// <summary>Represents new or explicitly requested work that may wake a deferred action.</summary>
    Significant
}

/// <summary>Represents an explicit action mutation emitted by a detector.</summary>
public abstract record GitHubActionOperation(
    GitHubActionTarget Target,
    DateTimeOffset OccurredAt);

/// <summary>Creates or updates an active action and optionally reactivates its latest terminal instance.</summary>
public sealed record CreateGitHubActionOperation(
    GitHubActionTarget Target,
    string SubjectUrl,
    string Title,
    string? Context,
    string Reason,
    DateTimeOffset OccurredAt,
    bool ReactivateTerminal = false,
    ActionEventSignificance Significance = ActionEventSignificance.Routine)
    : GitHubActionOperation(Target, OccurredAt);

/// <summary>Updates a matching open or snoozed action without creating one.</summary>
public sealed record UpdateGitHubActionOperation(
    GitHubActionTarget Target,
    string Title,
    string? Context,
    string Reason,
    DateTimeOffset OccurredAt)
    : GitHubActionOperation(Target, OccurredAt);

/// <summary>Marks a matching open or snoozed action as done.</summary>
public sealed record ResolveGitHubActionOperation(
    GitHubActionTarget Target,
    DateTimeOffset OccurredAt)
    : GitHubActionOperation(Target, OccurredAt);