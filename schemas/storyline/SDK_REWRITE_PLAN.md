# Storyline SDK Rewrite Plan

> **Status**: Planning Phase
> **Last Updated**: 2026-02-04
> **Scope**: Complete rewrite of storyline-theory and storyline-storyteller SDKs

This document outlines the comprehensive plan for rewriting the Storyline SDKs to implement the YAML specifications as loadable data with proper architectural alignment to SDK_FOUNDATIONS and STORYLINE_COMPOSER.

---

## Executive Summary

The Storyline SDK will follow the established **two-layer pattern** used by the Music SDK, with one key difference:

### Music vs Storyline Comparison

| Component | Music System | Storyline System |
|-----------|--------------|------------------|
| **Theory SDK** | `MusicTheory` | `StorylineTheory` |
| **Planner SDK** | `MusicStoryteller` | `StorylineStoryteller` |
| **Plugin** | `lib-music` | `lib-storyline` |
| **Data Source** | None (pure generation) | **Compressed archives from L4 services** |

**The Key Difference**: Storyline needs to aggregate data from multiple L4 services (character-personality, character-history, character-encounter, realm-history) before planning. The SDKs use **opaque string keys** to access archive entries, decoupling them from plugin types at compile time.

### Layer Responsibilities

| Layer | SDK | Role | Loads |
|-------|-----|------|-------|
| **Theory Layer** | `storyline-theory` | Primitives, data structures, YAML loading, **ArchiveExtractor** | `narrative-state.yaml`, `emotional-arcs.yaml` |
| **Composition Layer** | `storyline-storyteller` | GOAP planning, phase evaluation, story generation | `story-templates.yaml`, `story-actions.yaml` |

**Key Decisions**:
1. YAML files will be **directly loaded at runtime** as the canonical data source
2. Compress data models are **generated INTO the SDK** from plugin schemas (following existing patterns)
3. ArchiveExtractor uses **opaque string keys**, not compile-time plugin references

---

## Part 1: Architecture Overview

### 1.1 Two-Layer SDK Pattern

Following the music SDK precedent, with ArchiveExtractor for data aggregation:

```
┌─────────────────────────────────────────────────────────────────────┐
│                    STORYLINE-STORYTELLER                            │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────┐  │
│  │ TemplateEngine  │  │   GOAPPlanner   │  │  PhaseEvaluator     │  │
│  │ (loads story-   │  │ (plans action   │  │  (hybrid position/  │  │
│  │  templates.yaml)│  │  sequences)     │  │   state triggers)   │  │
│  └────────┬────────┘  └────────┬────────┘  └──────────┬──────────┘  │
│           │                    │                      │             │
│           └────────────────────┴──────────────────────┘             │
│                                │                                    │
│                    ┌───────────▼───────────┐                        │
│                    │    ActionRegistry     │                        │
│                    │ (loads story-actions  │                        │
│                    │  .yaml)               │                        │
│                    └───────────────────────┘                        │
├─────────────────────────────────────────────────────────────────────┤
│                      STORYLINE-THEORY                               │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────┐  │
│  │ NarrativeState  │  │  EmotionalArc   │  │   SpectrumTypes     │  │
│  │ (10 Life Value  │  │ (6 Reagan arcs  │  │   (genre mappings,  │  │
│  │  spectrums)     │  │  with math)     │  │    4-stage poles)   │  │
│  └────────┬────────┘  └────────┬────────┘  └──────────┬──────────┘  │
│           │                    │                      │             │
│           │      ┌─────────────┴─────────────┐                      │
│           │      │    ArchiveExtractor       │                      │
│           │      │ (opaque keys → WorldState)│                      │
│           │      └─────────────┬─────────────┘                      │
│           │                    │                                    │
│           └────────────────────┴───────────────────                 │
│                                │                                    │
│  ┌─────────────────────────────┴─────────────────────────────────┐  │
│  │                    Data Sources                               │  │
│  │  ┌──────────────────┐  ┌────────────────────────────────────┐ │  │
│  │  │   YAML Loaders   │  │   Generated/ (from plugin schemas) │ │  │
│  │  │  narrative-state │  │   PersonalityCompressData          │ │  │
│  │  │  emotional-arcs  │  │   HistoryCompressData              │ │  │
│  │  └──────────────────┘  │   EncounterCompressData            │ │  │
│  │                        │   RealmLoreCompressData            │ │  │
│  │                        └────────────────────────────────────┘ │  │
│  └───────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
```

### 1.2 Orchestration Flow

The plugin (lib-storyline) orchestrates data aggregation; the SDK interprets it:

```
Regional Watcher (Actor "god")
    │
    └── detects narrative opportunity (character death, treaty, etc.)
        │
        ▼
    POST /storyline/compose (with archive IDs)
        │
        ▼
┌───────────────────────────────────────────────────────┐
│               lib-storyline (L4 Plugin)               │
│                                                       │
│  1. Fetch archives from lib-resource                  │
│  2. Aggregate compressed entries into bundle          │
│  3. Pass bundle to SDK's ArchiveExtractor             │
│     └── SDK extracts what it needs, ignores unknowns  │
│     └── Returns WorldState                            │
│  4. Call SDK's StoryPlanner(WorldState)               │
│     └── Returns StorylinePlan                         │
│                                                       │
└───────────────────────────────────────────────────────┘
        │
        ▼
    Returns StorylinePlan to Watcher
        │
        ▼
    Watcher decides to call POST /storyline/instantiate
        │
        ▼
    lib-storyline spawns entities via other L4 plugins
```

### 1.3 Dependency Flow (Compile-Time)

```
lib-storyline (L4 plugin)
    │
    ├── references ──► storyline-storyteller SDK
    │                      │
    │                      └── references ──► storyline-theory SDK
    │                                              │
    │                                              └── Generated/CompressModels/
    │                                                  (copied from plugin schemas)
    │
    └── calls at runtime ──► lib-resource, lib-character-*, lib-realm-*
                             (no compile-time reference)
```

### 1.4 Design Principles

1. **YAML as Source of Truth**: The YAML files ARE the data, not documentation
2. **Opaque String Keys**: SDK uses string keys for archive entries, not compile-time plugin references
3. **Generated Models**: Compress data types are generated from plugin schemas into SDK's `/Generated/`
4. **Plugin Aggregates, SDK Interprets**: Plugin knows how to fetch data; SDK knows how to interpret it
5. **Lazy Evaluation**: Only generate current phase; re-evaluate world state before next
6. **Chained Atomic Actions**: Each action is atomic; chains connect via `chained_action`
7. **Genre-Agnostic Templates**: Arc shapes are universal; genre determines the spectrum
8. **Pure Computation**: No side effects; SDKs return plans, callers execute them

---

## Part 2: YAML File Mappings

### 2.1 narrative-state.yaml → storyline-theory

**Purpose**: Defines the 10 Life Value spectrums and their four-stage poles

**YAML Schema Additions Needed** (for action count modifiers):
```yaml
# In narrative-state.yaml - Genre-level pacing modifiers
genre_action_modifiers:
  thriller:
    multiplier: 0.8   # Tighter pacing
  epic_fantasy:
    multiplier: 1.5   # More expansive
  romance:
    multiplier: 1.0   # Neutral
  horror:
    multiplier: 0.9   # Slightly tighter
```

**C# Types to Generate**:

```csharp
// Core enum for spectrum identification
public enum SpectrumType
{
    LifeDeath,
    HonorDishonor,
    JusticeInjustice,
    FreedomSubjugation,
    LoveHate,
    RespectShame,
    PowerImpotence,
    SuccessFailure,
    AltruismSelfishness,
    WisdomIgnorance
}

// Greimas' Actantial Model - character role abstraction for templates
public enum ActantRole
{
    Subject,    // Who desires (protagonist)
    Object,     // What is desired (goal/person)
    Sender,     // Who initiates quest
    Receiver,   // Who benefits from quest completion
    Helper,     // Who assists the Subject
    Opponent    // Who opposes the Subject
}

// Four-stage pole structure (from YAML spectrum_definition.stages)
public sealed class SpectrumPole
{
    public required string Label { get; init; }      // e.g., "damnation"
    public required double Value { get; init; }      // -1.0, 0.0, 0.66, 1.0
    public required string Description { get; init; }
}

// Full spectrum definition (loaded from YAML)
public sealed class SpectrumDefinition
{
    public required SpectrumType Type { get; init; }
    public required string PositiveLabel { get; init; }  // e.g., "life"
    public required string NegativeLabel { get; init; }  // e.g., "death"
    public required SpectrumPole[] Stages { get; init; } // 4 stages
    public required string[] MediaExamples { get; init; }
}

// Runtime state container (mutable during story generation)
public sealed class NarrativeState
{
    // All 10 spectrums - nullable because only primary is tracked
    public double? LifeDeath { get; set; }
    public double? HonorDishonor { get; set; }
    public double? JusticeInjustice { get; set; }
    public double? FreedomSubjugation { get; set; }
    public double? LoveHate { get; set; }
    public double? RespectShame { get; set; }
    public double? PowerImpotence { get; set; }
    public double? SuccessFailure { get; set; }
    public double? AltruismSelfishness { get; set; }
    public double? WisdomIgnorance { get; set; }

    // Which spectrum is being tracked for arc progression
    public required SpectrumType PrimarySpectrum { get; init; }

    // GOAP heuristic: Euclidean distance to target state
    public double DistanceTo(NarrativeState target) { ... }

    // Get/set value by spectrum type
    public double? this[SpectrumType spectrum] { get; set; }
}

// Genre-to-spectrum mapping (loaded from YAML genre_spectrum_mappings)
public sealed class GenreSpectrumMapping
{
    public required string Genre { get; init; }
    public required string? Subgenre { get; init; }
    public required SpectrumType PrimarySpectrum { get; init; }
    public required SpectrumType? SecondarySpectrum { get; init; }
}
```

**YAML Loading Strategy**:
```csharp
public static class NarrativeStateLoader
{
    private static readonly Lazy<NarrativeStateData> _data = new(() =>
        YamlLoader.Load<NarrativeStateData>("narrative-state.yaml"));

    public static IReadOnlyList<SpectrumDefinition> Spectrums => _data.Value.Spectrums;
    public static IReadOnlyList<GenreSpectrumMapping> GenreMappings => _data.Value.GenreMappings;

    public static SpectrumType GetPrimarySpectrum(string genre, string? subgenre = null)
    {
        // Look up in GenreMappings, handle overrides
    }
}
```

### 2.2 emotional-arcs.yaml → storyline-theory

**Purpose**: Defines the 6 Reagan emotional arc shapes with mathematical forms

**C# Types to Generate**:

```csharp
public enum ArcType
{
    RagsToRiches,     // Monotonic rise
    Tragedy,          // Monotonic fall
    ManInHole,        // Fall-rise (U shape)
    Icarus,           // Rise-fall (inverted U)
    Cinderella,       // Rise-fall-rise
    Oedipus           // Fall-rise-fall
}

public enum ArcDirection
{
    Positive,  // Ends higher than starts
    Negative   // Ends lower than starts
}

// Control point for arc shape (from YAML control_points)
public sealed class ArcControlPoint
{
    public required double Position { get; init; }  // 0.0 to 1.0
    public required double Value { get; init; }     // Spectrum value
    public required string Label { get; init; }     // "start", "nadir", "triumph", etc.
}

// Full arc definition (loaded from YAML)
public sealed class EmotionalArc
{
    public required ArcType Type { get; init; }
    public required string ShapePattern { get; init; }      // "fall_rise", "rise_fall_rise"
    public required ArcDirection Direction { get; init; }
    public required string MathematicalForm { get; init; }  // For documentation
    public required ArcControlPoint[] ControlPoints { get; init; }
    public required double[] SampledTrajectory { get; init; } // Pre-computed 11 samples

    // Evaluate arc at any position (0.0 to 1.0) using interpolation
    public double EvaluateAt(double position)
    {
        // Interpolate from SampledTrajectory or use control points
    }
}
```

**YAML Loading Strategy**:
```csharp
public static class EmotionalArcLoader
{
    private static readonly Lazy<Dictionary<ArcType, EmotionalArc>> _arcs = new(() =>
        YamlLoader.Load<EmotionalArcsData>("emotional-arcs.yaml")
            .Arcs.ToDictionary(a => a.Type));

    public static EmotionalArc Get(ArcType type) => _arcs.Value[type];
    public static IReadOnlyCollection<EmotionalArc> All => _arcs.Value.Values;
}
```

### 2.3 story-actions.yaml → storyline-storyteller

**Purpose**: Defines the 46 GOAP actions with preconditions, effects, costs

**C# Types to Generate**:

```csharp
public enum ActionCategory
{
    Conflict,
    Relationship,
    Mystery,
    Resolution,
    Transformation
}

// Precondition for GOAP planning
public sealed class ActionPrecondition
{
    public required string Key { get; init; }     // e.g., "antagonist.known"
    public required object Value { get; init; }   // true, false, or numeric
    public ActionPreconditionOperator Operator { get; init; } = ActionPreconditionOperator.Equals;
}

public enum ActionPreconditionOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    LessThan,
    GreaterOrEqual,
    LessOrEqual
}

// Cardinality for effect application (schema-declared, runtime-applied)
public enum EffectCardinality
{
    Exclusive,  // Replaces any existing value at key (e.g., location)
    Additive    // Adds to collection at key (e.g., allies)
}

// Effect produced by action execution
public sealed class ActionEffect
{
    public required string Key { get; init; }     // e.g., "hero.at_mercy"
    public required object Value { get; init; }   // New value to set
    public EffectCardinality Cardinality { get; init; } = EffectCardinality.Exclusive;
}

// Narrative effect on the emotional arc
public sealed class NarrativeEffect
{
    public double? PrimarySpectrumDelta { get; init; }    // e.g., -0.4
    public double? SecondarySpectrumDelta { get; init; }
    public string? PositionAdvance { get; init; }         // "micro", "standard", "macro"
}

// Full action definition (loaded from YAML)
public sealed class StoryAction
{
    public required string Id { get; init; }              // e.g., "hero_at_mercy_of_villain"
    public required ActionCategory Category { get; init; }
    public required double Cost { get; init; }            // GOAP planning cost
    public required bool IsCoreEvent { get; init; }       // Obligatory scene?
    public required string[] ApplicableGenres { get; init; }

    public required ActionPrecondition[] Preconditions { get; init; }
    public required ActionEffect[] Effects { get; init; }
    public required NarrativeEffect NarrativeEffect { get; init; }

    public string? ChainedAction { get; init; }           // Follow-up action ID
    public string? Description { get; init; }
    public StoryActionVariant[]? Variants { get; init; }  // Genre-specific variations

    // Check if action is applicable given current world state
    public bool CanExecute(WorldState state) { ... }

    // Apply effects to world state (returns new state, immutable)
    public WorldState Execute(WorldState state) { ... }
}

// Genre-specific variant of an action
public sealed class StoryActionVariant
{
    public required string[] Genres { get; init; }
    public string? DescriptionOverride { get; init; }
    public NarrativeEffect? NarrativeEffectOverride { get; init; }
}
```

**YAML Loading Strategy**:
```csharp
public static class ActionRegistry
{
    private static readonly Lazy<Dictionary<string, StoryAction>> _actions = new(() =>
        YamlLoader.Load<StoryActionsData>("story-actions.yaml")
            .Actions.ToDictionary(a => a.Id));

    public static StoryAction Get(string id) => _actions.Value[id];
    public static IReadOnlyCollection<StoryAction> All => _actions.Value.Values;

    public static IEnumerable<StoryAction> GetCoreEvents(string genre)
    {
        return All.Where(a => a.IsCoreEvent && a.ApplicableGenres.Contains(genre));
    }

    public static IEnumerable<StoryAction> GetApplicable(WorldState state, string genre)
    {
        return All
            .Where(a => a.ApplicableGenres.Contains(genre))
            .Where(a => a.CanExecute(state));
    }
}
```

### 2.4 story-templates.yaml → storyline-storyteller

**Purpose**: Defines the 6 phase-based story templates with transition triggers

**YAML Schema Additions Needed** (for action count estimation):
```yaml
# In story-templates.yaml - Template-level defaults
man_in_hole:
  default_action_count: 50
  action_count_range: [30, 100]  # Valid override range
  # ...existing fields...
```

**C# Types to Generate**:

```csharp
// Phase position constraints (from YAML position block)
public sealed class PhasePosition
{
    public required double StcCenter { get; init; }       // Target position from STC
    public required double Floor { get; init; }           // Earliest advancement
    public required double Ceiling { get; init; }         // Forced advancement
    public required double ValidationBand { get; init; }  // ±tolerance
}

// Target state for phase completion (from YAML target_state)
public sealed class PhaseTargetState
{
    public required double MinPrimarySpectrum { get; init; }  // Range low
    public required double MaxPrimarySpectrum { get; init; }  // Range high
    public string? RangeDescription { get; init; }
}

// Transition trigger conditions (from YAML transition)
public sealed class PhaseTransition
{
    public required double PositionFloor { get; init; }   // Min position to advance
    public required double PositionCeiling { get; init; } // Force advance position
    public double? PrimarySpectrumMin { get; init; }      // State requirement
    public double? PrimarySpectrumMax { get; init; }      // State requirement
}

// Single phase in a story template
public sealed class StoryPhase
{
    public required int PhaseNumber { get; init; }
    public required string Name { get; init; }            // e.g., "nadir"
    public required PhasePosition Position { get; init; }
    public required PhaseTargetState TargetState { get; init; }
    public required PhaseTransition Transition { get; init; }
    public required string[] StcBeatsCovered { get; init; }
    public bool IsTerminal { get; init; }

    // Check if transition conditions are met
    public PhaseTransitionResult CheckTransition(double currentPosition, NarrativeState state)
    {
        if (currentPosition >= Transition.PositionCeiling)
            return PhaseTransitionResult.ForcedAdvance;

        if (currentPosition < Transition.PositionFloor)
            return PhaseTransitionResult.NotReady;

        // Check state requirements...
        return PhaseTransitionResult.Ready;
    }
}

public enum PhaseTransitionResult
{
    NotReady,       // Below position floor
    Ready,          // Meets all conditions
    ForcedAdvance   // Hit ceiling, advance anyway
}

// Full story template (loaded from YAML)
public sealed class StoryTemplate
{
    public required ArcType ArcType { get; init; }
    public required string Code { get; init; }            // e.g., "MAN_IN_HOLE"
    public required ArcDirection Direction { get; init; }
    public required string MathematicalForm { get; init; }
    public required StoryPhase[] Phases { get; init; }
    public required TemplateGenreCompatibility[] CompatibleGenres { get; init; }

    // Action count estimation (from YAML default_action_count, action_count_range)
    public required int DefaultActionCount { get; init; }           // e.g., 50
    public required (int Min, int Max) ActionCountRange { get; init; }  // e.g., (30, 100)

    // Get the phase for a given position
    public StoryPhase GetPhaseAt(double position)
    {
        // Find the phase whose position range contains this position
    }

    // Check genre compatibility
    public bool IsCompatibleWith(string genre, string? subgenre)
    {
        // Check CompatibleGenres list
    }
}

// Genre compatibility entry
public sealed class TemplateGenreCompatibility
{
    public required string Genre { get; init; }
    public string[]? Subgenres { get; init; }  // null = all subgenres
}
```

**YAML Loading Strategy**:
```csharp
public static class TemplateRegistry
{
    private static readonly Lazy<Dictionary<ArcType, StoryTemplate>> _templates = new(() =>
        YamlLoader.Load<StoryTemplatesData>("story-templates.yaml")
            .Templates.ToDictionary(t => t.ArcType));

    public static StoryTemplate Get(ArcType type) => _templates.Value[type];
    public static IReadOnlyCollection<StoryTemplate> All => _templates.Value.Values;

    public static IEnumerable<StoryTemplate> GetCompatible(string genre, string? subgenre)
    {
        return All.Where(t => t.IsCompatibleWith(genre, subgenre));
    }
}
```

---

## Part 3: GOAP Planner Integration

### 3.1 World State Model

The GOAP planner operates on a `WorldState` that combines:
1. **NarrativeState**: The emotional arc position
2. **StoryFacts**: Boolean/numeric facts about the story world

```csharp
public sealed class WorldState
{
    public required NarrativeState NarrativeState { get; init; }
    public required IReadOnlyDictionary<string, object> Facts { get; init; }
    public required double Position { get; init; }  // 0.0 to 1.0 story progress

    // Immutable update
    public WorldState WithFact(string key, object value) { ... }
    public WorldState WithNarrativeState(NarrativeState state) { ... }
    public WorldState WithPosition(double position) { ... }

    // Check if a precondition is satisfied
    public bool Satisfies(ActionPrecondition precondition) { ... }
}
```

### 3.2 Position Estimation & Action Count Resolution

Position estimation (`actionsCompleted / totalExpectedActions`) must handle variable action counts:

| Factor | Low End | High End |
|--------|---------|----------|
| Story Length | Short: 20-30 | Epic: 200+ |
| Genre | Thriller (tight): 50-70 | Fantasy (sprawling): 150+ |
| Template | Rags to Riches (4 phases): ~40 | Cinderella (5 phases): ~60 |
| Pacing | Fast-paced: fewer actions | Slow burn: more actions |

**Layered Resolution**:
```csharp
public int ResolveTargetActionCount(StoryTemplate template, string genre, int? requestOverride)
{
    // 1. Start with template default
    var baseCount = template.DefaultActionCount;  // e.g., 50

    // 2. Apply genre modifier
    var modifier = GenreModifiers.GetOrDefault(genre, 1.0);  // e.g., 0.8 for thriller
    var adjusted = (int)(baseCount * modifier);

    // 3. Apply request override if provided
    var target = requestOverride ?? adjusted;

    // 4. Clamp to template's valid range
    return Math.Clamp(target, template.ActionCountRange.Min, template.ActionCountRange.Max);
}
```

**Progress Tracking with Lazy Recalculation**:
```csharp
public sealed class StoryProgress
{
    public int ActionsCompleted { get; private set; }
    public int CompletedPhases { get; private set; }
    public int CurrentPhaseActionsPlanned { get; private set; }
    public int TotalActionsEstimate { get; private set; }

    public double Position => (double)ActionsCompleted / TotalActionsEstimate;

    /// <summary>
    /// Called when entering a new phase (lazy evaluation).
    /// Re-estimates total based on current trajectory.
    /// </summary>
    public void OnPhaseGenerated(int phaseActionCount, int remainingPhases)
    {
        CurrentPhaseActionsPlanned = phaseActionCount;

        // Re-estimate total based on current trajectory
        var avgPerPhase = ActionsCompleted > 0
            ? ActionsCompleted / CompletedPhases
            : phaseActionCount;
        TotalActionsEstimate = ActionsCompleted + (avgPerPhase * (remainingPhases + 1));
    }

    public void OnActionCompleted()
    {
        ActionsCompleted++;
    }

    public void OnPhaseCompleted()
    {
        CompletedPhases++;
    }
}
```

**Key Insight**: Position is an *estimate* that gets refined as the story progresses. The hybrid trigger's position ceiling (from Q3.2) prevents estimation drift from causing deadlocks.

### 3.3 GOAP Planning Algorithm

```csharp
public sealed class StoryGoapPlanner
{
    private readonly ActionRegistry _actions;

    public StorylinePlan Plan(
        WorldState initialState,
        StoryTemplate template,
        StoryPhase targetPhase,
        string genre,
        PlanningUrgency urgency = PlanningUrgency.Medium)
    {
        // A* search from initialState to targetPhase.TargetState
        // Uses NarrativeState.DistanceTo() as heuristic
        // Action costs from StoryAction.Cost
        // Urgency affects search parameters (from YAML goap_planning section)
    }
}

public enum PlanningUrgency
{
    Low,    // max_iterations: 1000, beam_width: 20
    Medium, // max_iterations: 500, beam_width: 15
    High    // max_iterations: 200, beam_width: 10
}
```

### 3.4 Chained Action Handling

Actions with `chained_action` must be handled atomically:

```csharp
public sealed class ActionChain
{
    public required StoryAction[] Actions { get; init; }
    public required double TotalCost { get; init; }

    public static ActionChain Build(StoryAction startAction, ActionRegistry registry)
    {
        var chain = new List<StoryAction> { startAction };
        var current = startAction;

        while (current.ChainedAction != null)
        {
            current = registry.Get(current.ChainedAction);
            chain.Add(current);
        }

        return new ActionChain
        {
            Actions = chain.ToArray(),
            TotalCost = chain.Sum(a => a.Cost)
        };
    }
}
```

---

## Part 4: Phase Evaluation Flow

### 4.1 Lazy Phase Generation

Per SDK_FOUNDATIONS: Only generate the current phase, re-evaluate world state before generating the next.

```csharp
public sealed class PhaseEvaluator
{
    private readonly StoryGoapPlanner _planner;
    private readonly TemplateRegistry _templates;

    public PhaseResult EvaluatePhase(
        StoryContext context,
        StoryPhase phase)
    {
        // 1. Plan action sequence to reach phase target
        var plan = _planner.Plan(
            context.CurrentState,
            context.Template,
            phase,
            context.Genre);

        // 2. Return plan for execution (don't execute here)
        return new PhaseResult
        {
            Phase = phase,
            ActionSequence = plan.Actions,
            EstimatedEndState = plan.ProjectedEndState,
            TransitionCheck = phase.CheckTransition(
                plan.ProjectedEndState.Position,
                plan.ProjectedEndState.NarrativeState)
        };
    }
}
```

### 4.2 Hybrid Transition Logic

```csharp
public PhaseTransitionResult CheckTransition(
    double currentPosition,
    NarrativeState state,
    StoryPhase phase)
{
    // Forced advancement at ceiling (deadlock prevention)
    if (currentPosition >= phase.Transition.PositionCeiling)
    {
        _logger.LogWarning("Forced phase advancement at position {Position}", currentPosition);
        return PhaseTransitionResult.ForcedAdvance;
    }

    // Not ready if below floor (speed-running prevention)
    if (currentPosition < phase.Transition.PositionFloor)
    {
        return PhaseTransitionResult.NotReady;
    }

    // Check state requirements
    var primaryValue = state[context.PrimarySpectrum];
    if (phase.Transition.PrimarySpectrumMin.HasValue &&
        primaryValue < phase.Transition.PrimarySpectrumMin)
    {
        return PhaseTransitionResult.NotReady;
    }

    if (phase.Transition.PrimarySpectrumMax.HasValue &&
        primaryValue > phase.Transition.PrimarySpectrumMax)
    {
        return PhaseTransitionResult.NotReady;
    }

    return PhaseTransitionResult.Ready;
}
```

---

## Part 5: StorylinePlan Output Format

### 5.1 Output Structure

Per STORYLINE_COMPOSER, the SDK outputs a `StorylinePlan` that callers execute:

```csharp
public sealed class StorylinePlan
{
    public required Guid PlanId { get; init; }
    public required ArcType ArcType { get; init; }
    public required string Genre { get; init; }
    public required string? Subgenre { get; init; }
    public required SpectrumType PrimarySpectrum { get; init; }

    public required StorylinePlanPhase[] Phases { get; init; }
    public required WorldState InitialState { get; init; }
    public required WorldState ProjectedEndState { get; init; }

    // Core events that MUST occur (obligatory scenes)
    public required string[] RequiredCoreEvents { get; init; }
}

public sealed class StorylinePlanPhase
{
    public required int PhaseNumber { get; init; }
    public required string PhaseName { get; init; }
    public required StorylinePlanAction[] Actions { get; init; }
    public required PhaseTargetState TargetState { get; init; }
    public required PhasePosition PositionBounds { get; init; }
}

public sealed class StorylinePlanAction
{
    public required string ActionId { get; init; }
    public required int SequenceIndex { get; init; }
    public required ActionEffect[] Effects { get; init; }
    public required NarrativeEffect NarrativeEffect { get; init; }
    public required bool IsCoreEvent { get; init; }
    public string? ChainedFrom { get; init; }
}
```

---

## Part 6: Archive Extraction & Plugin Integration

### 6.1 Generated Compress Models Pattern

Compress data schemas live in plugin schemas. Code generation copies models to the SDK's `/Generated/` directory. This follows established patterns:
- Behavior SDK has `/Generated/` models from behavior schemas
- `bannou-service/Generated/Events/` has event models from plugin event schemas
- `bannou-service/Generated/Models/` has request/response models from API schemas

**Generation Pipeline**:
```
Plugin Schemas                          SDK Generated/
─────────────────                       ───────────────
character-personality-api.yaml    →     PersonalityCompressData.cs
character-history-api.yaml        →     HistoryCompressData.cs
character-encounter-api.yaml      →     EncounterCompressData.cs
realm-history-api.yaml            →     RealmLoreCompressData.cs
```

The SDK references these generated types **without project dependency on plugins**.

### 6.2 ArchiveExtractor (storyline-theory)

The ArchiveExtractor lives in `storyline-theory` and uses **opaque string keys** to access archive entries. It has no compile-time knowledge of which plugins exist:

```csharp
/// <summary>
/// Extracts narrative-relevant data from compressed archives using opaque keys.
/// The SDK doesn't reference plugins directly - it receives a generic bundle
/// and extracts what it knows how to interpret.
/// </summary>
public sealed class ArchiveExtractor
{
    /// <summary>
    /// Extract WorldState from an archive bundle using opaque string keys.
    /// Unknown keys are silently ignored (forward compatibility).
    /// </summary>
    public WorldState ExtractWorldState(ArchiveBundle bundle, StoryContext context)
    {
        var facts = new Dictionary<string, object>();

        // SDK uses opaque keys - doesn't know/care which plugin provides them
        // If entry exists and SDK knows how to interpret it, use it
        // If entry missing or unknown type, skip gracefully

        if (bundle.TryGetEntry<PersonalityCompressData>("character-personality", out var personality))
        {
            // Map personality traits to story facts
            facts["protagonist.trait.confrontational"] = personality.Confrontational;
            facts["protagonist.trait.cautious"] = personality.Cautious;
            // ... SDK defines the interpretation rules
        }

        if (bundle.TryGetEntry<EncounterCompressData>("character-encounter", out var encounters))
        {
            // Map encounter history to story facts
            facts["protagonist.has_nemesis"] = encounters.Any(e => e.Sentiment < -0.5);
            facts["protagonist.has_ally"] = encounters.Any(e => e.Sentiment > 0.5);
        }

        if (bundle.TryGetEntry<HistoryCompressData>("character-history", out var history))
        {
            // Map historical events to arc-relevant facts
            facts["protagonist.experienced_trauma"] = history.HasTrauma;
            facts["protagonist.achieved_victory"] = history.HasMajorVictory;
        }

        return new WorldState
        {
            NarrativeState = CreateInitialNarrativeState(context),
            Facts = facts,
            Position = 0.0
        };
    }
}

/// <summary>
/// Generic archive bundle passed from plugin to SDK.
/// The plugin aggregates entries; SDK interprets them.
/// </summary>
public sealed class ArchiveBundle
{
    private readonly Dictionary<string, object> _entries = new();

    public void AddEntry<T>(string key, T data) where T : class
    {
        _entries[key] = data;
    }

    public bool TryGetEntry<T>(string key, out T? data) where T : class
    {
        if (_entries.TryGetValue(key, out var obj) && obj is T typed)
        {
            data = typed;
            return true;
        }
        data = null;
        return false;
    }
}
```

### 6.3 KernelExtractor (storyline-theory)

Separate from ArchiveExtractor - different purposes:
- **ArchiveExtractor**: "What is the starting state?" → WorldState for GOAP planning
- **KernelExtractor**: "What stories could emerge?" → NarrativeKernels for template selection

```csharp
/// <summary>
/// Identifies essential narrative events (kernels) from archive data.
/// Kernels are story opportunities - events that could seed new storylines.
/// </summary>
public sealed class KernelExtractor
{
    /// <summary>
    /// Extract narrative kernels from an archive bundle.
    /// Returns story opportunities ordered by significance.
    /// </summary>
    public List<NarrativeKernel> ExtractKernels(ArchiveBundle bundle)
    {
        var kernels = new List<NarrativeKernel>();

        // Death is ALWAYS a kernel for archived characters
        if (bundle.TryGetEntry<CharacterCompressData>("character", out var character))
        {
            kernels.Add(new NarrativeKernel
            {
                Type = KernelType.Death,
                Significance = 1.0,  // Maximum significance
                Description = $"Death of {character.Name}",
                Data = new Dictionary<string, object>
                {
                    ["cause"] = character.DeathCause,
                    ["location"] = character.DeathLocation
                }
            });
        }

        // High-significance historical events
        if (bundle.TryGetEntry<HistoryCompressData>("character-history", out var history))
        {
            foreach (var p in history.Participations.Where(p => p.Significance > 0.7))
            {
                kernels.Add(new NarrativeKernel
                {
                    Type = KernelType.HistoricalEvent,
                    Significance = p.Significance,
                    Description = $"Participated in {p.EventCode} as {p.Role}",
                    Data = new Dictionary<string, object>
                    {
                        ["event"] = p.EventCode,
                        ["role"] = p.Role
                    }
                });
            }

            // Trauma creates narrative hooks
            if (history.HasBackstory && !string.IsNullOrEmpty(history.Backstory.Trauma))
            {
                kernels.Add(new NarrativeKernel
                {
                    Type = KernelType.Trauma,
                    Significance = 0.8,
                    Description = "Character trauma",
                    Data = new Dictionary<string, object> { ["trauma"] = history.Backstory.Trauma }
                });
            }

            // Unfinished goals create sequel hooks
            if (history.HasBackstory && history.Backstory.Goals?.Any() == true)
            {
                kernels.Add(new NarrativeKernel
                {
                    Type = KernelType.UnfinishedBusiness,
                    Significance = 0.75,
                    Description = "Unfinished goals",
                    Data = new Dictionary<string, object> { ["goals"] = history.Backstory.Goals }
                });
            }
        }

        // Deep conflicts (potential grudge/revenge storylines)
        if (bundle.TryGetEntry<EncounterCompressData>("character-encounter", out var encounters))
        {
            var conflicts = encounters.Encounters
                .Where(e => e.Perspectives.Any(p => p.SentimentShift < -0.5 && p.EmotionalImpact > 0.7));

            foreach (var conflict in conflicts)
            {
                kernels.Add(new NarrativeKernel
                {
                    Type = KernelType.Conflict,
                    Significance = 0.85,
                    Description = "Deep conflict encounter",
                    Data = new Dictionary<string, object>
                    {
                        ["encounterId"] = conflict.EncounterId,
                        ["otherCharacters"] = conflict.Perspectives.Select(p => p.OtherCharacterId).ToArray()
                    }
                });
            }

            // Deep bonds (legacy/protection storylines)
            var bonds = encounters.Perspectives
                .GroupBy(p => p.OtherCharacterId)
                .Where(g => g.Average(p => p.SentimentShift) > 0.6 && g.Count() > 5);

            foreach (var bond in bonds)
            {
                kernels.Add(new NarrativeKernel
                {
                    Type = KernelType.DeepBond,
                    Significance = 0.7,
                    Description = "Deep relationship bond",
                    Data = new Dictionary<string, object>
                    {
                        ["bondedCharacterId"] = bond.Key,
                        ["averageSentiment"] = bond.Average(p => p.SentimentShift)
                    }
                });
            }
        }

        return kernels.OrderByDescending(k => k.Significance).ToList();
    }
}

public sealed class NarrativeKernel
{
    public required KernelType Type { get; init; }
    public required double Significance { get; init; }  // 0-1 scale
    public required string Description { get; init; }
    public required Dictionary<string, object> Data { get; init; }
}

public enum KernelType
{
    Death,              // Character death (highest significance)
    HistoricalEvent,    // Participation in major world events
    Trauma,             // Past traumatic experiences
    UnfinishedBusiness, // Unachieved goals
    Conflict,           // Deep negative encounters (grudge potential)
    DeepBond            // Strong positive relationships (legacy potential)
}
```

### 6.4 Plugin Responsibility (lib-storyline)

The plugin handles data aggregation - it knows which services to call:

```csharp
// In lib-storyline plugin (L4) - NOT in SDK
public class StorylineService : IStorylineService
{
    // Plugin has compile-time knowledge of other L4 services
    private readonly IResourceClient _resourceClient;

    public async Task<StorylinePlan> ComposeAsync(ComposeRequest request)
    {
        // 1. Plugin fetches archives from lib-resource
        var characterArchive = await _resourceClient.GetArchiveAsync(request.CharacterId);
        var realmArchive = await _resourceClient.GetArchiveAsync(request.RealmId);

        // 2. Plugin aggregates into generic bundle
        var bundle = new ArchiveBundle();
        bundle.AddEntry("character-personality", characterArchive.GetEntry("personality"));
        bundle.AddEntry("character-encounter", characterArchive.GetEntry("encounters"));
        bundle.AddEntry("character-history", characterArchive.GetEntry("history"));
        bundle.AddEntry("realm-history", realmArchive.GetEntry("lore"));

        // 3. Pass to SDK - SDK extracts what it needs
        var worldState = _archiveExtractor.ExtractWorldState(bundle, context);

        // 4. Plan the story
        var plan = _storyPlanner.Plan(worldState, context);

        return plan;
    }
}
```

### 6.4 StoryContext (SDK-Defined)

The context for story generation - uses SDK types, not plugin types:

```csharp
public sealed class StoryContext
{
    // Story configuration (not archive data - that's in WorldState.Facts)
    public required StoryTemplate Template { get; init; }
    public required string Genre { get; init; }
    public required string? Subgenre { get; init; }
    public required SpectrumType PrimarySpectrum { get; init; }

    // Participant IDs (opaque to SDK - just GUIDs for tracking)
    public required Guid[] CharacterIds { get; init; }
    public required Guid RealmId { get; init; }

    // Greimas actant role assignments - enables character-agnostic templates
    // GOAP actions reference roles ("Helper betrays Subject"), not character IDs
    public required Dictionary<ActantRole, Guid[]> ActantAssignments { get; init; }

    /// <summary>
    /// Target action count. If null, derived from template default + genre modifier.
    /// Clamped to template's ActionCountRange.
    /// </summary>
    public int? TargetActionCount { get; init; }

    // Current state (updated during generation)
    public WorldState CurrentState { get; set; }

    // Progress tracking for position estimation
    public StoryProgress Progress { get; } = new();
}
```

### 6.5 Archive-to-WorldState Mapping (NOT Core SDK)

**Important**: The detailed mapping rules (how personality traits score for each template, what history events matter for which genres, etc.) are **NOT core SDK**. Per GAP_ANALYSIS Gap 5:

> "Archive-to-WorldState Extraction Rules" is explicitly NOT a schema definition. It's:
> - Documentation of how ArchiveExtractor maps fields → WorldState keys
> - Implementation tuning (scoring weights per template)
> - A "loader specification" - runtime configuration, not core SDK

This will be handled in Phase 5 (Integration) as configuration/tuning, not in the core SDK implementation phases.

### 6.7 Intent Generation Bridge

The Storyteller layer generates "intents" that the plugin translates to service calls.
SDK defines intent vocabulary; plugin dispatches to appropriate clients.

```csharp
// Intent types - SDK defines vocabulary, plugin interprets and dispatches
public enum StoryIntentType
{
    SpawnCharacter,      // Plugin → ICharacterClient.CreateAsync()
    AssignBehavior,      // Plugin → IActorClient.UpdateBehaviorAsync()
    CreateContract,      // Plugin → IContractClient.CreateFromTemplateAsync()
    TriggerEncounter,    // Plugin → ICharacterEncounterClient.CreateAsync()
    ModifyRelationship,  // Plugin → IRelationshipClient.UpdateAsync()
    UpdatePersonality,   // Plugin → ICharacterPersonalityClient.EvolveAsync()
    RecordHistory        // Plugin → ICharacterHistoryClient.AddParticipationAsync()
}

public interface IStorylineIntentGenerator
{
    // Generate story-level intents from plan execution
    IEnumerable<StorylineIntent> GenerateIntents(
        StorylinePlan plan,
        StoryPhase currentPhase,
        WorldState currentState);
}

public sealed class StorylineIntent
{
    public required StoryIntentType Type { get; init; }
    public required Dictionary<string, object> Parameters { get; init; }
    public required double Urgency { get; init; }     // Affects execution priority
    public required ActantRole? TargetRole { get; init; }  // Which actant this affects
}
```

**Plugin dispatches intents to services**:
```csharp
// In lib-storyline plugin - NOT in SDK
public async Task ExecuteIntentAsync(StorylineIntent intent, StoryContext context)
{
    switch (intent.Type)
    {
        case StoryIntentType.SpawnCharacter:
            var charRequest = MapToCharacterRequest(intent.Parameters);
            await _characterClient.CreateAsync(charRequest);
            break;

        case StoryIntentType.AssignBehavior:
            var actorId = GetActorForRole(intent.TargetRole, context);
            var abml = (string)intent.Parameters["abml_fragment"];
            await _actorClient.UpdateBehaviorAsync(actorId, abml);
            break;

        case StoryIntentType.CreateContract:
            var contractRequest = MapToContractRequest(intent.Parameters);
            await _contractClient.CreateFromTemplateAsync(contractRequest);
            break;

        // ... other intent types
    }
}
```

---

## Part 7: Implementation Order

### Phase 0: Generation Pipeline Setup

Before implementing SDK code, set up the generation pipeline for compress models:

**Scripts to Create/Modify**:
1. `scripts/generate-storyline-sdk.sh` - Generation script for storyline SDKs
2. Adds NSwag model generation from plugin compress schemas → SDK `/Generated/`

**Plugin Schemas Providing Compress Models**:
- `character-personality-api.yaml` → `PersonalityCompressData`
- `character-history-api.yaml` → `HistoryCompressData`
- `character-encounter-api.yaml` → `EncounterCompressData`
- `realm-history-api.yaml` → `RealmLoreCompressData`

**Output**:
```
sdks/storyline-theory/Generated/CompressModels/
├── PersonalityCompressData.cs
├── HistoryCompressData.cs
├── EncounterCompressData.cs
└── RealmLoreCompressData.cs
```

**Validation**:
- [ ] Generation script runs without errors
- [ ] All compress model types are generated
- [ ] SDK compiles with generated types

### Phase 1: storyline-theory Foundation

**Files to Create**:
1. `storyline-theory/YamlLoader.cs` - Generic YAML loading with embedded resources
2. `storyline-theory/Spectrums/SpectrumType.cs` - Enum for 10 spectrums
3. `storyline-theory/Spectrums/SpectrumDefinition.cs` - Loaded from YAML
4. `storyline-theory/Spectrums/NarrativeState.cs` - Runtime state container
5. `storyline-theory/Spectrums/NarrativeStateLoader.cs` - Loads narrative-state.yaml
6. `storyline-theory/Arcs/ArcType.cs` - Enum for 6 arcs
7. `storyline-theory/Arcs/EmotionalArc.cs` - Loaded from YAML
8. `storyline-theory/Arcs/EmotionalArcLoader.cs` - Loads emotional-arcs.yaml
9. `storyline-theory/Actants/ActantRole.cs` - Greimas' 6 actant roles enum
10. `storyline-theory/Archives/ArchiveBundle.cs` - Generic archive container (opaque keys)
11. `storyline-theory/Archives/ArchiveExtractor.cs` - Extracts WorldState from bundle
12. `storyline-theory/Archives/KernelExtractor.cs` - Extracts NarrativeKernels for story seeding
13. `storyline-theory/Archives/NarrativeKernel.cs` - Kernel model with KernelType enum

**Validation**:
- [ ] All 10 spectrums load correctly
- [ ] 4-stage poles parse correctly
- [ ] Genre mappings resolve correctly
- [ ] All 6 arcs load correctly
- [ ] Arc interpolation works at any position
- [ ] Control points match sampled trajectory
- [ ] ArchiveBundle accepts entries with string keys
- [ ] ArchiveExtractor produces WorldState from bundle
- [ ] KernelExtractor identifies Death, Trauma, Conflict, DeepBond kernels
- [ ] Kernels sorted by significance

### Phase 2: storyline-storyteller Actions

**Files to Create**:
1. `storyline-storyteller/Actions/ActionCategory.cs` - Enum (5 categories)
2. `storyline-storyteller/Actions/ActionPrecondition.cs` - Precondition model with operators
3. `storyline-storyteller/Actions/EffectCardinality.cs` - Exclusive vs Additive enum
4. `storyline-storyteller/Actions/ActionEffect.cs` - Effect model with cardinality
5. `storyline-storyteller/Actions/NarrativeEffect.cs` - Spectrum delta model
6. `storyline-storyteller/Actions/StoryAction.cs` - Full action model
7. `storyline-storyteller/Actions/ActionRegistry.cs` - Loads story-actions.yaml
8. `storyline-storyteller/Actions/ActionChain.cs` - Chained action handling

**Validation**:
- [ ] All 46 actions load correctly
- [ ] Preconditions evaluate correctly (all operators)
- [ ] Effects apply correctly with cardinality (Exclusive replaces, Additive adds)
- [ ] Chained actions resolve correctly
- [ ] Genre filtering works
- [ ] NarrativeEffect deltas parse correctly

### Phase 3: storyline-storyteller Templates

**Files to Create**:
1. `storyline-storyteller/Templates/StoryPhase.cs` - Phase model
2. `storyline-storyteller/Templates/StoryTemplate.cs` - Template model with DefaultActionCount, ActionCountRange
3. `storyline-storyteller/Templates/TemplateRegistry.cs` - Loads story-templates.yaml
4. `storyline-storyteller/Templates/PhaseEvaluator.cs` - Transition logic

**Validation**:
- [ ] All 6 templates load correctly
- [ ] Phase positions parse correctly
- [ ] Transition logic works (floor/ceiling/state)
- [ ] Genre compatibility filtering works
- [ ] DefaultActionCount and ActionCountRange parse correctly

### Phase 4: GOAP Planner

**Files to Create**:
1. `storyline-storyteller/Planning/WorldState.cs` - Combined state model
2. `storyline-storyteller/Planning/StoryGoapPlanner.cs` - A* search
3. `storyline-storyteller/Planning/StorylinePlan.cs` - Output format
4. `storyline-storyteller/Planning/PlanningUrgency.cs` - Search parameters
5. `storyline-storyteller/Planning/StoryProgress.cs` - Position tracking with lazy recalculation
6. `storyline-storyteller/Planning/ActionCountResolver.cs` - Layered resolution (template → genre → override)

**Validation**:
- [ ] A* search finds valid paths
- [ ] Heuristic (DistanceTo) guides search effectively
- [ ] Urgency parameters affect search behavior
- [ ] Plans include all required core events
- [ ] ActionCountResolver applies genre modifiers correctly
- [ ] StoryProgress recalculates position on phase generation
- [ ] Position ceiling prevents estimation drift deadlocks

### Phase 5: Integration & Tuning

**SDK Files to Create**:
1. `storyline-storyteller/Composition/StoryContext.cs` - Context model with ActantAssignments
2. `storyline-storyteller/Composition/StorylineComposer.cs` - Main entry point
3. `storyline-storyteller/Intents/StoryIntentType.cs` - Intent type enum (7 types)
4. `storyline-storyteller/Intents/StorylineIntent.cs` - Intent with typed parameters
5. `storyline-storyteller/Intents/IntentGenerator.cs` - Intent generation from plan

**Plugin Files to Create** (lib-storyline, NOT SDK):
1. `lib-storyline/StorylineService.cs` - Plugin orchestration (fetch, aggregate, call SDK)
2. `lib-storyline/Generated/` - Controller, interface from `storyline-api.yaml`

**Archive-to-WorldState Mapping (Tuning, NOT Core SDK)**:
- Define which personality traits map to which story facts
- Define scoring weights for different templates
- Document field → key mappings
- This is runtime configuration, handled here in integration phase

**Validation**:
- [ ] Full composition pipeline works end-to-end
- [ ] Plugin fetches archives and passes to SDK correctly
- [ ] Archive extraction produces meaningful WorldState
- [ ] Intent generation produces actionable output
- [ ] Regional Watcher → lib-storyline → SDK flow works

---

## Part 8: YAML Embedding Strategy

### 8.1 Embedded Resources

YAML files will be embedded in the assembly as resources:

```xml
<!-- In storyline-theory.csproj -->
<ItemGroup>
  <EmbeddedResource Include="Data\narrative-state.yaml" />
  <EmbeddedResource Include="Data\emotional-arcs.yaml" />
</ItemGroup>

<!-- In storyline-storyteller.csproj -->
<ItemGroup>
  <EmbeddedResource Include="Data\story-actions.yaml" />
  <EmbeddedResource Include="Data\story-templates.yaml" />
</ItemGroup>
```

### 8.2 YAML Loader Implementation

```csharp
public static class YamlLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public static T Load<T>(string resourceName)
    {
        var assembly = typeof(YamlLoader).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName));

        using var stream = assembly.GetManifestResourceStream(fullName);
        using var reader = new StreamReader(stream!);
        return Deserializer.Deserialize<T>(reader);
    }
}
```

---

## Part 9: Testing Strategy

### 9.1 Unit Tests

Each layer has comprehensive unit tests:

```csharp
// Theory layer tests
[Fact]
public void NarrativeStateLoader_LoadsAll10Spectrums()
{
    var spectrums = NarrativeStateLoader.Spectrums;
    Assert.Equal(10, spectrums.Count);
}

[Fact]
public void EmotionalArc_ManInHole_InterpolatesCorrectly()
{
    var arc = EmotionalArcLoader.Get(ArcType.ManInHole);
    var nadir = arc.EvaluateAt(0.5);
    Assert.InRange(nadir, 0.19, 0.21); // Should be ~0.20
}

// Storyteller layer tests
[Fact]
public void ActionRegistry_LoadsAll46Actions()
{
    var actions = ActionRegistry.All;
    Assert.Equal(46, actions.Count);
}

[Fact]
public void StoryGoapPlanner_FindsPathToNadir()
{
    var planner = new StoryGoapPlanner(ActionRegistry.Instance);
    var plan = planner.Plan(initialState, template, nadirPhase, "crime");
    Assert.NotEmpty(plan.Actions);
}
```

---

## Appendix A: Audit Decisions (SDK_FOUNDATIONS Alignment)

Decisions from the SDK_FOUNDATIONS.md audit (2026-02-04):

| # | Question | Decision | Rationale |
|---|----------|----------|-----------|
| 1 | TimeoutBehavior | `ForceTransition` only | Aligns with Q3.2's position ceiling. ExtendWithWarning/InsertBridgeContent add complexity without proven need. |
| 2 | Actant Roles | **Include** `ActantRole` enum | Enables character-agnostic templates ("Helper betrays Subject"), lightweight addition with high value. |
| 3 | KernelExtractor | **Separate** from ArchiveExtractor | Different purposes: Kernels = "what stories?", Extraction = "what state?". Different consumers. |
| 4 | Exclusion Logic | **Schema metadata**, not runtime enforcement | Declare `cardinality: exclusive/additive` in YAML, `EffectCardinality` enum applies at runtime. |
| 5 | Typed Intents | **Generic** with `StoryIntentType` enum | SDK remains pure computation (no service imports). Plugin dispatches by type. |
| 6 | Plot Units | **Deferred** | Summarization tool, not core planning. Future enhancement for archive compression. |

---

## Appendix B: Migration from Existing SDKs

The existing SDKs will be **completely replaced**, not enhanced. Key differences:

| Aspect | Old SDK | New SDK |
|--------|---------|---------|
| Data Source | Hardcoded C# | YAML files |
| Architecture | Single layer | Two layers (theory + storyteller) |
| State Model | Ad-hoc | 10 Life Value spectrums |
| Arc Shapes | Implicit | 6 explicit Reagan arcs |
| Phase Logic | Manual | Hybrid position/state triggers |
| Action System | Custom | GOAP with chained atomics |

**Migration Path**:
1. Create generation pipeline for compress models (Phase 0)
2. Create new SDKs alongside old (Phases 1-4)
3. Validate new SDKs produce correct output
4. Create `lib-storyline` plugin (Phase 5)
5. Update Regional Watcher actors to call lib-storyline
6. Delete old SDKs

---

## Appendix C: lib-storyline Plugin (L4)

The plugin is separate from the SDKs and handles:
- Fetching archives from lib-resource
- Aggregating compressed entries
- Calling SDK with generic ArchiveBundle
- Exposing HTTP API for Regional Watchers

**Schema**: `schemas/storyline-api.yaml`
**Layer**: L4 (Game Features)
**Dependencies**: lib-resource (L1), lib-character-* (L4, graceful degradation), lib-realm-* (L4, graceful degradation)

```yaml
# schemas/storyline-api.yaml
openapi: 3.0.0
info:
  title: Storyline Service API
  version: 1.0.0
x-service-layer: GameFeatures

paths:
  /storyline/compose:
    post:
      summary: Compose a storyline plan from character/realm archives
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ComposeRequest'
      responses:
        '200':
          description: Storyline plan
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/StorylinePlanResponse'

  /storyline/instantiate:
    post:
      summary: Instantiate a storyline plan (spawn entities)
      # ...
```

---

## Appendix D: YAML Schema Validation (Future)

Consider adding JSON Schema or equivalent for YAML validation:

```yaml
# narrative-state.schema.yaml
type: object
required:
  - version
  - spectrums
  - genre_spectrum_mappings
properties:
  spectrums:
    type: array
    items:
      type: object
      required: [type, positive_label, negative_label, stages]
      # ...
```

This enables CI validation that YAML files remain well-formed. Low priority - can be added later.

---

## Changelog

| Date | Version | Changes |
|------|---------|---------|
| 2026-02-04 | 1.0 | Initial plan document |
| 2026-02-04 | 1.1 | Added Music vs Storyline comparison, ArchiveExtractor pattern, opaque string keys, generated compress models from plugin schemas, Phase 0 for generation setup, lib-storyline plugin appendix, clarified Gap 5 is NOT core SDK |
| 2026-02-04 | 1.2 | Audit against SDK_FOUNDATIONS: Added ActantRole enum (Greimas), KernelExtractor (separate from ArchiveExtractor), EffectCardinality (schema metadata), StoryIntentType enum (generic with typed parameters), deferred Plot Units to future |
| 2026-02-04 | 1.3 | Added position estimation: StoryProgress with lazy recalculation, ActionCountResolver (template default → genre modifier → request override → clamped), StoryContext.TargetActionCount, StoryTemplate.DefaultActionCount/ActionCountRange, YAML schema additions for action count modifiers |
