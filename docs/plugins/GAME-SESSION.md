# Game Session Plugin Deep Dive

> **Plugin**: lib-game-session
> **Schema**: schemas/game-session-api.yaml
> **Version**: 2.0.0
> **State Store**: game-session-statestore (MySQL)

---

## Overview

Hybrid lobby/matchmade game session management with subscription-driven shortcut publishing, voice integration, and real-time chat. Manages two session types: **lobby** sessions (persistent, per-game-service entry points auto-created for subscribed accounts) and **matchmade** sessions (pre-created by matchmaking with reservation tokens and TTL-based expiry). Integrates with Permission service for `in_game` state tracking, Voice service for room lifecycle, and Subscription service for account eligibility. Features distributed subscriber session tracking via ETag-based optimistic concurrency, publishes WebSocket shortcuts to connected clients enabling one-click game join, **per-game horizontal scaling** via `SupportedGameServices` partitioning, and **generic lobbies** for open catch-all entry points without subscription requirements.

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
| lib-subscription (`ISubscriptionClient`) | Query account subscriptions for shortcut eligibility and startup cache warmup |
| lib-connect (`IConnectClient`) | Query connected sessions for an account on `subscription.updated` events |

> **Refactoring Consideration**: This plugin injects 4 service clients individually. Consider whether `IServiceNavigator` would reduce constructor complexity, trading explicit dependencies for cleaner signatures. Currently favoring explicit injection for dependency clarity.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-matchmaking | Creates matchmade sessions with reservations via `IGameSessionClient.CreateGameSessionAsync`; calls `PublishJoinShortcutAsync` to notify players |
| lib-analytics | Uses `IGameSessionClient` for session context queries |

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

| Topic | Event Type | Handler | Action |
|-------|-----------|---------|--------|
| `session.connected` | `SessionConnectedEvent` | `HandleSessionConnectedAsync` | Tracks session, fetches subscriptions if not cached, publishes join shortcuts for subscribed accounts |
| `session.disconnected` | `SessionDisconnectedEvent` | `HandleSessionDisconnectedAsync` | Removes session from subscriber tracking (only if authenticated) |
| `session.reconnected` | `SessionReconnectedEvent` | `HandleSessionReconnectedAsync` | Re-publishes shortcuts (treated same as new connection) |
| `subscription.updated` | `SubscriptionUpdatedEvent` | `HandleSubscriptionUpdatedAsync` | Updates subscription cache, queries Connect for all account sessions, publishes/revokes shortcuts |

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
| `SupportedGameServices` | `GAME_SESSION_SUPPORTED_GAME_SERVICES` | `generic` | Comma-separated game service stub names (see Horizontal Scaling) |
| `GenericLobbiesEnabled` | `GAME_SESSION_GENERIC_LOBBIES_ENABLED` | `false` | Auto-publish generic shortcuts without subscription (see Generic Lobbies) |
| `LockTimeoutSeconds` | `GAME_SESSION_LOCK_TIMEOUT_SECONDS` | `60` | Timeout in seconds for distributed session locks |

---

## Horizontal Scaling by Game

The `SupportedGameServices` configuration enables **per-game horizontal scaling** by partitioning which game-session instances handle which games. This is a comma-delimited list (CDL) that filters which `subscription.updated` events the instance processes.

### How It Works

```
Deployment Topology (example)
==============================

Node A (main)                    Node B (arcadia)              Node C (fantasia)
─────────────────────────────    ─────────────────────────     ─────────────────────────
SUPPORTED_GAME_SERVICES=generic  SUPPORTED_GAME_SERVICES=      SUPPORTED_GAME_SERVICES=
                                   arcadia                       fantasia

Handles:                         Handles:                      Handles:
 • Generic catch-all lobbies      • Arcadia game lobbies        • Fantasia game lobbies
 • Unknown/new games              • Arcadia subscriptions       • Fantasia subscriptions


subscription.updated event (stubName="arcadia") published
    │
    ├─► Node A: IsOurService("arcadia") → false → ignores
    ├─► Node B: IsOurService("arcadia") → true  → processes, publishes shortcut
    └─► Node C: IsOurService("arcadia") → false → ignores
```

### Configuration

```bash
# Main node - handles generic/catch-all (default)
GAME_SESSION_SUPPORTED_GAME_SERVICES=generic

# Dedicated game nodes
GAME_SESSION_SUPPORTED_GAME_SERVICES=arcadia
GAME_SESSION_SUPPORTED_GAME_SERVICES=fantasia

# Multi-game node
GAME_SESSION_SUPPORTED_GAME_SERVICES=arcadia,fantasia
```

> **Important**: If you create a new game service (e.g., `my-new-game`), subscription-based lobby shortcuts **will not work** until you add it to `SupportedGameServices` on at least one game-session instance. The default configuration only handles `generic`. This is by design for horizontal scaling, but means new games are silent until configured.

### Why This Works Transparently

Because **all game-session endpoints are accessed via prebound shortcuts** (not direct API calls), clients never need to know which node handles which game. When a subscription is created:

1. The `subscription.updated` event is published to all game-session instances
2. Only the instance configured to handle that game's `stubName` processes the event
3. That instance publishes the join shortcut to the player's WebSocket session
4. The shortcut routes to the correct node automatically via the mesh

This enables games to be moved between nodes, scaled independently, or consolidated without any client-side changes.

---

## Generic Lobbies

When `GenericLobbiesEnabled` is `true` AND `"generic"` is in `SupportedGameServices`, the service publishes a generic lobby shortcut to **all authenticated sessions** immediately on connect—without requiring a subscription.

### Use Cases

- **Open catch-all lobbies**: Players can join a general lobby without subscribing to any specific game
- **Testing/development**: Simplifies integration testing without subscription setup
- **Free-to-play entry points**: Let players experience multiplayer before committing to a game subscription

### Behavior

| GenericLobbiesEnabled | "generic" in SupportedGameServices | Result |
|-----------------------|------------------------------------|--------|
| `false` (default) | Yes | Generic shortcuts require subscription to "generic" service |
| `true` | Yes | Generic shortcuts auto-published to all authenticated sessions |
| `true` | No | No effect (instance doesn't handle generic) |
| `false` | No | No effect (instance doesn't handle generic) |

### Configuration

```bash
# Enable generic lobbies on the main node
GAME_SESSION_SUPPORTED_GAME_SERVICES=generic
GAME_SESSION_GENERIC_LOBBIES_ENABLED=true
```

### Flow Comparison

```
WITHOUT GenericLobbiesEnabled (subscription required):
──────────────────────────────────────────────────────
User connects → session.connected event
    │
    ├── Check subscriptions for "generic"
    │   └── Not subscribed? → No shortcut published
    │
    └── User must subscribe to "generic" first


WITH GenericLobbiesEnabled:
───────────────────────────
User connects → session.connected event
    │
    ├── GenericLobbiesEnabled=true && IsOurService("generic")
    │   └── Immediately publish generic lobby shortcut
    │
    └── User can join generic lobby without any subscription
```

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<GameSessionService>` | Scoped | Structured logging |
| `GameSessionServiceConfiguration` | Singleton | All 12 config properties |
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

1. **Kick does not clear permission state**: `KickPlayerAsync` removes the player from the session and publishes the `player-left` event with `Kicked=true`, but does NOT call `_permissionClient.ClearSessionStateAsync` for the kicked player's WebSocket session. The kicked player retains `game-session:in_game` permission state until session expiry. Compare to `LeaveGameSessionAsync` (line 751) and `LeaveGameSessionByIdAsync` (line 1162) which both clear state. Fix: Add permission state clearing to `KickPlayerAsync` matching the leave operations.

### Intentional Quirks

1. **Lobby stored twice**: Auto-created lobbies are saved under both `session:{sessionId}` and `lobby:{stubName}` keys. The lobby key enables O(1) lookup by game type; the session key enables the standard session operations. Potential for drift if one write fails.

2. **Session timeout 0 = infinite**: When `DefaultSessionTimeoutSeconds` is 0, `SessionTtlOptions` is null and no TTL is applied to state store saves. Sessions persist indefinitely until explicitly deleted or cleaned up.

3. **Chat allows messages from non-members**: `SendChatMessageAsync` looks up the sender in the player list but doesn't fail if not found. A sender who isn't in the session can still broadcast messages - `SenderName` will be null but the message is delivered.

4. **Whisper to non-existent target silently succeeds**: If the whisper target isn't in the session (or has left), the whisper is silently not delivered to them. The sender still receives their copy. No error returned.

5. **Chat returns OK when no players exist**: Returns `StatusCodes.OK` even when all players have left (no WebSocket sessions to deliver to). From the sender's perspective, the message "sent" successfully.

6. **Lock owner is random GUID per call**: Lock calls use `Guid.NewGuid().ToString()` as the lock owner. This means the same service instance cannot extend or re-acquire its own lock - each call gets a new identity.

### Design Considerations (Requires Planning)

1. **Inline event models not in schema**: `SessionCancelledClientEvent` and `SessionCancelledServerEvent` are defined as `internal class` in `ReservationCleanupService.cs` (lines 254-270). Should be defined in schema files and generated per FOUNDATION TENETS. These are used for the `game-session.session-cancelled` server event and the client-facing cancellation notification.

2. **T21 (SupportedGameServices fallback)**: Code has `?? new[]` fallback despite configuration having a default. Should either remove fallback or throw on null.

3. **Session list is a single key**: All session IDs are stored in one `session-list` key (a `List<string>`). Listing loads ALL IDs then loads each session individually. No database-level pagination. With thousands of sessions, this becomes a bottleneck.

4. **No cleanup of finished lobbies from session-list**: When a lobby's status becomes `Finished`, it remains in the `session-list` key. The cleanup service only handles matchmade session reservations, not lobby lifecycle.

5. **Join validates subscriber session but Leave does not**: `JoinGameSessionAsync` calls `IsValidSubscriberSessionAsync` to verify authorization, but leave operations only check player membership in the session. A player whose subscription expires while in a game can still leave.

6. **CleanupSessionModel duplicates fields**: The `ReservationCleanupService` defines its own minimal model classes (`CleanupSessionModel`, `CleanupReservationModel`, `CleanupPlayerModel`) rather than using the main `GameSessionModel`. Changes to the main model may not be reflected in cleanup logic.

7. **Move action special-cased for empty data**: `Move` is the only action type allowed to have null `ActionData`. Other actions log a debug message but proceed anyway, making the validation ineffective.

8. **Actions endpoint validates session existence, not player membership**: `PerformGameActionAsync` verifies the lobby exists and isn't finished but never checks if the requesting `AccountId` is actually a player in the session. Relies entirely on permission-based access control.
