# Dapr Transition Progress

*Last Updated: 2025-12-25*
*Status: ~85% Complete*

## Executive Summary

The Bannou platform is transitioning from Dapr sidecar-based infrastructure to native .NET infrastructure libraries (`lib-messaging`, `lib-state`, `lib-mesh`). This provides:

- **Eliminated Sidecar Overhead**: No more Dapr sidecar containers (reduces resource usage by 50%+)
- **Direct Backend Access**: Native Redis/RabbitMQ/MySQL connections without HTTP proxying
- **Simplified Deployment**: Single container per service instead of service + sidecar
- **Better Debugging**: Direct access to infrastructure without Dapr abstraction layer
- **Type Safety**: Strongly-typed interfaces with BannouJson serialization consistency

---

## Current Status

### Completed (85%)

| Component | Status | Notes |
|-----------|--------|-------|
| **lib-messaging** | ✅ Complete | MassTransit-based IMessageBus with RabbitMQ |
| **lib-state** | ✅ Complete | Redis + MySQL state stores with factory pattern |
| **lib-mesh** | ✅ Complete | YARP-based IMeshInvocationClient |
| **Plugin Libraries** | ✅ Complete | All lib-* plugins use infrastructure libs exclusively |
| **Shared Interfaces** | ✅ Complete | IStateStore, IMessageBus, IMeshInvocationClient in bannou-service |
| **EventPublisherBase** | ✅ Complete | Uses IMessageBus (not DaprClient) |

### Remaining (15%) - With Architectural Simplification Opportunities

| Component | Priority | Effort | Keep/Simplify/Delete |
|-----------|----------|--------|---------------------|
| IErrorEventEmitter | High | 2 hours | **DELETE** - Add method to IMessageBus instead |
| ErrorEventEmitter | High | - | **DELETE** - Merge into IMessageBus |
| ServiceErrorPublisher | High | - | **DELETE** - Unnecessary layer |
| IDistributedLockProvider | High | 2 hours | **Move to lib-state** |
| ServiceHeartbeatManager | High | 3 hours | **Simplify significantly** |
| DaprClientEventPublisher | High | 1 hour | **Simplify** - Use IMessageBus |
| Program.cs DaprClient | High | 2 hours | Remove all DaprClient usage |
| Schema server URLs | High | 2 hours | **Change** to `http://localhost:5012` |
| InvokeAppIdRewriteMiddleware | High | 30 min | **DELETE** - No longer needed |
| UseCloudEvents/MapSubscribeHandler | High | 30 min | **DELETE** - Dapr-specific |
| IDaprService naming | Medium | 2 hours | **Rename** to IBannouService |
| Remove Dapr NuGet packages | Low | 30 min | Final step |

---

## Deep Architectural Analysis

### 1. IErrorEventEmitter → IMessageBus.TryPublishErrorAsync - **DELETE Interface**

**Problem Analysis**: Error publishing is a subset of event publishing - having a separate `IErrorEventEmitter` interface is over-abstraction that provides no real benefit while adding DI complexity.

**Current Usage**: 130+ calls across all services
```csharp
await _errorEventEmitter.TryPublishAsync(
    serviceId: "documentation",
    operation: "CreateDocument",
    errorType: "unexpected_exception",
    message: ex.Message,
    stack: ex.StackTrace);
```

**Current Architecture (BAD)** - 3 unnecessary layers:
```
Service → IErrorEventEmitter → ErrorEventEmitter → ServiceErrorPublisher → DaprClient
```

**New Architecture (CORRECT)** - Direct use of existing interface:
```
Service → IMessageBus.TryPublishErrorAsync()
```

**Decision**: **DELETE ALL** - Add method to IMessageBus instead
- ❌ **DELETE** `IErrorEventEmitter` interface - unnecessary abstraction
- ❌ **DELETE** `ErrorEventEmitter` class - unnecessary implementation
- ❌ **DELETE** `ServiceErrorPublisher` class - unnecessary layer
- ✅ **ADD** `TryPublishErrorAsync` method to `IMessageBus` (same signature)

**Why This Is Better**:
1. Error publishing IS event publishing - same infrastructure, same topic, same format
2. Reduces DI complexity - one less interface to inject everywhere
3. Simpler mental model - `_messageBus` handles all messaging
4. Mass-replacement is trivial: `_errorEventEmitter.TryPublishAsync` → `_messageBus.TryPublishErrorAsync`

**Implementation - Add to IMessageBus**:
```csharp
// bannou-service/Services/IMessageBus.cs - ADD method
public interface IMessageBus
{
    // ... existing methods ...

    /// <summary>
    /// Publish a service error event. Convenience method with same signature as former IErrorEventEmitter.
    /// </summary>
    Task<bool> TryPublishErrorAsync(
        string serviceId,
        string operation,
        string errorType,
        string message,
        string? dependency = null,
        string? endpoint = null,
        ServiceErrorEventSeverity severity = ServiceErrorEventSeverity.Error,
        object? details = null,
        string? stack = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);
}
```

**Implementation - Add to MassTransitMessageBus**:
```csharp
// lib-messaging/Services/MassTransitMessageBus.cs - ADD method
private const string ERROR_TOPIC = "bannou-service-errors";

public async Task<bool> TryPublishErrorAsync(
    string serviceId, string operation, string errorType, string message,
    string? dependency = null, string? endpoint = null,
    ServiceErrorEventSeverity severity = ServiceErrorEventSeverity.Error,
    object? details = null, string? stack = null, string? correlationId = null,
    CancellationToken cancellationToken = default)
{
    try
    {
        var errorEvent = new ServiceErrorEvent
        {
            EventId = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow,
            ServiceId = serviceId,
            Operation = operation,
            Error_type = errorType,
            Message = message,
            Dependency = dependency,
            Endpoint = endpoint,
            Severity = severity,
            Stack = stack,
            CorrelationId = correlationId
        };

        await PublishAsync(ERROR_TOPIC, errorEvent, null, cancellationToken);
        return true;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to publish error event for {ServiceId}/{Operation}", serviceId, operation);
        return false;
    }
}
```

**Mass Replacement Strategy** (130+ usages):
```bash
# Step 1: Replace all usages (same signature, easy mass-replace)
find lib-* -name "*.cs" -exec sed -i 's/_errorEventEmitter\.TryPublishAsync/_messageBus.TryPublishErrorAsync/g' {} \;

# Step 2: Remove IErrorEventEmitter field declarations
find lib-* -name "*.cs" -exec sed -i '/private readonly IErrorEventEmitter _errorEventEmitter;/d' {} \;

# Step 3: Remove constructor parameter (more complex, may need manual review)
# Remove: IErrorEventEmitter errorEventEmitter,
# Remove: _errorEventEmitter = errorEventEmitter ?? throw new ArgumentNullException(nameof(errorEventEmitter));
```

**Files to Modify**:
- `bannou-service/Services/IMessageBus.cs` - Add TryPublishErrorAsync method
- `lib-messaging/Services/MassTransitMessageBus.cs` - Implement TryPublishErrorAsync
- `lib-*/` - All service implementations (mass-replace _errorEventEmitter → _messageBus)

**Files to Delete**:
- `bannou-service/Services/IErrorEventEmitter.cs` - Interface no longer needed
- `bannou-service/Services/ErrorEventEmitter.cs` - Implementation no longer needed
- `bannou-service/Events/ServiceErrorPublisher.cs` - Layer no longer needed

**Code Generation Updates** (`scripts/generate-implementation.sh`):

Current code generation injects IErrorEventEmitter - this must be updated:

```bash
# BEFORE (lines 60, 77, 85, 90):
using Dapr.Client;
private readonly IErrorEventEmitter _errorEventEmitter;
IErrorEventEmitter errorEventEmitter)
_errorEventEmitter = errorEventEmitter ?? throw new ArgumentNullException(nameof(errorEventEmitter));

# AFTER - Remove Dapr and IErrorEventEmitter, use infrastructure libs:
using BeyondImmersion.BannouService.Services;
private readonly IMessageBus _messageBus;
private readonly IStateStoreFactory _stateStoreFactory;
IMessageBus messageBus,
IStateStoreFactory stateStoreFactory)
_messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
_stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
```

Generated catch block (lines 249-257) changes from:
```csharp
await _errorEventEmitter.TryPublishAsync(...)
```
To:
```csharp
await _messageBus.TryPublishErrorAsync(...)
```

---

### 2. ServiceHeartbeatManager - SIGNIFICANT SIMPLIFICATION

**Current Functionality** (700+ lines):
1. Wait for Dapr connectivity on startup
2. Publish startup heartbeat
3. Verify Dapr subscriptions via metadata API
4. Periodic heartbeat publishing
5. Permission re-registration on heartbeat
6. Shutdown heartbeat
7. Service mapping change handling
8. Issue tracking and reporting

**Usage Analysis** (Program.cs):
```csharp
HeartbeatManager = new ServiceHeartbeatManager(DaprClient, heartbeatLogger, PluginLoader, mappingResolver, Configuration);
await HeartbeatManager.WaitForDaprConnectivityAsync(...);  // Startup check
HeartbeatManager.StartPeriodicHeartbeats();                // Periodic publishing
await HeartbeatManager.PublishShutdownHeartbeatAsync();    // Shutdown
```

**What We Actually Need**:
1. ✅ Startup connectivity check → Simple `IMessageBus.PublishAsync()` try/catch
2. ✅ Heartbeat publishing → `IMessageBus.PublishAsync()`
3. ❌ Dapr metadata API → **DELETE** - MassTransit handles subscriptions automatically
4. ✅ Periodic heartbeats → Simple timer with `IMessageBus.PublishAsync()`
5. ✅ Permission re-registration → Keep, already uses PluginLoader
6. ✅ Service mapping handling → Keep, useful for suppressing heartbeats

**What We DON'T Need**:
- HTTP client for Dapr metadata API queries
- `DiscoverExpectedSubscriptions()` - Dapr-specific
- `VerifySubscriptionsRegisteredAsync()` - Dapr-specific
- DaprClient dependency entirely

**Decision**: **SIMPLIFY** - Reduce to ~200 lines, remove Dapr-specific functionality

**Simplified Implementation**:
```csharp
public class ServiceHeartbeatManager : IAsyncDisposable
{
    private readonly IMessageBus _messageBus;
    private readonly ILogger<ServiceHeartbeatManager> _logger;
    private readonly PluginLoader _pluginLoader;
    private readonly IServiceAppMappingResolver _mappingResolver;
    private Timer? _heartbeatTimer;
    private const string HEARTBEAT_TOPIC = "bannou-service-heartbeats";

    public ServiceHeartbeatManager(
        IMessageBus messageBus,
        ILogger<ServiceHeartbeatManager> logger,
        PluginLoader pluginLoader,
        IServiceAppMappingResolver mappingResolver,
        AppConfiguration configuration)
    {
        _messageBus = messageBus;
        _logger = logger;
        _pluginLoader = pluginLoader;
        _mappingResolver = mappingResolver;
        // ... configuration reading
    }

    /// <summary>
    /// Verify messaging connectivity by attempting to publish.
    /// </summary>
    public async Task<bool> WaitForConnectivityAsync(int maxRetries = 30, int retryDelayMs = 2000, CancellationToken ct = default)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await PublishHeartbeatAsync(ServiceHeartbeatEventStatus.Healthy, ct);
                _logger.LogInformation("✅ Messaging connectivity confirmed on attempt {Attempt}", attempt);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Connectivity attempt {Attempt}/{MaxRetries} failed: {Error}", attempt, maxRetries, ex.Message);
                if (attempt < maxRetries) await Task.Delay(retryDelayMs, ct);
            }
        }
        return false;
    }

    public async Task PublishHeartbeatAsync(ServiceHeartbeatEventStatus status, CancellationToken ct = default)
    {
        var heartbeat = BuildHeartbeatEvent(status);
        await _messageBus.PublishAsync(HEARTBEAT_TOPIC, heartbeat, null, ct);
    }

    public void StartPeriodicHeartbeats()
    {
        _heartbeatTimer = new Timer(async _ => await PublishPeriodicHeartbeatAsync(), null,
            HeartbeatIntervalSeconds * 1000, HeartbeatIntervalSeconds * 1000);
    }

    // ... remaining simplified methods
}
```

**Files to Modify**:
- `bannou-service/Services/ServiceHeartbeatManager.cs` - Major rewrite (~500 lines removed)
- `bannou-service/Program.cs` - Pass IMessageBus instead of DaprClient

**Lines of Code Reduction**: ~700 → ~200 (70% reduction)

---

### 3. IDistributedLockProvider - MOVE Interface to Services/, Implementation to lib-state

**Current Usage**: Only PermissionsService uses distributed locking
```csharp
// lib-permissions/PermissionsService.cs:388
serviceLock = await _lockProvider.LockAsync(LOCK_STORE, LOCK_RESOURCE, lockOwnerId, expiryInSeconds: 30, cancellationToken);
```

**Purpose**: Prevent race conditions during permission registration (important!)

**Current Implementation Issues**:
- Interface + implementation in same file: `bannou-service/IDistributedLockProvider.cs` (wrong location)
- Implementation uses `DaprClient.GetStateAndETagAsync()` and `TrySaveStateAsync()`
- Should follow same pattern as IMessageBus, IStateStore, IMeshInvocationClient

**Decision**: **KEEP** interface name, **MOVE** to proper locations, **REIMPLEMENT** with lib-state

**Pattern Alignment** (same as other infrastructure interfaces):
```
bannou-service/Services/IMessageBus.cs              → lib-messaging/Services/MassTransitMessageBus.cs
bannou-service/Services/IStateStore.cs              → lib-state/Services/RedisStateStore.cs
bannou-service/Services/IMeshInvocationClient.cs    → lib-mesh/Services/YarpMeshInvocationClient.cs
bannou-service/Services/IDistributedLockProvider.cs → lib-state/Services/RedisDistributedLockProvider.cs (NEW)
```

**Interface Location Change**:
```
BEFORE: bannou-service/IDistributedLockProvider.cs           (root level, wrong)
AFTER:  bannou-service/Services/IDistributedLockProvider.cs  (Services/ directory, correct)
```

**Implementation** (using lib-state's Redis connection):
```csharp
// bannou-service/Services/IDistributedLockProvider.cs - Interface ONLY (move from root)
namespace BeyondImmersion.BannouService.Services;

public interface ILockResponse : IAsyncDisposable
{
    bool Success { get; }
}

public interface IDistributedLockProvider
{
    Task<ILockResponse> LockAsync(
        string storeName,
        string resourceId,
        string lockOwner,
        int expiryInSeconds,
        CancellationToken cancellationToken = default);
}
```

```csharp
// lib-state/Services/RedisDistributedLockProvider.cs - Implementation (native Redis)
using StackExchange.Redis;

namespace BeyondImmersion.BannouService.State;

public class RedisDistributedLockProvider : IDistributedLockProvider
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisDistributedLockProvider> _logger;

    public RedisDistributedLockProvider(IConnectionMultiplexer redis, ILogger<RedisDistributedLockProvider> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ILockResponse> LockAsync(
        string storeName, string resourceId, string lockOwner,
        int expiryInSeconds, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var lockKey = $"lock:{storeName}:{resourceId}";
        var lockValue = BannouJson.Serialize(new LockData { Owner = lockOwner, ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiryInSeconds) });

        // Redis SET NX EX pattern - atomic lock acquisition
        var acquired = await db.StringSetAsync(lockKey, lockValue, TimeSpan.FromSeconds(expiryInSeconds), When.NotExists);

        if (acquired)
        {
            _logger.LogDebug("Acquired lock {LockKey} for owner {Owner}", lockKey, lockOwner);
            return new RedisLockResponse(true, db, lockKey, lockOwner, _logger);
        }

        _logger.LogDebug("Failed to acquire lock {LockKey} - already held", lockKey);
        return new RedisLockResponse(false, db, lockKey, lockOwner, _logger);
    }

    private class LockData
    {
        public string Owner { get; set; } = "";
        public long ExpiresAtUnix { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public DateTimeOffset ExpiresAt
        {
            get => DateTimeOffset.FromUnixTimeSeconds(ExpiresAtUnix);
            set => ExpiresAtUnix = value.ToUnixTimeSeconds();
        }
    }
}

internal class RedisLockResponse : ILockResponse
{
    public bool Success { get; }
    private readonly IDatabase _db;
    private readonly string _lockKey;
    private readonly string _lockOwner;
    private readonly ILogger _logger;
    private bool _disposed;

    public RedisLockResponse(bool success, IDatabase db, string lockKey, string lockOwner, ILogger logger)
    {
        Success = success;
        _db = db;
        _lockKey = lockKey;
        _lockOwner = lockOwner;
        _logger = logger;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed || !Success) return;
        _disposed = true;

        try
        {
            await _db.KeyDeleteAsync(_lockKey);
            _logger.LogDebug("Released lock {LockKey} for owner {Owner}", _lockKey, _lockOwner);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error releasing lock {LockKey}", _lockKey);
        }
    }
}
```

**Files to Create**:
- `bannou-service/Services/IDistributedLockProvider.cs` - Interface ONLY (moved from root)
- `lib-state/Services/RedisDistributedLockProvider.cs` - Native Redis implementation

**Files to Modify**:
- `lib-state/StateServicePlugin.cs` - Register RedisDistributedLockProvider in DI
- `lib-permissions/PermissionsService.cs` - Update namespace import

**Files to Delete**:
- `bannou-service/IDistributedLockProvider.cs` - Replaced by Services/ version

**Standard DI Helper Pattern**: After this change, IDistributedLockProvider becomes one of the three standard infrastructure DI helpers that ALL services should inject:
1. `IMessageBus` - Event publishing (including error publishing via TryPublishErrorAsync)
2. `IStateStoreFactory` - State persistence (Redis, MySQL)
3. `IDistributedLockProvider` - Distributed locking (for any service that needs atomic operations)

---

### 4. IClientEventPublisher / DaprClientEventPublisher - SIMPLIFY

**Current Usage**: 8 usages across Permissions, Voice, Testing services
```csharp
await _clientEventPublisher.PublishToSessionAsync(sessionId, capabilitiesEvent);
await _clientEventPublisher.PublishToSessionsAsync(sessionIds, peerJoinedEvent, cancellationToken);
```

**Purpose**: Push events to specific WebSocket clients via session-specific RabbitMQ topics

**Current Problem**: `DaprClientEventPublisher` uses `DaprClient.PublishEventAsync()`

**Decision**: **KEEP** interface (well-designed), **SIMPLIFY** implementation

**Renamed Implementation**:
```csharp
// bannou-service/ClientEvents/MessageBusClientEventPublisher.cs
public class MessageBusClientEventPublisher : IClientEventPublisher
{
    private readonly IMessageBus _messageBus;
    private readonly ILogger<MessageBusClientEventPublisher> _logger;
    private const string SESSION_TOPIC_PREFIX = "CONNECT_SESSION_";

    public MessageBusClientEventPublisher(IMessageBus messageBus, ILogger<MessageBusClientEventPublisher> logger)
    {
        _messageBus = messageBus;
        _logger = logger;
    }

    public async Task<bool> PublishToSessionAsync<TEvent>(string sessionId, TEvent eventData, CancellationToken ct = default)
        where TEvent : BaseClientEvent
    {
        var eventName = GetEventName(eventData);
        if (!ClientEventWhitelist.IsValidEventName(eventName))
            throw new ArgumentException($"Unknown client event type: {eventName}");

        var topic = $"{SESSION_TOPIC_PREFIX}{sessionId}";
        await _messageBus.PublishAsync(topic, eventData, null, ct);
        _logger.LogDebug("Published client event {EventName} to session {SessionId}", eventName, sessionId);
        return true;
    }

    // ... PublishToSessionsAsync, IsValidEventName, GetEventName methods unchanged
}
```

**Files to Modify**:
- `bannou-service/ClientEvents/DaprClientEventPublisher.cs` - Rename and use IMessageBus
- `bannou-service/ClientEvents/ClientEventsDependencyInjection.cs` - Update registration

---

### 5. IDaprService Interface - RENAME to IBannouService

**Current State**: 450+ lines, provides essential service lifecycle functionality

**What It Does** (KEEP all of this):
- Service discovery via `[DaprServiceAttribute]`
- Heartbeat status reporting (`OnHeartbeat()`)
- Lifecycle methods (`OnStartAsync`, `OnRunningAsync`, `OnShutdownAsync`)
- Permission registration (`RegisterServicePermissionsAsync()`)
- Event consumer registration (`RegisterEventConsumers()`)
- Service enable/disable configuration
- Network mode presets

**Decision**: **RENAME** only, don't change functionality
- `IDaprService` → `IBannouService`
- `DaprServiceAttribute` → `BannouServiceAttribute`
- `DaprService` (base class if exists) → `BannouServiceBase`

**Scope of Rename** (grep results):
- ~50 files reference `IDaprService`
- ~25 files reference `DaprServiceAttribute`

**Approach**: Use `[Obsolete]` aliases for transition period:
```csharp
[Obsolete("Use IBannouService instead")]
public interface IDaprService : IBannouService { }

[Obsolete("Use BannouServiceAttribute instead")]
public class DaprServiceAttribute : BannouServiceAttribute { }
```

---

### 6. Program.cs Cleanup

**Current DaprClient Usages**:
```csharp
Line 81:  public static DaprClient DaprClient { get; private set; }
Line 113: var daprClientBuilder = new DaprClientBuilder();
Line 398: HeartbeatManager = new ServiceHeartbeatManager(DaprClient, ...);
Line 500: await DaprClient.DisposeAsync();
Line 733: await DaprClient.GetStateAsync<Dictionary<string, string>>("statestore", "service-mappings", ...);
```

**Replacement Strategy**:
1. Remove `DaprClient` property entirely
2. `ServiceHeartbeatManager` → Pass `IMessageBus` instead
3. State access → Use `IStateStoreFactory.GetStore<T>(storeName)`
4. Remove DaprClientBuilder and disposal

**Files to Modify**:
- `bannou-service/Program.cs`

---

## Implementation Plan

### Phase 1: Core Simplifications (Day 1) - 4 hours

**Step 1.1: IErrorEventEmitter → IMessageBus.TryPublishErrorAsync** (2 hours)
```bash
# Files to ADD method
bannou-service/Services/IMessageBus.cs           # Add TryPublishErrorAsync signature
lib-messaging/Services/MassTransitMessageBus.cs  # Add implementation

# Mass replacement in ALL lib-* services
find lib-* -name "*.cs" -exec sed -i 's/_errorEventEmitter\.TryPublishAsync/_messageBus.TryPublishErrorAsync/g' {} \;

# Remove field declarations and constructor params (manual review needed)
# Locations: All *Service.cs files in lib-* directories

# Files to DELETE
bannou-service/Services/IErrorEventEmitter.cs    # Interface no longer needed
bannou-service/Services/ErrorEventEmitter.cs     # Implementation no longer needed
bannou-service/Events/ServiceErrorPublisher.cs   # Layer no longer needed

# Update DI registration
bannou-service/Plugins/PluginLoader.cs           # Remove ErrorEventEmitter registration

# Update code generation
scripts/generate-implementation.sh               # Use IMessageBus, not IErrorEventEmitter
```

**Step 1.2: IDistributedLockProvider Migration** (2 hours)
```bash
# Move interface to correct location
mv bannou-service/IDistributedLockProvider.cs bannou-service/Services/IDistributedLockProvider.cs
# Edit to remove implementation, keep interface only
# Update namespace to BeyondImmersion.BannouService.Services

# Files to create
lib-state/Services/RedisDistributedLockProvider.cs       # Native Redis implementation (not Dapr)

# Files to modify
lib-state/StateServicePlugin.cs                          # Register RedisDistributedLockProvider
lib-permissions/PermissionsService.cs                    # Update namespace import
```

**Step 1.3: ClientEventPublisher Simplification** (1 hour)
```bash
# Files to modify (rename + use IMessageBus)
bannou-service/ClientEvents/DaprClientEventPublisher.cs → MessageBusClientEventPublisher.cs
bannou-service/ClientEvents/ClientEventsDependencyInjection.cs
```

**Step 1.4: Update Code Generation** (30 min)
```bash
# Update generate-implementation.sh to use standard DI helpers
scripts/generate-implementation.sh
```

**New code generation template** (replaces DaprClient + IErrorEventEmitter):
```csharp
// Generated service template - AFTER
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

[assembly: InternalsVisibleTo("lib-{service}.tests")]

namespace BeyondImmersion.BannouService.{ServicePascal};

[BannouService("{service}", typeof(I{ServicePascal}Service), lifetime: ServiceLifetime.Scoped)]
public class {ServicePascal}Service : I{ServicePascal}Service
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ILogger<{ServicePascal}Service> _logger;
    private readonly {ServicePascal}ServiceConfiguration _configuration;

    public {ServicePascal}Service(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        IDistributedLockProvider lockProvider,
        ILogger<{ServicePascal}Service> logger,
        {ServicePascal}ServiceConfiguration configuration)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
        _lockProvider = lockProvider ?? throw new ArgumentNullException(nameof(lockProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    // ... generated method stubs use:
    // - _messageBus.TryPublishErrorAsync(...) for error events
    // - _stateStoreFactory.Create<T>(storeName) for state stores
    // - _lockProvider.LockAsync(...) for distributed locking
}
```

### Phase 2: ServiceHeartbeatManager Simplification (Day 1-2) - 3 hours

```bash
# Major rewrite
bannou-service/Services/ServiceHeartbeatManager.cs       # Remove Dapr-specific code

# Update constructor call
bannou-service/Program.cs                                # Pass IMessageBus
```

**Key Changes**:
- Remove DaprClient dependency
- Remove Dapr metadata API calls
- Remove subscription verification
- Use IMessageBus for all publishing
- Reduce from ~700 lines to ~200 lines

### Phase 3: Program.cs DaprClient Removal (Day 2) - 2 hours

```bash
# Files to modify
bannou-service/Program.cs
```

**Changes**:
1. Remove `DaprClient` property
2. Remove `DaprClientBuilder` usage
3. Replace `LoadPersistedMappingsAsync` to use `IStateStoreFactory`
4. Update `ServiceHeartbeatManager` creation

### Phase 4: Naming Cleanup (Day 2-3) - 2 hours

```bash
# Core renames with obsolete aliases
bannou-service/Services/IDaprService.cs → IBannouService.cs
bannou-service/Attributes/DaprServiceAttribute.cs → BannouServiceAttribute.cs

# Update all references (can use sed/find-replace)
grep -rl "IDaprService" --include="*.cs" | xargs sed -i 's/IDaprService/IBannouService/g'
grep -rl "DaprServiceAttribute" --include="*.cs" | xargs sed -i 's/DaprServiceAttribute/BannouServiceAttribute/g'
```

### Phase 5: Schema URL and Routing Cleanup (Day 3) - 2 hours

**Current Problem**: All schemas use Dapr invoke prefix which generates unnecessary controller routes:
```yaml
# BEFORE - All schemas have this:
servers:
  - url: http://localhost:3500/v1.0/invoke/bannou/method
```

This generates controllers with route:
```csharp
[Route("v1.0/invoke/bannou/method")]  # Dapr baggage
```

And requires `InvokeAppIdRewriteMiddleware` to normalize paths.

**Solution**: Simplify schema URLs and remove middleware:
```yaml
# AFTER - Clean URLs:
servers:
  - url: http://localhost:5012
```

Generates controllers with route:
```csharp
[Route("")]  # No prefix needed
```

**Files to Modify**:
```bash
# Update all schema server URLs
schemas/accounts-api.yaml
schemas/auth-api.yaml
schemas/behavior-api.yaml
schemas/character-api.yaml
schemas/connect-api.yaml
schemas/documentation-api.yaml
schemas/game-session-api.yaml
schemas/location-api.yaml
schemas/mesh-api.yaml
schemas/messaging-api.yaml
schemas/orchestrator-api.yaml
schemas/permissions-api.yaml
schemas/realm-api.yaml
schemas/relationship-api.yaml
schemas/relationship-type-api.yaml
schemas/servicedata-api.yaml
schemas/species-api.yaml
schemas/state-api.yaml
schemas/subscriptions-api.yaml
schemas/voice-api.yaml
schemas/website-api.yaml

# Mass update command:
find schemas -name "*-api.yaml" -exec sed -i 's|url: http://localhost:3500/v1.0/invoke/bannou/method|url: http://localhost:5012|g' {} \;
```

**Files to Delete**:
```bash
# Middleware no longer needed
bannou-service/Middleware/InvokeAppIdRewriteMiddleware.cs
```

**Files to Modify - Program.cs**:
```csharp
// REMOVE these Dapr-specific lines:
webApp.UseCloudEvents();                    // Line ~334 - Dapr message format
endpointOptions.MapSubscribeHandler();      // Line ~343 - Dapr subscription discovery

// REMOVE middleware registration:
webApp.UseMiddleware<InvokeAppIdRewriteMiddleware>();  // No longer needed
```

**Regenerate All Services**:
```bash
# After schema changes, regenerate everything
./scripts/generate-all-services.sh
make format
make build
```

---

### Phase 6: Final NuGet Cleanup (Day 3) - 1 hour

```bash
# Remove Dapr NuGet packages
dotnet remove bannou-service/bannou-service.csproj package Dapr.AspNetCore
dotnet remove bannou-service/bannou-service.csproj package Dapr.Client
dotnet remove bannou-service/bannou-service.csproj package Dapr.Extensions.Configuration

# Remove http-tester Dapr dependencies
dotnet remove http-tester/http-tester.csproj package Dapr.AspNetCore
dotnet remove http-tester/http-tester.csproj package Dapr.Client

# Verify no Dapr references remain
grep -r "using Dapr" --include="*.cs" .
```

### Phase 7: Manual Constructor Updates (Day 3-4) - 4 hours

**CRITICAL**: All existing service implementations need constructor updates to use the three standard DI helpers instead of DaprClient and IErrorEventEmitter.

**Standard Infrastructure DI Pattern** (all services should have):
```csharp
public class {Service}Service : I{Service}Service
{
    // Standard infrastructure DI helpers
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IDistributedLockProvider _lockProvider;

    // Service-specific dependencies
    private readonly ILogger<{Service}Service> _logger;
    private readonly {Service}ServiceConfiguration _configuration;

    public {Service}Service(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        IDistributedLockProvider lockProvider,
        ILogger<{Service}Service> logger,
        {Service}ServiceConfiguration configuration)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
        _lockProvider = lockProvider ?? throw new ArgumentNullException(nameof(lockProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }
}
```

**Services Requiring Manual Updates**:

| Service | DaprClient | IErrorEventEmitter | Notes |
|---------|------------|-------------------|-------|
| AccountsService | ✅ Remove | ✅ Replace → _messageBus | Uses state store via lib-state already |
| AuthService | ✅ Remove | ✅ Replace → _messageBus | Has helper services (TokenService, SessionService) |
| BehaviorService | ✅ Remove | ✅ Replace → _messageBus | Stub only |
| CharacterService | ✅ Remove | ✅ Replace → _messageBus | |
| ConnectService | ✅ Remove | ✅ Replace → _messageBus | Complex - has many DI deps |
| DocumentationService | ✅ Remove | ✅ Replace → _messageBus | |
| GameSessionService | ✅ Remove | ✅ Replace → _messageBus | |
| LocationService | ✅ Remove | ✅ Replace → _messageBus | |
| OrchestratorService | ✅ Remove | ✅ Replace → _messageBus | Uses direct Redis already |
| PermissionsService | ✅ Remove | ✅ Replace → _messageBus | Already has IDistributedLockProvider |
| RealmService | ✅ Remove | ✅ Replace → _messageBus | |
| RelationshipService | ✅ Remove | ✅ Replace → _messageBus | |
| RelationshipTypeService | ✅ Remove | ✅ Replace → _messageBus | |
| ServicedataService | ✅ Remove | ✅ Replace → _messageBus | |
| SpeciesService | ✅ Remove | ✅ Replace → _messageBus | |
| StateService | ✅ Remove | ✅ Replace → _messageBus | Core infrastructure service |
| SubscriptionsService | ✅ Remove | ✅ Replace → _messageBus | |
| VoiceService | ✅ Remove | ✅ Replace → _messageBus | |
| WebsiteService | ✅ Remove | ✅ Replace → _messageBus | MVC integration |

**Update Process Per Service**:
```bash
# For each lib-{service}/{Service}Service.cs:

# 1. Remove DaprClient dependency
sed -i '/private readonly DaprClient _daprClient;/d' {file}
sed -i '/DaprClient daprClient,/d' {file}
sed -i '/_daprClient = daprClient/d' {file}

# 2. Replace IErrorEventEmitter with IMessageBus (if not already present)
sed -i 's/_errorEventEmitter\.TryPublishAsync/_messageBus.TryPublishErrorAsync/g' {file}
sed -i '/private readonly IErrorEventEmitter _errorEventEmitter;/d' {file}
sed -i '/IErrorEventEmitter errorEventEmitter,/d' {file}
sed -i '/_errorEventEmitter = errorEventEmitter/d' {file}

# 3. Ensure standard DI helpers are present (manual verification)
# - IMessageBus _messageBus
# - IStateStoreFactory _stateStoreFactory
# - IDistributedLockProvider _lockProvider

# 4. Update using statements
sed -i '/using Dapr\.Client;/d' {file}
# Ensure: using BeyondImmersion.BannouService.Services;
```

**Verification Checklist Per Service**:
- [ ] No `DaprClient` field or parameter
- [ ] No `IErrorEventEmitter` field or parameter
- [ ] Has `IMessageBus _messageBus`
- [ ] Has `IStateStoreFactory _stateStoreFactory`
- [ ] Has `IDistributedLockProvider _lockProvider`
- [ ] All `_errorEventEmitter.TryPublishAsync` → `_messageBus.TryPublishErrorAsync`
- [ ] No `using Dapr` statements
- [ ] Service compiles successfully

---

## Testing Strategy

### Unit Tests
- Mock `IMessageBus`, `IStateStoreFactory`, `IDistributedLockProvider`
- Verify correct topic names and event types
- Test lock acquisition/release patterns

### Integration Tests
- Run `make test-infrastructure` after Phase 1
- Run `make test-http` after Phase 3
- Run `make test-edge` after Phase 4
- Run `make all` after Phase 5

### Regression Verification
Each phase should end with successful `dotnet build` at minimum.

---

## Success Criteria

1. ✅ `grep -r "using Dapr" . --include="*.cs"` returns 0 results (entire codebase)
2. ✅ `grep -r "DaprClient" . --include="*.cs"` returns 0 results
3. ✅ `grep -r "IErrorEventEmitter" . --include="*.cs"` returns 0 results (interface deleted)
4. ✅ No Dapr NuGet packages in any .csproj files
5. ✅ All tests pass (`make all`)
6. ✅ Error publishing works via `IMessageBus.TryPublishErrorAsync()`
7. ✅ ServiceHeartbeatManager works with IMessageBus
8. ✅ Distributed locking works via lib-state (RedisDistributedLockProvider)
9. ✅ Client event publishing works via IMessageBus
10. ✅ Code generation uses infrastructure libs, not Dapr/IErrorEventEmitter
11. ✅ Schema server URLs are `http://localhost:5012` (no Dapr prefix)
12. ✅ `InvokeAppIdRewriteMiddleware.cs` deleted
13. ✅ No `UseCloudEvents()` or `MapSubscribeHandler()` calls in Program.cs
14. ✅ Controller routes are clean (no `v1.0/invoke/bannou/method` prefix)
15. ✅ ~720 lines of code removed via simplification

---

## Lines of Code Impact

| Component | Before | After | Reduction |
|-----------|--------|-------|-----------|
| ServiceHeartbeatManager | ~700 | ~200 | -500 (71%) |
| IErrorEventEmitter + ErrorEventEmitter + ServiceErrorPublisher | ~150 | 0 (DELETED) | -150 (100%) |
| IMessageBus.TryPublishErrorAsync (new method) | 0 | +30 | +30 (new) |
| IDistributedLockProvider | ~200 | ~150 | -50 (25%) |
| DaprClientEventPublisher | ~230 | ~180 | -50 (22%) |
| **Total** | ~1280 | ~560 | **-720 (56%)** |

**Note**: The IErrorEventEmitter approach eliminates 3 files entirely (interface + implementation + publisher) and adds ~30 lines to IMessageBus/MassTransitMessageBus. Net reduction is significant.

---

## Estimated Timeline

| Phase | Effort | Dependencies |
|-------|--------|--------------|
| Phase 1: Core Simplifications | 5.5 hours | None |
| Phase 2: ServiceHeartbeatManager | 3 hours | Phase 1 |
| Phase 3: Program.cs Cleanup | 2 hours | Phases 1, 2 |
| Phase 4: Naming Cleanup | 2 hours | Can parallel with 2-3 |
| Phase 5: Schema URL & Routing Cleanup | 2 hours | Phases 1-4 |
| Phase 6: Final NuGet Cleanup | 1 hour | Phase 5 |
| Phase 7: Manual Constructor Updates | 4 hours | Phases 1-6 |
| **Total** | **19.5 hours** (~3 days) | |

**Phase 1 Breakdown**:
- Step 1.1: IMessageBus.TryPublishErrorAsync (2 hours)
- Step 1.2: IDistributedLockProvider migration (2 hours)
- Step 1.3: ClientEventPublisher simplification (1 hour)
- Step 1.4: Code generation updates (30 min)

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| Breaking service communication | Test each phase incrementally |
| Lock semantics change | Extensive testing of PermissionsService registration |
| Message format incompatibility | BannouJson used consistently everywhere |
| Heartbeat timing issues | Verify periodic publishing works correctly |

---

## Original Plan Reference

The original Dapr replacement plan (`UPCOMING_PROPOSED_-_DAPR_REPLACEMENT_PLUGIN.md`) has been incorporated into this progress document. Key sections from the original plan that have been implemented:

- ✅ Section 5: lib-messaging (MassTransit implementation)
- ✅ Section 6: lib-state (Redis/MySQL implementations)
- ✅ Section 7.1-7.2: Orchestrator extensions (partial - heartbeats work)
- ⏳ Section 8: Migration strategy (this document)
- ⏳ Section 9: Rollback plan (defer until complete)

The original plan document can be archived after this transition is complete.
