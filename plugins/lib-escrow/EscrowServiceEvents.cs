using BeyondImmersion.BannouService.Events;

namespace BeyondImmersion.BannouService.Escrow;

/// <summary>
/// Partial class for EscrowService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class EscrowService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
    }
}
