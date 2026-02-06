# Implementation Tenets

> **Category**: Coding Patterns & Practices
> **When to Reference**: While actively writing service code
> **Tenets**: T3, T7, T8, T9, T14, T17, T20, T21, T23, T24, T25

These tenets define the patterns you follow while implementing services. Reference them during active development.

> **Note**: Schema Reference Hierarchy (formerly T26) is now covered in [SCHEMA-RULES.md](../SCHEMA-RULES.md) and referenced by Tenet 1 in [TENETS.md](../TENETS.md).

---

## Tenet 3: Event Consumer Fan-Out (MANDATORY)

**Rule**: Services that subscribe to pub/sub events MUST use the `IEventConsumer` infrastructure to enable multi-plugin event handling.

### The Problem

RabbitMQ queue binding allows only ONE consumer per queue to receive events. When multiple plugins need the same event (e.g., Auth, Permission, and GameSession all need `session.connected`), only one randomly "wins."

### The Solution: Application-Level Fan-Out

`IEventConsumer` provides fan-out within the bannou process:

1. **Generated Controllers** receive events from lib-messaging and dispatch via `IEventConsumer.DispatchAsync()`
2. **Services register handlers** in their `{Service}ServiceEvents.cs` partial class
3. **All registered handlers** receive every event, isolated from each other's failures

### Defining Event Subscriptions

Define subscriptions in `{service}-events.yaml`:

```yaml
info:
  title: MyService Events
  version: 1.0.0
  x-event-subscriptions:
    - topic: account.deleted
      event: AccountDeletedEvent
      handler: HandleAccountDeleted
    - topic: session.connected
      event: SessionConnectedEvent
      handler: HandleSessionConnected
```

**Field Definitions**:
- `topic`: The RabbitMQ routing key / topic name
- `event`: The event model class name (must exist in an events schema)
- `handler`: The handler method name (without `Async` suffix)

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

**Key Points**: Registration is idempotent. Handlers are isolated (one throwing doesn't prevent others). `IEventConsumer` is singleton.

---

## Tenet 7: Error Handling (STANDARDIZED)

**Rule**: Wrap all external calls in try-catch, use specific exception types where available.

### Standard Try-Catch Pattern

```csharp
try
{
    // External call (state store, service client, etc.)
    var result = await _stateStore.GetAsync(key, ct);
    if (result == null) return (StatusCodes.NotFound, null);
    return (StatusCodes.OK, result);
}
catch (ApiException ex)
{
    // Expected API error - log as warning, propagate status
    _logger.LogWarning(ex, "Service call failed with status {Status}", ex.StatusCode);
    return ((StatusCodes)ex.StatusCode, null);
}
catch (Exception ex)
{
    // Unexpected error - log as error, emit error event
    _logger.LogError(ex, "Failed operation {Operation}", operationName);
    await _messageBus.TryPublishErrorAsync(
        serviceId: _configuration.ServiceId ?? "unknown",
        operation: operationName, errorType: ex.GetType().Name, message: ex.Message);
    return (StatusCodes.InternalServerError, null);
}
```

### When ApiException Handling Applies

**IMPORTANT**: The `ApiException` catch block is ONLY required when making **inter-service calls** using:
- Generated service clients (e.g., `IItemClient`, `IAuthClient`, `IAccountClient`)
- `IServiceNavigator` to invoke other Bannou plugins
- `IMeshClient` for direct mesh invocation

These clients throw `ApiException` when the target service returns a non-2xx status code.

**ApiException handling is NOT required for**:
- State store operations (`IStateStore<T>`) - these throw standard exceptions, not ApiException
- Message bus operations (`IMessageBus`) - fire-and-forget, uses `TryPublishAsync`
- Lock provider operations (`IDistributedLockProvider`) - returns success/failure, doesn't throw
- Local business logic - no cross-service boundary

```csharp
// REQUIRED: Inter-service call - must catch ApiException
var (status, items) = await _itemClient.ListItemsByContainerAsync(request, ct);

// NOT REQUIRED: State store operation - ApiException won't be thrown
var container = await _containerStore.GetAsync(key, ct);

// NOT REQUIRED: Message publishing - uses TryPublish pattern
await _messageBus.TryPublishAsync("topic", evt, cancellationToken: ct);
```

### Error Event Publishing

Use `IMessageBus.TryPublishErrorAsync` for unexpected/internal failures only. **This method is ALWAYS safe to call** - both implementations (RabbitMQMessageBus and InMemoryMessageBus) have internal try/catch blocks that prevent exceptions from propagating. Returns `false` if publishing fails (prevents cascading failures). Replaces legacy `IErrorEventEmitter`.

**CRITICAL**: When an operation fails due to an unexpected exception:
1. Log the error
2. Call `TryPublishErrorAsync` (safe - won't throw)
3. Return failure for the ORIGINAL reason (not because error event failed)

**Emit for**:
- Unexpected exceptions that should never happen in normal operation
- Infrastructure failures (database unavailable, message broker down)
- Programming errors caught at runtime (null references, invalid state)

**Do NOT emit for**:
- Validation errors (400) - these are expected user input issues
- Authentication failures (401) - expected when credentials are wrong
- Authorization failures (403) - expected when permissions are insufficient
- Not found (404) - expected when resources don't exist
- Conflicts (409) - expected during concurrent modifications

**Guidelines**: Redact sensitive information (passwords, tokens). Include correlation IDs for tracing. Keep structured context minimal.

### Error Granularity

Services MUST distinguish between:
- **400 Bad Request**: Invalid input/validation failures
- **401 Unauthorized**: Missing or invalid authentication
- **403 Forbidden**: Valid auth but insufficient permissions
- **404 Not Found**: Resource doesn't exist
- **409 Conflict**: State conflict (duplicate creation, etc.)
- **500 Internal Server Error**: Unexpected failures

### Warning vs Error Log Levels

- **LogWarning**: Expected failures (timeouts, transient failures, API errors from downstream)
- **LogError**: Unexpected failures that should trigger `ServiceErrorEvent` emission

---

## Tenet 8: Return Pattern (MANDATORY)

**Rule**: All service methods MUST return `(StatusCodes, TResponse?)` tuples.

### StatusCodes Enum

**Important**: Use `BeyondImmersion.BannouService.StatusCodes` (our internal enum), NOT `Microsoft.AspNetCore.Http.StatusCodes` (static class).

```csharp
return (StatusCodes.OK, response);              // Success
return (StatusCodes.BadRequest, null);          // Validation error
return (StatusCodes.Unauthorized, null);        // Missing/invalid auth
return (StatusCodes.Forbidden, null);           // Valid auth, insufficient permissions
return (StatusCodes.NotFound, null);            // Resource doesn't exist
return (StatusCodes.Conflict, null);            // State conflict
return (StatusCodes.InternalServerError, null); // Unexpected failure
```

### Empty Payload for Error Responses (ABSOLUTE)

Error responses MUST return `null` as the second tuple element. **No exceptions for "structured error responses" or "API contracts".**

**Why null is required**:
- **Status codes are sufficient**: The status code already communicates what failed (400=validation, 404=not found, 409=conflict, etc.)
- **Error messages aren't actionable**: Clients cannot programmatically act on error strings without brittle magic string matching
- **Consistency**: All errors follow the same pattern - check status code, handle accordingly
- **Security**: Prevents accidental leakage of internal details

**Incorrect patterns** (even if they seem "helpful"):
```csharp
// WRONG: Structured error response - status code already says "bad request"
return (StatusCodes.BadRequest, new CompileResponse { Success = false, Errors = errors });

// WRONG: User-friendly message - status code already says "not found"
return (StatusCodes.NotFound, new Response { Error = "Entity not found" });

// WRONG: Boolean result with error status - redundant and inconsistent
return (StatusCodes.Conflict, new Response { Released = false });
```

**Correct pattern**:
```csharp
return (StatusCodes.BadRequest, null);   // Client checks status, knows validation failed
return (StatusCodes.NotFound, null);     // Client checks status, knows resource missing
return (StatusCodes.Conflict, null);     // Client checks status, knows conflict occurred
```

**For detailed errors** (like compilation failures), log server-side for debugging. Clients should retry with corrected input based on status code semantics, not parse error strings.

### Rationale

Tuple pattern enables clean status code propagation without throwing exceptions for expected failure cases. This provides explicit status handling, avoids exception overhead, and keeps service logic decoupled from HTTP concerns.

---

## Tenet 9: Multi-Instance Safety (MANDATORY)

**Rule**: Services MUST be safe to run as multiple instances across multiple nodes.

### Requirements

1. **No in-memory state** that isn't reconstructible from lib-state stores
2. **Use atomic state operations** for state that requires consistency
3. **Use ConcurrentDictionary** for local caches, never plain Dictionary
4. **Use IDistributedLockProvider** for cross-instance coordination

### Acceptable Patterns

```csharp
// Thread-safe local cache (acceptable for non-critical data)
private readonly ConcurrentDictionary<string, CachedItem> _cache = new();

// lib-state for authoritative state
await _stateStore.SaveAsync(key, value);
```

### Event-Backed Local Caches (ACCEPTABLE)

Local instance caches are acceptable when **both** conditions are met:

1. **Loaded via API on startup**: Cache is populated from authoritative source (another service or lib-state) during service initialization - no data loss risk
2. **Kept current via events**: Cache subscribes to relevant pub/sub events to stay synchronized with changes

**Example**: `_accountSubscriptions` in GameSessionService
- Loaded from SubscriptionClient on session connect
- Updated via `subscription.changed` events
- Authoritative state lives in Subscription service
- Local cache is purely for performance (avoid API call per operation)

```csharp
// ACCEPTABLE: Event-backed local cache
private static readonly ConcurrentDictionary<Guid, HashSet<string>> _accountSubscriptions = new();

// Loaded from API on session connect:
var subscriptions = await _subscriptionsClient.GetAccountSubscriptionsAsync(accountId);
_accountSubscriptions[accountId] = subscriptions;

// Updated via event subscription:
public Task HandleSubscriptionChangedAsync(SubscriptionChangedEvent evt)
{
    _accountSubscriptions[evt.AccountId] = evt.ActiveSubscriptions;
    return Task.CompletedTask;
}
```

**NOT acceptable**: Local state that is the *only* source of truth (e.g., tracking which WebSocket sessions are connected - this must use distributed state).

### Forbidden Patterns

```csharp
// Plain dictionary (not thread-safe)
private readonly Dictionary<string, Item> _items = new(); // NO!
```

### Local Locks vs Distributed Locks

**Local locks** (`lock`, `SemaphoreSlim`) protect in-memory state within a single process only. They do NOT work for cross-instance coordination.

### IDistributedLockProvider Pattern

For cross-instance coordination:

```csharp
await using var lockResponse = await _lockProvider.LockAsync(
    resourceId: $"permission-update:{body.EntityId}",
    lockOwner: Guid.NewGuid().ToString(),
    expiryInSeconds: 30, cancellationToken: ct);
if (!lockResponse.Success) return (StatusCodes.Conflict, null);
// Critical section - safe across all instances
```

### Optimistic Concurrency with ETags

For consistency without locking:

```csharp
var (value, etag) = await _stateStore.GetWithETagAsync(key, ct);
var saved = await _stateStore.TrySaveAsync(key, modifiedValue, etag, ct);
if (!saved) return (StatusCodes.Conflict, null);  // Concurrent modification
```

### Shared Security Components (CRITICAL)

**Rule**: Security-critical shared components (salts, keys, secrets) MUST use consistent shared values across all service instances, NEVER generate unique values per instance.

```csharp
// CORRECT - Use shared/deterministic value (consistent across instances)
_serverSalt = GuidGenerator.GetSharedServerSalt();

// WRONG - Per-instance random generation breaks distributed deployment
_serverSalt = GuidGenerator.GenerateServerSalt();  // BREAKS CROSS-INSTANCE OPERATIONS
```

**Why This Matters**:
- Client shortcuts published by instance A must work when routing to instance B
- Reconnection tokens from instance A must validate on instance B
- GUID-based security isolation requires predictable server component

**Services Requiring Shared State**:
| Service | Component | Pattern |
|---------|-----------|---------|
| Connect | Server salt for client GUIDs | `GetSharedServerSalt()` |
| Auth | JWT signing keys | Configuration-based (already correct) |

**Detection Criteria** - If a service has:
- `Singleton` lifetime
- Generates cryptographic values in constructor
- Participates in distributed deployment
- → MUST use shared/deterministic values, NOT per-instance random generation

---

## Tenet 14: Polymorphic Associations (STANDARDIZED)

**Rule**: When entities can reference multiple entity types, use the **Entity ID + Type Column** pattern in schemas and **composite string keys** for state store operations.

### Required Pattern: Separate Columns in API Schema

```yaml
CreateRelationshipRequest:
  type: object
  required: [entity1Id, entity1Type, entity2Id, entity2Type, relationshipTypeId]
  properties:
    entity1Id: { type: string, format: uuid }
    entity1Type: { $ref: '#/components/schemas/EntityType' }
    entity2Id: { type: string, format: uuid }
    entity2Type: { $ref: '#/components/schemas/EntityType' }

EntityType:
  type: string
  enum: [CHARACTER, NPC, ITEM, LOCATION, REALM]
```

### Composite Keys for State Store

```csharp
// Format: "{entityType}:{entityId}"
private static string BuildEntityRef(Guid id, EntityType type)
    => $"{type.ToString().ToLowerInvariant()}:{id}";

// Uniqueness constraint key
private static string BuildCompositeKey(...)
    => $"composite:{BuildEntityRef(id1, type1)}:{BuildEntityRef(id2, type2)}:{relationshipTypeId}";
```

### Application-Level Referential Integrity

Since lib-state stores cannot enforce foreign key constraints at the application level, implement validation in service logic and subscribe to `entity.deleted` events for cascade handling.

---

## Tenet 17: Client Event Schema Pattern (RECOMMENDED)

**Rule**: Services that push events to WebSocket clients MUST define those events in a dedicated `{service}-client-events.yaml` schema file.

### Client Events vs Service Events

| Type | File | Purpose | Consumers |
|------|------|---------|-----------|
| **Client Events** | `{service}-client-events.yaml` | Pushed TO clients via WebSocket | Game clients, SDK |
| **Service Events** | `{service}-events.yaml` | Service-to-service pub/sub | Other Bannou services |

### RabbitMQ Exchange Architecture

| Exchange | Type | Purpose |
|----------|------|---------|
| `bannou` | Fanout | Service events via `IMessageBus` (all subscribers receive all) |
| `bannou-client-events` | Direct | Client events via `IClientEventPublisher` (per-session routing) |

**CRITICAL: Publishing to Sessions**

```csharp
// CORRECT: Uses direct exchange with per-session routing
await _clientEventPublisher.PublishToSessionAsync(sessionId, clientEvent);

// WRONG: Uses fanout exchange - never reaches client
await _messageBus.PublishAsync($"CONNECT_SESSION_{sessionId}", clientEvent);
```

### Required Pattern

1. Define client events in `/schemas/{service}-client-events.yaml`
2. Generate models via `make generate`
3. Auto-included in SDKs via `scripts/generate-client-sdk.sh`

---

## Tenet 20: JSON Serialization (MANDATORY)

**Rule**: All JSON serialization and deserialization MUST use `BannouJson` helper methods. Direct use of `JsonSerializer` is forbidden except in unit tests specifically testing serialization behavior.

### Why Centralized Serialization?

Inconsistent serialization options caused significant debugging issues:
- Enum format mismatches (kebab-case vs PascalCase vs snake_case)
- Case sensitivity failures between services
- Missing converters causing deserialization exceptions

`BannouJson` (located in `sdks/core`, namespace `BeyondImmersion.Bannou.Core`) provides the single source of truth for all serialization settings. This is part of the Core SDK shared across server and client code.

### Required Pattern

```csharp
using BeyondImmersion.Bannou.Core;

// CORRECT: Use BannouJson helper (from Core SDK)
var model = BannouJson.Deserialize<MyModel>(jsonString);
var json = BannouJson.Serialize(model);

// CORRECT: Extension method syntax also available
var model = jsonString.FromJson<MyModel>();
var json = model.ToJson();

// FORBIDDEN: Direct JsonSerializer usage
var model = JsonSerializer.Deserialize<MyModel>(jsonString); // NO!
var json = JsonSerializer.Serialize(model); // NO!
var model = JsonSerializer.Deserialize<MyModel>(jsonString, options); // NO - even with custom options!
```

### JsonDocument Navigation is Allowed

This tenet targets **deserialization to typed models**, not DOM navigation. `JsonDocument.Parse()` and `JsonElement` navigation are acceptable for:

1. **External API responses**: Parsing third-party API responses with unknown/variable structure
2. **Metadata dictionaries**: Reading pre-parsed `JsonElement` values from metadata fields
3. **JSON introspection**: Examining JSON structure without deserializing to a model

```csharp
// ALLOWED: JsonDocument for navigating external API response
using var doc = JsonDocument.Parse(steamApiResponse);
var success = doc.RootElement.GetProperty("response").GetProperty("success").GetBoolean();

// ALLOWED: Reading JsonElement from metadata dictionary
if (metadata.TryGetValue("threshold", out var element) && element.ValueKind == JsonValueKind.Number)
    return element.GetDouble();

// FORBIDDEN: JsonSerializer for typed model deserialization (use BannouJson)
var model = JsonSerializer.Deserialize<SteamResponse>(steamApiResponse); // NO!
```

### Key Serialization Behaviors

All serialization via `BannouJson` uses these settings:

| Behavior | Setting | Example |
|----------|---------|---------|
| **Enums** | PascalCase strings matching C# names | `GettingStarted` (NOT `getting-started` or `getting_started`) |
| **Property matching** | Case-insensitive | Handles both `AccountId` and `accountId` |
| **Null values** | Ignored when writing | `{ "name": "test" }` not `{ "name": "test", "optional": null }` |
| **Numbers** | Strict parsing | `"123"` does NOT coerce to integer 123 |

**Async/Stream**: `BannouJson.DeserializeAsync`, `SerializeAsync`, `SerializeToUtf8Bytes` also available.

**Exception**: Unit tests validating serialization behavior MAY use `JsonSerializer` directly with `BannouJson.Options`.

---

## Tenet 21: Configuration-First Development (MANDATORY)

**Rule**: All runtime configuration MUST be defined in service configuration schemas and accessed through generated configuration classes. Direct `Environment.GetEnvironmentVariable` calls are forbidden except for documented exceptions.

### Requirements

1. **Define in Schema**: All configuration goes in `schemas/{service}-configuration.yaml`
2. **Use Injected Configuration**: Access via `{Service}ServiceConfiguration` class
3. **Fail-Fast Required Config**: Required values without defaults MUST throw at startup
4. **No Hardcoded Credentials**: Never fall back to hardcoded credentials or connection strings
5. **Use AppConstants**: Shared defaults use `AppConstants` constants, not hardcoded strings
6. **No Dead Configuration**: Every defined config property MUST be referenced in service code
7. **No Hardcoded Tunables**: Any tunable value (limits, timeouts, thresholds, capacities) MUST be a configuration property. A hardcoded tunable is a sign you need to create a new config property.
8. **Use Defined Infrastructure**: If a cache/ephemeral state store is defined for the service in `schemas/state-stores.yaml`, the service MUST implement cache read-through/write-through using that store
9. **NEVER Add Secondary Fallbacks for Schema-Defaulted Properties**: If a configuration property has a default value in the schema (which compiles into the C# property initializer), service code MUST NOT add a null-coalescing fallback. This is absolutely forbidden because:
   - **Redundant**: The default already exists in the generated class
   - **Dangerous**: If the fallback differs from the schema default, it creates silent behavioral differences
   - **Bug-masking**: If configuration loading fails catastrophically enough that a defaulted property becomes null, that's a critical infrastructure failure we NEED to know about via exception
   - **Confusing**: Developers read the schema default and expect that behavior; a hidden code fallback violates that expectation

### Configuration Completeness (MANDATORY)

**Rule**: Configuration properties exist to be used. If a property is defined in the configuration schema, the service implementation MUST reference it. Unused config properties indicate either dead schema (remove from configuration YAML) or missing functionality (implement the feature that uses it).

**Hardcoded Tunables are Forbidden**:

Any numeric literal that represents a limit, timeout, threshold, or capacity is a sign that a configuration property needs to exist. If you find yourself writing a magic number, define it in the configuration schema first.

```csharp
// FORBIDDEN: Hardcoded tunables - these need configuration properties
var maxResults = Math.Min(body.Limit, 1000);  // NO - define MaxResultsPerQuery in config schema
await Task.Delay(TimeSpan.FromSeconds(30));   // NO - define RetryDelaySeconds in config schema
if (list.Count >= 100) return error;          // NO - define MaxItemsPerContainer in config schema

// CORRECT: All tunables come from configuration
var maxResults = Math.Min(body.Limit, _configuration.MaxResultsPerQuery);
await Task.Delay(TimeSpan.FromSeconds(_configuration.RetryDelaySeconds));
if (list.Count >= _configuration.MaxItemsPerContainer) return error;
```

**Mathematical Constants are Not Tunables**:

Constants used for mathematical correctness (not business logic tuning) are acceptable as hardcoded values:

```csharp
// ACCEPTABLE: Mathematical constants for floating-point comparison
private const double Epsilon = 0.000001;  // Standard floating-point tolerance
if (Math.Abs(a - b) < Epsilon) { ... }

// ACCEPTABLE: Algorithm constants with mathematical meaning
private const double GoldenRatio = 1.618033988749895;
private const int BitsPerByte = 8;
```

**Stub Scaffolding Configuration**:

Configuration properties for unimplemented features (stubs) may exist without being referenced, provided the stub is documented and the config is clearly prepared for future implementation. Document these in the plugin's Stubs section.

**Defined State Stores Must Be Used**:

If `schemas/state-stores.yaml` defines a Redis cache store for your service (e.g., `item-template-cache`), you MUST implement cache read-through:

```csharp
// CORRECT: Cache store defined in schema is used for read-through caching
var cached = await _cacheStore.GetAsync(key, ct);
if (cached is not null) return cached;

var persistent = await _persistentStore.GetAsync(key, ct);
if (persistent is null) return null;

await _cacheStore.SaveAsync(key, persistent,
    new StateOptions { Ttl = _configuration.CacheTtlSeconds }, ct);
return persistent;
```

If a defined cache store is genuinely unnecessary, remove it from `schemas/state-stores.yaml` rather than leaving dead infrastructure.

### Allowed Exceptions (4 Categories)

Document with code comments explaining the exception:

1. **Assembly Loading Control**: `SERVICES_ENABLED`, `*_SERVICE_ENABLED/DISABLED` in `PluginLoader.cs`/`IBannouService.cs`
   - Required before DI container is available to determine which plugins to load

2. **Test Harness Control**: `DAEMON_MODE`, `PLUGIN` in test projects
   - Test infrastructure, not production code
   - Tests specifically testing configuration-binding may use `SetEnvironmentVariable`

3. **Orchestrator Environment Forwarding**: `Environment.GetEnvironmentVariables()` in `OrchestratorService.cs`
   - Forwards UNKNOWN configuration to deployed containers (orchestrator's core responsibility)
   - Not reading config for orchestrator itself - forwarding to child containers
   - Uses strict whitelist (`IsAllowedEnvironmentVariable`) and excludes per-container values
   - Required because container deployments need inherited infrastructure config

4. **Integration Test Runners**: `BANNOU_HTTP_ENDPOINT`, `BANNOU_APP_ID` in `http-tester/` and `edge-tester/`
   - Standalone test harnesses without access to bannou-service's DI or configuration system
   - Use `AppConstants.ENV_*` for env var names, not hardcoded strings
   - Must provide sensible defaults (e.g., `?? "http://localhost:5012"`)

### Pattern

```csharp
// CORRECT: Use injected configuration with fail-fast
_connectionString = config.ConnectionString
    ?? throw new InvalidOperationException("SERVICE_CONNECTION_STRING required");

// FORBIDDEN: Direct env var access, hidden fallbacks, hardcoded defaults
Environment.GetEnvironmentVariable("...");  // NO - use config class
?? "amqp://guest:guest@localhost";          // NO - masks config issues
```

---

## Tenet 23: Async Method Pattern (MANDATORY)

**Rule**: All methods returning `Task` or `Task<T>` MUST use the `async` keyword and contain at least one `await`.

### Why This Matters

Non-async Task-returning methods have several problems:
1. **Exception handling differs** - Exceptions thrown before returning the Task won't be captured in the Task
2. **Stack traces are incomplete** - Missing async state machine makes debugging harder
3. **Disposal timing issues** - `using` statements don't work correctly without `await`
4. **Inconsistent behavior** - Some code paths await, others don't

### Correct Pattern

```csharp
// CORRECT: async method with await
public async Task<AccountResponse> GetAccountAsync(Guid accountId, CancellationToken ct)
{
    var account = await _stateStore.GetAsync($"account:{accountId}", ct);
    return MapToResponse(account);
}

// CORRECT: async even when just returning a value
public async Task<int> GetCountAsync()
{
    return await Task.FromResult(_items.Count);
}
```

### Incorrect Patterns

```csharp
// WRONG: Returns Task without async/await
public Task<AccountResponse> GetAccountAsync(Guid accountId)
{
    var account = _stateStore.GetAsync($"account:{accountId}").Result; // BLOCKS!
    return Task.FromResult(MapToResponse(account));
}

// WRONG: Non-async method returning Task
public Task DoWorkAsync()
{
    _logger.LogInformation("Working");
    return Task.CompletedTask; // Should be: async Task, no return needed
}
```

### Synchronous Implementation of Async Interface

When implementing an interface that requires async methods but your implementation is synchronous, use `await Task.CompletedTask;`:

```csharp
public async Task DoWorkAsync()
{
    _logger.LogInformation("Working");
    // Synchronous work, but interface requires Task
    await Task.CompletedTask;
}

public async Task<bool> IsEnabledAsync()
{
    var result = _isEnabled; // Synchronous check
    await Task.CompletedTask;
    return result;
}
```

This maintains proper async semantics without pragma suppressions.

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
| Non-async Task-returning method | T23 | Add async keyword and await |
| `Task.FromResult` without async | T23 | Use async method with await |
| `.Result` or `.Wait()` on Task | T23 | Use await instead |

---

## Tenet 24: Using Statement Pattern (MANDATORY)

**Rule**: All disposable objects with method-scoped lifetimes MUST use `using` statements instead of manual `.Dispose()` calls.

### Why This Matters

Manual `.Dispose()` calls are error-prone:
1. **Exception paths miss disposal** - If an exception is thrown before the `.Dispose()` call, the resource leaks
2. **Maintenance burden** - Developers must remember to add dispose logic to finally blocks
3. **Code complexity** - try/finally patterns for disposal are verbose and obscure intent
4. **Analyzer blindness** - CA2000 cannot verify dispose is called on all paths with manual patterns

### Correct Pattern

```csharp
// CORRECT: using statement - guarantees disposal on all paths
using var connection = await _connectionFactory.CreateAsync(ct);
await connection.SendAsync(data, ct);
// connection.Dispose() called automatically when scope exits

// CORRECT: using block for explicit scope control
using (var reader = new StreamReader(stream))
{
    var content = await reader.ReadToEndAsync(ct);
    return Parse(content);
} // reader.Dispose() called here
```

### Incorrect Patterns

```csharp
// WRONG: Manual dispose in try/finally
var connection = await _connectionFactory.CreateAsync(ct);
try
{
    await connection.SendAsync(data, ct);
}
finally
{
    connection.Dispose(); // Use 'using' instead!
}

// WRONG: Manual dispose without exception safety
var reader = new StreamReader(stream);
var content = await reader.ReadToEndAsync(ct);
reader.Dispose(); // If ReadToEndAsync throws, reader leaks!

// WRONG: Conditional dispose that may miss paths
HttpResponseMessage? response = null;
try
{
    response = await _client.GetAsync(url, ct);
    // ... processing
}
finally
{
    response?.Dispose(); // Should be: using var response = await ...
}
```

### When Manual Dispose is Acceptable

Manual `.Dispose()` is only acceptable when the disposal scope extends **beyond the creating method**:

1. **Class-owned resources** - Objects stored in instance fields that are disposed in the class's `Dispose()` method
2. **Conditional ownership transfer** - When a resource is sometimes returned to the caller (who takes ownership)
3. **Async disposal patterns** - When `await using` is not possible due to framework constraints

```csharp
// ACCEPTABLE: Class-owned resource, disposed in class's Dispose()
private readonly Timer _heartbeatTimer;

public void Dispose()
{
    _heartbeatTimer.Dispose(); // Class owns lifetime, not a method
}

// ACCEPTABLE: Conditional ownership transfer
public async Task<Stream?> TryGetStreamAsync()
{
    var stream = await _factory.CreateAsync();
    if (!await ValidateAsync(stream))
    {
        stream.Dispose(); // We own it, validation failed
        return null;
    }
    return stream; // Caller takes ownership
}
```

### EditorConfig Enforcement

This tenet is enforced via `CA2000` (Dispose objects before losing scope) at warning level and `IDE0063` (Use simple 'using' statement) at suggestion level in `.editorconfig`.

---

## Tenet 25: Type Safety Across All Models (MANDATORY)

**Rule**: ALL models in the Bannou codebase - requests, responses, events, configuration, internal POCOs - MUST use the strongest available C# type for each field. String representations of typed values are **absolutely forbidden**.

### ⚠️ CRITICAL: There Are No "JSON Boundaries"

**A common misconception**: "Strings are needed because JSON is involved" or "we get strings from HTTP requests."

**This is FALSE.** Here's why:

1. **NSwag generates typed models** from OpenAPI schemas. Request models have enum properties, not strings.
2. **Configuration generator creates enum properties** from YAML enum definitions. Config classes have enum properties, not strings.
3. **Event schemas define enum types**. Generated event models have enum properties, not strings.
4. **BannouJson handles all serialization** automatically. Developers NEVER manually convert to/from JSON strings.

**The JSON wire format is irrelevant to your code.** ASP.NET Core + NSwag + BannouJson handle the HTTP layer. By the time your service method receives a request body, it's already a fully-typed C# object with enum properties.

### What This Means

```csharp
// When you receive a request:
public async Task<(StatusCodes, Response?)> CreateItemAsync(CreateItemRequest body, CancellationToken ct)
{
    // body.Category is ALREADY ItemCategory enum - NSwag generated it that way
    // body.Rarity is ALREADY ItemRarity enum - NSwag generated it that way
    // There is NO string parsing needed. The framework did it.
}

// When you read configuration:
public MyService(MyServiceConfiguration config)
{
    // config.DeploymentMode is ALREADY DeploymentMode enum - generator created it that way
    // config.StorageProvider is ALREADY StorageProvider enum - generator created it that way
    // There is NO string parsing needed. The DI system did it.
}

// When you receive an event:
public async Task HandleItemCreatedAsync(ItemCreatedEvent evt)
{
    // evt.Category is ALREADY ItemCategory enum - NSwag generated it that way
    // There is NO string parsing needed. BannouJson did it.
}
```

### Requirements

1. **Request/Response models**: Generated by NSwag with proper enum types. Never manually define string alternatives.
2. **Event models**: Generated by NSwag with proper enum types. Never manually define string alternatives.
3. **Configuration classes**: Generated with proper enum types. Never use string for enum config.
4. **Internal POCOs**: MUST mirror the types in generated models. If the schema has an enum, the POCO has an enum.
5. **GUIDs**: Always `Guid` type, never `string`.
6. **Dates**: Always `DateTimeOffset`, never `string`.
7. **No Enum.Parse in business logic**: If you're parsing enums, something is wrong with your model definitions.

### Correct Patterns

```csharp
// CORRECT: All models use proper types throughout the entire flow
public async Task<(StatusCodes, CreateItemResponse?)> CreateItemAsync(CreateItemRequest body, CancellationToken ct)
{
    // Request already has typed enums (NSwag generated)
    var model = new ItemTemplateModel
    {
        TemplateId = Guid.NewGuid(),
        Category = body.Category,        // ItemCategory enum → ItemCategory enum
        Rarity = body.Rarity,            // ItemRarity enum → ItemRarity enum
        CreatedAt = DateTimeOffset.UtcNow
    };

    await _stateStore.SaveAsync(key, model, ct);  // BannouJson serializes enums automatically

    // Event also has typed enums (NSwag generated)
    var evt = new ItemCreatedEvent
    {
        ItemId = model.TemplateId,
        Category = model.Category,       // ItemCategory enum → ItemCategory enum
        Rarity = model.Rarity            // ItemRarity enum → ItemRarity enum
    };
    await _messageBus.PublishAsync("item.created", evt, cancellationToken: ct);

    return (StatusCodes.OK, new CreateItemResponse { ItemId = model.TemplateId });
}

// CORRECT: Configuration uses enum types
public class MyServiceConfiguration : IServiceConfiguration
{
    public DeploymentMode DeploymentMode { get; set; } = DeploymentMode.Bannou;  // Enum, not string
    public StorageProvider StorageProvider { get; set; } = StorageProvider.Minio;  // Enum, not string
}

// CORRECT: Internal POCO mirrors typed properties
internal class ItemTemplateModel
{
    public Guid TemplateId { get; set; }           // Guid, not string
    public ItemCategory Category { get; set; }     // Enum, not string
    public ItemRarity Rarity { get; set; }         // Enum, not string
    public DateTimeOffset CreatedAt { get; set; }  // DateTimeOffset, not string
}
```

### Forbidden Patterns

```csharp
// FORBIDDEN: String representation of enum in ANY model
public string Category { get; set; } = string.Empty;  // NO - use ItemCategory enum
public string DeploymentMode { get; set; } = "bannou";  // NO - use DeploymentMode enum

// FORBIDDEN: ToString() when assigning to models
Category = body.Category.ToString(),  // NO - assign enum directly

// FORBIDDEN: Enum.Parse anywhere in service code
var rarity = Enum.Parse<ItemRarity>(someString);  // NO - if you need this, your model is wrong

// FORBIDDEN: String comparison for enum values
if (model.Status == "active") { ... }  // NO - use enum equality
if (config.Mode == "internal") { ... }  // NO - config should have enum property

// FORBIDDEN: String for GUID fields
public string OwnerId { get; set; } = string.Empty;  // NO - use Guid

// FORBIDDEN: Claiming "JSON requires strings"
// This excuse is invalid. BannouJson handles serialization. You never touch JSON directly.
```

### The ONLY Acceptable String Conversions

There are exactly TWO places where string conversion is acceptable:

#### 1. State Store Set APIs (Generic TItem)

The `ICacheableStateStore<T>` set operations use generic `TItem` parameters and handle serialization automatically:

```csharp
// CORRECT: Use proper types with generic set APIs
var cacheStore = _stateStoreFactory.GetCacheableStore<MyModel>(StateStoreDefinitions.MyCache);
await cacheStore.AddToSetAsync(indexKey, gameServiceId, ct);  // Guid serialized automatically

// Reading back preserves types
var ids = await cacheStore.GetSetAsync<Guid>(indexKey, ct);  // Returns IReadOnlyList<Guid>
foreach (var id in ids) { ... }  // No parsing needed
```

The set APIs serialize items via `BannouJson`, so `Guid`, `enum`, and other types work directly.

#### 2. External Third-Party APIs (Outside Bannou's Control)

When calling external services (Steam API, Discord API, payment processors) that return string data:

```csharp
// ACCEPTABLE: Parsing external API response that WE DON'T CONTROL
var steamResponse = await _httpClient.GetStringAsync(steamUrl, ct);
using var doc = JsonDocument.Parse(steamResponse);
var statusStr = doc.RootElement.GetProperty("status").GetString();
if (Enum.TryParse<SteamStatus>(statusStr, out var status)) { ... }
```

Note: This applies to TRUE external APIs, not other Bannou services. Bannou-to-Bannou calls use generated clients with proper types.

#### 3. Intentionally Generic Services (Service Hierarchy Isolation)

When a lower-layer service is **intentionally generic** and must not enumerate higher-layer services or entity types, use opaque string identifiers instead of enums. This prevents implicit coupling that violates [SERVICE-HIERARCHY.md](../SERVICE-HIERARCHY.md).

```csharp
// ACCEPTABLE: lib-resource (L1) uses strings to avoid enumerating L2+ services
public class RegisterReferenceRequest
{
    public string ResourceType { get; set; } = string.Empty;  // "character", "realm" - caller provides
    public Guid ResourceId { get; set; }
    public string SourceType { get; set; } = string.Empty;    // "actor", "scene" - caller provides
    public Guid SourceId { get; set; }
}

// The service doesn't validate these strings against an enum because:
// 1. lib-resource (L1) must not "know about" Actor (L4) or Scene (L4)
// 2. Adding a new L4 service shouldn't require modifying L1 schemas
// 3. The string is an opaque identifier, not a value to be matched against

// WRONG: Creating an enum defeats the purpose
public enum ResourceSourceType { ACTOR, SCENE, ... }  // L1 now depends on L4 types!
```

**When to apply this exception**:
- The service is **intentionally generic** (a registry, tracker, or router)
- The service is at a **lower layer** than its consumers
- Creating an enum would require **updating the schema** when higher-layer services are added
- The value is an **opaque identifier** used as a key, not a value with semantic meaning

**See also**: [SCHEMA-RULES.md "When NOT to Create Enums"](../SCHEMA-RULES.md#when-not-to-create-enums-service-hierarchy-consideration)

### Why Tests Use Proper Types Too

Test code follows the same rules. If a test is setting `DeploymentMode = "bannou"` as a string, the test is **wrong** - it should use `DeploymentMode = DeploymentMode.Bannou`.

The type system catches mistakes at compile time. String-based tests would compile successfully with typos like `"bannon"` and fail mysteriously at runtime.

### Serialization

`BannouJson` handles all enum serialization automatically (PascalCase string representation in JSON). State stores using `IStateStore<T>` serialize via `BannouJson`, so enum-typed POCO fields are stored as their string names and deserialized back to enum values transparently. **You never interact with this process.**

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
| Secondary fallback for schema-defaulted property | T21 | Remove fallback; throw exception if null (indicates infrastructure failure) |
| Non-async Task-returning method | T23 | Add async keyword and await |
| `Task.FromResult` without async | T23 | Use async method with await |
| `.Result` or `.Wait()` on Task | T23 | Use await instead |
| Manual `.Dispose()` in method scope | T24 | Use `using` statement instead |
| try/finally for disposal | T24 | Use `using` statement instead |
| String field for enum in ANY model | T25 | Use the generated enum type (exception: opaque identifiers for hierarchy isolation) |
| String field for GUID in ANY model | T25 | Use `Guid` type |
| `Enum.Parse` anywhere in service code | T25 | Your model is wrong - fix the type |
| `.ToString()` when assigning enum | T25 | Assign enum directly |
| String comparison for enum value | T25 | Use enum equality operator |
| Claiming "JSON requires strings" | T25 | FALSE - BannouJson handles serialization |
| String in request/response/event model | T25 | Schema should define enum type (exception: opaque identifiers for hierarchy isolation) |
| String in configuration class | T25 | Config schema should define enum type |
| Enum enumerating higher-layer services | T25 | Use opaque string identifiers - see SCHEMA-RULES.md |

> **Schema-related violations** (shared type in events schema, API `$ref` to events, cross-service `$ref`) are covered in [SCHEMA-RULES.md](../SCHEMA-RULES.md).

---

## Tenet 26: No Sentinel Values (MANDATORY)

**Rule**: Never use "magic values" to represent absence or special states. If a value can be absent, make it nullable. Period.

### What Are Sentinel Values?

Sentinel values are special values within a type's normal range that are given a "magic" meaning:

- `Guid.Empty` meaning "no GUID assigned"
- `-1` meaning "no index" or "invalid"
- `string.Empty` meaning "no value"
- `DateTime.MinValue` meaning "not set"
- `0` meaning "none" when 0 could be valid

### Why Sentinel Values Are Forbidden

1. **They circumvent NRT (Nullable Reference Types)**: NRT exists so the compiler can tell you when something might be null. Sentinel values bypass this by pretending a non-nullable type can represent absence.

2. **They hide bugs**: Code that forgets to check for the sentinel compiles and runs - it just produces wrong results. Nullable types make absence explicit and compiler-enforced.

3. **They're ambiguous**: Is `Guid.Empty` "not assigned yet" or "explicitly cleared" or "a bug"? A nullable `Guid?` with `null` is unambiguous: it has no value.

4. **They pollute business logic**: Every piece of code must remember the magic value convention. With nullable types, the type system enforces it.

5. **They break serialization semantics**: `null` in JSON means "absent". An empty GUID serializes as `"00000000-0000-0000-0000-000000000000"` - a value, not absence.

### The Correct Pattern: Use Nullable Types

```csharp
// CORRECT: Nullable type when value can be absent
public Guid? ContainerId { get; set; }  // null means "no container"
public int? SlotIndex { get; set; }      // null means "no slot assigned"
public DateTimeOffset? ExpiresAt { get; set; }  // null means "never expires"

// Usage is explicit and compiler-checked
if (item.ContainerId.HasValue)
{
    await ProcessContainer(item.ContainerId.Value);
}
else
{
    // Item has no container - handle appropriately
}
```

### Forbidden Patterns

```csharp
// FORBIDDEN: Guid.Empty as sentinel
public Guid ContainerId { get; set; }  // Uses Guid.Empty to mean "none"
if (item.ContainerId == Guid.Empty) { ... }  // Magic value check

// FORBIDDEN: -1 as sentinel
public int SlotIndex { get; set; } = -1;  // -1 means "no slot"
if (item.SlotIndex == -1) { ... }  // Magic value check

// FORBIDDEN: Empty string as sentinel
public string Category { get; set; } = string.Empty;  // Empty means "uncategorized"
if (string.IsNullOrEmpty(item.Category)) { ... }  // Conflates empty with absent

// FORBIDDEN: MinValue as sentinel
public DateTime CreatedAt { get; set; }  // MinValue means "not created"
if (item.CreatedAt == DateTime.MinValue) { ... }  // Magic value check
```

### Schema Implications

If a field can be absent, the OpenAPI schema must declare it nullable:

```yaml
# CORRECT: Nullable in schema
properties:
  containerId:
    type: string
    format: uuid
    nullable: true
    description: Container holding this item, null if unplaced

# WRONG: Non-nullable with implicit sentinel convention
properties:
  containerId:
    type: string
    format: uuid
    description: Container ID (empty GUID if unplaced)  # NO!
```

### Internal Models Must Match

If the API schema has a nullable field, the internal storage model MUST also be nullable:

```csharp
// API schema has nullable containerId
// Internal model MUST match:
internal class ItemInstanceModel
{
    public Guid? ContainerId { get; set; }  // Nullable - matches schema
}

// NOT this:
internal class ItemInstanceModel
{
    public Guid ContainerId { get; set; }  // Non-nullable with Guid.Empty convention - WRONG
}
```

### Migration Path

If existing code uses sentinel values, the fix is:

1. Update the schema to make the field nullable
2. Regenerate models
3. Update internal POCOs to use nullable types
4. Update service logic to use `HasValue`/null checks instead of sentinel comparisons
5. Handle data migration for existing stored values (convert sentinels to null)

---

## Quick Reference: Implementation Violations (Updated)

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
| Secondary fallback for schema-defaulted property | T21 | Remove fallback; throw exception if null (indicates infrastructure failure) |
| Non-async Task-returning method | T23 | Add async keyword and await |
| `Task.FromResult` without async | T23 | Use async method with await |
| `.Result` or `.Wait()` on Task | T23 | Use await instead |
| Manual `.Dispose()` in method scope | T24 | Use `using` statement instead |
| try/finally for disposal | T24 | Use `using` statement instead |
| String field for enum in ANY model | T25 | Use the generated enum type (exception: opaque identifiers for hierarchy isolation) |
| String field for GUID in ANY model | T25 | Use `Guid` type |
| `Enum.Parse` anywhere in service code | T25 | Your model is wrong - fix the type |
| `.ToString()` when assigning enum | T25 | Assign enum directly |
| String comparison for enum value | T25 | Use enum equality operator |
| Claiming "JSON requires strings" | T25 | FALSE - BannouJson handles serialization |
| String in request/response/event model | T25 | Schema should define enum type (exception: opaque identifiers for hierarchy isolation) |
| String in configuration class | T25 | Config schema should define enum type |
| Enum enumerating higher-layer services | T25 | Use opaque string identifiers - see SCHEMA-RULES.md |
| Using `Guid.Empty` to mean "none" | T26 | Make field `Guid?` nullable |
| Using `-1` to mean "no index" | T26 | Make field `int?` nullable |
| Using empty string for "absent" | T26 | Make field `string?` nullable |
| Using `DateTime.MinValue` for "not set" | T26 | Make field `DateTime?` nullable |
| Non-nullable internal model field when schema is nullable | T26 | Match nullability in internal model |

---

*This document covers tenets T3, T7, T8, T9, T14, T17, T20, T21, T23, T24, T25, T26. See [TENETS.md](../TENETS.md) for the complete index and Tenet 1 (Schema-First Development).*
