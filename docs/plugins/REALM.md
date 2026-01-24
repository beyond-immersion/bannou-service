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

### Bugs (Fix Immediately)

None identified.

### Intentional Quirks (Documented Behavior)

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

### Design Considerations (Requires Planning)

1. **GameServiceId mutability**: `UpdateRealmRequest` allows changing `GameServiceId`. This could break the game-service → realm relationship if not handled carefully by dependent systems that assume realm ownership is immutable.

2. **All-realms list as single key**: Like game-service, the master list grows linearly. Delete operations read the full list, filter, and rewrite. Not a concern with expected realm counts (dozens) but architecturally identical to the game-service concern.

3. **No reference counting for delete**: The delete endpoint does not verify that no entities (characters, locations, species) reference the realm. Dependent services call `RealmExistsAsync` on creation but nothing prevents deleting a realm that still has active entities.

4. **Event publishing non-transactional**: State store writes and event publishing are separate operations. A crash between writing state and publishing the event would leave dependent services unaware of the change until they directly query.

---

## Tenet Violations (Audit)

### Category: FOUNDATION

1. **Service Implementation Pattern (T6)** - RealmService.cs:29-43 - Missing null checks on constructor dependencies
   - What's wrong: The constructor assigns `stateStoreFactory`, `messageBus`, `logger`, and `configuration` directly without null-coalescing throws or `ArgumentNullException.ThrowIfNull`. Per T6, all constructor dependencies must have explicit null checks with meaningful exceptions.
   - Fix: Add `?? throw new ArgumentNullException(nameof(...))` for each dependency assignment, or use `ArgumentNullException.ThrowIfNull(...)` before assignment.

2. **Service Implementation Pattern (T6)** - RealmService.cs:42 - Missing null check on `eventConsumer` before use
   - What's wrong: The constructor calls `((IBannouService)this).RegisterEventConsumers(eventConsumer)` without first validating `eventConsumer` is non-null. Per T6, this should use `ArgumentNullException.ThrowIfNull(eventConsumer, nameof(eventConsumer))` before the call.
   - Fix: Add `ArgumentNullException.ThrowIfNull(eventConsumer, nameof(eventConsumer));` before the `RegisterEventConsumers` call.

3. **Service Implementation Pattern (T6)** - RealmService.cs:20 - State store not initialized in constructor
   - What's wrong: Per T6 and T4, the established pattern is to call `stateStoreFactory.GetStore<T>(StateStoreDefinitions.X)` once in the constructor and store it in a `private readonly IStateStore<T> _stateStore` field. Instead, this service calls `_stateStoreFactory.GetStore<T>(StateStoreDefinitions.Realm)` on every single operation (25+ times throughout the file), creating unnecessary overhead and diverging from the established pattern.
   - Fix: Add `private readonly IStateStore<RealmModel> _stateStore;` (and potentially typed stores for `string` and `List<string>`) initialized in the constructor from `stateStoreFactory.GetStore<T>(StateStoreDefinitions.Realm)`.

### Category: IMPLEMENTATION

4. **Multi-Instance Safety (T9)** - RealmService.cs:305-310 - Read-modify-write on all-realms list without distributed lock
   - What's wrong: `CreateRealmAsync` reads the all-realms list, checks if the ID is present, adds it, and writes back. This is a non-atomic read-modify-write sequence. If two instances create realms concurrently, one write can overwrite the other, losing a realm ID from the master list. No `IDistributedLockProvider` or ETag-based optimistic concurrency is used.
   - Fix: Use `IDistributedLockProvider` to lock the all-realms list key during modifications, or use `GetWithETagAsync`/`TrySaveAsync` with retry for optimistic concurrency.

5. **Multi-Instance Safety (T9)** - RealmService.cs:441-445 - Read-modify-write on all-realms list without distributed lock (delete path)
   - What's wrong: `DeleteRealmAsync` reads the all-realms list, removes an entry, and writes back without any concurrency control. Same race condition as the create path.
   - Fix: Same as violation #4 -- use distributed locking or optimistic concurrency.

6. **Multi-Instance Safety (T9)** - RealmService.cs:341-386 - Read-modify-write on realm model without concurrency control (update path)
   - What's wrong: `UpdateRealmAsync` reads the realm model, modifies fields, and saves without ETags or distributed locks. Two concurrent updates could result in a lost update (last-writer-wins without detection).
   - Fix: Use `GetWithETagAsync` and `TrySaveAsync` to detect concurrent modifications, returning `StatusCodes.Conflict` on ETag mismatch.

7. **Multi-Instance Safety (T9)** - RealmService.cs:480-499 - Read-modify-write on realm model without concurrency control (deprecate path)
   - What's wrong: Same as UpdateRealmAsync -- reads, modifies, and saves the realm model without any concurrency protection.
   - Fix: Use ETag-based optimistic concurrency.

8. **Multi-Instance Safety (T9)** - RealmService.cs:530-549 - Read-modify-write on realm model without concurrency control (undeprecate path)
   - What's wrong: Same pattern -- no concurrency control on read-modify-write of realm model.
   - Fix: Use ETag-based optimistic concurrency.

9. **Multi-Instance Safety (T9)** - RealmService.cs:265-271 - Race condition on code uniqueness check
   - What's wrong: `CreateRealmAsync` checks if a code index exists and then creates the realm. Between the check and the write, another instance could create the same code, resulting in duplicates. No distributed lock protects this critical section.
   - Fix: Use a distributed lock on the code index key (e.g., `lock:realm-code:{code}`) to prevent race conditions during code uniqueness validation.

10. **Internal Model Type Safety (T25)** - RealmService.cs:853 - RealmId stored as string instead of Guid
    - What's wrong: The internal `RealmModel` POCO uses `public string RealmId { get; set; } = string.Empty;` for an entity ID that is always a GUID. This forces `Guid.Parse(model.RealmId)` calls throughout the service (lines 236, 712, 743, 771, 807) and `realmId.ToString()` when populating the model (line 282).
    - Fix: Change `RealmId` to `public Guid RealmId { get; set; }` in `RealmModel` and remove all `Guid.Parse`/`.ToString()` conversions.

11. **Configuration-First (T21)** - RealmService.cs:23,39 - Dead configuration dependency
    - What's wrong: `_configuration` is assigned in the constructor (line 39) but never referenced anywhere in the service code (no `_configuration.X` usage exists). T21 mandates "no dead config" -- every defined config property and injected configuration must be used.
    - Fix: Either remove `_configuration` from the constructor and field (since `RealmServiceConfiguration` only has `ForceServiceId` which is framework-handled), or use it for the `TryPublishErrorAsync` `serviceId` parameter instead of the hardcoded `"realm"` string.

12. **Error Handling (T7)** - RealmService.cs:61-84, 94-127, etc. - Missing ApiException catch clause
    - What's wrong: All endpoint methods catch only the generic `Exception` but do not catch `ApiException` separately. Per T7, the standard pattern requires catching `ApiException` first (for expected API errors from downstream services) and only then catching generic `Exception` for unexpected failures. While this service primarily calls state stores (which may throw `ApiException`), the pattern should still be followed for consistency and correct log levels.
    - Fix: Add `catch (ApiException ex)` before `catch (Exception ex)` in all try-catch blocks, logging at Warning level and propagating the status code.

13. **Configuration-First (T21)** - RealmService.cs:80 - Hardcoded service ID string
    - What's wrong: `TryPublishErrorAsync` uses the hardcoded string `"realm"` as the service ID (lines 80, 121, 200, 242, 321, 398, 456, 510, 560, 674). Per T21, tunables and identifiers should come from configuration. The `RealmServiceConfiguration.ForceServiceId` or the service registration name should be used instead.
    - Fix: Use `_configuration.ForceServiceId ?? "realm"` or reference the service name from the `[BannouService]` attribute.

### Category: QUALITY

14. **Logging Standards (T10)** - RealmService.cs:70,103,345,422,484,534 - "Not found" logged at Warning instead of Debug
    - What's wrong: Resource-not-found is an expected outcome (T10 says "Expected Outcomes" should be logged at Debug level). Lines 70, 103, 345, 422, 484, and 534 all log "not found" conditions at `LogWarning`. Only the data inconsistency case (line 112) is arguably appropriate at Warning level.
    - Fix: Change `_logger.LogWarning("Realm not found...")` to `_logger.LogDebug("Realm not found...")` for standard not-found cases. Keep Warning for the data inconsistency case on line 112.

15. **Logging Standards (T10)** - RealmService.cs:273,490,540 - Expected user-input conflicts logged at Warning instead of Debug
    - What's wrong: "Realm with code already exists" (line 273), "Realm already deprecated" (line 490), and "Realm is not deprecated" (line 540) are expected validation/conflict outcomes that callers can trigger through normal usage. These should be Debug, not Warning.
    - Fix: Change these `LogWarning` calls to `LogDebug`.

16. **Logging Standards (T10)** - RealmService.cs:429 - Expected conflict (not deprecated) logged at Warning instead of Debug
    - What's wrong: Line 429 logs "Cannot delete realm {Code}: realm must be deprecated first" at Warning level. This is an expected business rule outcome, not an unexpected condition.
    - Fix: Change to `LogDebug`.

17. **XML Documentation (T19)** - RealmService.cs:686 - Missing XML documentation on helper method
    - What's wrong: The `LoadRealmsByIdsAsync` method (line 686) is a private method, but per project convention, helper methods that implement significant logic should have summary documentation. More importantly, `MapToResponse` (line 708) has no XML documentation.
    - Fix: Add `/// <summary>` blocks to `LoadRealmsByIdsAsync` and `MapToResponse`.

18. **XML Documentation (T19)** - RealmService.cs:851-866 - Missing XML documentation on RealmModel properties
    - What's wrong: The `RealmModel` class has a `<summary>` on the class itself, but none of its 12 properties have XML documentation comments. While this is an `internal` class, the project convention (T19) requires documentation on all public members, and since this class is exposed via `InternalsVisibleTo` to the test project, its members are effectively part of the testable API surface.
    - Fix: Add `/// <summary>` documentation to each property of `RealmModel`.

19. **Naming Conventions (T16)** - RealmService.cs:25-27 - Constants use SCREAMING_CASE instead of PascalCase
    - What's wrong: The constants `REALM_KEY_PREFIX`, `CODE_INDEX_PREFIX`, and `ALL_REALMS_KEY` use SCREAMING_SNAKE_CASE. C# convention (and the project's naming conventions) call for PascalCase for constants (e.g., `RealmKeyPrefix`, `CodeIndexPrefix`, `AllRealmsKey`).
    - Fix: Rename constants to PascalCase.

20. **Error Handling (T7)** - RealmService.cs:754-757, 790-793, 825-828 - Event publishing failures swallowed with Warning instead of Error
    - What's wrong: The event publishing helper methods catch exceptions and log at Warning level, but `TryPublishAsync` is already safe (it handles failures internally). The outer try-catch is redundant -- if `TryPublishAsync` throws, that is an unexpected failure and should be logged at Error level (since `TryPublishAsync` is documented to never throw). However, the more likely concern is that these redundant try-catches mask a misunderstanding of `TryPublishAsync` vs `PublishAsync`.
    - Fix: Since `TryPublishAsync` is safe (doesn't throw), remove the redundant try-catch blocks around the event publishing calls. If kept, log at Error level since a throw from `TryPublishAsync` would be genuinely unexpected.

21. **Internal Model Type Safety (T25)** - RealmService.cs:853-855 - String fields with `= string.Empty` defaults on model properties
    - What's wrong: `RealmModel.RealmId`, `Code`, and `Name` use `= string.Empty` as defaults. Per CLAUDE.md rules, `?? string.Empty` and `= string.Empty` hide bugs by silently coercing null to empty. Since `RealmId` should be `Guid` (see violation #10), `Code` should not be empty (it is always set on creation), and `Name` should not be empty (it is required). These defaults mask potential data corruption.
    - Fix: For `RealmId`, change to `Guid` type. For `Code` and `Name`, consider making them required (non-nullable without default) so that deserialization failures are caught rather than silently producing empty strings.

22. **Multi-Instance Safety (T9)** - RealmService.cs:603-615 - Read-modify-write on realm model without concurrency control (seed update path)
    - What's wrong: The seed operation with `UpdateExisting=true` reads, modifies, and saves the existing realm model without any distributed lock or ETag-based concurrency control.
    - Fix: Use optimistic concurrency or distributed locks consistent with the other write operations.
