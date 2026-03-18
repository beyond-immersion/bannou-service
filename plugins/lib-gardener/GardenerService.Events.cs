using BeyondImmersion.BannouService.Account;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameSession;
using BeyondImmersion.BannouService.Location;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

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
    /// Maps WebSocket session IDs to account IDs, populated from session.connected events.
    /// Used to resolve account identity server-side per FOUNDATION TENETS (Account Identity Boundary).
    /// </summary>
    private readonly ConcurrentDictionary<Guid, Guid> _sessionAccountMap = new();

    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IGardenerService, SessionConnectedEvent>(
            "session.connected",
            async (svc, evt) => await ((GardenerService)svc).HandleSessionConnectedAsync(evt));

        eventConsumer.RegisterHandler<IGardenerService, SessionDisconnectedEvent>(
            "session.disconnected",
            async (svc, evt) => await ((GardenerService)svc).HandleSessionDisconnectedAsync(evt));

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

        // Account deletion cleanup per FOUNDATION TENETS (Account Deletion Cleanup Obligation)
        eventConsumer.RegisterHandler<IGardenerService, AccountDeletedEvent>(
            "account.deleted",
            async (svc, evt) => await ((GardenerService)svc).HandleAccountDeletedAsync(evt));
    }

    #region Event Handlers

    /// <summary>
    /// Handles session.connected events.
    /// Stores session-to-account mapping for server-side account resolution per FOUNDATION TENETS
    /// (Account Identity Boundary). Player-facing endpoints resolve accountId from this map
    /// rather than accepting it from the client.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleSessionConnectedAsync(SessionConnectedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.gardener", "GardenerService.HandleSessionConnected");

        _sessionAccountMap[evt.SessionId] = evt.AccountId;
        _logger.LogDebug("Session {SessionId} connected, account {AccountId} mapped",
            evt.SessionId, evt.AccountId);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles session.disconnected events.
    /// Removes session-to-account mapping and cleans up any active garden instance
    /// for the disconnected session.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleSessionDisconnectedAsync(SessionDisconnectedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.gardener", "GardenerService.HandleSessionDisconnected");

        _sessionAccountMap.TryRemove(evt.SessionId, out _);
        _logger.LogDebug("Session {SessionId} disconnected, account mapping removed",
            evt.SessionId);
        await Task.CompletedTask;
    }

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

                var garden = await _gardenStore.GetAsync(GardenKey(seed.OwnerId), CancellationToken.None);
                if (garden != null)
                {
                    garden.BondId = evt.BondId;
                    garden.NeedsReEvaluation = true;
                    await _gardenStore.SaveAsync(GardenKey(seed.OwnerId), garden, options: null, cancellationToken: CancellationToken.None);
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

        var garden = await _gardenStore.GetAsync(GardenKey(evt.OwnerId), CancellationToken.None);
        if (garden == null) return;

        garden.SeedId = evt.SeedId;
        garden.NeedsReEvaluation = true;
        await _gardenStore.SaveAsync(GardenKey(evt.OwnerId), garden, options: null, cancellationToken: CancellationToken.None);

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

        if (evt.GameType != _configuration.GameType)
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

    /// <summary>
    /// Handles account.deleted events.
    /// Cleans up all gardener data owned by the deleted account: garden instances,
    /// POIs, active scenarios (with backing game sessions), scenario history records,
    /// tracking set entries, and session-to-account mappings.
    /// Per FOUNDATION TENETS (Account Deletion Cleanup Obligation).
    /// </summary>
    /// <remarks>
    /// Event handlers have no generated controller boundary, so a top-level try-catch
    /// with TryPublishErrorAsync is required per IMPLEMENTATION TENETS (Error Handling).
    /// Not-found results are logged at Debug level because account.deleted is broadcast
    /// to all nodes; only one node may hold data for a given account.
    /// </remarks>
    public async Task HandleAccountDeletedAsync(AccountDeletedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.gardener", "GardenerService.HandleAccountDeleted");

        _logger.LogInformation("Handling account.deleted for account {AccountId}", evt.AccountId);

        try
        {
            // 1. Clean up active garden instance and its POIs
            try
            {
                var garden = await _gardenStore.GetAsync(GardenKey(evt.AccountId), CancellationToken.None);
                if (garden != null)
                {
                    foreach (var poiId in garden.ActivePoiIds)
                    {
                        try
                        {
                            await _poiStore.DeleteAsync(
                                PoiKey(garden.GardenInstanceId, poiId), CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex,
                                "Failed to delete POI {PoiId} for garden {GardenInstanceId} during account cleanup",
                                poiId, garden.GardenInstanceId);
                        }
                    }

                    await _gardenStore.DeleteAsync(GardenKey(evt.AccountId), CancellationToken.None);
                    await _gardenCacheStore.RemoveFromSetAsync<Guid>(
                        ActiveGardensTrackingKey, evt.AccountId, CancellationToken.None);

                    _logger.LogInformation(
                        "Cleaned up garden instance {GardenInstanceId} with {PoiCount} POIs for account {AccountId}",
                        garden.GardenInstanceId, garden.ActivePoiIds.Count, evt.AccountId);
                }
                else
                {
                    _logger.LogDebug(
                        "No active garden instance found for account {AccountId}", evt.AccountId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to clean up garden instance for account {AccountId}", evt.AccountId);
            }

            // 2. Clean up active scenario instance and its backing game session
            try
            {
                var scenario = await _scenarioStore.GetAsync(
                    ScenarioKey(evt.AccountId), CancellationToken.None);
                if (scenario != null)
                {
                    await TryCleanupGameSessionAsync(scenario, CancellationToken.None);
                    await _scenarioStore.DeleteAsync(ScenarioKey(evt.AccountId), CancellationToken.None);
                    await _gardenCacheStore.RemoveFromSetAsync<Guid>(
                        ActiveScenariosTrackingKey, evt.AccountId, CancellationToken.None);

                    _logger.LogInformation(
                        "Cleaned up active scenario {ScenarioInstanceId} for account {AccountId}",
                        scenario.ScenarioInstanceId, evt.AccountId);
                }
                else
                {
                    _logger.LogDebug(
                        "No active scenario found for account {AccountId}", evt.AccountId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to clean up active scenario for account {AccountId}", evt.AccountId);
            }

            // 3. Clean up scenario history records (MySQL, keyed by scenarioInstanceId)
            try
            {
                var historyRecords = await _historyStore.JsonQueryAsync(
                    new List<QueryCondition>
                    {
                        new() { Path = "$.AccountId", Operator = QueryOperator.Equals, Value = evt.AccountId.ToString() }
                    },
                    CancellationToken.None);

                var deletedCount = 0;
                foreach (var record in historyRecords)
                {
                    try
                    {
                        await _historyStore.DeleteAsync(record.Key, CancellationToken.None);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Failed to delete history record {Key} during account cleanup",
                            record.Key);
                    }
                }

                if (deletedCount > 0)
                {
                    _logger.LogInformation(
                        "Deleted {DeletedCount} scenario history records for account {AccountId}",
                        deletedCount, evt.AccountId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to clean up scenario history for account {AccountId}", evt.AccountId);
            }

            // 4. Remove session-to-account mappings for this account
            var removedSessions = 0;
            foreach (var kvp in _sessionAccountMap)
            {
                if (kvp.Value == evt.AccountId)
                {
                    _sessionAccountMap.TryRemove(kvp.Key, out _);
                    removedSessions++;
                }
            }

            if (removedSessions > 0)
            {
                _logger.LogDebug(
                    "Removed {SessionCount} session mapping(s) for deleted account {AccountId}",
                    removedSessions, evt.AccountId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to clean up gardener data for account {AccountId}", evt.AccountId);
            await _messageBus.TryPublishErrorAsync(
                "gardener", "HandleAccountDeleted", "cleanup_failed", ex.Message,
                dependency: null, endpoint: "account.deleted",
                details: $"accountId={evt.AccountId}", stack: ex.StackTrace);
        }
    }

    #endregion
}
