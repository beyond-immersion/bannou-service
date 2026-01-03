# GOAP - Goal-Oriented Action Planning

> **Version**: 1.0
> **Status**: Implemented
> **Location**: `lib-behavior/Goap/`
> **Related**: [ABML Guide](./ABML.md)

GOAP is an AI planning technique that enables NPCs to autonomously discover sequences of actions to achieve their goals. Instead of hand-crafting behavior trees or state machines, you define **what NPCs want** (goals) and **what they can do** (actions), and the planner figures out **how** to get there.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Core Concepts](#2-core-concepts)
3. [GOAP in ABML](#3-goap-in-abml)
4. [The A* Planning Algorithm](#4-the-a-planning-algorithm)
5. [API Integration](#5-api-integration)
6. [Cognition Integration](#6-cognition-integration)
7. [Implementation Architecture](#7-implementation-architecture)
8. [Testing and Debugging](#8-testing-and-debugging)
9. [Examples](#9-examples)
10. [Best Practices](#10-best-practices)
- [Appendix A: Condition and Effect Syntax](#appendix-a-condition-and-effect-syntax)
- [Appendix B: Planning Options Reference](#appendix-b-planning-options-reference)

---

## 1. Overview

### 1.1 What is GOAP?

GOAP (Goal-Oriented Action Planning) is an AI technique originally developed for the game F.E.A.R. (2005). It separates the **what** from the **how**:

- **Goals**: Desired world states (e.g., "be fed", "have 50 gold")
- **Actions**: Things NPCs can do, with preconditions and effects
- **Planner**: A* search that finds action sequences to achieve goals

```
Current State → [A* Search] → Action Sequence → Goal Satisfied
```

### 1.2 Why GOAP Instead of Behavior Trees?

| Aspect | Behavior Trees | GOAP |
|--------|----------------|------|
| **Authoring** | Hand-craft every decision path | Define goals and actions, planner figures out paths |
| **Flexibility** | New situations need new branches | New situations can use existing actions in new combinations |
| **Emergent behavior** | Limited - only what's authored | High - planner discovers novel sequences |
| **Debugging** | Follow tree structure | Inspect world state and plan steps |
| **Performance** | O(1) per tick | O(n) planning, then O(1) execution |

GOAP excels when:
- NPCs have many ways to achieve goals
- The game world changes dynamically
- You want emergent, believable behavior
- Actions can combine in creative ways

### 1.3 GOAP in Bannou

In Bannou, GOAP is **integrated with ABML**:

1. **Goals** are defined in the `goals:` section of ABML documents
2. **Actions** are flows with `goap:` metadata blocks
3. **Planning** happens via the Behavior Service API
4. **Execution** runs the planned flows via the DocumentExecutor

The same ABML document can work both with and without GOAP - cutscenes and dialogues ignore GOAP metadata, while NPC behaviors use it for autonomous decision-making.

---

## 2. Core Concepts

### 2.1 World State

World state is an immutable key-value store representing the current state of an NPC's world:

```csharp
var worldState = new WorldState()
    .SetNumeric("hunger", 0.8f)      // How hungry (0-1)
    .SetNumeric("gold", 10)          // Currency
    .SetBoolean("has_weapon", true)  // Equipment
    .SetString("location", "town");  // Current location
```

**Properties:**
- **Immutable**: Operations return new instances (safe for A* backtracking)
- **Typed access**: `GetNumeric()`, `GetBoolean()`, `GetString()`
- **Hashable**: Used as keys in the closed set during search

### 2.2 Goals

A goal defines a desired world state through conditions:

```yaml
# ABML goal definition
goals:
  stay_fed:
    priority: 100
    conditions:
      hunger: "<= 0.3"    # Hunger must be at or below 0.3
```

**Goal properties:**
- **Name/ID**: Unique identifier
- **Priority**: Higher = more important (used when multiple goals compete)
- **Conditions**: Map of world state properties to condition strings

### 2.3 Actions

An action represents something an NPC can do:

```yaml
# ABML flow with GOAP metadata
flows:
  eat_meal:
    goap:
      preconditions:
        gold: ">= 5"           # Need 5 gold to buy food
        hunger: "> 0.5"        # Only eat when hungry
      effects:
        hunger: "-0.6"         # Reduces hunger by 0.6
        gold: "-5"             # Costs 5 gold
      cost: 1.0                # Planning cost (lower = preferred)

    actions:
      - go_to: { destination: tavern }
      - purchase: { item: meal }
      - consume: { item: meal }
```

**Action properties:**
- **ID**: Usually the flow name
- **Preconditions**: Conditions that must be true to execute
- **Effects**: Changes applied to world state when action completes
- **Cost**: Used by A* to prefer cheaper plans

### 2.4 Plans

A plan is an ordered sequence of actions that achieves a goal:

```
Plan: [work] → [eat_meal]
  - work: +10 gold, total cost: 2.0
  - eat_meal: requires gold>=5, reduces hunger, cost: 1.0
Total cost: 3.0
Nodes expanded: 15
Planning time: 2ms
```

**Plan properties:**
- **Goal**: The goal this plan achieves
- **Actions**: Ordered list of actions to execute
- **TotalCost**: Sum of all action costs
- **Statistics**: Nodes expanded, planning time

### 2.5 The Planning Problem

Given:
- **Current state**: Where the NPC is now
- **Goal**: Where the NPC wants to be
- **Available actions**: What the NPC can do

Find:
- **Action sequence** that transforms current state → goal state
- **Minimizing** total cost

---

## 3. GOAP in ABML

### 3.1 Document Structure

```yaml
version: "2.0"

metadata:
  id: npc-survival-behavior
  type: behavior
  description: Basic NPC survival with hunger and economy

context:
  variables:
    hunger: { type: float, default: 0.5 }
    gold: { type: int, default: 0 }

# GOAP goal definitions
goals:
  stay_fed:
    priority: 100
    conditions:
      hunger: "<= 0.3"

  earn_money:
    priority: 50
    conditions:
      gold: ">= 50"

# Flows with GOAP metadata become available actions
flows:
  eat_meal:
    goap:
      preconditions:
        gold: ">= 5"
        hunger: "> 0.5"
      effects:
        hunger: "-0.6"
        gold: "-5"
      cost: 1
    actions:
      - go_to: { destination: tavern }
      - purchase: { item: meal }
      - consume: { item: meal }

  work:
    goap:
      preconditions: {}          # No preconditions
      effects:
        gold: "+10"              # Earn 10 gold
        hunger: "+0.1"           # Working makes you hungry
      cost: 2
    actions:
      - go_to: { destination: workshop }
      - perform_labor: { duration: 1h }
      - collect_payment: {}

  forage:
    goap:
      preconditions:
        location: "== 'wilderness'"
      effects:
        hunger: "-0.3"           # Less effective than bought meal
      cost: 3                    # Higher cost = less preferred
    actions:
      - search_area: { type: food }
      - consume: { item: foraged_food }
```

### 3.2 Goal Definition

```yaml
goals:
  goal_name:
    priority: <integer>       # Higher = more important (default: 50)
    conditions:
      property: "condition"   # World state property → condition string
```

**Priority ranges:**
| Range | Use Case |
|-------|----------|
| 1-25 | Background desires (hobbies, preferences) |
| 26-50 | Normal goals (daily routine) |
| 51-75 | Important goals (earning money, social needs) |
| 76-100 | Critical goals (survival, safety) |

### 3.3 GOAP Flow Metadata

```yaml
flows:
  action_name:
    goap:
      preconditions:
        property: "condition"    # All must be true to execute
      effects:
        property: "effect"       # Applied when action completes
      cost: <float>              # Default: 1.0

    actions:
      # Actual behavior when this action executes
```

**Notes:**
- Flows without `goap:` blocks are not considered by the planner
- The same flow can be called directly AND used as a GOAP action
- `cost` is optional; defaults to 1.0

### 3.4 Condition Syntax

Conditions compare world state properties to literal values:

```
condition := operator value
operator  := ">" | ">=" | "<" | "<=" | "==" | "!="
value     := number | boolean | string

Examples:
"> 0.6"         # Greater than 0.6
">= 5"          # Greater than or equal to 5
"<= 0.3"        # Less than or equal to 0.3
"== true"       # Equals true
"!= 'idle'"     # Not equals "idle"
"== 'tavern'"   # Equals "tavern"
```

### 3.5 Effect Syntax

Effects modify world state when an action completes:

```
effect := value | delta
delta  := ("+" | "-") number

Examples:
"-0.8"          # Subtract 0.8 from current value
"+5"            # Add 5 to current value
"0.5"           # Set to 0.5 (absolute)
"tavern"        # Set to "tavern" (string)
"true"          # Set to true (boolean)
```

**Delta vs Absolute:**
- Use deltas (`+5`, `-0.3`) for incremental changes (resource consumption)
- Use absolute values (`tavern`, `true`) for state changes (location, flags)

---

## 4. The A* Planning Algorithm

### 4.1 How Planning Works

The planner uses A* search with world states as nodes:

```
1. Start node = current world state
2. While open set not empty:
   a. Pop node with lowest F-cost (G + H)
   b. If satisfies goal → reconstruct plan, return
   c. For each applicable action:
      - Apply effects to get new state
      - If not in closed set, add to open set
3. No plan found
```

**Cost calculation:**
- **G-cost**: Sum of action costs from start to current node
- **H-cost**: Heuristic distance to goal (sum of condition distances)
- **F-cost**: G + H (nodes with lower F-cost are explored first)

### 4.2 Heuristic Function

The heuristic estimates distance to goal:

```csharp
float DistanceToGoal(WorldState state, GoapGoal goal)
{
    float distance = 0;
    foreach (var condition in goal.Conditions)
    {
        var value = state.GetValue(condition.Key);
        distance += condition.Distance(value);  // 0 if satisfied
    }
    return distance;
}
```

**Distance calculation by condition type:**
| Condition | Distance Formula |
|-----------|------------------|
| `>= 10` (actual: 5) | 10 - 5 = 5 |
| `<= 0.3` (actual: 0.8) | 0.8 - 0.3 = 0.5 |
| `== true` (actual: false) | 1.0 |
| `== 'tavern'` (actual: 'home') | 1.0 |

### 4.3 Planning Options

Control search behavior with `PlanningOptions`:

```csharp
var options = new PlanningOptions
{
    MaxDepth = 10,           // Maximum actions in a plan
    MaxNodesExpanded = 1000, // Prevent runaway searches
    TimeoutMs = 100,         // Planning time limit
    HeuristicWeight = 1.0f   // A* weight (>1 = faster but less optimal)
};
```

**Preset configurations:**

| Preset | MaxDepth | MaxNodes | Timeout | Use Case |
|--------|----------|----------|---------|----------|
| `Default` | 10 | 1000 | 100ms | General planning |
| `Fast` | 5 | 500 | 50ms | Real-time decisions |
| `Thorough` | 20 | 5000 | 500ms | Complex scenarios |

### 4.4 Plan Validation

Plans can become invalid as the world changes. Validate before continuing:

```csharp
var result = await planner.ValidatePlanAsync(
    plan,
    currentActionIndex,
    currentState,
    activeGoals);

switch (result.Suggestion)
{
    case ValidationSuggestion.Continue:
        // Plan still valid, keep executing
        break;

    case ValidationSuggestion.Replan:
        // Plan invalid, generate new plan
        break;

    case ValidationSuggestion.Abort:
        // Goal satisfied or plan completed
        break;
}
```

**Validation reasons:**
| Reason | Description | Suggestion |
|--------|-------------|------------|
| `None` | Plan is valid | Continue |
| `PreconditionInvalidated` | Current action's preconditions not met | Replan |
| `BetterGoalAvailable` | Higher-priority goal now unsatisfied | Replan |
| `GoalAlreadySatisfied` | Goal already achieved | Abort |
| `PlanCompleted` | All actions executed | Abort |

---

## 5. API Integration

### 5.1 Compiling ABML with GOAP

When you compile an ABML document, GOAP metadata is automatically extracted and cached:

```csharp
var response = await behaviorClient.CompileAbmlBehaviorAsync(new CompileBehaviorRequest
{
    AbmlContent = yamlContent,
    BehaviorName = "npc-survival"
});

// GOAP metadata cached under behavior ID
string behaviorId = response.BehaviorId;
```

### 5.2 Generating Plans

Request a plan via the Behavior Service API:

```csharp
var planResponse = await behaviorClient.GenerateGoapPlanAsync(new GoapPlanRequest
{
    BehaviorId = behaviorId,    // Links to cached GOAP metadata
    AgentId = npcId,
    Goal = new GoapGoal
    {
        Name = "stay_fed",
        Priority = 100,
        Conditions = new Dictionary<string, string>
        {
            { "hunger", "<= 0.3" }
        }
    },
    WorldState = new Dictionary<string, object>
    {
        { "hunger", 0.8f },
        { "gold", 10 },
        { "location", "town" }
    },
    Options = new GoapPlanOptions
    {
        MaxDepth = 10,
        MaxNodes = 1000,
        TimeoutMs = 100
    }
});

if (planResponse.Success)
{
    // planResponse.Plan contains the action sequence
    foreach (var action in planResponse.Plan.Actions)
    {
        Console.WriteLine($"Step {action.Index}: {action.ActionId}");
    }
}
```

### 5.3 Validating Plans

Check if an existing plan is still valid:

```csharp
var validationResponse = await behaviorClient.ValidateGoapPlanAsync(
    new ValidateGoapPlanRequest
    {
        Plan = currentPlan,
        CurrentActionIndex = executionIndex,
        WorldState = currentWorldState,
        ActiveGoals = allAgentGoals
    });

if (!validationResponse.IsValid)
{
    // Need to replan
    var newPlan = await GenerateNewPlanAsync();
}
```

---

## 6. Cognition Integration

### 6.1 The 5-Stage Cognition Pipeline

GOAP integrates with Bannou's cognition pipeline at Stage 5:

```
Stage 1: Filter Attention      → filter_attention handler
Stage 2: Query Memory          → query_memory handler
Stage 3: Assess Significance   → assess_significance handler
Stage 4: Evaluate Goal Impact  → evaluate_goal_impact handler
Stage 5: Trigger GOAP Replan   → trigger_goap_replan handler
```

### 6.2 Urgency-Based Planning

Planning parameters adjust based on urgency:

| Urgency | Range | MaxDepth | Timeout | MaxNodes | Use Case |
|---------|-------|----------|---------|----------|----------|
| Low | 0 - 0.3 | 10 | 100ms | 1000 | Full deliberation |
| Medium | 0.3 - 0.7 | 6 | 50ms | 500 | Quick decision |
| High | 0.7 - 1.0 | 3 | 20ms | 200 | Immediate reaction |

**High urgency = shallower search** (fight-or-flight decisions need to be fast)

### 6.3 ABML Cognition Example

```yaml
version: "2.0"

metadata:
  id: npc-brain
  type: behavior

goals:
  stay_safe:
    priority: 100
    conditions:
      in_danger: "== false"

  stay_fed:
    priority: 75
    conditions:
      hunger: "<= 0.3"

flows:
  process_tick:
    actions:
      # Stage 1: Filter perceptions by importance
      - filter_attention:
          input: "${perceptions}"
          attention_budget: 100
          result_variable: "filtered"
          fast_track_variable: "urgent"

      # Handle urgent threats immediately
      - cond:
          if: "${len(urgent) > 0}"
          then:
            - call: handle_threat

      # Stage 2-3: Memory and significance
      - query_memory:
          context: "${filtered}"
          result_variable: "memories"

      - assess_significance:
          perceptions: "${filtered}"
          memories: "${memories}"
          result_variable: "significance"

      # Stage 4: Check if goals are affected
      - evaluate_goal_impact:
          perceptions: "${filtered}"
          current_goals: "${active_goals}"
          result_variable: "goal_impact"

      # Stage 5: Replan if needed
      - cond:
          if: "${goal_impact.requires_replan}"
          then:
            - trigger_goap_replan:
                goals: "${goal_impact.affected_goals}"
                urgency: "${goal_impact.urgency}"
                world_state: "${agent.world_state}"
                behavior_id: "${agent.behavior_id}"
                result_variable: "replan_result"

  handle_threat:
    goap:
      preconditions:
        in_danger: "== true"
      effects:
        in_danger: "false"
      cost: 1
    actions:
      - flee_or_fight: {}
```

### 6.4 The trigger_goap_replan Handler

This cognition handler triggers GOAP planning with urgency-aware parameters:

```yaml
- trigger_goap_replan:
    goals: "${affected_goals}"      # Goals to plan for
    urgency: 0.8                    # Urgency level (0-1)
    world_state: "${world}"         # Current world state
    behavior_id: "${behavior_id}"   # For loading cached GOAP metadata
    entity_id: "${npc.id}"          # For logging
    result_variable: "plan_result"  # Where to store the plan
```

**Result contains:**
- `Triggered`: Whether planning was initiated
- `Plan`: The generated plan (if successful)
- `Message`: Status or error message

---

## 7. Implementation Architecture

### 7.1 File Structure

```
lib-behavior/Goap/
├── WorldState.cs           # Immutable key-value state
├── GoapGoal.cs             # Goal with priority and conditions
├── GoapAction.cs           # Action with preconditions, effects, cost
├── GoapCondition.cs        # Condition parsing and evaluation
├── GoapActionEffects.cs    # Effect parsing and application
├── GoapPreconditions.cs    # Precondition container
├── GoapPlan.cs             # Plan result with actions and statistics
├── PlanningOptions.cs      # Search configuration
├── PlanValidationResult.cs # Validation result types
├── IGoapPlanner.cs         # Planner interface
├── GoapPlanner.cs          # A* implementation
└── GoapMetadataConverter.cs # ABML → GOAP type conversion
```

### 7.2 Key Classes

**WorldState** - Immutable state container:
```csharp
public sealed class WorldState : IEquatable<WorldState>
{
    public float GetNumeric(string key, float defaultValue = 0f);
    public bool GetBoolean(string key, bool defaultValue = false);
    public string GetString(string key, string defaultValue = "");

    // Returns new instance with modification
    public WorldState SetNumeric(string key, float value);
    public WorldState SetBoolean(string key, bool value);
    public WorldState SetString(string key, string value);

    // GOAP operations
    public bool SatisfiesGoal(GoapGoal goal);
    public bool SatisfiesPreconditions(GoapPreconditions preconditions);
    public WorldState ApplyEffects(GoapActionEffects effects);
    public float DistanceToGoal(GoapGoal goal);
}
```

**GoapPlanner** - A* search implementation:
```csharp
public sealed class GoapPlanner : IGoapPlanner
{
    public ValueTask<GoapPlan?> PlanAsync(
        WorldState currentState,
        GoapGoal goal,
        IReadOnlyList<GoapAction> availableActions,
        PlanningOptions? options = null,
        CancellationToken ct = default);

    public ValueTask<PlanValidationResult> ValidatePlanAsync(
        GoapPlan plan,
        int currentActionIndex,
        WorldState currentState,
        IReadOnlyList<GoapGoal>? activeGoals = null,
        CancellationToken ct = default);
}
```

### 7.3 Dependency Injection

The GOAP planner is registered as a singleton:

```csharp
// Automatic registration via BehaviorService
services.AddSingleton<IGoapPlanner, GoapPlanner>();
```

---

## 8. Testing and Debugging

### 8.1 Unit Testing Plans

```csharp
[Fact]
public async Task Planner_FindsWorkThenEatPlan()
{
    var planner = new GoapPlanner();

    // Start hungry with no gold
    var state = new WorldState()
        .SetNumeric("hunger", 0.8f)
        .SetNumeric("gold", 0);

    var goal = GoapGoal.FromMetadata(
        "stay_fed",
        100,
        new Dictionary<string, string> { { "hunger", "<= 0.3" } });

    var actions = new List<GoapAction>
    {
        GoapAction.FromMetadata("eat_meal",
            new Dictionary<string, string> { { "gold", ">= 5" } },
            new Dictionary<string, string> { { "hunger", "-0.6" }, { "gold", "-5" } },
            cost: 1.0f),

        GoapAction.FromMetadata("work",
            new Dictionary<string, string>(),
            new Dictionary<string, string> { { "gold", "+10" } },
            cost: 2.0f)
    };

    var plan = await planner.PlanAsync(state, goal, actions);

    Assert.NotNull(plan);
    Assert.Equal(2, plan.Actions.Count);
    Assert.Equal("work", plan.Actions[0].Action.Id);      // First: get gold
    Assert.Equal("eat_meal", plan.Actions[1].Action.Id);  // Then: eat
}
```

### 8.2 Testing Goal Satisfaction

```csharp
[Fact]
public void Goal_IsSatisfied_WhenConditionsMet()
{
    var goal = GoapGoal.FromMetadata(
        "fed",
        100,
        new Dictionary<string, string> { { "hunger", "<= 0.3" } });

    var hungryState = new WorldState().SetNumeric("hunger", 0.8f);
    var fedState = new WorldState().SetNumeric("hunger", 0.2f);

    Assert.False(hungryState.SatisfiesGoal(goal));
    Assert.True(fedState.SatisfiesGoal(goal));
}
```

### 8.3 Debugging Tips

**1. Log plan generation:**
```csharp
var plan = await planner.PlanAsync(state, goal, actions);
if (plan != null)
{
    _logger.LogInformation(
        "Plan found: {Actions} (cost: {Cost}, nodes: {Nodes}, time: {Time}ms)",
        string.Join(" -> ", plan.GetActionIds()),
        plan.TotalCost,
        plan.NodesExpanded,
        plan.PlanningTimeMs);
}
```

**2. Check why planning failed:**
- No actions available? Check `goap:` blocks exist
- Unreachable goal? No action sequence can satisfy conditions
- Timeout? Increase `TimeoutMs` or simplify action space

**3. Validate conditions parse correctly:**
```csharp
var condition = GoapCondition.Parse(">= 5");
Assert.Equal(ComparisonOperator.GreaterThanOrEqual, condition.Operator);
Assert.Equal(5, condition.TargetValue);
```

---

## 9. Examples

### 9.1 Basic Survival NPC

```yaml
version: "2.0"

metadata:
  id: survival-npc
  type: behavior

goals:
  survive:
    priority: 100
    conditions:
      health: ">= 50"
      hunger: "<= 0.5"

  prosper:
    priority: 50
    conditions:
      gold: ">= 100"

flows:
  rest:
    goap:
      preconditions:
        health: "< 80"
      effects:
        health: "+30"
        hunger: "+0.2"
      cost: 2
    actions:
      - go_to: { destination: home }
      - sleep: { duration: 8h }

  eat_cheap:
    goap:
      preconditions:
        gold: ">= 2"
        hunger: "> 0.3"
      effects:
        hunger: "-0.4"
        gold: "-2"
      cost: 1
    actions:
      - go_to: { destination: market }
      - buy: { item: bread }
      - consume: { item: bread }

  eat_good:
    goap:
      preconditions:
        gold: ">= 10"
        hunger: "> 0.3"
      effects:
        hunger: "-0.8"
        gold: "-10"
        health: "+5"
      cost: 2
    actions:
      - go_to: { destination: tavern }
      - order_meal: { quality: good }
      - consume: { item: meal }

  work_labor:
    goap:
      preconditions: {}
      effects:
        gold: "+5"
        hunger: "+0.15"
        health: "-5"
      cost: 2
    actions:
      - go_to: { destination: docks }
      - perform_labor: { type: hauling }

  work_skilled:
    goap:
      preconditions:
        has_skill: "== true"
      effects:
        gold: "+15"
        hunger: "+0.1"
      cost: 1
    actions:
      - go_to: { destination: workshop }
      - craft_goods: {}
```

### 9.2 Combat NPC

```yaml
version: "2.0"

metadata:
  id: combat-npc
  type: behavior

goals:
  eliminate_threat:
    priority: 100
    conditions:
      enemy_alive: "== false"

  stay_healthy:
    priority: 90
    conditions:
      health: ">= 30"

  be_armed:
    priority: 80
    conditions:
      has_weapon: "== true"

flows:
  attack_melee:
    goap:
      preconditions:
        has_weapon: "== true"
        enemy_in_range: "== true"
        stamina: ">= 20"
      effects:
        enemy_health: "-30"
        stamina: "-20"
      cost: 1
    actions:
      - swing_weapon: {}

  attack_ranged:
    goap:
      preconditions:
        has_ranged: "== true"
        ammo: ">= 1"
      effects:
        enemy_health: "-20"
        ammo: "-1"
      cost: 2
    actions:
      - aim: { target: enemy }
      - fire: {}

  approach_enemy:
    goap:
      preconditions:
        enemy_in_range: "== false"
      effects:
        enemy_in_range: "true"
        stamina: "-10"
      cost: 1
    actions:
      - move_to: { target: enemy }

  use_healing:
    goap:
      preconditions:
        health: "< 50"
        has_potion: "== true"
      effects:
        health: "+40"
        has_potion: "false"
      cost: 2
    actions:
      - use_item: { item: healing_potion }

  flee:
    goap:
      preconditions:
        health: "< 20"
      effects:
        enemy_in_range: "false"
        in_combat: "false"
      cost: 5
    actions:
      - disengage: {}
      - run_away: {}
```

### 9.3 Social NPC

```yaml
version: "2.0"

metadata:
  id: social-npc
  type: behavior

goals:
  maintain_friendships:
    priority: 60
    conditions:
      social_need: "<= 0.3"

  good_reputation:
    priority: 50
    conditions:
      reputation: ">= 50"

flows:
  chat_friend:
    goap:
      preconditions:
        has_friend_nearby: "== true"
        social_need: "> 0.2"
      effects:
        social_need: "-0.3"
        reputation: "+2"
      cost: 1
    actions:
      - approach: { target: nearest_friend }
      - initiate_conversation: { type: casual }

  help_stranger:
    goap:
      preconditions:
        stranger_needs_help: "== true"
      effects:
        reputation: "+10"
        stranger_needs_help: "false"
        social_need: "-0.1"
      cost: 3
    actions:
      - offer_help: {}
      - assist: {}

  attend_event:
    goap:
      preconditions:
        event_nearby: "== true"
        gold: ">= 5"
      effects:
        social_need: "-0.5"
        reputation: "+5"
        gold: "-5"
      cost: 2
    actions:
      - go_to: { destination: event_location }
      - participate: {}
```

---

## 10. Best Practices

### 10.1 Designing Goals

1. **Use meaningful priority ranges** - Reserve high priorities (90+) for survival
2. **Keep conditions simple** - 1-3 conditions per goal is ideal
3. **Avoid overlapping goals** - Goals should be distinct, not variations
4. **Consider goal conflicts** - What happens when goals compete?

### 10.2 Designing Actions

1. **Single responsibility** - Each action does one thing
2. **Clear preconditions** - Only what's actually required
3. **Measurable effects** - Effects should directly impact goal conditions
4. **Appropriate costs** - Lower cost = more preferred
5. **Avoid impossible actions** - Don't create actions that can never execute

### 10.3 Cost Tuning

| Action Type | Suggested Cost | Rationale |
|-------------|----------------|-----------|
| Cheap/easy actions | 1-2 | Preferred by planner |
| Standard actions | 3-5 | Normal options |
| Expensive/risky actions | 6-10 | Only when no better option |
| Emergency actions | 10+ | Last resort |

### 10.4 World State Design

1. **Flat structure** - Avoid deeply nested properties
2. **Numeric for gradients** - Hunger, health, reputation (0-1 or 0-100)
3. **Boolean for flags** - has_weapon, is_indoor, enemy_nearby
4. **String for categories** - location, mood, activity

### 10.5 Performance Tips

1. **Limit action count** - 10-20 actions is usually sufficient
2. **Use appropriate timeouts** - Don't over-plan simple decisions
3. **Cache GOAP metadata** - Compilation extracts and caches metadata
4. **Validate before re-planning** - Don't plan every tick

---

## Appendix A: Condition and Effect Syntax

### Condition Operators

| Operator | Meaning | Example | Matches |
|----------|---------|---------|---------|
| `>` | Greater than | `"> 0.5"` | 0.6, 0.7, 1.0 |
| `>=` | Greater than or equal | `">= 10"` | 10, 11, 100 |
| `<` | Less than | `"< 0.3"` | 0.0, 0.1, 0.2 |
| `<=` | Less than or equal | `"<= 100"` | 0, 50, 100 |
| `==` | Equals | `"== true"` | true |
| `!=` | Not equals | `"!= 'idle'"` | "walking", "fighting" |

### Effect Types

| Syntax | Type | Example | Result |
|--------|------|---------|--------|
| `+N` | Add | `"+10"` | current + 10 |
| `-N` | Subtract | `"-0.5"` | current - 0.5 |
| `N` | Set numeric | `"0.5"` | exactly 0.5 |
| `true/false` | Set boolean | `"true"` | exactly true |
| `'string'` | Set string | `"tavern"` | exactly "tavern" |

---

## Appendix B: Planning Options Reference

### Default Options

```csharp
public static PlanningOptions Default { get; } = new()
{
    MaxDepth = 10,
    MaxNodesExpanded = 1000,
    TimeoutMs = 100,
    AllowDuplicateActions = true,
    HeuristicWeight = 1.0f
};
```

### Urgency-Based Options

| Urgency | Threshold | MaxDepth | Timeout | MaxNodes |
|---------|-----------|----------|---------|----------|
| Low | < 0.3 | 10 | 100ms | 1000 |
| Medium | 0.3 - 0.7 | 6 | 50ms | 500 |
| High | >= 0.7 | 3 | 20ms | 200 |

### HeuristicWeight

| Weight | Behavior |
|--------|----------|
| 1.0 | Standard A* (optimal plans) |
| 1.2-1.5 | Faster search, slightly suboptimal |
| 2.0+ | Greedy search (fast but may miss best plan) |

---

*For ABML syntax and document structure, see the [ABML Guide](./ABML.md).*
