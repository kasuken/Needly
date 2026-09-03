using Microsoft.EntityFrameworkCore;
using Needly.Application.Actions;
using Needly.Domain;

namespace Needly.Infrastructure.Actions;

/// <summary>Persists ordered per-user automation rules and queries execution history.</summary>
public sealed class AutomationRuleService(
    IDbContextFactory<NeedlyDbContext> contextFactory,
    TimeProvider timeProvider,
    IActionChangeBroadcaster broadcaster) : IAutomationRuleService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<AutomationRuleItem>> GetAsync(
        Guid needlyUserId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rules = await dbContext.AutomationRules.AsNoTracking()
            .Where(rule => rule.NeedlyUserId == needlyUserId)
            .OrderBy(rule => rule.SortOrder)
            .ThenBy(rule => rule.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rules.Select(ToItem).ToArray();
    }

    /// <inheritdoc />
    public async Task<AutomationRuleItem> CreateAsync(
        Guid needlyUserId,
        string name,
        ActionFilter filter,
        RuleEffect effect,
        TimeSpan? snoozeDuration,
        CancellationToken cancellationToken)
    {
        var json = ActionFilterJsonSerializer.Serialize(filter);
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await EnsureUniqueNameAsync(dbContext, needlyUserId, null, name, cancellationToken).ConfigureAwait(false);
        var sortOrder = await dbContext.AutomationRules
            .Where(rule => rule.NeedlyUserId == needlyUserId)
            .Select(rule => (int?)rule.SortOrder)
            .MaxAsync(cancellationToken).ConfigureAwait(false) + 1 ?? 0;
        var rule = AutomationRule.Create(
            Guid.NewGuid(), needlyUserId, name, json, effect, snoozeDuration, true, sortOrder, timeProvider.GetUtcNow());
        dbContext.AutomationRules.Add(rule);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        broadcaster.Publish();
        return ToItem(rule);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(
        Guid needlyUserId,
        Guid ruleId,
        string name,
        ActionFilter filter,
        RuleEffect effect,
        TimeSpan? snoozeDuration,
        CancellationToken cancellationToken)
    {
        var json = ActionFilterJsonSerializer.Serialize(filter);
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rule = await FindAsync(dbContext, needlyUserId, ruleId, cancellationToken).ConfigureAwait(false);
        if (rule is null)
        {
            return false;
        }

        await EnsureUniqueNameAsync(dbContext, needlyUserId, ruleId, name, cancellationToken).ConfigureAwait(false);
        rule.Update(name, json, effect, snoozeDuration, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        broadcaster.Publish();
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> SetEnabledAsync(
        Guid needlyUserId,
        Guid ruleId,
        bool isEnabled,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rule = await FindAsync(dbContext, needlyUserId, ruleId, cancellationToken).ConfigureAwait(false);
        if (rule is null)
        {
            return false;
        }

        rule.SetEnabled(isEnabled, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        broadcaster.Publish();
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        Guid needlyUserId,
        Guid ruleId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rule = await FindAsync(dbContext, needlyUserId, ruleId, cancellationToken).ConfigureAwait(false);
        if (rule is null)
        {
            return false;
        }

        dbContext.AutomationRules.Remove(rule);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await NormalizeOrderAsync(dbContext, needlyUserId, cancellationToken).ConfigureAwait(false);
        broadcaster.Publish();
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> MoveAsync(
        Guid needlyUserId,
        Guid ruleId,
        int direction,
        CancellationToken cancellationToken)
    {
        if (direction is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rules = await dbContext.AutomationRules
            .Where(rule => rule.NeedlyUserId == needlyUserId)
            .OrderBy(rule => rule.SortOrder)
            .ThenBy(rule => rule.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var currentIndex = rules.FindIndex(rule => rule.Id == ruleId);
        var targetIndex = currentIndex + direction;
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= rules.Count)
        {
            return false;
        }

        (rules[currentIndex], rules[targetIndex]) = (rules[targetIndex], rules[currentIndex]);
        var now = timeProvider.GetUtcNow();
        for (var index = 0; index < rules.Count; index++)
        {
            rules[index].Reorder(index, now);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        broadcaster.Publish();
        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RuleExecutionItem>> GetHistoryAsync(
        Guid needlyUserId,
        int maximumCount,
        CancellationToken cancellationToken)
    {
        if (maximumCount is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await dbContext.RuleExecutions.AsNoTracking()
            .Where(execution => execution.NeedlyUserId == needlyUserId)
            .OrderByDescending(execution => execution.ExecutedAt)
            .ThenByDescending(execution => execution.Id)
            .Take(maximumCount)
            .Join(
                dbContext.Actions.AsNoTracking(),
                execution => execution.ActionId,
                action => action.Id,
                (execution, action) => new RuleExecutionItem(
                    execution.Id,
                    execution.RuleId,
                    execution.RuleName,
                    execution.ActionId,
                    action.Title,
                    execution.Effect,
                    execution.Explanation,
                    execution.ExecutedAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private static AutomationRuleItem ToItem(AutomationRule rule) => new(
        rule.Id,
        rule.Name,
        ActionFilterJsonSerializer.Deserialize(rule.FilterJson),
        rule.Effect,
        rule.SnoozeDuration,
        rule.IsEnabled,
        rule.SortOrder);

    private static Task<AutomationRule?> FindAsync(
        NeedlyDbContext dbContext,
        Guid needlyUserId,
        Guid ruleId,
        CancellationToken cancellationToken) =>
        dbContext.AutomationRules.SingleOrDefaultAsync(
            rule => rule.Id == ruleId && rule.NeedlyUserId == needlyUserId,
            cancellationToken);

    private static async Task EnsureUniqueNameAsync(
        NeedlyDbContext dbContext,
        Guid needlyUserId,
        Guid? excludedId,
        string name,
        CancellationToken cancellationToken)
    {
        var normalizedName = (name ?? string.Empty).Trim().ToUpperInvariant();
        if (await dbContext.AutomationRules.AnyAsync(
            rule => rule.NeedlyUserId == needlyUserId &&
                rule.Id != excludedId &&
                rule.NormalizedName == normalizedName,
            cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("An automation rule with this name already exists.");
        }
    }

    private async Task NormalizeOrderAsync(
        NeedlyDbContext dbContext,
        Guid needlyUserId,
        CancellationToken cancellationToken)
    {
        var rules = await dbContext.AutomationRules
            .Where(rule => rule.NeedlyUserId == needlyUserId)
            .OrderBy(rule => rule.SortOrder)
            .ThenBy(rule => rule.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        for (var index = 0; index < rules.Count; index++)
        {
            rules[index].Reorder(index, now);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}