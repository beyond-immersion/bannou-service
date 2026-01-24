# Claude Implementation Failures

> **Generated**: 2026-01-23
> **Last Updated**: 2026-01-24
> **Source**: Exhaustive codebase scan of all plugins
> **Purpose**: Track and fix systematic tenet violations introduced during AI-assisted development

---

## Remaining Work

| Category | Remaining | Original |
|----------|-----------|----------|
| [T21: Unused Configuration Properties](#t21-unused-config) | 8 services | ~20 services |
| [T21: Hardcoded Tunables](#t21-hardcoded-tunables) | 15 violations | 51 claimed |
| [T9: Non-Atomic Operations](#t9-non-atomic) | 5 violations | 36 claimed |

---

## Completed (Summary)

### T25: Enum-as-String — COMPLETE (9/9)

All internal POCOs now use proper enum types instead of strings. Fixed across lib-contract (5 fields), lib-currency (2 fields), lib-actor (1 field), lib-orchestrator (1 field).

### T25: GUID-as-String — COMPLETE (11/11 services)

All internal POCOs now use `Guid` instead of `string` for entity ID fields. Fixed across lib-account, lib-character, lib-location, lib-connect, lib-auth, lib-voice, lib-matchmaking, lib-game-session, lib-character-encounter, lib-asset. lib-documentation and lib-behavior were already compliant.

### T21: Unused Cache Stores — COMPLETE (5/5)

All schema-defined cache stores are now wired up:
- `CurrencyBalanceCache` — Cache read-through/write-through for balance operations
- `CurrencyHoldsCache` — Cache read-through/write-through for hold operations
- `MusicCompositions` — Composition caching for deterministic requests
- `InventoryContainerCache` — Cache read-through/write-through for container operations
- `InventoryLock` — Distributed locking for AddItem, RemoveItem, UpdateContainer

Additionally, the SaveLoad configurable store names violation is resolved — the service now uses `StateStoreDefinitions.SaveLoadCache` and `StateStoreDefinitions.SaveLoadPending` directly instead of configuration-driven store names.

### T9: Non-Atomic Operations — 12/13 services COMPLETE

All services except lib-documentation now use either optimistic concurrency (GetWithETagAsync/TrySaveAsync) or distributed locks for read-modify-write operations:
- lib-inventory — Distributed locks via `_lockProvider.LockAsync()`
- lib-contract — GetWithETagAsync/TrySaveAsync throughout (16+ patterns)
- lib-currency — GetWithETagAsync/TrySaveAsync for balance/hold operations
- lib-save-load — GetWithETagAsync/TrySaveAsync + distributed locks for rename
- lib-matchmaking — GetWithETagAsync/TrySaveAsync for queue state
- lib-actor — GetWithETagAsync/TrySaveAsync for template index operations
- lib-leaderboard — GetWithETagAsync/TrySaveAsync for definitions and seasons
- lib-game-session — Retry loop with GetWithETagAsync/TrySaveAsync for subscriber sessions
- lib-character-encounter — GetWithETagAsync/TrySaveAsync for perspectives and memory
- lib-achievement — GetWithETagAsync/TrySaveAsync for progress and definitions
- lib-asset — GetWithETagAsync/TrySaveAsync for bundle and index operations
- lib-scene — GetWithETagAsync/TrySaveAsync for checkout/commit/discard

### T21: Unused Configuration — 12/20 services COMPLETE

The following services now reference `_configuration` in their implementations:
- lib-currency (TransactionRetentionDays, HoldMaxDurationDays, IdempotencyTtlSeconds, DefaultAllowNegative)
- lib-analytics (Glicko2DefaultRating, Glicko2DefaultDeviation, Glicko2DefaultVolatility)
- lib-achievement (ProgressTtlSeconds, AutoSyncOnUnlock, RareThresholdPercent)
- lib-leaderboard (ScoreUpdateBatchSize, MaxEntriesPerQuery)
- lib-matchmaking (ServerSalt, ProcessingIntervalSeconds, DefaultMaxIntervals, DefaultMatchAcceptTimeoutSeconds, and 6 more)
- lib-music (CompositionCacheTtlSeconds)
- lib-game-session (DefaultSessionTimeoutSeconds, DefaultReservationTtlSeconds, MaxPlayersPerSession)
- lib-contract (MaxMilestonesPerTemplate, MaxPreboundApisPerMilestone)
- lib-documentation (MaxSearchResults, MinRelevanceScore, SearchCacheTtlSeconds)
- lib-mesh (EndpointTtlSeconds, HeartbeatIntervalSeconds)
- lib-relationship-type (SeedPageSize)
- lib-species (SeedPageSize)

---

## T21: Unused Configuration Properties {#t21-unused-config}

**Severity**: Medium — Dead config pollutes schema and misleads operators
**Fix complexity**: Low-Medium — Wire up in service or remove from config schema

### Services with ZERO `_configuration` references (8 remaining):

| # | Service | Status |
|---|---------|--------|
| 1 | lib-game-service | No references to `_configuration` |
| 2 | lib-realm | No references to `_configuration` |
| 3 | lib-realm-history | No references to `_configuration` |
| 4 | lib-relationship | No references to `_configuration` |
| 5 | lib-character-history | No references to `_configuration` |
| 6 | lib-character-personality | No references to `_configuration` |
| 7 | lib-location | No references to `_configuration` |
| 8 | lib-website | No references to `_configuration` |

**Action**: For each service, either wire up configuration properties in the service implementation or remove unused properties from the configuration schema.

---

## T21: Hardcoded Tunables {#t21-hardcoded-tunables}

**Severity**: Medium — Prevents runtime tuning, forces redeploy for config changes
**Fix complexity**: Medium — Add config property to schema, regenerate, wire up

### Hardcoded Retry Counts (4 violations)

| # | File | Method | Value | Notes |
|---|------|--------|-------|-------|
| 1 | lib-asset/AssetService.cs:2470 | `AddToIndexWithOptimisticConcurrencyAsync` | `maxRetries = 5` | |
| 2 | lib-asset/AssetService.cs:2543 | `RemoveFromIndexWithOptimisticConcurrencyAsync` | `maxRetries = 5` | |
| 3 | lib-game-session/GameSessionService.cs:1730 | `StoreSubscriberSessionAsync` | `attempt < 3` | |
| 4 | lib-game-session/GameSessionService.cs:1767 | `RemoveSubscriberSessionAsync` | `attempt < 3` | |

### Hardcoded Background Service Delays (7 violations)

| # | File | Value | Config Property Exists? |
|---|------|-------|------------------------|
| 1 | lib-achievement/RarityCalculationService.cs:56 | `TimeSpan.FromSeconds(30)` | YES — should use config |
| 2 | lib-asset/AssetService.cs:2509 | `TimeSpan.FromMilliseconds(10 * (attempt + 1))` | NO — needs config property |
| 3 | lib-asset/AssetService.cs:2573 | `TimeSpan.FromMilliseconds(10 * (attempt + 1))` | NO — needs config property |
| 4 | lib-currency/Services/CurrencyAutogainTaskService.cs:63 | `TimeSpan.FromSeconds(15)` | YES — should use config |
| 5 | lib-documentation/Services/SearchIndexRebuildService.cs:49 | `TimeSpan.FromSeconds(5)` | NO — needs config property |
| 6 | lib-mesh/Services/MeshHealthCheckService.cs:69 | `TimeSpan.FromSeconds(10)` | YES — should use config |
| 7 | lib-actor/Pool/PoolHealthMonitor.cs:78 | `TimeSpan.FromSeconds(5)` | YES — should use config |

### Hardcoded Static TTL Fields (4 violations)

| # | File | Value | Purpose |
|---|------|-------|---------|
| 1 | lib-orchestrator/OrchestratorStateManager.cs:37 | `TimeSpan.FromSeconds(90)` | Heartbeat TTL |
| 2 | lib-orchestrator/OrchestratorStateManager.cs:38 | `TimeSpan.FromMinutes(5)` | Routing TTL |
| 3 | lib-orchestrator/OrchestratorStateManager.cs:39 | `TimeSpan.FromDays(30)` | Config history TTL |
| 4 | lib-contract/ContractServiceClauseValidation.cs:36 | `TimeSpan.FromSeconds(15)` | Cache staleness threshold |

---

## T9: Non-Atomic Operations {#t9-non-atomic}

**Severity**: Critical — Data corruption under concurrency (lost updates)
**Fix complexity**: High — Requires ETag-based optimistic concurrency or distributed locks

### Remaining: lib-documentation (5 violations)

All violations are in index helper methods that manage namespace and trashcan document lists:

| # | Method | Lines | Store | Risk |
|---|--------|-------|-------|------|
| 1 | `AddDocumentToNamespaceIndexAsync` | 1704-1723 | `List<Guid>` (namespace docs) | Multiple concurrent document creations lose entries |
| 2 | `RemoveDocumentFromNamespaceIndexAsync` | 1728-1738 | `List<Guid>` (namespace docs) | Race condition on removal |
| 3 | `AddDocumentToTrashcanIndexAsync` | 1743-1754 | `List<Guid>` (trashcan) | Lost updates on concurrent delete |
| 4 | `RemoveDocumentFromTrashcanIndexAsync` | 1759-1774 | `List<Guid>` (trashcan) | Race condition on trashcan removal |
| 5 | `PurgeTrashcanAsync` | 1570-1620 | `List<Guid>` (trashcan) | Lost entries if purge runs concurrently with delete |

**Fix pattern**: Replace `GetAsync`/`SaveAsync` with `GetWithETagAsync`/`TrySaveAsync` and add retry logic (these are internal helpers, not API endpoints).

---
