namespace Needly.Domain;

/// <summary>Records that one detector durably processed one stored GitHub event.</summary>
public sealed class ActionEventReceipt
{
    private ActionEventReceipt()
    {
    }

    /// <summary>Gets the receipt identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the stored event identifier.</summary>
    public Guid EventId { get; private set; }

    /// <summary>Gets the stable detector identity.</summary>
    public string DetectorKey { get; private set; } = string.Empty;

    /// <summary>Gets when the detector's operations were committed.</summary>
    public DateTimeOffset ProcessedAt { get; private set; }

    /// <summary>Creates a durable detector-processing receipt.</summary>
    public static ActionEventReceipt Create(
        Guid id,
        Guid eventId,
        string detectorKey,
        DateTimeOffset processedAt) =>
        new()
        {
            Id = DomainGuard.Required(id, nameof(id)),
            EventId = DomainGuard.Required(eventId, nameof(eventId)),
            DetectorKey = DomainGuard.Required(detectorKey, 200, nameof(detectorKey)),
            ProcessedAt = DomainGuard.Timestamp(processedAt)
        };
}