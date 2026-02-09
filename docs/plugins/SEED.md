# Seed Plugin Deep Dive

> **Plugin**: lib-seed
> **Schema**: schemas/seed-api.yaml
> **Version**: 1.0.0
> **State Stores**: seed-statestore (MySQL), seed-growth-statestore (MySQL), seed-type-definitions-statestore (MySQL), seed-bonds-statestore (MySQL), seed-capabilities-cache (Redis), seed-lock (Redis)

---

## Overview

Generic progressive growth primitive (L2 GameFoundation) for game entities. Seeds start empty and grow by accumulating metadata across named domains, progressively gaining capabilities at configurable thresholds. Seeds are polymorphically owned (accounts, actors, realms, characters, relationships) and agnostic to what they represent -- guardian spirits, dungeon cores, combat archetypes, crafting specializations, and governance roles are all equally valid seed types. Seed types are string codes (not enums), allowing new types without schema changes. Each seed type defines its own growth phase labels, capability computation rules, and bond semantics. Consumers register seed types via API, contribute growth via events, and query capability manifests to gate actions.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Persistence for seeds, growth data, type definitions, bonds (MySQL); capability cache (Redis); distributed locks (Redis) |
| lib-state (`IDistributedLockProvider`) | Distributed locks for growth recording, seed activation, and seed updates |
| lib-messaging (`IMessageBus`) | Publishing lifecycle and growth events; error event publication |
| lib-messaging (`IEventConsumer`) | Consuming `seed.growth.contributed` events from external services |
| lib-game-service (`IGameServiceClient`) | Validates game service existence during seed creation and type registration (L2 hard dependency) |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-gardener (planned, L4) | First consumer -- creates `guardian` seeds for player accounts, contributes growth, queries capability manifests for UX module gating, manages seed bonds as the pair system |
| Dungeon plugin (planned, L4) | Will create `dungeon_core` and `dungeon_master` seeds for actor/character entities |
| Any future L4 consumer | Registers seed types via `seed/type/register`, contributes growth via `seed.growth.contributed` event, queries manifests via `seed/capability/get-manifest` |

---

## State Storage

**Store**: `seed-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `seed:{seedId}` | `SeedModel` | Core seed entity with owner, type, phase, status, bond reference |

**Store**: `seed-growth-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `growth:{seedId}` | `SeedGrowthModel` | Domain-to-depth map for growth tracking |

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
| `{seedId}` | Distributed lock for growth recording and seed updates |
| `owner:{ownerId}:{seedTypeCode}` | Distributed lock for seed activation (prevents concurrent activation races) |

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
| `seed.phase.changed` | `SeedPhaseChangedEvent` | Seed crossed a phase threshold during growth recording |
| `seed.capability.updated` | `SeedCapabilityUpdatedEvent` | Capability manifest recomputed and cached; includes manifest version and unlocked count |
| `seed.bond.formed` | `SeedBondFormedEvent` | Bond transitions to Active after all participants confirm |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `seed.growth.contributed` | `HandleGrowthContributedAsync` | External services report growth to a seed; calls `RecordGrowthInternalAsync` with `CancellationToken.None` |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `CapabilityRecomputeDebounceMs` | `SEED_CAPABILITY_RECOMPUTE_DEBOUNCE_MS` | `5000` | Debounce window (ms) for capability cache; reads within this window return cached manifest without recomputing |
| `GrowthDecayEnabled` | `SEED_GROWTH_DECAY_ENABLED` | `false` | Whether growth domains decay over time on read |
| `GrowthDecayRatePerDay` | `SEED_GROWTH_DECAY_RATE_PER_DAY` | `0.01` | Daily decay rate applied to all domain values when decay is enabled |
| `BondSharedGrowthMultiplier` | `SEED_BOND_SHARED_GROWTH_MULTIPLIER` | `1.5` | Growth amount multiplier applied when recording growth for a bonded seed with an active bond |
| `MaxSeedTypesPerGameService` | `SEED_MAX_SEED_TYPES_PER_GAME_SERVICE` | `50` | Maximum number of seed type definitions per game service |
| `DefaultMaxSeedsPerOwner` | `SEED_DEFAULT_MAX_SEEDS_PER_OWNER` | `3` | Default per-owner seed limit when seed type's `MaxPerOwner` is 0 |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<SeedService>` | Structured logging |
| `SeedServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access for MySQL and Redis stores |
| `IMessageBus` | Event publishing and error event publication |
| `IDistributedLockProvider` | Distributed locks for mutation operations |
| `IEventConsumer` | Event subscription registration for `seed.growth.contributed` |
| `IGameServiceClient` | Injected but unused -- intended for game service existence validation |

---

## API Endpoints (Implementation Notes)

### Seed CRUD (7 endpoints)

Standard CRUD operations on seed entities. `CreateSeedAsync` validates the seed type exists, checks the owner type is in the type's `AllowedOwnerTypes`, and enforces the per-owner limit via JSON query counting. Initial phase is determined from the first entry in the type's `GrowthPhases` list (falls back to `"initial"` if no phases defined). Seeds are always created with `Active` status.

`ActivateSeedAsync` implements exclusive activation -- only one seed of a given type can be active per owner. When activating a seed, all other active seeds of the same type for the same owner are set to `Dormant`. Uses a distributed lock scoped to `owner:{ownerId}:{seedTypeCode}` to prevent concurrent activation races.

`ArchiveSeedAsync` requires the seed to be `Dormant` first; active seeds cannot be archived directly.

`ListSeedsAsync` supports server-side pagination via `Page` and `PageSize` request fields. `GetSeedsByOwnerAsync` does not support pagination -- see Known Quirks.

### Growth (4 endpoints)

`RecordGrowthAsync` and `RecordGrowthBatchAsync` both delegate to `RecordGrowthInternalAsync`, which acquires a distributed lock on the seed, verifies Active status, applies the bond shared growth multiplier if the seed is bonded with an active bond, updates domain depths, checks for phase transitions against the seed type definition, and invalidates the capability cache by deleting it from Redis. Publishes `seed.growth.updated` per domain and `seed.phase.changed` if a phase boundary was crossed.

`GetGrowthAsync` returns domain depths. When `GrowthDecayEnabled` is true, applies a linear decay factor based on time since seed creation (not per-domain last activity). The decay is read-time only -- stored values are not modified.

`GetGrowthPhaseAsync` computes the current phase and next phase threshold from the seed type definition using `ComputePhaseInfo`, which iterates sorted phases and returns the highest phase whose `MinTotalGrowth` the seed has reached.

### Capabilities (1 endpoint)

`GetCapabilityManifestAsync` implements a read-through cache pattern. Checks Redis cache first; if the cached manifest is within the debounce window (`CapabilityRecomputeDebounceMs`), returns it directly. On cache miss or stale cache, computes the manifest from the seed type's `CapabilityRules` by evaluating each rule's domain depth against its unlock threshold and fidelity formula. Three fidelity formulas are supported:

- **linear** (default): 0 at threshold, 1 at 2x threshold. Formula: `(normalized - 1.0) / 1.0`
- **logarithmic**: `log(1 + normalized) / log(2)`, capped at 1.0
- **step**: 0 below threshold, 0.5 at 1-2x threshold, 1.0 at 2x+ threshold

Manifest version is monotonically incremented from the previous cached version.

### Seed Type Definitions (4 endpoints)

`RegisterSeedTypeAsync` checks for duplicate type codes per game service and enforces `MaxSeedTypesPerGameService`. Type definitions include growth phase definitions (labels + thresholds), capability rules (domain-to-capability mapping with fidelity formulas), bond configuration (cardinality + permanence), and allowed owner types.

`UpdateSeedTypeAsync` supports partial updates -- only non-null fields are applied. No validation that growth phase changes are compatible with existing seeds.

No delete endpoint exists for seed types -- see Known Quirks.

### Bonds (5 endpoints)

`InitiateBondAsync` validates both seeds exist, are the same type, and the type supports bonding (`BondCardinality >= 1`). Checks neither seed is already bonded. Creates a bond with the initiator auto-confirmed and the target pending confirmation. Does not acquire a distributed lock -- see Known Quirks.

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
│   ┌──────────────────────┐         ┌─────────────────────────┐     │
│   │ seed:{seedId}        │         │ growth:{seedId}          │     │
│   │ ├── OwnerId/Type     │    1:1  │ ├── Domains: {           │     │
│   │ ├── SeedTypeCode   ──┼──ref──→ │ │   "combat.melee": 3.2  │     │
│   │ ├── GrowthPhase      │         │ │   "crafting": 8.0      │     │
│   │ ├── TotalGrowth      │         │ │ }                      │     │
│   │ ├── Status           │         │ └──────────────┬──────────┘     │
│   │ └── BondId? ─────┐   │         │               │                │
│   └──────────────────┼───┘         │               ▼                │
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
│   ├── {seedId}                    -- Growth recording, updates      │
│   └── owner:{ownerId}:{typeCode}  -- Activation exclusivity        │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

- **Growth decay**: Configuration properties (`GrowthDecayEnabled`, `GrowthDecayRatePerDay`) are wired up and the read-time decay logic exists in `GetGrowthAsync`, but the feature is disabled by default and the decay formula uses time since seed creation rather than per-domain last activity time. The code comment references "deferred feature #352". The decay is read-only (presentation layer) -- stored domain values are never modified by decay.

- **IGameServiceClient**: Injected into the constructor but never called. Intended for validating that the `GameServiceId` in seed creation requests refers to a real game service. Currently, any GUID is accepted as a game service ID without verification.

---

## Potential Extensions

- **Variable Provider Factory**: Seed could expose `${seed.*}` variables to the Actor service's behavior system (e.g., `${seed.phase}`, `${seed.capabilities.combat.fidelity}`). This would follow the same pattern as character-personality and character-encounter providers.

- **Bond dissolution endpoint**: No endpoint exists to dissolve or break a bond. The `BondPermanent` flag on seed type definitions implies some bonds should be dissolvable, but no dissolution flow is implemented. Would need to handle clearing `BondId` on participant seeds, emitting a dissolution event, and respecting the permanence flag.

- **Seed type deletion**: No `DeleteSeedType` endpoint exists. Removing a type definition would need to consider existing seeds of that type (orphan prevention).

- **Per-domain decay tracking**: Growth decay currently uses seed creation time as the basis for all domains. A more accurate model would track last activity time per domain, allowing active domains to resist decay while inactive ones fade.

- **Capability push notifications**: Currently capabilities are pull-only (consumer calls `GetCapabilityManifest`). A push model via client events would allow real-time UI updates when capabilities unlock.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

- **BondStrength uses hardcoded 0.1f multiplier** (`SeedService.cs:1175`): Bond strength increment is `totalAmount * 0.1f`, a hardcoded magic number. Per IMPLEMENTATION TENETS (no hardcoded tunables), this should be a configuration property. The `BondSharedGrowthMultiplier` config controls the growth multiplier but there is no config for the strength-to-growth ratio.

- **InitiateBond missing distributed lock** (`SeedService.cs:844-927`): `InitiateBondAsync` reads both seeds, checks their bond status, and creates the bond without acquiring a distributed lock. Two concurrent `InitiateBond` calls could both pass the "not already bonded" check and create duplicate bonds for the same seeds. Should lock on both seed IDs (ordered to prevent deadlock).

- **GetSeedsByOwnerAsync hardcodes page size 100** (`SeedService.cs:215`): Uses `JsonQueryPagedAsync` with hardcoded `pageSize: 100` and `offset: 0`. Owners with more than 100 seeds will silently lose results. Unlike `ListSeedsAsync`, which accepts `Page`/`PageSize` from the request, this endpoint has no pagination support.

- **ListSeedTypesAsync hardcodes page size 100** (`SeedService.cs:781`): Same issue -- `JsonQueryPagedAsync` with hardcoded `pageSize: 100`. Game services with more than 50 types are prevented by config, but the max config (`MaxSeedTypesPerGameService`) defaults to 50 and could be raised to exceed the query limit.

### Intentional Quirks (Documented Behavior)

- **Seeds always created Active**: `CreateSeedAsync` always sets `Status = SeedStatus.Active` without checking if another seed of the same type is already active for the same owner. This means creating a second seed does NOT deactivate the first -- both remain active until `ActivateSeedAsync` is explicitly called on one, which deactivates the others.

- **Capability cache is invalidated, not updated**: Growth recording deletes the cache entry (`DeleteAsync`) rather than eagerly recomputing. The manifest is only recomputed on the next `GetCapabilityManifest` call. This is intentional for performance -- rapid growth bursts (e.g., batch operations) don't waste computation on intermediate manifests.

- **Linear fidelity yields 0 at exact threshold**: The linear formula `(normalized - 1.0) / 1.0` produces 0.0 when `domainDepth == threshold` (normalized = 1.0). Fidelity only begins increasing above the threshold and reaches 1.0 at exactly 2x the threshold. This means "unlocked" capabilities start with zero fidelity.

- **Growth decay is read-time presentation only**: When `GrowthDecayEnabled` is true, `GetGrowthAsync` applies a decay factor to returned domain values but does NOT modify stored data. Phase transitions and capability computations use stored (undecayed) values, so decay is purely cosmetic for API consumers reading growth data.

- **Metadata wrapped in `data` key**: When creating or updating seed metadata, the user-provided metadata object is nested under a `data` key in the stored model (`seed.Metadata["data"] = body.Metadata`). Consumers must be aware of this wrapping.

- **Phase computation sorts phases on every call**: `ComputePhaseInfo` calls `phases.OrderBy(p => p.MinTotalGrowth).ToList()` on every invocation rather than requiring pre-sorted phase definitions. This is safe but wasteful for frequent calls.

### Design Considerations (Requires Planning)

- **No cleanup of associated data on archive**: `ArchiveSeedAsync` sets the seed's status to `Archived` but does not clean up growth data (`growth:{seedId}`), capability cache (`cap:{seedId}`), or bond data (`bond:{bondId}`). Archived seeds retain all associated state indefinitely. A cleanup strategy is needed -- either immediate deletion, a background retention worker, or integration with lib-resource for compression.

- **Bond shared growth applied regardless of partner activity**: When a bonded seed records growth, the `BondSharedGrowthMultiplier` is applied unconditionally if the bond exists and is active. The partner seed does not need to be simultaneously active or growing. This means a bonded seed always gets boosted growth even if the partner is dormant or archived. Whether this is the intended semantic needs clarification.

- **UpdateSeedType allows changing phases for existing seeds**: `UpdateSeedTypeAsync` accepts new `GrowthPhases` definitions without checking whether existing seeds would be affected by threshold changes. A seed that was in phase "Mature" at threshold 50 could suddenly be reclassified to an earlier phase if thresholds are raised. No migration or recomputation of existing seed phases is triggered.

- **No GameServiceId cross-validation**: `IGameServiceClient` is injected but never called. Seeds can be created referencing any GUID as a game service ID, including non-existent ones. Should validate via `IGameServiceClient.GetGameServiceAsync()` during `CreateSeedAsync` and `RegisterSeedTypeAsync`.

---

## Work Tracking

No active work items.
