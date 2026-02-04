# Story Template Design Analysis

> **Status**: Design Decision Document
> **Created**: 2026-02-04
> **Purpose**: Resolve 6 open design questions from GAP_ANALYSIS.md

This document synthesizes research from STORYLINE_COMPOSER, COMPRESSION_AS_SEED_DATA, ACTOR_DATA_ACCESS_PATTERNS, SDK_FOUNDATIONS, and the YAML specification files to provide informed recommendations for the storyline template system.

---

## Executive Summary: Recommended Decisions

| Question | Recommendation | Rationale |
|----------|----------------|-----------|
| **3.1** Target State Derivation | **Arc-Shape-Derived with STC Anchors** | Reagan math + STC validation checkpoints |
| **3.2** Phase Transition Triggers | **Hybrid: Position Floor + State Ceiling** | Prevents both speed-running and deadlock |
| **3.3** Multi-Genre Handling | **Primary-Secondary Pattern** | Primary fixed per genre, secondaries layerable |
| **3.4** Obligatory Coverage | **Multiple Actions Per Phase + Static Validation** | Fail-fast at composition time |
| **3.5** Template Count & Scope | **6 Templates (One Per Reagan Arc)** | Genre-agnostic base; specialization via genre selection |
| **3.6** Phase Boundary Precision | **STC-Derived Ranges with Validation Bands** | Exact values as targets, ±0.05 tolerance |

---

## Key Research Findings

### From STORYLINE_COMPOSER (High-Level Vision)

1. **Core Philosophy**: "Emergent narrative archaeology" - discover stories that already exist in play history
2. **Template Structure**: `Template = Genre (primary spectrum) + Reagan Arc (shape)`
3. **Critical Pattern**: Lazy phase evaluation - only generate current phase + next trigger condition
4. **NarrativeState**: 10 Life Value spectrums from Story Grid, three-point scale (+1.0, 0.0, -1.0)
5. **Delegation Model**: Composer outputs intents; downstream services (quest, actor, encounter) execute

### From Compression & Actor Patterns

1. **Compressed Archives**: Rich narrative DNA suitable for story seeding (8+ traits, 10+ backstory elements, encounter history)
2. **Actor Data Access**: Variable Providers for ABML (`${personality.aggression}`, `${backstory.trauma}`)
3. **Memory System**: Keyword-based relevance scoring, bounded per-entity limits (100 default)
4. **State Flow**: Cognition pipeline (Perception → Attention → Significance → Memory → Intention)
5. **Service Hierarchy**: L4 storyline → L2 character effects (downward flow only)

### From SDK_FOUNDATIONS (Pure Computation Model)

1. **Two-Layer Architecture**: storyline-theory (data/primitives) + storyline-storyteller (composition/GOAP)
2. **Deterministic Generation**: Seed-based for reproducibility and caching
3. **Intent Output Pattern**: SDKs generate "what should happen"; services execute via existing APIs
4. **No Service Dependencies**: Pure computation libraries consumed by plugins
5. **Atomic Actions with Chaining**: Use GOAP's natural sequencing instead of complex deferred effects

### From YAML Specifications

**Phase Boundaries (from save-the-cat-beats.yaml)**:
| Beat | STC Percentage | Act |
|------|----------------|-----|
| Opening Image | 0.01 | Thesis |
| Catalyst | 0.12 | Thesis |
| Break into Two | 0.25 | Antithesis |
| Midpoint | 0.50 | Antithesis |
| All Is Lost | 0.75 | Synthesis |
| Break into Three | 0.80 | Synthesis |
| Final Image | 1.00 | Synthesis |

**Genre-Spectrum Mapping (from narrative-state.yaml)**:
| Genre | Primary Spectrum |
|-------|------------------|
| Action/Thriller/Horror | life_death |
| Crime | justice_injustice |
| Love | love_hate |
| War | honor_dishonor |
| Society | freedom_subjugation |
| Status | success_failure |
| Morality | altruism_selfishness |
| Worldview | wisdom_ignorance |

**Reagan Arc Shapes (from emotional-arcs.yaml)**:
| Arc | Shape | Nadir/Apex Position |
|-----|-------|---------------------|
| Rags to Riches | ↗ monotonic rise | N/A |
| Tragedy | ↘ monotonic fall | N/A |
| Man in Hole | ↘↗ U-shape | 40-60% (nadir) |
| Icarus | ↗↘ inverted U | 40-60% (apex) |
| Cinderella | ↗↘↗ rise-fall-rise | 30-40% and 95-100% (peaks) |
| Oedipus | ↘↗↘ fall-rise-fall | 30-40% and final (valleys) |

---

## Question 3.1: Target State Derivation

**Question**: How to calculate mid-phase spectrum values?

### Options Analysis

| Option | Pro | Con |
|--------|-----|-----|
| Linear Interpolation | Simple, predictable | Ignores Reagan's mathematical curves |
| STC-Derived | Uses existing semantics | Qualitative, not quantitative |
| Arc-Shape-Derived | Empirically rigorous | Continuous → discrete discretization |
| Hand-Tuned | Maximum control | Labor-intensive, inconsistent |

### Recommendation: **Arc-Shape-Derived with STC Anchors**

**Mechanism**:
1. Implement Reagan arc mathematical functions as callable methods
2. For each phase, evaluate `f(t)` at the phase's temporal midpoint
3. Use STC beat emotional functions as validation checkpoints

**Example Implementation**:
```csharp
public double GetTargetSpectrumValue(double progressPercent, ReaganArc arc)
{
    // Arc defines shape function f(t) where t = progress (0 to 1)
    return arc.ShapeFunction(progressPercent / 100.0);
}

// Man in Hole arc evaluation:
// progressPercent = 25% → returns 0.35 (falling toward nadir)
// progressPercent = 50% → returns 0.20 (at nadir, 40-60%)
// progressPercent = 75% → returns 0.65 (rising from nadir)
// progressPercent = 100% → returns 0.85 (resolution)
```

**STC Validation Checkpoints**:
- If phase includes `hit_rock_bottom` beat → verify target_state is in 0.1-0.3 range
- If phase includes `finale` beat → verify target_state matches arc ending range
- Bidirectional validation: Reagan provides numbers, STC validates they "feel right"

**Why This Works**:
- Reagan arcs cover 94% of published stories (empirical validation)
- STC beat semantics are industry-tested (screenplay/novel standards)
- Avoids hand-tuning while maintaining semantic correctness

---

## Question 3.2: Phase Transition Triggers

**Question**: What fires phase advancement?

### Options Analysis

| Option | Pro | Con |
|--------|-----|-----|
| Position-Based | Deterministic, matches STC | Ignores narrative state; hollow advancement |
| Action-Based | Ensures goals achieved | What if trigger action never selected? |
| State-Threshold | Organic, emergent | May never trigger; unpredictable timing |
| Hybrid | Guarantees progress + coherence | Most complex |

### Recommendation: **Hybrid with Position Floor + State Ceiling**

**Mechanism**:
```yaml
incitement_phase:
  position_floor: 0.10      # Can't advance before 10% elapsed
  position_ceiling: 0.20    # Must advance by 20% (with warning if forced)
  state_ceiling:
    primary_spectrum_max: 0.5  # Must have dropped below 0.5
    obligatory_scenes_completed: [inciting_attack]
```

**Advancement Logic**:
```
IF position >= floor AND state meets ceiling:
    ADVANCE to next phase
ELIF position >= ceiling:
    LOG WARNING "Forced advancement - state conditions unmet"
    ADVANCE to next phase (prevent deadlock)
```

**Why This Works**:
- **Position floor prevents speed-running**: GOAP can't skip ahead in 5% of story
- **State ceiling ensures narrative coherence**: Must achieve phase goals
- **Position ceiling prevents deadlock**: Always makes forward progress
- **Obligatory scene tracking**: Can't advance until required scenes satisfied

**Integration with Lazy Evaluation**:
- Phase advancement triggers continuation point evaluation
- Next phase generated with CURRENT world state (not stale snapshot)
- Supports STORYLINE_COMPOSER's core pattern of "only generate current phase"

---

## Question 3.3: Multi-Genre Handling

**Question**: How do templates handle layered genres (e.g., Die Hard = Action + Love)?

### Options Analysis

| Option | Pro | Con |
|--------|-----|-----|
| Single-Genre Templates | Simple, modular | How to compose without conflicts? |
| Multi-Spectrum Targets | Explicit control | Requires pre-defining all combinations |
| Genre-Agnostic Templates | Maximum reusability | Loses genre-specific nuances |

### Recommendation: **Primary-Secondary Pattern**

**Design**:
1. Templates are **primary-genre-specific** (target one primary spectrum)
2. Templates define **optional secondary spectrum targets** (may be empty)
3. At composition time, **authors can add additional secondary layers**

**Schema Example**:
```yaml
man_in_hole_template:
  # Primary spectrum determined by genre selection at composition time
  phases:
    descent:
      primary_target: 0.3      # Required - primary spectrum falls
      secondary_targets: {}    # Empty by default

    nadir:
      primary_target: 0.15     # Required - lowest point
      secondary_targets: {}

    ascent:
      primary_target: 0.75     # Required - primary spectrum rises
      secondary_targets: {}
```

**Composition Time Layering**:
```csharp
var composition = new StoryComposition
{
    Template = Templates.ManInHole,
    PrimaryGenre = ContentGenre.Action,      // → life_death spectrum
    SecondaryGenres = [ContentGenre.Love],   // → love_hate as secondary
    SecondaryTargets = new Dictionary<SpectrumType, PhaseTargets>
    {
        [SpectrumType.LoveHate] = new PhaseTargets
        {
            Descent = 0.6,   // Romance starts positive
            Nadir = 0.4,     // Relationship tested
            Ascent = 0.85    // Love confirmed through shared ordeal
        }
    }
};
```

**Validation Rule**: If multiple genres active, check that obligatory scenes for ALL genres can be satisfied by available actions. Reject incompatible combinations at composition time (fail-fast).

**Why This Works**:
- Respects Story Grid: Primary spectrum IS fixed per genre
- Allows layering: Secondary targets are author-controlled
- Fewer templates needed: 6 arc-based templates work for all 12 genres
- GOAP compatibility: Actions that affect primary are required; secondary effects optional

---

## Question 3.4: Obligatory Coverage

**Question**: What if obligatory scenes exceed phase count (e.g., 7 scenes, 5 phases)?

### Options Analysis

| Option | Pro | Con |
|--------|-----|-----|
| Require 100% | Guarantees completeness | Limits template flexibility |
| Allow Partial | Flexible, more templates viable | Risks incomplete stories |
| Multiple Per Phase | Most flexible | Requires validation that all reachable |

### Recommendation: **Multiple Actions Per Phase + Static Validation**

**Mechanism**:
1. Templates specify required obligatory scenes **per template** (not per phase)
2. Each phase has a `scene_capacity` guideline (soft limit)
3. Static validation at composition time ensures coverage

**Schema Example**:
```yaml
thriller_arc:
  required_obligatory_scenes:
    - inciting_crime
    - speech_in_praise_of_villain
    - hero_at_mercy_of_villain
    - false_ending
    - hero_becomes_victim
    - clock_plot
    - make_it_personal    # 7 scenes total

  phases:                  # 5 phases
    setup:
      target_state: { life_death: 0.6 }
      scene_capacity: 2    # Guideline: expect ~2 obligatory scenes here

    complication:
      target_state: { life_death: 0.4 }
      scene_capacity: 2

    crisis:
      target_state: { life_death: 0.2 }
      scene_capacity: 2

    climax:
      target_state: { life_death: 0.15 }
      scene_capacity: 1    # Core event phase

    resolution:
      target_state: { life_death: 0.7 }
      scene_capacity: 0    # No new obligatory scenes
```

**Validation Rules**:
1. **Coverage check**: Template covers all genre obligatory scenes
2. **Reachability check**: For each scene, exists action with `satisfies_obligatory: scene_name`
3. **Ordering check**: If scene A must precede B, phases allow this ordering

**Why This Works**:
- Respects Story Grid: All obligatory scenes included (genre coherence)
- Flexible design: Phases don't rigidly bind to scene count
- Fail-fast: Catches gaps at composition time, not runtime
- GOAP-friendly: Planner selects which actions; template ensures all available

---

## Question 3.5: Template Count & Scope

**Question**: How many templates, and what selection criteria?

### Options Considered

- 5 named templates (Revenge, Mystery, Redemption, Legacy, Tragic) - too genre-specific
- 72 templates (12 genres × 6 arcs) - premature explosion
- 3 templates (music SDK precedent) - insufficient for literary complexity

### Recommendation: **6 Templates (One Per Reagan Arc)**

**Initial Template Set**:
```yaml
templates:
  rags_to_riches:    # ↗ Rise (0.3 → 0.9)
    phases: [setup, rising_action, climax, resolution]
    arc_direction: positive

  tragedy:           # ↘ Fall (0.7 → 0.2)
    phases: [setup, complications, decline, catastrophe]
    arc_direction: negative

  man_in_hole:       # ↘↗ Fall-Rise (0.5 → 0.2 → 0.8)
    phases: [setup, descent, nadir, ascent, resolution]
    arc_direction: positive

  icarus:            # ↗↘ Rise-Fall (0.3 → 0.8 → 0.2)
    phases: [setup, ascent, apex, fall, catastrophe]
    arc_direction: negative

  cinderella:        # ↗↘↗ Rise-Fall-Rise (0.3 → 0.7 → 0.4 → 0.9)
    phases: [setup, first_rise, setback, recovery, triumph]
    arc_direction: positive

  oedipus:           # ↘↗↘ Fall-Rise-Fall (0.6 → 0.3 → 0.6 → 0.2)
    phases: [setup, first_fall, false_hope, final_fall, tragedy]
    arc_direction: negative
```

**Selection Workflow**:
```
Author selects: Genre (Action) + Subgenre (Epic) + Arc Template (man_in_hole)
System validates: compatible_arcs check (Epic allows man_in_hole)
System applies: Arc shape + Genre obligatory scenes + Subgenre conventions
```

**Why 6 Templates**:
1. Reagan's arcs cover 94% of published stories (empirical validation)
2. Each arc has distinct mathematical shape (not variants of each other)
3. Cross-genre applicability (man_in_hole works for Action, Crime, Love, etc.)
4. Balanced set: 3 positive endings, 3 negative endings
5. Avoids conflating "arc shape" with "genre flavor" (Revenge = Action + man_in_hole)

**Expansion Criteria** (if 6 proves insufficient):
- Add template if: (a) distinct shape not covered, AND (b) frequent in corpus
- Candidates: "episodic" (flat), "cyclical" (repeated rise/fall)
- But start with 6 and expand only with empirical evidence

---

## Question 3.6: Phase Boundary Precision

**Question**: Use exact STC percentages or round to simpler intervals?

### Options Analysis

| Option | Pro | Con |
|--------|-----|-----|
| Exact STC Values | Faithful to research | Is 0.12 vs 0.15 meaningful? |
| Round to Quartiles | Easy to reason about | Loses STC calibration |
| Template-Specific | Maximum flexibility | No standardization |

### Recommendation: **STC-Derived Ranges with Validation Bands**

**Mechanism**:
1. Templates use **STC exact percentages as phase centers**
2. Each phase has a **validation band** (±0.05 tolerance)
3. GOAP tracks **estimated story completion** (0.0-1.0 normalized)

**Schema Example**:
```yaml
phases:
  catalyst:
    stc_center: 0.11          # Midpoint of STC range (0.10-0.12)
    validation_band: 0.05     # Accept 0.06-0.16 as "close enough"
    position_floor: 0.08      # Earliest possible (hybrid trigger)
    position_ceiling: 0.18    # Latest allowed (prevent stalling)

  break_into_two:
    stc_center: 0.25
    validation_band: 0.05     # Accept 0.20-0.30
    position_floor: 0.20
    position_ceiling: 0.32

  midpoint:
    stc_center: 0.50
    validation_band: 0.05     # Accept 0.45-0.55
    position_floor: 0.45
    position_ceiling: 0.58

  all_is_lost:
    stc_center: 0.75
    validation_band: 0.05     # Accept 0.70-0.80
    position_floor: 0.70
    position_ceiling: 0.82
```

**Story Position Estimation**:
```csharp
// Position estimated via action sequence, not wall-clock time
double EstimatePosition(int actionsCompleted, int totalExpectedActions)
{
    return (double)actionsCompleted / totalExpectedActions;
}

// Example: 50 actions planned, action 6 completed
// Position ≈ 6/50 = 0.12 (near Catalyst)
```

**Why This Works**:
- **Preserves STC precision**: Uses exact values as targets
- **Allows flexibility**: Validation bands accommodate GOAP's discrete nature
- **Fail-safe bounds**: Position ceiling prevents getting stuck
- **Logging/telemetry**: Can warn if story deviates significantly from STC targets

**Why NOT Quartiles**:
- STC shows Act 1 ≠ exactly 25% (it's closer to 20-22%)
- Midpoint (0.50) and All Is Lost (0.75) are distinct beats within Act 2/3
- Quartile rounding would collapse these critical distinctions

---

## Implementation Priority

Based on dependency analysis, implement in this order:

```
1. Question 3.6 (Phase Boundaries)
   ↓ Defines WHEN phases occur - foundational for everything else

2. Question 3.1 (Target State Derivation)
   ↓ Defines WHAT states phases target - needs boundaries first

3. Question 3.2 (Phase Triggers)
   ↓ Defines HOW transitions happen - needs targets to evaluate

4. Question 3.4 (Obligatory Coverage)
   ↓ Validates ALL requirements met - needs triggers to enforce

5. Question 3.3 (Multi-Genre)
   ↓ Extends to layered genres - needs base system working

6. Question 3.5 (Template Count)
   ↓ Determines HOW MANY templates - validates design with concrete examples
```

---

## Key Trade-offs Accepted

### Mathematical Rigor vs. Pragmatic Simplicity
- **Accepted**: Use Reagan math for trajectory, discretize to phase targets
- **Mitigation**: STC validation checkpoints catch semantic errors

### Template Flexibility vs. Genre Coherence
- **Accepted**: Genre-agnostic arc templates (6) + genre-specific obligatory scenes
- **Mitigation**: Static validation ensures all obligatory scenes covered

### Deterministic Pacing vs. Emergent Narrative
- **Accepted**: Hybrid triggers (position floor + state ceiling)
- **Mitigation**: Position ceiling prevents deadlock; state ceiling ensures coherence

### Completeness Validation vs. Generative Freedom
- **Accepted**: Fail-fast validation at composition time
- **Mitigation**: Multiple actions per phase allows flexible scene distribution

---

## Next Steps

1. **Implement Reagan arc shape functions** in storyline-theory SDK
2. **Define 6 base templates** with phase definitions matching this analysis
3. **Create validation rules** for obligatory scene coverage
4. **Build hybrid trigger system** with position floors/ceilings
5. **Test with Die Hard, Silence of the Lambs** as multi-genre validation cases

---

## Research Sources

- `/docs/planning/STORYLINE_COMPOSER.md` - High-level vision and lazy evaluation pattern
- `/docs/planning/COMPRESSION_AS_SEED_DATA.md` - Archive-driven narrative seeding
- `/docs/planning/ACTOR_DATA_ACCESS_PATTERNS.md` - Variable Providers and state flow
- `/schemas/storyline/SDK_FOUNDATIONS.md` - Two-layer SDK architecture
- `/schemas/storyline/GAP_ANALYSIS.md` - Original design questions
- `/schemas/storyline/*.yaml` - Formal specifications (STC, Reagan, Story Grid, Propp)
