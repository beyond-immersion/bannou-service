using Dapr.Client;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Voice.Services;

/// <summary>
/// Implementation of SIP endpoint registry using Dapr state store for distributed state.
/// Uses ConcurrentDictionary for local caching while persisting to Redis via Dapr.
/// Thread-safe for multi-instance deployments (Tenet 4).
/// </summary>
public class SipEndpointRegistry : ISipEndpointRegistry
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<SipEndpointRegistry> _logger;
    private readonly VoiceServiceConfiguration _configuration;

    private const string STATE_STORE = "voice-statestore";
    private const string ROOM_PARTICIPANTS_PREFIX = "voice:room:participants:";

    /// <summary>
    /// Local cache of room participants for fast lookups.
    /// Key: roomId, Value: concurrent dictionary of accountId -> participant
    /// </summary>
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, ParticipantRegistration>> _localCache = new();

    /// <summary>
    /// Initializes a new instance of the SipEndpointRegistry.
    /// </summary>
    /// <param name="daprClient">Dapr client for state store operations.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Voice service configuration.</param>
    public SipEndpointRegistry(
        DaprClient daprClient,
        ILogger<SipEndpointRegistry> logger,
        VoiceServiceConfiguration configuration)
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <inheritdoc />
    public async Task<bool> RegisterAsync(
        Guid roomId,
        Guid accountId,
        SipEndpoint endpoint,
        string? sessionId = null,
        string? displayName = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var participant = new ParticipantRegistration
        {
            AccountId = accountId,
            SessionId = sessionId,
            DisplayName = displayName,
            Endpoint = endpoint,
            JoinedAt = now,
            LastHeartbeat = now,
            IsMuted = false
        };

        // Get or create room participant dictionary
        var roomParticipants = _localCache.GetOrAdd(roomId, _ => new ConcurrentDictionary<Guid, ParticipantRegistration>());

        // Try to add (fails if already exists)
        if (!roomParticipants.TryAdd(accountId, participant))
        {
            _logger.LogWarning("Participant {AccountId} already registered in room {RoomId}", accountId, roomId);
            return false;
        }

        // Persist to Dapr state store
        await PersistRoomParticipantsAsync(roomId, roomParticipants, cancellationToken);

        _logger.LogInformation("Registered participant {AccountId} in room {RoomId}", accountId, roomId);
        return true;
    }

    /// <inheritdoc />
    public async Task<ParticipantRegistration?> UnregisterAsync(
        Guid roomId,
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
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

        if (!roomParticipants.TryRemove(accountId, out var removed))
        {
            _logger.LogDebug("Participant {AccountId} not found in room {RoomId}", accountId, roomId);
            return null;
        }

        // Persist updated state
        await PersistRoomParticipantsAsync(roomId, roomParticipants, cancellationToken);

        _logger.LogInformation("Unregistered participant {AccountId} from room {RoomId}", accountId, roomId);
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
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        if (!_localCache.TryGetValue(roomId, out var roomParticipants))
        {
            roomParticipants = await LoadRoomParticipantsAsync(roomId, cancellationToken);
            if (roomParticipants == null)
            {
                return null;
            }
        }

        roomParticipants.TryGetValue(accountId, out var participant);
        return participant;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateHeartbeatAsync(
        Guid roomId,
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        if (!_localCache.TryGetValue(roomId, out var roomParticipants))
        {
            roomParticipants = await LoadRoomParticipantsAsync(roomId, cancellationToken);
            if (roomParticipants == null)
            {
                return false;
            }
        }

        if (!roomParticipants.TryGetValue(accountId, out var existing))
        {
            return false;
        }

        // Update heartbeat timestamp
        var updated = new ParticipantRegistration
        {
            AccountId = existing.AccountId,
            SessionId = existing.SessionId,
            DisplayName = existing.DisplayName,
            Endpoint = existing.Endpoint,
            JoinedAt = existing.JoinedAt,
            LastHeartbeat = DateTimeOffset.UtcNow,
            IsMuted = existing.IsMuted
        };

        roomParticipants[accountId] = updated;
        await PersistRoomParticipantsAsync(roomId, roomParticipants, cancellationToken);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateEndpointAsync(
        Guid roomId,
        Guid accountId,
        SipEndpoint newEndpoint,
        CancellationToken cancellationToken = default)
    {
        if (!_localCache.TryGetValue(roomId, out var roomParticipants))
        {
            roomParticipants = await LoadRoomParticipantsAsync(roomId, cancellationToken);
            if (roomParticipants == null)
            {
                return false;
            }
        }

        if (!roomParticipants.TryGetValue(accountId, out var existing))
        {
            return false;
        }

        // Update endpoint
        var updated = new ParticipantRegistration
        {
            AccountId = existing.AccountId,
            SessionId = existing.SessionId,
            DisplayName = existing.DisplayName,
            Endpoint = newEndpoint,
            JoinedAt = existing.JoinedAt,
            LastHeartbeat = DateTimeOffset.UtcNow,
            IsMuted = existing.IsMuted
        };

        roomParticipants[accountId] = updated;
        await PersistRoomParticipantsAsync(roomId, roomParticipants, cancellationToken);

        _logger.LogInformation("Updated endpoint for participant {AccountId} in room {RoomId}", accountId, roomId);
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
        await _daprClient.DeleteStateAsync(STATE_STORE, stateKey, cancellationToken: cancellationToken);

        _logger.LogInformation("Cleared room {RoomId}, removed {Count} participants", roomId, removed.Count);
        return removed;
    }

    /// <summary>
    /// Persists room participants to Dapr state store.
    /// </summary>
    private async Task PersistRoomParticipantsAsync(
        Guid roomId,
        ConcurrentDictionary<Guid, ParticipantRegistration> participants,
        CancellationToken cancellationToken)
    {
        var stateKey = $"{ROOM_PARTICIPANTS_PREFIX}{roomId}";
        var participantList = participants.Values.ToList();

        await _daprClient.SaveStateAsync(STATE_STORE, stateKey, participantList, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Loads room participants from Dapr state store into local cache.
    /// </summary>
    private async Task<ConcurrentDictionary<Guid, ParticipantRegistration>?> LoadRoomParticipantsAsync(
        Guid roomId,
        CancellationToken cancellationToken)
    {
        var stateKey = $"{ROOM_PARTICIPANTS_PREFIX}{roomId}";
        var participantList = await _daprClient.GetStateAsync<List<ParticipantRegistration>>(STATE_STORE, stateKey, cancellationToken: cancellationToken);

        if (participantList == null || participantList.Count == 0)
        {
            return null;
        }

        var dict = new ConcurrentDictionary<Guid, ParticipantRegistration>();
        foreach (var p in participantList)
        {
            dict[p.AccountId] = p;
        }

        // Cache locally
        _localCache[roomId] = dict;
        return dict;
    }
}
