using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Needly.Application.Actions;
using Needly.Domain;
using Needly.Infrastructure.GitHub;

namespace Needly.Infrastructure.Actions;

/// <summary>Evaluates outstanding reviewer feedback for staleness and escalates it to Follow up actions.</summary>
public sealed class FollowUpEvaluator(
    NeedlyDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<FollowUpOptions> options,
    IActionChangeBroadcaster? broadcaster = null) : IFollowUpEvaluator
{
    private readonly FollowUpOptions options = options.Value;

    /// <inheritdoc />
    public async Task<int> EvaluateAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var staleBefore = now - options.StaleFeedbackThreshold;
        var stale = await dbContext.Set<GitHubReviewerFeedbackStateEntity>()
            .Where(feedback => feedback.HasOutstandingChanges && feedback.UpdatedAt <= staleBefore)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var changed = 0;
        foreach (var feedback in stale)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await CreateIfMissingAsync(feedback, now, cancellationToken).ConfigureAwait(false))
            {
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

    private async Task<bool> CreateIfMissingAsync(
        GitHubReviewerFeedbackStateEntity feedback,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var repository = await dbContext.Repositories
            .SingleOrDefaultAsync(item => item.Id == feedback.RepositoryId, cancellationToken)
            .ConfigureAwait(false);
        var reviewer = await dbContext.GitHubUsers
            .SingleOrDefaultAsync(item => item.GitHubUserId == feedback.ReviewerGitHubUserId, cancellationToken)
            .ConfigureAwait(false);
        if (repository is null || reviewer is null)
        {
            return false;
        }

        var key = ActionKey.Create(
            ActionType.FollowUp,
            repository.Id,
            GitHubSubjectType.PullRequest,
            feedback.PullRequestNumber,
            ActionAssigneeType.User,
            reviewer.Id);
        var hasActiveFollowUp = await dbContext.Actions
            .AnyAsync(
                action => action.Key == key && (action.State == ActionState.Open || action.State == ActionState.Snoozed),
                cancellationToken)
            .ConfigureAwait(false);
        if (hasActiveFollowUp)
        {
            return false;
        }

        var action = NeedlyAction.CreateForUser(
            Guid.NewGuid(),
            ActionType.FollowUp,
            repository,
            reviewer,
            GitHubSubjectType.PullRequest,
            feedback.PullRequestNumber,
            $"https://github.com/{repository.Owner}/{repository.Name}/pull/{feedback.PullRequestNumber}",
            $"Follow up on PR #{feedback.PullRequestNumber}",
            $"Requested changes for @{feedback.ReviewerLogin} have had no new activity since {feedback.UpdatedAt:u}.",
            "Requested changes have gone unaddressed past the configured follow-up threshold.",
            now);
        dbContext.Actions.Add(action);
        return true;
    }
}
