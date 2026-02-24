# Game Session Plugin Deep Dive

> **Plugin**: lib-game-session
> **Schema**: schemas/game-session-api.yaml
> **Version**: 2.0.0
> **Layer**: GameFoundation
> **State Store**: game-session-statestore (MySQL)

---

## Overview

Multiplayer session container primitive (L2 GameFoundation) with subscription-driven shortcut publishing for basic game access. Manages two session types: **lobby** sessions (persistent, per-game-service entry points auto-created for subscribed accounts) and **matchmade** sessions (pre-created by matchmaking with reservation tokens and TTL-based expiry). Integrates with Permission for `in_game` state tracking and Subscription for account eligibility. Publishes WebSocket shortcuts to connected clients for one-click game join, lifecycle events for session state changes, and supports per-game horizontal scaling via `SupportedGameServices` partitioning.

GameSession is to players what Inventory is to items: a **container primitive**. It owns who is in what multiplayer context, with distributed locking, reservation tokens, and permission state management. Higher-layer services (Gardener, Matchmaking) create and manage these containers for their own purposes.

---

## Architectural Role

### What GameSession IS

GameSession is a **multiplayer session container primitive**. Its core responsibilities:

1. **Container CRUD** — create, join, leave, kick with distributed locking
2. **Reservation token system** — cryptographically secure one-time tokens for matchmaking
3. **Permission state tracking** — sets/clears `in_game` on Permission service
4. **Session lifecycle events** — `created`, `updated`, `deleted`, `player-joined`, `player-left`, `cancelled`
5. **Basic game-access shortcuts** — subscription-driven lobby shortcuts as an L2 fallback

### What GameSession is NOT

- **Not the player entry experience** — that's Gardener (L4), which orchestrates voids, gardens, POIs, and scenario selection
- **Not player identity** — that's Auth/Connect (L1), which manages JWT sessions and WebSocket connections
- **Not the UX capability surface** — that's Agency (L4), which translates guardian spirit seed growth into UI module fidelity
- **Not game access control** — that's Subscription (L2), which tracks which accounts can access which games

### Relationship with Gardener (L4)

Gardener is the **player experience orchestrator** — the player-side counterpart to Puppetmaster. Where GameSession provides containers, Gardener decides *when and why* to put players in them:

| Concern | GameSession (L2) | Gardener (L4) |
|---------|-------------------|---------------|
| "Who is in this multiplayer context?" | Owns this | Consumes this |
| "What does the player experience?" | No opinion | Owns this |
| "How does a player enter a game?" | Primitive shortcuts (L2 fallback) | Rich discovery experience (voids, POIs, scenarios) |
| "What happens during gameplay?" | Container membership tracking | Garden context, entity associations, scenario lifecycle |

**Current flow**: Gardener creates GameSession containers to back scenarios (`GameType="gardener-scenario"`), uses session IDs for cleanup tracking, and calls `LeaveGameSessionByIdAsync` on scenario completion. Matchmaking similarly creates containers with reservation tokens.

**Coexistence**: Games that declare `autoLobbyEnabled: true` on their GameService definition get naive lobby shortcuts from GameSession on connect. Games that declare `autoLobbyEnabled: false` (like Arcadia) rely on Gardener for entry orchestration. Both coexist in the same deployment — see [Design Consideration #8](#design-considerations-requires-planning) for the planned `autoLobbyEnabled` property.

**L2-only deployments** (no L4): GameSession's subscription-driven shortcut pipeline provides basic game entry. Players get shortcuts, join lobbies, and the container tracks membership. This is functional but lacks the rich progressive discovery experience that Gardener provides.

### Relationship with Agency (L4)

**None.** Agency is orthogonal — it translates guardian spirit seed growth into UX module fidelity. Agency works with seeds, not sessions. GameSession and Agency never interact.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL persistence for sessions, lobbies, session lists, subscriber sessions |
| lib-state (`IDistributedLockProvider`) | Session-level locks for concurrent join/leave/kick operations |
| lib-messaging (`IMessageBus`) | Publishing lifecycle and player events; error event publishing |
| lib-messaging (`IEventConsumer`) | 4 event subscriptions (session lifecycle, subscription changes) |
| lib-messaging (`IClientEventPublisher`) | Push shortcuts, chat messages, and cancellation notices to WebSocket sessions |
| lib-permission (`IPermissionClient`) | Set/clear `game-session:in_game` state on join/leave |
| lib-subscription (`ISubscriptionClient`) | Query account subscriptions for shortcut eligibility and startup cache warmup |
| lib-connect (`IConnectClient`) | Query connected sessions for an account on `subscription.updated` events |

> **Note**: This plugin injects 3 service clients individually (Permission, Subscription, Connect). Explicit injection is preferred for dependency clarity per FOUNDATION TENETS.

---

## Dependents (What Relies On This Plugin)

| Dependent | Layer | Relationship |
|-----------|-------|-------------|
| lib-matchmaking | L4 | Creates matchmade sessions with reservations via `IGameSessionClient.CreateGameSessionAsync`; calls `PublishJoinShortcutAsync` to notify players |
| lib-gardener | L4 | Creates `gardener-scenario` sessions to back player scenarios via `IGameSessionClient.CreateGameSessionAsync`; calls `LeaveGameSessionByIdAsync` on completion/abandonment; subscribes to `game-session.deleted` for observational logging |
| lib-analytics | L4 | Maps session IDs to game-service IDs via `IGameSessionClient.GetGameSessionAsync`; subscribes to `game-session.created` and `game-session.deleted` for cache maintenance |

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
| `game-session.updated` | `GameSessionUpdatedEvent` | Session state modified (includes `changedFields` list) |
| `game-session.deleted` | `GameSessionDeletedEvent` | Session removed (includes `deletedReason`) |
| `game-session.player-joined` | `GameSessionPlayerJoinedEvent` | Player joins a session |
| `game-session.player-left` | `GameSessionPlayerLeftEvent` | Player leaves or is kicked (includes `Kicked` flag) |
| `game-session.action.performed` | `GameSessionActionPerformedEvent` | Game action executed in session (includes `accountId`) |
| `game-session.cancelled` | `GameSessionCancelledEvent` | Matchmade session cancelled due to reservation expiry |

### Client Events

| Event Type | Trigger |
|-----------|---------|
| `ShortcutPublishedEvent` | Join shortcut pushed to player's WebSocket session |
| `ChatMessageReceivedEvent` | Chat message delivered to player's WebSocket session |
| `SessionCancelledEvent` | Matchmade session cancelled notification pushed to affected players |

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
| `GameSessionServiceConfiguration` | Singleton | Typed configuration for all service properties |
| `IStateStoreFactory` | Singleton | MySQL state store access |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IEventConsumer` | Scoped | Event handler registration |
| `IClientEventPublisher` | Scoped | Shortcut/chat/cancellation push to WebSocket sessions |
| `ITelemetryProvider` | Singleton | Distributed tracing spans for all async methods |
| `IPermissionClient` | Scoped | Permission state management |
| `ISubscriptionClient` | Scoped | Account subscription queries |
| `IConnectClient` | Scoped | Query connected sessions per account |
| `IDistributedLockProvider` | Singleton | Session-level distributed locks (session operations, session-list, lobby creation) |
| `GameSessionStartupService` | Hosted (BackgroundService) | Subscription cache warmup on startup |
| `ReservationCleanupService` | Hosted (BackgroundService) | Periodic expired reservation cleanup |

Service lifetime is **Scoped** (per-request) for GameSessionService. Two BackgroundServices for async operations.

---

## API Endpoints (Implementation Notes)

### Game Sessions (3 endpoints)

- **Create** (`/sessions/create`): Generates session ID. For matchmade: creates reservations with crypto-random tokens and expiry TTL. Saves to state store with optional TTL. Publishes `game-session.created`. Enforces `MaxPlayersPerSession` cap.
- **Get** (`/sessions/get`): Simple load-by-ID with NotFound on miss.
- **List** (`/sessions/list`): Loads ALL session IDs from `session-list`, loads each session individually, filters out `Finished` status. In-memory pagination.

### Lobby Join/Leave (2 endpoints)

- **Join** (`/sessions/join`): Lobby-based join. Validates subscriber session via distributed state. Gets lobby by game type. Acquires distributed lock. Checks capacity/status/duplicate player. Sets `game-session:in_game` permission state (rolls back player on failure, re-throws after rollback per IMPLEMENTATION TENETS). Publishes `player-joined` event.
- **Leave** (`/sessions/leave`): Lobby-based leave. Acquires lock. Clears permission state (best-effort, continues on failure). Marks session `Finished` when empty. Publishes `player-left` event.

### Direct Session Join/Leave (2 endpoints)

- **JoinById** (`/sessions/join-session`): For matchmade sessions. Validates reservation token and expiry. Marks reservation as claimed. Same lock/permission/status logic as lobby join (re-throws after rollback per IMPLEMENTATION TENETS).
- **LeaveById** (`/sessions/leave-session`): Direct leave by session ID. Same logic as lobby leave but without game type lookup.

### Admin (1 endpoint)

- **Kick** (`/sessions/kick`): Acquires lock. Removes player. Publishes `player-left` with `Kicked=true` and reason.

### Game Actions (1 endpoint)

- **Actions** (`/sessions/actions`): Validates lobby exists and isn't finished. Publishes `game-session.action.performed` event. Returns action ID with echoed data. Does not validate player membership (relies on permission state).

### Game Chat (1 endpoint)

- **Chat** (`/sessions/chat`): Builds `ChatMessageReceivedEvent`. For whispers: sends to target (with `IsWhisperToMe=true`) and sender only. For public: broadcasts to all player WebSocket sessions via `PublishToSessionsAsync`.

### Internal (1 endpoint)

- **PublishJoinShortcut** (`/sessions/publish-join-shortcut`): Called by matchmaking. Verifies session and reservation token. Generates route GUID and target GUID using server salt. Builds pre-bound `JoinGameSessionByIdRequest` as shortcut payload. Publishes `ShortcutPublishedEvent` to player's WebSocket session. Returns `shortcutRouteGuid` (required, non-nullable).

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
  │      └── Publish game-session.cancelled            │
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

1. ~~**Kick does not clear permission state**~~: **FIXED** (2026-01-31) - `KickPlayerAsync` now calls `_permissionClient.ClearSessionStateAsync` using the kicked player's WebSocket session ID (`playerToKick.SessionId`) before saving the session. Error handling matches the pattern in Leave methods (best-effort with logging and error event publishing).

### Intentional Quirks

1. **Lobby stored twice**: Auto-created lobbies are saved under both `session:{sessionId}` and `lobby:{stubName}` keys. The lobby key enables O(1) lookup by game type; the session key enables the standard session operations. Potential for drift if one write fails.

2. **Session timeout 0 = infinite**: When `DefaultSessionTimeoutSeconds` is 0, `SessionTtlOptions` is null and no TTL is applied to state store saves. Sessions persist indefinitely until explicitly deleted or cleaned up.

3. **Chat allows messages from non-members**: `SendChatMessageAsync` looks up the sender in the player list but doesn't fail if not found. A sender who isn't in the session can still broadcast messages - `SenderName` will be null but the message is delivered.

4. **Whisper to non-existent target silently succeeds**: If the whisper target isn't in the session (or has left), the whisper is silently not delivered to them. The sender still receives their copy. No error returned.

5. **Chat returns OK when no players exist**: Returns `StatusCodes.OK` even when all players have left (no WebSocket sessions to deliver to). From the sender's perspective, the message "sent" successfully.

6. **Lock owner is random GUID per call**: Lock calls use `Guid.NewGuid().ToString()` as the lock owner. This means the same service instance cannot extend or re-acquire its own lock - each call gets a new identity.

### Design Considerations (Requires Planning)

1. ~~**Inline event models not in schema**~~: **FIXED** (2026-02-24) - `GameSessionCancelledEvent` (server) and `SessionCancelledEvent` (client) are now defined in the event and client-event schemas respectively and generated per FOUNDATION TENETS.

2. **Session list is a single key**: All session IDs are stored in one `session-list` key (a `List<string>`). Listing loads ALL IDs then loads each session individually. No database-level pagination. With thousands of sessions, this becomes a bottleneck.

3. **No cleanup of finished lobbies from session-list**: When a lobby's status becomes `Finished`, it remains in the `session-list` key. The cleanup service only handles matchmade session reservations, not lobby lifecycle.

4. **Join validates subscriber session but Leave does not**: `JoinGameSessionAsync` calls `IsValidSubscriberSessionAsync` to verify authorization, but leave operations only check player membership in the session. A player whose subscription expires while in a game can still leave.

5. **CleanupSessionModel duplicates fields**: The `ReservationCleanupService` defines its own minimal model classes (`CleanupSessionModel`, `CleanupReservationModel`, `CleanupPlayerModel`) rather than using the main `GameSessionModel`. Changes to the main model may not be reflected in cleanup logic.

6. **Move action special-cased for empty data**: `Move` is the only action type allowed to have null `ActionData`. Other actions log a debug message but proceed anyway, making the validation ineffective.

7. **Actions endpoint validates session existence, not player membership**: `PerformGameActionAsync` verifies the lobby exists and isn't finished but never checks if the requesting `AccountId` is actually a player in the session. Relies entirely on permission-based access control.

8. **`autoLobbyEnabled` property on GameService definitions**: GameSession's subscription-driven shortcut pipeline currently publishes lobby shortcuts for ALL subscribed games on `session.connected`. Games with rich entry orchestration (Arcadia via Gardener) don't want naive lobby shortcuts — their entry flow is managed by L4. A boolean `autoLobbyEnabled` property (default `true`) on the GameService definition would let games opt out of naive lobby creation. GameSession's `HandleSessionConnectedAsync` would check this property before publishing shortcuts, skipping games where `autoLobbyEnabled: false`. This allows naive-lobby games and Gardener-orchestrated games to coexist in the same deployment. The property belongs on GameService (L2, same layer) rather than GameSession configuration because it describes a property of the game, not a deployment topology concern (which is what `SupportedGameServices` handles).

---

## Work Tracking

*This section tracks active development work. Markers are managed by `/audit-plugin` workflow.*

### Completed

- **2026-02-24**: L3 hardening pass — voice removal, lifecycle events, type safety, telemetry, error handling, distributed locks, filler removal. See details below.
  - Removed all voice integration (IVoiceClient dependency, VoiceEnabled, VoiceRoomId, voice types from schema). Resolved L2→L3 hierarchy violation.
  - Added `game-session.updated` (with `changedFields`) and `game-session.deleted` (with `deletedReason`) lifecycle events — previously defined but never published.
  - Added `game-session.cancelled` (`GameSessionCancelledEvent`) domain event and `SessionCancelledEvent` client event — both now schema-defined and generated.
  - Removed `success` filler boolean from `PublishJoinShortcutResponse`; `shortcutRouteGuid` is now required and non-nullable.
  - Type safety: `CleanupPlayerModel.WebSocketSessionId` string→`Guid?`; `GameSessionModel.Owner` `Guid`→`Guid?`; event handlers accept `Guid` directly; `Guid.Empty` sentinels replaced with `Guid?` nullable; `GameType` consolidated to `$ref`; enum `.ToString()` removed from action response.
  - Added `accountId` to `GameSessionActionPerformedEvent`.
  - Added distributed locks for session-list read-modify-write and lobby creation (double-check-under-lock in `GetOrCreateLobbySessionAsync`).
  - Added `ITelemetryProvider` and `StartActivity` spans to all internal async methods in `GameSessionService`, `ReservationCleanupService`, and `GameSessionStartupService`.
  - Fixed generic catch blocks in join methods to re-throw after rollback per IMPLEMENTATION TENETS; leave-session catch intentionally continues.
- **2026-01-31**: Fixed "Kick does not clear permission state" bug - added permission clearing to `KickPlayerAsync` matching the Leave methods.
