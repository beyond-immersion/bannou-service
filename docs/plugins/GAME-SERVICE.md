# Game Service Plugin Deep Dive

> **Plugin**: lib-game-service
> **Schema**: schemas/game-service-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFoundation
> **State Store**: game-service-statestore (MySQL), game-service-lock (Redis)
> **Implementation Map**: [docs/maps/GAME-SERVICE.md](../maps/GAME-SERVICE.md)

---

## Overview

The Game Service is a minimal registry (L2 GameFoundation) that maintains a catalog of available games/applications (e.g., Arcadia, Fantasia) that users can subscribe to. Provides simple CRUD operations for managing service definitions, with stub-name-based lookup for human-friendly identifiers. Internal-only, never internet-facing. Referenced by nearly all L2/L4 services for game-scoping operations.

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
| lib-game-session | L2 | Uses `IGameServiceClient` to check `autoLobbyEnabled` before publishing subscription-driven lobby shortcuts |
| lib-worldstate | L2 | Uses `IGameServiceClient` for game service scoping of time configuration and calendar templates |
| lib-faction | L4 | Uses `IGameServiceClient` for game service scoping of faction entities |

No services subscribe to game-service events.

---

## Configuration

| Property | Env Var | Default | Range | Purpose |
|----------|---------|---------|-------|---------|
| `ServiceListRetryAttempts` | `GAME_SERVICE_SERVICE_LIST_RETRY_ATTEMPTS` | 3 | 1–10 | Maximum optimistic concurrency retry attempts for service list mutations |

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

1. **Service versioning**: Track client compatibility versions to inform clients. Deployment version tracking belongs in Orchestrator (L3); a `minimumClientVersion` typed field could live here as a property of the game definition itself. No current consumer needs this.
<!-- AUDIT:NEEDS_DESIGN:2026-02-25:https://github.com/beyond-immersion/bannou-service/issues/480 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

No bugs identified.

### Intentional Quirks (Documented Behavior)

1. **Update cannot set description to null**: Since `null` means "don't change" in the update request, there's no way to explicitly set description back to null once it has a value. Setting to empty string `""` works as a workaround.

2. **No event consumers**: Three lifecycle events (`game-service.created`, `.updated`, `.deleted`) are published but no service currently subscribes to them. This is correct per FOUNDATION TENETS (Event-Driven Architecture): all meaningful state changes publish events even without current consumers. The events exist for future integration (e.g., analytics tracking, subscription service reacting to game service activation changes).

3. **Service list as single key**: `game-service-list` stores all service IDs as a single `List<Guid>`. Every create/delete reads the full list, modifies it, and writes it back with ETag-based optimistic concurrency (configurable retry via `ServiceListRetryAttempts`). This pattern is appropriate because game services represent top-level games/applications (Arcadia, Fantasia) — realistically dozens, never thousands. The simplicity of a single-key list outweighs the theoretical scaling concern.

4. **No concurrency control on updates**: `UpdateServiceAsync` uses plain `SaveAsync` without ETag-based optimistic concurrency or distributed locking. Two simultaneous updates to the same service will both succeed — last writer wins. This is acceptable because: all mutation endpoints require `admin` role, write frequency is extremely low (game service updates are rare admin operations), and there is no correctness invariant at risk (unlike `CreateServiceAsync` which uses distributed locking to enforce stub name uniqueness).

### Design Considerations (Requires Planning)

No design considerations pending.

---

## Work Tracking

### Pending

*Remaining quirks and design considerations are documented above but not yet scheduled for implementation.*
