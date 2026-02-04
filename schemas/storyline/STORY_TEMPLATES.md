# Story Templates Documentation

This document describes the structure, design decisions, and usage of the story-templates.yaml schema, which provides the phase-based framework for GOAP-driven narrative composition.

## Overview

Story templates define the **shape** of emotional progression through a narrative. They are:

- **Genre-agnostic**: The primary emotional spectrum is determined by genre selection at composition time, not by the template
- **Arc-shaped**: Based on the 6 archetypal emotional arcs from Reagan et al. (2016)
- **Phase-bounded**: Using Save the Cat beat timing for structural consistency
- **GOAP-integrated**: Target states guide action planning within each phase

## The Six Templates

| Template | Arc Pattern | Direction | Phases | Common In |
|----------|-------------|-----------|--------|-----------|
| **Rags to Riches** | Monotonic rise | Positive | 4 | Epic action, Courtship, Redemption |
| **Tragedy** | Monotonic fall | Negative | 4 | Horror, Noir, Disillusionment |
| **Man in a Hole** | Fall-Rise (U) | Positive | 5 | Most popular fiction, Thrillers |
| **Icarus** | Rise-Fall (∩) | Negative | 5 | Cautionary tales, Hubris |
| **Cinderella** | Rise-Fall-Rise | Positive | 5 | Fairy tales, Ultimate triumph |
| **Oedipus** | Fall-Rise-Fall | Negative | 5 | Complex tragedy, False hope |

These six shapes cover approximately 94% of published fiction according to the Reagan analysis.

## Template Structure

Each template contains:

### Arc Metadata
```yaml
rags_to_riches:
  id: 1
  code: "RAGS_TO_RICHES"
  arc_pattern: "up"           # Shape description
  arc_direction: "positive"   # Ends higher than starts
  mathematical_form: "..."    # Approximate function
```

### Compatible Genres
Templates list which genres (and subgenres) are compatible:
```yaml
compatible_genres:
  action: ["epic"]           # Only epic subgenre
  love: ["courtship"]        # Only courtship subgenre
  performance: true          # ALL subgenres
```

### Phase Definitions
Each phase includes:

#### Position Information
```yaml
position:
  stc_center: 0.35    # Target position from STC timing
  floor: 0.15         # Earliest advancement position
  ceiling: 0.55       # Latest (forced) advancement
  validation_band: 0.05  # ±5% tolerance
```

#### Target State
```yaml
target_state:
  primary_spectrum: [0.45, 0.60]  # Range on primary spectrum
  range_description: "Situation improving"
```

#### STC Beats Covered
```yaml
stc_beats_covered:
  - catalyst
  - debate
  - break_into_two
```

#### Transition Triggers
```yaml
transition:
  position_floor: 0.45      # Minimum position
  position_ceiling: 0.60    # Maximum position
  state_requirements:
    primary_spectrum_min: 0.45
```

## Phase Transition Logic

Phases advance using a **hybrid trigger** mechanism:

### Normal Advancement
```
IF position >= floor AND state meets requirements:
    ADVANCE to next phase
```

### Forced Advancement (Deadlock Prevention)
```
ELIF position >= ceiling:
    LOG WARNING "Forced advancement"
    ADVANCE to next phase
```

This prevents:
1. **Speed-running**: Position floor ensures adequate story development
2. **Deadlock**: Position ceiling ensures forward progress even if state conditions aren't perfectly met

## Design Decisions (from STORY_TEMPLATE_ANALYSIS.md)

### Q1: Target State Derivation
**Decision**: Arc-Shape-Derived with STC Anchors

Target spectrum values come from evaluating the arc's mathematical form at phase midpoints, validated against STC beat expectations.

### Q2: Phase Transition Triggers
**Decision**: Hybrid Position Floor + State Ceiling

Position floors prevent speed-running; state requirements ensure narrative coherence; position ceilings prevent deadlock.

### Q3: Multi-Genre Handling
**Decision**: Primary-Secondary Pattern

Primary genre determines the tracked spectrum. Secondary genres add flavor through available actions but don't change the arc shape.

### Q4: Obligatory Scene Coverage
**Decision**: Multiple Actions Per Phase + Static Validation

Phases have action pools (via scene_capacity guideline). Static validation at composition time ensures coverage.

### Q5: Template Count
**Decision**: 6 Templates (One Per Reagan Arc)

Covers 94% of stories. Genres select appropriate arc via `compatible_arcs` in subgenre definitions.

### Q6: Phase Boundary Precision
**Decision**: STC-Derived Ranges with Validation Bands

15 STC beats map to phase boundaries with ±5% tolerance bands for flexibility.

## Composition Workflow

### 1. Select Template
Choose based on desired arc shape and ending (positive/negative).

### 2. Select Primary Genre
Choose content genre from story-grid-genres.yaml. This determines:
- Primary emotional spectrum (from narrative-state.yaml)
- Obligatory scenes that must be satisfied
- Available actions (from story-actions.yaml)

Validate that genre's subgenre includes the template in `compatible_arcs`.

### 3. Optional Secondary Genres
Add internal genre or secondary external genre for layering.

### 4. Validate Coverage (Static)
Before execution, verify:
- All obligatory scenes can be satisfied by available actions
- Action preconditions are satisfiable
- No impossible action sequences

### 5. Generate Initial State
Create NarrativeState with primary spectrum value from phase 1 target_state.

### 6. GOAP Planning
For each phase:
1. Set goal = phase target_state
2. Plan action sequence using story-actions.yaml
3. Execute actions, updating NarrativeState
4. Check transition conditions
5. If met, advance to next phase

## Example: Man in a Hole + Crime/Murder Mystery

```yaml
composition:
  template: "man_in_hole"
  primary_genre: "crime"
  subgenre: "murder_mystery"
  # murder_mystery compatible_arcs includes man_in_hole ✓

initial_state:
  primary_spectrum: 0.60  # justice_injustice: neutral (case not started)
  position: 0.00

phase_1: # Setup
  target: [0.50, 0.70] on justice_injustice
  actions: [introduce_detective, establish_victim]
  advance_at: position >= 0.08 AND state >= 0.45

phase_2: # Descent (crime discovered)
  target: [0.30, 0.45] on justice_injustice
  actions: [inciting_crime, discovers_macguffin]
  advance_at: position >= 0.30 AND state <= 0.45

phase_3: # Nadir (investigation hits wall)
  target: [0.10, 0.30] on justice_injustice
  actions: [all_is_lost, hero_at_mercy_of_villain]
  advance_at: position >= 0.65 AND state <= 0.35

phase_4: # Ascent (breakthrough)
  target: [0.50, 0.75] on justice_injustice
  actions: [exposure_of_criminal, break_into_three]
  advance_at: position >= 0.88 AND state >= 0.50

phase_5: # Resolution (justice served)
  target: [0.70, 0.90] on justice_injustice
  actions: [brought_to_justice]
  # Terminal phase
```

## Validation Rules

### Composition-Time Validation
- **Template-Genre Compatibility**: Subgenre must include template in `compatible_arcs`
- **Obligatory Scene Coverage**: Actions must exist for all required scenes
- **Action Sequence Feasibility**: Valid GOAP path must exist

### Runtime Validation
- **Position Floor**: Cannot advance before minimum position
- **Forced Advancement Warning**: Log when ceiling forces advancement
- **Core Event Requirement**: Must occur before resolution phase

## Schema Dependencies

This schema depends on:
- `emotional-arcs.yaml` - Arc shape definitions
- `save-the-cat-beats.yaml` - STC timing and beat definitions
- `narrative-state.yaml` - Spectrum definitions
- `story-grid-genres.yaml` - Genre definitions with `compatible_arcs`
- `story-actions.yaml` - GOAP actions with preconditions/effects

## References

1. Reagan, A.J., Mitchell, L., Kiley, D., Danforth, C.M., & Dodds, P.S. (2016). "The emotional arcs of stories are dominated by six basic shapes." *EPJ Data Science*.

2. Snyder, B. (2005). *Save the Cat! The Last Book on Screenwriting You'll Ever Need.*

3. Coyne, S. (2015). *The Story Grid: What Good Editors Know.*

## Changelog

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2026-02-04 | Initial implementation |
