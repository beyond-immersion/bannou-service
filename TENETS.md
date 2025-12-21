# Bannou Service Development Tenets

> **Version**: 1.8
> **Last Updated**: 2025-12-20
> **Scope**: All Bannou microservices and related infrastructure

This document establishes the mandatory tenets for developing high-quality Bannou services. All service implementations, tests, and infrastructure MUST adhere to these tenets. Tenets must not be changed or added without EXPLICIT approval, without exception.

> ⚠️ **AI ASSISTANTS**: All tenets apply with heightened scrutiny to AI-generated code and suggestions. AI assistants MUST NOT bypass, weaken, or work around any tenet without explicit human approval. This includes modifying tests to pass with buggy implementations, adding fallback mechanisms, or any other "creative solutions" that violate the spirit of these tenets.

---

## Tenet 1: Schema-First Development (ABSOLUTE)

**Rule**: All API contracts, models, events, and configurations MUST be defined in OpenAPI YAML schemas before any code is written.

### Requirements

- Define all endpoints in `/schemas/{service}-api.yaml`
- Define all events in `/schemas/{service}-events.yaml` or `common-events.yaml`
- Use `x-permissions` to declare role/state requirements for WebSocket clients
- Run `make generate` or `make generate-services` to generate all code
- **NEVER** manually edit files in `*/Generated/` directories

**Important**: Scripts in `/scripts/` assume execution from the solution root directory. Always use Makefile commands rather than running scripts directly, as direct execution may fail due to path resolution issues.

### Best Practices

- **POST-only pattern** for internal service APIs (enables zero-copy WebSocket routing)
- Path parameters allowed only for browser-facing endpoints (Website, OAuth redirects)
- Consolidate shared enums in `components/schemas` with `$ref` references
- Follow naming conventions per Tenet 15 (Request/Response models, Event models)

### Generated vs Manual Files

```
lib-{service}/
├── Generated/                      # NEVER EDIT - auto-generated
│   ├── I{Service}Service.cs        # Service interface
│   ├── {Service}Models.cs          # Request/response models
│   ├── {Service}Controller.cs      # HTTP controller
│   ├── {Service}ServiceConfiguration.cs  # Configuration class
│   └── {Service}PermissionRegistration.Generated.cs
├── {Service}Service.cs             # MANUAL - business logic only
└── Services/                       # MANUAL - optional helper services
```

### Why POST-Only?

Path parameters (e.g., `/accounts/{id}`) cannot map to static GUIDs for zero-copy binary WebSocket routing. All parameters move to request bodies for static endpoint signatures.

**Related**: See Tenet 14 for browser-facing endpoint exceptions (OAuth, Website, WebSocket upgrade).

---

## Tenet 2: Dapr-First Infrastructure (MANDATORY)

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

**Connect** uses direct RabbitMQ connections **only** for dynamic per-session channel subscriptions (Dapr cannot create/destroy subscriptions at runtime). Connect still uses Dapr for:
- State management (`connect-statestore`)
- Publishing events (`session.connected`, `session.disconnected`)
- Service invocation via generated clients

**Voice** uses direct infrastructure connections for real-time media control:
- **RTPEngine** (UDP): The ng protocol requires stateless UDP communication with cookie-based request correlation for media stream control. Dapr has no UDP transport abstraction.
- **Kamailio** (HTTP): JSONRPC 2.0 over HTTP for SIP user registration and call routing. While technically HTTP, these are infrastructure control commands to the SIP proxy, not service-to-service calls.

Voice still uses Dapr for:
- State management (`voice-statestore`)
- Publishing events (`voice.room-closed`, `voice.tier-upgraded`, etc.)
- Service invocation via generated clients (AuthClient, GameSessionClient)

These are the **only** exceptions to the Dapr-first rule.

### State Store Naming Convention

See [Generated State Store Reference](docs/GENERATED-STATE-STORES.md) for the complete, auto-maintained list.

**Naming Patterns**:

| Pattern | Backend | Purpose |
|---------|---------|---------|
| `{service}-statestore` | Redis | Service-specific ephemeral state |
| `mysql-{service}-statestore` | MySQL | Persistent queryable data |

**Deployment Flexibility**: Dapr component abstraction means multiple logical state stores can share physical Redis/MySQL instances in simple deployments, while production deployments can map to dedicated infrastructure without code changes.

---

## Tenet 3: Event-Driven Architecture (REQUIRED)

**Rule**: All meaningful state changes MUST publish events, even without current consumers.

### Required Events Per Service

See [Generated Events Reference](docs/GENERATED-EVENTS.md) for the complete, auto-maintained list of all published events.

**Event Discovery**: The events reference is auto-generated from `schemas/*-events.yaml` files during `make generate`.

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

See **Tenet 15: Naming Conventions § Event Topic Naming** for the complete topic naming standard.

Format: `{entity}.{action}` (e.g., `account.deleted`, `session.invalidated`)

### Event Handler Pattern

Dapr pub/sub subscriptions are handled in dedicated event controllers:

**Location**: `lib-{service}/{Service}EventsController.cs`

```csharp
[ApiController]
[Route("[controller]")]
public class {Service}EventsController : ControllerBase
{
    [Topic("bannou-pubsub", "entity.action")]
    [HttpPost("handle-entity-action")]
    public async Task<IActionResult> HandleEntityAction()
    {
        var evt = await DaprEventHelper.ReadEventAsync<EntityActionEvent>(Request);
        if (evt == null) return BadRequest("Invalid event data");

        // Process event
        return Ok();
    }
}
```

**Rationale**: Separate from generated controller to allow Dapr subscription discovery while maintaining schema-first architecture for API endpoints.

### Lifecycle Events (x-lifecycle)

For CRUD-based resources with defined creation and destruction, use `x-lifecycle` in the API schema to auto-generate Created/Updated/Deleted events:

```yaml
# In {service}-api.yaml
x-lifecycle:
  EntityName:
    model:
      entityId: { type: string, format: uuid, primary: true, required: true }
      name: { type: string, required: true }
      createdAt: { type: string, format: date-time, required: true }
      # ... all entity fields that should appear in events
    sensitive: [passwordHash, secretKey]  # Fields excluded from events
```

**Generated Output** (`{service}-lifecycle-events.yaml`):
- `EntityNameCreatedEvent` - Full entity data on creation
- `EntityNameUpdatedEvent` - Full entity data + `changedFields` array
- `EntityNameDeletedEvent` - Entity ID + `deletedReason`

**Topic Pattern**: `{entity-kebab-case}.created`, `.updated`, `.deleted`

**Generation**: Run `make generate` - lifecycle event generation is automatic.

**When to Use x-lifecycle**:
- Entities with standard CRUD lifecycle (Account, Character, Relationship, Species, etc.)
- Resources that can be created, modified, and destroyed
- Events need full entity data for downstream processing

**When to Use Custom Events** (`{service}-events.yaml`):
- Non-resource events (state transitions, actions, notifications)
- Events that don't map to entity lifecycle (e.g., `session.connected`, `service.registered`)
- Events with custom payloads not derivable from entity models

### Full-State Events Pattern

For state that must be atomically consistent across instances, use full-state events instead of incremental updates:

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

**When to Use**:
- Routing tables, configuration, capability manifests
- State where partial updates could cause inconsistency
- High-frequency updates where ordering matters

---

## Tenet 4: Multi-Instance Safety (MANDATORY)

**Rule**: Services MUST be safe to run as multiple instances across multiple nodes.

### Requirements

1. **No in-memory state** that isn't reconstructible from Dapr state stores
2. **Use atomic Dapr operations** for state that requires consistency
3. **Use ConcurrentDictionary** for local caches, never plain Dictionary
4. **Use IDistributedLockProvider** for cross-instance coordination (see below)

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

**Local locks** (`lock`, `SemaphoreSlim`, `ReaderWriterLockSlim`) protect in-memory state within a single process:

```csharp
// CORRECT: Local lock for in-process coordination only
private readonly object _localLock = new object();
lock (_localLock)
{
    _cache[key] = value;  // Protecting in-memory dictionary
}
```

**Local locks do NOT work** for cross-instance coordination because each instance has its own lock object.

### IDistributedLockProvider Pattern

For cross-instance coordination, use `IDistributedLockProvider` which leverages Dapr's ETag-based optimistic concurrency:

```csharp
public class PermissionsService : IPermissionsService
{
    private readonly IDistributedLockProvider _lockProvider;

    public async Task<(StatusCodes, Response?)> UpdateAsync(Request body, CancellationToken ct)
    {
        // Acquire distributed lock with timeout
        await using var lockHandle = await _lockProvider.AcquireLockAsync(
            $"permission-update:{body.EntityId}",
            timeout: TimeSpan.FromSeconds(30),
            ct);

        if (lockHandle == null)
            return (StatusCodes.Conflict, null);  // Could not acquire lock

        // Critical section - safe across all instances
        // ... perform update ...
        return (StatusCodes.OK, response);
    }
}
```

**Implementation**: `RedisDistributedLockProvider` uses Dapr's `GetStateAndETagAsync`/`TrySaveStateAsync` for atomic lock acquisition without direct Redis access.

### Optimistic Concurrency with ETags

For state operations that need consistency without locking, use Dapr's optimistic concurrency:

```csharp
// Pattern 1: GetStateEntryAsync → modify → SaveAsync (implicit ETag)
var entry = await _daprClient.GetStateEntryAsync<AccountModel>(STATE_STORE, accountId, ct);
if (entry.Value == null)
    return (StatusCodes.NotFound, null);

entry.Value.DisplayName = newName;
var success = await entry.SaveAsync(cancellationToken: ct);

if (!success)
{
    // Concurrent modification - retry or return conflict
    return (StatusCodes.Conflict, null);
}

// Pattern 2: Explicit ETag handling
var (value, etag) = await _daprClient.GetStateAndETagAsync<Model>(STATE_STORE, key, ct);
var saved = await _daprClient.TrySaveStateAsync(STATE_STORE, key, modifiedValue, etag, ct);
if (!saved) return (StatusCodes.Conflict, null);
```

### Service Lifetime Summary

Service lifetime affects what helpers can be injected. See **Tenet 5: Helper Service Decomposition** for the complete lifetime rules table.

- **Scoped** (default): Stateless services (Auth, Accounts, GameSession)
- **Singleton**: Services with connection state or caches (Connect, Permissions)

---

## Tenet 5: Service Implementation Pattern (STANDARDIZED)

**Rule**: All service implementations MUST follow the standardized structure.

### Service Class Pattern

```csharp
[DaprService("service-name", typeof(IServiceNameService), lifetime: ServiceLifetime.Scoped)]
public class ServiceNameService : IServiceNameService
{
    // Constants for Dapr components
    private const string STATE_STORE = "service-statestore";
    private const string PUBSUB_NAME = "bannou-pubsub";

    // Required dependencies (always available)
    private readonly DaprClient _daprClient;
    private readonly ILogger<ServiceNameService> _logger;
    private readonly ServiceNameServiceConfiguration _configuration;

    // Optional dependencies (nullable - may not be registered)
    private readonly IAuthClient? _authClient;

    public ServiceNameService(
        DaprClient daprClient,
        ILogger<ServiceNameService> logger,
        ServiceNameServiceConfiguration configuration,
        IAuthClient? authClient = null)
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _authClient = authClient;  // May be null - check before use
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

**Context-Dependent** (may not be registered):

| Dependency | When Available |
|------------|----------------|
| `I{Service}Client` | When the service plugin is loaded |
| `IDistributedLockProvider` | When Redis-backed locking is configured |
| `IClientEventPublisher` | Only in Connect service context |

### Required vs Optional Dependencies

**Required dependencies** use non-nullable types with null guards:
```csharp
private readonly DaprClient _daprClient;  // Always required

public MyService(DaprClient daprClient)
{
    _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
}
```

**Optional dependencies** use nullable types with default parameters:
```csharp
private readonly IAuthClient? _authClient;  // May not be registered

public MyService(IAuthClient? authClient = null)
{
    _authClient = authClient;  // OK to be null
}

public async Task DoSomethingAsync()
{
    if (_authClient == null)
    {
        _logger.LogWarning("AuthClient not available, skipping auth check");
        return;
    }
    // Use _authClient safely
}
```

### Helper Service Decomposition

For complex services, decompose business logic into helper services in a `Services/` subdirectory:

```
lib-{service}/
├── Generated/                      # NEVER EDIT
├── {Service}Service.cs             # Main service implementation
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

**Registration** (in `{Service}ServicePlugin.cs`):

```csharp
// For Scoped main service (most services)
services.AddScoped<ITokenService, TokenService>();
services.AddScoped<ISessionService, SessionService>();

// For Singleton main service (Connect, Permissions, etc.)
services.AddSingleton<ISessionManager, DaprSessionManager>();
services.AddSingleton<IDistributedLockProvider, RedisDistributedLockProvider>();
```

**Example** (Auth Service - Scoped):
```csharp
public class AuthService : IAuthService
{
    private readonly ITokenService _tokenService;
    private readonly ISessionService _sessionService;
    private readonly IOAuthProviderService _oauthService;

    // Delegate authentication tasks appropriately
}
```

**Benefits**:
- **Unit testable**: Individual helpers can be tested in isolation
- **Mockable**: Interfaces enable clean mocking in main service tests
- **Single responsibility**: Each helper handles one domain concern

---

## Tenet 6: Return Pattern (MANDATORY)

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
- For unexpected errors, use `IErrorEventEmitter` to emit `ServiceErrorEvent` for internal logging (see **Tenet 7**)

### Generated Controller Mapping

NSwag-generated controllers handle all HTTP/status code boilerplate:

```csharp
// Generated controller (NEVER EDIT)
public async Task<IActionResult> GetAccountAsync([FromBody] GetAccountRequest body)
{
    var (statusCode, result) = await _service.GetAccountAsync(body, HttpContext.RequestAborted);
    return statusCode switch
    {
        StatusCodes.OK => Ok(result),
        StatusCodes.NotFound => NotFound(),
        StatusCodes.BadRequest => BadRequest(),
        StatusCodes.Unauthorized => Unauthorized(),
        StatusCodes.Forbidden => Forbid(),
        StatusCodes.Conflict => Conflict(),
        StatusCodes.InternalServerError => StatusCode(500),
        _ => StatusCode((int)statusCode)
    };
}
```

Service implementations only return tuples; controllers handle HTTP response construction.

### Rationale

Tuple pattern enables clean status code propagation without throwing exceptions for expected failure cases. This:
- Provides explicit status handling
- Avoids exception overhead for normal flows
- Makes error paths visible in code
- Enables clean controller response mapping
- Keeps service logic decoupled from HTTP concerns

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
    await _errorEventEmitter.EmitAsync("GetEntity", ex, new { EntityType = entityType, Key = key }, ct);
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
    await _errorEventEmitter.EmitAsync("ServiceCall", ex, new { Service = "accounts" }, ct);
    return (StatusCodes.InternalServerError, null);
}
```

### Status Code Conversion

The `StatusCodes` enum values map to HTTP status codes but are protocol-agnostic. The Connect service converts these to binary protocol status codes for WebSocket transmission, and the client SDK converts them back to familiar HTTP terms for client developers. This abstraction allows future protocols to use different status code representations while maintaining consistent service logic.

### Error Granularity

Services MUST distinguish between:
- **400 Bad Request**: Invalid input/validation failures
- **401 Unauthorized**: Missing or invalid authentication
- **403 Forbidden**: Valid auth but insufficient permissions
- **404 Not Found**: Resource doesn't exist
- **409 Conflict**: State conflict (duplicate creation, etc.)
- **500 Internal Server Error**: Unexpected failures

### Warning vs Error Log Levels

- **LogWarning**: Failures that are important to note but don't require immediate investigation (expected timeouts, transient failures that will retry, API errors from downstream services)
- **LogError**: Unexpected failures that should never happen in normal operation - these provoke actual support response and should trigger `ServiceErrorEvent` emission

### Error Event Emission (ServiceErrorEvent)

Use `IErrorEventEmitter` for unexpected/internal failures (similar to Sentry):

```csharp
public class MyService : IMyService
{
    private readonly IErrorEventEmitter _errorEventEmitter;

    public async Task<(StatusCodes, Response?)> DoSomethingAsync(Request body, CancellationToken ct)
    {
        try
        {
            // ... business logic ...
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in DoSomething");
            await _errorEventEmitter.EmitAsync(
                operation: "DoSomething",
                exception: ex,
                context: new { RequestId = body.RequestId },  // Redact sensitive data
                cancellationToken: ct);
            return (StatusCodes.InternalServerError, null);
        }
    }
}
```

**Guidelines**:
- Emit only for unexpected/internal failures that should never happen
- Do **not** emit for validation/user errors or expected conflicts
- Do not treat error events as success/failure signals for workflows
- Redact sensitive information; include correlation/request IDs and minimal structured context

---

## Tenet 8: Logging Standards (REQUIRED)

**Rule**: All operations MUST include appropriate logging with structured data.

**Why Log Levels Matter**: Debug logs are typically disabled in production to reduce noise and cost. Information logs provide operational visibility. Warning/Error logs trigger alerts and investigations. Using the wrong level means either missing critical issues or drowning in noise.

### Required Log Points

1. **Operation Entry** (Debug): Log input parameters
2. **External Calls** (Debug): Log before Dapr/service calls
3. **Expected Outcomes** (Debug): Resource not found, validation failures - expected cases
4. **Business Decisions** (Information): Log significant state changes (account created, session started)
5. **Security Events** (Warning): Log auth failures, permission denials
6. **Errors** (Error): Log unexpected exceptions with context

### Structured Logging Format

**ALWAYS use message templates**, not string interpolation:

```csharp
// CORRECT: Message template with named placeholders
_logger.LogDebug("Getting account {AccountId}", body.AccountId);

// WRONG: String interpolation loses structure
_logger.LogDebug($"Getting account {body.AccountId}");  // NO!
```

Message templates enable log aggregation tools to group, filter, and search by parameter values.

### Traceability

Include identifiable request information in logs for traceability:
- Entity IDs (`AccountId`, `SessionId`, `CharacterId`)
- Request identifiers when available
- User/session context for security-relevant operations

### Example Pattern

```csharp
public async Task<(StatusCodes, AccountResponse?)> GetAccountAsync(
    GetAccountRequest body,
    CancellationToken ct)
{
    _logger.LogDebug("Getting account {AccountId}", body.AccountId);

    try
    {
        var account = await _daprClient.GetStateAsync<AccountModel>(
            STATE_STORE, body.AccountId, ct);

        if (account == null)
        {
            _logger.LogDebug("Account {AccountId} not found", body.AccountId);  // Expected case
            return (StatusCodes.NotFound, null);
        }

        _logger.LogDebug("Retrieved account {AccountId} for user {DisplayName}",
            body.AccountId, account.DisplayName);
        return (StatusCodes.OK, MapToResponse(account));
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to get account {AccountId}", body.AccountId);
        return (StatusCodes.InternalServerError, null);
    }
}
```

### Forbidden Logging

**NEVER log**:
- Passwords or password hashes
- JWT tokens (log length/presence only)
- API keys or secrets
- Full credit card numbers
- Personal health information

---

## Tenet 9: Testing Requirements (THREE-TIER)

**Rule**: All services MUST have tests at appropriate tiers.

**For detailed testing architecture, plugin isolation boundaries, and test placement decisions, see [TESTING.md](TESTING.md).**

### Test Naming Convention

See **Tenet 15: Naming Conventions § Unit Test Naming** for the complete test naming standard (Osherove pattern).

Pattern: `UnitOfWork_StateUnderTest_ExpectedBehavior` (e.g., `GetAccount_WhenAccountExists_ReturnsAccount`)

### Testing Philosophy

**Test at the lowest appropriate level** - don't repeat the same test at multiple tiers:
- Unit tests verify business logic with mocked dependencies
- HTTP tests verify service integration and generated code works correctly
- Edge tests verify the client experience through the WebSocket protocol

**Each test should prevent a real regression** that would negatively impact clients or other services. Avoid pointless tests, but do test generated code at least once (typically at HTTP tier) to verify generation works correctly.

### Test Data Guidelines

- **Create new resources** for each test - don't rely on pre-existing state
- **Don't assert specific counts** in list responses - only verify expected items are present and unexpected items are absent
- **Tests should be independent** - a previous test failing should not impact the current test
- **Cleanup is not critical** - tests create their own data, so leftover state is acceptable

### Tier 1 - Unit Tests (`lib-{service}.tests/`)

**Purpose**: Test business logic with mocked dependencies

**Requirements**:
- Test configuration binding from environment variables
- Test permission registration endpoint counts
- Test error handling paths
- Test business logic with various inputs
- **NOT acceptable**: Constructor null tests only

```csharp
[Fact]
public async Task GetAccount_WhenAccountExists_ReturnsAccount()
{
    // Arrange
    var accountId = Guid.NewGuid().ToString();
    var expectedAccount = new AccountModel { Id = accountId, DisplayName = "Test" };
    _mockDaprClient
        .Setup(d => d.GetStateAsync<AccountModel>(It.IsAny<string>(), accountId, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(expectedAccount);

    // Act
    var (status, result) = await _service.GetAccountAsync(
        new GetAccountRequest { AccountId = accountId },
        CancellationToken.None);

    // Assert
    Assert.Equal(StatusCodes.OK, status);
    Assert.NotNull(result);
    Assert.Equal("Test", result.DisplayName);
}
```

### Tier 2 - HTTP Integration Tests (`http-tester/Tests/`)

**Purpose**: Test actual service-to-service calls via Dapr, verify generated code works

**Requirements**:
- Test CRUD operations with real Dapr state stores
- Test event publication and cross-service effects
- Test error responses from actual services
- Validate proper status codes returned
- Verify generated clients and controllers function correctly

```csharp
public class AccountTestHandler : IServiceTestHandler
{
    public async Task<TestResult> TestCreateAccount(IAccountsClient client)
    {
        // Create unique resource for this test
        var uniqueEmail = $"test-{Guid.NewGuid()}@example.com";
        var request = new CreateAccountRequest { Email = uniqueEmail };
        var (status, result) = await client.CreateAccountAsync(request);

        // Verify expected item exists, don't assume specific counts
        return status == StatusCodes.OK && result?.AccountId != null
            ? TestResult.Successful()
            : TestResult.Failed("Account creation failed");
    }
}
```

### Tier 3 - Edge/WebSocket Tests (`edge-tester/Tests/`)

**Purpose**: Test client perspective through Connect service

**Requirements**:
- Test binary protocol compliance
- Test permission-gated endpoint access
- Test reconnection flows
- Test capability updates on permission changes

### Coverage Philosophy

Aim for **100% coverage of meaningful scenarios** - all reasonable paths through the code that could fail in ways that impact clients or other services. This is not about pedantically testing every input combination, but ensuring every distinct behavior is verified.

**What to test**:
- All business logic branches and error paths
- Edge cases that could cause failures
- Integration points between services
- Generated code (at least once, typically at HTTP tier)

**What NOT to test redundantly**:
- The same logic at multiple tiers (test at lowest appropriate level)
- Trivial property accessors with no logic
- Framework behavior (ASP.NET routing, Dapr SDK internals)

### Coverage Requirements Matrix

| Endpoint Type | Unit Test | HTTP Test | Edge Test |
|---------------|-----------|-----------|-----------|
| CRUD Operations | Required | Required | Optional |
| Authentication | Required | Required | Required |
| Events | Required | Required | Required |
| WebSocket-only | N/A | Via proxy | Required |

### Dapr StateEntry Mocking Limitations

**Important**: Dapr's `StateEntry<T>` class is sealed and cannot be instantiated with empty/null values directly. This affects unit testing of methods that use `GetBulkStateAsync` which returns `IReadOnlyList<BulkStateItem>`.

**Workarounds**:
1. **Mock the BulkStateItem response**: Create proper BulkStateItem instances with serialized JSON values
2. **Use HTTP integration tests**: For complex state operations requiring realistic Dapr behavior, write HTTP tests (`http-tester/Tests/`) that run against real Dapr components
3. **Test business logic separately**: Extract complex logic into testable helper methods that don't depend on Dapr types

**Example - Mocking GetBulkStateAsync**:
```csharp
var bulkItems = new List<BulkStateItem>
{
    new BulkStateItem("key1", JsonSerializer.Serialize(model1), "etag1"),
    new BulkStateItem("key2", JsonSerializer.Serialize(model2), "etag2")
};
_mockDaprClient
    .Setup(d => d.GetBulkStateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(),
        It.IsAny<int?>(), It.IsAny<IReadOnlyDictionary<string, string>?>(),
        It.IsAny<CancellationToken>()))
    .ReturnsAsync(bulkItems);
```

**When HTTP tests are preferred**: Tests that require verifying actual Dapr state store behavior, transaction atomicity, or cross-service event flows should use HTTP integration tests rather than attempting complex mocking.

---

## Tenet 10: X-Permissions Usage (DOCUMENTED)

**Rule**: All endpoints MUST declare x-permissions in schema, even if empty.

### Understanding X-Permissions

- Applies to **WebSocket client connections only**
- **Does NOT restrict** service-to-service calls within the cluster
- Enforced by Connect service when routing client requests
- Endpoints without x-permissions are **not exposed** to WebSocket clients (still reachable via Dapr service-to-service invocation)

### Service-to-Service Calls

Service-to-service calls via Dapr bypass x-permissions entirely. The cluster is a trusted environment - if a service can make a call, it's authorized to do so. This is intentional: internal services don't authenticate as "users" or "admins".

### Role Model & Hierarchy

Hierarchy: `anonymous` → `user` → `developer` → `admin` (higher roles include all lower roles)

**Permission Logic**: Client must have **the highest role specified** AND **all states specified**.

Since higher roles include lower ones, specifying multiple roles is redundant - the highest one is what matters. The common pitfall is assuming role OR state logic, when it's actually role AND state.

```yaml
# CORRECT: User role + must be in lobby
x-permissions:
  - role: user
    states:
      game-session: in_lobby  # Requires BOTH user role AND in_lobby state

# PITFALL: This does NOT mean "admin OR in_lobby"
# It means "admin AND in_lobby" - admin must ALSO be in lobby!
x-permissions:
  - role: admin
    states:
      game-session: in_lobby
```

### Role Selection Guide

| Role | Use When | Examples |
|------|----------|----------|
| `admin` | Destructive or exceptionally sensitive operations | All orchestrator endpoints, account deletion, certain auth endpoints |
| `developer` | Creating/managing resources and game assets, session service management | Character creation, realm management, game-session admin |
| `user` | Requires authentication but no special permissions | Most gameplay endpoints, profile viewing, joining lobbies |
| `anonymous` | Intentionally public (rare) | Server status, public leaderboards |

**Note**: Anonymous/guest client scenarios are not yet implemented - focus on `user`, `developer`, and `admin` for now.

### Permission Levels

```yaml
x-permissions:
  # Admin-only endpoints
  - role: admin
    states: {}

  # Authenticated users (role implies authentication)
  - role: user
    states: {}

  # Public endpoints
  - role: anonymous
    states: {}
```

### State-Based Access Pattern

States represent **contextual navigation** within the application - NOT authentication status.
Authentication is handled by roles (`user` = authenticated, `anonymous` = not authenticated).

Example states that services might set:
- `game-session: in_lobby` - User has joined a game lobby
- `game-session: in_game` - User is in an active game session
- `character: selected` - User has selected a character

States are:
- Session-scoped (per WebSocket connection)
- Managed by Permissions service
- Updated via service events
- Used for progressive API unlocking based on user context

### Example: Progressive API Access

```yaml
# Join lobby endpoint - any authenticated user
/lobby/join:
  post:
    x-permissions:
      - role: user
        states: {}

# Start game endpoint - requires being in a lobby
/game/start:
  post:
    x-permissions:
      - role: user
        states:
          game-session: in_lobby
```

---

## Tenet 11: No Fallback Behavior in Tests (MANDATORY)

**Rule**: Tests MUST validate the intended behavior path, not fallback mechanisms.

### Requirements

- Tests should fail fast when the intended path doesn't work
- Do NOT add HTTP fallbacks to bypass Dapr pub/sub issues
- A test that "works" via a fallback is worse than a failing test (it masks real issues)
- Integration tests must exercise the actual system as it will run in production

### Retries vs Fallbacks

**Retries are acceptable** - retrying the same operation for transient failures:
```csharp
// OK: Retry the same mechanism for transient failures
for (int i = 0; i < 3; i++)
{
    try {
        await daprClient.PublishEventAsync("bannou-pubsub", "entity.action", event);
        break;
    }
    catch when (i < 2) {
        await Task.Delay(100);  // Brief delay before retry
    }
}
```

**Fallbacks are forbidden** - switching to a different mechanism when the intended one fails:
```csharp
// BAD: Fallback to different mechanism
try {
    await daprClient.PublishEventAsync("bannou-pubsub", "entity.action", event);
}
catch {
    // Fallback to direct HTTP - MASKS THE REAL ISSUE
    await httpClient.PostAsync("http://127.0.0.1:3500/v1.0/invoke/bannou/method/...");
}
```

### Example of WRONG Pattern (Historical PermissionsTestHandler)

```csharp
// BAD: HTTP fallback that bypasses actual Dapr pub/sub
try {
    await daprClient.PublishEventAsync("bannou-pubsub", "entity.action", event);
}
catch {
    // Fallback to direct HTTP - MASKS THE REAL ISSUE
    await httpClient.PostAsync("http://127.0.0.1:3500/v1.0/invoke/bannou/method/...");
}
```

### Correct Pattern

```csharp
// GOOD: Let the test fail if pub/sub doesn't work
await daprClient.PublishEventAsync("bannou-pubsub", "entity.action", event);
// If this fails, we need to fix the infrastructure, not work around it
```

### Rationale

- **Masked bugs**: Fallbacks hide real issues until production
- **False confidence**: Passing tests don't mean the system works
- **Technical debt**: Fallbacks accumulate and become load-bearing
- **CI/CD trust**: Green pipeline must mean the system is healthy

---

## Tenet 12: Test Integrity (ABSOLUTE)

**Rule**: Tests MUST assert CORRECT expected behavior. NEVER modify a failing test to match buggy implementation.

### The Golden Rule

**A failing test with correct assertions = implementation bug that needs fixing.**

**NEVER**:
- Change `Times.Never` to `Times.AtLeastOnce` to make tests pass
- Remove assertions that "inconveniently" fail
- Weaken test conditions to accommodate wrong behavior
- Add exceptions or special cases to avoid test failures
- Claim success when tests fail

### When a Test Fails

1. **Verify the test is correct** - Does it assert the expected behavior per requirements?
2. **If test is correct**: The IMPLEMENTATION is wrong - fix the implementation
3. **If test is wrong**: Fix the test to assert correct behavior, then ensure implementation passes
4. **NEVER**: Change a correct test to pass with buggy implementation

### Example: The Session Publishing Bug

```csharp
// CORRECT TEST - asserts sessions without WebSocket connections should NOT receive events
_mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
    "session-without-connection",
    It.IsAny<CapabilitiesRefreshEvent>(),
    It.IsAny<CancellationToken>()), Times.Never);  // CORRECT

// WRONG "FIX" - changing to pass with buggy implementation
_mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
    "session-without-connection",
    It.IsAny<CapabilitiesRefreshEvent>(),
    It.IsAny<CancellationToken>()), Times.AtLeastOnce);  // HIDES BUG!
```

### Why This Matters

- **Masked bugs**: Changing tests hides real issues until production
- **False confidence**: Green tests with weakened assertions are worthless
- **Technical debt**: Hidden bugs compound and cause cascading failures
- **Production crashes**: The session publishing bug causes RabbitMQ channel crashes
- **Trust erosion**: Tests that don't test anything destroy pipeline confidence

### Reporting Requirements

When a test fails, report:
1. What the test was asserting (the expected behavior)
2. What the implementation actually does (the actual behavior)
3. Where in the implementation the bug exists
4. The impact of the bug (e.g., "crashes RabbitMQ channel")

**DO NOT** silently change the test and claim success.

---

## Quick Reference: Common Violations

| Violation | Tenet | Fix |
|-----------|-------|-----|
| Editing Generated/ files | 1 | Edit schema, regenerate |
| Direct database access | 2 | Use DaprClient |
| Missing event publication | 3 | Add PublishEventAsync |
| Plain Dictionary for cache | 4 | Use ConcurrentDictionary |
| Local lock for cross-instance coordination | 4 | Use IDistributedLockProvider |
| Manual HTTP calls | 2 | Use generated clients |
| Scoped helper in Singleton service | 5 | Use Singleton helpers |
| Using Microsoft.AspNetCore.Http.StatusCodes | 6 | Use BeyondImmersion.BannouService.StatusCodes |
| Generic catch returning 500 | 7 | Catch ApiException specifically |
| No tests for service | 9 | Add three-tier tests |
| Missing x-permissions | 10 | Add to schema |
| HTTP fallback in tests | 11 | Remove fallback, fix root cause |
| Changing test to pass with buggy impl | 12 | Keep test, fix implementation |
| GPL library in NuGet package | 17 | Use MIT/BSD alternative or infrastructure container |
| Missing XML documentation on public API | 18 | Add `<summary>`, `<param>`, `<returns>` |
| Composite string FK in API schema | 13 | Use separate ID + Type columns |
| Direct FK constraints for polymorphic | 13 | Use application-level validation |
| GET/path params on WebSocket API endpoint | 14 | Use POST-only pattern (GET endpoints go through NGINX, not WebSocket) |

---

## Tenet 13: Polymorphic Associations (STANDARDIZED)

**Rule**: When entities can reference multiple entity types (e.g., relationships between characters, NPCs, items, locations), use the **Entity ID + Type Column** pattern in schemas and **composite string keys** for state store operations.

**When This Applies**: Use this pattern when a single field needs to reference entities of different types. For relationships between entities of the same type (e.g., Character-to-Character friendships), standard ID references are sufficient.

### The Problem

Polymorphic foreign keys—where a single reference can point to different entity types—cannot use traditional SQL foreign key constraints. This limitation applies equally to Dapr state stores.

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
    relationshipTypeId: { type: string, format: uuid }

EntityType:
  type: string
  enum: [CHARACTER, NPC, ITEM, LOCATION, REALM]
```

### Why Separate Columns?

| Benefit | Explanation |
|---------|-------------|
| **API Clarity** | Self-documenting OpenAPI schema with explicit types |
| **Type Safety** | Enum validation at API boundary catches typos at compile time |
| **Query Filtering** | Easy `if (entityType == EntityType.CHARACTER)` without parsing |
| **Generated Code** | NSwag generates proper enum types, not stringly-typed code |

### Composite Keys for State Store Operations

Use composite string keys internally for Dapr state store operations:

```csharp
private const string STATE_STORE = "relationship-statestore";

// Format: "{entityType}:{entityId}" (lowercase for consistency)
private static string BuildEntityRef(Guid id, EntityType type)
    => $"{type.ToString().ToLowerInvariant()}:{id}";

// Uniqueness constraint key
private static string BuildCompositeKey(
    Guid id1, EntityType type1, Guid id2, EntityType type2, Guid relationshipTypeId)
    => $"composite:{BuildEntityRef(id1, type1)}:{BuildEntityRef(id2, type2)}:{relationshipTypeId}";

// Index key for finding relationships involving an entity
private static string BuildEntityIndexKey(Guid id, EntityType type)
    => $"entity-index:{BuildEntityRef(id, type)}";
```

### Forbidden Patterns

```yaml
# WRONG: Composite string in API schema (loses type safety)
CreateRelationshipRequest:
  properties:
    entity1Ref:
      type: string
      description: "Format: {entityType}:{guid}"  # NO! Requires parsing
    entity2Ref:
      type: string
```

```csharp
// WRONG: Parsing composite strings in business logic
var parts = entity1Ref.Split(':');
var entityType = parts[0];  // NO! Runtime errors, no compile-time safety

// CORRECT: Use typed parameters
public async Task<(StatusCodes, RelationshipResponse?)> CreateRelationshipAsync(
    CreateRelationshipRequest body, CancellationToken ct)
{
    // body.Entity1Type is already EntityType enum - no parsing needed
    if (body.Entity1Type == EntityType.CHARACTER)
    {
        // Validate character exists via generated client
    }
}
```

### Application-Level Referential Integrity

Since Dapr state stores cannot enforce foreign key constraints, implement validation in service logic:

```csharp
public async Task<(StatusCodes, RelationshipResponse?)> CreateRelationshipAsync(
    CreateRelationshipRequest body, CancellationToken ct)
{
    // 1. Validate entities exist
    if (!await ValidateEntityExistsAsync(body.Entity1Id, body.Entity1Type, ct))
        return (StatusCodes.BadRequest, null);
    if (!await ValidateEntityExistsAsync(body.Entity2Id, body.Entity2Type, ct))
        return (StatusCodes.BadRequest, null);

    // 2. Check for duplicate (composite uniqueness)
    var compositeKey = BuildCompositeKey(body.Entity1Id, body.Entity1Type,
        body.Entity2Id, body.Entity2Type, body.RelationshipTypeId);
    if (!string.IsNullOrEmpty(await _daprClient.GetStateAsync<string>(STATE_STORE, compositeKey, ct)))
        return (StatusCodes.Conflict, null);

    // 3. Create the relationship...
}

private async Task<bool> ValidateEntityExistsAsync(Guid id, EntityType type, CancellationToken ct)
{
    return type switch
    {
        EntityType.CHARACTER => await ValidateCharacterAsync(id, ct),
        EntityType.NPC => await ValidateNpcAsync(id, ct),
        _ => false
    };
}
```

### Event-Driven Cascade Handling

Subscribe to entity deletion events to maintain consistency:

```csharp
[Topic("bannou-pubsub", "entity.deleted")]
[HttpPost("handle-entity-deleted")]
public async Task<IActionResult> HandleEntityDeleted([FromBody] EntityDeletedEvent evt)
{
    var indexKey = $"entity-index:{evt.EntityType.ToLowerInvariant()}:{evt.EntityId}";
    var relationshipIds = await _daprClient.GetStateAsync<List<string>>(STATE_STORE, indexKey);

    foreach (var id in relationshipIds ?? new())
        await _relationshipService.EndRelationshipAsync(new EndRelationshipRequest { RelationshipId = id }, default);

    return Ok();
}
```

### Pattern Summary

| Layer | Pattern | Example |
|-------|---------|---------|
| **API Schema** | Separate ID + Type columns | `entity1Id: uuid`, `entity1Type: EntityType` |
| **State Store Keys** | Composite string | `"character:abc123"`, `"rel:character:abc123:npc:def456:friend"` |
| **Validation** | Application-level | Service validates entity existence before relationship creation |
| **Cascade Deletes** | Event-driven | Subscribe to `entity.deleted` events |
| **Uniqueness** | Composite index key | Store relationship ID at composite key to prevent duplicates |

### Why NOT Other Patterns

| Pattern | Why Not |
|---------|---------|
| **Exclusive Arc** (multiple nullable FK columns) | Doesn't map to key-value stores; schema explosion with many entity types |
| **Separate Junction Tables** | N² tables for N entity types; complex union queries |
| **Common Super Table** | Requires cross-service coordination; single point of failure |
| **JSON Column** | No schema validation; poor query performance |

### References

This pattern is informed by industry analysis:
- [DoltHub: Polymorphic Associations](https://www.dolthub.com/blog/2024-06-25-polymorphic-associations/) - Comprehensive comparison of 5 approaches
- [Hashrocket: Modeling Polymorphic Associations](https://hashrocket.com/blog/posts/modeling-polymorphic-associations-in-a-relational-database) - Exclusive arc vs type discriminator analysis
- [GitLab: Polymorphic Associations](https://docs.gitlab.com/ee/development/database/polymorphic_associations.html) - Enterprise guidance recommending separate tables

---

## Tenet 14: Browser-Facing Endpoints (DOCUMENTED)

**Rule**: Some endpoints are accessed directly by browsers through NGINX rather than through the WebSocket binary protocol. These endpoints use standard HTTP methods (GET) and path parameters.

### How Browser-Facing Endpoints Work

Browser-facing endpoints are:
- Routed through NGINX reverse proxy (see `provisioning/openresty/nginx.conf`)
- NOT included in WebSocket API (no x-permissions = not exposed to WebSocket clients)
- Using GET methods and path parameters (not POST-only pattern)

These endpoints bypass the WebSocket binary protocol entirely - they're standard HTTP endpoints that browsers access directly.

### Current Browser-Facing Endpoints

| Service | Endpoints | Reason |
|---------|-----------|--------|
| Website | All `/website/*` | Public website, SEO, caching |
| Auth | `/auth/oauth/{provider}/init` | OAuth redirect flow |
| Auth | `/auth/oauth/{provider}/callback` | OAuth provider callback |
| Connect | `/connect` (GET) | WebSocket upgrade handshake |

### Security Considerations

- Standard web security applies (CORS, CSRF protection, rate limiting)
- OAuth endpoints MUST validate `state` parameter to prevent CSRF
- Website endpoints should implement appropriate caching headers

---

## Tenet 15: Naming Conventions (CONSOLIDATED)

**Rule**: All identifiers MUST follow consistent naming patterns across the codebase. This tenet consolidates all naming conventions in one place.

### 1. Async Method Naming

All asynchronous methods MUST use the `Async` suffix.

**Pattern**: `{Action}Async`

**Examples**:
- `GetAccountAsync`
- `CreateSessionAsync`
- `ValidateTokenAsync`
- `HandleAccountDeletedAsync`

**Rationale**: Callers immediately know if a method needs awaiting. This is the standard C# async naming convention.

### 2. Request/Response Models

All API models follow a consistent naming pattern.

**Patterns**:
- Request: `{Action}Request` (e.g., `CreateAccountRequest`, `GetAccountRequest`)
- Response: `{Entity}Response` (e.g., `AccountResponse`, `SessionResponse`)
- List Response: `{Entity}ListResponse` (e.g., `AccountListResponse`)
- Event Models: `{Entity}{Action}Event` (e.g., `AccountCreatedEvent`, `SessionInvalidatedEvent`)

**Defined In**: OpenAPI schemas in `/schemas/` directory.

### 3. Event Topic Naming

All pub/sub event topics follow a consistent naming pattern.

**Pattern**: `{entity}.{action}` (kebab-case entity, lowercase action)

**Examples**:
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

**Lifecycle Events**: Auto-generated from `x-lifecycle` follow pattern `{entity-kebab-case}.created`, `.updated`, `.deleted`.

### 4. State Store Key Naming

Internal Dapr state store keys within a service follow consistent patterns.

**Patterns**:
| Type | Pattern | Example |
|------|---------|---------|
| Entity keys | `{entity-prefix}{id}` | `account-{guid}` |
| Index keys | `{index-name}:{value}` | `email-index:user@example.com` |
| Hierarchical | Colons for nesting | `session:{id}:capabilities` |
| List keys | `{entity-plural}-list` | `accounts-list` |

**State Store Names**: Follow pattern `{service}-statestore` (Redis) or `mysql-{service}-statestore` (MySQL). See [Generated State Stores Reference](docs/GENERATED-STATE-STORES.md).

### 5. Configuration Property Naming

All service configuration properties follow consistent patterns.

**Requirements**:
- PascalCase for all property names
- Include units in time-based names: `TimeoutSeconds`, `HeartbeatIntervalSeconds`
- Include units in size-based names: `BufferSizeBytes`, `MaxMessageSize`
- Document environment variable in XML comment

**Example**:
```csharp
/// <summary>
/// Maximum time to wait for a response before timing out.
/// Environment variable: BANNOU_TIMEOUTSECONDS
/// </summary>
public int TimeoutSeconds { get; set; } = 30;
```

### 6. Unit Test Naming

Follow the [Osherove naming standard](https://osherove.com/blog/2005/4/3/naming-standards-for-unit-tests.html).

**Pattern**: `UnitOfWork_StateUnderTest_ExpectedBehavior`

**Examples**:
- `GetAccount_WhenAccountExists_ReturnsAccount`
- `CreateSession_WhenTokenExpired_ReturnsUnauthorized`
- `DeleteRelationship_WhenNotFound_ReturnsNotFound`
- `ValidateToken_WhenTokenIsNull_ThrowsArgumentNullException`

**Rationale**: Test names describe the scenario being tested, making test failures immediately understandable.

### Quick Reference Table

| Category | Pattern | Example |
|----------|---------|---------|
| Async methods | `{Action}Async` | `GetAccountAsync` |
| Request models | `{Action}Request` | `CreateAccountRequest` |
| Response models | `{Entity}Response` | `AccountResponse` |
| Event models | `{Entity}{Action}Event` | `AccountCreatedEvent` |
| Event topics | `{entity}.{action}` | `account.created` |
| State keys | `{entity-prefix}{id}` | `account-{guid}` |
| Config properties | PascalCase + units | `TimeoutSeconds` |
| Test methods | Osherove standard | `Method_State_Result` |

---

## Tenet 16: Client Event Schema Pattern (RECOMMENDED)

**Rule**: Services that push events to WebSocket clients MUST define those events in a dedicated `{service}-client-events.yaml` schema file.

### Client Events vs Service Events

| Type | File | Purpose | Consumers |
|------|------|---------|-----------|
| **Client Events** | `{service}-client-events.yaml` | Pushed TO clients via WebSocket | Game clients, SDK |
| **Service Events** | `{service}-events.yaml` | Service-to-service pub/sub via RabbitMQ | Other Bannou services |

Clients need models to deserialize incoming WebSocket events, but don't need service-to-service event models.

### Required Pattern

1. **Define client events** in `/schemas/{service}-client-events.yaml`:

```yaml
components:
  schemas:
    VoicePeerJoinedEvent:
      allOf:
        - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
      type: object
      required: [sessionId]
      properties:
        sessionId:
          type: string
        displayName:
          type: string
          nullable: true
```

2. **Generate models** via `make generate-services PLUGIN={service}`:
   - Creates `lib-{service}/Generated/{Service}ClientEventsModels.cs`

3. **Auto-included in SDKs**:
   - `scripts/generate-client-sdk.sh` auto-discovers `*ClientEventsModels.cs` files
   - Automatically adds to both `Bannou.SDK` and `Bannou.Client.SDK`

### Auto-Discovery Mechanism

The SDK generation script discovers client event models automatically:

```bash
# In scripts/generate-client-sdk.sh
LIB_CLIENT_EVENT_FILES=($(find ./lib-*/Generated -name "*ClientEventsModels.cs" 2>/dev/null || true))
EVENT_FILES=("${EVENT_FILES[@]}" "${LIB_CLIENT_EVENT_FILES[@]}")
```

### Benefits

- **Zero Manual SDK Updates**: New client events automatically included in SDK packages
- **Separation of Concerns**: Client vs service events clearly distinguished in schemas
- **Type Safety**: Game clients get properly typed event models for deserialization
- **Schema Validation**: Client events validated like all other schemas
- **Consistent Pattern**: All services follow identical client event generation

### When to Use

Use `{service}-client-events.yaml` when your service sends events directly to WebSocket clients:
- Voice: `VoicePeerJoinedEvent`, `VoiceRoomClosedEvent`, `VoiceTierUpgradeEvent`
- Connect: `CapabilitiesRefreshEvent`, `DisconnectNotificationEvent`
- GameSession: `PlayerJoinedEvent`, `GameStateChangedEvent`

### When NOT to Use

Do NOT create client events for service-to-service events that clients never see:
- `account.deleted` - Only consumed by AuthService
- `session.invalidated` - Only consumed by ConnectService
- `service.registered` - Internal infrastructure events

---

## Tenet 17: Licensing Requirements (MANDATORY)

**Rule**: All dependencies MUST use permissive licenses (MIT, BSD, Apache 2.0). Copyleft licenses (GPL, LGPL, AGPL) are forbidden for linked code but acceptable for infrastructure containers.

### Core Principle

We use only licenses that do not impose:
- **Copyleft obligations** (requiring derivative works to use the same license)
- **Forced attribution** beyond reasonable notices
- **Share-alike requirements** (requiring source disclosure)

### Acceptable Licenses

| License | Status | Notes |
|---------|--------|-------|
| MIT | ✅ Preferred | No restrictions beyond copyright notice |
| BSD-2-Clause, BSD-3-Clause | ✅ Approved | Minimal attribution requirements |
| Apache 2.0 | ✅ Approved | Patent grant included |
| ISC | ✅ Approved | Functionally equivalent to MIT |
| Unlicense, CC0 | ✅ Approved | Public domain dedication |

### Forbidden Licenses (for linked code)

| License | Status | Reason |
|---------|--------|--------|
| GPL v2/v3 | ❌ Forbidden | Copyleft - forces open-sourcing derivative works |
| LGPL | ❌ Forbidden | Weak copyleft - still has linking obligations |
| AGPL | ❌ Forbidden | Network copyleft - triggers on network use |
| Creative Commons BY-SA | ❌ Forbidden | Share-alike requirement |
| Any "viral" license | ❌ Forbidden | Contaminates proprietary code |

### Infrastructure Container Exception

GPL/LGPL software is acceptable when run as **separate infrastructure containers** that we communicate with via network protocols, provided:

1. **No Linking**: We never link GPL code into our binaries
2. **No Distribution**: We never distribute the GPL software to customers
3. **Network Separation**: Communication is via network protocols (HTTP, UDP, TCP) not function calls
4. **No Modification**: We use unmodified upstream images

**Legal Basis**: Network communication with GPL software does not create derivative works. This is established GPL interpretation - the software runs as a separate process, and network APIs (pipes, sockets, HTTP) do not trigger copyleft.

**Current Infrastructure Containers**:
- RTPEngine (GPLv3) - UDP ng protocol for media control
- Kamailio (GPLv2+) - HTTP JSONRPC for SIP routing

### Docker Compose References Are NOT Distribution

Referencing a GPL-licensed image in docker-compose.yml is NOT distributing the software:

```yaml
# This is a REFERENCE, not distribution
services:
  kamailio:
    image: ghcr.io/kamailio/kamailio-ci:latest  # Points to their registry
```

When someone clones our repo and runs `docker compose pull`:
- The image downloads from **Kamailio's registry** (they distribute it)
- We distributed a **YAML file containing a URL** (not the software)
- GPL compliance is **their responsibility**, not ours

This is legally equivalent to documentation saying "you also need to install Kamailio."

### Version Pinning for License Stability

When a package changes license in newer versions, pin to the last permissive version:

```xml
<!-- SIPSorcery v8.0.14: Last version under pure BSD-3-Clause (pre-May 2025 license change) -->
<PackageReference Include="SIPSorcery" Version="8.0.14" />
```

**Requirements**:
- Document the reason for pinning in XML comment
- Include the version number and license in the comment
- Review pinned packages periodically for security updates

### Verification Process

Before adding any dependency:

1. **Check license** on NuGet, npm, or GitHub
2. **Verify license file** in the repository (not just metadata)
3. **Check for license changes** in recent versions
4. **Document** the license in package reference comment if non-obvious
5. **If uncertain**, ask before adding

### Build Configuration Requirements

Some libraries have license-conditional features:

```bash
# FFmpeg: LGPL by default, but GPL codecs must be disabled
./configure --enable-gpl=no --disable-libx264 --disable-libx265

# Use BSD-licensed alternatives:
# - libvpx (VP8/VP9) instead of x264
# - openh264 (Cisco BSD) for H.264
```

---

## Tenet 18: XML Documentation Standards (REQUIRED)

**Rule**: All public classes, interfaces, methods, and properties MUST have XML documentation comments.

### Required Documentation

**Minimum Requirements**:
- `<summary>` on all public types and members
- `<param>` for all method parameters
- `<returns>` for methods with return values
- `<exception>` for explicitly thrown exceptions

### Class/Interface Documentation

```csharp
/// <summary>
/// Coordinates RTMP streaming for voice rooms by managing FFmpeg processes.
/// Each room with streaming enabled gets a dedicated FFmpeg subprocess.
/// </summary>
/// <remarks>
/// This service implements the RTMP streaming extension described in
/// docs/UPCOMING_PROPOSED_-_VOICE_STREAMING.md
/// </remarks>
public interface IStreamingCoordinator
{
    // ...
}
```

### Method Documentation

```csharp
/// <summary>
/// Validates a JWT token and returns the associated session information.
/// </summary>
/// <param name="token">The JWT token to validate. Must not be null or empty.</param>
/// <param name="ct">Cancellation token for the operation.</param>
/// <returns>
/// A tuple containing the status code and session response.
/// Returns (OK, session) if valid, (Unauthorized, null) if invalid.
/// </returns>
/// <exception cref="ArgumentNullException">Thrown when token is null.</exception>
public async Task<(StatusCodes, SessionResponse?)> ValidateTokenAsync(
    string token,
    CancellationToken ct = default);
```

### Property Documentation

```csharp
/// <summary>
/// Gets or sets the maximum concurrent RTMP streams per node.
/// Environment variable: BANNOU_MAXCONCURRENTSTREAMS
/// </summary>
/// <value>Defaults to 10 streams.</value>
public int MaxConcurrentStreams { get; set; } = 10;
```

### Configuration Properties

Configuration properties MUST document their environment variable:

```csharp
/// <summary>
/// JWT signing secret for token generation and validation.
/// Environment variable: AUTH_JWT_SECRET
/// </summary>
/// <remarks>
/// Must be at least 32 characters for HS256 algorithm.
/// Change this value in production deployments.
/// </remarks>
public string JwtSecret { get; set; } = "default-dev-secret";
```

### When to Use `<remarks>`

Use `<remarks>` for:
- Implementation details not essential to understanding the API
- References to related documentation or design documents
- Performance considerations or caveats
- Historical context or migration notes

### When to Use `<inheritdoc/>`

Use `<inheritdoc/>` when implementing an interface where the base documentation is sufficient:

```csharp
public class TokenService : ITokenService
{
    /// <inheritdoc/>
    public async Task<string> GenerateTokenAsync(Guid accountId, CancellationToken ct)
    {
        // Implementation...
    }
}
```

**Do NOT use `<inheritdoc/>`** when the implementation has important differences from the interface contract.

### Forbidden Patterns

```csharp
// BAD: Empty or trivial documentation
/// <summary>
/// Gets the account.
/// </summary>
public async Task<Account> GetAccountAsync(Guid id);  // NO! Doesn't explain behavior

// BAD: Repeating the method name
/// <summary>
/// CreateAccountAsync
/// </summary>
public async Task CreateAccountAsync(...);  // NO! Not helpful

// BAD: Missing parameter documentation
/// <summary>
/// Validates user credentials and creates a session.
/// </summary>
public async Task<Session> LoginAsync(string email, string password);  // NO! Params undocumented
```

### Documentation for Generated Code

Generated files in `*/Generated/` directories do not require manual documentation - they inherit documentation from schemas via NSwag.

### Build Enforcement

XML documentation warnings are enabled project-wide:

```xml
<PropertyGroup>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  <NoWarn>$(NoWarn);CS1591</NoWarn>  <!-- Only suppress in test projects -->
</PropertyGroup>
```

Production projects SHOULD treat missing documentation as warnings (enable CS1591).

---

## Enforcement

- **Code Review**: All PRs checked against tenets
- **CI/CD**: Automated validation where possible
- **Schema Regeneration**: Must pass after any schema changes
- **Test Coverage**: 100% of meaningful scenarios (see Tenet 9: Coverage Philosophy)

---

*This document is the authoritative source for Bannou service development standards. Updates require team review.*
