using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameSession;
using BeyondImmersion.BannouService.Location;
using BeyondImmersion.BannouService.Seed;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Gardener;

/// <summary>
/// Partial class for GardenerService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPLEMENTATION TENETS - DI Listener:</b> ISeedEvolutionListener is handled by
/// <see cref="GardenerSeedEvolutionListener"/> (registered as Singleton) because
/// ISeedEvolutionListener must be resolvable from BackgroundService (Singleton) context,
/// while GardenerService is Scoped.
/// </para>
/// </remarks>
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

        // Location entity presence events for entity session registry bindings
        eventConsumer.RegisterHandler<IGardenerService, LocationEntityArrivedEvent>(
            "location.entity-arrived",
            async (svc, evt) => await ((GardenerService)svc).HandleLocationEntityArrivedAsync(evt));

        eventConsumer.RegisterHandler<IGardenerService, LocationEntityDepartedEvent>(
            "location.entity-departed",
            async (svc, evt) => await ((GardenerService)svc).HandleLocationEntityDepartedAsync(evt));
    }

    #region Event Handlers

    /// <summary>
    /// Handles seed.bond.formed events.
    /// Enables shared garden and adjusts POI scoring for bond-preferred scenarios.
    /// </summary>
    public async Task HandleSeedBondFormedAsync(SeedBondFormedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.gardener", "GardenerService.HandleSeedBondFormed");

        if (!_configuration.BondSharedGardenEnabled)
        {
            _logger.LogDebug("Bond shared garden disabled, ignoring bond formed event");
            return;
        }

        // Bond events contain seed IDs; we need account IDs to find garden instances.
        // Query the seed service for each seed's owner to find their garden instances.
        var updatedCount = 0;

        foreach (var participantSeedId in evt.ParticipantSeedIds)
        {
            try
            {
                var seed = await _seedClient.GetSeedAsync(
                    new GetSeedRequest { SeedId = participantSeedId }, CancellationToken.None);

                var garden = await GardenStore.GetAsync(GardenKey(seed.OwnerId), CancellationToken.None);
                if (garden != null)
                {
                    garden.BondId = evt.BondId;
                    garden.NeedsReEvaluation = true;
                    await GardenStore.SaveAsync(GardenKey(seed.OwnerId), garden, options: null, cancellationToken: CancellationToken.None);
                    updatedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update garden for seed {SeedId} in bond {BondId}",
                    participantSeedId, evt.BondId);
            }
        }

        _logger.LogInformation(
            "Bond {BondId} formed with {ParticipantCount} participant seeds, updated {UpdatedCount} gardens",
            evt.BondId, evt.ParticipantSeedIds.Count, updatedCount);
    }

    /// <summary>
    /// Handles seed.activated events.
    /// Updates the active seed reference in the garden instance.
    /// </summary>
    public async Task HandleSeedActivatedAsync(SeedActivatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.gardener", "GardenerService.HandleSeedActivated");

        var garden = await GardenStore.GetAsync(GardenKey(evt.OwnerId), CancellationToken.None);
        if (garden == null) return;

        garden.SeedId = evt.SeedId;
        garden.NeedsReEvaluation = true;
        await GardenStore.SaveAsync(GardenKey(evt.OwnerId), garden, options: null, cancellationToken: CancellationToken.None);

        _logger.LogInformation(
            "Updated active seed to {SeedId} for garden owner {OwnerId}",
            evt.SeedId, evt.OwnerId);
    }

    /// <summary>
    /// Handles game-session.deleted events for gardener-scenario game type.
    /// Cleanup is primarily handled by GardenerScenarioLifecycleWorker since we cannot
    /// reverse-lookup which account owns a scenario by GameSessionId. This handler
    /// logs the event for observability; the lifecycle worker detects orphaned scenarios
    /// via the LastActivityAt timeout (typically within 30 seconds).
    /// </summary>
    public async Task HandleGameSessionDeletedAsync(GameSessionDeletedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.gardener", "GardenerService.HandleGameSessionDeleted");

        if (evt.GameType != "gardener-scenario")
            return;

        _logger.LogInformation(
            "Gardener-backed game session {GameSessionId} deleted externally, " +
            "lifecycle worker will clean up associated scenario on next cycle",
            evt.SessionId);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles location.entity-arrived events.
    /// Registers entity session bindings for the location so that all sessions
    /// observing the arriving entity also receive location client events.
    /// </summary>
    /// <remarks>
    /// Queries the entity session registry for sessions associated with the arriving
    /// entity (by entityType + entityId), then registers each session for the location.
    /// This enables LocationPresenceChangedClientEvent delivery to players at that location.
    /// Gracefully degrades if no sessions are registered for the entity (e.g., NPC
    /// entities or entities without character-to-session bindings).
    /// </remarks>
    public async Task HandleLocationEntityArrivedAsync(LocationEntityArrivedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.gardener", "GardenerService.HandleLocationEntityArrived");

        var sessions = await _entitySessionRegistry.GetSessionsForEntityAsync(
            evt.EntityType, evt.EntityId, CancellationToken.None);

        if (sessions.Count == 0)
        {
            _logger.LogDebug(
                "No sessions registered for entity {EntityType}:{EntityId}, skipping location registration for {LocationId}",
                evt.EntityType, evt.EntityId, evt.LocationId);
            return;
        }

        foreach (var sessionId in sessions)
        {
            await _entitySessionRegistry.RegisterAsync(
                "location", evt.LocationId, sessionId, CancellationToken.None);
        }

        _logger.LogDebug(
            "Registered {SessionCount} session(s) for location {LocationId} from entity {EntityType}:{EntityId}",
            sessions.Count, evt.LocationId, evt.EntityType, evt.EntityId);
    }

    /// <summary>
    /// Handles location.entity-departed events.
    /// Unregisters entity session bindings for the location so that sessions
    /// no longer receive client events for the departed location.
    /// </summary>
    /// <remarks>
    /// Queries the entity session registry for sessions associated with the departing
    /// entity, then unregisters each session from the location. Gracefully degrades
    /// if no sessions are registered for the entity.
    /// </remarks>
    public async Task HandleLocationEntityDepartedAsync(LocationEntityDepartedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.gardener", "GardenerService.HandleLocationEntityDeparted");

        var sessions = await _entitySessionRegistry.GetSessionsForEntityAsync(
            evt.EntityType, evt.EntityId, CancellationToken.None);

        if (sessions.Count == 0)
        {
            _logger.LogDebug(
                "No sessions registered for entity {EntityType}:{EntityId}, skipping location unregistration for {LocationId}",
                evt.EntityType, evt.EntityId, evt.LocationId);
            return;
        }

        foreach (var sessionId in sessions)
        {
            await _entitySessionRegistry.UnregisterAsync(
                "location", evt.LocationId, sessionId, CancellationToken.None);
        }

        _logger.LogDebug(
            "Unregistered {SessionCount} session(s) from location {LocationId} for entity {EntityType}:{EntityId}",
            sessions.Count, evt.LocationId, evt.EntityType, evt.EntityId);
    }

    #endregion
}
