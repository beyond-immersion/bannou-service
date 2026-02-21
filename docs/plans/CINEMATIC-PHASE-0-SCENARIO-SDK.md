# Phase 0: storyline-scenario SDK Extraction

> **Status**: Draft
> **Parent Plan**: [CINEMATIC-SYSTEM.md](CINEMATIC-SYSTEM.md)
> **Prerequisites**: None (first phase)
> **Estimated Scope**: New SDK + internal refactor of lib-storyline

---

## Goal

Extract lib-storyline's scenario format from serialized JSON blobs into a typed SDK. This is a **design phase** -- the typed condition model, phase model, mutation model, and quest hook model defined here become the pattern template for cinematic-composer (Phase 1). The SDK must be usable by client-side authoring tools without importing plugin dependencies.

---

## What This Phase Is NOT

- **Not a feature change**: lib-storyline's API surface is unchanged. All existing endpoints continue to work identically.
- **Not a fit scorer**: The SDK provides mechanical condition evaluation (met / not met), not semantic scoring. The current `evaluate-fit` endpoint's personality-weighted scoring logic either moves to the actor behavior layer or is simplified to condition-match counting.
- **Not the storyline recomposition**: The storyteller SDK's output format (`StorylinePlan`) is unchanged in this phase. That's future work (Phase 6 in the master plan).

---

## Current State

lib-storyline stores scenario definitions in `ScenarioDefinitionModel` (MySQL-backed) with these JSON blob fields:

| Field | Current Type | Contains |
|-------|-------------|----------|
| `TriggerConditionsJson` | `string` (serialized JSON) | Condition expressions -- trait checks, backstory flags, relationship state, location, world-state |
| `PhasesJson` | `string` (serialized JSON) | Execution phases -- target states, completion criteria |
| `MutationsJson` | `string?` (serialized JSON) | Character mutations -- personality axis changes, backstory additions, relationship changes |
| `QuestHooksJson` | `string?` (serialized JSON) | Quest spawning rules -- quest template refs, trigger conditions |
| `ExclusivityTagsJson` | `string?` (serialized JSON) | Mutual exclusion tags |
| `TagsJson` | `string?` (serialized JSON) | Classification tags |

There is no typed data model for these -- they're opaque JSON parsed at runtime. The condition evaluation logic (`FindAvailableAsync`, `EvaluateFitAsync`, `TestScenarioAsync`) lives in the service implementation.

---

## Deliverables

### 1. `sdks/storyline-scenario/` SDK

```
sdks/storyline-scenario/
|-- storyline-scenario.csproj
|
|-- Definitions/
|   |-- ScenarioDefinition.cs        # Root: code, name, conditions, phases, mutations, hooks, metadata
|   |-- TriggerCondition.cs          # Typed condition model (see "Condition Type System" below)
|   |-- ScenarioPhase.cs             # Phase: target state, completion criteria, timeout
|   |-- ScenarioMutation.cs          # Mutation: axis, delta, operation, target
|   |-- QuestHook.cs                 # Quest spawning: template code, trigger event, parameters
|   |-- ScenarioMetadata.cs          # Priority, cooldown, exclusivity tags, classification tags
|   `-- ConditionOperator.cs         # Enum: equals, greater_than, less_than, contains, exists, etc.
|
|-- Evaluation/
|   |-- ConditionEvaluator.cs        # Evaluates conditions against context snapshot
|   |-- ConditionResult.cs           # Per-condition: met/not-met, actual value, expected value
|   |-- EvaluationResult.cs          # Aggregate: all condition results, count met/total
|   `-- CharacterStateSnapshot.cs    # Portable character state for evaluation (no service deps)
|
|-- Validation/
|   `-- ScenarioValidator.cs         # Structural: phases ordered, conditions complete, no orphan refs
|
`-- Serialization/
    `-- ScenarioSerializer.cs        # YAML/JSON round-trip for authoring tools
```

**Dependencies**: None. Pure data model + evaluation logic. No NuGet packages beyond System.Text.Json.

### 2. Updated lib-storyline

- `StorylineServiceModels.cs`: `ScenarioDefinitionModel` storage fields remain as `string` (JSON) for MySQL compatibility, but deserialization targets the SDK's typed models
- `StorylineService.cs`: `FindAvailableAsync` delegates condition evaluation to SDK's `ConditionEvaluator`
- `StorylineService.cs`: `TestScenarioAsync` delegates to SDK's `ConditionEvaluator`
- `StorylineService.cs`: `EvaluateFitAsync` simplified to return `EvaluationResult` (conditions met/not-met with counts), removing personality-weighted scoring
- lib-storyline.csproj: Add project reference to `sdks/storyline-scenario/`

### 3. Updated lib-storyline.tests

- Test project adds reference to `sdks/storyline-scenario/`
- Existing tests pass unchanged (behavioral equivalence)
- New tests for SDK types directly (condition evaluation, validation, serialization round-trip)

---

## The Condition Type System

This is the most important design decision in Phase 0. The typed conditions defined here will be mirrored (with domain-appropriate types) in cinematic-composer.

### Condition Types

| Type | What It Checks | Parameters | Example |
|------|----------------|------------|---------|
| `trait` | Character personality trait value | `axis` (string), `operator`, `value` (float) | "openness > 0.6" |
| `backstory` | Character backstory element presence | `element_code` (string), `present` (bool) | "has backstory element 'orphan'" |
| `relationship` | Relationship state between entities | `target_type`, `relationship_type`, `operator`, `value` | "has 'rival' relationship with target" |
| `location` | Character's current location | `location_code` (string) or `location_id` (Guid) | "character is in 'tavern_district'" |
| `world_state` | Realm/world-state value | `key` (string), `operator`, `value` | "current_season == 'winter'" |
| `tag` | Character has tag | `tag` (string), `present` (bool) | "character has tag 'MEGA_STRENGTH'" |
| `stat` | Character stat value | `stat_code` (string), `operator`, `value` (float) | "strength >= 50" |

### CharacterStateSnapshot

A portable, service-independent snapshot of everything the condition evaluator needs:

```csharp
public sealed class CharacterStateSnapshot
{
    public required Guid CharacterId { get; init; }
    public required Guid RealmId { get; init; }

    /// <summary>Personality traits: axis code -> value (-1.0 to 1.0).</summary>
    public IReadOnlyDictionary<string, float> Traits { get; init; } = new Dictionary<string, float>();

    /// <summary>Backstory element codes present on this character.</summary>
    public IReadOnlySet<string> BackstoryElements { get; init; } = new HashSet<string>();

    /// <summary>Relationship types: "target_id:relationship_type" -> strength.</summary>
    public IReadOnlyDictionary<string, float> Relationships { get; init; } = new Dictionary<string, float>();

    /// <summary>Current location code.</summary>
    public string? LocationCode { get; init; }

    /// <summary>Current location ID.</summary>
    public Guid? LocationId { get; init; }

    /// <summary>Tags on this character.</summary>
    public IReadOnlySet<string> Tags { get; init; } = new HashSet<string>();

    /// <summary>Stat values: stat code -> value.</summary>
    public IReadOnlyDictionary<string, float> Stats { get; init; } = new Dictionary<string, float>();

    /// <summary>World state values: key -> value (opaque strings, evaluator does type coercion).</summary>
    public IReadOnlyDictionary<string, string> WorldState { get; init; } = new Dictionary<string, string>();
}
```

The plugin is responsible for **assembling** this snapshot from L4 service calls (personality, character, location, etc.). The SDK evaluates against it without any service dependencies.

### ConditionEvaluator

```csharp
public static class ConditionEvaluator
{
    /// <summary>
    /// Evaluates all trigger conditions against the provided character state.
    /// Returns per-condition results with actual values for diagnostics.
    /// </summary>
    public static EvaluationResult Evaluate(
        IReadOnlyList<TriggerCondition> conditions,
        CharacterStateSnapshot snapshot);
}

public sealed class EvaluationResult
{
    /// <summary>Per-condition results.</summary>
    public required IReadOnlyList<ConditionResult> Results { get; init; }

    /// <summary>How many conditions were satisfied.</summary>
    public int MetCount => Results.Count(r => r.Met);

    /// <summary>Total conditions evaluated.</summary>
    public int TotalCount => Results.Count;

    /// <summary>True if ALL conditions are met.</summary>
    public bool AllMet => Results.All(r => r.Met);
}
```

---

## Implementation Steps

### Step 1: Create SDK Project

1. Create `sdks/storyline-scenario/storyline-scenario.csproj` targeting the same framework as other SDKs
2. Set namespace to `BeyondImmersion.Bannou.StorylineScenario`
3. No external dependencies

### Step 2: Define Typed Models

1. Define `TriggerCondition` with discriminated condition types (trait, backstory, relationship, location, world_state, tag, stat)
2. Define `ScenarioPhase` with typed fields replacing the JSON blob
3. Define `ScenarioMutation` with typed mutation operations
4. Define `QuestHook` with typed quest template references
5. Define `ScenarioMetadata` for priority, cooldown, exclusivity tags, classification tags
6. Define `ScenarioDefinition` as the root type aggregating all of the above
7. Define `ConditionOperator` enum

**Critical**: The JSON structure currently stored in MySQL must be deserializable into these typed models. Examine the actual JSON being stored (or the service code that constructs it) to ensure the typed models are compatible.

### Step 3: Define Evaluation Types

1. Define `CharacterStateSnapshot` (service-independent character state)
2. Define `ConditionResult` (per-condition met/not-met with actual/expected)
3. Define `EvaluationResult` (aggregate with counts)
4. Implement `ConditionEvaluator` (evaluate conditions against snapshot)

### Step 4: Define Validation and Serialization

1. Implement `ScenarioValidator` (phases ordered, conditions structurally valid, no orphan references)
2. Implement `ScenarioSerializer` (YAML and JSON round-trip using System.Text.Json + a YAML library if needed, or JSON-only for now)

### Step 5: Update lib-storyline

1. Add project reference from `lib-storyline.csproj` to `storyline-scenario.csproj`
2. In `StorylineService.cs`, replace inline condition evaluation with calls to `ConditionEvaluator.Evaluate()`
3. In `StorylineServiceModels.cs`, add deserialization helpers that convert the JSON blob fields into SDK typed models
4. Simplify `EvaluateFitAsync` to return `EvaluationResult` (condition match counts) instead of personality-weighted scores
5. Update `TestScenarioAsync` to delegate to the SDK

### Step 6: Update Tests

1. Add project reference from `lib-storyline.tests.csproj` to `storyline-scenario.csproj`
2. Verify all existing tests pass (behavioral equivalence for condition matching)
3. Add new unit tests for SDK types directly:
   - `ConditionEvaluator` tests for each condition type
   - `ScenarioValidator` tests for valid and invalid scenarios
   - `ScenarioSerializer` round-trip tests
   - Edge cases: empty conditions (always matches), null snapshot fields, type coercion

### Step 7: Build Verification

1. `dotnet build plugins/lib-storyline/lib-storyline.csproj --no-restore` succeeds
2. `dotnet test plugins/lib-storyline.tests/lib-storyline.tests.csproj --no-restore` passes
3. Full `dotnet build` succeeds (no other projects broken)

---

## Acceptance Criteria

1. `sdks/storyline-scenario/` exists with typed models for all scenario components
2. `ConditionEvaluator` correctly evaluates all condition types against `CharacterStateSnapshot`
3. lib-storyline references the SDK and delegates condition evaluation to it
4. All existing lib-storyline tests pass unchanged
5. New SDK-level tests cover condition evaluation, validation, and serialization
6. The SDK has zero service dependencies (no NuGet packages beyond System.Text.Json)
7. `dotnet build` succeeds for the full solution

---

## Risks

| Risk | Mitigation |
|------|------------|
| JSON blob format doesn't match typed models | Read actual JSON from service code before defining types. The service code that *writes* the JSON defines the implicit schema. |
| `evaluate-fit` simplification breaks callers | No callers exist -- endpoints are unused. But verify via grep that no other service calls storyline evaluate-fit. |
| SDK introduces circular dependency | SDK has zero dependencies. lib-storyline depends on SDK. No reverse path possible. |
| Existing condition evaluation has edge cases | Preserve the current logic exactly in the SDK's `ConditionEvaluator`, including any quirks. Behavioral equivalence is the goal, not refactoring. |

---

## What Phase 0 Enables

- **Phase 1**: cinematic-composer follows the same typed model pattern (CinematicScenario mirrors ScenarioDefinition, EncounterContext mirrors CharacterStateSnapshot)
- **Client tooling**: Authoring tools can reference `storyline-scenario` to define and validate scenarios without importing the plugin
- **Future storyline recomposition**: The typed `ScenarioDefinition` becomes the target output format for the storyline-storyteller SDK

---

*This is the Phase 0 implementation plan. For architectural context, see [CINEMATIC-SYSTEM.md](CINEMATIC-SYSTEM.md).*
