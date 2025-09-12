# Bannou Service Development Instructions

## Overview

This file contains specific instructions for Claude Code when working on the Bannou service platform. These instructions work in conjunction with the broader Arcadia development context from the knowledge base memory files.

## Architecture Principles

### Schema-First Development (Critical)
**Schema-first development is the foundational architectural pattern for all Bannou services. This section contains the complete standards previously documented in API-DESIGN.md.**

**⚠️ CRITICAL DEVELOPMENT PROCESS - NEVER SKIP THESE STEPS:**

1. **Schema First**: ALL service definitions start with OpenAPI YAML in `/schemas/` directory
2. **Generate Controllers**: Run `dotnet build -p:GenerateNewServices=true` to generate controllers and message types
3. **Implement Services**: Write business logic ONLY in service implementation classes
4. **Never Edit Generated**: Controllers and message classes are auto-generated - **NEVER EDIT MANUALLY**

**Complete Schema-First Generation Workflow (MANDATORY)**:
```bash
# 1. CREATE/UPDATE SCHEMA FIRST (defines ALL components)
edit schemas/service-name-api.yaml

# 2. GENERATE EVERYTHING FROM SCHEMA
./generate-all-services.sh
# This generates:
# - lib-service/Generated/ServiceController.Generated.cs (pure shell controller)
# - lib-service/Generated/IServiceService.cs (service interface)
# - lib-service/Generated/ServiceService.Generated.cs (base service implementation)
# - lib-service/Generated/ServiceConfiguration.cs (configuration class)
# - lib-service/Generated/RequestResponseModels.cs (all models)

# 3. EXTEND SERVICE IMPLEMENTATION (ONLY place for custom logic)
# Edit lib-service/ServiceService.cs to extend generated base
# Services MUST return (StatusCodes, ResponseModel?) tuples
```

**CRITICAL ARCHITECTURE RULES**:
- **Services Return Tuples**: `(StatusCodes, ResponseModel?)` using custom `StatusCodes` enum
- **Controllers Are Pure Shells**: Only call services and use `StatusCodes.ToActionResult()`
- **1:1 Controller-Service Mapping**: Every controller method maps directly to service method
- **No Manual Logic in Generated Classes**: Only service implementations can have additional logic
- **NEVER CREATE ANY CLASSES MANUALLY** - controllers, interfaces, configurations, models ALL generated from schemas
- **NEVER CREATE PROJECTS MANUALLY** - all projects (services, tests, etc.) must use code generation or templates unless explicitly instructed otherwise
- **RESEARCH ALTERNATIVES WHEN REQUESTED** - when told to find an alternative to something, research multiple options and provide context/advice, even if an alternative is suggested simultaneously
- **ASK FOR DIRECTION ON FAILURES** - when unable to accomplish a task, ask for direction instead of skipping ahead or piling additional changes on top of the failure

### WebSocket-First Architecture
- **Connect service** provides zero-copy message routing via service GUIDs
- **Binary protocol**: 24-byte header + JSON payload
- **Dual transport**: HTTP for development, WebSocket for production
- For detailed protocol specifications, see WebSocket protocol documentation in arcadia-kb technical architecture

### Service Structure (Consolidated 2025)
```
lib-{service}/                 # Single consolidated service plugin
├── I{Service}Service.cs       # Service interface
├── {Service}Service.cs        # Business logic implementation
├── {Service}Configuration.cs  # Configuration with [ServiceConfiguration] attribute
└── Data/                      # Entity Framework models (if needed)
```

## Development Workflow

### Development Environment
- **MegaLinter**: Use `docker run --rm -v $(pwd):/tmp/lint:rw oxsecurity/megalinter-dotnet:v8 -e "ENABLE=EDITORCONFIG"`
- **Line Endings**: Project uses LF (Unix) line endings consistently (.gitattributes configured)
- **EditorConfig**: Enforces consistent formatting across all files
- **NSwag**: Configured for LF line endings in generated code
- **XML Documentation**: All public classes, methods, and properties MUST have comprehensive XML documentation (`<summary>` tags minimum)

### Testing Policy
- **Lint Testing**: Use MegaLinter command above
- **Integration Testing**: `docker compose --env-file .env -f provisioning/docker-compose.yml -f provisioning/docker-compose.ci.yml up --exit-code-from=bannou-tester`
- **Post-Generation**: Run `./fix-generated-line-endings.sh` after NSwag generation
- **CI/CD Reproduction**: All GitHub Actions workflows can be reproduced locally using Docker Compose and commands in Makefile

### Commit Policy
- **NEVER commit changes** unless explicitly instructed by the user
- Always make changes and present them for review first
- The user will handle branch management and commits when ready

### Development Workflow
1. Analyze the code and make necessary changes
2. Present changes to the user for review
3. Let the user handle testing and committing when they're ready
4. Focus on code quality and correctness rather than automated validation

### 1. Service Development
1. **Read Schema First**: Always check `schemas/{service}-api.yaml`
2. **Generate Code**: Run `nswag run` to generate controllers/models
3. **Implement Logic**: Write business logic in service classes only
4. **Fix Line Endings**: Run `./fix-generated-line-endings.sh` after generation
5. **Test**: Use dual-transport testing framework

### 2. Testing Strategy
**Always use existing testing infrastructure:**

- **`http-tester/`** - Direct HTTP endpoint testing with interactive console
- **`edge-tester/`** - WebSocket protocol testing via Connect service
- **`lib-testing-core/`** - Dual-transport framework with schema-driven test generation

**Testing Commands:**
```bash
# Local testing
make test-http          # Direct HTTP endpoint tests
make test-websocket     # WebSocket protocol tests
make test-unit          # All unit tests
make test-integration   # Docker-based integration tests
make test-all           # Complete test suite

# CI/CD pipeline
make ci-test            # Matches GitHub Actions workflow
```

### 3. Code Generation Systems

Bannou uses **two complementary code generation systems** with distinct responsibilities:

#### NSwag (Primary API Generation) ✅ WORKING PERFECTLY
**Purpose**: Generate API contracts, controllers, models, and **SERVICE CLIENTS** from OpenAPI schemas  
**Input**: `schemas/*-api.yaml` files  
**Output**: ASP.NET Core controllers, request/response models, **client classes for service-to-service calls**  

```bash
# PREFERRED: Use unified generation script (generates BOTH controllers AND clients)
./generate-all-services.sh                  # Generates all 5 controllers + clients + event models

# ALTERNATIVE: Individual generation (if needed)
nswag run nswag.json                        # Main API schemas (accounts, auth, etc.)
./fix-generated-line-endings.sh            # Fix line endings for EditorConfig compliance
```

**Generated Files** (Current Status):
- ✅ `Controllers/Generated/AuthController.Generated.cs` (410 lines) - AuthControllerBase
- ✅ `Controllers/Generated/AccountsController.Generated.cs` (539 lines) - AccountsControllerBase  
- ✅ `Controllers/Generated/WebsiteController.Generated.cs` (1104 lines) - WebsiteControllerBase
- ✅ `Controllers/Generated/BehaviourController.Generated.cs` (498 lines) - BehaviourControllerBase
- ✅ `Controllers/Generated/ConnectController.Generated.cs` (759 lines) - ConnectControllerBase
- ✅ **SERVICE CLIENTS**: `lib-{service}/Generated/{Service}Client.cs` - DaprServiceClientBase clients for service-to-service calls
- ✅ `lib-accounts-core/Generated/AccountsEventsModels.cs` - Event models from accounts-events.yaml

**Critical Architecture Note**:
- **Service Clients are REQUIRED** for service-to-service communication
- **Never inject service interfaces directly** (e.g., `IAccountsService`) from other services
- **Always use generated clients** (e.g., `IAccountsClient`) for distributed service calls
- **Clients inherit from `DaprServiceClientBase`** for automatic app-id resolution

**Resolved Issues**:
- ✅ Duplicate ControllerBase conflicts fixed via unique `/ClassName` parameters
- ✅ Configuration file execution issues bypassed with direct command approach
- ✅ Event model generation working perfectly (4 event classes + enum)
- ✅ **Service client generation added to unified script** - fixes service-to-service communication architecture

**Build Integration**: Runs via MSBuild target when `GenerateNewServices=true` OR unified script

#### Roslyn Source Generators (Specialized Patterns) ✅ ARCHITECTURE OPTIMIZED
**Purpose**: Generate specialized business logic patterns that NSwag cannot handle  
**Input**: Various schema files + MSBuild properties  
**Output**: Service scaffolding, unit tests, specialized patterns  

```bash
# Generate specialized patterns (controlled by MSBuild flags)
dotnet build -p:GenerateNewServices=true    # Service scaffolding
dotnet build -p:GenerateUnitTests=true      # Unit test projects
# Event models now handled by NSwag (eliminated duplication)
```

**Generators**:
1. **EventModelGenerator**: ✅ DISABLED - NSwag handles event models perfectly, eliminated conflicts
2. **ServiceScaffoldGenerator**: ✅ WORKING - Creates service interfaces and DI registrations
3. **UnitTestGenerator**: ✅ WORKING - Creates comprehensive unit test projects

**✅ RESOLVED**: EventModelGenerator disabled to eliminate duplication with NSwag event generation. Clear division of responsibilities established.

#### Division of Responsibilities

**Use NSwag For**: 
- API controllers and routing
- Request/response models
- Client generation (C#, TypeScript)
- OpenAPI documentation
- Data validation attributes

**Use Roslyn For**:
- Event model generation and pub/sub patterns
- Service interface scaffolding beyond controllers
- Unit test project generation
- Custom DI registration patterns
- Business logic scaffolding

**❌ AVOID DUPLICATION**: 
- Never manually create models that can be generated from schemas
- Don't use both systems for the same purpose
- NSwag takes precedence for API contracts

### 4. Quality Assurance
```bash
# Format code with EditorConfig rules
dotnet format

# Run all unit tests
dotnet test

# Lint verification (EditorConfig only)
docker run --rm -v $(pwd):/tmp/lint:rw oxsecurity/megalinter-dotnet:v8 -e "ENABLE=EDITORCONFIG"
```

### 5. CI/CD Integration Testing Dependencies

**"Bannou" Omnipotent Mode**: Integration testing runs all services on a single node in "bannou" mode, requiring all dependent infrastructure services.

**Required Infrastructure Services for CI**:
- **Redis**: Required for Dapr state management and caching
- **MySQL/PostgreSQL**: Required for persistent data storage
- **RabbitMQ**: Required for Dapr pub/sub messaging
- **Dapr Sidecar**: Required for service mesh functionality

**CI Docker Compose Dependency Management**:
When adding new services to the codebase, ALWAYS update:

1. **`provisioning/docker-compose.ci.yml`**: Add any new infrastructure dependencies (databases, message queues, etc.)
2. **`.env` file**: Add required environment variables and connection strings
3. **Dapr components**: Add component configurations in `provisioning/dapr/components/ci/`
4. **Service configuration**: Ensure new services can initialize with CI environment settings

**Example New Service Integration**:
```bash
# When adding a new service that requires MongoDB:
# 1. Add MongoDB to docker-compose.ci.yml
# 2. Add MONGODB_CONNECTION_STRING to .env
# 3. Add MongoDB Dapr component configuration
# 4. Ensure service can start without manual database setup
```

**Integration Testing Philosophy**: All services must be able to start and pass basic health checks in "bannou" omnipotent mode with only infrastructure dependencies running. This ensures the CI pipeline can validate complete system integration.

## Service Implementation Guidelines

### 0. XML Documentation Requirements (MANDATORY)
**All public classes, methods, properties, and parameters MUST have comprehensive XML documentation:**

```csharp
/// <summary>
/// Brief description of what the class/method does.
/// </summary>
/// <param name="paramName">Description of the parameter.</param>
/// <returns>Description of what is returned.</returns>
/// <exception cref="ExceptionType">When this exception is thrown.</exception>
public class ExampleClass
{
    /// <summary>
    /// Gets or sets the example property description.
    /// </summary>
    public string ExampleProperty { get; set; }
    
    /// <summary>
    /// Performs the example operation with the given input.
    /// </summary>
    /// <param name="input">The input data to process.</param>
    /// <returns>The processed result.</returns>
    public string ExampleMethod(string input) => input;
}
```

**Documentation Standards:**
- **Classes**: Describe purpose and primary responsibility
- **Methods**: Describe what the method does (not how), include parameters and return values
- **Properties**: Use "Gets or sets..." pattern for mutable properties, "Gets..." for read-only
- **Parameters**: Be specific about expected values, formats, constraints
- **Exceptions**: Document all thrown exceptions with conditions
- **Return Values**: Describe what is returned and any special conditions

### 1. Service Attributes (Required)
```csharp
[DaprService("service-name", typeof(IServiceInterface), lifetime: ServiceLifetime.Scoped)]
public class ServiceNameService : DaprService<ServiceNameConfiguration>, IServiceNameService
{
    // Implementation
}
```

### 2. Configuration Pattern
```csharp
[ServiceConfiguration(typeof(ServiceNameService), envPrefix: "SERVICENAME_")]
public class ServiceNameConfiguration : IServiceConfiguration
{
    [Required]
    public string RequiredSetting { get; set; } = string.Empty;
    
    // Other settings with defaults
}
```

### 3. Database Integration (when needed)
```csharp
// Entity Framework context with soft delete
public class ServiceDbContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Global soft delete filter
        modelBuilder.Entity<EntityName>()
            .HasQueryFilter(e => e.DeletedAt == null);
    }
}
```

## Assembly Loading System

### Deployment Flexibility
Single codebase deploys in unlimited configurations:

```bash
# Development (all services)
ALL_SERVICES_ENABLED=true

# Production node examples
ACCOUNTS_SERVICE_ENABLED=true
AUTH_SERVICE_ENABLED=true
CONNECT_SERVICE_ENABLED=true
BEHAVIOR_SERVICE_ENABLED=true

# Regional distribution
OMEGA_SERVICES_ENABLED=true
ARCADIA_SERVICES_ENABLED=false
```

### Service Discovery
- **Default routing**: All services route to "bannou" (omnipotent default)
- **Production**: Event-driven service-to-app-id mapping via RabbitMQ
- **Dynamic scaling**: Services can be redistributed without code changes

## Integration with Arcadia

### Current Development Phase (from BANNOU_CORE_MEMORY.md)
- **NPC Behavior Systems**: ABML YAML DSL for character behaviors
- **Character Agents**: Lifecycle management with MySQL + Redis
- **World Simulation**: Multi-realm coordination and state management
- **Economic Systems**: Trading, resources, and crafting services

### Game System Services (Schema-First Ready)
- **Behavior Management**: `schemas/behavior-api.yaml` → ABML YAML support
- **Character Lifecycle**: Birth, aging, relationships, death across generations
- **Memory Systems**: Multi-tiered memory with relationship awareness
- **Economic Simulation**: Authentic physics-based crafting and trading

## Current Implementation Status

### ✅ Completed Services (Schema-First Migration Complete)
Based on recent commits and current build status:

- **✅ Accounts Service**: Schema-driven implementation with MySQL persistence
- **✅ Auth Service**: Complete authentication system with JWT and multi-provider support
- **✅ Behavior Service**: ABML YAML DSL foundation for character behaviors  
- **✅ Connect Service**: WebSocket-first edge gateway with binary protocol
- **✅ Website Service**: MVC integration with schema-driven APIs
- **✅ Core Infrastructure**: Assembly loading, service discovery, Dapr integration

### 🔧 Active Development Areas
- **ABML YAML Parser**: YamlDotNet integration for behavior definition language
- **Character Agent Services**: NPC lifecycle management and state persistence
- **Cross-Service Integration**: Event-driven communication via RabbitMQ
- **Performance Optimization**: Behavior compilation caching and scaling

### 🎯 Implementation Standards Established
- **Single Plugin Architecture**: One `lib-{service}` per service (consolidation complete)
- **Schema-First Generation**: All controllers/models auto-generated from OpenAPI specs
- **Tuple-Based Services**: `(StatusCodes, ResponseModel?)` pattern implemented across all services
- **Documentation**: XML documentation warnings reduced to minimal levels
- **Testing**: Dual-transport (HTTP + WebSocket) validation framework operational

## 🎯 CRITICAL: Dapr-First Development Patterns

### ⚠️ MANDATORY SERVICE IMPLEMENTATION APPROACH

**Services MUST follow Dapr-first patterns - never use Entity Framework directly**

#### **✅ Correct Dapr Patterns**:
```csharp
public class ExampleService : IExampleService
{
    private readonly DaprClient _daprClient;
    private const string STATE_STORE = "service-store";
    
    // ✅ State Management via Dapr
    await _daprClient.SaveStateAsync(STATE_STORE, key, data);
    var data = await _daprClient.GetStateAsync<ModelType>(STATE_STORE, key);
    await _daprClient.DeleteStateAsync(STATE_STORE, key);
    
    // ✅ Event Publishing via Dapr
    await _daprClient.PublishEventAsync("pubsub", "topic", eventData);
    
    // ✅ Service-to-Service calls via Dapr (automatic routing)
    var response = await _httpClient.PostAsync("/api/endpoint", content);
    // ServiceAppMappingResolver automatically routes to correct node
}
```

#### **❌ Anti-Patterns - NEVER DO THIS**:
```csharp
// ❌ WRONG: Direct Entity Framework usage
private readonly DbContext _dbContext;
await _dbContext.Entities.AddAsync(entity);
await _dbContext.SaveChangesAsync();

// ❌ WRONG: Direct SQL connections  
private readonly IDbConnection _connection;

// ❌ WRONG: Direct RabbitMQ usage
private readonly IConnection _rabbitConnection;
```

### 🏗️ Service Architecture Requirements

#### **1. Dependency Injection Pattern**:
```csharp
public class ServiceNameService : IServiceNameService
{
    private readonly DaprClient _daprClient;           // ✅ Required for all services
    private readonly ILogger<ServiceNameService> _logger;
    private readonly ServiceNameConfiguration _configuration;
    
    // Constructor injection only - no direct database/messaging dependencies
}
```

#### **2. Configuration Schema-Driven**:
- **All configuration** must be defined in OpenAPI schemas, NOT preserved manually
- **Code generation** always regenerates configuration classes from schemas
- **Environment variables** follow `SERVICENAME_PROPERTY` pattern
- **Dapr components** handle external dependencies (Redis, MySQL, RabbitMQ)

#### **3. State Management Strategy**:
```csharp
// State store naming convention
private const string STATE_STORE = "{service-name}-store";
private const string KEY_PREFIX = "{entity-type}-";

// Storage pattern
await _daprClient.SaveStateAsync(
    STATE_STORE, 
    $"{KEY_PREFIX}{entityId}", 
    entityModel);
```

#### **4. Event-Driven Communication**:
```csharp
// Publish domain events for state changes
await _daprClient.PublishEventAsync(
    "bannou-pubsub",           // Pub/sub component name
    "account-created",         // Event topic from schema
    new AccountCreatedEvent    // Event model from schema
    {
        AccountId = accountId,
        Email = email,
        Timestamp = DateTime.UtcNow
    });
```

### 🔄 Service Discovery & Request Routing

#### **Automatic Dapr Service Resolution**:
The `ServiceAppMappingResolver` automatically handles service-to-service calls:

1. **Development Mode**: All services route to "bannou" (single node)
2. **Production Mode**: Services route to appropriate distributed nodes
3. **No Code Changes**: Same service call works in both modes

#### **Service-to-Service Call Pattern**:
```csharp
// Service makes normal HTTP call - routing is automatic
var response = await _httpClient.PostAsync("/api/accounts/create", jsonContent);

// ServiceAppMappingResolver determines:
// - Is "accounts" service on this node? → Direct call
// - Is "accounts" service on another node? → Dapr invoke
// - Developer doesn't need to know the difference
```

#### **Service Loading Mechanism**:
Services are discovered via `[DaprService]` attributes:
```csharp
[DaprService("accounts", typeof(IAccountsService), lifetime: ServiceLifetime.Scoped)]
public class AccountsService : IAccountsService { }
```

The framework automatically:
- Discovers all services via reflection
- Registers them in DI container  
- Maps them to app instances via configuration
- Routes requests based on current deployment topology

### 📋 Schema-First Event Definition

#### **Event Schemas Required**:
All events must be defined in `schemas/{service}-events.yaml`:
```yaml
# schemas/accounts-events.yaml
AccountCreatedEvent:
  type: object
  properties:
    accountId:
      type: string
    email:
      type: string
    timestamp:
      type: string
      format: date-time
```

#### **Generated Event Models**:
NSwag automatically generates event classes from schemas:
```csharp
// Auto-generated from schema
public class AccountCreatedEvent
{
    public string AccountId { get; set; }
    public string Email { get; set; }
    public DateTime Timestamp { get; set; }
}
```

## Error Handling & Debugging

### Common Issues
1. **Generated file line endings**: Always run `./fix-generated-line-endings.sh` after NSwag
2. **EditorConfig violations**: Use `dotnet format` before committing
3. **Schema mismatches**: Regenerate controllers with `nswag run`
4. **Service registration**: Verify `[DaprService]` and `[ServiceConfiguration]` attributes

### Debugging Tools
- **Swagger UI**: Available at `/swagger` in development
- **Service discovery**: Connect service provides service mappings at runtime
- **Dual transport testing**: Ensures HTTP and WebSocket behavior consistency
- **Integration logs**: Docker Compose provides complete request tracing

## Git Workflow

### Branch Management
- **Never commit** unless explicitly requested by user
- **Always format** code with `dotnet format` before changes
- **Run tests** locally before suggesting commits
- **Check EditorConfig** compliance with GitHub Super-Linter

### Commit Guidelines
When user requests commits:
```bash
# Standard commit process
git add .
git commit -m "Descriptive message

🤖 Generated with [Claude Code](https://claude.ai/code)

Co-Authored-By: Claude <noreply@anthropic.com>"
```

## Advanced Features

### Code Generation Troubleshooting

#### NSwag Issues
- **Controller not generated**: Check schema file exists at expected path, verify nswag.json output path
- **Wrong working directory**: NSwag configs must be run from correct directory (bannou-service/ for most configs)
- **Line ending issues**: Always run `./fix-generated-line-endings.sh` after generation
- **Missing models**: Verify OpenAPI schema has proper `operationId` and `components/schemas` sections

#### Roslyn Generator Issues  
- **No output files**: Check MSBuild properties are set (`-p:GenerateNewServices=true`)
- **Build errors**: Roslyn generators run during build - check for compilation errors first
- **Status verification**: Check `bannou-service/obj/Debug/net9.0/` for generated .g.cs files
- **Conflicting output**: Remove manually created files that conflict with generated ones

#### When to Use Which System
**Missing API endpoints** → Use NSwag with proper schema definition  
**Missing event handling** → Use Roslyn EventModelGenerator (if working)  
**Missing service patterns** → Use Roslyn ServiceScaffoldGenerator (if working)  
**Missing validation** → Use NSwag data annotations  
**Missing client code** → Use NSwag client generation  
**Missing unit tests** → Use Roslyn UnitTestGenerator (if working)

### Roslyn Source Generators Status
- **`EventModelGenerator.cs`** ✅ DISABLED - NSwag handles event models perfectly, eliminated duplicate conflicts
- **`ServiceScaffoldGenerator.cs`** ✅ WORKING - Available for specialized service patterns NSwag cannot generate  
- **`UnitTestGenerator.cs`** ✅ WORKING - Available for comprehensive unit test generation
- **Resolution**: EventModelGenerator disabled due to conflict with NSwag event models. Other generators functional.

### Service Client Architecture (CRITICAL for Service-to-Service Communication)

**Generated Service Clients** - NSwag creates these automatically from schemas:
```csharp
// Generated by NSwag from schemas/{service}-api.yaml
public partial class AccountsClient : DaprServiceClientBase, IAccountsClient
{
    private readonly IServiceAppMappingResolver _resolver;
    private readonly DaprClient _daprClient;
    
    public async Task<CreateAccountResponse> CreateAccountAsync(CreateAccountRequest request)
    {
        var appId = _resolver.GetAppIdForService("accounts"); // Defaults to "bannou"
        return await _daprClient.InvokeMethodAsync<CreateAccountRequest, CreateAccountResponse>(
            appId, "api/accounts/create", request);
    }
}
```

**Service-to-Service Communication Pattern**:
```csharp
// ✅ CORRECT: Use generated client in AuthService
public class AuthService : IAuthService
{
    private readonly IAccountsClient _accountsClient; // Generated client injection
    
    public async Task<(StatusCodes, LoginResponse?)> LoginAsync(LoginRequest request)
    {
        // Use generated client for service-to-service calls
        var account = await _accountsClient.GetAccountByEmailAsync(request.Email);
        // Business logic continues...
    }
}

// ❌ WRONG: Never inject service interfaces directly from other services
public class AuthService : IAuthService
{
    private readonly IAccountsService _accountsService; // WRONG - direct service injection
}
```

### WebSocket Binary Protocol Integration
```csharp
// Connect service message routing
public async Task ProcessMessage(ReadOnlyMemory<byte> message)
{
    var binaryMessage = new BinaryMessage(message);
    var serviceGuid = binaryMessage.ServiceGuid;  // 16 bytes
    var messageId = binaryMessage.MessageId;      // 8 bytes  
    var payload = binaryMessage.Payload;          // Variable JSON
    
    // Route without payload inspection (zero-copy)
    await RouteToDestination(serviceGuid, message);
}
```

## Troubleshooting Reference

### Build Issues
- **NSwag errors**: Check OpenAPI schema syntax in `/schemas/`
- **Line ending issues**: Run `./fix-generated-line-endings.sh`
- **Missing dependencies**: Check project references in `.csproj` files

### Runtime Issues  
- **Service not found**: Verify `{SERVICE_NAME}_SERVICE_ENABLED=true`
- **Auth failures**: Check JWT configuration in Auth service
- **Database errors**: Verify connection strings and migrations
- **WebSocket issues**: Check Connect service configuration and binary protocol

### Testing Issues
- **HTTP tests fail**: Check service URLs in test configuration
- **WebSocket tests fail**: Verify Connect service is running
- **Integration tests fail**: Check Docker Compose service dependencies
- **Schema compliance**: Ensure generated controllers match OpenAPI specs

---

## Optimized Development Workflow (2025)

### Complete Service Implementation Process
```bash
# 1. SCHEMA-FIRST: Create/update OpenAPI specification
edit schemas/service-name-api.yaml

# 2. GENERATE: Use unified generation script (PREFERRED)
./generate-all-services.sh                 # Generates all controllers, models, event classes

# 3. IMPLEMENT: Write business logic in service classes
# Generated: Controllers/Generated/ServiceController.Generated.cs (abstract base)
# Create: lib-service/ServiceService.cs (concrete implementation)

# 4. TEST: Validate with dual-transport testing
make test-http          # Direct HTTP endpoint validation
make test-websocket     # WebSocket protocol via Connect service
make test-all           # Complete validation suite

# 5. VALIDATE: Run quality checks
dotnet format          # EditorConfig compliance
dotnet build           # Verify compilation
```

### Automated Development Commands

```bash
# Development
make build              # Build all services
make up                 # Start local development environment
make generate-services  # Generate from OpenAPI schemas (uses unified script)

# Testing (Dual Transport)
make test-http          # Direct HTTP endpoint tests
make test-websocket     # WebSocket protocol tests  
make test-integration   # Docker-based integration tests
make ci-test            # Full CI pipeline locally

# Code Quality & Generation
./generate-all-services.sh  # PREFERRED: Unified generation (bypasses config issues)
dotnet format               # Fix EditorConfig issues
dotnet test                 # Run unit tests
```
