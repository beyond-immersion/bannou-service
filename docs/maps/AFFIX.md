# Affix Implementation Map

> **Plugin**: lib-affix
> **Schema**: schemas/affix-api.yaml
> **Layer**: GameFeatures
> **Deep Dive**: [docs/plugins/AFFIX.md](../plugins/AFFIX.md)
> **Status**: Aspirational -- pseudo-code represents intended behavior, not verified implementation

---

## Summary Table

| Field | Value |
|-------|-------|
| Plugin | lib-affix |
| Layer | L4 GameFeatures |
| Endpoints | 27 |
| State Stores | affix-definitions (MySQL), affix-implicit-mappings (MySQL), affix-instances (MySQL), affix-definition-cache (Redis), affix-instance-cache (Redis), affix-pool-cache (Redis), affix-lock (Redis) |
| Events Published | 10 (affix.definition.created, affix.definition.updated, affix.instance.initialized, affix.modifier.applied, affix.modifier.removed, affix.modifier.rerolled, affix.instance.state-changed, affix.influence.changed, affix.batch.generated, affix.rarity.changed) |
| Events Consumed | 2 (item.template.created, item.template.updated) |
| Client Events | 0 |
| Background Services | 1 (OrphanReconciliationWorker) |

---

## State

**Store**: `affix-definitions` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `def:{definitionId}` | `AffixDefinitionModel` | Primary definition lookup by ID |
| `def-code:{gameServiceId}:{code}` | `AffixDefinitionModel` | Code-uniqueness lookup within game service |

**Store**: `affix-implicit-mappings` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `impl:{mappingId}` | `ImplicitMappingModel` | Primary mapping lookup by ID |
| `impl-tpl:{gameServiceId}:{itemTemplateCode}` | `ImplicitMappingModel` | Lookup implicits for an item template code |

**Store**: `affix-instances` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `inst:{itemInstanceId}` | `AffixInstanceModel` | Per-item affix state (slots, rolled values, states, influences, quality) |
| `inst-game:{gameServiceId}` | `List<string>` | Index of all affix instance item IDs for a game service (cleanup) |

**Store**: `affix-definition-cache` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `def:{definitionId}` | `AffixDefinitionModel` | Definition hot cache (read-through from MySQL) |
| `def-group:{gameServiceId}:{modGroup}` | `List<AffixDefinitionModel>` | All definitions in a mod group (exclusivity validation, tier listing) |

**Store**: `affix-instance-cache` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `inst:{itemInstanceId}` | `AffixInstanceModel` | Instance hot cache (read-through from MySQL) |
| `stats:{itemInstanceId}` | `ComputedStatsModel` | Cached computed stats for an item (derived) |
| `equip:{entityId}:{entityType}` | `EquipmentStatsModel` | Cached aggregate equipment stats for an entity (derived) |

**Store**: `affix-pool-cache` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `pool:{gameServiceId}:{itemClass}:{slotType}:{ilvlBucket}` | `CachedAffixPool` | Pre-computed affix pool with weights for fast generation |
| `pool-inf:{gameServiceId}:{itemClass}:{slotType}:{ilvlBucket}:{influenceKey}` | `CachedAffixPool` | Influence-specific pool extension |

**Store**: `affix-lock` (Backend: Redis)

| Key Pattern | Purpose |
|-------------|---------|
| `def:{definitionId}` | Definition mutation lock |
| `item:{itemInstanceId}` | Item affix modification lock |
| `pool-rebuild:{gameServiceId}` | Pool cache rebuild lock (singleton rebuild on invalidation) |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | 7 stores: 3 MySQL (definitions, implicit mappings, instances), 3 Redis cache (definition, instance, pool), 1 Redis lock |
| lib-state (IDistributedLockProvider) | L0 | Hard | Definition mutation locks, per-item affix modification locks, pool rebuild locks |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing 10 event topics |
| lib-messaging (IEventConsumer) | L0 | Hard | Subscribing to item.template.created and item.template.updated |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Span instrumentation (`"bannou.affix"` component) |
| lib-resource (IResourceClient) | L1 | Hard | Cleanup callback registration for game-service deletion (OnRunningAsync) |
| lib-item (IItemClient) | L2 | Hard | Item existence validation, template lookups for base stats and item class resolution |
| lib-game-service (IGameServiceClient) | L2 | Hard | Game service existence validation for definition scoping |
| lib-inventory (IInventoryClient) | L2 | Hard | Equipment container queries for stat computation, socket container detection |
| lib-analytics (IAnalyticsClient) | L4 | Soft | Affix generation statistics for economy monitoring (graceful degradation if absent) |

**DI Provider/Listener Interfaces**:
- Implements `IItemInstanceDestructionListener` — receives in-process cleanup from lib-item (L2) when item instances are destroyed. High-frequency exception: deletes from MySQL and invalidates Redis cache (distributed state, multi-node safe). Orphan reconciliation worker provides durability guarantee.
- Implements `IVariableProviderFactory` — provides `${affix.*}` variable namespace to Actor (L2) behavior system for NPC item evaluation.
- Implements `ISeededResourceProvider` — `SourceType => "affix"` for game-service deletion cleanup via lib-resource (L1).

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `affix.definition.created` | `AffixDefinitionCreatedEvent` | CreateDefinition (lifecycle) |
| `affix.definition.updated` | `AffixDefinitionUpdatedEvent` | UpdateDefinition, DeprecateDefinition (lifecycle; changedFields includes deprecation fields) |
| `affix.instance.initialized` | `AffixInstanceInitializedEvent` | InitializeItemAffixes |
| `affix.modifier.applied` | `AffixModifierAppliedEvent` | ApplyAffix |
| `affix.modifier.removed` | `AffixModifierRemovedEvent` | RemoveAffix |
| `affix.modifier.rerolled` | `AffixModifierRerolledEvent` | RerollValues |
| `affix.instance.state-changed` | `AffixInstanceStateChangedEvent` | SetItemState |
| `affix.influence.changed` | `AffixInfluenceChangedEvent` | SetInfluence (meaningful state change must publish event) |
| `affix.batch.generated` | `AffixBatchGeneratedEvent` | BatchGenerateAffixSets (deduped by source within configurable window) |
| `affix.rarity.changed` | `AffixRarityChangedEvent` | ApplyAffix, RemoveAffix (only when effective rarity transitions) |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `item.template.created` | `HandleItemTemplateCreated` | Check if new template has implicit mappings; warm pool cache for template's category |
| `item.template.updated` | `HandleItemTemplateUpdated` | Filter for changedFields containing isDeprecated; invalidate pool cache entries for deprecated template's category |

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<AffixService>` | Structured logging |
| `AffixServiceConfiguration` | Typed configuration access (18 properties) |
| `IStateStoreFactory` | State store access (creates 7 stores) |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Event subscription for item template lifecycle events |
| `IDistributedLockProvider` | Distributed lock acquisition |
| `ITelemetryProvider` | Telemetry span instrumentation |
| `IItemClient` | Item template lookups, item existence validation (L2) |
| `IGameServiceClient` | Game service existence validation (L2) |
| `IInventoryClient` | Equipment container queries, socket detection (L2) |
| `IResourceClient` | Cleanup callback registration (L1, OnRunningAsync) |
| `IServiceProvider` | Runtime resolution of soft L4 dependencies (Analytics) |
| `AffixPoolBuilder` | Internal helper: pool cache construction, weight computation |
| `AffixStatComputer` | Internal helper: stat aggregation (base + affixes + quality + sockets) |
| `AffixItemDestructionListener` | DI Listener: IItemInstanceDestructionListener implementation |
| `AffixItemEvaluationProviderFactory` | DI Provider: IVariableProviderFactory for ${affix.*} namespace |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| CreateDefinition | POST /affix/definition/create | developer | definition, def-code, def-cache, pool-cache | affix.definition.created |
| GetDefinition | POST /affix/definition/get | developer | - | - |
| ListDefinitions | POST /affix/definition/list | developer | - | - |
| UpdateDefinition | POST /affix/definition/update | developer | definition, def-cache, mod-group-cache, pool-cache | affix.definition.updated |
| DeprecateDefinition | POST /affix/definition/deprecate | developer | definition, def-cache, mod-group-cache, pool-cache | affix.definition.updated |
| SeedDefinitions | POST /affix/definition/seed | developer | definition, def-code, pool-cache | - |
| ListModGroups | POST /affix/definition/list-mod-groups | developer | - | - |
| CreateImplicitMapping | POST /affix/implicit/create | developer | implicit-mapping | - |
| GetImplicitMapping | POST /affix/implicit/get | developer | - | - |
| SeedImplicitMappings | POST /affix/implicit/seed | developer | implicit-mapping | - |
| RollImplicits | POST /affix/implicit/roll | developer | - | - |
| InitializeItemAffixes | POST /affix/initialize | [] | instance, instance-cache, instance-game-index | affix.instance.initialized |
| GetAffixInstance | POST /affix/instance/get | [] | - | - |
| ApplyAffix | POST /affix/apply | [] | instance, instance-cache, stats-cache | affix.modifier.applied, affix.rarity.changed |
| RemoveAffix | POST /affix/remove | [] | instance, instance-cache, stats-cache | affix.modifier.removed, affix.rarity.changed |
| RerollValues | POST /affix/reroll-values | [] | instance, instance-cache, stats-cache | affix.modifier.rerolled |
| SetItemState | POST /affix/state/set | [] | instance, instance-cache, stats-cache | affix.instance.state-changed |
| SetInfluence | POST /affix/influence/set | [] | instance, instance-cache, pool-cache | affix.influence.changed |
| GenerateAffixPool | POST /affix/generate/pool | [] | pool-cache (on miss) | - |
| GenerateAffixSet | POST /affix/generate/set | [] | - | - |
| BatchGenerateAffixSets | POST /affix/generate/batch | [] | - | affix.batch.generated |
| GetItemAffixes | POST /affix/item/get | [] | - | - |
| ComputeItemStats | POST /affix/item/compute-stats | [] | stats-cache | - |
| ComputeEquipmentStats | POST /affix/equipment/compute | [] | equip-cache | - |
| CompareItems | POST /affix/item/compare | [] | - | - |
| EstimateItemValue | POST /affix/item/estimate-value | [] | - | - |
| CleanupByGameService | POST /affix/cleanup-by-game-service | [] | all stores | - |

---

## Methods

### CreateDefinition
POST /affix/definition/create | Roles: [developer]

CALL IGameServiceClient.GetGameServiceAsync(gameServiceId) -> 400 if not found
// Validate statGrants has at least one entry -> 400 if empty
READ _definitionStore:"def-code:{gameServiceId}:{code}" -> 409 if non-null
COUNT _definitionStore WHERE $.gameServiceId = gameServiceId -> 400 if >= config.MaxDefinitionsPerGameService
WRITE _definitionStore:"def:{definitionId}" <- AffixDefinitionModel from request
WRITE _definitionStore:"def-code:{gameServiceId}:{code}" <- AffixDefinitionModel
WRITE _definitionCache:"def:{definitionId}" <- AffixDefinitionModel (TTL: DefinitionCacheTtlSeconds)
FOREACH itemClass in definition.validItemClasses
 DELETE _poolCache:"pool:{gameServiceId}:{itemClass}:*"
PUBLISH affix.definition.created { definitionId, gameServiceId, code, slotType, modGroup, tier, statGrants }
RETURN (200, CreateDefinitionResponse)

---

### GetDefinition
POST /affix/definition/get | Roles: [developer]

IF request has definitionId
 READ _definitionCache:"def:{definitionId}"
 IF cache miss
 READ _definitionStore:"def:{definitionId}" -> 404 if null
 WRITE _definitionCache:"def:{definitionId}" <- definition (TTL: DefinitionCacheTtlSeconds)
ELSE // lookup by gameServiceId + code
 READ _definitionCache:"def-code:{gameServiceId}:{code}"
 IF cache miss
 READ _definitionStore:"def-code:{gameServiceId}:{code}" -> 404 if null
 WRITE _definitionCache:"def:{definitionId}" <- definition (TTL: DefinitionCacheTtlSeconds)
RETURN (200, GetDefinitionResponse)

---

### ListDefinitions
POST /affix/definition/list | Roles: [developer]

QUERY _definitionStore WHERE
 $.gameServiceId = gameServiceId
 AND $.slotType = slotType (if provided)
 AND $.modGroup = modGroup (if provided)
 AND $.category = category (if provided)
 AND $.tags CONTAINS ANY tags (if provided)
 AND $.tier >= tierMin (if provided)
 AND $.tier <= tierMax (if provided)
 AND $.requiredInfluences CONTAINS requiredInfluence (if provided)
 AND ($.isDeprecated = false OR includeDeprecated = true)
 ORDER BY $.modGroup ASC, $.tier ASC
 PAGED(page, pageSize)
RETURN (200, PagedAffixDefinitionsResponse)

---

### UpdateDefinition
POST /affix/definition/update | Roles: [developer]

READ _definitionStore:"def:{definitionId}" [with ETag] -> 404 if null
LOCK _lockStore:"def:{definitionId}" -> 409 if fails
 // Validate no identity-level field changes (code, gameServiceId, slotType, modGroup) -> 400
 // Apply partial update: merge non-null request fields into existing model
 ETAG-WRITE _definitionStore:"def:{definitionId}" <- updatedModel -> 409 if ETag mismatch
 DELETE _definitionCache:"def:{definitionId}"
 DELETE _definitionCache:"def-group:{gameServiceId}:{modGroup}"
 IF generation-relevant fields changed (statGrants, spawnWeight, spawnTagModifiers, validItemClasses, requiredItemLevel, requiredInfluences)
 FOREACH itemClass in affected classes
 DELETE _poolCache:"pool:{gameServiceId}:{itemClass}:*"
 PUBLISH affix.definition.updated { definitionId, gameServiceId, changedFields }
RETURN (200, UpdateDefinitionResponse)

---

### DeprecateDefinition
POST /affix/definition/deprecate | Roles: [developer]

READ _definitionStore:"def:{definitionId}" [with ETag] -> 404 if null
IF already deprecated
 RETURN (200, DeprecateDefinitionResponse) // idempotent
LOCK _lockStore:"def:{definitionId}" -> 409 if fails
 // Set isDeprecated=true, deprecatedAt=now, deprecationReason from request
 ETAG-WRITE _definitionStore:"def:{definitionId}" <- updatedModel -> 409 if ETag mismatch
 DELETE _definitionCache:"def:{definitionId}"
 DELETE _definitionCache:"def-group:{gameServiceId}:{modGroup}"
 FOREACH itemClass in definition.validItemClasses
 DELETE _poolCache:"pool:{gameServiceId}:{itemClass}:*"
 PUBLISH affix.definition.updated { definitionId, gameServiceId, changedFields: [isDeprecated, deprecatedAt, deprecationReason] }
RETURN (200, DeprecateDefinitionResponse)

---

### SeedDefinitions
POST /affix/definition/seed | Roles: [developer]

CALL IGameServiceClient.GetGameServiceAsync(gameServiceId) -> 400 if not found
FOREACH definition in request.definitions
 READ _definitionStore:"def-code:{gameServiceId}:{code}"
 IF non-null -> skip (increment skippedCount)
 ELSE
 WRITE _definitionStore:"def:{definitionId}" <- AffixDefinitionModel
 WRITE _definitionStore:"def-code:{gameServiceId}:{code}" <- AffixDefinitionModel
 // increment createdCount
// Pool cache invalidation deferred to end (once for all affected item classes)
FOREACH itemClass in all created definitions' validItemClasses (deduped)
 DELETE _poolCache:"pool:{gameServiceId}:{itemClass}:*"
RETURN (200, SeedDefinitionsResponse { createdCount, skippedCount })

---

### ListModGroups
POST /affix/definition/list-mod-groups | Roles: [developer]

QUERY _definitionStore WHERE $.gameServiceId = gameServiceId
 AND ($.isDeprecated = false OR includeDeprecated = true)
// Group by modGroup in memory, count definitions per group
RETURN (200, ListModGroupsResponse { modGroups: [{ code, definitionCount }] })

---

### CreateImplicitMapping
POST /affix/implicit/create | Roles: [developer]

READ _implicitMappingStore:"impl-tpl:{gameServiceId}:{itemTemplateCode}" -> 409 if non-null
FOREACH definitionId in request.implicitDefinitionIds
 READ _definitionStore:"def:{definitionId}" -> 400 if null
 // Validate definition.slotType == "implicit" -> 400 if not
WRITE _implicitMappingStore:"impl:{mappingId}" <- ImplicitMappingModel
WRITE _implicitMappingStore:"impl-tpl:{gameServiceId}:{itemTemplateCode}" <- ImplicitMappingModel
RETURN (200, ImplicitMappingResponse)

---

### GetImplicitMapping
POST /affix/implicit/get | Roles: [developer]

READ _implicitMappingStore:"impl-tpl:{gameServiceId}:{itemTemplateCode}" -> 404 if null
RETURN (200, ImplicitMappingResponse)

---

### SeedImplicitMappings
POST /affix/implicit/seed | Roles: [developer]

FOREACH mapping in request.mappings
 READ _implicitMappingStore:"impl-tpl:{gameServiceId}:{itemTemplateCode}"
 IF non-null -> skip (increment skippedCount)
 ELSE
 // Validate all referenced definitions exist and have slotType "implicit"
 WRITE _implicitMappingStore:"impl:{mappingId}" <- ImplicitMappingModel
 WRITE _implicitMappingStore:"impl-tpl:{gameServiceId}:{itemTemplateCode}" <- ImplicitMappingModel
 // increment createdCount
RETURN (200, SeedImplicitMappingsResponse { createdCount, skippedCount })

---

### RollImplicits
POST /affix/implicit/roll | Roles: [developer]

READ _implicitMappingStore:"impl-tpl:{gameServiceId}:{itemTemplateCode}" -> 404 if null
FOREACH implicit definition ref in mapping
 READ _definitionCache:"def:{definitionId}"
 IF cache miss
 READ _definitionStore:"def:{definitionId}" -> 400 if null
 // Roll values: for each statGrant, random between min/max (using override ranges if present)
// Pure computation -- no state is persisted
RETURN (200, RolledImplicitsResponse { rolledSlots })

---

### InitializeItemAffixes
POST /affix/initialize | Roles: []

CALL IItemClient.GetItemInstanceAsync(itemInstanceId) -> 400 if not found
READ _instanceStore:"inst:{itemInstanceId}" -> 409 if non-null (already initialized)
// Construct AffixInstanceModel from request.affixSetData
WRITE _instanceStore:"inst:{itemInstanceId}" <- AffixInstanceModel
WRITE _instanceCache:"inst:{itemInstanceId}" <- AffixInstanceModel (TTL: InstanceCacheTtlSeconds)
WRITE _instanceStore:"inst-game:{gameServiceId}" <- append itemInstanceId to index
PUBLISH affix.instance.initialized { itemInstanceId, gameServiceId, effectiveRarity, itemLevel }
RETURN (200, AffixInstanceResponse)

---

### GetAffixInstance
POST /affix/instance/get | Roles: []

READ _instanceCache:"inst:{itemInstanceId}"
IF cache miss
 READ _instanceStore:"inst:{itemInstanceId}" -> 404 if null
 WRITE _instanceCache:"inst:{itemInstanceId}" <- instance (TTL: InstanceCacheTtlSeconds)
RETURN (200, AffixInstanceResponse)

---

### ApplyAffix
POST /affix/apply | Roles: []

LOCK _lockStore:"item:{itemInstanceId}" -> 409 if fails
 READ _instanceStore:"inst:{itemInstanceId}" [with ETag]
 IF null -> create empty AffixInstanceModel (first affix on unmanaged item)
 READ _definitionCache:"def:{definitionId}"
 IF cache miss
 READ _definitionStore:"def:{definitionId}" -> 400 if null
 // Validate definition not deprecated -> 400 if deprecated (Category B guard)
 CALL IItemClient.GetItemInstanceAsync(itemInstanceId) -> 400 if not found
 // Validate: validItemClasses includes item's class -> 400 if invalid
 // Validate: itemLevel >= definition.requiredItemLevel -> 400 if insufficient
 // Validate: not corrupted, not mirrored -> 400 if either true
 // Validate: slot type has capacity -> 400 if full
 // Validate: modGroup not occupied -> 409 if occupied
 // Validate: requiredInfluences subset of instance.influences -> 400 if not met
 // Roll values for each statGrant
 // Append AffixSlotModel to appropriate slot array
 // Recompute effectiveRarity
 ETAG-WRITE _instanceStore:"inst:{itemInstanceId}" <- updatedInstance -> 409 if ETag mismatch
 DELETE _instanceCache:"inst:{itemInstanceId}"
 DELETE _instanceCache:"stats:{itemInstanceId}"
 PUBLISH affix.modifier.applied { itemInstanceId, definitionId, definitionCode, slotType, rolledValues, modGroup }
 IF effectiveRarity changed
 PUBLISH affix.rarity.changed { itemInstanceId, previousRarity, newRarity }
RETURN (200, ApplyAffixResponse)

---

### RemoveAffix
POST /affix/remove | Roles: []

LOCK _lockStore:"item:{itemInstanceId}" -> 409 if fails
 READ _instanceStore:"inst:{itemInstanceId}" [with ETag] -> 404 if null
 // Find target affix slot by definitionId -> 404 if not present
 // Validate: not corrupted, not mirrored -> 400 if either true
 // Validate: target slot not fractured -> 400 if fractured
 // Remove AffixSlotModel from slot array
 // Recompute effectiveRarity
 ETAG-WRITE _instanceStore:"inst:{itemInstanceId}" <- updatedInstance -> 409 if ETag mismatch
 DELETE _instanceCache:"inst:{itemInstanceId}"
 DELETE _instanceCache:"stats:{itemInstanceId}"
 PUBLISH affix.modifier.removed { itemInstanceId, definitionId, definitionCode, slotType, modGroup }
 IF effectiveRarity changed
 PUBLISH affix.rarity.changed { itemInstanceId, previousRarity, newRarity }
RETURN (200, RemoveAffixResponse)

---

### RerollValues
POST /affix/reroll-values | Roles: []

LOCK _lockStore:"item:{itemInstanceId}" -> 409 if fails
 READ _instanceStore:"inst:{itemInstanceId}" [with ETag] -> 404 if null
 // Find target affix slot by definitionId -> 404 if not present
 // Validate: not corrupted, not mirrored -> 400 if either true
 // Note: isFractured does NOT block reroll (only removal is blocked)
 READ _definitionCache:"def:{definitionId}"
 IF cache miss
 READ _definitionStore:"def:{definitionId}" -> 400 if null
 // Capture previous rolledValues
 // Re-roll: for each statGrant, new random between min/max
 // Update AffixSlotModel.rolledValues
 ETAG-WRITE _instanceStore:"inst:{itemInstanceId}" <- updatedInstance -> 409 if ETag mismatch
 DELETE _instanceCache:"inst:{itemInstanceId}"
 DELETE _instanceCache:"stats:{itemInstanceId}"
 PUBLISH affix.modifier.rerolled { itemInstanceId, definitionId, definitionCode, previousValues, newValues }
RETURN (200, RerollValuesResponse)

---

### SetItemState
POST /affix/state/set | Roles: []

LOCK _lockStore:"item:{itemInstanceId}" -> 409 if fails
 READ _instanceStore:"inst:{itemInstanceId}" [with ETag] -> 404 if null
 // Validate state transition legality:
 // Cannot uncorrupt, unmirror, or unsplit -> 400 if attempted
 // Apply state flag changes to AffixStatesModel
 IF request includes definitionId for fracture
 // Find affix slot, set isFractured = true -> 404 if slot not found
 ETAG-WRITE _instanceStore:"inst:{itemInstanceId}" <- updatedInstance -> 409 if ETag mismatch
 DELETE _instanceCache:"inst:{itemInstanceId}"
 DELETE _instanceCache:"stats:{itemInstanceId}"
 PUBLISH affix.instance.state-changed { itemInstanceId, changedFlags: [{ flagName, oldValue, newValue }] }
RETURN (200, SetItemStateResponse)

---

### SetInfluence
POST /affix/influence/set | Roles: []

LOCK _lockStore:"item:{itemInstanceId}" -> 409 if fails
 READ _instanceStore:"inst:{itemInstanceId}" [with ETag] -> 404 if null
 // Validate: not mirrored -> 400 if mirrored
 // Update influences array from request
 ETAG-WRITE _instanceStore:"inst:{itemInstanceId}" <- updatedInstance -> 409 if ETag mismatch
 DELETE _instanceCache:"inst:{itemInstanceId}"
 // Influence change affects eligible affix pools for this item
 DELETE _poolCache:"pool:{gameServiceId}:{itemClass}:*" // invalidate pools for affected item class
 PUBLISH affix.influence.changed { itemInstanceId, previousInfluences, newInfluences }
RETURN (200, SetInfluenceResponse)

---

### GenerateAffixPool
POST /affix/generate/pool | Roles: []

// Compute ilvlBucket = floor(itemLevel / config.ItemLevelBucketSize) * config.ItemLevelBucketSize
READ _poolCache:"pool:{gameServiceId}:{itemClass}:{slotType}:{ilvlBucket}"
IF cache miss
 LOCK _lockStore:"pool-rebuild:{gameServiceId}"
 QUERY _definitionStore WHERE $.gameServiceId = gameServiceId
 AND $.slotType = slotType
 AND $.validItemClasses CONTAINS itemClass
 AND $.isDeprecated = false
 AND $.requiredItemLevel <= ilvlBucket upper bound
 // Build CachedAffixPool with weighted entries (base weight x tag modifiers)
 WRITE _poolCache:"pool:{gameServiceId}:{itemClass}:{slotType}:{ilvlBucket}" <- pool (TTL: PoolCacheTtlSeconds)
 IF influences provided
 // Build influence-extended pool
 WRITE _poolCache:"pool-inf:{gameServiceId}:{itemClass}:{slotType}:{ilvlBucket}:{influenceKey}" <- pool
// In-memory filter: exclude requiredItemLevel > itemLevel, exclude existingModGroups
// Apply externalWeightModifiers, exclude entries with weight <= 0
RETURN (200, AffixPoolResponse { entries with effectiveWeight and statGrantRanges })

---

### GenerateAffixSet
POST /affix/generate/set | Roles: []

READ _implicitMappingStore:"impl-tpl:{gameServiceId}:{itemTemplateCode}"
IF mapping exists
 // Roll implicits (same as RollImplicits logic)
// Determine target prefix/suffix counts from targetRarity and config defaults
FOREACH slot to fill (prefix, suffix)
 // Invoke GenerateAffixPool internal logic (pool cache + in-memory filter)
 // Weighted random select one definition
 // Roll values for selected definition
 // Add selected modGroup to exclusion list for subsequent selections
// Pure computation -- no state is persisted
RETURN (200, AffixSetDataResponse { implicitSlots, prefixSlots, suffixSlots, effectiveRarity, itemLevel })

---

### BatchGenerateAffixSets
POST /affix/generate/batch | Roles: []

FOREACH item in request.items (parallel where pool is cached)
 // Same logic as GenerateAffixSet per item
// Batch event deduplication by source within window:
READ Redis dedup key for {source}:{windowBucket}
IF window not yet published
 WRITE Redis dedup key (TTL: GenerationEventDeduplicationWindowSeconds)
 PUBLISH affix.batch.generated { sourceId, batchSize, gameServiceId }
// Soft dependency: analytics
IF IAnalyticsClient available via IServiceProvider
 CALL IAnalyticsClient.PublishEventAsync(generation statistics)
RETURN (200, BatchAffixSetDataResponse { results })

---

### GetItemAffixes
POST /affix/item/get | Roles: []

READ _instanceCache:"inst:{itemInstanceId}"
IF cache miss
 READ _instanceStore:"inst:{itemInstanceId}" -> 404 if null
 WRITE _instanceCache:"inst:{itemInstanceId}" <- instance (TTL: InstanceCacheTtlSeconds)
FOREACH slot in (implicitSlots + prefixSlots + suffixSlots + enchantSlots)
 READ _definitionCache:"def:{slot.definitionId}"
 IF cache miss
 READ _definitionStore:"def:{slot.definitionId}"
 // Enrich slot with: displayName, tier, category, statGrants ranges
IF states.isIdentified == false
 // Return slot counts but withhold stat details (rolledValues = null)
RETURN (200, EnrichedAffixInstanceResponse)

---

### ComputeItemStats
POST /affix/item/compute-stats | Roles: []

READ _instanceCache:"stats:{itemInstanceId}"
IF cache hit -> RETURN (200, ComputedItemStatsResponse)
READ _instanceCache:"inst:{itemInstanceId}"
IF cache miss
 READ _instanceStore:"inst:{itemInstanceId}" -> 404 if null
CALL IItemClient.GetItemTemplateAsync(templateId) // for base stats
// Aggregate via AffixStatComputer:
// base stats + implicit values + explicit values + quality modifier
// value = rolledValue x (1 + quality/100)
IF config.IncludeSocketStatsInEquipment
 CALL IInventoryClient.GetContainerChildrenAsync(containerId, type: "socket")
 FOREACH socket container
 READ _instanceStore:"inst:{socketedGemInstanceId}"
 IF gem has affix instance -> add gem stat values
WRITE _instanceCache:"stats:{itemInstanceId}" <- computedStats (TTL: ComputedStatsCacheTtlSeconds)
RETURN (200, ComputedItemStatsResponse { stats, qualityModifier })

---

### ComputeEquipmentStats
POST /affix/equipment/compute | Roles: []

READ _instanceCache:"equip:{entityId}:{entityType}"
IF cache hit -> RETURN (200, EquipmentStatsResponse)
CALL IInventoryClient.ListContainersAsync(ownerType, ownerId, isEquipmentSlot: true)
FOREACH equipment container (parallel)
 FOREACH item in container
 READ _instanceStore:"inst:{itemInstanceId}" (or cache)
 IF affix instance exists
 // Compute item stats (same as ComputeItemStats, including sockets)
// Aggregate per-stat totals across all equipped items
WRITE _instanceCache:"equip:{entityId}:{entityType}" <- equipmentStats (TTL: EquipmentStatsCacheTtlSeconds)
RETURN (200, EquipmentStatsResponse { perStatTotals, perItemBreakdown })

---

### CompareItems
POST /affix/item/compare | Roles: []

// For each of the two itemInstanceIds:
READ _instanceStore:"inst:{itemInstanceIdA}" (or cache) -> 404 if null
READ _instanceStore:"inst:{itemInstanceIdB}" (or cache) -> 404 if null
CALL IItemClient.GetItemTemplateAsync(templateIdA) // base stats for item A
CALL IItemClient.GetItemTemplateAsync(templateIdB) // base stats for item B
// Compute stats for each via AffixStatComputer (without socket aggregation for speed)
// Diff: for each statCode in either -> { statCode, valueA, valueB, delta, winner }
RETURN (200, ItemComparisonResponse { statDiffs })

---

### EstimateItemValue
POST /affix/item/estimate-value | Roles: []

READ _instanceStore:"inst:{itemInstanceId}" (or cache) -> 404 if null
FOREACH affix slot in instance
 READ _definitionCache:"def:{slot.definitionId}"
 IF cache miss
 READ _definitionStore:"def:{slot.definitionId}"
 // Compute tier percentile (= highest, lower tiers score lower)
 READ _definitionCache:"def-group:{gameServiceId}:{modGroup}" // all tiers for percentile calc
 // Compute roll percentile: (rolledValue - minValue) / (maxValue - minValue)
 // Score affix: tierWeight x rollPercentile
// Aggregate: sum of affix scores, weighted by slot type importance
// Apply multipliers: isFractured bonus, influences bonus, quality bonus
// Normalize to 0.0-1.0; compute suggestedCurrencyValue
RETURN (200, ItemValueEstimateResponse { normalizedScore, suggestedCurrencyValue, scoringFactors })

---

### CleanupByGameService
POST /affix/cleanup-by-game-service | Roles: []

// Instance cleanup
READ _instanceStore:"inst-game:{gameServiceId}" // list of all itemInstanceIds
FOREACH itemInstanceId in list
 DELETE _instanceStore:"inst:{itemInstanceId}"
 DELETE _instanceCache:"inst:{itemInstanceId}"
 DELETE _instanceCache:"stats:{itemInstanceId}"
DELETE _instanceStore:"inst-game:{gameServiceId}"
// Definition cleanup
QUERY _definitionStore WHERE $.gameServiceId = gameServiceId
FOREACH definition in results
 DELETE _definitionStore:"def:{definitionId}"
 DELETE _definitionStore:"def-code:{gameServiceId}:{definition.code}"
 DELETE _definitionCache:"def:{definitionId}"
 DELETE _definitionCache:"def-group:{gameServiceId}:{definition.modGroup}" // deduped
// Implicit mapping cleanup
QUERY _implicitMappingStore WHERE $.gameServiceId = gameServiceId
FOREACH mapping in results
 DELETE _implicitMappingStore:"impl:{mappingId}"
 DELETE _implicitMappingStore:"impl-tpl:{gameServiceId}:{mapping.itemTemplateCode}"
// Pool cache cleanup
DELETE _poolCache:"pool:{gameServiceId}:*" // bulk delete by prefix
RETURN (200, empty)

---

## Background Services

### OrphanReconciliationWorker
**Interval**: config.OrphanReconciliationIntervalMinutes (default: 60 minutes)
**Purpose**: Detect and delete affix instances whose item instances no longer exist in lib-item. Provides durability guarantee for missed IItemInstanceDestructionListener notifications.

QUERY _instanceStore WHERE all records PAGED(page, config.OrphanReconciliationBatchSize)
FOREACH page of affix instances
 CALL IItemClient.GetItemInstancesBatchAsync([itemInstanceIds in page])
 FOREACH itemInstanceId where item not found
 DELETE _instanceStore:"inst:{itemInstanceId}"
 DELETE _instanceCache:"inst:{itemInstanceId}"
 DELETE _instanceCache:"stats:{itemInstanceId}"
// IF IItemClient unavailable: skip batch, retry next interval
// Does NOT publish events for orphan deletions (internal maintenance)
