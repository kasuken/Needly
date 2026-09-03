using Needly.Application.Actions;

namespace Needly.Web.Components.Rules;

/// <summary>Requests one rule ordering move.</summary>
public sealed record RuleMoveRequest(AutomationRuleItem Rule, int Direction);

/// <summary>Requests an automation rule enabled-state change.</summary>
public sealed record RuleToggleRequest(AutomationRuleItem Rule, bool IsEnabled);