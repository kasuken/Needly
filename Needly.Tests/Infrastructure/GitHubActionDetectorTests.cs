using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Needly.Application.GitHub;
using Needly.Domain;
using Needly.Infrastructure;
using Needly.Infrastructure.GitHub;
using Xunit;

namespace Needly.Tests.Infrastructure;

public sealed class GitHubActionDetectorTests
{
    private const long GitHubInstallationId = 501;
    private const long GitHubRepositoryId = 101;
    private const long AuthorGitHubId = 201;
    private const long ReviewerGitHubId = 202;
    private const long OtherReviewerGitHubId = 203;
    private const long TeamGitHubId = 301;
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 3, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MergeReady_AuthoritativeHappyState_CreatesMergeActionForAuthorOnly()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var lookup = new FakePullRequestLookup
        {
            Result = new GitHubPullRequestReadiness(
                42,
                AuthorGitHubId,
                "author",
                "head-1",
                "Improve action detection",
                "https://github.com/octocat/needly/pull/42",
                IsOpen: true,
                IsDraft: false,
                ApprovalCount: 1,
                HasChangesRequested: false,
                GitHubCheckState.Passing,
                IsMergeable: true,
                HasConflicts: false,
                BaseTime.AddMinutes(1))
        };
        var handler = CreateHandler(database, lookup);

        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "opened",
            PullRequestPayload(false, BaseTime.AddMinutes(1), []),
            BaseTime.AddMinutes(1));

        await using var verification = database.CreateContext();
        var action = await verification.Actions.AsNoTracking().SingleAsync();
        Assert.Equal(ActionType.Merge, action.Type);
        Assert.Equal(seed.Author.Id, action.AssigneeId);
        Assert.Equal(ActionState.Open, action.State);
        Assert.Equal(1, lookup.CallCount);
    }

    [Fact]
    public async Task MergeReady_ConfiguredTwoApprovals_RejectsSingleApproval()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var lookup = new FakePullRequestLookup { Result = ReadyPullRequest(BaseTime.AddMinutes(1)) };
        var handler = CreateHandler(database, lookup, requiredApprovals: 2);

        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "opened",
            PullRequestPayload(false, BaseTime.AddMinutes(1), []),
            BaseTime.AddMinutes(1));

        await using var verification = database.CreateContext();
        Assert.Empty(await verification.Actions.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("approvals")]
    [InlineData("changes-requested")]
    [InlineData("checks")]
    [InlineData("mergeability")]
    [InlineData("conflicts")]
    [InlineData("closed")]
    public async Task MergeReady_WhenAnyConditionRegresses_ResolvesExistingAction(string regression)
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var lookup = new FakePullRequestLookup { Result = ReadyPullRequest(BaseTime.AddMinutes(1)) };
        var handler = CreateHandler(database, lookup);
        await HandleAsync(database.Context, handler, "pull_request", "opened", PullRequestPayload(false, BaseTime.AddMinutes(1), []), BaseTime.AddMinutes(1));
        lookup.Result = regression switch
        {
            "draft" => ReadyPullRequest(BaseTime.AddMinutes(2)) with { IsDraft = true },
            "approvals" => ReadyPullRequest(BaseTime.AddMinutes(2)) with { ApprovalCount = 0 },
            "changes-requested" => ReadyPullRequest(BaseTime.AddMinutes(2)) with { HasChangesRequested = true },
            "checks" => ReadyPullRequest(BaseTime.AddMinutes(2)) with { CheckState = GitHubCheckState.Failing },
            "mergeability" => ReadyPullRequest(BaseTime.AddMinutes(2)) with { IsMergeable = null },
            "conflicts" => ReadyPullRequest(BaseTime.AddMinutes(2)) with { HasConflicts = true },
            "closed" => ReadyPullRequest(BaseTime.AddMinutes(2)) with { IsOpen = false },
            _ => throw new InvalidOperationException($"Unknown regression '{regression}'.")
        };

        await HandleAsync(database.Context, handler, "pull_request_review", "submitted", PullRequestReviewPayload(ReviewerGitHubId, "reviewer", "approved", BaseTime.AddMinutes(2)), BaseTime.AddMinutes(2));

        await using var verification = database.CreateContext();
        Assert.Equal(ActionState.Done, (await verification.Actions.AsNoTracking().SingleAsync()).State);
    }

    [Fact]
    public async Task MergeReady_AfterRegressionRecovers_ReactivatesWithoutDuplicate()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var lookup = new FakePullRequestLookup { Result = ReadyPullRequest(BaseTime.AddMinutes(1)) };
        var handler = CreateHandler(database, lookup);
        await HandleAsync(database.Context, handler, "pull_request", "opened", PullRequestPayload(false, BaseTime.AddMinutes(1), []), BaseTime.AddMinutes(1));
        lookup.Result = ReadyPullRequest(BaseTime.AddMinutes(2)) with { CheckState = GitHubCheckState.Failing };
        await HandleAsync(database.Context, handler, "check_run", "completed", CheckRunPayload("Build", "failure", "head-1", BaseTime.AddMinutes(2)), BaseTime.AddMinutes(2));
        lookup.Result = ReadyPullRequest(BaseTime.AddMinutes(3));
        await HandleAsync(database.Context, handler, "check_run", "completed", CheckRunPayload("Build", "success", "head-1", BaseTime.AddMinutes(3)), BaseTime.AddMinutes(3));

        await using var verification = database.CreateContext();
        var action = await verification.Actions.AsNoTracking().SingleAsync(item => item.Type == ActionType.Merge);
        Assert.Equal(ActionState.Open, action.State);
        Assert.Equal(BaseTime.AddMinutes(3), action.WaitingSince);
        Assert.Equal(1, await verification.Actions.CountAsync(item => item.Type == ActionType.Merge));
    }

    [Fact]
    public async Task MergeReady_PullRequestClose_ResolvesWithoutApiLookup()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var lookup = new FakePullRequestLookup { Result = ReadyPullRequest(BaseTime.AddMinutes(1)) };
        var handler = CreateHandler(database, lookup);
        await HandleAsync(database.Context, handler, "pull_request", "opened", PullRequestPayload(false, BaseTime.AddMinutes(1), []), BaseTime.AddMinutes(1));

        await HandleAsync(database.Context, handler, "pull_request", "closed", PullRequestPayload(false, BaseTime.AddMinutes(2), [], merged: true), BaseTime.AddMinutes(2));

        await using var verification = database.CreateContext();
        Assert.Equal(ActionState.Done, (await verification.Actions.AsNoTracking().SingleAsync()).State);
        Assert.Equal(1, lookup.CallCount);
    }

    [Fact]
    public async Task MergeReady_InsufficientApiState_ResolvesExistingAction()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var lookup = new FakePullRequestLookup { Result = ReadyPullRequest(BaseTime.AddMinutes(1)) };
        var handler = CreateHandler(database, lookup);
        await HandleAsync(database.Context, handler, "pull_request", "opened", PullRequestPayload(false, BaseTime.AddMinutes(1), []), BaseTime.AddMinutes(1));
        lookup.Result = null;

        await HandleAsync(database.Context, handler, "check_run", "completed", CheckRunPayload("Build", "success", "head-1", BaseTime.AddMinutes(2)), BaseTime.AddMinutes(2));

        await using var verification = database.CreateContext();
        Assert.Equal(ActionState.Done, (await verification.Actions.AsNoTracking().SingleAsync()).State);
    }

    [Fact]
    public async Task MergeReady_ApiFailure_RollsBackEventWithoutChangingExistingAction()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var lookup = new FakePullRequestLookup { Result = ReadyPullRequest(BaseTime.AddMinutes(1)) };
        var handler = CreateHandler(database, lookup);
        await HandleAsync(database.Context, handler, "pull_request", "opened", PullRequestPayload(false, BaseTime.AddMinutes(1), []), BaseTime.AddMinutes(1));
        lookup.Exception = new HttpRequestException("GitHub unavailable.");
        var pending = CreateStoredEvent(
            database.Context,
            "check_run",
            "completed",
            CheckRunPayload("Build", "failure", "head-1", BaseTime.AddMinutes(2)),
            BaseTime.AddMinutes(2));
        await pending.PersistAsync();

        await Assert.ThrowsAsync<HttpRequestException>(() => handler.HandleAsync(pending.StoredEvent, CancellationToken.None));

        await using var verification = database.CreateContext();
        Assert.Equal(ActionState.Open, (await verification.Actions.AsNoTracking().SingleAsync()).State);
        Assert.DoesNotContain(
            await verification.ActionEventReceipts.AsNoTracking().ToListAsync(),
            receipt => receipt.EventId == pending.StoredEvent.EventId);
    }

    [Fact]
    public async Task Respond_ExactMention_IsCaseInsensitiveWithoutPrefixMatches()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var handler = CreateHandler(database);

        await HandleAsync(
            database.Context,
            handler,
            "issue_comment",
            "created",
            IssueCommentPayload(
                "@UsEr-202 please respond; @user-202-extra is a different token.",
                AuthorGitHubId,
                "author",
                BaseTime.AddMinutes(1)),
            BaseTime.AddMinutes(1));

        await using var verification = database.CreateContext();
        var action = await verification.Actions.AsNoTracking().SingleAsync();
        Assert.Equal(ActionType.Respond, action.Type);
        Assert.Equal(seed.Reviewer.Id, action.AssigneeId);
        Assert.Equal(1, await verification.Actions.CountAsync());
    }

    [Fact]
    public async Task Respond_PullRequestReviewCommentByAnotherUser_CreatesAuthorAction()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var handler = CreateHandler(database);

        await HandleAsync(
            database.Context,
            handler,
            "pull_request_review_comment",
            "created",
            PullRequestReviewCommentPayload(
                "created",
                BaseTime.AddMinutes(1),
                "Could you clarify this?",
                ReviewerGitHubId,
                "reviewer"),
            BaseTime.AddMinutes(1));

        await using var verification = database.CreateContext();
        var action = await verification.Actions.AsNoTracking().SingleAsync();
        Assert.Equal(ActionType.Respond, action.Type);
        Assert.Equal(GitHubSubjectType.PullRequest, action.SubjectType);
        Assert.Equal(seed.Author.Id, action.AssigneeId);
    }

    [Fact]
    public async Task Respond_AuthorAlsoMentioned_IsCountedOnce()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var handler = CreateHandler(database);

        await HandleAsync(
            database.Context,
            handler,
            "issue_comment",
            "created",
            IssueCommentPayload("@AUTHOR can you answer?", ReviewerGitHubId, "reviewer", BaseTime.AddMinutes(1)),
            BaseTime.AddMinutes(1));

        await using var verification = database.CreateContext();
        var action = await verification.Actions.AsNoTracking().SingleAsync();
        Assert.Equal(seed.Author.Id, action.AssigneeId);
        Assert.Contains("1 comment need", action.Context, StringComparison.Ordinal);
        Assert.Equal(1, await verification.Actions.CountAsync());
    }

    [Fact]
    public async Task Respond_OwnAndBotAuthoredComments_DoNotCreateActions()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(
            database.Context,
            handler,
            "issue_comment",
            "created",
            IssueCommentPayload("An author update.", AuthorGitHubId, "author", BaseTime.AddMinutes(1)),
            BaseTime.AddMinutes(1));
        await HandleAsync(
            database.Context,
            handler,
            "issue_comment",
            "created",
            IssueCommentPayload("@user-202 automated update.", 998, "automation", BaseTime.AddMinutes(2), userType: "Bot"),
            BaseTime.AddMinutes(2));
        await HandleAsync(
            database.Context,
            handler,
            "issue_comment",
            "created",
            IssueCommentPayload("@user-202 automated update.", 999, "automation[bot]", BaseTime.AddMinutes(3)),
            BaseTime.AddMinutes(3));

        await using var verification = database.CreateContext();
        Assert.Empty(await verification.Actions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Respond_OwnReplyResolvesOnlyWhenAfterTrigger()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(
            database.Context,
            handler,
            "issue_comment",
            "created",
            IssueCommentPayload("Question for the author.", ReviewerGitHubId, "reviewer", BaseTime.AddMinutes(1), commentId: 7001),
            BaseTime.AddMinutes(1));
        await HandleAsync(
            database.Context,
            handler,
            "issue_comment",
            "created",
            IssueCommentPayload("Simultaneous author comment.", AuthorGitHubId, "author", BaseTime.AddMinutes(1), commentId: 7002),
            BaseTime.AddMinutes(1));

        await using (var equalVerification = database.CreateContext())
        {
            Assert.Equal(ActionState.Open, (await equalVerification.Actions.AsNoTracking().SingleAsync()).State);
        }

        await HandleAsync(
            database.Context,
            handler,
            "issue_comment",
            "created",
            IssueCommentPayload("Later author reply.", AuthorGitHubId, "author", BaseTime.AddMinutes(2), commentId: 7003),
            BaseTime.AddMinutes(2));

        await using var verification = database.CreateContext();
        Assert.Equal(ActionState.Done, (await verification.Actions.AsNoTracking().SingleAsync()).State);
    }

    [Fact]
    public async Task Respond_MultipleComments_UpdateOneActionWithDurableCount()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(
            database.Context,
            handler,
            "issue_comment",
            "created",
            IssueCommentPayload("First question.", ReviewerGitHubId, "reviewer", BaseTime.AddMinutes(1), commentId: 7101),
            BaseTime.AddMinutes(1));
        await HandleAsync(
            database.Context,
            handler,
            "issue_comment",
            "created",
            IssueCommentPayload("Second question.", OtherReviewerGitHubId, "other-reviewer", BaseTime.AddMinutes(2), commentId: 7102),
            BaseTime.AddMinutes(2));

        await using var verification = database.CreateContext();
        var action = await verification.Actions.AsNoTracking().SingleAsync();
        Assert.Contains("2 comments need", action.Context, StringComparison.Ordinal);
        Assert.Contains("Second question", action.Context, StringComparison.Ordinal);
        Assert.Equal(BaseTime.AddMinutes(2), action.LastActivityAt);
        Assert.Equal(1, await verification.Actions.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Respond_SubjectClose_ResolvesIssueAndPullRequestActions(bool isPullRequest)
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(
            database.Context,
            handler,
            "issue_comment",
            "created",
            IssueCommentPayload(
                "Question for the author.",
                ReviewerGitHubId,
                "reviewer",
                BaseTime.AddMinutes(1),
                isPullRequest),
            BaseTime.AddMinutes(1));

        if (isPullRequest)
        {
            await HandleAsync(
                database.Context,
                handler,
                "pull_request",
                "closed",
                PullRequestPayload(false, BaseTime.AddMinutes(2), [], merged: true),
                BaseTime.AddMinutes(2));
        }
        else
        {
            await HandleAsync(
                database.Context,
                handler,
                "issues",
                "closed",
                IssuePayload(BaseTime.AddMinutes(2)),
                BaseTime.AddMinutes(2));
        }

        await using var verification = database.CreateContext();
        Assert.Equal(ActionState.Done, (await verification.Actions.AsNoTracking().SingleAsync()).State);
    }

    [Fact]
    public async Task ReviewRequested_DraftThenReady_CreatesUserAndTeamActionsVisibleToTeamMember()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var handler = CreateHandler(database);

        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "review_requested",
            PullRequestPayload(
                draft: true,
                updatedAt: BaseTime.AddMinutes(1),
                requestedReviewerIds: [ReviewerGitHubId]),
            BaseTime.AddMinutes(1));
        Assert.Empty(await database.Context.Actions.AsNoTracking().ToListAsync());

        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "ready_for_review",
            PullRequestPayload(
                draft: false,
                updatedAt: BaseTime.AddMinutes(2),
                requestedReviewerIds: [ReviewerGitHubId],
                requestedTeam: true),
            BaseTime.AddMinutes(2));

        await using var verification = database.CreateContext();
        var actions = await verification.Actions.AsNoTracking().OrderBy(action => action.AssigneeType).ToListAsync();
        Assert.Equal(2, actions.Count);
        Assert.All(actions, action => Assert.Equal(ActionState.Open, action.State));
        Assert.All(actions, action => Assert.Equal(BaseTime.AddMinutes(2), action.WaitingSince));
        Assert.Contains(actions, action => action.AssigneeType == ActionAssigneeType.User && action.AssigneeId == seed.Reviewer.Id);
        Assert.Contains(actions, action => action.AssigneeType == ActionAssigneeType.Team && action.AssigneeId == seed.Team.Id);

        var visible = await new InboxVisibilityService(
            verification,
            new FixedTimeProvider(BaseTime.AddMinutes(2)))
            .GetVisibleAsync(seed.NeedlyUser.Id, CancellationToken.None);
        Assert.Equal(2, visible.Count);
    }

    [Fact]
    public async Task PullRequestReview_SubmittedByOneReviewer_LeavesOtherReviewerOpenThenCloseResolvesIt()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "ready_for_review",
            PullRequestPayload(
                draft: false,
                updatedAt: BaseTime.AddMinutes(1),
                requestedReviewerIds: [ReviewerGitHubId, OtherReviewerGitHubId]),
            BaseTime.AddMinutes(1));

        await HandleAsync(
            database.Context,
            handler,
            "pull_request_review",
            "submitted",
            PullRequestReviewPayload(ReviewerGitHubId, "reviewer", "approved", BaseTime.AddMinutes(2)),
            BaseTime.AddMinutes(2));

        await using (var verification = database.CreateContext())
        {
            var actions = await verification.Actions.AsNoTracking().OrderBy(action => action.AssigneeId).ToListAsync();
            Assert.Equal(ActionState.Done, actions.Single(action => action.AssigneeId == seed.Reviewer.Id).State);
            Assert.Equal(ActionState.Open, actions.Single(action => action.AssigneeId == seed.OtherReviewer.Id).State);
        }

        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "closed",
            PullRequestPayload(false, BaseTime.AddMinutes(3), [ReviewerGitHubId, OtherReviewerGitHubId]),
            BaseTime.AddMinutes(3));

        await using var closedVerification = database.CreateContext();
        Assert.All(
            await closedVerification.Actions.AsNoTracking().ToListAsync(),
            action => Assert.Equal(ActionState.Done, action.State));
    }

    [Fact]
    public async Task ReviewRequest_RemovedThenRequestedAgain_ReactivatesDoneActionAtRequestTime()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "review_requested",
            PullRequestPayload(false, BaseTime.AddMinutes(1), [ReviewerGitHubId]),
            BaseTime.AddMinutes(1));
        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "review_request_removed",
            PullRequestPayload(false, BaseTime.AddMinutes(2), [ReviewerGitHubId]),
            BaseTime.AddMinutes(2));

        await using (var removedVerification = database.CreateContext())
        {
            Assert.Equal(ActionState.Done, (await removedVerification.Actions.AsNoTracking().SingleAsync()).State);
        }

        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "review_requested",
            PullRequestPayload(false, BaseTime.AddMinutes(3), [ReviewerGitHubId]),
            BaseTime.AddMinutes(3));

        await using var verification = database.CreateContext();
        var action = await verification.Actions.AsNoTracking().SingleAsync();
        Assert.Equal(ActionState.Open, action.State);
        Assert.Equal(BaseTime.AddMinutes(3), action.WaitingSince);
        Assert.Equal(BaseTime.AddMinutes(3), action.LastActivityAt);
        Assert.Equal(1, await verification.Actions.CountAsync());
    }

    [Fact]
    public async Task TeamReviewRequested_WhenActiveTeamMemberSubmits_ResolvesOnlyTeamAction()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "review_requested",
            PullRequestPayload(false, BaseTime.AddMinutes(1), [], requestedTeam: true),
            BaseTime.AddMinutes(1));

        await using (var requestedVerification = database.CreateContext())
        {
            var action = await requestedVerification.Actions.AsNoTracking().SingleAsync();
            Assert.Equal(ActionAssigneeType.Team, action.AssigneeType);
            Assert.Equal(ActionState.Open, action.State);
        }

        await HandleAsync(
            database.Context,
            handler,
            "pull_request_review",
            "submitted",
            PullRequestReviewPayload(ReviewerGitHubId, "reviewer", "approved", BaseTime.AddMinutes(2)),
            BaseTime.AddMinutes(2));

        await using var verification = database.CreateContext();
        var resolved = await verification.Actions.AsNoTracking().SingleAsync();
        Assert.Equal(ActionState.Done, resolved.State);
        Assert.Equal(1, await verification.Actions.CountAsync());
    }

    [Fact]
    public async Task ResolveFeedback_MultipleReviewers_AggregatesAndClearsEachReviewerIndependently()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(
            database.Context,
            handler,
            "pull_request_review",
            "submitted",
            PullRequestReviewPayload(ReviewerGitHubId, "reviewer", "changes_requested", BaseTime.AddMinutes(1), 9001),
            BaseTime.AddMinutes(1));
        await HandleAsync(
            database.Context,
            handler,
            "pull_request_review",
            "submitted",
            PullRequestReviewPayload(OtherReviewerGitHubId, "other-reviewer", "changes_requested", BaseTime.AddMinutes(2), 9002),
            BaseTime.AddMinutes(2));

        await using (var aggregateVerification = database.CreateContext())
        {
            var action = await aggregateVerification.Actions.AsNoTracking().SingleAsync();
            Assert.Equal(ActionType.Resolve, action.Type);
            Assert.Contains("@reviewer", action.Context, StringComparison.Ordinal);
            Assert.Contains("@other-reviewer", action.Context, StringComparison.Ordinal);
            Assert.Contains("2 reviewers", action.Context, StringComparison.Ordinal);
        }

        await HandleAsync(
            database.Context,
            handler,
            "pull_request_review",
            "submitted",
            PullRequestReviewPayload(ReviewerGitHubId, "reviewer", "approved", BaseTime.AddMinutes(3), 9003),
            BaseTime.AddMinutes(3));

        await using (var partialVerification = database.CreateContext())
        {
            var action = await partialVerification.Actions.AsNoTracking().SingleAsync();
            Assert.Equal(ActionState.Open, action.State);
            Assert.DoesNotContain("@reviewer,", action.Context, StringComparison.Ordinal);
            Assert.Contains("@other-reviewer", action.Context, StringComparison.Ordinal);
            Assert.Contains("1 reviewer", action.Context, StringComparison.Ordinal);
        }

        await HandleAsync(
            database.Context,
            handler,
            "pull_request_review",
            "dismissed",
            PullRequestReviewPayload(OtherReviewerGitHubId, "other-reviewer", "dismissed", BaseTime.AddMinutes(4), 9002),
            BaseTime.AddMinutes(4));

        await using var verification = database.CreateContext();
        var resolved = await verification.Actions.AsNoTracking().SingleAsync();
        Assert.Equal(ActionState.Done, resolved.State);
        Assert.Equal(1, await verification.Actions.CountAsync());
    }

    [Fact]
    public async Task ResolveFeedback_ReviewCommentsAndNewCommit_UpdateOneResolveActionWithApproximateCount()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(
            database.Context,
            handler,
            "pull_request_review",
            "submitted",
            PullRequestReviewPayload(ReviewerGitHubId, "reviewer", "changes_requested", BaseTime.AddMinutes(1), 9001),
            BaseTime.AddMinutes(1));
        await HandleAsync(
            database.Context,
            handler,
            "pull_request_review_comment",
            "created",
            PullRequestReviewCommentPayload("created", BaseTime.AddMinutes(2)),
            BaseTime.AddMinutes(2));

        await using (var commentVerification = database.CreateContext())
        {
            var action = await commentVerification.Actions.AsNoTracking().SingleAsync();
            Assert.Contains("approximately 1 unresolved review comment", action.Context, StringComparison.Ordinal);
        }

        await HandleAsync(
            database.Context,
            handler,
            "pull_request_review_comment",
            "deleted",
            PullRequestReviewCommentPayload("deleted", BaseTime.AddMinutes(3)),
            BaseTime.AddMinutes(3));
        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "synchronize",
            PullRequestPayload(false, BaseTime.AddMinutes(4), [], headSha: "head-2"),
            BaseTime.AddMinutes(4));

        await using var verification = database.CreateContext();
        var updated = await verification.Actions.AsNoTracking().SingleAsync();
        Assert.Equal(ActionType.Resolve, updated.Type);
        Assert.Equal(ActionState.Open, updated.State);
        Assert.Equal(BaseTime.AddMinutes(4), updated.LastActivityAt);
        Assert.Contains("approximately 0 unresolved review comments", updated.Context, StringComparison.Ordinal);
        Assert.DoesNotContain(await verification.Actions.AsNoTracking().ToListAsync(), action => action.Type == ActionType.Review);
    }

    [Fact]
    public async Task CiFailures_MultipleKindsAggregateAndPartialGreenRemainsOpenUntilEveryCheckSucceeds()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "opened",
            PullRequestPayload(false, BaseTime.AddMinutes(1), []),
            BaseTime.AddMinutes(1));
        await HandleAsync(database.Context, handler, "check_suite", "completed", CheckSuitePayload("action_required", BaseTime.AddMinutes(2)), BaseTime.AddMinutes(2));
        await HandleAsync(database.Context, handler, "check_run", "completed", CheckRunPayload("Build", "failure", "head-1", BaseTime.AddMinutes(3)), BaseTime.AddMinutes(3));
        await HandleAsync(database.Context, handler, "workflow_run", "completed", WorkflowRunPayload("Tests", "timed_out", "head-1", BaseTime.AddMinutes(4)), BaseTime.AddMinutes(4));

        await using (var aggregateVerification = database.CreateContext())
        {
            var action = await aggregateVerification.Actions.AsNoTracking().SingleAsync();
            Assert.Equal(ActionType.Fix, action.Type);
            Assert.Contains("GitHub Actions", action.Context, StringComparison.Ordinal);
            Assert.Contains("Build", action.Context, StringComparison.Ordinal);
            Assert.Contains("Tests", action.Context, StringComparison.Ordinal);
            Assert.Contains("https://github.com/octocat/needly/actions/runs/6101/jobs/Build", action.Context, StringComparison.Ordinal);
            Assert.Contains("https://github.com/octocat/needly/actions/runs/6201", action.Context, StringComparison.Ordinal);
            Assert.Contains("3 CI checks are failing", action.Reason, StringComparison.Ordinal);
        }

        await HandleAsync(database.Context, handler, "check_run", "completed", CheckRunPayload("Build", "success", "head-1", BaseTime.AddMinutes(5)), BaseTime.AddMinutes(5));
        await using (var partialVerification = database.CreateContext())
        {
            var action = await partialVerification.Actions.AsNoTracking().SingleAsync();
            Assert.Equal(ActionState.Open, action.State);
            Assert.DoesNotContain("Build", action.Context, StringComparison.Ordinal);
            Assert.Contains("GitHub Actions", action.Context, StringComparison.Ordinal);
            Assert.Contains("Tests", action.Context, StringComparison.Ordinal);
        }

        await HandleAsync(database.Context, handler, "check_suite", "completed", CheckSuitePayload("success", BaseTime.AddMinutes(6)), BaseTime.AddMinutes(6));
        await HandleAsync(database.Context, handler, "workflow_run", "completed", WorkflowRunPayload("Tests", "success", "head-1", BaseTime.AddMinutes(7)), BaseTime.AddMinutes(7));

        await using var verification = database.CreateContext();
        var resolved = await verification.Actions.AsNoTracking().SingleAsync();
        Assert.Equal(ActionState.Done, resolved.State);
        Assert.Equal(1, await verification.Actions.CountAsync());
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("timed_out")]
    [InlineData("cancelled")]
    [InlineData("action_required")]
    [InlineData("stale")]
    public async Task CiFailure_EachFailureConclusion_CreatesFixAction(string conclusion)
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(database.Context, handler, "pull_request", "opened", PullRequestPayload(false, BaseTime.AddMinutes(1), []), BaseTime.AddMinutes(1));
        await HandleAsync(database.Context, handler, "check_run", "completed", CheckRunPayload("Build", conclusion, "head-1", BaseTime.AddMinutes(2)), BaseTime.AddMinutes(2));

        await using var verification = database.CreateContext();
        var action = await verification.Actions.AsNoTracking().SingleAsync();
        Assert.Equal(ActionType.Fix, action.Type);
        Assert.Equal(ActionState.Open, action.State);
        Assert.Contains("Build", action.Context, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CiFailure_NewHeadResolvesThenReactivatesWithoutDuplicateAndOldHeadGreenCannotResolveCurrentFailure()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(database.Context, handler, "pull_request", "opened", PullRequestPayload(false, BaseTime.AddMinutes(1), []), BaseTime.AddMinutes(1));
        await HandleAsync(database.Context, handler, "check_run", "completed", CheckRunPayload("Build", "failure", "head-1", BaseTime.AddMinutes(2)), BaseTime.AddMinutes(2));
        await HandleAsync(database.Context, handler, "pull_request", "synchronize", PullRequestPayload(false, BaseTime.AddMinutes(3), [], headSha: "head-2"), BaseTime.AddMinutes(3));

        await using (var synchronizedVerification = database.CreateContext())
        {
            Assert.Equal(ActionState.Done, (await synchronizedVerification.Actions.AsNoTracking().SingleAsync()).State);
        }

        await HandleAsync(database.Context, handler, "check_run", "completed", CheckRunPayload("Build", "cancelled", "head-2", BaseTime.AddMinutes(4)), BaseTime.AddMinutes(4));
        await HandleAsync(database.Context, handler, "check_run", "completed", CheckRunPayload("Build", "success", "head-1", BaseTime.AddMinutes(5)), BaseTime.AddMinutes(5));

        await using var verification = database.CreateContext();
        var action = await verification.Actions.AsNoTracking().SingleAsync();
        Assert.Equal(ActionState.Open, action.State);
        Assert.Equal(BaseTime.AddMinutes(4), action.WaitingSince);
        Assert.Contains("head-2", action.Context, StringComparison.Ordinal);
        Assert.Equal(1, await verification.Actions.CountAsync());
    }

    [Fact]
    public async Task PullRequestClosedAfterReviewFeedbackAndCiFailure_ResolvesEveryActionType()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(database.Context, handler, "pull_request", "review_requested", PullRequestPayload(false, BaseTime.AddMinutes(1), [ReviewerGitHubId]), BaseTime.AddMinutes(1));
        await HandleAsync(database.Context, handler, "pull_request_review", "submitted", PullRequestReviewPayload(OtherReviewerGitHubId, "other-reviewer", "changes_requested", BaseTime.AddMinutes(2)), BaseTime.AddMinutes(2));
        await HandleAsync(database.Context, handler, "check_run", "completed", CheckRunPayload("Build", "failure", "head-1", BaseTime.AddMinutes(3)), BaseTime.AddMinutes(3));
        await HandleAsync(database.Context, handler, "pull_request", "closed", PullRequestPayload(false, BaseTime.AddMinutes(4), [], merged: true), BaseTime.AddMinutes(4));

        await using var verification = database.CreateContext();
        var actions = await verification.Actions.AsNoTracking().OrderBy(action => action.Type).ToListAsync();
        Assert.Equal([ActionType.Review, ActionType.Fix, ActionType.Resolve], actions.Select(action => action.Type).ToArray());
        Assert.All(actions, action => Assert.Equal(ActionState.Done, action.State));
    }

    [Fact]
    public async Task Detectors_IrrelevantAndUnassociatedEventsAreIgnoredWhileMalformedPullRequestFailsPrecisely()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(
            database.Context,
            handler,
            "push",
            string.Empty,
            new { action = "created", comment = new { id = 1 } },
            BaseTime.AddMinutes(1));
        await HandleAsync(
            database.Context,
            handler,
            "check_run",
            "completed",
            CheckRunPayload("Build", "failure", "head-1", BaseTime.AddMinutes(2), associatedPullRequest: false),
            BaseTime.AddMinutes(2));

        var malformed = CreateStoredEvent(
            database.Context,
            "pull_request",
            "opened",
            new { action = "opened" },
            BaseTime.AddMinutes(3));
        await malformed.PersistAsync();
        var exception = await Assert.ThrowsAsync<JsonException>(
            () => handler.HandleAsync(malformed.StoredEvent, CancellationToken.None));

        Assert.Contains("pull_request", exception.Message, StringComparison.Ordinal);
        await using var verification = database.CreateContext();
        Assert.Empty(await verification.Actions.AsNoTracking().ToListAsync());
        Assert.Equal(10, await verification.ActionEventReceipts.CountAsync());
    }

    [Fact]
    public async Task CiFailure_MissingRepositoryContextOrActiveAuthorIdentity_IsIgnored()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        var repositoryless = new GitHubStoredEvent(
            Guid.NewGuid(),
            GitHubInstallationId,
            null,
            "check_run",
            "completed",
            JsonSerializer.Serialize(CheckRunPayload("Build", "failure", "head-1", BaseTime.AddMinutes(1))),
            BaseTime.AddMinutes(1));

        await handler.HandleAsync(repositoryless, CancellationToken.None);
        var authorMembership = await database.Context.InstallationMembers
            .SingleAsync(member => member.GitHubUserId == seed.Author.Id);
        authorMembership.Deactivate(BaseTime.AddMinutes(2));
        await database.Context.SaveChangesAsync();
        await HandleAsync(database.Context, handler, "pull_request", "opened", PullRequestPayload(false, BaseTime.AddMinutes(3), []), BaseTime.AddMinutes(3));
        await HandleAsync(database.Context, handler, "check_run", "completed", CheckRunPayload("Build", "failure", "head-1", BaseTime.AddMinutes(4)), BaseTime.AddMinutes(4));

        await using var verification = database.CreateContext();
        Assert.Empty(await verification.Actions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CiFailure_UnknownInstallationRepositoryRecord_IsIgnored()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        var handler = CreateHandler(database);
        var unknownRepositoryEvent = new GitHubStoredEvent(
            Guid.NewGuid(),
            GitHubInstallationId,
            GitHubRepositoryId,
            "check_run",
            "completed",
            JsonSerializer.Serialize(CheckRunPayload("Build", "failure", "head-1", BaseTime.AddMinutes(1))),
            BaseTime.AddMinutes(1));

        await handler.HandleAsync(unknownRepositoryEvent, CancellationToken.None);

        await using var verification = database.CreateContext();
        Assert.Empty(await verification.Actions.AsNoTracking().ToListAsync());
        Assert.Empty(await verification.ActionEventReceipts.AsNoTracking().ToListAsync());
    }

    [Fact]
    public void RegisteredDetectors_HaveStableUniqueKeysAndOrders()
    {
        var detectors = GetDetectors();

        Assert.Equal(
            [("github.review-requested.v1", 100), ("github.resolve-feedback.v1", 200), ("github.ci-failure.v1", 300), ("github.respond.v1", 350), ("github.merge-ready.v1", 400)],
            detectors.Select(detector => (detector.Key, detector.Order)).OrderBy(item => item.Order).ToArray());
        Assert.Equal(detectors.Count, detectors.Select(detector => detector.Key).Distinct(StringComparer.Ordinal).Count());
    }

    private static GitHubActionEventHandler CreateHandler(
        DetectorTestDatabase database,
        IGitHubPullRequestLookup? pullRequestLookup = null,
        int requiredApprovals = 1)
    {
        return new GitHubActionEventHandler(
            database,
            GetDetectors(pullRequestLookup, requiredApprovals),
            NullLogger<GitHubActionEventHandler>.Instance);
    }

    private static IReadOnlyList<IGitHubActionDetector> GetDetectors(
        IGitHubPullRequestLookup? pullRequestLookup = null,
        int requiredApprovals = 1)
    {
        var services = new ServiceCollection();
        services.AddNeedlyGitHubIntegration();
        services.Configure<GitHubActionOptions>(options => options.RequiredApprovals = requiredApprovals);
        services.AddSingleton(pullRequestLookup ?? new FakePullRequestLookup());
        return services.BuildServiceProvider().GetServices<IGitHubActionDetector>().ToArray();
    }

    private static async Task HandleAsync(
        NeedlyDbContext context,
        GitHubActionEventHandler handler,
        string eventName,
        string action,
        object payload,
        DateTimeOffset receivedAt)
    {
        var pendingEvent = CreateStoredEvent(context, eventName, action, payload, receivedAt);
        await pendingEvent.PersistAsync();
        await handler.HandleAsync(pendingEvent.StoredEvent, CancellationToken.None);
    }

    private static PendingStoredEvent CreateStoredEvent(
        NeedlyDbContext context,
        string eventName,
        string action,
        object payload,
        DateTimeOffset receivedAt)
    {
        var eventId = Guid.NewGuid();
        var rawEvent = RawEvent.CreateDelivery(
            eventId,
            TestData.InstallationId,
            GitHubInstallationId,
            TestData.RepositoryId,
            GitHubRepositoryId,
            $"delivery-{eventId:N}",
            eventName,
            action,
            JsonSerializer.Serialize(payload),
            receivedAt);
        return new PendingStoredEvent(
            context,
            rawEvent,
            new GitHubStoredEvent(
                rawEvent.Id,
                rawEvent.GitHubInstallationId,
                rawEvent.GitHubRepositoryId,
                rawEvent.EventName,
                rawEvent.EventAction,
                rawEvent.PayloadJson,
                rawEvent.ReceivedAt));
    }

    private static object PullRequestPayload(
        bool draft,
        DateTimeOffset updatedAt,
        long[] requestedReviewerIds,
        bool requestedTeam = false,
        string headSha = "head-1",
        bool merged = false) =>
        new
        {
            action = string.Empty,
            number = 42,
            pull_request = new
            {
                html_url = "https://github.com/octocat/needly/pull/42",
                title = "Improve action detection",
                draft,
                merged,
                closed_at = merged ? updatedAt : (DateTimeOffset?)null,
                merged_at = merged ? updatedAt : (DateTimeOffset?)null,
                updated_at = updatedAt,
                user = User(AuthorGitHubId, "author"),
                head = new { sha = headSha },
                requested_reviewers = requestedReviewerIds.Select(id =>
                    User(id, id == ReviewerGitHubId ? "reviewer" : "other-reviewer")),
                requested_teams = requestedTeam
                    ? new object[] { new { id = TeamGitHubId, slug = "maintainers", name = "Maintainers" } }
                    : Array.Empty<object>()
            },
            requested_reviewer = requestedReviewerIds.Length == 1
                ? User(requestedReviewerIds[0], "reviewer")
                : null,
            requested_team = requestedTeam
                ? new { id = TeamGitHubId, slug = "maintainers", name = "Maintainers" }
                : null
        };

    private static object PullRequestReviewPayload(
        long reviewerId,
        string reviewerLogin,
        string state,
        DateTimeOffset submittedAt,
        long reviewId = 9001) =>
        new
        {
            action = "submitted",
            number = 42,
            pull_request = new
            {
                html_url = "https://github.com/octocat/needly/pull/42",
                title = "Improve action detection",
                draft = false,
                merged = false,
                updated_at = submittedAt,
                user = User(AuthorGitHubId, "author"),
                head = new { sha = "head-1" },
                requested_reviewers = Array.Empty<object>(),
                requested_teams = Array.Empty<object>()
            },
            review = new
            {
                id = reviewId,
                state,
                user = User(reviewerId, reviewerLogin),
                submitted_at = submittedAt,
                html_url = $"https://github.com/octocat/needly/pull/42#pullrequestreview-{reviewId}"
            }
        };

    private static object PullRequestReviewCommentPayload(
        string action,
        DateTimeOffset occurredAt,
        string body = "",
        long commenterId = AuthorGitHubId,
        string commenterLogin = "author",
        string? userType = null,
        long commentId = 8001) =>
        new
        {
            action,
            pull_request = new
            {
                number = 42,
                html_url = "https://github.com/octocat/needly/pull/42",
                title = "Improve action detection",
                draft = false,
                merged = false,
                updated_at = occurredAt,
                user = User(AuthorGitHubId, "author"),
                head = new { sha = "head-1" },
                requested_reviewers = Array.Empty<object>(),
                requested_teams = Array.Empty<object>()
            },
            comment = new
            {
                id = commentId,
                user = User(commenterId, commenterLogin, userType),
                created_at = occurredAt,
                updated_at = occurredAt,
                pull_request_review_id = 9001,
                body,
                html_url = $"https://github.com/octocat/needly/pull/42#discussion_r{commentId}"
            }
        };

    private static object IssueCommentPayload(
        string body,
        long commenterId,
        string commenterLogin,
        DateTimeOffset occurredAt,
        bool isPullRequest = false,
        string? userType = null,
        long commentId = 7001) =>
        new
        {
            action = "created",
            issue = new
            {
                number = 42,
                html_url = isPullRequest
                    ? "https://github.com/octocat/needly/pull/42"
                    : "https://github.com/octocat/needly/issues/42",
                title = "Improve action detection",
                user = User(AuthorGitHubId, "author"),
                pull_request = isPullRequest ? new object() : null
            },
            comment = new
            {
                id = commentId,
                user = User(commenterId, commenterLogin, userType),
                created_at = occurredAt,
                updated_at = occurredAt,
                body,
                html_url = $"https://github.com/octocat/needly/issues/42#issuecomment-{commentId}"
            }
        };

    private static object IssuePayload(DateTimeOffset occurredAt) =>
        new
        {
            action = "closed",
            issue = new
            {
                number = 42,
                html_url = "https://github.com/octocat/needly/issues/42",
                title = "Improve action detection",
                user = User(AuthorGitHubId, "author"),
                pull_request = (object?)null,
                updated_at = occurredAt
            }
        };

    private static object CheckSuitePayload(string conclusion, DateTimeOffset occurredAt) =>
        new
        {
            action = "completed",
            check_suite = new
            {
                id = 6001,
                head_sha = "head-1",
                status = "completed",
                conclusion,
                updated_at = occurredAt,
                url = "https://github.com/octocat/needly/commit/head-1/checks",
                app = new { name = "GitHub Actions" },
                pull_requests = new[] { new { number = 42 } }
            }
        };

    private static object CheckRunPayload(
        string name,
        string conclusion,
        string headSha,
        DateTimeOffset occurredAt,
        bool associatedPullRequest = true) =>
        new
        {
            action = "completed",
            check_run = new
            {
                id = 6101,
                name,
                status = "completed",
                conclusion,
                completed_at = occurredAt,
                details_url = $"https://github.com/octocat/needly/actions/runs/6101/jobs/{name}",
                check_suite = new
                {
                    id = 6001,
                    head_sha = headSha,
                    status = "completed",
                    conclusion,
                    updated_at = occurredAt,
                    app = new { name = "GitHub Actions" },
                    pull_requests = associatedPullRequest ? new[] { new { number = 42 } } : Array.Empty<object>()
                },
                pull_requests = associatedPullRequest ? new[] { new { number = 42 } } : Array.Empty<object>()
            }
        };

    private static object WorkflowRunPayload(
        string name,
        string conclusion,
        string headSha,
        DateTimeOffset occurredAt) =>
        new
        {
            action = "completed",
            workflow_run = new
            {
                id = 6201,
                name,
                display_title = name,
                head_sha = headSha,
                status = "completed",
                conclusion,
                updated_at = occurredAt,
                html_url = "https://github.com/octocat/needly/actions/runs/6201",
                pull_requests = new[] { new { number = 42 } }
            }
        };

    private static object User(long id, string login, string? type = null) => new { id, login, type };

    private static GitHubPullRequestReadiness ReadyPullRequest(DateTimeOffset observedAt) =>
        new(
            42,
            AuthorGitHubId,
            "author",
            "head-1",
            "Improve action detection",
            "https://github.com/octocat/needly/pull/42",
            IsOpen: true,
            IsDraft: false,
            ApprovalCount: 1,
            HasChangesRequested: false,
            GitHubCheckState.Passing,
            IsMergeable: true,
            HasConflicts: false,
            observedAt);

    private static async Task<SeedResult> SeedAsync(NeedlyDbContext context)
    {
        var installation = Installation.Create(
            TestData.InstallationId,
            GitHubInstallationId,
            "octocat",
            BaseTime,
            GitHubAccountType.Organization);
        var repository = TestData.CreateRepository();
        var author = TestData.CreateGitHubUser(Guid.Parse("71000000-0000-0000-0000-000000000001"), AuthorGitHubId);
        var reviewer = TestData.CreateGitHubUser(Guid.Parse("71000000-0000-0000-0000-000000000002"), ReviewerGitHubId);
        var otherReviewer = TestData.CreateGitHubUser(Guid.Parse("71000000-0000-0000-0000-000000000003"), OtherReviewerGitHubId);
        var needlyUser = NeedlyUser.Create(
            Guid.Parse("71000000-0000-0000-0000-000000000004"),
            reviewer.Id,
            "reviewer@example.com",
            "Reviewer",
            BaseTime);
        var team = Team.Create(
            Guid.Parse("71000000-0000-0000-0000-000000000005"),
            installation.Id,
            TeamGitHubId,
            "maintainers",
            "Maintainers",
            BaseTime);
        context.AddRange(installation, repository, author, reviewer, otherReviewer, needlyUser, team);
        context.InstallationMembers.AddRange(
            InstallationMember.Create(Guid.NewGuid(), installation.Id, author.Id, BaseTime),
            InstallationMember.Create(Guid.NewGuid(), installation.Id, reviewer.Id, BaseTime),
            InstallationMember.Create(Guid.NewGuid(), installation.Id, otherReviewer.Id, BaseTime));
        context.TeamMembers.Add(TeamMember.Create(Guid.NewGuid(), team.Id, reviewer.Id, BaseTime));
        await context.SaveChangesAsync();
        return new SeedResult(author, reviewer, otherReviewer, needlyUser, team);
    }

    private sealed record SeedResult(
        GitHubUser Author,
        GitHubUser Reviewer,
        GitHubUser OtherReviewer,
        NeedlyUser NeedlyUser,
        Team Team);

    private sealed record PendingStoredEvent(
        NeedlyDbContext Context,
        RawEvent RawEvent,
        GitHubStoredEvent StoredEvent)
    {
        internal async Task PersistAsync()
        {
            Context.RawEvents.Add(RawEvent);
            await Context.SaveChangesAsync();
        }
    }

    private sealed class FakePullRequestLookup : IGitHubPullRequestLookup
    {
        internal GitHubPullRequestReadiness? Result { get; set; }

        internal Exception? Exception { get; set; }

        internal int CallCount { get; private set; }

        public Task<GitHubPullRequestReadiness?> GetAsync(
            long gitHubInstallationId,
            string repositoryOwner,
            string repositoryName,
            int pullRequestNumber,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(Result);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class DetectorTestDatabase : IDbContextFactory<NeedlyDbContext>, IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly DbContextOptions<NeedlyDbContext> options;

        private DetectorTestDatabase(
            SqliteConnection connection,
            DbContextOptions<NeedlyDbContext> options,
            NeedlyDbContext context)
        {
            this.connection = connection;
            this.options = options;
            Context = context;
        }

        internal NeedlyDbContext Context { get; }

        internal static async Task<DetectorTestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<NeedlyDbContext>().UseSqlite(connection).Options;
            var context = new NeedlyDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new DetectorTestDatabase(connection, options, context);
        }

        internal NeedlyDbContext CreateContext() => new(options);

        public NeedlyDbContext CreateDbContext() => CreateContext();

        public Task<NeedlyDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateContext());
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}