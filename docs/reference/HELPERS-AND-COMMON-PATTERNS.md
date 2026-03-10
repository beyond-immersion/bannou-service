# Helpers & Common Patterns

> **Purpose**: A guide to shared helpers, common patterns, and test validators available in `bannou-service/` and `test-utilities/`. This is **not a rules document** — tenets define what you MUST do; this document shows you what's available to help you do it well.
>
> **When to Reference**: When implementing a new plugin, writing tests, or looking for an existing helper before writing custom code.

---

## Table of Contents

1. [State Store Helpers](#1-state-store-helpers)
2. [Event & Messaging Helpers](#2-event--messaging-helpers)
3. [Background Worker Helpers](#3-background-worker-helpers)
4. [DI Provider & Listener Interfaces](#4-di-provider--listener-interfaces)
5. [Variable Provider Cache](#5-variable-provider-cache)
6. [History & Compression Helpers](#6-history--compression-helpers)
7. [Client Event Publishing](#7-client-event-publishing)
8. [Service Client Helpers](#8-service-client-helpers)
9. [Enum Mapping](#9-enum-mapping)
10. [Telemetry](#10-telemetry)
11. [Shared Models & Enums](#11-shared-models--enums)
12. [Attributes](#12-attributes)
13. [Test Validators](#13-test-validators)
14. [Miscellaneous Helpers](#14-miscellaneous-helpers)
15. [Category B Deprecation Template](#15-category-b-deprecation-template)

---

## 1. State Store Helpers

### UpdateWithRetryAsync (Optimistic Concurrency)

**File**: `bannou-service/Services/StateStoreExtensions.cs`

Extension methods on `IStateStore<T>` that encapsulate the ETag-based optimistic concurrency retry loop. Two overloads:

**Overload 1: Pure mutation** — when the callback always mutates and never needs to skip or delete:

```csharp
var (result, entity) = await _stateStore.UpdateWithRetryAsync(
    BuildEntityKey(entityId),
    entity => { entity.Name = newName; entity.UpdatedAt = DateTimeOffset.UtcNow; },
    _configuration.MaxConcurrencyRetries,
    _logger,
    ct);

if (result == UpdateResult.NotFound) return (StatusCodes.NotFound, null);
if (result == UpdateResult.Conflict) return (StatusCodes.Conflict, null);
// result == UpdateResult.Success → entity is the saved state
```

**Overload 2: Validate-then-mutate** — when the callback needs to validate, skip, or conditionally delete:

```csharp
var (result, entity, errorStatus) = await _stateStore.UpdateWithRetryAsync(
    BuildEntityKey(entityId),
    async current =>
    {
        // Idempotency: already in desired state
        if (current.IsDeprecated)
            return MutationResult.SkipWith(StatusCodes.OK);

        // Validation: reject invalid transitions
        if (!current.CanDeprecate)
            return MutationResult.SkipWith(StatusCodes.BadRequest);

        // Mutate in place
        current.IsDeprecated = true;
        current.DeprecatedAt = DateTimeOffset.UtcNow;
        await Task.CompletedTask;
        return MutationResult.Mutated;
    },
    _configuration.MaxConcurrencyRetries,
    _logger,
    ct);

if (result == UpdateResult.ValidationFailed) return (errorStatus, null);
```

**Types**:
- `UpdateResult`: `Success`, `NotFound`, `Conflict`, `ValidationFailed`, `Deleted`
- `MutationOutcome`: `Mutated`, `Skip`, `Delete`
- `MutationResult`: Record struct with static factories — `MutationResult.Mutated`, `.Delete`, `.SkipWith(StatusCodes)`

**When to use**: Any read-modify-write loop with `GetWithETagAsync` + `TrySaveAsync`. Replaces 15-20 lines of retry boilerplate with 3-5 lines. Not suitable when: the loop has inter-service calls between get and save, dual save paths per iteration, or complex post-save branching.

### State Store Key Builders

Not a shared helper — each service defines its own. The **pattern** is standardized:

```csharp
private const string ENTITY_KEY_PREFIX = "entity:";

#region Key Building Helpers

internal static string BuildEntityKey(Guid id)
    => $"{ENTITY_KEY_PREFIX}{id}";

#endregion
```

**Rules**: `const` prefix, `internal static` visibility (for provider/test access), `Build` prefix (not `Get`), grouped in `#region`. See T6 in FOUNDATION.md for the full specification.

### State Store Interface Hierarchy

See T4 in [FOUNDATION.md](tenets/FOUNDATION.md) for the canonical interface hierarchy table and backend rules. Factory methods for quick reference:

| Factory Method | Interface | Backends |
|----------------|-----------|----------|
| `GetStore<T>()` | `IStateStore<T>` | All |
| `GetCacheableStore<T>()` | `ICacheableStateStore<T>` | Redis, InMemory |
| `GetQueryableStore<T>()` | `IQueryableStateStore<T>` | MySQL |
| `GetJsonQueryableStore<T>()` | `IJsonQueryableStateStore<T>` | MySQL |
| `GetSearchableStore<T>()` | `ISearchableStateStore<T>` | Redis+Search |
| `GetRedisOperations()` | `IRedisOperations` | Redis |

All stores use `StateStoreDefinitions` constants (schema-first).

### Distributed Locking

**Interface**: `IDistributedLockProvider` — Redis-backed distributed locks.

```csharp
var lockOwner = $"create-sub-{Guid.NewGuid():N}";
await using var lockResponse = await _lockProvider.LockAsync(
    StateStoreDefinitions.SubscriptionLock,
    $"account:{body.AccountId}:service:{body.ServiceId}",
    lockOwner,
    _configuration.LockTimeoutSeconds,
    cancellationToken: ct);
if (!lockResponse.Success) return (StatusCodes.Conflict, null);
```

Lock store names come from `StateStoreDefinitions` constants. Lock owner strings should be `$"{operation}-{Guid:N}"` for debuggability.

---

## 2. Event & Messaging Helpers

### Generated Event Publishers

**Files**: `plugins/lib-{service}/Generated/{Service}EventPublisher.cs`

Generated from `x-event-publications` in event schemas. Typed extension methods on `IMessageBus`:

```csharp
// Preferred: generated typed method
await _messageBus.PublishQuestAcceptedAsync(event, ct);

// Also acceptable: generated topic constant
await _messageBus.TryPublishAsync(QuestPublishedTopics.QuestAccepted, event, ct);
```

Inline topic strings are forbidden — always use generated publishers or constants.

**Parameterized topics**: Topics with runtime placeholders (e.g., `asset.processing.job.{poolType}`) generate two overloads — use the parameterized one:

```csharp
await _messageBus.PublishAssetProcessingJobDispatchedAsync(event, poolType, ct);
```

### Generated Topic Constants

**Files**: `plugins/lib-{service}/Generated/{Service}PublishedTopics.cs`

`public const string` fields for every topic a service publishes. Generated by `generate-published-topics.py`.

### IEventConsumer (Event Fan-Out)

**Files**: `bannou-service/Events/IEventConsumer.cs`, `EventConsumer.cs`, `EventConsumerExtensions.cs`

RabbitMQ allows one consumer per queue. `IEventConsumer` provides application-level fan-out: register handlers from multiple plugins for the same event.

```csharp
// In {Service}ServiceEvents.cs
protected void RegisterEventConsumers(IEventConsumer eventConsumer)
{
    eventConsumer.RegisterHandler<IMyService, AccountDeletedEvent>(
        "account.deleted",
        async (svc, evt) => await ((MyService)svc).HandleAccountDeletedAsync(evt));
}
```

Registration is idempotent. Handlers are failure-isolated. `IEventConsumer` is singleton.

### Account Deletion Cleanup Pattern

**Tenet**: T28 Account Deletion Cleanup Obligation (FOUNDATION.md)

Every service that stores data with account ownership (`ownerType: Account` or data keyed by `accountId`) MUST subscribe to `account.deleted` and clean up all account-owned data. This is the one entity where event-based cleanup is mandatory because lib-resource cannot work (privacy + hierarchy constraints).

**Reference implementation**: `plugins/lib-collection/CollectionServiceEvents.cs`

**Required structure** in `{Service}ServiceEvents.cs`:

```csharp
protected void RegisterEventConsumers(IEventConsumer eventConsumer)
{
    eventConsumer.RegisterHandler<IMyService, AccountDeletedEvent>(
        "account.deleted",
        async (svc, evt) => await ((MyService)svc).HandleAccountDeletedAsync(evt));
}

public async Task HandleAccountDeletedAsync(AccountDeletedEvent evt)
{
    using var activity = _telemetryProvider.StartActivity(
        "bannou.my-service", "MyService.HandleAccountDeleted");
    _logger.LogInformation("Handling account.deleted for account {AccountId}", evt.AccountId);
    try
    {
        await CleanupForAccountAsync(evt.AccountId);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to clean up data for account {AccountId}", evt.AccountId);
        await _messageBus.TryPublishErrorAsync(
            "my-service", "CleanupForAccount", ex.GetType().Name, ex.Message,
            endpoint: "account.deleted", details: $"accountId={evt.AccountId}",
            stack: ex.StackTrace);
    }
}
```

**Checklist**:

| Requirement | Why |
|-------------|-----|
| Telemetry span in handler | No generated controller boundary for event handlers |
| Top-level try-catch with error event | Same — must provide own catch-all |
| Per-item try-catch in cleanup loop | One corrupt record must not block all cleanup |
| Per-item failures at `Warning` level | Expected/recoverable; `Error` is for infrastructure failures |
| "Not found" is `Debug`, not `Warning` | Multi-node broadcast means another node may have already cleaned up |
| Publish `*.deleted` lifecycle events for each cleaned entity | Downstream consumers (lib-resource callbacks, caches) need to react |
| Add `account-events.yaml` to `x-event-subscriptions` | Schema declares the subscription |

**Multi-node idempotency**: `account.deleted` is a broadcast event — all nodes receive it. State store deletes are naturally idempotent. Handlers must treat "not found" as success, not error.

### Error Event Publishing

**Method**: `IMessageBus.TryPublishErrorAsync(...)` — always safe to call (internal catch prevents propagation).

```csharp
await _messageBus.TryPublishErrorAsync(
    "character",           // serviceName
    "DeleteCharacter",     // operation
    ex.GetType().Name,     // errorType
    ex.Message,            // message
    dependency: "state",   // optional
    endpoint: "redis",     // optional
    stack: ex.StackTrace); // optional
```

Instance identity (`serviceId`, `appId`) is injected internally from `IMeshInstanceIdentifier` — never provide it manually. Never construct `ServiceErrorEvent` directly.

---

## 3. Background Worker Helpers

### WorkerErrorPublisher

**File**: `bannou-service/Services/WorkerErrorPublisher.cs`

Extension method on `IServiceProvider` for background workers (which can't constructor-inject scoped `IMessageBus`):

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "{Worker} cycle failed", nameof(MyWorker));
    await _serviceProvider.TryPublishWorkerErrorAsync(
        "my-service", "ProcessCycle", ex, _logger, stoppingToken);
}
```

Handles scope creation, `IMessageBus` resolution, and the safety catch-all. Workers that have singleton `IMessageBus` access (L0 infrastructure workers like HeartbeatEmitter) call `_messageBus.TryPublishErrorAsync` directly instead.

### Background Worker Polling Loop Pattern

Every `BackgroundService.ExecuteAsync` follows a canonical skeleton with specific cancellation handling. The key structural requirements:

1. **Startup delay** — configurable, with its own `catch (OperationCanceledException) { return; }`
2. **Double-catch filter** — `catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)` BEFORE generic `catch (Exception)` to prevent shutdown being logged as error
3. **Per-cycle telemetry** — `StartActivity` on each cycle, NOT on the `ExecuteAsync` method itself
4. **Error event publishing** — via `WorkerErrorPublisher.TryPublishWorkerErrorAsync`
5. **Delay after work** — with its own cancellation handler

**Constructor dependencies** (standardized):
```csharp
public MyWorker(
    IServiceProvider serviceProvider,       // scope creation
    ILogger<MyWorker> logger,
    MyServiceConfiguration configuration,
    ITelemetryProvider telemetryProvider)    // per-cycle spans
```

**Store access**: Resolve `IStateStoreFactory` from the DI scope once per cycle, call `GetStore<T>()` for each needed store immediately, pass store references as parameters to sub-methods. Never store `IStateStoreFactory` as a field or re-resolve it per sub-method.

See T6 in FOUNDATION.md for the complete canonical skeleton with code.

---

## 4. DI Provider & Listener Interfaces

Interfaces in `bannou-service/Providers/` that enable cross-layer communication without hierarchy violations. Higher-layer services implement and register them; lower-layer services discover via `IEnumerable<T>`.

### Provider Interfaces (Pull — Always Distributed-Safe)

| Interface | Direction | Purpose | Consumers |
|-----------|-----------|---------|-----------|
| `IVariableProviderFactory` | L4→L2 | Actor pulls character data from L4 | Actor runtime |
| `IPrerequisiteProviderFactory` | L4→L2 | Quest pulls prerequisite checks from L4 | Quest service |
| `IBehaviorDocumentProvider` | L4→L2 | Actor pulls behavior docs from L4 | Actor runtime |
| `ISeededResourceProvider` | L2/L3/L4→L1 | Resource discovers embedded resources | Resource service |
| `ITransitCostModifierProvider` | L4→L2 | Transit pulls cost modifiers from L4 | Transit service |

**Shape**: Interface in `bannou-service/Providers/`, higher layer implements and registers as Singleton, lower layer discovers via `IEnumerable<T>` with graceful degradation.

### Listener Interfaces (Push — Local-Only Fan-Out)

| Interface | Direction | Purpose | Distributed-Safe? |
|-----------|-----------|---------|-------------------|
| `ISeedEvolutionListener` | L2→L4 | Seed pushes evolution notifications | Safe when writing to distributed state |
| `ICollectionUnlockListener` | L2→L4 | Collection pushes unlock notifications | Same |
| `ISessionActivityListener` | L1→L1 | Connect pushes session lifecycle | Same |
| `IItemInstanceDestructionListener` | L2→L4 | Item pushes instance destruction | Same |

**Critical**: Listeners fire only on the node that processed the request. Reactions MUST write to distributed state (Redis/MySQL). For cross-node cache invalidation, use broadcast events via `IEventConsumer` instead.

### Other Provider-Like Interfaces

| Interface | File | Purpose |
|-----------|------|---------|
| `IEntitySessionRegistry` | `Providers/` | Route events to WebSocket sessions by entity type/ID |
| `IUnhandledExceptionHandler` | `Providers/` | Plugin hook for unhandled exceptions |
| `EmbeddedResourceProvider` | `Providers/` | Load embedded assembly resources |

---

## 5. Variable Provider Cache

### VariableProviderCacheBucket

**File**: `bannou-service/Providers/VariableProviderCacheBucket.cs`

Composition helper for `ConcurrentDictionary`-based caches used by Variable Provider implementations. Encapsulates: entry storage, TTL-based expiry, stale-data fallback on load failure, and invalidation.

```csharp
// In your cache class constructor:
_personalityBucket = new VariableProviderCacheBucket<Guid, PersonalityData>(
    scopeFactory,
    async (sp, key, ct) =>
    {
        var client = sp.GetRequiredService<ICharacterPersonalityClient>();
        var (status, response) = await client.GetPersonalityAsync(
            new GetPersonalityRequest { CharacterId = key }, ct);
        return status == StatusCodes.OK ? MapToData(response) : null;
    },
    TimeSpan.FromMinutes(configuration.PersonalityCacheTtlMinutes));

// Usage:
var data = await _personalityBucket.GetOrLoadAsync(characterId, ct);
_personalityBucket.Invalidate(characterId);
_personalityBucket.InvalidateAll();
```

Eliminates duplicate `CachedEntry` record types and ~80% of get-or-load boilerplate. Not suitable for: nested dictionary caches, multi-param load functions, or caches without stale fallback.

**Cache invalidation**: Caches using `ConcurrentDictionary` MUST invalidate via self-event-subscription (subscribe to own `*.updated` events via `IEventConsumer`), NOT inline invalidation at mutation sites. Inline invalidation only reaches the processing node.

---

## 6. History & Compression Helpers

Shared helpers in `bannou-service/History/` used by character-personality, character-history, character-encounter, and realm-history.

### CompressionHelper

**File**: `bannou-service/History/CompressionHelper.cs`

Decompresses JSON data from lib-resource compression callbacks:

```csharp
var data = CompressionHelper.DecompressJsonData<MyModel>(compressedBytes);
```

### DualIndexHelper

**File**: `bannou-service/History/DualIndexHelper.cs`

Manages entities stored under two keys (primary ID + secondary lookup key) with coordinated save/delete and distributed locking.

### BackstoryStorageHelper

**File**: `bannou-service/History/BackstoryStorageHelper.cs` (+ `IBackstoryStorageHelper`)

Shared storage pattern for backstory-type data (list of typed elements) used by character-history and realm-history.

### PaginationHelper

**File**: `bannou-service/History/PaginationHelper.cs`

Offset-based pagination with metadata:

```csharp
var result = PaginationHelper.Paginate(allItems, body.Page, body.PageSize);
// result.Items, result.TotalCount, result.Page, result.PageSize,
// result.HasNextPage, result.HasPreviousPage, result.TotalPages
```

Defaults: PageSize=20, MaxPageSize=100. Not suitable for cursor-based pagination.

### TimestampHelper

**File**: `bannou-service/History/TimestampHelper.cs`

Standardized timestamp operations for history services.

---

## 7. Client Event Publishing

### IClientEventPublisher

**Files**: `bannou-service/ClientEvents/IClientEventPublisher.cs`, `MessageBusClientEventPublisher.cs`

Pushes events to WebSocket clients via the `bannou-client-events` direct exchange (per-session routing). NOT the same as `IMessageBus` (which uses the `bannou` fanout exchange for service-to-service events).

```csharp
// Push to specific session
await _clientEventPublisher.PublishToSessionAsync(sessionId, clientEvent);

// Push to all sessions for an entity
await _entitySessionRegistry.PublishToEntitySessionsAsync("account", accountId, clientEvent);
```

Client events are defined in `{service}-client-events.yaml` and generate models in `{Service}ClientEventsModels.cs`.

---

## 8. Service Client Helpers

### ServiceClientExtensions

**File**: `bannou-service/ServiceClients/ServiceClientExtensions.cs`

Extension methods for generated service clients.

### ServiceRequestContext

**File**: `bannou-service/ServiceClients/ServiceRequestContext.cs`

Middleware that captures and forwards request context (session IDs, correlation IDs) through service-to-service mesh calls.

### SessionIdForwardingHandler

**File**: `bannou-service/ServiceClients/SessionIdForwardingHandler.cs`

HTTP message handler that forwards session IDs through the mesh invocation chain.

---

## 9. Enum Mapping

### EnumMapping Extensions

**File**: `bannou-service/EnumMapping.cs`

Shared extension methods for mapping between enum types at SDK/plugin boundaries:

| Method | Purpose |
|--------|---------|
| `MapByName<TSource, TTarget>()` | Name-matching conversion (throws on no match) |
| `MapByNameOrDefault<TSource, TTarget>(fallback)` | Name-matching with fallback for superset→subset |
| `TryMapByName<TSource, TTarget>(out result)` | Non-throwing name-matching |

```csharp
// A2 SDK boundary mapping
var quality = sdkChord.Quality.MapByName<MusicTheory.ChordQuality, ChordSymbolQuality>();

// Superset → subset with fallback
var ownerType = entityType.MapByNameOrDefault<EntityType, ContainerOwnerType>(
    ContainerOwnerType.Other);
```

Every mapping MUST have a corresponding `EnumMappingValidator` test (see [Test Validators](#13-test-validators)).

---

## 10. Telemetry

### ITelemetryProvider

**File**: `bannou-service/Services/ITelemetryProvider.cs`

L0 infrastructure dependency. All async methods in service code need a span:

```csharp
using var activity = _telemetryProvider.StartActivity(
    "bannou.matchmaking", "TicketResolver.ResolveTicket");
```

Spans nest automatically via `Activity.Current` (no parameter passing needed). Returns null when telemetry is disabled (all `?.SetTag` calls no-op). See T30 in IMPLEMENTATION-BEHAVIOR.md for scope rules and naming conventions.

**When NOT to add spans**: Primary interface methods in `*Service.cs` (generated controller already wraps these), pure synchronous methods, and trivial property accessors.

### NullTelemetryProvider

**File**: `bannou-service/Services/NullTelemetryProvider.cs`

No-op implementation used when telemetry is disabled. All methods are no-ops.

---

## 11. Shared Models & Enums

### StatusCodes

**File**: `bannou-service/Enums.cs` — `BeyondImmersion.BannouService.StatusCodes`

All service methods return `(StatusCodes, TResponse?)` tuples. Use this, NOT `Microsoft.AspNetCore.Http.StatusCodes`.

### ServiceLayer

**File**: `bannou-service/Enums.cs`

Enum defining the six hierarchy layers: `Infrastructure`, `AppFoundation`, `GameFoundation`, `AppFeatures`, `GameFeatures`, `Extensions`.

### HttpMethodTypes

**File**: `bannou-service/Enums.cs`

### ErrorResponses

**File**: `bannou-service/Helpers/ErrorResponses.cs`

Standardized error response helpers.

### MetadataHelper

**File**: `bannou-service/MetadataHelper.cs`

Helpers for working with opaque client metadata (`additionalProperties: true` fields).

### BannouJson

Serialization/deserialization MUST always use `BannouJson.Serialize/Deserialize` — never direct `System.Text.Json.JsonSerializer` calls.

---

## 12. Attributes

### BannouServiceAttribute

**File**: `bannou-service/Attributes/BannouServiceAttribute.cs`

Marks service implementation classes for automatic DI discovery:

```csharp
[BannouService("location", typeof(ILocationService),
    lifetime: ServiceLifetime.Scoped,
    layer: ServiceLayer.GameFoundation)]
public partial class LocationService : ILocationService { }
```

### ServiceConfigurationAttribute

**File**: `bannou-service/Attributes/ServiceConfigurationAttribute.cs`

Marks configuration classes with env prefix binding:

```csharp
[ServiceConfiguration(envPrefix: "BANNOU_")]
public class LocationServiceConfiguration { }
```

### ResourceCleanupRequiredAttribute

**File**: `bannou-service/Attributes/ResourceCleanupRequiredAttribute.cs`

Generated onto service classes that declare `x-references` in their API schema. Lists the cleanup callback method names that must be implemented.

### ParameterizedTopicAttribute

**File**: `bannou-service/Attributes/ParameterizedTopicAttribute.cs`

Generated onto parameterized event publisher overloads.

### Configuration Validation Attributes

| Attribute | File | Purpose |
|-----------|------|---------|
| `ConfigRequiredAttribute` | `Attributes/` | Marks required config properties |
| `ConfigRangeAttribute` | `Attributes/` | Numeric range validation |
| `ConfigPatternAttribute` | `Attributes/` | Regex pattern validation |
| `ConfigStringLengthAttribute` | `Attributes/` | String length validation |
| `ConfigMultipleOfAttribute` | `Attributes/` | Numeric multiple-of validation |

---

## 13. Test Validators

Shared validators in `test-utilities/` that provide one-line structural tests.

### ServiceConstructorValidator

**File**: `test-utilities/ServiceConstructorValidator.cs`

Validates: single public constructor, no optional params, no defaults, proper null checks.

```csharp
[Fact]
public void MyService_ConstructorIsValid() =>
    ServiceConstructorValidator.ValidateServiceConstructor<MyService>();
```

### StateStoreKeyValidator

**File**: `test-utilities/StateStoreKeyValidator.cs`

Validates: `const` prefix fields exist, `Build*Key()` methods exist, visibility is `internal static`.

```csharp
[Fact]
public void MyService_HasValidKeyBuilders() =>
    StateStoreKeyValidator.ValidateKeyBuilders<MyService>();
```

### ServiceHierarchyValidator

**File**: `test-utilities/ServiceHierarchyValidator.cs`

Validates constructor `I*Client` parameters against the declared `ServiceLayer` to catch hierarchy violations.

```csharp
[Fact]
public void MyService_RespectsDependencyHierarchy() =>
    ServiceHierarchyValidator.ValidateServiceHierarchy<MyService>();
```

### EnumMappingValidator

**File**: `test-utilities/EnumMappingValidator.cs`

Validates enum boundary mappings at test time:

| Method | When to Use |
|--------|-------------|
| `AssertFullCoverage<TA, TB>()` | Identical enums at A2 boundaries |
| `AssertSupersetToSubsetMapping<TSuper, TSub>(extras...)` | SDK has extra values not in schema |
| `AssertSubset<TSub, TSuper>()` | One-directional subset check |
| `AssertSwitchCoversAllValues<T>(func)` | Lossy switch expression coverage |

```csharp
[Fact]
public void ArcType_FullCoverage() =>
    EnumMappingValidator.AssertFullCoverage<ArcType, StorylineTheory.ArcType>();
```

### EventPublishingValidator

**File**: `test-utilities/EventPublishingValidator.cs`

Structural test that every generated `Publish*Async` method is called from the plugin assembly.

### ResourceCleanupValidator

**File**: `test-utilities/ResourceCleanupValidator.cs`

Validates that services with `[ResourceCleanupRequired]` attributes implement the declared cleanup methods.

### PermissionMatrixValidator

**File**: `test-utilities/PermissionMatrixValidator.cs`

Validates permission matrix registration.

### ControllerValidator

**File**: `test-utilities/ControllerValidator.cs`

Validates generated controller structure.

### TestConfigurationHelper

**File**: `test-utilities/TestConfigurationHelper.cs`

Helpers for creating test configuration instances.

---

## 14. Miscellaneous Helpers

### AppConstants

**File**: `bannou-service/AppConstants.cs`

Shared constants used across the platform. Contains default infrastructure names, ports, environment variable name constants, and protocol constants.

| Constant | Value | Purpose |
|----------|-------|---------|
| `DEFAULT_APP_NAME` | `"bannou"` | Omnipotent routing fallback (use `Program.Configuration.EffectiveAppId` for routing decisions) |
| `ORCHESTRATOR_SERVICE_NAME` | `"orchestrator"` | Logical name for orchestrator control plane |
| `PUBSUB_NAME` | `"bannou-pubsub"` | Default pub/sub component name |
| `DEFAULT_REDIS_PORT` | `6379` | Redis connection default |
| `DEFAULT_RABBITMQ_PORT` | `5672` | RabbitMQ AMQP default |
| `DEFAULT_BANNOU_HTTP_PORT` | `3500` | Service mesh HTTP default |
| `ENV_BANNOU_APP_ID` | `"BANNOU_APP_ID"` | Env var name for app ID (used by test runners per T21 exception 4) |
| `ENV_BANNOU_HTTP_ENDPOINT` | `"BANNOU_HTTP_ENDPOINT"` | Env var name for HTTP endpoint (test runners) |
| `BROADCAST_GUID` | `FFFFFFFF-...` | Special GUID for broadcast messages in Relayed/Internal connection modes |

### ExtensionMethods

**File**: `bannou-service/ExtensionMethods.cs`

General-purpose extension methods used across the codebase.

### TemplateSubstitutor

**File**: `bannou-service/Utilities/TemplateSubstitutor.cs`

Template string substitution for payload templates (e.g., `{{resourceId}}` in `x-references` cleanup payloads).

### ResponseValidator

**File**: `bannou-service/Utilities/ResponseValidator.cs`

Validation helpers for response data.

### GuidGenerator

**File**: `bannou-service/Protocol/GuidGenerator.cs`

Shared GUID generation with deterministic/shared modes for multi-instance safety. Security-critical: use `GetSharedServerSalt()` (never per-instance random generation).

### IEmailService

**Files**: `bannou-service/Services/IEmailService.cs`, `ConsoleEmailService.cs`, `SendGridEmailService.cs`, `SesEmailService.cs`, `SmtpEmailService.cs`

Email abstraction with multiple backend implementations.

### IMeshInstanceIdentifier

**File**: `bannou-service/Services/IMeshInstanceIdentifier.cs`

Provides the process-stable instance identity for error events and distributed debugging. Priority: `MESH_INSTANCE_ID` env var > `--force-service-id` CLI > random GUID (stable for process lifetime).

### IPermissionRegistry

**File**: `bannou-service/Services/IPermissionRegistry.cs`

Interface for services to register their permission matrices at startup.

### IServiceAppMappingResolver

**File**: `bannou-service/Services/IServiceAppMappingResolver.cs`

Resolves service names to app-ids for mesh routing.

---

## 15. Category B Deprecation Template

Category B entities are content templates where instances persist independently — the template must remain readable forever because historical instances reference it. T31 defines the rules; this section provides the copy-paste reference implementation. See [IMPLEMENTATION-BEHAVIOR.md § Category B Reference Pattern](tenets/IMPLEMENTATION-BEHAVIOR.md#category-b-reference-pattern-checklist) for the full checklist.

**Reference implementations**: `lib-item` (Item Template) for lifecycle infrastructure and storage, `lib-collection` (Collection Entry Template) for correct idempotent endpoint pattern.

### API Schema Pattern (`{service}-api.yaml`)

#### Deprecate Endpoint

```yaml
  /myservice/template/deprecate:
    post:
      operationId: deprecateTemplate
      tags:
      - Template
      summary: Deprecate a template
      description: |
        Marks a template as deprecated. Deprecated templates cannot be used
        to create new instances, but existing instances remain valid.
        Category B deprecation (per IMPLEMENTATION TENETS): one-way, no undeprecate,
        no delete. Idempotent — returns OK if already deprecated.
      x-permissions:
      - role: developer
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/DeprecateTemplateRequest'
      responses:
        '200':
          description: Template deprecated successfully (or already deprecated)
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/TemplateResponse'
        '404':
          description: Template not found
        # NO '409' — idempotent deprecation returns OK when already deprecated
```

#### Deprecate Request Model

```yaml
    DeprecateTemplateRequest:
      type: object
      description: Request to deprecate a template (Category B — one-way, no delete)
      additionalProperties: false
      required:
      - templateId
      properties:
        templateId:
          type: string
          format: uuid
          description: Template to deprecate
        reason:
          type: string
          nullable: true
          maxLength: 500
          description: Reason for deprecation (recommended for audit trail)
```

#### Response Model Deprecation Fields

Add these fields to the response model alongside existing entity fields:

```yaml
    TemplateResponse:
      type: object
      # ... other properties ...
      properties:
        # ... entity-specific fields ...
        isDeprecated:
          type: boolean
          description: Whether this template is deprecated
        deprecatedAt:
          type: string
          format: date-time
          nullable: true
          description: When this template was deprecated (null if not deprecated)
        deprecationReason:
          type: string
          nullable: true
          maxLength: 500
          description: Reason for deprecation (null if not deprecated)
```

**Status enum alternative**: Use ONLY when the entity has a Draft lifecycle state that the triple-field model cannot represent. The enum must include `Deprecated` as a terminal value:

```yaml
    TemplateStatus:
      type: string
      description: Template lifecycle status
      enum: [Draft, Active, Deprecated]
```

When using the status enum, the response model uses `status` instead of `isDeprecated`, but `deprecatedAt` and `deprecationReason` are still recommended as separate fields for audit purposes.

#### List Endpoint `includeDeprecated` Parameter

Add to the list/query request model:

```yaml
    ListTemplatesRequest:
      type: object
      properties:
        # ... other filters ...
        includeDeprecated:
          type: boolean
          default: false
          description: Include deprecated templates in results (excluded by default)
```

### Events Schema Pattern (`{service}-events.yaml`)

#### x-event-publications

```yaml
info:
  x-event-publications:
    # Template lifecycle events (auto-generated from x-lifecycle)
    - topic: myservice.template.created
      event: MyTemplateCreatedEvent
      description: Published when a new template is created
    - topic: myservice.template.updated
      event: MyTemplateUpdatedEvent
      description: Published when a template is updated (including deprecation via changedFields)
    # NOTE: *.deleted is unused Category B infrastructure — exists for future safe deletion pattern
    # Do not remove; do not count as a "published event"; structural tests should exclude it
```

**Topic naming**: Use dot-separated Pattern C per T16: `{service}.{entity}.{action}`. NOT hyphenated Pattern B (`service-entity.action`).

#### x-lifecycle

```yaml
x-lifecycle:
  topic_prefix: myservice
  MyTemplate:
    model:
      templateId: { type: string, format: uuid, primary: true, required: true, description: "Unique template identifier" }
      code: { type: string, required: true, description: "Unique template code" }
      # ... entity-specific fields ...
      isDeprecated: { type: boolean, required: true, description: "Whether template is deprecated" }
      deprecatedAt: { type: string, format: date-time, nullable: true, description: "When the template was deprecated" }
      deprecationReason: { type: string, nullable: true, description: "Reason for deprecation" }
    sensitive: []
```

### Implementation Pattern (`{Service}Service.cs`)

#### Deprecate Method

```csharp
public async Task<(StatusCodes, TemplateResponse?)> DeprecateTemplateAsync(
    DeprecateTemplateRequest body, CancellationToken ct)
{
    var key = BuildTemplateKey(body.TemplateId);
    var template = await _templateStore.GetAsync(key, ct);
    if (template == null)
        return (StatusCodes.NotFound, null);

    // Idempotent: already deprecated = OK (per IMPLEMENTATION TENETS)
    if (template.IsDeprecated)
        return (StatusCodes.OK, MapToResponse(template));

    template.IsDeprecated = true;
    template.DeprecatedAt = DateTimeOffset.UtcNow;
    template.DeprecationReason = body.Reason;
    template.UpdatedAt = DateTimeOffset.UtcNow;

    await _templateStore.SaveAsync(key, template, ct);

    // Publish as *.updated with changedFields — NOT a dedicated deprecation event
    await _messageBus.PublishTemplateUpdatedAsync(template,
        changedFields: new[] { "isDeprecated", "deprecatedAt", "deprecationReason" });

    return (StatusCodes.OK, MapToResponse(template));
}
```

#### Instance Creation Guard

```csharp
public async Task<(StatusCodes, InstanceResponse?)> CreateInstanceAsync(
    CreateInstanceRequest body, CancellationToken ct)
{
    var templateKey = BuildTemplateKey(body.TemplateId);
    var template = await _templateStore.GetAsync(templateKey, ct);
    if (template == null)
        return (StatusCodes.NotFound, null);

    // Category B instance creation guard (per IMPLEMENTATION TENETS)
    if (template.IsDeprecated)
        return (StatusCodes.BadRequest, null);

    // ... create instance logic ...
}
```

#### List Filtering

```csharp
public async Task<(StatusCodes, ListTemplatesResponse?)> ListTemplatesAsync(
    ListTemplatesRequest body, CancellationToken ct)
{
    var templates = await _templateStore.QueryAsync(/* ... */, ct);

    // Filter deprecated unless explicitly requested (per IMPLEMENTATION TENETS)
    if (!body.IncludeDeprecated)
        templates = templates.Where(t => !t.IsDeprecated);

    // ... pagination, mapping, return ...
}
```

### Clean-Deprecated Endpoint (B17–B22)

Category B entities support a cleanup sweep that permanently removes deprecated templates with zero remaining instances. This is the Category B safe deletion mechanism — there is no per-entity delete endpoint.

#### API Schema Pattern

All clean-deprecated endpoints use shared request/response models from `common-api.yaml`:

```yaml
  /myservice/template/clean-deprecated:
    post:
      operationId: cleanDeprecatedTemplates
      tags:
      - Template
      summary: Clean deprecated templates with zero remaining instances
      description: |
        Category B cleanup sweep (per IMPLEMENTATION TENETS). Iterates all deprecated
        templates and permanently removes those with zero remaining instances,
        subject to an optional grace period. Publishes template.deleted events for
        each removed template. Idempotent and safe to call at any frequency.
        Supports dry-run mode for admin panel preview.
      x-permissions:
      - role: admin
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: 'common-api.yaml#/components/schemas/CleanDeprecatedRequest'
      responses:
        '200':
          description: Cleanup sweep completed
          content:
            application/json:
              schema:
                $ref: 'common-api.yaml#/components/schemas/CleanDeprecatedResponse'
```

**Shared models** (`common-api.yaml`):
- `CleanDeprecatedRequest`: `gracePeriodDays` (int, default 0), `dryRun` (bool, default false)
- `CleanDeprecatedResponse`: `cleaned` (int), `remaining` (int), `errors` (int), `cleanedIds` (uuid array)

#### Implementation Pattern

**File**: `bannou-service/Helpers/DeprecationCleanupHelper.cs`

All clean-deprecated implementations MUST use `DeprecationCleanupHelper.ExecuteCleanupSweepAsync` (per T31 B20). The helper provides standardized per-item error isolation, grace period evaluation, dry-run support, logging, and telemetry. The service provides delegates for storage access and event publishing (inherently service-specific).

```csharp
public async Task<(StatusCodes, CleanDeprecatedResponse?)> CleanDeprecatedTemplatesAsync(
    CleanDeprecatedRequest body, CancellationToken ct)
{
    using var activity = _telemetryProvider.StartActivity(
        "bannou.myservice", "MyService.CleanDeprecatedTemplatesAsync");

    // 1. Query all deprecated entities
    var deprecated = await _templateStore.QueryAsync(
        t => t.IsDeprecated, ct);

    // 2. Delegate to shared helper
    var result = await DeprecationCleanupHelper.ExecuteCleanupSweepAsync(
        deprecated,
        getEntityId: t => t.TemplateId,
        getDeprecatedAt: t => t.DeprecatedAt,
        hasActiveInstancesAsync: async (t, c) =>
            await _instanceStore.ExistsAsync(BuildInstancesByTemplateKey(t.TemplateId), c),
        deleteAndPublishAsync: async (t, c) =>
        {
            await _templateStore.DeleteAsync(BuildTemplateKey(t.TemplateId), c);
            // Remove from indexes, caches, etc.
            await _messageBus.PublishTemplateDeletedAsync(new TemplateDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                TemplateId = t.TemplateId,
                // ... lifecycle fields ...
            }, c);
        },
        body.GracePeriodDays,
        body.DryRun,
        _logger,
        _telemetryProvider,
        ct);

    // 3. Map helper result to generated response
    return (StatusCodes.OK, new CleanDeprecatedResponse
    {
        Cleaned = result.Cleaned,
        Remaining = result.Remaining,
        Errors = result.Errors,
        CleanedIds = result.CleanedIds.ToList()
    });
}
```

**Key points:**
- The `*.deleted` lifecycle event (previously unused infrastructure) is now published here
- `hasActiveInstancesAsync` is service-specific — each service checks its own instance stores
- `deleteAndPublishAsync` must handle all stores, indexes, and caches for the entity
- The helper's `CleanupSweepResult` maps trivially to the generated `CleanDeprecatedResponse`

### What NOT to Include

| Anti-Pattern | Why |
|---|---|
| `POST /{entity}/undeprecate` | Category B deprecation is one-way and terminal |
| `POST /{entity}/delete` (per-entity) | Use `clean-deprecated` sweep instead — no per-entity delete for Category B |
| `409` response for "already deprecated" | Violates idempotency — return `OK` |
| Dedicated `*.deprecated` event | Use `*.updated` with changedFields |
| `isActive` field for deprecation | `isActive` is a separate concept (admin disable); deprecation is `isDeprecated` |
| Service-specific request/response for clean-deprecated | Use shared `CleanDeprecatedRequest`/`CleanDeprecatedResponse` from `common-api.yaml` |
| Custom cleanup loop without `DeprecationCleanupHelper` | Use the helper for standardized error isolation, grace period, and logging (B20) |

---

## Quick Reference: "I need to..."

| Task | Helper/Pattern | Location |
|------|---------------|----------|
| Read-modify-write with retry | `UpdateWithRetryAsync` | `StateStoreExtensions.cs` |
| Publish a service event | Generated `Publish*Async` method | `Generated/{Service}EventPublisher.cs` |
| Publish an error event from a worker | `TryPublishWorkerErrorAsync` | `WorkerErrorPublisher.cs` |
| Push an event to a WebSocket client | `IClientEventPublisher` | `ClientEvents/` |
| Cache data for a Variable Provider | `VariableProviderCacheBucket` | `Providers/` |
| Map between SDK and schema enums | `MapByName` / `MapByNameOrDefault` | `EnumMapping.cs` |
| Paginate a list | `PaginationHelper.Paginate` | `History/PaginationHelper.cs` |
| Decompress JSON from archives | `CompressionHelper.DecompressJsonData` | `History/CompressionHelper.cs` |
| Add a telemetry span | `_telemetryProvider.StartActivity(...)` | N/A (pattern, not helper) |
| Validate constructor pattern (test) | `ServiceConstructorValidator` | `test-utilities/` |
| Validate key builders (test) | `StateStoreKeyValidator` | `test-utilities/` |
| Validate hierarchy (test) | `ServiceHierarchyValidator` | `test-utilities/` |
| Validate enum mappings (test) | `EnumMappingValidator` | `test-utilities/` |
| Validate event publishing (test) | `EventPublishingValidator` | `test-utilities/` |
| Validate resource cleanup (test) | `ResourceCleanupValidator` | `test-utilities/` |
| Add Category B deprecation to a template entity | Category B Deprecation Template | § 15 (schema + implementation patterns) |
| Implement clean-deprecated sweep | `DeprecationCleanupHelper.ExecuteCleanupSweepAsync` | `Helpers/DeprecationCleanupHelper.cs` |

---

*This document catalogs existing infrastructure. For rules and requirements, see [TENETS.md](TENETS.md) and the category-specific tenet documents in [tenets/](tenets/).*
