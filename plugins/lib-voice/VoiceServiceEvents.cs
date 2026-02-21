using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Voice;

/// <summary>
/// Partial class for VoiceService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
/// <remarks>
/// Per the events schema (x-event-subscriptions: []), this service has no
/// external event subscriptions. Voice publishes events but does not consume them.
/// </remarks>
public partial class VoiceService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    /// <remarks>
    /// Voice does not consume external events per schema design (x-event-subscriptions: []).
    /// Future: could subscribe to stream.broadcast.started if needed.
    /// </remarks>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        // Voice does not consume external events.
        // Future: could subscribe to stream.broadcast.started if needed.
        _logger.LogDebug("Voice service event consumers registered (no external subscriptions)");
    }
}
