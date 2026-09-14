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

/// <summary>Controls whether draft pull requests are included.</summary>
public enum DraftFilter
{
    /// <summary>Matches actions regardless of draft state.</summary>
    Any,

    /// <summary>Matches only pull requests that are drafts.</summary>
    OnlyDrafts,

    /// <summary>Matches only pull requests that are not drafts.</summary>
    ExcludeDrafts
}

/// <summary>Controls whether actions whose review was requested through CODEOWNERS are included.</summary>
public enum CodeownersFilter
{
    /// <summary>Matches actions regardless of how the review was requested.</summary>
    Any,

    /// <summary>Matches only actions whose review was requested through CODEOWNERS.</summary>
    OnlyRequested,

    /// <summary>Matches only actions whose review was not requested through CODEOWNERS.</summary>
    ExcludeRequested
}

/// <summary>
/// Buckets a pull request by the total number of changed lines (additions plus deletions).
/// </summary>
public enum ActionSizeBucket
{
    /// <summary>Fewer than 10 changed lines.</summary>
    XS,

    /// <summary>Fewer than 50 changed lines.</summary>
    S,

    /// <summary>Fewer than 250 changed lines.</summary>
    M,

    /// <summary>Fewer than 1000 changed lines.</summary>
    L,

    /// <summary>1000 or more changed lines.</summary>
    XL
}

/// <summary>Classifies pull requests into an <see cref="ActionSizeBucket"/> by changed line count.</summary>
public static class ActionSizeBucketClassifier
{
    /// <summary>
    /// Buckets a pull request by additions plus deletions: XS &lt; 10, S &lt; 50, M &lt; 250, L &lt; 1000,
    /// otherwise XL.
    /// </summary>
    /// <param name="additions">The number of added lines.</param>
    /// <param name="deletions">The number of deleted lines.</param>
    /// <returns>The size bucket for the total changed line count.</returns>
    public static ActionSizeBucket Classify(int additions, int deletions)
    {
        var changedLines = Math.Max(0, additions) + Math.Max(0, deletions);
        return changedLines switch
        {
            < 10 => ActionSizeBucket.XS,
            < 50 => ActionSizeBucket.S,
            < 250 => ActionSizeBucket.M,
            < 1000 => ActionSizeBucket.L,
            _ => ActionSizeBucket.XL
        };
    }
}

/// <summary>
/// Defines persistence-neutral action criteria shared by saved views and automation rules.
/// Non-empty option collections use OR semantics; different criteria use AND semantics.
/// </summary>
public sealed record ActionFilter
{
    /// <summary>Gets the current serialized filter schema version.</summary>
    public const int CurrentSchemaVersion = 2;

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

    // Schema version 2 additions (issue #32). Kept as a separate block, appended after the
    // version 1 criteria above, to avoid reformatting or reordering existing members.
    /// <summary>Gets the accepted label names, or an empty collection for any label.</summary>
    public string[] Labels { get; init; } = [];

    /// <summary>Gets the pull request draft-state criterion.</summary>
    public DraftFilter IsDraft { get; init; }

    /// <summary>Gets the accepted pull request size buckets, or an empty collection for any size.</summary>
    public ActionSizeBucket[] SizeBuckets { get; init; } = [];

    /// <summary>Gets the accepted milestone titles, or an empty collection for any milestone.</summary>
    public string[] Milestones { get; init; } = [];

    /// <summary>Gets the CODEOWNERS review-request criterion.</summary>
    public CodeownersFilter RequestedViaCodeowners { get; init; }

    // Added for issue #24 (inbox search). Kept as a minimal, standalone addition so this
    // does not conflict with the enriched-filter work for issue #32 above.
    /// <summary>Gets the free-text search term, or <see langword="null"/> for no free-text criterion.</summary>
    /// <remarks>
    /// Matched as a case-insensitive substring against the subject title, reason and context
    /// (see <see cref="ActionFilterCandidate.Title"/>, <see cref="ActionFilterCandidate.Reason"/> and
    /// <see cref="ActionFilterCandidate.Context"/>) by <see cref="ActionFilterMatcher"/>.
    /// </remarks>
    public string? FreeText { get; init; }
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
/// <param name="Labels">The GitHub label names on the subject.</param>
/// <param name="IsDraft">Whether the subject pull request is a draft, or null when not applicable or unknown.</param>
/// <param name="SizeBucket">The pull request size bucket, or null when not applicable or unknown.</param>
/// <param name="Milestone">The subject milestone title, when known.</param>
/// <param name="RequestedViaCodeowners">Whether the review was requested through CODEOWNERS.</param>
/// <param name="Title">The subject title, used to match <see cref="ActionFilter.FreeText"/>. Added for issue #24.</param>
/// <param name="Reason">The action reason text, used to match <see cref="ActionFilter.FreeText"/>. Added for issue #24.</param>
/// <param name="Context">Additional context text, used to match <see cref="ActionFilter.FreeText"/>. Added for issue #24.</param>
public sealed record ActionFilterCandidate(
    ActionType Type,
    ActionState State,
    string Repository,
    string Organization,
    string? Author,
    ActionAssigneeScope AssigneeScope,
    TimeSpan WaitingDuration,
    bool HasBotInvolvement,
    string[] Labels,
    bool? IsDraft,
    ActionSizeBucket? SizeBucket,
    string? Milestone,
    bool RequestedViaCodeowners,
    string? Title = null,
    string? Reason = null,
    string? Context = null);

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
            } &&
            // Schema version 2 additions (issue #32). Kept as a separate block, appended after the
            // version 1 criteria above, to avoid reformatting or reordering existing logic.
            ContainsAny(filter.Labels, candidate.Labels) &&
            filter.IsDraft switch
            {
                DraftFilter.Any => true,
                DraftFilter.OnlyDrafts => candidate.IsDraft == true,
                DraftFilter.ExcludeDrafts => candidate.IsDraft != true,
                _ => false
            } &&
            (filter.SizeBuckets.Length == 0 ||
                (candidate.SizeBucket is { } sizeBucket && filter.SizeBuckets.Contains(sizeBucket))) &&
            Contains(filter.Milestones, candidate.Milestone) &&
            filter.RequestedViaCodeowners switch
            {
                CodeownersFilter.Any => true,
                CodeownersFilter.OnlyRequested => candidate.RequestedViaCodeowners,
                CodeownersFilter.ExcludeRequested => !candidate.RequestedViaCodeowners,
                _ => false
            } &&
            // Added for issue #24 (inbox search).
            MatchesFreeText(filter.FreeText, candidate);
    }

    private static bool Contains<T>(IReadOnlyCollection<T> accepted, T value)
        where T : struct, Enum =>
        accepted.Count == 0 || accepted.Contains(value);

    private static bool Contains(IReadOnlyCollection<string> accepted, string? value) =>
        accepted.Count == 0 ||
        (value is not null && accepted.Contains(value, StringComparer.OrdinalIgnoreCase));

    private static bool ContainsAny(IReadOnlyCollection<string> accepted, IReadOnlyCollection<string> values) =>
        accepted.Count == 0 ||
        values.Any(value => accepted.Contains(value, StringComparer.OrdinalIgnoreCase));

    // Added for issue #24 (inbox search): case-insensitive substring match against title, reason and
    // context. A null or empty FreeText criterion matches everything, consistent with the other criteria.
    private static bool MatchesFreeText(string? freeText, ActionFilterCandidate candidate) =>
        string.IsNullOrEmpty(freeText) ||
        ContainsIgnoreCase(candidate.Title, freeText) ||
        ContainsIgnoreCase(candidate.Reason, freeText) ||
        ContainsIgnoreCase(candidate.Context, freeText);

    private static bool ContainsIgnoreCase(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
