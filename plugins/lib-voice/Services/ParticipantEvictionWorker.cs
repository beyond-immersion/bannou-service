using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.Voice.ClientEvents;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Permission;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Voice.Services;

/// <summary>
/// Background service that periodically:
/// 1. Evicts participants with stale heartbeats (missed > ParticipantHeartbeatTimeoutSeconds)
/// 2. Auto-deletes empty rooms with AutoCleanup=true after EmptyRoomGracePeriodSeconds
/// 3. Auto-declines timed-out broadcast consent requests
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPLEMENTATION TENETS - Background Service Pattern:</b>
/// Uses IServiceProvider.CreateScope() to access scoped services (IMessageBus, IClientEventPublisher, IPermissionClient).
/// Follows established patterns from IdleRoomCleanupWorker, SeedDecayWorkerService.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Configuration-First:</b>
/// Uses ParticipantEvictionCheckIntervalSeconds, ParticipantHeartbeatTimeoutSeconds,
/// EmptyRoomGracePeriodSeconds, and BroadcastConsentTimeoutSeconds from VoiceServiceConfiguration.
/// </para>
/// </remarks>
public class ParticipantEvictionWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISipEndpointRegistry _endpointRegistry;
    private readonly ILogger<ParticipantEvictionWorker> _logger;
    private readonly VoiceServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Interval between eviction cycles, from configuration.
    /// </summary>
    private TimeSpan WorkerInterval => TimeSpan.FromSeconds(_configuration.ParticipantEvictionCheckIntervalSeconds);

    /// <summary>
    /// Initializes the participant eviction worker with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for creating scopes to access scoped services.</param>
    /// <param name="endpointRegistry">SIP endpoint registry for participant tracking (singleton).</param>
    /// <param name="logger">Logger for structured logging.</param>
    /// <param name="configuration">Voice service configuration with eviction settings.</param>
    public ParticipantEvictionWorker(
        IServiceProvider serviceProvider,
        ISipEndpointRegistry endpointRegistry,
        ILogger<ParticipantEvictionWorker> logger,
        VoiceServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider)
    {
        _serviceProvider = serviceProvider;
        _endpointRegistry = endpointRegistry;
        _logger = logger;
        _configuration = configuration;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
    }

    /// <summary>
    /// Main execution loop for the background service.
    /// Runs on a configurable interval and checks for stale participants, empty rooms, and consent timeouts.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token for graceful shutdown.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.voice", "ParticipantEvictionWorker.ExecuteAsync");
        _logger.LogInformation(
            "Participant eviction worker starting, interval: {Interval}s, heartbeat timeout: {Timeout}s, grace period: {Grace}s, consent timeout: {Consent}s",
            _configuration.ParticipantEvictionCheckIntervalSeconds,
            _configuration.ParticipantHeartbeatTimeoutSeconds,
            _configuration.EmptyRoomGracePeriodSeconds,
            _configuration.BroadcastConsentTimeoutSeconds);

        // Initial delay before first cycle to let the system stabilize
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_configuration.EvictionWorkerInitialDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Participant eviction worker cancelled during startup");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessEvictionCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during participant eviction cycle");
                try
                {
                    using var errorScope = _serviceProvider.CreateScope();
                    var messageBus = errorScope.ServiceProvider.GetRequiredService<IMessageBus>();
                    await messageBus.TryPublishErrorAsync(
                        "voice",
                        "ParticipantEvictionWorker",
                        ex.GetType().Name,
                        ex.Message,
                        severity: ServiceErrorEventSeverity.Error);
                }
                catch (Exception pubEx)
                {
                    _logger.LogDebug(pubEx, "Failed to publish error event - continuing eviction loop");
                }
            }

            try
            {
                await Task.Delay(WorkerInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Participant eviction worker stopped");
    }

    /// <summary>
    /// Processes one eviction cycle: stale participants, empty rooms, consent timeouts.
    /// </summary>
    internal async Task ProcessEvictionCycleAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.voice", "ParticipantEvictionWorker.ProcessEvictionCycleAsync");
        var trackedRoomIds = _endpointRegistry.GetAllTrackedRoomIds();

        if (trackedRoomIds.Count == 0)
        {
            return;
        }

        _logger.LogDebug("Eviction cycle: scanning {Count} tracked rooms", trackedRoomIds.Count);

        using var scope = _serviceProvider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var clientEventPublisher = scope.ServiceProvider.GetRequiredService<IClientEventPublisher>();
        var permissionClient = scope.ServiceProvider.GetRequiredService<IPermissionClient>();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var roomStore = stateStoreFactory.GetStore<VoiceRoomData>(StateStoreDefinitions.Voice);

        var now = DateTimeOffset.UtcNow;
        var heartbeatTimeout = TimeSpan.FromSeconds(_configuration.ParticipantHeartbeatTimeoutSeconds);
        var gracePeriod = TimeSpan.FromSeconds(_configuration.EmptyRoomGracePeriodSeconds);
        var consentTimeout = TimeSpan.FromSeconds(_configuration.BroadcastConsentTimeoutSeconds);

        var totalEvicted = 0;
        var totalRoomsDeleted = 0;
        var totalConsentDeclined = 0;

        foreach (var roomId in trackedRoomIds)
        {
            try
            {
                var roomData = await roomStore.GetAsync($"voice:room:{roomId}", cancellationToken);
                if (roomData == null)
                {
                    continue;
                }

                // 1. Evict stale participants
                var evicted = await EvictStaleParticipantsAsync(
                    roomId, roomData, now, heartbeatTimeout,
                    messageBus, clientEventPublisher, permissionClient, roomStore,
                    cancellationToken);
                totalEvicted += evicted;

                // 2. Auto-delete empty rooms with AutoCleanup after grace period
                if (roomData.AutoCleanup && roomData.LastParticipantLeftAt.HasValue)
                {
                    var currentCount = await _endpointRegistry.GetParticipantCountAsync(roomId, cancellationToken);
                    if (currentCount == 0 && now - roomData.LastParticipantLeftAt.Value > gracePeriod)
                    {
                        await DeleteEmptyRoomAsync(roomId, roomData, messageBus, clientEventPublisher, roomStore, stateStoreFactory, cancellationToken);
                        totalRoomsDeleted++;
                        continue;
                    }
                }

                // 3. Auto-decline timed-out broadcast consent requests
                if (roomData.BroadcastState == BroadcastConsentState.Pending && roomData.BroadcastRequestedAt.HasValue)
                {
                    if (now - roomData.BroadcastRequestedAt.Value > consentTimeout)
                    {
                        await AutoDeclineConsentAsync(roomId, roomData, messageBus, clientEventPublisher, permissionClient, roomStore, cancellationToken);
                        totalConsentDeclined++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing room {RoomId} during eviction cycle", roomId);
            }
        }

        if (totalEvicted > 0 || totalRoomsDeleted > 0 || totalConsentDeclined > 0)
        {
            _logger.LogInformation(
                "Eviction cycle complete: evicted={Evicted}, rooms_deleted={Deleted}, consent_declined={Declined}",
                totalEvicted, totalRoomsDeleted, totalConsentDeclined);
        }
    }

    /// <summary>
    /// Evicts participants with stale heartbeats from a room.
    /// </summary>
    /// <returns>Number of participants evicted.</returns>
    private async Task<int> EvictStaleParticipantsAsync(
        Guid roomId,
        VoiceRoomData roomData,
        DateTimeOffset now,
        TimeSpan heartbeatTimeout,
        IMessageBus messageBus,
        IClientEventPublisher clientEventPublisher,
        IPermissionClient permissionClient,
        IStateStore<VoiceRoomData> roomStore,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.voice", "ParticipantEvictionWorker.EvictStaleParticipantsAsync");
        var participants = await _endpointRegistry.GetRoomParticipantsAsync(roomId, cancellationToken);
        var staleParticipants = participants
            .Where(p => now - p.LastHeartbeat > heartbeatTimeout)
            .ToList();

        if (staleParticipants.Count == 0)
        {
            return 0;
        }

        var evictedCount = 0;
        var broadcastBroken = false;

        foreach (var stale in staleParticipants)
        {
            _logger.LogInformation(
                "Evicting stale participant {SessionId} from room {RoomId}, last heartbeat: {LastHeartbeat}",
                stale.SessionId, roomId, stale.LastHeartbeat);

            // Unregister from endpoint registry
            await _endpointRegistry.UnregisterAsync(roomId, stale.SessionId, cancellationToken);

            var remainingCount = await _endpointRegistry.GetParticipantCountAsync(roomId, cancellationToken);

            // Publish peer left service event
            await messageBus.TryPublishAsync("voice.peer.left", new VoicePeerLeftEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                RoomId = roomId,
                PeerSessionId = stale.SessionId,
                RemainingCount = remainingCount
            });

            // Notify remaining peers via client event
            var remainingParticipants = await _endpointRegistry.GetRoomParticipantsAsync(roomId, cancellationToken);
            if (remainingParticipants.Count > 0)
            {
                var peerLeftEvent = new VoicePeerLeftClientEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    RoomId = roomId,
                    PeerSessionId = stale.SessionId,
                    DisplayName = stale.DisplayName,
                    RemainingParticipantCount = remainingCount
                };

                await clientEventPublisher.PublishToSessionsAsync(
                    remainingParticipants.Select(p => p.SessionId.ToString()),
                    peerLeftEvent,
                    cancellationToken);
            }

            // Clear permission state for evicted participant
            try
            {
                await permissionClient.ClearSessionStateAsync(new ClearSessionStateRequest
                {
                    SessionId = stale.SessionId,
                    ServiceId = "voice"
                }, cancellationToken);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Failed to clear voice permission state for evicted session {SessionId}", stale.SessionId);
            }

            // Check if eviction breaks broadcast consent
            if (roomData.BroadcastState == BroadcastConsentState.Approved ||
                roomData.BroadcastState == BroadcastConsentState.Pending)
            {
                broadcastBroken = true;
            }

            evictedCount++;

            // If room is now empty and AutoCleanup, set timestamp
            if (remainingCount == 0 && roomData.AutoCleanup)
            {
                roomData.LastParticipantLeftAt = now;
                await roomStore.SaveAsync($"voice:room:{roomId}", roomData, cancellationToken: cancellationToken);
            }
        }

        // If eviction broke broadcast consent, stop broadcast
        if (broadcastBroken)
        {
            await StopBroadcastFromWorkerAsync(roomId, roomData, VoiceBroadcastStoppedReason.ConsentRevoked,
                messageBus, clientEventPublisher, permissionClient, roomStore, cancellationToken);
        }

        return evictedCount;
    }

    /// <summary>
    /// Deletes an empty room that has exceeded its grace period.
    /// </summary>
    private async Task DeleteEmptyRoomAsync(
        Guid roomId,
        VoiceRoomData roomData,
        IMessageBus messageBus,
        IClientEventPublisher clientEventPublisher,
        IStateStore<VoiceRoomData> roomStore,
        IStateStoreFactory stateStoreFactory,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.voice", "ParticipantEvictionWorker.DeleteEmptyRoomAsync");
        _logger.LogInformation("Auto-deleting empty room {RoomId} after grace period", roomId);

        // Stop any in-progress broadcast
        if (roomData.BroadcastState != BroadcastConsentState.Inactive)
        {
            // No participants to notify, just reset state
            roomData.BroadcastState = BroadcastConsentState.Inactive;
            roomData.BroadcastConsentedSessions.Clear();
            roomData.BroadcastRequestedBy = null;
            roomData.BroadcastRequestedAt = null;
        }

        // Clear any remaining participants (shouldn't be any, but be safe)
        await _endpointRegistry.ClearRoomAsync(roomId, cancellationToken);

        // Delete room data
        await roomStore.DeleteAsync($"voice:room:{roomId}", cancellationToken);

        // Delete session -> room mapping
        var stringStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.Voice);
        await stringStore.DeleteAsync($"voice:session-room:{roomData.SessionId}", cancellationToken);

        // Publish room deleted service event with Empty reason
        await messageBus.TryPublishAsync("voice.room.deleted", new VoiceRoomDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RoomId = roomId,
            Reason = VoiceRoomDeletedReason.Empty
        });
    }

    /// <summary>
    /// Auto-declines a timed-out broadcast consent request.
    /// Silence is not consent (design decision Q2).
    /// </summary>
    private async Task AutoDeclineConsentAsync(
        Guid roomId,
        VoiceRoomData roomData,
        IMessageBus messageBus,
        IClientEventPublisher clientEventPublisher,
        IPermissionClient permissionClient,
        IStateStore<VoiceRoomData> roomStore,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.voice", "ParticipantEvictionWorker.AutoDeclineConsentAsync");
        _logger.LogInformation("Auto-declining broadcast consent for room {RoomId} due to timeout", roomId);

        roomData.BroadcastState = BroadcastConsentState.Inactive;
        roomData.BroadcastConsentedSessions.Clear();
        roomData.BroadcastRequestedBy = null;
        roomData.BroadcastRequestedAt = null;
        await roomStore.SaveAsync($"voice:room:{roomId}", roomData, cancellationToken: cancellationToken);

        var participants = await _endpointRegistry.GetRoomParticipantsAsync(roomId, cancellationToken);
        var participantSessionIds = participants.Select(p => p.SessionId).ToHashSet();

        // Clear consent_pending permission states, restore to in_room
        foreach (var sessionId in participantSessionIds)
        {
            try
            {
                await permissionClient.UpdateSessionStateAsync(new SessionStateUpdate
                {
                    SessionId = sessionId,
                    ServiceId = "voice",
                    NewState = "in_room"
                }, cancellationToken);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Failed to restore voice:in_room state for session {SessionId}", sessionId);
            }
        }

        // Publish declined service event
        await messageBus.TryPublishAsync("voice.broadcast.declined", new VoiceBroadcastDeclinedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RoomId = roomId,
            DeclinedBySessionId = null
        });

        // Publish client event
        if (participantSessionIds.Count > 0)
        {
            var updateEvent = new VoiceBroadcastConsentUpdateClientEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                RoomId = roomId,
                State = BroadcastConsentState.Inactive,
                ConsentedCount = 0,
                TotalCount = participantSessionIds.Count,
                DeclinedByDisplayName = null
            };

            await clientEventPublisher.PublishToSessionsAsync(
                participantSessionIds.Select(id => id.ToString()),
                updateEvent,
                cancellationToken);
        }
    }

    /// <summary>
    /// Stops broadcast from the background worker context.
    /// </summary>
    private async Task StopBroadcastFromWorkerAsync(
        Guid roomId,
        VoiceRoomData roomData,
        VoiceBroadcastStoppedReason reason,
        IMessageBus messageBus,
        IClientEventPublisher clientEventPublisher,
        IPermissionClient permissionClient,
        IStateStore<VoiceRoomData> roomStore,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.voice", "ParticipantEvictionWorker.StopBroadcastFromWorkerAsync");
        var previousState = roomData.BroadcastState;
        roomData.BroadcastState = BroadcastConsentState.Inactive;
        roomData.BroadcastConsentedSessions.Clear();
        roomData.BroadcastRequestedBy = null;
        roomData.BroadcastRequestedAt = null;
        await roomStore.SaveAsync($"voice:room:{roomId}", roomData, cancellationToken: cancellationToken);

        // Publish stopped service event
        await messageBus.TryPublishAsync("voice.broadcast.stopped", new VoiceBroadcastStoppedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RoomId = roomId,
            Reason = reason
        });

        // Publish client event
        var participants = await _endpointRegistry.GetRoomParticipantsAsync(roomId, cancellationToken);
        var participantSessionIds = participants.Select(p => p.SessionId).ToHashSet();

        // Clear consent_pending states if they were set
        if (previousState == BroadcastConsentState.Pending)
        {
            foreach (var sessionId in participantSessionIds)
            {
                try
                {
                    await permissionClient.UpdateSessionStateAsync(new SessionStateUpdate
                    {
                        SessionId = sessionId,
                        ServiceId = "voice",
                        NewState = "in_room"
                    }, cancellationToken);
                }
                catch (ApiException ex)
                {
                    _logger.LogWarning(ex, "Failed to restore voice:in_room state for session {SessionId}", sessionId);
                }
            }
        }

        if (participantSessionIds.Count > 0)
        {
            var updateEvent = new VoiceBroadcastConsentUpdateClientEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                RoomId = roomId,
                State = BroadcastConsentState.Inactive,
                ConsentedCount = 0,
                TotalCount = participantSessionIds.Count,
                DeclinedByDisplayName = null
            };

            await clientEventPublisher.PublishToSessionsAsync(
                participantSessionIds.Select(id => id.ToString()),
                updateEvent,
                cancellationToken);
        }
    }
}
