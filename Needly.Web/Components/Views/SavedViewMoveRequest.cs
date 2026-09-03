using Needly.Application.Actions;

namespace Needly.Web.Components.Views;

/// <summary>Requests one Saved View ordering move.</summary>
public sealed record SavedViewMoveRequest(SavedViewItem View, int Direction);