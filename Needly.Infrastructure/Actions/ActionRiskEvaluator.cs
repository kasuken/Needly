using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Needly.Application.Actions;
using Needly.Domain;

namespace Needly.Infrastructure.Actions;

/// <summary>Evaluates persisted open actions against configured attention thresholds.</summary>
public sealed class ActionRiskEvaluator(
    NeedlyDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<ActionRiskOptions> options,
    IActionChangeBroadcaster? broadcaster = null) : IActionRiskEvaluator
{
    private readonly ActionRiskOptions options = options.Value;

    /// <inheritdoc />
    public async Task<int> EvaluateAsync(CancellationToken cancellationToken)
    {
        var actions = await dbContext.Actions
            .Where(action => action.State == ActionState.Open)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        var changed = 0;
        foreach (var action in actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var riskReason = GetRiskReason(action, now);
            if (riskReason is null)
            {
                if (action.IsAtRisk)
                {
                    action.ClearRisk();
                    changed++;
                }
            }
            else if (!action.IsAtRisk || !string.Equals(action.RiskReason, riskReason, StringComparison.Ordinal))
            {
                action.MarkAtRisk(riskReason);
                changed++;
            }
        }

        if (changed > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            broadcaster?.Publish();
        }

        return changed;
    }

    private string? GetRiskReason(NeedlyAction action, DateTimeOffset now)
    {
        if (action.Type == ActionType.Review && now - action.WaitingSince > options.ReviewWaitingThreshold)
        {
            return $"Review has been waiting longer than {options.ReviewWaitingThreshold}.";
        }

        return now - action.LastActivityAt > options.InactivityThreshold
            ? $"Action has had no activity for longer than {options.InactivityThreshold}."
            : null;
    }
}