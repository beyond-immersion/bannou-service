# Game Service Plugin Deep Dive

> **Plugin**: lib-game-service
> **Schema**: schemas/game-service-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFoundation
> **State Store**: game-service-statestore (MySQL), game-service-lock (Redis)

---

## Overview

The Game Service is a minimal registry (L2 GameFoundation) that maintains a catalog of available games/applications (e.g., Arcadia, Fantasia) that users can subscribe to. Provides simple CRUD operations for managing service definitions, with stub-name-based lookup for human-friendly identifiers. Internal-only, never internet-facing. Referenced by nearly all L2/L4 services for game-scoping operations.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Persistence for service registry entries and indexes |
| lib-state (`IDistributedLockProvider`) | Distributed lock on stub name uniqueness during create |
| lib-messaging (`IMessageBus`) | Publishing lifecycle events |
| lib-messaging (`IEventConsumer`) | Event handler registration (currently no handlers) |
| lib-resource (`IResourceClient`) | Reference checking and cleanup coordination on delete |
| lib-telemetry (`ITelemetryProvider`) | Span instrumentation for async helper methods |

---

## Dependents (What Relies On This Plugin)

| Dependent | Layer | Relationship |
|-----------|-------|-------------|
| lib-subscription | L2 | Calls `GetServiceAsync` via `IGameServiceClient` to resolve stub names → service IDs for subscription creation |
| lib-collection | L2 | Uses `IGameServiceClient` for game service scoping of collection types |
| lib-seed | L2 | Uses `IGameServiceClient` for game service scoping of seed types |
| lib-analytics | L4 | Uses `IGameServiceClient` for service validation |
| lib-status | L4 | Uses `IGameServiceClient` for game service scoping of status templates |
| lib-license | L4 | Uses `IGameServiceClient` for game service scoping of license boards |
| lib-faction | L4 | Uses `IGameServiceClient` for game service scoping of faction entities |

No services subscribe to game-service events.

---

## State Storage

**Store**: `game-service-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `game-service:{serviceId}` | `GameServiceRegistryModel` | Service data (name, stub, description, active status) |
| `game-service-stub:{stubName}` | `string` | Stub name → service ID lookup index |
| `game-service-list` | `List<Guid>` | Master list of all service IDs |

**Store**: `game-service-lock` (Backend: Redis)

| Key Pattern | Purpose |
|-------------|---------|
| `game-service-stub:{stubName}` | Distributed lock for stub name uniqueness during create |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `game-service.created` | `GameServiceCreatedEvent` | New service entry created |
| `game-service.updated` | `GameServiceUpdatedEvent` | Service metadata modified (includes `changedFields` list) |
| `game-service.deleted` | `GameServiceDeletedEvent` | Service permanently removed (includes optional `deletedReason`) |

All event publishing uses `TryPublishAsync` (non-fatal on failure — the method internally handles exceptions).

### Consumed Events

This plugin does not consume external events.

---

## Configuration

| Property | Env Var | Default | Range | Purpose |
|----------|---------|---------|-------|---------|
| `ServiceListRetryAttempts` | `GAME_SERVICE_SERVICE_LIST_RETRY_ATTEMPTS` | 3 | 1–10 | Maximum optimistic concurrency retry attempts for service list mutations |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<GameServiceService>` | Singleton | Structured logging |
| `GameServiceServiceConfiguration` | Singleton | Service configuration (retry attempts) |
| `IStateStoreFactory` | Singleton | State store access |
| `IMessageBus` | Singleton | Event publishing |
| `IEventConsumer` | Singleton | Event registration (no handlers) |
| `IDistributedLockProvider` | Singleton | Distributed locks for stub name uniqueness |
| `IResourceClient` | Scoped | Reference checking and cleanup on delete |
| `ITelemetryProvider` | Singleton | Telemetry span creation |

Service lifetime is **Singleton** (registry is read-mostly, safe to share).

---

## API Endpoints (Implementation Notes)

**All 5 endpoints are straightforward CRUD.** Key implementation details:

- **GetService**: Dual-lookup strategy — tries GUID first, falls back to stub name index. Stub names normalized to lowercase.
- **CreateService**: Acquires distributed lock on normalized stub name, then validates uniqueness via composite key `game-service-stub:{name}`. Generates UUID, stores three keys (data + stub index + list). Publishes `game-service.created`.
- **UpdateService**: Only updates fields that are non-null AND different from current values. Tracks changed field names for event payload. Does not publish if nothing changed.
- **DeleteService**: Checks external references via `IResourceClient.CheckReferencesAsync`. If references exist, executes cleanup callbacks with `ALL_REQUIRED` policy — returns 409 Conflict if cleanup fails or is refused. On success, removes all three keys (data, stub index, list entry). Publishes with optional deletion reason.
- **ListServices**: Loads all service IDs from master list, bulk-fetches models, optionally filters by `activeOnly`. Supports pagination via `skip`/`take` parameters (default 0/50, max take 200). Returns `totalCount` reflecting total matching items before pagination.

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

1. **Service metadata schema validation**: The `metadata` field could support schema validation per service type.
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/228 -->
2. **Service versioning**: Track deployment versions to inform clients of compatibility.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

No bugs identified.

### Intentional Quirks (Documented Behavior)

1. **Update cannot set description to null**: Since `null` means "don't change" in the update request, there's no way to explicitly set description back to null once it has a value. Setting to empty string `""` works as a workaround.

### Design Considerations (Requires Planning)

1. **Service list as single key**: `game-service-list` stores all service IDs as a single `List<Guid>`. Every create/delete reads the full list, modifies it, and writes it back with ETag-based optimistic concurrency (configurable retry via `ServiceListRetryAttempts`). Not a problem with dozens of services, but would become a bottleneck with thousands.

2. **No event consumers**: Three lifecycle events are published but no service currently subscribes to them. They exist for future integration (e.g., analytics tracking).

3. **No concurrency control on updates**: Two simultaneous updates to the same service will both succeed — last writer wins. Acceptable given admin-only access and low write frequency.

4. **`autoLobbyEnabled` property for game entry mode**: GameSession (L2) currently publishes naive lobby shortcuts for all subscribed games on `session.connected`. Games with rich entry orchestration (e.g., Arcadia via Gardener L4) don't want this — their entry flow is managed by higher-layer services. A boolean `autoLobbyEnabled` property (default `true`) on the `GameServiceRegistryModel` would let games declare their entry mode. GameSession checks this before publishing shortcuts, skipping games where `autoLobbyEnabled: false`. This allows naive-lobby games and orchestrated-entry games to coexist in the same deployment. The property belongs here (on the game definition) because it describes how a game wants players to enter — a property of the game, not of GameSession's deployment topology.

---

## Work Tracking

### Completed

- **2026-02-24**: L3 hardening pass — T26 (removed Guid.Empty sentinels), T30 (telemetry spans), T9 (distributed lock on stub name create), T21 (config for retry attempts, removed dead code), T28 (lib-resource cleanup on delete), x-resource-lifecycle schema, updated dependents list (5 missing), misleading event handler comment fixed

### Pending

*Remaining quirks and design considerations are documented above but not yet scheduled for implementation.*
