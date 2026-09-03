using Needly.Domain;

namespace Needly.Web.Components.Rules;

/// <summary>Contains a validated automation rule editor result.</summary>
public sealed record RuleEditResult(
    string Name,
    ActionFilter Filter,
    RuleEffect Effect,
    TimeSpan? SnoozeDuration);