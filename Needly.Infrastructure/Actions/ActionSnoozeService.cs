using Microsoft.EntityFrameworkCore;
using Needly.Application.Actions;
using Needly.Domain;

namespace Needly.Infrastructure.Actions;

/// <summary>Reopens persisted actions when their snooze deadline elapses.</summary>
public sealed class ActionSnoozeService(
    IDbContextFactory<NeedlyDbContext> contextFactory,
    TimeProvider timeProvider,
    IActionChangeBroadcaster broadcaster) : IActionSnoozeService
{
    /// <inheritdoc />
    public async Task<int> ResurfaceDueAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        var snoozedActions = await dbContext.Actions
            .Where(action => action.State == ActionState.Snoozed)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var dueActions = snoozedActions
            .Where(action => action.SnoozedUntil <= now)
            .ToList();
        foreach (var action in dueActions)
        {
            action.ChangeState(ActionState.Open, now);
        }

        if (dueActions.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            broadcaster.Publish();
        }

        return dueActions.Count;
    }
}