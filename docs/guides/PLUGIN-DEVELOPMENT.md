# Plugin Development Guide

This guide walks through creating and extending Bannou service plugins using schema-first development.

## Overview

Bannou services are implemented as **plugins** - independent .NET assemblies that can be loaded or excluded at startup. Each plugin follows the same structure:

```
plugins/lib-{service}/
├── Generated/                       # Auto-generated (never edit)
│   ├── {Service}Controller.cs
│   ├── I{Service}Service.cs
│   ├── {Service}Models.cs
│   └── {Service}ServiceConfiguration.cs
├── {Service}Service.cs              # Business logic implementation
├── {Service}ServiceModels.cs        # Internal data models (storage, cache, DTOs)
├── {Service}ServiceEvents.cs        # Event handlers (partial class)
├── {Service}ServicePlugin.cs        # Plugin registration
└── lib-{service}.csproj
```

> **Manual files**: `{Service}Service.cs`, `{Service}ServiceModels.cs`, and `{Service}ServiceEvents.cs` are the only files you should edit. All files in `Generated/` are auto-generated from schemas.

## The Automation Advantage

Schema-first development means you write **18-35% of the code** for a typical service:

| What You Write | What Gets Generated |
|----------------|---------------------|
| ~200-500 lines of OpenAPI YAML | ~2,000-5,000 lines of C# (controllers, models, clients) |
| ~500-2,000 lines of business logic | Validation, routing, serialization, permissions |
| ~200-800 lines of tests | Test infrastructure and patterns to follow |

**Key stats across 41 services:**
- **~65%** of service code is auto-generated
- Schema-to-code amplification: **~4x** (1 YAML line → 4 C# lines)
- Simple CRUD services: 65-85% generated
- Complex services (Auth, Connect): 35-50% generated

## Creating a New Service

### Step 1: Define the OpenAPI Schema

Create your API specification in the `/schemas/` directory.

> **IMPORTANT**: Before writing any schema, read [SCHEMA-RULES.md](../reference/SCHEMA-RULES.md). It covers NRT compliance, validation keywords, extension attributes (`x-permissions`, `x-lifecycle`, etc.), and 30+ anti-patterns to avoid.

```yaml
# schemas/example-api.yaml
openapi: 3.0.0
info:
  title: Example Service API
  version: 1.0.0
servers:
  - url: http://localhost:5012
    description: Bannou service endpoint

paths:
  /example/greet:
    post:
      operationId: Greet
      summary: Greet a user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GreetRequest'
      responses:
        '200':
          description: Greeting response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/GreetResponse'
        '400':
          description: Invalid request

components:
  schemas:
    GreetRequest:
      type: object
      required: [name]
      properties:
        name:
          type: string
          minLength: 1
          maxLength: 100

    GreetResponse:
      type: object
      required: [message]
      properties:
        message:
          type: string
```

**Critical**: Always use `bannou` as the app-id in the `servers` URL. This ensures generated controller routes match what clients send. See [Bannou Design](../BANNOU_DESIGN.md) for the technical explanation.

### Step 2: Generate the Plugin

Run the generation script:

```bash
scripts/generate-all-services.sh
```

This creates the complete plugin structure with:
- Controller with routing and validation
- Service interface
- Request/response models
- Configuration class
- Project file

### Step 3: Implement Business Logic

Edit the service implementation file:

> **File organization**: Business logic goes in `{Service}Service.cs`. Internal data models (storage models, cache entries, DTOs not exposed via API) go in `{Service}ServiceModels.cs`. Event handlers go in `{Service}ServiceEvents.cs`.

```csharp
// plugins/lib-example/ExampleService.cs
namespace BeyondImmersion.Bannou.Example;

[BannouService("example", typeof(IExampleService), lifetime: ServiceLifetime.Scoped)]
public class ExampleService : IExampleService
{
    private readonly IStateStore<ExampleModel> _stateStore;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<ExampleService> _logger;
    private readonly ExampleServiceConfiguration _configuration;

    public ExampleService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        ILogger<ExampleService> logger,
        ExampleServiceConfiguration configuration)
    {
        _stateStore = stateStoreFactory.GetStore<ExampleModel>(StateStoreDefinitions.Example);
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<(StatusCodes, GreetResponse?)> GreetAsync(
        GreetRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Greeting {Name}", request.Name);

            var response = new GreetResponse
            {
                Message = $"Hello, {request.Name}!"
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to greet {Name}", request.Name);
            return (StatusCodes.InternalServerError, null);
        }
    }
}
```

**Key patterns**:
- Services return `(StatusCodes, ResponseModel?)` tuples
- Use `StatusCodes` enum, not HTTP status codes directly
- All infrastructure access goes through the three infrastructure libs (lib-state, lib-messaging, lib-mesh)

### Step 4: Enable and Test

Enable the service via environment variable:

```bash
EXAMPLE_SERVICE_ENABLED=true
```

Run tests:

```bash
make test PLUGIN=example      # Unit tests for this plugin
make test-http                # HTTP integration tests
make test-edge                # WebSocket tests
```

## Service Implementation Patterns

### State Management

Use `lib-state` for data persistence. The factory provides specialized interfaces for different capabilities:

#### Core Operations (All Backends)

```csharp
// In service constructor - use StateStoreDefinitions constants (schema-first)
private readonly IStateStore<MyModel> _stateStore;

public MyService(IStateStoreFactory stateStoreFactory)
{
    _stateStore = stateStoreFactory.GetStore<MyModel>(StateStoreDefinitions.MyService);
}

// Save
await _stateStore.SaveAsync(key, data, cancellationToken: ct);

// Get
var data = await _stateStore.GetAsync(key, ct);

// Delete
await _stateStore.DeleteAsync(key, ct);

// Optimistic concurrency with ETags
var (value, etag) = await _stateStore.GetWithETagAsync(key, ct);
var saved = await _stateStore.TrySaveAsync(key, modifiedValue, etag, ct);

// Bulk operations
var items = await _stateStore.GetBulkAsync(keys, ct);
await _stateStore.SaveBulkAsync(keyValuePairs, ct);
```

#### Sets and Sorted Sets (Redis + InMemory Only)

Use `ICacheableStateStore<T>` for set membership and leaderboard operations:

```csharp
private readonly ICacheableStateStore<MyModel> _cacheStore;

public MyService(IStateStoreFactory stateStoreFactory)
{
    _cacheStore = stateStoreFactory.GetCacheableStore<MyModel>(StateStoreDefinitions.MyCache);
}

// Set operations - unique item collections
await _cacheStore.AddToSetAsync("online-users", userId, ct);
await _cacheStore.RemoveFromSetAsync("online-users", userId, ct);
var isOnline = await _cacheStore.SetContainsAsync("online-users", userId, ct);
var allOnline = await _cacheStore.GetSetAsync<string>("online-users", ct);

// Sorted set operations - ranked data (leaderboards, priority queues)
await _cacheStore.SortedSetAddAsync("leaderboard:daily", memberId, score, ct);
var rank = await _cacheStore.SortedSetRankAsync("leaderboard:daily", memberId, descending: true, ct);
var topTen = await _cacheStore.SortedSetRangeByRankAsync("leaderboard:daily", 0, 9, descending: true, ct);
await _cacheStore.SortedSetIncrementAsync("leaderboard:daily", memberId, pointsEarned, ct);

// Atomic counter operations - reference counts, rate limiting, page views
var visits = await _cacheStore.IncrementAsync("page-visits", 1, ct);
await _cacheStore.DecrementAsync("available-slots", 1, ct);
var currentCount = await _cacheStore.GetCounterAsync("page-visits", ct);
await _cacheStore.SetCounterAsync("reset-counter", 0, ct);

// Hash operations - multiple fields under one key
await _cacheStore.HashSetAsync<string>("user:123", "lastLogin", DateTime.UtcNow.ToString("O"), ct);
var lastLogin = await _cacheStore.HashGetAsync<string>("user:123", "lastLogin", ct);
await _cacheStore.HashIncrementAsync("user:123", "loginCount", 1, ct);
var allFields = await _cacheStore.HashGetAllAsync<string>("user:123", ct);
var exists = await _cacheStore.HashExistsAsync("user:123", "lastLogin", ct);
await _cacheStore.HashDeleteAsync("user:123", "temporaryField", ct);
```

#### Low-Level Redis Operations (Lua Scripts, Transactions)

Use `IRedisOperations` only for truly low-level operations not covered by `ICacheableStateStore`:

```csharp
public MyService(IStateStoreFactory stateStoreFactory)
{
    // Returns null in InMemory mode - always check!
    _redisOps = stateStoreFactory.GetRedisOperations();
}

// Lua scripts for complex atomic operations spanning multiple keys
if (_redisOps != null)
{
    var result = await _redisOps.ScriptEvaluateAsync(
        luaScript,
        new RedisKey[] { "key1", "key2" },
        new RedisValue[] { "arg1", "arg2" },
        ct);

    // Direct database access for transactions
    var db = _redisOps.GetDatabase();
    var tran = db.CreateTransaction();
    // ... transaction operations
}
```

**Important**: Keys passed to `IRedisOperations` are NOT prefixed - they are raw Redis keys. This enables cross-store atomic operations in Lua scripts but requires manual key management.

**Prefer ICacheableStateStore**: For counters, hashes, sets, and sorted sets, use `ICacheableStateStore<T>` instead of `IRedisOperations`. The cacheable interface:
- Works with InMemory mode (useful for testing)
- Automatically prefixes keys
- Provides type-safe generic methods

#### Interface Summary

| Interface | Factory Method | Backends | Use Case |
|-----------|---------------|----------|----------|
| `IStateStore<T>` | `GetStore<T>()` | All | Core CRUD, bulk ops, ETags |
| `ICacheableStateStore<T>` | `GetCacheableStore<T>()` | Redis, InMemory | Sets, sorted sets, counters, hashes |
| `ISearchableStateStore<T>` | `GetSearchableStore<T>()` | Redis+Search | Full-text search (extends Cacheable) |
| `IQueryableStateStore<T>` | `GetQueryableStore<T>()` | MySQL | LINQ queries |
| `IRedisOperations` | `GetRedisOperations()` | Redis | Lua scripts, transactions

**Note**: `ISearchableStateStore<T>` extends `ICacheableStateStore<T>`, so searchable stores have access to all cacheable operations (sets, sorted sets, counters, hashes) in addition to full-text search.

State store backend is configured via `IStateStoreFactory` service configuration.

### Event Publishing

Use `lib-messaging` to publish events for other services to consume:

```csharp
// In service
private readonly IMessageBus _messageBus;

public MyService(IMessageBus messageBus)
{
    _messageBus = messageBus;
}

// Publish event
await _messageBus.PublishAsync(
    "example.action",           // Topic/routing key
    new ExampleEvent { ... },   // Event data
    cancellationToken: ct
);
```

### Event Subscription

Subscribe to events via `lib-messaging`:

```csharp
// Static subscription (survives restarts)
await _messageSubscriber.SubscribeAsync<AccountDeletedEvent>(
    "account.deleted",
    async (evt, ct) => await HandleAccountDeletedAsync(evt, ct));

// Dynamic subscription (per-session, disposable)
var sub = await _messageSubscriber.SubscribeDynamicAsync<MyEvent>(
    "session.events",
    async (evt, ct) => await HandleEventAsync(evt, ct));
// Later: await sub.DisposeAsync();
```

### Service-to-Service Calls

Call other services using generated clients (which use `lib-mesh` internally):

```csharp
private readonly IAuthClient _authClient;

public async Task<(StatusCodes, SomeResponse?)> SomeMethodAsync(...)
{
    // Validate token with Auth service
    var (status, validation) = await _authClient.ValidateTokenAsync(
        new ValidateTokenRequest { Token = token });

    if (status != StatusCodes.OK)
    {
        return (StatusCodes.Unauthorized, null);
    }

    // Continue with business logic...
}
```

Generated clients use mesh service resolution for routing, supporting both monolith ("bannou") and distributed deployment topologies.

For direct mesh invocation without generated clients:

```csharp
private readonly IMeshInvocationClient _meshClient;

public async Task<SomeResponse> CallServiceAsync(SomeRequest request, CancellationToken ct)
{
    return await _meshClient.InvokeMethodAsync<SomeRequest, SomeResponse>(
        "service-name",
        "method-name",
        request,
        ct);
}
```

### Seeded Resources

Plugins can provide **seeded resources** - read-only factory defaults (ABML behaviors, templates, configuration data) that are discoverable via lib-resource.

**When to use seeded resources:**
- Shipping default ABML behavior definitions with lib-actor
- Providing template species/realm configurations
- Including scenario templates for game designers
- Any static content that should be queryable at runtime

**Implementation using EmbeddedResourceProvider:**

1. Add embedded resources to your plugin project:
   ```xml
   <!-- In lib-{service}.csproj -->
   <ItemGroup>
     <EmbeddedResource Include="Resources\Behaviors\*.yaml" />
   </ItemGroup>
   ```

2. Create a provider by subclassing `EmbeddedResourceProvider`:
   ```csharp
   using BeyondImmersion.BannouService.Providers;
   using System.Reflection;

   public class BehaviorSeededProvider : EmbeddedResourceProvider
   {
       public override string ResourceType => "behavior";
       public override string ContentType => "application/yaml";

       protected override Assembly ResourceAssembly =>
           typeof(BehaviorSeededProvider).Assembly;

       protected override string ResourcePrefix =>
           "BeyondImmersion.LibActor.Resources.Behaviors.";
   }
   ```

3. Register in your plugin's DI setup:
   ```csharp
   // In ConfigureServices:
   services.AddSingleton<ISeededResourceProvider, BehaviorSeededProvider>();
   ```

4. Query via lib-resource API:
   ```
   POST /resource/seeded/list
   { "resourceType": "behavior" }

   POST /resource/seeded/get
   { "resourceType": "behavior", "identifier": "idle" }
   ```

**Note:** Seeded resources are read-only. If consumers need to modify them, they should copy to their own state stores.

See [RESOURCE.md](../plugins/RESOURCE.md#seeded-resource-management) for full documentation.

## POST-Only API Pattern

All service APIs must use POST requests with body parameters:

**Correct** (Bannou services):
```yaml
paths:
  /account/get:
    post:
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetAccountRequest'

components:
  schemas:
    GetAccountRequest:
      type: object
      required: [account_id]
      properties:
        account_id:
          type: string
          format: uuid
```

**Incorrect** (breaks zero-copy routing):
```yaml
paths:
  /account/{accountId}:
    get:
      parameters:
        - name: accountId
          in: path
```

Path parameters create variable paths that can't map to static GUIDs, breaking the WebSocket routing system.

## Adding Permissions

Use the `x-permissions` extension to declare endpoint access requirements:

```yaml
paths:
  /example/admin-only:
    post:
      x-permissions:
        roles: [admin]
      # ...

  /example/authenticated:
    post:
      x-permissions:
        roles: [user, admin]
      # ...

  /example/public:
    post:
      x-permissions:
        roles: [anonymous]
      # ...
```

See [Permissions Specification](../X-PERMISSIONS-SPECIFICATION.md) for complete documentation.

## Configuration

Add service-specific configuration to the schema:

```yaml
x-service-configuration:
  properties:
    MaxRetries:
      type: integer
      default: 3
    TimeoutSeconds:
      type: integer
      default: 30
    EnableCaching:
      type: boolean
      default: true
```

This generates a typed configuration class. Values come from environment variables:

```bash
BANNOU_EXAMPLE_MaxRetries=5
BANNOU_EXAMPLE_TimeoutSeconds=60
```

## Testing Your Plugin

### Unit Tests

Located in `plugins/lib-{service}.tests/`:

```csharp
[Test]
public async Task Greet_ValidName_ReturnsGreeting()
{
    // Arrange
    var service = CreateService();
    var request = new GreetRequest { Name = "Alice" };

    // Act
    var (status, response) = await service.GreetAsync(request);

    // Assert
    Assert.That(status, Is.EqualTo(StatusCodes.OK));
    Assert.That(response?.Message, Is.EqualTo("Hello, Alice!"));
}
```

### Integration Tests

HTTP tests in `tools/http-tester/` verify service-to-service communication:

```csharp
[Test]
public async Task Greet_ThroughHttp_ReturnsGreeting()
{
    var client = CreateExampleClient();
    var response = await client.GreetAsync(new GreetRequest { Name = "Alice" });
    Assert.That(response.Message, Contains.Substring("Alice"));
}
```

### Edge Tests

WebSocket tests in `tools/edge-tester/` verify the full protocol:

```csharp
[Test]
public async Task Greet_ThroughWebSocket_ReturnsGreeting()
{
    await ConnectAndAuthenticate();

    var response = await SendRequest<GreetRequest, GreetResponse>(
        "example/greet",
        new GreetRequest { Name = "Alice" });

    Assert.That(response.Message, Contains.Substring("Alice"));
}
```

## Common Pitfalls

### Never Edit Generated Code
Files in `Generated/` directories are overwritten on regeneration. Put all custom logic in the `*Service.cs` file.

### Don't Use Path Parameters
Path parameters break zero-copy WebSocket routing. Always use POST with body parameters.

### Always Use StatusCodes Enum
Return `StatusCodes.OK`, not `200`. This keeps services independent of HTTP concerns.

### Don't Forget the Server URL
The `servers` URL must use `bannou` as the app-id or controller routes won't match.

### Run Formatting After Generation
```bash
make format
```
This fixes line endings and applies code style rules.

## Next Steps

- [Testing Guide](TESTING.md) - Detailed testing documentation (MANDATORY reading for test placement decisions)
- [Deployment Guide](DEPLOYMENT.md) - Deploy your service to production
- [Plugin Deep-Dives](../plugins/) - Comprehensive documentation for all 41 services (see how existing services handle similar patterns)
- [TENETS Reference](../reference/TENETS.md) - Development rules organized by category (Foundation, Implementation, Quality)
- [Bannou Design](../BANNOU_DESIGN.md) - Understand the architecture
- [Development Rules](../reference/TENETS.md) - Mandatory development constraints
