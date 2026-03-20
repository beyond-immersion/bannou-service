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
| `serviceType` | C (System State/Mode) | `AnalyticsServiceType` enum | Discriminates how to interpret `serviceId`. `Game` = GUID referencing a registered GameService entry (existing behavior). `System` = logical service name string for platform-level analytics (e.g., `"auth"`, `"permission"`). Introduced by #142 serviceType/serviceId refactoring. |
| `serviceId` | Polymorphic (T14) | Opaque string | Service scope for analytics data. For `serviceType: Game`, a GUID string resolved via `IGameServiceClient`. For `serviceType: System`, a logical service name string that IS the identity (no resolution). Replaces former `gameServiceId: Guid`. |
| `entityType` | A (Entity Reference) | `EntityType` enum | Identifies what kind of entity analytics are tracked for (account, character, guild, actor). All valid values are first-class Bannou entities. Uses the shared `EntityType` enum from `common-api.yaml`. |
| `targetEntityType` | A (Entity Reference) | `EntityType` enum | Identifies the entity type being controlled in controller history records. Same shared `EntityType` enum. |
| `eventType` | B (Game Content Type) | Opaque string | Type of analytics event (e.g., `"kill"`, `"death"`, `"score"`, `"action"`, `"auth.login.successful"`). Vocabulary defined by game services and system event handlers at runtime through event ingestion. New event types require no schema changes. |
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
<!-- AUDIT:DESIGN_RESOLVED:2026-03-20:https://github.com/beyond-immersion/bannou-service/issues/249 -->

~~The Glicko-2 algorithm includes a concept of "rating period decay" where a player's rating deviation increases over time when they don't play. The `CalculateGlicko2Update` handles the no-games case (deviation increases by volatility), but there is no scheduled task or event that triggers this decay for inactive players. Players who stop playing retain their last RD indefinitely.~~ **DESIGN RESOLVED** (2026-03-20) — See Work Tracking § Design Resolved for the full design. Summary: `AnalyticsRatingDecayWorker` background service with Redis sorted set index (`rating-decay-tracker`) scored by `LastMatchAt` for efficient inactive-rating discovery. Time-based inactivity threshold (`RatingDecayInactivityDays`, default: 30). Reuses existing `analytics.rating.updated` event with nullable `MatchId` (per FOUNDATION TENETS: background state changes publish only lifecycle events, no action event). Self-limiting: worker skips and removes ratings that have reached `Glicko2DefaultDeviation`.

### ~~Per-Game Milestone Definitions~~
<!-- AUDIT:REJECTED:2026-03-20:https://github.com/beyond-immersion/bannou-service/issues/602 -->

~~Milestones are configurable via `MilestoneThresholds` as a global comma-separated list. There is no API to define custom milestone values per game service or score type. All entities use the same configured threshold set regardless of context.~~ **REJECTED** (2026-03-20) — #602 closed as wrong abstraction. Analytics should accumulate scores and publish score changes; milestone interpretation belongs in consuming services. The `analytics.milestone.reached` event, `MilestoneThresholds` config, and `CheckAndPublishMilestoneAsync` are marked for deprecation pending Achievement migration ([#705](https://github.com/beyond-immersion/bannou-service/issues/705)). Achievement will evaluate thresholds against `analytics.score.updated` data using its own `MilestoneValue` definition fields, then migrate off Analytics events entirely to direct domain event subscriptions (`quest.completed`, `collection.unlocked`, `seed.phase.changed`) and direct API progress updates. See Work Tracking.

## Potential Extensions

- **Auth audit event consumption**: Security monitoring, anomaly detection, and IP geolocation enrichment from auth audit events (login success/fail, registration, OAuth, MFA). Auth publishes 12 well-typed events with IP addresses — Analytics is the natural consumer for cross-account correlation and admin alerting. **DESIGN RESOLVED** (2026-03-20) — Three-phase plan: Phase 1 (#142) subscribes to all 12 auth events, ingests into existing pipeline under `serviceType: System, serviceId: "auth"` (requires serviceType/serviceId refactoring from `gameServiceId: Guid`). Phase 2 (#639) adds IP geolocation enrichment via db-ip MMDB + MaxMind.GeoIP2 reader and per-IP entity tracking with deterministic GUIDs. Phase 3 (new issue) adds cross-account IP correlation with time-windowed distinct counting and `SecurityAlertClientEvent` admin alerting. See Work Tracking.
<!-- AUDIT:DESIGN_RESOLVED:2026-03-20:https://github.com/beyond-immersion/bannou-service/issues/142 -->
<!-- AUDIT:DESIGN_RESOLVED:2026-03-20:https://github.com/beyond-immersion/bannou-service/issues/639 -->
- ~~**Event replay**: Ability to reprocess buffered events after a bug fix (currently events are deleted after processing)~~ **REJECTED** (2026-03-20) — #634 closed as wrong-layer. Event archival/replay is an observability stack concern (Loki/OTEL/Grafana, see #183/#185), not Analytics application code. Building a custom event archive duplicates infrastructure-layer observability. For corrupted summary recovery: admin reset endpoint ([#707](https://github.com/beyond-immersion/bannou-service/issues/707)) deletes summaries for a scope, letting them rebuild naturally as new events arrive. Consumer deduplication problem makes replay impractical regardless. Source event data is durable in owning services (character-history, realm-history, game-session MySQL).
<!-- AUDIT:REJECTED:2026-03-20:https://github.com/beyond-immersion/bannou-service/issues/634 -->
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

- [#249](https://github.com/beyond-immersion/bannou-service/issues/249) - Glicko-2 rating period decay. `AnalyticsRatingDecayWorker` background service (canonical T6 polling loop) finds inactive ratings via Redis sorted set index (`rating-decay-tracker` on `analytics-rating`, scored by `LastMatchAt` ms), applies the existing `CalculateGlicko2Update` zero-games formula to increase RD by volatility, and publishes `analytics.rating.updated` with `MatchId = null` (per FOUNDATION TENETS: action events vs lifecycle events). Self-limiting: skips and removes sorted set entries when RD reaches `Glicko2DefaultDeviation`. Time-based inactivity threshold via `RatingDecayInactivityDays` (default: 30). New config: `RatingDecayInactivityDays`, `RatingDecayIntervalSeconds` (3600), `RatingDecayStartupDelaySeconds` (60), `RatingDecayBatchSize` (500), `RatingDecayLockExpirySeconds` (300). Schema change: `matchId` on `AnalyticsRatingUpdatedEvent` becomes nullable. Sorted set maintained by `UpdateSkillRatingAsync` (add/update on match), decay worker (remove at max RD), and `HandleAccountDeletedAsync` (cleanup).

- [#142](https://github.com/beyond-immersion/bannou-service/issues/142) + [#639](https://github.com/beyond-immersion/bannou-service/issues/639) - Auth audit event consumption and IP geolocation enrichment. Three-phase design:
  - **Prerequisite refactoring**: Rename `gameServiceId: Guid` → `serviceType: AnalyticsServiceType` + `serviceId: string` across all Analytics schemas, models, events, and key builders. `AnalyticsServiceType` enum: `Game` (GUID, resolved via IGameServiceClient — existing behavior), `System` (logical service name string, no resolution). Entity key pattern: `analytics-entity:{serviceType}:{serviceId}:{entityType}:{entityId}`. Consumers (Achievement, Leaderboard) filter `serviceType == Game` in event handlers — system analytics don't trigger game-scoped features. Polymorphic association per T14.
  - **Phase 1 (Auth ingestion, #142)**: Subscribe to all 12 auth audit events. Buffer with `serviceType: System, serviceId: "auth"`. Per-account entity summaries (`EntityType.Account, entityId: accountId`). Failed logins with null `accountId` dropped from per-account aggregation. Milestones fire via existing threshold infrastructure. No new config — system service names are self-identifying.
  - **Phase 2 (IP geolocation, #639)**: `IGeolocationService` singleton helper with `[BannouHelperService]`. MaxMind.GeoIP2 reader (Apache 2.0) + db-ip.com "IP to City Lite" MMDB (CC BY 4.0, no account). Config: `GeoIpDatabasePath` (nullable, null = disabled). Enriches auth events at ingestion with country/city. Per-IP entity tracking: deterministic GUID from IP string via namespace UUID v5, `EntityType.Other`. Graceful degradation when MMDB absent.
  - **Phase 3 (Anomaly detection, new issue)**: Cross-account IP correlation with time-windowed distinct counting (Redis Sets with TTL buckets). Configurable thresholds. `SecurityAlertClientEvent` to admin sessions via `IClientEventPublisher` + `IEntitySessionRegistry`. Behavioral baselines require Phase 1+2 data first.

- **Milestone infrastructure deprecation** (pending [#705](https://github.com/beyond-immersion/bannou-service/issues/705) — Achievement migration off Analytics events): `analytics.milestone.reached` event, `MilestoneThresholds` config, and `CheckAndPublishMilestoneAsync` are marked for removal once Achievement no longer subscribes. Achievement is migrating to direct domain event subscriptions (`quest.completed`, `collection.unlocked`, `seed.phase.changed`) and direct API progress updates — Analytics is the most optional plugin and should never be in the critical path for any service's core functionality. #602 (per-game milestones) rejected as wrong abstraction; #690 (Gardener milestone integration) rejected as wrong data source (Gardener should use Seed growth data). Leaderboard's `analytics.score.updated`/`analytics.rating.updated` subscriptions face the same concern — tracked separately in the Leaderboard investigation scope.

- [#429](https://github.com/beyond-immersion/bannou-service/issues/429) (CLOSED — fully decomposed): Economic velocity & distribution extensions. Faucet/sink classification → #703 accumulation engine. Wealth distribution → #470 Currency background worker. Global supply → #211 Currency Redis counters. Velocity measurement → deferred to #635 time-series (derived query: `count(time-bucket) / supply-snapshot`). Divine economic intervention → #538 Worldstate CalendarEvents + NPC GOAP (behavioral management, not statistical monitoring — economy is managed by behavior, measured by Analytics). Location-scoped management → #538 location-scoped CalendarEvents (market days, trade festivals, self-scheduled economic checkpoints as durable `Once` events).

- [#707](https://github.com/beyond-immersion/bannou-service/issues/707) - Admin summary reset endpoint. `POST /analytics/summary/reset` (admin-only) deletes entity summaries for a `serviceType`/`serviceId` scope with optional `entityType`/`entityId` filters. Dry-run mode (default). Summaries rebuild naturally as new events arrive. Replaces #634 (event replay, rejected as wrong-layer — observability stack concern).
