using Microsoft.EntityFrameworkCore;
using Needly.Application.Actions;
using Needly.Domain;

namespace Needly.Infrastructure.Actions;

/// <summary>
/// Returns the authorized, installation-scoped Done view: actions archived or completed for the
/// current user, and how long ago they closed.
/// </summary>
public sealed class DoneActionsService(
    IDbContextFactory<NeedlyDbContext> contextFactory,
    TimeProvider timeProvider,
    IActionChangeBroadcaster broadcaster) : IDoneActionsService
{
    /// <summary>
    /// How far back the Done view queries closed actions. Chosen to keep the view fast as historical
    /// data grows; older completions remain on GitHub itself, they simply stop surfacing here.
    /// </summary>
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(90);

    /// <summary>The rolling window used to compute the weekly cleared count.</summary>
    private static readonly TimeSpan WeeklyWindow = TimeSpan.FromDays(7);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DoneAction>> GetAsync(
        Guid needlyUserId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var gitHubUserId = await GetGitHubUserIdAsync(dbContext, needlyUserId, cancellationToken)
            .ConfigureAwait(false);
        if (gitHubUserId is null)
        {
            return [];
        }

        var now = timeProvider.GetUtcNow();
        var cutoff = now - RetentionWindow;

        // The retention cutoff is compared client-side, not in the EF query: this codebase's SQLite
        // provider does not reliably translate a DateTimeOffset range comparison alongside these other
        // predicates (the same reason ActionRiskEvaluator compares its waiting/inactivity thresholds
        // after materializing rather than inside the query). The State/installation/membership/assignee
        // predicates below all translate fine and keep the fetched set small before the client-side filter.
        var closedStates = new[] { ActionState.Archived, ActionState.Done };
        var closedRowsQuery = await dbContext.Actions
            .AsNoTracking()
            .Where(action => closedStates.Contains(action.State))
            .Where(action => dbContext.Installations.Any(installation =>
                installation.Id == action.InstallationId &&
                installation.State == InstallationState.Active))
            .Where(action => dbContext.InstallationMembers.Any(member =>
                member.InstallationId == action.InstallationId &&
                member.GitHubUserId == gitHubUserId.Value &&
                member.IsActive))
            .Where(action =>
                (action.AssigneeType == ActionAssigneeType.User &&
                 action.AssigneeId == gitHubUserId.Value) ||
                (action.AssigneeType == ActionAssigneeType.Team &&
                 dbContext.TeamMembers.Any(member =>
                     member.TeamId == action.AssigneeId &&
                     member.GitHubUserId == gitHubUserId.Value &&
                     member.IsActive)))
            .Join(
                dbContext.Repositories.AsNoTracking(),
                action => action.RepositoryId,
                repository => repository.Id,
                (action, repository) => new { Action = action, Repository = repository })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var closedRows = closedRowsQuery
            .Where(row => row.Action.UpdatedAt >= cutoff)
            .Select(row => new CandidateRow(row.Action, row.Repository, IsAutomationArchived: false))
            .ToArray();

        var archivedDispositionActionIds = (await dbContext.ActionDispositions
            .AsNoTracking()
            .Where(disposition =>
                disposition.NeedlyUserId == needlyUserId &&
                disposition.IsArchived)
            .Select(disposition => new { disposition.ActionId, disposition.UpdatedAt })
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false))
            .Where(disposition => disposition.UpdatedAt >= cutoff)
            .Select(disposition => disposition.ActionId)
            .ToArray();

        var dispositionRows = archivedDispositionActionIds.Length == 0
            ? []
            : (await dbContext.Actions
                .AsNoTracking()
                .Where(action => archivedDispositionActionIds.Contains(action.Id))
                .Where(action => action.State == ActionState.Open)
                .Where(action => dbContext.Installations.Any(installation =>
                    installation.Id == action.InstallationId &&
                    installation.State == InstallationState.Active))
                .Where(action => dbContext.InstallationMembers.Any(member =>
                    member.InstallationId == action.InstallationId &&
                    member.GitHubUserId == gitHubUserId.Value &&
                    member.IsActive))
                .Where(action =>
                    (action.AssigneeType == ActionAssigneeType.User &&
                     action.AssigneeId == gitHubUserId.Value) ||
                    (action.AssigneeType == ActionAssigneeType.Team &&
                     dbContext.TeamMembers.Any(member =>
                         member.TeamId == action.AssigneeId &&
                         member.GitHubUserId == gitHubUserId.Value &&
                         member.IsActive)))
                .Join(
                    dbContext.Repositories.AsNoTracking(),
                    action => action.RepositoryId,
                    repository => repository.Id,
                    (action, repository) => new { Action = action, Repository = repository })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
                .Select(row => new CandidateRow(row.Action, row.Repository, IsAutomationArchived: true))
                .ToArray();

        var rows = closedRows.Concat(dispositionRows).ToArray();
        if (rows.Length == 0)
        {
            return [];
        }

        var lookups = await LoadLookupsAsync(dbContext, needlyUserId, rows, cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(row => CreateDoneAction(row, lookups, now))
            .OrderByDescending(action => action.ClosedAt)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DoneAction>> GetAsync(
        Guid needlyUserId,
        ActionFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var actions = await GetAsync(needlyUserId, cancellationToken).ConfigureAwait(false);
        return actions
            .Where(action => ActionFilterMatcher.IsMatch(filter, DoneActionFilterCandidate.Create(action)))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<int> GetWeeklyClearedCountAsync(
        Guid needlyUserId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var gitHubUserId = await GetGitHubUserIdAsync(dbContext, needlyUserId, cancellationToken)
            .ConfigureAwait(false);
        if (gitHubUserId is null)
        {
            return 0;
        }

        var since = timeProvider.GetUtcNow() - WeeklyWindow;

        // As above, the UpdatedAt threshold is applied client-side after the translatable predicates
        // narrow the set down, rather than inside the EF query.
        var clearedStates = new[] { ActionState.Archived, ActionState.Muted, ActionState.Done };
        var stateClearedCount = (await dbContext.Actions
            .AsNoTracking()
            .Where(action =>
                clearedStates.Contains(action.State) &&
                dbContext.Installations.Any(installation =>
                    installation.Id == action.InstallationId &&
                    installation.State == InstallationState.Active) &&
                dbContext.InstallationMembers.Any(member =>
                    member.InstallationId == action.InstallationId &&
                    member.GitHubUserId == gitHubUserId.Value &&
                    member.IsActive) &&
                ((action.AssigneeType == ActionAssigneeType.User &&
                  action.AssigneeId == gitHubUserId.Value) ||
                 (action.AssigneeType == ActionAssigneeType.Team &&
                  dbContext.TeamMembers.Any(member =>
                      member.TeamId == action.AssigneeId &&
                      member.GitHubUserId == gitHubUserId.Value &&
                      member.IsActive))))
            .Select(action => action.UpdatedAt)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false))
            .Count(updatedAt => updatedAt >= since);

        // A disposition flag can, in principle, coincide with a state already counted above (for
        // example an old automation-archived flag left on an action that later got resolved). The
        // count is documented as approximate ("roughly the last 7 days"), so this rare overlap is an
        // acceptable simplification rather than a strict, de-duplicated ledger.
        var dispositionClearedCount = (await dbContext.ActionDispositions
            .AsNoTracking()
            .Where(disposition =>
                disposition.NeedlyUserId == needlyUserId &&
                (disposition.IsArchived || disposition.IsMuted))
            .Join(
                dbContext.Actions.AsNoTracking(),
                disposition => disposition.ActionId,
                action => action.Id,
                (disposition, action) => new { disposition.UpdatedAt, Action = action })
            .Where(row =>
                dbContext.Installations.Any(installation =>
                    installation.Id == row.Action.InstallationId &&
                    installation.State == InstallationState.Active) &&
                dbContext.InstallationMembers.Any(member =>
                    member.InstallationId == row.Action.InstallationId &&
                    member.GitHubUserId == gitHubUserId.Value &&
                    member.IsActive) &&
                ((row.Action.AssigneeType == ActionAssigneeType.User &&
                  row.Action.AssigneeId == gitHubUserId.Value) ||
                 (row.Action.AssigneeType == ActionAssigneeType.Team &&
                  dbContext.TeamMembers.Any(member =>
                      member.TeamId == row.Action.AssigneeId &&
                      member.GitHubUserId == gitHubUserId.Value &&
                      member.IsActive))))
            .Select(row => row.UpdatedAt)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false))
            .Count(updatedAt => updatedAt >= since);

        return stateClearedCount + dispositionClearedCount;
    }

    /// <inheritdoc />
    public async Task<bool> RestoreAsync(
        Guid needlyUserId,
        Guid actionId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var gitHubUserId = await GetGitHubUserIdAsync(dbContext, needlyUserId, cancellationToken)
            .ConfigureAwait(false);
        if (gitHubUserId is null)
        {
            return false;
        }

        var action = await dbContext.Actions.SingleOrDefaultAsync(action =>
            action.Id == actionId &&
            (action.State == ActionState.Archived || action.State == ActionState.Done) &&
            dbContext.Installations.Any(installation =>
                installation.Id == action.InstallationId &&
                installation.State == InstallationState.Active) &&
            dbContext.InstallationMembers.Any(member =>
                member.InstallationId == action.InstallationId &&
                member.GitHubUserId == gitHubUserId.Value &&
                member.IsActive) &&
            ((action.AssigneeType == ActionAssigneeType.User &&
              action.AssigneeId == gitHubUserId.Value) ||
             (action.AssigneeType == ActionAssigneeType.Team &&
              dbContext.TeamMembers.Any(member =>
                  member.TeamId == action.AssigneeId &&
                  member.GitHubUserId == gitHubUserId.Value &&
                  member.IsActive))),
            cancellationToken).ConfigureAwait(false);
        if (action is null)
        {
            return false;
        }

        action.ChangeState(ActionState.Open, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        broadcaster.Publish();
        return true;
    }

    private static async Task<Guid?> GetGitHubUserIdAsync(
        NeedlyDbContext dbContext,
        Guid needlyUserId,
        CancellationToken cancellationToken) =>
        await dbContext.NeedlyUsers
            .AsNoTracking()
            .Where(user => user.Id == needlyUserId)
            .Select(user => (Guid?)user.GitHubUserId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    private static async Task<Lookups> LoadLookupsAsync(
        NeedlyDbContext dbContext,
        Guid needlyUserId,
        IReadOnlyCollection<CandidateRow> rows,
        CancellationToken cancellationToken)
    {
        var userAssigneeIds = rows
            .Where(row => row.Action.AssigneeType == ActionAssigneeType.User)
            .Select(row => row.Action.AssigneeId)
            .Distinct()
            .ToArray();
        var teamAssigneeIds = rows
            .Where(row => row.Action.AssigneeType == ActionAssigneeType.Team)
            .Select(row => row.Action.AssigneeId)
            .Distinct()
            .ToArray();
        var actionIds = rows.Select(row => row.Action.Id).ToArray();

        var gitHubUsersById = userAssigneeIds.Length == 0
            ? new Dictionary<Guid, GitHubUser>()
            : await dbContext.GitHubUsers
                .AsNoTracking()
                .Where(user => userAssigneeIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, cancellationToken)
                .ConfigureAwait(false);
        var teamsById = teamAssigneeIds.Length == 0
            ? new Dictionary<Guid, Team>()
            : await dbContext.Teams
                .AsNoTracking()
                .Where(team => teamAssigneeIds.Contains(team.Id))
                .ToDictionaryAsync(team => team.Id, cancellationToken)
                .ConfigureAwait(false);

        var archiveUndosByActionId = (await dbContext.ActionLifecycleUndos
                .AsNoTracking()
                .Where(undo =>
                    actionIds.Contains(undo.ActionId) &&
                    undo.AppliedState == ActionState.Archived &&
                    undo.UsedAt == null)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .GroupBy(undo => undo.ActionId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(undo => undo.CreatedAt).First());

        var archiverUserIds = archiveUndosByActionId.Values
            .Select(undo => undo.NeedlyUserId)
            .Distinct()
            .ToArray();
        var archiverNamesById = archiverUserIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.NeedlyUsers
                .AsNoTracking()
                .Where(user => archiverUserIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken)
                .ConfigureAwait(false);

        var ruleExecutionsByActionId = (await dbContext.RuleExecutions
                .AsNoTracking()
                .Where(execution =>
                    execution.NeedlyUserId == needlyUserId &&
                    actionIds.Contains(execution.ActionId) &&
                    execution.Effect == RuleEffect.AutoArchive)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .GroupBy(execution => execution.ActionId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(execution => execution.ExecutedAt).First());

        return new Lookups(
            gitHubUsersById,
            teamsById,
            archiveUndosByActionId,
            archiverNamesById,
            ruleExecutionsByActionId);
    }

    private static DoneAction CreateDoneAction(CandidateRow row, Lookups lookups, DateTimeOffset now)
    {
        var (action, repository, isAutomationArchived) = row;
        var (closedReason, closedAt, closedBy, canRestore) = isAutomationArchived
            ? DescribeAutomationArchive(action, lookups)
            : DescribeLifecycleClosure(action, lookups);

        return new DoneAction(
            action.Id,
            repository.Owner,
            repository.Name,
            action.Title,
            action.SubjectNumber,
            action.SubjectType,
            action.SubjectUrl.Value,
            action.Type,
            action.State,
            action.Reason,
            action.Context,
            FormatAssignee(action, lookups),
            closedReason,
            closedAt,
            closedBy,
            now > closedAt ? now - closedAt : TimeSpan.Zero,
            action.AuthorLogin,
            action.AssigneeType == ActionAssigneeType.User
                ? ActionAssigneeScope.Me
                : ActionAssigneeScope.MyTeam,
            action.HasBotInvolvement,
            canRestore);
    }

    private static (string Reason, DateTimeOffset ClosedAt, string? ClosedBy, bool CanRestore) DescribeLifecycleClosure(
        NeedlyAction action,
        Lookups lookups)
    {
        if (action.State == ActionState.Done)
        {
            // GitHub resolution (merge, close, or a submitted review) is not distinguished on the
            // domain model today, so the closed reason is intentionally generic rather than guessed.
            return ("Completed on GitHub", action.UpdatedAt, null, true);
        }

        if (lookups.ArchiveUndosByActionId.TryGetValue(action.Id, out var undo))
        {
            var archivedBy = lookups.ArchiverNamesById.GetValueOrDefault(undo.NeedlyUserId);
            return ("Manually archived", undo.CreatedAt, archivedBy, true);
        }

        return ("Manually archived", action.UpdatedAt, null, true);
    }

    private static (string Reason, DateTimeOffset ClosedAt, string? ClosedBy, bool CanRestore) DescribeAutomationArchive(
        NeedlyAction action,
        Lookups lookups)
    {
        if (lookups.RuleExecutionsByActionId.TryGetValue(action.Id, out var execution))
        {
            return (execution.Explanation, execution.ExecutedAt, $"Rule: {execution.RuleName}", false);
        }

        return ("Archived by an automation rule", action.UpdatedAt, null, false);
    }

    private static string FormatAssignee(NeedlyAction action, Lookups lookups)
    {
        if (action.AssigneeType == ActionAssigneeType.User)
        {
            lookups.GitHubUsersById.TryGetValue(action.AssigneeId, out var user);
            return string.IsNullOrWhiteSpace(user?.DisplayName)
                ? $"@{user?.Login}"
                : $"{user.DisplayName} (@{user.Login})";
        }

        lookups.TeamsById.TryGetValue(action.AssigneeId, out var team);
        return string.IsNullOrWhiteSpace(team?.Name)
            ? $"@{team?.Slug}"
            : $"{team.Name} (@{team.Slug})";
    }

    private sealed record CandidateRow(NeedlyAction Action, Repository Repository, bool IsAutomationArchived);

    private sealed record Lookups(
        IReadOnlyDictionary<Guid, GitHubUser> GitHubUsersById,
        IReadOnlyDictionary<Guid, Team> TeamsById,
        IReadOnlyDictionary<Guid, ActionLifecycleUndo> ArchiveUndosByActionId,
        IReadOnlyDictionary<Guid, string> ArchiverNamesById,
        IReadOnlyDictionary<Guid, RuleExecution> RuleExecutionsByActionId);
}
