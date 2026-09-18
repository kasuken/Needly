using Needly.Domain;
using Xunit;

namespace Needly.Tests.Domain;

public sealed class ActionQueryParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NullOrWhitespaceQuery_ReturnsEmptyFilter(string? query)
    {
        var result = ActionQueryParser.Parse(query);

        Assert.True(result.IsSuccess);
        Assert.Equal(new ActionFilter(), result.Filter);
    }

    [Fact]
    public void Parse_BareWord_BecomesFreeText()
    {
        var result = ActionQueryParser.Parse("dashboard");

        Assert.True(result.IsSuccess);
        Assert.Equal("dashboard", result.Filter!.FreeText);
    }

    [Fact]
    public void Parse_QuotedPhrase_BecomesFreeTextWithSpacesPreserved()
    {
        var result = ActionQueryParser.Parse("\"auth middleware\"");

        Assert.True(result.IsSuccess);
        Assert.Equal("auth middleware", result.Filter!.FreeText);
    }

    [Fact]
    public void Parse_ImplicitAndBetweenBareTermAndQualifier_CombinesBoth()
    {
        var result = ActionQueryParser.Parse("\"auth middleware\" org:contoso");

        Assert.True(result.IsSuccess);
        Assert.Equal("auth middleware", result.Filter!.FreeText);
        Assert.Equal(["contoso"], result.Filter.Organizations);
    }

    [Fact]
    public void Parse_MultipleQualifiers_AllCombineWithAnd()
    {
        var result = ActionQueryParser.Parse("repo:kasuken/Needly author:dependabot is:review waiting:>2d");

        Assert.True(result.IsSuccess);
        var filter = result.Filter!;
        Assert.Equal(["kasuken/Needly"], filter.Repositories);
        Assert.Equal(["dependabot"], filter.Authors);
        Assert.Equal([ActionType.Review], filter.Types);
        Assert.Equal(TimeSpan.FromDays(2), filter.WaitingAtLeast);
    }

    [Fact]
    public void Parse_ExplicitAnd_BehavesLikeImplicitAnd()
    {
        var result = ActionQueryParser.Parse("repo:a AND author:b");

        Assert.True(result.IsSuccess);
        Assert.Equal(["a"], result.Filter!.Repositories);
        Assert.Equal(["b"], result.Filter.Authors);
    }

    [Theory]
    [InlineData("and")]
    [InlineData("AND")]
    [InlineData("And")]
    public void Parse_AndKeyword_IsCaseInsensitive(string keyword)
    {
        var result = ActionQueryParser.Parse($"repo:a {keyword} author:b");

        Assert.True(result.IsSuccess);
        Assert.Equal(["a"], result.Filter!.Repositories);
        Assert.Equal(["b"], result.Filter.Authors);
    }

    [Theory]
    [InlineData("or")]
    [InlineData("OR")]
    [InlineData("Or")]
    public void Parse_OrKeyword_IsCaseInsensitive(string keyword)
    {
        var result = ActionQueryParser.Parse($"is:review {keyword} is:merge");

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[] { ActionType.Review, ActionType.Merge },
            result.Filter!.Types.OrderBy(t => t));
    }

    [Theory]
    [InlineData("not")]
    [InlineData("NOT")]
    [InlineData("Not")]
    public void Parse_NotKeyword_IsCaseInsensitive(string keyword)
    {
        var result = ActionQueryParser.Parse($"{keyword} bot:true");

        Assert.True(result.IsSuccess);
        Assert.Equal(BotInvolvementFilter.ExcludeBots, result.Filter!.BotInvolvement);
    }

    [Fact]
    public void Parse_OrOfSameQualifier_UnionsValues()
    {
        var result = ActionQueryParser.Parse("is:review OR is:merge");

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[] { ActionType.Review, ActionType.Merge },
            result.Filter!.Types.OrderBy(t => t));
    }

    [Fact]
    public void Parse_OrOfSameQualifier_IsCaseInsensitiveKeyword()
    {
        var lower = ActionQueryParser.Parse("is:review or is:merge");
        var mixed = ActionQueryParser.Parse("is:review Or is:merge");

        Assert.True(lower.IsSuccess);
        Assert.True(mixed.IsSuccess);
        Assert.Equal(
            lower.Filter!.Types.OrderBy(t => t),
            mixed.Filter!.Types.OrderBy(t => t));
    }

    [Fact]
    public void Parse_NotOfIsQualifier_ComplementsOverActionTypeDomain()
    {
        var result = ActionQueryParser.Parse("NOT is:review");

        Assert.True(result.IsSuccess);
        var types = result.Filter!.Types;
        Assert.DoesNotContain(ActionType.Review, types);
        Assert.Equal(Enum.GetValues<ActionType>().Length - 1, types.Length);
    }

    [Fact]
    public void Parse_NotOfBot_FlipsBotInvolvement()
    {
        var result = ActionQueryParser.Parse("NOT bot:true");

        Assert.True(result.IsSuccess);
        Assert.Equal(BotInvolvementFilter.ExcludeBots, result.Filter!.BotInvolvement);
    }

    [Fact]
    public void Parse_DoubleNot_RoundTrips()
    {
        var result = ActionQueryParser.Parse("NOT NOT is:review");

        Assert.True(result.IsSuccess);
        Assert.Equal([ActionType.Review], result.Filter!.Types);
    }

    [Fact]
    public void Parse_ParenthesesGroupNestedOrWithinAnd()
    {
        var result = ActionQueryParser.Parse("(is:review OR is:merge) AND repo:kasuken/Needly");

        Assert.True(result.IsSuccess);
        var filter = result.Filter!;
        Assert.Equal(
            new[] { ActionType.Review, ActionType.Merge },
            filter.Types.OrderBy(t => t));
        Assert.Equal(["kasuken/Needly"], filter.Repositories);
    }

    [Fact]
    public void Parse_DeeplyNestedParentheses_Parses()
    {
        var result = ActionQueryParser.Parse("(((is:review)))");

        Assert.True(result.IsSuccess);
        Assert.Equal([ActionType.Review], result.Filter!.Types);
    }

    [Fact]
    public void Parse_AndBindsTighterThanOr()
    {
        // is:review AND repo:a OR is:merge AND repo:b -- with AND higher precedence than OR, this groups as
        // (is:review AND repo:a) OR (is:merge AND repo:b); since that mixes fields (is + repo) within a single
        // OR branch this hits the cross-field ceiling once the OR combines the two (different) branches.
        var result = ActionQueryParser.Parse("is:review repo:a OR is:merge repo:b");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_SameFieldAndWithDifferentValues_IntersectsToEmptyAndFails()
    {
        var result = ActionQueryParser.Parse("is:review AND is:merge");

        Assert.False(result.IsSuccess);
        Assert.Contains("AND", result.Error);
    }

    [Fact]
    public void Parse_SameFieldAndWithSameValue_Succeeds()
    {
        var result = ActionQueryParser.Parse("is:review AND is:review");

        Assert.True(result.IsSuccess);
        Assert.Equal([ActionType.Review], result.Filter!.Types);
    }

    [Fact]
    public void Parse_WaitingGreaterThan_SetsWaitingAtLeast()
    {
        var result = ActionQueryParser.Parse("waiting:>2d");

        Assert.True(result.IsSuccess);
        Assert.Equal(TimeSpan.FromDays(2), result.Filter!.WaitingAtLeast);
    }

    [Theory]
    [InlineData("waiting:>2h", 2, "h")]
    [InlineData("waiting:>=3d", 3, "d")]
    [InlineData("waiting:>1w", 7, "d")]
    [InlineData("waiting:>30m", 30, "m")]
    public void Parse_WaitingUnits_AreConverted(string query, double amount, string unit)
    {
        var result = ActionQueryParser.Parse(query);

        Assert.True(result.IsSuccess);
        var expected = unit switch
        {
            "d" => TimeSpan.FromDays(amount),
            "h" => TimeSpan.FromHours(amount),
            "m" => TimeSpan.FromMinutes(amount),
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(expected, result.Filter!.WaitingAtLeast);
    }

    [Fact]
    public void Parse_BotTrue_SetsOnlyBots()
    {
        var result = ActionQueryParser.Parse("bot:true");

        Assert.True(result.IsSuccess);
        Assert.Equal(BotInvolvementFilter.OnlyBots, result.Filter!.BotInvolvement);
    }

    [Fact]
    public void Parse_BotFalse_SetsExcludeBots()
    {
        var result = ActionQueryParser.Parse("bot:false");

        Assert.True(result.IsSuccess);
        Assert.Equal(BotInvolvementFilter.ExcludeBots, result.Filter!.BotInvolvement);
    }

    [Theory]
    [InlineData("self-owned:true", SelfOwnedRepositoryFilter.OnlySelfOwned)]
    [InlineData("self-owned:false", SelfOwnedRepositoryFilter.ExcludeSelfOwned)]
    [InlineData("NOT self-owned:true", SelfOwnedRepositoryFilter.ExcludeSelfOwned)]
    [InlineData("NOT self-owned:false", SelfOwnedRepositoryFilter.OnlySelfOwned)]
    [InlineData("self-owned:true AND self-owned:true", SelfOwnedRepositoryFilter.OnlySelfOwned)]
    [InlineData("self-owned:true OR self-owned:true", SelfOwnedRepositoryFilter.OnlySelfOwned)]
    public void Parse_SelfOwned_CompilesToTheMatchingCriterion(string query, SelfOwnedRepositoryFilter expected)
    {
        var result = ActionQueryParser.Parse(query);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(expected, result.Filter!.SelfOwnedRepository);
    }

    [Fact]
    public void Parse_SelfOwnedCombinedWithOtherQualifiers_AndsTheCriteriaTogether()
    {
        var result = ActionQueryParser.Parse("is:merge state:open self-owned:true");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal([ActionType.Merge], result.Filter!.Types);
        Assert.Equal([ActionState.Open], result.Filter.States);
        Assert.Equal(SelfOwnedRepositoryFilter.OnlySelfOwned, result.Filter.SelfOwnedRepository);
    }

    [Theory]
    [InlineData("self-owned:maybe")]
    [InlineData("self-owned:true AND self-owned:false")]
    [InlineData("self-owned:true OR self-owned:false")]
    public void Parse_UnsupportedSelfOwnedUsage_ReportsASpecificError(string query)
    {
        var result = ActionQueryParser.Parse(query);

        Assert.False(result.IsSuccess);
        Assert.Contains("self-owned:", result.Error);
    }

    [Theory]
    [InlineData("state:open", ActionState.Open)]
    [InlineData("state:Snoozed", ActionState.Snoozed)]
    [InlineData("state:DONE", ActionState.Done)]
    public void Parse_StateQualifier_ParsesCaseInsensitively(string query, ActionState expected)
    {
        var result = ActionQueryParser.Parse(query);

        Assert.True(result.IsSuccess);
        Assert.Equal([expected], result.Filter!.States);
    }

    // ----- Required error cases -------------------------------------------------------------------------------

    [Theory]
    [InlineData("(is:review")]
    [InlineData("((is:review)")]
    [InlineData("is:review)")]
    [InlineData(")")]
    public void Parse_UnmatchedParentheses_ReturnsSpecificError(string query)
    {
        var result = ActionQueryParser.Parse(query);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Contains("parenthes", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.ErrorContext);
        Assert.True(result.ErrorPosition >= 0);
    }

    [Fact]
    public void Parse_UnknownQualifier_ReturnsSpecificError()
    {
        var result = ActionQueryParser.Parse("risk:high");

        Assert.False(result.IsSuccess);
        Assert.Contains("Unknown qualifier", result.Error);
        Assert.Contains("risk", result.Error);
    }

    [Fact]
    public void Parse_UnsupportedLabelQualifier_ReportedAsUnknown()
    {
        var result = ActionQueryParser.Parse("label:bug");

        Assert.False(result.IsSuccess);
        Assert.Contains("Unknown qualifier", result.Error);
    }

    [Fact]
    public void Parse_UnknownIsValue_ReturnsSpecificError()
    {
        var result = ActionQueryParser.Parse("is:teleport");

        Assert.False(result.IsSuccess);
        Assert.Contains("Unknown 'is:' value", result.Error);
        Assert.Contains("teleport", result.Error);
    }

    [Fact]
    public void Parse_UnknownStateValue_ReturnsSpecificError()
    {
        var result = ActionQueryParser.Parse("state:frozen");

        Assert.False(result.IsSuccess);
        Assert.Contains("Unknown 'state:' value", result.Error);
    }

    [Theory]
    [InlineData("waiting:2d")]
    [InlineData("waiting:abc")]
    [InlineData("waiting:>abc")]
    [InlineData("waiting:>5x")]
    [InlineData("waiting:>")]
    public void Parse_MalformedWaitingDuration_ReturnsSpecificError(string query)
    {
        var result = ActionQueryParser.Parse(query);

        Assert.False(result.IsSuccess);
        Assert.Contains("waiting:", result.Error);
    }

    [Fact]
    public void Parse_WaitingUpperBound_IsExplicitlyDescopedWithSpecificError()
    {
        var result = ActionQueryParser.Parse("waiting:<1h");

        Assert.False(result.IsSuccess);
        Assert.Contains("not supported", result.Error);
        Assert.Contains("WaitingAtLeast", result.Error);
    }

    [Fact]
    public void Parse_UnknownBotValue_ReturnsSpecificError()
    {
        var result = ActionQueryParser.Parse("bot:maybe");

        Assert.False(result.IsSuccess);
        Assert.Contains("Unknown 'bot:' value", result.Error);
    }

    [Fact]
    public void Parse_QualifierWithNoValue_ReturnsSpecificError()
    {
        var result = ActionQueryParser.Parse("repo:");

        Assert.False(result.IsSuccess);
        Assert.Contains("requires a value", result.Error);
    }

    [Fact]
    public void Parse_UnterminatedQuote_ReturnsSpecificError()
    {
        var result = ActionQueryParser.Parse("\"auth middleware");

        Assert.False(result.IsSuccess);
        Assert.Contains("Unterminated quoted phrase", result.Error);
    }

    [Fact]
    public void Parse_DanglingOperator_ReturnsSpecificError()
    {
        var result = ActionQueryParser.Parse("is:review AND");

        Assert.False(result.IsSuccess);
        Assert.Contains("AND", result.Error);
    }

    [Fact]
    public void Parse_CrossFieldOr_ReturnsCeilingError()
    {
        // This is the exact example from the issue's own "known limitation" callout: the flat AND-of-single
        // -field-OR-arrays model cannot express OR across different qualifiers.
        var result = ActionQueryParser.Parse("(repo:a OR author:b) AND NOT bot:true");

        Assert.False(result.IsSuccess);
        Assert.Contains("OR", result.Error);
        Assert.Contains("qualifier", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_NotOfUnboundedField_ReturnsCeilingError()
    {
        var result = ActionQueryParser.Parse("NOT repo:kasuken/Needly");

        Assert.False(result.IsSuccess);
        Assert.Contains("cannot be negated", result.Error);
    }

    [Fact]
    public void Parse_NotOfFreeText_ReturnsCeilingError()
    {
        var result = ActionQueryParser.Parse("NOT dashboard");

        Assert.False(result.IsSuccess);
        Assert.Contains("cannot be negated", result.Error);
    }

    [Fact]
    public void Parse_NotOfCompoundExpression_ReturnsCeilingError()
    {
        var result = ActionQueryParser.Parse("NOT (repo:a author:b)");

        Assert.False(result.IsSuccess);
        Assert.Contains("NOT", result.Error);
    }

    [Fact]
    public void Parse_OrOfFreeTextTerms_ReturnsCeilingError()
    {
        var result = ActionQueryParser.Parse("foo OR bar");

        Assert.False(result.IsSuccess);
        Assert.Contains("FreeText", result.Error);
    }
}
