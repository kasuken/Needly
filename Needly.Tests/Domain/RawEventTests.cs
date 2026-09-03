using Needly.Domain;
using Xunit;

namespace Needly.Tests.Domain;

public sealed class RawEventTests
{
    [Fact]
    public void Create_ValidDeliveryIdentity_TrimsAndRetainsIdentity()
    {
        var eventId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var repositoryId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var receivedAt = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.FromHours(2));

        var rawEvent = RawEvent.Create(
            eventId,
            TestData.InstallationId,
            repositoryId,
            "  delivery-123  ",
            "pull_request",
            "opened",
            "{\"action\":\"opened\"}",
            receivedAt);

        Assert.Equal(eventId, rawEvent.Id);
        Assert.Equal(TestData.InstallationId, rawEvent.InstallationId);
        Assert.Equal(repositoryId, rawEvent.RepositoryId);
        Assert.Equal("delivery-123", rawEvent.DeliveryId);
        Assert.Equal("pull_request", rawEvent.EventName);
        Assert.Equal("opened", rawEvent.EventAction);
        Assert.Equal("{\"action\":\"opened\"}", rawEvent.PayloadJson);
        Assert.Equal(receivedAt.ToUniversalTime(), rawEvent.ReceivedAt);
        Assert.Null(rawEvent.ProcessedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_MissingDeliveryIdentity_ThrowsArgumentException(string? deliveryId)
    {
        var exception = Assert.Throws<ArgumentException>(() => RawEvent.Create(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            TestData.InstallationId,
            null,
            deliveryId!,
            "pull_request",
            null,
            "{}",
            TestData.CreatedAt));

        Assert.Equal("deliveryId", exception.ParamName);
    }

    [Fact]
    public void Create_DeliveryIdentityOverMaximumLength_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() => RawEvent.Create(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            TestData.InstallationId,
            null,
            new string('d', 101),
            "pull_request",
            null,
            "{}",
            TestData.CreatedAt));

        Assert.Equal("deliveryId", exception.ParamName);
        Assert.Contains("100", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_DeliveryIdentityAtMaximumLength_IsAcceptedWithoutTruncation()
    {
        var deliveryId = new string('d', 100);

        var rawEvent = RawEvent.Create(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            TestData.InstallationId,
            null,
            deliveryId,
            "pull_request",
            null,
            "{}",
            TestData.CreatedAt);

        Assert.Equal(100, rawEvent.DeliveryId.Length);
        Assert.Equal(deliveryId, rawEvent.DeliveryId);
    }
}
