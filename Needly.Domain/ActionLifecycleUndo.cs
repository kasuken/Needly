namespace Needly.Domain;

/// <summary>Persists the prior state needed to undo one user lifecycle change.</summary>
public sealed class ActionLifecycleUndo
{
    private ActionLifecycleUndo()
    {
    }

    /// <summary>Gets the undo identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the Needly user who owns this undo operation.</summary>
    public Guid NeedlyUserId { get; private set; }

    /// <summary>Gets the changed action.</summary>
    public Guid ActionId { get; private set; }

    /// <summary>Gets the lifecycle state that preceded the change.</summary>
    public ActionState PreviousState { get; private set; }

    /// <summary>Gets the previous snooze deadline.</summary>
    public DateTimeOffset? PreviousSnoozedUntil { get; private set; }

    /// <summary>Gets the state applied by the change.</summary>
    public ActionState AppliedState { get; private set; }

    /// <summary>Gets the suppression created by a mute change, when present.</summary>
    public Guid? SuppressionId { get; private set; }

    /// <summary>Gets when the lifecycle change occurred.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when this undo was consumed.</summary>
    public DateTimeOffset? UsedAt { get; private set; }

    /// <summary>Creates a durable undo record from an action's current state.</summary>
    public static ActionLifecycleUndo Create(
        Guid id,
        Guid needlyUserId,
        NeedlyAction action,
        ActionState appliedState,
        Guid? suppressionId,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new ActionLifecycleUndo
        {
            Id = DomainGuard.Required(id, nameof(id)),
            NeedlyUserId = DomainGuard.Required(needlyUserId, nameof(needlyUserId)),
            ActionId = action.Id,
            PreviousState = action.State,
            PreviousSnoozedUntil = action.SnoozedUntil,
            AppliedState = appliedState,
            SuppressionId = suppressionId,
            CreatedAt = DomainGuard.Timestamp(createdAt)
        };
    }

    /// <summary>Marks this undo record as consumed.</summary>
    public void MarkUsed(DateTimeOffset usedAt)
    {
        if (UsedAt is not null)
        {
            throw new InvalidOperationException("The lifecycle change has already been undone.");
        }

        UsedAt = DomainGuard.NotBefore(usedAt, CreatedAt, nameof(usedAt));
    }
}