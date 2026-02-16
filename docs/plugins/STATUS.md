# Status Plugin Deep Dive

> **Plugin**: lib-status
> **Schema**: schemas/status-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFeatures
> **State Stores**: status-templates (MySQL), status-instances (MySQL), status-containers (MySQL), status-active-cache (Redis), status-seed-effects-cache (Redis), status-lock (Redis)

---

## Overview

Unified entity effects query layer (L4 GameFeatures) aggregating temporary contract-managed statuses and passive seed-derived capabilities into a single query point. Any system needing "what effects does this entity have" -- combat buffs, death penalties, divine blessings, subscription benefits -- queries lib-status. Follows the "items in inventories" pattern: status templates define effect definitions, status containers hold per-entity inventory containers, and granting a status creates an item instance in that container. Contract integration is optional per-template for complex lifecycle; simple TTL-based statuses use lib-item's native decay system. Internal-only, never internet-facing.

---

## The Unified Effects Layer (Architectural Target)

> **Status**: All 16 status endpoints are fully implemented. The two-source architecture (item-based + seed-derived), grant flow with stacking, contract integration, and seed effects cache are operational. lib-divine is the first active consumer. The broader vision described below -- Status as THE universal effects query point for everything from combat buffs to death penalties to divine blessings -- is the architectural target these systems serve.

### Status Is THE Single Query Point for "What Effects Does This Entity Have?"

Any system that needs to know about active effects on an entity -- combat buffs, death penalties, subscription benefits, divine blessings, environmental effects, curses, equipment bonuses, seed-derived passive capabilities -- queries Status through one unified API (`GetEffects`). The two-source architecture (item-based temporary effects + seed-derived persistent capabilities) exists specifically so that Status can answer this question comprehensively regardless of the effect's origin. This unification is critical: if combat systems query one service for buffs, divine systems query another for blessings, and progression systems query a third for passives, consumers need to know about every effect source. Status eliminates this by being the universal aggregation point.

### Death Penalty and Resurrection Are Core Arcadia Mechanics, Not Extensions

In Arcadia, death is transformation, not punishment. Death creates content -- compressed character archives become generative input for the Content Flywheel (ghosts, undead, quests, NPC memories, legacy mechanics). The death penalty status is a contract-backed effect with multiple resurrection conditions ("wait 30 seconds OR consume resurrection scroll OR reach a shrine") expressed through Contract's conditional milestone system. The death penalty status drives gameplay decisions (resurrection scrolls have real economic value because death penalties matter) and economy (the underworld itself offers gameplay during the penalty). This is a core use case for Status's contract integration, not a potential extension -- it's one of the primary reasons contract-backed statuses exist. The `onTick` pattern and conditional milestone resolution in Potential Extensions are also needed primarily for death penalty mechanics.

### Divine Blessings Complete the Cosmological Moral Feedback Loop

lib-divine is already Status's first consumer. The full vision: regional watcher god actors (running on Actor) observe NPC behavior patterns and spend divinity (a finite resource) on blessings for characters who impress them. These blessings are granted as temporary status effects via Status's grant API. A Blessing of Commerce from Hermes increases trade effectiveness. A Curse of Dishonor from Ares reduces combat prowess. Blessings expire, can be cleansed, and stack -- all using Status's existing stacking system. This creates the cosmological moral feedback loop: the morality system (Faction → Obligation → Actor cognition) drives NPC behavior → god actors observe that behavior → divine blessings/curses are granted via Status → those effects modify future NPC behavior through the variable provider. The loop closes.

### A Variable Provider for ABML Is a Critical Architectural Requirement

NPCs need to know their own status effects to make decisions. A poisoned NPC should seek healing. A blessed NPC should be more aggressive. A dead character's actor should transition to underworld behavior. `${status.is_dead}`, `${status.has_buff.<code>}`, `${status.poison_stacks}`, `${status.active_count}` are not nice-to-haves -- they are required integration points for the NPC intelligence stack. Every other data-providing L4 service (personality, encounters, history, faction, obligation) already has a variable provider factory. Status is the gap. Without it, NPC behavior cannot react to active effects, which breaks the divine blessing feedback loop and makes combat buffs/debuffs invisible to ABML behavior logic. This should be prioritized as a core requirement, not deferred as a potential extension.

### Entity-Agnostic Design Is Intentional and Must Be Preserved

Status uses opaque entity types (`entityType` as string, not enum) because effects apply to ANYTHING: characters, accounts, locations, realms, factions, dungeon cores. An environmental effect on a location (perpetual fog), a realm-wide curse, an account subscription benefit (double XP), a faction-wide morale boost -- all use the same Status primitives. The polymorphic ownership pattern (shared with Collection, License, Seed) ensures Status never needs to enumerate entity types from other layers. New entity types work automatically.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Persistence for templates, instances, containers (MySQL); active status cache, seed effects cache (Redis); distributed locks (Redis) |
| lib-state (`IDistributedLockProvider`) | Distributed locks for entity-level status mutations, template updates |
| lib-messaging (`IMessageBus`) | Publishing lifecycle events, custom status events, error events |
| lib-messaging (`IEventConsumer`) | Event subscription registration for `seed.capability.updated` |
| lib-inventory (`IInventoryClient`) | Status container creation and management; container deletion on cleanup (L2 hard dependency) |
| lib-item (`IItemClient`) | Status item instance creation, destruction, and template validation (L2 hard dependency) |
| lib-game-service (`IGameServiceClient`) | Validates game service existence for template and container scoping (L2 hard dependency) |
| lib-resource (`IResourceClient`) | Cleanup callback registration for character deletion (L1 hard dependency -- resolved in `StatusServicePlugin.OnRunningAsync`, not constructor) |
| lib-contract (`IContractClient`) | Contract lifecycle for statuses with `contractTemplateId` (L1 hard dependency -- constructor-injected) |
| lib-seed (`ISeedClient`) | Seed capability queries for unified effects layer (L2 **soft** dependency -- resolved at runtime via `IServiceProvider`; gated by `SeedEffectsEnabled`) |
| `ISeedEvolutionListener` (DI listener) | `StatusSeedEvolutionListener` registered as singleton; receives seed capability change notifications for cache invalidation |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-divine (L4) | First consumer -- grants Minor/Standard blessings as temporary status items; subscribes to `status.expired` and `status.removed` to detect when blessings expire or are cleansed |
| Combat systems (L4, planned) | Subscribe to `status.granted`, `status.removed`, `status.expired` to track active buffs/debuffs |
| lib-analytics (L4) | Subscribes to all status events for aggregate statistics |
| Any L4 needing "what effects does this entity have" | Calls `GetEffects` for unified item-based + seed-derived view |

---

## State Storage

**Store**: `status-templates` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `tpl:{statusTemplateId}` | `StatusTemplateModel` | Status template definition with category, stacking rules, item/contract references |
| `tpl:{gameServiceId}:{code}` | `StatusTemplateModel` | Dual-key for code-based lookup within a game service |

**Store**: `status-instances` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `inst:{statusInstanceId}` | `StatusInstanceModel` | Active status instance with entity, template code, stack count, source, contract/item references, metadata |

**Store**: `status-containers` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `ctr:{containerId}` | `StatusContainerModel` | Maps entity to its inventory container for a game service |
| `ctr:{entityId}:{entityType}:{gameServiceId}` | `StatusContainerModel` | Dual-key for entity-based container lookup |

**Store**: `status-active-cache` (Backend: Redis, prefix: `status:active`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `active:{entityId}:{entityType}` | `ActiveStatusCacheModel` | Cached list of active item-based statuses per entity; rebuilt from MySQL instances on cache miss |

**Store**: `status-seed-effects-cache` (Backend: Redis, prefix: `status:seed`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `seed:{entityId}:{entityType}` | `SeedEffectsCacheModel` | Cached seed-derived capability effects per entity; invalidated on `seed.capability.updated` events and `ISeedEvolutionListener` notifications |

**Store**: `status-lock` (Backend: Redis, prefix: `status:lock`)

| Key Pattern | Purpose |
|-------------|---------|
| `entity:{entityType}:{entityId}` | Distributed lock for status mutations (grant, remove, cleanse) |
| `tpl:{statusTemplateId}` | Distributed lock for template updates |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `status-template.created` | `StatusTemplateCreatedEvent` | New status template created via `CreateStatusTemplateAsync` |
| `status-template.updated` | `StatusTemplateUpdatedEvent` | Status template fields updated; includes `ChangedFields` list |
| `status-template.deleted` | `StatusTemplateDeletedEvent` | **Never published** -- generated by `x-lifecycle` but no delete endpoint exists |
| `status.granted` | `StatusGrantedEvent` | Status effect applied to an entity; includes `GrantResult` (granted, stacked, refreshed, replaced) |
| `status.removed` | `StatusRemovedEvent` | Status effect removed from an entity; includes `StatusRemoveReason` |
| `status.expired` | `StatusExpiredEvent` | Status effect expired via TTL (item decay) or contract timeout |
| `status.stacked` | `StatusStackedEvent` | Status stack count changed; includes `OldStackCount` and `NewStackCount` |
| `status.grant-failed` | `StatusGrantFailedEvent` | Grant attempt rejected; includes `GrantFailureReason` |
| `status.cleansed` | `StatusCleansedEvent` | Bulk category cleanse; includes category and count removed |

### Consumed Events

| Topic | Event Type | Handler | Notes |
|-------|-----------|---------|-------|
| `seed.capability.updated` | `SeedCapabilityUpdatedEvent` | `HandleSeedCapabilityUpdated` | Invalidate seed effects cache for affected entity |
| `item.expired` | `ItemExpiredEvent` | `HandleItemExpired` | **Blocked on #407** -- commented out in schema. When implemented: clean up status instance record, invalidate cache, publish `status.expired` |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `MaxStatusesPerEntity` | `STATUS_MAX_STATUSES_PER_ENTITY` | `50` | Maximum concurrent active statuses per entity |
| `MaxStacksPerStatus` | `STATUS_MAX_STACKS_PER_STATUS` | `10` | Global maximum stack count per status (template `maxStacks` takes precedence if lower) |
| `MaxStatusTemplatesPerGameService` | `STATUS_MAX_STATUS_TEMPLATES_PER_GAME_SERVICE` | `200` | Maximum status template definitions per game service |
| `StatusCacheTtlSeconds` | `STATUS_STATUS_CACHE_TTL_SECONDS` | `60` | TTL for active status cache per entity (short -- statuses change frequently) |
| `SeedEffectsCacheTtlSeconds` | `STATUS_SEED_EFFECTS_CACHE_TTL_SECONDS` | `300` | TTL for seed-derived effects cache (longer -- capabilities change less frequently) |
| `MaxCachedEntities` | `STATUS_MAX_CACHED_ENTITIES` | `10000` | Maximum entity active-status cache entries retained in Redis (LRU eviction when exceeded) |
| `CacheWarmingEnabled` | `STATUS_CACHE_WARMING_ENABLED` | `false` | Enable proactive cache warming on startup for recently active entities |
| `LockTimeoutSeconds` | `STATUS_LOCK_TIMEOUT_SECONDS` | `30` | TTL for distributed locks on status mutations |
| `LockAcquisitionTimeoutSeconds` | `STATUS_LOCK_ACQUISITION_TIMEOUT_SECONDS` | `5` | Maximum seconds to wait when acquiring a distributed lock |
| `DefaultPageSize` | `STATUS_DEFAULT_PAGE_SIZE` | `50` | Default page size for paginated queries |
| `DefaultStatusDurationSeconds` | `STATUS_DEFAULT_STATUS_DURATION_SECONDS` | `60` | Default duration when template has no `defaultDurationSeconds` and no contract |
| `SeedEffectsEnabled` | `STATUS_SEED_EFFECTS_ENABLED` | `true` | Enable seed-derived passive effects in unified queries (disable if Seed is not deployed) |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<StatusService>` | Structured logging |
| `StatusServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access for MySQL and Redis stores |
| `IMessageBus` | Event publishing and error event publication |
| `IDistributedLockProvider` | Distributed locks for mutation operations |
| `IEventConsumer` | Event subscription registration for consumed events |
| `StatusSeedEvolutionListener` | Implements `ISeedEvolutionListener` (registered as singleton); invalidates seed effects cache on phase changes and capability changes |
| `IInventoryClient` | Status container creation and deletion (L2 hard) |
| `IItemClient` | Status item instance CRUD and template validation (L2 hard) |
| `IContractClient` | Contract instance creation and termination for contract-backed statuses (L1 hard) |
| `IGameServiceClient` | Game service existence validation (L2 hard) |
| `IServiceProvider` | Runtime resolution of soft dependency (`ISeedClient`) |

---

## API Endpoints (Implementation Notes)

### Template Management (6 endpoints)

`CreateStatusTemplateAsync` validates the game service exists via `IGameServiceClient`, validates `itemTemplateId` exists via `IItemClient`, checks code uniqueness per game service, enforces `MaxStatusTemplatesPerGameService`, and saves with dual keys (by ID and by `gameServiceId:code`). Publishes `status-template.created` lifecycle event.

`GetStatusTemplateAsync` loads from MySQL by ID. `GetStatusTemplateByCodeAsync` loads by the `gameServiceId:code` dual key. Both return 404 if not found.

`ListStatusTemplatesAsync` performs paged JSON query with required `gameServiceId` filter, optional `category` filter, sorted by code ascending.

`UpdateStatusTemplateAsync` acquires a distributed lock on the template, loads, validates, applies non-null fields only (partial update pattern). Clamps `MaxStacks` to `MaxStacksPerStatus` config. Publishes `status-template.updated` lifecycle event with `changedFields`.

`SeedStatusTemplatesAsync` performs bulk creation, skipping templates whose code already exists or whose item template doesn't exist (idempotent). Returns created/skipped counts.

### Status Operations (7 endpoints)

`GrantStatusAsync` is the core operation -- see the **Grant Flow** section below. Looks up template by `gameServiceId:code`, acquires entity-level distributed lock, checks for existing instances of the same template code, delegates to `HandleStackingAsync` if any exist, otherwise checks entity-wide status limit, then calls `CreateNewStatusInstanceAsync`.

`RemoveStatusAsync` loads instance by ID, acquires entity lock, calls `RemoveInstanceInternalAsync` (destroys item, terminates contract if present, deletes record, invalidates cache, publishes `status.removed`).

`RemoveBySourceAsync` acquires entity lock, queries instances by `entityId` + `entityType` + `sourceId`, removes each via `RemoveInstanceInternalAsync`.

`RemoveByCategoryAsync` acquires entity lock, queries instances by `entityId` + `entityType` + `category`, removes each, then publishes a single `status.cleansed` batch event with the total count.

`HasStatusAsync` checks active cache (rebuilt from MySQL on miss), filters out expired entries, returns bool + instance ID + stack count if found.

`ListStatusesAsync` loads active cache (rebuilt on miss), filters expired and optionally by category. If `includePassive` and `SeedEffectsEnabled`: also loads seed effects cache (rebuilt from ISeedClient on miss). Merges and paginates in-memory.

`GetStatusAsync` loads instance by ID from MySQL. 404 if not found.

### Effects Query (2 endpoints)

`GetEffectsAsync` loads both active status cache and seed effects cache, merges into unified response with source attribution (`EffectSource.item_based` vs `EffectSource.seed_derived`), returns counts and effects array.

`GetSeedEffectsAsync` loads seed effects cache only (rebuilt on miss via `ISeedClient`). Returns seed-derived capabilities with domain, fidelity, and seed attribution.

### Cleanup (1 endpoint)

`CleanupByOwnerAsync` queries all containers for `ownerType` + `ownerId`. For each: deletes inventory container (cascades to items), deletes all instance records, deletes container record, invalidates caches. Returns count of removed statuses and deleted containers. Called by lib-resource cleanup callbacks.

---

## Grant Flow (Core Operation)

The `GrantStatusAsync` flow is the most complex operation:

1. Acquire distributed lock: `entity:{entityType}:{entityId}`
2. Load status template by `statusTemplateCode` + `gameServiceId`
3. Find or auto-create status container for entity:
   - Query containers by `entityId` + `entityType` + `gameServiceId`
   - If none: create inventory container (type: `status_effects`, ownerType: `Other`, constraint: unlimited), save container record with dual keys
4. Query existing instances for entity + template code from MySQL
5. If existing instances found, apply stacking behavior:
   - `ignore`: return existing, publish `status.grant-failed`
   - `replace`: remove existing (destroy item, cancel contract, delete record), proceed with new grant
   - `refresh_duration` / `increase_intensity`: increment stack count, reset timer on item, cancel + recreate contract if applicable, publish `status.stacked`
   - `independent`: check `maxStacks` limit, create entirely new item + contract + instance record per stack
6. Check `MaxStatusesPerEntity` limit
7. Calculate `expiresAt`: `durationOverrideSeconds` > template `defaultDurationSeconds` > config `DefaultStatusDurationSeconds` > null (permanent)
8. **Create item instance** (saga-ordered -- easily reversible):
   - `IItemClient.CreateItemInstanceAsync(templateId, containerId, realmId=gameServiceId, quantity=1)`
   - Note: `RealmId` is set to `GameServiceId` as a partition key (per Collection pattern), not as an actual realm reference
9. **Contract lifecycle** (if template has `contractTemplateId` -- soft, skip if `IContractClient` unavailable):
   - Create contract instance with saga compensation (destroy item if contract fails)
   - Set template values from grant request metadata
   - Auto-propose + consent (single-party)
   - Complete "apply" milestone (triggers prebound APIs)
10. Save `StatusInstanceModel` to MySQL
11. Invalidate active status cache (cache will be rebuilt from MySQL on next read)
12. Publish `status.granted` event
13. Return success response

---

## Visual Aid

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    Status Service Architecture                          │
│                                                                         │
│  ┌─────────────┐    ┌──────────────┐    ┌──────────────────────────┐   │
│  │ Caller (L4) │───>│ GrantStatus  │───>│ Template Lookup (MySQL)  │   │
│  └─────────────┘    └──────┬───────┘    └──────────────────────────┘   │
│                            │                                            │
│                            ▼                                            │
│                   ┌────────────────┐                                    │
│                   │ Find/Create    │──> IInventoryClient.               │
│                   │ Container      │    CreateContainerAsync             │
│                   └────────┬───────┘    (type: status_effects)          │
│                            │                                            │
│                            ▼                                            │
│              ┌─────────────────────────┐                                │
│              │ Check Stacking Behavior │                                │
│              │ ignore/replace/refresh/ │                                │
│              │ increase/independent    │                                │
│              └────────────┬────────────┘                                │
│                           │                                             │
│              ┌────────────▼────────────┐                                │
│              │ Create Item (saga step) │──> IItemClient.                │
│              │ (reversible on failure) │    CreateItemInstanceAsync      │
│              └────────────┬────────────┘    (with expiresAt for TTL)    │
│                           │                                             │
│              ┌────────────▼────────────┐                                │
│              │ Create Contract (soft)  │──> IContractClient (optional)  │
│              │ (compensate item on     │    (saga: destroy item on      │
│              │  failure)               │     contract failure)           │
│              └────────────┬────────────┘                                │
│                           │                                             │
│              ┌────────────▼────────────┐   ┌──────────────────────────┐│
│              │ Save Instance (MySQL)   │   │ Active Cache (Redis)     ││
│              │ + Invalidate Cache      │──>│ Rebuilt on miss from     ││
│              └────────────┬────────────┘   │ MySQL instances          ││
│                           │                └──────────────────────────┘│
│                           ▼                                            │
│              ┌─────────────────────────┐                               │
│              │ Publish status.granted  │──> IMessageBus                │
│              └─────────────────────────┘                               │
│                                                                        │
│  ═══════════════════════════════════════════════════════════════════    │
│  UNIFIED EFFECTS QUERY                                                 │
│                                                                        │
│  ┌─────────────┐    ┌──────────────────┐    ┌─────────────────────┐   │
│  │ GetEffects  │───>│ Active Cache     │    │ Seed Effects Cache  │   │
│  │ (unified)   │    │ (item-based)     │    │ (seed-derived)      │   │
│  └─────────────┘    │ Redis TTL: 60s   │    │ Redis TTL: 300s     │   │
│         │           └────────┬─────────┘    └──────────┬──────────┘   │
│         │                    │                          │              │
│         │           On miss: │ rebuild from    On miss: │ query        │
│         │                    │ MySQL instances          │ ISeedClient  │
│         │                    ▼                          ▼              │
│         └─────> Merge with EffectSource attribution                   │
│                 (item_based vs seed_derived)                           │
│                                                                        │
│  ═══════════════════════════════════════════════════════════════════    │
│  EXPIRATION (blocked on #407)                                          │
│                                                                        │
│  lib-item decay worker ──> item.expired event ──> Status cleans up    │
│  (manages expiresAt)       (not yet published)    instance records     │
│                                                    + publishes          │
│                                                    status.expired       │
│                                                                        │
│  Contract-backed statuses: contract prebound API calls /status/remove  │
│  on milestone expiry (Status never polls contracts)                    │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

All 16 API endpoints are fully implemented. The remaining stub is the `item.expired` event subscription.

### Blocking Dependency

- **#407 (Item Decay/Expiration System)**: The `item.expired` event subscription is commented out in the events schema. lib-item does not yet publish `item.expired` events. Until #407 is implemented, TTL-based status expiration relies on **lazy expiration during cache rebuild** -- when `GetOrBuildActiveCacheAsync` encounters expired instances in MySQL, it deletes them and publishes `status.expired`. This means expired statuses persist in MySQL until the next cache miss for that entity. Contract-backed statuses still work independently (contract prebound APIs call `/status/remove` on expiry).
<!-- AUDIT:BLOCKED:2026-02-13 -->

### Missing Implementation

- **DeleteStatusTemplate endpoint**: The `x-lifecycle` declaration generates `StatusTemplateDeletedEvent`, and the event is listed in `x-event-publications`, but there is no delete endpoint in the API schema or service interface. Template deletion is not implemented.
<!-- AUDIT:NEEDS_DESIGN:2026-02-12:https://github.com/beyond-immersion/bannou-service/issues/411 -->

- **Cache warming**: `CacheWarmingEnabled` config property exists and is checked in the constructor (logs a message when true), but no actual cache warming logic is implemented. Setting it to `true` has no functional effect beyond the log message.
<!-- AUDIT:NEEDS_DESIGN:2026-02-12:https://github.com/beyond-immersion/bannou-service/issues/412 -->

- **MaxCachedEntities LRU eviction**: The `MaxCachedEntities` config (default 10,000) is referenced in the constructor log message but is not enforced. Redis TTL handles individual entry expiration, but there is no mechanism to enforce the 10,000 entry cap. The code comments note "MaxCachedEntities is enforced by Redis eviction policy" but this requires external Redis `maxmemory-policy` configuration.
<!-- AUDIT:NEEDS_DESIGN:2026-02-12:https://github.com/beyond-immersion/bannou-service/issues/412 -->

---

## Potential Extensions

- **Tick-based effects (DOT/HOT)**: Repeating actions during a status duration (damage every 3s, healing every 5s). Requires either contract milestone ticking support or a dedicated tick worker. The `onTick` pattern from #282 defines the interface; contract prebound APIs handle execution.
<!-- AUDIT:NEEDS_DESIGN:2026-02-12:https://github.com/beyond-immersion/bannou-service/issues/417 -->

- **Conditional milestone completion**: Death penalty resurrection conditions ("wait 30s OR use resurrection scroll OR reach shrine"). Requires Contract's conditional milestone resolution (`anyOf` condition types).
<!-- AUDIT:NEEDS_DESIGN:2026-02-13:https://github.com/beyond-immersion/bannou-service/issues/419 -->

- **Subscription renewal flows**: Premium subscription auto-renewal with `checkEndpoint` for billing verification. Requires contract renewal milestone pattern.
<!-- AUDIT:NEEDS_DESIGN:2026-02-13:https://github.com/beyond-immersion/bannou-service/issues/421 -->

- **Source tracking cascading**: Premium subscription granting child statuses (double-xp, cosmetics access). On parent removal, cascade-remove children via `RemoveBySourceAsync`. The endpoint exists but the subscription-creates-children workflow is deferred.
<!-- AUDIT:NEEDS_DESIGN:2026-02-13:https://github.com/beyond-immersion/bannou-service/issues/423 -->

- **IStatusEffectProvider DI interface**: If L2 services ever need status data (e.g., Character needs "is dead?" checks), add `IStatusEffectProvider` in `bannou-service/Providers/` with DI inversion. Status implements it; Character discovers via `IEnumerable<IStatusEffectProvider>`.
<!-- AUDIT:NEEDS_DESIGN:2026-02-13:https://github.com/beyond-immersion/bannou-service/issues/425 -->

- **Client events**: `status-client-events.yaml` for pushing status change notifications to connected clients (buff applied, buff expired, death state entered).
<!-- AUDIT:NEEDS_DESIGN:2026-02-13:https://github.com/beyond-immersion/bannou-service/issues/426 -->

- **Variable provider factory**: `IStatusVariableProviderFactory` for ABML behavior expressions (`${status.has_buff}`, `${status.is_dead}`, `${status.poison_stacks}`).

- **Effect magnitude computation**: Status templates define base magnitudes; stacking computes actual magnitude from base * stackCount * fidelity. Requires typed effect definitions beyond MVP scope.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. ~~**Missing account cleanup callback registration**~~: **FIXED** (2026-02-12) - Added account cleanup callback registration in `StatusServicePlugin.RegisterResourceCleanupCallbacksAsync`. Both character and account callbacks now register independently with separate try-catch blocks. The `CleanupByOwnerAsync` endpoint was already polymorphic and required no changes.

2. ~~**SeedStatusTemplatesAsync skips item template validation**~~: **FIXED** (2026-02-12) - Added item template validation in `SeedStatusTemplatesAsync`. Templates referencing non-existent item templates are now skipped with a warning log and counted as "skipped" in the response, consistent with the idempotent seeding pattern.

3. ~~**SeedStatusTemplatesAsync skips game service validation**~~: **FIXED** (2026-02-12) - Added `_gameServiceClient.GetServiceAsync` validation at the top of `SeedStatusTemplatesAsync`, matching `CreateStatusTemplateAsync` pattern. Returns 404 if the game service doesn't exist, preventing orphaned templates.

### Intentional Quirks (Documented Behavior)

- **Polymorphic ownership with opaque entity types**: Entity types are opaque strings (e.g., "character", "account"), NOT enums. This follows the Collection/License/Seed pattern and avoids enumerating entity types from other layers. Status does NOT validate entity existence -- the caller is responsible for ensuring the entity exists before granting statuses.

- **No Status-specific expiration worker**: Time-based expiration is entirely delegated to lib-item's native decay system (#407). Status subscribes to `item.expired` events to clean up its own records. For contract-backed statuses, the contract template includes prebound APIs that call `/status/remove` on milestone expiry. Status never polls for expirations -- it is always event-driven.

- **Containers auto-created on first grant**: Status containers are created lazily when the first status is granted to an entity for a given game service. There is no explicit "create container" endpoint. This matches the Collection pattern and reduces API surface for consumers.

- **Active cache invalidation is full-entity, not surgical**: Any status change (grant, remove, stack, cleanse) invalidates the entire entity's active cache entry. The cache is rebuilt from MySQL instances on next read. Cache TTL is short (60s default) to minimize staleness. This is simpler and safer than surgical updates.

- **Seed effects gated by config**: The `SeedEffectsEnabled` configuration flag completely disables seed-derived effects in unified queries. When false, `ISeedClient` is never resolved, `ISeedEvolutionListener` callbacks are no-ops, and `GetEffects`/`ListStatuses` return only item-based effects. This allows deployments without lib-seed.

- **Contract integration is hard but usage is conditional**: `IContractClient` is constructor-injected (L1 hard dependency, always available). Contract creation only occurs when the template has a `contractTemplateId`. If contract creation fails during grant, the saga pattern compensates by destroying the already-created item instance, and the grant returns a failure response with `GrantFailureReason.ContractFailed`.

- **Lazy expiration during cache rebuild**: Since the `item.expired` event (#407) is not yet available, expired statuses are cleaned up opportunistically when `GetOrBuildActiveCacheAsync` rebuilds the active cache from MySQL. Expired instances found during rebuild are deleted from MySQL, and `status.expired` events are published. This means expired statuses survive in MySQL until the next cache miss for that entity (default 60s cache TTL).

- **Stacking behavior `ignore` publishes a failure event**: When a template uses `ignore` stacking and the status is already present, the grant is rejected and a `StatusGrantFailedEvent` is published with `GrantFailureReason.stack_behavior_ignore`. The existing status instance ID is included in the failure response.

### Design Considerations (Requires Planning)

- **Item template per status template**: Each status template requires an `itemTemplateId` reference. This means game designers must create item templates for every status type before creating status templates. The relationship between item template properties and status behavior needs documentation.

- **Hardcoded `EntityType.Character` in contract creation**: `CreateNewStatusInstanceAsync` hardcodes `EntityType.Character` as the contract party entity type (line 1261), but statuses can be granted to any entity type (polymorphic ownership). Contract creation will set the wrong entity type for non-character entities (e.g., accounts with subscription statuses).
<!-- AUDIT:NEEDS_DESIGN:2026-02-12:https://github.com/beyond-immersion/bannou-service/issues/415 -->

- **Hardcoded `EntityType.Character` in contract termination**: `RemoveInstanceInternalAsync` also hardcodes `EntityType.Character` as the `RequestingEntityType` for `TerminateContractInstanceAsync` (line 1440). Same issue as above.
<!-- AUDIT:NEEDS_DESIGN:2026-02-12:https://github.com/beyond-immersion/bannou-service/issues/415 -->

---

## Integration Points Summary

### Outbound (Status calls)

| Target | API | When |
|--------|-----|------|
| lib-inventory (L2) | `CreateContainerAsync` | First grant for an entity/game-service pair (auto-create, type: `status_effects`, constraint: unlimited) |
| lib-inventory (L2) | `DeleteContainerAsync` | Cleanup by owner (cascades to items) |
| lib-item (L2) | `CreateItemInstanceAsync` | Every grant (create status item with `expiresAt`) |
| lib-item (L2) | `DestroyItemInstanceAsync` | Remove, expire, replace, cleanup |
| lib-item (L2) | `GetItemTemplateAsync` | Template creation (validate item template exists) |
| lib-game-service (L2) | `GetGameServiceAsync` | Template creation (validate game service exists) |
| lib-resource (L1) | `RegisterCleanupCallbackAsync` | On startup (register for character + account deletion) |
| lib-contract (L1) | `CreateContractInstanceAsync` | Grant with `contractTemplateId` |
| lib-contract (L1) | `TerminateContractInstanceAsync` | Remove contract-backed status |
| lib-seed (L2, soft) | `GetSeedsByOwnerAsync` | Rebuild seed effects cache (list seeds for entity) |
| lib-seed (L2, soft) | `GetCapabilityManifestAsync` | Rebuild seed effects cache (get capabilities per seed) |
| lib-seed (L2, soft) | `GetSeedAsync` | Seed effects cache invalidation via event (look up seed owner) |

### Inbound (Others call Status)

| Caller | Endpoint | When |
|--------|----------|------|
| lib-divine (L4) | `/status/grant` | Grant blessing as temporary status |
| lib-divine (L4) | `/status/remove` | Remove blessing on revocation |
| lib-divine (L4) | `/status/has` | Check if entity has a specific blessing |
| lib-resource (L1) | `/status/cleanup-by-owner` | Character or account deletion cleanup callback |
| Combat systems (L4, planned) | `/status/grant`, `/status/remove`, `/status/list` | Buff/debuff management |

---

## Work Tracking

### Active

- [#282](https://github.com/beyond-immersion/bannou-service/issues/282) - Status service design (stacking model, five modes)
- [#407](https://github.com/beyond-immersion/bannou-service/issues/407) - **BLOCKING**: Item Decay/Expiration System (required for proactive TTL-based status expiration; lazy expiration during cache rebuild is the interim workaround)

### Completed

- [#375](https://github.com/beyond-immersion/bannou-service/issues/375) - Pipeline architecture (Collection -> Seed -> Status) -- implemented
- [#280](https://github.com/beyond-immersion/bannou-service/issues/280) - Itemize anything pattern -- implemented
- **Account cleanup callback registration** (2026-02-12) - Fixed missing account cleanup callback in `StatusServicePlugin.OnRunningAsync`
- **SeedStatusTemplatesAsync item template validation** (2026-02-12) - Added item template validation in seed endpoint
- **SeedStatusTemplatesAsync game service validation** (2026-02-12) - Added game service existence validation in seed endpoint
