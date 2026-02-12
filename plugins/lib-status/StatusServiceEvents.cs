using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.State;
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
    /// </summary>
    /// <param name="evt">The seed capability updated event.</param>
    public async Task HandleSeedCapabilityUpdatedAsync(SeedCapabilityUpdatedEvent evt)
    {
        if (!_configuration.SeedEffectsEnabled)
        {
            return;
        }

        var cacheKey = $"seed:{evt.OwnerId}:{evt.OwnerType}";
        try
        {
            await SeedEffectsCacheStore.DeleteAsync(cacheKey);
            _logger.LogDebug(
                "Invalidated seed effects cache for {OwnerType} {OwnerId} via event",
                evt.OwnerType, evt.OwnerId);
        }
        catch (Exception ex)
        {
            // Cache invalidation failure is non-fatal; cache will expire via TTL
            _logger.LogWarning(ex,
                "Failed to invalidate seed effects cache for {OwnerType} {OwnerId} via event",
                evt.OwnerType, evt.OwnerId);
        }
    }
}
