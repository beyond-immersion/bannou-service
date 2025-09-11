# Bannou Service Development Instructions

## Overview

This file contains specific instructions for Claude Code when working on the Bannou service platform. These instructions work in conjunction with the broader Arcadia development context from the knowledge base memory files.

**⚠️ IMPORTANT**: Always reference the core memory files for architectural context:
- **@~/repos/arcadia-kb/ARCADIA_CORE_MEMORY.md** - Game design, world building, and development priorities
- **@~/repos/arcadia-kb/BANNOU_CORE_MEMORY.md** - Service architecture, API specifications, and technical implementation

## Architecture Principles

### Schema-First Development (Critical)
**ALWAYS reference [API-DESIGN.md](API-DESIGN.md) before making any service changes.**

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

### WebSocket-First Architecture
- **Connect service** provides zero-copy message routing via service GUIDs
- **Binary protocol**: 24-byte header + JSON payload
- **Dual transport**: HTTP for development, WebSocket for production
- See **[WEBSOCKET-PROTOCOL.md](WEBSOCKET-PROTOCOL.md)** for complete protocol specification

### Service Structure (Consolidated 2025)
```
lib-{service}/                 # Single consolidated service plugin
├── I{Service}Service.cs       # Service interface
├── {Service}Service.cs        # Business logic implementation
├── {Service}Configuration.cs  # Configuration with [ServiceConfiguration] attribute
└── Data/                      # Entity Framework models (if needed)
```

## Development Workflow

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
**Purpose**: Generate API contracts, controllers, models, and clients from OpenAPI schemas  
**Input**: `schemas/*-api.yaml` files  
**Output**: ASP.NET Core controllers, request/response models, client classes  

```bash
# PREFERRED: Use unified generation script (bypasses config file issues)
./generate-all-services.sh                  # Generates all 5 controllers + event models

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
- ✅ `lib-accounts-core/Generated/AccountsEventsModels.cs` - Event models from accounts-events.yaml

**Resolved Issues**:
- ✅ Duplicate ControllerBase conflicts fixed via unique `/ClassName` parameters
- ✅ Configuration file execution issues bypassed with direct command approach
- ✅ Event model generation working perfectly (4 event classes + enum)

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

### Service Client Architecture
```csharp
// Generated service clients with dynamic Dapr routing
public class ServiceClient : IServiceClient
{
    private readonly IServiceAppMappingResolver _resolver;
    
    public async Task<Response> CallServiceAsync(Request request)
    {
        var appId = _resolver.GetAppIdForService("service-name"); // Defaults to "bannou"
        return await _daprClient.InvokeMethodAsync<Request, Response>(appId, "method", request);
    }
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

**Remember**: Always check the core memory files for current development phase and priorities before making significant architectural decisions.