namespace Needly.Domain;

/// <summary>
/// Identifies the work a Needly action asks an assignee to perform.
/// </summary>
public enum ActionType
{
    /// <summary>Review a pull request or issue.</summary>
    Review,

    /// <summary>Respond to a question, mention, or comment.</summary>
    Respond,

    /// <summary>Fix a failing or blocked change.</summary>
    Fix,

    /// <summary>Resolve outstanding feedback or discussion.</summary>
    Resolve,

    /// <summary>Merge a change that is ready.</summary>
    Merge,

    /// <summary>Make a decision that is blocking progress.</summary>
    Decide,

    /// <summary>Follow up on stalled work.</summary>
    FollowUp,

    /// <summary>Monitor a subject for a future state.</summary>
    Monitor,

    /// <summary>Read information that requires no direct response.</summary>
    FYI
}

/// <summary>
/// Describes the lifecycle state of a Needly action.
/// </summary>
public enum ActionState
{
    /// <summary>The action currently requires attention.</summary>
    Open,

    /// <summary>The action is temporarily deferred.</summary>
    Snoozed,

    /// <summary>The action was removed from the active inbox.</summary>
    Archived,

    /// <summary>The action and its future activity are suppressed.</summary>
    Muted,

    /// <summary>The underlying work was completed.</summary>
    Done
}

/// <summary>
/// Identifies whether an action is assigned to a GitHub user or team.
/// </summary>
public enum ActionAssigneeType
{
    /// <summary>The assignee is a GitHub user.</summary>
    User,

    /// <summary>The assignee is a GitHub team.</summary>
    Team
}

/// <summary>
/// Identifies the kind of GitHub subject associated with an action.
/// </summary>
public enum GitHubSubjectType
{
    /// <summary>The subject is a pull request.</summary>
    PullRequest,

    /// <summary>The subject is an issue.</summary>
    Issue
}