using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Genesis;

/// <summary>
/// Partial class for GenesisService event handling.
/// Contains self-subscription event consumers that maintain the in-memory wallet map across
/// nodes in multi-node deployments.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why self-subscribe?</b> The wallet map is per-node (local in-memory state). When an entity
/// is created on Node A, Node A populates its local wallet map immediately (in
/// <see cref="CreateEntityAsync"/>). But Node B also needs to know about that entity's wallets —
/// in case Currency on Node B processes a credit for one of them. The broadcast
/// <c>genesis.entity.created</c> event delivers that information to every node, and each node's
/// handler populates its local wallet map from the event payload.
/// </para>
/// <para>
/// <b>Multi-node coherence:</b> Event handlers run on every node via RabbitMQ fan-out. The local
/// handler on Node A will re-populate the map for entities it already populated during
/// <c>CreateEntityAsync</c> — that's a no-op overwrite with identical data.
/// </para>
/// </remarks>
public partial class GenesisService
{
    /// <summary>
    /// Registers event consumers for self-subscription events.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IGenesisService, GenesisEntityCreatedEvent>(
            GenesisPublishedTopics.GenesisEntityCreated,
            async (svc, evt) => await ((GenesisService)svc).HandleGenesisEntityCreatedAsync(evt));

        eventConsumer.RegisterHandler<IGenesisService, GenesisEntityDeletedEvent>(
            GenesisPublishedTopics.GenesisEntityDeleted,
            async (svc, evt) => await ((GenesisService)svc).HandleGenesisEntityDeletedAsync(evt));
    }

    /// <summary>
    /// Handles <c>genesis.entity.created</c> events for wallet map coherence across nodes.
    /// Reads the template to get growth mappings and populates the local wallet map for every
    /// wallet owned by the new entity.
    /// </summary>
    public async Task HandleGenesisEntityCreatedAsync(GenesisEntityCreatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisService.HandleGenesisEntityCreated");

        _logger.LogDebug(
            "Received genesis.entity.created for entity {EntityId} (template {TemplateCode}), updating wallet map",
            evt.EntityId, evt.TemplateCode);

        try
        {
            // Load the template to capture its growth mappings snapshot. The template is already
            // saved before the event is published (see CreateEntityAsync), so this read is safe
            // across nodes — MySQL replication delivers it before the RabbitMQ event in practice.
            var template = await _templateStore.GetAsync(BuildTemplateKey(evt.TemplateCode), CancellationToken.None);
            if (template == null)
            {
                _logger.LogWarning(
                    "genesis.entity.created for entity {EntityId}: template {TemplateCode} not found, " +
                    "wallet map entry cannot be populated on this node",
                    evt.EntityId, evt.TemplateCode);
                return;
            }

            var growthMappings = template.Economy.GrowthMappings.ToList();
            foreach (var (walletCode, walletId) in evt.WalletIds)
            {
                _growthState.SetWalletMapping(walletId, new GenesisWalletMapping(
                    EntityId: evt.EntityId,
                    TemplateCode: evt.TemplateCode,
                    WalletCode: walletCode,
                    GrowthMappings: growthMappings));
            }

            _logger.LogDebug(
                "Wallet map populated for entity {EntityId}: {WalletCount} mappings added",
                evt.EntityId, evt.WalletIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to populate wallet map for entity {EntityId} from genesis.entity.created event",
                evt.EntityId);
            // Do not publish error event — a failure here degrades this node's ability to
            // process currency mutations for the new entity, but doesn't corrupt any distributed
            // state. The next genesis.entity.updated event or plugin restart will re-populate.
        }
    }

    /// <summary>
    /// Handles <c>genesis.entity.deleted</c> events for wallet map coherence across nodes.
    /// Removes the deleted entity's wallets from the local wallet map.
    /// </summary>
    public async Task HandleGenesisEntityDeletedAsync(GenesisEntityDeletedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisService.HandleGenesisEntityDeleted");

        _logger.LogDebug(
            "Received genesis.entity.deleted for entity {EntityId}, removing from wallet map",
            evt.EntityId);

        try
        {
            foreach (var walletId in evt.WalletIds.Values)
                _growthState.TryRemoveWalletMapping(walletId);

            _logger.LogDebug(
                "Wallet map cleaned for entity {EntityId}: {WalletCount} mappings removed",
                evt.EntityId, evt.WalletIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to remove wallet map entries for entity {EntityId} from genesis.entity.deleted event",
                evt.EntityId);
        }

        await Task.CompletedTask;
    }
}
