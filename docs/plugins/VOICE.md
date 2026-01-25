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

5. **String-based tier and codec storage**: `VoiceRoomData` stores tier and codec as strings, requiring parsing in service methods. Allows future extensibility but loses type safety at the persistence layer. Changing to enum types would require model migration.

6. **Hardcoded tunable fallbacks**: `P2PCoordinator` returns 6 as fallback for `P2PMaxParticipants`, `ScaledTierCoordinator` returns 100 for `ScaledMaxParticipants` and 22222 for `RtpEnginePort` when configuration values are invalid. Should rely on schema defaults or throw for invalid configuration.

7. **Hardcoded SIP credential expiration**: `GenerateSipCredentials` uses 24-hour expiration. Should be a configuration property (`SipCredentialExpirationHours`).
