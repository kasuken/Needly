namespace Needly.Domain;

/// <summary>Identifies a per-user action automation effect.</summary>
public enum RuleEffect
{
    /// <summary>Hides the action from the user's active inbox.</summary>
    AutoArchive,

    /// <summary>Hides and suppresses the action for the user.</summary>
    Mute,

    /// <summary>Defers the action for a configured duration.</summary>
    Snooze,

    /// <summary>Presents the action as informational.</summary>
    MarkFyi,

    /// <summary>Pins the action ahead of unpinned work.</summary>
    Pin
}

/// <summary>Represents an enabled or disabled ordered automation rule owned by one user.</summary>
public sealed class AutomationRule
{
    private AutomationRule()
    {
    }

    /// <summary>Gets the rule identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the owning Needly user identifier.</summary>
    public Guid NeedlyUserId { get; private set; }

    /// <summary>Gets the rule name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Gets the normalized rule name.</summary>
    public string NormalizedName { get; private set; } = string.Empty;

    /// <summary>Gets the versioned structured condition JSON.</summary>
    public string FilterJson { get; private set; } = string.Empty;

    /// <summary>Gets the effect applied by this rule.</summary>
    public RuleEffect Effect { get; private set; }

    /// <summary>Gets the snooze duration when <see cref="Effect"/> is <see cref="RuleEffect.Snooze"/>.</summary>
    public TimeSpan? SnoozeDuration { get; private set; }

    /// <summary>Gets whether this rule participates in evaluation.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>Gets the rule evaluation order.</summary>
    public int SortOrder { get; private set; }

    /// <summary>Gets when the rule was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the rule was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates an automation rule.</summary>
    public static AutomationRule Create(
        Guid id,
        Guid needlyUserId,
        string name,
        string filterJson,
        RuleEffect effect,
        TimeSpan? snoozeDuration,
        bool isEnabled,
        int sortOrder,
        DateTimeOffset createdAt)
    {
        if (sortOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sortOrder));
        }

        ValidateEffect(effect, snoozeDuration);
        var timestamp = DomainGuard.Timestamp(createdAt);
        var validatedName = DomainGuard.Required(name, 100, nameof(name));
        return new AutomationRule
        {
            Id = DomainGuard.Required(id, nameof(id)),
            NeedlyUserId = DomainGuard.Required(needlyUserId, nameof(needlyUserId)),
            Name = validatedName,
            NormalizedName = validatedName.ToUpperInvariant(),
            FilterJson = DomainGuard.Required(filterJson, 16000, nameof(filterJson)),
            Effect = effect,
            SnoozeDuration = snoozeDuration,
            IsEnabled = isEnabled,
            SortOrder = sortOrder,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    /// <summary>Updates the editable rule definition.</summary>
    public void Update(
        string name,
        string filterJson,
        RuleEffect effect,
        TimeSpan? snoozeDuration,
        DateTimeOffset updatedAt)
    {
        ValidateEffect(effect, snoozeDuration);
        var validatedName = DomainGuard.Required(name, 100, nameof(name));
        Name = validatedName;
        NormalizedName = validatedName.ToUpperInvariant();
        FilterJson = DomainGuard.Required(filterJson, 16000, nameof(filterJson));
        Effect = effect;
        SnoozeDuration = snoozeDuration;
        UpdatedAt = DomainGuard.NotBefore(updatedAt, CreatedAt, nameof(updatedAt));
    }

    /// <summary>Enables or disables the rule.</summary>
    public void SetEnabled(bool isEnabled, DateTimeOffset updatedAt)
    {
        IsEnabled = isEnabled;
        UpdatedAt = DomainGuard.NotBefore(updatedAt, CreatedAt, nameof(updatedAt));
    }

    /// <summary>Changes the rule's evaluation order.</summary>
    public void Reorder(int sortOrder, DateTimeOffset updatedAt)
    {
        if (sortOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sortOrder));
        }

        SortOrder = sortOrder;
        UpdatedAt = DomainGuard.NotBefore(updatedAt, CreatedAt, nameof(updatedAt));
    }

    private static void ValidateEffect(RuleEffect effect, TimeSpan? snoozeDuration)
    {
        if (!Enum.IsDefined(effect))
        {
            throw new ArgumentOutOfRangeException(nameof(effect));
        }

        if (effect == RuleEffect.Snooze && (snoozeDuration is null || snoozeDuration <= TimeSpan.Zero))
        {
            throw new ArgumentOutOfRangeException(nameof(snoozeDuration), "Snooze rules require a positive duration.");
        }

        if (effect != RuleEffect.Snooze && snoozeDuration is not null)
        {
            throw new ArgumentException("Only snooze rules can define a snooze duration.", nameof(snoozeDuration));
        }
    }
}

/// <summary>Stores the current automated presentation and lifecycle state for one user and action.</summary>
public sealed class ActionDisposition
{
    private ActionDisposition()
    {
    }

    /// <summary>Gets the disposition identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the owning Needly user identifier.</summary>
    public Guid NeedlyUserId { get; private set; }

    /// <summary>Gets the affected action identifier.</summary>
    public Guid ActionId { get; private set; }

    /// <summary>Gets whether the action is archived for this user.</summary>
    public bool IsArchived { get; private set; }

    /// <summary>Gets whether the action is muted for this user.</summary>
    public bool IsMuted { get; private set; }

    /// <summary>Gets when the action should reappear for this user.</summary>
    public DateTimeOffset? SnoozedUntil { get; private set; }

    /// <summary>Gets whether the action is presented as FYI for this user.</summary>
    public bool IsFyi { get; private set; }

    /// <summary>Gets whether the action is pinned for this user.</summary>
    public bool IsPinned { get; private set; }

    /// <summary>Gets when the disposition was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the disposition was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates an empty disposition for one user and action.</summary>
    public static ActionDisposition Create(
        Guid id,
        Guid needlyUserId,
        Guid actionId,
        DateTimeOffset createdAt)
    {
        var timestamp = DomainGuard.Timestamp(createdAt);
        return new ActionDisposition
        {
            Id = DomainGuard.Required(id, nameof(id)),
            NeedlyUserId = DomainGuard.Required(needlyUserId, nameof(needlyUserId)),
            ActionId = DomainGuard.Required(actionId, nameof(actionId)),
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    /// <summary>Applies one rule effect without clearing effects from earlier matching rules.</summary>
    public void Apply(RuleEffect effect, DateTimeOffset? snoozedUntil, DateTimeOffset updatedAt)
    {
        var timestamp = DomainGuard.NotBefore(updatedAt, CreatedAt, nameof(updatedAt));
        switch (effect)
        {
            case RuleEffect.AutoArchive:
                IsArchived = true;
                break;
            case RuleEffect.Mute:
                IsMuted = true;
                break;
            case RuleEffect.Snooze when snoozedUntil > timestamp:
                SnoozedUntil = snoozedUntil.Value.ToUniversalTime();
                break;
            case RuleEffect.Snooze:
                throw new ArgumentOutOfRangeException(nameof(snoozedUntil));
            case RuleEffect.MarkFyi:
                IsFyi = true;
                break;
            case RuleEffect.Pin:
                IsPinned = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(effect));
        }

        UpdatedAt = timestamp;
    }
}

/// <summary>Records one durable, idempotent rule effect applied to an action.</summary>
public sealed class RuleExecution
{
    private RuleExecution()
    {
    }

    /// <summary>Gets the execution identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the owning Needly user identifier.</summary>
    public Guid NeedlyUserId { get; private set; }

    /// <summary>Gets the executed rule identifier.</summary>
    public Guid RuleId { get; private set; }

    /// <summary>Gets the rule name captured when execution occurred.</summary>
    public string RuleName { get; private set; } = string.Empty;

    /// <summary>Gets the affected action identifier.</summary>
    public Guid ActionId { get; private set; }

    /// <summary>Gets the originating event identifier.</summary>
    public Guid EventId { get; private set; }

    /// <summary>Gets the effect that was applied.</summary>
    public RuleEffect Effect { get; private set; }

    /// <summary>Gets the rule order captured when execution occurred.</summary>
    public int RuleOrder { get; private set; }

    /// <summary>Gets the human-readable match and effect explanation.</summary>
    public string Explanation { get; private set; } = string.Empty;

    /// <summary>Gets the unique action-event-rule execution identity.</summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>Gets when the effect executed.</summary>
    public DateTimeOffset ExecutedAt { get; private set; }

    /// <summary>Creates a durable rule execution record.</summary>
    public static RuleExecution Create(
        Guid id,
        Guid needlyUserId,
        Guid ruleId,
        string ruleName,
        Guid actionId,
        Guid eventId,
        RuleEffect effect,
        int ruleOrder,
        string explanation,
        DateTimeOffset executedAt)
    {
        if (ruleOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ruleOrder));
        }

        var userId = DomainGuard.Required(needlyUserId, nameof(needlyUserId));
        var automationRuleId = DomainGuard.Required(ruleId, nameof(ruleId));
        var affectedActionId = DomainGuard.Required(actionId, nameof(actionId));
        var sourceEventId = DomainGuard.Required(eventId, nameof(eventId));
        return new RuleExecution
        {
            Id = DomainGuard.Required(id, nameof(id)),
            NeedlyUserId = userId,
            RuleId = automationRuleId,
            RuleName = DomainGuard.Required(ruleName, 100, nameof(ruleName)),
            ActionId = affectedActionId,
            EventId = sourceEventId,
            Effect = effect,
            RuleOrder = ruleOrder,
            Explanation = DomainGuard.Required(explanation, 1000, nameof(explanation)),
            IdempotencyKey = $"{userId:N}:{automationRuleId:N}:{affectedActionId:N}:{sourceEventId:N}",
            ExecutedAt = DomainGuard.Timestamp(executedAt)
        };
    }
}