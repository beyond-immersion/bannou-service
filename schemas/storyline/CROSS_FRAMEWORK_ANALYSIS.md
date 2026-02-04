# Cross-Framework Mapping Analysis

> **Status**: Analysis In Progress
> **Date**: 2026-02-04
> **Purpose**: Clarify what "cross-framework mapping" actually means and whether NCP is the right approach

---

## The Problem in Plain English

We have four story frameworks. Each describes stories differently:

| Framework | What It Describes | Units | Example |
|-----------|-------------------|-------|---------|
| **Save the Cat** | WHEN things happen | Percentages (0-100%) | "Catalyst at 12%" |
| **Reagan Arcs** | WHAT SHAPE the emotional journey takes | Continuous curve f(t)→[0,1] | "Man in hole: fall then rise" |
| **Propp Functions** | WHAT EVENTS can occur | Discrete events (31 types) | "VILLAINY, DEPARTURE, PURSUIT" |
| **Story Grid** | WHAT MUST happen for genre satisfaction | Obligatory scenes + conventions | "Hero at Mercy of Villain (Action)" |

**The original assumption**: These frameworks describe the same thing in different vocabularies, so we need a "Rosetta Stone" to translate between them.

**The reality**: These frameworks describe **different concerns** that work together, not competing descriptions of the same thing.

---

## The Music SDK Precedent

How does music-storyteller handle this? It doesn't have this problem because:

```
Music SDK Architecture:
┌─────────────────────────────────────────────────┐
│ ONE state space: EmotionalState (6 dimensions)  │
│ - Tension, Energy, Valence, etc.                │
└─────────────────────────────────────────────────┘
          ▲                    ▲
          │                    │
┌─────────────────┐  ┌─────────────────────────┐
│ Actions affect  │  │ Templates define phases │
│ the state space │  │ with target states      │
│ (TensionUp, etc)│  │ (NarrativeTemplate)     │
└─────────────────┘  └─────────────────────────┘
```

**Key insight**: The music SDK invented ONE framework (EmotionalState) and everything operates on it. It doesn't map between competing external frameworks.

**Our situation**: We adopted Story Grid's 10 Life Value spectrums as our state space (NarrativeState). The other frameworks should be **sources for deriving actions and templates**, not things we map between at runtime.

---

## Reframing: Orchestration, Not Translation

Instead of "mapping frameworks to each other," think of it as:

```
Concerns Layer Cake:
┌─────────────────────────────────────────────────────────────┐
│ REQUIREMENTS: What must happen? (Story Grid obligatory scenes)│
│ - Genre X requires scenes A, B, C                            │
├─────────────────────────────────────────────────────────────┤
│ TIMING: When should things happen? (Save the Cat percentages)│
│ - Catalyst by 12%, Midpoint at 50%, etc.                     │
├─────────────────────────────────────────────────────────────┤
│ SHAPE: What trajectory? (Reagan arcs as value curves)        │
│ - Man in hole: NarrativeState falls then rises               │
├─────────────────────────────────────────────────────────────┤
│ EVENTS: What can happen? (Propp-inspired action library)     │
│ - GOAP actions derived from Propp + Story Grid               │
├─────────────────────────────────────────────────────────────┤
│ STATE: Where are we? (NarrativeState - 10 Life Value spectrums)│
│ - LifeDeath=0.3, LoveHate=0.7, etc.                          │
└─────────────────────────────────────────────────────────────┘
```

Each layer answers a different question. They don't translate to each other - they constrain/inform each other.

---

## What We Actually Need

### 1. State Space (DONE)
`narrative-state.yaml` defines NarrativeState with 10 Life Value spectrums.

### 2. Action Library (NOT DONE)
GOAP actions that affect NarrativeState. **Sources**:
- Propp's 31 functions → inspire event types
- Story Grid's Five Commandments → inform action structure
- SDK_FOUNDATIONS already has sketches (ConflictActions, RelationshipActions, etc.)

### 3. Template Library (NOT DONE)
StoryArcTemplates that define phase sequences. **Sources**:
- Save the Cat timing → phase boundaries
- Reagan arcs → target trajectory shape
- Story Grid genres → required scenes per genre

### 4. Genre Constraints (PARTIALLY DONE)
Per-genre requirements. **Sources**:
- Story Grid obligatory scenes → must-have checklist
- Story Grid conventions → world/character requirements
- Subgenre arc directions → compatible Reagan arcs

---

## The NCP Question

### What NCP Provides
- 144 narrative functions (Dramatica vocabulary)
- `custom_appreciation_namespace` for labeling events with multiple framework terms
- Four Throughlines structure
- Nine Dynamics for story outcome constraints

### Why We Considered It
The idea was: map Propp → NCP, STC → NCP, Story Grid → NCP, creating a "hub" vocabulary.

### The Concern
NCP is itself a framework (Dramatica). Mapping Propp to NCP means translating one framework into another - adding complexity, not reducing it.

### Alternative: Skip the Hub
Instead of:
```
Propp ──┐
        ├──► NCP (hub) ──► Our System
STC ────┤
StoryGrid─┘
```

Do this:
```
Propp ────────► Action Library (inspired by)
STC ──────────► Template Timing (use directly)
StoryGrid ────► Genre Constraints (use directly)
Reagan ───────► Trajectory Shape (use directly)
                    │
                    ▼
              NarrativeState
              (our state space)
```

No translation hub needed. Each framework contributes its unique concern.

---

## Breaking Down the Work

### Piece 1: Action Library
**Question**: What GOAP actions do we need?

**Approach**:
1. Review Propp's 31 functions - which are story-agnostic events?
2. Review Story Grid's obligatory scenes - which imply actions?
3. Define actions with preconditions, effects on NarrativeState, and cost

**Output**: `story-actions.yaml` with ~30-50 GOAP actions

**Example** (from SDK_FOUNDATIONS):
```yaml
actions:
  introduce_antagonist:
    preconditions:
      antagonist.known: false
    effects:
      antagonist.known: true
    narrative_state_effect:
      primary_spectrum: -0.2  # Threat introduced (genre-contextual)
    cost: 1.0
```

### Piece 2: Template Library
**Question**: What story arc templates do we support?

**Approach**:
1. Use Save the Cat timing for phase boundaries
2. Use Reagan arcs for trajectory constraints
3. Define phase targets and allowed actions per phase

**Output**: `story-templates.yaml` with ~5-10 templates

**Example**:
```yaml
templates:
  action_adventure:
    genre: action
    reagan_arc: man_in_hole  # Trajectory shape

    phases:
      setup:
        position: [0.0, 0.12]
        target_state: { life_death: [0.6, 0.8] }  # Stable but not perfect

      catalyst:
        position: [0.12, 0.15]
        required_actions: [introduce_antagonist]
        target_state: { life_death: [0.3, 0.5] }  # Threat emerges

      # ... more phases mapped from STC
```

### Piece 3: Genre Constraint Validation
**Question**: Does a generated story satisfy genre requirements?

**Approach**:
1. Enumerate Story Grid obligatory scenes per genre
2. Check if generated action sequence includes required scenes
3. Flag missing requirements

**Output**: Validation logic (not a YAML file, code in SDK)

---

## Comparison: Hub Approach vs Direct Approach

| Aspect | NCP Hub Approach | Direct Approach |
|--------|------------------|-----------------|
| **Complexity** | HIGH: 4 mappings to NCP | LOW: each framework used for its concern |
| **Maintenance** | Must sync 4 mappings | Frameworks isolated |
| **Runtime cost** | Translation overhead | No translation |
| **Flexibility** | Can query "what's the Propp equivalent?" | Can't cross-query (but do we need to?) |
| **Precedent** | None (we'd invent it) | Music SDK pattern |

---

## Recommendation

**Skip the NCP hub. Use each framework for its specific concern:**

1. **NarrativeState** (Story Grid) → The state space ✓ DONE
2. **Action Library** (Propp-inspired) → What can happen
3. **Template Timing** (Save the Cat) → When things happen
4. **Trajectory Shape** (Reagan) → How values change
5. **Genre Constraints** (Story Grid) → What must happen

This matches how music-storyteller works: one state space, actions that affect it, templates that sequence actions.

---

## Open Questions

1. **Do we ever need cross-framework queries?**
   - "What's the Propp equivalent of this STC beat?"
   - If yes, NCP hub has value. If no, skip it.

2. **How do we validate template coverage?**
   - Ensure templates can generate all Story Grid obligatory scenes
   - Ensure timing aligns with STC expectations

3. **What's the minimum viable action library?**
   - Start with Story Grid's obligatory scenes as required actions
   - Add Propp-inspired filler actions for variety

---

## Next Steps

1. **Decide**: Hub approach or direct approach?
2. **If direct**:
   - Define action library schema
   - Enumerate actions from Propp + Story Grid
   - Define template schema
   - Create 2-3 example templates
3. **If hub**:
   - Map Propp → NCP functions
   - Map STC → NCP functions
   - Map Story Grid → NCP functions
   - Then derive actions from NCP

---

## References

- `SDK_FOUNDATIONS.md` - Architecture patterns from music SDK
- `GAP_ANALYSIS.md` - Original gap inventory
- `narrative-state.yaml` - State space definition
- `story-grid-genres.yaml` - Genre definitions with subgenres
- `~/repos/narrative-context-protocol` - NCP schema (if hub approach chosen)
