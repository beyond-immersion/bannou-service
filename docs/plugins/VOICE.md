# Voice Plugin Deep Dive

> **Plugin**: lib-voice
> **Schema**: schemas/voice-api.yaml
> **Version**: 2.0.0
> **State Store**: voice-statestore (Redis)

---

## Overview

The Voice service (L3 AppFeatures) provides voice room coordination for P2P and scaled-tier (SFU) WebRTC communication. Supports dual room modes (persistent rooms created via API, ad-hoc rooms auto-created on join), broadcast consent flows for streaming integration, participant TTL enforcement via background worker, and automatic tier upgrade when P2P rooms exceed capacity. Integrates with Kamailio (SIP proxy) and RTPEngine (media relay) for the scaled tier. Permission-state-gated SDP exchange and broadcast consent. Internal service accessed by other services via lib-mesh.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis persistence for room data and participant registrations |
| lib-messaging (`IMessageBus`) | Service event publishing (8 event types) and error events via `TryPublishErrorAsync` |
| lib-permission (`IPermissionClient`) | Permission state management: `voice:in_room`, `voice:consent_pending`, `voice:ringing` |
| lib-connect (`IClientEventPublisher`) | Publishing WebSocket events to specific sessions (peer events, broadcast consent events) |
| Kamailio (external, config-only) | SIP proxy for scaled tier; host/port used in SIP credential generation (registrar URI). No active client integration — `IKamailioClient`/`KamailioClient` files exist but are orphaned. |
| RTPEngine (external) | Media relay for SFU voice conferencing (UDP bencode ng protocol) |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-streaming (L4, future) | Will subscribe to `voice.room.broadcast.approved` to start broadcast, `voice.room.broadcast.stopped` to stop |
| lib-stream (L3, future) | Will subscribe to `voice.room.created`/`voice.room.deleted` for stream-voice coordination |

> **Hierarchy note**: GameSession (L2) previously depended on Voice via `IVoiceClient` -- this was a hierarchy violation (L2 cannot depend on L3). The dependency has been removed. Voice now manages its own room lifecycle independently, and higher-layer services (lib-streaming at L4) will orchestrate voice-stream coordination via event subscriptions.

---

## State Storage

**Store**: `voice-statestore` (Backend: Redis, prefix: `voice`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `voice:room:{roomId}` | `VoiceRoomData` | Room configuration, broadcast consent state, room mode flags |
| `voice:session-room:{sessionId}` | `string` | Session-to-room mapping for quick lookup (stores roomId as string) |
| `voice:room:participants:{roomId}` | `List<ParticipantRegistration>` | Room participant list with endpoints and heartbeats |

**VoiceRoomData Fields** (beyond basic room config):

| Field | Type | Purpose |
|-------|------|---------|
| `BroadcastState` | `BroadcastConsentState` | Current broadcast consent state (Inactive/Pending/Approved) |
| `BroadcastRequestedBy` | `Guid?` | Session that requested broadcast consent |
| `BroadcastConsentedSessions` | `HashSet<Guid>` | Sessions that have consented to broadcasting |
| `BroadcastRequestedAt` | `DateTimeOffset?` | When the current broadcast consent request was initiated |
| `AutoCleanup` | `bool` | Whether the room auto-deletes when empty after grace period |
| `Password` | `string?` | Optional room password for access control |
| `LastParticipantLeftAt` | `DateTimeOffset?` | Timestamp for grace period tracking on empty autoCleanup rooms |

---

## Events

### Published Client Events (via IClientEventPublisher)

| Event | Trigger |
|-------|---------|
| `VoicePeerJoinedEvent` | New peer joins P2P room (includes SDP offer) |
| `VoicePeerLeftEvent` | Peer leaves room |
| `VoicePeerUpdatedEvent` | Peer sends SDP answer via `/voice/peer/answer` |
| `VoiceTierUpgradeEvent` | Room upgrades from P2P to scaled (includes SIP credentials per participant) |
| `VoiceRoomClosedEvent` | Room deleted (reason: Manual, Empty, Error) |
| `VoiceBroadcastConsentRequestEvent` | Broadcast consent requested — sent to all room participants |
| `VoiceBroadcastConsentUpdateEvent` | Broadcast consent state changed (progress, approved, declined, stopped) |

### Published Service Events (via IMessageBus)

| Topic | Event Model | Trigger |
|-------|-------------|---------|
| `voice.room.created` | `VoiceRoomCreatedEvent` | Room created (via API or ad-hoc join) |
| `voice.room.deleted` | `VoiceRoomDeletedEvent` | Room deleted (reason: Manual, Empty, Error) |
| `voice.room.tier-upgraded` | `VoiceRoomTierUpgradedEvent` | Room upgrades from P2P to scaled tier |
| `voice.participant.joined` | `VoiceParticipantJoinedEvent` | Participant joins a room |
| `voice.participant.left` | `VoiceParticipantLeftEvent` | Participant leaves a room |
| `voice.room.broadcast.approved` | `VoiceRoomBroadcastApprovedEvent` | All participants consented to broadcasting |
| `voice.room.broadcast.declined` | `VoiceRoomBroadcastDeclinedEvent` | A participant declined (or consent timed out) |
| `voice.room.broadcast.stopped` | `VoiceRoomBroadcastStoppedEvent` | Broadcasting stopped (reason: ConsentRevoked, RoomClosed, Manual, Error) |

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
| `RtpEngineTimeoutSeconds` | `VOICE_RTPENGINE_TIMEOUT_SECONDS` | `5` | Timeout in seconds for RTPEngine UDP requests (range 1-60) |
| `KamailioRequestTimeoutSeconds` | `VOICE_KAMAILIO_REQUEST_TIMEOUT_SECONDS` | `5` | Kamailio HTTP timeout |
| `SipCredentialExpirationHours` | `VOICE_SIP_CREDENTIAL_EXPIRATION_HOURS` | `24` | Hours until SIP credentials expire |
| `EvictionWorkerInitialDelaySeconds` | `VOICE_EVICTION_WORKER_INITIAL_DELAY_SECONDS` | `10` | Seconds to wait after startup before the first eviction cycle (range 0-120) |
| `ParticipantHeartbeatTimeoutSeconds` | `VOICE_PARTICIPANT_HEARTBEAT_TIMEOUT_SECONDS` | `60` | Seconds of missed heartbeats before participant is evicted |
| `ParticipantEvictionCheckIntervalSeconds` | `VOICE_PARTICIPANT_EVICTION_CHECK_INTERVAL_SECONDS` | `15` | How often background worker checks for stale participants |
| `BroadcastConsentTimeoutSeconds` | `VOICE_BROADCAST_CONSENT_TIMEOUT_SECONDS` | `30` | Seconds to wait for all participants before auto-declining |
| `AdHocRoomsEnabled` | `VOICE_AD_HOC_ROOMS_ENABLED` | `false` | If true, joining a non-existent room auto-creates it with autoCleanup |
| `EmptyRoomGracePeriodSeconds` | `VOICE_EMPTY_ROOM_GRACE_PERIOD_SECONDS` | `300` | Seconds an empty autoCleanup room persists before auto-deletion |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `VoiceService` | Scoped | Main service implementation |
| `ParticipantEvictionWorker` | Hosted | Background worker for heartbeat TTL, empty room cleanup, consent timeouts |
| `ISipEndpointRegistry` / `SipEndpointRegistry` | Singleton | Participant tracking with local ConcurrentDictionary cache + Redis persistence |
| `IP2PCoordinator` / `P2PCoordinator` | Singleton | P2P mesh topology decisions and upgrade thresholds |
| `IScaledTierCoordinator` / `ScaledTierCoordinator` | Singleton | SFU management, SIP credential generation, RTP allocation |
| `IRtpEngineClient` / `RtpEngineClient` | Singleton | UDP bencode client for RTPEngine ng protocol |
| `IPermissionClient` | (via mesh) | Permission state management: `voice:in_room`, `voice:consent_pending`, `voice:ringing` |
| `IClientEventPublisher` | (via DI) | WebSocket event delivery to sessions |
| `IEventConsumer` | (via DI) | Event consumer registration (currently no handlers) |

---

## API Endpoints (Implementation Notes)

### Room Management (internal-only, `x-permissions: []`)

| Endpoint | Notes |
|----------|-------|
| `/voice/room/create` | Creates a new voice room associated with a session. Supports `autoCleanup` and `password` options. Publishes `voice.room.created` event. Default tier is P2P, default codec is Opus. Uses `P2PMaxParticipants` from config if `maxParticipants` is 0 or unset. |
| `/voice/room/get` | Retrieves room details with live participant count from endpoint registry. Admin-only via `x-permissions`. |
| `/voice/room/join` | **Most complex endpoint.** If room not found and `AdHocRoomsEnabled`, auto-creates with `autoCleanup=true`. Password validation if room is password-protected. Multi-step join with automatic tier upgrade: (1) capacity check, (2) tier upgrade if needed, (3) register participant, (4) set `voice:in_room` + `voice:ringing` for peers, (5) fire background tier upgrade if pending, (6) return peer list with `isBroadcasting` and `broadcastState` flags. Publishes `voice.participant.joined` event. |
| `/voice/room/leave` | Unregisters participant via endpoint registry, clears `voice:in_room` permission state, notifies remaining peers with `VoicePeerLeftEvent`. If room is now empty and `autoCleanup=true`, sets `LastParticipantLeftAt` for grace period. If leaving breaks broadcast consent, stops broadcast. Publishes `voice.participant.left` event. |
| `/voice/room/delete` | Deletes room and clears all participants. If broadcasting, stops broadcast first (reason: RoomClosed). For scaled tier rooms, releases RTP server resources. Notifies all participants with `VoiceRoomClosedEvent` (reason: Manual/Empty/Error). Publishes `voice.room.deleted` event. |

### Broadcast Consent (`x-permissions: role: user, states: { voice: in_room }` or `voice: consent_pending`)

| Endpoint | Notes |
|----------|-------|
| `/voice/room/broadcast/request` | Initiates broadcast consent flow. Sets room to Pending state, sends `VoiceBroadcastConsentRequestEvent` to all participants, sets `voice:consent_pending` permission state. Requires `voice:in_room`. 409 if already Pending/Approved. |
| `/voice/room/broadcast/consent` | Responds to consent request. If declined: resets to Inactive, publishes `voice.room.broadcast.declined`. If all consented: sets Approved, publishes `voice.room.broadcast.approved`. Requires `voice:consent_pending`. |
| `/voice/room/broadcast/stop` | Stops active broadcast. Resets to Inactive, publishes `voice.room.broadcast.stopped`. Any participant can revoke. Requires `voice:in_room`. |
| `/voice/room/broadcast/status` | Returns current `BroadcastConsentStatus` (state, consented/pending session IDs, RTP endpoint). Requires `voice:in_room`. |

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

2. **RTP server pool allocation**: `AllocateRtpServerAsync` currently returns the single configured RTP server. The implementation notes "In production, this would select from a pool based on load."
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/258 -->

3. ~~**IKamailioClient methods unused**~~: **FIXED** (2026-02-11) - Removed `GetActiveDialogsAsync`, `TerminateDialogAsync`, `ReloadDispatcherAsync`, `GetStatsAsync` and all supporting JSONRPC infrastructure (models, `CallRpcAsync`, `_messageBus` dependency, `_requestId` counter). `IKamailioClient` now exposes only `IsHealthyAsync`. ~~Dead `_kamailioClient` field in `ScaledTierCoordinator`~~: **FIXED** (2026-02-11) - Removed `IKamailioClient` injection from `ScaledTierCoordinator` constructor (field was stored but never used by any method). Also removed orphaned DI registration from `VoiceServicePlugin`. `IKamailioClient.cs` and `KamailioClient.cs` still exist as source files but are now completely unreferenced — can be deleted during next cleanup pass.

4. **VoiceRoomStateEvent**: Defined in `voice-client-events.yaml` but never published by the service.
<!-- AUDIT:NEEDS_DESIGN:2026-02-11:https://github.com/beyond-immersion/bannou-service/issues/396 -->

---

## Potential Extensions

1. **RTP server pool**: Multiple RTPEngine instances with load-based allocation. *(Duplicate of Stubs #2 — tracked by #258)*
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/258 -->
2. **Room quality metrics**: Track audio quality, latency, packet loss per participant.
<!-- AUDIT:NEEDS_DESIGN:2026-02-11:https://github.com/beyond-immersion/bannou-service/issues/400 -->
3. **Recording support**: Integrate RTPEngine recording for compliance/replay.
<!-- AUDIT:NEEDS_DESIGN:2026-02-11:https://github.com/beyond-immersion/bannou-service/issues/401 -->
4. **Mute state synchronization**: Currently `IsMuted` is tracked but not synchronized across peers.
<!-- AUDIT:NEEDS_DESIGN:2026-02-11:https://github.com/beyond-immersion/bannou-service/issues/402 -->
5. **ICE trickle support**: Current implementation sends all ICE candidates in initial SDP; could support trickle ICE for faster connections.
<!-- AUDIT:NEEDS_DESIGN:2026-02-11:https://github.com/beyond-immersion/bannou-service/issues/403 -->

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

7. **Permission state race on peer leave during join**: The sequential `voice:ringing` loop in `NotifyPeerJoinedAsync` can set state on a session that left between the participant list fetch and the state update. This is benign: every `UpdateSessionStateAsync` call is try-catch wrapped (logs warning), and the leaving session's own `LeaveVoiceRoomAsync` path clears its permission state independently.

### Design Considerations (Requires Planning)

1. **RTPEngine UDP protocol**: Uses raw UDP with bencode encoding. No connection state, no retries on packet loss. `_sendLock` prevents concurrent sends but lost responses are not retried - the operation simply times out. Additionally, cookie mismatch responses (stale data from previous timed-out requests) are logged but used anyway — a correctness bug.
<!-- AUDIT:NEEDS_DESIGN:2026-02-11:https://github.com/beyond-immersion/bannou-service/issues/404 -->

2. ~~**Permission state race condition**~~: **FIXED** (2026-02-11) - Reclassified as Intentional Quirk. The sequential `voice:ringing` loop in `NotifyPeerJoinedAsync` can set state on a dead session if a peer leaves mid-loop, but every call is try-catch wrapped (LogWarning), the leaving session's cleanup path clears its own state, and no data integrity risk exists. Moved to Intentional Quirks #7.

3. **SIP credential expiration not enforced**: Credentials have a 24-hour expiration timestamp (`SipCredentialExpirationHours`) but no server-side enforcement. Clients receive the expiration but there's no background task to rotate credentials or invalidate sessions.
<!-- AUDIT:NEEDS_DESIGN:2026-02-11:https://github.com/beyond-immersion/bannou-service/issues/405 -->

4. ~~**Hardcoded fallbacks in coordinators**~~: **FIXED** (2026-02-11) - Removed secondary fallbacks from `P2PCoordinator.GetP2PMaxParticipants()` (was `?? 6`, schema default is `8`), `ScaledTierCoordinator.GetScaledMaxParticipants()` (was `?? 100`), and `ScaledTierCoordinator.GetStunServers()` (was `?? "stun:stun.l.google.com:19302"`). All three now use configuration values directly; schema defaults guarantee non-null/non-zero values.

5. ~~**Kamailio health check path assumption**~~: **MOOT** (2026-02-11) - `KamailioClient` is now orphaned code (no DI registration, no consumers). The health check path concern only applies if the client is re-activated in the future.

---

## Work Tracking

### Issues Created

| Date | Issue | Gap | Status |
|------|-------|-----|--------|
| 2026-01-31 | [#195](https://github.com/beyond-immersion/bannou-service/issues/195) | RTPEngine publish/subscribe methods unused in scaled tier | Needs Design |
| 2026-02-01 | [#258](https://github.com/beyond-immersion/bannou-service/issues/258) | RTP server pool allocation with load-based selection | Needs Design |
| 2026-02-11 | [#396](https://github.com/beyond-immersion/bannou-service/issues/396) | VoiceRoomStateEvent defined but never published — redundancy with JoinVoiceRoomResponse | Needs Design |
| 2026-02-11 | [#400](https://github.com/beyond-immersion/bannou-service/issues/400) | Room quality metrics design — metric set, sources, storage, analytics integration | Needs Design |
| 2026-02-11 | [#401](https://github.com/beyond-immersion/bannou-service/issues/401) | Voice recording support architecture — storage, consent, RTPEngine protocol, retention | Needs Design |
| 2026-02-11 | [#402](https://github.com/beyond-immersion/bannou-service/issues/402) | Mute state synchronization — self vs admin mute, SFU enforcement, notification scope | Needs Design |
| 2026-02-11 | [#403](https://github.com/beyond-immersion/bannou-service/issues/403) | ICE trickle support — relay vs accumulate, permission gating, P2P/SFU scope | Needs Design |
| 2026-02-11 | [#404](https://github.com/beyond-immersion/bannou-service/issues/404) | RTPEngine UDP client: cookie mismatch correctness bug and retry strategy | Needs Design |
| 2026-02-11 | [#405](https://github.com/beyond-immersion/bannou-service/issues/405) | SIP credential expiration enforcement — Bannou vs Kamailio, rotation strategy | Needs Design |

### Completed

| Date | Gap | Action |
|------|-----|--------|
| 2026-02-11 | IKamailioClient methods unused (Stubs #3) | Removed 4 dead JSONRPC methods and all supporting infrastructure. `IKamailioClient` reduced to `IsHealthyAsync` only. |
| 2026-02-11 | Hardcoded fallbacks in coordinators (Design Considerations #4) | Removed 3 secondary fallbacks in `P2PCoordinator` and `ScaledTierCoordinator`. Config properties have schema defaults; fallbacks were unreachable and violated IMPLEMENTATION TENETS (T21). |
| 2026-02-11 | Dead `_kamailioClient` field in ScaledTierCoordinator (Stubs #3 follow-up) | Removed `IKamailioClient` injection from `ScaledTierCoordinator` constructor, removed orphaned DI registration from `VoiceServicePlugin`, updated tests. `IKamailioClient.cs` and `KamailioClient.cs` are now fully orphaned source files. |
| 2026-02-11 | Permission state race condition (Design Considerations #2) | Reclassified as Intentional Quirk #7. Race is benign: try-catch handles dead sessions, cleanup paths are independent. No code changes needed. |
