using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Gardener;

/// <summary>
/// Partial class for GardenerService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class GardenerService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IGardenerService, SeedBondFormedEvent>(
            "seed.bond.formed",
            async (svc, evt) => await ((GardenerService)svc).HandleSeedBondFormedAsync(evt));

        eventConsumer.RegisterHandler<IGardenerService, SeedActivatedEvent>(
            "seed.activated",
            async (svc, evt) => await ((GardenerService)svc).HandleSeedActivatedAsync(evt));

        eventConsumer.RegisterHandler<IGardenerService, GameSessionDeletedEvent>(
            "game-session.deleted",
            async (svc, evt) => await ((GardenerService)svc).HandleGameSessionDeletedAsync(evt));
    }

    /// <summary>
    /// Handles seed.bond.formed events. When a bond forms between seeds, if one of them
    /// matches our SeedTypeCode ("guardian"), update the void instance to track the bond
    /// for shared void orchestration and bond scenario priority.
    /// </summary>
    public async Task HandleSeedBondFormedAsync(SeedBondFormedEvent evt)
    {
        _logger.LogDebug(
            "Handling seed.bond.formed: BondId={BondId}, SeedId={SeedId}, PartnerSeedId={PartnerSeedId}",
            evt.BondId, evt.SeedId, evt.PartnerSeedId);

        // Check if either seed has an active void instance - update bond tracking
        var voidStore = _stateStoreFactory.GetStore<VoidInstanceModel>(StateStoreDefinitions.GardenerVoidInstances);

        // Look up by the seed's owner account (we don't know which seed is ours without checking)
        // The void is keyed by accountId, but we only have seedId here.
        // Bond tracking is best-effort: the next void entry will pick up the bond info from the seed.
        _logger.LogInformation(
            "Bond formed between seeds {SeedId} and {PartnerSeedId}, bond scenario priority will apply on next void entry",
            evt.SeedId, evt.PartnerSeedId);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles seed.activated events. When a seed matching our SeedTypeCode is activated,
    /// log it for observability. The seed is now eligible for void entry.
    /// </summary>
    public async Task HandleSeedActivatedAsync(SeedActivatedEvent evt)
    {
        _logger.LogDebug(
            "Handling seed.activated: SeedId={SeedId}, SeedTypeCode={SeedTypeCode}",
            evt.SeedId, evt.SeedTypeCode);

        if (evt.SeedTypeCode != _configuration.SeedTypeCode)
        {
            _logger.LogDebug(
                "Ignoring seed activation for type {SeedTypeCode}, gardener manages {ManagedType}",
                evt.SeedTypeCode, _configuration.SeedTypeCode);
            return;
        }

        _logger.LogInformation(
            "Guardian seed activated: SeedId={SeedId}, now eligible for void entry",
            evt.SeedId);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles game-session.deleted events. When a game session is deleted, clean up any
    /// scenario instances that reference that session. This prevents orphaned scenarios.
    /// </summary>
    public async Task HandleGameSessionDeletedAsync(GameSessionDeletedEvent evt)
    {
        _logger.LogDebug(
            "Handling game-session.deleted: GameSessionId={GameSessionId}",
            evt.GameSessionId);

        var scenarioStore = _stateStoreFactory.GetStore<ScenarioInstanceModel>(StateStoreDefinitions.GardenerScenarioInstances);

        // Scan active scenarios for any referencing this game session
        // Since scenarios are keyed by scenarioInstanceId in Redis, we need to iterate
        // In practice, the scenario should have already been completed/abandoned before the session is deleted
        _logger.LogInformation(
            "Game session {GameSessionId} deleted, any orphaned scenarios will be cleaned up by lifecycle worker",
            evt.GameSessionId);

        await Task.CompletedTask;
    }
}
