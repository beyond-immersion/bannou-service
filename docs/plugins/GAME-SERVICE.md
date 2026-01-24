# Game Service Plugin Deep Dive

> **Plugin**: lib-game-service
> **Schema**: schemas/game-service-api.yaml
> **Version**: 1.0.0
> **State Store**: game-service-statestore (MySQL)

---

## Overview

The Game Service is a minimal registry that maintains a catalog of available games/applications (e.g., Arcadia, Fantasia) that users can subscribe to. It provides simple CRUD operations for managing service definitions, with stub-name-based lookup for human-friendly identifiers. Internal-only, never internet-facing.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Persistence for service registry entries and indexes |
| lib-messaging (`IMessageBus`) | Publishing lifecycle events and error events |
| lib-messaging (`IEventConsumer`) | Event handler registration (currently no handlers) |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-subscription | Calls `GetServiceAsync` via `IGameServiceClient` to resolve stub names → service IDs for subscription creation |
| lib-analytics | Uses `IGameServiceClient` for service validation |

No services subscribe to game-service events.

---

## State Storage

**Store**: `game-service-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `game-service:{serviceId}` | `GameServiceRegistryModel` | Service data (name, stub, description, active status) |
| `game-service-stub:{stubName}` | `string` | Stub name → service ID lookup index |
| `game-service-list` | `List<string>` | Master list of all service IDs |

---

## Events

### Published Events

| Topic | Trigger |
|-------|---------|
| `game-service.created` | New service entry created |
| `game-service.updated` | Service metadata modified (includes `changedFields` list) |
| `game-service.deleted` | Service permanently removed (includes optional `deletedReason`) |

All event publishing is wrapped in try-catch and uses `TryPublishAsync` (non-fatal on failure).

### Consumed Events

This plugin does not consume external events.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| — | — | — | No service-specific configuration properties |

The generated `GameServiceServiceConfiguration` contains only the framework-level `ForceServiceId` property.

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<GameServiceService>` | Singleton | Structured logging |
| `GameServiceServiceConfiguration` | Singleton | Framework config (empty) |
| `IStateStoreFactory` | Singleton | State store access |
| `IMessageBus` | Singleton | Event publishing |
| `IEventConsumer` | Singleton | Event registration (no handlers) |

Service lifetime is **Singleton** (registry is read-mostly, safe to share).

---

## API Endpoints (Implementation Notes)

**All 5 endpoints are straightforward CRUD.** Key implementation details:

- **GetService**: Dual-lookup strategy — tries GUID first, falls back to stub name index. Stub names normalized to lowercase.
- **CreateService**: Validates stub name uniqueness via composite key `game-service-stub:{name}`. Generates UUID, stores three keys (data + stub index + list). Publishes `game-service.created`.
- **UpdateService**: Only updates fields that are non-null AND different from current values. Tracks changed field names for event payload. Does not publish if nothing changed.
- **DeleteService**: Removes all three keys (data, stub index, list entry). Publishes with optional deletion reason.
- **ListServices**: Loads all service IDs from master list, bulk-fetches models, optionally filters by `activeOnly`.

---

## Visual Aid

```
State Store Key Relationships
==============================

game-service-list ──────────────► [id1, id2, id3, ...]
                                         │
                                         ▼
                              game-service:{id1} ──► GameServiceRegistryModel
                              game-service:{id2} ──► GameServiceRegistryModel
                                         │
                                         │ model.StubName
                                         ▼
                              game-service-stub:arcadia ──► "id1"
                              game-service-stub:fantasia ──► "id2"
                                         │
                                         │ (reverse lookup)
                                         ▼
                              GetService(stubName: "arcadia")
                                → index lookup → id1
                                → data lookup → full model
```

---

## Stubs & Unimplemented Features

None. The service is feature-complete for its scope.

---

## Potential Extensions

1. **Pagination for ListServices**: Currently loads all services into memory. Would matter at scale (hundreds of game services).
2. **Service metadata schema validation**: The `metadata` field could support schema validation per service type.
3. **Service versioning**: Track deployment versions to inform clients of compatibility.

---

## Known Quirks & Caveats

### Tenet Violations (Fix Immediately)

#### 1. FOUNDATION TENETS (T6) - Missing Constructor Null Checks

**File**: `plugins/lib-game-service/GameServiceService.cs`, lines 34-37
**Issue**: Constructor assigns dependencies without null-guard checks. Per T6, all injected dependencies must be validated with `?? throw new ArgumentNullException(nameof(...))` or `ArgumentNullException.ThrowIfNull(...)`.

```csharp
// CURRENT (wrong):
_stateStoreFactory = stateStoreFactory;
_messageBus = messageBus;
_logger = logger;
_configuration = configuration;

// REQUIRED:
_stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
_messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
_logger = logger ?? throw new ArgumentNullException(nameof(logger));
_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
```

Also missing: `ArgumentNullException.ThrowIfNull(eventConsumer, nameof(eventConsumer));` before the `RegisterEventConsumers` call on line 40.

#### 2. IMPLEMENTATION TENETS (T25) - String ServiceId in Internal POCO

**File**: `plugins/lib-game-service/GameServiceService.cs`, lines 526
**Issue**: `GameServiceRegistryModel.ServiceId` is `string` instead of `Guid`. T25 mandates that internal POCOs use the strongest available C# type. This causes `Guid.Parse(model.ServiceId)` scattered throughout the code (lines 386, 451, 476, 501) -- exactly the fragile pattern T25 forbids.

```csharp
// CURRENT (wrong):
public string ServiceId { get; set; } = string.Empty;

// REQUIRED:
public Guid ServiceId { get; set; }
```

Fix will also eliminate all `Guid.Parse(model.ServiceId)` calls and `serviceId.ToString()` conversions (lines 185, 195, 198, 201, 331).

#### 3. IMPLEMENTATION TENETS (T25) - String StubName/DisplayName with `= string.Empty`

**File**: `plugins/lib-game-service/GameServiceService.cs`, lines 527-528
**Issue**: While `StubName` and `DisplayName` are genuinely strings (not enum/Guid misuse), their initialization with `= string.Empty` masks potential null assignment bugs. However, this is a minor issue -- the primary T25 violation is `ServiceId` above. These are noted as the `string.Empty` default has no CLAUDE.md justification comment (acceptable patterns require a compiler-satisfaction or external-service-defensive comment).

#### 4. IMPLEMENTATION TENETS (T7) - Missing ApiException Catch in Error Handling

**File**: `plugins/lib-game-service/GameServiceService.cs`, lines 87-92, 135-140, 210-216, 285-291, 340-345
**Issue**: All five endpoint methods catch only `Exception` (generic catch). T7 requires catching `ApiException` specifically first (for expected API errors from state store or downstream services), then generic `Exception` for unexpected failures. The pattern should be:

```csharp
catch (ApiException ex)
{
    _logger.LogWarning(ex, "Service call failed with status {Status}", ex.StatusCode);
    return ((StatusCodes)ex.StatusCode, null);
}
catch (Exception ex)
{
    _logger.LogError(ex, "...");
    await PublishErrorEventAsync(...);
    return (StatusCodes.InternalServerError, null);
}
```

#### 5. QUALITY TENETS (T10) - LogInformation Used for Routine Operations (Should Be Debug)

**File**: `plugins/lib-game-service/GameServiceService.cs`, lines 50, 100-101, 148-149, 224, 299
**Issue**: Entry-point logging for routine CRUD operations uses `LogInformation` but T10 specifies "Operation Entry" should be at `Debug` level. `Information` is for "significant state changes" (business decisions), not for every request received. The lines logging "Listing services", "Getting service", etc. should be `LogDebug`. The lines logging successful creation/update/deletion (lines 203, 279, 333) are correctly `LogInformation` since those are meaningful state changes.

#### 6. QUALITY TENETS (T10) - LogWarning Used for Expected Not-Found (Should Be Debug)

**File**: `plugins/lib-game-service/GameServiceService.cs`, lines 128-129, 241, 317
**Issue**: "Service not found" is logged as `Warning`. T10 says "Expected Outcomes" (resource not found, validation failures) should be at `Debug` level. Not-found is an expected outcome, not a security event or anomaly. `Warning` is for "security events" (auth failures, permission denials).

#### 7. QUALITY TENETS (T10) - LogWarning Used for Validation Failures (Should Be Debug)

**File**: `plugins/lib-game-service/GameServiceService.cs`, lines 156, 162, 175, 230, 305
**Issue**: Validation failures ("Stub name is required", "Display name is required", "Service ID is required", "already exists") are logged as `Warning`. Per T10, validation failures are "Expected Outcomes" and should use `Debug` level. These are not security events.

#### 8. IMPLEMENTATION TENETS (T9) - No Concurrency Protection on Shared List

**File**: `plugins/lib-game-service/GameServiceService.cs`, lines 353-377
**Issue**: `AddToServiceListAsync` and `RemoveFromServiceListAsync` perform read-modify-write on `game-service-list` without distributed locking or optimistic concurrency (ETags). Since the service is `Singleton` lifetime and could run on multiple instances, concurrent creates/deletes can cause lost updates to the list. T9 requires either `IDistributedLockProvider` or ETag-based optimistic concurrency for state that requires consistency.

#### 9. FOUNDATION TENETS (T6) - Missing IStateStore Field (Re-created on Every Call)

**File**: `plugins/lib-game-service/GameServiceService.cs`, lines 55-59, 106-107, 167-168, 234, 309-310, 355, 370
**Issue**: Rather than creating the `IStateStore<T>` once in the constructor (as shown in T6 pattern: `_stateStore = stateStoreFactory.GetStore<ServiceModel>(StateStoreDefinitions.ServiceName)`), the service calls `_stateStoreFactory.GetStore<T>(StateStoreName)` inside every method. While functionally correct (the factory likely caches stores), this diverges from the T6 standardized pattern and creates unnecessary overhead. The store should be initialized once as a field.

#### 10. QUALITY TENETS (T19) - Missing Parameter Documentation

**File**: `plugins/lib-game-service/GameServiceService.cs`, lines 48, 98, 146, 222, 297
**Issue**: Public methods have `<summary>` tags but are missing `<param>` and `<returns>` documentation as required by T19.

```csharp
// CURRENT:
/// <summary>
/// List all registered game services, optionally filtered by active status.
/// </summary>
public async Task<(StatusCodes, ListServicesResponse?)> ListServicesAsync(...)

// REQUIRED:
/// <summary>
/// List all registered game services, optionally filtered by active status.
/// </summary>
/// <param name="body">Request containing filter criteria.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>Tuple of status code and list of services.</returns>
```

#### 11. QUALITY TENETS (T19) - Missing XML Documentation on Internal Model

**File**: `plugins/lib-game-service/GameServiceService.cs`, lines 524-533
**Issue**: `GameServiceRegistryModel` class has a `<summary>` but individual properties lack documentation. While this is an internal class, T19 says "all public classes, interfaces, methods, and properties" -- this class is internal so this is a minor concern. However, property-level documentation would improve maintainability.

#### 12. IMPLEMENTATION TENETS (T21) - Dead Configuration Injection

**File**: `plugins/lib-game-service/GameServiceService.cs`, lines 20, 37
**Issue**: `_configuration` is injected and assigned but never referenced anywhere in the service code. T21 states "Every defined config property MUST be referenced in service code" and "If a property is unused, remove it from the configuration schema." The configuration class only has `ForceServiceId` (framework-level), so this may be acceptable as framework boilerplate, but the field itself is unused dead code.

### Bugs (Fix Immediately)

None identified beyond the tenet violations above.

### Intentional Quirks (Documented Behavior)

1. **Stub names are always lowercase**: `ToLowerInvariant()` applied on creation. Input case is lost permanently — responses always return the normalized lowercase version.

2. **GUIDs stored as strings**: Internal model uses `string ServiceId` rather than `Guid`. Adds parsing overhead in `MapToServiceInfo()` but avoids serialization edge cases with the state store. **NOTE: This is now a formal T25 violation (see Tenet Violations #2 above) -- `BannouJson` handles Guid serialization natively, so the "edge case" justification is invalid.**

3. **Unix timestamps for dates**: `CreatedAtUnix` and `UpdatedAtUnix` stored as `long` (seconds since epoch), converted to `DateTimeOffset` in API responses.

4. **Update cannot set description to null**: Since `null` means "don't change" in the update request, there's no way to explicitly set description back to null once it has a value. However, setting to empty string `""` works — the null check (`body.Description != null`) passes for empty strings.

### Design Considerations (Requires Planning)

1. **Service list as single key**: `game-service-list` stores all service IDs as a single `List<string>`. Every create/delete reads the full list, modifies it, and writes it back. Not a problem with dozens of services, but would become a bottleneck with thousands and concurrent modifications.

2. **No event consumers**: Three lifecycle events are published but no service currently subscribes to them. They exist for future integration (e.g., analytics tracking, subscription cleanup on delete).

3. **No concurrency control on updates**: Two simultaneous updates to the same service will both succeed — last writer wins. Acceptable given admin-only access and low write frequency.

4. **Dead event handler comment**: Line 39 references `GameServiceServiceEvents.cs` for event handler registration, but this file doesn't exist. The service is wired to support event consumers but none are defined.
