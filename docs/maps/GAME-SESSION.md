# Game Session Implementation Map

> **Plugin**: lib-game-session
> **Schema**: schemas/game-session-api.yaml
> **Layer**: GameFoundation
> **Deep Dive**: [docs/plugins/GAME-SESSION.md](../plugins/GAME-SESSION.md)

---

| Field | Value |
|-------|-------|
| Plugin | lib-game-session |
| Layer | L2 GameFoundation |
| Endpoints | 11 |
| State Stores | game-session-statestore (MySQL) |
| Events Published | 7 (`game-session.created`, `game-session.updated`, `game-session.deleted`, `game-session.player-joined`, `game-session.player-left`, `game-session.cancelled`, `game-session.action.performed`) |
| Events Consumed | 4 |
| Client Events | 8 pushed (`SessionChatReceived`, `SessionCancelled`, `ShortcutPublished`, `PlayerJoined`, `PlayerLeft`, `PlayerKicked`, `SessionStateChanged` on join/leave/kick) |
| Background Services | 2 |

---

## State

**Store**: `game-session-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `session:{sessionId}` | `GameSessionModel` | Individual session data (players, status, settings, reservations) |
| `session-list` | `List<string>` | Global index of all session IDs for listing and cleanup |
| `lobby:{stubName}` | `GameSessionModel` | Per-game-service lobby session for O(1) lookup by game type |
| `subscriber-sessions:{accountId}` | `SubscriberSessionsModel` | Set of WebSocket session IDs for a subscribed account (ETag-managed) |

**Lock store**: `GameSessionLock` used for all distributed lock acquisitions.

**Note**: State stores are constructor-cached as readonly fields (`_sessionStore`, `_sessionListStore`, `_subscriberSessionStore`).

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | MySQL persistence for sessions, lobbies, session list, subscriber sessions |
| lib-state (`IDistributedLockProvider`) | L0 | Hard | Session-level locks for join/leave/kick; session-list locks for creation/cleanup; lobby creation locks |
| lib-messaging (`IMessageBus`) | L0 | Hard | Publishing 7 lifecycle/domain event topics |
| lib-messaging (`IEventConsumer`) | L0 | Hard | Registering 4 event subscription handlers |
| lib-messaging (`IClientEventPublisher`) | L0 | Hard | Pushing shortcuts, chat, and cancellation events to WebSocket sessions |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Span instrumentation on all internal async methods |
| lib-permission (`IPermissionClient`) | L1 | Hard | Set/clear `game-session:in_game` state on join/leave/kick |
| lib-subscription (`ISubscriptionClient`) | L1 | Hard | Query account subscriptions for shortcut eligibility and startup cache warmup |
| lib-connect (`IConnectClient`) | L1 | Hard | Query connected sessions per account on `subscription.updated` events |
| lib-game-service (`IGameServiceClient`) | L2 | Hard | Check `autoLobbyEnabled` flag before publishing subscription-driven lobby shortcuts (fail-open) |

**Static in-process cache**: `_accountSubscriptions` is a `static ConcurrentDictionary<Guid, HashSet<string>>` (accountId to subscribed stubNames). Local filter only -- authoritative state is in lib-state. Warmed by `GameSessionStartupService` at startup, maintained by event handlers.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `game-session.created` | `GameSessionCreatedEvent` | CreateGameSession success |
| `game-session.updated` | `GameSessionUpdatedEvent` | JoinGameSession, JoinGameSessionById, LeaveGameSession, LeaveGameSessionById, KickPlayer (with `changedFields`) |
| `game-session.deleted` | `GameSessionDeletedEvent` | ReservationCleanupService cancels expired matchmade session |
| `game-session.player-joined` | `GameSessionPlayerJoinedEvent` | JoinGameSession, JoinGameSessionById success |
| `game-session.player-left` | `GameSessionPlayerLeftEvent` | LeaveGameSession, LeaveGameSessionById, KickPlayer success (includes `kicked` flag and `reason`) |
| `game-session.cancelled` | `GameSessionCancelledEvent` | ReservationCleanupService cancels expired matchmade session |
| `game-session.action.performed` | `GameSessionActionPerformedEvent` | PerformGameAction success |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `session.connected` | `HandleSessionConnectedAsync` | Stores subscriber session, fetches subscriptions if not cached, publishes lobby join shortcuts for subscribed services with `autoLobbyEnabled` |
| `session.disconnected` | `HandleSessionDisconnectedAsync` | Removes session from `subscriber-sessions:{accountId}` (authenticated sessions only) |
| `session.reconnected` | `HandleSessionReconnectedAsync` | Treats as new connection -- delegates to connected handler for shortcut re-publishing |
| `subscription.updated` | `HandleSubscriptionUpdatedAsync` | Updates local cache, queries Connect for all account sessions, publishes or revokes shortcuts per subscription state |

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<GameSessionService>` | Structured logging |
| `GameSessionServiceConfiguration` | Typed configuration (12 properties) |
| `IStateStoreFactory` | State store access (MySQL) |
| `IDistributedLockProvider` | Distributed locks for session mutations |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Event handler registration |
| `IClientEventPublisher` | WebSocket push (shortcuts, chat, cancellation, player-joined, player-left, state-changed) |
| `ITelemetryProvider` | Telemetry span instrumentation |
| `IPermissionClient` | Permission state management |
| `ISubscriptionClient` | Subscription queries |
| `IConnectClient` | Connected session queries |
| `IGameServiceClient` | Game service definition lookups |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| ListGameSessions | POST /sessions/list | admin | - | - |
| CreateGameSession | POST /sessions/create | [] | session, session-list | game-session.created |
| GetGameSession | POST /sessions/get | user, state:in_game | - | - |
| JoinGameSession | POST /sessions/join | [] | session | game-session.player-joined, game-session.updated |
| LeaveGameSession | POST /sessions/leave | user, state:in_game | session | game-session.player-left, game-session.updated |
| KickPlayer | POST /sessions/kick | admin | session | game-session.player-left, game-session.updated |
| SendChatMessage | POST /sessions/chat | user, state:in_game | - | - (client events only) |
| PerformGameAction | POST /sessions/actions | user, state:in_game | - | game-session.action.performed |
| JoinGameSessionById | POST /sessions/join-session | [] | session | game-session.player-joined, game-session.updated |
| LeaveGameSessionById | POST /sessions/leave-session | user, state:in_game | session | game-session.player-left, game-session.updated |
| PublishJoinShortcut | POST /sessions/publish-join-shortcut | [] | - | - (client events only) |

---

## Methods

### ListGameSessions
POST /sessions/list | Roles: [admin]

```
READ session-list:session-list -> sessionIds (default empty list)
FOREACH sessionId in sessionIds
  READ session:session:{sessionId} -> model
  // skip null (log warning) and Finished status
RETURN (200, GameSessionListResponse { Sessions, TotalCount })
```

// Note: body.GameType and body.Status filters exist in schema but are not applied in code.

---

### CreateGameSession
POST /sessions/create | Roles: []

```
// Cap maxPlayers at config.MaxPlayersPerSession
// Default sessionType to Lobby if null
IF sessionType == Matchmade AND body.ExpectedPlayers has entries
  // Generate ReservationModel per expected player
  // Each reservation: crypto-random 32-byte base64 token, ExpiresAt from TTL
WRITE session:session:{sessionId} <- GameSessionModel from request
LOCK GameSessionLock:session-list                    -> 409 if fails
  READ session-list:session-list -> sessionIds
  // Add sessionId to list
  WRITE session-list:session-list <- updated list
PUBLISH game-session.created { sessionId, gameType, sessionName, status, maxPlayers, currentPlayers, isPrivate, owner, createdAt, sessionType, gameSettings, reservationExpiresAt }
RETURN (200, GameSessionResponse)
```

// Note: Session is saved BEFORE session-list lock. If lock fails, session record is orphaned.

---

### GetGameSession
POST /sessions/get | Roles: [user, state:in_game]

```
READ session:session:{sessionId}                     -> 404 if null
RETURN (200, GameSessionResponse)
```

---

### JoinGameSession
POST /sessions/join | Roles: []

```
// Validate subscriber session from distributed state
READ subscriber-sessions:subscriber-sessions:{accountId}
  -> 401 if session not found in subscriber sessions
READ lobby:lobby:{gameType}                          -> 404 if null
LOCK GameSessionLock:session:{lobbyId}               -> 409 if fails
  READ session:session:{lobbyId} -> model
  // Validate: not full, not finished, player not already present -> 409
  // Add player to model.Players
  // Status: Waiting->Active on first player; Active->Full at MaxPlayers
  CALL IPermissionClient.UpdateSessionStateAsync { SessionId, NewState = "in_game" }
    // On ApiException: remove player from model, return 500
    // On other exception: remove player, re-throw
  WRITE session:session:{lobbyId} <- updated model
PUBLISH game-session.player-joined { sessionId, accountId }
PUBLISH game-session.updated { sessionId, changedFields: [currentPlayers, status] }
PUSH PlayerJoinedClientEvent to OTHER players' sessions { sessionId, player { accountId, displayName, role, characterData }, currentPlayerCount, maxPlayers }
IF previousStatus != model.Status
  PUSH SessionStateChangedClientEvent to ALL players' sessions { sessionId, previousState, newState, reason = "player_joined", changedBy = accountId }
RETURN (200, JoinGameSessionResponse { SessionId, PlayerRole = Player, GameData })
```

---

### LeaveGameSession
POST /sessions/leave | Roles: [user, state:in_game]

```
READ lobby:lobby:{gameType}                          -> 404 if null
LOCK GameSessionLock:session:{lobbyId}               -> 409 if fails
  READ session:session:{lobbyId} -> model            -> 404 if null
  // Find player by accountId                        -> 404 if not found
  // Remove player from model.Players
  // Status: Full->Active if was full; any->Finished if 0 players
  CALL IPermissionClient.ClearSessionStateAsync { SessionId, ServiceId }
    // Failure tolerated: log error event, continue
  WRITE session:session:{lobbyId} <- updated model
PUBLISH game-session.player-left { sessionId, accountId, kicked = false }
PUBLISH game-session.updated { sessionId, changedFields: [currentPlayers, status] }
PUSH PlayerLeftClientEvent to REMAINING players' sessions { sessionId, playerId, displayName, currentPlayerCount }
IF previousStatus != model.Status
  PUSH SessionStateChangedClientEvent to REMAINING players' sessions { sessionId, previousState, newState, reason = "player_left" }
RETURN (200)
```

---

### KickPlayer
POST /sessions/kick | Roles: [admin]

```
LOCK GameSessionLock:session:{sessionId}             -> 409 if fails
  READ session:session:{sessionId} -> model          -> 404 if null
  // Find target player by targetAccountId           -> 404 if not found
  // Remove player from model.Players
  // Status: Full->Active only (does NOT set Finished when 0 players)
  CALL IPermissionClient.ClearSessionStateAsync { SessionId = player.SessionId, ServiceId }
    // Failure tolerated: log error event, continue
  WRITE session:session:{sessionId} <- updated model
PUBLISH game-session.player-left { sessionId, accountId = targetAccountId, kicked = true, reason }
PUBLISH game-session.updated { sessionId, changedFields: [currentPlayers, status] }
PUSH PlayerKickedClientEvent to REMAINING + KICKED player sessions { sessionId, kickedPlayerId, kickedPlayerName, reason }
IF previousStatus != model.Status
  PUSH SessionStateChangedClientEvent to REMAINING players' sessions { sessionId, previousState, newState, reason = "player_kicked" }
RETURN (200)
```

// Bug: Does not set Finished when last player is kicked. Does not clear in_game permission state.

---

### SendChatMessage
POST /sessions/chat | Roles: [user, state:in_game]

```
READ lobby:lobby:{gameType}                          -> 404 if null
READ session:session:{lobbyId} -> model              -> 404 if null
// Build SessionChatReceivedClientEvent
IF messageType == Whisper AND targetPlayerId has value
  // Find target player in model.Players
  PUSH SessionChatReceivedClientEvent to target session { isWhisperToMe = true }
  PUSH SessionChatReceivedClientEvent to sender session { isWhisperToMe = false }
ELSE
  // Collect all player WebSocket session IDs
  PUSH SessionChatReceivedClientEvent to all player sessions
RETURN (200)
```

// Bug: Does not validate sender is a session member. Non-members can send messages.

---

### PerformGameAction
POST /sessions/actions | Roles: [user, state:in_game]

```
READ lobby:lobby:{gameType}                          -> 404 if null
READ session:session:{lobbyId} -> model              -> 404 if null
IF model.Status == Finished                          -> 400
IF accountId not in model.Players                    -> 403
PUBLISH game-session.action.performed { sessionId, accountId, actionId, actionType, targetId }
RETURN (200, GameActionResponse { ActionId })
```

// No lock, no state mutation. Fire-and-forget event publishing.

---

### JoinGameSessionById
POST /sessions/join-session | Roles: []

```
LOCK GameSessionLock:session:{gameSessionId}         -> 409 if fails
  READ session:session:{gameSessionId} -> model      -> 404 if null
  IF model.SessionType == Matchmade
    // Find reservation for accountId                -> 403 if no reservation
    // Validate token matches                        -> 403 if invalid token
    // Check reservation expiry                      -> 409 if expired
    // Check not already claimed                     -> 409 if already claimed
    // Mark reservation.Claimed = true
  ELSE
    // Check capacity                                -> 409 if full
  // Validate: not finished, not already in session  -> 409
  // Add player to model.Players
  // Status: Waiting->Active, Active->Full
  CALL IPermissionClient.UpdateSessionStateAsync { SessionId = webSocketSessionId, NewState = "in_game" }
    // On ApiException: remove player, un-claim reservation if matchmade, return 500
    // On other exception: same rollback, re-throw
  WRITE session:session:{gameSessionId} <- updated model
PUBLISH game-session.player-joined { sessionId = gameSessionId, accountId }
PUBLISH game-session.updated { sessionId, changedFields: [currentPlayers, status, reservations] }
PUSH PlayerJoinedClientEvent to OTHER players' sessions { sessionId, player { accountId, displayName, role, characterData }, currentPlayerCount, maxPlayers }
IF previousStatus != model.Status
  PUSH SessionStateChangedClientEvent to ALL players' sessions { sessionId, previousState, newState, reason = "player_joined", changedBy = accountId }
RETURN (200, JoinGameSessionResponse { SessionId = gameSessionId, PlayerRole = Player, GameData })
```

---

### LeaveGameSessionById
POST /sessions/leave-session | Roles: [user, state:in_game]

```
LOCK GameSessionLock:session:{gameSessionId}         -> 409 if fails
  READ session:session:{gameSessionId} -> model      -> 404 if null
  // Find player by accountId                        -> 404 if not found
  // Remove player from model.Players
  // Status: Full->Active if was full; any->Finished if 0 players
  IF body.WebSocketSessionId has value
    CALL IPermissionClient.ClearSessionStateAsync { SessionId = webSocketSessionId, ServiceId }
      // Failure tolerated: log error event, continue
  WRITE session:session:{gameSessionId} <- updated model
PUBLISH game-session.player-left { sessionId = gameSessionId, accountId, kicked = false }
PUBLISH game-session.updated { sessionId, changedFields: [currentPlayers, status] }
PUSH PlayerLeftClientEvent to REMAINING players' sessions { sessionId, playerId, displayName, currentPlayerCount }
IF previousStatus != model.Status
  PUSH SessionStateChangedClientEvent to REMAINING players' sessions { sessionId, previousState, newState, reason = "player_left" }
RETURN (200)
```

---

### PublishJoinShortcut
POST /sessions/publish-join-shortcut | Roles: []

```
READ session:session:{gameSessionId}                 -> 404 if null
// Find reservation matching body.ReservationToken   -> 400 if not found
// Generate routeGuid via GuidGenerator with server salt
// Generate targetGuid via GuidGenerator with server salt
// Build pre-bound JoinGameSessionByIdRequest as shortcut payload
PUSH ShortcutPublishedEvent to targetWebSocketSessionId { routeGuid, targetGuid, boundPayload, metadata { name, expiresAt } }
RETURN (200, PublishJoinShortcutResponse { ShortcutRouteGuid })
```

---

## Background Services

### GameSessionStartupService
**Interval**: One-shot, runs once after `config.StartupServiceDelaySeconds` delay
**Purpose**: Bulk-populates `_accountSubscriptions` static cache at startup

```
// For each stubName in config.SupportedGameServices (CSV split):
CALL ISubscriptionClient.QueryCurrentSubscriptionsAsync { StubName = stubName }
FOREACH accountId in response.AccountIds
  // Add to _accountSubscriptions cache
// Failures per service are caught and logged; others continue
```

---

### ReservationCleanupService
**Interval**: `config.CleanupIntervalSeconds` (default 30s)
**Purpose**: Cancel matchmade sessions with expired reservations where not all players claimed

```
READ session-list:session-list -> sessionIds
FOREACH sessionId in sessionIds
  READ session:session:{sessionId} -> session (as CleanupSessionModel)
  // Skip non-Matchmade, skip no ReservationExpiresAt, skip not-yet-expired
  IF expired AND claimedCount < totalReservations
    LOCK GameSessionLock:session:{sessionId}         // skip on failure
      READ session:session:{sessionId}               // re-read under lock
      FOREACH reservation WHERE Claimed
        PUSH SessionCancelledClientEvent to player session { sessionId, reason }
      PUBLISH game-session.cancelled { sessionId, reason }
      READ session:session:{sessionId} -> fullModel  // for lifecycle event
      IF fullModel != null
        PUBLISH game-session.deleted { sessionId, deletedReason }
      DELETE session:session:{sessionId}
      LOCK GameSessionLock:session-list
        READ session-list:session-list
        // Remove sessionId from list
        WRITE session-list:session-list <- updated list
```

---

## Event Handlers (Internal)

### HandleSessionConnectedInternal
**Trigger**: `session.connected` event

```
IF config.GenericLobbiesEnabled AND IsOurService("generic")
  // Store subscriber session (ETag retry loop)
  // Get-or-create generic lobby
  PUSH ShortcutPublishedEvent to session
// Check _accountSubscriptions cache; if miss:
CALL ISubscriptionClient.QueryCurrentSubscriptionsAsync { AccountId }
FOREACH subscribed stubName (filtered to _supportedGameServices, exclude generic if already handled)
  CALL IGameServiceClient.GetServiceAsync { StubName }  // fail-open: true on error
  IF autoLobbyEnabled
    // Store subscriber session (ETag retry loop)
    // Get-or-create lobby for stubName
    PUSH ShortcutPublishedEvent to session
```

---

### HandleSessionDisconnectedInternal
**Trigger**: `session.disconnected` event

```
IF accountId has value
  // Remove session from subscriber-sessions:{accountId} (ETag retry loop)
  // Delete key if no sessions remain
```

---

### HandleSubscriptionUpdatedInternal
**Trigger**: `subscription.updated` event

```
// Update _accountSubscriptions local cache
IF NOT IsOurService(stubName)                        -> return
CALL IConnectClient.GetAccountSessionsAsync { AccountId }
  // Falls back to local subscriber-sessions store on failure
FOREACH connected session
  IF subscription active
    // Store subscriber session
    CALL IGameServiceClient.GetServiceAsync { StubName }  // fail-open
    IF autoLobbyEnabled
      // Get-or-create lobby
      PUSH ShortcutPublishedEvent to session
  ELSE
    PUSH ShortcutRevokedEvent to session             // unconditional of autoLobbyEnabled
```
