# Genesis Implementation Map

> **Plugin**: lib-genesis
> **Schema**: schemas/genesis-api.yaml
> **Layer**: GameFoundation
> **Deep Dive**: [docs/plugins/GENESIS.md](../plugins/GENESIS.md)

---

## Summary

| Field | Value |
|-------|-------|
| Plugin | lib-genesis |
| Layer | L2 GameFoundation |
| Endpoints | 19 (all generated) |
| State Stores | genesis-templates (MySQL), genesis-entities (MySQL), genesis-entity-cache (Redis), genesis-lock (Redis) |
| Events Published | 9 (genesis.template.created, genesis.template.updated, genesis.entity.created, genesis.entity.updated, genesis.entity.deleted, genesis.entity.phase-changed, genesis.entity.bond-created, genesis.entity.bond-dissolved, genesis.entity.transition-failed) |
| Events Consumed | 2 (self-subscription: genesis.entity.created, genesis.entity.deleted) |
| Client Events | 0 |
| Background Services | 0 implemented (1 planned: GenesisGrowthFlushWorkerService) |
| DI Interfaces | 0 implemented (3 planned: ICurrencyTransactionListener, ISeedEvolutionListener, IVariableProviderFactory) |

---

## State

**Store**: `genesis-templates` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `template:{templateCode}` | `GenesisTemplateModel` | Primary lookup by template code. Full template configuration: seed, economy, storage, awakening, bond. |
| `template-game:{gameServiceId}` | `GenesisTemplateListModel` | Templates registered for a game service (paginated query). |

**Store**: `genesis-entities` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `entity:{entityId}` | `GenesisEntityModel` | Primary lookup. Lifecycle state, provisioned references (seedId internal, walletIds, inventoryIds), cognitive stage, actor/character IDs, physical form, bond, status. |
| `entity-code:{gameServiceId}:{realmId}:{code}` | `GenesisEntityModel` | Code-uniqueness lookup within game/realm scope. |
| `entity-template:{templateCode}:{realmId}` | `GenesisEntityListModel` | Entities by template and realm (paginated query). |
| `entity-wallet:{walletId}` | `GenesisEntityModel` | Reverse index: wallet → entity. Used by ICurrencyTransactionListener for O(1) genesis entity lookup from wallet credit. |

**Store**: `genesis-entity-cache` (Backend: Redis, prefix: `genesis:cache`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `entity:{entityId}` | `CachedGenesisEntity` | Hot cache for entity lookups. TTL: EntityCacheTtlMinutes config. Event-driven invalidation. |
| `caps:{entityId}` | `CachedCapabilityManifest` | Cached seed capability manifest for fast capability checks and variable provider reads. TTL: CapabilityCacheTtlMinutes config. |

**Store**: `genesis-lock` (Backend: Redis, prefix: `genesis:lock`)

| Key Pattern | Purpose |
|-------------|---------|
| `transition:{entityId}` | Phase transition lock — serializes actor spawning and character creation across nodes. |
| `entity:{entityId}` | Entity mutation lock — create, update, destroy, bind-physical-form. |
| `bond:{entityId}` | Bond formation/dissolution lock. |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | Template store (MySQL), entity store (MySQL), entity cache (Redis) |
| lib-state (`IDistributedLockProvider`) | L0 | Hard | Transition, entity mutation, and bond locks (Redis) |
| lib-messaging (`IMessageBus`) | L0 | Hard | Publishing 9 event topics + self-subscription for wallet map coherence |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Span instrumentation on async helpers |
| lib-resource (`IResourceClient`) | L1 | Hard | Cleanup callback registration, compression callback registration, cascade cleanup execution |
| lib-seed (`ISeedClient`) | L2 | Hard | Seed type registration, seed creation, growth batch recording, capability manifest queries |
| lib-currency (`ICurrencyClient`) | L2 | Hard | Wallet creation, balance queries (includeBalances flag), balance credit on restore |
| lib-character (`ICharacterClient`) | L2 | Hard | Character creation in system realm at Awakened phase |
| lib-actor (`IActorClient`) | L2 | Hard | Actor spawning at Stirring phase, character binding at Awakened phase |
| lib-inventory (`IInventoryClient`) | L2 | Hard | Container creation for template-defined inventories |
| lib-item (`IItemClient`) | L2 | Hard | Physical form validation when entity is item-based |
| lib-relationship (`IRelationshipClient`) | L2 | Hard | Bond creation/dissolution (Relationship-based bonds) |
| lib-realm (`IRealmClient`) | L2 | Hard | System realm existence and isSystemType validation at template registration and awakening |
| lib-species (`ISpeciesClient`) | L2 | Hard | Species existence validation in system realm at template registration and awakening |
| lib-game-service (`IGameServiceClient`) | L2 | Hard | Game service scoping validation |

All dependencies are L0/L1/L2 — constructor injection per SERVICE-HIERARCHY.md. No soft dependencies (Genesis is L2; no L3/L4 clients).

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `genesis.template.created` | `GenesisTemplateCreatedEvent` | RegisterTemplate |
| `genesis.template.updated` | `GenesisTemplateUpdatedEvent` | UpdateTemplate, DeprecateTemplate (changedFields includes deprecation fields per IMPLEMENTATION TENETS) |
| `genesis.entity.created` | `GenesisEntityCreatedEvent` | CreateEntity — includes entityId, templateCode, gameServiceId, realmId, walletIds, inventoryIds |
| `genesis.entity.updated` | `GenesisEntityUpdatedEvent` | BindPhysicalForm, status changes |
| `genesis.entity.deleted` | `GenesisEntityDeletedEvent` | DestroyEntity |
| `genesis.entity.phase-changed` | `GenesisEntityPhaseChangedEvent` | ISeedEvolutionListener cognitive stage transition — includes entityId, templateCode, phaseName, cognitiveStage, actorId (if spawned), characterId (if created) |
| `genesis.entity.bond-created` | `GenesisEntityBondCreatedEvent` | CreateBond |
| `genesis.entity.bond-dissolved` | `GenesisEntityBondDissolvedEvent` | DissolveBond |
| `genesis.entity.transition-failed` | `GenesisEntityTransitionFailedEvent` | ISeedEvolutionListener when phase transition cannot complete — includes entityId, targetPhase, targetCognitiveStage, failureReason |

> **Note**: Deep dive listed a dedicated `genesis.template.deprecated` event. Per IMPLEMENTATION TENETS (T31), deprecation uses `genesis.template.updated` with changedFields containing `[IsDeprecated, DeprecatedAt, DeprecationReason]`.

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `genesis.entity.created` | Self-subscription | Updates in-memory `ConcurrentDictionary<walletId, WalletMapping>` with new entity's wallet-to-entity mappings |
| `genesis.entity.deleted` | Self-subscription | Removes destroyed entity's wallet mappings from in-memory wallet map |

These are self-subscriptions for multi-node wallet map coherence. All external reactions are via DI Listeners (ICurrencyTransactionListener, ISeedEvolutionListener), not event subscriptions.

---

## DI Services

#### Constructor Dependencies

| Service | Role |
|---------|------|
| `ILogger<GenesisService>` | Structured logging |
| `GenesisServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | Acquires 4 state stores in constructor |
| `IDistributedLockProvider` | Transition, entity, and bond locks |
| `IMessageBus` | Event publishing |
| `ITelemetryProvider` | Span instrumentation |
| `IResourceClient` | Cleanup and compression callback registration |
| `ISeedClient` | Seed lifecycle management |
| `ICurrencyClient` | Wallet management and balance queries |
| `ICharacterClient` | Character creation at awakening |
| `IActorClient` | Actor spawning and character binding |
| `IInventoryClient` | Container creation |
| `IItemClient` | Physical form validation (item-based) |
| `IRelationshipClient` | Bond creation/dissolution |
| `IRealmClient` | System realm validation |
| `ISpeciesClient` | Species validation |
| `IGameServiceClient` | Game service scoping validation |
| `IEventConsumer` | Event consumer registration (self-subscriptions) |

#### DI Interfaces Implemented by This Plugin (Planned — Not Yet Implemented)

> The following DI interfaces are specified in the deep dive but are NOT yet implemented in code. No listener or provider classes exist in the plugin. The growth flush worker, wallet map, and variable provider are planned infrastructure.

| Interface | Registered As | Direction | Consumer | Status |
|-----------|---------------|-----------|----------|--------|
| `ICurrencyTransactionListener` (new) | `Singleton` | L2→L2 push | Currency (L2) dispatches wallet credit/debit notifications; Genesis checks in-memory wallet map and buffers matched credits | **Not implemented** |
| `ISeedEvolutionListener` | `Singleton` | L2→L2 push | Seed (L2) dispatches phase change, growth recorded, and capability change notifications; Genesis handles cognitive stage transitions | **Not implemented** |
| `IVariableProviderFactory` | `Singleton` | L2→L2 pull | Actor (L2) discovers `${genesis.*}` variables for ABML behavior execution | **Not implemented** |

---

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| RegisterTemplate | POST /genesis/template/register | generated | developer | template, template-game | genesis.template.created |
| GetTemplate | POST /genesis/template/get | generated | developer | - | - |
| ListTemplates | POST /genesis/template/list | generated | developer | - | - |
| UpdateTemplate | POST /genesis/template/update | generated | developer | template | genesis.template.updated |
| DeprecateTemplate | POST /genesis/template/deprecate | generated | developer | template | genesis.template.updated |
| CleanDeprecated | POST /genesis/template/clean-deprecated | generated | admin | template, template-game | - |
| CreateEntity | POST /genesis/entity/create | generated | [] | entity, entity-code, entity-template, entity-wallet, cache | genesis.entity.created |
| GetEntity | POST /genesis/entity/get | generated | [] | cache (read-through) | - |
| ListEntities | POST /genesis/entity/list | generated | [] | - | - |
| GetCapabilities | POST /genesis/entity/get-capabilities | generated | [] | cache (read-through) | - |
| DestroyEntity | POST /genesis/entity/destroy | generated | [] | entity, entity-code, entity-template, entity-wallet, cache | genesis.entity.deleted |
| BindPhysicalForm | POST /genesis/entity/bind-physical-form | generated | [] | entity, cache | genesis.entity.updated |
| CreateBond | POST /genesis/entity/create-bond | generated | [] | entity, cache | genesis.entity.bond-created |
| GetBond | POST /genesis/entity/get-bond | generated | [] | - | - |
| DissolveBond | POST /genesis/entity/dissolve-bond | generated | [] | entity, cache | genesis.entity.bond-dissolved |
| CleanupByCharacter | POST /genesis/cleanup-by-character | generated | [] | entity, entity-code, entity-template, entity-wallet, cache | genesis.entity.deleted |
| CleanupByRealm | POST /genesis/cleanup-by-realm | generated | [] | entity, entity-code, entity-template, entity-wallet, cache | genesis.entity.deleted |
| GetCompressData | POST /genesis/get-compress-data | generated | [] | - | - |
| RestoreFromArchive | POST /genesis/restore-from-archive | generated | admin | entity, entity-code, entity-template, entity-wallet | genesis.entity.created |

---

## Methods

### RegisterTemplate
POST /genesis/template/register | Roles: [developer]

```
VALIDATE template structure:
  - every growthMapping[].walletCode references a wallet in wallets[]
  - every growthMapping[].domain references a domain in seed.domains[]
  - no duplicate (walletCode, domain, direction) triples               -> 400 if invalid
CALL IRealmClient.GetRealmByCodeAsync({ code: awakening.systemRealmCode })  -> 400 if not found or not isSystemType
CALL ISpeciesClient.GetSpeciesByCodeAsync({ code: awakening.characterSpeciesCode })  -> 400 if not found in realm
READ genesis-templates:"template:{templateCode}"
IF exists                                                              -> RETURN (200, existing)  // idempotent
CALL ISeedClient.RegisterSeedTypeAsync({
  seedTypeCode, gameServiceId, displayName, description,
  maxPerOwner: 1, allowedOwnerTypes: [Other],
  growthPhases: [{ phaseCode, displayName, minTotalGrowth }],
  bondCardinality: 0, bondPermanent: false, capabilityRules })
LOCK genesis-lock:"entity:{templateCode}"
  WRITE genesis-templates:"template:{templateCode}" <- GenesisTemplateModel from request
  WRITE genesis-templates:"template-game:{gameServiceId}" <- add to index
  PUBLISH genesis.template.created { templateCode, gameServiceId }
RETURN (200, GenesisTemplateResponse)
```

### GetTemplate
POST /genesis/template/get | Roles: [developer]

```
READ genesis-templates:"template:{templateCode}"                       -> 404 if null
RETURN (200, GenesisTemplateResponse)
```

### ListTemplates
POST /genesis/template/list | Roles: [developer]

```
QUERY genesis-templates:"template-game:{gameServiceId}"
  WHERE $.IsDeprecated filter (default: exclude deprecated)
  PAGED(page, pageSize ?? config.DefaultPageSize)
RETURN (200, ListTemplatesResponse)
```

### UpdateTemplate
POST /genesis/template/update | Roles: [developer]

```
READ genesis-templates:"template:{templateCode}"                       -> 404 if null
VALIDATE updated fields (same structural validations as RegisterTemplate)
IF awakening fields changed
  CALL IRealmClient.GetRealmByCodeAsync({ code: awakening.systemRealmCode })  -> 400 if invalid
  CALL ISpeciesClient.GetSpeciesByCodeAsync({ code: awakening.characterSpeciesCode })  -> 400 if invalid
LOCK genesis-lock:"entity:{templateCode}"
  // Template config is snapshot at entity creation — update only affects new entities
  WRITE genesis-templates:"template:{templateCode}" <- updated GenesisTemplateModel
  PUBLISH genesis.template.updated { templateCode, changedFields }
RETURN (200, GenesisTemplateResponse)
```

### DeprecateTemplate
POST /genesis/template/deprecate | Roles: [developer]

```
READ genesis-templates:"template:{templateCode}"                       -> 404 if null
IF template.IsDeprecated                                               -> RETURN (200, existing)  // idempotent per IMPLEMENTATION TENETS
SET template.IsDeprecated = true
SET template.DeprecatedAt = now
SET template.DeprecationReason = request.reason
WRITE genesis-templates:"template:{templateCode}" <- updated
PUBLISH genesis.template.updated { templateCode, changedFields: [IsDeprecated, DeprecatedAt, DeprecationReason] }
RETURN (200, GenesisTemplateResponse)
```

### CleanDeprecated
POST /genesis/template/clean-deprecated | Roles: [admin]

```
// Standard Category B sweep using DeprecationCleanupHelper
CALL DeprecationCleanupHelper.ExecuteCleanupSweepAsync(
  store: genesis-templates,
  entityName: "genesis-template",
  hasActiveInstances: (template) => {
    QUERY genesis-entities:"entity-template:{template.TemplateCode}:*"
    RETURN count > 0
  })
  // For each deprecated template with no referencing entities:
  //   DELETE genesis-templates:"template:{templateCode}"
  //   DELETE genesis-templates:"template-game:{gameServiceId}" <- remove from index
RETURN (200, CleanDeprecatedStringKeyResponse { cleaned, remaining, errors, cleanedIds })
```

### CreateEntity
POST /genesis/entity/create | Roles: []

```
READ genesis-templates:"template:{templateCode}"                       -> 404 if null
IF template.IsDeprecated                                               -> 400 "template deprecated, cannot create new entities"
CALL IGameServiceClient.GetServiceAsync({ serviceId: gameServiceId })   -> 400 if not found
IF request.code != null
  READ genesis-entities:"entity-code:{gameServiceId}:{realmId}:{code}"
  IF exists                                                            -> 409 "entity code already in use"
// Resolve currencyDefinitionIds from template wallet codes (needed for balance queries)
FOREACH wallet in template.economy.wallets
  CALL ICurrencyClient.GetCurrencyDefinitionAsync({ code: wallet.currencyCode })  -> 400 if not found
  currencyDefIds[wallet.walletCode] = response.definitionId
LOCK genesis-lock:"entity:{newEntityId}"
  // Provision seed (internal — seedId never exposed in API)
  CALL ISeedClient.CreateSeedAsync({
    ownerId: entityId, ownerType: Other,
    seedTypeCode: template.seed.seedTypeCode,
    gameServiceId: gameServiceId })
  // Provision wallets
  FOREACH wallet in template.economy.wallets
    CALL ICurrencyClient.CreateWalletAsync({
      ownerId: entityId, ownerType: Other, realmId: realmId })
    walletIds[wallet.walletCode] = response.wallet.walletId
  // Provision inventories
  FOREACH inventory in template.storage.inventories
    CALL IInventoryClient.CreateContainerAsync({
      ownerId: entityId, ownerType: Other,
      containerType: inventory.inventoryCode,
      constraintModel: inventory.constraintModel,  // ContainerConstraintModel enum
      maxSlots: inventory.capacity,
      allowedCategories: inventory.allowedCategories })
    inventoryIds[inventory.inventoryCode] = response.containerId
  // Register resource cleanup callbacks
  CALL IResourceClient.RegisterResourceCleanupCallbacksAsync(...)
  // Register compression callbacks
  CALL IResourceClient.RegisterCompressCallbacksAsync(...)
  // Store entity record
  WRITE genesis-entities:"entity:{entityId}" <- GenesisEntityModel {
    entityId, templateCode, gameServiceId, realmId, code, displayName,
    seedId (internal), walletIds, inventoryIds,
    currentPhase: template.seed.phases[0].phaseName,
    cognitiveStage: Dormant, status: Active,
    createdAt: now, updatedAt: now
  }
  IF request.code != null
    WRITE genesis-entities:"entity-code:{gameServiceId}:{realmId}:{code}" <- entity
  WRITE genesis-entities:"entity-template:{templateCode}:{realmId}" <- add to index
  FOREACH walletId in walletIds.Values
    WRITE genesis-entities:"entity-wallet:{walletId}" <- entity
  PUBLISH genesis.entity.created { entityId, templateCode, gameServiceId, realmId, walletIds, inventoryIds }
RETURN (200, GenesisEntityResponse)
```

### GetEntity
POST /genesis/entity/get | Roles: []

```
READ genesis-entity-cache:"entity:{entityId}"
IF cache miss
  READ genesis-entities:"entity:{entityId}"                            -> 404 if null
  WRITE genesis-entity-cache:"entity:{entityId}" <- cached (TTL: config.EntityCacheTtlMinutes)
IF request.includeBalances (default: config.IncludeBalancesDefault)
  READ genesis-templates:"template:{entity.templateCode}"
  FOREACH walletCode, walletId in entity.walletIds
    CALL ICurrencyClient.GetCurrencyDefinitionAsync({ code: template.wallet[walletCode].currencyCode })
    CALL ICurrencyClient.GetBalanceAsync({
      walletId: walletId, currencyDefinitionId: response.definitionId })
    walletBalances[walletCode] = response.amount
RETURN (200, GenesisEntityResponse { ..., walletBalances })
```

### ListEntities
POST /genesis/entity/list | Roles: []

```
QUERY genesis-entities:"entity-template:{templateCode}:{realmId}"
  WHERE $.CognitiveStage filter, $.Status filter, $.CurrentPhase filter
  PAGED(page, pageSize ?? config.DefaultPageSize)
RETURN (200, ListEntitiesResponse)
```

### GetCapabilities
POST /genesis/entity/get-capabilities | Roles: []

```
READ genesis-entities:"entity:{entityId}"                              -> 404 if null
READ genesis-entity-cache:"caps:{entityId}"
IF cache miss
  CALL ISeedClient.GetCapabilityManifestAsync({ seedId: entity.seedId })
  // Response: CapabilityManifestResponse { capabilities: Capability[], version }
  // Map Capability.unlocked -> GenesisCapability.isUnlocked
  WRITE genesis-entity-cache:"caps:{entityId}" <- cached (TTL: config.CapabilityCacheTtlMinutes)
RETURN (200, GetCapabilitiesResponse)
```

### DestroyEntity
POST /genesis/entity/destroy | Roles: []

```
READ genesis-entities:"entity:{entityId}"                              -> 404 if null
READ genesis-templates:"template:{entity.templateCode}"
LOCK genesis-lock:"entity:{entityId}"
  // Stop actor if running
  IF entity.actorId != null
    CALL IActorClient.StopActorAsync({ actorId: entity.actorId.ToString() })  // actorId is string in Actor API
  // Archive character if awakened and template says to
  IF entity.characterId != null AND template.archiveOnDestruction
    CALL IResourceClient.ExecuteCompressAsync({
      resourceType: "character", resourceId: entity.characterId })
  // Dissolve bond if exists
  IF entity.bondId != null
    CALL IRelationshipClient.EndRelationshipAsync({ relationshipId: entity.bondId })
  // Cleanup provisioned infrastructure via Resource (cascades to seed, wallets, inventories)
  CALL IResourceClient.ExecuteCleanupAsync({
    resourceType: "genesis-entity", resourceId: entityId })
  // Delete entity record and all indexes
  DELETE genesis-entities:"entity:{entityId}"
  IF entity.code != null
    DELETE genesis-entities:"entity-code:{gameServiceId}:{realmId}:{code}"
  DELETE genesis-entities:"entity-template:{templateCode}:{realmId}" <- remove from index
  FOREACH walletId in entity.walletIds.Values
    DELETE genesis-entities:"entity-wallet:{walletId}"
  DELETE genesis-entity-cache:"entity:{entityId}"
  DELETE genesis-entity-cache:"caps:{entityId}"
  PUBLISH genesis.entity.deleted { entityId, templateCode, gameServiceId, realmId }
RETURN (200, null)
```

### BindPhysicalForm
POST /genesis/entity/bind-physical-form | Roles: []

```
READ genesis-entities:"entity:{entityId}"                              -> 404 if null
READ genesis-templates:"template:{entity.templateCode}"
VALIDATE request.physicalFormType matches template.physicalFormType     -> 400 if mismatch
IF request.physicalFormType == Item
  CALL IItemClient.GetItemInstanceAsync({ instanceId: request.physicalFormId })  -> 400 if not found
// Location validation deferred to caller (deep dive does not specify ILocationClient dependency)
LOCK genesis-lock:"entity:{entityId}"
  SET entity.physicalFormType = request.physicalFormType
  SET entity.physicalFormId = request.physicalFormId
  SET entity.updatedAt = now
  WRITE genesis-entities:"entity:{entityId}" <- updated entity
  DELETE genesis-entity-cache:"entity:{entityId}"
  PUBLISH genesis.entity.updated { entityId, changedFields: [physicalFormType, physicalFormId] }
RETURN (200, GenesisEntityResponse)
```

### CreateBond
POST /genesis/entity/create-bond | Roles: []

```
READ genesis-entities:"entity:{entityId}"                              -> 404 if null
READ genesis-templates:"template:{entity.templateCode}"
IF NOT template.bond.enabled                                           -> 400 "bonds not enabled for this template"
IF template.bond.cardinality == None                                   -> 400 "template cardinality is None"
IF (template.bond.cardinality == OptionalOne OR RequiredOne)
  AND entity.bondTargetEntityId != null                                -> 409 "entity already has a bond"
// Validate target entity exists via appropriate client for targetEntityType
CALL validate target existence                                         -> 400 if not found
LOCK genesis-lock:"bond:{entityId}"
  SET entity.bondTargetEntityType = request.targetEntityType
  SET entity.bondTargetEntityId = request.targetEntityId
  // If already awakened, create Relationship immediately
  IF entity.characterId != null
    CALL IRelationshipClient.GetRelationshipTypeByCodeAsync({
      code: template.bond.relationshipTypeCode })                      -> 400 if not found
    CALL IRelationshipClient.CreateRelationshipAsync({
      entity1Id: entity.characterId, entity1Type: Character,
      entity2Id: request.targetEntityId, entity2Type: request.targetEntityType,
      relationshipTypeId: relType.relationshipTypeId, startedAt: now })
    SET entity.bondId = response.relationshipId
  SET entity.updatedAt = now
  WRITE genesis-entities:"entity:{entityId}" <- updated entity
  DELETE genesis-entity-cache:"entity:{entityId}"
  PUBLISH genesis.entity.bond-created { entityId, targetEntityType, targetEntityId, bondId }
RETURN (200, GenesisEntityResponse)
```

### GetBond
POST /genesis/entity/get-bond | Roles: []

```
READ genesis-entities:"entity:{entityId}"                              -> 404 if null
IF entity.bondTargetEntityId == null                                   -> 404 "no active bond"
RETURN (200, GenesisBondResponse { bondId, bondTargetEntityType, bondTargetEntityId })
```

### DissolveBond
POST /genesis/entity/dissolve-bond | Roles: []

```
READ genesis-entities:"entity:{entityId}"                              -> 404 if null
IF entity.bondTargetEntityId == null                                   -> 404 "no active bond"
LOCK genesis-lock:"bond:{entityId}"
  // If Relationship was materialized (entity is awakened), end it
  IF entity.bondId != null
    CALL IRelationshipClient.EndRelationshipAsync({ relationshipId: entity.bondId })
  SET entity.bondTargetEntityType = null
  SET entity.bondTargetEntityId = null
  SET entity.bondId = null
  SET entity.updatedAt = now
  WRITE genesis-entities:"entity:{entityId}" <- updated entity
  DELETE genesis-entity-cache:"entity:{entityId}"
  PUBLISH genesis.entity.bond-dissolved { entityId }
RETURN (200, null)
```

### CleanupByCharacter
POST /genesis/cleanup-by-character | Roles: []

```
// Called by lib-resource during character deletion (x-references CASCADE)
QUERY genesis-entities WHERE $.CharacterId == request.characterId
FOREACH entity in results
  // Per-item error isolation (T7) — one corrupt entity must not block cleanup of others
  TRY
    LOCK genesis-lock:"entity:{entity.entityId}"
      IF entity.actorId != null
        CALL IActorClient.StopActorAsync({ actorId: entity.actorId.ToString() })
      IF entity.bondId != null
        CALL IRelationshipClient.EndRelationshipAsync({ relationshipId: entity.bondId })
      CALL IResourceClient.ExecuteCleanupAsync({
        resourceType: "genesis-entity", resourceId: entity.entityId })
      DELETE genesis-entities (all keys for entity)
      DELETE genesis-entity-cache (all keys for entity)
      PUBLISH genesis.entity.deleted { entity.entityId, ... }
  CATCH -> log Warning, continue
RETURN (200, null)
```

### CleanupByRealm
POST /genesis/cleanup-by-realm | Roles: []

```
// Called by lib-resource during realm deletion (x-references CASCADE)
// Process in batches to avoid overwhelming infrastructure
LOOP
  QUERY genesis-entities WHERE $.RealmId == request.realmId
    PAGED(1, config.CleanupBatchSize)
  IF no results -> BREAK
  FOREACH entity in batch
    // Per-item error isolation (T7)
    TRY
      READ genesis-templates:"template:{entity.templateCode}"
      LOCK genesis-lock:"entity:{entity.entityId}"
        IF entity.actorId != null
          CALL IActorClient.StopActorAsync({ actorId: entity.actorId.ToString() })
        IF entity.characterId != null AND template?.archiveOnDestruction
          CALL IResourceClient.ExecuteCompressAsync({
            resourceType: "character", resourceId: entity.characterId })
        IF entity.bondId != null
          CALL IRelationshipClient.EndRelationshipAsync({ relationshipId: entity.bondId })
        CALL IResourceClient.ExecuteCleanupAsync({
          resourceType: "genesis-entity", resourceId: entity.entityId })
        DELETE genesis-entities (all keys for entity)
        DELETE genesis-entity-cache (all keys for entity)
        PUBLISH genesis.entity.deleted { entity.entityId, ... }
    CATCH -> log Warning, continue
RETURN (200, null)
```

### GetCompressData
POST /genesis/get-compress-data | Roles: []

```
// Called by lib-resource during character compression (x-compression-callback, priority 10)
QUERY genesis-entities WHERE $.CharacterId == request.characterId
FOREACH entity in results
  READ genesis-entity-cache:"caps:{entity.entityId}"
  IF cache miss
    CALL ISeedClient.GetCapabilityManifestAsync(entity.seedId)
  READ genesis-templates:"template:{entity.templateCode}"
  FOREACH walletCode, walletId in entity.walletIds
    CALL ICurrencyClient.GetCurrencyDefinitionAsync({ code: template.wallet[walletCode].currencyCode })
    CALL ICurrencyClient.GetBalanceAsync({
      walletId: walletId, currencyDefinitionId: response.definitionId })
    walletBalances[walletCode] = response.amount
  archive.entities.add {
    entity state snapshot, walletBalances, capabilities, currentPhase, cognitiveStage
  }
RETURN (200, GenesisArchive extends ResourceArchiveBase)
```

### RestoreFromArchive
POST /genesis/restore-from-archive | Roles: [admin]

```
// Re-provisions genesis entities from archive
FOREACH archivedEntity in request.archive.entities
  READ genesis-templates:"template:{archivedEntity.templateCode}"      -> 400 if template gone
  // Re-provision seed
  CALL ISeedClient.CreateSeedAsync(
    ownerType: Other, ownerId: archivedEntity.entityId,
    seedTypeCode: template.seed.seedTypeCode,
    gameServiceId: archivedEntity.gameServiceId)
  // Re-provision wallets with archived balances
  FOREACH wallet in template.economy.wallets
    CALL ICurrencyClient.GetCurrencyDefinitionAsync({ code: wallet.currencyCode })  -> 400 if not found
    CALL ICurrencyClient.CreateWalletAsync({
      ownerId: archivedEntity.entityId, ownerType: Other, realmId: archivedEntity.realmId })
    walletIds[wallet.walletCode] = response.wallet.walletId
    // Restore balance via Credit
    IF archivedEntity.walletBalances[wallet.walletCode] > 0
      CALL ICurrencyClient.CreditCurrencyAsync({
        walletId: walletIds[wallet.walletCode],
        currencyDefinitionId: currencyDef.definitionId,
        amount: archivedEntity.walletBalances[wallet.walletCode],
        transactionType: Refund, idempotencyKey: unique })
  // Re-provision inventories
  FOREACH inventory in template.storage.inventories
    CALL IInventoryClient.CreateContainerAsync({
      ownerId: archivedEntity.entityId, ownerType: Other,
      containerType: inventory.inventoryCode,
      constraintModel: inventory.constraintModel,  // ContainerConstraintModel enum
      maxSlots: inventory.capacity,
      allowedCategories: inventory.allowedCategories })
    inventoryIds[inventory.inventoryCode] = response.containerId
  // Register resource callbacks
  CALL IResourceClient.RegisterResourceCleanupCallbacksAsync(...)
  CALL IResourceClient.RegisterCompressCallbacksAsync(...)
  // Store entity record (actor/character NOT restored — re-created when growth conditions met)
  WRITE genesis-entities:"entity:{entityId}" <- GenesisEntityModel {
    entityId, templateCode, gameServiceId, realmId, code, displayName,
    seedId (new), walletIds (new), inventoryIds (new),
    currentPhase: archivedEntity.currentPhase,
    cognitiveStage: Dormant,  // Reset — actor/character re-emerge via growth
    actorId: null, characterId: null,
    status: Active, createdAt: archivedEntity.createdAt, updatedAt: now
  }
  // Rebuild indexes
  WRITE genesis-entities:"entity-code:..." (if code)
  WRITE genesis-entities:"entity-template:..." <- add to index
  FOREACH walletId in walletIds.Values
    WRITE genesis-entities:"entity-wallet:{walletId}" <- entity
  PUBLISH genesis.entity.created { entityId, templateCode, ... }
RETURN (200, RestoreFromArchiveResponse)
```

---

## Background Services

> **Not yet implemented.** The growth flush worker is specified in the deep dive but no `BackgroundService` class exists in the plugin code. `GenesisServicePlugin` inherits `StandardServicePlugin<IGenesisService>` with no overrides.

### GenesisGrowthFlushWorkerService (Planned)
**Interval**: config.GrowthFlushIntervalSeconds (default: 5s)
**Purpose**: Drains the in-memory growth accumulator and applies batched seed growth. Consolidates multiple currency credits per entity into a single Seed.RecordGrowthBatch call, reducing lock contention from one-per-credit to one-per-entity-per-flush.

```
// Scoped: resolve state stores once per execution cycle
DRAIN growthAccumulator atomically  // ConcurrentDictionary<entityId, GrowthAccumulator>
GROUP by entityId
FOREACH entityId, accumulated in groups
  READ genesis-entities:"entity:{entityId}"
  IF entity == null -> skip (destroyed between buffer and flush)
  READ genesis-templates:"template:{entity.templateCode}"
  // Apply template growth mappings: amount × ratio, direction filter
  FOREACH mapping in template.economy.growthMappings
    IF accumulated.walletCode == mapping.walletCode
      AND accumulated.direction matches mapping.direction
      growthBatch.add(domain: mapping.domain, amount: accumulated.amount * mapping.ratio)
  IF growthBatch is empty -> skip
  CALL ISeedClient.RecordGrowthBatchAsync({
    seedId: entity.seedId,
    entries: growthBatch,  // [{ domain, amount }]
    source: "genesis-growth-flush" })
  // Phase transitions are detected by ISeedEvolutionListener (fired by Seed after growth recording)
```

---

## Non-Standard Implementation Patterns

> **Partially implemented.** The plugin lifecycle and DI listener sections below are specified in the deep dive but NOT yet implemented in code. `GenesisServicePlugin` is a bare `StandardServicePlugin<IGenesisService>` with no lifecycle overrides. No listener or provider classes exist.

#### Plugin Lifecycle (OnStartAsync) — Planned

```
// Populate in-memory wallet map from MySQL for ICurrencyTransactionListener fast-path
QUERY genesis-entities:"entity:*" (all active entities)
FOREACH entity in results
  READ genesis-templates:"template:{entity.templateCode}"
  FOREACH walletCode, walletId in entity.walletIds
    walletMap[walletId] = GenesisWalletMapping {
      entityId, templateCode, growthMappings: template.economy.growthMappings
    }
// Subscribe to own events for multi-node wallet map coherence
SUBSCRIBE genesis.entity.created -> add wallet mappings to walletMap
SUBSCRIBE genesis.entity.deleted -> remove wallet mappings from walletMap
```

#### Plugin Lifecycle (OnRunningAsync) — Planned

```
// Register seed type definitions with Seed service
// (Seed is guaranteed available — L2 loaded before L2 OnRunning executes)
// Seed types are registered per template at RegisterTemplate time,
// but pre-existing templates need their types re-registered on startup
QUERY genesis-templates (all)
FOREACH template in results
  CALL ISeedClient.RegisterSeedTypeAsync({
    seedTypeCode: template.seed.seedTypeCode,
    gameServiceId: template.gameServiceId,
    displayName: template.displayName,
    description: template.description,
    maxPerOwner: 1, allowedOwnerTypes: [Other],
    growthPhases: [{ phaseCode, displayName, minTotalGrowth }],
    bondCardinality: 0, bondPermanent: false,
    capabilityRules: template.seed.capabilityRules })
// Register resource cleanup callbacks
CALL IResourceClient.RegisterResourceCleanupCallbacksAsync([
  { resourceType: "character", sourceType: "genesis", onDelete: CASCADE,
    endpoint: "/genesis/cleanup-by-character" },
  { resourceType: "realm", sourceType: "genesis", onDelete: CASCADE,
    endpoint: "/genesis/cleanup-by-realm" }
])
// Register compression callbacks
CALL IResourceClient.RegisterCompressCallbacksAsync([
  { resourceType: "character", sourceType: "genesis", priority: 10,
    compressEndpoint: "/genesis/get-compress-data",
    decompressEndpoint: "/genesis/restore-from-archive" }
])
```

#### DI Listener: GenesisCurrencyTransactionListener (ICurrencyTransactionListener) — Planned

```
// Called in-process by Currency (L2) after any wallet credit/debit
// This is the microsecond-fast filtering path — no network I/O

OnCurrencyCredited(walletId, currencyCode, amount, newBalance):
  READ walletMap[walletId]  // ConcurrentDictionary, in-memory
  IF miss -> RETURN  // Not a genesis-managed wallet (~microseconds)
  // Buffer credit in growth accumulator for flush worker
  growthAccumulator.AddOrUpdate(mapping.entityId, {
    walletCode: mapping.walletCode, amount, direction: Credit
  })

OnCurrencyDebited(walletId, currencyCode, amount, newBalance):
  READ walletMap[walletId]  // ConcurrentDictionary, in-memory
  IF miss -> RETURN
  // Buffer debit (for templates with direction: Debit or Both)
  growthAccumulator.AddOrUpdate(mapping.entityId, {
    walletCode: mapping.walletCode, amount, direction: Debit
  })
```

#### DI Listener: GenesisEvolutionListener (ISeedEvolutionListener) — Planned

```
// Called in-process by Seed (L2) after growth recording, phase transitions, capability changes

OnPhaseChangedAsync(seedId, seedTypeCode, oldPhase, newPhase, totalGrowth):
  // Look up genesis entity by seedId (internal field on entity model)
  READ genesis-entities WHERE seedId match  // Uses internal reverse lookup
  IF entity == null -> RETURN  // Not a genesis-managed seed
  READ genesis-templates:"template:{entity.templateCode}"
  // Determine cognitive stage for new phase
  targetCognitiveStage = template.seed.phases[newPhase].cognitiveStage
  IF targetCognitiveStage == entity.cognitiveStage -> RETURN  // Phase changed but stage didn't
  LOCK genesis-lock:"transition:{entity.entityId}"
    IF targetCognitiveStage == EventBrain AND entity.cognitiveStage == Dormant
      // Stage 2: Spawn Actor — Actor API is template-based (templateId: Guid), not behaviorRef
      // Genesis must have a pre-registered actor template; resolve templateId from template config
      CALL IActorClient.SpawnActorAsync({
        templateId: resolved actor template Guid,
        actorId: entity.entityId.ToString(),  // actorId is string in Actor API
        characterId: null, realmId: entity.realmId })               -> on failure: publish transition-failed
      SET entity.actorId = response.actorId  // string, stored as string
      SET entity.cognitiveStage = EventBrain
    ELSE IF targetCognitiveStage == CharacterBrain AND entity.cognitiveStage == EventBrain
      // Stage 3: Create Character and bind to Actor
      // Re-validate system realm (defense-in-depth against post-registration deletion)
      CALL IRealmClient.GetRealmByCodeAsync({ code: template.awakening.systemRealmCode })
      IF not found or not system realm                                 -> publish transition-failed, RETURN
      CALL ISpeciesClient.GetSpeciesByCodeAsync({ code: template.awakening.characterSpeciesCode })
      IF not found                                                     -> publish transition-failed, RETURN
      // Character API uses realmId (Guid) and speciesId (Guid), not codes
      // Resolve IDs from the GetRealmByCode/GetSpeciesByCode responses
      CALL ICharacterClient.CreateCharacterAsync({
        name: entity.displayName ?? entity.templateCode,
        realmId: realm.realmId,
        speciesId: species.speciesId,
        birthDate: now })
      SET entity.characterId = response.characterId
      // Seed personality traits if configured
      IF template.awakening.initialPersonalityTraits != null
        // Behavior not specified in deep dive: personality seeding mechanism
        // (likely ICharacterPersonalityClient or direct PersonalityTraits on character)
      // Bind actor to character — actorId is string in Actor API
      CALL IActorClient.BindActorCharacterAsync({
        actorId: entity.actorId, characterId: entity.characterId })
      SET entity.cognitiveStage = CharacterBrain
      // Create deferred bond Relationship if bond intent exists
      IF entity.bondTargetEntityId != null AND entity.bondId == null
        CALL IRelationshipClient.GetRelationshipTypeByCodeAsync({
          code: template.bond.relationshipTypeCode })
        CALL IRelationshipClient.CreateRelationshipAsync({
          entity1Id: entity.characterId, entity1Type: Character,
          entity2Id: entity.bondTargetEntityId, entity2Type: entity.bondTargetEntityType,
          relationshipTypeId: relType.relationshipTypeId, startedAt: now })
        SET entity.bondId = response.relationshipId
    SET entity.currentPhase = newPhase
    SET entity.updatedAt = now
    WRITE genesis-entities:"entity:{entity.entityId}" <- updated entity
    DELETE genesis-entity-cache:"entity:{entity.entityId}"
    DELETE genesis-entity-cache:"caps:{entity.entityId}"
    PUBLISH genesis.entity.phase-changed {
      entityId, templateCode, phaseName: newPhase, cognitiveStage,
      actorId, characterId
    }

OnGrowthRecordedAsync(seedId, domain, amount):
  // Invalidate capability cache (growth may unlock new capabilities)
  READ genesis-entities WHERE seedId match
  IF entity != null
    DELETE genesis-entity-cache:"caps:{entity.entityId}"

OnCapabilitiesChangedAsync(seedId, capabilityCount):
  // Invalidate capability cache
  READ genesis-entities WHERE seedId match
  IF entity != null
    DELETE genesis-entity-cache:"caps:{entity.entityId}"
```

#### DI Provider: GenesisVariableProviderFactory (IVariableProviderFactory) — Planned

```
// Provides ${genesis.*} namespace to Actor (L2) for ABML behavior execution
// ProviderName: "genesis"

CreateAsync(characterId):
  // Look up genesis entity by characterId
  QUERY genesis-entities WHERE $.CharacterId == characterId
  IF entity == null -> RETURN null provider (no genesis entity for this character)
  READ genesis-templates:"template:{entity.templateCode}"
  READ genesis-entity-cache:"caps:{entity.entityId}"
  IF cache miss
    CALL ISeedClient.GetCapabilityManifestAsync({ seedId: entity.seedId })
  RETURN GenesisVariableProvider(entity, template, capabilities)

// Variables provided:
// ${genesis.templateCode}              -> entity.templateCode
// ${genesis.currentPhase}              -> entity.currentPhase
// ${genesis.cognitiveStage}            -> entity.cognitiveStage
// ${genesis.entityId}                  -> entity.entityId
// ${genesis.physicalFormType}          -> entity.physicalFormType
// ${genesis.physicalFormId}            -> entity.physicalFormId
// ${genesis.wallet.<code>}             -> lazy: CALL ICurrencyClient.GetBalanceAsync({ walletId, currencyDefinitionId })
// ${genesis.wallet.<code>.cap}         -> from CurrencyDefinition.perWalletCap (resolved via GetCurrencyDefinitionAsync)
// ${genesis.wallet.<code>.rate}        -> from CurrencyDefinition.autogainAmount (resolved via GetCurrencyDefinitionAsync)
// ${genesis.wallet.<code>.ratio}       -> balance / perWalletCap
// ${genesis.inventory.<code>.count}    -> lazy: CALL IInventoryClient.GetContainerSummary
// ${genesis.inventory.<code>.capacity} -> template inventory config
// ${genesis.inventory.<code>.full}     -> count >= capacity
// ${genesis.capability.<code>}         -> capabilities.HasCapability(code)
// ${genesis.capability.count}          -> capabilities.Count
// ${genesis.bond.active}               -> entity.bondTargetEntityId != null
// ${genesis.bond.targetId}             -> entity.bondTargetEntityId
// ${genesis.bond.targetType}           -> entity.bondTargetEntityType
```

---

*Implementation exists for all 19 schema endpoints. DI listeners (ICurrencyTransactionListener, ISeedEvolutionListener), variable provider (IVariableProviderFactory), growth flush worker, and plugin lifecycle hooks are specified in the deep dive but not yet implemented — pseudo-code for those sections represents intended behavior.*
