# Dungeon Implementation Map

> **Plugin**: lib-dungeon
> **Schema**: schemas/dungeon-api.yaml
> **Layer**: GameFeatures
> **Deep Dive**: [docs/plugins/DUNGEON.md](../plugins/DUNGEON.md)
> **Status**: Aspirational -- pseudo-code represents intended behavior, not verified implementation

---

## Summary Table

| Field | Value |
|-------|-------|
| Plugin | lib-dungeon |
| Layer | L4 GameFeatures |
| Endpoints | 25 (25 generated) |
| State Stores | dungeon-cores (MySQL), dungeon-bonds (MySQL), dungeon-inhabitants (Redis), dungeon-cache (Redis), dungeon-lock (Redis) |
| Events Published | 12 (dungeon.created, dungeon.updated, dungeon.deleted, dungeon.bond.formed, dungeon.bond.dissolved, dungeon.inhabitant.spawned, dungeon.inhabitant.killed, dungeon.memory.captured, dungeon.memory.manifested, dungeon.trap-triggered, dungeon.layout-changed, dungeon.phase-changed) |
| Events Consumed | 2 |
| Client Events | 0 |
| Background Services | 0 |

---

## State

**Store**: `dungeon-cores` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `core:{dungeonId}` | `DungeonCoreModel` | Primary dungeon core record: identity, personality type, status, seed/economy refs, core location, domain radius, characterId (null until Awakened) |
| `core-code:{gameServiceId}:{code}` | `DungeonCoreModel` | Code-uniqueness lookup within game service scope |

**Store**: `dungeon-bonds` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `bond:{bondId}` | `DungeonBondModel` | Primary bond record: contract ref, bond type, master entity ref, master seed ID, formation timestamp |
| `bond-dungeon:{dungeonId}` | `DungeonBondModel` | Active bond lookup by dungeon (at most one active bond per dungeon) |
| `bond-master:{entityType}:{entityId}` | `DungeonBondModel` | Active bond lookup by master entity |

**Store**: `dungeon-inhabitants` (Backend: Redis, prefix: `dungeon:inhab`)

Durability note: Inhabitants are intentionally volatile. Dungeon creatures are pneuma echoes — on Redis restart, the dungeon core actor reconstructs from seed capabilities and mana reserves.

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `inhab:{dungeonId}:{inhabitantId}` | `InhabitantModel` | Individual creature state: species, quality, room location, soul slot usage |
| `inhab-counts:{dungeonId}` | `InhabitantCountsModel` | Denormalized species count map for fast capability checks |

**Store**: `dungeon-cache` (Backend: Redis, prefix: `dungeon:cache`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `cap:{dungeonId}` | `CachedCapabilityManifest` | Cached dungeon_core seed capability manifest for action gating |
| `mastercap:{dungeonId}` | `CachedCapabilityManifest` | Cached dungeon_master seed capability manifest for communication gating |
| `vitals:{dungeonId}` | `DungeonVitalsCache` | Cached volatile state: core integrity, mana, generation rate, threat level |

**Store**: `dungeon-lock` (Backend: Redis, prefix: `dungeon:lock`)

| Key Pattern | Purpose |
|-------------|---------|
| `core:{dungeonId}` | Dungeon core mutation lock (create, update, activate, deactivate, delete) |
| `bond:{dungeonId}` | Bond formation/dissolution lock (one bond operation at a time) |
| `spawn:{dungeonId}` | Spawn operation lock (prevent concurrent mana overdraft) |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | 5 stores: dungeon-cores, dungeon-bonds, dungeon-inhabitants, dungeon-cache, dungeon-lock |
| lib-state (IDistributedLockProvider) | L0 | Hard | Core mutation, bond formation, spawn operation locks |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing 12 event topics |
| lib-messaging (IEventConsumer) | L0 | Hard | Registering genesis phase-changed and contract terminated handlers |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Span instrumentation for all async methods |
| lib-resource (IResourceClient) | L1 | Hard | Reference tracking and cleanup callback registration (character, realm, game-service targets) |
| lib-contract (IContractClient) | L1 | Hard | Bond contract creation from dungeon-master-bond template, milestone tracking, termination |
| lib-genesis (IGenesisClient) | L2 | Hard | Entity creation from per-personality templates, capability checks, entity destruction with Content Flywheel archival |
| lib-currency (ICurrencyClient) | L2 | Hard | Mana credit/debit for spawn costs and trap charges |
| lib-actor (IActorClient) | L2 | Hard | Perception injection into bonded master's character Actor |
| lib-character (ICharacterClient) | L2 | Hard | Character validation for willing bond formation |
| lib-game-service (IGameServiceClient) | L2 | Hard | Game service existence validation |
| lib-item (IItemClient) | L2 | Hard | Memory item creation, manifestation outputs, loot generation |
| lib-inventory (IInventoryClient) | L2 | Hard | Memory inventory access, trap/treasure container management |
| lib-collection (ICollectionClient) | L2 | Hard | Permanent dungeon_knowledge entries for memory capture |
| lib-mapping (IMappingClient) | L4 | Soft | Domain boundary registration, room connectivity queries (dungeon operates without spatial awareness if absent) |
| lib-scene (ISceneClient) | L4 | Soft | Memory manifestation as visual decorations (falls back to item-based forms if absent) |
| lib-save-load (ISaveLoadClient) | L4 | Soft | Persistent dungeon construction state (layout resets on actor restart if absent) |
| lib-gardener (IGardenerClient) | L4 | Soft | Creating dungeon garden instances for Pattern A bonded masters (master experience disabled if absent) |
| lib-analytics (IAnalyticsClient) | L4 | Soft | Event significance scoring for memory capture (local calculation if absent) |

**Notes**:
- No lib-puppetmaster dependency: Genesis handles actor spawning at Stirring via Actor L2 directly.
- No lib-seed dependency: Seed management is encapsulated by Genesis.
- Genesis is also aspirational (no schema/code yet). Dungeon cannot be built until Genesis is implemented.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `dungeon.created` | `DungeonCreatedEvent` | Create (x-lifecycle) |
| `dungeon.updated` | `DungeonUpdatedEvent` | Update, Activate, Deactivate (x-lifecycle with changedFields) |
| `dungeon.deleted` | `DungeonDeletedEvent` | Delete, CleanupByRealm, CleanupByGameService (x-lifecycle) |
| `dungeon.bond.formed` | `DungeonBondFormedEvent` | FormBond |
| `dungeon.bond.dissolved` | `DungeonBondDissolvedEvent` | DissolveBond, Deactivate, HandleContractTerminatedAsync, CleanupByCharacter |
| `dungeon.inhabitant.spawned` | `DungeonInhabitantSpawnedEvent` | Spawn |
| `dungeon.inhabitant.killed` | `DungeonInhabitantKilledEvent` | Kill |
| `dungeon.memory.captured` | `DungeonMemoryCapturedEvent` | CaptureMemory |
| `dungeon.memory.manifested` | `DungeonMemoryManifestedEvent` | ManifestMemory |
| `dungeon.trap-triggered` | `DungeonTrapTriggeredEvent` | ActivateTrapHandler (ABML action) |
| `dungeon.layout-changed` | `DungeonLayoutChangedEvent` | SealPassageHandler, ShiftLayoutHandler (ABML actions) |
| `dungeon.phase-changed` | `DungeonPhaseChangedEvent` | HandleGenesisPhaseChangedAsync (at Stirring, Awakened, Ancient transitions) |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `genesis.entity.phase-changed` | `HandleGenesisPhaseChangedAsync` | Filters to dungeon_core and dungeon_master template entities. At Stirring: registers domain perception subscriptions, initializes inhabitant store, registers Mapping boundary (soft). At Awakened: stores characterId on DungeonCoreModel, configures environment. For dungeon_master seeds: advances bond contract milestones. Invalidates cached capability manifests. Publishes dungeon.phase-changed. |
| `contract.terminated` | `HandleContractTerminatedAsync` | Filters to dungeon-master-bond contracts. Archives or deletes master seed per config. Clears bond records and indexes. Destroys dungeon garden (soft). Publishes dungeon.bond.dissolved. |

---

## DI Services

#### Constructor Dependencies

| Service | Role |
|---------|------|
| `ILogger<DungeonService>` | Structured logging |
| `ITelemetryProvider` | Telemetry span creation |
| `DungeonServiceConfiguration` | Typed configuration access (14 properties) |
| `IStateStoreFactory` | Creates 5 state stores in constructor |
| `IDistributedLockProvider` | Distributed locks for core, bond, spawn operations |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Registers genesis and contract event handlers |
| `IGenesisClient` | Entity creation/destruction via per-personality templates (L2) |
| `ICurrencyClient` | Mana credit/debit (L2) |
| `IContractClient` | Bond contract lifecycle (L1) |
| `IActorClient` | Perception injection into master's Actor (L2) |
| `ICharacterClient` | Character validation for willing bonds (L2) |
| `IGameServiceClient` | Game service validation (L2) |
| `IResourceClient` | Reference tracking, cleanup callbacks (L1) |
| `IItemClient` | Memory item creation, manifestation outputs (L2) |
| `IInventoryClient` | Memory inventory access, container management (L2) |
| `ICollectionClient` | Permanent knowledge grants (L2) |
| `IServiceProvider` | Runtime resolution of soft L3/L4 dependencies |

#### DI Interfaces Implemented by This Plugin

| Interface | Registered As | Direction | Consumer |
|-----------|---------------|-----------|----------|
| `IVariableProviderFactory` (×4) | Singleton | L4→L2 pull | Actor (L2) discovers `${seed.*}`, `${dungeon.*}`, `${master.seed.*}`, `${master.*}` providers |
| `ISeedEvolutionListener` | Singleton | L2→L4 push | Seed (L2) notifies Dungeon of growth/phase/capability events; Dungeon updates cached manifests in Redis |

Variable provider factories:

| Factory | Namespace | Data Source |
|---------|-----------|-------------|
| `DungeonCoreSeedVariableProviderFactory` | `${seed.*}` | dungeon_core seed growth domains and capability manifest |
| `DungeonActorVariableProviderFactory` | `${dungeon.*}` | Volatile actor state: core integrity, mana, inhabitants, feelings, intruders |
| `DungeonMasterSeedVariableProviderFactory` | `${master.seed.*}` | dungeon_master seed phases and capabilities |
| `DungeonMasterCharacterVariableProviderFactory` | `${master.*}` | Master character data: health, location, combat state |

ABML action handlers (registered as internal collection-injected singletons):

| Handler | Action | Description |
|---------|--------|-------------|
| `SpawnMonsterHandler` | `spawn_monster:` | Create pneuma echo from genetic library, gated by spawn_monster.* capability |
| `ActivateTrapHandler` | `activate_trap:` | Trigger trap system, gated by activate_trap.* capability |
| `SealPassageHandler` | `seal_passage:` | Block/unblock passage, gated by seal_passage capability |
| `ShiftLayoutHandler` | `shift_layout:` | Minor structural change, gated by shift_layout capability |
| `EmitMiasmaHandler` | `emit_miasma:` | Adjust ambient mana density, gated by emit_miasma capability |
| `ManifestMemoryHandler` | `manifest_memory:` | Crystallize memory into physical form, gated by manifest_memory capability |
| `CommunicateMasterHandler` | `communicate_master:` | Send perception to bonded master's Actor, gated by master perception capabilities |
| `SpawnEventAgentHandler` | `spawn_event_agent:` | Create encounter coordinator, gated by spawn_event_agent capability |

---

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| Create | POST /dungeon/create | generated | developer | core, core-code | dungeon.created |
| Get | POST /dungeon/get | generated | developer | - | - |
| GetByCode | POST /dungeon/get-by-code | generated | developer | - | - |
| List | POST /dungeon/list | generated | developer | - | - |
| Update | POST /dungeon/update | generated | developer | core, core-code | dungeon.updated |
| Activate | POST /dungeon/activate | generated | developer | core | dungeon.updated |
| Deactivate | POST /dungeon/deactivate | generated | developer | core, bond, bond-dungeon, bond-master, mastercap | dungeon.updated, dungeon.bond.dissolved |
| Delete | POST /dungeon/delete | generated | developer | core, core-code, bond*, inhab*, counts, cap, mastercap, vitals | dungeon.deleted, dungeon.bond.dissolved |
| FormBond | POST /dungeon/bond/form | generated | developer | bond, bond-dungeon, bond-master, core | dungeon.bond.formed |
| DissolveBond | POST /dungeon/bond/dissolve | generated | developer | bond, bond-dungeon, bond-master, mastercap, core | dungeon.bond.dissolved |
| GetBond | POST /dungeon/bond/get | generated | developer | - | - |
| GetBondByMaster | POST /dungeon/bond/get-by-master | generated | developer | - | - |
| Spawn | POST /dungeon/inhabitant/spawn | generated | [] | inhab, inhab-counts | dungeon.inhabitant.spawned |
| Kill | POST /dungeon/inhabitant/kill | generated | [] | inhab, inhab-counts | dungeon.inhabitant.killed |
| ListInhabitants | POST /dungeon/inhabitant/list | generated | developer | - | - |
| GetCounts | POST /dungeon/inhabitant/get-counts | generated | developer | - | - |
| CaptureMemory | POST /dungeon/memory/capture | generated | [] | (via ICollectionClient, IInventoryClient) | dungeon.memory.captured |
| ManifestMemory | POST /dungeon/memory/manifest | generated | [] | (via IInventoryClient, IItemClient) | dungeon.memory.manifested |
| ListMemories | POST /dungeon/memory/list | generated | developer | - | - |
| GetMemory | POST /dungeon/memory/get | generated | developer | - | - |
| GetVitals | POST /dungeon/vitals | generated | developer | - | - |
| GetDomainInfo | POST /dungeon/domain | generated | developer | - | - |
| CleanupByCharacter | POST /dungeon/cleanup-by-character | generated | [] | bond, bond-dungeon, bond-master, mastercap, core | dungeon.bond.dissolved |
| CleanupByRealm | POST /dungeon/cleanup-by-realm | generated | [] | (all keys per dungeon) | dungeon.deleted, dungeon.bond.dissolved |
| CleanupByGameService | POST /dungeon/cleanup-by-game-service | generated | [] | (all keys per dungeon) | dungeon.deleted, dungeon.bond.dissolved |

---

## Methods

### Create
POST /dungeon/create | Roles: [developer]

```
CALL IGameServiceClient.GetGameServiceAsync({ gameServiceId })          -> 404 if not found
READ core-code:{gameServiceId}:{code}                                   -> 409 if exists

LOCK dungeon-lock:core:{dungeonId}                                      -> 409 if fails
  CALL IGenesisClient.CreateEntityAsync({
    template: "dungeon_core:{personalityType}", gameServiceId, ... })
    // Genesis provisions: seed, mana wallet, memory inventory, trap inventory
  WRITE core:{dungeonId} <- DungeonCoreModel {
    dungeonId, gameServiceId, code, personalityType, status: Active,
    coreLocation, domainRadius: config.DefaultDomainRadius,
    genesisEntityId, characterId: null, actorId: null, bondId: null }
  WRITE core-code:{gameServiceId}:{code} <- same DungeonCoreModel
  CALL IResourceClient.RegisterResourceAsync({ resourceType: dungeon, resourceId: dungeonId })
  // Soft: persist initial layout
  IF ISaveLoadClient available
    CALL ISaveLoadClient.SaveAsync(layout)
  PUBLISH dungeon.created { full model }
RETURN (200, CreateDungeonResponse)
```

### Get
POST /dungeon/get | Roles: [developer]

```
READ core:{dungeonId}                                                   -> 404 if null
READ cap:{dungeonId}                                                    // enrich with cached capabilities (may be null)
RETURN (200, GetDungeonResponse { ...model, capabilities })
```

### GetByCode
POST /dungeon/get-by-code | Roles: [developer]

```
READ core-code:{gameServiceId}:{code}                                   -> 404 if null
RETURN (200, GetDungeonByCodeResponse)
```

### List
POST /dungeon/list | Roles: [developer]

```
QUERY dungeon-cores WHERE $.gameServiceId == gameServiceId
  [AND $.status == status (if provided)]
  [AND $.personalityType == personalityType (if provided)]
  ORDER BY createdAt DESC
  PAGED(page, pageSize)
RETURN (200, ListDungeonsResponse { dungeons, totalCount, page, pageSize })
```

### Update
POST /dungeon/update | Roles: [developer]

```
READ core:{dungeonId}                                                   -> 404 if null
LOCK dungeon-lock:core:{dungeonId}                                      -> 409 if fails
  READ core:{dungeonId} [with ETag]
  // Apply partial updates from request
  ETAG-WRITE core:{dungeonId} <- updated DungeonCoreModel               -> 409 if conflict
  IF code changed
    DELETE core-code:{gameServiceId}:{oldCode}
    WRITE core-code:{gameServiceId}:{newCode} <- updated model
  PUBLISH dungeon.updated { changedFields: [...] }
RETURN (200, UpdateDungeonResponse)
```

### Activate
POST /dungeon/activate | Roles: [developer]

```
READ core:{dungeonId}                                                   -> 404 if null
IF status == Active                                                     -> RETURN (200, response)  // idempotent
LOCK dungeon-lock:core:{dungeonId}                                      -> 409 if fails
  READ core:{dungeonId} [with ETag]
  ETAG-WRITE core:{dungeonId} <- model { status: Active }               -> 409 if conflict
  PUBLISH dungeon.updated { changedFields: ["status"] }
RETURN (200, ActivateDungeonResponse)
```

### Deactivate
POST /dungeon/deactivate | Roles: [developer]

```
READ core:{dungeonId}                                                   -> 404 if null
IF status == Dormant                                                    -> RETURN (200, response)  // idempotent
LOCK dungeon-lock:core:{dungeonId}                                      -> 409 if fails
  // Dissolve active bond if any
  READ bond-dungeon:{dungeonId}
  IF bond exists
    CALL IContractClient.TerminateContractAsync({ contractId })
    IF config.MasterSeedArchiveOnDissolve
      // Archive master seed via Genesis
    ELSE
      // Delete master seed via Genesis
    IF IGardenerClient available
      CALL IGardenerClient.DestroyGardenAsync(...)
    DELETE bond:{bondId}
    DELETE bond-dungeon:{dungeonId}
    DELETE bond-master:{entityType}:{entityId}
    DELETE mastercap:{dungeonId}
    PUBLISH dungeon.bond.dissolved { reason: "deactivation" }
  READ core:{dungeonId} [with ETag]
  ETAG-WRITE core:{dungeonId} <- model { status: Dormant, bondId: null } -> 409 if conflict
  PUBLISH dungeon.updated { changedFields: ["status"] }
RETURN (200, DeactivateDungeonResponse)
```

### Delete
POST /dungeon/delete | Roles: [developer]

```
READ core:{dungeonId}                                                   -> 404 if null
LOCK dungeon-lock:core:{dungeonId}                                      -> 409 if fails
  // 1. Deactivate if active (inline: dissolve bond, stop actor)
  IF status == Active
    // (see Deactivate logic inline)

  // 2. Remove all inhabitants
  FOREACH inhabitant in inhab:{dungeonId}:*
    DELETE inhab:{dungeonId}:{inhabitantId}
  DELETE inhab-counts:{dungeonId}

  // 3. Clear cache entries
  DELETE cap:{dungeonId}
  DELETE mastercap:{dungeonId}
  DELETE vitals:{dungeonId}

  // 4. Destroy genesis entity (handles seed, wallets, inventories, actor, character archival)
  CALL IGenesisClient.DestroyEntityAsync({ genesisEntityId })

  // 5. Coordinate lib-resource cleanup
  CALL IResourceClient.ExecuteCleanupAsync({ resourceType: dungeon, resourceId: dungeonId })

  // 6. Delete records
  DELETE core:{dungeonId}
  DELETE core-code:{gameServiceId}:{code}

  PUBLISH dungeon.deleted { full model }
  // NOTE: Design Consideration #9 — multi-service compensation for partial failure
  // in this 7+ step orchestration is UNRESOLVED
RETURN (200, DeleteDungeonResponse)
```

### FormBond
POST /dungeon/bond/form | Roles: [developer]

```
READ core:{dungeonId}                                                   -> 404 if null
IF status != Active                                                     -> 409
READ bond-dungeon:{dungeonId}                                           -> 409 if not null (already bonded)
READ bond-master:{entityType}:{entityId}                                -> 409 if not null (master already bonded)
IF masterEntityType == character
  CALL ICharacterClient.GetCharacterAsync({ characterId })              -> 404 if not found

LOCK dungeon-lock:bond:{dungeonId}                                      -> 409 if fails
  CALL IContractClient.CreateContractAsync({
    templateCode: config.BondContractTemplateCode,
    parties: [dungeon_core actor, dungeon_master entity],
    bondType })
    // Contract prebound API triggers dungeon_master seed creation (character-owned, Pattern B)
  WRITE bond:{bondId} <- DungeonBondModel {
    bondId, dungeonId, contractId, bondType,
    masterEntityType, masterEntityId, masterSeedId, formedAt: now }
  WRITE bond-dungeon:{dungeonId} <- same
  WRITE bond-master:{entityType}:{entityId} <- same
  READ core:{dungeonId} [with ETag]
  ETAG-WRITE core:{dungeonId} <- model { bondId }                       -> 409 if conflict
  PUBLISH dungeon.bond.formed { bondId, dungeonId, masterEntityType, masterEntityId, bondType }
RETURN (200, FormBondResponse)
```

### DissolveBond
POST /dungeon/bond/dissolve | Roles: [developer]

```
READ bond:{bondId}                                                      -> 404 if null
LOCK dungeon-lock:bond:{bond.dungeonId}                                 -> 409 if fails
  CALL IContractClient.TerminateContractAsync({ contractId })
  IF config.MasterSeedArchiveOnDissolve
    // Archive master seed via Genesis (preserves growth)
  ELSE
    // Delete master seed via Genesis
  IF IGardenerClient available
    CALL IGardenerClient.DestroyGardenAsync(...)
  DELETE bond:{bondId}
  DELETE bond-dungeon:{bond.dungeonId}
  DELETE bond-master:{bond.masterEntityType}:{bond.masterEntityId}
  DELETE mastercap:{bond.dungeonId}
  READ core:{bond.dungeonId} [with ETag]
  ETAG-WRITE core:{bond.dungeonId} <- model { bondId: null }            -> 409 if conflict
  PUBLISH dungeon.bond.dissolved { bondId, dungeonId, reason }
RETURN (200, DissolveBondResponse)
```

### GetBond
POST /dungeon/bond/get | Roles: [developer]

```
READ bond-dungeon:{dungeonId}                                           -> 404 if null
READ mastercap:{dungeonId}                                              // enrich with master capabilities
RETURN (200, GetBondResponse { bond, masterSeedPhase, capabilitySummary })
```

### GetBondByMaster
POST /dungeon/bond/get-by-master | Roles: [developer]

```
READ bond-master:{entityType}:{entityId}                                -> 404 if null
RETURN (200, GetBondByMasterResponse)
```

### Spawn
POST /dungeon/inhabitant/spawn | Roles: []

```
READ core:{dungeonId}                                                   -> 404 if null
READ cap:{dungeonId}                                                    -> check spawn_monster.{tier} capability
IF !capability unlocked                                                 -> RETURN (400, null)
READ inhab-counts:{dungeonId}
IF total >= config.MaxInhabitantsPerDungeon                             -> RETURN (409, null)

LOCK dungeon-lock:spawn:{dungeonId}                                     -> 409 if fails
  CALL ICurrencyClient.GetWalletAsync({ walletId })                     -> verify mana
  // spawnCost = baseCost * config.SpawnCostMultiplier
  IF wallet.balance < spawnCost                                         -> RETURN (409, null)
  CALL ICurrencyClient.DebitAsync({ walletId, amount: spawnCost })
  WRITE inhab:{dungeonId}:{inhabitantId} <- InhabitantModel {
    inhabitantId, dungeonId, species, qualityTier, roomLocation }
  READ inhab-counts:{dungeonId} [with ETag]
  ETAG-WRITE inhab-counts:{dungeonId} <- counts { total++, species++ }
  // Record seed growth
  CALL IGenesisClient.RecordGrowthAsync({ domain: "genetic_library.{species}" })
  IF masterDirected
    CALL IGenesisClient.RecordGrowthAsync({ seedId: bond.masterSeedId, domain: "command.spawning", amount: 0.1 })
  PUBLISH dungeon.inhabitant.spawned { inhabitantId, dungeonId, species, qualityTier }
RETURN (200, SpawnInhabitantResponse)
```

### Kill
POST /dungeon/inhabitant/kill | Roles: []

```
READ inhab:{dungeonId}:{inhabitantId}                                   -> 404 if null
READ core:{dungeonId}                                                   -> 404 if null
CALL ICurrencyClient.CreditAsync({ walletId, amount, reason: "inhabitant_death" })
CALL IGenesisClient.RecordGrowthAsync({ domain: "mana_reserves.harvested" })
CALL IGenesisClient.RecordGrowthAsync({ domain: "genetic_library.{species}" })
// Evaluate memory capture significance
IF significanceScore >= config.MemorySignificanceThreshold
  // Trigger memory capture inline or via self-call
DELETE inhab:{dungeonId}:{inhabitantId}
READ inhab-counts:{dungeonId} [with ETag]
ETAG-WRITE inhab-counts:{dungeonId} <- counts { total--, species-- }
PUBLISH dungeon.inhabitant.killed { inhabitantId, dungeonId, species, killedBy }
RETURN (200, KillInhabitantResponse)
```

### ListInhabitants
POST /dungeon/inhabitant/list | Roles: [developer]

```
READ core:{dungeonId}                                                   -> 404 if null
// Redis prefix scan: inhab:{dungeonId}:*
// Optional species filter
RETURN (200, ListInhabitantsResponse { inhabitants, total })
```

### GetCounts
POST /dungeon/inhabitant/get-counts | Roles: [developer]

```
READ inhab-counts:{dungeonId}                                           -> 404 if null
RETURN (200, GetInhabitantCountsResponse { countsBySpecies, total })
```

### CaptureMemory
POST /dungeon/memory/capture | Roles: []

```
READ core:{dungeonId}                                                   -> 404 if null
// Calculate significance
IF IAnalyticsClient available
  CALL IAnalyticsClient.ScoreEventAsync(eventData) -> significanceScore
ELSE
  significanceScore = localCalculation(eventData)
IF significanceScore < config.MemorySignificanceThreshold
  RETURN (200, { captured: false })

// 1. Grant permanent Collection entry
CALL ICollectionClient.GrantEntryAsync({
  collectionType: "dungeon_knowledge", ownerId, ownerType, entryCode })
// 2. Create memory Item instance in dungeon's memories inventory
CALL IItemClient.CreateItemInstanceAsync({
  templateCode: "dungeon_memory",
  customStats: { significance, eventType, participants, emotionalContext } })
CALL IInventoryClient.AddItemAsync({ inventoryId, itemInstanceId })
// 3. Record seed growth
CALL IGenesisClient.RecordGrowthAsync({ domain: "memory_depth.capture", amount: 0.5 })
// 4. Master growth if present
IF masterPresent AND activeBond
  READ bond-dungeon:{dungeonId}
  CALL IGenesisClient.RecordGrowthAsync({ seedId: bond.masterSeedId, domain: "perception.emotional", amount: 0.1 })
// 5. Queue for manifestation if above threshold and capable
IF significanceScore >= config.MemoryManifestationThreshold
  READ cap:{dungeonId}
  IF capability "manifest_memory" unlocked
    // Mark memory item for manifestation
PUBLISH dungeon.memory.captured { dungeonId, memoryItemInstanceId, significanceScore, eventType }
RETURN (200, CaptureMemoryResponse)
```

### ManifestMemory
POST /dungeon/memory/manifest | Roles: []

```
READ core:{dungeonId}                                                   -> 404 if null
READ cap:{dungeonId}
IF !capability "manifest_memory"                                        -> RETURN (400, null)
CALL IInventoryClient.GetItemAsync({ inventoryId, itemInstanceId })     -> 404 if not found
// Consume the memory item from inventory
CALL IInventoryClient.RemoveItemAsync({ inventoryId, itemInstanceId })
// Create manifestation based on type
IF manifestationType == "item"
  CALL IItemClient.CreateItemInstanceAsync({ templateCode: "memory_artifact" })
  CALL IInventoryClient.AddItemAsync(destination, outputItemId)
ELSE IF manifestationType == "scene"
  IF ISceneClient available
    CALL ISceneClient.AddDecorationAsync(...)
ELSE IF manifestationType == "environmental"
  IF IMappingClient available
    CALL IMappingClient.RegisterEnvironmentalEffectAsync(...)
// Record seed growth
CALL IGenesisClient.RecordGrowthAsync({ domain: "memory_depth.manifestation", amount: 1.0 })
IF masterGuided AND activeBond
  READ bond-dungeon:{dungeonId}
  CALL IGenesisClient.RecordGrowthAsync({ seedId: bond.masterSeedId, domain: "coordination.manifestation", amount: 0.5 })
PUBLISH dungeon.memory.manifested { dungeonId, memoryItemInstanceId, manifestationType, outputItemId }
RETURN (200, ManifestMemoryResponse)
```

### ListMemories
POST /dungeon/memory/list | Roles: [developer]

```
READ core:{dungeonId}                                                   -> 404 if null
CALL IInventoryClient.ListItemsAsync({
  inventoryId: coreModel.memoryInventoryId,
  filters: { significance, eventType, manifestationStatus } })
RETURN (200, ListMemoriesResponse { memories, total })
```

### GetMemory
POST /dungeon/memory/get | Roles: [developer]

```
READ core:{dungeonId}                                                   -> 404 if null
CALL IInventoryClient.GetItemAsync({ inventoryId, itemInstanceId })     -> 404 if not found
RETURN (200, GetMemoryResponse)
```

### GetVitals
POST /dungeon/vitals | Roles: [developer]

```
READ core:{dungeonId}                                                   -> 404 if null
READ vitals:{dungeonId}                                                 // may be null (cold dungeon)
READ inhab-counts:{dungeonId}
READ bond-dungeon:{dungeonId}                                           // may be null (no bond)
RETURN (200, GetDungeonVitalsResponse {
  coreIntegrity, currentMana, manaGenerationRate, threatLevel,
  inhabitantSummary, activeBondSummary })
```

### GetDomainInfo
POST /dungeon/domain | Roles: [developer]

```
READ core:{dungeonId}                                                   -> 404 if null
IF IMappingClient available
  CALL IMappingClient.GetDomainBoundaryAsync({ dungeonId })             -> spatial details
RETURN (200, GetDomainInfoResponse {
  domainRadius, coreLocation, roomCount, activeTrapCount, extensionCoreLocations })
```

### CleanupByCharacter
POST /dungeon/cleanup-by-character | Roles: []

```
READ bond-master:character:{characterId}
IF null                                                                 -> RETURN (200, {})  // nothing to do
LOCK dungeon-lock:bond:{bond.dungeonId}
  CALL IContractClient.TerminateContractAsync({ contractId })
  IF config.MasterSeedArchiveOnDissolve
    // Archive master seed
  ELSE
    // Delete master seed
  IF IGardenerClient available
    CALL IGardenerClient.DestroyGardenAsync(...)
  DELETE bond:{bondId}
  DELETE bond-dungeon:{bond.dungeonId}
  DELETE bond-master:character:{characterId}
  DELETE mastercap:{bond.dungeonId}
  READ core:{bond.dungeonId} [with ETag]
  ETAG-WRITE core:{bond.dungeonId} <- model { bondId: null }
  PUBLISH dungeon.bond.dissolved { reason: "character_deleted" }
RETURN (200, CleanupByCharacterResponse)
```

### CleanupByRealm
POST /dungeon/cleanup-by-realm | Roles: []

```
QUERY dungeon-cores WHERE $.realmId == realmId
FOREACH dungeon in dungeons
  // Per-item error isolation
  // Inline Delete logic (deactivate, dissolve bond, remove inhabitants,
  // destroy genesis entity, delete records)
  PUBLISH dungeon.deleted { dungeonId }
RETURN (200, CleanupByRealmResponse { deletedCount })
```

### CleanupByGameService
POST /dungeon/cleanup-by-game-service | Roles: []

```
QUERY dungeon-cores WHERE $.gameServiceId == gameServiceId
FOREACH dungeon in dungeons
  // Per-item error isolation
  // Inline Delete logic
  PUBLISH dungeon.deleted { dungeonId }
RETURN (200, CleanupByGameServiceResponse { deletedCount })
```

---

## Background Services

No background services.

---

## Non-Standard Implementation Patterns

#### OnStartAsync

```
// Register resource cleanup callbacks with lib-resource
CALL IResourceClient.RegisterCleanupCallbacksAsync({
  targets: [
    { resourceType: character, onDelete: CASCADE, endpoint: /dungeon/cleanup-by-character },
    { resourceType: realm, onDelete: CASCADE, endpoint: /dungeon/cleanup-by-realm },
    { resourceType: game-service, onDelete: CASCADE, endpoint: /dungeon/cleanup-by-game-service }
  ]
})
// Register ISeedEvolutionListener for cached capability manifest updates
// Register 4 IVariableProviderFactory implementations
// Register 8 ABML IActionHandler implementations
// Register event consumers
```
