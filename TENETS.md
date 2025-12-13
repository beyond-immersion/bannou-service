# Bannou Service Development Tenets

> **Version**: 1.0
> **Last Updated**: 2025-12-11
> **Scope**: All Bannou microservices and related infrastructure

This document establishes the mandatory tenets for developing high-quality Bannou services. All service implementations, tests, and infrastructure MUST adhere to these tenets. Tenets must not be changed or added without EXPLICIT approval, without exception.

---

## Tenet 1: Schema-First Development (ABSOLUTE)

**Rule**: All API contracts, models, events, and configurations MUST be defined in OpenAPI YAML schemas before any code is written.

### Requirements

- Define all endpoints in `/schemas/{service}-api.yaml`
- Define all events in `/schemas/{service}-events.yaml` or `common-events.yaml`
- Use `x-permissions` to declare role/state requirements for WebSocket clients
- Run `scripts/generate-all-services.sh` to generate all code
- **NEVER** manually edit files in `*/Generated/` directories

### Best Practices

- **POST-only pattern** for internal service APIs (enables zero-copy WebSocket routing)
- Path parameters allowed only for browser-facing endpoints (Website, OAuth redirects)
- Consolidate shared enums in `components/schemas` with `$ref` references
- Follow naming convention: `{Action}Request`, `{Entity}Response`

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

### Exceptions: Orchestrator Service and Connect Service

Orchestrator uses direct Redis/RabbitMQ connections to avoid Dapr chicken-and-egg startup dependency. Connect uses a direct RabbitMQ for dynamic per-session channel subscriptions, which Dapr doesn't support. These are the **only** exceptions.

### State Store Naming Convention

- `{service}-statestore` for service-specific persistent state
- `auth-statestore` for authentication/session state (Redis, ephemeral)
- `accounts-statestore` for account data (MySQL, persistent)

---

## Tenet 3: Event-Driven Architecture (REQUIRED)

**Rule**: All meaningful state changes MUST publish events, even without current consumers.

### Required Events Per Service

| Service | Required Events |
|---------|-----------------|
| Accounts | `account.created`, `account.updated`, `account.deleted` |
| Auth | `session.created`, `session.invalidated`, `session.updated` |
| Permissions | `service.registered`, `capability.updated` |
| GameSession | `game-session.created`, `game-session.ended`, `game-action.executed` |
| Subscriptions | `subscription.created`, `subscription.expired`, `subscription.revoked` |

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

Format: `{entity}.{action}`

Examples:
- `account.deleted`
- `session.invalidated`
- `game-session.created`

### Event Handler Pattern

```csharp
// Events/ServiceEventsController.cs
[ApiController]
[Route("[controller]")]
public class ServiceEventsController : ControllerBase
{
    [Topic("bannou-pubsub", "entity.action")]
    [HttpPost("handle-entity-action")]
    public async Task<IActionResult> HandleEntityAction([FromBody] EntityActionEvent evt)
    {
        // Process event
        return Ok();
    }
}
```

---

## Tenet 4: Multi-Instance Safety (MANDATORY)

**Rule**: Services MUST be safe to run as multiple instances across multiple nodes.

### Requirements

1. **No in-memory state** that isn't reconstructible from Dapr state stores
2. **Use atomic Dapr operations** for state that requires consistency
3. **Use ConcurrentDictionary** for local caches, never plain Dictionary
4. **Prefer Dapr distributed locks** over local locks for cross-instance coordination

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

// Local locks for cross-instance coordination
private readonly object _lock = new object();
lock (_lock) { ... } // Only for in-process coordination!
```

### Service Lifetime Guidelines

- **Scoped**: Default for stateless services (Auth, Accounts, GameSession)
- **Singleton**: For services with connection state or caches (Connect, Permissions)

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

    // Required dependencies
    private readonly DaprClient _daprClient;
    private readonly ILogger<ServiceNameService> _logger;
    private readonly ServiceNameServiceConfiguration _configuration;

    // Optional: Generated service clients for cross-service calls
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
        _authClient = authClient;
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

### Helper Service Decomposition

For complex services, decompose into helper services:

```csharp
// Main service delegates to helpers
public class AuthService : IAuthService
{
    private readonly ITokenService _tokenService;
    private readonly ISessionService _sessionService;
    private readonly IOAuthProviderService _oauthService;

    // Delegate authentication tasks appropriately
}
```

Benefits:
- Single responsibility principle
- Easier unit testing
- Clearer code organization

---

## Tenet 6: Return Pattern (MANDATORY)

**Rule**: All service methods MUST return `(StatusCodes, TResponse?)` tuples.

### Pattern

```csharp
// Success case
return (StatusCodes.OK, new ResponseModel { ... });

// Not found
return (StatusCodes.NotFound, null);

// Validation error
return (StatusCodes.BadRequest, null);

// Unauthorized
return (StatusCodes.Unauthorized, null);

// Server error
return (StatusCodes.InternalServerError, null);
```

### Rationale

Tuple pattern enables clean status code propagation without throwing exceptions for expected failure cases. This:
- Provides explicit status handling
- Avoids exception overhead for normal flows
- Makes error paths visible in code
- Enables clean controller response mapping

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
    // Specific handling for known API errors
    _logger.LogWarning(ex, "Service call failed with status {Status}", ex.StatusCode);
    return (MapStatusCode(ex.StatusCode), null);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Unexpected error calling service");
    return (StatusCodes.InternalServerError, null);
}
```

### Error Granularity

Services MUST distinguish between:
- **400 Bad Request**: Invalid input/validation failures
- **401 Unauthorized**: Missing or invalid authentication
- **403 Forbidden**: Valid auth but insufficient permissions
- **404 Not Found**: Resource doesn't exist
- **409 Conflict**: State conflict (duplicate creation, etc.)
- **500 Internal Server Error**: Unexpected failures

### Error Event Emission (ServiceErrorEvent)

- Emit `ServiceErrorEvent` (from `common-events.yaml`) only for unexpected/internal failures (similar to Sentry).
- Do **not** emit for validation/user errors or expected conflicts.
- Do not treat error events as success/failure signals for workflows; return API responses or use explicit callbacks/events for that.
- Redact sensitive information; include correlation/request IDs and minimal structured context.

---

## Tenet 8: Logging Standards (REQUIRED)

**Rule**: All operations MUST include appropriate logging with structured data.

### Required Log Points

1. **Operation Entry** (Debug): Log input parameters
2. **External Calls** (Debug): Log before Dapr/service calls
3. **Business Decisions** (Information): Log significant state changes
4. **Errors** (Error): Log exceptions with context
5. **Security Events** (Warning): Log auth failures, permission denials

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
            _logger.LogInformation("Account {AccountId} not found", body.AccountId);
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
public async Task GetAccount_WhenExists_ReturnsAccount()
{
    // Arrange
    var accountId = Guid.NewGuid().ToString();
    var expectedAccount = new AccountModel { Id = accountId, DisplayName = "Test" };
    _mockDaprClient
        .Setup(d => d.GetStateAsync<AccountModel>(It.IsAny<string>(), accountId, null, default))
        .ReturnsAsync(expectedAccount);

    // Act
    var (status, result) = await _service.GetAccountAsync(
        new GetAccountRequest { AccountId = accountId });

    // Assert
    Assert.Equal(StatusCodes.OK, status);
    Assert.NotNull(result);
    Assert.Equal("Test", result.DisplayName);
}
```

### Tier 2 - HTTP Integration Tests (`http-tester/Tests/`)

**Purpose**: Test actual service-to-service calls via Dapr

**Requirements**:
- Test CRUD operations with real Dapr state stores
- Test event publication and cross-service effects
- Test error responses from actual services
- Validate proper status codes returned

```csharp
public class AccountTestHandler : IServiceTestHandler
{
    public async Task<TestResult> TestCreateAccount(IAccountsClient client)
    {
        var request = new CreateAccountRequest { Email = "test@example.com" };
        var (status, result) = await client.CreateAccountAsync(request);

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

### Coverage Requirements Matrix

| Endpoint Type | Unit Test | HTTP Test | Edge Test |
|---------------|-----------|-----------|-----------|
| CRUD Operations | Required | Required | Optional |
| Authentication | Required | Required | Required |
| Events | Required | Required | Required |
| WebSocket-only | N/A | Via proxy | Required |

---

## Tenet 10: X-Permissions Usage (DOCUMENTED)

**Rule**: All endpoints MUST declare x-permissions in schema, even if empty.

### Understanding X-Permissions

- Applies to **WebSocket client connections only**
- **Does NOT restrict** service-to-service calls within the cluster (service is not a role)
- Enforced by Connect service when routing client requests

### Role Model & Hierarchy

- Hierarchy: `anonymous` → `user` → `developer` → `admin` (higher roles include lower)
- **Exactly one role per endpoint.** Combining roles makes them all required.
- Role + state are **ANDed**: if both are present the client must satisfy both.
- Prefer `developer` for internal/dev-only APIs (e.g., website CMS, tooling).
- Use `admin` only for truly privileged operations (orchestrator, account CRUD, etc.).
- Use `anonymous` only for endpoints that are intentionally public (rare: status/leaderboards).
- State-only gating is valid (e.g., game-session flows) but you may also combine with a role when needed.

### Permission Levels

```yaml
x-permissions:
  # Admin-only endpoints
  - role: admin
    states: {}

  # Authenticated users with specific state
  - role: user
    states:
      auth: authenticated

  # Public endpoints
  - role: anonymous
    states: {}
```

### State-Based Access Pattern

States represent navigation context:
- `auth: authenticated` - User has valid session
- `lobby: joined` - User is in a game lobby
- `game: active` - User is in an active game session

States are:
- Session-scoped (per WebSocket connection)
- Managed by Permissions service
- Updated via service events
- Used for progressive API unlocking

### Example: Progressive API Access

```yaml
# Join lobby endpoint - requires authentication only
/lobby/join:
  post:
    x-permissions:
      - role: user
        states:
          auth: authenticated

# Start game endpoint - requires being in lobby
/game/start:
  post:
    x-permissions:
      - role: user
        states:
          auth: authenticated
          lobby: joined
```

---

## Tenet 11: No Fallback Behavior in Tests (MANDATORY)

**Rule**: Tests MUST validate the intended behavior path, not fallback mechanisms.

### Requirements

- Tests should fail fast when the intended path doesn't work
- Do NOT add HTTP fallbacks to bypass Dapr pub/sub issues
- A test that "works" via a fallback is worse than a failing test (it masks real issues)
- Integration tests must exercise the actual system as it will run in production

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
| Manual HTTP calls | 2 | Use generated clients |
| Generic catch returning 500 | 7 | Catch ApiException specifically |
| No tests for service | 9 | Add three-tier tests |
| Missing x-permissions | 10 | Add to schema |
| HTTP fallback in tests | 11 | Remove fallback, fix root cause |
| Changing test to pass with buggy impl | 12 | Keep test, fix implementation |

---

## Enforcement

- **Code Review**: All PRs checked against tenets
- **CI/CD**: Automated validation where possible
- **Schema Regeneration**: Must pass after any schema changes
- **Test Coverage**: Minimum 80% on business logic

---

*This document is the authoritative source for Bannou service development standards. Updates require team review.*
