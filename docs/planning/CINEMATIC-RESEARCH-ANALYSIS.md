# Cinematic Research Analysis: What We Actually Have and What To Build With It

> **Type**: Actionability analysis
> **Inputs**: [CINEMATIC-SYSTEM.md](CINEMATIC-SYSTEM.md), [CINEMATIC-THEORY-RESEARCH.md](CINEMATIC-THEORY-RESEARCH.md), 9 detailed research cards (cinema-analysis/cards/), both vision documents, existing SDK structures
> **Purpose**: Determine which research sources map to which SDK, in which domain, in which combination -- with maximum alignment to the Bannou/Arcadia vision

---

## What the Cards Actually Gave Us

We have 9 detailed research cards representing deep reads of the sources the research document identified. Here's the honest assessment of what each one delivered versus what was expected.

### Card 1: EMOTE Model (Chi et al., SIGGRAPH 2000) -- EXCEEDED EXPECTATIONS

The EMOTE card delivered the complete computational model: four Effort floats (-1 to +1 per axis), three Shape dimensions, and -- critically -- the actual equations mapping abstract Effort parameters to concrete low-level motion parameters (path curvature, timing inflection points, anticipation velocity, overshoot, flourishes). The paper provides the specific constants: `Tval = (-1*ind + 1*f(ind,fre)) + dir`, `ti = 0.5 + 0.4*max(str,sud) - 0.4*max(lgt,sus) + 0.8*f(bnd,lgt)`, etc.

**What this means**: EMOTE is not just "Laban as floats." It is a tested, CMA-validated transformation pipeline that takes keyframe animations and produces expressively modified movement from the same base. The independence property -- Effort/Shape applied independently of geometric movement definition -- is the architectural keystone. It means choreography planning (WHAT happens) and quality styling (HOW it looks) are genuinely separable concerns.

**Validation**: 3 Certified Movement Analysts + 1 consultant. 53-76% correct identification, 0.8-7.7% "opposite" errors. The low opposite rate is the important number -- experts almost never saw the wrong quality.

**Verdict**: TIER 1 for CinematicTheory. The EMOTE transformation pipeline is as foundational to the cinematic SDK as pitch-class sets are to MusicTheory.

### Card 2: Toric Space (Lino & Christie, SIGGRAPH 2015) -- DELIVERED EXACTLY WHAT WAS NEEDED

A compact 3D parameterization `(alpha, theta, phi)` that represents ALL valid camera positions framing two targets. Closed-form algebraic conversion to Cartesian. Visual properties (framing, distance, vantage angle, projected size) map to intervals that prune independently via intersection. Deterministic, time-budget controllable (N samples parameter), with explicit failure detection when constraints are inconsistent.

**What this means**: This is the geometric solver for the most common shot in combat cinematics (two participants). It replaces stochastic search (PSO) with deterministic interval computation. The four screen-space manipulators (position, size, vantage, vertigo/dolly-zoom) fall out naturally. Viewpoint interpolation for smooth camera transitions between composed shots is built into the formalization.

**Performance**: 20-40% faster than PSO, massively better on precise framing (75-100% vs 9-30%), zero standard deviation (deterministic).

**Limitation**: Two-target only. Three+ targets require pairwise decomposition. This is acceptable -- most combat shots frame two participants, and group shots can decompose into pairwise subproblems.

**Verdict**: TIER 1 for CinematicTheory. THE per-frame geometric camera solver.

### Card 3: Virtual Cinematographer / DCCL (He/Cohen/Salesin, SIGGRAPH 1996) -- THE ARCHITECTURE TEMPLATE

The two-tier decomposition (idioms as HFSMs selecting shot types and timing; camera modules as geometric solvers computing placement) is the most important architectural contribution. 16 camera modules implemented (apex, external, internal, track, pan, follow, etc.). Complete idiom implementations for 2-actor conversation, 3-actor conversation, and movement sequences. Hierarchical composition via call/return with exception-based interruption.

**What this means**: The HFSM idiom architecture is the camera direction layer of CinematicTheory. Idioms encode WHAT to shoot and WHEN to cut as finite state machines. Camera modules (enhanced with Toric Space) handle WHERE to put the camera. The system runs each tick with no future knowledge -- directly applicable to game runtime.

**Key encoded rules**: Don't cross the line of interest, avoid jump cuts (same-module transitions merge into single shots), use establishing shots before detail, enforce minimum shot duration, reaction shots via timed transitions.

**Critical insight from the card**: HFSM, not GOAP, for camera control. The card explicitly argues that camera direction is a different problem from choreography planning and needs a different solver. Fast state lookup + condition testing vs. search. Predictable. Composable. Stylistic (different idiom implementations = different directorial styles).

**Verdict**: TIER 1 for CinematicTheory. The camera direction architecture. Integrate Toric Space as the geometric solver inside the camera module implementations.

### Card 4: Facade (Mateas & Stern, AIIDE 2005) -- THE COMPOSITION TEMPLATE

The most detailed card (24K). The beat sequencing / drama manager architecture maps one-to-one to CinematicStoryteller's choreographic exchange selector. The key algorithm: filter eligible beats by preconditions, score against target tension arc, modulate with weights and priorities, greedy-select. This IS the core of CinematicStoryteller.

**What this actually delivers beyond the algorithm**:

1. **Tone matching > causal linking** for perceived coherence. If consecutive choreographic phrases are at the same emotional intensity (matching the current point on the tension arc), they will feel narratively connected even without explicit causal dependency. This dramatically reduces annotation burden.

2. **The gist point concept**. Each choreographic phrase has a point where its dramatic information has been delivered. If interrupted before the gist point, replay compressed. After, interruption is fine. Directly applicable to combat exchanges interrupted by player QTE input or opponent disruption.

3. **Causal independence is a feature**. Choreographic exchanges should be order-independent so the fight director can sequence them based on tension arc fit. Only structurally necessary ordering (setup before reversal, escalation before climax) needs preconditions.

4. **25% content utilization rate is expected and correct**. A library of choreographic exchanges where any given fight uses a fraction is not waste -- it is the cost of responsiveness and replay value.

5. **Greedy selection is correct for interactive contexts**. Combat is volatile. Plan one exchange ahead, not the entire fight.

6. **Three parallel state channels** (momentum, escalation, revelation) feeding into a single tension scalar for scoring. Maps to Facade's three social games (affinity, hot-button, therapy).

**Verdict**: TIER 1 for CinematicStoryteller. The direct architectural template.

### Card 5: Film Editing Patterns (Wu/Christie et al., 2018) -- THE EDITING GRAMMAR

A formal language for constraints BETWEEN consecutive shots. Three layers: framing properties (per-shot), shot relations (between consecutive shots), and sequence structure (length constraints, embedded sub-sequences). Five validated stylistic patterns: Same-size, Intensify, Opposition, Frameshare, Shot-Reverse-Shot. The constraint propagation solver filters candidate framings, not generates them.

**What this means**: FEP is the grammar layer between individual shots (Toric Space geometry) and shot sequences (DCCL idioms). Given a set of valid shots for each beat, FEP prunes to the subset that forms coherent editing sequences. The five patterns cover >50% of professional editing practice.

**Key finding**: Intensify (gradually closer shots) is the combat escalation pattern formalized as a constraint. Opposition (opposite horizontal regions) encodes conflict visually. Both are directly implementable in CinematicTheory.

**Integration**: FEP explicitly uses Toric Space as its geometric solver. These two papers are designed to work together.

**Verdict**: TIER 2 for CinematicTheory. Implement after Toric Space and DCCL idioms are working; FEP adds the editing grammar on top.

### Card 6: Murch / In the Blink of an Eye (2001) -- DOMAIN KNOWLEDGE EXTRACTION

The Rule of Six is a directly implementable weighted scoring function for cut decisions: Emotion 51%, Story 23%, Rhythm 10%, Eye trace 7%, 2D planarity 5%, 3D continuity 4%. The masking property: satisfying higher criteria makes the audience unaware of lower-criteria problems.

**What else the card delivered**:

1. **Blink theory as cut timing basis**. Cut rates should match the audience's expected blink rate for the current emotional intensity. Fight sequences: 15-25 cuts/min. Calm dialogue: 4-8. The blink rate tracks emotional intensity, not action rate.

2. **Branch points, not continuous timelines**. Every shot has discrete cut points. You cannot cut between branches -- the result feels "too long or not long enough." This constrains how CinematicTheory should enumerate candidate cuts.

3. **The Dragnet anti-pattern**. Don't always frame the active entity. Sometimes the most interesting information is on the entity REACTING, not acting. In combat: show the defender's face during the attacker's cue phase.

4. **Detail-density tempo scaling**. Visual detail level affects appropriate cut timing. Simpler rendering should use longer shot durations. CinematicTheory should expose a fidelity coefficient that clients use to scale.

5. **Referred pain diagnostic**. If a sequence feels wrong at beat N, the fix may be at beat N-3. The symptom point and the error point are often different.

**Verdict**: TIER 2 for CinematicTheory. Rule of Six replaces DCCL's timing heuristics with a principled scoring function. Blink theory provides cut frequency targets. Branch points constrain cut candidate enumeration. All directly implementable but dependent on the DCCL idiom architecture being in place.

### Card 7: Reagan Emotional Arcs (2016) -- ALREADY INTEGRATED, REUSE

Six arc shapes extracted via SVD from 1,327 stories. Mode 1 (simple rise or fall) explains 75.6% of variance. Man in a Hole (fall then rise) is the most common narrative shape and maps to Jackie Chan Principle 1 ("Start with a DISADVANTAGE"). Arcs are linearly combinable as basis functions.

**What the card adds beyond the research doc**: Derivative metrics for real-time use (Hope = direct sentiment value, Tension = derivative/slope, Stakes = distance from neutral) mapping to PAD dimensions (Hope ~ Valence, Tension ~ Arousal, Stakes ~ intensity modifier). And the practical note that 200-point resolution is overkill for a fight -- 20-40 points suffices.

**Verdict**: TIER 2 for CinematicStoryteller. Already in StorylineTheory. Reuse the same arc shapes with combat-domain interpretation. The GOAP planner uses the arc as a target curve for beat scoring (per Facade's algorithm).

### Card 8: Resonator Model (Brown & Tu, 2021) -- THE CONSTRAINT GENERATOR

Once fight start-valence and end-valence are fixed, internal structure is constrained to oscillate between rises and falls. Four basic shapes (N, V, Lambda, И) from a 2x2 crossing of initial shift x final shift. Harmonic order (integer >= 1) scales complexity: more oscillations = more reversals = longer fights.

**What the card delivered beyond the research doc**:

1. **Hypershifts as planning units**. Paired rise-fall (Lambda) or fall-rise (V) segments are the building blocks for harmonic expansion. Each represents a protagonist attempt + outcome. This is the natural GOAP planning unit for CinematicStoryteller.

2. **Intensification principle**. In longer narratives, shifts start small and become progressively larger. Early oscillations are gentle; later oscillations are dramatic. This produces natural escalation without explicit rules -- it emerges from the constraint + intensification principle.

3. **Clean nesting with Bordwell and L4D Director**. Resonator = macro (overall fight shape, endpoint constraints). Bordwell pause-burst-pause = meso (rhythm within each shift). C.R.A. = micro (individual exchanges within each burst). L4D Director FSM = internal dynamics of a single shift (Build-Up/Peak/Relax).

4. **The four shapes map exactly to Reagan's first four arcs**. Reagan's "Cinderella" and "Oedipus" are second harmonics. The Resonator model explains WHY these six shapes exist and provides the generation mechanism.

**Verdict**: TIER 1 for CinematicStoryteller. The Resonator model + Reagan arcs together give us: arc shape selection (from dramatic context), endpoint constraint generation, harmonic complexity scaling, and the hypershift as GOAP planning unit. This is the dramatic planning backbone.

### Card 9: Darshak / Cinematic Visual Discourse (Jhala & Young, 2010) -- RULE EXTRACTION, NOT ARCHITECTURE

The three-layer operator hierarchy (dramatic pattern -> film idiom -> primitive shot) with typed parameters, preconditions, effects, temporal constraints, and decomposition recipes. The belief-state model: camera actions modeled as operating on the viewer's beliefs `(BEL V (condition))`, `(infocus ?entity)`, `(visible ?entity)`.

**What to extract**: The operator schema as a data model for a film rule library. Belief-state effects separating geometry from communication. Focus continuity as the fundamental constraint (infocus must hold before tracking). Temporal binding (camera actions synchronized with events they film). Protection intervals (don't break focus chains between setup and payoff). Polti's 36 dramatic situations as parameterized templates for combat scenarios.

**What to skip**: The DPOCL-T planner (we have GOAP). ConstraintCam (superseded by Toric Space).

**Critical validation finding**: Intentional shot composition measurably improves narrative comprehension over naive approaches. A master shot (single static camera) fails to communicate causal chains. This validates the entire premise of the cinematic SDK.

**Verdict**: TIER 2 for CinematicTheory. Extract the rule encodings and belief-state model. Encode focus continuity, temporal binding, and protection intervals as constraints within the DCCL idiom framework. Skip the planner architecture entirely.

---

## Sources NOT in the Cards (Referenced in Research Doc, Unread)

These were cited in the research document but we don't have detailed analysis cards:

| Source | What We Know | Assessment |
|--------|-------------|------------|
| **Ronfard 2021** (Eurographics STAR) | Organizing taxonomy (Decoupage/Mise-en-scene/Montage x Procedural/Declarative/Optimization/Learning) | Useful vocabulary but not computable. Read for framing, don't implement from. |
| **SAFD / Weapons of Choice** | C.R.A. pattern, distance categories, 7-move rule, notation system | Critical practitioner knowledge but no card. The research doc captured enough for the C.R.A. struct design. A deep read would enrich the move library. |
| **Bordwell "Planet Hong Kong"** | Pause-burst-pause formal analysis | Captured well enough in the research doc synthesis. The pattern is simple and well-understood. |
| **Booth "L4D AI Director" 2009** | Build-Up/Peak/Relax FSM | Captured well enough in the research doc. A GDC talk, not a paper. The 3-state FSM is the entirety of what's extractable. |
| **Cohn VNG 2013** | E-I-L-P-R constituency grammar | No card. The research doc's synthesis is adequate. See the analysis below for why VNG is validation-only, not generation. |
| **Cutting et al. 2016** | Three pacing channels (shot duration, flicker motion, luminance) | Useful empirical data but secondary. The key finding (shot duration decreases toward climax) is already encoded in the Resonator model's intensification principle. |
| **Constrained Hypertubes** (Christie/Languenon) | Camera path planning | No card. Potentially useful for complex camera movements (orbital shots, crane) but Toric Space handles 80%+ of combat camera needs. Defer. |
| **PSL** (Ronfard et al.) | Prose Storyboard Language | No card. Potentially useful as intermediate representation but ABML DomainActions already serve this role. Redundant. |
| **Eisenstein** | Five montage types | Metric and Rhythmic are already captured in Cutting/Murch/Bordwell. Tonal maps to PAD. Intellectual remains unformalizable. |

---

## The Synthesis: What Goes Where

### CinematicTheory (Pure Computation SDK)

Analogous to MusicTheory. No service dependencies. The formal grammar of choreographic composition.

**Structure** (following music-theory pattern):

```
sdks/cinematic-theory/
├── Movement/           # Laban Effort/Shape (EMOTE model)
│   ├── EffortProfile.cs       # Four-float effort parameters
│   ├── ShapeProfile.cs        # Three-dimension shape parameters
│   ├── EffortTransform.cs     # EMOTE equations: Effort -> motion parameters
│   └── NamedEfforts.cs        # Eight named efforts (Punch, Slash, Press, etc.)
│
├── Choreography/       # C.R.A. pattern + frame data + distance
│   ├── Exchange.cs            # Cue-Reaction-Action atomic unit
│   ├── DistanceState.cs       # Out/Long/Medium/Close state machine
│   ├── FrameData.cs           # Startup/active/recovery timing
│   ├── Phrase.cs              # 3-7 exchanges (Seven-Move Rule)
│   └── MoveLibrary.cs         # Parameterized move vocabulary
│
├── Camera/             # Toric Space + DCCL idioms + FEP + Murch
│   ├── Toric/
│   │   ├── ToricSpace.cs          # Core (alpha, theta, phi) parameterization
│   │   ├── IntervalPruner.cs      # Visual property interval computation
│   │   ├── ViewpointSearch.cs     # N-sample search with time budget
│   │   └── ViewpointInterpolation.cs  # Smooth camera transitions
│   ├── Modules/
│   │   ├── ICameraModule.cs       # Interface (actors -> camera placement)
│   │   ├── ApexModule.cs          # Between-actors framing
│   │   ├── ExternalModule.cs      # Over-shoulder (the workhorse)
│   │   ├── InternalModule.cs      # Single-subject close
│   │   ├── TrackModule.cs         # Moving with subject
│   │   └── FollowModule.cs        # Following from behind
│   ├── Idioms/
│   │   ├── IFilmIdiom.cs          # HFSM interface
│   │   ├── DuelIdiom.cs           # Two-combatant fight
│   │   ├── GroupFightIdiom.cs     # Multi-combatant (nests DuelIdiom)
│   │   ├── ChaseIdiom.cs          # Pursuit sequence
│   │   └── AmbushIdiom.cs         # Surprise attack
│   ├── Editing/
│   │   ├── MurchScorer.cs         # Rule of Six weighted scoring
│   │   ├── BlinkRateTarget.cs     # Emotional intensity -> cut frequency
│   │   ├── BranchPoint.cs         # Discrete cut candidate
│   │   └── EditingPattern.cs      # FEP five patterns as constraints
│   └── Rules/
│       ├── FocusContinuity.cs     # Darshak's infocus precondition
│       ├── TemporalBinding.cs     # Camera sync with events
│       └── ProtectionInterval.cs  # Setup-payoff focus chain
│
├── Drama/              # Tension curves + affect tracking
│   ├── ReaganArc.cs           # Six arc shapes as basis functions
│   ├── ResonatorConstraint.cs # Endpoint -> valid shapes + harmonics
│   ├── PadAffect.cs           # Pleasure/Arousal/Dominance state
│   └── IntensityFsm.cs        # L4D Director Build-Up/Peak/Relax
│
└── Output/             # ABML document generation
    ├── DomainActionBuilder.cs  # Produce DomainAction sequences
    └── CinematicDocument.cs    # Complete ABML cinematic document
```

**What each source contributes to CinematicTheory**:

| Source | Domain | What It Provides | Confidence |
|--------|--------|-----------------|------------|
| EMOTE (Chi 2000) | Movement/ | Effort/Shape transformation pipeline, equations, constants | Very High -- complete computational model with validation |
| Toric Space (Lino 2015) | Camera/Toric/ | Two-target geometric solver, interval pruning, interpolation | Very High -- closed-form algebra, deterministic |
| DCCL (He 1996) | Camera/Modules/ + Camera/Idioms/ | Camera module architecture, HFSM idiom pattern | Very High -- complete implemented system |
| FEP (Wu 2018) | Camera/Editing/ | Five editing patterns as shot-to-shot constraints | High -- validated against 1018-shot database |
| Murch (2001) | Camera/Editing/ | Rule of Six scoring, blink-rate targets, branch points | High -- decades of practitioner wisdom, directly encodable |
| Darshak (Jhala 2010) | Camera/Rules/ | Focus continuity, temporal binding, protection intervals, belief-state model | Medium-High -- extract rules, skip planner |
| Reagan (2016) | Drama/ | Six arc shapes as basis functions | High -- already in StorylineTheory, validated |
| Resonator (Brown 2021) | Drama/ | Endpoint constraint -> valid shapes, harmonic complexity | High -- explains WHY Reagan arcs exist |
| L4D Director (Booth 2009) | Drama/ | Build-Up/Peak/Relax intensity FSM | High -- simple, proven in shipped product |
| SAFD / Stage Combat | Choreography/ | C.R.A. pattern, distance categories, 7-move rule | Medium -- practitioner knowledge, not academic formalization |
| Bordwell (2000) | Choreography/ | Pause-burst-pause phrase rhythm | Medium -- well-understood but not formally validated in the same way |
| Frame Data (FGC) | Choreography/ | Startup/active/recovery timing model | High -- universally adopted in fighting games |

### CinematicStoryteller (GOAP-Driven Composition SDK)

Analogous to MusicStoryteller. Depends on CinematicTheory only. Uses GOAP planning to compose cinematics from encounter context.

**Structure** (following music-storyteller pattern):

```
sdks/cinematic-storyteller/
├── Planning/           # GOAP action definitions
│   ├── ChoreographyPlanner.cs    # GOAP planner for fight composition
│   ├── CameraPlanner.cs          # NOT GOAP -- HFSM idiom orchestrator
│   └── Actions/
│       ├── ComposeExchange.cs         # C.R.A. exchange selection
│       ├── InsertEnvironmental.cs     # Environmental interaction
│       ├── AddDramaticPause.cs        # Tension-building beat
│       ├── InjectQteWindow.cs         # Player interaction moment
│       ├── ComposeGroupAction.cs      # Multi-participant coordination
│       └── TriggerContinuation.cs     # Continuation point marking
│
├── State/              # World state for planning
│   ├── FightState.cs              # Current fight state
│   ├── ParticipantState.cs        # Per-combatant state (PAD, momentum)
│   ├── ThreeChannels.cs           # Momentum/Escalation/Revelation
│   └── EnvironmentState.cs        # Spatial affordances
│
├── Sequencing/         # Facade-style beat sequencing
│   ├── ExchangeSequencer.cs       # Drama manager (scores against arc)
│   ├── TensionScorer.cs           # Score exchange against target curve
│   ├── GistPoint.cs               # Interruption management
│   └── ToneMatching.cs            # Perceived coherence via intensity match
│
├── Arcs/               # Target tension curve management
│   ├── ArcSelector.cs             # Dramatic context -> arc shape
│   ├── HypershiftComposer.cs      # Resonator model hypershift planning
│   └── HarmonicScaler.cs          # Complexity parameter -> shift count
│
├── Agency/             # QTE density computation
│   ├── FidelityMapper.cs          # Spirit fidelity -> interaction density
│   └── ContinuationActivator.cs   # Decide which points become interactive
│
└── Composition/        # Final output assembly
    ├── FightComposer.cs           # Orchestrates all layers
    └── AbmlSerializer.cs          # Produces ABML document
```

**What each source contributes to CinematicStoryteller**:

| Source | Domain | What It Provides | Confidence |
|--------|--------|-----------------|------------|
| Facade (Mateas 2005) | Sequencing/ | Beat sequencing algorithm, tone matching, gist points, greedy selection, three parallel channels | Very High -- the direct template |
| Reagan (2016) | Arcs/ | Arc shape selection for target tension curves | High -- already proven in StorylineTheory |
| Resonator (Brown 2021) | Arcs/ | Hypershift as planning unit, harmonic complexity scaling, endpoint constraint | High -- formal constraint generation |
| L4D Director (Booth 2009) | Sequencing/ | Within-shift intensity FSM (Build-Up/Peak/Relax) | High -- proven in shipped product |
| EMOTE (Chi 2000) | (via CinematicTheory) | Quality styling of planned exchanges | Very High -- separate transform pass |
| Darshak (Jhala 2010) | (via CinematicTheory) | Polti dramatic situation templates as encounter type classification | Medium -- useful vocabulary, not directly generative |
| SAFD / Stage Combat | Planning/Actions/ | C.R.A. as the atomic action, exchange structure | Medium -- practitioner knowledge |

---

## The Critical Design Decisions

### Decision 1: HFSM for Camera, GOAP for Choreography

The Virtual Cinematographer card makes this explicit: camera direction is a reactive, tick-by-tick problem (what to show NOW given what's happening). Choreography planning is a compositional problem (what SHOULD happen over the next phrase to match the tension arc). Different problems, different solvers.

- **CinematicStoryteller's GOAP planner**: composes choreographic exchanges (WHAT happens)
- **CinematicTheory's HFSM idioms**: determine camera behavior (HOW it's shown)
- The two run in parallel. The GOAP planner produces a sequence of C.R.A. exchanges. The HFSM idiom reacts to those exchanges as events, making camera decisions each tick.

This maps exactly to how the existing behavior system works: DocumentExecutor runs the behavior GOAP, the Actor runtime reacts per-tick.

### Decision 2: Three Genuinely Independent Layers

EMOTE's independence property is the architectural key: the same base choreography, styled differently, looks completely different. This gives us three composable, independently testable layers:

1. **Structure** (CinematicStoryteller): WHAT happens. Exchanges, environmental interactions, reversals. Output: a sequence of C.R.A. patterns with distance states and structural timing.

2. **Quality** (CinematicTheory Movement/): HOW it happens. Effort/Shape parameters per character per beat, driven by personality and dramatic context. Applied as a transformation on the structural output.

3. **Presentation** (CinematicTheory Camera/): HOW it's shown. HFSM idiom selection, Toric Space geometry, Murch cut scoring, FEP editing patterns. Runs reactively alongside execution.

This is the same three-tier architecture that Bares/Lester (1998) described (narrative planning, character direction, camera computation) and that EMOTE's own design embodies (geometry independent of expression). It's also what the Bannou vision demands: the GOAP planner handles encounter orchestration, the Theory SDK handles formal grammar, and the camera system presents it cinematically.

### Decision 3: Cohn VNG Is Validation-Only, Not Generation

The research document suggested VNG (E-I-L-P-R constituency grammar) as a micro-level structural layer. After reviewing all the cards, the conclusion is that VNG is better used as a **post-hoc validation grammar** rather than a generation constraint.

**Why**: The GOAP planner + Facade sequencing + Reagan arc already implicitly produce E-I-L-P-R structure. Scoring exchanges against a Man-in-a-Hole arc naturally produces setup beats (E), initiating exchanges (I), extending combat (L), the climactic reversal (P), and aftermath (R). Adding VNG as an explicit generation constraint creates competing constraints with the tension arc -- the planner would need to satisfy both "match the tension curve" AND "produce valid VNG constituency," and these can conflict.

**Better use**: After the GOAP planner produces a sequence, validate it against VNG rules. "Does this sequence have a recognizable dramatic constituency?" If not, flag for replanning. This is a quality check, not a generation driver.

This also applies to Mandler-Johnson story grammar and Propp's morphology -- they're taxonomic/analytic frameworks, not generative ones. StorylineTheory already uses them appropriately. CinematicTheory doesn't need them directly.

### Decision 4: Skip PSL, Skip Constrained Hypertubes, Skip Full Darshak Planning

Three things the research document suggested that the card analysis shows we don't need:

1. **Prose Storyboard Language**: ABML DomainActions already serve as the intermediate representation. Adding PSL creates a second IR between Theory and ABML. Not worth the complexity.

2. **Constrained Hypertubes**: Toric Space + DCCL idiom transitions handle 80%+ of combat camera needs. Hypertubes would handle orbital shots and crane movements, but those are rare in combat cinematics. Defer to post-MVP.

3. **Darshak's full POCL planner**: Extract the rule encodings (focus continuity, temporal binding, protection intervals, Polti patterns). Encode them as constraints within the DCCL HFSM framework. The rules transfer; the planner architecture does not.

### Decision 5: The Resonator Model + Reagan Arcs Together, Not Separately

The Resonator card revealed that the two models are mathematically equivalent at low harmonic orders. Reagan identifies shapes empirically; Resonator explains WHY those shapes exist and provides the generation mechanism.

**For CinematicStoryteller**: Use Reagan's arc labels as the user-facing vocabulary (Man in a Hole, Icarus, etc.) but use the Resonator's `(start_valence, end_valence, harmonic_order)` as the generation parameters. The dramatic context provides start_valence and end_valence; the encounter stakes/duration determine harmonic_order. This is clean, formally grounded, and produces the right arc shape automatically.

### Decision 6: Facade's Greedy Selection Over Global GOAP Optimization

The Facade card's analysis makes this clear: combat is too volatile for globally optimal sequence planning. The opponent can disrupt any plan at any moment. Player QTE inputs create branching. Environmental events are unpredictable.

**The right approach**: Plan one exchange (hypershift) ahead using GOAP, not the entire fight. The sequencer scores the next exchange against the target arc, selects greedily, executes, then replans. This is exactly Facade's algorithm with C.R.A. exchanges replacing jdbs.

The GOAP planner still provides value -- it searches the space of valid next-exchanges given preconditions (distance state, participant state, available moves). But it operates at the phrase horizon (3-7 exchanges), not the fight horizon.

---

## Alignment with Vision

### North Star 1: Living Game Worlds

The cinematic system makes NPC combat encounters feel authored even though they're procedurally composed. An Event Brain detects an encounter, calls `/cinematic/compose`, CinematicStoryteller plans a choreographic sequence that respects participant personalities (Effort profiles from `${personality.*}` and `${combat.*}`), and the runtime executes it with camera direction that follows film conventions. The world has genuine cinematic combat that was simulated, not authored.

### North Star 5: Emergent Over Authored

The entire system is emergent. The GOAP planner composes from a library of C.R.A. exchanges. The Effort/Shape transformation makes each character's fighting style unique based on personality data. The camera HFSM reacts to the choreography as it unfolds. No fight is scripted. The same two characters fighting in different locations with different emotional states produce different cinematics because the spatial affordances, tension arc context, and dramatic weight all differ.

### North Star 6: GOAP Is The Universal Planner

The choreography composition layer uses GOAP, consistent with NPC behavior, narrative generation, music composition. But the camera direction layer uses HFSM, which is the right tool for that specific problem. This is not a contradiction -- GOAP is the universal planner for compositional/strategic decisions; HFSM is a reactive control structure for real-time presentation decisions. The behavior-compiler already uses both (GOAP for planning, BehaviorModelInterpreter for execution).

### Progressive Agency Connection

The Facade card's three-channel state model (momentum, escalation, revelation) maps cleanly to the progressive agency gradient. The fidelity value determines which channels the player can influence:

- **Low fidelity**: Player observes all three channels via cinematic (same fight, no interaction)
- **Medium fidelity**: Player can influence momentum (approach/retreat) via QTE windows
- **High fidelity**: Player can influence all channels (stance, timing, combo direction)

The GOAP planner plans the same fight regardless. The fidelity value only determines how many continuation points become interactive. This is exactly the CINEMATIC-SYSTEM.md design, now grounded in Facade's formally described architecture.

### The Content Flywheel

Cinematic fights generate content. An epic encounter becomes a character encounter memory (lib-character-encounter). A dramatic reversal becomes a character history event (lib-character-history). A realm-shaking battle becomes realm history (lib-realm-history). All of this feeds the Storyline service's compressed archives, which feed future narrative generation. The cinematic system doesn't just display combat -- it produces the experiential data that the flywheel consumes.

---

## Implementation Priority (Revised from Research Analysis)

### Phase 1: CinematicTheory Core (Minimum Viable SDK)

Build the three foundational layers that everything else depends on:

1. **Movement/EffortProfile + EffortTransform** -- EMOTE model equations. Pure math, highly testable.
2. **Choreography/Exchange + DistanceState + FrameData** -- C.R.A. struct with distance state machine and timing.
3. **Camera/Toric/** -- Toric Space geometric solver. Closed-form algebra.
4. **Camera/Modules/** -- Port DCCL camera modules (apex, external, internal at minimum), using Toric Space internally.
5. **Drama/ResonatorConstraint + ReaganArc** -- Arc shape generation from endpoint constraints.

**Deliverable**: SDK that can take two participant positions + an arc shape and produce: (a) a sequence of C.R.A. exchanges with Effort-styled parameters, (b) camera positions for each beat via Toric Space + camera modules. Unit-testable in isolation.

### Phase 2: CinematicTheory Camera Direction + CinematicStoryteller Core

6. **Camera/Idioms/DuelIdiom** -- HFSM for two-combatant camera direction. Uses modules from Phase 1.
7. **Camera/Editing/MurchScorer** -- Rule of Six cut scoring replacing DCCL timing heuristics.
8. **Sequencing/ExchangeSequencer** -- Facade-style drama manager scoring exchanges against arc.
9. **Arcs/HypershiftComposer** -- Resonator model hypershift planning.
10. **Planning/ChoreographyPlanner** -- GOAP action definitions for exchange composition.

**Deliverable**: SDK that can take encounter context (participants, environment, dramatic context) and produce a complete ABML cinematic document with camera direction.

### Phase 3: Polish + Integration

11. **Camera/Editing/EditingPattern** -- FEP five patterns as shot-to-shot constraints.
12. **Agency/FidelityMapper** -- QTE density from spirit fidelity.
13. **Camera/Idioms/GroupFightIdiom** -- Multi-combatant camera (nests DuelIdiom).
14. **Camera/Rules/** -- Darshak rule extractions (focus continuity, temporal binding, protection intervals).
15. **lib-cinematic plugin** -- Thin API wrapper following established pattern.

---

## Sources We Still Need But Don't Have Cards For

| Source | What We Need From It | Priority |
|--------|---------------------|----------|
| **SAFD "Weapons of Choice"** | Complete C.R.A. notation system, phrase structure taxonomy, specific move vocabulary | High -- enriches the Exchange/Phrase models and MoveLibrary |
| **Bordwell "Planet Hong Kong"** | Formal pause-burst-pause analysis details, specific rhythm metrics | Medium -- the research doc captured the pattern, but detailed metrics would improve Phrase rhythm generation |
| **Booth "L4D AI Director" GDC talk** | Implementation details of the FSM thresholds, decay rates, aggregate intensity computation | Medium -- a short GDC talk, probably 30 min of watching for concrete parameters |

Everything else cited in the research document either has a card or is adequately captured in the research doc synthesis.

---

## Final Assessment

The research compiled in CINEMATIC-THEORY-RESEARCH.md was well-targeted. Of the 20 sources in the original priority list, the 9 we have cards for cover all TIER 1 and most TIER 2 sources. The analysis confirms the four-layer architecture from the research doc (Movement Vocabulary, Beat Composition, Camera Direction, Dramatic Planning) but refines the implementation approach:

- **Camera direction uses HFSM, not GOAP** (confirmed by Virtual Cinematographer + Darshak analysis)
- **VNG is validation-only, not generation** (resolved the competing-constraints concern)
- **Resonator + Reagan are unified, not layered** (same model at different abstraction levels)
- **Facade's greedy sequencing is correct for combat** (not global optimization)
- **Three independent layers** (Structure/Quality/Presentation) confirmed by EMOTE's independence property

The SDK pair (CinematicTheory + CinematicStoryteller) can be built from the sources we have. No additional research is blocking implementation.

---

*This document is the actionability analysis for the cinematic system's formal foundations. For the architectural plan, see [CINEMATIC-SYSTEM.md](CINEMATIC-SYSTEM.md). For the raw research compilation, see [CINEMATIC-THEORY-RESEARCH.md](CINEMATIC-THEORY-RESEARCH.md).*
