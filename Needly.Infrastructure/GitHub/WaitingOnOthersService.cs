using Microsoft.EntityFrameworkCore;
using Needly.Application.GitHub;
using Needly.Domain;

namespace Needly.Infrastructure.GitHub;

/// <summary>
/// Queries the user's own open pull requests that are blocked waiting on somebody else, directly over the
/// durable GitHub action state already collected by the detection pipeline. This is a read-only projection:
/// it introduces no new persisted state and no new detectors.
/// </summary>
public sealed class WaitingOnOthersService(
    NeedlyDbContext dbContext,
    TimeProvider timeProvider) : IWaitingOnOthersService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<WaitingOnOthersItem>> GetWaitingAsync(
        Guid needlyUserId,
        CancellationToken cancellationToken)
    {
        var author = await dbContext.NeedlyUsers
            .AsNoTracking()
            .Where(user => user.Id == needlyUserId)
            .Join(
                dbContext.GitHubUsers.AsNoTracking(),
                user => user.GitHubUserId,
                gitHubUser => gitHubUser.Id,
                (user, gitHubUser) => new { gitHubUser.Id, gitHubUser.GitHubUserId })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (author is null)
        {
            return [];
        }

        var pullRequests = await dbContext.Set<GitHubPullRequestStateEntity>()
            .AsNoTracking()
            .Where(pullRequest => pullRequest.AuthorGitHubUserId == author.GitHubUserId && pullRequest.IsOpen)
            .Join(
                dbContext.Repositories.AsNoTracking().Where(repository => repository.IsActive),
                pullRequest => pullRequest.RepositoryId,
                repository => repository.Id,
                (pullRequest, repository) => new { PullRequest = pullRequest, Repository = repository })
            .Where(item =>
                dbContext.Installations.Any(installation =>
                    installation.Id == item.Repository.InstallationId &&
                    installation.State == InstallationState.Active) &&
                dbContext.InstallationMembers.Any(member =>
                    member.InstallationId == item.Repository.InstallationId &&
                    member.GitHubUserId == author.Id &&
                    member.IsActive))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (pullRequests.Count == 0)
        {
            return [];
        }

        var repositoryIds = pullRequests.Select(item => item.Repository.Id).Distinct().ToArray();
        var pullRequestNumbers = pullRequests.Select(item => item.PullRequest.PullRequestNumber).Distinct().ToArray();

        var reviewRequests = await dbContext.Set<GitHubReviewRequestStateEntity>()
            .AsNoTracking()
            .Where(state =>
                repositoryIds.Contains(state.RepositoryId) &&
                pullRequestNumbers.Contains(state.PullRequestNumber))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var reviewerFeedback = await dbContext.Set<GitHubReviewerFeedbackStateEntity>()
            .AsNoTracking()
            .Where(state =>
                repositoryIds.Contains(state.RepositoryId) &&
                pullRequestNumbers.Contains(state.PullRequestNumber))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = timeProvider.GetUtcNow();
        var items = new List<WaitingOnOthersItem>();
        foreach (var item in pullRequests)
        {
            var pullRequest = item.PullRequest;
            var repository = item.Repository;
            var requests = reviewRequests
                .Where(state =>
                    state.RepositoryId == repository.Id &&
                    state.PullRequestNumber == pullRequest.PullRequestNumber)
                .ToArray();
            var feedback = reviewerFeedback
                .Where(state =>
                    state.RepositoryId == repository.Id &&
                    state.PullRequestNumber == pullRequest.PullRequestNumber)
                .ToArray();

            if (Categorize(pullRequest, requests, feedback) is not { } waiting)
            {
                continue;
            }

            items.Add(new WaitingOnOthersItem(
                repository.Id,
                repository.Owner,
                repository.Name,
                pullRequest.PullRequestNumber,
                pullRequest.Title,
                pullRequest.Url,
                pullRequest.IsDraft,
                waiting.Reason,
                waiting.BlockingParty,
                waiting.WaitingSince,
                now > waiting.WaitingSince ? now - waiting.WaitingSince : TimeSpan.Zero));
        }

        return items
            .OrderByDescending(entry => entry.WaitingDuration)
            .ToArray();
    }

    /// <summary>
    /// Determines whether one pull request is currently waiting on somebody else and, if so, why.
    /// </summary>
    /// <remarks>
    /// The durable state store does not persist a pull request's "opened at" timestamp or a per-review head
    /// SHA, so two of the three buckets use the closest available proxy: a requested-reviewer's own
    /// <see cref="GitHubReviewRequestStateEntity.UpdatedAt"/> for "awaiting review" (exact), and the pull
    /// request's own <see cref="GitHubPullRequestStateEntity.UpdatedAt"/> (last known GitHub-side activity,
    /// bumped on every push) for "awaiting re-review" and "no reviewer assigned" (an approximation, called out
    /// in the PR description).
    /// </remarks>
    private static (WaitingOnOthersReason Reason, string BlockingParty, DateTimeOffset WaitingSince)? Categorize(
        GitHubPullRequestStateEntity pullRequest,
        IReadOnlyCollection<GitHubReviewRequestStateEntity> requests,
        IReadOnlyCollection<GitHubReviewerFeedbackStateEntity> feedback)
    {
        var activeRequests = requests.Where(request => request.IsRequested).ToArray();
        if (activeRequests.Length > 0)
        {
            var earliest = activeRequests.Min(request => request.UpdatedAt);
            var reviewers = string.Join(", ", activeRequests
                .OrderBy(request => request.AssigneeLogin, StringComparer.OrdinalIgnoreCase)
                .Select(request => $"@{request.AssigneeLogin}"));
            return (WaitingOnOthersReason.AwaitingReview, reviewers, earliest);
        }

        var outstanding = feedback
            .Where(state => state.HasOutstandingChanges && pullRequest.UpdatedAt > state.UpdatedAt)
            .OrderBy(state => state.ReviewerLogin, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (outstanding.Length > 0)
        {
            var reviewers = string.Join(", ", outstanding.Select(state => $"@{state.ReviewerLogin}"));
            return (WaitingOnOthersReason.AwaitingReReview, reviewers, pullRequest.UpdatedAt);
        }

        if (feedback.Count == 0)
        {
            return (WaitingOnOthersReason.NoReviewerAssigned, "Unassigned", pullRequest.UpdatedAt);
        }

        return null;
    }
}
