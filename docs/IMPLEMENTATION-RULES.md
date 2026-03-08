# Implementation Rules Quick Reference

> **Purpose**: Concentrated reference for implementing service methods and writing implementation maps.
> **Authority**: This document summarizes rules from the [Tenets](reference/TENETS.md). The tenet documents are authoritative; this is a focused subset.
> **Related**: [Implementation Map Template](reference/IMPLEMENTATION-MAP-TEMPLATE.md)

---

## 1. Method Return Pattern

All service methods return `(StatusCodes, TResponse?)` tuples using `BeyondImmersion.BannouService.StatusCodes` (not `Microsoft.AspNetCore.Http.StatusCodes`). Error responses return `null` as the second element. Success responses must not contain filler properties — every field must provide information the caller cannot derive from the status code alone.

```
RETURN (200, EntityResponse)     // Success with data
RETURN (404, null)               // Not found
RETURN (409, null)               // Conflict (ETag mismatch, lock failure, duplicate)
RETURN (400, null)               // Bad request (validation failure)
```

Forbidden in success responses: confirmation booleans (`deleted: true`), echoed request fields, action timestamps (`executedAt`), confirmation messages.

**See**: IMPLEMENTATION TENETS in `tenets/IMPLEMENTATION-BEHAVIOR.md`

---

## 2. Error Handling

The generated controller provides the catch-all error boundary for every endpoint — it logs the exception, publishes an error event, and returns 500. **Do not duplicate this in service methods.**

Service-level try-catch is permitted only for:
- **Inter-service calls**: Catch `ApiException` from generated clients to translate upstream failures
- **Specific recovery**: Catch a specific exception type when there is meaningful recovery logic (e.g., rollback an index on ETag failure)

Error events are published via `_messageBus.TryPublishErrorAsync()` for **unexpected/internal failures only** — never for validation errors or expected 404/409 responses.

**See**: IMPLEMENTATION TENETS in `tenets/IMPLEMENTATION-BEHAVIOR.md`

---

## 3. State Access

All state access goes through infrastructure libs — never direct database or cache connections.

- **State stores**: `IStateStoreFactory.GetStore<T>(StateStoreDefinitions.{Store})` — cache the reference in the constructor
- **Optimistic concurrency**: `GetWithETagAsync()` + `TrySaveAsync()` — return 409 Conflict on ETag mismatch
- **Queryable stores**: `IJsonQueryableStateStore<T>` for MySQL JSON queries, `ICacheableStateStore<T>` for Redis sets/sorted sets

```csharp
// Constructor — cache store references
_accountStore = stateStoreFactory.GetStore<AccountModel>(StateStoreDefinitions.Account);
```

**See**: FOUNDATION TENETS in `tenets/FOUNDATION.md`

---

## 4. Distributed Locking

Use `IDistributedLockProvider` for operations requiring mutual exclusion (uniqueness enforcement, TOCTOU prevention). Return 409 Conflict if the lock cannot be acquired.

```csharp
await using var lockResponse = await _lockProvider.LockAsync(
    StateStoreDefinitions.AccountLock, lockKey, ownerId, expirySeconds, ct);
if (!lockResponse.Success)
    return (StatusCodes.Conflict, null);
```

No in-memory authoritative state across instances. Use `ConcurrentDictionary` for local caches only — never as the source of truth. All coordination must go through distributed state (Redis/MySQL).

**See**: IMPLEMENTATION TENETS in `tenets/IMPLEMENTATION-BEHAVIOR.md`

---

## 5. Event Publishing

All state-changing operations must publish typed events. Use `x-lifecycle` in the events schema for standard CRUD lifecycle events. Events are published via `_messageBus.TryPublishAsync()` which handles buffering and retry internally.

```
// After mutation succeeds
PUBLISH entity.updated { entityId, changedFields: [...] }
```

Event types must be defined in the service's events schema (`*-events.yaml`). No anonymous objects. No publishing to another service's topic namespace.

**See**: FOUNDATION TENETS in `tenets/FOUNDATION.md`

---

## 6. Event Consumption

Multi-handler event consumption uses `IEventConsumer` with `RegisterEventConsumers` called in the service constructor. Event handler implementations go in `*ServiceEvents.cs` (the partial class).

```csharp
// In constructor
RegisterEventConsumers(eventConsumer);

// In *ServiceEvents.cs (partial class)
protected void RegisterEventConsumers(IEventConsumer eventConsumer)
{
    eventConsumer.RegisterHandler<IMyService, EntityDeletedEvent>(
        "entity.deleted",
        async (svc, evt) => await ((MyService)svc).HandleEntityDeletedAsync(evt));
}

public async Task HandleEntityDeletedAsync(EntityDeletedEvent evt) { ... }
```

**See**: IMPLEMENTATION TENETS in `tenets/IMPLEMENTATION-BEHAVIOR.md`

---

## 7. Client Events

Server-to-client WebSocket push uses `IClientEventPublisher`, not `IMessageBus`. Client events are defined in `*-client-events.yaml` and target specific sessions.

```csharp
await _clientEventPublisher.PublishToSessionAsync(sessionId, clientEvent, ct);
```

**See**: IMPLEMENTATION TENETS in `tenets/IMPLEMENTATION-BEHAVIOR.md`

---

## 8. Cross-Service Communication

| Direction | Mechanism | Example |
|-----------|-----------|---------|
| Higher layer calls lower layer | Generated client via lib-mesh | L4 calls `ICharacterClient` (L2) |
| Lower layer needs higher layer data | DI Provider interface (`IEnumerable<T>`) | Actor (L2) discovers `IVariableProviderFactory` from L4 |
| Lower layer notifies higher layer | DI Listener interface (local-only push) | Seed (L2) fires `ISeedEvolutionListener` |
| Broadcast notification | `IMessageBus` events | Any layer publishes, any layer subscribes |

Never: lower-layer subscribing to higher-layer events for data acquisition, publishing events instead of calling APIs when the hierarchy permits direct calls, lower-layer caching higher-layer data.

**See**: FOUNDATION TENETS in `tenets/FOUNDATION.md`

---

## 9. Resource Cleanup

Dependent data cleanup uses lib-resource exclusively — never subscribe to `*.deleted` lifecycle events for cleanup purposes.

- **Producers** (services with dependent data in higher layers): Call `_resourceClient.ExecuteCleanupAsync()` in their delete flow
- **Consumers** (services that store data referencing other entities): Implement `ISeededResourceProvider` and register with lib-resource

Exceptions: Account-owned data uses `account.deleted` event subscription instead of lib-resource (Account Deletion Cleanup Obligation — privacy prohibits centralized account reference tracking). High-frequency instance cleanup (items at scale) uses DI Listener pattern instead. See FOUNDATION TENETS for both exceptions.

**See**: FOUNDATION TENETS in `tenets/FOUNDATION.md`

---

## 10. Telemetry

All async helper methods and internal operations get `StartActivity` spans. Controller-level spans are auto-generated — do not add them manually.

```csharp
using var activity = _telemetryProvider.StartActivity("bannou.{service}", "{Class}.{Method}");
```

Only async methods need spans. Synchronous methods do not.

**See**: IMPLEMENTATION TENETS in `tenets/IMPLEMENTATION-BEHAVIOR.md`

---

## 11. Type Safety

All models — request, response, event, configuration, internal — use proper types:
- **Enums** for finite value sets (never strings with `Enum.Parse`)
- **Guid** for identifiers (never strings)
- **DateTimeOffset** for timestamps
- **Nullable types** for optional/absent values (never sentinel values)

Forbidden sentinel values: `Guid.Empty` for "none", `-1` for "no index", empty string for "absent". Use `Guid?`, `int?`, `string?` instead.

**See**: IMPLEMENTATION TENETS in `tenets/IMPLEMENTATION-DATA.md`

---

## 12. Configuration

All service configuration uses generated config classes from `*-configuration.yaml` schemas. No `Environment.GetEnvironmentVariable()`. No hardcoded tunables. No dead configuration properties.

Every configurable value (timeouts, batch sizes, thresholds, feature flags) must be defined in the configuration schema and accessed via the generated configuration class.

**See**: IMPLEMENTATION TENETS in `tenets/IMPLEMENTATION-DATA.md`

---

## 13. Deprecation

Two categories with different lifecycles:

| Category | Entities | Deprecation | Deletion | Undeprecation |
|----------|----------|-------------|----------|---------------|
| **A** (Definitions) | Species, locations, relationship types | Required before delete | Allowed after deprecation | Allowed (reversible) |
| **B** (Templates) | Quest definitions, contract templates | One-way | Never deleted | Not allowed |

Both categories use the triple-field model: `IsDeprecated`, `DeprecatedAt`, `DeprecationReason`. Deprecation is always idempotent (return 200 OK if already deprecated). Publish via `*.updated` event with `changedFields` — no dedicated deprecation events.

**See**: IMPLEMENTATION TENETS in `tenets/IMPLEMENTATION-BEHAVIOR.md`

---

## 14. Polymorphic Entities

For services handling multiple entity types:

- **L1/L2 entity references**: Use the `EntityType` enum (shared across the hierarchy)
- **Game-configurable content types**: Use opaque strings (not enums) — allows new types without schema changes
- **Composite state keys**: `{entityType}:{entityId}` format for polymorphic ownership

Decision tree: If the valid set of types is the same as `EntityType` values, use `EntityType`. If the valid set includes non-entity roles or game-specific codes, define a service-specific enum or use opaque strings.

**See**: IMPLEMENTATION TENETS in `tenets/IMPLEMENTATION-DATA.md`

---

## 15. Endpoint Permissions

Every endpoint MUST declare `x-permissions` in the schema. The permission level determines who can call the endpoint via WebSocket — it is not optional and there are no defaults.

Seven levels: Pre-Auth Public (`role: anonymous`), Authenticated User (`role: user`), State-Gated User (`role: user` + `states`), Developer (`role: developer`), Admin (`role: admin`), Service-to-Service (`[]`), and Browser-Facing (T15 exception).

Key rules:
- **Default to `[]`** (service-to-service) — add a role only when you have a concrete WebSocket use case
- **State-gate session-like contexts** using the Entry/Context/Exit pattern (entry = `role: user`, in-context = `role: user` + states, exit = `role: user` + states)
- **Never use `role: admin` when you mean `[]`** — admin means a human administrator with a WebSocket session; `[]` means automated service-to-service access
- **Each endpoint gets its own permission level** — independent of other endpoints on the same service

**See**: [ENDPOINT-PERMISSION-GUIDELINES.md](reference/ENDPOINT-PERMISSION-GUIDELINES.md) for the complete decision framework, and FOUNDATION TENETS T13 in `tenets/FOUNDATION.md`
