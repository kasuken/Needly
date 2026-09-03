using Needly.Domain;
using Xunit;

namespace Needly.Tests.Domain;

public sealed class ActionKeyTests
{
    private static readonly Guid RepositoryId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AssigneeId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Create_SameIdentityTuple_ReturnsStableCanonicalKey()
    {
        var first = CreateKey();
        var second = CreateKey();

        Assert.Equal(first, second);
        Assert.Equal(
            "0:11111111111111111111111111111111:0:42:0:22222222222222222222222222222222",
            first.Value);
        Assert.Equal(first.Value, first.ToString());
    }

    [Fact]
    public void Create_WhenActionTypeChanges_ReturnsDifferentKey()
    {
        var original = CreateKey();
        var changed = CreateKey(type: ActionType.Respond);

        Assert.NotEqual(original, changed);
        Assert.NotEqual(original.Value, changed.Value);
    }

    [Theory]
    [InlineData(ActionType.Review, "0:")]
    [InlineData(ActionType.Respond, "1:")]
    [InlineData(ActionType.Fix, "2:")]
    [InlineData(ActionType.Resolve, "3:")]
    [InlineData(ActionType.Merge, "4:")]
    [InlineData(ActionType.Decide, "5:")]
    [InlineData(ActionType.FollowUp, "6:")]
    [InlineData(ActionType.Monitor, "7:")]
    [InlineData(ActionType.FYI, "8:")]
    public void Create_EveryActionType_UsesExpectedIdentityDiscriminator(
        ActionType type,
        string expectedPrefix)
    {
        var key = CreateKey(type: type);

        Assert.StartsWith(expectedPrefix, key.Value, StringComparison.Ordinal);
        Assert.Equal(type, (ActionType)int.Parse(key.Value.Split(':')[0]));
    }

    [Fact]
    public void Create_WhenSubjectChanges_ReturnsDifferentKeys()
    {
        var original = CreateKey();
        ActionKey[] changedKeys =
        [
            CreateKey(repositoryId: Guid.Parse("33333333-3333-3333-3333-333333333333")),
            CreateKey(subjectType: GitHubSubjectType.Issue),
            CreateKey(subjectNumber: 43)
        ];

        Assert.All(changedKeys, changed => Assert.NotEqual(original, changed));
        Assert.Equal(changedKeys.Length, changedKeys.Distinct().Count());
    }

    [Fact]
    public void Create_WhenAssigneeChanges_ReturnsDifferentKeys()
    {
        var original = CreateKey();
        ActionKey[] changedKeys =
        [
            CreateKey(assigneeType: ActionAssigneeType.Team),
            CreateKey(assigneeId: Guid.Parse("44444444-4444-4444-4444-444444444444"))
        ];

        Assert.All(changedKeys, changed => Assert.NotEqual(original, changed));
        Assert.NotEqual(changedKeys[0], changedKeys[1]);
    }

    private static ActionKey CreateKey(
        ActionType type = ActionType.Review,
        Guid? repositoryId = null,
        GitHubSubjectType subjectType = GitHubSubjectType.PullRequest,
        int subjectNumber = 42,
        ActionAssigneeType assigneeType = ActionAssigneeType.User,
        Guid? assigneeId = null) =>
        ActionKey.Create(
            type,
            repositoryId ?? RepositoryId,
            subjectType,
            subjectNumber,
            assigneeType,
            assigneeId ?? AssigneeId);
}
