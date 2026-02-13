# Dungeon Plugin Deep Dive

> **Plugin**: lib-dungeon
> **Schema**: schemas/dungeon-api.yaml
> **Version**: 1.0.0
> **State Stores**: dungeon-cores (MySQL), dungeon-bonds (MySQL), dungeon-inhabitants (Redis), dungeon-memories (MySQL), dungeon-cache (Redis), dungeon-lock (Redis)
> **Status**: Pre-implementation (architectural specification)
> **Planning**: [DUNGEON-AS-ACTOR.md](../planning/DUNGEON-AS-ACTOR.md), [HOUDINI-PROCEDURAL-GENERATION.md](../planning/HOUDINI-PROCEDURAL-GENERATION.md)

## Overview

Dungeon lifecycle orchestration service (L4 GameFeatures) for living dungeon entities that perceive, grow, and act autonomously within the Bannou actor system. A thin orchestration layer (like Divine over Currency/Seed/Collection, Quest over Contract, Escrow over Currency/Item) that composes existing Bannou primitives to deliver dungeon-as-actor game mechanics.

**Composability**: Dungeon core identity is owned here. Dungeon behavior is Actor (event brain) via Puppetmaster. Dungeon growth is Seed (`dungeon_core` seed type). Dungeon master bond is Contract. Mana economy is Currency. Physical layout is Save-Load + Mapping. Visual composition is Scene. Memory items are Item. Monster spawning and trap activation are dungeon-specific APIs orchestrated by lib-dungeon. Player-facing dungeon master experience is Gardener (dungeon garden type). Procedural chamber generation is a future integration with lib-procedural (Houdini backend).

**The divine actor parallel**: Dungeon cores follow the same structural pattern as divine actors -- event brain actors launched via Puppetmaster, backed by seeds for progressive growth, with a currency-based economy and bonded relationships. The difference is in the *ceremony*: lib-divine orchestrates blessings, divinity economy, and follower management; lib-dungeon orchestrates monster spawning, trap activation, memory manifestation, and master bonds. They are parallel orchestration layers composing the same underlying primitives (Actor, Seed, Currency, Contract, Gardener), not the same service. This mirrors how Quest and Escrow both compose Contract but provide different game-flavored APIs.

**Critical architectural insight**: Dungeon cores influence characters through the character's own Actor, not directly. A dungeon core's Actor (event brain) monitors domain events and makes decisions; the bonded master's character Actor receives commands as perceptions, gated by the master's `dungeon_master` seed capabilities. This is the same indirect influence pattern used by divine actors (gods influence through the character's Actor, not by controlling the character directly).

**The dungeon as garden**: From the dungeon master's perspective, the dungeon IS a garden -- an abstract conceptual space that defines their gameplay context. The dungeon core actor serves as the gardener behavior, managing what the master perceives, what commands are available, and what events reach them. For adventurers entering the dungeon, it is a physical game location -- they interact through normal combat, exploration, and trap mechanics. This dual nature (physical space for visitors, garden for the master) is a key architectural distinction.

**Two seed types, one pair**: The dungeon system introduces two seed types that grow in parallel: `dungeon_core` (the dungeon's own progressive growth -- mana capacity, genetic library, trap sophistication, spatial control, memory depth) and `dungeon_master` (the bonded entity's growth in the mastery role -- perception, command, channeling, coordination). Seeds track growth in *roles*, not growth in *entities*. The same character can hold a `guardian` seed (their spirit growth), a `dungeon_master` seed (their mastery role growth), and any other role-specific seeds simultaneously.

**Zero Arcadia-specific content**: lib-dungeon is a generic dungeon management service. Arcadia's personality types (martial, memorial, festive, scholarly), specific creature species, and narrative manifestation styles are configured through ABML behaviors and seed type definitions at deployment time, not baked into lib-dungeon.

**Current status**: Pre-implementation. No schema, no code. This deep dive is an architectural specification based on [DUNGEON-AS-ACTOR.md](../planning/DUNGEON-AS-ACTOR.md) and the broader architectural patterns established by lib-divine, lib-gardener, and lib-puppetmaster. Internal-only, never internet-facing.

---

## Why Not lib-divine? (Architectural Rationale)

Dungeon cores and divine actors share the same structural pattern. Both are event brain actors launched via Puppetmaster, backed by seeds, with currency economies and bonded relationships. The question arises: should dungeon cores simply be a *type* of deity in lib-divine?

**The answer is no -- same pattern, different ceremony.**

| Concern | lib-divine Ceremony | lib-dungeon Ceremony |
|---------|---------------------|----------------------|
| **Identity** | Deity with domains, personality traits | Dungeon core with personality type, core location, domain radius |
| **Economy** | Divinity (earned from mortal actions in domain) | Mana (harvested from deaths, ambient leyline proximity) |
| **Bonds** | Followers (many characters per deity) via Relationship | Master (one entity per dungeon) via Contract |
| **Effects on others** | Blessings granted via Status/Collection | Monster spawning, trap activation, layout shifting, memory manifestation |
| **Growth** | `deity_domain` seed (domain influence) | `dungeon_core` seed (mana, genetics, traps, expansion, memory) |
| **Garden** | Tends player discovery/lobby/in-game spaces | Tends dungeon space for the bonded master |
| **Physical presence** | None (gods are immaterial observers) | Yes -- rooms, corridors, traps, inhabitants, manifested memories |

lib-divine's APIs (blessings, follower management, attention slots, divinity generation) don't map to dungeon mechanics. Dungeon-specific APIs (spawn monster, activate trap, seal passage, shift layout, manifest memory) have no analogue in the divine service. Forcing both into one service would bloat lib-divine with dungeon mechanics or require the dungeon to shoehorn its operations into blessing/follower semantics.

**What they share is infrastructure, not API surface**:
- Both launch actors via Puppetmaster (event brain type)
- Both use Seed for progressive growth
- Both use Currency for their economy
- Both use Gardener for tending conceptual spaces
- Both influence characters indirectly through the character's Actor

This shared infrastructure is already factored into L0/L1/L2 services. lib-divine and lib-dungeon are both L4 orchestration layers that compose these primitives differently.

---

## The Dungeon as Garden

The Gardener deep dive defines a garden as "a conceptual space that defines a player's current gameplay context." Every player is always in some garden, and the gardener behavior manages their experience. The dungeon extends this pattern:

| Perspective | Experience | System |
|-------------|-----------|--------|
| **Dungeon master** (bonded partner) | The dungeon IS their garden -- they perceive through the dungeon's senses, issue commands, and experience the dungeon's emotional state | Gardener (dungeon garden type) |
| **Adventurers** (visitors) | The dungeon is a physical game location with monsters, traps, puzzles, and loot | Normal game mechanics (combat, inventory, mapping) |
| **Dungeon core** (the actor) | The dungeon tends its own space AND the master's garden experience simultaneously | Actor + Gardener APIs + Dungeon APIs |

The dungeon core actor serves as the gardener behavior for the master's garden. When a character bonds with a dungeon core:

1. A "dungeon" garden type is created for the master
2. The dungeon core actor becomes the gardener behavior for that garden
3. Entity associations bind the master's character, the dungeon's inhabitants, and relevant inventories to the garden context
4. The master's perception of the dungeon (what they "see") is managed by the garden's entity session registrations
5. Commands from the master arrive as perceptions in the dungeon core's actor, gated by the `dungeon_master` seed capabilities

When no master is bonded, the dungeon core actor still runs autonomously -- it just has no garden to tend (no partner receiving its experience). It continues to perceive, decide, and act within its domain purely based on its own `dungeon_core` seed capabilities.

**Multi-game variability**: The dungeon garden behavior document varies per game. In Arcadia, the dungeon master experience uses perception-gated awareness, command-gated actions, and channeling-gated power flows. A different game might use different mechanics for the master-dungeon relationship, or omit the dungeon garden entirely (autonomous dungeons with no master partnership). lib-dungeon provides primitives, not policy.

---

## The Asymmetric Bond Pattern

The dungeon-master relationship follows the same structural pattern as the player-character relationship:

```
PLAYER-CHARACTER BOND                    CHARACTER-DUNGEON BOND
---------------------                    ----------------------

Account (with guardian seed)             Character (with dungeon_master seed)
    |                                        |
    | possesses (asymmetric)                 | bonds with (asymmetric)
    | can "go away"                          | can "go away"
    | mutual benefit while engaged           | mutual benefit while engaged
    |                                        |
    v                                        v
Character (autonomous NPC brain)         Dungeon (autonomous Actor brain)
    always running                           always running
    acts independently when                  acts independently when
    player is absent                         master is absent
```

Both relationships share key properties:
- **Asymmetric agency**: One party has broader context (player sees UX, character sees dungeon rooms), the other has deeper local knowledge (character knows its body, dungeon knows its domain)
- **Graceful absence**: The autonomous entity continues functioning when its partner disengages
- **Progressive depth**: The relationship deepens with shared experience (guardian seed grows, dungeon_master seed grows)
- **Contractual terms**: Governed by agreements with consequences (implicit for player-character, explicit Contract for character-dungeon)

The partner is not always a character. Corrupted bonds use a monster (actor-managed entity). When the partner isn't a character, the dungeon core has fewer tools -- no character Actor to send commands to as perceptions. The paired seeds still grow, but the dungeon core exerts influence primarily through the seed bond alone (almost no active control), and the monster avatar operates with limited agency to grow its `dungeon_master` seed deliberately.

---

## Seed Types

### dungeon_core (The Dungeon's Growth)

| Property | Value |
|----------|-------|
| **SeedTypeCode** | `dungeon_core` |
| **DisplayName** | Dungeon Core |
| **MaxPerOwner** | 1 |
| **AllowedOwnerTypes** | `["actor"]` |
| **BondCardinality** | 0 (Contract handles the relationship, not seed bonds) |

**Growth Phases** (ordered by MinTotalGrowth):

| Phase | MinTotalGrowth | Behavior |
|-------|---------------|----------|
| Dormant | 0.0 | Reactive only -- responds to intrusions with basic instinct. Personality barely visible. |
| Stirring | 10.0 | Proactive spawning and basic trap usage. Personality preferences begin to emerge. |
| Awakened | 50.0 | Layout manipulation, complex tactics, memory capture. Strong personality expression. |
| Ancient | 200.0 | Memory manifestation, event coordination, full master synergy. Unique living entity. |

**Growth Domains**:

| Domain | Subdomain | Purpose |
|--------|-----------|---------|
| `mana_reserves` | `.ambient`, `.harvested` | Accumulated mana capacity (ambient from leyline, harvested from deaths) |
| `genetic_library` | `.{species}` | Logos completion per species for monster spawning quality |
| `trap_complexity` | `.mechanical`, `.magical`, `.puzzle` | Trap design sophistication by type |
| `domain_expansion` | `.radius`, `.rooms`, `.extension` | Spatial control capability |
| `memory_depth` | `.capture`, `.manifestation` | Memory accumulation and manifestation quality |

**Capability Rules** (gated by growth domain thresholds):

| Capability Code | Domain | Threshold | Formula | Description |
|----------------|--------|-----------|---------|-------------|
| `spawn_monster.basic` | `genetic_library` | 1.0 | linear | Basic pneuma echo creation |
| `spawn_monster.enhanced` | `genetic_library` | 5.0 | logarithmic | Enhanced quality spawning |
| `spawn_monster.alpha` | `genetic_library` | 20.0 | step | Alpha-tier creature spawning |
| `activate_trap.basic` | `trap_complexity` | 1.0 | linear | Basic trap activation |
| `activate_trap.complex` | `trap_complexity` | 10.0 | logarithmic | Complex trap activation |
| `seal_passage` | `domain_expansion` | 5.0 | step | Block/unblock passages |
| `shift_layout` | `domain_expansion` | 15.0 | step | Minor structural changes |
| `emit_miasma` | `mana_reserves` | 3.0 | linear | Ambient mana density control |
| `spawn_event_agent` | `mana_reserves` | 10.0 | step | Encounter coordinator creation |
| `evolve_inhabitant` | `genetic_library` | 10.0 | logarithmic | Monster echo upgrades |
| `manifest_memory` | `memory_depth` | 8.0 | step | Crystallize memories into physical form |

### dungeon_master (The Master's Role Growth)

| Property | Value |
|----------|-------|
| **SeedTypeCode** | `dungeon_master` |
| **DisplayName** | Dungeon Master |
| **MaxPerOwner** | 1 (can only master one dungeon at a time) |
| **AllowedOwnerTypes** | `["character", "actor"]` (character for willing bonds, actor for monster avatars) |
| **BondCardinality** | 0 (Contract handles the relationship) |

**Growth Phases** (ordered by MinTotalGrowth):

| Phase | MinTotalGrowth | Communication Fidelity |
|-------|---------------|----------------------|
| Bonded | 0.0 | Basic emotional awareness -- crude impressions of dungeon state |
| Attuned | 5.0 | Clear communication, basic commands accepted |
| Symbiotic | 25.0 | Rich command vocabulary, power channeling, tactical coordination |
| Transcendent | 100.0 | Near-perfect coordination, shared consciousness |

**Growth Domains**:

| Domain | Subdomain | Purpose |
|--------|-----------|---------|
| `perception` | `.emotional`, `.spatial`, `.tactical` | Shared awareness with the dungeon core |
| `command` | `.spawning`, `.traps`, `.layout`, `.strategy` | Directing the dungeon's actions |
| `channeling` | `.passive`, `.combat`, `.regeneration` | Mana/power flow between core and master |
| `coordination` | `.defenders`, `.encounters`, `.manifestation` | Directing autonomous dungeon systems |

**Capability Rules** (gated by growth domain thresholds):

| Capability Code | Domain | Threshold | Formula | Description |
|----------------|--------|-----------|---------|-------------|
| `perception.basic` | `perception` | 0.0 | linear | Sense dungeon's emotional state from bonding moment |
| `perception.tactical` | `perception` | 5.0 | logarithmic | Perceive intruder details, room states |
| `command.basic` | `command` | 2.0 | linear | Simple directives: "spawn defenders", "activate trap" |
| `command.tactical` | `command` | 10.0 | logarithmic | Multi-wave defense, ambush coordination |
| `channeling.basic` | `channeling` | 3.0 | linear | Receive mana/power from core |
| `channeling.combat` | `channeling` | 15.0 | logarithmic | Channel core's combat abilities in battle |
| `coordination.event_brain` | `coordination` | 20.0 | step | Direct encounter coordinators personally |

### Growth Contribution Events

| Event | Core Seed Domain | Master Seed Domain | Source |
|-------|-----------------|-------------------|--------|
| Monster killed within domain | `mana_reserves.harvested` +0.1 | -- | Dungeon cognition |
| Logos absorbed from death | `genetic_library.{species}` +varies | -- | Dungeon cognition |
| Trap successfully triggers | `trap_complexity.*` +0.2 | `command.traps` +0.1 (if master directed) | Dungeon cognition |
| Domain boundary expanded | `domain_expansion.*` +1.0 | -- | Domain management |
| Master command executed | -- | `command.*` +0.1 | Bond communication |
| Master perceives dungeon state | -- | `perception.*` +0.05 | Bond communication |
| Master channels power | -- | `channeling.*` +0.2 | Bond communication |
| Significant memory stored | `memory_depth.capture` +0.5 | `perception.emotional` +0.1 (if master present) | Memory system |
| Memory manifested | `memory_depth.manifestation` +1.0 | `coordination.manifestation` +0.5 (if master guided) | Memory system |
| Adventurers defeated | `mana_reserves` +0.5, `genetic_library` +0.1 | `command.strategy` +0.3 (if master directed) | Analytics milestone |

---

## Dependencies (What This Plugin Relies On)

### Hard Dependencies (constructor injection -- crash if missing)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Dungeon core records (MySQL), bond records (MySQL), inhabitant state (Redis), memory records (MySQL), dungeon cache (Redis), distributed locks (Redis) |
| lib-state (`IDistributedLockProvider`) | Distributed locks for dungeon mutations, bond formation, spawn operations |
| lib-messaging (`IMessageBus`) | Publishing dungeon lifecycle events, memory events, bond events, inhabitant events |
| lib-messaging (`IEventConsumer`) | Registering handlers for domain-scoped combat, death, intrusion, and seed phase events |
| lib-seed (`ISeedClient`) | `dungeon_core` and `dungeon_master` seed type registration, growth recording, capability manifest queries (L2) |
| lib-currency (`ICurrencyClient`) | Mana wallet creation, credit/debit for spawn costs and trap charges (L2) |
| lib-contract (`IContractClient`) | Dungeon-master bond management -- creation, milestone tracking, termination (L1) |
| lib-actor (`IActorClient`) | Injecting perceptions into the bonded master's character Actor for indirect influence (L2) |
| lib-character (`ICharacterClient`) | Validating character existence for willing bond formation (L2) |
| lib-game-service (`IGameServiceClient`) | Validating game service existence for dungeon scoping (L2) |
| lib-resource (`IResourceClient`) | Reference tracking, cleanup callback registration (L1) |

### Soft Dependencies (runtime resolution via `IServiceProvider` -- graceful degradation)

| Dependency | Usage | Behavior When Missing |
|------------|-------|-----------------------|
| lib-mapping (`IMappingClient`) | Domain boundary registration, room connectivity queries, spatial affordance queries | Dungeon operates without spatial awareness; room-based features disabled |
| lib-scene (`ISceneClient`) | Memory manifestation as visual decorations (paintings, environmental effects) | Memory manifestation limited to item-based forms only |
| lib-save-load (`ISaveLoadClient`) | Persistent dungeon construction state (room layout, trap placement, structural data) | Dungeon layout resets on actor restart; volatile-only operation |
| lib-item (`IItemClient`) | Memory item creation (data crystals, memory fragments), loot generation | Memory items and loot spawning disabled |
| lib-inventory (`IInventoryClient`) | Trap/treasure container management within dungeon rooms | Container-based loot management disabled |
| lib-puppetmaster (`IPuppetmasterClient`) | Starting/stopping dungeon core actors on creation/deactivation | Dungeon actors must be managed manually via Actor APIs |
| lib-gardener (`IGardenerClient`) | Creating dungeon garden instances for bonded masters | Master experience orchestration disabled; bond provides seed growth only |
| lib-analytics (`IAnalyticsClient`) | Event significance scoring for memory capture thresholds | Memory capture uses local significance calculation only |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| *(none yet)* | Dungeon is a new L4 service with no current consumers. Future dependents: combat systems (subscribe to dungeon inhabitant events), Storyline (consume dungeon memory archives for narrative generation), Gardener (dungeon as garden type) |

---

## State Storage

### Dungeon Core Store
**Store**: `dungeon-cores` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `core:{dungeonId}` | `DungeonCoreModel` | Primary lookup by dungeon ID. Stores identity, personality type, status, seed references, economy references, core location, domain radius. |
| `core-code:{gameServiceId}:{code}` | `DungeonCoreModel` | Code-uniqueness lookup within game service scope |

### Bond Store
**Store**: `dungeon-bonds` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `bond:{bondId}` | `DungeonBondModel` | Primary lookup by bond ID. Stores contract reference, bond type (Priest/Paladin/Corrupted), master entity reference (type + ID), master seed ID, formation timestamp. |
| `bond-dungeon:{dungeonId}` | `DungeonBondModel` | Active bond lookup by dungeon (at most one active bond per dungeon) |
| `bond-master:{entityType}:{entityId}` | `DungeonBondModel` | Active bond lookup by master entity |

### Inhabitant Store
**Store**: `dungeon-inhabitants` (Backend: Redis, prefix: `dungeon:inhab`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `inhab:{dungeonId}:{inhabitantId}` | `InhabitantModel` | Individual monster/creature state: species, quality level, room location, stats, soul slot usage |
| `inhab-counts:{dungeonId}` | `InhabitantCountsModel` | Denormalized species count map for fast capability checks |

### Memory Store
**Store**: `dungeon-memories` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `mem:{dungeonId}:{memoryId}` | `DungeonMemoryModel` | Stored memory: event type, significance score, participants, location, outcome, emotional context, manifestation status |

### Dungeon Cache
**Store**: `dungeon-cache` (Backend: Redis, prefix: `dungeon:cache`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `cap:{dungeonId}` | `CachedCapabilityManifest` | Cached dungeon_core seed capability manifest for fast action gating |
| `mastercap:{dungeonId}` | `CachedCapabilityManifest` | Cached dungeon_master seed capability manifest for communication gating |
| `vitals:{dungeonId}` | `DungeonVitalsCache` | Cached volatile state: core integrity, current mana, mana generation rate, threat level |

### Distributed Locks
**Store**: `dungeon-lock` (Backend: Redis, prefix: `dungeon:lock`)

| Key Pattern | Purpose |
|-------------|---------|
| `core:{dungeonId}` | Dungeon core mutation lock (create, update, deactivate, delete) |
| `bond:{dungeonId}` | Bond formation/dissolution lock (one bond at a time) |
| `spawn:{dungeonId}` | Spawn operation lock (prevent concurrent mana overdraft) |
| `memory:{dungeonId}` | Memory capture/manifestation lock |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `dungeon.created` | `DungeonCreatedEvent` | Dungeon core entity created (lifecycle) |
| `dungeon.updated` | `DungeonUpdatedEvent` | Dungeon core entity updated (lifecycle) |
| `dungeon.deleted` | `DungeonDeletedEvent` | Dungeon core entity deleted (lifecycle) |
| `dungeon.bond.formed` | `DungeonBondFormedEvent` | Character or monster bonds with dungeon core |
| `dungeon.bond.dissolved` | `DungeonBondDissolvedEvent` | Bond terminated (master death, contract breach, voluntary) |
| `dungeon.inhabitant.spawned` | `DungeonInhabitantSpawnedEvent` | Monster spawned within dungeon domain |
| `dungeon.inhabitant.killed` | `DungeonInhabitantKilledEvent` | Monster killed within dungeon domain |
| `dungeon.memory.captured` | `DungeonMemoryCapturedEvent` | Significant event stored as dungeon memory |
| `dungeon.memory.manifested` | `DungeonMemoryManifestedEvent` | Memory crystallized into physical form (item, painting, environmental) |
| `dungeon.trap.triggered` | `DungeonTrapTriggeredEvent` | Trap activated within dungeon domain |
| `dungeon.layout.changed` | `DungeonLayoutChangedEvent` | Passage sealed/unsealed or structural shift occurred |
| `dungeon.phase.changed` | `DungeonPhaseChangedEvent` | Dungeon core seed transitioned to new growth phase |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `seed.phase.changed` | `HandleSeedPhaseChangedAsync` | For `dungeon_core` seeds: update cached phase, publish `dungeon.phase.changed`, re-evaluate available actions. For `dungeon_master` seeds: advance bond contract milestones. |
| `seed.capability.updated` | `HandleSeedCapabilityUpdatedAsync` | Invalidate cached capability manifests for affected dungeon or master |
| `contract.terminated` | `HandleContractTerminatedAsync` | Clean up bond record when dungeon-master contract ends; archive master seed if configured |

### Resource Cleanup (T28)

| Target Resource | Source Type | On Delete | Cleanup Endpoint |
|----------------|-------------|-----------|-----------------|
| character | dungeon | CASCADE | `/dungeon/cleanup-by-character` |
| realm | dungeon | CASCADE | `/dungeon/cleanup-by-realm` |

### DI Listener Patterns

| Pattern | Interface | Action |
|---------|-----------|--------|
| Seed evolution | `ISeedEvolutionListener` | Receives growth, phase change, and capability notifications for dungeon core and master seeds. Updates cached manifests. Writes to distributed state for multi-node safety. |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `CoreSeedTypeCode` | `DUNGEON_CORE_SEED_TYPE_CODE` | `dungeon_core` | Seed type code for dungeon core growth |
| `MasterSeedTypeCode` | `DUNGEON_MASTER_SEED_TYPE_CODE` | `dungeon_master` | Seed type code for dungeon master role growth |
| `ManaCurrencyCode` | `DUNGEON_MANA_CURRENCY_CODE` | `mana` | Currency code for mana economy within each game service |
| `BondContractTemplateCode` | `DUNGEON_BOND_CONTRACT_TEMPLATE_CODE` | `dungeon-master-bond` | Contract template code for master bonds |
| `MaxInhabitantsPerDungeon` | `DUNGEON_MAX_INHABITANTS_PER_DUNGEON` | `100` | Maximum concurrent creature instances per dungeon |
| `MaxMemoriesPerDungeon` | `DUNGEON_MAX_MEMORIES_PER_DUNGEON` | `500` | Maximum stored memories before oldest are pruned |
| `MemorySignificanceThreshold` | `DUNGEON_MEMORY_SIGNIFICANCE_THRESHOLD` | `0.5` | Minimum significance score for memory capture |
| `MemoryManifestationThreshold` | `DUNGEON_MEMORY_MANIFESTATION_THRESHOLD` | `0.8` | Minimum significance to queue for physical manifestation |
| `CapabilityCacheTtlSeconds` | `DUNGEON_CAPABILITY_CACHE_TTL_SECONDS` | `300` | TTL for cached seed capability manifests |
| `SpawnCostMultiplier` | `DUNGEON_SPAWN_COST_MULTIPLIER` | `1.0` | Global multiplier for monster spawn mana costs |
| `DefaultDomainRadius` | `DUNGEON_DEFAULT_DOMAIN_RADIUS` | `500.0` | Default domain radius (meters from core) for new dungeons |
| `DungeonGardenTypeCode` | `DUNGEON_GARDEN_TYPE_CODE` | `dungeon` | Garden type code registered with Gardener for master experience |
| `DistributedLockTimeoutSeconds` | `DUNGEON_DISTRIBUTED_LOCK_TIMEOUT_SECONDS` | `30` | Timeout for distributed lock acquisition |
| `MasterSeedArchiveOnDissolve` | `DUNGEON_MASTER_SEED_ARCHIVE_ON_DISSOLVE` | `true` | Whether to archive (true) or delete (false) master seeds on bond dissolution |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<DungeonService>` | Structured logging |
| `DungeonServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (creates 6 stores) |
| `IMessageBus` | Event publishing |
| `IDistributedLockProvider` | Distributed lock acquisition (L0) |
| `ISeedClient` | Seed type registration, growth recording, capability queries (L2) |
| `ICurrencyClient` | Mana wallet management (L2) |
| `IContractClient` | Bond contract lifecycle (L1) |
| `IActorClient` | Perception injection into master's character Actor (L2) |
| `ICharacterClient` | Character validation for willing bonds (L2) |
| `IGameServiceClient` | Game service validation (L2) |
| `IResourceClient` | Reference tracking, cleanup callbacks (L1) |
| `IServiceProvider` | Runtime resolution of soft L4 dependencies |

### Variable Provider Factories

| Factory | Namespace | Data Source | Registration |
|---------|-----------|-------------|--------------|
| `DungeonCoreSeedVariableProviderFactory` | `${seed.*}` | dungeon_core seed growth domains and capability manifest | `IVariableProviderFactory` (DI singleton) |
| `DungeonActorVariableProviderFactory` | `${dungeon.*}` | Volatile actor state (core integrity, mana, inhabitants, feelings, intruders) | `IVariableProviderFactory` (DI singleton) |
| `DungeonMasterSeedVariableProviderFactory` | `${master.seed.*}` | dungeon_master seed phases and capabilities (active when bonded) | `IVariableProviderFactory` (DI singleton) |
| `DungeonMasterCharacterVariableProviderFactory` | `${master.*}` | Master character data -- health, location, combat state (active when bonded to character) | `IVariableProviderFactory` (DI singleton) |

### ABML Action Handlers

| Handler | Action | Description |
|---------|--------|-------------|
| `SpawnMonsterHandler` | `spawn_monster:` | Create pneuma echo from genetic library, gated by `spawn_monster.*` capability |
| `ActivateTrapHandler` | `activate_trap:` | Trigger trap system, gated by `activate_trap.*` capability |
| `SealPassageHandler` | `seal_passage:` | Block/unblock passage, gated by `seal_passage` capability |
| `ShiftLayoutHandler` | `shift_layout:` | Minor structural change, gated by `shift_layout` capability |
| `EmitMiasmaHandler` | `emit_miasma:` | Adjust ambient mana density, gated by `emit_miasma` capability |
| `ManifestMemoryHandler` | `manifest_memory:` | Crystallize memory into physical form, gated by `manifest_memory` capability |
| `CommunicateMasterHandler` | `communicate_master:` | Send perception to bonded master's Actor, gated by master's perception capabilities |
| `SpawnEventAgentHandler` | `spawn_event_agent:` | Create encounter coordinator, gated by `spawn_event_agent` capability |

---

## API Endpoints (Implementation Notes)

### Dungeon Core Management (8 endpoints)

All endpoints require `developer` role.

- **Create** (`/dungeon/create`): Validates game service existence. Provisions mana currency wallet via `ICurrencyClient`, `dungeon_core` seed via `ISeedClient`. Personality type stored in seed metadata. Optionally starts dungeon core actor via `IPuppetmasterClient` (soft). Optionally persists initial layout via `ISaveLoadClient` (soft). Saves under both ID and code lookup keys.
- **Get** (`/dungeon/get`): Load from MySQL by dungeonId. Enriches with cached capability manifest if available.
- **GetByCode** (`/dungeon/get-by-code`): JSON query by gameServiceId + code.
- **List** (`/dungeon/list`): Paged JSON query with required gameServiceId filter, optional status, personality type, and growth phase filters.
- **Update** (`/dungeon/update`): Acquires distributed lock. Partial update. Publishes lifecycle updated event.
- **Activate** (`/dungeon/activate`): Lock, set status Active. Start dungeon core actor via Puppetmaster (soft). Publishes activation event.
- **Deactivate** (`/dungeon/deactivate`): Lock, set status Dormant. Stop actor via Puppetmaster (soft). Dissolve active bond if any. Publishes dormancy event.
- **Delete** (`/dungeon/delete`): Lock. Deactivate if active. Dissolve bond. Remove inhabitants. Delete memories. Coordinate cleanup via lib-resource. Delete record. Publishes lifecycle deleted event.

### Bond Management (4 endpoints)

All endpoints require `developer` role.

- **FormBond** (`/dungeon/bond/form`): Validates dungeon is Active and has no active bond. Validates master entity exists. Creates Contract instance from bond template with bond type terms (Priest/Paladin/Corrupted). Contract prebound API triggers `dungeon_master` seed creation for the master entity. Updates dungeon core's bond references. If master is a character and Gardener available, creates dungeon garden instance. Publishes `dungeon.bond.formed`.
- **DissolveBond** (`/dungeon/bond/dissolve`): Lock. Terminates Contract. Archives or deletes `dungeon_master` seed per `MasterSeedArchiveOnDissolve` config. Clears bond references. Destroys dungeon garden if active. Publishes `dungeon.bond.dissolved`.
- **GetBond** (`/dungeon/bond/get`): Returns active bond details for a dungeon, including master seed phase and capability summary.
- **GetBondByMaster** (`/dungeon/bond/get-by-master`): Lookup active bond by master entity type + ID.

### Inhabitant Management (4 endpoints)

All endpoints require `developer` role.

- **Spawn** (`/dungeon/inhabitant/spawn`): Validates dungeon `spawn_monster.*` seed capability for requested quality tier. Validates sufficient mana. Deducts spawn cost from mana wallet. Creates inhabitant record in Redis. Updates denormalized counts. Records seed growth to `genetic_library.{species}`. If master directed: records growth to master's `command.spawning`. Publishes `dungeon.inhabitant.spawned`.
- **Kill** (`/dungeon/inhabitant/kill`): Removes inhabitant. Credits mana from death (`mana_reserves.harvested` growth). Absorbs logos to `genetic_library.{species}`. Evaluates memory capture significance. Publishes `dungeon.inhabitant.killed`.
- **List** (`/dungeon/inhabitant/list`): Returns all inhabitants for a dungeon with optional species filter.
- **GetCounts** (`/dungeon/inhabitant/get-counts`): Returns denormalized species count map.

### Memory Management (4 endpoints)

All endpoints require `developer` role.

- **CaptureMemory** (`/dungeon/memory/capture`): Calculates significance score from event properties. If above `MemorySignificanceThreshold`: stores memory, records seed growth to `memory_depth.capture`. If master present: records growth to master's `perception.emotional`. If above `MemoryManifestationThreshold` and `manifest_memory` capability unlocked: queues for manifestation. Publishes `dungeon.memory.captured`.
- **ManifestMemory** (`/dungeon/memory/manifest`): Validates `manifest_memory` capability. Manifests as item (via `IItemClient`), scene decoration (via `ISceneClient`), or environmental effect (via `IMappingClient`) based on manifestation type. Records seed growth to `memory_depth.manifestation`. If master guided: records growth to master's `coordination.manifestation`. Publishes `dungeon.memory.manifested`.
- **ListMemories** (`/dungeon/memory/list`): Paged query with optional significance, event type, and manifestation status filters.
- **GetMemory** (`/dungeon/memory/get`): Load by memoryId.

### Domain Management (2 endpoints)

- **GetVitals** (`/dungeon/vitals`): Returns volatile dungeon state -- core integrity, current mana, mana generation rate, threat level, inhabitant summary, active bond summary.
- **GetDomainInfo** (`/dungeon/domain`): Returns domain boundaries, room count, extension core locations, active trap count. Queries Mapping (soft) for spatial details.

### Cleanup Endpoints (2 endpoints)

Resource-managed cleanup via lib-resource (per FOUNDATION TENETS):

- **CleanupByCharacter** (`/dungeon/cleanup-by-character`): Dissolves any bond where the character is the master. Removes character-specific memories.
- **CleanupByRealm** (`/dungeon/cleanup-by-realm`): Deactivates and deletes all dungeons in the realm. Cascades bond dissolution, inhabitant removal, memory deletion.

---

## Dungeon Cognition Pipeline

The dungeon core actor uses a simplified cognition pipeline (`creature_base` template) with fewer stages than the humanoid pipeline:

```
+-----------------------------------------------------------------------+
|                    DUNGEON COGNITION PIPELINE                          |
|                                                                        |
|   1. FILTER ATTENTION                                                  |
|   -------------------                                                  |
|   Priority: intrusion (10), combat (8), death (7), loot (5)           |
|   Threat fast-track: urgency > 0.8 bypasses to action                 |
|   Attention budget: configurable perceptions per tick                  |
|                                                                        |
|   2. MEMORY QUERY (simplified)                                         |
|   ----------------------------                                         |
|   "Have I seen these intruders before?"                                |
|   "What happened last time in this room?"                              |
|   Entity-indexed lookup for adventurer history                         |
|                                                                        |
|   3. CAPABILITY CHECK (via dungeon_core seed manifest)                 |
|   ----------------------------------------------------                 |
|   Query seed capability manifest for available actions                 |
|   Higher fidelity = better execution quality                           |
|   Unlocked capabilities gate which intentions can form                 |
|                                                                        |
|   4. MASTER COMMUNICATION CHECK (via dungeon_master seed manifest)     |
|   ----------------------------------------------------------------     |
|   If bonded: check master's perception.* capabilities                  |
|   Higher perception fidelity = richer information shared               |
|   If master has command.tactical: accept complex directives            |
|   If no bond or Corrupted bond: skip (dungeon acts autonomously)       |
|                                                                        |
|   5. INTENTION FORMATION                                               |
|   ----------------------                                               |
|   Apply master commands (if any, and if command capability allows)      |
|   Evaluate threats -> spawn defenders or activate traps                |
|   Evaluate opportunities -> absorb corpses, claim resources            |
|   Evaluate memories -> store significant events                        |
|   Record seed growth contributions for significant actions             |
|                                                                        |
|   SKIPPED STAGES (unlike humanoid_base):                               |
|   No significance assessment (dungeon cares about everything)          |
|   No goal impact evaluation (goals are fixed: survive, grow)           |
+-----------------------------------------------------------------------+
```

---

## Physical Construction

Dungeon physical form is a cross-service concern distributed across multiple services:

| Aspect | Service | Purpose |
|--------|---------|---------|
| **Spatial data** | Mapping (L4, soft) | Room boundaries, corridors, connectivity graph, affordance queries (e.g., "what objects can be thrown in room X?") |
| **Visual composition** | Scene (L4, soft) | Node trees for room decorations, memory manifestation paintings, environmental effects |
| **Persistent state** | Save-Load (L4, soft) | Versioned dungeon construction snapshots: room properties, trap placements, structural modifications |
| **Procedural generation** | lib-procedural (L4, future) | Houdini-backed generation of new chambers, corridors, and environmental features when domain_expansion capabilities are exercised |
| **Inhabitant tracking** | Dungeon (this service) | Monster/creature positions, species, quality, soul slot usage |

As the `dungeon_core` seed grows, `domain_expansion.*` capabilities unlock. When the dungeon core actor exercises `shift_layout` or expands its domain, the physical changes are:
1. Persisted via Save-Load (structural state)
2. Registered in Mapping (spatial index)
3. Composed in Scene (visual decorations)
4. Future: generated via lib-procedural (Houdini HDA execution for geometry)

**Deterministic generation**: When procedural generation is available, dungeon growth uses deterministic seeds (same growth parameters = identical chamber geometry), enabling reproducible dungeon layouts and Redis-cached generation results.

---

## Bond Types

| Bond Type | Master Entity | Relationship | Death Behavior | Master Seed Effect |
|-----------|--------------|-------------|----------------|--------------------|
| **Priest** | Character (willing) | Core provides mana, master provides direction | Bond and master seed growth preserved through master death | Full growth tracking; seed archived on dissolution, retains all growth |
| **Paladin** | Character (willing) | Core channels combat abilities through master | Master seed growth halved on death; bond can be re-formed | Full growth; `channeling.combat` domains grow faster |
| **Corrupted** | Monster (dominated) | Core dominates, master is avatar | Core dies if avatar destroyed | Minimal agency -- growth happens passively, monster cannot deliberately direct growth |

Bond formation is entirely Contract-driven. The contract template (`dungeon-master-bond`) includes:
- Party roles: `dungeon_core` (actor entity) and `dungeon_master` (character or actor entity)
- Bond type term (Priest/Paladin/Corrupted)
- Prebound API on contract creation: create `dungeon_master` seed for the bonded entity
- Milestones linked to master seed phase transitions (Bonded -> Attuned -> Symbiotic -> Transcendent)
- Enforcement mode: consequence-based

---

## Visual Aid

```
+-----------------------------------------------------------------------+
|                    DUNGEON STATE ARCHITECTURE                          |
|                                                                        |
|   ACTOR STATE              DUNGEON_CORE SEED     DUNGEON_MASTER SEED  |
|   (Volatile, Redis)        (Progressive, MySQL)   (Progressive, MySQL) |
|   +------------------+     +------------------+   +------------------+ |
|   | CoreIntegrity    |     | Growth Domains:  |   | Growth Domains:  | |
|   | CurrentMana      |     |  mana_reserves   |   |  perception      | |
|   | ManaGenRate  <---|-----|  genetic_lib.*   |   |  command         | |
|   | InhabitantCounts |     |  trap_complex.*  |   |  channeling      | |
|   | ActiveTraps      |     |  domain_exp.*    |   |  coordination    | |
|   | RoomHazardLevels |     |  memory_depth.*  |   |                  | |
|   | Feelings         |     |                  |   | Capabilities:    | |
|   | Memories --------|---->| Capabilities:    |   |  perception      | |
|   | ActiveIntruders  |     |  spawn_monster v |   |   .tactical v    | |
|   | BondContractId   |     |  shift_layout v  |   |  command         | |
|   | BondedMasterRef  |     |  manifest_mem v  |   |   .tactical v    | |
|   +------------------+     |  spawn_alpha x   |   |  channeling      | |
|                             |                  |   |   .combat v      | |
|        CONTRACT             | Phase: Awakened  |   |                  | |
|   +------------------+     | Metadata:        |   | Phase: Symbiotic | |
|   | Bond Type:       |     |  personality:    |   | Owner: character | |
|   |  Paladin         |     |   martial        |   +------------------+ |
|   | Death Clause     |     +------------------+                        |
|   | Power Sharing    |                                                 |
|   | Milestones:      |     DUNGEON GARDEN (Gardener)                   |
|   |  initial_bond    |     +------------------------------------+      |
|   |  attuned         |     | Garden type: dungeon               |      |
|   |  symbiotic       |     | Player: bonded master              |      |
|   |  transcendent    |     | Entities: character, inventory,    |      |
|   +------------------+     |   dungeon inhabitants, mana wallet  |      |
|                             | Tended by: dungeon core actor      |      |
|                             +------------------------------------+      |
|                                                                        |
|   PHYSICAL FORM                                                        |
|   +------+  +-------+  +---------+  +------------+                    |
|   |Mapping|  | Scene |  |Save-Load|  |Procedural  |                    |
|   |spatial|  |visual |  |persist  |  |(future)    |                    |
|   |index  |  |nodes  |  |layout   |  |Houdini gen |                    |
|   +------+  +-------+  +---------+  +------------+                    |
+-----------------------------------------------------------------------+
```

---

## Stubs & Unimplemented Features

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no generated code, no service implementation exists. The following phases are planned per [DUNGEON-AS-ACTOR.md](../planning/DUNGEON-AS-ACTOR.md):

### Phase 0: Seed Foundation (Prerequisite)
- Register `dungeon_core` seed type with growth phases, domains, and capability rules
- Register `dungeon_master` seed type with growth phases, domains, and capability rules
- Create `dungeon-master-bond` contract template with bond type variants

### Phase 1: Core Infrastructure (Actor + Seed Integration)
- Create dungeon-api.yaml schema with all endpoints
- Create dungeon-events.yaml schema
- Create dungeon-configuration.yaml schema
- Generate service code
- Implement dungeon core CRUD (create provisions seed + wallet + optionally actor)
- Implement variable provider factories for ABML expression access
- Implement ABML action handlers for dungeon capabilities

### Phase 2: Dungeon Master Bond (Contract + Master Seed)
- Implement bond formation flow (Contract creation triggers master seed creation)
- Implement bond communication (perception injection gated by master capabilities)
- Implement bond dissolution with seed archival
- Implement Corrupted bond variant for monster avatars

### Phase 3: Capabilities (Seed-Gated Actions)
- Implement spawn capabilities with quality tiers gated by seed
- Implement trap capabilities
- Implement event coordinator spawning
- Wire growth contributions for all capability executions

### Phase 4: Memory System
- Implement memory capture with significance scoring
- Implement memory manifestation (item, scene, environmental) gated by seed capability
- Implement personality-based manifestation style preferences
- Wire growth contributions for memory system events

### Phase 5: Physical Construction
- Integrate with Save-Load for persistent dungeon layout
- Integrate with Mapping for spatial domain registration
- Integrate with Scene for visual composition
- Future: integrate with lib-procedural for Houdini-based chamber generation

### Phase 6: Garden Integration
- Register dungeon garden type with Gardener
- Implement dungeon garden creation on bond formation
- Implement entity session registration for master's dungeon experience
- Create ABML gardener behavior for dungeon master experience orchestration

---

## Potential Extensions

1. **Mana as Currency wallet vs. virtual resource**: The dungeon_core seed tracks `mana_reserves` growth (long-term capacity), but volatile mana balance needs a home. Currency wallet enables NPC economic participation (dungeon trades with merchants for materials). Virtual resource in actor state is simpler but isolated from the economy.

2. **Mega-dungeon coordination**: Multiple dungeon cores in a mega-dungeon complex. Each core has its own `dungeon_core` seed and actor. Coordination via shared territory Contracts or a parent-child dungeon hierarchy.

3. **Cross-realm aberrant dungeons**: Dungeons that span realm boundaries. Requires design decisions about seed GameServiceId constraints and cross-realm actor communication.

4. **Guardian seed cross-pollination**: When a player character serves as dungeon master, combat/strategy experience should plausibly feed the guardian seed's domains. Configurable multiplier per seed type pair (default 0.0, enabled for dungeon_master -> guardian).

5. **Dungeon political integration**: Officially-sanctioned dungeons interact with faction/political systems. `dungeon_core` seed growth phases could map to political recognition tiers.

6. **Client events**: `dungeon-client-events.yaml` for pushing dungeon state changes (intrusion alerts, memory manifestations, bond communication) to the master's WebSocket client.

7. **Variable provider for Status**: NPCs inside a dungeon need to know dungeon-specific effects (`${dungeon.miasma_level}`, `${dungeon.room_hazard}`) for GOAP decision-making.

8. **Dungeon economy integration**: Dungeons as economic actors -- trading monster parts, selling access, purchasing materials for trap construction through the Currency/Escrow system.

9. **Procedural generation via Houdini**: When lib-procedural is implemented, dungeon growth (domain_expansion capabilities) triggers HDA execution to generate chamber geometry. Deterministic seeds enable cached, reproducible layouts. HDA templates define visual style; dungeon personality + parameters customize output.

---

## Known Quirks & Caveats

### Intentional Quirks (Documented Behavior)

1. **Personality type is opaque string**: Not an enum. Follows the same extensibility pattern as seed type codes, collection type codes, faction codes. Arcadia defines "martial", "memorial", "festive", "scholarly"; other games define their own. lib-dungeon stores whatever personality string is provided.

2. **Bond uniqueness is one-active-per-dungeon**: A dungeon core can only have one active bond (one master at a time). The master entity can also only master one dungeon at a time (`MaxPerOwner: 1` on `dungeon_master` seed). These constraints are enforced by both the seed system and the bond lookup keys.

3. **Master seed archival is configurable**: On bond dissolution, the `dungeon_master` seed is archived (preserving experience for future bonds) or deleted (every new bond starts fresh), controlled by `MasterSeedArchiveOnDissolve` config. Archival is the default because prior mastery experience creates richer gameplay (experienced masters are valuable to new dungeons).

4. **Corrupted bonds have minimal master agency**: When a monster serves as dungeon master, the monster has limited ability to deliberately grow its `dungeon_master` seed. Growth happens passively through the bond. This is intentional -- corrupted bonds represent domination, not partnership.

5. **No seed-to-seed bonds**: The dungeon_core and dungeon_master seeds are NOT bonded via the seed bond system (BondCardinality: 0). The Contract is the relationship mechanism. Seeds grow independently in parallel, connected by the Contract. This is deliberate -- the dungeon can outgrow its master (Ancient dungeon with Bonded master) or vice versa (Transcendent master of a Stirring dungeon), creating interesting asymmetric dynamics.

6. **Dungeon personality stored in seed metadata**: Personality type is a permanent characteristic stored in the `dungeon_core` seed's metadata at creation time, not in the dungeon core record. This follows the established pattern of seeds carrying permanent entity characteristics.

7. **Physical form is cross-service, not owned by lib-dungeon**: lib-dungeon owns identity, bond, inhabitants, and memories. Physical layout (rooms, corridors) is owned by Mapping + Save-Load. Visual appearance is owned by Scene. lib-dungeon orchestrates but does not store spatial or visual data directly.

### Design Considerations (Requires Planning)

1. **Mana economy model**: Should dungeons have their own Currency wallet (enabling NPC economic participation) or use `current_mana` as a virtual resource in actor state? Currency wallet is richer but adds complexity. The planning document leaves this open.

2. **Dungeon garden type design**: The dungeon-as-garden concept requires: a registered garden type in Gardener, entity association rules for the dungeon context, ABML action handlers for Gardener APIs (analogous to Puppetmaster's `spawn_watcher:`, `watch:` handlers), and a gardener behavior document for the dungeon core actor. This is a cross-service design effort.

3. **Actor type registration**: The dungeon core actor template (event_brain, category: dungeon_core, domain: "dungeon") needs to be registered in the Actor system. This includes the cognition template (`creature_base`), event subscriptions, and capability references. Design decisions: is `creature_base` an existing template or does it need creation?

4. **Growth contribution debouncing**: Dungeons generate many growth events per tick (every monster kill, every trap trigger). Growth contributions to lib-seed need debouncing (configurable, planning doc suggests 5000ms default) to avoid overwhelming the seed service with individual growth API calls.

5. **Memory-to-archive pipeline**: Dungeon memories should feed into the Content Flywheel -- when a dungeon is destroyed or goes dormant, its accumulated memories become generative input for Storyline. This requires design decisions about the compression/archive format and the handoff to lib-resource.

6. **Entity Session Registry for dungeon master**: The dungeon master's garden needs entity session registrations (dungeon -> session, inhabitants -> session, master character -> session) via the Entity Session Registry in Connect (L1). This depends on the Entity Session Registry being implemented first (see [Gardener Design #7](GARDENER.md)).

---

## Work Tracking

*No active work items. Plugin is in pre-implementation phase. See [DUNGEON-AS-ACTOR.md](../planning/DUNGEON-AS-ACTOR.md) for the full planning document.*
