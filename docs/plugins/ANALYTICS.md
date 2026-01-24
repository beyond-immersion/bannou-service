# Analytics Plugin Deep Dive

> **Plugin**: lib-analytics
> **Schema**: schemas/analytics-api.yaml
> **Version**: 1.0.0
> **State Stores**: analytics-summary (Redis), analytics-summary-data (MySQL), analytics-rating (Redis), analytics-history (Redis), analytics-history-data (MySQL)

## Overview

The Analytics plugin is the central event aggregation point for all game-related statistics. It handles event ingestion (buffered via Redis sorted sets), entity summary computation, Glicko-2 skill rating calculations, and controller history tracking. It publishes score updates and milestone events that are consumed by the Achievement and Leaderboard services for downstream processing. It subscribes to game session lifecycle events and character/realm history events to automatically ingest analytics data, resolving game service context via cached realm/character lookups.

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
| AppConfiguration (DI singleton) | Not directly used (no cross-cutting config needed) |

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

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------
| `analytics.score.updated` | `AnalyticsScoreUpdatedEvent` | During buffer flush, for each event with non-zero value |
| `analytics.rating.updated` | `AnalyticsRatingUpdatedEvent` | After all players' Glicko-2 ratings are saved (batch publish) |
| `analytics.milestone.reached` | `AnalyticsMilestoneReachedEvent` | When a score crosses a milestone threshold |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `game-session.action.performed` | `HandleGameActionPerformedAsync` | Resolves game service via session mapping, buffers action event |
| `game-session.created` | `HandleGameSessionCreatedAsync` | Saves session-to-gameService mapping, buffers creation event |
| `game-session.deleted` | `HandleGameSessionDeletedAsync` | Removes session mapping, buffers deletion event |
| `character-history.participation.recorded` | `HandleCharacterParticipationRecordedAsync` | Resolves game service via character→realm→gameService chain, buffers event |
| `character-history.backstory.created` | `HandleCharacterBackstoryCreatedAsync` | Resolves game service via character→realm→gameService chain, buffers event |
| `character-history.backstory.updated` | `HandleCharacterBackstoryUpdatedAsync` | Resolves game service via character→realm→gameService chain, buffers event |
| `realm-history.participation.recorded` | `HandleRealmParticipationRecordedAsync` | Resolves game service via realm lookup, buffers event |
| `realm-history.lore.created` | `HandleRealmLoreCreatedAsync` | Resolves game service via realm lookup, buffers event |
| `realm-history.lore.updated` | `HandleRealmLoreUpdatedAsync` | Resolves game service via realm lookup, buffers event |
| `character.updated` | `HandleCharacterUpdatedForCacheInvalidationAsync` | Invalidates character-to-realm resolution cache |
| `realm.updated` | `HandleRealmUpdatedForCacheInvalidationAsync` | Invalidates realm-to-gameService resolution cache |

All history event handlers follow the fail-fast pattern: if game service resolution fails (realm/character not found, service unavailable), the event is dropped with error logging and an error event published via `TryPublishErrorAsync`. Events are never buffered with incorrect game service IDs.

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------
| `EventBufferSize` | `ANALYTICS_EVENT_BUFFER_SIZE` | 1000 | Max events to buffer before flush |
| `EventBufferFlushIntervalSeconds` | `ANALYTICS_EVENT_BUFFER_FLUSH_INTERVAL_SECONDS` | 5 | Time-based flush trigger for oldest buffered event |
| `Glicko2DefaultRating` | `ANALYTICS_GLICKO2_DEFAULT_RATING` | 1500.0 | Starting Glicko-2 rating for new entities |
| `Glicko2DefaultDeviation` | `ANALYTICS_GLICKO2_DEFAULT_DEVIATION` | 350.0 | Starting rating deviation (max uncertainty) |
| `Glicko2DefaultVolatility` | `ANALYTICS_GLICKO2_DEFAULT_VOLATILITY` | 0.06 | Starting volatility (standard value) |
| `Glicko2SystemConstant` | `ANALYTICS_GLICKO2_SYSTEM_CONSTANT` | 0.5 | Tau - controls how quickly volatility changes |
| `ResolutionCacheTtlSeconds` | `ANALYTICS_RESOLUTION_CACHE_TTL_SECONDS` | 300 | TTL for resolution caches (game service, realm, character lookups) |
| `SessionMappingTtlSeconds` | `ANALYTICS_SESSION_MAPPING_TTL_SECONDS` | 3600 | TTL for game session mappings (should exceed typical session duration) |
| `MilestoneThresholds` | `ANALYTICS_MILESTONE_THRESHOLDS` | 10,25,50,... | Comma-separated score thresholds that trigger milestone events |
| `EventBufferLockExpiryBaseSeconds` | `ANALYTICS_EVENT_BUFFER_LOCK_EXPIRY_BASE_SECONDS` | 10 | Base distributed lock expiry (actual = max(this, 2x flush interval)) |
| `RatingUpdateLockExpirySeconds` | `ANALYTICS_RATING_UPDATE_LOCK_EXPIRY_SECONDS` | 30 | Distributed lock expiry for skill rating update operations |
| `Glicko2VolatilityConvergenceTolerance` | `ANALYTICS_GLICKO2_VOLATILITY_CONVERGENCE_TOLERANCE` | 1e-06 | Convergence tolerance for volatility iteration |
| `ControllerHistoryRetentionDays` | `ANALYTICS_CONTROLLER_HISTORY_RETENTION_DAYS` | 90 | Days to retain controller history records (0 = indefinite) |
| `ControllerHistoryCleanupBatchSize` | `ANALYTICS_CONTROLLER_HISTORY_CLEANUP_BATCH_SIZE` | 5000 | Maximum records to delete per cleanup invocation |

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
| `IEventConsumer` | Registers handlers for 9 consumed event types |

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

game-session.action.performed ──┐
game-session.created ───────────┤    ┌─────────────────────┐
game-session.deleted ───────────┼──► │  Event Buffer       │
character-history.* ────────────┤    │  (Redis Sorted Set) │
realm-history.* ────────────────┘    └────────┬────────────┘
        │                                     │
        ▼                       (size >= EventBufferSize OR
  ┌──────────────┐               age >= FlushIntervalSeconds)
  │ Resolution   │                            │
  │ character→   │                  ┌─────────▼─────────┐
  │ realm→       │                  │  Flush (locked)    │
  │ gameService  │                  │  Group by entity   │
  │ (cached)     │                  │  Update summaries  │
  └──────────────┘                  └──┬────────────┬────┘
                                       │            │
                     ┌─────────────────▼──┐   ┌────▼─────────────┐
                     │ analytics.score    │   │ analytics        │
                     │ .updated           │   │ .milestone       │
                     │                    │   │ .reached         │
                     └──────┬─────────────┘   └──────┬───────────┘
                            │                        │
                    ┌───────┴───────┐       ┌────────┴───────┐
                    │  Leaderboard  │       │  Achievement   │
                    │  Service      │       │  Service       │
                    └───────────────┘       └────────────────┘

Direct API
                     ┌─────────────────────┐
/rating/update ────► │  Lock (game+type)   │
                     │  Snapshot ratings    │
                     │  Glicko-2 Calc All  │
                     │  Save All           │──► analytics.rating.updated
                     └─────────────────────┘         │
                                              ┌──────┴──────┐
                                              │ Leaderboard │
                                              └─────────────┘
```

## Stubs & Unimplemented Features

### Rating Period Decay

The Glicko-2 algorithm includes a concept of "rating period decay" where a player's rating deviation increases over time when they don't play. The `CalculateGlicko2Update` handles the no-games case (deviation increases by volatility), but there is no scheduled task or event that triggers this decay for inactive players. Players who stop playing retain their last RD indefinitely.

### Per-Game Milestone Definitions

Milestones are configurable via `MilestoneThresholds` as a global comma-separated list. There is no API to define custom milestone values per game service or score type. All entities use the same configured threshold set regardless of context.

## Potential Extensions

- **Rating period scheduling**: A background task that periodically increases RD for inactive players (common in competitive games)
- **Per-game milestones**: API for game-specific milestone definitions (currently global config only)
- **Event replay**: Ability to reprocess buffered events after a bug fix (currently events are deleted after processing)
- **Time-series queries**: Adding time-bucketed aggregations for trend analysis

## Known Quirks & Caveats

### Intentional Behavior

1. **Event ingestion requires Redis backend for buffer operations**: `EnsureSummaryStoreRedisAsync()` validates the `analytics-summary` store is Redis before buffer operations. The sorted set operations used for event buffering are Redis-specific. Summary data itself is persisted to MySQL (`analytics-summary-data`) during flush for queryability, while the buffer remains in Redis for high-throughput ingestion.

2. **`GetSkillRating` returns default values instead of 404**: When no rating exists for an entity, the endpoint returns 200 with default rating values (1500/350/0.06) and `MatchesPlayed = 0`. This is intentional - callers should always get a usable rating without checking for 404. New players start at the default rating.

3. **Session ID used as EntityId for game action events**: `HandleGameActionPerformedAsync` uses `evt.SessionId` as both `EntityId` and `SessionId`. This tracks session-level aggregates (total actions per session), not per-player stats. Per-player analytics require the game to emit events with player entity IDs directly via the `/analytics/event/ingest` endpoint.

4. **Flush uses double-check pattern with lock**: The flush logic checks whether to flush BEFORE acquiring the lock, then re-checks AFTER acquiring it. This prevents the common race condition where multiple instances simultaneously decide to flush, acquire the lock sequentially, and the second instance flushes an empty buffer unnecessarily.

5. **Score events published during flush, not during ingestion**: `analytics.score.updated` events are published only when the buffer flushes, not when events are ingested. This means leaderboard and achievement updates are delayed by up to `EventBufferFlushIntervalSeconds` (default 5s). This is intentional - batching reduces event storm pressure on downstream services.

6. **History event resolution drops events on failure**: If the realm or character lookup fails when handling character-history or realm-history events, the event is silently dropped (not retried). An error event is published via `TryPublishErrorAsync` for monitoring, but the analytics data is permanently lost for that event. This follows the principle that incorrect data (wrong GameServiceId) is worse than missing data.

7. **Resolution caches are event-invalidated**: The character-to-realm and realm-to-gameService resolution caches subscribe to `character.updated` and `realm.updated` lifecycle events. When a character's realm or a realm's game service changes, the cached entry is immediately deleted. This ensures cache staleness is bounded to in-flight lookups only, not the full TTL window.

## Tenet Violations (Fix Immediately)

1. [IMPLEMENTATION] **Null-forgiving operator (`!`) used in ParseMilestoneThresholds** -
   File: `plugins/lib-analytics/AnalyticsService.cs`, line 117.
   The code `.Select(v => v!.Value)` uses a null-forgiving operator. Although the preceding `.Where(v => v.HasValue)` guarantees non-null, CLAUDE.md explicitly prohibits `variable!` usage anywhere in Bannou code. Should be rewritten to avoid the `!` operator, e.g., by using `.Select(v => v.GetValueOrDefault())` or pattern matching with `OfType<int>()` after filtering.

2. [IMPLEMENTATION] **Missing null checks in constructor (T6 violation)** -
   File: `plugins/lib-analytics/AnalyticsService.cs`, lines 87-95.
   The constructor assigns all dependencies directly without null validation: `_messageBus = messageBus;`, `_stateStoreFactory = stateStoreFactory;`, etc. Per T6 (Service Implementation Pattern), all dependencies MUST use `?? throw new ArgumentNullException(nameof(...))` or `ArgumentNullException.ThrowIfNull(...)`. All 9 injected parameters lack null guards.

3. [IMPLEMENTATION] **ApiException catches use LogError instead of LogWarning (T7 violation)** -
   File: `plugins/lib-analytics/AnalyticsService.cs`, lines 1277, 1369, 1480, 1567.
   Per T7, `ApiException` is an expected API error and MUST be logged at Warning level. The code uses `_logger.LogError(ex, "Failed to resolve game service...")` for all ApiException catches. Should use `_logger.LogWarning`. Additionally, these emit error events via `TryPublishErrorAsync` which is incorrect for expected API failures per T7 ("Do NOT emit for: Not found (404)").

4. [IMPLEMENTATION] **Hardcoded tunables in Glicko-2 clamping (T21 violation)** -
   File: `plugins/lib-analytics/AnalyticsService.cs`, line 1030.
   `Math.Clamp(newRating, 100, 4000)` uses hardcoded min/max rating bounds. Line 1031: `Math.Clamp(newRD, 30, maxDeviation)` uses a hardcoded minimum RD of 30. Per T21, all tunable values (limits, thresholds, capacities) MUST be configuration properties. Should be `_configuration.Glicko2MinRating`, `_configuration.Glicko2MaxRating`, and `_configuration.Glicko2MinDeviation`.

5. [IMPLEMENTATION] **Hardcoded cleanup sub-batch size (T21 violation)** -
   File: `plugins/lib-analytics/AnalyticsService.cs`, line 929.
   `Math.Min(100, ...)` uses a hardcoded value of 100 for the per-iteration delete batch limit during cleanup. Per T21, this tunable should be a configuration property (e.g., `ControllerHistoryCleanupSubBatchSize`).

6. [IMPLEMENTATION] **Hardcoded MaxVolatilityIterations constant (T21 violation)** -
   File: `plugins/lib-analytics/AnalyticsService.cs`, line 67.
   `private const int MaxVolatilityIterations = 100;` is a hardcoded tunable controlling convergence. Per T21, any numeric literal representing a limit must be a configuration property. Should be `_configuration.Glicko2MaxVolatilityIterations`.

7. [QUALITY] **Unused `using System.Text.Json` import** -
   File: `plugins/lib-analytics/AnalyticsService.cs`, line 17.
   The `System.Text.Json` namespace is imported but `JsonSerializer`, `JsonDocument`, `JsonElement`, and `JsonValueKind` are never used anywhere in the file. This import should be removed. If it were used for direct serialization, it would also violate T20 (BannouJson only).

8. [IMPLEMENTATION] **`?? string.Empty` used without justification (CLAUDE.md violation)** -
   File: `plugins/lib-analytics/AnalyticsService.cs`, line 1942.
   `summaryEtag ?? string.Empty` coerces a potentially null ETag to empty string. The `summaryEtag` variable comes from `GetWithETagAsync` where a null ETag indicates a new entity (no prior version). The `TrySaveAsync` API should receive the actual null to indicate "create if not exists" vs the empty string which may have different semantics. If the API requires non-null, add a comment per CLAUDE.md acceptable patterns explaining the compiler satisfaction case.

9. [IMPLEMENTATION] **`string.Empty` default for internal POCO string fields (potential T25 boundary)** -
   File: `plugins/lib-analytics/AnalyticsService.cs`, lines 2113, 2135, 2162.
   `BufferedAnalyticsEvent.EventType`, `GameSessionMappingData.GameType`, and `SkillRatingData.RatingType` use `= string.Empty` defaults. While these are strings (not enums or GUIDs) so T25 is not strictly violated, the pattern silently allows empty-string entities which could mask bugs. Consider whether these should throw on empty/null assignment rather than defaulting to empty.

10. [IMPLEMENTATION] **Local `Dictionary<string, SkillRatingData>` in UpdateSkillRatingAsync (T9 consideration)** -
    File: `plugins/lib-analytics/AnalyticsService.cs`, line 565.
    `var currentRatings = new Dictionary<string, SkillRatingData>();` uses a plain `Dictionary` rather than `ConcurrentDictionary`. Per T9, local caches should use `ConcurrentDictionary` for thread safety. While this dictionary is method-local and protected by the distributed lock (making concurrent access unlikely within a single request), T9 explicitly states "Use ConcurrentDictionary for local caches, never plain Dictionary." The same applies to line 1860 `var eventsByEntity = new Dictionary<...>()`.

11. [QUALITY] **Operation-entry logging at Information level for high-throughput endpoints (T10 concern)** -
    File: `plugins/lib-analytics/AnalyticsService.cs`, lines 149, 200, 280, 327, 471, 535, 692, 739, 870.
    Per T10, "Operation Entry" should be logged at Debug level. The service logs at Information for every `IngestEvent`, `IngestEventBatch`, `GetEntitySummary`, `QueryEntitySummaries`, `GetSkillRating`, `UpdateSkillRating`, `RecordControllerEvent`, `QueryControllerHistory`, and `CleanupControllerHistory`. For a high-throughput ingestion service, logging every single event at Information creates excessive log volume. These should be `LogDebug` per T10 guidelines.

12. [QUALITY] **Validation failures logged at LogWarning but are not security events (T10 nuance)** -
    File: `plugins/lib-analytics/AnalyticsService.cs`, lines 333, 339, 345, 357, 745, 751.
    Validation failures (invalid limit, offset, minEvents, sortBy, time range) are logged at Warning. Per T10, expected outcomes like validation failures should be logged at Debug level. Warning is reserved for "Security Events: Auth failures, permission denials." Input validation errors are expected business logic, not security concerns.

13. [QUALITY] **Missing XML `<returns>` documentation on private helper methods** -
    File: `plugins/lib-analytics/AnalyticsService.cs`, lines 1141, 1151, 1161, 1206, 1307, 1399, 1417, 1428, 1515, 1597, 1676, 1812.
    Per T19, methods with return values should have `<returns>` documentation. Several private helpers (`BuildResolutionCacheOptions`, `BuildSessionMappingCacheOptions`, `EnsureSummaryStoreRedisAsync`, `ResolveGameServiceIdAsync`, `ResolveGameServiceIdForSessionAsync`, `ResolveGameServiceIdForRealmAsync`, `ResolveGameServiceIdForCharacterAsync`, `BufferAnalyticsEventAsync`, `FlushBufferedEventsIfNeededAsync`) have `<summary>` but lack `<returns>` tags. T19 says "All public classes, interfaces, methods" but private helpers with XML docs should still be complete.

14. [QUALITY] **Missing XML `<param>` tags on constructor** -
    File: `plugins/lib-analytics/AnalyticsService.cs`, lines 75-85.
    The constructor has a `<summary>` (line 73) but no `<param>` tags for its 10 parameters. Per T19, all method parameters should be documented with `<param>` tags.

15. [QUALITY] **Missing XML documentation on `BuildResolutionCacheOptions` and `BuildSessionMappingCacheOptions`** -
    File: `plugins/lib-analytics/AnalyticsService.cs`, lines 1141, 1151.
    These methods have no `<summary>` XML documentation at all. Per T19, all methods should have summary documentation.

16. [IMPLEMENTATION] **Error events emitted for "not found" resolution failures (T7 violation)** -
    File: `plugins/lib-analytics/AnalyticsService.cs`, lines 1248-1258, 1344-1355, 1451-1462, 1538-1549.
    When realm/character/game-service lookups return null (not found), the code logs at Error and publishes error events via `TryPublishErrorAsync`. Per T7, "Do NOT emit for: Not found (404) - expected when resources don't exist." A realm or character not being found is an expected condition (the entity may have been deleted), not an internal error. These should log at Warning and NOT emit error events.

17. [FOUNDATION] **`_serviceScope?.Dispose()` in Plugin without using statement (T24 consideration)** -
    File: `plugins/lib-analytics/AnalyticsServicePlugin.cs`, lines 74, 147.
    The plugin uses manual `.Dispose()` calls on `_serviceScope`. Per T24, this is acceptable since the scope's lifetime extends beyond the method (class-owned resource disposed in shutdown). However, line 74 is in `OnStartAsync` on the error path -- if `_service` is null, the scope is disposed manually in a method context. This could use `using` pattern for the error path since ownership is not transferred.

18. [IMPLEMENTATION] **Missing `ArgumentNullException.ThrowIfNull` for `eventConsumer` in constructor** -
    File: `plugins/lib-analytics/AnalyticsService.cs`, line 100.
    Per T6, the standard pattern requires `ArgumentNullException.ThrowIfNull(eventConsumer, nameof(eventConsumer));` before calling `RegisterEventConsumers`. The code calls `RegisterEventConsumers(eventConsumer)` directly without null validation, which would throw a NullReferenceException instead of a clear ArgumentNullException if null were passed.
