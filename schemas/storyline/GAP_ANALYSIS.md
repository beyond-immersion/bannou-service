# Storyline SDK Gap Analysis

> **Status**: Analysis Complete
> **Date**: 2026-02-03
> **Purpose**: Identify missing components between research YAML files and complete SDK implementation

## Executive Summary

Four research frameworks have been distilled into YAML:
1. **Save the Cat Beats** (Blake Snyder) - Timing and pacing
2. **Emotional Arcs** (Reagan et al.) - Shape and sentiment trajectory
3. **Story Grid Genres** (Shawn Coyne) - Structure and obligatory elements
4. **Propp Functions** (Vladimir Propp) - Event sequencing and causality

These are **vertical pillars** - each internally consistent but operating independently. The gaps fall into two categories:

- **Integration Gaps**: Work we must do to connect these frameworks
- **Research Gaps**: Potentially existing frameworks we should find before inventing

---

## The Four Pillars: What Exists

### Pillar 1: Save the Cat Beats (`save-the-cat-beats.yaml`)

| Strength | Limitation |
|----------|------------|
| Precise percentage-based timing (bs2, fiction strategies) | No causal relationships between beats |
| 16 beats with emotional functions | Genre categories are narrative types, not structural |
| Multiple media strategies | Doesn't specify HOW to move between beats |

**Key Data Points**:
- Breakpoints: 16 position indices with percentages
- Beat types: single, sequence, act
- Emotional functions: establish_baseline, disrupt_equilibrium, hit_rock_bottom, etc.

### Pillar 2: Emotional Arcs (`emotional-arcs.yaml`)

| Strength | Limitation |
|----------|------------|
| Mathematically rigorous (SVD-derived) | Mode vectors are "preliminary" (line 376) |
| 6 shapes cover 94% of stories | Classification threshold (0.1) needs empirical tuning |
| Quantitative NarrativeState mapping formulas | Only 2 of 6 arcs have beat mappings |

**Key Data Points**:
- 6 canonical arcs with mathematical forms
- SVD modes 1-3 with variance explained
- Sentiment → NarrativeState dimension formulas (maps to primary Life Value spectrum based on genre)

### Pillar 3: Story Grid Genres (`story-grid-genres.yaml`)

| Strength | Limitation |
|----------|------------|
| Five Commandments provide scene-level structure | No timing/pacing guidance |
| Value poles give measurable change dimensions | Genre conventions are qualitative, not computational |
| Obligatory scenes are concrete requirements | Five-Leaf Clover is classification, not generation |

**Key Data Points**:
- 6-level story unit hierarchy (beat → global)
- 12+ value spectrums with genre associations
- 12 content genres (9 external, 3 internal) with conventions + obligatory scenes
- Four Core Framework: Need, Value, Emotion, Event

### Pillar 4: Propp Functions (`propp-functions.yaml`)

| Strength | Limitation |
|----------|------------|
| Preconditions/postconditions enable FSM | Folktale-specific; may not generalize |
| Deterministic with seed | Only 2 paths in Act 2 (struggle vs task) |
| Paired functions create causal chains | No emotional/sentiment dimension |

**Key Data Points**:
- 31 functions with symbols, variants, pre/post conditions
- 7 dramatis personae with spheres of action
- Three-act structure with deterministic Acts 1 & 3, probabilistic Act 2
- Generation algorithm with seeded randomness

---

## Gap Inventory

### Gap 1: NarrativeState Canonical Definition

**Type**: Integration using Story Grid's Life Value spectrums (see Research Document Integration)

**Current State**:
- `emotional-arcs.yaml` provides sentiment trajectory shapes (Reagan's 6 arcs)
- STORYLINE_COMPOSER.md proposes 6 dimensions but **without citation** (derived from music SDK by analogy - not valid for literary narrative)
- `FOUR-CORE-FRAMEWORK.md` provides Story Grid's 10 Life Value spectrums (properly cited, published methodology)
- No authoritative YAML schema exists

**Decision**: Use Story Grid's 10 Life Value spectrums from `FOUR-CORE-FRAMEWORK.md`.

**What's Missing**:
```yaml
# Based on Story Grid Four Core Framework
narrative_state:
  # Each dimension is a value spectrum (0 = negative pole, 1 = positive pole)
  dimensions:
    life_death:
      negative: death
      positive: life
      negation_of_negation: damnation  # Fate worse than death
      genres: [action, thriller, horror]

    honor_dishonor:
      negative: dishonor
      positive: honor_with_victory
      negation_of_negation: victory_with_dishonor_masquerading_as_honor
      genres: [war]

    justice_injustice:
      negative: injustice
      positive: justice
      negation_of_negation: tyranny
      genres: [crime]

    freedom_subjugation:
      negative: subjugation
      positive: freedom
      negation_of_negation: slavery_with_illusion_of_freedom
      genres: [western, society]

    love_hate:
      negative: hate
      positive: love
      negation_of_negation: hate_masquerading_as_love
      genres: [love]

    respect_shame:
      negative: shame
      positive: respect
      negation_of_negation: hollow_praise
      genres: [performance, status]

    power_impotence:
      negative: impotence
      positive: power
      negation_of_negation: power_through_cooption
      genres: [society]

    success_failure:
      negative: failure
      positive: success
      negation_of_negation: selling_out
      genres: [status]

    altruism_selfishness:
      negative: selfishness
      positive: altruism
      negation_of_negation: selfishness_disguised_as_altruism
      genres: [morality]

    wisdom_ignorance:
      negative: ignorance
      positive: wisdom
      negation_of_negation: willful_denial
      genres: [worldview]

  # Reagan arc shapes apply to PRIMARY value spectrum for the genre
  arc_integration:
    # Genre determines which spectrum is primary
    # Arc shape determines how that spectrum changes over time
    # Example: Action + man_in_hole = LifeDeath falls then rises
```

**Dependencies**: None - this is foundational

---

### Gap 2: Cross-Framework Mapping

**Type**: Integration using NCP schema infrastructure (see Research Sprint Results)

**Current State**:
- Each framework uses different units: percentages (STC), sentiment values (Arcs), events (Propp), scenes (Story Grid)
- `emotional-arcs.yaml` has 2 partial beat mappings (man_in_hole, cinderella)
- **NCP provides `custom_appreciation_namespace` for exactly this purpose**
- **NCP's 144 narrative functions can serve as the hub vocabulary**

**What's Missing** (mapping content, not schema):

| From | To NCP Function | Status |
|------|-----------------|--------|
| Propp 31 Functions → NCP Functions | **TODO** | Map by semantic equivalence |
| STC 16 Beats → NCP Functions | **TODO** | Map by narrative purpose |
| Story Grid Scenes → NCP Functions | **TODO** | Map by structural role |

**Note on Reagan Arcs**: Reagan arcs are **continuous mathematical functions** f(t) that output a value 0-1 at any normalized time t. They do NOT have discrete phases that map to NCP functions. Instead:
- Reagan arcs provide the **shape** of how the primary Life Value spectrum changes over time
- At any t, f(t) gives the current position on that spectrum
- This is orthogonal to NCP function mapping - NCP functions are discrete events, Reagan arcs are continuous curves

**Resolved by NCP**: Schema structure for bidirectional framework lookup.

**Still Required**: Populate the mappings with our framework-specific data (Propp, STC, Story Grid only - Reagan is value curve, not event sequence).

---

### Gap 3: Narrative Templates (Arc Phase Definitions)

**Type**: Integration (we define it, using the frameworks)

**Current State**:
- STORYLINE_COMPOSER.md mentions: RevengeArc, MysteryArc, RedemptionArc, LegacyArc, TragicArc
- No formal phase definitions exist
- music-storyteller has NarrativeTemplate as precedent

**What's Missing**:
```yaml
templates:
  revenge_arc:
    emotional_arc_affinity: [man_in_hole, cinderella]  # Which arcs fit
    story_grid_genre: action  # Primary content genre

    phases:
      incitement:
        position: [0.0, 0.12]  # Percentage range
        target_state: { life_death: 0.4 }  # Primary spectrum for action genre
        required_beats: [OPENING_IMAGE, CATALYST]
        propp_functions: [VILLAINY]
        obligatory_scenes: [inciting_attack]

      discovery:
        position: [0.12, 0.25]
        target_state: { life_death: 0.3 }  # Threat increasing
        # ...
```

**Dependencies**: Requires Gap 1 (NarrativeState) and Gap 2 (Cross-Framework Mapping)

---

### Gap 4: Story Action Library

**Type**: Integration (we define it)

**Current State**:
- lib-behavior has GOAP infrastructure (A* planner, actions with preconditions/effects)
- music-storyteller has action categories (TensionActions, ColorActions, etc.)
- No storyline action library exists

**What's Missing**:
```yaml
actions:
  conflict:
    introduce_antagonist:
      preconditions:
        - key: antagonist.known
          value: false
        - key: tension.current
          operator: "<"
          value: 0.7
      effects:
        - key: antagonist.known
          value: true
        - key: tension.current
          operator: "+="
          value: 0.3
      cost: 2.0
      propp_equivalent: VILLAINY  # Links to Propp
      stc_beat_affinity: CATALYST  # Likely occurs near this beat
      narrative_state_delta:
        life_death: -0.3  # Threat to survival introduced
```

**Dependencies**: Requires Gap 1 (NarrativeState), Gap 2 (mapping to understand affinities)

---

### Gap 5: Archive-to-WorldState Extraction Rules

**Type**: Integration (we define it)

**Current State**:
- lib-resource defines archive structure
- character-history, character-personality, character-encounter define data shapes
- No mapping from archive fields to GOAP WorldState exists

**What's Missing**:
```yaml
extraction_rules:
  character_personality:
    field_mappings:
      confrontational:
        world_state_key: "protagonist.aggression_preference"
        transform: "direct"  # value maps directly
        scoring:
          weight: 0.3
          templates_affected: [revenge_arc]  # High confrontational → revenge affinity

  character_encounter:
    story_potential_scoring:
      negative_sentiment_threshold: -0.5
      conflict_indicator:
        condition: "sentiment < -0.5 AND encounter_type == 'conflict'"
        world_state_effect: "antagonist_relationship.exists = true"
        narrative_weight: 0.8
```

**Dependencies**: Stable archive schema (exists), Gap 1 (NarrativeState for scoring)

---

### Gap 6: Framework Compatibility Matrix

**Type**: Integration (we derive empirically or define heuristically)

**Current State**:
- `emotional-arcs.yaml` has `generation_guidance.recommendations_by_genre` (lines 439-445)
- `save-the-cat-beats.yaml` genres are different from Story Grid genres
- No unified compatibility assessment

**What's Missing**:
```yaml
compatibility_matrix:
  # Story Grid Content Genre → Recommended Emotional Arcs
  action:
    primary_arcs: [man_in_hole, cinderella]
    secondary_arcs: [rags_to_riches]
    avoid_arcs: [tragedy, oedipus]  # Action audiences expect triumph
    stc_genre_mapping: [monster_in_the_house, dude_with_a_problem, golden_fleece]
    propp_path_preference: struggle  # vs task

  horror:
    primary_arcs: [icarus, oedipus, tragedy]
    avoid_arcs: [rags_to_riches]
    stc_genre_mapping: [monster_in_the_house]
    propp_path_preference: struggle
```

**Dependencies**: Gap 2 (Cross-Framework Mapping)

---

## Gap Discussion: Research vs Integration

### Candidates for Research (Potential Fifth Pillar)

**Gap 2 (Cross-Framework Mapping)** is the key question. Before we invent mappings, we should ask:

> Has anyone already analyzed how these narrative frameworks align?

**Potential Research Domains**:

1. **Computational Narratology**
   - Academic field explicitly focused on formalizing narrative structure
   - Key researchers: Mark Riedl, Pablo Gervás (already cited in propp-functions.yaml), James Ryan
   - Potential papers on "story grammar" or "narrative generation grammars"

2. **Screenwriting Software Research**
   - Tools like Final Draft, Save the Cat! software, Story Grid Editor
   - May have implicit mappings in their implementations
   - Industry knowledge not necessarily published academically

3. **AI Story Generation Papers**
   - GPT-era narrative generation research
   - May have unified representations of story structure
   - Check: ACL, EMNLP, AAAI proceedings

4. **Cognitive Science of Narrative**
   - How humans process stories might reveal universal structural principles
   - Jerome Bruner, Roger Schank (story schemas)
   - Could provide theoretical grounding for mappings

**Specific Search Queries**:
- "Save the Cat Propp functions mapping"
- "story grid emotional arc alignment"
- "computational narrative structure unified model"
- "story grammar generation"
- "narrative beat function correspondence"

**Key Paper to Find (if it exists)**:
A paper that treats multiple narrative frameworks as *projections* of an underlying story space, similar to how Reagan et al. showed emotional arcs are SVD projections of sentiment timeseries.

### Likely Integration Work (No External Research)

These gaps are almost certainly our own synthesis work:

| Gap | Why Integration |
|-----|-----------------|
| Gap 1: NarrativeState | We're defining our SDK's state space |
| Gap 3: Narrative Templates | Application-specific (revenge, legacy, etc.) |
| Gap 4: Story Actions | GOAP actions for our system |
| Gap 5: Archive Extraction | Our archive format → our WorldState |
| Gap 6: Compatibility Matrix | Can be derived from Gap 2 once it exists |

---

## Dependency Graph

```
                    ┌─────────────────────┐
                    │  Gap 2: Cross-      │
                    │  Framework Mapping  │◄──── USE NCP AS HUB SCHEMA
                    │  (populate NCP      │      (~/repos/narrative-context-protocol)
                    │   namespaces)       │
                    └─────────┬───────────┘
                              │
              ┌───────────────┼───────────────┐
              │               │               │
              ▼               ▼               ▼
    ┌─────────────────┐ ┌───────────┐ ┌─────────────────┐
    │ Gap 6:          │ │ Gap 3:    │ │ Gap 4:          │
    │ Compatibility   │ │ Narrative │ │ Story Actions   │
    │ Matrix          │ │ Templates │ │ (NCP functions  │
    │ (derivable from │ │           │ │  → GOAP actions)│
    │  NCP mappings)  │ │           │ │                 │
    └─────────────────┘ └─────┬─────┘ └────────┬────────┘
                              │                │
                              │                │
                              ▼                ▼
                    ┌─────────────────────────────────┐
                    │     Gap 1: NarrativeState       │◄──── FOUNDATIONAL
                    │     (align with NCP storypoint  │      (can use NCP structure)
                    │      structure)                 │
                    └─────────────────┬───────────────┘
                                      │
                                      ▼
                    ┌─────────────────────────────────┐
                    │     Gap 5: Archive Extraction   │
                    │     (depends on NarrativeState  │
                    │      for scoring)               │
                    └─────────────────────────────────┘
```

---

## Recommended Resolution Order

### Phase 0: Research Sprint (Gap 2)

Before building, spend bounded time (1-2 days) searching for existing cross-framework mappings.

**If found**: We have a fifth pillar to integrate
**If not found**: We create the mapping ourselves, documenting our reasoning

### Phase 1: Foundation (Gap 1)

Define NarrativeState as canonical SDK type. This unblocks everything else.

### Phase 2: Mapping (Gap 2, if research found nothing)

Create cross-framework mapping. This is the hardest intellectual work.

Approach options:
1. **Temporal alignment**: Map everything to percentage-of-story timeline
2. **Functional equivalence**: Match by narrative purpose/effect
3. **Empirical analysis**: Analyze existing stories with all four lenses

### Phase 3: Actions (Gap 4)

Define GOAP action library. Each action references mappings from Phase 2.

### Phase 4: Templates (Gap 3)

Build narrative templates using:
- NarrativeState targets from Phase 1
- Cross-framework anchors from Phase 2
- Available actions from Phase 3

### Phase 5: Extraction (Gap 5)

Map archives to WorldState. Can happen in parallel with Phase 3-4.

### Phase 6: Compatibility (Gap 6)

Derive compatibility matrix from accumulated knowledge. Largely empirical/heuristic.

---

## Open Questions

1. ~~**Should NarrativeState have 6 dimensions or fewer/more?**~~
   - **ANSWERED**: 10 dimensions based on Story Grid's Life Value spectrums
   - Each dimension maps to a Maslow need level (properly grounded in human psychology)
   - Dimensions are not orthogonal - they map to specific content genres
   - Stories layer genres, each bringing its spectrum (1 primary, N secondary)

2. **Are the frameworks genuinely compatible?**
   - STC is prescriptive (Hollywood structure)
   - Propp is descriptive (folktale patterns)
   - Story Grid is analytical (editor's lens)
   - Emotional Arcs are statistical (corpus-derived)
   - They may describe different phenomena

3. **What's the right granularity for templates?**
   - Too coarse: "revenge_arc" is too vague
   - Too fine: Loses generativity
   - music-storyteller uses 3 templates; is 5-7 right for storylines?

4. **How do we validate mappings?**
   - Analyze existing stories?
   - Expert review?
   - Generated story quality testing?

---

## Resolved Modeling Issues (2026-02-04)

During schema validation, several inconsistencies were identified between the research documents and the `narrative-state.yaml` implementation. These have been analyzed and resolved.

### Primary Source Authority

Both `STORY-GRID-101.md` and `FOUR-CORE-FRAMEWORK.md` are interpretations of **Shawn Coyne's Story Grid methodology**. When they conflict, we defer to:

1. **Primary Source Extraction**: `docs/research/STORY-GRID-PRIMARY-SOURCE.md` (extracted 2026-02-04)
2. **Original Book**: "The Story Grid: What Good Editors Know" by Shawn Coyne (2015)
3. **Authoritative Website**: [storygrid.com](https://storygrid.com) - free, continuously updated

**Reference Hierarchy**: `STORY-GRID-PRIMARY-SOURCE.md` > `STORY-GRID-101.md` > `FOUR-CORE-FRAMEWORK.md`

### Issue 1: Core Emotions by Genre

**Problem**: The current model assigns `core_emotion` per spectrum, not per genre. But genres sharing a spectrum have different emotions.

**Primary Source Data** (from STORY-GRID-PRIMARY-SOURCE.md):

| Genre | Core Emotion (PRIMARY SOURCE) |
|-------|-------------------------------|
| Action | Excitement |
| Horror | **Fear** |
| Thriller | **Anxiety/Dread** |

**Resolution**: Add `genre_overrides` section to `narrative-state.yaml`:

```yaml
genre_overrides:
  horror:
    core_emotion: "Fear"           # Spectrum default: "Excitement"
  thriller:
    core_emotion: "Anxiety/Dread"  # Spectrum default: "Excitement"
```

**Note**: FOUR-CORE-FRAMEWORK.md incorrectly stated Thriller = "Excitement". Primary source clearly states "anxiety/dread".

### Issue 2: Core Need - All Life/Death Genres Use "Safety"

**Problem**: FOUR-CORE-FRAMEWORK.md indicated:
- Action → Core Need: "Survival"
- Thriller/Horror → Core Need: "Safety"

**Primary Source Correction** (STORY-GRID-PRIMARY-SOURCE.md line 217):

| Genre | Core Need (PRIMARY SOURCE) |
|-------|---------------------------|
| Action | **Safety** |
| Horror | **Safety** |
| Thriller | **Safety** |

**All three genres have Core Need = Safety.** FOUR-CORE-FRAMEWORK.md was WRONG about Action having "Survival".

**Resolution**: Change the `life_death` spectrum default to `core_need: "Safety"`. No genre overrides needed for core_need.

```yaml
life_death:
  core_need: "Safety"  # CORRECTED from "Survival" per primary source
  core_emotion: "Excitement"  # Default, overridden per genre
```

**Rationale**: The primary source is authoritative. All three genres sharing this spectrum address the same fundamental human need (Safety/security from harm), but evoke different emotions.

### Issue 3: External vs Internal Genre Classification Conflict

**Problem**: Research documents disagree on classification of Love, Performance, and Society:

| Genre | STORY-GRID-101.md | FOUR-CORE-FRAMEWORK.md |
|-------|-------------------|------------------------|
| Love | External | Internal |
| Performance | External | Internal |
| Society | External | Internal |
| **Total** | 9 external + 3 internal | 6 external + 6 internal |

**Resolution**: Trust STORY-GRID-101.md (9 external + 3 internal).

STORY-GRID-101.md directly quotes Coyne:
> "Nine are external and based on your protagonist's wants. Those external genres are Action, War, Horror, Crime, Thriller, Western, Love, Performance, and Society."

FOUR-CORE-FRAMEWORK.md's 6/6 split is an interpretive synthesis, not a direct quote.

**Canonical Classification**:
- **External** (9): Action, War, Horror, Crime, Thriller, Western, Love, Performance, Society
- **Internal** (3): Status, Morality, Worldview

**Note**: Document the discrepancy in YAML as a comment for future reference.

### Issue 4: One-Spectrum-One-Emotion Model Limitation

**Problem**: The architecture assigns one `core_emotion` per spectrum, but multiple genres sharing a spectrum may have different emotions (Action=Excitement, Horror=Fear on same `life_death` spectrum).

**Resolution**: The `genre_overrides` mechanism (Issues 1-2) solves this without abandoning the spectrum-centric model.

**Alternatives Considered**:
- **Split spectrums**: Create `survival_physical` (Action), `survival_existential` (Thriller), `survival_horror` (Horror). Rejected: spectrum explosion, breaks Maslow correspondence.
- **Move emotions to genre level entirely**: Remove `core_emotion` from spectrums. Rejected: larger refactor, loses intuitive defaults.

**Decision**: Genre-level overrides are the minimum viable change. Spectrum defaults handle the common case; overrides handle research-documented exceptions.

### Schema Change Required

Update `narrative-state.yaml` with these corrections:

**1. Fix `life_death` spectrum default:**
```yaml
life_death:
  negative: death
  positive: life
  negation_of_negation: damnation
  core_need: "Safety"        # CORRECTED from "Survival" per primary source
  core_emotion: "Excitement" # Default (Action uses this)
  genres: [action, thriller, horror]
```

**2. Add genre_overrides section (emotions only - need is now correct at spectrum level):**
```yaml
# Genre-specific overrides for spectrum defaults
# Use when research indicates a genre differs from its primary spectrum's default
genre_overrides:
  horror:
    core_emotion: "Fear"           # Override spectrum default "Excitement"
  thriller:
    core_emotion: "Anxiety/Dread"  # Override spectrum default "Excitement"
  # Action uses spectrum default (Excitement) - no override needed
```

**Note**: No `core_need` overrides are required. All three genres (Action, Horror, Thriller) sharing the `life_death` spectrum have Core Need = "Safety" per the primary source.

### New Discoveries from Primary Source (2026-02-04)

The following findings from `docs/research/STORY-GRID-PRIMARY-SOURCE.md` require schema updates or documentation:

#### 1. Four-Stage Value Spectrum (MAJOR FINDING)

**Current Model**: 3 stages (Negative, Positive, Negation of Negation)

**Primary Source Model** (lines 236-257): 4 stages:
1. **Positive** - The ideal state
2. **Contrary** - A less-than-ideal state (but not terrible)
3. **Negative** - The bad state
4. **Negation of the Negation** - Fate worse than the negative

**Example from Primary Source**:
| Genre | Positive | Contrary | Negative | Negation of Negation |
|-------|----------|----------|----------|----------------------|
| Action/Thriller | Life | Unconsciousness | Death | Damnation |
| Crime | Justice | Unfairness | Injustice | Tyranny |
| Love | Love | Desire | Indifference | Hate/Self-hatred |
| Morality | Good | Wavering | Indifference | Evil |

**Action Required**: ~~Add `contrary` field to all spectrum definitions in `narrative-state.yaml`.~~ **DONE** (2026-02-04)

All 10 spectrums now have `contrary` field with appropriate values:
- life_death: Unconsciousness
- honor_dishonor: Honor without Victory
- justice_injustice: Unfairness
- freedom_subjugation: Restriction
- love_hate: Desire (with Indifference as separate negative stage)
- respect_shame: Obscurity
- power_impotence: Limited Influence
- success_failure: Compromise
- altruism_selfishness: Wavering/Temptation (with Indifference as separate stage)
- wisdom_ignorance: Doubt/Confusion

#### 2. Performance Genre Core Value Correction

~~**Current (WRONG)**: `respect_shame` spectrum assigned to Performance~~ **RESOLVED**
**Primary Source (line 224)**: "Accomplishment vs. Failure"

**Resolution (2026-02-04)**: Performance genre remapped to `success_failure` spectrum. Both Status and Performance now share this spectrum, which is appropriate as both involve achievement-based narratives.

#### 3. Worldview Genre Core Value Discrepancy

**Current**: `wisdom_ignorance` spectrum (Wisdom vs. Ignorance)
**Primary Source (line 225)**: "Meaning vs. Meaninglessness"

~~**Analysis**: These are different concepts~~ **RESOLVED**

**Resolution (2026-02-04)**: Retain `wisdom_ignorance` as the spectrum name. The concepts overlap: wisdom enables meaning; ignorance leads to meaninglessness. Spectrum labels include both terms for clarity (e.g., "Wisdom / Meaning"). No compelling reason to change the naming.

#### 4. Internal Genre Subgenres (for reference)

Primary source defines subgenres that affect value trajectory:

**Worldview Subgenres** (line 159):
| Subgenre | Start | End | Direction |
|----------|-------|-----|-----------|
| Maturation | Naive | Sophisticated | Positive |
| Education | Ignorant | Wise | Positive |
| Disillusionment | Belief | Disillusion | Negative |
| Revelation | Blindness | Understanding | Either |

**Morality Subgenres** (line 179):
| Subgenre | Arc |
|----------|-----|
| Redemption | Bad → Good |
| Punitive | Good → Bad |
| Testing | Maintaining goodness under pressure |

**Status Subgenres** (line 194):
| Subgenre | Arc |
|----------|-----|
| Admiration | Rise to success |
| Pathetic | Rise then fall |
| Tragic | Fall from grace |
| Sentimental | Unearned success |

**Action Required**: Add subgenre support to genre definitions. Subgenre affects which Reagan arc shapes are compatible.

#### 5. Obligatory Scenes (for future Gap 3/4 work)

Primary source provides detailed obligatory scenes per genre. Key examples:

**Thriller** (7 scenes): Inciting Crime, Speech in Praise of Villain, Hero at Mercy of Villain, False Ending, Hero Becomes Victim, Clock Plot, Make It Personal

**Love** (6 scenes): Lovers Meet, First Kiss, Confession of Love, Proof of Love, Break Up, Reunite

**Action** (4 scenes): Hero Outmatched, Hero at Mercy of Villain, Hero's Sacrifice, Final Confrontation

**Action Required**: Capture in `story-grid-genres.yaml` under each genre's `obligatory_scenes` list.

#### 6. Conventions by Genre (for future Gap 3/4 work)

Primary source provides conventions that set genre "flavor":

**Thriller**: MacGuffin, Red Herring, Ticking Clock, Strong Mentor, Hidden/Masked Villain
**Love**: External Need, Opposing Forces, Secondary Romance, Rivals, Secrets, Gender Challenges, Moral Weight
**Horror**: Sin/Transgression, Supernatural or Serial Antagonist, Labyrinth, Past Evil

**Action Required**: Capture in `story-grid-genres.yaml` under each genre's `conventions` list.

#### 7. Crisis Types (for Gap 4 Story Actions)

Primary source distinguishes two crisis types (line 295):
- **Best Bad Choice**: Two negative options, must choose lesser evil
- **Irreconcilable Goods**: Two positive options, can only choose one

**Action Required**: Model crisis type in story action library. This affects how GOAP presents climactic choices.

### Implementation Notes

When querying core_emotion or core_need for a genre:

```csharp
public string GetCoreEmotion(string genre)
{
    // Check override first
    if (_genreOverrides.TryGetValue(genre, out var overrides)
        && overrides.CoreEmotion != null)
    {
        return overrides.CoreEmotion;
    }

    // Fall back to spectrum default
    var spectrum = GetPrimarySpectrum(genre);
    return spectrum.CoreEmotion;
}
```

---

## Path Forward

### Immediate Action: Bounded Research Sprint

Before building any integration work, we conduct a **bounded research sprint** (1-2 days maximum) to determine whether Gap 2 (Cross-Framework Mapping) has existing academic or industry solutions.

**The Core Question**:
> Has anyone already established formal correspondences between narrative structure frameworks (Propp, Save the Cat, Story Grid, Emotional Arcs)?

**Search Strategy**:

| Query | Target |
|-------|--------|
| "Propp functions Save the Cat beat sheet" | Direct mapping attempts |
| "story grammar narrative generation" | Computational unification |
| "emotional arc story structure alignment" | Arc + structure combination |
| "unified narrative model" OR "narrative metamodel" | Theoretical frameworks |
| "computational narratology story state" | State-space formalizations |
| Pablo Gervás post-2013 work | Extension of Propp grammar research |
| Mark Riedl narrative planning | Georgia Tech story generation |

**Where to Search**:
- Google Scholar (academic papers)
- arXiv cs.CL (computation and language)
- ACL Anthology (computational linguistics proceedings)
- ICCC proceedings (computational creativity)
- Semantic Scholar (citation graphs)

**What Constitutes Success**:
- A paper mapping 2+ of our frameworks to each other
- A "story state space" or "narrative state" formalization
- A generative grammar incorporating timing/pacing elements
- Even partial mappings we can extend

### Decision Point

After the research sprint, we reach a decision point:

```
                     Research Sprint Complete
                              │
                              ▼
              ┌───────────────────────────────┐
              │  Did we find cross-framework  │
              │  mapping research?            │
              └───────────────┬───────────────┘
                              │
              ┌───────────────┴───────────────┐
              │                               │
              ▼                               ▼
    ┌─────────────────┐             ┌─────────────────┐
    │  YES: Integrate │             │  NO: Create our │
    │  as Fifth Pillar│             │  own mapping    │
    └────────┬────────┘             └────────┬────────┘
             │                               │
             ▼                               ▼
    Document source,              Document methodology,
    validate alignment,           create mapping schema,
    adapt to our needs            validate empirically
```

### Resolution Order (If No External Research Found)

If the research sprint yields nothing substantial, we proceed with original synthesis work in this order:

| Phase | Gap | Deliverable | Rationale |
|-------|-----|-------------|-----------|
| **1** | NarrativeState (Gap 1) | `narrative-state.yaml` | Foundation - everything needs this vocabulary |
| **2** | Cross-Framework Mapping (Gap 2) | `cross-framework-mapping.yaml` | Core intellectual work - unlocks templates and actions |
| **3** | Story Actions (Gap 4) | `story-actions.yaml` | GOAP building blocks that reference the mapping |
| **4** | Narrative Templates (Gap 3) | `narrative-templates.yaml` | Composed from actions using mapping anchors |
| **5** | Archive Extraction (Gap 5) | `archive-extraction.yaml` | Can parallel Phase 4 |
| **6** | Compatibility Matrix (Gap 6) | `compatibility-matrix.yaml` | Derived from accumulated knowledge |

### Current Status

- [x] Gap analysis complete
- [x] Path forward documented
- [x] **COMPLETE**: Research sprint for cross-framework mappings
- [x] **DECISION MADE**: Use NCP as hub schema for cross-framework mapping
- [x] **COMPLETE**: Audit of existing research documents for gap-filling potential
- [x] **COMPLETE**: Primary source extraction (`docs/research/STORY-GRID-PRIMARY-SOURCE.md`)
- [x] **Phase 1: NarrativeState schema** → `schemas/storyline/narrative-state.yaml`
  - 10 Life Value spectrums from Story Grid Four Core Framework
  - Primary spectrum fixed per genre (genre_spectrum_mapping)
  - Secondary spectrums unrestricted (ANY spectrum can be secondary via genre layering)
  - story_combinations section with real media examples (Die Hard, Silence of the Lambs, etc.)
  - Reagan arc integration for temporal dynamics
  - Implementation notes with C# code patterns
  - **RESOLVED**: External/Internal genre classification uses 9+3 split per primary source
  - **RESOLVED**: Core Need = "Safety" for all life_death spectrum genres (Action, Horror, Thriller)
  - **RESOLVED**: Genre emotion overrides: Horror="Fear", Thriller="Anxiety/Dread"
  - **DONE**: Added `genre_overrides` section to YAML schema (Horror, Thriller emotion overrides)
  - **DONE**: Added `contrary` field to all 10 spectrums (4-stage model per primary source)
  - **DONE**: Worldview keeps `wisdom_ignorance` spectrum (2026-02-04 decision) - concepts overlap with "meaning/meaninglessness", labels include both
  - **DONE**: Performance genre remapped to `success_failure` (2026-02-04 decision) - primary source says "Accomplishment vs Failure" which maps directly
  - **TODO**: Add subgenre support for internal genres (affects arc compatibility) - requires architectural design
- [ ] Phase 2: Framework-to-NCP mappings (Propp→NCP, STC→NCP, StoryGrid→NCP)
  - **Note**: Reagan arcs are VALUE CURVES, not event sequences - they don't map to NCP functions
  - Reagan arcs provide the SHAPE of how Life Value spectrums change over time f(t)→[0,1]
- [ ] Phase 3: Story actions library (NCP functions as action templates)
  - **TODO**: Model crisis types (Best Bad Choice vs Irreconcilable Goods)
- [ ] Phase 4: Narrative templates (leverage NCP's Four Throughlines + Nine Dynamics)
  - **TODO**: Capture obligatory scenes per genre from primary source
  - **TODO**: Capture conventions per genre from primary source
- [ ] Phase 5: Archive extraction rules
- [ ] Phase 6: Compatibility matrix (derivable from NCP mappings)

---

## Research Document Integration

The following research documents in `docs/research/` provide solutions or patterns for each gap.

### Gap 1: NarrativeState - Using Existing Research

**DECISION: Use Story Grid's 10 Life Value spectrums.**

The 6-dimension model in STORYLINE_COMPOSER (Tension, Stakes, Mystery, Urgency, Intimacy, Hope) has **no citation**. It was invented by analogy to the music-storyteller SDK's `EmotionalState`, which uses dimensions for *musical perception* (brightness, warmth, energy, valence). Those dimensions describe how music *sounds*, not how stories *mean*. Applying musical perception vocabulary to literary narrative is a category error.

**FOUR-CORE-FRAMEWORK.md** provides the authoritative model - Story Grid's 10 Life Value spectrums:

```csharp
// From FOUR-CORE-FRAMEWORK.md - Story Grid's canonical spectrums
// Each spectrum is 0-1 normalized (negative pole → positive pole)
public class NarrativeState
{
    // Survival/Physical Domain
    public double LifeDeath { get; set; }           // Action, Thriller, Horror

    // Safety/Security Domain
    public double HonorDishonor { get; set; }       // War
    public double JusticeInjustice { get; set; }    // Crime
    public double FreedomSubjugation { get; set; }  // Western, Society

    // Connection/Belonging Domain
    public double LoveHate { get; set; }            // Love

    // Esteem/Recognition Domain
    public double RespectShame { get; set; }        // Performance, Status
    public double PowerImpotence { get; set; }      // Society
    public double SuccessFailure { get; set; }      // Status

    // Self-Actualization Domain
    public double AltruismSelfishness { get; set; } // Morality
    public double WisdomIgnorance { get; set; }     // Worldview
}
```

**Why Story Grid's model is authoritative:**
1. Published methodology (Shawn Coyne, 2015-2020)
2. Grounded in Maslow's hierarchy of needs
3. Each spectrum maps to specific content genres
4. Includes "negation of negation" concept (fate worse than the negative pole)
5. Already has genre → value spectrum → core emotion → core event mappings

**EMOTIONAL-ARCS-SVD-METHODOLOGY.md** provides temporal dynamics:
- Reagan's arcs describe *sentiment trajectory over time* (rise/fall patterns)
- This becomes the *shape* of how value spectrums change during the story
- Example: "Man in Hole" arc = LifeDeath spectrum falls then rises

The Story Grid spectrums define *what's at stake*. Reagan's arcs define *how that stake changes over time*.

### Gap 2: Cross-Framework Mapping - Using Existing Research

**NCP** (already documented) provides the hub schema.

**PROPPER-IMPLEMENTATION.md** provides Propp-specific detail:
- 31 functions with Greek letter symbols (β, γ, δ, ε, ζ, η, θ, A-W)
- Variant counts per function (1-6 variants each)
- Three-act timing: Act 1 (deterministic), Act 2 (50/50 struggle/task), Act 3 (deterministic)
- Seeded generation for reproducibility

```yaml
# From PROPPER-IMPLEMENTATION - Propp → NCP mapping source
propp_functions:
  A_VILLAINY:
    symbol: "A"
    variants: ["villain causes harm", "member lacks something", "member desires something"]
    act: 1
    position_range: [0.08, 0.12]
    ncp_functions: [Temptation, Threat, Uncontrolled]  # TO POPULATE
```

### Gap 3: Narrative Templates - Using Existing Research

**FIVE-LEAF-GENRE-CLOVER.md** provides complete template structure:

```csharp
// From FIVE-LEAF-GENRE-CLOVER.md
public class StoryConfiguration
{
    // Genre specification (5 leaves)
    public FiveLeafGenreClassification Genres { get; set; }

    // Derived from Content Genre
    public CoreNeed CoreNeed { get; set; }
    public ValueSpectrum CoreValue { get; set; }
    public CoreEmotion TargetEmotion { get; set; }
    public CoreEvent RequiredClimax { get; set; }
    public List<string> Conventions { get; set; }
    public List<string> ObligatoryMoments { get; set; }

    // Derived from Structure Genre
    public int ProtagonistCount { get; set; }
    public bool RequiresCausalChain { get; set; }
    public bool RequiresClosedEnding { get; set; }

    // Derived from Reality Genre
    public bool RequiresMagicSystem { get; set; }
    public double SuspensionOfDisbeliefLevel { get; set; }

    // Derived from Time/Style Genre
    public TimeSpan TargetDuration { get; set; }
    public bool IsComedy { get; set; }
}
```

**STORY-GRID-101.md** provides the Foolscap planning template:
- Six core questions for global planning
- Three-act structure (Beginning Hook, Middle Build, Ending Payoff)
- Conventions and Obligatory Events per genre
- Controlling Idea pattern

### Gap 4: Story Actions - Using Existing Research

**STORY-GRID-101.md** provides the Five Commandments as a GOAP action pattern:

```
Inciting Incident → Initial world state perturbation
Turning Point     → Complication requiring re-planning
Crisis            → Decision point requiring character choice
Climax            → Action execution
Resolution        → New world state
```

**RE-PRAXIS-LOGIC-DATABASE.md** provides the query pattern for action preconditions:

```csharp
// From RE-PRAXIS - Action precondition queries
var query = new DBQuery()
    .Where("?character.relationships.?target.sentiment!?s")
    .Where("gt ?s 0.5")
    .Where("?character.attributes.confrontational!?c")
    .Where("gt ?c 0.3");

// If query succeeds, action "confront_directly" is available
```

**PROPPER-IMPLEMENTATION.md** provides 31 Propp functions as action templates:
- Each function has 1-6 variants
- Seeded selection enables reproducibility
- Three-act structure constrains sequencing

### Gap 5: Archive Extraction - Using Existing Research

**RE-PRAXIS-LOGIC-DATABASE.md** provides the world state representation:

```
# Hierarchical tree with cardinality
character.attributes.personality.confrontational!0.7
character.relationships.rival_id.sentiment!-0.5
character.relationships.rival_id.tags.enemy
story.acts.current!2
story.timeline.last_event.type!VILLAINY
```

Key patterns:
- `.` (dot) for one-to-many relationships
- `!` (exclusion) for one-to-one values
- Variable binding (`?var`) for pattern queries
- Before-access listeners for computed properties

Integration with archive:
```csharp
// Map character-personality archive to Re:Praxis world state
db.Insert($"character.personality.confrontational!{archive.Confrontational}");
db.Insert($"character.personality.trusting!{archive.Trusting}");

// Map character-encounter to relationship data
foreach (var encounter in archive.Encounters)
{
    db.Insert($"character.relationships.{encounter.OtherId}.sentiment!{encounter.Sentiment}");
    db.Insert($"character.relationships.{encounter.OtherId}.encounter_count!{encounter.Count}");
}
```

### Gap 6: Compatibility Matrix - Using Existing Research

**FOUR-CORE-FRAMEWORK.md** provides the Genre→Need→Value→Emotion→Event table:

| Genre | Core Need | Life Value | Emotion | Core Event |
|-------|-----------|------------|---------|------------|
| Action | Survival | Death↔Life | Excitement | Hero at Mercy of Villain |
| Horror | Safety | Damnation↔Life | Fear | Victim at Mercy of Monster |
| Crime | Safety | Injustice↔Justice | Intrigue | Exposure of Criminal |
| Love | Connection | Hate↔Love | Romance | Proof of Love |
| Status | Respect | Failure↔Success | Admiration/Pity | Big Choice |
| Morality | Self-Transcendence | Selfishness↔Altruism | Satisfaction/Contempt | Big Choice |
| Worldview | Self-Actualization | Ignorance↔Wisdom | Satisfaction/Pity | Cognitive Growth |

**FIVE-LEAF-GENRE-CLOVER.md** provides reality/structure/time/style constraints:
- Structure genre → protagonist count, causality requirements
- Reality genre → suspension of disbelief level, magic/tech systems
- Time genre → pacing constraints
- Style genre → comedy/drama, medium-specific affordances

Combined with NCP mappings, this enables:
```yaml
compatibility_matrix:
  action:
    story_grid_genre: action
    reagan_arcs: [man_in_hole, cinderella]
    ncp_dynamics:
      story_outcome: success
      story_judgment: good
    core_need: survival
    value_spectrum: life_death
    propp_path: struggle  # vs task
    obligatory_scenes: [hero_at_mercy_of_villain]
```

---

## Research Sprint Results (2026-02-03)

### Executive Summary

**The Narrative Context Protocol (NCP) solves the schema infrastructure problem for Gap 2.** While no pre-built mapping between our four frameworks exists, NCP provides:

1. **The schema structure** for cross-framework mapping via `custom_appreciation_namespace`
2. **145 canonical narrative functions** that can serve as translation intermediaries
3. **979 appreciations** organized by the Four Throughlines
4. **Framework-agnostic design** explicitly intended for multi-methodology support

We don't need to invent a mapping schema - we need to populate NCP's existing schema with our Propp/STC/StoryGrid/Reagan mappings.

### KEY FINDING: Narrative Context Protocol (NCP)

**Source**: [arXiv 2503.04844](https://arxiv.org/abs/2503.04844), MIT Licensed
**Local Repository**: `~/repos/narrative-context-protocol`
**Research Summary**: `docs/research/NARRATIVE-CONTEXT-PROTOCOL.md`

NCP is a JSON schema (v1.2.0) from The Dramatica Co. designed for "authorial intent transport across multi-agent systems." Its architecture directly addresses Gap 2:

#### Cross-Framework Mapping Support

NCP includes explicit fields for mapping appreciations to external frameworks:

```json
{
    "appreciation": "Main Character Symptom",
    "narrative_function": "Disbelief",
    "custom_appreciation": "Alternative Viewpoint",
    "custom_appreciation_namespace": {
        "Dramatica": "Influence Character Symptom",
        "Hero's Journey": "Call to Adventure",
        "Save the Cat!": "Debate"
    }
}
```

This is **exactly what we need** - a standardized way to express that a single narrative moment corresponds to multiple framework terms.

#### 145 Narrative Functions as Translation Layer

NCP defines 144 narrative functions organized into categories:

| Category | Count | Examples |
|----------|-------|----------|
| Perception & Awareness | ~15 | Aware, Consider, Realize, Preconscious |
| Action & Approach | ~20 | Approach, Attempt, Pursuit, Inaction |
| Thought & Analysis | ~15 | Analysis, Logic, Theory, Evaluation |
| Emotional/Psychological | ~20 | Feel, Trust, Doubt, Fear, Hope, Temptation |
| Change & Transformation | ~20 | Change, Becoming, Process, Progress |
| Knowledge & Understanding | ~15 | Knowledge, Learning, Understanding, Truth |
| Causation & Effect | ~10 | Cause, Effect, Result, Consequence |
| Control & Agency | ~15 | Control, Uncontrolled, Protection, Threat |

These functions can serve as the **canonical vocabulary** for our cross-framework mappings. Instead of mapping Propp → STC directly, we map:
- Propp Function → NCP Narrative Function
- STC Beat → NCP Narrative Function
- Story Grid Scene → NCP Narrative Function

**Note**: Reagan arcs are NOT mapped to NCP functions. Reagan arcs are continuous mathematical functions f(t)→[0,1] that describe how the primary Life Value spectrum changes over time. They provide SHAPE, not events. NCP functions are discrete narrative events.

#### Integration with GOAP

The existing research document shows how NCP dynamics can inform GOAP goals:

```csharp
// Dynamics define binary story constraints
// For an Action genre story (primary spectrum: LifeDeath)
if (dynamics.StoryOutcome == "success" && dynamics.StoryJudgment == "good")
{
    goalState.LifeDeath = 0.9;  // Hero survives, threat resolved
}
```

And how narrative functions map to GOAP actions:

```csharp
// Example: VILLAINY action for Action genre (primary: LifeDeath)
var goapAction = new NarrativeAction(
    name: "Villainy",
    preconditions: new NarrativeStateRange { MaxLifeDeath = 0.7 },  // Not already in mortal peril
    effects: new NarrativeStateDelta { LifeDeath = -0.3 },  // Threat introduced
    cost: 1.2
);
```

#### Dramatica Terminology Alignment

NCP includes a translation table from legacy terminology to canonical Dramatica terms (see `docs/terminology/10.dramatica-translation.md`), demonstrating the pattern we can follow for our frameworks.

### Other Research Findings

#### 1. Dramatica Theory & Seven-Paradigm Comparison
**Source**: [Dramatica Theory](https://dramatica.com/theory/), [Comparison of Seven Story Paradigms](https://dramatica.com/theory/articles/a-comparison-of-seven-story-paradigms)

Dramatica is a comprehensive formal narrative framework comparing:
- Syd Field, Michael Hauge, Robert McKee, Linda Seger, John Truby, Christopher Vogler

**What it provides**:
- Four-throughline model (Objective Story, Main Character, Impact Character, Relationship)
- Distinction between author-perspective (structure) vs audience-perspective (meaning)
- Mapping between the six paradigms' act structures and character arcs

**What it lacks**:
- No Propp mapping
- No Reagan emotional arc mapping
- No Story Grid mapping
- No Save the Cat mapping

#### 2. ProppOntology (OWL)
**Source**: [ResearchGate - Propp Ontology](https://www.researchgate.net/publication/319293980)

An OWL-based ontology formalizing Propp's 31 functions with:
- Class hierarchy of narrative functions
- Dramatis Personae relationships
- Instance-level annotations for folktale analysis

**Useful for**: Reference implementation of Propp in a formal ontology language.

#### 3. Propp ↔ Campbell Comparative Studies
**Source**: [Wiley - Pixar Hero's Journey Study](https://onlinelibrary.wiley.com/doi/abs/10.1111/jpcu.70010)

Academic papers exist comparing Propp's Morphology to Campbell's Monomyth:
- Both describe hero journeys but at different granularities
- Propp: 31 functions, folktale-specific
- Campbell: 17 stages, myth-specific
- No formal alignment table widely available (behind paywalls)

#### 4. Hero's Journey ↔ Save the Cat Informal Mappings
**Source**: [Save the Cat - Three Worlds](https://savethecat.com/tips-and-tactics/revisiting-save-the-cat-strikes-back-the-three-worlds), Various blog posts

Informal correspondences documented:
- "Break Into 2" ≈ "Crossing the Threshold"
- "Debate" ≈ "Refusal of the Call"
- "All Is Lost" ≈ "Supreme Ordeal"

**Quality**: Informal, not rigorous, varying interpretations.

#### 5. Reagan's Arcs → Procedural Generation
**Source**: [arXiv - Emotional Arc Guided Procedural Game Level Generation](https://arxiv.org/html/2508.02132v1)

Recent paper operationalizing Reagan's 6 arcs for game generation:
- Maps Rise/Fall patterns to story graph nodes
- Uses LLM generation with emotional arc constraints
- Validates via sentiment analysis

**What it lacks**: No mapping to structural frameworks (Propp, STC, Story Grid).

#### 6. Narrative Ontologies
**Sources**:
- [GOLEM Ontology](https://www.mdpi.com/2076-0787/14/10/193) - Extends CIDOC CRM
- [Universal Narrative Model](https://arxiv.org/html/2503.04844v2/) - Based on Dramatica
- NOnt (Narrative Ontology) - First-order logic, JSON/RDF

Multiple formal ontologies exist but:
- GOLEM focuses on character/world representation, not structural mapping
- UNM is Dramatica-based, doesn't cover our four frameworks
- None integrate Story Grid or Reagan's arcs

### What Does NOT Exist

| Gap | Status |
|-----|--------|
| Propp ↔ Save the Cat mapping | **NOT FOUND** |
| Propp ↔ Story Grid mapping | **NOT FOUND** |
| Propp ↔ Reagan arcs mapping | **NOT FOUND** |
| Save the Cat ↔ Story Grid mapping | **NOT FOUND** |
| Save the Cat ↔ Reagan arcs mapping | **NOT FOUND** |
| Story Grid ↔ Reagan arcs mapping | **NOT FOUND** |
| Any unified model covering all four | **NOT FOUND** |

### Conclusion

**NCP provides the schema infrastructure; we provide the mapping content.** The discovery changes our approach:

| Original Assumption | Revised Understanding |
|---------------------|----------------------|
| We need to invent a mapping schema | NCP's `custom_appreciation_namespace` already exists |
| We need a canonical function vocabulary | NCP's 144 narrative functions are our vocabulary |
| Mapping Propp↔STC↔StoryGrid is 3 pairwise mappings | Map each event framework to NCP functions (3 mappings, hub-and-spoke); Reagan arcs are orthogonal value curves |
| Gap 2 is entirely original synthesis | Gap 2 is populating an existing schema with our framework data |

**What we still do ourselves**:
1. Map Propp's 31 functions to NCP narrative functions
2. Map STC's 16 beats to NCP narrative functions
3. Map Story Grid's obligatory scenes to NCP narrative functions
4. Define NarrativeState deltas for each NCP function (using Story Grid Life Value spectrums)
5. Select Reagan arc shape per template (continuous value curve, orthogonal to NCP event mapping)

**What NCP gives us**:
1. JSON Schema structure (v1.2.0, Draft-07 compliant)
2. Cross-framework namespace pattern
3. Four Throughlines for perspective tracking
4. Nine Dynamics for story outcome constraints
5. Storybeat sequencing with scope hierarchy

### Recommended Mapping Approach (Revised)

With NCP as the hub, we propose a **hub-and-spoke mapping strategy**:

```
         ┌─────────────┐
         │    Propp    │
         │ 31 Functions│
         └──────┬──────┘
                │
    ┌───────────┼───────────┐
    │           │           │
    ▼           ▼           ▼
┌───────┐  ┌────────────┐  ┌──────────┐
│ Save  │  │    NCP     │  │  Story   │
│ the   │◄─┤   144      ├─►│  Grid    │
│ Cat   │  │ Functions  │  │ Scenes   │
└───────┘  └────────────┘  └──────────┘

         ┌─────────────┐
         │   Reagan    │ ─────► VALUE CURVES (orthogonal)
         │   6 Arcs    │        Applied to Life Value spectrums
         └─────────────┘        Not mapped to NCP functions
```

**Key insight**: Reagan arcs are NOT part of the hub-and-spoke. They are continuous value curves f(t) → [0,1] that describe HOW a Life Value spectrum changes over time. NCP functions are discrete narrative events. These are orthogonal concepts - Reagan provides shape, NCP/Propp/STC/StoryGrid provide events.

**Phase 1: Map discrete event frameworks to NCP functions**

For Propp, STC, and Story Grid (discrete event/beat frameworks), identify which NCP narrative function(s) correspond:

```yaml
# Example: Propp → NCP mapping
propp_to_ncp:
  VILLAINY:
    ncp_functions: [Temptation, Threat, Uncontrolled]
    typical_position: 0.10  # Timeline position (orthogonal to Reagan arc)
    # Note: narrative_state_delta uses Story Grid Life Value spectrums

  MEDIATION:
    ncp_functions: [Aware, Consider, Knowledge]
    typical_position: 0.12
```

**Reagan arcs are handled separately** - they provide the continuous value curve f(t) that determines the SHAPE of Life Value spectrum changes, not discrete events.

**Phase 2: Use NCP `custom_appreciation_namespace` for bidirectional lookup**

```json
{
    "narrative_function": "Temptation",
    "custom_appreciation_namespace": {
        "Propp": "VILLAINY",
        "Save the Cat": "CATALYST",
        "Story Grid": "Inciting Incident"
    }
}
```

**Note**: Reagan arcs are NOT included in namespace mappings because they are continuous value functions, not discrete events. Reagan arc selection (e.g., "man_in_hole") determines the SHAPE of primary spectrum change over time - a separate concern from event sequencing.

**Phase 3: Normalize to timeline + NarrativeState**

- All frameworks already have implicit or explicit timing
- NCP's `sequence` field provides temporal ordering
- Map each function to NarrativeState deltas
- Reagan's arcs provide the "shape" that constrains delta sequences

**Phase 4: Validate empirically**

Analyze known stories through all five lenses (four frameworks + NCP) to verify mappings produce coherent results.

---

## References

### Primary Sources (Already Captured in YAML)
- Blake Snyder, "Save the Cat!" (2005)
- Reagan et al., "Emotional arcs of stories" (2016)
- **Shawn Coyne, "The Story Grid: What Good Editors Know" (2015)** - See `docs/research/STORY-GRID-PRIMARY-SOURCE.md` for comprehensive extraction
- Vladimir Propp, "Morphology of the Folktale" (1928)
- Pablo Gervás, "Propp's Morphology as Grammar for Generation" (2013)

### Primary Source Extractions
- **`docs/research/STORY-GRID-PRIMARY-SOURCE.md`** - Comprehensive extraction of Shawn Coyne's Story Grid methodology (extracted 2026-02-04). Authoritative reference for genre classifications, Four Core Framework, value spectrums, Five Commandments, obligatory scenes, and conventions.

### Hub Schema (NCP)
- **The Dramatica Co.** (2024). Narrative Context Protocol Specification v1.2.0
- **arXiv**: 2503.04844
- **Local Repository**: `~/repos/narrative-context-protocol`
- **Research Summary**: `docs/research/NARRATIVE-CONTEXT-PROTOCOL.md`
- Phillips, M.A., & Huntley, C. (1993). *Dramatica: A New Theory of Story*

### Additional Research Documents (in docs/research/)

| Document | Gaps Addressed | Key Contribution |
|----------|----------------|------------------|
| **`STORY-GRID-PRIMARY-SOURCE.md`** | **Gap 1, 3, 4, 6** | **AUTHORITATIVE**: Four Core Framework with exact values, 4-stage value spectrums, obligatory scenes, conventions, Five Commandments, genre subgenres |
| `FOUR-CORE-FRAMEWORK.md` | Gap 1, Gap 6 | 10 Life Value spectrums as NarrativeState dimensions; Genre→Need→Value→Emotion→Event table (**NOTE**: Contains errors corrected by primary source) |
| `FIVE-LEAF-GENRE-CLOVER.md` | Gap 3, Gap 6 | FiveLeafGenreClassification schema; StoryConfiguration with derived constraints |
| `STORY-GRID-101.md` | Gap 3, Gap 4 | Foolscap template for planning; Five Commandments → GOAP action pattern |
| `RE-PRAXIS-LOGIC-DATABASE.md` | Gap 4, Gap 5 | Hierarchical tree storage for world state; query patterns for action preconditions |
| `PROPPER-IMPLEMENTATION.md` | Gap 2, Gap 4 | 31 functions with variants and act timing; seeded deterministic generation |
| `EMOTIONAL-ARCS-SVD-METHODOLOGY.md` | Gap 1 | Sentiment trajectory shapes; 200-point resolution standard |
| `SAVE-THE-CAT-BEAT-SHEET.md` | Gap 2, Gap 3, Gap 4 | MidpointType/DeathType enums; beat function categories; B Story pattern; image contrast validation |

### Potential Research Targets
- Mark Riedl - Georgia Tech computational narrative
- James Ryan - Sheldon/Expressive AI group
- ACL/EMNLP story generation tracks
- ICCC (International Conference on Computational Creativity)
- Narrative Intelligence symposia proceedings
