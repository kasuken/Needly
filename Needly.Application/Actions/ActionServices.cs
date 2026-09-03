using Needly.Domain;

namespace Needly.Application.Actions;

/// <summary>Changes actions owned by an authenticated Needly user.</summary>
public interface IActionLifecycleService
{
    /// <summary>Archives a visible action and returns a durable undo handle.</summary>
    Task<ActionLifecycleChange?> ArchiveAsync(
        Guid needlyUserId,
        Guid actionId,
        CancellationToken cancellationToken);

    /// <summary>Snoozes a visible action until an explicit UTC instant.</summary>
    Task<ActionLifecycleChange?> SnoozeAsync(
        Guid needlyUserId,
        Guid actionId,
        DateTimeOffset snoozedUntil,
        CancellationToken cancellationToken);

    /// <summary>Mutes a visible action and suppresses future actions for its subject and assignee.</summary>
    Task<ActionLifecycleChange?> MuteAsync(
        Guid needlyUserId,
        Guid actionId,
        CancellationToken cancellationToken);

    /// <summary>Restores the persisted action state captured by a lifecycle change.</summary>
    Task<bool> UndoAsync(
        Guid needlyUserId,
        Guid undoId,
        CancellationToken cancellationToken);
}

/// <summary>Resurfaces actions whose persisted snooze deadline has elapsed.</summary>
public interface IActionSnoozeService
{
    /// <summary>Opens all due snoozed actions and returns the number changed.</summary>
    Task<int> ResurfaceDueAsync(CancellationToken cancellationToken);
}

/// <summary>Broadcasts committed action changes without carrying user-specific state.</summary>
public interface IActionChangeBroadcaster
{
    /// <summary>Occurs after a durable action change commits.</summary>
    event Action? Changed;

    /// <summary>Notifies subscribers that authorized inbox data may have changed.</summary>
    void Publish();
}

/// <summary>Identifies a durable lifecycle change that can be undone.</summary>
public sealed record ActionLifecycleChange(
    Guid UndoId,
    Guid ActionId,
    ActionState State,
    DateTimeOffset? SnoozedUntil);

/// <summary>Evaluates open actions for waiting and inactivity risk.</summary>
public interface IActionRiskEvaluator
{
    /// <summary>Updates persisted risk state for all currently open actions.</summary>
    Task<int> EvaluateAsync(CancellationToken cancellationToken);
}