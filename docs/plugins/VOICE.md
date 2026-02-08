# Voice Plugin Deep Dive

> **Plugin**: lib-voice
> **Schema**: schemas/voice-api.yaml
> **Version**: 1.1.0
> **State Store**: voice-statestore (Redis)

---

## Overview

The Voice service (L4 GameFeatures) provides WebRTC-based voice communication for game sessions, supporting both P2P mesh topology (small groups) and scaled tier via SFU for large rooms. Integrates with Kamailio (SIP proxy) and RTPEngine (media relay) for the scaled tier. Features automatic tier upgrade when P2P rooms exceed capacity and permission-state-gated SDP exchange. Integrated with lib-game-session for room lifecycle management.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis persistence for room data and participant registrations |
| lib-messaging (`IMessageBus`) | Error event publishing via `TryPublishErrorAsync` |
| lib-permission (`IPermissionClient`) | Setting `voice:ringing` state for SDP answer permission gating |
| lib-connect (`IClientEventPublisher`) | Publishing WebSocket events to specific sessions |
| Kamailio (external) | SIP proxy for scaled tier room management (JSON-RPC 2.0 protocol) |
| RTPEngine (external) | Media relay for SFU voice conferencing (UDP bencode ng protocol) |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-game-session | Creates/joins/leaves/deletes voice rooms during session lifecycle via `IVoiceClient` (**VIOLATION**) |

> **⚠️ SERVICE HIERARCHY VIOLATION**: GameSession (L2 Game Foundation) currently depends on Voice (L4 Game Features) via `IVoiceClient`. This is a **critical violation** - L2 services cannot depend on L4. **Remediation**: Invert the dependency. Voice (L4) should subscribe to `game-session.created` and `game-session.ended` events from GameSession (L2), automatically creating rooms when `VoiceEnabled=true` and publishing `voice.room.created`. This is the correct direction (L4 → L2). See "Service-to-service events stub" (#238) for related work on adding service events to Voice.

---

## State Storage

**Store**: `voice-statestore` (Backend: Redis, prefix: `voice`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `voice:room:{roomId}` | `VoiceRoomData` | Room configuration (tier, codec, max participants, RTP URI) |
| `voice:session-room:{sessionId}` | `string` | Session-to-room mapping for quick lookup (stores roomId as string) |
| `voice:room:participants:{roomId}` | `List<ParticipantRegistration>` | Room participant list with endpoints and heartbeats |

---

## Events

### Published Client Events (via IClientEventPublisher)

| Event | Trigger |
|-------|---------|
| `VoicePeerJoinedEvent` | New peer joins P2P room (includes SDP offer) |
| `VoicePeerLeftEvent` | Peer leaves room |
| `VoicePeerUpdatedEvent` | Peer sends SDP answer via `/voice/peer/answer` |
| `VoiceTierUpgradeEvent` | Room upgrades from P2P to scaled (includes SIP credentials per participant) |
| `VoiceRoomClosedEvent` | Room deleted (reason: session_ended, admin_action, error) |

### Published Service Events

None. The `voice-events.yaml` is a stub with empty publications (`x-event-publications: []`).

### Consumed Events

This plugin does not consume external events (`x-event-subscriptions: []`).

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `ScaledTierEnabled` | `VOICE_SCALED_TIER_ENABLED` | `false` | Enable SIP-based scaled tier |
| `TierUpgradeEnabled` | `VOICE_TIER_UPGRADE_ENABLED` | `false` | Auto-upgrade P2P to scaled when capacity exceeded |
| `TierUpgradeMigrationDeadlineMs` | `VOICE_TIER_UPGRADE_MIGRATION_DEADLINE_MS` | `30000` | Migration window in ms for clients to switch tiers |
| `P2PMaxParticipants` | `VOICE_P2P_MAX_PARTICIPANTS` | `8` | Max P2P mesh size (schema allows 2-16) |
| `ScaledMaxParticipants` | `VOICE_SCALED_MAX_PARTICIPANTS` | `100` | Max SFU room size (schema allows 1-500) |
| `StunServers` | `VOICE_STUN_SERVERS` | `"stun:stun.l.google.com:19302"` | Comma-separated STUN URLs for WebRTC |
| `SipPasswordSalt` | `VOICE_SIP_PASSWORD_SALT` | `null` (nullable) | Required if ScaledTierEnabled; shared across all instances |
| `SipDomain` | `VOICE_SIP_DOMAIN` | `"voice.bannou.local"` | SIP registration domain |
| `KamailioHost` | `VOICE_KAMAILIO_HOST` | `"localhost"` | Kamailio server address |
| `KamailioRpcPort` | `VOICE_KAMAILIO_RPC_PORT` | `5080` | Kamailio JSON-RPC port |
| `KamailioSipPort` | `VOICE_KAMAILIO_SIP_PORT` | `5060` | Kamailio SIP signaling port for client registration |
| `RtpEngineHost` | `VOICE_RTPENGINE_HOST` | `"localhost"` | RTPEngine server address |
| `RtpEnginePort` | `VOICE_RTPENGINE_PORT` | `22222` | RTPEngine ng protocol port |
| `KamailioRequestTimeoutSeconds` | `VOICE_KAMAILIO_REQUEST_TIMEOUT_SECONDS` | `5` | Kamailio HTTP timeout |
| `SipCredentialExpirationHours` | `VOICE_SIP_CREDENTIAL_EXPIRATION_HOURS` | `24` | Hours until SIP credentials expire |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `VoiceService` | Scoped | Main service implementation |
| `ISipEndpointRegistry` / `SipEndpointRegistry` | Singleton | Participant tracking with local ConcurrentDictionary cache + Redis persistence |
| `IP2PCoordinator` / `P2PCoordinator` | Singleton | P2P mesh topology decisions and upgrade thresholds |
| `IScaledTierCoordinator` / `ScaledTierCoordinator` | Singleton | SFU management, SIP credential generation, RTP allocation |
| `IKamailioClient` / `KamailioClient` | Singleton | JSON-RPC client for Kamailio SIP proxy control |
| `IRtpEngineClient` / `RtpEngineClient` | Singleton | UDP bencode client for RTPEngine ng protocol |
| `IPermissionClient` | (via mesh) | Permission state management for `voice:ringing` |
| `IClientEventPublisher` | (via DI) | WebSocket event delivery to sessions |
| `IEventConsumer` | (via DI) | Event consumer registration (currently no handlers) |

---

## API Endpoints (Implementation Notes)

### Room Management (internal-only, `x-permissions: []`)

| Endpoint | Notes |
|----------|-------|
| `/voice/room/create` | Creates room for a game session. Checks for existing room via session-room mapping (409 Conflict if exists). Default tier is P2P, default codec is Opus. Uses `P2PMaxParticipants` from config if `maxParticipants` is 0 or unset. |
| `/voice/room/get` | Retrieves room details with live participant count from endpoint registry. Admin-only via `x-permissions`. |
| `/voice/room/join` | **Most complex endpoint.** Multi-step join with automatic tier upgrade: (1) capacity check, (2) tier upgrade if needed, (3) register participant, (4) set `voice:ringing` for existing AND joining peers, (5) fire background tier upgrade if pending, (6) return peer list for P2P mesh. |
| `/voice/room/leave` | Unregisters participant via endpoint registry, notifies remaining peers with `VoicePeerLeftEvent`. |
| `/voice/room/delete` | Deletes room and clears all participants. For scaled tier rooms, releases RTP server resources first. Notifies all participants with `VoiceRoomClosedEvent`. Removes both room data and session-to-room mapping. |

### Peer Management

| Endpoint | Notes |
|----------|-------|
| `/voice/peer/heartbeat` | Updates participant heartbeat timestamp. Admin-only via `x-permissions`. |
| `/voice/peer/answer` | **Client-facing endpoint.** Called by WebSocket clients after receiving `VoicePeerJoinedEvent`. Requires `voice:ringing` permission state (role=user, states: voice:ringing). Publishes `VoicePeerUpdatedEvent` with SDP answer to target session only. |

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

1. **RTPEngine publish/subscribe**: `PublishAsync` and `SubscribeRequestAsync` are fully implemented in `RtpEngineClient` but never called by `VoiceService`. XML docs indicate these are reserved for future SFU publisher/subscriber routing.
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/195 -->

2. **Service-to-service events**: `voice-events.yaml` is a stub with empty publications/subscriptions. No domain events are published to the message bus (only error events via `TryPublishErrorAsync`).
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/238 -->

3. **RTP server pool allocation**: `AllocateRtpServerAsync` currently returns the single configured RTP server. The implementation notes "In production, this would select from a pool based on load."
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/258 -->

4. **IKamailioClient methods unused**: `GetActiveDialogsAsync`, `TerminateDialogAsync`, `ReloadDispatcherAsync`, and `GetStatsAsync` are implemented but never called by VoiceService. Only `IsHealthyAsync` is used (indirectly via `AllocateRtpServerAsync`).

5. **VoiceRoomStateEvent**: Defined in `voice-client-events.yaml` but never published by the service.

---

## Potential Extensions

1. **RTP server pool**: Multiple RTPEngine instances with load-based allocation.
2. **Participant TTL enforcement**: Background worker to expire participants with stale heartbeats.
3. **Room quality metrics**: Track audio quality, latency, packet loss per participant.
4. **Recording support**: Integrate RTPEngine recording for compliance/replay.
5. **Mute state synchronization**: Currently `IsMuted` is tracked but not synchronized across peers.
6. **ICE trickle support**: Current implementation sends all ICE candidates in initial SDP; could support trickle ICE for faster connections.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None identified.

### Intentional Quirks (Documented Behavior)

1. **SDP answer in SdpOffer field**: The `VoicePeerUpdatedEvent` reuses the `SdpOffer` field in `VoicePeerInfo` to carry the SDP answer from `/voice/peer/answer`. This is intentional - the same model represents both directions of the WebRTC handshake.

2. **Fire-and-forget tier upgrade**: `Task.Run()` for tier upgrade is not awaited in `JoinVoiceRoomAsync`. The join response returns immediately while upgrade happens in background. Errors are logged but don't affect the join response.

3. **P2P upgrade threshold is "exceeds", not "at"**: `ShouldUpgradeToScaledAsync` triggers when `currentParticipantCount > maxP2P`. A room AT capacity is still P2P; only when the next participant joins does upgrade trigger. This is deliberate to avoid unnecessary upgrades.

4. **Local cache + Redis dual storage**: `SipEndpointRegistry` maintains a local `ConcurrentDictionary` cache that is synchronized with Redis on every mutation. This is for performance but means state can be transiently inconsistent across service instances.

5. **Session-based privacy**: All participant tracking uses `sessionId` (WebSocket session) rather than `accountId` to prevent leaking account information. Display names are opt-in.

6. **Permission state set for BOTH directions**: When joining a room with existing peers, `voice:ringing` is set for both the joining session AND all existing sessions. This enables bidirectional SDP exchange.

### Design Considerations (Requires Planning)

1. **No participant TTL enforcement**: Heartbeats are tracked in `LastHeartbeat` but no background worker expires stale participants. If a client disconnects without leaving, the participant remains until room deletion.

2. **RTPEngine UDP protocol**: Uses raw UDP with bencode encoding. No connection state, no retries on packet loss. `_sendLock` prevents concurrent sends but lost responses are not retried - the operation simply times out.

3. **Permission state race condition**: Setting `voice:ringing` for existing peers happens sequentially in a loop. If a peer leaves between the check and the state update, the state is set on a dead session (benign but wasteful).

4. **SIP credential expiration not enforced**: Credentials have a 24-hour expiration timestamp (`SipCredentialExpirationHours`) but no server-side enforcement. Clients receive the expiration but there's no background task to rotate credentials or invalidate sessions.

5. **Hardcoded fallbacks in coordinators**: `P2PCoordinator` returns 6 as fallback for `P2PMaxParticipants`, `ScaledTierCoordinator` returns 100 for `ScaledMaxParticipants`. These differ from schema defaults (8 and 100). Should either throw for invalid config or use schema defaults consistently.

6. **Kamailio health check path assumption**: `KamailioClient.IsHealthyAsync` assumes a `/health` endpoint exists by replacing `/RPC` in the configured endpoint URL. This may not work for all Kamailio deployments.

---

## Work Tracking

### Issues Created

| Date | Issue | Gap | Status |
|------|-------|-----|--------|
| 2026-01-31 | [#195](https://github.com/beyond-immersion/bannou-service/issues/195) | RTPEngine publish/subscribe methods unused in scaled tier | Needs Design |
| 2026-02-01 | [#238](https://github.com/beyond-immersion/bannou-service/issues/238) | Service-to-service events stub | Needs Design |
| 2026-02-01 | [#258](https://github.com/beyond-immersion/bannou-service/issues/258) | RTP server pool allocation with load-based selection | Needs Design |
