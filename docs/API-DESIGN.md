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
- Hybrid binary header + JSON payload protocol via Connect service edge gateway
- Zero-copy message routing using client-salted service GUIDs
- Same API contracts validated through WebSocket hybrid protocol
- Explicit channel multiplexing for fair scheduling and performance

### Hybrid WebSocket Protocol Architecture
**Revolutionary Design**: Binary header (31 bytes) for efficient routing + JSON payload for service data compatibility

```typescript
// Protocol structure - binary header + JSON payload
interface HybridWebSocketMessage {
  // BINARY HEADER (31 bytes total)
  messageFlags: number;     // 1 byte - message type flags
  channel: string;          // 2 bytes - explicit service channel ID
  sequence: number;         // 4 bytes - per-channel message ordering
  serviceGuid: string;      // 16 bytes - client-salted routing GUID
  messageId: bigint;        // 8 bytes - request/response correlation

  // JSON PAYLOAD (variable length)
  payload: any;             // Generated request/response models from OpenAPI
}

// Explicit channel multiplexing (replaces priority queuing)
export enum ServiceChannel {
  Authentication = 0x01,    // Auth service requests
  Accounts = 0x02,          // Account management
  Permissions = 0x03,       // Dynamic API discovery
  Chat = 0x04,              // Real-time communication
  NpcBehavior = 0x05,       // NPC behavior commands
  GameSession = 0x06,       // Session management
  WorldState = 0x07         // World state updates
}
```

**Key Technical Benefits**:
- **Zero-Copy Routing**: Connect service extracts binary header, forwards JSON unchanged to target service
- **Client-Salted Security**: Unique GUIDs per client prevent cross-client exploitation
- **TCP Head-of-Line Blocking Solution**: Explicit channels with fair round-robin scheduling
- **Schema Consistency**: JSON payload uses identical models as HTTP transport
- **Performance Optimization**: Binary routing header minimizes serialization overhead

## POST-Only API Pattern (MANDATORY for Zero-Copy Routing)

### Why POST-Only?

**The Problem**: Bannou's Connect service uses GUID-based zero-copy routing where each API endpoint maps to a unique GUID. This elegant design allows the Connect service to route WebSocket messages without decoding payloads - it simply extracts the 16-byte GUID from the binary header and forwards the message to the correct service.

**Path Parameters Break This Model**: Traditional REST patterns like `GET /accounts/{accountId}` use dynamic path segments. A single GUID cannot represent all possible values of `{accountId}`, breaking the zero-copy routing design.

**Solution**: ALL service APIs use POST-only patterns where parameters are in the request body instead of the URL path. This enables:
- **Static Endpoint Signatures**: Each endpoint has exactly one GUID
- **Zero-Copy Routing**: Connect service routes without parsing payloads
- **Consistent Client Experience**: Same endpoint behavior across HTTP and WebSocket transports

### Website Exception

**Website APIs remain REST-style** because:
- Browser-facing endpoints need bookmarkable URLs
- SEO and caching rely on URL structure
- Traditional HTTP semantics expected by web browsers
- Website service is NOT routed through Connect service WebSocket gateway

### Pattern Transformation

**Before (REST-style - WRONG for Bannou services)**:
```yaml
paths:
  /accounts:
    get:
      operationId: ListAccounts
      parameters:
        - name: page
          in: query
          schema: { type: integer }
  /accounts/{accountId}:
    get:
      operationId: GetAccount
    put:
      operationId: UpdateAccount
    delete:
      operationId: DeleteAccount
```

**After (POST-only - CORRECT for Bannou services)**:
```yaml
paths:
  /accounts/list:
    post:
      operationId: ListAccounts
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListAccountsRequest'
  /accounts/get:
    post:
      operationId: GetAccount
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetAccountRequest'
  /accounts/update:
    post:
      operationId: UpdateAccount
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/UpdateAccountRequest'
  /accounts/delete:
    post:
      operationId: DeleteAccount
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/DeleteAccountRequest'

components:
  schemas:
    GetAccountRequest:
      type: object
      required: [account_id]
      properties:
        account_id:
          type: string
          format: uuid
    ListAccountsRequest:
      type: object
      properties:
        page:
          type: integer
          default: 1
        page_size:
          type: integer
          default: 20
```

### Service Implementation Impact

**Request Bodies Contain All Parameters**:
```csharp
// Service method receives single request body
public async Task<(StatusCodes, AccountResponse?)> GetAccountAsync(
    GetAccountRequest body,
    CancellationToken cancellationToken = default)
{
    var accountId = body.AccountId;  // Extract from body, not path
    // ... implementation
}
```

**Generated Clients Use Request Bodies**:
```csharp
// Generated client call pattern
var account = await accountsClient.GetAccountAsync(
    new GetAccountRequest { AccountId = accountId }
);
```

### Version Updates (December 2025)

All service schemas were updated to POST-only pattern:
- `accounts-api.yaml`: 2.0.0 → 3.0.0
- `auth-api.yaml`: 3.0.0 → 4.0.0
- `behavior-api.yaml`: 2.0.0 → 3.0.0
- `connect-api.yaml`: 1.0.0 → 2.0.0
- `game-session-api.yaml`: 1.0.0 → 2.0.0
- `orchestrator-api.yaml`: 2.2.0 → 3.0.0
- `permissions-api.yaml`: 2.0.0 → 3.0.0

**Website APIs remain unchanged** - uses traditional REST patterns for browser compatibility.

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

#### ⚠️ CRITICAL: `servers` URL Constraint

**All service schemas MUST use `bannou` as the app-id in the `servers` URL:**

```yaml
servers:
  - url: http://localhost:3500/v1.0/invoke/bannou/method  # ✅ CORRECT
    description: Dapr sidecar endpoint (internal only)
```

**Why This Matters:**

The `servers` URL is NOT cosmetic - NSwag uses it to generate the controller's route prefix:
- Schema `servers` URL → Controller `[Route("v1.0/invoke/{app-id}/method")]` attribute
- Clients use `bannou` as the default app-id via `DaprServiceClientBase`
- Dapr preserves the full path when forwarding (does NOT strip the prefix)
- Controller route must match the path clients send

**What Happens With Wrong App-ID:**
```yaml
servers:
  - url: http://localhost:3500/v1.0/invoke/game-session/method  # ❌ WRONG
```
- Generates: `[Route("v1.0/invoke/game-session/method")]`
- Client sends: `/v1.0/invoke/bannou/method/sessions/create`
- Controller expects: `/v1.0/invoke/game-session/method/sessions/create`
- **Result: 404 Not Found**

**The Dynamic Service Mapping Exception:**
The `ServiceAppMappingResolver` can dynamically route to different app-ids at runtime (e.g., `npc-processing-omega-01`), but this affects which Dapr sidecar receives the request, not the path structure. The path still contains whatever app-id the client used, and controllers must match it.

#### Schema Template

```yaml
# schemas/service-api.yaml
openapi: 3.0.0
info:
  title: Service API
  version: 1.0.0
servers:
  - url: http://localhost:3500/v1.0/invoke/bannou/method
    description: Dapr sidecar endpoint (internal only)
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

Bannou's schema-first plugin architecture integrates seamlessly with Dapr service mesh:

**Generated Controller** (from OpenAPI schema):
```csharp
// lib-accounts/Generated/AccountsController.Generated.cs - Auto-generated
[ApiController]
[Route("accounts")]
public abstract class AccountsControllerBase : ControllerBase
{
    protected readonly IAccountsService _service;

    /// <summary>
    /// Create a new user account
    /// </summary>
    /// <param name="request">Account creation details</param>
    /// <returns>Created account information</returns>
    [HttpPost("create")]
    [ProducesResponseType(typeof(CreateAccountResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public abstract Task<ActionResult<CreateAccountResponse>> CreateAccount(
        [FromBody] CreateAccountRequest request,
        CancellationToken cancellationToken = default);
}

// lib-accounts/AccountsController.cs - Concrete implementation (never edit manually)
public class AccountsController : AccountsControllerBase
{
    public AccountsController(IAccountsService service)
    {
        _service = service;
    }

    public override async Task<ActionResult<CreateAccountResponse>> CreateAccount(
        CreateAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var (statusCode, response) = await _service.CreateAccountAsync(request, cancellationToken);
        return statusCode.ToActionResult(response);
    }
}
```

**Service Implementation** (your business logic):
```csharp
// lib-accounts/AccountsService.cs - Only manual file in plugin
[DaprService("accounts", typeof(IAccountsService), lifetime: ServiceLifetime.Scoped)]
public class AccountsService : IAccountsService
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<AccountsService> _logger;
    private readonly AccountsServiceConfiguration _configuration;

    public AccountsService(
        DaprClient daprClient,
        ILogger<AccountsService> logger,
        AccountsServiceConfiguration configuration)
    {
        _daprClient = daprClient;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<(StatusCodes, CreateAccountResponse?)> CreateAccountAsync(
        CreateAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate request using Dapr state store
            var existingAccount = await _daprClient.GetStateAsync<AccountModel>(
                "accounts-store",
                request.Username,
                cancellationToken: cancellationToken);

            if (existingAccount != null)
            {
                return (StatusCodes.Conflict, null);
            }

            // Create new account via Dapr state management
            var account = new AccountModel
            {
                Username = request.Username,
                Email = request.Email,
                CreatedAt = DateTime.UtcNow
            };

            await _daprClient.SaveStateAsync(
                "accounts-store",
                request.Username,
                account,
                cancellationToken: cancellationToken);

            // Publish account creation event
            await _daprClient.PublishEventAsync(
                "bannou-pubsub",
                "account-created",
                new AccountCreatedEvent { Username = account.Username },
                cancellationToken: cancellationToken);

            var response = new CreateAccountResponse
            {
                Id = account.Id,
                Username = account.Username,
                Email = account.Email
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create account for {Username}", request.Username);
            return (StatusCodes.InternalServerError, null);
        }
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

## Comprehensive Service Implementation Procedure

### Phase 1: Design-First Analysis (MANDATORY)

#### 1.1 Requirements Analysis for Arcadia Integration
**Before writing any code**, analyze service requirements from multiple perspectives:

**Arcadia Game Requirements**:
- What functionality does this service provide for NPCs' autonomous behavior?
- How does this service support cross-realm interactions (Omega/Arcadia/Fantasia)?
- What guardian spirit mechanics require this service?
- How will this service scale to support 100k+ NPCs?

**Production-Ready Standards**:
- Security & privacy compliance (GDPR, data protection)
- Scalability requirements (horizontal scaling via assembly loading)
- Monitoring and alerting integration
- Data consistency and backup requirements
- Cross-service communication patterns

**Development Experience**:
- Local testing capability (all tests runnable locally)
- Quick iteration times (schema-first generation)
- Clear error messages and debugging support
- Integration with existing CI/CD pipeline

#### 1.2 Event-Driven Architecture Planning
**Document ALL event types** the service will consume and publish via RabbitMQ Dapr integration:

**Events the Service Consumes**:
```yaml
# Example: Behavior Service Event Consumption
behavior_service_events:
  consumes:
    - npc_lifecycle_events:       # Character birth, death, aging
        topics: ["npc-created", "npc-died", "npc-aged"]
        source_services: ["character-service"]
        processing: "Update behavior templates based on lifecycle changes"

    - world_state_changes:        # Environmental factors affecting behavior
        topics: ["weather-changed", "time-of-day", "region-events"]
        source_services: ["world-service", "weather-service"]
        processing: "Adjust behavior priorities and available actions"

    - social_interaction_events:  # NPC-to-NPC and NPC-to-player interactions
        topics: ["conversation-started", "relationship-changed", "conflict-occurred"]
        source_services: ["social-service", "conversation-service"]
        processing: "Update relationship memories and social behavior patterns"
```

**Events the Service Publishes**:
```yaml
# Example: Behavior Service Event Publishing
behavior_service_events:
  publishes:
    - behavior_decision_made:     # When NPC makes autonomous decision
        topic: "behavior-decision"
        consumers: ["character-service", "world-service", "social-service"]
        payload: "{ npc_id, decision_type, action_taken, reasoning, confidence_score }"

    - behavior_compilation_completed: # When behavior stack is compiled
        topic: "behavior-compiled"
        consumers: ["character-service", "memory-service"]
        payload: "{ npc_id, compiled_behaviors, cultural_adaptations, expiry_time }"

    - emergency_behavior_triggered:   # When NPC enters emergency state
        topic: "npc-emergency"
        consumers: ["world-service", "quest-service"]
        payload: "{ npc_id, emergency_type, required_intervention, priority_level }"
```

#### 1.3 Datastore Architecture Planning
**Define ALL data persistence requirements** via Dapr state management:

**Redis Integration (Fast Access)**:
```yaml
# Hot data requiring sub-10ms access
redis_stores:
  behavior_cache_store:
    component_name: "behavior-cache"
    use_cases:
      - "Compiled behavior stacks (TTL: 24 hours)"
      - "Active decision trees (TTL: 1 hour)"
      - "Cultural adaptation cache (TTL: 1 week)"
    performance_targets:
      - "Read latency: <5ms p95"
      - "Write latency: <10ms p95"

  session_state_store:
    component_name: "session-state"
    use_cases:
      - "Active NPC decision context"
      - "Behavior execution state"
      - "Memory consolidation queues"
```

**MySQL Integration (Persistent Storage)**:
```yaml
# Durable data requiring ACID transactions
mysql_stores:
  behavior_definitions_store:
    component_name: "behavior-definitions"
    use_cases:
      - "ABML YAML behavior templates"
      - "Cultural behavior variations"
      - "Behavior performance analytics"
    schema_requirements:
      - "Full ACID compliance for behavior updates"
      - "Versioning support for behavior evolution"
      - "Cross-service foreign key relationships"
```

### Phase 2: Schema-First Development (Implementation)

#### 2.1 OpenAPI Schema Design
**Create comprehensive schema** in `/schemas/{service}-api.yaml`:

**Required Schema Elements**:
- Complete request/response models with validation
- Error response models for all failure scenarios
- Operation IDs matching service method names
- Security schemes (JWT, session-based)
- Event model definitions (for RabbitMQ integration)
- Configuration parameter documentation

**Event Model Integration**:
```yaml
# Include event schemas in OpenAPI specification
components:
  schemas:
    # Standard API models
    CreateBehaviorRequest: { ... }
    CreateBehaviorResponse: { ... }

    # Event models for RabbitMQ integration
    BehaviorDecisionEvent:
      type: object
      description: "Published when NPC makes autonomous decision"
      required: [npc_id, decision_type, action_taken, timestamp]
      properties:
        npc_id: { type: string, format: uuid }
        decision_type: { type: string, enum: [social, economic, survival, exploration] }
        action_taken: { type: string, description: "Human-readable action description" }
        reasoning: { type: string, description: "AI reasoning for decision" }
        confidence_score: { type: number, minimum: 0, maximum: 1 }
        timestamp: { type: string, format: date-time }
```

#### 2.2 Code Generation & Implementation
**Execute schema-first generation workflow**:

```bash
# 1. Generate all service components
./generate-all-services.sh {service-name}

# 2. Verify generation succeeded
make build

# 3. Implement business logic in {Service}Service.cs only
# - Replace NotImplementedException with actual logic
# - Follow (StatusCodes, ResponseModel?) tuple pattern
# - Integrate with Dapr for state/events

# 4. Implement unit tests in generated test project
# - Test all success scenarios
# - Test all validation failure scenarios
# - Test all error conditions
# - Achieve 90%+ code coverage

# 5. Run comprehensive testing
make test-all-v2  # Unit + Integration + WebSocket tests
```

### Phase 3: Production Integration & Testing

#### 3.1 Service Integration Testing
**Validate service works within complete ecosystem**:

**HTTP Integration Testing**:
- Direct service endpoint validation
- Inter-service communication via generated clients
- Schema compliance verification
- Performance benchmarking

**WebSocket Protocol Testing**:
- Binary protocol compliance via Connect service
- Service GUID routing validation
- Concurrent connection handling
- Message ordering and delivery guarantees

**Event Integration Testing**:
- RabbitMQ pub/sub message flow validation
- Event schema compliance
- Consumer processing validation
- Event ordering and exactly-once delivery

#### 3.2 Performance & Scalability Validation
**Ensure service meets Arcadia's scale requirements**:

**Load Testing**:
- 1000+ concurrent operations per service instance
- Sub-100ms response time for 95th percentile
- Graceful degradation under overload
- Memory and CPU usage profiling

**Scalability Testing**:
- Horizontal scaling via assembly loading system
- Cross-node event delivery validation
- Database connection pooling efficiency
- Cache hit rate optimization

### Complete Development Workflow

#### For New Services:
1. **Design Phase**: Requirements analysis + event/datastore planning (Phase 1)
2. **Schema Development**: Create comprehensive OpenAPI specification (Phase 2.1)
3. **Code Generation**: Generate all service components (Phase 2.2)
4. **Business Logic**: Implement service logic following established patterns
5. **Unit Testing**: Comprehensive test coverage in generated test project
6. **Integration Testing**: HTTP + WebSocket + Event testing (Phase 3.1)
7. **Performance Validation**: Load testing and scalability verification (Phase 3.2)
8. **Production Deployment**: Assembly loading configuration and monitoring

#### For Service Modifications:
1. **Impact Analysis**: Review event consumers and datastore dependencies
2. **Schema Updates**: Modify OpenAPI specification for changes
3. **Regeneration**: Run generation pipeline to update all components
4. **Business Logic Updates**: Modify service implementation only
5. **Test Updates**: Update unit and integration tests as needed
6. **Compatibility Testing**: Ensure backward compatibility with existing consumers
7. **Deployment**: Rolling update via assembly loading system

## Auth Service Complete Design

### Service Architecture (Refactored December 2025)

**Auth Service Helper Services**: The Auth service uses internal DI services (NOT plugins) for better maintainability:
- **ITokenService** / **TokenService**: JWT generation/validation, refresh tokens, secure token operations
- **ISessionService** / **SessionService**: Session lifecycle management, Redis state operations via Dapr (`auth-statestore`)
- **IOAuthProviderService** / **OAuthProviderService**: OAuth2 flows (Discord, Google, Twitch) + Steam Session Tickets

These are registered as scoped services in `AuthServicePlugin.cs` and injected into `AuthService.cs`.

**State Store Configuration**:
- Auth service uses `auth-statestore` Dapr component (NOT generic "statestore")
- Key prefixes: `session:`, `refresh:`, `account-sessions:`
- This prevents collisions with other services using Dapr state stores

### Service Boundaries and Responsibilities
**Auth Service Handles**: User authentication, token management, OAuth provider integration, secure routing preferences
**Auth Service Does NOT Handle**: Guardian spirit context (Game-Session service), API capabilities (Permissions service), game state management

### Complete Authentication Flows

#### Username/Password Login Flow
**Client Steps**:
1. **POST /api/auth/login/credentials** (username/password in headers)
2. Receive response: `{access_token: "jwt...", refresh_token: "refresh...", session_key: "opaque_redis_key"}`
3. **Connect via NGINX routing**: Client connects to load balancer, NGINX+Lua queries Redis for routing preferences
4. **Generic queue handling**: If capacity-limited, queue service manages access to any resource type
5. **WebSocket establishment**: Connect to assigned Connect service instance with JWT containing Redis session key
6. **WebSocket re-auth for stickiness**: Connect service updates user routing preference in Redis

**Services Background**:
- **Auth Service**: Validates credentials → Creates account (via Accounts service) → Generates JWT with opaque Redis session key → Updates routing preference in Redis
- **NGINX+Lua+Redis**: Queries `user:123:preferred_connect` → Routes to preferred Connect instance or load balances
- **Generic Queue Service**: Manages capacity for connect_websocket, game_session_join, realm_enter, etc.
- **Connect Service**: Validates JWT → Extracts Redis session key → Establishes WebSocket → Can update its own preference via auth re-auth API

**JWT Security Enhancement**: JWTs contain opaque Redis keys instead of direct sessionID exposure, preventing session hijacking and providing secure server-side session management

#### Steam Session Tickets Login Flow (NOT OAuth!)
**IMPORTANT**: Steam does NOT use OAuth or OpenID for game authentication. It uses Session Tickets via the Steamworks API.

**Client Steps (Game)**:
1. **Call** `ISteamUser::GetAuthTicketForWebApi("bannou")` in the game client
2. **Wait** for `GetAuthSessionTicketResponse_t` callback (10-20 seconds)
3. **Convert** ticket bytes to hex string: `BitConverter.ToString(ticketData).Replace("-", "")`
4. **POST /auth/steam/verify** with `{ ticket: "hex_string" }`
5. **Same routing flow** as username/password steps 2-6 (with Redis session key in JWT)

**Services Background**:
- **Auth Service**: Calls Steam Web API `ISteamUserAuth/AuthenticateUserTicket/v1/` with Publisher API key → Steam returns SteamID if valid → Links to account → Same JWT/routing process
- **Steam Web API**: `GET https://partner.steam-api.com/ISteamUserAuth/AuthenticateUserTicket/v1/?key=PUBLISHER_KEY&appid=APP_ID&ticket=HEX_TICKET&identity=bannou`
- **Security**: SteamID comes from Steam's response (never trust client-provided SteamID), Publisher API key never exposed to clients

**Reference**: See AUTH-ACCOUNTS-IMPLEMENTATION-PLAN.md in Implementation Guides for complete Steam Session Tickets flow details and provider mocking strategy for CI/CD testing.

### Infrastructure Architecture

#### NGINX+Redis Dynamic Routing Layer
**Purpose**: Secure server-side routing with service heartbeat integration and JWT Redis session keys
**Implementation**:
- NGINX with Lua script queries Redis for routing preferences and service health per request
- Enhanced load balancing based on real-time service heartbeat data and capacity metrics
- Falls back to least-loaded healthy instance if no preference or preferred instance unavailable
- High performance with Redis connection pooling and efficient heartbeat queries
- Zero sensitive information exposure in JWT payload (uses opaque Redis session keys)
- Integration with service heartbeat system for intelligent routing decisions based on actual service load and health status

#### Generic Login Queue System
**Purpose**: Capacity management for ANY resource type, not just Connect service
**Queue Types**:
- `connect_websocket`: Queue for WebSocket connections to Connect service
- `game_session_join`: Queue for joining specific game sessions
- `realm_enter`: Queue for entering Omega/Arcadia/Fantasia realms
- Custom queues: Services can register their own queue requirements

**Dual Endpoint Architecture**:

**External Client Endpoint** (Public HTTP):
```yaml
# /schemas/queue-api.yaml - Client-facing endpoints
/queue/{queue-type}/status:
  get:
    summary: Check queue status for user
    parameters:
      - name: queue-type
        in: path
        required: true
        schema:
          type: string
          enum: [connect_websocket, game_session_join, realm_enter]
    responses:
      '200':
        description: Queue status information
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/QueueStatusResponse'

/queue/{queue-type}/join:
  post:
    summary: Join queue for resource access
    security:
      - BearerAuth: []
    responses:
      '200':
        description: Successfully joined queue
      '409':
        description: Already in queue
```

**Internal Service Endpoint** (Dapr Service-to-Service):
```yaml
# Internal management endpoints - not exposed to clients
/internal/queue/{queue-type}/manage:
  post:
    summary: Manage queue capacity and processing
    description: Used by resource services to update capacity and process queue
    requestBody:
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/QueueManagementRequest'

/internal/queue/{queue-type}/capacity:
  put:
    summary: Update resource capacity
    description: Resource services report current capacity changes
    requestBody:
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/CapacityUpdateRequest'
```

**Queue Data Models**:
```csharp
public class QueueStatusResponse
{
    public string QueueType { get; set; }
    public string UserId { get; set; }
    public QueuePosition Position { get; set; }
    public TimeSpan EstimatedWaitTime { get; set; }
    public int TotalInQueue { get; set; }
    public DateTime JoinedAt { get; set; }
    public QueueStatus Status { get; set; }
}

public class QueuePosition
{
    public int Current { get; set; }
    public int Total { get; set; }
    public bool IsGranted { get; set; }
    public DateTime? GrantedAt { get; set; }
    public TimeSpan? GrantExpiry { get; set; }
}

public class QueueManagementRequest
{
    public string QueueType { get; set; }
    public QueueAction Action { get; set; }
    public int? ProcessCount { get; set; } // Number of users to grant access
    public string? UserId { get; set; }    // For specific user operations
    public Dictionary<string, object>? Context { get; set; }
}

public class CapacityUpdateRequest
{
    public string QueueType { get; set; }
    public string ServiceInstanceId { get; set; }
    public int CurrentCapacity { get; set; }
    public int MaxCapacity { get; set; }
    public double LoadThreshold { get; set; }
    public QueueCapacityStatus Status { get; set; }
}

public enum QueueStatus
{
    Waiting,
    Granted,
    Expired,
    Removed
}

public enum QueueAction
{
    ProcessNext,
    GrantAccess,
    RemoveUser,
    PauseQueue,
    ResumeQueue,
    UpdateCapacity
}

public enum QueueCapacityStatus
{
    Available,
    NearCapacity,
    AtCapacity,
    Unavailable
}
```

**Service Integration Pattern**:
```csharp
// Connect Service integration example
[DaprService("connect", typeof(IConnectService))]
public class ConnectService : BannouServiceBase, IConnectService
{
    private readonly DaprClient _daprClient;

    public async Task<ActionResult> EstablishWebSocketConnection()
    {
        // Report current capacity to queue service
        await _daprClient.InvokeMethodAsync(
            "queue",
            "internal/queue/connect_websocket/capacity",
            new CapacityUpdateRequest
            {
                QueueType = "connect_websocket",
                ServiceInstanceId = _serviceInstanceId,
                CurrentCapacity = _connectionManager.GetCurrentConnections(),
                MaxCapacity = _configuration.MaxWebSocketConnections,
                LoadThreshold = 0.85,
                Status = GetCapacityStatus()
            });

        // Process queue grants when capacity becomes available
        if (_connectionManager.HasAvailableCapacity())
        {
            await _daprClient.InvokeMethodAsync(
                "queue",
                "internal/queue/connect_websocket/manage",
                new QueueManagementRequest
                {
                    QueueType = "connect_websocket",
                    Action = QueueAction.ProcessNext,
                    ProcessCount = _connectionManager.GetAvailableSlots()
                });
        }
    }
}
```

**Client Interaction Flow**:
1. **Client attempts resource access** → Resource unavailable (at capacity)
2. **HTTP 429 Response** with queue endpoint: `{"queue_endpoint": "/queue/connect_websocket/join"}`
3. **Client joins queue** → `POST /queue/connect_websocket/join`
4. **Client polls status** → `GET /queue/connect_websocket/status` (every 5-10 seconds)
5. **Queue grants access** → `status.position.isGranted = true`
6. **Client attempts resource access again** → Success within grant window

**Benefits of Dual Architecture**:
- **Simple client integration**: Standard HTTP polling, no WebSocket complexity for queuing
- **Efficient service management**: Internal Dapr calls for capacity updates and queue processing
- **Scalable**: External polling reduces real-time connection overhead
- **Flexible**: Services can implement custom queue logic via internal endpoints
- **Secure**: Internal management endpoints not exposed to clients

**Detailed Implementation**: For comprehensive queue service implementation requirements including persistence patterns, fairness algorithms, capacity coordination, analytics, and security measures, reference the Queue Service Implementation Requirements documentation in the Implementation Guides section.

**Connect Service Integration**: For complete WebSocket protocol implementation including hybrid binary header design, client-salted security model, explicit channel multiplexing, and production deployment architecture, reference the Connect Service Implementation Guide in the Implementation Guides section.

#### Session Stickiness via WebSocket Re-auth
**Mechanism**: Connect service provides internal API for authenticated clients to update routing preference
**Flow**: Client establishes WebSocket → Connect service calls auth re-auth API → Updates `user:123:preferred_connect` in Redis
**Benefit**: Elegant stickiness without JWT payload exposure or complex session tracking

### Event Architecture
**Consumes**:
- `account-lifecycle` events from accounts-service → Update auth state, invalidate tokens for deleted accounts
- `security` events from audit services → Update policies, temporary account locks, token revocation

**Publishes**:
- `auth-success` → accounts/connect/permissions/audit services: `{user_id, session_id, auth_method, timestamp, account_roles}`
- `auth-failed` → security/audit services: `{username, ip_address, failure_reason, timestamp, attempt_count}`
- `token-refreshed` → connect/permissions services: `{user_id, session_id, new_token_expires_at}`
- `logout` → connect/permissions services: `{user_id, session_id, timestamp}`
- `oauth-account-linked` → accounts service: `{user_id, provider, external_id}`

### Datastore Architecture
**Redis (Authentication & Routing)**:
- `auth-sessions`: Active JWT tokens (24h TTL), refresh tokens (7d TTL), rate limiting counters
- `auth-cache`: Password hash cache, OAuth provider metadata, security policies
- `user-routing`: User routing preferences `user:123:preferred_connect` (7d TTL, renewable)
- Performance targets: Token validation <3ms p95, routing lookup <2ms p95

**MySQL (Persistent Auth Data)**:
- `auth-data`: Long-term refresh token hashes, authentication audit logs, OAuth provider account linking
- ACID compliance for token revocation, indexed queries for security analysis

### Security Implementation
- **JWT Security**: No sensitive routing information in JWT payload, secure validation only
- **Password Hashing**: bcrypt/Argon2 with configurable rounds
- **Network Security**: HTTPS mandatory, NGINX TLS termination
- **Rate Limiting**: Configurable thresholds per IP/account, Redis-backed counters
- **Audit Logging**: All auth events logged to MySQL with correlation IDs

### Documentation & Maintenance Requirements

**Mandatory Documentation Updates**:
- Service README with operational procedures
- Event flow diagrams showing pub/sub relationships
- Datastore schema documentation with relationships
- Performance characteristics and scaling limits
- Troubleshooting guide for common issues

**Ongoing Maintenance**:
- Event schema evolution and versioning
- Performance monitoring and optimization
- Datastore maintenance and backup validation
- Security audit and dependency updates

## Standardized Service Patterns

### Service Heartbeat & Load Reporting

**Purpose**: All Bannou services should report their current health and load statistics to Redis for dynamic service discovery, load balancing, and routing decisions.

**Architecture**: Leverages existing Dapr Redis integration to provide standardized reporting without additional infrastructure dependencies.

#### Implementation Pattern

**Redis Key Structure**:
```
service:heartbeat:{service-name}:{instance-id}
service:load:{service-name}:{instance-id}
service:capacity:{service-name}:{instance-id}
```

**Heartbeat Pattern**:
```csharp
public abstract class BannouServiceBase
{
    private readonly DaprClient _daprClient;
    private readonly Timer _heartbeatTimer;
    private readonly string _serviceInstanceId;

    protected BannouServiceBase(DaprClient daprClient, string serviceName)
    {
        _daprClient = daprClient;
        _serviceInstanceId = $"{serviceName}-{Environment.MachineName}-{Environment.ProcessId}";

        // Start heartbeat timer - report every 30 seconds
        _heartbeatTimer = new Timer(ReportHeartbeat, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
    }

    private async void ReportHeartbeat(object state)
    {
        var heartbeat = new ServiceHeartbeat
        {
            ServiceName = GetServiceName(),
            InstanceId = _serviceInstanceId,
            Timestamp = DateTimeOffset.UtcNow,
            Status = GetServiceStatus(),
            LoadMetrics = GetCurrentLoadMetrics(),
            CapacityMetrics = GetCapacityMetrics(),
            Version = GetServiceVersion()
        };

        await _daprClient.SaveStateAsync(
            "bannou-redis-store",
            $"service:heartbeat:{GetServiceName()}:{_serviceInstanceId}",
            heartbeat,
            metadata: new Dictionary<string, string> { { "ttl", "90" } } // 90 second TTL
        );
    }

    protected abstract string GetServiceName();
    protected abstract ServiceStatus GetServiceStatus();
    protected abstract LoadMetrics GetCurrentLoadMetrics();
    protected abstract CapacityMetrics GetCapacityMetrics();
    protected abstract string GetServiceVersion();
}
```

**Data Models**:
```csharp
public class ServiceHeartbeat
{
    public string ServiceName { get; set; }
    public string InstanceId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public ServiceStatus Status { get; set; }
    public LoadMetrics LoadMetrics { get; set; }
    public CapacityMetrics CapacityMetrics { get; set; }
    public string Version { get; set; }
}

public class LoadMetrics
{
    public int ActiveConnections { get; set; }
    public int ActiveRequests { get; set; }
    public double CpuUsagePercent { get; set; }
    public double MemoryUsagePercent { get; set; }
    public double RequestsPerSecond { get; set; }
    public double AvgResponseTimeMs { get; set; }
}

public class CapacityMetrics
{
    public int MaxConnections { get; set; }
    public int MaxConcurrentRequests { get; set; }
    public double ThrottleThreshold { get; set; }
    public string[] SupportedCapabilities { get; set; }
}

public enum ServiceStatus
{
    Healthy,
    Degraded,
    Unavailable,
    Maintenance
}
```

#### Service-Specific Implementations

**Connect Service Example**:
```csharp
[DaprService("connect", typeof(IConnectService))]
public class ConnectService : BannouServiceBase, IConnectService
{
    private readonly HybridWebSocketConnectionManager _connectionManager;

    protected override LoadMetrics GetCurrentLoadMetrics()
    {
        return new LoadMetrics
        {
            ActiveConnections = _connectionManager.GetConnectionCount(),
            ActiveRequests = _connectionManager.GetActiveRequestCount(),
            CpuUsagePercent = GetCpuUsage(),
            MemoryUsagePercent = GetMemoryUsage(),
            RequestsPerSecond = _connectionManager.GetRequestsPerSecond(),
            AvgResponseTimeMs = _connectionManager.GetAverageResponseTime()
        };
    }

    protected override CapacityMetrics GetCapacityMetrics()
    {
        return new CapacityMetrics
        {
            MaxConnections = _configuration.MaxWebSocketConnections,
            MaxConcurrentRequests = _configuration.MaxConcurrentRequests,
            ThrottleThreshold = 0.85, // Start throttling at 85% capacity
            SupportedCapabilities = new[] { "websocket", "hybrid-binary-protocol", "explicit-channels", "client-salted-guids", "real-time" }
        };
    }
}
```

**Auth Service Example**:
```csharp
[DaprService("auth", typeof(IAuthService))]
public class AuthService : BannouServiceBase, IAuthService
{
    protected override LoadMetrics GetCurrentLoadMetrics()
    {
        return new LoadMetrics
        {
            ActiveConnections = 0, // HTTP-only service
            ActiveRequests = _activeRequestCounter.Value,
            CpuUsagePercent = GetCpuUsage(),
            MemoryUsagePercent = GetMemoryUsage(),
            RequestsPerSecond = _metricsCollector.GetRequestsPerSecond(),
            AvgResponseTimeMs = _metricsCollector.GetAverageResponseTime()
        };
    }

    protected override CapacityMetrics GetCapacityMetrics()
    {
        return new CapacityMetrics
        {
            MaxConnections = 0, // HTTP-only
            MaxConcurrentRequests = _configuration.MaxConcurrentLogins,
            ThrottleThreshold = 0.80, // More conservative for auth
            SupportedCapabilities = new[] { "jwt", "oauth", "session-management" }
        };
    }
}
```

#### Integration with Service Discovery

**NGINX Routing Enhancement**:
```lua
-- Enhanced NGINX Lua script using heartbeat data
local redis = require "resty.redis"
local red = redis:new()

-- Get user preference
local preferred_connect = red:get("user:" .. user_id .. ":preferred_connect")

-- Get healthy Connect service instances
local heartbeat_keys = red:keys("service:heartbeat:connect:*")
local healthy_instances = {}

for _, key in ipairs(heartbeat_keys) do
    local heartbeat = red:get(key)
    if heartbeat and heartbeat.status == "Healthy" then
        local load_ratio = heartbeat.loadMetrics.activeConnections / heartbeat.capacityMetrics.maxConnections
        if load_ratio < heartbeat.capacityMetrics.throttleThreshold then
            table.insert(healthy_instances, {
                instance_id = heartbeat.instanceId,
                load_ratio = load_ratio,
                is_preferred = (heartbeat.instanceId == preferred_connect)
            })
        end
    end
end

-- Route to preferred if available and healthy, otherwise least loaded
local target_instance = select_optimal_instance(healthy_instances, preferred_connect)
```

**Generic Queue Service Integration**:
- Queue service monitors all service heartbeats to determine resource availability
- Automatically adjusts queue processing rates based on downstream service load
- Can proactively pause queues when target services report high load or degraded status

#### Benefits

**For Service Operators**:
- Real-time visibility into service health and performance across all nodes
- Automatic load balancing based on actual service metrics, not just round-robin
- Early warning system for services approaching capacity limits
- Centralized monitoring without additional infrastructure

**For NGINX+Redis Routing**:
- Route users to truly available services, not just responsive ones
- Implement intelligent load balancing based on actual capacity and current load
- Graceful degradation when services report throttling thresholds

**For Development**:
- Standardized pattern reduces boilerplate across all services
- Automatic integration with existing Dapr Redis components
- Base class inheritance provides consistent implementation
- Easy to extend with service-specific metrics

#### Production Considerations

**Redis Performance**:
- Heartbeat keys use TTL (90 seconds) to automatically clean up dead instances
- Efficient Redis operations (SET with TTL, KEYS with pattern)
- Consider Redis keyspace notifications for real-time service discovery updates

**Monitoring Integration**:
- Heartbeat data easily consumable by Prometheus/Grafana for alerting
- Service mesh observability integration via standardized metrics format
- Historical trend analysis for capacity planning

**Scalability**:
- Pattern scales to hundreds of service instances without performance impact
- Redis clustering can distribute heartbeat data across multiple nodes
- Service discovery queries can be cached and refreshed periodically

## JSON Serialization Standards (System.Text.Json)

### DaprSerializerConfig (MANDATORY)

**All Bannou services use System.Text.Json** with a standardized configuration for Dapr state management and service communication.

**Configuration Definition** (`IServiceConfiguration.cs`):
```csharp
public static readonly JsonSerializerOptions DaprSerializerConfig = new()
{
    PropertyNamingPolicy = null,               // PascalCase property names
    PropertyNameCaseInsensitive = false,       // Case-SENSITIVE matching (prevents bugs)
    NumberHandling = JsonNumberHandling.Strict, // No string-to-number coercion
    WriteIndented = false,                      // Compact JSON for efficiency
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};
```

**Why NOT Newtonsoft.Json**:
- System.Text.Json is faster and more memory-efficient
- Native .NET integration (no additional dependencies)
- Better Dapr SDK compatibility
- Simpler AOT compilation support for future optimization

**Why Case-Sensitive**:
- Prevents subtle bugs where `ExpiresAtUnix` vs `expiresAtUnix` cause silent deserialization failures
- Properties default to `0`/`null` instead of throwing, creating hard-to-debug issues
- Explicit casing catches schema mismatches during development rather than production

### Unix Timestamp Pattern for DateTimeOffset

**Problem**: `DateTimeOffset` serialization has edge cases across JSON libraries and Dapr state stores.

**Solution**: Use explicit Unix timestamp backing fields with `[JsonIgnore]` on the `DateTimeOffset` properties.

**Implementation Pattern**:
```csharp
public class SessionDataModel
{
    public string SessionId { get; set; } = "";
    public Guid AccountId { get; set; }

    // Unix timestamp backing field (what gets serialized)
    public long CreatedAtUnix { get; set; }

    // Computed property (NOT serialized)
    [JsonIgnore]
    public DateTimeOffset CreatedAt
    {
        get => DateTimeOffset.FromUnixTimeSeconds(CreatedAtUnix);
        set => CreatedAtUnix = value.ToUnixTimeSeconds();
    }

    // Same pattern for ExpiresAt, DisconnectedAt, etc.
    public long ExpiresAtUnix { get; set; }

    [JsonIgnore]
    public DateTimeOffset ExpiresAt
    {
        get => DateTimeOffset.FromUnixTimeSeconds(ExpiresAtUnix);
        set => ExpiresAtUnix = value.ToUnixTimeSeconds();
    }
}
```

**Benefits**:
- Consistent serialization across all services and languages
- No timezone confusion (Unix timestamps are always UTC)
- Smaller JSON payload than ISO 8601 strings
- Easy comparison and arithmetic on timestamps

## Dapr State Store Architecture

### Service-Specific State Stores

**Each service uses dedicated Dapr state store components** to maintain clean separation and enable independent scaling.

**State Store Configuration Pattern** (`provisioning/dapr/components/`):
```yaml
# connect-statestore.yaml - Connect service state
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: connect-statestore
spec:
  type: state.redis
  version: v1
  metadata:
  - name: redisHost
    value: redis:6379
  - name: redisPassword
    value: ""
  - name: actorStateStore
    value: "false"
```

### Key Prefix Strategy (CRITICAL)

**Dapr prefixes all keys with the app-id, NOT the component name**. This means services sharing the same `app-id` (like in monolithic deployment) will have key collisions if they use identical key prefixes.

**WRONG - Causes Collision**:
```csharp
// Auth service uses "session:" prefix
private const string SESSION_KEY_PREFIX = "session:";
// Redis key: bannou||session:{sessionKey}

// Connect service ALSO uses "session:" prefix
private const string SESSION_KEY_PREFIX = "session:";
// Redis key: bannou||session:{sessionId} - COLLISION!
```

**CORRECT - Unique Prefixes Per Service**:
```csharp
// Auth service - uses "session:" for auth sessions
private const string SESSION_KEY_PREFIX = "session:";
// Redis key: bannou||session:{sessionKey}

// Connect service - uses "ws-session:" for WebSocket state
private const string SESSION_KEY_PREFIX = "ws-session:";
private const string SESSION_MAPPINGS_KEY_PREFIX = "ws-mappings:";
private const string SESSION_HEARTBEAT_KEY_PREFIX = "heartbeat:";
private const string RECONNECTION_TOKEN_KEY_PREFIX = "reconnect:";
// Redis keys: bannou||ws-session:{sessionId}, bannou||reconnect:{token}, etc.
```

### State Store Usage Patterns

**DaprSessionManager Pattern** (Connect Service):
```csharp
public class DaprSessionManager : ISessionManager
{
    private readonly DaprClient _daprClient;
    private const string STATE_STORE = "connect-statestore";

    public async Task SetConnectionStateAsync(
        string sessionId,
        ConnectionStateData stateData,
        TimeSpan? ttl = null)
    {
        var key = SESSION_KEY_PREFIX + sessionId;
        var ttlSeconds = (int)(ttl?.TotalSeconds ?? SESSION_TTL_SECONDS);

        await _daprClient.SaveStateAsync(
            STATE_STORE,
            key,
            stateData,
            metadata: new Dictionary<string, string>
            {
                { "ttlInSeconds", ttlSeconds.ToString() }
            });
    }

    public async Task<ConnectionStateData?> GetConnectionStateAsync(string sessionId)
    {
        var key = SESSION_KEY_PREFIX + sessionId;
        return await _daprClient.GetStateAsync<ConnectionStateData>(STATE_STORE, key);
    }
}
```

### Exception: Direct Redis/Database Connections

**Orchestrator Service uses direct connections** to avoid chicken-and-egg issues:
- Orchestrator manages Dapr infrastructure (can't use Dapr to manage Dapr)
- Needs metadata unavailable through Dapr abstractions (container status, health details)
- Requires direct RabbitMQ and Redis access for infrastructure management

**All other services MUST use Dapr abstractions** for state management to:
- Maintain infrastructure flexibility (swap Redis for another store)
- Enable automatic TTL management
- Benefit from Dapr's distributed transaction support
- Simplify testing with Dapr emulation

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
