using Microsoft.Extensions.Options;
using Needly.Domain;
using Needly.Infrastructure.Actions;
using Xunit;

namespace Needly.Tests.Infrastructure;

public sealed class ReviewRiskClassifierTests
{
    private static readonly IReviewRiskClassifier Classifier =
        new ReviewRiskClassifier(Options.Create(new ReviewRiskOptions()));

    [Fact]
    public void Classify_NullChangedFilePaths_DegradesToUnknownNeverLow()
    {
        var result = Classifier.Classify(null, additions: 10, deletions: 5);

        Assert.Equal(ReviewRiskLevel.Unknown, result.Level);
        Assert.Empty(result.MatchedSignals);
    }

    [Theory]
    [InlineData("src/Auth/Login.cs")]
    [InlineData("app/authentication/handler.py")]
    [InlineData("services/oauth/token.go")]
    public void Classify_AuthenticationPaths_IsHighWithAuthenticationSignal(string path)
    {
        var result = Classifier.Classify([path], additions: 5, deletions: 1);

        Assert.Equal(ReviewRiskLevel.High, result.Level);
        Assert.Contains("authentication", result.MatchedSignals);
    }

    [Fact]
    public void Classify_DatabaseMigrationPath_IsHighWithDatabaseMigrationSignal()
    {
        var result = Classifier.Classify(
            ["Needly.Infrastructure/Migrations/20260101000000_AddThing.cs"], additions: 20, deletions: 0);

        Assert.Equal(ReviewRiskLevel.High, result.Level);
        Assert.Contains("database migration", result.MatchedSignals);
    }

    [Fact]
    public void Classify_BillingPath_IsHighWithBillingSignal()
    {
        var result = Classifier.Classify(["src/billing/InvoiceService.cs"], additions: 3, deletions: 1);

        Assert.Equal(ReviewRiskLevel.High, result.Level);
        Assert.Contains("billing", result.MatchedSignals);
    }

    [Fact]
    public void Classify_InfrastructurePath_IsHighWithInfrastructureSignal()
    {
        var result = Classifier.Classify(["infra/main.tf"], additions: 2, deletions: 0);

        Assert.Equal(ReviewRiskLevel.High, result.Level);
        Assert.Contains("infrastructure", result.MatchedSignals);
    }

    [Fact]
    public void Classify_WorkflowConfigPath_IsMediumWithCiCdSignal()
    {
        var result = Classifier.Classify([".github/workflows/build.yml"], additions: 4, deletions: 0);

        Assert.Equal(ReviewRiskLevel.Medium, result.Level);
        Assert.Contains("CI/CD configuration", result.MatchedSignals);
    }

    [Fact]
    public void Classify_ControllerPath_IsMediumWithPublicApiSignal()
    {
        var result = Classifier.Classify(["src/Controllers/OrdersController.cs"], additions: 6, deletions: 2);

        Assert.Equal(ReviewRiskLevel.Medium, result.Level);
        Assert.Contains("public API", result.MatchedSignals);
    }

    [Fact]
    public void Classify_LargeDiff_IsMediumWithLargeDiffSignal()
    {
        var result = Classifier.Classify(["src/Widgets/Widget.cs"], additions: 400, deletions: 200);

        Assert.Equal(ReviewRiskLevel.Medium, result.Level);
        Assert.Contains("large diff", result.MatchedSignals);
    }

    [Fact]
    public void Classify_DocumentationOnly_IsLowWithDocumentationSignal()
    {
        var result = Classifier.Classify(["docs/github-app.md", "README.md"], additions: 10, deletions: 2);

        Assert.Equal(ReviewRiskLevel.Low, result.Level);
        Assert.Contains("documentation", result.MatchedSignals);
    }

    [Fact]
    public void Classify_TestFilesOnly_IsLowWithTestsOnlySignal()
    {
        var result = Classifier.Classify(
            ["Needly.Tests/Domain/NeedlyActionTests.cs", "Needly.Tests/Infrastructure/FooTests.cs"],
            additions: 30,
            deletions: 5);

        Assert.Equal(ReviewRiskLevel.Low, result.Level);
        Assert.Contains("tests only", result.MatchedSignals);
    }

    [Fact]
    public void Classify_NoMatchedSignals_IsLowWithNoSignals()
    {
        var result = Classifier.Classify(["src/Widgets/Widget.cs"], additions: 5, deletions: 1);

        Assert.Equal(ReviewRiskLevel.Low, result.Level);
        Assert.Empty(result.MatchedSignals);
    }

    [Fact]
    public void Classify_MultipleMatchedSignals_OverallLevelIsTheHighestSignal()
    {
        var result = Classifier.Classify(
            ["src/Auth/Login.cs", "docs/readme.md", ".github/workflows/build.yml"],
            additions: 5,
            deletions: 1);

        Assert.Equal(ReviewRiskLevel.High, result.Level);
        Assert.Contains("authentication", result.MatchedSignals);
        Assert.Contains("documentation", result.MatchedSignals);
        Assert.Contains("CI/CD configuration", result.MatchedSignals);
    }

    [Fact]
    public void Classify_NeverReturnsLevelWithoutMatchedSignalsWhenAnySignalMatched()
    {
        var result = Classifier.Classify(["src/Auth/Login.cs"], additions: 1, deletions: 0);

        Assert.NotEmpty(result.MatchedSignals);
    }
}
