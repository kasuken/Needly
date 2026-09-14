namespace Needly.Domain;

/// <summary>
/// Represents a durable work item derived from GitHub activity.
/// </summary>
public sealed class NeedlyAction
{
    private NeedlyAction()
    {
    }

    /// <summary>Gets the action identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the installation that owns the action.</summary>
    public Guid InstallationId { get; private set; }

    /// <summary>Gets the stable identity used to coalesce matching events.</summary>
    public ActionKey Key { get; private set; }

    /// <summary>Gets the type of work required.</summary>
    public ActionType Type { get; private set; }

    /// <summary>Gets the current lifecycle state.</summary>
    public ActionState State { get; private set; }

    /// <summary>Gets whether the assignee is a user or team.</summary>
    public ActionAssigneeType AssigneeType { get; private set; }

    /// <summary>Gets the GitHub user or team identifier.</summary>
    public Guid AssigneeId { get; private set; }

    /// <summary>Gets the subject repository identifier.</summary>
    public Guid RepositoryId { get; private set; }

    /// <summary>Gets whether the subject is a pull request or issue.</summary>
    public GitHubSubjectType SubjectType { get; private set; }

    /// <summary>Gets the repository-scoped subject number.</summary>
    public int SubjectNumber { get; private set; }

    /// <summary>Gets the validated GitHub subject link.</summary>
    public GitHubDeepLink SubjectUrl { get; private set; }

    /// <summary>Gets the action title.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Gets optional context needed to perform the action.</summary>
    public string? Context { get; private set; }

    /// <summary>Gets the explanation of why the action needs attention.</summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>Gets when the action was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the action record was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets when the current wait for attention began.</summary>
    public DateTimeOffset WaitingSince { get; private set; }

    /// <summary>Gets when a snoozed action should return to the inbox.</summary>
    public DateTimeOffset? SnoozedUntil { get; private set; }

    /// <summary>Gets the latest GitHub activity represented by this action.</summary>
    public DateTimeOffset LastActivityAt { get; private set; }

    /// <summary>Gets whether the open action has exceeded an attention threshold.</summary>
    public bool IsAtRisk { get; private set; }

    /// <summary>Gets the current waiting or inactivity risk explanation.</summary>
    public string? RiskReason { get; private set; }

    /// <summary>Gets the GitHub subject author login when known.</summary>
    public string? AuthorLogin { get; private set; }

    /// <summary>Gets whether the subject author or triggering activity involved a bot.</summary>
    public bool HasBotInvolvement { get; private set; }

    // Schema version 2 filter facts (issue #32). Kept as a separate block, appended after the
    // existing filter-metadata members above, to avoid reformatting or reordering existing code.
    /// <summary>Gets the GitHub label names on the subject.</summary>
    public string[] Labels { get; private set; } = [];

    /// <summary>Gets whether the subject pull request is a draft, or null when not applicable or unknown.</summary>
    public bool? IsDraft { get; private set; }

    /// <summary>Gets the pull request size bucket derived from changed lines, or null when not applicable or unknown.</summary>
    public ActionSizeBucket? SizeBucket { get; private set; }

    /// <summary>Gets the subject milestone title, when known.</summary>
    public string? Milestone { get; private set; }

    /// <summary>
    /// Gets whether the review was requested through a CODEOWNERS entry. Always <see langword="false"/>
    /// until CODEOWNERS parsing is implemented.
    /// </summary>
    public bool RequestedViaCodeowners { get; private set; }

    // Schema version 3 review risk facts (issue #34). Kept as a separate block, appended after the
    // schema version 2 filter facts above, to avoid reformatting or reordering existing members.
    // Issue #35 (agent-authored PR detection), running concurrently in another branch, also extends
    // this class with its own schema version 3 facts; each addition is a clearly separated, minimal,
    // appended block so both are easy to merge.
    /// <summary>
    /// Gets the pull request's overall review risk level, or null when not applicable (the subject is
    /// an issue) or not yet computed.
    /// </summary>
    public ReviewRiskLevel? ReviewRiskLevel { get; private set; }

    /// <summary>
    /// Gets the risk signal names that produced <see cref="ReviewRiskLevel"/> (for example
    /// "authentication", "database migration"). Never populated without a level.
    /// </summary>
    public string[] ReviewRiskSignals { get; private set; } = [];

    /// <summary>
    /// Creates an action assigned to a GitHub user.
    /// </summary>
    /// <param name="id">The action identifier.</param>
    /// <param name="type">The work type.</param>
    /// <param name="repository">The subject repository.</param>
    /// <param name="assignee">The assigned GitHub user.</param>
    /// <param name="subjectType">The subject type.</param>
    /// <param name="subjectNumber">The subject number.</param>
    /// <param name="subjectUrl">The GitHub subject URL.</param>
    /// <param name="title">The action title.</param>
    /// <param name="context">Optional action context.</param>
    /// <param name="reason">Why the action needs attention.</param>
    /// <param name="createdAt">The explicit creation timestamp.</param>
    /// <returns>A new open action.</returns>
    public static NeedlyAction CreateForUser(
        Guid id,
        ActionType type,
        Repository repository,
        GitHubUser assignee,
        GitHubSubjectType subjectType,
        int subjectNumber,
        string subjectUrl,
        string title,
        string? context,
        string reason,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(assignee);

        return Create(
            id,
            type,
            repository,
            ActionAssigneeType.User,
            assignee.Id,
            subjectType,
            subjectNumber,
            subjectUrl,
            title,
            context,
            reason,
            createdAt);
    }

    /// <summary>
    /// Creates an action assigned to a GitHub team.
    /// </summary>
    /// <param name="id">The action identifier.</param>
    /// <param name="type">The work type.</param>
    /// <param name="repository">The subject repository.</param>
    /// <param name="assignee">The assigned GitHub team.</param>
    /// <param name="subjectType">The subject type.</param>
    /// <param name="subjectNumber">The subject number.</param>
    /// <param name="subjectUrl">The GitHub subject URL.</param>
    /// <param name="title">The action title.</param>
    /// <param name="context">Optional action context.</param>
    /// <param name="reason">Why the action needs attention.</param>
    /// <param name="createdAt">The explicit creation timestamp.</param>
    /// <returns>A new open action.</returns>
    public static NeedlyAction CreateForTeam(
        Guid id,
        ActionType type,
        Repository repository,
        Team assignee,
        GitHubSubjectType subjectType,
        int subjectNumber,
        string subjectUrl,
        string title,
        string? context,
        string reason,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(assignee);

        if (repository.InstallationId != assignee.InstallationId)
        {
            throw new ArgumentException("The team and repository must belong to the same installation.", nameof(assignee));
        }

        return Create(
            id,
            type,
            repository,
            ActionAssigneeType.Team,
            assignee.Id,
            subjectType,
            subjectNumber,
            subjectUrl,
            title,
            context,
            reason,
            createdAt);
    }

    /// <summary>
    /// Applies a matching GitHub event and advances action activity without changing its identity.
    /// </summary>
    /// <param name="key">The stable key produced by the incoming event.</param>
    /// <param name="title">The latest action title.</param>
    /// <param name="context">The latest optional context.</param>
    /// <param name="reason">The latest reason the action needs attention.</param>
    /// <param name="activityAt">When the represented GitHub activity occurred.</param>
    /// <param name="updatedAt">When Needly applied the event.</param>
    public void ApplyEvent(
        ActionKey key,
        string title,
        string? context,
        string reason,
        DateTimeOffset activityAt,
        DateTimeOffset updatedAt)
    {
        if (key != Key)
        {
            throw new ArgumentException("The event identity does not match this action.", nameof(key));
        }

        if (State is not (ActionState.Open or ActionState.Snoozed))
        {
            throw new InvalidOperationException("Only active actions can receive event updates.");
        }

        Title = DomainGuard.Required(title, 500, nameof(title));
        Context = DomainGuard.Optional(context, 4000, nameof(context));
        Reason = DomainGuard.Required(reason, 1000, nameof(reason));

        var normalizedActivityAt = DomainGuard.Timestamp(activityAt);
        if (normalizedActivityAt > LastActivityAt)
        {
            LastActivityAt = normalizedActivityAt;
            ClearRisk();
        }

        var normalizedUpdatedAt = DomainGuard.NotBefore(updatedAt, CreatedAt, nameof(updatedAt));
        if (normalizedUpdatedAt > UpdatedAt)
        {
            UpdatedAt = normalizedUpdatedAt;
        }
    }

    /// <summary>
    /// Changes the action lifecycle state.
    /// </summary>
    /// <param name="state">The new lifecycle state.</param>
    /// <param name="changedAt">The explicit state-change timestamp.</param>
    public void ChangeState(ActionState state, DateTimeOffset changedAt)
    {
        var timestamp = DomainGuard.NotBefore(changedAt, CreatedAt, nameof(changedAt));
        if (state == State)
        {
            return;
        }

        State = state;
        UpdatedAt = timestamp > UpdatedAt ? timestamp : UpdatedAt;

        if (state != ActionState.Snoozed)
        {
            SnoozedUntil = null;
        }

        if (state == ActionState.Open)
        {
            WaitingSince = timestamp;
        }
        else
        {
            ClearRisk();
        }
    }

    /// <summary>Defers the action until an explicit future instant.</summary>
    /// <param name="snoozedUntil">The UTC instant when the action should reopen.</param>
    /// <param name="changedAt">The explicit state-change timestamp.</param>
    public void Snooze(DateTimeOffset snoozedUntil, DateTimeOffset changedAt)
    {
        var timestamp = DomainGuard.NotBefore(changedAt, CreatedAt, nameof(changedAt));
        var deadline = DomainGuard.Timestamp(snoozedUntil);
        if (deadline <= timestamp)
        {
            throw new ArgumentOutOfRangeException(
                nameof(snoozedUntil),
                snoozedUntil,
                "The snooze deadline must be later than the state-change timestamp.");
        }

        State = ActionState.Snoozed;
        SnoozedUntil = deadline;
        UpdatedAt = timestamp > UpdatedAt ? timestamp : UpdatedAt;
        ClearRisk();
    }

    /// <summary>Restores a lifecycle state captured by a durable undo operation.</summary>
    /// <param name="state">The state to restore.</param>
    /// <param name="snoozedUntil">The previous snooze deadline, when applicable.</param>
    /// <param name="changedAt">The explicit restore timestamp.</param>
    public void RestoreLifecycle(
        ActionState state,
        DateTimeOffset? snoozedUntil,
        DateTimeOffset changedAt)
    {
        var timestamp = DomainGuard.NotBefore(changedAt, CreatedAt, nameof(changedAt));
        if (state == ActionState.Snoozed && snoozedUntil is null)
        {
            throw new ArgumentException("A snoozed action requires a deadline.", nameof(snoozedUntil));
        }

        State = state;
        SnoozedUntil = state == ActionState.Snoozed
            ? DomainGuard.Timestamp(snoozedUntil!.Value)
            : null;
        UpdatedAt = timestamp > UpdatedAt ? timestamp : UpdatedAt;
        if (state == ActionState.Open)
        {
            WaitingSince = timestamp;
        }
        else
        {
            ClearRisk();
        }
    }

    /// <summary>Marks an open action as exceeding an attention threshold.</summary>
    /// <param name="reason">The threshold-specific risk explanation.</param>
    public void MarkAtRisk(string reason)
    {
        if (State != ActionState.Open)
        {
            throw new InvalidOperationException("Only open actions can be marked at risk.");
        }

        IsAtRisk = true;
        RiskReason = DomainGuard.Required(reason, 1000, nameof(reason));
    }

    /// <summary>Clears any current attention risk marker.</summary>
    public void ClearRisk()
    {
        IsAtRisk = false;
        RiskReason = null;
    }

    /// <summary>Updates the persistence-neutral facts used by Saved Views and Rules.</summary>
    /// <param name="authorLogin">The subject author login when known.</param>
    /// <param name="hasBotInvolvement">Whether the subject author or current activity involves a bot.</param>
    public void UpdateFilterMetadata(string? authorLogin, bool hasBotInvolvement)
    {
        AuthorLogin = DomainGuard.Optional(authorLogin, 100, nameof(authorLogin));
        HasBotInvolvement = hasBotInvolvement;
    }

    /// <summary>
    /// Updates the persistence-neutral subject facts added for schema version 2 (issue #32) and used
    /// by Saved Views and Rules. Pass through the action's current values for facts an event's payload
    /// does not carry, so unrelated events do not clobber previously learned facts.
    /// </summary>
    /// <param name="labels">The GitHub label names on the subject.</param>
    /// <param name="isDraft">Whether the subject pull request is a draft, or null when not applicable or unknown.</param>
    /// <param name="sizeBucket">The pull request size bucket, or null when not applicable or unknown.</param>
    /// <param name="milestone">The subject milestone title, when known.</param>
    /// <param name="requestedViaCodeowners">Whether the review was requested through CODEOWNERS.</param>
    public void UpdateSubjectMetadata(
        string[] labels,
        bool? isDraft,
        ActionSizeBucket? sizeBucket,
        string? milestone,
        bool requestedViaCodeowners)
    {
        Labels = DomainGuard.Labels(labels, nameof(labels));
        IsDraft = isDraft;
        SizeBucket = sizeBucket;
        Milestone = DomainGuard.Optional(milestone, 200, nameof(milestone));
        RequestedViaCodeowners = requestedViaCodeowners;
    }

    /// <summary>
    /// Updates the review risk classification added for schema version 3 (issue #34) and used by
    /// Saved Views and Rules. Pass through the action's current values when an event did not recompute
    /// risk, so unrelated events do not clobber a previously computed classification.
    /// </summary>
    /// <param name="level">
    /// The overall review risk level, or null when not applicable (the subject is an issue) or not yet
    /// computed.
    /// </param>
    /// <param name="matchedSignals">The matched risk signal names backing <paramref name="level"/>.</param>
    public void UpdateReviewRisk(ReviewRiskLevel? level, string[] matchedSignals)
    {
        ReviewRiskLevel = level;
        ReviewRiskSignals = level is null ? [] : DomainGuard.Labels(matchedSignals, nameof(matchedSignals));
    }

    private static NeedlyAction Create(
        Guid id,
        ActionType type,
        Repository repository,
        ActionAssigneeType assigneeType,
        Guid assigneeId,
        GitHubSubjectType subjectType,
        int subjectNumber,
        string subjectUrl,
        string title,
        string? context,
        string reason,
        DateTimeOffset createdAt)
    {
        DomainGuard.Positive(subjectNumber, nameof(subjectNumber));
        var timestamp = DomainGuard.Timestamp(createdAt);

        return new NeedlyAction
        {
            Id = DomainGuard.Required(id, nameof(id)),
            InstallationId = repository.InstallationId,
            Key = ActionKey.Create(type, repository.Id, subjectType, subjectNumber, assigneeType, assigneeId),
            Type = type,
            State = ActionState.Open,
            AssigneeType = assigneeType,
            AssigneeId = DomainGuard.Required(assigneeId, nameof(assigneeId)),
            RepositoryId = repository.Id,
            SubjectType = subjectType,
            SubjectNumber = subjectNumber,
            SubjectUrl = GitHubDeepLink.Create(subjectUrl, repository.Owner, repository.Name, subjectType, subjectNumber),
            Title = DomainGuard.Required(title, 500, nameof(title)),
            Context = DomainGuard.Optional(context, 4000, nameof(context)),
            Reason = DomainGuard.Required(reason, 1000, nameof(reason)),
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
            WaitingSince = timestamp,
            LastActivityAt = timestamp
        };
    }
}