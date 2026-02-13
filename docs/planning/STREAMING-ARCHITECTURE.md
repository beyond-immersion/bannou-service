# Streaming Architecture: Voice (L3) + Platform Integration (L3) + In-Game Metagame (L4)

> **Status**: Design Draft v2
> **Last Updated**: 2026-02-10
> **Supersedes**: VOICE-STREAMING.md (RTMP I/O plan, now absorbed into lib-stream)
> **Depends On**: Collection Plugin (lib-collection), Seed Plugin (lib-seed), Contract Plugin (lib-contract), Currency Plugin (lib-currency), Relationship Plugin (lib-relationship)
> **Related**: [VOICE-STREAMING.md](VOICE-STREAMING.md) (lib-voice L3 redesign companion doc)
> **Source Context**: arcadia-kb Audience Service Architecture, Advanced Audience Dynamics, Systems Mining Synthesis (2026-02-10)

---

## Executive Summary

Three services that together create a complete voice, streaming, and audience metagame stack -- cleanly separated by domain and optionality.

| Service | Layer | Role |
|---------|-------|------|
| **lib-voice** | L3 (AppFeatures) | Pure voice rooms: create, join, leave, P2P mesh, scaled SFU. No game concepts. Works with lib-connect alone. |
| **lib-stream** | L3 (AppFeatures) | Platform streaming: link Twitch/YouTube accounts, RTMP output (FFmpeg), camera ingestion, sentiment processing. Can broadcast server-side content without player involvement. |
| **lib-showtime** | L4 (GameFeatures) | In-game streaming metagame: simulated audience pools, hype trains, streamer careers, real-simulated audience blending. |

**The three-service principle**: Each service delivers value independently. lib-voice provides voice chat whether or not anyone is streaming. lib-stream can broadcast game content to Twitch whether or not there's voice involved or an in-game metagame. lib-showtime provides a complete audience simulation metagame whether or not real platforms or voice rooms exist. They compose beautifully but never require each other.

**Privacy-first**: Real audience data never enters the event system as text, usernames, or PII. lib-stream processes raw platform events into **batched sentiment pulses** -- arrays of anonymous sentiment values with optional opaque tracking GUIDs for consistency. No platform user IDs, no message content, no personally identifiable information leaves lib-stream's boundary. Voice broadcasting to external platforms requires explicit client-side opt-in -- a potential privacy nightmare otherwise.

**Key architectural change from v1**: lib-voice moves from L4 (GameFeatures) to L3 (AppFeatures). RTMP output management moves from lib-voice to lib-stream. The former VOICE-STREAMING.md plan is absorbed into lib-stream. This eliminates the GameSession (L2) → Voice (L4) hierarchy violation that existed in the original design.

---

## Vision Alignment

### Which North Stars This Serves

**Living Game Worlds**: The streaming metagame creates another layer of autonomous behavior -- simulated audiences watching, reacting, following, and migrating between in-game streamers. The audience IS part of the world, not a UI overlay.

**The Content Flywheel**: Streaming sessions generate events (hype trains, world-first discoveries, audience milestones) that feed into character history, realm history, and analytics. A legendary streaming moment becomes part of the world's lore.

**Ship Games Fast**: Voice rooms and platform streaming are app-level primitives usable by any game built on Bannou, not just Arcadia. A non-game real-time collaboration tool gets voice rooms without pulling in game dependencies.

**Emergent Over Authored**: Audience behavior emerges from personality matching, interest decay, and competitive attention dynamics -- not scripted reactions. When real audiences blend in, they add genuine unpredictability that no algorithm can replicate.

### Omega Realm Integration

The Omega realm (cyberpunk meta-dashboard) provides the diegetic context for streaming. The player's "full-dive VR machine" has a streaming module. In Omega, the streaming metagame is explicit -- you can see your audience stats, manage your stream, and compete with other streamers. In other realms, the streaming concept might manifest differently (e.g., a bard performing for a crowd in Arcadia).

---

## Architecture Overview

```
                    EXTERNAL PLATFORMS
                    ┌──────────┐  ┌──────────┐
                    │  Twitch  │  │  YouTube  │
                    └────┬─────┘  └─────┬────┘
                         │              │
              Webhooks / OAuth API      │
                         │              │
    ┌────────────────────▼──────────────▼──────────────────┐
    │  lib-stream (L3 AppFeatures)                          │
    │                                                       │
    │  Platform Account Linking (OAuth)                     │
    │  Platform Session Detection (webhooks)                │
    │  Raw Event Ingestion (chat, subs, raids)              │
    │  Sentiment Processing (text → sentiment values)       │
    │  RTMP Output Management (FFmpeg)  ◄─── NEW in v2     │
    │  Camera/Audio Source Ingestion     ◄─── NEW in v2     │
    │  ─────────────────────────────────────────────        │
    │  Publishes: stream.audience.pulse (batched)           │
    │  Publishes: stream.platform.session.started/ended     │
    │  Publishes: stream.broadcast.started/stopped          │
    │  No game knowledge. No L2 dependencies.               │
    │  Soft depends on: lib-voice (L3) for audio source     │
    └───────────────────────┬──────────────────────────────┘
                            │
              stream.audience.pulse events
              (sentiment arrays, no PII)
                            │
                            ▼
    ┌───────────────────────────────────────────────────────┐
    │  lib-showtime (L4 GameFeatures)                      │
    │                                                       │
    │  Simulated Audience Pool (always available)           │
    │  Interest Matching Engine                             │
    │  Hype Train Mechanics                                 │
    │  Streamer Career (Seed type: streamer)                │
    │  Follow/Subscribe Dynamics                            │
    │  ─────────────────────────────────────────────        │
    │  Real Audience Blending (when L3 available):          │
    │    Sentiment pulses → "real-derived" audience members │
    │    Tracking GUIDs → consistent phantom identities     │
    │    Blended seamlessly with simulated members          │
    │  ─────────────────────────────────────────────        │
    │  Depends on: L0, L1, L2 (game-session, character)    │
    │  Soft depends on: L3 (lib-stream, lib-voice)          │
    │  Composable with: Seed, Collection, Currency,         │
    │                    Contract, Relationship              │
    └───────────────────────────────────────────────────────┘


    ┌───────────────────────────────────────────────────────┐
    │  lib-voice (L3 AppFeatures)          ◄─── REDESIGNED  │
    │                                                       │
    │  Pure Voice Rooms (create/join/leave/delete)          │
    │  P2P Mesh Topology (small groups)                     │
    │  Scaled SFU via Kamailio/RTPEngine                    │
    │  Automatic Tier Upgrade (P2P → Scaled)                │
    │  WebRTC SDP Signaling                                 │
    │  ─────────────────────────────────────────────        │
    │  NO game concepts (no sessions, no subscriptions)     │
    │  Depends on: L0, L1 (connect, auth, permission)      │
    │  Soft depends on: nothing                             │
    │  Exposes: RTP audio endpoint for lib-stream           │
    └───────────────────────────────────────────────────────┘
```

### Dependency Graph

```
lib-showtime (L4) ──hard──► L0, L1, L2 (game-session, character, game-service)
       │
       ├──soft──► lib-stream (L3)    # Sentiment pulses, platform sessions
       └──soft──► lib-voice (L3)     # Voice room awareness for audience context

lib-stream (L3) ──hard──► L0, L1 (account, auth)
       │
       └──soft──► lib-voice (L3)     # RTP audio source for RTMP output

lib-voice (L3) ──hard──► L0, L1 (connect, auth, permission)
```

### Deployment Modes

```bash
# Voice only (collaboration tool, no streaming, no game)
VOICE_SERVICE_ENABLED=true

# Voice + platform streaming (broadcast voice rooms to Twitch)
VOICE_SERVICE_ENABLED=true
STREAM_SERVICE_ENABLED=true

# Platform streaming only (broadcast game cameras, no voice)
STREAM_SERVICE_ENABLED=true

# Full streaming metagame (voice + platform + in-game audiences)
VOICE_SERVICE_ENABLED=true
STREAM_SERVICE_ENABLED=true
SHOWTIME_SERVICE_ENABLED=true

# In-game metagame only (100% simulated audiences, no real platforms)
SHOWTIME_SERVICE_ENABLED=true
```

---

## Privacy-First Sentiment Architecture

This is the load-bearing design decision. Real audience data must never create compliance liabilities (GDPR, CCPA, data deletion orders). The solution: real audience events are reduced to anonymous sentiment values before leaving lib-stream's process boundary.

### Why No Text Content

1. **Data deletion compliance**: If a Twitch user requests data deletion under GDPR, we'd need to purge their messages from every downstream system (analytics, state stores, event logs). With sentiment-only data, there's nothing to delete -- the original text never left lib-stream.
2. **Analytics cache problem**: Analytics ingests events for aggregation. Flushing specific user data from analytical stores is operationally expensive and error-prone. Sentiment values have no user association.
3. **Legal exposure**: Storing third-party platform user content creates licensing and liability questions. Sentiment approximations are derived data, not reproductions.

### The Sentiment Pulse Model

lib-stream processes raw platform events (chat messages, subscriptions, raids, emotes) into periodic **sentiment pulses** -- batched arrays of anonymous sentiment data points.

```
SentimentPulse:
  pulseId: Guid                       # Unique pulse identifier
  streamSessionId: Guid               # The lib-showtime in-game session (if linked)
  platformSessionId: Guid             # The lib-stream platform session
  timestamp: DateTime                 # When this pulse was assembled
  intervalSeconds: int                # Configured pulse interval
  approximateViewerCount: int         # Platform-reported viewer count (approximate)
  sentiments: SentimentEntry[]        # The batch (see below)
```

Each entry in the batch:

```
SentimentEntry:
  category: SentimentCategory         # Enum: see below
  intensity: float                    # 0.0 to 1.0 (strength of sentiment)
  trackingId: Guid?                   # null = anonymous, non-null = "important" viewer
  viewerType: TrackedViewerType?      # null = anonymous, non-null = role category
```

### Sentiment Categories

```
SentimentCategory enum:
  Excited       # High-energy positive (hype, celebration, spam of positive emotes)
  Supportive    # Calm positive (encouragement, constructive comments)
  Critical      # Negative feedback (complaints, dissatisfaction)
  Curious       # Engagement without clear valence (questions, "what happened?")
  Surprised     # Unexpected reaction (plot twists, world-first discoveries)
  Amused        # Entertainment response (jokes, funny moments)
  Bored         # Low engagement signals (AFK, minimal interaction)
  Hostile       # Aggressive negativity (distinct from critical -- toxicity signals)
```

### Tracked Viewers ("Important" Sentiments)

Most sentiments in a pulse are anonymous -- just a category + intensity with no tracking information. A configurable subset of "important" viewers receive opaque tracking GUIDs:

```
TrackedViewerType enum:
  Subscriber    # Platform subscriber (Twitch sub, YouTube member)
  Moderator     # Platform moderator
  RaidLeader    # Led a raid into the stream
  VIP           # Platform VIP designation
  HighEngager   # Algorithmically determined high-engagement viewer
  Returner      # Has been present across multiple streaming sessions
```

**How tracking IDs work**:

1. lib-stream maintains an **ephemeral, in-memory** mapping: `platformUserId → trackingId`
2. This mapping exists ONLY during an active platform session
3. When the session ends, the mapping is destroyed -- the tracking IDs become orphaned
4. The tracking IDs are Bannou-generated GUIDs with NO relationship to platform user IDs
5. The same real viewer gets the SAME tracking ID across pulses within a session (consistency)
6. But across sessions, they get a NEW tracking ID (no cross-session tracking)
7. There is NO way to reverse a tracking ID back to a platform user

**Batch Timing and Size**:

| Config | Purpose | Default |
|--------|---------|---------|
| SentimentPulseIntervalSeconds | How often pulses are published | 15 |
| SentimentMinBatchSize | Minimum sentiments before publishing a pulse | 5 |
| SentimentMaxBatchSize | Maximum sentiments per pulse (overflow drops lowest-intensity) | 200 |

**Timing rationale**: 15-second intervals create enough delay that individual messages can't be correlated to specific sentiment entries by timing alone. Combined with batching, this makes de-anonymization impractical even for someone monitoring both the platform chat and the sentiment stream.

---

## Voice Broadcasting Consent Model

**This is a load-bearing privacy boundary.** Voice rooms contain personal audio data. Broadcasting that audio to an external platform (Twitch, YouTube, custom RTMP) requires explicit, informed consent from every participant.

### Two Distinct Broadcast Modes

| Mode | What's Broadcast | Who Initiates | Consent Required |
|------|-----------------|---------------|------------------|
| **Server-side content** | Game cameras, game audio, server-generated content | Admin API or ENV config | No player consent (it's game content) |
| **Voice room broadcast** | Participant voice audio from a lib-voice room | Client-side opt-in | Explicit consent from ALL room participants |

### Consent Flow for Voice Broadcasting

```
Player requests "broadcast my voice room" via client
    │
    ▼
lib-voice checks: is this session in a voice room?
    │ yes
    ▼
lib-voice publishes: voice.room.broadcast.requested
    │
    ▼
All room participants receive VoiceBroadcastConsentRequestEvent
    │
    ├── All consent → lib-voice publishes voice.room.broadcast.approved
    │                     │
    │                     ▼
    │                 lib-stream receives event, starts RTMP output
    │                 (connects to voice room's RTP audio source)
    │
    └── Any decline → lib-voice publishes voice.room.broadcast.declined
                          Broadcast does not start
```

**Key rules**:
- New participants joining a broadcasting room are warned before joining
- Any participant can revoke consent at any time, stopping the broadcast
- The broadcast status is visible to all room participants
- Voice rooms that are broadcasting are marked as such in room metadata

### Server-Side Content Broadcasting (No Consent Needed)

lib-stream can broadcast game content independently of any voice room:

```bash
# ENV-configured: always broadcast this game camera to this RTMP endpoint
STREAM_AUTO_BROADCAST_CAMERA_ID=main-arena-cam
STREAM_AUTO_BROADCAST_RTMP_URL=rtmp://live.twitch.tv/app/YOUR_KEY

# Or via admin API:
POST /stream/broadcast/start
{
    "sourceType": "camera",
    "sourceId": "main-arena-cam",
    "rtmpOutputUrl": "rtmp://live.twitch.tv/app/YOUR_KEY"
}
```

This mode uses lib-stream's FFmpeg management to combine video (from game cameras) with optional audio (from game audio sources) and output to RTMP. No player voice data is involved.

---

## lib-voice: L3 App Feature (Pure Voice Rooms)

### Service Identity

| Property | Value |
|----------|-------|
| **Layer** | L3 (AppFeatures) -- **changed from L4** |
| **Plugin** | `plugins/lib-voice/` |
| **Schema prefix** | `voice` |
| **Service name** | `voice` |
| **Hard dependencies** | L0 (state, messaging), L1 (connect, auth, permission) |
| **Soft dependencies** | None |
| **Cannot depend on** | L2, L4 |
| **When absent** | No voice chat; lib-stream can still broadcast game content; lib-showtime runs without voice |

### What This Service Does

1. **Voice Room Management**: Create, get, delete rooms with configurable max participants
2. **P2P Mesh Topology**: WebRTC peer-to-peer connections for small groups (default max 8)
3. **Scaled SFU Tier**: Kamailio/RTPEngine-based SFU for larger rooms
4. **Automatic Tier Upgrade**: Transparent P2P → Scaled migration when room grows
5. **WebRTC Signaling**: SDP offer/answer relay, ICE candidate exchange
6. **Participant Lifecycle**: Join, leave, heartbeat, registration with TTL enforcement
7. **Audio Source Exposure**: Exposes RTP audio endpoint metadata so lib-stream can connect

### What This Service Does NOT Do

- No game session references (rooms are generic, identified by sessionId which is a Connect/Auth L1 concept)
- No RTMP output (that's lib-stream's domain now)
- No platform integration (that's lib-stream)
- No audience simulation (that's lib-showtime)
- No subscription checks (higher layers gate access before calling voice)

### Key Architectural Change: L4 → L3

The current lib-voice is L4 because it was built before the service hierarchy and has a direct dependency on GameSession (L2). The redesign strips all game concepts:

| Before (L4) | After (L3) |
|-------------|------------|
| `sessionId` described as "game session ID" | `sessionId` kept as-is (it's a Connect/Auth L1 concept, not game-specific) |
| GameSession creates/deletes voice rooms | Higher layers (lib-showtime L4) orchestrate room lifecycle |
| Voice room tied to game session lifecycle | Voice room is independent; higher layers manage association |
| Voice publishes zero domain events | Voice publishes room lifecycle events |

### What Carries Over From Current Implementation

The internal helpers are voice-level primitives that don't care about layer placement:

- **P2PCoordinator**: Mesh topology management, max participants logic
- **ScaledTierCoordinator**: SFU allocation, SIP credential generation
- **SipEndpointRegistry**: Participant tracking with Redis persistence
- **KamailioClient**: JSONRPC 2.0 integration with Kamailio SIP proxy
- **RtpEngineClient**: Bencode UDP protocol for RTPEngine media relay
- **VoiceRoomState**: Room data models (sessionId stays as-is, add broadcast consent fields)

### Service Events (NEW)

```yaml
# Published by lib-voice
voice.room.created:
  schema:
    VoiceRoomCreatedEvent:
      type: object
      required: [eventId, timestamp, roomId, sessionId, tier, maxParticipants]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        roomId: { type: string, format: uuid }
        sessionId: { type: string, format: uuid }
        tier: { $ref: '#/components/schemas/VoiceTier' }
        maxParticipants: { type: integer }

voice.room.deleted:
  schema:
    VoiceRoomDeletedEvent:
      type: object
      required: [eventId, timestamp, roomId, reason]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        roomId: { type: string, format: uuid }
        reason: { type: string, enum: [manual, empty, error] }

voice.room.tier-upgraded:
  schema:
    VoiceRoomTierUpgradedEvent:
      type: object
      required: [eventId, timestamp, roomId, previousTier, newTier]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        roomId: { type: string, format: uuid }
        previousTier: { $ref: '#/components/schemas/VoiceTier' }
        newTier: { $ref: '#/components/schemas/VoiceTier' }
        rtpAudioEndpoint: { type: string, nullable: true }

voice.participant.joined:
  schema:
    VoiceParticipantJoinedEvent:
      type: object
      required: [eventId, timestamp, roomId, participantSessionId, currentCount]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        roomId: { type: string, format: uuid }
        participantSessionId: { type: string, format: uuid }
        currentCount: { type: integer }

voice.participant.left:
  schema:
    VoiceParticipantLeftEvent:
      type: object
      required: [eventId, timestamp, roomId, participantSessionId, remainingCount]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        roomId: { type: string, format: uuid }
        participantSessionId: { type: string, format: uuid }
        remainingCount: { type: integer }

voice.room.broadcast.approved:
  description: >
    All participants consented to broadcasting. lib-stream subscribes
    to this to start RTMP output from the room's audio source.
  schema:
    VoiceRoomBroadcastApprovedEvent:
      type: object
      required: [eventId, timestamp, roomId, requestedBySessionId]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        roomId: { type: string, format: uuid }
        requestedBySessionId: { type: string, format: uuid }
        rtpAudioEndpoint: { type: string, description: RTP endpoint for mixed audio }

voice.room.broadcast.stopped:
  description: >
    Broadcasting stopped (consent revoked, room closed, or manual stop).
  schema:
    VoiceRoomBroadcastStoppedEvent:
      type: object
      required: [eventId, timestamp, roomId, reason]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        roomId: { type: string, format: uuid }
        reason: { type: string, enum: [consent_revoked, room_closed, manual, error] }
```

See [VOICE-STREAMING.md](VOICE-STREAMING.md) for the complete lib-voice L3 redesign including API changes, implementation details, and migration approach.

---

## lib-stream: L3 App Feature (Platform Streaming Integration + RTMP Output)

### Service Identity

| Property | Value |
|----------|-------|
| **Layer** | L3 (AppFeatures) |
| **Plugin** | `plugins/lib-stream/` |
| **Schema prefix** | `stream` |
| **Service name** | `stream` |
| **Hard dependencies** | L0 (state, messaging), L1 (account, auth) |
| **Soft dependencies** | L3 (lib-voice -- for voice room audio source) |
| **Cannot depend on** | L2, L4 |
| **When absent** | lib-showtime runs on 100% simulated audiences; lib-voice works without broadcasting |

### What This Service Does

1. **Platform Account Linking**: OAuth flows to connect Bannou accounts to Twitch, YouTube, or custom RTMP endpoints
2. **Platform Session Detection**: Webhook subscriptions (Twitch EventSub, YouTube webhooks) detect when linked accounts go live
3. **Raw Event Ingestion**: Consumes platform events (chat messages, subscriptions, raids, follows, emote usage) via platform APIs/webhooks
4. **Sentiment Processing**: Converts raw events into sentiment values using configurable processing rules
5. **Sentiment Pulse Publishing**: Batches sentiments and publishes `stream.audience.pulse` events on the message bus
6. **Stream Key Security**: Manages RTMP credentials with masking in all responses and logs
7. **RTMP Output Management** (NEW): FFmpeg process management for broadcasting to RTMP endpoints
8. **Audio Source Integration** (NEW): Connects to lib-voice RTP audio endpoints for voice room broadcasting
9. **Camera Ingestion** (NEW): Receives game camera RTMP streams as video sources
10. **Broadcast Lifecycle** (NEW): Start/stop/monitor broadcasts with health tracking and fallback cascade

### What This Service Does NOT Do

- Does not know about games, characters, realms, or any L2 concepts
- Does not manage simulated audiences (that's lib-showtime L4)
- Does not store message content or platform usernames beyond the ephemeral processing window
- Does not persist the platformUserId→trackingId mapping (in-memory only)
- Does not initiate voice room broadcasts (client requests through lib-voice, which publishes consent events)

### API Endpoints

All endpoints use POST-only pattern for zero-copy WebSocket routing.

#### Platform Account Management

```yaml
/stream/platform/link:
  post:
    operationId: linkPlatform
    summary: Initiate OAuth flow to link a streaming platform account
    x-permissions:
      - role: user
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/LinkPlatformRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/LinkPlatformResponse'
      '400':
        description: Invalid platform or already linked
      '409':
        description: Platform account already linked to another Bannou account

/stream/platform/callback:
  post:
    operationId: platformCallback
    summary: Handle OAuth callback from streaming platform
    x-permissions:
      - role: user
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/PlatformCallbackRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/PlatformLinkDetail'
      '400':
        description: Invalid or expired authorization code
      '404':
        description: No pending link request found

/stream/platform/unlink:
  post:
    operationId: unlinkPlatform
    summary: Unlink a streaming platform account
    x-permissions:
      - role: user
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/UnlinkPlatformRequest'
    responses:
      '200':
        description: Platform unlinked
      '404':
        description: Platform link not found

/stream/platform/list:
  post:
    operationId: listPlatforms
    summary: List linked streaming platforms for an account
    x-permissions:
      - role: user
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/ListPlatformsRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/ListPlatformsResponse'
```

#### Platform Session Management

```yaml
/stream/session/start:
  post:
    operationId: startPlatformSession
    summary: Start monitoring a platform streaming session
    x-permissions:
      - role: user
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/StartPlatformSessionRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/PlatformSessionStatus'
      '400':
        description: Platform not linked or not currently live
      '409':
        description: Session already active for this platform link

/stream/session/stop:
  post:
    operationId: stopPlatformSession
    summary: Stop monitoring a platform streaming session
    x-permissions:
      - role: user
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/StopPlatformSessionRequest'
    responses:
      '200':
        description: Session monitoring stopped
      '404':
        description: No active session found

/stream/session/associate:
  post:
    operationId: associateSession
    summary: Associate a platform session with an in-game streaming session
    x-permissions:
      - role: user
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/AssociateSessionRequest'
    responses:
      '200':
        description: Association updated
      '404':
        description: Platform session not found

/stream/session/status:
  post:
    operationId: getPlatformSessionStatus
    summary: Get current platform session status
    x-permissions:
      - role: user
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/PlatformSessionStatusRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/PlatformSessionStatus'
      '404':
        description: No active session found

/stream/session/list:
  post:
    operationId: listPlatformSessions
    summary: List active and recent platform sessions for an account
    x-permissions:
      - role: user
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/ListPlatformSessionsRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/ListPlatformSessionsResponse'
```

#### Broadcast Management (NEW -- absorbed from VOICE-STREAMING.md)

```yaml
/stream/broadcast/start:
  post:
    operationId: startBroadcast
    summary: Start broadcasting to an RTMP endpoint
    description: >
      Starts an FFmpeg process to broadcast audio/video to an RTMP destination.
      Source can be a game camera (server-side, admin-only), game audio, or
      a voice room audio endpoint (requires prior consent via lib-voice).
      For voice room sources, lib-stream subscribes to
      voice.room.broadcast.approved events rather than accepting direct requests.
    x-permissions:
      - role: admin
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/StartBroadcastRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/BroadcastStatus'
      '400':
        description: Invalid source or RTMP URL, connectivity failed
      '409':
        description: Broadcast already active for this source

/stream/broadcast/stop:
  post:
    operationId: stopBroadcast
    summary: Stop an active broadcast
    x-permissions:
      - role: admin
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/StopBroadcastRequest'
    responses:
      '200':
        description: Broadcast stopped
      '404':
        description: Broadcast not found

/stream/broadcast/update:
  post:
    operationId: updateBroadcast
    summary: Update broadcast configuration (causes ~2-3s interruption)
    x-permissions:
      - role: admin
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/UpdateBroadcastRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/BroadcastStatus'
      '400':
        description: Invalid URL or connectivity failed
      '404':
        description: Broadcast not found

/stream/broadcast/status:
  post:
    operationId: getBroadcastStatus
    summary: Get broadcast status
    x-permissions:
      - role: admin
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/BroadcastStatusRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/BroadcastStatus'
      '404':
        description: Broadcast not found

/stream/broadcast/list:
  post:
    operationId: listBroadcasts
    summary: List all active broadcasts
    x-permissions:
      - role: admin
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/ListBroadcastsRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/ListBroadcastsResponse'
```

#### Webhook Endpoints

```yaml
/stream/webhook/twitch:
  post:
    operationId: handleTwitchWebhook
    summary: Handle Twitch EventSub webhook notifications
    x-permissions: []
    requestBody:
      schema:
        $ref: '#/components/schemas/TwitchWebhookPayload'
    responses:
      '200':
        description: Webhook processed
      '400':
        description: Invalid signature or payload

/stream/webhook/youtube:
  post:
    operationId: handleYouTubeWebhook
    summary: Handle YouTube webhook notifications
    x-permissions: []
    requestBody:
      schema:
        $ref: '#/components/schemas/YouTubeWebhookPayload'
    responses:
      '200':
        description: Webhook processed
      '400':
        description: Invalid verification token

/stream/webhook/custom:
  post:
    operationId: handleCustomWebhook
    summary: Handle custom RTMP platform webhook notifications
    x-permissions: []
    requestBody:
      schema:
        $ref: '#/components/schemas/CustomWebhookPayload'
    responses:
      '200':
        description: Webhook processed
      '400':
        description: Invalid signature
```

#### Admin / Debug

```yaml
/stream/admin/pulse/latest:
  post:
    operationId: getLatestPulse
    summary: Get the most recent sentiment pulse for a platform session
    x-permissions:
      - role: developer
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/LatestPulseRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/SentimentPulse'
      '404':
        description: No active session or no pulses yet

/stream/admin/sentiment/test:
  post:
    operationId: testSentimentProcessing
    summary: Test sentiment processing with sample input
    x-permissions:
      - role: developer
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/TestSentimentRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/TestSentimentResponse'
```

### Broadcast Models (NEW)

```yaml
BroadcastSourceType:
  type: string
  enum: [Camera, GameAudio, VoiceRoom]
  description: >
    Type of audio/video source for the broadcast.
    Camera: RTMP input from a game camera.
    GameAudio: Audio source from a game server.
    VoiceRoom: RTP audio from a lib-voice room (requires consent).

StartBroadcastRequest:
  type: object
  required: [sourceType, rtmpOutputUrl]
  properties:
    sourceType:
      $ref: '#/components/schemas/BroadcastSourceType'
    sourceId:
      type: string
      description: >
        Source identifier. For Camera: camera RTMP URL or camera ID.
        For GameAudio: audio source URL. For VoiceRoom: voice room ID
        (but note: voice room broadcasts are initiated via consent events,
        not this endpoint directly).
    rtmpOutputUrl:
      type: string
      description: RTMP destination (e.g., rtmp://live.twitch.tv/app/{key})
    backgroundVideoUrl:
      type: string
      nullable: true
      description: Primary video source (for audio-only sources, provides video background)
    fallbackStreamUrl:
      type: string
      nullable: true
    fallbackImageUrl:
      type: string
      nullable: true
    audioCodec:
      type: string
      default: "aac"
    audioBitrate:
      type: string
      default: "128k"

StopBroadcastRequest:
  type: object
  required: [broadcastId]
  properties:
    broadcastId:
      type: string
      format: uuid

UpdateBroadcastRequest:
  type: object
  required: [broadcastId]
  properties:
    broadcastId:
      type: string
      format: uuid
    rtmpOutputUrl:
      type: string
      nullable: true
    backgroundVideoUrl:
      type: string
      nullable: true
    fallbackStreamUrl:
      type: string
      nullable: true
    fallbackImageUrl:
      type: string
      nullable: true

BroadcastStatusRequest:
  type: object
  required: [broadcastId]
  properties:
    broadcastId:
      type: string
      format: uuid

BroadcastStatus:
  type: object
  properties:
    broadcastId:
      type: string
      format: uuid
    sourceType:
      $ref: '#/components/schemas/BroadcastSourceType'
    isActive:
      type: boolean
    rtmpOutputUrl:
      type: string
      nullable: true
      description: Masked URL (stream key hidden)
    currentVideoSource:
      type: string
      nullable: true
    videoSourceType:
      type: string
      enum: [primary_stream, fallback_stream, fallback_image, default, black]
      nullable: true
    startedAt:
      type: string
      format: date-time
      nullable: true
    durationSeconds:
      type: integer
      nullable: true
    health:
      type: string
      enum: [healthy, degraded, unhealthy, stopped]

ListBroadcastsRequest:
  type: object
  properties:
    activeOnly:
      type: boolean
      default: true

ListBroadcastsResponse:
  type: object
  required: [broadcasts]
  properties:
    broadcasts:
      type: array
      items:
        $ref: '#/components/schemas/BroadcastStatus'
```

### Fallback Cascade (from VOICE-STREAMING.md)

```
Primary RTMP (backgroundVideoUrl)
  └─ Failed → Fallback Stream (fallbackStreamUrl)
       └─ Failed → Fallback Image (fallbackImageUrl)
            └─ Failed → Default Background (STREAM_DEFAULT_BACKGROUND_VIDEO)
                 └─ Failed → Black Video (lavfi color=black)
```

Each fallback publishes `stream.broadcast.source-changed` events.

### BroadcastCoordinator Service

Location: `lib-stream/Services/BroadcastCoordinator.cs`

```csharp
/// <summary>
/// Manages FFmpeg processes for RTMP broadcasting.
/// Singleton lifetime - survives individual request scopes.
/// Absorbed from VOICE-STREAMING.md's StreamingCoordinator design.
/// </summary>
public interface IBroadcastCoordinator
{
    Task<(StatusCodes, BroadcastStatus?)> StartBroadcastAsync(
        StartBroadcastRequest request, CancellationToken ct = default);

    Task<(StatusCodes, BroadcastStatus?)> StopBroadcastAsync(
        Guid broadcastId, CancellationToken ct = default);

    Task<(StatusCodes, BroadcastStatus?)> UpdateBroadcastAsync(
        Guid broadcastId, UpdateBroadcastRequest request, CancellationToken ct = default);

    BroadcastStatus? GetBroadcastStatus(Guid broadcastId);

    IReadOnlyList<BroadcastStatus> GetAllActiveBroadcasts();
}
```

**Key Implementation Details** (carried from VOICE-STREAMING.md):
- Track FFmpeg processes in `ConcurrentDictionary<Guid, BroadcastContext>`
- Validate RTMP URLs via FFprobe before accepting
- Monitor process health via stderr parsing
- Auto-restart on crash if `BroadcastRestartOnFailure` enabled
- Stream key masking: never log or return full RTMP URLs
- Publish client events via `IClientEventPublisher`

### Service Events

```yaml
# Published by lib-stream
stream.platform.linked:
  schema:
    StreamPlatformLinkedEvent:
      type: object
      required: [eventId, timestamp, accountId, linkId, platform]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        accountId: { type: string, format: uuid }
        linkId: { type: string, format: uuid }
        platform: { $ref: '#/components/schemas/StreamPlatform' }

stream.platform.unlinked:
  schema:
    StreamPlatformUnlinkedEvent:
      type: object
      required: [eventId, timestamp, accountId, linkId, platform]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        accountId: { type: string, format: uuid }
        linkId: { type: string, format: uuid }
        platform: { $ref: '#/components/schemas/StreamPlatform' }

stream.platform.session.started:
  schema:
    StreamPlatformSessionStartedEvent:
      type: object
      required: [eventId, timestamp, platformSessionId, accountId, platform]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        platformSessionId: { type: string, format: uuid }
        accountId: { type: string, format: uuid }
        platform: { $ref: '#/components/schemas/StreamPlatform' }
        inGameSessionId: { type: string, format: uuid, nullable: true }

stream.platform.session.ended:
  schema:
    StreamPlatformSessionEndedEvent:
      type: object
      required: [eventId, timestamp, platformSessionId, accountId, platform, durationSeconds]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        platformSessionId: { type: string, format: uuid }
        accountId: { type: string, format: uuid }
        platform: { $ref: '#/components/schemas/StreamPlatform' }
        durationSeconds: { type: integer }
        peakViewerCount: { type: integer }

stream.audience.pulse:
  description: >
    Batched sentiment data from a real audience. Privacy-safe: contains
    no text content, no platform usernames, no PII.
  schema:
    $ref: '#/components/schemas/SentimentPulse'

stream.broadcast.started:
  description: A broadcast to an RTMP endpoint has started
  schema:
    StreamBroadcastStartedEvent:
      type: object
      required: [eventId, timestamp, broadcastId, sourceType]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        broadcastId: { type: string, format: uuid }
        sourceType: { $ref: '#/components/schemas/BroadcastSourceType' }

stream.broadcast.stopped:
  description: A broadcast has stopped
  schema:
    StreamBroadcastStoppedEvent:
      type: object
      required: [eventId, timestamp, broadcastId, reason]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        broadcastId: { type: string, format: uuid }
        reason: { type: string, enum: [manual, error, source_disconnected, consent_revoked] }
```

### Event Subscriptions (lib-stream consumes)

```yaml
x-event-subscriptions:
  # From lib-voice (L3, optional)
  - topic: voice.room.broadcast.approved
    event: VoiceRoomBroadcastApprovedEvent
    description: >
      Start RTMP output for a voice room after all participants consented.
      Connects to the room's RTP audio endpoint.

  - topic: voice.room.broadcast.stopped
    event: VoiceRoomBroadcastStoppedEvent
    description: >
      Stop RTMP output for a voice room. Consent revoked or room closed.

  # From game engines (camera discovery)
  - topic: camera.stream.started
    event: CameraStreamStartedEvent
    description: >
      A game engine camera stream became available. Register as a
      potential video source for broadcasts.

  - topic: camera.stream.ended
    event: CameraStreamEndedEvent
    description: >
      A game engine camera stream ended. Remove from available sources.
```

### State Stores

```yaml
stream-platforms:
  backend: mysql
  description: >
    Platform link records including encrypted OAuth tokens.

stream-sessions:
  backend: redis
  description: >
    Active platform session tracking. Ephemeral.

stream-sentiment-buffer:
  backend: redis
  description: >
    Buffered sentiments awaiting batch publication. TTL-based cleanup.

stream-broadcasts:
  backend: redis
  description: >
    Active broadcast tracking. FFmpeg process state, source info.
    Ephemeral -- cleaned up when broadcast stops.

stream-cameras:
  backend: redis
  description: >
    Discovered camera sources from game engines. TTL-based.
```

### Configuration

```yaml
# stream-configuration.yaml
x-service-configuration:
  # Feature Flags
  StreamEnabled:
    type: boolean
    env: STREAM_ENABLED
    default: false

  # Platform OAuth (unchanged from v1)
  TwitchClientId:
    type: string
    env: STREAM_TWITCH_CLIENT_ID
    default: ""
  TwitchClientSecret:
    type: string
    env: STREAM_TWITCH_CLIENT_SECRET
    default: ""
  TwitchWebhookSecret:
    type: string
    env: STREAM_TWITCH_WEBHOOK_SECRET
    default: ""
  YouTubeClientId:
    type: string
    env: STREAM_YOUTUBE_CLIENT_ID
    default: ""
  YouTubeClientSecret:
    type: string
    env: STREAM_YOUTUBE_CLIENT_SECRET
    default: ""
  YouTubeWebhookVerificationToken:
    type: string
    env: STREAM_YOUTUBE_WEBHOOK_TOKEN
    default: ""

  # Sentiment Processing (unchanged from v1)
  SentimentPulseIntervalSeconds:
    type: integer
    env: STREAM_SENTIMENT_PULSE_INTERVAL_SECONDS
    default: 15
  SentimentMinBatchSize:
    type: integer
    env: STREAM_SENTIMENT_MIN_BATCH_SIZE
    default: 5
  SentimentMaxBatchSize:
    type: integer
    env: STREAM_SENTIMENT_MAX_BATCH_SIZE
    default: 200

  # Tracked Viewer Configuration (unchanged from v1)
  MaxTrackedViewersPerSession:
    type: integer
    env: STREAM_MAX_TRACKED_VIEWERS_PER_SESSION
    default: 50
  TrackedViewerEngagementThreshold:
    type: number
    format: float
    env: STREAM_TRACKED_VIEWER_ENGAGEMENT_THRESHOLD
    default: 0.7
  TrackSubscribers:
    type: boolean
    env: STREAM_TRACK_SUBSCRIBERS
    default: true
  TrackModerators:
    type: boolean
    env: STREAM_TRACK_MODERATORS
    default: true
  TrackRaidLeaders:
    type: boolean
    env: STREAM_TRACK_RAID_LEADERS
    default: true

  # Token Encryption
  TokenEncryptionKey:
    type: string
    env: STREAM_TOKEN_ENCRYPTION_KEY
    default: ""

  # Session Cleanup
  SessionHistoryRetentionHours:
    type: integer
    env: STREAM_SESSION_HISTORY_RETENTION_HOURS
    default: 168

  # Broadcast Configuration (NEW -- from VOICE-STREAMING.md)
  BroadcastEnabled:
    type: boolean
    env: STREAM_BROADCAST_ENABLED
    default: false
  FfmpegPath:
    type: string
    env: STREAM_FFMPEG_PATH
    default: "/usr/bin/ffmpeg"
  DefaultBackgroundVideo:
    type: string
    env: STREAM_DEFAULT_BACKGROUND_VIDEO
    default: "/opt/bannou/backgrounds/default.mp4"
  MaxConcurrentBroadcasts:
    type: integer
    env: STREAM_MAX_CONCURRENT_BROADCASTS
    default: 10
  BroadcastAudioCodec:
    type: string
    env: STREAM_BROADCAST_AUDIO_CODEC
    default: "aac"
  BroadcastAudioBitrate:
    type: string
    env: STREAM_BROADCAST_AUDIO_BITRATE
    default: "128k"
  BroadcastVideoCodec:
    type: string
    env: STREAM_BROADCAST_VIDEO_CODEC
    default: "libvpx"
  BroadcastRestartOnFailure:
    type: boolean
    env: STREAM_BROADCAST_RESTART_ON_FAILURE
    default: true
  BroadcastHealthCheckIntervalSeconds:
    type: integer
    env: STREAM_BROADCAST_HEALTH_CHECK_INTERVAL_SECONDS
    default: 10
  RtmpProbeTimeoutSeconds:
    type: integer
    env: STREAM_RTMP_PROBE_TIMEOUT_SECONDS
    default: 5

  # Auto-broadcast (server-side content, no player involvement)
  AutoBroadcastCameraId:
    type: string
    env: STREAM_AUTO_BROADCAST_CAMERA_ID
    default: ""
    description: Camera ID to auto-broadcast on startup (empty = disabled)
  AutoBroadcastRtmpUrl:
    type: string
    env: STREAM_AUTO_BROADCAST_RTMP_URL
    default: ""
    description: RTMP destination for auto-broadcast (empty = disabled)
```

### Background Services

1. **TokenRefreshWorker**: Periodically refreshes OAuth tokens for linked platforms before they expire.
2. **WebhookSubscriptionManager**: Ensures webhook subscriptions are active for all linked platforms.
3. **SentimentBatchPublisher**: Drains the sentiment buffer at the configured pulse interval and publishes `stream.audience.pulse` events.
4. **SessionCleanupWorker**: Purges ended session records older than the configured retention period.
5. **BroadcastHealthMonitor** (NEW): Monitors FFmpeg process health via stderr parsing, auto-restarts on crash, publishes health events.
6. **AutoBroadcastStarter** (NEW): On startup, checks `AutoBroadcastCameraId` and `AutoBroadcastRtmpUrl` config. If both set, starts a broadcast automatically.

### License Compliance (Tenet 18)

| Component | License | Status | Notes |
|-----------|---------|--------|-------|
| **FFmpeg** | LGPL v2.1+ | Container | Process isolation, not linked |
| **MediaMTX** | MIT | Approved | Optional test RTMP server |

FFmpeg runs as a separate process with network/IPC communication only. No code linking occurs. Must use LGPL-compliant build (no `--enable-gpl`, `--enable-nonfree`, `libx264`, `libx265`).

---

## lib-showtime: L4 Game Feature (In-Game Streaming Metagame)

### Service Identity

| Property | Value |
|----------|-------|
| **Layer** | L4 (GameFeatures) |
| **Plugin** | `plugins/lib-showtime/` |
| **Schema prefix** | `showtime` |
| **Service name** | `showtime` |
| **Hard dependencies** | L0 (state, messaging), L1 (account, auth, permission), L2 (game-session, game-service, character) |
| **Soft dependencies** | L3 (lib-stream for real audience, lib-voice for room awareness), L4 (lib-seed for career, lib-collection for unlocks, lib-currency for tips, lib-relationship for follows, lib-analytics for reporting) |
| **When absent** | No streaming metagame; voice and platform streaming still work independently |

### What This Service Does

1. **Simulated Audience Pool Management**: Maintains a pool of lightweight audience member data objects with personality, interest, and engagement flags
2. **In-Game Stream Sessions**: Creates and manages streaming sessions within the game world
3. **Audience Matching & Assignment**: Matches audience members to stream sessions based on personality/interest
4. **Hype Train Mechanics**: Event-driven excitement generation with escalating levels
5. **Follow/Subscribe Dynamics**: Audience members follow streamers based on sustained interest
6. **Real Audience Blending**: When lib-stream (L3) is available, ingests sentiment pulses and creates "real-derived" audience members
7. **Streamer Career Progression**: Manages streamer growth via Seed composability
8. **Voice Room Orchestration**: When lib-voice (L3) is available, creates voice rooms for game sessions that want voice
9. **Broadcast Orchestration**: When lib-stream (L3) is available, coordinates broadcasting by passing RTMP URLs and managing the consent flow

### What This Service Does NOT Do

- Does not connect to external platforms (that's lib-stream L3)
- Does not handle FFmpeg/RTMP (that's lib-stream L3)
- Does not manage voice room primitives (that's lib-voice L3)
- Does not process raw chat messages or platform events
- Does not store any PII

### Key Change From v1: Game-Session Integration

lib-showtime (L4) now owns the "game sessions have voice" orchestration that previously lived in GameSession (L2):

```
game-session.created event
    │
    ▼
lib-showtime subscribes, checks if game session wants voice
    │ yes
    ▼
lib-showtime calls lib-voice (L3) to create voice room
    │
    ▼
lib-showtime stores voice room ID ↔ game session ID mapping
    │
    ▼
game-session.ended event → lib-showtime deletes voice room via lib-voice
```

This eliminates the L2→L4 hierarchy violation entirely. GameSession publishes events; lib-showtime consumes them and orchestrates the voice+streaming stack.

### API Endpoints, Models, Events, State Stores, Configuration

The lib-showtime API surface, models, events, state stores, and configuration are **unchanged from v1** of this document. The complete specifications are maintained below for reference. The only change is in the event subscriptions:

### Updated Event Subscriptions

```yaml
x-event-subscriptions:
  # From lib-stream (L3, optional)
  - topic: stream.audience.pulse
    event: SentimentPulse
    description: >
      Real audience sentiment data for blending with simulated audience.
      Graceful degradation: if this event never arrives (L3 absent),
      streaming operates on 100% simulated audiences.

  - topic: stream.platform.session.started
    event: StreamPlatformSessionStartedEvent
    description: >
      A linked account went live. If associated with an in-game session,
      start real audience blending.

  # From lib-voice (L3, optional)
  - topic: voice.room.created
    event: VoiceRoomCreatedEvent
    description: >
      Track voice room creation for audience context. "Streamer is in
      a voice room with N participants" affects audience behavior.

  - topic: voice.participant.joined
    event: VoiceParticipantJoinedEvent
    description: Track voice room participant changes for audience context.

  - topic: voice.participant.left
    event: VoiceParticipantLeftEvent
    description: Track voice room participant changes for audience context.

  # From game services (L2)
  - topic: game-session.created
    event: GameSessionCreatedEvent
    description: >
      Create voice room (via lib-voice) and/or streaming session
      if the game session is configured for voice/streaming.

  - topic: game-session.ended
    event: GameSessionEndedEvent
    description: >
      End any associated streaming sessions and delete voice rooms.

  # Self-generated events for background processing
  - topic: showtime.session.ended
    event: ShowtimeSessionEndedEvent
    description: Trigger career progression and cleanup on session end.
```

---

## The Real vs. Simulated Audience Metagame

The blending creates a natural Turing test:

**Simulated audience members** are generated algorithmically with personality/interest flags. Their behavior is predictable within their personality parameters -- a "Loyal, Combat-focused" simulated viewer always reacts positively to combat moments and always comes back.

**Real-derived audience members** are created from L3 sentiment pulses. Their behavior is unpredictable because it reflects actual human reactions. A real viewer might:
- Get excited about something niche that no simulated personality profile would predict
- Leave at an unexpected moment (bathroom break, real-life interruption)
- Return after a long absence with no algorithmic explanation
- React to meta-game context (knowing the streamer's reputation from out-of-game sources)

**The metagame**: Players may start noticing that some audience members behave "differently" -- more erratically, more surprisingly, more humanly. The game never labels which are real. But keen players might develop theories, and that speculation IS the metagame.

**Design rule**: The game UI NEVER reveals the real/simulated distinction to players. The `isRealDerived` flag on `AudienceFollower` is admin-only visibility for debugging and analytics.

---

## Composability Map

### Seed: Streamer Career

```yaml
Seed Type: "streamer"
Growth Domains:
  audience_growth:     # Total followers gained
  engagement_quality:  # Average audience engagement across sessions
  content_diversity:   # Variety of content tags across sessions
  discovery_rate:      # World-first discoveries per stream hour
Phase Labels:
  - Unknown     # No streams yet
  - Rising      # Starting to attract regular audience
  - Popular     # Consistent audience, multiple followers
  - Celebrity   # Large following, hype trains common
  - Legend      # Server-wide recognition, record-breaking streams
```

### Collection: Streaming Unlocks

| Collection Category | Example Entries | Trigger |
|-------------------|-----------------|---------|
| Streaming Milestones | "First Stream", "100 Followers", "1000 Watch Hours" | `showtime.milestone.reached` events |
| Hype Achievements | "First Hype Train", "Level 5 Hype", "Legendary Hype" | `showtime.hype.completed` events |
| World-First Streams | "Discovered [X] On Stream" | `showtime.milestone.reached` with WorldFirstCount |

### Currency: Virtual Tips

```yaml
Currency Type: "stream_tip"
Scope: Per game service
Properties:
  - Non-transferable (audience members "tip" the streamer)
  - Generated by audience engagement events (not real money)
  - Spent on stream customization
  - Autogain: passive tip generation from follower count
```

### Contract: Sponsorship Deals

```yaml
Contract Template: "stream_sponsorship"
Parties:
  - Role: sponsor (NPC merchant, guild, game entity)
  - Role: streamer
Milestones:
  - "stream_hours": Stream X hours with sponsor's product visible
  - "audience_reach": Reach X total viewers during sponsored streams
  - "hype_generation": Trigger X hype trains during sponsored streams
Prebound API on completion:
  - Credit streamer with sponsorship payment (Currency)
  - Grant sponsor reputation boost (Seed growth)
```

---

## Open Design Questions

1. **Sentiment processing approach**: Simple keyword/emoji matching, or lightweight NLP model?

2. **Cross-session tracking ID persistence**: Currently, tracking IDs are destroyed when a platform session ends. Should there be "returner" detection via hashed platform user IDs?

3. **NPC streamers**: Should NPC streams have simulated audiences by default? Should players "watch" NPC streams in-game?

4. **Collective audience groups**: One object representing 20 people. Start individual-only and add collectives when pool size demands it?

5. **Interest matching algorithm weights**: Configurable or hardcoded?

6. **Streamer raids**: Should in-game streamers be able to "raid" other streams?

7. **Voice room broadcasting UX**: When a player requests broadcast, what's the client-side consent UX? Modal dialog? In-room notification with timer? Veto-based (proceeds unless someone objects)?

8. **Realm-specific manifestation**: In Omega, streaming is explicit. In Arcadia, same mechanics as "performing for a crowd." How much realm-specific logic in lib-showtime vs. client rendering?

---

## Implementation Priority

### Phase 1: lib-voice L3 Redesign

Redesign lib-voice as L3 AppFeatures. See [VOICE-STREAMING.md](VOICE-STREAMING.md) for details.

- Strip game-session references from API schema
- Change layer from GameFeatures to AppFeatures
- Add domain event publishing (room lifecycle, participant changes)
- Add broadcast consent flow (request/approve/stop)
- Add participant TTL enforcement background worker
- Preserve internal helpers (P2P, Scaled, SIP, Kamailio, RTPEngine)

### Phase 2: lib-showtime (L4) Standalone

Build the in-game streaming metagame with 100% simulated audiences.

- Audience pool management
- Stream session lifecycle
- Audience matching engine
- Hype train mechanics
- Game-session event subscription (creates voice rooms via lib-voice)
- Seed integration for streamer career

### Phase 3: lib-stream (L3) Core

Build the platform integration layer.

- Platform account linking (Twitch first, YouTube second)
- Webhook ingestion for Twitch EventSub
- Sentiment processing pipeline
- Sentiment pulse publishing
- Session management

### Phase 4: lib-stream Broadcast Management

Add RTMP output capabilities to lib-stream.

- BroadcastCoordinator (FFmpeg process management)
- RTMP URL validation via FFprobe
- Fallback cascade
- Voice room broadcast via consent event subscription
- Camera discovery event subscription
- Server-side auto-broadcast from ENV config
- Health monitoring + auto-restart

### Phase 5: Real Audience Blending

Connect L3 to L4.

- Sentiment pulse consumer in lib-showtime
- Real-derived audience member creation
- Tracking ID consistency across pulses
- Blending with simulated audience behavior

### Phase 6: Polish & Enrichment

- Collection integration for streaming milestones
- Currency integration for virtual tips
- Contract integration for sponsorship deals
- Leaderboard integration for streamer rankings

---

*This document is self-contained for schema generation. All model shapes, event schemas, configuration properties, state stores, and API endpoint signatures are specified at sufficient detail to produce YAML schemas without referencing external documentation.*
