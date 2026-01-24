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
| `analytics-session-mapping:{sessionId}` | analytics-summary (Redis) | Game session to game service ID cache (TTL: SummaryCacheTtlSeconds) |
| `analytics-game-service-cache:{stubName}` | analytics-summary (Redis) | Game type stub to service ID cache (TTL: SummaryCacheTtlSeconds) |
| `analytics-realm-game-service-cache:{realmId}` | analytics-summary (Redis) | Realm to game service ID cache (TTL: SummaryCacheTtlSeconds) |
| `analytics-character-realm-cache:{characterId}` | analytics-summary (Redis) | Character to realm ID cache (TTL: SummaryCacheTtlSeconds) |
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
| `SummaryCacheTtlSeconds` | `ANALYTICS_SUMMARY_CACHE_TTL_SECONDS` | 300 | TTL for game service, session, realm, and character mapping caches |
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

### Custom Milestone Definitions

Milestones are defined as a hardcoded array of thresholds. There is no API to define custom milestone values per game service or score type. All entities use the same threshold set regardless of context.

## Potential Extensions

- **Rating period scheduling**: A background task that periodically increases RD for inactive players (common in competitive games)
- **Custom milestones**: Configuration or API for game-specific milestone definitions
- **Event replay**: Ability to reprocess buffered events after a bug fix (currently events are deleted after processing)
- **Time-series queries**: Adding time-bucketed aggregations for trend analysis
- **Realm EntityType**: Adding a `Realm` value to the `EntityType` enum to properly distinguish realm analytics from custom entities

## Known Quirks & Caveats

### Intentional Behavior

1. **Event ingestion requires Redis backend for buffer operations**: `EnsureSummaryStoreRedisAsync()` validates the `analytics-summary` store is Redis before buffer operations. The sorted set operations used for event buffering are Redis-specific. Summary data itself is persisted to MySQL (`analytics-summary-data`) during flush for queryability, while the buffer remains in Redis for high-throughput ingestion.

2. **`GetSkillRating` returns default values instead of 404**: When no rating exists for an entity, the endpoint returns 200 with default rating values (1500/350/0.06) and `MatchesPlayed = 0`. This is intentional - callers should always get a usable rating without checking for 404. New players start at the default rating.

3. **Session ID used as EntityId for game action events**: `HandleGameActionPerformedAsync` uses `evt.SessionId` as both `EntityId` and `SessionId`. This tracks session-level aggregates (total actions per session), not per-player stats. Per-player analytics require the game to emit events with player entity IDs directly via the `/analytics/event/ingest` endpoint.

4. **Flush uses double-check pattern with lock**: The flush logic checks whether to flush BEFORE acquiring the lock, then re-checks AFTER acquiring it. This prevents the common race condition where multiple instances simultaneously decide to flush, acquire the lock sequentially, and the second instance flushes an empty buffer unnecessarily.

5. **Score events published during flush, not during ingestion**: `analytics.score.updated` events are published only when the buffer flushes, not when events are ingested. This means leaderboard and achievement updates are delayed by up to `EventBufferFlushIntervalSeconds` (default 5s). This is intentional - batching reduces event storm pressure on downstream services.

6. **History event resolution drops events on failure**: If the realm or character lookup fails when handling character-history or realm-history events, the event is silently dropped (not retried). An error event is published via `TryPublishErrorAsync` for monitoring, but the analytics data is permanently lost for that event. This follows the principle that incorrect data (wrong GameServiceId) is worse than missing data.

### Design Considerations (Requires Planning)

1. **Session mapping TTL too short for long sessions**: `SaveGameSessionMappingAsync` uses `SummaryCacheTtlSeconds` (default 300s = 5 min) as TTL. Game sessions can last hours. After expiry, every action event requires a full game-session service lookup, adding latency and inter-service traffic. The mapping should either have a much longer TTL, no TTL (cleaned up on session deletion), or use a separate TTL configuration.

2. **Milestone thresholds are hardcoded**: The array defines thresholds as `{ 10, 25, 50, 100, 250, 500, 1000, ... }`. Per the tenets, hardcoded tunables should be configuration properties. Different games may want different milestone progressions. This should be either a configuration property or a per-game-service API.

3. **Summary data config name is misleading**: `SummaryCacheTtlSeconds` is used for session mappings, game service caches, realm caches, and character caches, but NOT for entity summary data. Summaries persist in MySQL indefinitely. The configuration name implies summaries are cached with this TTL, but they're not. Consider renaming to clarify scope.

4. **Realm events use `EntityType.Custom`**: Realm participation/lore events use `EntityType.Custom` because the enum lacks a Realm value. This makes realm analytics indistinguishable from other custom entities in queries. Adding a `Realm` value to the EntityType enum (in the schema) would fix this.

5. **Resolution caches have no invalidation**: Cached game service, realm-to-gameService, and character-to-realm lookups persist for `SummaryCacheTtlSeconds`. If a realm's game service assignment changes or a character moves realms, stale mappings serve incorrect game service IDs until TTL expires. Either subscribe to realm/character change events for cache invalidation, or accept the 5-minute staleness window.

6. **Plugin lifecycle holds service reference past scope disposal**: `AnalyticsServicePlugin.OnStartAsync` creates a DI scope with `using var scope`, resolves `IAnalyticsService`, stores the reference as `_service`, then the scope is disposed at method exit. For scoped services, the stored reference is technically invalid after scope disposal. The reference continues to be used in `OnRunningAsync` and `OnShutdownAsync`. In practice this works because the service has no disposable dependencies that the scope would clean up, but it violates DI lifetime expectations.
