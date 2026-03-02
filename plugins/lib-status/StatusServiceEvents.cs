using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Status;

/// <summary>
/// Partial class for StatusService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
/// <remarks>
/// <para>
/// The <c>seed.capability.updated</c> subscription provides the distributed guarantee
/// for seed effects cache invalidation across all nodes. The DI listener
/// (<see cref="StatusSeedEvolutionListener"/>) provides fast local notification.
/// </para>
/// </remarks>
public partial class StatusService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IStatusService, SeedCapabilityUpdatedEvent>(
            "seed.capability.updated",
            async (svc, evt) => await ((StatusService)svc).HandleSeedCapabilityUpdatedAsync(evt));
    }

    /// <summary>
    /// Handles seed.capability.updated events for distributed cache invalidation.
    /// Complements the DI listener which only fires on the node that processed the API call.
    /// The event carries seedId only; we look up the seed owner to invalidate the correct cache entry.
    /// </summary>
    /// <param name="evt">The seed capability updated event.</param>
    public async Task HandleSeedCapabilityUpdatedAsync(SeedCapabilityUpdatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.status", "StatusService.HandleSeedCapabilityUpdatedAsync");
        if (!_configuration.SeedEffectsEnabled)
        {
            return;
        }

        // SeedCapabilityUpdatedEvent carries seedId, not ownerId;
        // look up the seed to find the owner for cache invalidation
        var seedClient = _serviceProvider.GetService<ISeedClient>();
        if (seedClient == null)
        {
            _logger.LogDebug("ISeedClient not available for seed effects cache invalidation via event");
            return;
        }

        try
        {
            var seed = await seedClient.GetSeedAsync(
                new GetSeedRequest { SeedId = evt.SeedId },
                CancellationToken.None);

            var cacheKey = SeedEffectsCacheKey(seed.OwnerId, seed.OwnerType);
            await SeedEffectsCacheStore.DeleteAsync(cacheKey);
            _logger.LogDebug(
                "Invalidated seed effects cache for {OwnerType} {OwnerId} via event (seed {SeedId})",
                seed.OwnerType, seed.OwnerId, evt.SeedId);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("Seed {SeedId} not found for cache invalidation via event", evt.SeedId);
        }
        catch (Exception ex)
        {
            // Cache invalidation failure is non-fatal; cache will expire via TTL
            _logger.LogWarning(ex,
                "Failed to invalidate seed effects cache for seed {SeedId} via event",
                evt.SeedId);
        }
    }
}
