# Story Actions Analysis & Design Decisions

> **Version**: 1.0
> **Date**: 2026-02-04
> **Status**: Decisions Finalized
> **Scope**: Story action library design for GOAP-based narrative composition

This document captures the analysis and design decisions for implementing story actions in the Bannou storyline composition system. All decisions have been made considering the long-term goals in STORYLINE_COMPOSER.md and the technical patterns established in SDK_FOUNDATIONS.md.

---

## Executive Summary

Story actions are the atomic units that GOAP uses to compose narratives. This analysis addresses five key design decisions:

| Decision | Recommendation |
|----------|---------------|
| Crisis Type Modeling | Template constraint + emergent detection |
| Obligatory Scenes | Hybrid: tagged actions + template validation |
| Conventions | WorldState preconditions + soft validation |
| Genre-Specific Actions | Universal actions with genre-contextual labels |
| Minimum Action Count | ~50 actions with variant system |

These decisions optimize for:
- **Compositional flexibility** (GOAP can find creative paths)
- **Structural integrity** (genre requirements are validated)
- **Library efficiency** (universal actions reduce redundancy)
- **Incremental development** (phased implementation roadmap)

---

## Foundation: What We Already Have

### Decided & Complete

| Component | Status | Source |
|-----------|--------|--------|
| State space (10 Life Value spectrums) | Complete | narrative-state.yaml |
| Framework composition (no NCP hub) | Decided 2026-02-04 | GAP_ANALYSIS.md |
| GOAP action pattern (preconditions/effects/cost) | Sketched | SDK_FOUNDATIONS.md |
| Five action categories | Sketched | SDK_FOUNDATIONS.md |
| Subgenre → arc mapping | Complete | story-grid-genres.yaml |

### Research Complete

| Resource | Content | Location |
|----------|---------|----------|
| Obligatory scenes + conventions | All 12 genres | story-grid-genres.yaml, STORY-GRID-PRIMARY-SOURCE.md |
| Propp functions | 31 functions with variants, pre/post conditions | propp-functions.yaml |
| GOAP action examples | Five categories with spectrum effects | SDK_FOUNDATIONS.md |
| STC timing percentages | Beat positioning | SAVE-THE-CAT-BEAT-SHEET.md |

---

## Architectural Context

### From STORYLINE_COMPOSER.md

The storyline system follows these principles that affect action design:

1. **Emergent Narrative from Archives**: Stories are seeded from compressed character data (personality, history, encounters, backstory). Actions must work with archive-extracted WorldState.

2. **GOAP-Based Planning**: Actions have preconditions, effects, and costs. The planner finds sequences that transform current state → goal state while satisfying constraints.

3. **Lazy Phase Evaluation**: Only the current phase is generated upfront. Next phases generate when continuation triggers fire, using CURRENT world state. Actions don't need to plan entire story arc.

4. **Deterministic Composition**: Same seeds + template + constraints = same plan. Enables caching, replay, moderation.

5. **Regional Watchers as Creative Decision-Makers**: Composers produce plans; gods (Regional Watchers) approve or reject. Actions are mechanics; creative intent comes from template selection.

### From SDK_FOUNDATIONS.md

The GOAP action pattern establishes:

```
StoryAction {
  Id:                   string
  Preconditions:        Dictionary<string, object>  // What must be true
  Effects:              Dictionary<string, object>  // What becomes true
  Cost:                 double                      // Planning weight
  NarrativeStateEffect: StateEffect                 // Spectrum movements
  ChainedAction:        string (optional)           // Paired action
}
```

**Five Action Categories**:
- **ConflictActions**: Opposition & escalation (IntroduceAntagonist, EscalateConflict, BetrayalReveal)
- **RelationshipActions**: Bonds & trust (FormAlliance, SharedOrdeal, RevealConnection, DestroyTrust)
- **MysteryActions**: Information & investigation (PlantClue, RevealSecret, RedHerring, ConnectDots)
- **ResolutionActions**: Crisis & conclusion (ConfrontationBegin/Resolve, Sacrifice, Justice, Restoration)
- **TransformationActions**: Character growth (CharacterGrowth, Revelation)

**Cost Semantics**:
- **Micro (0.5-0.8)**: Pacing, filler, investigation (PlantClue)
- **Standard (1.0-1.5)**: Major beats, transitions (IntroduceAntagonist)
- **Macro (2.0-3.0)**: Climax moments, irreversible (Sacrifice, DestroyTrust)

**Multi-Phase via Chaining**: Complex story beats use two linked actions (ConfrontationBegin → ConfrontationResolve) rather than deferred effects.

---

## Decision 1: Crisis Type Modeling

### Question

Story Grid defines two crisis types:
- **Best Bad Choice**: Two negative options, choose lesser evil
- **Irreconcilable Goods**: Two positive options, can only choose one

How do we represent these in the action schema?

### Analysis

Crises are **not actions themselves**—they're the **structure of the choice** at a decision point. A crisis emerges when:
- Multiple actions are available
- Each action has significant cost
- Choosing one precludes or damages another value spectrum

The same action (e.g., "Sacrifice") could be Best Bad Choice in one context and Irreconcilable Goods in another, depending on available alternatives.

### Decision: Template Constraint + Emergent Detection

Crisis type is a **property of the narrative moment**, not the action.

**Implementation**:

```yaml
# Template defines crisis requirement at specific phases
phase:
  name: "all_is_lost"
  position: [0.70, 0.85]
  crisis_requirement:
    type: "best_bad_choice"  # or "irreconcilable_goods" or "any"
    min_options: 2
    min_cost_per_option: 0.2
    spectrum_divergence: true  # Options must affect different spectrums

# GOAP planner validates crisis exists at required phases
# Detection: when 2+ actions are valid and each damages different spectrums ≥ threshold
```

**Rationale**:
1. GOAP naturally surfaces crises when planning—multiple valid paths with trade-offs
2. Template phases can **require** crisis structure without hard-coding which actions form it
3. Aligns with lazy phase evaluation—crisis type determined at generation time based on current world state
4. Keeps actions clean—no crisis metadata needed on individual actions

---

## Decision 2: Obligatory Scenes — Actions or Constraints?

### Question

Each genre has required scenes (Hero at Mercy of Villain, Lovers Meet, etc.). Are these:
- Required actions in template sequence?
- Validation constraints (template must satisfy all)?
- Hybrid approach?

### Analysis

Obligatory scenes research reveals:
- **~46 unique scene types** across 12 genres
- **12 core events** (one per genre) that are climactic/terminal
- **Universal 6-7 beat structure** shared by most external genres:
  1. Inciting Incident
  2. Denial/Resistance
  3. Forced Engagement
  4. Discovery (MacGuffin/Goal)
  5. Failed Strategy
  6. All Is Lost (Crisis)
  7. Core Event (Climax)
  8. Resolution

Core events represent the **mandatory climax** that defines genre satisfaction. Other obligatory scenes are **structural checkpoints** with flexibility in manifestation.

### Decision: Hybrid — Tagged Actions + Template Validation

**Two-tier design**:
1. **Core events = Required terminal actions** (must be explicitly reached)
2. **Other obligatory scenes = Validation constraints** (multiple actions can satisfy)

**Implementation**:

```yaml
# Actions tagged with obligatory scene satisfaction
actions:
  confrontation_begin:
    id: "confrontation_begin"
    category: "resolution"
    cost: 1.5

    # This action can satisfy multiple obligatory scenes depending on genre
    satisfies_obligatory:
      action: "hero_at_mercy_of_villain"
      horror: "victim_at_mercy_of_monster"
      thriller: "hero_at_mercy_of_villain"

    preconditions:
      antagonist_known: true
      protagonist_ready: true

    effects:
      confrontation_in_progress: true

    narrative_effect:
      primary_spectrum: -0.3  # Crisis point

  # Core event actions are explicit
  proof_of_love:
    id: "proof_of_love"
    category: "resolution"
    cost: 2.5
    is_core_event: true
    applicable_genres: ["love"]

    satisfies_obligatory:
      love: "proof_of_love"

    preconditions:
      lovers_separated: true
      love_tested: true

    effects:
      love_proven: true
      lovers_reunited: true

    narrative_effect:
      love_hate: +0.4
      primary_spectrum: +0.3

# Template defines requirements (not specific actions)
templates:
  action_revenge:
    genre: "action"

    obligatory_requirements:
      - scene_type: "inciting_incident"
        phase_range: [0.0, 0.15]
        required: true

      - scene_type: "speech_praising_villain"
        phase_range: [0.15, 0.50]
        required: true

      - scene_type: "all_is_lost"
        phase_range: [0.70, 0.85]
        required: true

      - scene_type: "hero_at_mercy_of_villain"
        phase_range: [0.85, 0.95]
        required: true
        is_core_event: true  # Cannot complete without this

      - scene_type: "sacrifice_rewarded"
        phase_range: [0.95, 1.0]
        required: true

# Validation: generated storyline must satisfy all requirements
# Multiple actions can satisfy same requirement
# Validator checks coverage, not specific action selection
```

**Rationale**:
1. Core events ARE terminal actions—story cannot "complete" genre without reaching them
2. Other scenes are checkpoints with manifestation flexibility
3. Propp analysis shows functions map many-to-many with actions ("Hero at Mercy" could be confrontation, capture, betrayal, etc.)
4. GOAP finds creative paths while template ensures structural requirements

---

## Decision 3: Conventions — Enforcement Level

### Question

Genres have conventions (MacGuffin, Red Herring, Labyrinth, Triangle). Are these:
- Just documentation for story composer?
- WorldState preconditions?
- Template validation constraints?

### Analysis

Conventions are **genre flavor elements** representing world-building:
- **MacGuffin**: Object driving the plot (Thriller, Crime, Action)
- **Red Herring**: Misleading information (Thriller, Crime)
- **Triangle**: Rival for affection (Love)
- **Labyrinth**: Claustrophobic danger space (Horror)
- **Clock/Deadline**: Time pressure (Thriller, Action)

Unlike obligatory scenes (narrative beats), conventions are **world elements** that exist and can be referenced by actions.

### Decision: WorldState Preconditions + Soft Validation

Conventions exist as WorldState elements. Some are hard requirements; others enhance but aren't mandatory.

**Implementation**:

```yaml
# Conventions as WorldState elements (seeded from archives or generated)
world_state:
  conventions:
    macguffin:
      exists: true
      type: "stolen_documents"
      location: "villain_headquarters"
      known_to_hero: false
      known_to_villain: true

    red_herrings:
      - id: "false_suspect"
        revealed: false
      - id: "misleading_evidence"
        revealed: false

    clock:
      active: true
      deadline: "72_hours"
      consequences: "bomb_detonates"

# Actions reference conventions as preconditions
actions:
  discover_macguffin:
    preconditions:
      conventions.macguffin.exists: true
      conventions.macguffin.known_to_hero: false

    effects:
      conventions.macguffin.known_to_hero: true

    narrative_effect:
      primary_spectrum: +0.15
      wisdom_ignorance: +0.1

  reveal_red_herring:
    preconditions:
      conventions.red_herrings.?rh.revealed: false

    effects:
      conventions.red_herrings.?rh.revealed: true

    narrative_effect:
      wisdom_ignorance: -0.1  # False understanding

# Template validation (hard vs soft requirements)
templates:
  thriller_standard:
    genre: "thriller"

    convention_requirements:
      # HARD: Must exist for genre satisfaction
      - convention: "macguffin"
        required: true
        introduced_by_phase: "complication"

      - convention: "clock"
        required: true
        introduced_by_phase: "midpoint"

      # SOFT: Enhances but optional
      - convention: "red_herring"
        required: false
        recommended_count: 2
        enhances: "mystery_depth"

# Composition can GENERATE missing conventions
# If macguffin not in archive seeds, composer creates one fitting the story
```

**Rationale**:
1. Conventions are world elements that actions can reference—not narrative beats
2. Archive extraction creates initial WorldState; conventions can be seeded or generated
3. Some conventions are HARD requirements (Thriller needs MacGuffin); others are SOFT
4. Aligns with lazy phase evaluation—conventions introduced as needed during composition

---

## Decision 4: Genre-Specific Actions?

### Question

Should some actions ONLY apply to certain genres?
- Example: "First Kiss" only valid for Love genre?
- Or are all actions universal with genre-contextual effects?

### Analysis

Love genre has 4 unique scenes not shared with other genres:
- Lovers Meet, First Kiss, Confession of Love, Lovers Reunite

But the UNDERLYING action pattern is universal:
- "First Kiss" = "First Connection/Intimacy Moment"
- A Crime story could have partners form a bond (not romantic)
- A War story has kinship bonds forged under fire

The SDK_FOUNDATIONS pattern shows actions affect the **primary spectrum** which is genre-contextual. Same action, different spectrum effect based on genre.

### Decision: Universal Actions with Genre-Contextual Labeling

Actions are mechanics; genres provide context. One action serves multiple genre contexts.

**Implementation**:

```yaml
# Universal action with genre-contextual labeling
actions:
  establish_intimate_connection:
    id: "establish_intimate_connection"
    category: "relationship"
    cost: 1.5

    # Universal definition
    universal_name: "First Connection Moment"
    description: "Two characters form a significant emotional bond"

    # Genre-specific labels (for display/obligatory matching)
    genre_labels:
      love: "First Kiss"
      crime: "Partner Trust Established"
      war: "Kinship Bond Forged"
      performance: "Mentor-Protege Connection"
      action: "Alliance Sealed"

    # Which obligatory scenes this satisfies per genre
    satisfies_obligatory:
      love: ["first_kiss_connection"]
      war: ["sacrifice_for_kinship"]  # Partial satisfaction
      # Other genres: valid action but not satisfying obligatory

    preconditions:
      characters.?a.present: true
      characters.?b.present: true
      relationship.?a.?b.sentiment: "> -0.3"  # Not hostile

    effects:
      relationship.?a.?b.bonded: true
      relationship.?a.?b.sentiment: "+0.3"

    narrative_effect:
      love_hate: +0.2
      primary_spectrum: +0.1

  # Rare genre-restricted actions (exception, not rule)
  false_ending:
    id: "false_ending"
    category: "resolution"
    cost: 1.0

    # Only Horror and Thriller REQUIRE this; others can use but don't need
    applicable_genres: ["horror", "thriller"]

    satisfies_obligatory:
      horror: "false_ending"
      thriller: "false_ending"

    preconditions:
      antagonist_apparently_defeated: true

    effects:
      antagonist_revealed_alive: true
      false_victory_exposed: true

    narrative_effect:
      primary_spectrum: -0.2  # Reversal
```

**Rationale**:
1. Reduces action library size significantly (one action, many contexts)
2. Same preconditions/effects work across genres—only labeling differs
3. Obligatory scene satisfaction is contextual
4. Aligns with GOAP's genre-contextual spectrum effects
5. Rare exceptions (False Ending) can have `applicable_genres` restrictions

---

## Decision 5: Minimum Viable Action Count

### Question

Start with:
- ~30 actions (obligatory scenes only)?
- ~50 actions (obligatory + Propp variants)?
- ~100+ actions (comprehensive library)?

### Analysis

**Counting exercise**:

| Category | Count | Source |
|----------|-------|--------|
| Core events (12 genres) | 12 | Mandatory terminal actions |
| Universal structure scenes | 7 | Inciting, Denial, Forced, Discovery, Fails, All Is Lost, Resolution |
| Genre-specific obligatory | ~25 | Unique scenes per genre (de-duplicated) |
| Propp core functions | 31 | Base narrative grammar |
| SDK category actions | ~25 | ConflictActions, RelationshipActions, etc. |

**Overlap analysis**: Many obligatory scenes map to Propp functions:
- "Inciting Attack" ≈ Propp "Villainy" (A)
- "Hero at Mercy of Villain" ≈ Propp "Struggle" (H) outcome
- "Discovery of MacGuffin" ≈ Propp "Reconnaissance" (ε) result

### Decision: ~50 Actions with Variant System

**Composition**:
- 12 core event actions (one per genre, mandatory)
- 7 universal structure actions (shared across genres)
- 15-20 genre-specific actions (unique scenes)
- 10-15 Propp-derived actions (non-overlapping)

**Variant system**: Each action has 2-4 variants for manifestation flexibility.

~50 base actions × ~3 variants = ~150 total action manifestations

**Implementation Roadmap**:

```yaml
# Phase 1: MVP (~30 actions)
phase_1:
  target: 30
  timeline: "Initial implementation"

  includes:
    core_events: 12  # One per genre (mandatory)
    universal_structure: 7
      - inciting_incident
      - denial_resistance
      - forced_engagement
      - discovery
      - strategy_fails
      - all_is_lost
      - resolution
    high_frequency_genre: 11
      - speech_praising_villain  # Action, Horror, Crime, Thriller, Western
      - false_ending  # Horror, Thriller
      - lovers_meet  # Love
      - lovers_break_up  # Love
      - lovers_reunite  # Love
      - big_battle_preparation  # War
      - training_montage  # Performance
      - revolution_planning  # Society
      - showdown_approach  # Western
      - moral_temptation  # Morality
      - worldview_challenge  # Worldview

# Phase 2: Propp Integration (~50 actions)
phase_2:
  target: 50
  timeline: "After MVP validation"

  adds:
    propp_functions: 20
      - absentation  # β - family member leaves
      - interdiction  # γ - prohibition given
      - violation  # δ - prohibition violated
      - reconnaissance  # ε - villain seeks information
      - delivery  # ζ - villain gains information
      - trickery  # η - villain deceives
      - complicity  # θ - victim deceived
      - villainy_variants  # A - 19 variants
      - mediation  # B - hero learns of lack
      - departure  # ↑ - hero leaves home
      - donor_test  # D - hero tested
      - hero_reaction  # E - hero responds
      - magical_agent  # F - hero receives help
      - guidance  # G - hero guided to goal
      - struggle  # H - hero and villain fight
      - branding  # J - hero marked
      - victory  # I - villain defeated
      - liquidation  # K - lack resolved
      - return  # ↓ - hero returns
      - pursuit  # Pr - hero pursued

# Phase 3: Comprehensive (~100 actions)
phase_3:
  target: 100
  timeline: "Post-launch expansion"

  adds:
    convention_actions: 15
      - plant_macguffin
      - reveal_macguffin
      - plant_red_herring
      - expose_red_herring
      - establish_clock
      - clock_pressure_increase
      - triangle_introduction
      - triangle_jealousy
      - labyrinth_entry
      - labyrinth_navigation
      - mentor_introduction
      - mentor_lesson
      - mentor_betrayal
      - secret_revealed
      - ritual_of_intimacy

    rare_mechanics: 15
      - deus_ex_machina  # Emergency resolution
      - unreliable_narrator_reveal
      - flashback_revelation
      - parallel_timeline_merge
      - prophecy_fulfillment
      - sacrifice_refused
      - villain_redemption
      - hero_corruption
      - bittersweet_resolution
      - pyrrhic_victory
      - hidden_identity_reveal
      - double_agent_exposure
      - dying_message
      - inheritance_revelation
      - curse_broken

    game_specific: 20
      # Custom actions for Arcadia-specific mechanics
      # Defined per game, not in base library
```

**Rationale**:
1. MVP covers all obligatory scenes across all genres
2. Propp integration adds narrative grammar depth
3. Variant system provides manifestation variety without action explosion
4. Phased approach allows validation before expansion
5. ~50 actions is tractable for manual curation and testing

---

## Action Schema Definition

Based on all decisions, the complete action schema:

```yaml
# Story Action Schema v1.0
action:
  # Identity
  id: string                          # Unique identifier (snake_case)
  category: enum                      # conflict | relationship | mystery | resolution | transformation

  # Display
  universal_name: string              # Genre-neutral name
  description: string                 # What this action does narratively
  genre_labels:                       # Genre-specific display names
    [genre]: string

  # Planning
  cost: number                        # 0.5-3.0, controls GOAP selection priority
  preconditions:                      # WorldState requirements
    [key]: value | operator
  effects:                            # WorldState changes
    [key]: value | delta
  chained_action: string?             # Next action GOAP should plan (for paired actions)

  # Narrative Impact
  narrative_effect:
    primary_spectrum: number          # Effect on genre's primary spectrum (-1.0 to +1.0)
    [spectrum_name]: number           # Optional secondary spectrum effects

  # Genre Integration
  applicable_genres: [string]?        # If set, restricts to these genres (rare)
  satisfies_obligatory:               # Which obligatory scenes this satisfies per genre
    [genre]: [scene_type]
  is_core_event: boolean              # If true, this is a genre's climactic action

  # Variants
  variants:
    - id: string
      description: string
      precondition_modifiers: {}?     # Additional/modified preconditions
      effect_modifiers: {}?           # Additional/modified effects
      probability_weight: number      # For random selection (default 1.0)
```

---

## Template Integration

Templates define requirements that actions must satisfy:

```yaml
# Template Schema (relevant to actions)
template:
  id: string
  genre: string
  emotional_arc_affinity: [arc_code]

  # Obligatory Scene Requirements
  obligatory_requirements:
    - scene_type: string              # From story-grid-genres.yaml
      phase_range: [start, end]       # 0.0-1.0 story position
      required: boolean               # Hard vs soft requirement
      is_core_event: boolean          # If true, must be reached for completion

  # Convention Requirements
  convention_requirements:
    - convention: string              # macguffin, red_herring, clock, etc.
      required: boolean               # Hard vs soft
      introduced_by_phase: string     # When it must exist
      recommended_count: number?      # For countable conventions

  # Crisis Requirements
  phases:
    - name: string
      position: [start, end]
      crisis_requirement:
        type: enum                    # best_bad_choice | irreconcilable_goods | any
        min_options: number
        min_cost_per_option: number
        spectrum_divergence: boolean
      target_state:
        [spectrum]: number            # Expected spectrum position at phase end
```

---

## Validation Rules

The storyline validator checks:

1. **Obligatory Scene Coverage**: All required scenes satisfied by at least one action
2. **Core Event Reached**: Genre's core event action appears in plan
3. **Convention Existence**: Required conventions exist in WorldState by specified phase
4. **Crisis Structure**: Required crisis phases have 2+ valid actions with divergent costs
5. **Phase Ordering**: Actions respect phase_range constraints
6. **Arc Alignment**: Final spectrum positions match selected emotional arc's end_range

---

## References

- **STORYLINE_COMPOSER.md**: System architecture and composition flow
- **SDK_FOUNDATIONS.md**: GOAP action pattern and implementation details
- **story-grid-genres.yaml**: Genre definitions with obligatory scenes and conventions
- **narrative-state.yaml**: 10 Life Value spectrums and Reagan arc integration
- **propp-functions.yaml**: 31 narrative functions with pre/post conditions
- **GAP_ANALYSIS.md**: Research findings and decision log

---

## Changelog

| Date | Version | Changes |
|------|---------|---------|
| 2026-02-04 | 1.0 | Initial decisions document |
