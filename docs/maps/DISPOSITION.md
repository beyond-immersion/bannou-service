# Disposition Implementation Map

> **Plugin**: lib-disposition
> **Schema**: schemas/disposition-api.yaml
> **Layer**: GameFeatures
> **Deep Dive**: [docs/plugins/DISPOSITION.md](../plugins/DISPOSITION.md)
> **Status**: Aspirational -- pseudo-code represents intended behavior, not verified implementation

## Summary Table

| Field | Value |
|-------|-------|
| Plugin | lib-disposition |
| Layer | L4 GameFeatures |
| Endpoints | 17 (17 generated) |
| State Stores | disposition-feelings (MySQL), disposition-drives (MySQL), disposition-cache (Redis), disposition-lock (Redis) |
| Events Published | 10 (disposition.feeling.changed, disposition.feeling.shocked, disposition.feeling.decayed, disposition.drive.formed, disposition.drive.intensified, disposition.drive.satisfied, disposition.drive.frustrated, disposition.drive.abandoned, disposition.drive.fulfilled, disposition.guardian.shifted) |
| Events Consumed | 10 (2 deferred: hearsay Phase 4+) |
| Client Events | 0 |
| Background Services | 3 |

---

## State

**Store**: `disposition-feelings` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `feeling:{feelingId}` | `FeelingEntryModel` | Primary feeling record |
| `feeling:char:{characterId}` | query | All feelings held by a character |
| `feeling:char:{characterId}:{targetType}` | query | Feelings filtered by target type |
| `feeling:char:{characterId}:{targetType}:{targetId}` | query | All axes for a specific target |
| `feeling:about:{targetType}:{targetId}` | `FeelingAboutModel` | Reverse index: who feels what about this target |

**Store**: `disposition-drives` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `drive:{driveId}` | `DriveEntryModel` | Primary drive record |
| `drive:char:{characterId}` | query | All drives for a character |
| `drive:char:{characterId}:{category}` | query | Category-filtered drives |
| `drive:code-idx:{driveCode}` | `DriveCodeIndexModel` | Characters with this drive code (scenario queries) |

**Store**: `disposition-cache` (Backend: Redis, prefix: `disposition:cache`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `manifest:{characterId}` | `DispositionManifestModel` | Cached composite manifest for variable provider reads. TTL: `CacheTtlMinutes` |

**Store**: `disposition-lock` (Backend: Redis, prefix: `disposition:lock`)

| Key Pattern | Purpose |
|-------------|---------|
| `feeling:{characterId}:{targetType}:{targetId}` | Feeling mutation lock |
| `drive:{characterId}` | Drive mutation lock |
| `synthesis:{characterId}` | Synthesis serialization lock |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | 4 state stores: feelings (MySQL), drives (MySQL), cache (Redis), lock (Redis) |
| lib-state (`IDistributedLockProvider`) | L0 | Hard | Per-character feeling/drive/synthesis locks |
| lib-messaging (`IMessageBus`) | L0 | Hard | Publishing all 10 event topics |
| lib-messaging (`IEventConsumer`) | L0 | Hard | Subscribing to 10 upstream event topics |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Span instrumentation on helpers and workers |
| lib-character (`ICharacterClient`) | L2 | Hard | Character existence validation |
| lib-resource (`IResourceClient`) | L1 | Hard | Cleanup callback registration (character/realm CASCADE), compression callback registration |
| lib-relationship (`IRelationshipClient`) | L2 | Hard | Relationship type queries for base value synthesis |
| lib-character-encounter (`ICharacterEncounterClient`) | L4 | Soft | Encounter sentiment for base synthesis (0.0 if absent) |
| lib-character-personality (`ICharacterPersonalityClient`) | L4 | Soft | Personality traits for bias + drive formation (neutral if absent) |
| lib-character-history (`ICharacterHistoryClient`) | L4 | Soft | Backstory elements for drive seeding (no backstory drives if absent) |
| lib-hearsay (`IHearsayClient`) | L4 | Soft | Belief manifests for hearsay synthesis component (Phase 4+, plugin not yet implemented) |
| lib-faction (`IFactionClient`) | L4 | Soft | Faction membership for circumstance-derived drives (no faction drives if absent) |
| lib-obligation (`IObligationClient`) | L4 | Soft | Violation history for violation-catalyzed drives (no violation drives if absent) |

**Note**: All soft L4 dependencies resolved via `IServiceProvider.GetService<T>()` at synthesis time. Unavailable sources get weight 0.0 with proportional redistribution to remaining sources.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `disposition.feeling.changed` | `DispositionFeelingChangedEvent` | RecordFeeling, ResynthesizeBase, SynthesisWorker — when effective value change exceeds `FeelingChangeThreshold` |
| `disposition.feeling.shocked` | `DispositionFeelingShockedEvent` | RecordShock — large immediate modifier shifts from formative events |
| `disposition.feeling.decayed` | `DispositionFeelingDecayedEvent` | ModifierDecayWorker — batch event for modifier decay below threshold |
| `disposition.drive.formed` | `DispositionDriveFormedEvent` | SeedDrive, DriveEvaluationWorker — new drive emerged |
| `disposition.drive.intensified` | `DispositionDriveIntensifiedEvent` | DriveEvaluationWorker — drive intensity increased |
| `disposition.drive.satisfied` | `DispositionDriveSatisfiedEvent` | RecordProgress, DriveEvaluationWorker — satisfaction crossed threshold |
| `disposition.drive.frustrated` | `DispositionDriveFrustratedEvent` | RecordFrustration, DriveEvaluationWorker — frustration increased significantly |
| `disposition.drive.abandoned` | `DispositionDriveAbandonedEvent` | RecordFrustration, DriveEvaluationWorker — intensity dropped below `DriveMinIntensityThreshold` |
| `disposition.drive.fulfilled` | `DispositionDriveFulfilledEvent` | RecordProgress, DriveEvaluationWorker — satisfaction reached `DriveFulfillmentThreshold` |
| `disposition.guardian.shifted` | `DispositionGuardianShiftedEvent` | RecordSpiritAction — guardian spirit feeling axis changed |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `encounter.recorded` | `HandleEncounterRecordedAsync` | Update feeling modifiers toward encounter participants based on type and outcome |
| `personality.evolved` | `HandlePersonalityEvolvedAsync` | Trigger full base resynthesis; check personality-innate drive formation |
| `combat-preferences.evolved` | `HandleCombatPreferencesEvolvedAsync` | Check mastery-category drive formation for combat domain |
| `relationship.created` | `HandleRelationshipCreatedAsync` | Seed initial feelings toward partner based on relationship type |
| `relationship.deleted` | `HandleRelationshipDeletedAsync` | Trigger base resynthesis (relationship component removed); feelings persist |
| `obligation.violation.reported` | `HandleViolationReportedAsync` | Apply guilt composite modifier shifts; check `redeem_past` drive formation |
| `character-history.backstory.created` | `HandleBackstoryCreatedAsync` | Seed drives from backstory elements (GOAL → mastery/identity; TRAUMA → survival/social) |
| `seed.phase.changed` | `HandleSeedPhaseChangedAsync` | Update guardian spirit feelings on phase milestone (familiarity, trust) |
| `hearsay.belief.acquired` | `HandleBeliefAcquiredAsync` | Trigger resynthesis for affected character-target pairs // Future: not yet published by lib-hearsay |
| `hearsay.belief.corrected` | `HandleBeliefCorrectedAsync` | Trigger resynthesis on corrected belief // Future: not yet published by lib-hearsay |

---

## DI Services

#### Constructor Dependencies

| Service | Role |
|---------|------|
| `ILogger<DispositionService>` | Structured logging |
| `DispositionServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | Acquires 4 state stores in constructor |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Event handler registration |
| `IDistributedLockProvider` | Distributed lock acquisition |
| `ICharacterClient` | Character existence validation (L2) |
| `IResourceClient` | Cleanup + compression callback registration (L1) |
| `IRelationshipClient` | Relationship type queries for synthesis (L2) |
| `IServiceProvider` | Runtime resolution of 6 soft L4 dependencies |
| `ITelemetryProvider` | Span instrumentation |

#### DI Interfaces Implemented by This Plugin

| Interface | Registered As | Direction | Consumer |
|-----------|---------------|-----------|----------|
| `IVariableProviderFactory` | `Singleton` | L4→L2 pull | Actor (L2) discovers `DispositionProviderFactory` via `IEnumerable<IVariableProviderFactory>` for `${disposition.*}` variables |

---

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| RecordFeeling | POST /disposition/feeling/record | generated | [] | feeling, indexes, cache | disposition.feeling.changed |
| RecordShock | POST /disposition/feeling/record-shock | generated | [] | feeling, indexes, cache | disposition.feeling.shocked |
| QueryFeelings | POST /disposition/feeling/query | generated | [] | - | - |
| GetDispositionManifest | POST /disposition/feeling/get-manifest | generated | [] | cache (write-through) | - |
| QueryFeelingsAbout | POST /disposition/feeling/query-about | generated | [] | - | - |
| ResynthesizeBase | POST /disposition/feeling/resynthesize | generated | [] | feeling, cache | disposition.feeling.changed |
| SeedDrive | POST /disposition/drive/seed | generated | [] | drive, indexes, cache | disposition.drive.formed |
| QueryDrives | POST /disposition/drive/query | generated | [] | - | - |
| RecordProgress | POST /disposition/drive/record-progress | generated | [] | drive, cache | disposition.drive.satisfied, disposition.drive.fulfilled |
| RecordFrustration | POST /disposition/drive/record-frustration | generated | [] | drive, indexes, cache | disposition.drive.frustrated, disposition.drive.abandoned |
| FindByDrive | POST /disposition/drive/find-by-drive | generated | [] | - | - |
| RecordSpiritAction | POST /disposition/guardian/record-action | generated | [] | feeling, indexes, cache | disposition.guardian.shifted |
| GetSpiritRelationship | POST /disposition/guardian/get-relationship | generated | [] | - | - |
| CleanupByCharacter | POST /disposition/cleanup-by-character | generated | [] | feeling, drive, indexes, cache | - |
| CleanupByRealm | POST /disposition/cleanup-by-realm | generated | [] | feeling, drive, indexes, cache | - |
| GetCompressData | POST /disposition/get-compress-data | generated | [] | - | - |
| RestoreFromArchive | POST /disposition/restore-from-archive | generated | [] | feeling, drive, indexes, cache | - |

---

## Methods

### RecordFeeling
POST /disposition/feeling/record | Roles: []

```
CALL _characterClient.GetCharacterAsync(body.CharacterId)          -> 404 if null
LOCK disposition-lock:feeling:{characterId}:{targetType}:{targetId}  -> 409 if timeout
  READ _feelingsStore:feeling:char:{characterId}:{targetType}:{targetId} WHERE axis == body.Axis
  IF existing feeling found
    // Reinforcement: newModifier = oldModifier + amount * (1.0 - abs(oldModifier))
    WRITE _feelingsStore:feeling:{feelingId} <- updated modifier, recomputed effectiveValue
  ELSE
    IF count(character feelings) >= config.MaxFeelingsPerCharacter   -> 409
    // Synthesize base from available sources with weight redistribution
    CALL _relationshipClient.QueryRelationshipsAsync(characterId, targetId)
    SOFT-CALL ICharacterEncounterClient (encounter sentiment)
    SOFT-CALL ICharacterPersonalityClient (personality bias)
    SOFT-CALL IHearsayClient (hearsay belief signal)
    // base = sum(contribution_i * redistributed_weight_i)
    WRITE _feelingsStore:feeling:{feelingId} <- new FeelingEntryModel
    WRITE _feelingsStore indexes (char, char:type, char:type:target, about)
  // effectiveValue = clamp(base + modifier, -1.0, 1.0)
  DELETE _cacheStore:manifest:{characterId}
  IF abs(newEffective - oldEffective) >= config.FeelingChangeThreshold
    PUBLISH disposition.feeling.changed { characterId, axis, oldValue, newValue }
RETURN (200, FeelingResponse)
```

### RecordShock
POST /disposition/feeling/record-shock | Roles: []

```
CALL _characterClient.GetCharacterAsync(body.CharacterId)          -> 404 if null
LOCK disposition-lock:feeling:{characterId}:{targetType}:{targetId}  -> 409 if timeout
  FOREACH axisPair in body.AxisModifiers
    READ _feelingsStore:feeling:char:{characterId}:{targetType}:{targetId} WHERE axis == axisPair.Axis
    IF existing: apply shock modifier (magnitude: config.ShockModifierMagnitude, decayRate: config.ShockModifierDecayRate)
    ELSE: create new feeling with shock modifier as initial modifier
    WRITE _feelingsStore:feeling:{feelingId} <- updated entry
    WRITE _feelingsStore indexes
  DELETE _cacheStore:manifest:{characterId}
  PUBLISH disposition.feeling.shocked { characterId, causeEvent, modifierDelta }
RETURN (200, ShockResponse)
```

### QueryFeelings
POST /disposition/feeling/query | Roles: []

```
QUERY _feelingsStore WHERE characterId == body.CharacterId
  // Optional filters: targetType, targetId, axis, minAbsEffectiveValue, time range
  // ORDER BY abs(effectiveValue) DESC
  // PAGED(body.Page ?? 1, body.PageSize ?? config.QueryPageSize)
RETURN (200, QueryFeelingsResponse { feelings, totalCount, page, pageSize })
```

### GetDispositionManifest
POST /disposition/feeling/get-manifest | Roles: []

```
READ _cacheStore:manifest:{characterId}
IF cache hit
  RETURN (200, DispositionManifestResponse from cache)
// Cache miss: build manifest
QUERY _feelingsStore WHERE characterId == body.CharacterId
QUERY _drivesStore WHERE characterId == body.CharacterId
WRITE _cacheStore:manifest:{characterId} <- DispositionManifestModel with TTL config.CacheTtlMinutes
RETURN (200, DispositionManifestResponse)
```

### QueryFeelingsAbout
POST /disposition/feeling/query-about | Roles: []

```
QUERY _feelingsStore:feeling:about:{targetType}:{targetId}
  // Optional filters: axis, minAbsEffectiveValue
  // PAGED(body.Page ?? 1, body.PageSize ?? config.QueryPageSize)
RETURN (200, QueryFeelingsAboutResponse { results, totalCount, page, pageSize })
```

### ResynthesizeBase
POST /disposition/feeling/resynthesize | Roles: []

```
CALL _characterClient.GetCharacterAsync(body.CharacterId)          -> 404 if null
LOCK disposition-lock:synthesis:{characterId}                       -> 409 if timeout
  QUERY _feelingsStore WHERE characterId == body.CharacterId
  FOREACH feeling in results [per-item error isolation]
    // Resolve available sources, redistribute weights for unavailable
    SOFT-CALL ICharacterEncounterClient (encounter sentiment for target)
    SOFT-CALL ICharacterPersonalityClient (personality bias)
    SOFT-CALL IHearsayClient (hearsay belief for target)
    CALL _relationshipClient.QueryRelationshipsAsync(characterId, targetId)
    // newBase = sum(contribution_i * redistributed_weight_i)
    WRITE _feelingsStore:feeling:{feelingId} <- updated base, sourceBreakdown, lastSynthesizedAt
    IF abs(newEffective - oldEffective) >= config.FeelingChangeThreshold
      PUBLISH disposition.feeling.changed { characterId, axis, oldValue, newValue }
  DELETE _cacheStore:manifest:{characterId}
RETURN (200, ResynthesizeResponse { feelingsUpdated })
```

### SeedDrive
POST /disposition/drive/seed | Roles: []

```
CALL _characterClient.GetCharacterAsync(body.CharacterId)          -> 404 if null
LOCK disposition-lock:drive:{characterId}                           -> 409 if timeout
  QUERY _drivesStore WHERE characterId == body.CharacterId
  IF count(drives) >= config.MaxDrivesPerCharacter                  -> 409
  WRITE _drivesStore:drive:{driveId} <- new DriveEntryModel from request
  WRITE _drivesStore indexes (char, char:category, code-idx)
  DELETE _cacheStore:manifest:{characterId}
  PUBLISH disposition.drive.formed { characterId, driveCode, category, originType, intensity }
RETURN (200, DriveResponse)
```

### QueryDrives
POST /disposition/drive/query | Roles: []

```
QUERY _drivesStore WHERE characterId == body.CharacterId
  // Optional filters: category, minIntensity, minSatisfaction, minFrustration
  // ORDER BY intensity DESC
RETURN (200, QueryDrivesResponse { drives, totalCount })
```

### RecordProgress
POST /disposition/drive/record-progress | Roles: []

```
LOCK disposition-lock:drive:{characterId}                           -> 409 if timeout
  READ _drivesStore:drive:{driveId}                                 -> 404 if null
  // Increase satisfaction, reinforce intensity (positive feedback)
  WRITE _drivesStore:drive:{driveId} <- updated satisfaction, intensity, lastProgressAt
  DELETE _cacheStore:manifest:{characterId}
  IF satisfaction increase significant
    PUBLISH disposition.drive.satisfied { characterId, driveCode, oldSatisfaction, newSatisfaction }
  IF satisfaction >= config.DriveFulfillmentThreshold (first time)
    PUBLISH disposition.drive.fulfilled { characterId, driveCode, totalDuration }
RETURN (200, DriveResponse)
```

### RecordFrustration
POST /disposition/drive/record-frustration | Roles: []

```
LOCK disposition-lock:drive:{characterId}                           -> 409 if timeout
  READ _drivesStore:drive:{driveId}                                 -> 404 if null
  // Increase frustration
  WRITE _drivesStore:drive:{driveId} <- updated frustration, lastFrustrationAt
  IF frustration increase significant
    PUBLISH disposition.drive.frustrated { characterId, driveCode, oldFrustration, newFrustration }
  IF intensity < config.DriveMinIntensityThreshold
    DELETE _drivesStore:drive:{driveId} and all indexes
    PUBLISH disposition.drive.abandoned { characterId, driveCode, finalState }
  DELETE _cacheStore:manifest:{characterId}
RETURN (200, DriveResponse)
```

### FindByDrive
POST /disposition/drive/find-by-drive | Roles: []

```
QUERY _drivesStore:drive:code-idx:{driveCode}
  // Optional filter: minIntensity
  // PAGED(body.Page ?? 1, body.PageSize ?? config.QueryPageSize)
RETURN (200, FindByDriveResponse { characters, totalCount, page, pageSize })
```

### RecordSpiritAction
POST /disposition/guardian/record-action | Roles: []

```
CALL _characterClient.GetCharacterAsync(body.CharacterId)          -> 404 if null
// Resolve guardian spirit characterId (NEXIUS system realm character)
LOCK disposition-lock:feeling:{characterId}:Character:{guardianCharacterId}  -> 409 if timeout
  READ _feelingsStore guardian axes (trust, resentment, familiarity, gratitude, defiance)
  // Compute alignment from last N nudges (config.GuardianAlignmentWindowSize)
  // Update guardian feeling axes based on alignment signal and action outcome
  WRITE _feelingsStore guardian feeling entries <- updated axes
  DELETE _cacheStore:manifest:{characterId}
  PUBLISH disposition.guardian.shifted { characterId, axis, oldValue, newValue }
RETURN (200, GuardianActionResponse)
```

### GetSpiritRelationship
POST /disposition/guardian/get-relationship | Roles: []

```
CALL _characterClient.GetCharacterAsync(body.CharacterId)          -> 404 if null
READ _feelingsStore guardian axes for characterId                   -> 404 if no guardian history
// compliance = trust * 0.4 + familiarity * 0.2 - resentment * 0.3 - defiance * 0.1
// clamp(compliance, 0.0, 1.0)
// trending = derive from lastModifiedAt timestamps and shift direction
RETURN (200, GuardianRelationshipResponse { axes, compliance, recentHistory, trending })
```

### CleanupByCharacter
POST /disposition/cleanup-by-character | Roles: []

```
// Called by lib-resource (CASCADE on character deletion)
QUERY _feelingsStore WHERE characterId == body.CharacterId
FOREACH feeling [per-item error isolation]
  DELETE _feelingsStore:feeling:{feelingId}
  DELETE from feeling:about reverse indexes
DELETE _feelingsStore:feeling:char:{characterId} (and all sub-indexes)

QUERY _drivesStore WHERE characterId == body.CharacterId
FOREACH drive [per-item error isolation]
  DELETE _drivesStore:drive:{driveId}
  DELETE from drive:code-idx:{driveCode}
DELETE _drivesStore:drive:char:{characterId} (and all sub-indexes)

DELETE _cacheStore:manifest:{characterId}
RETURN (200, CleanupResponse { feelingsRemoved, drivesRemoved })
```

### CleanupByRealm
POST /disposition/cleanup-by-realm | Roles: []

```
// Called by lib-resource (CASCADE on realm deletion)
// Enumerate characters in realm — requires ICharacterClient query
CALL _characterClient.ListCharactersByRealmAsync(body.RealmId)
FOREACH character [per-item error isolation]
  // Delegate to CleanupByCharacter logic
  CleanupByCharacter(character.CharacterId)
RETURN (200, RealmCleanupResponse { charactersAffected, feelingsRemoved, drivesRemoved })
```

### GetCompressData
POST /disposition/get-compress-data | Roles: []

```
// Called by lib-resource for character compression
QUERY _feelingsStore WHERE characterId == body.CharacterId AND abs(effectiveValue) > 0.3
QUERY _drivesStore WHERE characterId == body.CharacterId
RETURN (200, DispositionArchive { characterId, feelings (high-magnitude), drives (all) })
```

### RestoreFromArchive
POST /disposition/restore-from-archive | Roles: []

```
// Called by lib-resource for character decompression
CALL _characterClient.GetCharacterAsync(body.CharacterId)          -> 404 if null
FOREACH feeling in archive.Feelings
  // Restore modifier at 50% strength (emotional distance)
  WRITE _feelingsStore:feeling:{feelingId} <- feeling with modifier * 0.5
  WRITE _feelingsStore indexes
FOREACH drive in archive.Drives
  // Restore drives as-is (aspirations persist)
  WRITE _drivesStore:drive:{driveId} <- drive
  WRITE _drivesStore indexes
DELETE _cacheStore:manifest:{characterId}
// Trigger immediate base resynthesis from current source data
LOCK disposition-lock:synthesis:{characterId}
  // ResynthesizeBase logic (sources may have changed during compression)
RETURN (200, RestoreResponse { feelingsRestored, drivesRestored })
```

---

## Background Services

### DispositionModifierDecayWorkerService
**Interval**: `config.ModifierDecayIntervalMinutes`
**Purpose**: Decays feeling modifiers toward zero over time; removes negligible feelings

```
LOCK disposition-lock:decay-worker
  QUERY _feelingsStore WHERE modifier != 0 ORDER BY lastModifiedAt ASC LIMIT config.ModifierDecayBatchSize
  FOREACH feeling [per-item error isolation]
    daysSince = (UtcNow - feeling.LastModifiedAt).TotalDays
    newModifier = modifier * (1.0 - modifierDecayRate * daysSince)
    IF abs(newModifier) < config.MinConfidenceForProvider AND abs(baseValue) < config.MinConfidenceForProvider
      DELETE _feelingsStore:feeling:{feelingId} and all indexes
    ELSE
      WRITE _feelingsStore:feeling:{feelingId} <- updated modifier, effectiveValue
  PUBLISH disposition.feeling.decayed { batch counts, affected characterIds }
  DELETE affected _cacheStore:manifest entries
```

### DispositionSynthesisWorkerService
**Interval**: `config.SynthesisIntervalMinutes`
**Purpose**: Periodically resynthesizes base values from source services

```
LOCK disposition-lock:synthesis-worker
  QUERY _feelingsStore WHERE lastSynthesizedAt < (UtcNow - SynthesisInterval) LIMIT config.SynthesisBatchSize (distinct characterIds)
  FOREACH characterId [per-item error isolation]
    // Delegate to ResynthesizeBase logic
    ResynthesizeBase(characterId)
```

### DispositionDriveEvaluationWorkerService
**Interval**: `config.DriveEvaluationIntervalMinutes`
**Purpose**: Evaluates drive formation, evolution, satisfaction decay, and removal

```
LOCK disposition-lock:drive-worker
  QUERY _drivesStore for characters with active drives (batched)
  FOREACH characterId [per-item error isolation]
    SOFT-CALL ICharacterPersonalityClient (personality traits)
    SOFT-CALL ICharacterHistoryClient (backstory elements)
    SOFT-CALL IFactionClient (faction membership)

    // Satisfaction decay
    FOREACH drive
      daysSince = (UtcNow - (lastProgressAt ?? acquiredAt)).TotalDays
      newSatisfaction = satisfaction * (1.0 - config.DriveSatisfactionDecayRatePerDay * daysSince)
      WRITE _drivesStore:drive:{driveId} <- updated satisfaction

    // Removal: drives below threshold
    FOREACH drive WHERE intensity < config.DriveMinIntensityThreshold
      DELETE _drivesStore:drive:{driveId} and all indexes
      PUBLISH disposition.drive.abandoned { characterId, driveCode, finalState }

    // Fulfillment detection
    FOREACH drive WHERE satisfaction >= config.DriveFulfillmentThreshold
      PUBLISH disposition.drive.fulfilled { characterId, driveCode, totalDuration }

    // Personality-innate drive formation
    IF personality conditions met AND domain engagement threshold reached
      SeedDrive(characterId, driveCode, category, Personality, intensity)
      PUBLISH disposition.drive.formed

    // Intensity evolution for recently satisfied drives
    FOREACH drive WHERE lastProgressAt is recent
      WRITE _drivesStore:drive:{driveId} <- increased intensity
      PUBLISH disposition.drive.intensified (if significant)

    DELETE _cacheStore:manifest:{characterId}
```

---

## Non-Standard Implementation Patterns

#### OnRunningAsync

```
// Register resource cleanup callbacks (per FOUNDATION TENETS T28)
CALL _resourceClient.RegisterResourceCleanupCallbacksAsync()
  // character → CASCADE → /disposition/cleanup-by-character
  // realm → CASCADE → /disposition/cleanup-by-realm

// Register compression callback
CALL _resourceClient.RegisterCompressionCallbackAsync()
  // character → priority 20 → /disposition/get-compress-data, /disposition/restore-from-archive

// Validate synthesis weight sum at startup (resolved design decision, 2026-03-16)
// x-constraint-groups sum-equals: 1.0 in configuration schema
IF abs(sum(weights) - 1.0) > epsilon
  LOG Warning "Synthesis weights do not sum to 1.0"
```
