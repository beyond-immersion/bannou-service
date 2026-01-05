# Plugin Development Guide

This guide walks through creating and extending Bannou service plugins using schema-first development.

## Overview

Bannou services are implemented as **plugins** - independent .NET assemblies that can be loaded or excluded at startup. Each plugin follows the same structure:

```
lib-{service}/
├── Generated/                    # Auto-generated (never edit)
│   ├── {Service}Controller.cs
│   ├── I{Service}Service.cs
│   ├── {Service}Models.cs
│   └── {Service}ServiceConfiguration.cs
├── {Service}Service.cs           # Your business logic (only manual file)
├── {Service}ServicePlugin.cs     # Plugin registration
└── lib-{service}.csproj
```

## The Automation Advantage

Schema-first development means you write **18-35% of the code** for a typical service:

| What You Write | What Gets Generated |
|----------------|---------------------|
| ~200-500 lines of OpenAPI YAML | ~2,000-5,000 lines of C# (controllers, models, clients) |
| ~500-2,000 lines of business logic | Validation, routing, serialization, permissions |
| ~200-800 lines of tests | Test infrastructure and patterns to follow |

**Key stats across 18 services:**
- **63.6%** of service code is auto-generated
- Schema-to-code amplification: **3.97x** (1 YAML line → 4 C# lines)
- Simple CRUD services: 65-85% generated
- Complex services (Auth, Connect): 35-50% generated

For the complete analysis with per-service breakdowns, see [Automation Analysis](../reference/AUTOMATION-ANALYSIS.md).

## Creating a New Service

### Step 1: Define the OpenAPI Schema

Create your API specification in the `/schemas/` directory:

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

Edit the service implementation file (the only manual file):

```csharp
// lib-example/ExampleService.cs
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
        _stateStore = stateStoreFactory.Create<ExampleModel>("example");
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

Use `lib-state` for data persistence (supports Redis and MySQL backends):

```csharp
// In service constructor
private readonly IStateStore<MyModel> _stateStore;

public MyService(IStateStoreFactory stateStoreFactory)
{
    _stateStore = stateStoreFactory.Create<MyModel>("my-service");
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
```

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

Located in `lib-{service}.tests/`:

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

HTTP tests in `http-tester/` verify service-to-service communication:

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

WebSocket tests in `edge-tester/` verify the full protocol:

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

- [Testing Guide](TESTING.md) - Detailed testing documentation
- [Deployment Guide](DEPLOYMENT.md) - Deploy your service to production
- [Bannou Design](../BANNOU_DESIGN.md) - Understand the architecture
- [Development Rules](../reference/TENETS.md) - Mandatory development constraints
