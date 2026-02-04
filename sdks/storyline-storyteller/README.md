# Storyline Storyteller SDK

Narrative-driven story composition using formal narrative theory and GOAP planning. Designed for procedural story generation in games.

## Overview

This SDK builds on `StorylineTheory` to provide higher-level narrative generation capabilities:

- **Templates**: Pre-defined story structures for common patterns
- **Actions**: GOAP actions that modify narrative state
- **Planning**: A* planner for finding action sequences to reach narrative goals
- **Engagement**: Real-time audience engagement estimation

## Design Philosophy

The Storyline Storyteller SDK follows the same architecture as the Music Storyteller SDK:

1. **Theory First**: All generation is grounded in narrative theory from `StorylineTheory`
2. **GOAP Integration**: Story actions are designed for Goal-Oriented Action Planning
3. **State-Driven**: Narrative progresses through a 6-dimensional state space
4. **Deterministic**: Seeded generation produces reproducible results

## Key Components

### Narrative Actions

Actions modify narrative state and represent story beats:

```csharp
var action = NarrativeActions.RaiseStakes;
var newState = action.Apply(currentState);
// Stakes increased, possibly tension too
```

### Story Planner

The planner finds action sequences to reach narrative goals:

```csharp
var planner = new StoryPlanner();
var plan = planner.Plan(
    currentState: NarrativeState.Equilibrium,
    goalState: NarrativeState.Presets.Climax,
    availableActions: NarrativeActions.All,
    maxDepth: 20);
```

### Templates

Pre-built narrative structures for common patterns:

```csharp
var template = NarrativeTemplates.HeroJourney;
var beats = template.GetBeats(totalDuration: TimeSpan.FromHours(2));
```

## Dependencies

- `BeyondImmersion.Bannou.StorylineTheory` - Narrative theory primitives

## Usage with ABML

This SDK integrates with the ABML behavior system. Story actions can be:
1. Triggered by ABML behavior trees
2. Used to guide NPC decision-making
3. Evaluated for narrative coherence scoring

## Thread Safety

All static classes are thread-safe. Planner instances maintain internal state and should not be shared across threads.
