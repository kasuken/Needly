using Microsoft.EntityFrameworkCore;
using Needly.Application.Actions;
using Needly.Domain;

namespace Needly.Infrastructure.Actions;

/// <summary>Applies authorized, durable user lifecycle changes to inbox actions.</summary>
public sealed class ActionLifecycleService(
    IDbContextFactory<NeedlyDbContext> contextFactory,
    TimeProvider timeProvider,
    IActionChangeBroadcaster broadcaster) : IActionLifecycleService
{
    /// <inheritdoc />
    public Task<ActionLifecycleChange?> ArchiveAsync(
        Guid needlyUserId,
        Guid actionId,
        CancellationToken cancellationToken) =>
        ChangeAsync(needlyUserId, actionId, ActionState.Archived, null, cancellationToken);

    /// <inheritdoc />
    public Task<ActionLifecycleChange?> SnoozeAsync(
        Guid needlyUserId,
        Guid actionId,
        DateTimeOffset snoozedUntil,
        CancellationToken cancellationToken)
    {
        var deadline = snoozedUntil.ToUniversalTime();
        if (deadline <= timeProvider.GetUtcNow())
        {
            throw new ArgumentOutOfRangeException(
                nameof(snoozedUntil),
                snoozedUntil,
                "The snooze deadline must be in the future.");
        }

        return ChangeAsync(needlyUserId, actionId, ActionState.Snoozed, deadline, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ActionLifecycleChange?> MuteAsync(
        Guid needlyUserId,
        Guid actionId,
        CancellationToken cancellationToken) =>
        ChangeAsync(needlyUserId, actionId, ActionState.Muted, null, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> UndoAsync(
        Guid needlyUserId,
        Guid undoId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var undo = await dbContext.ActionLifecycleUndos.SingleOrDefaultAsync(
            item => item.Id == undoId &&
                item.NeedlyUserId == needlyUserId &&
                item.UsedAt == null,
            cancellationToken).ConfigureAwait(false);
        if (undo is null)
        {
            return false;
        }

        var action = await GetAuthorizedActionAsync(
            dbContext,
            needlyUserId,
            undo.ActionId,
            requireOpen: false,
            includeSuppressed: true,
            cancellationToken).ConfigureAwait(false);
        if (action is null || action.State != undo.AppliedState)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        if (undo.SuppressionId is { } suppressionId)
        {
            var suppression = await dbContext.ActionSuppressions.SingleOrDefaultAsync(
                item => item.Id == suppressionId && item.NeedlyUserId == needlyUserId && item.IsActive,
                cancellationToken).ConfigureAwait(false);
            suppression?.Deactivate(now);
        }

        if (undo.PreviousState != undo.AppliedState)
        {
            action.RestoreLifecycle(undo.PreviousState, undo.PreviousSnoozedUntil, now);
        }

        undo.MarkUsed(now);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        broadcaster.Publish();
        return true;
    }

    private async Task<ActionLifecycleChange?> ChangeAsync(
        Guid needlyUserId,
        Guid actionId,
        ActionState state,
        DateTimeOffset? snoozedUntil,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var action = await GetAuthorizedActionAsync(
            dbContext,
            needlyUserId,
            actionId,
            requireOpen: true,
            includeSuppressed: false,
            cancellationToken).ConfigureAwait(false);
        if (action is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        ActionSuppression? suppression = null;
        if (state == ActionState.Muted)
        {
            suppression = ActionSuppression.Create(Guid.NewGuid(), needlyUserId, action, now);
            dbContext.ActionSuppressions.Add(suppression);
        }

        var changesSharedAction = state != ActionState.Muted ||
            action.AssigneeType == ActionAssigneeType.User;
        var undo = ActionLifecycleUndo.Create(
            Guid.NewGuid(),
            needlyUserId,
            action,
            changesSharedAction ? state : action.State,
            suppression?.Id,
            now);
        dbContext.ActionLifecycleUndos.Add(undo);

        if (changesSharedAction && state == ActionState.Snoozed)
        {
            action.Snooze(snoozedUntil!.Value, now);
        }
        else if (changesSharedAction)
        {
            action.ChangeState(state, now);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        broadcaster.Publish();
        return new ActionLifecycleChange(
            undo.Id,
            action.Id,
            state,
            state == ActionState.Snoozed ? action.SnoozedUntil : null);
    }

    private static async Task<NeedlyAction?> GetAuthorizedActionAsync(
        NeedlyDbContext dbContext,
        Guid needlyUserId,
        Guid actionId,
        bool requireOpen,
        bool includeSuppressed,
        CancellationToken cancellationToken)
    {
        var gitHubUserId = await dbContext.NeedlyUsers
            .Where(user => user.Id == needlyUserId)
            .Select(user => (Guid?)user.GitHubUserId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (gitHubUserId is null)
        {
            return null;
        }

        return await dbContext.Actions.SingleOrDefaultAsync(action =>
            action.Id == actionId &&
            (!requireOpen || action.State == ActionState.Open) &&
            dbContext.Installations.Any(installation =>
                installation.Id == action.InstallationId &&
                installation.State == InstallationState.Active) &&
            dbContext.InstallationMembers.Any(member =>
                member.InstallationId == action.InstallationId &&
                member.GitHubUserId == gitHubUserId.Value &&
                member.IsActive) &&
            (includeSuppressed || !dbContext.ActionSuppressions.Any(suppression =>
                suppression.NeedlyUserId == needlyUserId &&
                suppression.InstallationId == action.InstallationId &&
                suppression.RepositoryId == action.RepositoryId &&
                suppression.SubjectType == action.SubjectType &&
                suppression.SubjectNumber == action.SubjectNumber &&
                suppression.AssigneeType == action.AssigneeType &&
                suppression.AssigneeId == action.AssigneeId &&
                suppression.IsActive)) &&
            ((action.AssigneeType == ActionAssigneeType.User &&
              action.AssigneeId == gitHubUserId.Value) ||
             (action.AssigneeType == ActionAssigneeType.Team &&
              dbContext.TeamMembers.Any(member =>
                  member.TeamId == action.AssigneeId &&
                  member.GitHubUserId == gitHubUserId.Value &&
                  member.IsActive))),
            cancellationToken).ConfigureAwait(false);
    }
}