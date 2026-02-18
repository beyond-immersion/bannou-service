# storyline-composer SDK Design

> **Status**: Design Complete
> **Parent Plan**: [CINEMATIC-PHASE-0-SCENARIO-SDK.md](CINEMATIC-PHASE-0-SCENARIO-SDK.md) (supersedes)
> **SDK**: `sdks/storyline-composer/`
> **Namespace**: `BeyondImmersion.Bannou.StorylineComposer`
> **Dependencies**: `sdks/core/` (BannouJson, DiscriminatedRecordConverter)

---

## Purpose

The storyline-composer SDK provides typed models, condition evaluation, validation, and serialization for hand-authored scenario definitions. It is the **human-authored counterpart** to the storyline-storyteller SDK (procedural generation). Both produce output consumed by the Storyline plugin.

**Design principles** (from CINEMATIC-SYSTEM.md):
- **Plugins are passive registries.** The SDK defines data shapes; the plugin stores them. Neither ranks, recommends, or executes.
- **God-actors provide judgment.** Phase transitions, quest spawning decisions, and mutation timing are god-actor ABML behavior decisions, not SDK or plugin logic.
- **Format agnosticism.** Hand-authored and procedurally generated scenarios produce the same document format.

**Naming note**: `StorylineComposer` exists as a class in `storyline-storyteller/Composition/StorylineComposer.cs` (the GOAP planning entry point). No namespace collision -- that class is `BeyondImmersion.Bannou.StorylineStoryteller.Composition.StorylineComposer`; this SDK is `BeyondImmersion.Bannou.StorylineComposer`. The storyteller class should eventually be renamed to `StorylinePlanComposer` or `NarrativeComposer` since the SDK owns the name now.

---

## Directory Structure

```
sdks/storyline-composer/
├── storyline-composer.csproj
│
├── Definitions/
│   ├── ScenarioDefinition.cs         # Root document type
│   ├── ScenarioPhase.cs              # Phase metadata (not executable)
│   ├── ScenarioMetadata.cs           # Priority, cooldown, exclusivity, tags
│   └── PhaseQuestHook.cs             # Per-phase advisory quest suggestion
│
├── Conditions/
│   ├── TriggerCondition.cs           # Abstract base + all discriminated subtypes + converter
│   └── ConditionOperator.cs          # Comparison operator enum
│
├── Mutations/
│   ├── ScenarioMutation.cs           # Abstract base + all discriminated subtypes + converter
│   └── RelationshipOperation.cs      # Create/Strengthen/Weaken/End enum
│
├── Evaluation/
│   ├── EvaluationContext.cs          # Dictionary-based O(1) lookup context
│   ├── ConditionEvaluator.cs         # Static evaluator: conditions x context -> results
│   ├── ConditionResult.cs            # Per-condition: met/not-met, actual, expected
│   └── EvaluationResult.cs           # Aggregate: all results, counts, allMet
│
├── Validation/
│   ├── ScenarioValidator.cs          # Structural validation
│   ├── ValidationResult.cs           # Result with issues list
│   └── IScenarioValidationRule.cs    # Extensible rule interface
│
└── Serialization/
    └── ScenarioSerializer.cs         # JSON round-trip (YAML deferred to engine SDKs)

sdks/storyline-composer.tests/
├── storyline-composer.tests.csproj
├── Conditions/
│   └── ConditionEvaluatorTests.cs
├── Validation/
│   └── ScenarioValidatorTests.cs
└── Serialization/
    └── ScenarioSerializerTests.cs  # Round-trip tests covering both conditions and mutations
```

---

## Project File

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageId>BeyondImmersion.Bannou.StorylineComposer</PackageId>
    <RootNamespace>BeyondImmersion.Bannou.StorylineComposer</RootNamespace>
    <AssemblyName>BeyondImmersion.Bannou.StorylineComposer</AssemblyName>
    <TargetFrameworks>net8.0;net9.0</TargetFrameworks>
    <VersionPrefix>0.1.0</VersionPrefix>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../core/BeyondImmersion.Bannou.Core.csproj" />
  </ItemGroup>
</Project>
```

**Dependency**: `sdks/core/` only (BannouJson, `DiscriminatedRecordConverter<T>`). Core itself has zero external dependencies -- System.Text.Json is inbox. This follows the same pattern as `client`, `server`, and `bundle-format`, which all reference core.

---

## Type Definitions

### ScenarioDefinition (Root Document)

```csharp
namespace BeyondImmersion.Bannou.StorylineComposer.Definitions;

/// <summary>
/// A hand-authored scenario definition -- a narrative recipe card that god-actors
/// read to orchestrate gameplay experiences. Not executable; the god-actor's ABML
/// behavior decides how and when to cook the recipe.
/// </summary>
public sealed class ScenarioDefinition
{
    /// <summary>Unique code for this scenario (uppercase, underscores). Machine identifier.</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable scenario name.</summary>
    public required string Name { get; init; }

    /// <summary>Narrative description of what this scenario is about.</summary>
    public string? Description { get; init; }

    /// <summary>Conditions that must be met for this scenario to be available.</summary>
    public required IReadOnlyList<TriggerCondition> TriggerConditions { get; init; }

    /// <summary>Ordered narrative phases. Metadata only -- not executable.</summary>
    public required IReadOnlyList<ScenarioPhase> Phases { get; init; }

    /// <summary>Priority, cooldown, exclusivity, scoping.</summary>
    public ScenarioMetadata Metadata { get; init; } = ScenarioMetadata.Default;
}
```

### ScenarioPhase (Enriched Metadata, Not Executable)

Phases are organizational markers with enough metadata for a god-actor to read and interpret. They are **not** completion-evaluated, timer-driven, or branch-conditional.

```csharp
/// <summary>
/// A narrative phase -- a labeled beat in the scenario's arc. The god-actor decides
/// when a phase is "done" via its own GOAP evaluation. The scenario just describes
/// what the phase IS, not when or how to transition.
/// </summary>
public sealed class ScenarioPhase
{
    /// <summary>Sequential phase number (1-based).</summary>
    public required int PhaseNumber { get; init; }

    /// <summary>Human-readable phase name (e.g., "Discovery", "Confrontation").</summary>
    public required string Name { get; init; }

    /// <summary>Narrative description of this beat.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// Machine-readable code for ABML expression matching.
    /// God-actors check ${storyline.current_phase} against this.
    /// </summary>
    public string? PhaseCode { get; init; }

    /// <summary>
    /// What the world should look like when this phase is "narratively complete."
    /// Descriptive metadata the god-actor reads, not a completion condition.
    /// Example: "The character has learned about the betrayal."
    /// </summary>
    public string? NarrativeTargetState { get; init; }

    /// <summary>
    /// Character mutations this phase should produce when the god-actor decides
    /// the phase is done. Advisory -- the god-actor may skip or add mutations.
    /// </summary>
    public IReadOnlyList<ScenarioMutation> SuggestedMutations { get; init; } = [];

    /// <summary>
    /// Quest hooks this phase warrants. Advisory -- the god-actor picks the subset
    /// that fits the current narrative via its GOAP evaluation.
    /// </summary>
    public IReadOnlyList<PhaseQuestHook> SuggestedQuestHooks { get; init; } = [];

    /// <summary>Classification tags for this phase.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}
```

**What phases do NOT contain** (by design):
- Completion criteria (god-actor evaluates)
- Timer-based transitions (god-actor decides pacing)
- Conditional branching logic (god-actor's GOAP handles this)
- Expected duration (game-time pacing is the god-actor's aesthetic preference)

### PhaseQuestHook (Per-Phase Advisory)

```csharp
/// <summary>
/// A quest suggestion attached to a specific phase. The god-actor reads these
/// when deciding which quests to spawn. Not automatically triggered.
/// </summary>
public sealed class PhaseQuestHook
{
    /// <summary>Quest definition code to potentially spawn.</summary>
    public required string QuestCode { get; init; }

    /// <summary>Optional term overrides for the quest contract template.</summary>
    public IReadOnlyDictionary<string, string>? TermOverrides { get; init; }

    /// <summary>Why this quest fits this phase (for authoring tools and god-actor context).</summary>
    public string? Description { get; init; }
}
```

No `delaySeconds` -- timing is the god-actor's decision. No conditional expressions -- condition evaluation is the god-actor's GOAP job. No group semantics -- the god-actor picks the subset that fits.

### ScenarioMetadata

```csharp
/// <summary>
/// Operational metadata for scenario discovery and filtering.
/// </summary>
public sealed class ScenarioMetadata
{
    /// <summary>Higher priority scenarios are preferred when multiple match.</summary>
    public int Priority { get; init; }

    /// <summary>Minimum seconds between triggering this scenario for the same character.</summary>
    public int? CooldownSeconds { get; init; }

    /// <summary>
    /// Mutual exclusion tags. A character can only have one active scenario
    /// per exclusivity tag.
    /// </summary>
    public IReadOnlyList<string> ExclusivityTags { get; init; } = [];

    /// <summary>Classification tags for filtering (e.g., "combat", "social", "mystery").</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Scope to a specific realm (null = global).</summary>
    public Guid? RealmId { get; init; }

    /// <summary>Scope to a specific game service (null = global).</summary>
    public Guid? GameServiceId { get; init; }

    /// <summary>
    /// How likely this scenario is to produce a character death if fully played out.
    /// 0.0 = impossible (social, mystery, crafting scenarios).
    /// 0.5 = possible under specific conditions (combat with escape routes).
    /// 1.0 = guaranteed death if the lethal branch resolves (sacrifice, execution, boss kill).
    /// God-actors read this alongside ${status.plot_armor} to filter scenario selection.
    /// Characters with plot armor > 0 cannot die -- gods will not select high-lethality
    /// scenarios for protected characters, or will intervene before lethal resolution.
    /// See docs/planning/DEATH-AND-PLOT-ARMOR.md for the full mechanic design.
    /// </summary>
    public float Lethality { get; init; }

    /// <summary>
    /// Which phase codes contain potential lethal outcomes. Empty if lethality is 0.
    /// God-actors use this to know which phase transitions to monitor for death risk.
    /// Phase codes reference <see cref="ScenarioPhase.PhaseCode"/> values.
    /// </summary>
    public IReadOnlyList<string> LethalPhases { get; init; } = [];

    /// <summary>Default metadata instance.</summary>
    public static ScenarioMetadata Default { get; } = new();
}
```

---

## Condition Type System (Discriminated Records)

### Base Type and Subtypes

Uses the `abstract record` + `sealed record` pattern (structurally similar to `behavior-compiler/Documents/Actions/ActionNode.cs`). JSON round-trip is handled by subclassing `DiscriminatedRecordConverter<T>` from `sdks/core/` -- a generic base converter that peeks the discriminator property, maps it to the concrete type, and handles recursion-safe (de)serialization. Each converter subclass is a one-liner that passes its type map to the base constructor.

```csharp
namespace BeyondImmersion.Bannou.StorylineComposer.Conditions;

/// <summary>
/// A trigger condition that must be met for a scenario to be available.
/// Discriminated by <see cref="Type"/> -- each subtype has exactly the fields it needs.
/// </summary>
[JsonConverter(typeof(TriggerConditionConverter))]
public abstract record TriggerCondition(string Type);

/// <summary>Character personality trait within a range.</summary>
public sealed record TraitRangeCondition(
    string Axis,
    float? Min,
    float? Max
) : TriggerCondition("trait_range");

/// <summary>Character has a specific backstory element.</summary>
public sealed record BackstoryCondition(
    string ElementType,
    string? Key
) : TriggerCondition("backstory");

/// <summary>Character has (or lacks) a specific relationship type.</summary>
public sealed record RelationshipCondition(
    string TypeCode,
    bool MustExist,
    string? EntityType
) : TriggerCondition("relationship");

/// <summary>Character age within a range.</summary>
public sealed record AgeRangeCondition(
    int? Min,
    int? Max
) : TriggerCondition("age_range");

/// <summary>Character is at a specific location.</summary>
public sealed record LocationCondition(
    Guid? LocationId,
    string? LocationCode
) : TriggerCondition("location");

/// <summary>Current game time of day within a range.</summary>
public sealed record TimeOfDayCondition(
    int? Min,
    int? Max
) : TriggerCondition("time_of_day");

/// <summary>World state key matches a value.</summary>
public sealed record WorldStateCondition(
    string Key,
    ConditionOperator Operator,
    string? Value
) : TriggerCondition("world_state");

/// <summary>Character stat meets a threshold.</summary>
public sealed record StatCondition(
    string StatCode,
    ConditionOperator Operator,
    float Value
) : TriggerCondition("stat");

/// <summary>Character has (or lacks) a specific tag.</summary>
public sealed record TagCondition(
    string Tag,
    bool MustExist
) : TriggerCondition("tag");

/// <summary>
/// Custom condition evaluated by the caller (god-actor), not the SDK.
/// Always evaluates to true in the SDK's ConditionEvaluator.
/// </summary>
public sealed record CustomCondition(
    string Domain,
    string Code,
    IReadOnlyDictionary<string, object>? Parameters
) : TriggerCondition("custom");
```

### ConditionOperator

```csharp
public enum ConditionOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Contains
}
```

### TriggerConditionConverter (Polymorphic JSON)

Subclasses `DiscriminatedRecordConverter<TriggerCondition>` from `sdks/core/`. The entire implementation is the type map:

```csharp
public class TriggerConditionConverter() : DiscriminatedRecordConverter<TriggerCondition>("type",
    new Dictionary<string, Type>
    {
        ["trait_range"] = typeof(TraitRangeCondition),
        ["backstory"] = typeof(BackstoryCondition),
        ["relationship"] = typeof(RelationshipCondition),
        ["age_range"] = typeof(AgeRangeCondition),
        ["location"] = typeof(LocationCondition),
        ["time_of_day"] = typeof(TimeOfDayCondition),
        ["world_state"] = typeof(WorldStateCondition),
        ["stat"] = typeof(StatCondition),
        ["tag"] = typeof(TagCondition),
        ["custom"] = typeof(CustomCondition),
    });
```

The base converter handles: peek discriminator (case-insensitive property matching), map to concrete type, recursion-safe deserialization, serialization of concrete runtime type. Discriminator values are matched exactly (case-sensitive) since they are machine identifiers.

---

## Mutation Type System (Discriminated Records)

### Base Type and Subtypes

```csharp
namespace BeyondImmersion.Bannou.StorylineComposer.Mutations;

/// <summary>
/// A character mutation that a scenario phase suggests applying.
/// The plugin maps each subtype to the appropriate L4 service call.
/// The SDK validates structural integrity only.
/// </summary>
[JsonConverter(typeof(ScenarioMutationConverter))]
public abstract record ScenarioMutation(string Type);

/// <summary>
/// Record a personality-affecting experience. Declarative, not prescriptive:
/// describes WHAT happened (betrayal at 0.7 intensity), not HOW to mutate
/// (shift trust axis by -0.3). The character-personality service internally
/// maps experience types to trait axis impacts based on its own rules.
/// Matches the RecordExperienceRequest API shape.
/// </summary>
public sealed record PersonalityMutation(
    string ExperienceType,
    float Intensity
) : ScenarioMutation("personality");

/// <summary>Add a backstory element to the character.</summary>
public sealed record BackstoryMutation(
    string ElementType,
    string Key,
    string Value,
    float Strength
) : ScenarioMutation("backstory");

/// <summary>
/// Create, strengthen, weaken, or end a relationship.
/// TargetRole is a participant binding key from TriggerScenarioRequest.additionalParticipants,
/// not a relationship role -- it identifies WHICH participant in the scenario is the
/// relationship target (e.g., "betrayer", "rescuer").
/// </summary>
public sealed record RelationshipMutation(
    RelationshipOperation Operation,
    string TypeCode,
    string TargetRole
) : ScenarioMutation("relationship");

/// <summary>Record a memorable encounter.</summary>
public sealed record EncounterMutation(
    string EncounterType,
    float Sentiment,
    float Significance
) : ScenarioMutation("encounter");

/// <summary>Domain-specific mutation handled by the caller.</summary>
public sealed record CustomMutation(
    string Domain,
    string Operation,
    IReadOnlyDictionary<string, object>? Parameters
) : ScenarioMutation("custom");
```

### RelationshipOperation

```csharp
public enum RelationshipOperation
{
    Create,
    Strengthen,
    Weaken,
    End
}
```

### ScenarioMutationConverter

Same pattern -- subclasses `DiscriminatedRecordConverter<ScenarioMutation>` with its own type map:

```csharp
public class ScenarioMutationConverter() : DiscriminatedRecordConverter<ScenarioMutation>("type",
    new Dictionary<string, Type>
    {
        ["personality"] = typeof(PersonalityMutation),
        ["backstory"] = typeof(BackstoryMutation),
        ["relationship"] = typeof(RelationshipMutation),
        ["encounter"] = typeof(EncounterMutation),
        ["custom"] = typeof(CustomMutation),
    });
```

---

## Evaluation

### EvaluationContext (Dictionary-Based, O(1) Lookups)

```csharp
namespace BeyondImmersion.Bannou.StorylineComposer.Evaluation;

/// <summary>
/// Everything the condition evaluator needs to check conditions against.
/// Assembled by the plugin from flat API request data or variable provider values.
/// Optimized for O(1) lookups during bulk scenario evaluation.
/// </summary>
public sealed class EvaluationContext
{
    /// <summary>Character being evaluated.</summary>
    public required Guid CharacterId { get; init; }

    /// <summary>Realm context.</summary>
    public required Guid RealmId { get; init; }

    /// <summary>Personality traits: axis code -> value (-1.0 to 1.0).</summary>
    public IReadOnlyDictionary<string, float> Traits { get; init; }
        = new Dictionary<string, float>();

    /// <summary>
    /// Backstory elements present: element type -> set of keys.
    /// Lookup: backstoryElements.TryGetValue("origin", out var keys) &amp;&amp; keys.Contains("orphan").
    /// Supports both exact key matching and type-existence checks.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> BackstoryElements { get; init; }
        = new Dictionary<string, IReadOnlySet<string>>();

    /// <summary>
    /// Relationships keyed by type code.
    /// Lookup: relationships.ContainsKey("rival").
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<RelationshipEntry>> Relationships { get; init; }
        = new Dictionary<string, IReadOnlyList<RelationshipEntry>>();

    /// <summary>Character age, if known.</summary>
    public int? Age { get; init; }

    /// <summary>Current location ID.</summary>
    public Guid? LocationId { get; init; }

    /// <summary>Current location code.</summary>
    public string? LocationCode { get; init; }

    /// <summary>Current game time of day (0-23 or domain-specific range).</summary>
    public int? TimeOfDay { get; init; }

    /// <summary>Character stat values: stat code -> value.</summary>
    public IReadOnlyDictionary<string, float> Stats { get; init; }
        = new Dictionary<string, float>();

    /// <summary>World state key-value pairs.</summary>
    public IReadOnlyDictionary<string, string> WorldState { get; init; }
        = new Dictionary<string, string>();

    /// <summary>Character tags.</summary>
    public IReadOnlySet<string> Tags { get; init; }
        = new HashSet<string>();
}

/// <summary>
/// A relationship entry for context lookups.
/// </summary>
public sealed record RelationshipEntry(Guid EntityId, string EntityType);
```

### ConditionEvaluator

```csharp
/// <summary>
/// Evaluates trigger conditions against an evaluation context.
/// Pure computation, no service dependencies.
/// </summary>
public static class ConditionEvaluator
{
    /// <summary>
    /// Evaluates all conditions and returns aggregate results.
    /// </summary>
    public static EvaluationResult Evaluate(
        IReadOnlyList<TriggerCondition> conditions,
        EvaluationContext context);

    /// <summary>
    /// Evaluates a single condition with detailed diagnostics.
    /// </summary>
    public static ConditionResult EvaluateSingle(
        TriggerCondition condition,
        EvaluationContext context);
}
```

The implementation uses pattern matching on the discriminated condition types -- no `string.IsNullOrEmpty` guards on nullable fields because each subtype has exactly the required fields:

```csharp
// Before (flat model, null guards everywhere):
case TriggerConditionType.TraitRange:
    if (string.IsNullOrEmpty(condition.TraitAxis)) return (false, ...);
    var trait = characterState.Traits?.FirstOrDefault(t => ...);  // O(n)

// After (discriminated records, type-safe):
case TraitRangeCondition c:
    context.Traits.TryGetValue(c.Axis, out var value);  // O(1), Axis is required
```

### ConditionResult / EvaluationResult

```csharp
/// <summary>Per-condition evaluation result with diagnostics.</summary>
public sealed class ConditionResult
{
    public required TriggerCondition Condition { get; init; }
    public required bool Met { get; init; }
    public string? ActualValue { get; init; }
    public string? ExpectedValue { get; init; }
    public string? Details { get; init; }
}

/// <summary>Aggregate evaluation result.</summary>
public sealed class EvaluationResult
{
    public required IReadOnlyList<ConditionResult> Results { get; init; }

    // NOTE: These are computed from LINQ on every access. For bulk scenario evaluation
    // (FindAvailableAsync iterates hundreds of definitions), consider caching these values
    // at construction time instead. Same concern applies to any filtered subset operations
    // on the Results list (e.g., grouping by condition type).
    public int MetCount => Results.Count(r => r.Met);
    public int TotalCount => Results.Count;
    public bool AllMet => Results.All(r => r.Met);
}
```

---

## Validation

Following scene-composer's extensible rule pattern:

```csharp
namespace BeyondImmersion.Bannou.StorylineComposer.Validation;

/// <summary>
/// Validates structural integrity of scenario definitions.
/// </summary>
public sealed class ScenarioValidator
{
    public void AddRule(IScenarioValidationRule rule);
    public bool RemoveRule<T>() where T : IScenarioValidationRule;
    public ValidationResult Validate(ScenarioDefinition definition);
}

public interface IScenarioValidationRule
{
    IEnumerable<ValidationIssue> Validate(
        ScenarioDefinition definition,
        ScenarioValidationContext context);
}
```

### Built-in Rules

| Rule | Severity | Checks |
|------|----------|--------|
| `PhaseOrderRule` | Error | Phase numbers are sequential starting from 1, no duplicates |
| `ConditionCompleteRule` | Error | Required fields per condition subtype are present and valid (non-empty axis, valid operator, etc.) |
| `MutationCompleteRule` | Error | Required fields per mutation subtype are present and valid |
| `CodeFormatRule` | Warning | Code follows `UPPER_SNAKE_CASE` convention |
| `EmptyPhasesRule` | Error | At least one phase exists |
| `QuestHookCodeRule` | Warning | Quest hook codes are non-empty |
| `PhaseCodeUniqueRule` | Warning | Phase codes (when present) are unique within the definition |
| `EmptyConditionsRule` | Info | Scenario with zero conditions always matches (intentional?) |

---

## Serialization

```csharp
namespace BeyondImmersion.Bannou.StorylineComposer.Serialization;

/// <summary>
/// JSON round-trip for scenario definitions.
/// Delegates to BannouJson with the discriminated record converters
/// (TriggerConditionConverter, ScenarioMutationConverter) already registered
/// via [JsonConverter] attributes on the abstract base records.
/// </summary>
public static class ScenarioSerializer
{
    /// <summary>Serialize a scenario definition to JSON.</summary>
    public static string ToJson(ScenarioDefinition definition)
        => BannouJson.Serialize(definition);

    /// <summary>Deserialize a scenario definition from JSON.</summary>
    public static ScenarioDefinition FromJson(string json)
        => BannouJson.DeserializeRequired<ScenarioDefinition>(json);
}
```

No custom `JsonSerializerOptions` needed -- `BannouJson.Options` provides the standard configuration, and the `[JsonConverter]` attributes on `TriggerCondition` and `ScenarioMutation` direct STJ to the correct converters automatically. `ScenarioSerializer` is a thin convenience wrapper.

YAML support is deferred to engine-specific SDKs (`storyline-composer-godot`, etc.) that can bring a YAML library dependency.

---

## Schema Changes (storyline-api.yaml)

The scenario portion of `storyline-api.yaml` gets rewritten. SDK types are referenced via `x-sdk-type`:

### Types moved to SDK (excluded from NSwag generation)

| API Schema Type | SDK Type | x-sdk-type |
|----------------|----------|------------|
| `TriggerCondition` | `TriggerCondition` (abstract + subtypes) | `BeyondImmersion.Bannou.StorylineComposer.Conditions.TriggerCondition` |
| `TriggerConditionType` | Eliminated | Discriminator is `TriggerCondition.Type` string |
| `ScenarioPhase` | `ScenarioPhase` | `BeyondImmersion.Bannou.StorylineComposer.Definitions.ScenarioPhase` |
| `ScenarioMutation` | `ScenarioMutation` (abstract + subtypes) | `BeyondImmersion.Bannou.StorylineComposer.Mutations.ScenarioMutation` |
| `MutationType` | Eliminated | Discriminator is `ScenarioMutation.Type` string |
| `ScenarioQuestHook` | `PhaseQuestHook` | `BeyondImmersion.Bannou.StorylineComposer.Definitions.PhaseQuestHook` |
| `ConditionResult` | `ConditionResult` | `BeyondImmersion.Bannou.StorylineComposer.Evaluation.ConditionResult` |
| `ConditionOperator` | `ConditionOperator` | `BeyondImmersion.Bannou.StorylineComposer.Conditions.ConditionOperator` |
| `RelationshipOperation` | `RelationshipOperation` | `BeyondImmersion.Bannou.StorylineComposer.Mutations.RelationshipOperation` |

### Types that stay in the API schema

- All request/response wrappers (`CreateScenarioDefinitionRequest`, etc.)
- API `ScenarioDefinition` response model -- **must be renamed** (e.g., `StoredScenarioDefinition` or `ScenarioDefinitionResponse`) to avoid collision with the SDK's `ScenarioDefinition`. The API type wraps the SDK type and adds storage-layer fields (`scenarioId`, `enabled`, `deprecated`, `createdAt`, `updatedAt`, `etag`) that the SDK doesn't own. The SDK owns the name `ScenarioDefinition`.
- `ScenarioExecution`, `ScenarioStatus` (execution tracking)
- `ScenarioMatch` (discovery results)
- `ScenarioDefinitionSummary` (list responses)

### API Request Flattening

`FindAvailableScenariosRequest` is flattened. The deeply nested `CharacterStateSnapshot` with `TraitSnapshot[]`, `BackstorySnapshot[]`, `RelationshipSnapshot[]` is replaced:

```yaml
# Before (deeply nested)
FindAvailableScenariosRequest:
  characterState:
    traits: [{axis, value}, ...]
    backstoryElements: [{elementType, key}, ...]
    relationships: [{relationshipTypeCode, otherEntityId, otherEntityType}, ...]

# After (flat key-value, matches variable provider format)
FindAvailableScenariosRequest:
  characterId: Guid
  realmId: Guid?
  traits: Dict<string, float>?        # axis -> value
  backstoryElements: Dict<string, string[]>?  # elementType -> [keys]
  relationships: string[]?             # "typeCode" (existence check only)
  age: int?
  locationId: Guid?
  locationCode: string?
  timeOfDay: int?
  stats: Dict<string, float>?
  worldState: Dict<string, string>?
  tags: string[]?
  maxResults: int = 10
  excludeTags: string[]?
```

The plugin converts this flat request to the SDK's `EvaluationContext` with minimal transformation. God-actors pass whatever flat data they already have from variable providers.

The same flattening applies to `TestScenarioRequest`, `TriggerScenarioRequest`, and `EvaluateFitRequest`.

`CharacterStateSnapshot`, `TraitSnapshot`, `BackstorySnapshot`, `RelationshipSnapshot` are eliminated from the schema.

---

## Plugin Changes (lib-storyline)

### What Changes

1. **Add project reference** to `sdks/storyline-composer/storyline-composer.csproj`
2. **Replace inline condition evaluation** (`EvaluateConditions`, `EvaluateSingleCondition` -- lines 1882-2052) with `ConditionEvaluator.Evaluate()`
3. **Replace JSON blob deserialization** -- `BannouJson.Deserialize<List<TriggerCondition>>(definition.TriggerConditionsJson)` now deserializes to SDK discriminated types via `[JsonConverter]` attributes (no custom options needed)
4. **Assemble EvaluationContext** from flat API request data in `FindAvailableScenariosAsync`, `TestScenarioTriggerAsync`, `EvaluateScenarioFitAsync`, `TriggerScenarioAsync`
5. **Mutation application** stays in plugin -- maps SDK `ScenarioMutation` subtypes to L4 service calls via pattern matching
6. **Quest hook model** changes from scenario-level `ScenarioQuestHook[]` to per-phase `PhaseQuestHook[]` on `ScenarioPhase`
7. **Storage model** (`ScenarioDefinitionModel`) keeps JSON string fields for MySQL but `BannouJson.Deserialize` handles the SDK discriminated types automatically via `[JsonConverter]` attributes

### What Doesn't Change

- Composition endpoints (`/storyline/compose`, `/plan/get`, `/plan/list`) -- these use storyline-storyteller, not storyline-composer
- Event publishing patterns
- State store topology
- Service dependencies (lib-resource, lib-relationship)

---

## Condition Evaluation: Before and After

### Before (inline in StorylineService.cs)

```csharp
// O(n) lookup, null guards, flat God Object
case TriggerConditionType.TraitRange:
    if (string.IsNullOrEmpty(condition.TraitAxis))
        return (false, null, null, "Missing trait axis");
    var trait = characterState.Traits?.FirstOrDefault(t =>
        t.Axis.Equals(condition.TraitAxis, StringComparison.OrdinalIgnoreCase));
    if (trait is null)
        return (false, "not found", $"{condition.TraitMin}-{condition.TraitMax}", ...);
    var inRange = (!condition.TraitMin.HasValue || trait.Value >= condition.TraitMin.Value) &&
                  (!condition.TraitMax.HasValue || trait.Value <= condition.TraitMax.Value);
```

### After (SDK ConditionEvaluator)

```csharp
// O(1) lookup, no null guards, type-safe discriminated record
case TraitRangeCondition c:
    if (!context.Traits.TryGetValue(c.Axis, out var value))
        return new ConditionResult { Condition = c, Met = false,
            ActualValue = "not found", ExpectedValue = FormatRange(c.Min, c.Max) };
    var met = (c.Min is null || value >= c.Min) && (c.Max is null || value <= c.Max);
    return new ConditionResult { Condition = c, Met = met,
        ActualValue = value.ToString("F2"), ExpectedValue = FormatRange(c.Min, c.Max) };
```

---

## Scenario JSON Document Format

A complete scenario definition serialized by the SDK:

```json
{
  "code": "TAVERN_BETRAYAL",
  "name": "The Tavern Betrayal",
  "description": "A trusted ally reveals their true colors during a tavern gathering.",
  "triggerConditions": [
    { "type": "trait_range", "axis": "openness", "min": 0.4, "max": null },
    { "type": "relationship", "typeCode": "ally", "mustExist": true, "entityType": null },
    { "type": "location", "locationId": null, "locationCode": "tavern_district" },
    { "type": "world_state", "key": "current_season", "operator": "Equals", "value": "winter" }
  ],
  "phases": [
    {
      "phaseNumber": 1,
      "name": "Discovery",
      "phaseCode": "discovery",
      "narrativeTargetState": "The character has noticed suspicious behavior from their ally.",
      "suggestedMutations": [],
      "suggestedQuestHooks": [
        { "questCode": "INVESTIGATE_ALLY", "description": "Gather information about the ally's recent activities." }
      ],
      "tags": ["social", "investigation"]
    },
    {
      "phaseNumber": 2,
      "name": "Confrontation",
      "phaseCode": "confrontation",
      "narrativeTargetState": "The betrayal is revealed. The character must choose how to respond.",
      "suggestedMutations": [
        { "type": "personality", "experienceType": "BETRAYAL", "intensity": 0.7 },
        { "type": "relationship", "operation": "End", "typeCode": "ally", "targetRole": "betrayer" },
        { "type": "encounter", "encounterType": "betrayal", "sentiment": -0.8, "significance": 0.9 }
      ],
      "suggestedQuestHooks": [
        { "questCode": "CONFRONT_BETRAYER", "description": "Face the betrayer directly." },
        { "questCode": "SEEK_REVENGE", "description": "Plan retribution for the betrayal." }
      ],
      "tags": ["combat", "emotional"]
    },
    {
      "phaseNumber": 3,
      "name": "Resolution",
      "phaseCode": "resolution",
      "narrativeTargetState": "The character has processed the betrayal and its consequences.",
      "suggestedMutations": [
        { "type": "backstory", "elementType": "trauma", "key": "betrayed_by_ally", "value": "A trusted ally turned against them.", "strength": 0.7 }
      ],
      "suggestedQuestHooks": [],
      "tags": ["reflective"]
    }
  ],
  "metadata": {
    "priority": 50,
    "cooldownSeconds": 86400,
    "exclusivityTags": ["betrayal_arc"],
    "tags": ["social", "drama", "betrayal"],
    "realmId": null,
    "gameServiceId": null,
    "lethality": 0.5,
    "lethalPhases": ["confrontation"]
  }
}
```

---

## Future: Composer SDK Family

When music-composer and cinematic-composer arrive, shared patterns will be extracted based on proven overlap -- not prematurely abstracted.

| SDK | Domain | Conditions | Output | Plugin |
|-----|--------|------------|--------|--------|
| storyline-composer | Narrative scenarios | Personality, backstory, relationships, world state | `ScenarioDefinition` | lib-storyline |
| music-composer | Musical scenarios | Rhythm, harmony, tension, emotional state | `MusicalScenario` | lib-music |
| cinematic-composer | Cinematic encounters | Spatial, emotional, relationship, combat state | `CinematicScenario` | *(future)* |

**Already shared** (in `sdks/core/`):
- `DiscriminatedRecordConverter<T>` -- generic polymorphic JSON converter for discriminated record hierarchies

**Expected shared patterns** (extract when second consumer proves them):
- `EvaluationContext` + `ConditionEvaluator` architecture
- `ValidationResult` / `IValidationRule` interface
- `*Serializer` static class pattern (may be unnecessary if BannouJson suffices)

---

## Implementation Order

1. Create SDK project, add to both `bannou-sdks.sln` and the main service solution
2. Define discriminated condition records + JSON converter
3. Define discriminated mutation records + JSON converter
4. Define `ScenarioDefinition`, `ScenarioPhase`, `PhaseQuestHook`, `ScenarioMetadata`
5. Implement `EvaluationContext` and `ConditionEvaluator`
6. Implement `ScenarioValidator` with built-in rules
7. Implement `ScenarioSerializer`
8. Write SDK tests (conditions, mutations, evaluation, validation, serialization round-trip)
9. Rewrite scenario portion of `storyline-api.yaml` with `x-sdk-type` references
10. Regenerate storyline service code
11. Update `lib-storyline` to reference SDK, replace inline evaluation
12. Update `lib-storyline.tests`
13. Build verification: `dotnet build` + `dotnet test`

---

*This design supersedes [CINEMATIC-PHASE-0-SCENARIO-SDK.md](CINEMATIC-PHASE-0-SCENARIO-SDK.md). For architectural context, see [CINEMATIC-SYSTEM.md](CINEMATIC-SYSTEM.md) and [ORCHESTRATION-PATTERNS.md](../reference/ORCHESTRATION-PATTERNS.md).*
