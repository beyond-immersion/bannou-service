using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Genesis;

/// <summary>
/// Partial class for GenesisService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class GenesisService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IGenesisService, EntityCreatedEvent>(
            "genesis.entity.created",
            async (svc, evt) => await ((GenesisService)svc).HandleGenesisEntityCreatedAsync(evt));

        eventConsumer.RegisterHandler<IGenesisService, EntityDeletedEvent>(
            "genesis.entity.deleted",
            async (svc, evt) => await ((GenesisService)svc).HandleGenesisEntityDeletedAsync(evt));

    }

    /// <summary>
    /// Handles genesis.entity.created events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleGenesisEntityCreatedAsync(EntityCreatedEvent evt)
    {
        // TODO: Implement genesis.entity.created event handling
        _logger.LogInformation("Received {Topic} event", "genesis.entity.created");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles genesis.entity.deleted events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleGenesisEntityDeletedAsync(EntityDeletedEvent evt)
    {
        // TODO: Implement genesis.entity.deleted event handling
        _logger.LogInformation("Received {Topic} event", "genesis.entity.deleted");
        return Task.CompletedTask;
    }
}
