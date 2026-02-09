using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Seed;

/// <summary>
/// Partial class for SeedService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class SeedService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<ISeedService, SeedGrowthContributedEvent>(
            "seed.growth.contributed",
            async (svc, evt) => await ((SeedService)svc).HandleGrowthContributedAsync(evt));

    }

    /// <summary>
    /// Handles seed.growth.contributed events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleGrowthContributedAsync(SeedGrowthContributedEvent evt)
    {
        // TODO: Implement seed.growth.contributed event handling
        _logger.LogInformation("Received seed.growth.contributed event");
        return Task.CompletedTask;
    }
}
