# Genesis Plugin Deep Dive

> **Plugin**: lib-genesis (not yet created)
> **Schema**: `schemas/genesis-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: genesis-templates (MySQL), genesis-entities (MySQL), genesis-entity-cache (Redis), genesis-lock (Redis) — all planned
> **Layer**: GameFoundation
> **Status**: Aspirational — no schema, no generated code, no service implementation exists.
> **Planning**: [ACTOR-BOUND-ENTITIES.md](../planning/ACTOR-BOUND-ENTITIES.md)
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
Genesis receives notification
    ↓ Checks: is this wallet genesis-managed? → load entity → load template
    ↓ Applies growth mapping: amount × ratio → seed domain growth
    ↓ Calls Seed.RecordGrowth (internal — seed never exposed externally)
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

### ICurrencyTransactionListener (New DI Interface)

Defined in `bannou-service/Providers/`:

```
ICurrencyTransactionListener
    OnCurrencyCredited(walletId, currencyCode, amount, newBalance, cancellationToken)
    OnCurrencyDebited(walletId, currencyCode, amount, newBalance, cancellationToken)
```

Currency (L2) discovers implementations via `IEnumerable<ICurrencyTransactionListener>` and fires after any wallet mutation. Genesis implements this interface to apply growth mappings on credits. Follows the same DI Listener pattern as `ISeedEvolutionListener`, `ICollectionUnlockListener`, and `IItemInstanceDestructionListener`.

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
│   │   ├── minTotalGrowth       double
│   │   ├── cognitiveStage       CognitiveStage enum: Dormant | EventBrain | CharacterBrain
│   │   └── behaviorRef          string? (reference to pre-compiled ABML bytecode)
│   └── capabilityRules[]   capability definitions (code, domain, threshold, formula)
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
│       └── direction            TransactionDirection enum: Credit | Debit | Both
│
├── storage                 # Inventories the entity owns
│   └── inventories[]       list:
│       ├── inventoryCode        string (local reference, e.g., "loot", "memories", "traps")
│       ├── constraintModel      InventoryConstraintModel enum: Slot | Weight |
│       │                        Volumetric | Unlimited
│       ├── capacity             int?
│       └── categoryRestrictions string[]? (item category filters)
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

## Dependencies (What This Plugin Relies On)

### Hard Dependencies (constructor injection — crash if missing)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Template store (MySQL), entity store (MySQL), entity cache (Redis), distributed locks (Redis) |
| lib-state (`IDistributedLockProvider`) | Distributed locks for lifecycle transitions and entity mutations |
| lib-messaging (`IMessageBus`) | Publishing lifecycle events (entity created, phase changed, destroyed) |
| lib-seed (`ISeedClient`) | Seed type registration at startup, seed creation, growth recording, capability manifest queries (L2) |
| lib-currency (`ICurrencyClient`) | Wallet creation, balance queries (for `includeBalances` flag), autogain configuration (L2) |
| lib-character (`ICharacterClient`) | Character creation in system realm at Awakened phase, character validation (L2) |
| lib-actor (`IActorClient`) | Actor spawning at Stirring phase, character binding at Awakened phase (L2) |
| lib-inventory (`IInventoryClient`) | Container creation for entity storage (L2) |
| lib-item (`IItemClient`) | Physical form tracking and validation when entity is item-based (L2) |
| lib-collection (`ICollectionClient`) | Knowledge/experience tracking via Collection grants (L2) |
| lib-resource (`IResourceClient`) | Reference tracking, cleanup callback registration, archival for content flywheel (L1) |
| lib-relationship (`IRelationshipClient`) | Bond creation/dissolution for simple Relationship-based bonds (L2) |
| lib-game-service (`IGameServiceClient`) | Game service scoping validation (L2) |

### DI Listener Implementations (no event subscriptions)

| Interface | Source | Genesis Reaction |
|-----------|--------|-----------------|
| `ICurrencyTransactionListener` (new) | Currency (L2) | Look up wallet → entity → template → growth mapping. Record seed growth per ratio and direction filter. |
| `ISeedEvolutionListener` (existing) | Seed (L2) | Look up seed → entity → template. Handle cognitive stage transition: spawn actor (Stirring), create character + bind (Awakened). Publish `genesis.entity.phase-changed`. |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-dungeon (L4) | Creates genesis entities with template "dungeon_core". Subscribes to `genesis.entity.phase-changed` for dungeon-specific post-transition work (spatial domain registration, inhabitant store initialization, species creation). Stores `genesisEntityId` on DungeonCoreModel. |
| lib-divine (L4) | Creates genesis entities with template "deity_domain". Subscribes to `genesis.entity.phase-changed` for divine-specific post-transition work (attention slot initialization, follower management setup, divinity economy activation). |
| lib-actor (L2) | Discovers `GenesisVariableProviderFactory` via `IEnumerable<IVariableProviderFactory>` DI injection; creates genesis provider instances per entity for ABML behavior execution (`${genesis.*}` variables). |
| Game engine / SDK | Creates genesis entities for simple types (living weapons, treasure chests, haunted buildings). Credits wallets to drive growth. Queries entity state for gameplay interactions. |

---

## State Storage

### Template Store
**Store**: `genesis-templates` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `template:{templateCode}` | `GenesisTemplateModel` | Primary lookup by template code. Stores full template configuration including seed, economy, storage, awakening, and bond definitions. |
| `template-game:{gameServiceId}` | `GenesisTemplateListModel` | Templates registered for a game service (paginated query). |

### Entity Store
**Store**: `genesis-entities` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `entity:{entityId}` | `GenesisEntityModel` | Primary lookup by entity ID. Stores lifecycle state, all provisioned references (seedId internal, walletIds, inventoryIds), cognitive stage, actor/character IDs, physical form, bond, status. |
| `entity-code:{gameServiceId}:{realmId}:{code}` | `GenesisEntityModel` | Code-uniqueness lookup within game/realm scope. |
| `entity-template:{templateCode}:{realmId}` | `GenesisEntityListModel` | Entities by template and realm (paginated query). |
| `entity-wallet:{walletId}` | `GenesisEntityModel` | Reverse index: wallet → entity. Used by ICurrencyTransactionListener to look up genesis entity from a wallet credit event. |

### Entity Cache
**Store**: `genesis-entity-cache` (Backend: Redis, prefix: `genesis:cache`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `entity:{entityId}` | `CachedGenesisEntity` | Hot cache for entity lookups. TTL-based with event-driven invalidation. |
| `caps:{entityId}` | `CachedCapabilityManifest` | Cached seed capability manifest for fast capability checks and variable provider reads. |

### Distributed Locks
**Store**: `genesis-lock` (Backend: Redis, prefix: `genesis:lock`)

| Key Pattern | Purpose |
|-------------|---------|
| `transition:{entityId}` | Phase transition lock — serializes actor spawning and character creation. Prevents duplicate actors/characters in multi-node deployments. |
| `entity:{entityId}` | Entity mutation lock — create, update, destroy, bind-physical-form. |
| `bond:{entityId}` | Bond formation/dissolution lock. |

### Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `templateCode` (on entity and template) | B (Content Code) | Opaque string | Template codes ("dungeon_core", "sentient_weapon", "treasure_chest"); game-configurable, registered as seed data |
| `cognitiveStage` (on entity) | C (System State) | `CognitiveStage` enum | Finite set: `Dormant`, `EventBrain`, `CharacterBrain`. System-owned lifecycle states. |
| `physicalFormType` (on entity and template) | C (System State) | `PhysicalFormType` enum | Finite set: `Item`, `Location`, `None`. System-owned classification of what the entity is in the world. |
| `status` (on entity) | C (System State) | `GenesisEntityStatus` enum | Finite set: `Active`, `Dormant`, `Archived`. System-owned lifecycle states. |
| `direction` (on growth mapping) | C (System State) | `TransactionDirection` enum | Finite set: `Credit`, `Debit`, `Both`. Controls which currency transactions trigger growth. |
| `constraintModel` (on inventory definition) | C (System State) | `InventoryConstraintModel` enum | References Inventory's constraint model enum. |
| `cardinality` (on bond definition) | C (System State) | `BondCardinality` enum | Finite set: `None`, `OptionalOne`, `RequiredOne`, `Many`. |
| `walletCode`, `inventoryCode` (on entity walletIds/inventoryIds maps) | B (Content Code) | Opaque string | Local reference codes defined per template; game-configurable |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `genesis.template.created` | `GenesisTemplateCreatedEvent` | Template registered |
| `genesis.template.updated` | `GenesisTemplateUpdatedEvent` | Template configuration updated |
| `genesis.template.deprecated` | `GenesisTemplateDeprecatedEvent` | Template deprecated (Category B) |
| `genesis.entity.created` | `GenesisEntityCreatedEvent` | Entity created from template. Includes entityId, templateCode, all provisioned IDs (wallets, inventories). |
| `genesis.entity.updated` | `GenesisEntityUpdatedEvent` | Entity record updated (physical form bound, status changed, etc.) |
| `genesis.entity.deleted` | `GenesisEntityDeletedEvent` | Entity destroyed and archived |
| `genesis.entity.phase-changed` | `GenesisEntityPhaseChangedEvent` | Cognitive stage transition processed. Includes entityId, templateCode, phaseName, cognitiveStage, actorId (if spawned), characterId (if created). This is the primary event domain plugins subscribe to. |
| `genesis.entity.bond-created` | `GenesisEntityBondCreatedEvent` | Bond formed between entity and target |
| `genesis.entity.bond-dissolved` | `GenesisEntityBondDissolvedEvent` | Bond removed |

### Consumed Events

None. Genesis uses DI Listeners exclusively (`ICurrencyTransactionListener`, `ISeedEvolutionListener`), not event subscriptions. Both source services (Currency, Seed) are L2 and guaranteed co-located.

### Resource Cleanup (FOUNDATION TENETS)

| Target Resource | Source Type | On Delete | Cleanup Endpoint |
|----------------|-------------|-----------|-----------------|
| character | genesis | CASCADE | `/genesis/cleanup-by-character` |
| realm | genesis | CASCADE | `/genesis/cleanup-by-realm` |

### Compression Callback (via x-compression-callback)

| Resource Type | Source Type | Priority | Compress Endpoint | Decompress Endpoint |
|--------------|-------------|----------|-------------------|---------------------|
| character | genesis | 10 | `/genesis/get-compress-data` | `/genesis/restore-from-archive` |

Genesis entities linked to characters (via characterId at Awakened phase) are archived when the character is compressed. Priority 10 ensures genesis data is archived before domain-specific data that depends on it.

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
| `CleanupBatchSize` | `GENESIS_CLEANUP_BATCH_SIZE` | `100` | Number of entities to process per batch during cleanup (range: 10-1000) |

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

## Variable Provider: `${genesis.*}` Namespace

Implements `IVariableProviderFactory` (via `GenesisVariableProviderFactory`) providing entity state to Actor (L2) via the Variable Provider Factory pattern. Loads from the cached entity record and capability manifest.

### Entity State

| Variable | Type | Description |
|----------|------|-------------|
| `${genesis.templateCode}` | string | Template code of this entity |
| `${genesis.currentPhase}` | string | Current growth phase name |
| `${genesis.cognitiveStage}` | string | Current cognitive stage (Dormant, EventBrain, CharacterBrain) |
| `${genesis.entityId}` | Guid | Entity identifier |
| `${genesis.physicalFormType}` | string | Physical form type (Item, Location, None) |
| `${genesis.physicalFormId}` | Guid? | Physical form entity ID |

### Wallet State

| Variable | Type | Description |
|----------|------|-------------|
| `${genesis.wallet.<code>}` | double | Current balance of named wallet (triggers lazy eval on access) |
| `${genesis.wallet.<code>.cap}` | double | Maximum balance from template configuration |
| `${genesis.wallet.<code>.rate}` | double | Autogain base rate from template configuration |
| `${genesis.wallet.<code>.ratio}` | double | Balance as fraction of cap (0.0 = empty, 1.0 = full) |

### Inventory State

| Variable | Type | Description |
|----------|------|-------------|
| `${genesis.inventory.<code>.count}` | int | Number of items in named inventory |
| `${genesis.inventory.<code>.capacity}` | int | Maximum capacity from template configuration |
| `${genesis.inventory.<code>.full}` | bool | Whether inventory is at capacity |

### Capabilities

| Variable | Type | Description |
|----------|------|-------------|
| `${genesis.capability.<code>}` | bool | Whether entity has unlocked this capability |
| `${genesis.capability.count}` | int | Total number of unlocked capabilities |

### Bond State

| Variable | Type | Description |
|----------|------|-------------|
| `${genesis.bond.active}` | bool | Whether the entity has an active bond |
| `${genesis.bond.targetId}` | Guid? | Bond target entity ID |
| `${genesis.bond.targetType}` | string? | Bond target entity type |

### ABML Usage Examples

```yaml
flows:
  treasure_chest_behavior:
    # Should I generate loot?
    - cond:
      - when: "${genesis.wallet.mana.ratio > 0.8}"
        then:
          - call: signal_ready_for_harvest
          - set:
              glow_intensity: "${genesis.wallet.mana.ratio}"

      # Am I full of items already?
      - when: "${genesis.inventory.loot.full}"
        then:
          - call: enter_dormant_mode

  weapon_stirring_behavior:
    # Can I communicate with my wielder?
    - cond:
      - when: "${genesis.capability.active.impulse}"
        then:
          - call: send_danger_impulse
            when: "${world.nearby_hostiles > 0}"

      - when: "${genesis.capability.active.speak}"
        then:
          - call: whisper_to_wielder
            message: "danger_ahead"
```

---

## API Endpoints

### Template Management (5 endpoints)

Templates are Category B deprecation entities (persist forever, no delete).

- **RegisterTemplate** (`/genesis/template/register`): Registers a new genesis template. Validates seed domain/phase configuration, wallet codes, growth mapping references, and system realm existence. Idempotent by `templateCode`. Requires `developer` role.
- **GetTemplate** (`/genesis/template/get`): Returns template by code.
- **ListTemplates** (`/genesis/template/list`): Paginated listing with `includeDeprecated` filter (default: false).
- **UpdateTemplate** (`/genesis/template/update`): Updates template configuration. Does not affect existing entities (they snapshot template config at creation). Requires `developer` role.
- **DeprecateTemplate** (`/genesis/template/deprecate`): Category B deprecation with optional reason. Idempotent (returns OK if already deprecated). Prevents new entity creation from this template.
- **CleanDeprecated** (`/genesis/template/clean-deprecated`): Standard Category B sweep using `DeprecationCleanupHelper`. Admin role.

### Entity Lifecycle (5 endpoints)

- **CreateEntity** (`/genesis/entity/create`): Creates entity from template. Provisions seed, wallets (with autogain configuration), inventories, and resource references. Returns entity with all wallet and inventory IDs. Input: `templateCode`, `gameServiceId`, `realmId`, optional `code`, `displayName`.
- **GetEntity** (`/genesis/entity/get`): Returns entity state. `includeBalances: bool` (default from `IncludeBalancesDefault` config) triggers Currency queries for wallet balances when true.
- **ListEntities** (`/genesis/entity/list`): Paginated listing. Filters by `templateCode`, `realmId`, `cognitiveStage`, `status`, `currentPhase`.
- **GetCapabilities** (`/genesis/entity/get-capabilities`): Returns current seed capability manifest (passthrough to internal Seed query).
- **DestroyEntity** (`/genesis/entity/destroy`): Stops actor (if running), archives character (if awakened, and `archiveOnDestruction` is true), cleans up wallets/inventories/seed via Resource, publishes `genesis.entity.deleted`.

### Physical Form (1 endpoint)

- **BindPhysicalForm** (`/genesis/entity/bind-physical-form`): Associates entity with its physical form (item instance ID or location ID). Called after the physical form is created by the domain plugin or game engine. Validates form existence.

### Bond Management (3 endpoints)

Simple Relationship-based bonds only. Complex bonds (Dungeon's Contract-based dual mastery) stay in domain plugins.

- **CreateBond** (`/genesis/entity/create-bond`): Creates a Relationship between the entity and a target. Uses entity's `characterId` if awakened, or `entityId` reference if dormant. Validates entity state and bond cardinality from template. Input: `targetEntityType`, `targetEntityId`.
- **GetBond** (`/genesis/entity/get-bond`): Returns active bond for entity.
- **DissolveBond** (`/genesis/entity/dissolve-bond`): Removes bond (Relationship deletion). Publishes `genesis.entity.bond-dissolved`.

### Resource Cleanup (2 endpoints)

- **CleanupByCharacter** (`/genesis/cleanup-by-character`): Called by lib-resource during character deletion. Removes genesis entities where `characterId` matches. Cascades destruction (stops actors, cleans up wallets/inventories).
- **CleanupByRealm** (`/genesis/cleanup-by-realm`): Called by lib-resource during realm deletion. Removes all genesis entities in the realm.

### Compression (2 endpoints)

- **GetCompressData** (`/genesis/get-compress-data`): Returns `GenesisArchive` (extends `ResourceArchiveBase`) containing entity state snapshot, wallet balances, capability manifest, and growth progress for character archival.
- **RestoreFromArchive** (`/genesis/restore-from-archive`): Restores entity from archive. Re-provisions seed, wallets, and inventories. Does not restore actor/character (those are re-created when growth conditions are met again).

### Endpoint Permissions

All endpoints are internal-only (`x-permissions: []`) except:
- Template management: `x-permissions: [role: developer]`
- CleanDeprecated: `x-permissions: [role: admin]`
- RestoreFromArchive: `x-permissions: [role: admin]`

---

## Potential Extensions

- **Environment-modulated autogain rates**: Currency autogain provides the base rate, but environmental factors (leyline density, biome type, sacred site proximity) could modulate the effective rate. This could be implemented as a `IAutogainRateModifierProvider` DI interface that Genesis (or Currency) discovers, with Environment (L4) providing the modifier. Without Environment, the base rate applies unmodified.

- **Dungeon core as treasure chest spawner**: A dungeon core's ABML behavior could call `Genesis.CreateEntity(template: "treasure_chest")` to spawn treasure chests within its domain, then credit the chest's mana wallet from the dungeon's own mana reserves. The dungeon decides where to place treasure, how much mana to invest, and what loot tables to associate — all through ABML behavior, not service code.

- **Cross-entity mana transfer**: Genesis entities could transfer currency between wallets (a dungeon feeding mana to its treasure chests). This is already possible via Currency.Transfer — no Genesis extension needed. Noting here because it's a natural gameplay pattern.

- **Genesis entity as NPC economic node**: GOAP-driven NPCs could evaluate "is it worth visiting this chest?" by querying `${genesis.wallet.mana.ratio}` via the variable provider. A chest at 95% mana capacity is a known economic opportunity. Information about rich chests propagates through Hearsay, distorting as it spreads.

- **Template inheritance**: Templates could inherit from parent templates, overriding specific fields. A "cursed_treasure_chest" inherits from "treasure_chest" but overrides personality traits and behavior references. This avoids template duplication for variants.

- **Batch entity creation**: For scenarios where many entities are created at once (dungeon populating rooms with treasure), a batch creation endpoint could provision multiple entities in a single call, reducing round-trips.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*(None — no implementation exists yet.)*

### Intentional Quirks (Documented Behavior)

- **Seed is fully encapsulated**: The seedId never appears in any API response. Callers cannot call Seed APIs directly for genesis-managed seeds. All growth flows through currency wallet credits. This is a deliberate architectural choice: the seed is an implementation detail of the awakening lifecycle, not a public contract.

- **ICurrencyTransactionListener is local-only**: Genesis relies on DI listeners, not event subscriptions. This is safe because Currency and Genesis are both L2 and guaranteed co-located. If a future deployment mode separates L2 services across nodes, this would need to be revisited with event subscription as a fallback.

- **Template configuration is snapshot at entity creation**: Updating a template does not retroactively change existing entities. Each entity captures the template's seed domains, phases, wallet definitions, and growth mappings at creation time. This prevents configuration drift from affecting in-progress entity lifecycles.

- **Debit-driven growth is supported but unusual**: Growth mappings can specify `direction: Debit` or `direction: Both`, meaning spending currency can also drive growth. Example: a dungeon that grows stronger as it spends mana to spawn creatures (learning from the act of creation). This is intentionally supported but should be used with care to avoid feedback loops.

- **Actor spawning without Puppetmaster**: Genesis spawns actors directly via Actor (L2), bypassing Puppetmaster (L4). This means genesis-managed actors don't benefit from Puppetmaster's dynamic behavior hot-reload, processing pool management, or regional watcher coordination. This is correct: genesis-managed entities have fixed behaviors (per template phase) and are entity-bound, not task-bound.

- **No automatic growth for currency-less entities**: An entity whose template defines no wallets and no growth mappings will remain Dormant forever. This is valid (a purely inert entity record) but likely a template misconfiguration if the entity was intended to awaken.

### Design Considerations (Requires Planning)

- **ICurrencyTransactionListener interface scope**: The proposed interface fires on ALL currency transactions, not just genesis-managed wallets. Genesis must filter to its own wallets via the `entity-wallet:{walletId}` reverse index. At scale with many non-genesis wallets, this creates unnecessary listener invocations. Options: (a) accept the overhead (filter is a fast Redis lookup), (b) Currency provides a scoped listener registration mechanism, (c) Genesis uses event subscription with topic filtering instead of DI listener.

- **System realm provisioning**: Templates reference system realm codes (`DUNGEON_CORES`, `SENTIENT_ARMS`, `SENTIENT_CONTAINERS`). These realms must be seeded before entities can awaken. Genesis should validate realm existence at template registration time and provide clear error messages. Whether Genesis provisions the realms itself or requires them to be pre-seeded is a deployment design question.

- **Bond before awakening**: The bond endpoint needs to handle pre-awakened entities (no characterId yet). Options: (a) require awakening before bonding, (b) create a Relationship using the entityId with a custom reference pattern, (c) store the bond intent and establish the Relationship when the character is created. Option (c) is most flexible but adds complexity.

- **Multi-wallet growth mapping conflicts**: If multiple wallets map to the same seed domain, their growth contributions are additive. This is likely correct (a dungeon with both ambient and harvested mana growing `mana_reserves`), but should be documented and validated at template registration time to prevent accidental double-counting.

- **Concurrent ICurrencyTransactionListener processing**: If Currency's autogain worker credits many wallets rapidly, Genesis processes them sequentially through the DI listener. At 10K+ genesis entities with autogain, this could create back-pressure. The listener should be fast (Redis lookup + Seed.RecordGrowth) but needs performance profiling at scale.

---

## Work Tracking

*(No active work — aspirational service.)*
