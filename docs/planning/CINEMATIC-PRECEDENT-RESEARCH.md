# Situationally Triggered Cinematics: Precedent Research

> **Type**: Research reference document
> **Purpose**: Established precedent and formal theory for triggered/on-demand cinematics in games
> **Related**: [CINEMATIC-SYSTEM.md](CINEMATIC-SYSTEM.md) (the architectural gap this research informs)
> **Date**: 2026-02-17

---

## Overview

This document captures research across three domains -- combat cinematic triggers, interactive cinematic (QTE) systems, and procedural cinematic theory -- to establish precedent for the compositional layer described in [CINEMATIC-SYSTEM.md](CINEMATIC-SYSTEM.md). The goal is to identify what terminology, design patterns, and formal frameworks already exist for systems that take gameplay state and produce choreographed visual sequences with defined entry/exit points and continuation logic.

---

## Part 1: Terminology Landscape

The field has no single canonical term. Several overlapping terms coexist across industry and academia:

| Term | Scope | Origin |
|------|-------|--------|
| **Quick Time Event (QTE)** | Button-prompt interaction during cinematic | Yu Suzuki, Shenmue (1999) |
| **Context-Sensitive Action** | Any action gated by compound state predicates | Industry-wide |
| **Paired / Synchronized Animation** | Two-entity coordinated choreography | GDC animation talks, Unreal Engine |
| **Super Freeze / Super Flash** | Gameplay-to-cinematic transition state in fighters | FGC frame data community |
| **Active Cinematic** | Sequence that is both gameplay and cutscene | Naughty Dog, Uncharted 2 (GDC 2010) |
| **Scripted Sequence** | Pre-defined events triggered by player location/actions | Half-Life tradition |
| **Film Idiom** | FSM encoding how to film a particular scene type | He, Cohen, Salesin (SIGGRAPH 1996) |
| **Drama Manager** | Omniscient agent steering interactive narrative | Mateas & Stern, Facade (2003) |
| **Foldback Structure** | Local branching within globally convergent narrative | Academic narrative studies |
| **Cinematic Strike / Cinematic Evasion** | Timed input during boss cinematic transition | Final Fantasy XVI (2023) |
| **Hitstop / Hitfreeze** | Micro-pause on impact enabling decision/cancel windows | Fighting game tradition |

The unifying pattern across all systems is best described as a **State-Conditioned Cinematic Transition**: a system where a specific conjunction of gameplay state variables (health, position, input, meter, character identity, stage identity) triggers a transition from interactive gameplay into a choreographed animation sequence with defined entry semantics, optional branching/continuation points, and exit semantics.

---

## Part 2: Combat Cinematic Trigger Systems

### 2.1 Mortal Kombat: Fatalities, Stage Transitions, and Conditional Cinematics

**Evolution from MK1 (1992) to MK1 (2023):**

| Era | System | Trigger | Innovation |
|-----|--------|---------|------------|
| MK1 (1992) | Fatality | Opponent defeated + input + distance | The original state-triggered cinematic |
| MK2-3 (1993-95) | Stage Fatality | Defeated + specific position + input | Spatial awareness added |
| Deception (2004) | Death Traps | Knockback into hazard zone (any health) | Mid-match environmental cinematics |
| Armageddon (2006) | Kreate-A-Fatality | Post-defeat + continuous input chain | Player-composed cinematics (11 depth levels) |
| MK9 (2011) | X-Ray Attacks | Super meter full + input | Mid-match cinematic triggers |
| MKX (2015) | Brutalities | Final hit + match conditions (move count, distance, etc.) | Complex multi-condition hidden triggers |
| MK11 (2019) | Krushing Blows | Highly specific conditional (counter hit, block type, combo length) | Once-per-game conditional cinematics; **"three-beat formula"** for pacing |
| MK11 (2019) | Fatal Blows | Health < 30%, once per match | Health-threshold-triggered cinematic |
| MK1 (2023) | Kameo Fatalities | Post-defeat + Kameo partner | Multi-entity choreography |

**Key Design Insight -- Three-Beat Formula**: NetherRealm's design team structures each fatality around "three beats to help sell it" -- setup, escalation, climax. This rhythmic principle applies to any choreographed combat cinematic.

**Key Design Experiment -- Kreate-A-Fatality**: MK Armageddon experimented with replacing pre-authored cinematics with real-time player-composed sequences (chains, transitions, finishers across 11 levels). This is the closest any fighting game has come to runtime choreographic composition -- and it was widely considered a misstep, suggesting that authored quality matters more than compositional freedom for this specific use case.

### 2.2 Dead or Alive: Danger Zones and Environmental Cinematic Transitions

DOA's cinematic triggers are built on top of its Triangle System (strikes beat throws, throws beat holds, holds beat strikes). The system evolved through increasingly sophisticated environmental integration:

| Era | System | Trigger | Innovation |
|-----|--------|---------|------------|
| DOA1 (1996) | Simple Danger Zones | Contact with arena edge | Positional trigger leading to state change |
| DOA2 (1999) | Multi-Tier Environments | Knockback into typed boundary | Boundary-type-specific cinematic responses |
| DOA3-4 (2001-05) | Expanded Interactivity | Knockback vector + geometry | Continuum from gameplay to cinematic-like sequences |
| DOA5 (2012) | Power Blow | Health < 50% + input | **Player-directed spatial targeting mid-cinematic** |
| DOA5 (2012) | Cliffhanger | Knocked over edge | **Cinematic trigger that opens new interactive sub-state** |
| DOA6 (2019) | Break Blow | Gauge + input | **Cancelable before cinematic** -- player chooses full cinematic or extended combo |

**Key Design Innovations**:

- **Power Blow**: A cinematic trigger with a mid-cinematic player choice (directional aiming) that determines subsequent gameplay state. The player literally aims their opponent at environmental hazards during the cinematic.
- **Cliffhanger**: A cinematic trigger that opens a new interactive sub-state rather than playing to completion -- the fallen fighter grabs the edge, creating an attacker/defender mini-game.
- **Break Blow cancel**: The player can cancel after the initial hit, before the cinematic plays -- choosing between cinematic payoff and gameplay optimization.

### 2.3 Wrestling Games: The Grapple as Cinematic Trigger

Wrestling games present a unique challenge: almost every significant move is a paired animation requiring two entities to be precisely synchronized through a choreographic sequence. The entire genre is a system for managing state-triggered paired cinematics.

**The AKI Engine (WrestleMania 2000, No Mercy)**:
- Proximity trigger enters lock-up state
- Input race during lock-up determines move selection
- Weak vs. strong grapple tiers (tapping vs. holding)
- Position determines entire move palette (front: 16 moves; back: 4 moves)
- Attitude Meter (momentum gauge) gates finisher cinematics
- **Reversal as continuation point**: Every grapple has an implicit branch point where the sequence can transition to a counter-animation

**Fire Pro Wrestling**:
- Timing-based resolution with **audio sync point** -- the "smack" sound when arms lock is the timing reference
- Progressive escalation: stronger moves are gated by accumulated damage (similar to how Bannou's progressive agency gates interaction density)

**WWE 2K Series (modern)**:
- 30 different combos per character with customizable assigned moves
- Breaker system (identify attack type and match button)
- **Major Reversals**: Explicit continuation points with visual prompts during paired cinematic -- "mid-move reversals prompted in the middle of certain moves while they're being executed"
- 3,400+ animations per title

**GDC-Documented System -- UFC Undisputed**:
- "The Next Generation of Fighting Games: Physics & Animation in UFC 2009 Undisputed" (GDC 2009) -- the most formally documented paired animation system
- Covers character animation techniques, physics/animation blending, full-body IK targeting, character navigation for grappling

### 2.4 Dragon Ball Z: Clash Systems and Dramatic Finishes

**Beam Struggles (Budokai Tenkaichi)**:
- Triggered by simultaneous beam attacks colliding
- Symmetric interactive cinematic: both players rotate sticks to push beams
- Continuous mash input determines winner
- Absent from most post-Tenkaichi 3 games until Sparking Zero -- suggesting the design was seen as "gimmicky"

**Dragon Rush (Budokai 3)**:
- Multi-round interactive cinematic built on iterated rock-paper-scissors
- Three rounds, each a continuation point with branching
- Attacker wants mismatches; defender tries to match

**Dramatic Finishes (FighterZ)**:
- The most complex multi-variable trigger: attacker character + defender character + stage + team state + attack type + KO conditions
- ~20 unique cinematics, each recreating an iconic anime moment where the trigger conditions reference the narrative context (Trunks killed Frieza on a rocky battlefield, so Trunks vs. Frieza on Rocky Field triggers that cinematic)
- Some have positional awareness within the cinematic itself

---

## Part 3: Interactive Cinematic and QTE Systems

### 3.1 Agency Model Spectrum

The research converged on a spectrum of player agency within choreographed sequences:

```
Binary Pass/Fail ─────► Branching Selection ─────► Continuous Performance
   (Shenmue, RE4)        (Heavy Rain, Detroit)       (DMC, Bayonetta)
  "Did you react?"      "Which path?"                "How well are you playing?"
```

With two hybrid positions:
- **Binary + Accumulation** (Asura's Wrath): Individual QTEs are binary but feed a Burst gauge that gates narrative progression
- **Threshold-Triggered Cinematic** (GoW 2018): Gameplay performance triggers pre-selected cinematic payoff -- the "QTE" is replaced by earning the right to a spectacular moment

### 3.2 Shenmue (1999): The Origin

Yu Suzuki coined "Quick Timer Events" (later "Quick Time Events"). His design intent: "a fusion of gameplay and movie" inspired by Jackie Chan films. Part of his "FREE" (Full Reactive Eyes Entertainment) philosophy emphasizing continuous player engagement.

- **Agency**: Binary pass/fail with checkpoint restart
- **Choreography**: Pre-authored, no procedural assembly
- **Failure**: Death or restart (punitive)
- **Key principle**: Seamless flow from cinema to QTE "without any loading pauses"

### 3.3 Quantic Dream (Heavy Rain, Detroit: Become Human)

**Heavy Rain (2010)** -- the most significant evolution of the QTE from Shenmue's binary model:

- 170+ days of motion capture shooting with 70+ actors and stuntmen, plus 60 days with 50 actors for facial animation -- **specifically to create branching choreographic paths**
- Fight scenes are "fully choreographed and could be viewed as cut scenes, where you affect the outcome by pressing precise button combinations" -- but "even a successful fight can take two or three different directions before you win"
- Academic analysis confirmed the game uses a **foldback structure**: local branching between key plot points, but key plot points themselves are fixed

**Detroit: Become Human (2018)** -- extends to the logical conclusion:

- Three playable characters who can permanently die, with the story continuing
- Flowchart system makes branching structure transparent
- ~40 possible endings, hundreds of combinations
- Every QTE has narrative consequence

**David Cage's stated philosophy** (D.I.C.E. Summit 2013):

> "Game over is a failure of the game designer, not the player."

Cage's design genre term: **"interactive drama"** (not "adventure game" or "interactive movie").

### 3.4 God of War: The QTE Evolution

**GoW (2005, Jaffe)**: The "macro/micro" combat architecture. Macro: player chooses between combat, magic, or QTE kill. Micro: the input sequence within each approach. QTEs deliver spectacle that standard combat cannot. Health-threshold trigger initiates the QTE.

**GoW (2018, Barlog)**: Explicit removal of QTEs. Replaced by the stun-grab execution system: combat performance (stun accumulation) triggers context-specific execution animation. Barlog: "the emotional connection of a QTE but with greater freedom."

This represents **the post-QTE design evolution**: spectacular moments earned through systemic gameplay mastery rather than tested through input accuracy.

### 3.5 Resident Evil 4/5/6: Lessons

**RE4 (Mikami, 2005)**: Key design insight -- all cutscenes must be real-time so "you'll never know if a QTE is going to happen, so you'll never let go of the controller." Used foley sounds as rhythmic cues before visual prompts.

**RE5 (2009)**: Co-op QTEs as synchronized multi-player cinematics via the "Partner Action Button." Exposed problem: QTEs pertaining to one player while the other has nothing to do.

**RE6 (2012)**: The canonical cautionary case study for QTE overuse. "So afraid of losing your attention that it squeezes drama into every possible moment." **Lesson: QTEs have a diminishing returns curve. When everything is a QTE, nothing feels special.**

### 3.6 Platinum Games (Bayonetta, DMC): Performance-Driven Cinematics

The Platinum/Capcom stylish-action tradition inverts the QTE relationship: instead of the system testing the player during a cinematic, the player's combat creativity drives cinematic quality.

**DMC Style Rank** (D -> C -> B -> A -> S -> SS -> SSS):
- Each attack adds points; same moves yield diminishing returns (variety required)
- Gauge constantly depletes (sustained engagement required)
- Damage drops rank by two levels
- Visual/audio escalation matches rank

**Bayonetta's layered escalation hierarchy**:

| Layer | Mechanic | Trigger | Cinematic Effect |
|-------|----------|---------|------------------|
| 1 | Witch Time | Precision dodge in narrow window | Slow-motion cinematic state |
| 2 | Dodge Offset | Hold attack during dodge | Combo preservation through defensive actions |
| 3 | Wicked Weaves / Umbran Climax | Combo finishers / full magic gauge | Giant summoned entities |
| 4 | Torture Attacks | Magic gauge + proximity | Cinematic kill device (iron maiden, guillotine, etc.) |
| 5 | Climax Finishers | Boss-ending QTE | Full demon summoning for killing blow |

**Key insight**: The "choice" is the ongoing quality of play. The system never stops for a decision. It continuously responds with varying spectacle based on performance. This is a **generative-mode** interaction model.

### 3.7 Asura's Wrath (2012): The Extreme Case

~90% cinematic content with interactive elements, structured as episodic anime (22 episodes with bumpers and previews). CyberConnect2 used backward development: theme first, then story, then gameplay to serve the story.

**The Burst Gauge**: Central innovation. Starts empty, fills through combat performance AND QTE success. When full, triggers a narrative-progressing cinematic. The gauge is simultaneously a combat mechanic, a QTE reward, and a scene-transition trigger -- a **narrative pacing device** that ensures minimum engagement duration per scene.

**Episode scoring**: Time Score + Battle Points + Synchronic Rate (QTE precision). EXCELLENT rating requires precise timing; GOOD is merely successful.

Sits on the controversial side of the "game or movie?" threshold, demonstrating that the threshold exists and generates polarized reception even with high execution quality.

---

## Part 4: Formal Frameworks and Academic Theory

### 4.1 Choice Poetics (Mawhorter, FDG 2014)

The most rigorous academic framework for analyzing interactive choices, including QTEs. Analyzes choices through:

- **Options**: What the player can choose (succeed/fail, left/right, attack/defend)
- **Outcomes**: What results (death, alternative animation, narrative branch)
- **Player Goals**: What the player is trying to achieve
- **Modes of Engagement**: How the player approaches the experience

Key choice types that map to cinematic triggers:
- **Blind choice**: No framing information (Shenmue's surprise QTEs)
- **False choice**: Appears to branch but doesn't (some Heavy Rain QTEs at foldback points)
- **Dead-end option**: Brings story to immediate end (RE4 QTE failure -> death)
- **Obvious choice**: One option is clearly better but the act of choosing creates meaning (Detroit's moral moments)

### 4.2 Interactive Cinema Taxonomy (MediAsia 2019)

Four modes of interactive cinema interaction:

1. **Cognitive mode**: Engagement through interpretation and meaning-making
2. **Selective mode**: Empowered to choose narrative paths (Detroit branching)
3. **Physical mode**: Engagement through embodied physical activity (QTE button presses)
4. **Generative mode**: Inputs contribute to creation of new content (DMC/Bayonetta performance-driven)

Bannou's progressive agency slider spans Physical -> Generative based on the spirit's fidelity value.

### 4.3 Narrative Agency Roles (Wardrip-Fruin et al., 2016)

Three roles the player can assume:

1. **Audience**: Observing pre-authored content (passive cutscene)
2. **Actor**: Performing within pre-authored structure (QTE execution)
3. **Author**: Making choices that alter the narrative (branching cinematics)

Applied to the case studies:
- Shenmue: Audience / Actor alternation
- Heavy Rain / Detroit: Actor with Author moments
- DMC / Bayonetta: Simultaneous Actor + Author (performance style creates unique sequences)
- Asura's Wrath: Actor / Actor / Audience cycling

Bannou's progressive agency continuously shifts a player along the Audience -> Actor -> Author axis based on fidelity.

### 4.4 Virtual Cinematography

**He, Cohen, Salesin, "The Virtual Cinematographer" (SIGGRAPH 1996)**: The seminal paper. Encodes cinematographic expertise as hierarchically organized FSMs called **film idioms** -- each idiom handles a particular scene type (three actors conversing, one actor moving). Generates complete camera specifications in real-time.

**Lino et al., "A Real-time Cinematography System" (SCA 2010)**: Uses **Director Volumes** -- spatial partitions providing characterization over viewpoint space -- for camera selection. Traditional path-planning for camera transitions.

**Cine-AI (CHI PLAY 2022)**: Open-source procedural cinematography that imitates specific human directors' styles. Users correctly associated generated cutscenes with target directors. [GitHub: inanevin/Cine-AI](https://github.com/inanevin/Cine-AI).

**Fundamental tension** (Halper et al., Eurographics 2001): Optimal constraint satisfaction makes cameras jump erratically, so systems must trade composition quality against **frame coherence** (temporal consistency).

### 4.5 GOAP and Planning for Cinematics

**Jeff Orkin, "Three States and a Plan: The A.I. of F.E.A.R." (GDC 2006)**: The foundational GOAP presentation. F.E.A.R.'s FSM has only three states; A* plans action sequences. Characters dynamically re-plan when environments change.

**Narrative planning survey** (Young et al., 2013): Comprehensive coverage of STRIPS-based, HTN-based, and hybrid narrative planning approaches.

**Mateas & Stern, Facade (AIIDE 2005)**: Drama manager architecture using ABL (A Behavior Language) -- a reactive planning language managing parallel/sequential behavior. Beats are atomic units of story content; a sequencer selects beats based on preconditions and author-specified tension arcs.

**Key gap**: While GOAP has been extensively used for NPC behavior and narrative planning is well-studied, the specific application of GOAP to **choreographic composition** (planning dramatic beat sequences for combat cinematics) appears unexplored in published literature.

### 4.6 Continuation-Based Design

**Formal CS foundation**: A continuation is an abstract representation of the control state of a program -- it reifies the execution context at a given point, allowing it to be resumed later. Delimited continuations capture only a bounded portion of execution context.

**Connection to game animation**: Robert Nystrom's *Game Programming Patterns* describes the **pushdown automaton** (state machine with a stack) for animation -- push a new state (firing), and when complete, pop to resume previous state. This is functionally equivalent to a continuation-based system.

**Application to interactive sequences**: The continuation point concept (as used in Bannou's `CinematicInterpreter`) is a direct application of delimited continuations to gameplay -- a named pause in a deterministic sequence where external input can be injected before resumption.

**Known limitation**: "Coroutines aren't very amenable to interruptions -- if an NPC is walking to the door and you decide to hit it with your sword, it's awkward to get it to react appropriately but still eventually open the door." This is precisely the tension that continuation points with timeout-based default flows address.

### 4.7 Procedural Animation Composition

**Assassin's Creed Odyssey (GDC 2019)**: ~15% of 30 hours of cinematics are fully procedural; all scenes contain some procedural elements. The system generates staging, acting, editing, framing, and lighting. The most concrete industry implementation of automated cinematic composition.

**Shadow of Mordor/War**: The Nemesis System composes execution animations from modular pieces based on enemy type, player weapon, environment, and relationship history. GDC 2015 and 2018 talks cover the cinematic camera system blending with procedural Nemesis cameras, and the multi-character combat animation pipeline.

**Motion Graphs (Kovar, Gleicher, Pighin, SIGGRAPH 2002)**: Directed graphs over motion capture data for creating realistic, controllable motion. The theoretical ancestor of modern motion matching systems.

### 4.8 Fighting Game Animation Architecture

Fighting game animation systems are **hierarchical state machines with resource-gated transition overrides**:

1. **Base layer**: HFSM managing character state (standing, crouching, jumping, etc.)
2. **Cancel graph**: Directed graph defining which states can transition to which, subject to frame-data timing
3. **Resource gates**: Meter-spending mechanics (Roman Cancel, supers) that unlock non-standard transitions
4. **Sync points**: Multi-character states (throws, cinematic supers) requiring coordination

**Arc System Works' limited animation** (GDC 2015): Guilty Gear Xrd achieves "anime-quality" by deliberately skipping interpolation frames in 3D animation to emulate hand-drawn 2D feel. Character models: 2 months; animation: 2 months.

The cancel graph is a concrete instance of **real-time choreographic branching**: at every frame, the graph defines possible continuations, and player input selects the branch.

---

## Part 5: Cross-Cutting Design Patterns

### 5.1 Common State Machine Structure

All combat cinematic systems share a common underlying pattern:

```
GAMEPLAY_STATE
    |
    +-- [compound predicate over state variables]
    |
    v
TRANSITION_STATE (super freeze, hitstop, screen flash)
    |
    v
CINEMATIC_STATE
    |
    +-- [optional: continuation point / branch point]
    |   +-- Player input -> BRANCH_A
    |   +-- Timeout -> DEFAULT_FLOW
    |   +-- Opponent input -> COUNTER_BRANCH
    |
    v
EXIT_STATE (damage resolution, position update, state restoration)
    |
    v
GAMEPLAY_STATE (or MATCH_END)
```

### 5.2 Choreography Composition Taxonomy

| Approach | Examples | Content Volume |
|----------|----------|---------------|
| **Fixed Linear** | Shenmue, RE4, Asura's Wrath | 1x per sequence |
| **Fixed Branching** | Heavy Rain, Detroit | 2-4x per sequence (mocap multiplier) |
| **Contextual Selection** | GoW 2018, Shadow of Mordor | N animations x M contexts |
| **Performance-Scaled** | DMC, Bayonetta | N components x M style levels |
| **Rule-Based Procedural** | AC Odyssey dialogue | Rule-authored, unbounded output |
| **GOAP-Planned Procedural** | (Bannou's CinematicStoryteller target) | Theoretically unbounded |

Production cost scales dramatically: Fixed Linear is cheapest; Fixed Branching multiplies mocap costs; Contextual Selection requires large animation libraries; Performance-Scaled requires layered VFX systems; Procedural trades production cost for engineering cost.

### 5.3 Failure Handling Taxonomy

| Pattern | Examples | Design Intent |
|---------|----------|---------------|
| **Death/Restart** | Shenmue, RE4, GoW 2005 | Maintain tension through stakes |
| **Degraded Outcome** | RE5 (damage), Asura's Wrath (lower score) | Allow completion while rewarding mastery |
| **Alternative Path** | Heavy Rain, Detroit | Reinforce meaningful choice |
| **Graceful Scaling** | DMC/Bayonetta (lower rank) | Reward mastery with spectacle |
| **No Failure State** | GoW 2018 (earned executions) | Skill test is in earning the opportunity, not executing it |

### 5.4 Multi-Entity Synchronization Approaches

| Approach | Used By | Mechanism |
|----------|---------|-----------|
| **Attacker-driven, victim locked** | MK Fatalities, FighterZ Dramatic Finish | Victim enters passive receiving state |
| **Physics-driven hand-off** | DOA stage transitions | Knockback physics + stage geometry determine response |
| **Symmetric paired animation** | Wrestling grapples (AKI, Fire Pro, WWE 2K) | Matched-frame synchronized export |
| **Symmetric competitive** | Beam Struggle, Dragon Rush | Both animate simultaneously; input determines outcome |

### 5.5 Key Design Principles

1. **Uncertainty sustains attention** (Mikami, RE4): All cutscenes must be real-time so players never know when a QTE is coming. The uncertainty creates sustained engagement.

2. **Failure should produce valid state** (Cage, Heavy Rain): "Game over is a failure of game design." Every QTE outcome should be valid narrative, not a broken state requiring retry.

3. **Performance quality should modulate spectacle** (Kamiya/Itsuno, DMC/Bayonetta): Instead of testing the player during cinematics, let combat creativity drive cinematic quality. Inverts the QTE relationship.

4. **Three-beat formula** (NetherRealm, MK): Setup -> escalation -> climax structures each triggered cinematic. A rhythmic principle applicable to any choreographed combat sequence.

5. **QTEs have diminishing returns** (RE6 cautionary tale): When every interaction is a QTE, the mechanic loses its capacity for emphasis. QTEs work as punctuation, not grammar.

6. **Progressive escalation gates spectacle** (Fire Pro, AKI wrestling): Stronger/more dramatic cinematics are gated by accumulated gameplay state. The player earns access to more spectacular sequences.

---

## Part 6: Relevance to Bannou's Cinematic System

### 6.1 Confirmed Architectural Novelty

The research confirmed four aspects of the [CINEMATIC-SYSTEM.md](CINEMATIC-SYSTEM.md) design that appear to have no published precedent:

**GOAP for choreographic composition**: GOAP is well-established for NPC behavior (Orkin, F.E.A.R. 2006) and narrative planning is well-studied (Young et al., 2013; Riedl & Bulitko, 2013), but applying GOAP specifically to combat choreography composition -- planning dramatic beat sequences from participant capabilities, spatial affordances, and dramatic context -- is unexplored in published literature.

**Continuation points with timeout defaults**: The CS concept of delimited continuations and the game design concept of QTEs have never been formally connected in the literature. The `CinematicInterpreter`'s continuation points with timeout-based default flows are a principled formalization of what other systems implement ad hoc. Every system studied has some form of "pause and branch or continue" logic, but none formalize it as a first-class concept with explicit timeout semantics.

**Progressive agency as continuous variable**: No published system implements a continuous float controlling QTE density within a fixed choreographic sequence. FF16's cinematic strikes are binary. Fighting game cancel windows are fixed per move. The concept that the same choreographic computation produces different interaction densities based on a continuous fidelity value is novel.

**Theory/Storyteller/Plugin as repeatable pattern**: While individual layers have academic equivalents (film idioms ~ Theory; drama managers ~ Storyteller; ABL execution ~ Runtime), the explicit three-layer pattern applied to multiple creative domains (music, narrative, choreography) with identical architecture appears unique.

### 6.2 Validated Design Decisions

The research validates several CinematicStoryteller design choices:

- **GOAP actions map naturally to dramatic beats**: The combat trigger research shows that choreographic sequences decompose into discrete beats (exchange, pause, environmental interaction, group action) -- exactly the GOAP action set proposed in CINEMATIC-SYSTEM.md.
- **Spatial reasoning is essential**: Every system studied (except pure QTEs) considers spatial context. DOA's entire system is position-dependent. MK's stage fatalities require position. Wrestling games' move palettes change based on relative position.
- **Capability matching prevents impossible choreography**: Wrestling games distinguish weak/strong moves by accumulated damage state. Bayonetta's escalation is gated by meter. These enforce "don't choreograph what can't happen."
- **The timeout-default pattern handles graceful degradation**: When the player doesn't interact, the system needs a valid continuation. This is exactly the continuation point timeout pattern.
- **The progressive agency gradient maps to established precedent**: Fire Pro's progressive escalation (weak grapples first, strong later) and Bayonetta's layered hierarchy (Witch Time -> Wicked Weaves -> Torture Attacks -> Climax) demonstrate that audiences accept graduated interaction density.

### 6.3 The Core Unsolved Problem

The single largest open problem across all seven research areas is **automated generation of cinematically satisfying choreography** that is simultaneously:

1. **Spatially valid** -- respects environment geometry
2. **Capability-consistent** -- respects participant abilities
3. **Dramatically structured** -- has tension arc, climax, resolution
4. **Cinematographically directed** -- camera placement serves the drama
5. **Interactively composable** -- supports player agency at variable density

Current systems achieve subsets:
- Shadow of Mordor: (1) + (2) via modular selection from pre-authored libraries
- AC Odyssey: (4) via rule-based procedural generation
- Drama managers: (3) via narrative planning
- No system achieves all five simultaneously

This is exactly the gap CinematicStoryteller targets.

---

## Part 7: Source Bibliography

### GDC Talks

- Ethan Walker, "Embracing your Narrative Nemesis: Cinematic Storytelling in Middle-earth: Shadow of Mordor" (GDC 2015)
- John Piel & Camille Chu, "Animation Bootcamp: Middle-earth: Shadow of War: Creating Multi-Character Combat Animations" (GDC 2018)
- Francois Paradis, "Procedural Generation of Cinematic Dialogues in Assassin's Creed Odyssey" (GDC 2019)
- Jeff Orkin, "Three States and a Plan: The A.I. of F.E.A.R." (GDC 2006)
- Chris Conway, "Goal-Oriented Action Planning: Ten Years Old and No Fear!" (GDC 2015)
- Junya C. Motomura, "Guilty Gear Xrd's Art Style: The X Factor Between 2D and 3D" (GDC 2015)
- David Jaffe, GDC 2006 Interview (God of War design philosophy)
- Cory Barlog, "Evolving God of War's Combat" (GDC 2019)
- Bruno Velazquez, "Animation Bootcamp: God of War: Breathing New Life into a Hardened Spartan" (GDC 2019)
- Shinji Mikami, "Real-Time 3D Movies in Resident Evil 4" (GDC 2005)
- David Cage, "Creating an Emotional Rollercoaster in Heavy Rain" (GDC 2011)
- David Cage, "The Peter Pan Syndrome" (D.I.C.E. Summit 2013)
- Neil Druckmann & Bruce Straley, "Creating The Active Cinematic Experience of Uncharted 2" (GDC 2010)
- Daniel Holden, "Character Control with Neural Networks in Assassin's Creed" (GDC 2018)
- Jonathan Cooper, "Animation Bootcamp: Animating The 3rd Assassin" (GDC 2013)
- "The Next Generation of Fighting Games: Physics & Animation in UFC 2009 Undisputed" (GDC 2009)

### Seminal Academic Papers

- He, Cohen, Salesin, "The Virtual Cinematographer: A Paradigm for Automatic Real-Time Camera Control and Directing" (SIGGRAPH 1996)
- Kovar, Gleicher, Pighin, "Motion Graphs" (SIGGRAPH 2002)
- Mateas & Stern, "Structuring Content in the Facade Interactive Drama Architecture" (AIIDE 2005)
- Riedl & Bulitko, "Interactive Narrative: An Intelligent Systems Approach" (AI Magazine 34(1), 2013)
- Riedl & Young, "Narrative Planning: Balancing Plot and Character" (JAIR)
- Lino, Christie, Lamarche, Guy, Olivier, "A Real-time Cinematography System for Interactive 3D Environments" (SCA 2010)
- Mawhorter et al., "Towards a Theory of Choice Poetics" (FDG 2014)
- Young, Ware, Cassell, Robertson, "Plans and Planning in Narrative Generation" (2013)
- Porteous, Cavazza, Charles, "Applying Planning to Interactive Storytelling" (ACM TIST 2010)
- Cardona-Rivera et al., "The Story So Far on Narrative Planning" (ICAPS 2024)
- Klevjer, "In Defense of Cutscenes" (2002)
- Halper, Helbing, Strothotte, "A Camera Engine for Computer Games" (Eurographics 2001)
- Magerko, "A Comparative Analysis of Story Representations for Interactive Narrative Systems" (AIIDE 2007)
- Wardrip-Fruin et al., "Player Agency in Interactive Narrative: Audience, Actor & Author" (2016)
- Elson & Riedl, "A Lightweight Intelligent Virtual Cinematography System for Machinima Production" (AIIDE 2007)
- Burelli, "Game Cinematography: From Camera Control to Player Emotions" (Springer 2016)
- "Cine-AI: Generating Video Game Cutscenes in the Style of Human Directors" (CHI PLAY 2022)
- "Breaking the Shackles: Toward a Taxonomy of Interactive Cinema" (MediAsia 2019)
- "Implementation of Hierarchical Finite State Machine for Controlling 2D Character Animation" (IEEE 2023)
- "SceneCraft: Automating Interactive Narrative Scene Generation with LLMs" (AIIDE 2023)

### Books and Reference Works

- Robert Nystrom, *Game Programming Patterns* -- "State" chapter (pushdown automata for animation)
- Chris Crawford, *Chris Crawford on Interactive Storytelling* (2nd Edition, 2012)
- Mateas, "A Behavior Language for Story-Based Believable Agents" (AAAI Spring Symposium, 2002)
- Orkin, "Applying Goal-Oriented Action Planning to Games" (2004)

### Game-Specific Design Analysis

- NetherRealm Studios, "Designing Mortal Kombat's Famously Bloody Fatalities" (Game Developer)
- Andi Hamilton, "How AKI Corp's Wrestling Games Captured the Spirit of Pro Wrestling" (Medium)
- CritPoints, "Hitstop/Hitfreeze/Hitlag" (2017) -- Fighting game micro-transition analysis
- Stinger Magazine, "Ranking Systems in Action Games" -- DMC/Bayonetta scoring frameworks
- Game Design After Hours, "RE4's Knife Fight" -- QTE encounter design analysis

---

*This document captures precedent research for the cinematic composition system described in [CINEMATIC-SYSTEM.md](CINEMATIC-SYSTEM.md). It is a reference, not a specification -- the architectural decisions live in CINEMATIC-SYSTEM.md.*
