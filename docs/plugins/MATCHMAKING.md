# Matchmaking Plugin Deep Dive

> **Plugin**: lib-matchmaking
> **Schema**: schemas/matchmaking-api.yaml
> **Version**: 1.0.0
> **State Store**: matchmaking-statestore (Redis, prefix: `mm`)

---

## Overview

Ticket-based matchmaking with skill windows, query matching, party support, and configurable accept/decline flow. Players join queues by creating tickets with optional skill ratings, string/numeric properties, and query filters. A background service processes queues at configurable intervals, expanding skill windows over time until matches form or tickets timeout. Formed matches enter an accept/decline phase with configurable deadline. On full acceptance, creates a matchmade game session via `IGameSessionClient` with reservation tokens and publishes join shortcuts. Supports immediate match checks on ticket creation, auto-requeue on decline, and pending match state restoration on reconnection.

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
| `matchmaking.queue-created` | `MatchmakingQueueCreatedEvent` | New queue created |
| `matchmaking.queue-updated` | `MatchmakingQueueUpdatedEvent` | Queue configuration changed |
| `matchmaking.queue-deleted` | `MatchmakingQueueDeletedEvent` | Queue deleted (all tickets cancelled) |

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
| `IEventConsumer` | Scoped | Event handler registration |
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

1. **Queue statistics are placeholder**: `MatchesFormedLastHour`, `AverageWaitSeconds`, `MedianWaitSeconds`, `TimeoutRatePercent`, `CancelRatePercent` fields exist on `QueueModel` but are not actively computed or updated during match processing.
2. **Tournament support declared but minimal**: `TournamentIdRequired` and `TournamentId` on tickets exist as fields but no tournament-specific matching logic is implemented.

---

## Potential Extensions

1. **Skill rating integration**: Fetch skill ratings from Analytics service (Glicko-2) instead of requiring them in the join request.
2. **Queue scheduling**: Enable/disable queues on time-based schedules (peak hours only, weekend tournaments).
3. **Match quality scoring**: Track match quality metrics (skill spread, wait time) for queue tuning.
4. **Cross-queue matching**: Allow tickets in multiple queues to be matched together (first-match-wins).

---

## Tenet Violations (Fix Immediately)

### FOUNDATION TENETS

#### T6: Missing Constructor Null Checks
**File**: `plugins/lib-matchmaking/MatchmakingService.cs`, lines 81-88
**Issue**: Constructor parameters are assigned directly without `ArgumentNullException.ThrowIfNull` or `?? throw new ArgumentNullException(...)` guards. All injected dependencies (`messageBus`, `stateStoreFactory`, `logger`, `configuration`, `clientEventPublisher`, `gameSessionClient`, `permissionClient`, `lockProvider`) lack null validation.
**Fix**: Add `?? throw new ArgumentNullException(nameof(param))` for each dependency, or use `ArgumentNullException.ThrowIfNull(param, nameof(param))` per the T6 service class pattern.

### IMPLEMENTATION TENETS

#### T7: Missing ApiException Catch Blocks
**File**: `plugins/lib-matchmaking/MatchmakingService.cs`, ALL public endpoint methods (lines 153, 183, 270, 348, 407, 558, 611, 654, 700, 802, 844, 900)
**Issue**: Every endpoint method catches only `Exception` generically. None distinguish between `ApiException` (expected API errors from downstream service calls to `IGameSessionClient`, `IPermissionClient`) and unexpected `Exception`. Per T7, `ApiException` should be caught separately, logged as Warning, and return the propagated status code.
**Fix**: Add `catch (ApiException ex)` before the generic `catch (Exception ex)` in each method that calls external service clients. Log at Warning level and propagate the status code.

#### T7: Empty Catch Blocks Swallowing Exceptions
**File**: `plugins/lib-matchmaking/MatchmakingService.cs`, lines 1254 and 1307
**Issue**: `catch { /* Ignore */ }` swallows all exceptions silently when calling `_permissionClient.ClearSessionStateAsync` and `_permissionClient.UpdateSessionStateAsync`. While the intent (don't block main flow) is acceptable, the catch should at minimum log at Debug level so failures are traceable. A completely silent catch hides infrastructure failures.
**Fix**: Change to `catch (Exception ex) { _logger.LogDebug(ex, "Failed to update/clear permission state for ..."); }` to maintain traceability.

#### T7: Bare Catch Without Exception Type in Algorithm
**File**: `plugins/lib-matchmaking/Helpers/MatchmakingAlgorithm.cs`, line 125
**Issue**: `catch { return true; }` uses a bare catch without specifying the exception type. This swallows all exceptions including `OutOfMemoryException`, `StackOverflowException`, etc. Even for a "best effort" parse, this should catch only expected exceptions.
**Fix**: Change to `catch (Exception) { return true; }` at minimum, or preferably `catch (FormatException) { return true; }` to only catch parse-related exceptions.

#### T9: Plain Dictionary in Internal POCO
**File**: `plugins/lib-matchmaking/MatchmakingService.cs`, lines 2007-2008
**Issue**: `TicketModel` uses `Dictionary<string, string>` and `Dictionary<string, double>` for `StringProperties` and `NumericProperties`. Per T9, local mutable state should use `ConcurrentDictionary`. While these are serialized POCOs, during the matching algorithm multiple tickets may be accessed concurrently from the background service and from API requests simultaneously.
**Fix**: Consider if these POCOs are truly accessed concurrently. If state store serialization handles them (they are deserialized fresh each time), this may be acceptable. However, if any code path mutates these dictionaries after initial creation, they should be `ConcurrentDictionary`. Document the thread-safety assumption if keeping plain Dictionary.

#### T10: Excessive LogInformation for Operation Entry
**File**: `plugins/lib-matchmaking/MatchmakingService.cs`, lines 116, 173, 203, 290, 368, 435, 634, 724, 825
**Issue**: Per T10, "Operation Entry" should be logged at Debug level. Many endpoint entry points log at `LogInformation` (e.g., "Listing matchmaking queues", "Getting queue {QueueId}", "Updating queue {QueueId}", "Player {AccountId} joining queue {QueueId}"). T10 specifies: "Operation Entry (Debug): Log input parameters". Information level is for "Business Decisions (Information): Significant state changes".
**Fix**: Change operation entry logs to `LogDebug`. Keep `LogInformation` only for significant state change confirmations (e.g., "Queue {QueueId} created successfully" at line 267 is correct).

#### T23: Non-Async Task-Returning Method
**File**: `plugins/lib-matchmaking/MatchmakingServiceEvents.cs`, lines 41-48
**Issue**: `HandleSessionConnectedAsync` returns `Task.CompletedTask` without using the `async` keyword. Per T23, all Task-returning methods must be `async` with at least one `await`. The correct pattern for synchronous implementations of async interfaces is `async Task` with `await Task.CompletedTask`.
**Fix**: Change to:
```csharp
public async Task HandleSessionConnectedAsync(SessionConnectedEvent evt)
{
    _logger.LogDebug("Session {SessionId} connected, account {AccountId}",
        evt.SessionId, evt.AccountId);
    await Task.CompletedTask;
}
```

### QUALITY TENETS

#### T19: Missing XML Documentation on Internal Models
**File**: `plugins/lib-matchmaking/MatchmakingService.cs`, lines 1990-1993, 2018-2023, 2054-2062
**Issue**: Several internal classes lack `<summary>` XML documentation: `SkillExpansionStepModel` (line 1990), `PartyMemberModel` (line 2018), `MatchedTicketModel` (line 2054). While internal, T19 requires documentation on all public AND internal types for maintainability.
**Fix**: Add `/// <summary>` tags to `SkillExpansionStepModel`, `PartyMemberModel`, and `MatchedTicketModel`.

#### T19: Missing Parameter Documentation on Constructor
**File**: `plugins/lib-matchmaking/MatchmakingService.cs`, lines 67-79
**Issue**: The constructor has `/// <summary>` but no `<param>` tags for any of its 9 parameters. Per T19, all method parameters should have `<param>` documentation.
**Fix**: Add `<param name="...">` for each constructor parameter.

### CLAUDE.md RULES

#### Unjustified `?? string.Empty` Usage
**File**: `plugins/lib-matchmaking/MatchmakingService.cs`, line 324
**Issue**: `etag ?? string.Empty` is used when passing the etag to `TrySaveAsync`. The etag was just retrieved from `GetWithETagAsync` which returns `string?`. If etag is null, it means the record was just created or there's a data issue - silently coercing to empty string hides this. Per CLAUDE.md, `?? string.Empty` requires either compiler satisfaction (documented that null is impossible) or external service defensive coding (with error logging).
**Fix**: Either add a comment explaining why the null-coalesce is safe (the record was just loaded successfully so etag should be non-null), or throw if etag is null since it indicates a programming error (record exists but has no etag).

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None identified.

### Intentional Quirks (Documented Behavior)

1. **ServerSalt required (fail-fast)**: Constructor throws `InvalidOperationException` if `ServerSalt` is empty. All instances must share the same salt for consistent shortcut GUID generation.

2. **Immediate match check on join**: When `ImmediateMatchCheckEnabled=true`, newly created tickets immediately try to form a match (with no skill expansion). This provides instant matches when enough compatible players are already waiting.

3. **Exclusive groups prevent dual-queuing**: If two queues share an `ExclusiveGroup`, a player can only be in one at a time. Prevents exploiting overlapping queues.

4. **Party skill aggregation**: Three modes for multi-player parties: `Highest` (uses max rating), `Average` (mean), `Weighted` (configurable per-position weights). Default fallback is Average.

5. **Disconnect cancels tickets per-session**: When a session disconnects, only tickets with matching `WebSocketSessionId` are cancelled. A player with tickets from multiple sessions only loses the disconnected session's tickets.

6. **Reconnection restores pending matches only**: Reconnection checks for `pending-match:{accountId}` (match in accept phase). It does NOT restore `Searching` tickets - those are cancelled on disconnect.

7. **Background service creates scoped service**: `MatchmakingBackgroundService` creates a new DI scope per processing cycle via `IServiceProvider.CreateScope()`. This correctly handles the scoped lifetime of `MatchmakingService`.

8. **Auto-requeue creates new tickets**: When `AutoRequeueOnDecline=true` and a match is declined, non-declining players get new tickets (new IDs, reset intervals). Their original tickets are cleaned up.

9. **Match lock timeout 30 seconds**: Accept/decline operations use a 30-second distributed lock on the match. Shorter than game-session's 60-second locks because accept operations are simpler.

### Design Considerations (Requires Planning)

1. **Queue ticket lists grow unbounded**: `queue-tickets:{queueId}` accumulates ticket IDs. Cancelled/completed tickets are removed individually, but during processing, all IDs are loaded to check status.

2. **No rate limiting on queue joins**: A player can rapidly join and leave queues up to `MaxConcurrentTicketsPerPlayer` times with no cooldown. Could be exploited to game skill windows.

3. **Stats not computed in real-time**: Queue statistics (AverageWaitSeconds, etc.) are stored fields that would need active computation. Currently they remain at default values unless manually set.

4. **Algorithm is internal and inline**: `MatchmakingAlgorithm` is constructed directly in the service constructor (not DI-registered). `InternalsVisibleTo` enables unit testing, but integration tests can't substitute it.

5. **Permission state cleanup on match finalization**: Each matched player's permission state is cleared individually with separate try-catch. A failure clearing one player's state doesn't prevent others from being cleared.

6. **Pending match TTL provides reconnection window**: `PendingMatchRedisKeyTtlSeconds=300` (5 minutes) defines how long after disconnect a player can reconnect and resume the accept flow. After TTL expiry, the match may timeout independently.

7. **Index locks are 15 seconds, match locks are 30 seconds**: Queue list operations (add/remove from queue-list, queue-tickets) use 15-second locks (lines 1515, 1534, 1657, 1677), while match accept/decline uses 30-second locks (line 728). Different timeouts for different operation complexities.

8. **Lock owner is random GUID per call**: Like game-session, each lock acquisition uses `Guid.NewGuid().ToString()` as the owner. The same service instance cannot extend or re-acquire its own lock.

9. **ListQueues loads all queue IDs then fetches each**: `ListQueuesAsync` (lines 119-149) loads all queue IDs from `queue-list`, then iterates and loads each queue individually. No batch loading. With many queues, this is N+1 database calls.

10. **Reconnection re-sends MatchFoundEvent with remaining time**: When a player reconnects with a pending match (lines 146-158), they receive a new `MatchFoundEvent` with `AcceptTimeoutSeconds` calculated as the remaining time until deadline. Negative values are clamped to 0 (`Math.Max(0, ...)`).

11. **Reconnection updates ticket session ID silently**: Lines 135-140 update the ticket's `WebSocketSessionId` to the new session on reconnection. No event published for this session ID change.

12. **Disconnect handler returns early without logging for unauthenticated sessions**: Line 58-63 - if `AccountId` is null (unauthenticated connection), cleanup is skipped with only a debug log. No warning for potential missed cleanup.

13. **Session.connected is a no-op**: `HandleSessionConnectedAsync` (lines 41-48) does nothing - just logs at debug level. Players must explicitly join queues; there's no automatic queue restoration on fresh connection (only on reconnection).
