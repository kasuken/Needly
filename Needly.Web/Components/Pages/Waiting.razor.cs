using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Needly.Application.GitHub;

namespace Needly.Web.Components.Pages;

public partial class Waiting
{
    private IReadOnlyList<WaitingOnOthersItem> _items = [];
    private bool _loading = true;
    private bool _loadFailed;
    private Guid? _needlyUserId;

    [Inject] private IWaitingOnOthersService WaitingService { get; set; } = null!;
    [Inject] private ILogger<Waiting> Logger { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationState { get; set; }

    private string Summary => _loading || _loadFailed
        ? "Waiting on others"
        : _items.Count == 0
            ? "Nothing is waiting on anyone else"
            : $"{_items.Count} things waiting on others";

    private IReadOnlyList<WaitingGroupView> Groups => _items
        .GroupBy(item => new { Order = GroupOrder(item.Reason), Title = GroupTitle(item.Reason) })
        .OrderBy(group => group.Key.Order)
        .Select(group => new WaitingGroupView(group.Key.Title, group.ToArray()))
        .ToArray();

    protected override async Task OnInitializedAsync()
    {
        var state = AuthenticationState is null ? null : await AuthenticationState;
        if (!Guid.TryParse(
                state?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                out var userId))
        {
            _loading = false;
            _loadFailed = true;
            return;
        }

        _needlyUserId = userId;
        await LoadAsync();
    }

    private async Task RetryAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        if (_needlyUserId is not { } userId)
        {
            return;
        }

        _loading = true;
        _loadFailed = false;
        StateHasChanged();
        try
        {
            _items = await WaitingService.GetWaitingAsync(userId, CancellationToken.None);
        }
        catch (DbException exception)
        {
            Logger.LogError(exception, "Failed to load waiting-on-others items for {NeedlyUserId}", userId);
            _loadFailed = true;
        }
        catch (InvalidOperationException exception)
        {
            Logger.LogError(exception, "Failed to load waiting-on-others items for {NeedlyUserId}", userId);
            _loadFailed = true;
        }
        finally
        {
            _loading = false;
        }
    }

    private static int GroupOrder(WaitingOnOthersReason reason) => reason switch
    {
        WaitingOnOthersReason.AwaitingReReview => 0,
        WaitingOnOthersReason.AwaitingReview => 1,
        WaitingOnOthersReason.NoReviewerAssigned => 2,
        _ => 3
    };

    private static string GroupTitle(WaitingOnOthersReason reason) => reason switch
    {
        WaitingOnOthersReason.AwaitingReReview => "Addressed, awaiting re-review",
        WaitingOnOthersReason.AwaitingReview => "Awaiting review",
        WaitingOnOthersReason.NoReviewerAssigned => "No reviewer assigned",
        _ => "Waiting"
    };

    private sealed record WaitingGroupView(string Title, IReadOnlyList<WaitingOnOthersItem> Items);
}
