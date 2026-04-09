# Status Plugin Deep Dive

> **Plugin**: lib-status
> **Schema**: schemas/status-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFeatures
> **State Stores**: status-templates (MySQL), status-instances (MySQL), status-containers (MySQL), status-active-cache (Redis), status-seed-effects-cache (Redis), status-lock (Redis)
> **Short**: Unified entity effects query layer aggregating contract statuses and seed capabilities

---

## Overview

Unified entity effects query layer (L4 GameFeatures) aggregating temporary contract-managed statuses and passive seed-derived capabilities into a single query point. Any system needing "what effects does this entity have" -- combat buffs, death penalties, divine blessings, subscription benefits -- queries lib-status. Follows the "items in inventories" pattern: status templates define effect definitions, status containers hold per-entity inventory containers, and granting a status creates an item instance in that container. Contract integration is optional per-template for complex lifecycle; simple TTL-based statuses use lib-item's native decay system. Internal-only, never internet-facing.

---

## The Unified Effects Layer (Architectural Target)

> **Status**: All 19 status endpoints are fully implemented (including Category A deprecation lifecycle). The two-source architecture (item-based + seed-derived), grant flow with stacking, contract integration, and seed effects cache are operational. lib-divine is the first active consumer. The broader vision described below -- Status as THE universal effects query point for everything from combat buffs to death penalties to divine blessings -- is the architectural target these systems serve.

### Status Is THE Single Query Point for "What Effects Does This Entity Have?"

Any system that needs to know about active effects on an entity -- combat buffs, death penalties, subscription benefits, divine blessings, environmental effects, curses, equipment bonuses, seed-derived passive capabilities -- queries Status through one unified API (`GetEffects`). The two-source architecture (item-based temporary effects + seed-derived persistent capabilities) exists specifically so that Status can answer this question comprehensively regardless of the effect's origin. This unification is critical: if combat systems query one service for buffs, divine systems query another for blessings, and progression systems query a third for passives, consumers need to know about every effect source. Status eliminates this by being the universal aggregation point.

### Death Penalty and Resurrection Are Core Arcadia Mechanics, Not Extensions

In Arcadia, death is transformation, not punishment. Death creates content -- compressed character archives become generative input for the Content Flywheel (ghosts, undead, quests, NPC memories, legacy mechanics). The death penalty status is a contract-backed effect with multiple resurrection conditions ("wait 30 seconds OR consume resurrection scroll OR reach a shrine") expressed through Contract's conditional milestone system. The death penalty status drives gameplay decisions (resurrection scrolls have real economic value because death penalties matter) and economy (the underworld itself offers gameplay during the penalty). This is a core use case for Status's contract integration, not a potential extension -- it's one of the primary reasons contract-backed statuses exist. The `onTick` pattern and conditional milestone resolution in Potential Extensions are also needed primarily for death penalty mechanics.

### Equipment Intent Enchantments as a Status Source

Equipment carrying Type 2 intent-channeling enchantments (charms and curses) produces Status effects on the wielder while equipped, scaled by environmental pneuma density. These effects use `sourceId` set to the item instance ID, enabling cascade removal via `RemoveBySourceAsync` on unequip. Status templates follow the naming convention `ENCHANT_CHARM_{effect_code}` and `ENCHANT_CURSE_{effect_code}`. Environmental mana tier transitions (dead/thin/normal/rich) produce additional Status effects (`ENCHANT_MANA_STARVED`, `ENCHANT_MANA_DIMINISHED`, `ENCHANT_MANA_SATURATED`) that modify or override the equipment enchantment effects. See [EQUIPMENT-ENCHANTMENT-DUALITY.md](../planning/EQUIPMENT-ENCHANTMENT-DUALITY.md) for the complete design.

### Divine Blessings Complete the Cosmological Moral Feedback Loop

lib-divine is already Status's first consumer. The full vision: regional watcher god actors (running on Actor) observe NPC behavior patterns and spend divinity (a finite resource) on blessings for characters who impress them. These blessings are granted as temporary status effects via Status's grant API. A Blessing of Commerce from a commerce-domain god increases trade effectiveness. A Curse of Dishonor from a war-domain god reduces combat prowess. Blessings expire, can be cleansed, and stack -- all using Status's existing stacking system. This creates the cosmological moral feedback loop: the morality system (Faction → Obligation → Actor cognition) drives NPC behavior → god actors observe that behavior → divine blessings/curses are granted via Status → those effects modify future NPC behavior through the variable provider. The loop closes.

### A Variable Provider for ABML Is a Critical Architectural Requirement

NPCs need to know their own status effects to make decisions. A poisoned NPC should seek healing. A blessed NPC should be more aggressive. A dead character's actor should transition to underworld behavior. `${status.is_dead}`, `${status.has_buff.<code>}`, `${status.poison_stacks}`, `${status.active_count}` are not nice-to-haves -- they are required integration points for the NPC intelligence stack. Every other data-providing L4 service (personality, encounters, history, faction, obligation) already has a variable provider factory. Status is the gap. Without it, NPC behavior cannot react to active effects, which breaks the divine blessing feedback loop and makes combat buffs/debuffs invisible to ABML behavior logic. This should be prioritized as a core requirement, not deferred as a potential extension.

### Entity-Agnostic Design Is Intentional and Must Be Preserved

Status uses the shared `EntityType` enum from `common-api.yaml` because effects apply to ANYTHING: characters, accounts, locations, realms, factions, dungeon cores. An environmental effect on a location (perpetual fog), a realm-wide curse, an account subscription benefit (double XP), a faction-wide morale boost -- all use the same Status primitives. The polymorphic ownership pattern (shared with Collection, License, Seed) uses the shared enum per IMPLEMENTATION TENETS.

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
| lib-seed (`ISeedClient`) | Seed capability queries for unified effects layer (L2 **hard** dependency -- constructor-injected; feature gated by `SeedEffectsEnabled` config) |
| lib-connect (`IEntitySessionRegistry`) | Entity-to-session resolution for pushing client events to WebSocket sessions observing affected entities (L1 hard dependency) |
| lib-telemetry (`ITelemetryProvider`) | Span instrumentation for async methods per IMPLEMENTATION TENETS (L0 hard dependency) |
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

### Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `entityType` | A (Entity Reference) | `EntityType` enum | Identifies what kind of entity has a status effect (character, account, location, realm, faction, etc.). All valid values are first-class Bannou entities. Recently migrated from opaque string to the shared `EntityType` enum from `common-api.yaml`. |
| `statusTemplateCode` | B (Game Content Type) | Opaque string | Identifies a specific status definition within a game service (e.g., `"blessing_of_commerce"`, `"poison_tier_2"`, `"death_penalty"`). Vocabulary defined per game at deployment time via template seeding. New codes require no schema changes. |
| `category` | C (System State/Mode) | `StatusCategory` enum | Effect classification (`buff`, `debuff`, `death`, `subscription`, `event`, `passive`). Used for filtering and category-targeted cleanse operations. Finite set of system-defined categories. |
| `stackBehavior` | C (System State/Mode) | `StackBehavior` enum | How multiple applications interact (`refresh_duration`, `independent`, `increase_intensity`, `replace`, `ignore`). Template-level configuration controlling grant resolution. |
| `effectSource` | C (System State/Mode) | `EffectSource` enum | Whether an effect originates from an item or a seed capability (`item_based`, `seed_derived`). Source attribution in unified effects queries. |
| `reason` (on `StatusRemovedEvent`, `StatusCleansedEvent`) | C (System State/Mode) | `StatusRemoveReason` enum | Why a status was removed (`expired`, `cleansed`, `cancelled`, `source_removed`, `admin`). Service-specific removal classification. |
| `reason` (on `StatusGrantFailedEvent`) | C (System State/Mode) | `GrantFailureReason` enum | Why a grant was rejected (`template_not_found`, `template_deprecated`, `entity_at_max_statuses`, `stack_limit_reached`, `stack_behavior_ignore`, `contract_failed`, `item_creation_failed`). |
| `grantResult` | C (System State/Mode) | `GrantResult` enum | How a successful grant was resolved (`granted`, `stacked`, `refreshed`, `replaced`). Outcome classification for the grant flow. |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `status.template.created` | `StatusTemplateCreatedEvent` | New status template created via `CreateStatusTemplateAsync` |
| `status.template.updated` | `StatusTemplateUpdatedEvent` | Status template fields updated; includes `ChangedFields` list |
| `status.template.deleted` | `StatusTemplateDeletedEvent` | Status template deleted via `DeleteStatusTemplateAsync` (requires prior deprecation) |
| `status.instance.granted` | `StatusGrantedEvent` | Status effect applied to an entity; includes `GrantResult` (granted, stacked, refreshed, replaced) |
| `status.instance.removed` | `StatusRemovedEvent` | Status effect removed from an entity; includes `StatusRemoveReason` |
| `status.instance.expired` | `StatusExpiredEvent` | Status effect expired via TTL (item decay) or contract timeout |
| `status.instance.stacked` | `StatusStackedEvent` | Status stack count changed; includes `OldStackCount` and `NewStackCount` |
| `status.instance.grant-failed` | `StatusGrantFailedEvent` | Grant attempt rejected; includes `GrantFailureReason` |
| `status.instance.cleansed` | `StatusCleansedEvent` | Bulk category cleanse; includes category and count removed |

### Consumed Events

| Topic | Event Type | Handler | Notes |
|-------|-----------|---------|-------|
| `seed.capability.updated` | `SeedCapabilityUpdatedEvent` | `HandleSeedCapabilityUpdated` | Invalidate seed effects cache for affected entity |
| `item.expired` | `ItemExpiredEvent` | `HandleItemExpired` | **Blocked on #407** -- commented out in schema. When implemented: clean up status instance record, invalidate cache, publish `status.expired` |

---

## Client Events

Server-to-client push events delivered via WebSocket through the Entity Session Registry (L1).

| Event | Schema | Trigger |
|-------|--------|---------|
| `status.effect.changed` | `StatusEffectChangedClientEvent` | Any status mutation: grant, remove, expire, stack, or cleanse |

**Schema**: `schemas/status-client-events.yaml`

**Routing**: Uses the entity's own type (e.g., `"character"`) with the entity's ID, so sessions already watching a character for inventory/collection changes also receive status effect updates. This is the same entity routing key used by Inventory and Collection.

**Change types** (via `StatusChangeType` discriminator):
- `granted` — New status effect applied to entity
- `removed` — Status explicitly removed (by source, admin, or cancellation)
- `expired` — Status TTL elapsed (lazy expiration during cache rebuild)
- `stacked` — Stack count increased or duration refreshed on existing status
- `cleansed` — Status removed by category cleanse mechanic

**Batch operations**: `RemoveBySourceAsync` and `RemoveByCategoryAsync` publish one client event per removed instance (each goes through `RemoveInstanceInternalAsync` which publishes individually).

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `MaxStatusesPerEntity` | `STATUS_MAX_STATUSES_PER_ENTITY` | `50` | Maximum concurrent active statuses per entity |
| `MaxStacksPerStatus` | `STATUS_MAX_STACKS_PER_STATUS` | `10` | Global maximum stack count per status (template `maxStacks` takes precedence if lower) |
| `MaxStatusTemplatesPerGameService` | `STATUS_MAX_STATUS_TEMPLATES_PER_GAME_SERVICE` | `200` | Maximum status template definitions per game service |
| `StatusCacheTtlSeconds` | `STATUS_STATUS_CACHE_TTL_SECONDS` | `60` | TTL for active status cache per entity (short -- statuses change frequently) |
| `SeedEffectsCacheTtlSeconds` | `STATUS_SEED_EFFECTS_CACHE_TTL_SECONDS` | `300` | TTL for seed-derived effects cache (longer -- capabilities change less frequently) |
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
| `ISeedClient` | Seed capability queries for unified effects cache (L2 hard) |
| `IResourceClient` | Cleanup callback registration (L1 hard -- resolved in `OnRunningAsync`) |
| `IEntitySessionRegistry` | Entity-to-session resolution for client event publishing (L1 hard) |
| `ITelemetryProvider` | Span instrumentation for async methods (L0 hard) |

---

## API Endpoints (Implementation Notes)

### Template Management (9 endpoints)

`CreateStatusTemplateAsync` validates the game service exists via `IGameServiceClient`, validates `itemTemplateId` exists via `IItemClient`, checks code uniqueness per game service, enforces `MaxStatusTemplatesPerGameService`, and saves with dual keys (by ID and by `gameServiceId:code`). Publishes `status.template.created` lifecycle event.

`GetStatusTemplateAsync` loads from MySQL by ID. `GetStatusTemplateByCodeAsync` loads by the `gameServiceId:code` dual key. Both return 404 if not found.

`ListStatusTemplatesAsync` performs paged JSON query with required `gameServiceId` filter, optional `category` filter, sorted by code ascending. Filters out deprecated templates by default; use `includeDeprecated: true` to include them.

`UpdateStatusTemplateAsync` acquires a distributed lock on the template, loads, validates, applies non-null fields only (partial update pattern). Clamps `MaxStacks` to `MaxStacksPerStatus` config. Publishes `status.template.updated` lifecycle event with `changedFields`.

`SeedStatusTemplatesAsync` performs bulk creation, skipping templates whose code already exists or whose item template doesn't exist (idempotent). Returns created/skipped counts.

`DeprecateStatusTemplateAsync` marks a template as deprecated (Category A per IMPLEMENTATION TENETS). Idempotent -- already-deprecated returns OK. Sets `IsDeprecated`, `DeprecatedAt`, `DeprecationReason`. Publishes `status.template.updated` with deprecation fields in `changedFields`. Deprecated templates reject new grants with `GrantFailureReason.TemplateDeprecated`.

`UndeprecateStatusTemplateAsync` reverses deprecation (Category A supports undeprecation). Idempotent -- not-deprecated returns OK. Clears deprecation fields. Publishes `status.template.updated`.

`DeleteStatusTemplateAsync` permanently deletes a template. Requires prior deprecation (returns BadRequest if not deprecated). Deletes both keys, publishes `status.template.deleted` lifecycle event.

### Status Operations (7 endpoints)

`GrantStatusAsync` is the core operation -- see the **Grant Flow** section below. Looks up template by `gameServiceId:code`, rejects deprecated templates with `GrantFailureReason.TemplateDeprecated`, acquires entity-level distributed lock, checks for existing instances of the same template code, delegates to `HandleStackingAsync` if any exist, otherwise checks entity-wide status limit, then calls `CreateNewStatusInstanceAsync`.

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
9. **Contract lifecycle** (if template has `contractTemplateId` -- data-gated, skipped when template has no contract):
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
│ Status Service Architecture │
│ │
│ ┌─────────────┐ ┌──────────────┐ ┌──────────────────────────┐ │
│ │ Caller (L4) │───>│ GrantStatus │───>│ Template Lookup (MySQL) │ │
│ └─────────────┘ └──────┬───────┘ └──────────────────────────┘ │
│ │ │
│ ▼ │
│ ┌────────────────┐ │
│ │ Find/Create │──> IInventoryClient. │
│ │ Container │ CreateContainerAsync │
│ └────────┬───────┘ (type: status_effects) │
│ │ │
│ ▼ │
│ ┌─────────────────────────┐ │
│ │ Check Stacking Behavior │ │
│ │ ignore/replace/refresh/ │ │
│ │ increase/independent │ │
│ └────────────┬────────────┘ │
│ │ │
│ ┌────────────▼────────────┐ │
│ │ Create Item (saga step) │──> IItemClient. │
│ │ (reversible on failure) │ CreateItemInstanceAsync │
│ └────────────┬────────────┘ (with expiresAt for TTL) │
│ │ │
│ ┌────────────▼────────────┐ │
│ │ Create Contract │──> IContractClient (data-gated │
│ │ (compensate item on │ by contractTemplateId; saga: │
│ │ failure) │ destroy item on failure) │
│ └────────────┬────────────┘ │
│ │ │
│ ┌────────────▼────────────┐ ┌──────────────────────────┐│
│ │ Save Instance (MySQL) │ │ Active Cache (Redis) ││
│ │ + Invalidate Cache │──>│ Rebuilt on miss from ││
│ └────────────┬────────────┘ │ MySQL instances ││
│ │ └──────────────────────────┘│
│ ▼ │
│ ┌─────────────────────────┐ │
│ │ Publish status.granted │──> IMessageBus │
│ └─────────────────────────┘ │
│ │
│ ═══════════════════════════════════════════════════════════════════ │
│ UNIFIED EFFECTS QUERY │
│ │
│ ┌─────────────┐ ┌──────────────────┐ ┌─────────────────────┐ │
│ │ GetEffects │───>│ Active Cache │ │ Seed Effects Cache │ │
│ │ (unified) │ │ (item-based) │ │ (seed-derived) │ │
│ └─────────────┘ │ Redis TTL: 60s │ │ Redis TTL: 300s │ │
│ │ └────────┬─────────┘ └──────────┬──────────┘ │
│ │ │ │ │
│ │ On miss: │ rebuild from On miss: │ query │
│ │ │ MySQL instances │ ISeedClient │
│ │ ▼ ▼ │
│ └─────> Merge with EffectSource attribution │
│ (item_based vs seed_derived) │
│ │
│ ═══════════════════════════════════════════════════════════════════ │
│ EXPIRATION (blocked on #407) │
│ │
│ lib-item decay worker ──> item.expired event ──> Status cleans up │
│ (manages expiresAt) (not yet published) instance records │
│ + publishes │
│ status.expired │
│ │
│ Contract-backed statuses: contract prebound API calls /status/remove │
│ on milestone expiry (Status never polls contracts) │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

All 19 API endpoints are fully implemented. The remaining stub is the `item.expired` event subscription.

### Blocking Dependency

- **#407 (Item Decay/Expiration System)**: The `item.expired` event subscription is commented out in the events schema. lib-item does not yet publish `item.expired` events. Until #407 is implemented, TTL-based status expiration relies on **lazy expiration during cache rebuild** -- when `GetOrBuildActiveCacheAsync` encounters expired instances in MySQL, it deletes them and publishes `status.expired`. This means expired statuses persist in MySQL until the next cache miss for that entity. Contract-backed statuses still work independently (contract prebound APIs call `/status/remove` on expiry).
<!-- AUDIT:BLOCKED:2026-02-13 -->

### Missing Implementation

- ~~**DeleteStatusTemplate endpoint**~~: **IMPLEMENTED** (2026-03-07) - Added `DeprecateStatusTemplateAsync`, `UndeprecateStatusTemplateAsync`, and `DeleteStatusTemplateAsync` endpoints following Category A deprecation lifecycle. Grants are rejected for deprecated templates.

- ~~**Cache warming and MaxCachedEntities**~~: **RESOLVED** (2026-03-23) — Config properties `CacheWarmingEnabled` and `MaxCachedEntities` were never added to the schema. Redis TTL (60s default) provides sufficient cache lifecycle management. The misleading code comment referencing `MaxCachedEntities` has been removed. If proactive cache warming or entity cap enforcement is needed in the future, add config properties at that time. See GH#412.

---

## Potential Extensions

- ~~**Tick-based effects (DOT/HOT)**~~: **RESOLVED** (2026-03-24) — Tick execution is a game-server/client concern, not a Bannou service concern. Status provides the data layer: active effects with stack counts, durations, and template metadata. Game servers run their own tick loops, query Status for current effect state, and apply game-specific damage/healing formulas (flat multiply, diminishing returns, caps — all game logic). ABML behaviors use status data (`${status.poison_stacks}`) as weights in NPC decision-making ("I'm poisoned → seek healing"), but NPCs do not execute damage calculations — the game server does. No tick infrastructure, no pre-calculated cumulative values (the game server has stack count + metadata and can apply its own formulas; pre-calculation would violate T29 by inspecting opaque metadata, and the aggregation formula is inherently game-specific). See GH#417.

- ~~**Conditional milestone completion**~~: **RESOLVED** (2026-03-24) — Resurrection condition evaluation is a game design choice, not a Bannou infrastructure concern. Multiple patterns already exist: (1) **Death as status**: grant a "death" status; cure via itemized prebound API (antidote item's `OnUsable` checks `HasStatus("death")`, `OnUse` calls `/status/remove`). Poison, curses, and any curable condition work identically. (2) **Death as quest**: on death, trigger a Quest with "return to life" objectives using Contract's existing sequential milestones. (3) **Item TTL expiry**: status item decays after N seconds (#407), auto-removing the death penalty. The "OR" logic ("wait 30s OR scroll OR shrine") is trivially handled by the first mechanism to call `/status/remove` — no `anyOf` milestone grouping needed on Contract. See GH#419.

- ~~**Subscription renewal flows**~~: **RESOLVED** (2026-03-25) — Subscription renewal is already implemented via `RenewSubscriptionAsync`. External billing systems (Stripe, PayPal, etc.) push renewal notifications via webhooks; the game server or billing integration handler calls `RenewSubscriptionAsync`. The `checkEndpoint` concept (Contract polling external billing) is unnecessary — billing systems push, they don't need to be polled. If subscription benefits include status effects, the game server manages Status grants using `sourceId` for cascade cleanup via `RemoveBySourceAsync`. Premium effects, premium inventories, premium UX, and other monetization patterns are L5 extension concerns — the composable APIs (Status, Subscription, Relationship, Collection, Inventory) provide all building blocks with zero blockers. See GH#421 and FAQ: WHY-IS-THERE-NO-MONETIZATION-PLUGIN.

- ~~**Source tracking cascading**~~: **RESOLVED** (2026-03-25) — Auto-cascading when a parent status is removed is a game implementation concern, not Status infrastructure. The behavior on parent removal is inherently per-status-type and undefined at the platform level: some parents cascade-remove children (premium subscription → benefit statuses), some leave children ticking independently (poison source removed → existing stacks persist), some modify children without removing them (blessing aura fades → reduced effectiveness). Status cannot bake in one behavior because it varies per game and per status type. The primitives are already sufficient: `sourceId` on `GrantStatusRequest` links children to parents, `RemoveBySourceAsync` removes all children sharing a sourceId when the game server decides cascading is appropriate. See GH#423.



- ~~**Client events**: `status-client-events.yaml` for pushing status change notifications to connected clients~~: **IMPLEMENTED** (2026-02-27) - `StatusEffectChangedClientEvent` with `StatusChangeType` discriminator. See [Client Events](#client-events) section.

- **Variable provider factory**: `StatusProviderFactory` implementing `IVariableProviderFactory` for `${status.*}` ABML namespace. Critical for NPC intelligence stack — closes the divine blessing feedback loop (Divine grants blessings → Status stores effects → Actor perceives via `${status.*}` → NPC behavior reacts). Follows the established pattern (14 existing implementations). Implementation: add `status` to `schemas/variable-providers.yaml`, regenerate `VariableProviderDefinitions.cs`, implement `StatusProviderFactory` + `StatusVariableProvider` in `plugins/lib-status/Providers/`. Data source: `GetOrBuildActiveCacheAsync` (existing Redis cache). Registration: `[BannouHelperService("status-provider", typeof(IStatusService), typeof(IVariableProviderFactory), lifetime: ServiceLifetime.Singleton)]`. Proposed variables: `${status.active_count}` (total active), `${status.has.<code>}` (boolean per template code), `${status.stacks.<code>}` (stack count per code), `${status.category_count.<category>}` (count by category), `${status.has_category.<category>}` (boolean per category). All qualitative behavioral data — magnitudes and damage numbers are game-server math (see resolved tick-based effects and effect magnitude items).
<!-- AUDIT:CONFIRMED:2026-03-25 -->

- ~~**Prerequisite provider factory**~~: **REJECTED** (2026-03-25) — `CHARACTER_LEVEL` and `REPUTATION` are game implementation concepts, not Bannou primitives — the same reasoning that rejects dedicated combat/skill/magic plugins (see FAQ: WHY-ARE-THERE-NO-SKILL-MAGIC-OR-COMBAT-PLUGINS). "Level" is a seed domain depth, "reputation" is a faction standing seed — both are Seed data, not Status data. Status should not interpret game-specific concepts as prerequisite types. Quest's `PrerequisiteType` enum needs `CharacterLevel` and `Reputation` removed — these are game concepts that belong in the dynamic provider path, registered by L5 extensions for each game's specific definitions. The existing dynamic provider infrastructure (`IPrerequisiteProviderFactory` discovered via `IEnumerable`) already handles this correctly — games register providers for whatever prerequisite types they define. See GH#719 for Quest schema cleanup.



---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. ~~**Missing account cleanup callback registration**~~: **FIXED** (2026-02-12) - Added account cleanup callback registration. Subsequently migrated to `account.deleted` event subscription per FOUNDATION TENETS (Account Deletion Cleanup Obligation) — lib-resource must not track account references (privacy constraint). The account `x-references` entry was removed from `status-api.yaml`, replaced with `HandleAccountDeletedAsync` in `StatusService.Events.cs` that delegates to the polymorphic `CleanupByOwnerAsync` endpoint. `account-service-events.yaml` is declared in `x-event-subscriptions`. Character cleanup continues to use lib-resource as normal.

2. ~~**SeedStatusTemplatesAsync skips item template validation**~~: **FIXED** (2026-02-12) - Added item template validation in `SeedStatusTemplatesAsync`. Templates referencing non-existent item templates are now skipped with a warning log and counted as "skipped" in the response, consistent with the idempotent seeding pattern.

3. ~~**SeedStatusTemplatesAsync skips game service validation**~~: **FIXED** (2026-02-12) - Added `_gameServiceClient.GetServiceAsync` validation at the top of `SeedStatusTemplatesAsync`, matching `CreateStatusTemplateAsync` pattern. Returns 404 if the game service doesn't exist, preventing orphaned templates.

### Intentional Quirks (Documented Behavior)

- **Every status template requires an item template (`itemTemplateId` is required)**: This is the "items in inventories" pattern (#280/#282) — every active status is a physical item in a per-entity status container. The item provides TTL infrastructure, container-based cleanup, saga compensation, and economy integration (status-granting consumables participate in loot/craft/trade). For minimal status backing items (death penalty, divine blessing, environmental effect), create an item template with `displayName`, `iconAssetId` (for UI), and `quantityModel: Unique`. The status template's own fields (category, stacking, duration, contract) carry the gameplay configuration. Multiple status templates may share one item template if they differ only in status-level properties. Use `SeedStatusTemplatesAsync` for bulk creation.

- **Polymorphic ownership with EntityType enum**: Entity types use the shared `EntityType` enum from `common-api.yaml` per IMPLEMENTATION TENETS. Status does NOT validate entity existence -- the caller is responsible for ensuring the entity exists before granting statuses.

- **No Status-specific expiration worker**: Time-based expiration is entirely delegated to lib-item's native decay system (#407). Status subscribes to `item.expired` events to clean up its own records. For contract-backed statuses, the contract template includes prebound APIs that call `/status/remove` on milestone expiry. Status never polls for expirations -- it is always event-driven.

- **Containers auto-created on first grant**: Status containers are created lazily when the first status is granted to an entity for a given game service. There is no explicit "create container" endpoint. This matches the Collection pattern and reduces API surface for consumers.

- **Active cache invalidation is full-entity, not surgical**: Any status change (grant, remove, stack, cleanse) invalidates the entire entity's active cache entry. The cache is rebuilt from MySQL instances on next read. Cache TTL is short (60s default) to minimize staleness. This is simpler and safer than surgical updates.

- **Seed effects gated by config**: The `SeedEffectsEnabled` configuration flag completely disables seed-derived effects in unified queries. When false, `ISeedClient` is still constructor-injected (L2 hard dependency per SERVICE-HIERARCHY.md) but never called — `ISeedEvolutionListener` callbacks are no-ops, and `GetEffects`/`ListStatuses` return only item-based effects. This is data-level feature gating, not service-level availability gating.

- **Contract integration is hard but usage is conditional**: `IContractClient` is constructor-injected (L1 hard dependency, always available). Contract creation only occurs when the template has a `contractTemplateId`. If contract creation fails during grant, the saga pattern compensates by destroying the already-created item instance, and the grant returns a failure response with `GrantFailureReason.ContractFailed`.

- **Lazy expiration during cache rebuild**: Since the `item.expired` event (#407) is not yet available, expired statuses are cleaned up opportunistically when `GetOrBuildActiveCacheAsync` rebuilds the active cache from MySQL. Expired instances found during rebuild are deleted from MySQL, and `status.expired` events are published. This means expired statuses survive in MySQL until the next cache miss for that entity (default 60s cache TTL).

- **Stacking behavior `ignore` publishes a failure event**: When a template uses `ignore` stacking and the status is already present, the grant is rejected and a `StatusGrantFailedEvent` is published with `GrantFailureReason.stack_behavior_ignore`. The existing status instance ID is included in the failure response.

### Design Considerations (Requires Planning)

- ~~**Item template per status template**~~: **RESOLVED** (2026-03-23) — The 1:1 requirement is architecturally correct and intentional per the "Itemize Anything" pattern (#280/#282). Every status IS an item in a container — that's the design foundation. The item instance provides TTL/expiry infrastructure (via #407), container-based cleanup (cascading deletes), saga compensation (destroy item if contract fails), and integration with the economy (status-granting consumables are tradeable/craftable/lootable items). Game designers create item templates for each status type; `SeedStatusTemplatesAsync` handles bulk creation ergonomically. The typical pattern is 1:1 (one item template per status template), though multiple status templates MAY share the same item template when they differ only in status-specific properties (category, stacking behavior, duration, contract). Relevant item template properties for status backing items: `displayName` and `iconAssetId` (for client UI rendering), `quantityModel: Unique` (each status instance is distinct). Other item properties (stats, effects, rarity) are optional — the status template's own fields carry the meaningful gameplay configuration.

- ~~**Hardcoded `EntityType.Character` in contract creation/termination**~~: **FIXED** (2026-03-07) - Both contract creation and termination now use the entity's actual `EntityType` from the request/instance model instead of hardcoded `EntityType.Character`.

- ~~**ISeedClient injection pattern: constructor vs runtime resolution**~~: **FIXED** (2026-03-09) - All ISeedClient usage now uses constructor injection per FOUNDATION TENETS. Seed is L2, Status is L4 — hard dependency, no graceful degradation.

- ~~**Dead config #412: InactiveCheckIntervalSeconds**~~: **RESOLVED** (2026-03-23) — Config property `InactiveCheckIntervalSeconds` was never added to the schema. No inactive status check feature exists or is planned. If needed in the future, add config property at that time. See GH#412.

- ~~**echoed fields in query responses**~~: **FIXED** (2026-03-09) - Removed `entityId` and `entityType` from `GetEffectsResponse` and `SeedEffectsResponse`. Caller already knows what they requested per IMPLEMENTATION TENETS.

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
| lib-seed (L2, hard) | `GetSeedsByOwnerAsync` | Rebuild seed effects cache (list seeds for entity) |
| lib-seed (L2, hard) | `GetCapabilityManifestAsync` | Rebuild seed effects cache (get capabilities per seed) |
| lib-seed (L2, hard) | `GetSeedAsync` | Seed effects cache invalidation via event (look up seed owner) |

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
- **Batch lifecycle events** (2026-03-15): Switch to batch: true for high-frequency instance lifecycle events. Tracked via [#650](https://github.com/beyond-immersion/bannou-service/issues/650).
- **Variable provider factory** (2026-03-25): `StatusProviderFactory` for `${status.*}` ABML namespace. Tracked via [#718](https://github.com/beyond-immersion/bannou-service/issues/718).

### Completed

- [#426](https://github.com/beyond-immersion/bannou-service/issues/426) - Client events via Entity Session Registry -- implemented (2026-02-27)
- [#375](https://github.com/beyond-immersion/bannou-service/issues/375) - Pipeline architecture (Collection -> Seed -> Status) -- implemented
- [#280](https://github.com/beyond-immersion/bannou-service/issues/280) - Itemize anything pattern -- implemented
- **Account cleanup callback registration** (2026-02-12) - Fixed missing account cleanup callback in `StatusServicePlugin.OnRunningAsync`
- **SeedStatusTemplatesAsync item template validation** (2026-02-12) - Added item template validation in seed endpoint
- **SeedStatusTemplatesAsync game service validation** (2026-02-12) - Added game service existence validation in seed endpoint
- **Hardening pass** (2026-03-07):
 - Fixed cache key casing mismatch in `StatusSeedEvolutionListener` (PascalCase → matching `StatusService.SeedEffectsCacheKey`)
 - Fixed hardcoded `EntityType.Character` in contract creation/termination (now uses actual entity type)
 - Fixed metadata type-narrowing (`Dictionary<string, object>?` → `object?` per tenets)
 - Fixed client event name from `status.effect-changed` to `status.effect.changed` (Pattern C)
 - Added Category A deprecation lifecycle (3 new endpoints, grant guard, `includeDeprecated` filter)
 - Added `TemplateDeprecated` to `GrantFailureReason` enum
 - Added `maxLength` to all string properties, `maxItems` to all array properties, `maximum`/`minimum` to numeric properties
 - Moved `x-references` under `info:` block, fixed cleanup endpoint permissions to `[]`
 - Removed-violating metadata description mentioning Divine integration convention
 - Added hierarchy validation test
 - Migrated `entityType` documentation from "opaque strings" to `EntityType` enum
