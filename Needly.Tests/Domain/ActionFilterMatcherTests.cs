using Needly.Domain;
using Xunit;

namespace Needly.Tests.Domain;

public sealed class ActionFilterMatcherTests
{
    private static readonly ActionFilterCandidate Candidate = new(
        ActionType.Review,
        ActionState.Open,
        "octo-org/needly",
        "octo-org",
        "octocat",
        ActionAssigneeScope.Me,
        TimeSpan.FromHours(12),
        HasBotInvolvement: true);

    [Fact]
    public void IsMatch_EmptyFilter_MatchesAnyCandidate()
    {
        Assert.True(ActionFilterMatcher.IsMatch(new ActionFilter(), Candidate));
    }

    [Theory]
    [MemberData(nameof(SingleCriterionFilters))]
    public void IsMatch_EachConfiguredCriterion_MatchesExpectedCandidate(ActionFilter filter)
    {
        Assert.True(ActionFilterMatcher.IsMatch(filter, Candidate));
    }

    [Theory]
    [MemberData(nameof(MismatchedCriterionFilters))]
    public void IsMatch_WhenAnyConfiguredCriterionDiffers_DoesNotMatch(ActionFilter filter)
    {
        Assert.False(ActionFilterMatcher.IsMatch(filter, Candidate));
    }

    [Fact]
    public void IsMatch_OptionCollectionsUseOrAndDifferentCriteriaUseAnd()
    {
        var matching = new ActionFilter
        {
            Types = [ActionType.Fix, ActionType.Review],
            States = [ActionState.Open],
            Repositories = ["other/repository", "OCTO-ORG/NEEDLY"],
            Organizations = ["OCTO-ORG"],
            Authors = ["OCTOCAT"],
            AssigneeScope = ActionAssigneeScope.Me,
            WaitingAtLeast = TimeSpan.FromHours(12),
            BotInvolvement = BotInvolvementFilter.OnlyBots
        };
        var oneCriterionDiffers = matching with { States = [ActionState.Snoozed] };

        Assert.True(ActionFilterMatcher.IsMatch(matching, Candidate));
        Assert.False(ActionFilterMatcher.IsMatch(oneCriterionDiffers, Candidate));
    }

    [Fact]
    public void IsMatch_WaitingThreshold_IsInclusive()
    {
        var atThreshold = new ActionFilter { WaitingAtLeast = TimeSpan.FromHours(12) };
        var pastThreshold = new ActionFilter { WaitingAtLeast = TimeSpan.FromHours(12).Add(TimeSpan.FromTicks(1)) };

        Assert.True(ActionFilterMatcher.IsMatch(atThreshold, Candidate));
        Assert.False(ActionFilterMatcher.IsMatch(pastThreshold, Candidate));
    }

    [Fact]
    public void IsMatch_BotFilter_DistinguishesBotAndHumanActivity()
    {
        var humanCandidate = Candidate with { HasBotInvolvement = false };

        Assert.True(ActionFilterMatcher.IsMatch(
            new ActionFilter { BotInvolvement = BotInvolvementFilter.OnlyBots }, Candidate));
        Assert.False(ActionFilterMatcher.IsMatch(
            new ActionFilter { BotInvolvement = BotInvolvementFilter.ExcludeBots }, Candidate));
        Assert.True(ActionFilterMatcher.IsMatch(
            new ActionFilter { BotInvolvement = BotInvolvementFilter.ExcludeBots }, humanCandidate));
    }

    public static TheoryData<ActionFilter> SingleCriterionFilters => new()
    {
        new ActionFilter { Types = [ActionType.Review] },
        new ActionFilter { States = [ActionState.Open] },
        new ActionFilter { Repositories = ["OCTO-ORG/NEEDLY"] },
        new ActionFilter { Organizations = ["OCTO-ORG"] },
        new ActionFilter { Authors = ["OCTOCAT"] },
        new ActionFilter { AssigneeScope = ActionAssigneeScope.Me },
        new ActionFilter { WaitingAtLeast = TimeSpan.FromHours(8) },
        new ActionFilter { BotInvolvement = BotInvolvementFilter.OnlyBots }
    };

    public static TheoryData<ActionFilter> MismatchedCriterionFilters => new()
    {
        new ActionFilter { Types = [ActionType.Fix] },
        new ActionFilter { States = [ActionState.Snoozed] },
        new ActionFilter { Repositories = ["octo-org/other"] },
        new ActionFilter { Organizations = ["another-org"] },
        new ActionFilter { Authors = ["hubot"] },
        new ActionFilter { AssigneeScope = ActionAssigneeScope.MyTeam },
        new ActionFilter { WaitingAtLeast = TimeSpan.FromDays(1) },
        new ActionFilter { BotInvolvement = BotInvolvementFilter.ExcludeBots }
    };
}