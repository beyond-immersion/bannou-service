# Implementation Tenets: Service Behavior & Contracts

> **Category**: How services communicate, respond, and manage lifecycles
> **When to Reference**: While designing service method behavior — event flow, error handling, response shape, distributed safety, client push, tracing, entity lifecycles
> **Tenets**: T3, T7, T8, T9, T17, T30, T31
> **Source Code Category**: `IMPLEMENTATION TENETS` (shared with [Data Modeling & Code Discipline](IMPLEMENTATION-DATA.md))

These tenets define what a service method **does** at runtime.

> **Note**: Schema Reference Hierarchy (formerly T26) is now covered in [SCHEMA-RULES.md](../SCHEMA-RULES.md) and referenced by Tenet 1 in [TENETS.md](../TENETS.md).

---

## Tenet 3: Event Consumer Fan-Out (MANDATORY)

**Rule**: Services subscribing to pub/sub events MUST use `IEventConsumer` for multi-plugin event handling.

RabbitMQ allows only ONE consumer per queue. When multiple plugins need the same event, `IEventConsumer` provides application-level fan-out: generated controllers receive events and dispatch via `DispatchAsync()`, services register handlers in `{Service}ServiceEvents.cs`, and all registered handlers receive every event, isolated from each other's failures.

### Defining Event Subscriptions

In `{service}-events.yaml`:

```yaml
info:
  title: MyService Events
  version: 1.0.0
  x-event-subscriptions:
    - topic: account.deleted
      event: AccountDeletedEvent
      handler: HandleAccountDeleted      # Method name without Async suffix
```

### Generated Files

Running `make generate` produces:

- `Generated/{Service}EventsController.cs` - Event subscription handlers (always regenerated)
- `{Service}ServiceEvents.cs` - Handler registrations (generated once, then manual)

### Implementation Pattern

In the service constructor (see Tenet 6 for full pattern):
```csharp
ArgumentNullException.ThrowIfNull(eventConsumer, nameof(eventConsumer));
RegisterEventConsumers(eventConsumer);
```

In `{Service}ServiceEvents.cs` (partial class):
```csharp
protected void RegisterEventConsumers(IEventConsumer eventConsumer)
{
    eventConsumer.RegisterHandler<IMyService, AccountDeletedEvent>(
        "account.deleted",
        async (svc, evt) => await ((MyService)svc).HandleAccountDeletedAsync(evt));
}

public async Task HandleAccountDeletedAsync(AccountDeletedEvent evt)
{
    // Business logic here
}
```

Registration is idempotent. Handlers are isolated (one throwing doesn't prevent others). `IEventConsumer` is singleton.

---

## Tenet 7: Error Handling (STANDARDIZED)

**Rule**: Use specific exception types where available. Let unexpected exceptions propagate to the generated controller's catch-all boundary.

### Generated Controller Exception Boundary (DO NOT DUPLICATE)

The generated controller wraps **every** service method call in a try-catch that handles:
- `ApiException` → logs warning, returns 503
- `Exception` → logs error, calls `TryPublishErrorAsync`, records telemetry error, returns 500

```csharp
// GENERATED CONTROLLER (auto-generated, do NOT replicate in service code):
try
{
    var (statusCode, result) = await _implementation.GetAccountAsync(body, cancellationToken);
    return ConvertToActionResult(statusCode, result);
}
catch (ApiException ex_)
{
    logger_.LogWarning(ex_, "Dependency error in {Endpoint}", "post:account/get");
    activity_?.SetStatus(ActivityStatusCode.Error, "Dependency error");
    return StatusCode(503);
}
catch (Exception ex_)
{
    logger_.LogError(ex_, "Unexpected error in {Endpoint}", "post:account/get");
    await messageBus_.TryPublishErrorAsync("account", "GetAccount", ...);
    activity_?.SetStatus(ActivityStatusCode.Error, ex_.Message);
    return StatusCode(500);
}
```

**Service methods do NOT need top-level try-catch blocks.** If a state store, message bus, or lock provider call throws an unexpected exception inside a service method, it propagates to the generated controller which already handles logging, error event publishing, telemetry, and HTTP 500 response. Adding redundant try-catches in service methods would either swallow exceptions the controller should see, or catch-and-rethrow for no benefit.

### When Service-Level Try-Catch IS Required

Service methods need their own try-catch in exactly two situations:

**1. Inter-service calls via generated clients** (e.g., `IItemClient`, `ICharacterClient`):
```csharp
// CORRECT: Catch ApiException on inter-service calls to map status codes
try
{
    var (status, character) = await _characterClient.GetCharacterAsync(request, ct);
    if (status != StatusCodes.OK) return (status, null);
    // ... use character
}
catch (ApiException ex)
{
    _logger.LogWarning(ex, "Character service call failed with status {Status}", ex.StatusCode);
    return ((StatusCodes)ex.StatusCode, null);
}
```

**2. Specific recovery logic** (e.g., partial failure in a loop, graceful degradation):
```csharp
// CORRECT: Catch around a specific operation where you want to recover, not crash
foreach (var id in accountIds)
{
    try
    {
        var account = await accountStore.GetAsync(key, ct);
        results.Add(account);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to load account {AccountId}", id);
        failures.Add(new BulkOperationFailure { AccountId = id, Error = ex.Message });
    }
}
```

### When ApiException Handling Applies

The `ApiException` catch is ONLY required for **inter-service calls** (generated clients like `IItemClient`, `IServiceNavigator`, `IMeshClient`). NOT required for state store operations, message bus operations, lock provider operations, or local business logic. For these, let exceptions propagate to the generated controller.

### Error Event Publishing

`IMessageBus.TryPublishErrorAsync` is ALWAYS safe to call (internal try/catch prevents propagation). Returns `false` on failure. When an operation fails unexpectedly: log the error, call `TryPublishErrorAsync`, return failure for the ORIGINAL reason.

**Emit for**: Unexpected exceptions, infrastructure failures, programming errors caught at runtime.
**Do NOT emit for**: Validation (400), authentication (401), authorization (403), not found (404), conflicts (409).

### Instance Identity in Error Events (MANDATORY)

Every `ServiceErrorEvent` carries three identity fields that distinguish **which node** emitted the error in a distributed deployment:

| Field | Source | What It Identifies |
|-------|--------|--------------------|
| `serviceId` | `IMeshInstanceIdentifier.InstanceId` | The unique node/process (e.g., "which of the 5 Character nodes") |
| `serviceName` | Caller-provided string | The logical service (e.g., "character", "mesh", "messaging") |
| `appId` | `IServiceConfiguration.EffectiveAppId` | The deployment identity (e.g., "bannou", "bannou-npc-pool-3") |

**Callers MUST NOT provide instance identity.** The `TryPublishErrorAsync` method injects `serviceId` and `appId` internally from `IMeshInstanceIdentifier` and `IServiceConfiguration`. Callers provide only the logical `serviceName` and operational context:

```csharp
// CORRECT: Pass logical service name, let the bus handle instance identity
await _messageBus.TryPublishErrorAsync(
    "character",                    // serviceName: logical service
    "DeleteCharacter",              // operation: what failed
    ex.GetType().Name,              // errorType: exception class
    ex.Message,                     // message: human-readable
    dependency: "state",            // optional: external dependency involved
    endpoint: "redis:character",    // optional: specific endpoint
    stack: ex.StackTrace);          // optional: stack trace

// FORBIDDEN: Never construct ServiceErrorEvent directly
var errorEvent = new ServiceErrorEvent   // NO! Only RabbitMQMessageBus does this
{
    ServiceId = Guid.NewGuid(),          // NO! Instance ID comes from IMeshInstanceIdentifier
    ServiceName = "character",
    AppId = "bannou",                    // NO! AppId comes from IServiceConfiguration
    // ...
};
```

**Why this matters**: In a distributed deployment with multiple instances of the same service, `serviceId` (from `IMeshInstanceIdentifier`) is the only way to correlate errors to the specific node that produced them. Using `Guid.NewGuid()`, fixed strings, or configuration-based values would make error events useless for debugging multi-node issues.

**`IMeshInstanceIdentifier` priority**: `MESH_INSTANCE_ID` env var > `--force-service-id` CLI > random GUID (stable for process lifetime). Registered as singleton by lib-mesh (L0).

### Error Granularity & Log Levels

Services MUST distinguish: 400 Bad Request, 401 Unauthorized, 403 Forbidden, 404 Not Found, 409 Conflict, 500 Internal Server Error.

- **LogWarning**: Expected failures (timeouts, transient failures, downstream API errors)
- **LogError**: Unexpected failures that should trigger error event emission

---

## Tenet 8: Return Pattern (MANDATORY)

**Rule**: All service methods MUST return `(StatusCodes, TResponse?)` tuples using `BeyondImmersion.BannouService.StatusCodes` (NOT `Microsoft.AspNetCore.Http.StatusCodes`).

```csharp
return (StatusCodes.OK, response);              // Success
return (StatusCodes.NotFound, null);            // Resource doesn't exist
return (StatusCodes.InternalServerError, null); // Unexpected failure
```

### Empty Payload for Error Responses (ABSOLUTE)

Error responses MUST return `null` as the second tuple element. Status codes are sufficient (400=validation, 404=not found, 409=conflict). Error message strings aren't programmatically actionable and risk leaking internal details. For detailed errors (like compilation failures), log server-side.

```csharp
// WRONG: Structured error response - status code already communicates the failure
return (StatusCodes.BadRequest, new CompileResponse { Success = false, Errors = errors });

// CORRECT: Null payload, status code tells the story
return (StatusCodes.BadRequest, null);
```

### No Filler Properties in Success Responses (ABSOLUTE)

The same principle applies to success responses: **every property in a response type MUST provide information the caller cannot derive from the status code alone.** A 200 OK already communicates "the operation succeeded." Properties that merely restate this fact are filler — they exist because someone assumed a response object needed fields in it, not because the caller needs the data.

**Filler properties are FORBIDDEN in response schemas.** If removing a property would leave the caller with exactly the same information (because the status code already communicated it), that property should not exist.

#### Filler Patterns (FORBIDDEN)

| Pattern | Example | Why It's Filler |
|---------|---------|-----------------|
| **Success boolean** | `locked: true`, `deleted: true`, `executed: true` | 200 OK already says the operation succeeded |
| **Confirmation message** | `message: "Registration complete"` | Human-readable restatement of 200 OK |
| **Action timestamp** | `registeredAt`, `recompiledAt`, `executedAt` | Confirms "yes, this happened just now" — obvious from receiving 200 OK |
| **Request echo** | `appId` echoed back from the request | Caller already knows what they sent |
| **Healthy boolean** | `healthy: true` on a health endpoint | If the service answered 200, it's healthy |
| **Observability metrics** | `failedPushCount`, `totalTokenCount` | Internal operational metrics, not caller-actionable data |

#### What IS Meaningful (REQUIRED to keep)

| Pattern | Example | Why It's Meaningful |
|---------|---------|---------------------|
| **Resource ID** | `contractId` on a create response | Caller needs this to reference the resource |
| **Computed state** | `healthyCount`, `totalCount` on a list | Derived values caller couldn't compute from request |
| **Entity timestamps** | `createdAt` on a GET response | Part of the entity's stored state, not a confirmation |
| **Changed state** | `newPhase`, `capabilities` | Side effects the caller needs to know about |
| **Operational data** | `nextHeartbeatSeconds`, `ttlSeconds` | Caller needs this to schedule future actions |
| **Cache/version info** | `version`, `etag` | Caller needs this for cache invalidation or optimistic concurrency |

#### The Litmus Test

> **"If I deleted this property from the response, would the caller lose any information they didn't already have from the status code and their own request?"**

- **YES** → Property is meaningful. Keep it.
- **NO** → Property is filler. Remove it from the schema.

#### When a Response Would Be Empty

If removing all filler leaves a response with zero properties, the response type should still exist in the schema (NSwag requires it), but it should be an empty object with a description explaining that the status code is the response:

```yaml
LockContractResponse:
  type: object
  description: Empty response. HTTP 200 confirms the lock succeeded.
  properties: {}
```

```csharp
// Implementation returns the empty response type
return (StatusCodes.OK, new LockContractResponse());
```

This is cleaner than inventing filler fields to make the response "look" like it has content.

---

## Tenet 9: Multi-Instance Safety (MANDATORY)

**Rule**: Services MUST be safe to run as multiple instances across multiple nodes.

### Requirements

1. **No in-memory state** that isn't reconstructible from lib-state stores
2. **Use atomic state operations** for state requiring consistency
3. **Use ConcurrentDictionary** for local caches, never plain Dictionary
4. **Use IDistributedLockProvider** for cross-instance coordination

### Event-Backed Local Caches (ACCEPTABLE)

Local caches are acceptable when **both**: (1) loaded via API on startup from authoritative source, and (2) kept current via event subscriptions. The authoritative state must live in a service or lib-state store. Local state as the *only* source of truth is NOT acceptable.

```csharp
// ACCEPTABLE: Event-backed local cache (authoritative state in Subscription service)
private static readonly ConcurrentDictionary<Guid, HashSet<string>> _accountSubscriptions = new();
```

### Distributed Lock Pattern

Lock stores are schema-first — defined in `schemas/state-stores.yaml` and referenced via `StateStoreDefinitions` constants (same as all state stores per T4). The `storeName` parameter MUST be a `StateStoreDefinitions` constant, never a hardcoded string.

```csharp
// CORRECT: Lock store from StateStoreDefinitions, resource ID identifies the specific resource
var lockOwner = $"create-sub-{Guid.NewGuid():N}";
await using var lockResponse = await _lockProvider.LockAsync(
    StateStoreDefinitions.SubscriptionLock,
    $"account:{body.AccountId}:service:{body.ServiceId}",
    lockOwner,
    _configuration.LockTimeoutSeconds,
    cancellationToken: ct);
if (!lockResponse.Success) return (StatusCodes.Conflict, null);

// FORBIDDEN: Hardcoded lock store name
await using var lockResponse = await _lockProvider.LockAsync(
    "my-lock-store",  // Must use StateStoreDefinitions constant
    ...);
```

### Optimistic Concurrency with ETags

```csharp
var (value, etag) = await _stateStore.GetWithETagAsync(key, ct);
var saved = await _stateStore.TrySaveAsync(key, modifiedValue, etag, ct);
if (!saved) return (StatusCodes.Conflict, null);
```

### Shared Security Components (CRITICAL)

Security-critical shared components (salts, keys, secrets) MUST use consistent shared values across all instances, NEVER generate unique values per instance. Client shortcuts from instance A must work on instance B; reconnection tokens from A must validate on B.

```csharp
// CORRECT - shared/deterministic (consistent across instances)
_serverSalt = GuidGenerator.GetSharedServerSalt();

// WRONG - per-instance random generation breaks distributed deployment
_serverSalt = GuidGenerator.GenerateServerSalt();
```

**Detection**: If a service has Singleton lifetime + generates cryptographic values in constructor + participates in distributed deployment → MUST use shared/deterministic values.

---

## Tenet 17: Client Event Schema Pattern (RECOMMENDED)

**Rule**: Services pushing events to WebSocket clients MUST define them in `{service}-client-events.yaml`.

| Type | File | Purpose | Consumers |
|------|------|---------|-----------|
| **Client Events** | `{service}-client-events.yaml` | Pushed TO clients via WebSocket | Game clients, SDK |
| **Service Events** | `{service}-events.yaml` | Service-to-service pub/sub | Other Bannou services |

| Exchange | Type | Purpose |
|----------|------|---------|
| `bannou` | Fanout | Service events via `IMessageBus` |
| `bannou-client-events` | Direct | Client events via `IClientEventPublisher` (per-session routing) |

```csharp
// CORRECT: Uses direct exchange with per-session routing
await _clientEventPublisher.PublishToSessionAsync(sessionId, clientEvent);

// WRONG: Uses fanout exchange - never reaches client
await _messageBus.PublishAsync($"CONNECT_SESSION_{sessionId}", clientEvent);
```

---

## Tenet 30: Telemetry Span Instrumentation (MANDATORY)

**Rule**: All async methods in service code MUST create a telemetry span via `ITelemetryProvider.StartActivity`. This applies to generated controller wrappers, helper DI services, and async methods in service implementation classes (except the primary methods that generated controllers already wrap).

### Why This Matters

Bannou's telemetry system builds on .NET's `System.Diagnostics.Activity` and `System.Diagnostics.Metrics`. With an exporter configured (OTLP, Prometheus, console), spans provide a 4-level hierarchy that answers "where is time being spent?" without printf-debugging:

```
Mesh span (transport + everything)
  └─ Controller span (endpoint handler, without transport)
       ├─ HelperService.MethodA (domain logic chunk)
       │    ├─ StateStore.GetAsync (already instrumented via WrapStateStore)
       │    └─ MessageBus.TryPublishAsync (already instrumented)
       └─ HelperService.MethodB
            └─ MeshClient.InvokeMethodAsync (already instrumented via lib-mesh)
```

- **Mesh span**: Already exists — lib-mesh instruments all inter-service calls
- **Controller span**: Generated into controller wrappers — measures endpoint execution without transport
- **Helper/service spans**: Added per this tenet — measures domain logic chunks
- **Infrastructure spans**: Already exist — lib-state, lib-messaging, lib-mesh wrap operations

### The Zero-Signature-Change Pattern

`Activity.Current` is ambient via `AsyncLocal<T>`. When a controller span starts an `Activity`, every async method called within that `await` chain automatically sees it as the parent. Child spans nest automatically. **No parameter passing or signature changes are required.**

```csharp
// In a helper DI service — span automatically nests under the controller span
public async Task<TicketModel?> ResolveTicketAsync(Guid ticketId, CancellationToken ct)
{
    using var activity = _telemetryProvider.StartActivity(
        "bannou.matchmaking", "TicketResolver.ResolveTicket");

    var ticket = await _stateStore.GetAsync(key, ct);  // state store span nests under this
    if (ticket == null) return null;

    await _permissionClient.ClearSessionStateAsync(...);  // mesh span nests under this
    return ticket;
}
```

### Scope Rules

| Code Location | Span Required? | How |
|---------------|---------------|-----|
| **Generated controllers** | Yes | Code generation adds spans automatically |
| **Helper DI services** (`Services/*.cs`) | Yes — all `async` methods | Manual: add `StartActivity` call |
| **Service implementation** (`*Service.cs`) | Yes — async helper methods only | Manual: add `StartActivity` call |
| **Service implementation** — primary interface methods | No | Controller span already covers these |
| **Event handlers** (`*ServiceEvents.cs`) | Yes — all `async` handlers | Manual: add `StartActivity` call |
| **Infrastructure libs** (lib-state, lib-messaging, lib-mesh) | Already instrumented | No action needed |

### The Async Heuristic

"If it's `async`, it gets a span" — including methods with `await Task.CompletedTask` that are currently synchronous. Those methods are async because they logically should (or will) contain awaitable operations. The span costs nothing when telemetry is disabled (`StartActivity` returns null, all `?.SetTag` calls no-op) and will provide value when the method eventually gains real async work.

### Naming Convention

Activity names follow the pattern `{component}.{class}.{method}`:

```csharp
// Component is the service's telemetry component name
_telemetryProvider.StartActivity("bannou.matchmaking", "QueueProcessor.ProcessQueue");
_telemetryProvider.StartActivity("bannou.account", "AccountLookupHelper.ResolveByEmail");
_telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingServiceEvents.HandleSessionDisconnected");
```

### What NOT to Instrument

- **Pure synchronous computation** (non-async methods): These are CPU-bound and show up as the gap between async spans. The gap is almost always trivial.
- **Trivial property accessors or validation helpers**: Only instrument methods that represent meaningful units of work.
- **Generated code**: Never add spans to `*/Generated/` files — instrument via code generation templates instead.

### Implementation Priority

1. **Generated controller spans** — highest value per effort, zero ongoing maintenance. Add to NSwag templates so every endpoint gets a span automatically.
2. **Helper DI service spans** — manual but targeted. Add to all async methods in `Services/*.cs` files within plugins.
3. **Service implementation async helpers** — the private async methods in `*Service.cs` that aren't primary interface methods.
4. **Event handler spans** — async handlers in `*ServiceEvents.cs`.

### Dependency

Services that need to create spans must have access to `ITelemetryProvider`. This is already available via DI (constructor injection) in all services since it's an L0 infrastructure dependency. Helper DI services should accept `ITelemetryProvider` in their constructors.

---

## Tenet 31: Deprecation Lifecycle (MANDATORY)

**Rule**: Entities that other entities reference by ID MUST support a deprecation lifecycle before deletion. Entities that are never referenced by ID MUST NOT have deprecation — use immediate deletion. The pattern, storage, behavior, and cleanup rules are standardized below.

### Why Deprecation Exists

Deprecation solves one problem: **entities referenced by other entities cannot be safely deleted without a transition period.** Deleting a species while thousands of characters reference it corrupts the character data. Deleting an item template while instances persist across the game world makes those instances unresolvable.

Deprecation is NOT a soft-delete mechanism, NOT a "be safe" default, and NOT a substitute for proper cleanup. It exists exclusively to protect referential integrity during a managed phase-out.

### The Decision Tree

Before adding deprecation to any entity, apply this tree mechanically:

```
Does persistent data in OTHER services/entities store this entity's ID?
├── YES → Category A: Deprecate-Before-Delete
│         (World-building definitions with eventual deletion)
│
├── NO, but instances of this template persist with this template's ID?
│   └── Category B: Deprecate-Only (no delete endpoint)
│         (Content templates where instances outlive the template's relevance)
│
└── NO references at all
    └── No deprecation. Immediate hard delete.
        (Instances, sessions, configuration, sub-entity data)
```

### Category A: World-Building Definitions (Deprecate → [Merge] → Delete)

These are foundational definitions that other entities reference by ID. Deletion would orphan or corrupt downstream data. The transition period gives operators and automated systems time to migrate references.

**Current Category A entities**: Species, Realm, Relationship Type, Seed Type, Location.

**Lifecycle**: `Active` → `Deprecated` → (optional `Merge`) → `Delete`

**Required endpoints**:
- `POST /{entity}/deprecate` — marks deprecated, publishes `*.updated` event
- `POST /{entity}/undeprecate` — reverses deprecation (decisions can be reversed)
- `POST /{entity}/delete` — permanent removal (MUST reject if not deprecated)
- `POST /{entity}/merge` — optional, for entities with many cross-service references

**Required storage fields** (triple-field model on internal `*Model`):

```csharp
/// <summary>Whether this entity is deprecated and should not be used for new references.</summary>
public bool IsDeprecated { get; set; }

/// <summary>When deprecation occurred, null if not deprecated.</summary>
public DateTimeOffset? DeprecatedAt { get; set; }

/// <summary>Audit reason for deprecation, null if not deprecated.</summary>
public string? DeprecationReason { get; set; }
```

All three fields are MANDATORY for Category A. `DeprecationReason` provides audit context for why a world-building definition is being phased out. Omitting it (bare boolean or timestamp-only) is a violation.

**Delete flow** (mandatory sequence):
1. Verify `IsDeprecated == true` — reject with `BadRequest` if not
2. `CheckReferencesAsync` via lib-resource
3. If references exist: `ExecuteCleanupAsync` with `ALL_REQUIRED` policy
4. If cleanup fails: return `Conflict`
5. Delete from state store + indexes
6. Publish `*.deleted` event

### Category B: Content Templates (Deprecate-Only, No Delete)

These are templates/definitions where instances persist independently. The template must remain readable forever because historical instances reference it. Deprecation prevents new instances while preserving the template as a read-only archive.

**Current Category B entities**: Item Template, Quest Definition, Chat Room Type, Gardener Scenario Template, Storyline Scenario Definition.

**Lifecycle**: `Active` → `Deprecated` (terminal — no delete endpoint)

**Required endpoints**:
- `POST /{entity}/deprecate` — marks deprecated, publishes `*.updated` event
- NO undeprecate endpoint (the "no new instances" guarantee is part of the system's contract)
- NO delete endpoint (template persists forever)

**Required storage**: Either the triple-field model (matching Category A) OR a status enum when the entity has additional lifecycle states beyond active/deprecated:

```yaml
# Status enum pattern (when entity has Draft/Active/Deprecated lifecycle)
TemplateStatus:
  type: string
  description: Template lifecycle status
  enum: [Draft, Active, Deprecated]
```

When using the triple-field model, `DeprecationReason` is RECOMMENDED but not required for Category B (content management decisions are typically less consequential than world-building decisions).

**Instance creation guard**: All services with Category B entities MUST check deprecation status before creating new instances and reject with `BadRequest` if deprecated. This check is the entire purpose of Category B deprecation.

### What Does NOT Get Deprecation

The following entity types MUST use immediate hard delete (no deprecation):

| Type | Examples | Why No Deprecation |
|------|----------|-------------------|
| **Instance data** | Characters, relationships, encounters, save slots, inventory containers | Concrete instances, not definitions. No entity's *meaning* depends on them existing. lib-resource handles cascade cleanup of dependent data. |
| **Session/ephemeral data** | Game sessions, matchmaking tickets, voice rooms | Transient by nature. TTL or explicit cleanup. |
| **Configuration singletons** | Worldstate realm configs, currency definitions, calendar templates | Per-realm settings. You update or remove configuration, you don't "deprecate" it. |
| **Sub-entity data** | Character personality, character history, character encounters | Components of a parent entity, not standalone definitions. Cleaned up via lib-resource when parent is deleted. |

Adding deprecation to these entity types is **over-engineering** and a violation of this tenet. If you think an entity needs deprecation but falls into one of these categories, the entity likely belongs in Category A or B and the categorization above needs review — present the case rather than adding deprecation to instance data.

### Behavioral Rules (All Categories)

**Idempotency**: Deprecation MUST be idempotent. If the entity is already deprecated, return `OK` (not `Conflict`). The caller's intent is "this entity should be deprecated" — if it already is, the intent is satisfied. This is critical for automated systems (god-actors, background workers) that may retry operations.

```csharp
// CORRECT: Idempotent deprecation
if (entity.IsDeprecated)
    return (StatusCodes.OK, MapToResponse(entity));

// FORBIDDEN: Non-idempotent deprecation
if (entity.IsDeprecated)
    return (StatusCodes.Conflict, null);  // Punishes the caller for a satisfied precondition
```

**Undeprecation idempotency**: For Category A entities, undeprecation MUST also be idempotent. If not deprecated, return `OK`.

**Error codes**: Operations that fail due to wrong deprecation state MUST return `BadRequest` (not `Conflict`). The entity is not in the expected state — this is a precondition failure, not a competing write.

| Scenario | Status Code |
|----------|-------------|
| Deprecate: already deprecated | `OK` (idempotent) |
| Undeprecate: not deprecated | `OK` (idempotent, Category A only) |
| Delete: not deprecated (Category A) | `BadRequest` |
| Create instance: template deprecated (Category B) | `BadRequest` |

**Events**: Deprecation state changes MUST be published as `*.updated` events with `changedFields` containing the deprecation field names (e.g., `["isDeprecated", "deprecatedAt", "deprecationReason"]`). Do NOT create dedicated deprecation events (e.g., `item-template.deprecated`). Deprecation is a field change, not a separate lifecycle event. Consumers already subscribe to `*.updated` for all field changes.

```yaml
# CORRECT: Deprecation is a field change published via the standard updated event
# species.updated with changedFields: ["isDeprecated", "deprecatedAt", "deprecationReason"]

# FORBIDDEN: Dedicated deprecation event
# item-template.deprecated  ← forces consumers to subscribe to two topics
```

**List filtering**: All list/query endpoints for entities with deprecation MUST support an `includeDeprecated` parameter (default `false`). Deprecated entities are excluded from list results by default but accessible when explicitly requested.

```yaml
# Schema pattern for list endpoints
includeDeprecated:
  type: boolean
  default: false
  description: Whether to include deprecated entities in results
```

### Merge Pattern (Category A Extension)

Merge is an OPTIONAL extension for Category A entities with many cross-service references. Not all Category A entities need merge — it's valuable when thousands of instances reference the deprecated definition and manual migration is impractical.

**Requirements when implementing merge**:
1. Source entity MUST be deprecated (reject with `BadRequest` if not)
2. Target entity MUST NOT be deprecated (reject with `BadRequest` if deprecated)
3. Use distributed locks on both source and target entity indexes
4. Track partial failures in a `failedEntityIds` response field
5. Support optional `deleteAfterMerge` flag (skipped automatically on partial failure)
6. Publish `*.merged` event with source ID, target ID, migrated count, and failed IDs

**Current entities with merge**: Species, Realm, Relationship Type.

### Cross-Service Deprecation Checks

When a service creates entities that reference a definition in another service, it SHOULD check the definition's deprecation status and reject creation if deprecated. Use the target service's `Exists` endpoint or client, which returns `isActive` as `false` for deprecated entities.

```csharp
// CORRECT: Check deprecation before creating referencing entity
var (status, exists) = await _realmClient.RealmExistsAsync(
    new RealmExistsRequest { RealmId = body.RealmId }, ct);
if (status != StatusCodes.OK || exists?.IsActive != true)
    return (StatusCodes.BadRequest, null);  // Realm is deprecated or missing
```

### Known Violations (Tracked for Remediation)

These are existing violations to be fixed, NOT patterns to follow:

| Service | Violation | Correct Behavior |
|---------|-----------|-----------------|
| Species | Returns `Conflict` on already-deprecated | Return `OK` (idempotent) |
| Realm | Returns `Conflict` on already-deprecated | Return `OK` (idempotent) |
| Location | Returns `Conflict` on already-deprecated | Return `OK` (idempotent) |
| Relationship Type | Returns `Conflict` on already-deprecated | Return `OK` (idempotent) |
| Seed Type | Returns `Conflict` on already-deprecated | Return `OK` (idempotent) |
| Species | Returns `Conflict` on undeprecate-when-not-deprecated | Return `OK` (idempotent) |
| Realm | Returns `BadRequest` on undeprecate-when-not-deprecated | Return `OK` (idempotent) |
| Location | Returns `BadRequest` on undeprecate-when-not-deprecated | Return `OK` (idempotent) |
| Relationship Type | Returns `BadRequest` on undeprecate-when-not-deprecated | Return `OK` (idempotent) |
| Seed Type | Returns `Conflict` on undeprecate-when-not-deprecated | Return `OK` (idempotent) |
| Faction | Returns `BadRequest` on undeprecate-when-not-deprecated | Return `OK` (idempotent) |
| Location | Delete does not require deprecation first | Require deprecation (Category A) |
| Item Template | Uses dedicated `item-template.deprecated` event | Use `*.updated` with `changedFields` |
| Item Template | Missing `DeprecationReason` field | Add to model and schema |
| Quest Definition | Bare boolean, no timestamp or reason | Add full triple-field model |
| Storyline Scenario | Bare boolean, no timestamp or reason | Add full triple-field model (or status enum) |
| Item Template | Does not block instance creation for deprecated templates | Add deprecation check in `CreateInstanceAsync` |

---

## Quick Reference

For the consolidated violations table covering all implementation tenets, see [TENETS.md Quick Reference: Common Violations](../TENETS.md#quick-reference-common-violations). Schema-related violations are covered in [SCHEMA-RULES.md](../SCHEMA-RULES.md).

---

*This document covers tenets T3, T7, T8, T9, T17, T30, T31. See [TENETS.md](../TENETS.md) for the complete index and [IMPLEMENTATION-DATA.md](IMPLEMENTATION-DATA.md) for data modeling & code discipline tenets (T14, T20, T21, T23, T24, T25, T26).*
