# Chat Plugin Deep Dive

> **Plugin**: lib-chat
> **Schema**: schemas/chat-api.yaml
> **Version**: 1.0.0
> **Layer**: AppFoundation
> **State Store**: chat-rooms (MySQL), chat-rooms-cache (Redis), chat-messages (MySQL), chat-messages-ephemeral (Redis), chat-participants (Redis), chat-room-types (MySQL), chat-bans (MySQL), chat-lock (Redis)
> **Implementation Map**: [docs/maps/CHAT.md](../maps/CHAT.md)
> **Short**: Universal typed message channel primitives with ephemeral and persistent storage

---

## Overview

The Chat service (L1 AppFoundation) provides universal typed message channel primitives for real-time communication. Room types determine valid message formats (text, sentiment, emoji, custom-validated payloads), with rooms optionally governed by Contract instances for lifecycle management. Supports ephemeral (Redis TTL) and persistent (MySQL) message storage, participant moderation (kick/ban/mute), rate limiting via atomic Redis counters, typing indicators via Redis sorted set with server-side expiry, and automatic idle room cleanup. Three built-in room types (text, sentiment, emoji) are registered on startup. Internal-only, never internet-facing.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| *(none currently)* | No other plugin references `IChatClient` or subscribes to chat events |

> **Planned consumers** (tracked via issues on their respective plugins):
> - **Gardener** ([#386](https://github.com/beyond-immersion/bannou-service/issues/386)): Bond communication between paired guardian spirits via Chat rooms with progressive message format expansion gated by bond strength. Gardener (L4) creates and manages `bond_communication` rooms using Chat's existing room type and contract integration. No Chat changes needed.
> - **Lexicon** ([#454](https://github.com/beyond-immersion/bannou-service/issues/454)): NPC structured communication via a custom `lexicon` room type where messages are Lexicon concept tuples (`[INTENT] + [SUBJECT]* + [MODIFIER]* + [CONTEXT]*`) validated against the Lexicon ontology. Chat's room type registration and custom `ValidatorConfig` are already sufficient. Blocked on lib-lexicon implementation.
> - **Connect** ([#382](https://github.com/beyond-immersion/bannou-service/issues/382)): Companion chat room integration for WebSocket sessions. Connect (L1) can use `IChatClient` as a hard dependency (same layer). The `CompanionRoomMode` config property exists in Connect's schema but has no runtime implementation yet.
> - Chat publishes 17 service events available for future consumers (e.g., analytics event ingestion).

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
| `MessageRetentionMaxRoomTypeResults` | `CHAT_MESSAGE_RETENTION_MAX_ROOM_TYPE_RESULTS` | 1000 | Maximum room types with retention configuration to process per cleanup cycle |
| `MessageRetentionMaxRoomsPerType` | `CHAT_MESSAGE_RETENTION_MAX_ROOMS_PER_TYPE` | 1000 | Maximum rooms per type to process per retention cleanup cycle |
| `ServerSalt` | `CHAT_SERVER_SALT` | (dev default) | Server salt for session shortcut GUID generation |

---

## Visual Aid

```
Room Lifecycle & Storage Bifurcation
=====================================

 CreateRoom
 │
 ┌───────┴───────┐
 │ MySQL (room) │
 │ Redis (cache) │
 └───────┬───────┘
 │
 ┌───────────┼───────────┐
 │ │ │
 JoinRoom SendMessage Contract Event
 │ │ │
 ┌────┴────┐ ┌───┴────┐ ┌──┴──────────────────────┐
 │ Redis │ │ Check │ │ FindRoomsByContractId │
 │ Hash │ │ Format │ │ (query, limit 100) │
 │ {room}: │ └───┬────┘ └──┬──────────────────────┘
 │ {sess} │ │ │
 └────┬────┘ ┌───┴────────┐ │ ContractRoomAction:
 │ │ │ ├─ Lock → status=Locked
 Permission │ Persistent │ ├─ Archive → Resource.Compress
 State: │ room? │ ├─ Delete → notify + wipe
 "in_room" │ │ └─ Continue → no-op
 │ Yes No │
 │ │ │ │
 ┌───┘ │ │ │
 │ MySQL│ Redis │
 │ msg │ msg │
 │ store│ +TTL │
 └────────┴───────┘
 │
 Idle Cleanup Worker
 (periodic scan)
 │
 ┌───────┴───────┐
 │ Contract room?│
 │ → skip │
 │ Persistent? │
 │ → archive │
 │ Ephemeral? │
 │ → delete │
 └───────────────┘
```

---

## Stubs & Unimplemented Features

*(None -- all endpoints implemented)*

---

## Potential Extensions

1. **Message edit support**: Add edit endpoint with version history tracking, edit timestamps, and `ChatMessageEditedEvent`/`ChatMessageEditedClientEvent` for real-time UI updates.
<!-- AUDIT:NEEDS_DESIGN:2026-02-22:https://github.com/beyond-immersion/bannou-service/issues/450 -->

2. **Threaded replies**: Add `replyToMessageId` to messages for conversation threading, with threaded history query support.
<!-- AUDIT:NEEDS_DESIGN:2026-02-22:https://github.com/beyond-immersion/bannou-service/issues/451 -->

3. **Message reactions**: Allow participants to add emoji reactions to messages, stored as a separate model linked by message ID.
<!-- AUDIT:NEEDS_DESIGN:2026-02-22:https://github.com/beyond-immersion/bannou-service/issues/452 -->

### North Star: Social Fabric Transport Layer

Chat is a critical component of the NPC social communication stack described in VISION.md's "Social Fabric" section. The architecture positions Chat as the **transport layer** for a multi-service communication pipeline: Lexicon (L4) provides concept ontology and vocabulary validation, Collection (L2) gates vocabulary by character discovery level, Hearsay (L4) propagates beliefs with concept-level distortion across social hops, Disposition (L4) provides drive-motivated communication needs, and Actor (L2) executes ABML social behaviors that compose and interpret structured messages. Chat's role is to provide the typed message channels with format validation, rate limiting, persistence, and real-time delivery -- all of which are already implemented.

Key existing infrastructure that serves this vision:
- **Custom room types with `ValidatorConfig`**: Lexicon registers a `lexicon` room type on startup with structured message validation
- **`SendMessageBatch`**: Enables bulk NPC communication at 100K NPC scale via game server batching
- **Ephemeral vs persistent storage**: NPC chatter can use ephemeral rooms; important social interactions persist
- **Contract-governed rooms**: Quest-bound, bond-bound, and encounter-bound conversations get lifecycle management

No Chat changes are needed for this integration path -- the transport layer is ready. The blocking dependency is lib-lexicon (L4), tracked in [#454](https://github.com/beyond-immersion/bannou-service/issues/454).

---

## Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `roomTypeCode` (on rooms, messages, events) | B (Content Code) | Opaque string | Room types are a dynamic registry of string codes; three built-in types (`text`, `sentiment`, `emoji`) plus unlimited custom types registered per game service via API. Extensible without schema changes. |
| `senderType` (on join, participant, message models, and events) | B (Content Code) | Opaque string | Identifies what kind of entity sent a message (e.g., `"session"`, `"character"`, `"system"`, `"npc"`); opaque to Chat, defined by callers, extensible without schema changes. |
| `MessageFormat` | C (System State) | Service-specific enum | Finite content format modes (`Text`, `Sentiment`, `Emoji`, `Custom`) determining validation rules for room messages. |
| `PersistenceMode` | C (System State) | Service-specific enum | Finite storage modes (`Ephemeral`, `Persistent`) determining whether messages go to Redis (TTL) or MySQL (durable). |
| `RoomTypeStatus` | C (System State) | Service-specific enum | Finite lifecycle states for room type definitions (`Active`, `Deprecated`). |
| `ChatRoomStatus` | C (System State) | Service-specific enum | Finite room lifecycle states (`Active`, `Locked`, `Archived`). |
| `ChatParticipantRole` | C (System State) | Service-specific enum | Finite participant privilege levels (`Owner`, `Moderator`, `Member`, `ReadOnly`) determining moderation capabilities. |
| `ContractRoomAction` | C (System State) | Service-specific enum | Finite actions for contract lifecycle responses (`Continue`, `Lock`, `Archive`, `Delete`). |
| `ChatRoomLockReason` | C (System State) | Service-specific enum | Finite reasons for room locking (`ContractFulfilled`, `ContractBreachDetected`, `ContractTerminated`, `ContractExpired`, `Manual`). |

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*(None)*

### Intentional Quirks (Documented Behavior)

1. **Service events strip text content for privacy**: `ChatMessageSentEvent` (service bus) intentionally omits `TextContent` and `CustomPayload`. Only metadata (roomId, messageFormat, sentimentCategory, emojiCode) is published. Client events (`ChatMessageReceivedEvent`) include full content since they're private to room participants.

2. **Mute auto-expires lazily on next message attempt**: No background worker unmutes participants. Instead, `SendMessage` checks `MutedUntil` and clears the mute if expired, publishing `ChatParticipantUnmutedEvent` and `ChatParticipantUnmutedClientEvent`. The participant appears muted in roster queries until their next send attempt or explicit `UnmuteParticipant` call.

3. **Idle cleanup skips contract-governed rooms**: Rooms with `ContractId` set are never cleaned up by the idle worker. Their lifecycle is managed entirely by contract events (fulfilled/breach/terminated/expired).

4. **Idle cleanup archives persistent rooms, deletes ephemeral**: Different cleanup actions based on room type `PersistenceMode`. Persistent rooms have valuable history and are archived via Resource; ephemeral rooms are transient and deleted outright.

5. **Owner promotion on leave uses seniority fallback**: When the room owner leaves, the first Moderator is promoted. If no moderators exist, the oldest member (by `JoinedAt`) becomes owner. Publishes `ChatParticipantRoleChangedEvent` and `ChatParticipantRoleChangedClientEvent` with `ChangedBySessionId = null` to indicate automatic promotion. This ensures a room always has an owner while participants remain.

6. **Built-in room types cannot be deleted**: No delete endpoint exists for room types -- only deprecation. The three built-in types (`text`, `sentiment`, `emoji`) are idempotently re-registered on every startup, so deprecating them has no lasting effect.

7. **Room cache is write-through, not invalidation-based**: Both MySQL and Redis cache are updated on every room mutation. No cache invalidation needed -- data is always consistent across both stores.

8. **Ephemeral rooms return empty history**: `GetMessageHistory` returns an empty list for ephemeral room types because messages live in Redis with TTL and are not queryable for ordered history.

9. **Message history cursor is ISO 8601 round-trip format**: Cursor pagination uses `Timestamp.ToString("O")` as the cursor value. Clients must preserve the cursor string exactly as received.

10. **Permission state errors are swallowed**: `SetParticipantPermissionStateAsync` and `ClearParticipantPermissionStateAsync` catch `ApiException` (logged as warning) and general `Exception` (logged as error) without failing the parent operation. Permission failures do not block join/leave/kick/ban operations.

11. **Typing events go to ALL room participants including the sender**: `IEntitySessionRegistry.PublishToEntitySessionsAsync` broadcasts to all registered entity sessions for the room, including the session that triggered the typing. Client SDKs are expected to filter self-originated events by comparing `ParticipantSessionId` to their own session ID.

12. **Typing shortcuts use empty `x-permissions`**: Typing and end-typing endpoints are not in the capability manifest. Authorization comes from session shortcut existence -- shortcuts are published only to joined participants and revoked on departure. Stale shortcuts (after disconnect) are harmless; the background worker cleans up expired typing state.

13. **Entity session registration for room event routing**: Chat registers each participant as an entity session (`entityType="chat-room"`, `entityId=roomId`) on join, and unregisters on leave/kick/ban. ConnectService's existing disconnect handler calls `UnregisterSessionAsync` for automatic cleanup on WebSocket disconnect. This enables typing event fan-out via `IEntitySessionRegistry` without participant store lookups.

14. **Typing heartbeat deduplication**: Only the first typing signal for a session+room publishes `ChatTypingStartedClientEvent`. Subsequent heartbeat refreshes silently update the sorted set timestamp without re-publishing the start event. This prevents UI flicker from rapid heartbeat signals.

15. **SendMessageBatch publishes per-message service events**: `SendMessageBatch` publishes a `ChatMessageSentEvent` for each successfully sent message in the batch, matching `SendMessage` behavior exactly. This ensures downstream consumers see all messages regardless of send path. RabbitMQ handles the per-message event volume efficiently even for large batches.

16. **Ban expiry is silent**: The `BanExpiryWorker` deletes expired ban records from MySQL without publishing `chat.participant.unbanned` events. Contrast with `UnbanParticipant` which explicitly publishes the event. Ban record deletion is garbage collection — the actual state transition (participant becomes eligible to rejoin) happens passively when `ExpiresAt` passes, not when the worker cleans the record. Same passive-expiry pattern as mute auto-expiry (quirk #2).

### Deprecation Lifecycle (Category B)

Room types are **Category B entities** — rooms reference room types by code, and existing rooms must continue to function after a room type is deprecated. Per:

- **Deprecation is one-way**: Once deprecated, a room type cannot be undeprecated. No undeprecate endpoint exists.
- **No delete endpoint**: Room type definitions persist forever. Only deprecation is supported (see quirk #6).
- **Instance creation guard**: Creating new rooms with a deprecated room type code must be rejected with `BadRequest`.
- **Storage model**: Room type definitions use triple-field deprecation: `IsDeprecated` (bool), `DeprecatedAt` (DateTimeOffset?), `DeprecationReason` (string?).
- **Idempotent deprecation**: Deprecating an already-deprecated room type returns `OK` (not `Conflict`).
- **List filtering**: `ListRoomTypes` includes `includeDeprecated` parameter (default: `false`).
- **Events**: Deprecation is communicated via `chat.room-type.updated` with `changedFields` containing the deprecation fields (no dedicated deprecation event per tenets).

### Design Considerations (Requires Planning)

1. **AdminGetStats has O(N) participant counting**: Queries up to 1000 rooms, then performs individual `HashCount` calls for each room to sum total participants. Could become slow with many active rooms. Consider maintaining a running total or using a dedicated counter.
<!-- AUDIT:NEEDS_DESIGN:2026-02-22:https://github.com/beyond-immersion/bannou-service/issues/455 -->

2. **Rate limit counters and typing sorted set share store with participant hashes**: Rate limit keys (`rate:{roomId}:{sessionId}`) and the typing sorted set (`typing:active`) use the same `chat-participants` Redis store as participant hashes. While key prefixes prevent collision, the store mixes three different data patterns (hashes, atomic counters, sorted set).
<!-- AUDIT:NEEDS_DESIGN:2026-02-22:https://github.com/beyond-immersion/bannou-service/issues/456 -->

---

## Work Tracking

*(No active items)*
