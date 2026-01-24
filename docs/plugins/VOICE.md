# Voice Plugin Deep Dive

> **Plugin**: lib-voice
> **Schema**: schemas/voice-api.yaml
> **Version**: 1.1.0
> **State Store**: voice-statestore (Redis)

---

## Overview

The Voice service provides WebRTC-based voice communication for game sessions, supporting both P2P mesh topology (small groups) and scaled tier via SFU (Selective Forwarding Unit) for large rooms. Integrates with Kamailio (SIP proxy) and RTPEngine (media relay) for the scaled tier. Features automatic tier upgrade when P2P rooms exceed capacity, permission-state-gated SDP answer exchange, and session-based participant tracking for privacy.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis persistence for room data and participant registrations |
| lib-messaging (`IMessageBus`) | Error event publishing |
| lib-permission (`IPermissionClient`) | Setting `voice:ringing` state for SDP answer permission gating |
| lib-connect (`IClientEventPublisher`) | Publishing WebSocket events to specific sessions |
| Kamailio (external) | SIP proxy for scaled tier room management |
| RTPEngine (external) | Media relay for SFU voice conferencing |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-game-session | Creates/joins/leaves/deletes voice rooms during session lifecycle |

---

## State Storage

**Store**: `voice-statestore` (Backend: Redis, prefix: `voice`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `voice:room:{roomId}` | `VoiceRoomData` | Room configuration (tier, codec, max participants, RTP URI) |
| `voice:session-room:{sessionId}` | `string` | Session-to-room mapping for quick lookup |
| `voice:participants:{roomId}` | `List<ParticipantRegistration>` | Room participant list with endpoints and heartbeats |

---

## Events

### Published Client Events (via IClientEventPublisher)

| Event | Trigger |
|-------|---------|
| `VoicePeerJoinedEvent` | New peer joins P2P room (includes SDP offer) |
| `VoicePeerLeftEvent` | Peer leaves room |
| `VoicePeerUpdatedEvent` | Peer sends SDP answer |
| `VoiceTierUpgradeEvent` | Room upgrades from P2P to scaled (includes SIP credentials) |
| `VoiceRoomClosedEvent` | Room deleted (reason: session_ended, admin_action, error) |

### Published Service Events

None. The voice-events.yaml is a stub with empty publications.

### Consumed Events

This plugin does not consume external events.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `ScaledTierEnabled` | `VOICE_SCALED_TIER_ENABLED` | `false` | Enable SIP-based scaled tier |
| `TierUpgradeEnabled` | `VOICE_TIER_UPGRADE_ENABLED` | `false` | Auto-upgrade P2P to scaled |
| `TierUpgradeMigrationDeadlineMs` | `VOICE_TIER_UPGRADE_MIGRATION_DEADLINE_MS` | `30000` | Migration window in ms |
| `P2PMaxParticipants` | `VOICE_P2P_MAX_PARTICIPANTS` | `8` | Max P2P mesh size |
| `ScaledMaxParticipants` | `VOICE_SCALED_MAX_PARTICIPANTS` | `100` | Max SFU room size |
| `StunServers` | `VOICE_STUN_SERVERS` | `"stun:stun.l.google.com:19302"` | Comma-separated STUN URLs |
| `SipPasswordSalt` | `VOICE_SIP_PASSWORD_SALT` | `null` | Required if ScaledTierEnabled |
| `SipDomain` | `VOICE_SIP_DOMAIN` | `"voice.bannou.local"` | SIP registration domain |
| `KamailioHost` | `VOICE_KAMAILIO_HOST` | `"localhost"` | Kamailio server address |
| `KamailioRpcPort` | `VOICE_KAMAILIO_RPC_PORT` | `5080` | Kamailio JSON-RPC port |
| `RtpEngineHost` | `VOICE_RTPENGINE_HOST` | `"localhost"` | RTPEngine server address |
| `RtpEnginePort` | `VOICE_RTPENGINE_PORT` | `22222` | RTPEngine ng protocol port |
| `KamailioRequestTimeoutSeconds` | `VOICE_KAMAILIO_REQUEST_TIMEOUT_SECONDS` | `5` | Kamailio HTTP timeout |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `VoiceService` | Scoped | Main service implementation |
| `ISipEndpointRegistry` | Singleton | Participant tracking with local cache + Redis |
| `IP2PCoordinator` | Singleton | P2P mesh topology decisions |
| `IScaledTierCoordinator` | Singleton | SFU management, SIP credentials, RTP allocation |
| `IKamailioClient` | Singleton | JSON-RPC client for Kamailio SIP proxy |
| `IRtpEngineClient` | Singleton | UDP bencode client for RTPEngine ng protocol |
| `IPermissionClient` | (via mesh) | Permission state management |
| `IClientEventPublisher` | (via DI) | WebSocket event delivery |

Service lifetime is **Scoped** (per-request).

---

## API Endpoints (Implementation Notes)

### Create Room (`/voice/room/create`)

Creates voice room for a game session. Checks for existing room (prevents duplicates via 409 Conflict). Default tier is P2P, default codec is Opus. Stores room data and session-to-room mapping in Redis.

### Get Room (`/voice/room/get`)

Retrieves room details with live participant count from endpoint registry. Returns NotFound if room doesn't exist.

### Join Room (`/voice/room/join`) - Most Complex

Multi-step join with automatic tier upgrade:
1. **Capacity check**: If P2P full and scaled disabled, returns error. If P2P full and scaled enabled, triggers background upgrade.
2. **Register participant**: Adds to endpoint registry (409 if already joined).
3. **Permission state**: Sets `voice:ringing` on all OTHER participants (enables them to call `/voice/peer/answer`).
4. **Tier upgrade**: If needed, fires `Task.Run()` background upgrade (fire-and-forget).
5. **Response**: Returns peer list for P2P mesh signaling.

Uses `sessionId` (not accountId) for all participant tracking - privacy benefit.

### Leave Room (`/voice/room/leave`)

Unregisters participant, notifies remaining peers with `VoicePeerLeftEvent`.

### Delete Room (`/voice/room/delete`)

Deletes room and clears all participants. For scaled tier rooms, releases RTP server resources first. Notifies all participants with `VoiceRoomClosedEvent`. Removes both room data and session-to-room mapping.

### Peer Heartbeat (`/voice/peer/heartbeat`)

Updates participant heartbeat timestamp for TTL-based expiration tracking.

### Answer Peer (`/voice/peer/answer`) - Client-Facing

Called by WebSocket clients after receiving `VoicePeerJoinedEvent`. Requires `voice:ringing` permission state. Publishes `VoicePeerUpdatedEvent` with SDP answer to the target session only.

---

## Visual Aid

```
Voice Communication Flow (P2P → Scaled Upgrade)
=================================================

  Client A                    Voice Service                   Client B
  ========                    =============                   ========

  JoinRoom ──────────────────► Register A
                                Set voice:ringing on B ─────► [state updated]
                                Send VoicePeerJoinedEvent ──► Receives SDP offer
                                                              from A
                              ◄──────────────────────────────  AnswerPeer
                                Send VoicePeerUpdatedEvent
  Receives SDP ◄───────────────  (A's answer)
  answer from B

  ... P2P mesh established ...

  Client C joins (exceeds P2PMaxParticipants)
  JoinRoom ──────────────────► Register C
                                ShouldUpgrade? YES
                                ┌─────────────────────┐
                                │ Background Task:    │
                                │ AllocateRtpServer() │
                                │ Update room tier    │
                                │ For each participant│
                                │   GenSipCredentials │
                                │   Send TierUpgrade  │
                                └─────────────────────┘
  TierUpgradeEvent ◄────────────────────────────────────────► TierUpgradeEvent
  (SIP creds + RTP URI)                                       (SIP creds + RTP URI)

  ... All clients switch to SFU mode ...
```

---

## Stubs & Unimplemented Features

1. **RTPEngine publish/subscribe**: `PublishAsync` and `SubscribeRequestAsync` are implemented in `RtpEngineClient` but never called by `VoiceService`. Reserved for future SFU publisher/subscriber routing.
2. **Service-to-service events**: `voice-events.yaml` is a stub with empty publications. No domain events are published to the message bus.
3. **RTP server pool allocation**: `AllocateRtpServerAsync` currently returns the single configured RTP server. Future: pool-based selection by load.

---

## Potential Extensions

1. **RTP server pool**: Multiple RTPEngine instances with load-based allocation.
2. **Participant TTL enforcement**: Background worker to expire participants with stale heartbeats.
3. **Room quality metrics**: Track audio quality, latency, packet loss per participant.
4. **Recording support**: Integrate RTPEngine recording for compliance/replay.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None identified.

### Intentional Quirks (Documented Behavior)

1. **SDP answer in SdpOffer field**: The `VoicePeerUpdatedEvent` reuses the `SdpOffer` field to carry the SDP answer from `/voice/peer/answer`. Not a bug - intentional field reuse for the asymmetric WebRTC handshake (offer from joiner, answer from existing peer).

2. **SessionId-based tracking (not AccountId)**: All participant tracking uses WebSocket session IDs. Supports multiple simultaneous connections per account and prevents account ID enumeration through voice room queries.

3. **Fire-and-forget tier upgrade**: `Task.Run()` for tier upgrade is not awaited. The join response returns immediately while upgrade happens in background. Errors are logged but don't affect the join response.

4. **Kamailio snake_case JSON**: `KamailioClient` uses a custom `snake_case` JSON naming policy instead of `BannouJson.Options`. Intentional: Kamailio's JSON-RPC API uses snake_case conventions.

5. **Local cache + Redis hybrid in SipEndpointRegistry**: Maintains `ConcurrentDictionary` local cache with Redis as distributed truth. Loads from Redis on cache miss. Handles multi-instance scenarios but cache can be briefly stale.

6. **P2P upgrade threshold is "exceeds", not "at"**: `ShouldUpgradeToScaledAsync` triggers when `currentParticipantCount > maxP2P`. A room AT capacity is still P2P; only when the next participant joins does upgrade trigger.

7. **Defensive null coalescing for Kamailio responses**: External API responses use `?? string.Empty` with explanatory comments. Valid per TENETS for third-party service data where null responses are possible.

### Design Considerations (Requires Planning)

1. **No participant TTL enforcement**: Heartbeats are tracked but no background worker expires stale participants. If a client disconnects without leaving, the participant remains until room deletion.

2. **RTPEngine UDP protocol**: Uses raw UDP with bencode encoding. No connection state, no retries on packet loss. `_sendLock` prevents concurrent sends but lost responses are not retried.

3. **Permission state race condition**: Setting `voice:ringing` for existing peers happens sequentially. If a peer leaves between the check and the state update, the state is set on a dead session (benign but wasteful).

4. **SIP credential expiration not enforced**: Credentials have a 24-hour expiration timestamp but no server-side enforcement. Clients receive the expiration but there's no background task to rotate credentials.

5. **String-based tier and codec storage**: `VoiceRoomData` stores tier and codec as strings, requiring parsing in service methods. Allows future extensibility but loses type safety at the persistence layer.

---

## Tenet Violations (Audit)

> **Audit Date**: 2026-01-24
> **Auditor**: Claude Opus 4.5 (Tenet Compliance Audit)

### Category: IMPLEMENTATION

1. **T10 Logging Standards - Wrong Log Level for Operation Entry** - VoiceService.cs:81 - Operation entry logged at Information instead of Debug
   - What's wrong: `CreateVoiceRoomAsync` logs "Creating voice room for session {SessionId}" at LogInformation. Per T10, operation entry should be Debug level.
   - Fix: Change `_logger.LogInformation("Creating voice room for session {SessionId}"` to `_logger.LogDebug`.

2. **T10 Logging Standards - Wrong Log Level for Operation Entry** - VoiceService.cs:205 - Operation entry logged at Information instead of Debug
   - What's wrong: `JoinVoiceRoomAsync` logs "Session {SessionId} joining voice room {RoomId}" at LogInformation. Per T10, operation entry should be Debug level.
   - Fix: Change `_logger.LogInformation("Session {SessionId} joining voice room"` to `_logger.LogDebug`.

3. **T10 Logging Standards - Wrong Log Level for Operation Entry** - VoiceService.cs:398 - Operation entry logged at Information instead of Debug
   - What's wrong: `LeaveVoiceRoomAsync` logs "Session {SessionId} leaving voice room {RoomId}" at LogInformation. Per T10, operation entry should be Debug level.
   - Fix: Change `_logger.LogInformation("Session {SessionId} leaving voice room"` to `_logger.LogDebug`.

4. **T10 Logging Standards - Wrong Log Level for Operation Entry** - VoiceService.cs:443 - Operation entry logged at Information instead of Debug
   - What's wrong: `DeleteVoiceRoomAsync` logs "Deleting voice room {RoomId}" at LogInformation. Per T10, operation entry should be Debug level.
   - Fix: Change `_logger.LogInformation("Deleting voice room {RoomId}"` to `_logger.LogDebug`.

5. **T10 Logging Standards - Wrong Log Level for Operation Entry** - VoiceService.cs:936 - Permission registration logged at Information instead of Debug
   - What's wrong: `RegisterServicePermissionsAsync` logs "Registering Voice service permissions..." at LogInformation. This is an operation entry, should be Debug.
   - Fix: Change `_logger.LogInformation("Registering Voice service permissions..."` to `_logger.LogDebug`.

6. **T21 Configuration-First - Hardcoded Tunable Fallback** - P2PCoordinator.cs:107 - Hardcoded default of 6 for P2PMaxParticipants
   - What's wrong: `GetP2PMaxParticipants()` returns `6` as fallback when configuration value is <= 0. The schema already defines a default of `8`, so this should either use the schema default or throw if configuration is invalid.
   - Fix: Remove the fallback and rely on the schema default, or throw `InvalidOperationException` if `P2PMaxParticipants <= 0` to indicate invalid configuration.

7. **T21 Configuration-First - Hardcoded Tunable Fallback** - ScaledTierCoordinator.cs:68 - Hardcoded default of 100 for ScaledMaxParticipants
   - What's wrong: `GetScaledMaxParticipants()` returns `100` as fallback when configuration value is <= 0. The schema already defines a default of `100`, so this should either use the schema default or throw if configuration is invalid.
   - Fix: Remove the fallback and rely on the schema default, or throw `InvalidOperationException` if `ScaledMaxParticipants <= 0` to indicate invalid configuration.

8. **T21 Configuration-First - Hardcoded Tunable** - ScaledTierCoordinator.cs:94 - Hardcoded 24-hour SIP credential expiration
   - What's wrong: `GenerateSipCredentials` uses `DateTimeOffset.UtcNow.AddHours(24)` for credential expiration. This is a tunable value that should be defined in configuration.
   - Fix: Add `SipCredentialExpirationHours` to voice-configuration.yaml with default of 24, then use `_configuration.SipCredentialExpirationHours` in the code.

9. **T21 Configuration-First - Hardcoded Tunable Fallback** - ScaledTierCoordinator.cs:149 - Hardcoded fallback port 22222 for RtpEnginePort
   - What's wrong: `AllocateRtpServerAsync` uses `22222` as fallback when `RtpEnginePort <= 0`. The schema already defines this default.
   - Fix: Remove the fallback ternary and rely on the schema default, or throw if configuration is invalid.

10. **T23 Async Method Pattern - Non-async Task-returning method** - P2PCoordinator.cs:58 - `ShouldUpgradeToScaledAsync` uses `await Task.CompletedTask` pattern but could be simplified
    - What's wrong: The method is marked async and does `await Task.CompletedTask` which is technically compliant but the interface requires `Task<bool>`. Since the implementation is synchronous, this is acceptable per T23 but the `await Task.CompletedTask` should come after the synchronous logic.
    - Note: This is borderline - the current implementation is technically compliant per the "Synchronous Implementation of Async Interface" section of T23.

11. **T23 Async Method Pattern - Non-async Task-returning method** - P2PCoordinator.cs:80 - `CanAcceptNewParticipantAsync` uses `await Task.CompletedTask` pattern
    - What's wrong: Same as above - technically compliant but implementation is synchronous.
    - Note: This is borderline compliant per T23.

12. **T23 Async Method Pattern - Non-async Task-returning method** - P2PCoordinator.cs:111 - `BuildP2PConnectionInfoAsync` uses `await Task.CompletedTask` pattern
    - What's wrong: Same as above - technically compliant but implementation is synchronous.
    - Note: This is borderline compliant per T23.

13. **T23 Async Method Pattern - Non-async Task-returning method** - ScaledTierCoordinator.cs:44 - `CanAcceptNewParticipantAsync` uses `await Task.CompletedTask` pattern
    - What's wrong: Same as above - technically compliant but implementation is synchronous.
    - Note: This is borderline compliant per T23.

14. **T23 Async Method Pattern - Non-async Task-returning method** - ScaledTierCoordinator.cs:111 - `BuildScaledConnectionInfoAsync` uses `await Task.CompletedTask` pattern
    - What's wrong: Same as above - technically compliant but implementation is synchronous.
    - Note: This is borderline compliant per T23.

15. **T25 Internal Model Type Safety - String for Enum** - VoiceRoomState.cs:22-27 - `VoiceRoomData.Tier` and `VoiceRoomData.Codec` are strings instead of enums
    - What's wrong: `Tier` is stored as `string` (values: "p2p", "scaled") instead of using the `VoiceTier` enum. `Codec` is stored as `string` (values: "opus", "g711", "g722") instead of using the `VoiceCodec` enum. This requires parsing throughout the service methods and loses compile-time type safety.
    - Fix: Change `Tier` to `VoiceTier` type and `Codec` to `VoiceCodec` type in the `VoiceRoomData` class. Update all usages to assign enums directly instead of strings, and remove the `ParseVoiceTier`/`ParseVoiceCodec` helper methods from service code (parsing only needed at external boundaries).

16. **T25 Internal Model Type Safety - Enum.Parse in Business Logic** - VoiceService.cs:104-105 - String to enum conversion when creating room data
    - What's wrong: In `CreateVoiceRoomAsync`, tier and codec are converted to strings using ternary expressions (`body.PreferredTier == VoiceTier.Scaled ? "scaled" : "p2p"`). This should assign the enum directly to the model.
    - Fix: After fixing VoiceRoomData to use enum types, change to `Tier = body.PreferredTier == VoiceTier.Scaled ? VoiceTier.Scaled : VoiceTier.P2p` and similar for codec.

17. **T25 Internal Model Type Safety - String Comparison for Enum** - VoiceService.cs:221 - String comparison for tier check
    - What's wrong: Uses `roomData.Tier?.ToLowerInvariant() == "scaled"` to check tier. Should use enum equality.
    - Fix: After fixing VoiceRoomData to use enum types, change to `roomData.Tier == VoiceTier.Scaled`.

18. **T25 Internal Model Type Safety - String Comparison for Enum** - VoiceService.cs:465 - String comparison for tier check
    - What's wrong: Uses `roomData.Tier?.ToLowerInvariant() == "scaled"` to check tier in DeleteVoiceRoomAsync. Should use enum equality.
    - Fix: After fixing VoiceRoomData to use enum types, change to `roomData.Tier == VoiceTier.Scaled`.

19. **T25 Internal Model Type Safety - String Assignment for Enum** - VoiceService.cs:864-865 - String assignment in TryUpgradeToScaledTierAsync
    - What's wrong: `Tier = "scaled"` and `Codec = roomData.Codec` assigns strings. Should use enum types directly.
    - Fix: After fixing VoiceRoomData to use enum types, change to `Tier = VoiceTier.Scaled`.

### Category: FOUNDATION

20. **T6 Service Implementation Pattern - Missing Constructor Null Checks** - SipEndpointRegistry.cs:33-41 - Constructor does not validate parameters
    - What's wrong: The constructor accepts `IStateStoreFactory`, `ILogger<SipEndpointRegistry>`, and `VoiceServiceConfiguration` but does not validate them. Per T6, NRT-protected parameters don't need explicit null checks, but the assignment from `stateStoreFactory.GetStore<>()` should be validated since it could theoretically return null.
    - Note: This is borderline - NRT protects the parameters, but the result of `GetStore<>()` is not null-checked.

21. **T6 Service Implementation Pattern - Missing Constructor Null Checks** - P2PCoordinator.cs:21-29 - Constructor does not validate parameters
    - What's wrong: Same as above - NRT protects parameters but no validation on assignments.
    - Note: Borderline compliant due to NRT.

22. **T6 Service Implementation Pattern - Missing Constructor Null Checks** - ScaledTierCoordinator.cs:29-41 - Constructor does not validate parameters
    - What's wrong: Same as above - NRT protects parameters but no validation on assignments.
    - Note: Borderline compliant due to NRT.

23. **T6 Service Implementation Pattern - Missing Constructor Null Checks** - KamailioClient.cs:39-52 - Constructor does not validate parameters
    - What's wrong: Constructor accepts `HttpClient`, `string host`, `int port`, `TimeSpan requestTimeout`, `ILogger<KamailioClient>`, and `IMessageBus`. The host/port are used to construct `_rpcEndpoint` but `host` is not validated for null/empty.
    - Fix: Add validation at the start of the constructor: `ArgumentException.ThrowIfNullOrEmpty(host, nameof(host));`

24. **T6 Service Implementation Pattern - Missing Constructor Null Checks** - RtpEngineClient.cs:33-68 - Constructor validates host but not other parameters
    - What's wrong: Constructor validates `host` is not null/empty but does not validate `logger` or `messageBus`.
    - Note: NRT protects these parameters at compile time, so this is borderline compliant.

### Category: QUALITY

25. **T10 Logging Standards - Wrong Log Level** - SipEndpointRegistry.cs:86 - Registration success logged at Information
    - What's wrong: "Registered session {SessionId} in room {RoomId}" is logged at Information. This is a business decision/state change, so Information is actually correct.
    - Note: This is actually compliant - business decisions should be at Information level.

26. **T10 Logging Standards - Wrong Log Level** - SipEndpointRegistry.cs:116 - Unregistration success logged at Information
    - What's wrong: "Unregistered session {SessionId} from room {RoomId}" is logged at Information. This is a business decision/state change, so Information is correct.
    - Note: This is actually compliant.

27. **T10 Logging Standards - Wrong Log Level** - SipEndpointRegistry.cs:229 - Endpoint update logged at Information
    - What's wrong: "Updated endpoint for session {SessionId} in room {RoomId}" logged at Information. This is a state change, so Information is correct.
    - Note: This is actually compliant.

### Summary of Critical Violations

**Must Fix (High Priority):**
1. T25 violations (5 instances) - `VoiceRoomData` uses strings for enums, causing fragile string comparisons throughout the codebase
2. T10 violations (5 instances) - Operation entry logs at Information instead of Debug
3. T21 violations (4 instances) - Hardcoded tunables that should be in configuration

**Should Fix (Medium Priority):**
1. T6 violations - Constructor parameter validation (though NRT provides compile-time safety)

**Acceptable/Borderline:**
1. T23 `await Task.CompletedTask` pattern - technically compliant per the documented exception for synchronous implementations of async interfaces

### Notes

- The plugin correctly uses `IClientEventPublisher` for WebSocket client events (T17 compliant)
- The plugin correctly uses `IMessageBus.TryPublishErrorAsync` for error events (T7 compliant)
- The plugin correctly uses `IStateStoreFactory` and `StateStoreDefinitions` (T4 compliant)
- The plugin correctly uses `ConcurrentDictionary` for local caches (T9 compliant)
- The plugin correctly uses `BannouJson` options are NOT needed for Kamailio (documented intentional exception for external API)
- No null-forgiving operators (`!`) found in the codebase
- No `?? string.Empty` violations found (all instances are for external Kamailio API responses with proper comments)
- No tenet numbers referenced in source code comments (T0 compliant - uses "FOUNDATION TENETS" category name)
