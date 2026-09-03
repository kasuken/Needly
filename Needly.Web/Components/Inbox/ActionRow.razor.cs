using Microsoft.AspNetCore.Components;
using MudBlazor;
using Needly.Application.GitHub;
using Needly.Domain;

namespace Needly.Web.Components.Inbox;

public partial class ActionRow
{
    [Parameter, EditorRequired]
    public VisibleAction Action { get; set; } = null!;

    [Parameter]
    public bool Selected { get; set; }

    [Parameter]
    public EventCallback<VisibleAction> OnArchive { get; set; }

    [Parameter]
    public EventCallback<ActionSnoozeRequest> OnSnooze { get; set; }

    [Parameter]
    public EventCallback<VisibleAction> OnMute { get; set; }

    private string CssClass => $"inbox-action inbox-action--{Action.Type.ToString().ToLowerInvariant()}" +
        (Selected ? " inbox-action--selected" : string.Empty);

    private string ActionIcon => Action.Type switch
    {
        ActionType.Review => Icons.Material.Outlined.RateReview,
        ActionType.Fix => Icons.Material.Outlined.Build,
        ActionType.Resolve => Icons.Material.Outlined.TaskAlt,
        ActionType.Merge => Icons.Material.Outlined.Merge,
        ActionType.Respond => Icons.Material.Outlined.Reply,
        ActionType.FollowUp => Icons.Material.Outlined.Update,
        ActionType.FYI => Icons.Material.Outlined.Visibility,
        _ => Icons.Material.Outlined.RadioButtonChecked
    };

    private string SubjectLabel =>
        Action.SubjectType == GitHubSubjectType.PullRequest ? "PR" : "Issue";

    private string PrimaryLabel => Action.Type == ActionType.Review
        ? "Review on GitHub"
        : "Open on GitHub";

    private string WaitingLabel => FormatDuration(Action.WaitingDuration);

    private Task ArchiveAsync() => OnArchive.InvokeAsync(Action);

    private Task SnoozeAsync(ActionSnoozeChoice choice) =>
        OnSnooze.InvokeAsync(new ActionSnoozeRequest(Action, choice));

    private Task MuteAsync() => OnMute.InvokeAsync(Action);

    private static string FormatDuration(TimeSpan duration) => duration.TotalDays switch
    {
        >= 2 => $"{(int)duration.TotalDays} days",
        >= 1 => "1 day",
        _ when duration.TotalHours >= 2 => $"{(int)duration.TotalHours} hours",
        _ when duration.TotalHours >= 1 => "1 hour",
        _ when duration.TotalMinutes >= 2 => $"{(int)duration.TotalMinutes} minutes",
        _ => "just now"
    };
}