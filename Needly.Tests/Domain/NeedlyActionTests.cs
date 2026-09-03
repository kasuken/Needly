using Needly.Domain;
using Xunit;

namespace Needly.Tests.Domain;

public sealed class NeedlyActionTests
{
    [Fact]
    public void CreateForUser_ValidValues_InitializesIdentityContentAndOpenTimestamps()
    {
        var repository = TestData.CreateRepository();
        var assignee = TestData.CreateGitHubUser();
        var suppliedTimestamp = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.FromHours(2));
        var expectedTimestamp = suppliedTimestamp.ToUniversalTime();

        var action = TestData.CreateAction(
            repository: repository,
            assignee: assignee,
            createdAt: suppliedTimestamp);

        Assert.Equal(Guid.Parse("44444444-4444-4444-4444-444444444444"), action.Id);
        Assert.Equal(repository.InstallationId, action.InstallationId);
        Assert.Equal(repository.Id, action.RepositoryId);
        Assert.Equal(ActionType.Review, action.Type);
        Assert.Equal(ActionState.Open, action.State);
        Assert.Equal(ActionAssigneeType.User, action.AssigneeType);
        Assert.Equal(assignee.Id, action.AssigneeId);
        Assert.Equal(GitHubSubjectType.PullRequest, action.SubjectType);
        Assert.Equal(42, action.SubjectNumber);
        Assert.Equal("https://github.com/octocat/needly/pull/42", action.SubjectUrl.Value);
        Assert.Equal("Review the change", action.Title);
        Assert.Equal("Requested by the maintainer", action.Context);
        Assert.Equal("The pull request is waiting for review", action.Reason);
        Assert.Equal(expectedTimestamp, action.CreatedAt);
        Assert.Equal(expectedTimestamp, action.UpdatedAt);
        Assert.Equal(expectedTimestamp, action.WaitingSince);
        Assert.Equal(expectedTimestamp, action.LastActivityAt);
    }

    [Fact]
    public void ApplyEvent_NewerThenOlderActivity_UpdatesContentAndKeepsTimestampsMonotonic()
    {
        var action = TestData.CreateAction();
        var latestActivity = TestData.CreatedAt.AddHours(2);
        var latestUpdate = TestData.CreatedAt.AddHours(3);

        action.ApplyEvent(
            action.Key,
            "First update",
            "First context",
            "First reason",
            latestActivity,
            latestUpdate);
        action.ApplyEvent(
            action.Key,
            "Latest payload",
            null,
            "Latest reason",
            TestData.CreatedAt.AddHours(1),
            TestData.CreatedAt.AddHours(2));

        Assert.Equal("Latest payload", action.Title);
        Assert.Null(action.Context);
        Assert.Equal("Latest reason", action.Reason);
        Assert.Equal(latestActivity, action.LastActivityAt);
        Assert.Equal(latestUpdate, action.UpdatedAt);
        Assert.Equal(TestData.CreatedAt, action.CreatedAt);
        Assert.Equal(TestData.CreatedAt, action.WaitingSince);
    }

    [Fact]
    public void ApplyEvent_MismatchedKey_RejectsWithoutMutatingAction()
    {
        var action = TestData.CreateAction();
        var mismatchedKey = ActionKey.Create(
            ActionType.Respond,
            action.RepositoryId,
            action.SubjectType,
            action.SubjectNumber,
            action.AssigneeType,
            action.AssigneeId);

        var exception = Assert.Throws<ArgumentException>(() => action.ApplyEvent(
            mismatchedKey,
            "Changed title",
            "Changed context",
            "Changed reason",
            TestData.CreatedAt.AddHours(1),
            TestData.CreatedAt.AddHours(1)));

        Assert.Equal("key", exception.ParamName);
        Assert.Equal("Review the change", action.Title);
        Assert.Equal("Requested by the maintainer", action.Context);
        Assert.Equal("The pull request is waiting for review", action.Reason);
        Assert.Equal(ActionState.Open, action.State);
        Assert.Equal(TestData.CreatedAt, action.LastActivityAt);
        Assert.Equal(TestData.CreatedAt, action.UpdatedAt);
    }

    [Theory]
    [InlineData(ActionState.Snoozed)]
    [InlineData(ActionState.Archived)]
    [InlineData(ActionState.Muted)]
    [InlineData(ActionState.Done)]
    public void ChangeState_NonOpenState_TransitionsAndAdvancesUpdatedAt(ActionState targetState)
    {
        var action = TestData.CreateAction();
        var changedAt = TestData.CreatedAt.AddHours(1);

        action.ChangeState(targetState, changedAt);

        Assert.Equal(targetState, action.State);
        Assert.Equal(changedAt, action.UpdatedAt);
        Assert.Equal(TestData.CreatedAt, action.WaitingSince);
    }

    [Fact]
    public void ChangeState_StaleValidTimestamp_ChangesStateWithoutDecreasingUpdatedAt()
    {
        var action = TestData.CreateAction();
        var latestUpdate = TestData.CreatedAt.AddHours(3);
        action.ApplyEvent(
            action.Key,
            "Updated title",
            null,
            "Updated reason",
            TestData.CreatedAt.AddHours(2),
            latestUpdate);

        action.ChangeState(ActionState.Snoozed, TestData.CreatedAt.AddHours(1));

        Assert.Equal(ActionState.Snoozed, action.State);
        Assert.Equal(latestUpdate, action.UpdatedAt);
        Assert.Equal(TestData.CreatedAt, action.WaitingSince);
        Assert.Equal(TestData.CreatedAt.AddHours(2), action.LastActivityAt);
    }

    [Theory]
    [InlineData(ActionState.Snoozed)]
    [InlineData(ActionState.Archived)]
    [InlineData(ActionState.Muted)]
    [InlineData(ActionState.Done)]
    public void ChangeState_PreviouslyNonOpenAction_ReactivatesAndResetsWaitingSince(ActionState previousState)
    {
        var action = TestData.CreateAction();
        action.ChangeState(previousState, TestData.CreatedAt.AddHours(1));
        var reactivatedAt = TestData.CreatedAt.AddHours(2);

        action.ChangeState(ActionState.Open, reactivatedAt);

        Assert.Equal(ActionState.Open, action.State);
        Assert.Equal(reactivatedAt, action.UpdatedAt);
        Assert.Equal(reactivatedAt, action.WaitingSince);
        Assert.Equal(TestData.CreatedAt, action.LastActivityAt);
    }

    [Theory]
    [InlineData(ActionState.Archived)]
    [InlineData(ActionState.Muted)]
    [InlineData(ActionState.Done)]
    public void ApplyEvent_TerminalAction_RejectsUpdateWithoutChangingContent(ActionState terminalState)
    {
        var action = TestData.CreateAction();
        action.ChangeState(terminalState, TestData.CreatedAt.AddHours(1));

        Assert.Throws<InvalidOperationException>(() => action.ApplyEvent(
            action.Key,
            "Changed title",
            null,
            "Changed reason",
            TestData.CreatedAt.AddHours(2),
            TestData.CreatedAt.AddHours(2)));

        Assert.Equal(terminalState, action.State);
        Assert.Equal("Review the change", action.Title);
        Assert.Equal("Requested by the maintainer", action.Context);
        Assert.Equal(TestData.CreatedAt, action.LastActivityAt);
        Assert.Equal(TestData.CreatedAt.AddHours(1), action.UpdatedAt);
    }
}
