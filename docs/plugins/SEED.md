# Seed Plugin Deep Dive

> **Plugin**: lib-seed
> **Schema**: schemas/seed-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFoundation
> **State Stores**: seed-statestore (MySQL), seed-growth-statestore (MySQL), seed-type-definitions-statestore (MySQL), seed-bonds-statestore (MySQL), seed-capabilities-cache (Redis), seed-lock (Redis)

---

## Overview

Generic progressive growth primitive (L2 GameFoundation) for game entities. Seeds start empty and grow by accumulating metadata across named domains, progressively gaining capabilities at configurable thresholds. Seeds are polymorphically owned (accounts, actors, realms, characters, relationships) and agnostic to what they represent -- guardian spirits, dungeon cores, combat archetypes, crafting specializations, and governance roles are all equally valid seed types. Seed types are string codes (not enums), allowing new types without schema changes. Each seed type defines its own growth phase labels, capability computation rules, and bond semantics. Consumers register seed types via API, contribute growth via the record API or DI provider listeners (e.g., Collection→Seed pipeline), and query capability manifests to gate actions.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Persistence for seeds, growth data, type definitions, bonds (MySQL); capability cache (Redis); distributed locks (Redis) |
| lib-state (`IDistributedLockProvider`) | Distributed locks for growth recording, seed activation, and seed updates |
| lib-messaging (`IMessageBus`) | Publishing lifecycle and growth events; error event publication |
| `ICollectionUnlockListener` (DI provider) | `SeedCollectionUnlockListener` registered as singleton; receives Collection entry unlock notifications via in-process DI dispatch for guaranteed delivery |
| lib-game-service (`IGameServiceClient`) | Validates game service existence during seed creation and type registration (L2 hard dependency) |
| lib-worldstate (`IWorldstateClient`, L2, **required future migration**) | Decay worker MUST transition from real-time intervals to game-time via Worldstate's `GetElapsedGameTime` API. At the default 24:1 time ratio, real-time decay is 24x slower than it should be per game-day. Seeds representing guardian spirits, dungeon cores, and faction growth all evolve in the simulated world's time, not server time. Migration adds a `DecayTimeSource` config property (enum: `RealTime`, `GameTime`; default `GameTime` once Worldstate is implemented). |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-actor (L2) | Actor discovers `SeedProviderFactory` via `IEnumerable<IVariableProviderFactory>` DI injection; creates `SeedProvider` instances per character for ABML behavior execution (`${seed.*}` variables) |
| lib-collection (L2) | Collection dispatches entry unlock notifications to `SeedCollectionUnlockListener` via `ICollectionUnlockListener` DI interface; listener matches entry tags against seed type `collectionGrowthMappings` to drive growth |
| lib-gardener (planned, L4) | First consumer -- creates `guardian` seeds for player accounts, contributes growth, queries capability manifests for UX module gating, manages seed bonds as the pair system |
| Dungeon plugin (planned, L4) | Will create `dungeon_core` and `dungeon_master` seeds for actor/character entities |
| Any future L4 consumer | Registers seed types via `seed/type/register`, contributes growth via `seed/growth/record`, queries manifests via `seed/capability/get-manifest` |

---

## Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `ownerType` | A (Entity Reference) | `EntityType` enum | All valid values are first-class Bannou entities (accounts, actors, realms, characters, relationships). Recently migrated to shared EntityType enum. |
| `seedTypeCode` | B (Content Code) | Opaque string | Game-configurable seed type identifier. New types registered via API without schema changes (e.g., `guardian`, `dungeon_core`, `combat_archetype`). |
| `growthPhase` | B (Content Code) | Opaque string | Phase labels defined per seed type in `GrowthPhases` configuration. Extensible per type without schema changes (e.g., `nascent`, `stirring`, `awakened`, `ancient`). Falls back to `"initial"` if no phases defined. |
| `status` | C (System State) | `SeedStatus` enum | Finite lifecycle states: `active`, `dormant`, `archived`. System-owned transitions. |
| `direction` (SeedPhaseChangedEvent) | C (System State) | `PhaseChangeDirection` enum | Binary system state: `progressed` or `regressed`. Determined by growth vs decay mechanics. |

---

## State Storage

**Store**: `seed-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `seed:{seedId}` | `SeedModel` | Core seed entity with owner, type, phase, status, bond reference |

**Store**: `seed-growth-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `growth:{seedId}` | `SeedGrowthModel` | Per-domain growth entries (`DomainGrowthEntry` with `Depth`, `LastActivityAt`, `PeakDepth`), plus `LastDecayedAt` for decay worker tracking |

**Store**: `seed-type-definitions-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `type:{gameServiceId}:{seedTypeCode}` | `SeedTypeDefinitionModel` | Registered seed type definitions with phases, capability rules, bond config |

**Store**: `seed-bonds-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `bond:{bondId}` | `SeedBondModel` | Bond entities with participant list, strength, shared growth |

**Store**: `seed-capabilities-cache` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `cap:{seedId}` | `CapabilityManifestModel` | Computed capability manifest with fidelity scores; invalidated on growth recording, recomputed on next read |

**Store**: `seed-lock` (Backend: Redis)

| Key Pattern | Purpose |
|-------------|---------|
| `{seedId}` | Distributed lock for growth recording, seed updates, and bond initiation (ordered dual-lock for bonds) |
| `bond:{bondId}` | Distributed lock for bond confirmation (serializes concurrent participant confirmations) |
| `owner:{ownerId}:{seedTypeCode}` | Distributed lock for seed activation (prevents concurrent activation races) |
| `type:{gameServiceId}:{seedTypeCode}` | Distributed lock for seed type updates (prevents concurrent type mutations) |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `seed.created` | `SeedCreatedEvent` | New seed created via `CreateSeedAsync` |
| `seed.updated` | `SeedUpdatedEvent` | Seed display name or metadata updated; includes `ChangedFields` list |
| `seed.activated` | `SeedActivatedEvent` | Seed set to Active status; includes `PreviousActiveSeedId` if another seed was deactivated |
| `seed.archived` | `SeedArchivedEvent` | Seed archived (soft delete) |
| `seed.growth.updated` | `SeedGrowthUpdatedEvent` | Per-domain event fired for each domain in a growth recording; includes previous and new depth |
| `seed.phase.changed` | `SeedPhaseChangedEvent` | Seed crossed a phase threshold during growth recording or decay; includes `Direction` (`Progressed` or `Regressed`) |
| `seed.capability.updated` | `SeedCapabilityUpdatedEvent` | Capability manifest recomputed and cached; includes manifest version and unlocked count |
| `seed.bond.formed` | `SeedBondFormedEvent` | Bond transitions to Active after all participants confirm |
| `seed.type.created` | `SeedTypeCreatedEvent` | New seed type registered via `RegisterSeedTypeAsync` |
| `seed.type.updated` | `SeedTypeUpdatedEvent` | Seed type updated, deprecated, or undeprecated; includes `ChangedFields` list |
| `seed.type.deleted` | `SeedTypeDeletedEvent` | Seed type hard-deleted via `DeleteSeedTypeAsync` |

### Consumed Events

No event subscriptions. The Collection→Seed growth pipeline uses the `ICollectionUnlockListener` DI provider pattern for guaranteed in-process delivery instead of event bus subscriptions. See `SeedCollectionUnlockListener.cs`.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `CapabilityRecomputeDebounceMs` | `SEED_CAPABILITY_RECOMPUTE_DEBOUNCE_MS` | `5000` | Debounce window (ms) for capability cache; reads within this window return cached manifest without recomputing |
| `GrowthDecayEnabled` | `SEED_GROWTH_DECAY_ENABLED` | `false` | Global toggle for background growth decay; per-type overrides take precedence when set |
| `GrowthDecayRatePerDay` | `SEED_GROWTH_DECAY_RATE_PER_DAY` | `0.01` | Global daily exponential decay rate; per-type overrides take precedence when set |
| `DecayWorkerIntervalSeconds` | `SEED_DECAY_WORKER_INTERVAL_SECONDS` | `900` | Seconds between decay worker cycles (range: 60-86400) |
| `DecayWorkerStartupDelaySeconds` | `SEED_DECAY_WORKER_STARTUP_DELAY_SECONDS` | `30` | Seconds to wait after startup before first decay cycle (range: 0-300) |
| `BondSharedGrowthMultiplier` | `SEED_BOND_SHARED_GROWTH_MULTIPLIER` | `1.5` | Growth amount multiplier applied when recording growth for a seed with an active bond |
| `MaxSeedTypesPerGameService` | `SEED_MAX_SEED_TYPES_PER_GAME_SERVICE` | `50` | Maximum number of seed type definitions per game service |
| `DefaultMaxSeedsPerOwner` | `SEED_DEFAULT_MAX_SEEDS_PER_OWNER` | `3` | Default per-owner seed limit when seed type's `MaxPerOwner` is 0 |
| `BondStrengthGrowthRate` | `SEED_BOND_STRENGTH_GROWTH_RATE` | `0.1` | Rate at which bond strength increases per unit of shared growth recorded |
| `SeedDataCacheTtlSeconds` | `SEED_SEED_DATA_CACHE_TTL_SECONDS` | `60` | TTL in seconds for the seed data cache used by the variable provider factory (range: 5-3600) |
| `DefaultQueryPageSize` | `SEED_DEFAULT_QUERY_PAGE_SIZE` | `100` | Default page size for queries that do not expose pagination parameters (`GetSeedsByOwnerAsync`, `ListSeedTypesAsync`, `RecomputeSeedsForTypeAsync`) |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<SeedService>` | Structured logging |
| `SeedServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access for MySQL and Redis stores |
| `IMessageBus` | Event publishing and error event publication |
| `IDistributedLockProvider` | Distributed locks for mutation operations |
| `SeedCollectionUnlockListener` | Implements `ICollectionUnlockListener` (registered as singleton); matches entry tags against seed type `collectionGrowthMappings` to drive growth |
| `IGameServiceClient` | Validates game service existence during seed creation and type registration |
| `ISeedDataCache` / `SeedDataCache` | Singleton cache for character seed data (seeds, growth, capabilities) used by the variable provider factory; TTL-based expiration with `ConcurrentDictionary`; loads via `ISeedClient` through mesh |
| `SeedProviderFactory` | Implements `IVariableProviderFactory` to provide `${seed.*}` variables to the Actor service's behavior system; creates `SeedProvider` instances from cached data |
| `SeedDecayWorkerService` | Background `HostedService` that periodically applies exponential decay to growth domains; disabled when `GrowthDecayEnabled` is false |

---

## API Endpoints (Implementation Notes)

### Seed CRUD (7 endpoints)

Standard CRUD operations on seed entities. `CreateSeedAsync` validates the game service exists via `IGameServiceClient`, validates the seed type exists, checks the owner type is in the type's `AllowedOwnerTypes`, and enforces the per-owner limit via JSON query counting (excluding archived seeds). Initial phase is determined using `ComputePhaseInfo` against the type's sorted `GrowthPhases` at 0 total growth (falls back to `"initial"` if no phases defined). Seeds are always created with `Active` status.

`ActivateSeedAsync` implements exclusive activation -- only one seed of a given type can be active per owner. When activating a seed, all other active seeds of the same type for the same owner are set to `Dormant`. Uses a distributed lock scoped to `owner:{ownerId}:{seedTypeCode}` to prevent concurrent activation races.

`ArchiveSeedAsync` requires the seed to be `Dormant` first; active seeds cannot be archived directly.

`ListSeedsAsync` supports server-side pagination via `Page` and `PageSize` request fields. `GetSeedsByOwnerAsync` uses `DefaultQueryPageSize` configuration for its query limit.

### Growth (4 endpoints)

`RecordGrowthAsync` and `RecordGrowthBatchAsync` both delegate to `RecordGrowthInternalAsync`, which acquires a distributed lock on the seed, verifies Active status, applies the bond shared growth multiplier if the seed has an active permanent bond, updates per-domain `DomainGrowthEntry` values (Depth, LastActivityAt, PeakDepth), checks for phase transitions against the seed type definition, and invalidates the capability cache by deleting it from Redis. Publishes `seed.growth.updated` per domain and `seed.phase.changed` (with `Direction = Progressed`) if a phase boundary was crossed. For permanent bonds, resets `LastActivityAt` on matching domains of bonded partners to prevent their decay.

`GetGrowthAsync` returns domain depths directly from stored values. Decay is applied by the background `SeedDecayWorkerService`, not at read time.

**Cross-pollination**: When `SameOwnerGrowthMultiplier` > 0 on the seed type definition, growth recording on a seed also applies a fraction of the raw growth (before bond multiplier) to sibling seeds of the same type owned by the same entity. Cross-pollination uses a try-lock with 3-second timeout per sibling (best-effort -- lock failures are silently skipped). Full processing is applied to each sibling: phase transitions, capability cache invalidation, and event publication. Events from cross-pollinated growth have `CrossPollinated = true`. Dormant siblings receive cross-pollinated growth; archived siblings do not. Cross-pollination is structurally prevented from cascading by using a dedicated `ApplyCrossPollination` method that never queries for further siblings.

`GetGrowthPhaseAsync` computes the current phase and next phase threshold from the seed type definition using `ComputePhaseInfo`, which iterates sorted phases and returns the highest phase whose `MinTotalGrowth` the seed has reached.

### Capabilities (1 endpoint)

`GetCapabilityManifestAsync` implements a read-through cache pattern. Checks Redis cache first; if the cached manifest is within the debounce window (`CapabilityRecomputeDebounceMs`), returns it directly. On cache miss or stale cache, computes the manifest from the seed type's `CapabilityRules` by evaluating each rule's domain depth against its unlock threshold and fidelity formula. Three fidelity formulas are supported:

- **linear** (default): 0 at threshold, 1 at 2x threshold. Formula: `(normalized - 1.0) / 1.0`
- **logarithmic**: `log(1 + normalized) / log(2)`, capped at 1.0
- **step**: 0 below threshold, 0.5 at 1-2x threshold, 1.0 at 2x+ threshold

Manifest version is monotonically incremented from the previous cached version.

### Seed Type Definitions (7 endpoints)

`RegisterSeedTypeAsync` validates the game service exists via `IGameServiceClient`, checks for duplicate type codes per game service, and enforces `MaxSeedTypesPerGameService`. Type definitions include growth phase definitions (labels + thresholds), capability rules (domain-to-capability mapping with fidelity formulas), bond configuration (cardinality + permanence), allowed owner types, optional per-type decay overrides (`GrowthDecayEnabled`, `GrowthDecayRatePerDay`) that take precedence over global configuration, and a `SameOwnerGrowthMultiplier` (0.0-1.0, default 0.0) that controls cross-pollination of growth to same-type same-owner siblings. Publishes `seed.type.created` lifecycle event.

`UpdateSeedTypeAsync` acquires a distributed lock on the type key, supports partial updates (only non-null fields applied), and triggers recomputation of all existing seeds' phases and capability caches when growth phases or capability rules change. Publishes `seed.type.updated` with `changedFields`.

`ListSeedTypesAsync` supports `includeDeprecated` filter (default: false) to control visibility of deprecated types.

`DeprecateSeedTypeAsync` marks a seed type as deprecated, preventing creation of new seeds of this type. Existing seeds are unaffected. Publishes `seed.type.updated` with `changedFields = ["isDeprecated", "deprecatedAt", "deprecationReason"]`. No distributed lock needed (simple flag flip).

`UndeprecateSeedTypeAsync` restores a deprecated seed type to active status. Publishes `seed.type.updated` with the same changed fields.

`DeleteSeedTypeAsync` hard-deletes a deprecated seed type. Requires deprecation first (`BadRequest` if not deprecated) and zero non-archived seeds (`Conflict` if any exist, checked via same-service JSON query). Acquires a distributed lock to prevent concurrent deletes. Publishes `seed.type.deleted`. `CreateSeedAsync` checks the `IsDeprecated` flag to prevent seed creation for deprecated types. No merge endpoint exists -- see [#374](https://github.com/beyond-immersion/bannou-service/issues/374).

### Bonds (5 endpoints)

`InitiateBondAsync` acquires ordered distributed locks on both seed IDs (sorted to prevent deadlock), validates both seeds exist, are the same type, and the type supports bonding (`BondCardinality >= 1`). Checks neither seed is already bonded. Creates a bond with the initiator auto-confirmed and the target pending confirmation.

`ConfirmBondAsync` sets the confirming participant's `Confirmed` flag. When all participants are confirmed, transitions bond to `Active`, updates all participant seeds with the `BondId`, and publishes `seed.bond.formed`.

`GetBondForSeedAsync` follows the seed's `BondId` reference to load the bond. `GetBondPartnersAsync` loads partner seed summaries for all bond participants except the requesting seed.

---

## Visual Aid

```
┌─────────────────────────────────────────────────────────────────────┐
│                    State Store Key Relationships                     │
│                                                                     │
│   seed-type-definitions-statestore (MySQL)                          │
│   ┌──────────────────────────────────────────┐                      │
│   │ type:{gameServiceId}:{seedTypeCode}      │                      │
│   │ ├── GrowthPhases[] (phase thresholds)    │←─── Looked up by    │
│   │ ├── CapabilityRules[] (fidelity formulas)│     CreateSeed,      │
│   │ ├── BondCardinality / BondPermanent      │     RecordGrowth,    │
│   │ └── AllowedOwnerTypes / MaxPerOwner      │     GetManifest,     │
│   └──────────────────────────────────────────┘     InitiateBond     │
│                                                                     │
│   seed-statestore (MySQL)           seed-growth-statestore (MySQL)  │
│   ┌──────────────────────┐         ┌─────────────────────────────┐ │
│   │ seed:{seedId}        │         │ growth:{seedId}              │ │
│   │ ├── OwnerId/Type     │    1:1  │ ├── Domains: {               │ │
│   │ ├── SeedTypeCode   ──┼──ref──→ │ │   "combat.melee": {        │ │
│   │ ├── GrowthPhase      │         │ │     Depth: 3.2,            │ │
│   │ ├── TotalGrowth      │         │ │     LastActivityAt: ...,   │ │
│   │ ├── Status           │         │ │     PeakDepth: 4.1 }       │ │
│   │ └── BondId? ─────┐   │         │ │ }                          │ │
│   └──────────────────┼───┘         │ ├── LastDecayedAt?           │ │
│                      │              │ └──────────────┬──────────────┘ │
│                      ▼              │               ▼                │
│                      │              │  seed-capabilities-cache       │
│                      ▼              │  (Redis)                       │
│   seed-bonds-statestore (MySQL)    │  ┌─────────────────────────┐  │
│   ┌──────────────────────┐         │  │ cap:{seedId}            │  │
│   │ bond:{bondId}        │         │  │ ├── Version (monotonic) │  │
│   │ ├── Participants[]   │         │  │ ├── ComputedAt          │  │
│   │ │   ├── SeedId ──────┼──ref──→ │  │ └── Capabilities[] {   │  │
│   │ │   ├── Role         │  seed   │  │     code, fidelity,     │  │
│   │ │   └── Confirmed    │         │  │     unlocked }          │  │
│   │ ├── BondStrength     │         │  └─────────────────────────┘  │
│   │ ├── SharedGrowth     │         │  Invalidated on growth record │
│   │ └── Status           │         │  Recomputed on next read      │
│   └──────────────────────┘                                          │
│                                                                     │
│   seed-lock (Redis)                                                 │
│   ├── {seedId}                    -- Growth recording, updates,     │
│   │                                  bond initiation (ordered)      │
│   ├── bond:{bondId}              -- Bond confirmation serialization │
│   ├── owner:{ownerId}:{typeCode}  -- Activation exclusivity        │
│   └── type:{gsId}:{typeCode}      -- Type definition updates       │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

*(No current stubs -- growth decay was the last stub and is now fully implemented.)*

---

## Potential Extensions

- **Bond dissolution endpoint**: No endpoint exists to dissolve or break a bond. The `BondPermanent` flag on seed type definitions implies some bonds should be dissolvable, but no dissolution flow is implemented. Would need to handle clearing `BondId` on participant seeds, emitting a dissolution event, and respecting the permanence flag.
<!-- AUDIT:NEEDS_DESIGN:2026-02-09:https://github.com/beyond-immersion/bannou-service/issues/362 -->

- **Seed type merge**: No `MergeSeedType` endpoint exists. Unlike species merge (simple foreign key reassignment), seed type merge is fundamentally complex due to incompatible growth domains, phase definitions, capability rules, bond constraints, and per-owner limits. See [#374](https://github.com/beyond-immersion/bannou-service/issues/374).
<!-- AUDIT:NEEDS_DESIGN:2026-02-09:https://github.com/beyond-immersion/bannou-service/issues/374 -->

- **Cross-seed-type growth transfer matrix** ([#354](https://github.com/beyond-immersion/bannou-service/issues/354)): When an entity holds seeds of different types (e.g., `guardian` + `dungeon_master`), experience in one role could partially feed growth in the other via configurable `SeedGrowthTransferRule` mappings with domain-to-domain multipliers. Distinct from same-type cross-pollination (`SameOwnerGrowthMultiplier`, already implemented). Not blocking initial implementation -- add when gameplay testing validates which cross-type relationships feel right.
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/354 -->

- **Seed owner type promotion / re-parenting** ([#437](https://github.com/beyond-immersion/bannou-service/issues/437)): Mechanism to atomically change a seed's `ownerType` and `ownerId` (e.g., character-owned → account-owned) while preserving all growth data. Required for dungeon Pattern A (full split mastery) where the `dungeon_master` seed promotes from character to account ownership. Depends on #436 (household split mechanic). Recommended approach: direct `seed/reparent` endpoint with per-owner limit validation against the new owner.
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/437 -->

- **Client events for guardian spirit progression** ([#497](https://github.com/beyond-immersion/bannou-service/issues/497)): Push `SeedPhaseChanged`, `SeedCapabilityChanged`, `SeedGrowthUpdated`, `SeedBondFormed`, and `SeedActivated` client events via `IClientEventPublisher` using the Entity Session Registry (#426). Gardener registers `seed → session` bindings. The Entity Session Registry resolves the previous concern about "introducing session/WebSocket awareness into a foundational service" — Seed only needs `IEntitySessionRegistry` (L1 hard dependency) and `IClientEventPublisher` (L1), not session management knowledge. Supersedes the previous "L4 consumer responsibility" framing from [#365](https://github.com/beyond-immersion/bannou-service/issues/365). Also includes investigation items: verify whether `seed.updated` is emitted for the deactivated seed during `ActivateSeedAsync`, and consider adding `seed.bond.initiated` for pending bond state.
<!-- AUDIT:NEEDS_DESIGN:2026-02-26:https://github.com/beyond-immersion/bannou-service/issues/497 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*(No current bugs.)*

### Intentional Quirks (Documented Behavior)

- **Seeds always created Active**: `CreateSeedAsync` always sets `Status = SeedStatus.Active` without checking if another seed of the same type is already active for the same owner. This means creating a second seed does NOT deactivate the first -- both remain active until `ActivateSeedAsync` is explicitly called on one, which deactivates the others.

- **Capability cache is invalidated, not updated**: Growth recording deletes the cache entry (`DeleteAsync`) rather than eagerly recomputing. The manifest is only recomputed on the next `GetCapabilityManifest` call. This is intentional for performance -- rapid growth bursts (e.g., batch operations) don't waste computation on intermediate manifests.

- **Linear fidelity yields 0 at exact threshold**: The linear formula `(normalized - 1.0) / 1.0` produces 0.0 when `domainDepth == threshold` (normalized = 1.0). Fidelity only begins increasing above the threshold and reaches 1.0 at exactly 2x the threshold. This means "unlocked" capabilities start with zero fidelity.

- **Growth decay is write-back via background worker**: The `SeedDecayWorkerService` always runs on its configured interval and resolves decay config per type via `ResolveDecayConfig` (per-type override wins, then global fallback). Applies exponential decay (`depth *= (1 - ratePerDay) ^ decayDays`) to stored domain values for types where decay resolves to enabled. Decay is not applied at read time -- `GetGrowthAsync` returns stored values directly. Phase regressions are detected and published as `SeedPhaseChangedEvent` with `Direction = Regressed`. The global `GrowthDecayEnabled` is the fallback default, not a kill switch -- a type with `GrowthDecayEnabled = true` will decay even if the global is false.

- **Bond shared activity resets partner decay timers**: When a bonded seed (permanent bond only) records growth, `LastActivityAt` is reset on matching domains of all bonded partners. This prevents partners from decaying in domains they share activity in, even if the partner itself didn't record growth directly.

- **Metadata wrapped in `data` key**: When creating or updating seed metadata, the user-provided metadata object is nested under a `data` key in the stored model (`seed.Metadata["data"] = body.Metadata`). Consumers must be aware of this wrapping.

- **Phase computation sorts phases on every call**: `ComputePhaseInfo` calls `phases.OrderBy(p => p.MinTotalGrowth).ToList()` on every invocation rather than requiring pre-sorted phase definitions. This is safe but wasteful for frequent calls.

- **Cross-pollination is best-effort**: `ApplyCrossPollination` acquires a try-lock with 3-second timeout. If the sibling seed is locked by another operation, cross-pollination is silently skipped (logged at Debug level). This prevents deadlocks but means siblings may occasionally miss cross-pollinated growth during high-contention periods. The multiplier uses raw amounts (before bond multiplier), and the receiving side does not apply its own bond multiplier.

### Design Considerations (Requires Planning)

- **No cleanup of associated data on archive**: `ArchiveSeedAsync` sets the seed's status to `Archived` but does not clean up growth data (`growth:{seedId}`), capability cache (`cap:{seedId}`), or bond data (`bond:{bondId}`). Archived seeds retain all associated state indefinitely. A cleanup strategy is needed -- either immediate deletion, a background retention worker, or integration with lib-resource for compression.
<!-- AUDIT:NEEDS_DESIGN:2026-02-09:https://github.com/beyond-immersion/bannou-service/issues/366 -->

- **Decay worker uses real-time, should use game-time**: The decay background worker (`SeedDecayWorkerService`) uses `DateTimeOffset.UtcNow` and real-time `Task.Delay` intervals. In a world with a 24:1 game-time ratio, decay applied per real-time cycle is 24x slower than intended per game-day. Guardian spirits, dungeon cores, faction seeds, and all other seed types evolve in the simulated world's time. When Worldstate (L2) is implemented, the decay worker must call `GetElapsedGameTime` to compute game-days elapsed since last decay cycle, then apply `GrowthDecayRatePerDay` against game-days rather than real-days. The `DecayWorkerIntervalSeconds` config remains the real-time check frequency; the decay amount per cycle is computed from game-time elapsed.
<!-- AUDIT:BLOCKED:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/434 -->

---

## Work Tracking

- [#361](https://github.com/beyond-immersion/bannou-service/issues/361) - Variable provider factory for Actor behavior system (implemented: `SeedProviderFactory`, `SeedProvider`, `SeedDataCache`)
- [#434](https://github.com/beyond-immersion/bannou-service/issues/434) - Seed decay worker must transition from real-time to game-time via Worldstate (blocked by Worldstate implementation)
