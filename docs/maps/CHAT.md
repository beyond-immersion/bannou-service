# Chat Implementation Map

> **Plugin**: lib-chat
> **Schema**: schemas/chat-api.yaml
> **Layer**: AppFoundation
> **Deep Dive**: [docs/plugins/CHAT.md](../plugins/CHAT.md)

---

| Field | Value |
|-------|-------|
| Plugin | lib-chat |
| Layer | L1 AppFoundation |
| Endpoints | 32 |
| State Stores | chat-room-types (MySQL), chat-rooms (MySQL), chat-rooms-cache (Redis), chat-messages (MySQL), chat-messages-ephemeral (Redis), chat-participants (Redis), chat-bans (MySQL), chat-lock (Redis) |
| Events Published | 17 (room-type.created, room-type.updated, room.created, room.updated, room.deleted, room.locked, room.archived, participant.joined, participant.left, participant.kicked, participant.banned, participant.unbanned, participant.muted, participant.unmuted, participant.role-changed, message.sent, message.deleted) |
| Events Consumed | 4 (contract.fulfilled, contract.breach.detected, contract.terminated, contract.expired) |
| Client Events | 16 |
| Background Services | 4 |

---

## State

**Store**: `chat-room-types` (Backend: MySQL, `IJsonQueryableStateStore`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `type:{scope}:{code}` | `ChatRoomTypeModel` | Room type definitions. Scope is gameServiceId GUID or `"global"` for built-in types |

**Store**: `chat-rooms` (Backend: MySQL, `IJsonQueryableStateStore`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `room:{roomId}` | `ChatRoomModel` | Primary room records with lifecycle status, contract bindings, activity timestamps |

**Store**: `chat-rooms-cache` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `room:{roomId}` | `ChatRoomModel` | Write-through cache for active rooms. Cache-aside read: Redis first, MySQL fallback, populate on miss |

**Store**: `chat-messages` (Backend: MySQL, `IJsonQueryableStateStore`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{roomId}:{messageId}` | `ChatMessageModel` | Persistent message history for durable room types |

**Store**: `chat-messages-ephemeral` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{roomId}:{messageId}` | `ChatMessageModel` | Ephemeral messages with TTL from `EphemeralMessageTtlMinutes` config |

**Store**: `chat-participants` (Backend: Redis, `ICacheableStateStore`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{roomId}` (Redis hash) | Hash of `sessionId` → `ChatParticipantModel` | All participants for a room. Uses HashGetAll, HashSet, HashDelete, HashCount |
| `rate:{roomId}:{sessionId}` | Atomic counter | Rate limiting via Redis INCR with 60s TTL |
| `typing:active` (Sorted set) | Members: `{roomId:N}:{sessionId:N}`, Scores: Unix ms | Global sorted set tracking active typing state |

**Store**: `chat-bans` (Backend: MySQL, `IJsonQueryableStateStore`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `ban:{roomId}:{sessionId}` | `ChatBanModel` | Ban records with optional expiry timestamp |

**Store**: `chat-lock` (Backend: Redis) — used by `IDistributedLockProvider` only

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | 7 data stores + lock store |
| lib-state (`IDistributedLockProvider`) | L0 | Hard | Locks for room type, room, and participant mutations |
| lib-messaging (`IMessageBus`) | L0 | Hard | Publishing 17 service event topics |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Span instrumentation on async helpers |
| lib-connect (`IClientEventPublisher`) | L1 | Hard | 16 client event types + typing shortcut publish/revoke |
| lib-connect (`IEntitySessionRegistry`) | L1 | Hard | Room-level entity session registration and typing event fan-out |
| lib-contract (`IContractClient`) | L1 | Hard | Validating governing contract existence on room creation |
| lib-resource (`IResourceClient`) | L1 | Hard | Room archival via `ExecuteCompressAsync` |
| lib-permission (`IPermissionClient`) | L1 | Hard | Setting/clearing `in_room` permission state on join/leave/kick/ban |

**Notes**:
- Leaf node: No other plugin currently references `IChatClient` or subscribes to chat events.
- No DI provider/listener interfaces implemented.
- Uses `IResourceClient.ExecuteCompressAsync` for archival only — no `x-references` cleanup integration. Chat rooms are transient containers, not persistent foundational resources.
- Permission state errors are swallowed (logged, not propagated). Permission failures do not block join/leave/kick/ban.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `chat.room-type.created` | `ChatRoomTypeCreatedEvent` | RegisterRoomType |
| `chat.room-type.updated` | `ChatRoomTypeUpdatedEvent` | UpdateRoomType, DeprecateRoomType |
| `chat.room.created` | `ChatRoomCreatedEvent` | CreateRoom |
| `chat.room.updated` | `ChatRoomUpdatedEvent` | UpdateRoom |
| `chat.room.deleted` | `ChatRoomDeletedEvent` | DeleteRoom, CleanupIdleRooms (worker), contract action (Delete) |
| `chat.room.locked` | `ChatRoomLockedEvent` | Contract action (Lock) |
| `chat.room.archived` | `ChatRoomArchivedEvent` | ArchiveRoom, contract action (Archive) |
| `chat.participant.joined` | `ChatParticipantJoinedEvent` | JoinRoom |
| `chat.participant.left` | `ChatParticipantLeftEvent` | LeaveRoom |
| `chat.participant.kicked` | `ChatParticipantKickedEvent` | KickParticipant |
| `chat.participant.banned` | `ChatParticipantBannedEvent` | BanParticipant |
| `chat.participant.unbanned` | `ChatParticipantUnbannedEvent` | UnbanParticipant |
| `chat.participant.muted` | `ChatParticipantMutedEvent` | MuteParticipant |
| `chat.participant.unmuted` | `ChatParticipantUnmutedEvent` | UnmuteParticipant, SendMessage (lazy auto-unmute) |
| `chat.participant.role-changed` | `ChatParticipantRoleChangedEvent` | ChangeParticipantRole, LeaveRoom (owner auto-promotion) |
| `chat.message.sent` | `ChatMessageSentEvent` | SendMessage, SendMessageBatch (metadata only — no text content for privacy) |
| `chat.message.deleted` | `ChatMessageDeletedEvent` | DeleteMessage |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `contract.fulfilled` | `HandleContractFulfilledAsync` | Query rooms by ContractId, apply per-room fulfilled action (default: Archive) |
| `contract.breach.detected` | `HandleContractBreachDetectedAsync` | Query rooms by ContractId, apply per-room breach action (default: Lock) |
| `contract.terminated` | `HandleContractTerminatedAsync` | Query rooms by ContractId, apply per-room terminated action (default: Delete) |
| `contract.expired` | `HandleContractExpiredAsync` | Query rooms by ContractId, apply per-room expired action (default: Archive) |

All handlers paginate with `ContractRoomQueryBatchSize` and cap at `MaxContractRoomQueryResults`.

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<ChatService>` | Structured logging |
| `ChatServiceConfiguration` | Typed configuration access (31 properties) |
| `IStateStoreFactory` | State store access (7 stores) |
| `IDistributedLockProvider` | Distributed locks for mutations |
| `IMessageBus` | Service event publishing |
| `IEventConsumer` | Contract lifecycle event handler registration |
| `IClientEventPublisher` | WebSocket client event publishing + shortcut management |
| `IEntitySessionRegistry` | Room-level session registration and typing fan-out |
| `IContractClient` | Contract validation on room creation |
| `IResourceClient` | Room archival (compression) |
| `IPermissionClient` | Permission state management |
| `ITelemetryProvider` | Telemetry span instrumentation |
| `IdleRoomCleanupWorker` | Background: periodic idle room cleanup |
| `TypingExpiryWorker` | Background: typing indicator timeout |
| `BanExpiryWorker` | Background: expired ban cleanup |
| `MessageRetentionWorker` | Background: expired persistent message cleanup |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| RegisterRoomType | POST /chat/type/register | developer | room-type | chat.room-type.created |
| GetRoomType | POST /chat/type/get | developer | - | - |
| ListRoomTypes | POST /chat/type/list | developer | - | - |
| UpdateRoomType | POST /chat/type/update | developer | room-type | chat.room-type.updated |
| DeprecateRoomType | POST /chat/type/deprecate | developer | room-type | chat.room-type.updated |
| CreateRoom | POST /chat/room/create | user | room, cache | chat.room.created |
| GetRoom | POST /chat/room/get | user | - | - |
| ListRooms | POST /chat/room/list | user | - | - |
| UpdateRoom | POST /chat/room/update | user, state:in_room | room, cache | chat.room.updated |
| DeleteRoom | POST /chat/room/delete | user, state:in_room | room, cache, participants | chat.room.deleted |
| ArchiveRoom | POST /chat/room/archive | user, state:in_room | room, cache | chat.room.archived |
| JoinRoom | POST /chat/room/join | user | participants, room, cache | chat.participant.joined |
| LeaveRoom | POST /chat/room/leave | user, state:in_room | participants, room, cache, typing | chat.participant.left (+role-changed) |
| ListParticipants | POST /chat/room/participants | user, state:in_room | - | - |
| KickParticipant | POST /chat/room/participant/kick | user, state:in_room | participants, room, cache, typing | chat.participant.kicked |
| BanParticipant | POST /chat/room/participant/ban | user, state:in_room | bans, participants, room, cache, typing | chat.participant.banned |
| UnbanParticipant | POST /chat/room/participant/unban | user, state:in_room | bans | chat.participant.unbanned |
| MuteParticipant | POST /chat/room/participant/mute | user, state:in_room | participants | chat.participant.muted |
| UnmuteParticipant | POST /chat/room/participant/unmute | user, state:in_room | participants | chat.participant.unmuted |
| ChangeParticipantRole | POST /chat/room/participant/change-role | user, state:in_room | participants | chat.participant.role-changed |
| SendMessage | POST /chat/message/send | user, state:in_room | messages/buffer, participants, room, cache, typing | chat.message.sent (+unmuted) |
| SendMessageBatch | POST /chat/message/send-batch | [] | messages/buffer, room, cache | chat.message.sent (per message) |
| GetMessageHistory | POST /chat/message/history | user, state:in_room | - | - |
| DeleteMessage | POST /chat/message/delete | user, state:in_room | messages | chat.message.deleted |
| PinMessage | POST /chat/message/pin | user, state:in_room | messages | - |
| UnpinMessage | POST /chat/message/unpin | user, state:in_room | messages | - |
| SearchMessages | POST /chat/message/search | user, state:in_room | - | - |
| AdminListRooms | POST /chat/admin/rooms | admin | - | - |
| AdminGetStats | POST /chat/admin/stats | admin | - | - |
| AdminForceCleanup | POST /chat/admin/cleanup | admin | room, cache, participants | chat.room.deleted/archived |
| Typing | POST /chat/typing | [] | typing | - |
| EndTyping | POST /chat/end-typing | [] | typing | - |

---

## Methods

### RegisterRoomType
POST /chat/type/register | Roles: [developer]

```
LOCK chat-lock:"register-room-type" (key=type:{scope}:{code})  -> 409 if fails
  READ room-type:type:{scope}:{code}                            -> 409 if exists
  IF gameServiceId set
    COUNT room-types WHERE $.GameServiceId = gameServiceId       -> 409 if >= MaxRoomTypesPerGameService
  WRITE room-type:type:{scope}:{code} <- ChatRoomTypeModel from request
  PUBLISH chat.room-type.created { code, gameServiceId, displayName, messageFormat, persistenceMode, status }
RETURN (200, RoomTypeResponse)
```

### GetRoomType
POST /chat/type/get | Roles: [developer]

```
READ room-type:type:{scope}:{code}                              -> 404 if null
RETURN (200, RoomTypeResponse)
```

### ListRoomTypes
POST /chat/type/list | Roles: [developer]

```
QUERY room-types WHERE $.Code EXISTS
  [+ $.GameServiceId = X] [+ $.MessageFormat = X] [+ $.Status = X]
  ORDER BY $.CreatedAt DESC PAGED(page, pageSize)
RETURN (200, ListRoomTypesResponse { items, totalCount, page, pageSize })
```

### UpdateRoomType
POST /chat/type/update | Roles: [developer]

```
LOCK chat-lock:type:{code}                                      -> 409 if fails
  READ room-type:type:{scope}:{code}                            -> 404 if null
  // Apply non-null request fields, track changedFields
  WRITE room-type:type:{scope}:{code} <- updated model
  PUBLISH chat.room-type.updated { code, ..., changedFields }
RETURN (200, RoomTypeResponse)
```

### DeprecateRoomType
POST /chat/type/deprecate | Roles: [developer]

```
LOCK chat-lock:type:{code}                                      -> 409 if fails
  READ room-type:type:{scope}:{code}                            -> 404 if null
  IF already deprecated
    RETURN (200, RoomTypeResponse)                               // idempotent
  WRITE room-type:type:{scope}:{code} <- Status=Deprecated, UpdatedAt=now
  PUBLISH chat.room-type.updated { ..., changedFields: ["status"] }
RETURN (200, RoomTypeResponse)
```

### CreateRoom
POST /chat/room/create | Roles: [user]

```
READ room-type by code                                          -> 404 if null, 400 if deprecated
IF contractId set
  CALL _contractClient.GetContractInstanceAsync({ contractId })  -> 404 if not found
WRITE room:room:{roomId} <- ChatRoomModel { newGuid, roomTypeCode, sessionId, contractId, ... }
WRITE cache:room:{roomId} <- same model
PUBLISH chat.room.created { roomId, roomTypeCode, sessionId, contractId, status, participantCount=0 }
RETURN (200, ChatRoomResponse)
```

### GetRoom
POST /chat/room/get | Roles: [user]

```
READ room:room:{roomId}                                         -> 404 if null
  // cache-aside: Redis first, MySQL fallback, populate cache on miss
READ participants:{roomId} HashCount                             // live participant count
RETURN (200, ChatRoomResponse)
```

### ListRooms
POST /chat/room/list | Roles: [user]

```
QUERY rooms WHERE $.RoomId EXISTS
  [+ $.RoomTypeCode = X] [+ $.SessionId = X] [+ $.Status = X]
  ORDER BY $.CreatedAt DESC PAGED(page, pageSize)
FOREACH room in results
  READ participants:{roomId} HashCount                           // sequential participant count
RETURN (200, ListRoomsResponse { items, totalCount, page, pageSize })
```

### UpdateRoom
POST /chat/room/update | Roles: [user, state: chat=in_room]

```
LOCK chat-lock:{roomId}                                         -> 409 if fails
  READ room:room:{roomId}                                       -> 404 if null
  // Apply non-null fields: displayName, maxParticipants, metadata
  WRITE room:room:{roomId} <- updated model
  WRITE cache:room:{roomId} <- updated model
  READ participants:{roomId} HashGetAll                          // for broadcast + count
  PUBLISH chat.room.updated { roomId, ..., changedFields }
  IF participants present
    PUSH ChatRoomUpdatedClientEvent to all participant sessions
RETURN (200, ChatRoomResponse)
```

### DeleteRoom
POST /chat/room/delete | Roles: [user, state: chat=in_room]

```
LOCK chat-lock:{roomId}                                         -> 409 if fails
  READ room:room:{roomId}                                       -> 404 if null
  READ participants:{roomId} HashGetAll
  IF participants present
    PUSH ChatRoomDeletedClientEvent { reason="Room deleted" } to all participant sessions
  FOREACH participant
    CALL _permissionClient.ClearSessionStateAsync(sessionId)     // swallows errors
  DELETE participants:{roomId}                                   // HashDelete entire key
  DELETE room:room:{roomId}
  DELETE cache:room:{roomId}
  PUBLISH chat.room.deleted { roomId, ..., deletedReason="Explicitly deleted" }
RETURN (200, ChatRoomResponse)                                   // snapshot before deletion
```

### ArchiveRoom
POST /chat/room/archive | Roles: [user, state: chat=in_room]

```
LOCK chat-lock:{roomId}                                         -> 409 if fails
  READ room:room:{roomId}                                       -> 404 if null
  IF already archived
    RETURN (200, ChatRoomResponse)                               // idempotent
  CALL _resourceClient.ExecuteCompressAsync({ resourceType="chat-room", resourceId=roomId })
  WRITE room:room:{roomId} <- IsArchived=true, Status=Archived
  WRITE cache:room:{roomId} <- same
  PUBLISH chat.room.archived { roomId, archiveId }
RETURN (200, ChatRoomResponse)
```

### JoinRoom
POST /chat/room/join | Roles: [user]

```
// callerSessionId from ServiceRequestContext                    -> 401 if null
LOCK chat-lock:{roomId}                                         -> 409 if fails
  READ room:room:{roomId}                                       -> 404 if null
  IF room.Status == Locked                                      -> 403
  READ bans:ban:{roomId}:{callerSessionId}
  IF ban exists and not expired                                  -> 403
  READ room-type by room.RoomTypeCode
  READ participants:{roomId} HashGetAll
  IF caller already in participants
    RETURN (200, ChatRoomResponse)                               // idempotent
  IF participants.Count >= effectiveMaxParticipants              -> 409
  WRITE participants:{roomId} HashSet(callerSessionId, ChatParticipantModel)
  WRITE room:room:{roomId} <- LastActivityAt=now
  WRITE cache:room:{roomId} <- same
  CALL _permissionClient.UpdateSessionStateAsync({ sessionId, serviceId="chat", newState="in_room" })
  CALL _entitySessionRegistry.RegisterAsync("chat-room", roomId, sessionId)
  PUSH typing shortcuts to joining session (2 shortcuts: typing + end-typing)
  PUBLISH chat.participant.joined { roomId, roomTypeCode, participantSessionId, role, currentCount }
  PUSH ChatParticipantJoinedClientEvent to existing participant sessions
RETURN (200, ChatRoomResponse)
```

### LeaveRoom
POST /chat/room/leave | Roles: [user, state: chat=in_room]

```
// callerSessionId from ServiceRequestContext                    -> 401 if null
LOCK chat-lock:{roomId}                                         -> 409 if fails
  READ room:room:{roomId}                                       -> 404 if null
  READ participants:{roomId} HashGetAll
  IF caller not in participants                                  -> 404
  DELETE participants:{roomId} HashDelete(callerSessionId)
  IF leaving.Role == Owner AND remaining participants exist
    READ participants:{roomId} HashGetAll                        // re-fetch remaining
    // Promote first Moderator, or oldest Member by JoinedAt
    WRITE participants:{roomId} HashSet(newOwner with Role=Owner)
    PUBLISH chat.participant.role-changed { ..., newRole=Owner, changedBySessionId=null }
    PUSH ChatParticipantRoleChangedClientEvent to remaining sessions
  WRITE room:room:{roomId} <- LastActivityAt=now
  WRITE cache:room:{roomId} <- same
  CALL _permissionClient.ClearSessionStateAsync(callerSessionId) // swallows errors
  CALL _entitySessionRegistry.UnregisterAsync("chat-room", roomId, callerSessionId)
  // Clear typing state: SortedSetRemove + optional ChatTypingStoppedClientEvent
  PUSH revoke typing shortcuts to departing session (2 revocations)
  PUBLISH chat.participant.left { roomId, participantSessionId, remainingCount }
  IF remaining participants
    PUSH ChatParticipantLeftClientEvent to remaining sessions
RETURN (200, ChatRoomResponse)
```

### ListParticipants
POST /chat/room/participants | Roles: [user, state: chat=in_room]

```
READ room:room:{roomId}                                         -> 404 if null
  // cache-aside: Redis first, MySQL fallback
READ participants:{roomId} HashGetAll
// Mute status computed inline: isMuted && (mutedUntil == null || mutedUntil > now)
RETURN (200, ParticipantsResponse { roomId, participants })
```

### KickParticipant
POST /chat/room/participant/kick | Roles: [user, state: chat=in_room]

```
// callerSessionId from ServiceRequestContext                    -> 401 if null
LOCK chat-lock:{roomId}                                         -> 409 if fails
  READ room:room:{roomId}                                       -> 404 if null
  READ participants:{roomId} HashGetAll
  IF caller not found OR target not found                        -> 404
  IF caller not Owner/Moderator                                  -> 403
  IF target.Role <= caller.Role AND caller != Owner              -> 403 (role hierarchy)
  DELETE participants:{roomId} HashDelete(targetSessionId)
  WRITE room:room:{roomId} <- LastActivityAt=now
  WRITE cache:room:{roomId} <- same
  CALL _permissionClient.ClearSessionStateAsync(targetSessionId) // swallows errors
  CALL _entitySessionRegistry.UnregisterAsync("chat-room", roomId, targetSessionId)
  // Clear typing state + revoke typing shortcuts for target
  PUBLISH chat.participant.kicked { roomId, targetSessionId, kickedBySessionId, reason }
  PUSH ChatParticipantKickedClientEvent to all participants + target session
RETURN (200, ChatRoomResponse)
```

### BanParticipant
POST /chat/room/participant/ban | Roles: [user, state: chat=in_room]

```
// callerSessionId from ServiceRequestContext                    -> 401 if null
LOCK chat-lock:{roomId}                                         -> 409 if fails
  READ room:room:{roomId}                                       -> 404 if null
  READ participants:{roomId} HashGetAll
  IF caller not found OR caller not Owner/Moderator              -> 403
  WRITE bans:ban:{roomId}:{targetSessionId} <- ChatBanModel { optional ExpiresAt }
  READ participants:{roomId} HashGet(targetSessionId)            // check if currently in room
  IF target is present
    DELETE participants:{roomId} HashDelete(targetSessionId)
    CALL _permissionClient.ClearSessionStateAsync(targetSessionId)
  CALL _entitySessionRegistry.UnregisterAsync("chat-room", roomId, targetSessionId)
  // Clear typing state + revoke typing shortcuts for target
  WRITE room:room:{roomId} <- LastActivityAt=now
  WRITE cache:room:{roomId} <- same
  PUBLISH chat.participant.banned { roomId, targetSessionId, bannedBySessionId, reason, durationMinutes }
  PUSH ChatParticipantBannedClientEvent to all participants + target session
RETURN (200, ChatRoomResponse)
```

### UnbanParticipant
POST /chat/room/participant/unban | Roles: [user, state: chat=in_room]

```
// callerSessionId from ServiceRequestContext                    -> 401 if null
LOCK chat-lock:{roomId}                                         -> 409 if fails
  READ room:room:{roomId}                                       -> 404 if null
  READ participants:{roomId} HashGetAll
  IF caller not found OR caller not Owner/Moderator              -> 403
  READ bans:ban:{roomId}:{targetSessionId}                      -> 404 if null
  DELETE bans:ban:{roomId}:{targetSessionId}
  PUBLISH chat.participant.unbanned { roomId, targetSessionId, unbannedBySessionId }
  IF participants present
    PUSH ChatParticipantUnbannedClientEvent to all participant sessions
RETURN (200, ChatRoomResponse)
```

### MuteParticipant
POST /chat/room/participant/mute | Roles: [user, state: chat=in_room]

```
// callerSessionId from ServiceRequestContext                    -> 401 if null
LOCK chat-lock:{roomId}                                         -> 409 if fails
  READ room:room:{roomId}                                       -> 404 if null
  READ participants:{roomId} HashGetAll
  IF caller not found OR caller not Owner/Moderator              -> 403
  READ participants:{roomId} HashGet(targetSessionId)            -> 404 if null
  WRITE participants:{roomId} HashSet(target with IsMuted=true, MutedUntil)
  PUBLISH chat.participant.muted { roomId, targetSessionId, mutedBySessionId, durationMinutes }
  PUSH ChatParticipantMutedClientEvent to all participant sessions
RETURN (200, ChatRoomResponse)
```

### UnmuteParticipant
POST /chat/room/participant/unmute | Roles: [user, state: chat=in_room]

```
// callerSessionId from ServiceRequestContext                    -> 401 if null
LOCK chat-lock:{roomId}                                         -> 409 if fails
  READ room:room:{roomId}                                       -> 404 if null
  READ participants:{roomId} HashGetAll
  IF caller not found OR caller not Owner/Moderator              -> 403
  READ participants:{roomId} HashGet(targetSessionId)            -> 404 if null
  IF target not muted                                            -> 400
  WRITE participants:{roomId} HashSet(target with IsMuted=false, MutedUntil=null)
  PUBLISH chat.participant.unmuted { roomId, targetSessionId, unmutedBySessionId }
  PUSH ChatParticipantUnmutedClientEvent to all participant sessions
RETURN (200, ChatRoomResponse)
```

### ChangeParticipantRole
POST /chat/room/participant/change-role | Roles: [user, state: chat=in_room]

```
// callerSessionId from ServiceRequestContext                    -> 401 if null
LOCK chat-lock:{roomId}                                         -> 409 if fails
  READ room:room:{roomId}                                       -> 404 if null
  READ participants:{roomId} HashGetAll
  IF caller not found OR caller not Owner                        -> 403
  IF target not found                                            -> 404
  IF targetSessionId == callerSessionId                          -> 400 (cannot self-change)
  IF newRole == Owner                                            -> 400 (cannot promote to Owner)
  WRITE participants:{roomId} HashSet(target with Role=newRole)
  PUBLISH chat.participant.role-changed { roomId, participantSessionId, oldRole, newRole, changedBySessionId }
  PUSH ChatParticipantRoleChangedClientEvent to all participant sessions
RETURN (200, ChatRoomResponse)
```

### SendMessage
POST /chat/message/send | Roles: [user, state: chat=in_room]

```
// callerSessionId from ServiceRequestContext                    -> 401 if null
READ room:room:{roomId}                                         -> 404 if null
  // cache-aside: Redis first, MySQL fallback
IF room.Status == Locked OR room.Status == Archived              -> 403
READ participants:{roomId} HashGet(callerSessionId)              -> 403 if null
IF participant.Role == ReadOnly                                  -> 403
READ room-type by room.RoomTypeCode                              -> 500 if null (data integrity)

// Lazy auto-unmute: if muted and MutedUntil has passed
IF participant.IsMuted AND participant.MutedUntil <= now
  WRITE participants:{roomId} HashSet(sender with IsMuted=false, MutedUntil=null)
  PUBLISH chat.participant.unmuted { unmutedBySessionId=callerSessionId }
  PUSH ChatParticipantUnmutedClientEvent to all participant sessions
ELSE IF participant.IsMuted                                      -> 403

// Rate limit check
WRITE participants:rate:{roomId}:{sessionId} INCR (TTL=60s)
IF counter > effectiveRateLimit                                  -> 400

// Validate message content against room type's MessageFormat + ValidatorConfig
IF validation fails                                              -> 400

WRITE participants:{roomId} HashSet(sender with LastActivityAt=now)

IF roomType.PersistenceMode == Persistent
  WRITE messages:{roomId}:{messageId} <- ChatMessageModel
ELSE
  WRITE buffer:{roomId}:{messageId} <- ChatMessageModel (TTL=EphemeralMessageTtlMinutes*60)

WRITE room:room:{roomId} <- LastActivityAt=now
WRITE cache:room:{roomId} <- same

// Clear typing state on send
DELETE typing:active SortedSetRemove({roomId:N}:{sessionId:N})
IF was typing -> PUSH ChatTypingStoppedClientEvent via entity sessions

READ participants:{roomId} HashGetAll                            // broadcast target list
PUBLISH chat.message.sent { roomId, messageId, messageFormat, ... }  // no text/custom content
PUSH ChatMessageReceivedClientEvent { full content } to all participant sessions
RETURN (200, ChatMessageResponse)
```

### SendMessageBatch
POST /chat/message/send-batch | Roles: []

```
READ room:room:{roomId}                                         -> 404 if null
  // cache-aside: Redis first, MySQL fallback
IF room.Status != Active                                         -> 403
READ room-type by room.RoomTypeCode                              -> 500 if null
READ participants:{roomId} HashGetAll                            // broadcast target list

FOREACH message in batch
  // Per-message try-catch: failures collected, not propagated
  // Validate content against room type format
  IF roomType.PersistenceMode == Persistent
    WRITE messages:{roomId}:{messageId} <- ChatMessageModel
  ELSE
    WRITE buffer:{roomId}:{messageId} <- ChatMessageModel (TTL)
  PUBLISH chat.message.sent { roomId, messageId, messageFormat, ... }  // metadata only, no text/custom
  PUSH ChatMessageReceivedClientEvent to all participant sessions

WRITE room:room:{roomId} <- LastActivityAt=now
WRITE cache:room:{roomId} <- same
RETURN (200, SendMessageBatchResponse { messageCount, failed })
```

### GetMessageHistory
POST /chat/message/history | Roles: [user, state: chat=in_room]

```
READ room:room:{roomId}                                         -> 404 if null
READ room-type by room.RoomTypeCode                              -> 500 if null

IF roomType.PersistenceMode == Ephemeral
  RETURN (200, MessageHistoryResponse { messages=[], hasMore=false })

QUERY messages WHERE $.RoomId = roomId [AND $.Timestamp < cursor]
  ORDER BY $.Timestamp DESC PAGED(0, limit+1)
// Request limit+1 to detect hasMore; return first limit items
// NextCursor = last message's Timestamp.ToString("O")
RETURN (200, MessageHistoryResponse { messages, hasMore, nextCursor })
```

### DeleteMessage
POST /chat/message/delete | Roles: [user, state: chat=in_room]

```
// callerSessionId from ServiceRequestContext                    -> 401 if null
READ messages:{roomId}:{messageId}                               -> 404 if null
READ room:room:{roomId}                                         -> 404 if null
DELETE messages:{roomId}:{messageId}
READ participants:{roomId} HashGetAll                            // broadcast target list
PUBLISH chat.message.deleted { roomId, messageId, deletedBySessionId }
PUSH ChatMessageDeletedClientEvent to all participant sessions
RETURN (200, ChatMessageResponse)                                // snapshot before deletion
```

### PinMessage
POST /chat/message/pin | Roles: [user, state: chat=in_room]

```
LOCK chat-lock:{roomId}                                         -> 409 if fails
  READ messages:{roomId}:{messageId}                             -> 404 if null
  IF already pinned
    RETURN (200, ChatMessageResponse)                            // idempotent
  COUNT messages WHERE $.RoomId = roomId AND $.IsPinned = true
  IF count >= MaxPinnedMessagesPerRoom                           -> 409
  WRITE messages:{roomId}:{messageId} <- IsPinned=true
  READ room:room:{roomId}                                       -> 404 if null
  READ participants:{roomId} HashGetAll
  PUSH ChatMessagePinnedClientEvent { isPinned=true } to all participant sessions
RETURN (200, ChatMessageResponse)
```

### UnpinMessage
POST /chat/message/unpin | Roles: [user, state: chat=in_room]

```
READ messages:{roomId}:{messageId}                               -> 404 if null
WRITE messages:{roomId}:{messageId} <- IsPinned=false
READ room:room:{roomId}                                         -> 404 if null
READ participants:{roomId} HashGetAll
PUSH ChatMessagePinnedClientEvent { isPinned=false } to all participant sessions
RETURN (200, ChatMessageResponse)
```

### SearchMessages
POST /chat/message/search | Roles: [user, state: chat=in_room]

```
READ room:room:{roomId}                                         -> 404 if null
READ room-type by room.RoomTypeCode
IF roomType != null AND roomType.PersistenceMode == Ephemeral    -> 400
  // Missing room type (null) skips ephemeral check, treated as persistent
QUERY messages WHERE $.RoomId = roomId AND $.TextContent CONTAINS query
  ORDER BY $.Timestamp DESC PAGED(0, limit)
RETURN (200, SearchMessagesResponse { messages, totalMatches })
```

### AdminListRooms
POST /chat/admin/rooms | Roles: [admin]

```
QUERY rooms WHERE $.RoomId EXISTS
  [+ $.RoomTypeCode = X] [+ $.Status = X]
  ORDER BY $.CreatedAt DESC PAGED(page, pageSize)
FOREACH room in results
  READ participants:{roomId} HashCount                           // sequential
RETURN (200, ListRoomsResponse { items, totalCount, page, pageSize })
```

### AdminGetStats
POST /chat/admin/stats | Roles: [admin]

```
COUNT rooms WHERE $.RoomId EXISTS                                // totalRooms
COUNT rooms WHERE $.Status = Active                              // activeRooms
COUNT rooms WHERE $.Status = Locked                              // lockedRooms
COUNT rooms WHERE $.Status = Archived                            // archivedRooms
COUNT room-types WHERE $.Code EXISTS                             // totalRoomTypes
QUERY rooms WHERE $.RoomId EXISTS PAGED(0, config.AdminStatsMaxRooms)  // T21: configurable cap (default 1000)
FOREACH room in results
  READ participants:{roomId} HashCount                           // sequential, O(N)
RETURN (200, AdminStatsResponse { totalRooms, activeRooms, lockedRooms, archivedRooms, totalParticipants, totalRoomTypes })
```

### AdminForceCleanup
POST /chat/admin/cleanup | Roles: [admin]

```
// Delegates to CleanupIdleRoomsAsync — see Background Services
RETURN (200, AdminCleanupResponse { cleanedRooms, archivedRooms, deletedRooms })
```

### Typing
POST /chat/typing | Roles: [] (shortcut-authorized)

```
READ typing:active SortedSetScore("{roomId:N}:{sessionId:N}")
WRITE typing:active SortedSetAdd("{roomId:N}:{sessionId:N}", epochMs)
IF existingScore == null                                         // new typing, not heartbeat
  READ participants:{roomId} HashGet(sessionId)                  // display name
  PUSH ChatTypingStartedClientEvent via IEntitySessionRegistry to room sessions
RETURN (200)
```

### EndTyping
POST /chat/end-typing | Roles: [] (shortcut-authorized)

```
DELETE typing:active SortedSetRemove("{roomId:N}:{sessionId:N}")
IF was present
  PUSH ChatTypingStoppedClientEvent via IEntitySessionRegistry to room sessions
RETURN (200)
```

---

## Background Services

### IdleRoomCleanupWorker
**Interval**: `IdleRoomCleanupIntervalMinutes` (default: 60 min)
**Startup Delay**: `IdleRoomCleanupStartupDelaySeconds` (default: 30s)
**Lock**: `chat-lock:"idle-room-cleanup"` (expiry: `IdleRoomCleanupLockExpirySeconds`)

```
LOCK chat-lock:"idle-room-cleanup"
  QUERY rooms WHERE $.LastActivityAt < (now - IdleRoomTimeoutMinutes) PAGED(0, 1000)
  FOREACH room in results
    IF room.ContractId set -> SKIP                               // contract-governed, not auto-cleaned
    READ room-type by room.RoomTypeCode
    IF roomType.PersistenceMode == Persistent AND NOT room.IsArchived
      CALL _resourceClient.ExecuteCompressAsync({ resourceType="chat-room", resourceId=roomId })
      IF success
        WRITE room:room:{roomId} <- IsArchived=true, Status=Archived
        DELETE cache:room:{roomId}
        // archivedCount++
      ELSE
        // LogWarning, continue
    ELSE                                                         // ephemeral or already-archived
      READ participants:{roomId} HashGetAll
      FOREACH participant
        CALL _permissionClient.ClearSessionStateAsync(sessionId) // swallows errors
      DELETE participants:{roomId}
      DELETE room:room:{roomId}
      DELETE cache:room:{roomId}
      PUBLISH chat.room.deleted { ..., deletedReason="Idle room cleanup" }
      // deletedCount++
```

### TypingExpiryWorker
**Interval**: `TypingWorkerIntervalMilliseconds` (default: 1000 ms)
**Lock**: None (no distributed lock)

```
READ typing:active SortedSetRangeByScore(0, now - TypingTimeoutSeconds*1000, limit=TypingWorkerBatchSize)
FOREACH expired entry
  DELETE typing:active SortedSetRemove(member)
  // Parse roomId and sessionId from member string
  PUSH ChatTypingStoppedClientEvent via IEntitySessionRegistry to room sessions
```

### BanExpiryWorker
**Interval**: `BanExpiryIntervalMinutes` (default: 60 min)
**Startup Delay**: `BanExpiryStartupDelaySeconds` (default: 30s)
**Lock**: `chat-lock:"ban-expiry-cycle"` (expiry: `BanExpiryLockExpirySeconds`)

```
LOCK chat-lock:"ban-expiry-cycle"
  QUERY bans WHERE $.ExpiresAt < now PAGED(0, BanExpiryBatchSize)
  FOREACH ban in results
    DELETE bans:ban:{roomId}:{sessionId}
```

### MessageRetentionWorker
**Interval**: `MessageRetentionCleanupIntervalMinutes` (default: 360 min)
**Startup Delay**: `MessageRetentionStartupDelaySeconds` (default: 60s)
**Lock**: `chat-lock:"message-retention-cleanup"` (expiry: `MessageRetentionLockExpirySeconds`)

```
LOCK chat-lock:"message-retention-cleanup"
  QUERY room-types WHERE $.PersistenceMode = Persistent AND $.RetentionDays != null AND $.Status = Active
    PAGED(0, MessageRetentionMaxRoomTypeResults)
  FOREACH roomType in results
    cutoff = now - roomType.RetentionDays days
    QUERY rooms WHERE $.RoomTypeCode = roomType.Code PAGED(0, MessageRetentionMaxRoomsPerType)
    FOREACH room in rooms
      QUERY messages WHERE $.RoomId = roomId AND $.Timestamp < cutoff
        PAGED(0, MessageRetentionBatchSize)
      FOREACH message in expired
        DELETE messages:{roomId}:{messageId}
```
