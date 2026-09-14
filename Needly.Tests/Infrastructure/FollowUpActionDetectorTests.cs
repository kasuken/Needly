using Microsoft.EntityFrameworkCore;
using Needly.Domain;
using Needly.Infrastructure;
using Xunit;
using static Needly.Tests.Infrastructure.DetectorTestSupport;

namespace Needly.Tests.Infrastructure;

public sealed class FollowUpActionDetectorTests
{
    [Fact]
    public async Task OutstandingChangesResolved_ResolvesFollowUpActionForReviewer()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(database.Context, handler, "pull_request", "opened", PullRequestPayload(false, BaseTime.AddMinutes(1), []), BaseTime.AddMinutes(1));
        await HandleAsync(
            database.Context,
            handler,
            "pull_request_review",
            "submitted",
            PullRequestReviewPayload(OtherReviewerGitHubId, "other-reviewer", "changes_requested", BaseTime.AddMinutes(2)),
            BaseTime.AddMinutes(2));
        await SeedFollowUpActionAsync(database.Context, seed);

        await HandleAsync(
            database.Context,
            handler,
            "pull_request_review",
            "submitted",
            PullRequestReviewPayload(OtherReviewerGitHubId, "other-reviewer", "approved", BaseTime.AddMinutes(3), reviewId: 9002),
            BaseTime.AddMinutes(3));

        await using var verification = database.CreateContext();
        var followUp = await verification.Actions.AsNoTracking().SingleAsync(action => action.Type == ActionType.FollowUp);
        Assert.Equal(ActionState.Done, followUp.State);
    }

    [Fact]
    public async Task OutstandingChangesStillPending_LeavesFollowUpActionOpen()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(database.Context, handler, "pull_request", "opened", PullRequestPayload(false, BaseTime.AddMinutes(1), []), BaseTime.AddMinutes(1));
        await HandleAsync(
            database.Context,
            handler,
            "pull_request_review",
            "submitted",
            PullRequestReviewPayload(OtherReviewerGitHubId, "other-reviewer", "changes_requested", BaseTime.AddMinutes(2)),
            BaseTime.AddMinutes(2));
        await SeedFollowUpActionAsync(database.Context, seed);

        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "synchronize",
            PullRequestPayload(false, BaseTime.AddMinutes(3), [], headSha: "head-2"),
            BaseTime.AddMinutes(3));

        await using var verification = database.CreateContext();
        var followUp = await verification.Actions.AsNoTracking().SingleAsync(action => action.Type == ActionType.FollowUp);
        Assert.Equal(ActionState.Open, followUp.State);
    }

    [Fact]
    public async Task PullRequestClosed_ResolvesFollowUpActionRegardlessOfFeedbackState()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(database.Context, handler, "pull_request", "opened", PullRequestPayload(false, BaseTime.AddMinutes(1), []), BaseTime.AddMinutes(1));
        await HandleAsync(
            database.Context,
            handler,
            "pull_request_review",
            "submitted",
            PullRequestReviewPayload(OtherReviewerGitHubId, "other-reviewer", "changes_requested", BaseTime.AddMinutes(2)),
            BaseTime.AddMinutes(2));
        await SeedFollowUpActionAsync(database.Context, seed);

        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "closed",
            PullRequestPayload(false, BaseTime.AddMinutes(3), [], merged: true),
            BaseTime.AddMinutes(3));

        await using var verification = database.CreateContext();
        var followUp = await verification.Actions.AsNoTracking().SingleAsync(action => action.Type == ActionType.FollowUp);
        Assert.Equal(ActionState.Done, followUp.State);
    }

    [Fact]
    public async Task ResolvingTwice_IsIdempotentAndDoesNotThrow()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(database.Context, handler, "pull_request", "opened", PullRequestPayload(false, BaseTime.AddMinutes(1), []), BaseTime.AddMinutes(1));
        await HandleAsync(
            database.Context,
            handler,
            "pull_request_review",
            "submitted",
            PullRequestReviewPayload(OtherReviewerGitHubId, "other-reviewer", "changes_requested", BaseTime.AddMinutes(2)),
            BaseTime.AddMinutes(2));
        await SeedFollowUpActionAsync(database.Context, seed);
        await HandleAsync(
            database.Context,
            handler,
            "pull_request_review",
            "submitted",
            PullRequestReviewPayload(OtherReviewerGitHubId, "other-reviewer", "approved", BaseTime.AddMinutes(3), reviewId: 9002),
            BaseTime.AddMinutes(3));

        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "closed",
            PullRequestPayload(false, BaseTime.AddMinutes(4), [], merged: true),
            BaseTime.AddMinutes(4));

        await using var verification = database.CreateContext();
        Assert.Equal(1, await verification.Actions.CountAsync(action => action.Type == ActionType.FollowUp));
        var followUp = await verification.Actions.AsNoTracking().SingleAsync(action => action.Type == ActionType.FollowUp);
        Assert.Equal(ActionState.Done, followUp.State);
    }

    private static async Task SeedFollowUpActionAsync(NeedlyDbContext context, SeedResult seed)
    {
        var action = NeedlyAction.CreateForUser(
            Guid.NewGuid(),
            ActionType.FollowUp,
            seed.Repository,
            seed.OtherReviewer,
            GitHubSubjectType.PullRequest,
            42,
            "https://github.com/octocat/needly/pull/42",
            "Follow up on PR #42",
            "Requested changes have had no new activity.",
            "Requested changes have gone unaddressed past the configured follow-up threshold.",
            BaseTime);
        context.Actions.Add(action);
        await context.SaveChangesAsync();
    }
}
