using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Affix;

/// <summary>
/// Partial class for AffixService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class AffixService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IAffixService, ItemTemplateCreatedEvent>(
            "item.template.created",
            async (svc, evt) => await ((AffixService)svc).HandleItemTemplateCreatedAsync(evt));

        eventConsumer.RegisterHandler<IAffixService, ItemTemplateUpdatedEvent>(
            "item.template.updated",
            async (svc, evt) => await ((AffixService)svc).HandleItemTemplateUpdatedAsync(evt));

        eventConsumer.RegisterHandler<IAffixService, SeedCapabilityUpdatedEvent>(
            "seed.capability.updated",
            async (svc, evt) => await ((AffixService)svc).HandleSeedCapabilityUpdatedAsync(evt));
    }

    /// <summary>
    /// Handles item.template.created events.
    /// Checks for implicit mappings and warms pool cache for the template's category.
    /// </summary>
    public async Task HandleItemTemplateCreatedAsync(ItemTemplateCreatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.affix", "AffixService.HandleItemTemplateCreated");

        _logger.LogInformation("Handling item.template.created for template {TemplateId}", evt.TemplateId);

        // Check if new template has implicit mappings — no action needed
        // Pool cache warming happens on first request (lazy)
        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles item.template.updated events.
    /// Filters for deprecation changes and invalidates pool cache for the deprecated template's category.
    /// </summary>
    public async Task HandleItemTemplateUpdatedAsync(ItemTemplateUpdatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.affix", "AffixService.HandleItemTemplateUpdated");

        // Filter for changedFields containing isDeprecated
        if (evt.ChangedFields == null || !evt.ChangedFields.Contains("isDeprecated"))
            return;

        _logger.LogInformation("Template {TemplateId} deprecated, invalidating relevant pool caches", evt.TemplateId);

        // Pool cache invalidation is handled lazily — deprecated definitions are filtered during pool build
        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles seed.capability.updated events for cross-node cache invalidation.
    /// Complements ISeedEvolutionListener which only fires on the processing node.
    /// </summary>
    public async Task HandleSeedCapabilityUpdatedAsync(SeedCapabilityUpdatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.affix", "AffixService.HandleSeedCapabilityUpdated");

        // Filter for item-traits seed type
        if (evt.SeedTypeCode != _configuration.ItemTraitsSeedTypeCode)
            return;

        _logger.LogDebug("Invalidating instance cache for seed capability update, seed {SeedId}", evt.SeedId);

        // Cross-node cache invalidation: look up the seed to get the item instance ID,
        // then invalidate the instance cache on this node.
        try
        {
            var seedResponse = await _seedClient.GetSeedAsync(
                new GetSeedRequest { SeedId = evt.SeedId }, default);
            if (seedResponse != null && seedResponse.OwnerId != Guid.Empty)
            {
                await _instanceCache.DeleteAsync(BuildInstanceCacheKey(seedResponse.OwnerId));
                await _statsCache.DeleteAsync(BuildStatsCacheKey(seedResponse.OwnerId));
            }
        }
        catch (BeyondImmersion.Bannou.Core.ApiException ex)
        {
            _logger.LogWarning(ex, "Failed to look up seed {SeedId} for cache invalidation", evt.SeedId);
        }
    }
}
