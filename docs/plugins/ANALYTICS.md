# Analytics Plugin Deep Dive

> **Plugin**: lib-analytics
> **Schema**: schemas/analytics-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFeatures
> **State Store**: analytics-summary (Redis), analytics-summary-data (MySQL), analytics-rating (Redis), analytics-history-data (MySQL)
> **Implementation Map**: [docs/maps/ANALYTICS.md](../maps/ANALYTICS.md)

## Overview

The Analytics plugin (L4 GameFeatures) is the central event aggregation point for all game-related statistics. Handles event ingestion, entity summary computation, Glicko-2 skill rating calculations, and controller history tracking. Publishes score updates and milestone events consumed by Achievement and Leaderboard for downstream processing. Subscribes to game session lifecycle and character/realm history events for automatic ingestion. Unlike typical L4 services, Analytics only observes via event subscriptions -- it does not invoke L2/L4 service APIs and should not be called by L1/L2/L3 services.

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-achievement | Subscribes to `analytics.score.updated` to check achievement progress thresholds |
| lib-achievement | Subscribes to `analytics.milestone.reached` to trigger milestone-based achievements |
| lib-leaderboard | Subscribes to `analytics.score.updated` to update leaderboard entries |
| lib-leaderboard | Subscribes to `analytics.rating.updated` to update skill rating leaderboards |
| lib-divine | Subscribes to `analytics.score.updated` for divine intervention triggers (stub) |

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
| `metadata` (on `IngestEventRequest`) | -- (Client Metadata) | `object` (`additionalProperties: true`) | Opaque client-provided event context. Analytics stores and returns this data without inspecting its structure, per T29 (No Metadata Bag Contracts). |

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
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/602 -->

Milestones are configurable via `MilestoneThresholds` as a global comma-separated list. There is no API to define custom milestone values per game service or score type. All entities use the same configured threshold set regardless of context.

## Potential Extensions

- **Rating period scheduling**: A background task that periodically increases RD for inactive players (common in competitive games)
- **Per-game milestones**: API for game-specific milestone definitions (currently global config only)
- **Event replay**: Ability to reprocess buffered events after a bug fix (currently events are deleted after processing)
- **Time-series queries**: Adding time-bucketed aggregations for trend analysis

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. ~~**`QueryControllerHistory` ignores `Offset` parameter**~~: **FIXED** (2026-03-08) - The schema was missing the `offset` field entirely on `QueryControllerHistoryRequest`. Added `offset` (integer, default 0, minimum 0) to the schema, regenerated, updated the method to pass `body.Offset` instead of hardcoded `0`, and added offset validation matching the `QueryEntitySummariesAsync` pattern.

2. ~~**`IngestEvent` drops `SessionId` from request**~~: **FIXED** (2026-03-08) - Added `sessionId` (nullable Guid) to `IngestEventRequest` schema and wired `SessionId = body.SessionId` in both `IngestEventAsync` and `IngestEventBatchAsync`. Downstream consumers (Achievement, Leaderboard) now receive the session ID for API-ingested events via `AnalyticsScoreUpdatedEvent`.

### Intentional Quirks (Documented Behavior)

1. **GetSkillRating returns default values instead of 404**: When no rating exists for an entity, the endpoint returns 200 with default rating values (1500/350/0.06) and `MatchesPlayed = 0`. New players start at the default rating.

2. **Score events published during flush, not during ingestion**: `analytics.score.updated` events are published only when the buffer flushes. Leaderboard and achievement updates are delayed by up to `EventBufferFlushIntervalSeconds` (default 5s) to reduce event storm pressure.

3. **History event resolution drops events on failure**: If the realm or character lookup fails when handling history events, the event is silently dropped (not retried). An error event is published for monitoring, but the analytics data is permanently lost. Incorrect data (wrong GameServiceId) is considered worse than missing data.

4. **Cache invalidation is best-effort**: Handlers for `character.updated` and `realm.updated` events catch exceptions and log warnings but do not fail. Stale cache entries will eventually expire via TTL.

5. **`string.Empty` default for internal POCO string fields**: `BufferedAnalyticsEvent.EventType`, `GameSessionMappingData.GameType`, and `SkillRatingData.RatingType` use `= string.Empty` defaults. These are standard NRT compliance — every construction site sets these fields explicitly, and the defaults only serve as deserialization safety for non-nullable string properties.

### Design Considerations (Requires Planning)

1. **No automatic controller history cleanup** - The `CleanupControllerHistory` endpoint exists but must be called manually (e.g., via scheduled cron job or orchestrator task). There is no background service that automatically purges expired records. For production deployments, consider adding a periodic cleanup task or documenting the requirement for external scheduling.

2. **Constructor injects 4 service clients individually** - The constructor takes `IGameServiceClient`, `IGameSessionClient`, `IRealmClient`, and `ICharacterClient` as separate parameters. Consider whether `IServiceNavigator` would reduce constructor complexity, trading explicit dependencies for cleaner signatures. Currently favoring explicit injection for dependency clarity.

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above.
