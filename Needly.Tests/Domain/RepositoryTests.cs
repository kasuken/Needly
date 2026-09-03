using Needly.Domain;
using Xunit;

namespace Needly.Tests.Domain;

public sealed class RepositoryTests
{
    [Fact]
    public void TryStartHistoricalBootstrap_FreshRepository_ClaimsOnce()
    {
        var repository = CreateRepository();
        var startedAt = TestData.CreatedAt.AddMinutes(1);

        var firstClaimed = repository.TryStartHistoricalBootstrap(startedAt, TestData.CreatedAt);
        var secondClaimed = repository.TryStartHistoricalBootstrap(
            startedAt.AddMinutes(1),
            TestData.CreatedAt);

        Assert.True(firstClaimed);
        Assert.False(secondClaimed);
    }

    [Fact]
    public void TryStartHistoricalBootstrap_StaleClaim_ClaimsAgain()
    {
        var repository = CreateRepository();
        var firstStartedAt = TestData.CreatedAt.AddMinutes(1);
        repository.TryStartHistoricalBootstrap(firstStartedAt, TestData.CreatedAt);

        var claimed = repository.TryStartHistoricalBootstrap(
            firstStartedAt.AddMinutes(16),
            firstStartedAt);

        Assert.True(claimed);
        Assert.Equal(firstStartedAt.AddMinutes(16), repository.HistoricalBootstrapStartedAt);
    }

    [Fact]
    public void CompleteHistoricalBootstrap_CompletedRepository_CannotBeClaimedAgain()
    {
        var repository = CreateRepository();
        var startedAt = TestData.CreatedAt.AddMinutes(1);
        var completedAt = startedAt.AddMinutes(1);
        repository.TryStartHistoricalBootstrap(startedAt, TestData.CreatedAt);

        repository.CompleteHistoricalBootstrap(completedAt);

        Assert.Equal(completedAt, repository.HistoricalBootstrapCompletedAt);
        Assert.False(repository.TryStartHistoricalBootstrap(
            completedAt.AddMinutes(30),
            completedAt.AddMinutes(15)));
    }

    [Fact]
    public void Update_InactiveRepository_ClearsHistoricalBootstrapCompletion()
    {
        var repository = CreateRepository();
        repository.TryStartHistoricalBootstrap(TestData.CreatedAt.AddMinutes(1), TestData.CreatedAt);
        repository.CompleteHistoricalBootstrap(TestData.CreatedAt.AddMinutes(2));
        repository.Deactivate(TestData.CreatedAt.AddMinutes(3));

        repository.Update("octo-org", "renamed", TestData.CreatedAt.AddMinutes(4));

        Assert.True(repository.IsActive);
        Assert.Null(repository.HistoricalBootstrapStartedAt);
        Assert.Null(repository.HistoricalBootstrapCompletedAt);
    }

    private static Repository CreateRepository() =>
        Repository.Create(
            TestData.RepositoryId,
            TestData.InstallationId,
            701,
            "octo-org",
            "needly",
            TestData.CreatedAt);
}