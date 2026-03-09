# Storyline Plugin Deep Dive

> **Plugin**: lib-storyline
> **Schema**: schemas/storyline-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFeatures
> **State Stores**: storyline-plans (Redis), storyline-plan-index (Redis), storyline-scenario-definitions (MySQL), storyline-scenario-executions (MySQL), storyline-scenario-cache (Redis), storyline-scenario-cooldown (Redis), storyline-scenario-active (Redis), storyline-scenario-idempotency (Redis), storyline-lock (Redis)
> **Implementation Map**: [docs/maps/STORYLINE.md](../maps/STORYLINE.md)
> **Short**: Seeded narrative generation from compressed archives via storyline-theory/storyteller SDKs

---

## Overview

The Storyline service (L4 GameFeatures) wraps the `storyline-theory` and `storyline-storyteller` SDKs to provide HTTP endpoints for seeded narrative generation from compressed archives. Plans describe narrative arcs with phases, actions, and entity requirements -- callers (gods/regional watchers) decide whether to instantiate them. Also manages scenario definitions (reusable narrative templates with trigger conditions, mutations, and quest hooks) with a full CRUD lifecycle, condition-based discovery, fit scoring, and execution with distributed locking and cooldown enforcement. Provides character compression data for archival via `x-compression-callback`. Internal-only, requires the `developer` role for all endpoints.

---

## SDK Architecture

The Storyline service wraps two internal SDKs:

### storyline-theory
- **Arcs**: ArcType enum (RagsToRiches, Tragedy, ManInHole, Icarus, Cinderella, Oedipus)
- **Spectrums**: SpectrumType enum (10 Life Value spectrums from Story Grid)
- **Actants**: ActantRole enum (Greimas actantial model -- Subject, Object, Sender, Receiver, Helper, Opponent)
- **Archives**: ArchiveBundle, ArchiveExtractor, archive model types

### storyline-storyteller
- **Composition**: StorylineComposer (main entry point)
- **Planning**: StoryGoapPlanner, StorylinePlan, StorylinePlanPhase, StorylinePlanAction, PlanningUrgency
- **Templates**: StoryTemplate, PhasePosition, PhaseTargetState, TemplateRegistry
- **Actions**: ActionEffect, NarrativeEffect, EffectCardinality

The plugin bridges HTTP requests to SDK calls. SDK types are exposed directly in API responses via `x-sdk-type` annotations in the schema. An `SdkTypeMapper` static class in `StorylineServiceModels.cs` maps between generated API types and SDK types at the plugin boundary, using `MapByName` for enum conversion and manual mapping for complex types (phases, actions, effects).

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| None currently | No services consume Storyline events yet |

> **Note**: The events schema notes that future phases may have consumers subscribing to `storyline.plan.composed` for monitoring storyline activity or indexing for search. God-actors (Puppetmaster regional watchers) are the primary intended callers of the compose and scenario trigger APIs.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `PlanCacheTtlSeconds` | `STORYLINE_PLAN_CACHE_TTL_SECONDS` | `3600` | TTL in seconds for cached composed plans |
| `DefaultPlanningUrgency` | `STORYLINE_DEFAULT_PLANNING_URGENCY` | `Medium` | Default GOAP planning urgency tier (Low=1000/20, Medium=500/15, High=200/10) |
| `PlanCacheEnabled` | `STORYLINE_PLAN_CACHE_ENABLED` | `true` | Whether to cache deterministic plans (those with explicit seed) |
| `DefaultGenre` | `STORYLINE_DEFAULT_GENRE` | `"drama"` | Default genre when not specified and cannot be inferred |
| `MaxSeedSources` | `STORYLINE_MAX_SEED_SOURCES` | `10` | Maximum number of seed sources per compose request |
| `ConfidenceBaseScore` | `STORYLINE_CONFIDENCE_BASE_SCORE` | `0.5` | Base confidence score before any bonuses are applied |
| `ConfidencePhaseThreshold` | `STORYLINE_CONFIDENCE_PHASE_THRESHOLD` | `3` | Minimum number of phases to receive a phase count bonus |
| `ConfidencePhaseBonus` | `STORYLINE_CONFIDENCE_PHASE_BONUS` | `0.2` | Confidence bonus when phase threshold is met |
| `ConfidenceCoreEventBonus` | `STORYLINE_CONFIDENCE_CORE_EVENT_BONUS` | `0.15` | Confidence bonus when plan contains core events |
| `ConfidenceActionCountBonus` | `STORYLINE_CONFIDENCE_ACTION_COUNT_BONUS` | `0.15` | Confidence bonus when action count is within acceptable range |
| `ConfidenceMinActionCount` | `STORYLINE_CONFIDENCE_MIN_ACTION_COUNT` | `5` | Minimum action count for action count bonus |
| `ConfidenceMaxActionCount` | `STORYLINE_CONFIDENCE_MAX_ACTION_COUNT` | `20` | Maximum action count for action count bonus |
| `RiskMinActionThreshold` | `STORYLINE_RISK_MIN_ACTION_THRESHOLD` | `3` | Minimum action count before `thin_content` risk is flagged |
| `RiskMinPhaseThreshold` | `STORYLINE_RISK_MIN_PHASE_THRESHOLD` | `2` | Minimum phase count before `flat_arc` risk is flagged |
| `ScenarioDefinitionCacheTtlSeconds` | `STORYLINE_SCENARIO_DEFINITION_CACHE_TTL_SECONDS` | `300` | TTL in seconds for cached scenario definitions via Redis read-through cache |
| `ScenarioCooldownDefaultSeconds` | `STORYLINE_SCENARIO_COOLDOWN_DEFAULT_SECONDS` | `86400` | Default cooldown in seconds before a scenario can re-trigger for the same character; 0 = no cooldown |
| `ScenarioIdempotencyTtlSeconds` | `STORYLINE_SCENARIO_IDEMPOTENCY_TTL_SECONDS` | `3600` | TTL in seconds for idempotency keys to prevent duplicate triggers |
| `ScenarioMaxActivePerCharacter` | `STORYLINE_SCENARIO_MAX_ACTIVE_PER_CHARACTER` | `3` | Maximum number of active (in-progress) scenarios per character |
| `ScenarioTriggerLockTimeoutSeconds` | `STORYLINE_SCENARIO_TRIGGER_LOCK_TIMEOUT_SECONDS` | `30` | Timeout in seconds for the distributed lock during scenario trigger |
| `ScenarioFitScoreBaseWeight` | `STORYLINE_SCENARIO_FIT_SCORE_BASE_WEIGHT` | `0.5` | Base weight for scenario fit score calculation |
| `ScenarioTraitMatchBonus` | `STORYLINE_SCENARIO_TRAIT_MATCH_BONUS` | `0.15` | Bonus added to fit score for each matching trait condition |
| `ScenarioBackstoryMatchBonus` | `STORYLINE_SCENARIO_BACKSTORY_MATCH_BONUS` | `0.1` | Bonus added to fit score for each matching backstory condition |
| `ScenarioRelationshipMatchBonus` | `STORYLINE_SCENARIO_RELATIONSHIP_MATCH_BONUS` | `0.12` | Bonus added to fit score for each matching relationship condition |
| `ScenarioLocationMatchBonus` | `STORYLINE_SCENARIO_LOCATION_MATCH_BONUS` | `0.08` | Bonus added to fit score for matching location condition |
| `ScenarioWorldStateMatchBonus` | `STORYLINE_SCENARIO_WORLD_STATE_MATCH_BONUS` | `0.05` | Bonus added to fit score for matching world state conditions |
| `ScenarioFitScoreMinimumThreshold` | `STORYLINE_SCENARIO_FIT_SCORE_MINIMUM_THRESHOLD` | `0.3` | Minimum fit score required for a scenario to appear in find-available results |

---

## Visual Aid

```
Scenario Execution Flow (TriggerScenarioAsync)
=================================================

  Regional Watcher           StorylineService                  Redis                MySQL
  (god-actor)
       │                         │                              │                    │
       │  POST /scenario/trigger │                              │                    │
       │ ──────────────────────>│                              │                    │
       │                         │  Check idempotency key       │                    │
       │                         │ ────────────────────────────>│ (idemp store)      │
       │                         │  <── null (not duplicate) ───│                    │
       │                         │                              │                    │
       │                         │  Acquire distributed lock    │                    │
       │                         │ ────────────────────────────>│ (lock store)       │
       │                         │  <── lock acquired ──────────│                    │
       │                         │                              │                    │
       │                         │  Get scenario definition     │                    │
       │                         │ ────────────────────────────>│ (cache store)      │
       │                         │  <── cache miss ─────────────│                    │
       │                         │ ────────────────────────────────────────────────>│
       │                         │  <── definition ────────────────────────────────│
       │                         │  populate cache ────────────>│                    │
       │                         │                              │                    │
       │                         │  Check cooldown              │                    │
       │                         │ ────────────────────────────>│ (cooldown store)   │
       │                         │  <── null (no cooldown) ─────│                    │
       │                         │                              │                    │
       │                         │  Check active count          │                    │
       │                         │ ────────────────────────────>│ (active store)     │
       │                         │  <── count < max ────────────│                    │
       │                         │                              │                    │
       │                         │  Save execution record       │                    │
       │                         │ ────────────────────────────────────────────────>│
       │                         │  Add to active set ─────────>│                    │
       │                         │  Store idempotency key ─────>│                    │
       │                         │                              │                    │
       │                         │  Publish scenario.triggered  │                    │
       │                         │  Apply mutations (L4 clients)│                    │
       │                         │  Spawn quests (L4 client)    │                    │
       │                         │                              │                    │
       │                         │  Update execution: Completed │                    │
       │                         │ ────────────────────────────────────────────────>│
       │                         │  Remove from active set ────>│                    │
       │                         │  Set cooldown with TTL ─────>│                    │
       │                         │                              │                    │
       │                         │  Publish scenario.completed  │                    │
       │  <── TriggerResponse ───│                              │                    │


State Store Key Relationships
================================

  Plan Stores (Redis, ephemeral):
    storyline:plan:{planId}           → CachedPlan (TTL from config)
    storyline:plan:cache:{cacheKey}   → CachedPlan (deterministic cache)
    storyline:idx:realm:{realmId}     → Sorted Set (planId → timestamp score)

  Scenario Stores:
    MySQL (durable):
      {scenarioId}                    → ScenarioDefinitionModel
      {executionId}                   → ScenarioExecutionModel

    Redis (ephemeral):
      storyline:scenario:cache:{scenarioId}        → ScenarioDefinitionModel (TTL read-through)
      storyline:scenario:cd:cooldown:{charId}:{scenarioId} → CooldownMarker (TTL)
      storyline:scenario:active:active:{charId}    → Set<ActiveScenarioEntry>
      storyline:scenario:idemp:{idempotencyKey}    → IdempotencyMarker (TTL)
      storyline:lock:lock:{charId}:{scenarioId}    → Distributed lock
```

---

## Stubs & Unimplemented Features

1. **ContinuePhase**: The SDK has a `ContinuePhase` method for multi-phase composition but the HTTP API only exposes single-call composition. No endpoint exists for iterative phase generation.

2. **EntitiesToSpawn**: The `ComposeResponse.entitiesToSpawn` field is always null. The code comment says "MVP: callers provide archive IDs, no entity spawning."

3. **Links extraction**: The `ComposeResponse.links` field is always null. Comment: "MVP: no link extraction."

4. **Event subscription**: The events schema notes "Future phases may subscribe to `resource.compressed` for discovery" but no subscriptions are implemented. `RegisterEventConsumers` in `StorylineServiceEvents.cs` is empty.

5. **Delayed quest spawning**: `ScenarioQuestHook.DelaySeconds` is accepted but ignored. All quests are spawned immediately with a debug log noting delayed spawning is not implemented.

6. **Multi-phase scenario execution**: The `TriggerScenarioAsync` method applies all mutations and spawns all quests in a single synchronous pass, then marks execution as Completed. The `ScenarioPhaseCompletedEvent` topic constant exists and the `ScenarioPhase` model supports multi-phase definitions, but there is no phased execution loop. `CurrentPhase` is set to 1 then immediately set to `phases.Count` on completion.

7. **ScenarioFailedEvent**: The topic constant exists (`StorylinePublishedTopics.ScenarioFailed`) and the event schema is defined, but no code path currently publishes it. Trigger failures return error status codes but do not publish the failed event.

8. **ScenarioAvailableEvent**: The topic constant exists (`StorylinePublishedTopics.ScenarioAvailable`) and the event schema is defined, but no code path currently publishes it. It would be emitted by a background process detecting newly available scenarios.

---

## Potential Extensions

1. **ListPlans without realm filter**: Currently returns empty results when no realm filter is provided. Could implement paginated full scan or require realm filter (breaking change).

2. **Plan invalidation**: No mechanism to invalidate cached plans when source archives change. Could subscribe to `resource.archive.updated` events.

3. **Streaming composition**: The SDK supports iterative phase generation via `ContinuePhase`. Could expose a streaming endpoint for long-running compositions.

4. **Plan validation**: No validation that plan actions are achievable given the world state. Could add a validation endpoint.

5. **Multi-realm plans**: Current design assumes single realm anchor. Could support cross-realm storylines.

6. **StorylineVariableProvider** (`${storyline.*}` namespace): A Variable Provider Factory implementation exposing active storyline state to ABML behavior expressions. NPC behaviors could check `${storyline.is_participant}` to prioritize storyline goals, and Regional Watchers could query `${storyline.active_count}` or `${storyline.completion_rate}` to throttle storyline spawning when too many are stalled. Would follow the same DI registration pattern as personality, encounters, and quest providers.

7. **Fidelity Scoring**: A scoring system measuring how closely a generated storyline matches its source archive material (distinct from the implemented confidence scoring, which measures plan structural quality). Proposed factors: character consistency (personality traits preserved, weight 0.3), relationship accuracy (encounter history honored, 0.25), historical grounding (backstory elements used, 0.2), thematic coherence (theme matches character arc, 0.15), plausibility (world rules respected, 0.1). Could be returned alongside confidence in `ComposeResponse` to give callers both "is this plan well-formed?" and "is this plan true to its sources?".

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None identified.

### Intentional Quirks (Documented Behavior)

1. **Empty ListPlans without realm filter**: Returns empty results rather than error when no `realmId` provided. This is intentional to avoid expensive full scans -- callers should always filter by realm.

2. **Gzip decompression assumed**: Archive entry data is expected to be gzip-compressed and base64-encoded. No validation or fallback for uncompressed data.

3. **Character ID from archive ID**: The actant assignment logic attempts to map archive IDs to character IDs directly, which works for character archives but may not work for other archive types.

4. **First character as default Subject**: If no role hints provided, the first character in the archive bundle becomes the Subject actant by default.

5. **GetPlan returns 404 when not found**: Returns proper `StatusCodes.NotFound` for missing or expired plans. The `Found` boolean was removed from `GetPlanResponse` -- the response wraps the plan in a nullable `plan` field, and the caller receives 404 on miss.

6. **GetScenarioDefinition returns 404 when not found**: Similarly, the `Found` boolean was removed from `GetScenarioDefinitionResponse`. The response wraps the definition in a `scenario` field, with 404 on miss.

7. **Scenario codes auto-normalized**: `CreateScenarioDefinitionAsync` converts codes to uppercase with underscores (`ToUpperInvariant().Replace('-', '_')`). A warning is logged if the code didn't match this format, but normalization proceeds silently.

8. **Deprecation disables scenario**: `DeprecateScenarioDefinitionAsync` sets both `IsDeprecated = true` and `Enabled = false`. Deprecated scenarios are excluded from `FindAvailableScenarios` by both the enabled and deprecated filters.

9. **Custom conditions always pass**: `TriggerConditionType.Custom` returns `(true, "custom", "custom", ...)` in condition evaluation -- custom conditions are considered met by default since they are caller-evaluated.

10. **Custom mutations always succeed**: `MutationType.Custom` returns `(true, "Custom mutation - caller must handle")` -- the service does not execute custom mutations, it reports success and expects the caller to handle them.

11. **GetCompressData uses `x-permissions: []`**: This endpoint is service-to-service only (called by lib-resource during character compression). It is not exposed to WebSocket clients.

12. **Scenario trigger immediately completes**: The current implementation applies all mutations and spawns all quests synchronously within `TriggerScenarioAsync`, then marks the execution as `Completed`. This is a Phase 1 simplification; true multi-phase execution with background progression is a future capability (see Stubs #6).

### Design Considerations (Requires Planning)

1. **Archive type handling**: Only handles `character`, `character-history`, `character-encounter`, `character-personality` archive types. Unknown types are logged and skipped. Should realm archives be supported for cross-realm storylines?

2. **Cache key stability**: The cache key includes seed, goal, arc type, genre, and sorted archive/snapshot IDs. Changes to this formula would invalidate existing cached plans.

3. **Plan index cleanup**: The realm index sorted set entries are not cleaned up when plans expire from the plan cache. Over time this could accumulate stale entries pointing to expired plans. No background worker exists to reconcile the index against actual plan existence.

4. **ListScenarioDefinitions full table scan**: `ListScenarioDefinitionsAsync` queries all definitions from MySQL (`d => true`) and applies filters in memory. For large numbers of scenario definitions this could become a performance concern.

5. **GetScenarioHistory redundant filter**: `GetScenarioHistoryAsync` queries MySQL with `e.PrimaryCharacterId == body.CharacterId` but then applies the same filter again in memory with `.Where(e => e.PrimaryCharacterId == body.CharacterId)`. Functionally harmless but unnecessary.

6. **FindAvailableScenarios full table scan**: `FindAvailableScenariosAsync` queries all definitions from MySQL (`d => true`) and applies scope/enabled/tag filters in memory. At scale this queries the full definitions table on every discovery call.

7. **Mutation failure does not fail trigger**: If an individual mutation fails (e.g., character-personality service unavailable), the trigger still succeeds with `Success = false` on that mutation. No compensation or rollback is performed for previously applied mutations. This may be intentional (best-effort mutations) but means scenarios can complete with partial state changes.

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above.

### Completed

- **Hardening pass (2026-03-09)** - Comprehensive tenet compliance audit and fixes:
  - T8: Removed `Found` boolean from `GetPlanResponse` and `GetScenarioDefinitionResponse`; `GetPlanAsync` returns `NotFound` on miss
  - T13: Changed `x-permissions` on `/storyline/get-compress-data` to `[]` (service-to-service only)
  - T25/T1: Changed `StorylinePlanComposedEvent` fields `goal`, `arcType`, `primarySpectrum` from `type: string` to `$ref` enum types
  - T25: `ScenarioMutation.experienceType` and `backstoryElementType` changed from `type: string` to Storyline-owned enums (`StorylineExperienceType`, `StorylineBackstoryElementType`) with A2 boundary `MapByName` mapping to CharacterPersonality/CharacterHistory enums; `EnumMappingValidator` subset tests added
  - T21: Removed dead config property `ScenarioFitScoreRecommendThreshold`
  - T5: Replaced all inline topic strings with `StorylinePublishedTopics` constants
  - T6: Replaced all inline key construction with `Build*Key()` methods (`BuildPlanKey`, `BuildPlanIndexKey`, `BuildScenarioDefinitionKey`, `BuildExecutionKey`, `BuildCooldownKey`, `BuildActiveKey`, `BuildLockResource`)
  - T16: Fixed lifecycle event names to use Pattern C (`storyline.scenario-definition.{action}`) without `Storyline` prefix
  - T16: Renamed topic `storyline.composed` → `storyline.plan.composed` (Pattern C multi-entity naming); event model renamed `StorylineComposedEvent` → `StorylinePlanComposedEvent`
