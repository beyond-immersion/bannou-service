# The Storyteller Layer: Behavioral Composition Architecture

> **Status**: Architectural Vision
> **Created**: 2026-01-17
> **Parent Documents**: MUSIC-IMPLEMENTATION-ROADMAP.md, MUSIC-THEORY-FOUNDATION.md
> **Purpose**: Define the goal-oriented behavioral layer that transforms note generation into musical storytelling

---

## Executive Summary

This document describes the **Storyteller Layer** - a GOAP (Goal-Oriented Action Planning) behavior that sits above the music theory engine and provides **compositional intent**. This is the missing piece that transforms algorithmically-correct note sequences into emotionally coherent music.

**The Core Insight**: Music isn't generated - it's *told*. A composition is a narrative with intention, memory, and destination. The Storyteller is the layer that knows *why* we're making each musical choice.

---

## The Problem: Generation vs. Composition

### What We Have (The Low-Level Engine)

Our music-theory SDK provides excellent building blocks:
- **Pitch/Interval/Scale/Chord**: Correct music theory primitives
- **ProgressionGenerator**: Produces harmonically valid chord sequences
- **MelodyGenerator**: Creates scale-appropriate melodies with contour
- **MotifLibrary**: Style-specific melodic fragments
- **StyleDefinition**: Data-driven style configurations from 53k+ analyzed tunes
- **Phrase/Period**: Structural containers for musical ideas
- **VoiceLeader**: Smooth chord transitions
- **MIDI-JSON Output**: Playable results

### What's Missing

Even with all these pieces working correctly, the result sounds like **"correct notes wandering around"** rather than **"music with purpose"**.

Why? Because the generators make choices through **weighted random selection**:
- "Pick a chord that's harmonically valid"
- "Pick a note that fits the scale and contour"
- "Maybe insert a motif here"

There's no *why*. No *intention*. No *story being told*.

### What Humans Do Differently

When a human composer writes, they have a mental model:
- "I want this section to feel like longing"
- "We've been building tension - time to release"
- "That motif from the opening needs to come back here for closure"
- "This is the climax - everything should converge here"

**The Storyteller Layer encodes this intentionality as a GOAP behavior.**

---

## Architecture Overview

### Current Architecture (Bottom-Up)
```
StyleDefinition → ProgressionGenerator → MelodyGenerator → MIDI-JSON
                  ↓                      ↓
              (weighted random)      (weighted random)
```

Each layer makes locally-valid choices without global awareness.

### Proposed Architecture (Top-Down Intent)
```
┌─────────────────────────────────────────────────────────────┐
│                    STORYTELLER BEHAVIOR                      │
│                                                              │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────┐   │
│  │  Narrative   │  │  Emotional   │  │     Planning     │   │
│  │  Templates   │  │ State Model  │  │      (GOAP)      │   │
│  └──────────────┘  └──────────────┘  └──────────────────┘   │
│         │                │                    │              │
│         └────────────────┼────────────────────┘              │
│                          ↓                                   │
│              ┌───────────────────────┐                       │
│              │   INTENT GENERATION   │                       │
│              │                       │                       │
│              │ "For the next 4 bars: │                       │
│              │  - Build tension 0.2  │                       │
│              │  - Avoid tonic        │                       │
│              │  - Use counter-motif  │                       │
│              │  - Ascending energy"  │                       │
│              └───────────────────────┘                       │
│                          │                                   │
└──────────────────────────┼───────────────────────────────────┘
                           ↓
┌──────────────────────────────────────────────────────────────┐
│                   MUSIC THEORY ENGINE                         │
│  (Receives intent, generates music that SERVES the intent)   │
│                                                              │
│  StyleDefinition → ProgressionGenerator → MelodyGenerator    │
│                    ↓                      ↓                  │
│              (directed by intent)    (goal-seeking)          │
└──────────────────────────────────────────────────────────────┘
                           ↓
                      MIDI-JSON Output
```

**Key Shift**: The generators don't decide *what* to create - they receive *intent* and figure out *how* to achieve it.

---

## The Emotional State Space

Music exists in an emotional space. The Storyteller tracks position in this space and plans trajectories through it.

### Primary Dimensions

```
              High Tension
                   │
                   │     ╭───────────╮
                   │    ╱  CLIMAX    ╲
                   │   ╱   ZONE       ╲
                   │  ╱                ╲
    Dark ──────────┼──────────────────────── Bright
     │             │╲                ╱  │
     │             │ ╲  RESOLUTION  ╱   │
     │             │  ╲   ZONE     ╱    │
     │             │   ╰───────────╯    │
                   │
              Low Tension
```

### Dimension Definitions

| Dimension | Low (0.0) | High (1.0) | Musical Markers |
|-----------|-----------|------------|-----------------|
| **Tension** | Relaxed, resolved, stable | Unresolved, expectant, urgent | Dissonance, harmonic rhythm, melodic instability |
| **Brightness** | Dark, minor, melancholic | Light, major, joyful | Mode, register, chord quality |
| **Energy** | Still, spacious, calm | Driving, dense, active | Tempo, rhythmic density, articulation |
| **Warmth** | Cool, distant, stark | Warm, intimate, lush | Timbre, register, voicing density |
| **Stability** | Wandering, searching | Grounded, home | Tonal center clarity, pedal tones |

### Derived States

Complex emotional states emerge from dimension combinations:

| Emotional State | Tension | Brightness | Energy | Description |
|-----------------|---------|------------|--------|-------------|
| **Longing** | 0.6 | 0.3 | 0.3 | Minor, slow, reaching upward |
| **Triumph** | 0.3 | 0.9 | 0.8 | Major, resolved, powerful |
| **Melancholy** | 0.4 | 0.2 | 0.2 | Dark, gentle, introspective |
| **Joy** | 0.2 | 0.8 | 0.7 | Bright, bouncy, resolved |
| **Anticipation** | 0.7 | 0.5 | 0.6 | Building, unresolved, forward |
| **Peace** | 0.1 | 0.5 | 0.2 | Neutral, still, complete |
| **Urgency** | 0.9 | 0.4 | 0.9 | Driving, unresolved, intense |
| **Wonder** | 0.5 | 0.6 | 0.3 | Suspended, open, exploratory |

---

## The Composition State Model

The Storyteller maintains awareness of the composition's current state across multiple domains.

### Position State
```yaml
position:
  current_bar: 24
  total_bars: 64
  current_section: "B"
  section_bar: 8           # Bar within current section
  phase: "development"     # intro | exposition | development | climax | resolution | coda
  normalized_position: 0.375  # 0-1 progress through piece
```

### Emotional State
```yaml
emotional:
  tension: 0.7
  brightness: 0.4
  energy: 0.6
  warmth: 0.5
  stability: 0.4

  trajectory:              # Where we're heading
    target_tension: 0.9
    target_brightness: 0.3
    bars_to_target: 8
```

### Harmonic State
```yaml
harmonic:
  current_key: "D dorian"
  original_key: "D dorian"
  modulation_distance: 0   # Semitones from original

  tonal_stability: 0.5     # How "at home" we feel
  last_chord_function: "dominant"
  last_cadence_type: "half"
  bars_since_tonic: 8
  bars_since_cadence: 4

  harmonic_rhythm: "moderate"  # slow | moderate | fast
```

### Thematic State (Memory)
```yaml
thematic:
  main_motif:
    id: "opening_gesture"
    total_uses: 3
    last_use_bar: 8
    bars_since_use: 16
    transformations_used: ["original", "sequence"]

  secondary_material:
    - id: "counter_melody"
      uses: 2
      last_use_bar: 16
    - id: "rhythmic_hook"
      uses: 4
      last_use_bar: 20

  introduced_material: ["opening_gesture", "counter_melody", "rhythmic_hook", "bridge_figure"]

  listener_familiarity:    # How well-established is each idea?
    opening_gesture: 0.8
    counter_melody: 0.5
    rhythmic_hook: 0.7
```

### Listener Model
```yaml
listener:
  expected_resolution: true    # Has tension created expectation?
  surprise_budget: 0.3         # How much unexpected is acceptable?
  attention_level: 0.7         # Engagement (drops if too predictable or chaotic)

  exposure:
    tonic_chord: 12           # Times heard
    main_motif: 3
    current_mode: 24          # Bars in this mode
```

---

## Narrative Templates

Different musical "stories" have different shapes. The Storyteller selects and follows a narrative template.

### Template Structure

```yaml
narrative_template:
  id: string
  name: string
  description: string
  imagery: string[]              # Evocative descriptions for inspiration
  total_phases: int
  phases: Phase[]
```

### Phase Structure

```yaml
phase:
  name: string
  relative_duration: float       # Fraction of total piece

  emotional_target:
    tension: float
    brightness: float
    energy: float
    # ... other dimensions

  harmonic_character:
    tonal_stability: string      # "grounded" | "wandering" | "searching"
    preferred_functions: string[] # ["tonic", "subdominant"] etc.
    cadence_type: string?

  thematic_goals:
    introduce_new: boolean
    develop_existing: string[]   # IDs of material to develop
    recall: string[]             # IDs of material to bring back

  musical_character:
    mode_preference: string[]
    register: string             # "low" | "middle" | "high" | "expanding"
    texture: string              # "sparse" | "moderate" | "dense"
    ornamentation: string        # "minimal" | "moderate" | "elaborate"
    rhythmic_character: string   # "spacious" | "flowing" | "driving"
```

### Example: Journey and Return (Classic Celtic)

```yaml
id: journey_and_return
name: "Journey and Return"
description: "Leave home, adventure into the unknown, return transformed"
imagery:
  - "Setting out on a misty morning"
  - "The road stretches ahead"
  - "Strange lands, new sights"
  - "The longing for home"
  - "The familiar hills appear"
  - "Home at last, but changed"

phases:
  - name: home
    relative_duration: 0.25
    emotional_target:
      tension: 0.2
      brightness: 0.6
      energy: 0.4
      warmth: 0.7
      stability: 0.9
    harmonic_character:
      tonal_stability: "grounded"
      preferred_functions: ["tonic", "subdominant"]
      cadence_type: "half"  # Open ending, more to come
    thematic_goals:
      introduce_new: true
      develop_existing: []
      recall: []
    musical_character:
      mode_preference: ["major", "mixolydian"]
      register: "middle"
      texture: "moderate"
      ornamentation: "moderate"
      rhythmic_character: "flowing"

  - name: departure
    relative_duration: 0.25
    emotional_target:
      tension: 0.4
      brightness: 0.5
      energy: 0.5
      warmth: 0.5
      stability: 0.6
    harmonic_character:
      tonal_stability: "moving"
      preferred_functions: ["dominant", "secondary_dominant"]
      cadence_type: null  # No strong cadence
    thematic_goals:
      introduce_new: true
      develop_existing: ["main_motif"]
      recall: []
    musical_character:
      mode_preference: ["dorian", "mixolydian"]
      register: "middle_to_high"
      texture: "moderate"
      ornamentation: "moderate"
      rhythmic_character: "forward"

  - name: adventure
    relative_duration: 0.25
    emotional_target:
      tension: 0.7
      brightness: 0.4
      energy: 0.7
      warmth: 0.4
      stability: 0.3
    harmonic_character:
      tonal_stability: "wandering"
      preferred_functions: ["secondary", "borrowed"]
      cadence_type: "deceptive"
    thematic_goals:
      introduce_new: true
      develop_existing: ["main_motif", "secondary_material"]
      recall: []
    musical_character:
      mode_preference: ["dorian", "minor"]
      register: "full_range"
      texture: "dense"
      ornamentation: "elaborate"
      rhythmic_character: "driving"

  - name: return
    relative_duration: 0.25
    emotional_target:
      tension: 0.3
      brightness: 0.7
      energy: 0.5
      warmth: 0.8
      stability: 0.85
    harmonic_character:
      tonal_stability: "resolving"
      preferred_functions: ["dominant", "tonic"]
      cadence_type: "authentic_perfect"
    thematic_goals:
      introduce_new: false
      develop_existing: []
      recall: ["main_motif"]  # The return of the theme
    musical_character:
      mode_preference: ["major", "mixolydian"]
      register: "middle"
      texture: "moderate"
      ornamentation: "moderate"
      rhythmic_character: "settling"
```

### Example: Autumn to Winter (Seasonal)

```yaml
id: autumn_to_winter
name: "Autumn to Winter"
description: "The fading warmth of autumn giving way to winter's stark beauty"
imagery:
  - "Golden light through changing leaves"
  - "Warmth fading from the air"
  - "Leaves falling in slow spirals"
  - "Bare branches against grey sky"
  - "First frost, crystalline stillness"
  - "Stark beauty in the silence"

phases:
  - name: golden_autumn
    relative_duration: 0.3
    emotional_target:
      tension: 0.3
      brightness: 0.7
      energy: 0.5
      warmth: 0.8
      stability: 0.7
    harmonic_character:
      tonal_stability: "grounded"
      preferred_functions: ["tonic", "subdominant"]
    thematic_goals:
      introduce_new: true
    musical_character:
      mode_preference: ["major", "mixolydian"]
      register: "middle"
      texture: "warm"
      ornamentation: "flowing"
      rhythmic_character: "gentle"

  - name: fading
    relative_duration: 0.25
    emotional_target:
      tension: 0.5
      brightness: 0.5
      energy: 0.4
      warmth: 0.5
      stability: 0.5
    harmonic_character:
      tonal_stability: "shifting"
      preferred_functions: ["subdominant", "modal_interchange"]
    thematic_goals:
      develop_existing: ["main_motif"]
    musical_character:
      mode_preference: ["mixolydian", "dorian"]  # Transition
      register: "middle_descending"
      texture: "thinning"
      contour_preference: "predominantly_descending"
      rhythmic_character: "slowing"

  - name: stillness
    relative_duration: 0.25
    emotional_target:
      tension: 0.4
      brightness: 0.3
      energy: 0.2
      warmth: 0.3
      stability: 0.6
    harmonic_character:
      tonal_stability: "settled_but_cold"
      preferred_functions: ["tonic_minor", "subdominant"]
    thematic_goals:
      develop_existing: ["main_motif"]
      recall: []
    musical_character:
      mode_preference: ["minor", "dorian"]
      register: "lower"
      texture: "sparse"
      ornamentation: "minimal"
      rhythmic_character: "spacious"

  - name: winter_beauty
    relative_duration: 0.2
    emotional_target:
      tension: 0.2
      brightness: 0.4
      energy: 0.3
      warmth: 0.2
      stability: 0.8
    harmonic_character:
      tonal_stability: "resolved_in_new_place"
      preferred_functions: ["tonic"]
      cadence_type: "plagal"  # Gentle, not triumphant
    thematic_goals:
      recall: ["main_motif"]  # But transformed
    musical_character:
      mode_preference: ["minor", "dorian"]
      register: "middle"
      texture: "clear"
      ornamentation: "crystalline"  # Sparse but precise
      rhythmic_character: "still"
```

### Example: Tension and Release (Universal)

```yaml
id: tension_and_release
name: "Tension and Release"
description: "The fundamental musical narrative - build and resolve"
imagery:
  - "Gathering clouds"
  - "The storm builds"
  - "Lightning strikes"
  - "Rain falls"
  - "Clearing skies"

phases:
  - name: stability
    relative_duration: 0.2
    emotional_target: { tension: 0.2, stability: 0.8 }

  - name: disturbance
    relative_duration: 0.2
    emotional_target: { tension: 0.5, stability: 0.5 }

  - name: building
    relative_duration: 0.25
    emotional_target: { tension: 0.8, stability: 0.3, energy: 0.8 }

  - name: climax
    relative_duration: 0.1
    emotional_target: { tension: 0.95, energy: 0.95 }

  - name: release
    relative_duration: 0.15
    emotional_target: { tension: 0.3, stability: 0.7 }

  - name: peace
    relative_duration: 0.1
    emotional_target: { tension: 0.1, stability: 0.9 }
```

---

## The Action-Effect System

The Storyteller achieves emotional goals through musical actions. Each action has predictable effects on the emotional state.

### Action Categories

#### Tension Actions
```yaml
tension_actions:
  - action: use_dominant_seventh
    effects:
      tension: +0.15
      stability: -0.1
    preconditions: []

  - action: use_secondary_dominant
    effects:
      tension: +0.2
      stability: -0.2
      brightness: -0.05
    preconditions: []

  - action: use_diminished_chord
    effects:
      tension: +0.25
      stability: -0.25
      brightness: -0.1
    preconditions: []

  - action: increase_harmonic_rhythm
    effects:
      tension: +0.1
      energy: +0.15
    preconditions: []

  - action: ascending_sequence
    effects:
      tension: +0.1
      energy: +0.1
      brightness: +0.05
    preconditions: []

  - action: increase_rhythmic_density
    effects:
      energy: +0.2
      tension: +0.05
    preconditions: []

  - action: extend_phrase_beyond_expectation
    effects:
      tension: +0.15
    preconditions:
      - listener.expected_resolution: true

  - action: chromatic_voice_leading
    effects:
      tension: +0.1
      color: "searching"
    preconditions: []
```

#### Resolution Actions
```yaml
resolution_actions:
  - action: authentic_cadence
    effects:
      tension: -0.4
      stability: +0.5
    preconditions:
      - tension: ">0.3"  # Only meaningful if there was tension
      - last_chord_function: "dominant"

  - action: plagal_cadence
    effects:
      tension: -0.2
      stability: +0.3
      warmth: +0.1
    preconditions:
      - tension: ">0.2"

  - action: half_cadence
    effects:
      tension: +0.1  # Actually increases tension (unresolved)
      stability: -0.1
    preconditions: []
    description: "Creates pause without resolution"

  - action: deceptive_cadence
    effects:
      tension: +0.05  # Maintains tension
      surprise: +0.3
    preconditions:
      - listener.expected_resolution: true

  - action: return_to_tonic_key
    effects:
      stability: +0.3
      tension: -0.2
    preconditions:
      - harmonic.modulation_distance: ">0"

  - action: decrease_rhythmic_density
    effects:
      energy: -0.2
      tension: -0.1
    preconditions: []
```

#### Color/Character Actions
```yaml
color_actions:
  - action: modal_interchange_bVI
    effects:
      brightness: -0.2
      color: "bittersweet"
    preconditions: []

  - action: modal_interchange_bVII
    effects:
      brightness: -0.1
      color: "modal"
    preconditions: []

  - action: dorian_inflection
    effects:
      brightness: -0.1
      warmth: +0.05
      color: "celtic_melancholy"
    preconditions:
      - current_mode: "minor"

  - action: picardy_third
    effects:
      brightness: +0.3
      warmth: +0.2
      surprise: +0.2
    preconditions:
      - current_mode: "minor"
      - phase: "resolution"

  - action: move_to_relative_minor
    effects:
      brightness: -0.3
      tension: +0.1
      stability: -0.1
    preconditions:
      - current_mode: "major"

  - action: move_to_relative_major
    effects:
      brightness: +0.3
      stability: +0.1
    preconditions:
      - current_mode: "minor"
```

#### Thematic Actions
```yaml
thematic_actions:
  - action: introduce_main_motif
    effects:
      thematic.main_motif_uses: +1
      listener.familiarity.main_motif: +0.3
    preconditions:
      - phase: "exposition"

  - action: return_main_motif
    effects:
      stability: +0.2
      listener.familiarity.main_motif: +0.1
      emotional: "recognition"
    preconditions:
      - thematic.bars_since_main_motif: ">8"

  - action: transform_motif_sequence
    effects:
      tension: +0.1
      development: +0.2
    preconditions:
      - thematic.main_motif_uses: ">1"

  - action: transform_motif_inversion
    effects:
      contrast: +0.2
      development: +0.2
    preconditions:
      - thematic.main_motif_uses: ">1"

  - action: fragment_motif
    effects:
      tension: +0.1
      instability: +0.1
    preconditions:
      - phase: "development"

  - action: combine_motifs
    effects:
      complexity: +0.2
      development: +0.3
    preconditions:
      - thematic.introduced_material.count: ">1"
```

#### Register/Texture Actions
```yaml
register_actions:
  - action: move_to_high_register
    effects:
      brightness: +0.15
      tension: +0.1
    preconditions: []

  - action: move_to_low_register
    effects:
      brightness: -0.1
      warmth: +0.1
      weight: +0.2
    preconditions: []

  - action: expand_register
    effects:
      energy: +0.1
      grandeur: +0.2
    preconditions: []

  - action: contract_register
    effects:
      intimacy: +0.2
      energy: -0.1
    preconditions: []

  - action: thicken_texture
    effects:
      warmth: +0.1
      energy: +0.1
    preconditions: []

  - action: thin_texture
    effects:
      clarity: +0.2
      energy: -0.1
      intimacy: +0.1
    preconditions: []
```

---

## The GOAP Planning Process

### Overview

The Storyteller uses Goal-Oriented Action Planning to select musical actions that move from current state toward phase targets.

```
┌─────────────────────────────────────────────────────────────┐
│                     GOAP PLANNER                            │
│                                                             │
│  1. Assess current state                                    │
│  2. Identify target state (from narrative phase)            │
│  3. Calculate state delta (what needs to change)            │
│  4. Find actions that move toward target                    │
│  5. Select best action considering:                         │
│     - Effect magnitude                                      │
│     - Precondition satisfaction                             │
│     - Side effects                                          │
│     - Musical continuity                                    │
│  6. Execute action (generate musical content)               │
│  7. Update state                                            │
│  8. Repeat until phase complete                             │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### Planning Granularity

The Storyteller plans at multiple levels:

| Level | Scope | Planning Horizon |
|-------|-------|------------------|
| **Narrative** | Whole piece | Select narrative template |
| **Phase** | 8-16 bars | Target emotional state for phase |
| **Section** | 4-8 bars | Specific actions to achieve phase target |
| **Phrase** | 2-4 bars | Detailed musical choices |

### Example Planning Sequence

**Current State:**
```yaml
position: { bar: 16, phase: "exposition" }
emotional: { tension: 0.3, brightness: 0.6, energy: 0.4 }
thematic: { main_motif_uses: 2, bars_since_main_motif: 8 }
```

**Phase Target (Development):**
```yaml
emotional_target: { tension: 0.7, brightness: 0.4, energy: 0.7 }
```

**State Delta:**
```yaml
tension: +0.4
brightness: -0.2
energy: +0.3
```

**Planner Reasoning:**
1. Need to increase tension significantly (+0.4)
2. Need to decrease brightness (-0.2)
3. Need to increase energy (+0.3)
4. Main motif hasn't been heard in 8 bars - opportunity for development

**Selected Action Sequence:**
```yaml
- action: move_to_relative_minor
  expected_effect: { brightness: -0.3, tension: +0.1 }

- action: transform_motif_sequence
  expected_effect: { tension: +0.1, development: +0.2 }

- action: increase_rhythmic_density
  expected_effect: { energy: +0.2, tension: +0.05 }

- action: ascending_sequence
  expected_effect: { tension: +0.1, energy: +0.1 }
```

**Result:** Musical content generated to execute these actions.

---

## ABML Integration

The Storyteller is implemented as an ABML behavior, leveraging the existing GOAP infrastructure.

### Behavior Definition

```yaml
# behaviors/composition/storyteller.abml.yaml
version: "3.0"
metadata:
  id: composition_storyteller
  type: goap_behavior
  description: "Top-level compositional intelligence that plans and directs music generation"

context:
  variables:
    request:
      type: CompositionRequest
      description: "Initial composition request with style, mood, duration"

    narrative:
      type: NarrativeTemplate
      description: "Selected narrative arc"

    state:
      type: CompositionState
      description: "Current state across all domains"

    current_phase:
      type: NarrativePhase
      description: "Current narrative phase"

    section_plan:
      type: List<PlannedAction>
      description: "Actions planned for current section"

# World state model for GOAP
world_model:
  state_variables:
    # Position
    - name: current_bar
      type: int
    - name: current_phase
      type: string

    # Emotional
    - name: tension
      type: float
      range: [0, 1]
    - name: brightness
      type: float
      range: [0, 1]
    - name: energy
      type: float
      range: [0, 1]
    - name: stability
      type: float
      range: [0, 1]

    # Harmonic
    - name: tonal_center
      type: string
    - name: current_mode
      type: string
    - name: bars_since_tonic
      type: int

    # Thematic
    - name: main_motif_uses
      type: int
    - name: bars_since_main_motif
      type: int

# Goals
goals:
  primary:
    - name: complete_narrative
      condition: "current_bar >= total_bars"

    - name: achieve_phase_target
      condition: "emotional_distance(current, phase_target) < 0.1"

  constraints:
    - name: tension_must_resolve
      condition: "if phase == 'resolution' then tension < 0.3"

    - name: main_motif_must_return
      condition: "if phase == 'resolution' then bars_since_main_motif < 8"

    - name: maintain_coherence
      condition: "listener.attention > 0.3"

# Action library reference
actions:
  library: musical_actions
  categories:
    - tension
    - resolution
    - color
    - thematic
    - register

# Planning configuration
planning:
  strategy: goap

  # Plan at section level (4-8 bars)
  planning_scope: section

  # Re-plan when state diverges from expectation
  replan_threshold: 0.2

  # Prefer actions with multiple aligned effects
  action_selection:
    prefer_multi_effect: true
    avoid_contradictory: true
    musical_continuity_weight: 0.3

flows:
  # Main composition flow
  compose:
    trigger: composition.request
    actions:
      # 1. Analyze request and select narrative
      - invoke_behavior:
          behavior: select_narrative
          parameters:
            mood: "${request.mood}"
            duration: "${request.duration}"
            style: "${request.style}"
          result_variable: narrative

      # 2. Initialize state
      - set_variable:
          name: state
          value:
            position: { bar: 0, phase: "${narrative.phases[0].name}" }
            emotional: "${narrative.phases[0].emotional_target}"
            thematic: { main_motif_uses: 0 }

      # 3. Generate each phase
      - foreach:
          items: "${narrative.phases}"
          as: phase
          do:
            - invoke_behavior:
                behavior: compose_phase
                parameters:
                  phase: "${phase}"
                  state: "${state}"
                  narrative: "${narrative}"
                result_variable: phase_content

            - list_append:
                list: composition_sections
                item: "${phase_content}"

            - update_state:
                state: "${state}"
                with: "${phase_content.end_state}"

      # 4. Render to MIDI-JSON
      - service_call:
          service: music
          method: render
          parameters:
            sections: "${composition_sections}"
          result_variable: midi_json

      # 5. Return result
      - return:
          value: "${midi_json}"

  # Phase composition with GOAP planning
  compose_phase:
    actions:
      - set_variable:
          name: current_phase
          value: "${phase}"

      - set_variable:
          name: target_state
          value: "${phase.emotional_target}"

      # Plan actions to reach target state
      - goap_plan:
          current_state: "${state}"
          goal_state: "${target_state}"
          action_library: musical_actions
          constraints:
            - "${phase.harmonic_character}"
            - "${phase.thematic_goals}"
          result_variable: section_plan

      # Execute planned actions
      - foreach:
          items: "${section_plan}"
          as: action
          do:
            - invoke_behavior:
                behavior: execute_musical_action
                parameters:
                  action: "${action}"
                  state: "${state}"
                result_variable: musical_content

            - list_append:
                list: phase_content
                item: "${musical_content}"

            # Update state after action
            - apply_action_effects:
                action: "${action}"
                state: "${state}"

      - return:
          value:
            content: "${phase_content}"
            end_state: "${state}"
```

### Execute Musical Action Behavior

```yaml
# behaviors/composition/execute_musical_action.abml.yaml
version: "3.0"
metadata:
  id: execute_musical_action
  type: composition_behavior
  description: "Translates a planned action into actual musical content"

flows:
  execute:
    actions:
      - cond:
          # Tension actions
          - when: "${action.category == 'tension'}"
            then:
              - invoke_behavior:
                  behavior: generate_tension_passage
                  parameters:
                    action: "${action}"
                    state: "${state}"

          # Resolution actions
          - when: "${action.category == 'resolution'}"
            then:
              - invoke_behavior:
                  behavior: generate_resolution_passage
                  parameters:
                    action: "${action}"
                    state: "${state}"

          # Thematic actions
          - when: "${action.category == 'thematic'}"
            then:
              - invoke_behavior:
                  behavior: generate_thematic_passage
                  parameters:
                    action: "${action}"
                    state: "${state}"

          # Default: delegate to theory engine
          - else:
              - service_call:
                  service: music
                  method: generate_passage
                  parameters:
                    constraints: "${action.to_constraints()}"
                    state: "${state}"
```

---

## Integration with Music Theory Engine

The Storyteller doesn't replace the theory engine - it directs it. The interface between layers is the **Intent**.

### Intent Structure

```csharp
/// <summary>
/// Compositional intent from the Storyteller to the theory engine.
/// Describes WHAT to achieve, not HOW.
/// </summary>
public class CompositionIntent
{
    /// <summary>Target emotional state for this passage.</summary>
    public EmotionalState EmotionalTarget { get; set; }

    /// <summary>Harmonic constraints.</summary>
    public HarmonicIntent Harmony { get; set; }

    /// <summary>Melodic constraints.</summary>
    public MelodicIntent Melody { get; set; }

    /// <summary>Thematic requirements.</summary>
    public ThematicIntent Thematic { get; set; }

    /// <summary>Duration in bars.</summary>
    public int Bars { get; set; }
}

public class HarmonicIntent
{
    public bool AvoidTonic { get; set; }
    public CadenceType? EndingCadence { get; set; }
    public HarmonicFunction[] PreferredFunctions { get; set; }
    public double HarmonicRhythmDensity { get; set; }  // 0-1
    public bool AllowModalInterchange { get; set; }
}

public class MelodicIntent
{
    public ContourShape PreferredContour { get; set; }
    public double Density { get; set; }
    public string[] RegisterPreference { get; set; }  // "high", "expanding", etc.
    public double OrnamentationLevel { get; set; }
    public PitchClass? TargetPitch { get; set; }  // For cadential approach
}

public class ThematicIntent
{
    public string[] MotifIdsToUse { get; set; }
    public MotifTransformation[] AllowedTransformations { get; set; }
    public bool IntroduceNewMaterial { get; set; }
    public double MotifDensity { get; set; }  // How often to use motifs
}
```

### Theory Engine Receives Intent

```csharp
public interface IIntentDrivenGenerator
{
    /// <summary>
    /// Generate musical content that fulfills the given intent.
    /// </summary>
    GeneratedContent Generate(
        CompositionIntent intent,
        CompositionState currentState,
        StyleDefinition style);
}
```

The generators (Progression, Melody, etc.) are modified to accept intent and make choices that serve it, rather than making random choices.

---

## Listener Model

The Storyteller maintains a model of the listener's experience to ensure engagement.

### Listener State Variables

```yaml
listener:
  # Attention/engagement
  attention: 0.7           # 0=lost, 1=fully engaged

  # Expectations
  expected_resolution: true  # Has tension created expectation?
  expected_motif_return: false

  # Familiarity
  familiarity:
    tonic: 0.9             # How "home" is established
    main_motif: 0.7        # How well-known is the theme
    current_mode: 0.8      # How settled in the mode

  # Surprise budget
  surprise_tolerance: 0.4  # How much unexpected before confusion
  recent_surprises: 0.1    # Accumulated recent surprises
```

### Listener-Aware Planning

The Storyteller considers listener state when planning:

```yaml
listener_rules:
  # If attention is dropping, do something interesting
  - condition: "listener.attention < 0.4"
    preferred_actions: ["introduce_new_material", "unexpected_modulation", "texture_change"]

  # If surprise budget is low, be more predictable
  - condition: "listener.recent_surprises > listener.surprise_tolerance"
    avoid_actions: ["deceptive_cadence", "abrupt_modulation"]

  # If resolution is expected, delivering it is satisfying
  - condition: "listener.expected_resolution"
    high_value_actions: ["authentic_cadence"]

  # If motif hasn't been heard in a while, its return creates recognition
  - condition: "familiarity.main_motif > 0.5 AND bars_since_main_motif > 12"
    high_value_actions: ["return_main_motif"]
```

---

## Open Questions for Research

1. **Emotional Dimension Refinement**: Are tension/brightness/energy/warmth/stability the right dimensions? What does music psychology research say?

2. **Action Effect Quantification**: The effect magnitudes (+0.2 tension, etc.) are estimates. How do we calibrate them? Can we learn them from analyzed music?

3. **Listener Model Validation**: How do we validate that our listener model predicts actual human experience?

4. **Narrative Template Discovery**: Can we extract narrative templates from existing compositions? Analyze the emotional arc of famous pieces?

5. **Cross-Cultural Narratives**: Different musical traditions may have different narrative archetypes. How do we model Celtic vs. Baroque vs. Jazz storytelling?

6. **Composer Intention vs. Listener Experience**: The Storyteller models composer intent, but music is ultimately about listener experience. How do these relate?

7. **Emergence and Serendipity**: Human composers sometimes discover unexpected beauty through accident. How does a planning system allow for serendipity?

---

## Next Steps

1. **Define Emotional Dimensions**: Research music psychology to validate/refine the emotional state space

2. **Catalog Narrative Templates**: Document common musical narratives across styles

3. **Formalize Action Effects**: Create a comprehensive action-effect database

4. **Implement CompositionState**: Build the state tracking infrastructure

5. **Build GOAP Integration**: Connect to existing ABML planning system

6. **Test with Simple Narratives**: Start with "Tension and Release" and validate the system produces coherent results

7. **Iterate on Listener Model**: Implement basic attention/expectation tracking

---

## References and Research Findings

This section documents the academic and practical research that informs the Storyteller layer design.

---

### Compositional Process Theories

#### How Composers Actually Work

Research into compositional processes reveals that composers don't work linearly from start to finish, but navigate a space of possibilities guided by intent:

**Constraint-Driven Exploration**: Stravinsky famously said "The more constraints one imposes, the more one frees oneself." Composers set up constraints (key, form, instrumentation, emotional target) and then explore within those boundaries. This validates our GOAP approach—the "goal" provides constraint, and planning finds paths within it.

**Beethoven's Sketchbooks**: Analysis of Beethoven's compositional process shows he worked through multiple layers:
1. Initial melodic "kernels" or motifs
2. Harmonic skeleton
3. Formal architecture
4. Surface elaboration

This maps directly to our hierarchical planning: narrative template → section goals → phrase actions → note selection.

**Nick Collins' Research** (2008, "The Analysis of Generative Music Programs"): Validated that GOAP-style planning is appropriate for music, as composition inherently involves goal-oriented behavior with planning under constraints.

**David Cope's EMI System**: While often criticized for being "merely recombinant," Cope's work demonstrated that musical coherence requires understanding of:
- Signature patterns (our motifs)
- Harmonic expectation (our progression model)
- Voice leading (already implemented)
- Contextual awareness (our CompositionState)

#### The "Double Consciousness"

Research on expert composers (both classical and contemporary) reveals what psychologists call "double consciousness"—the ability to be simultaneously *inside* the music (feeling it emotionally) and *outside* it (analyzing structure and craft).

This directly informs our Listener Model. The composer must model the listener's experience while creating, anticipating:
- What has the listener heard?
- What does the listener expect?
- How can I surprise them without losing them?

---

### Musical Emotion Frameworks

#### The Valence-Arousal-Tension Model

Psychology research consistently validates three primary dimensions of musical emotion:

| Dimension | Description | Musical Correlates |
|-----------|-------------|-------------------|
| **Valence** | Positive/negative affect | Major/minor mode, consonance/dissonance, tempo |
| **Arousal** | Activation level | Tempo, dynamics, note density, register |
| **Tension** | Expectation/resolution | Harmonic distance, rhythmic syncopation, melodic contour |

**Recommendation**: Add `valence` to our emotional state model:

```csharp
public class EmotionalState
{
    public double Tension { get; }    // 0-1: resolution to climax
    public double Brightness { get; } // 0-1: dark to bright
    public double Energy { get; }     // 0-1: calm to energetic
    public double Warmth { get; }     // 0-1: distant to intimate
    public double Stability { get; }  // 0-1: unstable to grounded
    public double Valence { get; }    // 0-1: negative to positive (NEW)
}
```

#### ITPRA Theory (David Huron, 2006)

Huron's **Imagination-Tension-Prediction-Reaction-Appraisal** model explains how music creates emotion:

1. **Imagination**: Listeners imagine what comes next (our expectation tracking)
2. **Tension**: Uncertainty about predictions creates physiological tension
3. **Prediction**: The brain constantly predicts the next event
4. **Reaction**: Automatic response when prediction is confirmed or violated
5. **Appraisal**: Conscious evaluation of the emotional experience

**Key Insight**: Music generates emotion through the *interplay of expectation and outcome*. This validates our emphasis on the Listener Model tracking expectations.

**Implementation Implications**:
- Track predictability of current passage
- Vary between predictable (comforting) and surprising (engaging)
- Surprise without preparation → jarring
- Prepared surprise → delight

#### BRECVEMA Framework (Patrik Juslin)

Juslin identified 8 psychological mechanisms through which music evokes emotion:

| Mechanism | Description | Relevance |
|-----------|-------------|-----------|
| **Brain stem reflex** | Automatic response to loud/sudden | Dynamics, accents |
| **Rhythmic entrainment** | Body syncs to beat | Tempo, pulse strength |
| **Evaluative conditioning** | Learned associations | Genre conventions |
| **Contagion** | Mimicking expressed emotion | Melodic contour, tempo |
| **Visual imagery** | Music evokes images | Programmatic content |
| **Episodic memory** | Personal associations | Style references |
| **Musical expectancy** | Prediction/violation | Harmonic/melodic surprises |
| **Aesthetic judgment** | Appreciation of craft | Sophistication, elegance |

**For procedural generation**: We can directly influence Brain stem, Rhythmic entrainment, Contagion, and Musical expectancy. The others depend on listener context.

---

### Melodic Expectation Models

#### Narmour's Implication-Realization (I-R) Model

Eugene Narmour's theory explains how listeners form melodic expectations:

**Core Principles**:
1. **Registral Direction**: After a small interval, expect continuation in same direction
2. **Intervallic Difference**: After a large interval, expect reversal or smaller interval
3. **Registral Return**: Tendency to return to starting pitch
4. **Closure**: Tension resolves on stable scale degrees

**Implication Types**:
- **Process** (P): Small interval implies continuation (C→D→E)
- **Reversal** (R): Large interval implies reversal (C→G→F)
- **Duplication** (D): Repeated note implies change coming
- **Retrospective** (VR): Pattern clarifies in retrospect

**Implementation**:
```csharp
public enum MelodicImplication
{
    Process,      // Continue direction
    Reversal,     // Change direction
    Duplication,  // Hold, then change
    Closure       // Move toward tonic
}

public MelodicImplication GetImplication(int lastInterval, double phrasePosition)
{
    if (phrasePosition > 0.85) return MelodicImplication.Closure;
    if (Math.Abs(lastInterval) <= 2) return MelodicImplication.Process;
    if (Math.Abs(lastInterval) >= 5) return MelodicImplication.Reversal;
    return MelodicImplication.Process;
}
```

#### Tonal Tension Model (Lerdahl & Krumhansl)

Quantifies harmonic tension based on:
1. **Distance on Circle of Fifths**: More distant = more tension
2. **Chord Quality**: Dissonance adds tension
3. **Hierarchical Position**: Non-tonic chords are inherently tenser

**Tension Calculation**:
```
tension = fifthsDistance(current, tonic) × 0.4 +
          dissonanceLevel(current) × 0.3 +
          metricalWeight(position) × 0.3
```

This validates our harmonic tension tracking in CompositionState.

---

### Generative Theory of Tonal Music (GTTM)

Lerdahl & Jackendoff's foundational work establishes that musical understanding involves:

1. **Grouping Structure**: Segmentation into phrases and sections
2. **Metrical Structure**: Hierarchy of strong/weak beats
3. **Time-Span Reduction**: Identifying structurally important events
4. **Prolongational Reduction**: Tension/relaxation patterns

**For the Storyteller**:
- GTTM validates hierarchical structure (narrative → section → phrase)
- Time-span reduction suggests which notes "matter" for the skeleton
- Prolongational reduction maps directly to our tension curves

**Well-Formedness Rules** (must be obeyed):
- Phrases have clear boundaries
- Metric accents align with grouping boundaries
- Important events fall on strong beats

**Preference Rules** (should be followed when possible):
- Prefer parallel structures (A-A' over A-B)
- Prefer balanced phrase lengths
- Prefer smooth harmonic rhythm

---

### Topic Theory and Musical Semantics

**Topic Theory** (Ratner, Agawu, Monelle) identifies conventional musical gestures that carry meaning:

| Topic | Musical Features | Semantic Association |
|-------|-----------------|---------------------|
| **Pastoral** | 6/8, parallel thirds, drones | Countryside, innocence |
| **Heroic** | Dotted rhythms, brass timbre, major | Triumph, nobility |
| **Lament** | Descending chromatic bass, minor | Grief, tragedy |
| **Hunt** | 6/8, horn fifths, galloping rhythm | Nobility, chase |
| **March** | Duple meter, steady pulse, forte | Military, determination |
| **Singing style** | Lyrical melody, simple accompaniment | Personal expression |

**For Style-Specific Generation**: Topics provide a vocabulary of meaningful gestures. Celtic music has its own topics (jig rhythm = dance, slow air = lament).

---

### Composer Insights on Intent and Expression

#### "Opening Windows, Not Painting Pictures"

Contemporary film composers describe their role not as injecting emotion directly, but creating conditions for emotion to emerge in the listener:

**Hans Zimmer**: "You don't write what's on screen. You write what's *not* on screen—the internal emotional experience."

**John Williams**: "The music should reveal what the characters cannot say."

**Ennio Morricone**: "Melody is the most direct path to the heart, but harmony provides the landscape it travels through."

**For the Storyteller**: We're not encoding "sad" or "happy" directly. We're creating musical conditions that invite those feelings:
- Minor mode + slow tempo + descending melody → invites sadness
- The listener completes the emotional experience

#### Constraint as Creative Tool

**Austin Wintory** (Journey composer): "Every limitation I've ever been given has made the music better. Total freedom is terrifying."

**Koji Kondo** (Nintendo): "The hardware limitations of early games forced me to find the essence of each melody. Every note had to justify its existence."

**For procedural generation**: This validates aggressive constraint in our GOAP goals. The Storyteller shouldn't try to do everything—it should set tight constraints and explore deeply within them.

#### The Architecture of Anticipation

**Howard Shore**: "The Ring theme must be recognizable but never boring. Each statement reveals something new while confirming what the audience already knows."

This describes the fundamental tension in narrative music:
- **Recognition**: Listener connects to familiar material
- **Development**: Material transforms, revealing new facets
- **Satisfaction**: Expectations ultimately fulfilled (perhaps unexpectedly)

---

### Practical Implications for Implementation

#### Emotional State Refinement

Based on research, the emotional state model should include:

```csharp
public sealed class EmotionalState
{
    // Core dimensions (validated by psychology research)
    public double Valence { get; }    // Positive/negative affect
    public double Arousal { get; }    // Activation/energy level
    public double Tension { get; }    // Expectation/resolution

    // Derived dimensions (musical correlates)
    public double Brightness { get; } // Mode, register, timbre-like
    public double Stability { get; }  // Harmonic groundedness
    public double Intimacy { get; }   // Distance/closeness
}
```

#### Expectation Tracking

Implement Narmour-style implication tracking:

```csharp
public sealed class ExpectationModel
{
    public double MelodicPredictability { get; }  // How expected was last note?
    public double HarmonicPredictability { get; } // How expected was last chord?
    public MelodicImplication CurrentImplication { get; }
    public int SurprisesSinceLastResolution { get; }
}
```

#### Topic Recognition

Catalog topics by style for semantic coherence:

```yaml
# topics/celtic.yaml
topics:
  jig:
    meter: 6/8
    tempo_range: [100, 130]
    semantic: dance, celebration
  reel:
    meter: 4/4
    tempo_range: [100, 140]
    semantic: energy, virtuosity
  slow_air:
    meter: 4/4
    tempo_range: [60, 80]
    semantic: lament, longing, memory
```

---

### Key Academic Sources

#### Core Theoretical Works

1. **Lerdahl, F. & Jackendoff, R.** (1983). *A Generative Theory of Tonal Music*. MIT Press.
   - Foundational text on musical grammar and hierarchy

2. **Huron, D.** (2006). *Sweet Anticipation: Music and the Psychology of Expectation*. MIT Press.
   - ITPRA theory, expectation as emotional engine

3. **Narmour, E.** (1990). *The Analysis and Cognition of Basic Melodic Structures*. University of Chicago Press.
   - Implication-Realization model for melodic expectation

4. **Juslin, P. & Sloboda, J.** (2010). *Handbook of Music and Emotion*. Oxford University Press.
   - BRECVEMA framework, comprehensive emotion research

5. **Lerdahl, F.** (2001). *Tonal Pitch Space*. Oxford University Press.
   - Quantitative tension model

#### Computational Creativity

6. **Cope, D.** (2005). *Computer Models of Musical Creativity*. MIT Press.
   - Experiments in Musical Intelligence, recombinant approaches

7. **Collins, N.** (2008). "The Analysis of Generative Music Programs."
   - Framework for evaluating procedural music systems

8. **Miranda, E. & Biles, J.** (2007). *Evolutionary Computer Music*. Springer.
   - Genetic algorithms for music, fitness functions

#### Music Psychology

9. **Krumhansl, C.** (1990). *Cognitive Foundations of Musical Pitch*. Oxford University Press.
   - Tonal hierarchies, key perception

10. **Temperley, D.** (2007). *Music and Probability*. MIT Press.
    - Probabilistic models of musical expectation

11. **Meyer, L.B.** (1956). *Emotion and Meaning in Music*. University of Chicago Press.
    - Foundational text on expectation and meaning

#### Semiotics and Meaning

12. **Monelle, R.** (2000). *The Sense of Music*. Princeton University Press.
    - Topic theory and musical semantics

13. **Hatten, R.** (1994). *Musical Meaning in Beethoven*. Indiana University Press.
    - Markedness, topics, and expressive meaning

---

### Research Gaps and Future Directions

1. **Cross-Cultural Emotion**: Most research focuses on Western tonal music. Celtic music emotion may have different correlates.

2. **Procedural Narrative**: Little research exists on how procedural systems create coherent musical narratives. We are in novel territory.

3. **Real-Time Adaptation**: How should the Storyteller adjust when listener context is unknown? (Game music problem)

4. **Emergent Complexity**: How do simple rules create perceived complexity? This is the core challenge.

5. **Evaluation Metrics**: How do we measure if the Storyteller is "working"? Subjective evaluation is essential but difficult to automate.

---

*This document describes an architectural vision informed by research. Implementation details will evolve as we build and test.*
