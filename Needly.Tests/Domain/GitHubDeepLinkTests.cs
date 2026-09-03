using Needly.Domain;
using Xunit;

namespace Needly.Tests.Domain;

public sealed class GitHubDeepLinkTests
{
    [Theory]
    [InlineData("not-a-url")]
    [InlineData("http://github.com/octocat/needly/pull/42")]
    [InlineData("https://example.com/octocat/needly/pull/42")]
    [InlineData("https://github.com/octocat/needly/pull/42?view=files")]
    public void Create_InvalidOrNonGitHubLink_ThrowsArgumentException(string value)
    {
        var exception = Assert.Throws<ArgumentException>(() => GitHubDeepLink.Create(
            value,
            "octocat",
            "needly",
            GitHubSubjectType.PullRequest,
            42));

        Assert.Equal("value", exception.ParamName);
        Assert.IsType<FormatException>(exception.InnerException);
    }

    [Theory]
    [InlineData("different-owner", "needly", GitHubSubjectType.PullRequest, 42)]
    [InlineData("octocat", "different-repository", GitHubSubjectType.PullRequest, 42)]
    [InlineData("octocat", "needly", GitHubSubjectType.Issue, 42)]
    [InlineData("octocat", "needly", GitHubSubjectType.PullRequest, 43)]
    public void Create_SubjectIdentityMismatch_ThrowsArgumentException(
        string expectedOwner,
        string expectedRepository,
        GitHubSubjectType expectedSubjectType,
        int expectedSubjectNumber)
    {
        var exception = Assert.Throws<ArgumentException>(() => GitHubDeepLink.Create(
            "https://github.com/octocat/needly/pull/42",
            expectedOwner,
            expectedRepository,
            expectedSubjectType,
            expectedSubjectNumber));

        Assert.Equal("value", exception.ParamName);
        Assert.Contains("does not match", exception.Message, StringComparison.Ordinal);
    }
}
