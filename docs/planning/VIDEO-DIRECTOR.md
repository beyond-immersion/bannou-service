# Video Director: Dynamic Cinematic Generation from Game Data

> **Type**: Design
> **Status**: Aspirational
> **Created**: 2026-03-07
> **Last Updated**: 2026-03-09
> **North Stars**: #5
> **Related Plugins**: Director, Music, Storyline, Actor, Cinematic, Broadcast, Showtime

## Summary

Designs a composition layer that maps musical structure onto Bannou's existing narrative, choreographic, and cinematic infrastructure to generate real-time entertainment cinematics (music videos, adventure trailers, promotional content) from game world data. The system takes a musical template, thematic intent, character selection criteria, and scene preferences, then orchestrates Storyline, CinematicStoryteller, MusicStoryteller, and the Actor runtime to produce deterministic, seed-reproducible cinematics rendered in the game engine. Not to be confused with the Director plugin (L4) which provides human-in-the-loop orchestration, though the two concepts may converge. No implementation exists yet.

---

## The Vision

A system that generates **real-time, non-pre-rendered cinematics** from game world data -- music videos, adventure montages, promotional trailers -- using the same character selection, narrative composition, and behavior execution infrastructure that drives the live game world. The output plays in the game engine like a live scene: characters move, fight, emote, and interact on real terrain with real lighting, timed to music, with procedural camera work.

The key insight: **Bannou already has every component needed for this.** Storyline composes narrative arcs. The Cinematic system choreographs action sequences. The Music system provides structurally-understood compositions. The Actor runtime executes behaviors. The Counterpoint Composer SDK provides the temporal skeleton that synchronizes everything. What's missing is the **orchestration layer that maps a song's structure onto a narrative arc onto a cast of characters onto a sequence of scenes**.

This is not a video editing tool. It is a **compositional engine** that takes:
- A musical structure (song template, composed piece, or live music)
- A thematic intent (friendship, rivalry, adventure, loss, triumph)
- Character selection criteria (relationship type, life stages, personality profiles)
- Scene/location preferences (regions, landmarks, time of day, seasons)

...and produces a fully-realized cinematic that the game engine renders in real-time.

---

## The Practical Example: "Nobody" by OneRepublic

The song that sparked this concept. "Nobody" is an upbeat, almost cartoony pop anthem about being someone's ride-or-die companion. Lyrically it borders on dark ("when you got demons trying to break through the walls", "there ain't no kind of line I wouldn't cross if you need me to") but musically it's pure sunshine -- a bromance/romance adventure song.

### Scenario A: The Fighters

Two characters selected with a `FRIEND` relationship type, both with combat-oriented `${combat.*}` profiles, lifelong bond visible in `${encounters.*}` history.

| Song Section | Game Time | Scene |
|-------------|-----------|-------|
| **Intro** (4 bars) | Childhood | Two kids sparring with sticks in a village clearing. Camera: wide establishing shot, Toric space framing both subjects |
| **Verse 1** (8 bars) | Childhood → Teen | Quick cuts: sharing meals, getting in trouble, first real wooden sword training. Energy curve rising gently |
| **Pre-Chorus** (4 bars) | Teen years | Side by side on a hilltop overlooking their village. Sunset. Camera pushes in. Tension building |
| **Chorus 1** (8 bars) | Young adult | Full combat montage -- back-to-back fighting in a dungeon, dramatic synchronized attacks, environmental affordance use (one kicks a pillar, other exploits the opening). C.R.A. choreography at peak energy |
| **Verse 2** (8 bars) | Various ages | Quieter adventure moments: camping, traveling, one patching the other's wounds, laughing in a tavern with a party |
| **Pre-Chorus** (4 bars) | Transition | Walking toward something unknown together. Camera tracking behind them |
| **Chorus 2** (8 bars) | Prime years | Bigger, more dramatic combat. Boss fight. One goes down, other stands over them protectively. Dramatic camera work (Intensify pattern -- progressively closer shots) |
| **Bridge** (8 bars) | Reflective | Quiet moments intercut: sparring as kids / sparring as adults, same moves, different scale. Split-screen or match-cut transitions |
| **Final Chorus** (8 bars) | Present day | The definitive fight sequence. Everything they've learned. Perfect synchronization. Environment-reactive choreography. Peak energy, peak dramatic tension |
| **Outro** (4 bars) | Present day | Walking away from the aftermath, same framing as the hilltop shot but older, scarred, still together |

### Scenario B: The Divergent Paths

Same song, same relationship bond, but characters who grew into different roles -- one a blacksmith, one a guard captain.

| Song Section | Scene Difference |
|-------------|-----------------|
| **Verse 1** | Shared childhood, but showing early divergence (one drawn to the forge, other to the training yard) |
| **Chorus 1** | Intercut: blacksmith hammering a blade in perfect rhythm / guard captain drilling soldiers. Same energy, parallel action |
| **Verse 2** | Meeting for dinner, drinks. Catching up. The blacksmith gives the captain a new sword -- lingering on the craftsmanship |
| **Chorus 2** | The captain fights with the blacksmith's blade. Cut to blacksmith watching from the walls. The weapon IS the bond |
| **Bridge** | Quiet: sitting together, older now, in the same tavern from childhood |

**The same system, same song template, same thematic intent** -- different character selection produces a fundamentally different cinematic. This is North Star #5 (Emergent Over Authored) applied to content generation.

---

## How It Maps to Existing Systems

Every component of Video Director maps to an existing or planned Bannou system:

### The Temporal Skeleton: Music Structure

The Counterpoint Composer SDK's **Structural Template** provides the timing backbone:

```yaml
template:
  name: "upbeat-anthem-arc"
  tempo: 120
  time_signature: "4/4"
  total_bars: 64

  form:
    - section: "Intro"
      bars: 4
      energy: 0.3
      tension: 0.2
    - section: "Verse1"
      bars: 8
      energy: 0.5
      tension: 0.3
    - section: "PreChorus1"
      bars: 4
      energy: 0.65
      tension: 0.5
    - section: "Chorus1"
      bars: 8
      energy: 0.9
      tension: 0.8
    # ... etc

  tension_curve: [0.2, 0.3, 0.5, 0.8, 0.4, 0.5, 0.9, 0.6, 1.0, 0.3]
  energy_curve: [0.3, 0.5, 0.65, 0.9, 0.5, 0.65, 0.95, 0.6, 1.0, 0.2]
```

This template is the **contract between music and visuals**. Every other system reads from it to know: "how energetic should this moment be?", "how tense?", "how many bars do I have?", "where are the phrase boundaries?"

The MusicTheory + MusicStoryteller SDKs can either:
1. **Generate music** procedurally from this template (full Bannou pipeline)
2. **Analyze existing music** to extract structural parameters (Counterpoint Composer's analysis tool)
3. **Use a pre-composed piece** with a manually-authored template that maps to its structure

### The Narrative Arc: Storyline Composition

Storyline's GOAP-based narrative planner maps directly to music structure:

| Music Concept | Storyline Concept | Mapping |
|--------------|-------------------|---------|
| Song form (ABABCB) | StorylinePlan phases | Each section = one or more narrative phases |
| Energy curve | Reagan arc type | Rising energy = Rising Action, peak = Climax |
| Tension curve | Resonator model oscillations | Tension rise-fall within sections |
| Verse (lower energy) | Exposition / Character moments | Quieter, relationship-building scenes |
| Chorus (peak energy) | Climax / Action sequences | Combat, dramatic events, big moments |
| Bridge (reflective) | Falling action / Reflection | Callbacks, parallels, emotional weight |
| Outro (resolution) | Resolution / Denouement | Emotional landing, thematic closure |

The Video Director composes a **scene plan** by:
1. Reading the music template's section boundaries and energy/tension curves
2. Requesting Storyline to compose a narrative arc that matches the template's emotional shape
3. Mapping narrative phases to music sections
4. Each mapped section becomes a **cinematic segment** with specific dramatic requirements

### The Choreography: Cinematic Composition

CinematicTheory + CinematicStoryteller handle the actual moment-to-moment choreography within each segment:

- **High-energy sections** (chorus): Full C.R.A. combat choreography, Laban Effort profiles from `${combat.*}`, environmental affordances from Mapping, dramatic camera work (Intensify pattern, Murch Rule of Six for cut timing)
- **Low-energy sections** (verse): Character interaction cinematics -- walking, talking, emoting. Camera work follows DCCL dialogue idioms (shot-reverse-shot, over-shoulder)
- **Transitional sections** (pre-chorus, bridge): Match-cut opportunities, temporal transitions (child → adult), parallel action intercuts

The key innovation: CinematicStoryteller's **dramatic planning** (Reagan arcs, Resonator model, L4D Director FSM) is already designed to match tension curves. Video Director simply provides the tension curve from the music template instead of from gameplay context.

### The Characters: Selection Pipeline

Characters are selected the same way Puppetmaster would select them for a live game event:

1. **Relationship query**: "Find two characters with relationship type FRIEND and encounter count > 50"
2. **Personality filter**: "Both should have `${personality.courage}` > 0.6" (for a brave adventure theme)
3. **Combat profile match**: "At least one should have `${combat.style}` = melee" (for action sequences)
4. **Archive richness**: "Characters with compressed archives containing > 10 history entries" (for montage material)
5. **Species/appearance**: "Same species" or "contrasting species" depending on visual theme

The Variable Provider Factory pattern provides all this data without hierarchy violations. Character archives from lib-resource provide historical material for flashback/montage composition.

### The Scenes: Location & Environment

Location, Mapping, Scene, and Environment services provide the visual context:

- **Location hierarchy**: Select regions, cities, buildings, landmarks for each scene segment
- **Mapping affordances**: "What objects exist in this location for combat choreography?"
- **Scene composition**: Node trees with mesh, marker, volume, emitter nodes for visual setup
- **Environment state**: Weather, time of day, season -- matched to emotional tone of each section
- **Worldstate time**: Game clock provides temporal anchoring for flashback sequences

### The Camera: Procedural Cinematography

CinematicTheory's camera direction system handles shot composition automatically:

- **Toric Space**: Two-target framing for character interactions (20-40% faster than optimization search)
- **DCCL Idioms**: Film grammar as hierarchical state machines (DuelIdiom for combat, DialogueIdiom for conversation)
- **Film Editing Patterns**: Shot-to-shot constraints (Intensify for building tension, Shot-Reverse-Shot for dialogue)
- **Murch Rule of Six**: Cut timing scored against emotion (51%), story (23%), rhythm (10%)
- **Beat-synced cuts**: Cut points aligned to musical bar/beat boundaries for rhythmic cohesion

### The Execution: Actor Runtime

The Actor runtime (L2) executes everything:

- **Event Brain actors** coordinate multi-character scenes (same as live game encounter coordination)
- **CinematicRunner** manages entity control acquisition and release
- **CutsceneSession** handles multi-entity sync points
- **CinematicInterpreter** executes choreographic ABML with continuation points
- **IClientCutsceneHandler** delivers actions to the game engine for rendering

---

## Architecture: The Composition Pipeline

```
                    AUTHORING LAYER (Human Selections)
                    ┌─────────────────────────────────┐
                    │  Song / Template Selection       │
                    │  Theme / Intent Selection        │
                    │  Character Selection Criteria     │
                    │  Location / Scene Preferences     │
                    │  Style Parameters (cuts/sec, etc) │
                    └──────────────┬──────────────────┘
                                   │
                    ANALYSIS LAYER │ (Understanding the Music)
                    ┌──────────────▼──────────────────┐
                    │  Structural Template Extraction   │
                    │  (Counterpoint Composer SDK)      │
                    │  Section boundaries, energy curve, │
                    │  tension curve, harmonic rhythm    │
                    └──────────────┬──────────────────┘
                                   │
                    NARRATIVE LAYER │ (Story Arc Mapping)
                    ┌──────────────▼──────────────────┐
                    │  Scene Plan Composition           │
                    │  (Storyline SDK + Video Director)  │
                    │  Music sections → narrative phases │
                    │  Character archives → scene material│
                    │  Theme → Reagan arc type           │
                    └──────────────┬──────────────────┘
                                   │
                    CHOREOGRAPHY LAYER │ (Per-Segment Detail)
                    ┌──────────────▼──────────────────┐
                    │  Segment Composition              │
                    │  (CinematicStoryteller SDK)        │
                    │  Action sequences (C.R.A.)        │
                    │  Character interactions            │
                    │  Environmental affordance use      │
                    │  Camera direction (Toric + DCCL)   │
                    └──────────────┬──────────────────┘
                                   │
                    SYNCHRONIZATION LAYER │ (Music ↔ Visuals)
                    ┌──────────────▼──────────────────┐
                    │  Beat-Level Timing                │
                    │  Cut points on bar boundaries     │
                    │  Action impacts on strong beats   │
                    │  Camera transitions on phrases    │
                    │  Energy-matched motion intensity   │
                    └──────────────┬──────────────────┘
                                   │
                    EXECUTION LAYER │ (Real-Time Playback)
                    ┌──────────────▼──────────────────┐
                    │  Actor Runtime (Event Brain)      │
                    │  CinematicRunner + Interpreter    │
                    │  CutsceneSession (multi-entity)   │
                    │  IClientCutsceneHandler → Engine   │
                    │  Real-time rendering, no pre-render│
                    └─────────────────────────────────┘
```

### The Video Director Component (New)

The new component sits between the authoring layer and the existing SDKs. It is the **mapping engine** that translates "song structure + theme + characters" into inputs that existing systems already understand.

**What Video Director owns**:
- Music-to-narrative mapping (section → phase assignment)
- Character casting (selection criteria → character resolution)
- Scene assignment (narrative phase + energy level → location selection)
- Temporal staging (which life period maps to which song section)
- Montage composition (selecting and ordering flashback/memory material from archives)
- Beat-sync coordination (ensuring cut points, impacts, and transitions align with musical beats)

**What Video Director does NOT own**:
- Music generation or analysis (MusicTheory / MusicStoryteller / Counterpoint Composer)
- Narrative arc composition (Storyline SDK)
- Combat choreography (CinematicTheory / CinematicStoryteller)
- Camera direction (Toric Space, DCCL, Film Editing Patterns)
- Behavior execution (Actor runtime, CinematicRunner)
- Character data (Character, Personality, Encounter, History services)
- Scene rendering (game engine via IClientCutsceneHandler)

### Determinism and Reproducibility

Like Music and Storyline, Video Director compositions are **deterministic when seeded**:
- Same seed + same song template + same character criteria + same theme = identical cinematic every time
- Enables caching, sharing, and replay
- Different seeds with same parameters produce variations (different combat choreography, different camera angles, different scene ordering within constraints)
- Seeds can be embedded in shareable "video codes" for social distribution

---

## Music-Cinematic Synchronization

The hardest technical problem: making visuals feel like they were choreographed to the music. This requires synchronization at three temporal scales:

### Macro Sync: Section ↔ Scene (bars)

Each music section maps to a cinematic scene type:

| Section Energy | Scene Type | Cinematic Style |
|---------------|------------|-----------------|
| 0.0 - 0.3 | Ambient / Establishing | Wide shots, slow camera, environmental beauty |
| 0.3 - 0.5 | Character / Dialogue | Medium shots, shot-reverse-shot, emotive acting |
| 0.5 - 0.7 | Action Building | Tracking shots, increasing pace, preparation montage |
| 0.7 - 0.9 | Action / Combat | Full choreography, rapid cuts, environmental interaction |
| 0.9 - 1.0 | Climax / Peak | Slow-motion impacts, dramatic camera, visual peak |

Section boundaries are **hard sync points** -- scene transitions always happen on section boundaries to maintain structural coherence.

### Meso Sync: Phrase ↔ Shot Sequence (4-8 bars)

Within each section, musical phrases map to shot sequences:
- Each 4-bar phrase = one choreographic exchange or interaction beat
- Phrase boundaries are **cut opportunities** (Murch's rhythm criterion)
- Camera idiom transitions happen on phrase boundaries
- Energy variations within a section map to shot distance (higher energy = closer framing, per Intensify pattern)

### Micro Sync: Beat ↔ Action (individual beats)

The most visceral synchronization:
- **Strong beats (1, 3)**: Impact moments (sword strikes, footfalls, dramatic gestures)
- **Weak beats (2, 4)**: Preparation and recovery (wind-up, follow-through)
- **Syncopation**: Unexpected action timing for surprise/humor (character stumbles off-beat, comedic timing)
- **Downbeat of new section**: Hard visual transition (cut, location change, time jump)

This micro-sync is what makes a music video *feel* like a music video. The CinematicStoryteller's frame data system (startup frames, active frames, recovery frames) already models action timing at this granularity -- Video Director maps these to musical beat positions.

### The Sync Point Protocol

Music and cinematics coordinate via named sync points (existing CutsceneSession infrastructure):

```
Music Channel:    ──verse──┤sync:chorus_start├──chorus──┤sync:chorus_end├──
Cinematic Channel: ──walk──┤sync:chorus_start├──fight───┤sync:chorus_end├──
Camera Channel:    ──wide──┤sync:chorus_start├──tight───┤sync:chorus_end├──
```

At each sync point, all channels are guaranteed aligned. Between sync points, each channel manages its own timing within the constraint of landing on the next sync point at the right moment.

---

## Role-Based Cinematic Composition

The Counterpoint Composer SDK's ensemble role model (Part 4C) introduces a concept that maps powerfully onto multi-character cinematics: **named roles with distinct constraint profiles that self-organize through shared structural templates**. In music, these are melody, bass, harmony pad, rhythmic counterpoint, ornamental fill. In cinematics, the equivalent is a formalized system of dramatic roles with screen time budgets, camera attention levels, and action density constraints.

### From Music Ensemble Roles to Cinematic Roles

Music ensemble roles define *how prominently* and *in what character* each voice contributes to the whole. The cinematic parallel is immediate:

| Music Ensemble Role | Cinematic Role | Constraint Profile |
|---|---|---|
| **Melody** | **Protagonist / Focus** | Most screen time, camera follows, highest dramatic action freedom, close-up permission |
| **Bass line** | **Counterforce / Dramatic anchor** | Foundational tension, drives conflict scenes, strong visual presence, grounded camera |
| **Harmony pad** | **Environment / Atmosphere** | Sustained wide shots, establishing framing, setting mood, minimal character action |
| **Rhythmic counterpoint** | **Ensemble action / Supporting cast** | Fills action gaps, complementary choreography, medium shots, syncopated screen time |
| **Ornamental fill** | **Detail / Ambient texture** | Sparse screen time, decorative visual moments, reaction shots, environmental close-ups |

Just as music roles have register (pitch range), density (note frequency), contour freedom (melodic liberty), and rhythmic character, cinematic roles have:

- **Screen time budget**: What fraction of the section this role occupies (melody = 40-60%, ornamental = 5-10%)
- **Camera distance**: Close-up permission, medium shot default, wide-only restriction
- **Action density**: How much choreographic complexity this role carries per bar
- **Dramatic freedom**: How much the role can deviate from the scene's energy baseline (melody has high freedom, harmony pad is constrained to the baseline)
- **Doubling**: Whether multiple characters can share this role (harmony and rhythmic allow doubling, melody and bass typically don't)

### The Cross-Domain Sync: Musical Prominence Drives Visual Prominence

This is where the concept becomes Video-Director-specific and goes beyond what general cinematic theory provides. The music's own role structure **directly controls** the cinematic's role structure in real time:

- When the **melody instrument solos**, the protagonist gets uninterrupted screen time -- close-up, full dramatic action
- When the **bass takes a walking run**, the cinematic shifts focus to the counterforce -- the antagonist steps forward, the opposing tension is featured
- When **harmony swells**, the camera pulls wide for environmental beauty -- the world itself becomes the subject
- When **rhythmic counterpoint fills** between phrases, supporting cast gets quick cuts of complementary action
- When **ornamental fills** decorate the top of the mix, the camera catches fleeting detail -- a reaction face, a glinting blade, a bird startled from a tree

This creates a synchronization layer **above** beat-level sync. Beat sync aligns impacts to strong beats. Role sync aligns **narrative attention** to musical prominence. The two together produce the feeling that the music and visuals are a single integrated composition -- which they are, because they share the same structural template.

### Self-Organizing Multi-Character Scenes

The ensemble model's self-organizing property (from the tavern scene in Part 4C) applies directly to cinematic casting. Characters don't need a centralized director assigning them roles -- the template IS the coordination:

1. **Shared structural template**: All characters follow the same section structure and energy curve
2. **Role assignment by fit**: Characters are cast into roles based on personality, capability, and relationship data
   - Highest `${personality.extroversion}` + most encounter history with co-star → melody/protagonist
   - Highest `${combat.aggression}` + antagonistic relationship → bass/counterforce
   - Characters with `${backstory.*}` richness but lower combat → rhythmic/ensemble
   - Locations and environment → harmony pad (automatic, not character-driven)
3. **Constraint-gated choreography**: Each character's choreographic complexity is bounded by their role's density and action freedom constraints
4. **Combinatorial variety**: Different character selections in the same roles produce different cinematics, and the template guarantees all combinations are dramatically coherent

### Role Compatibility Matrix for Cinematics

Paralleling the music ensemble's compatibility matrix:

| | Protagonist | Counterforce | Ensemble | Atmosphere | Detail |
|---|---|---|---|---|---|
| **Protagonist** | -- | Full choreography (C.R.A.) | Medium interaction | No direct interaction | Reaction shots only |
| **Counterforce** | Full choreography | -- | Medium interaction | No direct interaction | Reaction shots only |
| **Ensemble** | Medium interaction | Medium interaction | -- | Background action | No interaction |
| **Atmosphere** | No direct interaction | No direct interaction | Background action | -- | Ambient only |
| **Detail** | Reaction shots only | Reaction shots only | No interaction | Ambient only | -- |

Protagonist and counterforce need the tightest choreographic coordination (they're the melody and bass -- the harmonic framework). Ensemble characters interact with both but at lower coordination density. Atmosphere and detail are independent -- they fill the visual texture without requiring character interaction choreography.

### The "Nobody" Example with Role Assignment

Revisiting the practical example with explicit role mapping:

**Scenario A (The Fighters)**: Both characters share the protagonist role equally (a **duet** in music terms -- two melodies interlocking). This is the high-independence counterpoint model from the Rigoletto film: each character's screen time is a complete, standalone visual narrative, but when intercut, they form a unified dramatic whole.

**Scenario B (The Divergent Paths)**: One character takes the melody role, the other takes rhythmic counterpoint. The blacksmith gets sustained, lyrical screen time (harmony pad during forge scenes -- the craft IS the atmosphere). The guard captain gets action-dense screen time. When they're together, roles converge toward a shared melody. This role asymmetry is what makes the parallel-lives montage work -- it's not two identical tracks, it's two complementary voices.

**Ensemble expansion (4-6 characters)**: For a party adventure video, one character is melody (protagonist), one is bass (the rival or the anchor), two are rhythmic counterpoint (the supporting fighters), and the world/locations are harmony pad. The structural template's role definitions constrain each character's screen time and choreographic density, preventing the "too many characters, no focus" problem identified in Open Question #3.

### Addressing Open Question #3: Multi-Character Scaling

The ensemble role model directly resolves the multi-character scaling concern. A 6-character cinematic isn't 6 equal parts fighting for attention -- it's a **structured arrangement** where:

- 1-2 characters are in foreground roles (melody/bass) with ~50-60% of screen time
- 2-3 characters are in supporting roles (rhythmic counterpoint) with ~25-35%
- Environment and ambient detail fill ~10-15%

The role compatibility matrix means the choreography system only needs to coordinate tightly between 2-3 characters at any given moment, not all 6 simultaneously. This is the same insight that makes orchestras work: 80 musicians don't all coordinate with each other -- they coordinate within sections, and sections coordinate through the shared score.

---

## The Authoring Interface

Users author cinematics through selection and configuration, not scripting:

### Step 1: Select Music

| Option | Description |
|--------|------------|
| **Existing song template** | Choose from analyzed structural templates (legally safe -- uncopyrightable parameters only) |
| **Procedural generation** | MusicStoryteller generates music from emotional parameters |
| **Custom template** | Author a structural template manually for custom timing |

### Step 2: Select Theme

Themes map to Storyline's narrative goal system:

| Theme | Reagan Arc | Emotional Spectrum | Scene Emphasis |
|-------|-----------|-------------------|----------------|
| **Brotherhood** | Man in a Hole | Love-Hate | Shared trials, mutual support, parallel growth |
| **Rivalry** | Oedipus | Success-Failure | Competition, one-upmanship, grudging respect |
| **Adventure** | Rags to Riches | Success-Failure | Discovery, challenge, triumph |
| **Loss** | Tragedy | Life-Death | Memory, absence, legacy |
| **Romance** | Cinderella | Love-Hate | Meeting, separation, reunion |
| **Redemption** | Man in a Hole | Justice-Injustice | Fall, struggle, rise |
| **Legacy** | Rags to Riches | Wisdom-Ignorance | Teaching, inheritance, continuation |

### Step 3: Select Characters and Assign Roles

| Selection Mode | Description |
|---------------|------------|
| **Automatic** | System selects characters matching theme criteria and assigns roles by fit |
| **Criteria-based** | Author specifies relationship type, personality ranges, combat styles; system assigns roles |
| **Specific** | Author picks exact characters and assigns roles manually |
| **Hybrid** | Author picks protagonist(s) and assigns key roles, system fills supporting cast into remaining roles |

Character selection criteria reference Variable Provider data:
- `${personality.*}` for character personality fit and role assignment (extroversion → protagonist, aggression → counterforce)
- `${combat.*}` for action sequence compatibility and choreographic role density
- `${encounters.*}` for relationship history richness and inter-character coordination needs
- `${backstory.*}` for montage/flashback material
- `${seed.*}` for growth phase (determines character maturity/capability)

**Role assignment** (automatic or manual): Each selected character is placed into a cinematic ensemble role (Protagonist, Counterforce, Ensemble, Atmosphere, Detail) that determines their screen time budget, camera attention, and choreographic density. See [Role-Based Cinematic Composition](#role-based-cinematic-composition) for the full model.

### Step 4: Select Scenes/Locations

| Selection Mode | Description |
|---------------|------------|
| **Automatic** | System selects from character history (places they've actually been) |
| **Thematic** | Author picks environment types (forest, city, dungeon, coastline) |
| **Specific** | Author picks exact locations |
| **Temporal** | Author specifies time periods (childhood village, adult city, elder retreat) |

### Step 5: Configure Style

| Parameter | Range | Effect |
|-----------|-------|--------|
| **Cut density** | Low / Medium / High | Cuts per minute (4-8 / 10-15 / 15-25) |
| **Camera personality** | Documentary / Cinematic / Music Video | Framing style, movement, effects |
| **Action intensity** | Grounded / Heroic / Spectacular | Choreography scale, environmental interaction level |
| **Temporal structure** | Linear / Chronological Montage / Non-Linear | How life stages are ordered and intercut |
| **Transition style** | Hard Cut / Match Cut / Dissolve / Wipe | How scenes change |

### Step 6: Generate

The system composes and executes. Output is a real-time playable cinematic with:
- Full 3D rendering in the game engine
- Procedural camera work
- Music synchronized to all visual events
- No pre-rendering -- plays like a live scene
- Deterministic with seed -- same inputs reproduce same output

---

## Life Stage Montage: The Temporal Dimension

A distinguishing feature of Video Director cinematics is the ability to show characters **across their lifetimes**. This leverages Arcadia's generational cycle system and character archive infrastructure.

### How Temporal Staging Works

Character archives contain compressed history across their entire life. The Video Director can decompose a character's life into temporal segments:

```
Character Archive
├── Childhood (age 0-12)
│   ├── Location: birth village
│   ├── Key events: first friendship, first injury, family moments
│   └── Visual: child model, small weapons/tools
├── Adolescence (age 12-18)
│   ├── Location: training grounds, school, apprenticeship
│   ├── Key events: first real fight, mentor relationship, coming of age
│   └── Visual: teen model, practice equipment
├── Young Adult (age 18-30)
│   ├── Location: first adventure, different cities
│   ├── Key events: major battles, career establishment, deep friendships
│   └── Visual: adult model, real equipment
├── Prime (age 30-50)
│   ├── Location: established home, territories
│   ├── Key events: leadership, family, greatest achievements
│   └── Visual: mature model, refined equipment
└── Elder (age 50+)
    ├── Location: retreat, teaching space
    ├── Key events: mentoring, reflection, legacy
    └── Visual: aged model, symbolic items
```

The Video Director maps these temporal segments to music sections, creating montage sequences that show character growth across a lifetime in the span of a song. The **bridge** section is particularly powerful for temporal callbacks -- showing the same action (sparring, a walk, a shared meal) at different life stages with match-cut transitions.

### Match-Cut Generation

Match cuts are the most cinematically satisfying transition for temporal montage. The system identifies **action parallels** across life stages:

1. Query character archive for recurring action types across ages
2. Identify visual parallels (sparring at age 8 and age 28, same location visited at different seasons)
3. CinematicStoryteller composes the exit frame of Scene A to match the entry frame of Scene B
4. Camera position is calculated via Toric Space to maintain spatial continuity across the cut

---

## Convergence with Director Plugin

The Director plugin and Video Director share deep structural similarities:

| Aspect | Director Plugin | Video Director |
|--------|----------------|---------------|
| **Purpose** | Coordinate live game events | Generate entertainment cinematics |
| **Actor control** | Observe / Steer / Drive | Full choreographic control |
| **Character selection** | Developer picks actors to observe/steer | System selects from criteria or author picks |
| **Timing** | Real-time, event-driven | Music-driven, predetermined structure |
| **Output** | Live gameplay moments players witness | Rendered cinematic for viewing |
| **Audience** | Players in the game world | Viewers (in-game, streaming, or standalone) |

### The Unifying Insight: Live World Music Videos

The most exciting possibility: **Video Director as a triggerable in-game event.**

Imagine a developer, during a live streamed session, triggering a "music video event" where:
1. The game world doesn't pause -- it becomes the stage
2. Characters are gently steered (Director Tier 2) into choreographic positions
3. Music begins (procedural or pre-composed)
4. The cinematic system takes over camera control for the stream
5. Characters perform choreographed sequences timed to music
6. Other players nearby see the event unfold in real-time (it's happening in the world)
7. When the song ends, characters resume autonomous behavior seamlessly

This is **Director + Video Director combined**: Director's infrastructure for actor control and event coordination, Video Director's music-synchronized composition engine. The same APIs, the same actor runtime, the same camera system.

### Structural Unification

Both systems could share a common event model:

```
DirectedEvent (existing Director concept)
├── eventType: "live_orchestration" (standard Director)
├── eventType: "cinematic_video" (Video Director mode)
│   ├── musicTemplate: (structural template reference)
│   ├── theme: "brotherhood"
│   ├── castingCriteria: { ... }
│   └── syncMode: "music_driven"
├── actors: [list of participating actor IDs]
├── targetAudience: [player targeting methods]
└── broadcastPriority: 8
```

The Director plugin's event lifecycle (create → add actors → activate → set phase → complete) maps perfectly to Video Director's generation pipeline (select → cast → compose → execute → finish).

### The Streaming Angle

For live streaming (Broadcast + Showtime services), this creates a unique content category:

- **Pre-stream intro**: Auto-generated music video showcasing the session's characters
- **Mid-stream intermission**: Dynamic music video generated from the session's events so far
- **Highlight reel**: Post-session cinematic composed from the most dramatic moments, timed to music
- **On-demand showcase**: Streamer triggers a music video for any two characters on demand

The Showtime service's hype train mechanics could even tie into this -- audience hype levels influence which characters get featured, what intensity level the choreography reaches, and whether the cinematic gets an extended chorus or a shorter cut.

---

## Embedded Bannou: The Content Creation Tool

Embedded Bannou makes Video Director accessible **outside a live game server**:

### Standalone Video Director Application

A desktop application (Electron, native, or web-based) embedding Bannou in-process:

1. **No server required**: SQLite + InMemory backends, all services in-process
2. **Import game data**: Load character archives, location data, scene compositions from exports
3. **Author cinematics**: Full selection/configuration UI
4. **Preview in real-time**: Game engine renders directly
5. **Export**: Share as seed codes, video files, or cinematic packages

### Use Cases

| Use Case | Description |
|----------|------------|
| **Fan content** | Players create music videos featuring their characters |
| **Community events** | Guilds produce promotional videos for recruitment |
| **Developer tools** | QA and marketing create trailers from live game data |
| **Modding** | Modders create cinematic content for custom scenarios |
| **Music integration** | Musicians create game-world music videos for their songs |

### Data Pipeline

```
Live Game Server
    │
    ├── Export: character archives, location data, scene compositions
    │
    ▼
Embedded Bannou (Desktop App)
    │
    ├── Import game data into SQLite stores
    ├── Video Director authoring interface
    ├── Compose cinematic (all SDKs run locally)
    ├── Game engine preview (Stride/Unity/Unreal embedded)
    │
    ▼
Output: Seed code (shareable), video file, cinematic package
```

---

## What Needs to Be Built

### New Components

| Component | Type | Purpose |
|-----------|------|---------|
| **VideoDirectorComposer** | SDK (Pure Computation) | Music-to-narrative mapping, temporal staging, montage composition, beat-sync coordination |
| **MatchCutAnalyzer** | SDK (VideoDirectorComposer submodule) | Archive analysis for visual parallels across life stages |
| **MusicSceneMapper** | SDK (VideoDirectorComposer submodule) | Section energy → scene type mapping, phrase → shot sequence timing |
| **LifeStageDecomposer** | SDK (VideoDirectorComposer submodule) | Character archive → temporal segment extraction |
| **CastingEngine** | SDK (VideoDirectorComposer submodule) | Theme + criteria → character selection with variable provider queries |
| **RoleMappingEngine** | SDK (VideoDirectorComposer submodule) | Music ensemble roles → cinematic roles, cross-domain prominence sync |

### Existing Components Leveraged (No Changes Needed)

| Component | Role in Video Director |
|-----------|----------------------|
| **Counterpoint Composer SDK** | Music structural template analysis and representation |
| **MusicTheory + MusicStoryteller** | Procedural music generation, emotional state tracking |
| **StorylineTheory + StorylineStoryteller** | Narrative arc composition from archives |
| **CinematicTheory + CinematicStoryteller** | Choreography composition, camera direction |
| **CinematicInterpreter + CinematicRunner** | Runtime execution of composed cinematics |
| **CutsceneSession + CutsceneCoordinator** | Multi-entity synchronization |
| **Actor Runtime** | Behavior execution for all participating entities |
| **Variable Provider Factory** | Character data access (personality, combat, encounters, etc.) |
| **lib-resource** | Character archive access for historical material |
| **Location / Mapping / Scene** | Environment data for scene composition |
| **Environment / Worldstate** | Weather, time, season for atmospheric control |
| **Director Plugin** | Event coordination infrastructure (for live-world mode) |
| **Broadcast + Showtime** | Streaming integration (for live-world mode) |

### Plugin Option: lib-video-director (L4)

If implemented as a Bannou service:

| Endpoint | Purpose |
|----------|---------|
| `/video-director/compose` | Full pipeline: template + theme + criteria → cinematic plan |
| `/video-director/cast` | Character selection from criteria |
| `/video-director/preview` | Lightweight plan preview (scene list, timing, cast) without full choreography |
| `/video-director/execute` | Launch cinematic execution on the actor runtime |
| `/video-director/templates` | CRUD for reusable video templates (theme + style presets) |
| `/video-director/abort` | Stop active cinematic |

**Layer**: L4 (GameFeatures) -- needs Character, Personality, Encounter, History (L4), Asset (L3), Storyline (L4), Cinematic (L4), Actor (L2).

**Soft dependencies**: Director (L4) for live-world mode, Broadcast + Showtime (L3/L4) for streaming integration.

---

## The SDK Pattern (Consistent with All Bannou SDKs)

Following the established Theory/Storyteller/Plugin pattern:

```
VideoDirectorTheory (Pure Computation, No Dependencies)
├── MusicSceneMapping      — section energy → scene type rules
├── TemporalStaging        — archive → life stage decomposition
├── MatchCutRules          — visual parallel identification
├── BeatSyncRules          — action-to-beat alignment constraints
├── MontageGrammar         — temporal intercut ordering rules
└── CinematicRoles         — role constraint profiles (screen time, camera, density, freedom)

VideoDirectorComposer (GOAP-Driven Composition, Depends on Theory Only)
├── CastingPlanner         — theme + criteria → cast selection GOAP
├── RoleMappingEngine      — music ensemble roles → cinematic roles, cross-domain prominence sync
├── ScenePlanner           — music sections + cast + locations → scene plan GOAP
├── MontageSequencer       — temporal staging + match cuts → ordered sequence
├── BeatSyncScheduler      — choreography timing → beat-aligned schedule
└── CameraDirectionMapper  — scene type + energy + role → camera idiom selection

lib-video-director (L4 Plugin, Thin API Wrapper)
├── Composition endpoint   — orchestrates full pipeline
├── Casting endpoint       — character selection only
├── Template management    — reusable configuration presets
├── Execution endpoint     — launches on actor runtime
└── Variable provider      — ${video.*} namespace for actors in cinematics
```

---

## Open Questions

1. **Asset requirements**: What 3D assets does the game engine need pre-loaded to render a cinematic? Character models at different ages? Location meshes? How does the engine handle "show me this character at age 12" when only the adult model exists? This is a game engine concern, not a Bannou concern, but it constrains what's practically achievable.

2. **Duration limits**: How long can a single cinematic be? A 3-minute song is manageable; a 10-minute epic requires more scene variety and risks repetition. The system needs a "content budget" concept -- how much unique material is available from the selected characters' archives.

3. **Multi-character scaling**: Largely addressed by the [Role-Based Cinematic Composition](#role-based-cinematic-composition) model -- ensemble roles constrain screen time and choreographic density, preventing the "too many characters, no focus" problem. Remaining question: what's the practical upper bound? A 6-character party video works (1-2 foreground, 2-3 supporting, environment). Does a 12-character war montage work, or does it collapse into visual noise regardless of role assignment?

4. **Live-world mode physics**: When triggering a music video in a live world, how do non-participating entities react? Do NPCs stop and watch? Continue their routines? The Director's ControlGateManager handles entity ownership, but ambient world behavior during a cinematic is a design question.

5. **Player agency during live videos**: If a player's character is selected for a live music video, do they lose control? The progressive agency system suggests the spirit would observe (the character is "performing"), which is consistent with the combat cinematic model where low-fidelity spirits watch autonomously.

6. **Music licensing for analyzed templates**: The Counterpoint Composer SDK's legal analysis is thorough (templates contain only uncopyrightable structural parameters), but community perception of "we analyzed your song to make a game video" needs careful messaging.

