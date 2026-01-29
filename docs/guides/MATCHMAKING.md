# Matchmaking and Game Sessions Developer Guide

This guide covers the Matchmaking Service (`lib-matchmaking`) and its integration with Game Sessions (`lib-game-session`) for competitive and casual multiplayer game matching.

## Architecture Overview

Bannou's matchmaking system uses a **ticket-based queue architecture** with configurable skill matching:

```
Player → Join Queue → Ticket Created → Background Processing → Match Formed
                                              ↓
                            Game Session Created with Reservations
                                              ↓
                                Players Join via Reservation Tokens
```

Key architectural decisions:
- **Queue-based organization**: Pre-defined queues with full configuration
- **Ticket system**: UUID-identified matchmaking requests with properties
- **Immediate + interval matching**: Quick matches form instantly, complex matches optimized
- **Skill window expansion**: Configurable expansion curves per queue
- **Reservation system**: Matchmade sessions have reserved slots with tokens
- **Event-driven**: All state changes publish typed events

## Core Concepts

### Tickets

A ticket represents a player (or party) waiting for a match. Tickets contain:
- Player/party information
- Queue-specific properties
- Skill rating (fetched from lib-analytics)
- Interval counter for timeout/expansion tracking

### Queues

Queues are pre-configured matchmaking pools. Each queue defines:
- Player count requirements (min/max)
- Skill matching behavior
- Party size limits
- Exclusive group membership
- Match accept timeouts

### Skill Expansion

As players wait longer, the skill window expands to prioritize finding a match over perfect skill matching:

```yaml
skillExpansion:
  - intervals: 0, range: 50     # First interval: ±50 rating
  - intervals: 2, range: 150    # After 2 intervals: ±150
  - intervals: 4, range: 400    # After 4 intervals: ±400
  - intervals: 6, range: null   # After 6 intervals: any skill
```

## API Endpoints

All endpoints use POST-only design per FOUNDATION TENETS (zero-copy WebSocket routing).

### Queue Management (Admin)

#### Create Queue
```
POST /matchmaking/queue/create
```

Creates a new matchmaking queue with full configuration.

**Request Body:**
```json
{
  "queueId": "ranked-1v1",
  "gameId": "my-game",
  "sessionGameType": "my-game",
  "displayName": "Ranked Duel",
  "minCount": 2,
  "maxCount": 2,
  "countMultiple": 2,
  "intervalSeconds": 15,
  "maxIntervals": 6,
  "skillExpansion": [
    { "intervals": 0, "range": 50 },
    { "intervals": 2, "range": 100 },
    { "intervals": 4, "range": 200 },
    { "intervals": 6, "range": null }
  ],
  "partySkillAggregation": "highest",
  "useSkillRating": true,
  "ratingCategory": "my-game-duel"
}
```

#### Update Queue
```
POST /matchmaking/queue/update
```

Modify queue configuration. Active tickets continue with previous settings.

#### Delete Queue
```
POST /matchmaking/queue/delete
```

Remove a queue. Active tickets are cancelled with reason `queue_disabled`.

### Queue Discovery (User)

#### List Queues
```
POST /matchmaking/queue/list
```

**Request Body:**
```json
{
  "gameId": "my-game",
  "includeDisabled": false
}
```

**Response:**
```json
{
  "queues": [
    {
      "queueId": "ranked-1v1",
      "gameId": "my-game",
      "displayName": "Ranked Duel",
      "enabled": true,
      "minCount": 2,
      "maxCount": 2,
      "currentTickets": 47,
      "averageWaitSeconds": 12.5
    }
  ]
}
```

#### Get Queue Details
```
POST /matchmaking/queue/get
```

Returns full configuration including skill expansion steps.

### Matchmaking Flow (User)

#### Join Queue
```
POST /matchmaking/join
```

**Request Body:**
```json
{
  "queueId": "ranked-1v1",
  "accountId": "550e8400-e29b-41d4-a716-446655440000",
  "sessionId": "websocket-session-id",
  "partyId": null,
  "properties": {
    "region": "us-west",
    "preferredMaps": ["arena", "duel_pit"]
  }
}
```

**Response:**
```json
{
  "ticketId": "ticket-uuid",
  "queueId": "ranked-1v1",
  "joinedAt": "2026-01-10T12:00:00Z",
  "estimatedWaitSeconds": 15
}
```

After joining, the player receives prebound shortcuts for `/matchmaking/leave` and `/matchmaking/status`.

#### Leave Queue
```
POST /matchmaking/leave
```

Available only after joining (state-based permission).

**Request Body:**
```json
{
  "ticketId": "ticket-uuid"
}
```

#### Get Status
```
POST /matchmaking/status
```

Available only after joining. Returns current position and wait time.

#### Accept Match
```
POST /matchmaking/accept
```

Available when match is formed (state: `match_pending`).

#### Decline Match
```
POST /matchmaking/decline
```

Declining cancels the match for all players. If `autoRequeueOnDecline` is enabled, non-declining players are automatically requeued.

## Match Flow

```
1. Player joins queue
   └─ Ticket created in Redis
   └─ Session state: matchmaking=in_queue
   └─ Shortcuts published: /leave, /status

2. Background processing (every N seconds)
   └─ Increment ticket intervals
   └─ Expand skill windows per queue config
   └─ Attempt match formation
   └─ Handle timeouts

3. Match formed
   └─ Game session created via lib-game-session
   └─ Reservation tokens generated for each player
   └─ Session state: matchmaking=match_pending
   └─ MatchFoundEvent pushed to all players
   └─ Shortcuts published: /accept, /decline

4. All players accept
   └─ Session state cleared
   └─ JoinShortcut published with reservation token
   └─ Players join game session

5. Player declines (or timeout)
   └─ MatchCancelledEvent to all players
   └─ Non-declining players requeued (if enabled)
   └─ Game session cancelled
```

## Game Session Integration

### Session Types

The game-session service supports two session types:

| Type | Description | Use Case |
|------|-------------|----------|
| `lobby` | Persistent, open join | Casual hangout spaces |
| `matchmade` | Reserved slots, time-limited | Competitive matches |

### Reservation System

Matchmade sessions use reservations to control who can join:

```json
{
  "sessionType": "matchmade",
  "expectedPlayers": ["player-1-uuid", "player-2-uuid"],
  "reservationTtlSeconds": 60,
  "reservations": [
    {
      "accountId": "player-1-uuid",
      "token": "reservation-token-1",
      "claimed": false,
      "expiresAt": "2026-01-10T12:01:00Z"
    }
  ]
}
```

Players join using their reservation token:
```
POST /sessions/join-session
{
  "targetSessionId": "game-session-uuid",
  "reservationToken": "reservation-token-1"
}
```

### Reservation Cleanup

The `ReservationCleanupService` background service handles expired reservations:
- Runs every `CleanupIntervalSeconds` (default: 30)
- Finds matchmade sessions with expired reservations
- Cancels sessions where not enough players claimed
- Notifies remaining players via `SessionCancelledEvent`
- Cleans up orphaned session state

## Queue Configuration Presets

### Ranked 1v1

Tight skill matching with gradual expansion:

```yaml
queueId: ranked-1v1
gameId: my-game
sessionGameType: my-game
displayName: Ranked Duel
minCount: 2
maxCount: 2
countMultiple: 2
intervalSeconds: 15
maxIntervals: 6  # 90 seconds max wait

skillExpansion:
  - intervals: 0
    range: 50
  - intervals: 2
    range: 100
  - intervals: 4
    range: 200
  - intervals: 6
    range: null

partySkillAggregation: highest
allowConcurrent: true
exclusiveGroup: ranked
useSkillRating: true
ratingCategory: my-game-duel
```

### Team Competitive (5v5)

Full team matching with longer wait tolerance:

```yaml
queueId: competitive-5v5
gameId: my-game
sessionGameType: my-game
displayName: Competitive 5v5
minCount: 10
maxCount: 10
countMultiple: 5
intervalSeconds: 20
maxIntervals: 9  # 3 minutes max wait

skillExpansion:
  - intervals: 0
    range: 100
  - intervals: 3
    range: 200
  - intervals: 6
    range: 400
  - intervals: 9
    range: null

partySkillAggregation: highest
partyMaxSize: 5
allowConcurrent: true
exclusiveGroup: competitive
useSkillRating: true
ratingCategory: my-game-team
```

### Casual Quick Play

No skill filtering, fast matches:

```yaml
queueId: casual-quickplay
gameId: my-game
sessionGameType: my-game
displayName: Quick Play
minCount: 4
maxCount: 8
countMultiple: 1
intervalSeconds: 10
maxIntervals: 3  # 30 seconds max wait

skillExpansion:
  - intervals: 0
    range: null  # No skill filtering

partySkillAggregation: average
partyMaxSize: 4
allowConcurrent: true
exclusiveGroup: null
useSkillRating: false
```

### Battle Royale

Large player pools, starts when minimum reached:

```yaml
queueId: battle-royale-solo
gameId: my-game
sessionGameType: my-game
displayName: Battle Royale
minCount: 20
maxCount: 100
countMultiple: 1
intervalSeconds: 30
maxIntervals: 4  # 2 minutes, then start with whoever we have

skillExpansion:
  - intervals: 0
    range: 200
  - intervals: 2
    range: null

partySkillAggregation: highest
partyMaxSize: 1  # Solo only
allowConcurrent: false
exclusiveGroup: null
useSkillRating: true
ratingCategory: my-game-br
startWhenMinimumReached: true
```

### Casual Mini-Game (e.g., Mahjong)

Fixed player count, no skill:

```yaml
queueId: mahjong-casual
gameId: my-game-minigames
sessionGameType: generic
displayName: Mahjong
minCount: 4
maxCount: 4
countMultiple: 4
intervalSeconds: 15
maxIntervals: 8  # 2 minutes

skillExpansion:
  - intervals: 0
    range: null

partySkillAggregation: average
partyMaxSize: 4
allowConcurrent: true
exclusiveGroup: minigames
useSkillRating: false
```

### Tournament

Extended wait for seeded matches:

```yaml
queueId: tournament-bracket
gameId: my-game
sessionGameType: my-game
displayName: Tournament Match
minCount: 2
maxCount: 2
countMultiple: 2
intervalSeconds: 60
maxIntervals: 10  # 10 minutes for tournament

skillExpansion:
  - intervals: 0
    range: null  # Tournaments use seeding, not live skill

partySkillAggregation: highest
allowConcurrent: false
exclusiveGroup: null
useSkillRating: false
requiresRegistration: true
tournamentIdRequired: true
```

## Configuration Reference

### Matchmaking Service

| Variable | Default | Description |
|----------|---------|-------------|
| `MATCHMAKING_ENABLED` | `true` | Enable/disable service |
| `MATCHMAKING_SERVER_SALT` | (required) | Server salt for GUID generation |
| `MATCHMAKING_PROCESSING_INTERVAL_SECONDS` | `15` | Default interval between processing cycles |
| `MATCHMAKING_DEFAULT_MAX_INTERVALS` | `6` | Default max intervals before timeout |
| `MATCHMAKING_MAX_CONCURRENT_TICKETS_PER_PLAYER` | `3` | Max concurrent tickets per player |
| `MATCHMAKING_DEFAULT_MATCH_ACCEPT_TIMEOUT_SECONDS` | `30` | Time to accept formed match |
| `MATCHMAKING_STATS_PUBLISH_INTERVAL_SECONDS` | `60` | Stats event publication interval |
| `MATCHMAKING_PENDING_MATCH_REDIS_KEY_TTL_SECONDS` | `300` | TTL for pending match data |
| `MATCHMAKING_IMMEDIATE_MATCH_CHECK_ENABLED` | `true` | Enable quick match on ticket creation |
| `MATCHMAKING_AUTO_REQUEUE_ON_DECLINE` | `true` | Auto-requeue non-declining players |
| `MATCHMAKING_BACKGROUND_SERVICE_STARTUP_DELAY_SECONDS` | `5` | Delay before processing starts |
| `MATCHMAKING_DEFAULT_RESERVATION_TTL_SECONDS` | `120` | Reservation TTL for game sessions |
| `MATCHMAKING_DEFAULT_JOIN_DEADLINE_SECONDS` | `120` | Deadline for players to join session |

### Game Session Service

| Variable | Default | Description |
|----------|---------|-------------|
| `GAME_SESSION_ENABLED` | `true` | Enable/disable service |
| `GAME_SESSION_SERVER_SALT` | (required) | Server salt for GUID generation |
| `GAME_SESSION_MAX_PLAYERS_PER_SESSION` | `16` | Max players per session |
| `GAME_SESSION_DEFAULT_SESSION_TIMEOUT_SECONDS` | `7200` | Session timeout |
| `GAME_SESSION_DEFAULT_RESERVATION_TTL_SECONDS` | `60` | Default reservation TTL |
| `GAME_SESSION_DEFAULT_LOBBY_MAX_PLAYERS` | `100` | Max players for lobbies |
| `GAME_SESSION_CLEANUP_INTERVAL_SECONDS` | `30` | Reservation cleanup interval |
| `GAME_SESSION_CLEANUP_SERVICE_STARTUP_DELAY_SECONDS` | `10` | Cleanup service startup delay |
| `GAME_SESSION_STARTUP_SERVICE_DELAY_SECONDS` | `2` | Subscription cache init delay |
| `GAME_SESSION_SUPPORTED_GAME_SERVICES` | `generic` | Comma-separated game service names |

## Events

### Server Events (lib-messaging)

| Topic | Event | Description |
|-------|-------|-------------|
| `matchmaking.ticket-created` | `MatchmakingTicketCreatedEvent` | Ticket created |
| `matchmaking.ticket-cancelled` | `MatchmakingTicketCancelledEvent` | Ticket cancelled (with reason) |
| `matchmaking.match-formed` | `MatchmakingMatchFormedEvent` | Match successfully formed |
| `matchmaking.match-accepted` | `MatchmakingMatchAcceptedEvent` | All players accepted |
| `matchmaking.match-cancelled` | `MatchmakingMatchCancelledEvent` | Match cancelled (decline/timeout) |
| `matchmaking.stats` | `MatchmakingStatsEvent` | Queue statistics (basic counts; detailed metrics like avg wait time are placeholder) |
| `game-session.session-cancelled` | `SessionCancelledServerEvent` | Session cancelled |

### Client Events (WebSocket Push)

| Event | Description |
|-------|-------------|
| `matchmaking.match_found` | Match formed, waiting for accept |
| `matchmaking.match_confirmed` | All players accepted, join game |
| `matchmaking.match_cancelled` | Match cancelled (reason provided) |
| `matchmaking.ticket_cancelled` | Ticket removed (timeout/queue disabled) |
| `game-session.session_cancelled` | Game session cancelled |

### Cancel Reason Codes

| Code | Description |
|------|-------------|
| `cancelled_by_user` | Player called `/matchmaking/leave` |
| `timeout` | Max intervals exceeded |
| `session_disconnected` | WebSocket dropped, no reconnect |
| `party_disbanded` | Party dissolved during matching |
| `match_declined` | Someone declined the match |
| `queue_disabled` | Admin disabled the queue |

## Exclusive Groups

Exclusive groups prevent players from being in multiple conflicting queues:

```yaml
# Can't be in two arena queues simultaneously
arena-1v1:
  exclusiveGroup: arena
arena-2v2:
  exclusiveGroup: arena

# Minigames can run alongside arena
mahjong-casual:
  exclusiveGroup: minigames
poker-casual:
  exclusiveGroup: minigames

# Tournament is exclusive to itself only
tournament:
  allowConcurrent: false
  exclusiveGroup: null
```

## Party Skill Aggregation

When parties queue together, their combined skill is calculated based on the queue configuration:

| Method | Calculation | Use Case |
|--------|-------------|----------|
| `highest` | Max skill in party | Anti-smurf for competitive |
| `average` | Mean of all skills | Casual/social play |
| `weighted` | Custom weights | Advanced balancing |

**Weighted example:**
```yaml
partySkillAggregation: weighted
partySkillWeights: [0.7, 0.2, 0.1]  # 70% highest, 20% second, 10% third
```

## Testing

### HTTP Tests

Located in `http-tester/Matchmaking/MatchmakingTestHandler.cs`:
- Queue CRUD operations
- Join/leave flow
- Status retrieval
- Match formation simulation

### WebSocket Tests

Located in `edge-tester/Matchmaking/MatchmakingWebSocketTestHandler.cs`:
- Full matchmaking flow via WebSocket
- Client event reception
- Shortcut-based API calls

### Running Tests

```bash
# Unit tests
make test

# HTTP integration tests
make test-http

# WebSocket edge tests
make test-edge

# All tests
make all
```

## Common Patterns

### Checking Queue Status Before Join

```typescript
// List available queues
const queues = await client.listQueues({ gameId: "my-game" });

// Find suitable queue
const rankedQueue = queues.find(q => q.queueId === "ranked-1v1");

// Check wait time
if (rankedQueue.averageWaitSeconds < 30) {
  await client.joinQueue({ queueId: "ranked-1v1", ... });
}
```

### Handling Match Events

```typescript
// Subscribe to match events
ws.on("matchmaking.match_found", (event) => {
  showMatchFoundUI(event.players, event.timeoutSeconds);
});

ws.on("matchmaking.match_confirmed", (event) => {
  // Join the game session
  joinGameSession(event.sessionId, event.reservationToken);
});

ws.on("matchmaking.match_cancelled", (event) => {
  showCancellationReason(event.reason);
  // Will be auto-requeued if configured
});
```

### Graceful Disconnect Handling

The matchmaking service handles disconnects gracefully:
1. Player disconnects during queue wait
2. Pending match data stored in Redis (TTL: 5 minutes)
3. On reconnect, check for pending match
4. If found, re-push MatchFoundEvent
5. Player can continue match flow

This is handled automatically by subscribing to `session.reconnected` events.
