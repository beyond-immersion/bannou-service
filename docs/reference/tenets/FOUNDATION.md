# Foundation Tenets

> **Category**: Architecture & Design
> **When to Reference**: Before starting any new service, feature, or significant code change
> **Tenets**: T1, T2, T4, T5, T6, T13, T15, T18

These tenets define the architectural foundation of Bannou. Understanding them is prerequisite to any development work.

---

## Tenet 1: Schema-First Development (ABSOLUTE)

**Rule**: All API contracts, models, events, and configurations MUST be defined in OpenAPI YAML schemas before any code is written.

### Requirements

- Define all endpoints in `/schemas/{service}-api.yaml`
- Define all events in `/schemas/{service}-events.yaml` or `common-events.yaml`
- Use `x-permissions` to declare role/state requirements for WebSocket clients
- **ALL schema properties MUST have `description` fields** - NSwag generates XML documentation from these
- Run `make generate` to generate all code
- **NEVER** manually edit files in `*/Generated/` directories

**Important**: Scripts in `/scripts/` assume execution from the solution root directory. Always use Makefile commands rather than running scripts directly.

### Property Documentation (MANDATORY)

Every property in every schema MUST have a `description` field. NSwag converts these to XML `<summary>` tags in generated code. Missing descriptions cause CS1591 compiler warnings.

```yaml
# CORRECT: Property has description
properties:
  accountId:
    type: string
    format: uuid
    description: Unique identifier for the account

# WRONG: Missing description causes CS1591 warning
properties:
  accountId:
    type: string
    format: uuid
```

**Why This Matters**: XML documentation enables IntelliSense, auto-generated API docs, and compile-time validation that all public members are documented.

### Best Practices

- **POST-only pattern** for internal service APIs (enables zero-copy WebSocket routing)
- Path parameters allowed only for browser-facing endpoints (Website, OAuth redirects)
- Consolidate shared enums in `components/schemas` with `$ref` references

### Generated vs Manual Files

```
plugins/lib-{service}/
├── Generated/                      # NEVER EDIT - auto-generated
│   ├── I{Service}Service.cs        # Service interface
│   ├── {Service}Models.cs          # Request/response models
│   ├── {Service}Controller.cs      # HTTP controller
│   ├── {Service}ServiceConfiguration.cs  # Configuration class
│   ├── {Service}PermissionRegistration.cs
│   └── {Service}EventsController.cs     # Event subscription handlers
├── {Service}Service.cs             # MANUAL - business logic only
├── {Service}ServiceEvents.cs       # MANUAL - event handler implementations
└── Services/                       # MANUAL - optional helper services
```

### Why POST-Only?

Path parameters (e.g., `/account/{id}`) cannot map to static GUIDs for zero-copy binary WebSocket routing. All parameters move to request bodies for static endpoint signatures.

**Related**: See Tenet 15 for browser-facing endpoint exceptions (OAuth, Website, WebSocket upgrade).

### Allowed Exceptions

#### 1. Binary Format Specifications (ABML Bytecode)

The ABML Local Runtime uses a binary bytecode format for client-side behavior model execution. This format is specified in dedicated documentation rather than OpenAPI because:
- Binary formats are not HTTP APIs - OpenAPI is designed for REST/HTTP specifications
- Bytecode is a compiler output, not a request/response contract
- The specification document serves as the "schema" defining format, opcodes, and semantics

```
plugins/lib-behavior/Runtime/    # ABML bytecode interpreter (canonical location)
├── BehaviorModel.cs             # Binary format (not OpenAPI)
├── BehaviorModelInterpreter.cs  # Stack-based VM (not generated)
├── BehaviorOpcode.cs            # Opcode definitions (not generated)
└── StateSchema.cs               # Input/output schema (not generated)
```

**Key Principle**: Binary format specifications use dedicated markdown documentation as their "schema". The format spec is the contract between compiler (server) and interpreter (client).

---

## Tenet 2: Code Generation System (FOUNDATIONAL)

**Rule**: All service code is generated from schemas via a defined 9-component pipeline. Understanding what is generated vs. manual is essential.

### Generation Pipeline

Run `make generate` to execute the full pipeline in order:

| Step | Source | Generated Output |
|------|--------|------------------|
| 1. State Stores | `schemas/state-stores.yaml` | `lib-state/Generated/StateStoreDefinitions.cs` |
| 2. Lifecycle Events | `x-lifecycle` in `{service}-events.yaml` | `schemas/Generated/{service}-lifecycle-events.yaml` |
| 3. Common Events | `common-events.yaml` | `bannou-service/Generated/Events/CommonEventsModels.cs` |
| 4. Service Events | `{service}-events.yaml` | `bannou-service/Generated/Events/{Service}EventsModels.cs` |
| 5. Client Events | `{service}-client-events.yaml` | `lib-{service}/Generated/{Service}ClientEventsModels.cs` |
| 6. Service API | `{service}-api.yaml` | Controllers, models, clients, interfaces |
| 7. Configuration | `{service}-configuration.yaml` | `{Service}ServiceConfiguration.cs` |
| 8. Permissions | `x-permissions` in api.yaml | `{Service}PermissionRegistration.cs` |
| 9. Event Subscriptions | `x-event-subscriptions` in events.yaml | `{Service}EventsController.cs` + `{Service}ServiceEvents.cs` |

**Order Matters**: State stores and events must be generated before service APIs because services may reference these types.

### What Is Safe to Edit

| File Pattern | Safe to Edit? | Notes |
|--------------|---------------|-------|
| `lib-{service}/{Service}Service.cs` | Yes | Main business logic |
| `lib-{service}/Services/*.cs` | Yes | Helper services |
| `lib-{service}/{Service}ServiceEvents.cs` | Yes | Generated once, then manual |
| `lib-{service}/Generated/*.cs` | Never | Regenerated on `make generate` |
| `bannou-service/Generated/*.cs` | Never | All generated directories |
| `schemas/*.yaml` | Yes | Edit schemas, regenerate code |
| `schemas/Generated/*.yaml` | Never | Generated lifecycle events |

### Schema File Types

| File Pattern | Purpose |
|--------------|---------|
| `state-stores.yaml` | State store definitions (backend, prefix, service owner) |
| `{service}-api.yaml` | API endpoints with `x-permissions` |
| `{service}-events.yaml` | Service events with `x-lifecycle`, `x-event-subscriptions` |
| `{service}-configuration.yaml` | Service configuration with `x-service-configuration` |
| `{service}-client-events.yaml` | Server→client WebSocket push events |
| `common-events.yaml` | Shared infrastructure events |

### Configuration Environment Variable Naming (MANDATORY)

**Rule**: ALL configuration properties MUST have explicit `env:` keys following the `{SERVICE}_{PROPERTY}` pattern.

**Three Inviolable Requirements**:
1. **Explicit `env:` keys**: Every property MUST have an explicit `env:` field - never rely on auto-generation from property name
2. **Service prefix**: All env vars MUST start with the service prefix (e.g., `CONNECT_`, `AUTH_`, `STATE_`)
3. **Underscore separation**: Multi-word properties use underscores between words (e.g., `MAX_CONCURRENT_CONNECTIONS`)

```yaml
# In {service}-configuration.yaml
x-service-configuration:
  properties:
    JwtSecret:
      type: string
      env: AUTH_JWT_SECRET           # CORRECT: Explicit env with SERVICE_PROPERTY format
    MaxConcurrentConnections:
      type: integer
      env: CONNECT_MAX_CONCURRENT_CONNECTIONS  # CORRECT: Underscores between words
    Enabled:
      type: boolean
      env: BEHAVIOR_ENABLED          # CORRECT: Even simple properties need explicit env
```

**Correct Examples**:
- `AUTH_JWT_SECRET`, `AUTH_JWT_ISSUER`, `AUTH_MOCK_PROVIDERS`
- `CONNECT_MAX_CONCURRENT_CONNECTIONS`, `CONNECT_HEARTBEAT_INTERVAL_SECONDS`
- `STATE_REDIS_CONNECTION_STRING`, `STATE_DEFAULT_CONSISTENCY`
- `GAME_SESSION_MAX_PLAYERS_PER_SESSION` (hyphenated services use underscores)

**Prohibited Patterns** (cause binding failures or inconsistency):
- Missing `env:` key - Generates `MAXCONCURRENTCONNECTIONS` instead of `MAX_CONCURRENT_CONNECTIONS`
- `JWTSECRET` - No service prefix, no delimiter
- `JwtSecret` - camelCase not allowed
- `auth-jwt-secret` - kebab-case not allowed
- `AUTH_JWTSECRET` - Missing underscore delimiter in property name
- `REDIS_CONNECTION_STRING` - Missing service prefix (should be `STATE_REDIS_CONNECTION_STRING`)
- `GAME-SESSION_ENABLED` - Hyphen in prefix (should be `GAME_SESSION_ENABLED`)

### Namespace for Generated Events

All event models are generated into a single namespace:

```csharp
using BeyondImmersion.BannouService.Events;

// All event types available from all services
var acctEvent = new AccountDeletedEvent { ... };
var authEvent = new SessionInvalidatedEvent { ... };
```

### Allowed Exceptions

#### 1. Runtime Interpreters for Compiled Artifacts

The ABML behavior model interpreter is handwritten code that **consumes** generated artifacts (compiled bytecode), not **produces** them. The compiler-to-interpreter boundary is:
- **Compiler (lib-behavior)**: Generated from ABML YAML via `BehaviorCompiler` - follows schema-first
- **Interpreter (lib-behavior/Runtime)**: Handwritten VM that executes bytecode - NOT generated

```
plugins/lib-behavior/Compiler/   # Compilation (schema-first via YAML)
├── BehaviorCompiler.cs          # Orchestrates compilation pipeline
├── Actions/                     # Action-specific compilers (generated patterns)
└── Codegen/                     # Bytecode emission

plugins/lib-behavior/Runtime/    # Execution (handwritten interpreter)
├── BehaviorModelInterpreter.cs  # Stack-based VM (NOT generated)
└── CinematicInterpreter.cs      # Streaming composition (NOT generated)
```

**Key Principle**: Interpreters are analogous to the JVM or .NET CLR - they execute compiled output but are not themselves generated from schemas. The bytecode format spec serves as the interface contract.

---

## Tenet 4: Infrastructure Libs Pattern (ABSOLUTE)

**Rule**: Services MUST use the three infrastructure libs (`lib-messaging`, `lib-mesh`, `lib-state`) for all infrastructure concerns. Direct database/cache/queue access is FORBIDDEN with NO exceptions in service code.

**Infrastructure libs cannot be disabled** - they are core to the architecture and provide the abstraction layer that enables deployment flexibility. All services depend on these abstractions regardless of deployment topology.

### The Three Infrastructure Libs

| Lib | Purpose | Replaces |
|-----|---------|----------|
| **lib-state** | State management (Redis/MySQL) | Direct Redis/MySQL connections |
| **lib-messaging** | Event pub/sub (RabbitMQ) | Direct RabbitMQ channel access |
| **lib-mesh** | Service invocation (YARP) | Direct HTTP client calls |

### Usage Patterns

```csharp
// lib-state: Use IStateStore<T> for state operations
// ALWAYS use StateStoreDefinitions constants for store names (schema-first)
_stateStore = stateStoreFactory.GetStore<MyModel>(StateStoreDefinitions.MyService);
await _stateStore.SaveAsync(key, value, cancellationToken: ct);
await _stateStore.SaveAsync(key, value, new StateOptions { Ttl = TimeSpan.FromMinutes(30) }); // TTL
var (value, etag) = await _stateStore.GetWithETagAsync(key, ct); // Optimistic concurrency

// lib-messaging: Use IMessageBus for event publishing
await _messageBus.PublishAsync("entity.action", evt, cancellationToken: ct);
await _messageSubscriber.SubscribeAsync<MyEvent>("topic", async (evt, ct) => await HandleAsync(evt, ct));

// Dynamic subscription (per-session, disposable) - for WebSocket session handlers
var subscription = await _messageSubscriber.SubscribeDynamicAsync<MyEvent>(
    "session.events", async (evt, ct) => await HandleSessionEventAsync(evt, ct));
await subscription.DisposeAsync();  // Clean up when session ends

// lib-mesh: Use IMeshInvocationClient or generated clients for service calls
await _meshClient.InvokeMethodAsync<Request, Response>("account", "get-account", request, ct);
await _accountClient.GetAccountAsync(request, ct);  // Generated client (preferred)
```

**FORBIDDEN**:
```csharp
new MySqlConnection(connectionString);  // Use lib-state
ConnectionMultiplexer.Connect(...);     // Use lib-state
channel.BasicPublish(...);              // Use lib-messaging
httpClient.PostAsync("http://account/api/...");  // Use lib-mesh
```

Generated clients are auto-registered as Singletons and use mesh service resolution internally.

### Why Infrastructure Libs?

1. **Consistent Serialization**: All libs use `BannouJson` for JSON handling
2. **Unified Error Handling**: Standard exception types across all infrastructure
3. **Testability**: Interfaces enable mocking without infrastructure dependencies
4. **Portability**: Backend can change without service code changes
5. **Performance**: Optimized implementations with connection pooling and caching

### State Store Schema-First Pattern

**All state stores are defined in `schemas/state-stores.yaml`** - the single source of truth. Code generation produces:
- `plugins/lib-state/Generated/StateStoreDefinitions.cs` - Type-safe constants and configurations
- `docs/GENERATED-STATE-STORES.md` - Documentation

**ALWAYS use `StateStoreDefinitions` constants** instead of hardcoded store names:

```csharp
// CORRECT: Use generated constants
_stateStore = stateStoreFactory.GetStore<AccountModel>(StateStoreDefinitions.Account);
_cacheStore = stateStoreFactory.GetStore<SessionData>(StateStoreDefinitions.Auth);

// FORBIDDEN: Hardcoded store names
_stateStore = stateStoreFactory.GetStore<AccountModel>("account-statestore"); // NO!
```

| Backend | Purpose | Example Stores |
|---------|---------|----------------|
| Redis | Ephemeral state, caches, rankings | `auth-statestore`, `connect-statestore` |
| MySQL | Persistent queryable data | `account-statestore`, `character-statestore` |

Backend selection is handled by `IStateStoreFactory` based on configurations defined in `schemas/state-stores.yaml`.

### Allowed Exceptions

While infrastructure libs are mandatory for service code, certain specialized components have legitimate reasons for direct infrastructure access:

#### 1. SDK/Client Bundle Code (sdks/server, sdks/client)

Client and Server SDK packages that ship to external consumers may use `System.Text.Json` directly instead of `BannouJson`. This is because:
- SDKs must be self-contained without internal Bannou dependencies
- Clients need standard .NET serialization they can configure
- `BannouJson` is an internal abstraction not exposed to SDK consumers

```csharp
// In SDK packages (allowed):
var json = JsonSerializer.Serialize(request, options);

// In lib-* or bannou-service (forbidden):
var json = JsonSerializer.Serialize(request); // Use BannouJson.Serialize()
```

#### 2. MassTransit Dynamic RabbitMQ (lib-messaging internals)

`MassTransitMessageBus` uses direct RabbitMQ management API for dynamic queue/exchange creation. This is internal to lib-messaging, not service code:
- Dynamic subscriptions require runtime topology changes
- MassTransit abstracts RabbitMQ but needs management API access
- Service code still uses `IMessageBus`/`IMessageSubscriber` interfaces

#### 3. Docker.DotNet (Orchestrator Service)

The Orchestrator service uses `Docker.DotNet` for container management. This is legitimate because:
- Container orchestration IS the service's core responsibility
- No abstraction lib exists (Docker is the infrastructure being managed)
- Service manages deployment topology, not application state

```csharp
// In OrchestratorService (allowed):
using var client = new DockerClientConfiguration().CreateClient();
await client.Containers.StartContainerAsync(containerId, new());

// In any other service (forbidden - use lib-mesh for service calls)
```

#### 4. SDK Behavior Runtime (Client-Side Bytecode Execution)

The ABML Local Runtime interpreter in `lib-behavior/Runtime/` is client-side code designed for embedded execution in game clients (copied to SDKs with namespace transformation). It does NOT use lib-state, lib-messaging, or lib-mesh because:
- Runs on game clients, not Bannou servers
- Designed for offline/embedded execution without network dependencies
- State is provided by game engine, not Bannou state stores
- No pub/sub needed - intents are returned synchronously

```csharp
// In lib-behavior/Runtime/ (allowed - copied to SDKs):
public void Evaluate(ReadOnlySpan<double> inputState, Span<double> outputState)
{
    // Pure computation - no infrastructure dependencies
    // State provided by game client, outputs returned directly
}
```

**Key Principle**: Client SDK code designed for embedded execution is exempt from infrastructure lib requirements since it runs outside the Bannou service context.

**Key Principle (General)**: These exceptions are for infrastructure lib internals or specialized services where the infrastructure IS the domain. Regular service code must always use the three infrastructure libs.

---

## Tenet 5: Event-Driven Architecture (REQUIRED)

**Rule**: All meaningful state changes MUST publish events, even without current consumers.

### No Anonymous Events (ABSOLUTE)

**All events MUST be defined as typed schemas** - anonymous object publishing is FORBIDDEN for BOTH service events AND client events:

```csharp
// CORRECT: Use typed event models
await _messageBus.PublishAsync("account.created", new AccountCreatedEvent { ... });
await _clientEventPublisher.PublishToSessionAsync(sessionId, new ShortcutPublishedEvent { ... });

// FORBIDDEN: Anonymous object publishing - causes MassTransit runtime error
await _messageBus.PublishAsync("account.created", new { AccountId = id }); // NO!
await _messageBus.PublishAsync(topic, new { event_name = "...", session_id = "..." }); // NO!
```

**Critical Technical Limitation**: MassTransit (used by lib-messaging) throws `System.ArgumentException: Message types must not be anonymous types` at runtime when attempting to publish anonymous objects. This error is not caught at compile time.

**Why Typed Events Are Required**:
- **MassTransit Requirement**: MassTransit cannot serialize anonymous types for RabbitMQ transport
- Event schemas enable code generation for consumers
- Type safety catches breaking changes at compile time
- Documentation is auto-generated from schemas
- Event versioning and evolution require explicit contracts

**Event Type Locations**:
| Event Type | Schema File | Generated Output |
|------------|-------------|------------------|
| Service Events | `{service}-events.yaml` | `bannou-service/Generated/Events/{Service}EventsModels.cs` |
| Client Events | `{service}-client-events.yaml` | `lib-{service}/Generated/{Service}ClientEventsModels.cs` |
| Common Client Events | `common-client-events.yaml` | `bannou-service/Generated/CommonClientEventsModels.cs` |

### Required Events Per Service

See [Generated Events Reference](../GENERATED-EVENTS.md) for the complete, auto-maintained list of all published events.

### Event Schema Pattern

```yaml
EventName:
  type: object
  required: [eventId, timestamp, entityId]
  properties:
    eventId: { type: string, format: uuid }
    timestamp: { type: string, format: date-time }
    entityId: { type: string }
    # ... entity-specific fields
```

### Topic Naming Convention

**Pattern**: `{entity}.{action}` (kebab-case entity, lowercase action)

| Topic | Description |
|-------|-------------|
| `account.created` | Account lifecycle event |
| `account.deleted` | Account lifecycle event |
| `session.invalidated` | Session state change |
| `game-session.player-joined` | Game session event |
| `character.realm.joined` | Hierarchical action |

**Infrastructure Events**: Use `bannou-` prefix for system-level events:
- `bannou.full-service-mappings` - Service routing updates
- `bannou.service-heartbeats` - Health monitoring

### Lifecycle Events (x-lifecycle) - NEVER MANUALLY CREATE

**ABSOLUTE RULE**: CRUD-style lifecycle events (Created/Updated/Deleted) MUST be auto-generated via `x-lifecycle` in the events schema. **NEVER manually define these event patterns.**

```yaml
# In {service}-events.yaml
x-lifecycle:
  EntityName:
    model:
      entityId: { type: string, format: uuid, primary: true, required: true }
      name: { type: string, required: true }
      createdAt: { type: string, format: date-time, required: true }
    sensitive: [passwordHash, secretKey]  # Fields excluded from events
```

**Generated Output** (`schemas/Generated/{service}-lifecycle-events.yaml`):
- `EntityNameCreatedEvent` - Full entity data on creation
- `EntityNameUpdatedEvent` - Full entity data + `changedFields` array
- `EntityNameDeletedEvent` - Entity ID + `deletedReason`

**Why This Rule Exists**: Ensures consistent event structure, handles sensitive field exclusion, guarantees `changedFields` tracking on updates, and prevents copy-paste errors.

**FORBIDDEN**: Manually defining `*CreatedEvent`, `*UpdatedEvent`, `*DeletedEvent` in `components/schemas`. Use `x-lifecycle` instead.

### Full-State Events Pattern

For atomically consistent state across instances, include complete state + monotonic version:

```yaml
FullServiceMappingsEvent:
  properties:
    mappings: { type: object, additionalProperties: { type: string } }
    version: { type: integer, format: int64 }
```

**Consumer Pattern** (version-check-and-replace):
```csharp
public bool ReplaceAllMappings(IReadOnlyDictionary<string, string> mappings, long version)
{
    lock (_versionLock)
    {
        if (version <= _currentVersion) return false;  // Reject stale
        _mappings.Clear();
        foreach (var kvp in mappings) _mappings[kvp.Key] = kvp.Value;
        _currentVersion = version;
        return true;
    }
}
```

### Canonical Event Definitions (CRITICAL)

**Rule**: Each `{service}-events.yaml` file MUST contain ONLY canonical definitions for events that service PUBLISHES. No `$ref` references to other service event files are allowed.

**Why**: NSwag follows `$ref` and generates ALL types it encounters, causing duplicate type definitions that break compilation.

```yaml
# CORRECT: Canonical definitions only
components:
  schemas:
    SessionInvalidatedEvent:
      type: object
      required: [sessionIds, reason]
      properties:
        sessionIds:
          type: array
          items: { type: string }

# WRONG: $ref to another service's events
components:
  schemas:
    AccountDeletedEvent:
      $ref: './account-events.yaml#/components/schemas/AccountDeletedEvent'  # NO!
```

---

## Tenet 6: Service Implementation Pattern (STANDARDIZED)

**Rule**: All service implementations MUST follow the standardized structure.

### Partial Class Requirement (MANDATORY)

**ALL service classes MUST be declared as `partial class` from initial creation.**

```csharp
// CORRECT - Always use partial
public partial class AuthService : IAuthService

// WRONG - Will require retroactive conversion
public class AuthService : IAuthService
```

**Why Partial is Required**:
1. Event handlers MAY be implemented in separate `{Service}ServiceEvents.cs` file
2. Schema-driven event subscription generation needs partial class target
3. Separation of concerns - business logic vs. event handling
4. 15+ services required retroactive conversion when this wasn't followed

**File Structure**:
```
plugins/lib-{service}/
├── {Service}Service.cs          # Main implementation (partial class, REQUIRED)
└── {Service}ServiceEvents.cs    # Event handlers (partial class, OPTIONAL - only if service subscribes to events)
```

**ServiceEvents.cs is OPTIONAL**: The `RegisterEventConsumers()` method has a default no-op implementation
in `IEventConsumerRegistrar`. Services that don't subscribe to any events do NOT need a ServiceEvents.cs file.
Only create this file when your service needs to handle events from the message bus.

### Service Class Pattern

```csharp
[BannouService("service-name", typeof(IServiceNameService), lifetime: ServiceLifetime.Scoped)]
public partial class ServiceNameService : IServiceNameService
{
    private readonly IStateStore<ServiceModel> _stateStore;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<ServiceNameService> _logger;
    private readonly ServiceNameServiceConfiguration _configuration;
    private readonly IAuthClient _authClient;

    public ServiceNameService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        ILogger<ServiceNameService> logger,
        ServiceNameServiceConfiguration configuration,
        IEventConsumer eventConsumer,
        IAuthClient authClient)
    {
        _stateStore = stateStoreFactory.GetStore<ServiceModel>(StateStoreDefinitions.ServiceName);
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _authClient = authClient ?? throw new ArgumentNullException(nameof(authClient));

        // Register event handlers via partial class
        ArgumentNullException.ThrowIfNull(eventConsumer, nameof(eventConsumer));
        RegisterEventConsumers(eventConsumer);
    }

    public async Task<(StatusCodes, ResponseModel?)> MethodAsync(
        RequestModel body,
        CancellationToken ct = default)
    {
        // Business logic returns tuple (StatusCodes, nullable response)
        return (StatusCodes.OK, response);
    }
}
```

### Common Dependencies

**Always Available** (registered by core infrastructure):

| Dependency | Purpose |
|------------|---------|
| `IStateStoreFactory` | Create typed state stores (Redis/MySQL) |
| `IMessageBus` | Publish events to RabbitMQ (includes `TryPublishErrorAsync` for error events) |
| `IMessageSubscriber` | Subscribe to RabbitMQ topics |
| `IMeshInvocationClient` | Service-to-service invocation |
| `ILogger<T>` | Structured logging |
| `{Service}ServiceConfiguration` | Generated configuration class |
| `IEventConsumer` | Register event handlers for pub/sub fan-out |
| `I{Service}Client` | Generated service clients for inter-service calls |
| `IDistributedLockProvider` | Redis-backed distributed locking |
| `IClientEventPublisher` | Push events to WebSocket clients (via Connect service) |

### Helper Service Decomposition

For complex services, decompose business logic into helper services in a `Services/` subdirectory:

```
plugins/lib-{service}/
├── Generated/                      # NEVER EDIT
├── {Service}Service.cs             # Main service implementation
├── {Service}ServiceEvents.cs       # Event handler implementations
└── Services/                       # Optional helper services (DI-registered)
    ├── I{HelperName}Service.cs     # Interface for mockability
    └── {HelperName}Service.cs      # Implementation
```

**Lifetime Rules** (Critical for DI correctness):

| Main Service | Helper Service | Valid? |
|--------------|----------------|--------|
| Singleton | Singleton | Required |
| Singleton | Scoped | Captive dependency - will fail |
| Scoped | Singleton | OK |
| Scoped | Scoped | Recommended |

**Rule**: Helper service lifetime MUST be equal to or longer than the main service lifetime.

---

## Tenet 13: X-Permissions Usage (DOCUMENTED)

**Rule**: All endpoints MUST declare x-permissions in schema, even if empty.

### Understanding X-Permissions

- Applies to **WebSocket client connections only**
- **Does NOT restrict** service-to-service calls within the cluster
- Enforced by Connect service when routing client requests
- Endpoints without x-permissions are **not exposed** to WebSocket clients

### Role Hierarchy

Hierarchy: `anonymous` → `user` → `developer` → `admin` (higher roles include all lower roles)

**Permission Logic**: Client must have **the highest role specified** AND **all states specified**.

```yaml
# User role + must be in lobby
x-permissions:
  - role: user
    states:
      game-session: in_lobby  # Requires BOTH user role AND in_lobby state
```

### Role Selection Guide

| Role | Use When | Examples |
|------|----------|----------|
| `admin` | Destructive or sensitive operations | Orchestrator endpoints, account deletion |
| `developer` | Creating/managing resources | Character creation, realm management |
| `user` | Requires authentication | Most gameplay endpoints |
| `anonymous` | Intentionally public (rare) | Server status |

---

## Tenet 15: Browser-Facing Endpoints (DOCUMENTED)

**Rule**: Some endpoints are accessed directly by browsers through NGINX rather than through the WebSocket binary protocol. These are EXCEPTIONAL cases, not the norm.

### How Browser-Facing Endpoints Work

Browser-facing endpoints are:
- Routed through NGINX reverse proxy
- NOT included in WebSocket API (no x-permissions)
- Using GET methods and path parameters (not POST-only pattern)

**Important**: Do NOT design new endpoints as browser-facing unless they have a specific requirement that cannot be met through the WebSocket protocol. The POST-only pattern with WebSocket routing is the default.

### Current Browser-Facing Endpoints

| Service | Endpoints | Reason |
|---------|-----------|--------|
| Website | All `/website/*` | Public website, SEO, caching |
| Auth | `/auth/oauth/{provider}/init` | OAuth redirect flow |
| Auth | `/auth/oauth/{provider}/callback` | OAuth provider callback |
| Connect | `/connect` (GET) | WebSocket upgrade handshake |

These represent the complete list of browser-facing endpoints. Any additions require explicit justification.

---

## Tenet 18: Licensing Requirements (MANDATORY)

**Rule**: All dependencies MUST use permissive licenses (MIT, BSD, Apache 2.0). Copyleft licenses (GPL, LGPL, AGPL) are forbidden for linked code but acceptable for infrastructure containers.

### Acceptable Licenses

| License | Status |
|---------|--------|
| MIT | Preferred |
| BSD-2-Clause, BSD-3-Clause | Approved |
| Apache 2.0 | Approved |
| ISC, Unlicense, CC0 | Approved |

### Forbidden Licenses (for linked code)

| License | Status | Reason |
|---------|--------|--------|
| GPL v2/v3 | Forbidden | Copyleft |
| LGPL | Forbidden | Weak copyleft |
| AGPL | Forbidden | Network copyleft |

### Infrastructure Container Exception

GPL/LGPL software is acceptable when run as **separate infrastructure containers** that we communicate with via network protocols (not linked into our binaries).

**Current Infrastructure Containers**: RTPEngine (GPLv3), Kamailio (GPLv2+)

### Version Pinning for License Stability

When a package changes license, pin to the last permissive version with XML comment documentation.

---

## Quick Reference: Foundation Violations

| Violation | Tenet | Fix |
|-----------|-------|-----|
| Editing Generated/ files | T1, T2 | Edit schema, regenerate |
| Wrong env var format (`JWTSECRET`) | T2 | Use `{SERVICE}_{PROPERTY}` pattern |
| Missing `env:` key in config schema | T2 | Add explicit `env:` with proper naming |
| Missing service prefix (`REDIS_CONNECTION_STRING`) | T2 | Add prefix (e.g., `STATE_REDIS_CONNECTION_STRING`) |
| Hyphen in env var prefix (`GAME-SESSION_`) | T2 | Use underscore (`GAME_SESSION_`) |
| Direct Redis/MySQL connection | T4 | Use IStateStoreFactory via lib-state |
| Direct RabbitMQ connection | T4 | Use IMessageBus via lib-messaging |
| Direct HTTP service calls | T4 | Use IMeshInvocationClient or generated clients via lib-mesh |
| Anonymous event objects | T5 | Define typed event in schema |
| Manually defining lifecycle events | T5 | Use `x-lifecycle` in events schema |
| Service class missing `partial` | T6 | Add `partial` keyword |
| Missing x-permissions on endpoint | T13 | Add to schema (even if empty array) |
| Designing browser-facing without justification | T15 | Use POST-only WebSocket pattern |
| GPL library in NuGet package | T18 | Use MIT/BSD alternative |

---

*This document covers tenets T1, T2, T4, T5, T6, T13, T15, T18. See [TENETS.md](../TENETS.md) for the complete index.*
