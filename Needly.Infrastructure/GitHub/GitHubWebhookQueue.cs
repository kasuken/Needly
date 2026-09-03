using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Needly.Application.GitHub;

namespace Needly.Infrastructure.GitHub;

/// <summary>Provides bounded in-process signaling for durably stored webhook events.</summary>
public sealed class GitHubWebhookQueue : IGitHubWebhookQueue
{
    private readonly Channel<Guid> channel;

    /// <summary>Creates a bounded queue using the configured capacity.</summary>
    public GitHubWebhookQueue(IOptions<GitHubAppOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(options.Value.WebhookQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });
    }

    /// <inheritdoc />
    public ValueTask EnqueueAsync(Guid eventId, CancellationToken cancellationToken)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("A stored event identifier is required.", nameof(eventId));
        }

        return channel.Writer.WriteAsync(eventId, cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<Guid> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var eventId in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return eventId;
        }
    }
}