using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Genesis;

/// <summary>
/// Partial class for GenesisService event handling.
/// Contains self-subscription event consumers for wallet map coherence.
/// </summary>
public partial class GenesisService
{
    /// <summary>
    /// Registers event consumers for self-subscription events.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IGenesisService, EntityCreatedEvent>(
            GenesisPublishedTopics.EntityCreated,
            async (svc, evt) => await ((GenesisService)svc).HandleGenesisEntityCreatedAsync(evt));

        eventConsumer.RegisterHandler<IGenesisService, EntityDeletedEvent>(
            GenesisPublishedTopics.EntityDeleted,
            async (svc, evt) => await ((GenesisService)svc).HandleGenesisEntityDeletedAsync(evt));
    }

    /// <summary>
    /// Handles genesis.entity.created events for wallet map coherence.
    /// </summary>
    public async Task HandleGenesisEntityCreatedAsync(EntityCreatedEvent evt)
    {
        _logger.LogDebug("Received genesis.entity.created for entity {EntityId}", evt.EntityId);
        // Wallet map coherence handled by external DI listener (GenesisCurrencyTransactionListener)
        // Self-subscription ensures all nodes see entity creation for wallet map updates
        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles genesis.entity.deleted events for wallet map coherence.
    /// </summary>
    public async Task HandleGenesisEntityDeletedAsync(EntityDeletedEvent evt)
    {
        _logger.LogDebug("Received genesis.entity.deleted for entity {EntityId}", evt.EntityId);
        // Wallet map coherence handled by external DI listener (GenesisCurrencyTransactionListener)
        // Self-subscription ensures all nodes see entity deletion for wallet map cleanup
        await Task.CompletedTask;
    }
}
