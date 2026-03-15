# Seed Plugin Deep Dive

> **Plugin**: lib-seed
> **Schema**: schemas/seed-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFoundation
> **State Store**: seed-statestore (MySQL), seed-growth-statestore (MySQL), seed-type-definitions-statestore (MySQL), seed-bonds-statestore (MySQL), seed-capabilities-cache (Redis), seed-lock (Redis)
> **Implementation Map**: [docs/maps/SEED.md](../maps/SEED.md)
> **Short**: Generic progressive growth primitive with polymorphic ownership and phase-gated capabilities

---

## Overview

Generic progressive growth primitive (L2 GameFoundation) for game entities. Seeds start empty and grow by accumulating metadata across named domains, progressively gaining capabilities at configurable thresholds. Seeds are polymorphically owned (accounts, actors, realms, characters, relationships) and agnostic to what they represent -- guardian spirits, dungeon cores, combat archetypes, crafting specializations, and governance roles are all equally valid seed types. Seed types are string codes (not enums), allowing new types without schema changes. Each seed type defines its own growth phase labels, capability computation rules, and bond semantics. Consumers register seed types via API, contribute growth via the record API or DI provider listeners (e.g., Collection→Seed pipeline), and query capability manifests to gate actions.

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

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-actor (L2) | Actor discovers `SeedProviderFactory` via `IEnumerable<IVariableProviderFactory>` DI injection; creates `SeedProvider` instances per character for ABML behavior execution (`${seed.*}` variables) |
| lib-collection (L2) | Collection dispatches entry unlock notifications to `SeedCollectionUnlockListener` via `ICollectionUnlockListener` DI interface; listener matches entry tags against seed type `collectionGrowthMappings` to drive growth |
| lib-gardener (L4) | Creates `guardian` seeds for player accounts, contributes growth via `ISeedClient`, queries capability manifests for UX module gating, manages seed bonds as the pair system; implements `ISeedEvolutionListener` for phase change notifications |
| lib-faction (L4) | Creates faction seeds, contributes growth via `ISeedClient`, implements `ISeedEvolutionListener` for faction capability progression |
| lib-status (L4) | Calls `ISeedClient` for seed-derived capability queries; implements `ISeedEvolutionListener` for capability change notifications |
| lib-divine (L4, planned) | Will create deity domain power seeds, contribute growth via `ISeedClient`, and tie divinity generation to domain seed depth. Currently fully stubbed. |
| lib-agency (L4, planned) | Will read guardian spirit seed capability depths to compute UX fidelity manifests (progressive agency). Pre-implementation, no schema exists yet. |
| Dungeon plugin (planned, L4) | Will create `dungeon_core` and `dungeon_master` seeds for actor/character entities |

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

- **Cross-seed-type growth transfer matrix** ([#354](https://github.com/beyond-immersion/bannou-service/issues/354)): When an entity holds seeds of different types (e.g., `guardian` + `dungeon_master`), experience in one role could partially feed growth in the other via configurable `SeedGrowthTransferRule` mappings with domain-to-domain multipliers. Distinct from same-type cross-pollination (`SameOwnerGrowthMultiplier`, already implemented). Not blocking initial implementation -- add when gameplay testing validates which cross-type relationships feel right.
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/354 -->

- **Seed owner type promotion / re-parenting** ([#437](https://github.com/beyond-immersion/bannou-service/issues/437)): Mechanism to atomically change a seed's `ownerType` and `ownerId` (e.g., character-owned → account-owned) while preserving all growth data. Required for dungeon Pattern A (full split mastery) where the `dungeon_master` seed promotes from character to account ownership. Depends on #436 (household split mechanic). Recommended approach: direct `seed/reparent` endpoint with per-owner limit validation against the new owner.
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/437 -->

- **Client events for guardian spirit progression** ([#497](https://github.com/beyond-immersion/bannou-service/issues/497)): Push `SeedPhaseChanged`, `SeedCapabilityChanged`, `SeedGrowthUpdated`, `SeedBondFormed`, and `SeedActivated` client events via `IClientEventPublisher` using the Entity Session Registry (#426). Gardener registers `seed → session` bindings. The Entity Session Registry resolves the previous concern about "introducing session/WebSocket awareness into a foundational service" — Seed only needs `IEntitySessionRegistry` (L1 hard dependency) and `IClientEventPublisher` (L1), not session management knowledge. Supersedes the previous "L4 consumer responsibility" framing from [#365](https://github.com/beyond-immersion/bannou-service/issues/365). Also includes investigation items: verify whether `seed.updated` is emitted for the deactivated seed during `ActivateSeedAsync`, and consider adding `seed.bond.initiated` for pending bond state.
<!-- AUDIT:NEEDS_DESIGN:2026-02-26:https://github.com/beyond-immersion/bannou-service/issues/497 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. **SeedType deprecation is wrong category (Foundation Tenets — T31)**: SeedType is currently implemented as Category A (`IDeprecateAndMergeEntity`) but T31's decision tree classifies it as Category B (`ICleanDeprecatedEntity`) — seed instances persist with the type's code, and merge is fundamentally impractical due to incompatible growth domains, phase definitions, capability rules, and bond constraints. The current implementation has: `undeprecate` endpoint (forbidden for Category B), per-entity `delete` endpoint (forbidden for Category B), missing `clean-deprecated` sweep endpoint (required for Category B), missing `instanceEntity` in `x-lifecycle`, and wrong marker interface. See [#645](https://github.com/beyond-immersion/bannou-service/issues/645) for the reclassification plan. Supersedes [#374](https://github.com/beyond-immersion/bannou-service/issues/374) (merge — impractical).
<!-- AUDIT:NEEDS_DESIGN:2026-03-14:https://github.com/beyond-immersion/bannou-service/issues/645 -->

2. **Seed archive is a soft-delete pattern (Foundation Tenets — T28)**: `ArchiveSeedAsync` sets `Status = Archived` but retains the seed record, growth data, capability cache, and bond data indefinitely. This is a soft-delete pattern — T28 explicitly forbids retaining records with a status flag for non-Account entities. Seeds are instance entities (not definitions), so the correct pattern is immediate hard delete. The `seed.deleted` lifecycle event is declared via `x-lifecycle` but is never published because no hard-delete path exists. Fix requires: adding a hard-delete endpoint (or repurposing archive to hard-delete), cleaning up associated growth/capability/bond data on deletion, and publishing the `seed.deleted` event. Growth data preservation for narrative purposes should use lib-resource compression callbacks before deletion, not indefinite retention. See [#366](https://github.com/beyond-immersion/bannou-service/issues/366) for the design discussion (which also covers bond dissolution dependency).
<!-- AUDIT:NEEDS_DESIGN:2026-02-09:https://github.com/beyond-immersion/bannou-service/issues/366 -->

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

- **Decay worker uses real-time, should use game-time**: The decay background worker (`SeedDecayWorkerService`) uses `DateTimeOffset.UtcNow` and real-time `Task.Delay` intervals. In a world with a 24:1 game-time ratio, decay applied per real-time cycle is 24x slower than intended per game-day. Guardian spirits, dungeon cores, faction seeds, and all other seed types evolve in the simulated world's time. When Worldstate (L2) is implemented, the decay worker must call `GetElapsedGameTime` to compute game-days elapsed since last decay cycle, then apply `GrowthDecayRatePerDay` against game-days rather than real-days. The `DecayWorkerIntervalSeconds` config remains the real-time check frequency; the decay amount per cycle is computed from game-time elapsed. See [#545](https://github.com/beyond-immersion/bannou-service/issues/545) for the broader cross-service migration plan (covers both Currency autogain and Seed decay).
<!-- AUDIT:BLOCKED:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/545 -->

- **Realm association for game-time decay**: Seeds will gain a nullable `realmId` field set at creation time, making realm association explicit for game-time decay calculations. When null and `AutoAssociateRealm` config is true (default), realm is inferred from owner type (character → character's realm, realm-owned → the realm itself, actor → bound character's realm). When null and config is false, the seed has no decay. This ensures guardian spirit seeds (account-owned, no realm) naturally avoid decay, while character-owned and realm-owned seeds can be tied to specific realm timelines. A character in Realm A could have a seed explicitly tied to Realm B's time (e.g., a dungeon master bonded to a dungeon in a different realm). See [#545](https://github.com/beyond-immersion/bannou-service/issues/545) for implementation details.
<!-- AUDIT:NEEDS_DESIGN:2026-03-05:https://github.com/beyond-immersion/bannou-service/issues/545 -->

---

## Work Tracking

- [#362](https://github.com/beyond-immersion/bannou-service/issues/362) - Bond dissolution endpoint design (triaged: cancel + dissolve flows, permanent bonds unbreakable)
- [#366](https://github.com/beyond-immersion/bannou-service/issues/366) - Seed archive soft-delete → hard-delete migration (elevated to Bug: T28 violation; depends on #362 for bond dissolution)
- [#645](https://github.com/beyond-immersion/bannou-service/issues/645) - Reclassify SeedType deprecation from Category A to Category B (elevated to Bug: T31 violation; supersedes #374)
- [#354](https://github.com/beyond-immersion/bannou-service/issues/354) - Cross-seed-type growth transfer matrix (creative game design decisions needed, not blocking)
- [#437](https://github.com/beyond-immersion/bannou-service/issues/437) - Seed owner type promotion / re-parenting for dungeon Pattern A (depends on #436 household split)
- [#497](https://github.com/beyond-immersion/bannou-service/issues/497) - Client events for guardian spirit progression via Entity Session Registry (depends on #426)
- [#545](https://github.com/beyond-immersion/bannou-service/issues/545) - Currency/Seed background workers game-time migration via Worldstate (blocked by Worldstate; includes RealmId design for realm association)
