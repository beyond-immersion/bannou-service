# Claude Implementation Failures

> **Generated**: 2026-01-23
> **Source**: Exhaustive codebase scan of all plugins
> **Purpose**: Track and fix systematic tenet violations introduced during AI-assisted development

---

## Priority Order (Simplest → Most Complex)

1. [T25: Enum-as-String](#t25-enum-as-string) — 9 violations
2. [T25: GUID-as-String](#t25-guid-as-string) — 25 violations
3. [T21: Unused Cache Stores](#t21-unused-cache-stores) — 5 violations
4. [T21: Unused Configuration Properties](#t21-unused-config) — ~20 services
5. [T21: Hardcoded Tunables](#t21-hardcoded-tunables) — 51 violations
6. [T9: Non-Atomic Operations](#t9-non-atomic) — 36 violations

**Total: ~148 violations across ~25 services**

---

## T25: Enum-as-String {#t25-enum-as-string}

**Severity**: Medium — Type safety gap, forces string comparison in business logic
**Fix complexity**: Trivial — Change `string` to enum type, fix assignments

| # | Service | File | Field | Should Be |
|---|---------|------|-------|-----------|
| 1 | lib-contract | ContractService.cs | `ContractInstanceModel.Status` | `ContractStatus` |
| 2 | lib-contract | ContractService.cs | `ContractPartyModel.ConsentStatus` | `ConsentStatus` |
| 3 | lib-contract | ContractService.cs | `MilestoneInstanceModel.Status` | `MilestoneStatus` |
| 4 | lib-contract | ContractService.cs | `PreboundApiModel.ExecutionMode` | `ExecutionMode` |
| 5 | lib-contract | ContractService.cs | `BreachModel.Status` | `BreachStatus` |
| 6 | lib-currency | CurrencyService.cs | `WalletModel.Status` | `WalletStatus` |
| 7 | lib-currency | CurrencyService.cs | `HoldModel.Status` | `HoldStatus` |
| 8 | lib-actor | ActorService.cs | `ActorAssignment.Status` | `ActorStatus` |
| 9 | lib-orchestrator | OrchestratorService.cs | `ProcessorInstance.Status` | `ProcessorStatus` |

**Fix pattern**:
```csharp
// WRONG:
public string Status { get; set; } = "Active";

// CORRECT:
public ContractStatus Status { get; set; } = ContractStatus.Active;
```

---

## T25: GUID-as-String {#t25-guid-as-string}

**Severity**: Medium — Prevents type-safe ID comparisons, wastes memory
**Fix complexity**: Trivial — Change `string` to `Guid`, fix assignments

Affected services (internal POCOs store entity IDs as `string` instead of `Guid`):

| # | Service | Approximate Violations |
|---|---------|----------------------|
| 1 | lib-account | 2-3 (AccountModel ID fields) |
| 2 | lib-character | 2-3 (CharacterModel ID fields) |
| 3 | lib-location | 2-3 (LocationModel ID, ParentId, RealmId) |
| 4 | lib-character-encounter | 3-4 (EncounterModel character IDs, location ID) |
| 5 | lib-connect | 2 (session AccountId, ConnectionId) |
| 6 | lib-auth | 2 (session AccountId) |
| 7 | lib-voice | 2 (room/peer IDs) |
| 8 | lib-matchmaking | 2-3 (ticket/queue IDs) |
| 9 | lib-asset | 2 (asset owner, realm IDs) |
| 10 | lib-documentation | 1 (document owner) |
| 11 | lib-behavior | 1 (behavior owner) |

**Fix pattern**:
```csharp
// WRONG:
public string AccountId { get; set; } = string.Empty;
public string RealmId { get; set; } = string.Empty;

// CORRECT:
public Guid AccountId { get; set; }
public Guid RealmId { get; set; }
```

---

## T21: Unused Cache Stores {#t21-unused-cache-stores}

**Severity**: High — Defined infrastructure that was never wired up (dead schema)
**Fix complexity**: Low-Medium — Either implement cache read-through or remove from schema

### Verified Unused Stores (5 total):

| # | Store Constant | Service | Backend | Purpose | Verdict |
|---|---------------|---------|---------|---------|---------|
| 1 | `CurrencyBalanceCache` | lib-currency | Redis | Real-time balance lookups | **IMPLEMENT** |
| 2 | `CurrencyHoldsCache` | lib-currency | Redis | Authorization hold state | **IMPLEMENT** |
| 3 | `MusicCompositions` | lib-music | Redis | Cached generated compositions | **IMPLEMENT** |
| 4 | `InventoryContainerCache` | lib-inventory | Redis | Container state cache | **IMPLEMENT** |
| 5 | `InventoryLock` | lib-inventory | Redis | Distributed locks | **IMPLEMENT** |

### Additional Violation: SaveLoad Uses Configuration-Driven Store Names

`SaveLoadCache` and `SaveLoadPending` — These ARE being referenced, but through a **BAD PATTERN**:
- `_configuration.HotCacheStoreName` defaults to `"save-load-cache"`
- `_configuration.PendingUploadStoreName` defaults to `"save-load-pending"`

**This is WRONG.** Configurable store names are a BAD pattern because:
1. StateStoreDefinitions exists precisely to provide predictable, generated, consistent store names
2. Configuration-driven names break predictability and discoverability
3. They prevent store names from being shared/known across services
4. They create a vector for misconfiguration that corrupts data

**Action**: Remove the configuration properties and use `StateStoreDefinitions.SaveLoadCache` and `StateStoreDefinitions.SaveLoadPending` directly.

---

### Detailed Analysis Per Store:

#### 1. CurrencyBalanceCache — MISSING CACHE LAYER

**Schema definition** (`schemas/state-stores.yaml:410-414`):
```yaml
currency-balance-cache:
  backend: redis
  prefix: "currency:balance"
  service: Currency
  purpose: Real-time balance lookups (cached, refreshed on access)
```

**Current implementation**: CurrencyService uses `StateStoreDefinitions.CurrencyBalances` (MySQL) directly for all balance operations. Every balance lookup hits the database.

**Why this matters**: Balance lookups are HIGH FREQUENCY operations (every transaction, every UI refresh). MySQL round-trips add latency. The Redis cache was designed as a read-through cache to accelerate balance reads.

**Action**: Implement cache read-through pattern:
1. On read: Check Redis cache → miss → read from MySQL → populate cache → return
2. On write: Update MySQL → invalidate/update cache
3. TTL: Short (60-300 seconds) since balances change frequently

---

#### 2. CurrencyHoldsCache — MISSING CACHE LAYER

**Schema definition** (`schemas/state-stores.yaml:422-426`):
```yaml
currency-holds-cache:
  backend: redis
  prefix: "currency:hold"
  service: Currency
  purpose: Authorization hold state for pre-auth scenarios
```

**Current implementation**: CurrencyService uses `StateStoreDefinitions.CurrencyHolds` (MySQL) directly.

**Why this matters**: Authorization holds are checked before every debit to ensure the hold is valid. Pre-auth scenarios (hold → capture) require fast lookups.

**Action**: Implement cache read-through similar to balance cache.

---

#### 3. MusicCompositions — INCOMPLETE SERVICE

**Schema definition** (`schemas/state-stores.yaml:390-394`):
```yaml
music-compositions:
  backend: redis
  prefix: "music:comp"
  service: Music
  purpose: Cached generated compositions
```

**Current implementation**: MusicService generates compositions on every request. No caching. The `CreateStyleAsync` method even has a comment: "// For now, return the style definition as if it was created // In production, this would persist to state store"

**Why this matters**: Music generation is COMPUTE INTENSIVE. Same request with same seed should return cached result, not regenerate.

**Action**: Implement composition caching:
1. Generate cache key from request parameters (styleId, seed, durationBars, mood, key, tempo)
2. Check cache before generation
3. Store generated composition with TTL (compositions with explicit seed can be long-lived)

---

#### 4. InventoryContainerCache — MISSING CACHE LAYER

**Schema definition** (`schemas/state-stores.yaml:483-488`):
```yaml
inventory-container-cache:
  backend: redis
  prefix: "inv:cont"
  service: Inventory
  purpose: Container state and item list cache
```

**Current implementation**: InventoryService uses `StateStoreDefinitions.InventoryContainerStore` (MySQL) directly.

**Why this matters**: Container lookups happen on every inventory access. Caching container state reduces database load for hot containers (player inventories, shop inventories).

**Action**: Implement cache read-through for container state.

---

#### 5. InventoryLock — MISSING CONCURRENCY PROTECTION

**Schema definition** (`schemas/state-stores.yaml:494-498`):
```yaml
inventory-lock:
  backend: redis
  prefix: "inv:lock"
  service: Inventory
  purpose: Distributed locks for concurrent modifications
```

**Current implementation**: InventoryService has NO distributed locking. Container modifications use non-atomic read-modify-write.

**Why this matters**: This is a T9 violation PLUS a missing infrastructure store. Without distributed locks, concurrent item moves can corrupt inventory state.

**Action**: Implement distributed locking for container modifications using `IDistributedLockProvider`.

---

## T21: Unused Configuration Properties {#t21-unused-config}

**Severity**: Medium — Dead config pollutes schema and misleads operators
**Fix complexity**: Low-Medium — Wire up in service or remove from config schema

### Services with ZERO `_configuration` references:

| # | Service | Properties Defined | Status |
|---|---------|-------------------|--------|
| 1 | lib-currency | 8 (DefaultAllowNegative, DefaultPrecision, AutogainProcessingMode, AutogainTaskIntervalMs, AutogainBatchSize, TransactionRetentionDays, IdempotencyTtlSeconds, HoldMaxDurationDays) | Stub |
| 2 | lib-analytics | 10 (SummaryStoreName, RatingStoreName, HistoryStoreName, EventBufferSize, EventBufferFlushIntervalSeconds, Glicko2DefaultRating, Glicko2DefaultDeviation, Glicko2DefaultVolatility, Glicko2SystemConstant, SummaryCacheTtlSeconds) | Stub |
| 3 | lib-achievement | 16 (DefinitionStoreName, ProgressStoreName, UnlockStoreName, SteamApiKey, SteamAppId, XboxClientId, XboxClientSecret, PlayStationClientId, PlayStationClientSecret, MockPlatformSync, AutoSyncOnUnlock, SyncRetryAttempts, SyncRetryDelaySeconds, ProgressCacheTtlSeconds, RarityCalculationIntervalMinutes, RareThresholdPercent) | Stub |
| 4 | lib-leaderboard | 7 (DefinitionStoreName, RankingStoreName, SeasonStoreName, MaxEntriesPerQuery, RankCacheTtlSeconds, ScoreUpdateBatchSize, AutoArchiveOnSeasonEnd) | Stub |
| 5 | lib-matchmaking | 12 (ServerSalt, ProcessingIntervalSeconds, DefaultMaxIntervals, MaxConcurrentTicketsPerPlayer, DefaultMatchAcceptTimeoutSeconds, StatsPublishIntervalSeconds, PendingMatchRedisKeyTtlSeconds, ImmediateMatchCheckEnabled, AutoRequeueOnDecline, BackgroundServiceStartupDelaySeconds, DefaultReservationTtlSeconds, DefaultJoinDeadlineSeconds) | Stub |
| 6 | lib-game-service | 1 (StateStoreName) | Stub |
| 7 | lib-music | 1 (Enabled) | Stub |
| 8 | lib-game-session | 9 (ServerSalt, MaxPlayersPerSession, DefaultSessionTimeoutSeconds, DefaultReservationTtlSeconds, DefaultLobbyMaxPlayers, CleanupIntervalSeconds, CleanupServiceStartupDelaySeconds, StartupServiceDelaySeconds, SupportedGameServices) | Stub |
| 9 | lib-contract | All properties | Stub |
| 10 | lib-documentation | All properties | Stub |
| 11 | lib-realm | All properties | Stub |
| 12 | lib-realm-history | All properties | Stub |
| 13 | lib-relationship | All properties | Stub |
| 14 | lib-relationship-type | All properties | Stub |
| 15 | lib-species | All properties | Stub |
| 16 | lib-character-history | All properties | Stub |
| 17 | lib-character-personality | All properties | Stub |
| 18 | lib-location | All properties | Stub |
| 19 | lib-mesh | 19 (UseLocalRouting, EndpointHost, EndpointPort, HeartbeatIntervalSeconds, EndpointTtlSeconds, DegradationThresholdSeconds, DefaultLoadBalancer, LoadThresholdPercent, EnableServiceMappingSync, HealthCheckEnabled, HealthCheckIntervalSeconds, HealthCheckTimeoutSeconds, CircuitBreakerEnabled, CircuitBreakerThreshold, CircuitBreakerResetSeconds, MaxRetries, RetryDelayMilliseconds, EnableDetailedLogging, MetricsEnabled) | Has logic but ignores config |
| 20 | lib-website | All properties | Stub |

---

## T21: Hardcoded Tunables {#t21-hardcoded-tunables}

**Severity**: Medium — Prevents runtime tuning, forces redeploy for config changes
**Fix complexity**: Medium — Add config property to schema, regenerate, wire up

### Common patterns found across 17 services:

**Retry counts** (~8 instances):
```csharp
// Hardcoded in: lib-auth, lib-connect, lib-mesh, lib-actor, lib-asset, lib-matchmaking, lib-orchestrator, lib-currency
var maxRetries = 3;  // Should be _configuration.MaxRetries
```

**TTL/timeout values** (~15 instances):
```csharp
// Various services
TimeSpan.FromMinutes(5)    // cache TTLs
TimeSpan.FromSeconds(30)   // operation timeouts
TimeSpan.FromHours(1)      // session expiry
```

**HTTP client timeouts** (~6 instances):
```csharp
// lib-auth, lib-mesh, lib-asset, lib-orchestrator
Timeout = TimeSpan.FromSeconds(10)  // Should be _configuration.HttpTimeoutSeconds
```

**Pagination/batch limits** (~10 instances):
```csharp
// Various services
.Take(100)     // Should be _configuration.MaxPageSize
limit = 50     // Should be _configuration.DefaultBatchSize
```

**Background service intervals** (~8 instances):
```csharp
// lib-actor, lib-matchmaking, lib-mesh, lib-orchestrator
await Task.Delay(TimeSpan.FromSeconds(30))  // Should be _configuration.PollingIntervalSeconds
```

**Buffer sizes** (~4 instances):
```csharp
// lib-analytics, lib-messaging
bufferSize = 1000  // Should be _configuration.EventBufferSize
```

---

## T9: Non-Atomic Operations {#t9-non-atomic}

**Severity**: Critical — Data corruption under concurrency (lost updates)
**Fix complexity**: High — Requires ETag-based optimistic concurrency or distributed locks

All violations follow the same dangerous pattern:
```csharp
// DANGEROUS: Another request between Get and Save causes lost update
var data = await _store.GetAsync(key);
data.SomeList.Add(newItem);  // or Remove, or modify
await _store.SaveAsync(key, data);

// CORRECT: Optimistic concurrency
var (data, etag) = await _store.GetWithETagAsync(key);
data.SomeList.Add(newItem);
var saved = await _store.TrySaveAsync(key, data, etag);
if (!saved) { /* retry or conflict response */ }
```

### Violations by service:

| # | Service | Approximate Count | Operations Affected |
|---|---------|-------------------|-------------------|
| 1 | lib-inventory | 8 | Container list ops, weight recalc, slot counting |
| 2 | lib-contract | 5 | Party consent, milestone status, breach tracking |
| 3 | lib-currency | 4 | Balance operations, hold captures |
| 4 | lib-save-load | 4 | Slot management, version pinning |
| 5 | lib-matchmaking | 3 | Queue state, ticket management |
| 6 | lib-actor | 3 | Actor lifecycle, pool management |
| 7 | lib-leaderboard | 2 | Score updates, season management |
| 8 | lib-game-session | 2 | Player join/leave |
| 9 | lib-character-encounter | 2 | Perspective updates, memory decay |
| 10 | lib-achievement | 1 | Progress updates |
| 11 | lib-asset | 1 | Bundle management |
| 12 | lib-documentation | 1 | Document updates |
| 13 | lib-scene | 1 | Checkout/commit |

---

## Fix Strategy

### Phase 1: Type Safety (T25) — ~34 fixes
Straightforward find-and-replace. Change field types, fix assignments and comparisons. No architectural changes needed.

### Phase 2: Dead Infrastructure (T21 cache stores) — 5 fixes
For each unused store: determine if the cache is actually needed for performance, then either implement read-through caching or remove from `schemas/state-stores.yaml`.

### Phase 3: Configuration Wiring (T21 config + tunables) — ~71 fixes
Wire up existing config properties in service implementations. For hardcoded tunables, add new properties to config schemas, regenerate, then reference them.

### Phase 4: Concurrency Safety (T9) — 36 fixes
Most complex. Each violation needs analysis of the specific operation to determine whether optimistic concurrency (ETags) or pessimistic locking (distributed locks) is appropriate. List operations on shared state almost always need ETags.

---

## Progress Tracking

- [x] Phase 1: T25 enum-as-string (9/9)
  - [x] CurrencyService - WalletModel.Status, HoldModel.Status → enum types
  - [x] ContractService - ContractInstanceModel.Status, ContractPartyModel.ConsentStatus, MilestoneInstanceModel.Status, BreachModel.Status, PreboundApiModel.ExecutionMode → enum types
  - [x] ActorAssignment - Status → ActorStatus enum
  - [x] OrchestratorService - Created ProcessorStatus enum, ProcessorInstance.Status → ProcessorStatus
- [ ] Phase 1: T25 GUID-as-string (4/11 services complete)
  - [x] lib-account - AccountModel.AccountId → Guid
  - [x] lib-character - CharacterModel.CharacterId, RealmId, SpeciesId → Guid; CharacterArchiveModel similarly; RefCountData.CharacterId → Guid
  - [x] lib-location - LocationModel.LocationId, RealmId, ParentLocationId → Guid/Guid?
  - [x] lib-connect - ConnectionStateData.SessionId, AccountId → Guid/Guid?; SessionHeartbeat.SessionId, InstanceId → Guid; SessionEvent.SessionId → Guid; PendingRPCInfo.ClientSessionId → Guid
  - [ ] lib-character-encounter - Complex (many List<string> to List<Guid>)
  - [ ] lib-auth - SessionModel IDs
  - [ ] lib-voice - Room/peer IDs
  - [ ] lib-matchmaking - Ticket/queue IDs
  - [ ] lib-asset - Owner/realm IDs
  - [ ] lib-documentation - Document owner
  - [ ] lib-behavior - Behavior owner
- [x] Phase 2: T21 unused cache stores (5/5)
  - [x] MusicCompositions - Implemented composition caching for deterministic requests
  - [x] CurrencyBalanceCache - Implemented cache read-through/write-through for balance operations
  - [x] CurrencyHoldsCache - Implemented cache read-through/write-through for hold operations
  - [x] InventoryContainerCache - Implemented cache read-through/write-through for container operations
  - [x] InventoryLock - Implemented distributed locking for AddItem, RemoveItem, UpdateContainer operations
- [ ] Phase 3: T21 unused config (0/20 services)
- [ ] Phase 3: T21 hardcoded tunables (0/51)
- [ ] Phase 4: T9 non-atomic operations (0/36)
