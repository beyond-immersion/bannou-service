# Storyline SDK Theoretical Foundations

> **Status**: ARCHITECTURE DECIDED, PENDING ITEMS FLAGGED (See Design Decisions section)
> **Priority**: High
> **Related**: `docs/planning/STORYLINE_COMPOSER.md`, `sdks/music-theory/`, `sdks/music-storyteller/`, `docs/plugins/MUSIC.md`
> **Target**: `sdks/storyline-theory/`, `sdks/storyline-storyteller/`
> **Data Schemas**: `schemas/storyline/` (structured YAML specifications)

## Executive Summary

This document captures the theoretical foundations for the storyline SDKs, following the same rigor applied to the music SDKs. The music-storyteller SDK implements peer-reviewed music cognition research (Huron's ITPRA, Koelsch's BRECVEMA, Lerdahl & Jackendoff's Pitch Space). The storyline SDKs will implement equivalent peer-reviewed narrative theory.

**Key Finding**: Computational narratology is a 40+ year field with formal specifications, validated models, and existing implementations. We are not inventing theory - we are implementing it.

**Major Discovery**: The Narrative Context Protocol (NCP) is **open source** - providing a complete JSON schema implementation of Dramatica storyforms with 979+ appreciations, 128 narrative functions, and formal quad algebra.

---

## External Resources & Repositories

### Cloned Reference Implementations

| Repository | Location | Description | Use Case |
|------------|----------|-------------|----------|
| **narrative-context-protocol** | `~/repos/narrative-context-protocol` | Open-source Dramatica JSON schemas | Storyform structure, 4 throughlines, quad algebra |
| **re-praxis** | `~/repos/re-praxis` | C# exclusion logic database | State representation patterns, cardinality enforcement |
| **propper** | `~/repos/propper` | Go implementation of Propp functions | Narrative generation algorithm, three-act structure |
| **propper-narrative** | `~/repos/propper-narrative` | Extended Propp narrative generation | Function sequencing, variant handling |
| **beatsheet** | `~/repos/beatsheet` | JavaScript Save the Cat calculator | Beat percentages, timing validation |
| **core-stories** | `~/repos/core-stories` | Python emotional arc analysis | SVD methodology, sentiment analysis patterns |

### Local Research Materials

Location: `~/repos/story-analysis/research/` (24 markdown files, 4.9 MB)

Key files:
- `THE-FOUR-CORE-FRAMEWORK.md` (84 KB) - Comprehensive Story Grid data
- `propp_functions.md` - Concise 31 function reference
- `Save the Cat Beat Sheet 101.md` - Complete beat methodology
- Genre-specific files (Action, Horror, Thriller, Romance, etc.)

---

## Data Schema Files

Structured YAML specifications ready for code generation:

| Schema | Location | Contents |
|--------|----------|----------|
| **Propp Functions** | `schemas/storyline/propp-functions.yaml` | 31 functions with preconditions/postconditions, 7 dramatis personae, variants, ordering constraints |
| **Save the Cat Beats** | `schemas/storyline/save-the-cat-beats.yaml` | 15+ beats with bs2/fiction percentages, 10 genre categories, Five-Point Finale |
| **Story Grid Genres** | `schemas/storyline/story-grid-genres.yaml` | Five Commandments, Four Core Framework, 12 genres with conventions/obligatory scenes |
| **Emotional Arcs** | `schemas/storyline/emotional-arcs.yaml` | 6 arc shapes with mathematical definitions, SVD methodology, labMT parameters |

These schemas follow the schema-first development pattern - generate C# classes from YAML definitions.

---

## Design Decisions (Resolved)

The following architectural decisions have been made through design review:

### Multi-Phase Action Effects

**Decision**: Use atomic action sequences (GOAP handles sequencing naturally).

Actions that have multi-phase effects (e.g., tension spike during confrontation, then resolution) are modeled as chained atomic actions rather than complex effect objects:

```csharp
// Instead of: NarrativeStateEffect = new() { Tension = +0.3, then = -0.4 }  // INVALID
// Use chained actions:
ConfrontationBegin  → { Tension = +0.3 }  → chains to → ConfrontationResolve → { Tension = -0.4 }
```

**Rationale**: Aligns with how GOAP already works. The planner sees action sequences, not magic delayed effects.

### Template Phase Definitions

**Decision**: Hybrid approach (milestone-based with timing constraints).

Phases are defined by state-based entry/exit conditions BUT with min/max duration constraints:

```csharp
new StoryPhase("Pursuit")
{
    MinDuration = 0.20,  // At least 20% of story
    MaxDuration = 0.45,  // At most 45%
    TargetPosition = 0.35, // Ideally center at 35%
    ExitCondition = state => state.Get("confrontation.proximity") > 0.8,
    TimeoutBehavior = TimeoutBehavior.ForceTransition
}
```

**Rationale**: Pure milestones give unpredictable pacing; pure timing ignores story content. Hybrid ensures pacing while allowing content-driven transitions.

### Intent Execution Model

**Decision**: Follow Actor/Behavior data access patterns (per `ACTOR_DATA_ACCESS_PATTERNS.md`).

| Intent Type | Execution Pattern |
|-------------|-------------------|
| **Read operations** (get character traits, history) | Variable Providers (cached) |
| **Write operations** (create character, modify inventory) | API calls via lib-mesh |
| **Real-time state** | Event subscriptions |

Example:
```csharp
// CharacterIntent execution
await _characterClient.UpdateAsync(intent.ToCharacterRequest());  // lib-mesh

// QuestIntent execution
await _contractClient.CreateFromTemplateAsync(intent.ToContractRequest());  // lib-mesh
```

**Rationale**: Consistent with existing Bannou patterns. No special execution model needed.

---

## Pending Items (Require Further Research)

### Emotional Arc Vectors

**Status**: PENDING PROPER METHODOLOGY

An initial SVD extraction was performed on `core-stories/output/sample-timeseries.csv.gz` (3,078 books, 100 sampling points). However, this differs from Reagan et al.'s methodology (1,327 books, 200 sampling points after specific filtering).

**Findings**:
- Extracted vectors are qualitatively consistent (Mode 1 = monotonic, Mode 2 = parabolic, Mode 3 = two inflections)
- Quantitative values differ due to different corpus and sampling
- First 3 modes explain 83.78% variance (Reagan reports 87.2% for first 3)

**Required**: Either (a) re-run Reagan's exact methodology with identical filtering, or (b) obtain Reagan's published vectors, or (c) accept approximate vectors with documented deviation.

### Scoring Algorithms

**Status**: FRAMEWORKS IDENTIFIED, ALGORITHMS PENDING

Research found qualitative frameworks but no computational formulas:

| Scorer | Framework Source | Convertible Elements |
|--------|------------------|---------------------|
| **KernelIdentifier** | Barthes (conceptual), Propp (functional) | Events satisfying Propp functions or obligatory scenes = kernels |
| **GenreComplianceScorer** | Story Grid obligatory scenes + conventions | Checklist completion percentage |
| **PacingSatisfactionScorer** | Save the Cat beat percentages | Distance from target positions |
| **NarrativePotentialScorer** | No direct source | Needs explicit design (conflict density, relationship count, backstory richness) |

**Required**: Design explicit algorithms with configurable parameters. Each needs:
1. Input specification
2. Calculation formula
3. Output range and interpretation
4. Configurable weights/thresholds

---

## Part 1: Music SDK Precedent Analysis

### What Music-Storyteller Implements

The music-storyteller SDK layers three academically validated frameworks:

| Framework | Academic Source | Year | What It Provides |
|-----------|-----------------|------|------------------|
| **ITPRA** | David Huron, *Sweet Anticipation* | 2006 | Expectation-based listener engagement model |
| **Tonal Pitch Space** | Lerdahl & Jackendoff, *Generative Theory of Tonal Music* | 1983 | Harmonic distance and function calculations |
| **BRECVEMA** | Stefan Koelsch, *Brain and Music* | 2012 | 8 emotional induction mechanisms |

### Music SDK Architecture Pattern

```
sdks/music-theory/           ← Pure data + mechanics (no dependencies)
├── Pitch/                   # PitchClass, Interval, Note
├── Harmony/                 # Chord, Scale, HarmonicFunction
├── Time/                    # Duration, Meter, Tempo
├── Style/                   # Genre-specific rules
└── Output/                  # MidiJson serialization

sdks/music-storyteller/      ← Planning layer (depends on music-theory)
├── Templates/               # NarrativeTemplate, NarrativePhase
├── State/                   # EmotionalState (6 dimensions)
├── Listener/                # ListenerModel (ITPRA implementation)
├── Mechanisms/              # MechanismState (BRECVEMA implementation)
├── Planning/                # GOAPPlanner, Intent generation
└── Storyteller.cs           # Main orchestrator

plugins/lib-music/           ← Service wrapper (caching, persistence, HTTP)
```

### Key Design Principles from Music SDKs

1. **Theory is separate from service** - SDKs have no service dependencies
2. **Layered models** - Multiple complementary frameworks, not one monolithic theory
3. **Quantified state spaces** - EmotionalState uses 6 normalized dimensions (0-1)
4. **Templates define journeys** - Phases with target states, not fixed content
5. **GOAP for action selection** - A* search finds paths through state space
6. **Deterministic when seeded** - Same inputs produce same outputs

---

## Part 2: Narrative Theory Research

### Tier 1: Formally Specified Frameworks (Computational Ready)

#### Vladimir Propp's Morphology of the Folktale (1928)

> **Schema**: `schemas/storyline/propp-functions.yaml`
> **Reference Implementations**: `~/repos/propper` (Go), `~/repos/propper-narrative` (Extended)

**31 narrative functions** occurring in fixed sequential order:

| Phase | Functions | Description |
|-------|-----------|-------------|
| **Preparation** (1-7) | Absentation, Interdiction, Violation, Reconnaissance, Delivery, Trickery, Complicity | Setup and initial disruption |
| **Complication** (8-11) | Villainy/Lack, Mediation, Counteraction, Departure | Problem established, hero commits |
| **Donor Sequence** (12-15) | Testing, Reaction, Acquisition, Guidance | Hero gains magical aid |
| **Struggle** (16-19) | Combat, Branding, Victory, Liquidation | Conflict and resolution |
| **Return** (20-31) | Return, Pursuit, Rescue, Recognition, Exposure, Transfiguration, Punishment, Wedding | Hero returns transformed |

**Seven Character Archetypes (Dramatis Personae)**:
- Villain (A), Dispatcher (B), Helper (C), Donor (D), Hero (E), Princess/Sought-for Person (F), False Hero (G)

**Computational Formalization** (Gervás 2013):

Each function has formal preconditions and postconditions enabling GOAP-style planning:

```yaml
villainy:
  symbol: "A"
  preconditions:
    - villain_present
    - victim_available
  postconditions:
    - harm_caused
    - narrative_problem_established
  initiates: complication_phase
```

The `propper` Go implementation demonstrates a three-act compositional algorithm:
1. **Preparation** → Select from functions 1-7 with probabilistic inclusion
2. **Complication** → Function 8 (Villainy/Lack) is mandatory; 9-11 follow
3. **Return** → Functions 20-31 with constraint that Wedding requires prior Victory

**Best For**: Quest narratives, folktale patterns, adventure storylines, procedural story generation.

---

#### Dramatica Theory (Huntley & Phillips, 1994)

> **MAJOR DISCOVERY**: The Narrative Context Protocol (NCP) is **open source**!
> **Repository**: `~/repos/narrative-context-protocol`
> **Format**: Complete JSON schemas for storyforms

The most formally specified narrative framework, based on the **Story Mind** concept.

**Four Throughlines** (every complete story requires all four):
1. **Objective Story** (`we`) - External plot everyone participates in
2. **Main Character** (`i`) - Protagonist's internal journey
3. **Obstacle Character** (`you`) - Character challenging MC's worldview
4. **Subjective Story** (`they`) - Relationship between MC and OC

**NCP Schema Provides**:
- **979+ Appreciations**: Named narrative elements with formal definitions
- **128 Narrative Functions**: Quad-based element relationships
- **9 Story Dynamics**: Driver, Limit, Outcome, Judgment, etc. with vector values
- **4 Perspectives**: `i`, `you`, `we`, `they` mapping to throughlines
- **Quad Algebra**: Mathematical relationships between elements

Example from NCP schema:
```json
{
  "storyform": {
    "dynamics": {
      "driver": { "value": "action", "vector": 1 },
      "limit": { "value": "optionlock", "vector": -1 },
      "outcome": { "value": "success", "vector": 1 },
      "judgment": { "value": "good", "vector": 1 }
    },
    "throughlines": {
      "objective_story": {
        "domain": "physics",
        "concern": "obtaining",
        "issue": "attitude",
        "problem": "expectation"
      }
    }
  }
}
```

**Best For**: Complete story analysis, identifying structural holes, character arc consistency.

**Implementation Strategy**: Selective adoption - use throughline structure and dynamics for story validation, defer full quad algebra to later phases. NCP provides the complete formal specification when needed.

---

#### Story Grammars (Rumelhart, Mandler & Johnson, Thorndyke, 1970s-80s)

Context-free rewrite rules for story structure:

```
STORY       → SETTING + EVENT_STRUCTURE
EVENT_STRUCT → EPISODE+
EPISODE     → BEGINNING + DEVELOPMENT + ENDING
BEGINNING   → EVENT | EPISODE
DEVELOPMENT → SIMPLE_REACTION + GOAL_PATH
GOAL_PATH   → ATTEMPT + OUTCOME
```

**Empirical Validation**: Stories conforming to grammatical structure are better remembered (recall experiments).

**Best For**: Hierarchical story composition, validation of structural completeness.

---

#### Plot Units (Wendy Lehnert, 1981)

Affect-based configurations representing emotional patterns:

```
Positive (+)  ─────┐
                   ├───► Resolution
Negative (-)  ─────┘
     │
Mental (M) ────────► Goal/Intention
```

Plot units **overlap** when narratives are cohesive. Graph topology reveals which events are central vs. peripheral.

**Best For**: Narrative summarization, identifying essential events, adaptation decisions.

---

### Tier 2: Practitioner Frameworks (Genre Conventions)

#### Story Grid (Shawn Coyne)

> **Schema**: `schemas/storyline/story-grid-genres.yaml`
> **Research**: `~/repos/story-analysis/research/THE-FOUR-CORE-FRAMEWORK.md` (84 KB comprehensive data)

**Five Commandments** - Every scene must have:
1. **Inciting Incident** - Disrupts life balance (causal or coincidental)
2. **Progressive Complications** - Obstacles escalate (turning points)
3. **Crisis** - Forced choice between two options (best bad choice or irreconcilable goods)
4. **Climax** - Action taken in response to crisis (reveals character)
5. **Resolution** - Outcome of climax action (new equilibrium)

**Four Core Framework** (per genre):

| Component | Description | Example (Action) |
|-----------|-------------|------------------|
| **Core Need** | What protagonist must obtain | Survival |
| **Value Spectrum** | What's at stake (negative ↔ positive) | Death ↔ Life |
| **Core Emotion** | What reader should feel | Excitement |
| **Core Event** | Obligatory climactic scene | Hero at Mercy of Villain |

**12 Genre Categories** (9 External + 3 Internal):

**External Genres** (external change):
- Action, Horror, Thriller, Crime, Western, War, Love, Performance, Society

**Internal Genres** (internal change):
- Worldview, Morality, Status

**Genre-Specific Requirements** (expanded from schema):

| Genre | Core Value | Core Event | Key Conventions |
|-------|------------|------------|-----------------|
| **Action** | Life ↔ Death | Hero at Mercy of Villain | Clear goal, Clock, Training |
| **Thriller** | Life ↔ Damnation | Hero at Mercy of Villain | Master villain, MacGuffin, Red herrings |
| **Horror** | Life ↔ Fate Worse Than Death | Victim at Mercy of Monster | Monster with unique power, Sin/transgression |
| **Crime** | Justice ↔ Injustice | Exposure of Criminal | Red herrings, Clue trail, Making it personal |
| **Love** | Love ↔ Hate | Proof of Love | Helpers/harmers, Triangle, External obstacles |
| **Worldview** | Sophistication ↔ Naïveté | Moment of Realization | Mentor, Coming-of-age markers |
| **Morality** | Altruism ↔ Selfishness | Moral Weight Scene | Temptation, Sacrifice opportunity |
| **Status** | Success ↔ Failure | Big Event | Rise/fall arc, External measure of success |

**Best For**: Genre validation, ensuring reader/player expectations are met, obligatory scene checklists.

---

#### Save the Cat (Blake Snyder, 2005)

> **Schema**: `schemas/storyline/save-the-cat-beats.yaml`
> **Reference Implementation**: `~/repos/beatsheet` (JavaScript calculator)
> **Research**: `~/repos/story-analysis/research/Save the Cat Beat Sheet 101.md`

**15 Beats with Percentage Targets** (two strategies from beatsheet):

| Beat | BS2 % | Fiction % | Description |
|------|-------|-----------|-------------|
| Opening Image | 0% | 0% | Visual "before" snapshot |
| Theme Stated | 5% | 3% | Theme/lesson articulated |
| Set-Up | 1-10% | 1-10% | World, flaws established |
| Catalyst | 10% | 10% | Inciting incident |
| Debate | 10-20% | 10-20% | Hero questions journey |
| Break into Two | 20% | 20% | Hero commits to Act 2 |
| B Story | 22% | 22% | Secondary story (often relationship) |
| Fun and Games | 20-50% | 20-50% | "Promise of the premise" |
| Midpoint | 50% | 50% | False victory or false defeat |
| Bad Guys Close In | 50-75% | 50-68% | Pressures mount |
| All Is Lost | 75% | 68% | Lowest point, "whiff of death" |
| Dark Night of Soul | 75-80% | 68-77% | Hero in despair |
| Break into Three | 80% | 77% | Solution found via B Story |
| Finale | 80-99% | 77-99% | Hero applies learning, wins |
| Final Image | 100% | 100% | Visual "after" (transformation) |

**Midpoint Types** (from schema):
- **False Victory**: Hero seems to achieve goal but hasn't truly
- **False Defeat**: Appears all is lost but isn't; often followed by raising stakes

**Five-Point Finale Structure**:
1. Gathering the Team (80%)
2. Executing the Plan (85%)
3. High Tower Surprise (90%)
4. Dig Deep Down (95%)
5. Execution of New Plan (97%)

**10 Genre Categories** with core elements:
- **Monster in the House**: Monster + enclosed space + sin that created monster
- **Golden Fleece**: Road + team + prize worth dying for
- **Out of the Bottle**: Wish + spell + lesson to learn
- **Dude with a Problem**: Innocent hero + sudden event + life-or-death stakes
- **Rites of Passage**: Life problem + wrong way + acceptance
- **Buddy Love**: Incomplete hero + counterpart + complication
- **Whydunit**: Detective + secret + dark turn
- **Fool Triumphant**: Fool + establishment + transmutation
- **Institutionalized**: Group + choice + sacrifice
- **Superhero**: Special power + nemesis + curse

**Best For**: Pacing constraints, beat timing validation, act structure enforcement.

---

#### McKee's Story (Robert McKee, 1997)

**Value-Based Hierarchical Structure**:

| Level | Definition | Scale |
|-------|------------|-------|
| **Beat** | Smallest unit of value change | Seconds |
| **Scene** | Action through conflict turning values with perceptible significance | Minutes |
| **Sequence** | Series of scenes building to larger value change | 10-15 minutes |
| **Act** | Series of sequences leading to extreme value change | 20-40 minutes |
| **Story** | Acts with irreversible climactic change | Full work |

A standard film has **40-60 events**, each turning values from positive to negative or vice versa.

**Best For**: Understanding dramatic rhythm, scene-level value tracking.

---

### Tier 3: Research-Validated Patterns

#### Six Emotional Arcs (Reagan et al., 2016)

> **Schema**: `schemas/storyline/emotional-arcs.yaml`
> **Reference Implementation**: `~/repos/core-stories` (Python/Jupyter, labMT sentiment analysis)
> **Paper**: "The emotional arcs of stories are dominated by six basic shapes" (EPJ Data Science)
> **arXiv**: https://arxiv.org/abs/1606.07772

Data mining of 1,327 Project Gutenberg stories (after quality filtering from 1,748) using SVD decomposition:

| Arc | Pattern | Mathematical Form | Inflections | Examples |
|-----|---------|-------------------|-------------|----------|
| **Rags to Riches** | ↗ | f(t) ≈ mt + c (m > 0) | 0 | Rocky, Pursuit of Happyness |
| **Tragedy** | ↘ | f(t) ≈ -mt + c (m > 0) | 0 | Hamlet, Flowers for Algernon |
| **Man in a Hole** | ↘↗ | f(t) ≈ a(t - 0.5)² + c (a > 0) | 1 @ 40-60% | A Christmas Carol, Finding Nemo |
| **Icarus** | ↗↘ | f(t) ≈ -a(t - 0.5)² + c (a > 0) | 1 @ 40-60% | Great Gatsby, Breaking Bad |
| **Cinderella** | ↗↘↗ | Complex (modes 1+2) | 2 | Jane Eyre, Pride and Prejudice |
| **Oedipus** | ↘↗↘ | Complex (modes 1+2) | 2 | 1984, The Metamorphosis |

**Methodology** (from core-stories):
- **Corpus**: Project Gutenberg (20K-100K words, 150+ downloads, 1000+ unique words)
- **Sentiment**: labMT (Language Assessment by Mechanical Turk) - 10,000 words scored 1-9
- **Analysis**: Sliding window (10K words), 200 equidistant sample points
- **Decomposition**: SVD with first 6 modes explaining 94.1% of variance

**SVD Mode Mapping**:
- Mode 1 (+/-) → Rags to Riches / Tragedy
- Mode 2 (+/-) → Man in a Hole / Icarus
- Modes 1+2 combined → Cinderella / Oedipus

**Key Finding**: Cinderella arc (rise-fall-rise) correlates with highest download counts - most satisfying for audiences.

**Best For**: Pacing templates, emotional trajectory planning, audience satisfaction prediction, arc classification.

---

#### Kernels vs. Satellites (Barthes/Chatman)

From Roland Barthes' "Introduction to the Structural Analysis of Narratives" (1966):

| Type | Definition | Deletability | Function |
|------|------------|--------------|----------|
| **Kernels** (Cardinal Functions) | Branching points where choices occur | Cannot delete without altering story logic | Drive narrative forward |
| **Satellites** (Catalyzers) | Elaboration and texture | Can be freely deleted | Embellish kernels |

**Practical Application**: When compressing or adapting stories, kernels must be preserved; satellites may be changed or removed.

**Best For**: Narrative summarization, compression decisions, archive extraction.

---

#### Fabula vs. Sjuzhet (Russian Formalism)

| Concept | Definition | Example |
|---------|------------|---------|
| **Fabula** | Chronological sequence of events (what happened) | Murder → Investigation → Arrest |
| **Sjuzhet** | Artistic arrangement (how it's told) | Body found → Flashbacks → Reveal |

Maps to Chatman's **Story vs. Discourse**:
- **Story**: Content (events, characters, settings)
- **Discourse**: Expression (narration, order, focalization)

**Best For**: Separating "what happens" from "how it's presented" in generation.

---

#### Greimas' Actantial Model (6 Actants)

Abstract character roles independent of specific characters:

| Axis | Actant 1 | Actant 2 |
|------|----------|----------|
| **Desire** | Subject (who wants) | Object (what is wanted) |
| **Communication** | Sender (who initiates quest) | Receiver (who benefits) |
| **Power** | Helper (who assists) | Opponent (who opposes) |

**Key Insight**: Multiple characters can fill one actant role; one character can fill multiple actant roles.

**Best For**: Character role abstraction, relationship modeling.

---

## Part 3: Computational Precedents

### Façade (Mateas & Stern, 2005)

> **Reference**: ABL (A Behavior Language) documentation
> **Pattern**: Beat-based drama management

**Architecture**:
- **Behaviors**: Small programs performing dramatic action
- **Beats**: Collections of 10-100 behaviors for specific situations
- **Drama Manager**: Sequences beats based on story state (27 beats total)

**Key Innovation**: Beat-based drama management with Aristotelian dramatic arc.

**Lesson for Us**: The drama manager pattern aligns with Regional Watchers calling the storyline composer. ABL's sequential/parallel behavior trees influenced ABML design.

---

### Versu (Evans & Short, 2013)

> **C# Implementation**: `~/repos/re-praxis` (exclusion logic database)
> **Pattern**: Praxis exclusion logic for state representation

**Architecture**:
- **Exclusion Logic Database**: Facts with cardinality constraints
- **Utility-Based Agents**: Characters select highest-utility actions
- **Social Practices**: Multiple concurrent interaction patterns

**Re-Praxis Implementation Details** (from `~/repos/re-praxis`):

```csharp
// Cardinality operators: '.' = MANY, '!' = ONE
// "character.bob.location!kitchen" - Bob can only be in ONE location
// "character.bob.friends.alice" - Bob can have MANY friends

var db = new PraxisDatabase();
db.Insert("character.bob.location!kitchen");  // Exclusive - sets location
db.Insert("character.bob.friends.alice");     // Additive - adds friend
db.Insert("character.bob.location!bedroom");  // Replaces kitchen (ONE cardinality)

var query = db.Query("character.bob.location!?where");
// Returns: [{ where: "bedroom" }]
```

**Key Patterns for Storyline SDK**:
- Cardinality enforcement (`.` for MANY, `!` for ONE) maps to NarrativeState constraints
- Query syntax with variables (`?var`) enables pattern matching on story state
- Exclusion semantics ensure world state consistency

**Lesson for Us**: Re-Praxis patterns could inform WorldState representation for GOAP planning - enforce that a character can only have ONE current goal, but MANY relationships.

---

### Scheherazade (Riedl, Georgia Tech)

**Architecture**:
- Crowdsourced narrative knowledge
- Hierarchical neural story generation
- Plot structure learning from human examples

**Key Innovation**: Learning narrative patterns from examples rather than hand-coding rules.

**Lesson for Us**: Archive extraction is similar - learning story patterns from accumulated play history.

---

## Part 4: Proposed SDK Architecture

### Layer Mapping (Music → Storyline)

| Music Concept | Storyline Equivalent | Academic Basis |
|---------------|---------------------|----------------|
| `PitchClass`, `Interval` | `NarrativeFunction`, `StoryAtom` | Propp's 31 functions |
| `Chord`, `Scale` | `Scene`, `Sequence` | McKee's hierarchy |
| `HarmonicFunction` | `ActantRole` | Greimas' 6 actants |
| `Cadence` | `ResolutionPattern` | Genre-specific endings |
| `Voice` | `CharacterThread` | Dramatica throughlines |
| `EmotionalState` (6D) | `NarrativeState` (6D) | Proposed (see below) |
| `ListenerModel` (ITPRA) | `AudienceModel` | Genre expectations |
| `MechanismState` (BRECVEMA) | `EngagementState` | Active narrative hooks |
| `NarrativeTemplate` | `StoryArcTemplate` | Hero's Journey, Save the Cat |
| `NarrativePhase` | `StoryPhase` | Act structure |
| `CompositionIntent` | `StorylineIntent` | Concrete actions |
| `GOAPPlanner` | Same (reuse from lib-behavior) | A* for action sequencing |

---

### storyline-theory SDK Structure

```
sdks/storyline-theory/
├── Elements/
│   ├── StoryAtom.cs              # Smallest narrative unit
│   ├── NarrativeFunction.cs      # Propp's 31 functions
│   ├── Scene.cs                  # McKee's scene structure
│   ├── Sequence.cs               # Scene groupings
│   └── Act.cs                    # Major structural divisions
│
├── Characters/
│   ├── ActantRole.cs             # Greimas' 6 roles
│   ├── CharacterThread.cs        # Character's narrative arc
│   ├── Relationship.cs           # Inter-character dynamics
│   └── CharacterArchetype.cs     # Propp's 7 dramatis personae
│
├── Structure/
│   ├── StoryBeat.cs              # Value-changing moment
│   ├── Kernel.cs                 # Essential event (Barthes)
│   ├── Satellite.cs              # Optional elaboration
│   ├── BeatType.cs               # Save the Cat beat taxonomy
│   └── FabulaSjuzhet.cs          # Story content vs presentation
│
├── Genre/
│   ├── GenreConventions.cs       # Story Grid conventions
│   ├── ObligatoryScene.cs        # Required genre scenes
│   ├── CoreValue.cs              # What's at stake (life, love, justice...)
│   └── GenreDefinitions.cs       # Built-in genre configurations
│
├── State/
│   ├── NarrativeState.cs         # 6-dimensional state space
│   ├── WorldState.cs             # GOAP-compatible world representation
│   ├── StoryProgress.cs          # Arc completion tracking
│   └── ValueSpectrum.cs          # Positive ↔ Negative value tracking
│
├── Arcs/
│   ├── EmotionalArc.cs           # Six validated arc shapes
│   ├── CharacterArc.cs           # Transformation vs flat
│   └── ArcTemplate.cs            # Reusable arc patterns
│
├── Scoring/
│   ├── KernelIdentifier.cs       # Classify essential vs optional
│   ├── NarrativePotentialScorer.cs # Rate archive entries for story seeds
│   ├── GenreComplianceScorer.cs  # Check convention adherence
│   └── PacingSatisfactionScorer.cs # Evaluate emotional trajectory
│
└── Output/
    ├── StorylinePlan.cs          # Complete plan output
    ├── StorylineJson.cs          # Serialization format
    └── FabulaRepresentation.cs   # Chronological event sequence
```

---

### storyline-storyteller SDK Structure

```
sdks/storyline-storyteller/
├── Templates/
│   ├── StoryArcTemplate.cs       # Base template class
│   ├── RevengeArc.cs             # Discovery → Pursuit → Confrontation → Aftermath
│   ├── MysteryArc.cs             # Hook → Investigation → Revelations → Resolution
│   ├── RedemptionArc.cs          # Fall → Suffering → Realization → Atonement
│   ├── LegacyArc.cs              # Death → Memory → Inspiration → Continuation
│   ├── TragicArc.cs              # Hubris → Warning → Denial → Fall → Consequences
│   ├── QuestArc.cs               # Call → Threshold → Trials → Reward → Return
│   └── RomanceArc.cs             # Meet → Obstacles → Separation → Reunion
│
├── Actions/
│   ├── IStoryAction.cs           # Action interface (preconditions/effects/cost)
│   ├── ActionLibrary.cs          # Registry of available actions
│   ├── ConflictActions.cs        # IntroduceAntagonist, EscalateConflict, Betrayal...
│   ├── RelationshipActions.cs    # FormAlliance, DestroyTrust, RevealConnection...
│   ├── MysteryActions.cs         # PlantClue, RevealSecret, RedHerring, ConnectDots...
│   ├── ResolutionActions.cs      # Confrontation, Redemption, Justice, Restoration...
│   └── TransformationActions.cs  # CharacterGrowth, Revelation, Sacrifice...
│
├── Planning/
│   ├── StoryGOAPPlanner.cs       # A* search for story actions
│   ├── StoryGOAPGoal.cs          # Target narrative state
│   ├── StoryGOAPAction.cs        # Wrapper for IStoryAction
│   ├── NarrativePlanner.cs       # High-level arc planning
│   └── PhasePlanner.cs           # Per-phase action selection
│
├── Audience/
│   ├── AudienceModel.cs          # Genre expectation tracking
│   ├── ExpectationState.cs       # What audience anticipates
│   ├── SurpriseTracker.cs        # Novelty and twist tracking
│   └── SatisfactionPredictor.cs  # Will this resolution satisfy?
│
├── Engagement/
│   ├── EngagementState.cs        # Which narrative hooks are active
│   ├── TensionMechanism.cs       # Unresolved conflict tracking
│   ├── MysteryMechanism.cs       # Unanswered question tracking
│   ├── RelationshipMechanism.cs  # Emotional investment tracking
│   └── StakesMechanism.cs        # What's at risk tracking
│
├── Extraction/
│   ├── ArchiveExtractor.cs       # ResourceArchive → WorldState
│   ├── BackstoryParser.cs        # character-history → StoryElements
│   ├── EncounterAnalyzer.cs      # character-encounter → Relationships
│   ├── PersonalityMapper.cs      # character-personality → CharacterTraits
│   └── KernelExtractor.cs        # Identify essential events from history
│
├── Intent/
│   ├── StorylineIntent.cs        # Abstract intent
│   ├── CharacterIntent.cs        # Spawn/modify character
│   ├── RelationshipIntent.cs     # Create/modify relationship
│   ├── ConflictIntent.cs         # Establish/escalate conflict
│   ├── QuestIntent.cs            # Create quest via lib-contract
│   ├── SceneIntent.cs            # Setup scene via lib-scene
│   └── BehaviorIntent.cs         # Generate ABML fragment
│
├── Continuation/
│   ├── ContinuationPoint.cs      # Lazy evaluation pause point
│   ├── PhaseTransition.cs        # Inter-phase continuation
│   ├── WorldStateDelta.cs        # What changed since pause
│   └── AdaptivePlanner.cs        # Replan based on world changes
│
└── Storyteller.cs                # Main orchestrator
```

---

### NarrativeState: The 6-Dimensional State Space

Parallel to `EmotionalState` in music-storyteller:

```csharp
/// <summary>
/// Six-dimensional narrative state space for story beat tracking.
/// All dimensions normalized 0.0 to 1.0.
/// </summary>
public sealed class NarrativeState
{
    /// <summary>
    /// Conflict intensity. 0 = resolved/peaceful, 1 = climactic confrontation.
    /// Maps to: unresolved conflicts, active threats, confrontation proximity.
    /// </summary>
    public double Tension { get; set; }

    /// <summary>
    /// What's at risk. 0 = trivial consequences, 1 = existential/irreversible.
    /// Maps to: life/death, love/loss, honor/shame magnitude.
    /// </summary>
    public double Stakes { get; set; }

    /// <summary>
    /// Unanswered questions. 0 = everything clear, 1 = deep enigma.
    /// Maps to: hidden information, unrevealed secrets, unclear motivations.
    /// </summary>
    public double Mystery { get; set; }

    /// <summary>
    /// Time pressure. 0 = leisurely/no deadline, 1 = desperate/immediate.
    /// Maps to: countdown timers, approaching threats, windows closing.
    /// </summary>
    public double Urgency { get; set; }

    /// <summary>
    /// Character emotional closeness. 0 = strangers/enemies, 1 = deeply bonded.
    /// Maps to: relationship depth, trust levels, shared history.
    /// </summary>
    public double Intimacy { get; set; }

    /// <summary>
    /// Expected outcome valence. 0 = despair/doom, 1 = hope/triumph.
    /// Maps to: protagonist advantage, resource availability, ally support.
    /// </summary>
    public double Hope { get; set; }

    // Distance calculation for GOAP heuristics
    public double DistanceTo(NarrativeState target) =>
        Math.Sqrt(
            Math.Pow(Tension - target.Tension, 2) +
            Math.Pow(Stakes - target.Stakes, 2) +
            Math.Pow(Mystery - target.Mystery, 2) +
            Math.Pow(Urgency - target.Urgency, 2) +
            Math.Pow(Intimacy - target.Intimacy, 2) +
            Math.Pow(Hope - target.Hope, 2)
        ) / Math.Sqrt(6); // Normalize to 0-1

    public NarrativeState InterpolateTo(NarrativeState target, double t) =>
        new()
        {
            Tension = Tension + (target.Tension - Tension) * t,
            Stakes = Stakes + (target.Stakes - Stakes) * t,
            Mystery = Mystery + (target.Mystery - Mystery) * t,
            Urgency = Urgency + (target.Urgency - Urgency) * t,
            Intimacy = Intimacy + (target.Intimacy - Intimacy) * t,
            Hope = Hope + (target.Hope - Hope) * t
        };

    public static class Presets
    {
        public static NarrativeState Equilibrium =>
            new() { Tension = 0.2, Stakes = 0.3, Mystery = 0.2, Urgency = 0.2, Intimacy = 0.5, Hope = 0.7 };

        public static NarrativeState RisingAction =>
            new() { Tension = 0.5, Stakes = 0.5, Mystery = 0.5, Urgency = 0.5, Intimacy = 0.5, Hope = 0.5 };

        public static NarrativeState Climax =>
            new() { Tension = 0.95, Stakes = 0.9, Mystery = 0.3, Urgency = 0.9, Intimacy = 0.8, Hope = 0.4 };

        public static NarrativeState Resolution =>
            new() { Tension = 0.1, Stakes = 0.2, Mystery = 0.1, Urgency = 0.1, Intimacy = 0.9, Hope = 0.85 };

        public static NarrativeState Tragedy =>
            new() { Tension = 0.1, Stakes = 0.3, Mystery = 0.1, Urgency = 0.1, Intimacy = 0.8, Hope = 0.1 };

        public static NarrativeState MysteryHook =>
            new() { Tension = 0.4, Stakes = 0.4, Mystery = 0.9, Urgency = 0.3, Intimacy = 0.3, Hope = 0.5 };

        public static NarrativeState DarkestHour =>
            new() { Tension = 0.7, Stakes = 0.9, Mystery = 0.2, Urgency = 0.8, Intimacy = 0.7, Hope = 0.1 };
    }
}
```

---

### Genre Configuration Example

```csharp
public static class GenreDefinitions
{
    public static GenreConventions Thriller => new()
    {
        Name = "Thriller",
        PrimaryValue = CoreValue.Life,
        ValueSpectrum = new ValueSpectrum
        {
            Positive = "Life",
            Negative = "Death",
            NegationOfNegation = "Damnation" // Fate worse than death
        },

        ObligatoryScenes = new[]
        {
            new ObligatoryScene("IncitingCrime", "Crime indicative of master villain"),
            new ObligatoryScene("StakesPersonal", "Stakes become personal to hero"),
            new ObligatoryScene("VillainMotivation", "Hero discovers what villain wants"),
            new ObligatoryScene("HeroAtMercy", "Hero at mercy of villain"),
            new ObligatoryScene("FalseEnding", "Villain appears defeated but isn't"),
            new ObligatoryScene("FinalConfrontation", "Hero confronts villain directly")
        },

        Conventions = new[]
        {
            new GenreConvention("MasterVillain", "Antagonist more powerful than protagonist"),
            new GenreConvention("Clock", "Ticking time pressure element"),
            new GenreConvention("MacGuffin", "Object of desire driving plot"),
            new GenreConvention("RedHerring", "False lead misdirecting audience")
        },

        ExpectedArc = EmotionalArc.ManInHole, // Fall then rise

        NarrativeStateTargets = new Dictionary<string, NarrativeState>
        {
            ["Opening"] = new() { Tension = 0.3, Stakes = 0.4, Mystery = 0.6, Urgency = 0.3, Intimacy = 0.3, Hope = 0.6 },
            ["Midpoint"] = new() { Tension = 0.7, Stakes = 0.7, Mystery = 0.4, Urgency = 0.6, Intimacy = 0.5, Hope = 0.4 },
            ["DarkestHour"] = new() { Tension = 0.8, Stakes = 0.9, Mystery = 0.2, Urgency = 0.9, Intimacy = 0.6, Hope = 0.15 },
            ["Climax"] = new() { Tension = 0.95, Stakes = 0.95, Mystery = 0.1, Urgency = 0.95, Intimacy = 0.7, Hope = 0.5 },
            ["Resolution"] = new() { Tension = 0.1, Stakes = 0.2, Mystery = 0.05, Urgency = 0.1, Intimacy = 0.8, Hope = 0.9 }
        }
    };

    public static GenreConventions Mystery => new()
    {
        Name = "Mystery",
        PrimaryValue = CoreValue.Justice,
        ValueSpectrum = new ValueSpectrum
        {
            Positive = "Justice",
            Negative = "Injustice",
            NegationOfNegation = "Tyranny"
        },

        ObligatoryScenes = new[]
        {
            new ObligatoryScene("CrimeDiscovered", "The crime/mystery is revealed"),
            new ObligatoryScene("ClueGathering", "Detective collects evidence"),
            new ObligatoryScene("RedHerringPursuit", "False lead is followed"),
            new ObligatoryScene("TruthRevealed", "Detective exposes the truth"),
            new ObligatoryScene("CriminalUnmasked", "Identity of perpetrator revealed")
        },

        Conventions = new[]
        {
            new GenreConvention("CleverCriminal", "Criminal capable of concealment"),
            new GenreConvention("CleverDetective", "Investigator capable of solving"),
            new GenreConvention("Clues", "Evidence available to audience"),
            new GenreConvention("RedHerrings", "Misleading evidence"),
            new GenreConvention("FairPlay", "Audience has same info as detective")
        },

        ExpectedArc = EmotionalArc.RagsToRiches, // Steady rise toward truth

        NarrativeStateTargets = new Dictionary<string, NarrativeState>
        {
            ["Opening"] = new() { Tension = 0.4, Stakes = 0.5, Mystery = 0.9, Urgency = 0.3, Intimacy = 0.2, Hope = 0.5 },
            ["Investigation"] = new() { Tension = 0.5, Stakes = 0.5, Mystery = 0.7, Urgency = 0.4, Intimacy = 0.4, Hope = 0.6 },
            ["Complication"] = new() { Tension = 0.6, Stakes = 0.6, Mystery = 0.5, Urgency = 0.6, Intimacy = 0.5, Hope = 0.5 },
            ["Revelation"] = new() { Tension = 0.8, Stakes = 0.7, Mystery = 0.2, Urgency = 0.7, Intimacy = 0.6, Hope = 0.7 },
            ["Resolution"] = new() { Tension = 0.2, Stakes = 0.2, Mystery = 0.05, Urgency = 0.1, Intimacy = 0.7, Hope = 0.9 }
        }
    };
}
```

---

## Part 5: Story Action Library

### Action Categories (Parallel to Music's TensionActions, ColorActions, etc.)

```csharp
// ConflictActions - Introduce and escalate opposition
public static class ConflictActions
{
    public static IStoryAction IntroduceAntagonist => new StoryAction
    {
        Id = "introduce_antagonist",
        Preconditions = { ("antagonist.known", false) },
        Effects = { ("antagonist.known", true), ("tension", +0.2) },
        Cost = 1.0,
        NarrativeStateEffect = new() { Tension = +0.15, Stakes = +0.1, Mystery = +0.2 }
    };

    public static IStoryAction EscalateConflict => new StoryAction
    {
        Id = "escalate_conflict",
        Preconditions = { ("conflict.active", true) },
        Effects = { ("tension", +0.15), ("stakes", +0.1) },
        Cost = 1.0,
        NarrativeStateEffect = new() { Tension = +0.15, Stakes = +0.1, Urgency = +0.1 }
    };

    public static IStoryAction BetrayalReveal => new StoryAction
    {
        Id = "betrayal_reveal",
        Preconditions = { ("trusted_character.exists", true), ("betrayal.hidden", true) },
        Effects = { ("betrayal.hidden", false), ("tension", +0.3), ("intimacy", -0.2) },
        Cost = 2.0, // High impact, use sparingly
        NarrativeStateEffect = new() { Tension = +0.25, Stakes = +0.15, Hope = -0.2, Intimacy = -0.15 }
    };
}

// RelationshipActions - Build and modify character bonds
public static class RelationshipActions
{
    public static IStoryAction FormAlliance => new StoryAction
    {
        Id = "form_alliance",
        Preconditions = { ("characters.compatible", true) },
        Effects = { ("alliance.formed", true), ("intimacy", +0.15) },
        Cost = 1.0,
        NarrativeStateEffect = new() { Intimacy = +0.15, Hope = +0.1 }
    };

    public static IStoryAction SharedOrdeal => new StoryAction
    {
        Id = "shared_ordeal",
        Preconditions = { ("danger.present", true), ("characters.together", true) },
        Effects = { ("intimacy", +0.2), ("trust", +0.15) },
        Cost = 1.5,
        NarrativeStateEffect = new() { Intimacy = +0.2, Tension = +0.1, Hope = +0.05 }
    };
}

// MysteryActions - Information revelation and concealment
public static class MysteryActions
{
    public static IStoryAction PlantClue => new StoryAction
    {
        Id = "plant_clue",
        Preconditions = { ("mystery.active", true) },
        Effects = { ("clue.available", true), ("mystery", -0.1) },
        Cost = 0.5,
        NarrativeStateEffect = new() { Mystery = -0.1, Hope = +0.05 }
    };

    public static IStoryAction RevealSecret => new StoryAction
    {
        Id = "reveal_secret",
        Preconditions = { ("secret.hidden", true), ("revelation.justified", true) },
        Effects = { ("secret.hidden", false), ("mystery", -0.2), ("tension", +0.1) },
        Cost = 1.5,
        NarrativeStateEffect = new() { Mystery = -0.2, Tension = +0.1, Stakes = +0.05 }
    };

    public static IStoryAction RedHerring => new StoryAction
    {
        Id = "red_herring",
        Preconditions = { ("mystery.active", true) },
        Effects = { ("false_lead.planted", true) },
        Cost = 0.8,
        NarrativeStateEffect = new() { Mystery = +0.1, Urgency = +0.05 }
    };
}

// ResolutionActions - Move toward story conclusion
// NOTE: Multi-phase effects (spike then resolution) are modeled as atomic action sequences.
// GOAP planner handles sequencing naturally - no special "then" syntax needed.
public static class ResolutionActions
{
    // Confrontation is a two-action sequence: escalation followed by resolution
    public static IStoryAction ConfrontationBegin => new StoryAction
    {
        Id = "confrontation_begin",
        Preconditions = { ("antagonist.known", true), ("protagonist.ready", true) },
        Effects = { ("confrontation.in_progress", true) },
        Cost = 1.5,
        NarrativeStateEffect = new() { Tension = +0.3, Urgency = +0.2 },
        ChainedAction = "confrontation_resolve"  // GOAP will plan this next
    };

    public static IStoryAction ConfrontationResolve => new StoryAction
    {
        Id = "confrontation_resolve",
        Preconditions = { ("confrontation.in_progress", true) },
        Effects = { ("confrontation.in_progress", false), ("confrontation.occurred", true) },
        Cost = 0.5,
        NarrativeStateEffect = new() { Tension = -0.4, Urgency = -0.3 }
    };

    public static IStoryAction Sacrifice => new StoryAction
    {
        Id = "sacrifice",
        Preconditions = { ("character.willing", true), ("sacrifice.meaningful", true) },
        Effects = { ("sacrifice.made", true), ("stakes.resolved", true) },
        Cost = 3.0, // Major story beat
        NarrativeStateEffect = new() { Tension = -0.2, Intimacy = +0.3, Hope = +0.2 }
    };
}
```

---

## Part 6: Integration with Existing Systems

### Reusing lib-behavior GOAP

The storyline-storyteller SDK should reuse the existing GOAP infrastructure:

```csharp
// Storyline planning uses same A* planner with different action space
public class StorylinePlanner
{
    private readonly GOAPPlanner _planner; // From lib-behavior
    private readonly ActionLibrary _storyActions;

    public StorylinePlan Plan(StorylineRequest request, WorldState worldState)
    {
        // Convert NarrativeState targets to GOAP goals
        var goal = GOAPGoal.FromNarrativePhase(request.TargetPhase);

        // Run A* with story actions
        var plan = _planner.CreatePlan(
            worldState,
            goal,
            _storyActions.GetApplicableActions(worldState),
            request.UrgencyTier // Reuse low/medium/high parameters
        );

        return ConvertToStorylinePlan(plan, request);
    }
}
```

### Archive Extraction for Storyline Seeding

```csharp
public class ArchiveExtractor
{
    /// <summary>
    /// Convert compressed character archive to GOAP-compatible WorldState
    /// </summary>
    public WorldState ExtractWorldState(ResourceArchive archive)
    {
        var worldState = new WorldState();

        // Extract from character-base
        var baseData = archive.GetEntry<CharacterCompressData>("character");
        worldState.Set("protagonist.name", baseData.Name);
        worldState.Set("protagonist.species", baseData.Species);
        worldState.Set("protagonist.death_cause", baseData.DeathCause);
        worldState.Set("protagonist.alive", false); // Archived = dead

        // Extract from character-personality
        var personality = archive.GetEntry<PersonalityCompressData>("character-personality");
        if (personality.HasPersonality)
        {
            worldState.Set("protagonist.confrontational", personality.Personality.Confrontational);
            worldState.Set("protagonist.loyal", personality.Personality.Loyal);
            worldState.Set("protagonist.vengeful", personality.Personality.Vengeful);
            // ... map all trait axes
        }

        // Extract from character-history
        var history = archive.GetEntry<HistoryCompressData>("character-history");
        foreach (var participation in history.Participations)
        {
            worldState.Set($"history.{participation.EventCode}.participated", true);
            worldState.Set($"history.{participation.EventCode}.role", participation.Role);
        }
        if (history.HasBackstory)
        {
            worldState.Set("backstory.trauma", history.Backstory.Trauma);
            worldState.Set("backstory.goals", history.Backstory.Goals);
            worldState.Set("backstory.fears", history.Backstory.Fears);
        }

        // Extract from character-encounter
        var encounters = archive.GetEntry<EncounterCompressData>("character-encounter");
        worldState.Set("encounter.count", encounters.EncounterCount);
        worldState.Set("encounter.positive_ratio", CalculatePositiveRatio(encounters));
        foreach (var perspective in encounters.Perspectives.GroupBy(p => p.OtherCharacterId))
        {
            var sentiment = perspective.Average(p => p.SentimentShift);
            worldState.Set($"relationship.{perspective.Key}.sentiment", sentiment);
        }

        return worldState;
    }

    /// <summary>
    /// Identify kernels (essential events) from archive for story seeding
    /// </summary>
    public List<Kernel> ExtractKernels(ResourceArchive archive)
    {
        var kernels = new List<Kernel>();

        // Death is always a kernel for archived characters
        var baseData = archive.GetEntry<CharacterCompressData>("character");
        kernels.Add(new Kernel
        {
            Type = KernelType.Death,
            Significance = 1.0,
            Data = new { Cause = baseData.DeathCause, Location = baseData.DeathLocation }
        });

        // High-significance historical participations are kernels
        var history = archive.GetEntry<HistoryCompressData>("character-history");
        foreach (var p in history.Participations.Where(p => p.Significance > 0.7))
        {
            kernels.Add(new Kernel
            {
                Type = KernelType.HistoricalEvent,
                Significance = p.Significance,
                Data = new { Event = p.EventCode, Role = p.Role }
            });
        }

        // Traumatic backstory elements are kernels
        if (history.HasBackstory && !string.IsNullOrEmpty(history.Backstory.Trauma))
        {
            kernels.Add(new Kernel
            {
                Type = KernelType.Trauma,
                Significance = 0.8,
                Data = new { Trauma = history.Backstory.Trauma }
            });
        }

        // High-impact negative encounters are kernels (potential grudge storylines)
        var encounters = archive.GetEntry<EncounterCompressData>("character-encounter");
        foreach (var e in encounters.Encounters.Where(e =>
            e.Perspectives.Any(p => p.SentimentShift < -0.5 && p.EmotionalImpact > 0.7)))
        {
            kernels.Add(new Kernel
            {
                Type = KernelType.Conflict,
                Significance = 0.85,
                Data = new { Encounter = e }
            });
        }

        return kernels.OrderByDescending(k => k.Significance).ToList();
    }
}
```

---

## Part 7: Academic References

### Primary Sources (Implement These)

| Source | Citation | Resource | What It Provides |
|--------|----------|----------|------------------|
| **Propp** | Propp, V. (1928). *Morphology of the Folktale* | Schema: `propp-functions.yaml` | 31 narrative functions, 7 character types |
| **Story Grid** | Coyne, S. (2015). *The Story Grid* | Schema: `story-grid-genres.yaml` | Genre conventions, obligatory scenes |
| **Save the Cat** | Snyder, B. (2005). *Save the Cat!* | Schema: `save-the-cat-beats.yaml` | 15 beats with timing, genre categories |
| **Emotional Arcs** | Reagan, A. et al. (2016). "The emotional arcs of stories" | Schema: `emotional-arcs.yaml` | Six validated arc shapes |
| **McKee** | McKee, R. (1997). *Story: Substance, Structure, Style* | - | Value-based structure, beat hierarchy |
| **Barthes** | Barthes, R. (1966). "Introduction to the Structural Analysis of Narratives" | - | Kernels vs. satellites |

### Secondary Sources (Reference)

| Source | Citation | Resource | What It Provides |
|--------|----------|----------|------------------|
| **Dramatica/NCP** | Phillips, M. & Huntley, C. (1994). *Dramatica Theory* | Repo: `narrative-context-protocol` | Complete formal model, JSON schemas |
| **Greimas** | Greimas, A.J. (1966). *Structural Semantics* | - | 6 actants model |
| **Chatman** | Chatman, S. (1978). *Story and Discourse* | - | Fabula/sjuzhet distinction |
| **Campbell** | Campbell, J. (1949). *The Hero with a Thousand Faces* | - | Monomyth / Hero's Journey |

### Computational Precedents

| System | Citation | Resource | Lesson |
|--------|----------|----------|--------|
| **Façade/ABL** | Mateas, M. & Stern, A. (2005). "Structuring Content in the Façade Interactive Drama Architecture" | - | Beat-based drama management |
| **Versu/Praxis** | Evans, R. & Short, E. (2013). "Versu—A Simulationist Storytelling System" | Repo: `re-praxis` | Exclusion logic, emergent narrative |
| **Scheherazade** | Li, B. et al. (2013). "Story Generation with Crowdsourced Plot Graphs" | - | Learning narrative patterns |
| **Gervás** | Gervás, P. (2013). "Propp's Morphology of the Folk Tale as a Grammar for Generation" | Repos: `propper`, `propper-narrative` | Propp functions for generation |
| **Core Stories** | Reagan, A. et al. (2016). Emotional arc analysis | Repo: `core-stories` | SVD decomposition, labMT sentiment |
| **Beat Sheet** | Snyder methodology calculator | Repo: `beatsheet` | Beat percentage calculations |

### Additional Computational Tools

| Tool | Repository | Language | Use Case |
|------|------------|----------|----------|
| **BookNLP** | `github.com/booknlp/booknlp` | Python | Character extraction, coreference |
| **Tracery** | `github.com/galaxykate/tracery` | JavaScript | Grammar-based text generation |
| **Ink** | `github.com/inkle/ink` | C# | Branching narrative scripting |
| **AESOP** | Leon et al. (2013) | - | Plot unit extraction |

---

## Part 8: Implementation Phases

### Phase 1: storyline-theory Foundation

1. Define `NarrativeState` (6 dimensions) with presets and interpolation
2. Implement `CoreValue` and `ValueSpectrum` enums
3. Implement Propp's 31 `NarrativeFunction` types
4. Implement Greimas' 6 `ActantRole` types
5. Implement `Kernel` and `Satellite` classification
6. Define `EmotionalArc` enum (6 shapes)
7. Create `GenreConventions` and `ObligatoryScene` structures
8. Build-in genre definitions (Thriller, Mystery, Horror, Romance, Action)

### Phase 2: storyline-storyteller Planning

1. Define `StoryArcTemplate` base class with phases
2. Implement initial templates (RevengeArc, MysteryArc, QuestArc, LegacyArc)
3. Create `IStoryAction` interface with preconditions/effects
4. Implement action libraries (Conflict, Relationship, Mystery, Resolution)
5. Adapt GOAP planner for story action space
6. Implement `NarrativePlanner` orchestrating phase-by-phase planning

### Phase 3: Archive Extraction

1. Implement `ArchiveExtractor` consuming lib-resource archives
2. Implement `BackstoryParser` for character-history data
3. Implement `EncounterAnalyzer` for character-encounter data
4. Implement `PersonalityMapper` for character-personality data
5. Implement `KernelExtractor` identifying essential events
6. Create `WorldState` generation from extracted elements

### Phase 4: Audience & Engagement Models

1. Implement `AudienceModel` tracking genre expectations
2. Implement `EngagementState` tracking active narrative hooks
3. Implement `SatisfactionPredictor` for resolution evaluation
4. Implement `PacingAnalyzer` for arc trajectory validation

### Phase 5: Intent Generation

1. Implement `StorylineIntent` hierarchy
2. Create intent-to-lib-character mapping
3. Create intent-to-lib-actor mapping
4. Create intent-to-lib-contract mapping (quest milestones)
5. Create intent-to-ABML compiler (behavior fragments)

### Phase 6: Continuation System

1. Implement `ContinuationPoint` for lazy phase evaluation
2. Implement `WorldStateDelta` for change detection
3. Implement `AdaptivePlanner` for mid-storyline replanning
4. Integration with lib-behavior continuation points

---

## Part 9: Implementation Insights from Reference Repositories

### Key Patterns Discovered

#### 1. NCP Storyform Structure (from narrative-context-protocol)

The Narrative Context Protocol provides a complete JSON schema for storyforms that we can adapt:

```typescript
// NCP perspective mapping to throughlines
const perspectives = {
  "i": "main_character",      // Internal journey
  "you": "obstacle_character", // Challenge to MC worldview
  "we": "objective_story",    // External plot
  "they": "subjective_story"  // MC-OC relationship
};

// Story dynamics with vectors (-1 or +1)
const dynamics = {
  "driver": { value: "action" | "decision", vector: 1 | -1 },
  "limit": { value: "timelock" | "optionlock", vector: 1 | -1 },
  "outcome": { value: "success" | "failure", vector: 1 | -1 },
  "judgment": { value: "good" | "bad", vector: 1 | -1 }
};
```

**Adoption Strategy**: Use throughline structure for story validation; defer quad algebra to future phases.

#### 2. Exclusion Logic Patterns (from re-praxis)

Re-Praxis demonstrates cardinality-enforced state management:

```csharp
// Pattern: Use cardinality operators for WorldState constraints
// This ensures GOAP planner operates on consistent state

public class NarrativeWorldState : PraxisDatabase
{
    // Character can only have ONE current goal (exclusive)
    public void SetGoal(string character, string goal) =>
        Insert($"character.{character}.goal!{goal}");

    // Character can have MANY relationships (additive)
    public void AddRelationship(string character, string other, string type) =>
        Insert($"character.{character}.relationships.{other}.{type}");

    // Query with pattern matching
    public IEnumerable<string> GetAlliesOf(string character) =>
        Query($"character.{character}.relationships.?ally.ally")
            .Select(r => r["ally"]);
}
```

**Adoption Strategy**: Consider adapting exclusion logic for WorldState to ensure GOAP preconditions work correctly.

#### 3. Three-Act Propp Generation (from propper)

The propper Go implementation shows a compositional approach:

```go
// Generation algorithm (simplified from propper)
func GenerateNarrative(seed int64) []Function {
    rng := rand.New(rand.NewSource(seed))
    narrative := []Function{}

    // Phase 1: Preparation (optional functions 1-7)
    for _, f := range preparationFunctions {
        if rng.Float64() < f.probability {
            narrative = append(narrative, f)
        }
    }

    // Phase 2: Complication (8 mandatory, 9-11 optional)
    narrative = append(narrative, villanyOrLack) // Always required
    // ... add remaining complication functions

    // Phase 3: Return (constraints: wedding requires victory)
    // ... ordered return functions with dependencies

    return narrative
}
```

**Adoption Strategy**: Use probabilistic function selection with seed for reproducibility.

#### 4. Beat Percentage Calculation (from beatsheet)

The beatsheet implementation provides two percentage strategies:

```javascript
// BS2 (Blake Snyder 2) breakpoints
const bs2 = [0, 0.01, 0.05, 0.10, 0.20, 0.22, 0.50, 0.75, 0.80, 0.99, 1.0];

// Fiction (novel) breakpoints - adjusted for longer form
const fiction = [0, 0.01, 0.03, 0.10, 0.20, 0.22, 0.50, 0.68, 0.77, 0.99, 1.0];

function calculateBeat(totalLength, beatIndex, strategy) {
    const breakpoints = strategy === 'bs2' ? bs2 : fiction;
    return Math.floor(totalLength * breakpoints[beatIndex]);
}
```

**Adoption Strategy**: Support multiple timing strategies; default to fiction for longer-form game narratives.

#### 5. SVD Arc Classification (from core-stories)

The core-stories Jupyter notebooks show arc classification via mode coefficients:

```python
# Simplified from core-stories methodology
def classify_arc(sentiment_timeseries):
    # Mean-center the timeseries
    centered = sentiment_timeseries - np.mean(sentiment_timeseries)

    # Project onto SVD modes (pre-computed from corpus)
    mode1_coef = np.dot(centered, MODE1_VECTOR)
    mode2_coef = np.dot(centered, MODE2_VECTOR)

    # Classify by dominant mode
    if abs(mode1_coef) > abs(mode2_coef):
        return "rags_to_riches" if mode1_coef > 0 else "tragedy"
    else:
        return "man_in_hole" if mode2_coef > 0 else "icarus"

    # Complex arcs require examining mode combinations
    # ...
```

**Adoption Strategy**: Pre-compute mode vectors; classify generated storylines to ensure arc compliance.

### Implementation Complexity Assessment

| Component | Complexity | Existing Implementation | Notes |
|-----------|------------|------------------------|-------|
| Propp Functions | Low | `propper` (Go) | Direct port to C# |
| Save the Cat Beats | Low | `beatsheet` (JS) | Simple percentage math |
| Story Grid Genres | Low | Schema only | Configuration data |
| Emotional Arcs | Medium | `core-stories` (Python) | Port SVD classification |
| NCP/Dramatica | High | `narrative-context-protocol` | Selective adoption |
| Exclusion Logic | Medium | `re-praxis` (C#) | Already C#, adapt patterns |
| GOAP Integration | Medium | lib-behavior exists | Reuse with story actions |

**Conclusion**: We have **less work than music-analysis** because:
1. Multiple working implementations exist to reference
2. Narrative structures are more enumerable than music theory
3. We can adopt selectively (skip full Dramatica quad algebra)
4. GOAP infrastructure already exists in lib-behavior

---

## Conclusion

The storyline SDKs have a rich foundation of academic theory to draw from. The key insight is that **narrative theory is as formalized as music theory** - we're not inventing, we're implementing.

The architecture mirrors the music SDKs:
- **storyline-theory**: Pure data structures and mechanics (Propp, Greimas, McKee)
- **storyline-storyteller**: Planning and templates (GOAP, arc templates, genre conventions)
- **lib-storyline**: Service wrapper (caching, persistence, HTTP endpoints)

The existing infrastructure (GOAP planner, compression archives, character ecosystem) provides the foundation. The remaining work is defining the narrative state space, action libraries, and genre configurations - all of which have academic precedent to follow.

### Resource Summary

**Data Schemas** (ready for code generation):
- `schemas/storyline/propp-functions.yaml` - 31 functions, 7 archetypes, preconditions/postconditions
- `schemas/storyline/save-the-cat-beats.yaml` - 15+ beats, 10 genres, Five-Point Finale
- `schemas/storyline/story-grid-genres.yaml` - 12 genres, Five Commandments, Four Core Framework
- `schemas/storyline/emotional-arcs.yaml` - 6 arc shapes, SVD methodology, labMT parameters

**Reference Implementations** (cloned to ~/repos/):
- `narrative-context-protocol` - Dramatica JSON schemas (NCP open source)
- `re-praxis` - C# exclusion logic (Versu state management)
- `propper` / `propper-narrative` - Go/Inform Propp implementations
- `beatsheet` - JavaScript beat percentage calculator
- `core-stories` - Python emotional arc analysis (SVD)

**Research Materials**:
- `~/repos/story-analysis/research/` - 24 files, 4.9 MB of structured research data

### Implementation Readiness

| Component | Data Schema | Reference Impl | Research | Ready |
|-----------|-------------|----------------|----------|-------|
| Propp Functions | ✓ | ✓ (Go) | ✓ | **YES** |
| Save the Cat | ✓ | ✓ (JS) | ✓ | **YES** |
| Story Grid | ✓ | - | ✓ | **YES** |
| Emotional Arcs | ✓ | ✓ (Python) | ✓ | **YES** |
| NCP/Dramatica | - | ✓ (JSON) | ✓ | SELECTIVE |
| Exclusion Logic | - | ✓ (C#) | ✓ | OPTIONAL |
| GOAP Integration | - | ✓ (lib-behavior) | - | **YES** |

**Complexity Assessment**: Less work than music-analysis because existing implementations exist and narrative structures are more enumerable than harmonic theory.

---

*Document Status: RESEARCH COMPLETE, DATA SCHEMAS CREATED - Ready for implementation*
