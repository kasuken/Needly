using Needly.Infrastructure.GitHub;
using Xunit;

namespace Needly.Tests.Infrastructure;

public sealed class AgentClassifierTests
{
    private static AgentClassifier CreateClassifier() => new(new AgentDetectionOptions());

    [Theory]
    [InlineData("dependabot[bot]", "dependabot", "Dependabot")]
    [InlineData("DEPENDABOT[BOT]", "dependabot", "Dependabot")]
    [InlineData("renovate[bot]", "renovate", "Renovate")]
    [InlineData("copilot-swe-agent[bot]", "github-copilot", "GitHub Copilot")]
    [InlineData("devin-ai-integration[bot]", "devin", "Devin")]
    public void Classify_KnownBotLogin_ReturnsSpecificAgentIdentity(
        string login, string expectedKey, string expectedDisplayName)
    {
        var identity = CreateClassifier().Classify(login, type: "Bot", appSlug: null);

        Assert.NotNull(identity);
        Assert.Equal(expectedKey, identity!.Key);
        Assert.Equal(expectedDisplayName, identity.DisplayName);
    }

    [Fact]
    public void Classify_AppSlugMatchesARule_ReturnsSpecificAgentIdentityEvenWithoutABotLogin()
    {
        var identity = CreateClassifier().Classify(login: "some-user", type: null, appSlug: "dependabot");

        Assert.NotNull(identity);
        Assert.Equal("dependabot", identity!.Key);
    }

    [Fact]
    public void Classify_RecognizedBotThatMatchesNoRule_FallsBackToOtherBot()
    {
        var identity = CreateClassifier().Classify("some-other-tool[bot]", type: null, appSlug: null);

        Assert.NotNull(identity);
        Assert.Equal("other-bot", identity!.Key);
        Assert.Equal("Other bot", identity.DisplayName);
    }

    [Fact]
    public void Classify_BotTypeWithoutBotSuffixedLogin_FallsBackToOtherBot()
    {
        var identity = CreateClassifier().Classify("some-app", type: "Bot", appSlug: null);

        Assert.NotNull(identity);
        Assert.Equal("other-bot", identity!.Key);
    }

    [Theory]
    [InlineData("octocat", null)]
    [InlineData(null, null)]
    public void Classify_HumanOrUnknownLogin_ReturnsNull(string? login, string? type)
    {
        Assert.Null(CreateClassifier().Classify(login, type, appSlug: null));
    }

    [Fact]
    public void Classify_CustomRuleFromConfiguration_IsUsedWithoutACodeChange()
    {
        var options = new AgentDetectionOptions
        {
            Rules =
            [
                new AgentDetectionRule
                {
                    Key = "acme-bot",
                    DisplayName = "Acme Bot",
                    LoginEquals = ["acme-deploy[bot]"]
                }
            ]
        };
        var classifier = new AgentClassifier(options);

        var identity = classifier.Classify("acme-deploy[bot]", type: "Bot", appSlug: null);

        Assert.NotNull(identity);
        Assert.Equal("acme-bot", identity!.Key);
        Assert.Equal("Acme Bot", identity.DisplayName);
    }

    [Fact]
    public void Classify_FirstMatchingRuleWins_WhenMultipleRulesCouldMatch()
    {
        var options = new AgentDetectionOptions
        {
            Rules =
            [
                new AgentDetectionRule { Key = "first", DisplayName = "First", LoginEndsWith = ["[bot]"] },
                new AgentDetectionRule { Key = "second", DisplayName = "Second", LoginEquals = ["dependabot[bot]"] }
            ]
        };
        var classifier = new AgentClassifier(options);

        var identity = classifier.Classify("dependabot[bot]", type: null, appSlug: null);

        Assert.Equal("first", identity!.Key);
    }

    [Theory]
    [InlineData("octocat", null, false)]
    [InlineData("dependabot[bot]", null, true)]
    [InlineData("some-app", "Bot", true)]
    [InlineData(null, null, false)]
    public void IsBot_MatchesLoginSuffixOrType(string? login, string? type, bool expected)
    {
        Assert.Equal(expected, AgentClassifier.IsBot(login, type));
    }
}
