# Generated Planning Catalog

> **Source**: `docs/planning/*.md`
> **Do not edit manually** - regenerate with `make generate-docs`

Planning, design, research, and architectural analysis documents.

## Vision Documents

### ABML/GOAP Expansion Opportunities {#abml-goap-opportunities}

**Type**: Vision Document | **Status**: Aspirational | **Last Updated**: 2026-03-09 | **North Stars**: #1, #2, #4 | [Full Document](planning/ABML-GOAP-OPPORTUNITIES.md)

Identifies expansion opportunities for the ABML/GOAP system beyond current NPC cognition, combat choreography, and music composition. Covers adaptive tutorials, procedural quest generation, social dynamics, faction economy simulation, a cinematography SDK, and dialogue evolution as future applications of the behavioral intelligence infrastructure. All opportunities are design-only and require new schemas, services, or extensions to existing plugins before realization.

### Compression Gameplay Patterns: Emergent Gameplay from Archived Entities {#compression-gameplay-patterns}

**Type**: Vision Document | **Status**: Aspirational | **Last Updated**: 2026-03-09 | **North Stars**: #2 | [Full Document](planning/COMPRESSION-GAMEPLAY-PATTERNS.md)

Explores gameplay patterns that emerge from treating compressed character archives as generative inputs rather than terminal states. Covers resurrection variants (ghosts, zombies, revenants, clones), procedural quest generation from unfinished business, NPC memory seeding from the dead, legacy mechanics for descendants, live snapshots for AI consumption, and cross-entity compression for scenes, realms, and items. The compression infrastructure in lib-resource and storyline consumption are implemented; the gameplay patterns described here are aspirational designs that would consume that infrastructure.

### Cultural Emergence: Divine Curation of Organic Identity and Custom {#cultural-emergence}

**Type**: Vision Document | **Status**: Aspirational | **Last Updated**: 2026-03-09 | **North Stars**: #1, #5 | [Full Document](planning/CULTURAL-EMERGENCE.md)

Describes how divine actors observe emergent material conditions at settlements (dominant production, geography, governance, trade significance) and crystallize them into cultural identity and customs via Hearsay beliefs and Faction norms, requiring zero new services. Customs such as coming-of-age ceremonies, harvest festivals, mourning rites, and trade protocols emerge organically because the divine GOAP planner selects culturally appropriate practices and artifacts from what is actually available and valued in each community. Several key dependencies remain unimplemented, including the Hearsay, Disposition, Agency, Workshop, Trade, and Environment plugins.

### Player-Driven World Changes: Divine Orchestration Through Narrative Agency {#player-driven-world-changes}

**Type**: Vision Document | **Status**: Aspirational | **Last Updated**: 2026-03-09 | **North Stars**: #1, #2, #5 | [Full Document](planning/PLAYER-DRIVEN-WORLD-CHANGES.md)

Describes the divine orchestration pattern where gods create conditions for permanent world changes and let characters carry them through via the Storyline, Quest, and Contract pipeline, rather than issuing direct decrees. Players and NPCs participate equally in quest chains whose Contract prebound callbacks enact lasting world state changes such as founding traditions, establishing trade routes, and creating organizations. Requires zero new services or code changes, relying entirely on authored ABML behaviors over existing service composition. The pattern is aspirational, pending full implementation of the Divine, Storyline, and Gardener services.

## Design

### Actor-Bound Entities: Living Things That Grow Into Autonomous Agents {#actor-bound-entities}

**Type**: Design | **Status**: Aspirational | **Last Updated**: 2026-03-09 | **North Stars**: #1, #2 | [Full Document](planning/ACTOR-BOUND-ENTITIES.md)

Defines the unified three-stage cognitive progression (Dormant, Stirring, Awakened) for any entity that grows from inert object to autonomous agent. Covers gods, dungeons, living weapons, and future entity types using system realms, Seed growth phases, and the Variable Provider Factory pattern. Validates the composability thesis by demonstrating that living weapons require zero new plugins. No implementation exists yet beyond the foundational services it composes.

This document incorporates and expands `docs/planning/DUNGEON-EXTENSIONS-NOTES.md`. External inspiration: *Tales of Destiny* (Swordians), *Xenoblade Chronicles 2* (Blades), *Persona* (bond = power), *DanMachi* (Falna/blessing system), Dungeon Core LitRPG genre.

### Bannou Embedded Mode: In-Process Service Invocation {#bannou-embedded}

**Type**: Design | **Status**: Aspirational | **Last Updated**: 2026-03-09 | **North Stars**: #4 | [Full Document](planning/BANNOU-EMBEDDED.md)

Describes how Bannou services can run fully in-process without HTTP, WebSocket, or external infrastructure, enabling embedded deployment on Android, desktop, and console. The investigation found that infrastructure libs already support embedded backends, the ASP.NET Core coupling is phantom, and the total implementation is approximately a 2-3 day effort across plugin system decoupling, client generation template changes, and a new embedded host composition root. No implementation has been started.

### Behavior Composition: Fingerprinted Components and the Plan Cache {#behavior-composition}

**Type**: Design | **Status**: Aspirational | **Last Updated**: 2026-03-09 | **North Stars**: #3, #5 | [Full Document](planning/BEHAVIOR-COMPOSITION.md)

Defines the compositional model for fingerprinted, catalogued, reusable ABML behavior components that can be discovered by similarity, strung together via continuation points, and cached as pre-computed GOAP plans to avoid redundant planning across thousands of similar agents. Builds on four existing systems (ABML compiler, continuation points, GOAP planner, Asset service) to connect them into a unified component registry and plan cache targeting 100K+ concurrent agent scale. No implementation exists yet; all described components (IComponentRegistry, PlanCache, CompositeAssembler, PlanFingerprint) are proposed architecture.

### The Cinematic System: From Combat Dream to Choreographic Reality {#cinematic-system}

**Type**: Design | **Status**: Aspirational | **Last Updated**: 2026-03-09 | **North Stars**: #1, #2, #4 | [Full Document](planning/CINEMATIC-SYSTEM.md)

Designs the cinematic composition system, the last remaining structural gap in the Bannou architecture. The runtime for cinematics already exists (CinematicInterpreter, CutsceneCoordinator, IClientCutsceneHandler) but the compositional layer that generates choreographic ABML documents from encounter context is missing. Proposes three new components following the established Theory/Storyteller/Plugin pattern: CinematicTheory SDK for dramatic grammar and spatial reasoning, CinematicStoryteller SDK for GOAP-driven auto-composition, and lib-cinematic (L4) as the thin API wrapper. No implementation exists yet.

### Counterpoint Composer SDK: Structural Template Music Authoring {#counterpoint-composer-sdk}

**Type**: Design | **Status**: Aspirational | **Last Updated**: 2026-03-09 | **North Stars**: #5 | [Full Document](planning/COUNTERPOINT-COMPOSER-SDK.md)

Proposes a MusicComposer SDK providing structural template workbench tooling for human composers to author counterpoint-compatible music. Templates capture uncopyrightable structural parameters (chord progressions, form, energy curves) and the SDK validates harmonic compatibility between pieces at specified temporal offsets, enabling interlocking regional themes, deity leitmotifs, and generational character music in Arcadia. The SDK does not yet exist; MusicTheory and MusicStoryteller SDKs provide the foundation primitives it would build on.

### Cryptic Trails: Distributed Knowledge Puzzles Through Emergent Clue Composition {#cryptic-trails}

**Type**: Design | **Status**: Aspirational | **Last Updated**: 2026-03-09 | **North Stars**: #1, #2, #5 | [Full Document](planning/CRYPTIC-TRAILS.md)

Designs a distributed knowledge puzzle system where NPC characters autonomously discover world secrets by accumulating clues from multiple sources (mementos, item affixes, rumors, documents) and generating hypotheses when their Lexicon discovery tiers cross association visibility thresholds. Composes entirely from existing services (Lexicon, Hearsay, Collection, Seed, Disposition, Actor) with three small extensions: a Hearsay inference channel, skill-gated Lexicon tier offsets, and Discovery Templates as seeded configuration. No prerequisite services are implemented yet; Lexicon, Hearsay, and Disposition are all aspirational.

### Death & Plot Armor System {#death-and-plot-armor}

**Type**: Design | **Status**: Aspirational | **Last Updated**: 2026-03-09 | **North Stars**: #1, #2, #5 | [Full Document](planning/DEATH-AND-PLOT-ARMOR.md)

Designs the death and plot armor system as a behavioral constraint enforced by god-actors through ABML, not by any service or SDK. Plot armor is a continuous float tracked via the Status service that prevents character death while above zero; god-actors deplete it based on danger intensity, god personality, and narrative position. When plot armor reaches zero, deaths become possible and trigger a multi-phase post-death gameplay sequence (Last Stand, divine judgment, underworld entry) that feeds the content flywheel. No new services are required -- the system composes entirely from existing primitives (Status, Actor, Divine, Cinematic, Character-Encounter, Contract).

### Deployment Modes: From Embedded Single-Player to Hyper-Scaled Multi-Node {#deployment-modes}

**Type**: Design | **Status**: Active | **Last Updated**: 2026-03-14 | **North Stars**: #1, #3, #4 | [Full Document](planning/DEPLOYMENT-MODES.md)

Catalogs the four deployment modes that a game built on Bannou can target (embedded single-player, non-dedicated player-hosted, dedicated single-node, hyper-scaled multi-node) and maps each to existing architecture, infrastructure backend selection, and identified gaps. All four modes use the same binary and codebase; the deployment mode is a configuration choice. Three modes work today or require only mechanical implementation; distributed world simulation for the non-dedicated and hyper-scaled modes represents the primary architectural gap requiring new design work in Actor regional affinity, Location partitioning, and node-aware mesh routing.

### Developer Streams: Directed Multi-Feed Streaming via Divine Actor Orchestration {#developer-streams}

**Type**: Design | **Status**: Aspirational | **Last Updated**: 2026-03-10 | **North Stars**: #4, #5 | [Full Document](planning/DEVELOPER-STREAMS.md)

Designs a system where a divine actor (regional watcher pattern) orchestrates multi-feed video streaming from a developer's workspace -- selecting which terminal, IDE, or browser feed to feature based on detected activity events, compositing them into a single directed output stream via Broadcast's RTMP pipeline. Uses 100% of the same Actor runtime, ABML behaviors, Director plugin, Agency feed management, and Broadcast composition infrastructure as in-game cinematic direction. Improvements to directing behaviors, feed management, or composition for either use case (developer streams or in-game cinematics) directly benefit the other. No implementation exists yet.

### Dungeon Extensions Design Notes {#dungeon-extensions-notes}

**Type**: Design | **Status**: Aspirational | **Last Updated**: 2026-03-09 | **North Stars**: #1, #2, #4 | [Full Document](planning/DUNGEON-EXTENSIONS-NOTES.md)

Explores dungeon system extensions beyond the core DUNGEON deep dive, including Workshop integration for habitat creature production, the dual memory system (Collection for permanent knowledge plus Inventory for consumable creative resources), floor-based environmental defense strategies, and the three-stage cognitive progression from dormant seed to awakened character brain. Identifies three areas requiring additional design work: the UNDERWORLD system realm with actor rebinding, the dual memory system replacing the custom memory store, and the Workshop adapter for habitat creature production. No schemas or implementation exist yet.

### Dungeon Mana Absorption: Cost of Entry {#dungeon-mana-absorption}

**Type**: Design | **Status**: Aspirational | **Last Updated**: 2026-03-09 | **North Stars**: #1, #2, #4 | [Full Document](planning/DUNGEON-MANA-ABSORPTION.md)

Designs a mana absorption mechanic where dungeons continuously sap mana from intruders who cross their domain boundary, creating a natural time limit on exploration, an economic cost for dungeon diving, and a non-lethal income stream that fuels the dungeon's own mana economy. The mechanic scales with dungeon cognitive stage (Dormant through Ancient) and composes entirely from existing Bannou primitives (Currency, Status, Seed, Actor, Environment) with no new services required. Inspired by Solo Leveling gate mechanics and Dungeon Core LitRPG, with formal grounding in Arcadia's pneuma thermodynamics.

### Information Economy: Knowledge as Commodity in Living Worlds {#information-economy}

**Type**: Design | **Status**: Aspirational | **Last Updated**: 2026-03-13 | **North Stars**: #1, #2, #5 | [Full Document](planning/INFORMATION-ECONOMY.md)

Describes how information becomes a first-class economic commodity through the composition of existing Bannou primitives, requiring zero new plugins. Reified as physical "itemized contract" items that inject Hearsay beliefs on consumption, information follows the same physical logistics as any tradeable good -- it must be discovered, recorded, transported, and can be stolen, forged, or destroyed. The discovery tier model from Lexicon creates natural expertise differentials that GOAP can price, causing NPC party composition to include non-combat specialists (cartographers, zoologists, archaeologists) when the economic math favors hiring experts over self-development. Markets for information emerge organically when characters discover valuable knowledge and other characters develop drives to acquire it.

### Location-Bound Production: From Farming to Factorio {#location-bound-production}

**Type**: Design | **Status**: Aspirational | **Last Updated**: 2026-03-09 | **North Stars**: #1, #2, #3, #4, #5 | [Full Document](planning/LOCATION-BOUND-PRODUCTION.md)

Describes a general pattern for location-bound production (farming, construction, fermentation, mining, factory automation) that composes entirely from existing service primitives with zero new plugins. Sub-locations hold stage-based inventories, Workshop blueprints drive time-based transformation between stages, and the game clock progresses production even when nobody is watching. The pattern scales from simple farming to full Factorio-style factory automation chains when combined with Transit for transport and a reactive production trigger design addition to Workshop.

### Logos Resonance Items: Memory-Forged Equipment with Experiential Prerequisites {#logos-resonance-items}

**Type**: Design | **Status**: Aspirational | **Last Updated**: 2026-03-09 | **North Stars**: #1, #2, #4 | [Full Document](planning/LOGOS-RESONANCE-ITEMS.md)

Designs a gradient activation system for equipment where affixes have experiential prerequisites evaluated against the wielder at runtime, replacing traditional binary soulbinding with a fidelity model. Items are created by god-actors during formative events (boss kills, climactic battles) and carry activation prerequisites tied to the experiences that forged them, so the earner naturally meets most prerequisites while other wielders achieve partial activation. Requires extending the Affix system with an IActivationPrerequisiteProviderFactory DI pattern (following the established Quest prerequisite model) and ABML behavior authoring for god-actors. Both Affix and Loot plugins remain aspirational with no schemas or implementations yet.

### Memento Inventories: Location-Based Spiritual Ecology {#memento-inventories}

**Type**: Design | **Status**: Aspirational | **Last Updated**: 2026-03-09 | **North Stars**: #1, #2, #3, #5 | [Full Document](planning/MEMENTO-INVENTORIES.md)

Generalizes the dungeon core memory inventory pattern to all locations in the game world, where each location accumulates memento items generated from real gameplay events such as deaths, battles, emotional moments, and masterwork creations. Characters with spiritual perception abilities (necromancers, mediums, historians, detectives, bards, craftsmen) interact with these mementos to summon spirit echoes, extract forensic evidence, compose performances, or imbue crafted items with historical significance. Requires zero new services or plugins, composing entirely from Item, Inventory, Location, Actor, and ABML behavior documents. No implementation exists yet; this is the design specification for the spiritual ecology layer of the content flywheel.

### Plugin Lifecycle Pipeline: From Idea to Production-Ready {#plugin-lifecycle-pipeline}

**Type**: Design | **Status**: Implemented | **Last Updated**: 2026-03-09 | **North Stars**: #4 | [Full Document](planning/PLUGIN-LIFECYCLE-PIPELINE.md)

Formalizes the end-to-end development lifecycle for Bannou plugins across seven stages, from deep dive concept through production-ready implementation. Defines readiness levels (L0-L7), the skill commands that drive progression, and ordering constraints that prevent architectural rework. All seven stages now have corresponding skill commands or established manual processes.

### Sanctuaries and Spirit Dens: Sacred Geography of the Living World {#sanctuaries-and-spirit-dens}

**Type**: Design | **Status**: Aspirational | **Last Updated**: 2026-03-09 | **North Stars**: #1, #2, #5 | [Full Document](planning/SANCTUARIES-AND-SPIRIT-DENS.md)

Designs two complementary sacred geography systems composed from existing Bannou primitives: Sanctuaries (divine non-aggression zones where supernatural peace overrides predator ecology via GOAP action cost modification) and Spirit Dens (leyline convergence points where accumulated species-logos crystallizes into supernatural exemplar animals following the Actor-Bound Entity pattern). Both use Workshop lazy evaluation for scalable spiritual production-vs-decay dynamics, enabling hundreds of sacred sites with zero server ticks. No implementation exists; key dependencies (Environment, Ethology, Workshop, Agency) remain unimplemented.

### Self-Hosted Deployment: Single-Player and Local Server Experiences {#self-hosted-deployment}

**Type**: Design | **Status**: Active | **Last Updated**: 2026-03-09 | **North Stars**: #1, #4, #5 | [Full Document](planning/SELF-HOSTED-DEPLOYMENT.md)

Designs how Bannou ships as a local dedicated server alongside a game client for single-player or LAN multiplayer experiences. The existing architecture (plugin loading, in-memory infrastructure backends, environment-driven service selection, lazy evaluation) already supports this deployment mode with zero code changes. The SQLite state store backend has been implemented, removing the primary infrastructure gap. Remaining investments are SDK convenience layers, in-process mesh routing for embedded .NET engines, and documentation.

### Video Director: Dynamic Cinematic Generation from Game Data {#video-director}

**Type**: Design | **Status**: Aspirational | **Last Updated**: 2026-03-09 | **North Stars**: #5 | [Full Document](planning/VIDEO-DIRECTOR.md)

Designs a composition layer that maps musical structure onto Bannou's existing narrative, choreographic, and cinematic infrastructure to generate real-time entertainment cinematics (music videos, adventure trailers, promotional content) from game world data. The system takes a musical template, thematic intent, character selection criteria, and scene preferences, then orchestrates Storyline, CinematicStoryteller, MusicStoryteller, and the Actor runtime to produce deterministic, seed-reproducible cinematics rendered in the game engine. Not to be confused with the Director plugin (L4) which provides human-in-the-loop orchestration, though the two concepts may converge. No implementation exists yet.

## Research

### Situationally Triggered Cinematics: Precedent Research {#cinematic-precedent-research}

**Type**: Research | **Status**: Aspirational | **Last Updated**: 2026-03-09 | **North Stars**: #1, #3 | [Full Document](planning/CINEMATIC-PRECEDENT-RESEARCH.md)

Compiles precedent research across combat cinematic triggers, interactive cinematic (QTE) systems, procedural cinematic theory, and formal academic frameworks to inform the design of the CinematicStoryteller SDK. Identifies four areas of architectural novelty in the proposed system (GOAP for choreographic composition, continuation points with timeout defaults, progressive agency as continuous variable, and the repeatable Theory/Storyteller/Plugin pattern) while validating key design decisions against established industry patterns. No implementation exists; the cinematic system described in CINEMATIC-SYSTEM.md remains aspirational.

### Cinematic Research Analysis: What We Actually Have and What To Build With It {#cinematic-research-analysis}

**Type**: Research | **Status**: Aspirational | **Last Updated**: 2026-03-09 | **North Stars**: #1, #5 | [Full Document](planning/CINEMATIC-RESEARCH-ANALYSIS.md)

Analyzes nine detailed academic research cards covering cinematic theory, camera systems, and dramatic composition to determine which sources map to which SDK layer for the planned CinematicTheory and CinematicStoryteller SDKs. Establishes key architectural decisions including HFSM for camera direction versus GOAP for choreography, three independent composable layers (Structure, Quality, Presentation), and Facade-style greedy sequencing for interactive combat contexts. No implementation exists yet as neither SDK nor the lib-cinematic plugin have been created.

### Cinematic Theory Research: Formal Foundations for CinematicTheory SDK {#cinematic-theory-research}

**Type**: Research | **Status**: Aspirational | **Last Updated**: 2026-03-09 | **North Stars**: #1, #2 | [Full Document](planning/CINEMATIC-THEORY-RESEARCH.md)

Compiles formal academic research across three domains -- computational cinematography, fight choreography, and dramatic grammar -- that provide the theoretical foundations for a planned CinematicTheory SDK. Maps specific models (Laban movement analysis, Cohn visual narrative grammar, toric camera space, SAFD stage combat patterns) to a five-layer architecture for procedural combat choreography and camera direction. No implementation exists yet; this is a research compilation informing future SDK design.

### Predator Ecology Patterns: Behavioral Modeling for Living Worlds {#predator-ecology-patterns}

**Type**: Research | **Status**: Aspirational | **Last Updated**: 2026-03-09 | **North Stars**: #1, #5 | [Full Document](planning/PREDATOR-ECOLOGY-PATTERNS.md)

Compiles established wildlife ecology research on predator coexistence, niche partitioning, intraguild predation, mesopredator release, and territorial behavior into a specification for the Ethology service behavioral archetype system. Defines 30+ behavioral float axes across six domains (hunting, temporal, spatial, competition, metabolic, sensory) with concrete species profile examples and seven interaction rules that produce emergent predator ecosystems from parameter interactions. No implementation exists yet; the related plugins (Ethology, Environment, Disposition) remain aspirational with no schemas or generated code.

## Implementation Plans

### Batch Lifecycle Events: Normalized High-Frequency Event Publishing {#batch-lifecycle-events}

**Type**: Implementation Plan | **Status**: Active | **Last Updated**: 2026-03-13 | **North Stars**: #1 | [Full Document](planning/BATCH-LIFECYCLE-EVENTS.md)

Normalizes high-frequency event publishing across Bannou by extending x-lifecycle with a batch: true option that generates only batch event types, adding a shared EventBatcher helper to bannou-service, creating shared batch endpoint request/response models in common-api.yaml, and adding structural tests to enforce consistency. A structural analysis of x-references declarations revealed that nearly all 16 services storing per-character dependent data become high-frequency event publishers at 100K NPC scale, with their lifecycle events serving purely informational/analytics purposes (cleanup handled by lib-resource or DI Listeners, not event subscription). This establishes x-references targeting character-scale entities as a structural heuristic for batch: true candidacy, applicable to 15 of 16 x-references services.

### Git Registry Plugin - Self-Hosted Git Server for Bannou {#git-registry-plugin}

**Type**: Implementation Plan | **Status**: Aspirational | **Last Updated**: 2026-03-09 | **North Stars**: N/A | [Full Document](planning/GIT-REGISTRY-PLUGIN.md)

Proposes a self-hosted Git server as a Bannou plugin (lib-git) using git.exe process execution for protocol handling, with a POST-only repository management API and WebSocket-based real-time synchronization via the Connect service. The estimated effort is 6-8 weeks covering core protocol, management API, real-time sync, and testing. No implementation exists yet; neither schemas nor plugin code have been created.

## Architectural Analysis

### Behavioral Gaps: What the Behavior System Must Become {#behavioral-gaps}

**Type**: Architectural Analysis | **Status**: Active | **Last Updated**: 2026-03-10 | **North Stars**: #1, #2, #3, #5 | [Full Document](planning/BEHAVIORAL-GAPS.md)

Catalogs every identified gap between the behavior system's current implementation (ABML compiler, GOAP planner, Actor runtime, Puppetmaster) and the requirements revealed by planning documents (VIDEO-DIRECTOR, COMPOSITIONAL-CINEMATICS, DEVELOPER-STREAMS, BEHAVIOR-COMPOSITION). The core infrastructure is solid; the missing layers are behavior composition (component registry, plan cache, composite assembly) and domain-specific SDKs and content (cinematic, economic, directing, god-actor behaviors). Gaps are organized into 12 categories with priority tiers and GH issue cross-references.

### Compositional Cinematics: The Anime Production Paradigm for Real-Time 3D {#compositional-cinematics}

**Type**: Architectural Analysis | **Status**: Aspirational | **Last Updated**: 2026-03-09 | **North Stars**: #1, #2, #5 | [Full Document](planning/COMPOSITIONAL-CINEMATICS.md)

Analyzes how the anime production paradigm of decomposing complex scenes into independently-produced layers with shared spatial constraints maps onto Bannou's cinematic architecture. Fingerprinted behavior components serve as character cels, continuation points as composition seams, and CutsceneSession sync barriers as same-layer synchronization moments. Extends the paradigm to distributed scene sourcing where multiple servers simultaneously simulate different scene versions for instantaneous flashbacks, perspective splits, and temporal montage. No new systems are proposed; this is a unifying architectural lens on planned and existing systems including lib-cinematic, lib-behavior, lib-actor, and the Video Director.

### Vision Progress: Cross-Service Architectural Audit {#vision-progress}

**Type**: Architectural Analysis | **Status**: Active | **Last Updated**: 2026-03-09 | **North Stars**: #1, #2, #5 | [Full Document](planning/VISION-PROGRESS.md)

Records the results of a full cross-service architectural audit comparing VISION.md, PLAYER-VISION.md, and BEHAVIORAL-BOOTSTRAP.md against the 76-service plugin architecture. Categorizes findings as resolved (11 issues with architectural rationale), open (8 issues with priority and impact), or design recommendations requiring human judgment. The behavioral bootstrap critical path analysis identifies Puppetmaster watcher-actor spawning as the first-order blocker and Divine implementation as the second-order blocker for the content flywheel.

## Summary

- **Documents in catalog**: 32

---

*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for architectural context.*
