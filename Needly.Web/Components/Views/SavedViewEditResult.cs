using Needly.Domain;

namespace Needly.Web.Components.Views;

/// <summary>Contains a validated Saved View editor result.</summary>
public sealed record SavedViewEditResult(string Name, ActionFilter Filter);