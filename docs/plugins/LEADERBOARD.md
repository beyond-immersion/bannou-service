# Leaderboard Plugin Deep Dive

> **Plugin**: lib-leaderboard
> **Schema**: schemas/leaderboard-api.yaml
> **Version**: 1.0.0
> **State Stores**: leaderboard-definition (Redis), leaderboard-ranking (Redis, Sorted Sets)

---

## Overview

Real-time leaderboard management built on Redis Sorted Sets for O(log N) ranking operations. Supports polymorphic entity types (Account, Character, Guild, Actor, Custom), four score update modes (Replace, Increment, Max, Min), seasonal rotation with archival, and automatic score ingestion from Analytics events. Definitions are scoped per game service with configurable sort order, entity type restrictions, and public/private visibility. Provides percentile calculations, neighbor queries (entries around a given entity), and batch score submission.

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
| `AutoArchiveOnSeasonEnd` | `LEADERBOARD_AUTO_ARCHIVE_ON_SEASON_END` | `true` | Retain previous season data when starting new season |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<LeaderboardService>` | Scoped | Structured logging |
| `LeaderboardServiceConfiguration` | Singleton | All 3 config properties |
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

- **CreateSeason** (`/leaderboard/season/create`): Validates leaderboard is seasonal. If `AutoArchiveOnSeasonEnd=false`, deletes previous season's ranking data and removes from season index. Increments season number with ETag-based optimistic concurrency. Adds new season to season index. Publishes `season.started` event.
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
       ├── AutoArchiveOnSeasonEnd=true?
       │    └── Previous season data RETAINED
       │
       ├── AutoArchiveOnSeasonEnd=false?
       │    └── Previous season ranking DELETED
       │
       ├── Increment CurrentSeason (optimistic concurrency)
       ├── Add to season index set
       └── Publish: leaderboard.season.started
```

---

## Stubs & Unimplemented Features

1. **IncludeArchived not implemented**: `ListLeaderboardDefinitions` returns `NotImplemented` when `IncludeArchived=true`. Archived leaderboards are mentioned in the API but not tracked.
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

None identified.

### Intentional Quirks (Documented Behavior)

1. **Polymorphic member keys**: Sorted set members use format `{EntityType}:{EntityId}` enabling mixed entity types on a single leaderboard. `ParseMemberKey` uses `Enum.Parse` with `ignoreCase=true` for deserialization.

2. **Percentile calculation**: Uses `(1 - rank/total) * 100` formula. Rank 1 of 100 entries = 99th percentile. Single-entry leaderboards default to 100th percentile.

3. **Analytics event matching is two-phase**: First attempts direct lookup using ScoreType/RatingType as the leaderboard ID. If not found, scans all definitions in the game service checking metadata for a `scoreType`/`ratingType` key match. Reports error if multiple definitions match (ambiguous).

4. **Rating updates require Replace mode**: `HandleRatingUpdatedAsync` validates the matched leaderboard uses `UpdateMode.Replace`. Increment/Max/Min modes are rejected with an error event because Glicko-2 ratings are absolute values.

5. **Entry count is queried per-response**: `MapToResponse` doesn't cache entry counts. Each definition get/list/update calls `SortedSetCountAsync` to get the current entry count. For `ListDefinitions` with many leaderboards, this is N Redis calls.

6. **Rank events only on rank change**: `leaderboard.rank.changed` is only published when the rank actually changes (previous rank != new rank). Score updates that don't affect rank produce no event.

7. **Delete cascades to all seasons**: `DeleteLeaderboardDefinition` for seasonal leaderboards iterates the season index and deletes each season's sorted set. If the season index is empty but `CurrentSeason` is set, it falls back to deleting just the current season.

### Design Considerations (Requires Planning)

1. **No distributed locks**: Unlike game-session or matchmaking, leaderboard operations don't use distributed locks. Redis sorted set operations are atomic at the command level, but the read-calculate-write pattern in `SubmitScore` (get previous, calculate final, add) is not atomic across the full operation.

2. **Definition index loaded in full**: `ListLeaderboardDefinitions` loads all IDs from the set, then loads each definition individually. With hundreds of leaderboards per game service, this generates O(N) Redis calls.

3. **AutoArchiveOnSeasonEnd=false deletes data**: The naming is misleading - when archive is disabled, previous season data is permanently deleted on season creation. There's no way to recover it.

4. **Batch submit has no event publishing**: Unlike individual `SubmitScore` which publishes `entry.added` and `rank.changed` events, batch submissions produce no events. Consumers relying on leaderboard events won't see batch updates.

5. **No score validation or bounds**: Scores are stored as doubles with no min/max validation. Negative scores, NaN, and infinity are all accepted by the sorted set operations.

6. **No pagination for season index**: `GetSetAsync<int>` loads all season numbers into memory. Long-running seasonal leaderboards with many seasons could accumulate significant season index data.
