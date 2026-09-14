using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using Needly.Application.Actions;
using Needly.Domain;

namespace Needly.Web.Components.Pages;

public partial class Done : IDisposable
{
    private IReadOnlyList<DoneAction> _actions = [];
    private ActionFilter _filter = new();
    private Guid? _needlyUserId;
    private int _weeklyClearedCount;
    private bool _loading = true;
    private bool _loadFailed;
    private bool _disposed;

    [Inject] private IDoneActionsService DoneService { get; set; } = null!;
    [Inject] private IActionChangeBroadcaster ChangeBroadcaster { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private ILogger<Done> Logger { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationState { get; set; }

    private string WeeklySummary => _weeklyClearedCount switch
    {
        0 => "No actions cleared this week yet.",
        1 => "1 action cleared this week.",
        _ => $"{_weeklyClearedCount} actions cleared this week."
    };

    private bool HasActiveFilter =>
        _filter.Types.Length > 0 ||
        _filter.States.Length > 0 ||
        _filter.Repositories.Length > 0 ||
        _filter.Organizations.Length > 0 ||
        _filter.Authors.Length > 0 ||
        _filter.AssigneeScope != ActionAssigneeScope.Any ||
        _filter.WaitingAtLeast is not null ||
        _filter.BotInvolvement != BotInvolvementFilter.Any;

    protected override async Task OnInitializedAsync()
    {
        ChangeBroadcaster.Changed += OnActionsChanged;
        var state = AuthenticationState is null ? null : await AuthenticationState;
        var userIdValue = state?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var needlyUserId))
        {
            _loading = false;
            _loadFailed = true;
            Logger.LogWarning("Authenticated Done view request had no valid Needly user identifier");
            return;
        }

        _needlyUserId = needlyUserId;
        await LoadAsync();
    }

    public void Dispose()
    {
        _disposed = true;
        ChangeBroadcaster.Changed -= OnActionsChanged;
    }

    private async void OnActionsChanged()
    {
        if (_disposed || _needlyUserId is null)
        {
            return;
        }

        await InvokeAsync(async () =>
        {
            await LoadAsync();
            StateHasChanged();
        });
    }

    private Task RetryAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        if (_needlyUserId is not { } needlyUserId)
        {
            return;
        }

        _loading = true;
        _loadFailed = false;
        try
        {
            _actions = await DoneService.GetAsync(needlyUserId, _filter, CancellationToken.None);
            _weeklyClearedCount = await DoneService.GetWeeklyClearedCountAsync(needlyUserId, CancellationToken.None);
        }
        catch (DbException exception)
        {
            HandleLoadFailure(exception, needlyUserId);
        }
        catch (InvalidOperationException exception)
        {
            HandleLoadFailure(exception, needlyUserId);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task OnFilterChangedAsync(ActionFilter filter)
    {
        _filter = filter;
        await LoadAsync();
    }

    private async Task RestoreAsync(DoneAction action)
    {
        if (_needlyUserId is not { } needlyUserId)
        {
            return;
        }

        var restored = await DoneService.RestoreAsync(needlyUserId, action.ActionId, CancellationToken.None);
        Snackbar.Add(
            restored ? "Restored to the inbox" : "That action is no longer available.",
            restored ? Severity.Success : Severity.Warning);
        if (restored)
        {
            await LoadAsync();
        }
    }

    private void HandleLoadFailure(Exception exception, Guid needlyUserId)
    {
        _loadFailed = true;
        Logger.LogError(exception, "Failed to load the Done view for Needly user {NeedlyUserId}", needlyUserId);
    }

    private static string FormatClosedAt(DoneAction action) => action.TimeSinceClosed.TotalDays switch
    {
        >= 2 => $"{(int)action.TimeSinceClosed.TotalDays} days ago",
        >= 1 => "1 day ago",
        _ when action.TimeSinceClosed.TotalHours >= 2 => $"{(int)action.TimeSinceClosed.TotalHours} hours ago",
        _ when action.TimeSinceClosed.TotalHours >= 1 => "1 hour ago",
        _ when action.TimeSinceClosed.TotalMinutes >= 2 => $"{(int)action.TimeSinceClosed.TotalMinutes} minutes ago",
        _ => "just now"
    };
}
