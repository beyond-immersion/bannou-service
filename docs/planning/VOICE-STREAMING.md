# lib-voice L3 Redesign: Pure Voice Rooms

> **Status**: Design Draft
> **Last Updated**: 2026-02-10
> **Supersedes**: Previous VOICE-STREAMING.md (RTMP I/O plan, absorbed into lib-stream in STREAMING-ARCHITECTURE.md)
> **Parent Document**: [STREAMING-ARCHITECTURE.md](STREAMING-ARCHITECTURE.md) (three-service architecture)
> **Related Issues**: GameSession→Voice hierarchy violation (documented in VOICE.md deep dive)

---

## Executive Summary

Redesign lib-voice from L4 (GameFeatures) to L3 (AppFeatures) by stripping all game concepts and making it a pure voice room service. This eliminates the GameSession (L2) → Voice (L4) hierarchy violation, enables voice rooms to function independently of any game deployment, and positions voice as a platform primitive usable by any application built on Bannou.

**What changes**:
- Layer classification: L4 GameFeatures → L3 AppFeatures
- API descriptions: Remove game-specific language (sessionId is already an L1 Connect/Auth concept, not a game concept -- no rename needed)
- Service events: New room lifecycle and broadcast consent events published via IMessageBus
- Broadcast consent flow: New endpoints and client events for voice broadcasting approval
- Background worker: Participant TTL enforcement (missing from current implementation)

**What stays the same**:
- All internal helper services (P2PCoordinator, ScaledTierCoordinator, SipEndpointRegistry, KamailioClient, RtpEngineClient)
- WebRTC signaling flow (SDP offer/answer relay, ICE candidate exchange)
- P2P mesh topology and scaled SFU tier with automatic upgrade
- Permission-gated SDP exchange via `voice:ringing` state
- Client events for peer join/leave/update/tier-upgrade/room-closed
- Redis state storage patterns and key structure
- Kamailio/RTPEngine infrastructure integration
- The `sessionId` field in all request/response models (it's a Connect session ID, not a game concept)

**What moves elsewhere**:
- RTMP output management → lib-stream (L3) `BroadcastCoordinator`
- Game session lifecycle orchestration → lib-streaming (L4) event subscriptions
- FFmpeg process management → lib-stream (L3)
- Camera discovery event handling → lib-stream (L3)

---

## Why L3, Not L4

The current lib-voice is L4 because it was built before the service hierarchy existed and was tightly coupled to GameSession. But voice rooms have no inherent game dependency:

| Question | Answer |
|----------|--------|
| Does voice need to know about games? | No -- rooms are generic containers for voice participants |
| Does voice need game sessions? | No -- any service can create a room with a sessionId (which is a Connect/Auth concept, not game-specific) |
| Does voice need characters or realms? | No -- participants are identified by WebSocket session IDs |
| Does voice need subscriptions? | No -- access control is the caller's responsibility |
| Can voice work without L2 services? | Yes -- it only needs L0 (state, messaging) and L1 (connect, auth, permission) |

**L3 AppFeatures is the correct classification** because:
- Voice provides optional non-game infrastructure (like Asset, Orchestrator, Documentation, Website)
- Voice depends only on L0 and L1 -- it cannot access L2 or L4
- Voice can be disabled without breaking any lower-layer service
- Voice enables any Bannou deployment to have real-time audio communication

**The hierarchy violation is eliminated entirely**:
- Before: GameSession (L2) → Voice (L4) = VIOLATION
- After: lib-streaming (L4) → Voice (L3) = VALID (L4 can soft-depend on L3)

---

## Architecture

### Service Identity

| Property | Current (L4) | Redesigned (L3) |
|----------|-------------|-----------------|
| **Layer** | L4 (GameFeatures) | L3 (AppFeatures) |
| **Plugin** | `plugins/lib-voice/` | `plugins/lib-voice/` (same) |
| **Schema prefix** | `voice` | `voice` (same) |
| **Hard dependencies** | L0, L1, IGameSessionClient (L2 VIOLATION) | L0, L1 (connect, auth, permission) |
| **Soft dependencies** | None | None |
| **Cannot depend on** | (violated -- depended on L2) | L2, L4 |
| **When absent** | Game sessions break (violation) | Voice unavailable; lib-stream can still broadcast server content; lib-streaming runs without voice |

### Dependency Diagram

```
lib-voice (L3 AppFeatures)
    │
    ├──hard──► lib-state (L0)        # Room data, participant registrations
    ├──hard──► lib-messaging (L0)    # Service events, error publishing
    ├──hard──► lib-connect (L0/L1)   # IClientEventPublisher for WebSocket push
    ├──hard──► lib-permission (L1)   # voice:ringing state for SDP exchange
    └──hard──► lib-auth (L1)         # Session validation

    NO dependencies on L2 or L4
```

### Who Calls Voice

```
                              lib-voice (L3)
                                    ▲
                                    │
                ┌───────────────────┼────────────────────┐
                │                   │                    │
        lib-streaming (L4)    lib-stream (L3)     Any future L3-L5
        Creates/deletes       Reads RTP endpoint   service that needs
        rooms on game-        for RTMP output      voice rooms
        session events        after consent
```

**Key insight**: Voice doesn't know or care who creates rooms. It provides a room API; higher layers decide when and why to use it.

---

## API Schema Changes

### voice-api.yaml Modifications

The API schema changes are minimal. The `sessionId` field is **not renamed** -- it refers to the WebSocket session ID generated by lib-connect (L1), not a game session. It was always an L1 concept; only the descriptions incorrectly framed it as game-specific.

#### x-service-layer Addition

```yaml
# Add to voice-api.yaml top-level
x-service-layer: AppFeatures  # Changed from GameFeatures (or unset, which defaulted to GameFeatures)
```

#### Request/Response Models: No Field Changes

All existing fields (`sessionId`, `roomId`, `tier`, etc.) remain as-is. The `sessionId` in `CreateVoiceRoomRequest` is the Connect/Auth session ID -- a perfectly appropriate identifier for an L3 service. The only changes are to description text.

#### Description Updates

All endpoint descriptions that reference "game session" should be updated to remove the game-specific framing:

| Endpoint | Before | After |
|----------|--------|-------|
| `/voice/room/create` | "Creates a new voice room associated with a game session. Called by GameSession service..." | "Creates a new voice room associated with a session. Any service can call this." |
| `/voice/room/join` | "Called by GameSession service when a player joins a session." | "Registers a participant in the voice room." |
| `/voice/room/leave` | "Called by GameSession service when a player leaves a session." | "Removes a participant from the voice room." |
| `/voice/room/delete` | "Called by GameSession service when a session is deleted." | "Deletes a voice room and notifies all participants." |
| Top-level description | "Mostly Internal Service... accessed by GameSession service via service-to-service calls" | "Voice room coordination service. Internal service accessed by other services via lib-mesh." |

#### New Endpoints: Broadcast Consent

```yaml
# New endpoints for voice broadcasting consent flow

/voice/room/broadcast/request:
  post:
    summary: Request broadcast consent from all room participants
    description: >
      Initiates the broadcast consent flow. All current room participants
      receive a VoiceBroadcastConsentRequestEvent. Broadcasting only starts
      after all participants consent. If ANY participant declines, the
      broadcast request is denied.

      This endpoint is the ONLY way to initiate voice room broadcasting.
      lib-stream subscribes to the resulting approval/decline events.
    operationId: requestBroadcastConsent
    tags:
    - Voice Broadcasting
    x-permissions:
    - role: user
      states:
        voice: in_room
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/BroadcastConsentRequest'
    responses:
      '200':
        description: Consent request sent to all participants
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/BroadcastConsentStatus'
      '404':
        description: Room not found
      '409':
        description: Broadcast already active or consent pending

/voice/room/broadcast/consent:
  post:
    summary: Respond to a broadcast consent request
    description: >
      Called by each participant to consent or decline broadcasting.
      When all participants consent, lib-voice publishes
      voice.room.broadcast.approved. If any participant declines,
      lib-voice publishes voice.room.broadcast.declined.
    operationId: respondBroadcastConsent
    tags:
    - Voice Broadcasting
    x-permissions:
    - role: user
      states:
        voice: consent_pending
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/BroadcastConsentResponse'
    responses:
      '200':
        description: Consent response recorded
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/BroadcastConsentStatus'
      '404':
        description: Room not found or no pending consent request

/voice/room/broadcast/stop:
  post:
    summary: Stop broadcasting from a voice room
    description: >
      Any participant can stop an active broadcast at any time.
      This is equivalent to revoking consent. Publishes
      voice.room.broadcast.stopped with reason consent_revoked.
    operationId: stopBroadcast
    tags:
    - Voice Broadcasting
    x-permissions:
    - role: user
      states:
        voice: in_room
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/StopBroadcastConsentRequest'
    responses:
      '200':
        description: Broadcast stopped
      '404':
        description: Room not found or not broadcasting

/voice/room/broadcast/status:
  post:
    summary: Get broadcast status for a voice room
    description: >
      Returns the current broadcast state: whether consent is pending,
      active, or inactive.
    operationId: getBroadcastStatus
    tags:
    - Voice Broadcasting
    x-permissions:
    - role: user
      states:
        voice: in_room
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/BroadcastStatusRequest'
    responses:
      '200':
        description: Broadcast status
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/BroadcastConsentStatus'
      '404':
        description: Room not found
```

#### New Request/Response Models

```yaml
BroadcastConsentState:
  type: string
  enum: [inactive, pending, approved, broadcasting]
  description: >
    Current state of broadcast consent for a room.
    inactive: No broadcast request pending.
    pending: Consent request sent, awaiting all responses.
    approved: All participants consented, waiting for lib-stream.
    broadcasting: RTMP output active (confirmed by lib-stream event).

BroadcastConsentRequest:
  type: object
  additionalProperties: false
  required:
  - roomId
  properties:
    roomId:
      type: string
      format: uuid
      description: Voice room to broadcast
    requestingSessionId:
      type: string
      format: uuid
      description: Session ID of the participant requesting broadcast

BroadcastConsentResponse:
  type: object
  additionalProperties: false
  required:
  - roomId
  - sessionId
  - consented
  properties:
    roomId:
      type: string
      format: uuid
    sessionId:
      type: string
      format: uuid
      description: Session ID of the responding participant
    consented:
      type: boolean
      description: True if participant consents to broadcasting

StopBroadcastConsentRequest:
  type: object
  additionalProperties: false
  required:
  - roomId
  properties:
    roomId:
      type: string
      format: uuid
    sessionId:
      type: string
      format: uuid
      description: Session ID of the participant stopping the broadcast

BroadcastStatusRequest:
  type: object
  additionalProperties: false
  required:
  - roomId
  properties:
    roomId:
      type: string
      format: uuid

BroadcastConsentStatus:
  type: object
  additionalProperties: false
  properties:
    roomId:
      type: string
      format: uuid
    state:
      $ref: '#/components/schemas/BroadcastConsentState'
    requestedBySessionId:
      type: string
      format: uuid
      nullable: true
      description: Who initiated the broadcast request (null if inactive)
    consentedSessionIds:
      type: array
      items:
        type: string
        format: uuid
      description: Sessions that have consented so far
    pendingSessionIds:
      type: array
      items:
        type: string
        format: uuid
      description: Sessions that haven't responded yet
    rtpAudioEndpoint:
      type: string
      nullable: true
      description: >
        RTP audio endpoint for the room's mixed audio output. Only populated
        when room is in scaled tier. Provided to lib-stream so it can connect
        its RTMP output to the voice room's audio.
```

---

## Service Events (NEW)

The current voice-events.yaml is completely empty (`x-event-publications: []`, `x-event-subscriptions: []`). The redesign adds room lifecycle events and broadcast consent events.

### Published Events

```yaml
# voice-events.yaml (replacing the empty stub)
x-event-publications:
  - topic: voice.room.created
    event: VoiceRoomCreatedEvent
    description: Published when a voice room is created

  - topic: voice.room.deleted
    event: VoiceRoomDeletedEvent
    description: Published when a voice room is deleted

  - topic: voice.room.tier-upgraded
    event: VoiceRoomTierUpgradedEvent
    description: Published when a room upgrades from P2P to scaled tier

  - topic: voice.participant.joined
    event: VoiceParticipantJoinedEvent
    description: Published when a participant joins a room

  - topic: voice.participant.left
    event: VoiceParticipantLeftEvent
    description: Published when a participant leaves a room

  - topic: voice.room.broadcast.approved
    event: VoiceRoomBroadcastApprovedEvent
    description: All participants consented to broadcasting

  - topic: voice.room.broadcast.declined
    event: VoiceRoomBroadcastDeclinedEvent
    description: A participant declined broadcast consent

  - topic: voice.room.broadcast.stopped
    event: VoiceRoomBroadcastStoppedEvent
    description: Broadcasting stopped (consent revoked, room closed, or manual stop)

x-event-subscriptions: []  # Voice does not consume external events
```

### Event Schemas

```yaml
components:
  schemas:
    VoiceRoomCreatedEvent:
      type: object
      description: Published when a voice room is created
      required: [eventId, timestamp, roomId, sessionId, tier, maxParticipants]
      properties:
        eventId:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        roomId:
          type: string
          format: uuid
        sessionId:
          type: string
          format: uuid
          description: Connect/Auth session ID of the room creator
        tier:
          $ref: 'voice-api.yaml#/components/schemas/VoiceTier'
        maxParticipants:
          type: integer

    VoiceRoomDeletedEvent:
      type: object
      description: Published when a voice room is deleted
      required: [eventId, timestamp, roomId, reason]
      properties:
        eventId:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        roomId:
          type: string
          format: uuid
        reason:
          type: string
          enum: [manual, empty, error]

    VoiceRoomTierUpgradedEvent:
      type: object
      description: Published when a room upgrades from P2P to scaled tier
      required: [eventId, timestamp, roomId, previousTier, newTier]
      properties:
        eventId:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        roomId:
          type: string
          format: uuid
        previousTier:
          $ref: 'voice-api.yaml#/components/schemas/VoiceTier'
        newTier:
          $ref: 'voice-api.yaml#/components/schemas/VoiceTier'
        rtpAudioEndpoint:
          type: string
          nullable: true
          description: RTP mixed audio endpoint (for lib-stream to connect to)

    VoiceParticipantJoinedEvent:
      type: object
      description: Published when a participant joins a room
      required: [eventId, timestamp, roomId, participantSessionId, currentCount]
      properties:
        eventId:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        roomId:
          type: string
          format: uuid
        participantSessionId:
          type: string
          format: uuid
        currentCount:
          type: integer

    VoiceParticipantLeftEvent:
      type: object
      description: Published when a participant leaves a room
      required: [eventId, timestamp, roomId, participantSessionId, remainingCount]
      properties:
        eventId:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        roomId:
          type: string
          format: uuid
        participantSessionId:
          type: string
          format: uuid
        remainingCount:
          type: integer

    VoiceRoomBroadcastApprovedEvent:
      type: object
      description: >
        All participants consented to broadcasting. lib-stream subscribes
        to this to start RTMP output from the room's audio source.
      required: [eventId, timestamp, roomId, requestedBySessionId]
      properties:
        eventId:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        roomId:
          type: string
          format: uuid
        requestedBySessionId:
          type: string
          format: uuid
        rtpAudioEndpoint:
          type: string
          description: RTP endpoint for mixed audio (for lib-stream to connect RTMP output)

    VoiceRoomBroadcastDeclinedEvent:
      type: object
      description: A participant declined broadcast consent
      required: [eventId, timestamp, roomId, declinedBySessionId]
      properties:
        eventId:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        roomId:
          type: string
          format: uuid
        declinedBySessionId:
          type: string
          format: uuid

    VoiceRoomBroadcastStoppedEvent:
      type: object
      description: Broadcasting stopped
      required: [eventId, timestamp, roomId, reason]
      properties:
        eventId:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        roomId:
          type: string
          format: uuid
        reason:
          type: string
          enum: [consent_revoked, room_closed, manual, error]
```

### Who Consumes These Events

| Event | Consumer | Why |
|-------|----------|-----|
| `voice.room.created` | lib-streaming (L4) | Track voice rooms for audience context |
| `voice.room.deleted` | lib-streaming (L4) | Clean up streaming session associations |
| `voice.room.tier-upgraded` | lib-stream (L3) | Update RTP audio endpoint for active broadcasts |
| `voice.participant.joined` | lib-streaming (L4) | Adjust audience behavior based on room size |
| `voice.participant.left` | lib-streaming (L4) | Adjust audience behavior based on room size |
| `voice.room.broadcast.approved` | lib-stream (L3) | Start RTMP output for the voice room |
| `voice.room.broadcast.declined` | (nobody currently) | Informational; could drive UI feedback |
| `voice.room.broadcast.stopped` | lib-stream (L3) | Stop RTMP output for the voice room |

**Voice does not subscribe to any external events.** It is a purely reactive service -- callers invoke its API, and it publishes events about what happened.

---

## Client Events (Additions)

The existing client events (VoicePeerJoinedEvent, VoicePeerLeftEvent, VoicePeerUpdatedEvent, VoiceTierUpgradeEvent, VoiceRoomClosedEvent) remain unchanged. The redesign adds broadcast consent events.

### New Client Events for voice-client-events.yaml

```yaml
VoiceBroadcastConsentRequestEvent:
  allOf:
    - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
  type: object
  additionalProperties: false
  x-client-event: true
  description: >
    Sent to all room participants when someone requests broadcast consent.
    Each participant should respond via /voice/room/broadcast/consent.
  required:
    - eventName
    - eventId
    - timestamp
    - roomId
    - requestedBySessionId
  properties:
    eventName:
      type: string
      default: "voice.broadcast_consent_request"
    roomId:
      type: string
      format: uuid
    requestedBySessionId:
      type: string
      format: uuid
      description: Who initiated the broadcast request
    requestedByDisplayName:
      type: string
      nullable: true
      description: Display name of requester for UI

VoiceBroadcastConsentUpdateEvent:
  allOf:
    - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
  type: object
  additionalProperties: false
  x-client-event: true
  description: >
    Sent to all room participants when the broadcast consent state changes
    (someone consented, someone declined, broadcast started, broadcast stopped).
  required:
    - eventName
    - eventId
    - timestamp
    - roomId
    - state
  properties:
    eventName:
      type: string
      default: "voice.broadcast_consent_update"
    roomId:
      type: string
      format: uuid
    state:
      $ref: 'voice-api.yaml#/components/schemas/BroadcastConsentState'
    consentedCount:
      type: integer
      description: How many participants have consented
    totalCount:
      type: integer
      description: Total participants who need to consent
    declinedByDisplayName:
      type: string
      nullable: true
      description: If declined, who declined (for UI feedback)
```

---

## Configuration Changes

### voice-configuration.yaml Additions

```yaml
# New properties to add to x-service-configuration

# Participant TTL (background worker enforcement)
ParticipantHeartbeatTimeoutSeconds:
  type: integer
  description: Seconds of missed heartbeats before participant is evicted
  env: VOICE_PARTICIPANT_HEARTBEAT_TIMEOUT_SECONDS
  default: 60
  minimum: 10
  maximum: 600

ParticipantEvictionCheckIntervalSeconds:
  type: integer
  description: How often the background worker checks for stale participants
  env: VOICE_PARTICIPANT_EVICTION_CHECK_INTERVAL_SECONDS
  default: 15
  minimum: 5
  maximum: 120

# Broadcast Consent
BroadcastConsentTimeoutSeconds:
  type: integer
  description: Seconds to wait for all participants to respond to a consent request
  env: VOICE_BROADCAST_CONSENT_TIMEOUT_SECONDS
  default: 30
  minimum: 10
  maximum: 120
```

### Configuration Properties Removed

None. All existing voice configuration properties remain valid -- they control P2P/scaled tier behavior which is unchanged.

### Properties That Could Be Removed (Design Decision)

The VOICE-STREAMING.md v1 proposed adding streaming-specific configuration to lib-voice (`VOICE_STREAMING_ENABLED`, `VOICE_FFMPEG_PATH`, etc.). Since RTMP output now lives in lib-stream, **none of those properties are needed in lib-voice**. They have been absorbed into lib-stream's configuration in STREAMING-ARCHITECTURE.md.

---

## Internal Model Changes

### VoiceRoomData (VoiceRoomState.cs)

```csharp
// BEFORE
public class VoiceRoomData
{
    public Guid RoomId { get; set; }
    public Guid SessionId { get; set; }
    // ...
}

// AFTER: Add broadcast consent fields (SessionId stays as-is)
public class VoiceRoomData
{
    public Guid RoomId { get; set; }
    public Guid SessionId { get; set; }        // Connect/Auth session ID -- unchanged
    public BroadcastConsentState BroadcastState { get; set; } = BroadcastConsentState.Inactive;
    public Guid? BroadcastRequestedBy { get; set; }
    public HashSet<Guid> BroadcastConsentedSessions { get; set; } = new();
    // All other fields unchanged (Tier, Codec, MaxParticipants, CreatedAt, RtpServerUri)
}
```

### State Store Key Changes

| Before | After | Notes |
|--------|-------|-------|
| `voice:room:{roomId}` | `voice:room:{roomId}` | Unchanged |
| `voice:session-room:{sessionId}` | `voice:session-room:{sessionId}` | Unchanged |
| `voice:room:participants:{roomId}` | `voice:room:participants:{roomId}` | Unchanged |

No key changes needed -- `sessionId` is already an L1 concept.

---

## Implementation Changes

### VoiceService.cs Changes

#### Constructor: Remove Game Dependencies

The current constructor is clean -- it doesn't inject `IGameSessionClient` or any L2 service. The hierarchy violation exists because GameSession (L2) calls Voice (L4), not because Voice calls GameSession. So the constructor needs no changes beyond what the new generated interface requires.

#### CreateVoiceRoomAsync: Add Event Publishing

```csharp
// Key change: publish room created event (sessionId stays as-is)
public async Task<(StatusCodes, VoiceRoomResponse?)> CreateVoiceRoomAsync(
    CreateVoiceRoomRequest body, CancellationToken cancellationToken)
{
    // ...existing creation logic unchanged...

    var roomData = new VoiceRoomData
    {
        RoomId = roomId,
        SessionId = body.SessionId,  // Connect/Auth session ID -- unchanged
        Tier = body.PreferredTier,
        // ...rest unchanged...
    };

    // Save room data (unchanged)
    await roomStore.SaveAsync($"{ROOM_KEY_PREFIX}{roomId}", roomData, cancellationToken: cancellationToken);

    // Save session-room mapping (unchanged)
    await stringStore.SaveAsync(
        $"{SESSION_ROOM_KEY_PREFIX}{body.SessionId}",
        roomId.ToString(),
        cancellationToken: cancellationToken);

    // NEW: Publish room created event
    await _messageBus.PublishAsync("voice.room.created", new VoiceRoomCreatedEvent
    {
        EventId = Guid.NewGuid(),
        Timestamp = DateTimeOffset.UtcNow,
        RoomId = roomId,
        SessionId = body.SessionId,
        Tier = roomData.Tier,
        MaxParticipants = roomData.MaxParticipants
    }, cancellationToken);

    // ...return response...
}
```

#### JoinVoiceRoomAsync: Add Event Publishing

Add service event publishing at the end of the method (after existing client event publishing):

```csharp
// NEW: Publish participant joined event (service-to-service)
await _messageBus.PublishAsync("voice.participant.joined", new VoiceParticipantJoinedEvent
{
    EventId = Guid.NewGuid(),
    Timestamp = DateTimeOffset.UtcNow,
    RoomId = body.RoomId,
    ParticipantSessionId = body.SessionId,
    CurrentCount = newCount
}, cancellationToken);
```

#### LeaveVoiceRoomAsync: Add Event Publishing

```csharp
// NEW: Publish participant left event (service-to-service)
await _messageBus.PublishAsync("voice.participant.left", new VoiceParticipantLeftEvent
{
    EventId = Guid.NewGuid(),
    Timestamp = DateTimeOffset.UtcNow,
    RoomId = body.RoomId,
    ParticipantSessionId = body.SessionId,
    RemainingCount = remainingCount
}, cancellationToken);
```

#### DeleteVoiceRoomAsync: Add Events, Handle Broadcast Stop

```csharp
// Delete session-room mapping (unchanged key structure)
await stringStore.DeleteAsync($"{SESSION_ROOM_KEY_PREFIX}{roomData.SessionId}", cancellationToken);

// If broadcasting, stop it first
if (roomData.BroadcastState == BroadcastConsentState.Broadcasting ||
    roomData.BroadcastState == BroadcastConsentState.Approved)
{
    await _messageBus.PublishAsync("voice.room.broadcast.stopped", new VoiceRoomBroadcastStoppedEvent
    {
        EventId = Guid.NewGuid(),
        Timestamp = DateTimeOffset.UtcNow,
        RoomId = body.RoomId,
        Reason = VoiceRoomBroadcastStoppedReason.RoomClosed
    }, cancellationToken);
}

// NEW: Publish room deleted event
await _messageBus.PublishAsync("voice.room.deleted", new VoiceRoomDeletedEvent
{
    EventId = Guid.NewGuid(),
    Timestamp = DateTimeOffset.UtcNow,
    RoomId = body.RoomId,
    Reason = VoiceRoomDeletedReason.Manual  // or derive from body.Reason
}, cancellationToken);
```

#### TryUpgradeToScaledTierAsync: Add Event

```csharp
// After successful upgrade, publish tier-upgraded event
await _messageBus.PublishAsync("voice.room.tier-upgraded", new VoiceRoomTierUpgradedEvent
{
    EventId = Guid.NewGuid(),
    Timestamp = DateTimeOffset.UtcNow,
    RoomId = roomId,
    PreviousTier = VoiceTier.P2p,
    NewTier = VoiceTier.Scaled,
    RtpAudioEndpoint = rtpServerUri
}, cancellationToken);
```

#### New Methods: Broadcast Consent Flow

```csharp
// New: RequestBroadcastConsentAsync
public async Task<(StatusCodes, BroadcastConsentStatus?)> RequestBroadcastConsentAsync(
    BroadcastConsentRequest body, CancellationToken cancellationToken)
{
    // 1. Validate room exists
    // 2. Check broadcast state is inactive
    // 3. Get all current participants
    // 4. Set room broadcast state to Pending
    // 5. Set voice:consent_pending permission state for all participants
    // 6. Publish VoiceBroadcastConsentRequestEvent (client event) to all participants
    // 7. Return consent status with pending session IDs
}

// New: RespondBroadcastConsentAsync
public async Task<(StatusCodes, BroadcastConsentStatus?)> RespondBroadcastConsentAsync(
    BroadcastConsentResponse body, CancellationToken cancellationToken)
{
    // 1. Validate room exists and broadcast state is Pending
    // 2. Record consent/decline
    // 3. If declined:
    //    a. Set broadcast state to Inactive
    //    b. Publish voice.room.broadcast.declined (service event)
    //    c. Publish VoiceBroadcastConsentUpdateEvent (client event) with declined state
    // 4. If all consented:
    //    a. Set broadcast state to Approved
    //    b. Publish voice.room.broadcast.approved (service event) with RTP endpoint
    //    c. Publish VoiceBroadcastConsentUpdateEvent (client event) with approved state
    // 5. Else (still waiting):
    //    a. Publish VoiceBroadcastConsentUpdateEvent (client event) with progress
    // 6. Return updated consent status
}

// New: StopBroadcastAsync
public async Task<StatusCodes> StopBroadcastAsync(
    StopBroadcastConsentRequest body, CancellationToken cancellationToken)
{
    // 1. Validate room exists and broadcast state is Broadcasting or Approved
    // 2. Set broadcast state to Inactive
    // 3. Clear consent tracking
    // 4. Publish voice.room.broadcast.stopped (service event)
    // 5. Publish VoiceBroadcastConsentUpdateEvent (client event) with inactive state
}
```

### VoiceServicePlugin.cs: No Changes

All existing helper service registrations remain identical. No new DI registrations are needed for the L3 redesign -- the broadcast consent flow uses only existing dependencies (state store, message bus, client event publisher, permission client).

### VoiceServiceEvents.cs: New File

Currently does not exist. Create it with a partial class for event-driven broadcast state updates:

```csharp
// VoiceServiceEvents.cs
// Handles external events that affect broadcast state
// Currently: no external event subscriptions
// Future: could subscribe to stream.broadcast.started to update state to Broadcasting
```

For now, the transition from Approved → Broadcasting happens when lib-voice receives confirmation (either via a callback endpoint or by subscribing to `stream.broadcast.started`). This is a design decision documented in the open questions below.

---

## Background Worker: Participant TTL Enforcement

The current implementation has heartbeat tracking (`LastHeartbeat` in `ParticipantRegistration`) but **no enforcement**. Participants that disconnect without calling `/voice/room/leave` remain registered indefinitely.

### New: ParticipantEvictionWorker

```csharp
/// <summary>
/// Background service that periodically checks for stale participants
/// (missed heartbeats beyond the configured timeout) and evicts them.
/// </summary>
public class ParticipantEvictionWorker : BackgroundService
{
    // Every ParticipantEvictionCheckIntervalSeconds:
    // 1. Scan all rooms for participants with LastHeartbeat older than timeout
    // 2. For each stale participant:
    //    a. Unregister from SipEndpointRegistry
    //    b. Notify remaining peers via VoicePeerLeftEvent (client event)
    //    c. Publish voice.participant.left (service event)
    //    d. If room is now empty, delete it and publish voice.room.deleted
    //    e. If participant was in a broadcasting room and the eviction breaks consent,
    //       stop the broadcast (voice.room.broadcast.stopped with consent_revoked)
}
```

Register in `VoiceServicePlugin.ConfigureServices`:
```csharp
services.AddHostedService<ParticipantEvictionWorker>();
```

---

## What Carries Over Unchanged

These internal helpers are voice-level primitives with no layer dependencies:

### P2PCoordinator (Services/P2PCoordinator.cs)
- Mesh topology management
- Max participants logic (`GetP2PMaxParticipants()`, `CanAcceptNewParticipantAsync()`)
- Peer list generation (`GetMeshPeersForNewJoinAsync()`)
- Tier upgrade decision (`ShouldUpgradeToScaledAsync()`)
- **No changes needed**

### ScaledTierCoordinator (Services/ScaledTierCoordinator.cs)
- SFU allocation (`AllocateRtpServerAsync()`)
- RTP server release (`ReleaseRtpServerAsync()`)
- SIP credential generation (`GenerateSipCredentials()`)
- Max participants logic (`GetScaledMaxParticipants()`, `CanAcceptNewParticipantAsync()`)
- **No changes needed**

### SipEndpointRegistry (Services/SipEndpointRegistry.cs)
- Dual-layer state (ConcurrentDictionary + Redis)
- Participant registration/unregistration
- Room participant queries
- Heartbeat tracking
- **No changes needed**

### KamailioClient (Clients/KamailioClient.cs)
- JSONRPC 2.0 integration with Kamailio SIP proxy
- Snake_case serialization for Kamailio API
- **No changes needed**

### RtpEngineClient (Clients/RtpEngineClient.cs)
- Custom bencode codec for RTPEngine ng protocol
- UDP communication with cookie-based request tracking
- **No changes needed**

### VoiceRoomState (Services/VoiceRoomState.cs)
- Room data model and participant registration model
- `SessionId` stays as-is (it's a Connect/Auth concept, not game-specific)
- **Addition**: Broadcast consent tracking fields in VoiceRoomData

---

## What Moves to lib-stream

The original VOICE-STREAMING.md proposed adding an `IStreamingCoordinator` to lib-voice for FFmpeg/RTMP management. In the three-service architecture, ALL of this moves to lib-stream as `IBroadcastCoordinator`:

| VOICE-STREAMING.md Component | Now Lives In | New Name |
|------------------------------|-------------|----------|
| `IStreamingCoordinator` interface | lib-stream | `IBroadcastCoordinator` |
| `StreamingCoordinator` implementation | lib-stream | `BroadcastCoordinator` |
| FFmpeg process management | lib-stream | BroadcastCoordinator internals |
| RTMP URL validation (FFprobe) | lib-stream | BroadcastCoordinator internals |
| Fallback cascade logic | lib-stream | BroadcastCoordinator internals |
| Stream health monitoring | lib-stream | `BroadcastHealthMonitor` background worker |
| Streaming config (FFMPEG_PATH, etc.) | lib-stream | `stream-configuration.yaml` |
| Streaming client events | lib-stream | `stream-client-events.yaml` |
| Camera discovery event handling | lib-stream | `StreamServiceEvents.cs` |

See [STREAMING-ARCHITECTURE.md](STREAMING-ARCHITECTURE.md) lib-stream section for complete details.

---

## What Moves to lib-streaming (L4)

Game session lifecycle orchestration that currently lives in GameSession (L2) calling Voice (L4) moves to lib-streaming (L4):

| Current Pattern | New Pattern |
|----------------|-------------|
| GameSession creates voice room on session start | lib-streaming subscribes to `game-session.created`, calls lib-voice to create room |
| GameSession adds participants on join | lib-streaming subscribes to game-session participant events, calls lib-voice |
| GameSession deletes voice room on session end | lib-streaming subscribes to `game-session.ended`, calls lib-voice to delete room |

See [STREAMING-ARCHITECTURE.md](STREAMING-ARCHITECTURE.md) lib-streaming section for complete details.

---

## Permission State Changes

### Existing States (Unchanged)

| State | Trigger | Enables |
|-------|---------|---------|
| `voice:ringing` | Peer joins room (SDP offer delivered) | `/voice/peer/answer` endpoint |

### New States

| State | Trigger | Enables |
|-------|---------|---------|
| `voice:in_room` | Participant successfully joins a room | `/voice/room/broadcast/request`, `/voice/room/broadcast/stop`, `/voice/room/broadcast/status` |
| `voice:consent_pending` | Broadcast consent request initiated | `/voice/room/broadcast/consent` |

**Implementation note**: `voice:in_room` should be set when a participant joins and cleared when they leave. This is a new permission state that needs to be set/cleared in `JoinVoiceRoomAsync` and `LeaveVoiceRoomAsync` respectively.

---

## Open Design Questions

1. **Approved → Broadcasting transition**: When lib-stream starts RTMP output after receiving `voice.room.broadcast.approved`, how does lib-voice learn about it to update its state from Approved to Broadcasting? Options:
   - A) lib-voice subscribes to `stream.broadcast.started` (creates a soft dependency on lib-stream, slightly violates "voice has no external subscriptions")
   - B) lib-stream calls a voice API endpoint to confirm broadcast started (synchronous coupling)
   - C) Leave state as Approved and treat Approved/Broadcasting as equivalent from voice's perspective (simplest)
   - **Recommendation**: Option C. Voice doesn't need to distinguish Approved from Broadcasting. The consent flow is complete once all participants consent; what happens with the audio is lib-stream's domain.

2. **Consent timeout handling**: When a broadcast consent request times out (not all participants responded within `BroadcastConsentTimeoutSeconds`), should it:
   - A) Auto-decline (treat timeout as rejection)
   - B) Auto-approve if majority consented
   - **Recommendation**: Option A. Silence is not consent, especially for privacy.

3. **New participant joins during active broadcast**: When someone joins a room that's already broadcasting:
   - A) Notify them before joining (warning + option to join anyway)
   - B) Auto-pause broadcast, request new consent from all
   - C) Allow join but exclude their audio from broadcast until they explicitly consent
   - **Recommendation**: Option A via the `isBroadcasting` flag in join response. The client can show a warning. The broadcast continues with existing consented participants. The new participant's audio is included only after they consent (which may require a re-consent flow for existing participants too).

4. **VoiceRoomClosedEvent reason enum**: Current enum is `[session_ended, admin_action, error]`. With L3, `session_ended` is a game concept. Replace with:
   - `manual` (API-initiated deletion)
   - `empty` (all participants left, background worker cleanup)
   - `error` (infrastructure failure)
   - `admin_action` (admin-initiated)

5. **Empty room auto-deletion**: Should voice rooms be automatically deleted when the last participant leaves? Or should they persist until explicitly deleted? Currently they persist. With a background worker doing TTL enforcement, empty rooms could be cleaned up after a configurable grace period.

---

## Implementation Phases

### Phase 1: Schema Changes (~1 day)
- [ ] Update `voice-api.yaml`: `x-service-layer: AppFeatures`
- [ ] Update description text to remove game references
- [ ] Add broadcast consent endpoints to `voice-api.yaml`
- [ ] Add broadcast consent models to `voice-api.yaml`
- [ ] Update `voice-events.yaml`: add all published event schemas
- [ ] Update `voice-client-events.yaml`: add broadcast consent client events
- [ ] Add new configuration properties to `voice-configuration.yaml`
- [ ] Run `cd scripts && ./generate-service.sh voice`
- [ ] Verify `dotnet build` succeeds

### Phase 2: Service Implementation (~2 days)
- [ ] Update `VoiceRoomData`: add broadcast consent fields
- [ ] Update `VoiceService.CreateVoiceRoomAsync`: publish room created event
- [ ] Update `VoiceService.JoinVoiceRoomAsync`: publish participant event, set in_room state
- [ ] Update `VoiceService.LeaveVoiceRoomAsync`: publish participant event, clear in_room state
- [ ] Update `VoiceService.DeleteVoiceRoomAsync`: publish events, handle broadcast stop
- [ ] Update `VoiceService.TryUpgradeToScaledTierAsync`: publish tier-upgraded event
- [ ] Implement `RequestBroadcastConsentAsync`
- [ ] Implement `RespondBroadcastConsentAsync`
- [ ] Implement `StopBroadcastAsync`
- [ ] Implement `GetBroadcastStatusAsync`
- [ ] Verify `dotnet build` succeeds

### Phase 3: Background Worker (~0.5 day)
- [ ] Implement `ParticipantEvictionWorker`
- [ ] Register in `VoiceServicePlugin.ConfigureServices`
- [ ] Handle broadcast consent invalidation on eviction
- [ ] Verify `dotnet build` succeeds

### Phase 4: GameSession Integration Update (~1 day)
- [ ] Update `lib-game-session` to remove `IVoiceClient` dependency
- [ ] Create voice room orchestration in lib-streaming (L4) or as interim event handler
- [ ] Update game-session tests that reference voice
- [ ] Verify `dotnet build` succeeds

### Phase 5: Documentation & Tests (~1 day)
- [ ] Update `docs/plugins/VOICE.md` deep dive: L3, new events, no hierarchy violation
- [ ] Update `docs/reference/SERVICE-HIERARCHY.md`: move voice from L4 to L3
- [ ] Write unit tests for broadcast consent flow
- [ ] Write unit tests for participant eviction worker
- [ ] Write unit tests for event publishing
- [ ] Verify `make test` succeeds

**Total estimated effort**: ~5.5 days

---

## Migration Notes

### Breaking Changes

1. **Service layer**: L4 → L3 changes the `[BannouService]` attribute, affecting plugin load order. Voice will now load before L4 services.
2. **New endpoints**: Broadcast consent endpoints are additive (non-breaking), but consumers should be aware of the new permission states.
3. **New events**: Voice now publishes service events. Consumers should subscribe if interested.

### Who Must Update

| Consumer | Change Required |
|----------|----------------|
| lib-game-session | Remove `IVoiceClient` usage entirely. Publish events instead. |
| http-tester | Update voice test expectations for new layer; add broadcast consent tests |
| edge-tester | Update voice-related WebSocket test flows for new permission states |
| lib-streaming (new) | Subscribe to game-session events, call voice API to create/delete rooms |

### Rollback Plan

If the L3 redesign causes issues, the original L4 code can be restored from git. The schema changes are the only irreversible part -- but since voice rooms are ephemeral Redis state, there's no data migration concern.

---

## License Compliance (Tenet 18)

No changes. All existing dependencies (SIPSorcery BSD-3, Kamailio/RTPEngine as separate processes) remain compliant. FFmpeg is NOT a lib-voice dependency in the new architecture -- it belongs to lib-stream.

---

## References

- [STREAMING-ARCHITECTURE.md](STREAMING-ARCHITECTURE.md) - Parent document with three-service architecture
- [docs/plugins/VOICE.md](../plugins/VOICE.md) - Current voice plugin deep dive
- [docs/reference/SERVICE-HIERARCHY.md](../reference/SERVICE-HIERARCHY.md) - Service layer definitions
- [SIPSorcery v8.0.14](https://www.nuget.org/packages/SIPSorcery/8.0.14) - BSD-3-Clause (pinned in sdks/client-voice)
