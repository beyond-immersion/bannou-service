# Character Personality Implementation Map

> **Plugin**: lib-character-personality
> **Schema**: schemas/character-personality-api.yaml
> **Layer**: GameFeatures
> **Deep Dive**: [docs/plugins/CHARACTER-PERSONALITY.md](../plugins/CHARACTER-PERSONALITY.md)

---

## Summary

| Field | Value |
|-------|-------|
| Plugin | lib-character-personality |
| Layer | L4 GameFeatures |
| Endpoints | 12 |
| State Stores | character-personality-statestore (MySQL) |
| Events Published | 8 (`personality.created`, `personality.updated`, `personality.evolved`, `personality.deleted`, `combat-preferences.created`, `combat-preferences.updated`, `combat-preferences.evolved`, `combat-preferences.deleted`) |
| Events Consumed | 2 (self-subscription: `personality.evolved`, `combat-preferences.evolved`) |
| Client Events | 0 |
| Background Services | 0 |

---

## State

**Store**: `character-personality-statestore` (Backend: MySQL)

Two logical key namespaces within the same physical store, separated by prefix. Both acquired via `StateStoreDefinitions.CharacterPersonality` with different type parameters.

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `personality-{characterId}` | `PersonalityData` | Trait axes with float values, version, timestamps, archetype hint |
| `combat-{characterId}` | `CombatPreferencesData` | Combat style, range, role, risk/retreat thresholds, protect-allies flag |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | Personality and combat preference persistence |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing 8 lifecycle and evolution events |
| lib-messaging (IEventConsumer) | L0 | Hard | Self-subscription for cache invalidation |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Span instrumentation in event handlers and providers |
| lib-resource (IResourceClient) | L1 | Hard | Character reference registration/unregistration; cleanup and compression callback registration |

**Notes**:
- No L2 service client dependencies. Character references tracked via lib-resource (L1), not ICharacterClient.
- No account.deleted handler required — characters are L2 entities cleaned up via lib-resource cascade.
- `IPersonalityDataCache` is an internal singleton cache, not a cross-service dependency.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `personality.created` | `PersonalityCreatedEvent` | SetPersonality when no personality exists |
| `personality.updated` | `PersonalityUpdatedEvent` | SetPersonality when personality already exists |
| `personality.evolved` | `PersonalityEvolvedEvent` | RecordExperience when evolution occurs and ETag save succeeds |
| `personality.deleted` | `PersonalityDeletedEvent` | DeletePersonality; CleanupByCharacter (if personality existed) |
| `combat-preferences.created` | `CombatPreferencesCreatedEvent` | SetCombatPreferences when no combat prefs exist |
| `combat-preferences.updated` | `CombatPreferencesUpdatedEvent` | SetCombatPreferences when combat prefs already exist |
| `combat-preferences.evolved` | `CombatPreferencesEvolvedEvent` | EvolveCombatPreferences when evolution occurs and ETag save succeeds |
| `combat-preferences.deleted` | `CombatPreferencesDeletedEvent` | DeleteCombatPreferences; CleanupByCharacter (if combat prefs existed) |

All events published via generated typed extension methods (`_messageBus.Publish*Async`). No inline topic strings.

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `personality.evolved` | `HandlePersonalityEvolvedAsync` | Invalidates IPersonalityDataCache for characterId (cross-node cache coherence) |
| `combat-preferences.evolved` | `HandleCombatPreferencesEvolvedAsync` | Invalidates IPersonalityDataCache for characterId (same cache covers both) |

Self-subscription pattern: the plugin publishes and subscribes to its own evolution events for in-memory cache invalidation across nodes per IMPLEMENTATION TENETS multi-instance safety.

---

## DI Services

#### Constructor Dependencies

| Service | Role |
|---------|------|
| `ILogger<CharacterPersonalityService>` | Structured logging |
| `CharacterPersonalityServiceConfiguration` | Evolution probabilities, batch limits, retry counts, combat shift multipliers |
| `IStateStoreFactory` | State store acquisition (not stored as field) |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Self-subscription registration (not stored as field) |
| `IPersonalityDataCache` | Cache invalidation target for event handlers |
| `IResourceClient` | Character reference tracking and callback registration |
| `ITelemetryProvider` | Span instrumentation |

#### DI Interfaces Implemented by This Plugin

| Interface | Registered As | Direction | Consumer |
|-----------|---------------|-----------|----------|
| `IVariableProviderFactory` (PersonalityProviderFactory) | Singleton | L4→L2 pull | Actor (L2) discovers via `IEnumerable<IVariableProviderFactory>` for `${personality.*}` ABML expressions |
| `IVariableProviderFactory` (CombatPreferencesProviderFactory) | Singleton | L4→L2 pull | Actor (L2) discovers via `IEnumerable<IVariableProviderFactory>` for `${combat.*}` ABML expressions |

#### Internal Helper Services

| Service | File | Lifetime | Role |
|---------|------|----------|------|
| `PersonalityDataCache` | Caching/PersonalityDataCache.cs | Singleton | In-memory TTL cache backed by two `VariableProviderCacheBucket` instances; invalidated via event handlers |
| `PersonalityProviderFactory` | Providers/PersonalityProviderFactory.cs | Singleton | Creates `PersonalityProvider` instances from cached data; provider name: `VariableProviderDefinitions.Personality` |
| `PersonalityProvider` | Providers/PersonalityProvider.cs | Per-call | Serves `${personality.version}`, `${personality.traits.*}`, `${personality.OPENNESS}`, etc. Has static `Empty` instance for non-character actors |
| `CombatPreferencesProviderFactory` | Providers/CombatPreferencesProviderFactory.cs | Singleton | Creates `CombatPreferencesProvider` instances from cached data; provider name: `VariableProviderDefinitions.Combat` |
| `CombatPreferencesProvider` | Providers/CombatPreferencesProvider.cs | Per-call | Serves `${combat.style}`, `${combat.riskTolerance}`, etc. Has static `Empty` with hard-coded defaults |

---

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| GetPersonality | POST /character-personality/get | generated | [] | - | - |
| SetPersonality | POST /character-personality/set | generated | developer | personality | personality.created / personality.updated |
| RecordExperience | POST /character-personality/evolve | generated | [] | personality | personality.evolved |
| BatchGetPersonalities | POST /character-personality/batch-get | generated | [] | - | - |
| DeletePersonality | POST /character-personality/delete | generated | [] | personality | personality.deleted |
| GetCombatPreferences | POST /character-personality/get-combat | generated | [] | - | - |
| SetCombatPreferences | POST /character-personality/set-combat | generated | developer | combat | combat-preferences.created / combat-preferences.updated |
| EvolveCombatPreferences | POST /character-personality/evolve-combat | generated | [] | combat | combat-preferences.evolved |
| DeleteCombatPreferences | POST /character-personality/delete-combat | generated | [] | combat | combat-preferences.deleted |
| CleanupByCharacter | POST /character-personality/cleanup-by-character | generated | [] | personality, combat | personality.deleted, combat-preferences.deleted |
| GetCompressData | POST /character-personality/get-compress-data | generated | [] | - | - |
| RestoreFromArchive | POST /character-personality/restore-from-archive | generated | [] | personality, combat | - |

---

## Methods

### GetPersonality
POST /character-personality/get | Roles: []

```
READ personalityStore:personality-{characterId}                  -> 404 if null
RETURN (200, PersonalityResponse)
```

---

### SetPersonality
POST /character-personality/set | Roles: [developer]

```
READ personalityStore:personality-{characterId}
IF existing == null
  // New personality
  WRITE personalityStore:personality-{characterId} <- PersonalityData from request { version: 1 }
  CALL IResourceClient.RegisterReferenceAsync(sourceId: characterId, targetType: character)
  PUBLISH personality.created { characterId, version }
ELSE
  // Update existing
  WRITE personalityStore:personality-{characterId} <- PersonalityData from request { version: existing.version + 1, preserving createdAtUnix }
  PUBLISH personality.updated { characterId, version }
RETURN (200, PersonalityResponse)
```

**Notes**: Plain `SaveAsync` (no ETag). Last writer wins intentionally — set is an authoritative replace. Resource reference source ID for personality is `characterId.ToString()`.

---

### RecordExperience
POST /character-personality/evolve | Roles: []

```
READ personalityStore:personality-{characterId} [with ETag]      -> 404 if null
// Roll evolution probability once (not re-rolled on retry)
IF Random.Shared.NextDouble() >= config.BaseEvolutionProbability * intensity
  RETURN (200, ExperienceResult { personalityEvolved: false, changedTraits: [] })

// Evolution triggered — retry loop with ETag concurrency
FOREACH attempt IN 0..config.MaxConcurrencyRetries
  IF attempt > 0
    READ personalityStore:personality-{characterId} [with ETag]  -> 404 if null
  // GetAffectedTraits maps experienceType to trait axes with direction weights
  // Shift = (MinTraitShift + (MaxTraitShift - MinTraitShift) * intensity) * direction
  // Only traits present in stored record are shifted; clamped to [-1.0, +1.0]
  ETAG-WRITE personalityStore:personality-{characterId} <- mutated PersonalityData
  IF save succeeded
    PUBLISH personality.evolved { characterId, experienceType, intensity, version, affectedTraits }
    RETURN (200, ExperienceResult { personalityEvolved: true, changedTraits, newVersion })

// All retries exhausted — evolution silently suppressed
RETURN (200, ExperienceResult { personalityEvolved: false, changedTraits: [] })
```

**Notes**: Non-deterministic (`Random.Shared`). Probability roll is evaluated once before retry loop. Silent suppression on retry exhaustion (log warning emitted but no error event).

---

### BatchGetPersonalities
POST /character-personality/batch-get | Roles: []

```
IF characterIds.Count > config.MaxBatchSize                      -> 400
FOREACH characterId IN characterIds
  READ personalityStore:personality-{characterId}
  IF data != null
    // Add to personalities list
  ELSE
    // Add to notFound list
RETURN (200, BatchPersonalityResponse { personalities, notFound })
```

**Notes**: Sequential iteration (not parallel). Always returns 200 with partial results.

---

### DeletePersonality
POST /character-personality/delete | Roles: []

```
READ personalityStore:personality-{characterId}                  -> 404 if null
CALL IResourceClient.UnregisterReferenceAsync(sourceId: characterId)
DELETE personalityStore:personality-{characterId}
PUBLISH personality.deleted { characterId }
RETURN (200)
```

**Notes**: Unregisters resource reference before delete. No distributed lock on delete.

---

### GetCombatPreferences
POST /character-personality/get-combat | Roles: []

```
READ combatPreferencesStore:combat-{characterId}                 -> 404 if null
RETURN (200, CombatPreferencesResponse)
```

---

### SetCombatPreferences
POST /character-personality/set-combat | Roles: [developer]

```
READ combatPreferencesStore:combat-{characterId}
IF existing == null
  // New combat preferences
  WRITE combatPreferencesStore:combat-{characterId} <- CombatPreferencesData from request { version: 1 }
  CALL IResourceClient.RegisterReferenceAsync(sourceId: "combat-{characterId}", targetType: character)
  PUBLISH combat-preferences.created { characterId, version }
ELSE
  // Update existing
  WRITE combatPreferencesStore:combat-{characterId} <- CombatPreferencesData from request { version: existing.version + 1, preserving createdAtUnix }
  PUBLISH combat-preferences.updated { characterId, version }
RETURN (200, CombatPreferencesResponse)
```

**Notes**: Resource reference source ID for combat is `"combat-{characterId}"` (distinct from personality's plain `characterId`). A character can have two separate references registered.

---

### EvolveCombatPreferences
POST /character-personality/evolve-combat | Roles: []

```
READ combatPreferencesStore:combat-{characterId} [with ETag]     -> 404 if null
// Roll evolution probability once
IF Random.Shared.NextDouble() >= config.CombatStyleTransitionProbability * intensity
  RETURN (200, CombatEvolutionResult { preferencesEvolved: false })

// Evolution triggered — retry loop with ETag concurrency
FOREACH attempt IN 0..config.MaxConcurrencyRetries
  IF attempt > 0
    READ combatPreferencesStore:combat-{characterId} [with ETag] -> 404 if null
  // Capture previousPreferences snapshot before mutation
  // ApplyCombatEvolution: switch on CombatExperienceType, mutates style/range/role/risk/retreat/protectAllies
  // Uses configurable probabilities for style/role transitions and shift multipliers
  ETAG-WRITE combatPreferencesStore:combat-{characterId} <- mutated CombatPreferencesData
  IF save succeeded
    PUBLISH combat-preferences.evolved { characterId, experienceType, intensity, version }
    RETURN (200, CombatEvolutionResult { preferencesEvolved: true, previousPreferences, newPreferences, newVersion })

// All retries exhausted — evolution silently suppressed
RETURN (200, CombatEvolutionResult { preferencesEvolved: false })
```

**Notes**: Same structure as RecordExperience. `CombatPreferencesEvolvedEvent` does not include which properties changed (unlike `PersonalityEvolvedEvent` which includes `affectedTraits`).

---

### DeleteCombatPreferences
POST /character-personality/delete-combat | Roles: []

```
READ combatPreferencesStore:combat-{characterId}                 -> 404 if null
CALL IResourceClient.UnregisterReferenceAsync(sourceId: "combat-{characterId}")
DELETE combatPreferencesStore:combat-{characterId}
PUBLISH combat-preferences.deleted { characterId }
RETURN (200)
```

---

### CleanupByCharacter
POST /character-personality/cleanup-by-character | Roles: []

```
READ personalityStore:personality-{characterId}
IF existingPersonality != null
  DELETE personalityStore:personality-{characterId}
  PUBLISH personality.deleted { characterId }

READ combatPreferencesStore:combat-{characterId}
IF existingCombat != null
  DELETE combatPreferencesStore:combat-{characterId}
  PUBLISH combat-preferences.deleted { characterId }

RETURN (200, CleanupByCharacterResponse { personalityDeleted, combatPreferencesDeleted })
```

**Notes**: Always returns 200 (idempotent). Called by lib-resource on character cascade delete. Does NOT call UnregisterReferenceAsync — lib-resource manages reference lifecycle as the caller.

---

### GetCompressData
POST /character-personality/get-compress-data | Roles: []

```
READ personalityStore:personality-{characterId}
READ combatPreferencesStore:combat-{characterId}
IF personalityData == null AND combatData == null                -> 404
RETURN (200, CharacterPersonalityArchive { hasPersonality, hasCombatPreferences, personality?, combatPreferences? })
```

**Notes**: Returns 404 only when both are absent. Partial data (one present) returns 200. Archive includes `ResourceId`, `ResourceType`, `SchemaVersion`, `ArchivedAt`.

---

### RestoreFromArchive
POST /character-personality/restore-from-archive | Roles: []

```
// Decompress and deserialize archive data
// try-catch: returns 400 on invalid data (base64/gzip/JSON failure)
IF archiveData.HasPersonality AND archiveData.Personality != null
  WRITE personalityStore:personality-{characterId} <- PersonalityData from archive { version + 1, preserving createdAtUnix }
  CALL IResourceClient.RegisterReferenceAsync(sourceId: characterId)
IF archiveData.HasCombatPreferences AND archiveData.CombatPreferences != null
  WRITE combatPreferencesStore:combat-{characterId} <- CombatPreferencesData from archive { version + 1, preserving createdAtUnix }
  CALL IResourceClient.RegisterReferenceAsync(sourceId: "combat-{characterId}")
RETURN (200, RestoreFromArchiveResponse { personalityRestored, combatPreferencesRestored })
```

**Notes**: Does NOT publish created events on restore — this is a system operation, not a lifecycle event. Version is bumped (+1) on restore. Uses `CompressionHelper.DecompressJsonData` and `BannouJson.Deserialize`.

---

## Background Services

No background services.

---

## Non-Standard Implementation Patterns

### Plugin Lifecycle: OnRunningAsync

```
// Register ABML template for compile-time semantic analysis
CALL IResourceTemplateRegistry.Register(CharacterPersonalityTemplate)
  // Enables SemanticAnalyzer to validate ${candidate.personality.archetypeHint} etc.

// Register cascade cleanup callback with lib-resource (generated partial class)
CALL CharacterPersonalityService.RegisterResourceCleanupCallbacksAsync(resourceClient)
  // On character delete → POST /character-personality/cleanup-by-character

// Register compression callbacks with lib-resource (generated partial class)
CALL CharacterPersonalityCompressionCallbacks.RegisterAsync(resourceClient)
  // Compress: POST /character-personality/get-compress-data
  // Decompress: POST /character-personality/restore-from-archive
  // Priority: 10 (after character base data at priority 0)
```

**Notes**: Both callback registrations catch `ApiException` internally and return `bool`. Plugin enters Running state even if registration fails (logs warning).
