# Behavior Plugin V2 - GOAP Planning & ABML Runtime

> **Status**: PHASES 1-4 MOSTLY COMPLETE, PHASE 5 IN PROGRESS
> **Created**: 2024-12-28
> **Updated**: 2026-01-05 (Phase 5 actor integration Bannou-side COMPLETE)
> **Related Documents**:
> - **[ABML Guide](../guides/ABML.md)** - **ABML Language Specification & Runtime** (authoritative)
> - [ABML_LOCAL_RUNTIME.md](./ONGOING_-_ABML_LOCAL_RUNTIME.md) - Local client execution & bytecode compilation
> - [GOAP_FIRST_STEPS.md](./GOAP_FIRST_STEPS.md) - GOAP implementation details
> - [ACTORS_PLUGIN_V3.md](./UPCOMING_-_ACTORS_PLUGIN_V3.md) - Actor infrastructure (authoritative)

**Implementation Status**:
- **Phase 1 (ABML Runtime)**: ✅ COMPLETE - 585 tests passing. See [ABML Guide](../guides/ABML.md).
- **Phase 2 (GOAP)**: ✅ COMPLETE - See [GOAP_FIRST_STEPS.md](./GOAP_FIRST_STEPS.md). Full A* planner, metadata caching, API endpoints.
- **Phase 3 (Multi-Channel)**: ✅ COMPLETE - Sync points, barriers, deadlock detection.
- **Phase 4 (Cognition)**: ✅ MOSTLY COMPLETE - All 6 handlers implemented (FilterAttention, AssessSignificance, EvaluateGoalImpact, QueryMemory, StoreMemory, TriggerGoapReplan). Registered in DocumentExecutorFactory. Pipeline orchestration via ABML behaviors.
- **Phase 5 (Actor Integration)**: 🔄 IN PROGRESS - Bannou-side perception/state wiring COMPLETE. Stride-side integration NEXT. See [ACTORS_PLUGIN_V3.md §5.2](./UPCOMING_-_ACTORS_PLUGIN_V3.md).

**Bannou-Specific Constraints**: See [ABML Guide Appendix A](../guides/ABML.md#appendix-a-bannou-implementation-requirements) for mandatory infrastructure patterns.

---

## Executive Summary

The Behavior Plugin is the **logic layer** for autonomous agent decision-making in Bannou. While the Actor Plugin provides infrastructure (lifecycle, state persistence, event routing), the Behavior Plugin provides:

- **ABML Runtime**: Parsing, compilation, and execution of ABML (Arcadia Behavior Markup Language) documents
- **GOAP Planner**: Goal-Oriented Action Planning for intelligent action selection
- **Cognition Pipeline**: Perception interpretation, attention allocation, memory integration
- **Multi-Channel Executor**: Parallel execution for cutscenes, dialogues, and complex sequences

### Design Principle: Separation of Concerns

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           ACTOR PLUGIN (Infrastructure)                       │
│                                                                               │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐               │
│  │ Actor Lifecycle │  │ State           │  │ Event           │               │
│  │ Management      │  │ Persistence     │  │ Routing         │               │
│  └────────┬────────┘  └────────┬────────┘  └────────┬────────┘               │
│           │                    │                    │                         │
│           └────────────────────┼────────────────────┘                         │
│                                │                                              │
│                     Messages & State to/from actors                           │
└────────────────────────────────┼──────────────────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                          BEHAVIOR PLUGIN (Logic)                              │
│                                                                               │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐               │
│  │ ABML Runtime    │  │ GOAP Planner    │  │ Cognition       │               │
│  │ Engine          │  │                 │  │ Pipeline        │               │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘               │
│                                                                               │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐               │
│  │ Expression      │  │ Action Handler  │  │ Memory          │               │
│  │ Evaluator       │  │ Registry        │  │ Integration     │               │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘               │
└───────────────────────────────────────────────────────────────────────────────┘
```

**Actor plugin routes events. Behavior plugin interprets them.**

### Execution Layer Model

The behavior system operates across four execution layers with different latency characteristics:

| Layer | Location | Latency | Use Case |
|-------|----------|---------|----------|
| **Event Brain** | Cloud | 100-500ms | Cinematic orchestration, QTE presentation, dramatic pacing |
| **Character Agent** | Cloud | 50-200ms | Tactical decisions, personality-informed choices, memory integration |
| **Local Runtime** | Client | <1ms | Frame-by-frame combat decisions, action selection |
| **Game Engine** | Client | Per-frame | Animation state machines, physics, collision response |

See [ABML_LOCAL_RUNTIME.md](./UPCOMING_-_ABML_LOCAL_RUNTIME.md) for client-side bytecode execution details.

---

## Part 1: Core Architecture

### 1.1 Component Overview

| Component | Responsibility | Dependencies |
|-----------|---------------|--------------|
| **ABML Parser** | YAML→AST transformation | YamlDotNet |
| **ABML Compiler** | AST→Executable behavior | Expression compiler |
| **ABML Executor** | Run compiled behaviors | Action handlers |
| **Expression Evaluator** | `${expr}` evaluation | Custom parser (Parlot) |
| **Action Handler Registry** | Extensible action execution | lib-mesh, lib-messaging |
| **GOAP Planner** | A* search over action space | Planning algorithms |
| **Goal Manager** | Track/prioritize agent goals | State management |
| **Cognition Pipeline** | Perception→Memory→Intention | Memory service integration |

### 1.2 ABML Runtime Engine

The ABML runtime follows a hybrid execution model (see UNIFIED_SCRIPTING_LANGUAGE_RESEARCH):

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                          ABML RUNTIME ENGINE                                  │
│                                                                               │
│  ┌───────────────┐    ┌───────────────┐    ┌───────────────┐                 │
│  │  YAML Parser  │───▶│   Compiler    │───▶│   Executor    │                 │
│  │  (YamlDotNet) │    │ (AST→Plan)    │    │ (Tree-walk)   │                 │
│  └───────────────┘    └───────────────┘    └───────────────┘                 │
│                             │                     │                           │
│                             ▼                     ▼                           │
│  ┌──────────────────────────────────────────────────────────────────────────┐│
│  │                    EXPRESSION EVALUATOR                                  ││
│  │                                                                          ││
│  │   ┌────────────┐    ┌────────────┐    ┌────────────┐                     ││
│  │   │   Lexer    │───▶│   Parser   │───▶│  Bytecode  │                     ││
│  │   │            │    │ (Parlot)   │    │    VM      │                     ││
│  │   └────────────┘    └────────────┘    └────────────┘                     ││
│  │                                                                          ││
│  │   Expression Cache: Compiled expressions reused across evaluations       ││
│  └──────────────────────────────────────────────────────────────────────────┘│
│                                                                               │
│  ┌──────────────────────────────────────────────────────────────────────────┐│
│  │                   ACTION HANDLER REGISTRY                                ││
│  │                                                                          ││
│  │   ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐           ││
│  │   │ Control │ │ Entity  │ │ Speech  │ │ Audio   │ │ Service │           ││
│  │   │ Flow    │ │ Actions │ │ Actions │ │ Actions │ │ Calls   │           ││
│  │   └─────────┘ └─────────┘ └─────────┘ └─────────┘ └─────────┘           ││
│  └──────────────────────────────────────────────────────────────────────────┘│
└──────────────────────────────────────────────────────────────────────────────┘
```

#### Execution Model Decision: Dual-Target Compilation

ABML compiles to **two different execution targets** based on use case:

| Target | Executor | Use Case | Performance |
|--------|----------|----------|-------------|
| **Cloud Interpreter** | Tree-walking `DocumentExecutor` | Cutscenes, dialogues, GOAP actions | 10-50μs/node |
| **Local Runtime** | Stack-based `BehaviorModelInterpreter` | Frame-by-frame combat decisions | <0.5ms total |

**Cloud Interpreter (this document)**:
- Tree-walking interpreter with bytecode expression VM
- Full ABML feature support (service calls, events, async)
- Hot-reload friendly (just reload YAML, rebuild AST)
- Used by Character Agent and Event Brain layers

**Local Runtime (see [ABML_LOCAL_RUNTIME.md](./UPCOMING_-_ABML_LOCAL_RUNTIME.md))**:
- Full bytecode compilation for client execution
- Allocation-free evaluation for 60fps combat
- SDK contains interpreter with zero ABML source dependency
- Used by Game Client layer

### 1.3 Behavior Types and Variants

Characters have multiple **behavior types**, and each type can have **variants** representing different styles:

```
Character Behaviors
├── combat (type)
│   ├── sword-and-shield (variant)
│   ├── dual-wield (variant)
│   └── unarmed (variant)
├── movement (type)
│   ├── standard (variant)
│   └── mounted (variant)
└── interaction (type)
    └── default (variant)
```

When multiple behavior types are active simultaneously, they output to **Intent Channels** (locomotion, action, attention, stance) with urgency values. An `IntentMerger` resolves conflicts - highest urgency wins for exclusive channels, blending for compatible channels.

See [ABML_LOCAL_RUNTIME.md](./UPCOMING_-_ABML_LOCAL_RUNTIME.md) Section 6.4 for full details on Intent Channels.

### 1.4 Multi-Channel Execution Model (Cutscenes)

> **Note**: "Multi-Channel" here refers to **cutscene synchronization channels** (camera, hero, audio), not the Intent Channels used for behavior coordination. These are distinct concepts.

For cutscenes, dialogues, and complex sequences, ABML v2.0 uses multi-channel parallel execution with named sync points:

```yaml
# Example: Multi-channel cutscene
channels:
  camera:
    - fade_in: { duration: 1s }
    - move_to: { shot: wide_throne_room, duration: 2s }
    - emit: establishing_complete
    - wait_for: @hero.at_mark
    - crane_up: { reveal: boss, duration: 3s }

  hero:
    - wait_for: @camera.establishing_complete
    - walk_to: { mark: hero_mark_1, speed: cautious }
    - emit: at_mark
    - speak: "Your reign ends today!"

  audio:
    - play: { track: ambient_throne_room, fade_in: 2s }
    - wait_for: @camera.establishing_complete
    - crossfade_to: { track: boss_theme, duration: 1s }
```

**Runtime State Model**:

```csharp
/// <summary>
/// Tracks execution state for a multi-channel ABML document.
/// </summary>
public class DocumentExecution
{
    /// <summary>
    /// Current state of each channel.
    /// </summary>
    public Dictionary<string, ChannelState> Channels { get; } = new();

    /// <summary>
    /// Sync points that have been emitted.
    /// </summary>
    public HashSet<string> EmittedSyncPoints { get; } = new();

    /// <summary>
    /// Channels waiting for specific sync points.
    /// </summary>
    public Dictionary<string, List<string>> WaitRegistry { get; } = new();

    /// <summary>
    /// Shared context variables across all channels.
    /// </summary>
    public Dictionary<string, object> Context { get; } = new();
}

/// <summary>
/// State of a single execution channel.
/// </summary>
public class ChannelState
{
    public int CurrentStepIndex { get; set; }
    public ChannelStatus Status { get; set; }
    public string? WaitingFor { get; set; }
}

public enum ChannelStatus
{
    Running,
    WaitingForSync,
    Complete,
    Branched
}
```

**Sync Patterns Supported**:

| Pattern | Syntax | Behavior |
|---------|--------|----------|
| Single wait | `wait_for: @channel.point` | Block until sync point emitted |
| Barrier (all) | `wait_for: [a_ready, b_ready]` | Block until all complete |
| Race (any) | `wait_for: { any_of: [...] }` | Continue when first completes |
| Timeout | `wait_for: { all_of: [...], timeout: 5s }` | Fallback if timeout reached |

**Deadlock Detection**:
```
Channel A waits for @B.ready
Channel B waits for @A.ready
→ DEADLOCK detected at compile time
```

---

## Part 2: GOAP Integration

### 2.1 What is GOAP?

**Goal-Oriented Action Planning** (GOAP) enables agents to plan sequences of actions to achieve goals, rather than following fixed behavior trees.

**Key Concepts**:

| Concept | Description | Example |
|---------|-------------|---------|
| **World State** | Current state of the world as key-value pairs | `{ hunger: 0.8, has_food: false, at_market: false }` |
| **Goal** | Desired world state | `{ hunger: < 0.3 }` |
| **Action** | Transform world state with preconditions and effects | Eat: requires `has_food=true`, effects `hunger -= 0.5` |
| **Plan** | Sequence of actions to reach goal from current state | `[GoToMarket, BuyFood, Eat]` |
| **Cost** | Numeric weight for action selection | Lower cost = preferred |

### 2.2 GOAP Annotations in ABML

GOAP metadata are **optional annotations** in ABML behaviors. This allows:
- ABML documents to work without GOAP (cutscenes, dialogues, dialplans)
- GOAP-aware consumers to extract planning metadata
- Single source of truth for behaviors and their GOAP properties

```yaml
version: "2.0"
metadata:
  id: "blacksmith_behaviors"
  type: "behavior"  # Enables GOAP planning

# Top-level goals
goals:
  meet_basic_needs:
    priority: 100
    conditions:
      energy: ">= 0.3"
      hunger: "<= 0.7"

  earn_income:
    priority: 80
    conditions:
      gold: ">= 50"

  maintain_reputation:
    priority: 60
    conditions:
      shop_reputation: ">= 0.7"

# Behaviors with GOAP annotations
flows:
  eat_meal:
    # GOAP metadata - optional, extracted by Behavior service
    goap:
      preconditions:
        hunger: "> 0.6"
        gold: ">= 5"
        at_location: "tavern"
      effects:
        hunger: "-0.8"
        gold: "-5"
      cost: 2

    # Core behavior - always executed
    actions:
      - walk_to: { location: "tavern" }
      - purchase: { item: "meal", cost: 5 }
      - animate: { animation: "eating", duration: 30 }
      - update_stat: { stat: "hunger", delta: -0.8 }

  forge_sword:
    goap:
      preconditions:
        forge_lit: true
        materials.iron: ">= 5"
        skill.blacksmithing: ">= 30"
      effects:
        inventory.swords: "+1"
        materials.iron: "-5"
        skill.blacksmithing: "+0.1"
        gold: "+20"  # When sold
      cost: 10

    actions:
      - verify_materials: { iron: 5 }
      - animate: { animation: "forging", duration: 60 }
      - create_item: { type: "sword", quality: "${skill.blacksmithing / 100}" }
      - update_inventory: { swords: "+1", iron: "-5" }
```

### 2.3 GOAP Planner Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                            GOAP PLANNER                                      │
│                                                                              │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │                        Goal Evaluation                                 │  │
│  │                                                                        │  │
│  │   Current Goals ──▶ Priority Sort ──▶ Unsatisfied Filter ──▶ Top Goal │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                                      │                                       │
│                                      ▼                                       │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │                         A* Search                                      │  │
│  │                                                                        │  │
│  │   World State ──────────────────────────────────────────▶ Goal State  │  │
│  │        │                                                      ▲       │  │
│  │        │         ┌─────────────────────────────┐              │       │  │
│  │        └────────▶│ Available Actions (ABML)    │──────────────┘       │  │
│  │                  │                             │                       │  │
│  │                  │ ┌───────┐ ┌───────┐ ┌───────┐                       │  │
│  │                  │ │ Eat   │ │ Forge │ │ Sleep │ ...                  │  │
│  │                  │ └───────┘ └───────┘ └───────┘                       │  │
│  │                  │                             │                       │  │
│  │                  │ Each action has:            │                       │  │
│  │                  │ - Preconditions             │                       │  │
│  │                  │ - Effects                   │                       │  │
│  │                  │ - Cost                      │                       │  │
│  │                  └─────────────────────────────┘                       │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                                      │                                       │
│                                      ▼                                       │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │                          Plan Output                                   │  │
│  │                                                                        │  │
│  │   [ GoToTavern, EatMeal ] ──▶ Execute sequentially via ABML Runtime   │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────────────┘
```

### 2.4 World State Representation

```csharp
/// <summary>
/// Represents the world state for GOAP planning.
/// Uses a flat key-value structure with dot-notation for nested access.
/// </summary>
public class WorldState
{
    private readonly Dictionary<string, float> _numericState = new();
    private readonly Dictionary<string, bool> _booleanState = new();
    private readonly Dictionary<string, string> _stringState = new();

    /// <summary>
    /// Gets a numeric value from world state.
    /// Supports dot-notation: "inventory.iron" → inventory subsystem, iron key.
    /// </summary>
    public float GetNumeric(string key, float defaultValue = 0f)
        => _numericState.GetValueOrDefault(key, defaultValue);

    /// <summary>
    /// Applies effects from a GOAP action to produce new world state.
    /// </summary>
    public WorldState ApplyEffects(GoapActionEffects effects)
    {
        var newState = Clone();
        foreach (var (key, delta) in effects.NumericDeltas)
        {
            newState._numericState[key] = GetNumeric(key) + delta;
        }
        foreach (var (key, value) in effects.BooleanSets)
        {
            newState._booleanState[key] = value;
        }
        return newState;
    }

    /// <summary>
    /// Checks if preconditions are satisfied.
    /// </summary>
    public bool SatisfiesPreconditions(GoapPreconditions preconditions)
    {
        foreach (var (key, condition) in preconditions.NumericConditions)
        {
            var value = GetNumeric(key);
            if (!condition.Evaluate(value))
                return false;
        }
        foreach (var (key, expected) in preconditions.BooleanConditions)
        {
            if (GetBoolean(key) != expected)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Calculates distance to goal state (heuristic for A*).
    /// </summary>
    public float DistanceToGoal(GoapGoal goal)
    {
        float distance = 0f;
        foreach (var (key, condition) in goal.Conditions)
        {
            var currentValue = GetNumeric(key);
            var targetValue = condition.TargetValue;
            distance += Math.Abs(currentValue - targetValue);
        }
        return distance;
    }
}
```

### 2.5 Planner Implementation

```csharp
/// <summary>
/// GOAP planner using A* search over action space.
/// </summary>
public interface IGoapPlanner
{
    /// <summary>
    /// Plans a sequence of actions to achieve the goal from current state.
    /// Returns null if no valid plan found within constraints.
    /// </summary>
    Task<GoapPlan?> PlanAsync(
        WorldState currentState,
        GoapGoal goal,
        IReadOnlyList<GoapAction> availableActions,
        PlanningOptions options,
        CancellationToken ct);
}

public class GoapPlanner : IGoapPlanner
{
    private readonly ILogger<GoapPlanner> _logger;
    private readonly int _maxSearchDepth;
    private readonly int _maxNodesExpanded;

    public async Task<GoapPlan?> PlanAsync(
        WorldState currentState,
        GoapGoal goal,
        IReadOnlyList<GoapAction> availableActions,
        PlanningOptions options,
        CancellationToken ct)
    {
        // A* search implementation
        var openSet = new PriorityQueue<PlanNode, float>();
        var closedSet = new HashSet<string>();

        var startNode = new PlanNode
        {
            State = currentState,
            Actions = new List<GoapAction>(),
            GCost = 0,
            HCost = currentState.DistanceToGoal(goal)
        };

        openSet.Enqueue(startNode, startNode.FCost);
        int nodesExpanded = 0;

        while (openSet.Count > 0 && nodesExpanded < _maxNodesExpanded)
        {
            ct.ThrowIfCancellationRequested();

            var current = openSet.Dequeue();
            nodesExpanded++;

            // Goal check
            if (current.State.SatisfiesGoal(goal))
            {
                _logger.LogDebug(
                    "Plan found with {ActionCount} actions after expanding {NodesExpanded} nodes",
                    current.Actions.Count,
                    nodesExpanded);

                return new GoapPlan
                {
                    Actions = current.Actions,
                    TotalCost = current.GCost,
                    GoalId = goal.Name
                };
            }

            var stateHash = current.State.GetHashCode().ToString();
            if (closedSet.Contains(stateHash))
                continue;
            closedSet.Add(stateHash);

            // Expand neighbors (applicable actions)
            foreach (var action in availableActions)
            {
                if (!current.State.SatisfiesPreconditions(action.Preconditions))
                    continue;

                var newState = current.State.ApplyEffects(action.Effects);
                var newNode = new PlanNode
                {
                    State = newState,
                    Actions = current.Actions.Append(action).ToList(),
                    GCost = current.GCost + action.Cost,
                    HCost = newState.DistanceToGoal(goal)
                };

                if (newNode.Actions.Count <= _maxSearchDepth)
                {
                    openSet.Enqueue(newNode, newNode.FCost);
                }
            }
        }

        _logger.LogWarning(
            "No plan found for goal {GoalName} after expanding {NodesExpanded} nodes",
            goal.Name,
            nodesExpanded);

        return null;
    }
}

public class PlanNode
{
    public WorldState State { get; init; } = null!;
    public List<GoapAction> Actions { get; init; } = new();
    public float GCost { get; init; }  // Cost so far
    public float HCost { get; init; }  // Heuristic to goal
    public float FCost => GCost + HCost;
}

public class GoapPlan
{
    public List<GoapAction> Actions { get; init; } = new();
    public float TotalCost { get; init; }
    public string GoalId { get; init; } = string.Empty;
}
```

### 2.6 Dynamic Replanning

GOAP plans must adapt to world changes:

```csharp
/// <summary>
/// Monitors plan validity and triggers replanning when needed.
/// </summary>
public class PlanExecutionMonitor
{
    private readonly IGoapPlanner _planner;
    private readonly IWorldStateProvider _worldStateProvider;

    /// <summary>
    /// Reasons that trigger replanning.
    /// </summary>
    public enum ReplanReason
    {
        ActionFailed,           // Current action couldn't complete
        PreconditionInvalidated,// Next action's preconditions no longer met
        BetterGoalAvailable,    // Higher priority goal became pursuable
        ExternalInterrupt,      // Event changed world state significantly
        PlanCompleted           // Current plan finished, need new goal
    }

    /// <summary>
    /// Checks if current plan is still valid.
    /// Called before each action execution.
    /// </summary>
    public async Task<PlanValidationResult> ValidatePlanAsync(
        GoapPlan currentPlan,
        int currentActionIndex,
        CancellationToken ct)
    {
        var worldState = await _worldStateProvider.GetCurrentStateAsync(ct);

        // Check if next action's preconditions are still met
        if (currentActionIndex < currentPlan.Actions.Count)
        {
            var nextAction = currentPlan.Actions[currentActionIndex];
            if (!worldState.SatisfiesPreconditions(nextAction.Preconditions))
            {
                return new PlanValidationResult
                {
                    IsValid = false,
                    Reason = ReplanReason.PreconditionInvalidated,
                    InvalidatedAtIndex = currentActionIndex
                };
            }
        }

        // Check if a higher priority goal became pursuable
        var higherPriorityGoal = await CheckForBetterGoalAsync(currentPlan.GoalId, worldState, ct);
        if (higherPriorityGoal != null)
        {
            return new PlanValidationResult
            {
                IsValid = false,
                Reason = ReplanReason.BetterGoalAvailable,
                NewGoal = higherPriorityGoal
            };
        }

        return new PlanValidationResult { IsValid = true };
    }
}
```

---

## Part 3: Cognition Pipeline

### 3.1 Overview

The cognition pipeline transforms raw perceptions into intentions and goals:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         COGNITION PIPELINE                                   │
│                                                                              │
│  Raw Perception                                                              │
│       │                                                                      │
│       ▼                                                                      │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │ 1. ATTENTION FILTER                                                     ││
│  │    - Budget allocation based on agent state                             ││
│  │    - Priority filtering (threats > novelty > routine)                   ││
│  │    - Capacity limiting (max perceptions per tick)                       ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│       │                                                                      │
│       ▼                                                                      │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │ 2. MEMORY INTEGRATION                                                   ││
│  │    - Query relevant memories                                            ││
│  │    - Context enrichment                                                 ││
│  │    - Recognition (known entities, places, patterns)                     ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│       │                                                                      │
│       ▼                                                                      │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │ 3. SIGNIFICANCE ASSESSMENT                                              ││
│  │    - Emotional impact scoring                                           ││
│  │    - Goal relevance evaluation                                          ││
│  │    - Relationship context                                               ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│       │                                                                      │
│       ▼                                                                      │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │ 4. MEMORY STORAGE                                                       ││
│  │    - Store significant experiences                                      ││
│  │    - Update relationship models                                         ││
│  │    - Consolidation scheduling                                           ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│       │                                                                      │
│       ▼                                                                      │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │ 5. INTENTION FORMATION                                                  ││
│  │    - Goal update based on perceptions                                   ││
│  │    - Urgency assessment                                                 ││
│  │    - Replan trigger decision                                            ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│       │                                                                      │
│       ▼                                                                      │
│  Trigger GOAP Replan (if needed)                                             │
└──────────────────────────────────────────────────────────────────────────────┘
```

### 3.2 ABML Representation of Cognition

The cognition pipeline can be expressed in ABML for customization per agent type:

```yaml
version: "2.0"
metadata:
  id: "npc_cognition_pipeline"
  type: "cognition"

flows:
  process_perception:
    triggers:
      - event: "perception.received"

    actions:
      # Stage 1: Attention filtering
      - filter_attention:
          input: "${perception.raw_data}"
          attention_budget: "${agent.attention_remaining}"
          priority_weights:
            threat: 10.0
            novelty: 5.0
            social: 3.0
            routine: 1.0
          result_variable: "filtered_perceptions"

      # Stage 2: Memory integration
      - service_call:
          service: "memory"
          method: "find-relevant"
          parameters:
            context: "${filtered_perceptions}"
            entity_id: "${agent.id}"
            limit: 10
          result_variable: "relevant_memories"

      # Stage 3: Significance assessment
      - for_each:
          variable: "perception"
          collection: "${filtered_perceptions}"
          do:
            - assess_significance:
                perception: "${perception}"
                memories: "${relevant_memories}"
                relationships: "${agent.relationships}"
                personality: "${agent.personality}"
                result_variable: "significance_score"

            # Stage 4: Memory storage (conditional)
            - cond:
                - when: "${significance_score > 0.7}"
                  then:
                    - service_call:
                        service: "memory"
                        method: "store-experience"
                        parameters:
                          entity_id: "${agent.id}"
                          experience: "${perception}"
                          significance: "${significance_score}"
                          context: "${relevant_memories}"

      # Stage 5: Intention formation
      - evaluate_goal_impact:
          perceptions: "${filtered_perceptions}"
          current_goals: "${agent.goals}"
          result_variable: "goal_updates"

      - cond:
          - when: "${goal_updates.requires_replan}"
            then:
              - trigger_goap_replan:
                  goals: "${goal_updates.affected_goals}"
                  urgency: "${goal_updates.urgency}"
```

### 3.3 Attention System

```csharp
/// <summary>
/// Attention allocation based on agent state and perception salience.
/// </summary>
public interface IAttentionSystem
{
    /// <summary>
    /// Filters perceptions based on attention budget and priority.
    /// </summary>
    Task<FilteredPerceptions> FilterAsync(
        RawPerceptions input,
        AttentionBudget budget,
        AttentionWeights weights,
        CancellationToken ct);
}

public class AttentionBudget
{
    /// <summary>
    /// Total attention units available this tick (affected by energy, stress, etc.)
    /// </summary>
    public float TotalUnits { get; init; }

    /// <summary>
    /// Maximum perceptions to process regardless of budget.
    /// </summary>
    public int MaxPerceptions { get; init; }

    /// <summary>
    /// Reserved attention for specific categories.
    /// </summary>
    public Dictionary<string, float> CategoryReservations { get; init; } = new();
}

public class AttentionWeights
{
    /// <summary>
    /// Priority multipliers by perception category.
    /// </summary>
    public float ThreatWeight { get; init; } = 10.0f;
    public float NoveltyWeight { get; init; } = 5.0f;
    public float SocialWeight { get; init; } = 3.0f;
    public float RoutineWeight { get; init; } = 1.0f;

    /// <summary>
    /// Dynamic adjustments based on agent state.
    /// </summary>
    public Dictionary<string, float> ContextualModifiers { get; init; } = new();
}
```

---

## Part 4: Expression Language

### 4.1 Syntax Design

The ABML expression language uses `${...}` for evaluation contexts:

```yaml
# Variable access
condition: "${npc.stats.energy > 0.5}"

# Dot-path navigation
value: "${npc.inventory.items[0].name}"

# Operators
check: "${a + b > c && d in allowed_list}"

# Function calls
distance: "${distance_to(target)}"
formatted: "${format('{0} gold', player.gold)}"

# Null safety
greeting: "${npc?.relationship[player.id]?.title ?? 'stranger'}"

# Ternary
result: "${health < 0.3 ? 'critical' : 'stable'}"
```

### 4.2 Expression Grammar (Parlot-based)

```csharp
/// <summary>
/// ABML expression parser using Parlot.
/// </summary>
public class AbmlExpressionParser
{
    // Grammar definition
    // expr         : ternary
    // ternary      : or ('?' expr ':' expr)?
    // or           : and ('||' and)*
    // and          : equality ('&&' equality)*
    // equality     : comparison (('==' | '!=') comparison)*
    // comparison   : addition (('<' | '<=' | '>' | '>=') addition)*
    // addition     : multiplication (('+' | '-') multiplication)*
    // multiplication : unary (('*' | '/') unary)*
    // unary        : ('!' | '-')? primary
    // primary      : literal | variable | function_call | '(' expr ')'
    // variable     : IDENTIFIER ('.' IDENTIFIER | '[' expr ']' | '?.')*
    // function_call: IDENTIFIER '(' (expr (',' expr)*)? ')'
    // literal      : NUMBER | STRING | BOOLEAN | NULL

    private readonly Parser<AbmlExpression> _parser;
    private readonly ConcurrentDictionary<string, CompiledExpression> _cache = new();

    public AbmlExpressionParser()
    {
        _parser = BuildParser();
    }

    public T Evaluate<T>(string expression, IVariableScope scope)
    {
        var compiled = _cache.GetOrAdd(expression, Compile);
        return compiled.Execute<T>(scope);
    }

    private CompiledExpression Compile(string expression)
    {
        var ast = _parser.Parse(expression);
        return new BytecodeCompiler().Compile(ast);
    }
}
```

### 4.3 Built-in Functions

| Function | Description | Example |
|----------|-------------|---------|
| `distance_to(target)` | Distance to entity | `${distance_to(player) < 10}` |
| `has_item(item_id)` | Check inventory | `${has_item('sword')}` |
| `random(min, max)` | Random number | `${random(1, 100) > 50}` |
| `now()` | Current timestamp | `${now()}` |
| `format(template, ...)` | String formatting | `${format('{0} coins', gold)}` |
| `length(collection)` | Collection size | `${length(inventory.items) > 0}` |
| `contains(collection, item)` | Membership check | `${contains(skills, 'blacksmithing')}` |
| `min(a, b)` / `max(a, b)` | Math functions | `${min(health, 1.0)}` |
| `clamp(val, min, max)` | Range limiting | `${clamp(damage, 0, 100)}` |

### 4.4 Template Syntax (Fluid/Liquid)

For text output (dialogue, narration), use `{{ }}` Liquid syntax via Fluid:

```yaml
speak:
  text: "Hello {{ customer.name | capitalize }}! That will be {{ price }} gold."
```

### 4.5 Variable Scopes

Per [ABML Guide Section 11](../guides/ABML.md#11-context-and-variables):

| Scope | Lifetime | Access | Bannou Implementation |
|-------|----------|--------|----------------------|
| `local` | Current flow execution | `${variable}` | In-memory execution context |
| `document` | Document lifetime | `${document.variable}` | In-memory, lifetime of execution |
| `entity` | Entity lifetime | `${entity.property}` | `IStateStore<T>` with entity key prefix |
| `world` | Persistent world state | `${world.property}` | `IStateStore<T>` with world state key prefix |

```csharp
/// <summary>
/// Variable scope implementation for ABML execution.
/// </summary>
public interface IVariableScope
{
    /// <summary>
    /// Gets a value by path, respecting scope prefixes.
    /// </summary>
    object? GetValue(string path);

    /// <summary>
    /// Sets a value. Only local and document scopes are writable.
    /// </summary>
    void SetValue(string path, object? value);

    /// <summary>
    /// Creates a child scope (for flow calls).
    /// </summary>
    IVariableScope CreateChildScope();
}

public class ExecutionScope : IVariableScope
{
    private readonly Dictionary<string, object?> _locals = new();
    private readonly DocumentScope _documentScope;
    private readonly IEntityStateProvider _entityProvider;
    private readonly IWorldStateProvider _worldProvider;

    public object? GetValue(string path)
    {
        if (path.StartsWith("document."))
            return _documentScope.GetValue(path[9..]);
        if (path.StartsWith("entity."))
            return _entityProvider.GetValue(path[7..]);
        if (path.StartsWith("world."))
            return _worldProvider.GetValue(path[6..]);

        // Default to local scope
        return _locals.GetValueOrDefault(path);
    }
}
```

---

## Part 5: Action Handler Registry

### 5.1 Handler Interface

Per [ABML Guide Section 7.5](../guides/ABML.md#75-handler-contract), actions follow this contract:

| Execution Mode | ABML Syntax | Handler Behavior |
|----------------|-------------|------------------|
| Fire-and-forget | `- action: {...}` | Execute, return immediately |
| Await completion | `- action: {..., await: completion}` | Execute, signal when done |

```csharp
/// <summary>
/// Handles execution of a specific action type in ABML.
/// </summary>
public interface IActionHandler
{
    /// <summary>
    /// Action type identifier (e.g., "speak", "animate", "service_call").
    /// </summary>
    string ActionType { get; }

    /// <summary>
    /// Executes the action.
    /// </summary>
    /// <param name="action">Action definition from ABML</param>
    /// <param name="context">Execution context with variables and scope</param>
    /// <param name="awaitCompletion">True if ABML specified `await: completion`</param>
    /// <param name="ct">Cancellation token</param>
    Task<ActionResult> ExecuteAsync(
        ActionDefinition action,
        ExecutionContext context,
        bool awaitCompletion,
        CancellationToken ct);
}

public class ActionResult
{
    public bool Success { get; init; }
    public object? ReturnValue { get; init; }
    public string? ErrorMessage { get; init; }
    public ActionResultType ResultType { get; init; }
}

public enum ActionResultType
{
    Completed,          // Action finished successfully
    Started,            // Fire-and-forget: action dispatched, not awaited
    Failed,             // Action failed (triggers on_error if defined)
    Skipped             // Action skipped due to condition
}
```

### 5.2 Action Categories

Per [ABML Guide Section 7](../guides/ABML.md#7-actions):

| Category | Actions | Handler | Notes |
|----------|---------|---------|-------|
| **Control Flow** | `cond`, `for_each`, `repeat`, `goto`, `call`, `return`, `branch`, `emit`, `wait_for`, `parallel` | `ControlFlowHandler` | Built-in, no external dispatch |
| **Variables** | `set`, `increment`, `decrement`, `clear` | `VariableHandler` | Built-in |
| **Service** | `service_call`, `publish` | `ServiceCallHandler` | Uses lib-mesh/lib-messaging |
| **Entity** | `animate`, `move_to`, `look_at`, `spawn`, `despawn` | `EntityActionHandler` | Domain-specific |
| **Speech** | `speak`, `narrate`, `choice` | `SpeechHandler` | Domain-specific |
| **Audio** | `audio.play`, `audio.stop`, `audio.sfx` | `AudioHandler` | Domain-specific |
| **Camera** | `camera.cut_to`, `camera.pan`, `camera.shake` | `CameraHandler` | Domain-specific |
| **GOAP** | `trigger_goap_replan`, `update_goal` | `GoapActionHandler` | Behavior plugin internal |
| **Memory** | `remember`, `query_memory` | `MemoryHandler` | Memory service integration |

**Note**: `goto` = flow transfer (within `flows:`), `branch` = channel transfer (within `channels:`). See [ABML Guide Section 5](../guides/ABML.md#5-control-flow).

### 5.3 Error Handling

Per [ABML Guide Section 12](../guides/ABML.md#12-error-handling), errors can be handled at two levels:

**Action-level** (`on_error` block):
```yaml
- service_call:
    service: economy_service
    method: transfer_gold
    on_error:
      - log: { message: "Transfer failed: ${error.message}" }
      - goto: { flow: "handle_failed_transaction" }
```

**Document-level** (global error handlers):
```yaml
errors:
  service_unavailable:
    - speak: { text: "I can't help you right now." }
    - end_interaction
```

```csharp
/// <summary>
/// Error context passed to error handlers.
/// </summary>
public class ActionError
{
    public string ActionType { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? ErrorCode { get; init; }
    public Exception? Exception { get; init; }
    public Dictionary<string, object?> Details { get; init; } = new();
}

/// <summary>
/// Error handler execution in the ABML executor.
/// </summary>
public async Task<bool> TryHandleErrorAsync(
    ActionError error,
    ActionDefinition action,
    ExecutionContext context,
    CancellationToken ct)
{
    // 1. Check for action-level on_error
    if (action.Parameters.TryGetValue("on_error", out var onError))
    {
        context.SetVariable("error", error);
        await ExecuteActionsAsync((List<ActionDefinition>)onError, context, ct);
        return true;
    }

    // 2. Check for document-level error handler
    var errorType = error.ErrorCode ?? "unknown";
    if (context.Document.ErrorHandlers.TryGetValue(errorType, out var handler))
    {
        context.SetVariable("error", error);
        await ExecuteActionsAsync(handler, context, ct);
        return true;
    }

    // 3. No handler - propagate error
    return false;
}
```

### 5.4 Publish Action Handler

Per [ABML Guide Appendix A](../guides/ABML.md#appendix-a-bannou-implementation-requirements), ABML `publish` actions with inline payloads must be mapped to typed events:

```yaml
# ABML allows inline payloads
- publish:
    topic: "npc.state_changed"
    payload:
      npc_id: "${npc.id}"
      new_state: "${current_state}"
```

```csharp
/// <summary>
/// Maps ABML publish payloads to typed events.
/// Inline payloads are forbidden in Bannou - must use typed event models.
/// </summary>
public class PublishActionHandler : IActionHandler
{
    private readonly IMessageBus _messageBus;
    private readonly IEventTypeRegistry _eventRegistry;

    public string ActionType => "publish";

    public async Task<ActionResult> ExecuteAsync(
        ActionDefinition action,
        ExecutionContext context,
        bool awaitCompletion,
        CancellationToken ct)
    {
        var topic = context.ResolveString(action.Parameters["topic"]);
        var payload = context.ResolveObject(action.Parameters["payload"]);

        // Map to typed event (required by Tenet 5 / MassTransit)
        var typedEvent = _eventRegistry.CreateTypedEvent(topic, payload)
            ?? throw new InvalidOperationException(
                $"No typed event registered for topic '{topic}'. " +
                "ABML publish actions must map to typed events in Bannou.");

        await _messageBus.PublishAsync(topic, typedEvent, cancellationToken: ct);

        return new ActionResult { Success = true, ResultType = ActionResultType.Completed };
    }
}
```

### 5.5 Service Call Handler Example

```csharp
/// <summary>
/// Handles service_call actions in ABML.
/// </summary>
public class ServiceCallHandler : IActionHandler
{
    private readonly IMeshInvocationClient _meshClient;
    private readonly ILogger<ServiceCallHandler> _logger;

    public string ActionType => "service_call";

    public async Task<ActionResult> ExecuteAsync(
        ActionDefinition action,
        ExecutionContext context,
        CancellationToken ct)
    {
        var serviceName = context.ResolveString(action.Parameters["service"]);
        var methodName = context.ResolveString(action.Parameters["method"]);

        object? parameters = null;
        if (action.Parameters.TryGetValue("parameters", out var paramsObj))
        {
            parameters = context.ResolveObject(paramsObj);
        }

        try
        {
            var result = await _meshClient.InvokeMethodAsync<object, object>(
                serviceName,
                methodName,
                parameters,
                ct);

            // Store result if result_variable specified
            if (action.Parameters.TryGetValue("result_variable", out var varName))
            {
                context.SetVariable(varName.ToString()!, result);
            }

            return new ActionResult
            {
                Success = true,
                ReturnValue = result,
                ResultType = ActionResultType.Completed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Service call failed: {Service}.{Method}",
                serviceName, methodName);

            // Check for on_error handler
            if (action.Parameters.TryGetValue("on_error", out var errorHandler))
            {
                return new ActionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ResultType = ActionResultType.Failed
                };
            }

            throw;
        }
    }
}
```

---

## Part 6: Actor Integration

> **Note**: For authoritative actor architecture, see [ACTORS_PLUGIN_V3.md](./UPCOMING_-_ACTORS_PLUGIN_V3.md).

### 6.1 The Actor Paradigm: State Updates, Not Direct Control

**Critical insight**: Actors provide **flavor, not foundation**. Characters have massive, self-sufficient behavior stacks that handle every situation. Actors influence characters by updating **state inputs** that the behavior stack reads.

| Actor Output | Frequency | What It Does |
|--------------|-----------|--------------|
| **State Updates** (primary) | Every few ticks | Updates feelings, goals, memories - read by behavior stack |
| **Behavior Changes** (secondary) | Rare | Learning/growth - adds or swaps behavior variants |

**The flow**: Actor → emits STATE → Behavior Stack reads state → EmitIntent → IntentChannels → Character acts

### 6.2 NPC Brain Actor Using Behavior Plugin

The NPC brain actor demonstrates the Actor+Behavior integration with state-update paradigm:

```csharp
/// <summary>
/// NPC cognitive processor actor.
/// Receives perceptions, processes through cognition, emits STATE UPDATES.
/// Does NOT directly control characters - influences via state updates.
/// </summary>
public partial class NpcBrainActor : NpcBrainActorBase
{
    private readonly IDocumentExecutor _executor;  // Tree-walking for cognition
    private readonly IGoapPlanner _goapPlanner;
    private readonly IActorStateEmitter _stateEmitter;

    // State from actor plugin (persisted automatically)
    // - State.Feelings (Dictionary<string, double>)
    // - State.Goals (GoalState)
    // - State.Memories (List<Memory>)

    public override async Task HandlePerceptionAsync(
        PerceptionEvent perception,
        CancellationToken ct)
    {
        // 1. Run cognition pipeline via tree-walking executor (NOT bytecode)
        // This is the hot-reloadable, debuggable path for NPC decision-making
        var result = await _executor.ExecuteAsync(
            _cognitionBehavior,
            "process_perception",
            _scope.WithPerception(perception),
            ct);

        // 2. PRIMARY OUTPUT: Emit state updates to Game Server
        // The character's behavior stack reads these and responds accordingly
        if (result.StateUpdates is { } updates)
        {
            await _stateEmitter.EmitStateUpdateAsync(
                new ActorStateUpdate
                {
                    CharacterId = CharacterId,
                    ActorId = ActorId,
                    Timestamp = DateTimeOffset.UtcNow,
                    Feelings = updates.Feelings,      // e.g., { angry: 0.9 }
                    Goals = updates.Goals,            // e.g., { target: entityId }
                    Memories = updates.NewMemories    // e.g., [betrayed_by: X]
                },
                ct);
        }

        // 3. SECONDARY OUTPUT (rare): Behavior composition changes
        // Only for learning/growth over time, not moment-to-moment control
        if (result.BehaviorChange is { } change)
        {
            await _stateEmitter.EmitStateUpdateAsync(
                new ActorStateUpdate
                {
                    CharacterId = CharacterId,
                    ActorId = ActorId,
                    Timestamp = DateTimeOffset.UtcNow,
                    BehaviorChange = change  // e.g., { added: ["learned_technique_v2"] }
                },
                ct);
        }
    }
}
```

**Key differences from earlier design:**
- **No `actor.objective.*` events** - actors emit state updates, not objectives
- **Behavior stack handles action selection** - character already knows how to fight, flee, etc.
- **Actor provides context** - feelings, goals, memories influence HOW behavior stack responds
- **Tree-walking executor** - cognition uses debuggable interpreter, not bytecode

### 6.3 State Flow: Actor to Character

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    ACTOR → BEHAVIOR STACK FLOW                               │
│                                                                              │
│  ┌────────────────────┐                     ┌────────────────────┐          │
│  │    NPC ACTOR       │                     │  GAME SERVER       │          │
│  │  (Pool Node)       │                     │  (Stride)          │          │
│  │                    │                     │                    │          │
│  │  Cognition:        │   ActorStateUpdate  │  Character's       │          │
│  │  - filter_attention│──────────────────►│  Behavior Stack:   │          │
│  │  - assess_signif.  │                     │                    │          │
│  │  - query_memory    │   Feelings:         │  READS state as    │          │
│  │  - store_memory    │   angry=0.9         │  input variables:  │          │
│  │  - evaluate_goals  │   Goals:            │  - feelings.angry  │          │
│  │                    │   target=entityX    │  - goals.target    │          │
│  │  Produces STATE    │   Memories:         │  - memories.*      │          │
│  │  (not commands)    │   betrayed_by[X]    │                    │          │
│  └────────────────────┘                     │  PRODUCES intents: │          │
│                                             │  - EmitIntent(     │          │
│                                             │    action=attack,  │          │
│                                             │    urgency=0.9)    │          │
│                                             └────────────────────┘          │
│                                                                              │
│  WITHOUT ACTOR: Behavior stack still works, characters just don't change.   │
└──────────────────────────────────────────────────────────────────────────────┘
```

### 6.4 Behavior-Actor Communication Patterns

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    ACTOR ←→ BEHAVIOR COMMUNICATION                           │
│                                                                              │
│  ┌────────────────────┐                     ┌────────────────────┐          │
│  │    NPC ACTOR       │                     │  BEHAVIOR SERVICE  │          │
│  │                    │                     │                    │          │
│  │  Receives:         │   lib-messaging     │  Provides:         │          │
│  │  - perception.*    │◄───────────────────▶│  - ABML compilation│          │
│  │  - game.time.tick  │   (direct sub)      │  - GOAP planning   │          │
│  │                    │                     │  - Cognition proc. │          │
│  │  State:            │                     │                    │          │
│  │  - Feelings        │   IGoapPlanner      │  Caches:           │          │
│  │  - Goals           │   (direct DI)       │  - Compiled ABML   │          │
│  │  - Memories        │                     │  - Expression eval │          │
│  │                    │                     │                    │          │
│  │  Emits:            │   lib-messaging     │  Storage:          │          │
│  │  - state updates   │──────────────────►│  - lib-asset       │          │
│  │    (primary)       │                     │    (bytecode)      │          │
│  │  - behavior change │                     │                    │          │
│  │    (secondary)     │                     │                    │          │
│  └────────────────────┘                     └────────────────────┘          │
│                                                                              │
└──────────────────────────────────────────────────────────────────────────────┘
```

---

## Part 7: API Schema Extensions

### 7.1 New Endpoints

The existing behavior-api.yaml needs these additions:

```yaml
# Additional endpoints for behavior-api.yaml

paths:
  /goap/plan:
    post:
      summary: Generate GOAP plan
      description: |
        Plans a sequence of actions to achieve the goal from current state.
        Uses A* search over available ABML actions with GOAP annotations.
      operationId: GenerateGoapPlan
      tags:
        - GOAP
      x-permissions:
        - role: user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GoapPlanRequest'
      responses:
        '200':
          description: Plan generated successfully
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/GoapPlanResponse'

  /goap/validate-plan:
    post:
      summary: Validate existing plan
      description: |
        Checks if a plan is still valid given current world state.
        Returns validation result and reason if invalid.
      operationId: ValidateGoapPlan
      tags:
        - GOAP
      x-permissions:
        - role: user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ValidatePlanRequest'
      responses:
        '200':
          description: Validation result
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ValidatePlanResponse'

  /cognition/process:
    post:
      summary: Process perception through cognition pipeline
      description: |
        Runs a perception through the full cognition pipeline:
        attention → memory → significance → storage → intention.
      operationId: ProcessCognition
      tags:
        - Cognition
      x-permissions:
        - role: user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ProcessCognitionRequest'
      responses:
        '200':
          description: Cognition result
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ProcessCognitionResponse'

  /execute/action:
    post:
      summary: Execute single ABML action
      description: |
        Executes a single action from a compiled behavior.
        Used for step-by-step plan execution.
      operationId: ExecuteAction
      tags:
        - Execution
      x-permissions:
        - role: user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ExecuteActionRequest'
      responses:
        '200':
          description: Execution result
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ExecuteActionResponse'

  /execute/flow:
    post:
      summary: Execute ABML flow
      description: |
        Executes a complete ABML flow (behavior, dialogue, cutscene).
        Returns execution ID for tracking async completion.
      operationId: ExecuteFlow
      tags:
        - Execution
      x-permissions:
        - role: user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ExecuteFlowRequest'
      responses:
        '202':
          description: Execution started
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ExecuteFlowResponse'

components:
  schemas:
    GoapPlanRequest:
      type: object
      required:
        - agent_id
        - goal
        - world_state
        - behavior_id
      properties:
        agent_id:
          type: string
          description: Agent requesting the plan
        goal:
          $ref: '#/components/schemas/GoapGoal'
        world_state:
          type: object
          additionalProperties: true
          description: Current world state as key-value pairs
        behavior_id:
          type: string
          description: Compiled behavior containing available actions
        options:
          $ref: '#/components/schemas/PlanningOptions'

    GoapPlanResponse:
      type: object
      required:
        - success
      properties:
        success:
          type: boolean
        plan:
          $ref: '#/components/schemas/GoapPlan'
        planning_time_ms:
          type: integer
        nodes_expanded:
          type: integer
        failure_reason:
          type: string

    GoapPlan:
      type: object
      required:
        - actions
        - total_cost
        - goal_id
      properties:
        actions:
          type: array
          items:
            $ref: '#/components/schemas/PlannedAction'
        total_cost:
          type: number
        goal_id:
          type: string

    PlannedAction:
      type: object
      required:
        - action_id
        - name
        - cost
      properties:
        action_id:
          type: string
        name:
          type: string
        cost:
          type: number
        estimated_duration_seconds:
          type: integer

    PlanningOptions:
      type: object
      properties:
        max_depth:
          type: integer
          default: 10
        max_nodes:
          type: integer
          default: 1000
        timeout_ms:
          type: integer
          default: 100

    ValidatePlanRequest:
      type: object
      required:
        - plan
        - current_action_index
        - world_state
      properties:
        plan:
          $ref: '#/components/schemas/GoapPlan'
        current_action_index:
          type: integer
        world_state:
          type: object
          additionalProperties: true

    ValidatePlanResponse:
      type: object
      required:
        - is_valid
      properties:
        is_valid:
          type: boolean
        reason:
          type: string
          enum:
            - action_failed
            - precondition_invalidated
            - better_goal_available
            - external_interrupt
            - plan_completed
        invalidated_at_index:
          type: integer
        suggested_action:
          type: string
          enum:
            - continue
            - replan
            - abort

    ProcessCognitionRequest:
      type: object
      required:
        - agent_id
        - perception
      properties:
        agent_id:
          type: string
        perception:
          type: object
          additionalProperties: true
        current_state:
          type: object
          additionalProperties: true
        current_goals:
          type: array
          items:
            $ref: '#/components/schemas/GoapGoal'

    ProcessCognitionResponse:
      type: object
      required:
        - processed_perceptions
      properties:
        processed_perceptions:
          type: integer
        attention_remaining:
          type: number
        memories_stored:
          type: array
          items:
            type: string
        requires_replan:
          type: boolean
        updated_goals:
          type: array
          items:
            $ref: '#/components/schemas/GoapGoal'
        significance_scores:
          type: object
          additionalProperties:
            type: number

    ExecuteActionRequest:
      type: object
      required:
        - agent_id
        - action_id
      properties:
        agent_id:
          type: string
        action_id:
          type: string
        context:
          type: object
          additionalProperties: true
        timeout_ms:
          type: integer
          default: 30000

    ExecuteActionResponse:
      type: object
      required:
        - success
      properties:
        success:
          type: boolean
        result:
          type: object
          additionalProperties: true
        error_message:
          type: string
        execution_time_ms:
          type: integer

    ExecuteFlowRequest:
      type: object
      required:
        - agent_id
        - flow_id
      properties:
        agent_id:
          type: string
        flow_id:
          type: string
        behavior_id:
          type: string
        context:
          type: object
          additionalProperties: true
        async:
          type: boolean
          default: true

    ExecuteFlowResponse:
      type: object
      required:
        - execution_id
      properties:
        execution_id:
          type: string
        status:
          type: string
          enum:
            - started
            - running
            - completed
            - failed
```

---

## Part 8: Implementation Roadmap

### Phase 1: Core ABML Runtime (Foundation) - COMPLETE

**Goal**: Parse, compile, and execute basic ABML documents.

**Status**: COMPLETE - 585 tests passing. See [ABML Guide](../guides/ABML.md) for full documentation.

- [x] ABML Parser (YamlDotNet-based)
  - [x] Document structure validation
  - [x] Error reporting with line numbers
  - [x] Schema version checking

- [x] Expression Evaluator
  - [x] Lexer and parser (Parlot)
  - [x] Bytecode compiler (register-based VM)
  - [x] Expression cache

- [x] Basic Action Handlers
  - [x] Control flow (cond, for_each, goto, call, return)
  - [x] Variable operations (set, increment)
  - [x] Error handling (on_error, _error_handled)

- [x] ABML Executor
  - [x] Tree-walking interpreter
  - [x] Variable scope management (local, document, entity, world)
  - [x] Multi-channel execution with sync points
  - [x] Deadlock detection

**Tests**: 585 unit tests covering parser, expression evaluator, executor, channels, and error handling.

### Phase 2: GOAP Integration - COMPLETE ✅

**Goal**: Extract GOAP annotations and plan action sequences.

**Status**: COMPLETE - All components implemented and tested.

- [x] GOAP Metadata Extractor
  - [x] Parse goap: blocks from ABML (GoapMetadataConverter)
  - [x] Build action graph
  - [x] GOAP metadata caching at compile time (BehaviorBundleManager)

- [x] World State Model
  - [x] Key-value state representation (WorldState.cs - immutable)
  - [x] Precondition evaluation (GoapPreconditions, GoapCondition)
  - [x] Effect application (GoapActionEffects with deltas/absolutes)

- [x] A* Planner
  - [x] Priority queue implementation (PriorityQueue<PlanNode, float>)
  - [x] Heuristic function (DistanceToGoal)
  - [x] Plan optimization (cost-based ordering)

- [x] Plan Validation
  - [x] Precondition checking (ValidateGoapPlanAsync)
  - [x] Goal reachability
  - [x] Replan reasons (action_failed, precondition_invalidated, better_goal_available)

- [x] BehaviorService Integration
  - [x] /goap/plan endpoint (GenerateGoapPlanAsync)
  - [x] /goap/validate-plan endpoint (ValidateGoapPlanAsync)
  - [x] HTTP integration tests

**Tests**: 672 lines of GOAP tests in lib-behavior.tests/Goap/ plus HTTP integration tests.

### Phase 3: Multi-Channel Execution - COMPLETE

**Goal**: Support parallel execution with sync points.

**Status**: COMPLETE - Included in Phase 1 implementation. See [ABML Guide Section 7](../guides/ABML.md#7-channels-and-parallelism) and [Section 3: Document Composition](../guides/ABML.md#3-document-composition).

- [x] Channel Executor
  - [x] Cooperative round-robin scheduling
  - [x] Sync point registry
  - [x] Emit/wait_for mechanics

- [x] Barrier Synchronization
  - [x] All-of waiting
  - [x] Any-of racing
  - [x] Channel scope isolation

- [x] Deadlock Detection
  - [x] Runtime deadlock detection
  - [x] Clear error reporting

- [x] Document Composition
  - [x] Import resolution (DocumentLoader, IDocumentResolver, LoadedDocument)
  - [x] Context passing (ExecutionContext.TryResolveFlow, namespaced flow references)
  - [x] Context-relative resolution (imported documents resolve flows relative to their own imports)
  - [x] FileSystemDocumentResolver for production use with path traversal protection
  - [x] Relative path resolution (`./sibling.yml`, `../parent/file.yml`)
  - [x] Goto to imported flow (direct and from nested calls)
  - [x] Variable scope isolation across imports
  - [x] Schema-only imports (skipped during document loading)
  - [x] Circular import detection

**Tests**: 15+ multi-channel tests covering sync points, barriers, and deadlock detection; 31 document loader tests covering import resolution, circular detection, namespaced flow execution, context-relative resolution, goto across imports, relative paths, variable scope isolation, and file system loading.

### Phase 4: Cognition Pipeline - PARTIAL

**Goal**: Perception processing and goal formation.

**Status**: PARTIAL - Individual handlers implemented in `lib-behavior/Handlers/`. Pipeline orchestration and integration tests pending.

**Implemented Components** (`lib-behavior/Handlers/` and `lib-behavior/Cognition/`):
- [x] Attention System
  - [x] `FilterAttentionHandler.cs` - Priority-based filtering
  - [x] `AttentionBudget`, `AttentionWeights` types in `CognitionTypes.cs`
  - [ ] Full budget allocation integration

- [x] Memory Integration
  - [x] `QueryMemoryHandler.cs` - Memory retrieval
  - [x] `StoreMemoryHandler.cs` - Memory persistence
  - [x] `IMemoryStore` interface + `ActorLocalMemoryStore` implementation
  - [ ] Context enrichment patterns

- [x] Significance Assessment
  - [x] `AssessSignificanceHandler.cs` - Significance scoring
  - [x] `SignificanceWeights`, `SignificanceScore` types
  - [ ] Full emotional impact integration

- [x] Intention Formation
  - [x] `EvaluateGoalImpactHandler.cs` - Goal impact assessment
  - [x] `TriggerGoapReplanHandler.cs` - Replan triggering
  - [x] `GoalImpactResult` type
  - [ ] Urgency assessment integration

**Pending**:
- [ ] `CognitionPipeline` orchestrator class (chains handlers together)
- [ ] `PerceptionEvent` type definition
- [ ] End-to-end integration tests
- [ ] BehaviorService API endpoints for cognition

**Tests**: Handler unit tests exist. End-to-end cognition tests pending.

### Phase 5: Actor Integration

**Goal**: Seamless NPC brain actor using Behavior plugin.

- [ ] Behavior Client
  - [ ] Generated client usage
  - [ ] Async patterns

- [ ] NPC Brain Actor Type
  - [ ] Schema definition
  - [ ] State model
  - [ ] Message handlers

- [ ] Integration Patterns
  - [ ] Perception routing
  - [ ] Plan execution
  - [ ] Objective emission

**Tests**: Full NPC lifecycle with actor + behavior integration.

---

## Part 9: Configuration

### 9.1 Configuration Schema

```yaml
# schemas/behavior-configuration.yaml (extended)
x-service-configuration:
  properties:
    # Existing properties...

    # GOAP Planner settings
    GoapMaxSearchDepth:
      type: integer
      default: 10
      description: Maximum actions in a plan
      env: BEHAVIOR_GOAP_MAX_SEARCH_DEPTH

    GoapMaxNodesExpanded:
      type: integer
      default: 1000
      description: Maximum A* nodes to expand
      env: BEHAVIOR_GOAP_MAX_NODES_EXPANDED

    GoapPlanningTimeoutMs:
      type: integer
      default: 100
      description: Maximum time for planning
      env: BEHAVIOR_GOAP_PLANNING_TIMEOUT_MS

    # Expression cache settings
    ExpressionCacheMaxSize:
      type: integer
      default: 10000
      description: Maximum cached expressions
      env: BEHAVIOR_EXPRESSION_CACHE_MAX_SIZE

    ExpressionCacheTtlMinutes:
      type: integer
      default: 60
      description: Cache entry TTL
      env: BEHAVIOR_EXPRESSION_CACHE_TTL_MINUTES

    # Cognition settings
    CognitionAttentionBudgetDefault:
      type: number
      default: 100.0
      description: Default attention units per tick
      env: BEHAVIOR_COGNITION_ATTENTION_BUDGET_DEFAULT

    CognitionMaxPerceptionsPerTick:
      type: integer
      default: 10
      description: Maximum perceptions processed per tick
      env: BEHAVIOR_COGNITION_MAX_PERCEPTIONS_PER_TICK

    CognitionSignificanceThreshold:
      type: number
      default: 0.7
      description: Threshold for memory storage
      env: BEHAVIOR_COGNITION_SIGNIFICANCE_THRESHOLD

    # Multi-channel execution
    MultiChannelMaxConcurrentChannels:
      type: integer
      default: 20
      description: Maximum concurrent channels per execution
      env: BEHAVIOR_MULTICHANNEL_MAX_CONCURRENT

    MultiChannelSyncTimeoutMs:
      type: integer
      default: 30000
      description: Default sync point timeout
      env: BEHAVIOR_MULTICHANNEL_SYNC_TIMEOUT_MS
```

---

## Part 10: .NET Library Dependencies

| Purpose | Library | Version | License |
|---------|---------|---------|---------|
| YAML Parsing | YamlDotNet | 16.3.0 | MIT |
| Expression Parsing | Parlot | 1.0.0 | MIT |
| Template Rendering | Fluid | 2.7.0 | MIT |
| State Machine | Stateless | 5.20.0 | Apache-2.0 |
| Priority Queue | System.Collections | Built-in | MIT |

All dependencies use permissive licenses per Tenet 18.

---

## Appendix A: Document Type Summary

| Type | GOAP | Channels | Deterministic | Use Case |
|------|------|----------|---------------|----------|
| `behavior` | Yes | Optional | No | NPC autonomous behavior |
| `cutscene` | No | Primary | Yes (seeded) | Choreographed sequences |
| `dialogue` | No | Optional | Player-driven | Conversations |
| `dialplan` | No | Some | Event-driven | Call routing |
| `cognition` | No | No | No | Perception processing |
| `timeline` | No | Primary | Yes | Time-based animations |

---

## Appendix B: Glossary

| Term | Definition |
|------|------------|
| **ABML** | Arcadia Behavior Markup Language - YAML-based DSL for agent behaviors |
| **GOAP** | Goal-Oriented Action Planning - AI technique for dynamic action selection |
| **World State** | Key-value representation of current game/simulation state |
| **Channel** | Independent execution track in multi-channel ABML |
| **Sync Point** | Named marker for cross-channel synchronization |
| **Cognition Pipeline** | Perception → Memory → Intention processing flow |
| **Attention Budget** | Limited resource for perception processing |
| **Plan Validation** | Checking if planned actions are still executable |

---

*Document created: 2024-12-28*
*This is the v2 design for the Behavior Plugin, integrating ABML runtime and GOAP planning.*
