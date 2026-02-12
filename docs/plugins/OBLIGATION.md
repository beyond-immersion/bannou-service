# Obligation Plugin Deep Dive

> **Plugin**: lib-obligation
> **Schema**: schemas/obligation-api.yaml
> **Version**: 1.0.0
> **State Stores**: obligation-cache (Redis), obligation-action-mappings (MySQL), obligation-violations (MySQL), obligation-idempotency (Redis), obligation-lock (Redis)

---

## Overview

Contract-aware obligation tracking for NPC cognition (L4 GameFeatures). Provides dynamically-updated action cost modifiers based on active contracts (guild charters, trade agreements, quest oaths). The obligation service is the bridge between the Contract service's behavioral clauses and the GOAP planner's action cost system, enabling NPCs to have "second thoughts" before violating their obligations.

**Two-Layer Design**: Works standalone with raw contract penalties. When personality data is available (soft L4 dependency via character-personality's variable provider), obligation costs are enriched with trait-weighted moral reasoning. Without personality enrichment, costs are the unweighted base penalties from contract behavioral clauses.

**Core Flow**:
1. Contract activated -> obligation service extracts behavioral clauses -> caches per-party
2. Actor cognition evaluates actions -> reads `${obligations.*}` variables from provider
3. GOAP planner sees modified action costs -> selects alternative if cost too high
4. If actor proceeds despite cost -> report-violation -> breach + feedback events

**Variable Provider**: Implements `IVariableProviderFactory` providing the `${obligations.*}` namespace to Actor (L2) via the Variable Provider Factory pattern.

See [GitHub Issue #410](https://github.com/beyond-immersion/bannou-service/issues/410) for the original design specification ("Second Thoughts" feature).

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Persistence for action mappings (MySQL), violation history (MySQL), obligation cache (Redis), idempotency keys (Redis), distributed locks (Redis) |
| lib-state (`IDistributedLockProvider`) | Distributed locks for obligation cache rebuild operations |
| lib-messaging (`IMessageBus`) | Publishing violation reported and cache rebuilt events; error event publication |
| lib-contract (`IContractClient`) | Querying active contracts for characters, extracting behavioral clauses, reporting breaches on knowing violations (L1 hard dependency) |
| character-personality variable provider (soft L4 dependency) | Personality traits (`${personality.honesty}`, `${personality.loyalty}`, `${personality.agreeableness}`, `${personality.conscientiousness}`, `${combat.preferred_engagement}`) for moral weighting of obligation costs; graceful degradation when unavailable |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-actor (L2) | Actor discovers `ObligationProviderFactory` via `IEnumerable<IVariableProviderFactory>` DI injection; creates obligation provider instances per character for ABML behavior execution (`${obligations.*}` variables) |
| lib-character-personality (L4, planned) | Subscribes to `obligation.violation.reported` events to trigger `OATH_BROKEN` / `RESISTED_TEMPTATION` personality drift |
| lib-character-encounter (L4, planned) | Subscribes to `obligation.violation.reported` events to record encounter memories with negative sentiment when violations involve other characters |

---

## State Storage

**Store**: `obligation-cache` (Backend: Redis, prefix: `obligation:cache`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{characterId}` | Obligation manifest | Cached obligation entries per character with pre-computed violation cost map; event-driven invalidation via contract lifecycle events |

**Store**: `obligation-action-mappings` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `mapping:{tag}` | `ActionMappingModel` | Registered GOAP action tag to violation type code mappings; fallback to 1:1 convention when no explicit mapping exists |

**Store**: `obligation-violations` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `violation:{violationId}` | `ViolationRecordModel` | Historical violation records per character; queryable by contract, type, and time range |

**Store**: `obligation-idempotency` (Backend: Redis, prefix: `obligation:idemp`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{idempotencyKey}` | Violation ID | TTL-based deduplication for violation reports (configurable via `IdempotencyTtlSeconds`) |

**Store**: `obligation-lock` (Backend: Redis, prefix: `obligation:lock`)

| Key Pattern | Purpose |
|-------------|---------|
| `cache:{characterId}` | Distributed lock for obligation cache rebuild operations (serializes concurrent rebuilds) |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `obligation.violation.reported` | `ObligationViolationReportedEvent` | Character knowingly violates an obligation via `ReportViolationAsync`; includes full violation context for downstream personality drift and encounter memory |
| `obligation.cache.rebuilt` | `ObligationCacheRebuiltEvent` | Obligation cache rebuilt for a character; includes obligation/contract counts and trigger reason (observability) |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `contract.activated` | `HandleContractActivated` | Rebuild obligation cache for contract parties when a contract becomes active |
| `contract.terminated` | `HandleContractTerminated` | Remove obligations when a contract is terminated early |
| `contract.fulfilled` | `HandleContractFulfilled` | Remove obligations when all required milestones complete |
| `contract.expired` | `HandleContractExpired` | Remove obligations when a contract reaches natural expiration |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `CacheTtlMinutes` | `OBLIGATION_CACHE_TTL_MINUTES` | `10` | TTL in minutes for obligation manifest cache entries per character (range: 1-1440) |
| `EvaluationTimeoutMs` | `OBLIGATION_EVALUATION_TIMEOUT_MS` | `5000` | Timeout in milliseconds for evaluate-action operations (range: 500-30000) |
| `MaxObligationsPerCharacter` | `OBLIGATION_MAX_OBLIGATIONS_PER_CHARACTER` | `200` | Safety limit on cached obligations per character to prevent runaway contract accumulation (range: 10-1000) |
| `BreachReportEnabled` | `OBLIGATION_BREACH_REPORT_ENABLED` | `true` | Whether to auto-report violations as breaches to the contract service |
| `IdempotencyTtlSeconds` | `OBLIGATION_IDEMPOTENCY_TTL_SECONDS` | `86400` | TTL in seconds for violation report idempotency keys (range: 3600-604800) |
| `DefaultPageSize` | `OBLIGATION_DEFAULT_PAGE_SIZE` | `20` | Default page size for paginated queries (range: 1-100) |
| `LockTimeoutSeconds` | `OBLIGATION_LOCK_TIMEOUT_SECONDS` | `30` | Timeout in seconds for distributed locks on obligation cache operations (range: 5-120) |
| `MaxConcurrencyRetries` | `OBLIGATION_MAX_CONCURRENCY_RETRIES` | `3` | Maximum retry attempts for optimistic concurrency conflicts (range: 1-10) |
| `MaxActiveContractsQuery` | `OBLIGATION_MAX_ACTIVE_CONTRACTS_QUERY` | `100` | Maximum number of active contracts to query per character during cache rebuild (range: 10-500) |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<ObligationService>` | Structured logging |
| `ObligationServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access for MySQL and Redis stores |
| `IMessageBus` | Event publishing and error event publication |
| `IDistributedLockProvider` | Distributed locks for cache rebuild operations |
| `IContractClient` | Querying active contracts, extracting behavioral clauses, reporting breaches (L1 hard dependency) |
| `ObligationProviderFactory` | Implements `IVariableProviderFactory` to provide `${obligations.*}` variables to the Actor service's behavior system |

---

## API Endpoints (Implementation Notes)

**All endpoints are currently stubbed** -- returning `NotImplemented`. The schema and models are complete; business logic implementation is pending.

### Action Tag Mapping (3 endpoints)

Configuration endpoints for mapping GOAP action tags to violation type codes. By default, tags are matched 1:1 against violation type codes (tag "theft" matches violation type "theft"). Explicit mappings are only needed when vocabularies differ or when one action triggers multiple violation types (e.g., "attack_surrendered_enemy" -> ["honor_combat", "show_mercy"]).

- **SetActionMapping**: Idempotent upsert by tag. Requires `developer` role.
- **ListActionMappings**: Cursor-paginated with optional text search across tag names and descriptions.
- **DeleteActionMapping**: Removes mapping; tag falls back to 1:1 convention. Requires `developer` role.

### Obligation Query (2 endpoints)

Runtime query endpoints for the actor cognition pipeline and external callers.

- **QueryObligations**: Returns all active obligations for a character derived from active contracts with behavioral clauses. Includes pre-computed violation cost map keyed by violation type code. Cache-backed with event-driven invalidation; `forceRefresh` bypasses cache.
- **EvaluateAction**: Non-mutating speculative query. Given action tags, resolves to violation types via mappings, matches against character's obligations, and returns per-action cost breakdowns. When personality enrichment is available, costs are weighted by traits (honesty, loyalty, agreeableness, conscientiousness) and combat preferences. Primarily for external callers; actor cognition reads from the variable provider cache.

### Violation Management (2 endpoints)

Recording and querying knowing obligation violations.

- **ReportViolation**: Records a knowing violation, optionally reports breach to lib-contract (controlled by `BreachReportEnabled`), publishes `obligation.violation.reported` event. Idempotent via `idempotencyKey`.
- **QueryViolations**: Cursor-paginated violation history. Filters by contract, violation type, and time range. Ordered by timestamp descending.

### Cache Management (1 endpoint)

- **InvalidateCache**: Administrative endpoint to force a full cache rebuild from current contract state. Requires `developer` role.

### Resource Cleanup (1 endpoint)

- **CleanupByCharacter**: Called by lib-resource during cascading character deletion. Removes cached obligation manifests and violation history. Registered via `x-references` in the API schema.

### Compression (2 endpoints)

- **GetCompressData**: Called by Resource service during character compression. Returns violation history for archival as `ObligationArchive` (extends `ResourceArchiveBase`).
- **RestoreFromArchive**: Restores violation history from archive data. Obligation cache is rebuilt automatically from active contracts, not from archive. Requires `admin` role.

---

## Visual Aid

```
┌─────────────────────────────────────────────────────────────────────┐
│                   Obligation Data Flow                               │
│                                                                     │
│  Contract Service (L1)                                              │
│  ┌─────────────────────────────────────────────┐                    │
│  │ contract.activated / terminated / fulfilled  │                    │
│  │ / expired events                             │                    │
│  └──────────────────────┬──────────────────────┘                    │
│                         ▼                                           │
│  ┌─────────────────────────────────────────────┐                    │
│  │ ObligationServiceEvents                     │                    │
│  │ Rebuilds/clears obligation cache per party  │                    │
│  └──────────────────────┬──────────────────────┘                    │
│                         ▼                                           │
│  obligation-cache (Redis)          obligation-action-mappings (MySQL)│
│  ┌────────────────────────┐       ┌──────────────────────────┐      │
│  │ {characterId}           │       │ mapping:{tag}             │      │
│  │ ├── obligations[]       │       │ ├── tag                   │      │
│  │ │   ├── contractId      │       │ ├── violationTypes[]      │      │
│  │ │   ├── clauseCode      │       │ └── description           │      │
│  │ │   ├── violationType   │       └──────────────┬─────────────┘      │
│  │ │   └── basePenalty     │                      │                    │
│  │ ├── violationCostMap{}  │    (tag resolution)  │                    │
│  │ └── lastRefreshedAt     │◄─────────────────────┘                    │
│  └─────────┬──────────────┘                                            │
│            │                                                           │
│            ▼                                                           │
│  ObligationProviderFactory ─── IVariableProviderFactory                │
│  ┌──────────────────────────────────────────────────┐                  │
│  │ ${obligations.active_count}                       │                  │
│  │ ${obligations.violation_cost.<type>}               │                  │
│  │ ${obligations.highest_penalty_type}                │ ──► Actor (L2)  │
│  │ ${obligations.total_obligation_cost}               │     ABML/GOAP   │
│  └──────────────────────────────────────────────────┘                  │
│                                                                       │
│  obligation-violations (MySQL)     obligation-idempotency (Redis)     │
│  ┌────────────────────────┐       ┌──────────────────────────┐        │
│  │ violation:{violationId} │       │ {idempotencyKey}          │        │
│  │ ├── characterId         │       │ → violationId (TTL-based) │        │
│  │ ├── contractId          │       └──────────────────────────┘        │
│  │ ├── violationType       │                                           │
│  │ ├── actionTag           │  ──► obligation.violation.reported event  │
│  │ ├── motivationScore     │      (downstream: personality drift,     │
│  │ └── violationCost       │       encounter memory)                  │
│  └────────────────────────┘                                           │
│                                                                       │
│  obligation-lock (Redis)                                              │
│  └── cache:{characterId}  -- Serializes concurrent cache rebuilds     │
└───────────────────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

**All 11 endpoints are stubbed** -- the service implementation returns `NotImplemented` for every operation. The schema, models, events, configuration, and state stores are fully defined; business logic implementation is pending.

1. **Action Mapping CRUD** (`SetActionMapping`, `ListActionMappings`, `DeleteActionMapping`): No mapping storage or retrieval logic.
2. **Obligation Query** (`QueryObligations`, `EvaluateAction`): No contract querying, cache management, or personality-weighted cost computation.
3. **Violation Reporting** (`ReportViolation`, `QueryViolations`): No violation storage, idempotency checking, or breach reporting to lib-contract.
4. **Cache Management** (`InvalidateCache`): No cache rebuild logic.
5. **Resource Cleanup** (`CleanupByCharacter`): No cleanup of obligation or violation data.
6. **Compression** (`GetCompressData`, `RestoreFromArchive`): No archive serialization or restoration.
7. **Event Handlers**: Contract lifecycle event handlers (`HandleContractActivated`, `HandleContractTerminated`, `HandleContractFulfilled`, `HandleContractExpired`) are not implemented.
8. **Variable Provider Factory**: `ObligationProviderFactory` providing `${obligations.*}` variables to Actor is not implemented.

---

## Potential Extensions

- **lib-moral integration**: The original design (#410) envisions a companion lib-moral service (L4) providing social/cultural norm framework that enriches obligation costs with location-dependent, faction-specific, and species-instinctual norms. Obligation is designed to integrate via `GetService<T>()` + graceful degradation.

- **Cognition stage integration**: A 6th cognition stage (`evaluate_consequences`) inserted into the Actor behavior pipeline between `store_memory` and `evaluate_goal_impact`. Opt-in via `conscience: true` ABML metadata flag. The obligation service provides the data; the cognition stage integration lives in lib-actor/lib-behavior.

- **Post-violation emotional feedback**: When knowing violations occur, downstream consumers could: increase stress/decrease comfort (emotional modification), record high-significance memories, trigger personality drift (`OATH_BROKEN`, `RESISTED_TEMPTATION`, `GUILTY_CONSCIENCE` experience types), and record encounter memories for violations involving other characters.

- **Violation retention/pruning**: No retention policy exists for violation history. A background worker or configurable retention period could prune old violation records.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*(No current bugs -- all endpoints are stubbed.)*

### Intentional Quirks (Documented Behavior)

- **Violation types are opaque strings**: No separate taxonomy or enum is maintained for violation types. The vocabulary is defined entirely by contract templates' behavioral clause definitions (e.g., "theft", "deception", "violence", "honor_combat"). This allows the violation vocabulary to grow organically as new contract templates are authored.

- **1:1 convention-based tag matching as default**: When no explicit action mapping exists for a GOAP action tag, the tag name is used directly as the violation type code. Explicit mappings via `SetActionMapping` are only needed when vocabularies differ or when one action maps to multiple violation types.

- **Personality enrichment is fully optional**: The two-layer design means all obligation costs function without character-personality. When personality data is unavailable, `EvaluateAction` returns raw `basePenalty` values (i.e., `weightedPenalty == basePenalty`). The `moralWeightingApplied` response field indicates whether enrichment was applied.

- **Cache is event-driven, not TTL-only**: The obligation cache is rebuilt when contract lifecycle events fire (activated, terminated, fulfilled, expired). The `CacheTtlMinutes` provides a secondary expiration for stale entries that might not receive events (edge case). This means obligations update in near-real-time as contracts change.

- **Resource cleanup via x-references**: Character deletion triggers cleanup through lib-resource's `x-references` cascade mechanism, not via direct event subscription. This follows FOUNDATION TENETS for resource-managed cleanup.

- **Compression priority 25**: Obligation data is compressed at priority 25 during character archival, placing it after core character data but before encounter/history data in the compression pipeline.

### Design Considerations (Requires Planning)

- **Contract behavioral clause format**: The obligation service expects contracts to contain behavioral clauses with violation type codes and base penalties. The exact schema for behavioral clauses within contract templates is not yet defined in lib-contract. This is a prerequisite for implementation.
<!-- AUDIT:NEEDS_DESIGN:2026-02-12:https://github.com/beyond-immersion/bannou-service/issues/410 -->

- **Personality variable consumption pattern**: The design specifies reading personality variables (`${personality.honesty}`, etc.) for moral weighting, but the mechanism for accessing another service's variable provider data within the obligation service (as opposed to within the Actor runtime) needs clarification. The obligation service may need to call character-personality's API directly rather than going through the variable provider system.
<!-- AUDIT:NEEDS_DESIGN:2026-02-12:https://github.com/beyond-immersion/bannou-service/issues/410 -->

---

## Work Tracking

- [#410](https://github.com/beyond-immersion/bannou-service/issues/410) - Feature: Second Thoughts -- Prospective Consequence Evaluation for NPC Cognition (design spec for lib-obligation + lib-moral)
