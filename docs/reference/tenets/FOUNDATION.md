# Foundation Tenets

> **Category**: Architecture & Design
> **When to Reference**: Before starting any new service, feature, or significant code change
> **Tenets**: T4, T5, T6, T13, T15, T18, T27, T28, T29, T32

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
  additionalProperties: false
  description: Event published when an entity action occurs
  required: [eventId, timestamp, entityId]
  properties:
    eventId:
      type: string
      format: uuid
      description: Unique event identifier
    timestamp:
      type: string
      format: date-time
      description: When the event occurred
    entityId:
      type: string
      format: uuid
      description: Entity this event pertains to
    # ... entity-specific fields with descriptions
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

### Background Service Store Access (MANDATORY)

Singleton `BackgroundService` classes cannot constructor-inject scoped dependencies like `IStateStoreFactory`. This creates a structural constraint where the T4/T6 constructor-caching pattern (`_stateStore = stateStoreFactory.GetStore<T>(...)`) cannot be physically followed. The correct pattern is to acquire stores **once per DI scope** and pass them as parameters to sub-methods — never re-acquire per method call.

```csharp
// CORRECT: Acquire stores once at scope creation, pass through as parameters
private async Task ProcessCycleAsync(CancellationToken ct)
{
    using var scope = _serviceProvider.CreateScope();
    var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();

    // Acquire all needed stores once per scope (equivalent to constructor-caching)
    var entityStore = stateStoreFactory.GetStore<EntityModel>(StateStoreDefinitions.MyEntity);
    var indexStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.MyEntity);

    // Pass store references to sub-methods — do NOT re-resolve the factory
    await ProcessBatchAsync(entityStore, indexStore, ct);
}

private async Task ProcessBatchAsync(
    IStateStore<EntityModel> entityStore,
    IStateStore<string> indexStore,
    CancellationToken ct)
{
    // Use passed store references directly
    var entity = await entityStore.GetAsync(key, ct);
}

// FORBIDDEN: Re-acquiring factory or stores per sub-method
private async Task ProcessBatchAsync(IStateStoreFactory factory, CancellationToken ct)
{
    var store = factory.GetStore<EntityModel>(StateStoreDefinitions.MyEntity);  // WRONG
}
```

**Rules**:
1. Resolve `IStateStoreFactory` from the DI scope exactly once per cycle
2. Call `GetStore<T>()` for each needed store immediately after resolving the factory
3. Pass the store references as method parameters to all sub-methods within that scope
4. Never pass `IStateStoreFactory` itself to sub-methods — pass the resolved stores
5. Never store `IStateStoreFactory` as a field on the background service class

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
| Documentation | `/documentation/view/{slug}`, `/documentation/raw/{slug}` (GET) | Browser-rendered markdown-to-HTML for knowledge base |

---

## Tenet 18: Licensing Requirements (MANDATORY)

**Rule**: All dependencies MUST use permissive licenses (MIT, BSD, Apache 2.0). Copyleft (GPL, LGPL, AGPL) is forbidden for linked code but acceptable for infrastructure containers communicating via network protocols.

**Approved**: MIT (preferred), BSD-2/3-Clause, Apache 2.0, ISC, Unlicense, CC0.
**Forbidden (linked code)**: GPL v2/v3, LGPL, AGPL.
**Infrastructure exception**: GPL software in separate containers (e.g., RTPEngine GPLv3, Kamailio GPLv2+).

When a package changes license, pin to the last permissive version with XML comment documentation.

---

## Tenet 27: Cross-Service Communication Discipline (MANDATORY)

**Rule**: Bannou has three mechanisms for cross-service communication. Each has exactly one valid use case. Using the wrong mechanism creates invisible coupling, loses error feedback, or wastes infrastructure.

### The Three Mechanisms

| Mechanism | What It Is | Valid Use |
|-----------|-----------|-----------|
| **Direct API call** (lib-mesh) | Synchronous request/response via generated clients | Higher layer calling lower layer for a specific outcome |
| **DI Provider/Listener interface** | In-process interface discovered via `IEnumerable<T>` | Lower layer exchanging data with optional higher layers |
| **Broadcast event** (lib-messaging) | Async fire-and-forget notification | Announcing state changes to unknown/external consumers |

### When to Use What

**Higher → Lower (L4 calling L2, L2 calling L1, etc.)**: Use **direct API calls**. The service hierarchy already permits this direction. The caller gets synchronous validation, error handling, and confirmation. Never publish an event when you could call the API directly.

```csharp
// CORRECT: L4 (Collection) calls L2 (Seed) directly — valid hierarchy direction
var (status, response) = await _seedClient.RecordGrowthAsync(new RecordGrowthRequest
{
    SeedId = seedId,
    Entries = domainEntries
}, ct);

// FORBIDDEN: Publishing to a lower layer's event topic as a disguised API call
await _messageBus.PublishAsync("seed.growth.contributed", new SeedGrowthContributedEvent { ... });
```

**Lower → Higher (L2 notifying L4, L1 notifying L3, etc.)**: Use **DI Listener interfaces** for targeted, in-process notifications to co-located consumers. Use **broadcast events** for notifying unknown/external/distributed consumers.

```csharp
// CORRECT: L2 (Seed) notifies L4 listeners via DI interface
foreach (var listener in _evolutionListeners)
{
    if (listener.InterestedSeedTypes.Count == 0
        || listener.InterestedSeedTypes.Contains(seedTypeCode))
        await listener.OnGrowthRecordedAsync(notification, ct);
}

// ALSO CORRECT: Broadcasting state change for unknown consumers
await _messageBus.PublishAsync("seed.phase.changed", phaseChangedEvent);
```

**Announcement to unknown consumers**: Use **broadcast events**. These are "something happened" notifications that any service may subscribe to. The publisher does not know or care who is listening.

### The Inverted Subscription Anti-Pattern (FORBIDDEN)

**Never define event schemas for the purpose of receiving data from services that could call your API directly.** This pattern — where a foundational service defines an event topic, subscribes to it, and waits for higher-layer services to publish to it — is a disguised API call with worse guarantees:

- No synchronous confirmation (publisher doesn't know if it worked)
- No validation feedback (malformed data silently dropped)
- RabbitMQ serialization overhead for in-process communication
- Fire-and-forget semantics for operations that require reliability

```csharp
// ANTI-PATTERN: L2 defines event, L4 publishes to it — this is just a bad API call
// In seed-events.yaml: defines seed.growth.contributed
// In lib-collection: await _messageBus.PublishAsync("seed.growth.contributed", ...)
// In lib-seed: subscribes to seed.growth.contributed and processes growth

// CORRECT: L4 calls L2's API directly
// In lib-collection: await _seedClient.RecordGrowthAsync(request, ct)
```

### Lower-Layer Event Subscriptions to Higher Layers (FORBIDDEN)

A lower-layer service must NEVER subscribe to events published by a higher-layer service, even for cache invalidation. This creates a conceptual dependency — the lower layer's correctness depends on whether the higher layer is enabled and publishing events.

When a lower-layer service uses DI Provider interfaces (`IVariableProviderFactory`, `IBehaviorDocumentProvider`, etc.) to pull data from optional higher layers, the **provider owns the cache**. The provider implementation (registered by the L4 plugin) handles its own cache management and invalidation by subscribing to its own service's events internally. The lower-layer consumer just calls the interface and gets fresh data.

```csharp
// FORBIDDEN: Actor (L2) subscribing to CharacterPersonality (L4) events
// In actor-events.yaml: x-event-subscriptions for personality.evolved
// Actor invalidates cache when personality changes

// CORRECT: PersonalityProviderFactory (L4) manages its own cache internally
// Actor just calls IVariableProviderFactory.CreateAsync() and gets current data
// PersonalityProviderFactory subscribes to personality.evolved within its own L4 plugin
```

### DI Interface Pattern Reference

Eight established provider/listener patterns exist (interfaces in `bannou-service/Providers/`):

| Interface | Direction | Purpose |
|-----------|-----------|---------|
| `IVariableProviderFactory` | L4 → L2 (data pull) | Actor pulls character data from L4 providers |
| `IPrerequisiteProviderFactory` | L4 → L2 (data pull) | Quest pulls prerequisite checks from L4 providers |
| `IBehaviorDocumentProvider` | L4 → L2 (data pull) | Actor pulls behavior docs from L4 providers |
| `ISeededResourceProvider` | L4 → L1 (data pull) | Resource pulls compression data from L4 providers |
| `ISeedEvolutionListener` | L2 → L4 (notification push) | Seed pushes evolution notifications to L4 listeners |
| `ICollectionUnlockListener` | L2 → L4 (notification push) | Collection pushes unlock notifications to L4 listeners |
| `ISessionActivityListener` | L1 → L1 (lifecycle push) | Connect pushes session lifecycle to Permission (high-frequency heartbeats) |
| `IItemInstanceDestructionListener` | L2 → L4 (cleanup push) | Item pushes instance destruction to L4 data owners (high-frequency T28 exception) |

All follow the same shape: interface defined in shared code (`bannou-service/Providers/`), higher-layer implements and registers as Singleton, lower-layer discovers via `IEnumerable<T>` with graceful degradation on failure.

### Startup Registration via DI Discovery (PERMITTED)

For services that need to register configuration or metadata at startup (e.g., permission matrices), a DI provider interface discovered at initialization is preferred over publishing registration events:

```csharp
// CORRECT: Permission discovers registrants via DI at startup
public class PermissionService
{
    public PermissionService(IEnumerable<IPermissionRegistrant> registrants, ...)
    {
        foreach (var registrant in registrants)
            RegisterPermissionMatrix(registrant.GetMatrix());
    }
}

// ANTI-PATTERN: Services publish registration events that Permission subscribes to
await _messageBus.PublishAsync("permission.service-registered", registrationEvent);
```

### Summary Decision Table

| Scenario | Mechanism | Example |
|----------|-----------|---------|
| L4 needs L2 to do something | Direct API call | Collection calls `ISeedClient.RecordGrowthAsync()` |
| L4 needs to register with L1 | DI Provider interface | Service implements `IPermissionRegistrant` |
| L2 needs data from optional L4 | DI Provider interface | Actor uses `IVariableProviderFactory` |
| L2 needs to notify optional L4 | DI Listener interface | Seed uses `ISeedEvolutionListener` |
| Something happened, anyone can react | Broadcast event | `seed.phase.changed`, `character.created` |
| High-frequency instance cleanup (T28 exception) | DI Listener + orphan worker | Item uses `IItemInstanceDestructionListener` |
| High-frequency session lifecycle | DI Listener (heartbeats DI-only) | Connect uses `ISessionActivityListener` |

---

## Tenet 28: Resource-Managed Cleanup (MANDATORY)

**Rule**: When a foundational resource (L1/L2) is deleted, all dependent data in higher layers MUST be cleaned up through **lib-resource** — never through lifecycle event subscriptions. Plugins must not subscribe to `*.deleted` or `*.lifecycle.*` events from other plugins for the purpose of destroying their own dependent data.

### Why This Matters

Event-based cleanup (`character.deleted` → L4 subscriber deletes its own data) has fundamental reliability problems:

1. **No ordering** — cleanup may execute after the resource is already gone, or race with other cleanup
2. **No blocking** — events can't prevent deletion when references still exist (RESTRICT policy impossible)
3. **No confirmation** — publisher doesn't know if cleanup succeeded or silently failed
4. **Invisible coupling** — subscribers are hidden; auditing what depends on what requires reading every event schema
5. **Missed events** — RabbitMQ failures, service restarts, or deployment gaps can lose deletion events permanently

lib-resource solves all five problems with centralized reference tracking, coordinated callback execution, and explicit cleanup policies (CASCADE, RESTRICT, DETACH).

### The Pattern

**Step 1**: Higher-layer services register references via lib-resource API when creating dependent data:

```csharp
// L4 (Character-Encounter) creates encounter data referencing a character
await _resourceClient.RegisterReferenceAsync(new RegisterReferenceRequest
{
    ResourceType = "character",
    ResourceId = characterId,
    SourceType = "character-encounter",
    SourceId = encounterId
}, ct);
```

**Step 2**: Higher-layer services implement cleanup callbacks via `ISeededResourceProvider`:

```csharp
// L4 provides cleanup logic that lib-resource calls during deletion
public class EncounterResourceProvider : ISeededResourceProvider
{
    public string SourceType => "character-encounter";

    public async Task CleanupReferencesAsync(
        string resourceType, Guid resourceId, CancellationToken ct)
    {
        await DeleteEncountersForCharacterAsync(resourceId, ct);
    }
}
```

**Step 3**: When the foundational resource is deleted, lib-resource coordinates cleanup:

```csharp
// L2 (Character) asks lib-resource to coordinate deletion
var result = await _resourceClient.ExecuteCleanupAsync(new ExecuteCleanupRequest
{
    ResourceType = "character",
    ResourceId = characterId,
    Policy = CleanupPolicy.Cascade
}, ct);
```

### What This Replaces

| Before (FORBIDDEN) | After (REQUIRED) |
|---------------------|-------------------|
| Character-Encounter subscribes to `character.deleted` | Character-Encounter registers with lib-resource, implements `ISeededResourceProvider` |
| Any L4 plugin subscribes to L2 `*.deleted` for destruction | L4 registers references and cleanup with lib-resource |

### Events Are Still Valid for Live State Reactions

Not all responses to state changes are "cleanup." Some are operational reactions to live state that don't involve destroying dependent data. These remain event-appropriate:

| Reaction Type | Mechanism | Example |
|---------------|-----------|---------|
| **Dependent data destruction** | lib-resource (MANDATORY) | Delete encounter records when character is deleted |
| **Cache invalidation** | Broadcast event (acceptable) | Invalidate analytics cache when character is updated |
| **Live session management** | Broadcast event (acceptable) | Auth invalidates live sessions when account is deleted |
| **State synchronization** | Broadcast event (acceptable) | Permission recompiles manifests when roles change |

**The test**: "If this event were lost, would data integrity be violated?" If yes → lib-resource. If no (cache rebuilds, sessions expire naturally, state converges eventually) → events are acceptable.

### Account Privacy Exception

Account resources (L1) are **exempt from lib-resource registration**. We do not track or compile historical reference data about accounts beyond what Analytics stores for its specific purpose. This is a deliberate privacy decision — the system should not maintain a centralized record of everything that references an account.

Auth's subscription to `account.deleted` for session invalidation is acceptable because:

1. It's L1→L1 (same layer, not cross-layer cleanup)
2. Sessions are live ephemeral state, not persistent dependent data
3. Sessions expire naturally via TTL regardless — the event accelerates invalidation, it doesn't provide the only path to cleanup
4. No data integrity violation if the event is lost (sessions time out)

This exception does not extend to other L1 resources. It is specific to Account due to the privacy constraint.

### High-Frequency Instance Lifecycle Exception

lib-resource cleanup is the correct default for persistent dependent data. However, when dependent data follows a **high-frequency template→instance pattern**, per-instance resource reference registration creates unacceptable overhead on the reference store and becomes the bottleneck itself.

**When this exception applies** (ALL criteria must be met):

1. **Instance frequency**: The parent entity instances are created and destroyed at rates where per-instance resource registration would flood the reference store (thousands/minute at scale — e.g., item instances across 100K NPCs in loot, crafting, trading, combat, decay)
2. **1:1 keying**: The dependent data is keyed directly by the parent entity's instance ID (one dependent record per parent instance, not a fan-out of many references per parent)
3. **Transient lifecycle**: Instances are relatively short-lived game objects, not persistent definitional entities that exist for the life of the deployment
4. **No RESTRICT requirement**: The dependent service never needs to block parent deletion (CASCADE-only — if the parent is destroyed, dependent data should always be cleaned up, never preserved)

**When this exception does NOT apply**:

- Character→CharacterPersonality: Characters are long-lived, low-frequency entities. Use lib-resource.
- Character→CharacterEncounter: Same. Encounters are created frequently but keyed by character (long-lived parent). Use lib-resource.
- GameService→AffixDefinitions: Game services are deployment-level entities, not transient instances. Use lib-resource.

**Required pattern**: Use a **DI Listener interface** (not event subscription, not lib-resource) for cleanup notification:

```csharp
// bannou-service/Providers/IItemInstanceDestructionListener.cs
public interface IItemInstanceDestructionListener
{
    /// <summary>
    /// Called by Item (L2) after an item instance is destroyed.
    /// Implementors clean up their own dependent data keyed by itemInstanceId.
    /// </summary>
    /// <remarks>
    /// LOCAL-ONLY FAN-OUT: This listener fires only on the node that processed
    /// the deletion request. Reactions MUST write to distributed state (MySQL/Redis
    /// deletes). The broadcast event (item.instance.destroyed) is still published
    /// via IMessageBus for distributed consumers.
    ///
    /// Implementing services MUST provide an orphan reconciliation worker as the
    /// durability guarantee for missed listener notifications.
    /// </remarks>
    Task OnItemInstanceDestroyedAsync(Guid itemInstanceId, Guid gameServiceId, CancellationToken ct);
}
```

**Required safeguards**:

1. **DI Listener writes to distributed state** — MySQL deletes, Redis cache invalidation. Distributed safety per SERVICE-HIERARCHY.md § "DI Provider vs Listener."
2. **Broadcast event still published** — `item.instance.destroyed` via IMessageBus for any distributed consumer that needs it. The listener is an optimization, not a replacement.
3. **Orphan reconciliation worker** — Background worker periodically scans dependent records, batch-checks parent existence via the parent service's client, and deletes orphaned records. This is the durability guarantee that makes the pattern safe even when listeners miss notifications.
4. **Cache TTL as secondary safety net** — Redis-cached dependent data expires naturally even if both listener and reconciliation miss a deletion.

**Why DI Listener instead of event subscription**: This follows the established DI Listener pattern (ISeedEvolutionListener, ICollectionUnlockListener, ISessionActivityListener) rather than creating a new category of "acceptable event-based cleanup." The parent service (Item L2) dispatches to co-located listeners after the mutation — zero RabbitMQ overhead, guaranteed in-process delivery, graceful degradation if no listeners are registered (L4 not enabled). Event subscription for cleanup remains forbidden per the base T28 rule.

**Current instances**:

| Interface | Direction | Parent → Dependent | Justification |
|-----------|-----------|-------------------|---------------|
| `ISessionActivityListener` | L1→L1 (Connect→Permission) | Session→Permission state | Heartbeats every 30s per session; event bus overhead unacceptable |
| `IItemInstanceDestructionListener` | L2→L4 (Item→Affix, future item-data-owners) | ItemInstance→AffixInstance | Items created/destroyed at loot/combat/trading frequency across 100K NPCs |

### Enforcement

When adding a new service that stores data keyed by another service's entity:

1. **Default**: Register references with lib-resource API when creating dependent data, implement `ISeededResourceProvider` for cleanup callbacks
2. **Exception**: If ALL four criteria of the High-Frequency Instance Lifecycle Exception are met, use DI Listener + orphan reconciliation instead
3. Do NOT add `x-event-subscriptions` for the parent entity's `*.deleted` event for cleanup purposes (regardless of which path you use)
4. Do NOT add event handler methods for parent entity deletion

---

## Tenet 29: No Metadata Bag Contracts (INVIOLABLE)

**Rule**: `additionalProperties: true` (or any untyped metadata dictionary) MUST NEVER be used as a data contract between services. If Service A stores data that Service B reads by convention, that data MUST be defined in a schema, generated into typed code, and owned by the service responsible for the domain concept.

### Why This Exists

This tenet exists because of a specific anti-pattern that was proposed: storing `biomeCode` in Location's (L2) `additionalProperties` metadata bag as a convention for Environment (L4) to consume. This is a **total schema-first violation** that defeats every architectural guarantee Bannou provides.

### The Eight Failures of Metadata Bag Contracts

1. **Total Schema-First Violation (Tenet 1)**: A convention that says "put `biomeCode` in Location's metadata JSON bag" is the exact opposite of schema-first development. It's an unschematized, ungenerated, unenforced verbal agreement. No OpenAPI spec declares it. No code is generated for it. No validator catches its absence. It exists only in documentation that an implementer may or may not read.

2. **Zero Type Safety**: `additionalProperties: true` means the metadata field accepts any JSON. The key could be missing entirely (silent null), misspelled (`biomCode`, `BiomeCode`, `biome_code` — all silent misses), wrong type (`biomeCode: 42`, `biomeCode: true`), or semantically invalid (`biomeCode: "foobar"` — no matching template). None of these are caught at compile time, generation time, schema validation time, or even runtime without explicit defensive code that shouldn't be necessary.

3. **Invisible Cross-Service Coupling**: The producer's schema says nothing about the consuming service's data. A developer refactoring the metadata handling, changing its storage model, or migrating its data has zero signal that another service depends on specific keys in that JSON bag. The coupling is invisible to schema analysis tools, generated code, import/dependency graphs, code review, and the ServiceHierarchyValidator.

4. **Higher-Layer Domain Data Polluting Lower-Layer Storage**: If the data belongs to a higher-layer domain concept (e.g., biome codes are an ecological L4 concept), storing it in a lower-layer service (L2) means: non-game deployments (`GAME_FEATURES=false`) carry domain-specific data for no reason, storage size increases for concepts the service doesn't use, backup/restore/migration must preserve data it doesn't understand, and admin tooling shows fields it can't explain.

5. **No Referential Integrity**: Nothing prevents creating an entity with a metadata value that references something that doesn't exist, deleting the referenced thing while metadata still points to it, or two entities spelling the same value differently. With owned bindings, all of this is enforceable at the API level.

6. **No Lifecycle Management**: Nobody knows who updates the metadata when the referenced value is renamed, when the entity should change its metadata value due to a game event, or when world initialization needs to ensure metadata is populated. With service-owned bindings, the owning service manages its own data lifecycle.

7. **Untestable in Isolation**: The producer's unit tests have no reason to verify metadata structure. The consumer's unit tests can't verify that metadata is correctly populated without standing up the producer. Integration tests must verify a cross-service convention that no schema enforces. The gap between "entity created" and "metadata contains valid data" is an untestable assumption.

8. **Convention Drift is Undetectable**: If the convention changes (rename a key, add a required field, change a type from float to object), there is no schema diff, no generated code change, no compilation error, no automated migration. Just silent runtime breakage that manifests as wrong behavior with no error message pointing to the cause.

### What Metadata Bags ARE For

Metadata dictionaries (`additionalProperties: true`), when they exist at all, serve exactly TWO purposes:

1. **Client-side display data**: Game clients store rendering hints, UI preferences, or display-only information that no Bannou service consumes. Example: a location's `mapIcon`, `ambientSoundFile`, or `tooltipColor`.

2. **Game-specific implementation data**: Data that the game engine (not Bannou services) interprets at runtime. Example: Unity-specific physics parameters, Unreal material overrides, or custom game mode settings.

In both cases, the metadata is **opaque to all Bannou plugins**. No plugin reads it by convention. No plugin's correctness depends on its structure.

### The Correct Pattern: Service-Owned Bindings

When Service B needs data associated with Service A's entities, Service B MUST own that data:

```csharp
// CORRECT: Environment (L4) owns its own location-climate bindings
// Environment's schema defines the binding model
// Environment stores bindings in its own state store
// Environment validates biomeCode against its own climate template registry
// Environment manages binding lifecycle through its own API

// In environment-api.yaml:
//   /environment/climate-binding/create  (validates location exists via ILocationClient)
//   /environment/climate-binding/get     (by locationId)
//   /environment/climate-binding/update  (revalidates)
//   /environment/climate-binding/delete  (cleanup)

// FORBIDDEN: Environment reads biomeCode from Location's metadata bag
// var metadata = locationResponse.Metadata;
// var biomeCode = metadata?["biomeCode"]?.ToString();  // NEVER!
```

### How This Applies to Each Service Layer

| Scenario | Wrong | Right |
|----------|-------|-------|
| L4 needs data on L2 entities | Store in L2's metadata bag by convention | L4 owns binding table, references L2 entity by ID |
| L2 needs client display hints | Store typed fields in L2's schema | Use metadata bag (clients read it, no plugin does) |
| Two L4 services share data about L2 entities | Both read from L2's metadata bag | One L4 service owns the data, the other queries it via API |
| L4 needs to tag L2 entities | Add tag to L2's metadata by convention | L4 maintains its own tag-to-entity mapping |

### Detection & Enforcement

**Schema-level**: Any schema property with `additionalProperties: true` must be documented as client-only metadata. Code review must verify no plugin reads metadata keys from another service's entities by convention.

**Code-level**: If you see `JsonElement` navigation on another service's metadata field, or dictionary key lookups on response metadata, this is a violation. The consuming service should own its own data.

**The test**: "If I renamed or removed this metadata key, would any Bannou plugin break?" If yes — **violation**. That data must be in a schema.

---

## Tenet 32: Account Identity Boundary (INVIOLABLE)

**Rule**: `accountId` is a privileged identifier restricted to the **identity, session, and access boundary**. Services outside this boundary MUST NOT accept `accountId` from clients and SHOULD NOT propagate it in events. The Connect gateway resolves session-to-account mapping; downstream services identify callers by `webSocketSessionId` or domain-specific identifiers.

### Why This Exists

A client sending `accountId` in a request body can substitute any account's ID. The permission system validates roles and states, not that `request.accountId == session.accountId`. Accepting client-provided `accountId` creates an impersonation surface that only works by coincidence (clients typically send their own ID) rather than by enforcement.

Beyond security, `accountId` propagation creates invisible coupling. When a game feature service emits `accountId` in events, every consumer implicitly depends on the account identity concept. Using `sessionId` instead means consumers couple to the session layer (Connect, L1, always available) rather than the account domain.

### The Identity Boundary (Exhaustive)

Services on this list have justified `accountId` usage. **No other service may use `accountId` in client-facing contracts.**

| Service | Layer | Why accountId is justified | Pattern |
|---------|-------|--------------------------|---------|
| **Account** | L1 | IS the account entity (CRUD) | Internal-only, admin role |
| **Auth** | L1 | Authenticates accounts, issues JWTs | Derives from credentials; NEVER accepts in request body |
| **Connect** | L1 | Session-to-account mapping | Derives from JWT; maps sessions to accounts |
| **Permission** | L1 | Receives accountId via session events | Zero accountId in API -- uses sessionId exclusively |
| **Subscription** | L2 | Account-to-game access mapping | Internal service-to-service queries (called by Auth, GameSession) |
| **Game-Session** | L2 | Multiplayer container tracking account membership | Server-injected via shortcut system; client never provides accountId |
| **Analytics** | L4 | Controller history audit trail | Account-level aggregation for statistics, audit, anti-cheat |
| **Gardener** | L4 | Player experience orchestration | All endpoints called by divine actors (internal service callers) via Actor action handlers, not by players directly |

### The Five Rules

**Rule 1: Client-facing endpoints MUST NOT accept accountId in request bodies.**

Any endpoint with `x-permissions: [role: user]` that is directly accessible via WebSocket (not shortcut-gated) MUST NOT include `accountId` as a request field. The Connect gateway authenticated the session via JWT. Downstream services identify the caller by `webSocketSessionId`.

```yaml
# FORBIDDEN: Client provides accountId
JoinMatchmakingRequest:
  required: [webSocketSessionId, accountId, queueId]  # accountId from untrusted client!

# CORRECT: Service uses webSocketSessionId only
JoinMatchmakingRequest:
  required: [webSocketSessionId, queueId]  # session identity sufficient
```

**Rule 2: Server-injected accountId via shortcuts is the correct pattern.**

For endpoints that need accountId but are triggered by client action, the service constructs the request body (including accountId resolved from session context) and publishes a shortcut. The client triggers the shortcut by GUID. This is how Game-Session's join/leave and Matchmaking's leave/accept/decline work.

```csharp
// CORRECT: Server constructs prebound request with accountId
var preboundRequest = new LeaveMatchmakingRequest
{
    WebSocketSessionId = sessionId,
    AccountId = accountId,  // Server-resolved, not client-provided
    TicketId = ticketId
};
await PublishShortcutAsync(sessionId, "matchmaking/leave", preboundRequest, ct);
```

**Rule 3: Internal service-to-service endpoints MAY accept accountId.**

When the caller is a trusted service (role: admin, role: developer, or mesh-routed service call), accepting accountId is fine. Account, Subscription, and Gardener (divine actor caller) fall into this category. The key distinction: the accountId comes from an authenticated service, not from an untrusted WebSocket client.

**Rule 4: Events SHOULD use sessionId rather than accountId.**

Services outside the identity boundary should use `sessionId` or domain-specific identifiers (ticketId, matchId, entityId) in event payloads. Consumers needing account-level correlation can query Connect or Analytics.

Exceptions: Identity-boundary services (Auth, Connect, Subscription, Game-Session) where accountId IS the domain concept being communicated. These services exist specifically to manage account-level state.

```yaml
# FORBIDDEN: Game feature service emitting accountId in events
MatchmakingTicketCreatedEvent:
  required: [eventId, timestamp, ticketId, queueId, accountId]  # Why is accountId here?

# CORRECT: Use domain identifiers; consumers correlate via Connect if needed
MatchmakingTicketCreatedEvent:
  required: [eventId, timestamp, ticketId, queueId, webSocketSessionId]
```

**Rule 5: Polymorphic owner fields mixing accountId with service names are forbidden.**

A single string field described as "accountId (UUID) for users, service name (string) for services" violates both this tenet and T14 (Polymorphic Associations). Use proper polymorphic typing:

```yaml
# FORBIDDEN: Overloaded string field
owner:
  type: string
  description: "accountId (UUID) for users, service name for services"

# CORRECT: Proper polymorphic typing per T14
ownerType:
  type: string
  enum: [Account, Service]
ownerId:
  type: string
  description: Account session ID (UUID) or service name depending on ownerType
```

### Reference Implementation

**Voice (L3)** uses `sessionId` exclusively with zero `accountId` references. Its code documents the design decision explicitly: "Using sessionId instead of accountId to support multiple sessions per account." This is the reference pattern for services outside the identity boundary.

**Permission (L1)** demonstrates that even within the identity boundary, a service can operate entirely on `sessionId`. Permission compiles capability manifests per session, not per account. It receives accountId from session events for internal mapping but never exposes it in its API.

### Detection Rules

| Signal | Verdict |
|--------|---------|
| `accountId` in request model + endpoint has `x-permissions: [role: user]` without shortcut gating | **Violation of Rule 1** |
| `accountId` in event model for service not in identity boundary table | **Violation of Rule 4** |
| String field described as "accountId or service name" | **Violation of Rule 5 + T14** |
| Service outside boundary accepting accountId with `role: admin` or `role: developer` | **Review needed** -- may be legitimate internal API |
| Service in boundary using server-injected accountId via shortcuts | **Correct pattern** |

---

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
| Publishing event to lower-layer's topic instead of calling API | T27 | Use generated client directly (hierarchy permits the call) |
| Lower-layer subscribing to higher-layer events | T27 | Use DI Provider/Listener interface in `bannou-service/Providers/` |
| Publishing registration events at startup | T27 | Use DI Provider interface discovered via `IEnumerable<T>` |
| Defining event schema to receive data from callers | T27 | Remove event; expose API endpoint; callers use generated client |
| Lower-layer caching higher-layer data with event invalidation | T27 | Provider owns its cache; lower layer calls provider interface |
| Subscribing to `*.deleted` for dependent data cleanup | T28 | Register with lib-resource; implement `ISeededResourceProvider` |
| Event-based cleanup for persistent dependent data | T28 | Use lib-resource with CASCADE/RESTRICT/DETACH policy |
| Cleanup handler in `*ServiceEvents.cs` for another service's entity | T28 | Move to lib-resource cleanup callback; remove event subscription |
| Using `additionalProperties: true` as cross-service data contract | T29 | Owning service defines its own schema, stores its own data |
| Reading metadata keys from another service's response by convention | T29 | Query the service that owns the domain concept via API |
| Storing higher-layer domain data in lower-layer metadata bags | T29 | Higher-layer service owns binding table, references lower-layer entity by ID |
| `JsonElement` navigation on another service's metadata field | T29 | Define data in owning service's schema, use generated types |
| Documentation specifying "put X in service Y's metadata" | T29 | X belongs in the schema of the service that owns concept X |
| Client-facing endpoint accepting accountId in request body | T32 | Remove accountId; use webSocketSessionId; resolve account server-side if needed |
| Non-boundary service emitting accountId in events | T32 | Use sessionId or domain-specific identifiers (ticketId, matchId) |
| Polymorphic string field "accountId or service name" | T32, T14 | Use ownerType enum + ownerId; use sessionId for user-initiated operations |
| Service outside identity boundary accepting accountId with role: user | T32 | Use shortcut system for server-injected accountId, or remove entirely |
| New service added to identity boundary table without approval | T32 | Discuss with team; justify why accountId is required at the domain level |

> **Schema-related violations** (editing Generated/ files, wrong env var format, missing description, etc.) are covered in [SCHEMA-RULES.md](../SCHEMA-RULES.md).

---

*This document covers tenets T4, T5, T6, T13, T15, T18, T27, T28, T29, T32. See [TENETS.md](../TENETS.md) for the complete index and Tenet 1 (Schema-First Development).*
