using Microsoft.EntityFrameworkCore;
using Needly.Domain;
using Xunit;
using static Needly.Tests.Infrastructure.DetectorTestSupport;

namespace Needly.Tests.Infrastructure;

public sealed class DecideActionDetectorTests
{
    [Fact]
    public async Task LabeledAndAssigned_CreatesDecideActionForAssignee()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var handler = CreateHandler(database);

        await HandleAsync(
            database.Context,
            handler,
            "issues",
            "assigned",
            IssuePayload("assigned", BaseTime.AddMinutes(1), labels: ["needs-decision"], assigneeIds: [ReviewerGitHubId]),
            BaseTime.AddMinutes(1));

        await using var verification = database.CreateContext();
        var action = await verification.Actions.AsNoTracking().SingleAsync();
        Assert.Equal(ActionType.Decide, action.Type);
        Assert.Equal(GitHubSubjectType.Issue, action.SubjectType);
        Assert.Equal(seed.Reviewer.Id, action.AssigneeId);
        Assert.Equal(ActionState.Open, action.State);
    }

    [Fact]
    public async Task MentionInBody_CreatesDecideActionForMentionedUser()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var handler = CreateHandler(database);

        await HandleAsync(
            database.Context,
            handler,
            "issues",
            "opened",
            IssuePayload(
                "opened",
                BaseTime.AddMinutes(1),
                labels: ["rfc"],
                body: $"Please weigh in @user-{ReviewerGitHubId}."),
            BaseTime.AddMinutes(1));

        await using var verification = database.CreateContext();
        var action = await verification.Actions.AsNoTracking().SingleAsync();
        Assert.Equal(ActionType.Decide, action.Type);
        Assert.Equal(seed.Reviewer.Id, action.AssigneeId);
    }

    [Fact]
    public async Task NoDecisionLabel_DoesNotCreateAction()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var handler = CreateHandler(database);

        await HandleAsync(
            database.Context,
            handler,
            "issues",
            "assigned",
            IssuePayload("assigned", BaseTime.AddMinutes(1), labels: ["bug"], assigneeIds: [ReviewerGitHubId]),
            BaseTime.AddMinutes(1));

        await using var verification = database.CreateContext();
        Assert.Empty(await verification.Actions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ReapplyingUnchangedState_DoesNotDuplicateAction()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(
            database.Context,
            handler,
            "issues",
            "assigned",
            IssuePayload("assigned", BaseTime.AddMinutes(1), labels: ["needs-decision"], assigneeIds: [ReviewerGitHubId]),
            BaseTime.AddMinutes(1));

        await HandleAsync(
            database.Context,
            handler,
            "issues",
            "edited",
            IssuePayload("edited", BaseTime.AddMinutes(2), labels: ["needs-decision"], assigneeIds: [ReviewerGitHubId]),
            BaseTime.AddMinutes(2));

        await using var verification = database.CreateContext();
        Assert.Equal(1, await verification.Actions.CountAsync());
        var action = await verification.Actions.AsNoTracking().SingleAsync();
        Assert.Equal(ActionState.Open, action.State);
    }

    [Fact]
    public async Task LastDecisionLabelRemoved_ResolvesOpenAction()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(
            database.Context,
            handler,
            "issues",
            "assigned",
            IssuePayload("assigned", BaseTime.AddMinutes(1), labels: ["needs-decision"], assigneeIds: [ReviewerGitHubId]),
            BaseTime.AddMinutes(1));

        await HandleAsync(
            database.Context,
            handler,
            "issues",
            "unlabeled",
            IssuePayload("unlabeled", BaseTime.AddMinutes(2), labels: [], assigneeIds: [ReviewerGitHubId]),
            BaseTime.AddMinutes(2));

        await using var verification = database.CreateContext();
        Assert.Equal(ActionState.Done, (await verification.Actions.AsNoTracking().SingleAsync()).State);
    }

    [Fact]
    public async Task UnassignedUser_ResolvesOnlyThatUsersAction()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(
            database.Context,
            handler,
            "issues",
            "assigned",
            IssuePayload(
                "assigned",
                BaseTime.AddMinutes(1),
                labels: ["needs-decision"],
                assigneeIds: [ReviewerGitHubId, OtherReviewerGitHubId]),
            BaseTime.AddMinutes(1));

        await HandleAsync(
            database.Context,
            handler,
            "issues",
            "unassigned",
            IssuePayload("unassigned", BaseTime.AddMinutes(2), labels: ["needs-decision"], assigneeIds: [OtherReviewerGitHubId]),
            BaseTime.AddMinutes(2));

        await using var verification = database.CreateContext();
        var actions = await verification.Actions.AsNoTracking().ToListAsync();
        Assert.Equal(2, actions.Count);
        Assert.Equal(ActionState.Done, actions.Single(action => action.AssigneeId == seed.Reviewer.Id).State);
        Assert.Equal(ActionState.Open, actions.Single(action => action.AssigneeId == seed.OtherReviewer.Id).State);
    }

    [Fact]
    public async Task IssueClosed_ResolvesAllOpenDecideActions()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(
            database.Context,
            handler,
            "issues",
            "assigned",
            IssuePayload(
                "assigned",
                BaseTime.AddMinutes(1),
                labels: ["needs-decision"],
                assigneeIds: [ReviewerGitHubId, OtherReviewerGitHubId]),
            BaseTime.AddMinutes(1));

        await HandleAsync(
            database.Context,
            handler,
            "issues",
            "closed",
            IssuePayload("closed", BaseTime.AddMinutes(2), labels: ["needs-decision"], assigneeIds: [ReviewerGitHubId, OtherReviewerGitHubId]),
            BaseTime.AddMinutes(2));

        await using var verification = database.CreateContext();
        Assert.All(await verification.Actions.AsNoTracking().ToListAsync(), action => Assert.Equal(ActionState.Done, action.State));
    }

    [Fact]
    public async Task PullRequestIssuePayload_IsIgnored()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var handler = CreateHandler(database);

        await HandleAsync(
            database.Context,
            handler,
            "issues",
            "assigned",
            new
            {
                action = "assigned",
                issue = new
                {
                    number = 77,
                    html_url = "https://github.com/octocat/needly/issues/77",
                    title = "Ship the v2 pricing model",
                    user = User(AuthorGitHubId, "author"),
                    pull_request = new { },
                    updated_at = BaseTime.AddMinutes(1),
                    labels = new[] { new { id = 1, name = "needs-decision" } },
                    assignees = new[] { User(ReviewerGitHubId, "reviewer") }
                }
            },
            BaseTime.AddMinutes(1));

        await using var verification = database.CreateContext();
        Assert.Empty(await verification.Actions.AsNoTracking().ToListAsync());
    }
}
