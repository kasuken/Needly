using Needly.Application.GitHub;

namespace Needly.Web.Components.Inbox;

/// <summary>Identifies a preset or custom snooze selection from an inbox row.</summary>
public enum ActionSnoozeChoice
{
    LaterToday,
    Tomorrow,
    NextWeek,
    Custom
}

/// <summary>Requests a snooze choice for one visible inbox action.</summary>
public sealed record ActionSnoozeRequest(
    VisibleAction Action,
    ActionSnoozeChoice Choice);