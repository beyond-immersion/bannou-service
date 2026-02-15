# Obligation Plugin Deep Dive

> **Plugin**: lib-obligation
> **Schema**: schemas/obligation-api.yaml
> **Version**: 1.0.0
> **State Stores**: obligation-cache (Redis), obligation-action-mappings (MySQL), obligation-violations (MySQL), obligation-idempotency (Redis), obligation-lock (Redis)
> **Guide**: [Morality System](../guides/MORALITY-SYSTEM.md) (cross-service integration with lib-obligation and lib-faction)

---

## Overview

Contract-aware obligation tracking for NPC cognition (L4 GameFeatures), bridging the Contract service's behavioral clauses and the GOAP planner's action cost system to enable NPCs to have "second thoughts" before violating obligations. Provides dynamically-updated action cost modifiers based on active contracts (guild charters, trade agreements, quest oaths), working standalone with raw contract penalties or enriched with personality-weighted moral reasoning when character-personality data is available. Implements `IVariableProviderFactory` providing the `${obligations.*}` namespace to Actor (L2) via the Variable Provider Factory pattern. See [GitHub Issue #410](https://github.com/beyond-immersion/bannou-service/issues/410) for the original design specification ("Second Thoughts" feature).

---

## Core Mechanics

**Three-Layer Design**: Works standalone with raw contract penalties (Layer 1). When personality data is available (soft L4 dependency via character-personality's variable provider), obligation costs are enriched with trait-weighted moral reasoning (Layer 2). When Hearsay data is available (soft L4 dependency, planned), norm costs use belief-filtered penalties reflecting what the NPC *believes* social rules to be, not ground truth (Layer 3). Each enrichment layer is independently optional and gracefully degrades. The `NormResolutionMode` configuration controls the fallback when Hearsay is unavailable: `PerfectKnowledge` (default) queries Faction directly for ground-truth norms, while `UncertaintySimulation` applies random variance to simulate imperfect social knowledge without the full belief propagation system.

**Core Flow**:
1. Contract activated -> obligation service extracts behavioral clauses -> caches per-party
2. Actor cognition evaluates actions -> reads `${obligations.*}` variables from provider
3. GOAP planner sees modified action costs -> selects alternative if cost too high
4. If actor proceeds despite cost -> report-violation -> breach + feedback events

---

## The Second Thoughts Vision (Architectural Target)

> **Status**: All 11 obligation endpoints are fully implemented. Contract-driven obligation extraction, personality-weighted cost computation, violation reporting, and the variable provider factory work end-to-end. The broader vision described below -- emergent moral character arcs, post-violation feedback loops, and progressive player moral agency -- represents the narrative payoff that the current machinery enables.

### The Narrative Promise: NPCs with a Conscience

The honest merchant NPC hesitates before selling stolen goods because her guild charter's behavioral clause makes "fence_stolen_goods" cost 20.6 after personality weighting. The corrupted guard gradually becomes more willing to take bribes -- each knowing violation triggers `OATH_BROKEN` personality drift, eroding honesty over time, which reduces the personality weight multiplier, which lowers future bribe costs. The redeemed thief chooses to return stolen property because joining a new guild added faction norms that make theft expensive, and each time he resists temptation, `RESISTED_TEMPTATION` ticks honesty upward, compounding the cost. None of these are scripted character arcs. They emerge from the repeated interaction between social context (Faction), personal traits (Personality), contractual commitments (Contract), and accumulated moral choices (Obligation), expressed through GOAP action cost modifications in the Actor cognition pipeline.

### Post-Violation Feedback Loops Make Moral Choices Narratively Generative

When an NPC commits a knowing violation (flagged by the `evaluate_consequences` cognition stage), the consequences ripple through multiple systems:

- **Personality drift**: `OATH_BROKEN` / `RESISTED_TEMPTATION` / `GUILTY_CONSCIENCE` experience types shift personality traits over time via lib-character-personality. Repeated theft erodes honesty; repeated resistance reinforces it.
- **Guilt as composite emotion**: The guilt pattern (stress up, sadness up, comfort down, joy down) is expressed through the existing actor emotional model -- not a new emotion axis, but a recognizable composite. The emotional state modulates future behavior through ABML expressions.
- **Encounter memory**: Violations witnessed by other characters create negative-sentiment encounters via lib-character-encounter. The witness remembers. This propagates social consequences -- the merchant remembers being swindled and adjusts future interactions.
- **Contract breach**: Knowing violations auto-report to lib-contract (controlled by `BreachReportEnabled`), triggering formal enforcement terms -- penalties, contract termination, escalation through governance structures.
- **Divine attention**: Regional watcher god actors may notice patterns of consistent virtue or vice. Consistent violations → loss of divine favor. Consistent virtue → divine blessing (via lib-status). This creates a cosmological moral feedback loop.

Each of these consequences feeds back into the inputs of the morality system (personality changes affect weighting, encounter memories affect relationships, divine blessings/curses affect status), making moral choices narratively generative rather than terminal. This is how the morality system participates in the Content Flywheel.

### Progressive Moral Agency for Player Characters

Player characters have NPC brains running at all times. The guardian spirit (the player) influences but doesn't directly control. The morality system creates friction between what the player wants and what the character is willing to do. A character with high honesty RESISTS being pushed toward theft -- the resistance is proportional to the moral cost. A player who consistently forces immoral actions doesn't just get the immediate reward; they get a character whose personality has shifted, whose reputation is damaged, whose divine relationships are strained, and who resists LESS next time. The "second thoughts" moment -- a visible hesitation proportional to moral cost -- is the player-facing expression of the `evaluate_consequences` stage. See [Morality System guide](../guides/MORALITY-SYSTEM.md) for the complete pipeline and player experience design.

### The Integration Roadmap

The full morality pipeline requires five integration points to be complete: (1) Faction norms flowing to Obligation as ambient cost sources alongside contractual obligations, (2) the faction variable provider exposing norm and territory data (`${faction.has_norm.<type>}`, `${faction.in_controlled_territory}`), (3) the `evaluate_consequences` cognition stage being fully wired in the Actor pipeline, (4) post-violation feedback loops to personality/encounter/divine systems, and (5) data-driven trait-to-violation-type mapping replacing the current hardcoded switch. The current implementation has the machinery in place; these integration points connect it into the full pipeline.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Persistence for action mappings (MySQL), violation history (MySQL), obligation cache (Redis), idempotency keys (Redis), distributed locks (Redis) |
| lib-state (`IDistributedLockProvider`) | Distributed locks for obligation cache rebuild operations |
| lib-messaging (`IMessageBus`) | Publishing violation reported and cache rebuilt events; error event publication |
| lib-contract (`IContractClient`) | Querying active contracts for characters, extracting behavioral clauses, reporting breaches on knowing violations (L1 hard dependency) |
| lib-resource (`IResourceClient`) | Registering/unregistering character references, cleanup callback registration, and compression callback registration (L1 hard dependency) |
| character-personality variable provider (soft L4 dependency) | Personality traits (`${personality.honesty}`, `${personality.loyalty}`, `${personality.agreeableness}`, `${personality.conscientiousness}`) for moral weighting of obligation costs; graceful degradation when unavailable. Accessed via `IEnumerable<IVariableProviderFactory>` DI discovery, not a direct client dependency |

**Planned Dependencies (not yet in code):**

| Planned Dependency | Planned Usage |
|--------------------|---------------|
| lib-hearsay (`IHearsayClient`, soft L4) | Belief-filtered norm costs: Hearsay's `${hearsay.norm.believed_cost.<type>}` would replace raw Faction penalties with what the NPC *believes* the penalty to be. Config properties `NormResolutionMode` and `NormUncertaintyVariance` are pre-defined for this integration but not yet referenced in code |
| lib-faction (`IFactionClient`, soft L4) | Query applicable norms for characters when Hearsay is unavailable. Would provide ground-truth norm data for the `PerfectKnowledge` fallback path. No `IFactionClient` injection exists in current code |

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
| `MaxObligationsPerCharacter` | `OBLIGATION_MAX_OBLIGATIONS_PER_CHARACTER` | `200` | Safety limit on cached obligations per character to prevent runaway contract accumulation (range: 10-1000) |
| `BreachReportEnabled` | `OBLIGATION_BREACH_REPORT_ENABLED` | `true` | Whether to auto-report violations as breaches to the contract service |
| `IdempotencyTtlSeconds` | `OBLIGATION_IDEMPOTENCY_TTL_SECONDS` | `86400` | TTL in seconds for violation report idempotency keys (range: 3600-604800) |
| `DefaultPageSize` | `OBLIGATION_DEFAULT_PAGE_SIZE` | `20` | Default page size for paginated queries (range: 1-100) |
| `LockTimeoutSeconds` | `OBLIGATION_LOCK_TIMEOUT_SECONDS` | `30` | Timeout in seconds for distributed locks on obligation cache operations (range: 5-120) |
| `MaxActiveContractsQuery` | `OBLIGATION_MAX_ACTIVE_CONTRACTS_QUERY` | `100` | Maximum number of active contracts to query per character during cache rebuild (range: 10-500) |
| `NormResolutionMode` | `OBLIGATION_NORM_RESOLUTION_MODE` | `PerfectKnowledge` | How norm costs are resolved when Hearsay is unavailable: `PerfectKnowledge` (query Faction directly) or `UncertaintySimulation` (apply random variance to simulate imperfect knowledge). **Stub scaffolding — not yet referenced in service code** |
| `NormUncertaintyVariance` | `OBLIGATION_NORM_UNCERTAINTY_VARIANCE` | `0.2` | Max variance (+/-) applied to norm penalties in UncertaintySimulation mode (range: 0.0-0.5; only used when NormResolutionMode is UncertaintySimulation). **Stub scaffolding — not yet referenced in service code** |

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
| `IResourceClient` | Resource reference tracking, cleanup callback registration, compression callback registration (L1 hard dependency) |
| `IEventConsumer` | Registers event handlers for contract lifecycle events (contract.activated/terminated/fulfilled/expired) |
| `IEnumerable<IVariableProviderFactory>` | DI collection for locating the personality provider factory; used by `TryGetPersonalityTraitsAsync` for moral weighting |
| `IJsonQueryableStateStore<ActionMappingModel>` | JSON path queries for action mappings (paginated listing with text search) — obtained via `stateStoreFactory.GetJsonQueryableStore` |
| `IJsonQueryableStateStore<ViolationRecordModel>` | JSON path queries for violations (paginated history, cleanup batch queries, compression data export) — obtained via `stateStoreFactory.GetJsonQueryableStore` |
| `ObligationProviderFactory` | Implements `IVariableProviderFactory` to provide `${obligations.*}` variables to the Actor service's behavior system; registered as singleton in plugin |

---

## API Endpoints (Implementation Notes)

**All 11 endpoints are fully implemented** with complete business logic, error handling, and distributed locking.

### Action Tag Mapping (3 endpoints)

Configuration endpoints for mapping GOAP action tags to violation type codes. By default, tags are matched 1:1 against violation type codes (tag "theft" matches violation type "theft"). Explicit mappings are only needed when vocabularies differ or when one action triggers multiple violation types (e.g., "attack_surrendered_enemy" -> ["honor_combat", "show_mercy"]).

- **SetActionMapping**: Idempotent upsert by tag. Requires `developer` role.
- **ListActionMappings**: Cursor-paginated with optional text search across tag names and descriptions.
- **DeleteActionMapping**: Removes mapping; tag falls back to 1:1 convention. Requires `developer` role.

### Obligation Query (2 endpoints)

Runtime query endpoints for the actor cognition pipeline and external callers.

- **QueryObligations**: Returns all active obligations for a character derived from active contracts with behavioral clauses. Includes pre-computed violation cost map keyed by violation type code. Cache-backed with event-driven invalidation; `forceRefresh` bypasses cache.
- **EvaluateAction**: Non-mutating speculative query. Given action tags, resolves to violation types via mappings, matches against character's obligations, and returns per-action cost breakdowns. When personality enrichment is available, costs are weighted by traits (honesty, loyalty, agreeableness, conscientiousness). Accepts optional `locationId` and `targetEntityId`/`targetEntityType` fields, but `locationId` is not yet used (see Stubs). Primarily for external callers; actor cognition reads from the variable provider cache.

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
│  │ ${obligations.has_obligations}                     │                  │
│  │ ${obligations.contract_count}                      │                  │
│  │ ${obligations.violation_cost.<type>}               │ ──► Actor (L2)  │
│  │ ${obligations.highest_penalty_type}                │     ABML/GOAP   │
│  │ ${obligations.total_obligation_cost}               │                  │
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

All 11 API endpoints are fully implemented. The following supporting integrations have scaffolding but are not yet wired:

1. **Norm resolution config properties (stub scaffolding)**: `NormResolutionMode` and `NormUncertaintyVariance` are defined in the configuration schema and generated into `ObligationServiceConfiguration`, but `_configuration.NormResolutionMode` and `_configuration.NormUncertaintyVariance` are never referenced in service code. These are pre-positioned for the planned faction norm integration (Hearsay-filtered costs / Faction direct query fallback). Acceptable per T21 stub scaffolding exception.

2. **Event template registration not wired**: `ObligationEventTemplates.RegisterAll(IEventTemplateRegistry)` is generated from the `x-event-template` on `ObligationViolationReportedEvent` but is never called in `OnRunningAsync`. The `obligation_violation_reported` event template is not available at runtime for ABML `emit_event` actions. Needs a call in `OnRunningAsync` once the event template registry is available.

3. **Reference tracking helpers not called**: `RegisterCharacterReferenceAsync` and `UnregisterCharacterReferenceAsync` are generated from `x-references` but never invoked in service code. The cleanup callback IS registered (CASCADE policy works), but individual reference counts are not tracked. This means lib-resource cannot answer "does obligation have data for character X?" during pre-deletion checks. Only affects pre-deletion reference count queries, not actual cleanup execution.

4. **`locationId` on EvaluateActionRequest unused**: The schema defines an optional `locationId` field on `EvaluateActionRequest`, but the service code never reads `body.LocationId`. Planned for location-aware norm weighting (e.g., lawless districts reduce penalties). See Potential Extensions.

## Implementation Status

1. **Action Mapping CRUD** (`SetActionMapping`, `ListActionMappings`, `DeleteActionMapping`): Idempotent upsert with `CreatedAt`/`UpdatedAt` tracking, cursor-paginated listing with text search via JSON path queries, convention-based 1:1 fallback on delete.
2. **Obligation Query** (`QueryObligations`, `EvaluateAction`): Cache-backed contract querying with distributed locking and double-check pattern, personality-weighted cost computation with graceful degradation when character-personality unavailable.
3. **Violation Reporting** (`ReportViolation`, `QueryViolations`): Redis TTL-based idempotency, optional breach reporting to lib-contract (controlled by `BreachReportEnabled`), cursor-paginated history with contract/type/time-range filters.
4. **Cache Management** (`InvalidateCache`): Full cache rebuild from current contract state with distributed locking and observability event.
5. **Resource Cleanup** (`CleanupByCharacter`): Removes cached obligations and violation history via lib-resource CASCADE.
6. **Compression** (`GetCompressData`, `RestoreFromArchive`): Archive serialization via BannouJson; obligation cache rebuilt from active contracts on restore, not from archive data (intentional -- archived obligations may reference expired contracts).
7. **Event Handlers**: All 4 contract lifecycle handlers (`HandleContractActivated`, `HandleContractTerminated`, `HandleContractFulfilled`, `HandleContractExpired`) rebuild/clear obligation cache per character party. Terminated and expired handlers query contract for parties (event payload doesn't include them); activated and fulfilled handlers extract parties from the event directly.
8. **Variable Provider Factory**: `ObligationProviderFactory` registered as singleton, provides `${obligations.*}` variables to Actor via `IEnumerable<IVariableProviderFactory>` DI discovery. Empty provider returned on cache miss (graceful degradation).

---

## Potential Extensions

- **Cognition stage integration**: A 6th cognition stage (`evaluate_consequences`) inserted into the Actor behavior pipeline between `store_memory` and `evaluate_goal_impact`. Opt-in via `conscience: true` ABML metadata flag. The obligation service provides the data; the cognition stage integration lives in lib-actor/lib-behavior. This is the key remaining piece for the "second thoughts" feature to be active in NPC behavior.

- **Hearsay-filtered norm costs (planned)**: When lib-hearsay is available, obligation costs for faction norms should use belief-filtered penalties (`${hearsay.norm.believed_cost.<type>}`) instead of raw Faction penalties. This creates information asymmetry: NPCs in remote areas may not know about newly enacted laws, and NPCs in rumor-dense areas may overestimate penalties. The `NormResolutionMode` configuration provides the fallback behavior when Hearsay is unavailable: `PerfectKnowledge` queries Faction directly (NPCs know all norms instantly), `UncertaintySimulation` applies `NormUncertaintyVariance` as random +/- variance to penalties (a lightweight approximation of imperfect knowledge without Hearsay's full belief propagation and convergence system).

- **Location-aware norm weighting**: The `EvaluateAction` request accepts a `locationId` field, but location-based cost adjustment (e.g., lawless district reduces norm penalties) is not yet implemented. Could query lib-faction's norm resolution hierarchy or use location metadata directly to modulate base penalties per-location.

- **Species instinctual norms**: #410 envisioned species-specific norms (e.g., predator species has lower penalty for violence). These could be represented as species-based implicit contracts or integrated into the faction norm framework via species-faction associations.

- **Post-violation emotional feedback**: When knowing violations occur, downstream consumers could: increase stress/decrease comfort (emotional modification), record high-significance memories, trigger personality drift (`OATH_BROKEN`, `RESISTED_TEMPTATION`, `GUILTY_CONSCIENCE` experience types), and record encounter memories for violations involving other characters. The `obligation.violation.reported` event already publishes the full context needed for this.

- **Violation retention/pruning**: No retention policy exists for violation history. A background worker or configurable retention period could prune old violation records.

**Note on lib-moral**: The original #410 design envisioned a companion lib-moral service for social/cultural norm framework. This has been absorbed: personality-weighted moral reasoning is built into obligation's two-layer design, and the norm framework (faction-specific, location-dependent norms with violation types and base penalties) is provided by lib-faction. No separate lib-moral service is planned.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. **Hardcoded personality trait mapping**: The personality weight computation maps violation types to traits via a hardcoded static dictionary (10 entries: `theft`→honesty+conscientiousness, `deception`→honesty, `violence`→agreeableness, `honor_combat`→conscientiousness+loyalty, `betrayal`→loyalty, `exploitation`→agreeableness+honesty, `oath_breaking`→loyalty+conscientiousness, `trespass`→conscientiousness, `disrespect`→agreeableness, `contraband`→conscientiousness; everything else→conscientiousness). This is in tension with violation types being opaque strings that "grow organically" -- any new type falls through to the default, silently degrading moral reasoning quality. The mapping should be data-driven (part of action mapping store or behavioral clause definitions) or at minimum configurable.

2. **Event templates not registered at startup**: `ObligationEventTemplates.RegisterAll()` is generated but never called in `ObligationServicePlugin.OnRunningAsync`. The `obligation_violation_reported` event template is unavailable at runtime, meaning ABML `emit_event:obligation_violation_reported` actions would fail. Fix: add `ObligationEventTemplates.RegisterAll(registry)` call to `OnRunningAsync` (requires resolving `IEventTemplateRegistry` from DI).

3. **Reference tracking helpers never invoked**: `RegisterCharacterReferenceAsync` / `UnregisterCharacterReferenceAsync` are generated from `x-references` but never called when violations are recorded or cleaned up. The cleanup callback IS registered (CASCADE works), but lib-resource has no individual reference records for obligation→character. Pre-deletion reference count checks via `/resource/check` will not include obligation as a reference holder. Fix: call `RegisterCharacterReferenceAsync` when first recording a violation for a character, and `UnregisterCharacterReferenceAsync` during `CleanupByCharacter`.

### Intentional Quirks (Documented Behavior)

- **Violation types are opaque strings**: No separate taxonomy or enum is maintained for violation types. The vocabulary is defined entirely by contract templates' behavioral clause definitions (e.g., "theft", "deception", "violence", "honor_combat"). This allows the violation vocabulary to grow organically as new contract templates are authored.

- **1:1 convention-based tag matching as default**: When no explicit action mapping exists for a GOAP action tag, the tag name is used directly as the violation type code. Explicit mappings via `SetActionMapping` are only needed when vocabularies differ or when one action maps to multiple violation types.

- **Personality enrichment is fully optional**: The two-layer design means all obligation costs function without character-personality. When personality data is unavailable, `EvaluateAction` returns raw `basePenalty` values (i.e., `weightedPenalty == basePenalty`). The `moralWeightingApplied` response field indicates whether enrichment was applied.

- **Cache is event-driven, not TTL-only**: The obligation cache is rebuilt when contract lifecycle events fire (activated, terminated, fulfilled, expired). The `CacheTtlMinutes` provides a secondary expiration for stale entries that might not receive events (edge case). This means obligations update in near-real-time as contracts change.

- **Resource cleanup via x-references**: Character deletion triggers cleanup through lib-resource's `x-references` cascade mechanism, not via direct event subscription. This follows FOUNDATION TENETS for resource-managed cleanup.

- **Compression priority 25**: Obligation data is compressed at priority 25 during character archival, placing it after core character data but before encounter/history data in the compression pipeline.

- **DecodeCursor swallows all exceptions**: The cursor decoding method uses a bare `catch` that returns 0 on any failure (including malformed base64 or non-integer content). This is intentional — invalid cursors are treated as "start from beginning" rather than erroring. Matches the pattern used by other paginated Bannou services.

- **Terminated/expired event handlers query contract service for parties**: Unlike `ContractActivatedEvent` and `ContractFulfilledEvent` (which include `Parties` in the event payload), `ContractTerminatedEvent` and `ContractExpiredEvent` do not. Obligation must call `GetContractInstanceAsync` to discover affected character parties, adding an extra round-trip for these events.

- **Provider reads cache only (no rebuild)**: The `ObligationProviderFactory` reads from Redis cache directly. If the cache is empty for a character, it returns an empty provider (zero obligations) rather than triggering a cache rebuild. Cache population happens only through API calls (`QueryObligations`/`InvalidateCache`) or contract lifecycle events. This means a newly-started Obligation service with a cold cache will report zero obligations until the first query or contract event.

### Design Considerations (Requires Planning)

- **Contract behavioral clause format**: The obligation service parses `CustomTerms.behavioral_clauses` from contract instances, expecting objects with `clauseCode`, `violationType`, `basePenalty` (defaults to 0.0), and optional `description` fields. This format works but is not formally defined in lib-contract's schema -- it's a convention that both services must agree on. Defining a `BehavioralClause` schema extension in contract-api.yaml would formalize this handshake and enable validation.
<!-- AUDIT:NEEDS_DESIGN:2026-02-12:https://github.com/beyond-immersion/bannou-service/issues/410 -->

- **Faction-to-contract bridge**: Faction norms (violation types + base penalties + severity) need to flow into the obligation system through the contract pipeline. The #410 design represents social norms as implicit contract templates -- faction membership automatically creates contracts with behavioral clauses matching the faction's norms. The mechanism for this automatic contract creation on faction join/leave is not yet implemented. This is the bridge that makes faction norms visible to obligation's cost computation without a direct faction dependency.
<!-- AUDIT:NEEDS_DESIGN:2026-02-12:https://github.com/beyond-immersion/bannou-service/issues/410 -->

---

## Work Tracking

- [#410](https://github.com/beyond-immersion/bannou-service/issues/410) - Feature: Second Thoughts -- Prospective Consequence Evaluation for NPC Cognition (original design spec; lib-moral absorbed into lib-obligation + lib-faction)
