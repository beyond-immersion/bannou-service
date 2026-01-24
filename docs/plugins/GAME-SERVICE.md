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

### Bugs (Fix Immediately)

None identified.

### Intentional Quirks (Documented Behavior)

1. **Stub names are always lowercase**: `ToLowerInvariant()` applied on creation. Input case is lost permanently — responses always return the normalized lowercase version.

2. **GUIDs stored as strings**: Internal model uses `string ServiceId` rather than `Guid`. Adds parsing overhead in `MapToServiceInfo()` but avoids serialization edge cases with the state store.

3. **Unix timestamps for dates**: `CreatedAtUnix` and `UpdatedAtUnix` stored as `long` (seconds since epoch), converted to `DateTimeOffset` in API responses.

4. **Update cannot set description to null**: Since `null` means "don't change" in the update request, there's no way to explicitly set description back to null once it has a value. However, setting to empty string `""` works — the null check (`body.Description != null`) passes for empty strings.

### Design Considerations (Requires Planning)

1. **Service list as single key**: `game-service-list` stores all service IDs as a single `List<string>`. Every create/delete reads the full list, modifies it, and writes it back. Not a problem with dozens of services, but would become a bottleneck with thousands and concurrent modifications.

2. **No event consumers**: Three lifecycle events are published but no service currently subscribes to them. They exist for future integration (e.g., analytics tracking, subscription cleanup on delete).

3. **No concurrency control on updates**: Two simultaneous updates to the same service will both succeed — last writer wins. Acceptable given admin-only access and low write frequency.

4. **Dead event handler comment**: Line 39 references `GameServiceServiceEvents.cs` for event handler registration, but this file doesn't exist. The service is wired to support event consumers but none are defined.
