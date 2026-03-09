# Generated Planning Catalog

> **Source**: `docs/planning/*.md`
> **Do not edit manually** - regenerate with `make generate-docs`

Planning, design, research, and architectural analysis documents.

## Research

### Situationally Triggered Cinematics: Precedent Research {#cinematic-precedent-research}

**Type**: Research reference document | [Full Document](planning/CINEMATIC-PRECEDENT-RESEARCH.md)

This document captures research across three domains -- combat cinematic triggers, interactive cinematic (QTE) systems, and procedural cinematic theory -- to establish precedent for the compositional layer described in [CINEMATIC-SYSTEM.md](CINEMATIC-SYSTEM.md). The goal is to identify what terminology, design patterns, and formal frameworks already exist for systems that take gameplay state and produce choreographed visual sequences with defined entry/exit points and continuation logic.

### Cinematic Theory Research: Formal Foundations for CinematicTheory SDK {#cinematic-theory-research}

**Type**: Research compilation | [Full Document](planning/CINEMATIC-THEORY-RESEARCH.md)

Three domains of formal theory converge on what a CinematicTheory SDK needs:

## Architectural Analysis

### Behavior Composition: Fingerprinted Components and the Plan Cache {#behavior-composition}

**Type**: Architectural planning document | [Full Document](planning/BEHAVIOR-COMPOSITION.md)

Bannou's behavior system already has the infrastructure for composable behaviors: continuation points create named seams in bytecode, streaming composition injects extensions at those seams, the Asset service stores compiled behaviors for cross-node/client distribution, and content hashing (SHA256) uniquely identifies every compiled behavior. What's missing is the **compositional model** -- the formalized system that treats behaviors as fingerprinted, catalogued, reusable components that can be discovered by similarity, strung together via continuation points, and cached as pre-computed plans to avoid redundant GOAP planning across thousands of similar agents.

### The Cinematic System: From Combat Dream to Choreographic Reality {#cinematic-system}

**Type**: Architectural planning document | [Full Document](planning/CINEMATIC-SYSTEM.md)

The Bannou architecture has exactly one remaining structural gap: **cinematic composition**. Every other system -- from the lowest infrastructure primitive to the highest orchestration layer -- exists as either an implemented plugin, a fully specified design, or both. The cinematic system is the only place where the architecture says "and then this happens" without having defined *how* it happens.

### Counterpoint Composer SDK: Structural Template Music Authoring {#counterpoint-composer-sdk}

**Type**: Architectural planning document | [Full Document](planning/COUNTERPOINT-COMPOSER-SDK.md)

The Bannou music system generates adaptive, procedural music at runtime via MusicTheory (formal harmony/melody) and MusicStoryteller (narrative-driven emotional arcs). This works beautifully for dynamic, situational soundtrack generation. But **composed music** -- regional themes, deity leitmotifs, character themes, cinematic set pieces -- requires human authorship.

## Other

### ABML/GOAP Expansion Opportunities {#abml-goap-opportunities}

**Last Updated**: 2026-02-11 | [Full Document](planning/ABML-GOAP-OPPORTUNITIES.md)

| # | Opportunity | What It Needs From Bannou | Status |
|---|-------------|--------------------------|--------|
| 1 | [Adaptive Tutorial/Onboarding](#1-adaptive-tutorial--onboarding-system) | New SDK or service; player state observation pipeline | Design only |
| 2 | [Procedural Quest Generation](#2-procedural-quest-generation) | Quest template system; GOAP integration in Quest/Storyline | Design only |
| 3 | [Social Dynamics Engine](#3-social-dynamics-engine) | ABML behavior patterns; possible Relationship schema extensions | Design only |
| 4 | [Faction/Economy Simulation](#4-faction--economy-simulation) | Faction service or realm-level actor patterns; Currency/Relationship extensions | Design only |
| 5 | [Cinematography SDK](#5-cinematography-sdk) | New SDK wrapping existing cutscene infrastructure | Design only |
| 6 | [Dialogue Evolution System](#6-dialogue-evolution-system) | GOAP integration with ABML dialogue document type | Design only |
| 7+ | [Additional Ideas](#additional-opportunities) | Varies | Sketches only |

### Actor-Bound Entities: Living Things That Grow Into Autonomous Agents {#actor-bound-entities}

**Status**: Vision Document (architectural analysis, no implementation) | [Full Document](planning/ACTOR-BOUND-ENTITIES.md)

Bannou's actor system, seed growth, and dynamic character binding combine to enable a powerful pattern: **entities that begin as inert objects and progressively grow into autonomous agents with personalities, memories, and the full cognitive stack**. This document unifies two implementations of this pattern -- **dungeon cores** and **living weapons** -- and demonstrates that they are structurally identical at the infrastructure level, differing only in domain-specific ceremony.

### Bannou Embedded Mode: In-Process Service Invocation {#bannou-embedded}

**Status**: Investigation complete, implementation not started | [Full Document](planning/BANNOU-EMBEDDED.md)

Bannou's current inter-service invocation path is:

### Cinematic Research Analysis: What We Actually Have and What To Build With It {#cinematic-research-analysis}

**Type**: Actionability analysis | [Full Document](planning/CINEMATIC-RESEARCH-ANALYSIS.md)

We have 9 detailed research cards representing deep reads of the sources the research document identified. Here's the honest assessment of what each one delivered versus what was expected.

### Compression Gameplay Patterns: Emergent Gameplay from Archived Entities {#compression-gameplay-patterns}

**Status**: Vision Document (foundation implemented, gameplay patterns pending) | [Full Document](planning/COMPRESSION-GAMEPLAY-PATTERNS.md)

When a character dies and is compressed, their entire life story -- personality, memories, relationships, history, encounters -- crystallizes into a rich archive. This isn't just data cleanup; it's **generative input for emergence**.

### Cryptic Trails: Distributed Knowledge Puzzles Through Emergent Clue Composition {#cryptic-trails}

**Status**: Design | [Full Document](planning/CRYPTIC-TRAILS.md)

A dungeon spawns beneath an ancient city. The entrance is hidden. No quest marker points to it. No NPC says "go here." Instead, a character examining an old sword notices an affix description referencing "depths that predate the city above." A death memento at a tavern records someone who "fell into a hidden passage." A rumor circulates about scratching sounds beneath the inn. A historical text in a library describes darkspawn tunnel construction techniques. Each clue alone is a curiosity. Together, they form a hypothesis: **the old quarter sits atop darkspawn tunnels.**

### Cultural Emergence: Divine Curation of Organic Identity and Custom {#cultural-emergence}

**Status**: Vision Document (design analysis, no implementation) | [Full Document](planning/CULTURAL-EMERGENCE.md)

Culture in Bannou is not designed. It is **observed, named, and formalized** by divine actors who possess the cross-domain perspective to recognize what a settlement has already become. The customs that emerge match their context because they are derived from it. The status board is not a feature -- it is one possible cultural artifact that appears when material conditions, governance structures, and divine aesthetics align to produce a society that values formalized self-knowledge.

No new services. No new plugins. Just divine actors doing what they do: watching the world, understanding what it has become, and giving it the cultural vocabulary to know itself.

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
