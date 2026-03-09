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

## Other

### Death & Plot Armor System {#death-and-plot-armor}

**Status**: Design | [Full Document](planning/DEATH-AND-PLOT-ARMOR.md)

Plot armor is a **hard numerical value** tracked per character that determines whether that character can die as a result of scenario outcomes, cinematic exchanges, or combat encounters. While the value is above zero, the character **cannot be killed** -- dangerous outcomes are deflected by divine intervention, luck, autonomous reflexes, or other narrative mechanisms. When the value reaches zero, cinematic deaths become possible.

### Dungeon Mana Absorption: Cost of Entry {#dungeon-mana-absorption}

**Status**: Design | [Full Document](planning/DUNGEON-MANA-ABSORPTION.md)

Dungeons continuously sap mana from intruders who cross their domain boundary. An initial burst absorbs a significant portion of the entrant's mana reserves on crossing the threshold (~20%), followed by a slower continuous drain (~1% per game-minute). This creates a natural time limit on dungeon exploration (~80 minutes before depletion), makes dungeon-diving an economically costly activity, and -- critically -- **fuels the dungeon's own mana economy from attempts, not just from deaths**.

### Git Registry Plugin - Self-Hosted Git Server for Bannou {#git-registry-plugin}

**Status**: Ready for Implementation | **Last Updated**: 2025-12-27 | [Full Document](planning/GIT-REGISTRY-PLUGIN.md)

Implement a self-hosted Git server as a Bannou plugin using `git.exe` process execution for protocol handling, with a comprehensive repository management API and WebSocket-based real-time synchronization.

### Location-Bound Production: From Farming to Factorio {#location-bound-production}

**Status**: Design | [Full Document](planning/LOCATION-BOUND-PRODUCTION.md)

A location is a container. A container holds items. Items transform over time. Workshop already models time-based transformation with lazy evaluation against Worldstate's game clock, source/destination inventories, worker scaling, and environment-modified rates. Location already supports hierarchical sub-locations. Inventory already supports containers associated with any entity.

### Logos Resonance Items: Memory-Forged Equipment with Experiential Prerequisites {#logos-resonance-items}

**Status**: Design | [Full Document](planning/LOGOS-RESONANCE-ITEMS.md)

Traditional soulbinding is a binary lock: the item works for you, or it doesn't. Logos resonance items replace this with a **gradient of experiential affinity** -- the item is freely tradeable, always functional, but its full capabilities emerge only when the wielder's accumulated experiences resonate with the memories crystallized within it.

### Memento Inventories: Location-Based Spiritual Ecology {#memento-inventories}

**Status**: Design | [Full Document](planning/MEMENTO-INVENTORIES.md)

Every death leaves a mark. Every battle scars the earth. Every act of love, betrayal, creation, or destruction imprints itself on the place where it happened. Memento inventories generalize the dungeon's memory inventory pattern -- already designed as a dual Collection/Inventory system for dungeon cores -- to **all locations in the game world**. Each location accumulates memento items generated from real gameplay events: deaths, battles, emotional moments, masterwork creations. These mementos contain compressed real data from real characters and real events, not authored content.

### Player-Driven World Changes: Divine Orchestration Through Narrative Agency {#player-driven-world-changes}

**Status**: Vision Document (design analysis, no implementation) | [Full Document](planning/PLAYER-DRIVEN-WORLD-CHANGES.md)

Divine actors are not diminished by orchestrating through narrative rather than decreeing directly. They are elevated -- from administrators to storytellers. The god that commissions a Storyline scenario and watches characters struggle, compete, fail, and ultimately succeed in transforming their world is doing something more interesting than the god that simply inserts a database row.

Players are not diminished by operating within divinely-orchestrated scenarios. They are elevated -- from quest consumers to world shapers. The player who helps Lily establish the Flower Tide Festival has permanently changed the game world, and the world will remember that they did.

The services do not know the difference. Contract fires callbacks. Faction creates norms. Hearsay propagates beliefs. Character History records events. The content flywheel spins. Whether the catalyst was a divine decree, an NPC's ambition, or a player's choice -- the world changes, and the change generates more world.

No new plugins. No code changes. Just a different question in the divine actor's ABML behavior: not "should this change happen?" but "who should carry this change into being?"

### Plugin Lifecycle Pipeline: From Idea to Production-Ready {#plugin-lifecycle-pipeline}

[Full Document](planning/PLUGIN-LIFECYCLE-PIPELINE.md)

Bannou's existing documentation and skill infrastructure already covers most of the plugin development lifecycle, but the stages aren't formalized into a pipeline with clear readiness gates. This document defines that pipeline.

### Predator Ecology Patterns: Behavioral Modeling for Living Worlds {#predator-ecology-patterns}

**Status**: Research Document (ecological analysis for behavioral modeling) | [Full Document](planning/PREDATOR-ECOLOGY-PATTERNS.md)

Real-world predator ecosystems are governed by discoverable rules that produce emergent complexity from simple parameters. Multiple carnivorous predator species **routinely coexist** in the same geographic area -- this is the norm in healthy ecosystems, not the exception. The African savanna supports 7+ large predator species; North American temperate forests support 6+. Coexistence is not about tolerance; it is about **niche partitioning along multiple axes simultaneously** (temporal, spatial, prey size, hunting style). These rules translate directly into Ethology service behavioral axes and ABML behavior expressions, producing realistic predator ecosystems from parameter interactions rather than scripted encounters.

### Sanctuaries and Spirit Dens: Sacred Geography of the Living World {#sanctuaries-and-spirit-dens}

**Status**: Vision Document (design analysis, no implementation) | [Full Document](planning/SANCTUARIES-AND-SPIRIT-DENS.md)

Two complementary concepts create sacred geography in the Arcadia game world: **Sanctuaries** (divine non-aggression zones where supernatural peace overrides predator ecology) and **Spirit Dens** (leyline convergence points where accumulated species-logos crystallizes into supernatural exemplar animals surrounded by their kin). Both emerge from the interaction of existing systems -- the predator ecology rules, the memento inventory system, the divine economy, and the leyline/pneuma framework -- requiring zero new services.

### Self-Hosted Deployment: Single-Player and Local Server Experiences {#self-hosted-deployment}

**Status**: Design | [Full Document](planning/SELF-HOSTED-DEPLOYMENT.md)

Bannou's architecture -- schema-first code generation, plugin loading, in-memory infrastructure backends, environment-driven service selection, and lazy evaluation -- already supports self-hosted deployment with zero code changes. A game built on Bannou can ship as a local dedicated server alongside the game client (the Satisfactory model), giving players a single-player or LAN multiplayer experience powered by the full service stack.

### Video Director: Dynamic Cinematic Generation from Game Data {#video-director}

**Status**: Aspirational planning | [Full Document](planning/VIDEO-DIRECTOR.md)

Video Director is not a new system -- it is a **composition layer** that maps musical structure onto the narrative, choreographic, and cinematic infrastructure Bannou already provides. The core insight is that every component of a music video (narrative arc, character selection, action choreography, camera direction, temporal montage, beat synchronization) already has a Bannou system designed to handle it. What's missing is the conductor that reads a musical score and tells each system when to play.

The convergence with the Director plugin is particularly compelling: the same event coordination infrastructure that lets developers orchestrate live game events can orchestrate music videos, with the music providing the timing that a human director would otherwise provide manually. The result is a system where a developer can trigger a dynamic music video in a live, streaming game world -- characters performing choreographed action timed to music while the world continues around them -- and every run produces a unique cinematic from the same inputs because the characters, their histories, and their personalities are all real, simulated data.

This is the Content Flywheel applied to entertainment content: the richer the game world's history, the richer the cinematics it can produce.

### Vision Progress: Cross-Service Architectural Audit {#vision-progress}

**Last Updated**: 2026-03-02 | [Full Document](planning/VISION-PROGRESS.md)

This document records the results of a full cross-service audit comparing the vision documents against the generated service details and deep dive documentation. Issues are categorized as resolved (with architectural rationale), open (with priority and impact), or questions (requiring human judgment).

## Summary

- **Documents in catalog**: 25

---

*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for architectural context.*
