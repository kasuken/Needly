using Microsoft.EntityFrameworkCore;
using Needly.Application.Actions;
using Needly.Application.GitHub;
using Needly.Domain;

namespace Needly.Infrastructure.Actions;

/// <summary>Persists and queries per-user Saved Views.</summary>
public sealed class SavedViewService(
    IDbContextFactory<NeedlyDbContext> contextFactory,
    IInboxVisibilityService inboxVisibilityService,
    TimeProvider timeProvider,
    IActionChangeBroadcaster broadcaster) : ISavedViewService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SavedViewItem>> GetAsync(
        Guid needlyUserId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var saved = await dbContext.SavedViews
            .AsNoTracking()
            .Where(view => view.NeedlyUserId == needlyUserId)
            .OrderBy(view => view.SortOrder)
            .ThenBy(view => view.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var visible = await inboxVisibilityService
            .GetVisibleAsync(needlyUserId, cancellationToken).ConfigureAwait(false);

        var builtIn = BuiltInSavedViews.All.Select(view => view with
        {
            OpenCount = CountMatches(visible, view.Filter)
        });
        var custom = saved.Select(view =>
        {
            var filter = ActionFilterJsonSerializer.Deserialize(view.FilterJson);
            return new SavedViewItem(
                view.Id.ToString("N"),
                view.Id,
                view.Name,
                filter,
                view.SortOrder,
                CountMatches(visible, filter),
                false);
        });
        return [.. builtIn, .. custom];
    }

    /// <inheritdoc />
    public async Task<SavedViewItem> CreateAsync(
        Guid needlyUserId,
        string name,
        ActionFilter filter,
        CancellationToken cancellationToken)
    {
        var filterJson = ActionFilterJsonSerializer.Serialize(filter);
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await EnsureUniqueNameAsync(dbContext, needlyUserId, null, name, cancellationToken)
            .ConfigureAwait(false);
        var sortOrder = await dbContext.SavedViews
            .Where(view => view.NeedlyUserId == needlyUserId)
            .Select(view => (int?)view.SortOrder)
            .MaxAsync(cancellationToken).ConfigureAwait(false) + 1 ?? 0;
        var view = SavedView.Create(Guid.NewGuid(), needlyUserId, name, filterJson, sortOrder, timeProvider.GetUtcNow());
        dbContext.SavedViews.Add(view);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        broadcaster.Publish();
        return new SavedViewItem(view.Id.ToString("N"), view.Id, view.Name, filter, view.SortOrder, 0, false);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(
        Guid needlyUserId,
        Guid viewId,
        string name,
        ActionFilter filter,
        CancellationToken cancellationToken)
    {
        var filterJson = ActionFilterJsonSerializer.Serialize(filter);
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var view = await dbContext.SavedViews.SingleOrDefaultAsync(
            item => item.Id == viewId && item.NeedlyUserId == needlyUserId,
            cancellationToken).ConfigureAwait(false);
        if (view is null)
        {
            return false;
        }

        await EnsureUniqueNameAsync(dbContext, needlyUserId, viewId, name, cancellationToken)
            .ConfigureAwait(false);
        view.Update(name, filterJson, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        broadcaster.Publish();
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        Guid needlyUserId,
        Guid viewId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var view = await dbContext.SavedViews.SingleOrDefaultAsync(
            item => item.Id == viewId && item.NeedlyUserId == needlyUserId,
            cancellationToken).ConfigureAwait(false);
        if (view is null)
        {
            return false;
        }

        dbContext.SavedViews.Remove(view);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await NormalizeOrderAsync(dbContext, needlyUserId, cancellationToken).ConfigureAwait(false);
        broadcaster.Publish();
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> MoveAsync(
        Guid needlyUserId,
        Guid viewId,
        int direction,
        CancellationToken cancellationToken)
    {
        if (direction is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var views = await dbContext.SavedViews
            .Where(view => view.NeedlyUserId == needlyUserId)
            .OrderBy(view => view.SortOrder)
            .ThenBy(view => view.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var currentIndex = views.FindIndex(view => view.Id == viewId);
        var targetIndex = currentIndex + direction;
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= views.Count)
        {
            return false;
        }

        (views[currentIndex], views[targetIndex]) = (views[targetIndex], views[currentIndex]);
        var now = timeProvider.GetUtcNow();
        for (var index = 0; index < views.Count; index++)
        {
            views[index].Reorder(index, now);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        broadcaster.Publish();
        return true;
    }

    private static int CountMatches(IReadOnlyList<VisibleAction> actions, ActionFilter filter) =>
        actions.Count(action => ActionFilterMatcher.IsMatch(filter, VisibleActionFilterCandidate.Create(action)));

    private static async Task EnsureUniqueNameAsync(
        NeedlyDbContext dbContext,
        Guid needlyUserId,
        Guid? excludedId,
        string name,
        CancellationToken cancellationToken)
    {
        var normalizedName = (name ?? string.Empty).Trim().ToUpperInvariant();
        if (await dbContext.SavedViews.AnyAsync(
            view => view.NeedlyUserId == needlyUserId &&
                view.Id != excludedId &&
                view.NormalizedName == normalizedName,
            cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("A saved view with this name already exists.");
        }
    }

    private async Task NormalizeOrderAsync(
        NeedlyDbContext dbContext,
        Guid needlyUserId,
        CancellationToken cancellationToken)
    {
        var views = await dbContext.SavedViews
            .Where(view => view.NeedlyUserId == needlyUserId)
            .OrderBy(view => view.SortOrder)
            .ThenBy(view => view.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        for (var index = 0; index < views.Count; index++)
        {
            views[index].Reorder(index, now);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}