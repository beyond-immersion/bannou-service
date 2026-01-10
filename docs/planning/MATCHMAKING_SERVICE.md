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

## Design Decisions (Resolved)

### D1: Query Language Syntax
**Decision**: **Lucene-like syntax** with MIT-licensed parser library

```
properties.skill:warrior AND properties.rating:[800 TO 2400]
properties.region:us OR properties.region:eu
+properties.mode:ranked -properties.map:tutorial
```

**Rationale**: Industry standard, Nakama-compatible, most expressive. One-time implementation cost for maximum future flexibility.

---

### D2: Skill Window Expansion
**Decision**: **Configurable per Queue** via `skill_expansion` array

```yaml
skill_expansion:
  - intervals: 0, range: 50     # First interval: ±50 rating
  - intervals: 2, range: 150    # After 2 intervals: ±150
  - intervals: 4, range: 400    # After 4 intervals: ±400
  - intervals: 6, range: null   # After 6 intervals: any skill
```

**Rationale**: Different queues need different expansion curves. Ranked needs slow expansion, casual can be loose. Configure once, never touch code.

---

### D3: Match Formation Trigger
**Decision**: **Immediate + Interval**

- On ticket creation: immediate "quick match" check for obvious matches
- Background processor: runs every N seconds for complex combinatorial matching

**Rationale**: Best of both worlds. Easy matches form instantly, complex matches get proper optimization.

---

### D4: Party Skill Aggregation
**Decision**: **Configurable per Queue** with `party_skill_aggregation` setting

Options: `highest` (default, anti-smurf), `average`, `weighted`

```yaml
party_skill_aggregation: "highest"
# Or for weighted:
party_skill_aggregation: "weighted"
party_skill_weights: [0.7, 0.2, 0.1]
```

**Rationale**: Competitive queues need anti-smurf protection (highest), casual can use average. Per-queue config.

---

### D5: Match Result Delivery
**Decision**: **Push + State Storage** with event-driven reconnection handling

1. Match forms → store `pending_match:{session_id}` in Redis (short TTL)
2. Push `MatchFoundEvent` via `IClientEventPublisher`
3. Matchmaking subscribes to `session.reconnected` event
4. On reconnect: check Redis for session_id, re-push if found

**Rationale**: Handles WiFi blips gracefully. Connect service stays dumb about matchmaking. Event-driven, decoupled.

---

### D6: Queue Identification
**Decision**: **Pre-Defined Queues** with preset examples

- Admin creates queues via API with full configuration
- Common presets documented for: 1v1 ranked, team casual, battle royale, FFA, etc.
- Queue config includes all behavior settings (skill expansion, party aggregation, etc.)

**Rationale**: Queues need extensive config. Dynamic queues risk fragmentation. Presets make setup easy.

---

### D7: Cancel/Timeout Behavior
**Decision**: **Event + Reason Code**

`MatchmakingCancelledEvent` with reason codes:
- `cancelled_by_user` - Player called `/matchmaking/leave`
- `timeout` - Max intervals exceeded
- `session_disconnected` - WebSocket dropped, didn't reconnect in time
- `party_disbanded` - Party dissolved during matching
- `match_declined` - Someone declined the formed match
- `queue_disabled` - Admin disabled the queue

**Rationale**: Clear feedback enables good client UX. Events make the world go 'round.

---

### D8: Concurrent Queue Limit
**Decision**: **Configurable per Queue** with exclusive groups

```yaml
# Can't double-queue arena, but mahjong is fine
arena-1v1:
  allow_concurrent: true
  exclusive_group: "arena"
arena-2v2:
  allow_concurrent: true
  exclusive_group: "arena"
mahjong-casual:
  allow_concurrent: true
  exclusive_group: null
tournament:
  allow_concurrent: false  # This queue only
```

Plus global max tickets (default: 3) to prevent abuse.

**Rationale**: Can't be in two arena queues, but arena + mahjong is fine. Exclusive groups handle this cleanly.

---

### D9: Game Session Integration
**Decision**: **Auto-Create via RPC** (mesh call to lib-game-session)

1. Match formed with players
2. Matchmaker calls `lib-game-session/sessions/create` via mesh
3. `MatchFoundEvent` includes `session_id`
4. Players ready to join

**Rationale**: Dependency direction matters. `lib-matchmaking` → `lib-game-session` is correct. Event-based would invert this.

---

### D10: Historical Match Data
**Decision**: **Minimal Stats Only**

Matchmaking tracks operational metrics:
- Queue depths, avg wait times, matches per interval, timeout rates
- Published as `MatchmakingStatsEvent` for dashboards

Player history owned by lib-analytics (already exists).

**Rationale**: Don't duplicate lib-analytics' job. Matchmaking focuses on operational health.

---

## Queue Preset Examples

Common configurations for different game types. Use as starting points when creating queues.

### Preset: Ranked 1v1

```yaml
queue_id: "ranked-1v1"
game_id: "arcadia"
display_name: "Ranked Duel"
min_count: 2
max_count: 2
count_multiple: 2
interval_seconds: 15
max_intervals: 6  # 90 seconds max wait

skill_expansion:
  - intervals: 0, range: 50
  - intervals: 2, range: 100
  - intervals: 4, range: 200
  - intervals: 6, range: null

party_skill_aggregation: "highest"
allow_concurrent: true
exclusive_group: "ranked"
use_skill_rating: true
rating_category: "arcadia-duel"  # lib-analytics rating category
```

### Preset: Team Competitive (5v5)

```yaml
queue_id: "competitive-5v5"
game_id: "arcadia"
display_name: "Competitive 5v5"
min_count: 10
max_count: 10
count_multiple: 5  # Must form complete teams
interval_seconds: 20
max_intervals: 9  # 3 minutes max wait

skill_expansion:
  - intervals: 0, range: 100
  - intervals: 3, range: 200
  - intervals: 6, range: 400
  - intervals: 9, range: null

party_skill_aggregation: "highest"
party_max_size: 5  # Full team allowed
allow_concurrent: true
exclusive_group: "competitive"
use_skill_rating: true
rating_category: "arcadia-team"
```

### Preset: Casual Quick Play

```yaml
queue_id: "casual-quickplay"
game_id: "arcadia"
display_name: "Quick Play"
min_count: 4
max_count: 8
count_multiple: 1  # Flexible sizing
interval_seconds: 10
max_intervals: 3  # 30 seconds max wait

skill_expansion:
  - intervals: 0, range: null  # No skill filtering

party_skill_aggregation: "average"
party_max_size: 4
allow_concurrent: true
exclusive_group: null  # Can queue with anything
use_skill_rating: false
```

### Preset: Battle Royale

```yaml
queue_id: "battle-royale-solo"
game_id: "arcadia"
display_name: "Battle Royale"
min_count: 20
max_count: 100
count_multiple: 1
interval_seconds: 30
max_intervals: 4  # 2 minutes, then start with whoever we have

skill_expansion:
  - intervals: 0, range: 200
  - intervals: 2, range: null

party_skill_aggregation: "highest"
party_max_size: 1  # Solo only
allow_concurrent: false  # BR queue only
exclusive_group: null
use_skill_rating: true
rating_category: "arcadia-br"
start_when_minimum_reached: true  # Start at min_count after max_intervals
```

### Preset: Casual Mini-Game (Mahjong)

```yaml
queue_id: "mahjong-casual"
game_id: "arcadia-minigames"
display_name: "Mahjong"
min_count: 4
max_count: 4
count_multiple: 4
interval_seconds: 15
max_intervals: 8  # 2 minutes

skill_expansion:
  - intervals: 0, range: null  # No skill for casual

party_skill_aggregation: "average"
party_max_size: 4
allow_concurrent: true
exclusive_group: "minigames"  # Can't queue multiple minigames
use_skill_rating: false
```

### Preset: Tournament

```yaml
queue_id: "tournament-bracket"
game_id: "arcadia"
display_name: "Tournament Match"
min_count: 2
max_count: 2
count_multiple: 2
interval_seconds: 60
max_intervals: 10  # 10 minutes for tournament

skill_expansion:
  - intervals: 0, range: null  # Tournaments use seeding, not live skill

party_skill_aggregation: "highest"
allow_concurrent: false  # Tournament queue only
exclusive_group: null
use_skill_rating: false
requires_registration: true  # Must be registered for tournament
tournament_id_required: true  # Links to tournament system
```

---

## Next Steps

1. **Schema Design**: Define `matchmaking-api.yaml`, `matchmaking-events.yaml`, `matchmaking-client-events.yaml`, `matchmaking-configuration.yaml`
2. **Endpoint Specification**: Request/response models for each operation
3. **Helper Services**: Design internal services (QueueManager, MatchProcessor, TicketStore, QueryParser)
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
