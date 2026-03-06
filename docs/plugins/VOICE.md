# Voice Plugin Deep Dive

> **Plugin**: lib-voice
> **Schema**: schemas/voice-api.yaml
> **Version**: 2.0.0
> **Layer**: AppFeatures
> **State Store**: voice-statestore (Redis), voice-lock (Redis)
> **Implementation Map**: [docs/maps/VOICE.md](../maps/VOICE.md)

---

## Overview

Voice room coordination service (L3 AppFeatures) providing pure voice rooms as a platform primitive: P2P mesh topology for small groups, Kamailio/RTPEngine-based SFU for larger rooms, automatic tier upgrade, WebRTC SDP signaling, broadcast consent flows for streaming integration, and participant TTL enforcement via background worker. Agnostic to games, sessions, and subscriptions -- voice rooms are generic containers identified by Connect/Auth session IDs. Part of a planned three-service stack (voice, broadcast, showtime) where each delivers value independently; voice provides audio infrastructure while higher layers decide when and why to use it. Moved from L4 to L3 to eliminate a hierarchy violation where GameSession (L2) previously depended on Voice (L4) for room lifecycle.

---

## Design Notes

**The three-service principle**: Voice is one of three services that together create a complete voice, streaming, and audience metagame stack. Each delivers value independently. lib-voice provides voice chat whether or not anyone is streaming. lib-broadcast (L3) can broadcast game content to Twitch whether or not voice is involved. lib-showtime (L4) provides a complete audience simulation metagame whether or not real platforms or voice rooms exist. They compose beautifully but never require each other.

**Composability**: Voice room primitives are owned here. RTMP broadcast output is lib-broadcast (L3). Game session voice orchestration is lib-showtime (L4). Platform account linking is lib-broadcast (L3). Audience simulation is lib-showtime (L4). Voice provides the audio infrastructure; higher layers decide when and why to use it.

**Privacy-first broadcasting**: Voice rooms contain personal audio data. Broadcasting that audio to external platforms (Twitch, YouTube, custom RTMP) requires explicit, informed consent from every participant. The broadcast consent flow is owned by lib-voice because it's a voice room concern; the actual RTMP output is lib-broadcast's domain. This separation ensures that consent is enforced at the audio source, not at the broadcast destination.

**Current implementation status**: Core voice room lifecycle, P2P mesh, scaled SFU with automatic upgrade, WebRTC signaling, participant heartbeat, broadcast consent flow, event publishing, and TTL enforcement are **fully implemented**. Integration with lib-broadcast and lib-showtime is **future** (those services don't exist yet).

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
    |  Subscribes to: voice.broadcast.approved          |
    |  Subscribes to: voice.broadcast.stopped           |
    |  No game knowledge. No L2 dependencies.                |
    |  Soft depends on: lib-voice (L3) for audio source      |
    +------------------------+-------------------------------+
                             |
               broadcast.audience.pulse events
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

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-broadcast (L3, future) | Subscribes to `voice.broadcast.approved` to start RTMP output; subscribes to `voice.broadcast.stopped` to stop; reads RTP audio endpoint from tier-upgraded events |
| lib-showtime (L4, future) | Subscribes to `voice.room.created`/`voice.room.deleted` for broadcast-voice coordination; subscribes to participant events for audience context adjustments |

> **Hierarchy note**: GameSession (L2) previously depended on Voice via `IVoiceClient` -- this was a hierarchy violation (L2 cannot depend on L3). The dependency has been removed. Voice now manages its own room lifecycle independently, and higher-layer services (lib-showtime at L4) will orchestrate voice-broadcast coordination via event subscriptions. The new dependency flow is clean: lib-showtime (L4) soft-depends on lib-voice (L3), which is permitted by the hierarchy.

---

## Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `tier` | C (System State/Mode) | `VoiceTier` enum (`P2P`, `Scaled`) | Communication infrastructure tier; system topology choice, not game content |
| `codec` | C (System State/Mode) | `VoiceCodec` enum (`Opus`, `G711`, `G722`) | Audio codec selection; system infrastructure choice |
| `broadcastState` / `state` | C (System State/Mode) | `BroadcastConsentState` enum (`Inactive`, `Pending`, `Approved`) | Tracks position in the broadcast consent state machine |
| `reason` (VoiceRoomDeletedEvent) | C (System State/Mode) | `VoiceRoomDeletedReason` enum (`Manual`, `Empty`, `Error`) | Classifies the cause of room deletion; system lifecycle reason |
| `reason` (VoiceRoomBroadcastStoppedEvent) | C (System State/Mode) | `VoiceBroadcastStoppedReason` enum (`ConsentRevoked`, `RoomClosed`, `Manual`, `Error`) | Classifies the cause of broadcast termination; system lifecycle reason |

**Notes**:
- Voice service has no `EntityType` enum fields (Category A). Participants are identified by `sessionId` (WebSocket session UUID), not by entity type polymorphism. This is a deliberate privacy-first design -- session IDs prevent leaking account information.
- Voice service has no opaque string type fields (Category B). It is game-agnostic with no game-configurable content types.
- All type fields are Category C (system state/mode), reflecting Voice's nature as a pure infrastructure primitive.

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
    |       Publish voice.broadcast.approved (includes RTP audio endpoint)
    |       lib-broadcast receives event, starts RTMP output
    |
    +-- Any decline (or timeout):
    |       Reset broadcast state to Inactive
    |       Publish voice.broadcast.declined
    |       Broadcast does not start
    |
    +-- Any participant revokes at any time:
            /voice/room/broadcast/stop
            Reset broadcast state to Inactive
            Publish voice.broadcast.stopped (reason: ConsentRevoked)
            lib-broadcast stops RTMP output
```

### Key Rules

- New participants joining a broadcasting room can check the `broadcastState` field in the join response
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
| `KamailioSipPort` | `VOICE_KAMAILIO_SIP_PORT` | `5060` | Kamailio SIP signaling port for client registration |
| `RtpEngineHost` | `VOICE_RTPENGINE_HOST` | `"localhost"` | RTPEngine server address |
| `RtpEnginePort` | `VOICE_RTPENGINE_PORT` | `22222` | RTPEngine ng protocol port |
| `RtpEngineTimeoutSeconds` | `VOICE_RTPENGINE_TIMEOUT_SECONDS` | `5` | Timeout in seconds for RTPEngine UDP requests (range 1-60) |
| `SipCredentialExpirationHours` | `VOICE_SIP_CREDENTIAL_EXPIRATION_HOURS` | `24` | Hours until SIP credentials expire |
| `EvictionWorkerInitialDelaySeconds` | `VOICE_EVICTION_WORKER_INITIAL_DELAY_SECONDS` | `10` | Seconds to wait after startup before the first eviction cycle (range 0-120) |
| `ParticipantHeartbeatTimeoutSeconds` | `VOICE_PARTICIPANT_HEARTBEAT_TIMEOUT_SECONDS` | `60` | Seconds of missed heartbeats before participant is evicted |
| `ParticipantEvictionCheckIntervalSeconds` | `VOICE_PARTICIPANT_EVICTION_CHECK_INTERVAL_SECONDS` | `15` | How often background worker checks for stale participants |
| `BroadcastConsentTimeoutSeconds` | `VOICE_BROADCAST_CONSENT_TIMEOUT_SECONDS` | `30` | Seconds to wait for all participants before auto-declining |
| `AdHocRoomsEnabled` | `VOICE_AD_HOC_ROOMS_ENABLED` | `false` | If true, joining a non-existent room auto-creates it with autoCleanup |
| `LockTimeoutSeconds` | `VOICE_LOCK_TIMEOUT_SECONDS` | `30` | Timeout in seconds for distributed lock acquisition (range 5-120) |
| `EmptyRoomGracePeriodSeconds` | `VOICE_EMPTY_ROOM_GRACE_PERIOD_SECONDS` | `300` | Seconds an empty autoCleanup room persists before auto-deletion |

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
                         Publish voice.broadcast.approved
                         (with RTP audio endpoint)
                                            |
                                            v
                                      Subscribe to event
                                      Start FFmpeg:
                                      RTP audio --> RTMP output -------> Live!

  ... Broadcasting active ...

  Client B: /broadcast/stop
                         Set state: Inactive
                         Publish voice.broadcast.stopped
                                            |
                                            v
                                      Stop FFmpeg process
                                      Publish broadcast.broadcast-output.updated -> Offline
```

### Deployment Modes

```bash
# Voice only (collaboration tool, no streaming, no game)
VOICE_SERVICE_ENABLED=true

# Voice + platform streaming (broadcast voice rooms to Twitch)
VOICE_SERVICE_ENABLED=true
BROADCAST_SERVICE_ENABLED=true

# Full streaming metagame (voice + platform + in-game audiences)
VOICE_SERVICE_ENABLED=true
BROADCAST_SERVICE_ENABLED=true
SHOWTIME_SERVICE_ENABLED=true

# Platform streaming only (broadcast game cameras, no voice)
BROADCAST_SERVICE_ENABLED=true

# In-game metagame only (100% simulated audiences, no real platforms)
SHOWTIME_SERVICE_ENABLED=true
```

---

## Stubs & Unimplemented Features

1. **RTPEngine publish/subscribe**: `PublishAsync` and `SubscribeRequestAsync` are fully implemented in `RtpEngineClient` but never called by `VoiceService`. XML docs indicate these are reserved for future SFU publisher/subscriber routing.
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/195 -->

2. **RTP server pool allocation**: `AllocateRtpServerAsync` currently returns the single configured RTP server. The implementation notes "In production, this would select from a pool based on load."
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/258 -->

3. **VoiceRoomStateClientEvent**: Defined in `voice-client-events.yaml`. Now published by `HandleSessionReconnectedAsync` for reconnection state restoration. Not yet published during normal join flow (still returns state via JoinVoiceRoomResponse).
<!-- AUDIT:NEEDS_DESIGN:2026-02-11:https://github.com/beyond-immersion/bannou-service/issues/396 -->

4. **lib-broadcast integration**: lib-broadcast does not exist yet. When implemented, it will subscribe to `voice.broadcast.approved` and `voice.broadcast.stopped` to manage RTMP output. The RTP audio endpoint metadata in the broadcast approved event enables this integration. **Voice's integration surface is complete for launch** — all three broadcast events (`approved`, `declined`, `stopped`) are published with correct typed models. No voice code changes needed for initial integration; blocked on lib-broadcast service implementation. Note: Broadcast Phase 4 (mute-aware audio mixing) will need mute state synchronization events from Voice — tracked by #402.
<!-- AUDIT:BLOCKED:2026-03-01 -->

5. **lib-showtime integration**: lib-showtime does not exist yet. When implemented, it will subscribe to voice room lifecycle events and orchestrate the game-session-to-voice-room lifecycle that previously lived in GameSession (L2). **Voice's integration surface is complete** — all four lifecycle events (`voice.room.created`, `voice.room.deleted`, `voice.peer.joined`, `voice.peer.left`) are published with correct typed models. No voice code changes needed; blocked on lib-showtime service implementation.
<!-- AUDIT:BLOCKED:2026-03-01 -->


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
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/547 -->
7. **Voice-to-text transcription**: For accessibility. Voice audio routed through a speech-to-text service, with transcriptions pushed as client events alongside the audio stream. Privacy-sensitive: requires separate consent from broadcast consent.
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/548 -->
8. **NPC voice integration**: When combined with text-to-speech services, NPC dialogue could be delivered through voice rooms. A dungeon master's commands to their dungeon could be vocalized. This would require voice rooms to accept synthetic audio sources alongside human participants.
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/549 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. **RTPEngine cookie mismatch correctness bug**: The RTPEngine UDP client uses raw UDP with bencode encoding. Cookie mismatch responses (stale data from previous timed-out requests) are logged but used anyway, meaning the service can act on wrong response data. The triage on #404 confirms this is a correctness bug requiring a loop-receive until the correct cookie arrives or timeout expires, discarding mismatched responses.
<!-- AUDIT:NEEDS_DESIGN:2026-02-11:https://github.com/beyond-immersion/bannou-service/issues/404 -->

### Intentional Quirks (Documented Behavior)

1. **SDP answer in SdpOffer field**: The `VoicePeerUpdatedEvent` reuses the `SdpOffer` field in `VoicePeerInfo` to carry the SDP answer from `/voice/peer/answer`. This is intentional - the same model represents both directions of the WebRTC handshake.

2. **Fire-and-forget tier upgrade**: `Task.Run()` for tier upgrade is not awaited in `JoinVoiceRoomAsync`. Uses `CancellationToken.None` (not request-scoped token) so the upgrade completes independently of the HTTP request lifecycle. The join response returns immediately while upgrade happens in background. Errors are logged but don't affect the join response.

3. **P2P upgrade threshold is "exceeds", not "at"**: `ShouldUpgradeToScaledAsync` triggers when `currentParticipantCount > maxP2P`. A room AT capacity is still P2P; only when the next participant joins does upgrade trigger. This is deliberate to avoid unnecessary upgrades.

4. **Local cache + Redis dual storage**: `SipEndpointRegistry` maintains a local `ConcurrentDictionary` cache that is synchronized with Redis on every mutation. This is for performance but means state can be transiently inconsistent across service instances.

5. **Session-based privacy**: All participant tracking uses `sessionId` (WebSocket session) rather than `accountId` to prevent leaking account information. Display names are opt-in.

6. **Permission state set for BOTH directions**: When joining a room with existing peers, `voice:ringing` is set for both the joining session AND all existing sessions. This enables bidirectional SDP exchange.

7. **Permission state race on peer leave during join**: The sequential `voice:ringing` loop in `NotifyPeerJoinedAsync` can set state on a session that left between the participant list fetch and the state update. This is benign: every `UpdateSessionStateAsync` call is try-catch wrapped (logs warning), and the leaving session's own `LeaveVoiceRoomAsync` path clears its permission state independently.

8. **Realm-agnostic by design (L3 boundary)**: Voice rooms have zero realm or game concept awareness. The same voice room primitive manifests differently per realm via client rendering — explicit voice chat in Omega (cyberpunk), "speaking at a gathering" or "bard performing for a crowd" in Arcadia. Realm-specific presentation is entirely a lib-showtime (L4) and client concern. This is an architectural boundary enforced by Voice's L3 AppFeatures layer: it cannot depend on L2 GameFoundation services like Realm.

### Design Considerations (Requires Planning)

1. **RTPEngine UDP retry strategy**: Beyond the cookie mismatch bug (see Bugs #1), the RTPEngine UDP client has no retry logic for lost responses — the operation simply times out. A retry strategy with exponential backoff is needed for production reliability.
<!-- AUDIT:NEEDS_DESIGN:2026-02-11:https://github.com/beyond-immersion/bannou-service/issues/404 -->

2. **SIP credential expiration not enforced**: Credentials have a 24-hour expiration timestamp (`SipCredentialExpirationHours`) but no server-side enforcement. Clients receive the expiration but there's no background task to rotate credentials or invalidate sessions.
<!-- AUDIT:NEEDS_DESIGN:2026-02-11:https://github.com/beyond-immersion/bannou-service/issues/405 -->

---

## Work Tracking

### Issues Created

| Date | Issue | Gap | Status |
|------|-------|-----|--------|
| 2026-01-31 | [#195](https://github.com/beyond-immersion/bannou-service/issues/195) | RTPEngine publish/subscribe methods unused in scaled tier | Needs Design |
| 2026-02-01 | [#258](https://github.com/beyond-immersion/bannou-service/issues/258) | RTP server pool allocation with load-based selection | Needs Design |
| 2026-02-11 | [#396](https://github.com/beyond-immersion/bannou-service/issues/396) | VoiceRoomStateClientEvent not published during join flow -- redundancy with JoinVoiceRoomResponse | Needs Design |
| 2026-02-11 | [#400](https://github.com/beyond-immersion/bannou-service/issues/400) | Room quality metrics design -- metric set, sources, storage, analytics integration | Needs Design |
| 2026-02-11 | [#401](https://github.com/beyond-immersion/bannou-service/issues/401) | Voice recording support architecture -- storage, consent, RTPEngine protocol, retention | Needs Design |
| 2026-02-11 | [#402](https://github.com/beyond-immersion/bannou-service/issues/402) | Mute state synchronization -- self vs admin mute, SFU enforcement, notification scope | Needs Design |
| 2026-02-11 | [#403](https://github.com/beyond-immersion/bannou-service/issues/403) | ICE trickle support -- relay vs accumulate, permission gating, P2P/SFU scope | Needs Design |
| 2026-02-11 | [#404](https://github.com/beyond-immersion/bannou-service/issues/404) | RTPEngine UDP client: cookie mismatch correctness bug and retry strategy | Needs Design |
| 2026-02-11 | [#405](https://github.com/beyond-immersion/bannou-service/issues/405) | SIP credential expiration enforcement -- Bannou vs Kamailio, rotation strategy | Needs Design |
| 2026-03-01 | [#547](https://github.com/beyond-immersion/bannou-service/issues/547) | Spatial audio: layer placement and position metadata design | Needs Design |
| 2026-03-01 | [#548](https://github.com/beyond-immersion/bannou-service/issues/548) | Voice-to-text transcription: STT service, audio capture model, consent design | Needs Design |
| 2026-03-01 | [#549](https://github.com/beyond-immersion/bannou-service/issues/549) | NPC voice integration: synthetic audio sources, layer placement, participant model | Needs Design |

### Completed

(None — all processed items have been removed from the document.)
