# Bannou / Arcadia: The Vision

> **Purpose**: This document distills the ultimate objectives, system interdependencies, and non-negotiable design principles of the Bannou platform and its flagship game, Arcadia. It exists to prime agents and humans alike with the "big picture" so that implementation work -- audits, tickets, cross-cutting concerns -- never loses sight of why these systems exist and how they must work together.
>
> **Sources**: Arcadia planning documentation (arcadia-kb), Bannou planning documentation (docs/planning), Bannou guides (docs/guides), Bannou plugin deep dives (docs/plugins).
>
> **This is not a technical specification.** For architecture details, see BANNOU-DESIGN.md. For tenets, see TENETS.md. For service hierarchy, see SERVICE-HIERARCHY.md. This document answers "what are we building and why" at the highest level.

---

## The One-Paragraph Vision

Bannou is a monoservice game backend platform (75 services, 892+ endpoints) that provides everything a multiplayer game needs -- authentication, economies, inventories, quests, matchmaking, voice, spatial data, save/load -- combined with an autonomous NPC intelligence stack capable of running 100,000+ concurrent AI-driven characters. Its flagship game, Arcadia, uses this platform to create **living worlds where content is generated from accumulated play history, not hand-authored by designers**. NPCs think, remember, evolve, form relationships, run economies, and generate emergent narratives. Dungeons are conscious entities. Gods curate regional flavor. Characters age, marry, have children, and die -- and their compressed life archives seed future content for other players. The longer a world runs, the richer it becomes. This is the fundamental thesis: **more play produces more content, which produces more play**.

---

## The Five North Stars

Every system, every service, every line of code should ultimately serve one or more of these goals. If work doesn't connect back to at least one, question whether it belongs.

### 1. Living Game Worlds

The world is alive whether or not a player is watching. NPCs have autonomous cognition pipelines (perception, appraisal, memory, goal evaluation, intention formation). They pursue their own aspirations, form relationships, run businesses, participate in politics, and generate emergent stories. Regional Watcher "gods" (Moira/Fate, Thanatos/Death, Silvanus/Forest, Ares/War, Typhon/Monsters, Hermes/Commerce) monitor event streams with aesthetic preferences and orchestrate narrative opportunities. The world has genuine history that was simulated, not authored.

**Systems that serve this**: Actor, Behavior (ABML/GOAP), Character Personality, Character Encounter, Character History, Realm History, Puppetmaster, Relationship, Storyline, Analytics, Generational Cycles.

### 2. The Content Flywheel

Traditional games have finite, hand-authored content consumed once. Arcadia generates content from accumulated play history. When a character dies, their compressed archive becomes generative input -- ghosts, undead, quests, NPC memories, and legacy mechanics all emerge from real play data. Year 1 yields ~1,000 story seeds; Year 5 yields ~500,000. Content generation accelerates with world age. This is a data network effect applied to game content.

**Systems that serve this**: Resource (compression), Character History, Realm History, Storyline (GOAP-driven narrative from archives), Dungeon-as-Actor (memory manifestation), Generational Cycles, Death/Underworld mechanics.

### 3. 100,000+ Concurrent AI NPCs

Not a nice-to-have -- a requirement for the living world to work. The architecture (horizontal actor pool scaling, zero-allocation bytecode VM, bounded perception queues, Variable Provider Factory for cross-layer data access, direct RabbitMQ event delivery) is designed specifically for this scale. Each NPC is a long-running cognitive process making decisions every 100-500ms.

**Systems that serve this**: Actor (pool deployment modes), Behavior (ABML compiler + bytecode VM), Orchestrator (processing pool management), Puppetmaster (dynamic behavior loading), lib-messaging (event delivery at scale).

### 4. Ship Games Fast

Eliminate the 6-18 month backend development phase for game studios. 75 integrated services covering every backend need. Schema-first development means new services go from concept to production in under a day. Auto-generated SDKs for Unity, Unreal, Godot, Stride. Single binary deployment configurable from monolith to fully distributed microservices via environment variables.

**Systems that serve this**: Schema-first code generation (184 maintained YAML schemas -> 400+ generated files, ~5x line amplification), Plugin architecture, Monoservice deployment, Orchestrator (environment management), auto-generated client SDKs.

### 5. Emergent Over Authored

The fundamental design principle. Quests, economies, social dynamics, combat choreography, music, narrative arcs, and dungeon layouts all emerge from autonomous systems interacting -- not from scripted triggers or hand-placed content. The game designer's role shifts from authoring content to designing the systems that generate content.

**Systems that serve this**: GOAP planning (used across NPC behavior, narrative generation, music composition, combat choreography), Event Brain (procedural cinematic exchanges), Dungeon-as-Actor, Regional Watchers, Crafting simulation, World-state-driven scenarios.

---

## How The Systems Must Work Together

This section describes the critical interdependencies. These are not optional integrations -- they are load-bearing connections that define what makes Arcadia work.

### System Realms: Metaphysical Entities as Characters

The single most powerful architectural pattern in Bannou is the **System Realm**. Realm's `isSystemType` flag creates conceptual namespaces — non-physical spaces where metaphysical entities exist as first-class Characters. A Character in a system realm gets the *entire* L2/L4 entity stack for free:

| Capability | Service | What System Realm Characters Get |
|------------|---------|----------------------------------|
| Identity | Character (L2) | Name, realm binding, species, alive/dead lifecycle |
| Personality | Character Personality (L4) | Quantified traits, available as `${personality.*}` |
| Memory | Character Encounter (L4) | Notable interactions, sentiment tracking |
| History | Character History (L4) | Backstory, historical event participation |
| Growth | Seed (L2) | Progressive capabilities via seed growth pipeline |
| Knowledge | Collection (L2) | Content unlocks, vocabulary, permanent knowledge |
| Bonds | Relationship (L2) | Typed relationships with other entities |
| Cognition | Actor (L2) | Full ABML behavior execution with all variable providers |

**Zero new infrastructure required.** Every service that works with Characters works with system realm characters automatically.

#### Planned System Realms

| System Realm | Purpose | Entities | Key Integration |
|--------------|---------|----------|-----------------|
| **PANTHEON** | Gods and deities | Divine characters with domain power seeds | Content flywheel orchestration via god-actors |
| **NEXIUS** | Guardian spirits | Player account spirits with progressive agency seeds | Agency UX manifest, pair bonds, spirit personality |
| **DUNGEON_CORES** | Sentient dungeons | Awakened dungeon entities with mana economies | Dungeon-as-Actor pattern, master bonds |
| **SENTIENT_ARMS** | Living weapons | Awakened weapon entities with wielder bonds | Zero-plugin entity type (proves composability) |
| **UNDERWORLD** | Dead characters | Post-mortem afterlife entities | Content flywheel amplifier (death → afterlife gameplay → living world narrative) |

#### The Unified Cognitive Progression

All entities that can become autonomous agents — NPCs, gods, dungeons, weapons, guardian spirits — follow the same three-stage pattern:

```
Stage 1: DORMANT (No Actor)
  Entity exists as seed + physical form (item, location, nothing).
  Growth accumulates passively from game events. Zero runtime cost.
  Thousands can exist per world simultaneously.
          │
          ▼  [seed reaches Stirring threshold]
Stage 2: STIRRING / EVENT BRAIN (Actor, No Character)
  Puppetmaster spawns an event brain actor running ABML behavior.
  Entity perceives, decides, acts — but without rich cognitive data.
  ${personality.*} resolves to null → instinct-only defaults.
          │
          ▼  [seed reaches Awakened threshold]
Stage 3: AWAKENED / CHARACTER BRAIN (Actor + Character in System Realm)
  Character created in appropriate system realm. Actor binds via
  /actor/bind-character — NO relaunch needed, same ABML document.
  Full L2/L4 variable providers activate on next tick:
  ${personality.*}, ${encounters.*}, ${backstory.*}, ${quest.*},
  ${world.*}, ${obligations.*}, and all domain-specific providers.
  Entity now has personality, memories, grudges, aspirations.
```

The same ABML behavior document works at every stage — null-safe expressions that return real data only after cognitive awakening. A weapon's behavior file doesn't change when it becomes sentient; the variable providers simply start returning data instead of null.

**Why this matters**: Gods, guardian spirits, dungeons, weapons, and the dead are *not* special cases requiring custom infrastructure. They are Characters in conceptual spaces. This collapses entire categories of "how does X integrate with Y" questions into a single answer: "it's a Character in a system realm — everything works." The Dungeon Ecosystem, Content Flywheel, and Economy diagrams below all depend on entities following this pattern. Living weapons — the validation case — require **zero new Bannou plugins**, composing entirely from Item, Seed, Collection, Actor, Character, Relationship, and the existing variable provider chain.

For detailed specifications, see [DIVINE.md](../plugins/DIVINE.md) (god characters, PANTHEON realm), [ACTOR-BOUND-ENTITIES.md](../planning/ACTOR-BOUND-ENTITIES.md) (unified 3-stage progression, living weapons), and [ORCHESTRATION-PATTERNS.md](ORCHESTRATION-PATTERNS.md) (bootstrap sequence, god-actor interaction patterns).

### The NPC Intelligence Stack

```
── Character Self-Model ──
Character Personality (L4)  ──provides ${personality.*}, ${combat.*}──┐
Character Encounter (L4)    ──provides ${encounters.*}────────────────┤
Character History (L4)      ──provides ${backstory.*}─────────────────┤
                                                                      │
── World Awareness ──                                                 │
Worldstate (L2)             ──provides ${world.*}─────────────────────┤
Transit (L2)                ──provides ${transit.*}───────────────────┤
Location (L2)               ──provides ${location.*}──────────────────┤
                                                                      ▼
── Progress & Growth ──                                    Variable Provider Factory
Seed (L2)                   ──provides ${seed.*}──────────────────────┤
Quest (L2)                  ──provides ${quest.*}─────────────────────┤
                                                                      │
── Social Structures ──                                               │
Obligation (L4)             ──provides ${obligations.*}───────────────┤
Faction (L4)                ──provides ${faction.*}───────────────────┤
                                                                      │
── Planned (Pre-Implementation) ──                                    │
Disposition (L4)            ──will provide ${disposition.*}───────────┤
Hearsay (L4)                ──will provide ${hearsay.*}───────────────┤
Lexicon (L4)                ──will provide ${lexicon.*}───────────────┘
                                                                      │
                                                                      ▼
Behavior (L4) ──compiles ABML──▶ Actor (L2) ──executes bytecode──▶ NPC Actions
                                      ▲
                                      │
                                Puppetmaster (L4)
                           (dynamic behavior loading
                            via Asset Service L3)
```

**Why this matters**: NPC behavior expressions like `${personality.aggression} > 0.7 AND ${encounters.last_hostile_days} < 3` require data from **11 implemented variable providers** (spanning L2 and L4) flowing into the L2 Actor runtime without hierarchy violations, with 3 additional providers planned. These providers organize into four cognitive domains: **character self-model** (personality, combat preferences, encounter memories, backstory), **world awareness** (game time, transit routes, location context), **progress and growth** (seed capabilities, quest objectives), and **social structures** (contractual obligations, faction norms). The Variable Provider Factory pattern is the architectural keystone that makes autonomous NPCs possible within the service hierarchy -- each provider is independently implemented, registered via DI, and discovered by Actor's `IEnumerable<IVariableProviderFactory>` at runtime. Break this pattern and you break NPC intelligence.

### The Content Flywheel Loop

```
Player Actions ──events──▶ Character History / Realm History
                                      │
                                      ▼
                           Resource Service (compression)
                           Character dies → archive created
                                      │
                                      ▼
                           Storyline Composer (GOAP planning)
                           Archive data → narrative seeds
                                      │
                                      ▼
                           Regional Watchers / Puppetmaster
                           Seeds → orchestrated scenarios
                                      │
                                      ▼
                           New Player Experiences
                           (quests, ghosts, memories, lore)
                                      │
                                      ▼
                           More Player Actions ──loops back──▶
```

**Why this matters**: This is the data network effect. Every component in this loop must work for the flywheel to spin. If compression doesn't capture enough fidelity, Storyline has nothing to work with. If Storyline can't generate from archives, Regional Watchers have no scenarios to orchestrate. If Regional Watchers don't orchestrate, the world feels static despite having rich history. The loop is only as strong as its weakest link.

### The Combat Dream

Combat *decisions* are behavioral -- Actor + variable providers + GOAP handle "should I fight, flee, or negotiate?" at 100-500ms cognitive ticks. But combat *choreography* -- "wind up, fake left, strike right, follow through" -- is a distinct computational domain operating at a different temporal scale (5-30s real-time sequences). This parallels Music and Storyline exactly: a pure-computation SDK handles the domain logic, a thin plugin wraps it as an API, and ABML behaviors decide when to invoke it.

```
Event Brain (Actor) ──queries──▶ Mapping Service (affordances)
        │                              "What objects within 5m can be grabbed?"
        │                              "Are there elevation changes for dramatic leaps?"
        │
        ├──queries──▶ Character Variable Providers
        │                 ${personality.*}, ${combat.*}, ${encounters.*}
        │                 "What can this character do? What would they choose?"
        │
        ├──calls──▶ lib-cinematic (L4, planned)
        │              /cinematic/compose     → participants + environment → sequence
        │              /cinematic/extend      → continuation point + new context → extension
        │              /cinematic/resolve-input → QTE + player input → outcome + branch
        │                    │
        │                    ▼
        │              CinematicTheory SDK (planned, extends behavior-compiler)
        │              CinematicInterpreter (EXISTS in sdks/behavior-compiler/Runtime/)
        │                 Streaming composition with continuation points
        │
        └──gated by──▶ Agency: ${spirit.domain.combat.fidelity}
                         None:   full choreography, zero QTEs (watch character fight)
                         Low:    rare, simple decision points (approach/retreat)
                         Medium: timing windows, moderate QTEs (dodge/parry/strike)
                         High:   stance + combo direction (rich decision points)
                         Master: full martial choreography (dense QTE sequences)
```

This follows an established pattern across multiple computational domains:

| Domain | SDK (Pure Computation) | Plugin (API Wrapper) | ABML Decides |
|--------|------------------------|---------------------|--------------|
| **Music** | MusicTheory + MusicStoryteller | lib-music (L4) | "play music for this mood" |
| **Narrative** | StorylineTheory + StorylineStoryteller | lib-storyline (L4) | "compose a story from this archive" |
| **Spatial** | VoxelBuilder | lib-procedural (L4) | "expand this dungeon wing" |
| **Choreography** | CinematicTheory (planned) | lib-cinematic (L4, planned) | "initiate combat encounter" |

**Connection to Progressive Agency**: QTE/decision points in cinematics ARE the combat domain of PLAYER-VISION.md's progressive UX expansion. The cinematic plugin reads `${spirit.domain.combat.fidelity}` from Agency to determine interaction density -- same choreography computation, different player interaction windows based on the spirit's earned understanding. A new spirit watches their character fight autonomously (the character was always capable; the spirit simply couldn't perceive or influence combat structure). A master-level spirit directs full martial choreography in real-time. This directly implements the "agency is earned context" gradient.

**Why this matters**: THE DREAM is that combat feels like a choreographed cinematic generated in real-time from actual environment, character capabilities, and player input. The CinematicInterpreter already exists in the behavior-compiler SDK -- it wraps BehaviorModelInterpreter with streaming composition and continuation point support. What's missing is the higher-level composition logic (CinematicComposer: affordance evaluation, capability matching, dramatic pacing, agency-gated QTE insertion) and the thin API wrapper plugin (lib-cinematic). The architectural path is clear and follows proven patterns. Remove any component and combat becomes mechanical rather than cinematic.

### The Dungeon Ecosystem

```
Dungeon Core (Actor, event_brain type)
        │
        ├──perceives──▶ Domain-scoped events (intrusion, combat, death, loot)
        │
        ├──decides──▶ ABML cognition pipeline (simplified 3-stage)
        │                 ${dungeon.mana_reserves}, ${dungeon.genetic_library.*}
        │
        ├──acts──▶ Spawn monsters (pneuma echoes), activate traps,
        │          seal passages, shift layout, manifest memories
        │
        ├──bonds──▶ Dungeon Master (character) via Contract Service
        │              Bidirectional: dungeon perceives, master directs
        │
        └──remembers──▶ Memory system with physical manifestation
                         Paintings on walls (Scene), data crystals (Item),
                         environmental effects (Mapping)
```

**Why this matters**: Dungeons-as-Actors prove the architecture's generality. If the Actor system can run NPC brains AND dungeon intelligences AND Regional Watchers AND Event Brains, the platform genuinely supports "any autonomous entity." The dungeon master bond introduces actor-to-actor partnerships -- a new pattern with implications for any system where two autonomous entities need to cooperate.

### The Economy as Living System

```
Currency (L2) ──wallets, holds, exchange rates──▶ Player/NPC transactions
     │                                                    │
     ├──autogain worker──▶ Passive income generation      │
     │                                                    │
     ├──escrow endpoints──▶ Escrow (L4)                   │
     │                      Multi-party atomic exchanges   │
     │                              │                      │
     │                              ▼                      │
     │                    Contract (L1) ── FSM + consent   │
     │                                                    │
Item (L2) + Inventory (L2) ──item movement──▶ Trade/Loot  │
     │                                                    │
     └──NPC GOAP decisions──▶ NPCs buy/sell/craft/trade───┘
           based on needs, aspirations, personality
```

**Why this matters**: The economy must be NPC-driven, not player-driven. Supply, demand, pricing, and trade routes emerge from NPC behavior -- what they need, what they produce, what they want. Player economies layer on top of this NPC economic substrate. If the economy is just player-to-player, the world feels dead when players are offline. God-actors (Hermes/Commerce) can manipulate currency velocity through narrative events -- "divine economic intervention."

### The Social Fabric

```
Lexicon (L4) ──concept ontology──▶ Chat (L1) custom "lexicon" room type
     │                                    │
     │                              Validates structured messages:
     │                              [INTENT] + [SUBJECT]* + [MODIFIER]* + [CONTEXT]*
     │                                    │
     ├──discovery gating──▶ Collection (L2)
     │                       NPCs can only say what they know
     │                       (vocabulary expands with world experience)
     │                                    │
     ├──belief propagation──▶ Hearsay (L4)
     │                         Concept-level distortion across social hops:
     │                         [WARN]+[DIREWOLF]+[NORTH_GATE] (direct witness)
     │                           → [WARN]+[CANINE]+[NORTH_GATE] (second-hand)
     │                             → [WARN]+[ANIMAL] (rumor)
     │                                    │
     ├──drive motivations──▶ Disposition (L4)
     │                        Drives create communication needs:
     │                        protect_family → broadcast [WARN]
     │                        master_craft → seek [REQUEST]+[TEACHING]
     │                        gain_wealth → offer [TRADE]+[GOODS]
     │                                    │
     └──social cognition──▶ ${social.*} Variable Provider
                              Recent messages, ambient mood, unanswered questions
                                          │
                                          ▼
                              Actor (L2) ──ABML social behaviors──▶ NPC Actions
                                          ▲
                                          │
                              Game Server batches NPC interactions
                              (bulk API calls solve 100K NPC scale)
```

**Why this matters**: This is how the world *sounds* alive. NPCs gossip, warn each other about dangers, trade information, broadcast needs, and build social networks -- all through structured Lexicon concepts, not free text. Hearsay distortion creates emergent information asymmetry: one NPC witnessed direwolves at the north gate, but by the time the rumor reaches the market district, it's just "some animal threat up north." Disposition drives combined with social messaging produce emergent economic coordination -- hungry NPCs broadcast food requests, aspiring craftsmen seek teaching, merchants advertise goods. These dynamics are the mechanism that makes a market town *feel* different from a frontier outpost, a military camp different from a farming village. Without this layer, the world runs its simulation but doesn't feel inhabited.

Communication uses structured Lexicon entries (concept tuples) that are machine-native and GOAP-consumable. The same structured messages that NPCs exchange are progressively revealed to players through the Agency service's UX capability manifest -- early spirits perceive only emotional tones from nearby conversations, while experienced spirits can read full Lexicon vocabulary and eventually compose their own messages. For implementation details, see [Character Communication Guide](../guides/CHARACTER-COMMUNICATION.md).

**Systems that serve this**: Lexicon, Chat, Collection, Hearsay, Disposition, Agency, Actor.

### Procedural Content Generation Pipeline

```
Artist authors HDA (Houdini Digital Asset) ──stored in──▶ Asset Service (L3)
                                                                │
Game server / NPC / World Builder sends request ────────────────┤
  with template ID, parameters, seed                            │
                                                                ▼
                                                   Procedural Service (L4)
                                                   Acquires Houdini worker
                                                   from Orchestrator pool
                                                                │
                                                                ▼
                                                   Headless Houdini execution
                                                   (deterministic: same seed = same output)
                                                                │
                                                                ▼
                                                   Generated asset ──▶ Asset Service
                                                   Optionally bundled (.bannou format)
                                                                │
                                                   ┌────────────┼────────────┐
                                                   ▼            ▼            ▼
                                               Mapping      Scene       Client
                                             (terrain)   (buildings)  (download)
```

**Why this matters**: This turns Bannou from a platform that *manages* pre-made content into one that *generates* content on demand. Combined with autonomous NPCs, this means worlds can be both behaviorally AND geometrically procedural. An NPC builder character could trigger generation of a unique building. A dungeon could grow new chambers through procedural terrain generation. The world physically changes through simulation, not just through hand-placed assets.

---

## The Metaphysical Foundation

Arcadia's game systems are grounded in a consistent metaphysical framework. Understanding this prevents systems from contradicting each other.

### Logos, Pneuma, and Reality

- **Logos**: Pure information particles. The "words" that define what things are. The True Names system is built on this -- knowing something's logos grants some influence over it.
- **Pneuma**: Organized logos with volatile/emergent properties. The spiritual matter of the world. Active pneuma is magical energy; spent pneuma becomes physical matter.
- **Manifest Reality**: Physical world created when pneuma transitions from active to spent state.

### Implications for Game Systems

- **Magic**: Thermodynamically compliant. Magic provides precise control over pneuma, not creation from nothing. Six stages of spellcasting (accumulation, attunement, manifestation, manipulation, concentration, ignition).
- **Dungeon Creatures**: Pneuma echoes, not truly alive. Logos memory seeds given form through dungeon mana. When killed, the pneuma body disperses and only the logos seed remains (explaining respawn and why monsters drop "pieces").
- **Death**: Characters have a soul architecture (logos bundle + pneuma shell + sense of self). Death transforms, not ends. The underworld exists inside the leyline network with aspiration-based afterlife pathways.
- **Guardian Spirits**: Player accounts are fragments of Nexius (Goddess of Connections). Each is a divine shard that manages a household of characters across lifetimes. The spirit evolves by feeding it experiences through gameplay.
- **Gods**: Function as microservices/long-running network tasks monitoring event streams. They spend divinity (finite resource) on blessings for characters who impress them. Regional Watchers are the game-system implementation of gods.
- **Technology**: Mana interferes with electrical potential, making traditional electronics impossible. All technology uses sequential magical transformations via enchantment.

---

## Design Principles That Must Never Be Violated

These are the philosophical commitments that distinguish Arcadia from every other game. Violating them produces a technically functional but spiritually dead product.

### 1. Characters Are Independent Entities

Players do not "own" or "control" characters. The guardian spirit possesses and influences them. Characters have their own NPC brain running at all times. If the player is slow or absent, the character acts autonomously based on personality. Characters can resist or resent being pushed against their nature. This is not a gimmick -- it is the foundation of the dual-agency training system.

### 2. Intentional Inequality

Not every player will have the same experience. Some content will be rare or impossible based on how the world develops. A blacksmith and a warrior have fundamentally different experiences, and that is embraced. Reject artificial balance and homogenization.

### 3. World-State Drives Everything

Available scenarios, quests, and possibilities are determined by the actual simulated state of the world. If there are no forests, there are no lumberjack scenarios. If a town has been destroyed, its quest chains end. Content emerges from simulation, not from scripted triggers.

### 4. Death Creates, Not Destroys

Death is transformation, not punishment. Character death archives become generative input for the content flywheel. The underworld offers its own gameplay. Fulfilled characters contribute more to the guardian spirit's evolution than those who die with unfinished business. The Fulfillment Principle: more fulfilled in life = more logos flow to the guardian spirit.

### 5. Authentic Simulation

All game systems are grounded in real-world physics, chemistry, biology, and social dynamics. 37+ crafting processes mirror actual historical/scientific procedures. Fantasy elements enhance but do not replace natural laws. Magic is thermodynamically compliant.

### 6. GOAP Is The Universal Planner

Goal-Oriented Action Planning (A* search over action spaces) is used for NPC behavior, narrative arc generation, quest chain composition, NPC economic decisions, tutorial adaptation, cinematography, and music composition. One planning paradigm powers many systems. This is a deliberate architectural choice -- it means improvements to the GOAP planner benefit every system simultaneously.

### 7. The Service Hierarchy Is Inviolable

The 6-layer service hierarchy (L0 Infrastructure through L5 Extensions) with strict downward-only dependencies is not just an engineering convenience -- it enables meaningful deployment modes, independent scaling, and clean domain boundaries. The Variable Provider Factory and Prerequisite Provider Factory patterns exist specifically to allow data to flow upward through interfaces without creating forbidden dependencies. Respect the hierarchy or the entire monoservice architecture breaks down.

---

## The Player Experience in One Page

**First Minutes**: A divine shard falls from the sky, hits an object (a staff, a hammer, a cart wheel), and the player's guardian spirit awakens in the body of a child in a living world. There is no character creation screen. The tutorial is the character's entire first life.

**Daily Play**: The player possesses a household member and participates in the world. Characters are always autonomous -- the NPC brain runs at all times, and the player gradually learns to *collaborate with* that autonomy rather than override it. Combat feels collaborative because the character co-pilot has opinions and acts on them. If the player goes idle, the character continues living. Crafting mirrors real processes. Trade emerges from NPC supply and demand.

**When Offline**: The world continues. NPCs live their lives. Seasons change. Politics shift. Generational cycles turn. When the player returns, the ambitious lieutenant became the guard captain, the old baker died, new buildings went up, a plague swept through. None of this was scripted for the player.

**Long-Term Arc**: Across generations, the guardian spirit evolves. Characters age, marry, have children, and die. Family businesses and dynasties persist. Knowledge and traits are inherited through a genetic system. The player manages a household across decades and centuries.

**Multi-Realm**: Omega (cyberpunk meta-dashboard), Arcadia (western RPG/economic simulation), Fantasia (primitive fantasy survival). Knowledge transfers between realms, not resources.

**Endgame**: There is no endgame. The world is the endgame. A server running for 5 years has 500x the content of one running for 1 year. The content flywheel ensures the game gets better the longer it runs.

### Progressive Agency and the UX Capability Manifest

The guardian spirit starts nearly inert. Through accumulated experience across characters and lifetimes, it gains understanding -- and understanding manifests as increased control fidelity and richer UX surface area. The server maintains a **UX capability manifest** per spirit (analogous to the permission capability manifest for service endpoints) that tells the client which interaction modalities are available and at what fidelity. The client renders accordingly: the same underlying simulation, but the UX surface area exposed varies dramatically per player. The server is authoritative; the client cannot unlock things locally. The Agency service (L4) manages this manifest, reading from the guardian spirit's seed growth data and accumulated experience.

This model produces a remarkable consequence: **different players experience what feel like fundamentally different games**, even though they share the same world. A combat-focused spirit sees a rich action RPG with stance selection, timing windows, and combo choreography. A crafting-focused spirit sees an authentic process simulation with 37+ real-world procedures. A social-focused spirit sees a relationship and political simulation. A farming-focused spirit sees detailed soil management and crop planning. The *world* is identical -- the *window into it* varies per spirit. Arcadia is simultaneously an ARPG, a farming game, a monster raiser, and an economic sim because these are different UX manifests on the same simulation.

Every boundary in Arcadia is a gradient, not a wall. Tutorial to gameplay is the entire first generation of limited agency. Idle to active play is a continuous spectrum of spirit influence over autonomous characters. One game genre to another is a UX manifest shift based on engagement domain. Alpha to release is a widening aperture of scenario connectivity. For the full progressive agency model, domain-specific UX expansion tables, and the alpha-to-release deployment gradient, see [PLAYER-VISION.md](PLAYER-VISION.md).

---

## Scale Targets

| Dimension | Target |
|-----------|--------|
| Concurrent AI NPCs | 100,000+ |
| Backend services | 75 (892+ endpoints) |
| State stores | 188+ (118+ Redis + 71+ MySQL) |
| Event types | 432+ |
| Crafting processes | 37+ (authentic simulation) |
| Generational cycle | 80-100 year saeculums with 4 turnings |
| Content flywheel | Year 1: ~1K story seeds, Year 5: ~500K |
| Deployment modes | Monolith to fully distributed, same binary |
| Engine support | Unity, Unreal, Godot, Stride |
| Schema amplification | ~5:1 (184 maintained YAML → 400+ generated files) |

---

## What This Document Is For

When performing high-level audits, cross-cutting concern analysis, or architectural reviews, use this document to answer:

- **"Does this change serve one of the five north stars?"** If not, question whether it belongs.
- **"Does this change respect the system interdependencies?"** The diagrams above show load-bearing connections. Changes that weaken these connections weaken the platform.
- **"Does this change violate a design principle?"** The seven principles above are non-negotiable philosophical commitments.
- **"Does this change help or hinder the content flywheel?"** The flywheel is the ultimate competitive advantage. Everything that makes it spin faster is valuable; everything that slows it down is suspect.
- **"Does this work at the target scale?"** 100,000+ NPCs, 500K+ story seeds, multi-generational persistence. If a design only works for 100 NPCs, it doesn't work.

This is the north star. Don't get lost in the details.
