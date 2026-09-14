using Microsoft.Extensions.Options;
using Needly.Application.GitHub;
using Needly.Domain;
using Needly.Infrastructure.Actions;
using Xunit;

namespace Needly.Tests.Infrastructure;

public sealed class AttentionScoreCalculatorTests
{
    private static readonly VisibleAction BaseAction = new(
        ActionId: Guid.NewGuid(),
        RepositoryOwner: "octo-org",
        RepositoryName: "needly",
        SubjectTitle: "Add retry logic to the webhook dispatcher",
        SubjectNumber: 42,
        SubjectType: GitHubSubjectType.PullRequest,
        SubjectUrl: "https://github.com/octo-org/needly/pull/42",
        Type: ActionType.Review,
        State: ActionState.Open,
        Reason: "Review requested",
        Context: null,
        AssigneeDisplay: "Octocat (@octocat)",
        TriggerDisplay: "Review requested",
        WaitingSince: DateTimeOffset.UtcNow.AddMinutes(-30),
        WaitingDuration: TimeSpan.FromMinutes(30),
        IsAtRisk: false,
        RiskReason: null,
        AuthorLogin: "hubot",
        AssigneeScope: ActionAssigneeScope.MyTeam,
        HasBotInvolvement: false,
        IsPinned: false,
        Labels: [],
        IsDraft: null,
        SizeBucket: null,
        Milestone: null,
        AgentAuthor: null,
        AgentDisplayName: null,
        RequestedViaCodeowners: false);

    private static AttentionScoreCalculator CreateCalculator(AttentionScoreOptions? options = null) =>
        new(Options.Create(options ?? new AttentionScoreOptions()));

    [Fact]
    public void Calculate_ReviewAction_AwardsTypeBasePoints()
    {
        var calculator = CreateCalculator();

        var result = calculator.Calculate(BaseAction);

        Assert.Equal(30, result.Score);
        Assert.Single(result.Reasons);
        Assert.Equal(30, result.Reasons[0].Points);
    }

    [Fact]
    public void Calculate_FyiAction_ScoresZeroWithNoReasons()
    {
        var calculator = CreateCalculator();
        var action = BaseAction with { Type = ActionType.FYI };

        var result = calculator.Calculate(action);

        Assert.Equal(0, result.Score);
        Assert.Empty(result.Reasons);
        Assert.Equal("Low", result.Band);
    }

    [Fact]
    public void Calculate_AtRiskAction_AddsConfiguredBonusAndUsesRiskReasonText()
    {
        var calculator = CreateCalculator();
        var action = BaseAction with { IsAtRisk = true, RiskReason = "waiting over 8 hours" };

        var result = calculator.Calculate(action);

        Assert.Equal(50, result.Score);
        Assert.Contains(result.Reasons, reason => reason.Points == 20 && reason.Description == "waiting over 8 hours");
    }

    [Fact]
    public void Calculate_PinnedAction_AddsPinnedBonus()
    {
        var calculator = CreateCalculator();
        var action = BaseAction with { IsPinned = true };

        var result = calculator.Calculate(action);

        Assert.Equal(45, result.Score);
        Assert.Contains(result.Reasons, reason => reason.Description == "pinned");
    }

    [Fact]
    public void Calculate_DirectAssignment_ScoresHigherThanTeamAssignment()
    {
        var calculator = CreateCalculator();
        var direct = BaseAction with { AssigneeScope = ActionAssigneeScope.Me };
        var team = BaseAction with { AssigneeScope = ActionAssigneeScope.MyTeam };

        Assert.True(calculator.Calculate(direct).Score > calculator.Calculate(team).Score);
    }

    [Fact]
    public void Calculate_DraftPullRequest_AppliesConfiguredPenalty()
    {
        var calculator = CreateCalculator();
        var action = BaseAction with { IsDraft = true };

        var result = calculator.Calculate(action);

        // 30 (Review) - 15 (draft) = 15, clamped to the 0-100 range.
        Assert.Equal(15, result.Score);
        Assert.Contains(result.Reasons, reason => reason.Points < 0 && reason.Description == "still a draft");
    }

    [Theory]
    [InlineData(ActionSizeBucket.L)]
    [InlineData(ActionSizeBucket.XL)]
    public void Calculate_LargeOrExtraLargeDiff_AddsLargeDiffBonus(ActionSizeBucket bucket)
    {
        var calculator = CreateCalculator();
        var action = BaseAction with { SizeBucket = bucket };

        Assert.Contains(calculator.Calculate(action).Reasons, reason => reason.Description.Contains(bucket.ToString()));
    }

    [Theory]
    [InlineData(ActionSizeBucket.XS)]
    [InlineData(ActionSizeBucket.M)]
    public void Calculate_SmallOrMediumDiff_DoesNotAddLargeDiffBonus(ActionSizeBucket bucket)
    {
        var calculator = CreateCalculator();
        var action = BaseAction with { SizeBucket = bucket };

        Assert.DoesNotContain(calculator.Calculate(action).Reasons, reason => reason.Description.Contains("large change"));
    }

    [Fact]
    public void Calculate_UrgentLabelPresent_AddsUrgentLabelBonusCaseInsensitively()
    {
        var calculator = CreateCalculator();
        var action = BaseAction with { Labels = ["documentation", "SECURITY"] };

        var result = calculator.Calculate(action);

        Assert.Contains(result.Reasons, reason => reason.Description == "labeled \"SECURITY\"");
    }

    [Fact]
    public void Calculate_NoUrgentLabel_DoesNotAddUrgentLabelBonus()
    {
        var calculator = CreateCalculator();
        var action = BaseAction with { Labels = ["documentation"] };

        Assert.DoesNotContain(calculator.Calculate(action).Reasons, reason => reason.Description.StartsWith("labeled"));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(3, 2)]
    [InlineData(9, 6)]
    [InlineData(25, 12)]
    [InlineData(80, 20)]
    public void Calculate_WaitingDuration_UsesHighestMatchingTierWithoutStacking(int hours, int expectedWaitingPoints)
    {
        var calculator = CreateCalculator();
        var action = BaseAction with { WaitingDuration = TimeSpan.FromHours(hours) };

        var result = calculator.Calculate(action);
        var waitingReason = result.Reasons.SingleOrDefault(reason => reason.Description.StartsWith("waiting"));

        if (expectedWaitingPoints == 0)
        {
            Assert.Null(waitingReason);
        }
        else
        {
            Assert.NotNull(waitingReason);
            Assert.Equal(expectedWaitingPoints, waitingReason.Points);
        }
    }

    [Fact]
    public void Calculate_EverySignalStacked_ClampsAtOneHundred()
    {
        var calculator = CreateCalculator();
        var action = BaseAction with
        {
            Type = ActionType.Review,
            IsAtRisk = true,
            RiskReason = "waiting a long time",
            IsPinned = true,
            AssigneeScope = ActionAssigneeScope.Me,
            SizeBucket = ActionSizeBucket.XL,
            Labels = ["urgent"],
            WaitingDuration = TimeSpan.FromDays(5)
        };

        var result = calculator.Calculate(action);

        Assert.Equal(100, result.Score);
        // Every point in the clamped score must still be traceable to a named reason.
        Assert.True(result.Reasons.Sum(reason => reason.Points) >= 100);
    }

    [Fact]
    public void Calculate_ReasonsAreOrderedByDescendingPoints()
    {
        var calculator = CreateCalculator();
        var action = BaseAction with { IsAtRisk = true, RiskReason = "x", IsPinned = true, IsDraft = true };

        var points = calculator.Calculate(action).Reasons.Select(reason => reason.Points).ToArray();

        Assert.Equal(points.OrderByDescending(value => value), points);
    }

    [Fact]
    public void Calculate_CustomOptions_OverridesDefaultWeights()
    {
        var options = new AttentionScoreOptions
        {
            TypeBasePoints = new(StringComparer.OrdinalIgnoreCase) { ["Review"] = 5 },
            AtRiskBonus = 0
        };
        var calculator = CreateCalculator(options);
        var action = BaseAction with { IsAtRisk = true, RiskReason = "x" };

        Assert.Equal(5, calculator.Calculate(action).Score);
    }
}
