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

### Tenet Violations (Fix Immediately)

1. **FOUNDATION TENETS (T6) - Missing null checks in constructor**: The constructor at `GameSessionService.cs` lines 134-170 does not perform `ArgumentNullException.ThrowIfNull()` or `?? throw new ArgumentNullException()` checks on any of its injected dependencies (`stateStoreFactory`, `messageBus`, `logger`, `configuration`, `clientEventPublisher`, `voiceClient`, `permissionClient`, `subscriptionClient`, `lockProvider`). The T6 pattern requires explicit null-guard on all constructor parameters.
   - **File**: `plugins/lib-game-session/GameSessionService.cs`, lines 146-154
   - **Fix**: Add `ArgumentNullException.ThrowIfNull()` for each constructor parameter before assignment.

2. **IMPLEMENTATION TENETS (T21) - Hardcoded lock timeout**: All `_lockProvider.LockAsync()` calls use a hardcoded `60` second expiry (lines 452, 705, 877, 1075, 1338). This is a tunable value that must be defined as a configuration property.
   - **File**: `plugins/lib-game-session/GameSessionService.cs`, lines 452, 705, 877, 1075, 1338
   - **Fix**: Add `LockTimeoutSeconds` to `schemas/game-session-configuration.yaml` and use `_configuration.LockTimeoutSeconds` instead of the literal `60`.

3. **IMPLEMENTATION TENETS (T25) - String field for GUID in internal POCO**: `GameSessionModel.SessionId` is declared as `string` (line 2212) but represents a GUID. All operations on it involve `Guid.Parse()` or `.ToString()` conversions. Similarly, `CleanupSessionModel.SessionId` at `ReservationCleanupService.cs` line 226.
   - **File**: `plugins/lib-game-session/GameSessionService.cs`, line 2212; `ReservationCleanupService.cs`, line 226
   - **Fix**: Change `SessionId` to `Guid` type in both `GameSessionModel` and `CleanupSessionModel`. Remove all `Guid.Parse(model.SessionId)` and `sessionId.ToString()` conversions.

4. **IMPLEMENTATION TENETS (T25) - String comparison for enum values**: `HandleSubscriptionUpdatedInternalAsync` at line 1635 accepts `action` as `string` and compares it with string literals (`"created"`, `"renewed"`, `"updated"`, `"cancelled"`, `"expired"`) at lines 1641, 1656. The `SubscriptionUpdatedEvent.Action` is a typed `SubscriptionUpdatedEventAction` enum, but `GameSessionServiceEvents.cs` line 77 converts it to string with `.ToString().ToLowerInvariant()`.
   - **File**: `plugins/lib-game-session/GameSessionServiceEvents.cs`, line 77; `GameSessionService.cs`, lines 1635, 1641, 1656
   - **Fix**: Change `HandleSubscriptionUpdatedInternalAsync` parameter from `string action` to `SubscriptionUpdatedEventAction action` and use enum equality checks instead of string comparisons.

5. **IMPLEMENTATION TENETS (T25) - `.ToString()` populating event models**: `CreateGameSessionAsync` at lines 345-347 uses `session.GameType.ToString()` and `session.Status.ToString()` to populate `GameSessionCreatedEvent`. The `GameSessionActionPerformedEvent.ActionType` at line 643 uses `body.ActionType.ToString()`. If the event models use string fields for cross-language compatibility, this conversion should be at the event boundary only, which it is -- but the internal `GameSessionModel` stores `SessionId` as string (see violation 3 above).
   - **File**: `plugins/lib-game-session/GameSessionService.cs`, lines 345, 347, 643
   - **Fix**: Verify that event model schema fields are intentionally string for cross-language compatibility. If so, this is acceptable at the event boundary. The primary fix is violation 3.

6. **IMPLEMENTATION TENETS (T7) - Missing ApiException catch**: All endpoint methods (e.g., `ListGameSessionsAsync`, `CreateGameSessionAsync`, `JoinGameSessionAsync`, etc.) catch only the generic `Exception` at their outer try-catch. They do not catch `ApiException` separately for expected API failures from downstream service calls (`_voiceClient`, `_permissionClient`, `_subscriptionClient`). Per T7, `ApiException` should be caught first and logged as warning with status propagation, before the generic `Exception` catch.
   - **File**: `plugins/lib-game-session/GameSessionService.cs`, lines 219-232, 360-373, 565-578, 662-675, 840-853, 1039-1052, 1189-1202, 1304-1317, 1391-1403, 1521-1534
   - **Fix**: Add `catch (ApiException ex)` before `catch (Exception ex)` in each endpoint method. Log as warning and propagate status code.

7. **IMPLEMENTATION TENETS (T21) - Hardcoded fallback for SupportedGameServices**: `GameSessionStartupService.cs` line 65 has `?? new[] { "arcadia", "generic" }` as a fallback when `_configuration.SupportedGameServices` is null. Similarly in `GameSessionService.cs` line 158. The configuration has a default value of `"arcadia,generic"` already, so this fallback masks configuration issues.
   - **File**: `plugins/lib-game-session/GameSessionStartupService.cs`, line 65; `GameSessionService.cs`, line 158
   - **Fix**: Since `GameSessionServiceConfiguration.SupportedGameServices` has a non-null default (`"arcadia,generic"`), remove the `?? new[]` fallback. If the value can genuinely be null despite the default, throw an `InvalidOperationException` for fail-fast behavior.

8. **QUALITY TENETS (T10) - LogInformation used for operation entry**: Multiple endpoints log at `Information` level for routine operation entry (e.g., line 181 "Listing game sessions", line 246 "Creating game session", line 385 "Getting game session", line 429 "Player joining game"). Per T10, operation entry should be `Debug` level. `Information` is for significant state changes.
   - **File**: `plugins/lib-game-session/GameSessionService.cs`, lines 181, 246, 385, 429, 597, 692, 871, 1069, 1221, 1332, 1423
   - **Fix**: Change operation entry logs from `LogInformation` to `LogDebug`. Keep `LogInformation` only for the success outcome logs that represent meaningful state changes (e.g., "Game session created successfully", "Player joined").

9. **QUALITY TENETS (T19) - Missing XML documentation on internal helper methods**: `LoadSessionAsync` (line 2099), `MapModelToResponse` (line 2107), `MapRequestGameTypeToResponse` (line 2135), `MapChatMessageType` (line 2157), `MapVoiceTierToConnectionInfoTier` (line 2168), `MapVoiceCodecToConnectionInfoCodec` (line 2178) are missing `<summary>` XML documentation.
   - **File**: `plugins/lib-game-session/GameSessionService.cs`, lines 2099, 2107, 2135, 2157, 2168, 2178
   - **Fix**: Add `/// <summary>` documentation to each helper method.

10. **QUALITY TENETS (T19) - Missing XML documentation on internal model properties**: `GameSessionModel` properties (lines 2212-2222) are missing `<summary>` tags on: `SessionId`, `GameType`, `SessionName`, `Status`, `MaxPlayers`, `CurrentPlayers`, `IsPrivate`, `Owner`, `Players`, `CreatedAt`, `GameSettings`.
    - **File**: `plugins/lib-game-session/GameSessionService.cs`, lines 2212-2222
    - **Fix**: Add `/// <summary>` documentation to each property.

11. **IMPLEMENTATION TENETS (T5) - Untyped event models defined inline**: `SessionCancelledClientEvent` and `SessionCancelledServerEvent` are defined inline in `ReservationCleanupService.cs` (lines 254-270) rather than being defined in a schema file and generated. Per T5, all events must be typed schemas.
    - **File**: `plugins/lib-game-session/ReservationCleanupService.cs`, lines 254-270
    - **Fix**: Define `SessionCancelledClientEvent` in `schemas/game-session-client-events.yaml` and `SessionCancelledServerEvent` in `schemas/game-session-events.yaml`, then regenerate.

12. **CLAUDE.md - `?? string.Empty` without justification**: `GameSessionModel.SessionId` (line 2212) and `ReservationModel.Token` (line 2263) use `= string.Empty` initializers on fields that should be `Guid` (violation 3) or should throw on null. `CleanupSessionModel.SessionId` (line 226), `CleanupPlayerModel.WebSocketSessionId` (line 248), `SessionCancelledClientEvent.SessionId` (line 257), `SessionCancelledClientEvent.Reason` (line 258), `SessionCancelledServerEvent.SessionId` (line 268), `SessionCancelledServerEvent.Reason` (line 269) all use `= string.Empty` without the required justification comment.
    - **File**: `plugins/lib-game-session/GameSessionService.cs`, lines 2212, 2263; `ReservationCleanupService.cs`, lines 226, 248, 257, 258, 268, 269
    - **Fix**: For `SessionId` fields, change to `Guid` type (per violation 3). For `Token`, `Reason`, and `WebSocketSessionId` fields on internal models, either make them nullable (`string?`) or add the required justification comment explaining why empty string is acceptable.

13. **IMPLEMENTATION TENETS (T25) - String field for WebSocketSessionId in CleanupPlayerModel**: `CleanupPlayerModel.WebSocketSessionId` at `ReservationCleanupService.cs` line 248 is `string` but represents a GUID (WebSocket session IDs are GUIDs).
    - **File**: `plugins/lib-game-session/ReservationCleanupService.cs`, line 248
    - **Fix**: Change to `Guid WebSocketSessionId { get; set; }`.

14. **IMPLEMENTATION TENETS (T25) - String field for SessionId in SessionCancelledClientEvent and SessionCancelledServerEvent**: Both inline event models at `ReservationCleanupService.cs` lines 257, 268 use `string SessionId` instead of `Guid SessionId`.
    - **File**: `plugins/lib-game-session/ReservationCleanupService.cs`, lines 257, 268
    - **Fix**: Change to `Guid SessionId`. However, the better fix is violation 11 (define in schema and generate).

### Bugs (Fix Immediately)

None identified beyond the tenet violations above.

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

1. **Session list is a single key**: All session IDs are stored in one `session-list` key (a `List<string>`). Listing loads ALL IDs then loads each session individually. No database-level pagination. With thousands of sessions, this becomes a bottleneck.

2. **No cleanup of finished lobbies from session-list**: When a lobby's status becomes `Finished`, it remains in the `session-list` key. The cleanup service only handles matchmade session reservations, not lobby lifecycle.

3. **Join validates subscriber session but Leave does not**: `JoinGameSessionAsync` calls `IsValidSubscriberSessionAsync` to verify authorization, but leave operations only check player membership in the session. A player whose subscription expires while in a game can still leave.

4. **Chat broadcasts to all players regardless of game state**: `SendChatMessageAsync` sends to all players in the session. There's no concept of "muted" players or chat rate limiting.

5. **Lock timeout is hardcoded 60 seconds**: All `_lockProvider.LockAsync` calls use a 60-second expiry. Not configurable. Long-running operations under lock (e.g., multiple voice/permission calls) could approach this limit.

6. **CleanupSessionModel duplicates fields**: The `ReservationCleanupService` defines its own minimal model classes (`CleanupSessionModel`, `CleanupReservationModel`, `CleanupPlayerModel`) rather than using the main `GameSessionModel`. Changes to the main model may not be reflected in cleanup logic.

7. **Chat allows messages from non-members**: `SendChatMessageAsync` (line 1435) looks up the sender in the player list but doesn't fail if not found. A sender who isn't in the session can still broadcast messages - `SenderName` will be null but the message is delivered.

8. **Whisper to non-existent target silently succeeds**: Lines 1468-1487 - if the whisper target isn't in the session (or has left), the whisper is silently not delivered to them. The sender still receives their copy. No error returned.

9. **Chat returns OK when no players exist**: Line 1461 returns `StatusCodes.OK` even when all players have left (no WebSocket sessions to deliver to). From the sender's perspective, the message "sent" successfully.

10. **Move action special-cased for empty data**: Line 617 - `Move` is the only action type allowed to have null `ActionData`. Other actions log a debug message but proceed anyway, making the validation ineffective.

11. **Actions endpoint validates session existence, not player membership**: `PerformGameActionAsync` (lines 576-626) verifies the lobby exists and isn't finished but never checks if the requesting `AccountId` is actually a player in the session. Relies entirely on permission-based access control.

12. **Lock owner is random GUID per call**: Lines 444, 696, 868, 1066, 1330 all use `Guid.NewGuid().ToString()` as the lock owner. This means the same service instance cannot extend or re-acquire its own lock - each call gets a new identity.
