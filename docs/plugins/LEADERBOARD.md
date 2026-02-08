# Leaderboard Plugin Deep Dive

> **Plugin**: lib-leaderboard
> **Schema**: schemas/leaderboard-api.yaml
> **Version**: 1.0.0
> **State Stores**: leaderboard-definition (Redis), leaderboard-ranking (Redis, Sorted Sets)

---

## Overview

Real-time leaderboard management (L4 GameFeatures) built on Redis Sorted Sets. Supports polymorphic entity types (Account, Character, Guild, Actor, Custom), multiple score update modes, seasonal rotation with archival, and automatic score ingestion from Analytics events. Definitions are scoped per game service with configurable sort order and entity type restrictions. Provides percentile calculations, neighbor queries, and batch score submission.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis persistence for definitions (key-value) and rankings (sorted sets) |
| lib-messaging (`IMessageBus`) | Publishing leaderboard lifecycle events; error event publishing |
| lib-messaging (`IEventConsumer`) | 2 event subscriptions (analytics score and rating updates) |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| (none identified) | No services currently call `ILeaderboardClient` in production code |

---

## State Storage

**Stores**: 2 Redis stores (via lib-state `IStateStoreFactory`)

| Store | Key Pattern | Data Type | Purpose |
|-------|-------------|-----------|---------|
| `leaderboard-definition` | `{gameServiceId}:{leaderboardId}` | `LeaderboardDefinitionData` | Individual leaderboard configuration (entity types, sort order, update mode, season) |
| `leaderboard-definition` | `leaderboard-definitions:{gameServiceId}` | `Set<string>` | Index of all leaderboard IDs for a game service |
| `leaderboard-definition` | `leaderboard-seasons:{gameServiceId}:{leaderboardId}` | `Set<int>` | Season numbers for seasonal leaderboards |
| `leaderboard-ranking` | `lb:{gameServiceId}:{leaderboardId}` | Redis Sorted Set | Non-seasonal ranking data (member=`{entityType}:{entityId}`, score=double) |
| `leaderboard-ranking` | `lb:{gameServiceId}:{leaderboardId}:season{N}` | Redis Sorted Set | Per-season ranking data |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `leaderboard.entry.added` | `LeaderboardEntryAddedEvent` | First score submitted for an entity on a leaderboard |
| `leaderboard.rank.changed` | `LeaderboardRankChangedEvent` | Entity's rank changes after score update |
| `leaderboard.season.started` | `LeaderboardSeasonStartedEvent` | New season created for a seasonal leaderboard |

### Consumed Events

| Topic | Event Type | Handler |
|-------|-----------|---------|
| `analytics.score.updated` | `AnalyticsScoreUpdatedEvent` | Matches event to leaderboard by ID or metadata, submits score |
| `analytics.rating.updated` | `AnalyticsRatingUpdatedEvent` | Matches event to leaderboard by ID or metadata, submits rating as score (Replace mode only) |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `MaxEntriesPerQuery` | `LEADERBOARD_MAX_ENTRIES_PER_QUERY` | `1000` | Maximum entries returned per ranking query |
| `ScoreUpdateBatchSize` | `LEADERBOARD_SCORE_UPDATE_BATCH_SIZE` | `1000` | Maximum scores per batch submission |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<LeaderboardService>` | Scoped | Structured logging |
| `LeaderboardServiceConfiguration` | Singleton | All 2 config properties |
| `IStateStoreFactory` | Singleton | Redis state store access (2 stores) |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IEventConsumer` | Scoped | Event handler registration |

Service lifetime is **Scoped** (per-request). No background services.

---

## API Endpoints (Implementation Notes)

### Definitions (5 endpoints)

- **CreateLeaderboardDefinition** (`/leaderboard/definition/create`): Checks for existing definition (Conflict on duplicate). Saves definition with entity types, sort order, update mode, seasonal flag. If seasonal, sets `CurrentSeason=1`. Adds to per-gameService definition index via `AddToSetAsync`.
- **GetLeaderboardDefinition** (`/leaderboard/definition/get`): Loads definition by composite key. Queries ranking sorted set for current entry count.
- **ListLeaderboardDefinitions** (`/leaderboard/definition/list`): Loads all definition IDs from gameService index set. Filters `IncludeArchived` returns `NotImplemented`. Loads each definition individually with entry count. Sorted alphabetically by leaderboard ID.
- **UpdateLeaderboardDefinition** (`/leaderboard/definition/update`): ETag-based optimistic concurrency. Partial update (DisplayName, Description, IsPublic). Returns Conflict on concurrent modification.
- **DeleteLeaderboardDefinition** (`/leaderboard/definition/delete`): Deletes definition, removes from index set. For seasonal: iterates all seasons from season index and deletes each ranking sorted set. For non-seasonal: deletes the single ranking sorted set.

### Scores (2 endpoints)

- **SubmitScore** (`/leaderboard/score/submit`): Validates entity type against definition's allowed types. Gets previous score and rank. Calculates final score via `CalculateFinalScore` (Replace/Increment/Max/Min). Adds to sorted set. Publishes `entry.added` for first-time entities or `rank.changed` if rank shifted. Returns previous/current score and rank with rank change delta.
- **SubmitScoreBatch** (`/leaderboard/score/submit-batch`): Validates batch size against `ScoreUpdateBatchSize` config. Filters entries by allowed entity types. Uses `SortedSetAddBatchAsync` for efficient bulk insert. Always uses Replace mode regardless of leaderboard's UpdateMode. Returns accepted/rejected counts.

### Rankings (3 endpoints)

- **GetEntityRank** (`/leaderboard/rank/get`): Gets entity's score from sorted set. Gets 0-based rank (converted to 1-based). Calculates percentile: `(1 - rank/total) * 100`. Returns rank, score, total entries, percentile.
- **GetTopRanks** (`/leaderboard/rank/top`): Validates count against `MaxEntriesPerQuery`. Uses `SortedSetRangeByRankAsync` with offset/count pagination. Parses member keys back to entity type + ID. Returns 1-based ranks.
- **GetRanksAround** (`/leaderboard/rank/around`): Gets entity's rank first. Calculates window: `[rank - countBefore, rank + countAfter]`. Clamps start to 0. Validates total requested against `MaxEntriesPerQuery`.

### Seasons (2 endpoints)

- **CreateSeason** (`/leaderboard/season/create`): Validates leaderboard is seasonal. If `archivePrevious=false` in request, deletes previous season's ranking data and removes from season index. Increments season number with ETag-based optimistic concurrency. Adds new season to season index. Publishes `season.started` event.
- **GetSeason** (`/leaderboard/season/get`): Validates leaderboard is seasonal. Defaults to current season if no number specified. Validates season exists in season index. Returns entry count from ranking sorted set.

---

## Visual Aid

```
Leaderboard Architecture
==========================

  State Store Layout (Redis):

  ┌─ leaderboard-definition store ─────────────────────────────────┐
  │                                                                 │
  │  Key: {gameServiceId}:{leaderboardId}                           │
  │  Value: LeaderboardDefinitionData                               │
  │    ├── DisplayName, Description                                 │
  │    ├── EntityTypes: [Account, Character, Guild, Actor, Custom]  │
  │    ├── SortOrder: Ascending | Descending                        │
  │    ├── UpdateMode: Replace | Increment | Max | Min              │
  │    ├── IsSeasonal, CurrentSeason                                │
  │    └── Metadata (for analytics event matching)                  │
  │                                                                 │
  │  Key: leaderboard-definitions:{gameServiceId}                   │
  │  Value: Set<string> (leaderboard IDs)                           │
  │                                                                 │
  │  Key: leaderboard-seasons:{gameServiceId}:{leaderboardId}       │
  │  Value: Set<int> (season numbers)                               │
  └─────────────────────────────────────────────────────────────────┘

  ┌─ leaderboard-ranking store ─────────────────────────────────────┐
  │                                                                 │
  │  Key: lb:{gameServiceId}:{leaderboardId}[:season{N}]            │
  │  Type: Redis Sorted Set                                         │
  │  Member: {EntityType}:{EntityId}                                │
  │  Score: double                                                  │
  │                                                                 │
  │  Operations: O(log N) add/rank/score, O(log N + M) range       │
  └─────────────────────────────────────────────────────────────────┘


Score Update Modes
===================

  CalculateFinalScore(mode, previous, new):
       │
       ├── Replace   → new
       ├── Increment → previous + new
       ├── Max       → max(previous, new)
       └── Min       → min(previous, new)


Analytics Integration
======================

  analytics.score.updated / analytics.rating.updated
       │
       ├── Match leaderboard by direct ID lookup
       │    (ScoreType/RatingType as leaderboard ID)
       │
       ├── Fallback: scan all definitions in game service
       │    Match metadata[scoreType/ratingType] == event type
       │
       └── Submit score via internal SubmitScoreAsync
            (Rating events require Replace mode)


Season Lifecycle
=================

  CreateSeason (admin)
       │
       ├── body.ArchivePrevious=true (default)?
       │    └── Previous season data RETAINED
       │
       ├── body.ArchivePrevious=false?
       │    └── Previous season ranking DELETED
       │
       ├── Increment CurrentSeason (optimistic concurrency)
       ├── Add to season index set
       └── Publish: leaderboard.season.started
```

---

## Stubs & Unimplemented Features

1. **IncludeArchived not implemented**: `ListLeaderboardDefinitions` returns `NotImplemented` when `IncludeArchived=true`. Archived leaderboards are mentioned in the API but not tracked.
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/232 -->
2. **GetSeason returns approximate dates**: `StartedAt` uses the definition's `CreatedAt` timestamp (not the actual season start). `EndedAt` uses `UtcNow` for inactive seasons. No per-season start/end tracking exists.
3. **Batch submit ignores UpdateMode**: `SubmitScoreBatch` always uses Replace mode regardless of the leaderboard's configured `UpdateMode`. Individual `SubmitScore` respects the mode.

---

## Potential Extensions

1. **Per-season timestamps**: Track actual season start/end dates for historical queries and season duration analytics.
2. **Leaderboard TTL**: Auto-archive or delete leaderboards that haven't received scores in a configurable period.
3. **Cross-game leaderboards**: Aggregate scores across multiple game services for global rankings.
4. **Score decay**: Time-weighted scoring where older scores gradually decrease to encourage active participation.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. ~~**`archivePrevious` request parameter is ignored**~~: **FIXED** (2026-01-31) - `CreateSeasonAsync` now uses `body.ArchivePrevious` instead of `_configuration.AutoArchiveOnSeasonEnd`. The request parameter defaults to `true` per the schema, giving callers per-request control over archival behavior.

### Intentional Quirks

1. **Percentile calculation**: Uses `(1 - rank/total) * 100` formula. Rank 1 of 100 entries = 99th percentile. Single-entry leaderboards default to 100th percentile.

2. **Rank events only on rank change**: `leaderboard.rank.changed` is only published when the rank actually changes (previous rank != new rank). Score updates that don't affect rank produce no event.

3. **`archivePrevious=false` deletes data**: When the request specifies not to archive, previous season data is permanently deleted on season creation. There's no way to recover it. The parameter defaults to `true`, so callers must explicitly opt-in to deletion.

4. **Batch submit has no event publishing**: Unlike individual `SubmitScore` which publishes `entry.added` and `rank.changed` events, batch submissions produce no events. Consumers relying on leaderboard events won't see batch updates.

5. **No score validation or bounds**: Scores are stored as doubles with no min/max validation. Negative scores, NaN, and infinity are all accepted by the sorted set operations.

### Design Considerations

1. **No distributed locks**: Unlike game-session or matchmaking, leaderboard operations don't use distributed locks. Redis sorted set operations are atomic at the command level, but the read-calculate-write pattern in `SubmitScore` (get previous, calculate final, add) is not atomic across the full operation.

2. **Definition index loaded in full**: `ListLeaderboardDefinitions` loads all IDs from the set, then loads each definition individually. With hundreds of leaderboards per game service, this generates O(N) Redis calls.

3. **No pagination for season index**: `GetSetAsync<int>` loads all season numbers into memory. Long-running seasonal leaderboards with many seasons could accumulate significant season index data.

4. **Unused state store `leaderboard-season`**: The `state-stores.yaml` defines a `leaderboard-season` store (MySQL backend) for "Season history and archives", but the service doesn't use it. All season data is stored in the `leaderboard-definition` Redis store instead. Either the store should be removed from the schema, or the service should be updated to use it for proper season archival.

---

## Work Tracking

### Completed

- **2026-01-31**: Fixed `archivePrevious` request parameter being ignored in `CreateSeasonAsync` - now uses per-request flag instead of global configuration. Removed orphaned `AutoArchiveOnSeasonEnd` configuration property (per-request control is sufficient).

*Use `/audit-plugin leaderboard` to process remaining bugs and design considerations.*
