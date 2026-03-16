# Character History Implementation Map

> **Plugin**: lib-character-history
> **Schema**: schemas/character-history-api.yaml
> **Layer**: GameFeatures
> **Deep Dive**: [docs/plugins/CHARACTER-HISTORY.md](../plugins/CHARACTER-HISTORY.md)

---

## Summary

| Field | Value |
|-------|-------|
| Plugin | lib-character-history |
| Layer | L4 GameFeatures |
| Endpoints | 12 (all generated) |
| State Stores | character-history-statestore (MySQL) |
| Events Published | 7 (character-history.participation.batch-created, .batch-modified, .batch-destroyed, .backstory.created, .backstory.updated, .backstory.deleted, character-history.deleted) |
| Events Consumed | 4 (self-subscribe for cache invalidation) |
| Client Events | 0 |
| Background Services | 1 (EventBatcherWorker) |

---

## State

**Store**: `character-history-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `participation-{participationId}` | `ParticipationData` | Individual participation record |
| `participation-index-{characterId}` | Index list | Character → participation ID list (DualIndexHelper primary) |
| `participation-event-{eventId}` | Index list | Event → participation ID list (DualIndexHelper secondary) |
| `backstory-{characterId}` | `BackstoryData` | All backstory elements for a character (single document) |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | MySQL persistence for participations and backstory |
| lib-state (`IDistributedLockProvider`) | L0 | Hard | Write safety for DualIndexHelper and BackstoryStorageHelper |
| lib-messaging (`IMessageBus`) | L0 | Hard | Publishing backstory and history-deleted events |
| lib-messaging (`IEventConsumer`) | L0 | Hard | Self-subscribe for BackstoryCache invalidation |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Span creation for event handlers |
| lib-resource (`IResourceClient`) | L1 | Hard | Reference tracking (register/unregister) and cleanup/compression callback registration |

**Notes:**
- No L2 or L3 service client dependencies. This plugin is a leaf node at L4.
- `ICharacterHistoryClient` is used by `BackstoryCache` for self-mesh call on cache miss (service calling itself via lib-mesh).
- lib-storyline (L4) calls this plugin as a soft dependency for backstory mutations.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `character-history.participation.batch-created` | `ParticipationBatchCreatedEvent` | EventBatcherWorker flush of accumulated RecordParticipation calls |
| `character-history.participation.batch-modified` | `ParticipationBatchModifiedEvent` | Unused infrastructure (participations are immutable) |
| `character-history.participation.batch-destroyed` | `ParticipationBatchDestroyedEvent` | EventBatcherWorker flush of accumulated DeleteParticipation calls |
| `character-history.backstory.created` | `CharacterBackstoryCreatedEvent` | SetBackstory or AddBackstoryElement when no prior backstory exists |
| `character-history.backstory.updated` | `CharacterBackstoryUpdatedEvent` | SetBackstory or AddBackstoryElement when backstory already exists |
| `character-history.backstory.deleted` | `CharacterBackstoryDeletedEvent` | DeleteBackstory on success |
| `character-history.deleted` | `CharacterHistoryDeletedEvent` | DeleteAllHistory on success |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `character-history.backstory.created` | `HandleBackstoryCreatedAsync` | `_backstoryCache.Invalidate(characterId)` |
| `character-history.backstory.updated` | `HandleBackstoryUpdatedAsync` | `_backstoryCache.Invalidate(characterId)` |
| `character-history.backstory.deleted` | `HandleBackstoryDeletedAsync` | `_backstoryCache.Invalidate(characterId)` |
| `character-history.deleted` | `HandleHistoryDeletedAsync` | `_backstoryCache.Invalidate(characterId)` |

All consumed events are self-subscriptions for cross-node BackstoryCache invalidation. No external event subscriptions.

---

## DI Services

#### Constructor Dependencies

| Service | Role |
|---------|------|
| `IMessageBus` | Event publishing |
| `IStateStoreFactory` | Store acquisition (not stored as field — used only in constructor) |
| `ILogger<CharacterHistoryService>` | Structured logging |
| `IEventConsumer` | Self-event subscription registration |
| `CharacterHistoryServiceConfiguration` | Configuration access |
| `IDistributedLockProvider` | Passed to DualIndexHelper and BackstoryStorageHelper |
| `IResourceClient` | Reference tracking with lib-resource |
| `ITelemetryProvider` | Telemetry spans |
| `IBackstoryCache` | Cache invalidation in event handlers |
| `ParticipationEventBatcher` | Batch event accumulation |

#### DI Interfaces Implemented by This Plugin

| Interface | Registered As | Direction | Consumer |
|-----------|---------------|-----------|----------|
| `IVariableProviderFactory` (via `BackstoryProviderFactory`) | Singleton | L4→L2 pull | Actor (L2) discovers via `IEnumerable<IVariableProviderFactory>` for `${backstory.*}` expressions |

#### Helper Services

| Class | Location | Lifetime | Role |
|-------|----------|----------|------|
| `ParticipationEventBatcher` | Services/ | Singleton | Accumulates participation created/destroyed entries for periodic batch flush |
| `BackstoryCache` | Caching/ | Singleton | TTL cache backed by `VariableProviderCacheBucket`; loads via self-mesh call on miss |
| `BackstoryProviderFactory` | Providers/ | Singleton | Creates `BackstoryProvider` per actor execution; returns empty for non-character actors |
| `BackstoryProvider` | Providers/ | Per-request | Resolves `${backstory.*}` ABML expressions from cached backstory data |

---

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| RecordParticipation | POST /character-history/record-participation | generated | [] | participation, indexes | participation.batch-created (batched) |
| GetParticipation | POST /character-history/get-participation | generated | [] | - | - |
| GetEventParticipants | POST /character-history/get-event-participants | generated | [] | - | - |
| DeleteParticipation | POST /character-history/delete-participation | generated | [] | participation, indexes | participation.batch-destroyed (batched) |
| GetBackstory | POST /character-history/get-backstory | generated | [] | - | - |
| SetBackstory | POST /character-history/set-backstory | generated | [] | backstory | backstory.created or backstory.updated |
| AddBackstoryElement | POST /character-history/add-backstory-element | generated | [] | backstory | backstory.created or backstory.updated |
| DeleteBackstory | POST /character-history/delete-backstory | generated | [] | backstory | backstory.deleted |
| DeleteAllHistory | POST /character-history/delete-all | generated | [] | participation, indexes, backstory | character-history.deleted |
| SummarizeHistory | POST /character-history/summarize | generated | [] | - | - |
| GetCompressData | POST /character-history/get-compress-data | generated | [] | - | - |
| RestoreFromArchive | POST /character-history/restore-from-archive | generated | [] | participation, indexes, backstory | - |

All 12 endpoints use `x-permissions: []` (service-to-service only).

---

## Methods

### RecordParticipation
POST /character-history/record-participation | Roles: []

```
READ _participationHelper.GetRecordsByPrimaryKeyAsync(characterId)
IF any existing.EventId == body.EventId
  RETURN (409, null)                                    // duplicate participation
LOCK participation-index-{characterId}                  -> 409 if not acquired
  WRITE participation-{participationId} <- ParticipationData from request
  WRITE participation-index-{characterId} <- add participationId
  WRITE participation-event-{eventId} <- add participationId
CALL _participationEventBatcher.AddCreated(entry)       // synchronous accumulate
CALL RegisterCharacterReferenceAsync(participationId, characterId)
RETURN (200, HistoricalParticipation)
```

---

### GetParticipation
POST /character-history/get-participation | Roles: []

```
QUERY _participationQueryStore WHERE $.CharacterId == characterId
  + optional $.EventCategory filter
  + optional $.Significance >= minimumSignificance filter
  ORDER BY $.EventDateUnix DESC
  PAGED(page, pageSize)
RETURN (200, ParticipationListResponse)
```

---

### GetEventParticipants
POST /character-history/get-event-participants | Roles: []

```
QUERY _participationQueryStore WHERE $.EventId == eventId
  + optional $.Role filter
  ORDER BY $.Significance DESC
  PAGED(page, pageSize)
RETURN (200, ParticipationListResponse)
```

---

### DeleteParticipation
POST /character-history/delete-participation | Roles: []

```
READ _participationHelper.GetRecordAsync(participationId) -> 404 if null
CALL UnregisterCharacterReferenceAsync(participationId, characterId)
LOCK participation-index-{characterId}                     -> 409 if not acquired
  DELETE participation-{participationId}
  WRITE participation-index-{characterId} <- remove participationId
  WRITE participation-event-{eventId} <- remove participationId
CALL _participationEventBatcher.AddDestroyed(entry)        // synchronous accumulate
RETURN (200, null)
```

---

### GetBackstory
POST /character-history/get-backstory | Roles: []

```
READ backstory-{characterId}                             -> 404 if null
FILTER elements by ElementTypes (if provided)            // in-memory filter
FILTER elements by MinimumStrength (if provided)         // in-memory filter
RETURN (200, BackstoryResponse)
// Note: UpdatedAt = null when backstory has never been modified after creation
```

---

### SetBackstory
POST /character-history/set-backstory | Roles: []

```
IF body.ReplaceExisting == true
  IF elements.Count > config.MaxBackstoryElements        -> 400
ELSE (merge mode)
  READ existing backstory-{characterId}
  IF existing != null
    CALCULATE postMergeCount (existing + truly new)
    IF postMergeCount > config.MaxBackstoryElements      -> 400
  ELSE
    IF elements.Count > config.MaxBackstoryElements      -> 400
LOCK backstory-{characterId}                             -> 409 if not acquired
  WRITE backstory-{characterId} <- BackstoryData (merge or replace)
IF result.IsNew
  PUBLISH character-history.backstory.created { characterId, elementCount }
  CALL RegisterCharacterReferenceAsync("backstory-{characterId}", characterId)
ELSE
  PUBLISH character-history.backstory.updated { characterId, elementCount, replaceExisting }
RETURN (200, BackstoryResponse)
```

---

### AddBackstoryElement
POST /character-history/add-backstory-element | Roles: []

```
READ existing backstory-{characterId}
IF existing != null AND element is new (no type+key match)
  IF existing.Elements.Count >= config.MaxBackstoryElements  -> 400
LOCK backstory-{characterId}                                  -> 409 if not acquired
  WRITE backstory-{characterId} <- add/update element (upsert by type+key)
IF result.IsNew
  PUBLISH character-history.backstory.created { characterId, elementCount }
  CALL RegisterCharacterReferenceAsync("backstory-{characterId}", characterId)
ELSE
  PUBLISH character-history.backstory.updated { characterId, elementCount, replaceExisting: false }
RETURN (200, BackstoryResponse)
```

---

### DeleteBackstory
POST /character-history/delete-backstory | Roles: []

```
LOCK backstory-{characterId}                             -> 409 if not acquired
  DELETE backstory-{characterId}                         -> 404 if not found
CALL UnregisterCharacterReferenceAsync("backstory-{characterId}", characterId)
PUBLISH character-history.backstory.deleted { characterId }
RETURN (200, null)
```

---

### DeleteAllHistory
POST /character-history/delete-all | Roles: []

```
READ all participations via _participationHelper.GetRecordsByPrimaryKeyAsync(characterId)
FOREACH participation
  CALL UnregisterCharacterReferenceAsync(participationId, characterId)
LOCK participation-index-{characterId}                   -> 409 if not acquired
  DELETE all participation records and both indexes
READ backstory-{characterId}
IF backstory exists
  CALL UnregisterCharacterReferenceAsync("backstory-{characterId}", characterId)
  LOCK backstory-{characterId}                           -> 409 if not acquired
    DELETE backstory-{characterId}
PUBLISH character-history.deleted { characterId, participationsDeleted, backstoryDeleted }
RETURN (200, DeleteAllHistoryResponse)
// Note: Reference unregistration occurs before lock — partial inconsistency on lock failure
```

---

### SummarizeHistory
POST /character-history/summarize | Roles: []

```
READ backstory-{characterId}
IF backstory != null
  TAKE top body.MaxBackstoryPoints elements by Strength DESC
  FOREACH element: generate human-readable summary string
READ participations via _participationHelper.GetRecordsByPrimaryKeyAsync(characterId)
IF participations.Count > 0
  TAKE top body.MaxLifeEvents participations by Significance DESC
  FOREACH participation: generate human-readable summary string
RETURN (200, HistorySummaryResponse { KeyBackstoryPoints, MajorLifeEvents })
```

---

### GetCompressData
POST /character-history/get-compress-data | Roles: []

```
READ all participations sorted by EventDate DESC
READ backstory-{characterId}
IF both empty                                            -> 404
CALL SummarizeHistory(characterId, config.MaxCompressBackstoryPoints, config.MaxCompressLifeEvents)
RETURN (200, CharacterHistoryArchive { CharacterId, Participations, Backstory, Summaries })
```

---

### RestoreFromArchive
POST /character-history/restore-from-archive | Roles: []

```
DECODE body.Data from Base64
DECOMPRESS via CompressionHelper.DecompressJsonData      -> 400 on failure
IF archive.HasParticipations
  FOREACH participation in archive.Participations
    LOCK participation-index-{characterId}               -> 409 if not acquired
      WRITE participation record and indexes via DualIndexHelper
    CALL RegisterCharacterReferenceAsync(participationId, characterId)
IF archive.HasBackstory
  LOCK backstory-{characterId}                           -> 409 if not acquired
    WRITE backstory-{characterId} <- BackstoryData (replace)
  CALL RegisterCharacterReferenceAsync("backstory-{characterId}", characterId)
RETURN (200, RestoreFromArchiveResponse { ParticipationsRestored, BackstoryRestored })
// Note: Restored participations do NOT publish batch events — only live RecordParticipation does
```

---

## Background Services

### EventBatcherWorker
**Interval**: `config.ParticipationEventBatchIntervalSeconds` (default 5s)
**Startup Delay**: `config.ParticipationEventBatchStartupDelaySeconds` (default 10s)
**Purpose**: Flushes accumulated participation batch events.

```
EVERY ParticipationEventBatchIntervalSeconds
  FOREACH flushable in [_created, _destroyed]
    IF entries accumulated
      PUBLISH character-history.participation.batch-{created|destroyed} { Entries, Count, WindowStartedAt }
```

---

## Non-Standard Implementation Patterns

### Plugin Lifecycle (OnRunningAsync)

```
RESOLVE IResourceTemplateRegistry                        // L0 infrastructure, fail-fast
CALL templateRegistry.Register(CharacterHistoryTemplate) // ABML ${candidate.history.*} path validation
CREATE scope
RESOLVE IResourceClient from scope
CALL RegisterResourceCleanupCallbacksAsync(resourceClient)
  // CASCADE: on character.deleted → POST /character-history/delete-all
CALL CharacterHistoryCompressionCallbacks.RegisterAsync(resourceClient)
  // Compression priority 20: get-compress-data + restore-from-archive
```

### ConfigureServices

```
REGISTER ParticipationEventBatcher as Singleton
REGISTER EventBatcherWorker as IHostedService
  // Receives batcher.AllFlushables, config intervals, telemetry provider
```
