# Implement Chat Service (L1 App Foundation)

> **Status**: Design Draft
> **Last Updated**: 2026-02-10
> **Depends On**: Contract (L1), Resource (L1), Permission (L1), Connect (L1), Auth (L1)
> **Related**: [STREAMING-ARCHITECTURE.md](STREAMING-ARCHITECTURE.md) (shared sentiment primitives), [VOICE-STREAMING.md](VOICE-STREAMING.md) (voice room companion rooms)

## Context

The Chat service is a new L1 App Foundation plugin that provides typed message channels as a universal communication primitive. Rather than a traditional text-only chat system, Chat provides a **typed channel architecture** where the channel type determines what messages are valid -- text, sentiment, emoji, or custom-validated payloads.

**Why this service exists**:
- Bannou has voice communication (lib-voice) but no text communication -- this is backwards from what most platforms ship first
- Applications (not just games) need chat: P2P sessions, support channels, collaboration tools
- Games need chat: in-game message boards, town criers, trade postings, NPC communication
- The streaming architecture (STREAMING-ARCHITECTURE.md) needs a delivery primitive for audience sentiments -- a sentiment chat room is a channel where messages ARE sentiments, reusable across lib-stream and lib-streaming
- Contract-governed rooms enable powerful lifecycle management: a contract's FSM governs room creation, moderation, and destruction via prebound APIs

**Why L1 (App Foundation)**, not L3:
- Zero L2+ dependencies -- Chat depends only on L0 (state, messaging) and L1 (connect, auth, permission, contract, resource)
- Connect (L1) should have firsthand knowledge of chat rooms to create companion channels for P2P sessions -- this requires Chat to be L1 (Connect can only depend on L0 and L1)
- Chat is wanted in every deployment scenario, regardless of whether game features are enabled
- Contract and Resource integration is natural: both are L1, hard dependencies are clean
- Same structural profile as Permission, Contract, and Resource -- lightweight infrastructure that every layer above can rely on

**Design decisions made during planning**:
- Room types are a **dynamic registry** (string codes, like seed types) -- not a fixed enum. Built-in types (text, sentiment, emoji) are pre-registered; custom types are added via API
- Rooms optionally reference a governing **Contract** -- contract state changes trigger room lifecycle actions (lock, archive, delete) and contract prebound APIs can call any chat endpoint (kick, ban, send system messages)
- **SentimentCategory** moves to `common-api.yaml` as a shared primitive used by both lib-chat and lib-stream
- Sender identity is **opaque polymorphic** (senderType string + senderId Guid + displayName string) -- no enum coupling to higher layers
- Message content is **polymorphic by room type** -- TextContent, SentimentContent, EmojiContent, or CustomContent (validated against room type's validator config)
- Moderation primitives (kick, ban, mute) exist as first-class endpoints; contracts can invoke them via prebound APIs but they work independently
- Connect creates companion text chat rooms for P2P sessions automatically

---

## Architecture Overview

### Service Identity

| Property | Value |
|----------|-------|
| **Layer** | L1 (AppFoundation) |
| **Plugin** | `plugins/lib-chat/` |
| **Schema prefix** | `chat` |
| **Service name** | `chat` |
| **Hard dependencies** | L0 (state, messaging, mesh), L1 (connect, auth, permission, contract, resource) |
| **Soft dependencies** | None |
| **Cannot depend on** | L2, L3, L4 |
| **When absent** | Crash -- L1 is required for all deployments |

### Dependency Diagram

```
lib-chat (L1 AppFoundation)
    │
    ├──hard──► lib-state (L0)        # Room data, messages, type definitions
    ├──hard──► lib-messaging (L0)    # Service events, real-time delivery
    ├──hard──► lib-mesh (L0)         # Service-to-service calls
    ├──hard──► lib-connect (L1)      # IClientEventPublisher for WebSocket push
    ├──hard──► lib-auth (L1)         # Session validation
    ├──hard──► lib-permission (L1)   # chat:in_room state for messaging
    ├──hard──► lib-contract (L1)     # Contract-governed room lifecycle
    └──hard──► lib-resource (L1)     # Room archival

    NO dependencies on L2, L3, or L4
```

### Who Calls Chat

```
                              lib-chat (L1)
                                    ▲
                                    │
                ┌───────────────────┼───────────────────────┐
                │                   │                       │
        lib-connect (L1)     lib-streaming (L4)      Any L2-L5 service
        Auto-creates P2P     Creates game chat       that needs message
        companion rooms       rooms, NPC boards       channels
                              sentiment channels
```

### Deployment Modes

```bash
# Minimal cloud service: chat always available (L1)
BANNOU_ENABLE_APP_FOUNDATION=true
# Result: account, auth, connect, permission, contract, resource, chat

# Game deployment: game services use chat for message boards, NPC comms
BANNOU_ENABLE_APP_FOUNDATION=true
BANNOU_ENABLE_GAME_FOUNDATION=true
# Result: L1 + realm, character, etc. -- all can create chat rooms

# Full game: streaming metagame creates sentiment chat rooms
BANNOU_ENABLE_APP_FOUNDATION=true
BANNOU_ENABLE_GAME_FOUNDATION=true
BANNOU_ENABLE_GAME_FEATURES=true
# Result: lib-streaming uses sentiment rooms for audience reaction channels
```

---

## Room Type Registry

Room types are a dynamic registry, not a fixed enum. This follows the seed type pattern -- string codes allow new types without schema changes.

### Built-In Types (Pre-Registered on Startup)

| Code | MessageFormat | Description |
|------|--------------|-------------|
| `text` | Text | Traditional text messages. Default for P2P companion rooms. |
| `sentiment` | Sentiment | Sentiment entries (SentimentCategory + intensity). No text. Privacy-safe by design. |
| `emoji` | Emoji | Emoji codes only (Unicode or game-defined). Lightweight reactions. |

### Custom Types (Registered via API)

Higher-layer services register custom room types with domain-specific validation:

```
# Examples of custom types a game might register:
"guild_board"     → Text, persistent, max 200 chars, contract-governed by guild charter
"trade_posting"   → Custom, persistent, required fields: [itemCode, price, currency]
"crowd_reaction"  → Sentiment, ephemeral, allow anonymous senders
"npc_comms"       → Text, ephemeral, max 500 chars, system senders only
"arena_emotes"    → Emoji, ephemeral, allowedValues: [cheer, boo, gasp, laugh, rage]
```

### Room Type Definition Model

```yaml
ChatRoomTypeDefinition:
  code: string                    # Unique type code
  displayName: string             # Human-readable name
  description: string?            # Optional
  gameServiceId: Guid?            # Scoped to game service (null = global)
  messageFormat: MessageFormat    # Text, Sentiment, Emoji, Custom
  validatorConfig: ValidatorConfig? # Validation rules (null = format defaults)
  persistenceMode: PersistenceMode  # Ephemeral (Redis) or Persistent (MySQL)
  defaultMaxParticipants: int?    # null = use service default
  retentionDays: int?             # null = use service default
  defaultContractTemplateId: Guid? # Default contract template for rooms of this type
  allowAnonymousSenders: bool     # Whether null senderId is allowed
  rateLimitPerMinute: int?        # null = use service default
  metadata: JsonElement?          # Arbitrary client rendering metadata
  status: RoomTypeStatus          # Active, Deprecated
  createdAt: DateTimeOffset
  updatedAt: DateTimeOffset?

ValidatorConfig:
  maxMessageLength: int?          # Maximum message length (text/custom)
  allowedPattern: string?         # Regex pattern for content validation
  allowedValues: string[]?        # Whitelist of allowed values (emoji codes, etc.)
  requiredFields: string[]?       # Required JSON fields (Custom format)
  jsonSchema: string?             # Full JSON Schema string (Custom format, complex validation)
```

---

## Shared Sentiment Primitives

The `SentimentCategory` enum moves to `common-api.yaml` as a system-wide shared type. This enables both lib-chat (L1) and lib-stream (L3, per STREAMING-ARCHITECTURE.md) to use the same sentiment vocabulary without schema coupling.

```yaml
# Addition to common-api.yaml components/schemas
SentimentCategory:
  type: string
  description: >
    Standardized sentiment categories for anonymous audience and reaction data.
    Used by lib-chat for sentiment room messages and lib-stream for platform
    audience processing. Designed for privacy-safe communication where text
    content is inappropriate or unnecessary.
  enum:
    - Excited       # High-energy positive (hype, celebration)
    - Supportive    # Calm positive (encouragement, constructive)
    - Critical      # Negative feedback (complaints, dissatisfaction)
    - Curious       # Engagement without clear valence (questions)
    - Surprised     # Unexpected reaction (plot twists, discoveries)
    - Amused        # Entertainment response (jokes, funny moments)
    - Bored         # Low engagement signals (AFK, minimal interaction)
    - Hostile       # Aggressive negativity (toxicity signals)
```

STREAMING-ARCHITECTURE.md's `SentimentPulse` and `SentimentEntry` schemas should `$ref` this common type once it exists, replacing their inline definitions.

---

## Contract-Backed Rooms

Rooms optionally reference a governing Contract. This creates a powerful lifecycle model where the Contract FSM drives room behavior.

### How It Works

1. **Room creation**: Creator optionally provides a `contractId`. Chat validates the contract exists and is active via `IContractClient`.
2. **Contract governs lifecycle**: Chat subscribes to contract lifecycle events. When the governing contract changes state, Chat executes the configured action on the room.
3. **Prebound API moderation**: The contract's milestones/terms can include prebound APIs targeting chat endpoints (`/chat/room/participant/kick`, `/chat/message/send`, `/chat/room/delete`). The contract FSM triggers these automatically.
4. **Room-level overrides**: Each room can override the default contract actions via `contractBreachAction` and `contractCompletionAction` fields (set at room creation, defaulting to service config).

### Contract State → Room Action

| Contract State | Default Room Action | Configurable? |
|---------------|---------------------|---------------|
| Completed | Archive | Yes (Continue, Lock, Archive, Delete) |
| Breached | Lock | Yes (Continue, Lock, Archive, Delete) |
| Expired | Archive | Yes (Continue, Lock, Archive, Delete) |
| Cancelled | Delete | Yes (Continue, Lock, Archive, Delete) |

### Example: Guild Board Governed by Guild Charter

```
1. Guild creates a Contract (guild charter) with parties = guild members
2. Guild creates a Chat room: type="guild_board", contractId=charterContractId
3. Charter has a "member expelled" clause with prebound API:
   → POST /chat/room/participant/kick { roomId, sessionId }
4. Charter breach (guild disbanded):
   → Chat receives contract.breached event
   → Room locked (default breach action)
   → System message sent: "Guild disbanded. Board locked."
5. Charter completed (guild objective met):
   → Chat receives contract.completed event
   → Room archived via Resource
```

---

## Connect Integration

Connect (L1) creates companion text chat rooms for P2P sessions automatically.

### Flow

```
Client connects via WebSocket
    │
    ▼
Connect establishes session (existing flow)
    │
    ▼
Connect calls IChatClient.CreateRoomAsync
    type: "text", sessionId: connectSessionId
    │
    ▼
Chat creates ephemeral room, returns roomId
    │
    ▼
Connect stores roomId in session metadata
    │
    ▼
Connect includes chatRoomId in session info / capability manifest
    │
    ▼
Client disconnects → Connect calls IChatClient.DeleteRoomAsync
```

This means every connected client has a chat channel available by default. Higher layers can create additional rooms for specific purposes, but the P2P companion room exists as a baseline.

---

## Message Model

Messages are polymorphic by room type. The base fields are shared; the content varies.

### Base Message Fields

```yaml
ChatMessage:
  messageId: Guid
  roomId: Guid
  senderType: string?          # Opaque: "session", "character", "system", "anonymous", etc.
  senderId: Guid?              # Entity ID (nullable for anonymous/system)
  displayName: string?         # Human-readable sender name
  timestamp: DateTimeOffset
  roomTypeCode: string         # Which room type this message belongs to
  isPinned: bool
```

### Content by Room Type

| Room Type | Content Shape | Example |
|-----------|--------------|---------|
| Text | `{ text: string }` | `{ text: "Hello world" }` |
| Sentiment | `{ category: SentimentCategory, intensity: float }` | `{ category: "Excited", intensity: 0.85 }` |
| Emoji | `{ emojiCode: string, emojiSetId: Guid? }` | `{ emojiCode: "cheer", emojiSetId: null }` |
| Custom | `{ payload: JsonElement }` | `{ payload: { itemCode: "sword_01", price: 500 } }` |

### API Content Model (Discriminated Union)

```yaml
SendMessageContent:
  type: object
  description: >
    Message content discriminated by the room's message format.
    Exactly one field must be set, matching the room type's format.
  properties:
    text:
      type: string
      nullable: true
      description: Text content (for Text format rooms)
    sentimentCategory:
      $ref: 'common-api.yaml#/components/schemas/SentimentCategory'
      nullable: true
      description: Sentiment category (for Sentiment format rooms)
    sentimentIntensity:
      type: number
      format: float
      minimum: 0.0
      maximum: 1.0
      nullable: true
      description: Sentiment intensity 0.0-1.0 (for Sentiment format rooms)
    emojiCode:
      type: string
      nullable: true
      description: Emoji code - Unicode or custom (for Emoji format rooms)
    emojiSetId:
      type: string
      format: uuid
      nullable: true
      description: Custom emoji set reference (null = Unicode)
    customPayload:
      type: string
      nullable: true
      description: JSON string validated against room type's validator config (for Custom format rooms)
```

The service validates that exactly one content field group is set and matches the room's `messageFormat`.

---

## Resource Integration (Archival)

Rooms are archived via lib-resource when explicitly requested or when a governing contract triggers archival.

### Archive Flow

```
/chat/room/archive called (or contract triggers Archive action)
    │
    ▼
Chat calls IResourceClient to create archive
    │
    ├── Room metadata (type, participants, contract, timestamps)
    ├── Message history (if persistent room)
    └── Participant history (join/leave/kick/ban records)
    │
    ▼
Room marked as archived (isArchived = true)
    │
    ▼
Messages and participant data cleaned up after archive confirmed
```

### Idle Room Auto-Cleanup

A background worker periodically scans for idle rooms (no messages within `IdleRoomTimeoutMinutes`):
- **Ephemeral rooms**: Deleted directly (no archive needed -- data is already TTL-managed)
- **Persistent rooms without contracts**: Archived via Resource, then deleted
- **Contract-governed rooms**: Skipped (contract FSM governs lifecycle)

---

## Implementation Steps

### Step 1: Update Shared Schema (common-api.yaml)

Add `SentimentCategory` to `schemas/common-api.yaml` under `components/schemas`:

```yaml
SentimentCategory:
  type: string
  description: >
    Standardized sentiment categories for anonymous audience and reaction data.
    Used by lib-chat for sentiment room messages and lib-stream for platform
    audience processing.
  enum:
    - Excited
    - Supportive
    - Critical
    - Curious
    - Surprised
    - Amused
    - Bored
    - Hostile
```

This is additive -- no existing schemas are affected.

### Step 2: Create Schema Files

#### 2a. `schemas/chat-api.yaml`

Header: `x-service-layer: AppFoundation`, `servers: [{ url: http://localhost:5012 }]`

**Enums** (in `components/schemas`):

```yaml
MessageFormat:
  type: string
  enum: [Text, Sentiment, Emoji, Custom]
  description: Determines what kind of content a room accepts

PersistenceMode:
  type: string
  enum: [Ephemeral, Persistent]
  description: Whether room messages are stored in Redis (TTL) or MySQL (durable)

RoomTypeStatus:
  type: string
  enum: [Active, Deprecated]
  description: Lifecycle status of a room type definition

ChatRoomStatus:
  type: string
  enum: [Active, Locked, Archived]
  description: Current state of a chat room

ChatParticipantRole:
  type: string
  enum: [Owner, Moderator, Member, ReadOnly]
  description: Participant's role within a chat room

ContractRoomAction:
  type: string
  enum: [Continue, Lock, Archive, Delete]
  description: Action to take on a contract-governed room when contract state changes
```

**28 POST endpoints** across 5 groups:

Room Type Management (5):
- `/chat/type/register` - Register a new room type [developer]
- `/chat/type/get` - Get room type by code [developer]
- `/chat/type/list` - List room types with filters [developer]
- `/chat/type/update` - Update a room type definition [developer]
- `/chat/type/deprecate` - Soft-deprecate a room type [developer]

Room Management (6):
- `/chat/room/create` - Create a room [user]
- `/chat/room/get` - Get room by ID [user]
- `/chat/room/list` - List rooms with filters [user]
- `/chat/room/update` - Update room settings [user, chat:in_room]
- `/chat/room/delete` - Delete a room [user, chat:in_room]
- `/chat/room/archive` - Archive a room to Resource [user, chat:in_room]

Participant Management (7):
- `/chat/room/join` - Join a room [user]
- `/chat/room/leave` - Leave a room [user, chat:in_room]
- `/chat/room/participants` - List participants [user, chat:in_room]
- `/chat/room/participant/kick` - Remove a participant [user, chat:in_room]
- `/chat/room/participant/ban` - Ban a participant [user, chat:in_room]
- `/chat/room/participant/unban` - Unban a participant [user, chat:in_room]
- `/chat/room/participant/mute` - Mute a participant [user, chat:in_room]

Message Operations (7):
- `/chat/message/send` - Send a message [user, chat:in_room]
- `/chat/message/send-batch` - Send multiple messages [developer]
- `/chat/message/history` - Get message history paginated [user, chat:in_room]
- `/chat/message/delete` - Delete a specific message [user, chat:in_room]
- `/chat/message/pin` - Pin a message [user, chat:in_room]
- `/chat/message/unpin` - Unpin a message [user, chat:in_room]
- `/chat/message/search` - Full-text search in persistent rooms [user, chat:in_room]

Admin/Debug (3):
- `/chat/admin/rooms` - List all rooms system-wide [developer]
- `/chat/admin/stats` - Room/message statistics [developer]
- `/chat/admin/cleanup` - Force cleanup of idle rooms [developer]

**Key request/response models** (all properties must have `description` fields, NRT compliant):

- `RegisterRoomTypeRequest`: code, displayName, gameServiceId?, messageFormat, validatorConfig?, persistenceMode, defaultMaxParticipants?, retentionDays?, defaultContractTemplateId?, allowAnonymousSenders, rateLimitPerMinute?, metadata?
- `RoomTypeResponse`: all definition fields
- `ListRoomTypesRequest`: gameServiceId?, messageFormat?, status?, page, pageSize
- `ListRoomTypesResponse`: items[], totalCount, page, pageSize
- `CreateRoomRequest`: roomTypeCode, sessionId? (Connect session), contractId?, displayName?, maxParticipants?, contractBreachAction?, contractCompletionAction?, metadata?
- `ChatRoomResponse`: roomId, roomTypeCode, sessionId?, contractId?, displayName, status, participantCount, maxParticipants, isArchived, createdAt, metadata?
- `ListRoomsRequest`: roomTypeCode?, sessionId?, status?, page, pageSize
- `JoinRoomRequest`: roomId, senderType?, senderId?, displayName?, role? (defaults to Member)
- `LeaveRoomRequest`: roomId
- `ParticipantsResponse`: participants[] (sessionId, senderType, senderId, displayName, role, joinedAt, isMuted)
- `KickParticipantRequest`: roomId, targetSessionId, reason?
- `BanParticipantRequest`: roomId, targetSessionId, reason?, durationMinutes? (null = permanent)
- `MuteParticipantRequest`: roomId, targetSessionId, durationMinutes? (null = permanent)
- `SendMessageRequest`: roomId, content (SendMessageContent), senderType?, senderId?, displayName?
- `SendMessageBatchRequest`: roomId, messages[] (content + sender info)
- `ChatMessageResponse`: messageId, roomId, senderType?, senderId?, displayName?, timestamp, content, isPinned
- `MessageHistoryRequest`: roomId, before? (cursor), limit (default 50)
- `MessageHistoryResponse`: messages[], hasMore, nextCursor?
- `SearchMessagesRequest`: roomId, query, limit (default 20)
- `SearchMessagesResponse`: messages[], totalMatches

#### 2b. `schemas/chat-events.yaml`

**x-lifecycle** for `ChatRoom` entity:
- Model fields: roomId (primary), roomTypeCode, sessionId?, contractId?, displayName, status, participantCount, maxParticipants, isArchived, createdAt
- Sensitive: metadata (exclude from lifecycle events)

**x-lifecycle** for `ChatRoomType` entity:
- Model fields: code (primary), displayName, gameServiceId?, messageFormat, persistenceMode, allowAnonymousSenders, status, createdAt
- Sensitive: validatorConfig, metadata (exclude)

**x-event-subscriptions**:
- `contract.completed` → `ContractCompletedEvent` → HandleContractCompleted
- `contract.breached` → `ContractBreachedEvent` → HandleContractBreached

**x-event-publications** (lifecycle + custom):
- Lifecycle events (auto-generated): chat-room.created, chat-room.updated, chat-room.deleted, chat-room-type.created, chat-room-type.updated, chat-room-type.deleted
- `chat.participant.joined` → ChatParticipantJoinedEvent
- `chat.participant.left` → ChatParticipantLeftEvent
- `chat.participant.kicked` → ChatParticipantKickedEvent
- `chat.participant.banned` → ChatParticipantBannedEvent
- `chat.participant.muted` → ChatParticipantMutedEvent
- `chat.participant.unmuted` → ChatParticipantUnmutedEvent
- `chat.message.sent` → ChatMessageSentEvent (metadata only -- NO text content for privacy)
- `chat.message.deleted` → ChatMessageDeletedEvent
- `chat.room.locked` → ChatRoomLockedEvent (contract-triggered)
- `chat.room.archived` → ChatRoomArchivedEvent

**Custom event schemas** (in `components/schemas`):

ChatParticipantJoinedEvent: eventId, timestamp, roomId, roomTypeCode, participantSessionId, senderType?, senderId?, displayName?, role, currentCount

ChatParticipantLeftEvent: eventId, timestamp, roomId, participantSessionId, remainingCount

ChatParticipantKickedEvent: eventId, timestamp, roomId, targetSessionId, kickedBySessionId, reason?

ChatParticipantBannedEvent: eventId, timestamp, roomId, targetSessionId, bannedBySessionId, reason?, durationMinutes?

ChatParticipantMutedEvent: eventId, timestamp, roomId, targetSessionId, mutedBySessionId, durationMinutes?

ChatParticipantUnmutedEvent: eventId, timestamp, roomId, targetSessionId, unmutedBySessionId

ChatMessageSentEvent: eventId, timestamp, roomId, roomTypeCode, messageId, senderType?, senderId?, messageFormat (NO content field for text/custom -- only sentimentCategory + intensity for sentiment rooms, emojiCode for emoji rooms)

ChatMessageDeletedEvent: eventId, timestamp, roomId, messageId, deletedBySessionId

ChatRoomLockedEvent: eventId, timestamp, roomId, reason (ContractBreached, ContractExpired, Manual)

ChatRoomArchivedEvent: eventId, timestamp, roomId, archiveId (from Resource)

#### 2c. `schemas/chat-client-events.yaml`

Client events pushed via IClientEventPublisher to room participants:

- `ChatMessageReceivedEvent` - New message in room (INCLUDES content -- client needs it for rendering)
- `ChatMessageDeletedClientEvent` - Message deleted
- `ChatMessagePinnedEvent` - Message pinned/unpinned
- `ChatParticipantJoinedClientEvent` - Someone joined the room
- `ChatParticipantLeftClientEvent` - Someone left the room
- `ChatParticipantKickedClientEvent` - Someone was kicked (sent to all participants)
- `ChatParticipantBannedClientEvent` - Someone was banned
- `ChatParticipantMutedClientEvent` - Someone was muted
- `ChatParticipantUnmutedClientEvent` - Someone was unmuted
- `ChatRoomLockedClientEvent` - Room locked (contract action)
- `ChatRoomDeletedClientEvent` - Room is being deleted

All extend `BaseClientEvent` with `eventName` field (e.g., `chat.message_received`).

#### 2d. `schemas/chat-configuration.yaml`

```yaml
x-service-configuration:
  properties:
    ChatEnabled:
      type: boolean
      env: CHAT_ENABLED
      default: true
      description: Master enable flag for the chat service

    DefaultRetentionDays:
      type: integer
      env: CHAT_DEFAULT_RETENTION_DAYS
      default: 30
      minimum: 1
      maximum: 3650
      description: Default message retention for persistent rooms when room type does not specify

    MaxRoomTypesPerGameService:
      type: integer
      env: CHAT_MAX_ROOM_TYPES_PER_GAME_SERVICE
      default: 50
      minimum: 1
      maximum: 500
      description: Maximum custom room types per game service

    DefaultMaxParticipantsPerRoom:
      type: integer
      env: CHAT_DEFAULT_MAX_PARTICIPANTS_PER_ROOM
      default: 100
      minimum: 1
      maximum: 10000
      description: Default participant limit when room type does not specify

    DefaultRateLimitPerMinute:
      type: integer
      env: CHAT_DEFAULT_RATE_LIMIT_PER_MINUTE
      default: 60
      minimum: 1
      maximum: 600
      description: Default messages per minute per participant when room type does not specify

    IdleRoomCleanupIntervalMinutes:
      type: integer
      env: CHAT_IDLE_ROOM_CLEANUP_INTERVAL_MINUTES
      default: 60
      minimum: 5
      maximum: 1440
      description: How often the background worker checks for idle rooms

    IdleRoomTimeoutMinutes:
      type: integer
      env: CHAT_IDLE_ROOM_TIMEOUT_MINUTES
      default: 1440
      minimum: 10
      maximum: 43200
      description: Minutes of inactivity before a room is eligible for auto-cleanup

    EphemeralMessageTtlMinutes:
      type: integer
      env: CHAT_EPHEMERAL_MESSAGE_TTL_MINUTES
      default: 60
      minimum: 5
      maximum: 1440
      description: TTL for messages in ephemeral (Redis) rooms

    MaxPinnedMessagesPerRoom:
      type: integer
      env: CHAT_MAX_PINNED_MESSAGES_PER_ROOM
      default: 10
      minimum: 0
      maximum: 100
      description: Maximum pinned messages per room

    DefaultContractBreachAction:
      $ref: 'chat-api.yaml#/components/schemas/ContractRoomAction'
      env: CHAT_DEFAULT_CONTRACT_BREACH_ACTION
      default: Lock
      description: Default action when governing contract is breached

    DefaultContractCompletionAction:
      $ref: 'chat-api.yaml#/components/schemas/ContractRoomAction'
      env: CHAT_DEFAULT_CONTRACT_COMPLETION_ACTION
      default: Archive
      description: Default action when governing contract completes

    AutoCreateP2pCompanionRooms:
      type: boolean
      env: CHAT_AUTO_CREATE_P2P_COMPANION_ROOMS
      default: true
      description: Whether Connect automatically creates text rooms for P2P sessions

    MessageHistoryPageSize:
      type: integer
      env: CHAT_MESSAGE_HISTORY_PAGE_SIZE
      default: 50
      minimum: 10
      maximum: 200
      description: Default page size for message history queries
```

#### 2e. Update `schemas/state-stores.yaml`

Add under `x-state-stores:`:

```yaml
chat-room-types:
  backend: mysql
  service: Chat
  purpose: Room type definitions (durable, queryable by code/gameServiceId)

chat-rooms:
  backend: mysql
  service: Chat
  purpose: Chat room records (durable, queryable by type/session/status)

chat-rooms-cache:
  backend: redis
  prefix: "chat:room"
  service: Chat
  purpose: Active room state cache (participant lists, room metadata)

chat-messages:
  backend: mysql
  service: Chat
  purpose: Persistent message history for durable rooms

chat-messages-ephemeral:
  backend: redis
  prefix: "chat:msg"
  service: Chat
  purpose: Ephemeral message buffer for non-persistent rooms (TTL-based)

chat-participants:
  backend: redis
  prefix: "chat:part"
  service: Chat
  purpose: Active participant tracking (room membership, mute state, last activity)

chat-bans:
  backend: mysql
  service: Chat
  purpose: Ban records (durable, queryable by roomId/participant)

chat-lock:
  backend: redis
  prefix: "chat:lock"
  service: Chat
  purpose: Distributed locks for room and participant modifications
```

### Step 3: Generate Service

```bash
cd scripts && ./generate-service.sh chat
```

This bootstraps the entire plugin (see SCHEMA-RULES.md "New Service Bootstrap"):
- `plugins/lib-chat/` directory + csproj + AssemblyInfo
- `plugins/lib-chat/Generated/` (controller, interface, config, permissions, events controller)
- `bannou-service/Generated/` (models, client, event models, updated StateStoreDefinitions)
- Template files: ChatService.cs, ChatServiceModels.cs, ChatServicePlugin.cs
- `plugins/lib-chat.tests/` test project

**Build check**: `dotnet build` to verify generation succeeded.

### Step 4: Fill In Plugin Registration

#### 4a. `plugins/lib-chat/ChatServicePlugin.cs` (generated template -> fill in)

Extends `BaseBannouPlugin`:
- `PluginName => "chat"`, `DisplayName => "Chat Service"`
- ConfigureServices: register `IdleRoomCleanupWorker` as hosted service
- OnRunningAsync: register built-in room types ("text", "sentiment", "emoji") if not already present

### Step 5: Fill In Internal Models

#### 5a. `plugins/lib-chat/ChatServiceModels.cs` (generated template -> fill in)

Internal storage models (not API-facing):

- **`ChatRoomTypeModel`**: code (string), displayName (string), description (string?), gameServiceId (Guid?), messageFormat (MessageFormat), validatorConfig (ValidatorConfigModel?), persistenceMode (PersistenceMode), defaultMaxParticipants (int?), retentionDays (int?), defaultContractTemplateId (Guid?), allowAnonymousSenders (bool), rateLimitPerMinute (int?), metadata (Dictionary<string, object>?), status (RoomTypeStatus), createdAt (DateTimeOffset), updatedAt (DateTimeOffset?)

- **`ValidatorConfigModel`**: maxMessageLength (int?), allowedPattern (string?), allowedValues (List<string>?), requiredFields (List<string>?), jsonSchema (string?)

- **`ChatRoomModel`**: roomId (Guid), roomTypeCode (string), sessionId (Guid?), contractId (Guid?), displayName (string?), status (ChatRoomStatus), maxParticipants (int?), contractBreachAction (ContractRoomAction?), contractCompletionAction (ContractRoomAction?), isArchived (bool), metadata (Dictionary<string, object>?), createdAt (DateTimeOffset), lastActivityAt (DateTimeOffset)

- **`ChatParticipantModel`**: roomId (Guid), sessionId (Guid), senderType (string?), senderId (Guid?), displayName (string?), role (ChatParticipantRole), joinedAt (DateTimeOffset), lastActivityAt (DateTimeOffset), isMuted (bool), mutedUntil (DateTimeOffset?)

- **`ChatMessageModel`**: messageId (Guid), roomId (Guid), senderType (string?), senderId (Guid?), displayName (string?), timestamp (DateTimeOffset), messageFormat (MessageFormat), textContent (string?), sentimentCategory (SentimentCategory?), sentimentIntensity (float?), emojiCode (string?), emojiSetId (Guid?), customPayload (string?), isPinned (bool)

- **`ChatBanModel`**: banId (Guid), roomId (Guid), targetSessionId (Guid), bannedBySessionId (Guid), reason (string?), bannedAt (DateTimeOffset), expiresAt (DateTimeOffset?)

All models use proper types per IMPLEMENTATION TENETS (T25 enums, T26 nullable for optional).

### Step 6: Create Event Handlers

#### 6a. `plugins/lib-chat/ChatServiceEvents.cs` (manual - not auto-generated)

Partial class of ChatService:
- `RegisterEventConsumers(IEventConsumer eventConsumer)` - registers handlers for contract events
- `HandleContractCompletedAsync(ContractCompletedEvent evt)`:
  1. Query rooms with this contractId
  2. For each room: execute the room's `contractCompletionAction` (or service default)
  3. Lock → set status to Locked, publish ChatRoomLockedEvent + client event
  4. Archive → call archive flow, publish ChatRoomArchivedEvent
  5. Delete → call delete flow, publish lifecycle deleted event
  6. Continue → no-op
- `HandleContractBreachedAsync(ContractBreachedEvent evt)`:
  1. Same pattern but uses `contractBreachAction`

### Step 7: Implement Service Business Logic

#### 7a. `plugins/lib-chat/ChatService.cs` (generated template -> fill in)

Partial class with `[BannouService("chat", typeof(IChatService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.AppFoundation)]`:

**Constructor dependencies**:
- `IStateStoreFactory` - for all state stores
- `IMessageBus` - event publishing
- `IClientEventPublisher` - WebSocket push to participants
- `IDistributedLockProvider` - concurrent modification safety
- `ILogger<ChatService>` - structured logging
- `ChatServiceConfiguration` - typed config
- `IEventConsumer` - event handler registration
- `IContractClient` - validate contract exists on room creation
- `IResourceClient` - room archival
- `IPermissionClient` - set/clear chat:in_room state

**Store initialization** (in constructor):
- `_roomTypeStore` = GetJsonQueryableStore<ChatRoomTypeModel>(StateStoreDefinitions.ChatRoomTypes)
- `_roomStore` = GetJsonQueryableStore<ChatRoomModel>(StateStoreDefinitions.ChatRooms)
- `_roomCache` = GetStore<ChatRoomModel>(StateStoreDefinitions.ChatRoomsCache)
- `_messageStore` = GetJsonQueryableStore<ChatMessageModel>(StateStoreDefinitions.ChatMessages)
- `_messageBuffer` = GetStore<ChatMessageModel>(StateStoreDefinitions.ChatMessagesEphemeral)
- `_participantStore` = GetStore<ChatParticipantModel>(StateStoreDefinitions.ChatParticipants)
- `_banStore` = GetJsonQueryableStore<ChatBanModel>(StateStoreDefinitions.ChatBans)

**Key method implementations** (all follow T7 error handling, T8 return pattern):

| Method | Key Logic |
|--------|-----------|
| `RegisterRoomTypeAsync` | Validate code uniqueness (code + gameServiceId), check MaxRoomTypesPerGameService, validate messageFormat/validatorConfig compatibility, save, publish lifecycle event |
| `GetRoomTypeAsync` | Lookup by code + gameServiceId |
| `ListRoomTypesAsync` | JSON query with filters (gameServiceId, messageFormat, status), paged |
| `UpdateRoomTypeAsync` | Lock, load, update mutable fields, publish lifecycle event |
| `DeprecateRoomTypeAsync` | Set status to Deprecated, publish lifecycle event |
| `CreateRoomAsync` | Validate type exists and active, validate contract if provided (via IContractClient), create room, publish lifecycle event |
| `GetRoomAsync` | Lookup by ID (cache first, then MySQL), return 404 if not found |
| `ListRoomsAsync` | JSON query with filters (type, session, status), paged |
| `UpdateRoomAsync` | Lock, validate caller is Owner, update mutable fields, publish lifecycle event |
| `DeleteRoomAsync` | Lock, validate caller is Owner or room is empty, notify all participants (client event), cleanup participants/messages, publish lifecycle event |
| `ArchiveRoomAsync` | Validate room is persistent, call IResourceClient to create archive, mark isArchived, publish ChatRoomArchivedEvent |
| `JoinRoomAsync` | Lock room, validate not full/not banned, add participant, set chat:in_room permission state, publish event + client event |
| `LeaveRoomAsync` | Lock room, remove participant, clear permission state, publish event + client event. If Owner leaves, promote next Moderator or oldest Member. |
| `ListParticipantsAsync` | Read participant store for room |
| `KickParticipantAsync` | Validate caller is Owner or Moderator with higher role than target, remove participant, clear permission state, publish events |
| `BanParticipantAsync` | Validate caller role, kick if present, save ban record, publish events |
| `UnbanParticipantAsync` | Validate caller role, remove ban record, publish event |
| `MuteParticipantAsync` | Validate caller role, set isMuted + mutedUntil on participant, publish events |
| `SendMessageAsync` | Validate participant is in room and not muted, validate message content against room type, check rate limit, save message (ephemeral or persistent based on room type), publish service event + client event |
| `SendMessageBatchAsync` | Same as send but for multiple messages atomically (for bulk sentiment pushes from lib-stream) |
| `GetMessageHistoryAsync` | Query messages by roomId, cursor-based pagination, ordered by timestamp desc |
| `DeleteMessageAsync` | Validate caller is Owner/Moderator or message sender, remove message, publish events |
| `PinMessageAsync` | Validate caller is Owner/Moderator, check MaxPinnedMessagesPerRoom, set isPinned, publish client event |
| `UnpinMessageAsync` | Validate caller is Owner/Moderator, clear isPinned, publish client event |
| `SearchMessagesAsync` | Full-text search in persistent room messages (MySQL) |

**Content validation** (private helper):
1. Load room type definition
2. Match `messageFormat` against provided content fields
3. Text: validate maxMessageLength, allowedPattern from validatorConfig
4. Sentiment: validate category is valid SentimentCategory, intensity in [0.0, 1.0]
5. Emoji: validate emojiCode against allowedValues if set, or accept any string
6. Custom: validate against jsonSchema if set, validate requiredFields, validate allowedPattern
7. If validation fails, return BadRequest with specific error

**Rate limiting** (private helper):
1. Key: `chat:rate:{roomId}:{sessionId}`
2. Increment counter in Redis with TTL of 60 seconds
3. If counter exceeds room type's rateLimitPerMinute (or service default), return 429

**Permission state management**:
- On join: set `chat:in_room` state for the participant's session
- On leave/kick/ban: clear `chat:in_room` state
- This enables x-permissions to gate send/moderation endpoints to room participants only

**State key patterns**:
- Room type: `type:{gameServiceId}:{code}` (or `type:global:{code}` for unscoped)
- Room: `room:{roomId}`
- Room cache: `chat:room:{roomId}`
- Participants: `chat:part:{roomId}:{sessionId}`
- Messages (ephemeral): `chat:msg:{roomId}:{messageId}`
- Messages (persistent): `{roomId}:{messageId}` in MySQL
- Bans: `ban:{roomId}:{sessionId}`
- Rate limit: `chat:rate:{roomId}:{sessionId}`
- Lock: `chat:lock:{roomId}` or `chat:lock:type:{code}`

### Step 8: Background Worker

#### 8a. `plugins/lib-chat/Services/IdleRoomCleanupWorker.cs`

```csharp
/// <summary>
/// Background service that periodically checks for idle rooms
/// (no messages within the configured timeout) and cleans them up.
/// Ephemeral rooms are deleted. Persistent rooms are archived via Resource.
/// Contract-governed rooms are skipped (contract FSM governs lifecycle).
/// </summary>
public class IdleRoomCleanupWorker : BackgroundService
```

Every `IdleRoomCleanupIntervalMinutes`:
1. Query rooms with `lastActivityAt` older than `IdleRoomTimeoutMinutes`
2. Skip rooms with `contractId` (contract governs lifecycle)
3. Ephemeral rooms: delete directly (data is TTL-managed)
4. Persistent rooms: archive via Resource, then delete
5. Publish `chat-room.deleted` lifecycle event for each cleaned room

Register in `ChatServicePlugin.ConfigureServices`:
```csharp
services.AddHostedService<IdleRoomCleanupWorker>();
```

### Step 9: Build and Verify

```bash
dotnet build
```

Verify no compilation errors, all generated code resolves, no CS1591 warnings.

### Step 10: Unit Tests

The test project and template `ChatServiceTests.cs` were auto-created in Step 3. Fill in with comprehensive tests:

#### 10a. `plugins/lib-chat.tests/ChatServiceTests.cs` (generated template -> fill in)

**Constructor validation**:
- `ChatService_ConstructorIsValid()` via `ServiceConstructorValidator`

**Room type tests**:
- `RegisterRoomType_ValidRequest_SavesAndPublishesEvent`
- `RegisterRoomType_DuplicateCode_ReturnsConflict`
- `RegisterRoomType_ExceedsMaxPerGameService_ReturnsConflict`
- `RegisterRoomType_InvalidValidatorForFormat_ReturnsBadRequest`
- `ListRoomTypes_FiltersByGameServiceId`
- `DeprecateRoomType_SetsStatusToDeprecated`

**Room CRUD tests**:
- `CreateRoom_ValidRequest_SavesRoomAndPublishesEvent`
- `CreateRoom_WithContract_ValidatesContractExists`
- `CreateRoom_InvalidType_ReturnsNotFound`
- `CreateRoom_DeprecatedType_ReturnsBadRequest`
- `GetRoom_Exists_ReturnsRoom`
- `GetRoom_NotFound_ReturnsNotFound`
- `DeleteRoom_ByOwner_DeletesAndNotifiesParticipants`
- `ArchiveRoom_PersistentRoom_CreatesArchiveViaResource`

**Participant tests**:
- `JoinRoom_ValidRequest_AddsParticipantAndSetsPermissionState`
- `JoinRoom_RoomFull_ReturnsConflict`
- `JoinRoom_Banned_ReturnsForbidden`
- `LeaveRoom_RemovesParticipantAndClearsPermissionState`
- `LeaveRoom_OwnerLeaves_PromotesNextModerator`
- `KickParticipant_ByOwner_RemovesAndPublishesEvent`
- `KickParticipant_ByMember_ReturnsForbidden`
- `BanParticipant_SavesBanAndKicksIfPresent`
- `MuteParticipant_SetsIsMutedFlag`

**Message tests**:
- `SendMessage_TextRoom_ValidText_SavesAndPublishes`
- `SendMessage_TextRoom_ExceedsMaxLength_ReturnsBadRequest`
- `SendMessage_SentimentRoom_ValidSentiment_SavesAndPublishes`
- `SendMessage_SentimentRoom_TextContent_ReturnsBadRequest`
- `SendMessage_EmojiRoom_ValidEmoji_SavesAndPublishes`
- `SendMessage_EmojiRoom_TextContent_ReturnsBadRequest`
- `SendMessage_CustomRoom_PassesValidation_SavesAndPublishes`
- `SendMessage_CustomRoom_FailsValidation_ReturnsBadRequest`
- `SendMessage_MutedParticipant_ReturnsForbidden`
- `SendMessage_RateLimitExceeded_Returns429`
- `SendMessageBatch_MultipleMessages_SavesAll`
- `GetMessageHistory_ReturnsPagedResults`
- `PinMessage_ByModerator_SetsIsPinned`
- `PinMessage_ExceedsMax_ReturnsConflict`
- `DeleteMessage_BySender_DeletesMessage`
- `DeleteMessage_ByModerator_DeletesMessage`

**Contract integration tests**:
- `HandleContractCompleted_ArchiveAction_ArchivesRoom`
- `HandleContractCompleted_LockAction_LocksRoom`
- `HandleContractCompleted_DeleteAction_DeletesRoom`
- `HandleContractCompleted_ContinueAction_NoOp`
- `HandleContractBreached_DefaultAction_LocksRoom`

**Event handler tests**:
- `HandleContractCompleted_NoRoomsWithContract_NoOp`
- `HandleContractBreached_MultipleRooms_HandlesAll`

All tests use the capture pattern (Callback on mock setups) to verify saved state and published events.

### Step 11: Update Documentation

- Update `docs/reference/SERVICE-HIERARCHY.md`: Add `chat` to L1 AppFoundation table, update Quick Reference, update deployment modes examples
- Update `docs/planning/STREAMING-ARCHITECTURE.md`: Note that `SentimentCategory` now lives in `common-api.yaml` via `$ref`, lib-streaming can use sentiment chat rooms for audience reaction channels
- Create `docs/plugins/CHAT.md` deep dive document (follows template from other plugins)

---

## Files Created/Modified Summary

| File | Action |
|------|--------|
| `schemas/common-api.yaml` | Modify (add SentimentCategory enum) |
| `schemas/chat-api.yaml` | Create (28 endpoints, all models, 6 enums) |
| `schemas/chat-events.yaml` | Create (2 lifecycle entities, 2 subscriptions, 12+ custom events) |
| `schemas/chat-client-events.yaml` | Create (11 client push events) |
| `schemas/chat-configuration.yaml` | Create (13 config properties) |
| `schemas/state-stores.yaml` | Modify (add 8 chat stores) |
| `plugins/lib-chat/ChatService.cs` | Fill in (auto-generated template) |
| `plugins/lib-chat/ChatServiceModels.cs` | Fill in (auto-generated template) |
| `plugins/lib-chat/ChatServicePlugin.cs` | Fill in (auto-generated template) |
| `plugins/lib-chat/ChatServiceEvents.cs` | Create (NOT auto-generated) |
| `plugins/lib-chat/Services/IdleRoomCleanupWorker.cs` | Create (background worker) |
| `plugins/lib-chat.tests/ChatServiceTests.cs` | Fill in (auto-generated template) |
| `plugins/lib-chat/lib-chat.csproj` | Auto-generated by `generate-service.sh` |
| `plugins/lib-chat/AssemblyInfo.cs` | Auto-generated by `generate-service.sh` |
| `plugins/lib-chat/Generated/*` | Auto-generated (do not edit) |
| `bannou-service/Generated/*` | Auto-generated (updated) |
| `bannou-service.sln` | Auto-updated by `generate-service.sh` |
| `plugins/lib-chat.tests/*` | Auto-generated test project |
| `docs/reference/SERVICE-HIERARCHY.md` | Modify (add chat to L1) |
| `docs/planning/STREAMING-ARCHITECTURE.md` | Modify (note shared SentimentCategory) |
| `docs/plugins/CHAT.md` | Create (deep dive document) |

## Verification

1. `dotnet build` - compiles without errors or warnings
2. `dotnet test plugins/lib-chat.tests/` - all unit tests pass
3. Verify no CS1591 warnings (all schema properties have descriptions)
4. Verify StateStoreDefinitions.cs contains Chat store constants after generation
5. Verify ChatClient.cs generated in bannou-service for other services to use
6. Verify SentimentCategory in common-api.yaml is referenced (not duplicated) by chat schemas
7. Verify SERVICE-HIERARCHY.md lists chat in L1

---

## Open Design Questions

1. **Connect companion room timing**: Should Connect create the companion room on WebSocket upgrade (immediate) or lazily on first chat message? Immediate means every connection has a room; lazy reduces resource usage for clients that never chat. **Recommendation**: Lazy creation with `AutoCreateP2pCompanionRooms` config controlling the behavior. Connect stores a flag "chat available" in session metadata; the room is created on first `/chat/room/create` call with the session's ID. This avoids creating rooms for every health check or short-lived connection.

2. **Contract event granularity**: The plan subscribes to `contract.completed` and `contract.breached`. Contract may also publish `contract.expired`, `contract.cancelled`, or other terminal state events. Need to verify Contract's actual event catalog and subscribe to all terminal states. **Recommendation**: Subscribe to all terminal contract states; treat them all as configurable actions.

3. **Message search in ephemeral rooms**: The plan includes `/chat/message/search` but only for persistent rooms (MySQL full-text). Should ephemeral rooms support search? **Recommendation**: No. Ephemeral rooms are TTL-managed Redis data -- search is not a primitive Redis supports well, and ephemeral rooms are meant to be transient. If search is needed, use a persistent room.

4. **Cross-room messaging**: Should a sender be able to post to multiple rooms simultaneously (broadcast)? **Recommendation**: Not in v1. Callers that need broadcast (like lib-stream pushing sentiments) can use `send-batch` per room or publish events that Chat consumes. Add cross-room broadcast as a v2 feature if needed.

5. **Room type inheritance**: Should custom room types be able to "extend" built-in types (e.g., "guild_board extends text with maxLength=200")? **Recommendation**: Not explicitly. Custom types set their own `messageFormat` (which determines validation base) and `validatorConfig` (which adds constraints). This achieves the same result without a formal inheritance mechanism.

6. **Participant identity stability**: When a participant's session reconnects (new sessionId via Connect), how do they rejoin their rooms? **Recommendation**: Higher layers handle this. Connect publishes reconnection events; the orchestrating service (lib-streaming, or the game service) calls `/chat/room/join` with the new sessionId. Chat doesn't need to understand reconnection semantics.

---

*This document is self-contained for schema generation. All model shapes, event schemas, configuration properties, state stores, and API endpoint signatures are specified at sufficient detail to produce YAML schemas without referencing external documentation.*
