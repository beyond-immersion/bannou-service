# Compositional Cinematics: The Anime Production Paradigm for Real-Time 3D

> **Type**: Architectural Analysis
> **Status**: Aspirational
> **Created**: 2026-03-09
> **Last Updated**: 2026-03-09
> **North Stars**: #1, #2, #5
> **Related Plugins**: Cinematic, Behavior, Actor, Puppetmaster, Agency, Save-Load, Scene, Mapping

## Summary

Analyzes how the anime production paradigm of decomposing complex scenes into independently-produced layers with shared spatial constraints maps onto Bannou's cinematic architecture. Fingerprinted behavior components serve as character cels, continuation points as composition seams, and CutsceneSession sync barriers as same-layer synchronization moments. Extends the paradigm to distributed scene sourcing where multiple servers simultaneously simulate different scene versions for instantaneous flashbacks, perspective splits, and temporal montage. No new systems are proposed; this is a unifying architectural lens on planned and existing systems including lib-cinematic, lib-behavior, lib-actor, and the Video Director.

---

## Executive Summary

Anime studios discovered a production principle through decades of budget-constrained filmmaking: **decompose multi-character scenes into independently-produced layers that share spatial constraints, then composite them together**. Characters on separate transparent cels (A/B/C layers) are animated by different artists, on different schedules, using the same layout (vanishing points, perspective grid, horizon line). Direct character-to-character physical contact -- the "same layer" moment -- is the expensive exception, not the default. Strategic camera cuts hide the boundaries between independent layers. The result is cinematic production where the default is independence and synchronization is earned through dramatic necessity.

This document argues that Bannou's existing and planned architecture -- behavior composition, the cinematic system, the Video Director, and the distributed monoservice deployment model -- converges on the same production paradigm, but applied to real-time 3D with procedurally-generated performances from actual game world data. Further, the distributed architecture enables an extension that even anime cannot achieve: **multiple servers simultaneously simulating different versions of the same scene** (present/past, victor/loser perspective, reality/memory), composited by the client in real-time.

This is not a new system to build. It is a unifying lens on systems that already exist or are planned, plus a distributed extension that falls naturally out of the monoservice architecture.

---

## Part 1: The Anime Production Pipeline

### 1.1 How Anime Layer Separation Actually Works

In anime production, each continuous camera shot is called a **cut**. Each cut is decomposed into multiple transparent layers:

- **Background art**: Painted or digitally composed environment (the "stage")
- **A cel**: Bottommost character layer, placed directly over the background
- **B cel**: Next character layer, on top of A
- **C cel and beyond**: Additional layers as needed

Different parts of the **same** character are also split across layers -- hair, mouth, eyes, and body can be on separate cels so only the moving element needs new drawings. A character who is "listening" while another speaks may have only their mouth layer (lip flaps) and eye layer (blinks) updated, while everything else is a held drawing.

The critical production insight: **characters that don't physically touch can be animated entirely independently**. Two characters talking in the same scene can be drawn by different animators, on different schedules, with different levels of quality investment. The shared **layout** drawing provides the spatial contract -- vanishing points, horizon line, scale references -- ensuring everything looks correct when composited.

The compositing stage is called **satsuei** (literally "photography"). Originally done with a physical animation stand and camera, now done digitally in After Effects, RETAS Studio, or similar tools. The compositor merges layers and adds camera movements, lighting, depth-of-field, and effects. Much of what viewers perceive as "animation" is actually photography work -- a character appears to walk forward because the background layer is pulled behind them.

### 1.2 How This Minimizes Synchronization Cost

Physical contact between characters (hand-holding, grappling, one carrying another) requires both characters to be on the **same cel layer** -- their outlines must match frame-for-frame. This is expensive because it cannot be parallelized or reused.

Anime production systematically minimizes these moments:

| Technique | Purpose |
|---|---|
| **Camera cuts** | Cut to a close-up of one character before contact, cut away after -- avoid showing the actual moment of interaction |
| **Implied staging** | Characters face each other but never touch -- framing implies closeness without requiring same-layer animation |
| **Cycling animations** | Walking, running, breathing -- reusable loops that run independently per character |
| **Bank animation** | Stock sequences (transformation scenes, recurring attacks) stored and reused across episodes |
| **Reaction shots** | Cut to a character's face reacting rather than showing the action that caused the reaction |

The high-quality "sakuga" moments -- fluid, detailed animation where characters interact directly -- are surgically placed at dramatic peaks. They stand out precisely because the surrounding material is efficient limited animation. Studios like Ufotable (Demon Slayer) and Kyoto Animation push the ratio of sakuga to limited animation higher, but even their work follows the layer-separation production model.

### 1.3 The Film Coverage Analog

Live-action filmmaking uses the same principle through **coverage**: shooting the same scene from multiple angles independently, then assembling the final sequence in editorial.

- **Master shot**: Wide, uninterrupted take showing all characters
- **Close-ups**: Isolated shots of individual characters
- **Over-the-shoulder**: One character framed from behind the other's shoulder
- **Insert shots**: Detail shots of objects, hands, props

Each angle is captured independently. Character A's close-up doesn't need to be filmed simultaneously with Character B's. The editor assembles the final scene by selecting the best take and angle for each moment, cutting between independent pieces that share spatial consistency (same set, same lighting, same blocking).

The "cutting room floor" concept means filmed segments are intentionally designed as **rearrangeable building blocks**. A director who films with good coverage gives the editor maximum flexibility to reorder, emphasize, and pace the scene in post-production.

### 1.4 The Game Industry Gap

Traditional game cinematics are fundamentally linear: script a sequence, place characters, choreograph simultaneous animations, set camera waypoints, play it back. Every character is part of one unified simulation. You cannot "cut to coverage" because there is no coverage -- there is one timeline with all characters animated together.

The closest industry precedent is **Assassin's Creed Odyssey** (GDC 2019), which procedurally generated cinematic dialogue scenes by treating characters as independently animatable entities with procedural camera coverage. But AC Odyssey focused on camera composition over pre-authored dialogue animations, not procedural character performance selection.

Academic work on "virtual cinematography" (Galvane 2015, Cine-AI 2022) encodes film grammar as hierarchically organized state machines for automatic camera control. These systems implement the coverage concept algorithmically but still assume a single unified simulation.

**What no one has done**: treat the character performances themselves as independently producible and compositable building blocks, selected from a library via GOAP planning, linked via continuation point seams, with synchronization applied surgically at dramatically motivated moments. That is what Bannou's architecture enables.

---

## Part 2: The Bannou Mapping

### 2.1 Pipeline Correspondence

| Anime Stage | Bannou Equivalent | Component |
|---|---|---|
| **Layout** (perspective, scale, layer assignments) | Scene document + Mapping spatial data (shared spatial context for all characters) | lib-scene, lib-mapping |
| **Key animation** (individual character drawings on separate cels) | Fingerprinted behavior components (each character's performance is self-contained ABML bytecode) | Behavior Composition registry |
| **In-between animation** (filling movement between key frames) | Continuation points with default flows (graceful fill between authored moments) | CinematicInterpreter |
| **Bank animation** (reusable stock sequences) | Plan cache + component registry (cached compositions reused across similar scenarios) | PlanCache, BehaviorModelCache |
| **Satsuei / Photography** (compositing layers, camera, lighting, effects) | CutsceneSession + CinematicRunner + Video Director (assembling independent performances into unified output) | lib-cinematic, lib-video-director |
| **Editorial** (selecting cuts, ordering sequences, timing) | GOAP-driven component selection + beat-sync scheduling + role-based screen time budgets | CinematicStoryteller, Video Director |
| **Sakuga** (high-quality synchronized animation at dramatic peaks) | CutsceneSession sync barriers (the rare moments of true multi-character synchronization) | CutsceneCoordinator |

### 2.2 Role-Based Composition IS Layer Separation

VIDEO-DIRECTOR.md's ensemble role model already encodes the anime layer principle using music vocabulary:

| Anime Layer Concept | Video Director Role | Behavioral Implication |
|---|---|---|
| Full animation, same cel (both characters drawn together, frame-matched) | Protagonist + Counterforce in full C.R.A. choreography | CutsceneSession sync barrier required -- expensive, used at dramatic peaks |
| Limited animation, separate cel (cycling walk, lip flaps, held poses) | Ensemble / Atmosphere roles with low action density | Independent behavior components -- no sync needed |
| Bank animation (reusable stock) | Cached fingerprinted components from plan cache | Same combat exchange component reused for similar NPCs |
| Match cuts between layers | Continuation points between components | Composition seam where one character's segment ends and another begins |
| Camera determining which layer is "in focus" | Role-based screen time budgets and camera idiom selection | Protagonist gets close-ups, Ensemble gets medium shots, Atmosphere gets wide-only |

### 2.3 The Independence Default

The critical insight from anime production, applied to Bannou:

**Most of a cinematic sequence requires zero synchronization between characters.**

Consider a 30-second scene with two characters in a tavern:

```
Component A (3s): Roy close-up, speaking
  Roy: fingerprinted "dialogue_earnest" component (full animation)
  Marth: fingerprinted "listening_attentive" component (blinks, slight nods)
  Sync required: NONE (separate "cels", cut hides boundaries)
    │ continuation_point: "dialogue_beat_1"
    ▼
Component B (3s): Marth reaction shot
  Marth: fingerprinted "reaction_emotional" component (full animation)
  Roy: fingerprinted "idle_seated" component (held pose, breathing cycle)
  Sync required: NONE
    │ continuation_point: "emotional_shift"
    ▼
Component C (4s): Wide establishing shot
  Both characters: independent idle components, minimal action
  Camera: Toric Space two-target framing
  Sync required: NONE (spatial context only)
    │ continuation_point: "action_trigger"
    ▼
Component D (5s): Roy grabs Marth's shoulder -- SAKUGA MOMENT
  Both characters: shared "physical_contact_shoulder_grab" component
  Sync required: YES (CutsceneSession sync barrier -- same "cel layer")
    │ continuation_point: "post_contact"
    ▼
Component E (3s): Roy close-up, emotional aftermath
  Roy: fingerprinted "emotional_aftermath" component
  Marth: not visible (camera framing excludes)
  Sync required: NONE
```

**4 out of 5 components need zero synchronization.** Characters share the scene but their performances are independent. Camera cuts hide the seams. Only the physical contact moment (Component D) requires a CutsceneSession sync barrier -- the "same cel layer" moment.

This ratio (80%+ independent, <20% synchronized) mirrors actual anime production ratios. Even in action-heavy sequences, the moments of true physical contact are brief -- most "action" is characters attacking independently with camera cuts between them.

### 2.4 The Combinatorial Consequence

In anime, each cel is drawn once for a specific scene. Layer separation saves production cost but doesn't generate new content.

In Bannou, because components are fingerprinted, GOAP-selected, role-assignable, and continuation-point-linked, the combinatorial space explodes:

- **200 combat exchange components** in the registry, each hand-crafted for cinematic quality
- **50 dialogue components** per emotional register
- **30 transition/reaction components**
- Different character selections (personality drives component selection via `${personality.*}`)
- Different music drives different timing (dramatic strings vs. upbeat pop)
- Different environments provide different spatial affordances (Mapping queries)
- Different life stage archives provide different flashback material

The same 5-component tavern scene structure produces a **completely different cinematic** with different characters, different music, different dialogue components, different environmental staging. No combination is pre-authored -- each is emergently composed from the component registry.

---

## Part 3: Distributed Scene Sourcing

### 3.1 Beyond Single-Server Composition

Everything in Part 2 operates within a single server's simulation. But Bannou's distributed monoservice architecture enables a more powerful extension: **multiple servers simultaneously simulating different scenes, composited by the client**.

This is not a new infrastructure concept. It is the natural extension of three things that already exist:

| Existing Mechanism | Cinematic Extension |
|---|---|
| **Orchestrator processing pools** (spin up ephemeral workers for Actor NPC brains) | Spin up ephemeral **scene servers**, each simulating one scene/location/time period |
| **Seed switching** (player in void selects which seed to "drop into", each potentially on different server) | Cinematic system selects which scene feed to "cut to", each on a different server |
| **Connect multi-node relay** (WebSocket mesh between Connect instances) | Client maintains connections to multiple scene servers, receives multiple feeds |
| **Agency UX manifest** ("what can this spirit perceive right now") | "Which scene feed is the client rendering right now" -- same gating mechanism, cinematic-driven trigger |

### 3.2 Three Levels of Distributed Composition

#### Level 1: Cross-Location Cuts

Two characters in different places. Two different servers. Client cuts between feeds.

```
Server A: Village yard -- Roy doing sword practice
Server B: Dungeon floor 7 -- Marth fighting the boss

Video Director: Beat-sync cuts between feeds
  Verse: Roy's routine practice (calm, rhythmic)
  Chorus: Marth's desperate combat (intense, chaotic)
  Bridge: Split-screen or rapid intercuts
  Climax: Roy "senses" something wrong (perception injection)
          Marth falls (CinematicRunner completes)
```

Each server runs its scene independently. Zero synchronization between servers. The emotional connection comes from editorial juxtaposition, not simultaneous action. This is the easiest case -- two completely independent "cel layers" over different backgrounds.

#### Level 2: Same-Location, Different Cameras

Both characters in the same tavern, single server. The anime paradigm applied locally: independent behavior components per character, camera cuts providing the compositional framework.

This is the tavern example from Part 2. The server runs one simulation, but each character's behavior is an independent component. The CinematicStoryteller selects components per character based on their role assignment and the Video Director handles camera placement and cut timing.

#### Level 3: Same-Location, Different TIME

This is the breakthrough that distributed architecture uniquely enables. Two servers running the **same physical space** at different points in time:

```
Server A (Present, 1067): Tavern -- old bartender, renovated interior,
                          rain outside, Roy drinking alone

Server B (Past, 1042):    Tavern -- young bartender (same person),
                          original decor, summer evening,
                          Roy and Marth laughing together

Client composites by cutting between feeds:
  Old bartender's face → young bartender's face (match cut)
  Empty chair across from Roy → Marth sitting there (spatial match)
  Rain on window → sunset through window (environmental contrast)
```

#### Why Flashbacks Are Hard in Traditional Games

Flashbacks require loading different versions of the same environment: different assets, different character models (younger versions), different weather, different lighting, different NPC states. In a single-engine architecture, this means:

1. Unload the present-day scene
2. Load historical assets
3. Swap character models to younger versions
4. Change weather and lighting
5. Play the flashback
6. Reverse everything to return to present

The loading screens break emotional momentum. The asset swapping creates visible hitches. The complexity discourages developers from using flashbacks at all, which is why they are rare in games despite being a fundamental storytelling tool in film and anime.

#### Why Distributed Sourcing Solves This

With parallel servers, both versions exist simultaneously:

- **Server A** runs the present-day scene continuously
- **Server B** boots from a historical state snapshot (via Save-Load service) and runs the past version
- The client receives both feeds
- The cut between present and past is **instantaneous** -- no loading, no asset swapping
- Emotional momentum is preserved because the transition is a camera cut, not a scene load

The flashback server is an ephemeral processing pool worker:

```
Orchestrator
  │
  ├── Main game server (present day, persistent)
  │
  └── Processing pool: "flashback workers"
      ├── Flashback Server A: loads save snapshot from 1042
      │   - Save-Load provides versioned state
      │   - Resource archives provide character appearance/equipment at that age
      │   - Worldstate provides historical time/season/weather
      │   - Runs just long enough for the cinematic segment
      │   - Released back to pool when flashback ends
      │
      └── Flashback Server B: (available for next flashback request)
```

### 3.3 Extended Applications of Parallel Simulation

Same-location-different-time is the most immediately useful case, but the principle extends further:

| Application | Server A | Server B | Compositional Effect |
|---|---|---|---|
| **Flashback** | Present-day scene | Historical snapshot of same location | Match-cut temporal transitions |
| **Perspective split** | Battle from victor's perspective (heroic staging, warm lighting) | Same battle from loser's perspective (chaotic, desperate) | Intercut to show both sides of a conflict |
| **Memory distortion** | Objective event as it happened | Character's *memory* of the event (filtered through personality -- coward remembers enemies larger, braggart remembers own role as central) | Unreliable narrator for storytelling |
| **Prophetic vision** | Current reality | God-actor's speculative simulation of a possible future | Character sees consequences before they choose |
| **Dream sequence** | Waking world | Surreal remix of real locations and characters from archives | Disposition/personality-driven environmental distortion |
| **Parallel lives** | Seed A's character in Location X | Seed B's character in Location Y | Cross-seed cinematic showing divergent paths |

### 3.4 The Agency Connection

The switching mechanism between scene feeds is structurally identical to existing seed/character switching:

```
Player experience:
  Void → select Seed A → drop into Server X
  Void → select Seed B → drop into Server Y
  (Agency manages which feed the client renders)

Cinematic experience:
  Scene 1 → cut to flashback → return to present
  Scene 1 → cut to different location → return
  (Video Director manages which feed the client renders)
```

The Agency service already manages the UX capability manifest -- "what can this spirit perceive right now." For cinematics, the manifest temporarily becomes "you are watching a directed sequence; here are the available feeds and the current feed is X." The switching infrastructure is the same; only the trigger changes from player choice to cinematic direction.

### 3.5 Streaming and Broadcast Integration

For live streaming (Broadcast + Showtime services), distributed scene sourcing creates a multi-camera live production setup:

- **Multiple "camera feeds"** from different scene servers, available simultaneously
- **Broadcast service** coordinates which feed goes to the streaming platform
- **Showtime service** hype mechanics could influence which scene gets screen time
- **Director plugin** provides the human-in-the-loop override for selecting between feeds during live events

A developer orchestrating a live streamed event could have:
- Server A: the main event happening in the arena
- Server B: the crowd reaction in the tavern watching the event
- Server C: flashback to the rivals' childhood training together
- Cut between them in real-time, synced to procedurally generated music

---

## Part 4: The Unifying Principle

### 4.1 Independence Is the Default; Synchronization Is Earned

The anime industry's core lesson, applied across all levels of the architecture:

| Level | Independence (Separate Cels) | Synchronization (Same Cel) |
|---|---|---|
| **Single character** | Body, mouth, eyes, hair on separate layers (only moving parts need new frames) | Full-body animation (expensive, used for dramatic movement) |
| **Multi-character, single server** | Each character runs independent behavior components | CutsceneSession sync barriers for physical contact |
| **Multi-character, multi-server** | Each scene simulated on separate server | Cross-server sync points for match-cuts and temporal alignment |
| **Multi-scene composition** | Each scene is an independent production unit | Video Director beat-sync coordinates scene boundaries |

At every level, the design optimizes for independence and treats synchronization as a surgical intervention at dramatically motivated moments.

### 4.2 Camera Cuts Hide the Seams

In anime, cuts between different cel layers are invisible because the compositing is seamless. In Bannou:

- **Within a server**: Camera cuts between character close-ups hide the fact that each character is running an independent behavior component. You never see the transition from "active" to "background" because the camera has already moved.
- **Between servers**: Feed switches are camera cuts. The client stops rendering Server A's output and starts rendering Server B's. From the viewer's perspective, this is indistinguishable from a normal camera cut within a single scene.
- **Across time**: Match-cuts between present and flashback are visually continuous (same spatial position, different temporal state) because both servers share the same scene geometry and character positioning constraints.

### 4.3 Quality Investment Follows the Anime Model

In anime, production budget is concentrated on sakuga moments (key animation) while everything else uses efficient limited animation. The Bannou equivalent:

| Anime Quality Tier | Bannou Equivalent | When Used |
|---|---|---|
| **Sakuga** (full animation, high frame count, senior animators) | Full CutsceneSession-synchronized choreography, custom-authored components | Direct character interaction, climactic combat, emotional peaks |
| **Standard animation** (competent but efficient, adequate frame count) | GOAP-selected components from registry, moderate action density | Dialogue scenes, exploration, moderate-intensity action |
| **Limited animation** (held cels, lip flaps, cycling, bank footage) | Cached plan components, cycling idle behaviors, reaction-shot components | Background characters, atmosphere, non-focal moments |

The component registry naturally stratifies by quality: high-quality "sakuga" components cost more in the GOAP planner (longer duration, more sync barriers, more spatial requirements), so the planner only selects them when the dramatic context justifies the cost. Routine moments get efficient cached components. This mirrors how anime directors allocate their animation budget -- the planner IS the director, making resource allocation decisions.

---

## Part 5: Relationship to Other Planning Documents

This document does not introduce new systems. It provides a unifying lens on planned systems:

| Document | What It Defines | How This Document Relates |
|---|---|---|
| **BEHAVIOR-COMPOSITION.md** | Fingerprinted components, plan cache, composite assembly | These are the "cels" -- independently produced, content-addressed, GOAP-selected |
| **VIDEO-DIRECTOR.md** | Music-driven cinematic generation, role-based composition | This is the "satsuei" stage -- compositing independent performances into unified output |
| **CINEMATIC-SYSTEM.md** | CinematicInterpreter, continuation points, CutsceneSession | This is the runtime -- continuation points are composition seams, sync barriers are "same layer" moments |
| **BANNOU-EMBEDDED.md** | Standalone Bannou for offline content creation | This is the "production studio" -- running the full stack for content creation |

### What This Document Adds

1. **The anime production paradigm as explicit design principle**: Independence is the default; synchronization is the exception earned through dramatic necessity. This should inform all cinematic component authoring and GOAP cost tuning.

2. **Distributed scene sourcing**: Multiple servers simulating different scenes (or different versions of the same scene) simultaneously, composited by the client. This is the architectural extension that enables flashbacks, perspective splits, and temporal montage without loading screens.

3. **The Agency-as-compositor model**: Agency's UX manifest mechanism, already used for seed switching and progressive perception, serves as the client-side compositor that determines which scene feed to render at any moment.

4. **Quality stratification via GOAP cost**: The planner naturally allocates "animation budget" by assigning higher GOAP costs to synchronized multi-character components and lower costs to independent single-character components. Dramatic peaks justify the cost; routine moments don't. This mirrors anime production budget allocation.

---

## Open Questions

### 5.1 Client Architecture for Multi-Feed Rendering

How does the game engine maintain multiple "scene contexts" simultaneously? Options:

- **Multiple viewports**: Each scene server's state rendered in a separate viewport, client composites them
- **Scene switching**: Client maintains state for multiple servers but renders only one at a time (the "cut" is a viewport swap)
- **Layered rendering**: Client renders background from one server, characters from another (closest to actual anime cel compositing, but most complex)

The practical answer likely depends on the engine. Unreal's Composure system supports real-time layer-based compositing. Stride and Unity would need custom implementations.

### 5.2 Latency Budget for Cross-Server Cuts

When the Video Director signals a cut from Server A to Server B, the client needs Server B's current state ready to render. Options:

- **Pre-buffering**: Client receives both feeds continuously, only renders one (bandwidth cost, minimal latency)
- **Predictive loading**: Video Director's beat-sync schedule is known ahead of time; client begins receiving Server B's feed N frames before the cut
- **Async fade**: Brief fade-to-black or dissolve transition that covers the latency of switching feeds (least technically demanding, most cinematically forgiving)

### 5.3 Historical State Fidelity for Flashbacks

How much of the historical state does a flashback server need? A perfect reproduction of "the tavern in 1042" requires:

- Character models at historical ages (asset pipeline question)
- NPC positions and behaviors from 25 years ago (not stored at that granularity)
- Environmental state (weather, season, time of day) from historical Worldstate snapshots
- Building/furniture state before renovations

Practical answer: **flashbacks are impressionistic, not photographic**. The flashback server loads a Save-Load snapshot that provides the broad strokes (which characters existed, what the location looked like, what season it was), and the rest is filled by the cinematic system's staging. This matches how film flashbacks work -- they show the *emotional truth* of the past, not a documentary recreation.

### 5.4 Interaction Between Distributed Scenes and Progressive Agency

When a cinematic cuts between multiple scene servers, does the spirit's agency level apply independently per scene? If the spirit has high combat fidelity, do they get QTE opportunities in both the present-day combat AND the flashback combat? Or does the cinematic context suppress agency (the spirit is "watching a story unfold")?

The most natural answer: agency applies to the scene the spirit is "in" (present day). Flashbacks, perspective shifts, and prophetic visions are observational -- the spirit watches but cannot intervene. This is consistent with the progressive agency model (you cannot influence events you weren't present for) and simplifies the multi-server interaction model.

### 5.5 Scale Implications

At the production-tool scale (Embedded Bannou, content creation), running 2-4 ephemeral scene servers simultaneously is trivial. At the live-game scale (100K concurrent players), triggering flashback cinematics that spin up processing pool workers has resource implications:

- How many simultaneous flashback servers can the Orchestrator pool sustain?
- Should flashback cinematics be rate-limited per realm?
- Can multiple players witnessing the same god-orchestrated event share a single flashback server?

The processing pool model already handles this for Actor NPC brains -- the same scaling mechanisms (pool size limits, queue management, auto-scaling) apply to scene servers.

---

## Industry Precedents

| System | What It Does | How Bannou Goes Further |
|---|---|---|
| **Anime cel compositing** | Independent character layers merged over shared background | Characters are GOAP-selected behavior components, not hand-drawn; composition is procedural, not manual |
| **Film coverage + editorial** | Independent camera angles assembled in post | "Coverage" is generated procedurally via camera idiom state machines; editorial is beat-synced to music |
| **AC Odyssey procedural cinematics** (GDC 2019) | Procedural camera + staging for dialogue scenes | Procedural character performance selection from a component library, not just procedural camera |
| **Unreal Composure** | Real-time layer-based compositing for mixed reality | Applied to game-world cinematics with multiple authoritative simulations |
| **Cine-AI** (CHI PLAY 2022) | Procedural camera in the style of specific directors | GOAP-driven composition of both character performances AND camera work, with music synchronization |

---

## Conclusion

The anime industry's production philosophy -- **decompose complex scenes into independently-produced layers that share spatial constraints, then composite them** -- maps precisely onto Bannou's cinematic architecture. Fingerprinted behavior components are cels. Continuation points are composition seams. CutsceneSession sync barriers are "same layer" moments. The GOAP planner is the animation director allocating quality budget. The Video Director is the photography department compositing everything together.

The distributed monoservice architecture extends this beyond what anime or film can achieve: multiple servers simultaneously simulating different versions of the same scene, enabling instantaneous flashbacks, perspective splits, and temporal montage without loading screens. The same infrastructure that handles multi-seed player experiences handles multi-feed cinematic composition -- the client already knows how to switch between different servers' authoritative states.

The unifying principle: **independence is the default; synchronization is the exception.** This should inform every design decision in the cinematic pipeline, from component authoring (minimize sync barriers) to GOAP cost tuning (synchronized components cost more) to distributed architecture (each scene server is an independent production unit).

---

*This document captures the anime compositing paradigm as a unifying design philosophy for Bannou's cinematic systems. For the component system, see [BEHAVIOR-COMPOSITION.md](BEHAVIOR-COMPOSITION.md). For the music-driven composition engine, see [VIDEO-DIRECTOR.md](VIDEO-DIRECTOR.md). For the runtime infrastructure, see [CINEMATIC-SYSTEM.md](CINEMATIC-SYSTEM.md). For the standalone production tool, see [BANNOU-EMBEDDED.md](BANNOU-EMBEDDED.md).*
