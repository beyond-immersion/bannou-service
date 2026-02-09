# Foundation Tenets

> **Category**: Architecture & Design
> **When to Reference**: Before starting any new service, feature, or significant code change
> **Tenets**: T4, T5, T6, T13, T15, T18

These tenets define the architectural foundation of Bannou. Understanding them is prerequisite to any development work.

> **Note**: Schema-related rules (formerly T1, T2) are now consolidated in [SCHEMA-RULES.md](../SCHEMA-RULES.md) and referenced by Tenet 1 in [TENETS.md](../TENETS.md).

---

## Tenet 4: Infrastructure Libs Pattern (ABSOLUTE)

**Rule**: Services MUST use the three infrastructure libs for all infrastructure concerns. Direct database/cache/queue access is FORBIDDEN in service code.

| Lib | Purpose | Replaces |
|-----|---------|----------|
| **lib-state** | State management (Redis/MySQL) | Direct Redis/MySQL connections |
| **lib-messaging** | Event pub/sub (RabbitMQ) | Direct RabbitMQ channel access |
| **lib-mesh** | Service invocation (YARP) | Direct HTTP client calls |

Infrastructure libs cannot be disabled - they provide the abstraction layer enabling deployment flexibility.

### Usage Patterns

```csharp
// lib-state: ALWAYS use StateStoreDefinitions constants (schema-first)
_stateStore = stateStoreFactory.GetStore<MyModel>(StateStoreDefinitions.MyService);
await _stateStore.SaveAsync(key, value, cancellationToken: ct);
await _stateStore.SaveAsync(key, value, new StateOptions { Ttl = TimeSpan.FromMinutes(30) });
var (value, etag) = await _stateStore.GetWithETagAsync(key, ct);

// lib-state: Sets and sorted sets (Redis + InMemory only)
var cacheStore = stateStoreFactory.GetCacheableStore<MyModel>(StateStoreDefinitions.MyCache);
await cacheStore.AddToSetAsync("my-set", item, ct);
await cacheStore.SortedSetAddAsync("leaderboard", memberId, score, ct);

// lib-state: Atomic operations (Lua scripts, counters, hashes - Redis only)
var redisOps = stateStoreFactory.GetRedisOperations();  // Returns null in InMemory mode

// lib-messaging
await _messageBus.PublishAsync("entity.action", evt, cancellationToken: ct);
await _messageSubscriber.SubscribeAsync<MyEvent>("topic", async (evt, ct) => await HandleAsync(evt, ct));
var subscription = await _messageSubscriber.SubscribeDynamicAsync<MyEvent>(  // Disposable per-session
    "session.events", async (evt, ct) => await HandleSessionEventAsync(evt, ct));

// lib-mesh: Generated clients preferred
await _accountClient.GetAccountAsync(request, ct);
await _meshClient.InvokeMethodAsync<Request, Response>("account", "get-account", request, ct);
```

**FORBIDDEN**: `new MySqlConnection(...)`, `ConnectionMultiplexer.Connect(...)`, `channel.BasicPublish(...)`, `httpClient.PostAsync("http://account/api/...")`.

Generated clients are auto-registered as Singletons and use mesh service resolution internally.

### Dependency Hardness by Layer (ABSOLUTE)

Dependencies on L0/L1/L2 services MUST be hard (fail at startup if missing). Dependencies on L3/L4 MAY be soft (graceful degradation). See [SERVICE-HIERARCHY.md § Dependency Handling Patterns](../SERVICE-HIERARCHY.md#dependency-handling-patterns-mandatory).

```csharp
// CORRECT: Constructor injection for L0/L1/L2 (fails at startup if missing)
public LocationService(IContractClient contractClient, ...) { _contractClient = contractClient; }

// FORBIDDEN: Silent degradation for guaranteed service
if (contractClient == null) { return; }  // Hides deployment errors - THROW instead

// CORRECT: Soft dependency on L4 (truly optional)
var analyticsClient = serviceProvider.GetService<IAnalyticsClient>();
if (analyticsClient == null) { /* graceful degradation OK */ }
```

### State Store Schema-First Pattern

All stores defined in `schemas/state-stores.yaml`. Generation produces `StateStoreDefinitions.cs` constants and documentation.

```csharp
// CORRECT: Generated constants
_stateStore = stateStoreFactory.GetStore<AccountModel>(StateStoreDefinitions.Account);

// FORBIDDEN: Hardcoded store names
_stateStore = stateStoreFactory.GetStore<AccountModel>("account-statestore");
```

| Backend | Purpose | Example Stores |
|---------|---------|----------------|
| Redis | Ephemeral state, caches, rankings | `auth-statestore`, `connect-statestore` |
| MySQL | Persistent queryable data | `account-statestore`, `character-statestore` |

### State Store Interface Hierarchy

| Interface | Backends | Purpose |
|-----------|----------|---------|
| `IStateStore<T>` | All | Core CRUD, bulk operations, ETags |
| `ICacheableStateStore<T>` | Redis, InMemory | Sets and Sorted Sets |
| `IQueryableStateStore<T>` | MySQL only | LINQ expression queries with pagination |
| `IJsonQueryableStateStore<T>` | MySQL only | JSON path queries within stored documents |
| `ISearchableStateStore<T>` | Redis+Search only | Full-text search with FT.* commands |
| `IRedisOperations` | Redis only | Lua scripts, atomic counters, hashes, TTL |

**Factory methods**: `GetStore<T>()` (all), `GetCacheableStore<T>()` (Redis/InMemory), `GetQueryableStore<T>()` (MySQL), `GetJsonQueryableStore<T>()` (MySQL), `GetSearchableStore<T>()` (Redis+Search), `GetRedisOperations()` (Redis, returns null otherwise).

**IRedisOperations use cases**: Lua scripts for atomic multi-key operations, `INCR`/`DECR` counters, `HGET`/`HSET` hashes, TTL manipulation, cross-store atomic operations. Keys are NOT prefixed (raw Redis keys).

### Lua Script Requirements (STRICT)

Lua scripts via `IRedisOperations.ScriptEvaluateAsync()` are a **last resort**. Before writing one, verify that `ICacheableStateStore<T>` methods, `IRedisOperations` counters/hashes, and `IDistributedLockProvider` + separate operations are all insufficient. Use Lua only when **atomicity across multiple distinct operations is genuinely required**.

**Absolute Restrictions**:

| Restriction | Reason |
|-------------|--------|
| **FORBIDDEN: Loops/iteration** | Lua blocks Redis (single-threaded). Loops cause latency spikes for ALL clients. |
| **FORBIDDEN: Inline script strings** | Scripts MUST be in `.lua` files under `Scripts/`, loaded via `{Plugin}LuaScripts.cs`, embedded as resources. |
| **FORBIDDEN: Large dataset processing** | Scripts cannot be killed after a write. Long scripts make Redis unresponsive. |
| **FORBIDDEN: Blocking commands** | `BLPOP`, `BRPOP`, etc. cannot be used in Lua. |

**Required file structure**:
```
plugins/lib-{service}/
├── Scripts/
│   └── MyOperation.lua          # Embedded resource
├── Services/
│   └── {Service}LuaScripts.cs   # Static loader class with ConcurrentDictionary cache
└── lib-{service}.csproj         # <EmbeddedResource Include="Scripts\*.lua" />
```

See lib-mesh and lib-state for loader class examples. Loader class pattern:

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

**Best practices**: Use `redis.pcall()` over `redis.call()`, keep scripts minimal (atomic part only, handle results in C#), parameterize all scripts (KEYS[] and ARGV[]).

### Infrastructure Lib Backend Access

Each infrastructure lib accesses its specific backend directly - that's their purpose. Service code uses infrastructure lib interfaces, never backends directly.

| Infrastructure Lib | Direct Backend Access |
|-------------------|----------------------|
| lib-state | Redis, MySQL |
| lib-messaging | RabbitMQ |
| lib-orchestrator | Docker, Portainer, Kubernetes APIs |
| lib-voice | RTPEngine, Kamailio |
| lib-asset | MinIO |

---

## Tenet 5: Event-Driven Architecture (REQUIRED)

**Rule**: All meaningful state changes MUST publish events, even without current consumers.

### No Anonymous Events (ABSOLUTE)

All events MUST be defined as typed schemas. Anonymous object publishing is FORBIDDEN:

```csharp
// CORRECT
await _messageBus.PublishAsync("account.created", new AccountCreatedEvent { ... });

// FORBIDDEN: Anonymous objects cause MassTransit runtime errors
await _messageBus.PublishAsync("account.created", new { AccountId = id });
```

**Event Type Locations**:

| Event Type | Schema File | Generated Output |
|------------|-------------|------------------|
| Service Events | `{service}-events.yaml` | `bannou-service/Generated/Events/{Service}EventsModels.cs` |
| Client Events | `{service}-client-events.yaml` | `lib-{service}/Generated/{Service}ClientEventsModels.cs` |
| Common Client Events | `common-client-events.yaml` | `bannou-service/Generated/CommonClientEventsModels.cs` |

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

**Pattern**: `{entity}.{action}` (kebab-case entity, lowercase action). Examples: `account.created`, `game-session.player-joined`, `character.realm.joined`. Infrastructure events use `bannou.` prefix (e.g., `bannou.full-service-mappings`).

### Lifecycle Events (x-lifecycle) - NEVER MANUALLY CREATE

CRUD-style lifecycle events MUST be auto-generated via `x-lifecycle` in the events schema. NEVER manually define `*CreatedEvent`/`*UpdatedEvent`/`*DeletedEvent`.

```yaml
x-lifecycle:
  EntityName:
    model:
      entityId: { type: string, format: uuid, primary: true, required: true }
      name: { type: string, required: true }
    sensitive: [passwordHash, secretKey]  # Fields excluded from events
```

This generates `EntityNameCreatedEvent` (full data), `EntityNameUpdatedEvent` (full data + `changedFields`), and `EntityNameDeletedEvent` (ID + `deletedReason`).

### Full-State Events Pattern

For atomically consistent state across instances, include complete state + monotonic version. Consumers use version-check-and-replace: reject if `version <= _currentVersion`, otherwise replace all state and update version.

### Canonical Event Definitions (CRITICAL)

Each `{service}-events.yaml` MUST contain ONLY canonical definitions for events that service PUBLISHES. No `$ref` references to other service event files - NSwag follows `$ref` and generates ALL types it encounters, causing duplicate type definitions.

---

## Tenet 6: Service Implementation Pattern (STANDARDIZED)

**Rule**: All service implementations MUST follow the standardized structure.

### Partial Class Requirement (MANDATORY)

ALL service classes MUST be `partial class` from initial creation. Event handlers are implemented in separate `{Service}ServiceEvents.cs` (optional - only needed when subscribing to events).

```
plugins/lib-{service}/
├── {Service}Service.cs          # Main implementation (partial class, REQUIRED)
└── {Service}ServiceEvents.cs    # Event handlers (partial class, OPTIONAL)
```

### Service Class Pattern

```csharp
[BannouService("service-name", typeof(IServiceNameService), lifetime: ServiceLifetime.Scoped)]
public partial class ServiceNameService : IServiceNameService
{
    private readonly IStateStore<ServiceModel> _stateStore;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<ServiceNameService> _logger;
    private readonly ServiceNameServiceConfiguration _configuration;

    public ServiceNameService(
        IStateStoreFactory stateStoreFactory, IMessageBus messageBus,
        ILogger<ServiceNameService> logger, ServiceNameServiceConfiguration configuration,
        IEventConsumer eventConsumer, IAuthClient authClient)
    {
        _stateStore = stateStoreFactory.GetStore<ServiceModel>(StateStoreDefinitions.ServiceName);
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        RegisterEventConsumers(eventConsumer);
    }

    public async Task<(StatusCodes, ResponseModel?)> MethodAsync(
        RequestModel body, CancellationToken ct = default)
    {
        return (StatusCodes.OK, response);
    }
}
```

### Common Dependencies

| Dependency | Purpose |
|------------|---------|
| `IStateStoreFactory` | Create typed state stores (Redis/MySQL) |
| `IMessageBus` | Publish events (includes `TryPublishErrorAsync`) |
| `IMessageSubscriber` | Subscribe to RabbitMQ topics |
| `IMeshInvocationClient` | Service-to-service invocation |
| `ILogger<T>` | Structured logging |
| `{Service}ServiceConfiguration` | Generated configuration class |
| `IEventConsumer` | Register event handlers for pub/sub fan-out |
| `I{Service}Client` | Generated service clients for inter-service calls |
| `IDistributedLockProvider` | Redis-backed distributed locking |
| `IClientEventPublisher` | Push events to WebSocket clients |

### Helper Service Decomposition

For complex services, decompose into helper services in `Services/` subdirectory with interfaces for mockability.

**Lifetime rule**: Helper service lifetime MUST be equal to or longer than the main service lifetime. A Singleton main service CANNOT inject a Scoped helper (captive dependency).

---

## Tenet 13: X-Permissions Usage (DOCUMENTED)

**Rule**: All endpoints MUST declare x-permissions in schema, even if empty.

- Applies to **WebSocket client connections only** (NOT service-to-service calls)
- Enforced by Connect service when routing client requests
- Endpoints without x-permissions are **not exposed** to WebSocket clients

**Role hierarchy**: `anonymous` → `user` → `developer` → `admin` (higher includes lower). Client must have the highest role specified AND all states specified.

```yaml
# Example: User role + must be in lobby
x-permissions:
  - role: user
    states:
      game-session: in_lobby  # Requires BOTH user role AND in_lobby state
```

| Role | Use When | Examples |
|------|----------|----------|
| `admin` | Destructive or sensitive operations | Orchestrator, account deletion |
| `developer` | Creating/managing resources | Character creation, realm management |
| `user` | Requires authentication | Most gameplay endpoints |
| `anonymous` | Intentionally public (rare) | Server status |

---

## Tenet 15: Browser-Facing Endpoints (DOCUMENTED)

**Rule**: Some endpoints are accessed directly by browsers through NGINX rather than WebSocket. These are EXCEPTIONAL.

Bannou uses POST-only APIs because each endpoint maps to a fixed 16-byte GUID for zero-copy binary routing. Path parameters (`/account/{id}`) break this since `{id}` varies. Browser-facing endpoints are the exception: routed through NGINX, NOT in WebSocket API, using GET + path parameters.

**Current browser-facing endpoints** (complete list - additions require justification):

| Service | Endpoints | Reason |
|---------|-----------|--------|
| Website | All `/website/*` | Public website, SEO, caching |
| Auth | `/auth/oauth/{provider}/init`, `/auth/oauth/{provider}/callback` | OAuth redirect flow |
| Connect | `/connect` (GET) | WebSocket upgrade handshake |

---

## Tenet 18: Licensing Requirements (MANDATORY)

**Rule**: All dependencies MUST use permissive licenses (MIT, BSD, Apache 2.0). Copyleft (GPL, LGPL, AGPL) is forbidden for linked code but acceptable for infrastructure containers communicating via network protocols.

**Approved**: MIT (preferred), BSD-2/3-Clause, Apache 2.0, ISC, Unlicense, CC0.
**Forbidden (linked code)**: GPL v2/v3, LGPL, AGPL.
**Infrastructure exception**: GPL software in separate containers (e.g., RTPEngine GPLv3, Kamailio GPLv2+).

When a package changes license, pin to the last permissive version with XML comment documentation.

---

## Quick Reference: Foundation Violations

| Violation | Tenet | Fix |
|-----------|-------|-----|
| Direct Redis/MySQL connection | T4 | Use IStateStoreFactory via lib-state |
| Direct RabbitMQ connection | T4 | Use IMessageBus via lib-messaging |
| Direct HTTP service calls | T4 | Use generated clients via lib-mesh |
| Graceful degradation for L0/L1/L2 dependency | T4 | Constructor injection; see SERVICE-HIERARCHY.md |
| Lua script when interface method exists | T4 | Use `ICacheableStateStore` or `IRedisOperations` methods |
| Inline Lua script string | T4 | Move to `.lua` file with loader class |
| Loop/iteration in Lua script | T4 | Restructure; use C# for loops |
| Anonymous event objects | T5 | Define typed event in schema |
| Manually defining lifecycle events | T5 | Use `x-lifecycle` in events schema |
| Service class missing `partial` | T6 | Add `partial` keyword |
| Missing x-permissions on endpoint | T13 | Add to schema (even if empty array) |
| Designing browser-facing without justification | T15 | Use POST-only WebSocket pattern |
| GPL library in NuGet package | T18 | Use MIT/BSD alternative |

> **Schema-related violations** (editing Generated/ files, wrong env var format, missing description, etc.) are covered in [SCHEMA-RULES.md](../SCHEMA-RULES.md).

---

*This document covers tenets T4, T5, T6, T13, T15, T18. See [TENETS.md](../TENETS.md) for the complete index and Tenet 1 (Schema-First Development).*
