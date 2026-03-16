using BeyondImmersion.BannouService.Events;
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
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleItemTemplateCreatedAsync(ItemTemplateCreatedEvent evt)
    {
        // TODO: Implement item.template.created event handling
        _logger.LogInformation("Received {Topic} event", "item.template.created");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles item.template.updated events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleItemTemplateUpdatedAsync(ItemTemplateUpdatedEvent evt)
    {
        // TODO: Implement item.template.updated event handling
        _logger.LogInformation("Received {Topic} event", "item.template.updated");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles seed.capability.updated events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleSeedCapabilityUpdatedAsync(SeedCapabilityUpdatedEvent evt)
    {
        // TODO: Implement seed.capability.updated event handling
        _logger.LogInformation("Received {Topic} event", "seed.capability.updated");
        return Task.CompletedTask;
    }
}
