namespace Needly.Domain;

/// <summary>Identifies how an action is assigned relative to the current user.</summary>
public enum ActionAssigneeScope
{
    /// <summary>Matches direct and team assignments.</summary>
    Any,

    /// <summary>Matches actions assigned directly to the current user.</summary>
    Me,

    /// <summary>Matches actions assigned to one of the current user's teams.</summary>
    MyTeam
}

/// <summary>Controls whether actions involving bots are included.</summary>
public enum BotInvolvementFilter
{
    /// <summary>Matches actions regardless of bot involvement.</summary>
    Any,

    /// <summary>Matches only actions that involve a bot.</summary>
    OnlyBots,

    /// <summary>Matches only actions that do not involve a bot.</summary>
    ExcludeBots
}

/// <summary>
/// Defines persistence-neutral action criteria shared by saved views and automation rules.
/// Non-empty option collections use OR semantics; different criteria use AND semantics.
/// </summary>
public sealed record ActionFilter
{
    /// <summary>Gets the current serialized filter schema version.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Gets the serialized filter schema version.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Gets the accepted action types, or an empty collection for any type.</summary>
    public ActionType[] Types { get; init; } = [];

    /// <summary>Gets the accepted action states, or an empty collection for any state.</summary>
    public ActionState[] States { get; init; } = [];

    /// <summary>Gets accepted owner-qualified repository names, or an empty collection for any repository.</summary>
    public string[] Repositories { get; init; } = [];

    /// <summary>Gets accepted organization logins, or an empty collection for any organization.</summary>
    public string[] Organizations { get; init; } = [];

    /// <summary>Gets accepted author logins, or an empty collection for any author.</summary>
    public string[] Authors { get; init; } = [];

    /// <summary>Gets the assignment scope relative to the current user.</summary>
    public ActionAssigneeScope AssigneeScope { get; init; }

    /// <summary>Gets the minimum time the action must have been waiting.</summary>
    public TimeSpan? WaitingAtLeast { get; init; }

    /// <summary>Gets the bot involvement criterion.</summary>
    public BotInvolvementFilter BotInvolvement { get; init; }
}

/// <summary>Contains the action and viewer facts consumed by <see cref="ActionFilterMatcher"/>.</summary>
/// <param name="Type">The action type.</param>
/// <param name="State">The action lifecycle state.</param>
/// <param name="Repository">The owner-qualified repository name.</param>
/// <param name="Organization">The repository organization or owner login.</param>
/// <param name="Author">The subject author login, when known.</param>
/// <param name="AssigneeScope">How the action is assigned relative to the viewer.</param>
/// <param name="WaitingDuration">How long the action has waited for attention.</param>
/// <param name="HasBotInvolvement">Whether the subject author or triggering activity involves a bot.</param>
public sealed record ActionFilterCandidate(
    ActionType Type,
    ActionState State,
    string Repository,
    string Organization,
    string? Author,
    ActionAssigneeScope AssigneeScope,
    TimeSpan WaitingDuration,
    bool HasBotInvolvement);

/// <summary>Applies the shared saved-view and rule filter semantics to action facts.</summary>
public static class ActionFilterMatcher
{
    /// <summary>Determines whether all configured criteria match the supplied action facts.</summary>
    /// <param name="filter">The shared filter criteria.</param>
    /// <param name="candidate">The action and viewer facts to inspect.</param>
    /// <returns><see langword="true"/> when every configured criterion matches.</returns>
    public static bool IsMatch(ActionFilter filter, ActionFilterCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(candidate);

        return Contains(filter.Types, candidate.Type) &&
            Contains(filter.States, candidate.State) &&
            Contains(filter.Repositories, candidate.Repository) &&
            Contains(filter.Organizations, candidate.Organization) &&
            Contains(filter.Authors, candidate.Author) &&
            (filter.AssigneeScope == ActionAssigneeScope.Any || filter.AssigneeScope == candidate.AssigneeScope) &&
            (filter.WaitingAtLeast is null || candidate.WaitingDuration >= filter.WaitingAtLeast) &&
            filter.BotInvolvement switch
            {
                BotInvolvementFilter.Any => true,
                BotInvolvementFilter.OnlyBots => candidate.HasBotInvolvement,
                BotInvolvementFilter.ExcludeBots => !candidate.HasBotInvolvement,
                _ => false
            };
    }

    private static bool Contains<T>(IReadOnlyCollection<T> accepted, T value)
        where T : struct, Enum =>
        accepted.Count == 0 || accepted.Contains(value);

    private static bool Contains(IReadOnlyCollection<string> accepted, string? value) =>
        accepted.Count == 0 ||
        (value is not null && accepted.Contains(value, StringComparer.OrdinalIgnoreCase));
}