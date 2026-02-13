# Voice Plugin Deep Dive

> **Plugin**: lib-voice
> **Schema**: schemas/voice-api.yaml
> **Version**: 2.0.0
> **State Store**: voice-statestore (Redis)
> **Planning**: [VOICE-STREAMING.md](../planning/VOICE-STREAMING.md), [STREAMING-ARCHITECTURE.md](../planning/STREAMING-ARCHITECTURE.md)

## Overview

Voice room coordination service (L3 AppFeatures) providing pure voice rooms as a platform primitive: P2P mesh topology for small groups, Kamailio/RTPEngine-based SFU for larger rooms, automatic tier upgrade, WebRTC SDP signaling, broadcast consent flows for streaming integration, and participant TTL enforcement via background worker. Agnostic to games, sessions, and subscriptions -- voice rooms are generic containers identified by Connect/Auth session IDs.

**The three-service principle**: Voice is one of three services that together create a complete voice, streaming, and audience metagame stack. Each delivers value independently. lib-voice provides voice chat whether or not anyone is streaming. lib-broadcast (L3) can broadcast game content to Twitch whether or not voice is involved. lib-showtime (L4) provides a complete audience simulation metagame whether or not real platforms or voice rooms exist. They compose beautifully but never require each other.

**Composability**: Voice room primitives are owned here. RTMP broadcast output is lib-broadcast (L3). Game session voice orchestration is lib-showtime (L4). Platform account linking is lib-broadcast (L3). Audience simulation is lib-showtime (L4). Voice provides the audio infrastructure; higher layers decide when and why to use it.

**Critical architectural insight**: Voice moved from L4 (GameFeatures) to L3 (AppFeatures) to eliminate a hierarchy violation. GameSession (L2) previously depended on Voice (L4) to create/delete voice rooms -- a forbidden upward dependency. The redesign strips all game concepts from voice. Any service can create a room; higher layers (lib-showtime at L4) orchestrate the game-session-to-voice-room lifecycle via event subscriptions. The `sessionId` field in all models refers to the Connect/Auth session ID (L1 concept), not a game session.

**Privacy-first broadcasting**: Voice rooms contain personal audio data. Broadcasting that audio to external platforms (Twitch, YouTube, custom RTMP) requires explicit, informed consent from every participant. The broadcast consent flow is owned by lib-voice because it's a voice room concern; the actual RTMP output is lib-broadcast's domain. This separation ensures that consent is enforced at the audio source, not at the broadcast destination.

**Zero game-specific content**: lib-voice is a generic voice room service. Whether voice rooms back game sessions, collaborative workspaces, or social hangouts is determined by the caller, not by lib-voice. The service knows nothing about games, characters, realms, or subscriptions.

**Current implementation status**: The core voice room lifecycle (create, join, leave, delete), P2P mesh topology, scaled SFU tier with automatic upgrade, WebRTC signaling, and participant heartbeat tracking are **fully implemented**. The broadcast consent flow, service event publishing, participant TTL enforcement background worker, and ad-hoc room modes are **implemented**. The L3 layer classification and game-agnostic API descriptions are the **target state** per [VOICE-STREAMING.md](../planning/VOICE-STREAMING.md). Integration with lib-broadcast and lib-showtime is **future** (those services don't exist yet).

---

## Vision Alignment

### Which North Stars This Serves

**Ship Games Fast**: Voice rooms are an app-level primitive usable by any game built on Bannou, not just Arcadia. A non-game real-time collaboration tool gets voice rooms without pulling in game dependencies. Voice as L3 means it's available in any deployment mode -- voice-only, voice + streaming, or the full game stack.

**Living Game Worlds**: When combined with lib-showtime (L4), voice rooms become part of the in-game streaming metagame. NPCs can "hear" that players are in voice rooms (via lib-showtime's audience context), and the social dynamics of voice communication feed into the world simulation. A bard performing for a crowd in Arcadia has voice; the crowd's reaction feeds into the audience simulation.

**Emergent Over Authored**: Voice rooms enable unscripted, emergent social interactions. The broadcast consent flow allows those interactions to become part of the streaming metagame -- a spontaneous voice conversation becomes a broadcast moment that generates audience reactions, hype trains, and streamer career progression, none of which was authored or scripted.

### The Three-Service Architecture

```
                    EXTERNAL PLATFORMS
                    +----------+  +----------+
                    |  Twitch  |  |  YouTube  |
                    +----+-----+  +-----+----+
                         |              |
              Webhooks / OAuth API      |
                         |              |
    +--------------------v--------------v--------------------+
    |  lib-broadcast (L3 AppFeatures)                           |
    |                                                        |
    |  Platform Account Linking (OAuth)                      |
    |  Sentiment Processing (text -> anonymous sentiment)    |
    |  RTMP Output Management (FFmpeg)                       |
    |  Camera/Audio Source Ingestion                          |
    |  ------------------------------------                  |
    |  Subscribes to: voice.room.broadcast.approved          |
    |  Subscribes to: voice.room.broadcast.stopped           |
    |  No game knowledge. No L2 dependencies.                |
    |  Soft depends on: lib-voice (L3) for audio source      |
    +------------------------+-------------------------------+
                             |
               stream.audience.pulse events
               (sentiment arrays, no PII)
                             |
                             v
    +--------------------------------------------------------+
    |  lib-showtime (L4 GameFeatures)                       |
    |                                                        |
    |  Simulated Audience Pool (always available)            |
    |  Hype Train Mechanics                                  |
    |  Streamer Career (Seed type: streamer)                 |
    |  Real Audience Blending (when L3 available)            |
    |  Voice Room Orchestration (creates rooms for games)    |
    |  ------------------------------------                  |
    |  Subscribes to: game-session events                    |
    |  Subscribes to: voice room lifecycle events            |
    |  Soft depends on: lib-broadcast (L3), lib-voice (L3)      |
    +--------------------------------------------------------+

    +--------------------------------------------------------+
    |  lib-voice (L3 AppFeatures)          <--- THIS SERVICE  |
    |                                                        |
    |  Pure Voice Rooms (create/join/leave/delete)           |
    |  P2P Mesh Topology (small groups)                      |
    |  Scaled SFU via Kamailio/RTPEngine                     |
    |  Automatic Tier Upgrade (P2P -> Scaled)                |
    |  Broadcast Consent Flow                                |
    |  ------------------------------------                  |
    |  NO game concepts (no sessions, no subscriptions)      |
    |  Depends on: L0, L1 (connect, auth, permission)        |
    |  Soft depends on: nothing                              |
    |  Exposes: RTP audio endpoint for lib-broadcast            |
    +--------------------------------------------------------+
```

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis persistence for room data and participant registrations |
| lib-messaging (`IMessageBus`) | Service event publishing (8 event types) and error events via `TryPublishErrorAsync` |
| lib-permission (`IPermissionClient`) | Permission state management: `voice:in_room`, `voice:consent_pending`, `voice:ringing` |
| lib-connect (`IClientEventPublisher`) | Publishing WebSocket events to specific sessions (peer events, broadcast consent events) |
| Kamailio (external, config-only) | SIP proxy for scaled tier; host/port used in SIP credential generation (registrar URI). No active client integration -- `IKamailioClient`/`KamailioClient` files exist but are orphaned. |
| RTPEngine (external) | Media relay for SFU voice conferencing (UDP bencode ng protocol) |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-broadcast (L3, future) | Subscribes to `voice.room.broadcast.approved` to start RTMP output; subscribes to `voice.room.broadcast.stopped` to stop; reads RTP audio endpoint from tier-upgraded events |
| lib-showtime (L4, future) | Subscribes to `voice.room.created`/`voice.room.deleted` for broadcast-voice coordination; subscribes to participant events for audience context adjustments |

> **Hierarchy note**: GameSession (L2) previously depended on Voice via `IVoiceClient` -- this was a hierarchy violation (L2 cannot depend on L3). The dependency has been removed. Voice now manages its own room lifecycle independently, and higher-layer services (lib-showtime at L4) will orchestrate voice-broadcast coordination via event subscriptions. The new dependency flow is clean: lib-showtime (L4) soft-depends on lib-voice (L3), which is permitted by the hierarchy.

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
| `VoiceBroadcastConsentRequestEvent` | Broadcast consent requested -- sent to all room participants |
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

This plugin does not consume external events (`x-event-subscriptions: []`). Voice is a purely reactive service -- callers invoke its API, and it publishes events about what happened. This is a deliberate architectural choice: voice has no external dependencies to subscribe to, and its event model is unidirectional (publish-only).

### Who Consumes These Events (Target Architecture)

| Event | Consumer | Why |
|-------|----------|-----|
| `voice.room.created` | lib-showtime (L4) | Track voice rooms for audience context |
| `voice.room.deleted` | lib-showtime (L4) | Clean up streaming session associations |
| `voice.room.tier-upgraded` | lib-broadcast (L3) | Update RTP audio endpoint for active broadcasts |
| `voice.participant.joined` | lib-showtime (L4) | Adjust audience behavior based on room size |
| `voice.participant.left` | lib-showtime (L4) | Adjust audience behavior based on room size |
| `voice.room.broadcast.approved` | lib-broadcast (L3) | Start RTMP output for the voice room |
| `voice.room.broadcast.declined` | (nobody currently) | Informational; could drive UI feedback |
| `voice.room.broadcast.stopped` | lib-broadcast (L3) | Stop RTMP output for the voice room |

---

## The Broadcast Consent Flow

**This is a load-bearing privacy boundary.** Voice rooms contain personal audio data. Broadcasting that audio to an external platform requires explicit, informed consent from every participant.

### Two Distinct Broadcast Modes (via lib-broadcast)

| Mode | What's Broadcast | Who Initiates | Consent Required |
|------|-----------------|---------------|------------------|
| **Server-side content** | Game cameras, game audio | Admin API or ENV config | No player consent (it's game content) |
| **Voice room broadcast** | Participant voice audio | Client-side opt-in | Explicit consent from ALL room participants |

### Consent Flow

```
Player requests "broadcast my voice room" via client
    |
    v
lib-voice: /voice/room/broadcast/request
    |
    v
Validate: room exists, broadcast state is Inactive, requester is in room
    |
    v
Set room broadcast state to Pending
Set voice:consent_pending permission state for all participants
Send VoiceBroadcastConsentRequestEvent to all participants via WebSocket
    |
    v
Each participant calls /voice/room/broadcast/consent
    |
    +-- All consent:
    |       Set broadcast state to Approved
    |       Publish voice.room.broadcast.approved (includes RTP audio endpoint)
    |       lib-broadcast receives event, starts RTMP output
    |
    +-- Any decline (or timeout):
    |       Reset broadcast state to Inactive
    |       Publish voice.room.broadcast.declined
    |       Broadcast does not start
    |
    +-- Any participant revokes at any time:
            /voice/room/broadcast/stop
            Reset broadcast state to Inactive
            Publish voice.room.broadcast.stopped (reason: ConsentRevoked)
            lib-broadcast stops RTMP output
```

### Key Rules

- New participants joining a broadcasting room are warned before joining (via `isBroadcasting` flag in join response)
- Any participant can revoke consent at any time, immediately stopping the broadcast
- The broadcast status is visible to all room participants
- Silence is not consent: unanswered consent requests auto-decline after `BroadcastConsentTimeoutSeconds`
- Voice does not distinguish "Approved" from "Broadcasting" -- once all consent, what happens with the audio is lib-broadcast's domain

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

## Visual Aids

### Voice Communication Flow (P2P -> Scaled Upgrade)

```
  Client A                    Voice Service                   Client B
  ========                    =============                   ========

  JoinRoom -----------------> Register A
                                Set voice:ringing on B ------> [state updated]
                                Send VoicePeerJoinedEvent ---> Receives SDP offer
                                                               from A
                              <-------------------------------  AnswerPeer
                                Send VoicePeerUpdatedEvent
  Receives SDP <---------------  (A's answer)
  answer from B

  ... P2P mesh established ...

  Client C joins (exceeds P2PMaxParticipants)
  JoinRoom -----------------> Register C
                                ShouldUpgrade? YES
                                +---------------------+
                                | Background Task:    |
                                | AllocateRtpServer() |
                                | Update room tier    |
                                | For each participant|
                                |   GenSipCredentials |
                                |   Send TierUpgrade  |
                                +---------------------+
  TierUpgradeEvent <---------------------------------------> TierUpgradeEvent
  (SIP creds + RTP URI)                                      (SIP creds + RTP URI)

  ... All clients switch to SFU mode ...
```

### Broadcast Consent Flow (Voice -> Broadcast Integration)

```
  Client A          Voice Service          lib-broadcast (L3)        Twitch
  ========          =============          ==============         ======

  /broadcast/request --> Validate room
                         Set state: Pending
                         VoiceBroadcastConsentRequestEvent -----> Client B, C
                                                                  (via WebSocket)
  Client B consents ---> Record consent
                         VoiceBroadcastConsentUpdateEvent ------> All clients
                         (progress: 1/2 consented)

  Client C consents ---> Record consent
                         All consented!
                         Set state: Approved
                         Publish voice.room.broadcast.approved
                         (with RTP audio endpoint)
                                            |
                                            v
                                      Subscribe to event
                                      Start FFmpeg:
                                      RTP audio --> RTMP output -------> Live!

  ... Broadcasting active ...

  Client B: /broadcast/stop
                         Set state: Inactive
                         Publish voice.room.broadcast.stopped
                                            |
                                            v
                                      Stop FFmpeg process
                                      Publish stream.broadcast.stopped -> Offline
```

### Deployment Modes

```bash
# Voice only (collaboration tool, no streaming, no game)
VOICE_SERVICE_ENABLED=true

# Voice + platform streaming (broadcast voice rooms to Twitch)
VOICE_SERVICE_ENABLED=true
STREAM_SERVICE_ENABLED=true

# Full streaming metagame (voice + platform + in-game audiences)
VOICE_SERVICE_ENABLED=true
STREAM_SERVICE_ENABLED=true
SHOWTIME_SERVICE_ENABLED=true

# Platform streaming only (broadcast game cameras, no voice)
STREAM_SERVICE_ENABLED=true

# In-game metagame only (100% simulated audiences, no real platforms)
SHOWTIME_SERVICE_ENABLED=true
```

---

## Stubs & Unimplemented Features

1. **RTPEngine publish/subscribe**: `PublishAsync` and `SubscribeRequestAsync` are fully implemented in `RtpEngineClient` but never called by `VoiceService`. XML docs indicate these are reserved for future SFU publisher/subscriber routing.
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/195 -->

2. **RTP server pool allocation**: `AllocateRtpServerAsync` currently returns the single configured RTP server. The implementation notes "In production, this would select from a pool based on load."
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/258 -->

3. ~~**IKamailioClient methods unused**~~: **FIXED** (2026-02-11) - Removed `GetActiveDialogsAsync`, `TerminateDialogAsync`, `ReloadDispatcherAsync`, `GetStatsAsync` and all supporting JSONRPC infrastructure (models, `CallRpcAsync`, `_messageBus` dependency, `_requestId` counter). `IKamailioClient` now exposes only `IsHealthyAsync`. ~~Dead `_kamailioClient` field in `ScaledTierCoordinator`~~: **FIXED** (2026-02-11) - Removed `IKamailioClient` injection from `ScaledTierCoordinator` constructor (field was stored but never used by any method). Also removed orphaned DI registration from `VoiceServicePlugin`. `IKamailioClient.cs` and `KamailioClient.cs` still exist as source files but are now completely unreferenced -- can be deleted during next cleanup pass.

4. **VoiceRoomStateEvent**: Defined in `voice-client-events.yaml` but never published by the service.
<!-- AUDIT:NEEDS_DESIGN:2026-02-11:https://github.com/beyond-immersion/bannou-service/issues/396 -->

5. **L3 layer classification**: The API schema still declares voice as L4 (GameFeatures) or has no explicit `x-service-layer`. The target state is `x-service-layer: AppFeatures` per [VOICE-STREAMING.md](../planning/VOICE-STREAMING.md). This requires updating the schema and regenerating, then updating SERVICE-HIERARCHY.md.

6. **Game-agnostic API descriptions**: Several endpoint descriptions still reference "game session" language. The target state has all descriptions framed as generic session concepts per [VOICE-STREAMING.md](../planning/VOICE-STREAMING.md).

7. **lib-broadcast integration**: lib-broadcast does not exist yet. When implemented, it will subscribe to `voice.room.broadcast.approved` and `voice.room.broadcast.stopped` to manage RTMP output. The RTP audio endpoint metadata in the broadcast approved event enables this integration.

8. **lib-showtime integration**: lib-showtime does not exist yet. When implemented, it will subscribe to voice room lifecycle events and orchestrate the game-session-to-voice-room lifecycle that previously lived in GameSession (L2).

---

## Potential Extensions

1. **RTP server pool**: Multiple RTPEngine instances with load-based allocation. *(Duplicate of Stubs #2 -- tracked by #258)*
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/258 -->
2. **Room quality metrics**: Track audio quality, latency, packet loss per participant.
<!-- AUDIT:NEEDS_DESIGN:2026-02-11:https://github.com/beyond-immersion/bannou-service/issues/400 -->
3. **Recording support**: Integrate RTPEngine recording for compliance/replay.
<!-- AUDIT:NEEDS_DESIGN:2026-02-11:https://github.com/beyond-immersion/bannou-service/issues/401 -->
4. **Mute state synchronization**: Currently `IsMuted` is tracked but not synchronized across peers.
<!-- AUDIT:NEEDS_DESIGN:2026-02-11:https://github.com/beyond-immersion/bannou-service/issues/402 -->
5. **ICE trickle support**: Current implementation sends all ICE candidates in initial SDP; could support trickle ICE for faster connections.
<!-- AUDIT:NEEDS_DESIGN:2026-02-11:https://github.com/beyond-immersion/bannou-service/issues/403 -->
6. **Spatial audio**: For in-game voice (Arcadia bard performances, dungeon master communication), voice rooms could carry spatial position metadata. Clients mix audio volumes based on character proximity. Voice wouldn't compute audio -- it would relay position metadata alongside SDP, and clients would handle spatial mixing locally.
7. **Voice-to-text transcription**: For accessibility. Voice audio routed through a speech-to-text service, with transcriptions pushed as client events alongside the audio stream. Privacy-sensitive: requires separate consent from broadcast consent.
8. **NPC voice integration**: When combined with text-to-speech services, NPC dialogue could be delivered through voice rooms. A dungeon master's commands to their dungeon could be vocalized. This would require voice rooms to accept synthetic audio sources alongside human participants.

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

1. **RTPEngine UDP protocol**: Uses raw UDP with bencode encoding. No connection state, no retries on packet loss. `_sendLock` prevents concurrent sends but lost responses are not retried - the operation simply times out. Additionally, cookie mismatch responses (stale data from previous timed-out requests) are logged but used anyway -- a correctness bug.
<!-- AUDIT:NEEDS_DESIGN:2026-02-11:https://github.com/beyond-immersion/bannou-service/issues/404 -->

2. **SIP credential expiration not enforced**: Credentials have a 24-hour expiration timestamp (`SipCredentialExpirationHours`) but no server-side enforcement. Clients receive the expiration but there's no background task to rotate credentials or invalidate sessions.
<!-- AUDIT:NEEDS_DESIGN:2026-02-11:https://github.com/beyond-immersion/bannou-service/issues/405 -->

3. **Realm-specific voice manifestation**: In Omega (cyberpunk), voice rooms are explicit. In Arcadia, the same mechanics manifest as "speaking at a gathering" or "bard performing for a crowd." How much realm-specific logic belongs in lib-voice (none -- it's L3) vs. lib-showtime (L4) vs. client rendering? The answer should be: lib-voice provides the audio primitive; lib-showtime provides the game context; the client renders the appropriate visual metaphor based on realm. Voice never knows about realms.

4. **Voice room capacity and the streaming metagame**: When lib-showtime creates voice rooms for game sessions, how does room capacity interact with audience size? A streamer with 50 simulated audience members shouldn't need a 50-person voice room -- only real participants need voice. lib-showtime must track the distinction between voice participants and audience members independently.

---

## Work Tracking

### Issues Created

| Date | Issue | Gap | Status |
|------|-------|-----|--------|
| 2026-01-31 | [#195](https://github.com/beyond-immersion/bannou-service/issues/195) | RTPEngine publish/subscribe methods unused in scaled tier | Needs Design |
| 2026-02-01 | [#258](https://github.com/beyond-immersion/bannou-service/issues/258) | RTP server pool allocation with load-based selection | Needs Design |
| 2026-02-11 | [#396](https://github.com/beyond-immersion/bannou-service/issues/396) | VoiceRoomStateEvent defined but never published -- redundancy with JoinVoiceRoomResponse | Needs Design |
| 2026-02-11 | [#400](https://github.com/beyond-immersion/bannou-service/issues/400) | Room quality metrics design -- metric set, sources, storage, analytics integration | Needs Design |
| 2026-02-11 | [#401](https://github.com/beyond-immersion/bannou-service/issues/401) | Voice recording support architecture -- storage, consent, RTPEngine protocol, retention | Needs Design |
| 2026-02-11 | [#402](https://github.com/beyond-immersion/bannou-service/issues/402) | Mute state synchronization -- self vs admin mute, SFU enforcement, notification scope | Needs Design |
| 2026-02-11 | [#403](https://github.com/beyond-immersion/bannou-service/issues/403) | ICE trickle support -- relay vs accumulate, permission gating, P2P/SFU scope | Needs Design |
| 2026-02-11 | [#404](https://github.com/beyond-immersion/bannou-service/issues/404) | RTPEngine UDP client: cookie mismatch correctness bug and retry strategy | Needs Design |
| 2026-02-11 | [#405](https://github.com/beyond-immersion/bannou-service/issues/405) | SIP credential expiration enforcement -- Bannou vs Kamailio, rotation strategy | Needs Design |

### Completed

| Date | Gap | Action |
|------|-----|--------|
| 2026-02-11 | IKamailioClient methods unused (Stubs #3) | Removed 4 dead JSONRPC methods and all supporting infrastructure. `IKamailioClient` reduced to `IsHealthyAsync` only. |
| 2026-02-11 | Hardcoded fallbacks in coordinators (Design Considerations #4) | Removed 3 secondary fallbacks in `P2PCoordinator` and `ScaledTierCoordinator`. Config properties have schema defaults; fallbacks were unreachable and violated IMPLEMENTATION TENETS (T21). |
| 2026-02-11 | Dead `_kamailioClient` field in ScaledTierCoordinator (Stubs #3 follow-up) | Removed `IKamailioClient` injection from `ScaledTierCoordinator` constructor, removed orphaned DI registration from `VoiceServicePlugin`, updated tests. `IKamailioClient.cs` and `KamailioClient.cs` are now fully orphaned source files. |
| 2026-02-11 | Permission state race condition (Design Considerations #2) | Reclassified as Intentional Quirk #7. Race is benign: try-catch handles dead sessions, cleanup paths are independent. No code changes needed. |
