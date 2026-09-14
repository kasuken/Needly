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
        HasBotInvolvement: true,
        Labels: ["bug", "needs-triage"],
        IsDraft: true,
        SizeBucket: ActionSizeBucket.M,
        Milestone: "v2",
        RequestedViaCodeowners: true,
        Title: "Add retry logic to the webhook dispatcher",
        Reason: "Review requested on pull request #42",
        Context: "Blocked on CI failures in the auth middleware",
        RiskLevel: ReviewRiskLevel.High);

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

    // Schema version 2 additions (issue #32).
    [Fact]
    public void IsMatch_Labels_UseOrSemanticsWithinTheCriterion()
    {
        Assert.True(ActionFilterMatcher.IsMatch(new ActionFilter { Labels = ["bug"] }, Candidate));
        Assert.True(ActionFilterMatcher.IsMatch(
            new ActionFilter { Labels = ["missing", "NEEDS-TRIAGE"] }, Candidate));
        Assert.False(ActionFilterMatcher.IsMatch(new ActionFilter { Labels = ["enhancement"] }, Candidate));
    }

    [Fact]
    public void IsMatch_DraftFilter_DistinguishesDraftAndReadyPullRequests()
    {
        var readyCandidate = Candidate with { IsDraft = false };
        var unknownCandidate = Candidate with { IsDraft = null };

        Assert.True(ActionFilterMatcher.IsMatch(new ActionFilter { IsDraft = DraftFilter.OnlyDrafts }, Candidate));
        Assert.False(ActionFilterMatcher.IsMatch(
            new ActionFilter { IsDraft = DraftFilter.ExcludeDrafts }, Candidate));
        Assert.True(ActionFilterMatcher.IsMatch(
            new ActionFilter { IsDraft = DraftFilter.ExcludeDrafts }, readyCandidate));
        Assert.False(ActionFilterMatcher.IsMatch(
            new ActionFilter { IsDraft = DraftFilter.OnlyDrafts }, unknownCandidate));
        Assert.True(ActionFilterMatcher.IsMatch(
            new ActionFilter { IsDraft = DraftFilter.ExcludeDrafts }, unknownCandidate));
    }

    [Fact]
    public void IsMatch_SizeBuckets_UseOrSemanticsAndRequireAKnownBucket()
    {
        var unknownSizeCandidate = Candidate with { SizeBucket = null };

        Assert.True(ActionFilterMatcher.IsMatch(new ActionFilter { SizeBuckets = [ActionSizeBucket.M] }, Candidate));
        Assert.True(ActionFilterMatcher.IsMatch(
            new ActionFilter { SizeBuckets = [ActionSizeBucket.XS, ActionSizeBucket.M] }, Candidate));
        Assert.False(ActionFilterMatcher.IsMatch(new ActionFilter { SizeBuckets = [ActionSizeBucket.XL] }, Candidate));
        Assert.False(ActionFilterMatcher.IsMatch(
            new ActionFilter { SizeBuckets = [ActionSizeBucket.M] }, unknownSizeCandidate));
    }

    [Fact]
    public void IsMatch_Milestones_MatchTheSingleAssignedMilestone()
    {
        Assert.True(ActionFilterMatcher.IsMatch(new ActionFilter { Milestones = ["v2"] }, Candidate));
        Assert.False(ActionFilterMatcher.IsMatch(new ActionFilter { Milestones = ["v3"] }, Candidate));
        Assert.False(ActionFilterMatcher.IsMatch(
            new ActionFilter { Milestones = ["v2"] }, Candidate with { Milestone = null }));
    }

    [Fact]
    public void IsMatch_RequestedViaCodeowners_DistinguishesCodeownersAndDirectRequests()
    {
        var directCandidate = Candidate with { RequestedViaCodeowners = false };

        Assert.True(ActionFilterMatcher.IsMatch(
            new ActionFilter { RequestedViaCodeowners = CodeownersFilter.OnlyRequested }, Candidate));
        Assert.False(ActionFilterMatcher.IsMatch(
            new ActionFilter { RequestedViaCodeowners = CodeownersFilter.ExcludeRequested }, Candidate));
        Assert.True(ActionFilterMatcher.IsMatch(
            new ActionFilter { RequestedViaCodeowners = CodeownersFilter.ExcludeRequested }, directCandidate));
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
        new ActionFilter { BotInvolvement = BotInvolvementFilter.OnlyBots },
        new ActionFilter { Labels = ["BUG"] },
        new ActionFilter { IsDraft = DraftFilter.OnlyDrafts },
        new ActionFilter { SizeBuckets = [ActionSizeBucket.M] },
        new ActionFilter { Milestones = ["V2"] },
        new ActionFilter { RequestedViaCodeowners = CodeownersFilter.OnlyRequested },
        new ActionFilter { FreeText = "retry logic" },
        new ActionFilter { RiskLevels = [ReviewRiskLevel.High] }
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
        new ActionFilter { BotInvolvement = BotInvolvementFilter.ExcludeBots },
        new ActionFilter { Labels = ["enhancement"] },
        new ActionFilter { IsDraft = DraftFilter.ExcludeDrafts },
        new ActionFilter { SizeBuckets = [ActionSizeBucket.XL] },
        new ActionFilter { Milestones = ["v3"] },
        new ActionFilter { RequestedViaCodeowners = CodeownersFilter.ExcludeRequested },
        new ActionFilter { FreeText = "does not appear anywhere" },
        new ActionFilter { RiskLevels = [ReviewRiskLevel.Low] }
    };

    // Schema version 3 additions (issue #34).
    [Fact]
    public void IsMatch_RiskLevels_UseOrSemanticsAndRequireAKnownLevel()
    {
        var unknownRiskCandidate = Candidate with { RiskLevel = null };

        Assert.True(ActionFilterMatcher.IsMatch(new ActionFilter { RiskLevels = [ReviewRiskLevel.High] }, Candidate));
        Assert.True(ActionFilterMatcher.IsMatch(
            new ActionFilter { RiskLevels = [ReviewRiskLevel.Low, ReviewRiskLevel.High] }, Candidate));
        Assert.False(ActionFilterMatcher.IsMatch(new ActionFilter { RiskLevels = [ReviewRiskLevel.Medium] }, Candidate));
        Assert.False(ActionFilterMatcher.IsMatch(
            new ActionFilter { RiskLevels = [ReviewRiskLevel.High] }, unknownRiskCandidate));
    }

    [Theory]
    [InlineData("retry logic")]
    [InlineData("RETRY LOGIC")]
    [InlineData("webhook dispatcher")]
    public void IsMatch_FreeTextMatchingTitle_IsCaseInsensitiveSubstring(string freeText)
    {
        Assert.True(ActionFilterMatcher.IsMatch(new ActionFilter { FreeText = freeText }, Candidate));
    }

    [Fact]
    public void IsMatch_FreeTextMatchingReason_Matches()
    {
        Assert.True(ActionFilterMatcher.IsMatch(new ActionFilter { FreeText = "pull request #42" }, Candidate));
    }

    [Fact]
    public void IsMatch_FreeTextMatchingContext_Matches()
    {
        Assert.True(ActionFilterMatcher.IsMatch(new ActionFilter { FreeText = "auth middleware" }, Candidate));
    }

    [Fact]
    public void IsMatch_FreeTextNotFoundInTitleReasonOrContext_DoesNotMatch()
    {
        Assert.False(ActionFilterMatcher.IsMatch(new ActionFilter { FreeText = "nonexistent phrase" }, Candidate));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsMatch_NullOrEmptyFreeText_MatchesAnyCandidate(string? freeText)
    {
        Assert.True(ActionFilterMatcher.IsMatch(new ActionFilter { FreeText = freeText }, Candidate));
    }

    [Fact]
    public void IsMatch_FreeTextWithNullTitleReasonAndContext_DoesNotMatch()
    {
        var candidate = Candidate with { Title = null, Reason = null, Context = null };
        Assert.False(ActionFilterMatcher.IsMatch(new ActionFilter { FreeText = "retry" }, candidate));
    }
}