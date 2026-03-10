# Storyline Implementation Map

> **Plugin**: lib-storyline
> **Schema**: schemas/storyline-api.yaml
> **Layer**: GameFeatures
> **Deep Dive**: [docs/plugins/STORYLINE.md](../plugins/STORYLINE.md)

---

## Summary Table

| Field | Value |
|-------|-------|
| Plugin | lib-storyline |
| Layer | L4 GameFeatures |
| Endpoints | 15 |
| State Stores | storyline-plans (Redis), storyline-plan-index (Redis), storyline-scenario-definitions (MySQL), storyline-scenario-executions (MySQL), storyline-scenario-cache (Redis), storyline-scenario-cooldown (Redis), storyline-scenario-active (Redis), storyline-scenario-idempotency (Redis), storyline-lock (Redis) |
| Events Published | 3 actually published (storyline.plan.composed, storyline.scenario.triggered, storyline.scenario.completed); 6 additional defined in schema but not yet published (storyline.scenario-definition.created, storyline.scenario-definition.updated, storyline.scenario-definition.deleted, storyline.scenario.phase-completed, storyline.scenario.failed, storyline.scenario.available) |
| Events Consumed | 0 |
| Client Events | 0 |
| Background Services | 0 |

---

## State

**Store**: `storyline-plans` (Backend: Redis, IStateStore)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{planId}` | `CachedPlan` | Cached composed storyline plan with TTL |
| `cache:{seed\|goal\|arc\|genre\|archives\|snapshots}` | `CachedPlan` | Deterministic cache key (when seed provided) |

**Store**: `storyline-plan-index` (Backend: Redis, ICacheableStateStore, Sorted Set)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `realm:{realmId}` | Sorted Set (score = unix timestamp) | Plan index by realm for paginated list queries |

**Store**: `storyline-scenario-definitions` (Backend: MySQL, IQueryableStateStore)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{scenarioId}` | `ScenarioDefinitionModel` | Durable scenario template definitions with conditions, phases, mutations, quest hooks |

**Store**: `storyline-scenario-cache` (Backend: Redis, ICacheableStateStore)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{scenarioId}` | `ScenarioDefinitionModel` | Read-through cache for scenario definitions (TTL from config) |

**Store**: `storyline-scenario-executions` (Backend: MySQL, IQueryableStateStore)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{executionId}` | `ScenarioExecutionModel` | Durable scenario execution history with status, phase progress, outcomes |

**Store**: `storyline-scenario-cooldown` (Backend: Redis, ICacheableStateStore)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `cooldown:{characterId}:{scenarioId}` | `CooldownMarker` | Per-character per-scenario cooldown with TTL-based auto-expiry |

**Store**: `storyline-scenario-active` (Backend: Redis, ICacheableStateStore, Set)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `active:{characterId}` | Set of `ActiveScenarioEntry` | Active scenario tracking per character (set membership) |

**Store**: `storyline-scenario-idempotency` (Backend: Redis, ICacheableStateStore)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{idempotencyKey}` | `IdempotencyMarker` | Trigger deduplication with TTL-based auto-expiry |

**Store**: `storyline-lock` (Backend: Redis) -- used via `IDistributedLockProvider` only

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `lock:{characterId}:{scenarioId}` | lock | Distributed lock for scenario trigger operations |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | Persistence for plans (Redis), scenario definitions (MySQL), executions (MySQL), cooldowns (Redis), active tracking (Redis), idempotency (Redis) |
| lib-state (IDistributedLockProvider) | L0 | Hard | Distributed locks on scenario trigger operations |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing service events (storyline.plan.composed, scenario triggered/completed) |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Span instrumentation on async helper methods |
| lib-resource (IResourceClient) | L1 | Hard | Fetching archive and snapshot data for composition |
| lib-relationship (IRelationshipClient) | L2 | Hard | Creating/ending relationships during mutation application |
| lib-character-personality (ICharacterPersonalityClient) | L4 | Soft | Recording personality experiences during PersonalityEvolve mutations (graceful degradation if absent) |
| lib-character-history (ICharacterHistoryClient) | L4 | Soft | Adding backstory elements during BackstoryAdd mutations (graceful degradation if absent) |
| lib-quest (IQuestClient) | L4 | Soft | Spawning quests via quest hooks during scenario trigger (graceful degradation if absent) |

**Notes**:
- `IRelationshipClient` is constructor-injected as a hard L2 dependency. Used only during RelationshipCreate/RelationshipEnd mutations.
- L4 soft dependencies (`ICharacterPersonalityClient`, `ICharacterHistoryClient`, `IQuestClient`) are resolved at runtime via `_serviceProvider.GetService<T>()` with null check. If absent, the corresponding mutation/quest hook is skipped with a logged message.
- `StorylineComposer` (SDK) is directly instantiated in the constructor -- pure computation, not DI.
- The compression callback (`x-compression-callback`) is registered on the schema for Resource service integration. The `GetCompressData` endpoint is called by Resource during character archival.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `storyline.plan.composed` | `StorylinePlanComposedEvent` | Compose -- after successful plan generation, includes planId, goal, arcType, confidence, archive/snapshot IDs, generation time |
| `storyline.scenario.triggered` | `ScenarioTriggeredEvent` | TriggerScenario -- after execution record created, includes executionId, scenarioId, characterId, participants, phase count |
| `storyline.scenario.completed` | `ScenarioCompletedEvent` | TriggerScenario -- after all mutations applied and quests spawned, includes mutation/quest counts, duration, quest IDs |

**Defined in schema but NOT currently published**:
- `storyline.scenario-definition.created` -- CreateScenarioDefinition does not publish this event
- `storyline.scenario-definition.updated` -- UpdateScenarioDefinition does not publish this event; DeprecateScenarioDefinition publishes with changedFields
- `storyline.scenario-definition.deleted` -- Category B infrastructure, never published by design
- `storyline.scenario.phase-completed` -- Multi-phase execution not yet implemented
- `storyline.scenario.failed` -- Failure path not yet implemented (current trigger completes atomically)
- `storyline.scenario.available` -- Discovery notification not yet implemented

---

## Events Consumed

This plugin does not consume external events.

---

## DI Services

#### Constructor Dependencies

| Service | Role |
|---------|------|
| `IMessageBus` | Event publishing (storyline.plan.composed, scenario triggered/completed) |
| `IStateStoreFactory` | Constructor-consumed to acquire all 8 state stores |
| `IResourceClient` | Fetching archives and snapshots from Resource service |
| `IRelationshipClient` | Creating/ending relationships during mutation application |
| `IServiceProvider` | Runtime resolution of soft L4 dependencies (personality, history, quest clients) |
| `IDistributedLockProvider` | Distributed locks for scenario trigger deduplication |
| `ITelemetryProvider` | Telemetry spans on async helper methods |
| `ILogger<StorylineService>` | Structured logging |
| `StorylineServiceConfiguration` | All configurable thresholds, TTLs, fit score weights, cooldown defaults |

---

## Method Index

> **Roles column**: `[]` = service-to-service only (not exposed via WebSocket). See SCHEMA-RULES.md x-permissions.

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| Compose | POST /storyline/compose | generated | [developer] | plan, plan-index | storyline.plan.composed |
| GetPlan | POST /storyline/plan/get | generated | [developer] | - | - |
| ListPlans | POST /storyline/plan/list | generated | [developer] | - | - |
| CreateScenarioDefinition | POST /storyline/scenario/create | generated | [developer] | scenario-definition, scenario-cache | - |
| GetScenarioDefinition | POST /storyline/scenario/get | generated | [user] | scenario-cache (write-through) | - |
| ListScenarioDefinitions | POST /storyline/scenario/list | generated | [user] | - | - |
| UpdateScenarioDefinition | POST /storyline/scenario/update | generated | [developer] | scenario-definition, scenario-cache | - |
| DeprecateScenarioDefinition | POST /storyline/scenario/deprecate | generated | [developer] | scenario-definition, scenario-cache | storyline.scenario-definition.updated |
| FindAvailableScenarios | POST /storyline/scenario/find-available | generated | [] | - | - |
| TestScenarioTrigger | POST /storyline/scenario/test | generated | [developer] | - | - |
| EvaluateScenarioFit | POST /storyline/scenario/evaluate-fit | generated | [] | - | - |
| TriggerScenario | POST /storyline/scenario/trigger | generated | [] | scenario-execution, scenario-active, scenario-cooldown, scenario-idempotency | storyline.scenario.triggered, storyline.scenario.completed |
| GetActiveScenarios | POST /storyline/scenario/get-active | generated | [user] | - | - |
| GetScenarioHistory | POST /storyline/scenario/get-history | generated | [user] | - | - |
| GetCompressData | POST /storyline/get-compress-data | generated | [] | - | - |

---

## Methods

### Compose
POST /storyline/compose | Roles: [developer]

```
IF body.SeedSources.Count == 0                                   -> 400
IF body.SeedSources.Count > config.MaxSeedSources                -> 400

// Deterministic cache check (when seed provided and cache enabled)
IF body.Seed set AND config.PlanCacheEnabled
  cacheKey = ComputeCacheKey(body)
    // key = "cache:seed:{seed}|goal:{goal}|arc:{arcType}|genre:{genre}|archives:{sorted}|snapshots:{sorted}"
  READ _planStore:cacheKey
  IF cached != null
    RETURN (200, ComposeResponse from cached plan, cached=true, generationTimeMs=0)

// Fetch archives/snapshots from Resource
// see helper: FetchSeedDataAsync
FOREACH source in body.SeedSources
  IF source.ArchiveId set
    CALL IResourceClient.GetArchiveAsync(archiveId)              -> 400 if not found
    // see helper: PopulateBundleFromEntries (decompress gzip+base64, add to ArchiveBundle)
  ELSE IF source.SnapshotId set
    CALL IResourceClient.GetSnapshotAsync(snapshotId)            -> 400 if not found
    // see helper: PopulateBundleFromEntries
  ELSE
    -> 400 "must have archiveId or snapshotId"

// Resolve arc type: request override -> goal mapping
// see helper: ResolveArcType
//   Revenge->Oedipus, Resurrection->ManInHole, Legacy->RagsToRiches,
//   Mystery->Cinderella, Peace->ManInHole, default->ManInHole

// Get SDK template for arc type
template = TemplateRegistry.Get(arcType)                         -> 400 if not found

genre = body.Genre ?? config.DefaultGenre

// Resolve primary spectrum from goal
// see helper: ResolveSpectrumFromGoal
//   Revenge->JusticeInjustice, Resurrection->LifeDeath, Legacy->SuccessFailure,
//   Mystery->WisdomIgnorance, Peace->LoveHate, default->JusticeInjustice

// Extract character IDs and realm from archive data
// see helper: ExtractEntitiesFromArchives

// Build Greimas actant assignments from seed source roles
// see helper: BuildActantAssignments
//   Maps role strings to ActantRole enum (protagonist->Subject, antagonist->Opponent, etc.)
//   Default: first character becomes Subject if no assignments

urgency = body.Urgency ?? config.DefaultPlanningUrgency

// Call SDK
sdkPlan = StorylineComposer.Compose(context, bundle, urgency)    -> 400 if SDK error

planId = NewGuid()

// Build response with confidence and risks
// see helper: CalculateConfidence (config-driven: base + phase bonus + core event bonus + action count bonus, capped at 1.0)
// see helper: IdentifyRisks (thin_content if actions < RiskMinActionThreshold, missing_obligatory_scenes if coreEvents == 0, flat_arc if phases < RiskMinPhaseThreshold)
// see helper: InferThemes (goal name + arc-specific themes)

// Cache plan
WRITE _planStore:{planId} <- CachedPlan [with TTL = config.PlanCacheTtlSeconds]

// If seed provided, also cache under deterministic key
IF body.Seed set AND config.PlanCacheEnabled
  WRITE _planStore:cache:{cacheKey} <- CachedPlan [with TTL = config.PlanCacheTtlSeconds]

// Update realm index
IF body.Constraints.RealmId set
  WRITE _planIndexStore (sorted set):realm:{realmId} <- planId (score = createdAt unix seconds)

// see helper: PublishComposedEventAsync
PUBLISH storyline.plan.composed { planId, realmId, goal, arcType, primarySpectrum, confidence, genre, archiveIds, snapshotIds, phaseCount, entityCount, generationTimeMs, cached, seed, composedAt }

RETURN (200, ComposeResponse { planId, confidence, goal, genre, arcType, primarySpectrum, themes, phases, risks, generationTimeMs, cached=false })
```

### GetPlan
POST /storyline/plan/get | Roles: [developer]

```
READ _planStore:{planId}                                         -> 404 if null
RETURN (200, GetPlanResponse { plan: ComposeResponse from cached })
```

### ListPlans
POST /storyline/plan/list | Roles: [developer]

```
IF body.RealmId set
  indexKey = realm:{realmId}
  totalCount = SORTED_SET_COUNT _planIndexStore:indexKey
  rangeResults = SORTED_SET_RANGE _planIndexStore:indexKey (offset, offset+limit-1, descending)
  FOREACH (planIdStr, score) in rangeResults
    READ _planStore:{planIdStr}
    IF cached != null
      // Build PlanSummary { planId, goal, arcType, confidence, realmId, createdAt, expiresAt }
ELSE
  // No realm filter: return empty list (full scan expensive)

RETURN (200, ListPlansResponse { plans, totalCount })
```

### CreateScenarioDefinition
POST /storyline/scenario/create | Roles: [developer]

```
normalizedCode = body.Code.ToUpperInvariant().Replace('-', '_')

// Check duplicate code within scope
// see helper: FindScenarioByCodeAsync
QUERY _scenarioDefinitionStore WHERE Code == normalizedCode      -> 409 if exists

scenarioId = NewGuid()
etag = NewGuid()

// Build storage model with JSON-serialized nested objects
WRITE _scenarioDefinitionStore:{scenarioId} <- ScenarioDefinitionModel
WRITE _scenarioCacheStore:{scenarioId} <- ScenarioDefinitionModel [with TTL = config.ScenarioDefinitionCacheTtlSeconds]

// NOTE: storyline.scenario-definition.created event is NOT published (schema defines it but implementation omits)

RETURN (200, ScenarioDefinition)
```

### GetScenarioDefinition
POST /storyline/scenario/get | Roles: [user]

```
IF body.ScenarioId set
  // see helper: GetScenarioDefinitionWithCacheAsync
  READ _scenarioCacheStore:{scenarioId}                          // try cache first
  IF null
    READ _scenarioDefinitionStore:{scenarioId}                   // fall back to MySQL
    IF found
      WRITE _scenarioCacheStore:{scenarioId} <- model [with TTL] // populate cache
ELSE IF body.Code set
  normalizedCode = body.Code.ToUpperInvariant().Replace('-', '_')
  // see helper: FindScenarioByCodeAsync
  QUERY _scenarioDefinitionStore WHERE Code == normalizedCode
ELSE
  -> 400

-> 404 if null

RETURN (200, GetScenarioDefinitionResponse { scenario: ScenarioDefinition })
```

### ListScenarioDefinitions
POST /storyline/scenario/list | Roles: [user]

```
QUERY _scenarioDefinitionStore WHERE true                        // all definitions from MySQL
// Apply filters in memory:
IF body.RealmId set
  filter: null RealmId (global) OR RealmId == body.RealmId
IF body.GameServiceId set
  filter: null GameServiceId (global) OR GameServiceId == body.GameServiceId
IF body.Tags set AND count > 0
  filter: definition tags intersect with requested tags (OR logic)
IF !body.IncludeDeprecated
  filter: !IsDeprecated

// Paginate: order by Priority desc, then Code asc
paginated = filtered.Skip(body.Offset).Take(body.Limit)

// Build summaries with condition/phase/mutation/quest hook counts
RETURN (200, ListScenarioDefinitionsResponse { scenarios, totalCount })
```

### UpdateScenarioDefinition
POST /storyline/scenario/update | Roles: [developer]

```
// see helper: GetScenarioDefinitionWithCacheAsync
READ scenario definition by ID (cache -> MySQL fallback)         -> 404 if null

// Manual ETag concurrency check
IF existing.Etag != body.Etag                                    -> 409

// Apply non-null fields from request:
//   Name, Description, TriggerConditions (JSON), Phases (JSON),
//   Mutations (JSON), QuestHooks (JSON), CooldownSeconds,
//   ExclusivityTags (JSON), Priority, Enabled, Tags (JSON)
existing.UpdatedAt = now
existing.Etag = NewGuid()

WRITE _scenarioDefinitionStore:{scenarioId} <- updated model
DELETE _scenarioCacheStore:{scenarioId}
WRITE _scenarioCacheStore:{scenarioId} <- updated model [with TTL = config.ScenarioDefinitionCacheTtlSeconds]

// NOTE: storyline.scenario-definition.updated event is NOT published

RETURN (200, ScenarioDefinition)
```

### DeprecateScenarioDefinition
POST /storyline/scenario/deprecate | Roles: [developer]

```
// see helper: GetScenarioDefinitionWithCacheAsync
READ scenario definition by ID (cache -> MySQL fallback)         -> 404 if null
IF existing.IsDeprecated == true                                 -> 200 (idempotent, per IMPLEMENTATION TENETS)

existing.IsDeprecated = true
existing.DeprecatedAt = now
existing.DeprecationReason = body.Reason
existing.Enabled = false
existing.UpdatedAt = now
existing.Etag = NewGuid()

WRITE _scenarioDefinitionStore:{scenarioId} <- updated model
DELETE _scenarioCacheStore:{scenarioId}
PUBLISH storyline.scenario-definition.updated { scenarioId, code, name, ..., changedFields: ["isDeprecated", "deprecatedAt", "deprecationReason", "enabled"] }

RETURN (200)
```

### FindAvailableScenarios
POST /storyline/scenario/find-available | Roles: []

```
// Get all candidate scenarios
QUERY _scenarioDefinitionStore WHERE true
// Filter: Enabled AND !IsDeprecated AND scope matches (realm, game service)
// Filter: exclude tags from body.ExcludeTags (if any)

FOREACH definition in candidates
  conditions = Deserialize(definition.TriggerConditionsJson)

  // Evaluate all conditions against character state
  // see helper: EvaluateConditions
  //   Base fit score from config.ScenarioFitScoreBaseWeight
  //   Per-condition type bonuses: TraitRange, BackstoryElement,
  //   RelationshipExists/Missing, AgeRange, LocationAt, TimeOfDay, WorldState
  //   Custom conditions auto-pass (not evaluated server-side)

  IF fitScore < config.ScenarioFitScoreMinimumThreshold -> skip

  // Check cooldown
  READ _scenarioCooldownStore:cooldown:{characterId}:{scenarioId}
  onCooldown = marker is not null

  // Add ScenarioMatch { scenarioId, code, name, fitScore, conditionsMet, conditionsTotal, onCooldown, cooldownExpiresAt }

// Sort by fitScore desc, conditionsMet desc; take body.MaxResults
RETURN (200, FindAvailableScenariosResponse { matches })
```

### TestScenarioTrigger
POST /storyline/scenario/test | Roles: [developer]

```
// see helper: GetScenarioDefinitionWithCacheAsync
READ scenario definition (cache -> MySQL)                        -> 404 if null

conditions = Deserialize(definition.TriggerConditionsJson)

// Evaluate each condition individually with detailed results
// see helper: EvaluateSingleCondition
//   Returns (met, actualValue, expectedValue, details) per condition type
FOREACH condition in conditions
  conditionResults.Add(ConditionResult { conditionType, met, actualValue, expectedValue, details })

// Check blocking reasons (in order):
//   1. !Enabled -> "Scenario is disabled"
//   2. IsDeprecated -> "Scenario is deprecated"
//   3. !allConditionsMet -> "Not all conditions are met"
//   4. Cooldown check: READ _scenarioCooldownStore:cooldown:{characterId}:{scenarioId}
//   5. Active limit: COUNT _scenarioActiveStore (set):active:{characterId} >= config.ScenarioMaxActivePerCharacter

// Predict mutations (only if all conditions met and no blocking reason)
IF allConditionsMet AND blockingReason is null
  mutations = Deserialize(definition.MutationsJson)
  FOREACH mutation in mutations
    // see helper: DescribeMutation (human-readable description)
    predictedMutations.Add(PredictedMutation { mutationType, description })

RETURN (200, TestScenarioResponse { wouldTrigger, conditionResults, predictedMutations, blockingReason })
```

### EvaluateScenarioFit
POST /storyline/scenario/evaluate-fit | Roles: []

```
// see helper: GetScenarioDefinitionWithCacheAsync
READ scenario definition (cache -> MySQL)                        -> 404 if null

conditions = Deserialize(definition.TriggerConditionsJson)

// Evaluate conditions against character state only (no locationId, timeOfDay, worldState)
// see helper: EvaluateConditions
(conditionsMet, fitScore) = EvaluateConditions(conditions, body.CharacterState, null, null, null)

RETURN (200, EvaluateFitResponse { fitScore, conditionsMet, conditionsTotal })
```

### TriggerScenario
POST /storyline/scenario/trigger | Roles: []

```
executionId = NewGuid()

// Idempotency check
IF body.IdempotencyKey set
  READ _scenarioIdempotencyStore:{idempotencyKey}
  IF exists
    READ _scenarioExecutionStore:{existingIdempotency.ExecutionId}
    IF found
      RETURN (200, TriggerScenarioResponse from existing execution)

// Acquire distributed lock
LOCK storyline-lock:lock:{characterId}:{scenarioId}              -> 409 if fails
  // timeout = config.ScenarioTriggerLockTimeoutSeconds

  // see helper: GetScenarioDefinitionWithCacheAsync
  READ scenario definition (cache -> MySQL)                      -> 404 if null
  IF !Enabled OR IsDeprecated                                    -> 400

  // Validate conditions (unless skipConditionCheck)
  IF !body.SkipConditionCheck
    conditions = Deserialize(definition.TriggerConditionsJson)
    (conditionsMet, fitScore) = EvaluateConditions(conditions, body.CharacterState, body.LocationId, body.TimeOfDay, body.WorldState)
    IF conditionsMet < conditions.Count                          -> 400

  // Check cooldown
  READ _scenarioCooldownStore:cooldown:{characterId}:{scenarioId} -> 409 if exists

  // Check active scenario limit
  activeCount = SET_COUNT _scenarioActiveStore:active:{characterId}
  IF activeCount >= config.ScenarioMaxActivePerCharacter          -> 409

  phases = Deserialize(definition.PhasesJson)
  mutations = Deserialize(definition.MutationsJson)
  questHooks = Deserialize(definition.QuestHooksJson)

  // Create execution record
  WRITE _scenarioExecutionStore:{executionId} <- ScenarioExecutionModel { status=Active, currentPhase=1, totalPhases=phases.Count }

  // Add to active set
  WRITE _scenarioActiveStore (set):active:{characterId} <- ActiveScenarioEntry { executionId, scenarioId, scenarioCode }

  // Store idempotency key
  IF body.IdempotencyKey set
    WRITE _scenarioIdempotencyStore:{idempotencyKey} <- IdempotencyMarker [with TTL = config.ScenarioIdempotencyTtlSeconds]

  PUBLISH storyline.scenario.triggered { executionId, scenarioId, scenarioCode, primaryCharacterId, additionalParticipants, orchestratorId, realmId, gameServiceId, fitScore, phaseCount, triggeredAt }

  // Apply mutations (all applied immediately in Phase 1)
  FOREACH mutation in mutations
    // see helper: ApplyMutationAsync
    //   PersonalityEvolve: CALL ICharacterPersonalityClient.RecordExperienceAsync (soft L4)
    //   BackstoryAdd: CALL ICharacterHistoryClient.AddBackstoryElementAsync (soft L4)
    //   RelationshipCreate: CALL IRelationshipClient.GetRelationshipTypeByCodeAsync + CreateRelationshipAsync (hard L2)
    //   RelationshipEnd: CALL IRelationshipClient.GetRelationshipTypeByCodeAsync + GetRelationshipsBetweenAsync + EndRelationshipAsync (hard L2)
    //   Custom: pass-through (caller-handled)
    appliedMutations.Add(AppliedMutation { mutationType, success, targetCharacterId, details })

  // Spawn quests
  FOREACH hook in questHooks
    // see helper: SpawnQuestAsync
    CALL IQuestClient.AcceptQuestAsync (soft L4, graceful degradation)
    IF success
      spawnedQuests.Add(SpawnedQuest { questInstanceId, questCode })

  // Update execution to completed
  execution.Status = Completed
  execution.CompletedAt = now
  execution.MutationsAppliedJson = Serialize(appliedMutations)
  execution.QuestsSpawnedJson = Serialize(spawnedQuests)
  WRITE _scenarioExecutionStore:{executionId} <- updated execution

  // Remove from active set
  DELETE _scenarioActiveStore (set):active:{characterId} <- activeEntry

  // Set cooldown
  cooldownSeconds = definition.CooldownSeconds ?? config.ScenarioCooldownDefaultSeconds
  IF cooldownSeconds > 0
    WRITE _scenarioCooldownStore:cooldown:{characterId}:{scenarioId} <- CooldownMarker [with TTL = cooldownSeconds]

  PUBLISH storyline.scenario.completed { executionId, scenarioId, scenarioCode, primaryCharacterId, additionalParticipants, orchestratorId, realmId, gameServiceId, phasesCompleted, totalMutationsApplied, totalQuestsSpawned, questIds, durationMs, startedAt, completedAt }

RETURN (200, TriggerScenarioResponse { executionId, scenarioId, status=Completed, triggeredAt, mutationsApplied, questsSpawned })
```

### GetActiveScenarios
POST /storyline/scenario/get-active | Roles: [user]

```
READ _scenarioActiveStore (set):active:{characterId}             // get all set members

FOREACH entry in activeMembers
  READ _scenarioExecutionStore:{entry.ExecutionId}
  IF execution != null
    // Build ScenarioExecution { executionId, scenarioId, code, name, status, currentPhase, totalPhases, triggeredAt, completedAt }

RETURN (200, GetActiveScenariosResponse { executions })
```

### GetScenarioHistory
POST /storyline/scenario/get-history | Roles: [user]

```
QUERY _scenarioExecutionStore WHERE PrimaryCharacterId == body.CharacterId
// Order by TriggeredAt desc
totalCount = results.Count

// Paginate: Skip(body.Offset).Take(body.Limit)
FOREACH execution in paginated
  // Build ScenarioExecution { executionId, scenarioId, code, name, status, currentPhase, totalPhases, triggeredAt, completedAt }

RETURN (200, GetScenarioHistoryResponse { executions, totalCount })
```

### GetCompressData
POST /storyline/get-compress-data | Roles: []

```
// Query all scenario executions for character
QUERY _scenarioExecutionStore WHERE PrimaryCharacterId == body.CharacterId
// Order by TriggeredAt desc

// Get active scenarios from Redis set
READ _scenarioActiveStore (set):active:{characterId}
activeScenarioCodes = activeMembers.Select(a => a.ScenarioCode)

// Build participation entries from executions
FOREACH execution in characterExecutions
  participations.Add(StorylineParticipation {
    executionId, scenarioId, scenarioCode, scenarioName,
    role="primary", phase, totalPhases, status, startedAt, completedAt
  })
  IF execution.Status == Completed
    completedCount++

// Derive active arcs from scenario code prefixes (first part before underscore)
activeArcs = activeScenarioCodes.Select(code => code.Split('_')[0]).Distinct()

RETURN (200, StorylineArchive { resourceId=characterId, resourceType="storyline", archivedAt, schemaVersion=1, characterId, participations, activeArcs, completedStorylines })
```

---

## Background Services

No background services.

---

## Non-Standard Implementation Patterns

No non-standard patterns. All endpoints use the standard generated-interface workflow.

### Helper Methods

#### ComputeCacheKey
Builds a deterministic cache key from a ComposeRequest by concatenating seed, goal, arc type, genre, and sorted archive/snapshot IDs. Key format: `cache:seed:{seed}|goal:{goal}|arc:{arcType}|genre:{genre|default}|archives:{sorted,ids}|snapshots:{sorted,ids}`.

#### FetchSeedDataAsync
Iterates seed sources, fetching archives via `IResourceClient.GetArchiveAsync` and snapshots via `IResourceClient.GetSnapshotAsync`. Each entry is decompressed and added to an `ArchiveBundle`. Returns the bundle, collected archive/snapshot IDs, and any error string.

#### PopulateBundleFromEntries
Decompresses gzip+base64 encoded entry data and deserializes based on source type: `character` -> `CharacterBaseArchive`, `character-history` -> `CharacterHistoryArchive`, `character-encounter` -> `CharacterEncounterArchive`, `character-personality` -> `CharacterPersonalityArchive`. Unknown types are logged and skipped.

#### DecompressEntry
Static method. Converts base64 string to bytes, decompresses via GZipStream, reads as UTF-8 string.

#### ResolveArcType
Static method. Maps `StorylineGoal` to `ArcType`: Revenge->Oedipus, Resurrection->ManInHole, Legacy->RagsToRiches, Mystery->Cinderella, Peace->ManInHole, default->ManInHole. Request override takes precedence.

#### ResolveSpectrumFromGoal
Static method. Maps `StorylineGoal` to `SpectrumType`: Revenge->JusticeInjustice, Resurrection->LifeDeath, Legacy->SuccessFailure, Mystery->WisdomIgnorance, Peace->LoveHate, default->JusticeInjustice.

#### ResolveUrgency
Returns `requestedUrgency ?? config.DefaultPlanningUrgency`.

#### ExtractEntitiesFromArchives
Static method. Extracts character IDs and realm ID from the `CharacterBaseArchive` entry in the bundle (if present).

#### BuildActantAssignments
Static method. Maps seed source role strings to Greimas `ActantRole` enum: protagonist/subject/hero->Subject, antagonist/opponent/villain->Opponent, helper/ally/sidekick->Helper, object/goal/mcguffin->Object, sender/mentor/initiator->Sender, receiver/beneficiary->Receiver. Default: first character becomes Subject if no assignments exist.

#### CalculateConfidence
Config-driven confidence scoring. Starts at `config.ConfidenceBaseScore` (default 0.5). Adds `ConfidencePhaseBonus` if phase count >= `ConfidencePhaseThreshold`. Adds `ConfidenceCoreEventBonus` if any core events. Adds `ConfidenceActionCountBonus` if action count in `[ConfidenceMinActionCount, ConfidenceMaxActionCount]` range. Capped at 1.0.

#### IdentifyRisks
Config-driven risk identification. `thin_content` (Medium) if action count < `RiskMinActionThreshold`. `missing_obligatory_scenes` (High) if zero core events. `flat_arc` (Low) if phase count < `RiskMinPhaseThreshold`.

#### InferThemes
Static method. Yields goal name in lowercase, plus arc-specific themes: Tragedy->loss+fate, RagsToRiches->transformation+hope, ManInHole->resilience+recovery, Icarus->hubris+warning, Cinderella->perseverance+triumph, Oedipus->fate+inevitability.

#### UpdatePlanIndexAsync
Adds plan ID to the realm sorted set index with creation timestamp as score.

#### PublishComposedEventAsync
Publishes `storyline.plan.composed` event with plan ID, realm, goal, arc type, spectrum, confidence, genre, archive/snapshot IDs, phase count, entity count, generation time, cached flag, seed, and timestamp.

#### GetScenarioDefinitionWithCacheAsync
Read-through cache pattern. Tries Redis cache first (`_scenarioCacheStore`), falls back to MySQL (`_scenarioDefinitionStore`). On MySQL hit, populates Redis cache with TTL from `config.ScenarioDefinitionCacheTtlSeconds`.

#### FindScenarioByCodeAsync
Queries MySQL for scenario definitions matching normalized code. Applies additional scope filters (realm, game service) in memory.

#### BuildScenarioDefinitionResponse
Converts `ScenarioDefinitionModel` (storage, JSON-serialized nested objects) to `ScenarioDefinition` (API response, deserialized lists).

#### BuildScenarioSummary
Converts `ScenarioDefinitionModel` to `ScenarioDefinitionSummary` with condition/phase/mutation/quest hook counts.

#### EvaluateConditions
Evaluates all trigger conditions against character state snapshot. Returns (conditionsMet count, fitScore). Fit score starts at `config.ScenarioFitScoreBaseWeight` and adds per-condition-type bonuses. Capped at 1.0.

#### EvaluateSingleCondition
Evaluates one trigger condition with detailed results. Supports: TraitRange (check trait value in min/max range), BackstoryElement (check element type/key present), RelationshipExists/Missing (check relationship type code), AgeRange (check age in range), LocationAt (exact location match), TimeOfDay (hour range), WorldState (key-value match), Custom (auto-pass).

#### ApplyMutationAsync
Applies a single mutation. Dispatches by MutationType:
- PersonalityEvolve: Soft L4 call to `ICharacterPersonalityClient.RecordExperienceAsync`
- BackstoryAdd: Soft L4 call to `ICharacterHistoryClient.AddBackstoryElementAsync`
- RelationshipCreate: Hard L2 calls to `IRelationshipClient` (resolve type code, create relationship)
- RelationshipEnd: Hard L2 calls to `IRelationshipClient` (resolve type code, find active, end relationship)
- Custom: Pass-through (caller-handled)

All mutations use per-item error isolation. Returns (success, details string).

#### SpawnQuestAsync
Soft L4 dependency on `IQuestClient`. Calls `AcceptQuestAsync` with quest code, character ID, and term overrides from the hook. Delayed spawning is logged but not implemented (spawns immediately). Returns (questId, success).

#### DescribeMutation
Static method. Generates human-readable description strings for each mutation type.

#### BuildTriggerResponse
Static method. Converts `ScenarioExecutionModel` to `TriggerScenarioResponse`, deserializing JSON-stored mutations and quests.

#### SdkTypeMapper
Static utility class bridging generated API types and SDK types via `MapByName<TFrom, TTo>()`. Converts between generated and SDK representations of ArcType, SpectrumType, PlanningUrgency, StorylinePlanPhase, StorylinePlanAction, ActionEffect, NarrativeEffect, PhaseTargetState, PhasePosition.
