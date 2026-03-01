# Matchmaking Plugin Deep Dive

> **Plugin**: lib-matchmaking
> **Schema**: schemas/matchmaking-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFeatures
> **State Store**: matchmaking-statestore (Redis, prefix: `mm`)

---

## Overview

Ticket-based matchmaking (L4 GameFeatures) with skill windows, query matching, party support, and configurable accept/decline flow. A background service processes queues at configurable intervals, expanding skill windows over time until matches form or tickets timeout. On full acceptance, creates a matchmade game session via lib-game-session with reservation tokens and publishes join shortcuts via Connect. Supports immediate match checks on ticket creation, auto-requeue on decline, and pending match state restoration on reconnection.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis persistence for queues, tickets, matches, player indexes, pending matches |
| lib-state (`IDistributedLockProvider`) | Match-level locks for concurrent accept/decline operations |
| lib-messaging (`IMessageBus`) | Publishing matchmaking lifecycle events; error event publishing |
| lib-messaging (`IEventConsumer`) | 3 event subscriptions (session connect/disconnect/reconnect) |
| lib-messaging (`IClientEventPublisher`) | Push queue joined, match found, match confirmed, match cancelled events to WebSocket |
| lib-game-session (`IGameSessionClient`) | Create matchmade game sessions; publish join shortcuts to matched players |
| lib-permission (`IPermissionClient`) | Set/clear `matchmaking:in_queue` and `matchmaking:match_pending` states |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| (none identified) | No services currently call `IMatchmakingClient` in production code |

---

## State Storage

**Store**: `matchmaking-statestore` (Backend: Redis, Prefix: `mm`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `queue:{queueId}` | `QueueModel` | Queue configuration and statistics |
| `queue-list` | `List<string>` | All queue IDs (global index) |
| `ticket:{ticketId}` | `TicketModel` | Individual matchmaking ticket with status, skill, properties |
| `queue-tickets:{queueId}` | `List<Guid>` | Ticket IDs in a specific queue |
| `player-tickets:{accountId}` | `List<Guid>` | Active ticket IDs for a player |
| `match:{matchId}` | `MatchModel` | Formed match with accepted players, deadline, session ID |
| `pending-match:{accountId}` | `Guid` | Pending match ID for reconnection support |

### Type Field Classification

Every polymorphic "type" or "kind" field in the Matchmaking domain falls into one of three categories:

| Field | Model(s) | Cat | Values / Source | Rationale |
|-------|----------|-----|-----------------|-----------|
| `TicketStatus` | `TicketModel`, events | C | `searching`, `match_found`, `match_accepted`, `cancelled`, `expired` | Finite ticket lifecycle states the matchmaking engine manages. Service-owned enum (`TicketStatus`). |
| `CancelReason` | `TicketModel`, cancel events | C | `cancelled_by_user`, `timeout`, `session_disconnected`, `party_disbanded`, `match_declined`, `queue_disabled` | Finite cancellation causes the service recognizes. Service-owned enum (`CancelReason`). |
| `PartySkillAggregation` | `QueueModel` (queue config) | C | `highest`, `average`, `weighted` | Finite aggregation strategies for party skill rating. Service-owned enum (`PartySkillAggregation`). |
| `SessionGameType` | Queue configuration | B | `"generic"` (default), game-specific strings | Game type code used for game session creation. Opaque string; each game service defines its own game types without schema changes. |

**Category key**: **A** = Entity Reference (`EntityType` enum), **B** = Content Code (opaque string, game-configurable), **C** = System State (service-owned enum, finite).

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `matchmaking.ticket-created` | `MatchmakingTicketCreatedEvent` | Player joins queue |
| `matchmaking.ticket-cancelled` | `MatchmakingTicketCancelledEvent` | Ticket cancelled (user, disconnect, timeout, queue disabled) |
| `matchmaking.match-formed` | `MatchmakingMatchFormedEvent` | Match found from ticket pool |
| `matchmaking.match-accepted` | `MatchmakingMatchAcceptedEvent` | All players accepted; game session created |
| `matchmaking.match-declined` | `MatchmakingMatchDeclinedEvent` | Match cancelled due to decline |
| `matchmaking.queue.created` | `MatchmakingQueueCreatedEvent` | New queue created |
| `matchmaking.queue.updated` | `MatchmakingQueueUpdatedEvent` | Queue configuration changed |
| `matchmaking.queue.deleted` | `MatchmakingQueueDeletedEvent` | Queue deleted (all tickets cancelled) |
| `matchmaking.stats` | `MatchmakingStatsEvent` | Periodic stats snapshot (every `StatsPublishIntervalSeconds`) |

### Consumed Events

| Topic | Event Type | Handler |
|-------|-----------|---------|
| `session.connected` | `SessionConnectedEvent` | No-op (players must explicitly join queues) |
| `session.disconnected` | `SessionDisconnectedEvent` | Cancels all tickets associated with the disconnected session |
| `session.reconnected` | `SessionReconnectedEvent` | Restores pending match state; re-sends `MatchFoundEvent` with remaining accept time |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `ServerSalt` | `MATCHMAKING_SERVER_SALT` | dev salt | Shared salt for GUID generation (required, fail-fast) |
| `ProcessingIntervalSeconds` | `MATCHMAKING_PROCESSING_INTERVAL_SECONDS` | `15` | Interval between background match processing cycles |
| `DefaultMaxIntervals` | `MATCHMAKING_DEFAULT_MAX_INTERVALS` | `6` | Max intervals before ticket timeout/relaxation |
| `MaxConcurrentTicketsPerPlayer` | `MATCHMAKING_MAX_CONCURRENT_TICKETS_PER_PLAYER` | `3` | Hard cap on simultaneous tickets per account |
| `DefaultMatchAcceptTimeoutSeconds` | `MATCHMAKING_DEFAULT_MATCH_ACCEPT_TIMEOUT_SECONDS` | `30` | Time for all players to accept a formed match |
| `StatsPublishIntervalSeconds` | `MATCHMAKING_STATS_PUBLISH_INTERVAL_SECONDS` | `60` | Interval between stats event publications |
| `PendingMatchRedisKeyTtlSeconds` | `MATCHMAKING_PENDING_MATCH_REDIS_KEY_TTL_SECONDS` | `300` | TTL for pending match state (reconnection window) |
| `ImmediateMatchCheckEnabled` | `MATCHMAKING_IMMEDIATE_MATCH_CHECK_ENABLED` | `true` | Attempt match immediately on ticket creation |
| `AutoRequeueOnDecline` | `MATCHMAKING_AUTO_REQUEUE_ON_DECLINE` | `true` | Re-create tickets for non-declining players on match decline |
| `BackgroundServiceStartupDelaySeconds` | `MATCHMAKING_BACKGROUND_SERVICE_STARTUP_DELAY_SECONDS` | `5` | Delay before background processing starts |
| `DefaultReservationTtlSeconds` | `MATCHMAKING_DEFAULT_RESERVATION_TTL_SECONDS` | `120` | TTL for game session reservations |
| `DefaultJoinDeadlineSeconds` | `MATCHMAKING_DEFAULT_JOIN_DEADLINE_SECONDS` | `120` | Deadline for players to join after match confirmation |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<MatchmakingService>` | Scoped | Structured logging |
| `MatchmakingServiceConfiguration` | Singleton | All 12 config properties |
| `IStateStoreFactory` | Singleton | Redis state store access |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IEventConsumer` | Scoped | Event handler registration (used in constructor only) |
| `IClientEventPublisher` | Scoped | WebSocket event push (match found, confirmed, cancelled) |
| `IGameSessionClient` | Scoped | Create matchmade sessions; publish join shortcuts |
| `IPermissionClient` | Scoped | Permission state management (in_queue, match_pending) |
| `IDistributedLockProvider` | Singleton | Match-level distributed locks |
| `IMatchmakingAlgorithm` | (inline) | Pure algorithmic functions for matching and skill windows |
| `MatchmakingBackgroundService` | Hosted (BackgroundService) | Periodic queue processing loop |

Service lifetime is **Scoped** (per-request). One BackgroundService for interval processing.

---

## API Endpoints (Implementation Notes)

### Queue Administration (3 endpoints)

- **CreateQueue** (`/matchmaking/queue/create`): Creates queue with skill expansion steps, party config, exclusive groups. Falls back to config defaults for interval/maxIntervals/acceptTimeout when not specified.
- **UpdateQueue** (`/matchmaking/queue/update`): Partial update with ETag-based optimistic concurrency. Returns Conflict on concurrent modification.
- **DeleteQueue** (`/matchmaking/queue/delete`): Cancels ALL tickets in queue first (reason: `Queue_disabled`), then deletes queue and removes from list.

### Queues (2 endpoints)

- **ListQueues** (`/matchmaking/queue/list`): Filters by gameId and enabled status. Includes current ticket count per queue.
- **GetQueue** (`/matchmaking/queue/get`): Simple load-by-ID.

### Matchmaking (3 endpoints)

- **Join** (`/matchmaking/join`): Creates ticket. Validates: queue exists/enabled, concurrent ticket limit, exclusive group conflicts, not already in queue. Calculates party skill rating via aggregation (Highest/Average/Weighted). Sets `in_queue` permission state (rolls back ticket on failure). Publishes shortcuts. Attempts immediate match if enabled.
- **Leave** (`/matchmaking/leave`): Validates ticket ownership. Cancels with `Cancelled_by_user` reason.
- **Status** (`/matchmaking/status`): Returns ticket status, intervals elapsed, current skill range, estimated wait, match ID if formed.

### Match Accept/Decline (2 endpoints)

- **Accept** (`/matchmaking/accept`): Acquires match lock. Validates player in match, deadline not passed. Adds to accepted set. Notifies all players of progress. When all accepted: finalizes match (creates game session, publishes join shortcuts, clears state, publishes `match-accepted`).
- **Decline** (`/matchmaking/decline`): Validates player in match. Cancels match. If `AutoRequeueOnDecline` enabled, re-creates tickets for non-declining players.

### Statistics (1 endpoint)

- **Stats** (`/matchmaking/stats`): Per-queue statistics: current tickets, matches formed last hour, average/median wait, timeout/cancel rates. Optional queue/game ID filter.

---

## Visual Aid

```
Matchmaking Lifecycle
======================

  Player → JoinMatchmaking(queueId, skillRating, properties, query)
       │
       ├── Validate queue, ticket limits, exclusive groups
       ├── Calculate party skill (Highest/Average/Weighted)
       ├── Save ticket (Status: Searching)
       ├── Set permission state: in_queue
       ├── Publish shortcuts (leave, status)
       ├── Client event: QueueJoinedEvent
       └── ImmediateMatchCheck? → TryMatchTickets()
                                     │
       ┌─────────────────────────────┘
       │
  BackgroundService (every ProcessingIntervalSeconds):
       │
       ├── ProcessAllQueuesAsync
       │    │
       │    ├── For each queue:
       │    │    ├── Get all Searching tickets
       │    │    ├── Increment intervalsElapsed
       │    │    ├── GetCurrentSkillRange(queue, intervals)
       │    │    │    └── Walk SkillExpansion steps:
       │    │    │         [intervals=0, range=50]
       │    │    │         [intervals=2, range=100]
       │    │    │         [intervals=4, range=200]
       │    │    │         [intervals=6, range=null (unlimited)]
       │    │    │
       │    │    ├── TryMatchTickets(tickets, queue, skillRange)
       │    │    │    ├── Filter by query compatibility
       │    │    │    ├── Group by CountMultiple
       │    │    │    └── Select MinCount..MaxCount tickets within range
       │    │    │
       │    │    └── Match found? → FormMatchAsync()
       │    │         │
       │    │         ├── Save MatchModel (Status: Pending)
       │    │         ├── Update tickets (Status: Match_found)
       │    │         ├── Store pending-match for reconnection
       │    │         ├── Set permission state: match_pending
       │    │         ├── Client events: MatchFoundEvent + shortcuts
       │    │         └── Publish: matchmaking.match-formed
       │    │
       │    └── Tickets past MaxIntervals? → CancelTicketInternalAsync(Timeout)
       │
       └── Publish stats (every StatsPublishIntervalSeconds)


Match Accept/Decline Flow
===========================

  MatchFoundEvent → Client shows "Accept/Decline" UI
       │
       ├── AcceptMatch (lock: matchmaking-match:{matchId})
       │    ├── Add to AcceptedPlayers set
       │    ├── Notify all: MatchPlayerAcceptedEvent
       │    └── All accepted? → FinalizeMatchAsync()
       │         │
       │         ├── CreateGameSession(Matchmade, reservations)
       │         ├── PublishJoinShortcut per player
       │         ├── Client events: MatchConfirmedEvent
       │         ├── Clear permission state
       │         ├── Cleanup tickets
       │         └── Publish: matchmaking.match-accepted
       │
       └── DeclineMatch
            ├── CancelMatchAsync (Status: Cancelled)
            ├── Notify all: MatchCancelledEvent
            ├── AutoRequeueOnDecline?
            │    └── Re-create tickets for non-declining players
            └── Publish: matchmaking.match-declined


Reconnection Support
=====================

  session.disconnected → Cancel tickets for THIS session only
  session.reconnected  → Check pending-match:{accountId}
       │
       └── Pending match exists + Status=Pending?
            ├── Update ticket with new session ID
            └── Re-send MatchFoundEvent with remaining accept time
```

---

## Stubs & Unimplemented Features

1. **Queue statistics are placeholder**: `MatchesFormedLastHour`, `AverageWaitSeconds`, `MedianWaitSeconds`, `TimeoutRatePercent`, `CancelRatePercent` fields exist on `QueueModel` but are not actively computed or updated during match processing. The `matchmaking.stats` event sets `MatchesFormedSinceLastSnapshot = 0` with a TODO comment.
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/225 -->
2. **Tournament support declared but minimal**: `TournamentIdRequired` and `TournamentId` on tickets exist as fields but no tournament-specific matching logic is implemented.
3. **MatchmakingTicketUpdatedEvent defined but never published**: The events schema defines `MatchmakingTicketUpdatedEvent` but the service never publishes to `matchmaking.ticket-updated`. Only created and cancelled events fire.
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/225 -->

---

## Potential Extensions

1. **Skill rating integration**: Fetch skill ratings from Analytics service (Glicko-2) instead of requiring them in the join request.
2. **Queue scheduling**: Enable/disable queues on time-based schedules (peak hours only, weekend tournaments).
3. **Match quality scoring**: Track match quality metrics (skill spread, wait time) for queue tuning.
4. **Cross-queue matching**: Allow tickets in multiple queues to be matched together (first-match-wins).

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. **Reconnection does not republish accept/decline shortcuts**: When a player reconnects to a pending match via `HandleSessionReconnectedAsync`, the service sends `MatchFoundEvent` but does NOT call `PublishMatchShortcutsAsync`. The player receives the match notification but has no pre-bound shortcuts to accept or decline - they must manually call the API endpoints with the full request body.

### Intentional Quirks

1. **Disconnect cancels tickets per-session**: When a session disconnects, only tickets with matching `WebSocketSessionId` are cancelled. A player with tickets from multiple sessions only loses the disconnected session's tickets.

2. **Reconnection restores pending matches only**: Reconnection checks for `pending-match:{accountId}` (match in accept phase). It does NOT restore `Searching` tickets - those are cancelled on disconnect.

3. **Auto-requeue reuses existing tickets**: When `AutoRequeueOnDecline=true` and a match is declined, non-declining players have their EXISTING tickets reset to `Status: Searching` with `MatchId: null`. The tickets keep their original IDs and `IntervalsElapsed` values - intervals are NOT reset.

4. **Lock owner is random GUID per call**: Each lock acquisition uses `Guid.NewGuid().ToString()` as the owner. The same service instance cannot extend or re-acquire its own lock.

5. **Reconnection updates ticket session ID silently**: The ticket's `WebSocketSessionId` is updated to the new session on reconnection. No event published for this session ID change.

### Design Considerations

1. **Queue ticket lists grow unbounded**: `queue-tickets:{queueId}` accumulates ticket IDs. Cancelled/completed tickets are removed individually, but during processing, all IDs are loaded to check status.

2. **No rate limiting on queue joins**: A player can rapidly join and leave queues up to `MaxConcurrentTicketsPerPlayer` times with no cooldown. Could be exploited to game skill windows.

3. **Stats not computed in real-time**: Queue statistics (AverageWaitSeconds, etc.) are stored fields that would need active computation. Currently they remain at default values unless manually set.

4. **Algorithm is internal and inline**: `MatchmakingAlgorithm` is constructed directly in the service constructor (not DI-registered). `InternalsVisibleTo` enables unit testing, but integration tests can't substitute it.

5. **ListQueues loads all queue IDs then fetches each**: `ListQueuesAsync` loads all queue IDs from `queue-list`, then iterates and loads each queue individually. No batch loading. With many queues, this is N+1 database calls.

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow and should not be manually edited except to add new tracking markers.

*No items currently being tracked.*
