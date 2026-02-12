# Example Plan: Implement Seed Service (L2 Game Foundation)

> **Why this exists**: This is a real implementation plan preserved as a reference example. It demonstrates the expected structure, level of detail, and patterns for planning a new Bannou service from scratch. The Seed service was chosen because it exercises nearly every common pattern: schema-first development, state stores, events, configuration, bonds/relationships, distributed locks, caching, and unit tests.

## Context

The Seed service is a new L2 Game Foundation plugin that provides a generic progressive growth primitive. Seeds are entities that start empty and grow by accumulating metadata from external events, progressively gaining capabilities. They support polymorphic ownership (accounts, actors, realms, characters, relationships), configurable seed types with growth phases and capability rules, and seed-to-seed bonds.

This is the foundational growth primitive described in the [Seed System Guide](../guides/SEED-SYSTEM.md). The Gardener service (L4) is the first consumer -- see the [Gardener Deep Dive](../plugins/GARDENER.md) for implementation details. Three open design questions were deferred to GitHub issues with recommended defaults chosen (decay off, no cross-seed sharing, no cross-type transfer).

**Open question resolutions** *(plans should document any design decisions made during planning)*:
- Seed types support both API registration at runtime AND configuration-based seeding on startup.
- Growth phases are soft filters with configurable strictness. This is mostly a Gardener concern but Seed's API returns phase data for consumers to interpret.

---

## Implementation Steps

### Step 1: Create Schema Files

#### 1a. `schemas/seed-api.yaml`
- Write from the endpoint specifications below
- **Fix x-permissions format**: Translate planning doc shorthand to actual format:
  - `[authenticated]` → `- role: user` + `states: {}`
  - `[developer]` → `- role: developer` + `states: {}`
  - `[internal]` → `- role: developer` + `states: {}` (service-to-service calls bypass permissions; developer allows testing)
- Header: `x-service-layer: GameFoundation`, `servers: [{ url: http://localhost:5012 }]`
- 22 POST endpoints across seed CRUD, growth, capabilities, types, and bonds
- All component schemas from the draft (enums, requests, responses)
- Ensure all properties have `description` fields (CS1591 compliance)
- Ensure NRT compliance: optional ref types have `nullable: true`, required fields in `required` arrays

#### 1b. `schemas/seed-events.yaml`
- **x-lifecycle** for `Seed` entity (generates created/updated/deleted events):
  - Model fields: seedId (primary), ownerId, ownerType, seedTypeCode, gameServiceId, growthPhase, totalGrowth, displayName, status
  - Sensitive: metadata (exclude from lifecycle events - it's opaque type-specific data)
- **x-event-subscriptions**: Subscribe to `seed.growth.contributed` (external services publish growth gains)
- **x-event-publications**: Lifecycle events + custom events:
  - `seed.phase.changed` - growth phase transition
  - `seed.growth.updated` - domain values changed
  - `seed.capability.updated` - manifest recomputed
  - `seed.activated` - seed set as active
  - `seed.archived` - seed archived
  - `seed.bond.formed` - bond formed between seeds
- Custom event schemas for non-lifecycle events
- `SeedGrowthContributedEvent` schema (consumed event): seedId, domain, amount, source, sourceEventId, context

#### 1c. `schemas/seed-configuration.yaml`
From the planning doc config section:
- `CapabilityRecomputeDebounceMs` (int, default: 5000)
- `GrowthDecayEnabled` (bool, default: false)
- `GrowthDecayRatePerDay` (float, default: 0.01)
- `BondSharedGrowthMultiplier` (float, default: 1.5)
- `MaxSeedTypesPerGameService` (int, default: 50)
- `DefaultMaxSeedsPerOwner` (int, default: 3)
- All with `env: SEED_{PROPERTY}` format, single-line descriptions

#### 1d. Update `schemas/state-stores.yaml`
Add under `x-state-stores:`:
```yaml
seed-statestore:
  backend: mysql
  service: Seed
  purpose: Seed entity records (durable, queryable by owner/type)

seed-growth-statestore:
  backend: mysql
  service: Seed
  purpose: Growth domain records per seed (durable, queryable)

seed-capabilities-cache:
  backend: redis
  prefix: "seed:cap"
  service: Seed
  purpose: Computed capability manifests (cached, frequently read)

seed-bonds-statestore:
  backend: mysql
  service: Seed
  purpose: Bond records between seeds (durable)

seed-type-definitions-statestore:
  backend: mysql
  service: Seed
  purpose: Registered seed type definitions (durable, admin-managed)

seed-lock:
  backend: redis
  prefix: "seed:lock"
  service: Seed
  purpose: Distributed locks for seed modifications
```

### Step 2: Generate Service (creates project, code, and templates)

```bash
cd scripts && ./generate-service.sh seed
```

This single command bootstraps the entire plugin. It auto-creates:

**Plugin project infrastructure** (via `generate-project.sh`):
- `plugins/lib-seed/` directory
- `plugins/lib-seed/lib-seed.csproj` (with ServiceLib.targets import)
- `plugins/lib-seed/AssemblyInfo.cs` (ApiController, InternalsVisibleTo)
- Adds `lib-seed` to `bannou-service.sln` via `dotnet sln add`

**Generated code** (in `plugins/lib-seed/Generated/`):
- `ISeedService.cs` - interface
- `SeedController.cs` - HTTP routing
- `SeedController.Meta.cs` - runtime schema introspection
- `SeedServiceConfiguration.cs` - typed config class
- `SeedPermissionRegistration.cs` - permissions
- `SeedEventsController.cs` - event subscription handlers (from x-event-subscriptions)

**Generated code** (in `bannou-service/Generated/`):
- `Models/SeedModels.cs` - request/response models
- `Clients/SeedClient.cs` - client for other services to call Seed
- `Events/SeedEventsModels.cs` - event models
- Updated `StateStoreDefinitions.cs` with Seed store constants

**Template files** (created once if missing, never overwritten):
- `plugins/lib-seed/SeedService.cs` - business logic template with TODO stubs
- `plugins/lib-seed/SeedServiceModels.cs` - internal models template
- `plugins/lib-seed/SeedServicePlugin.cs` - plugin registration template

**Test project** (via `generate-tests.sh`):
- `plugins/lib-seed.tests/` directory, `.csproj`, `AssemblyInfo.cs`, `GlobalUsings.cs`
- `SeedServiceTests.cs` template with basic tests
- Adds `lib-seed.tests` to `bannou-service.sln` via `dotnet sln add`

**Build check**: `dotnet build` to verify generation succeeded.

### Step 3: Fill In Plugin Registration

#### 3a. `plugins/lib-seed/SeedServicePlugin.cs` (generated template → fill in)
The generator creates the skeleton. Fill in following the ItemServicePlugin pattern:
- Extends `BaseBannouPlugin`
- `PluginName => "seed"`, `DisplayName => "Seed Service"`
- Standard lifecycle: ConfigureServices, ConfigureApplication, OnStartAsync (creates scope), OnRunningAsync, OnShutdownAsync
- OnRunningAsync: register seed types from configuration if needed

### Step 4: Fill In Internal Models

#### 4a. `plugins/lib-seed/SeedServiceModels.cs` (generated template → fill in)
Internal storage models (not API-facing):

- **`SeedModel`**: seedId (Guid), ownerId (Guid), ownerType (string), seedTypeCode (string), gameServiceId (Guid), createdAt (DateTimeOffset), growthPhase (string), totalGrowth (float), bondId (Guid?), displayName (string), status (SeedStatus), metadata (Dictionary<string, object>?)
- **`SeedGrowthModel`**: seedId (Guid), domains (Dictionary<string, float>) - flat map of domain path to depth
- **`SeedTypeDefinitionModel`**: All fields from the planning doc SeedTypeDefinition, using proper C# types. GrowthPhases as List<GrowthPhaseEntry>, CapabilityRules as List<CapabilityRuleEntry>
- **`SeedBondModel`**: bondId (Guid), seedTypeCode (string), participants (List<BondParticipantEntry>), createdAt (DateTimeOffset), status (BondStatus), bondStrength (float), sharedGrowth (float), permanent (bool)
- **`CapabilityManifestModel`**: seedId (Guid), seedTypeCode (string), computedAt (DateTimeOffset), version (int), capabilities (List<CapabilityEntry>)
- Helper records: `GrowthPhaseEntry`, `CapabilityRuleEntry`, `BondParticipantEntry`, `CapabilityEntry`

All models use proper types per T25 (enums, Guids, DateTimeOffset). Nullable for optional fields per T26.

### Step 5: Create Event Handlers

#### 5a. `plugins/lib-seed/SeedServiceEvents.cs` (manual - not auto-generated)
Partial class of SeedService:
- `RegisterEventConsumers(IEventConsumer eventConsumer)` - registers handler for `seed.growth.contributed`
- `HandleGrowthContributedAsync(SeedGrowthContributedEvent evt)`:
  1. Validate seed exists and is active
  2. Load current growth data from `seed-growth-statestore`
  3. Add amount to specified domain (create domain if new)
  4. Recalculate total growth
  5. Check phase transitions against seed type thresholds
  6. Save updated growth
  7. Publish `seed.growth.updated` event
  8. If phase changed, publish `seed.phase.changed` event
  9. Debounce capability recomputation (config: CapabilityRecomputeDebounceMs)

### Step 6: Implement Service Business Logic

#### 6a. `plugins/lib-seed/SeedService.cs` (generated template → fill in)
Partial class with `[BannouService("seed", typeof(ISeedService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFoundation)]`:

**Constructor dependencies**:
- `IStateStoreFactory` - for all state stores
- `IMessageBus` - event publishing
- `IDistributedLockProvider` - concurrent modification safety
- `ILogger<SeedService>` - structured logging
- `SeedServiceConfiguration` - typed config
- `IEventConsumer` - event handler registration
- `IGameServiceClient` - validate gameServiceId exists (L2 hard dependency)

**Store initialization** (in constructor):
- `_seedStore` = GetStore<SeedModel>(StateStoreDefinitions.SeedStatestore)
- `_growthStore` = GetStore<SeedGrowthModel>(StateStoreDefinitions.SeedGrowthStatestore)
- `_capabilitiesCache` = GetStore<CapabilityManifestModel>(StateStoreDefinitions.SeedCapabilitiesCache)
- `_bondStore` = GetStore<SeedBondModel>(StateStoreDefinitions.SeedBondsStatestore)
- `_typeStore` = GetStore<SeedTypeDefinitionModel>(StateStoreDefinitions.SeedTypeDefinitionsStatestore)
- `_lockProvider` for distributed locks

**Key method implementations** (all follow T7 error handling, T8 return pattern):

| Method | Key Logic |
|--------|-----------|
| `CreateSeedAsync` | Validate type exists, check MaxPerOwner limit, validate ownerType in allowedOwnerTypes, create seed with initial phase, publish created event |
| `GetSeedAsync` | Lookup by ID, return 404 if not found |
| `GetSeedsByOwnerAsync` | JSON query by ownerId + ownerType, optional seedTypeCode filter |
| `ListSeedsAsync` | Paged JSON query with multiple optional filters |
| `UpdateSeedAsync` | Lock, load, validate, update mutable fields, publish updated event |
| `ActivateSeedAsync` | Lock by owner+type, deactivate current active, activate target, publish event |
| `ArchiveSeedAsync` | Validate not active, set status to Archived, publish event |
| `GetGrowthAsync` | Load growth model by seedId |
| `RecordGrowthAsync` | Lock, load growth, add domain amount, update totals, check phase transition, publish events, schedule capability recompute |
| `RecordGrowthBatchAsync` | Same as RecordGrowth but for multiple domains atomically |
| `GetGrowthPhaseAsync` | Load seed, load type definition, compute phase from total growth vs thresholds |
| `GetCapabilityManifestAsync` | Read from Redis cache; if miss, compute from growth + rules, cache result |
| `RegisterSeedTypeAsync` | Validate uniqueness (code + gameServiceId), check MaxSeedTypesPerGameService, save |
| `GetSeedTypeAsync` | Lookup by code + gameServiceId |
| `ListSeedTypesAsync` | Query by gameServiceId |
| `UpdateSeedTypeAsync` | Lock, update, optionally trigger recomputation for all seeds of this type |
| `InitiateBondAsync` | Validate both seeds same type, check cardinality, check existing bonds, create PendingConfirmation bond |
| `ConfirmBondAsync` | Validate participant, mark confirmed, if all confirmed → set Active, update seeds' bondId, publish event |
| `GetBondAsync` | Lookup by bondId |
| `GetBondForSeedAsync` | Load seed, return bond if bondId set |
| `GetBondPartnersAsync` | Load bond, return partner seed summaries |

**Capability computation** (private helper):
1. Load seed's growth domains
2. Load seed type's capability rules
3. For each rule: check if domain depth >= unlockThreshold
4. If unlocked, compute fidelity using fidelityFormula ("linear", "logarithmic", "step")
5. Build manifest with version increment
6. Cache in Redis with configurable TTL
7. Publish `seed.capability.updated` event

**State key patterns**:
- Seed: `seed:{seedId}`
- Growth: `growth:{seedId}`
- Type definition: `type:{gameServiceId}:{seedTypeCode}`
- Bond: `bond:{bondId}`
- Capabilities cache: `cap:{seedId}`
- Lock: `seed:lock:{seedId}` or `seed:lock:owner:{ownerId}:{seedTypeCode}`

### Step 7: Build and Verify

```bash
dotnet build
```

Verify no compilation errors, all generated code resolves, no CS1591 warnings.

### Step 8: Unit Tests

The test project and template `SeedServiceTests.cs` were auto-created in Step 2. Fill in with comprehensive tests:

#### 8a. `plugins/lib-seed.tests/SeedServiceTests.cs` (generated template → fill in)
Following testing patterns from TESTING-PATTERNS.md:

**Constructor validation**:
- `SeedService_ConstructorIsValid()` via `ServiceConstructorValidator`

**Seed CRUD tests** (capture pattern for state saves and event publishing):
- `CreateSeed_ValidRequest_SavesSeedAndPublishesEvent`
- `CreateSeed_ExceedsMaxPerOwner_ReturnsConflict`
- `CreateSeed_InvalidSeedType_ReturnsNotFound`
- `CreateSeed_InvalidOwnerType_ReturnsBadRequest`
- `GetSeed_Exists_ReturnsSeed`
- `GetSeed_NotFound_ReturnsNotFound`
- `ActivateSeed_DeactivatesPreviousAndActivatesTarget`
- `ArchiveSeed_ActiveSeed_ReturnsBadRequest`

**Growth tests**:
- `RecordGrowth_NewDomain_CreatesDomainEntry`
- `RecordGrowth_ExistingDomain_IncrementsDepth`
- `RecordGrowth_CrossesPhaseThreshold_PublishesPhaseChangedEvent`
- `RecordGrowthBatch_MultipleDomainsAtomically`

**Capability tests**:
- `GetCapabilityManifest_ComputesFromGrowthAndRules`
- `GetCapabilityManifest_CacheHit_ReturnsCachedVersion`
- `ComputeCapabilities_LinearFidelity_CorrectCalculation`
- `ComputeCapabilities_LogarithmicFidelity_CorrectCalculation`

**Type definition tests**:
- `RegisterSeedType_ValidRequest_SavesAndReturns`
- `RegisterSeedType_DuplicateCode_ReturnsConflict`
- `RegisterSeedType_ExceedsMaxTypesPerGameService_ReturnsConflict`

**Bond tests**:
- `InitiateBond_ValidSeeds_CreatesPendingBond`
- `InitiateBond_DifferentTypes_ReturnsBadRequest`
- `InitiateBond_AlreadyBonded_ReturnsConflict`
- `ConfirmBond_AllConfirmed_ActivatesBondAndUpdateSeeds`

**Event handler tests**:
- `HandleGrowthContributed_ValidEvent_RecordsGrowth`
- `HandleGrowthContributed_SeedNotFound_LogsWarning`

All tests use the capture pattern (Callback on mock setups) to verify saved state and published events, not just Verify calls.

---

## Files Created/Modified Summary

| File | Action |
|------|--------|
| `schemas/seed-api.yaml` | Create (from planning doc draft, fix x-permissions) |
| `schemas/seed-events.yaml` | Create (lifecycle + subscriptions + custom events) |
| `schemas/seed-configuration.yaml` | Create |
| `schemas/state-stores.yaml` | Modify (add 6 seed stores) |
| `plugins/lib-seed/SeedService.cs` | Fill in (auto-generated template) |
| `plugins/lib-seed/SeedServiceModels.cs` | Fill in (auto-generated template) |
| `plugins/lib-seed/SeedServicePlugin.cs` | Fill in (auto-generated template) |
| `plugins/lib-seed/SeedServiceEvents.cs` | Create (NOT auto-generated) |
| `plugins/lib-seed.tests/SeedServiceTests.cs` | Fill in (auto-generated template) |
| `plugins/lib-seed/lib-seed.csproj` | Auto-generated by `generate-service.sh` |
| `plugins/lib-seed/AssemblyInfo.cs` | Auto-generated by `generate-service.sh` |
| `plugins/lib-seed/Generated/*` | Auto-generated (do not edit) |
| `bannou-service/Generated/*` | Auto-generated (updated) |
| `bannou-service.sln` | Auto-updated by `generate-service.sh` |
| `plugins/lib-seed.tests/*` | Auto-generated test project |

## Verification

1. `dotnet build` - compiles without errors or warnings
2. `dotnet test plugins/lib-seed.tests/` - all unit tests pass
3. Verify no CS1591 warnings (all schema properties have descriptions)
4. Verify StateStoreDefinitions.cs contains Seed constants after generation
5. Verify SeedClient.cs generated in bannou-service for other services to use
