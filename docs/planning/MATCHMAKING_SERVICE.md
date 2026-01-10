# Matchmaking Service Design (`lib-matchmaking`)

> **Created**: 2026-01-10
> **Status**: Planning Phase - Gathering Requirements
> **Goal**: Nakama-competitive matchmaking with Bannou architectural patterns

---

## Executive Summary

Design a matchmaking service that provides competitive parity with [Nakama's matchmaker](https://heroiclabs.com/docs/nakama/concepts/multiplayer/matchmaker/) while leveraging Bannou's existing infrastructure (lib-analytics Glicko-2 ratings, lib-state for distributed state, lib-messaging for events).

---

## Requirements

### Core Requirements (Nakama Parity)

| Requirement | Description | Nakama Reference |
|-------------|-------------|------------------|
| **Ticket System** | UUID-identified matchmaking requests with properties | Ticket + MatchmakerIndex |
| **Property Matching** | String and numeric properties for filtering | StringProperties, NumericProperties |
| **Query Language** | Expressive filtering syntax for compatibility | Bluge query_string (Lucene-like) |
| **Min/Max Count** | Player count range requirements per ticket | min_count, max_count |
| **Count Multiple** | Divisibility constraint (e.g., teams of 2) | count_multiple |
| **Party Support** | Multiple players matchmaking as a unit | PartyId, atomic matching |
| **Interval Processing** | Configurable batch matching cycles | interval_sec (default 15s) |
| **Timeout Behavior** | Relax to min_count after max_intervals | max_intervals (default 2) |
| **Session Conflict Detection** | Same user cannot appear twice in match | SessionID deduplication |

### Bannou-Specific Requirements

| Requirement | Description | Rationale |
|-------------|-------------|-----------|
| **Opt-In Design** | Players explicitly join/leave queues | User control, no automatic enrollment |
| **Fast Matching** | Sub-second response for queue operations | Real-time feel, WebSocket push for results |
| **Skill Integration** | Use lib-analytics Glicko-2 ratings | Leverage existing skill system |
| **WebSocket Push** | Match results pushed to all participants | IClientEventPublisher pattern |
| **Multi-Instance Safe** | Works across distributed deployment | Tenet 9 compliance |
| **Event-Driven** | Publish events for match lifecycle | Tenet 5 compliance |

### Stretch Goals (Post-MVP)

| Feature | Description | Priority |
|---------|-------------|----------|
| **Bidirectional Matching** | Both parties' queries must match each other | Medium |
| **Backfill Support** | Add players to in-progress sessions | Medium |
| **Region Awareness** | Prefer low-latency matches | Medium |
| **Custom Matching Functions** | Extensible matching logic | Low |
| **Match Accept/Decline** | Confirmation flow before match start | Low |

---

## Available Infrastructure

### Existing Services We Can Leverage

| Service | How It Helps |
|---------|--------------|
| **lib-analytics** | Glicko-2 skill ratings via `/analytics/rating/get` |
| **lib-game-session** | Session creation after match formed (`/sessions/create`) |
| **lib-permission** | Session state for "in_matchmaking" |
| **lib-connect** | WebSocket push for match notifications |
| **lib-state** | Redis for ticket storage, distributed locks |
| **lib-messaging** | Events for match lifecycle |

### Infrastructure Libs Pattern

| Lib | Usage in Matchmaking |
|-----|---------------------|
| **lib-state** | Store tickets, match history, queue statistics |
| **lib-messaging** | Publish `matchmaking.ticket-created`, `matchmaking.match-formed`, etc. |
| **lib-mesh** | Call lib-analytics for ratings, lib-game-session for match creation |

### Existing Patterns to Follow

```
Game Session Join Flow (reference):
  1. Player calls /sessions/join
  2. Service validates, updates state
  3. Publishes GameSessionPlayerJoinedEvent
  4. Pushes PlayerJoinedEvent to WebSocket clients

Matchmaking Flow (proposed):
  1. Player calls /matchmaking/join-queue
  2. Service creates ticket, stores in Redis
  3. Publishes MatchmakingTicketCreatedEvent
  4. Background processor forms matches
  5. Publishes MatchmakingMatchFormedEvent
  6. Pushes MatchFoundEvent to WebSocket clients
```

---

## Tenet Compliance Shorthand

All implementation must comply with these tenets:

### Foundation Tenets
- **T1 Schema-First**: Define all endpoints, events, config in YAML before code
- **T2 Code Generation**: Use the 8-component pipeline, never edit Generated/
- **T4 Infrastructure Libs**: Use lib-state/lib-messaging/lib-mesh exclusively
- **T5 Event-Driven**: Publish typed events for all state changes
- **T6 Service Pattern**: Partial class, standard constructor, helper services as needed
- **T13 X-Permissions**: All endpoints declare permissions (WebSocket routing)

### Implementation Tenets
- **T7 Error Handling**: Try-catch with ApiException distinction, TryPublishErrorAsync
- **T8 Return Pattern**: `(StatusCodes, TResponse?)` tuples
- **T9 Multi-Instance Safety**: No in-memory authoritative state, use distributed locks
- **T14 Polymorphic Associations**: Entity ID + Type for queue entries
- **T17 Client Events**: Use IClientEventPublisher for WebSocket push
- **T20 JSON Serialization**: Use BannouJson exclusively
- **T21 Configuration-First**: All config via generated configuration class
- **T23 Async Pattern**: async/await on all Task-returning methods

### Quality Tenets
- **T10 Logging**: Structured logging with message templates
- **T11 Testing**: Unit + HTTP + Edge test coverage
- **T16 Naming**: `{Action}Async`, `{Action}Request`, `{Entity}Event` patterns
- **T19 XML Docs**: All public members documented
- **T22 Warning Suppression**: Fix warnings, don't suppress

---

## Open Questions

### Q1: Query Language Syntax

**Question**: What query syntax should we support for property matching?

| Option | Description | Pros | Cons |
|--------|-------------|------|------|
| **A) Lucene-like (Nakama)** | `properties.skill:warrior AND properties.rating:[800 TO 2400]` | Industry standard, powerful, Nakama-compatible | Complex parser, learning curve |
| **B) JSON Filter** | `{"skill": "warrior", "rating": {"$gte": 800, "$lte": 2400}}` | Familiar to MongoDB users, easy to serialize | Verbose, no boolean operators |
| **C) Simple Key-Value + Ranges** | Structured request object with equality/range fields | Type-safe, simple implementation | Less flexible, no complex queries |
| **D) SQL-like WHERE** | `skill = 'warrior' AND rating BETWEEN 800 AND 2400` | Familiar to developers | Needs parser, SQL injection concerns |

**Impact**: Query language affects API complexity, learning curve, and matching flexibility.

**Recommendation**: Option C for MVP (simple, type-safe), with path to Option A for power users later.

---

### Q2: Skill Window Expansion

**Question**: How should skill matching expand over wait time?

| Option | Description | Pros | Cons |
|--------|-------------|------|------|
| **A) Linear Expansion** | Window grows by fixed amount per interval | Predictable, easy to tune | May be too slow or too fast |
| **B) Exponential Expansion** | Window doubles each interval | Fast relaxation after initial wait | Can overshoot quickly |
| **C) Stepped Thresholds** | Discrete steps (exact → ±100 → ±300 → any) | Clear tiers, easy to explain | Jumpy transitions |
| **D) Configurable per Queue** | Each queue defines its own expansion curve | Maximum flexibility | Configuration complexity |

**Impact**: Affects match quality vs wait time tradeoff.

**Recommendation**: Option C with configurable steps per queue type.

---

### Q3: Match Formation Trigger

**Question**: When should the system attempt to form matches?

| Option | Description | Pros | Cons |
|--------|-------------|------|------|
| **A) Interval-Based (Nakama)** | Process every N seconds (e.g., 15s) | Batches efficiently, predictable load | Minimum wait = interval |
| **B) Immediate + Interval** | Try immediately on join, then interval | Faster for easy matches | More processing, race conditions |
| **C) Threshold-Based** | Process when queue reaches N tickets | Scales with demand | Unpredictable timing |
| **D) Event-Driven** | Process on every ticket add/remove | Lowest latency | High CPU for large pools |

**Impact**: Affects latency, server load, and match quality.

**Recommendation**: Option B - immediate check for quick matches, interval for complex matching.

---

### Q4: Party Skill Aggregation

**Question**: How should party skill ratings be calculated for matching?

| Option | Description | Pros | Cons |
|--------|-------------|------|------|
| **A) Average Rating** | Sum of ratings / party size | Simple, balanced | Can be gamed (smurf + pro) |
| **B) Highest Rating** | Use party's best player | Prevents smurfing | Harsh for casual groups |
| **C) Weighted Average** | Weight by role or contribution | Nuanced matching | Complex, game-specific |
| **D) Custom Formula** | Configurable aggregation function | Maximum flexibility | Implementation complexity |

**Impact**: Critical for competitive integrity in party-based matchmaking.

**Recommendation**: Option B as default (prevents boosting), configurable per queue.

---

### Q5: Match Result Delivery

**Question**: How should players receive match results?

| Option | Description | Pros | Cons |
|--------|-------------|------|------|
| **A) WebSocket Push Only** | Push MatchFoundEvent to connected clients | Real-time, stateless | Missed if disconnected |
| **B) Push + Polling Fallback** | Push if connected, poll endpoint otherwise | Reliable | More endpoints, complexity |
| **C) Push + State Storage** | Store pending matches, push + allow fetch | Handles reconnection | State management overhead |

**Impact**: Affects reliability and reconnection handling.

**Recommendation**: Option C - essential for handling brief disconnections during matching.

---

### Q6: Queue Identification

**Question**: How should queues be identified and managed?

| Option | Description | Pros | Cons |
|--------|-------------|------|------|
| **A) Pre-Defined Queues** | Admin creates queues (ranked-1v1, casual-5v5) | Simple, controlled | Less flexible |
| **B) Dynamic Queues** | Queues created on-demand by properties | Maximum flexibility | Fragmentation risk |
| **C) Queue Templates** | Pre-defined templates, instantiated per game/mode | Balanced | More configuration |

**Impact**: Affects how games configure matchmaking and queue fragmentation.

**Recommendation**: Option A for MVP - explicit queue definitions with game_id + mode.

---

### Q7: Cancel/Timeout Behavior

**Question**: What happens when matching times out or is cancelled?

| Option | Description | Pros | Cons |
|--------|-------------|------|------|
| **A) Silent Removal** | Just remove from pool, no notification | Simple | Poor UX |
| **B) Event + Reason** | Push timeout/cancel event with reason | Clear feedback | More event types |
| **C) Auto-Requeue Option** | Option to automatically requeue on timeout | Persistent | May frustrate users |

**Impact**: Affects user experience and debugging.

**Recommendation**: Option B - always notify with clear reason codes.

---

### Q8: Concurrent Queue Limit

**Question**: How many queues can a player be in simultaneously?

| Option | Description | Pros | Cons |
|--------|-------------|------|------|
| **A) Single Queue Only** | One queue per player | Simple, clear state | Limits flexibility |
| **B) Multiple Queues** | Up to N concurrent tickets (like Nakama: 3) | Flexibility | Complex state management |
| **C) Configurable per Game** | Each game decides limit | Maximum flexibility | Configuration overhead |

**Impact**: Affects player flexibility and state complexity.

**Recommendation**: Option B with default limit of 3, configurable via queue settings.

---

### Q9: Integration with Game Sessions

**Question**: What happens after a match is formed?

| Option | Description | Pros | Cons |
|--------|-------------|------|------|
| **A) Return Match Token** | Matchmaker returns token; players join session | Decoupled, flexible | Extra step for players |
| **B) Auto-Create Session** | Matchmaker calls lib-game-session automatically | Seamless | Tighter coupling |
| **C) Webhook/Event** | Publish event; external handler creates session | Extensible | Indirection, latency |

**Impact**: Affects the player experience and service coupling.

**Recommendation**: Option B for seamless experience - matchmaker creates session, returns session_id.

---

### Q10: Historical Match Data

**Question**: Should we track historical matchmaking data?

| Option | Description | Pros | Cons |
|--------|-------------|------|------|
| **A) Minimal (Stats Only)** | Just aggregate stats (wait times, completion rates) | Low storage | No individual analysis |
| **B) Recent History** | Keep last N matches per player | Balance | Rolling window maintenance |
| **C) Full History** | Store all match formations | Complete audit | Storage growth |

**Impact**: Affects debugging, analytics, and storage costs.

**Recommendation**: Option A for service, rely on lib-analytics for detailed player history.

---

## Next Steps

After open questions are resolved:

1. **Schema Design**: Define matchmaking-api.yaml, matchmaking-events.yaml, matchmaking-configuration.yaml
2. **Endpoint Specification**: Request/response models for each operation
3. **Helper Services**: Design internal services (QueueManager, MatchProcessor, TicketStore)
4. **Event Definitions**: Service events + client events for match lifecycle
5. **Implementation Plan**: Ordered implementation steps

---

## References

- [Nakama Matchmaker Documentation](https://heroiclabs.com/docs/nakama/concepts/multiplayer/matchmaker/)
- [Open Match (Google)](https://open-match.dev/site/docs/)
- [Glicko-2 Algorithm](http://www.glicko.net/glicko/glicko2.pdf)
- [POTENTIAL_ENHANCEMENTS.md](./POTENTIAL_ENHANCEMENTS.md) - Original matchmaking proposal

---

*This document will be expanded with schema designs and implementation details after questions are resolved.*
