# Analytics Implementation Map

> **Plugin**: lib-analytics
> **Schema**: schemas/analytics-api.yaml
> **Layer**: GameFeatures
> **Deep Dive**: [docs/plugins/ANALYTICS.md](../plugins/ANALYTICS.md)
>
> **Planned refactoring**: `gameServiceId: Guid` → `serviceType: AnalyticsServiceType` + `serviceId: string` across all schemas, models, events, and key builders ([#142](https://github.com/beyond-immersion/bannou-service/issues/142)). Key patterns in this map reflect the planned shape. Existing code still uses `gameServiceId`.

---

## Summary Table

| Field | Value |
|-------|-------|
| Plugin | lib-analytics |
| Layer | L4 GameFeatures |
| Endpoints | 9 (9 generated) |
| State Stores | analytics-summary (Redis), analytics-summary-data (MySQL), analytics-rating (Redis), analytics-history-data (MySQL) |
| Events Published | 4 (`analytics.score.updated`, `analytics.rating.updated`, `analytics.milestone.reached`, `analytics.controller.recorded`) |
| Events Consumed | 12 (implemented), 10 planned (auth audit events — [#142](https://github.com/beyond-immersion/bannou-service/issues/142)) |
| Client Events | 0 |
| Background Services | 1 implemented (ControllerHistoryCleanupWorker), 1 planned (AnalyticsRatingDecayWorker — [#249](https://github.com/beyond-immersion/bannou-service/issues/249)) |

---

## State

**Store**: `analytics-summary` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `analytics-event-buffer-entry:{eventId}` | `BufferedAnalyticsEvent` | Individual buffered event entries awaiting flush |
| `analytics-event-buffer-index` (sorted set) | sorted set of entry key strings, scored by timestamp ms | Time-ordered index of buffered events for flush processing |
| `analytics-session-mapping:{sessionId}` | `GameSessionMappingData` | Session-to-gameService-ID cache (TTL: `SessionMappingTtlSeconds`) |
| `analytics-game-service-cache:{stubName}` | `GameServiceCacheEntry` | Game type stub-to-ID resolution cache (TTL: `ResolutionCacheTtlSeconds`) |
| `analytics-realm-game-service-cache:{realmId}` | `RealmGameServiceCacheEntry` | Realm-to-gameService-ID resolution cache (TTL: `ResolutionCacheTtlSeconds`) |
| `analytics-character-realm-cache:{characterId}` | `CharacterRealmCacheEntry` | Character-to-realm-ID resolution cache (TTL: `ResolutionCacheTtlSeconds`) |

**Store**: `analytics-summary-data` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `analytics-entity:{serviceType}:{serviceId}:{entityType}:{entityId}` | `EntitySummaryData` | Entity summary aggregations (event counts, aggregates, timestamps). `serviceType` is `Game` (serviceId = GUID) or `System` (serviceId = service name string e.g. `"auth"`). Planned refactoring from `{gameServiceId}` — [#142](https://github.com/beyond-immersion/bannou-service/issues/142). |

**Store**: `analytics-rating` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `analytics-rating:{serviceType}:{serviceId}:{ratingType}:{entityType}:{entityId}` | `SkillRatingData` | Glicko-2 skill rating data per entity per rating type. Same serviceType/serviceId refactoring as summary keys. |
| `account-rating-index:{accountId}` | `string` (JSON string list) | Reverse index: account → list of rating keys. Maintained on write for Account-typed entities; consumed during account.deleted cleanup |
| `rating-decay-tracker` (sorted set) | sorted set of rating key strings, scored by LastMatchAt ms | Time-ordered index of ratings for decay worker processing. Maintained by `UpdateSkillRatingAsync` (add/update on match), removed by decay worker (at max RD), cleaned by `HandleAccountDeletedAsync`. Planned — [#249](https://github.com/beyond-immersion/bannou-service/issues/249). |

**Store**: `analytics-history-data` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `analytics-controller:{serviceType}:{serviceId}:{accountId}:{timestamp:o}` | `ControllerHistoryData` | Individual controller possession/release history events. Same serviceType/serviceId refactoring. |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | Acquires 11 typed store references across 4 store definitions |
| lib-state (IDistributedLockProvider) | L0 | Hard | Buffer flush lock, per-game/ratingType rating update lock |
| lib-messaging (IMessageBus) | L0 | Hard | Publishes 4 event topics, error reporting via TryPublishErrorAsync |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Span instrumentation on async helper methods; RecordCounter metric emission during buffer flush (`analytics.score.processed`, `analytics.events.processed`) |
| lib-game-service (IGameServiceClient) | L2 | Hard | Resolves game type stub names to game service IDs |
| lib-game-session (IGameSessionClient) | L2 | Hard | Fallback session-to-game-type resolution |
| lib-realm (IRealmClient) | L2 | Hard | Resolves realm IDs to game service IDs for history events |
| lib-character (ICharacterClient) | L2 | Hard | Resolves character IDs to realm IDs for history events |

**Notes**:
- Analytics is a **leaf node** for write calls — it makes no write calls to any other service. All 4 L2 client calls are read-only entity resolution lookups.
- No `x-references` registered with lib-resource. Analytics data is observational; no foundational resource depends on it.
- No `IAnalyticsClient` callers exist in any plugin. The generated client is available but unused.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `analytics.score.updated` | `AnalyticsScoreUpdatedEvent` | During buffer flush, for each buffered event with non-null Value, after successful ETag-guarded summary save |
| `analytics.rating.updated` | `AnalyticsRatingUpdatedEvent` | After all participants' Glicko-2 ratings saved under lock in UpdateSkillRating |
| `analytics.milestone.reached` | `AnalyticsMilestoneReachedEvent` | During buffer flush, when a score crosses a configured milestone threshold |
| `analytics.controller.recorded` | `AnalyticsControllerRecordedEvent` | Immediately after saving a controller history record in RecordControllerEvent |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `game-session.action.performed` | `HandleGameActionPerformedAsync` | Resolves session→gameService, buffers event as EntityType.Other with Value=1 |
| `game-session.created` | `HandleGameSessionCreatedAsync` | Resolves gameType→gameService, saves session mapping, buffers "session.created" event |
| `game-session.deleted` | `HandleGameSessionDeletedAsync` | Removes session mapping, resolves gameType→gameService, buffers "session.deleted" event |
| `character-history.participation.batch-created` | `HandleCharacterParticipationBatchCreatedAsync` | Processes batch entries with per-item error isolation: for each entry, resolves character→realm→gameService (two-hop, cached), buffers event with Value=1 |
| `character-history.backstory.created` | `HandleCharacterBackstoryCreatedAsync` | Resolves character→realm→gameService, buffers event with Value=ElementCount |
| `character-history.backstory.updated` | `HandleCharacterBackstoryUpdatedAsync` | Resolves character→realm→gameService, buffers event with Value=ElementCount |
| `realm-history.participation.recorded` | `HandleRealmParticipationRecordedAsync` | Resolves realm→gameService (one-hop, cached), buffers event with Value=1 |
| `realm-history.lore.created` | `HandleRealmLoreCreatedAsync` | Resolves realm→gameService, buffers event with Value=ElementCount |
| `realm-history.lore.updated` | `HandleRealmLoreUpdatedAsync` | Resolves realm→gameService, buffers event with Value=ElementCount |
| `character.updated` | `HandleCharacterUpdatedForCacheInvalidationAsync` | Deletes character-to-realm cache entry (best-effort, exceptions swallowed) |
| `realm.updated` | `HandleRealmUpdatedForCacheInvalidationAsync` | Deletes realm-to-gameService cache entry (best-effort, exceptions swallowed) |
| `account.deleted` | `HandleAccountDeletedAsync` | Deletes controller history records (MySQL query by $.AccountId), entity summaries where entityType=Account (MySQL query), and skill ratings for Account entities (via reverse index `account-rating-index:{accountId}`). Per-item error isolation, telemetry span, error event publishing. |

All history event handlers follow fail-fast: if game service resolution fails, the event is dropped permanently with error event publication. Incorrect GameServiceId is considered worse than missing data.

**Planned (Phase 1 — [#142](https://github.com/beyond-immersion/bannou-service/issues/142))**:

| Topic | Handler | Action |
|-------|---------|--------|
| `auth.login.successful` | `HandleAuthLoginSuccessfulAsync` | Buffers with `serviceType: System, serviceId: "auth"`, `EntityType.Account`, `entityId: accountId`, `eventType: "auth.login.successful"`, Value=1 |
| `auth.login.failed` | `HandleAuthLoginFailedAsync` | If `accountId` non-null: buffers per-account. Phase 2 adds per-IP entity. Null accountId: dropped from per-account, buffered for per-IP only (Phase 2). |
| `auth.registration.successful` | `HandleAuthRegistrationSuccessfulAsync` | Buffers with `serviceType: System, serviceId: "auth"`, per-account entity |
| `auth.oauth.successful` | `HandleAuthOAuthSuccessfulAsync` | Buffers with `serviceType: System, serviceId: "auth"`, per-account entity |
| `auth.steam.successful` | `HandleAuthSteamSuccessfulAsync` | Buffers with `serviceType: System, serviceId: "auth"`, per-account entity |
| `auth.password-reset.successful` | `HandleAuthPasswordResetSuccessfulAsync` | Buffers with `serviceType: System, serviceId: "auth"`, per-account entity |
| `auth.mfa.enabled` | `HandleAuthMfaEnabledAsync` | Buffers per-account |
| `auth.mfa.disabled` | `HandleAuthMfaDisabledAsync` | Buffers per-account |
| `auth.mfa.verified` | `HandleAuthMfaVerifiedAsync` | Buffers per-account |
| `auth.mfa.failed` | `HandleAuthMfaFailedAsync` | Buffers per-account |

Auth event handlers use `serviceType: System, serviceId: "auth"` — no game service resolution needed. No fail-fast on resolution (System type has no resolution step).

---

## DI Services

#### Constructor Dependencies

| Service | Role |
|---------|------|
| `ILogger<AnalyticsService>` | Structured logging |
| `AnalyticsServiceConfiguration` | All tunables: buffer, Glicko-2, TTLs, retention |
| `IStateStoreFactory` | Acquires 11 typed store references (not stored as field) |
| `IDistributedLockProvider` | Buffer flush lock and rating update lock |
| `IMessageBus` | Event publishing and error reporting |
| `ITelemetryProvider` | Span instrumentation |
| `IEventConsumer` | Registers 12 event subscriptions (not stored as field) |
| `IGameServiceClient` | Game type stub→ID resolution |
| `IGameSessionClient` | Session→game type fallback resolution |
| `IRealmClient` | Realm→game service ID resolution |
| `ICharacterClient` | Character→realm ID resolution |

---

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| IngestEvent | POST /analytics/event/ingest | generated | [] | buffer-entry, buffer-index, summary | analytics.score.updated, analytics.milestone.reached |
| IngestEventBatch | POST /analytics/event/ingest-batch | generated | [] | buffer-entry, buffer-index, summary | analytics.score.updated, analytics.milestone.reached |
| GetEntitySummary | POST /analytics/summary/get | generated | [admin] | - | - |
| QueryEntitySummaries | POST /analytics/summary/query | generated | [admin] | - | - |
| GetSkillRating | POST /analytics/rating/get | generated | [admin] | - | - |
| UpdateSkillRating | POST /analytics/rating/update | generated | [] | rating | analytics.rating.updated |
| RecordControllerEvent | POST /analytics/controller-history/record | generated | [] | history | analytics.controller.recorded |
| QueryControllerHistory | POST /analytics/controller-history/query | generated | [admin] | - | - |
| CleanupControllerHistory | POST /analytics/controller-history/cleanup | generated | [admin] | history | - |

---

## Methods

### IngestEvent
POST /analytics/event/ingest | Roles: []

```
// Build buffered event from request (including optional SessionId)
WRITE buffer-entry:analytics-event-buffer-entry:{newGuid} <- BufferedAnalyticsEvent from request
WRITE buffer-index:analytics-event-buffer-index (sorted set add, score=timestamp ms)
// Inline flush check after enqueue
IF buffer-index count >= config.EventBufferSize OR oldest entry age >= config.EventBufferFlushIntervalSeconds
  LOCK analytics-summary:"analytics-event-buffer-flush"     -> silent return if lock fails
    // see FlushBufferedEventsBatch below
    CALL FlushBufferedEventsBatch
RETURN (200, IngestEventResponse { EventId })
```

### IngestEventBatch
POST /analytics/event/ingest-batch | Roles: []

```
FOREACH evt IN request.Events
  WRITE buffer-entry:analytics-event-buffer-entry:{newGuid} <- BufferedAnalyticsEvent from evt
  WRITE buffer-index:analytics-event-buffer-index (sorted set add, score=timestamp ms)
  // Track accepted/rejected counts
IF accepted > 0
  IF buffer-index count >= config.EventBufferSize OR oldest entry age >= config.EventBufferFlushIntervalSeconds
    LOCK analytics-summary:"analytics-event-buffer-flush"   -> silent return if lock fails
      CALL FlushBufferedEventsBatch
RETURN (200, IngestEventBatchResponse { Accepted, Rejected, Errors })
// Always 200, even if all events rejected
```

### GetEntitySummary
POST /analytics/summary/get | Roles: [admin]

```
READ summary-data:analytics-entity:{gameServiceId}:{entityType}:{entityId}  -> 404 if null
RETURN (200, EntitySummaryResponse)
```

### QueryEntitySummaries
POST /analytics/summary/query | Roles: [admin]

```
IF limit <= 0 OR offset < 0 OR minEvents < 0                   -> 400
IF sortBy not in {totalevents, firsteventat, lasteventat, eventcount} -> 400
// Build JSON query conditions
conditions = [$.GameServiceId = gameServiceId]
IF entityType: conditions += [$.EntityType = entityType]
IF eventType: conditions += [$.EventCounts.{eventType} EXISTS]
IF minEvents > 0: conditions += [$.TotalEvents >= minEvents]
// Build sort spec
IF sortBy == "eventcount" AND eventType is empty: sort silently dropped
QUERY summary-data WHERE conditions ORDER BY sortSpec PAGED(offset, limit)
RETURN (200, QueryEntitySummariesResponse { Summaries, Total })
```

### GetSkillRating
POST /analytics/rating/get | Roles: [admin]

```
READ rating:analytics-rating:{gameServiceId}:{ratingType}:{entityType}:{entityId}
IF null
  // Returns 200 with defaults, never 404
  RETURN (200, SkillRatingResponse { defaults from config, MatchesPlayed=0 })
RETURN (200, SkillRatingResponse from stored data)
```

### UpdateSkillRating
POST /analytics/rating/update | Roles: []

```
IF results.Count < 2                                            -> 400
LOCK analytics-rating:"rating-update:{gameServiceId}:{ratingType}"
  -> 409 if lock fails (expiry: config.RatingUpdateLockExpirySeconds)

  // Pass 1: load all current ratings (or synthesize defaults)
  FOREACH result IN request.Results
    READ rating:analytics-rating:{gameServiceId}:{ratingType}:{entityType}:{entityId}
    // If null, create default from config

  // Snapshot pre-match values for opponent lookups
  originalRatings = snapshot of all (Rating, RD, Volatility)

  // Pass 2: calculate new ratings using pre-match opponent snapshots
  FOREACH result IN request.Results
    // Glicko-2 calculation with pairwise outcomes against all opponents
    // Uses originalRatings for opponent data (order-independent)
    newRating = CalculateGlicko2Update(player, opponents, outcomes)

  // Pass 3: save all updated ratings
  FOREACH calculated result
    WRITE rating:analytics-rating:{gameServiceId}:{ratingType}:{entityType}:{entityId} <- updated SkillRatingData
    IF entityType == Account
      // Maintain reverse index for account.deleted cleanup
      AddToStringList rating-index:account-rating-index:{entityId} <- ratingKey

  // Pass 4: publish events (still under lock)
  FOREACH calculated result
    PUBLISH analytics.rating.updated { GameServiceId, EntityId, EntityType, RatingType, PreviousRating, NewRating, RatingChange, NewRatingDeviation, MatchId }

RETURN (200, UpdateSkillRatingResponse { UpdatedRatings })
```

### RecordControllerEvent
POST /analytics/controller-history/record | Roles: []

```
WRITE history-data:analytics-controller:{gameServiceId}:{accountId}:{timestamp:o} <- ControllerHistoryData from request
PUBLISH analytics.controller.recorded { GameServiceId, AccountId, TargetEntityId, TargetEntityType, Action, SessionId }
RETURN (200, null)
```

### QueryControllerHistory
POST /analytics/controller-history/query | Roles: [admin]

```
IF limit <= 0                                                   -> 400
IF startTime > endTime                                          -> 400
// Build JSON query conditions
conditions = [$.GameServiceId = gameServiceId]
IF accountId: conditions += [$.AccountId = accountId]
IF targetEntityId: conditions += [$.TargetEntityId = targetEntityId]
IF targetEntityType: conditions += [$.TargetEntityType = targetEntityType]
IF startTime: conditions += [$.Timestamp >= startTime]
IF endTime: conditions += [$.Timestamp <= endTime]
QUERY history-data WHERE conditions ORDER BY $.Timestamp DESC PAGED(offset, limit)
RETURN (200, QueryControllerHistoryResponse { Events })
```

### CleanupControllerHistory
POST /analytics/controller-history/cleanup | Roles: [admin]

```
retentionDays = request.OlderThanDays ?? config.ControllerHistoryRetentionDays
IF retentionDays <= 0
  RETURN (200, CleanupControllerHistoryResponse { RecordsDeleted=0 })
cutoffTime = now - retentionDays
conditions = [$.Timestamp < cutoffTime]
IF gameServiceId: conditions += [$.GameServiceId = gameServiceId]
IF request.DryRun (default: true)
  COUNT history-data WHERE conditions
  RETURN (200, CleanupControllerHistoryResponse { RecordsDeleted=count })
// Actual deletion in batched loop
totalDeleted = 0
WHILE totalDeleted < config.ControllerHistoryCleanupBatchSize
  QUERY history-data WHERE conditions PAGED(0, config.ControllerHistoryCleanupSubBatchSize)
  IF batch empty: BREAK
  FOREACH item IN batch
    DELETE history-data:analytics-controller:{item.Key}
    totalDeleted++
RETURN (200, CleanupControllerHistoryResponse { RecordsDeleted=totalDeleted })
```

---

### FlushBufferedEventsBatch (internal helper)

Called from IngestEvent, IngestEventBatch, and event handlers after buffer threshold check + lock acquisition.

```
// Core flush loop — processes buffer in batches
LOOP
  READ buffer-index sorted set (oldest first, up to config.EventBufferSize entries)
  IF empty: BREAK
  // Fetch event data in bulk from buffer store
  FOREACH entryKey IN batch
    READ buffer-entry:{entryKey}
  // Group events by entity key ({gameServiceId}:{entityType}:{entityId})
  FOREACH entityGroup
    READ summary-data:analytics-entity:{entityKey} [with ETag]
    // Accumulate all events into summary (counts, aggregates, timestamps)
    FOREACH evt IN group with Value
      // Record score update for later event publication
    ETAG-WRITE summary-data:analytics-entity:{entityKey} <- updated summary
      -> IF ETag mismatch (409): skip entire entity batch (retry next flush)
    // On successful save:
    FOREACH score event in group
      PUBLISH analytics.score.updated { GameServiceId, EntityId, EntityType, ScoreType, PreviousValue, NewValue, Delta, SessionId }
      RECORD_COUNTER TelemetryMetrics.AnalyticsScoreProcessed, value=(long)Delta,
        tags: { game_service_id=GameServiceId, entity_type=EntityType, score_type=ScoreType }
      // Check milestone thresholds
      FOREACH threshold IN config.MilestoneThresholds
        IF previousValue < threshold AND newValue >= threshold
          PUBLISH analytics.milestone.reached { GameServiceId, EntityId, EntityType, MilestoneType=scoreType, MilestoneValue=threshold, MilestoneName="{scoreType}_{threshold}" }
    // Per-entity batch throughput metric
    RECORD_COUNTER TelemetryMetrics.AnalyticsEventsProcessed, value=entityEvents.Count,
      tags: { game_service_id=GameServiceId }
    // Cleanup processed entries
    FOREACH evt IN group
      DELETE buffer-entry:{entryKey}
      // Remove from sorted set
  IF batch.Count < batchSize: BREAK
```

---

## Background Services

### ControllerHistoryCleanupWorker

Periodic background service that automatically purges expired controller history records.

```
ExecuteAsync:
  DELAY config.ControllerHistoryCleanupStartupDelaySeconds
  LOOP while not cancelled:
    TRY:
      SPAN "bannou.analytics" / "ControllerHistoryCleanupWorker.ProcessCycle"
      SCOPE = CreateScope()
      stateStoreFactory = scope.Resolve(IStateStoreFactory)
      historyDataStore = stateStoreFactory.GetStore<ControllerHistoryData>(AnalyticsHistoryData)
      historyDataQueryStore = stateStoreFactory.GetJsonQueryableStore<ControllerHistoryData>(AnalyticsHistoryData)
      retentionDays = config.ControllerHistoryRetentionDays
      IF retentionDays <= 0: CONTINUE (indefinite retention)
      cutoffTime = now - retentionDays
      conditions = [$.Timestamp < cutoffTime]
      totalDeleted = 0
      WHILE totalDeleted < config.ControllerHistoryCleanupBatchSize:
        batch = QUERY historyDataQueryStore WHERE conditions PAGED(0, config.ControllerHistoryCleanupSubBatchSize)
        IF batch empty: BREAK
        FOREACH item IN batch:
          DELETE historyDataStore:analytics-controller:{item.Key}
          totalDeleted++
      LOG Information "Cleanup completed: {DeletedCount} records deleted"
    CATCH OperationCanceledException when stopping: BREAK
    CATCH Exception: LOG Error, TryPublishWorkerErrorAsync
    DELAY config.ControllerHistoryCleanupIntervalSeconds
```

**Configuration**:
- `ControllerHistoryCleanupStartupDelaySeconds` (default: 30) — delay before first cycle
- `ControllerHistoryCleanupIntervalSeconds` (default: 3600) — interval between cycles (1 hour default)
- Uses existing `ControllerHistoryRetentionDays`, `ControllerHistoryCleanupBatchSize`, `ControllerHistoryCleanupSubBatchSize`

### AnalyticsRatingDecayWorker (Planned — [#249](https://github.com/beyond-immersion/bannou-service/issues/249))

Periodic background service that applies Glicko-2 rating period decay to inactive players. Design resolved 2026-03-20.

```
ExecuteAsync:
  DELAY config.RatingDecayStartupDelaySeconds
  LOOP while not cancelled:
    TRY:
      SPAN "bannou.analytics" / "AnalyticsRatingDecayWorker.ProcessCycle"
      SCOPE = CreateScope()
      stateStoreFactory = scope.Resolve(IStateStoreFactory)
      ratingStore = stateStoreFactory.GetStore<SkillRatingData>(AnalyticsRating)
      cacheableStore = stateStoreFactory.GetCacheableStore<SkillRatingData>(AnalyticsRating)

      cutoffMs = (now - config.RatingDecayInactivityDays).ToUnixTimeMilliseconds()
      entries = cacheableStore.SortedSetRangeByScoreAsync(
          "rating-decay-tracker", 0, cutoffMs, limit: config.RatingDecayBatchSize)
      IF empty: CONTINUE

      LOCK analytics-rating:"rating-decay-cycle" (expiry: config.RatingDecayLockExpirySeconds)
        -> IF lock fails: CONTINUE (another node is processing)

        FOREACH entry IN entries (per-item error isolation T7):
          rating = ratingStore.GetAsync(entry.member)
          IF null: SortedSetRemoveAsync("rating-decay-tracker", entry.member), CONTINUE
          IF rating.RatingDeviation >= config.Glicko2DefaultDeviation - 0.001:
            SortedSetRemoveAsync("rating-decay-tracker", entry.member), CONTINUE

          // Apply existing Glicko-2 no-games-played formula
          (_, newRD, newVolatility) = CalculateGlicko2Update(rating, emptyResults)
          rating.RatingDeviation = newRD
          rating.Volatility = newVolatility
          ratingStore.SaveAsync(entry.member, rating)

          IF newRD >= config.Glicko2DefaultDeviation - 0.001:
            SortedSetRemoveAsync("rating-decay-tracker", entry.member)  // At max, done
          ELSE:
            SortedSetAddAsync("rating-decay-tracker", entry.member, now.ToUnixTimeMilliseconds())  // Re-eligible next cycle

          PUBLISH analytics.rating.updated {
            MatchId = null, RatingChange = 0,
            PreviousRating = rating.Rating, NewRating = rating.Rating,
            NewRatingDeviation = newRD, ... }

      LOG Information "Decay cycle completed: {Processed} ratings processed"
    CATCH OperationCanceledException when stopping: BREAK
    CATCH Exception: LOG Error, TryPublishWorkerErrorAsync
    DELAY config.RatingDecayIntervalSeconds
```

**Configuration** (all planned — not yet in schema):
- `RatingDecayInactivityDays` (default: 30) — days of inactivity before decay applies
- `RatingDecayIntervalSeconds` (default: 3600) — interval between decay cycles
- `RatingDecayStartupDelaySeconds` (default: 60) — delay before first cycle
- `RatingDecayBatchSize` (default: 500) — max ratings to process per cycle
- `RatingDecayLockExpirySeconds` (default: 300) — distributed lock expiry

**Schema change required**: `matchId` on `AnalyticsRatingUpdatedEvent` becomes nullable (remove from `required`, add `nullable: true`). Per FOUNDATION TENETS: background state changes publish only lifecycle events, no action event.

**Sorted set maintenance**:
- `UpdateSkillRatingAsync`: add/update entry scored by `LastMatchAt` after each rating save
- Decay worker: remove entry when RD reaches `Glicko2DefaultDeviation`; update score to `now` otherwise
- `HandleAccountDeletedAsync`: remove entries during account cleanup

---

## Non-Standard Implementation Patterns

No non-standard patterns. All 9 endpoints are standard generated interface methods. No plugin lifecycle overrides, no manual routes, no controller-only endpoints, no custom overrides.
