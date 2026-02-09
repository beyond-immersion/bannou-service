# Planning: Seed Service & Gardener Service

> **Status**: Draft
> **Created**: 2026-02-09
> **Related**: [PLAYER-VISION.md](../reference/PLAYER-VISION.md), [VISION.md](../reference/VISION.md), [SERVICE-HIERARCHY.md](../reference/SERVICE-HIERARCHY.md)

This document outlines the design and schemas for two new services: **Seed** (a generic progressive growth primitive at L2) and **Gardener** (the player experience orchestrator at L4). Seed implements the foundational "growth in roles" abstraction used by the player agency model (PLAYER-VISION.md), the dungeon system (DUNGEON-AS-ACTOR.md), and future systems. Gardener is the first consumer of Seed, implementing the void/scenario system for players.

---

## Table of Contents

1. [Overview](#overview)
   - [Design Philosophy](#design-philosophy)
   - [Dependency Graph](#dependency-graph)
   - [Architectural Decision: Why Gardener Stays at L4](#architectural-decision-why-gardener-stays-at-l4)
   - [Seed Consumers (Current and Future)](#seed-consumers-current-and-future)
2. [Seed Service (L2 Game Foundation)](#seed-service)
3. [Gardener Service (L4 Game Features)](#gardener-service)
4. [Integration Points](#integration-points)
5. [Extensions to Existing Services](#extensions-to-existing-services)
6. [State Stores](#state-stores)
7. [Schema Drafts](#schema-drafts)
8. [Open Design Questions](#open-design-questions)

---

## Overview

Two new services implement the player experience vision:

| Service | Layer | Role | Analogous To |
|---------|-------|------|-------------|
| **Seed** | L2 Game Foundation | Generic progressive growth entities with polymorphic ownership, domain-based experience accumulation, capability computation, and seed-to-seed bonds | Item (generic data container) |
| **Gardener** | L4 Game Features | Player experience orchestration -- void navigation, scenario routing, progressive discovery, deployment phase management. First consumer of lib-seed. | Puppetmaster (dynamic orchestration) |

### Design Philosophy

**Seeds track growth in roles, not growth in entities.** This is the core insight. A seed doesn't represent an entity -- it represents an entity's progressive mastery of a specific role. The same character can hold a `guardian` seed (growth as a player-controlled spirit), a `dungeon_master` seed (growth in the dungeon mastery role), and a `warrior` seed (growth in the combat archetype). Each tracks independent growth with its own domains, phases, and capabilities. The entity is the owner; the seed is the role.

**lib-seed is the generic growth primitive.** A seed is an entity that starts empty and grows by accumulating metadata from external events, progressively gaining capabilities. Seeds are owned by any entity type (accounts, actors, realms, characters, relationships) and are agnostic to what they represent. The Arcadia guardian spirit is one use case; dungeon cores, combat archetypes, crafting specializations, governance roles, faction identities, genetic lineages, and apprenticeship knowledge are all equally valid. Everything that makes this work -- SeedType string codes, arbitrary dot-separated growth domains, configurable capability rules, per-type phase labels -- is generic by design.

**lib-gardener is the player experience orchestrator.** It is the first and primary consumer of lib-seed, implementing the void/scenario system described in PLAYER-VISION.md. Other systems that grow seeds (dungeons, factions, etc.) use lib-seed directly with their own orchestration logic. Gardener's responsibilities (void navigation, scenario routing, POI spawning, deployment phase management) are entirely player-experience-specific and do not generalize across consumers.

**Design principle**: Puppetmaster orchestrates what NPCs experience. Gardener orchestrates what players experience. Seed is the foundational growth primitive that both (and future systems) reference. Each consumer provides its own domain-specific orchestration on top of the shared Seed foundation.

### Dependency Graph

```
                     Seed (L2)
                   /     |      \
                  /      |       \
          Character  Game Session  Relationship
          (L2)       (L2)          (L2)
                         |
                         |
                   Gardener (L4)
                   /   |   |   \
                  /    |   |    \
            Seed  Analytics  Puppetmaster  Behavior
            (L2)  (L4)       (L4)          (L4)
                    \         |           /
                     \        |          /
                      Asset  Game Session  Matchmaking
                      (L3)   (L2)          (L4)
```

### Architectural Decision: Why Gardener Stays at L4

Given the number of seed consumers (guardian spirits, dungeon cores, dungeon masters, future combat archetypes, crafting specializations, governance roles, factions, apprenticeships, lineages), we evaluated whether shared orchestration logic should be extracted from Gardener and placed at L2 alongside Seed. The decision: **no**.

**What every seed consumer does** (already handled by lib-seed at L2):
1. Register their seed type (`seed/type/register`)
2. Create seeds when entities enter roles (`seed/create`)
3. Publish growth events (`seed.growth.contributed`)
4. Query capabilities to gate actions (`seed/capability/get-manifest`)

**What Gardener does beyond seed management** (entirely player-experience-specific):
- Void navigation, position tracking, drift metrics
- POI spawning/despawning with scoring algorithms
- Scenario template management, routing, chaining
- Deployment phase management (Alpha/Beta/Release)
- Bond-specific shared void instances

None of these concepts apply to dungeons, factions, archetypes, or apprenticeships. Each consumer's orchestration is domain-specific:

| Consumer | Orchestration | Why It Doesn't Generalize |
|----------|--------------|--------------------------|
| **Gardener** | Void/POI/scenario system | Purely player UX; no other system has a "void" |
| **Dungeon plugin** | ABML cognition + actor state | Dungeon-specific; runs inside Actor runtime |
| **Combat archetypes** | Class ability gating per character | Character-level; integrated with combat system |
| **Governance** | Political simulation per realm | Realm-level; integrated with territorial systems |

**The right analogy is Item (L2) with multiple independent consumers.** Item is a generic data primitive. Inventory (L2), Escrow (L4), and Save-Load (L4) all consume it, each with their own orchestration logic. Nobody proposes merging Escrow's multi-party exchange orchestration into Item because it's domain-specific. Similarly, Gardener's void/scenario orchestration is domain-specific to the player experience and stays at L4.

**Three models considered, one chosen:**
1. ~~Relationship/RelationshipType model~~ (multiple API sets on one plugin): Doesn't apply -- Seed and Gardener have different layers (L2 vs L4) and completely different responsibilities.
2. ~~Item/Inventory model~~ (two plugins, same level): Doesn't apply -- Gardener is not foundational. Not every game deployment needs void/scenario/POI systems.
3. **Seed as standalone L2 foundation, Gardener as one of N consumers at L4**: Correct model. Shared primitive at L2, domain-specific orchestration at whatever layer the consumer lives in.

### Seed Consumers (Current and Future)

lib-seed is designed to be reused by any system that needs progressive growth tracking. Because seeds track growth in roles (not entities), a single entity can hold multiple seeds for different roles it plays. The following table shows known and anticipated consumers:

| Consumer | Seed Type Code | Seed Owner | Growth Domains | Capability Output |
|----------|---------------|-----------|----------------|-------------------|
| **Gardener** (player spirits) | `guardian` | Account | combat.*, crafting.*, social.*, trade.*, magic.*, exploration.* | UX capability modules for client |
| **Dungeon plugin** (dungeon consciousness) | `dungeon_core` | Actor | mana_reserves.*, genetic_library.*, trap_complexity.*, domain_expansion.*, memory_depth.* | Dungeon spawning/trap/manifestation capabilities |
| **Dungeon plugin** (mastery role) | `dungeon_master` | Character or Actor | perception.*, command.*, channeling.*, coordination.* | Bond communication/command capabilities |
| **Combat archetypes** (future) | `warrior`, `mage`, `ranger` | Character | archetype-specific combat domains | Class-specific combat abilities and UX |
| **Crafting specializations** (future) | `smith`, `alchemist`, `enchanter` | Character | trade-specific technique/material domains | Recipe unlocks, technique mastery |
| **Governance roles** (future) | `governor`, `guild_leader` | Character or Realm | diplomacy.*, logistics.*, taxation.*, military_command.* | Political actions and policy capabilities |
| **Faction system** (future) | `faction` | Realm or character group | military.*, trade.*, culture.* | Faction actions and policies |
| **Apprenticeship** (future) | `apprenticeship` | Relationship | technique.*, lore.*, material_science.* | Craftable items, teachable skills |
| **Genetic lineage** (future) | `lineage` | Character household | trait.strength, trait.magical_affinity... | Inheritable character traits |

Each consumer registers its own seed types, defines its own growth domains and capability rules, and provides its own orchestration logic. lib-seed provides the shared primitive: growth entity, domain accumulation, capability computation, bonding. No changes to lib-seed are required to add new consumers -- only new seed type registrations.

**Key validation**: The dungeon plugin demonstrates the design's generality by requiring two independent seed types (`dungeon_core` + `dungeon_master`) for a single system. The two seeds grow independently in parallel, connected by a Contract rather than a seed bond. This proves that seeds cleanly model asymmetric role growth within a partnership.

---

## Seed Service

### Identity

- **Service name**: `seed`
- **Layer**: L2 Game Foundation
- **Lifetime**: Scoped
- **Plugin**: `plugins/lib-seed/`

### Why L2

Seeds are foundational growth entities that track progressive mastery of roles. In Arcadia, every player has seeds (guardian spirits). Dungeon cores are seeds. Dungeon masters are seeds. Combat archetypes, crafting specializations, governance roles, factions -- all are seeds. The seed is as fundamental to the game model as Character or Item -- it's a generic primitive that higher-layer services build on. Like Item, it doesn't know or care what it represents; it only knows that something grows across named domains and unlocks capabilities at configurable thresholds.

### Core Concepts

#### The Seed Entity

A seed is owned by any entity (polymorphic via OwnerId + OwnerType). Created by any service that needs a progressive growth entity. Multiple seeds can exist per owner (configurable maximum per seed type).

```
Seed
├── SeedId (Guid)
├── OwnerId (Guid) -- polymorphic owner (account, actor, realm, character, etc.)
├── OwnerType (string) -- entity type discriminator (e.g., "account", "actor", "realm")
├── SeedType (string) -- configurable type code (e.g., "guardian", "dungeon_core", "faction")
├── GameServiceId (Guid) -- scoped to a game service
├── CreatedAt (DateTime)
├── GrowthPhase (string) -- computed phase label (configurable per seed type)
├── TotalGrowth (float) -- aggregate across all domains
├── GrowthDomains (map<string, float>) -- domain path → depth
├── BondId (Guid?) -- null if unbonded
├── DisplayName (string)
├── Status (SeedStatus enum: Active, Dormant, Archived)
└── Metadata (map<string, object>) -- seed-type-specific data
```

#### Seed Types

Seed types are string codes, not a fixed enum. This allows new seed types to be introduced without schema changes. Each seed type has a registered configuration that defines:

- Maximum seeds of this type per owner
- Growth phase definitions (labels and thresholds)
- Capability computation rules
- Bond cardinality (0 = no bonding, 1 = pair, N = group)

```
SeedTypeDefinition
├── SeedTypeCode (string) -- e.g., "guardian", "dungeon_core", "faction"
├── GameServiceId (Guid)
├── DisplayName (string)
├── Description (string)
├── MaxPerOwner (int) -- e.g., 3 for guardian spirits
├── AllowedOwnerTypes (list<string>) -- e.g., ["account"] for guardian, ["actor"] for dungeon
├── GrowthPhases (ordered list)
│   ├── PhaseCode (string) -- e.g., "nascent", "awakening", "aware", "attuned"
│   ├── DisplayName (string)
│   └── MinTotalGrowth (float) -- threshold to enter this phase
├── BondCardinality (int) -- 0 = no bonding, 1 = pair bond, N = group bond
└── CapabilityRules (list)
    ├── CapabilityCode (string) -- e.g., "combat.stance"
    ├── Domain (string) -- which domain this maps to
    ├── UnlockThreshold (float) -- minimum domain depth to unlock
    └── FidelityFormula (string) -- how depth maps to fidelity (e.g., "linear", "logarithmic")
```

For Arcadia's guardian spirits, the seed type definition would be:
- SeedTypeCode: `"guardian"`
- MaxPerOwner: 3
- AllowedOwnerTypes: `["account"]`
- GrowthPhases: Nascent (0), Awakening (5.0), Aware (25.0), Attuned (100.0)
- BondCardinality: 1 (pair bonds)

#### Growth Domains

A flat key-value map where keys are dot-separated domain paths and values are floating-point depth scores (0.0 to unbounded). Domains are fully dynamic -- new domains are created on first contribution. lib-seed doesn't define what domains exist; consumers do.

```
combat              → 5.0
combat.melee        → 3.2
combat.melee.sword  → 1.8
crafting            → 8.0
crafting.smithing   → 6.5
```

Sub-domain keys are published by consuming services via events (same pattern as Resource reference tracking). The Seed service stores whatever domain keys it receives.

#### Growth Phases

Phases are computed from total growth against the seed type's configured thresholds. They are a convenience classification, never hard gates. Phase labels are defined per seed type -- guardian spirits use "Nascent/Awakening/Aware/Attuned", dungeon cores might use "Dormant/Stirring/Awakened/Ancient", factions might use "Band/Organization/Institution/Empire".

#### Capability Manifests

Computed from growth domains using the seed type's capability rules. The manifest is a generic structured document -- lib-seed doesn't know what capabilities mean; consumers interpret them.

```
CapabilityManifest
├── SeedId (Guid)
├── SeedTypeCode (string)
├── ComputedAt (DateTime)
├── Version (int) -- increments on recomputation
└── Capabilities[]
    ├── CapabilityCode (string) -- e.g., "combat.stance"
    ├── Domain (string) -- parent growth domain
    ├── Fidelity (float) -- 0.0 to 1.0
    └── Unlocked (bool)
```

For Arcadia's guardian spirits, lib-gardener interprets these capabilities as UX modules and pushes them to the client. For dungeon cores, the Actor service would interpret them as spawning/building permissions. Same data structure, different interpretation.

#### Seed Bonds

Bonds connect two or more seeds with configurable semantics. Bond cardinality is defined by the seed type (pair = 1, group = N, none = 0).

```
SeedBond
├── BondId (Guid)
├── SeedTypeCode (string) -- bonds are between seeds of the same type
├── Participants[]
│   ├── SeedId (Guid)
│   ├── JoinedAt (DateTime)
│   └── Role (string?) -- optional role within the bond
├── CreatedAt (DateTime)
├── Status (BondStatus enum: PendingConfirmation, Active)
├── BondStrength (float) -- grows with shared experience
└── SharedGrowth (float) -- total co-present growth
```

For guardian spirits, this is the pair bond (Rynax-inspired, permanent, 1:1). For dungeon cores, it could be a dungeon-master bond. For factions, it could be an alliance bond. The semantics are determined by the consumer, not by lib-seed.

### API Endpoints

```
# Seed CRUD
POST /seed/create                    -- Create a new seed
POST /seed/get                       -- Get seed by ID
POST /seed/get-by-owner              -- Get seeds by owner ID and type
POST /seed/list                      -- List seeds with filtering
POST /seed/update                    -- Update seed metadata/display name
POST /seed/activate                  -- Set a seed as active (deactivates others of same type for owner)
POST /seed/archive                   -- Archive a seed (soft delete)

# Growth
POST /seed/growth/get                -- Get full growth domain map
POST /seed/growth/record             -- Record growth in a domain (internal, from consuming services)
POST /seed/growth/record-batch       -- Record growth across multiple domains atomically
POST /seed/growth/get-phase          -- Get current growth phase

# Capabilities
POST /seed/capability/get-manifest   -- Get current capability manifest

# Seed Type Definitions
POST /seed/type/register             -- Register a new seed type definition
POST /seed/type/get                  -- Get seed type definition
POST /seed/type/list                 -- List registered seed types
POST /seed/type/update               -- Update seed type definition

# Bonds
POST /seed/bond/initiate             -- Begin bond process between seeds
POST /seed/bond/confirm              -- Confirm a pending bond
POST /seed/bond/get                  -- Get bond by ID
POST /seed/bond/get-for-seed         -- Get bond for a specific seed
POST /seed/bond/get-partner          -- Get partner seed(s) public info
```

### Events

```yaml
# Published by Seed
seed.created                         -- New seed created
seed.phase.changed                   -- Seed transitioned growth phases
seed.growth.updated                  -- Growth domain values changed
seed.capability.updated              -- Capability manifest recomputed
seed.activated                       -- Seed set as active for owner
seed.archived                        -- Seed archived
seed.bond.formed                     -- Bond formed between seeds

# Consumed by Seed (from any service)
seed.growth.contributed              -- External service reports growth gain
```

The `seed.growth.contributed` event follows the same pattern as Resource's `resource.reference.registered` -- Seed defines the event schema, and any service publishes to it. Seed never depends on the contributor.

```
SeedGrowthContributedEvent
├── SeedId (Guid)
├── Domain (string) -- e.g., "combat.melee.sword"
├── Amount (float) -- growth to add
├── Source (string) -- contributing service (e.g., "character-encounter", "analytics")
├── SourceEventId (Guid?) -- optional reference to originating event
└── Context (map<string, string>) -- additional context for auditing
```

### Configuration

```yaml
SeedServiceConfiguration:
  CapabilityRecomputeDebounceMs: 5000    # Debounce recomputation on rapid growth
  GrowthDecayEnabled: false               # Whether unused domains decay over time
  GrowthDecayRatePerDay: 0.01            # If enabled, daily decay rate
  BondSharedGrowthMultiplier: 1.5        # Bonus when bonded seeds grow together
  MaxSeedTypesPerGameService: 50         # Safety limit
  DefaultMaxSeedsPerOwner: 3             # Default if not specified in type definition
```

---

## Gardener Service

### Identity

- **Service name**: `gardener`
- **Layer**: L4 Game Features
- **Lifetime**: Scoped
- **Plugin**: `plugins/lib-gardener/`

### Why L4

Gardener needs to read from nearly every layer: Seed and Game Session (L2), Asset (L3), Analytics, Behavior, Puppetmaster, Matchmaking (L4). It orchestrates the full player experience by composing content from across the service hierarchy. Classic L4 -- optional, feature-rich, maximum connectivity.

### Relationship to lib-seed

Gardener is the first and primary consumer of lib-seed. It interprets seed capabilities as UX modules, uses seed growth phases for scenario selection, and manages seed bonds as the pair system described in PLAYER-VISION.md. However, lib-gardener does NOT own seeds -- it creates and grows them via lib-seed's API, just as lib-escrow doesn't own currencies or items but orchestrates them via lib-currency and lib-item.

### Core Concepts

#### Void Instances

Each player in the void gets a dedicated void instance -- a lightweight server-side representation of their personal void space.

```
VoidInstance
├── VoidInstanceId (Guid)
├── SeedId (Guid) -- the player's active guardian seed
├── AccountId (Guid)
├── SessionId (Guid) -- Connect session ID
├── CreatedAt (DateTime)
├── Position (Vec3) -- player's current position in void space
├── Velocity (Vec3) -- current movement vector
├── ActivePois[] (list of active point-of-interest IDs)
├── Phase (DeploymentPhase enum: Alpha, Beta, Release)
├── ScenarioHistory[] -- recently visited scenario template IDs
└── DriftMetrics
    ├── TotalDistance (float)
    ├── DirectionalBias (Vec3) -- normalized average direction
    ├── HesitationCount (int) -- times the player stopped/reversed
    └── EngagementPattern (string) -- computed pattern label
```

Void instances are ephemeral (Redis-backed). They exist only while the player is in the void and are destroyed when they enter a scenario or disconnect.

#### Points of Interest (POIs)

Spawned dynamically by the Gardener based on seed metadata, movement patterns, and available content.

```
PointOfInterest
├── PoiId (Guid)
├── VoidInstanceId (Guid)
├── Position (Vec3) -- position in the void relative to player
├── PoiType (PoiType enum: Visual, Auditory, Environmental, Portal, Social)
├── ScenarioTemplateId (Guid) -- what scenario this leads to
├── Presentation
│   ├── VisualHint (string) -- client-interpreted visual cue
│   ├── AudioHint (string?) -- optional sound/music identifier
│   └── IntensityRamp (float) -- how aggressively the POI draws attention (0.0-1.0)
├── TriggerMode (TriggerMode enum: Proximity, Interaction, Prompted, Forced)
├── TriggerRadius (float) -- for proximity-triggered POIs
├── SpawnedAt (DateTime)
├── ExpiresAt (DateTime?) -- POIs may time out and despawn
└── Status (PoiStatus enum: Active, Entered, Declined, Expired)
```

#### Scenario Templates

Registered definitions of playable experiences. These are the building blocks that Gardener selects from when populating a player's void.

```
ScenarioTemplate
├── ScenarioTemplateId (Guid)
├── Code (string) -- human-readable identifier (e.g., "combat-basics-arena")
├── DisplayName (string)
├── Description (string)
├── Category (ScenarioCategory enum: Combat, Crafting, Social, Trade,
│     Exploration, Magic, Survival, Mixed, Narrative, Tutorial)
├── Subcategory (string?) -- finer classification within category
├── DomainWeights (map<string, float>) -- which growth domains this teaches
├── MinGrowthPhase (string) -- minimum seed phase to be offered this scenario
├── ConnectivityMode (ConnectivityMode enum: Isolated, WorldSlice, Persistent)
│     -- Alpha=Isolated, Beta=WorldSlice, Release=Persistent
├── AllowedPhases (list<DeploymentPhase>) -- which deployment phases can offer this
├── MaxConcurrentInstances (int) -- capacity limit
├── EstimatedDurationMinutes (int?) -- expected play time
├── Prerequisites
│   ├── RequiredDomains (map<string, float>) -- minimum domain depths
│   ├── RequiredScenarios (list<string>) -- scenario codes that must be completed first
│   └── ExcludedScenarios (list<string>) -- scenarios that exclude this one
├── Chaining
│   ├── LeadsTo (list<string>) -- scenario codes reachable from within this one
│   ├── ChainProbabilities (map<string, float>) -- weighted selection for chain offers
│   └── MaxChainDepth (int) -- how deep chaining can go before returning to void
├── Multiplayer
│   ├── MinPlayers (int) -- 1 for solo, 2+ for group
│   ├── MaxPlayers (int)
│   ├── MatchmakingQueueCode (string?) -- if this uses matchmaking for grouping
│   └── BondPreferred (bool) -- prioritize bonded seeds for this scenario
├── Content
│   ├── BehaviorDocumentId (string?) -- ABML document for scenario orchestration
│   ├── SceneDocumentId (Guid?) -- Scene service document for environment
│   ├── RealmId (Guid?) -- for WorldSlice mode, which realm to slice from
│   └── LocationCode (string?) -- for WorldSlice mode, where in the realm
├── CreatedAt (DateTime)
├── UpdatedAt (DateTime)
└── Status (TemplateStatus enum: Draft, Active, Deprecated)
```

#### Scenario Instances

Running instances of a scenario template, created when a player (or group) enters a scenario.

```
ScenarioInstance
├── ScenarioInstanceId (Guid)
├── ScenarioTemplateId (Guid)
├── GameSessionId (Guid) -- mapped to a Game Session for full service stack access
├── Participants[]
│   ├── SeedId (Guid)
│   ├── AccountId (Guid)
│   ├── SessionId (Guid)
│   ├── JoinedAt (DateTime)
│   └── Role (string) -- participant role within the scenario
├── ConnectivityMode (ConnectivityMode) -- inherited from template
├── Status (ScenarioStatus enum: Initializing, Active, Completing, Completed, Abandoned)
├── CreatedAt (DateTime)
├── CompletedAt (DateTime?)
├── GrowthAwarded (map<string, float>) -- domains and amounts awarded on completion
└── ChainedFrom (Guid?) -- if this was entered via chaining from another scenario
```

#### Deployment Phase Configuration

Global configuration that controls the scenario routing aperture.

```
DeploymentPhaseConfig
├── CurrentPhase (DeploymentPhase enum: Alpha, Beta, Release)
├── AlphaConfig
│   ├── MaxConcurrentScenarios (int)
│   ├── AllowedConnectivityModes: [Isolated]
│   └── GroupScenariosEnabled (bool)
├── BetaConfig
│   ├── MaxConcurrentScenarios (int)
│   ├── AllowedConnectivityModes: [Isolated, WorldSlice]
│   ├── WorldSliceRealms (list<Guid>) -- which realms can be sliced into
│   └── MaxWorldSliceDurationMinutes (int)
├── ReleaseConfig
│   ├── MaxConcurrentScenarios (int)
│   ├── AllowedConnectivityModes: [Isolated, WorldSlice, Persistent]
│   ├── PersistentEntryEnabled (bool) -- the "big switch"
│   └── VoidMinigamesEnabled (bool) -- keep void POIs active post-release
└── UpdatedAt (DateTime)
```

### Scenario Selection Algorithm

The Gardener's core intelligence is selecting which POIs to spawn for a given player. This is configuration-driven, not hardcoded, but the general algorithm:

1. **Query eligible templates**: Filter by seed growth phase, deployment phase, prerequisites, and cooldowns (don't re-offer recently declined/completed scenarios)
2. **Score by affinity**: Weight templates by how well their `DomainWeights` align with the seed's growth profile. High-growth seeds get scenarios that deepen existing domains; low-growth seeds get diverse breadth scenarios.
3. **Apply diversity pressure**: Avoid offering the same category repeatedly. If the last 3 POIs were combat, bias toward other categories.
4. **Apply narrative pressure**: If the player's drift metrics suggest a pattern (e.g., always moving in one direction, high hesitation indicating uncertainty), offer scenarios that respond to that pattern.
5. **Apply capacity constraints**: Don't offer scenarios that are at max concurrent instances.
6. **Select presentation**: Choose POI type and trigger mode based on the scenario's nature and the player's demonstrated response patterns (players that avoid prompted POIs get more proximity-triggered ones).

This algorithm runs periodically (configurable interval, maybe every 5-10 seconds) for each active void instance.

### API Endpoints

```
# Void Management
POST /gardener/void/enter              -- Player enters the void (creates void instance)
POST /gardener/void/get                -- Get current void instance state
POST /gardener/void/update-position    -- Report position/velocity (high frequency)
POST /gardener/void/leave              -- Player leaves the void (cleanup)

# POI Interaction
POST /gardener/poi/list                -- List active POIs for current void instance
POST /gardener/poi/interact            -- Player interacts with a POI
POST /gardener/poi/decline             -- Player explicitly declines a prompted POI

# Scenario Lifecycle
POST /gardener/scenario/enter          -- Enter a scenario (from POI or chain)
POST /gardener/scenario/get            -- Get current scenario state
POST /gardener/scenario/complete       -- Mark scenario as completed
POST /gardener/scenario/abandon        -- Abandon scenario, return to void
POST /gardener/scenario/chain          -- Enter a chained scenario from within current

# Scenario Template Management (admin/developer)
POST /gardener/template/create         -- Register a new scenario template
POST /gardener/template/get            -- Get template by ID
POST /gardener/template/get-by-code    -- Get template by code
POST /gardener/template/list           -- List templates with filtering
POST /gardener/template/update         -- Update template
POST /gardener/template/deprecate      -- Deprecate template

# Deployment Phase (admin)
POST /gardener/phase/get               -- Get current deployment phase config
POST /gardener/phase/update            -- Update deployment phase
POST /gardener/phase/get-metrics       -- Get phase-level metrics

# Bond Scenarios (paired players)
POST /gardener/bond/enter-together     -- Both bonded players enter a scenario together
POST /gardener/bond/get-shared-void    -- Get shared void state for bonded players
```

### Events

```yaml
# Published by Gardener
gardener.void.entered                  -- Player entered the void
gardener.void.left                     -- Player left the void
gardener.poi.spawned                   -- POI spawned for a player
gardener.poi.entered                   -- Player entered a POI / triggered a scenario
gardener.poi.declined                  -- Player declined a POI
gardener.poi.expired                   -- POI expired without interaction
gardener.scenario.started              -- Scenario instance created
gardener.scenario.completed            -- Scenario completed
gardener.scenario.abandoned            -- Scenario abandoned
gardener.scenario.chained              -- Player chained from one scenario to another
gardener.bond.entered-together         -- Bonded players entered a scenario together
gardener.phase.changed                 -- Deployment phase changed

# Consumed by Gardener
seed.phase.changed                     -- Adjust scenario offerings based on new phase
seed.growth.updated                    -- Recalculate scenario scoring
seed.bond.formed                       -- Enable bond-specific scenarios
seed.activated                         -- Track which seed is active
game-session.ended                     -- Scenario instance cleanup
analytics.milestone.reached            -- Inform scenario selection
```

### Background Services

#### Void Orchestrator Worker

A background service that runs the scenario selection algorithm for all active void instances at a configurable interval.

Responsibilities:
- Iterate active void instances
- Run scenario selection algorithm per instance
- Spawn/despawn POIs based on algorithm output
- Push POI updates to clients via Connect
- Clean up expired POIs
- Enforce capacity limits across all instances

#### Scenario Lifecycle Worker

Manages scenario instance lifecycle:
- Detect abandoned scenarios (player disconnected, timeout exceeded)
- Award growth on completion (publish `seed.growth.contributed` events to lib-seed)
- Clean up completed/abandoned instances
- Manage Game Session lifecycle for scenario instances

### Configuration

```yaml
GardenerServiceConfiguration:
  # Void orchestration
  VoidTickIntervalMs: 5000             # How often to evaluate void instances
  MaxActivePoisPerVoid: 8              # Max concurrent POIs per player
  PoiDefaultTtlMinutes: 10            # How long a POI lives before expiring
  PoiSpawnRadiusMin: 50.0              # Minimum distance from player to spawn POI
  PoiSpawnRadiusMax: 200.0             # Maximum distance from player to spawn POI
  MinPoiSpacing: 30.0                  # Minimum distance between POIs

  # Scenario selection
  AffinityWeight: 0.4                  # Weight for domain affinity in scoring
  DiversityWeight: 0.3                 # Weight for category diversity
  NarrativeWeight: 0.2                 # Weight for drift-pattern narrative response
  RandomWeight: 0.1                    # Weight for randomness / discovery
  RecentScenarioCooldownMinutes: 30    # Don't re-offer recently completed scenarios

  # Scenario instances
  MaxConcurrentScenariosGlobal: 1000   # Total across all players
  ScenarioTimeoutMinutes: 60           # Max duration before forced completion
  AbandonDetectionMinutes: 5           # Time without input before marking abandoned
  GrowthAwardMultiplier: 1.0           # Global tuning knob for growth gains

  # Bond features
  BondSharedVoidEnabled: true          # Whether bonded players share a void instance
  BondScenarioPriority: 1.5            # Scoring boost for bond-friendly scenarios

  # Phase defaults
  DefaultPhase: Alpha                  # Starting deployment phase

  # Seed integration
  SeedTypeCode: guardian               # Which seed type this gardener manages
```

---

## Integration Points

### Seed ← External Growth Contributions

Any service can publish growth contributions to seeds via the Seed service's own event topic. This is the same architectural pattern as Resource's reference tracking.

| Service | Domain Contributions | Trigger |
|---------|---------------------|---------|
| **Character Personality** (L4) | `combat.*`, `social.*` based on trait evolution | Personality trait shift events |
| **Character History** (L4) | `exploration.*`, `survival.*`, `stewardship.*` based on historical participation | History entry creation |
| **Character Encounter** (L4) | `social.*`, `combat.*` based on encounter type | Encounter resolution |
| **Analytics** (L4) | Any domain, based on aggregated statistics | Milestone events |
| **Behavior** (L4) | `magic.*`, `crafting.*` based on ABML execution context | Actor action completion |
| **Quest** (L2) | Domain matching quest category | Quest completion |

Each service knows which domains its data contributes to and publishes `seed.growth.contributed` with the appropriate domain key and amount. Seed doesn't need to know about any contributing service -- it just processes incoming growth events.

### Gardener → Seed

Gardener creates guardian seeds for new players, queries seed state for scenario selection, and triggers growth recording when scenarios complete. All via lib-seed's API -- Gardener doesn't own seed data.

Key interactions:
- Player first login → Gardener calls `seed/create` with OwnerType="account", SeedTypeCode="guardian"
- Scenario selection → Gardener calls `seed/growth/get` and `seed/capability/get-manifest`
- Scenario completion → Gardener publishes `seed.growth.contributed` events
- Void entry → Gardener calls `seed/get-by-owner` to find the player's active seed

### Gardener → Game Session

When a player enters a scenario, Gardener creates a Game Session (matchmade type) with a reservation token. The full service stack becomes available to the scenario instance through the Game Session. When the scenario ends, the Game Session is closed.

For WorldSlice and Persistent connectivity modes, the Game Session connects to actual realm state. For Isolated mode, the Game Session spins up a sandboxed instance.

### Gardener → Puppetmaster

For scenarios that involve NPCs (most of them), Gardener delegates NPC orchestration to Puppetmaster. Gardener decides WHAT scenario to run; Puppetmaster decides HOW the NPCs in that scenario behave. Clean separation:

- Gardener: "Run the 'village market' scenario for this player"
- Puppetmaster: "Load the market NPC behaviors, coordinate the Event Brain for any interactions"

### Gardener → Matchmaking

For group scenarios, Gardener can submit matchmaking tickets to group players into shared scenario instances. Matchmaking handles the queuing and acceptance flow; Gardener handles the scenario lifecycle once matched.

### Seed → Connect (Capability Distribution)

When the Seed service recomputes a capability manifest, it publishes `seed.capability.updated`. For guardian-type seeds, the Gardener (or a consumer registered on Connect's behalf) picks this up, interprets the capabilities as UX modules, and pushes the manifest to the client via the existing per-session RabbitMQ subscription channel -- the same mechanism used for permission capability updates.

### Seed Bonds → Connect (Pair Communication)

For bonded seeds, Connect establishes a shared communication channel. The bond exists in lib-seed; the communication channel is managed by Connect using the bond ID as the subscription key.

---

## Extensions to Existing Services

These are small changes needed in existing services to support the Seed/Gardener system.

### Connect

- **Bond communication channel**: When a seed bond exists, establish a shared RabbitMQ subscription that both players can publish to and receive from. This is a dedicated channel, not routed through any service API -- it's player-to-player, using Connect as the transport layer.
- **Capability manifest push**: Subscribe to `seed.capability.updated` events and push manifest updates to the appropriate client session. Same pattern as existing permission capability pushes. Gardener interprets raw capabilities into UX-specific format before pushing.

### Analytics

- **Seed growth contribution**: After processing aggregated statistics, publish `seed.growth.contributed` events to feed the relevant seeds. This is a new event publication, not a new subscription -- Analytics already ingests the raw data.

### Permission

- No changes required. UX capabilities are a separate axis from endpoint permissions, owned by lib-seed (computed) and lib-gardener (interpreted), not Permission.

### Game Session

- **Gardener-managed sessions**: A new session source type (alongside lobby and matchmade) for Gardener-created scenario instances. Minimal change -- Game Session already supports multiple creation patterns.

---

## State Stores

### Seed Service

| Store Name | Backend | Purpose |
|------------|---------|---------|
| `seed-metadata` | MySQL | Seed entity records (durable, queryable by owner ID/type) |
| `seed-growth` | MySQL | Growth domain records (durable, queryable for analytics) |
| `seed-capabilities` | Redis | Computed capability manifests (cached, frequently read) |
| `seed-bonds` | MySQL | Bond records (durable) |
| `seed-type-definitions` | MySQL | Registered seed type definitions (durable, admin-managed) |

### Gardener Service

| Store Name | Backend | Purpose |
|------------|---------|---------|
| `gardener-void-instances` | Redis | Active void instance state (ephemeral, per-session) |
| `gardener-pois` | Redis | Active POIs per void instance (ephemeral) |
| `gardener-scenario-templates` | MySQL | Registered scenario template definitions (durable, queryable) |
| `gardener-scenario-instances` | Redis | Active scenario instance state (ephemeral) |
| `gardener-scenario-history` | MySQL | Completed scenario records (durable, for analytics and cooldown tracking) |
| `gardener-phase-config` | MySQL | Deployment phase configuration (durable, admin-managed) |

---

## Schema Drafts

### seed-api.yaml

```yaml
openapi: 3.0.0
info:
  title: Seed Service API
  version: 1.0.0
  description: >
    Generic progressive growth entity service (L2 GameFoundation). Seeds are
    entities that start empty and grow by accumulating metadata from external
    events, progressively gaining capabilities. Polymorphic ownership allows
    seeds to be bound to any entity type (accounts, actors, realms, characters,
    relationships). Growth domains are arbitrary key-value maps contributed by
    any consuming service via events, following the same pattern as Resource
    reference tracking. Capability manifests are computed from growth domains
    using configurable rules per seed type. The Seed service is agnostic to
    what seeds represent -- consumers (lib-gardener for player spirits,
    lib-actor for dungeon cores, etc.) provide the interpretation.
x-service-layer: GameFoundation

servers:
  - url: http://localhost:5012

paths:
  /seed/create:
    post:
      operationId: CreateSeed
      summary: Create a new seed
      description: >
        Creates a new seed bound to the specified owner. The seed type must
        be registered and the owner type must be allowed by the seed type
        definition. Returns conflict if creating this seed would exceed
        the type's MaxPerOwner limit.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CreateSeedRequest'
      responses:
        '200':
          description: Seed created successfully
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SeedResponse'

  /seed/get:
    post:
      operationId: GetSeed
      summary: Get seed by ID
      description: Returns the seed entity with current growth phase and summary data.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetSeedRequest'
      responses:
        '200':
          description: Seed found
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SeedResponse'

  /seed/get-by-owner:
    post:
      operationId: GetSeedsByOwner
      summary: Get seeds by owner ID and type
      description: >
        Returns all seeds owned by the specified entity. Optionally filter
        by seed type code and status.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetSeedsByOwnerRequest'
      responses:
        '200':
          description: Seeds found
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ListSeedsResponse'

  /seed/list:
    post:
      operationId: ListSeeds
      summary: List seeds with filtering
      description: >
        Returns seeds matching the specified filters. Supports filtering
        by seed type, owner type, growth phase, status, and game service.
      x-permissions: [developer]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListSeedsRequest'
      responses:
        '200':
          description: Seeds returned
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ListSeedsResponse'

  /seed/update:
    post:
      operationId: UpdateSeed
      summary: Update seed metadata or display name
      description: Updates mutable fields of a seed. Cannot change owner or seed type.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/UpdateSeedRequest'
      responses:
        '200':
          description: Seed updated
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SeedResponse'

  /seed/activate:
    post:
      operationId: ActivateSeed
      summary: Set a seed as active
      description: >
        Activates the specified seed. Only one seed of a given type can be
        active per owner at a time. Deactivates any previously active seed
        of the same type.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ActivateSeedRequest'
      responses:
        '200':
          description: Seed activated
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SeedResponse'

  /seed/archive:
    post:
      operationId: ArchiveSeed
      summary: Archive a seed
      description: >
        Soft-deletes a seed, preserving its data but removing it from active
        rotation. Archived seeds do not count toward the MaxPerOwner limit.
        Cannot archive an active seed.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ArchiveSeedRequest'
      responses:
        '200':
          description: Seed archived
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SeedResponse'

  /seed/growth/get:
    post:
      operationId: GetGrowth
      summary: Get full growth domain map
      description: >
        Returns the complete growth domain map for a seed, including all
        top-level and sub-domain entries with their current depth values.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetGrowthRequest'
      responses:
        '200':
          description: Growth data returned
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/GrowthResponse'

  /seed/growth/record:
    post:
      operationId: RecordGrowth
      summary: Record growth in a domain
      description: >
        Records growth in a specific domain for a seed. Primarily called
        internally by consuming services after processing game events.
        Triggers capability manifest recomputation if thresholds are crossed.
      x-permissions: [internal]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/RecordGrowthRequest'
      responses:
        '200':
          description: Growth recorded
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/GrowthResponse'

  /seed/growth/record-batch:
    post:
      operationId: RecordGrowthBatch
      summary: Record growth across multiple domains atomically
      description: >
        Records growth across multiple domains in a single atomic operation.
        Useful when a single game event contributes to multiple domains
        simultaneously.
      x-permissions: [internal]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/RecordGrowthBatchRequest'
      responses:
        '200':
          description: Growth recorded
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/GrowthResponse'

  /seed/growth/get-phase:
    post:
      operationId: GetGrowthPhase
      summary: Get current growth phase
      description: >
        Returns the current computed growth phase for the seed, based on
        the seed type's configured phase thresholds.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetGrowthPhaseRequest'
      responses:
        '200':
          description: Phase returned
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/GrowthPhaseResponse'

  /seed/capability/get-manifest:
    post:
      operationId: GetCapabilityManifest
      summary: Get current capability manifest
      description: >
        Returns the most recently computed capability manifest for the seed.
        Consumers interpret what capabilities mean (UX modules, spawning
        permissions, faction actions, etc.).
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetCapabilityManifestRequest'
      responses:
        '200':
          description: Manifest returned
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CapabilityManifestResponse'

  /seed/type/register:
    post:
      operationId: RegisterSeedType
      summary: Register a new seed type definition
      description: >
        Registers a seed type with its growth phase definitions, capability
        rules, bond cardinality, and owner type restrictions. Seed types
        are scoped to game services.
      x-permissions: [developer]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/RegisterSeedTypeRequest'
      responses:
        '200':
          description: Seed type registered
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SeedTypeResponse'

  /seed/type/get:
    post:
      operationId: GetSeedType
      summary: Get seed type definition
      description: Returns the full seed type definition including phases and capability rules.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetSeedTypeRequest'
      responses:
        '200':
          description: Seed type found
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SeedTypeResponse'

  /seed/type/list:
    post:
      operationId: ListSeedTypes
      summary: List registered seed types
      description: Returns all seed types registered for the specified game service.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListSeedTypesRequest'
      responses:
        '200':
          description: Seed types returned
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ListSeedTypesResponse'

  /seed/type/update:
    post:
      operationId: UpdateSeedType
      summary: Update seed type definition
      description: >
        Updates a seed type definition. Changes to phase thresholds or
        capability rules trigger recomputation for all seeds of this type.
      x-permissions: [developer]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/UpdateSeedTypeRequest'
      responses:
        '200':
          description: Seed type updated
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SeedTypeResponse'

  /seed/bond/initiate:
    post:
      operationId: InitiateBond
      summary: Begin bond process between seeds
      description: >
        Initiates a bond between seeds of the same type. All participants
        must confirm for the bond to become active. Returns conflict if
        any participant already has a bond and the type's cardinality is 1.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/InitiateBondRequest'
      responses:
        '200':
          description: Bond initiated, awaiting confirmation
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/BondResponse'

  /seed/bond/confirm:
    post:
      operationId: ConfirmBond
      summary: Confirm a pending bond
      description: >
        Confirms a pending bond. When all participants have confirmed, the
        bond becomes active. Bond permanence is determined by the seed type.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ConfirmBondRequest'
      responses:
        '200':
          description: Bond confirmed
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/BondResponse'

  /seed/bond/get:
    post:
      operationId: GetBond
      summary: Get bond by ID
      description: Returns the bond record with all participants and status.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetBondRequest'
      responses:
        '200':
          description: Bond found
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/BondResponse'

  /seed/bond/get-for-seed:
    post:
      operationId: GetBondForSeed
      summary: Get bond for a specific seed
      description: Returns the bond that the specified seed participates in, if any.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetBondForSeedRequest'
      responses:
        '200':
          description: Bond found
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/BondResponse'

  /seed/bond/get-partners:
    post:
      operationId: GetBondPartners
      summary: Get partner seed(s) public info
      description: Returns public information about the other seeds in the bond.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetBondPartnersRequest'
      responses:
        '200':
          description: Partners returned
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/BondPartnersResponse'

components:
  schemas:
    # --- Enums ---

    SeedStatus:
      type: string
      description: Lifecycle status of a seed.
      enum: [Active, Dormant, Archived]

    BondStatus:
      type: string
      description: Lifecycle status of a seed bond.
      enum: [PendingConfirmation, Active]

    # --- Seed Type Models ---

    GrowthPhaseDefinition:
      type: object
      description: Defines a growth phase with its threshold.
      required: [phaseCode, displayName, minTotalGrowth]
      properties:
        phaseCode:
          type: string
          description: >
            Machine-readable phase identifier (e.g., "nascent", "awakening").
        displayName:
          type: string
          description: Human-readable phase label.
        minTotalGrowth:
          type: number
          format: float
          description: Minimum total growth to enter this phase.

    CapabilityRule:
      type: object
      description: Maps a growth domain to a capability with unlock threshold and fidelity formula.
      required: [capabilityCode, domain, unlockThreshold, fidelityFormula]
      properties:
        capabilityCode:
          type: string
          description: Unique capability identifier (e.g., "combat.stance").
        domain:
          type: string
          description: Which growth domain this capability maps to.
        unlockThreshold:
          type: number
          format: float
          description: Minimum domain depth to unlock this capability.
        fidelityFormula:
          type: string
          description: >
            How domain depth maps to fidelity (0.0-1.0). Values: "linear",
            "logarithmic", "step". Consumers may define additional formulas.

    # --- Request Models ---

    CreateSeedRequest:
      type: object
      required: [ownerId, ownerType, seedTypeCode, gameServiceId]
      properties:
        ownerId:
          type: string
          format: uuid
          description: The entity that owns this seed.
        ownerType:
          type: string
          description: >
            Entity type discriminator (e.g., "account", "actor", "realm",
            "character", "relationship").
        seedTypeCode:
          type: string
          description: Registered seed type code (e.g., "guardian", "dungeon_core").
        gameServiceId:
          type: string
          format: uuid
          description: Game service this seed is scoped to.
        displayName:
          type: string
          nullable: true
          description: Human-readable name. Auto-generated if omitted.
        metadata:
          type: object
          additionalProperties: true
          nullable: true
          description: Seed-type-specific initial metadata.

    GetSeedRequest:
      type: object
      required: [seedId]
      properties:
        seedId:
          type: string
          format: uuid
          description: The seed to retrieve.

    GetSeedsByOwnerRequest:
      type: object
      required: [ownerId, ownerType]
      properties:
        ownerId:
          type: string
          format: uuid
          description: The owner entity ID.
        ownerType:
          type: string
          description: The owner entity type.
        seedTypeCode:
          type: string
          nullable: true
          description: Filter by seed type.
        includeArchived:
          type: boolean
          description: Whether to include archived seeds.
          default: false

    ListSeedsRequest:
      type: object
      properties:
        seedTypeCode:
          type: string
          nullable: true
          description: Filter by seed type.
        ownerType:
          type: string
          nullable: true
          description: Filter by owner type.
        gameServiceId:
          type: string
          format: uuid
          nullable: true
          description: Filter by game service.
        growthPhase:
          type: string
          nullable: true
          description: Filter by current growth phase code.
        status:
          $ref: '#/components/schemas/SeedStatus'
          nullable: true
        page:
          type: integer
          default: 1
        pageSize:
          type: integer
          default: 50

    UpdateSeedRequest:
      type: object
      required: [seedId]
      properties:
        seedId:
          type: string
          format: uuid
          description: The seed to update.
        displayName:
          type: string
          nullable: true
          description: New display name.
        metadata:
          type: object
          additionalProperties: true
          nullable: true
          description: Metadata fields to merge (set key to null to delete).

    ActivateSeedRequest:
      type: object
      required: [seedId]
      properties:
        seedId:
          type: string
          format: uuid
          description: The seed to activate.

    ArchiveSeedRequest:
      type: object
      required: [seedId]
      properties:
        seedId:
          type: string
          format: uuid
          description: The seed to archive.

    GetGrowthRequest:
      type: object
      required: [seedId]
      properties:
        seedId:
          type: string
          format: uuid
          description: The seed whose growth to retrieve.

    RecordGrowthRequest:
      type: object
      required: [seedId, domain, amount, source]
      properties:
        seedId:
          type: string
          format: uuid
          description: The seed to record growth for.
        domain:
          type: string
          description: >
            Dot-separated domain path (e.g., "combat.melee.sword").
            New domains are created automatically on first contribution.
        amount:
          type: number
          format: float
          description: Amount of growth to add.
        source:
          type: string
          description: Identifier of the contributing service (e.g., "character-encounter").
        sourceEventId:
          type: string
          format: uuid
          nullable: true
          description: Optional reference to the originating event.

    RecordGrowthBatchRequest:
      type: object
      required: [seedId, entries, source]
      properties:
        seedId:
          type: string
          format: uuid
          description: The seed to record growth for.
        entries:
          type: array
          items:
            $ref: '#/components/schemas/GrowthEntry'
          description: Domain-amount pairs to record.
        source:
          type: string
          description: Identifier of the contributing service.

    GrowthEntry:
      type: object
      description: A single domain-amount pair for batch growth recording.
      required: [domain, amount]
      properties:
        domain:
          type: string
          description: Domain path.
        amount:
          type: number
          format: float
          description: Growth amount.

    GetGrowthPhaseRequest:
      type: object
      required: [seedId]
      properties:
        seedId:
          type: string
          format: uuid
          description: The seed whose phase to retrieve.

    GetCapabilityManifestRequest:
      type: object
      required: [seedId]
      properties:
        seedId:
          type: string
          format: uuid
          description: The seed whose manifest to retrieve.

    RegisterSeedTypeRequest:
      type: object
      required:
        [seedTypeCode, gameServiceId, displayName, description,
         maxPerOwner, allowedOwnerTypes, growthPhases, bondCardinality]
      properties:
        seedTypeCode:
          type: string
          description: Unique code for this seed type (e.g., "guardian", "dungeon_core").
        gameServiceId:
          type: string
          format: uuid
          description: Game service this type is scoped to.
        displayName:
          type: string
          description: Human-readable name.
        description:
          type: string
          description: Description of what this seed type represents.
        maxPerOwner:
          type: integer
          description: Maximum seeds of this type per owner entity.
        allowedOwnerTypes:
          type: array
          items:
            type: string
          description: Entity types that can own seeds of this type.
        growthPhases:
          type: array
          items:
            $ref: '#/components/schemas/GrowthPhaseDefinition'
          description: Ordered growth phase definitions with thresholds.
        bondCardinality:
          type: integer
          description: >
            Max bond participants. 0 = no bonding, 1 = pair bonds,
            N = group bonds of up to N+1 participants.
        capabilityRules:
          type: array
          items:
            $ref: '#/components/schemas/CapabilityRule'
          nullable: true
          description: Rules for computing capabilities from growth domains.

    GetSeedTypeRequest:
      type: object
      required: [seedTypeCode, gameServiceId]
      properties:
        seedTypeCode:
          type: string
          description: The seed type code.
        gameServiceId:
          type: string
          format: uuid
          description: The game service scope.

    ListSeedTypesRequest:
      type: object
      required: [gameServiceId]
      properties:
        gameServiceId:
          type: string
          format: uuid
          description: Game service to list seed types for.

    UpdateSeedTypeRequest:
      type: object
      required: [seedTypeCode, gameServiceId]
      properties:
        seedTypeCode:
          type: string
          description: The seed type to update.
        gameServiceId:
          type: string
          format: uuid
          description: The game service scope.
        displayName:
          type: string
          nullable: true
          description: New display name.
        description:
          type: string
          nullable: true
          description: New description.
        maxPerOwner:
          type: integer
          nullable: true
          description: Updated maximum per owner.
        growthPhases:
          type: array
          items:
            $ref: '#/components/schemas/GrowthPhaseDefinition'
          nullable: true
          description: Updated phase definitions.
        capabilityRules:
          type: array
          items:
            $ref: '#/components/schemas/CapabilityRule'
          nullable: true
          description: Updated capability rules.

    InitiateBondRequest:
      type: object
      required: [initiatorSeedId, targetSeedId]
      properties:
        initiatorSeedId:
          type: string
          format: uuid
          description: The seed initiating the bond.
        targetSeedId:
          type: string
          format: uuid
          description: The seed being invited to bond.

    ConfirmBondRequest:
      type: object
      required: [bondId, confirmingSeedId]
      properties:
        bondId:
          type: string
          format: uuid
          description: The bond to confirm.
        confirmingSeedId:
          type: string
          format: uuid
          description: The seed confirming the bond.

    GetBondRequest:
      type: object
      required: [bondId]
      properties:
        bondId:
          type: string
          format: uuid
          description: The bond to retrieve.

    GetBondForSeedRequest:
      type: object
      required: [seedId]
      properties:
        seedId:
          type: string
          format: uuid
          description: The seed whose bond to retrieve.

    GetBondPartnersRequest:
      type: object
      required: [seedId]
      properties:
        seedId:
          type: string
          format: uuid
          description: The requesting seed (returns partner info).

    # --- Response Models ---

    SeedResponse:
      type: object
      required:
        [seedId, ownerId, ownerType, seedTypeCode, gameServiceId,
         createdAt, growthPhase, totalGrowth, bondId, displayName, status]
      properties:
        seedId:
          type: string
          format: uuid
          description: Unique identifier for this seed.
        ownerId:
          type: string
          format: uuid
          description: The entity that owns this seed.
        ownerType:
          type: string
          description: Owner entity type discriminator.
        seedTypeCode:
          type: string
          description: Registered seed type code.
        gameServiceId:
          type: string
          format: uuid
          description: Game service this seed is scoped to.
        createdAt:
          type: string
          format: date-time
          description: When the seed was created.
        growthPhase:
          type: string
          description: Current computed growth phase code.
        totalGrowth:
          type: number
          format: float
          description: Aggregate growth across all domains.
        bondId:
          type: string
          format: uuid
          nullable: true
          description: Bond ID if this seed is bonded, null otherwise.
        displayName:
          type: string
          description: Human-readable name.
        status:
          $ref: '#/components/schemas/SeedStatus'

    ListSeedsResponse:
      type: object
      required: [seeds, totalCount]
      properties:
        seeds:
          type: array
          items:
            $ref: '#/components/schemas/SeedResponse'
          description: Seeds matching the query.
        totalCount:
          type: integer
          description: Total matching seeds across all pages.

    GrowthResponse:
      type: object
      required: [seedId, totalGrowth, domains]
      properties:
        seedId:
          type: string
          format: uuid
          description: The seed this growth belongs to.
        totalGrowth:
          type: number
          format: float
          description: Aggregate growth across all domains.
        domains:
          type: object
          additionalProperties:
            type: number
            format: float
          description: >
            Map of domain path to depth value. Keys are dot-separated
            (e.g., "combat.melee.sword"). Values are floating-point depth scores.

    GrowthPhaseResponse:
      type: object
      required: [seedId, phaseCode, displayName, totalGrowth, nextPhaseCode, nextPhaseThreshold]
      properties:
        seedId:
          type: string
          format: uuid
          description: The seed.
        phaseCode:
          type: string
          description: Current phase code.
        displayName:
          type: string
          description: Current phase display name.
        totalGrowth:
          type: number
          format: float
          description: Current total growth.
        nextPhaseCode:
          type: string
          nullable: true
          description: Next phase code, null if at maximum.
        nextPhaseThreshold:
          type: number
          format: float
          nullable: true
          description: Growth needed for next phase, null if at maximum.

    CapabilityManifestResponse:
      type: object
      required: [seedId, seedTypeCode, computedAt, version, capabilities]
      properties:
        seedId:
          type: string
          format: uuid
          description: The seed this manifest belongs to.
        seedTypeCode:
          type: string
          description: The seed type for consumer interpretation.
        computedAt:
          type: string
          format: date-time
          description: When this manifest was last computed.
        version:
          type: integer
          description: Monotonically increasing version number.
        capabilities:
          type: array
          items:
            $ref: '#/components/schemas/Capability'
          description: List of capabilities with availability and fidelity.

    Capability:
      type: object
      required: [capabilityCode, domain, fidelity, unlocked]
      properties:
        capabilityCode:
          type: string
          description: >
            Unique capability identifier. Consumer-interpreted
            (e.g., UX module ID, spawning permission, faction action).
        domain:
          type: string
          description: Growth domain this capability maps to.
        fidelity:
          type: number
          format: float
          description: >
            Capability fidelity from 0.0 to 1.0. Higher values mean the
            seed has more developed capability in this area.
        unlocked:
          type: boolean
          description: Whether this capability is available at all.

    SeedTypeResponse:
      type: object
      required:
        [seedTypeCode, gameServiceId, displayName, description,
         maxPerOwner, allowedOwnerTypes, growthPhases, bondCardinality]
      properties:
        seedTypeCode:
          type: string
          description: Unique type code.
        gameServiceId:
          type: string
          format: uuid
          description: Game service scope.
        displayName:
          type: string
          description: Human-readable name.
        description:
          type: string
          description: Type description.
        maxPerOwner:
          type: integer
          description: Maximum seeds of this type per owner.
        allowedOwnerTypes:
          type: array
          items:
            type: string
          description: Allowed owner entity types.
        growthPhases:
          type: array
          items:
            $ref: '#/components/schemas/GrowthPhaseDefinition'
          description: Phase definitions with thresholds.
        bondCardinality:
          type: integer
          description: Bond participant limit.
        capabilityRules:
          type: array
          items:
            $ref: '#/components/schemas/CapabilityRule'
          nullable: true
          description: Capability computation rules.

    ListSeedTypesResponse:
      type: object
      required: [seedTypes]
      properties:
        seedTypes:
          type: array
          items:
            $ref: '#/components/schemas/SeedTypeResponse'
          description: Registered seed types.

    BondResponse:
      type: object
      required: [bondId, seedTypeCode, participants, createdAt, status, bondStrength, sharedGrowth]
      properties:
        bondId:
          type: string
          format: uuid
          description: Unique bond identifier.
        seedTypeCode:
          type: string
          description: Seed type this bond connects.
        participants:
          type: array
          items:
            $ref: '#/components/schemas/BondParticipant'
          description: Seeds participating in this bond.
        createdAt:
          type: string
          format: date-time
          description: When the bond was formed.
        status:
          $ref: '#/components/schemas/BondStatus'
        bondStrength:
          type: number
          format: float
          description: Grows with shared growth. Consumer-interpreted.
        sharedGrowth:
          type: number
          format: float
          description: Total accumulated shared growth.

    BondParticipant:
      type: object
      required: [seedId, joinedAt]
      properties:
        seedId:
          type: string
          format: uuid
          description: Participant seed ID.
        joinedAt:
          type: string
          format: date-time
          description: When this seed joined the bond.
        role:
          type: string
          nullable: true
          description: Optional role within the bond.

    BondPartnersResponse:
      type: object
      required: [bondId, partners]
      properties:
        bondId:
          type: string
          format: uuid
          description: The bond.
        partners:
          type: array
          items:
            $ref: '#/components/schemas/PartnerSummary'
          description: Partner seed summaries.

    PartnerSummary:
      type: object
      required: [seedId, ownerId, ownerType, growthPhase, status]
      properties:
        seedId:
          type: string
          format: uuid
          description: Partner seed ID.
        ownerId:
          type: string
          format: uuid
          description: Partner's owner entity ID.
        ownerType:
          type: string
          description: Partner's owner entity type.
        growthPhase:
          type: string
          description: Partner's current growth phase.
        status:
          $ref: '#/components/schemas/SeedStatus'
```

### gardener-api.yaml

```yaml
openapi: 3.0.0
info:
  title: Gardener Service API
  version: 1.0.0
  description: >
    Player experience orchestration service (L4 GameFeatures) for void
    navigation, scenario routing, and progressive discovery. The player-side
    counterpart to Puppetmaster: where Puppetmaster orchestrates what NPCs
    experience, Gardener orchestrates what players experience. Manages void
    instances (personal ambient spaces between game sessions), dynamically
    spawns points of interest based on seed metadata and movement patterns,
    routes players into scenarios (isolated sandboxes, world slices, or
    persistent realm entry depending on deployment phase), and coordinates
    scenario chaining for emergent discovery paths. First and primary
    consumer of lib-seed, interpreting seed capabilities as UX modules
    and seed bonds as the pair system described in PLAYER-VISION.md.
    Features a background Void Orchestrator worker that evaluates all
    active void instances on a configurable tick interval.
x-service-layer: GameFeatures

servers:
  - url: http://localhost:5012

paths:
  # --- Void Management ---

  /gardener/void/enter:
    post:
      operationId: EnterVoid
      summary: Player enters the void
      description: >
        Creates a void instance for the player. Called on login or return
        from a scenario. Queries lib-seed for the player's active guardian
        seed and uses its growth data to initialize the void with
        appropriate POIs.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/EnterVoidRequest'
      responses:
        '200':
          description: Void instance created
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/VoidStateResponse'

  /gardener/void/get:
    post:
      operationId: GetVoidState
      summary: Get current void instance state
      description: Returns the current state of the player's void instance including active POIs.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetVoidStateRequest'
      responses:
        '200':
          description: Void state returned
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/VoidStateResponse'

  /gardener/void/update-position:
    post:
      operationId: UpdatePosition
      summary: Report player position and velocity
      description: >
        High-frequency position update from the client. Used by the Void
        Orchestrator worker to determine POI spawning, proximity triggers,
        and drift metric accumulation.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/UpdatePositionRequest'
      responses:
        '200':
          description: Position updated
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PositionUpdateResponse'

  /gardener/void/leave:
    post:
      operationId: LeaveVoid
      summary: Player leaves the void
      description: >
        Destroys the void instance. Called on disconnect or when entering
        a seed directly (bypassing scenario entry).
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/LeaveVoidRequest'
      responses:
        '200':
          description: Void instance destroyed
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/LeaveVoidResponse'

  # --- POI Interaction ---

  /gardener/poi/list:
    post:
      operationId: ListPois
      summary: List active POIs for current void instance
      description: Returns all currently active points of interest in the player's void instance.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListPoisRequest'
      responses:
        '200':
          description: POIs returned
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ListPoisResponse'

  /gardener/poi/interact:
    post:
      operationId: InteractWithPoi
      summary: Player interacts with a POI
      description: >
        Signals that the player has engaged with a point of interest. May
        result in a scenario prompt, immediate scenario entry, or additional
        POI state changes.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/InteractWithPoiRequest'
      responses:
        '200':
          description: Interaction processed
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PoiInteractionResponse'

  /gardener/poi/decline:
    post:
      operationId: DeclinePoi
      summary: Explicitly decline a prompted POI
      description: >
        Declines a POI that presented a prompt. Factored into future scenario
        selection scoring.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/DeclinePoiRequest'
      responses:
        '200':
          description: POI declined
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/DeclinePoiResponse'

  # --- Scenario Lifecycle ---

  /gardener/scenario/enter:
    post:
      operationId: EnterScenario
      summary: Enter a scenario
      description: >
        Enters a scenario from a POI interaction or direct selection. Creates
        a scenario instance backed by a Game Session. The player transitions
        from void to scenario.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/EnterScenarioRequest'
      responses:
        '200':
          description: Scenario entered
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ScenarioStateResponse'

  /gardener/scenario/get:
    post:
      operationId: GetScenarioState
      summary: Get current scenario state
      description: Returns the current state of the player's active scenario instance.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetScenarioStateRequest'
      responses:
        '200':
          description: Scenario state returned
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ScenarioStateResponse'

  /gardener/scenario/complete:
    post:
      operationId: CompleteScenario
      summary: Complete a scenario
      description: >
        Marks the current scenario as completed. Awards growth to the
        player's seed via lib-seed. Returns the player to the void.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CompleteScenarioRequest'
      responses:
        '200':
          description: Scenario completed
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ScenarioCompletionResponse'

  /gardener/scenario/abandon:
    post:
      operationId: AbandonScenario
      summary: Abandon scenario and return to void
      description: >
        Abandons the current scenario without completion. Partial growth
        may be awarded based on participation.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/AbandonScenarioRequest'
      responses:
        '200':
          description: Scenario abandoned
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/AbandonScenarioResponse'

  /gardener/scenario/chain:
    post:
      operationId: ChainScenario
      summary: Chain to another scenario from within current
      description: >
        Enters a chained scenario from within the current active scenario.
        Chain depth is limited by the template's MaxChainDepth.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ChainScenarioRequest'
      responses:
        '200':
          description: Chained to new scenario
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ScenarioStateResponse'

  # --- Scenario Template Management ---

  /gardener/template/create:
    post:
      operationId: CreateTemplate
      summary: Register a new scenario template
      description: >
        Creates a new scenario template definition with prerequisites,
        chaining rules, and content references.
      x-permissions: [developer]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CreateTemplateRequest'
      responses:
        '200':
          description: Template created
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ScenarioTemplateResponse'

  /gardener/template/get:
    post:
      operationId: GetTemplate
      summary: Get scenario template by ID
      description: Returns the full scenario template definition.
      x-permissions: [developer]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetTemplateRequest'
      responses:
        '200':
          description: Template returned
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ScenarioTemplateResponse'

  /gardener/template/get-by-code:
    post:
      operationId: GetTemplateByCode
      summary: Get scenario template by code
      description: Looks up a template by its human-readable code identifier.
      x-permissions: [developer]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetTemplateByCodeRequest'
      responses:
        '200':
          description: Template returned
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ScenarioTemplateResponse'

  /gardener/template/list:
    post:
      operationId: ListTemplates
      summary: List scenario templates with filtering
      description: >
        Returns scenario templates matching the specified filters.
      x-permissions: [developer]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListTemplatesRequest'
      responses:
        '200':
          description: Templates returned
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ListTemplatesResponse'

  /gardener/template/update:
    post:
      operationId: UpdateTemplate
      summary: Update a scenario template
      description: Updates fields of an existing scenario template.
      x-permissions: [developer]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/UpdateTemplateRequest'
      responses:
        '200':
          description: Template updated
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ScenarioTemplateResponse'

  /gardener/template/deprecate:
    post:
      operationId: DeprecateTemplate
      summary: Deprecate a scenario template
      description: >
        Marks a template as deprecated. Not offered to new players but
        existing instances may complete.
      x-permissions: [developer]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/DeprecateTemplateRequest'
      responses:
        '200':
          description: Template deprecated
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ScenarioTemplateResponse'

  # --- Deployment Phase ---

  /gardener/phase/get:
    post:
      operationId: GetPhaseConfig
      summary: Get current deployment phase configuration
      description: Returns the active deployment phase and its configuration.
      x-permissions: [developer]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetPhaseConfigRequest'
      responses:
        '200':
          description: Phase config returned
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PhaseConfigResponse'

  /gardener/phase/update:
    post:
      operationId: UpdatePhaseConfig
      summary: Update deployment phase configuration
      description: >
        Updates the deployment phase or its configuration. Changing the
        phase affects which connectivity modes are available.
      x-permissions: [developer]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/UpdatePhaseConfigRequest'
      responses:
        '200':
          description: Phase config updated
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PhaseConfigResponse'

  /gardener/phase/get-metrics:
    post:
      operationId: GetPhaseMetrics
      summary: Get phase-level operational metrics
      description: Returns operational metrics for the current deployment phase.
      x-permissions: [developer]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetPhaseMetricsRequest'
      responses:
        '200':
          description: Metrics returned
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PhaseMetricsResponse'

  # --- Bond Scenarios ---

  /gardener/bond/enter-together:
    post:
      operationId: EnterScenarioTogether
      summary: Bonded players enter a scenario together
      description: >
        Both players in a seed bond enter the same scenario simultaneously.
        Both must be in the void. Queries lib-seed for bond state.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/EnterTogetherRequest'
      responses:
        '200':
          description: Both players entered scenario
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ScenarioStateResponse'

  /gardener/bond/get-shared-void:
    post:
      operationId: GetSharedVoidState
      summary: Get shared void state for bonded players
      description: >
        Returns the void state visible to both bonded players when they
        share a void instance.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetSharedVoidRequest'
      responses:
        '200':
          description: Shared void state returned
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SharedVoidStateResponse'

components:
  schemas:
    # --- Enums ---

    DeploymentPhase:
      type: string
      description: Current deployment phase controlling scenario connectivity aperture.
      enum: [Alpha, Beta, Release]

    ConnectivityMode:
      type: string
      description: >
        How a scenario instance connects to the game world.
      enum: [Isolated, WorldSlice, Persistent]

    ScenarioCategory:
      type: string
      description: Primary gameplay domain category for a scenario template.
      enum:
        [Combat, Crafting, Social, Trade, Exploration, Magic,
         Survival, Mixed, Narrative, Tutorial]

    PoiType:
      type: string
      description: Presentation type for a point of interest in the void.
      enum: [Visual, Auditory, Environmental, Portal, Social]

    TriggerMode:
      type: string
      description: How a POI is activated by the player.
      enum: [Proximity, Interaction, Prompted, Forced]

    PoiStatus:
      type: string
      description: Lifecycle status of a point of interest.
      enum: [Active, Entered, Declined, Expired]

    ScenarioStatus:
      type: string
      description: Lifecycle status of a scenario instance.
      enum: [Initializing, Active, Completing, Completed, Abandoned]

    TemplateStatus:
      type: string
      description: Lifecycle status of a scenario template.
      enum: [Draft, Active, Deprecated]

    # --- Spatial Types ---

    Vec3:
      type: object
      description: Three-dimensional vector for void space coordinates.
      required: [x, y, z]
      properties:
        x:
          type: number
          format: float
          description: X coordinate.
        y:
          type: number
          format: float
          description: Y coordinate.
        z:
          type: number
          format: float
          description: Z coordinate.

    # --- Void Models ---

    EnterVoidRequest:
      type: object
      required: [accountId, sessionId]
      properties:
        accountId:
          type: string
          format: uuid
          description: The player's account ID.
        sessionId:
          type: string
          format: uuid
          description: The Connect session ID.

    GetVoidStateRequest:
      type: object
      required: [accountId]
      properties:
        accountId:
          type: string
          format: uuid
          description: The player's account ID.

    UpdatePositionRequest:
      type: object
      required: [accountId, position, velocity]
      properties:
        accountId:
          type: string
          format: uuid
          description: The player reporting position.
        position:
          $ref: '#/components/schemas/Vec3'
        velocity:
          $ref: '#/components/schemas/Vec3'

    LeaveVoidRequest:
      type: object
      required: [accountId]
      properties:
        accountId:
          type: string
          format: uuid
          description: The player leaving the void.

    VoidStateResponse:
      type: object
      required: [voidInstanceId, seedId, accountId, position, activePois]
      properties:
        voidInstanceId:
          type: string
          format: uuid
          description: Unique identifier for this void instance.
        seedId:
          type: string
          format: uuid
          description: The player's active guardian seed.
        accountId:
          type: string
          format: uuid
          description: The player's account.
        position:
          $ref: '#/components/schemas/Vec3'
        activePois:
          type: array
          items:
            $ref: '#/components/schemas/PoiSummary'
          description: Currently active points of interest.

    PositionUpdateResponse:
      type: object
      required: [acknowledged]
      properties:
        acknowledged:
          type: boolean
          description: Whether the position update was processed.
        triggeredPois:
          type: array
          items:
            $ref: '#/components/schemas/PoiSummary'
          nullable: true
          description: POIs triggered by this position update.

    LeaveVoidResponse:
      type: object
      required: [accountId, sessionDurationSeconds]
      properties:
        accountId:
          type: string
          format: uuid
          description: The player that left.
        sessionDurationSeconds:
          type: number
          format: float
          description: How long the player was in the void.

    # --- POI Models ---

    PoiSummary:
      type: object
      required: [poiId, position, poiType, triggerMode, status]
      properties:
        poiId:
          type: string
          format: uuid
          description: Unique POI identifier.
        position:
          $ref: '#/components/schemas/Vec3'
        poiType:
          $ref: '#/components/schemas/PoiType'
        triggerMode:
          $ref: '#/components/schemas/TriggerMode'
        triggerRadius:
          type: number
          format: float
          nullable: true
          description: Activation radius for proximity-triggered POIs.
        visualHint:
          type: string
          description: Client-interpreted visual cue identifier.
        audioHint:
          type: string
          nullable: true
          description: Optional audio cue identifier.
        intensityRamp:
          type: number
          format: float
          description: How aggressively this POI draws attention (0.0-1.0).
        status:
          $ref: '#/components/schemas/PoiStatus'

    ListPoisRequest:
      type: object
      required: [accountId]
      properties:
        accountId:
          type: string
          format: uuid
          description: The player whose POIs to list.

    ListPoisResponse:
      type: object
      required: [voidInstanceId, pois]
      properties:
        voidInstanceId:
          type: string
          format: uuid
          description: The void instance.
        pois:
          type: array
          items:
            $ref: '#/components/schemas/PoiSummary'

    InteractWithPoiRequest:
      type: object
      required: [accountId, poiId]
      properties:
        accountId:
          type: string
          format: uuid
          description: The player interacting.
        poiId:
          type: string
          format: uuid
          description: The POI being interacted with.

    PoiInteractionResponse:
      type: object
      required: [poiId, result]
      properties:
        poiId:
          type: string
          format: uuid
          description: The POI.
        result:
          type: string
          description: >
            Interaction result: "scenario_prompt", "scenario_enter",
            "poi_update", or "chain_offer".
        scenarioTemplateId:
          type: string
          format: uuid
          nullable: true
          description: If result involves a scenario, the template ID.
        promptText:
          type: string
          nullable: true
          description: Prompt text if result is scenario_prompt.
        promptChoices:
          type: array
          items:
            type: string
          nullable: true
          description: Prompt choices if applicable.

    DeclinePoiRequest:
      type: object
      required: [accountId, poiId]
      properties:
        accountId:
          type: string
          format: uuid
          description: The player declining.
        poiId:
          type: string
          format: uuid
          description: The POI being declined.

    DeclinePoiResponse:
      type: object
      required: [poiId, acknowledged]
      properties:
        poiId:
          type: string
          format: uuid
        acknowledged:
          type: boolean

    # --- Scenario Models ---

    EnterScenarioRequest:
      type: object
      required: [accountId, scenarioTemplateId]
      properties:
        accountId:
          type: string
          format: uuid
          description: The player entering the scenario.
        scenarioTemplateId:
          type: string
          format: uuid
          description: The template to instantiate.
        poiId:
          type: string
          format: uuid
          nullable: true
          description: The POI that triggered this entry, if applicable.
        promptChoice:
          type: string
          nullable: true
          description: Selected prompt choice, if applicable.

    GetScenarioStateRequest:
      type: object
      required: [accountId]
      properties:
        accountId:
          type: string
          format: uuid

    CompleteScenarioRequest:
      type: object
      required: [accountId, scenarioInstanceId]
      properties:
        accountId:
          type: string
          format: uuid
        scenarioInstanceId:
          type: string
          format: uuid

    AbandonScenarioRequest:
      type: object
      required: [accountId, scenarioInstanceId]
      properties:
        accountId:
          type: string
          format: uuid
        scenarioInstanceId:
          type: string
          format: uuid

    ChainScenarioRequest:
      type: object
      required: [accountId, currentScenarioInstanceId, targetTemplateId]
      properties:
        accountId:
          type: string
          format: uuid
        currentScenarioInstanceId:
          type: string
          format: uuid
        targetTemplateId:
          type: string
          format: uuid

    ScenarioStateResponse:
      type: object
      required:
        [scenarioInstanceId, scenarioTemplateId, gameSessionId,
         connectivityMode, status, createdAt]
      properties:
        scenarioInstanceId:
          type: string
          format: uuid
        scenarioTemplateId:
          type: string
          format: uuid
        gameSessionId:
          type: string
          format: uuid
          description: The Game Session backing this scenario.
        connectivityMode:
          $ref: '#/components/schemas/ConnectivityMode'
        status:
          $ref: '#/components/schemas/ScenarioStatus'
        createdAt:
          type: string
          format: date-time
        chainedFrom:
          type: string
          format: uuid
          nullable: true
        chainDepth:
          type: integer
          default: 0

    ScenarioCompletionResponse:
      type: object
      required: [scenarioInstanceId, growthAwarded, returnToVoid]
      properties:
        scenarioInstanceId:
          type: string
          format: uuid
        growthAwarded:
          type: object
          additionalProperties:
            type: number
            format: float
          description: Map of domain path to growth amount awarded.
        returnToVoid:
          type: boolean

    AbandonScenarioResponse:
      type: object
      required: [scenarioInstanceId, partialGrowthAwarded]
      properties:
        scenarioInstanceId:
          type: string
          format: uuid
        partialGrowthAwarded:
          type: object
          additionalProperties:
            type: number
            format: float

    # --- Template Models ---

    DomainWeight:
      type: object
      required: [domain, weight]
      properties:
        domain:
          type: string
          description: Growth domain path.
        weight:
          type: number
          format: float
          description: Relative weight (0.0-1.0).

    ScenarioPrerequisites:
      type: object
      properties:
        requiredDomains:
          type: object
          additionalProperties:
            type: number
            format: float
          nullable: true
        requiredScenarios:
          type: array
          items:
            type: string
          nullable: true
        excludedScenarios:
          type: array
          items:
            type: string
          nullable: true

    ScenarioChaining:
      type: object
      properties:
        leadsTo:
          type: array
          items:
            type: string
          nullable: true
        chainProbabilities:
          type: object
          additionalProperties:
            type: number
            format: float
          nullable: true
        maxChainDepth:
          type: integer
          default: 3

    ScenarioMultiplayer:
      type: object
      required: [minPlayers, maxPlayers]
      properties:
        minPlayers:
          type: integer
          default: 1
        maxPlayers:
          type: integer
          default: 1
        matchmakingQueueCode:
          type: string
          nullable: true
        bondPreferred:
          type: boolean
          default: false

    ScenarioContent:
      type: object
      properties:
        behaviorDocumentId:
          type: string
          nullable: true
        sceneDocumentId:
          type: string
          format: uuid
          nullable: true
        realmId:
          type: string
          format: uuid
          nullable: true
        locationCode:
          type: string
          nullable: true

    CreateTemplateRequest:
      type: object
      required: [code, displayName, description, category, domainWeights, allowedPhases]
      properties:
        code:
          type: string
          description: Human-readable identifier (e.g., "combat-basics-arena").
        displayName:
          type: string
        description:
          type: string
        category:
          $ref: '#/components/schemas/ScenarioCategory'
        subcategory:
          type: string
          nullable: true
        domainWeights:
          type: array
          items:
            $ref: '#/components/schemas/DomainWeight'
        minGrowthPhase:
          type: string
          nullable: true
          description: Minimum seed growth phase code to be offered this scenario.
        connectivityMode:
          $ref: '#/components/schemas/ConnectivityMode'
          default: Isolated
        allowedPhases:
          type: array
          items:
            $ref: '#/components/schemas/DeploymentPhase'
        maxConcurrentInstances:
          type: integer
          default: 100
        estimatedDurationMinutes:
          type: integer
          nullable: true
        prerequisites:
          $ref: '#/components/schemas/ScenarioPrerequisites'
          nullable: true
        chaining:
          $ref: '#/components/schemas/ScenarioChaining'
          nullable: true
        multiplayer:
          $ref: '#/components/schemas/ScenarioMultiplayer'
          nullable: true
        content:
          $ref: '#/components/schemas/ScenarioContent'
          nullable: true

    GetTemplateRequest:
      type: object
      required: [scenarioTemplateId]
      properties:
        scenarioTemplateId:
          type: string
          format: uuid

    GetTemplateByCodeRequest:
      type: object
      required: [code]
      properties:
        code:
          type: string

    ListTemplatesRequest:
      type: object
      properties:
        category:
          $ref: '#/components/schemas/ScenarioCategory'
          nullable: true
        connectivityMode:
          $ref: '#/components/schemas/ConnectivityMode'
          nullable: true
        deploymentPhase:
          $ref: '#/components/schemas/DeploymentPhase'
          nullable: true
        status:
          $ref: '#/components/schemas/TemplateStatus'
          nullable: true
        page:
          type: integer
          default: 1
        pageSize:
          type: integer
          default: 50

    UpdateTemplateRequest:
      type: object
      required: [scenarioTemplateId]
      properties:
        scenarioTemplateId:
          type: string
          format: uuid
        displayName:
          type: string
          nullable: true
        description:
          type: string
          nullable: true
        domainWeights:
          type: array
          items:
            $ref: '#/components/schemas/DomainWeight'
          nullable: true
        maxConcurrentInstances:
          type: integer
          nullable: true
        prerequisites:
          $ref: '#/components/schemas/ScenarioPrerequisites'
          nullable: true
        chaining:
          $ref: '#/components/schemas/ScenarioChaining'
          nullable: true
        multiplayer:
          $ref: '#/components/schemas/ScenarioMultiplayer'
          nullable: true
        content:
          $ref: '#/components/schemas/ScenarioContent'
          nullable: true

    DeprecateTemplateRequest:
      type: object
      required: [scenarioTemplateId]
      properties:
        scenarioTemplateId:
          type: string
          format: uuid

    ListTemplatesResponse:
      type: object
      required: [templates, totalCount, page, pageSize]
      properties:
        templates:
          type: array
          items:
            $ref: '#/components/schemas/ScenarioTemplateResponse'
        totalCount:
          type: integer
        page:
          type: integer
        pageSize:
          type: integer

    ScenarioTemplateResponse:
      type: object
      required:
        [scenarioTemplateId, code, displayName, description, category,
         domainWeights, connectivityMode, allowedPhases,
         maxConcurrentInstances, status, createdAt, updatedAt]
      properties:
        scenarioTemplateId:
          type: string
          format: uuid
        code:
          type: string
        displayName:
          type: string
        description:
          type: string
        category:
          $ref: '#/components/schemas/ScenarioCategory'
        subcategory:
          type: string
          nullable: true
        domainWeights:
          type: array
          items:
            $ref: '#/components/schemas/DomainWeight'
        minGrowthPhase:
          type: string
          nullable: true
        connectivityMode:
          $ref: '#/components/schemas/ConnectivityMode'
        allowedPhases:
          type: array
          items:
            $ref: '#/components/schemas/DeploymentPhase'
        maxConcurrentInstances:
          type: integer
        estimatedDurationMinutes:
          type: integer
          nullable: true
        prerequisites:
          $ref: '#/components/schemas/ScenarioPrerequisites'
          nullable: true
        chaining:
          $ref: '#/components/schemas/ScenarioChaining'
          nullable: true
        multiplayer:
          $ref: '#/components/schemas/ScenarioMultiplayer'
          nullable: true
        content:
          $ref: '#/components/schemas/ScenarioContent'
          nullable: true
        status:
          $ref: '#/components/schemas/TemplateStatus'
        createdAt:
          type: string
          format: date-time
        updatedAt:
          type: string
          format: date-time

    # --- Phase Models ---

    GetPhaseConfigRequest:
      type: object
      description: Empty request body.

    UpdatePhaseConfigRequest:
      type: object
      properties:
        currentPhase:
          $ref: '#/components/schemas/DeploymentPhase'
          nullable: true
        maxConcurrentScenariosGlobal:
          type: integer
          nullable: true
        persistentEntryEnabled:
          type: boolean
          nullable: true
        voidMinigamesEnabled:
          type: boolean
          nullable: true

    PhaseConfigResponse:
      type: object
      required: [currentPhase, maxConcurrentScenariosGlobal, persistentEntryEnabled, voidMinigamesEnabled]
      properties:
        currentPhase:
          $ref: '#/components/schemas/DeploymentPhase'
        maxConcurrentScenariosGlobal:
          type: integer
        persistentEntryEnabled:
          type: boolean
        voidMinigamesEnabled:
          type: boolean

    GetPhaseMetricsRequest:
      type: object
      description: Empty request body.

    PhaseMetricsResponse:
      type: object
      required: [currentPhase, activeVoidInstances, activeScenarioInstances, scenarioCapacityUtilization]
      properties:
        currentPhase:
          $ref: '#/components/schemas/DeploymentPhase'
        activeVoidInstances:
          type: integer
        activeScenarioInstances:
          type: integer
        scenarioCapacityUtilization:
          type: number
          format: float
          description: Percentage of global capacity in use (0.0-1.0).

    # --- Bond Scenario Models ---

    EnterTogetherRequest:
      type: object
      required: [bondId, scenarioTemplateId]
      properties:
        bondId:
          type: string
          format: uuid
          description: The seed bond linking both players.
        scenarioTemplateId:
          type: string
          format: uuid

    GetSharedVoidRequest:
      type: object
      required: [bondId]
      properties:
        bondId:
          type: string
          format: uuid

    SharedVoidStateResponse:
      type: object
      required: [bondId, participants, sharedPois]
      properties:
        bondId:
          type: string
          format: uuid
        participants:
          type: array
          items:
            $ref: '#/components/schemas/BondedPlayerVoidState'
        sharedPois:
          type: array
          items:
            $ref: '#/components/schemas/PoiSummary'

    BondedPlayerVoidState:
      type: object
      required: [seedId, accountId, position, inVoid]
      properties:
        seedId:
          type: string
          format: uuid
        accountId:
          type: string
          format: uuid
        position:
          $ref: '#/components/schemas/Vec3'
        inVoid:
          type: boolean
          description: Whether this player is currently in the void.
```

---

## Open Design Questions

1. **Seed bond permanence**: Should bond permanence be a property of the seed type definition (guardian bonds are permanent, dungeon-master bonds are dissolvable)? Recommendation: Yes, add a `bondPermanent` boolean to `SeedTypeDefinition`.

2. **Growth decay**: Should unused domains decay over time? Arguments for: prevents seeds from being "good at everything" after enough time, encourages specialization. Arguments against: punishes players who take breaks. Recommendation: Off by default, configurable per seed type definition.

3. **Cross-seed growth sharing**: How much growth crosses between seeds of the same owner? Full sharing means a combat-heavy guardian seed immediately gives a dungeon master seed combat capability. Recommendation: Configurable multiplier per seed type pair (default 0.0 -- no cross-pollination unless explicitly configured).

4. **Void position protocol**: The UpdatePosition endpoint will be called frequently. Should this use the standard POST JSON pattern or a lighter-weight binary protocol through Connect? Recommendation: Start with POST JSON; optimize to binary only if latency/throughput becomes a problem.

5. **Growth phase as convenience vs. gate**: Phases should never be hard gates, but Gardener uses them for scenario selection (`minGrowthPhase`). Is this a soft filter (strong bias) or a hard filter (absolute requirement)? Recommendation: Soft filter with configurable strictness.

6. **Seed type registration timing**: Should seed types be registered via API at runtime, or seeded from configuration on startup? Recommendation: Both -- configuration for built-in types, API for dynamic types added later.

7. **Cross-seed-type growth transfer**: When an entity holds multiple seed types (e.g., a character with both `guardian` and `dungeon_master` seeds), should combat experience earned in the guardian context partially feed the dungeon_master seed's command domains? This would require a configurable transfer matrix per seed-type pair. Recommendation: Start with no cross-type transfer (each seed grows independently from its own events), add transfer matrices only when validated by gameplay testing. The dungeon-as-actor doc also raises this question for `dungeon_master` -> `guardian` transfer.

8. **Gardener orchestration extraction trigger**: At what point would shared orchestration patterns across seed consumers justify extracting a generic "growth orchestration" layer at L2? Current position: not until at least three consumers demonstrate genuinely shared orchestration logic (not just shared seed CRUD, which is already at L2). The abstraction should be extracted from working code, not pre-designed.
