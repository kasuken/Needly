using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Needly.Application.Actions;
using Needly.Application.GitHub;
using Needly.Domain;
using Needly.Infrastructure.Actions;

namespace Needly.Infrastructure.GitHub;

/// <summary>Applies detector operations for verified GitHub events as one durable transaction.</summary>
public sealed class GitHubActionEventHandler(
    IDbContextFactory<NeedlyDbContext> contextFactory,
    IEnumerable<IGitHubActionDetector> detectors,
    ILogger<GitHubActionEventHandler> logger,
    IActionChangeBroadcaster? broadcaster = null,
    AutomationRuleEvaluator? ruleEvaluator = null)
    : IGitHubActionEventHandler
{
    private readonly IReadOnlyList<IGitHubActionDetector> detectors = OrderDetectors(detectors);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The event's installation, repository, or an operation's active assignee is unavailable.
    /// </exception>
    public async Task HandleAsync(GitHubStoredEvent storedEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storedEvent);
        cancellationToken.ThrowIfCancellationRequested();

        if (detectors.Count == 0 ||
            (storedEvent.EventName is "check_suite" or "check_run" or "workflow_run" &&
             storedEvent.GitHubRepositoryId is null))
        {
            return;
        }

        await using var strategyContext = await contextFactory
            .CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        var committedActionChange = false;
        await strategy.ExecuteAsync(async () =>
        {
            await using var dbContext = await contextFactory
                .CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await dbContext.Database
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var actionChanged = await HandleInTransactionAsync(
                dbContext,
                storedEvent,
                cancellationToken).ConfigureAwait(false);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            committedActionChange = actionChanged;
        }).ConfigureAwait(false);

        if (committedActionChange)
        {
            broadcaster?.Publish();
        }

        logger.LogInformation(
            "Applied action detectors to stored GitHub event {EventId} of type {EventName}",
            storedEvent.EventId,
            storedEvent.EventName);
    }

    private async Task<bool> HandleInTransactionAsync(
        NeedlyDbContext dbContext,
        GitHubStoredEvent storedEvent,
        CancellationToken cancellationToken)
    {
        var isCheckEvent = storedEvent.EventName is "check_suite" or "check_run" or "workflow_run";
        var installation = await dbContext.Installations.SingleOrDefaultAsync(
            item => item.GitHubInstallationId == storedEvent.GitHubInstallationId,
            cancellationToken).ConfigureAwait(false);
        if (installation is null)
        {
            if (isCheckEvent)
            {
                return false;
            }

            throw new InvalidOperationException(
                $"GitHub installation {storedEvent.GitHubInstallationId} is unavailable for action processing.");
        }

        if (storedEvent.GitHubRepositoryId is not { } gitHubRepositoryId)
        {
            throw new InvalidOperationException(
                $"Stored event {storedEvent.EventId} has no repository for action processing.");
        }

        var repository = await dbContext.Repositories.SingleOrDefaultAsync(
            item => item.InstallationId == installation.Id &&
                item.GitHubRepositoryId == gitHubRepositoryId,
            cancellationToken).ConfigureAwait(false);
        if (repository is null)
        {
            if (isCheckEvent)
            {
                return false;
            }

            throw new InvalidOperationException(
                $"GitHub repository {gitHubRepositoryId} is unavailable for installation {storedEvent.GitHubInstallationId}.");
        }

        var userIdentities = await dbContext.InstallationMembers
            .Where(member => member.InstallationId == installation.Id)
            .Join(
                dbContext.GitHubUsers,
                member => member.GitHubUserId,
                user => user.Id,
                (member, user) => new UserIdentity(member, user))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var teams = await dbContext.Teams
            .Where(team => team.InstallationId == installation.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var teamMemberships = await dbContext.TeamMembers
            .Where(member => member.IsActive && teams.Select(team => team.Id).Contains(member.TeamId))
            .Join(
                dbContext.GitHubUsers,
                member => member.GitHubUserId,
                user => user.Id,
                (member, user) => new { member.TeamId, user.GitHubUserId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var actions = await dbContext.Actions
            .Where(action => action.RepositoryId == repository.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var suppressions = await dbContext.ActionSuppressions
            .Where(suppression => suppression.RepositoryId == repository.Id && suppression.IsActive)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        actions.Sort((left, right) =>
        {
            var timestampComparison = left.CreatedAt.CompareTo(right.CreatedAt);
            return timestampComparison != 0 ? timestampComparison : left.Id.CompareTo(right.Id);
        });
        var processedDetectorKeys = await dbContext.ActionEventReceipts
            .Where(receipt => receipt.EventId == storedEvent.EventId)
            .Select(receipt => receipt.DetectorKey)
            .ToHashSetAsync(cancellationToken).ConfigureAwait(false);
        var stateStore = new GitHubActionStateStore(dbContext, repository.Id);

        var availableIdentities = userIdentities
            .Where(identity => identity.Member.IsActive)
            .Select(identity => new GitHubActionIdentity(
                ActionAssigneeType.User,
                identity.User.Id,
                identity.User.GitHubUserId,
                identity.User.Login))
            .Concat(teams
                .Where(team => team.IsActive)
                .Select(team => new GitHubActionIdentity(
                    ActionAssigneeType.Team,
                    team.Id,
                    team.GitHubTeamId,
                    team.Slug,
                    teamMemberships
                        .Where(member => member.TeamId == team.Id)
                        .Select(member => member.GitHubUserId)
                        .OrderBy(gitHubUserId => gitHubUserId)
                        .ToArray())))
            .OrderBy(identity => identity.Type)
            .ThenBy(identity => identity.GitHubId)
            .ToArray();

        var actionChanged = false;
        foreach (var detector in detectors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (processedDetectorKeys.Contains(detector.Key))
            {
                continue;
            }

            var context = new GitHubActionDetectionContext(
                storedEvent,
                new GitHubActionInstallation(installation.Id, installation.GitHubInstallationId),
                new GitHubActionRepository(
                    repository.Id,
                    repository.GitHubRepositoryId,
                    repository.Owner,
                    repository.Name),
                availableIdentities,
                CreateActionSnapshots(actions, userIdentities, teams),
                stateStore);
            var operations = await detector.DetectAsync(context, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Action detector '{detector.Key}' returned no operation collection.");

            foreach (var operation in operations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                actionChanged |= ApplyOperation(
                    dbContext,
                    operation,
                    repository,
                    availableIdentities,
                    userIdentities,
                    teams,
                    actions,
                    suppressions);
            }

            dbContext.ActionEventReceipts.Add(ActionEventReceipt.Create(
                Guid.NewGuid(),
                storedEvent.EventId,
                detector.Key,
                storedEvent.ReceivedAt));
            processedDetectorKeys.Add(detector.Key);
        }

        dbContext.ChangeTracker.DetectChanges();
        var changedActions = dbContext.ChangeTracker.Entries<NeedlyAction>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .Select(entry => entry.Entity)
            .DistinctBy(action => action.Id)
            .ToArray();
        if (changedActions.Length > 0)
        {
            await ApplyFilterMetadataAsync(
                dbContext, storedEvent, changedActions, cancellationToken).ConfigureAwait(false);
            if (ruleEvaluator is not null)
            {
                await ruleEvaluator.EvaluateAsync(
                    dbContext, storedEvent, changedActions, cancellationToken).ConfigureAwait(false);
            }
        }

        return actionChanged;
    }

    private static async Task ApplyFilterMetadataAsync(
        NeedlyDbContext dbContext,
        GitHubStoredEvent storedEvent,
        IReadOnlyList<NeedlyAction> actions,
        CancellationToken cancellationToken)
    {
        var payload = System.Text.Json.JsonSerializer.Deserialize<GitHubActionWebhookPayload>(
            storedEvent.PayloadJson,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var payloadAuthor = payload?.PullRequest?.User ?? payload?.Issue?.User;
        foreach (var action in actions)
        {
            var author = payloadAuthor;
            if (author is null && action.SubjectType == GitHubSubjectType.PullRequest)
            {
                var state = dbContext.Set<GitHubPullRequestStateEntity>().Local.SingleOrDefault(item =>
                    item.RepositoryId == action.RepositoryId &&
                    item.PullRequestNumber == action.SubjectNumber)
                    ?? await dbContext.Set<GitHubPullRequestStateEntity>().SingleOrDefaultAsync(item =>
                        item.RepositoryId == action.RepositoryId &&
                        item.PullRequestNumber == action.SubjectNumber,
                        cancellationToken).ConfigureAwait(false);
                action.UpdateFilterMetadata(
                    state?.AuthorLogin,
                    IsBot(state?.AuthorLogin, null) || IsBot(payload?.Sender?.Login, payload?.Sender?.Type));
                continue;
            }

            action.UpdateFilterMetadata(
                author?.Login,
                IsBot(author?.Login, author?.Type) || IsBot(payload?.Sender?.Login, payload?.Sender?.Type));
        }
    }

    private static bool IsBot(string? login, string? type) =>
        string.Equals(type, "Bot", StringComparison.OrdinalIgnoreCase) ||
        login?.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase) == true;

    private static IReadOnlyList<GitHubActionSnapshot> CreateActionSnapshots(
        IReadOnlyList<NeedlyAction> actions,
        IReadOnlyList<UserIdentity> userIdentities,
        IReadOnlyList<Team> teams) =>
        actions.Select(action => new GitHubActionSnapshot(
            action.Id,
            CreateTarget(action, userIdentities, teams),
            action.State,
            action.Title,
            action.Context,
            action.Reason,
            action.LastActivityAt)).ToArray();

    private static GitHubActionTarget CreateTarget(
        NeedlyAction action,
        IReadOnlyList<UserIdentity> userIdentities,
        IReadOnlyList<Team> teams)
    {
        var gitHubAssigneeId = action.AssigneeType switch
        {
            ActionAssigneeType.User => userIdentities
                .Single(identity => identity.User.Id == action.AssigneeId)
                .User.GitHubUserId,
            ActionAssigneeType.Team => teams
                .Single(team => team.Id == action.AssigneeId)
                .GitHubTeamId,
            _ => throw new InvalidOperationException($"Unsupported action assignee type {action.AssigneeType}.")
        };

        return new GitHubActionTarget(
            action.Type,
            action.SubjectType,
            action.SubjectNumber,
            action.AssigneeType,
            gitHubAssigneeId);
    }

    private bool ApplyOperation(
        NeedlyDbContext dbContext,
        GitHubActionOperation operation,
        Repository repository,
        IReadOnlyList<GitHubActionIdentity> identities,
        IReadOnlyList<UserIdentity> userIdentities,
        IReadOnlyList<Team> teams,
        List<NeedlyAction> actions,
        IReadOnlyList<ActionSuppression> suppressions)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var identity = identities.SingleOrDefault(item =>
            item.Type == operation.Target.AssigneeType &&
            item.GitHubId == operation.Target.GitHubAssigneeId)
            ?? throw new InvalidOperationException(
                $"GitHub {operation.Target.AssigneeType} assignee {operation.Target.GitHubAssigneeId} is unavailable for action processing.");
        var matchingActions = actions.Where(action =>
            action.Type == operation.Target.Type &&
            action.SubjectType == operation.Target.SubjectType &&
            action.SubjectNumber == operation.Target.SubjectNumber &&
            action.AssigneeType == identity.Type &&
            action.AssigneeId == identity.Id);
        var activeAction = matchingActions.SingleOrDefault(action =>
            action.State is ActionState.Open or ActionState.Snoozed);

        if (operation is CreateGitHubActionOperation &&
            identity.Type == ActionAssigneeType.User &&
            suppressions.Any(suppression =>
            suppression.InstallationId == repository.InstallationId &&
            suppression.RepositoryId == repository.Id &&
            suppression.SubjectType == operation.Target.SubjectType &&
            suppression.SubjectNumber == operation.Target.SubjectNumber &&
            suppression.AssigneeType == identity.Type &&
            suppression.AssigneeId == identity.Id))
        {
            return false;
        }

        switch (operation)
        {
            case CreateGitHubActionOperation create:
                return ApplyCreate(
                    dbContext,
                    create,
                    repository,
                    identity,
                    userIdentities,
                    teams,
                    matchingActions,
                    activeAction,
                    actions);
            case UpdateGitHubActionOperation update when activeAction is not null:
                activeAction.ApplyEvent(
                    activeAction.Key,
                    update.Title,
                    update.Context,
                    update.Reason,
                    update.OccurredAt,
                    update.OccurredAt);
                return true;
            case ResolveGitHubActionOperation resolve when activeAction is not null:
                activeAction.ChangeState(ActionState.Done, resolve.OccurredAt);
                return true;
            default:
                return false;
        }
    }

    private bool ApplyCreate(
        NeedlyDbContext dbContext,
        CreateGitHubActionOperation operation,
        Repository repository,
        GitHubActionIdentity identity,
        IReadOnlyList<UserIdentity> userIdentities,
        IReadOnlyList<Team> teams,
        IEnumerable<NeedlyAction> matchingActions,
        NeedlyAction? activeAction,
        List<NeedlyAction> actions)
    {
        if (activeAction is not null)
        {
            if (activeAction.State == ActionState.Snoozed &&
                operation.Significance == ActionEventSignificance.Significant)
            {
                activeAction.ChangeState(ActionState.Open, operation.OccurredAt);
            }

            activeAction.ApplyEvent(
                activeAction.Key,
                operation.Title,
                operation.Context,
                operation.Reason,
                operation.OccurredAt,
                operation.OccurredAt);
            return true;
        }

        var latestTerminal = matchingActions
            .OrderByDescending(action => action.UpdatedAt)
            .ThenByDescending(action => action.Id)
            .FirstOrDefault();
        if (latestTerminal?.State == ActionState.Muted)
        {
            return false;
        }

        if (latestTerminal?.State == ActionState.Archived)
        {
            if (operation.Significance != ActionEventSignificance.Significant)
            {
                return false;
            }

            latestTerminal.ChangeState(ActionState.Open, operation.OccurredAt);
            latestTerminal.ApplyEvent(
                latestTerminal.Key,
                operation.Title,
                operation.Context,
                operation.Reason,
                operation.OccurredAt,
                operation.OccurredAt);
            return true;
        }

        if (latestTerminal?.State == ActionState.Done)
        {
            if (!operation.ReactivateTerminal)
            {
                return false;
            }

            latestTerminal.ChangeState(ActionState.Open, operation.OccurredAt);
            latestTerminal.ApplyEvent(
                latestTerminal.Key,
                operation.Title,
                operation.Context,
                operation.Reason,
                operation.OccurredAt,
                operation.OccurredAt);
            return true;
        }

        var action = identity.Type switch
        {
            ActionAssigneeType.User => NeedlyAction.CreateForUser(
                Guid.NewGuid(),
                operation.Target.Type,
                repository,
                userIdentities.Single(item => item.User.Id == identity.Id).User,
                operation.Target.SubjectType,
                operation.Target.SubjectNumber,
                operation.SubjectUrl,
                operation.Title,
                operation.Context,
                operation.Reason,
                operation.OccurredAt),
            ActionAssigneeType.Team => NeedlyAction.CreateForTeam(
                Guid.NewGuid(),
                operation.Target.Type,
                repository,
                teams.Single(team => team.Id == identity.Id),
                operation.Target.SubjectType,
                operation.Target.SubjectNumber,
                operation.SubjectUrl,
                operation.Title,
                operation.Context,
                operation.Reason,
                operation.OccurredAt),
            _ => throw new InvalidOperationException($"Unsupported action assignee type {identity.Type}.")
        };
        dbContext.Actions.Add(action);
        actions.Add(action);
        return true;
    }

    private static IReadOnlyList<IGitHubActionDetector> OrderDetectors(
        IEnumerable<IGitHubActionDetector> detectors)
    {
        ArgumentNullException.ThrowIfNull(detectors);
        var ordered = detectors
            .OrderBy(detector => detector.Order)
            .ThenBy(detector => detector.Key, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Any(detector => string.IsNullOrWhiteSpace(detector.Key) || detector.Key.Length > 200))
        {
            throw new InvalidOperationException("Action detector keys must contain between 1 and 200 characters.");
        }

        var duplicateKey = ordered
            .GroupBy(detector => detector.Key, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateKey is not null)
        {
            throw new InvalidOperationException($"Action detector key '{duplicateKey}' is registered more than once.");
        }

        return ordered;
    }

    private sealed record UserIdentity(InstallationMember Member, GitHubUser User);
}