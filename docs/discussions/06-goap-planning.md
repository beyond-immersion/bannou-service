# Part 6: GOAP - Planning Musical Narratives

> **Series**: [Understanding the Music Storyteller](./00-index-music-storyteller.md) | Part 6 of 7

## Why Planning Matters

A human composer doesn't think "now I'll use a V7 chord." They think "I want to build toward a climax in 8 bars." The chord choices follow from that goal.

**Goal-Oriented Action Planning (GOAP)** brings this goal-driven thinking to procedural composition. Instead of randomly applying music theory rules, the system:

1. Defines a **goal** (e.g., "reach high tension")
2. Searches through possible **actions** (musical techniques)
3. Finds an optimal **plan** to reach the goal
4. Executes the plan, generating music

---

## GOAP Architecture

```
GOAP Architecture
=================

┌─────────────────────────────────────────────────────────────────────┐
│                          WORLD STATE                                │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌────────────┐   │
│  │  Tension   │  │ Brightness │  │   Energy   │  │  Stability │   │
│  │    0.3     │  │    0.6     │  │    0.5     │  │    0.7     │   │
│  └────────────┘  └────────────┘  └────────────┘  └────────────┘   │
└───────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│                              GOAL                                   │
│                         "Tension ≥ 0.8"                             │
└───────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│                         GOAP PLANNER                                │
│                      (A* Search Algorithm)                          │
│                                                                     │
│   ┌──────────┐     ┌──────────┐     ┌──────────┐     ┌──────────┐ │
│   │ Action 1 │ ──► │ Action 2 │ ──► │ Action 3 │ ──► │  GOAL!   │ │
│   │ V7 Chord │     │ Sequence │     │ Dim7     │     │ T = 0.85 │ │
│   │ T +0.15  │     │ T +0.10  │     │ T +0.25  │     │          │ │
│   └──────────┘     └──────────┘     └──────────┘     └──────────┘ │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│                              PLAN                                   │
│  1. UseDominantSeventh (cost: 0.8)                                 │
│  2. AscendingSequence (cost: 0.9)                                  │
│  3. UseDiminishedSeventh (cost: 1.2)                               │
│  Total cost: 2.9                                                   │
└─────────────────────────────────────────────────────────────────────┘
```

---

## The A* Search Algorithm

GOAP uses A* search to find optimal plans:

```
A* Search in GOAP
=================

        Current State: T = 0.3
                │
        ┌───────┼───────┐───────┐
        │       │       │       │
        ▼       ▼       ▼       ▼
      V7 Cmd  Seq Up   Dim7   Delay
      ────────────────────────────
     T = 0.45  0.40    0.55   0.45
     Cost: 0.8 0.9     1.2    1.1
        │       │
   ┌────┴───┐   │
   │        │   │
   ▼        ▼   ▼
 Seq Up   Dim7  V7
 ─────────────────
T = 0.55  0.70 0.55
Cost: 1.7 2.0  1.8
   │
   ▼
 Dim7
 ────
T = 0.80  ← GOAL REACHED!
Cost: 2.9
```

**Key insight**: A* prioritizes nodes by `f(n) = g(n) + h(n)`
- `g(n)` = cost so far (sum of action costs)
- `h(n)` = heuristic (distance to goal)

---

## The GOAPPlanner Implementation

```csharp
public sealed class GOAPPlanner
{
    private readonly IReadOnlyList<GOAPAction> _actions;

    public int MaxDepth { get; init; } = 10;
    public int MaxNodesExplored { get; init; } = 1000;

    public Plan CreatePlan(WorldState current, GOAPGoal goal)
    {
        // Check if goal is already satisfied
        if (goal.IsSatisfied(current))
            return Plan.Empty(goal, current);

        // A* search
        var openSet = new PriorityQueue<PlanNode, double>();
        var closedSet = new HashSet<string>();

        var startNode = new PlanNode
        {
            State = current,
            Actions = [],
            GCost = 0,
            HCost = goal.GetDistance(current)
        };

        openSet.Enqueue(startNode, startNode.FCost);

        while (openSet.Count > 0 && nodesExplored < MaxNodesExplored)
        {
            var currentNode = openSet.Dequeue();

            // Check if we've reached the goal
            if (goal.IsSatisfied(currentNode.State))
            {
                return BuildPlan(goal, currentNode);
            }

            // Expand with available actions
            foreach (var action in _actions)
            {
                if (!action.IsSatisfied(currentNode.State))
                    continue;

                var newState = action.Apply(currentNode.State);
                var gCost = currentNode.GCost + action.CalculateCost(currentNode.State);
                var hCost = goal.GetDistance(newState);

                openSet.Enqueue(newNode, gCost + hCost);
            }
        }

        return Plan.Failed(goal);
    }
}
```

---

## Musical Actions

Actions are the building blocks of plans. Each action has:
- **Preconditions**: When can it be used?
- **Effects**: What does it change?
- **Cost**: How "expensive" is it?

### Action Categories

```
Action Categories
=================

┌────────────────────────────────────────────────────────────────────┐
│                         TENSION                                    │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐   │
│  │ UseDominant7    │  │ UseSecondaryDom │  │ UseDiminished7  │   │
│  │ T: +0.15        │  │ T: +0.20        │  │ T: +0.25        │   │
│  │ Cost: 0.8       │  │ Cost: 1.0       │  │ Cost: 1.2       │   │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘   │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐   │
│  │ IncreaseHarmRhy │  │ AscendingSeq    │  │ ChromaticVL     │   │
│  │ T: +0.10        │  │ T: +0.10        │  │ T: +0.10        │   │
│  │ Cost: 0.7       │  │ Cost: 0.9       │  │ Cost: 0.85      │   │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘   │
└────────────────────────────────────────────────────────────────────┘

┌────────────────────────────────────────────────────────────────────┐
│                        RESOLUTION                                  │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐   │
│  │ AuthenticCad    │  │ PlagalCadence   │  │ DecreaseHarmRhy │   │
│  │ T: -0.3         │  │ T: -0.15        │  │ T: -0.1         │   │
│  │ Stability: +0.3 │  │ Stability: +0.2 │  │ Energy: -0.1    │   │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘   │
└────────────────────────────────────────────────────────────────────┘

┌────────────────────────────────────────────────────────────────────┐
│                          THEMATIC                                  │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐   │
│  │ IntroduceMotif  │  │ DevelopMotif    │  │ RecapitulateThm │   │
│  │ Interest: +0.2  │  │ Interest: +0.15 │  │ Interest: +0.1  │   │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘   │
└────────────────────────────────────────────────────────────────────┘

┌────────────────────────────────────────────────────────────────────┐
│                          TEXTURE                                   │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐   │
│  │ ThickenTexture  │  │ ThinTexture     │  │ AddCounterMeldy │   │
│  │ Energy: +0.15   │  │ Energy: -0.1    │  │ Interest: +0.2  │   │
│  │ Warmth: +0.1    │  │ Warmth: -0.05   │  │ Energy: +0.1    │   │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘   │
└────────────────────────────────────────────────────────────────────┘

┌────────────────────────────────────────────────────────────────────┐
│                           COLOR                                    │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐   │
│  │ BrightenMode    │  │ DarkenMode      │  │ ModalInterchange│   │
│  │ Brightness: +   │  │ Brightness: -   │  │ Surprise: +     │   │
│  │ Valence: +      │  │ Valence: -      │  │ Warmth: change  │   │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘   │
└────────────────────────────────────────────────────────────────────┘
```

### Example Action Implementation

```csharp
public static class TensionActions
{
    public static IMusicalAction UseDominantSeventh { get; } = new DominantSeventhAction();

    private sealed class DominantSeventhAction : MusicalActionBase
    {
        public override string Id => "use_dominant_seventh";
        public override string Name => "Use Dominant Seventh";
        public override ActionCategory Category => ActionCategory.Tension;
        public override string Description => "Introduce a V7 chord to create expectation";
        public override double BaseCost => 0.8;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.TensionIncrease(0.15, "V7 has higher tension than V"),
            ActionEffect.StabilityChange(-0.1, "Dominant creates instability"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            ActionPrecondition.NotOnTonic,
        ];

        public override void Apply(CompositionState state)
        {
            base.Apply(state);
            state.Harmonic.ExpectingCadence = true;
            state.Listener.RegisterTensionEvent();
        }
    }
}
```

---

## The Action Library

The library registers and organizes all available actions:

```csharp
public sealed class ActionLibrary
{
    private readonly Dictionary<string, IMusicalAction> _actionsById = new();
    private readonly Dictionary<ActionCategory, List<IMusicalAction>> _actionsByCategory = new();

    public ActionLibrary()
    {
        // Register all built-in actions
        foreach (var action in TensionActions.All) Register(action);
        foreach (var action in ResolutionActions.All) Register(action);
        foreach (var action in ColorActions.All) Register(action);
        foreach (var action in ThematicActions.All) Register(action);
        foreach (var action in TextureActions.All) Register(action);
    }

    /// Gets executable actions given current state
    public IEnumerable<IMusicalAction> GetExecutableActions(CompositionState state)
    {
        return AllActions.Where(a => a.CanExecute(state));
    }

    /// Gets actions that move toward a target state
    public IEnumerable<(IMusicalAction action, double relevance)> FindActionsTowardTarget(
        EmotionalState current,
        EmotionalState target,
        CompositionState state)
    {
        // Calculate relevance based on how well action moves us toward target
        // Positive relevance = right direction
        // Cost factor included
    }
}
```

---

## Goals and Distance

Goals define what we're trying to achieve:

```csharp
public sealed class GOAPGoal
{
    public string Name { get; init; }
    public double Priority { get; init; }
    public IReadOnlyList<GoalCondition> Conditions { get; init; }

    /// Is the goal satisfied by the current state?
    public bool IsSatisfied(WorldState state)
    {
        return Conditions.All(c => c.IsSatisfied(state));
    }

    /// How far is the state from the goal? (heuristic for A*)
    public double GetDistance(WorldState state)
    {
        var totalDistance = 0.0;
        foreach (var condition in Conditions)
        {
            totalDistance += condition.GetDistance(state);
        }
        return totalDistance;
    }
}

// Example goals
var buildClimax = new GOAPGoal
{
    Name = "Build to Climax",
    Priority = 1.0,
    Conditions = [
        new GoalCondition("tension", GoalOperator.GreaterOrEqual, 0.8),
        new GoalCondition("energy", GoalOperator.GreaterOrEqual, 0.7)
    ]
};

var createResolution = new GOAPGoal
{
    Name = "Resolve Tension",
    Priority = 0.9,
    Conditions = [
        new GoalCondition("tension", GoalOperator.LessOrEqual, 0.2),
        new GoalCondition("stability", GoalOperator.GreaterOrEqual, 0.8)
    ]
};
```

---

## Plans and Execution

A plan is a sequence of actions to reach a goal:

```csharp
public sealed class Plan
{
    public GOAPGoal Goal { get; init; }
    public IReadOnlyList<GOAPAction> Actions { get; init; }
    public double TotalCost { get; init; }
    public WorldState ExpectedFinalState { get; init; }

    public bool IsValid => Actions.Count > 0;
    public bool IsEmpty => Actions.Count == 0 && Goal.IsSatisfied(ExpectedFinalState);

    public static Plan Failed(GOAPGoal goal) => new() { Goal = goal, Actions = [] };
    public static Plan Empty(GOAPGoal goal, WorldState state) =>
        new() { Goal = goal, Actions = [], ExpectedFinalState = state };
}
```

### Example Plan Execution

```
Plan: Build to Climax
=====================

Step 1: UseDominantSeventh
  Preconditions: ✓ Not on tonic
  Effects: Tension +0.15, Stability -0.1
  State: T=0.45, S=0.60

Step 2: IncreaseHarmonicRhythm
  Preconditions: ✓ (none)
  Effects: Tension +0.10, Energy +0.15
  State: T=0.55, S=0.60, E=0.65

Step 3: AscendingSequence
  Preconditions: ✓ (none)
  Effects: Tension +0.10, Energy +0.10
  State: T=0.65, S=0.60, E=0.75

Step 4: UseDiminishedSeventh
  Preconditions: ✓ Tension < 0.9, ✓ Stability > 0.2
  Effects: Tension +0.25, Stability -0.25
  State: T=0.90, S=0.35, E=0.75

Goal Reached: Tension ≥ 0.8 ✓
```

---

## Multi-Goal Planning

The planner can handle multiple goals by priority:

```csharp
public Plan CreatePlan(WorldState current, IEnumerable<GOAPGoal> goals)
{
    var sortedGoals = goals.OrderByDescending(g => g.Priority);

    foreach (var goal in sortedGoals)
    {
        var plan = CreatePlan(current, goal);
        if (plan.IsValid || plan.IsEmpty)
        {
            return plan;
        }
    }

    // Return failed plan for highest priority goal
    return Plan.Failed(sortedGoals.First());
}
```

---

## Constrained Planning

Limit actions to specific categories:

```csharp
// Only use tension and texture actions
var constrainedPlan = planner.CreateConstrainedPlan(
    currentState,
    goal,
    allowedCategories: [ActionCategory.Tension, ActionCategory.Texture]
);
```

---

## Connection to Narrative

GOAP integrates with the narrative system (see [Part 7](./07-storyteller-integration.md)):

```
Narrative → GOAP Connection
===========================

┌─────────────────────────────────────────────────────────────────────┐
│                     NARRATIVE TEMPLATE                              │
│  ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐ │
│  │  Setup  │→ │ Rising  │→ │ Climax  │→ │ Falling │→ │ Resolve │ │
│  │ Action  │  │ Action  │  │         │  │ Action  │  │         │ │
│  └────┬────┘  └────┬────┘  └────┬────┘  └────┬────┘  └────┬────┘ │
└───────┼───────────┼───────────┼───────────┼───────────┼──────────┘
        │           │           │           │           │
        ▼           ▼           ▼           ▼           ▼
┌─────────────────────────────────────────────────────────────────────┐
│                        GOAP GOALS                                   │
│  "Establish  "Build      "Peak       "Release    "Complete         │
│   theme"      tension"    tension"    tension"    resolution"      │
│  T: 0.2-0.4  T: 0.5-0.7  T: ≥0.8    T: 0.4-0.6  T: ≤0.2           │
│  S: ≥0.7     S: 0.4-0.6  E: ≥0.7    S: 0.5-0.7  S: ≥0.8           │
└───────────────────────────────────────────────────────────────────────┘
        │           │           │           │           │
        ▼           ▼           ▼           ▼           ▼
┌─────────────────────────────────────────────────────────────────────┐
│                        GOAP PLANS                                   │
│  [IntroMotif] [V7,Seq,   [Dim7,     [AuthCad,   [PlagalCad,        │
│               HarmRhy]   Texture]    ThinTex]   ReturnThm]         │
└───────────────────────────────────────────────────────────────────────┘
```

---

## Why GOAP for Music?

| Traditional Approach | GOAP Approach |
|---------------------|---------------|
| "Apply rule: after V, play I" | "Goal: resolve tension. Plan: use authentic cadence" |
| Local decisions only | Global goal-driven planning |
| No awareness of narrative | Plans fit narrative arc |
| Fixed rules | Dynamic action selection |
| Predictable output | Varied but coherent output |

---

## Academic Sources

### GOAP in Games
- Orkin, J. (2006). "Three States and a Plan: The A.I. of F.E.A.R." *Game Developers Conference*.
- Champandard, A.J. (2007). *Behavior Trees for Next-Gen AI*. AiGameDev.com.

### Planning Theory
- Fikes, R.E. & Nilsson, N.J. (1971). "STRIPS: A new approach to the application of theorem proving to problem solving." *Artificial Intelligence*, 2(3-4), 189-208.

### Music and AI Planning
- Herremans, D. & Chew, E. (2017). "MorpheuS: Generating structured music with constrained patterns and tension." *IEEE Transactions on Affective Computing*.

---

## Key Takeaways

1. **Goal-driven composition**: Define what you want, let the planner find how
2. **A* search**: Optimal paths through action space
3. **Action = Preconditions + Effects + Cost**: Musical techniques as planning operators
4. **Library organization**: Categories for different musical purposes
5. **Narrative integration**: GOAP bridges high-level story to low-level music

---

**Next**: [Part 7: The Storyteller - Weaving It Together](./07-storyteller-integration.md)

---

*This document is part of the [Music Storyteller SDK](https://github.com/BeyondImmersion/bannou) documentation.*
