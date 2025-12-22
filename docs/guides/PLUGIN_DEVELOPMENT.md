# Plugin Development Guide

This guide walks through creating and extending Bannou service plugins using schema-first development.

## Overview

Bannou services are implemented as **plugins** - independent .NET assemblies that can be loaded or excluded at startup. Each plugin follows the same structure:

```
lib-{service}/
├── Generated/                    # Auto-generated (never edit)
│   ├── {Service}Controller.Generated.cs
│   ├── I{Service}Service.cs
│   ├── {Service}Models.cs
│   └── {Service}ServiceConfiguration.cs
├── {Service}Service.cs           # Your business logic (only manual file)
├── {Service}ServicePlugin.cs     # Plugin registration
└── lib-{service}.csproj
```

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
  - url: http://localhost:3500/v1.0/invoke/bannou/method
    description: Dapr sidecar endpoint

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

[DaprService("example", typeof(IExampleService), lifetime: ServiceLifetime.Scoped)]
public class ExampleService : IExampleService
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<ExampleService> _logger;
    private readonly ExampleServiceConfiguration _configuration;

    public ExampleService(
        DaprClient daprClient,
        ILogger<ExampleService> logger,
        ExampleServiceConfiguration configuration)
    {
        _daprClient = daprClient;
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
- All infrastructure access goes through `DaprClient`

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

Use Dapr state stores for data persistence:

```csharp
private const string STATE_STORE = "example-statestore";

// Save
await _daprClient.SaveStateAsync(STATE_STORE, key, data, cancellationToken: ct);

// Get
var data = await _daprClient.GetStateAsync<ModelType>(STATE_STORE, key, cancellationToken: ct);

// Delete
await _daprClient.DeleteStateAsync(STATE_STORE, key, cancellationToken: ct);
```

State store configuration is in `/provisioning/dapr/components/`.

### Event Publishing

Publish events for other services to consume:

```csharp
await _daprClient.PublishEventAsync(
    "bannou-pubsub",           // Pub/sub component name
    "example-event-topic",      // Topic
    new ExampleEvent { ... },   // Event data
    cancellationToken: ct
);
```

### Event Subscription

Subscribe to events from other services:

```csharp
[Topic("bannou-pubsub", "account-deleted")]
[HttpPost("handle-account-deleted")]
public async Task<IActionResult> HandleAccountDeleted(
    [FromBody] AccountDeletedEvent eventData)
{
    // Handle the event
    return Ok();
}
```

### Service-to-Service Calls

Call other services using generated clients:

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

Generated clients handle Dapr routing automatically via `ServiceAppMappingResolver`.

## POST-Only API Pattern

All service APIs must use POST requests with body parameters:

**Correct** (Bannou services):
```yaml
paths:
  /accounts/get:
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
  /accounts/{accountId}:
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
