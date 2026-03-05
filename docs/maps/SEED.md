# Seed Implementation Map

> **Plugin**: lib-seed
> **Schema**: schemas/seed-api.yaml
> **Layer**: GameFoundation
> **Deep Dive**: [docs/plugins/SEED.md](../plugins/SEED.md)

---

## Summary

| Field | Value |
|-------|-------|
| Plugin | lib-seed |
| Layer | L2 GameFoundation |
| Endpoints | 24 |
| State Stores | seed (MySQL), seed-growth (MySQL), seed-type-definitions (MySQL), seed-bonds (MySQL), seed-capabilities-cache (Redis), seed-lock (Redis) |
| Events Published | 11 (seed.created, seed.updated, seed.activated, seed.archived, seed.growth.updated, seed.phase.changed, seed.capability.updated, seed.bond.formed, seed.type.created, seed.type.updated, seed.type.deleted) |
| Events Consumed | 0 |
| Client Events | 0 |
| Background Services | 1 (SeedDecayWorkerService) |

> **Note**: `seed.deleted` is declared via `x-lifecycle` in the events schema but is never published. Seeds are archived (soft-deleted), not hard-deleted.

---

## State

**Store**: `seed` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `seed:{seedId}` | `SeedModel` | Core seed entity with owner, type, phase, status, bond reference |

**Store**: `seed-growth` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `growth:{seedId}` | `SeedGrowthModel` | Per-domain growth entries (Depth, LastActivityAt, PeakDepth) plus LastDecayedAt |

**Store**: `seed-type-definitions` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `type:{gameServiceId}:{seedTypeCode}` | `SeedTypeDefinitionModel` | Seed type definitions with phases, capability rules, bond config, allowed owner types |
| `type:cross-game:{seedTypeCode}` | `SeedTypeDefinitionModel` | Cross-game type definitions when gameServiceId is null |

**Store**: `seed-bonds` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `bond:{bondId}` | `SeedBondModel` | Bond entities with participant list, strength, shared growth, status |

**Store**: `seed-capabilities-cache` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `cap:{seedId}` | `CapabilityManifestModel` | Computed capability manifest with fidelity scores; invalidated on growth, recomputed on next read |

**Store**: `seed-lock` (Backend: Redis)

| Key Pattern | Purpose |
|-------------|---------|
| `{seedId}` | Growth recording, seed updates, bond initiation (ordered dual-lock) |
| `bond:{bondId}` | Bond confirmation serialization |
| `owner:{ownerId}:{seedTypeCode}` | Activation exclusivity (one active seed per type per owner) |
| `type:{gameServiceId}:{seedTypeCode}` | Type definition update serialization |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | Acquires all six store references in constructor |
| lib-state (IDistributedLockProvider) | L0 | Hard | Distributed locks for mutations on seeds, bonds, types, activation |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing all 11 event topics |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Span instrumentation on internal async helpers |
| lib-game-service (IGameServiceClient) | L2 | Hard | Validates game service existence during seed creation and type registration |
| IEnumerable\<ISeedEvolutionListener\> | L4 | DI Collection | Dispatches growth, phase change, and capability notifications to registered L4 listeners |

**DI Provider/Listener Interfaces**:
- **ISeedEvolutionListener** — Seed **consumes** registered listeners (L2 → L4 push). Dispatched via `SeedEvolutionDispatcher` after growth recording, phase transitions, and capability recomputation.
- **ICollectionUnlockListener** — Seed **implements** via `SeedCollectionUnlockListener` (L2 ← L2 push from Collection). Translates collection entry unlock notifications into growth via `ISeedClient`.
- **IVariableProviderFactory** — Seed **implements** via `SeedProviderFactory` (L2 → L2 pull from Actor). Provides `${seed.*}` ABML variables via `ISeedDataCache`.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `seed.created` | `SeedCreatedEvent` | CreateSeed |
| `seed.updated` | `SeedUpdatedEvent` | UpdateSeed, ActivateSeed (for deactivated siblings) |
| `seed.activated` | `SeedActivatedEvent` | ActivateSeed |
| `seed.archived` | `SeedArchivedEvent` | ArchiveSeed |
| `seed.growth.updated` | `SeedGrowthUpdatedEvent` | RecordGrowthInternal (per domain), ApplyCrossPollination |
| `seed.phase.changed` | `SeedPhaseChangedEvent` | RecordGrowthInternal, ApplyCrossPollination, RecomputeSeedsForType, SeedDecayWorkerService |
| `seed.capability.updated` | `SeedCapabilityUpdatedEvent` | ComputeAndCacheManifest (on cache miss/stale) |
| `seed.bond.formed` | `SeedBondFormedEvent` | ConfirmBond (when all participants confirmed) |
| `seed.type.created` | `SeedTypeCreatedEvent` | RegisterSeedType |
| `seed.type.updated` | `SeedTypeUpdatedEvent` | UpdateSeedType, DeprecateSeedType, UndeprecateSeedType |
| `seed.type.deleted` | `SeedTypeDeletedEvent` | DeleteSeedType |

---

## Events Consumed

This plugin does not consume external events. The Collection-to-Seed growth pipeline uses the `ICollectionUnlockListener` DI provider pattern for guaranteed in-process delivery instead of event bus subscriptions.

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<SeedService>` | Structured logging |
| `SeedServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (constructor-only; not stored as field) |
| `IDistributedLockProvider` | Distributed locks for mutation operations |
| `IMessageBus` | Event publishing |
| `ITelemetryProvider` | Span instrumentation |
| `IGameServiceClient` | Game service existence validation |
| `IEventConsumer` | Event consumer registration (no handlers registered) |
| `IEnumerable<ISeedEvolutionListener>` | L4 listener dispatch for growth/phase/capability notifications |
| `SeedEvolutionDispatcher` | Static helper dispatching ISeedEvolutionListener callbacks with isolation |
| `SeedDecayWorkerService` | Background worker for exponential growth decay |
| `SeedCollectionUnlockListener` | ICollectionUnlockListener impl; routes collection unlocks to growth via ISeedClient |
| `SeedDataCache` | Singleton ConcurrentDictionary cache of character seed data for variable provider |
| `SeedProviderFactory` | IVariableProviderFactory impl; creates SeedProvider for `${seed.*}` ABML variables |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| CreateSeed | POST /seed/create | user | seed, growth | seed.created |
| GetSeed | POST /seed/get | user | - | - |
| GetSeedsByOwner | POST /seed/get-by-owner | user | - | - |
| ListSeeds | POST /seed/list | developer | - | - |
| UpdateSeed | POST /seed/update | user | seed | seed.updated |
| ActivateSeed | POST /seed/activate | user | seed (multiple) | seed.updated, seed.activated |
| ArchiveSeed | POST /seed/archive | user | seed | seed.archived |
| GetGrowth | POST /seed/growth/get | user | - | - |
| RecordGrowth | POST /seed/growth/record | developer | growth, seed, bond, cap (delete) | seed.growth.updated, seed.phase.changed |
| RecordGrowthBatch | POST /seed/growth/record-batch | developer | growth, seed, bond, cap (delete) | seed.growth.updated, seed.phase.changed |
| GetGrowthPhase | POST /seed/growth/get-phase | user | - | - |
| GetCapabilityManifest | POST /seed/capability/get-manifest | user | cap (write-through) | seed.capability.updated |
| RegisterSeedType | POST /seed/type/register | developer | type | seed.type.created |
| GetSeedType | POST /seed/type/get | user | - | - |
| ListSeedTypes | POST /seed/type/list | user | - | - |
| UpdateSeedType | POST /seed/type/update | developer | type, seed (recompute) | seed.type.updated, seed.phase.changed |
| DeprecateSeedType | POST /seed/type/deprecate | developer | type | seed.type.updated |
| UndeprecateSeedType | POST /seed/type/undeprecate | developer | type | seed.type.updated |
| DeleteSeedType | POST /seed/type/delete | developer | type (delete) | seed.type.deleted |
| InitiateBond | POST /seed/bond/initiate | user | bond | - |
| ConfirmBond | POST /seed/bond/confirm | user | bond, seed (multiple) | seed.bond.formed |
| GetBond | POST /seed/bond/get | user | - | - |
| GetBondForSeed | POST /seed/bond/get-for-seed | user | - | - |
| GetBondPartners | POST /seed/bond/get-partners | user | - | - |

---

## Methods

### CreateSeed
POST /seed/create | Roles: [user]

```
IF request.GameServiceId has value
  CALL IGameServiceClient.GetServiceAsync({ ServiceId })  -> 404 if not found
READ type-store:"type:{gameServiceId}:{seedTypeCode}"     -> 404 if null
IF type.IsDeprecated                                       -> 400
IF request.OwnerType not in type.AllowedOwnerTypes         -> 400
// maxPerOwner: type.MaxPerOwner if > 0, else config.DefaultMaxSeedsPerOwner
COUNT seed-store WHERE OwnerId, OwnerType, SeedTypeCode, GameServiceId, Status != Archived
IF count >= maxPerOwner                                    -> 409
// Phase computed via ComputePhaseInfo; falls back to "initial" if no phases defined
// DisplayName defaults to "{seedTypeCode}-{guid}"[..20] if not provided
// Metadata wrapped: seed.Metadata["data"] = request.Metadata
WRITE seed-store:"seed:{newId}" <- SeedModel from request
WRITE growth-store:"growth:{newId}" <- SeedGrowthModel { empty Domains }
PUBLISH seed.created { seedId, ownerId, ownerType, seedTypeCode, gameServiceId, growthPhase, totalGrowth, displayName, status, bondId }
RETURN (200, SeedResponse)
```

### GetSeed
POST /seed/get | Roles: [user]

```
READ seed-store:"seed:{seedId}"                            -> 404 if null
RETURN (200, SeedResponse)
```

### GetSeedsByOwner
POST /seed/get-by-owner | Roles: [user]

```
// Builds conditions: OwnerId, OwnerType, optionally SeedTypeCode, optionally Status != Archived
QUERY seed-store WHERE conditions PAGED(0, config.DefaultQueryPageSize)
RETURN (200, ListSeedsResponse { seeds, totalCount })
```

### ListSeeds
POST /seed/list | Roles: [developer]

```
// Builds conditions: optional SeedTypeCode, OwnerType, GameServiceId, GrowthPhase, Status
QUERY seed-store WHERE conditions PAGED((page-1)*pageSize, pageSize)
RETURN (200, ListSeedsResponse { seeds, totalCount })
```

### UpdateSeed
POST /seed/update | Roles: [user]

```
LOCK seed-lock:{seedId}                                    -> 409 if fails
READ seed-store:"seed:{seedId}"                            -> 404 if null
// Apply non-null fields: DisplayName, Metadata (wrapped under "data" key)
WRITE seed-store:"seed:{seedId}" <- updated SeedModel
PUBLISH seed.updated { ..., changedFields }
RETURN (200, SeedResponse)
```

### ActivateSeed
POST /seed/activate | Roles: [user]

```
READ seed-store:"seed:{seedId}"                            -> 404 if null
IF seed.Status == Active                                   -> 200 (already active, early return)
IF seed.Status == Archived                                 -> 400 (must be dormant)
LOCK seed-lock:"owner:{ownerId}:{seedTypeCode}"            -> 409 if fails
  QUERY seed-store WHERE OwnerId, OwnerType, SeedTypeCode, GameServiceId, Status = Active PAGED(0, 10)
  FOREACH activeSeed in results (where seedId != target)
    activeSeed.Status = Dormant
    WRITE seed-store:"seed:{activeSeed.SeedId}" <- updated
    PUBLISH seed.updated { ..., status: Dormant, changedFields: ["status"] }
  seed.Status = Active
  WRITE seed-store:"seed:{seedId}" <- updated
  PUBLISH seed.activated { seedId, ownerId, ownerType, seedTypeCode, previousActiveSeedId }
RETURN (200, SeedResponse)
```

### ArchiveSeed
POST /seed/archive | Roles: [user]

```
READ seed-store:"seed:{seedId}"                            -> 404 if null
IF seed.Status == Active                                   -> 400 (must deactivate first)
IF seed.Status == Archived                                 -> 200 (idempotent)
seed.Status = Archived
WRITE seed-store:"seed:{seedId}" <- updated
PUBLISH seed.archived { seedId, ownerId, ownerType, seedTypeCode }
RETURN (200, SeedResponse)
```

### GetGrowth
POST /seed/growth/get | Roles: [user]

```
READ growth-store:"growth:{seedId}"                        -> 404 if null
// TotalGrowth computed from sum of domain depths at response time
// Returns stored values directly; decay applied by background worker
RETURN (200, GrowthResponse { seedId, totalGrowth, domains })
```

### RecordGrowth
POST /seed/growth/record | Roles: [developer]

```
// Delegates to RecordGrowthInternal with single (domain, amount) entry
// See RecordGrowthInternal below
```

### RecordGrowthBatch
POST /seed/growth/record-batch | Roles: [developer]

```
// Delegates to RecordGrowthInternal with entries array
// See RecordGrowthInternal below
```

#### RecordGrowthInternal (private helper)

```
LOCK seed-lock:{seedId}                                    -> 409 if fails
  READ seed-store:"seed:{seedId}"                          -> 404 if null
  IF seed.Status != Active                                 -> 400
  READ growth-store:"growth:{seedId}"
  // Initialize empty growth if null
  IF seed.BondId has value
    READ bond-store:"bond:{bondId}"
    IF bond.Status == Active
      // Apply config.BondSharedGrowthMultiplier to all amounts
  FOREACH (domain, amount) in entries
    // Update domain entry: Depth += amount, LastActivityAt = now, PeakDepth = max
    PUBLISH seed.growth.updated { seedId, seedTypeCode, domain, previousDepth, newDepth, totalGrowth, crossPollinated: false }
  WRITE growth-store:"growth:{seedId}" <- updated
  IF bond active and permanent
    // Reset LastActivityAt on matching domains of bonded partners
    FOREACH partner in bond.Participants (where not self)
      READ growth-store:"growth:{partner.SeedId}"
      WRITE growth-store:"growth:{partner.SeedId}" <- updated LastActivityAt
  IF bond active
    bond.SharedGrowth += totalAdded
    bond.BondStrength += totalAdded * config.BondStrengthGrowthRate
    WRITE bond-store:"bond:{bondId}" <- updated
  seed.TotalGrowth = sum of domain depths
  READ type-store for phase computation
  // ComputePhaseInfo: sorts phases by MinTotalGrowth, finds highest reached
  IF phase changed
    seed.GrowthPhase = newPhase
    PUBLISH seed.phase.changed { ..., direction: Progressed }
    DISPATCH ISeedEvolutionListener.OnPhaseChangedAsync
  WRITE seed-store:"seed:{seedId}" <- updated TotalGrowth, GrowthPhase
  DELETE cap-store:"cap:{seedId}"  // Invalidate capability cache
  DISPATCH ISeedEvolutionListener.OnGrowthRecordedAsync
  // Cross-pollination (when seedType.SameOwnerGrowthMultiplier > 0)
  IF sameOwnerGrowthMultiplier > 0
    QUERY seed-store WHERE OwnerId, OwnerType, SeedTypeCode, GameServiceId, Status != Archived
    FOREACH sibling (where not self)
      // see ApplyCrossPollination: try-lock with 3s timeout, best-effort
      // Uses raw amounts * multiplier (not bond-boosted)
      // Full processing: phase transitions, cap invalidation, event publication
      // CrossPollinated = true on events
RETURN (200, GrowthResponse)
```

### GetGrowthPhase
POST /seed/growth/get-phase | Roles: [user]

```
READ seed-store:"seed:{seedId}"                            -> 404 if null
READ type-store:"type:{gameServiceId}:{seedTypeCode}"      -> 500 if null (data integrity error)
// ComputePhaseInfo: sorts phases, finds current + next phase threshold
RETURN (200, GrowthPhaseResponse { seedId, phaseCode, displayName, totalGrowth, nextPhaseCode?, nextPhaseThreshold? })
```

### GetCapabilityManifest
POST /seed/capability/get-manifest | Roles: [user]

```
READ cap-store:"cap:{seedId}"
IF cached and (now - cached.ComputedAt) < config.CapabilityRecomputeDebounceMs
  RETURN (200, CapabilityManifestResponse from cache)
READ seed-store:"seed:{seedId}"                            -> 404 if null
// Delegates to ComputeAndCacheManifest
```

#### ComputeAndCacheManifest (private helper)

```
READ growth-store:"growth:{seedId}"
READ type-store:"type:{gameServiceId}:{seedTypeCode}"
READ cap-store:"cap:{seedId}"  // For version increment
// Evaluate each CapabilityRule against domain depths
// Fidelity formulas: linear (default), logarithmic, step
// Version = (existing?.Version ?? 0) + 1
WRITE cap-store:"cap:{seedId}" <- CapabilityManifestModel
PUBLISH seed.capability.updated { seedId, seedTypeCode, version, capabilityCount }
DISPATCH ISeedEvolutionListener.OnCapabilitiesChangedAsync
RETURN (200, CapabilityManifestResponse)
```

### RegisterSeedType
POST /seed/type/register | Roles: [developer]

```
IF request.GameServiceId has value
  CALL IGameServiceClient.GetServiceAsync({ ServiceId })   -> 404 if not found
READ type-store:"type:{gameServiceId}:{seedTypeCode}"      -> 409 if already exists
COUNT type-store WHERE GameServiceId PAGED(0, 1)
IF count >= config.MaxSeedTypesPerGameService               -> 409
WRITE type-store:"type:{gameServiceId}:{seedTypeCode}" <- SeedTypeDefinitionModel from request
PUBLISH seed.type.created { seedTypeCode, gameServiceId, displayName, ... }
RETURN (200, SeedTypeResponse)
```

### GetSeedType
POST /seed/type/get | Roles: [user]

```
READ type-store:"type:{gameServiceId}:{seedTypeCode}"      -> 404 if null
RETURN (200, SeedTypeResponse)
```

### ListSeedTypes
POST /seed/type/list | Roles: [user]

```
QUERY type-store WHERE GameServiceId PAGED(0, config.DefaultQueryPageSize)
// Post-query in-memory filter: removes deprecated items if !includeDeprecated
RETURN (200, ListSeedTypesResponse { seedTypes })
```

### UpdateSeedType
POST /seed/type/update | Roles: [developer]

```
LOCK seed-lock:"type:{gameServiceId}:{seedTypeCode}"       -> 409 if fails
  READ type-store:"type:{gameServiceId}:{seedTypeCode}"    -> 404 if null
  // Apply non-null fields (patch semantics): displayName, description, maxPerOwner,
  //   growthPhases, capabilityRules, growthDecayEnabled, growthDecayRatePerDay,
  //   sameOwnerGrowthMultiplier, collectionGrowthMappings
  WRITE type-store:"type:{...}" <- updated
  PUBLISH seed.type.updated { ..., changedFields }
  IF phasesChanged or capabilityRulesChanged
    // RecomputeSeedsForType: paginated loop over all seeds of this type
    QUERY seed-store WHERE SeedTypeCode, GameServiceId, Status != Archived PAGED(offset, config.DefaultQueryPageSize)
    FOREACH seed in results
      // Recompute phase from new definitions
      IF phase changed
        WRITE seed-store:"seed:{seedId}" <- updated GrowthPhase, TotalGrowth
        PUBLISH seed.phase.changed { ..., direction }
        DISPATCH ISeedEvolutionListener.OnPhaseChangedAsync
      DELETE cap-store:"cap:{seedId}"  // Invalidate capability cache
RETURN (200, SeedTypeResponse)
```

### DeprecateSeedType
POST /seed/type/deprecate | Roles: [developer]

```
READ type-store:"type:{gameServiceId}:{seedTypeCode}"      -> 404 if null
IF type.IsDeprecated                                       -> 200 (idempotent)
type.IsDeprecated = true
type.DeprecatedAt = now
type.DeprecationReason = request.Reason
WRITE type-store:"type:{...}" <- updated
PUBLISH seed.type.updated { ..., changedFields: ["isDeprecated", "deprecatedAt", "deprecationReason"] }
RETURN (200, SeedTypeResponse)
```

### UndeprecateSeedType
POST /seed/type/undeprecate | Roles: [developer]

```
READ type-store:"type:{gameServiceId}:{seedTypeCode}"      -> 404 if null
IF !type.IsDeprecated                                      -> 200 (idempotent)
type.IsDeprecated = false
type.DeprecatedAt = null
type.DeprecationReason = null
WRITE type-store:"type:{...}" <- updated
PUBLISH seed.type.updated { ..., changedFields: ["isDeprecated", "deprecatedAt", "deprecationReason"] }
RETURN (200, SeedTypeResponse)
```

### DeleteSeedType
POST /seed/type/delete | Roles: [developer]

```
LOCK seed-lock:"type:{gameServiceId}:{seedTypeCode}"       -> 409 if fails
  READ type-store:"type:{gameServiceId}:{seedTypeCode}"    -> 404 if null
  IF !type.IsDeprecated                                    -> 400 (must deprecate first)
  COUNT seed-store WHERE SeedTypeCode, GameServiceId, Status != Archived
  IF count > 0                                             -> 409 (non-archived seeds exist)
  DELETE type-store:"type:{gameServiceId}:{seedTypeCode}"
  PUBLISH seed.type.deleted { seedTypeCode, gameServiceId, ... }
RETURN (200, null)
```

### InitiateBond
POST /seed/bond/initiate | Roles: [user]

```
// Acquire two locks in deterministic order (sorted by Guid) to prevent deadlocks
LOCK seed-lock:{orderedIds[0]}                             -> 409 if fails
  LOCK seed-lock:{orderedIds[1]}                           -> 409 if fails
    READ seed-store:"seed:{initiatorSeedId}"               -> 404 if null
    READ seed-store:"seed:{targetSeedId}"                  -> 404 if null
    IF initiator.SeedTypeCode != target.SeedTypeCode       -> 400
    READ type-store:"type:{...}"                           -> 400 if null
    IF type.BondCardinality < 1                            -> 400 (type doesn't support bonding)
    IF initiator.BondId has value                          -> 409 (already bonded)
    IF target.BondId has value                             -> 409 (already bonded)
    // Initiator auto-confirmed; target pending
    WRITE bond-store:"bond:{newBondId}" <- SeedBondModel { Status: PendingConfirmation }
    // Note: seed BondId is NOT set until ConfirmBond activates the bond
RETURN (200, BondResponse)
```

### ConfirmBond
POST /seed/bond/confirm | Roles: [user]

```
LOCK seed-lock:"bond:{bondId}"                             -> 409 if fails
  READ bond-store:"bond:{bondId}"                          -> 404 if null
  IF bond.Status != PendingConfirmation                    -> 400
  // Find confirming participant
  IF confirmingSeedId not in participants                   -> 400
  participant.Confirmed = true
  IF all participants confirmed
    bond.Status = Active
    FOREACH participant in bond.Participants
      READ seed-store:"seed:{participant.SeedId}"
      seed.BondId = bondId
      WRITE seed-store:"seed:{participant.SeedId}" <- updated
    PUBLISH seed.bond.formed { bondId, seedTypeCode, participantSeedIds }
  WRITE bond-store:"bond:{bondId}" <- updated
RETURN (200, BondResponse)
```

### GetBond
POST /seed/bond/get | Roles: [user]

```
READ bond-store:"bond:{bondId}"                            -> 404 if null
RETURN (200, BondResponse)
```

### GetBondForSeed
POST /seed/bond/get-for-seed | Roles: [user]

```
READ seed-store:"seed:{seedId}"                            -> 404 if null
IF seed.BondId is null                                     -> 404
READ bond-store:"bond:{seed.BondId}"                       -> 404 if null
RETURN (200, BondResponse)
```

### GetBondPartners
POST /seed/bond/get-partners | Roles: [user]

```
READ seed-store:"seed:{seedId}"                            -> 404 if null or no BondId
READ bond-store:"bond:{seed.BondId}"                       -> 404 if null
FOREACH participant in bond.Participants (where seedId != request.seedId)
  READ seed-store:"seed:{participant.SeedId}"
  // Silently skip if null
RETURN (200, BondPartnersResponse { bondId, partners })
```

---

## Background Services

### SeedDecayWorkerService
**Interval**: config.DecayWorkerIntervalSeconds (default 900s / 15min)
**Startup Delay**: config.DecayWorkerStartupDelaySeconds (default 30s)
**Purpose**: Applies exponential growth decay to inactive seed domains

```
// Creates a new DI scope per cycle for store access
QUERY type-store WHERE SeedTypeCode exists PAGED(typeOffset, config.DefaultQueryPageSize)
FOREACH seedType in results
  (decayEnabled, ratePerDay) = ResolveDecayConfig(seedType, config)
  // Per-type override wins, then global fallback
  IF !decayEnabled or ratePerDay <= 0 -> skip
  QUERY seed-store WHERE SeedTypeCode, GameServiceId, Status != Archived PAGED(offset, config.DefaultQueryPageSize)
  FOREACH seed in results
    LOCK seed-lock:{seed.SeedId} timeout:10 -> skip if fails
      READ seed-store:"seed:{seedId}"       // Re-read under lock
      READ growth-store:"growth:{seedId}"   -> skip if null or no domains
      FOREACH (domain, entry) in growth.Domains
        decayStartTime = max(entry.LastActivityAt, growth.LastDecayedAt ?? seed.CreatedAt)
        decayDays = (now - decayStartTime).TotalDays
        IF decayDays <= 0 -> skip domain
        entry.Depth *= (1 - ratePerDay) ^ decayDays
      growth.LastDecayedAt = now
      WRITE growth-store:"growth:{seedId}" <- updated
      IF any domains decayed
        seed.TotalGrowth = sum of domain depths
        // Recompute phase from type definition
        IF phase regressed
          WRITE seed-store:"seed:{seedId}" <- updated
          PUBLISH seed.phase.changed { ..., direction: Regressed }
          DISPATCH ISeedEvolutionListener.OnPhaseChangedAsync
        DELETE cap-store:"cap:{seedId}"     // Invalidate capability cache
```
