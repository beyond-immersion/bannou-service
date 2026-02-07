# Foundation Tenets

> **Category**: Architecture & Design
> **When to Reference**: Before starting any new service, feature, or significant code change
> **Tenets**: T4, T5, T6, T13, T15, T18

These tenets define the architectural foundation of Bannou. Understanding them is prerequisite to any development work.

> **Note**: Schema-related rules (formerly T1, T2) are now consolidated in [SCHEMA-RULES.md](../SCHEMA-RULES.md) and referenced by Tenet 1 in [TENETS.md](../TENETS.md).

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

// lib-state: Use ICacheableStateStore<T> for sets and sorted sets (Redis + InMemory only)
var cacheStore = stateStoreFactory.GetCacheableStore<MyModel>(StateStoreDefinitions.MyCache);
await cacheStore.AddToSetAsync("my-set", item, ct);  // Set operations
await cacheStore.SortedSetAddAsync("leaderboard", memberId, score, ct);  // Sorted set for rankings
var topTen = await cacheStore.SortedSetRangeByRankAsync("leaderboard", 0, 9, descending: true, ct);

// lib-state: Use IRedisOperations for atomic operations (Lua scripts, counters, hashes)
var redisOps = stateStoreFactory.GetRedisOperations();  // Returns null in InMemory mode
if (redisOps != null)
{
    // Lua scripts for complex atomic operations
    var result = await redisOps.ScriptEvaluateAsync(script, keys, values, ct);
    // Atomic counters
    var count = await redisOps.IncrementAsync("counter:visits", 1, ct);
    // Hash operations for structured data
    await redisOps.HashSetAsync("user:123", "lastSeen", DateTimeOffset.UtcNow.ToString(), ct);
}

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

### Dependency Hardness by Layer (ABSOLUTE)

**Rule**: Dependencies on L0/L1/L2 services MUST be hard (fail at startup if missing). Dependencies on L3/L4 MAY be soft (graceful degradation). See [SERVICE-HIERARCHY.md § Dependency Handling Patterns](../SERVICE-HIERARCHY.md#dependency-handling-patterns-mandatory) for the full pattern.

The SERVICE_HIERARCHY guarantees that L0/L1/L2 services are running when higher layers are enabled. Silent degradation for these guaranteed dependencies hides deployment configuration errors.

```csharp
// IDEAL: Constructor injection for L0/L1/L2 dependencies
public LocationService(IContractClient contractClient, ...)  // Fails at startup if missing
{
    _contractClient = contractClient;
}

// CURRENT WORKAROUND: For cross-plugin init (until layer-based loading is implemented)
// Use OnRunningAsync with THROW on null - never silent degradation
var contractClient = serviceProvider.GetService<IContractClient>()
    ?? throw new InvalidOperationException("IContractClient required");  // THROW, don't skip

// FORBIDDEN: Silent degradation for guaranteed service
if (contractClient == null) { return; }  // NO! Hides deployment errors

// CORRECT: Soft dependency on L4 service (truly optional)
var analyticsClient = serviceProvider.GetService<IAnalyticsClient>();
if (analyticsClient == null) { /* graceful degradation OK */ }  // L4 may not be enabled
```

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

### State Store Interface Hierarchy

lib-state provides specialized interfaces for different capabilities:

| Interface | Backends | Purpose |
|-----------|----------|---------|
| `IStateStore<T>` | All (Redis, MySQL, InMemory) | Core CRUD, bulk operations, ETags |
| `ICacheableStateStore<T>` | Redis, InMemory | Sets and Sorted Sets for caching/rankings |
| `IQueryableStateStore<T>` | MySQL only | LINQ expression queries with pagination |
| `IJsonQueryableStateStore<T>` | MySQL only | JSON path queries within stored documents |
| `ISearchableStateStore<T>` | Redis+Search only | Full-text search with FT.* commands |
| `IRedisOperations` | Redis only | Lua scripts, atomic counters, hashes, TTL |

**Factory Methods**:
```csharp
// Core operations (all backends)
var store = factory.GetStore<T>(storeName);

// Set/Sorted Set operations (Redis + InMemory - throws for MySQL)
var cacheStore = factory.GetCacheableStore<T>(storeName);

// LINQ queries (MySQL - throws for Redis)
var queryStore = factory.GetQueryableStore<T>(storeName);

// JSON path queries (MySQL - throws for Redis)
var jsonStore = factory.GetJsonQueryableStore<T>(storeName);

// Full-text search (Redis+Search - throws if search not enabled)
var searchStore = factory.GetSearchableStore<T>(storeName);

// Low-level Redis access (returns null if not using Redis backend)
var redisOps = factory.GetRedisOperations();
```

**When to Use IRedisOperations**:
- Lua scripts for atomic multi-key operations
- Atomic counters (`INCR`/`DECR`)
- Hash operations (`HGET`/`HSET`/`HINCRBY`)
- TTL manipulation (`EXPIRE`/`TTL`/`PERSIST`)
- Cross-store atomic operations (keys from different stores in one Lua script)

**Important**: Keys passed to `IRedisOperations` are NOT prefixed - they are raw Redis keys. This enables cross-store atomic operations but requires you to manage key prefixes manually.

### Lua Script Requirements (STRICT)

**Rule**: Lua scripts via `IRedisOperations.ScriptEvaluateAsync()` are a **last resort**. Always prefer built-in interface methods first.

**Before Writing a Lua Script, Verify**:
1. `ICacheableStateStore<T>` methods (sets, sorted sets) don't solve the problem
2. `IRedisOperations` atomic counters (`IncrementAsync`/`DecrementAsync`) are insufficient
3. `IRedisOperations` hash operations don't meet the need
4. `IDistributedLockProvider` + separate operations won't work (often acceptable for cleanup flows)

Lua scripts should only be used when **atomicity across multiple distinct operations is genuinely required** and the above alternatives are insufficient.

**Absolute Restrictions**:

| Restriction | Reason |
|-------------|--------|
| **FORBIDDEN: Loops/iteration in scripts** | Lua scripts block Redis (single-threaded). Loops scale O(N) and cause latency spikes affecting ALL clients. |
| **FORBIDDEN: Inline script strings** | Scripts MUST be in `.lua` files under `Scripts/`, loaded via a `{Plugin}LuaScripts.cs` helper, embedded as resources. |
| **FORBIDDEN: Large dataset processing** | Scripts cannot be killed once they perform a write. Long-running scripts make Redis unresponsive. |
| **FORBIDDEN: Blocking commands** | `BLPOP`, `BRPOP`, etc. cannot be used in Lua scripts. |

**Required Pattern** (see lib-mesh and lib-state for examples):

```
plugins/lib-{service}/
├── Scripts/
│   └── MyOperation.lua          # Embedded resource
├── Services/
│   └── {Service}LuaScripts.cs   # Static loader class
└── lib-{service}.csproj         # <EmbeddedResource Include="Scripts\*.lua" />
```

**Loader Class Pattern**:
```csharp
public static class MyServiceLuaScripts
{
    public static string MyOperation => GetScript("MyOperation");

    private static string GetScript(string name)
    {
        return _scriptCache.GetOrAdd(name, n =>
        {
            var resourceName = $"BeyondImmersion.BannouService.MyService.Scripts.{n}.lua";
            using var stream = typeof(MyServiceLuaScripts).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Lua script '{n}' not found");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        });
    }

    private static readonly ConcurrentDictionary<string, string> _scriptCache = new();
}
```

**Script Best Practices**:
- Use `redis.pcall()` instead of `redis.call()` to handle errors gracefully
- Keep scripts minimal - do the atomic part only, handle results in C#
- Parameterize all scripts (use KEYS[] and ARGV[]) - unparameterized scripts waste Redis memory via script cache
- Test scripts in pre-production with realistic data volumes

**Why These Restrictions Matter**:
- Redis is single-threaded - a slow script blocks ALL operations cluster-wide
- Once a script performs any write, it cannot be terminated (`SCRIPT KILL` fails)
- Memory allocated by Lua isn't controlled by Redis `maxmemory` - scripts can OOM the server
- Each unique script text is cached in Redis memory forever (until `SCRIPT FLUSH`)

### Infrastructure Lib Backend Access

Each infrastructure lib accesses its specific backend directly - this is their purpose:

| Infrastructure Lib | Direct Backend Access |
|-------------------|----------------------|
| lib-state | Redis, MySQL |
| lib-messaging | RabbitMQ |
| lib-orchestrator (Orchestrator service) | Docker, Portainer, Kubernetes APIs |
| lib-voice | RTPEngine, Kamailio |
| lib-asset | MinIO |

**Key Principle**: Infrastructure libs exist to abstract these backends. Service code uses the infrastructure lib interfaces, never the backends directly.

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

**Why Typed Events Are Required**:
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

**Infrastructure Events**: Use `bannou.` prefix for system-level events:
- `bannou.full-service-mappings` - Service routing updates
- `bannou.service-heartbeat` - Health monitoring

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
        // NRT-protected parameters: no null checks needed - compiler enforces non-null at call sites
        _stateStore = stateStoreFactory.GetStore<ServiceModel>(StateStoreDefinitions.ServiceName);
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        _authClient = authClient;

        // Register event handlers via partial class
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

### Why POST-Only is the Default

Bannou uses POST-only APIs (not RESTful GET/PUT/DELETE with path parameters) because of the WebSocket binary protocol architecture:

1. **Static endpoint signatures** - Each endpoint maps to a fixed 16-byte GUID
2. **Zero-copy routing** - Connect service extracts the GUID from the binary header and routes without parsing the JSON payload
3. **Path parameters break this** - `/account/{id}` cannot map to a single GUID because `{id}` varies

By moving all parameters to request bodies, every endpoint has a static path that maps to exactly one GUID, enabling zero-copy message routing.

**See also**: [BANNOU_DESIGN.md](../BANNOU_DESIGN.md) for the full POST-Only API Pattern explanation.

### How Browser-Facing Endpoints Work

Browser-facing endpoints are the **exception** to POST-only:
- Routed through NGINX reverse proxy (not WebSocket)
- NOT included in WebSocket API (no x-permissions)
- Using GET methods and path parameters for browser compatibility

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
| Direct Redis/MySQL connection | T4 | Use IStateStoreFactory via lib-state |
| Direct RabbitMQ connection | T4 | Use IMessageBus via lib-messaging |
| Direct HTTP service calls | T4 | Use IMeshInvocationClient or generated clients via lib-mesh |
| Graceful degradation for L0/L1/L2 dependency | T4 | Use constructor injection; see SERVICE-HIERARCHY.md |
| Lua script when interface method exists | T4 | Use `ICacheableStateStore` or `IRedisOperations` methods |
| Inline Lua script string | T4 | Move to `.lua` file with loader class |
| Loop/iteration in Lua script | T4 | Restructure to avoid iteration; use C# for loops |
| Lua script for large dataset | T4 | Use distributed lock + separate operations |
| Anonymous event objects | T5 | Define typed event in schema |
| Manually defining lifecycle events | T5 | Use `x-lifecycle` in events schema |
| Service class missing `partial` | T6 | Add `partial` keyword |
| Missing x-permissions on endpoint | T13 | Add to schema (even if empty array) |
| Designing browser-facing without justification | T15 | Use POST-only WebSocket pattern |
| GPL library in NuGet package | T18 | Use MIT/BSD alternative |

> **Schema-related violations** (editing Generated/ files, wrong env var format, missing description, etc.) are covered in [SCHEMA-RULES.md](../SCHEMA-RULES.md).

---

*This document covers tenets T4, T5, T6, T13, T15, T18. See [TENETS.md](../TENETS.md) for the complete index and Tenet 1 (Schema-First Development).*
