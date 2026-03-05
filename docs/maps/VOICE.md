# Voice Implementation Map

> **Plugin**: lib-voice
> **Schema**: schemas/voice-api.yaml
> **Layer**: AppFeatures
> **Deep Dive**: [docs/plugins/VOICE.md](../plugins/VOICE.md)

---

## Summary

| Field | Value |
|-------|-------|
| Plugin | lib-voice |
| Layer | L3 AppFeatures |
| Endpoints | 11 |
| State Stores | voice-statestore (Redis), voice-lock (Redis) |
| Events Published | 8 (`voice.room.created`, `voice.room.deleted`, `voice.room.tier-upgraded`, `voice.peer.joined`, `voice.peer.left`, `voice.broadcast.approved`, `voice.broadcast.declined`, `voice.broadcast.stopped`) |
| Events Consumed | 2 (`session.disconnected`, `session.reconnected`) |
| Client Events | 8 |
| Background Services | 1 (ParticipantEvictionWorker) |

---

## State

**Store**: `voice-statestore` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `voice:room:{roomId}` | `VoiceRoomData` | Room configuration, tier, broadcast consent state, auto-cleanup flags |
| `voice:session-room:{sessionId}` | `string` | Session-to-room mapping (stores roomId as string) |
| `voice:room:participants:{roomId}` | `List<ParticipantRegistration>` | Participant list with endpoints and heartbeats (managed by SipEndpointRegistry) |

**Store**: `voice-lock` (Backend: Redis)

| Key Pattern | Purpose |
|-------------|---------|
| `session-room:{sessionId}` | Lock for room creation per session |
| `room-create:{roomId}` | Lock for ad-hoc room creation |
| `broadcast-consent:{roomId}` | Lock for broadcast consent state mutations |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | Room data, session mappings, participant lists |
| lib-state (`IDistributedLockProvider`) | L0 | Hard | Broadcast consent atomicity, room creation races |
| lib-messaging (`IMessageBus`) | L0 | Hard | Publishing 8 service event types |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Span instrumentation on async helpers |
| lib-permission (`IPermissionClient`) | L1 | Hard | Session voice states: `in_room`, `ringing`, `consent_pending` |
| lib-connect (`IClientEventPublisher`) | L1 | Hard | WebSocket push of 8 client event types |

**Notes:**
- Voice is a **leaf node** — no other plugin calls `IVoiceClient`. Future consumers (lib-broadcast L3, lib-showtime L4) will subscribe to events.
- No lib-resource integration — voice rooms are ephemeral session-scoped Redis state with TTL/eviction lifecycle.
- External infrastructure (Kamailio, RTPEngine) accessed directly via `RtpEngineClient` (permitted per T4).

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `voice.room.created` | `VoiceRoomCreatedEvent` | CreateVoiceRoom, JoinVoiceRoom (ad-hoc) |
| `voice.room.deleted` | `VoiceRoomDeletedEvent` | DeleteVoiceRoom, ParticipantEvictionWorker (empty room) |
| `voice.room.tier-upgraded` | `VoiceRoomTierUpgradedEvent` | TryUpgradeToScaledTierAsync (background) |
| `voice.peer.joined` | `VoicePeerJoinedEvent` | JoinVoiceRoom |
| `voice.peer.left` | `VoicePeerLeftEvent` | LeaveVoiceRoom, HandleSessionDisconnectedAsync, ParticipantEvictionWorker |
| `voice.broadcast.approved` | `VoiceBroadcastApprovedEvent` | RespondBroadcastConsent (all consented) |
| `voice.broadcast.declined` | `VoiceBroadcastDeclinedEvent` | RespondBroadcastConsent (declined), ParticipantEvictionWorker (timeout) |
| `voice.broadcast.stopped` | `VoiceBroadcastStoppedEvent` | StopBroadcast, LeaveVoiceRoom, DeleteVoiceRoom, HandleSessionDisconnectedAsync, ParticipantEvictionWorker |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `session.disconnected` | `HandleSessionDisconnectedAsync` | Unregister participant, clear permission, notify peers, stop broadcast if active, set grace period if empty |
| `session.reconnected` | `HandleSessionReconnectedAsync` | Verify participant still registered, re-set `in_room` permission, push VoiceRoomStateClientEvent with current state |

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<VoiceService>` | Structured logging |
| `VoiceServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store resolution (constructor only, not stored) |
| `IMessageBus` | Service event publishing |
| `IDistributedLockProvider` | Distributed lock acquisition |
| `IPermissionClient` | Session permission state management |
| `IClientEventPublisher` | WebSocket client event push |
| `ITelemetryProvider` | Span instrumentation |
| `IEventConsumer` | Event handler registration (constructor only) |
| `ISipEndpointRegistry` | Participant tracking (local cache + Redis) |
| `IP2PCoordinator` | P2P mesh topology, capacity, upgrade threshold |
| `IScaledTierCoordinator` | SFU management, SIP credentials, RTP allocation |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| CreateVoiceRoom | POST /voice/room/create | [] | room, session-room | voice.room.created |
| GetVoiceRoom | POST /voice/room/get | [admin] | - | - |
| JoinVoiceRoom | POST /voice/room/join | [] | room (ad-hoc), session-room (ad-hoc), participants | voice.room.created (ad-hoc), voice.peer.joined |
| LeaveVoiceRoom | POST /voice/room/leave | [] | room (grace period), participants | voice.peer.left, voice.broadcast.stopped (if active) |
| DeleteVoiceRoom | POST /voice/room/delete | [] | room, session-room, participants | voice.room.deleted, voice.broadcast.stopped (if active) |
| PeerHeartbeat | POST /voice/peer/heartbeat | [admin] | participants (heartbeat) | - |
| AnswerPeer | POST /voice/peer/answer | [user; state: voice=ringing] | - | - |
| RequestBroadcastConsent | POST /voice/room/broadcast/request | [user; state: voice=in_room] | room | - |
| RespondBroadcastConsent | POST /voice/room/broadcast/consent | [user; state: voice=consent_pending] | room | voice.broadcast.approved or voice.broadcast.declined |
| StopBroadcast | POST /voice/room/broadcast/stop | [user; state: voice=in_room] | room | voice.broadcast.stopped |
| GetBroadcastStatus | POST /voice/room/broadcast/status | [user; state: voice=in_room] | - | - |

---

## Methods

### CreateVoiceRoom
POST /voice/room/create | Roles: []

```
LOCK voice-lock:"session-room:{sessionId}"                   -> 409 if fails
  READ _stringStore:"voice:session-room:{sessionId}"          -> 409 if already exists
  // Determine maxParticipants: use config P2PMaxParticipants if request value is 0
  WRITE _roomStore:"voice:room:{roomId}" <- new VoiceRoomData from request
  WRITE _stringStore:"voice:session-room:{sessionId}" <- roomId
  // Register creator as first participant
  CALL _endpointRegistry.RegisterAsync(roomId, sessionId, sipEndpoint, displayName)
  CALL _permissionClient.UpdateSessionStateAsync({ state: "in_room" })
  PUBLISH voice.room.created { roomId, sessionId, tier, maxParticipants }
  RETURN (200, VoiceRoomResponse)
```

---

### GetVoiceRoom
POST /voice/room/get | Roles: [admin]

```
READ _roomStore:"voice:room:{roomId}"                        -> 404 if null
CALL _endpointRegistry.GetRoomParticipantsAsync(roomId)
RETURN (200, VoiceRoomResponse)
```

---

### JoinVoiceRoom
POST /voice/room/join | Roles: []

```
READ _roomStore:"voice:room:{roomId}"
IF room == null AND AdHocRoomsEnabled
  LOCK voice-lock:"room-create:{roomId}"                     -> 409 if fails
    READ _roomStore:"voice:room:{roomId}"                    // double-check after lock
    IF still null
      WRITE _roomStore:"voice:room:{roomId}" <- new VoiceRoomData (autoCleanup=true)
      WRITE _stringStore:"voice:session-room:{sessionId}" <- roomId
      PUBLISH voice.room.created { roomId, sessionId, tier: P2P, maxParticipants }
ELSE IF room == null
  RETURN (404, null)

IF room.Password != null AND request.Password != room.Password
  RETURN (403, null)

CALL _endpointRegistry.GetParticipantCountAsync(roomId)
IF room.Tier == Scaled
  CALL _scaledTierCoordinator.CanAcceptNewParticipantAsync(roomId, count)  -> 409 if full
ELSE // P2P
  CALL _p2pCoordinator.CanAcceptNewParticipantAsync(roomId, count)
  IF !canAccept AND ScaledTierEnabled AND TierUpgradeEnabled
    // Synchronous upgrade attempt
    CALL TryUpgradeToScaledTierAsync(roomId, roomData)
    READ _roomStore:"voice:room:{roomId}"                    -> 500 if null after upgrade
  ELSE IF !canAccept
    RETURN (409, null)

CALL _endpointRegistry.RegisterAsync(roomId, sessionId, sipEndpoint, displayName)  -> 409 if already registered
CALL _permissionClient.UpdateSessionStateAsync({ state: "in_room" })

IF room.Tier == Scaled
  PUBLISH voice.peer.joined { roomId, peerSessionId, currentCount }
  RETURN (200, JoinVoiceRoomResponse { tier: Scaled, peers: [], rtpServerUri })
ELSE // P2P
  CALL _p2pCoordinator.GetMeshPeersForNewJoinAsync(roomId, sessionId)
  IF peers.Count > 0
    CALL _permissionClient.UpdateSessionStateAsync({ state: "ringing" })  // for joiner
  CALL _p2pCoordinator.ShouldUpgradeToScaledAsync(roomId, newCount)
  PUBLISH voice.peer.joined { roomId, peerSessionId, currentCount }
  // see helper: NotifyPeerJoinedAsync
  CALL NotifyPeerJoinedAsync(roomId, sessionId, sipEndpoint, displayName)
  IF shouldUpgrade
    // Fire-and-forget background tier upgrade (CancellationToken.None)
    // TryUpgradeToScaledTierAsync runs asynchronously
  RETURN (200, JoinVoiceRoomResponse { tier: P2P, peers, stunServers, tierUpgradePending })
```

---

### LeaveVoiceRoom
POST /voice/room/leave | Roles: []

```
CALL _endpointRegistry.UnregisterAsync(roomId, sessionId)    -> 404 if null
CALL _permissionClient.ClearSessionStateAsync({ sessionId, service: "voice" })
CALL _endpointRegistry.GetParticipantCountAsync(roomId)
PUBLISH voice.peer.left { roomId, peerSessionId, remainingCount }
// see helper: NotifyPeerLeftAsync
CALL NotifyPeerLeftAsync(roomId, sessionId, displayName, remainingCount)

LOCK voice-lock:"broadcast-consent:{roomId}"                 // non-fatal if fails (logs warning)
  READ _roomStore:"voice:room:{roomId}"
  IF broadcastState != Inactive
    // see helper: StopBroadcastInternalAsync
    CALL StopBroadcastInternalAsync(roomId, roomData, reason: ConsentRevoked)
  IF remainingCount == 0 AND roomData.AutoCleanup
    WRITE _roomStore:"voice:room:{roomId}" <- set LastParticipantLeftAt
RETURN (200)
```

---

### DeleteVoiceRoom
POST /voice/room/delete | Roles: []

```
READ _roomStore:"voice:room:{roomId}"                        -> 404 if null
// deleteReason defaults to Manual if request.Reason is null

IF broadcastState != Inactive
  LOCK voice-lock:"broadcast-consent:{roomId}"               // non-fatal if fails
    READ _roomStore:"voice:room:{roomId}"                    // fresh read inside lock
    IF broadcastState != Inactive
      // see helper: StopBroadcastInternalAsync
      CALL StopBroadcastInternalAsync(roomId, roomData, reason: RoomClosed)

CALL _endpointRegistry.GetRoomParticipantsAsync(roomId)
CALL _endpointRegistry.ClearRoomAsync(roomId)
IF room.Tier == Scaled AND rtpServerUri != null
  CALL _scaledTierCoordinator.ReleaseRtpServerAsync(roomId)

DELETE _roomStore:"voice:room:{roomId}"
DELETE _stringStore:"voice:session-room:{roomData.SessionId}"

// see helper: NotifyRoomClosedAsync
CALL NotifyRoomClosedAsync(roomId, participants, deleteReason)
FOREACH participant in participants
  CALL _permissionClient.ClearSessionStateAsync({ sessionId, service: "voice" })

PUBLISH voice.room.deleted { roomId, reason: deleteReason }
RETURN (200)
```

---

### PeerHeartbeat
POST /voice/peer/heartbeat | Roles: [admin]

```
CALL _endpointRegistry.UpdateHeartbeatAsync(roomId, sessionId)  -> 404 if false
RETURN (200)
```

---

### AnswerPeer
POST /voice/peer/answer | Roles: [user; state: voice=ringing]

```
CALL _endpointRegistry.GetParticipantAsync(roomId, targetSessionId)  -> 404 if null
CALL _endpointRegistry.GetParticipantAsync(roomId, senderSessionId)  // for display name
PUSH VoicePeerUpdatedClientEvent to [targetSessionId] {
  roomId, peer: { peerSessionId: senderSessionId, sdpOffer: sdpAnswer, displayName, iceCandidates }
}
// Note: SdpOffer field intentionally carries the SDP answer
RETURN (200)
```

---

### RequestBroadcastConsent
POST /voice/room/broadcast/request | Roles: [user; state: voice=in_room]

```
LOCK voice-lock:"broadcast-consent:{roomId}"                 -> 409 if fails
  READ _roomStore:"voice:room:{roomId}"                      -> 404 if null
  IF broadcastState != Inactive
    RETURN (409, null)
  CALL _endpointRegistry.GetRoomParticipantsAsync(roomId)
  IF participants.Count == 0
    RETURN (409, null)
  WRITE _roomStore:"voice:room:{roomId}" <- BroadcastState=Pending, RequestedBy, ConsentedSessions=empty, RequestedAt
  FOREACH participant in participants
    CALL _permissionClient.UpdateSessionStateAsync({ state: "consent_pending" })
  PUSH VoiceBroadcastConsentRequestClientEvent to [all participants] {
    roomId, requestedBySessionId, requestedByDisplayName
  }
  RETURN (200, BroadcastConsentStatus { state: Pending })
```

---

### RespondBroadcastConsent
POST /voice/room/broadcast/consent | Roles: [user; state: voice=consent_pending]

```
LOCK voice-lock:"broadcast-consent:{roomId}"                 -> 409 if fails
  READ _roomStore:"voice:room:{roomId}"                      -> 404 if null
  IF broadcastState != Pending
    RETURN (409, null)
  CALL _endpointRegistry.GetRoomParticipantsAsync(roomId)

  IF !consented  // participant declined
    WRITE _roomStore:"voice:room:{roomId}" <- BroadcastState=Inactive, clear consent data
    // see helper: ClearConsentPendingStatesAsync
    CALL ClearConsentPendingStatesAsync(participantSessionIds)
    PUBLISH voice.broadcast.declined { roomId, declinedBySessionId }
    // see helper: PublishBroadcastConsentUpdateAsync
    CALL PublishBroadcastConsentUpdateAsync(state: Inactive, declined display name)
    RETURN (200, BroadcastConsentStatus { state: Inactive })

  // Add session to consented set
  IF consentedSessions.IsSupersetOf(participantSessionIds)  // all consented
    WRITE _roomStore:"voice:room:{roomId}" <- BroadcastState=Approved
    CALL ClearConsentPendingStatesAsync(participantSessionIds)
    PUBLISH voice.broadcast.approved { roomId, requestedBySessionId, rtpAudioEndpoint }
    CALL PublishBroadcastConsentUpdateAsync(state: Approved)
    RETURN (200, BroadcastConsentStatus { state: Approved })

  // Partial consent — still waiting
  WRITE _roomStore:"voice:room:{roomId}" <- add sessionId to ConsentedSessions
  CALL PublishBroadcastConsentUpdateAsync(state: Pending, progress)
  RETURN (200, BroadcastConsentStatus { state: Pending })
```

---

### StopBroadcast
POST /voice/room/broadcast/stop | Roles: [user; state: voice=in_room]

```
LOCK voice-lock:"broadcast-consent:{roomId}"                 -> 409 if fails
  READ _roomStore:"voice:room:{roomId}"                      -> 404 if null
  IF broadcastState == Inactive
    RETURN (404)
  // see helper: StopBroadcastInternalAsync
  CALL StopBroadcastInternalAsync(roomId, roomData, reason: Manual)
  RETURN (200)
```

---

### GetBroadcastStatus
POST /voice/room/broadcast/status | Roles: [user; state: voice=in_room]

```
READ _roomStore:"voice:room:{roomId}"                        -> 404 if null
CALL _endpointRegistry.GetRoomParticipantsAsync(roomId)
// pendingIds = all participant session IDs minus consented session IDs
RETURN (200, BroadcastConsentStatus { state, consentedSessionIds, pendingSessionIds, requestedBySessionId, rtpAudioEndpoint })
```

---

## Background Services

### ParticipantEvictionWorker
**Initial Delay**: config.EvictionWorkerInitialDelaySeconds (default: 10s)
**Interval**: config.ParticipantEvictionCheckIntervalSeconds (default: 15s)
**Purpose**: Heartbeat TTL enforcement, empty room auto-deletion, broadcast consent timeout

```
FOREACH roomId in _endpointRegistry.GetAllTrackedRoomIds()
  READ _roomStore:"voice:room:{roomId}"                      // skip if null

  // 1. Stale participant eviction
  FOREACH participant WHERE LastHeartbeat > heartbeatTimeout
    CALL _endpointRegistry.UnregisterAsync(roomId, sessionId)
    PUBLISH voice.peer.left { roomId, peerSessionId, remainingCount }
    PUSH VoicePeerLeftClientEvent to remaining peers
    CALL _permissionClient.ClearSessionStateAsync(sessionId)
    IF room now empty AND AutoCleanup
      WRITE _roomStore:"voice:room:{roomId}" <- set LastParticipantLeftAt
  IF any eviction broke broadcast consent
    WRITE _roomStore:"voice:room:{roomId}" <- BroadcastState=Inactive
    PUBLISH voice.broadcast.stopped { reason: ConsentRevoked }
    IF was Pending: restore in_room permission for remaining
    PUSH VoiceBroadcastConsentUpdateClientEvent (state: Inactive)

  // 2. Empty room auto-delete
  IF AutoCleanup AND LastParticipantLeftAt > gracePeriod AND count == 0
    CALL _endpointRegistry.ClearRoomAsync(roomId)
    DELETE _roomStore:"voice:room:{roomId}"
    DELETE _stringStore:"voice:session-room:{sessionId}"
    PUBLISH voice.room.deleted { reason: Empty }

  // 3. Broadcast consent timeout
  IF BroadcastState == Pending AND BroadcastRequestedAt > consentTimeout
    WRITE _roomStore:"voice:room:{roomId}" <- BroadcastState=Inactive
    FOREACH participant: restore in_room permission
    PUBLISH voice.broadcast.declined { declinedBySessionId: null }
    PUSH VoiceBroadcastConsentUpdateClientEvent (state: Inactive)
```
