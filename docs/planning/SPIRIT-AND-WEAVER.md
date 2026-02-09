# Planning: Spirit Service & Weaver Service

> **Status**: Draft
> **Created**: 2026-02-09
> **Related**: [PLAYER-VISION.md](../reference/PLAYER-VISION.md), [VISION.md](../reference/VISION.md), [SERVICE-HIERARCHY.md](../reference/SERVICE-HIERARCHY.md)

This document outlines the design and schemas for two new services that implement the progressive player agency model described in PLAYER-VISION.md.

---

## Table of Contents

1. [Overview](#overview)
2. [Spirit Service (L2 Game Foundation)](#spirit-service)
3. [Weaver Service (L4 Game Features)](#weaver-service)
4. [Integration Points](#integration-points)
5. [Extensions to Existing Services](#extensions-to-existing-services)
6. [State Stores](#state-stores)
7. [Schema Drafts](#schema-drafts)

---

## Overview

Two new services implement the player experience vision:

| Service | Layer | Role | Analogous To |
|---------|-------|------|-------------|
| **Spirit** | L2 Game Foundation | Guardian spirit entity management, seeds, experience accumulation, UX capability manifests | Character (foundational game entity) |
| **Weaver** | L4 Game Features | Void orchestration, scenario routing, progressive discovery, player-side experience coordination | Puppetmaster (dynamic orchestration) |

**Design principle**: Puppetmaster orchestrates what NPCs experience. Weaver orchestrates what players experience. Spirit is the foundational entity that both systems reference.

### Dependency Graph

```
                    Spirit (L2)
                   /     |      \
                  /      |       \
          Character  Game Session  Relationship
          (L2)       (L2)          (L2)
                         |
                         |
                    Weaver (L4)
                   /   |   |   \
                  /    |   |    \
          Spirit  Analytics  Puppetmaster  Behavior
          (L2)    (L4)       (L4)          (L4)
                    \         |           /
                     \        |          /
                      Asset  Game Session  Matchmaking
                      (L3)   (L2)          (L4)
```

---

## Spirit Service

### Identity

- **Service name**: `spirit`
- **Layer**: L2 Game Foundation
- **Lifetime**: Scoped
- **Plugin**: `plugins/lib-spirit/`

### Why L2

The guardian spirit is a foundational game entity. Every player has exactly one spirit per account. Characters are bound to spirits. Seeds are owned by spirits. Game Sessions reference the active seed. The spirit is as fundamental to the game model as Character or Realm -- without it, nothing above it in the stack makes sense.

### Core Concepts

#### The Spirit Entity

One spirit per account. Created on first game-world entry (not on account creation -- the spirit awakens when the player first enters the void). Persists for the lifetime of the account.

```
Spirit
├── SpiritId (Guid)
├── AccountId (Guid) -- 1:1 relationship
├── CreatedAt (DateTime)
├── Phase (SpiritPhase enum: Nascent, Awakening, Aware, Attuned)
├── TotalExperience (float) -- aggregate across all domains
├── ExperienceDomains (map<string, float>) -- domain path → depth
├── PairBondId (Guid?) -- null if unpaired
└── Seeds[] (up to 3)
```

#### Spirit Phase

Not a hard gate, but a broad classification of the spirit's overall development. Used by Weaver for scenario routing and by the client for ambient presentation (visual complexity of the spirit entity in the void, for instance).

| Phase | Meaning | Approximate Trigger |
|-------|---------|-------------------|
| **Nascent** | Just created. First void entry. No scenario experience. | Spirit creation |
| **Awakening** | Has experienced a few scenarios. Beginning to show preferences. | First few scenario completions |
| **Aware** | Has meaningful experience in at least one domain. Can sustain a household. | Sufficient depth in any domain to unlock basic UX modules |
| **Attuned** | Deep experience across multiple domains. Full agency potential. | Sustained multi-domain engagement |

Phase transitions are computed, not configured -- the Spirit service evaluates experience domains and total experience to determine the current phase. No explicit "level up" moment.

#### Experience Domains

A flat key-value map where keys are dot-separated domain paths and values are floating-point depth scores (0.0 to unbounded, though practical ranges are 0.0 to ~100.0 for top-level domains).

Top-level domains (always tracked):

| Domain | What It Represents |
|--------|--------------------|
| `combat` | Martial engagement, fighting, tactical decisions |
| `crafting` | Material transformation, creation, repair |
| `social` | Conversation, negotiation, relationship building |
| `trade` | Economic activity, buying, selling, market analysis |
| `exploration` | Movement, discovery, navigation, danger assessment |
| `magic` | Pneuma manipulation, spellcasting, enchantment |
| `stewardship` | Household management, generational planning, resource allocation |
| `survival` | Sustenance, shelter, environmental adaptation |

Sub-domains emerge as depth increases:

```
combat              → 5.0
combat.melee        → 3.2
combat.melee.sword  → 1.8
combat.ranged       → 1.1
combat.tactical     → 2.4
crafting            → 8.0
crafting.smithing   → 6.5
crafting.alchemy    → 1.2
```

Sub-domain keys are not predefined in schema -- they're published by L4 services via events (same pattern as Resource reference tracking). The Spirit service stores whatever domain keys it receives. New game mechanics can introduce new sub-domains without schema changes.

#### Seeds

Up to 3 per spirit. Each seed is a distinct relationship to the game world.

```
Seed
├── SeedId (Guid)
├── SpiritId (Guid)
├── SeedType (SeedType enum: Guardian, DungeonMaster, Variant)
├── RealmId (Guid?) -- which realm this seed is bound to (null if unbound)
├── DisplayName (string) -- player-chosen or auto-generated
├── CreatedAt (DateTime)
├── LastActiveAt (DateTime)
├── Status (SeedStatus enum: Active, Dormant, Archived)
├── HouseholdId (Guid?) -- links to character household (for Guardian type)
├── EntityId (Guid?) -- links to dungeon core / other entity (for non-Guardian types)
└── Metadata (map<string, object>) -- seed-type-specific data
```

#### UX Capability Manifest

Computed from experience domains. Pushed to the client via Connect's per-session RabbitMQ queue whenever it changes. The manifest is a structured document:

```
UxCapabilityManifest
├── SpiritId (Guid)
├── ComputedAt (DateTime)
├── Version (int) -- increments on recomputation
└── Modules[]
    ├── ModuleId (string) -- e.g., "combat.stance", "crafting.material_selection"
    ├── Domain (string) -- parent domain key
    ├── Fidelity (float) -- 0.0 to 1.0, how much control the spirit has
    ├── Unlocked (bool) -- whether the module is available at all
    └── Metadata (map<string, object>) -- module-specific configuration
```

The computation rules (which experience thresholds unlock which modules at what fidelity) are driven by configuration, not hardcoded. This allows tuning without code changes and enables different rules per realm or seed type.

#### Pair Bonds

One pair bond per spirit, maximum. Permanent and unbreakable (by design -- the bond is metaphysical, not social).

```
PairBond
├── PairBondId (Guid)
├── SpiritAlphaId (Guid) -- first spirit in the pair
├── SpiritBetaId (Guid) -- second spirit in the pair
├── CreatedAt (DateTime)
├── BondStrength (float) -- grows with shared experience
└── SharedExperience (float) -- total co-present play time / shared scenarios
```

Pair bonds are initiated during a special void scenario (the pair discovery sequence) and are confirmed by both spirits explicitly. Once formed, the bond is permanent for the life of both accounts.

### API Endpoints

```
POST /spirit/create              -- Create spirit for account (first game entry)
POST /spirit/get                 -- Get spirit by ID
POST /spirit/get-by-account      -- Get spirit by account ID
POST /spirit/get-experience      -- Get full experience domain map
POST /spirit/record-experience   -- Record experience gain (internal, from L4 services)
POST /spirit/get-phase           -- Get current spirit phase
POST /spirit/get-capability-manifest -- Get current UX capability manifest

POST /spirit/seed/create         -- Create a new seed (up to 3)
POST /spirit/seed/get            -- Get seed by ID
POST /spirit/seed/list           -- List seeds for spirit
POST /spirit/seed/activate       -- Set a seed as the active session seed
POST /spirit/seed/archive        -- Archive a seed (soft delete, preserves data)

POST /spirit/pair/initiate       -- Begin pair bond process
POST /spirit/pair/confirm        -- Confirm pair bond (both spirits must confirm)
POST /spirit/pair/get            -- Get pair bond for spirit
POST /spirit/pair/get-partner    -- Get partner spirit's public info
```

### Events

```yaml
# Published by Spirit
spirit.created                    -- New spirit awakened
spirit.phase.changed              -- Spirit transitioned phases
spirit.experience.updated         -- Experience domain values changed
spirit.capability.updated         -- UX capability manifest recomputed
spirit.seed.created               -- New seed created
spirit.seed.activated             -- Seed set as active
spirit.seed.archived              -- Seed archived
spirit.pair.bonded                -- Pair bond formed

# Consumed by Spirit (from L4 services)
spirit.experience.contributed     -- L4 service reports experience gain
                                     (Character Personality, Character History,
                                      Character Encounter, Analytics, etc.)
```

The `spirit.experience.contributed` event follows the same pattern as Resource's `resource.reference.registered` -- Spirit defines the event schema in its own events YAML, and L4 services publish to it. Spirit never depends on L4; L4 depends on Spirit's event contract.

```
SpiritExperienceContributedEvent
├── SpiritId (Guid)
├── Domain (string) -- e.g., "combat.melee.sword"
├── Amount (float) -- how much experience to add
├── Source (string) -- what generated this (e.g., "character-encounter", "analytics")
├── SourceEventId (Guid?) -- optional reference to the originating event
└── Context (map<string, string>) -- additional context for auditing
```

### Configuration

```yaml
SpiritServiceConfiguration:
  MaxSeedsPerSpirit: 3
  PhaseThresholds:
    Awakening: 5.0        # total experience to transition from Nascent
    Aware: 25.0           # total experience for Aware
    Attuned: 100.0        # total experience for Attuned
  CapabilityRecomputeDebounceMs: 5000  # debounce recomputation on rapid experience gains
  ExperienceDecayEnabled: false        # whether unused domains decay over time
  ExperienceDecayRatePerDay: 0.01      # if enabled, daily decay rate for inactive domains
  PairBondEnabled: true                # feature flag for pair system
  PairBondSharedExperienceMultiplier: 1.5  # bonus when spirits experience things together
```

---

## Weaver Service

### Identity

- **Service name**: `weaver`
- **Layer**: L4 Game Features
- **Lifetime**: Scoped
- **Plugin**: `plugins/lib-weaver/`

### Why L4

Weaver needs to read from nearly every layer: Spirit and Game Session (L2), Asset (L3), Analytics, Behavior, Puppetmaster, Matchmaking (L4). It orchestrates the full player experience by composing content from across the service hierarchy. Classic L4 -- optional, feature-rich, maximum connectivity.

### Core Concepts

#### Void Instances

Each spirit in the void gets a dedicated void instance -- a lightweight server-side representation of their personal void space.

```
VoidInstance
├── VoidInstanceId (Guid)
├── SpiritId (Guid)
├── SessionId (Guid) -- Connect session ID
├── CreatedAt (DateTime)
├── Position (Vec3) -- spirit's current position in void space
├── Velocity (Vec3) -- current movement vector
├── ActivePois[] (list of active point-of-interest IDs)
├── Phase (DeploymentPhase enum: Alpha, Beta, Release)
├── ScenarioHistory[] -- recently visited scenario template IDs
└── DriftMetrics
    ├── TotalDistance (float)
    ├── DirectionalBias (Vec3) -- normalized average direction
    ├── HesitationCount (int) -- times the spirit stopped/reversed
    └── EngagementPattern (string) -- computed pattern label
```

Void instances are ephemeral (Redis-backed). They exist only while the spirit is in the void and are destroyed when the spirit enters a scenario or disconnects.

#### Points of Interest (POIs)

Spawned dynamically by the Weaver based on spirit metadata, movement patterns, and available content.

```
PointOfInterest
├── PoiId (Guid)
├── VoidInstanceId (Guid)
├── Position (Vec3) -- position in the void relative to spirit
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

Registered definitions of playable experiences. These are the building blocks that Weaver selects from when populating a spirit's void.

```
ScenarioTemplate
├── ScenarioTemplateId (Guid)
├── Code (string) -- human-readable identifier (e.g., "combat-basics-arena")
├── DisplayName (string)
├── Description (string)
├── Category (ScenarioCategory enum: Combat, Crafting, Social, Trade,
│     Exploration, Magic, Survival, Mixed, Narrative, Tutorial)
├── Subcategory (string?) -- finer classification within category
├── DomainWeights (map<string, float>) -- which experience domains this teaches
├── MinSpiritPhase (SpiritPhase) -- minimum phase to be offered this scenario
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
│   └── PairBondPreferred (bool) -- prioritize paired spirits for this scenario
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

Running instances of a scenario template, created when a spirit (or group) enters a scenario.

```
ScenarioInstance
├── ScenarioInstanceId (Guid)
├── ScenarioTemplateId (Guid)
├── GameSessionId (Guid) -- mapped to a Game Session for full service stack access
├── Participants[]
│   ├── SpiritId (Guid)
│   ├── SessionId (Guid)
│   ├── JoinedAt (DateTime)
│   └── Role (string) -- participant role within the scenario
├── ConnectivityMode (ConnectivityMode) -- inherited from template
├── Status (ScenarioStatus enum: Initializing, Active, Completing, Completed, Abandoned)
├── CreatedAt (DateTime)
├── CompletedAt (DateTime?)
├── ExperienceAwarded (map<string, float>) -- domains and amounts awarded on completion
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

The Weaver's core intelligence is selecting which POIs to spawn for a given spirit. This is configuration-driven, not hardcoded, but the general algorithm:

1. **Query eligible templates**: Filter by spirit phase, deployment phase, prerequisites, and cooldowns (don't re-offer recently declined/completed scenarios)
2. **Score by affinity**: Weight templates by how well their `DomainWeights` align with the spirit's experience profile. High-experience spirits get scenarios that deepen existing domains; low-experience spirits get diverse breadth scenarios.
3. **Apply diversity pressure**: Avoid offering the same category repeatedly. If the last 3 POIs were combat, bias toward other categories.
4. **Apply narrative pressure**: If the spirit's drift metrics suggest a pattern (e.g., always moving in one direction, high hesitation indicating uncertainty), offer scenarios that respond to that pattern.
5. **Apply capacity constraints**: Don't offer scenarios that are at max concurrent instances.
6. **Select presentation**: Choose POI type and trigger mode based on the scenario's nature and the spirit's demonstrated response patterns (spirits that avoid prompted POIs get more proximity-triggered ones).

This algorithm runs periodically (configurable interval, maybe every 5-10 seconds) for each active void instance.

### API Endpoints

```
# Void Management
POST /weaver/void/enter              -- Spirit enters the void (creates void instance)
POST /weaver/void/get                -- Get current void instance state
POST /weaver/void/update-position    -- Report spirit position/velocity (high frequency)
POST /weaver/void/leave              -- Spirit leaves the void (cleanup)

# POI Interaction
POST /weaver/poi/list                -- List active POIs for current void instance
POST /weaver/poi/interact            -- Spirit interacts with a POI
POST /weaver/poi/decline             -- Spirit explicitly declines a prompted POI

# Scenario Lifecycle
POST /weaver/scenario/enter          -- Enter a scenario (from POI or chain)
POST /weaver/scenario/get            -- Get current scenario state
POST /weaver/scenario/complete       -- Mark scenario as completed
POST /weaver/scenario/abandon        -- Abandon scenario, return to void
POST /weaver/scenario/chain          -- Enter a chained scenario from within current

# Scenario Template Management (admin/developer)
POST /weaver/template/create         -- Register a new scenario template
POST /weaver/template/get            -- Get template by ID
POST /weaver/template/get-by-code    -- Get template by code
POST /weaver/template/list           -- List templates with filtering
POST /weaver/template/update         -- Update template
POST /weaver/template/deprecate      -- Deprecate template

# Deployment Phase (admin)
POST /weaver/phase/get               -- Get current deployment phase config
POST /weaver/phase/update            -- Update deployment phase
POST /weaver/phase/get-metrics       -- Get phase-level metrics (active instances, etc.)

# Pair Scenarios (specialized)
POST /weaver/pair/enter-together     -- Both paired spirits enter a scenario together
POST /weaver/pair/get-shared-void    -- Get shared void state for paired spirits
```

### Events

```yaml
# Published by Weaver
weaver.void.entered                -- Spirit entered the void
weaver.void.left                   -- Spirit left the void
weaver.poi.spawned                 -- POI spawned for a spirit
weaver.poi.entered                 -- Spirit entered a POI / triggered a scenario
weaver.poi.declined                -- Spirit declined a POI
weaver.poi.expired                 -- POI expired without interaction
weaver.scenario.started            -- Scenario instance created
weaver.scenario.completed          -- Scenario completed
weaver.scenario.abandoned          -- Scenario abandoned
weaver.scenario.chained            -- Spirit chained from one scenario to another
weaver.pair.entered-together       -- Paired spirits entered a scenario together
weaver.phase.changed               -- Deployment phase changed

# Consumed by Weaver
spirit.phase.changed               -- Adjust scenario offerings based on new phase
spirit.experience.updated          -- Recalculate scenario scoring
spirit.pair.bonded                 -- Enable pair-specific scenarios
spirit.seed.activated              -- Track which seed is active for scenario context
game-session.ended                 -- Scenario instance cleanup
analytics.milestone.reached        -- Inform scenario selection
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
- Detect abandoned scenarios (spirit disconnected, timeout exceeded)
- Award experience on completion (publish `spirit.experience.contributed` events)
- Clean up completed/abandoned instances
- Manage Game Session lifecycle for scenario instances

### Configuration

```yaml
WeaverServiceConfiguration:
  # Void orchestration
  VoidTickIntervalMs: 5000           # How often to evaluate void instances
  MaxActivePoisPerVoid: 8            # Max concurrent POIs per spirit
  PoiDefaultTtlMinutes: 10          # How long a POI lives before expiring
  PoiSpawnRadiusMin: 50.0            # Minimum distance from spirit to spawn POI
  PoiSpawnRadiusMax: 200.0           # Maximum distance from spirit to spawn POI
  MinPoiSpacing: 30.0                # Minimum distance between POIs

  # Scenario selection
  AffinityWeight: 0.4                # Weight for domain affinity in scoring
  DiversityWeight: 0.3               # Weight for category diversity
  NarrativeWeight: 0.2               # Weight for drift-pattern narrative response
  RandomWeight: 0.1                  # Weight for randomness / discovery
  RecentScenarioCooldownMinutes: 30  # Don't re-offer recently completed scenarios

  # Scenario instances
  MaxConcurrentScenariosGlobal: 1000 # Total across all spirits
  ScenarioTimeoutMinutes: 60         # Max duration before forced completion
  AbandonDetectionMinutes: 5         # Time without input before marking abandoned
  ExperienceAwardMultiplier: 1.0     # Global tuning knob for experience gains

  # Pair features
  PairSharedVoidEnabled: true        # Whether pairs share a void instance
  PairScenarioPriority: 1.5          # Scoring boost for pair-friendly scenarios

  # Phase defaults
  DefaultPhase: Alpha                # Starting deployment phase
```

---

## Integration Points

### Spirit ← L4 Experience Contributions

L4 services publish experience contributions to the Spirit service's own event topic. This is the same architectural pattern as Resource's reference tracking.

| L4 Service | Domain Contributions | Trigger |
|------------|---------------------|---------|
| **Character Personality** | `combat.*`, `social.*` based on trait evolution | Personality trait shift events |
| **Character History** | `exploration.*`, `survival.*`, `stewardship.*` based on historical participation | History entry creation |
| **Character Encounter** | `social.*`, `combat.*` based on encounter type | Encounter resolution |
| **Analytics** | Any domain, based on aggregated statistics | Milestone events |
| **Behavior** | `magic.*`, `crafting.*` based on ABML execution context | Actor action completion |
| **Quest** | Domain matching quest category | Quest completion |

Each L4 service knows which domains its data contributes to and publishes `spirit.experience.contributed` with the appropriate domain key and amount. Spirit doesn't need to know about any L4 service -- it just processes incoming experience events.

### Weaver → Game Session

When a spirit enters a scenario, Weaver creates a Game Session (matchmade type) with a reservation token. The full service stack becomes available to the scenario instance through the Game Session. When the scenario ends, the Game Session is closed.

For WorldSlice and Persistent connectivity modes, the Game Session connects to actual realm state. For Isolated mode, the Game Session spins up a sandboxed instance.

### Weaver → Puppetmaster

For scenarios that involve NPCs (most of them), Weaver delegates NPC orchestration to Puppetmaster. Weaver decides WHAT scenario to run; Puppetmaster decides HOW the NPCs in that scenario behave. Clean separation:

- Weaver: "Run the 'village market' scenario for this spirit"
- Puppetmaster: "Load the market NPC behaviors, coordinate the Event Brain for any interactions"

### Weaver → Matchmaking

For group scenarios, Weaver can submit matchmaking tickets to group spirits into shared scenario instances. Matchmaking handles the queuing and acceptance flow; Weaver handles the scenario lifecycle once matched.

### Spirit → Connect (UX Capability Distribution)

When the Spirit service recomputes a UX capability manifest, it publishes `spirit.capability.updated`. The Connect service (or a consumer registered on Connect's behalf) picks this up and pushes the new manifest to the client via the existing per-session RabbitMQ subscription channel -- the same mechanism used for permission capability updates.

### Spirit → Relationship (Pair Bonds)

Pair bonds can be modeled through the existing Relationship service using account-level polymorphic entity types. The Spirit service manages the game-level pair bond semantics (bond strength, shared experience tracking), while Relationship stores the structural bond record.

Alternatively, Spirit can own pair bonds entirely since they're tightly coupled to spirit mechanics. This avoids extending Relationship's entity type system. Design decision to be made during implementation.

---

## Extensions to Existing Services

These are small changes needed in existing services to support the Spirit/Weaver system.

### Connect

- **Pair communication channel**: When a pair bond exists, establish a shared RabbitMQ subscription that both spirits can publish to and receive from. This is a dedicated channel, not routed through any service API -- it's spirit-to-spirit, using Connect as the transport layer.
- **UX capability manifest push**: Subscribe to `spirit.capability.updated` events and push manifest updates to the appropriate client session. Same pattern as existing permission capability pushes.

### Relationship (if pair bonds go here)

- **Account-level entity type**: Add `Account` or `Spirit` as a valid entity type for relationships. Currently supports Character and potentially other game entities.
- **Pair bond relationship type**: A new relationship type with special semantics (permanent, unbreakable, 1:1 limit per spirit).

### Analytics

- **Spirit experience contribution**: After processing aggregated statistics, publish `spirit.experience.contributed` events to feed the spirit's experience accumulation. This is a new event publication, not a new subscription -- Analytics already ingests the raw data.

### Permission

- No changes required. UX capabilities are a separate axis from endpoint permissions, owned by Spirit, not Permission.

### Game Session

- **Weaver-managed sessions**: A new session source type (alongside lobby and matchmade) for Weaver-created scenario instances. Minimal change -- Game Session already supports multiple creation patterns.

---

## State Stores

### Spirit Service

| Store Name | Backend | Purpose |
|------------|---------|---------|
| `spirit-metadata` | MySQL | Spirit entity records (durable, queryable by account ID) |
| `spirit-seeds` | MySQL | Seed entity records (durable, queryable by spirit ID) |
| `spirit-experience` | MySQL | Experience domain records (durable, queryable for analytics) |
| `spirit-capabilities` | Redis | Computed UX capability manifests (cached, frequently read) |
| `spirit-pair-bonds` | MySQL | Pair bond records (durable) |

### Weaver Service

| Store Name | Backend | Purpose |
|------------|---------|---------|
| `weaver-void-instances` | Redis | Active void instance state (ephemeral, per-session) |
| `weaver-pois` | Redis | Active POIs per void instance (ephemeral) |
| `weaver-scenario-templates` | MySQL | Registered scenario template definitions (durable, queryable) |
| `weaver-scenario-instances` | Redis | Active scenario instance state (ephemeral) |
| `weaver-scenario-history` | MySQL | Completed scenario records (durable, for analytics and cooldown tracking) |
| `weaver-phase-config` | MySQL | Deployment phase configuration (durable, admin-managed) |

---

## Schema Drafts

### spirit-api.yaml

```yaml
openapi: 3.0.0
info:
  title: Spirit Service API
  version: 1.0.0
  description: >
    Guardian spirit management service (L2 GameFoundation) for player-world
    relationships. Manages spirit identity, seeds (up to 3 per account),
    progressive experience accumulation across gameplay domains, UX capability
    manifest computation, and pair bonds between twin spirits. One spirit per
    account, created on first game-world entry. Experience domains are
    contributed by L4 services via events, following the same pattern as
    Resource reference tracking. The spirit is the foundational entity that
    connects a player account to the game world.
x-service-layer: GameFoundation

servers:
  - url: http://localhost:5012

paths:
  /spirit/create:
    post:
      operationId: CreateSpirit
      summary: Create a guardian spirit for an account
      description: >
        Creates a new guardian spirit bound to the specified account. Called on
        first game-world entry (not on account creation). Returns conflict if
        spirit already exists for this account. Spirit begins in Nascent phase
        with empty experience domains.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CreateSpiritRequest'
      responses:
        '200':
          description: Spirit created successfully
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SpiritResponse'

  /spirit/get:
    post:
      operationId: GetSpirit
      summary: Get spirit by ID
      description: Returns the spirit entity with current phase and summary experience data.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetSpiritRequest'
      responses:
        '200':
          description: Spirit found
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SpiritResponse'

  /spirit/get-by-account:
    post:
      operationId: GetSpiritByAccount
      summary: Get spirit by account ID
      description: >
        Looks up the spirit bound to the specified account. Returns not found
        if the account has not yet entered the game world.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetSpiritByAccountRequest'
      responses:
        '200':
          description: Spirit found
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SpiritResponse'

  /spirit/get-experience:
    post:
      operationId: GetExperience
      summary: Get full experience domain map
      description: >
        Returns the complete experience domain map for a spirit, including all
        top-level and sub-domain entries with their current depth values.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetExperienceRequest'
      responses:
        '200':
          description: Experience data returned
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ExperienceResponse'

  /spirit/record-experience:
    post:
      operationId: RecordExperience
      summary: Record experience gain for a spirit
      description: >
        Records experience in a specific domain for a spirit. Primarily called
        internally by L4 services after processing game events. Triggers UX
        capability manifest recomputation if thresholds are crossed.
      x-permissions: [internal]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/RecordExperienceRequest'
      responses:
        '200':
          description: Experience recorded
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ExperienceResponse'

  /spirit/get-capability-manifest:
    post:
      operationId: GetCapabilityManifest
      summary: Get current UX capability manifest
      description: >
        Returns the most recently computed UX capability manifest for the spirit.
        This manifest describes which interaction modules are available to the
        client and at what fidelity level.
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
                $ref: '#/components/schemas/UxCapabilityManifestResponse'

  /spirit/seed/create:
    post:
      operationId: CreateSeed
      summary: Create a new seed for a spirit
      description: >
        Creates a new seed (up to 3 per spirit). Each seed represents a distinct
        relationship to the game world. Returns conflict if the spirit already
        has the maximum number of seeds.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CreateSeedRequest'
      responses:
        '200':
          description: Seed created
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SeedResponse'

  /spirit/seed/get:
    post:
      operationId: GetSeed
      summary: Get seed by ID
      description: Returns the seed entity with current status and metadata.
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

  /spirit/seed/list:
    post:
      operationId: ListSeeds
      summary: List seeds for a spirit
      description: Returns all seeds (active, dormant, and optionally archived) for the specified spirit.
      x-permissions: [authenticated]
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

  /spirit/seed/activate:
    post:
      operationId: ActivateSeed
      summary: Set a seed as the active session seed
      description: >
        Activates the specified seed for the current session. Only one seed can
        be active at a time. Deactivates any previously active seed.
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

  /spirit/seed/archive:
    post:
      operationId: ArchiveSeed
      summary: Archive a seed
      description: >
        Soft-deletes a seed, preserving its data but removing it from active
        rotation. Archived seeds do not count toward the maximum seed limit.
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

  /spirit/pair/initiate:
    post:
      operationId: InitiatePairBond
      summary: Begin pair bond process
      description: >
        Initiates a pair bond between two spirits. Both spirits must confirm
        the bond for it to become active. Returns conflict if either spirit
        already has a pair bond.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/InitiatePairBondRequest'
      responses:
        '200':
          description: Pair bond initiated, awaiting confirmation
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PairBondResponse'

  /spirit/pair/confirm:
    post:
      operationId: ConfirmPairBond
      summary: Confirm pair bond
      description: >
        Confirms a pending pair bond. When both spirits have confirmed, the
        bond becomes active and permanent. Cannot be undone.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ConfirmPairBondRequest'
      responses:
        '200':
          description: Pair bond confirmed
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PairBondResponse'

  /spirit/pair/get:
    post:
      operationId: GetPairBond
      summary: Get pair bond for spirit
      description: Returns the pair bond record for the specified spirit, or not found if unpaired.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetPairBondRequest'
      responses:
        '200':
          description: Pair bond found
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PairBondResponse'

  /spirit/pair/get-partner:
    post:
      operationId: GetPairPartner
      summary: Get partner spirit public info
      description: >
        Returns public information about the paired spirit's partner, including
        phase, active seed, and bond strength. Used for pair communication
        and UI display.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetPairPartnerRequest'
      responses:
        '200':
          description: Partner info returned
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PairPartnerResponse'

components:
  schemas:
    # --- Enums ---

    SpiritPhase:
      type: string
      description: >
        Broad classification of the spirit's overall development stage.
        Computed from experience domains, not manually set.
      enum: [Nascent, Awakening, Aware, Attuned]

    SeedType:
      type: string
      description: The fundamental mode of engagement this seed represents.
      enum: [Guardian, DungeonMaster, Variant]

    SeedStatus:
      type: string
      description: Lifecycle status of a seed.
      enum: [Active, Dormant, Archived]

    PairBondStatus:
      type: string
      description: Lifecycle status of a pair bond.
      enum: [PendingConfirmation, Active]

    # --- Request Models ---

    CreateSpiritRequest:
      type: object
      required: [accountId]
      properties:
        accountId:
          type: string
          format: uuid
          description: The account to bind this spirit to.

    GetSpiritRequest:
      type: object
      required: [spiritId]
      properties:
        spiritId:
          type: string
          format: uuid
          description: The spirit to retrieve.

    GetSpiritByAccountRequest:
      type: object
      required: [accountId]
      properties:
        accountId:
          type: string
          format: uuid
          description: The account whose spirit to retrieve.

    GetExperienceRequest:
      type: object
      required: [spiritId]
      properties:
        spiritId:
          type: string
          format: uuid
          description: The spirit whose experience to retrieve.

    RecordExperienceRequest:
      type: object
      required: [spiritId, domain, amount, source]
      properties:
        spiritId:
          type: string
          format: uuid
          description: The spirit to record experience for.
        domain:
          type: string
          description: >
            Dot-separated domain path (e.g., "combat.melee.sword").
            New domains are created automatically on first contribution.
        amount:
          type: number
          format: float
          description: Amount of experience to add.
        source:
          type: string
          description: Identifier of the contributing service (e.g., "character-encounter").
        sourceEventId:
          type: string
          format: uuid
          nullable: true
          description: Optional reference to the originating event for auditing.

    GetCapabilityManifestRequest:
      type: object
      required: [spiritId]
      properties:
        spiritId:
          type: string
          format: uuid
          description: The spirit whose manifest to retrieve.

    CreateSeedRequest:
      type: object
      required: [spiritId, seedType]
      properties:
        spiritId:
          type: string
          format: uuid
          description: The spirit to create a seed for.
        seedType:
          $ref: '#/components/schemas/SeedType'
        realmId:
          type: string
          format: uuid
          nullable: true
          description: Optional realm to bind this seed to.
        displayName:
          type: string
          nullable: true
          description: Player-chosen name for the seed. Auto-generated if omitted.

    GetSeedRequest:
      type: object
      required: [seedId]
      properties:
        seedId:
          type: string
          format: uuid
          description: The seed to retrieve.

    ListSeedsRequest:
      type: object
      required: [spiritId]
      properties:
        spiritId:
          type: string
          format: uuid
          description: The spirit whose seeds to list.
        includeArchived:
          type: boolean
          description: Whether to include archived seeds in the response.
          default: false

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

    InitiatePairBondRequest:
      type: object
      required: [initiatorSpiritId, targetSpiritId]
      properties:
        initiatorSpiritId:
          type: string
          format: uuid
          description: The spirit initiating the bond.
        targetSpiritId:
          type: string
          format: uuid
          description: The spirit being invited to bond.

    ConfirmPairBondRequest:
      type: object
      required: [pairBondId, confirmingSpiritId]
      properties:
        pairBondId:
          type: string
          format: uuid
          description: The pair bond to confirm.
        confirmingSpiritId:
          type: string
          format: uuid
          description: The spirit confirming the bond.

    GetPairBondRequest:
      type: object
      required: [spiritId]
      properties:
        spiritId:
          type: string
          format: uuid
          description: The spirit whose pair bond to retrieve.

    GetPairPartnerRequest:
      type: object
      required: [spiritId]
      properties:
        spiritId:
          type: string
          format: uuid
          description: The requesting spirit (returns their partner's info).

    # --- Response Models ---

    SpiritResponse:
      type: object
      required: [spiritId, accountId, createdAt, phase, totalExperience, pairBondId]
      properties:
        spiritId:
          type: string
          format: uuid
          description: Unique identifier for this spirit.
        accountId:
          type: string
          format: uuid
          description: The account this spirit is bound to.
        createdAt:
          type: string
          format: date-time
          description: When the spirit was created.
        phase:
          $ref: '#/components/schemas/SpiritPhase'
        totalExperience:
          type: number
          format: float
          description: Aggregate experience across all domains.
        pairBondId:
          type: string
          format: uuid
          nullable: true
          description: Pair bond ID if this spirit is paired, null otherwise.

    ExperienceResponse:
      type: object
      required: [spiritId, totalExperience, domains]
      properties:
        spiritId:
          type: string
          format: uuid
          description: The spirit this experience belongs to.
        totalExperience:
          type: number
          format: float
          description: Aggregate experience across all domains.
        domains:
          type: object
          additionalProperties:
            type: number
            format: float
          description: >
            Map of domain path to depth value. Keys are dot-separated
            (e.g., "combat.melee.sword"). Values are floating-point depth scores.

    UxCapabilityManifestResponse:
      type: object
      required: [spiritId, computedAt, version, modules]
      properties:
        spiritId:
          type: string
          format: uuid
          description: The spirit this manifest belongs to.
        computedAt:
          type: string
          format: date-time
          description: When this manifest was last computed.
        version:
          type: integer
          description: Monotonically increasing version number.
        modules:
          type: array
          items:
            $ref: '#/components/schemas/UxModule'
          description: List of UX modules with availability and fidelity.

    UxModule:
      type: object
      required: [moduleId, domain, fidelity, unlocked]
      properties:
        moduleId:
          type: string
          description: >
            Unique module identifier (e.g., "combat.stance",
            "crafting.material_selection").
        domain:
          type: string
          description: Parent experience domain this module belongs to.
        fidelity:
          type: number
          format: float
          description: >
            Control fidelity from 0.0 to 1.0. Higher values mean the spirit
            has more precise control over this interaction modality.
        unlocked:
          type: boolean
          description: Whether this module is available to the client at all.

    SeedResponse:
      type: object
      required:
        [seedId, spiritId, seedType, realmId, displayName,
         createdAt, lastActiveAt, status, householdId, entityId]
      properties:
        seedId:
          type: string
          format: uuid
          description: Unique identifier for this seed.
        spiritId:
          type: string
          format: uuid
          description: The spirit that owns this seed.
        seedType:
          $ref: '#/components/schemas/SeedType'
        realmId:
          type: string
          format: uuid
          nullable: true
          description: Realm this seed is bound to, if any.
        displayName:
          type: string
          description: Human-readable name for this seed.
        createdAt:
          type: string
          format: date-time
          description: When this seed was created.
        lastActiveAt:
          type: string
          format: date-time
          description: When this seed was last active.
        status:
          $ref: '#/components/schemas/SeedStatus'
        householdId:
          type: string
          format: uuid
          nullable: true
          description: >
            For Guardian type seeds, the household this seed manages.
            Null for other seed types or before household binding.
        entityId:
          type: string
          format: uuid
          nullable: true
          description: >
            For non-Guardian seed types, the entity this seed is bound to
            (e.g., dungeon core ID for DungeonMaster type).

    ListSeedsResponse:
      type: object
      required: [spiritId, seeds]
      properties:
        spiritId:
          type: string
          format: uuid
          description: The spirit these seeds belong to.
        seeds:
          type: array
          items:
            $ref: '#/components/schemas/SeedResponse'
          description: List of seeds for this spirit.

    PairBondResponse:
      type: object
      required:
        [pairBondId, spiritAlphaId, spiritBetaId, createdAt,
         status, bondStrength, sharedExperience]
      properties:
        pairBondId:
          type: string
          format: uuid
          description: Unique identifier for this pair bond.
        spiritAlphaId:
          type: string
          format: uuid
          description: First spirit in the pair.
        spiritBetaId:
          type: string
          format: uuid
          description: Second spirit in the pair.
        createdAt:
          type: string
          format: date-time
          description: When the bond was formed.
        status:
          $ref: '#/components/schemas/PairBondStatus'
        bondStrength:
          type: number
          format: float
          description: >
            Strength of the bond, grows with shared experience. Affects
            pair communication fidelity and shared scenario bonuses.
        sharedExperience:
          type: number
          format: float
          description: Total accumulated shared experience between the pair.

    PairPartnerResponse:
      type: object
      required: [partnerSpiritId, partnerAccountId, phase, activeSeedType, bondStrength]
      properties:
        partnerSpiritId:
          type: string
          format: uuid
          description: The partner spirit's ID.
        partnerAccountId:
          type: string
          format: uuid
          description: The partner's account ID.
        phase:
          $ref: '#/components/schemas/SpiritPhase'
        activeSeedType:
          $ref: '#/components/schemas/SeedType'
          nullable: true
          description: What seed type the partner currently has active, if any.
        bondStrength:
          type: number
          format: float
          description: Current bond strength.
```

### weaver-api.yaml

```yaml
openapi: 3.0.0
info:
  title: Weaver Service API
  version: 1.0.0
  description: >
    Player experience orchestration service (L4 GameFeatures) for void
    navigation, scenario routing, and progressive discovery. The player-side
    counterpart to Puppetmaster: where Puppetmaster orchestrates what NPCs
    experience, Weaver orchestrates what players experience. Manages void
    instances (personal ambient spaces for spirits between game sessions),
    dynamically spawns points of interest based on spirit metadata and
    movement patterns, routes spirits into scenarios (isolated sandboxes,
    world slices, or persistent realm entry depending on deployment phase),
    and coordinates scenario chaining for emergent discovery paths. Features
    a background Void Orchestrator worker that evaluates all active void
    instances on a configurable tick interval.
x-service-layer: GameFeatures

servers:
  - url: http://localhost:5012

paths:
  # --- Void Management ---

  /weaver/void/enter:
    post:
      operationId: EnterVoid
      summary: Spirit enters the void
      description: >
        Creates a void instance for the specified spirit. Called when a spirit
        logs in or returns from a scenario. Returns the initial void state
        including any pre-seeded POIs based on the spirit's metadata.
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

  /weaver/void/get:
    post:
      operationId: GetVoidState
      summary: Get current void instance state
      description: Returns the current state of the spirit's void instance including active POIs.
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

  /weaver/void/update-position:
    post:
      operationId: UpdatePosition
      summary: Report spirit position and velocity
      description: >
        High-frequency position update from the client. Used by the Void
        Orchestrator worker to determine POI spawning, proximity triggers,
        and drift metric accumulation. Lightweight endpoint optimized for
        throughput.
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

  /weaver/void/leave:
    post:
      operationId: LeaveVoid
      summary: Spirit leaves the void
      description: >
        Destroys the void instance. Called when the spirit disconnects or
        enters a seed directly (bypassing scenario entry). Cleans up all
        active POIs.
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

  /weaver/poi/list:
    post:
      operationId: ListPois
      summary: List active POIs for current void instance
      description: >
        Returns all currently active points of interest in the spirit's void
        instance. POIs are spawned by the Void Orchestrator worker and may
        change between calls.
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

  /weaver/poi/interact:
    post:
      operationId: InteractWithPoi
      summary: Spirit interacts with a POI
      description: >
        Signals that the spirit has engaged with a point of interest. For
        proximity-triggered POIs, this is called automatically. For
        interaction-triggered POIs, this requires explicit action. May
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

  /weaver/poi/decline:
    post:
      operationId: DeclinePoi
      summary: Explicitly decline a prompted POI
      description: >
        Declines a POI that presented a prompt. The POI is marked as declined
        and factored into future scenario selection (the spirit chose not to
        engage with this type of content).
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

  /weaver/scenario/enter:
    post:
      operationId: EnterScenario
      summary: Enter a scenario
      description: >
        Enters a scenario from a POI interaction or direct selection. Creates
        a scenario instance backed by a Game Session. The spirit transitions
        from void to scenario. Returns the scenario state and Game Session
        connection details.
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

  /weaver/scenario/get:
    post:
      operationId: GetScenarioState
      summary: Get current scenario state
      description: Returns the current state of the spirit's active scenario instance.
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

  /weaver/scenario/complete:
    post:
      operationId: CompleteScenario
      summary: Complete a scenario
      description: >
        Marks the current scenario as completed. Awards experience to the
        spirit based on the scenario template's domain weights and the
        spirit's participation. Returns the spirit to the void.
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

  /weaver/scenario/abandon:
    post:
      operationId: AbandonScenario
      summary: Abandon scenario and return to void
      description: >
        Abandons the current scenario without completion. Partial experience
        may be awarded based on participation. Returns the spirit to the void.
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

  /weaver/scenario/chain:
    post:
      operationId: ChainScenario
      summary: Chain to another scenario from within current
      description: >
        Enters a chained scenario from within the current active scenario.
        The current scenario is paused or completed (depending on chain type)
        and the spirit transitions to the new scenario. Chain depth is limited
        by the template's MaxChainDepth.
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

  /weaver/template/create:
    post:
      operationId: CreateTemplate
      summary: Register a new scenario template
      description: >
        Creates a new scenario template definition. Templates define the
        structure, prerequisites, chaining rules, and content references
        for a playable scenario experience.
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

  /weaver/template/get:
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

  /weaver/template/get-by-code:
    post:
      operationId: GetTemplateByCode
      summary: Get scenario template by code
      description: >
        Looks up a template by its human-readable code identifier
        (e.g., "combat-basics-arena").
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

  /weaver/template/list:
    post:
      operationId: ListTemplates
      summary: List scenario templates with filtering
      description: >
        Returns scenario templates matching the specified filters. Supports
        filtering by category, deployment phase, connectivity mode, and status.
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

  /weaver/template/update:
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

  /weaver/template/deprecate:
    post:
      operationId: DeprecateTemplate
      summary: Deprecate a scenario template
      description: >
        Marks a template as deprecated. Deprecated templates are not offered
        to new spirits but existing active instances may complete.
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

  /weaver/phase/get:
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

  /weaver/phase/update:
    post:
      operationId: UpdatePhaseConfig
      summary: Update deployment phase configuration
      description: >
        Updates the deployment phase or its configuration. Changing the phase
        (e.g., Alpha to Beta, Beta to Release) affects which connectivity
        modes are available for scenario templates.
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

  /weaver/phase/get-metrics:
    post:
      operationId: GetPhaseMetrics
      summary: Get phase-level operational metrics
      description: >
        Returns operational metrics for the current deployment phase including
        active void instances, scenario instance counts, and capacity utilization.
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

  # --- Pair Scenarios ---

  /weaver/pair/enter-together:
    post:
      operationId: EnterScenarioTogether
      summary: Paired spirits enter a scenario together
      description: >
        Both spirits in a pair bond enter the same scenario simultaneously.
        Creates a shared scenario instance. Both spirits must be in the void.
      x-permissions: [authenticated]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/EnterTogetherRequest'
      responses:
        '200':
          description: Both spirits entered scenario
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ScenarioStateResponse'

  /weaver/pair/get-shared-void:
    post:
      operationId: GetSharedVoidState
      summary: Get shared void state for paired spirits
      description: >
        Returns the void state visible to both paired spirits when they share
        a void instance. Includes both spirits' positions and shared POIs.
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
        How a scenario instance connects to the game world. Alpha uses
        Isolated, Beta adds WorldSlice, Release adds Persistent.
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
      description: How a POI is activated by the spirit.
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

    # --- Void Request/Response Models ---

    EnterVoidRequest:
      type: object
      required: [spiritId, sessionId]
      properties:
        spiritId:
          type: string
          format: uuid
          description: The spirit entering the void.
        sessionId:
          type: string
          format: uuid
          description: The Connect session ID for this spirit.

    GetVoidStateRequest:
      type: object
      required: [spiritId]
      properties:
        spiritId:
          type: string
          format: uuid
          description: The spirit whose void state to retrieve.

    UpdatePositionRequest:
      type: object
      required: [spiritId, position, velocity]
      properties:
        spiritId:
          type: string
          format: uuid
          description: The spirit reporting position.
        position:
          $ref: '#/components/schemas/Vec3'
        velocity:
          $ref: '#/components/schemas/Vec3'

    LeaveVoidRequest:
      type: object
      required: [spiritId]
      properties:
        spiritId:
          type: string
          format: uuid
          description: The spirit leaving the void.

    VoidStateResponse:
      type: object
      required: [voidInstanceId, spiritId, position, activePois]
      properties:
        voidInstanceId:
          type: string
          format: uuid
          description: Unique identifier for this void instance.
        spiritId:
          type: string
          format: uuid
          description: The spirit this void belongs to.
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
          description: >
            POIs that were triggered by this position update
            (proximity-triggered POIs the spirit moved close to).
          nullable: true

    LeaveVoidResponse:
      type: object
      required: [spiritId, sessionDurationSeconds]
      properties:
        spiritId:
          type: string
          format: uuid
          description: The spirit that left.
        sessionDurationSeconds:
          type: number
          format: float
          description: How long the spirit was in the void.

    # --- POI Models ---

    PoiSummary:
      type: object
      required: [poiId, position, poiType, triggerMode, status]
      properties:
        poiId:
          type: string
          format: uuid
          description: Unique identifier for this POI.
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
          description: For proximity-triggered POIs, the activation radius.
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
      required: [spiritId]
      properties:
        spiritId:
          type: string
          format: uuid
          description: The spirit whose POIs to list.

    ListPoisResponse:
      type: object
      required: [voidInstanceId, pois]
      properties:
        voidInstanceId:
          type: string
          format: uuid
          description: The void instance these POIs belong to.
        pois:
          type: array
          items:
            $ref: '#/components/schemas/PoiSummary'
          description: Active POIs.

    InteractWithPoiRequest:
      type: object
      required: [spiritId, poiId]
      properties:
        spiritId:
          type: string
          format: uuid
          description: The spirit interacting.
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
          description: The POI that was interacted with.
        result:
          type: string
          description: >
            Interaction result. One of: "scenario_prompt" (shows a prompt),
            "scenario_enter" (immediate scenario entry), "poi_update"
            (POI state changed), "chain_offer" (presents chaining options).
        scenarioTemplateId:
          type: string
          format: uuid
          nullable: true
          description: If result involves a scenario, the template ID.
        promptText:
          type: string
          nullable: true
          description: If result is scenario_prompt, the prompt to display.
        promptChoices:
          type: array
          items:
            type: string
          nullable: true
          description: If result is scenario_prompt with choices, the available options.

    DeclinePoiRequest:
      type: object
      required: [spiritId, poiId]
      properties:
        spiritId:
          type: string
          format: uuid
          description: The spirit declining.
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
          description: The POI that was declined.
        acknowledged:
          type: boolean
          description: Whether the decline was processed.

    # --- Scenario Models ---

    EnterScenarioRequest:
      type: object
      required: [spiritId, scenarioTemplateId]
      properties:
        spiritId:
          type: string
          format: uuid
          description: The spirit entering the scenario.
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
          description: If the POI presented choices, which one was selected.

    GetScenarioStateRequest:
      type: object
      required: [spiritId]
      properties:
        spiritId:
          type: string
          format: uuid
          description: The spirit whose active scenario to retrieve.

    CompleteScenarioRequest:
      type: object
      required: [spiritId, scenarioInstanceId]
      properties:
        spiritId:
          type: string
          format: uuid
          description: The spirit completing the scenario.
        scenarioInstanceId:
          type: string
          format: uuid
          description: The scenario instance being completed.

    AbandonScenarioRequest:
      type: object
      required: [spiritId, scenarioInstanceId]
      properties:
        spiritId:
          type: string
          format: uuid
          description: The spirit abandoning the scenario.
        scenarioInstanceId:
          type: string
          format: uuid
          description: The scenario instance being abandoned.

    ChainScenarioRequest:
      type: object
      required: [spiritId, currentScenarioInstanceId, targetTemplateId]
      properties:
        spiritId:
          type: string
          format: uuid
          description: The spirit chaining scenarios.
        currentScenarioInstanceId:
          type: string
          format: uuid
          description: The current scenario being chained from.
        targetTemplateId:
          type: string
          format: uuid
          description: The template to chain into.

    ScenarioStateResponse:
      type: object
      required:
        [scenarioInstanceId, scenarioTemplateId, gameSessionId,
         connectivityMode, status, createdAt]
      properties:
        scenarioInstanceId:
          type: string
          format: uuid
          description: Unique identifier for this scenario instance.
        scenarioTemplateId:
          type: string
          format: uuid
          description: The template this instance was created from.
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
          description: When this instance was created.
        chainedFrom:
          type: string
          format: uuid
          nullable: true
          description: If chained, the scenario this was entered from.
        chainDepth:
          type: integer
          description: How many chains deep this scenario is.
          default: 0

    ScenarioCompletionResponse:
      type: object
      required: [scenarioInstanceId, experienceAwarded, returnToVoid]
      properties:
        scenarioInstanceId:
          type: string
          format: uuid
          description: The completed scenario.
        experienceAwarded:
          type: object
          additionalProperties:
            type: number
            format: float
          description: Map of domain path to experience amount awarded.
        returnToVoid:
          type: boolean
          description: Whether the spirit was returned to the void.

    AbandonScenarioResponse:
      type: object
      required: [scenarioInstanceId, partialExperienceAwarded]
      properties:
        scenarioInstanceId:
          type: string
          format: uuid
          description: The abandoned scenario.
        partialExperienceAwarded:
          type: object
          additionalProperties:
            type: number
            format: float
          description: Partial experience awarded based on participation.

    # --- Template Models ---

    DomainWeight:
      type: object
      description: A domain and its weight for experience calculation.
      required: [domain, weight]
      properties:
        domain:
          type: string
          description: Domain path (e.g., "combat.melee").
        weight:
          type: number
          format: float
          description: Relative weight for this domain (0.0-1.0).

    ScenarioPrerequisites:
      type: object
      description: Prerequisites for a scenario template.
      properties:
        requiredDomains:
          type: object
          additionalProperties:
            type: number
            format: float
          description: Minimum domain depths required.
          nullable: true
        requiredScenarios:
          type: array
          items:
            type: string
          description: Scenario codes that must be completed first.
          nullable: true
        excludedScenarios:
          type: array
          items:
            type: string
          description: Scenario codes that exclude this template.
          nullable: true

    ScenarioChaining:
      type: object
      description: Chaining configuration for a scenario template.
      properties:
        leadsTo:
          type: array
          items:
            type: string
          description: Scenario codes reachable from within this scenario.
          nullable: true
        chainProbabilities:
          type: object
          additionalProperties:
            type: number
            format: float
          description: Weighted selection probabilities for chain offers.
          nullable: true
        maxChainDepth:
          type: integer
          description: Maximum chaining depth before forcing return to void.
          default: 3

    ScenarioMultiplayer:
      type: object
      description: Multiplayer configuration for a scenario template.
      required: [minPlayers, maxPlayers]
      properties:
        minPlayers:
          type: integer
          description: Minimum players required.
          default: 1
        maxPlayers:
          type: integer
          description: Maximum players allowed.
          default: 1
        matchmakingQueueCode:
          type: string
          nullable: true
          description: Matchmaking queue code for grouping, if applicable.
        pairBondPreferred:
          type: boolean
          description: Whether to prioritize paired spirits.
          default: false

    ScenarioContent:
      type: object
      description: Content references for a scenario template.
      properties:
        behaviorDocumentId:
          type: string
          nullable: true
          description: ABML document for scenario orchestration.
        sceneDocumentId:
          type: string
          format: uuid
          nullable: true
          description: Scene service document for the environment.
        realmId:
          type: string
          format: uuid
          nullable: true
          description: For WorldSlice mode, which realm to slice from.
        locationCode:
          type: string
          nullable: true
          description: For WorldSlice mode, where in the realm.

    CreateTemplateRequest:
      type: object
      required: [code, displayName, description, category, domainWeights, allowedPhases]
      properties:
        code:
          type: string
          description: Human-readable identifier (e.g., "combat-basics-arena").
        displayName:
          type: string
          description: Display name for the scenario.
        description:
          type: string
          description: Detailed description of the scenario experience.
        category:
          $ref: '#/components/schemas/ScenarioCategory'
        subcategory:
          type: string
          nullable: true
          description: Finer classification within category.
        domainWeights:
          type: array
          items:
            $ref: '#/components/schemas/DomainWeight'
          description: Which experience domains this scenario teaches.
        minSpiritPhase:
          $ref: '#/components/schemas/SpiritPhase'
          default: Nascent
        connectivityMode:
          $ref: '#/components/schemas/ConnectivityMode'
          default: Isolated
        allowedPhases:
          type: array
          items:
            $ref: '#/components/schemas/DeploymentPhase'
          description: Which deployment phases can offer this scenario.
        maxConcurrentInstances:
          type: integer
          description: Maximum concurrent instances of this scenario.
          default: 100
        estimatedDurationMinutes:
          type: integer
          nullable: true
          description: Expected play time.
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
          description: The template to retrieve.

    GetTemplateByCodeRequest:
      type: object
      required: [code]
      properties:
        code:
          type: string
          description: The template code to look up.

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
          description: The template to update.
        displayName:
          type: string
          nullable: true
          description: New display name.
        description:
          type: string
          nullable: true
          description: New description.
        domainWeights:
          type: array
          items:
            $ref: '#/components/schemas/DomainWeight'
          nullable: true
          description: Updated domain weights.
        maxConcurrentInstances:
          type: integer
          nullable: true
          description: Updated capacity limit.
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
          description: The template to deprecate.

    ListTemplatesResponse:
      type: object
      required: [templates, totalCount, page, pageSize]
      properties:
        templates:
          type: array
          items:
            $ref: '#/components/schemas/ScenarioTemplateResponse'
          description: Templates matching the filters.
        totalCount:
          type: integer
          description: Total matching templates across all pages.
        page:
          type: integer
          description: Current page number.
        pageSize:
          type: integer
          description: Page size.

    ScenarioTemplateResponse:
      type: object
      required:
        [scenarioTemplateId, code, displayName, description, category,
         domainWeights, minSpiritPhase, connectivityMode, allowedPhases,
         maxConcurrentInstances, status, createdAt, updatedAt]
      properties:
        scenarioTemplateId:
          type: string
          format: uuid
          description: Unique identifier for this template.
        code:
          type: string
          description: Human-readable code.
        displayName:
          type: string
          description: Display name.
        description:
          type: string
          description: Detailed description.
        category:
          $ref: '#/components/schemas/ScenarioCategory'
        subcategory:
          type: string
          nullable: true
          description: Finer classification.
        domainWeights:
          type: array
          items:
            $ref: '#/components/schemas/DomainWeight'
          description: Experience domain weights.
        minSpiritPhase:
          $ref: '#/components/schemas/SpiritPhase'
        connectivityMode:
          $ref: '#/components/schemas/ConnectivityMode'
        allowedPhases:
          type: array
          items:
            $ref: '#/components/schemas/DeploymentPhase'
          description: Deployment phases that offer this scenario.
        maxConcurrentInstances:
          type: integer
          description: Capacity limit.
        estimatedDurationMinutes:
          type: integer
          nullable: true
          description: Expected play time.
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
          description: When this template was created.
        updatedAt:
          type: string
          format: date-time
          description: When this template was last updated.

    # --- Phase Models ---

    GetPhaseConfigRequest:
      type: object
      description: Empty request body, returns current phase config.

    UpdatePhaseConfigRequest:
      type: object
      properties:
        currentPhase:
          $ref: '#/components/schemas/DeploymentPhase'
          nullable: true
          description: New deployment phase.
        maxConcurrentScenariosGlobal:
          type: integer
          nullable: true
          description: Updated global capacity.
        persistentEntryEnabled:
          type: boolean
          nullable: true
          description: Enable/disable persistent world entry (Release phase).
        voidMinigamesEnabled:
          type: boolean
          nullable: true
          description: Keep void POIs active post-release.

    PhaseConfigResponse:
      type: object
      required: [currentPhase, maxConcurrentScenariosGlobal, persistentEntryEnabled, voidMinigamesEnabled]
      properties:
        currentPhase:
          $ref: '#/components/schemas/DeploymentPhase'
        maxConcurrentScenariosGlobal:
          type: integer
          description: Global scenario capacity.
        persistentEntryEnabled:
          type: boolean
          description: Whether persistent realm entry is available.
        voidMinigamesEnabled:
          type: boolean
          description: Whether void POIs remain active.

    GetPhaseMetricsRequest:
      type: object
      description: Empty request body, returns current metrics.

    PhaseMetricsResponse:
      type: object
      required:
        [currentPhase, activeVoidInstances, activeScenarioInstances,
         scenarioCapacityUtilization]
      properties:
        currentPhase:
          $ref: '#/components/schemas/DeploymentPhase'
        activeVoidInstances:
          type: integer
          description: Number of spirits currently in the void.
        activeScenarioInstances:
          type: integer
          description: Number of active scenario instances.
        scenarioCapacityUtilization:
          type: number
          format: float
          description: Percentage of global scenario capacity in use (0.0-1.0).

    # --- Pair Models ---

    EnterTogetherRequest:
      type: object
      required: [pairBondId, scenarioTemplateId]
      properties:
        pairBondId:
          type: string
          format: uuid
          description: The pair bond linking both spirits.
        scenarioTemplateId:
          type: string
          format: uuid
          description: The scenario template to enter together.

    GetSharedVoidRequest:
      type: object
      required: [pairBondId]
      properties:
        pairBondId:
          type: string
          format: uuid
          description: The pair bond to get shared void state for.

    SharedVoidStateResponse:
      type: object
      required: [pairBondId, spiritAlpha, spiritBeta, sharedPois]
      properties:
        pairBondId:
          type: string
          format: uuid
          description: The pair bond.
        spiritAlpha:
          $ref: '#/components/schemas/PairedSpiritVoidState'
        spiritBeta:
          $ref: '#/components/schemas/PairedSpiritVoidState'
        sharedPois:
          type: array
          items:
            $ref: '#/components/schemas/PoiSummary'
          description: POIs visible to both spirits.

    PairedSpiritVoidState:
      type: object
      required: [spiritId, position, inVoid]
      properties:
        spiritId:
          type: string
          format: uuid
          description: The spirit's ID.
        position:
          $ref: '#/components/schemas/Vec3'
        inVoid:
          type: boolean
          description: Whether this spirit is currently in the void.
```

---

## Open Design Questions

1. **Pair bond ownership**: Should pair bonds live in Spirit (tightly coupled to spirit mechanics) or Relationship (existing entity relationship infrastructure)? Recommendation: Spirit owns it, since pair bonds have unique semantics (permanent, 1:1, metaphysical communication channel) that don't map well to Relationship's general-purpose model.

2. **Experience decay**: Should unused domains decay over time? Arguments for: prevents spirits from being "good at everything" after enough play time, encourages specialization. Arguments against: punishes players who take breaks, contradicts "understanding persists across generations." Recommendation: Off by default, configurable per realm.

3. **Seed type extensibility**: The current SeedType enum (Guardian, DungeonMaster, Variant) is small. Should seed types be schema-extensible or hardcoded? Recommendation: Start hardcoded, extend via schema when new types are proven in gameplay.

4. **Void position protocol**: The UpdatePosition endpoint will be called frequently. Should this use the standard POST JSON pattern or a lighter-weight binary protocol through Connect? Recommendation: Start with POST JSON through the standard WebSocket path; optimize to binary only if latency/throughput becomes a problem.

5. **SpiritPhase vs. continuous**: Is the 4-phase enum sufficient, or should spirit development be purely continuous (just the experience domain map)? Phases are useful for Weaver's scenario routing and client presentation, but they impose discrete boundaries on what should be a gradient. Recommendation: Keep phases as a computed convenience classification, never use them as hard gates.

6. **Cross-seed experience sharing**: How much experience crosses seed boundaries? Full sharing means a combat-heavy Guardian seed immediately gives a DungeonMaster seed combat agency. Partial sharing preserves some discovery per seed. Recommendation: Configurable multiplier (default 0.5 -- half of experience crosses seeds).
