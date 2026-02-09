# Implementation Tenets

> **Category**: Coding Patterns & Practices
> **When to Reference**: While actively writing service code
> **Tenets**: T3, T7, T8, T9, T14, T17, T20, T21, T23, T24, T25, T26

These tenets define the patterns you follow while implementing services.

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

**Rule**: Wrap all external calls in try-catch, use specific exception types where available.

### Standard Try-Catch Pattern

```csharp
try
{
    var result = await _stateStore.GetAsync(key, ct);
    if (result == null) return (StatusCodes.NotFound, null);
    return (StatusCodes.OK, result);
}
catch (ApiException ex)
{
    _logger.LogWarning(ex, "Service call failed with status {Status}", ex.StatusCode);
    return ((StatusCodes)ex.StatusCode, null);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Failed operation {Operation}", operationName);
    await _messageBus.TryPublishErrorAsync(
        serviceId: _configuration.ServiceId ?? "unknown",
        operation: operationName, errorType: ex.GetType().Name, message: ex.Message);
    return (StatusCodes.InternalServerError, null);
}
```

### When ApiException Handling Applies

The `ApiException` catch is ONLY required for **inter-service calls** (generated clients like `IItemClient`, `IServiceNavigator`, `IMeshClient`). NOT required for state store operations, message bus operations, lock provider operations, or local business logic.

### Error Event Publishing

`IMessageBus.TryPublishErrorAsync` is ALWAYS safe to call (internal try/catch prevents propagation). Returns `false` on failure. When an operation fails unexpectedly: log the error, call `TryPublishErrorAsync`, return failure for the ORIGINAL reason.

**Emit for**: Unexpected exceptions, infrastructure failures, programming errors caught at runtime.
**Do NOT emit for**: Validation (400), authentication (401), authorization (403), not found (404), conflicts (409).

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

```csharp
await using var lockResponse = await _lockProvider.LockAsync(
    resourceId: $"permission-update:{body.EntityId}",
    lockOwner: Guid.NewGuid().ToString(),
    expiryInSeconds: 30, cancellationToken: ct);
if (!lockResponse.Success) return (StatusCodes.Conflict, null);
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

## Tenet 14: Polymorphic Associations (STANDARDIZED)

**Rule**: When entities reference multiple entity types, use **Entity ID + Type Column** in schemas and **composite string keys** for state store operations.

```yaml
# Schema pattern
CreateRelationshipRequest:
  properties:
    entity1Id: { type: string, format: uuid }
    entity1Type: { $ref: '#/components/schemas/EntityType' }
```

```csharp
// Composite keys for state store
private static string BuildEntityRef(Guid id, EntityType type)
    => $"{type.ToString().ToLowerInvariant()}:{id}";

private static string BuildCompositeKey(...)
    => $"composite:{BuildEntityRef(id1, type1)}:{BuildEntityRef(id2, type2)}:{relationshipTypeId}";
```

Since lib-state stores cannot enforce foreign key constraints, implement validation in service logic and subscribe to `entity.deleted` events for cascade handling.

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

## Tenet 20: JSON Serialization (MANDATORY)

**Rule**: All JSON serialization/deserialization MUST use `BannouJson` (from `BeyondImmersion.Bannou.Core`). Direct `JsonSerializer` use is forbidden except in unit tests specifically testing serialization behavior (with `BannouJson.Options`).

```csharp
// CORRECT
var model = BannouJson.Deserialize<MyModel>(jsonString);
var json = BannouJson.Serialize(model);
// Extension methods: jsonString.FromJson<MyModel>(), model.ToJson()

// FORBIDDEN
var model = JsonSerializer.Deserialize<MyModel>(jsonString);
```

### JsonDocument Navigation is Allowed

`JsonDocument.Parse()` and `JsonElement` navigation are acceptable for external API responses, metadata dictionaries, and JSON introspection. Only **typed model deserialization** must use BannouJson.

### Key Serialization Behaviors

| Behavior | Setting |
|----------|---------|
| **Enums** | PascalCase strings matching C# names |
| **Property matching** | Case-insensitive |
| **Null values** | Ignored when writing |
| **Numbers** | Strict parsing (no string coercion) |

---

## Tenet 21: Configuration-First Development (MANDATORY)

**Rule**: All runtime configuration MUST be defined in `schemas/{service}-configuration.yaml` and accessed through generated configuration classes. Direct `Environment.GetEnvironmentVariable` is forbidden except for documented exceptions.

### Requirements

1. **Define in Schema**: All configuration in `schemas/{service}-configuration.yaml`
2. **Use Injected Configuration**: Access via `{Service}ServiceConfiguration` class
3. **Fail-Fast Required Config**: Required values without defaults MUST throw at startup
4. **No Hardcoded Credentials**: Never fall back to hardcoded credentials or connection strings
5. **Use AppConstants**: Shared defaults use `AppConstants` constants
6. **No Dead Configuration**: Every defined config property MUST be referenced in the plugin (service, cache, provider, worker, etc.)
7. **No Hardcoded Tunables**: Any limit, timeout, threshold, or capacity MUST be a configuration property
8. **Use Defined Infrastructure**: If `schemas/state-stores.yaml` defines a cache store for the service, implement cache read-through using that store
9. **No Secondary Fallbacks**: If a config property has a schema default, NEVER add `??` fallback in code. The default exists in the generated class. If it's null, that's a critical infrastructure failure - throw, don't mask it.

```csharp
// FORBIDDEN: Hardcoded tunables
var maxResults = Math.Min(body.Limit, 1000);           // Define MaxResultsPerQuery in config
await Task.Delay(TimeSpan.FromSeconds(30));            // Define RetryDelaySeconds in config

// CORRECT: All tunables from configuration
var maxResults = Math.Min(body.Limit, _configuration.MaxResultsPerQuery);
```

**Mathematical constants** (epsilon, golden ratio, bits-per-byte) are NOT tunables and are acceptable as hardcoded values.

**Stub scaffolding**: Config properties for unimplemented features may exist unreferenced if documented in the plugin's Stubs section.

**Defined state stores**: If `schemas/state-stores.yaml` defines a Redis cache for your service, implement read-through caching. If genuinely unnecessary, remove it from the schema.

### Allowed Exceptions (4 Categories)

Document with code comments explaining the exception:

1. **Assembly Loading Control**: `SERVICES_ENABLED`, `*_SERVICE_ENABLED/DISABLED` in `PluginLoader.cs`/`IBannouService.cs` (required before DI container available)
2. **Test Harness Control**: `DAEMON_MODE`, `PLUGIN` in test projects
3. **Orchestrator Environment Forwarding**: `Environment.GetEnvironmentVariables()` in `OrchestratorService.cs` (forwards config to deployed containers via strict whitelist)
4. **Integration Test Runners**: `BANNOU_HTTP_ENDPOINT`, `BANNOU_APP_ID` in `http-tester/`/`edge-tester/` (standalone harnesses without DI access, use `AppConstants.ENV_*`)

```csharp
// CORRECT: Injected config with fail-fast
_connectionString = config.ConnectionString
    ?? throw new InvalidOperationException("SERVICE_CONNECTION_STRING required");

// FORBIDDEN
Environment.GetEnvironmentVariable("...");  // Use config class
?? "amqp://guest:guest@localhost";          // Masks config issues
```

---

## Tenet 23: Async Method Pattern (MANDATORY)

**Rule**: All methods returning `Task` or `Task<T>` MUST use `async` and contain at least one `await`. Non-async Task-returning methods have different exception handling, incomplete stack traces, and broken `using` semantics.

```csharp
// CORRECT
public async Task<AccountResponse> GetAccountAsync(Guid accountId, CancellationToken ct)
{
    var account = await _stateStore.GetAsync($"account:{accountId}", ct);
    return MapToResponse(account);
}

// WRONG: Blocks thread, wrong exception semantics
public Task<AccountResponse> GetAccountAsync(Guid accountId)
{
    var account = _stateStore.GetAsync($"account:{accountId}").Result; // BLOCKS!
    return Task.FromResult(MapToResponse(account));
}
```

### Synchronous Implementation of Async Interface

When implementing an async interface with synchronous logic, use `await Task.CompletedTask`:

```csharp
public async Task DoWorkAsync()
{
    _logger.LogInformation("Working");
    await Task.CompletedTask;
}
```

---

## Tenet 24: Using Statement Pattern (MANDATORY)

**Rule**: All disposable objects with method-scoped lifetimes MUST use `using` statements. Manual `.Dispose()` misses exception paths and leaks resources.

```csharp
// CORRECT
using var connection = await _connectionFactory.CreateAsync(ct);
await connection.SendAsync(data, ct);

// WRONG: If SendAsync throws, connection leaks
var connection = await _connectionFactory.CreateAsync(ct);
await connection.SendAsync(data, ct);
connection.Dispose();
```

### When Manual Dispose is Acceptable

Only when disposal scope extends beyond the creating method:

1. **Class-owned resources** - Fields disposed in the class's `Dispose()` method
2. **Conditional ownership transfer** - Resource sometimes returned to caller
3. **Async disposal constraints** - When `await using` is impossible due to framework limitations

Enforced via `CA2000` (warning) and `IDE0063` (suggestion) in `.editorconfig`.

---

## Tenet 25: Type Safety Across All Models (MANDATORY)

**Rule**: ALL models (requests, responses, events, configuration, internal POCOs) MUST use the strongest available C# type. String representations of typed values are **forbidden**.

### There Are No "JSON Boundaries"

"Strings are needed because JSON is involved" is FALSE. NSwag generates typed models from schemas with enum properties. Configuration generator creates enum properties from YAML. Event schemas define enum types. BannouJson handles all serialization automatically. By the time your service method receives a request, it's already a fully-typed C# object.

### Requirements

1. **Request/Response/Event models**: Generated with proper enum types by NSwag
2. **Configuration classes**: Generated with proper enum types
3. **Internal POCOs**: MUST mirror types from generated models (enum→enum, not enum→string)
4. **GUIDs**: Always `Guid`, never `string`
5. **Dates**: Always `DateTimeOffset`, never `string`
6. **No Enum.Parse in business logic**: If you're parsing enums, your model definition is wrong

```csharp
// CORRECT: Typed throughout the entire flow
var model = new ItemTemplateModel
{
    TemplateId = Guid.NewGuid(),
    Category = body.Category,        // ItemCategory enum -> ItemCategory enum
    Rarity = body.Rarity,            // ItemRarity enum -> ItemRarity enum
};

// FORBIDDEN
public string Category { get; set; } = string.Empty;  // Use ItemCategory enum
Category = body.Category.ToString();                    // Assign enum directly
if (model.Status == "active") { ... }                   // Use enum equality
var rarity = Enum.Parse<ItemRarity>(someString);        // Model is wrong
public string OwnerId { get; set; } = string.Empty;    // Use Guid
```

### Acceptable String Conversions (3 Cases Only)

1. **State Store Set APIs**: Generic `TItem` parameters serialize via BannouJson automatically - `Guid`, `enum`, etc. work directly.

2. **External Third-Party APIs**: Parsing responses from Steam, Discord, payment processors that we don't control. Does NOT apply to Bannou-to-Bannou calls.

3. **Intentionally Generic Services (Hierarchy Isolation)**: Lower-layer services that must NOT enumerate higher-layer types use opaque string identifiers to prevent coupling:

```csharp
// ACCEPTABLE: lib-resource (L1) uses strings to avoid enumerating L2+ services
public class RegisterReferenceRequest
{
    public string ResourceType { get; set; } = string.Empty;  // Opaque - caller provides
    public string SourceType { get; set; } = string.Empty;    // Opaque - caller provides
}
// Creating an enum would make L1 depend on L4 types - hierarchy violation
```

**When to apply**: Service is intentionally generic + at a lower layer + enum would require schema updates for new consumers + value is an opaque key, not semantic.

See also: [SCHEMA-RULES.md "When NOT to Create Enums"](../SCHEMA-RULES.md#when-not-to-create-enums-service-hierarchy-consideration)

Tests follow the same rules. `DeploymentMode = "bannou"` in a test is wrong - use `DeploymentMode.Bannou`.

---

## Tenet 26: No Sentinel Values (MANDATORY)

**Rule**: Never use "magic values" to represent absence. If a value can be absent, make it nullable.

Sentinel values (`Guid.Empty` for "none", `-1` for "no index", `string.Empty` for "absent", `DateTime.MinValue` for "not set") circumvent NRT, hide bugs that compile silently, create ambiguity, and break JSON serialization semantics (`null` means absent; an empty GUID is a value).

```csharp
// CORRECT: Nullable types - compiler-enforced, unambiguous
public Guid? ContainerId { get; set; }          // null = no container
public int? SlotIndex { get; set; }              // null = no slot
public DateTimeOffset? ExpiresAt { get; set; }   // null = never expires

// FORBIDDEN: Sentinel values
public Guid ContainerId { get; set; }            // Guid.Empty = "none"? Bug? Uninitialized?
public int SlotIndex { get; set; } = -1;         // -1 = "no slot"
```

### Schema & Model Consistency

If a field can be absent, the schema MUST declare `nullable: true`, and internal storage models MUST also use nullable types:

```yaml
containerId:
  type: string
  format: uuid
  nullable: true
  description: Container holding this item, null if unplaced
```

```csharp
// Internal model MUST match schema nullability
internal class ItemInstanceModel
{
    public Guid? ContainerId { get; set; }  // Nullable - matches schema
}
```

### Migration Path

For existing sentinel values: update schema to nullable → regenerate → update POCOs → replace sentinel comparisons with `HasValue`/null checks → migrate stored data.

---

## Quick Reference: Implementation Violations

| Violation | Tenet | Fix |
|-----------|-------|-----|
| Missing event consumer registration | T3 | Add RegisterEventConsumers call |
| Using IErrorEventEmitter | T7 | Use IMessageBus.TryPublishErrorAsync instead |
| Generic catch returning 500 | T7 | Catch ApiException specifically |
| Emitting error events for user errors | T7 | Only emit for unexpected/internal failures |
| Using Microsoft.AspNetCore.Http.StatusCodes | T8 | Use BeyondImmersion.BannouService.StatusCodes |
| Plain Dictionary for cache | T9 | Use ConcurrentDictionary |
| Per-instance salt/key generation | T9 | Use shared/deterministic values |
| Wrong exchange for client events | T17 | Use IClientEventPublisher, not IMessageBus |
| Direct `JsonSerializer` usage | T20 | Use `BannouJson.Serialize/Deserialize` |
| Direct `Environment.GetEnvironmentVariable` | T21 | Use service configuration class |
| Hardcoded credential fallback | T21 | Remove default, require configuration |
| Unused configuration property | T21 | Wire up in service or remove from schema |
| Hardcoded magic number for tunable | T21 | Define in configuration schema |
| Defined cache store not used | T21 | Implement cache read-through or remove store |
| Secondary fallback for schema-defaulted property | T21 | Remove fallback; throw if null (infrastructure failure) |
| Non-async Task-returning method | T23 | Add async keyword and await |
| `Task.FromResult` without async | T23 | Use async method with await |
| `.Result` or `.Wait()` on Task | T23 | Use await instead |
| Manual `.Dispose()` in method scope | T24 | Use `using` statement instead |
| try/finally for disposal | T24 | Use `using` statement instead |
| String field for enum in ANY model | T25 | Use generated enum type (exception: opaque hierarchy identifiers) |
| String field for GUID in ANY model | T25 | Use `Guid` type |
| `Enum.Parse` anywhere in service code | T25 | Your model is wrong - fix the type |
| `.ToString()` when assigning enum | T25 | Assign enum directly |
| String comparison for enum value | T25 | Use enum equality operator |
| Claiming "JSON requires strings" | T25 | FALSE - BannouJson handles serialization |
| String in request/response/event model | T25 | Schema should define enum type (exception: opaque hierarchy identifiers) |
| String in configuration class | T25 | Config schema should define enum type |
| Enum enumerating higher-layer services | T25 | Use opaque string identifiers - see SCHEMA-RULES.md |
| Using `Guid.Empty` to mean "none" | T26 | Make field `Guid?` nullable |
| Using `-1` to mean "no index" | T26 | Make field `int?` nullable |
| Using empty string for "absent" | T26 | Make field `string?` nullable |
| Using `DateTime.MinValue` for "not set" | T26 | Make field `DateTime?` nullable |
| Non-nullable model when schema is nullable | T26 | Match nullability in internal model |

> **Schema-related violations** (shared type in events schema, API `$ref` to events, cross-service `$ref`) are covered in [SCHEMA-RULES.md](../SCHEMA-RULES.md).

---

*This document covers tenets T3, T7, T8, T9, T14, T17, T20, T21, T23, T24, T25, T26. See [TENETS.md](../TENETS.md) for the complete index.*
