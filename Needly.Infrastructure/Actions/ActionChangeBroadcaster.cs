using Microsoft.Extensions.Logging;
using Needly.Application.Actions;

namespace Needly.Infrastructure.Actions;

/// <summary>Notifies subscribers after durable action changes commit.</summary>
public sealed class ActionChangeBroadcaster(ILogger<ActionChangeBroadcaster> logger)
    : IActionChangeBroadcaster
{
    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public void Publish()
    {
        var subscribers = Changed;
        if (subscribers is null)
        {
            return;
        }

        foreach (Action subscriber in subscribers.GetInvocationList())
        {
            try
            {
                subscriber();
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "An action-change subscriber failed");
            }
        }
    }
}