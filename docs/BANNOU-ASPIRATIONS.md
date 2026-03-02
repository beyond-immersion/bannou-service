# Bannou Aspirations: What a Game Backend Platform Can Be

> **Purpose**: This document describes what Bannou delivers to game developers, studios, and solo creators. It is the comprehensive answer to "why would I build my game on Bannou?" -- covering the platform's composable architecture, its creative SDK ecosystem, the genres it enables, the development time it eliminates, and what remains for the developer to do. It complements VISION.md (the architectural north stars) and PLAYER-VISION.md (the player experience design) by focusing on the **developer experience** and the **studio economics** of building games on Bannou.
>
> **Core Thesis**: Bannou eliminates 85-95% of backend systems, networking, and application infrastructure development for multiplayer games. What remains -- and what the developer should spend their time on -- is the creative work: behavior authoring, world composition, visual presentation, and the unique game feel that makes their project theirs.

---

## The Platform in One Paragraph

Bannou is a monoservice game backend platform comprising 76 planned service plugins (892+ endpoints across 184 maintained YAML schemas) that provides everything a multiplayer game needs -- authentication, real-time WebSocket communication, economies, inventories, quests, matchmaking, voice chat, spatial data, save/load, procedural content generation, and an autonomous NPC intelligence stack capable of running 100,000+ concurrent AI-driven characters. It compiles into a single binary that deploys as anything from an in-process library embedded directly in a game client (no server required) to a fully distributed microservices architecture spanning thousands of nodes. The same game code, the same SDK calls, the same ABML behavior documents work identically across all deployment modes. The game is the data; Bannou runs the simulation.

---

## I. The Three Deployment Modes

Bannou is not "a server you connect to." It is a runtime that can host your game's logic anywhere.

### Cloud Deployment (Traditional)

The familiar model: Bannou runs as a containerized service (Docker Compose, Kubernetes, Portainer, Docker Swarm -- all supported via the Orchestrator's pluggable backend architecture). Game clients connect via WebSocket through the Connect gateway. Services scale horizontally via environment variables -- enable or disable any of the 76 plugins per node without code changes.

```
Game Client ──WebSocket──▶ Connect Gateway ──mesh──▶ 76 Service Plugins
                                                      (distributed across nodes)
```

### Self-Hosted / Sidecar Deployment

The Satisfactory/Valheim model: Bannou ships as a sidecar binary alongside the game client or dedicated server. Infrastructure backends swap to local alternatives (SQLite instead of MySQL, InMemory message bus instead of RabbitMQ, local mesh routing instead of YARP). A factory game can run on ~15 of the 76 plugins, with Workshop's lazy evaluation computing production retroactively from elapsed game-time on next query -- the server does not need to stay running between play sessions.

```
Game Client + Bannou Sidecar (same machine, localhost:5012)
  └── SQLite for persistence
  └── InMemory pub/sub
  └── any number of plugins loaded
```

### Embedded / In-Process Deployment

The most radical mode: Bannou runs inside the game process itself. No separate server, no Docker, no network. The same `IServiceClient` interfaces that call HTTP in cloud mode resolve to direct DI method calls in embedded mode. The plugin architecture, background workers, state stores, and event system all function identically -- the only difference is that service invocations skip serialization and loopback networking.

```
Game Process
  └── Bannou EmbeddedHost (in-process)
      └── SQLite + InMemory backends
      └── All service plugins loaded in-process
      └── Same SDK API surface as cloud mode
```

**What this means**: A mobile game, a desktop single-player RPG, a Nintendo Switch title, and an MMO with 100,000 concurrent players can all be built on the same Bannou SDK. The game code is identical. The deployment mode is a configuration choice, not an architectural one.

---

## II. The 76-Plugin Composition Model

Bannou's services are not a monolithic "game server framework." They are **composable primitives** -- small, orthogonal services that combine to create emergent game systems. No single service "is" the economy, or "is" combat, or "is" crafting. These are behaviors that emerge from primitives interacting.

### The Six Layers

```
L0: Infrastructure (4 plugins)     State, Messaging, Mesh, Telemetry
L1: App Foundation (7 plugins)     Account, Auth, Chat, Connect, Permission, Contract, Resource
L2: Game Foundation (17 plugins)   Character, Realm, Species, Location, Currency, Item, Inventory,
                                   Actor, Quest, Seed, Collection, Transit, Worldstate, and more
L3: App Features (6 plugins)       Asset, Orchestrator, Documentation, Website, Voice, Broadcast
L4: Game Features (41 plugins)     Behavior, Puppetmaster, Matchmaking, Escrow, Faction, Divine,
                                   Dungeon, Craft, Trade, Workshop, Music, Storyline, and 29 more
L5: Extensions (developer-created) Game-specific vocabulary, simplified APIs, genre kits
```

### Why Composition, Not Modules

Traditional game backends offer "modules" -- self-contained features like "the inventory system" or "the matchmaking system." Bannou offers **primitives that compose**:

| Traditional Module | Bannou Composition |
|---|---|
| "Skill System" | Seed (mastery growth) + License (skill tree boards) + Status (active effects) + Collection (ability discovery) + Actor (behavior decisions) |
| "Crafting System" | Craft (recipe orchestration) + Item (materials/outputs) + Inventory (containers) + Contract (multi-step sessions) + Currency (costs) + Affix (modifier operations) |
| "Player Housing" | Gardener (garden lifecycle) + Seed (progression) + Scene (node tree) + Save-Load (delta persistence) + Inventory + Item (furniture) + Permission (visitor access) |
| "Combat System" | Actor (behavior decisions) + Character-Personality (preferences) + Ethology (species instincts) + Status (buffs/debuffs) + Cinematic SDK (choreography) + Agency (player input fidelity) |
| "NPC Economy" | Currency (wallets) + Item + Inventory + Trade (logistics) + Workshop (production) + Market (exchange) + GOAP (NPC decisions) |

**The absence of a housing plugin validates the architecture.** If a feature as visible and complex as player housing requires no new service, the primitive set is genuinely composable rather than theoretically so. The same validation applies to combat, skills, magic, classes, and guilds -- all are compositions of existing primitives, not discrete services.

### The Extension Pattern

When a studio finds itself repeatedly composing the same primitives in their game server code, they create an **L5 Extension** -- a thin facade that provides game-specific vocabulary over generic primitives. Six extension patterns are supported:

1. **Semantic Facade**: Translates domain language (e.g., `POST /skills/get-level` resolves to a seed domain depth query)
2. **Composition Orchestrator**: Atomically calls multiple primitives (e.g., "craft this item" = consume materials + create output + debit currency + grant XP)
3. **Configuration Hardener**: Registers game-specific types at startup (seed types, collection types, contract templates)
4. **View Aggregator**: Assembles cross-service data into game-specific views (e.g., "character sheet" = character + personality + quest + inventory data)
5. **Event Translator**: Re-publishes primitive events as domain events (e.g., `seed.phase.changed` becomes `skill.level-up`)
6. **Variable Provider**: Exposes computed game state to NPC behaviors via `IVariableProviderFactory`

Extensions follow the same schema-first development, plugin architecture, and testing patterns as core services. They are how studios make Bannou *theirs* without forking it.

---

## III. The Creative SDK Ecosystem

Bannou's most distinctive contribution is a family of **pure-computation SDKs** that generate creative content procedurally using formal academic theories -- not AI/LLM inference. Every SDK follows the same three-layer pattern:

```
Theory Layer     Pure computation primitives (pitch arithmetic, narrative grammar, spatial reasoning)
                 ↓
Storyteller Layer  GOAP-driven procedural generation (plan a composition from world state)
                 ↓
Composer Layer   Hand-authored content tools (interactive editors, command-based undo/redo)
```

All three layers produce the **same output format**, making hand-authored and procedurally generated content indistinguishable at runtime.

### Music Generation

| Layer | SDK | What It Does |
|---|---|---|
| Theory | `MusicTheory` | Pitch/interval arithmetic, scales, modes, chord construction, voice leading, MIDI-JSON output |
| Storyteller | `MusicStoryteller` | 6-dimensional emotional state tracking, narrative arc templates, GOAP-planned composition grounded in Lerdahl's Tonal Pitch Space, Huron's ITPRA model, and Juslin's BRECVEMA mechanisms |
| Plugin | `lib-music` (L4) | HTTP API wrapping both SDKs, Redis caching of deterministic compositions |

**Output**: Portable MIDI-JSON. The game engine renders it with any synthesizer. Deterministic when seeded -- same parameters always produce the same composition.

**What this eliminates**: Hand-authored adaptive music state machines (FMOD/Wwise), pre-recorded emotional variants of every track, and the need for a full-time composer for dynamic game music.

### Narrative Generation

| Layer | SDK | What It Does |
|---|---|---|
| Theory | `StorylineTheory` | Greimas actantial model, Propp's morphology, Barthes' narrative codes -- formal narrative primitives |
| Storyteller | `StorylineStoryteller` | GOAP-planned story arcs from compressed character archives, lazy phase evaluation via continuation points |
| Plugin | `lib-storyline` (L4) | HTTP API, scenario matching, dry-run testing, origin scenario for organic character creation |

**Two execution modes from the same definitions**: Simple Mode (deterministic MMO-style unlocks with periodic server checks) and Emergent Mode (regional watcher god-actors actively searching, scoring, and deciding which characters qualify for which narratives based on their actual life histories).

**What this eliminates**: Hand-scripted quest trigger systems, static branching dialogue trees, and the combinatorial explosion of pre-authoring every world-state/character-history/NPC-context condition that could fire a story event.

### Scene Composition

| Layer | SDK | What It Does |
|---|---|---|
| Composer | `SceneComposer` | Engine-agnostic editor with undo/redo command stack, multi-select state machine, gizmo abstraction, ViewModel-based UI |
| Storage | `lib-scene` (L4) | Hierarchical scene documents as node trees, exclusive checkout/commit locking, version history, full-text search |
| Distribution | `lib-asset` (L3) | `.bannou` bundles with LZ4 compression, pre-signed MinIO/S3 URLs |

Scenes are authored, stored, and consumed in the same format everywhere -- no baking step, no per-engine format conversion. Engine bridges exist for Stride and Godot, with Unity and Unreal patterns documented.

**What this eliminates**: Per-engine scene export pipelines, format conversion tools, and editor-to-runtime data transformations.

### Behavior Authoring (ABML)

| Layer | SDK | What It Does |
|---|---|---|
| Compiler | `behavior-compiler` | Multi-phase ABML-to-bytecode compilation, A*-based GOAP planner |
| Runtime | `behavior-expressions` | Variable resolution against 14+ provider namespaces (`${personality.*}`, `${world.*}`, `${quest.*}`, etc.) |
| Plugin | `lib-behavior` (L4) + `lib-actor` (L2) | ABML compilation API, actor execution runtime with 100,000+ NPC scale |

ABML (Arcadia Behavior Markup Language) is a YAML-based DSL where developers define NPC goals, actions, and conditions. The GOAP planner discovers action sequences autonomously -- the developer specifies *what* NPCs want, not *how* they achieve it.

**What this eliminates**: Hand-authored behavior trees, scripted NPC response tables, and the exponential complexity of manually specifying every possible NPC decision path.

### Cinematic Choreography (Planned)

| Layer | SDK | What It Does |
|---|---|---|
| Theory | `CinematicTheory` (planned) | Dramatic beat structure, tension curves, spatial affordance evaluation, capability matching, camera direction |
| Storyteller | `CinematicStoryteller` (planned) | GOAP-planned choreographic ABML documents from encounter context |
| Runtime | `CinematicInterpreter` (exists) | Streaming composition with continuation points, already in `sdks/behavior-compiler/Runtime/` |
| Plugin | `lib-cinematic` (L4, planned) | HTTP API callable from ABML behaviors |

The runtime infrastructure already exists. The missing piece is the compositional tier that evaluates spatial affordances, character capabilities, and dramatic pacing to produce choreographic sequences. Player agency (the guardian spirit's combat fidelity float) determines how many continuation points become interactive vs. auto-resolving.

### Procedural 3D Generation

| Component | What It Does |
|---|---|
| `lib-procedural` (L4) | Dispatches parametric generation requests to headless Houdini Digital Asset workers |
| `lib-asset` (L3) | Stores HDAs and generated output bundles |
| `Orchestrator` (L3) | Manages Houdini worker pool scaling |

Artists author Houdini Digital Assets (self-contained parametric templates). The game sends parameters and a seed. Output is deterministic geometry -- same seed always produces the same result. Combined with autonomous NPCs, this means worlds can be both behaviorally AND geometrically procedural.

---

## IV. The Universal Planner

GOAP (Goal-Oriented Action Planning) is not just an NPC behavior technique in Bannou. It is the **universal planning algorithm** used across every creative domain:

| Domain | What GOAP Plans | SDK |
|---|---|---|
| NPC Behavior | Action sequences from goals and world state | `behavior-compiler` |
| Narrative | Story arcs from compressed character archives | `StorylineStoryteller` |
| Music | Compositions from emotional state targets | `MusicStoryteller` |
| Combat Choreography | Dramatic beat sequences from spatial affordances | `CinematicStoryteller` (planned) |
| NPC Economics | Buy/sell/craft/trade decisions from needs and market state | Actor + GOAP |
| Quest Composition | Multi-step quest chains from archive analysis | Storyline + Quest |

One planning paradigm powers all systems. Improvements to the GOAP planner benefit every domain simultaneously. This is a deliberate architectural choice with compounding returns.

---

## V. What Bannou Replaces: A Genre-by-Genre Analysis

### The Common Backend

Every multiplayer game needs the same foundational infrastructure. Bannou provides all of it out of the box:

| System | Traditional Dev Time | Bannou Provides | Notes |
|---|---|---|---|
| **User Authentication** (email, OAuth, Steam, MFA) | 2-4 weeks | Auth + Account (L1) | JWT, TOTP MFA, 4 OAuth providers, session management |
| **Real-time Networking** (WebSocket, binary protocol) | 4-8 weeks | Connect (L1) | 31-byte binary header, zero-copy routing, client-salted GUIDs, reconnection |
| **Permission System** (RBAC, capability manifests) | 2-4 weeks | Permission (L1) | Precompiled manifests, dynamic per-session capabilities |
| **State Persistence** (Redis, MySQL, SQLite) | 2-4 weeks | State (L0) | 4 backends, optimistic concurrency, TTL, specialized interfaces |
| **Pub/Sub Messaging** (event bus, subscriptions) | 2-3 weeks | Messaging (L0) | RabbitMQ with InMemory testing mode, crash-fast philosophy |
| **Service Mesh** (discovery, routing, circuit breakers) | 3-6 weeks | Mesh (L0) | YARP-based, automatic service discovery, health monitoring |
| **Asset Storage** (upload, CDN, versioning) | 2-4 weeks | Asset (L3) | MinIO/S3, pre-signed URLs, bundles, transcoding pools |
| **Voice Chat** (WebRTC, SFU) | 4-8 weeks | Voice (L3) | P2P + Kamailio SFU, automatic tier upgrade |
| **Deployment/Orchestration** | 4-8 weeks | Orchestrator (L3) | Docker Compose/Swarm/Portainer/K8s, preset topologies, processing pools |
| **Save System** | 2-4 weeks | Save-Load (L4) | Delta saves, versioned writes, migration, cloud sync |
| **Matchmaking** | 3-6 weeks | Matchmaking (L4) | Ticket-based, skill windows, party support, accept/decline flow |
| **Leaderboards** | 1-2 weeks | Leaderboard (L4) | Redis sorted sets, seasonal rotation, polymorphic entities |
| **Achievement System** | 1-2 weeks | Achievement (L4) | Progressive/binary, rarity, platform sync (Steam/Xbox/PS) |
| **Analytics** | 2-4 weeks | Analytics (L4) | Event aggregation, Glicko-2 ratings, milestone detection |
| **Chat System** | 2-4 weeks | Chat (L1) | Typed rooms, rate limiting, moderation, persistent/ephemeral |

**Subtotal for common infrastructure**: 36-73 weeks of traditional development time, provided by Bannou's L0-L3 layers with zero game-specific code.

### Case Study: A Zelda-Like (Single-Player Action RPG)

A game like *Breath of the Wild* or *Tears of the Kingdom* -- open world, real-time combat, item crafting, cooking, equipment, puzzles, quests, and a save system.

| System | Traditional Dev | Bannou Replacement | Dev Time Saved |
|---|---|---|---|
| Item/Equipment system | 4-6 weeks | Item + Inventory + Affix | ~95% |
| Currency/Economy | 2-3 weeks | Currency | ~95% |
| Quest/Objective tracking | 4-8 weeks | Quest (over Contract) | ~90% |
| Save/Load with versioning | 3-5 weeks | Save-Load | ~95% |
| Crafting/Cooking | 3-5 weeks | Craft + Workshop | ~85% |
| NPC behavior/schedules | 6-12 weeks | Actor + ABML + GOAP | ~80% |
| World state persistence | 2-4 weeks | State + Worldstate | ~90% |
| Spatial data management | 3-5 weeks | Mapping + Scene | ~85% |
| Location/Map system | 2-3 weeks | Location + Transit | ~90% |
| Procedural content | 4-8 weeks | Procedural + Loot | ~80% |
| Progressive unlock/abilities | 3-5 weeks | Seed + License + Collection | ~85% |
| **Application infrastructure** | 8-12 weeks | Auth + Connect + State + Save-Load | ~95% |
| **Networking layer** | 6-10 weeks | Connect + Mesh + Messaging | ~95% |

**Traditional total**: ~50-86 weeks of systems + infrastructure development.
**With Bannou**: ~5-10 weeks of integration, behavior authoring, and configuration.
**Estimated savings**: ~88% of backend/systems development time.

**What the Zelda developer actually builds**:
- Visual presentation (3D models, animations, shaders, VFX, UI)
- Level/world design (terrain, dungeons, puzzle layouts)
- ABML behavior documents for NPCs and enemies
- Seed data (item templates, crafting recipes, quest definitions, loot tables)
- Game-specific combat feel (animation timing, hitboxes, camera work)
- Client-side rendering of Bannou's cinematic/behavior outputs

### Case Study: A Final Fantasy-Like (Party-Based RPG with Economy)

A game like *Final Fantasy XII* or *XIV* -- party-based combat, deep progression systems, living economy, matchmaking, guilds, housing, and extensive narrative.

| System | Traditional Dev | Bannou Replacement | Dev Time Saved |
|---|---|---|---|
| Character progression (jobs, skills) | 6-10 weeks | Seed + License (license boards!) + Status | ~90% |
| Party/Guild system | 4-6 weeks | Faction + Organization + Game-Session | ~85% |
| Deep crafting with specialization | 4-8 weeks | Craft + Workshop + Item + Affix | ~85% |
| Auction house / Market board | 4-8 weeks | Market + Escrow + Currency | ~90% |
| Multi-currency economy | 3-5 weeks | Currency (multi-currency wallets, exchange rates, holds) | ~95% |
| Quest chains with prerequisites | 6-10 weeks | Quest + Contract + Storyline | ~85% |
| Matchmaking / Duty Finder | 4-6 weeks | Matchmaking + Game-Session | ~90% |
| Player housing | 6-10 weeks | Gardener + Scene + Seed + Inventory | ~80% |
| Music system | 4-6 weeks | Music SDK (procedural, theory-grounded) | ~90% |
| Achievement/Trophy system | 2-3 weeks | Achievement + Collection | ~95% |
| Narrative/Cutscenes | 6-12 weeks | Storyline + Cinematic SDK | ~70% |
| NPC economy (vendors, supply/demand) | 4-8 weeks | Currency + Trade + Workshop + GOAP | ~85% |
| Voice chat for raids | 3-5 weeks | Voice | ~95% |
| Leaderboards / Rankings | 1-2 weeks | Leaderboard + Analytics | ~95% |
| Save/Profile system | 3-5 weeks | Save-Load | ~95% |
| **Application infrastructure** | 10-16 weeks | Full L0-L1 stack | ~95% |
| **Networking / Multiplayer** | 8-14 weeks | Connect + Mesh + Messaging | ~95% |

**Traditional total**: ~78-134 weeks of systems + infrastructure development.
**With Bannou**: ~8-15 weeks of integration, behavior authoring, seed data, and extensions.
**Estimated savings**: ~90% of backend/systems development time.

**What the Final Fantasy developer actually builds**:
- Visual presentation (character models, environments, UI, VFX)
- Job/class extension plugins (L5 facades over Seed + License + Status)
- ABML behavior documents for combat AI, NPC schedules, boss encounters
- Seed data (item templates, license boards, crafting recipes, currency definitions, matchmaking queues)
- Narrative content (scenario definitions for Storyline, quest templates for Quest)
- Game-specific combat choreography (cinematic templates, QTE designs)
- The client-side game engine (rendering, input, audio playback)

### The Pattern Across Genres

| Genre | Bannou Coverage | Estimated Savings | Key Remaining Work |
|---|---|---|---|
| **ARPG** (Zelda, Dark Souls) | Items, quests, economy, saves, NPC behavior, spatial data | ~88% | Combat feel, visual presentation, level design |
| **MMORPG** (FF XIV, WoW) | Full multiplayer stack + economy + progression + housing | ~90% | Client engine, visual content, class extensions |
| **Survival** (Valheim, Rust) | Crafting, building, economy, NPC AI, voice, saves | ~85% | Survival mechanics extensions, environmental visuals |
| **Factory** (Factorio, Satisfactory) | Workshop production, Transit logistics, Utility networks | ~90% | Visual factory rendering, UI for production chains |
| **Farming Sim** (Stardew Valley) | Workshop (lazy eval farming), economy, NPC relationships | ~85% | Crop visuals, seasonal art, social dialogue content |
| **Social Sim** (Animal Crossing) | Chat, relationships, housing (Gardener + Scene), economy | ~80% | Art style, social interaction UI, seasonal events |
| **Monster Raiser** (Pokemon) | Collection, Character, Seed (creature growth), GOAP battles | ~82% | Creature design, capture mechanics, battle presentation |
| **Strategy** (Civilization) | Faction, Currency, Transit, Worldstate, Workshop, Trade | ~80% | Map rendering, strategy UI, turn/real-time bridge |
| **Horror** (Resident Evil) | Save-Load, Item/Inventory, Actor (enemy AI), Scene | ~75% | Atmosphere, level design, horror-specific mechanics |

---

## VI. What the Developer Actually Does

Bannou eliminates systems development, networking, and application infrastructure. What remains is the creative work that makes each game unique.

### 1. Author ABML Behavior Documents

The primary creative act in a Bannou game. ABML is a YAML-based DSL where developers define:
- **NPC goals**: What characters want (eat, trade, guard, explore, craft)
- **Actions**: What characters can do (move, talk, buy, attack, build)
- **Conditions**: When actions are available (`${personality.aggression} > 0.7 AND ${world.time_of_day} == "night"`)
- **God behaviors**: How regional watchers curate the game world
- **Boss encounters**: Multi-phase combat behaviors with cinematic triggers

The GOAP planner handles the *how*. The developer specifies the *what* and the *when*.

### 2. Compose Seed Data

The game world is defined by configuration, not code:
- **Item templates**: What items exist, their properties, rarity, quantity models
- **Crafting recipes**: Workshop blueprints with source/destination inventories
- **Species definitions**: Playable and NPC races with trait modifiers
- **Location hierarchies**: World structure (regions, cities, buildings, rooms)
- **Currency definitions**: Economy currencies with scope and exchange rules
- **Quest templates**: Objectives, rewards, and prerequisite chains
- **License boards**: Skill tree grid layouts with adjacency and costs
- **Loot tables**: Weighted drop determination with contextual modifiers
- **Seed types**: Growth systems for any progressive mechanic
- **Transit modes**: How things move (walk, ride, teleport, sail)
- **Collection types**: What can be cataloged (bestiary, music, recipes)
- **Faction definitions**: Political entities with governance and norms

All seed data is loaded via API calls at startup or through bulk seeding endpoints. No code changes, no recompilation, no redeployment.

### 3. Build the Visual Layer

Everything the player sees and hears:
- 3D models, textures, animations, VFX
- UI/UX design and implementation
- Audio rendering of MIDI-JSON from the Music SDK
- Client-side interpretation of cinematic sequences
- World rendering from Scene/Mapping data
- Input handling and camera systems

### 4. Write Game-Specific Extensions (If Needed)

For repeated composition patterns, create L5 Extension plugins:
- **Semantic facades** that translate domain language to primitive calls
- **View aggregators** that assemble cross-service data for specific UI screens
- **Variable providers** that expose game-specific computed state to NPC behaviors

Extensions follow the same schema-first pipeline: write an OpenAPI schema, run `make generate`, implement the thin service layer.

### 5. Configure Deployment

Choose the deployment mode that fits the game:
- **Embedded**: Mobile/console single-player, no server
- **Self-hosted sidecar**: LAN/co-op, runs alongside the game
- **Cloud**: Persistent multiplayer, scales to any player count
- **Hybrid**: Single-player with optional cloud sync

All modes use the same SDK. Switching modes is configuration, not code.

---

## VII. The Content Flywheel: Games That Improve With Age

The most transformative aspect of building on Bannou is access to the **content flywheel** -- a data network effect where more play produces more content, which produces more play.

```
Player Actions ──events──▶ Character History / Realm History
                                    │
                                    ▼
                         Resource Service (compression)
                         Character dies → structured archive
                                    │
                                    ▼
                         Storyline Composer (GOAP planning)
                         Archive data → narrative seeds
                                    │
                                    ▼
                         Regional Watchers / God-Actors
                         Seeds → orchestrated scenarios
                                    │
                                    ▼
                         New Player Experiences
                         (quests, ghosts, memories, lore)
                                    │
                                    ▼
                         More Player Actions ──loops──▶
```

**Year 1**: ~1,000 story seeds from accumulated play.
**Year 5**: ~500,000 story seeds. The game has 500x more content than at launch.

This is not theoretical. The infrastructure is built:
- lib-resource compresses character archives when characters die
- lib-storyline generates narrative seeds from archives using GOAP
- lib-puppetmaster spawns god-actors that evaluate archives and commission scenarios
- lib-quest instantiates those scenarios as playable content
- The loop requires no developer intervention after initial behavior authoring

**A game built on Bannou gets better the longer it runs.** This is not a feature you can bolt on -- it is an emergent property of the architecture. Character death becomes a content-generation event rather than a content-removal event. Every NPC who dies automatically seeds quest hooks, NPC memories, and descendant behaviors.

---

## VIII. The Actor-Bound Entity Pattern: Living Things From Existing Services

Any entity that should progressively awaken -- a dungeon, a weapon, a ship, a haunted building -- follows the same three-stage pattern using only existing services:

```
Stage 1: DORMANT (No Actor, zero runtime cost)
  Entity = seed + physical form. Growth accumulates passively.
  Thousands can exist per world simultaneously.

Stage 2: STIRRING (Event Brain Actor, simplified cognition)
  Seed reaches threshold → Puppetmaster spawns actor.
  ABML behavior runs with null personality providers → instinct defaults.

Stage 3: AWAKENED (Full Character Brain)
  Seed reaches higher threshold → Character created in system realm.
  Actor binds to character. Full personality, memory, relationships activate.
  Entity is now a living thing with opinions and history.
```

**Living weapons require zero new plugins.** Every operation maps to a single existing API call (Item for physical form, Seed for growth, Collection for knowledge, Actor for behavior, Character for identity in the SENTIENT_ARMS system realm, Relationship for wielder bonds, Status for wielder effects). This is the strongest validation of Bannou's composability thesis.

**Dungeons require one plugin** (lib-dungeon) because they need multi-service atomic orchestration (spawn monster + activate trap + shift layout + manifest memory). The orchestration plugin is thin; the services it composes are the existing primitives.

---

## IX. Engine Integration

Bannou generates typed client artifacts for major game engines:

### .NET (Unity, Stride, Godot-C#)
- `BeyondImmersion.Bannou.Client` NuGet package
- Compile-time-safe typed proxies (`client.Auth.LoginAsync()`)
- Disposable typed event subscriptions
- `IBannouClient` interface for DI/mocking

### Unreal Engine (C++)
- Five auto-generated headers: `BannouProtocol.h`, `BannouTypes.h` (814 USTRUCTs), `BannouEnums.h`, `BannouEndpoints.h` (309 endpoints), `BannouEvents.h`
- Binary protocol implementation with RFC 4122 GUID handling
- Regeneration via `make generate-unreal-sdk` synchronized with schema changes

### TypeScript (Web, Electron)
- `@beyondimmersion/bannou-client` package
- Full typed client with promise-based API

### Any Language
- The WebSocket binary protocol is documented and engine-agnostic
- 31-byte request headers, 16-byte response headers
- JSON payloads -- any language with WebSocket and JSON support can integrate

---

## X. What Makes Bannou Different From Alternatives

| Aspect | PlayFab / GameSparks / Nakama | Bannou |
|---|---|---|
| **Architecture** | Cloud-only SaaS | Single binary: embedded, sidecar, or cloud |
| **NPC Intelligence** | None (client-side only) | 100,000+ concurrent AI NPCs with GOAP cognition |
| **Content Generation** | Static, hand-authored | Content flywheel: more play → more content |
| **Creative SDKs** | None | Music, Narrative, Scene, Behavior, Cinematic (procedural generation grounded in formal theory) |
| **Economy** | Basic virtual currency | Multi-currency, escrow, NPC-driven supply/demand, divine intervention |
| **Deployment** | Cloud-only, vendor-locked | Self-hosted, embedded, cloud, hybrid -- zero vendor lock-in |
| **Customization** | Dashboard configuration | Full source code, schema-first extensibility, L5 extension plugins |
| **Progressive Systems** | Level/XP numbers | Seed (generic growth), License (grid boards), Collection (discovery), Agency (UX-as-progression) |
| **Services** | 10-15 features | 76 composable primitives (892+ endpoints) |
| **AI/LLM Dependency** | Some use LLM for NPCs | Zero LLM dependency -- formal theory, deterministic, cacheable, cost-predictable |

---

## Summary: The Developer's Time Budget

For a typical multiplayer game, here is where a Bannou developer's time goes:

| Activity | % of Total Dev Time | What It Covers |
|---|---|---|
| **Visual Presentation** | 40-50% | Models, textures, animations, VFX, UI, audio rendering |
| **ABML Behavior Authoring** | 15-20% | NPC behaviors, boss encounters, god-actor scripts, world rules |
| **Seed Data & Configuration** | 10-15% | Item templates, recipes, species, locations, economies, quest chains |
| **Game-Specific Extensions** | 5-10% | L5 facade plugins for domain vocabulary |
| **Integration & Testing** | 5-10% | Connecting client to Bannou SDK, testing flows |
| **Backend Systems Development** | 0-5% | Nearly zero -- Bannou provides it all |

**The fundamental shift**: Game development becomes **content authoring and visual crafting**, not systems engineering. The developer's job is to make the game *feel* right -- the behavior documents, the compositions, the visual presentation, the unique game identity. The backend systems, networking, persistence, economies, AI, matchmaking, progression, and content generation are handled.

Bannou does not make games for you. It makes the piano. You write the music.

---

*This document captures the Bannou platform aspiration as of March 2026. For architectural details, see [BANNOU-DESIGN.md](BANNOU-DESIGN.md). For the gameplay vision, see [VISION.md](reference/VISION.md). For the player experience design, see [PLAYER-VISION.md](reference/PLAYER-VISION.md). For service hierarchy, see [SERVICE-HIERARCHY.md](reference/SERVICE-HIERARCHY.md).*
