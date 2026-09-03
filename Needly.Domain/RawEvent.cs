namespace Needly.Domain;

/// <summary>Identifies the durable processing state of a webhook delivery.</summary>
public enum RawEventStatus
{
    /// <summary>The delivery is persisted and waiting to be processed.</summary>
    Pending = 0,

    /// <summary>The delivery is currently being processed.</summary>
    Processing = 1,

    /// <summary>The delivery was processed successfully.</summary>
    Processed = 2,

    /// <summary>The delivery type is not handled and was intentionally skipped.</summary>
    Skipped = 3,

    /// <summary>A transient failure occurred and processing will be retried.</summary>
    RetryPending = 4,

    /// <summary>Processing exhausted its retry limit.</summary>
    Failed = 5
}

/// <summary>
/// Stores an immutable GitHub webhook delivery before action processing.
/// </summary>
public sealed class RawEvent
{
    private RawEvent()
    {
    }

    /// <summary>Gets the raw event identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the known internal installation that received the event.</summary>
    public Guid? InstallationId { get; private set; }

    /// <summary>Gets the GitHub installation identifier from the signed payload.</summary>
    public long GitHubInstallationId { get; private set; }

    /// <summary>Gets the optional repository associated with the event.</summary>
    public Guid? RepositoryId { get; private set; }

    /// <summary>Gets the optional GitHub repository identifier used for ordered processing.</summary>
    public long? GitHubRepositoryId { get; private set; }

    /// <summary>Gets the GitHub webhook delivery identifier.</summary>
    public string DeliveryId { get; private set; } = string.Empty;

    /// <summary>Gets the GitHub event name.</summary>
    public string EventName { get; private set; } = string.Empty;

    /// <summary>Gets the optional action from the webhook payload.</summary>
    public string? EventAction { get; private set; }

    /// <summary>Gets the original JSON payload.</summary>
    public string PayloadJson { get; private set; } = string.Empty;

    /// <summary>Gets when Needly received the webhook.</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>Gets when processing completed, if it has completed.</summary>
    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>Gets the durable processing state.</summary>
    public RawEventStatus Status { get; private set; }

    /// <summary>Gets the number of processing attempts.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>Gets the last processing error without payload or secret data.</summary>
    public string? LastError { get; private set; }

    /// <summary>Gets the earliest time at which processing may be retried.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>
    /// Creates a raw event.
    /// </summary>
    /// <param name="id">The raw event identifier.</param>
    /// <param name="installationId">The receiving installation identifier.</param>
    /// <param name="repositoryId">The optional repository identifier.</param>
    /// <param name="deliveryId">The GitHub delivery identifier.</param>
    /// <param name="eventName">The GitHub event name.</param>
    /// <param name="eventAction">The optional payload action.</param>
    /// <param name="payloadJson">The original JSON payload.</param>
    /// <param name="receivedAt">The explicit receipt timestamp.</param>
    /// <returns>A new unprocessed raw event.</returns>
    public static RawEvent Create(
        Guid id,
        Guid installationId,
        Guid? repositoryId,
        string deliveryId,
        string eventName,
        string? eventAction,
        string payloadJson,
        DateTimeOffset receivedAt)
    {
        if (repositoryId == Guid.Empty)
        {
            throw new ArgumentException("A repository identifier must be non-empty when supplied.", nameof(repositoryId));
        }

        return CreateDelivery(
            id,
            installationId,
            1,
            repositoryId,
            null,
            deliveryId,
            eventName,
            eventAction,
            payloadJson,
            receivedAt);
    }

    /// <summary>Creates a delivery using its external GitHub ordering identities.</summary>
    public static RawEvent CreateDelivery(
        Guid id,
        Guid? installationId,
        long gitHubInstallationId,
        Guid? repositoryId,
        long? gitHubRepositoryId,
        string deliveryId,
        string eventName,
        string? eventAction,
        string payloadJson,
        DateTimeOffset receivedAt)
    {
        if (installationId == Guid.Empty)
        {
            throw new ArgumentException("An installation identifier must be non-empty when supplied.", nameof(installationId));
        }

        if (repositoryId == Guid.Empty)
        {
            throw new ArgumentException("A repository identifier must be non-empty when supplied.", nameof(repositoryId));
        }

        if (gitHubRepositoryId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gitHubRepositoryId));
        }

        return new RawEvent
        {
            Id = DomainGuard.Required(id, nameof(id)),
            InstallationId = installationId,
            GitHubInstallationId = DomainGuard.Positive(gitHubInstallationId, nameof(gitHubInstallationId)),
            RepositoryId = repositoryId,
            GitHubRepositoryId = gitHubRepositoryId,
            DeliveryId = DomainGuard.Required(deliveryId, 100, nameof(deliveryId)),
            EventName = DomainGuard.Required(eventName, 100, nameof(eventName)),
            EventAction = DomainGuard.Optional(eventAction, 100, nameof(eventAction)),
            PayloadJson = DomainGuard.Required(payloadJson, int.MaxValue, nameof(payloadJson)),
            ReceivedAt = DomainGuard.Timestamp(receivedAt),
            Status = RawEventStatus.Pending
        };
    }

    /// <summary>Marks the event as being processed and increments its attempt count.</summary>
    public void MarkProcessing()
    {
        if (Status is not (RawEventStatus.Pending or RawEventStatus.RetryPending or RawEventStatus.Processing))
        {
            throw new InvalidOperationException("Only pending webhook events can begin processing.");
        }

        Status = RawEventStatus.Processing;
        AttemptCount++;
        LastError = null;
        NextAttemptAt = null;
    }

    /// <summary>Returns an interrupted processing attempt to the pending state during restart recovery.</summary>
    public void RecoverInterruptedProcessing()
    {
        if (Status == RawEventStatus.Processing)
        {
            Status = RawEventStatus.Pending;
        }
    }

    /// <summary>
    /// Marks the event as successfully processed.
    /// </summary>
    /// <param name="processedAt">The explicit completion timestamp.</param>
    public void MarkProcessed(DateTimeOffset processedAt)
    {
        if (Status != RawEventStatus.Processing)
        {
            throw new InvalidOperationException("The raw event is not being processed.");
        }

        ProcessedAt = DomainGuard.NotBefore(processedAt, ReceivedAt, nameof(processedAt));
        Status = RawEventStatus.Processed;
    }

    /// <summary>Marks an unknown event as intentionally skipped.</summary>
    /// <param name="processedAt">The explicit completion timestamp.</param>
    public void MarkSkipped(DateTimeOffset processedAt)
    {
        if (Status != RawEventStatus.Processing)
        {
            throw new InvalidOperationException("The raw event is not being processed.");
        }

        ProcessedAt = DomainGuard.NotBefore(processedAt, ReceivedAt, nameof(processedAt));
        Status = RawEventStatus.Skipped;
    }

    /// <summary>Records a processing failure and its optional retry schedule.</summary>
    /// <param name="error">A bounded, non-sensitive error description.</param>
    /// <param name="nextAttemptAt">The next attempt time, or null when retries are exhausted.</param>
    public void MarkFailed(string error, DateTimeOffset? nextAttemptAt)
    {
        if (Status != RawEventStatus.Processing)
        {
            throw new InvalidOperationException("The raw event is not being processed.");
        }

        LastError = DomainGuard.Required(error, 2000, nameof(error));
        if (nextAttemptAt is null)
        {
            Status = RawEventStatus.Failed;
            NextAttemptAt = null;
            return;
        }

        NextAttemptAt = DomainGuard.NotBefore(nextAttemptAt.Value, ReceivedAt, nameof(nextAttemptAt));
        Status = RawEventStatus.RetryPending;
    }
}