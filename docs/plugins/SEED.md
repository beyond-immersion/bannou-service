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

- **Bond dissolution endpoint** ([#362](https://github.com/beyond-immersion/bannou-service/issues/362)): **Confirmed** (2026-03-19): Two flows needed — Cancel (`POST /seed/bond/cancel`, PendingConfirmation → Cancelled, any participant) and Dissolve (`POST /seed/bond/dissolve`, Active non-permanent → Dissolved, any participant). Permanent bonds cannot be dissolved via API — period. Bond records preserved with terminal lifecycle status (Dissolved/Cancelled are NOT soft-delete — they're terminal states like Contract Completed, with historical SharedGrowth/BondStrength data). Add `Dissolved`, `Cancelled` to `BondStatus` enum. Publish `SeedBondDissolvedEvent`/`SeedBondCancelledEvent`. Add `x-event-subscription` for `contract.completed`/`contract.breached` for auto-dissolution (Seed L2 → Contract L1 = valid). Clear `BondId` on participant seeds on dissolution. Prerequisite for #366 (seed hard-delete).
<!-- AUDIT:CONFIRMED:2026-03-19:https://github.com/beyond-immersion/bannou-service/issues/362 -->

- **Bond growth propagation** ([Divinity Generation Architecture](../planning/DIVINITY-GENERATION-ARCHITECTURE.md), [#413](https://github.com/beyond-immersion/bannou-service/issues/413)): **Confirmed** (2026-03-19): Extend Seed bonds with `PropagationDirection` (None/AToB/BToA/Bidirectional) and `PropagationRatio` (0.0-1.0) so that growth on one bonded seed propagates a ratio to the partner. Anti-cascade via `propagated: true` flag (propagated growth does not re-propagate). `Mirrored` removed from v1 — `Bidirectional` with ratio 1.0 covers all cases. **Performance at 100K scale**: always batch propagation via a `SeedBondPropagationWorker` (accumulate credits in `ConcurrentDictionary`, flush on configurable interval) — eliminates hot-lock problem on god seeds with many followers. **Missing seeds at bond time**: with caller-specified GUIDs and GetOrCreate semantics on CreateSeed (per RESOURCE-TRANSACTIONS.md), Divine creates-or-gets seeds deterministically at bond time — fully synchronous, idempotent, safe for distributed/sharded scenarios, no event subscription needed. **Patron change cost**: gameplay decision authored in deity ABML behavior, not service code. Prerequisites: #362 (bond dissolution for patron changes).
<!-- AUDIT:CONFIRMED:2026-03-19:https://github.com/beyond-immersion/bannou-service/issues/413 -->

- **Cross-seed-type growth transfer matrix** ([#354](https://github.com/beyond-immersion/bannou-service/issues/354)): When an entity holds seeds of different types (e.g., `guardian` + `dungeon_master`), experience in one role could partially feed growth in the other via configurable `SeedGrowthTransferRule` mappings with domain-to-domain multipliers. Distinct from same-type cross-pollination (`SameOwnerGrowthMultiplier`, already implemented). Not blocking initial implementation -- add when gameplay testing validates which cross-type relationships feel right.
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/354 -->

- ~~**Seed owner type promotion / re-parenting**~~ ([#437](https://github.com/beyond-immersion/bannou-service/issues/437)): **IMPLEMENTED** (2026-03-19): Direct `POST /seed/reparent` endpoint that atomically changes `ownerType` and `ownerId` while preserving all growth data. Validates: new ownerType in AllowedOwnerTypes, per-owner limit not exceeded, seed Active or Dormant. Publishes `seed.updated` with `changedFields: ["ownerId", "ownerType"]`, invalidates capability cache. Bonds remain valid (ownership change, not bond relationship). RealmId remains unchanged (fixed-at-creation per Task 3). Reverse index unaffected (keyed by type, not owner).
<!-- AUDIT:IMPLEMENTED:2026-03-19:https://github.com/beyond-immersion/bannou-service/issues/437 -->

- **Client events for guardian spirit progression** ([#497](https://github.com/beyond-immersion/bannou-service/issues/497)): **Confirmed** (2026-03-19): 6 client events via `IClientEventPublisher` + Entity Session Registry: `SeedPhaseChangedClientEvent` (`seed.phase-changed`), `SeedCapabilityChangedClientEvent` (`seed.capability-changed`), `SeedGrowthUpdatedClientEvent` (`seed.growth-updated`), `SeedBondFormedClientEvent` (`seed.bond.formed`), `SeedBondInitiatedClientEvent` (`seed.bond.initiated`), `SeedActivatedClientEvent` (`seed.activated`). Names follow T16: `ClientEvent` suffix, Pattern A for non-endpoint-group entities, Pattern C for bond (endpoint group). Add `seed.bond.initiated` as both service + client event (target participant needs push notification for confirmation flow). Growth events use interval-based gating via `EventBatcher` pattern (accumulate, push at most once per configurable interval — smooth UX, consistent with established pattern). Investigation resolved: `seed.updated` with `changedFields: ["status"]` IS already emitted for deactivated siblings during `ActivateSeedAsync` — no new service event needed; optional `SeedDeactivatedClientEvent` for client convenience. Entity Session Registry (#426) is implemented — unblocked. Supersedes #365.
<!-- AUDIT:CONFIRMED:2026-03-19:https://github.com/beyond-immersion/bannou-service/issues/497 -->

- **Seed → Collection reverse pipeline** ([#700](https://github.com/beyond-immersion/bannou-service/issues/700)): **Confirmed** (2026-03-19): Add `capabilityCollectionGrants` array to seed type definitions mapping capability codes to collection type + entry code + tags. During `RecordGrowthInternal`, after growth is recorded, perform lightweight threshold comparison (same pattern as phase detection — O(grants × 1), no full manifest recompute) for seed types with non-empty `capabilityCollectionGrants`. When a capability rule's domain depth crosses its threshold, call `ICollectionClient.GrantEntryAsync` inline. `GrantEntryAsync` is idempotent — safe for multi-node, retries, and decay-regression-then-re-progression (collection entries are permanent unlocks regardless of subsequent decay). The granted entry's tags flow through the existing `SeedCollectionUnlockListener` on all other seeds owned by the same entity, completing the cross-seed pollination feedback loop. Seed and Collection are both L2 (guaranteed co-located). No new DI listeners, no background workers.
<!-- AUDIT:CONFIRMED:2026-03-19:https://github.com/beyond-immersion/bannou-service/issues/700 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. ~~**SeedType deprecation is wrong category (Foundation Tenets — T31)**~~: **FIXED** (2026-03-19) - Reclassified SeedType from Category A (`IDeprecateAndMergeEntity`) to Category B (`ICleanDeprecatedEntity`). Removed `undeprecate` and per-entity `delete` endpoints. Added `POST /seed/type/clean-deprecated` endpoint using `DeprecationCleanupHelper` with shared `CleanDeprecatedRequest`/`CleanDeprecatedStringKeyResponse` models. Added `instanceEntity: Seed` to SeedType `x-lifecycle`. Added type→seed reverse index (`type-seeds:{gameServiceId}:{seedTypeCode}`) maintained on seed creation. The clean-deprecated sweep checks ALL seeds (including archived) — will become fully functional when seed hard-delete (#366) removes archived seeds from the index. See [#645](https://github.com/beyond-immersion/bannou-service/issues/645).

2. **Seed archive is a soft-delete pattern (Foundation Tenets — T28)**: `ArchiveSeedAsync` sets `Status = Archived` but retains the seed record, growth data, capability cache, and bond data indefinitely. This is a soft-delete pattern — T28 explicitly forbids retaining records with a status flag for non-Account entities. Seeds are instance entities (not definitions), so the correct pattern is immediate hard delete. **Confirmed** (2026-03-19): Remove `Archived` from `SeedStatus` enum (leaving Active, Dormant). Replace `POST /seed/archive` with `POST /seed/delete` that hard-deletes seed record + growth + capability cache + bond participation. Bond dissolution (#362) is a prerequisite: auto-dissolve non-permanent bonds, reject permanent bonds (exception: account deletion cleanup force-dissolves permanent bonds). Publish `seed.deleted` lifecycle event. Remove `SeedArchivedEvent`. No retention worker (T28 forbids it). Character compression pipeline handles narrative data preservation separately. Related gap: Seed should register x-compression-callback for character archives (separate enhancement). See [#366](https://github.com/beyond-immersion/bannou-service/issues/366) and [#362](https://github.com/beyond-immersion/bannou-service/issues/362).
<!-- AUDIT:CONFIRMED:2026-03-19:https://github.com/beyond-immersion/bannou-service/issues/366 -->

3. ~~**Missing TransferGrowth endpoint for household split**~~: **FIXED** (2026-03-16) - Added `POST /seed/growth/transfer` endpoint with `TransferGrowthRequest`/`TransferGrowthResponse` models. Transfers proportional growth across all domains from source to target seed. Uses ordered dual-lock (smaller GUID first) to prevent deadlocks. Idempotent via `transferReferenceId` stored in Redis `seed-idempotency` store with configurable TTL. Publishes `seed.growth.transferred` event, per-domain `seed.growth.updated` events for the target, and `seed.phase.changed` events for either seed if phase transitions occur. Dispatches `ISeedEvolutionListener` callbacks for both seeds.

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

- ~~**Decay worker uses real-time, should use game-time**~~ ([#545](https://github.com/beyond-immersion/bannou-service/issues/545), resolved 2026-03-19): Design resolved. Add `DecayTimeSource` config property (`$ref: TimeSource` from `common-api.yaml`, default: `GameTime`). `TimeSource` enum is shared across Currency autogain, Seed decay, and Character-Encounter memory decay. Seeds with resolved `realmId` call `GetElapsedGameTime`; seeds without realmId are skipped by the decay worker (no decay). `DecayWorkerIntervalSeconds` remains the real-time check frequency; the decay amount per cycle is computed from game-time elapsed. Data transition: accept the discontinuity (pre-release).
<!-- AUDIT:RESOLVED:2026-03-19:https://github.com/beyond-immersion/bannou-service/issues/545 -->

- **Realm association for game-time decay**: **Confirmed** (2026-03-19): Seeds will gain a nullable `realmId` field on `SeedModel`, set at creation time and **fixed** (does not auto-follow owner realm changes). `AutoAssociateRealm` config (default `true`): infer realm from owner type on creation (character → character's realm, realm-owned → the realm itself, actor → bound character's realm, account → null). Seeds with null realmId after auto-association are skipped by the decay worker entirely — guardian spirit seeds (account-owned) naturally avoid decay. Fixed-at-creation is correct for v1: dungeon master seeds explicitly need fixed binding to the dungeon's realm, character realm migration is rare, and `POST /seed/update` can patch realmId if needed during migration callbacks. See [#545](https://github.com/beyond-immersion/bannou-service/issues/545) for implementation details.
<!-- AUDIT:CONFIRMED:2026-03-19:https://github.com/beyond-immersion/bannou-service/issues/545 -->

---

## Work Tracking

- [#362](https://github.com/beyond-immersion/bannou-service/issues/362) - Bond dissolution endpoint design (confirmed 2026-03-19: cancel + dissolve flows, contract event auto-dissolution, permanent bonds unbreakable; ready for implementation)
- [#366](https://github.com/beyond-immersion/bannou-service/issues/366) - Seed archive → hard-delete migration (confirmed 2026-03-19: remove Archived status, replace archive with hard-delete, no retention worker; depends on #362; ready for implementation)
- ~~[#645](https://github.com/beyond-immersion/bannou-service/issues/645)~~ - Reclassify SeedType deprecation from Category A to Category B (completed 2026-03-19: schema + code + map + tests updated)
- [#354](https://github.com/beyond-immersion/bannou-service/issues/354) - Cross-seed-type growth transfer matrix (creative game design decisions needed, not blocking)
- ~~[#437](https://github.com/beyond-immersion/bannou-service/issues/437)~~ - Seed owner type promotion / re-parenting (implemented 2026-03-19: POST /seed/reparent endpoint with AllowedOwnerTypes + per-owner limit validation)
- [#497](https://github.com/beyond-immersion/bannou-service/issues/497) - Client events for guardian spirit progression (confirmed 2026-03-19: 6 client events, interval-gated growth, seed.bond.initiated added, seed.dormant gap resolved; unblocked)
- [#545](https://github.com/beyond-immersion/bannou-service/issues/545) - Currency/Seed background workers game-time migration via Worldstate (blocked by Worldstate; RealmId design confirmed 2026-03-19: fixed-at-creation, AutoAssociateRealm config, null = no decay)
- [#413](https://github.com/beyond-immersion/bannou-service/issues/413) - Bond growth propagation for Divine/Faction (confirmed 2026-03-19: PropagationDirection/PropagationRatio, batched worker, GetOrCreate for missing seeds; depends on #362)
- [#700](https://github.com/beyond-immersion/bannou-service/issues/700) - Seed → Collection reverse pipeline (confirmed 2026-03-19: capabilityCollectionGrants on type defs, lightweight threshold check during growth recording, GrantEntryAsync inline; ready for implementation)
- ~~[#374](https://github.com/beyond-immersion/bannou-service/issues/374)~~ - Seed type merge endpoint (closed 2026-03-19: superseded by #645 — Category B reclassification eliminates merge)
- ~~[#671](https://github.com/beyond-immersion/bannou-service/issues/671)~~ - TransferGrowth endpoint for household split (completed 2026-03-16)
