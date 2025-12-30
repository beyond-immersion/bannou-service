# GOAP Implementation - First Steps

> **Status**: COMPLETE ✅
> **Created**: 2025-12-28
> **Updated**: 2025-12-30
> **Related**: [ABML_FIRST_STEPS.md](./ABML_FIRST_STEPS.md), [BEHAVIOR_PLUGIN_V2.md](./UPCOMING_-_BEHAVIOR_PLUGIN_V2.md)

---

## 1. Overview

### 1.1 What is GOAP?

**Goal-Oriented Action Planning (GOAP)** enables NPCs to dynamically select actions to achieve goals, rather than following predefined behavior trees or state machines.

```
Traditional: IF hungry AND has_food THEN eat
GOAP:        Goal(hunger < 0.3) → Plans [go_to_market, buy_food, eat] → Execute
```

GOAP excels when:
- Multiple actions can achieve the same goal
- Actions have preconditions and effects that chain naturally
- The optimal path depends on current world state
- NPCs need to adapt to changing circumstances

### 1.2 GOAP Concepts

| Concept | Description | Example |
|---------|-------------|---------|
| **World State** | Current state as key-value pairs | `{ hunger: 0.8, gold: 50, location: "home" }` |
| **Goal** | Desired world state conditions | `hunger <= 0.3` |
| **Action** | Transform with preconditions/effects/cost | Eat: requires `has_food`, effects `hunger: -0.5`, cost 2 |
| **Plan** | Sequence of actions from current to goal state | `[GoToMarket, BuyFood, Eat]` |
| **Planner** | A* search finding lowest-cost plan | Explores action graph |

### 1.3 GOAP in ABML

GOAP metadata are **optional annotations** on ABML flows. This allows:
- Same ABML documents to work without GOAP (cutscenes, dialogues)
- GOAP-aware systems to extract planning metadata
- Single source of truth for behaviors and their GOAP properties

```yaml
flows:
  eat_meal:
    goap:
      preconditions:
        hunger: "> 0.6"
        gold: ">= 5"
      effects:
        hunger: "-0.8"
        gold: "-5"
      cost: 2
    actions:
      - walk_to: { location: "tavern" }
      - purchase: { item: "meal" }
```

---

## 2. Architecture Decisions

### 2.1 Parser Captures Structure, Executor Implements Logic

Following the established pattern (proven with `on_error` handling):

```
DocumentParser (bannou-service/Abml/Parser/)
    │
    │  Captures GOAP structure into typed models:
    │  - GoapGoalDefinition
    │  - GoapFlowMetadata
    │
    ▼
lib-behavior/Goap/
    │
    │  Implements planning logic:
    │  - WorldState (immutable state container)
    │  - GoapPlanner (A* search)
    │  - GoapMetadataConverter (parsed → runtime types)
    │
    ▼
BehaviorService
    │
    │  Exposes API endpoints:
    │  - /goap/plan
    │  - /goap/validate-plan
    ▼
```

**Why this separation?**
- Parser stays focused on YAML structure, not planning algorithms
- Planning logic can be tested in isolation (lib-behavior.tests)
- Changes to planning don't affect parser
- Same models can be used by multiple services

### 2.2 Literals Only in Conditions

GOAP conditions use **literal values only**, not ABML expressions:

```yaml
# CORRECT: Literal conditions
preconditions:
  hunger: "> 0.6"      # Compare to 0.6
  gold: ">= 5"         # Compare to 5
  at_location: "tavern" # Compare to "tavern"

# NOT SUPPORTED: Expressions in conditions
preconditions:
  hunger: "> ${threshold * 2}"  # NO - expressions not evaluated
```

**Rationale:**
1. GOAP plans with **known values**, not computed ones
2. Preconditions must be evaluable against static world state
3. Keeps condition parsing simple and fast
4. Expression evaluation happens in ABML actions, not GOAP metadata

### 2.3 Immutable WorldState

WorldState is immutable for A* correctness:

```csharp
// Every modification returns a NEW state
var newState = currentState.ApplyEffects(action.Effects);

// Original unchanged - required for backtracking
Assert.NotSame(currentState, newState);
```

**Why immutable?**
- A* explores multiple branches simultaneously
- Each node needs its own state snapshot
- Prevents bugs from accidental state mutation
- Enables hash-based closed-set checks

---

## 3. Component Breakdown

### 3.1 Parser Models (bannou-service/Abml/Documents/)

These models capture GOAP structure from YAML:

```csharp
/// <summary>
/// GOAP goal definition from ABML goals: section.
/// </summary>
public sealed class GoapGoalDefinition
{
    public int Priority { get; init; } = 50;
    public IReadOnlyDictionary<string, string> Conditions { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>
/// GOAP metadata from flow goap: block.
/// </summary>
public sealed class GoapFlowMetadata
{
    public IReadOnlyDictionary<string, string> Preconditions { get; init; } =
        new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Effects { get; init; } =
        new Dictionary<string, string>();
    public float Cost { get; init; } = 1.0f;
}
```

### 3.2 Planning Types (lib-behavior/Goap/)

These types implement planning logic:

| Type | Purpose |
|------|---------|
| `WorldState` | Immutable key-value state (numeric/boolean/string) |
| `GoapCondition` | Parse and evaluate literal conditions |
| `GoapPreconditions` | Container for action preconditions |
| `GoapActionEffects` | Container for action effects (deltas/sets) |
| `GoapAction` | Action with preconditions/effects/cost |
| `GoapGoal` | Goal with priority and conditions |
| `GoapPlan` | Result: action sequence + statistics |
| `PlanningOptions` | Config: max depth, max nodes, timeout |
| `PlanValidationResult` | Validation result + replan reason |
| `IGoapPlanner` | Planner interface |
| `GoapPlanner` | A* implementation |
| `GoapMetadataConverter` | Convert parsed models to runtime types |

### 3.3 GoapCondition Syntax

```
condition := operator value
operator  := ">" | ">=" | "<" | "<=" | "==" | "!="
value     := number | boolean | string

Examples:
"> 0.6"     → Greater than 0.6
">= 5"      → Greater than or equal to 5
"== true"   → Equals true
"!= 'idle'" → Not equals "idle"
"<= 0.3"    → Less than or equal to 0.3
```

### 3.4 Effect Syntax

```
effect := value | delta
delta  := ("+" | "-") number

Examples:
"-0.8"      → Subtract 0.8 from current value
"+5"        → Add 5 to current value
"0.5"       → Set to 0.5 (absolute)
"tavern"    → Set to "tavern" (string)
"true"      → Set to true (boolean)
```

---

## 4. A* Planner Algorithm

### 4.1 Overview

```
┌─────────────────────────────────────────────────────────┐
│                    A* PLANNER                            │
│                                                          │
│  Input:                                                  │
│    - Current WorldState                                  │
│    - Target Goal                                         │
│    - Available Actions (from ABML flows)                 │
│    - Options (max depth, max nodes, timeout)             │
│                                                          │
│  Output:                                                 │
│    - GoapPlan (action sequence) or null                  │
│                                                          │
│  Algorithm:                                              │
│    1. Initialize open set with start node               │
│    2. While open set not empty:                         │
│       a. Dequeue lowest F-cost node                     │
│       b. If goal satisfied → return plan                │
│       c. Add to closed set                              │
│       d. For each applicable action:                    │
│          - Apply effects to get new state               │
│          - Calculate G (cost so far) + H (heuristic)    │
│          - If within limits, enqueue                    │
│    3. Return null (no plan found)                        │
└─────────────────────────────────────────────────────────┘
```

### 4.2 Heuristic Function

```csharp
float DistanceToGoal(WorldState state, GoapGoal goal)
{
    float distance = 0;
    foreach (var (key, condition) in goal.Conditions)
    {
        var currentValue = state.GetNumeric(key);
        var targetValue = condition.TargetValue;
        distance += Math.Abs(currentValue - targetValue);
    }
    return distance;
}
```

The heuristic is **admissible** (never overestimates) when:
- All action costs are >= 1
- Each action moves at most one unit toward goal

### 4.3 Thread Safety

```csharp
public ValueTask<GoapPlan?> PlanAsync(...)
{
    // All state is method-local
    var openSet = new PriorityQueue<PlanNode, float>();
    var closedSet = new HashSet<int>();

    // Safe for concurrent calls - no shared state
    ...
}
```

---

## 5. Integration with BehaviorService

### 5.1 Endpoint: /goap/plan

```yaml
POST /goap/plan
Request:
  agent_id: string
  goal: { name, priority, conditions }
  world_state: { key: value, ... }
  behavior_id: string  # References compiled ABML
  options?: { max_depth, max_nodes, timeout_ms }

Response:
  success: boolean
  plan?: { actions: [...], total_cost, goal_id }
  planning_time_ms: integer
  nodes_expanded: integer
  failure_reason?: string
```

### 5.2 Endpoint: /goap/validate-plan

```yaml
POST /goap/validate-plan
Request:
  plan: { actions, total_cost, goal_id }
  current_action_index: integer
  world_state: { key: value, ... }

Response:
  is_valid: boolean
  reason?: action_failed | precondition_invalidated | better_goal_available | plan_completed
  invalidated_at_index?: integer
  suggested_action: continue | replan | abort
```

---

## 6. Test Coverage Requirements

### 6.1 WorldState Tests

- Get/set numeric, boolean, string values
- Dot-notation key access (`inventory.iron`)
- Immutability verification (SetNumeric returns new instance)
- ApplyEffects with deltas and absolutes
- SatisfiesPreconditions with all operators
- SatisfiesGoal
- DistanceToGoal calculation
- GetHashCode for closed-set membership
- Equality comparison

### 6.2 GoapCondition Tests

- Parse all operators: `>`, `>=`, `<`, `<=`, `==`, `!=`
- Parse numeric values: integers, floats, negatives
- Parse boolean values: `true`, `false`
- Parse string values: quoted strings
- Evaluate against numeric state
- Evaluate against boolean state
- Evaluate against string state
- Invalid syntax handling

### 6.3 GoapPlanner Tests

- **Simple plan**: Single action achieves goal
- **Chain plan**: 3+ actions in sequence
- **Cost optimization**: 2 paths exist, cheaper selected
- **No plan**: Goal unreachable
- **Max depth limit**: Plan exceeds limit
- **Max nodes limit**: Search space too large
- **Timeout**: Planning cancelled
- **Concurrent calls**: Thread safety verification
- **Empty actions**: No available actions
- **Goal already satisfied**: Returns empty plan

### 6.4 GoapMetadataConverter Tests

- Convert GoapGoalDefinition to GoapGoal
- Convert GoapFlowMetadata to GoapAction
- Extract all goals from document
- Extract all actions from document
- Handle missing goap: blocks
- Handle missing goals: section
- Preserve flow names as action IDs

### 6.5 Parser Tests (extend existing)

- Parse goals: section
- Parse goap: blocks in flows
- Handle missing goals: section
- Handle missing goap: blocks
- Validate condition syntax
- Validate effect syntax

---

## 7. Implementation Checklist

### Phase 2.0: Documentation ✅
- [x] This document (GOAP_FIRST_STEPS.md)

### Phase 2.1: Parser Models ✅
- [x] Add GoapGoalDefinition class to AbmlDocument.cs
- [x] Add GoapFlowMetadata class to AbmlDocument.cs
- [x] Add Goals property to AbmlDocument
- [x] Add Goap property to Flow
- [x] Extend DocumentParser.ParseGoals()
- [x] Extend DocumentParser.ParseFlows() for goap: blocks
- [x] Add parser unit tests

### Phase 2.2: Core GOAP Types ✅
- [x] WorldState.cs (immutable, supports numeric/boolean/string)
- [x] GoapCondition.cs (all operators: >, >=, <, <=, ==, !=)
- [x] GoapPreconditions.cs
- [x] GoapActionEffects.cs
- [x] GoapAction.cs (with FromMetadata factory)
- [x] GoapGoal.cs
- [x] GoapPlan.cs
- [x] PlanningOptions.cs
- [x] PlanValidationResult.cs

### Phase 2.3: A* Planner ✅
- [x] IGoapPlanner.cs interface
- [x] GoapPlanner.cs implementation (264 lines, PriorityQueue-based)
- [x] Planner unit tests (lib-behavior.tests/Goap/GoapPlannerTests.cs)

### Phase 2.4: Metadata Converter ✅
- [x] GoapMetadataConverter.cs (HasGoapContent, ExtractGoals, ExtractActions)
- [x] Converter unit tests (lib-behavior.tests/Goap/GoapMetadataConverterTests.cs)

### Phase 2.5: BehaviorService Integration ✅
- [x] Update behavior-api.yaml with GOAP endpoints
- [x] Regenerate service code
- [x] Implement GenerateGoapPlanAsync (with metadata caching)
- [x] Implement ValidateGoapPlanAsync
- [x] Register GoapPlanner in DI (BehaviorServicePlugin)
- [x] GOAP metadata caching (BehaviorBundleManager.Save/Get/RemoveGoapMetadataAsync)
- [x] HTTP integration tests (http-tester/Tests/BehaviorTestHandler.cs)

### Phase 2.6: Final Tests ✅
- [x] All unit tests passing
- [x] Build with zero warnings
- [x] HTTP integration tests for GOAP endpoints

---

## 8. Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| YamlDotNet | 16.3.0 | Already used by DocumentParser |
| - | - | No new dependencies required |

GOAP uses only standard .NET types:
- `PriorityQueue<T, TPriority>` (built-in .NET 6+)
- `Dictionary<K, V>` for state storage
- `HashSet<T>` for closed set

---

## 9. Performance Targets

| Metric | Target | Notes |
|--------|--------|-------|
| Simple plan (1-3 actions) | < 1ms | Most NPC behaviors |
| Complex plan (5-10 actions) | < 10ms | Elaborate behavior chains |
| Large search space (1000 nodes) | < 100ms | Configurable limit |
| Memory per plan | < 1KB | PlanNode allocations |

---

*Document Status: COMPLETE - All GOAP phases implemented 2025-12-30*
