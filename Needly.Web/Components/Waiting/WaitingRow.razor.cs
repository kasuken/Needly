using Microsoft.AspNetCore.Components;
using MudBlazor;
using Needly.Application.GitHub;

namespace Needly.Web.Components.Waiting;

public partial class WaitingRow
{
    private string CssClass => $"waiting-item waiting-item--{Item.Reason.ToString().ToLowerInvariant()}";

    private string ReasonIcon => Item.Reason switch
    {
        WaitingOnOthersReason.NoReviewerAssigned => Icons.Material.Outlined.PersonSearch,
        WaitingOnOthersReason.AwaitingReview => Icons.Material.Outlined.RateReview,
        WaitingOnOthersReason.AwaitingReReview => Icons.Material.Outlined.Replay,
        _ => Icons.Material.Outlined.HourglassEmpty
    };

    private string ReasonLabel => Item.Reason switch
    {
        WaitingOnOthersReason.NoReviewerAssigned => "No reviewer has been assigned yet.",
        WaitingOnOthersReason.AwaitingReview => "Waiting for a review.",
        WaitingOnOthersReason.AwaitingReReview => "Feedback was addressed; waiting for a re-review.",
        _ => string.Empty
    };

    private string BlockingPartyLabel => Item.BlockingParty == "Unassigned"
        ? "Unassigned"
        : Item.BlockingParty;

    private string WaitingLabel => FormatDuration(Item.WaitingDuration);

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
