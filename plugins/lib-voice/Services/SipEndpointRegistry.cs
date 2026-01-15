using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Voice.Services;

/// <summary>
/// Implementation of SIP endpoint registry using lib-state for distributed state.
/// Uses ConcurrentDictionary for local caching while persisting to Redis via lib-state.
/// Thread-safe for multi-instance deployments (FOUNDATION TENETS).
/// Participants are keyed by sessionId to support multiple connections from the same account.
/// </summary>
public class SipEndpointRegistry : ISipEndpointRegistry
{
    private readonly IStateStore<List<ParticipantRegistration>> _stateStore;
    private readonly ILogger<SipEndpointRegistry> _logger;
    private readonly VoiceServiceConfiguration _configuration;

    private const string ROOM_PARTICIPANTS_PREFIX = "voice:room:participants:";

    /// <summary>
    /// Local cache of room participants for fast lookups.
    /// Key: roomId, Value: concurrent dictionary of sessionId -> participant
    /// </summary>
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, ParticipantRegistration>> _localCache = new();

    /// <summary>
    /// Initializes a new instance of the SipEndpointRegistry.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for state operations.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Voice service configuration.</param>
    public SipEndpointRegistry(
        IStateStoreFactory stateStoreFactory,
        ILogger<SipEndpointRegistry> logger,
        VoiceServiceConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(stateStoreFactory);
        _stateStore = stateStoreFactory.GetStore<List<ParticipantRegistration>>(StateStoreDefinitions.Voice);
        _logger = logger;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public async Task<bool> RegisterAsync(
        Guid roomId,
        string sessionId,
        SipEndpoint endpoint,
        string? displayName = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            throw new ArgumentException("SessionId is required", nameof(sessionId));
        }

        var now = DateTimeOffset.UtcNow;
        var participant = new ParticipantRegistration
        {
            SessionId = sessionId,
            DisplayName = displayName,
            Endpoint = endpoint,
            JoinedAt = now,
            LastHeartbeat = now,
            IsMuted = false
        };

        // Load existing participants from state store if not in local cache
        // Required for multi-instance safety (FOUNDATION TENETS) - another instance may have participants we don't have locally
        if (!_localCache.TryGetValue(roomId, out var roomParticipants))
        {
            // Try to load from state store
            roomParticipants = await LoadRoomParticipantsAsync(roomId, cancellationToken);
            if (roomParticipants == null)
            {
                // Room doesn't exist yet, create new dictionary
                roomParticipants = new ConcurrentDictionary<string, ParticipantRegistration>();
                _localCache[roomId] = roomParticipants;
            }
        }

        // Try to add (fails if already exists)
        if (!roomParticipants.TryAdd(sessionId, participant))
        {
            _logger.LogWarning("Session {SessionId} already registered in room {RoomId}", sessionId, roomId);
            return false;
        }

        // Persist to state store
        await PersistRoomParticipantsAsync(roomId, roomParticipants, cancellationToken);

        _logger.LogInformation("Registered session {SessionId} in room {RoomId}", sessionId, roomId);
        return true;
    }

    /// <inheritdoc />
    public async Task<ParticipantRegistration?> UnregisterAsync(
        Guid roomId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return null;
        }

        if (!_localCache.TryGetValue(roomId, out var roomParticipants))
        {
            // Try loading from state store
            roomParticipants = await LoadRoomParticipantsAsync(roomId, cancellationToken);
            if (roomParticipants == null)
            {
                _logger.LogDebug("Room {RoomId} not found", roomId);
                return null;
            }
        }

        if (!roomParticipants.TryRemove(sessionId, out var removed))
        {
            _logger.LogDebug("Session {SessionId} not found in room {RoomId}", sessionId, roomId);
            return null;
        }

        // Persist updated state
        await PersistRoomParticipantsAsync(roomId, roomParticipants, cancellationToken);

        _logger.LogInformation("Unregistered session {SessionId} from room {RoomId}", sessionId, roomId);
        return removed;
    }

    /// <inheritdoc />
    public async Task<List<ParticipantRegistration>> GetRoomParticipantsAsync(
        Guid roomId,
        CancellationToken cancellationToken = default)
    {
        if (!_localCache.TryGetValue(roomId, out var roomParticipants))
        {
            // Try loading from state store
            roomParticipants = await LoadRoomParticipantsAsync(roomId, cancellationToken);
            if (roomParticipants == null)
            {
                return new List<ParticipantRegistration>();
            }
        }

        return roomParticipants.Values.ToList();
    }

    /// <inheritdoc />
    public async Task<ParticipantRegistration?> GetParticipantAsync(
        Guid roomId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return null;
        }

        if (!_localCache.TryGetValue(roomId, out var roomParticipants))
        {
            roomParticipants = await LoadRoomParticipantsAsync(roomId, cancellationToken);
            if (roomParticipants == null)
            {
                return null;
            }
        }

        roomParticipants.TryGetValue(sessionId, out var participant);
        return participant;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateHeartbeatAsync(
        Guid roomId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return false;
        }

        if (!_localCache.TryGetValue(roomId, out var roomParticipants))
        {
            roomParticipants = await LoadRoomParticipantsAsync(roomId, cancellationToken);
            if (roomParticipants == null)
            {
                return false;
            }
        }

        if (!roomParticipants.TryGetValue(sessionId, out var existing))
        {
            return false;
        }

        // Update heartbeat timestamp
        var updated = new ParticipantRegistration
        {
            SessionId = existing.SessionId,
            DisplayName = existing.DisplayName,
            Endpoint = existing.Endpoint,
            JoinedAt = existing.JoinedAt,
            LastHeartbeat = DateTimeOffset.UtcNow,
            IsMuted = existing.IsMuted
        };

        roomParticipants[sessionId] = updated;
        await PersistRoomParticipantsAsync(roomId, roomParticipants, cancellationToken);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateEndpointAsync(
        Guid roomId,
        string sessionId,
        SipEndpoint newEndpoint,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return false;
        }

        if (!_localCache.TryGetValue(roomId, out var roomParticipants))
        {
            roomParticipants = await LoadRoomParticipantsAsync(roomId, cancellationToken);
            if (roomParticipants == null)
            {
                return false;
            }
        }

        if (!roomParticipants.TryGetValue(sessionId, out var existing))
        {
            return false;
        }

        // Update endpoint
        var updated = new ParticipantRegistration
        {
            SessionId = existing.SessionId,
            DisplayName = existing.DisplayName,
            Endpoint = newEndpoint,
            JoinedAt = existing.JoinedAt,
            LastHeartbeat = DateTimeOffset.UtcNow,
            IsMuted = existing.IsMuted
        };

        roomParticipants[sessionId] = updated;
        await PersistRoomParticipantsAsync(roomId, roomParticipants, cancellationToken);

        _logger.LogInformation("Updated endpoint for session {SessionId} in room {RoomId}", sessionId, roomId);
        return true;
    }

    /// <inheritdoc />
    public async Task<int> GetParticipantCountAsync(
        Guid roomId,
        CancellationToken cancellationToken = default)
    {
        if (!_localCache.TryGetValue(roomId, out var roomParticipants))
        {
            roomParticipants = await LoadRoomParticipantsAsync(roomId, cancellationToken);
            if (roomParticipants == null)
            {
                return 0;
            }
        }

        return roomParticipants.Count;
    }

    /// <inheritdoc />
    public async Task<List<ParticipantRegistration>> ClearRoomAsync(
        Guid roomId,
        CancellationToken cancellationToken = default)
    {
        List<ParticipantRegistration> removed = new();

        if (_localCache.TryRemove(roomId, out var roomParticipants))
        {
            removed = roomParticipants.Values.ToList();
        }

        // Delete from state store
        var stateKey = $"{ROOM_PARTICIPANTS_PREFIX}{roomId}";
        await _stateStore.DeleteAsync(stateKey, cancellationToken);

        _logger.LogInformation("Cleared room {RoomId}, removed {Count} participants", roomId, removed.Count);
        return removed;
    }

    /// <summary>
    /// Persists room participants to state store.
    /// </summary>
    private async Task PersistRoomParticipantsAsync(
        Guid roomId,
        ConcurrentDictionary<string, ParticipantRegistration> participants,
        CancellationToken cancellationToken)
    {
        var stateKey = $"{ROOM_PARTICIPANTS_PREFIX}{roomId}";
        var participantList = participants.Values.ToList();

        await _stateStore.SaveAsync(stateKey, participantList, null, cancellationToken);
    }

    /// <summary>
    /// Loads room participants from state store into local cache.
    /// </summary>
    private async Task<ConcurrentDictionary<string, ParticipantRegistration>?> LoadRoomParticipantsAsync(
        Guid roomId,
        CancellationToken cancellationToken)
    {
        var stateKey = $"{ROOM_PARTICIPANTS_PREFIX}{roomId}";
        var participantList = await _stateStore.GetAsync(stateKey, cancellationToken);

        if (participantList == null || participantList.Count == 0)
        {
            return null;
        }

        var dict = new ConcurrentDictionary<string, ParticipantRegistration>();
        foreach (var p in participantList)
        {
            dict[p.SessionId] = p;
        }

        // Cache locally
        _localCache[roomId] = dict;
        return dict;
    }
}
