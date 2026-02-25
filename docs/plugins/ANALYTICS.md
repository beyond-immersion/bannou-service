# Analytics Plugin Deep Dive

> **Plugin**: lib-analytics
> **Schema**: schemas/analytics-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFeatures
> **State Stores**: analytics-summary (Redis), analytics-summary-data (MySQL), analytics-rating (Redis), analytics-history-data (MySQL)

## Overview

The Analytics plugin (L4 GameFeatures) is the central event aggregation point for all game-related statistics. Handles event ingestion, entity summary computation, Glicko-2 skill rating calculations, and controller history tracking. Publishes score updates and milestone events consumed by Achievement and Leaderboard for downstream processing. Subscribes to game session lifecycle and character/realm history events for automatic ingestion. Unlike typical L4 services, Analytics only observes via event subscriptions -- it does not invoke L2/L4 service APIs and should not be called by L1/L2/L3 services.

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (IStateStoreFactory) | Event buffer, entity summaries, skill ratings, controller history, session mappings, game service/realm/character caches |
| lib-state (IDistributedLockProvider) | Distributed locks for buffer flush (prevents duplicate processing across instances) and rating updates (serializes per game+type) |
| lib-messaging (IMessageBus) | Publishing score/rating/milestone events and error events |
| lib-game-service (IGameServiceClient) | Resolving game type strings to game service IDs for event keying |
| lib-game-session (IGameSessionClient) | Resolving session IDs to game types (fallback when no cached mapping exists) |
| lib-realm (IRealmClient) | Resolving realm IDs to game service IDs for character/realm history events |
| lib-character (ICharacterClient) | Resolving character IDs to realm IDs for character history events |

> **Refactoring Consideration**: This plugin injects 4 service clients individually. Consider whether `IServiceNavigator` would reduce constructor complexity, trading explicit dependencies for cleaner signatures. Currently favoring explicit injection for dependency clarity.

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-achievement | Subscribes to `analytics.score.updated` to check achievement progress thresholds |
| lib-achievement | Subscribes to `analytics.milestone.reached` to trigger milestone-based achievements |
| lib-leaderboard | Subscribes to `analytics.score.updated` to update leaderboard entries |
| lib-leaderboard | Subscribes to `analytics.rating.updated` to update skill rating leaderboards |

## State Storage

**Redis stores** handle event buffering, session mappings, and resolution caches. The service validates the summary store is Redis at runtime via `EnsureSummaryStoreRedisAsync()` for buffer operations. **MySQL stores** provide server-side filtering, sorting, and pagination: `analytics-summary-data` for entity summaries, `analytics-history-data` for controller possession history with configurable retention.

| Key Pattern | Store | Purpose |
|-------------|-------|---------|
| `{gameServiceId}:{entityType}:{entityId}` | analytics-summary-data (MySQL) | Entity summary aggregations (event counts, aggregates, timestamps) |
| `analytics-event-buffer-entry:{eventId}` | analytics-summary (Redis) | Individual buffered event entries awaiting flush |
| `analytics-event-buffer-index` | analytics-summary (Redis) | Sorted set of buffered event keys (scored by timestamp) |
| `analytics-session-mapping:{sessionId}` | analytics-summary (Redis) | Game session to game service ID cache (TTL: SessionMappingTtlSeconds) |
| `analytics-game-service-cache:{stubName}` | analytics-summary (Redis) | Game type stub to service ID cache (TTL: ResolutionCacheTtlSeconds) |
| `analytics-realm-game-service-cache:{realmId}` | analytics-summary (Redis) | Realm to game service ID cache (TTL: ResolutionCacheTtlSeconds) |
| `analytics-character-realm-cache:{characterId}` | analytics-summary (Redis) | Character to realm ID cache (TTL: ResolutionCacheTtlSeconds) |
| `{gameServiceId}:{ratingType}:{entityType}:{entityId}` | analytics-rating (Redis) | Glicko-2 skill rating data per entity per rating type |
| `{gameServiceId}:controller:{accountId}:{timestamp:o}` | analytics-history-data (MySQL) | Individual controller history events |

### Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `entityType` | A (Entity Reference) | `EntityType` enum | Identifies what kind of entity analytics are tracked for (account, character, guild, actor). All valid values are first-class Bannou entities. Uses the shared `EntityType` enum from `common-api.yaml`. |
| `targetEntityType` | A (Entity Reference) | `EntityType` enum | Identifies the entity type being controlled in controller history records. Same shared `EntityType` enum. |
| `eventType` | B (Game Content Type) | Opaque string | Type of analytics event (e.g., `"kill"`, `"death"`, `"score"`, `"action"`). Vocabulary defined by game services at runtime through event ingestion. New event types require no schema changes. |
| `ratingType` | B (Game Content Type) | Opaque string | Skill rating category (e.g., `"overall"`, `"ranked"`, `"casual"`). Allows multiple independent Glicko-2 rating tracks per entity. Vocabulary defined per game at deployment time. |
| `scoreType` (on `AnalyticsScoreUpdatedEvent`) | B (Game Content Type) | Opaque string | Type of score that changed (e.g., `"kills"`, `"points"`, `"xp"`). Published during buffer flush for score-bearing events. Vocabulary matches ingested `eventType` values. |
| `milestoneType` (on `AnalyticsMilestoneReachedEvent`) | B (Game Content Type) | Opaque string | Type of milestone reached (e.g., `"total_kills"`, `"games_played"`). Published when a score crosses a configured threshold. Vocabulary matches aggregated event types. |
| `action` | C (System State/Mode) | `ControllerAction` enum | Controller possession action (`possess`, `release`). Binary state tracking whether control was taken or relinquished. |
| `metadata` (on `IngestEventRequest`) | -- (Client Metadata) | `object` (`additionalProperties: true`) | Opaque client-provided event context. Analytics stores and returns this data without inspecting its structure, per T29 (No Metadata Bag Contracts). |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `analytics.score.updated` | `AnalyticsScoreUpdatedEvent` | During buffer flush, for each event with non-zero value |
| `analytics.rating.updated` | `AnalyticsRatingUpdatedEvent` | After all players' Glicko-2 ratings are saved (batch publish) |
| `analytics.milestone.reached` | `AnalyticsMilestoneReachedEvent` | When a score crosses a milestone threshold |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `game-session.action.performed` | `HandleGameActionPerformedAsync` | Resolves game service via session mapping, buffers action event |
| `game-session.created` | `HandleGameSessionCreatedAsync` | Saves session-to-gameService mapping, buffers creation event |
| `game-session.deleted` | `HandleGameSessionDeletedAsync` | Removes session mapping, buffers deletion event |
| `character-history.participation.recorded` | `HandleCharacterParticipationRecordedAsync` | Resolves game service via character->realm->gameService chain, buffers event |
| `character-history.backstory.created` | `HandleCharacterBackstoryCreatedAsync` | Resolves game service via character->realm->gameService chain, buffers event |
| `character-history.backstory.updated` | `HandleCharacterBackstoryUpdatedAsync` | Resolves game service via character->realm->gameService chain, buffers event |
| `realm-history.participation.recorded` | `HandleRealmParticipationRecordedAsync` | Resolves game service via realm lookup, buffers event |
| `realm-history.lore.created` | `HandleRealmLoreCreatedAsync` | Resolves game service via realm lookup, buffers event |
| `realm-history.lore.updated` | `HandleRealmLoreUpdatedAsync` | Resolves game service via realm lookup, buffers event |
| `character.updated` | `HandleCharacterUpdatedForCacheInvalidationAsync` | Invalidates character-to-realm resolution cache |
| `realm.updated` | `HandleRealmUpdatedForCacheInvalidationAsync` | Invalidates realm-to-gameService resolution cache |

All history event handlers follow the fail-fast pattern: if game service resolution fails (realm/character not found, service unavailable), the event is dropped with error logging and an error event published via `TryPublishErrorAsync`. Events are never buffered with incorrect game service IDs.

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `EventBufferSize` | `ANALYTICS_EVENT_BUFFER_SIZE` | 1000 | Max events to buffer before flush |
| `EventBufferFlushIntervalSeconds` | `ANALYTICS_EVENT_BUFFER_FLUSH_INTERVAL_SECONDS` | 5 | Time-based flush trigger for oldest buffered event |
| `Glicko2DefaultRating` | `ANALYTICS_GLICKO2_DEFAULT_RATING` | 1500.0 | Starting Glicko-2 rating for new entities |
| `Glicko2DefaultDeviation` | `ANALYTICS_GLICKO2_DEFAULT_DEVIATION` | 350.0 | Starting rating deviation (max uncertainty) |
| `Glicko2DefaultVolatility` | `ANALYTICS_GLICKO2_DEFAULT_VOLATILITY` | 0.06 | Starting volatility (standard value) |
| `Glicko2SystemConstant` | `ANALYTICS_GLICKO2_SYSTEM_CONSTANT` | 0.5 | Tau - controls how quickly volatility changes |
| `Glicko2MinRating` | `ANALYTICS_GLICKO2_MIN_RATING` | 100.0 | Minimum allowed rating (floor clamp) |
| `Glicko2MaxRating` | `ANALYTICS_GLICKO2_MAX_RATING` | 4000.0 | Maximum allowed rating (ceiling clamp) |
| `Glicko2MinDeviation` | `ANALYTICS_GLICKO2_MIN_DEVIATION` | 30.0 | Minimum rating deviation (prevents overconfidence) |
| `Glicko2MaxVolatilityIterations` | `ANALYTICS_GLICKO2_MAX_VOLATILITY_ITERATIONS` | 100 | Max iterations for volatility convergence |
| `Glicko2VolatilityConvergenceTolerance` | `ANALYTICS_GLICKO2_VOLATILITY_CONVERGENCE_TOLERANCE` | 1e-06 | Convergence tolerance for volatility iteration |
| `ResolutionCacheTtlSeconds` | `ANALYTICS_RESOLUTION_CACHE_TTL_SECONDS` | 300 | TTL for resolution caches (game service, realm, character lookups) |
| `SessionMappingTtlSeconds` | `ANALYTICS_SESSION_MAPPING_TTL_SECONDS` | 3600 | TTL for game session mappings (should exceed typical session duration) |
| `MilestoneThresholds` | `ANALYTICS_MILESTONE_THRESHOLDS` | 10,25,50,... | Comma-separated score thresholds that trigger milestone events |
| `EventBufferLockExpiryBaseSeconds` | `ANALYTICS_EVENT_BUFFER_LOCK_EXPIRY_BASE_SECONDS` | 10 | Base distributed lock expiry (actual = max(this, 2x flush interval)) |
| `RatingUpdateLockExpirySeconds` | `ANALYTICS_RATING_UPDATE_LOCK_EXPIRY_SECONDS` | 30 | Distributed lock expiry for skill rating update operations |
| `ControllerHistoryRetentionDays` | `ANALYTICS_CONTROLLER_HISTORY_RETENTION_DAYS` | 90 | Days to retain controller history records (0 = indefinite) |
| `ControllerHistoryCleanupBatchSize` | `ANALYTICS_CONTROLLER_HISTORY_CLEANUP_BATCH_SIZE` | 5000 | Maximum records to delete per cleanup invocation |
| `ControllerHistoryCleanupSubBatchSize` | `ANALYTICS_CONTROLLER_HISTORY_CLEANUP_SUB_BATCH_SIZE` | 100 | Records to delete per iteration within a cleanup batch |

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<AnalyticsService>` | Structured logging |
| `AnalyticsServiceConfiguration` | Typed config access |
| `IStateStoreFactory` | Multi-store access (summary, rating, history) |
| `IDistributedLockProvider` | Flush lock and rating update lock coordination |
| `IMessageBus` | Event publishing and error reporting |
| `IGameServiceClient` | Game type to service ID resolution |
| `IGameSessionClient` | Session ID to game type resolution (fallback) |
| `IRealmClient` | Realm ID to game service ID resolution (for history events) |
| `ICharacterClient` | Character ID to realm ID resolution (for history events) |
| `IEventConsumer` | Registers handlers for 11 consumed event types |

## API Endpoints (Implementation Notes)

### Event Ingestion (`/analytics/event/ingest`, `/analytics/event/ingest-batch`)

Events are buffered in Redis using a two-part structure: individual event entries keyed by `analytics-event-buffer-entry:{eventId}`, and a sorted set `analytics-event-buffer-index` scored by event timestamp in milliseconds. Flush is triggered when either: (a) the sorted set size reaches `EventBufferSize`, or (b) the oldest entry is older than `EventBufferFlushIntervalSeconds`. A distributed lock prevents concurrent flush from multiple instances. The flush processes events in batches grouped by entity, updating each entity's summary with optimistic concurrency (ETag-based `TrySaveAsync`). On ETag conflict, the batch for that entity is skipped (will be retried next flush).

### Entity Summary (`/analytics/summary/get`, `/analytics/summary/query`)

Get returns a single entity's aggregated statistics by composite key from the MySQL `analytics-summary-data` store. Query uses MySQL JSON functions via `JsonQueryPagedAsync` for server-side filtering (gameServiceId, entityType, eventType existence, minEvents), sorting, and pagination. Supported sort fields: `totalevents`, `firsteventat`, `lasteventat`, `eventcount` (case-insensitive). This avoids loading all summaries into memory.

### Skill Rating (`/analytics/rating/get`, `/analytics/rating/update`)

Get returns the current Glicko-2 rating or default values if no rating exists (not 404). Update takes a match with 2+ results, acquires a distributed lock on the game+ratingType combination, loads current ratings, snapshots pre-match values, calculates all Glicko-2 updates using original opponent ratings (order-independent), saves all ratings, then publishes `analytics.rating.updated` per player. Returns Conflict if the lock cannot be acquired (another update for the same game+type is in progress).

### Controller History (`/analytics/controller-history/record`, `/analytics/controller-history/query`, `/analytics/controller-history/cleanup`)

Records are stored in the MySQL `analytics-history-data` store. Query uses MySQL JSON functions via `JsonQueryPagedAsync` for server-side filtering (gameServiceId, accountId, targetEntityId, targetEntityType, time range), sorting by timestamp descending, and limit. Cleanup endpoint (admin-only) deletes records older than `ControllerHistoryRetentionDays` (default 90 days) in batches, with dry-run preview support.

## Visual Aid

```
Event Sources                    Analytics Service                    Consumers

game-session.action.performed --+
game-session.created -----------+    +---------------------+
game-session.deleted -----------+---> |  Event Buffer       |
character-history.* ------------+    |  (Redis Sorted Set) |
realm-history.* ----------------+    +----------+----------+
        |                                       |
        v                       (size >= EventBufferSize OR
  +--------------+               age >= FlushIntervalSeconds)
  | Resolution   |                              |
  | character->  |                    +---------v---------+
  | realm->      |                    |  Flush (locked)    |
  | gameService  |                    |  Group by entity   |
  | (cached)     |                    |  Update summaries  |
  +--------------+                    +--+------------+----+
                                         |            |
                       +-----------------v--+   +----v---------------+
                       | analytics.score    |   | analytics          |
                       | .updated           |   | .milestone         |
                       |                    |   | .reached           |
                       +------+-------------+   +------+-------------+
                              |                        |
                      +-------+-------+       +--------+-------+
                      |  Leaderboard  |       |  Achievement   |
                      |  Service      |       |  Service       |
                      +---------------+       +----------------+

Direct API
                       +---------------------+
/rating/update ------> |  Lock (game+type)   |
                       |  Snapshot ratings    |
                       |  Glicko-2 Calc All  |
                       |  Save All           |---> analytics.rating.updated
                       +---------------------+         |
                                                +------+------+
                                                | Leaderboard |
                                                +-------------+
```

## Stubs & Unimplemented Features

### Rating Period Decay
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/249 -->

The Glicko-2 algorithm includes a concept of "rating period decay" where a player's rating deviation increases over time when they don't play. The `CalculateGlicko2Update` handles the no-games case (deviation increases by volatility), but there is no scheduled task or event that triggers this decay for inactive players. Players who stop playing retain their last RD indefinitely.

### Per-Game Milestone Definitions

Milestones are configurable via `MilestoneThresholds` as a global comma-separated list. There is no API to define custom milestone values per game service or score type. All entities use the same configured threshold set regardless of context.

## Potential Extensions

- **Rating period scheduling**: A background task that periodically increases RD for inactive players (common in competitive games)
- **Per-game milestones**: API for game-specific milestone definitions (currently global config only)
- **Event replay**: Ability to reprocess buffered events after a bug fix (currently events are deleted after processing)
- **Time-series queries**: Adding time-bucketed aggregations for trend analysis

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. ~~**Unused state store definition `analytics-history`**~~: **FIXED** (2026-02-01) - Removed the unused `analytics-history` Redis store definition from `schemas/state-stores.yaml`. The store was defined but never referenced in code. Updated regression tests accordingly.

### Intentional Quirks (Documented Behavior)

1. **GetSkillRating returns default values instead of 404**: When no rating exists for an entity, the endpoint returns 200 with default rating values (1500/350/0.06) and `MatchesPlayed = 0`. New players start at the default rating.

2. **Score events published during flush, not during ingestion**: `analytics.score.updated` events are published only when the buffer flushes. Leaderboard and achievement updates are delayed by up to `EventBufferFlushIntervalSeconds` (default 5s) to reduce event storm pressure.

3. **History event resolution drops events on failure**: If the realm or character lookup fails when handling history events, the event is silently dropped (not retried). An error event is published for monitoring, but the analytics data is permanently lost. Incorrect data (wrong GameServiceId) is considered worse than missing data.

4. **Cache invalidation is best-effort**: Handlers for `character.updated` and `realm.updated` events catch exceptions and log warnings but do not fail. Stale cache entries will eventually expire via TTL.

### Design Considerations (Requires Planning)

1. **`string.Empty` default for internal POCO string fields** - `BufferedAnalyticsEvent.EventType`, `GameSessionMappingData.GameType`, and `SkillRatingData.RatingType` use `= string.Empty` defaults. While not a T25 violation (these are strings, not enums), empty strings could mask bugs. Consider using nullable strings with validation at ingestion boundaries.

2. **No automatic controller history cleanup** - The `CleanupControllerHistory` endpoint exists but must be called manually (e.g., via scheduled cron job or orchestrator task). There is no background service that automatically purges expired records. For production deployments, consider adding a periodic cleanup task or documenting the requirement for external scheduling.

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above.

### Recently Completed

1. **Removed unused `analytics-history` state store** (2026-02-01)
   - Removed from `schemas/state-stores.yaml`
   - Regenerated `StateStoreDefinitions.cs` via `generate-state-stores.py`
   - Updated regression test to verify `analytics-history-data` instead
