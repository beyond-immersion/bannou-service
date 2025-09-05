# Bannou API Design & Documentation Strategy

This document describes Bannou's contract-first API development approach using OpenAPI/Swagger specifications to drive the entire development lifecycle.

## Overview

Bannou uses **Schema-Driven Development** where OpenAPI specifications define the contract for each service, then code generation tools create controllers, models, tests, and client libraries automatically. This approach perfectly complements Bannou's modular `lib-*-core` / `lib-*-service` architecture.

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

### Current Bannou Pattern
```
lib-accounts-core/          # Controllers, interfaces, message models
├── AccountController.cs    # [HttpPost] endpoints with [DaprRoute]
├── IAccountService.cs      # Service interface
└── Messages/              
    ├── CreateAccountRequest.cs   # Request models with [JsonProperty]
    └── CreateAccountResponse.cs  # Response models

lib-accounts-service-mysql/ # Implementation
└── AccountService.cs       # [DaprService] implementation
```

### Enhanced with OpenAPI
```
schemas/
└── accounts-api.yaml       # OpenAPI specification (single source of truth)

lib-accounts-core/          # Generated from schema
├── AccountController.cs    # Auto-generated with validation
├── IAccountService.cs      # Auto-generated interface  
└── Messages/               # Auto-generated models
    ├── CreateAccountRequest.cs  
    └── CreateAccountResponse.cs  

lib-accounts-service-mysql/ # Hand-written business logic
└── AccountService.cs       # Implements generated interface
```

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

**Schema-First Approach (Implemented)** ✅
1. Define OpenAPI schemas in `/schemas/` directory
2. Generate all controllers/models from schemas using NSwag
3. Implement only business logic in service classes
4. Maintain schema as the single source of truth

**Implementation Status**: Successfully implemented schema-first development for all Bannou services with comprehensive automated generation:
- Controllers and models for all services (accounts, auth, connect, behaviour)
- TypeScript clients for game integration
- WebSocket protocol definitions and binary communication
- Comprehensive schema-driven test generation system

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

### Complete Service Implementation (All Services Complete)
```bash
# Schema locations
schemas/accounts-api.yaml    # Account management
schemas/auth-api.yaml        # Authentication & authorization
schemas/connect-api.yaml     # WebSocket edge gateway
schemas/behaviour-api.yaml   # AI behavior management

# Generated controllers
bannou-service/Controllers/Generated/AccountsController.Generated.cs
bannou-service/Controllers/Generated/AuthController.Generated.cs
bannou-service/Controllers/Generated/ConnectController.Generated.cs
bannou-service/Controllers/Generated/BehaviourController.Generated.cs

# Additional generated assets
lib-accounts-core/AccountsGeneratedController.cs  # Accounts core controller
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

### 3. Implement Business Logic
```csharp
// Inherit from generated abstract controller
public class ServiceController : ServiceControllerControllerBase
{
    public override async Task<ActionResult<ResponseModel>> MethodName(
        RequestModel request, CancellationToken cancellationToken)
    {
        // Implement business logic here
        // Generated controller handles routing, validation, and serialization
    }
}
```

This approach ensures API contracts remain consistent while allowing focus on business logic implementation.

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