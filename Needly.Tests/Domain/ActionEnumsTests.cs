using Xunit;
using Needly.Domain;

namespace Needly.Tests.Domain;

public sealed class ActionEnumsTests
{
    [Fact]
    public void ActionType_DeclaresEverySupportedWorkType()
    {
        ActionType[] expected =
        [
            ActionType.Review,
            ActionType.Respond,
            ActionType.Fix,
            ActionType.Resolve,
            ActionType.Merge,
            ActionType.Decide,
            ActionType.FollowUp,
            ActionType.Monitor,
            ActionType.FYI
        ];

        Assert.Equal(expected, Enum.GetValues<ActionType>());
    }

    [Fact]
    public void ActionState_DeclaresEverySupportedLifecycleState()
    {
        ActionState[] expected =
        [
            ActionState.Open,
            ActionState.Snoozed,
            ActionState.Archived,
            ActionState.Muted,
            ActionState.Done
        ];

        Assert.Equal(expected, Enum.GetValues<ActionState>());
    }
}
