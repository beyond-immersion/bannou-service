# Roslyn Source Generation and Service Mapping Events System

## Table of Contents
1. [Overview](#overview)
2. [Roslyn Source Generator System](#roslyn-source-generator-system)
3. [Service Mapping Events System](#service-mapping-events-system)
4. [Development Workflow Integration](#development-workflow-integration)
5. [Implementation Examples](#implementation-examples)
6. [Testing Strategies](#testing-strategies)
7. [Production Deployment](#production-deployment)

## Overview

The Bannou architecture employs a sophisticated combination of compile-time source generation and runtime service discovery to enable both development efficiency and production scalability. This document details the two core systems that make this possible:

1. **Roslyn Source Generators**: Automated code scaffolding from OpenAPI schemas
2. **Service Mapping Events**: Dynamic runtime service discovery via RabbitMQ/Dapr

Together, these systems enable Bannou's unique "omnipotent monolith" deployment model where a single codebase can run as either a monolithic application or distributed microservices.

## Roslyn Source Generator System

### Architecture Overview

The Roslyn source generation system consists of two primary generators that work in tandem to create a complete service implementation from OpenAPI schemas:

```
schemas/*.yaml → Source Generators → Generated Code → Compilation
                     ↓                     ↓
           ServiceScaffoldGenerator   EventModelGenerator
                     ↓                     ↓
              Service Interfaces      Event Models
              Service Stubs          Event Publishers
              Client Registrations   Event Subscribers
              DI Extensions          Event Handlers
```

### ServiceScaffoldGenerator

Located in `/bannou-source-generator/ServiceScaffoldGenerator.cs`, this generator creates the core service scaffolding.

#### Key Features

1. **MSBuild Integration**: Controlled by the `GenerateNewServices` property
2. **Schema-Driven**: Reads OpenAPI YAML files from `/schemas/*-api.yaml`
3. **Preservation of Existing Code**: Only generates stub files if they don't exist
4. **Comprehensive Output**: Creates interfaces, implementations, clients, and DI extensions

#### Generated Artifacts

For each service schema (e.g., `accounts-api.yaml`), the generator produces:

**1. Service Interface** (`IAccountsService.g.cs`):
```csharp
namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// Service interface for Accounts operations.
/// Generated from OpenAPI schema: accounts-api.yaml
/// </summary>
public interface IAccountsService
{
    Task<IActionResult> GetStatus(CancellationToken cancellationToken = default);
    Task<IActionResult> CreateAccount(AccountCreateRequest request, CancellationToken cancellationToken = default);
    // ... additional methods from schema
}
```

**2. Service Implementation Stub** (`AccountsService.stub.g.cs`):
```csharp
namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// Default implementation for Accounts operations.
/// Generated stub - implement your business logic here.
/// This file is only generated if it doesn't already exist.
/// </summary>
public class AccountsService : IAccountsService
{
    private readonly ILogger<AccountsService> _logger;

    public AccountsService(ILogger<AccountsService> logger)
    {
        _logger = logger;
    }

    // TODO: Implement service methods based on IAccountsService interface
}
```

**3. Client Registration** (`AccountsClientExtensions.g.cs`):
```csharp
namespace BeyondImmersion.BannouService.Accounts;

public static partial class AccountsClientExtensions
{
    /// <summary>
    /// Registers the AccountsClient with Dapr routing support.
    /// Uses dynamic app-id resolution defaulting to "bannou".
    /// </summary>
    public static IServiceCollection AddAccountsClient(this IServiceCollection services)
    {
        return services.AddDaprServiceClient<AccountsClient, IAccountsClient>("accounts");
    }
}
```

**4. Service Registration** (`AccountsServiceExtensions.g.cs`):
```csharp
namespace BeyondImmersion.BannouService.Accounts;

public static partial class AccountsServiceExtensions
{
    /// <summary>
    /// Registers Accounts service and related dependencies.
    /// </summary>
    public static IServiceCollection AddAccountsService(this IServiceCollection services)
    {
        services.AddScoped<IAccountsService, AccountsService>();
        // Additional service-specific dependencies
        return services;
    }
}
```

### EventModelGenerator

Located in `/bannou-source-generator/EventModelGenerator.cs`, this generator creates event-driven communication infrastructure.

#### Key Features

1. **Event Schema Processing**: Reads `/schemas/*-events.yaml` files
2. **Pub/Sub Integration**: Generates Dapr pub/sub handlers and publishers
3. **Type-Safe Events**: Creates strongly-typed event models from schemas
4. **Extensible Handlers**: Partial classes allow custom event handling logic

#### Generated Artifacts

For each event schema (e.g., `service-mappings-events.yaml`), the generator produces:

**1. Event Models** (`ServiceMappingsEventModels.g.cs`):
```csharp
namespace BeyondImmersion.BannouService.ServiceMappings.Events;

public abstract class ServiceMappingsEventBase
{
    [Required]
    [JsonProperty("eventId")]
    public string EventId { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class ServiceMappingEvent : ServiceMappingsEventBase
{
    [Required]
    [JsonProperty("serviceName")]
    public string ServiceName { get; set; }

    [Required]
    [JsonProperty("appId")]
    public string AppId { get; set; }

    [Required]
    [JsonProperty("action")]
    public string Action { get; set; } // register, update, unregister

    [JsonProperty("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }
}
```

**2. Event Publisher** (`ServiceMappingsEventPublisher.g.cs`):
```csharp
public interface IServiceMappingsEventPublisher
{
    Task PublishEventAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken = default)
        where TEvent : ServiceMappingsEventBase;
}

public class ServiceMappingsEventPublisher : IServiceMappingsEventPublisher
{
    private const string PUB_SUB_NAME = "bannou-pubsub";
    private const string TOPIC_NAME = "bannou-service-mappings-events";

    public async Task PublishEventAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken = default)
        where TEvent : ServiceMappingsEventBase
    {
        await _daprClient.PublishEventAsync(PUB_SUB_NAME, TOPIC_NAME, eventData, cancellationToken);
    }
}
```

**3. Event Subscriber** (`ServiceMappingsEventSubscriber.g.cs`):
```csharp
[ApiController]
[Route("api/events/service-mappings")]
public partial class ServiceMappingsEventSubscriber : ControllerBase
{
    [Topic("bannou-pubsub", "bannou-service-mappings-events")]
    [HttpPost("handle")]
    public async Task<IActionResult> HandleEventAsync([FromBody] ServiceMappingsEventBase eventData)
    {
        // Routes to specific handlers based on event type
        var handled = await RouteEventAsync(eventData);
        return handled ? Ok() : BadRequest("No handler found");
    }

    protected virtual async Task<bool> RouteEventAsync(ServiceMappingsEventBase eventData)
    {
        // Override in partial class for custom event handling
        return await Task.FromResult(true);
    }
}
```

### Build Integration

The source generators are integrated into the MSBuild process through the `bannou-service.csproj`:

```xml
<!-- Service generation flag -->
<PropertyGroup>
  <GenerateNewServices Condition="'$(GenerateNewServices)' == ''">false</GenerateNewServices>
</PropertyGroup>

<!-- Source generator reference -->
<ItemGroup>
  <ProjectReference Include="../bannou-source-generator/bannou-source-generator.csproj" 
                    OutputItemType="Analyzer" 
                    ReferenceOutputAssembly="false" />
</ItemGroup>

<!-- Schema files as additional inputs -->
<ItemGroup>
  <AdditionalFiles Include="../schemas/*.yaml" />
</ItemGroup>

<!-- Service generation target -->
<Target Name="GenerateServices" BeforeTargets="Build" Condition="'$(GenerateNewServices)' == 'true'">
  <Message Text="🔧 Generating services from schemas..." Importance="high" />
  <!-- NSwag generation -->
  <Exec Command="$(NSwagExe_Net90) run nswag.json" />
  <!-- Fix line endings -->
  <Exec Command="../fix-generated-line-endings.sh" />
  <Message Text="✅ Service generation completed" Importance="high" />
</Target>
```

## Service Mapping Events System

### Architecture Overview

The service mapping events system enables dynamic runtime discovery and routing of services in distributed deployments:

```
Service Registration → RabbitMQ → Dapr Pub/Sub → Service Mapping Handler
                           ↓                            ↓
                    ServiceMappingEvent          Update Mapping Resolver
                           ↓                            ↓
                    Other Services            Route Future Requests
```

### Core Components

#### ServiceAppMappingResolver

Located in `/bannou-service/ServiceClients/ServiceAppMappingResolver.cs`, this component maintains the dynamic service-to-app-id mapping.

**Key Features**:
- Thread-safe `ConcurrentDictionary` for mappings
- Default "bannou" app-id for local omnipotent deployment
- Dynamic updates from RabbitMQ events
- Fallback mechanism for unmapped services

```csharp
public class ServiceAppMappingResolver : IServiceAppMappingResolver
{
    private readonly ConcurrentDictionary<string, string> _serviceMappings = new();
    private const string DEFAULT_APP_ID = "bannou"; // "almighty" - handles everything locally

    public string GetAppIdForService(string serviceName)
    {
        // Check for dynamic mapping first
        if (_serviceMappings.TryGetValue(serviceName, out var mappedAppId))
        {
            return mappedAppId;
        }
        // Default to "bannou" (omnipotent local node)
        return DEFAULT_APP_ID;
    }

    public void UpdateServiceMapping(string serviceName, string appId)
    {
        _serviceMappings.AddOrUpdate(serviceName, appId, (key, oldValue) => appId);
    }

    public void RemoveServiceMapping(string serviceName)
    {
        _serviceMappings.TryRemove(serviceName, out _);
    }
}
```

#### ServiceMappingEvent Model

The event model represents service discovery changes:

```csharp
public class ServiceMappingEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string ServiceName { get; set; } = "";  // e.g., "accounts", "character-agent"
    public string AppId { get; set; } = "";        // e.g., "bannou-accounts-prod"
    public string Action { get; set; } = "";       // register, update, unregister
    public Dictionary<string, object>? Metadata { get; set; }
}
```

#### ServiceMappingEventHandler

Located in `/bannou-service/ServiceClients/ServiceMappingEventHandler.cs`, this controller handles incoming service mapping events:

```csharp
[ApiController]
[Route("api/events/service-mapping")]
public class ServiceMappingEventHandler : ControllerBase
{
    [Topic("bannou-pubsub", "bannou-service-mappings")]
    [HttpPost("handle")]
    public async Task<IActionResult> HandleServiceMappingEventAsync([FromBody] ServiceMappingEvent eventData)
    {
        // Update the core mapping resolver
        switch (eventData.Action?.ToLowerInvariant())
        {
            case "register":
            case "update":
                _mappingResolver.UpdateServiceMapping(eventData.ServiceName, eventData.AppId);
                break;
            case "unregister":
                _mappingResolver.RemoveServiceMapping(eventData.ServiceName);
                break;
        }

        // Dispatch to custom handlers
        await _eventDispatcher.DispatchEventAsync(eventData);
        
        return Ok(new { status = "processed", eventId = eventData.EventId });
    }
}
```

### Event Flow

1. **Service Startup**: When a service starts in production, it publishes a registration event
2. **RabbitMQ Distribution**: Event is distributed via RabbitMQ to all Bannou instances
3. **Dapr Subscription**: Dapr routes the event to the ServiceMappingEventHandler
4. **Mapping Update**: The resolver updates its internal routing table
5. **Future Requests**: All future service calls use the updated routing

### Integration with Service Clients

Service clients automatically use the mapping resolver for routing:

```csharp
public abstract class DaprServiceClientBase
{
    protected readonly IServiceAppMappingResolver _mappingResolver;

    protected async Task<T> InvokeMethodAsync<T>(string methodName, object? data = null)
    {
        var appId = _mappingResolver.GetAppIdForService(_serviceName);
        return await _daprClient.InvokeMethodAsync<object, T>(
            appId,
            methodName,
            data,
            cancellationToken);
    }
}
```

## Development Workflow Integration

### Using the Makefile Commands

The Makefile provides convenient commands for service generation:

```bash
# Generate new services from schemas (preserves existing implementations)
make generate-services

# Regenerate all services (updates clients and events)
make regenerate-all-services
```

### Manual Build Integration

```bash
# Generate services during build
cd bannou-service
dotnet build -p:GenerateNewServices=true

# Or use MSBuild directly
dotnet msbuild -t:RegenerateAllServices
```

### Development Workflow

1. **Create Schema**: Add OpenAPI schema to `/schemas/` directory
   ```yaml
   # schemas/myservice-api.yaml
   openapi: 3.0.0
   info:
     title: MyService API
     version: 1.0.0
   paths:
     /api/myservice/status:
       get:
         operationId: getStatus
         responses:
           '200':
             description: Service status
   ```

2. **Generate Scaffolding**: Run `make generate-services`
   - Creates `IMyServiceService` interface
   - Generates `MyServiceService.stub.g.cs` if doesn't exist
   - Creates client and DI extensions

3. **Implement Business Logic**: Replace stub with actual implementation
   ```csharp
   public class MyServiceService : IMyServiceService
   {
       public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
       {
           // Your implementation here
           return Ok(new { status = "healthy" });
       }
   }
   ```

4. **Register Service**: The generated extensions handle registration
   ```csharp
   // In Program.cs or service configuration
   services.AddMyServiceService();
   services.AddMyServiceClient(); // For consuming the service
   ```

### Schema-First Development Benefits

1. **Contract Consistency**: API contracts are the source of truth
2. **Type Safety**: Generated interfaces ensure type-safe implementations
3. **Client Generation**: Automatic client generation for service consumption
4. **Documentation**: OpenAPI schemas serve as API documentation
5. **Testing**: Schema-driven test generation ensures comprehensive coverage

## Implementation Examples

### Example 1: Adding a New Service

**Step 1**: Create the schema
```yaml
# schemas/inventory-api.yaml
openapi: 3.0.0
info:
  title: Inventory Service API
  version: 1.0.0
paths:
  /api/inventory/items:
    get:
      operationId: getItems
      responses:
        '200':
          description: List of items
    post:
      operationId: createItem
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/Item'
```

**Step 2**: Generate the service
```bash
make generate-services
```

**Step 3**: Implement the service
```csharp
public class InventoryService : IInventoryService
{
    private readonly ILogger<InventoryService> _logger;
    private readonly IInventoryRepository _repository;

    public async Task<IActionResult> GetItems(CancellationToken cancellationToken)
    {
        var items = await _repository.GetAllAsync(cancellationToken);
        return Ok(items);
    }

    public async Task<IActionResult> CreateItem(Item item, CancellationToken cancellationToken)
    {
        var created = await _repository.CreateAsync(item, cancellationToken);
        return CreatedAtAction(nameof(GetItems), new { id = created.Id }, created);
    }
}
```

### Example 2: Publishing Service Mapping Events

```csharp
public class ServiceRegistrationHostedService : IHostedService
{
    private readonly IServiceMappingsEventPublisher _publisher;
    private readonly string _serviceName = "inventory";
    private readonly string _appId;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Publish registration event on startup
        await _publisher.PublishEventAsync(new ServiceMappingEvent
        {
            ServiceName = _serviceName,
            AppId = _appId,
            Action = "register",
            Metadata = new Dictionary<string, object>
            {
                ["version"] = "1.0.0",
                ["environment"] = "production"
            }
        }, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Publish unregistration event on shutdown
        await _publisher.PublishEventAsync(new ServiceMappingEvent
        {
            ServiceName = _serviceName,
            AppId = _appId,
            Action = "unregister"
        }, cancellationToken);
    }
}
```

### Example 3: Custom Event Handler

```csharp
// Create a partial class to extend the generated subscriber
public partial class ServiceMappingsEventSubscriber
{
    private readonly INotificationService _notificationService;

    protected override async Task<bool> RouteEventAsync(ServiceMappingsEventBase eventData)
    {
        if (eventData is ServiceMappingEvent mappingEvent)
        {
            // Custom handling logic
            await _notificationService.NotifyServiceChangeAsync(
                mappingEvent.ServiceName,
                mappingEvent.Action);

            // Log for monitoring
            _logger.LogInformation(
                "Service {ServiceName} {Action} on {AppId}",
                mappingEvent.ServiceName,
                mappingEvent.Action,
                mappingEvent.AppId);

            return true;
        }

        return await base.RouteEventAsync(eventData);
    }
}
```

## Testing Strategies

### Testing Generated Code

The generated code is designed to be testable through standard .NET testing approaches:

#### Unit Testing Service Implementations

```csharp
[TestClass]
public class InventoryServiceTests
{
    private readonly Mock<IInventoryRepository> _repositoryMock;
    private readonly InventoryService _service;

    [TestMethod]
    public async Task GetItems_ReturnsAllItems()
    {
        // Arrange
        var expectedItems = new List<Item> { new Item { Id = 1, Name = "Test" } };
        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedItems);

        // Act
        var result = await _service.GetItems(CancellationToken.None);

        // Assert
        Assert.IsInstanceOfType(result, typeof(OkObjectResult));
        var okResult = (OkObjectResult)result;
        Assert.AreEqual(expectedItems, okResult.Value);
    }
}
```

#### Integration Testing with Dapr

```csharp
[TestClass]
public class ServiceMappingIntegrationTests
{
    private readonly ServiceAppMappingResolver _resolver;
    private readonly ServiceMappingEventHandler _handler;

    [TestMethod]
    public async Task ServiceMapping_UpdatesResolver()
    {
        // Arrange
        var mappingEvent = new ServiceMappingEvent
        {
            ServiceName = "test-service",
            AppId = "test-app-id",
            Action = "register"
        };

        // Act
        await _handler.HandleServiceMappingEventAsync(mappingEvent);

        // Assert
        var appId = _resolver.GetAppIdForService("test-service");
        Assert.AreEqual("test-app-id", appId);
    }
}
```

### Testing Service Discovery

The service mapping system includes health check endpoints for monitoring:

```bash
# Check service mapping health
curl http://localhost:5000/api/events/service-mapping/health

# Response:
{
  "status": "healthy",
  "mappingCount": 3,
  "mappings": {
    "accounts": "bannou-accounts-prod",
    "inventory": "bannou-inventory-prod",
    "character-agent": "bannou"
  }
}
```

### HTTP vs Dapr Testing

The dual testing approach ensures both transport mechanisms work correctly:

```csharp
// HTTP Tester - Direct service endpoint validation
public class HttpServiceTester : ITestClient
{
    public async Task<T> SendRequestAsync<T>(string endpoint, object? data = null)
    {
        var response = await _httpClient.PostAsJsonAsync(endpoint, data);
        return await response.Content.ReadFromJsonAsync<T>();
    }
}

// Dapr Tester - Service invocation via Dapr
public class DaprServiceTester : ITestClient
{
    public async Task<T> SendRequestAsync<T>(string endpoint, object? data = null)
    {
        var appId = _mappingResolver.GetAppIdForService(_serviceName);
        return await _daprClient.InvokeMethodAsync<object, T>(appId, endpoint, data);
    }
}
```

## Production Deployment

### Deployment Modes

The combination of source generation and service mapping enables multiple deployment modes:

#### 1. Omnipotent Monolith (Development/Small Scale)

All services run in a single "bannou" instance:
```yaml
# docker-compose.yml
services:
  bannou:
    image: bannou:latest
    environment:
      - Include_Assemblies=*
      - DEFAULT_APP_ID=bannou
    ports:
      - "5000:5000"
```

#### 2. Selective Services (Staging)

Specific services per instance:
```yaml
services:
  bannou-accounts:
    image: bannou:latest
    environment:
      - Include_Assemblies=lib-accounts-*
      - SERVICE_NAME=accounts
      - APP_ID=bannou-accounts-staging

  bannou-inventory:
    image: bannou:latest
    environment:
      - Include_Assemblies=lib-inventory-*
      - SERVICE_NAME=inventory
      - APP_ID=bannou-inventory-staging
```

#### 3. Full Microservices (Production)

Each service type on dedicated instances with auto-scaling:
```yaml
# kubernetes deployment
apiVersion: apps/v1
kind: Deployment
metadata:
  name: bannou-accounts
spec:
  replicas: 3
  template:
    spec:
      containers:
      - name: bannou
        image: bannou:latest
        env:
        - name: Include_Assemblies
          value: "lib-accounts-*"
        - name: SERVICE_NAME
          value: "accounts"
        - name: APP_ID
          valueFrom:
            fieldRef:
              fieldPath: metadata.name
```

### Service Registration Flow

1. **Startup**: Service publishes registration event with its app-id
2. **Discovery**: All instances receive the mapping update
3. **Routing**: Future requests route to the correct instance
4. **Scaling**: New instances automatically register themselves
5. **Shutdown**: Services unregister on graceful shutdown

### Monitoring and Observability

The system provides several monitoring points:

```csharp
// Structured logging for service mapping changes
_logger.LogInformation("Service mapping updated",
    new { 
        ServiceName = serviceName,
        AppId = appId,
        Action = "register",
        Timestamp = DateTime.UtcNow
    });

// Metrics for routing decisions
_metrics.RecordServiceRoute(serviceName, appId);

// Health checks for service availability
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
```

## Best Practices

### Schema Design

1. **Version Your APIs**: Include version in the schema info section
2. **Use Operation IDs**: Provide clear operationId for each endpoint
3. **Define Components**: Use components/schemas for reusable models
4. **Document Thoroughly**: Include descriptions for all operations and parameters

### Source Generation

1. **Preserve Custom Code**: Never modify generated stub files after implementation
2. **Use Partial Classes**: Extend generated classes with partial implementations
3. **Version Control**: Commit generated interfaces but not stub files
4. **Regular Regeneration**: Keep generated code in sync with schemas

### Service Discovery

1. **Graceful Registration**: Always register on startup and unregister on shutdown
2. **Include Metadata**: Add version, environment, and capability metadata
3. **Handle Failures**: Implement retry logic for event publishing
4. **Monitor Mappings**: Regularly check the health endpoint for mapping status

### Testing

1. **Test Both Transports**: Validate both HTTP and Dapr communication paths
2. **Mock External Services**: Use the mapping resolver to route to test instances
3. **Integration Tests**: Test the full flow from schema to running service
4. **Load Testing**: Validate service discovery under high load

## Troubleshooting

### Common Issues and Solutions

#### Generated Code Not Appearing

**Symptom**: Running `make generate-services` doesn't create expected files

**Solution**:
```bash
# Check if schemas are properly formatted
yamllint schemas/*.yaml

# Verify generator is referenced
grep "bannou-source-generator" bannou-service/bannou-service.csproj

# Check build output for generator diagnostics
dotnet build -v detailed | grep "BSG\|BEG"
```

#### Service Mapping Not Updating

**Symptom**: Services always route to "bannou" despite events

**Solution**:
```bash
# Check RabbitMQ connectivity
docker logs bannou-rabbitmq

# Verify Dapr pub/sub configuration
dapr list

# Check health endpoint
curl http://localhost:5000/api/events/service-mapping/health
```

#### Line Ending Issues

**Symptom**: Git shows all generated files as modified

**Solution**:
```bash
# Run the line ending fix script
./fix-generated-line-endings.sh

# Configure git attributes
echo "*.g.cs text eol=lf" >> .gitattributes
```

## Conclusion

The Roslyn source generation and service mapping events system provides a powerful foundation for Bannou's flexible deployment architecture. By combining compile-time code generation with runtime service discovery, the system achieves:

- **Development Efficiency**: Schema-first development with automatic scaffolding
- **Type Safety**: Strongly-typed interfaces and models from OpenAPI schemas
- **Deployment Flexibility**: Same codebase for monolith or microservices
- **Production Scalability**: Dynamic routing for distributed deployments
- **Maintainability**: Clear separation between generated and custom code

This architecture enables teams to start with a simple monolithic deployment and gradually transition to microservices as scaling needs evolve, all without changing the core codebase.