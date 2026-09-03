using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using MudBlazor;
using Needly.Application.Actions;
using Needly.Application.GitHub;
using Needly.Domain;
using Needly.Web.Components.Inbox;
using Needly.Web.Components.Views;

namespace Needly.Web.Components.Pages;

public partial class Inbox
{
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private IReadOnlyList<VisibleAction> _actions = [];
    private ActionFilter _filter = new();
    private string _activeViewName = "All actions";
    private ElementReference _inboxRoot;
    private DotNetObjectReference<Inbox>? _dotNetReference;
    private InboxKeyboardInterop? _keyboardInterop;
    private Guid? _needlyUserId;
    private Guid? _selectedActionId;
    private bool _loading = true;
    private bool _refreshing;
    private bool _loadFailed;
    private bool _disposed;
    private bool _initialized;
    private string? _appliedViewKey;

    [Inject] private IInboxVisibilityService InboxService { get; set; } = null!;
    [Inject] private ISavedViewService SavedViewService { get; set; } = null!;
    [Inject] private SavedViewNavigationState ViewState { get; set; } = null!;
    [Inject] private IActionLifecycleService LifecycleService { get; set; } = null!;
    [Inject] private IActionChangeBroadcaster ChangeBroadcaster { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IJSRuntime JavaScript { get; set; } = null!;
    [Inject] private TimeProvider TimeProvider { get; set; } = null!;
    [Inject] private ILogger<Inbox> Logger { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    [Parameter]
    [SupplyParameterFromQuery(Name = "view")]
    public string? ViewKey { get; set; }

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationState { get; set; }

    private string Summary => _loading || _loadFailed
        ? "Inbox"
        : _actions.Count == 0
            ? HasActiveFilter ? "No matching actions" : "Nothing needs you"
            : $"{_actions.Count} things need you";

    private IReadOnlyList<ActionGroupView> Groups => _actions
        .GroupBy(action => new { Order = GroupOrder(action.Type), Title = GroupTitle(action.Type) })
        .OrderBy(group => group.Key.Order)
        .Select(group => new ActionGroupView(group.Key.Title, group.ToArray()))
        .ToArray();

    protected override async Task OnInitializedAsync()
    {
        ChangeBroadcaster.Changed += OnActionsChanged;
        var authenticationState = AuthenticationState is null ? null : await AuthenticationState;
        var userIdValue = authenticationState?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var needlyUserId))
        {
            _loading = false;
            _loadFailed = true;
            Logger.LogWarning("Authenticated inbox request had no valid Needly user identifier");
            return;
        }

        _needlyUserId = needlyUserId;
        await ViewState.InitializeAsync(needlyUserId, _disposeCancellation.Token);
        ActivateView();
        _initialized = true;
        await LoadAsync(showSkeleton: true);
    }

    protected override async Task OnParametersSetAsync()
    {
        if (_initialized && !string.Equals(_appliedViewKey, ViewKey, StringComparison.OrdinalIgnoreCase))
        {
            ActivateView();
            await LoadAsync(showSkeleton: false);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _dotNetReference = DotNetObjectReference.Create(this);
        _keyboardInterop = new InboxKeyboardInterop(JavaScript);
        await _keyboardInterop.InitializeAsync(_inboxRoot, _dotNetReference);
    }

    [JSInvokable]
    public async Task OnInboxShortcutAsync(string command, string actionId)
    {
        if (!Guid.TryParse(actionId, out var parsedActionId))
        {
            return;
        }

        await InvokeAsync(async () =>
        {
            var action = _actions.SingleOrDefault(item => item.ActionId == parsedActionId);
            if (action is null)
            {
                return;
            }

            _selectedActionId = action.ActionId;
            StateHasChanged();
            switch (command)
            {
                case "e":
                    await ArchiveAsync(action);
                    break;
                case "s":
                    await SnoozeAsync(new ActionSnoozeRequest(action, ActionSnoozeChoice.LaterToday));
                    break;
                case "m":
                    await MuteAsync(action);
                    break;
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        ChangeBroadcaster.Changed -= OnActionsChanged;
        await _disposeCancellation.CancelAsync();
        if (_keyboardInterop is not null)
        {
            await _keyboardInterop.DisposeAsync();
        }

        _dotNetReference?.Dispose();
        await _loadLock.WaitAsync();
        _loadLock.Release();
        _loadLock.Dispose();
        _disposeCancellation.Dispose();
    }

    private async void OnActionsChanged()
    {
        if (_disposed || _needlyUserId is null)
        {
            return;
        }

        try
        {
            await InvokeAsync(async () =>
            {
                await LoadAsync(showSkeleton: false);
                StateHasChanged();
            });
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
        }
    }

    private Task RetryAsync() => LoadAsync(showSkeleton: true);

    private async Task LoadAsync(bool showSkeleton)
    {
        if (_needlyUserId is not { } needlyUserId)
        {
            return;
        }

        await _loadLock.WaitAsync(_disposeCancellation.Token);
        try
        {
            _loadFailed = false;
            _loading = showSkeleton;
            _refreshing = !showSkeleton;
            _actions = await InboxService.GetVisibleAsync(
                needlyUserId,
                _filter,
                _disposeCancellation.Token);
            if (_selectedActionId is null || _actions.All(action => action.ActionId != _selectedActionId))
            {
                _selectedActionId = _actions.FirstOrDefault()?.ActionId;
            }
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
        }
        catch (DbException exception)
        {
            HandleLoadFailure(exception);
        }
        catch (InvalidOperationException exception)
        {
            HandleLoadFailure(exception);
        }
        finally
        {
            _loading = false;
            _refreshing = false;
            _loadLock.Release();
        }
    }

    private async Task ArchiveAsync(VisibleAction action)
    {
        if (_needlyUserId is not { } needlyUserId)
        {
            return;
        }

        var change = await LifecycleService.ArchiveAsync(
            needlyUserId, action.ActionId, _disposeCancellation.Token);
        ShowUndo(change, "Action archived");
    }

    private async Task SnoozeAsync(ActionSnoozeRequest request)
    {
        if (_needlyUserId is not { } needlyUserId)
        {
            return;
        }

        var deadline = request.Choice == ActionSnoozeChoice.Custom
            ? await GetCustomSnoozeDeadlineAsync()
            : GetPresetDeadline(request.Choice);
        if (deadline is null)
        {
            return;
        }

        var change = await LifecycleService.SnoozeAsync(
            needlyUserId, request.Action.ActionId, deadline.Value, _disposeCancellation.Token);
        ShowUndo(change, $"Snoozed until {deadline.Value:ddd, MMM d 'at' HH:mm} UTC");
    }

    private async Task MuteAsync(VisibleAction action)
    {
        if (_needlyUserId is not { } needlyUserId)
        {
            return;
        }

        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Mute this subject?",
            $"Future actions for {action.RepositoryOwner}/{action.RepositoryName} #{action.SubjectNumber} assigned to you will be suppressed.",
            yesText: "Mute subject",
            cancelText: "Cancel");
        if (confirmed != true)
        {
            return;
        }

        var change = await LifecycleService.MuteAsync(
            needlyUserId, action.ActionId, _disposeCancellation.Token);
        ShowUndo(change, "Subject muted. Future actions are suppressed.");
    }

    private async Task<DateTimeOffset?> GetCustomSnoozeDeadlineAsync()
    {
        var options = new DialogOptions
        {
            MaxWidth = MaxWidth.Small,
            FullWidth = true,
            CloseButton = true
        };
        var dialog = await DialogService.ShowAsync<CustomSnoozeDialog>("Custom snooze", options);
        var result = await dialog.Result;
        return result is { Canceled: false, Data: DateTimeOffset deadline } ? deadline : null;
    }

    private DateTimeOffset GetPresetDeadline(ActionSnoozeChoice choice)
    {
        var now = TimeProvider.GetUtcNow();
        return choice switch
        {
            ActionSnoozeChoice.LaterToday => LaterToday(now),
            ActionSnoozeChoice.Tomorrow => new DateTimeOffset(
                now.UtcDateTime.Date.AddDays(1).AddHours(9), TimeSpan.Zero),
            ActionSnoozeChoice.NextWeek => new DateTimeOffset(
                now.UtcDateTime.Date.AddDays(7).AddHours(9), TimeSpan.Zero),
            _ => throw new ArgumentOutOfRangeException(nameof(choice), choice, "A preset snooze choice is required.")
        };
    }

    private void ShowUndo(ActionLifecycleChange? change, string message)
    {
        if (change is null)
        {
            Snackbar.Add("That action is no longer available.", Severity.Warning);
            return;
        }

        Snackbar.Add(message, Severity.Normal, options =>
        {
            options.Action = "Undo";
            options.ActionColor = Color.Primary;
            options.OnClick = _ => UndoAsync(change.UndoId);
        });
    }

    private async Task UndoAsync(Guid undoId)
    {
        if (_needlyUserId is not { } needlyUserId)
        {
            return;
        }

        var restored = await LifecycleService.UndoAsync(
            needlyUserId, undoId, _disposeCancellation.Token);
        Snackbar.Add(
            restored ? "Action restored" : "This change can no longer be undone.",
            restored ? Severity.Success : Severity.Warning);
    }

    private void HandleLoadFailure(Exception exception)
    {
        _loadFailed = true;
        Logger.LogError(exception, "Failed to load the action inbox for Needly user {NeedlyUserId}", _needlyUserId);
    }

    private static DateTimeOffset LaterToday(DateTimeOffset now)
    {
        var endOfWorkday = new DateTimeOffset(now.UtcDateTime.Date.AddHours(17), TimeSpan.Zero);
        return endOfWorkday > now ? endOfWorkday : now.AddHours(2);
    }

    private static string GroupTitle(ActionType type) => type switch
    {
        ActionType.Review => "Review",
        ActionType.Fix => "Fix",
        ActionType.Resolve => "Resolve",
        ActionType.Merge => "Merge",
        ActionType.Respond => "Respond",
        ActionType.FollowUp => "Follow up",
        ActionType.FYI or ActionType.Monitor => "FYI",
        _ => "Other"
    };

    private static int GroupOrder(ActionType type) => type switch
    {
        ActionType.Review => 0,
        ActionType.Fix => 1,
        ActionType.Resolve => 2,
        ActionType.Merge => 3,
        ActionType.Respond => 4,
        ActionType.FollowUp => 5,
        ActionType.FYI or ActionType.Monitor => 6,
        _ => 7
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

    private async Task OnFilterChangedAsync(ActionFilter filter)
    {
        _filter = filter;
        _activeViewName = "Custom filter";
        if (!string.IsNullOrWhiteSpace(ViewKey))
        {
            _appliedViewKey = null;
            Navigation.NavigateTo("/inbox", replace: true);
        }

        await LoadAsync(showSkeleton: false);
    }

    private async Task SaveViewAsync()
    {
        if (_needlyUserId is not { } userId)
        {
            return;
        }

        var parameters = new DialogParameters
        {
            [nameof(SavedViewEditorDialog.InitialFilter)] = _filter
        };
        var dialog = await DialogService.ShowAsync<SavedViewEditorDialog>(
            "Save current filter",
            parameters,
            new DialogOptions { FullWidth = true, MaxWidth = MaxWidth.Medium, CloseButton = true });
        var result = await dialog.Result;
        if (result is not { Canceled: false, Data: SavedViewEditResult edit })
        {
            return;
        }

        try
        {
            var view = await SavedViewService.CreateAsync(
                userId,
                edit.Name,
                edit.Filter,
                _disposeCancellation.Token);
            await ViewState.ReloadAsync(_disposeCancellation.Token);
            Navigation.NavigateTo($"/?view={Uri.EscapeDataString(view.Key)}");
            Snackbar.Add("View saved", Severity.Success);
        }
        catch (InvalidOperationException exception)
        {
            Logger.LogWarning(exception, "Could not save inbox view for {NeedlyUserId}", userId);
            Snackbar.Add("A view with that name already exists.", Severity.Warning);
        }
    }

    private void ActivateView()
    {
        var view = string.IsNullOrWhiteSpace(ViewKey)
            ? null
            : ViewState.Views.FirstOrDefault(item =>
                string.Equals(item.Key, ViewKey, StringComparison.OrdinalIgnoreCase));
        _filter = view?.Filter ?? new ActionFilter();
        _activeViewName = view?.Name ?? "All actions";
        _appliedViewKey = ViewKey;
    }

    private sealed record ActionGroupView(string Title, IReadOnlyList<VisibleAction> Actions);
}