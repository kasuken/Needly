using Needly.Domain;
using Xunit;

namespace Needly.Tests.Domain;

public sealed class InstallationTests
{
    [Fact]
    public void Create_ValidInstallation_IsActive()
    {
        var installation = CreateInstallation();

        Assert.Equal(InstallationState.Active, installation.State);
        Assert.True(installation.IsActive);
    }

    [Fact]
    public void Suspend_ActiveInstallation_IsInactiveAndAdvancesTimestamp()
    {
        var installation = CreateInstallation();
        var suspendedAt = TestData.CreatedAt.AddMinutes(1);

        installation.Suspend(suspendedAt);

        Assert.Equal(InstallationState.Suspended, installation.State);
        Assert.False(installation.IsActive);
        Assert.Equal(suspendedAt, installation.UpdatedAt);
    }

    [Fact]
    public void Activate_SuspendedInstallation_IsActiveAndUpdatesAccount()
    {
        var installation = CreateInstallation();
        installation.Suspend(TestData.CreatedAt.AddMinutes(1));
        var activatedAt = TestData.CreatedAt.AddMinutes(2);

        installation.Activate("octo-org", activatedAt);

        Assert.Equal(InstallationState.Active, installation.State);
        Assert.True(installation.IsActive);
        Assert.Equal("octo-org", installation.AccountLogin);
        Assert.Equal(activatedAt, installation.UpdatedAt);
    }

    [Fact]
    public void Delete_ActiveInstallation_IsInactiveAndRetainsRecord()
    {
        var installation = CreateInstallation();
        var deletedAt = TestData.CreatedAt.AddMinutes(1);

        installation.Delete(deletedAt);

        Assert.Equal(InstallationState.Deleted, installation.State);
        Assert.False(installation.IsActive);
        Assert.Equal(deletedAt, installation.UpdatedAt);
    }

    private static Installation CreateInstallation() =>
        Installation.Create(TestData.InstallationId, 501, "octocat", TestData.CreatedAt);
}