# Cinematic Theory Research: Formal Foundations for CinematicTheory SDK

> **Type**: Research compilation
> **Purpose**: Formal theory and computational models that CinematicTheory can borrow from
> **Related**: [CINEMATIC-SYSTEM.md](CINEMATIC-SYSTEM.md)

---

## How This Document Is Organized

Three domains of formal theory converge on what a CinematicTheory SDK needs:

1. **Computational Cinematography** -- camera placement, shot selection, editing grammar
2. **Fight Choreography** -- movement vocabulary, beat composition, dramatic arc within combat
3. **Dramatic Grammar** -- tension curves, pacing models, narrative constituency grammars

Each section summarizes the most implementable findings. The synthesis at the end maps them to CinematicTheory's layered architecture.

---

## Part 1: Computational Cinematography

### 1.1 Ronfard's Directing Taxonomy (THE Organizing Framework)

Remi Ronfard's Eurographics 2021 STAR survey classifies all computational film directing along two axes:

**Directing Tasks**:
- **Decoupage**: Shot decomposition -- deciding how to break a scene into shots
- **Mise-en-scene**: Staging -- actor and camera placement within each shot
- **Montage**: Editing -- assembling shots into sequences with transitions

**Methodologies**: Procedural (rule-based, FSM), Declarative (constraint-based), Optimization (search/planning), Learning (data-driven, RL, neural)

Each cell in this 3x4 matrix is a distinct computational problem with known solution approaches. This taxonomy maps directly to how CinematicTheory should decompose its internal architecture.

> Ronfard. "Film Directing for Computer Games and Animation." Computer Graphics Forum (Eurographics 2021 STAR), 40(2), pp. 713-730.

### 1.2 The Virtual Cinematographer / DCCL (Real-Time Shot Selection)

He, Cohen, and Salesin (SIGGRAPH 1996) formalized 16 film textbook idioms as **hierarchical finite state machines (HFSMs)** -- small FSMs organized as a directed graph with call/return. Each idiom captures a specific scene type (three actors conversing, one actor traversing, etc.). Low-level **camera modules** (shared across idioms) handle geometric placement for each shot type.

**Why it matters**: This is the most directly relevant foundational model for real-time game cinematography. Idioms are composable FSMs. Camera modules are reusable placement solvers. The two-tier architecture (idiom selection + geometric solving) maps perfectly to CinematicTheory's needs.

> He, Cohen, Salesin. "The Virtual Cinematographer." SIGGRAPH 1996.
> Christianson et al. "Declarative Camera Control for Automatic Cinematography." AAAI 1996.

### 1.3 Toric Space (Two-Target Camera Solving)

Lino and Christie (SIGGRAPH 2015) parameterized camera viewpoint space for two-target framing onto a torus. Visual properties (size, vantage angle, visibility, on-screen position) become intervals in toric coordinates. An interval-based search finds valid viewpoints within this compact representation.

**Why it matters**: Most combat shots involve two participants. Toric space reduces the camera search space dramatically, enabling real-time computation and smooth interpolation. The most efficient geometric solver for the most common combat camera need.

> Lino, Christie. "Intuitive and Efficient Camera Control with the Toric Space." ACM TOG (SIGGRAPH 2015).

### 1.4 Constrained Hypertubes (Camera Path Planning)

Christie and Languenon decompose camera paths into **hypertubes** -- elementary movements based on established cinematographic techniques (traveling, arcing, etc.). Hypertubes connect via smooth transition relations. Camera properties are expressed as interval constraints solved via interval consistency techniques.

**Why it matters**: The hypertube is the unit of cinematographic camera motion, analogous to a "phrase" in movement. Composing hypertubes is composing camera choreography.

> Christie, Languenon. "Modeling Camera Control with Constrained Hypertubes." CP 2002.

### 1.5 Constraint Satisfaction vs. Frame Coherence (The Game Camera Problem)

Bourne and Sattar formalized the fundamental trade-off in game cameras: constraint satisfaction (getting the "right" shot) vs. frame coherence (not making the player sick). Camera control is an over-constrained CSP with explicit constraints for distance, height, occlusion, and frame coherence. Local search strategies solve it in real-time.

**Why it matters**: This is the practical formalization that every game camera system must address. The explicit modeling of the constraint-vs-coherence trade-off is directly implementable.

> Halper, Burry. "A Camera Engine for Computer Games." Computer Graphics Forum, 2001.
> Bourne, Sattar. "Applying Constraint Satisfaction Techniques to 3D Camera Control." 2004.

### 1.6 Prose Storyboard Language (Shot Description Grammar)

Ronfard et al. developed PSL -- a formal EBNF grammar for describing movies shot by shot. Each shot is a single sentence using limited vocabulary from film practice. Readable by both humans and machines. Covers all possible shot types, camera movements, and transitions.

**Why it matters**: PSL is the "intermediate representation" between dramatic intent and camera computation. It could serve as CinematicTheory's shot-level output format -- the equivalent of MIDI-JSON in MusicTheory.

> Ronfard et al. "The Prose Storyboard Language." arXiv 2015.

### 1.7 Film Editing Patterns (Montage Grammar)

Wu, Christie et al. formalized film editing practices as **constraints over shots** characterizing changes in visual properties between consecutive shots. Patterns like "intensify" and "opposition" encode editing rhetoric -- not individual shots but the *relationships between shots*.

**Why it matters**: FEP answers "given shot A, what properties should shot B have to achieve dramatic effect X?" This is the editing-level grammar that governs cut decisions.

> Wu, Christie et al. "Thinking Like a Director: Film Editing Patterns." ACM TOMM, 2018.

### 1.8 Darshak (Camera Planning as POCL Planning)

Jhala and Young built an end-to-end camera planning system using hierarchical partial-order causal link (POCL) planning. Cinematographic rules (180-degree rule, 30-degree rule, shot-reverse-shot, establishing shots) become **preconditions and effects in a planning domain**. The hierarchical structure (abstract communicative operators decompose into primitive camera operators) maps to HTN-style planning.

**Why it matters**: The most complete formalization of "film idiom as planning operators." The rule encodings are directly extractable for use with faster runtime solvers (like the GOAP planner Bannou already has).

> Jhala, Young. "Cinematic Visual Discourse." IEEE Trans. Multimedia, 2010.
> Jhala, Young. "A Discourse Planning Approach to Cinematic Camera Control." AAAI 2005.

### 1.9 The Bares-Lester Three-Tier Architecture

A narrative planner together with autonomous character directors creates cinematic goals that are passed to a constraint-based cinematography planner. The three tiers -- narrative planning, character direction, camera computation -- mirror human film production (writer/director/cinematographer).

**Why it matters**: This decomposition maps directly to Bannou's existing architecture: Event Brain (narrative planning) -> Actor behavior (character direction) -> CinematicTheory (camera computation).

> Bares, Gregoire, Lester. "Realtime Constraint-Based Cinematography." AAAI/IAAI 1998.

### 1.10 Industry Implementations

**Unity Cinemachine**: Pipeline-based camera composition (Body + Aim + Noise stages). State-Driven Camera maps virtual cameras to Animator Controller states. The industry standard for real-time camera formalism.

**God of War (2018)**: Single continuous camera with no cuts -- forces all transitions (exploration, combat, dialogue, cutscene) through continuous motion. The most demanding real-time camera constraint in commercial games.

**Director's Lens**: Computes a diverse set of cinematically valid camera placement suggestions for any narrative moment, with learned personalization from user edits. Integrated into Autodesk MotionBuilder.

> Unity Cinemachine Documentation.
> GDC 2019: "Evolving Combat in God of War."
> Lino, Christie et al. "The Director's Lens." ACM Multimedia 2011.

### 1.11 Visual Properties Language

Ranon, Christie, and Urli defined a formal language to express visual properties (shot size, vantage angle, on-screen position, visibility) with formal semantics for measuring their satisfaction. This is the "type system" for cinematic constraints -- given a camera configuration and a desired property, how satisfied is it?

> Ranon, Christie, Urli. "Accurately Measuring the Satisfaction of Visual Properties." Smart Graphics 2010.

---

## Part 2: Fight Choreography

### 2.1 Laban Movement Analysis (Movement Quality Parameterization)

Rudolf Laban's system categorizes movement along four bipolar continua:

| Factor | Pole A | Pole B | What It Encodes |
|--------|--------|--------|----------------|
| **Weight** | Light | Strong | Mover's impact on the world |
| **Time** | Sustained | Sudden | Sense of urgency |
| **Space** | Indirect | Direct | How attention orients to environment |
| **Flow** | Free | Bound | Attitude toward bodily control |

Combining Weight, Time, and Space produces **eight named efforts** that map directly to combat actions:

| Effort | Weight | Time | Space | Combat Application |
|--------|--------|------|-------|-------------------|
| **Punch/Thrust** | Strong | Sudden | Direct | Power strikes, lunges |
| **Slash** | Strong | Sudden | Indirect | Sweeping attacks, spinning strikes |
| **Press** | Strong | Sustained | Direct | Grapples, holds, driving pushes |
| **Wring** | Strong | Sustained | Indirect | Joint locks, twisting throws |
| **Dab** | Light | Sudden | Direct | Jabs, feints, precision strikes |
| **Flick** | Light | Sudden | Indirect | Deflections, distracting strikes |
| **Glide** | Light | Sustained | Direct | Evasive movement, controlled retreat |
| **Float** | Light | Sustained | Indirect | Circling, assessment, recovery |

A heavy brawler operates primarily in Punch/Press/Wring. A nimble fencer operates in Dab/Flick/Glide. **Character archetype maps directly to effort preference distribution.**

**EMOTE** (Chi et al., SIGGRAPH 2000) proved this computationally: it takes keyframe animations and applies Laban Effort/Shape parameters as transformation layers, producing expressively modified movement from the same base animation. Each effort parameter maps to a 0.0-1.0 float. Different body parts can have different values.

> Chi et al. "The EMOTE Model for Effort and Shape." SIGGRAPH 2000.
> Samadani & Gorbet. "Affective Movement Generation using Laban Effort and Shape and Hidden Markov Models."

### 2.2 The C.R.A. Pattern (Atomic Unit of Combat)

The Society of American Fight Directors codified the **Cue-Reaction-Action** pattern as the fundamental atomic unit of stage combat:

1. **Cue**: Aggressor's preparatory movement (drawing arm back, shifting weight). Always preceded by eye contact.
2. **Reaction**: Defender's acknowledgment and preparation (reading the cue, beginning defensive response).
3. **Action**: Both combatants complete the offensive/defensive action together (attack + parry, attack + avoid, feint + reaction).

Every exchange is built from C.R.A. sequences chained together. This is the smallest composable unit of fight choreography.

**Codified terminology**: Distance/Measure (proper distance for technique), Partnering (cooperative execution), Knap (impact sound effect), Avoidance (defensive movement), Invitation (deliberate opening).

**Notation system** (Weapons of Choice): `+` prefix = attack, `-` prefix = defense, numbered sequence, R/L for hand. Complete novices can read and replicate fights within 30 minutes of instruction.

> SAFD Glossary of Terms (2010/2016).
> Weapons of Choice: The Textbook of Stage Combat.

### 2.3 Distance Categories (Spatial State Machine)

From the Academie Duello action logic system:

| Distance | Name | What's Available |
|----------|------|-----------------|
| **Out** | Out of Distance | No attacks possible; assessment, circling |
| **Long** | Long Distance | Advance + lunge required; committed attacks only |
| **Medium** | Medium Distance | Primary weapon efficient; standard exchanges |
| **Close** | Close/Short | Dagger, grapple, elbow, knee; weapons entangle |

Distance is a spatial state that **constrains which moves are available**. Transitions between distances are themselves choreographic actions (advance, retreat, sidestep, lunge). Attack logic: "box the opponent into a corner where their only escape runs them into your weapon" -- forcing specific defensive responses narrows the opponent's option space.

> Academie Duello Fight Choreography Series.

### 2.4 Bordwell's Pause-Burst-Pause (Macro-Rhythm)

David Bordwell's formal analysis of Hong Kong action cinema identified the **pause-burst-pause** pattern as the fundamental rhythmic structure:

- **Burst**: Rapid sequence of beats at high tempo
- **Pause**: Held moment (fraction of a second to several seconds) -- often a blocked blow, a stare-down, or the Beijing Opera *liang hsiang* ("bright appearance") frozen pose

The pattern parallels music: bursts are phrases, pauses are rests. The oscillation creates the dance-like quality of martial arts cinema. Successive bursts escalate in intensity.

**Bordwell's three goals**: Clarity (viewer can see all maneuvers), Expressiveness (emotional feel to the action), Emotional coloring (specific quality of execution).

> David Bordwell. *Planet Hong Kong.* Harvard University Press, 2000/2011.

### 2.5 Jackie Chan's Nine Principles (Compositional Rules)

Tony Zhou codified nine principles from Chan's work:

1. **Start with a DISADVANTAGE** -- initial state deficit (outnumbered, unarmed, bad position)
2. **Use the ENVIRONMENT** -- surroundings are active participants, not backdrop
3. **Be CLEAR in your shots** -- wide framing, spatial legibility
4. **Action & Reaction in the SAME frame** -- both blow and impact shown simultaneously
5. **Do as many TAKES as necessary** -- commitment to perfect execution
6. **Let the audience feel the RHYTHM** -- pacing creates temporal engagement
7. **TWO good hits = ONE great hit** -- show action twice from different angles for amplification
8. **PAIN is humanizing** -- display physical consequences
9. **Earn your FINISH** -- build momentum so the final blow feels justified

Principles 1, 2, 6, 7, and 9 are directly formalizable as compositional constraints.

> Tony Zhou. "Jackie Chan -- How to Do Action Comedy." Every Frame a Painting, 2014.

### 2.6 Fight as Dramatic Arc (Micro-Narrative)

Multiple sources converge on a dramatic arc within fight scenes mirroring classical structure:

1. **Opening / Setup** -- establishment of combatants, distances, intentions
2. **Rising Action / Escalation** -- progressive intensification, shifts in who controls the fight
3. **Reversal(s)** -- the tide turns; character-defining moments
4. **Climax** -- the decisive moment
5. **Resolution** -- consequences, aftermath

Within each phrase, a **Want-Attempt-Evaluation-Adjustment** cycle operates: "I'm going to attack here" -> move -> "That didn't work, I'll strike here" -> move. This is an intention-based loop that maps directly to a GOAP planning system.

**The Seven-Move Rule**: Limit memorized patterns to 7 moves per actor, then recombine through rhythmic variation and looping. Aligns with cognitive science working memory limits. Natural phrase length for procedural generation.

> Weapons of Choice: The Textbook of Stage Combat.
> John Kreng. *Fight Choreography: The Art of Non-Verbal Dialogue.*

### 2.7 Fighting Game Frame Data (Micro-Timing)

The fighting game community independently formalized the most mathematically rigorous beat system:

| Parameter | What It Encodes |
|-----------|----------------|
| **Startup frames** | Preparation beats before impact |
| **Active frames** | Beats during which impact can occur |
| **Recovery frames** | Beats after action before next action possible |
| **Hit stun** | Beats the defender is locked out of action |
| **Frame advantage** | Gap between when one character can move while other cannot |
| **Hit stop** | Brief freeze at impact moment for emphasis |

This is fully formalized, frame-precise, and already exists as code in every fighting game. Combined with Laban effort qualities and C.R.A. structure, it provides the complete timing model.

> Capcom Fighting Network: SF Seminar: The Basics of Attacking.

### 2.8 Game Combat Systems (Procedural Choreography in Practice)

**Batman Arkham FreeFlow**: Four inputs (strike, cape, dodge, parry) -> contextual animation selection based on positions, enemy types, combo count -> animation cancel for flow -> choreographic sequences emerge from state machine transitions. Designed around DDR rhythm, not fighting game combos.

**God of War Cancel System**: ~4,000 cancel branches across all moves. Pre-hit and post-hit cancel windows. Buffer vs. instant command methods. The cancel system is a state machine with frame-precise transition windows.

**For Honor Motion Matching**: Database search for best-matching combat pose. Cost-function approach treating animation selection like pathfinding. "The goal is to be as predictable as possible" -- stability over responsiveness.

**Sifu**: ~200 base movements with 160+ contextual modifications. Curated vocabulary + contextual inflection = "alphabet + grammar."

**MAAIP** (SIGGRAPH 2023): Multi-Agent Adversarial Imitation Learning for physics-based two-character fighting. Trains control policies from mocap, preserving per-character style.

> FreeFlow Combat analysis. Eric Williams, "Combat Canceled." GDC 2016 Motion Matching. MAAIP, SIGGRAPH 2023.

---

## Part 3: Dramatic Grammar & Tension Models

### 3.1 Neil Cohn's Visual Narrative Grammar (The Lerdahl-Jackendoff Equivalent)

The closest thing to a "Generative Theory of Tonal Music" for drama. Cohn developed a **formal constituency grammar for visual narrative**, validated by ERP neural evidence showing the same brain mechanisms as linguistic syntax processing.

**Five narrative categories**:
- **Establisher (E)**: Introduces characters/setting without initiating action
- **Initial (I)**: Initiates tension of the narrative arc (preparatory action)
- **Prolongation (L)**: Medial state of extension (held moment, trajectory)
- **Peak (P)**: Climactic action, maximal event structure, height of tension
- **Release (R)**: Dissolves tension through aftermath/resolution

**Canonical rewrite rule**: `Phase -> (E) - (I (L)) - P - (R)` (parentheses = optional)

**Recursive hierarchy**: Individual images group into constituents that themselves fulfill narrative roles at higher levels. An Initial at one level can internally contain its own E-I-L-P-R structure. This is **directly analogous to how Lerdahl-Jackendoff's time-span reduction nests musical groups**.

**Neural validation**: Left anterior negativity (500-700ms) triggered by structural violations -- the same ERP component triggered by syntactic violations in language.

> Cohn. "Visual Narrative Structure." Cognitive Science, 2013.
> Cohn & Kuperberg. "The grammar of visual narrative: Neural evidence for constituent structure." 2014.

### 3.2 Reagan's Six Emotional Arcs (Validated Vonnegut)

1,700+ stories analyzed via sentiment analysis. Three independent decomposition methods (SVD, hierarchical clustering, SOM) converge on **six fundamental arc shapes**:

1. **Rags to Riches** -- steady rise
2. **Tragedy** -- steady fall
3. **Man in a Hole** -- fall then rise
4. **Icarus** -- rise then fall
5. **Cinderella** -- rise-fall-rise
6. **Oedipus** -- fall-rise-fall

These are representable as **basis functions** that can be linearly combined for complex narratives. Validates Vonnegut's intuition: "the simple shapes of stories can be fed into computers."

> Reagan et al. "The emotional arcs of stories are dominated by six basic shapes." EPJ Data Science, 2016.

### 3.3 The Resonator Model (Constrained Arc Generation)

Brown and Tu propose that once a story's beginning valence (happy/sad) and ending valence are fixed, the internal phases are constrained to oscillate between them like **standing wave modes in a resonator tube**. More complex plots are higher-order harmonics (more oscillation cycles between fixed endpoints).

**Why it matters for CinematicTheory**: Given a fight's opening state (disadvantage) and ending state (victory/defeat), the resonator model constrains what the tension curve between them can look like. Higher-order harmonics = more reversals = more complex fights.

> Brown & Tu. "The shapes of stories: A resonator model of plot structure." Frontiers of Narrative Studies, 2021.

### 3.4 Facade's Beat Sequencing (Runtime Tension Control)

Mateas and Stern's Facade (2005) is the landmark implementation of runtime dramatic tension management. The Drama Manager:

- Maintains a pool of **annotated beats** (preconditions, story value effects, weights)
- Scores candidate beats against a target **Aristotelian tension arc**
- Greedy-selects the beat whose effects most closely match the near-term trajectory

This is directly applicable to CinematicTheory: the "beats" are choreographic exchanges, and the tension arc is the fight's dramatic trajectory. Beat selection becomes a scoring function against the target curve.

> Mateas & Stern. "Structuring Content in the Facade Interactive Drama Architecture." AIIDE 2005.

### 3.5 Left 4 Dead's AI Director (Intensity FSM)

A three-phase FSM cycling continuously:

1. **Build-Up**: Spawn threats to increase intensity. Duration scales with how fast intensity rises.
2. **Peak/Sustain**: Maximum intensity reached. **Stop spawning**. Hold for a few seconds.
3. **Relax/Fade**: No threats spawn for ~30-45 seconds. Intensity decays naturally.

Per-entity intensity tracking: increases from damage, kills, proximity to threats; decreases over time during low-activity periods. The FSM uses aggregate state for phase transitions.

**Why it matters**: The Build-Up/Peak/Relax cycle is exactly Bordwell's pause-burst-pause pattern formalized as a state machine. The per-entity intensity tracking maps to per-participant dramatic weight in a choreographic sequence.

> Michael Booth. "The AI Systems of Left 4 Dead." Valve, 2009.

### 3.6 Eisenstein's Five Montage Types (Parameterized)

| Type | What It Controls | Parameterization |
|------|-----------------|-----------------|
| **Metric** | Shot duration rhythm | Tension via acceleration (shortening shots while preserving ratios). Pure temporal parameter. |
| **Rhythmic** | Content-driven pacing | Edit points from movement/content within the shot, not fixed duration. |
| **Tonal** | Emotional quality | Edit based on dominant emotional tone per shot (lighting, composition, movement quality). Maps to valence/arousal vector. |
| **Overtonal** | Cumulative effect | Weighted sum of metric + rhythmic + tonal. The emergent "feel." |
| **Intellectual** | Conceptual collision | Juxtaposition creates meaning in neither shot alone. The Kuleshov effect. |

Metric is trivially implementable. Tonal maps to PAD/Russell affect models. Intellectual remains the hardest to formalize.

> Eisenstein. *Film Form* (1949), *The Film Sense* (1942).

### 3.7 Walter Murch's Rule of Six (Edit Scoring Function)

A weighted priority system for editing decisions:

| Priority | Criterion | Weight |
|----------|----------|--------|
| 1 | **Emotion** | 51% |
| 2 | **Story** | 23% |
| 3 | **Rhythm** | 10% |
| 4 | **Eye trace** | 7% |
| 5 | **2D composition** | 5% |
| 6 | **3D spatial continuity** | 4% |

Never sacrifice higher priority for lower. Directly implementable as a multi-objective optimization function for camera cut decisions.

> Walter Murch. *In the Blink of an Eye.* 2001.

### 3.8 Cutting's Film Pacing Metrics

James Cutting (Cornell) measured 210 movies across 85 years with three continuous pacing channels:

- **Shot duration** (transition density -- how fast cuts come)
- **Flicker motion** (inter-frame pixel correlation -- visual change rate)
- **Luminance** (mean brightness -- correlated with emotional valence)

Average shot length decreased from 10.5s (1930s) to 4.3s (contemporary). Shot durations follow approximately exponential decay through setup, reaching minimum at climax, then lengthening in epilogue.

Three orthogonal, continuous, measurable pacing channels. Directly invertible for generation: specify target pacing curves, generate shot parameters.

> Cutting et al. "The evolution of pace in popular movies." Cognitive Research, 2016.

### 3.9 Story Grammars (Mandler-Johnson, Propp)

**Mandler-Johnson**: Story -> Setting + Event Structure. Event Structure -> Episode(s). Episode -> Initiating Event + Reaction + Goal + Attempt + Outcome + Resolution. Connectives: And (simultaneity), Then (sequence), Cause (causality). Validated by recall experiments -- violations are harder to remember.

**Propp**: 31 character functions in fixed order. Formalized as a context-free grammar by Gervas (2013, 2025) with extensions for long-range dependencies and character role restrictions. Multiple computational implementations.

> Mandler & Johnson. "Remembrance of things parsed." Cognitive Psychology, 1977.
> Gervas. "Propp's Morphology as a Grammar for Generation." Dagstuhl OASIcs, 2013.

### 3.10 Affect Models (Emotion Parameterization)

**Russell Circumplex**: 2D continuous space. Valence (pleasure/displeasure) x Arousal (activation/deactivation). Every emotional state is a point in 2D space. Tension = high arousal; valence determines fear-tension vs. excitement-tension.

**PAD Model**: 3D. Pleasure, Arousal, Dominance. Critical for combat: a fight where the protagonist is winning has different tension quality than one where they're losing, even at identical arousal. Dominance encodes this.

**Plutchik's Wheel**: 8 basic emotions with intensity gradients (annoyance -> anger -> rage) and algebraic combination rules (joy + trust = love). Discrete emotion vocabulary with composition.

---

## Synthesis: Mapping Theory to CinematicTheory Architecture

The planning document ([CINEMATIC-SYSTEM.md](CINEMATIC-SYSTEM.md)) identifies four areas CinematicTheory must formalize: dramatic grammar, spatial reasoning, capability matching, and camera direction. Here's how the research maps:

### Layer 1: Movement Vocabulary (Laban + Stage Combat + Frame Data)

Every atomic movement in a choreographic sequence is parameterized by:

| Parameter | Source | Type | Range |
|-----------|--------|------|-------|
| Laban Weight | LMA | float | 0.0 (Light) - 1.0 (Strong) |
| Laban Time | LMA | float | 0.0 (Sustained) - 1.0 (Sudden) |
| Laban Space | LMA | float | 0.0 (Indirect) - 1.0 (Direct) |
| Laban Flow | LMA | float | 0.0 (Free) - 1.0 (Bound) |
| Technique type | SAFD | enum | Strike, Parry, Avoid, Feint, Grapple, Throw |
| Target zone | SAFD | enum | Head, High-L, High-R, Low-L, Low-R, Center |
| Distance category | Academie Duello | enum | Out, Long, Medium, Close |
| Role | SAFD | enum | Aggressor, Defender |
| Startup frames | Frame data | int | Pre-impact duration |
| Active frames | Frame data | int | Impact window |
| Recovery frames | Frame data | int | Post-action vulnerability |

A character archetype defines its **effort preference distribution** (heavy brawler: biased toward Punch/Press/Wring; nimble fencer: biased toward Dab/Flick/Glide). This is the EMOTE model applied to combat.

### Layer 2: Beat Composition (C.R.A. + VNG Categories)

Each beat follows the **Cue-Reaction-Action** pattern (SAFD) and maps to a **Visual Narrative Grammar category** (Cohn):

| Beat Type | C.R.A. Phase | VNG Category | Example |
|-----------|-------------|-------------|---------|
| Setup beat | Cue dominant | **Establisher (E)** | Fighters circle, assess |
| Initiating beat | Full C.R.A. | **Initial (I)** | First exchange, testing |
| Extending beat | Repeated C.R.A. | **Prolongation (L)** | Continued exchange, escalation |
| Climactic beat | Decisive C.R.A. | **Peak (P)** | Finishing blow, reversal |
| Aftermath beat | Post-action | **Release (R)** | Consequences, recovery |

VNG's recursive constituency means a fight's macro-arc (E-I-L-P-R) can contain sub-arcs within each phase. An escalation phase (L) might internally contain its own mini E-I-L-P-R as a secondary exchange builds and resolves.

### Layer 3: Phrase and Sequence Structure (Bordwell + Tension Curves)

Phrases are 3-7 beats (Seven-Move Rule) following Bordwell's **pause-burst-pause** rhythm:

```
[Pause] -> [Burst: 3-7 beats at escalating tempo] -> [Pause] -> [Burst: higher intensity] -> ...
```

The overall fight follows one of Reagan's **six emotional arcs** (most commonly "Man in a Hole" for protagonist fights or "Oedipus" for fights with reversals), constrained by the **Resonator Model** (beginning state + ending state constrains the internal shape).

The **L4D AI Director pattern** (Build-Up / Peak / Relax) provides the runtime intensity FSM that manages moment-to-moment pacing within the chosen arc.

Escalation rules (from stage combat theory):
- Each phrase is more intense than the last
- Reversals create local dips but overall trajectory is upward
- Jackie Chan Principle 9: "Earn your finish" -- minimum accumulation before resolution

### Layer 4: Camera Direction (DCCL + Toric Space + Murch + FEP)

The camera layer composes from the research models:

| Decision | Model | How It Works |
|----------|-------|-------------|
| **Shot type selection** | DCCL / Virtual Cinematographer | Hierarchical FSM selects idiom for current scene type |
| **Two-target framing** | Toric Space | Efficient geometric solver for combat shots |
| **Camera path** | Constrained Hypertubes | Elementary movements composed with smooth transitions |
| **Cut timing** | Eisenstein Metric Montage + Cutting's metrics | Accelerating shot duration toward climax |
| **Cut selection** | Murch's Rule of Six | Weighted scoring: emotion 51%, story 23%, rhythm 10% |
| **Shot-to-shot grammar** | Film Editing Patterns (FEP) | Constraint-based relationships between consecutive shots |
| **Constraint solving** | CSP with frame coherence | Balance shot quality against visual smoothness |
| **Visual properties** | Ranon/Christie/Urli language | Formal measurement of property satisfaction |

### Layer 5: Dramatic Planning (GOAP + Facade + Darshak)

CinematicStoryteller uses GOAP planning (already in Bannou's behavior-compiler) to compose choreographic sequences. The planning approach draws from:

| Aspect | Source Model | Application |
|--------|-------------|-------------|
| **Target tension curve** | Reagan arcs + Resonator model | Define fight's emotional trajectory |
| **Beat scoring** | Facade beat sequencing | Score candidate exchanges against target curve |
| **Camera as plan** | Darshak POCL planning | Film idiom rules as planning operator preconditions |
| **Affect tracking** | PAD model | 3D emotional state (pleasure, arousal, dominance) per participant |
| **Pacing FSM** | L4D AI Director | Build-Up/Peak/Relax cycling within the tension curve |
| **Player adaptation** | PaSSAGE player modeling | Bias toward learned player preferences |

### The Complete Stack

```
CinematicStoryteller (GOAP planning)
    │  Target arc (Reagan/Resonator) + Participant context + Environment
    │  Output: ABML cinematic document
    │
    ├── Dramatic Grammar (Cohn VNG + Mandler-Johnson)
    │     Recursive E-I-L-P-R constituency
    │     Phrase structure: 3-7 beat bursts with pauses (Bordwell)
    │
    ├── Beat Composition (SAFD C.R.A. + Laban + Frame Data)
    │     Atomic exchanges with effort-quality parameterization
    │     Distance-state-constrained move availability
    │     Character archetype -> effort preference distribution
    │
    ├── Camera Direction (DCCL + Toric + Murch + FEP)
    │     Idiom-based shot selection (HFSM)
    │     Two-target geometric solving
    │     Cut scoring and editing grammar
    │
    └── Tension Management (L4D Director + Facade Sequencer)
          Build-Up/Peak/Relax FSM
          Per-participant PAD affect tracking
          Agency-gated QTE density (spirit fidelity)
```

---

## Key References (Organized by Priority for Implementation)

### Must-Read (Directly Implementable Formal Models)

- **Ronfard 2021**: "Film Directing for Computer Games and Animation" -- THE taxonomy survey
- **He/Cohen/Salesin 1996**: "The Virtual Cinematographer" -- HFSM idiom architecture
- **Lino/Christie 2015**: "Toric Space" -- efficient two-target camera solving
- **Cohn 2013**: "Visual Narrative Structure" -- formal grammar for dramatic sequence
- **Chi et al. 2000**: "The EMOTE Model" -- Laban Effort as animation parameters
- **Jhala/Young 2010**: "Cinematic Visual Discourse" -- film idiom as POCL operators
- **Reagan et al. 2016**: "Six Emotional Arcs" -- validated narrative shape primitives
- **SAFD Glossary**: C.R.A. pattern and codified combat vocabulary
- **Mateas/Stern 2005**: Facade beat sequencing -- runtime tension matching

### Highly Relevant (Strong Formal Models)

- **Christie/Languenon 2002**: Constrained Hypertubes for camera paths
- **Wu/Christie 2018**: Film Editing Patterns (montage grammar)
- **Ronfard et al. 2015**: Prose Storyboard Language (EBNF shot grammar)
- **Brown/Tu 2021**: Resonator model (constrained arc generation)
- **Booth 2009**: Left 4 Dead AI Director (intensity FSM)
- **Murch 2001**: Rule of Six (weighted edit scoring)
- **Cutting et al. 2016**: Film pacing as three measurable channels
- **Bordwell 2000**: *Planet Hong Kong* (pause-burst-pause formal analysis)
- **Weapons of Choice**: Phrase structure, 7-move rule, notation system
- **Ranon/Christie/Urli 2010**: Visual properties measurement language

### Supporting References

- **Halper/Burry 2001**: Camera engine constraint-vs-coherence trade-off
- **Bares/Lester 1998**: Three-tier cinematography architecture
- **Eisenstein 1949**: Five montage types (parameterizable axes)
- **Mandler/Johnson 1977**: Story grammar (rewrite rules)
- **Gervas 2013**: Propp's morphology as context-free grammar
- **Riedl/Young JAIR**: IPOCL narrative planning
- **Drucker 1994**: PhD thesis on intelligent camera control
- **Cohn/Kuperberg 2014**: Neural evidence for narrative constituency
- **MAAIP 2023**: Multi-agent adversarial interaction for combat animation

---

*This document captures formal theory research for the CinematicTheory SDK. For the architectural plan, see [CINEMATIC-SYSTEM.md](CINEMATIC-SYSTEM.md).*
