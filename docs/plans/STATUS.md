# Implementation Plan: Status Service (L4 Game Features)

> **Source**: GitHub issues #282, #375 (pipeline architecture), #280 (itemize anything), #281 (License pattern reference)
> **Related**: `docs/plugins/COLLECTION.md`, `docs/plugins/LICENSE.md`, `docs/plugins/SEED.md`, `docs/plugins/GARDENER.md`, `docs/plans/DIVINE.md`
> **Prerequisites**: lib-item (with decay/expiration from #407), lib-inventory, lib-contract, lib-seed, lib-character, lib-collection must be operational
> **Blocking Issues**: #407 (Item Decay/Expiration System) -- lib-status depends on lib-item's native `expiresAt` for timed buff expiration rather than managing its own TTL worker
> **Status**: DRAFT -- Awaiting approval

---

## Resolved Design Decisions

All open questions have been resolved. These decisions are final and inform the implementation below.

### Q1: Scope -> Unified Query Layer (Item-Based + Seed-Derived)

Status is the **universal "what effects does this entity currently have" layer**. It aggregates:
- **Item-based statuses** (buffs, debuffs, death, subscriptions): Temporary effects stored as items in status containers with contract-managed lifecycles.
- **Seed-derived effects** (passive capabilities): Emergent effects computed from seed growth/capability data via `ISeedClient`.

Any system that needs to know "what can this entity do / resist / access" queries lib-status. This maps directly to PLAYER-VISION's progressive agency pipeline: Experience (Collection) -> Understanding (Seed growth) -> Manifestation (Status effects).

### Q2: Storage Pattern -> Items in Inventories

Status follows the established "itemize anything" (#280) pattern used by Collection and License: status effects are item instances in per-entity status containers. This provides:
- Consistency with established patterns
- Contract prebound API integration for lifecycle management
- Item-based queryability and serialization
- Saga compensation pattern (create item first, then contract)

### Q3: MVP Scope -> Phase 1+2 (Core + Stacking)

Covers: Templates, status containers, grant/remove, contract integration, stacking behaviors, HasStatus/ListStatus unified queries, category filtering, duration tracking, seed-derived passive effects. Enough for Divine blessings, basic combat buffs, and the Collection -> Seed -> Status pipeline from #375.

**Not in MVP**: Tick-based effects (DOT/HOT), conditional milestone completion, subscription renewal flows, effect magnitude computation. These are Phase 3+.

### Q4: Entity Scope -> Polymorphic Ownership

Opaque `ownerType` + `ownerId` strings matching Collection/License/Seed pattern. Characters get combat buffs, accounts get subscription benefits, actors/locations could get environmental effects. Maximum flexibility with caller-responsibility validation.

### Q5: Stacking Model -> Five Modes (from #282)

| Mode | Behavior | Implementation |
|------|----------|----------------|
| `refresh_duration` | New application resets timer, preserves/increments stack count | Cancel old contract, create new with extended duration + incremented stack |
| `independent` | Each application is a separate item with own timer | New item + new contract per application |
| `increase_intensity` | Stacks increase effect magnitude, shared timer reset | Cancel old contract, create new with updated values + incremented stack |
| `replace` | New application replaces existing | Remove old item + cancel old contract, create new |
| `ignore` | Can't apply if already present | Return existing without modification |

### Q6: Contract Integration -> Optional Per-Template

Status templates optionally reference a `contractTemplateId`. When present, contract milestones manage lifecycle (duration, conditions, prebound API execution on apply/expire). When absent, lib-item's native decay system (#407) handles expiration -- the status item is created with `expiresAt` set from the template's `defaultDurationSeconds`, and lib-item's background worker destroys it on expiry and publishes `item.expired`. Status subscribes to `item.expired` to clean up its own records and publish `status.expired`. This eliminates the need for a Status-specific expiration worker and keeps time-based lifecycle as a first-class L2 concern. Complex statuses (death, subscriptions) still get full contract power when needed.

### Q7: Seed-Derived Effects -> ISeedEvolutionListener + Direct Query

Status implements `ISeedEvolutionListener` to receive capability change notifications and invalidate its seed effects cache. On unified queries, Status calls `ISeedClient.GetCapabilityManifestAsync` and returns capabilities as passive effects alongside item-based statuses. No custom mapping rules in MVP -- capabilities returned as-is with source attribution.

---

## Context

The Status service is a new L4 Game Features plugin that provides a unified query layer for entity effects -- both temporary contract-managed statuses (buffs, debuffs, death penalties, subscription benefits) stored as items in dedicated containers, and passive seed-derived capabilities computed from growth state.

**The composability thesis** (from #280, #281, #282, #375):
- **Status effects** -> Item instances in status containers (ownership: polymorphic entity)
- **Effect lifecycle** -> Contract (milestone duration, prebound API execution on apply/expire)
- **Effect storage** -> Inventory (container per entity, items per active status)
- **Effect definitions** -> Status templates (category, stacking rules, contract reference)
- **Passive effects** -> Seed capability manifests (queried, cached, aggregated)
- **Cross-pollination** -> Collection (L2) -> Seed (L2) -> Status (L4) pipeline from #375

**Design principle**: Puppetmaster orchestrates what NPCs experience. Gardener orchestrates what players experience. Divine orchestrates what gods experience. **Status provides the current-effects layer that all of these systems query.** It is to effects what Inventory is to items -- the universal container.

**Why L4**: Status depends on Inventory, Item, Seed (L2), and Contract (L1). It is optional -- games without status systems still function. It aggregates data from multiple layers. If L2 services ever need status data, an `IStatusEffectProvider` DI interface can be added (not in MVP). Classic L4 -- optional, feature-rich, maximum connectivity.

**Why not L2?** Despite being "universal," Status's dependencies are all downward from L4. No L2 service currently requires status data for correct operation. Character doesn't need to know about buffs; combat systems (L4) do. If this changes, the DI provider inversion pattern resolves it without hierarchy violations.

**First consumers**: lib-divine (Minor/Standard blessings as temporary status items), combat systems (buffs/debuffs), and any L4 service needing "what effects does this entity have."

---

## Implementation Steps

### Step 1: Create Schema Files

#### 1a. `schemas/status-api.yaml`

- **Header**: `x-service-layer: GameFeatures`, `servers: [{ url: http://localhost:5012 }]`
- All properties have `description` fields (CS1591 compliance)
- NRT compliance: optional ref types have `nullable: true`, required fields in `required` arrays

**~17 POST endpoints** across four API groups:

| Group | Endpoints | Permissions |
|-------|-----------|-------------|
| Template Management (6) | create, get, get-by-code, list, update, seed | `developer` |
| Status Operations (7) | grant, remove, remove-by-source, remove-by-category, has, list, get | `developer` |
| Effects Query (2) | get-effects, get-seed-effects | `developer` |
| Cleanup (1) | cleanup-by-owner | `developer` |

**Note**: The `developer` permission is used for all endpoints because Status is internal-only (never internet-facing). Service-to-service calls bypass permissions; developer allows testing.

**Enums** (defined in `components/schemas`):

```yaml
StatusCategory:
  type: string
  enum: [buff, debuff, death, subscription, event, passive]
  description: Classification of status effects for filtering and cleanse targeting

StackBehavior:
  type: string
  enum: [refresh_duration, independent, increase_intensity, replace, ignore]
  description: How multiple applications of the same status template interact

EffectSource:
  type: string
  enum: [item_based, seed_derived]
  description: Whether an effect comes from a status item or seed capability

StatusRemoveReason:
  type: string
  enum: [expired, cleansed, cancelled, source_removed, admin]
  description: Why a status was removed

GrantFailureReason:
  type: string
  enum: [template_not_found, entity_at_max_statuses, stack_limit_reached,
         stack_behavior_ignore, contract_failed, item_creation_failed]
  description: Why a status grant was rejected
```

**Request models** (one per endpoint):

- `CreateStatusTemplateRequest`: gameServiceId (uuid), code (string), displayName (string), description (string), category (StatusCategory), stackable (bool), maxStacks (int, default 1), stackBehavior (StackBehavior, default ignore), contractTemplateId (uuid, nullable -- omit for simple TTL statuses), itemTemplateId (uuid), defaultDurationSeconds (int, nullable -- for non-contract TTL management), iconAssetId (uuid, nullable)
- `GetStatusTemplateRequest`: statusTemplateId (uuid)
- `GetStatusTemplateByCodeRequest`: gameServiceId (uuid), code (string)
- `ListStatusTemplatesRequest`: gameServiceId (uuid), category (nullable StatusCategory), page (int, default 1), pageSize (int, default 50)
- `UpdateStatusTemplateRequest`: statusTemplateId (uuid), displayName (nullable string), description (nullable string), category (nullable StatusCategory), stackable (nullable bool), maxStacks (nullable int), stackBehavior (nullable StackBehavior), contractTemplateId (nullable uuid), defaultDurationSeconds (nullable int), iconAssetId (nullable uuid)
- `SeedStatusTemplatesRequest`: gameServiceId (uuid), templates (array of CreateStatusTemplateRequest)
- `GrantStatusRequest`: entityId (uuid), entityType (string), gameServiceId (uuid), statusTemplateCode (string), sourceId (uuid, nullable -- what granted this, for cascading removal), durationOverrideSeconds (int, nullable -- override template default), metadata (object, nullable -- arbitrary data passed to contract template values)
- `RemoveStatusRequest`: statusInstanceId (uuid), reason (StatusRemoveReason)
- `RemoveBySourceRequest`: entityId (uuid), entityType (string), sourceId (uuid)
- `RemoveByCategoryRequest`: entityId (uuid), entityType (string), category (StatusCategory), reason (StatusRemoveReason)
- `HasStatusRequest`: entityId (uuid), entityType (string), statusCode (string)
- `ListStatusesRequest`: entityId (uuid), entityType (string), category (nullable StatusCategory), includePassive (bool, default false), page (int, default 1), pageSize (int, default 50)
- `GetStatusRequest`: statusInstanceId (uuid)
- `GetEffectsRequest`: entityId (uuid), entityType (string), includePassive (bool, default true)
- `GetSeedEffectsRequest`: entityId (uuid), entityType (string)
- `CleanupByOwnerRequest`: ownerType (string), ownerId (uuid)

**Response models**:

- `StatusTemplateResponse`: statusTemplateId, gameServiceId, code, displayName, description, category, stackable, maxStacks, stackBehavior, contractTemplateId (nullable), itemTemplateId, defaultDurationSeconds (nullable), iconAssetId (nullable), createdAt, updatedAt
- `ListStatusTemplatesResponse`: templates (array), totalCount, page, pageSize
- `GrantStatusResponse`: statusInstanceId (uuid), statusTemplateCode (string), stackCount (int), contractInstanceId (uuid, nullable), itemInstanceId (uuid), grantedAt (date-time), expiresAt (date-time, nullable), grantResult (string: "granted", "stacked", "refreshed", "replaced")
- `GrantStatusFailedResponse`: reason (GrantFailureReason), existingStatusInstanceId (uuid, nullable -- set when ignore behavior)
- `HasStatusResponse`: hasStatus (bool), statusInstanceId (uuid, nullable), stackCount (int, nullable)
- `StatusInstanceResponse`: statusInstanceId, entityId, entityType, statusTemplateCode, category, stackCount, sourceId (nullable), contractInstanceId (nullable), itemInstanceId, grantedAt, expiresAt (nullable), metadata (object, nullable)
- `ListStatusesResponse`: statuses (array StatusEffectSummary), totalCount, page, pageSize
- `StatusEffectSummary`: statusCode (string), category (StatusCategory), effectSource (EffectSource), stackCount (int, nullable -- null for seed-derived), expiresAt (date-time, nullable), fidelity (float, nullable -- for seed-derived only), seedId (uuid, nullable), sourceId (uuid, nullable)
- `GetEffectsResponse`: entityId, entityType, itemBasedCount (int), seedDerivedCount (int), effects (array StatusEffectSummary)
- `SeedEffectsResponse`: entityId, entityType, effects (array of SeedEffectEntry -- capabilityCode, domain, fidelity, seedId, seedTypeCode)
- `CleanupResponse`: statusesRemoved (int), containersDeleted (int)

**x-references** (for lib-resource cleanup coordination):

```yaml
x-references:
  - target: character
    sourceType: status-container
    field: ownerId
    onDelete: cascade
    cleanup:
      endpoint: /status/cleanup-by-owner
      payloadTemplate: '{"ownerType": "character", "ownerId": "{{resourceId}}"}'
  - target: account
    sourceType: status-container
    field: ownerId
    onDelete: cascade
    cleanup:
      endpoint: /status/cleanup-by-owner
      payloadTemplate: '{"ownerType": "account", "ownerId": "{{resourceId}}"}'
```

#### 1b. `schemas/status-events.yaml`

**x-lifecycle** for `StatusTemplate` entity (generates created/updated/deleted events):
- Model fields: statusTemplateId (primary), gameServiceId, code, displayName, category, stackable, maxStacks, stackBehavior, createdAt, updatedAt
- Sensitive: contractTemplateId (exclude -- internal reference)

**x-event-subscriptions** (consumed events):
- `item.expired` -> `ItemExpiredEvent` -> `HandleItemExpired` -- lib-item's decay worker destroyed an expired item; clean up status instance record, invalidate cache, publish `status.expired` (#407)
- `seed.capability.updated` -> `SeedCapabilityUpdatedEvent` -> `HandleSeedCapabilityUpdated` -- invalidate seed effects cache for the entity

**x-event-publications** (published events):
- Lifecycle events from x-lifecycle: `status-template.created`, `status-template.updated`, `status-template.deleted`
- Custom events:
  - `status.granted` -> `StatusGrantedEvent` -- a status was applied to an entity
  - `status.removed` -> `StatusRemovedEvent` -- a status was removed (any reason)
  - `status.expired` -> `StatusExpiredEvent` -- a status expired via TTL or contract timeout
  - `status.stacked` -> `StatusStackedEvent` -- a status was stacked (count changed)
  - `status.grant-failed` -> `StatusGrantFailedEvent` -- a grant attempt was rejected
  - `status.cleansed` -> `StatusCleansedEvent` -- statuses removed by category (bulk cleanse)

**Custom event schemas** (in `components/schemas`):

```yaml
StatusGrantedEvent:
  type: object
  required: [eventId, entityId, entityType, statusTemplateCode, statusInstanceId, category, stackCount]
  properties:
    eventId: { type: string, format: uuid, description: Unique event identifier }
    entityId: { type: string, format: uuid, description: The entity receiving the status }
    entityType: { type: string, description: Entity type (character, account, etc.) }
    statusTemplateCode: { type: string, description: Status template code }
    statusInstanceId: { type: string, format: uuid, description: The status instance created }
    category: { $ref: 'status-api.yaml#/components/schemas/StatusCategory' }
    stackCount: { type: integer, description: Current stack count after grant }
    sourceId: { type: string, format: uuid, nullable: true, description: What granted this status }
    expiresAt: { type: string, format: date-time, nullable: true, description: When this status expires }
    grantResult: { type: string, description: How the grant resolved (granted, stacked, refreshed, replaced) }

StatusRemovedEvent:
  type: object
  required: [eventId, entityId, entityType, statusTemplateCode, statusInstanceId, reason]
  properties:
    eventId: { type: string, format: uuid, description: Unique event identifier }
    entityId: { type: string, format: uuid, description: The entity losing the status }
    entityType: { type: string, description: Entity type }
    statusTemplateCode: { type: string, description: Status template code }
    statusInstanceId: { type: string, format: uuid, description: The status instance removed }
    reason: { $ref: 'status-api.yaml#/components/schemas/StatusRemoveReason' }

StatusExpiredEvent:
  type: object
  required: [eventId, entityId, entityType, statusTemplateCode, statusInstanceId]
  properties:
    eventId: { type: string, format: uuid, description: Unique event identifier }
    entityId: { type: string, format: uuid, description: The entity whose status expired }
    entityType: { type: string, description: Entity type }
    statusTemplateCode: { type: string, description: Status template code }
    statusInstanceId: { type: string, format: uuid, description: The expired status instance }

StatusStackedEvent:
  type: object
  required: [eventId, entityId, entityType, statusTemplateCode, statusInstanceId, oldStackCount, newStackCount]
  properties:
    eventId: { type: string, format: uuid, description: Unique event identifier }
    entityId: { type: string, format: uuid, description: The entity whose status was stacked }
    entityType: { type: string, description: Entity type }
    statusTemplateCode: { type: string, description: Status template code }
    statusInstanceId: { type: string, format: uuid, description: The status instance }
    oldStackCount: { type: integer, description: Previous stack count }
    newStackCount: { type: integer, description: New stack count after stacking }

StatusGrantFailedEvent:
  type: object
  required: [eventId, entityId, entityType, statusTemplateCode, reason]
  properties:
    eventId: { type: string, format: uuid, description: Unique event identifier }
    entityId: { type: string, format: uuid, description: The entity that was targeted }
    entityType: { type: string, description: Entity type }
    statusTemplateCode: { type: string, description: Status template code attempted }
    reason: { $ref: 'status-api.yaml#/components/schemas/GrantFailureReason' }

StatusCleansedEvent:
  type: object
  required: [eventId, entityId, entityType, category, statusesRemoved]
  properties:
    eventId: { type: string, format: uuid, description: Unique event identifier }
    entityId: { type: string, format: uuid, description: The entity that was cleansed }
    entityType: { type: string, description: Entity type }
    category: { $ref: 'status-api.yaml#/components/schemas/StatusCategory' }
    statusesRemoved: { type: integer, description: Number of statuses removed }
    reason: { $ref: 'status-api.yaml#/components/schemas/StatusRemoveReason' }
```

#### 1c. `schemas/status-configuration.yaml`

All properties with `env: STATUS_{PROPERTY}` format, single-line descriptions:

```yaml
x-service-configuration:
  properties:
    # Limits
    MaxStatusesPerEntity:
      type: integer
      env: STATUS_MAX_STATUSES_PER_ENTITY
      minimum: 1
      maximum: 200
      default: 50
      description: Maximum concurrent active statuses per entity

    MaxStacksPerStatus:
      type: integer
      env: STATUS_MAX_STACKS_PER_STATUS
      minimum: 1
      maximum: 100
      default: 10
      description: Global maximum stack count per status (template maxStacks takes precedence if lower)

    MaxStatusTemplatesPerGameService:
      type: integer
      env: STATUS_MAX_STATUS_TEMPLATES_PER_GAME_SERVICE
      minimum: 1
      maximum: 1000
      default: 200
      description: Maximum status template definitions per game service

    # Cache
    StatusCacheTtlSeconds:
      type: integer
      env: STATUS_STATUS_CACHE_TTL_SECONDS
      minimum: 5
      maximum: 3600
      default: 60
      description: TTL in seconds for active status cache per entity (short -- statuses change frequently)

    SeedEffectsCacheTtlSeconds:
      type: integer
      env: STATUS_SEED_EFFECTS_CACHE_TTL_SECONDS
      minimum: 10
      maximum: 3600
      default: 300
      description: TTL in seconds for seed-derived effects cache (longer -- changes less frequently)

    # Distributed locks
    LockTimeoutSeconds:
      type: integer
      env: STATUS_LOCK_TIMEOUT_SECONDS
      minimum: 5
      maximum: 120
      default: 30
      description: TTL for distributed locks on status mutations

    MaxConcurrencyRetries:
      type: integer
      env: STATUS_MAX_CONCURRENCY_RETRIES
      minimum: 1
      maximum: 10
      default: 3
      description: ETag-based optimistic concurrency retry attempts for cache updates

    # Note: No expiration worker config -- TTL-based expiration delegated to lib-item's
    # native decay system (#407). Status reacts to `item.expired` events.

    # Defaults
    DefaultPageSize:
      type: integer
      env: STATUS_DEFAULT_PAGE_SIZE
      minimum: 1
      maximum: 500
      default: 50
      description: Default page size for paginated queries

    DefaultStatusDurationSeconds:
      type: integer
      env: STATUS_DEFAULT_STATUS_DURATION_SECONDS
      minimum: 1
      maximum: 86400
      default: 60
      description: Default duration when template has no defaultDurationSeconds and no contract

    # Seed integration
    SeedEffectsEnabled:
      type: boolean
      env: STATUS_SEED_EFFECTS_ENABLED
      default: true
      description: Enable seed-derived passive effects in unified queries (disable if Seed is not deployed)
```

#### 1d. Update `schemas/state-stores.yaml`

Add under `x-state-stores:`:

```yaml
status-templates:
  backend: mysql
  service: Status
  purpose: Status template definitions (durable, queryable by category/code/gameServiceId)

status-instances:
  backend: mysql
  service: Status
  purpose: Status instance records with metadata (durable, queryable by entity/source/category)

status-containers:
  backend: mysql
  service: Status
  purpose: Status container records mapping entities to inventory containers (durable)

status-active-cache:
  backend: redis
  prefix: "status:active"
  service: Status
  purpose: Active status cache per entity (fast lookup, rebuilt from instances on miss)

status-seed-effects-cache:
  backend: redis
  prefix: "status:seed"
  service: Status
  purpose: Cached seed-derived effects per entity (invalidated on capability.updated events)

status-lock:
  backend: redis
  prefix: "status:lock"
  service: Status
  purpose: Distributed locks for status mutations and template updates
```

### Step 2: Generate Service (creates project, code, and templates)

```bash
cd scripts && ./generate-service.sh status
```

This single command bootstraps the entire plugin. It auto-creates:

**Plugin project infrastructure** (via `generate-project.sh`):
- `plugins/lib-status/` directory
- `plugins/lib-status/lib-status.csproj` (with ServiceLib.targets import)
- `plugins/lib-status/AssemblyInfo.cs` (ApiController, InternalsVisibleTo)
- Adds `lib-status` to `bannou-service.sln` via `dotnet sln add`

**Generated code** (in `plugins/lib-status/Generated/`):
- `IStatusService.cs` - interface
- `StatusController.cs` - HTTP routing
- `StatusController.Meta.cs` - runtime schema introspection
- `StatusServiceConfiguration.cs` - typed config class
- `StatusPermissionRegistration.cs` - permissions
- `StatusEventsController.cs` - event subscription handlers (from x-event-subscriptions)

**Generated code** (in `bannou-service/Generated/`):
- `Models/StatusModels.cs` - request/response models
- `Clients/StatusClient.cs` - client for other services to call Status
- `Events/StatusEventsModels.cs` - event models
- Updated `StateStoreDefinitions.cs` with Status store constants

**Template files** (created once if missing, never overwritten):
- `plugins/lib-status/StatusService.cs` - business logic template with TODO stubs
- `plugins/lib-status/StatusServiceModels.cs` - internal models template
- `plugins/lib-status/StatusServicePlugin.cs` - plugin registration template

**Test project** (via `generate-tests.sh`):
- `plugins/lib-status.tests/` directory, `.csproj`, `AssemblyInfo.cs`, `GlobalUsings.cs`
- `StatusServiceTests.cs` template with basic tests
- Adds `lib-status.tests` to `bannou-service.sln` via `dotnet sln add`

**Build check**: `dotnet build` to verify generation succeeded.

### Step 3: Fill In Plugin Registration

#### 3a. `plugins/lib-status/StatusServicePlugin.cs` (generated template -> fill in)

The generator creates the skeleton. Fill in following the LicenseServicePlugin pattern:

- Extends `BaseBannouPlugin`
- `PluginName => "status"`, `DisplayName => "Status Service"`
- Standard lifecycle: ConfigureServices, ConfigureApplication, OnStartAsync (creates scope), OnRunningAsync, OnShutdownAsync
- **ConfigureServices**: Register `StatusSeedEvolutionListener` as `ISeedEvolutionListener` singleton.
- **OnRunningAsync**:
  1. Register resource cleanup callbacks with `IResourceClient` for character and account owner types
  2. Log success/failure of cleanup callback registration

### Step 4: Fill In Internal Models

#### 4a. `plugins/lib-status/StatusServiceModels.cs` (generated template -> fill in)

Internal storage models (not API-facing):

- **`StatusTemplateModel`**: StatusTemplateId (Guid), GameServiceId (Guid), Code (string), DisplayName (string), Description (string), Category (StatusCategory), Stackable (bool), MaxStacks (int), StackBehavior (StackBehavior), ContractTemplateId (Guid?), ItemTemplateId (Guid), DefaultDurationSeconds (int?), IconAssetId (Guid?), CreatedAt (DateTimeOffset), UpdatedAt (DateTimeOffset)
- **`StatusInstanceModel`**: StatusInstanceId (Guid), EntityId (Guid), EntityType (string), GameServiceId (Guid), StatusTemplateCode (string), Category (StatusCategory), StackCount (int), SourceId (Guid?), ContractInstanceId (Guid?), ItemInstanceId (Guid), ContainerId (Guid), GrantedAt (DateTimeOffset), ExpiresAt (DateTimeOffset?), Metadata (Dictionary<string, object>?)
- **`StatusContainerModel`**: ContainerId (Guid), EntityId (Guid), EntityType (string), GameServiceId (Guid), InventoryContainerId (Guid), CreatedAt (DateTimeOffset)
- **`ActiveStatusCacheModel`**: EntityId (Guid), EntityType (string), Statuses (List\<CachedStatusEntry\>), LastUpdated (DateTimeOffset)
- **`CachedStatusEntry`**: StatusInstanceId (Guid), StatusTemplateCode (string), Category (StatusCategory), StackCount (int), SourceId (Guid?), ExpiresAt (DateTimeOffset?), ItemInstanceId (Guid)
- **`SeedEffectsCacheModel`**: EntityId (Guid), EntityType (string), Effects (List\<CachedSeedEffect\>), ComputedAt (DateTimeOffset)
- **`CachedSeedEffect`**: CapabilityCode (string), Domain (string), Fidelity (float), SeedId (Guid), SeedTypeCode (string)

All models use proper types per T25 (enums, Guids, DateTimeOffset). Nullable for optional fields per T26.

### Step 5: Create Event Handlers

#### 5a. `plugins/lib-status/StatusServiceEvents.cs` (manual - not auto-generated)

Partial class of StatusService:

- `RegisterEventConsumers(IEventConsumer eventConsumer)` - registers handler for `seed.capability.updated` (soft -- Seed may not be enabled if SeedEffectsEnabled is false)

**Handler implementation**:

- `HandleSeedCapabilityUpdatedAsync(SeedCapabilityUpdatedEvent evt)`:
  1. Extract ownerId from the seed's owner (need to query ISeedClient.GetSeedAsync for the seed's ownerId and ownerType -- soft, skip if unavailable)
  2. Invalidate seed effects cache entry in Redis for this entity
  3. Log the invalidation at debug level

### Step 6: Create DI Listener and Event Handlers

#### 6a. Item Expiration Handling (via `item.expired` event subscription)

**No Status-specific expiration worker.** Time-based expiration is a first-class L2 concern handled by lib-item's native decay system (#407). When a status item's `expiresAt` passes, lib-item's `ItemDecayWorker` destroys the item and publishes `item.expired`.

Status subscribes to `item.expired` in `StatusServiceEvents.cs`:
1. Check if the expired item belongs to a status container (lookup by `containerId` in instance records)
2. If yes: acquire distributed lock on `entity:{entityType}:{entityId}`
3. Delete status instance record from MySQL
4. Invalidate active status cache in Redis
5. Publish `status.expired` event
6. Release lock

This means:
- Simple TTL buffs: lib-item handles the timer, Status reacts to the expiration event
- Contract-backed statuses: Contract manages lifecycle, calls Status `/remove` via prebound API on milestone expiry
- Status never polls for expirations -- it is always event-driven

#### 6b. `plugins/lib-status/Services/StatusSeedEvolutionListener.cs` (manual)

A singleton implementing `ISeedEvolutionListener` for seed capability change notifications. Follows the `GardenerSeedEvolutionListener` pattern.

```csharp
public class StatusSeedEvolutionListener : ISeedEvolutionListener
{
    // InterestedSeedTypes: empty set (interested in ALL seed types)
    // OnGrowthRecordedAsync: no-op (growth alone doesn't change capabilities)
    // OnPhaseChangedAsync: invalidate seed effects cache for entity
    // OnCapabilitiesChangedAsync: invalidate seed effects cache for entity
}
```

**Distributed safety**: Listener reactions delete a Redis cache key (distributed state). All nodes see the invalidation on next read. Safe per SERVICE-HIERARCHY.md DI Provider vs Listener rules.

### Step 7: Implement Service Business Logic

#### 7a. `plugins/lib-status/StatusService.cs` (generated template -> fill in)

Partial class with `[BannouService("status", typeof(IStatusService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFeatures)]`:

**Constructor dependencies**:
- `IStateStoreFactory` - for all state stores
- `IMessageBus` - event publishing
- `IDistributedLockProvider` - concurrent modification safety
- `ILogger<StatusService>` - structured logging
- `StatusServiceConfiguration` - typed config
- `IEventConsumer` - event handler registration
- `IInventoryClient` - status container management (L2 hard dependency)
- `IItemClient` - status item instances (L2 hard dependency)
- `IGameServiceClient` - validate game service existence (L2 hard dependency)
- `IResourceClient` - cleanup callback registration (L1 hard dependency)
- `IServiceProvider` - for optional soft dependencies

**Soft dependencies** (resolved at runtime via `IServiceProvider`, null-checked):
- `IContractClient` - contract lifecycle for statuses with contractTemplateId (L1 -- but resolved soft because some deployments may not use contract-backed statuses)
- `ISeedClient` - seed capability queries for unified effects (L2 -- resolved soft because seed integration is optional via `SeedEffectsEnabled`)
- `ICharacterClient` - character entity validation for character-type owners (L2 -- resolved soft because not all owner types need validation)

**Store initialization** (lazy accessors following License/Collection pattern):
- `TemplateStore` = GetQueryableStore\<StatusTemplateModel\>(StateStoreDefinitions.StatusTemplates)
- `InstanceStore` = GetQueryableStore\<StatusInstanceModel\>(StateStoreDefinitions.StatusInstances)
- `ContainerStore` = GetQueryableStore\<StatusContainerModel\>(StateStoreDefinitions.StatusContainers)
- `ActiveCache` = GetStore\<ActiveStatusCacheModel\>(StateStoreDefinitions.StatusActiveCache)
- `SeedEffectsCache` = GetStore\<SeedEffectsCacheModel\>(StateStoreDefinitions.StatusSeedEffectsCache)
- `_lockProvider` for distributed locks

**Key method implementations** (all follow T7 error handling, T8 return pattern):

| Method | Key Logic |
|--------|-----------|
| `CreateStatusTemplateAsync` | Validate gameServiceId. Validate code uniqueness per game. Validate itemTemplateId exists via `IItemClient`. If contractTemplateId provided, validate it (soft). Check MaxStatusTemplatesPerGameService. Save to MySQL (dual-key: by ID + by code). Publish lifecycle created event. |
| `GetStatusTemplateAsync` | Load from MySQL by ID. 404 if not found. |
| `GetStatusTemplateByCodeAsync` | JSON query by gameServiceId + code. 404 if not found. |
| `ListStatusTemplatesAsync` | Paged JSON query with optional category filter. |
| `UpdateStatusTemplateAsync` | Lock, load, validate, update non-null fields. Publish lifecycle updated event. |
| `SeedStatusTemplatesAsync` | Bulk create with upfront item template validation. Skip duplicates (idempotent). |
| `GrantStatusAsync` | **Core operation -- see detailed flow below.** |
| `RemoveStatusAsync` | Lock entity. Load instance. If contract-backed: cancel contract (soft). Destroy item via `IItemClient`. Delete instance record. Invalidate cache. Publish `status.removed` event. |
| `RemoveBySourceAsync` | Lock entity. Query instances by entityId + sourceId. Remove each (destroy items, cancel contracts, delete records). Invalidate cache. Publish `status.removed` per status. |
| `RemoveByCategoryAsync` | Lock entity. Query instances by entityId + category. Remove each. Invalidate cache. Publish `status.cleansed` event with count. |
| `HasStatusAsync` | Check active cache (rebuild on miss). Return bool + instance ID if found. |
| `ListStatusesAsync` | Load active cache (rebuild on miss). If includePassive: load seed effects cache (rebuild on miss). Merge, filter by category, paginate. |
| `GetStatusAsync` | Load instance by ID from MySQL. 404 if not found. |
| `GetEffectsAsync` | Load both caches. Merge into unified response with source attribution. |
| `GetSeedEffectsAsync` | Load seed effects cache (rebuild on miss). Return seed-derived effects only. |
| `CleanupByOwnerAsync` | Query all containers for ownerType + ownerId. For each: delete inventory container (cascades to items), delete all instance records, delete container record, invalidate caches. Return count. |

**GrantStatusAsync detailed flow** (the core operation):

1. Acquire distributed lock: `entity:{entityType}:{entityId}`
2. Load status template by code + gameServiceId
3. Find or auto-create status container for entity:
   - Query containers by entityId + entityType + gameServiceId
   - If none: create inventory container (`ContainerType = "status_{gameServiceId}"`, `ConstraintModel = Unlimited`), save container record
4. Load active status cache (rebuild from instances if miss)
5. Check existing status of same template code:
   - If exists and `stackBehavior == ignore`: return existing (publish grant-failed event)
   - If exists and `stackBehavior == replace`: remove existing (destroy item, cancel contract, delete record)
   - If exists and `stackBehavior == refresh_duration` or `increase_intensity`: handle stacking (see below)
   - If exists and `stackBehavior == independent`: check maxStacks limit
   - If not exists: proceed with new grant
6. Check MaxStatusesPerEntity limit
7. Calculate expiresAt:
   - From durationOverrideSeconds if provided
   - Else from template defaultDurationSeconds
   - Else from config DefaultStatusDurationSeconds
   - Null if none (permanent until removed)
8. **Create item instance** (saga-ordered: easily reversible):
   - `IItemClient.CreateItemInstanceAsync(templateId, containerId, gameServiceId, quantity=1, expiresAt=expiresAt)`
   - lib-item's native decay system (#407) handles the timer -- no Status worker needed
9. **Contract lifecycle** (if template has contractTemplateId -- soft, skip if IContractClient unavailable):
   - Create contract instance with saga compensation on failure (destroy item if contract fails)
   - Set template values: entityId, entityType, statusCode, duration, stack count, sourceId, plus request metadata
   - Propose + consent (auto, single-party)
   - Complete "apply" milestone (triggers prebound APIs for effect application)
10. Create StatusInstanceModel in MySQL
11. Update active status cache with optimistic concurrency (ETag retry)
12. Publish `status.granted` event
13. Return success response

**Stacking sub-flows**:

- **refresh_duration**: Load existing instance. If contract-backed: cancel old contract, create new contract with reset timer. Update instance: increment stackCount, reset expiresAt on item via `IItemClient.ModifyItemInstanceAsync` (lib-item's decay system respects the updated timer). Update item metadata. Publish `status.stacked` event.
- **increase_intensity**: Same as refresh_duration but contract template values include `{{stackCount}}` for magnitude scaling.
- **independent**: Create entirely new item + contract + instance record. Each stack is an independent entity.

**Cache rebuild helpers**:

- `RebuildActiveCacheAsync(entityId, entityType)`: Query all instances from MySQL for entity. Build cache model. Save to Redis with TTL.
- `RebuildSeedEffectsCacheAsync(entityId, entityType)`: If SeedEffectsEnabled and ISeedClient available: query `GetSeedsByOwnerAsync`, for each active seed query `GetCapabilityManifestAsync`, collect all capabilities. Build cache model. Save to Redis with TTL.

**Owner type mapping** (following License pattern):
- `MapToContainerOwnerType(string ownerType)` -> ContainerOwnerType for Inventory operations
- Character owners: validate via `ICharacterClient` (soft)
- Other owners: no validation (caller-responsibility)

**State key patterns**:
- Template: `tpl:{statusTemplateId}` and `tpl:{gameServiceId}:{code}`
- Instance: `inst:{statusInstanceId}`
- Container: `ctr:{containerId}` and `ctr:{entityId}:{entityType}:{gameServiceId}`
- Active cache: `active:{entityId}:{entityType}`
- Seed effects cache: `seed:{entityId}:{entityType}`
- Locks: `status:lock:entity:{entityType}:{entityId}`, `status:lock:tpl:{statusTemplateId}`

### Step 8: Build and Verify

```bash
dotnet build
```

Verify no compilation errors, all generated code resolves, no CS1591 warnings.

### Step 9: Unit Tests

The test project and template `StatusServiceTests.cs` were auto-created in Step 2. Fill in with comprehensive tests:

#### 9a. `plugins/lib-status.tests/StatusServiceTests.cs` (generated template -> fill in)

Following testing patterns from TESTING-PATTERNS.md:

**Constructor validation**:
- `StatusService_ConstructorIsValid()` via `ServiceConstructorValidator`

**Template CRUD tests** (capture pattern for state saves and event publishing):
- `CreateStatusTemplate_ValidRequest_SavesTemplateAndPublishesEvent`
- `CreateStatusTemplate_DuplicateCode_ReturnsConflict`
- `CreateStatusTemplate_InvalidGameServiceId_ReturnsNotFound`
- `CreateStatusTemplate_ExceedsMaxPerGameService_ReturnsConflict`
- `GetStatusTemplate_Exists_ReturnsTemplate`
- `GetStatusTemplate_NotFound_ReturnsNotFound`
- `GetStatusTemplateByCode_Exists_ReturnsTemplate`
- `ListStatusTemplates_WithCategoryFilter_ReturnsFiltered`
- `UpdateStatusTemplate_PartialUpdate_OnlyUpdatesProvidedFields`
- `SeedStatusTemplates_BulkCreate_SkipsDuplicates`

**Grant tests** (core operation):
- `GrantStatus_ValidRequest_CreatesItemAndInstanceAndPublishesEvent`
- `GrantStatus_WithContract_CreatesSagaOrderedItemThenContract`
- `GrantStatus_ContractFails_CompensatesItemCreation`
- `GrantStatus_AutoCreatesContainer_WhenNoneExists`
- `GrantStatus_ExceedsMaxStatusesPerEntity_ReturnsConflict`
- `GrantStatus_TemplateNotFound_ReturnsNotFound`

**Stacking tests**:
- `GrantStatus_IgnoreBehavior_ExistingStatus_ReturnsExisting`
- `GrantStatus_ReplaceBehavior_RemovesOldCreatesNew`
- `GrantStatus_RefreshDurationBehavior_IncrementsStackResetsTimer`
- `GrantStatus_IncreaseIntensityBehavior_IncrementsStackUpdatesContract`
- `GrantStatus_IndependentBehavior_CreatesNewInstance`
- `GrantStatus_IndependentBehavior_ExceedsMaxStacks_ReturnsConflict`

**Remove tests**:
- `RemoveStatus_Exists_DestroysItemDeletesRecordPublishesEvent`
- `RemoveStatus_WithContract_CancelsContractFirst`
- `RemoveStatus_NotFound_ReturnsNotFound`
- `RemoveBySource_RemovesAllFromSource`
- `RemoveByCategory_RemovesAllInCategory_PublishesCleansedEvent`

**Query tests**:
- `HasStatus_Exists_ReturnsTrue`
- `HasStatus_NotExists_ReturnsFalse`
- `ListStatuses_ReturnsActiveStatuses`
- `ListStatuses_WithCategoryFilter_FiltersCorrectly`
- `ListStatuses_IncludePassive_IncludesSeedDerivedEffects`
- `GetEffects_ReturnsUnifiedView`
- `GetSeedEffects_ReturnsSeedDerivedOnly`

**Cache tests**:
- `ActiveCache_Miss_RebuildsFromInstances`
- `SeedEffectsCache_Miss_RebuildsFromSeedClient`
- `SeedEffectsCache_InvalidatedOnCapabilityUpdate`

**Expiration worker tests**:
- `HandleItemExpired_StatusItem_RemovesInstanceAndPublishesExpiredEvent`
- `HandleItemExpired_NonStatusItem_IgnoresGracefully`

**Cleanup tests**:
- `CleanupByOwner_RemovesAllContainersAndInstances`

All tests use the capture pattern (Callback on mock setups) to verify saved state and published events, not just Verify calls.

---

## Files Created/Modified Summary

| File | Action |
|------|--------|
| `schemas/status-api.yaml` | Create (~17 endpoints across 4 groups) |
| `schemas/status-events.yaml` | Create (lifecycle + 1 subscription + 6 custom events) |
| `schemas/status-configuration.yaml` | Create (12 configuration properties) |
| `schemas/state-stores.yaml` | Modify (add 6 status stores) |
| `plugins/lib-status/StatusService.cs` | Fill in (auto-generated template) |
| `plugins/lib-status/StatusServiceModels.cs` | Fill in (auto-generated template) |
| `plugins/lib-status/StatusServicePlugin.cs` | Fill in (auto-generated template) |
| `plugins/lib-status/StatusServiceEvents.cs` | Create (NOT auto-generated -- partial class with 1 event handler) |
| ~~`StatusExpirationWorker.cs`~~ | Not needed -- item expiration delegated to lib-item decay (#407) |
| `plugins/lib-status/Services/StatusSeedEvolutionListener.cs` | Create (ISeedEvolutionListener for capability change cache invalidation) |
| `plugins/lib-status.tests/StatusServiceTests.cs` | Fill in (auto-generated template) |
| `plugins/lib-status/lib-status.csproj` | Auto-generated by `generate-service.sh` |
| `plugins/lib-status/AssemblyInfo.cs` | Auto-generated by `generate-service.sh` |
| `plugins/lib-status/Generated/*` | Auto-generated (do not edit) |
| `bannou-service/Generated/*` | Auto-generated (updated) |
| `bannou-service.sln` | Auto-updated by `generate-service.sh` |
| `plugins/lib-status.tests/*` | Auto-generated test project |

---

## Dependency Summary

### Hard Dependencies (constructor injection -- crash if missing)

| Dependency | Layer | Usage |
|------------|-------|-------|
| `IStateStoreFactory` | L0 | All state stores (MySQL templates/instances/containers, Redis caches/locks) |
| `IDistributedLockProvider` | L0 | Concurrent modification safety for entity-level status mutations |
| `IMessageBus` | L0 | Event publishing for all 6+ custom events |
| `IEventConsumer` | L0 | Event subscription registration for seed capability updates |
| `IInventoryClient` | L2 | Status container creation and management |
| `IItemClient` | L2 | Status item instance creation and destruction |
| `IGameServiceClient` | L2 | Validate game service existence for template/container scoping |
| `IResourceClient` | L1 | Cleanup callback registration for character/account deletion |

### Soft Dependencies (runtime resolution -- graceful degradation)

| Dependency | Layer | Usage | Behavior When Missing |
|------------|-------|-------|-----------------------|
| `IContractClient` | L1 | Contract lifecycle for statuses with contractTemplateId | Contract-backed statuses unavailable; TTL-based statuses still work |
| `ISeedClient` | L2 | Seed capability queries for unified effects layer | Seed-derived passive effects unavailable; item-based statuses still work |
| `ICharacterClient` | L2 | Character entity validation for character-type owners | Character validation skipped; caller-responsibility |

---

## Integration Points

### Status -> Inventory (L2, hard dependency)

| Interaction | API Call |
|-------------|----------|
| Create status container | `IInventoryClient.CreateContainerAsync` (type: "status_{gameServiceId}", unlimited constraint) |
| Delete container on cleanup | `IInventoryClient.DeleteContainerAsync` (cascades to items) |
| Query container contents for cache rebuild | `IInventoryClient.GetContainerAsync` (includeContents: true) |

### Status -> Item (L2, hard dependency)

| Interaction | API Call |
|-------------|----------|
| Create status item on grant | `IItemClient.CreateItemInstanceAsync` |
| Destroy item on remove/expire | `IItemClient.DestroyItemInstanceAsync` |
| Validate item template exists | `IItemClient.GetItemTemplateAsync` |

### Status -> Contract (L1, soft dependency)

| Interaction | API Call |
|-------------|----------|
| Create contract for status lifecycle | `IContractClient.CreateContractInstanceAsync` |
| Set template values | `IContractClient.SetContractTemplateValuesAsync` (entityId, statusCode, duration, stackCount, etc.) |
| Auto-propose and consent | `IContractClient.ProposeContractInstanceAsync` + `ConsentToContractAsync` |
| Complete "apply" milestone | `IContractClient.CompleteMilestoneAsync` (triggers prebound APIs for effect application) |
| Cancel contract on remove | `IContractClient.CancelContractInstanceAsync` (triggers cleanup prebound APIs) |

### Status -> Seed (L2, soft dependency)

| Interaction | API Call |
|-------------|----------|
| Query owner's active seeds | `ISeedClient.GetSeedsByOwnerAsync` |
| Query seed capability manifest | `ISeedClient.GetCapabilityManifestAsync` |
| Query seed details (owner lookup) | `ISeedClient.GetSeedAsync` |

### Status -> Resource (L1, hard dependency)

| Interaction | API Call |
|-------------|----------|
| Register cleanup callbacks on startup | `IResourceClient.RegisterCleanupCallbackAsync` (for character, account owner types) |

### Seed -> Status (consumed events)

| Event | Action |
|-------|--------|
| `seed.capability.updated` | Invalidate seed effects cache for affected entity |

### DI Listener: ISeedEvolutionListener

| Notification | Action |
|-------------|--------|
| `OnCapabilitiesChangedAsync` | Invalidate seed effects cache in Redis |
| `OnPhaseChangedAsync` | Invalidate seed effects cache in Redis (phase may gate effects) |
| `OnGrowthRecordedAsync` | No-op (growth alone doesn't change effects) |

### Consumers of Status (outbound -- Status publishes, others subscribe)

| Consumer | Event | Usage |
|----------|-------|-------|
| lib-divine (L4) | `status.expired` | Detect when Minor/Standard blessings expire |
| lib-divine (L4) | `status.removed` | Detect when blessings are cleansed/cancelled |
| Combat systems (L4) | `status.granted`, `status.removed`, `status.expired` | Track active buffs/debuffs |
| lib-analytics (L4) | All status events | Aggregate status application statistics |

---

## Future Extensions (Not in MVP)

These are explicitly deferred from the initial implementation:

1. **Tick-Based Effects (Phase 3)** -- Repeating actions during a status duration (DOT damage every 3s, HOT healing every 5s). Requires either contract milestone ticking support or a dedicated tick worker in Status. The `onTick` pattern from #282 defines the interface; Contract prebound APIs handle the execution.

2. **Conditional Milestone Completion (Phase 3)** -- Death penalty resurrection conditions: "wait 30s OR use resurrection scroll OR reach shrine." Requires Contract's conditional milestone resolution (`anyOf` condition types). Status grants the death-penalty item; Contract resolves when conditions are met; Status removes the item on resolution.

3. **Subscription Renewal (Phase 3)** -- Premium subscription auto-renewal with `checkEndpoint` for billing verification. Requires Contract renewal milestone pattern. On milestone expiry, check billing endpoint; on success, restart milestone; on failure, expire and cascade-remove child statuses.

4. **Source Tracking Cascading (Phase 3)** -- Premium subscription granting child statuses (double-xp, cosmetics access, priority queue). On parent removal, cascade-remove all children via `RemoveBySourceAsync`. The endpoint exists in MVP but the subscription-creates-children workflow is Phase 3.

5. **IStatusEffectProvider DI Interface** -- If L2 services ever need status data (e.g., Character needs "is dead?" checks), add `IStatusEffectProvider` in `bannou-service/Providers/` with DI inversion. Status implements it; Character discovers via `IEnumerable<IStatusEffectProvider>`. Not needed until L2 explicitly requires status queries.

6. **Client Events** -- `status-client-events.yaml` for pushing status change notifications to connected clients (buff applied, buff expired, death state entered). Create as follow-up once core service is working.

7. **Variable Provider Factory** -- `IStatusVariableProviderFactory` for ABML behavior expressions (`${status.has_buff}`, `${status.is_dead}`, `${status.poison_stacks}`). Enables Actor behavior documents to reference status state.

8. **Effect Magnitude Computation** -- Status templates define base magnitudes; stacking computes actual magnitude from base * stackCount * fidelity. This requires typed effect definitions (stat modifiers, resistance values, etc.) beyond the scope of MVP.

---

## Implementation-Time Design Questions

These are smaller questions that should be resolved during implementation, not blocking the plan:

1. **Container creation strategy**: Auto-create on first grant (implemented above) or require explicit creation? Recommendation: auto-create (matches Collection pattern, reduces API surface for consumers).

2. **Cache invalidation scope**: Invalidate entire entity cache on any status change, or surgically update the specific entry? Recommendation: full invalidation (simpler, safer, cache TTL is short at 60s).

3. **Contract-managed expiry detection**: How does Status know when a contract milestone expires? For non-contract statuses, lib-item's decay (#407) handles it via `item.expired`. For contract-managed statuses, options: (a) Status subscribes to contract events, (b) background worker queries contract state, (c) contract prebound API calls Status /remove on expiry. Recommendation: (c) -- contract template includes prebound API that calls Status /remove on milestone expiry. Status doesn't need to watch contracts.

4. **Seed effects interest filter**: Should `StatusSeedEvolutionListener.InterestedSeedTypes` be empty (all types) or configurable? Recommendation: configurable via a config property like `SeedEffectsSeedTypes` (comma-separated codes). Default empty = all types.

5. **Stacking across game services**: Can status templates from different game services stack with each other? Recommendation: no -- statuses are game-service-scoped. The container is per-entity-per-gameService.

---

## Verification

1. `dotnet build` -- compiles without errors or warnings
2. `dotnet test plugins/lib-status.tests/` -- all unit tests pass
3. Verify no CS1591 warnings (all schema properties have descriptions)
4. Verify `StateStoreDefinitions.cs` contains all 6 Status constants after generation
5. Verify `StatusClient.cs` generated in `bannou-service/Generated/Clients/` for other services to call Status
6. Verify event subscription handlers generated in `StatusEventsController.cs` for consumed events (`item.expired`, `seed.capability.updated`)
7. Verify the `IStatusService` interface has methods for all 17 endpoints
8. Manual verification: confirm `IInventoryClient`, `IItemClient`, `IGameServiceClient` are available via constructor injection (L2 loads before L4)
9. Verify grant flow end-to-end in unit tests: create item -> optionally create contract -> save instance record -> update cache -> publish event
10. Verify saga compensation: contract failure after item creation -> item destroyed, grant returns failure
