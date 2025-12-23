# Bannou Service Development Tenets

> **Version**: 2.1
> **Last Updated**: 2025-12-23
> **Scope**: All Bannou microservices and related infrastructure

This document establishes the mandatory tenets for developing high-quality Bannou services. All service implementations, tests, and infrastructure MUST adhere to these tenets. Tenets must not be changed or added without EXPLICIT approval, without exception.

> **AI ASSISTANTS**: All tenets apply with heightened scrutiny to AI-generated code and suggestions. AI assistants MUST NOT bypass, weaken, or work around any tenet without explicit human approval. This includes modifying tests to pass with buggy implementations, adding fallback mechanisms, or any other "creative solutions" that violate the spirit of these tenets.

---

## Tenet 1: Schema-First Development (ABSOLUTE)

**Rule**: All API contracts, models, events, and configurations MUST be defined in OpenAPI YAML schemas before any code is written.

### Requirements

- Define all endpoints in `/schemas/{service}-api.yaml`
- Define all events in `/schemas/{service}-events.yaml` or `common-events.yaml`
- Use `x-permissions` to declare role/state requirements for WebSocket clients
- Run `make generate` to generate all code
- **NEVER** manually edit files in `*/Generated/` directories

**Important**: Scripts in `/scripts/` assume execution from the solution root directory. Always use Makefile commands rather than running scripts directly.

### Best Practices

- **POST-only pattern** for internal service APIs (enables zero-copy WebSocket routing)
- Path parameters allowed only for browser-facing endpoints (Website, OAuth redirects)
- Consolidate shared enums in `components/schemas` with `$ref` references

### Generated vs Manual Files

```
lib-{service}/
├── Generated/                      # NEVER EDIT - auto-generated
│   ├── I{Service}Service.cs        # Service interface
│   ├── {Service}Models.cs          # Request/response models
│   ├── {Service}Controller.cs      # HTTP controller
│   ├── {Service}ServiceConfiguration.cs  # Configuration class
│   ├── {Service}PermissionRegistration.cs
│   └── {Service}EventsController.cs     # Dapr event handlers
├── {Service}Service.cs             # MANUAL - business logic only
├── {Service}ServiceEvents.cs       # MANUAL - event handler implementations
└── Services/                       # MANUAL - optional helper services
```

### Why POST-Only?

Path parameters (e.g., `/accounts/{id}`) cannot map to static GUIDs for zero-copy binary WebSocket routing. All parameters move to request bodies for static endpoint signatures.

**Related**: See Tenet 15 for browser-facing endpoint exceptions (OAuth, Website, WebSocket upgrade).

---

## Tenet 2: Code Generation System (FOUNDATIONAL)

**Rule**: All service code is generated from OpenAPI schemas via a defined 8-component pipeline. Understanding what is generated vs. manual is essential.

### Generation Pipeline

Run `make generate` to execute the full pipeline in order:

| Step | Source | Generated Output |
|------|--------|------------------|
| 1. Lifecycle Events | `x-lifecycle` in `{service}-events.yaml` | `schemas/Generated/{service}-lifecycle-events.yaml` |
| 2. Common Events | `common-events.yaml` | `bannou-service/Generated/Events/CommonEventsModels.cs` |
| 3. Service Events | `{service}-events.yaml` | `bannou-service/Generated/Events/{Service}EventsModels.cs` |
| 4. Client Events | `{service}-client-events.yaml` | `lib-{service}/Generated/{Service}ClientEventsModels.cs` |
| 5. Service API | `{service}-api.yaml` | Controllers, models, clients, interfaces |
| 6. Configuration | `{service}-configuration.yaml` | `{Service}ServiceConfiguration.cs` |
| 7. Permissions | `x-permissions` in api.yaml | `{Service}PermissionRegistration.cs` |
| 8. Event Subscriptions | `x-event-subscriptions` in events.yaml | `{Service}EventsController.cs` + `{Service}ServiceEvents.cs` |

**Order Matters**: Events must be generated before service APIs because services may reference event types.

### What Is Safe to Edit

| File Pattern | Safe to Edit? | Notes |
|--------------|---------------|-------|
| `lib-{service}/{Service}Service.cs` | ✅ Yes | Main business logic |
| `lib-{service}/Services/*.cs` | ✅ Yes | Helper services |
| `lib-{service}/{Service}ServiceEvents.cs` | ✅ Yes | Generated once, then manual |
| `lib-{service}/Generated/*.cs` | ❌ Never | Regenerated on `make generate` |
| `bannou-service/Generated/*.cs` | ❌ Never | All generated directories |
| `schemas/*.yaml` | ✅ Yes | Edit schemas, regenerate code |
| `schemas/Generated/*.yaml` | ❌ Never | Generated lifecycle events |

### Schema File Types

| File Pattern | Purpose |
|--------------|---------|
| `{service}-api.yaml` | API endpoints with `x-permissions` |
| `{service}-events.yaml` | Service events with `x-lifecycle`, `x-event-subscriptions` |
| `{service}-configuration.yaml` | Service configuration with `x-service-configuration` |
| `{service}-client-events.yaml` | Server→client WebSocket push events |
| `common-events.yaml` | Shared infrastructure events |

### Configuration Environment Variable Naming (MANDATORY)

**Rule**: ALL configuration environment variables MUST follow `{SERVICE}_{PROPERTY}` pattern.

```yaml
# In {service}-configuration.yaml
x-service-configuration:
  properties:
    JwtSecret:
      type: string
      env: AUTH_JWT_SECRET      # ✅ CORRECT: SERVICE_PROPERTY format
    MaxConnections:
      type: integer
      env: CONNECT_MAX_CONNECTIONS  # ✅ CORRECT: Underscores for multi-word
```

**Correct Examples**:
- `AUTH_JWT_SECRET`, `AUTH_JWT_ISSUER`, `AUTH_MOCK_PROVIDERS`
- `CONNECT_MAX_CONNECTIONS`, `CONNECT_WEBSOCKET_URL`
- `BEHAVIOR_CACHE_TTL_SECONDS`

**Prohibited Patterns** (cause binding failures):
- `JWTSECRET` - No service prefix, no delimiter
- `JwtSecret` - camelCase not allowed
- `auth-jwt-secret` - kebab-case not allowed
- `AUTH_JWTSECRET` - Missing underscore delimiter in property name

**Rationale**: Inconsistent environment variable naming caused 90+ configuration binding failures across 18 services.

### Namespace for Generated Events

All event models are generated into a single namespace:

```csharp
using BeyondImmersion.BannouService.Events;

// All event types available from all services
var acctEvent = new AccountDeletedEvent { ... };
var authEvent = new SessionInvalidatedEvent { ... };
```

---

## Tenet 3: Event Consumer Fan-Out (MANDATORY)

**Rule**: Services that subscribe to Dapr pub/sub events MUST use the `IEventConsumer` infrastructure to enable multi-plugin event handling.

### The Problem

Dapr's `[Topic]` attribute allows only ONE endpoint per app-id to receive events. When multiple plugins need the same event (e.g., Auth, Permissions, and GameSession all need `session.connected`), only one randomly "wins."

### The Solution: Application-Level Fan-Out

`IEventConsumer` provides fan-out within the bannou process:

1. **Generated Controllers** receive Dapr events and dispatch via `IEventConsumer.DispatchAsync()`
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
- `topic`: The Dapr pub/sub topic name
- `event`: The event model class name (must exist in an events schema)
- `handler`: The handler method name (without `Async` suffix)

### Generated Files

Running `make generate` produces:

- `Generated/{Service}EventsController.cs` - Dapr topic handlers (always regenerated)
- `{Service}ServiceEvents.cs` - Handler registrations (generated once, then manual)

### Service Constructor Pattern

```csharp
public MyService(
    DaprClient daprClient,
    ILogger<MyService> logger,
    MyServiceConfiguration configuration,
    IErrorEventEmitter errorEventEmitter,
    IEventConsumer eventConsumer)  // Required for event handling
{
    _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    _errorEventEmitter = errorEventEmitter ?? throw new ArgumentNullException(nameof(errorEventEmitter));

    // Register event handlers via partial class
    ArgumentNullException.ThrowIfNull(eventConsumer, nameof(eventConsumer));
    RegisterEventConsumers(eventConsumer);
}
```

### Handler Registration (in `{Service}ServiceEvents.cs`)

```csharp
public partial class MyService
{
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IMyService, AccountDeletedEvent>(
            "account.deleted",
            async (svc, evt) => await ((MyService)svc).HandleAccountDeletedAsync(evt));

        eventConsumer.RegisterHandler<IMyService, SessionConnectedEvent>(
            "session.connected",
            async (svc, evt) => await ((MyService)svc).HandleSessionConnectedAsync(evt));
    }

    public async Task HandleAccountDeletedAsync(AccountDeletedEvent evt)
    {
        _logger.LogInformation("Processing account.deleted for {AccountId}", evt.AccountId);
        // Business logic here
    }

    public async Task HandleSessionConnectedAsync(SessionConnectedEvent evt)
    {
        _logger.LogInformation("Processing session.connected for {SessionId}", evt.SessionId);
        // Business logic here
    }
}
```

### Key Points

- **Registration is idempotent** - calling multiple times with same key is safe
- **Handlers are isolated** - one handler throwing doesn't prevent others from running
- **Factory pattern** - handlers resolve fresh service instances from request scope
- **Singleton consumer** - `IEventConsumer` is registered as singleton in DI

---

## Tenet 4: Dapr-First Infrastructure (MANDATORY)

**Rule**: Services MUST use Dapr abstractions for all infrastructure concerns. No direct database/cache/queue access except Orchestrator.

### State Management

```csharp
// REQUIRED: Use DaprClient for state operations
await _daprClient.SaveStateAsync("service-statestore", key, value);
var data = await _daprClient.GetStateAsync<T>("service-statestore", key);

// FORBIDDEN: Direct Redis/MySQL access
var connection = new MySqlConnection(connectionString); // NO!
```

### Pub/Sub Events

```csharp
// REQUIRED: Dapr pub/sub for event publishing
await _daprClient.PublishEventAsync("bannou-pubsub", "entity.action", eventModel);

// FORBIDDEN: Direct RabbitMQ access
channel.BasicPublish(...); // NO!
```

### Service Invocation

```csharp
// REQUIRED: Use generated service clients
var (statusCode, result) = await _accountsClient.GetAccountAsync(request, ct);

// FORBIDDEN: Manual HTTP construction
var response = await httpClient.PostAsync("http://accounts/api/..."); // NO!
```

### Generated Client Registration

NSwag-generated clients are automatically registered as Singletons during plugin initialization. Inject them via constructor:

```csharp
public class MyService : IMyService
{
    private readonly IAccountsClient _accountsClient;  // Auto-registered Singleton
    private readonly IAuthClient _authClient;          // Auto-registered Singleton

    public MyService(IAccountsClient accountsClient, IAuthClient authClient)
    {
        _accountsClient = accountsClient;
        _authClient = authClient;
    }
}
```

Generated clients automatically use `ServiceAppMappingResolver` for routing, supporting both monolith ("bannou") and distributed deployment topologies.

### Exceptions: Orchestrator, Connect, and Voice Services

**Orchestrator** uses direct Redis/RabbitMQ connections to avoid Dapr chicken-and-egg startup dependency.

**Connect** uses direct RabbitMQ connections **only** for dynamic per-session channel subscriptions (Dapr cannot create/destroy subscriptions at runtime). Connect still uses Dapr for state management, publishing events, and service invocation.

**Voice** uses direct infrastructure connections for real-time media control (RTPEngine UDP, Kamailio HTTP). Voice still uses Dapr for state management, publishing events, and service invocation.

These are the **only** exceptions to the Dapr-first rule.

### State Store Naming Convention

See [Generated State Store Reference](../GENERATED-STATE-STORES.md) for the complete, auto-maintained list.

| Pattern | Backend | Purpose |
|---------|---------|---------|
| `{service}-statestore` | Redis | Service-specific ephemeral state |
| `mysql-{service}-statestore` | MySQL | Persistent queryable data |

---

## Tenet 5: Event-Driven Architecture (REQUIRED)

**Rule**: All meaningful state changes MUST publish events, even without current consumers.

### Required Events Per Service

See [Generated Events Reference](../GENERATED-EVENTS.md) for the complete, auto-maintained list of all published events.

### Event Schema Pattern

```yaml
EventName:
  type: object
  required: [eventId, timestamp, entityId]
  properties:
    eventId: { type: string, format: uuid }
    timestamp: { type: string, format: date-time }
    entityId: { type: string }
    # ... entity-specific fields
```

### Topic Naming Convention

**Pattern**: `{entity}.{action}` (kebab-case entity, lowercase action)

| Topic | Description |
|-------|-------------|
| `account.created` | Account lifecycle event |
| `account.deleted` | Account lifecycle event |
| `session.invalidated` | Session state change |
| `game-session.player-joined` | Game session event |
| `character.realm.joined` | Hierarchical action |

**Infrastructure Events**: Use `bannou-` prefix for system-level events:
- `bannou-full-service-mappings` - Service routing updates
- `bannou-service-heartbeats` - Health monitoring

### Lifecycle Events (x-lifecycle)

For CRUD-based resources, use `x-lifecycle` in the events schema to auto-generate Created/Updated/Deleted events:

```yaml
# In {service}-events.yaml
x-lifecycle:
  EntityName:
    model:
      entityId: { type: string, format: uuid, primary: true, required: true }
      name: { type: string, required: true }
      createdAt: { type: string, format: date-time, required: true }
    sensitive: [passwordHash, secretKey]  # Fields excluded from events
```

**Generated Output** (`Generated/{service}-lifecycle-events.yaml`):
- `EntityNameCreatedEvent` - Full entity data on creation
- `EntityNameUpdatedEvent` - Full entity data + `changedFields` array
- `EntityNameDeletedEvent` - Entity ID + `deletedReason`

### Full-State Events Pattern

For state that must be atomically consistent across instances, use full-state events:

```yaml
FullServiceMappingsEvent:
  properties:
    mappings:
      type: object
      additionalProperties: { type: string }
      description: Complete dictionary of serviceName -> appId
    version:
      type: integer
      format: int64
      description: Monotonically increasing for ordering
```

**Consumer Pattern**:
```csharp
public bool ReplaceAllMappings(IReadOnlyDictionary<string, string> mappings, long version)
{
    lock (_versionLock)
    {
        if (version <= _currentVersion) return false;  // Reject stale
        _mappings.Clear();
        foreach (var kvp in mappings) _mappings[kvp.Key] = kvp.Value;
        _currentVersion = version;
        return true;
    }
}
```

### Canonical Event Definitions (CRITICAL)

**Rule**: Each `{service}-events.yaml` file MUST contain ONLY canonical definitions for events that service PUBLISHES. No `$ref` references to other service event files are allowed.

**Why**: NSwag follows `$ref` and generates ALL types it encounters, causing duplicate type definitions that break compilation.

```yaml
# CORRECT: Canonical definitions only
components:
  schemas:
    SessionInvalidatedEvent:
      type: object
      required: [sessionIds, reason]
      properties:
        sessionIds:
          type: array
          items: { type: string }

# WRONG: $ref to another service's events
components:
  schemas:
    AccountDeletedEvent:
      $ref: './accounts-events.yaml#/components/schemas/AccountDeletedEvent'  # NO!
```

---

## Tenet 6: Service Implementation Pattern (STANDARDIZED)

**Rule**: All service implementations MUST follow the standardized structure.

### Partial Class Requirement (MANDATORY)

**ALL service classes MUST be declared as `partial class` from initial creation.**

```csharp
// ✅ CORRECT - Always use partial
public partial class AuthService : IAuthService

// ❌ WRONG - Will require retroactive conversion
public class AuthService : IAuthService
```

**Why Partial is Required**:
1. Event handlers are implemented in separate `{Service}ServiceEvents.cs` file
2. Schema-driven event subscription generation needs partial class target
3. Separation of concerns - business logic vs. event handling
4. 15+ services required retroactive conversion when this wasn't followed

**Required File Structure**:
```
lib-{service}/
├── {Service}Service.cs          # Main implementation (partial class)
└── {Service}ServiceEvents.cs    # Event handlers (partial class)
```

### Service Class Pattern

```csharp
[DaprService("service-name", typeof(IServiceNameService), lifetime: ServiceLifetime.Scoped)]
public partial class ServiceNameService : IServiceNameService
{
    // Constants for Dapr components
    private const string STATE_STORE = "service-statestore";
    private const string PUBSUB_NAME = "bannou-pubsub";

    // Required dependencies (always available)
    private readonly DaprClient _daprClient;
    private readonly ILogger<ServiceNameService> _logger;
    private readonly ServiceNameServiceConfiguration _configuration;
    private readonly IErrorEventEmitter _errorEventEmitter;

    // Optional dependencies (nullable - may not be registered)
    private readonly IAuthClient? _authClient;

    public ServiceNameService(
        DaprClient daprClient,
        ILogger<ServiceNameService> logger,
        ServiceNameServiceConfiguration configuration,
        IErrorEventEmitter errorEventEmitter,
        IEventConsumer eventConsumer,
        IAuthClient? authClient = null)
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _errorEventEmitter = errorEventEmitter ?? throw new ArgumentNullException(nameof(errorEventEmitter));
        _authClient = authClient;  // May be null - check before use

        // Register event handlers via partial class
        ArgumentNullException.ThrowIfNull(eventConsumer, nameof(eventConsumer));
        RegisterEventConsumers(eventConsumer);
    }

    public async Task<(StatusCodes, ResponseModel?)> MethodAsync(
        RequestModel body,
        CancellationToken ct = default)
    {
        // Business logic returns tuple (StatusCodes, nullable response)
        return (StatusCodes.OK, response);
    }
}
```

### Common Dependencies

**Always Available** (registered by core infrastructure):

| Dependency | Purpose |
|------------|---------|
| `DaprClient` | State stores, pub/sub, service invocation |
| `ILogger<T>` | Structured logging |
| `{Service}ServiceConfiguration` | Generated configuration class |
| `IServiceAppMappingResolver` | Service-to-app-id resolution |
| `IErrorEventEmitter` | Emit `ServiceErrorEvent` for unexpected failures |
| `IEventConsumer` | Register event handlers for pub/sub fan-out |

**Context-Dependent** (may not be registered):

| Dependency | When Available |
|------------|----------------|
| `I{Service}Client` | When the service plugin is loaded |
| `IDistributedLockProvider` | When Redis-backed locking is configured |
| `IClientEventPublisher` | Only in Connect service context |

### Helper Service Decomposition

For complex services, decompose business logic into helper services in a `Services/` subdirectory:

```
lib-{service}/
├── Generated/                      # NEVER EDIT
├── {Service}Service.cs             # Main service implementation
├── {Service}ServiceEvents.cs       # Event handler implementations
└── Services/                       # Optional helper services (DI-registered)
    ├── I{HelperName}Service.cs     # Interface for mockability
    └── {HelperName}Service.cs      # Implementation
```

**Lifetime Rules** (Critical for DI correctness):

| Main Service | Helper Service | Valid? |
|--------------|----------------|--------|
| Singleton | Singleton | ✅ Required |
| Singleton | Scoped | ❌ Captive dependency - will fail |
| Scoped | Singleton | ✅ OK |
| Scoped | Scoped | ✅ Recommended |

**Rule**: Helper service lifetime MUST be equal to or longer than the main service lifetime.

---

## Tenet 7: Error Handling (STANDARDIZED)

**Rule**: Wrap all external calls in try-catch, use specific exception types where available.

### Pattern for Dapr State Operations

```csharp
try
{
    var data = await _daprClient.GetStateAsync<T>(STATE_STORE, key, cancellationToken: ct);
    if (data == null)
        return (StatusCodes.NotFound, null);
    return (StatusCodes.OK, data);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to get {Entity} with key {Key}", entityType, key);
    await _errorEventEmitter.TryPublishAsync(
        serviceId: _configuration.ServiceId ?? "unknown",
        operation: "GetEntity",
        errorType: ex.GetType().Name,
        message: ex.Message,
        details: new { EntityType = entityType, Key = key },
        stack: ex.StackTrace,
        cancellationToken: ct);
    return (StatusCodes.InternalServerError, null);
}
```

### Pattern for Service Client Calls

```csharp
try
{
    var (statusCode, result) = await _accountsClient.GetAccountAsync(request, ct);
    if (statusCode != StatusCodes.OK)
        return (statusCode, null); // Propagate error
    return (StatusCodes.OK, MapToResponse(result!));
}
catch (ApiException ex)
{
    // Expected API error - log as warning, no error event
    _logger.LogWarning(ex, "Service call failed with status {Status}", ex.StatusCode);
    return ((StatusCodes)ex.StatusCode, null);
}
catch (Exception ex)
{
    // Unexpected error - log as error, emit error event
    _logger.LogError(ex, "Unexpected error calling service");
    await _errorEventEmitter.TryPublishAsync(
        serviceId: _configuration.ServiceId ?? "unknown",
        operation: "ServiceCall",
        errorType: ex.GetType().Name,
        message: ex.Message,
        dependency: "accounts",
        cancellationToken: ct);
    return (StatusCodes.InternalServerError, null);
}
```

### IErrorEventEmitter API

Use `IErrorEventEmitter` for unexpected/internal failures (similar to Sentry):

```csharp
Task<bool> TryPublishAsync(
    string serviceId,           // Service identifier
    string operation,           // Operation that failed (e.g., "GetAccount")
    string errorType,           // Exception type name
    string message,             // Error message
    string? dependency = null,  // External dependency that failed
    string? endpoint = null,    // Endpoint being called
    ServiceErrorEventSeverity severity = ServiceErrorEventSeverity.Error,
    object? details = null,     // Additional context (will be serialized)
    string? stack = null,       // Stack trace
    string? correlationId = null,
    CancellationToken cancellationToken = default);
```

**Guidelines**:
- Emit only for unexpected/internal failures that should never happen
- Do **not** emit for validation/user errors or expected conflicts
- Redact sensitive information; include correlation IDs and minimal structured context
- Method returns `false` if publishing fails (prevents cascading failures)

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

**Important**: Use `BeyondImmersion.BannouService.StatusCodes` (our internal enum), NOT `Microsoft.AspNetCore.Http.StatusCodes` (a static class with constants).

```csharp
using BeyondImmersion.BannouService;  // Our StatusCodes enum

// Success case
return (StatusCodes.OK, new ResponseModel { ... });

// Not found
return (StatusCodes.NotFound, null);

// Validation error
return (StatusCodes.BadRequest, null);

// Unauthorized
return (StatusCodes.Unauthorized, null);

// Forbidden (authenticated but insufficient permissions)
return (StatusCodes.Forbidden, null);

// Conflict (duplicate creation, concurrent modification, etc.)
return (StatusCodes.Conflict, null);

// Server error
return (StatusCodes.InternalServerError, null);
```

### Empty Payload for Error Responses

Error responses return `null` as the second tuple element for security reasons:
- Prevents leaking internal error details to clients
- Connect service discards null payloads entirely
- Clients receive only the status code

### Rationale

Tuple pattern enables clean status code propagation without throwing exceptions for expected failure cases. This provides explicit status handling, avoids exception overhead, and keeps service logic decoupled from HTTP concerns.

---

## Tenet 9: Multi-Instance Safety (MANDATORY)

**Rule**: Services MUST be safe to run as multiple instances across multiple nodes.

### Requirements

1. **No in-memory state** that isn't reconstructible from Dapr state stores
2. **Use atomic Dapr operations** for state that requires consistency
3. **Use ConcurrentDictionary** for local caches, never plain Dictionary
4. **Use IDistributedLockProvider** for cross-instance coordination

### Acceptable Patterns

```csharp
// Thread-safe local cache (acceptable for non-critical data)
private readonly ConcurrentDictionary<string, CachedItem> _cache = new();

// Dapr state for authoritative state
await _daprClient.SaveStateAsync(store, key, value);
```

### Forbidden Patterns

```csharp
// Plain dictionary (not thread-safe)
private readonly Dictionary<string, Item> _items = new(); // NO!
```

### Local Locks vs Distributed Locks

**Local locks** (`lock`, `SemaphoreSlim`) protect in-memory state within a single process only. They do NOT work for cross-instance coordination.

### IDistributedLockProvider Pattern

For cross-instance coordination, use `IDistributedLockProvider`:

```csharp
public class PermissionsService : IPermissionsService
{
    private readonly IDistributedLockProvider _lockProvider;
    private const string LOCK_STORE = "permissions-statestore";

    public async Task<(StatusCodes, Response?)> UpdateAsync(Request body, CancellationToken ct)
    {
        var lockOwnerId = Guid.NewGuid().ToString();

        await using var lockResponse = await _lockProvider.LockAsync(
            storeName: LOCK_STORE,
            resourceId: $"permission-update:{body.EntityId}",
            lockOwner: lockOwnerId,
            expiryInSeconds: 30,
            cancellationToken: ct);

        if (!lockResponse.Success)
            return (StatusCodes.Conflict, null);  // Could not acquire lock

        // Critical section - safe across all instances
        // ... perform update ...
        return (StatusCodes.OK, response);
    }
}
```

**API Reference**:
```csharp
Task<ILockResponse> LockAsync(
    string storeName,       // Dapr state store name
    string resourceId,      // Resource to lock (e.g., "permission-update:abc123")
    string lockOwner,       // Unique owner ID (typically new Guid)
    int expiryInSeconds,    // Lock TTL
    CancellationToken cancellationToken = default);

public interface ILockResponse : IAsyncDisposable
{
    bool Success { get; }  // Whether lock was acquired
}
```

### Optimistic Concurrency with ETags

For state operations that need consistency without locking, use Dapr's optimistic concurrency:

```csharp
var (value, etag) = await _daprClient.GetStateAndETagAsync<Model>(STATE_STORE, key, ct);
// Modify value...
var saved = await _daprClient.TrySaveStateAsync(STATE_STORE, key, modifiedValue, etag, ct);
if (!saved) return (StatusCodes.Conflict, null);  // Concurrent modification
```

### Shared Security Components (CRITICAL)

**Rule**: Security-critical shared components (salts, keys, secrets) MUST use consistent shared values across all service instances, NEVER generate unique values per instance.

```csharp
// ✅ CORRECT - Use shared/deterministic value (consistent across instances)
_serverSalt = GuidGenerator.GetSharedServerSalt();

// ❌ WRONG - Per-instance random generation breaks distributed deployment
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

## Tenet 10: Logging Standards (REQUIRED)

**Rule**: All operations MUST include appropriate logging with structured data.

### Required Log Points

1. **Operation Entry** (Debug): Log input parameters
2. **External Calls** (Debug): Log before Dapr/service calls
3. **Expected Outcomes** (Debug): Resource not found, validation failures
4. **Business Decisions** (Information): Significant state changes
5. **Security Events** (Warning): Auth failures, permission denials
6. **Errors** (Error): Unexpected exceptions with context

### Structured Logging Format

**ALWAYS use message templates**, not string interpolation:

```csharp
// CORRECT: Message template with named placeholders
_logger.LogDebug("Getting account {AccountId}", body.AccountId);

// WRONG: String interpolation loses structure
_logger.LogDebug($"Getting account {body.AccountId}");  // NO!
```

### Forbidden Log Formatting

**No bracket tag prefixes** - the logger already includes service/class context:

```csharp
// WRONG: Manual tag prefixes
_logger.LogInformation("[AUTH-EVENT] Processing account.deleted");  // NO!
_logger.LogDebug("[PERMISSIONS] Checking capabilities");  // NO!

// CORRECT: Let structured logging handle context
_logger.LogInformation("Processing account.deleted event for {AccountId}", evt.AccountId);
_logger.LogDebug("Checking capabilities for session {SessionId}", sessionId);
```

**No emojis in log messages** - emojis are acceptable only in `/scripts/` console output:

```csharp
// WRONG: Emojis in service logs
_logger.LogInformation("🚀 Service starting up");  // NO!
_logger.LogError("❌ Failed to connect");  // NO!

// CORRECT: Plain text
_logger.LogInformation("Service starting up");
_logger.LogError("Failed to connect to {Endpoint}", endpoint);
```

### Retry Mechanism Logging

For operations with retry logic, use appropriate log levels:

```csharp
// CORRECT: Debug for individual retry attempts, Error only when exhausted
for (int attempt = 1; attempt <= maxRetries; attempt++)
{
    try
    {
        return await DoOperationAsync();
    }
    catch (Exception ex) when (attempt < maxRetries)
    {
        // DEBUG for retry attempts - these are expected during transient failures
        _logger.LogDebug(ex, "Attempt {Attempt}/{MaxRetries} failed, retrying", attempt, maxRetries);
        await Task.Delay(retryDelay);
    }
    catch (Exception ex)
    {
        // ERROR only when all retries exhausted - this is the actual failure
        _logger.LogError(ex, "All {MaxRetries} attempts exhausted for operation {Operation}", maxRetries, operationName);
        throw;
    }
}
```

### Forbidden Logging

**NEVER log**: Passwords, JWT tokens (log length only), API keys, secrets, PII.

---

## Tenet 11: Testing Requirements (THREE-TIER)

**Rule**: All services MUST have tests at appropriate tiers.

**For detailed testing architecture and plugin isolation boundaries, see [TESTING.md](TESTING.md).**

### Testing Philosophy

**Test at the lowest appropriate level** - don't repeat the same test at multiple tiers:
- Unit tests verify business logic with mocked dependencies
- HTTP tests verify service integration and generated code
- Edge tests verify the client experience through WebSocket protocol

### Test Naming Convention

Follow the Osherove naming standard: `UnitOfWork_StateUnderTest_ExpectedBehavior`

Examples:
- `GetAccount_WhenAccountExists_ReturnsAccount`
- `CreateSession_WhenTokenExpired_ReturnsUnauthorized`

### Test Data Guidelines

- **Create new resources** for each test - don't rely on pre-existing state
- **Don't assert specific counts** - only verify expected items are present
- **Tests should be independent** - previous test failures shouldn't impact current test

### Tier 1 - Unit Tests (`lib-{service}.tests/`)

Test business logic with mocked dependencies.

### Tier 2 - HTTP Integration Tests (`http-tester/Tests/`)

Test actual service-to-service calls via Dapr, verify generated code works.

### Tier 3 - Edge/WebSocket Tests (`edge-tester/Tests/`)

Test client perspective through Connect service and binary protocol.

---

## Tenet 12: Test Integrity (ABSOLUTE)

**Rule**: Tests MUST validate the intended behavior path and assert CORRECT expected behavior.

### The Golden Rule

**A failing test with correct assertions = implementation bug that needs fixing.**

**NEVER**:
- Change `Times.Never` to `Times.AtLeastOnce` to make tests pass
- Remove assertions that "inconveniently" fail
- Weaken test conditions to accommodate wrong behavior
- Add HTTP fallbacks to bypass Dapr pub/sub issues
- Claim success when tests fail

### Retries vs Fallbacks

**Retries are acceptable** - retrying the same operation for transient failures.

**Fallbacks are forbidden** - switching to a different mechanism when the intended one fails masks real issues.

```csharp
// BAD: Fallback to different mechanism
try {
    await daprClient.PublishEventAsync("bannou-pubsub", "entity.action", event);
}
catch {
    await httpClient.PostAsync("http://127.0.0.1:3500/...");  // MASKS THE REAL ISSUE
}
```

### When a Test Fails

1. **Verify the test is correct** - Does it assert the expected behavior?
2. **If test is correct**: The IMPLEMENTATION is wrong - fix the implementation
3. **If test is wrong**: Fix the test, then ensure implementation passes
4. **NEVER**: Change a correct test to pass with buggy implementation

---

## Tenet 13: X-Permissions Usage (DOCUMENTED)

**Rule**: All endpoints MUST declare x-permissions in schema, even if empty.

### Understanding X-Permissions

- Applies to **WebSocket client connections only**
- **Does NOT restrict** service-to-service calls within the cluster
- Enforced by Connect service when routing client requests
- Endpoints without x-permissions are **not exposed** to WebSocket clients

### Role Hierarchy

Hierarchy: `anonymous` → `user` → `developer` → `admin` (higher roles include all lower roles)

**Permission Logic**: Client must have **the highest role specified** AND **all states specified**.

```yaml
# User role + must be in lobby
x-permissions:
  - role: user
    states:
      game-session: in_lobby  # Requires BOTH user role AND in_lobby state
```

### Role Selection Guide

| Role | Use When | Examples |
|------|----------|----------|
| `admin` | Destructive or sensitive operations | Orchestrator endpoints, account deletion |
| `developer` | Creating/managing resources | Character creation, realm management |
| `user` | Requires authentication | Most gameplay endpoints |
| `anonymous` | Intentionally public (rare) | Server status |

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

Since Dapr state stores cannot enforce foreign key constraints, implement validation in service logic and subscribe to `entity.deleted` events for cascade handling.

---

## Tenet 15: Browser-Facing Endpoints (DOCUMENTED)

**Rule**: Some endpoints are accessed directly by browsers through NGINX rather than through the WebSocket binary protocol.

### How Browser-Facing Endpoints Work

Browser-facing endpoints are:
- Routed through NGINX reverse proxy
- NOT included in WebSocket API (no x-permissions)
- Using GET methods and path parameters (not POST-only pattern)

### Current Browser-Facing Endpoints

| Service | Endpoints | Reason |
|---------|-----------|--------|
| Website | All `/website/*` | Public website, SEO, caching |
| Auth | `/auth/oauth/{provider}/init` | OAuth redirect flow |
| Auth | `/auth/oauth/{provider}/callback` | OAuth provider callback |
| Connect | `/connect` (GET) | WebSocket upgrade handshake |

---

## Tenet 16: Naming Conventions (CONSOLIDATED)

**Rule**: All identifiers MUST follow consistent naming patterns.

### Quick Reference

| Category | Pattern | Example |
|----------|---------|---------|
| Async methods | `{Action}Async` | `GetAccountAsync` |
| Request models | `{Action}Request` | `CreateAccountRequest` |
| Response models | `{Entity}Response` | `AccountResponse` |
| Event models | `{Entity}{Action}Event` | `AccountCreatedEvent` |
| Event topics | `{entity}.{action}` | `account.created` |
| State keys | `{entity-prefix}{id}` | `account-{guid}` |
| Config properties | PascalCase + units | `TimeoutSeconds` |
| Test methods | `UnitOfWork_State_Result` | `GetAccount_WhenExists_Returns` |

### Configuration Property Requirements

- PascalCase for all property names
- Include units in time-based names: `TimeoutSeconds`, `HeartbeatIntervalSeconds`
- Document environment variable in XML comment

---

## Tenet 17: Client Event Schema Pattern (RECOMMENDED)

**Rule**: Services that push events to WebSocket clients MUST define those events in a dedicated `{service}-client-events.yaml` schema file.

### Client Events vs Service Events

| Type | File | Purpose | Consumers |
|------|------|---------|-----------|
| **Client Events** | `{service}-client-events.yaml` | Pushed TO clients via WebSocket | Game clients, SDK |
| **Service Events** | `{service}-events.yaml` | Service-to-service pub/sub | Other Bannou services |

### Required Pattern

1. Define client events in `/schemas/{service}-client-events.yaml`
2. Generate models via `make generate`
3. Auto-included in SDKs via `scripts/generate-client-sdk.sh`

---

## Tenet 18: Licensing Requirements (MANDATORY)

**Rule**: All dependencies MUST use permissive licenses (MIT, BSD, Apache 2.0). Copyleft licenses (GPL, LGPL, AGPL) are forbidden for linked code but acceptable for infrastructure containers.

### Acceptable Licenses

| License | Status |
|---------|--------|
| MIT | ✅ Preferred |
| BSD-2-Clause, BSD-3-Clause | ✅ Approved |
| Apache 2.0 | ✅ Approved |
| ISC, Unlicense, CC0 | ✅ Approved |

### Forbidden Licenses (for linked code)

| License | Status | Reason |
|---------|--------|--------|
| GPL v2/v3 | ❌ Forbidden | Copyleft |
| LGPL | ❌ Forbidden | Weak copyleft |
| AGPL | ❌ Forbidden | Network copyleft |

### Infrastructure Container Exception

GPL/LGPL software is acceptable when run as **separate infrastructure containers** that we communicate with via network protocols (not linked into our binaries).

**Current Infrastructure Containers**: RTPEngine (GPLv3), Kamailio (GPLv2+)

### Version Pinning for License Stability

When a package changes license, pin to the last permissive version with XML comment documentation.

---

## Tenet 19: XML Documentation Standards (REQUIRED)

**Rule**: All public classes, interfaces, methods, and properties MUST have XML documentation comments.

### Minimum Requirements

- `<summary>` on all public types and members
- `<param>` for all method parameters
- `<returns>` for methods with return values
- `<exception>` for explicitly thrown exceptions

### Configuration Properties

Configuration properties MUST document their environment variable:

```csharp
/// <summary>
/// JWT signing secret for token generation and validation.
/// Environment variable: AUTH_JWT_SECRET
/// </summary>
public string JwtSecret { get; set; } = "default-dev-secret";
```

### When to Use `<inheritdoc/>`

Use when implementing an interface where the base documentation is sufficient. Do NOT use when the implementation has important differences.

### Generated Code

Generated files in `*/Generated/` directories do not require manual documentation - they inherit from schemas via NSwag.

---

## Tenet 20: JSON Serialization (MANDATORY)

**Rule**: All JSON serialization and deserialization MUST use `BannouJson` helper methods. Direct use of `JsonSerializer` is forbidden except in unit tests specifically testing serialization behavior.

### Why Centralized Serialization?

Inconsistent serialization options caused significant debugging issues:
- Enum format mismatches (kebab-case vs PascalCase vs snake_case)
- Case sensitivity failures between services
- Missing converters causing deserialization exceptions

`BannouJson` provides the single source of truth for all serialization settings.

### Required Pattern

```csharp
using BeyondImmersion.BannouService.Configuration;

// CORRECT: Use BannouJson helper
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

### Key Serialization Behaviors

All serialization via `BannouJson` uses these settings:

| Behavior | Setting | Example |
|----------|---------|---------|
| **Enums** | PascalCase strings matching C# names | `GettingStarted` (NOT `getting-started` or `getting_started`) |
| **Property matching** | Case-insensitive | Handles both `AccountId` and `accountId` |
| **Null values** | Ignored when writing | `{ "name": "test" }` not `{ "name": "test", "optional": null }` |
| **Numbers** | Strict parsing | `"123"` does NOT coerce to integer 123 |

### Async and Stream Operations

```csharp
// Async stream operations
var model = await BannouJson.DeserializeAsync<MyModel>(stream, ct);
await BannouJson.SerializeAsync(stream, model, ct);

// UTF-8 byte operations
var bytes = BannouJson.SerializeToUtf8Bytes(model);
var model = BannouJson.Deserialize<MyModel>(utf8Bytes);
```

### Exception: Serialization Unit Tests

Unit tests that specifically validate serialization behavior MAY use `JsonSerializer` directly to test against the raw API:

```csharp
// Allowed in serialization-focused unit tests only
[Fact]
public void BannouJson_Options_SerializesEnumsAsPascalCase()
{
    var result = JsonSerializer.Serialize(MyEnum.SomeValue, BannouJson.Options);
    Assert.Equal("\"SomeValue\"", result);
}
```

---

## Quick Reference: Common Violations

| Violation | Tenet | Fix |
|-----------|-------|-----|
| Editing Generated/ files | 1, 2 | Edit schema, regenerate |
| Wrong env var format (`JWTSECRET`) | 2 | Use `{SERVICE}_{PROPERTY}` pattern |
| Direct database access | 4 | Use DaprClient |
| Missing event publication | 5 | Add PublishEventAsync |
| Service class missing `partial` | 6 | Add `partial` keyword |
| Missing event consumer registration | 3 | Add RegisterEventConsumers call |
| Plain Dictionary for cache | 9 | Use ConcurrentDictionary |
| Per-instance salt/key generation | 9 | Use shared/deterministic values |
| Using Microsoft.AspNetCore.Http.StatusCodes | 8 | Use BeyondImmersion.BannouService.StatusCodes |
| Generic catch returning 500 | 7 | Catch ApiException specifically |
| `[TAG]` prefix in logs | 10 | Remove brackets, use structured logging |
| Emojis in log messages | 10 | Plain text only (scripts excepted) |
| HTTP fallback in tests | 12 | Remove fallback, fix root cause |
| Changing test to pass with buggy impl | 12 | Keep test, fix implementation |
| GPL library in NuGet package | 18 | Use MIT/BSD alternative |
| Missing XML documentation | 19 | Add `<summary>`, `<param>`, `<returns>` |
| Direct `JsonSerializer` usage | 20 | Use `BannouJson.Serialize/Deserialize` |

---

## Enforcement

- **Code Review**: All PRs checked against tenets
- **CI/CD**: Automated validation where possible
- **Schema Regeneration**: Must pass after any schema changes
- **Test Coverage**: 100% of meaningful scenarios

---

*This document is the authoritative source for Bannou service development standards. Updates require explicit approval.*
