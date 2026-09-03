namespace Needly.Domain;

/// <summary>Suppresses future actions for one user's installation subject and assignee.</summary>
public sealed class ActionSuppression
{
    private ActionSuppression()
    {
    }

    /// <summary>Gets the suppression identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the Needly user who muted the subject.</summary>
    public Guid NeedlyUserId { get; private set; }

    /// <summary>Gets the installation containing the subject.</summary>
    public Guid InstallationId { get; private set; }

    /// <summary>Gets the repository containing the subject.</summary>
    public Guid RepositoryId { get; private set; }

    /// <summary>Gets the muted subject type.</summary>
    public GitHubSubjectType SubjectType { get; private set; }

    /// <summary>Gets the repository-scoped subject number.</summary>
    public int SubjectNumber { get; private set; }

    /// <summary>Gets the muted assignee type.</summary>
    public ActionAssigneeType AssigneeType { get; private set; }

    /// <summary>Gets the muted internal assignee identifier.</summary>
    public Guid AssigneeId { get; private set; }

    /// <summary>Gets whether the suppression currently blocks detector creates.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets when the suppression was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the suppression was last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates an active per-user action suppression.</summary>
    public static ActionSuppression Create(
        Guid id,
        Guid needlyUserId,
        NeedlyAction action,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(action);
        var timestamp = DomainGuard.Timestamp(createdAt);
        return new ActionSuppression
        {
            Id = DomainGuard.Required(id, nameof(id)),
            NeedlyUserId = DomainGuard.Required(needlyUserId, nameof(needlyUserId)),
            InstallationId = action.InstallationId,
            RepositoryId = action.RepositoryId,
            SubjectType = action.SubjectType,
            SubjectNumber = action.SubjectNumber,
            AssigneeType = action.AssigneeType,
            AssigneeId = action.AssigneeId,
            IsActive = true,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    /// <summary>Stops this suppression from blocking future action creates.</summary>
    public void Deactivate(DateTimeOffset updatedAt)
    {
        IsActive = false;
        UpdatedAt = DomainGuard.NotBefore(updatedAt, CreatedAt, nameof(updatedAt));
    }
}