# Genesis Plugin Deep Dive

> **Plugin**: lib-genesis
> **Schema**: schemas/genesis-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFoundation
> **State Store**: genesis-templates (MySQL), genesis-entities (MySQL), genesis-entity-cache (Redis), genesis-lock (Redis)
> **Planning**: [ACTOR-BOUND-ENTITIES.md](../planning/ACTOR-BOUND-ENTITIES.md)
> **Implementation Map**: [docs/maps/GENESIS.md](../maps/GENESIS.md)
> **Short**: Template-driven entity awakening lifecycle — seed, economy, storage, and cognitive progression for entities that grow from inert objects into autonomous agents

## Overview

Template-driven entity awakening lifecycle service (L2 GameFoundation) for managing entities that progressively grow from inert objects into autonomous agents with personalities, memories, and the full cognitive stack. Encapsulates the Actor-Bound Entity pattern (previously a documentation pattern across VISION.md and ACTOR-BOUND-ENTITIES.md) as reusable infrastructure: a single `CreateEntity` call provisions the seed, currency wallets, inventories, and resource registrations from a template definition, then manages the Dormant → EventBrain → CharacterBrain cognitive progression automatically as currency accumulates. Seed growth is driven entirely by currency transactions via template-defined growth mappings — the seed is an internal implementation detail never exposed to callers. Domain-specific plugins (lib-dungeon, lib-divine) sit on top for their ceremony; simple entity types (treasure chests, living weapons, haunted buildings, sentient ships) need no additional plugin. Game-agnostic: entity types, growth domains, currencies, behaviors, and awakening configurations are all template-defined seed data. Internal-only, never internet-facing.

---

## Core Mechanics: The Currency-Driven Awakening Lifecycle

Every genesis entity follows the same three-stage cognitive progression. What distinguishes Genesis from a documentation pattern is that the entire lifecycle — from creation through actor spawning through character binding — is driven by a single input signal: **currency wallet credits**.

### The Single Rule for Consumers

Want an entity to grow? **Credit its wallet.** That's it. Genesis handles everything else.

### The Growth Chain (DI Listeners, Zero Event Subscriptions)

```
Game engine / NPC / background worker credits a currency wallet
    ↓
Currency fires ICurrencyTransactionListener (DI, local — L2 co-located)
    ↓
Genesis listener: in-memory ConcurrentDictionary<walletId, WalletMapping> check
    ↓ Miss (not genesis-managed): return immediately (~microseconds)
    ↓ Hit: buffer credit in ConcurrentDictionary<entityId, GrowthAccumulator>
    ↓
GenesisGrowthFlushWorkerService (periodic, every GrowthFlushIntervalSeconds)
    ↓ Drain accumulator atomically
    ↓ Group by entityId
    ↓ Per entity: apply template growth mappings (amount × ratio, direction filter)
    ↓ Call Seed.RecordGrowthBatch once per entity (batched — NOT per-credit)
    ↓
Seed fires ISeedEvolutionListener (DI, local — L2 co-located)
    ↓
Genesis receives phase notification
    ↓ Acquires distributed lock
    ↓ At Stirring: spawns Actor, loads pre-compiled ABML behavior
    ↓ At Awakened: creates Character in system realm, binds Actor
    ↓ Publishes genesis.entity.phase-changed event
    ↓
Domain plugin (if any) subscribes to event, performs domain-specific work
```

The entire lifecycle from "resource accumulates" to "entity awakens" runs through DI listeners. Both `ICurrencyTransactionListener` and `ISeedEvolutionListener` are L2 interfaces with guaranteed co-location. Both listeners write to distributed state (MySQL entity records, Seed growth records), satisfying multi-node safety requirements.

**In-memory wallet map**: Genesis maintains a `ConcurrentDictionary<Guid, GenesisWalletMapping>` mapping `walletId → (entityId, templateCode, growthMappings[])`. Populated at startup from MySQL, updated on entity create/destroy, invalidated via self-subscription to `genesis.entity.created`/`genesis.entity.deleted` events for multi-node coherence. This is the same pattern as Currency's `ICurrencyDataCache` — in-memory, populated from state store, event-invalidated.

**Batched growth flush**: Multiple currency credits to the same entity between flush cycles are consolidated into a single `Seed.RecordGrowthBatch` call. This reduces Seed lock contention by orders of magnitude at scale (one lock acquisition per entity per flush, not per wallet credit). A 5-10 second flush interval is invisible for entities whose growth phases span hours/days of accumulated experience. The interval is configurable via `GrowthFlushIntervalSeconds`.

### ICurrencyTransactionListener (New DI Interface)

Defined in `bannou-service/Providers/`:

```
ICurrencyTransactionListener
    OnCurrencyCredited(walletId, currencyCode, amount, newBalance, cancellationToken)
    OnCurrencyDebited(walletId, currencyCode, amount, newBalance, cancellationToken)
```

Currency (L2) discovers implementations via `IEnumerable<ICurrencyTransactionListener>` and fires after any wallet mutation. Genesis implements this interface, but does NOT process growth synchronously — it checks an in-memory wallet map (~microseconds) and buffers matched credits for the growth flush worker. Follows the same DI Listener pattern as `ISeedEvolutionListener`, `ICollectionUnlockListener`, and `IItemInstanceDestructionListener`.

### The Three Cognitive Stages

#### Stage 1: Dormant (No Actor)

The entity exists as a genesis entity record with a seed and optional wallets/inventories. No actor is running. Growth accumulates passively via currency autogain or manual credits. System cost is near zero — a world can have thousands of dormant genesis entities.

#### Stage 2: Event Brain Actor (Stirring Phase)

When currency accumulation drives the seed past the Stirring growth threshold, Genesis spawns an Actor (L2) directly, loading the pre-compiled ABML behavior referenced by the template's phase configuration. The entity can now perceive, decide, and act autonomously.

ABML expressions like `${personality.*}` resolve to null — the behavior falls through to instinct-driven default paths. The entity has capabilities (from seed growth) but not a rich inner life.

**Actor spawning uses Actor (L2) directly, not Puppetmaster (L4).** Actor is the behavior execution runtime; it spawns and runs actors whether Puppetmaster exists or not. Pre-compiled ABML bytecode is loaded as seed data via existing L1/L2 infrastructure. The GOAP planner lives in the behavior-compiler SDK which Actor already depends on as a library. Genesis-managed entities are entity-bound (one actor per entity, lifetime-coupled), not task-bound (Puppetmaster's domain of dynamic reassignment, regional watchers, and processing pool orchestration).

#### Stage 3: Character Brain Actor (Awakened Phase)

When the seed reaches the Awakened threshold, Genesis:

1. Creates a **Character** in the template-specified system realm (L2)
2. Seeds personality traits from the template's `initialPersonalityTraits`
3. Calls **Actor.BindCharacter** — no actor relaunch needed (L2)
4. Stores `characterId` on the entity record

After binding, the full L2/L4 variable provider stack activates on the next behavior tick: `${personality.*}`, `${encounters.*}`, `${backstory.*}`, `${quest.*}`, `${world.*}`, `${obligations.*}`, and all domain-specific providers. The ABML behavior document is the same one used in Stage 2 — expressions that returned null now return real data.

### Why Genesis Owns Actor Spawning

| Concern | Genesis (L2) | Puppetmaster (L4) |
|---------|-------------|-------------------|
| **Purpose** | Entity awakening lifecycle | Network task orchestration |
| **Spawns actors for** | Objects becoming sentient | Regional watchers, god-actors, system tasks |
| **Behavior source** | Pre-compiled, seeded as configuration | Dynamic, loaded from Asset (L3), hot-reloadable |
| **Lifecycle driver** | Seed phase transitions (from currency growth) | Admin/startup/divine ABML decisions |
| **Actor management** | Entity-bound (one actor per entity, lifetime-coupled) | Task-bound (reassignable, scalable across pools) |

---

## The Template Model

Templates are seed data registered at startup. They define a type of awakening entity — what currencies it accumulates, how those currencies map to growth, what inventories it owns, and how it awakens.

```
GenesisTemplate
├── templateCode            string (unique, e.g., "dungeon_core", "sentient_weapon",
│                           "treasure_chest", "haunted_site")
├── displayName             string
├── description             string
│
├── seed                    # Progressive growth configuration
│   ├── seedTypeCode        string (registered with Seed service at startup)
│   ├── domains[]           list of domain definitions (name, optional subdomains)
│   ├── phases[]            ordered list:
│   │   ├── phaseName            string ("Dormant", "Stirring", "Awakened", "Ancient")
│   │   ├── threshold             double
│   │   ├── cognitiveStage       CognitiveStage enum: Dormant | EventBrain | CharacterBrain
│   │   └── behaviorRef          string? (reference to pre-compiled ABML bytecode)
│   └── capabilityRules[]   capability definitions (code, domain, threshold)
│
├── economy                 # Currency wallets and growth mappings
│   ├── wallets[]           list:
│   │   ├── walletCode           string (local reference, e.g., "mana", "experience")
│   │   ├── currencyCode         string (references Currency definition)
│   │   ├── autogainEnabled      bool
│   │   ├── autogainBaseRate     double? (units per game-time-second)
│   │   └── autogainCap          double? (maximum balance)
│   │
│   └── growthMappings[]    # Currency → seed growth coupling
│       ├── walletCode           string (→ wallet defined above)
│       ├── domain               string (→ seed domain defined above)
│       ├── ratio                double (currency amount × ratio = growth amount)
│       └── direction            GrowthDirection enum: Credit | Debit | Both
│
├── storage                 # Inventories the entity owns
│   └── inventories[]       list:
│       ├── inventoryCode        string (local reference, e.g., "loot", "memories", "traps")
│       ├── constraintModel      string (pass-through to Inventory's ContainerConstraintModel)
│       ├── capacity             int?
│       └── allowedCategories    string[]? (item category filters, passed to Inventory)
│
├── awakening               # Character creation configuration
│   ├── systemRealmCode          string ("DUNGEON_CORES", "SENTIENT_ARMS",
│   │                            "SENTIENT_CONTAINERS", etc.)
│   ├── characterSpeciesCode     string (species in that realm)
│   └── initialPersonalityTraits map<string, double>? (trait axis → starting value)
│
├── physicalFormType         PhysicalFormType enum: Item | Location | None
│
├── bond                    # Simple bond configuration (Relationship-based only)
│   ├── enabled              bool
│   ├── relationshipTypeCode string? (e.g., "weapon_wielder", "container_master")
│   └── cardinality          BondCardinality enum: None | OptionalOne | RequiredOne | Many
│
└── archiveOnDestruction     bool (default: true — feed content flywheel)
```

### Growth Mapping Examples

**Treasure chest** (passive mana accumulation drives growth):
```
wallets:
  - walletCode: "mana", currencyCode: "mana", autogainEnabled: true,
    autogainBaseRate: 0.01, autogainCap: 1000.0

growthMappings:
  - walletCode: "mana", domain: "mana_accumulated", ratio: 1.0, direction: Credit
  - walletCode: "mana", domain: "awareness",        ratio: 0.1, direction: Credit
```

**Living weapon** (combat experience credited by game engine):
```
wallets:
  - walletCode: "experience", currencyCode: "weapon_experience",
    autogainEnabled: false

growthMappings:
  - walletCode: "experience", domain: "combat_experience", ratio: 1.0, direction: Credit
  - walletCode: "experience", domain: "awakening",         ratio: 0.2, direction: Credit
```

**Dungeon core** (ambient mana via autogain + harvested mana from deaths):
```
wallets:
  - walletCode: "mana", currencyCode: "mana", autogainEnabled: true,
    autogainBaseRate: 0.005, autogainCap: 5000.0

growthMappings:
  - walletCode: "mana", domain: "mana_reserves.ambient",   ratio: 1.0, direction: Credit
  - walletCode: "mana", domain: "domain_expansion.radius",  ratio: 0.05, direction: Credit
```

---

## The Entity Model

Each created entity tracks all infrastructure Genesis provisioned on its behalf, plus lifecycle state. The seed is internal — never appears in API responses.

```
GenesisEntity (external-facing response)
├── entityId                Guid
├── templateCode            string (→ template)
├── gameServiceId           Guid
├── realmId                 Guid
├── code                    string? (human-readable within scope)
├── displayName             string?
│
├── # Wallet references (callers credit these to drive growth)
├── walletIds               map<string, Guid> (walletCode → walletId)
├── walletBalances          map<string, double>? (only when includeBalances=true)
│
├── # Inventory references (callers place/take items)
├── inventoryIds            map<string, Guid> (inventoryCode → inventoryId)
│
├── # Lifecycle state (read-only — driven by currency accumulation)
├── currentPhase            string
├── cognitiveStage          CognitiveStage enum: Dormant | EventBrain | CharacterBrain
├── actorId                 Guid? (populated at EventBrain)
├── characterId             Guid? (populated at CharacterBrain)
│
├── # Physical form
├── physicalFormType        PhysicalFormType enum: Item | Location | None
├── physicalFormId          Guid?
│
├── # Bond (if created via genesis bond endpoint)
├── bondId                  Guid?
├── bondTargetEntityType    EntityType?
├── bondTargetEntityId      Guid?
│
├── status                  GenesisEntityStatus enum: Active | Dormant | Archived
├── createdAt               DateTimeOffset
└── updatedAt               DateTimeOffset
```

The internal `GenesisEntityModel` additionally stores `seedId: Guid` for internal lifecycle management. This field is never serialized to API responses.

---

## How Domain Plugins Sit On Top

### Dungeon (Before Genesis)

```
CreateDungeon (6 cross-service calls):
  1. Create dungeon_core seed (Seed API)
  2. Create mana wallet (Currency API)
  3. Create trap inventory (Inventory API)
  4. Create memory inventory (Inventory API)
  5. Store all references on DungeonCoreModel
  6. Register resource cleanup callbacks
  7. Publish dungeon.created

HandleSeedPhaseChanged — Stirring (3-4 calls):
  8.  Spawn actor via Puppetmaster
  9.  Store actorId
  10. Register perception subscriptions
  11. Publish dungeon.phase-changed

HandleSeedPhaseChanged — Awakened (4-5 calls):
  12. Create Character in DUNGEON_CORES system realm
  13. Seed personality traits
  14. Call Actor.BindCharacter
  15. Store characterId
  16. Publish dungeon.phase-changed
```

### Dungeon (After Genesis)

```
CreateDungeon (2 cross-service calls):
  1. Genesis.CreateEntity(template: "dungeon_core", ...)
     → Genesis handles: seed, wallets, inventories, resource registration
  2. Store genesisEntityId + domain-specific fields on DungeonCoreModel
     (personality type, core location, domain radius)
  3. Genesis.BindPhysicalForm(entityId, Location, locationId)
  4. Publish dungeon.created

HandleGenesisPhaseChanged — Stirring (domain-specific only):
  5. Genesis already spawned the actor — actorId in event payload
  6. Register domain-scoped perception subscriptions
  7. Initialize volatile inhabitant store
  8. Register spatial domain boundary
  9. Publish dungeon.phase-changed

HandleGenesisPhaseChanged — Awakened (domain-specific only):
  10. Genesis already created character and bound actor
  11. Store characterId from genesis event
  12. Ensure dungeon species exists in system realm
  13. Configure environment per existing floors
  14. Publish dungeon.phase-changed
```

Creation collapses from 6 cross-service calls to 2. Phase transitions contain only domain-specific work.

### Living Weapon (No Domain Plugin)

```
1. God-actor decides a weapon is worthy of awakening
2. Item.CreateInstance (the weapon item)
3. Genesis.CreateEntity(template: "sentient_weapon", ...)
4. Genesis.BindPhysicalForm(entityId, Item, itemInstanceId)
5. Done — Genesis manages the entire lifecycle from here
```

The game engine credits the weapon's "experience" wallet when combat events occur. Genesis converts those credits to seed growth via the template mapping. When the weapon awakens, Genesis spawns its actor and eventually creates its character. No domain-specific plugin needed.

### Treasure Chest (No Domain Plugin — the "Growing Treasure" Pattern)

```
Setup (deployment):
  Register "mana" currency definition
  Register "treasure_chest" genesis template (mana wallet with autogain,
    loot inventory, growth mappings, phase definitions)
  Seed the SENTIENT_CONTAINERS system realm

Creation (runtime):
  1. Game engine / dungeon core creates the physical form (Item or Location)
  2. Genesis.CreateEntity(template: "treasure_chest", ...)
  3. Genesis.BindPhysicalForm(entityId, Item, chestItemId)
  4. Done — mana wallet accumulates via Currency autogain

Passive accumulation (automatic):
  Currency autogain worker → credits mana wallet
      ↓ ICurrencyTransactionListener
  Genesis → applies growth mapping → Seed.RecordGrowth (internal)
      ↓ ISeedEvolutionListener (if threshold crossed)
  Genesis → spawns actor / creates character / binds

Chest opened (game engine):
  1. Genesis.GetEntity(entityId, includeBalances: true)
     → { walletBalances: { mana: 342.0 }, cognitiveStage: EventBrain,
         capabilities: [loot.basic, loot.rare] }
  2. Check capabilities → select loot table accordingly
  3. Loot.Generate(table: "treasure-chest", context: { manaLevel: 342 })
  4. Place generated items in player inventory
  5. Currency.Debit(chestManaWalletId, 342)
     ↓ ICurrencyTransactionListener (debit, direction: Credit → no growth mapped)
     → no growth recorded (spending mana does not grow the entity)
```

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-dungeon (L4) | Creates genesis entities with template "dungeon_core". Subscribes to `genesis.entity.phase-changed` for dungeon-specific post-transition work (spatial domain registration, inhabitant store initialization, species creation). Stores `genesisEntityId` on DungeonCoreModel. |
| lib-divine (L4) | Creates genesis entities with template "deity_domain". Subscribes to `genesis.entity.phase-changed` for divine-specific post-transition work (attention slot initialization, follower management setup, divinity economy activation). |
| lib-actor (L2) | Discovers `GenesisVariableProviderFactory` via `IEnumerable<IVariableProviderFactory>` DI injection; creates genesis provider instances per entity for ABML behavior execution (`${genesis.*}` variables). |
| Game engine / SDK | Creates genesis entities for simple types (living weapons, treasure chests, haunted buildings). Credits wallets to drive growth. Queries entity state for gameplay interactions. |

---

## Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `templateCode` (on entity and template) | B (Content Code) | Opaque string | Template codes ("dungeon_core", "sentient_weapon", "treasure_chest"); game-configurable, registered as seed data |
| `cognitiveStage` (on entity) | C (System State) | `CognitiveStage` enum | Finite set: `Dormant`, `EventBrain`, `CharacterBrain`. System-owned lifecycle states. |
| `physicalFormType` (on entity and template) | C (System State) | `PhysicalFormType` enum | Finite set: `Item`, `Location`, `None`. System-owned classification of what the entity is in the world. |
| `status` (on entity) | C (System State) | `GenesisEntityStatus` enum | Finite set: `Active`, `Dormant`, `Archived`. System-owned lifecycle states. |
| `direction` (on growth mapping) | C (System State) | `GrowthDirection` enum | Finite set: `Credit`, `Debit`, `Both`. Controls which currency transactions trigger growth. |
| `constraintModel` (on inventory definition) | Pass-through | Opaque string | Stored in template, passed through to Inventory's CreateContainer. Genesis does not branch on this value. |
| `cardinality` (on bond definition) | C (System State) | `BondCardinality` enum | Finite set: `None`, `OptionalOne`, `RequiredOne`, `Many`. |
| `walletCode`, `inventoryCode` (on entity walletIds/inventoryIds maps) | B (Content Code) | Opaque string | Local reference codes defined per template; game-configurable |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `EntityCacheTtlMinutes` | `GENESIS_ENTITY_CACHE_TTL_MINUTES` | `5` | TTL for cached entity records (range: 1-60) |
| `CapabilityCacheTtlMinutes` | `GENESIS_CAPABILITY_CACHE_TTL_MINUTES` | `5` | TTL for cached capability manifests (range: 1-60) |
| `IncludeBalancesDefault` | `GENESIS_INCLUDE_BALANCES_DEFAULT` | `false` | Default value for `includeBalances` flag on GetEntity. Set to true if most callers need balances. |
| `TransitionLockTimeoutSeconds` | `GENESIS_TRANSITION_LOCK_TIMEOUT_SECONDS` | `30` | Timeout for phase transition distributed locks (range: 5-120) |
| `EntityLockTimeoutSeconds` | `GENESIS_ENTITY_LOCK_TIMEOUT_SECONDS` | `30` | Timeout for entity mutation distributed locks (range: 5-120) |
| `DefaultPageSize` | `GENESIS_DEFAULT_PAGE_SIZE` | `20` | Default page size for paginated queries (range: 1-100) |
| `ListOperationMaxRetries` | `GENESIS_LIST_OPERATION_MAX_RETRIES` | `3` | Maximum retry attempts for optimistic concurrency on string list index operations (range: 1-10) |
| `CleanupBatchSize` | `GENESIS_CLEANUP_BATCH_SIZE` | `100` | Number of entities to process per batch during cleanup (range: 10-1000) |
| `GrowthFlushIntervalSeconds` | `GENESIS_GROWTH_FLUSH_INTERVAL_SECONDS` | `5` | Interval between growth accumulator flush cycles. Lower = more responsive phase transitions but more Seed lock acquisitions. Higher = more efficient batching but more growth latency. At 5s default, entities growing over game-hours/days see no perceptible delay. (range: 1-60) |
| `StartupDelaySeconds` | `GENESIS_STARTUP_DELAY_SECONDS` | `5` | Delay before background services start processing after plugin startup (range: 0-120) |

---

## Visual Aid

```
┌─────────────────────────────────────────────────────────────────────────┐
│ Genesis Entity Lifecycle (Currency-Driven)                              │
│                                                                         │
│  CREATION                                                               │
│  ┌────────────────────────────────────────────────────────────────┐     │
│  │ Genesis.CreateEntity(template: "treasure_chest", ...)         │     │
│  │   → Seed.Create (internal seedId, never exposed)              │     │
│  │   → Currency.CreateWallet (mana wallet, autogain enabled)     │     │
│  │   → Inventory.CreateContainer (loot inventory, 20 slots)     │     │
│  │   → Resource.RegisterCleanup                                  │     │
│  │   → Store entity record with all references                   │     │
│  └────────────────────────────────────────────────────────────────┘     │
│                              │                                          │
│  ACCUMULATION                ▼                                          │
│  ┌────────────────────────────────────────────────────────────────┐     │
│  │ Currency autogain worker credits mana wallet every tick        │     │
│  │   ↓ ICurrencyTransactionListener (DI)                         │     │
│  │ Genesis: wallet → entity → template → growthMapping           │     │
│  │   ↓ mana × 1.0 ratio → Seed.RecordGrowth("mana_accumulated") │     │
│  │   ↓ mana × 0.1 ratio → Seed.RecordGrowth("awareness")        │     │
│  └────────────────────────────────────────────────────────────────┘     │
│                              │                                          │
│  TRANSITION                  ▼                                          │
│  ┌────────────────────────────────────────────────────────────────┐     │
│  │ Seed growth crosses phase threshold                            │     │
│  │   ↓ ISeedEvolutionListener (DI)                                │     │
│  │                                                                │     │
│  │ Stirring (EventBrain):                                        │     │
│  │   → Actor.Spawn(behaviorRef: "treasure-chest-stirring")       │     │
│  │   → Store actorId on entity                                   │     │
│  │   → Publish genesis.entity.phase-changed                      │     │
│  │                                                                │     │
│  │ Awakened (CharacterBrain):                                    │     │
│  │   → Character.Create(realm: SENTIENT_CONTAINERS)              │     │
│  │   → Actor.BindCharacter(actorId, characterId)                 │     │
│  │   → Store characterId on entity                               │     │
│  │   → Publish genesis.entity.phase-changed                      │     │
│  └────────────────────────────────────────────────────────────────┘     │
│                              │                                          │
│  RUNTIME                     ▼                                          │
│  ┌────────────────────────────────────────────────────────────────┐     │
│  │ Entity is alive. Actor runs ABML. ${genesis.*} variables      │     │
│  │ available. Currency continues accumulating. Capabilities       │     │
│  │ expand with growth. Domain plugin (if any) adds ceremony.     │     │
│  └────────────────────────────────────────────────────────────────┘     │
│                              │                                          │
│  DESTRUCTION                 ▼                                          │
│  ┌────────────────────────────────────────────────────────────────┐     │
│  │ Genesis.DestroyEntity(entityId)                                │     │
│  │   → Actor.Stop (if running)                                   │     │
│  │   → Character archive via Resource (if awakened)              │     │
│  │   → Cleanup wallets, inventories, seed                        │     │
│  │   → Publish genesis.entity.deleted                            │     │
│  │   → Content flywheel: archive feeds Storyline                 │     │
│  └────────────────────────────────────────────────────────────────┘     │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

1. **Growth flush worker (`GenesisGrowthFlushWorkerService`)**: Specified in the implementation map but not implemented. No `BackgroundService` class exists. Config properties `GrowthFlushIntervalSeconds` and `StartupDelaySeconds` are defined but unreferenced. The worker would drain the in-memory growth accumulator and batch-call `ISeedClient.RecordGrowthBatchAsync` per entity, reducing lock contention from one-per-credit to one-per-entity-per-flush.

2. **`ICurrencyTransactionListener` implementation**: Specified in the implementation map but no listener class exists. This is the microsecond-fast filtering path that checks an in-memory `ConcurrentDictionary<walletId, WalletMapping>` to determine whether a wallet credit/debit belongs to a genesis entity, then buffers the credit in the growth accumulator for the flush worker.

3. **`ISeedEvolutionListener` implementation**: Specified in the implementation map but no listener class exists. This handles the Dormant → EventBrain → CharacterBrain cognitive stage transitions: spawning actors, creating characters in system realms, binding actors to characters, and creating deferred bond Relationships.

4. **`IVariableProviderFactory` implementation (`GenesisVariableProviderFactory`)**: Specified in the implementation map but no provider class exists. Would provide `${genesis.*}` variables to the Actor runtime for ABML behavior execution (entity state, wallet balances, inventory counts, capabilities, bond state).

5. **Plugin lifecycle hooks (`OnStartAsync`, `OnRunningAsync`)**: `GenesisServicePlugin` is a bare `StandardServicePlugin<IGenesisService>` with no overrides. The map specifies startup wallet map population, seed type re-registration for pre-existing templates, and resource cleanup/compression callback registration during lifecycle hooks.

6. **Event handlers are no-ops**: `HandleGenesisEntityCreatedAsync` and `HandleGenesisEntityDeletedAsync` in `GenesisService.Events.cs` are registered but do nothing (just `await Task.CompletedTask` with a debug log). They are placeholders for the wallet map coherence logic that depends on stub #2 (ICurrencyTransactionListener).

---

## Potential Extensions

- **Environment-modulated autogain rates**: Currency autogain provides the base rate, but environmental factors (leyline density, biome type, sacred site proximity) could modulate the effective rate. This could be implemented as a `IAutogainRateModifierProvider` DI interface that Genesis (or Currency) discovers, with Environment (L4) providing the modifier. Without Environment, the base rate applies unmodified.

- **Dungeon core as treasure chest spawner**: A dungeon core's ABML behavior could call `Genesis.CreateEntity(template: "treasure_chest")` to spawn treasure chests within its domain, then credit the chest's mana wallet from the dungeon's own mana reserves. The dungeon decides where to place treasure, how much mana to invest, and what loot tables to associate — all through ABML behavior, not service code.

- **Cross-entity mana transfer**: Genesis entities could transfer currency between wallets (a dungeon feeding mana to its treasure chests). This is already possible via Currency.Transfer — no Genesis extension needed. Noting here because it's a natural gameplay pattern.

- **Genesis entity as NPC economic node**: GOAP-driven NPCs could evaluate "is it worth visiting this chest?" by querying `${genesis.wallet.mana.ratio}` via the variable provider. A chest at 95% mana capacity is a known economic opportunity. Information about rich chests propagates through Hearsay, distorting as it spreads.

- **Template inheritance**: Templates could inherit from parent templates, overriding specific fields. A "cursed_treasure_chest" inherits from "treasure_chest" but overrides personality traits and behavior references. This avoids template duplication for variants.

- **Batch entity creation**: For scenarios where many entities are created at once (dungeon populating rooms with treasure), a batch creation endpoint could provision multiple entities in a single call, reducing round-trips.

- **External seed adoption (nullable `seedId` on create)**: The `/genesis/entity/create` endpoint accepts an optional `seedId` parameter. If provided, Genesis adopts the externally-created seed instead of provisioning one internally. The caller retains the seedId and can interact with it directly via Seed APIs (including creating Seed bonds for growth propagation), while Genesis manages the lifecycle (growth mappings, phase transitions, cognitive progression) identically to internally-created seeds. Validation: seed type must match template's `seedTypeCode`, seed must not already be genesis-managed, initial phase computed from existing growth. This follows the same external-creation-then-adoption pattern as `bind-physical-form` for items/locations. Primary use case: Divine creates deity seeds externally so it can form Seed bonds between deity seeds and follower character seeds for divine growth propagation — see [DIVINITY-GENERATION-ARCHITECTURE.md](../planning/DIVINITY-GENERATION-ARCHITECTURE.md). Also useful for any domain plugin that needs direct Seed API access (bonds, transfers) on genesis-managed entities without breaking encapsulation from Genesis's API perspective.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. ~~**Clean-deprecated sweep does not publish `genesis.template.deleted` event**~~: **FIXED** (2026-03-19) — Added `PublishTemplateDeletedAsync` call with full `TemplateDeletedEvent` construction in the `deleteAndPublishAsync` delegate of `CleanDeprecatedAsync`. The `*.deleted` lifecycle event is now published for each permanently removed deprecated template per IMPLEMENTATION TENETS (T31 B22).

2. ~~**CleanupByRealm does not batch — `CleanupBatchSize` config is dead**~~: **FIXED** (2026-03-19) — Refactored `CleanupByRealmAsync` to use `QueryPagedAsync` with `_configuration.CleanupBatchSize` per batch, always querying page 1 (entities are deleted as we go). `CleanupBatchSize` config is now referenced. Per IMPLEMENTATION TENETS (T21).

### Intentional Quirks (Documented Behavior)

- **Seed is fully encapsulated**: The seedId never appears in any Genesis API response. Callers cannot discover genesis-managed seedIds through Genesis. All growth flows through currency wallet credits. This is a deliberate architectural choice: the seed is an implementation detail of the awakening lifecycle, not a public contract. **Exception**: When the caller provides an external seedId via the nullable `seedId` on create (see Potential Extensions), the caller already holds the seedId because they created it — Genesis doesn't leak it, the caller brought it. This enables domain plugins (e.g., Divine) to create Seed bonds on genesis-managed seeds without Genesis exposing internal state.

- **Internally-created seeds use `OwnerType: Other, OwnerId: entityId`**: Genesis creates its encapsulated seeds with `EntityType.Other` and the genesis entityId as the owner. This is consistent across all entity types (items, locations, `PhysicalFormType.None`), available at creation time (before `BindPhysicalForm`), and invisible to external consumers. The standard `${seed.*}` variable provider queries by `OwnerType: Character` and would never find genesis seeds regardless — genesis entities use `${genesis.*}` for ABML variables instead. For externally-adopted seeds (nullable `seedId` on create), the caller's chosen ownerType is preserved; Genesis doesn't modify it.

- **ICurrencyTransactionListener is local-only with in-memory filtering**: Genesis relies on DI listeners, not event subscriptions. The listener checks an in-memory `ConcurrentDictionary<walletId, WalletMapping>` (~microseconds, no network I/O) and buffers matched credits for the periodic growth flush worker. This is safe because Currency and Genesis are both L2 and guaranteed co-located — the co-location is analogous to HFT firms co-locating with exchange servers for microsecond data delivery. Each node's in-memory wallet map is populated from the same MySQL state and invalidated via self-subscription events for multi-node coherence. If a future deployment mode separates L2 services across nodes, this would need to be revisited with event subscription as a fallback.

- **Growth is batched, not per-transaction**: The `GenesisGrowthFlushWorkerService` drains the growth accumulator every `GrowthFlushIntervalSeconds` (default: 5s) and calls `Seed.RecordGrowthBatch` once per entity. This means growth from multiple wallet credits within a flush interval is consolidated into a single Seed operation. Phase transitions are detected in the batched response and handled normally. The 5-10 second delay between wallet credit and seed growth recording is invisible for entities whose growth phases span hours/days of accumulated experience.

- **Template configuration is snapshot at entity creation**: Updating a template does not retroactively change existing entities. Each entity captures the template's seed domains, phases, wallet definitions, and growth mappings at creation time. This prevents configuration drift from affecting in-progress entity lifecycles.

- **Debit-driven growth is supported but unusual**: Growth mappings can specify `direction: Debit` or `direction: Both`, meaning spending currency can also drive growth. Example: a dungeon that grows stronger as it spends mana to spawn creatures (learning from the act of creation). This is intentionally supported but should be used with care to avoid feedback loops.

- **Bonds are deferred until awakening**: `CreateBond` stores bond intent on the Genesis entity record but does NOT create a Relationship in lib-relationship until the entity reaches CharacterBrain stage. Pre-awakening, `${genesis.bond.*}` variables (active, targetId, targetType) are available from the Genesis record — sufficient for `emit_perception:` targeting (RabbitMQ topic routing is not Relationship-dependent). At awakening, the Relationship is created automatically using the entity's new `characterId`. Calling `CreateBond` on an already-Awakened entity creates the Relationship immediately (no deferral). Consumers checking `bondId != null` can distinguish "bond exists as intent only" (`bondId == null`, bond fields populated) from "bond materialized as Relationship" (`bondId != null`).

- **Actor spawning without Puppetmaster**: Genesis spawns actors directly via Actor (L2), bypassing Puppetmaster (L4). This means genesis-managed actors don't benefit from Puppetmaster's dynamic behavior hot-reload, processing pool management, or regional watcher coordination. This is correct: genesis-managed entities have fixed behaviors (per template phase) and are entity-bound, not task-bound.

- **No automatic growth for currency-less entities**: An entity whose template defines no wallets and no growth mappings will remain Dormant forever. This is valid (a purely inert entity record) but likely a template misconfiguration if the entity was intended to awaken.

- **Genesis does not depend on Collection**: The ACTOR-BOUND-ENTITIES planning document describes Collection (lib-collection) as part of the overall actor-bound entity pattern for knowledge tracking (e.g., a living weapon earning "first_hundred_kills" or "boss_slayer" collection entries). However, Collection grants are made by the **game engine or domain plugins** — not by Genesis. Genesis does not inject `ICollectionClient` and has no reason to. The Collection → Seed growth pipeline runs entirely within Seed's own infrastructure: seed type definitions include `collectionGrowthMappings` that tie collection entry tags to growth domains, and Seed's `SeedCollectionUnlockListener` (implementing `ICollectionUnlockListener`) automatically translates collection unlocks into seed growth. Genesis is downstream of this — it receives `ISeedEvolutionListener` phase transition notifications from Seed, regardless of whether the growth that triggered the transition came from currency credits (Genesis's primary path), collection unlocks (Seed's built-in pipeline), or direct `RecordGrowth` API calls.

### Design Considerations (Requires Planning)

- ~~**Clean-deprecated `hasInstancesAsync` uses QueryAsync scan instead of reverse index**~~: **FIXED** (2026-03-20) — Migrated to standard reverse index pattern. Template→entity list maintained via `AddToStringListAsync` on entity create/restore and `RemoveFromStringListAsync` on entity deletion. `hasInstancesAsync` delegate now uses `HasStringListEntriesAsync` for O(1) checks. Added `ListOperationMaxRetries` config property (default: 3) for optimistic concurrency retries.

- ~~**ICurrencyTransactionListener interface scope**~~: **RESOLVED** — Genesis uses an in-memory `ConcurrentDictionary<walletId, WalletMapping>` for O(1) filtering (~microseconds, no network I/O), populated from MySQL at startup and invalidated via self-subscription events. Non-genesis wallets are discarded in microseconds. Matched credits are buffered in a growth accumulator, flushed periodically by `GenesisGrowthFlushWorkerService` which calls `Seed.RecordGrowthBatch` once per entity per flush interval. This eliminates both the filtering overhead concern (in-memory vs Redis) and the processing volume concern (batched vs per-transaction). At 100K wallets, the listener adds ~30ms of ConcurrentDictionary lookups per autogain tick (vs ~9 seconds with Redis lookups). The batched flush reduces Seed lock acquisitions from one-per-wallet-credit to one-per-entity-per-flush-interval, a 10-20x reduction in state store operations. See § Core Mechanics for the full design.

- ~~**Concurrent ICurrencyTransactionListener processing**~~: **RESOLVED** — The batched growth flush design eliminates this concern entirely. The DI listener itself is microsecond-fast (in-memory map check + buffer write to a ConcurrentDictionary). The heavy Seed work (lock acquisition, growth recording, phase checks) happens asynchronously in the flush worker, which processes all accumulated growth for each entity in a single `Seed.RecordGrowthBatch` call. Even at 100K wallets with 30K autogain-enabled, the listener adds negligible overhead to Currency's autogain cycle. See the resolved DC-1 and § Core Mechanics for the full design.

- ~~**Bond before awakening**~~: **RESOLVED** — Option (c): deferred Relationship creation. `CreateBond` at any cognitive stage stores bond intent on the Genesis entity record (`bondTargetEntityType`, `bondTargetEntityId`); `bondId` remains null (no Relationship yet). The `${genesis.bond.*}` variable provider (active, targetId, targetType) serves from the Genesis record and is available immediately at all stages — sufficient for pre-awakening communication via `emit_perception:` (RabbitMQ topic routing, not Relationship-dependent). At Awakened phase transition, Genesis creates the actual Relationship using `(characterId, Character) ↔ (bondTargetEntityId, bondTargetEntityType)` with the template's `relationshipTypeCode`, then sets `bondId`. The `${relationship.*}` variable provider activates on next tick with full relationship data (sentiment, bond depth). `DissolveBond` pre-awakened clears Genesis record fields; post-awakened also deletes the Relationship. Option (a) (require awakening) was ruled out because ACTOR-BOUND-ENTITIES specifies weapon wielder bonds at equip time, and Stirring-stage actors need `${genesis.bond.targetId}` for perception injection. Option (b) (physical form reference + migration) was ruled out because it creates temporary Relationships that get deleted and recreated at awakening, doesn't work for `PhysicalFormType.None` entities, and adds migration complexity to the transition flow. **Implementation**: Deferred bond storage is implemented in `CreateBondAsync`; awakening-time auto-creation is in Stub #3 (ISeedEvolutionListener).

---

## Work Tracking

*(All items from 2026-03-20 maintenance resolved — no active work.)*
