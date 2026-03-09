# Bannou Composition Reference

> **Sources**: `docs/BANNOU-ASPIRATIONS.md`, `docs/reference/ORCHESTRATION-PATTERNS.md`, `docs/plugins/*.md`
> **Do not edit manually** - regenerate with `make generate-docs`

## Composition Model

### The Three Deployment Modes

Bannou is not "a server you connect to." It is a runtime that can host your game's logic anywhere.

#### Cloud Deployment (Traditional)

The familiar model: Bannou runs as a containerized service (Docker Compose, Kubernetes, Portainer, Docker Swarm -- all supported via the Orchestrator's pluggable backend architecture). Game clients connect via WebSocket through the Connect gateway. Services scale horizontally via environment variables -- enable or disable any of the 76 plugins per node without code changes.

```
Game Client ──WebSocket──▶ Connect Gateway ──mesh──▶ 76 Service Plugins
                                                      (distributed across nodes)
```

#### Self-Hosted / Sidecar Deployment

The Satisfactory/Valheim model: Bannou ships as a sidecar binary alongside the game client or dedicated server. Infrastructure backends swap to local alternatives (SQLite instead of MySQL, InMemory message bus instead of RabbitMQ, local mesh routing instead of YARP). A factory game can run on ~15 of the 76 plugins, with Workshop's lazy evaluation computing production retroactively from elapsed game-time on next query -- the server does not need to stay running between play sessions.

```
Game Client + Bannou Sidecar (same machine, localhost:5012)
  └── SQLite for persistence
  └── InMemory pub/sub
  └── any number of plugins loaded
```

#### Embedded / In-Process Deployment

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

### The 76-Plugin Composition Model

Bannou's services are not a monolithic "game server framework." They are **composable primitives** -- small, orthogonal services that combine to create emergent game systems. No single service "is" the economy, or "is" combat, or "is" crafting. These are behaviors that emerge from primitives interacting.

#### The Six Layers

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

#### Why Composition, Not Modules

Traditional game backends offer "modules" -- self-contained features like "the inventory system" or "the matchmaking system." Bannou offers **primitives that compose**:

| Traditional Module | Bannou Composition |
|---|---|
| "Skill System" | Seed (mastery growth) + License (skill tree boards) + Status (active effects) + Collection (ability discovery) + Actor (behavior decisions) |
| "Crafting System" | Craft (recipe orchestration) + Item (materials/outputs) + Inventory (containers) + Contract (multi-step sessions) + Currency (costs) + Affix (modifier operations) |
| "Player Housing" | Gardener (garden lifecycle) + Seed (progression) + Scene (node tree) + Save-Load (delta persistence) + Inventory + Item (furniture) + Permission (visitor access) |
| "Combat System" | Actor (behavior decisions) + Character-Personality (preferences) + Ethology (species instincts) + Status (buffs/debuffs) + Cinematic SDK (choreography) + Agency (player input fidelity) |
| "NPC Economy" | Currency (wallets) + Item + Inventory + Trade (logistics) + Workshop (production) + Market (exchange) + GOAP (NPC decisions) |

**The absence of a housing plugin validates the architecture.** If a feature as visible and complex as player housing requires no new service, the primitive set is genuinely composable rather than theoretically so. The same validation applies to combat, skills, magic, classes, and guilds -- all are compositions of existing primitives, not discrete services.

#### The Extension Pattern

When a studio finds itself repeatedly composing the same primitives in their game server code, they create an **L5 Extension** -- a thin facade that provides game-specific vocabulary over generic primitives. Six extension patterns are supported:

1. **Semantic Facade**: Translates domain language (e.g., `POST /skills/get-level` resolves to a seed domain depth query)
2. **Composition Orchestrator**: Atomically calls multiple primitives (e.g., "craft this item" = consume materials + create output + debit currency + grant XP)
3. **Configuration Hardener**: Registers game-specific types at startup (seed types, collection types, contract templates)
4. **View Aggregator**: Assembles cross-service data into game-specific views (e.g., "character sheet" = character + personality + quest + inventory data)
5. **Event Translator**: Re-publishes primitive events as domain events (e.g., `seed.phase.changed` becomes `skill.level-up`)
6. **Variable Provider**: Exposes computed game state to NPC behaviors via `IVariableProviderFactory`

Extensions follow the same schema-first development, plugin architecture, and testing patterns as core services. They are how studios make Bannou *theirs* without forking it.

---

### The Creative SDK Ecosystem

Bannou's most distinctive contribution is a family of **pure-computation SDKs** that generate creative content procedurally using formal academic theories -- not AI/LLM inference. Every SDK follows the same three-layer pattern:

```
Theory Layer     Pure computation primitives (pitch arithmetic, narrative grammar, spatial reasoning)
                 ↓
Storyteller Layer  GOAP-driven procedural generation (plan a composition from world state)
                 ↓
Composer Layer   Hand-authored content tools (interactive editors, command-based undo/redo)
```

All three layers produce the **same output format**, making hand-authored and procedurally generated content indistinguishable at runtime.

#### Music Generation

| Layer | SDK | What It Does |
|---|---|---|
| Theory | `MusicTheory` | Pitch/interval arithmetic, scales, modes, chord construction, voice leading, MIDI-JSON output |
| Storyteller | `MusicStoryteller` | 6-dimensional emotional state tracking, narrative arc templates, GOAP-planned composition grounded in Lerdahl's Tonal Pitch Space, Huron's ITPRA model, and Juslin's BRECVEMA mechanisms |
| Plugin | `lib-music` (L4) | HTTP API wrapping both SDKs, Redis caching of deterministic compositions |

**Output**: Portable MIDI-JSON. The game engine renders it with any synthesizer. Deterministic when seeded -- same parameters always produce the same composition.

**What this eliminates**: Hand-authored adaptive music state machines (FMOD/Wwise), pre-recorded emotional variants of every track, and the need for a full-time composer for dynamic game music.

#### Narrative Generation

| Layer | SDK | What It Does |
|---|---|---|
| Theory | `StorylineTheory` | Greimas actantial model, Propp's morphology, Barthes' narrative codes -- formal narrative primitives |
| Storyteller | `StorylineStoryteller` | GOAP-planned story arcs from compressed character archives, lazy phase evaluation via continuation points |
| Plugin | `lib-storyline` (L4) | HTTP API, scenario matching, dry-run testing, origin scenario for organic character creation |

**Two execution modes from the same definitions**: Simple Mode (deterministic MMO-style unlocks with periodic server checks) and Emergent Mode (regional watcher god-actors actively searching, scoring, and deciding which characters qualify for which narratives based on their actual life histories).

**What this eliminates**: Hand-scripted quest trigger systems, static branching dialogue trees, and the combinatorial explosion of pre-authoring every world-state/character-history/NPC-context condition that could fire a story event.

#### Scene Composition

| Layer | SDK | What It Does |
|---|---|---|
| Composer | `SceneComposer` | Engine-agnostic editor with undo/redo command stack, multi-select state machine, gizmo abstraction, ViewModel-based UI |
| Storage | `lib-scene` (L4) | Hierarchical scene documents as node trees, exclusive checkout/commit locking, version history, full-text search |
| Distribution | `lib-asset` (L3) | `.bannou` bundles with LZ4 compression, pre-signed MinIO/S3 URLs |

Scenes are authored, stored, and consumed in the same format everywhere -- no baking step, no per-engine format conversion. Engine bridges exist for Stride and Godot, with Unity and Unreal patterns documented.

**What this eliminates**: Per-engine scene export pipelines, format conversion tools, and editor-to-runtime data transformations.

#### Behavior Authoring (ABML)

| Layer | SDK | What It Does |
|---|---|---|
| Compiler | `behavior-compiler` | Multi-phase ABML-to-bytecode compilation, A*-based GOAP planner |
| Runtime | `behavior-expressions` | Variable resolution against 14+ provider namespaces (`${personality.*}`, `${world.*}`, `${quest.*}`, etc.) |
| Plugin | `lib-behavior` (L4) + `lib-actor` (L2) | ABML compilation API, actor execution runtime with 100,000+ NPC scale |

ABML (Arcadia Behavior Markup Language) is a YAML-based DSL where developers define NPC goals, actions, and conditions. The GOAP planner discovers action sequences autonomously -- the developer specifies *what* NPCs want, not *how* they achieve it.

**What this eliminates**: Hand-authored behavior trees, scripted NPC response tables, and the exponential complexity of manually specifying every possible NPC decision path.

#### Cinematic Choreography (Planned)

| Layer | SDK | What It Does |
|---|---|---|
| Theory | `CinematicTheory` (planned) | Dramatic beat structure, tension curves, spatial affordance evaluation, capability matching, camera direction |
| Storyteller | `CinematicStoryteller` (planned) | GOAP-planned choreographic ABML documents from encounter context |
| Runtime | `CinematicInterpreter` (exists) | Streaming composition with continuation points, already in `sdks/behavior-compiler/Runtime/` |
| Plugin | `lib-cinematic` (L4, planned) | HTTP API callable from ABML behaviors |

The runtime infrastructure already exists. The missing piece is the compositional tier that evaluates spatial affordances, character capabilities, and dramatic pacing to produce choreographic sequences. Player agency (the guardian spirit's combat fidelity float) determines how many continuation points become interactive vs. auto-resolving.

#### Procedural 3D Generation

| Component | What It Does |
|---|---|
| `lib-procedural` (L4) | Dispatches parametric generation requests to headless Houdini Digital Asset workers |
| `lib-asset` (L3) | Stores HDAs and generated output bundles |
| `Orchestrator` (L3) | Manages Houdini worker pool scaling |

Artists author Houdini Digital Assets (self-contained parametric templates). The game sends parameters and a seed. Output is deterministic geometry -- same seed always produces the same result. Combined with autonomous NPCs, this means worlds can be both behaviorally AND geometrically procedural.

---

### The Universal Planner

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

### What the Developer Actually Does

Bannou eliminates systems development, networking, and application infrastructure. What remains is the creative work that makes each game unique.

#### 1. Author ABML Behavior Documents

The primary creative act in a Bannou game. ABML is a YAML-based DSL where developers define:
- **NPC goals**: What characters want (eat, trade, guard, explore, craft)
- **Actions**: What characters can do (move, talk, buy, attack, build)
- **Conditions**: When actions are available (`${personality.aggression} > 0.7 AND ${world.time_of_day} == "night"`)
- **God behaviors**: How regional watchers curate the game world
- **Boss encounters**: Multi-phase combat behaviors with cinematic triggers

The GOAP planner handles the *how*. The developer specifies the *what* and the *when*.

#### 2. Compose Seed Data

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

#### 3. Build the Visual Layer

Everything the player sees and hears:
- 3D models, textures, animations, VFX
- UI/UX design and implementation
- Audio rendering of MIDI-JSON from the Music SDK
- Client-side interpretation of cinematic sequences
- World rendering from Scene/Mapping data
- Input handling and camera systems

#### 4. Write Game-Specific Extensions (If Needed)

For repeated composition patterns, create L5 Extension plugins:
- **Semantic facades** that translate domain language to primitive calls
- **View aggregators** that assemble cross-service data for specific UI screens
- **Variable providers** that expose game-specific computed state to NPC behaviors

Extensions follow the same schema-first pipeline: write an OpenAPI schema, run `make generate`, implement the thin service layer.

#### 5. Configure Deployment

Choose the deployment mode that fits the game:
- **Embedded**: Mobile/console single-player, no server
- **Self-hosted sidecar**: LAN/co-op, runs alongside the game
- **Cloud**: Persistent multiplayer, scales to any player count
- **Hybrid**: Single-player with optional cloud sync

All modes use the same SDK. Switching modes is configuration, not code.

---

### The Actor-Bound Entity Pattern: Living Things From Existing Services

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

### Engine Integration

Bannou generates typed client artifacts for major game engines:

#### .NET (Unity, Stride, Godot-C#)
- `BeyondImmersion.Bannou.Client` NuGet package
- Compile-time-safe typed proxies (`client.Auth.LoginAsync()`)
- Disposable typed event subscriptions
- `IBannouClient` interface for DI/mocking

#### Unreal Engine (C++)
- Five auto-generated headers: `BannouProtocol.h`, `BannouTypes.h` (814 USTRUCTs), `BannouEnums.h`, `BannouEndpoints.h` (309 endpoints), `BannouEvents.h`
- Binary protocol implementation with RFC 4122 GUID handling
- Regeneration via `make generate-unreal-sdk` synchronized with schema changes

#### TypeScript (Web, Electron)
- `@beyondimmersion/bannou-client` package
- Full typed client with promise-based API

#### Any Language
- The WebSocket binary protocol is documented and engine-agnostic
- 31-byte request headers, 16-byte response headers
- JSON payloads -- any language with WebSocket and JSON support can integrate

---

## Orchestration Patterns

Bannou's 45+ services have no central "gameplay loop" plugin. Instead, **god-actors** — long-running ABML behavior documents executed by the Actor runtime — orchestrate services into emergent gameplay. Orchestration is authored content (YAML), not compiled code.

**Three patterns every developer should know:**

1. **Actor-Bound Entity Pattern** (Dormant → Event Brain → Character Brain): Any entity that progressively awakens (NPCs, dungeons, weapons, spirits) follows three cognitive stages. Dormant entities are just seeds with zero runtime cost. At a growth threshold, Puppetmaster spawns an event brain actor running simplified ABML. At a higher threshold, a Character is created in a system realm (PANTHEON, DUNGEON_CORES, SENTIENT_ARMS, NEXIUS, UNDERWORLD) and the actor binds to it, gaining full personality, memory, and relationships. No actor relaunch needed — the same ABML behavior progressively activates as variable providers return real data instead of null.

2. **Content Flywheel**: Character death → archive compressed (Resource) → god perceives archive → GOAP evaluates → storyline composed (Storyline) → quest spawned (Quest) → ghost NPC created (Actor) → new player experiences → more deaths → loop. Each step is a god-actor calling a service API. Different gods produce different narratives from the same archive. The loop requires no developer intervention after initial behavior authoring.

3. **Interaction Patterns**: God→Content (perceive archives, compose narratives, spawn quests), God→Economy (monitor velocity, spawn interventions), God→NPC (inject perceptions, amplify behaviors), Dungeon→Combat (spawn monsters, activate traps from ABML), Seed Phase→Cognitive Transition (ISeedEvolutionListener triggers actor spawn or character binding).

**When designing L4 services**: ask "does a god-actor need to consume my events?" Design event schemas with god-actor consumption in mind. **When designing new entity types**: ask "does this follow the actor-bound entity pattern?" If something should progressively awaken, it uses seeds, system realms, and ABML — potentially with zero new plugins (living weapons validate this). The gameplay loop lives in ABML behaviors, not in service code.

For full specifications: [DIVINE.md](../plugins/DIVINE.md), [DUNGEON.md](../plugins/DUNGEON.md), [BEHAVIORAL-BOOTSTRAP.md](../guides/BEHAVIORAL-BOOTSTRAP.md), [ACTOR-BOUND-ENTITIES.md](../planning/ACTOR-BOUND-ENTITIES.md)

---

## Service Registry

| Service | Layer | Role | EP |
|---------|-------|------|----|
| Mesh | L0 | Service-to-service invocation via YARP with circuit breaking and Redis-backed discovery | 8 |
| Messaging | L0 | RabbitMQ pub/sub infrastructure (IMessageBus/IMessageSubscriber) with in-memory testing mode | 4 |
| State | L0 | Unified state persistence (Redis/MySQL/SQLite/InMemory) with optimistic concurrency and specialized interfaces | 9 |
| Telemetry | L0 | OpenTelemetry distributed tracing and metrics via ITelemetryProvider (optional, NullTelemetryProvider fallback) | 2 |
| Account | L1 | Internal user account CRUD (never internet-facing; external access via Auth only) | 18 |
| Auth | L1 | Internet-facing authentication (email, OAuth, Steam, JWT, MFA, session management) | 19 |
| Chat | L1 | Universal typed message channel primitives with ephemeral and persistent storage | 32 |
| Connect | L1 | WebSocket edge gateway with zero-copy binary routing, client-salted GUIDs, and multi-node relay | 7 |
| Contract | L1 | Binding agreements with milestone progression, consent flows, and prebound API execution | 31 |
| Permission | L1 | RBAC capability manifest compilation from service x state x role permission matrices | 8 |
| Resource | L1 | Cross-layer reference tracking, cleanup coordination (CASCADE/RESTRICT/DETACH), and hierarchical compression | 17 |
| Actor | L2 | NPC behavior execution runtime (ABML, GOAP, perception queues, variable providers, dynamic character binding) | 17 |
| Character | L2 | Game world character management with realm partitioning and system realm support | 12 |
| Collection | L2 | Universal content unlock and archive system with DI-dispatched unlock listeners | 22 |
| Currency | L2 | Multi-currency economy (wallets, transfers, exchange rates, holds, escrow integration) | 34 |
| Game Service | L2 | Registry of available games/applications with stub-name lookup | 5 |
| Game Session | L2 | Multiplayer session containers (lobby/matchmade) with reservation tokens and shortcut publishing | 11 |
| Inventory | L2 | Container and item placement with constraint models (slot/weight/grid/volumetric/unlimited) | 16 |
| Item | L2 | Dual-model items -- templates (definitions) and instances (occurrences) with quantity models and binding | 16 |
| Location | L2 | Hierarchical location tree within realms (cities, regions, buildings, rooms) with depth tracking | 25 |
| Quest | L2 | Objective-based progression as thin orchestration over Contract with prerequisite provider extensibility | 18 |
| Realm | L2 | Top-level persistent world management with deprecation lifecycle and seed-from-configuration | 13 |
| Relationship | L2 | Entity-to-entity relationships with type taxonomy, bidirectional uniqueness, and ABML variable provider | 21 |
| Seed | L2 | Generic progressive growth primitive with polymorphic ownership and phase-gated capabilities | 24 |
| Species | L2 | Realm-scoped species definitions with trait modifiers and deprecation lifecycle | 13 |
| Subscription | L2 | Account-to-game access mapping with time-limited subscriptions and expiration worker | 7 |
| Transit | L2 | Geographic connectivity graph, transit modes, and game-time journey tracking | 33 |
| Worldstate | L2 | Per-realm game clock, calendar system, temporal events, and ${world.*} variable provider | 18 |
| Asset | L3 | Binary asset storage (MinIO/S3) with bundles, pre-signed URLs, and transcoding pool | 20 |
| Broadcast | L3 | Streaming platform integration for live content broadcasting | — |
| Documentation | L3 | Knowledge base API for AI agents with full-text search and git-sync namespaces | 27 |
| Orchestrator | L3 | Deployment orchestration (Docker/Swarm/Portainer/K8s) with processing pools and routing broadcasts | 23 |
| Voice | L3 | Voice room coordination with P2P mesh, Kamailio SFU, and automatic tier upgrade | 11 |
| Website | L3 | Public-facing browser CMS (news, profiles, downloads) using REST patterns — currently stubbed | 14 |
| Achievement | L4 | Achievement/trophy system with progressive/binary types, rarity calculation, and platform sync | 12 |
| Affix | L4 | Item modifier definition and procedural generation for equipment customization | — |
| Agency | L4 | Guardian spirit progressive agency and UX manifest engine for player capability unlocking | — |
| Analytics | L4 | Event aggregation, Glicko-2 skill ratings, and milestone detection (event-only observer) | 9 |
| Arbitration | L4 | Dispute resolution orchestration composing Contract/Faction primitives for jurisdictional rulings | — |
| Behavior | L4 | ABML compiler (YAML to bytecode), A*-based GOAP planner, and 5-stage cognition pipeline | 6 |
| Character Encounter | L4 | NPC encounter memory with per-participant perspectives, time-decay, and sentiment aggregation | 21 |
| Character History | L4 | Historical event participation and machine-readable backstory for behavior system consumption | 12 |
| Character Lifecycle | L4 | Generational cycle orchestration (aging, marriage, procreation, death, genetic inheritance) | — |
| Character Personality | L4 | Personality traits (bipolar axes) and combat preferences with probabilistic evolution | 12 |
| Craft | L4 | Recipe-based crafting orchestration composing Item, Inventory, Contract, Currency, and Affix | — |
| Director | L4 | Human-in-the-loop event coordination (Observe/Steer/Drive tiers) for live content management | — |
| Disposition | L4 | Emotional synthesis — per-character feelings about entities and long-term aspirational drives | — |
| Divine | L4 | Pantheon management, divinity economy, blessing orchestration (composes Currency/Seed/Collection/Status) | 22 |
| Dungeon | L4 | Living dungeon-as-actor lifecycle with personality, memory, and spatial manifestation | — |
| Environment | L4 | Weather simulation, temperature modeling, and ecological resources consuming Worldstate temporal data | — |
| Escrow | L4 | Full-custody multi-party asset exchange with 13-state FSM (currency/items/contracts) | 22 |
| Ethology | L4 | Species-level behavioral archetype registry with hierarchical overrides and individual noise | — |
| Faction | L4 | Seed-based faction growth with norms, enforcement tiers, territory, guild hierarchy, and political bonds | 31 |
| Gardener | L4 | Player experience orchestration (garden lifecycle, void/discovery, divine actor integration) | 23 |
| Hearsay | L4 | Social information propagation and NPC belief formation (what NPCs think they know vs. reality) | — |
| Leaderboard | L4 | Redis Sorted Set leaderboards with seasonal rotation, polymorphic entities, and Analytics ingestion | 12 |
| Lexicon | L4 | Structured world knowledge ontology with concept decomposition and strategy implications | — |
| License | L4 | Grid-based progression boards (skill trees) orchestrating Inventory, Items, and Contracts | 20 |
| Loot | L4 | Loot table management with weighted drops, contextual modifiers, and pity thresholds | — |
| Mapping | L4 | Spatial data management with authority-based channels, 3D indexing, and design-time authoring | 19 |
| Market | L4 | Marketplace orchestration (auctions, NPC vendors, price discovery) composing Escrow/Currency/Item | — |
| Matchmaking | L4 | Ticket-based matchmaking with skill windows, party support, accept/decline, and auto-requeue | 11 |
| Music | L4 | Pure-computation music generation using formal theory (MusicTheory + MusicStoryteller SDKs) | 8 |
| Obligation | L4 | Contract-aware GOAP action cost modifiers for NPC "second thoughts" before violating obligations | 11 |
| Organization | L4 | Legal entity management (shops, guilds, households as first-class economic and social actors) | — |
| Procedural | L4 | Procedural 3D asset generation via headless Houdini Digital Assets with deterministic output | — |
| Puppetmaster | L4 | Dynamic behavior orchestration, regional watchers, and IBehaviorDocumentProvider hierarchy bypass | 6 |
| Realm History | L4 | Realm historical events and lore management with archival text summarization | 12 |
| Save Load | L4 | Versioned save system with delta saves, schema migration, two-tier storage (Redis then MinIO), cloud sync | 26 |
| Scene | L4 | Hierarchical scene documents as node trees with checkout/commit workflow and version history | 19 |
| Showtime | L4 | In-game streaming metagame (simulated audiences, hype trains, streamer career progression) | — |
| Status | L4 | Unified entity effects query layer aggregating contract statuses and seed capabilities | 19 |
| Storyline | L4 | Seeded narrative generation from compressed archives via storyline-theory/storyteller SDKs | 15 |
| Trade | L4 | Economic logistics -- goods movement over game-time, border policies, supply/demand dynamics | — |
| Utility | L4 | Infrastructure network topology, flow calculation, and coverage cascading (aqueducts, power grids) | — |
| Workshop | L4 | Time-based automated production with lazy evaluation and background materialization | — |

**76 services, 903 endpoints**

For full per-service details: `docs/GENERATED-*-SERVICE-DETAILS.md`

---

*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for architectural context.*
