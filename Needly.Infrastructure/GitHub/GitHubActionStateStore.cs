using Microsoft.EntityFrameworkCore;
using Needly.Application.GitHub;
using Needly.Domain;

namespace Needly.Infrastructure.GitHub;

internal sealed class GitHubActionStateStore(NeedlyDbContext dbContext, Guid repositoryId)
    : IGitHubActionStateStore
{
    public async Task<GitHubPullRequestState?> GetPullRequestAsync(
        int pullRequestNumber,
        CancellationToken cancellationToken)
    {
        var local = dbContext.Set<GitHubPullRequestStateEntity>().Local.SingleOrDefault(
            state => state.RepositoryId == repositoryId && state.PullRequestNumber == pullRequestNumber);
        var entity = local ?? await dbContext.Set<GitHubPullRequestStateEntity>()
            .Where(state => state.RepositoryId == repositoryId && state.PullRequestNumber == pullRequestNumber)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return entity?.ToState();
    }

    public async Task UpsertPullRequestAsync(
        GitHubPullRequestState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        var entity = dbContext.Set<GitHubPullRequestStateEntity>().Local.SingleOrDefault(
            item => item.RepositoryId == repositoryId && item.PullRequestNumber == state.PullRequestNumber)
            ?? await dbContext.Set<GitHubPullRequestStateEntity>().SingleOrDefaultAsync(
                item => item.RepositoryId == repositoryId && item.PullRequestNumber == state.PullRequestNumber,
                cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            dbContext.Add(GitHubPullRequestStateEntity.Create(repositoryId, state));
        }
        else
        {
            entity.Update(state);
        }
    }

    public async Task<IReadOnlyList<GitHubReviewRequestState>> GetReviewRequestsAsync(
        int pullRequestNumber,
        CancellationToken cancellationToken) =>
        MapReviewRequests(await LoadWithAddedAsync(
            dbContext.Set<GitHubReviewRequestStateEntity>(),
            state => state.RepositoryId == repositoryId && state.PullRequestNumber == pullRequestNumber,
            cancellationToken).ConfigureAwait(false));

    private static IReadOnlyList<GitHubReviewRequestState> MapReviewRequests(
        IEnumerable<GitHubReviewRequestStateEntity> states) =>
        states
            .OrderBy(state => state.AssigneeType)
            .ThenBy(state => state.GitHubAssigneeId)
            .Select(state => state.ToState())
            .ToArray();

    public async Task UpsertReviewRequestAsync(
        GitHubReviewRequestState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        var entity = dbContext.Set<GitHubReviewRequestStateEntity>().Local.SingleOrDefault(item =>
            item.RepositoryId == repositoryId &&
            item.PullRequestNumber == state.PullRequestNumber &&
            item.AssigneeType == state.AssigneeType &&
            item.GitHubAssigneeId == state.GitHubAssigneeId)
            ?? await dbContext.Set<GitHubReviewRequestStateEntity>().SingleOrDefaultAsync(
                item => item.RepositoryId == repositoryId &&
                    item.PullRequestNumber == state.PullRequestNumber &&
                    item.AssigneeType == state.AssigneeType &&
                    item.GitHubAssigneeId == state.GitHubAssigneeId,
                cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            dbContext.Add(GitHubReviewRequestStateEntity.Create(repositoryId, state));
        }
        else
        {
            entity.Update(state);
        }
    }

    public async Task<IReadOnlyList<GitHubReviewerFeedbackState>> GetReviewerFeedbackAsync(
        int pullRequestNumber,
        CancellationToken cancellationToken) =>
        (await LoadWithAddedAsync(
            dbContext.Set<GitHubReviewerFeedbackStateEntity>(),
            state => state.RepositoryId == repositoryId && state.PullRequestNumber == pullRequestNumber,
            cancellationToken).ConfigureAwait(false))
            .OrderBy(state => state.ReviewerLogin)
            .ThenBy(state => state.ReviewerGitHubUserId)
            .Select(state => state.ToState())
            .ToArray();

    public async Task UpsertReviewerFeedbackAsync(
        GitHubReviewerFeedbackState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        var entity = dbContext.Set<GitHubReviewerFeedbackStateEntity>().Local.SingleOrDefault(item =>
            item.RepositoryId == repositoryId &&
            item.PullRequestNumber == state.PullRequestNumber &&
            item.ReviewerGitHubUserId == state.ReviewerGitHubUserId)
            ?? await dbContext.Set<GitHubReviewerFeedbackStateEntity>().SingleOrDefaultAsync(
                item => item.RepositoryId == repositoryId &&
                    item.PullRequestNumber == state.PullRequestNumber &&
                    item.ReviewerGitHubUserId == state.ReviewerGitHubUserId,
                cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            dbContext.Add(GitHubReviewerFeedbackStateEntity.Create(repositoryId, state));
        }
        else
        {
            entity.Update(state);
        }
    }

    public async Task<IReadOnlyList<GitHubCheckFailureState>> GetCheckFailuresAsync(
        int pullRequestNumber,
        CancellationToken cancellationToken) =>
        (await LoadWithAddedAsync(
            dbContext.Set<GitHubCheckFailureStateEntity>(),
            state => state.RepositoryId == repositoryId && state.PullRequestNumber == pullRequestNumber,
            cancellationToken).ConfigureAwait(false))
            .OrderBy(state => state.HeadSha)
            .ThenBy(state => state.CheckKey)
            .Select(state => state.ToState())
            .ToArray();

    public async Task UpsertCheckFailureAsync(
        GitHubCheckFailureState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        var entity = dbContext.Set<GitHubCheckFailureStateEntity>().Local.SingleOrDefault(item =>
            item.RepositoryId == repositoryId &&
            item.PullRequestNumber == state.PullRequestNumber &&
            item.HeadSha == state.HeadSha &&
            item.CheckKey == state.CheckKey)
            ?? await dbContext.Set<GitHubCheckFailureStateEntity>().SingleOrDefaultAsync(
                item => item.RepositoryId == repositoryId &&
                    item.PullRequestNumber == state.PullRequestNumber &&
                    item.HeadSha == state.HeadSha &&
                    item.CheckKey == state.CheckKey,
                cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            dbContext.Add(GitHubCheckFailureStateEntity.Create(repositoryId, state));
        }
        else
        {
            entity.Update(state);
        }
    }

    public async Task<IReadOnlyList<GitHubResponseState>> GetResponsesAsync(
        GitHubSubjectType subjectType,
        int subjectNumber,
        CancellationToken cancellationToken) =>
        (await LoadWithAddedAsync(
            dbContext.Set<GitHubResponseStateEntity>(),
            state => state.RepositoryId == repositoryId &&
                state.SubjectType == subjectType &&
                state.SubjectNumber == subjectNumber,
            cancellationToken).ConfigureAwait(false))
            .OrderBy(state => state.GitHubAssigneeId)
            .Select(state => state.ToState())
            .ToArray();

    public async Task UpsertResponseAsync(GitHubResponseState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        var entity = dbContext.Set<GitHubResponseStateEntity>().Local.SingleOrDefault(item =>
            item.RepositoryId == repositoryId &&
            item.SubjectType == state.SubjectType &&
            item.SubjectNumber == state.SubjectNumber &&
            item.GitHubAssigneeId == state.GitHubAssigneeId)
            ?? await dbContext.Set<GitHubResponseStateEntity>().SingleOrDefaultAsync(
                item => item.RepositoryId == repositoryId &&
                    item.SubjectType == state.SubjectType &&
                    item.SubjectNumber == state.SubjectNumber &&
                    item.GitHubAssigneeId == state.GitHubAssigneeId,
                cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            dbContext.Add(GitHubResponseStateEntity.Create(repositoryId, state));
        }
        else
        {
            entity.Update(state);
        }
    }

    private static async Task<IReadOnlyList<TEntity>> LoadWithAddedAsync<TEntity>(
        DbSet<TEntity> set,
        System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var persisted = await set.Where(predicate).ToListAsync(cancellationToken).ConfigureAwait(false);
        var added = set.Local.Where(entity => !persisted.Contains(entity)).AsQueryable().Where(predicate).ToArray();
        return [.. persisted, .. added];
    }
}

internal sealed class GitHubPullRequestStateEntity
{
    public Guid Id { get; private set; }
    public Guid RepositoryId { get; private set; }
    public int PullRequestNumber { get; private set; }
    public long AuthorGitHubUserId { get; private set; }
    public string AuthorLogin { get; private set; } = string.Empty;
    public string HeadSha { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Url { get; private set; } = string.Empty;
    public bool IsDraft { get; private set; }
    public bool IsOpen { get; private set; }
    public int? ApprovalCount { get; private set; }
    public bool? HasChangesRequested { get; private set; }
    public GitHubCheckState CheckState { get; private set; }
    public bool? IsMergeable { get; private set; }
    public bool? HasConflicts { get; private set; }
    public DateTimeOffset? ReadinessCheckedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static GitHubPullRequestStateEntity Create(Guid repositoryId, GitHubPullRequestState state) =>
        new GitHubPullRequestStateEntity { Id = Guid.NewGuid(), RepositoryId = repositoryId }.Apply(state);

    public void Update(GitHubPullRequestState state) => Apply(state);

    public GitHubPullRequestState ToState() => new(
        PullRequestNumber,
        AuthorGitHubUserId,
        AuthorLogin,
        HeadSha,
        Title,
        Url,
        IsDraft,
        UpdatedAt,
        IsOpen,
        ApprovalCount,
        HasChangesRequested,
        CheckState,
        IsMergeable,
        HasConflicts,
        ReadinessCheckedAt);

    private GitHubPullRequestStateEntity Apply(GitHubPullRequestState state)
    {
        PullRequestNumber = state.PullRequestNumber;
        AuthorGitHubUserId = state.AuthorGitHubUserId;
        AuthorLogin = state.AuthorLogin;
        HeadSha = state.HeadSha;
        Title = state.Title;
        Url = state.Url;
        IsDraft = state.IsDraft;
        IsOpen = state.IsOpen;
        ApprovalCount = state.ApprovalCount;
        HasChangesRequested = state.HasChangesRequested;
        CheckState = state.CheckState;
        IsMergeable = state.IsMergeable;
        HasConflicts = state.HasConflicts;
        ReadinessCheckedAt = state.ReadinessCheckedAt;
        UpdatedAt = state.UpdatedAt;
        return this;
    }
}

internal sealed class GitHubReviewRequestStateEntity
{
    public Guid Id { get; private set; }
    public Guid RepositoryId { get; private set; }
    public int PullRequestNumber { get; private set; }
    public ActionAssigneeType AssigneeType { get; private set; }
    public long GitHubAssigneeId { get; private set; }
    public string AssigneeLogin { get; private set; } = string.Empty;
    public bool IsRequested { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static GitHubReviewRequestStateEntity Create(Guid repositoryId, GitHubReviewRequestState state) =>
        new GitHubReviewRequestStateEntity { Id = Guid.NewGuid(), RepositoryId = repositoryId }.Apply(state);

    public void Update(GitHubReviewRequestState state) => Apply(state);

    public GitHubReviewRequestState ToState() => new(
        PullRequestNumber,
        AssigneeType,
        GitHubAssigneeId,
        AssigneeLogin,
        IsRequested,
        UpdatedAt);

    private GitHubReviewRequestStateEntity Apply(GitHubReviewRequestState state)
    {
        PullRequestNumber = state.PullRequestNumber;
        AssigneeType = state.AssigneeType;
        GitHubAssigneeId = state.GitHubAssigneeId;
        AssigneeLogin = state.AssigneeLogin;
        IsRequested = state.IsRequested;
        UpdatedAt = state.UpdatedAt;
        return this;
    }
}

internal sealed class GitHubReviewerFeedbackStateEntity
{
    public Guid Id { get; private set; }
    public Guid RepositoryId { get; private set; }
    public int PullRequestNumber { get; private set; }
    public long ReviewerGitHubUserId { get; private set; }
    public string ReviewerLogin { get; private set; } = string.Empty;
    public long ReviewId { get; private set; }
    public bool HasOutstandingChanges { get; private set; }
    public int ApproximateUnresolvedCommentCount { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static GitHubReviewerFeedbackStateEntity Create(Guid repositoryId, GitHubReviewerFeedbackState state) =>
        new GitHubReviewerFeedbackStateEntity { Id = Guid.NewGuid(), RepositoryId = repositoryId }.Apply(state);

    public void Update(GitHubReviewerFeedbackState state) => Apply(state);

    public GitHubReviewerFeedbackState ToState() => new(
        PullRequestNumber,
        ReviewerGitHubUserId,
        ReviewerLogin,
        ReviewId,
        HasOutstandingChanges,
        ApproximateUnresolvedCommentCount,
        UpdatedAt);

    private GitHubReviewerFeedbackStateEntity Apply(GitHubReviewerFeedbackState state)
    {
        PullRequestNumber = state.PullRequestNumber;
        ReviewerGitHubUserId = state.ReviewerGitHubUserId;
        ReviewerLogin = state.ReviewerLogin;
        ReviewId = state.ReviewId;
        HasOutstandingChanges = state.HasOutstandingChanges;
        ApproximateUnresolvedCommentCount = state.ApproximateUnresolvedCommentCount;
        UpdatedAt = state.UpdatedAt;
        return this;
    }
}

internal sealed class GitHubCheckFailureStateEntity
{
    public Guid Id { get; private set; }
    public Guid RepositoryId { get; private set; }
    public int PullRequestNumber { get; private set; }
    public string HeadSha { get; private set; } = string.Empty;
    public string CheckKey { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Url { get; private set; }
    public bool IsFailing { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static GitHubCheckFailureStateEntity Create(Guid repositoryId, GitHubCheckFailureState state) =>
        new GitHubCheckFailureStateEntity { Id = Guid.NewGuid(), RepositoryId = repositoryId }.Apply(state);

    public void Update(GitHubCheckFailureState state) => Apply(state);

    public GitHubCheckFailureState ToState() => new(
        PullRequestNumber,
        HeadSha,
        CheckKey,
        Name,
        Url,
        IsFailing,
        UpdatedAt);

    private GitHubCheckFailureStateEntity Apply(GitHubCheckFailureState state)
    {
        PullRequestNumber = state.PullRequestNumber;
        HeadSha = state.HeadSha;
        CheckKey = state.CheckKey;
        Name = state.Name;
        Url = state.Url;
        IsFailing = state.IsFailing;
        UpdatedAt = state.UpdatedAt;
        return this;
    }
}

internal sealed class GitHubResponseStateEntity
{
    public Guid Id { get; private set; }
    public Guid RepositoryId { get; private set; }
    public GitHubSubjectType SubjectType { get; private set; }
    public int SubjectNumber { get; private set; }
    public long GitHubAssigneeId { get; private set; }
    public bool IsPending { get; private set; }
    public int TriggerCount { get; private set; }
    public long LastTriggerCommentId { get; private set; }
    public DateTimeOffset LastTriggeredAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static GitHubResponseStateEntity Create(Guid repositoryId, GitHubResponseState state) =>
        new GitHubResponseStateEntity { Id = Guid.NewGuid(), RepositoryId = repositoryId }.Apply(state);

    public void Update(GitHubResponseState state) => Apply(state);

    public GitHubResponseState ToState() => new(
        SubjectType,
        SubjectNumber,
        GitHubAssigneeId,
        IsPending,
        TriggerCount,
        LastTriggerCommentId,
        LastTriggeredAt,
        UpdatedAt);

    private GitHubResponseStateEntity Apply(GitHubResponseState state)
    {
        SubjectType = state.SubjectType;
        SubjectNumber = state.SubjectNumber;
        GitHubAssigneeId = state.GitHubAssigneeId;
        IsPending = state.IsPending;
        TriggerCount = state.TriggerCount;
        LastTriggerCommentId = state.LastTriggerCommentId;
        LastTriggeredAt = state.LastTriggeredAt;
        UpdatedAt = state.UpdatedAt;
        return this;
    }
}