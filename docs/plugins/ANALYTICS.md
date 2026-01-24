# Analytics Plugin Deep Dive

> **Plugin**: lib-analytics
> **Schema**: schemas/analytics-api.yaml
> **Version**: 1.0.0
> **State Stores**: analytics-summary (Redis), analytics-rating (Redis), analytics-history (Redis)

## Overview

The Analytics plugin is the central event aggregation point for all game-related statistics. It handles event ingestion (buffered via Redis sorted sets), entity summary computation, Glicko-2 skill rating calculations, and controller history tracking. It publishes score updates and milestone events that are consumed by the Achievement and Leaderboard services for downstream processing. It subscribes to game session lifecycle events and character/realm history events to automatically ingest analytics data.

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (IStateStoreFactory) | Event buffer, entity summaries, skill ratings, controller history, session mappings, game service cache |
| lib-state (IDistributedLockProvider) | Distributed lock for buffer flush (prevents duplicate processing across instances) |
| lib-messaging (IMessageBus) | Publishing score/rating/milestone events and error events |
| lib-game-service (IGameServiceClient) | Resolving game type strings to game service IDs for event keying |
| lib-game-session (IGameSessionClient) | Resolving session IDs to game types (fallback when no cached mapping exists) |
| AppConfiguration (DI singleton) | Not directly used (no cross-cutting config needed) |

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-achievement | Subscribes to `analytics.score.updated` to check achievement progress thresholds |
| lib-achievement | Subscribes to `analytics.milestone.reached` to trigger milestone-based achievements |
| lib-leaderboard | Subscribes to `analytics.score.updated` to update leaderboard entries |
| lib-leaderboard | Subscribes to `analytics.rating.updated` to update skill rating leaderboards |

## State Storage

**All stores use Redis backend.** The service explicitly validates this at runtime via `EnsureSummaryStoreRedisAsync()` and returns InternalServerError if the summary store is not Redis.

| Key Pattern | Store | Purpose |
|-------------|-------|---------|
| `{gameServiceId}:{entityType}:{entityId}` | analytics-summary | Entity summary aggregations (event counts, aggregates, timestamps) |
| `analytics-summary-index:{gameServiceId}` | analytics-summary | Set of entity keys per game service (for query enumeration) |
| `analytics-event-buffer-entry:{eventId}` | analytics-summary | Individual buffered event entries awaiting flush |
| `analytics-event-buffer-index` | analytics-summary | Sorted set of buffered event keys (scored by timestamp) |
| `analytics-session-mapping:{sessionId}` | analytics-summary | Game session to game service ID cache (TTL: SummaryCacheTtlSeconds) |
| `analytics-game-service-cache:{stubName}` | analytics-summary | Game type stub to service ID cache (TTL: SummaryCacheTtlSeconds) |
| `{gameServiceId}:{ratingType}:{entityType}:{entityId}` | analytics-rating | Glicko-2 skill rating data per entity per rating type |
| `{gameServiceId}:controller:{accountId}:{timestamp:o}` | analytics-history | Individual controller history events |
| `analytics-controller-index:{gameServiceId}` | analytics-history | Set of controller event keys per game service |
| `analytics-controller-index:{gameServiceId}:account:{accountId}` | analytics-history | Set of controller event keys per account |

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------
| `analytics.score.updated` | `AnalyticsScoreUpdatedEvent` | During buffer flush, for each event with non-zero value |
| `analytics.rating.updated` | `AnalyticsRatingUpdatedEvent` | After each player's Glicko-2 rating is updated |
| `analytics.milestone.reached` | `AnalyticsMilestoneReachedEvent` | When a score crosses a hardcoded milestone threshold |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `game-session.action.performed` | `HandleGameActionPerformedAsync` | Buffers action event with session ID as entity |
| `game-session.created` | `HandleGameSessionCreatedAsync` | Saves session-to-gameService mapping, buffers creation event |
| `game-session.deleted` | `HandleGameSessionDeletedAsync` | Removes session mapping, buffers deletion event |
| `character-history.participation.recorded` | `HandleCharacterParticipationRecordedAsync` | Buffers participation event (game-agnostic, GameServiceId=Empty) |
| `character-history.backstory.created` | `HandleCharacterBackstoryCreatedAsync` | Buffers backstory creation event |
| `character-history.backstory.updated` | `HandleCharacterBackstoryUpdatedAsync` | Buffers backstory update event |
| `realm-history.participation.recorded` | `HandleRealmParticipationRecordedAsync` | Buffers realm participation event |
| `realm-history.lore.created` | `HandleRealmLoreCreatedAsync` | Buffers realm lore creation event |
| `realm-history.lore.updated` | `HandleRealmLoreUpdatedAsync` | Buffers realm lore update event |

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------
| `EventBufferSize` | `ANALYTICS_EVENT_BUFFER_SIZE` | 1000 | Max events to buffer before flush |
| `EventBufferFlushIntervalSeconds` | `ANALYTICS_EVENT_BUFFER_FLUSH_INTERVAL_SECONDS` | 5 | Time-based flush trigger for oldest buffered event |
| `Glicko2DefaultRating` | `ANALYTICS_GLICKO2_DEFAULT_RATING` | 1500.0 | Starting Glicko-2 rating for new entities |
| `Glicko2DefaultDeviation` | `ANALYTICS_GLICKO2_DEFAULT_DEVIATION` | 350.0 | Starting rating deviation (max uncertainty) |
| `Glicko2DefaultVolatility` | `ANALYTICS_GLICKO2_DEFAULT_VOLATILITY` | 0.06 | Starting volatility (standard value) |
| `Glicko2SystemConstant` | `ANALYTICS_GLICKO2_SYSTEM_CONSTANT` | 0.5 | Tau - controls how quickly volatility changes |
| `SummaryCacheTtlSeconds` | `ANALYTICS_SUMMARY_CACHE_TTL_SECONDS` | 300 | TTL for game service and session mapping caches |
| `EventBufferLockExpiryBaseSeconds` | `ANALYTICS_EVENT_BUFFER_LOCK_EXPIRY_BASE_SECONDS` | 10 | Base distributed lock expiry (actual = max(this, 2x flush interval)) |
| `Glicko2VolatilityConvergenceTolerance` | `ANALYTICS_GLICKO2_VOLATILITY_CONVERGENCE_TOLERANCE` | 1e-06 | Convergence tolerance for volatility iteration |

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<AnalyticsService>` | Structured logging |
| `AnalyticsServiceConfiguration` | Typed config access |
| `IStateStoreFactory` | Multi-store access (summary, rating, history) |
| `IDistributedLockProvider` | Flush lock coordination |
| `IMessageBus` | Event publishing and error reporting |
| `IGameServiceClient` | Game type to service ID resolution |
| `IGameSessionClient` | Session ID to game type resolution (fallback) |
| `IEventConsumer` | Registers handlers for 9 consumed event types |

## API Endpoints (Implementation Notes)

### Event Ingestion (`/analytics/event/ingest`, `/analytics/event/ingest-batch`)

Events are buffered in Redis using a two-part structure: individual event entries keyed by `analytics-event-buffer-entry:{eventId}`, and a sorted set `analytics-event-buffer-index` scored by event timestamp in milliseconds. Flush is triggered when either: (a) the sorted set size reaches `EventBufferSize`, or (b) the oldest entry is older than `EventBufferFlushIntervalSeconds`. A distributed lock prevents concurrent flush from multiple instances. The flush processes events in batches grouped by entity, updating each entity's summary with optimistic concurrency (ETag-based `TrySaveAsync`). On ETag conflict, the batch for that entity is skipped (will be retried next flush).

### Entity Summary (`/analytics/summary/get`, `/analytics/summary/query`)

Get returns a single entity's aggregated statistics by composite key. Query enumerates the summary index set for a game service, loads all summaries, applies in-memory filtering (entityType, eventType, minEvents), sorts, and paginates. Supported sort fields: `totalevents`, `firsteventat`, `lasteventat`, `eventcount` (case-insensitive).

### Skill Rating (`/analytics/rating/get`, `/analytics/rating/update`)

Get returns the current Glicko-2 rating or default values if no rating exists (not 404). Update takes a match with 2+ results, loads current ratings with ETags, calculates Glicko-2 updates for all participants, saves with optimistic concurrency, and publishes `analytics.rating.updated` per player. Returns Conflict if any save has an ETag mismatch.

### Controller History (`/analytics/controller-history/record`, `/analytics/controller-history/query`)

Records are stored individually and indexed in two sets: by game service (all events) and by account (per-account events). Query uses the account-specific or global index depending on whether `AccountId` is provided, loads all events from the index, filters in memory, sorts by timestamp descending, and applies limit.

## Visual Aid

```
Event Sources                    Analytics Service                    Consumers

game-session.action.performed ──┐
game-session.created ───────────┤    ┌─────────────────────┐
game-session.deleted ───────────┼──► │  Event Buffer       │
character-history.* ────────────┤    │  (Redis Sorted Set) │
realm-history.* ────────────────┘    └────────┬────────────┘
                                              │
                          (size >= EventBufferSize OR
                           age >= FlushIntervalSeconds)
                                              │
                                    ┌─────────▼─────────┐
                                    │  Flush (locked)    │
                                    │  Group by entity   │
                                    │  Update summaries  │
                                    └──┬────────────┬────┘
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
/rating/update ────► │  Glicko-2 Calc      │──► analytics.rating.updated
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

### Bugs (Fix Immediately)

1. **Glicko-2 uses player's own outcome for all opponent pairs**: In `UpdateSkillRatingAsync` (line 551), the `result.Outcome` (the player's global match outcome) is used as the expected result against every opponent. The Glicko-2 algorithm expects pairwise outcomes: if player A placed 2nd in a 4-player match, their outcome should be 1.0 vs players 3-4 and 0.0 vs player 1. The current code assigns the same outcome (e.g., 0.5 for a "middle placement") to all opponent pairs, producing incorrect rating adjustments for matches with more than 2 participants. For 1v1 matches this is correct.

2. **Milestone `break` permanently loses intermediate milestones**: `CheckAndPublishMilestoneAsync` (line 1681) breaks after publishing the first milestone crossed. If a score jumps from 0 to 200, only milestone 10 is published; milestones 25, 50, and 100 are permanently missed because subsequent events will have `previousValue >= 200` and never satisfy `previousValue < 25/50/100` again. The comment says "Only publish one milestone per event" but this causes permanent milestone loss, not deferral.

3. **Buffer error cleanup orphans sorted set entries**: In `BufferAnalyticsEventAsync` (line 1259-1291), if an error occurs after `SortedSetAddAsync` succeeds, the catch handler deletes the event entry but does not remove the corresponding sorted set entry. The orphaned entry persists until the next flush batch encounters it at line 1465 and removes it, but in the meantime it inflates `SortedSetCountAsync` and may trigger unnecessary flushes.

4. **Glicko-2 volatility iteration has no loop guard**: `CalculateNewVolatility` (line 891) uses `while (f(upperBound) < 0) { upperBound -= tau; }` with no iteration limit. For pathological parameter combinations (extremely high delta relative to phi+v), this could loop indefinitely. Standard Glicko-2 implementations include iteration caps.

### Intentional Quirks (Documented Behavior)

1. **Event ingestion requires Redis backend**: `EnsureSummaryStoreRedisAsync()` explicitly validates the backend is Redis and returns InternalServerError if not. The sorted set and set operations used for buffering are Redis-specific features that have no MySQL equivalent. This is validated per-request, not at startup.

2. **Game-agnostic events use `Guid.Empty` for GameServiceId**: Character history and realm history events set `GameServiceId = Guid.Empty` since they're not tied to a specific game service. This creates entity keys like `00000000-...:Character:{id}`, which is a valid composite key that won't collide with real game service IDs (UUIDs are never all-zeros).

3. **`GetSkillRating` returns default values instead of 404**: When no rating exists for an entity, the endpoint returns 200 with default rating values (1500/350/0.06) and `MatchesPlayed = 0`. This is intentional - callers should always get a usable rating without checking for 404. New players start at the default rating.

4. **Session ID used as EntityId for game action events**: `HandleGameActionPerformedAsync` uses `evt.SessionId` as both `EntityId` and `SessionId`. This tracks session-level aggregates (total actions per session), not per-player stats. Per-player analytics require the game to emit events with player entity IDs directly via the `/analytics/event/ingest` endpoint.

5. **Flush uses double-check pattern with lock**: The flush logic checks whether to flush BEFORE acquiring the lock, then re-checks AFTER acquiring it. This prevents the common race condition where multiple instances simultaneously decide to flush, acquire the lock sequentially, and the second instance flushes an empty buffer unnecessarily.

6. **Score events published during flush, not during ingestion**: `analytics.score.updated` events are published only when the buffer flushes, not when events are ingested. This means leaderboard and achievement updates are delayed by up to `EventBufferFlushIntervalSeconds` (default 5s). This is intentional - batching reduces event storm pressure on downstream services.

7. **Rating update returns Conflict on first ETag failure**: If ANY player's rating save fails due to concurrent modification, the entire match result returns 409 Conflict with no partial updates applied to remaining players. However, players processed BEFORE the conflicting one have already been saved - this is a partial-save scenario despite the Conflict response.

### Design Considerations (Requires Planning)

1. **Query loads all summaries into memory**: `QueryEntitySummariesAsync` (lines 362-390) loads every entity summary for a game service into memory, applies filters, then paginates. For game services with thousands or millions of entities, this causes unbounded memory usage and latency. A proper fix requires server-side filtering via Redis SCAN with pattern matching, or moving summaries to MySQL with JSON queries.

2. **Session mapping TTL too short for long sessions**: `SaveGameSessionMappingAsync` uses `SummaryCacheTtlSeconds` (default 300s = 5 min) as TTL. Game sessions can last hours. After expiry, every action event requires a full game-session service lookup, adding latency and inter-service traffic. The mapping should either have a much longer TTL, no TTL (cleaned up on session deletion), or use a separate TTL configuration.

3. **Controller history accumulates indefinitely**: Events in `analytics-history` store have no TTL and no cleanup mechanism. Sets and entries grow without bound over time. Needs a retention policy (time-based TTL, count-based cap, or a cleanup endpoint).

4. **Milestone thresholds are hardcoded**: The array at line 1662 defines thresholds as `{ 10, 25, 50, 100, 250, 500, 1000, ... }`. Per the tenets, hardcoded tunables should be configuration properties. Different games may want different milestone progressions. This should be either a configuration property or a per-game-service API.

5. **Summary data has no TTL (misleading config name)**: `SummaryCacheTtlSeconds` is used for session mappings and game service caches, but NOT for entity summary data. Summaries persist in Redis indefinitely. The configuration name implies summaries are cached with this TTL, but they're not. Either rename the config to clarify scope, or add actual summary TTL support.

6. **Realm events use `EntityType.Custom`**: Realm participation/lore events use `EntityType.Custom` because the enum lacks a Realm value. This makes realm analytics indistinguishable from other custom entities in queries. Adding a `Realm` value to the EntityType enum (in the schema) would require changes to the event models (separate enum per event type) or schema consolidation of the entity type enum.

7. **Game service cache has no invalidation**: Cached game service lookups persist for `SummaryCacheTtlSeconds`. If a game service is updated or deleted, stale mappings serve incorrect game service IDs until TTL expires. Either subscribe to game-service change events, or accept the 5-minute staleness window.

8. **Rating update race condition with partial saves**: In `UpdateSkillRatingAsync`, players are processed sequentially. If player 3's save conflicts, players 1-2 already have updated ratings saved. The response is 409 Conflict, but the caller has no way to know which players were actually updated. A transaction or all-or-nothing approach would require either Redis transactions or a different concurrency strategy.

9. **Plugin lifecycle disposes scope while holding service reference**: `AnalyticsServicePlugin.OnStartAsync` creates a DI scope, resolves `IAnalyticsService`, stores the reference, then disposes the scope via `using`. For scoped services, the stored reference may be in an invalid state after scope disposal. In practice, the stored reference is only used for lifecycle calls (`OnRunningAsync`, `OnShutdownAsync`), not request handling, so this likely works but violates DI lifetime expectations.
