# Chat Plugin Deep Dive

> **Plugin**: lib-chat
> **Schema**: schemas/chat-api.yaml
> **Version**: 1.0.0
> **State Store**: chat-rooms (MySQL), chat-rooms-cache (Redis), chat-messages (MySQL), chat-messages-ephemeral (Redis), chat-participants (Redis), chat-room-types (MySQL), chat-bans (MySQL)

---

## Overview

The Chat service (L1 AppFoundation) provides universal typed message channel primitives for real-time communication. Room types determine valid message formats (text, sentiment, emoji, custom-validated payloads), with rooms optionally governed by Contract instances for lifecycle management. Supports ephemeral (Redis TTL) and persistent (MySQL) message storage, participant moderation (kick/ban/mute), rate limiting via atomic Redis counters, typing indicators via Redis sorted set with server-side expiry, and automatic idle room cleanup. Three built-in room types (text, sentiment, emoji) are registered on startup. Internal-only, never internet-facing.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | 7 state stores: MySQL for room types, rooms, messages, bans; Redis for room cache, ephemeral messages, participants |
| lib-state (`IDistributedLockProvider`) | Distributed locks for room type, room, and participant mutations |
| lib-state (`ICacheableStateStore`) | Redis hash operations for participant tracking, atomic INCR for rate limiting, sorted set for typing state |
| lib-messaging (`IMessageBus`) | Publishing 14 service event types and error events via `TryPublishErrorAsync` |
| lib-connect (`IClientEventPublisher`) | Publishing 12 client event types to WebSocket sessions for real-time UI updates |
| lib-connect (`IEntitySessionRegistry`) | Room-level entity session registration for typing event fan-out to all room participants |
| lib-contract (`IContractClient`) | Validating governing contract existence on room creation |
| lib-resource (`IResourceClient`) | Archiving rooms via `ExecuteCompressAsync` (idle cleanup and contract actions) |
| lib-permission (`IPermissionClient`) | Setting/clearing `in_room` permission state on join/leave/kick/ban |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| *(none currently)* | No other plugin references `IChatClient` or subscribes to chat events |

> **Note**: Chat publishes 14 service events that are available for future consumers. Game session companion room integration and analytics event ingestion are likely future dependents.

---

## State Storage

### Store: `chat-room-types` (Backend: MySQL, `IJsonQueryableStateStore`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `type:{scope}:{code}` | `ChatRoomTypeModel` | Room type definitions. Scope is gameServiceId GUID or `"global"` for built-in types |

### Store: `chat-rooms` (Backend: MySQL, `IJsonQueryableStateStore`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `room:{roomId}` | `ChatRoomModel` | Primary room records with lifecycle status, contract bindings, activity timestamps |

### Store: `chat-rooms-cache` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `room:{roomId}` | `ChatRoomModel` | Write-through cache for active rooms. Cache-aside read pattern: Redis first, MySQL fallback, populate on miss |

### Store: `chat-messages` (Backend: MySQL, `IJsonQueryableStateStore`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{roomId}:{messageId}` | `ChatMessageModel` | Persistent message history for durable room types |

### Store: `chat-messages-ephemeral` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{roomId}:{messageId}` | `ChatMessageModel` | Ephemeral messages with TTL from `EphemeralMessageTtlMinutes` config |

### Store: `chat-participants` (Backend: Redis, `ICacheableStateStore`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{roomId}` (Redis hash) | Hash of `sessionId` -> `ChatParticipantModel` | All participants for a room in one hash. Uses `HashGetAll`, `HashSet`, `HashDelete`, `HashCount` |
| `rate:{roomId}:{sessionId}` | Atomic counter | Rate limiting via Redis INCR with 60s TTL |
| `typing:active` (Sorted set) | Members: `{roomId:N}:{sessionId:N}`, Scores: Unix ms | Global sorted set tracking active typing state. Worker queries by score range for expiry |

### Store: `chat-bans` (Backend: MySQL, `IJsonQueryableStateStore`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `ban:{roomId}:{targetSessionId}` | `ChatBanModel` | Ban records with optional expiry timestamp |

---

## Events

### Published Service Events (via IMessageBus)

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `chat-room-type.created` | `ChatRoomTypeCreatedEvent` | Room type registered |
| `chat-room-type.updated` | `ChatRoomTypeUpdatedEvent` | Room type updated or deprecated |
| `chat-room.created` | `ChatRoomCreatedEvent` | Room created |
| `chat-room.updated` | `ChatRoomUpdatedEvent` | Room updated |
| `chat-room.deleted` | `ChatRoomDeletedEvent` | Room deleted (manual, idle cleanup, or contract action) |
| `chat-participant.joined` | `ChatParticipantJoinedEvent` | Participant joins room |
| `chat-participant.left` | `ChatParticipantLeftEvent` | Participant leaves room |
| `chat-participant.kicked` | `ChatParticipantKickedEvent` | Participant kicked by moderator |
| `chat-participant.banned` | `ChatParticipantBannedEvent` | Participant banned |
| `chat-participant.muted` | `ChatParticipantMutedEvent` | Participant muted |
| `chat-message.sent` | `ChatMessageSentEvent` | Message sent (metadata only, no text content for privacy) |
| `chat-message.deleted` | `ChatMessageDeletedEvent` | Message deleted |
| `chat.room.locked` | `ChatRoomLockedEvent` | Room locked (contract-triggered) |
| `chat.room.archived` | `ChatRoomArchivedEvent` | Room archived via Resource service |

### Published Client Events (via IClientEventPublisher)

| Event | Trigger | Recipients |
|-------|---------|------------|
| `ChatMessageReceivedEvent` | Message sent or batch sent | All room participants |
| `ChatMessageDeletedClientEvent` | Message deleted | All room participants |
| `ChatMessagePinnedEvent` | Message pinned/unpinned | All room participants |
| `ChatParticipantJoinedClientEvent` | Participant joins | Existing room participants |
| `ChatParticipantLeftClientEvent` | Participant leaves | Remaining participants |
| `ChatParticipantKickedClientEvent` | Participant kicked | All participants + kicked target |
| `ChatParticipantBannedClientEvent` | Participant banned | All participants + banned target |
| `ChatParticipantMutedClientEvent` | Participant muted | All room participants |
| `ChatRoomLockedClientEvent` | Room locked | All room participants |
| `ChatRoomDeletedClientEvent` | Room deleted | All participants (before deletion) |
| `ChatTypingStartedClientEvent` | Participant starts typing (new only, not heartbeat) | All room participants via `IEntitySessionRegistry` |
| `ChatTypingStoppedClientEvent` | Participant stops typing (explicit, timeout, send, or departure) | All room participants via `IEntitySessionRegistry` |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `contract.fulfilled` | `HandleContractFulfilledAsync` | Find rooms by contract, apply configured fulfilled action (default: Archive) |
| `contract.breach.detected` | `HandleContractBreachDetectedAsync` | Find rooms by contract, apply configured breach action (default: Lock) |
| `contract.terminated` | `HandleContractTerminatedAsync` | Find rooms by contract, apply configured terminated action (default: Delete) |
| `contract.expired` | `HandleContractExpiredAsync` | Find rooms by contract, apply configured expired action (default: Archive) |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `MaxRoomTypesPerGameService` | `CHAT_MAX_ROOM_TYPES_PER_GAME_SERVICE` | 50 | Maximum custom room types per game service scope |
| `DefaultMaxParticipantsPerRoom` | `CHAT_DEFAULT_MAX_PARTICIPANTS_PER_ROOM` | 100 | Fallback participant limit (room -> type -> config chain) |
| `DefaultRateLimitPerMinute` | `CHAT_DEFAULT_RATE_LIMIT_PER_MINUTE` | 60 | Fallback messages per minute per participant |
| `IdleRoomCleanupIntervalMinutes` | `CHAT_IDLE_ROOM_CLEANUP_INTERVAL_MINUTES` | 60 | Background worker cycle interval |
| `IdleRoomTimeoutMinutes` | `CHAT_IDLE_ROOM_TIMEOUT_MINUTES` | 1440 | Minutes of inactivity before idle cleanup eligibility |
| `EphemeralMessageTtlMinutes` | `CHAT_EPHEMERAL_MESSAGE_TTL_MINUTES` | 60 | Redis TTL for ephemeral room messages |
| `MaxPinnedMessagesPerRoom` | `CHAT_MAX_PINNED_MESSAGES_PER_ROOM` | 10 | Maximum pinned messages per room |
| `DefaultContractFulfilledAction` | `CHAT_DEFAULT_CONTRACT_FULFILLED_ACTION` | Archive | Default action when governing contract fulfilled |
| `DefaultContractBreachAction` | `CHAT_DEFAULT_CONTRACT_BREACH_ACTION` | Lock | Default action on governing contract breach |
| `DefaultContractTerminatedAction` | `CHAT_DEFAULT_CONTRACT_TERMINATED_ACTION` | Delete | Default action on governing contract termination |
| `DefaultContractExpiredAction` | `CHAT_DEFAULT_CONTRACT_EXPIRED_ACTION` | Archive | Default action on governing contract expiration |
| `ContractRoomQueryBatchSize` | `CHAT_CONTRACT_ROOM_QUERY_BATCH_SIZE` | 100 | Page size for paginated contract-room queries during contract lifecycle events |
| `MaxContractRoomQueryResults` | `CHAT_MAX_CONTRACT_ROOM_QUERY_RESULTS` | 1000 | Safety cap on total rooms processed per contract lifecycle event, logs warning if reached |
| `MessageHistoryPageSize` | `CHAT_MESSAGE_HISTORY_PAGE_SIZE` | 50 | Default page size for message history queries |
| `LockExpirySeconds` | `CHAT_LOCK_EXPIRY_SECONDS` | 15 | Distributed lock expiry timeout |
| `IdleRoomCleanupStartupDelaySeconds` | `CHAT_IDLE_ROOM_CLEANUP_STARTUP_DELAY_SECONDS` | 30 | Initial delay before first cleanup cycle |
| `IdleRoomCleanupLockExpirySeconds` | `CHAT_IDLE_ROOM_CLEANUP_LOCK_EXPIRY_SECONDS` | 120 | Distributed lock expiry for idle room cleanup batch cycle |
| `TypingTimeoutSeconds` | `CHAT_TYPING_TIMEOUT_SECONDS` | 5 | Seconds of inactivity before typing indicator auto-expires |
| `TypingWorkerIntervalMilliseconds` | `CHAT_TYPING_WORKER_INTERVAL_MILLISECONDS` | 1000 | How often the typing expiry worker checks for stale typing entries |
| `TypingWorkerBatchSize` | `CHAT_TYPING_WORKER_BATCH_SIZE` | 100 | Maximum expired entries processed per worker cycle |
| `BanExpiryIntervalMinutes` | `CHAT_BAN_EXPIRY_INTERVAL_MINUTES` | 60 | How often the ban expiry worker checks for expired bans |
| `BanExpiryStartupDelaySeconds` | `CHAT_BAN_EXPIRY_STARTUP_DELAY_SECONDS` | 30 | Initial delay before ban expiry worker begins first cycle |
| `BanExpiryBatchSize` | `CHAT_BAN_EXPIRY_BATCH_SIZE` | 1000 | Maximum expired ban records processed per worker cycle |
| `BanExpiryLockExpirySeconds` | `CHAT_BAN_EXPIRY_LOCK_EXPIRY_SECONDS` | 120 | Distributed lock expiry for ban expiry batch cycle |
| `MessageRetentionCleanupIntervalMinutes` | `CHAT_MESSAGE_RETENTION_CLEANUP_INTERVAL_MINUTES` | 360 | How often the background worker checks for expired persistent messages |
| `MessageRetentionStartupDelaySeconds` | `CHAT_MESSAGE_RETENTION_STARTUP_DELAY_SECONDS` | 60 | Initial delay before the message retention cleanup worker begins first cycle |
| `MessageRetentionBatchSize` | `CHAT_MESSAGE_RETENTION_BATCH_SIZE` | 500 | Maximum expired messages to delete per room per cleanup cycle |
| `MessageRetentionLockExpirySeconds` | `CHAT_MESSAGE_RETENTION_LOCK_EXPIRY_SECONDS` | 300 | Distributed lock expiry for message retention cleanup cycle |
| `ServerSalt` | `CHAT_SERVER_SALT` | (dev default) | Server salt for session shortcut GUID generation |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `IMessageBus` | Service event publishing (14 event types) and error event publishing |
| `IClientEventPublisher` | WebSocket client event publishing (12 event types) and session shortcut publish/revoke |
| `IEntitySessionRegistry` | Room-level entity session management and typing event fan-out |
| `IDistributedLockProvider` | Distributed locks for room type, room, and participant mutations |
| `ILogger<ChatService>` | Structured logging |
| `ChatServiceConfiguration` | Typed configuration access (29 properties) |
| `IEventConsumer` | Event consumer registration for 4 contract lifecycle events |
| `IContractClient` | Contract instance validation on room creation |
| `IResourceClient` | Room archival via `ExecuteCompressAsync` |
| `IPermissionClient` | Permission state management (`in_room` state) |
| `IStateStoreFactory` | Access to all 7 state stores |
| `IdleRoomCleanupWorker` | Background hosted service for periodic idle room cleanup |
| `TypingExpiryWorker` | Background hosted service for typing indicator timeout enforcement |
| `BanExpiryWorker` | Background hosted service for periodic expired ban cleanup |
| `MessageRetentionWorker` | Background hosted service for periodic expired persistent message cleanup |
| `ITelemetryProvider` | Telemetry span instrumentation for async helpers |

---

## API Endpoints (Implementation Notes)

### Room Type Management (5 endpoints)

Standard CRUD for room type definitions. `RegisterRoomType` enforces uniqueness per game service scope and `MaxRoomTypesPerGameService` limit via count query. `DeprecateRoomType` is idempotent. Room types are never truly deleted -- only deprecated (built-in types are re-registered on every startup).

### Room Management (6 endpoints)

`CreateRoom` validates room type exists and is active, optionally validates governing contract via `IContractClient.GetContractInstanceAsync`, and seeds contract action overrides from request or room type defaults. Room reads use cache-aside pattern (Redis first, MySQL fallback). `ArchiveRoom` delegates to `IResourceClient.ExecuteCompressAsync` and marks `IsArchived = true`. `DeleteRoom` notifies all participants via client event before clearing permission states, deleting participants, and removing room data from both MySQL and Redis cache.

### Participant Management (7 endpoints)

`JoinRoom` checks ban status, capacity, and assigns the requested role (defaults to Member if not specified). Sets `in_room` permission state on join. `LeaveRoom` includes owner promotion logic: if the leaving participant is Owner, promotes first Moderator, or oldest member (by `JoinedAt`) if no moderators exist. `KickParticipant` enforces role hierarchy (Owner can kick anyone, Moderator can kick Members only). `MuteParticipant` stores optional `MutedUntil` timestamp for timed mutes. `UnbanParticipant` directly deletes the ban record.

### Message Operations (7 endpoints)

`SendMessage` validates message content against room type's `MessageFormat` and `ValidatorConfig`, enforces rate limiting via atomic Redis INCR with 60s TTL, auto-unmutes lazily if `MutedUntil` has passed, stores in MySQL or Redis depending on `PersistenceMode`, and publishes both service event (metadata only) and client event (full content). `SendMessageBatch` processes entries sequentially, silently skipping validation failures. `GetMessageHistory` uses cursor-based pagination with ISO 8601 timestamp cursor; returns empty for ephemeral rooms. `SearchMessages` uses JSON query `Contains` operator for full-text search on persistent rooms only. `PinMessage` enforces `MaxPinnedMessagesPerRoom` limit.

### Typing Indicators (2 endpoints)

`Typing` records active typing state via Redis sorted set upsert (`typing:active` key, score = Unix ms). Only publishes `ChatTypingStartedClientEvent` on NEW typing (score was null); heartbeat refreshes silently update the timestamp. Accessed via session shortcuts published on room join -- `x-permissions: []` with authorization from shortcut existence. `EndTyping` immediately clears typing state and publishes `ChatTypingStoppedClientEvent` if the session was actively typing. Both use `IEntitySessionRegistry.PublishToEntitySessionsAsync` for room-wide fan-out.

### Admin Operations (3 endpoints)

`AdminListRooms` mirrors `ListRooms` with admin-level permission. `AdminGetStats` queries room counts by status and sums participant hash counts across all rooms (up to 1000). `AdminForceCleanup` delegates to the same `CleanupIdleRoomsAsync` method used by the background worker.

---

## Visual Aid

```
Room Lifecycle & Storage Bifurcation
=====================================

                   CreateRoom
                      │
              ┌───────┴───────┐
              │  MySQL (room)  │
              │  Redis (cache) │
              └───────┬───────┘
                      │
          ┌───────────┼───────────┐
          │           │           │
     JoinRoom    SendMessage    Contract Event
          │           │           │
     ┌────┴────┐  ┌───┴────┐  ┌──┴──────────────────────┐
     │ Redis   │  │ Check  │  │ FindRoomsByContractId    │
     │ Hash    │  │ Format │  │ (query, limit 100)       │
     │ {room}: │  └───┬────┘  └──┬──────────────────────┘
     │ {sess}  │      │          │
     └────┬────┘  ┌───┴────────┐ │  ContractRoomAction:
          │       │            │ ├─ Lock    → status=Locked
     Permission   │ Persistent │ ├─ Archive → Resource.Compress
     State:       │ room?      │ ├─ Delete  → notify + wipe
     "in_room"    │            │ └─ Continue → no-op
                  │   Yes  No  │
                  │    │    │  │
              ┌───┘    │    │  │
              │   MySQL│ Redis │
              │   msg  │ msg   │
              │   store│ +TTL  │
              └────────┴───────┘
                      │
              Idle Cleanup Worker
              (periodic scan)
                      │
              ┌───────┴───────┐
              │ Contract room?│
              │   → skip      │
              │ Persistent?   │
              │   → archive   │
              │ Ephemeral?    │
              │   → delete    │
              └───────────────┘
```

---

## Stubs & Unimplemented Features

None. All 30 API endpoints are fully implemented with complete business logic, validation, event publishing, and error handling.

---

## Potential Extensions

1. **Message edit support**: Add edit endpoint with version history tracking, edit timestamps, and `ChatMessageEditedEvent`/`ChatMessageEditedClientEvent` for real-time UI updates.

2. **Threaded replies**: Add `replyToMessageId` to messages for conversation threading, with threaded history query support.

3. **Message reactions**: Allow participants to add emoji reactions to messages, stored as a separate model linked by message ID.

4. **Lexicon room type for NPC communication**: A custom `lexicon` room type where messages are structured as Lexicon entry combinations rather than free text. NPCs would communicate in the same ontological building blocks they think in, with discovery-level validation gating vocabulary per character. Location-scoped social rooms would enable ambient social perception for NPC cognition. See [CHARACTER-COMMUNICATION.md](../guides/CHARACTER-COMMUNICATION.md) for the full architectural design.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*(None identified)*

### Intentional Quirks (Documented Behavior)

1. **Service events strip text content for privacy**: `ChatMessageSentEvent` (service bus) intentionally omits `TextContent` and `CustomPayload`. Only metadata (roomId, messageFormat, sentimentCategory, emojiCode) is published. Client events (`ChatMessageReceivedEvent`) include full content since they're private to room participants.

2. **Mute auto-expires lazily on next message attempt**: No background worker unmutes participants. Instead, `SendMessage` checks `MutedUntil` and clears the mute if expired. The participant appears muted in roster queries until their next send attempt.

3. **Idle cleanup skips contract-governed rooms**: Rooms with `ContractId` set are never cleaned up by the idle worker. Their lifecycle is managed entirely by contract events (fulfilled/breach/terminated/expired).

4. **Idle cleanup archives persistent rooms, deletes ephemeral**: Different cleanup actions based on room type `PersistenceMode`. Persistent rooms have valuable history and are archived via Resource; ephemeral rooms are transient and deleted outright.

5. **Owner promotion on leave uses seniority fallback**: When the room owner leaves, the first Moderator is promoted. If no moderators exist, the oldest member (by `JoinedAt`) becomes owner. This ensures a room always has an owner while participants remain.

6. **Built-in room types cannot be deleted**: No delete endpoint exists for room types -- only deprecation. The three built-in types (`text`, `sentiment`, `emoji`) are idempotently re-registered on every startup, so deprecating them has no lasting effect.

7. **Room cache is write-through, not invalidation-based**: Both MySQL and Redis cache are updated on every room mutation. No cache invalidation needed -- data is always consistent across both stores.

8. **Ephemeral rooms return empty history**: `GetMessageHistory` returns an empty list for ephemeral room types because messages live in Redis with TTL and are not queryable for ordered history.

9. **Message history cursor is ISO 8601 round-trip format**: Cursor pagination uses `Timestamp.ToString("O")` as the cursor value. Clients must preserve the cursor string exactly as received.

10. **Permission state errors are swallowed**: `SetParticipantPermissionStateAsync` and `ClearParticipantPermissionStateAsync` catch `ApiException` (logged as warning) and general `Exception` (logged as error) without failing the parent operation. Permission failures do not block join/leave/kick/ban operations.

11. **Typing events go to ALL room participants including the sender**: `IEntitySessionRegistry.PublishToEntitySessionsAsync` broadcasts to all registered entity sessions for the room, including the session that triggered the typing. Client SDKs are expected to filter self-originated events by comparing `ParticipantSessionId` to their own session ID.

12. **Typing shortcuts use empty `x-permissions`**: Typing and end-typing endpoints are not in the capability manifest. Authorization comes from session shortcut existence -- shortcuts are published only to joined participants and revoked on departure. Stale shortcuts (after disconnect) are harmless; the background worker cleans up expired typing state.

13. **Entity session registration for room event routing**: Chat registers each participant as an entity session (`entityType="chat-room"`, `entityId=roomId`) on join, and unregisters on leave/kick/ban. ConnectService's existing disconnect handler calls `UnregisterSessionAsync` for automatic cleanup on WebSocket disconnect. This enables typing event fan-out via `IEntitySessionRegistry` without participant store lookups.

14. **Typing heartbeat deduplication**: Only the first typing signal for a session+room publishes `ChatTypingStartedClientEvent`. Subsequent heartbeat refreshes silently update the sorted set timestamp without re-publishing the start event. This prevents UI flicker from rapid heartbeat signals.

### Design Considerations (Requires Planning)

1. **AdminGetStats has O(N) participant counting**: Queries up to 1000 rooms, then performs individual `HashCount` calls for each room to sum total participants. Could become slow with many active rooms. Consider maintaining a running total or using a dedicated counter.

2. **Rate limit counters and typing sorted set share store with participant hashes**: Rate limit keys (`rate:{roomId}:{sessionId}`) and the typing sorted set (`typing:active`) use the same `chat-participants` Redis store as participant hashes. While key prefixes prevent collision, the store mixes three different data patterns (hashes, atomic counters, sorted set).

---

## Work Tracking

### Completed
- **2026-02-22**: Fixed `IdleRoomCleanupWorker` T9 violation - added distributed lock for multi-instance safety
