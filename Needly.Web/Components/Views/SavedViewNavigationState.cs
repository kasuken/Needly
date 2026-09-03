using System.Data.Common;
using Needly.Application.Actions;

namespace Needly.Web.Components.Views;

/// <summary>Maintains per-circuit Saved View navigation state for one authenticated user.</summary>
public sealed class SavedViewNavigationState(
    ISavedViewService savedViewService,
    IActionChangeBroadcaster broadcaster) : IDisposable
{
    private Guid? _needlyUserId;
    private bool _disposed;

    /// <summary>Occurs when the circuit's view list or counts change.</summary>
    public event Action? Changed;

    /// <summary>Gets the current built-in and user-defined views.</summary>
    public IReadOnlyList<SavedViewItem> Views { get; private set; } = [];

    /// <summary>Gets whether the state is loading.</summary>
    public bool IsLoading { get; private set; }

    /// <summary>Gets whether the latest load failed.</summary>
    public bool HasError { get; private set; }

    /// <summary>Loads the state for the authenticated circuit user.</summary>
    public async Task InitializeAsync(Guid needlyUserId, CancellationToken cancellationToken = default)
    {
        if (_needlyUserId is null)
        {
            broadcaster.Changed += OnActionsChanged;
        }

        _needlyUserId = needlyUserId;
        await ReloadAsync(cancellationToken);
    }

    /// <summary>Reloads views and authorized counts for the current circuit user.</summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (_needlyUserId is not { } needlyUserId || _disposed)
        {
            return;
        }

        IsLoading = true;
        HasError = false;
        Changed?.Invoke();
        try
        {
            Views = await savedViewService.GetAsync(needlyUserId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DbException)
        {
            HasError = true;
        }
        catch (InvalidDataException)
        {
            HasError = true;
        }
        catch (InvalidOperationException)
        {
            HasError = true;
        }
        finally
        {
            IsLoading = false;
            Changed?.Invoke();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        broadcaster.Changed -= OnActionsChanged;
    }

    private async void OnActionsChanged()
    {
        try
        {
            await ReloadAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }
}