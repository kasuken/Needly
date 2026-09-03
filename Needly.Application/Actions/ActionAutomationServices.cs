using Needly.Domain;

namespace Needly.Application.Actions;

/// <summary>Contains a built-in or user-defined view and its authorized open count.</summary>
public sealed record SavedViewItem(
    string Key,
    Guid? Id,
    string Name,
    ActionFilter Filter,
    int SortOrder,
    int OpenCount,
    bool IsBuiltIn);

/// <summary>Contains an editable automation rule.</summary>
public sealed record AutomationRuleItem(
    Guid Id,
    string Name,
    ActionFilter Filter,
    RuleEffect Effect,
    TimeSpan? SnoozeDuration,
    bool IsEnabled,
    int SortOrder);

/// <summary>Contains one displayed automation execution.</summary>
public sealed record RuleExecutionItem(
    Guid Id,
    Guid RuleId,
    string RuleName,
    Guid ActionId,
    string ActionTitle,
    RuleEffect Effect,
    string Explanation,
    DateTimeOffset ExecutedAt);

/// <summary>Manages built-in and user-defined Saved Views.</summary>
public interface ISavedViewService
{
    /// <summary>Gets all built-in and saved views with authorized counts.</summary>
    Task<IReadOnlyList<SavedViewItem>> GetAsync(Guid needlyUserId, CancellationToken cancellationToken);

    /// <summary>Creates an ordered user-defined view.</summary>
    Task<SavedViewItem> CreateAsync(
        Guid needlyUserId,
        string name,
        ActionFilter filter,
        CancellationToken cancellationToken);

    /// <summary>Updates a user-owned view.</summary>
    Task<bool> UpdateAsync(
        Guid needlyUserId,
        Guid viewId,
        string name,
        ActionFilter filter,
        CancellationToken cancellationToken);

    /// <summary>Deletes a user-owned view.</summary>
    Task<bool> DeleteAsync(Guid needlyUserId, Guid viewId, CancellationToken cancellationToken);

    /// <summary>Moves a user-owned view by one position.</summary>
    Task<bool> MoveAsync(Guid needlyUserId, Guid viewId, int direction, CancellationToken cancellationToken);
}

/// <summary>Manages and displays per-user automation rules.</summary>
public interface IAutomationRuleService
{
    /// <summary>Gets the user's ordered rules.</summary>
    Task<IReadOnlyList<AutomationRuleItem>> GetAsync(Guid needlyUserId, CancellationToken cancellationToken);

    /// <summary>Creates an ordered automation rule.</summary>
    Task<AutomationRuleItem> CreateAsync(
        Guid needlyUserId,
        string name,
        ActionFilter filter,
        RuleEffect effect,
        TimeSpan? snoozeDuration,
        CancellationToken cancellationToken);

    /// <summary>Updates a user-owned automation rule.</summary>
    Task<bool> UpdateAsync(
        Guid needlyUserId,
        Guid ruleId,
        string name,
        ActionFilter filter,
        RuleEffect effect,
        TimeSpan? snoozeDuration,
        CancellationToken cancellationToken);

    /// <summary>Enables or disables a user-owned automation rule.</summary>
    Task<bool> SetEnabledAsync(
        Guid needlyUserId,
        Guid ruleId,
        bool isEnabled,
        CancellationToken cancellationToken);

    /// <summary>Deletes a user-owned automation rule.</summary>
    Task<bool> DeleteAsync(Guid needlyUserId, Guid ruleId, CancellationToken cancellationToken);

    /// <summary>Moves a user-owned rule by one evaluation position.</summary>
    Task<bool> MoveAsync(Guid needlyUserId, Guid ruleId, int direction, CancellationToken cancellationToken);

    /// <summary>Gets recent execution explanations for the user.</summary>
    Task<IReadOnlyList<RuleExecutionItem>> GetHistoryAsync(
        Guid needlyUserId,
        int maximumCount,
        CancellationToken cancellationToken);
}

/// <summary>Defines the Saved Views that are always available.</summary>
public static class BuiltInSavedViews
{
    private static readonly ActionType[] AttentionTypes =
    [
        ActionType.Review,
        ActionType.Respond,
        ActionType.Fix,
        ActionType.Resolve,
        ActionType.Merge,
        ActionType.Decide,
        ActionType.FollowUp
    ];

    /// <summary>Gets all built-in views in display order.</summary>
    public static IReadOnlyList<SavedViewItem> All { get; } =
    [
        new("needs-me", null, "Needs me", new ActionFilter
        {
            Types = AttentionTypes,
            States = [ActionState.Open],
            AssigneeScope = ActionAssigneeScope.Me
        }, 0, 0, true),
        new("needs-my-team", null, "Needs my team", new ActionFilter
        {
            Types = AttentionTypes,
            States = [ActionState.Open],
            AssigneeScope = ActionAssigneeScope.MyTeam
        }, 1, 0, true),
        new("waiting-on-others", null, "Waiting on others", new ActionFilter
        {
            Types = [ActionType.FollowUp, ActionType.Monitor],
            States = [ActionState.Open],
            WaitingAtLeast = TimeSpan.FromDays(1)
        }, 2, 0, true),
        new("fyi", null, "FYI", new ActionFilter
        {
            Types = [ActionType.FYI, ActionType.Monitor],
            States = [ActionState.Open]
        }, 3, 0, true)
    ];
}