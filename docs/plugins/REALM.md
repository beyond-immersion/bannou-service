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

No services subscribe to realm events.

---

## State Storage

**Store**: `realm-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `realm:{realmId}` | `RealmModel` | Full realm definition (name, code, game service, deprecation state) |
| `code-index:{CODE}` | `string` | Code → realm ID lookup (uppercase normalized) |
| `all-realms` | `List<string>` | Master list of all realm IDs |

---

## Events

### Published Events

| Topic | Trigger |
|-------|---------|
| `realm.created` | New realm created |
| `realm.updated` | Realm metadata modified, deprecated, or undeprecated (includes `changedFields`) |
| `realm.deleted` | Realm permanently deleted |

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

### Read Operations (4 endpoints)

Standard read operations with two lookup strategies. **GetByCode** uses a two-step lookup: code index → realm ID → realm data. **Exists** returns an `exists` + `isActive` pair (always returns 200, never 404) for fast validation by dependent services. **List** supports filtering by category, active status, and deprecation, with pagination.

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

1. **VOID realm pattern**: The API schema documents a "VOID realm" concept as a sink for entities whose realm should be removed, but this is not enforced in service code — it's a convention for client implementations.

---

## Potential Extensions

1. **Realm merge**: Consolidate deprecated realms into active ones, migrating all associated entities (characters, locations, species).
2. **Batch exists check**: Validate multiple realm IDs in one call for services creating multi-realm entities.
3. **Realm statistics**: Track entity counts per realm (characters, locations, species) for capacity planning.
4. **Event consumption for cascade**: Listen to character/location deletion events to track reference counts for safe deletion.

---

## Known Quirks & Caveats

### Bugs

None identified.

### Intentional Quirks

1. **Codes are always uppercase**: `ToUpperInvariant()` normalization means "omega" and "OMEGA" resolve to the same realm. Original case is lost at creation time.

2. **Exists endpoint never returns 404**: Always returns `StatusCodes.OK` with `exists: true/false` and `isActive: true/false`. Callers must check both flags to determine usability. This avoids 404 semantics for a "check" operation.

3. **Two-step deletion safety**: Hard delete requires prior deprecation. This prevents accidental deletion of active realms and gives dependent services time to react to the deprecation event.

4. **Update publishes only when changed**: If an update request contains the same values as current state, no event is published and no timestamp is updated. Idempotent updates are no-ops.

5. **Seed continues on failure**: Individual realm failures during seeding don't abort the batch. Error messages are collected and returned alongside success counts.

6. **Code index inconsistency handling**: If the code index points to a realm ID that no longer exists in the data store (corruption), the service logs a warning about data inconsistency and returns NotFound rather than crashing.

7. **IsActive in Exists response is computed**: Line 235 returns `IsActive = model.IsActive && !model.IsDeprecated`. A realm with `IsActive=true` and `IsDeprecated=true` will return `IsActive=false` in the Exists response. The two properties are conflated.

8. **Metadata update has no equality check**: Unlike Name/Description/Category which check `body.X != model.X`, Metadata at line 377 only checks `body.Metadata != null`. Sending identical metadata still triggers an update event with "metadata" in changedFields.

9. **Seed updates are silent (no events)**: When `UpdateExisting=true`, seed operations (lines 607-616) directly update the model without calling `PublishRealmUpdatedEventAsync`. Regular updates publish events; seed updates don't.

10. **Seed updates are forceful overwrites**: Seed update (lines 607-612) sets `existingModel.Name = seedRealm.Name` directly without checking if values differ. This differs from `UpdateRealmAsync` which uses `if (body.X != model.X)` guards.

11. **LoadRealmsByIdsAsync silently drops missing realms**: Lines 696-703 filter out null models without logging. If the all-realms list contains an ID that doesn't exist in data store, it's silently excluded from results.

12. **Deprecate is not idempotent**: Lines 488-491 return `StatusCodes.Conflict` if realm is already deprecated. Calling deprecate twice fails the second time rather than being a no-op.

13. **Event publishing failures are swallowed**: Lines 754-757, 790-793, 825-828 catch event publishing exceptions and log warnings, but don't fail the operation. State changes succeed even if events fail to publish.

### Design Considerations

1. **GameServiceId mutability**: `UpdateRealmRequest` allows changing `GameServiceId`. This could break the game-service → realm relationship if not handled carefully by dependent systems that assume realm ownership is immutable.

2. **All-realms list as single key**: Like game-service, the master list grows linearly. Delete operations read the full list, filter, and rewrite. Not a concern with expected realm counts (dozens) but architecturally identical to the game-service concern.

3. **No reference counting for delete**: The delete endpoint does not verify that no entities (characters, locations, species) reference the realm. Dependent services call `RealmExistsAsync` on creation but nothing prevents deleting a realm that still has active entities.

4. **Event publishing non-transactional**: State store writes and event publishing are separate operations. A crash between writing state and publishing the event would leave dependent services unaware of the change until they directly query.

5. **Read-modify-write without distributed locks**: Create/Delete modify the all-realms list, Update modifies realm model. Requires ETag-based optimistic concurrency or distributed locks.
