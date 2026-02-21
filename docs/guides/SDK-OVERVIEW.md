# Bannou SDKs Overview

Bannou's SDK ecosystem follows a consistent three-layer pattern across creative domains: **theory** (formal primitives), **storyteller** (procedural generation), and **composer** (handcrafted authoring). Each layer is independently useful, and each domain doesn't necessarily need all three -- the pattern emerges where the domain's complexity warrants it.

---

## The Three-Layer Pattern

```
              COMPOSER                    STORYTELLER                  THEORY
        Handcrafted Authoring        Procedural Generation        Formal Primitives
   ┌────────────────────────┐   ┌────────────────────────┐   ┌────────────────────────┐
   │ Interactive/batch       │   │ GOAP-driven automated  │   │ Low-level building     │
   │ authoring tools for     │   │ composition from       │   │ blocks: pitches, plot  │
   │ human creators.         │   │ context and goals.     │   │ units, camera modules, │
   │                         │   │                        │   │ voxel grids.           │
   │ Validation, undo/redo,  │   │ Deterministic when     │   │                        │
   │ serialization, engine   │   │ seeded, enabling       │   │ Zero dependencies,     │
   │ bridge abstractions.    │   │ content-addressed      │   │ pure computation,      │
   │                         │   │ caching.               │   │ deterministic.         │
   │ Output: authored        │   │                        │   │                        │
   │ documents in the        │   │ Output: same format    │   │ Usable independently   │
   │ domain's format.        │   │ as hand-authored.      │   │ for scripting or       │
   │                         │   │                        │   │ custom generation.     │
   └────────────────────────┘   └────────────────────────┘   └────────────────────────┘
           depends on ──────────────► depends on ──────────────►
```

The key insight: **storytellers produce the same output format as composers**. A Bannou plugin receiving a scenario, a composition, or a scene document cannot tell whether it was hand-authored or procedurally generated. This enables god-actors to mix authored and generated content seamlessly in the content flywheel.

---

## Domain Matrix

| Domain | Theory | Storyteller | Composer | Status |
|--------|--------|-------------|----------|--------|
| **Music** | `music-theory` | `music-storyteller` | *planned* | Theory + Storyteller complete |
| **Storyline** | `storyline-theory` | `storyline-storyteller` | `storyline-composer` *planned* | Theory + Storyteller complete; Composer in design |
| **Cinematic** | `cinematic-theory` *planned* | `cinematic-storyteller` *planned* | `cinematic-composer` *planned* | All in design (4-phase plan) |
| **Scene** | -- | -- | `scene-composer` | Composer complete (reference impl) |
| **Voxel** | -- | -- | `voxel-builder` *planned* | Design complete, no code yet |
| **Behavior** | `behavior-expressions` | `behavior-compiler` | -- | Both complete (ABML is text-authored) |

**Why some domains skip layers**: Scene composition doesn't have meaningful "theory primitives" or "procedural generation" -- scenes are either hand-authored or composed by god-actors calling service APIs. Behavior is authored as ABML YAML text, so a visual composer isn't applicable. The pattern serves the domain, not the other way around.

---

## Creative Domain SDKs

### Music

Procedural music generation grounded in formal music theory and music cognition research.

| SDK | Purpose | Status |
|-----|---------|--------|
| `music-theory` | Pitch, scales, chords, harmony, voice leading, rhythm, MIDI-JSON output | Complete |
| `music-storyteller` | Emotional state model (6D), narrative templates, GOAP-driven composition | Complete |
| *music-composer* | Interactive music authoring with theory primitives | Planned (future) |

**How they compose**: MusicStoryteller uses GOAP planning to select musical actions (tension, resolution, thematic development) that move through an emotional state space, then delegates to MusicTheory for the actual pitch/harmony/voice-leading computation. The lib-music plugin wraps this as an HTTP API with Redis caching for deterministic outputs.

**Academic foundations**: Lerdahl's Tonal Pitch Space, Huron's expectation model, Juslin's emotional expression framework, Reagan's six emotional arc shapes.

### Storyline

Seeded narrative generation from compressed character archives, producing storyline plans that god-actors translate into quests, encounters, and world events.

| SDK | Purpose | Status |
|-----|---------|--------|
| `storyline-theory` | Archive extraction, actant assignment (Greimas), emotional arc classification (Reagan), kernel/satellite identification (Barthes), life value spectrums (Story Grid) | Complete |
| `storyline-storyteller` | GOAP-driven narrative planning, story action sequencing, template-based composition | Complete |
| `storyline-composer` | Typed scenario definitions, mechanical condition evaluation, mutation descriptions, YAML/JSON serialization | In design |

**How they compose**: When a character dies, lib-resource compresses their archive. A god-actor perceives the archive event and calls lib-storyline, which uses StorylineStoryteller to GOAP-plan a narrative arc. The storyteller uses StorylineTheory to extract kernels (critical plot points) from the archive, assign actant roles, and classify the emotional arc shape. The output is a StorylinePlan that the god-actor translates into concrete game actions (quest creation, NPC spawning, item placement).

**The storyline-composer SDK** extracts the scenario definition format from lib-storyline's current JSON blobs into a typed, portable SDK. This enables client-side authoring tools to create scenario definitions without importing plugin dependencies, and establishes the typed format that storyline-storyteller will eventually output (unifying hand-authored and procedural scenarios).

**Academic foundations**: Propp's Morphology of the Folktale, Greimas actantial model, Barthes/Chatman kernel theory, Coyne's Story Grid, Snyder's Save the Cat, Reagan et al. emotional arc classification.

### Cinematic

Choreographed combat encounters, cutscenes, and QTE sequences with formal camera direction and movement theory.

| SDK | Purpose | Status |
|-----|---------|--------|
| `cinematic-theory` | Toric Space camera solver, EMOTE/Laban movement transformation, film idiom HFSM, Murch Rule of Six scoring, dramatic grammar | In design |
| `cinematic-storyteller` | GOAP-driven choreography composition, beat sequencing (Facade algorithm), tension arc management, agency-scaled QTE density | In design |
| `cinematic-composer` | Timeline/track authoring format, participant slots with capability requirements, sync barriers, continuation points (QTE/choice/branch), ABML export | In design |

**How they will compose**: A god-actor or event brain detects an encounter opportunity and calls lib-cinematic to find mechanically matching scenarios. The plugin returns all scenarios whose trigger conditions are satisfied (binary predicate matching, no ranking). The actor's GOAP planner selects one. If procedural, cinematic-storyteller composes a choreography using Facade's greedy beat sequencing algorithm, styling movements via EMOTE/Laban effort transformation and directing cameras via HFSM film idioms. The output is a CinematicScenario -- the same format as hand-authored scenarios from cinematic-composer.

**Three independent layers**: Structure (WHAT happens -- exchange sequencing), Quality (HOW it moves -- Effort/Shape transformation), and Presentation (HOW it's shown -- camera direction). Each is separable, composable, and independently testable.

**Progressive agency integration**: Each continuation point (QTE window) has a priority threshold. The guardian spirit's fidelity score determines which continuation points become interactive vs auto-resolved. Same choreography, different interaction density per player.

**Academic foundations**: EMOTE (Chi et al., SIGGRAPH 2000), Toric Space (Lino & Christie, SIGGRAPH 2015), Virtual Cinematographer (He/Cohen/Salesin, SIGGRAPH 1996), Facade (Mateas & Stern, AIIDE 2005), Film Editing Patterns (Wu/Christie, ACM TOMM 2018), Murch's Rule of Six, Reagan emotional arcs, Resonator model (Brown & Tu, 2021), L4D AI Director, SAFD C.R.A. pattern.

### Scene

Hierarchical spatial composition for game worlds -- the reference implementation of the composer pattern.

| SDK | Purpose | Status |
|-----|---------|--------|
| `scene-composer` | Engine-agnostic scene graph editing, command-based undo/redo, multi-selection, transform operations, validation, engine bridge pattern | Complete |
| `scene-composer-stride` | Stride engine bridge: entity creation, bundle asset loading, LRU cache, gizmo rendering, physics picking | Complete |
| `scene-composer-godot` | Godot 4.x bridge: Node3D mapping, native ResourceLoader, procedural gizmos, cross-platform | Complete |

**Why scene-composer is the reference**: It established the patterns that all composers follow -- engine-agnostic core with pluggable bridges, command-based undo/redo, validation system, and optional service client for persistence. Two complete engine integrations (Stride, Godot) validate the bridge abstraction.

**Architecture**:
```
scene-composer (core)          Engine-agnostic: scene graph, commands, selection, validation
    |
    +-- ISceneComposerBridge   Contract for engine integration
    |       |
    |       +-- scene-composer-stride    Stride implementation
    |       +-- scene-composer-godot     Godot implementation
    |
    +-- ISceneServiceClient    Optional: checkout/commit persistence via lib-scene
```

### Voxel

Discrete spatial construction for dungeons, housing, NPC building, and lightweight procedural generation as a complement to Houdini.

| SDK | Purpose | Status |
|-----|---------|--------|
| `voxel-builder` | Sparse voxel grid (16x16x16 chunks), editing operations with undo/redo, import (BlockBench/MagicaVoxel), export (.bvox/mesh), meshing (culled/greedy/marching cubes), generation (WFC, L-systems, noise, template stamping) | Design complete, no code |
| `voxel-builder-godot` | Godot engine bridge: per-chunk ArrayMesh rendering, mouse-to-voxel picking, palette atlas generation | Planned |

**Why voxel-builder, not voxel-composer**: Voxel editing is self-contained -- a single grid, not a hierarchical document. Spatial composition of voxel objects happens at the Scene level: SceneComposer recognizes `voxel` node types in scene documents and delegates rendering to VoxelBuilder's engine bridge. No separate "voxel-composer" is needed because the composition is already handled by scene-composer.

**Primary consumers**: Dungeon cores (chamber growth, layout shifting, memory manifestation), NPC builders (ABML-driven construction), player housing (Gardener garden type), divine actors (environmental marks), lib-procedural (lightweight alternative to Houdini for discrete geometry).

**Custom format (.bvox)**: Chunk-aligned binary with LZ4 compression, optimized for streaming (load visible chunks first), delta saves (re-serialize only dirty chunks), and server efficiency. BlockBench (.bbmodel) and MagicaVoxel (.vox) import/export for artist workflow.

### Behavior

ABML (Arcadia Behavior Markup Language) compilation and execution for NPC brains, god-actors, and any autonomous entity.

| SDK | Purpose | Status |
|-----|---------|--------|
| `behavior-expressions` | Expression parsing, variable resolution, type coercion, runtime evaluation | Complete |
| `behavior-compiler` | Multi-phase ABML compilation (YAML -> AST -> bytecode), A*-based GOAP planner, stack-based interpreter | Complete |

**Why no behavior-composer**: ABML is a YAML-based DSL authored as text. The "authoring tool" is a text editor with syntax highlighting. A visual behavior tree editor is possible but not planned -- ABML's text format is expressive enough for the intended authors (game designers and god-actor behavior scripts).

---

## Infrastructure SDKs

These SDKs handle connectivity, serialization, and asset pipeline concerns. They don't follow the theory/storyteller/composer pattern because they're infrastructure, not creative domains.

### Connectivity

| SDK | Purpose | Target | Status |
|-----|---------|--------|--------|
| `core` | Shared types: BannouJson, ApiException, base events, IBannouEvent | All SDKs | Complete |
| `client` | WebSocket client, binary protocol, typed service proxies, event subscriptions, capability manifest, game transport (LiteNetLib/MessagePack) | Game clients | Complete |
| `client-voice` | P2P voice (WebRTC, 1-5 peers), scaled voice (SIP/RTP via Kamailio, 6+), automatic tier transition | Game clients with voice | Complete |
| `server` | Generated service clients for mesh calls, event subscriptions, includes full client SDK | Game servers, internal services | Complete |
| `protocol` | Binary WebSocket protocol specification (31-byte headers, zero-copy routing) | Low-level integration | Complete |
| `transport` | Game message transport (LiteNetLib UDP, MessagePack DTOs, fuzz testing) | Real-time gameplay | Complete |
| `bundle-format` | `.bannou` bundle format specification | Asset tooling | Complete |

### Asset Pipeline

| SDK | Purpose | Target | Status |
|-----|---------|--------|--------|
| `asset-bundler` | Engine-agnostic bundling pipeline: source extraction, processing, LZ4 compression, upload | Asset tooling | Complete |
| `asset-bundler-stride` | Stride asset compilation (FBX, textures via Stride pipeline) | Stride pipelines | Complete |
| `asset-bundler-godot` | Godot format processing (GLB pass-through, format conversion) | Godot pipelines | Complete |
| `asset-loader` | Async download, LRU cache, bundle registry | All platforms | Complete |
| `asset-loader-client` | WebSocket-based asset source (download via Connect gateway) | Game clients | Complete |
| `asset-loader-server` | Mesh-based asset source (download via lib-mesh) | Game servers | Complete |
| `asset-loader-stride` | Stride type loaders (model, texture, animation) | Stride runtime | Complete |
| `asset-loader-godot` | Godot type loaders (GLB, PNG, audio) | Godot runtime | Complete |

### TypeScript

| Package | Purpose | Status |
|---------|---------|--------|
| `@beyondimmersion/bannou-core` | Shared types: ApiResponse, BannouJson, base events | Complete |
| `@beyondimmersion/bannou-client` | WebSocket client, binary protocol, typed proxies (33 services), event subscriptions | Complete |

### Unreal Engine Helpers

| Artifact | Purpose | Status |
|----------|---------|--------|
| `BannouProtocol.h` | Binary protocol constants, flags, response codes | Generated |
| `BannouTypes.h` | All request/response USTRUCT definitions | Generated |
| `BannouEnums.h` | All UENUM definitions | Generated |
| `BannouEndpoints.h` | Endpoint constants with metadata | Generated |
| `BannouEvents.h` | Event name constants | Generated |

Unreal gets helper artifacts instead of a full SDK because Unreal developers have diverse networking requirements. The helpers eliminate tedious struct definitions while preserving full architectural control.

---

## How SDKs Relate to Plugins

SDKs are **pure computation libraries** with zero Bannou service dependencies. Plugins are **service implementations** that wrap SDKs behind HTTP APIs with state management, caching, and event publishing.

```
SDK (pure computation)              Plugin (service wrapper)
────────────────────                ───────────────────────
music-theory                   -->  lib-music
music-storyteller              -->      |
                                        +-- Redis caching
                                        +-- deterministic seed → cache key
                                        +-- HTTP API endpoints

storyline-theory               -->  lib-storyline
storyline-storyteller          -->      |
                                        +-- archive extraction
                                        +-- plan storage
                                        +-- scenario registry

cinematic-composer (planned)   -->  lib-cinematic (planned)
cinematic-storyteller (planned)-->      |
                                        +-- compilation cache
                                        +-- IBehaviorDocumentProvider
                                        +-- active instance tracking

scene-composer                 -->  lib-scene
                                        |
                                        +-- checkout/commit workflow
                                        +-- version history
                                        +-- full-text search

behavior-compiler              -->  lib-behavior (compilation)
behavior-expressions           -->  lib-actor (execution)
```

SDKs can be used independently of Bannou services -- in game clients, editor tools, CI pipelines, or any .NET application. The plugin layer adds persistence, caching, and API exposure.

---

## The Unified Scenario Pattern

Storyline and Cinematic domains converge on a **unified scenario pattern**: both hand-authored and procedurally generated content produces the same typed format, consumed by passive registry plugins, with god-actors providing all judgment and decision-making.

```
     Hand-Authored                              Procedural
  (storyline-composer)                    (storyline-storyteller)
  (cinematic-composer)                    (cinematic-storyteller)
          |                                        |
          |        same typed format               |
          +──────────────┬─────────────────────────+
                         |
                         v
              lib-{domain} Plugin (L4)
              - Scenario registry (CRUD)
              - Mechanical condition matching (binary predicates)
              - Trigger execution with safety (locks, cooldowns, limits)
              - Instance tracking
              - Does NOT rank or select -- returns all matches
                         |
                         v
                   God-Actor (ABML)
              - GOAP evaluation of candidates
              - Narrative judgment (which scenario fits the moment)
              - Trigger decision
              - Post-completion mutation execution
```

**Three principles**: (1) Plugins are passive registries, not intelligent selectors. (2) God-actors provide all judgment via ABML behavior documents. (3) Format agnosticism -- the runtime cannot distinguish hand-authored from procedural.

---

## Decision Guide

```
What are you building?

├─ A game client (.NET)
│  └─ BeyondImmersion.Bannou.Client
│     └─ Need voice? Add BeyondImmersion.Bannou.Client.Voice
│
├─ A game client (TypeScript/browser)
│  └─ @beyondimmersion/bannou-client
│
├─ A game client (Unreal C++)
│  └─ Copy sdks/unreal/Generated/*.h into your project
│
├─ A game server or internal service
│  └─ BeyondImmersion.Bannou.Server
│
├─ Asset bundling tools
│  └─ BeyondImmersion.Bannou.AssetBundler
│     └─ For Stride? Add .AssetBundler.Stride
│     └─ For Godot? Add .AssetBundler.Godot
│
├─ A scene editor
│  └─ BeyondImmersion.Bannou.SceneComposer
│     └─ For Stride? Add .SceneComposer.Stride
│     └─ For Godot? Add .SceneComposer.Godot
│
├─ Music-aware features
│  ├─ Need emotional/narrative composition? Use MusicStoryteller (includes MusicTheory)
│  └─ Just need theory primitives? Use MusicTheory only
│
├─ Narrative/storyline features
│  ├─ Need full composition? Use StorylineStoryteller (includes StorylineTheory)
│  └─ Just need theory primitives? Use StorylineTheory only
│
└─ Extending or developing Bannou SDKs
   └─ See sdks/CONVENTIONS.md for naming, structure, and versioning patterns
```

---

## Version Compatibility

All SDKs share the same version number and are released together. The version is stored in `sdks/SDK_VERSION`.

---

## Further Reading

### SDK Development
- [SDK Conventions](../../sdks/CONVENTIONS.md) - Naming, structure, versioning, and development patterns

### Creative Domain SDKs
- [MusicTheory README](../../sdks/music-theory/README.md) - Pitch, harmony, voice leading
- [MusicStoryteller README](../../sdks/music-storyteller/README.md) - Emotional composition
- [Music System Guide](MUSIC_SYSTEM.md) - Complete music system documentation
- [StorylineTheory README](../../sdks/storyline-theory/README.md) - Narrative theory primitives
- [StorylineStoryteller README](../../sdks/storyline-storyteller/README.md) - GOAP-driven narrative planning
- [SceneComposer README](../../sdks/scene-composer/README.md) - Scene editing core
- [Scene System Guide](SCENE-SYSTEM.md) - Complete scene system documentation

### Infrastructure SDKs
- [Core README](../../sdks/core/README.md) - Shared types
- [Client README](../../sdks/client/README.md) - WebSocket client
- [Server README](../../sdks/server/README.md) - Service clients
- [Voice README](../../sdks/client-voice/README.md) - Voice communication
- [AssetBundler README](../../sdks/asset-bundler/README.md) - Asset pipeline
- [Asset SDK Guide](ASSET-SDK.md) - Complete asset system documentation

### Platform Integration
- [TypeScript SDK Guide](TYPESCRIPT-SDK.md) - Browser/Node.js integration
- [Unreal Integration Guide](UNREAL-INTEGRATION.md) - C++ integration
- [WebSocket Protocol](../WEBSOCKET-PROTOCOL.md) - Binary protocol specification

### Planned SDKs
- [Cinematic System Plan](../plans/CINEMATIC-SYSTEM.md) - Cinematic SDK architecture
- [Storyline Composer Plan](../plans/STORYLINE-COMPOSER-SDK.md) - Storyline scenario SDK
- [Voxel Builder Plan](../plans/VOXEL-BUILDER-SDK.md) - Voxel SDK architecture
