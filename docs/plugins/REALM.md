# Realm Plugin Deep Dive

> **Plugin**: lib-realm
> **Schema**: schemas/realm-api.yaml
> **Version**: 1.0.0
> **State Store**: realm-statestore (MySQL)

---

## Overview

The Realm service manages top-level persistent worlds in the Arcadia game system. Realms are peer worlds (e.g., Omega, Arcadia, Fantasia) with no hierarchical relationships between them. Each realm operates as an independent world with distinct species populations and cultural contexts. Provides CRUD with deprecation lifecycle and seed-from-configuration support.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Persistence for realm definitions and indexes |
| lib-messaging (`IMessageBus`) | Publishing lifecycle events and error events |
| lib-messaging (`IEventConsumer`) | Event handler registration (no current handlers) |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-location | Calls `RealmExistsAsync` via `IRealmClient` to validate realm before location creation |
| lib-species | Calls `RealmExistsAsync` via `IRealmClient` to validate realm before species operations |
| lib-character | Calls `RealmExistsAsync` via `IRealmClient` to validate realm before character creation |
| lib-analytics | Subscribes to `realm.updated` event for cache invalidation (realm-to-gameService resolution cache) |

---

## State Storage

**Store**: `realm-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `realm:{realmId}` | `RealmModel` | Full realm definition (name, code, game service, deprecation state) |
| `code-index:{CODE}` | `string` | Code → realm ID lookup (uppercase normalized) |
| `all-realms` | `List<Guid>` | Master list of all realm IDs |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `realm.created` | `RealmCreatedEvent` | New realm created |
| `realm.updated` | `RealmUpdatedEvent` | Realm metadata modified, deprecated, or undeprecated (includes `changedFields`) |
| `realm.deleted` | `RealmDeletedEvent` | Realm permanently deleted |

Events are auto-generated from `x-lifecycle` in `realm-events.yaml`.

### Consumed Events

This plugin does not consume external events.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| — | — | — | No service-specific configuration properties |

The generated `RealmServiceConfiguration` contains only the framework-level `ForceServiceId` property.

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<RealmService>` | Scoped | Structured logging |
| `RealmServiceConfiguration` | Singleton | Framework config (empty) |
| `IStateStoreFactory` | Singleton | State store access |
| `IMessageBus` | Scoped | Event publishing |
| `IEventConsumer` | Scoped | Event registration (no handlers) |

Service lifetime is **Scoped** (per-request).

---

## API Endpoints (Implementation Notes)

### Read Operations (5 endpoints)

Standard read operations with two lookup strategies. **GetByCode** uses a two-step lookup: code index → realm ID → realm data. **Exists** returns an `exists` + `isActive` pair (always returns 200, never 404) for fast validation by dependent services. **ExistsBatch** validates multiple realm IDs in a single call using `GetBulkAsync`, returning per-realm results plus convenience flags (`allExist`, `allActive`) and lists of invalid/deprecated IDs. **List** supports filtering by category, active status, and deprecation, with pagination.

### Write Operations (3 endpoints)

- **Create**: Codes normalized to uppercase (`ToUpperInvariant()`). Validates code uniqueness via index. Stores realm data, code index, and appends to master list. Publishes `realm.created`.
- **Update**: Smart field tracking — only modifies fields where the new value differs from current. Tracks changed field names. Does not publish event if nothing changed. `GameServiceId` is mutable (can be updated).
- **Delete**: Two-step safety — realm MUST be deprecated first (returns 409 Conflict otherwise). Removes all three keys (data, code index, list entry). Publishes `realm.deleted`.

### Deprecation Operations (2 endpoints)

- **Deprecate**: Sets `isDeprecated = true`, records timestamp and optional reason. Publishes `realm.updated` with deprecation fields in `changedFields`.
- **Undeprecate**: Clears deprecation state. Returns 400 Bad Request if realm is not currently deprecated.

### Seed Operation (1 endpoint)

Idempotent bulk creation with optional `updateExisting` flag. Processes each realm independently with per-item error handling (failures don't stop the batch). Returns counts of created, updated, skipped, and error messages.

---

## Visual Aid

```
Realm Deletion Safety Chain
=============================

  [Active Realm]
       │
       │ POST /realm/deprecate
       │   (requires admin)
       ▼
  [Deprecated Realm]
   isDeprecated: true
   deprecatedAt: timestamp
   deprecationReason: "..."
       │
       ├──── POST /realm/undeprecate ───► [Active Realm again]
       │       (reversal path)
       │
       │ POST /realm/delete
       │   (requires admin + deprecated)
       ▼
  [Permanently Deleted]
   - realm:{id} removed
   - code-index:{CODE} removed
   - all-realms list updated

  [Active Realm] ─── POST /realm/delete ──► 409 Conflict
                     (cannot skip deprecation step)
```

---

## Stubs & Unimplemented Features

None identified.

---

## Potential Extensions

1. **Realm merge**: Consolidate deprecated realms into active ones, migrating all associated entities (characters, locations, species).
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/167 -->
2. ~~**Batch exists check**~~: **FIXED** (2026-01-31) - Added `/realm/exists-batch` endpoint that validates multiple realm IDs in a single call using `GetBulkAsync`. Returns per-realm results plus convenience flags `allExist`, `allActive`, and lists of `invalidRealmIds`/`deprecatedRealmIds`.
3. **Realm statistics**: Track entity counts per realm (characters, locations, species) for capacity planning.
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/169 -->
4. **Event consumption for cascade**: Listen to character/location deletion events to track reference counts for safe deletion.

---

## Known Quirks & Caveats

### Bugs

None identified.

### Intentional Quirks

1. **VOID realm is a convention, not enforced**: The API schema documents a "VOID realm" concept as a sink for entities whose realm should be removed. This is **intentionally not enforced** in realm service code — it's a seeding convention parallel to Species (VOID species) and RelationshipType (VOID type). The VOID realm should be seeded via `/realm/seed` with metadata `isSystemType: true`. Enforcement of VOID semantics (e.g., preventing new character creation in VOID realm) belongs to consuming services (Character, Location, Species), not the realm registry itself.

2. **Exists endpoint never returns 404**: Always returns `StatusCodes.OK` with `exists: true/false` and `isActive: true/false`. Callers must check both flags to determine usability. This avoids 404 semantics for a "check" operation.

3. **IsActive in Exists response is computed**: Returns `IsActive = model.IsActive && !model.IsDeprecated`. A realm with `IsActive=true` and `IsDeprecated=true` will return `IsActive=false` in the Exists response. The two properties are conflated.

4. **Metadata update has no equality check**: Unlike Name/Description/Category which check `body.X != model.X`, Metadata only checks `body.Metadata != null`. Sending identical metadata still triggers an update event with "metadata" in changedFields.

5. **Seed updates are silent (no events)**: When `UpdateExisting=true`, seed operations directly update the model without calling `PublishRealmUpdatedEventAsync`. Regular updates publish events; seed updates don't.

6. **LoadRealmsByIdsAsync silently drops missing realms**: If the all-realms list contains an ID that doesn't exist in data store, it's silently excluded from results.

7. **Deprecate is not idempotent**: Returns `StatusCodes.Conflict` if realm is already deprecated. Calling deprecate twice fails the second time rather than being a no-op.

8. **Event publishing failures are swallowed**: Event publishing exceptions are caught and logged as warnings, but don't fail the operation. State changes succeed even if events fail to publish.

### Design Considerations

1. **GameServiceId mutability**: `UpdateRealmRequest` allows changing `GameServiceId`. This could break the game-service → realm relationship if not handled carefully by dependent systems that assume realm ownership is immutable.

2. **All-realms list as single key**: Like game-service, the master list grows linearly. Delete operations read the full list, filter, and rewrite. Not a concern with expected realm counts (dozens) but architecturally identical to the game-service concern.

3. **No reference counting for delete**: The delete endpoint does not verify that no entities (characters, locations, species) reference the realm. Dependent services call `RealmExistsAsync` on creation but nothing prevents deleting a realm that still has active entities.

4. **Event publishing non-transactional**: State store writes and event publishing are separate operations. A crash between writing state and publishing the event would leave dependent services unaware of the change until they directly query.

5. **Read-modify-write without distributed locks**: Create/Delete modify the all-realms list, Update modifies realm model. Requires ETag-based optimistic concurrency or distributed locks.

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow and should not be manually edited except to add new tracking markers.

### In Progress

- **2026-01-31**: Realm merge feature requires design decisions. See [#167](https://github.com/beyond-immersion/bannou-service/issues/167) for open questions about entity migration ordering, species realm association handling, location hierarchy treatment, and partial failure policies.
- **2026-01-31**: Realm statistics feature requires design decisions. See [#169](https://github.com/beyond-immersion/bannou-service/issues/169) for open questions about synchronous vs event-driven counting, species multi-realm membership handling, historical tracking, and potential overlap with Analytics service.

### Completed

- **2026-01-31**: Added `/realm/exists-batch` endpoint for batch realm validation. Uses `GetBulkAsync` for efficient single-call validation of multiple realm IDs. Returns per-realm results with `allExist`/`allActive` convenience flags.
- **2026-01-31**: Reclassified "VOID realm pattern" from Stubs to Intentional Quirks. The VOID realm is intentionally a convention (like Species/RelationshipType VOID entries), not service-enforced logic. Created `provisioning/seed-data/realms.yaml` to provide seed data with VOID realm, paralleling the existing species.yaml and relationship-types.yaml files.
