# Game Session Plugin Deep Dive

> **Plugin**: lib-game-session
> **Schema**: schemas/game-session-api.yaml
> **Version**: 2.0.0
> **State Store**: game-session-statestore (MySQL)

---

## Overview

Hybrid lobby/matchmade game session management with subscription-driven shortcut publishing, voice integration, and real-time chat. Manages two session types: **lobby** sessions (persistent, per-game-service entry points auto-created for subscribed accounts) and **matchmade** sessions (pre-created by matchmaking with reservation tokens and TTL-based expiry). Integrates with Permission service for `in_game` state tracking, Voice service for room lifecycle, and Subscription service for account eligibility. Features distributed subscriber session tracking via ETag-based optimistic concurrency, and publishes WebSocket shortcuts to connected clients enabling one-click game join.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL persistence for sessions, lobbies, session lists, subscriber sessions |
| lib-state (`IDistributedLockProvider`) | Session-level locks for concurrent join/leave/kick operations |
| lib-messaging (`IMessageBus`) | Publishing lifecycle and player events; error event publishing |
| lib-messaging (`IEventConsumer`) | 4 event subscriptions (session lifecycle, subscription changes) |
| lib-messaging (`IClientEventPublisher`) | Push shortcuts, chat messages, and cancellation notices to WebSocket sessions |
| lib-voice (`IVoiceClient`) | Voice room create/join/leave/delete for sessions |
| lib-permission (`IPermissionClient`) | Set/clear `game-session:in_game` state on join/leave |
| lib-subscription (`ISubscriptionClient`) | Query account subscriptions for shortcut eligibility |

> **Refactoring Consideration**: This plugin injects 3 service clients individually. Consider whether `IServiceNavigator` would reduce constructor complexity, trading explicit dependencies for cleaner signatures. Currently favoring explicit injection for dependency clarity.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-matchmaking | Creates matchmade sessions with reservations; calls `PublishJoinShortcutAsync` |
| lib-analytics | References `IGameSessionClient` for session context |

---

## State Storage

**Store**: `game-session-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `session:{sessionId}` | `GameSessionModel` | Individual session data (players, status, settings, reservations) |
| `session-list` | `List<string>` | All session IDs (global index for listing/cleanup) |
| `lobby:{stubName}` | `GameSessionModel` | Per-game-service lobby session (duplicate of session: entry) |
| `subscriber-sessions:{accountId}` | `SubscriberSessionsModel` | Set of WebSocket session IDs for a subscribed account |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `game-session.created` | `GameSessionCreatedEvent` | New session created (lobby or matchmade) |
| `game-session.updated` | `GameSessionUpdatedEvent` | Session state changes |
| `game-session.deleted` | `GameSessionDeletedEvent` | Session removed |
| `game-session.player-joined` | `GameSessionPlayerJoinedEvent` | Player joins a session |
| `game-session.player-left` | `GameSessionPlayerLeftEvent` | Player leaves or is kicked (includes `Kicked` flag) |
| `game-session.action.performed` | `GameSessionActionPerformedEvent` | Game action executed in session |
| `game-session.session-cancelled` | `SessionCancelledServerEvent` | Matchmade session cancelled due to expired reservations |
| `permission.session-state-changed` | `SessionStateChangeEvent` | Player enters/exits game session (triggers permission recompilation) |

### Consumed Events

| Topic | Event Type | Handler |
|-------|-----------|---------|
| `session.connected` | `SessionConnectedEvent` | Tracks session, publishes join shortcuts for subscribed accounts |
| `session.disconnected` | `SessionDisconnectedEvent` | Removes session from subscriber tracking |
| `session.reconnected` | `SessionReconnectedEvent` | Re-publishes shortcuts (treated same as new connection) |
| `subscription.updated` | `SubscriptionUpdatedEvent` | Updates subscription cache, publishes/revokes shortcuts |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `ServerSalt` | `GAME_SESSION_SERVER_SALT` | dev salt | Shared salt for GUID generation (required, fail-fast) |
| `MaxPlayersPerSession` | `GAME_SESSION_MAX_PLAYERS_PER_SESSION` | `16` | Hard cap on players per session |
| `DefaultSessionTimeoutSeconds` | `GAME_SESSION_DEFAULT_SESSION_TIMEOUT_SECONDS` | `0` | Session TTL (0 = no expiry) |
| `DefaultReservationTtlSeconds` | `GAME_SESSION_DEFAULT_RESERVATION_TTL_SECONDS` | `60` | Default TTL for matchmade reservations |
| `DefaultLobbyMaxPlayers` | `GAME_SESSION_DEFAULT_LOBBY_MAX_PLAYERS` | `100` | Max players for auto-created lobbies |
| `CleanupIntervalSeconds` | `GAME_SESSION_CLEANUP_INTERVAL_SECONDS` | `30` | Interval between reservation cleanup cycles |
| `CleanupServiceStartupDelaySeconds` | `GAME_SESSION_CLEANUP_SERVICE_STARTUP_DELAY_SECONDS` | `10` | Delay before cleanup service starts |
| `StartupServiceDelaySeconds` | `GAME_SESSION_STARTUP_SERVICE_DELAY_SECONDS` | `2` | Delay before subscription cache warmup |
| `SubscriberSessionRetryMaxAttempts` | `GAME_SESSION_SUBSCRIBER_SESSION_RETRY_MAX_ATTEMPTS` | `3` | Max retries for ETag-based optimistic concurrency |
| `SupportedGameServices` | `GAME_SESSION_SUPPORTED_GAME_SERVICES` | `arcadia,generic` | Comma-separated game service stub names |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<GameSessionService>` | Scoped | Structured logging |
| `GameSessionServiceConfiguration` | Singleton | All 10 config properties |
| `IStateStoreFactory` | Singleton | MySQL state store access |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IEventConsumer` | Scoped | Event handler registration |
| `IClientEventPublisher` | Scoped | Shortcut/chat/cancellation push to WebSocket sessions |
| `IVoiceClient` | Scoped | Voice room lifecycle |
| `IPermissionClient` | Scoped | Permission state management |
| `ISubscriptionClient` | Scoped | Account subscription queries |
| `IDistributedLockProvider` | Singleton | Session-level distributed locks |
| `GameSessionStartupService` | Hosted (BackgroundService) | Subscription cache warmup on startup |
| `ReservationCleanupService` | Hosted (BackgroundService) | Periodic expired reservation cleanup |

Service lifetime is **Scoped** (per-request) for GameSessionService. Two BackgroundServices for async operations.

---

## API Endpoints (Implementation Notes)

### Game Sessions (3 endpoints)

- **Create** (`/sessions/create`): Generates session ID. For matchmade: creates reservations with crypto-random tokens and expiry TTL. Creates voice room if voice service available (non-fatal on failure). Saves to state store with optional TTL. Publishes `game-session.created`. Enforces `MaxPlayersPerSession` cap.
- **Get** (`/sessions/get`): Simple load-by-ID with NotFound on miss.
- **List** (`/sessions/list`): Loads ALL session IDs from `session-list`, loads each session individually, filters out `Finished` status. In-memory pagination.

### Lobby Join/Leave (2 endpoints)

- **Join** (`/sessions/join`): Lobby-based join. Validates subscriber session via distributed state. Gets lobby by game type. Acquires distributed lock. Checks capacity/status/duplicate player. Sets `game-session:in_game` permission state (rolls back player on failure). Publishes `player-joined` event.
- **Leave** (`/sessions/leave`): Lobby-based leave. Acquires lock. Clears permission state (best-effort, continues on failure). Leaves voice room if applicable. Marks session `Finished` when empty (deletes voice room). Publishes `player-left` event.

### Direct Session Join/Leave (2 endpoints)

- **JoinById** (`/sessions/join-session`): For matchmade sessions. Validates reservation token and expiry. Marks reservation as claimed. Same lock/permission/status logic as lobby join.
- **LeaveById** (`/sessions/leave-session`): Direct leave by session ID. Same logic as lobby leave but without game type lookup.

### Admin (1 endpoint)

- **Kick** (`/sessions/kick`): Acquires lock. Removes player. Publishes `player-left` with `Kicked=true` and reason.

### Game Actions (1 endpoint)

- **Actions** (`/sessions/actions`): Validates lobby exists and isn't finished. Publishes `game-session.action.performed` event. Returns action ID with echoed data. Does not validate player membership (relies on permission state).

### Game Chat (1 endpoint)

- **Chat** (`/sessions/chat`): Builds `ChatMessageReceivedEvent`. For whispers: sends to target (with `IsWhisperToMe=true`) and sender only. For public: broadcasts to all player WebSocket sessions via `PublishToSessionsAsync`.

### Internal (1 endpoint)

- **PublishJoinShortcut** (`/sessions/publish-join-shortcut`): Called by matchmaking. Verifies session and reservation token. Generates route GUID and target GUID using server salt. Builds pre-bound `JoinGameSessionByIdRequest` as shortcut payload. Publishes `ShortcutPublishedEvent` to player's WebSocket session.

---

## Visual Aid

```
Session Types & Lifecycle
===========================

  LOBBY (persistent, per-game-service)
  ┌────────────────────────────────────────────────────────────┐
  │ Account subscribes to "arcadia"                            │
  │      │                                                     │
  │      ▼                                                     │
  │ session.connected event                                    │
  │      │                                                     │
  │      ├── Check _accountSubscriptions cache                 │
  │      │   (miss? → fetch from SubscriptionClient)           │
  │      │                                                     │
  │      ├── Store subscriber session (ETag optimistic retry)  │
  │      │                                                     │
  │      └── PublishJoinShortcutAsync                           │
  │           │                                                │
  │           ├── GetOrCreateLobbySessionAsync("arcadia")      │
  │           ├── Generate route GUID + target GUID            │
  │           └── IClientEventPublisher → ShortcutPublishedEvent│
  │                                                            │
  │ Client invokes shortcut → /sessions/join                   │
  │      │                                                     │
  │      ├── Validate subscriber session                       │
  │      ├── Acquire distributed lock                          │
  │      ├── Set permission state: in_game                     │
  │      └── Publish player-joined event                       │
  └────────────────────────────────────────────────────────────┘


  MATCHMADE (temporary, created by matchmaking)
  ┌────────────────────────────────────────────────────────────┐
  │ MatchmakingService creates session with reservations       │
  │      │                                                     │
  │      ├── POST /sessions/create (SessionType=Matchmade,     │
  │      │   ExpectedPlayers=[A, B, C])                        │
  │      │                                                     │
  │      └── POST /sessions/publish-join-shortcut (per player) │
  │           │                                                │
  │           └── ShortcutPublishedEvent → WebSocket           │
  │                                                            │
  │ Client invokes shortcut → /sessions/join-session           │
  │      │                                                     │
  │      ├── Validate reservation token                        │
  │      ├── Check reservation expiry                          │
  │      ├── Mark reservation as claimed                       │
  │      └── (same lock/permission/event flow as lobby)        │
  │                                                            │
  │ ReservationCleanupService (periodic background):           │
  │      │                                                     │
  │      ├── Find matchmade sessions past expiry               │
  │      ├── claimedCount < totalReservations?                 │
  │      │   └── Cancel session, notify players, delete state  │
  │      └── Publish game-session.session-cancelled            │
  └────────────────────────────────────────────────────────────┘


Subscription Cache Architecture
=================================

  Static ConcurrentDictionary<Guid, HashSet<string>>
  (AccountId → Set of subscribed stubNames)
       │
       ├── Warmed at startup by GameSessionStartupService
       │   (queries SubscriptionClient for all supported services)
       │
       ├── Updated on session.connected (cache miss → fetch)
       │
       └── Updated on subscription.updated events
           (add/remove stubNames based on action + isActive)

  Distributed Subscriber Sessions (lib-state with ETags):
       │
       ├── subscriber-sessions:{accountId} → SubscriberSessionsModel
       │   (Set of WebSocket session GUIDs for this account)
       │
       ├── Written on session.connected (optimistic retry)
       ├── Read on subscription.updated (find sessions to notify)
       └── Deleted on session.disconnected
```

---

## Stubs & Unimplemented Features

1. **Actions endpoint echoes data**: `PerformGameActionAsync` publishes an event and returns the action data back without any game-specific logic. Actual game state mutation would need to be implemented per game type.
2. **No player-in-session validation for actions**: The Actions endpoint checks lobby exists and isn't finished, but doesn't verify the requesting player is actually in the session (relies on permission-based access control instead).

---

## Potential Extensions

1. **Game state machine**: Add per-game-type state machines that validate and apply actions (turns, moves, scoring).
2. **Spectator mode**: Allow joining with a `Spectator` role that receives events but cannot perform actions.
3. **Session persistence/replay**: Store action history for replay or late-join state reconstruction.
4. **Cross-instance lobby sync**: Replace the single `session-list` key with a proper indexed query for scaling.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

No bugs identified.

### False Positives Removed

The following items were identified as violations but do not apply:

1. **T6 (Constructor null checks)**: NRT provides compile-time safety. Adding runtime null checks for NRT-protected parameters is unnecessary.

2. **T19 (Internal helper methods)**: T19 applies to PUBLIC members only. Private helper methods don't require XML documentation.

3. **T19 (Internal model properties)**: T19 applies to PUBLIC members only. Internal model properties don't require XML documentation.

4. **T25 (`.ToString()` on event models)**: T25 explicitly allows `.ToString()` at event boundaries for cross-language compatibility. Event schemas intentionally use string types.

### Previously Fixed

1. **T21 (Hardcoded lock timeout)**: Added `LockTimeoutSeconds` to configuration schema (default: 60). All `_lockProvider.LockAsync()` calls now use `_configuration.LockTimeoutSeconds`.

2. **T10 (Logging levels)**: Changed operation entry logs from `LogInformation` to `LogDebug` for: Listing, Creating, Getting, Performing, and Kicking operations.

3. **T7 (ApiException catch)**: Added `ApiException` catches before `Exception` catches for all mesh client calls (voice, permission, subscription). ApiException logged at Warning level with status code; generic Exception logged at Error or Warning level depending on operation criticality.

### Intentional Quirks (Documented Behavior)

1. **Static `_accountSubscriptions` cache with eviction**: A `static ConcurrentDictionary` shared across all scoped instances. Only holds accounts with active subscriptions - entries are evicted when the last subscription is removed. Unsubscribed accounts are NOT cached (triggers a fresh Subscription service query on each connect event). This is a local filter cache only - authoritative state is in lib-state's subscriber sessions.

2. **Dual join paths**: `JoinGameSessionAsync` joins by game type (resolves lobby), while `JoinGameSessionByIdAsync` joins by session ID (for matchmade sessions). Both share the same lock/permission/event pattern but have different validation (subscriber session vs reservation token).

3. **Voice integration is non-fatal**: Voice room creation, join, leave, and delete failures are all caught and logged as warnings. Sessions function without voice if the voice service is unavailable.

4. **Permission state rollback on failure**: If setting `in_game` permission state fails during join, the player is removed from the session model before saving. For matchmade sessions, the reservation is also un-claimed.

5. **ServerSalt is required (fail-fast)**: Constructor throws `InvalidOperationException` if `ServerSalt` is empty. All instances must share the same salt for GUID generation to produce consistent shortcuts.

6. **Reconnection re-publishes shortcuts**: `session.reconnected` is handled identically to `session.connected` - all shortcuts are re-published. This handles the case where a client loses connection and reconnects without needing to re-subscribe.

7. **Lobby stored twice**: Auto-created lobbies are saved under both `session:{sessionId}` and `lobby:{stubName}` keys. The lobby key enables O(1) lookup by game type; the session key enables the standard session operations.

8. **Session timeout 0 = infinite**: When `DefaultSessionTimeoutSeconds` is 0, `SessionTtlOptions` is null and no TTL is applied to state store saves. Sessions persist indefinitely until explicitly deleted or cleaned up.

9. **Whisper chat delivery**: Whisper messages are sent to exactly two recipients (sender and target) using individual `PublishToSessionAsync` calls. The sender receives a copy with `IsWhisperToMe=false` for local echo.

### Design Considerations (Requires Planning)

1. **T25 (String SessionId in POCOs)**: `GameSessionModel.SessionId` and `CleanupSessionModel.SessionId` are `string` instead of `Guid`. Requires model refactoring and updating all `Guid.Parse()`/`.ToString()` conversions.

2. **T25 (String comparison for enum)**: `HandleSubscriptionUpdatedInternalAsync` compares string literals for subscription actions. Should accept typed enum parameter and use enum equality.

3. **T5 (Inline event models)**: `SessionCancelledClientEvent` and `SessionCancelledServerEvent` are defined inline. Should be defined in schema files and generated.

4. **T21 (SupportedGameServices fallback)**: Code has `?? new[]` fallback despite configuration having a default. Should either remove fallback or throw on null.

7. **Session list is a single key**: All session IDs are stored in one `session-list` key (a `List<string>`). Listing loads ALL IDs then loads each session individually. No database-level pagination. With thousands of sessions, this becomes a bottleneck.

8. **No cleanup of finished lobbies from session-list**: When a lobby's status becomes `Finished`, it remains in the `session-list` key. The cleanup service only handles matchmade session reservations, not lobby lifecycle.

9. **Join validates subscriber session but Leave does not**: `JoinGameSessionAsync` calls `IsValidSubscriberSessionAsync` to verify authorization, but leave operations only check player membership in the session. A player whose subscription expires while in a game can still leave.

10. **Chat broadcasts to all players regardless of game state**: `SendChatMessageAsync` sends to all players in the session. There's no concept of "muted" players or chat rate limiting.

11. **CleanupSessionModel duplicates fields**: The `ReservationCleanupService` defines its own minimal model classes (`CleanupSessionModel`, `CleanupReservationModel`, `CleanupPlayerModel`) rather than using the main `GameSessionModel`. Changes to the main model may not be reflected in cleanup logic.

12. **Chat allows messages from non-members**: `SendChatMessageAsync` looks up the sender in the player list but doesn't fail if not found. A sender who isn't in the session can still broadcast messages - `SenderName` will be null but the message is delivered.

13. **Whisper to non-existent target silently succeeds**: If the whisper target isn't in the session (or has left), the whisper is silently not delivered to them. The sender still receives their copy. No error returned.

14. **Chat returns OK when no players exist**: Returns `StatusCodes.OK` even when all players have left (no WebSocket sessions to deliver to). From the sender's perspective, the message "sent" successfully.

15. **Move action special-cased for empty data**: `Move` is the only action type allowed to have null `ActionData`. Other actions log a debug message but proceed anyway, making the validation ineffective.

16. **Actions endpoint validates session existence, not player membership**: `PerformGameActionAsync` verifies the lobby exists and isn't finished but never checks if the requesting `AccountId` is actually a player in the session. Relies entirely on permission-based access control.

17. **Lock owner is random GUID per call**: Lock calls use `Guid.NewGuid().ToString()` as the lock owner. This means the same service instance cannot extend or re-acquire its own lock - each call gets a new identity.
