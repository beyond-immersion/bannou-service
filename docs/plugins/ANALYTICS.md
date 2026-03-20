# Analytics Plugin Deep Dive

> **Plugin**: lib-analytics
> **Schema**: schemas/analytics-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFeatures
> **State Store**: analytics-summary (Redis), analytics-summary-data (MySQL), analytics-rating (Redis), analytics-history-data (MySQL)
> **Implementation Map**: [docs/maps/ANALYTICS.md](../maps/ANALYTICS.md)
> **Short**: Event aggregation, Glicko-2 skill ratings, and milestone detection (event-only observer)

## Overview

The Analytics plugin (L4 GameFeatures) is the central event aggregation point for all game-related statistics. Handles event ingestion, entity summary computation, Glicko-2 skill rating calculations, and controller history tracking. Publishes score updates and milestone events consumed by Achievement and Leaderboard for downstream processing. Subscribes to game session lifecycle and character/realm history events for automatic ingestion. Unlike typical L4 services, Analytics is a leaf node for write calls — it makes read-only entity resolution calls to L2 services (game-service, game-session, realm, character) but no write calls to any other service. It should not be called by L1/L2/L3 services.

## Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `entityType` | A (Entity Reference) | `EntityType` enum | Identifies what kind of entity analytics are tracked for (account, character, guild, actor). All valid values are first-class Bannou entities. Uses the shared `EntityType` enum from `common-api.yaml`. |
| `targetEntityType` | A (Entity Reference) | `EntityType` enum | Identifies the entity type being controlled in controller history records. Same shared `EntityType` enum. |
| `eventType` | B (Game Content Type) | Opaque string | Type of analytics event (e.g., `"kill"`, `"death"`, `"score"`, `"action"`). Vocabulary defined by game services at runtime through event ingestion. New event types require no schema changes. |
| `ratingType` | B (Game Content Type) | Opaque string | Skill rating category (e.g., `"overall"`, `"ranked"`, `"casual"`). Allows multiple independent Glicko-2 rating tracks per entity. Vocabulary defined per game at deployment time. |
| `scoreType` (on `AnalyticsScoreUpdatedEvent`) | B (Game Content Type) | Opaque string | Type of score that changed (e.g., `"kills"`, `"points"`, `"xp"`). Published during buffer flush for score-bearing events. Vocabulary matches ingested `eventType` values. |
| `milestoneType` (on `AnalyticsMilestoneReachedEvent`) | B (Game Content Type) | Opaque string | Type of milestone reached (e.g., `"total_kills"`, `"games_played"`). Published when a score crosses a configured threshold. Vocabulary matches aggregated event types. |
| `action` | C (System State/Mode) | `ControllerAction` enum | Controller possession action (`possess`, `release`). Binary state tracking whether control was taken or relinquished. |
| `metadata` (on `IngestEventRequest`) | -- (Client Metadata) | `object` (`additionalProperties: true`) | Opaque client-provided event context. Analytics stores and returns this data without inspecting its structure, per tenets (No Metadata Bag Contracts). |

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-achievement | Subscribes to `analytics.score.updated` to check achievement progress thresholds |
| lib-achievement | Subscribes to `analytics.milestone.reached` to trigger milestone-based achievements |
| lib-leaderboard | Subscribes to `analytics.score.updated` to update leaderboard entries |
| lib-leaderboard | Subscribes to `analytics.rating.updated` to update skill rating leaderboards |
| lib-divine | Subscribes to `analytics.score.updated` for divine intervention triggers (stub) |

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
| `RatingIndexMaxRetries` | `ANALYTICS_RATING_INDEX_MAX_RETRIES` | 3 | Max optimistic concurrency retries for rating reverse index operations |
| `MilestoneThresholds` | `ANALYTICS_MILESTONE_THRESHOLDS` | [10,25,50,...] | Score thresholds that trigger milestone events (integer array, comma-separated in env var) |
| `EventBufferLockExpiryBaseSeconds` | `ANALYTICS_EVENT_BUFFER_LOCK_EXPIRY_BASE_SECONDS` | 10 | Base distributed lock expiry (actual = max(this, 2x flush interval)) |
| `RatingUpdateLockExpirySeconds` | `ANALYTICS_RATING_UPDATE_LOCK_EXPIRY_SECONDS` | 30 | Distributed lock expiry for skill rating update operations |
| `ControllerHistoryRetentionDays` | `ANALYTICS_CONTROLLER_HISTORY_RETENTION_DAYS` | 90 | Days to retain controller history records (0 = indefinite) |
| `ControllerHistoryCleanupBatchSize` | `ANALYTICS_CONTROLLER_HISTORY_CLEANUP_BATCH_SIZE` | 5000 | Maximum records to delete per cleanup invocation |
| `ControllerHistoryCleanupSubBatchSize` | `ANALYTICS_CONTROLLER_HISTORY_CLEANUP_SUB_BATCH_SIZE` | 100 | Records to delete per iteration within a cleanup batch |
| `ControllerHistoryCleanupStartupDelaySeconds` | `ANALYTICS_CONTROLLER_HISTORY_CLEANUP_STARTUP_DELAY_SECONDS` | 30 | Startup delay before first cleanup cycle |
| `ControllerHistoryCleanupIntervalSeconds` | `ANALYTICS_CONTROLLER_HISTORY_CLEANUP_INTERVAL_SECONDS` | 3600 | Interval between cleanup cycles (1 hour default) |

## Visual Aid

```
Event Sources Analytics Service Consumers

game-session.action.performed --+
game-session.created -----------+ +---------------------+
game-session.deleted -----------+---> | Event Buffer |
character-history.* ------------+ | (Redis Sorted Set) |
realm-history.* ----------------+ +----------+----------+
 | |
 v (size >= EventBufferSize OR
 +--------------+ age >= FlushIntervalSeconds)
 | Resolution | |
 | character-> | +---------v---------+
 | realm-> | | Flush (locked) |
 | gameService | | Group by entity |
 | (cached) | | Update summaries |
 +--------------+ +--+------------+----+
 | |
 +-----------------v--+ +----v---------------+
 | analytics.score | | analytics |
 | .updated | | .milestone |
 | | | .reached |
 +------+-------------+ +------+-------------+
 | |
 +-------+-------+ +--------+-------+
 | Leaderboard | | Achievement |
 | Service | | Service |
 +---------------+ +----------------+

Direct API
 +---------------------+
/rating/update ------> | Lock (game+type) |
 | Snapshot ratings |
 | Glicko-2 Calc All |
 | Save All |---> analytics.rating.updated
 +---------------------+ |
 +------+------+
 | Leaderboard |
 +-------------+
```

## Stubs & Unimplemented Features

### Rating Period Decay
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/249 -->

The Glicko-2 algorithm includes a concept of "rating period decay" where a player's rating deviation increases over time when they don't play. The `CalculateGlicko2Update` handles the no-games case (deviation increases by volatility), but there is no scheduled task or event that triggers this decay for inactive players. Players who stop playing retain their last RD indefinitely.

### Per-Game Milestone Definitions
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/602 -->

Milestones are configurable via `MilestoneThresholds` as a global comma-separated list. There is no API to define custom milestone values per game service or score type. All entities use the same configured threshold set regardless of context.

## Potential Extensions

- **Auth audit event consumption**: Security monitoring, anomaly detection, and IP geolocation enrichment from auth audit events (login success/fail, registration, OAuth, MFA). Auth publishes 12 well-typed events with IP addresses — Analytics is the natural consumer for cross-account correlation and admin alerting.
<!-- AUDIT:EXTERNAL:2026-03-13:https://github.com/beyond-immersion/bannou-service/issues/142 -->
<!-- AUDIT:EXTERNAL:2026-03-13:https://github.com/beyond-immersion/bannou-service/issues/639 -->
- **Event replay**: Ability to reprocess buffered events after a bug fix (currently events are deleted after processing)
<!-- AUDIT:NEEDS_DESIGN:2026-03-11:https://github.com/beyond-immersion/bannou-service/issues/634 -->
- **Time-series queries**: Adding time-bucketed aggregations for trend analysis
<!-- AUDIT:NEEDS_DESIGN:2026-03-11:https://github.com/beyond-immersion/bannou-service/issues/635 -->
- **Declarative event accumulation engine**: Generic registration-driven accumulation system where services register templates defining which events to watch, what fields to extract (reusing Core SDK's `ResponseTransformation` / JsonPath infrastructure), how to accumulate (Sum/Count/Min/Max/Latest), and where to store results (category + field name). DI Provider inversion: `IAnalyticsAccumulationRegistrant` for registration discovery, `IAccumulatedDataProvider` for query. Partitioned single-consumer queues (`x-single-active-consumer`) for multi-node safety. In-memory batching with periodic MySQL persistence for restart recovery. First consumer: Currency TotalMinted/TotalBurned (#211). See [#703](https://github.com/beyond-immersion/bannou-service/issues/703).
<!-- AUDIT:DESIGN_RESOLVED:2026-03-20:https://github.com/beyond-immersion/bannou-service/issues/703 -->

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. ~~**Incomplete `account.deleted` cleanup — Redis skill ratings orphaned**~~: **FIXED** (2026-03-20) - Added reverse index `account-rating-index:{accountId}` on `analytics-rating` (Redis) maintained via `AddToStringListAsync` in `UpdateSkillRatingAsync` for Account-typed entities. `CleanupDataForAccountAsync` now reads the index, deletes each rating key, then deletes the index itself. Per-item error isolation on rating deletions. Added `RatingIndexMaxRetries` configuration property (default: 3).

2. ~~**Inline key interpolation bypasses Build\*Key() methods in cache handlers**~~: **FIXED** (2026-03-20) - Fixed 6 inline `$"{PREFIX}:{id}"` interpolation sites in `AnalyticsService.cs` (4 sites in `ResolveGameServiceIdForRealmAsync` and `ResolveGameServiceIdForCharacterAsync`) and `AnalyticsService.Events.cs` (2 sites in cache invalidation handlers) to use the existing `BuildRealmGameServiceCacheKey()` and `BuildCharacterRealmCacheKey()` methods. Per Foundation Tenets (key builders must be used at all call sites).

### Intentional Quirks (Documented Behavior)

1. **GetSkillRating returns default values instead of 404**: When no rating exists for an entity, the endpoint returns 200 with default rating values (1500/350/0.06) and `MatchesPlayed = 0`. New players start at the default rating.

2. **Score events published during flush, not during ingestion**: `analytics.score.updated` events are published only when the buffer flushes. Leaderboard and achievement updates are delayed by up to `EventBufferFlushIntervalSeconds` (default 5s) to reduce event storm pressure.

3. **History event resolution drops events on failure**: If the realm or character lookup fails when handling history events, the event is silently dropped (not retried). An error event is published for monitoring, but the analytics data is permanently lost. Incorrect data (wrong GameServiceId) is considered worse than missing data.

4. **Cache invalidation is best-effort**: Handlers for `character.updated` and `realm.updated` events catch exceptions and log warnings but do not fail. Stale cache entries will eventually expire via TTL.

5. **`string.Empty` default for internal POCO string fields**: `BufferedAnalyticsEvent.EventType`, `GameSessionMappingData.GameType`, and `SkillRatingData.RatingType` use `= string.Empty` defaults. These are standard NRT compliance — every construction site sets these fields explicitly, and the defaults only serve as deserialization safety for non-nullable string properties.

### Design Considerations (Requires Planning)

None currently identified.

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above.

### Design Resolved (Awaiting Implementation)

- [#703](https://github.com/beyond-immersion/bannou-service/issues/703) - Declarative event accumulation engine with PreboundApi-style registration. DI Provider inversion (`IAnalyticsAccumulationRegistrant` + `IAccumulatedDataProvider`), partitioned single-consumer queues for multi-node safety (`x-single-active-consumer`), in-memory batching with periodic MySQL persistence, `JsonPathIn` condition extension for OR-style event filtering. First consumer: Currency TotalMinted/TotalBurned (#211).
