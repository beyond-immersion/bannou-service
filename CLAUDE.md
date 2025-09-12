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

### Code Formatting Requirements
**Complete formatting workflow** (use `make format` for convenience):
1. **Line Ending Compliance**: `./fix-endings.sh` - Fixes line endings (CRLF→LF) and final newlines for ALL file types
2. **C# Code Formatting**: `dotnet format` - Handles C# syntax (spacing, indentation, braces, etc.)

**Why both tools are needed**:
- `dotnet format` handles C# syntax formatting but **cannot** fix line endings or final newlines
- `fix-endings.sh` handles line ending compliance for **all project files** (.cs, .md, .json, .yml, .sh, .xml, .csproj, etc.)
- **Always run both** before committing or use `make format` to run them in sequence

**File types handled by `fix-endings.sh`**: .cs, .md, .json, .yml/.yaml, .sh, .txt, .xml, .csproj, .sln

### Testing Policy
- **Lint Testing**: Use MegaLinter command above
- **Integration Testing**: `docker compose --env-file .env -f provisioning/docker-compose.yml -f provisioning/docker-compose.ci.yml up --exit-code-from=bannou-tester`
- **Post-Generation**: Run `./fix-endings.sh` after NSwag generation
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
4. **Fix Line Endings**: Run `./fix-endings.sh` after generation
5. **Test**: Use dual-transport testing framework

### 2. Three-Tier Testing Strategy

**Comprehensive Testing Architecture**: Bannou uses a sophisticated three-tier testing system that validates different aspects of the service architecture from infrastructure through service integration to client experience.

#### **Tier 1: Integration Testing (Infrastructure Validation)**
**Purpose**: Validates that Dapr infrastructure is functional and services can communicate in the most general sense
**Location**: `service-tests.sh` via Docker Compose CI pipeline  
**Scope**: Minimal, intentionally simple HTTP endpoint testing

**What it validates**:
- ✅ Bannou service starts successfully in "omnipotent mode"
- ✅ Dapr sidecar initializes without critical errors
- ✅ Basic HTTP endpoint accessibility (`/testing/run-enabled`)
- ✅ Database connectivity and service health

**Commands**:
```bash
# Quick integration test (matches GitHub Actions exactly)
make test-integration-v2

# Full CI pipeline with build, test, cleanup  
make ci-test-v2

# Manual Docker Compose execution
set -a && source .env && set +a && docker compose -p bannou-tests -f "./provisioning/docker-compose.yml" -f "./provisioning/docker-compose.ci.yml" up --exit-code-from=bannou-tester
```

**Architecture**: Uses Alpine Linux test container with simple curl commands to validate basic service availability.

#### **Tier 2: Service-to-Service Testing (HTTP Direct Testing)**
**Purpose**: Thoroughly tests inter-service API interactions and business logic validation  
**Location**: `http-tester/` project with comprehensive test suites  
**Scope**: Direct HTTP endpoint testing with authentication and service integration

**What it validates**:
- ✅ Authentication system (login, registration, JWT token handling)
- ✅ Account management service interactions  
- ✅ Service-to-service communication via generated C# clients
- ✅ Business logic correctness (e.g., accounts system as seen by auth system)
- ✅ API contract compliance and error handling
- ✅ Dapr service mapping and routing

**Key Features**:
- **Interactive Console**: Select and run individual tests for debugging
- **Daemon Mode**: Automated execution for CI/CD integration (`DAEMON_MODE=true`)
- **Generated Client Integration**: Uses NSwag-generated C# service clients for type-safe API calls
- **Authentication Flow**: Automatic registration/login with configurable credentials

**Test Handlers**:
- `AccountTestHandler`: Account creation, profile management, validation
- `AuthTestHandler`: Authentication flows and JWT token validation  
- `DaprServiceMappingTestHandler`: Service discovery and routing validation

**Commands**:
```bash
# Interactive HTTP testing (development)
dotnet run --project http-tester

# Automated HTTP testing (CI/CD)  
DAEMON_MODE=true dotnet run --project http-tester
```

#### **Tier 3: Client Experience Testing (WebSocket Protocol Testing)**
**Purpose**: Tests complete client experience through the WebSocket protocol via Connect service edge gateway  
**Location**: `edge-tester/` project with WebSocket-first testing  
**Scope**: Full client perspective testing including WebSocket binary protocol, authentication, and service interactions

**What it validates**:
- ✅ WebSocket connection establishment with JWT authentication
- ✅ Binary protocol message formatting and routing (24-byte header + JSON payload)
- ✅ Connect service edge gateway zero-copy message routing
- ✅ Service GUID resolution and client-to-service communication
- ✅ Message ID correlation and response handling
- ✅ Client authentication flows and session management

**Binary Protocol Architecture**:
```csharp
// Message structure: [ServiceGUID: 16 bytes][MessageID: 8 bytes][Flags: 1 byte][Payload: Variable]
public enum MessageFlags : byte
{
    None = 0,              // Default: Text/JSON to service, expecting response
    Binary = 1 << 0,       // Binary payload data
    Encrypted = 1 << 1,    // Encrypted payload  
    Compressed = 1 << 2,   // Compressed payload
    HighPriority = 1 << 3, // Skip to front of queues
    Event = 1 << 4,        // Fire-and-forget event
    Client = 1 << 5,       // Route to WebSocket client
    Response = 1 << 6      // Response to RPC
}
```

**Test Capabilities**:
- **WebSocket Authentication**: Bearer token in connection headers
- **Service Discovery**: Dynamic service GUID mapping and resolution
- **Message Correlation**: Request/response ID matching for async operations
- **Protocol Validation**: Binary message format compliance

**Commands**:
```bash
# WebSocket protocol testing (client perspective)
dotnet run --project edge-tester

# Background WebSocket testing (daemon mode)
DAEMON_MODE=true dotnet run --project edge-tester  
```

#### **Comprehensive Testing Commands**
```bash
# Development workflow (all tiers)
make test-unit              # Unit tests (C# libraries)
make test-http              # Tier 2: HTTP service testing  
make test-websocket         # Tier 3: WebSocket protocol testing
make test-integration-v2    # Tier 1: Infrastructure validation
make test-all               # Execute all testing tiers

# CI/CD pipeline (matches GitHub Actions)
make ci-test-v2             # Full integration pipeline
DAEMON_MODE=true make test-http     # Automated service testing
DAEMON_MODE=true make test-websocket # Automated client testing
```

#### **Testing Architecture Benefits**
**Progressive Validation**: Each tier builds upon the previous, ensuring comprehensive coverage:
1. **Infrastructure** → Basic service availability and health  
2. **Service Logic** → Business rules and inter-service communication
3. **Client Experience** → End-to-end user interaction patterns

**Development Efficiency**: Developers can isolate issues to specific tiers:
- Tier 1 failures → Infrastructure/deployment issues
- Tier 2 failures → Business logic or API contract issues  
- Tier 3 failures → Protocol or client integration issues

**Production Confidence**: Local execution of all three tiers provides complete confidence that changes will work in GitHub Actions CI/CD pipeline.

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
./fix-endings.sh                          # Fix line endings for EditorConfig compliance
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

### 5. Local Integration Testing Workflow (Validated Working Solution)

**Local Integration Testing Setup**: Complete workflow to run GitHub Actions integration tests locally without CI dependencies.

#### **Prerequisites**
- Docker and Docker Compose V2 installed
- `.env` file with required environment variables (see below)
- Stop any conflicting services (e.g., `docker stop conference-manager-redis`)

#### **Environment Variables Setup** 
Create/verify `.env` file in project root:
```bash
# Database
ACCOUNT_DB_USER=Franklin
ACCOUNT_DB_PASSWORD=DevPassword

# JWT Authentication
AUTH_TOKEN_PUBLIC_KEY=your-public-key
AUTH_TOKEN_PRIVATE_KEY=your-private-key

# Optional: Add any additional service-specific variables
```

#### **Docker Compose Integration Testing Commands**

**Makefile Targets (Docker Compose V2)**:
```bash
# Quick integration test (matches GitHub Actions exactly)  
make test-integration-v2

# Full CI pipeline with build, test, cleanup
make ci-test-v2

# Legacy Docker Compose V1 (may have environment variable issues)
make test-integration     # Original - may fail with variable loading
make ci-test             # Original - may fail with variable loading
```

**Manual Commands (Direct)**:
```bash
# Complete CI test pipeline (build + test + cleanup)
set -a && source .env && set +a && docker compose -p bannou-tests -f "./provisioning/docker-compose.yml" -f "./provisioning/docker-compose.ci.yml" build --pull
set -a && source .env && set +a && docker compose -p bannou-tests -f "./provisioning/docker-compose.yml" -f "./provisioning/docker-compose.ci.yml" up --exit-code-from=bannou-tester
set -a && source .env && set +a && docker compose -p bannou-tests -f "./provisioning/docker-compose.yml" -f "./provisioning/docker-compose.ci.yml" down --remove-orphans -v

# Integration test only (no rebuild)
set -a && source .env && set +a && docker compose -p bannou-tests -f "./provisioning/docker-compose.yml" -f "./provisioning/docker-compose.ci.yml" up --exit-code-from=bannou-tester
```

#### **Architecture Details**

**Service Architecture**: Integration testing uses "bannou" omnipotent mode where all services run in a single container.

**Docker Compose Stack**:
- **`bannou`**: Main service container with all APIs enabled
- **`account-db`**: MySQL 8.1.0 with healthcheck and proper authentication
- **`auth-redis`** & **`connect-redis`**: Redis instances for service caching
- **`bannou-dapr`**: Dapr sidecar (components disabled for HTTP-only testing)
- **`bannou-tester`**: Alpine Linux container that runs integration test scripts
- **`filebeat`**: Log aggregation (silenced during testing)

**Test Execution Flow**:
1. Docker builds/pulls all service images
2. MySQL starts with proper user permissions for Docker networks
3. Redis instances start for service caching
4. Bannou service starts and waits for database connectivity
5. Dapr sidecar starts (components disabled to avoid MySQL binding issues)
6. Alpine test container starts, installs curl, and runs health + integration tests
7. Test container calls `/testing/run-enabled` endpoint via HTTP
8. Exit code propagates success/failure back to Docker Compose

#### **Troubleshooting Guide**

**Common Issues and Solutions**:

1. **Environment Variable Issues**:
   ```bash
   # Error: "The ACCOUNT_DB_USER variable is not set"
   # Solution: Use Docker Compose V2 with explicit environment export
   set -a && source .env && set +a && docker compose ...
   ```

2. **Port Conflicts with Existing Redis**:
   ```bash
   # Error: "port is already allocated"
   # Solution: Stop conflicting containers
   docker stop conference-manager-redis
   docker ps  # Verify no port conflicts
   ```

3. **MySQL Authentication Failures**:
   ```bash
   # Error: "Access denied for user 'Franklin'@'172.x.x.x'"
   # Solution: Already fixed in grant-permissions.sql with Docker network patterns
   # File handles localhost, wildcard, and Docker network subnets automatically
   ```

4. **Network Connectivity in Test Container**:
   ```bash
   # Error: "Temporary failure resolving archive.ubuntu.com"
   # Solution: Already fixed - using Alpine Linux with apk package manager
   # Alpine: apk add --no-cache curl (works in Docker networks)
   ```

5. **Dapr Component Issues**:
   ```bash
   # Error: MySQL connection issues in Dapr sidecar
   # Solution: Components disabled in ci-disabled/ directory for HTTP-only testing
   # Override: bannou-dapr uses ./dapr/components/ci-disabled/ volume
   ```

#### **File Locations and Key Configurations**

**Docker Compose Override (`provisioning/docker-compose.ci.yml`)**:
- MySQL healthcheck with proper authentication parameters
- Alpine Linux test container with curl installation  
- Dapr components disabled via ci-disabled directory mount
- FileBeats logging disabled during testing

**Test Scripts**:
- **`wait-for-health.sh`**: Waits for HTTP health endpoint (not HTTPS)
- **`service-tests.sh`**: Calls integration test endpoint `/testing/run-enabled`

**MySQL Configuration (`provisioning/mysql/run-init/grant-permissions.sql`)**:
- Franklin user permissions for localhost, wildcard (%), Docker networks (172.x.x.x, 192.168.x.x)
- MySQL 8 native password authentication compatibility

**Dapr Components**:
- **Production**: `provisioning/dapr/components/` (includes MySQL binding)
- **CI Testing**: `provisioning/dapr/components/ci-disabled/` (empty directory, no MySQL binding)

#### **CI/CD Pipeline Integration**

**GitHub Actions Equivalence**: The local commands replicate the exact CI workflow:
1. ✅ Environment variable loading
2. ✅ Docker image building with `--pull` for latest base images  
3. ✅ Service orchestration with proper dependency ordering
4. ✅ Health check validation and test execution
5. ✅ Resource cleanup with `--remove-orphans -v`

**Development Workflow Integration**:
```bash
# Before committing changes, verify integration tests pass locally
make ci-test-v2

# Quick validation during development
make test-integration-v2

# If tests pass locally, they will pass in GitHub Actions
```

**Performance**: Complete local integration test cycle (build + test + cleanup) runs in ~3-5 minutes depending on machine performance.

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
1. **Generated file line endings**: Always run `./fix-endings.sh` after NSwag
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
- **Line ending issues**: Always run `./fix-endings.sh` after generation
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
- **Line ending issues**: Run `./fix-endings.sh`
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
make format            # Complete formatting (EditorConfig + C# formatting)
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
make generate-services      # PREFERRED: Unified generation (bypasses config issues)
make format                 # Complete formatting (EditorConfig + C# syntax)
make fix-editorconfig       # EditorConfig compliance only (line endings, final newlines)
dotnet format               # C# syntax formatting only (spacing, braces, etc.)
dotnet test                 # Run unit tests
```
