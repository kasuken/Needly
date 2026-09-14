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
    private const string SortOrderStorageKey = "needly.inbox.sortOrder";

    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly HashSet<Guid> _selectedIds = [];
    private IReadOnlyList<VisibleAction> _actions = [];
    private ActionFilter _filter = new();
    private string _activeViewName = "All actions";
    private ElementReference _inboxRoot;
    private DotNetObjectReference<Inbox>? _dotNetReference;
    private InboxKeyboardInterop? _keyboardInterop;
    private Guid? _needlyUserId;
    private Guid? _selectedActionId;
    private InboxSortOrder _sortOrder = InboxSortOrder.Attention;
    private IReadOnlyList<Guid> _lastUndoIds = [];
    private bool _loading = true;
    private bool _refreshing;
    private bool _loadFailed;
    private bool _disposed;
    private bool _initialized;
    private bool _selectionEnabled;
    private string? _appliedViewKey;

    [Inject] private IInboxVisibilityService InboxService { get; set; } = null!;
    [Inject] private IAttentionScoreCalculator ScoreCalculator { get; set; } = null!;
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

    private IReadOnlyList<ActionGroupView> Groups
    {
        get
        {
            var pinned = _actions.Where(action => action.IsPinned).ToArray();
            var unpinned = _actions.Where(action => !action.IsPinned);

            var groups = new List<ActionGroupView>();
            if (pinned.Length > 0)
            {
                groups.Add(new ActionGroupView("Pinned", Sorted(pinned)));
            }

            groups.AddRange(unpinned
                .GroupBy(action => new { Order = GroupOrder(action.Type), Title = GroupTitle(action.Type) })
                .OrderBy(group => group.Key.Order)
                .Select(group => new ActionGroupView(group.Key.Title, Sorted(group))));

            return groups;
        }
    }

    private IReadOnlyList<VisibleAction> Sorted(IEnumerable<VisibleAction> actions) => _sortOrder switch
    {
        InboxSortOrder.Newest => actions.OrderByDescending(action => action.WaitingSince).ToArray(),
        InboxSortOrder.Oldest => actions.OrderBy(action => action.WaitingSince).ToArray(),
        InboxSortOrder.Repository => actions
            .OrderBy(action => action.RepositoryOwner, StringComparer.OrdinalIgnoreCase)
            .ThenBy(action => action.RepositoryName, StringComparer.OrdinalIgnoreCase)
            .ToArray(),
        // Attention (default): highest explainable score first, oldest-waiting as the tie-break.
        _ => actions
            .OrderByDescending(action => ScoreCalculator.Calculate(action).Score)
            .ThenBy(action => action.WaitingSince)
            .ToArray()
    };

    private int SelectedCount => _selectedIds.Count;

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

        try
        {
            var stored = await JavaScript.InvokeAsync<string?>("localStorage.getItem", SortOrderStorageKey);
            if (stored is not null && Enum.TryParse<InboxSortOrder>(stored, out var savedOrder))
            {
                _sortOrder = savedOrder;
                StateHasChanged();
            }
        }
        catch (JSException)
        {
            // Local storage is unavailable in this browsing context; the default sort order still works.
        }
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
                case "p":
                    await TogglePinAsync(action);
                    break;
                case "x":
                    ToggleSelected(action.ActionId);
                    StateHasChanged();
                    break;
            }
        });
    }

    [JSInvokable]
    public async Task OnInboxNavigateAsync(string target)
    {
        var path = target switch
        {
            "inbox" => "/inbox",
            "views" => "/views",
            "rules" => "/rules",
            _ => null
        };
        if (path is null)
        {
            return;
        }

        await InvokeAsync(() => Navigation.NavigateTo(path));
    }

    [JSInvokable]
    public Task OnInboxUndoRequestedAsync() => InvokeAsync(UndoLastAsync);

    [JSInvokable]
    public Task OnInboxHelpRequestedAsync() => InvokeAsync(ShowKeyboardShortcutsAsync);

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

        ShowUndo([change.UndoId], message);
    }

    private void ShowUndo(IReadOnlyList<Guid> undoIds, string message)
    {
        if (undoIds.Count == 0)
        {
            Snackbar.Add("That action is no longer available.", Severity.Warning);
            return;
        }

        _lastUndoIds = undoIds;
        Snackbar.Add(message, Severity.Normal, options =>
        {
            options.Action = "Undo";
            options.ActionColor = Color.Primary;
            options.OnClick = _ => UndoAsync(undoIds);
        });
    }

    private Task UndoLastAsync() => _lastUndoIds.Count > 0
        ? UndoAsync(_lastUndoIds)
        : Task.CompletedTask;

    private async Task UndoAsync(IReadOnlyList<Guid> undoIds)
    {
        if (_needlyUserId is not { } needlyUserId)
        {
            return;
        }

        var restoredCount = 0;
        foreach (var undoId in undoIds)
        {
            if (await LifecycleService.UndoAsync(needlyUserId, undoId, _disposeCancellation.Token))
            {
                restoredCount++;
            }
        }

        _lastUndoIds = [];
        Snackbar.Add(
            restoredCount switch
            {
                0 => "This change can no longer be undone.",
                1 => "Action restored",
                _ => $"{restoredCount} actions restored"
            },
            restoredCount > 0 ? Severity.Success : Severity.Warning);
    }

    private async Task TogglePinAsync(VisibleAction action)
    {
        if (_needlyUserId is not { } needlyUserId)
        {
            return;
        }

        await LifecycleService.SetPinnedAsync(
            needlyUserId, action.ActionId, !action.IsPinned, _disposeCancellation.Token);
    }

    private void ToggleSelected(Guid actionId)
    {
        if (!_selectedIds.Remove(actionId))
        {
            _selectedIds.Add(actionId);
        }
    }

    private void ToggleSelectionMode()
    {
        _selectionEnabled = !_selectionEnabled;
        if (!_selectionEnabled)
        {
            _selectedIds.Clear();
        }
    }

    private void ToggleSelectAll()
    {
        if (_selectedIds.Count == _actions.Count)
        {
            _selectedIds.Clear();
        }
        else
        {
            _selectedIds.Clear();
            foreach (var action in _actions)
            {
                _selectedIds.Add(action.ActionId);
            }
        }
    }

    private void ClearSelection()
    {
        _selectedIds.Clear();
        _selectionEnabled = false;
    }

    private Task OnCheckedChanged((VisibleAction Action, bool Checked) change)
    {
        if (change.Checked)
        {
            _selectedIds.Add(change.Action.ActionId);
        }
        else
        {
            _selectedIds.Remove(change.Action.ActionId);
        }

        return Task.CompletedTask;
    }

    private async Task BulkArchiveAsync()
    {
        if (_needlyUserId is not { } needlyUserId)
        {
            return;
        }

        var targets = SelectedActions();
        var undoIds = new List<Guid>();
        foreach (var action in targets)
        {
            var change = await LifecycleService.ArchiveAsync(needlyUserId, action.ActionId, _disposeCancellation.Token);
            if (change is not null)
            {
                undoIds.Add(change.UndoId);
            }
        }

        ClearSelection();
        ShowUndo(undoIds, $"{undoIds.Count} actions archived");
    }

    private async Task BulkSnoozeAsync(ActionSnoozeChoice choice)
    {
        if (_needlyUserId is not { } needlyUserId)
        {
            return;
        }

        var deadline = choice == ActionSnoozeChoice.Custom
            ? await GetCustomSnoozeDeadlineAsync()
            : GetPresetDeadline(choice);
        if (deadline is null)
        {
            return;
        }

        var targets = SelectedActions();
        var undoIds = new List<Guid>();
        foreach (var action in targets)
        {
            var change = await LifecycleService.SnoozeAsync(
                needlyUserId, action.ActionId, deadline.Value, _disposeCancellation.Token);
            if (change is not null)
            {
                undoIds.Add(change.UndoId);
            }
        }

        ClearSelection();
        ShowUndo(undoIds, $"{undoIds.Count} actions snoozed until {deadline.Value:ddd, MMM d 'at' HH:mm} UTC");
    }

    private async Task BulkMuteAsync()
    {
        if (_needlyUserId is not { } needlyUserId)
        {
            return;
        }

        var targets = SelectedActions();
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Mute these subjects?",
            $"Future actions for {targets.Count} selected subjects assigned to you will be suppressed.",
            yesText: "Mute subjects",
            cancelText: "Cancel");
        if (confirmed != true)
        {
            return;
        }

        var undoIds = new List<Guid>();
        foreach (var action in targets)
        {
            var change = await LifecycleService.MuteAsync(needlyUserId, action.ActionId, _disposeCancellation.Token);
            if (change is not null)
            {
                undoIds.Add(change.UndoId);
            }
        }

        ClearSelection();
        ShowUndo(undoIds, $"{undoIds.Count} subjects muted. Future actions are suppressed.");
    }

    private async Task BulkPinAsync(bool isPinned)
    {
        if (_needlyUserId is not { } needlyUserId)
        {
            return;
        }

        foreach (var action in SelectedActions())
        {
            await LifecycleService.SetPinnedAsync(needlyUserId, action.ActionId, isPinned, _disposeCancellation.Token);
        }

        ClearSelection();
    }

    private IReadOnlyList<VisibleAction> SelectedActions() =>
        _actions.Where(action => _selectedIds.Contains(action.ActionId)).ToArray();

    private async Task OnSortOrderChangedAsync(InboxSortOrder order)
    {
        _sortOrder = order;
        try
        {
            await JavaScript.InvokeVoidAsync("localStorage.setItem", SortOrderStorageKey, order.ToString());
        }
        catch (JSException)
        {
            // Local storage is unavailable in this browsing context; the selection still applies for this session.
        }
    }

    private Task ShowKeyboardShortcutsAsync() => DialogService.ShowAsync<KeyboardShortcutsDialog>(
        "Keyboard shortcuts",
        new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true, CloseButton = true });

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
            Navigation.NavigateTo($"/inbox?view={Uri.EscapeDataString(view.Key)}");
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

    private enum InboxSortOrder
    {
        Attention,
        Newest,
        Oldest,
        Repository
    }
}