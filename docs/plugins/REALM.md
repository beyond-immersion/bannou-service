# Realm Plugin Deep Dive

> **Plugin**: lib-realm
> **Schema**: schemas/realm-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFoundation
> **State Store**: realm-statestore (MySQL), realm-lock (Redis)
> **Implementation Map**: [docs/maps/REALM.md](../maps/REALM.md)
> **Short**: Top-level persistent world management with deprecation lifecycle and seed-from-configuration

---

## Overview

The Realm service (L2 GameFoundation) manages top-level persistent worlds in the Arcadia game system. Realms are peer worlds (e.g., Omega, Arcadia, Fantasia) with no hierarchical relationships between them. Each realm operates as an independent world with distinct species populations and cultural contexts. Provides CRUD with deprecation lifecycle and seed-from-configuration support. Internal-only.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-location | Calls `RealmExistsAsync` via `IRealmClient` to validate realm before location creation |
| lib-species | Calls `RealmExistsAsync` via `IRealmClient` to validate realm before species operations |
| lib-character | Calls `RealmExistsAsync` via `IRealmClient` to validate realm before character creation |
| lib-faction | Calls `RealmExistsAsync` via `IRealmClient` to validate realm before faction operations |
| lib-analytics | Calls realm endpoints via `IRealmClient` for realm data lookups; subscribes to `realm.updated` event for cache invalidation (realm-to-gameService resolution cache) |
| lib-worldstate | Calls `RealmExistsAsync` via `IRealmClient` to validate realm before clock initialization |
| lib-puppetmaster | Subscribes to `realm.created`, `realm.updated`, and `realm.deleted` events for regional watcher lifecycle management |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `OptimisticRetryAttempts` | `REALM_OPTIMISTIC_RETRY_ATTEMPTS` | `3` | Retry count for ETag-based optimistic concurrency operations (1-10) |
| `MergeLockTimeoutSeconds` | `REALM_MERGE_LOCK_TIMEOUT_SECONDS` | `120` | Timeout for distributed lock during realm merge (10-600) |
| `AutoInitializeWorldstateClock` | `REALM_AUTO_INITIALIZE_WORLDSTATE_CLOCK` | `false` | When true, auto-initializes a worldstate realm clock after creating a new realm. Failure does not block realm creation |
| `DefaultCalendarTemplateCode` | `REALM_DEFAULT_CALENDAR_TEMPLATE_CODE` | `null` | Calendar template code for worldstate clock auto-initialization. Required when `AutoInitializeWorldstateClock` is true |

---

## Visual Aid

```
Realm Deletion Safety Chain
=============================

 [Active Realm]
 │
 │ POST /realm/deprecate
 │ (requires admin)
 ▼
 [Deprecated Realm]
 isDeprecated: true
 deprecatedAt: timestamp
 deprecationReason: "..."
 │
 ├──── POST /realm/undeprecate ───► [Active Realm again]
 │ (reversal path)
 │
 │ POST /realm/delete
 │ (requires admin + deprecated)
 ▼
 [Permanently Deleted]
 - realm:{id} removed
 - code-index:{CODE} removed
 - all-realms list updated

 [Active Realm] ─── POST /realm/delete ──► 400 BadRequest
 (cannot skip deprecation step)


Realm Merge Flow (Resource-Coordinated)
=========================================

 [Deprecated Source Realm] ──► POST /realm/merge ──► [Target Realm]
 │                                                   │
 │ Phase: Resource-Coordinated Migration             │
 │ CALL /resource/migrate/execute(realm, source,     │
 │                                target)            │
 │ Resource invokes registered migrate callbacks:    │
 │   → Species: /species/migrate-by-realm            │
 │   → Locations: /location/migrate-by-realm         │
 │   → Characters: /character/migrate-by-realm       │
 │ Each service owns its own migration logic         │
 │                                                   │
 └── if deleteAfterMerge && 0 failures ──► DeleteRealm(source)
```

**Deletion also updates the visual aid above** — the resource check step sits between "Deprecated Realm" and "Permanently Deleted", returning 409 Conflict if active L4 references (e.g., realm-history) exist with RESTRICT policy.

---

## Stubs & Unimplemented Features

None identified.

---

## Potential Extensions

None identified.

---

## Known Quirks & Caveats

### Bugs

None identified.

### Intentional Quirks

1. **VOID realm is a convention, not enforced**: The API schema documents a "VOID realm" concept as a sink for entities whose realm should be removed. This is **intentionally not enforced** in realm service code — it's a seeding convention parallel to Species (VOID species) and RelationshipType (VOID type). The VOID realm should be seeded via `/realm/seed` with `isSystemType: true`. Enforcement of VOID semantics (e.g., preventing new character creation in VOID realm) belongs to consuming services (Character, Location, Species), not the realm registry itself.

2. **Exists endpoint returns 404 for missing realms**: Returns `StatusCodes.OK` with `isActive` (computed) when the realm exists, or `404` when it does not. Callers use 404 to detect non-existence and check `isActive` to determine usability (active and non-deprecated).

3. **IsActive in Exists response is computed**: Returns `IsActive = model.IsActive && !model.IsDeprecated`. A realm with `IsActive=true` and `IsDeprecated=true` will return `IsActive=false` in the Exists response. The two properties are conflated.

4. **Metadata update has no equality check**: Unlike Name/Description/Category which check `body.X != model.X`, Metadata only checks `body.Metadata != null`. Sending identical metadata still triggers an update event with "metadata" in changedFields.

5. **LoadRealmsByIdsAsync silently drops missing realms**: If the all-realms list contains an ID that doesn't exist in data store, it's silently excluded from results.

6. **Event publishing uses aggressive retry with fail-loud crash semantics**: State store writes and event publishing are separate operations (no transactional outbox). However, lib-messaging's `TryPublishAsync` implements a sophisticated retry system via `MessageRetryBuffer`:
 - **On publish failure**: Messages are buffered in-memory and `TryPublishAsync` returns `true` (because delivery WILL be retried)
 - **Retry processing**: Every 5 seconds (configurable), the buffer is processed and failed messages are re-attempted
 - **Fail-loud thresholds**: If RabbitMQ stays down too long, the node **intentionally crashes** via `Environment.FailFast()`:
 - Buffer exceeds 10,000 messages (default `MESSAGING_RETRY_BUFFER_MAX_SIZE`)
 - Oldest message exceeds 5 minutes (default `MESSAGING_RETRY_BUFFER_MAX_AGE_SECONDS`)
 - **Why crash?**: Crashing triggers orchestrator restart, makes the failure visible in monitoring, and prevents silent data loss or unbounded memory growth

 **True loss scenarios** (rare):
 - Node dies (power failure, OOM kill) before buffer flushes
 - Clean shutdown with non-empty buffer (logged as warning)
 - Serialization failure (programming error, not retryable)

 The `PublishRealm*EventAsync` helper methods delegate directly to `TryPublishAsync` without redundant try/catch wrappers (per QUALITY TENETS error handling — `TryPublishAsync` is internally safe). This is the **standard Bannou architecture** used by all services.

7. **GameServiceId is mutable**: `UpdateRealmRequest` allows changing `GameServiceId`, which reassigns the realm to a different game service. This is intentional - realms can be reorganized (e.g., game service consolidation, re-branding). Dependent services handle this via event-driven cache invalidation: Analytics subscribes to `realm.updated` and invalidates its `realm-to-gameService` cache, ensuring subsequent lookups fetch the new `GameServiceId`. Other services (Location, Species, Character) validate realm existence on creation but don't cache `GameServiceId`, so they're unaffected.

8. **All-realms list stored as single key**: The `all-realms` key stores a `List<Guid>` of all realm IDs. Create/Delete operations read the entire list, modify it, and write it back. This is intentional: realms are top-level game worlds (expected count: single-digit to ~50 max), not high-volume entities. The list serializes to <5KB even at 100 realms, and realm creation/deletion are rare administrative operations. Alternative patterns (Redis SCAN, secondary index tables) would add complexity without benefit at this scale. The pattern matches game-service which has similar constraints.

### Design Considerations

1. **System realms are an architectural keystone**: The `isSystemType` flag on realms enables the **actor-bound entity pattern** — the single most powerful architectural pattern in Bannou (per VISION.md). System realms are non-physical conceptual spaces where metaphysical entities exist as first-class Characters, gaining the entire L2/L4 entity stack (personality, memory, history, growth, bonds, cognition) for free. Planned system realms: **PANTHEON** (gods/deities for content flywheel orchestration), **NEXIUS** (guardian spirits for progressive agency), **DUNGEON_CORES** (sentient dungeons), **SENTIENT_ARMS** (living weapons), **UNDERWORLD** (dead characters for afterlife gameplay). System realms are seeded via `/realm/seed` with `isSystemType: true`, protected from merge (merge validation rejects system realm sources), and the Realm service is **intentionally agnostic** to their semantics — it stores the flag; consuming services (Character, Puppetmaster, Gardener, Divine, Dungeon, etc.) interpret what "system realm" means for their domain. See [#268](https://github.com/beyond-immersion/bannou-service/issues/268) (closed — realm hierarchy explicitly not part of the flat-peer-world design).

2. **Realm deletion safety via L2 reference registration**: The intended deletion workflow is deprecate → merge → delete, with merge delegating to `/resource/migrate/execute` which invokes each service's registered migration callback. Realm's delete flow checks lib-resource for active references. Species ([#369](https://github.com/beyond-immersion/bannou-service/issues/369)), Location ([#590](https://github.com/beyond-immersion/bannou-service/issues/590)), and Character ([#591](https://github.com/beyond-immersion/bannou-service/issues/591)) now have `x-references` with `target: realm` registered as RESTRICT, completing the safety net.

Other design considerations resolved: Reference counting for safe deletion implemented via lib-resource integration (see [#170](https://github.com/beyond-immersion/bannou-service/issues/170), closed). Realm statistics evaluated and closed as wrong-layer — entity statistics belong in Analytics (L4), not Realm (L2) (see [#169](https://github.com/beyond-immersion/bannou-service/issues/169), closed).

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow and should not be manually edited except to add new tracking markers.

### Completed

- **2026-02-08**: Reference counting for safe deletion implemented via lib-resource integration. See [#170](https://github.com/beyond-immersion/bannou-service/issues/170) (closed). DeleteRealm now checks references and executes cleanup callbacks before proceeding.
- **2026-02-08**: Realm statistics evaluated and closed — entity count tracking belongs in Analytics (L4), not Realm (L2). See [#169](https://github.com/beyond-immersion/bannou-service/issues/169) (closed).
- **2026-02-08**: Realm merge feature implemented. Three-phase migration (species → locations root-first → characters) with continue-on-individual-failure policy, configurable page size, and optional post-merge deletion. See [#167](https://github.com/beyond-immersion/bannou-service/issues/167) (closed). Also added `/location/transfer-realm` endpoint as prerequisite.
- **2026-02-26**: deprecation lifecycle compliance — Deprecate and Undeprecate are now idempotent per IMPLEMENTATION TENETS.
- **2026-02-28**: Production hardening audit — comprehensive tenet compliance pass (schema,,,,,,) and post-audit code review fixes (ETag concurrency for seed update, ApiException handling in migration helpers).
- **2026-03-04**: Added `GetLocationCompressContext` endpoint and compression callback registration providing realm context (name, code, description) for location archives. Fixed `x-resource-lifecycle` placement (was at YAML root level instead of inside `info:` block, causing generators to silently skip it). Also registered `RealmContextTemplate` as `IResourceTemplate` for ABML path validation.
- **2026-03-08**: Realm deletion safety — Species ([#369](https://github.com/beyond-immersion/bannou-service/issues/369)), Location ([#590](https://github.com/beyond-immersion/bannou-service/issues/590)), and Character ([#591](https://github.com/beyond-immersion/bannou-service/issues/591)) now have `x-references` with `target: realm` registered as RESTRICT.
- **2026-03-15**: Merge refactored to resource-coordinated migration — Realm no longer directly calls ISpeciesClient or ICharacterClient for merge. Instead calls `IResourceClient.ExecuteMigrateAsync` which delegates to each service's registered migration callback. Removed `MergePageSize` config (pagination is now each service's concern). ILocationClient retained for compression context only.

### Pending Design

None.

### Ready for Implementation

None.
