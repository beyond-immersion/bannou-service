# Bannou API Design & Documentation Strategy

This document describes Bannou's contract-first API development approach using OpenAPI/Swagger specifications to drive the entire development lifecycle.

## Overview

Bannou uses **Schema-Driven Development** where OpenAPI specifications define the contract for each service, then code generation tools create controllers, models, tests, and client libraries automatically. This approach perfectly complements Bannou's consolidated service architecture with one plugin per service.

## Why Contract-First Development?

**Traditional Approach Problems:**
- Controllers and services get out of sync with documentation  
- Manual testing requires constant maintenance
- Client libraries become outdated
- API contracts are unclear between teams

**Contract-First Benefits:**
- **Single Source of Truth**: OpenAPI schema defines exactly what each endpoint accepts/returns
- **Automatic Validation**: Requests/responses validated against contracts automatically
- **Generated Documentation**: Interactive API docs with zero maintenance
- **Automated Testing**: Schema compliance tests run automatically  
- **Client Generation**: Auto-generate TypeScript/C# clients for games/services
- **Consistency**: All services follow identical patterns derived from schemas

## Architecture Integration

### Consolidated Service Architecture (2025)
**One Plugin Per Service** - Simplified and schema-driven:
```
schemas/
└── accounts-api.yaml       # OpenAPI specification (single source of truth)

lib-accounts/               # Single consolidated service plugin
├── Generated/              # NSwag generated from schema
│   ├── AccountController.cs    # Auto-generated controller with validation
│   └── AccountModels.cs        # Auto-generated request/response models
├── IAccountService.cs      # Service interface
├── AccountService.cs       # Business logic implementation
└── AccountServiceConfiguration.cs # Service configuration
```

### Architecture Benefits
- **Simplified Structure**: One service plugin instead of artificial core/service separation
- **Schema-First**: All controllers and models generated from OpenAPI specifications
- **Dapr Integration**: Service configuration handled via Dapr components, not separate projects
- **Clean Separation**: Generated code vs. business logic clearly separated
- **Consistent Patterns**: All services follow identical plugin structure

### Why Consolidation? (2025 Architectural Decision)

**Previous Architecture Issues:**
- **lib-*-core** / **lib-*-service** separation provided minimal architectural value
- Dual project maintenance burden without clear benefit
- Manual controller implementation conflicted with schema-first generation
- Unclear placement of new service functionality
- Extra complexity in project references and dependencies

**Consolidated Benefits:**
- **Single Source of Truth**: One schema → one service plugin → clear ownership
- **Dapr Handles Infrastructure**: Database/caching differences managed via Dapr components
- **ServiceLib.targets**: Common build patterns shared across all plugins
- **Deployment Flexibility**: Assembly loading system allows same code to deploy in different configurations
- **Testing Simplicity**: One project to test, one project to deploy
- **Schema-First Alignment**: Generated controllers replace manual implementation entirely

## Implementation Tools & Choices

### 1. Documentation & Code Generation

**NSwag** (Fully Implemented) ✅
- **Pros**: Powerful code generation, TypeScript client generation, full contract-first support
- **Cons**: More complex setup initially (resolved)
- **Implementation**: Complete schema-first development with automatic controller/model generation for all services
- **Runtime**: .NET 9 with NSwag.AspNetCore and NSwag.MSBuild packages
- **Generated Assets**: All controllers, TypeScript clients, and WebSocket protocol definitions

**Decision**: Successfully implemented NSwag for complete contract-first development across all Bannou services, with comprehensive test generation and WebSocket protocol support.

### 2. Code Generation Approach

**Schema-First Approach (Complete Generation)** ✅

**Core Design Philosophy**:
- **Services Return Tuples**: `(StatusCodes, ResponseModel?)` using custom `StatusCodes` enum 
- **Controllers Are Pure Shells**: Controllers only call services and convert tuples to ActionResults
- **1:1 Controller-Service Mapping**: Every controller method directly maps to a service method
- **No Manual Logic in Generated Classes**: Only service classes can have additional business logic beyond what's generated
- **Schema-First Everything**: Controllers, service interfaces, service implementations, and configuration ALL generated from OpenAPI schemas

**Complete Generation Workflow**:
1. Define API contract in OpenAPI YAML (`/schemas/` directory) with complete request/response models
2. Generate controllers, service interfaces, service implementations, and configuration with NSwag
3. Controllers use `StatusCodes.ToActionResult()` extension method to convert service tuples to HTTP responses
4. Add additional business logic only to service implementation classes
5. All other classes (controllers, interfaces, configurations) remain as generated

**Service Implementation Pattern**:
```csharp
// Generated from schema - can be extended with business logic
public class AccountsService : IAccountsService
{
    public async Task<(StatusCodes, AccountListResponse?)> ListAccountsAsync(...)
    {
        // Business logic here
        return (StatusCodes.OK, response);
    }
}
```

**Controller Pattern** (Generated):
```csharp
// Pure shell - never modified manually
public class AccountsController : AccountsControllerBase
{
    public override async Task<ActionResult<AccountListResponse>> ListAccounts(...)
    {
        var (statusCode, response) = await _service.ListAccountsAsync(...);
        return statusCode.ToActionResult(response);
    }
}
```

**Implementation Status**: Complete schema-first generation for all Bannou services:
- Controllers, service interfaces, service implementations, and configurations all generated
- Custom `StatusCodes` enum eliminates HTTP namespace dependencies in services  
- Tuple-based service pattern ensures consistent error handling
- Controllers are pure shells with zero manual logic
- Complete service implementation from schema in under a day

## WebSocket-First Architecture Integration

### Dual-Transport API Support
Bannou's schema-first approach seamlessly supports both transport mechanisms:

**HTTP Transport (Development/Testing)**
- Direct service endpoint access for debugging and development
- Traditional REST API patterns with JSON request/response
- Swagger UI for interactive API exploration and testing

**WebSocket Transport (Production)**  
- Binary protocol communication via Connect service edge gateway
- Zero-copy message routing using service GUIDs
- Same API contracts validated through WebSocket binary protocol

### Schema-Driven WebSocket Protocol
```typescript
// Generated from OpenAPI schemas
export interface ServiceRequestMessage {
  type: 'service_request';
  timestamp: number;
  correlation_id: string;
  service_method: string;  // "account.create", "auth.login" 
  payload: any;           // Generated request models
  expect_response: boolean;
}
```

The WebSocket protocol uses the same request/response models generated from OpenAPI schemas, ensuring perfect consistency between HTTP and WebSocket communication.

## Implementation Complete ✅

### Phase 1: Schema-First Development Foundation (Complete)
- ✅ Installed NSwag.AspNetCore and NSwag.MSBuild packages
- ✅ Converted project from .NET 10 to .NET 9 for tooling compatibility  
- ✅ Created comprehensive OpenAPI schemas for all services
- ✅ Configured automated controller generation from schemas
- ✅ Generated abstract controllers with full validation and documentation

### Complete Service Implementation (Clean Slate 2025)
```bash
# Schema locations (authoritative)
schemas/accounts-api.yaml    # Account management
schemas/auth-api-v3.yaml     # Authentication & authorization v3
schemas/connect-api.yaml     # WebSocket edge gateway with dynamic permissions
schemas/behavior-api.yaml    # ABML behavior management v2.0

# Service plugins (consolidated architecture)
lib-behavior/                # Single behavior service plugin
│                           # (replaces lib-behaviour-core + lib-behaviour-service)

# Schema-driven generation ready for:
# - Controllers generated from schemas via NSwag
# - Models and validation generated automatically  
# - Unit test projects via Roslyn source generator
```

**Generated Features:**
- Abstract controller classes with proper ASP.NET Core routing
- Full request/response models with validation attributes
- Support for multiple authentication methods (OAuth, username/password)
- Cancellation token support for async operations
- ActionResult<T> return types for proper HTTP response handling
- Complete XML documentation for all endpoints and models

### Phase 2: Client Generation & Testing (Complete)
- ✅ Extended schema-first approach to all Bannou services (auth, connect, behaviour)
- ✅ Generated TypeScript clients for web/game integration
- ✅ Implemented comprehensive schema-driven test generation
- ✅ Created dual-transport testing system (HTTP + WebSocket)
- ✅ Built WebSocket binary protocol implementation

### Generated TypeScript Clients
```bash
clients/typescript/AccountsClient.ts   # Account management client
clients/typescript/AuthClient.ts       # Authentication client  
clients/typescript/ConnectClient.ts    # WebSocket connection client
clients/typescript/BehaviourClient.ts  # AI behavior client
clients/typescript/BannouClient.ts     # WebSocket protocol client
clients/typescript/WebSocketProtocol.ts # Protocol definitions
```

## Schema-First Development Workflow

### 1. Define API Contract
```yaml
# schemas/service-api.yaml
openapi: 3.0.0
info:
  title: Service API
  version: 1.0.0
paths:
  /endpoint:
    post:
      operationId: MethodName
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/RequestModel'
```

### 2. Generate Controllers
```bash
# From bannou-service directory
export DOTNET_ROOT=$HOME/.dotnet
export PATH="$DOTNET_ROOT:$PATH"
nswag openapi2cscontroller \
  /input:../schemas/service-api.yaml \
  /namespace:BeyondImmersion.BannouService.Controllers.Generated \
  /output:Controllers/Generated/ServiceController.Generated.cs
```

### 3. Implement Service Logic
```csharp
// Single service plugin with generated controllers
namespace BeyondImmersion.BannouService.ServiceName;

public class ServiceNameService : IServiceNameService
{
    private readonly ILogger<ServiceNameService> _logger;

    public ServiceNameService(ILogger<ServiceNameService> logger)
    {
        _logger = logger;
    }

    // Implement business logic methods defined in schema
    // Generated controllers handle routing, validation, and serialization
    public async Task<ActionResult<ResponseModel>> MethodName(
        RequestModel request, CancellationToken cancellationToken)
    {
        // Pure business logic - no controller concerns
        _logger.LogDebug("Processing {Method}", nameof(MethodName));
        
        // Your implementation here
        return new ResponseModel { /* ... */ };
    }
}
```

This consolidated approach ensures API contracts remain consistent while maintaining clean separation between generated framework code and business logic.

### Phase 3: WebSocket Protocol & Testing (Complete)
- ✅ Implemented WebSocket-first architecture with Connect service edge gateway
- ✅ Created binary protocol with service GUID routing (zero-copy message routing)
- ✅ Built comprehensive schema-driven test generation system
- ✅ Implemented dual-transport testing (HTTP + WebSocket validation)
- ✅ Created production-ready WebSocket client SDK with TypeScript definitions
- ✅ Added automatic test generation for success, validation, and authorization scenarios

## Example: Accounts Service Schema

```yaml
# schemas/accounts-api.yaml
openapi: 3.0.0
info:
  title: Bannou Accounts API
  version: 1.0.0
  description: User account management for Bannou services

paths:
  /api/accounts/create:
    post:
      summary: Create new user account
      operationId: CreateAccount
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CreateAccountRequest'
      responses:
        '200':
          description: Account created successfully
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CreateAccountResponse'
        '400':
          description: Invalid request data
        '409':
          description: Account already exists

components:
  schemas:
    CreateAccountRequest:
      type: object
      required:
        - username
      properties:
        username:
          type: string
          minLength: 3
          maxLength: 32
          pattern: '^[a-zA-Z0-9_]+$'
        password:
          type: string
          minLength: 8
          format: password
        email:
          type: string
          format: email
        steam_id:
          type: string
          description: Steam OAuth user ID
        # ... additional properties
```

This schema would generate:
- Validated controller methods
- Request/response models with proper attributes
- Client libraries for games
- Interactive documentation
- Automated test cases

## Integration with Dapr

Bannou's Dapr integration works perfectly with OpenAPI:

```csharp
[ApiController]
[Route("api/accounts")]
[DaprController(typeof(IAccountService))]
public class AccountController : BaseDaprController
{
    /// <summary>
    /// Create a new user account
    /// </summary>
    /// <param name="request">Account creation details</param>
    /// <returns>Created account information</returns>
    [HttpPost("create")]
    [DaprRoute("create")]
    [ProducesResponseType(typeof(CreateAccountResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> CreateAccount([FromBody] CreateAccountRequest request)
    {
        // Dapr service call with automatic validation
        var result = await Service.CreateAccount(/* ... */);
        return Ok(result);
    }
}
```

The OpenAPI attributes provide:
- **Automatic Documentation**: Method summaries, parameter descriptions
- **Response Type Documentation**: Clear return types for each status code  
- **Request Validation**: Automatic model validation against schema
- **Client Generation**: TypeScript/C# clients know exact method signatures

## Benefits for Arcadia Integration

**Game Client Generation:**
```typescript
// Auto-generated from OpenAPI schema
export interface CreateAccountRequest {
  username: string;
  password?: string;
  email?: string;
  steam_id?: string;
}

export class BannouAccountsClient {
  async createAccount(request: CreateAccountRequest): Promise<CreateAccountResponse> {
    // Generated HTTP client code
  }
}
```

**Automated Testing:**
```csharp
// Generated test cases from OpenAPI schema
[Test]
public async Task CreateAccount_ValidRequest_ReturnsAccount()
{
    var request = new CreateAccountRequest 
    { 
        Username = "testuser",
        Email = "test@example.com"
    };
    
    // Automatically validates against schema
    var response = await _client.CreateAccount(request);
    
    // Schema compliance assertions
    Assert.That(response.Id, Is.GreaterThan(0));
    Assert.That(response.Username, Is.EqualTo(request.Username));
}
```

## Development Workflow

### For New Services:
1. **Design Schema First**: Define OpenAPI specification in `/schemas/`
2. **Generate Code**: Create controllers/models from schema
3. **Implement Logic**: Write business logic in service classes
4. **Auto-Test**: Run schema compliance tests
5. **Generate Clients**: Create game integration libraries

### For Existing Services:
1. **Document Current**: Add Swagger to existing controllers  
2. **Extract Schema**: Generate OpenAPI spec from running service
3. **Enhance Schema**: Add validation rules, examples, descriptions
4. **Migrate Gradually**: Move to schema-first for updates

## Configuration & Setup

### Package Requirements
```xml
<PackageReference Include="Swashbuckle.AspNetCore" Version="6.5.0" />
<PackageReference Include="Swashbuckle.AspNetCore.Annotations" Version="6.5.0" />
```

### Basic Configuration
```csharp
// Program.cs
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { 
        Title = "Bannou API", 
        Version = "v1",
        Description = "Modular microservice APIs for Bannou platform"
    });
    
    // Include XML documentation
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    c.IncludeXmlComments(xmlPath);
});

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Bannou API v1");
        c.RoutePrefix = "swagger"; // Available at /swagger
    });
}
```

## Next Steps

**Immediate Implementation:**
1. Install Swashbuckle.AspNetCore in bannou-service
2. Configure Swagger UI for development
3. Add XML documentation generation
4. Test with existing AccountController

**Future Enhancements:**
- Define comprehensive OpenAPI schemas
- Implement code generation pipeline  
- Generate TypeScript clients for Arcadia
- Add automated contract testing
- Extend to all Bannou services

This approach will make Bannou incredibly maintainable and will scale perfectly as you add the hundreds of services needed for Arcadia's NPC simulation systems. Each service gets defined once in a schema, then everything else generates automatically!