using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.CharacterLifecycle;

/// <summary>
/// Partial class for CharacterLifecycleService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class CharacterLifecycleService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<ICharacterLifecycleService, WorldstateYearChangedEvent>(
            "worldstate.year-changed",
            async (svc, evt) => await ((CharacterLifecycleService)svc).HandleYearChangedAsync(evt));

        eventConsumer.RegisterHandler<ICharacterLifecycleService, WorldstateSeasonChangedEvent>(
            "worldstate.season-changed",
            async (svc, evt) => await ((CharacterLifecycleService)svc).HandleSeasonChangedAsync(evt));

        eventConsumer.RegisterHandler<ICharacterLifecycleService, ContractTerminatedEvent>(
            "contract.terminated",
            async (svc, evt) => await ((CharacterLifecycleService)svc).HandleContractTerminatedAsync(evt));

        eventConsumer.RegisterHandler<ICharacterLifecycleService, ContractBreachDetectedEvent>(
            "contract.breach.detected",
            async (svc, evt) => await ((CharacterLifecycleService)svc).HandleContractBreachedAsync(evt));

        eventConsumer.RegisterHandler<ICharacterLifecycleService, SeedPhaseChangedEvent>(
            "seed.phase.changed",
            async (svc, evt) => await ((CharacterLifecycleService)svc).HandleSeedPhaseChangedAsync(evt));

    }

    /// <summary>
    /// Handles worldstate.year-changed events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleYearChangedAsync(WorldstateYearChangedEvent evt)
    {
        // TODO: Implement worldstate.year-changed event handling
        _logger.LogInformation("Received {Topic} event", "worldstate.year-changed");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles worldstate.season-changed events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleSeasonChangedAsync(WorldstateSeasonChangedEvent evt)
    {
        // TODO: Implement worldstate.season-changed event handling
        _logger.LogInformation("Received {Topic} event", "worldstate.season-changed");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles contract.terminated events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleContractTerminatedAsync(ContractTerminatedEvent evt)
    {
        // TODO: Implement contract.terminated event handling
        _logger.LogInformation("Received {Topic} event", "contract.terminated");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles contract.breach.detected events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleContractBreachedAsync(ContractBreachDetectedEvent evt)
    {
        // TODO: Implement contract.breach.detected event handling
        _logger.LogInformation("Received {Topic} event", "contract.breach.detected");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles seed.phase.changed events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleSeedPhaseChangedAsync(SeedPhaseChangedEvent evt)
    {
        // TODO: Implement seed.phase.changed event handling
        _logger.LogInformation("Received {Topic} event", "seed.phase.changed");
        return Task.CompletedTask;
    }
}
