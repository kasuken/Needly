using Needly.Domain;
using Xunit;

namespace Needly.Tests.Domain;

public sealed class NeedlyUserTests
{
    [Fact]
    public void CompleteOnboarding_NewUser_SetsCompletionAndUpdatedAt()
    {
        var user = CreateUser();
        var completedAt = TestData.CreatedAt.AddMinutes(5);

        user.CompleteOnboarding(completedAt);

        Assert.Equal(completedAt, user.OnboardingCompletedAt);
        Assert.Equal(completedAt, user.UpdatedAt);
    }

    [Fact]
    public void CompleteOnboarding_CompletedUser_PreservesOriginalCompletion()
    {
        var user = CreateUser();
        var firstCompletion = TestData.CreatedAt.AddMinutes(5);
        user.CompleteOnboarding(firstCompletion);

        user.CompleteOnboarding(firstCompletion.AddMinutes(5));

        Assert.Equal(firstCompletion, user.OnboardingCompletedAt);
    }

    private static NeedlyUser CreateUser() => NeedlyUser.Create(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "octocat@example.test",
        "The Octocat",
        TestData.CreatedAt);
}