# Suggested Tenet Augmentations

> **Created**: 2026-03-07
> **Source**: Cross-plugin pattern analysis across 15 plugins in 5 structural groups
> **Status**: Proposals for review — none of these are active tenets yet

This document captures patterns discovered by systematically comparing structurally similar plugins. Each finding represents either (A) a pattern already followed consistently but not documented, risking drift and re-discovery cost, or (B) an inconsistency between plugins that should be standardized. Bugs and tenet violations found during analysis are also documented.

---

## Table of Contents

1. [Proposed Tenet Additions](#i-proposed-tenet-additions)
   1. [Background Worker Polling Loop Pattern](#1-background-worker-polling-loop-pattern)
   2. [State Store Key Builders](#2-state-store-key-builders)
   3. [Event Topic Constants (Generated)](#3-event-topic-constants-generated)
   4. [Optimistic Concurrency Retry Loop](#4-optimistic-concurrency-retry-loop)
   5. [Per-Item Error Isolation in Batch Processing](#5-per-item-error-isolation-in-batch-processing)
   6. [Event Publishing Helper Methods](#6-event-publishing-helper-methods)
   7. [ChangedFields on All Updated Events](#7-changedfields-on-all-updated-events)
   8. [Partial Class Decomposition Threshold](#8-partial-class-decomposition-threshold)
   9. [Multi-Service Call Compensation](#9-multi-service-call-compensation)
   10. [ConcurrentDictionary Cache Invalidation Must Use Events](#10-concurrentdictionary-cache-invalidation-must-use-events)
2. [Bugs and Violations Found](#ii-bugs-and-violations-found)
3. [Inconsistencies Worth Standardizing](#iii-inconsistencies-worth-standardizing)
4. [Missing Abstractions and Shared Helpers](#iv-missing-abstractions-and-shared-helpers)
5. [Analysis Methodology](#v-analysis-methodology)

---

## I. Proposed Tenet Additions

### 1. Background Worker Polling Loop Pattern

**Target Tenet**: New section in T6 (Service Implementation Pattern) or T7 (Error Handling)
**Priority**: High — prevents structural errors in every new worker
**Source**: Agents analyzed matchmaking, subscription, currency, contract, escrow workers

#### The Pattern

Every `BackgroundService.ExecuteAsync` in the codebase follows an identical skeleton. This skeleton has specific requirements around cancellation handling that are easy to get wrong, and getting them wrong causes shutdown to be logged as an error on every graceful restart.

**Canonical skeleton**:

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    _logger.LogInformation("{Worker} starting, interval: {Interval}s",
        nameof(MyWorker), _configuration.MyWorkerIntervalSeconds);

    // 1. Startup delay (configurable, with its own cancellation handler)
    try
    {
        await Task.Delay(
            TimeSpan.FromSeconds(_configuration.MyWorkerStartupDelaySeconds),
            stoppingToken);
    }
    catch (OperationCanceledException) { return; }

    // 2. Main loop
    while (!stoppingToken.IsCancellationRequested)
    {
        // 3. Work with double-catch cancellation filter
        try
        {
            await ProcessCycleAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            break; // Graceful shutdown — NOT an error
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Worker} cycle failed", nameof(MyWorker));
            await TryPublishWorkerErrorAsync(ex, stoppingToken);
        }

        // 4. Delay with its own cancellation handler
        try
        {
            await Task.Delay(
                TimeSpan.FromSeconds(_configuration.MyWorkerIntervalSeconds),
                stoppingToken);
        }
        catch (OperationCanceledException) { break; }
    }

    _logger.LogInformation("{Worker} stopped", nameof(MyWorker));
}
```

**Key rules**:

| Rule | Why |
|------|-----|
| `OperationCanceledException` filter with `when (stoppingToken.IsCancellationRequested)` MUST come before generic `Exception` catch | Prevents shutdown being logged as an error |
| Startup delay MUST be configurable and have its own cancellation handler | Services may need different warm-up periods; handler prevents startup exceptions on fast shutdown |
| Cycle failures MUST publish error events via `TryPublishErrorAsync` | Currently inconsistent — matchmaking only logs, doesn't publish. Workers have no generated controller catch-all. |
| `ExecuteAsync` itself MUST NOT receive a `StartActivity` telemetry span | A span covering the entire process lifetime (hours/days) produces no useful telemetry. Matchmaking currently has this bug. Instrument the per-cycle `ProcessCycleAsync` instead. |

**Constructor dependencies** (standardized across all workers):

```csharp
public MyWorker(
    IServiceProvider serviceProvider,      // For scope creation
    ILogger<MyWorker> logger,
    MyServiceConfiguration configuration,
    ITelemetryProvider telemetryProvider)   // For per-cycle spans
```

No scoped dependencies (stores, message bus, service clients) may be constructor-injected into singleton BackgroundService classes.

**Worker configuration naming** (standardize):
- Startup delay: `{WorkerName}StartupDelaySeconds`
- Processing interval: `{WorkerName}IntervalSeconds` (prefer seconds; milliseconds only when sub-second precision matters)
- Batch size: `{WorkerName}BatchSize`

Currently inconsistent: `BackgroundServiceStartupDelaySeconds` vs `StartupDelaySeconds` vs `AutogainTaskStartupDelaySeconds`, and seconds vs minutes vs milliseconds for the same concept across plugins.

**Shared helper opportunity**: A `WorkerErrorPublisher` utility or extension method in `bannou-service/` could standardize the error event publishing pattern for workers. Currently, currency extracts this into a private `TryPublishErrorAsync` helper that creates a scope, resolves `IMessageBus`, and publishes — this pattern is repeated (or should be) in every worker. A shared helper would:
- Accept `IServiceProvider`, service name, operation name, and exception
- Handle scope creation, `IMessageBus` resolution, and the catch-all around `TryPublishErrorAsync`
- Prevent workers from forgetting error event publishing entirely (matchmaking's current bug)

---

### 2. State Store Key Builders

**Target Tenet**: Addition to T6 (Service Implementation Pattern)
**Priority**: High — prevents scattered magic strings and enables testability
**Source**: Agents analyzed all 15 plugins; found 3 distinct approaches

#### The Pattern

Services need to construct deterministic string keys for state store lookups. Three approaches exist in the codebase:

| Approach | Plugins | Example |
|----------|---------|---------|
| `const` prefix + `static Build*Key()` | location, transit, quest, collection | `private const string PREFIX = "loc:"; internal static string BuildLocationKey(Guid id) => $"{PREFIX}{id}";` |
| `static Get*Key()` or `*Key()` (no const) | escrow, status | `internal static string GetAgreementKey(Guid id) => $"agreement:{id}";` |
| Inline interpolation | worldstate, inventory | `$"realm:{realmId}"` scattered throughout |

**Proposed rule**: State store keys MUST use `const` prefix fields with `internal static` key-building methods:

```csharp
// CORRECT: Single-point key format, testable, accessible to provider factories
private const string ENTITY_KEY_PREFIX = "entity:";
private const string INDEX_KEY_PREFIX = "entity-index:";

internal static string BuildEntityKey(Guid id) => $"{ENTITY_KEY_PREFIX}{id}";
internal static string BuildIndexKey(string scope) => $"{INDEX_KEY_PREFIX}{scope}";

// FORBIDDEN: Inline interpolation (typo-prone, multi-point changes)
var entity = await _store.GetAsync($"entity:{entityId}", ct);
```

**Why `internal static`**: Provider factories (which live in the same assembly) often need to construct keys for cache loading. Transit's `TransitVariableProviderFactory` calls `TransitService.BuildJourneyKey()`. `private` forces the factory to duplicate the key format.

**Why `Build` prefix**: `Get` implies retrieval from a store; `Build` correctly communicates that a string is being constructed. Standardize on one verb.

**Shared helper opportunity**: Not applicable here — key builders are necessarily service-specific because each service has its own key format and prefix scheme. The tenet rule itself is the standardization mechanism.

---

### 3. Event Topic Constants (Generated)

**Target Tenet**: Addition to T5 (Event-Driven Architecture) and code generation pipeline
**Priority**: Medium — compile-time safety for topic strings
**Source**: Agent analyzed quest and escrow; both define `{Service}Topics` constants classes manually

#### The Current State

Quest and escrow manually define static classes with `internal const string` fields for every published topic:

```csharp
// QuestService.cs — manually maintained
internal static class QuestTopics
{
    internal const string QuestAccepted = "quest.accepted";
    internal const string QuestCompleted = "quest.completed";
    // ...5 more
}
```

This is good practice but manually maintained — which means it can drift from the event schema.

#### The Better Approach: Generate Topic Constants

The event schemas already declare `x-event-subscriptions` for consumed events, and event models already have `eventName` defaults that define the topic. The generation pipeline should produce a `{Service}PublishedTopics` constants class from the schema, similar to how client event names are generated for the SDK.

**Proposed approach**:

1. Event schemas already define `eventName` defaults on each event model (e.g., `default: quest.accepted`). The generator can extract all `eventName` defaults from a service's events schema.

2. Generate a `{Service}PublishedTopics` class into the plugin's `Generated/` directory:

```csharp
// Generated/{Service}PublishedTopics.cs — auto-generated, never edit
public static class QuestPublishedTopics
{
    public const string QuestAccepted = "quest.accepted";
    public const string QuestCompleted = "quest.completed";
    public const string QuestFailed = "quest.failed";
    // ... all topics from eventName defaults in quest-events.yaml
}
```

3. Service code references `QuestPublishedTopics.QuestAccepted` instead of inline strings. The manually maintained `{Service}Topics` classes can be deleted.

4. **Validation bonus**: The generator can validate that every event model in the schema has an `eventName` default, and that every topic published in service code matches a generated constant. This closes the loop — if a service publishes a topic that doesn't exist in its schema, it's a build error.

5. **Integration with generated event interface**: These constants could be included as static members on the generated `I{Service}Service` interface or the generated events controller, similar to how client SDK event names are already exposed for subscription convenience.

**Why not just a tenet rule to "define constants manually"**: Manual constants can drift from schemas. Generated constants are schema-first (T1) and cannot drift. The generation pipeline already extracts `eventName` — extending it to produce a constants class is minimal work.

---

### 4. Optimistic Concurrency Retry Loop

**Target Tenet**: Addition to T9 (Multi-Instance Safety)
**Priority**: High — the pattern has specific requirements that are easy to get wrong
**Source**: Agent analyzed quest and escrow; found identical retry shape at 7+ call sites

#### The Pattern

Every ETag-based optimistic concurrency operation follows the same shape:

```csharp
for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
{
    var (current, etag) = await _store.GetWithETagAsync(key, ct);
    if (current == null) return (StatusCodes.NotFound, null);

    // Mutate current
    current.Field = newValue;
    current.UpdatedAt = DateTimeOffset.UtcNow;

    var saved = await _store.TrySaveAsync(key, current, etag ?? string.Empty, cancellationToken: ct);
    if (saved == null)
    {
        _logger.LogDebug("Concurrency conflict on attempt {Attempt} for {Key}", attempt, key);
        continue;
    }

    // Success path: publish events, return response
    return (StatusCodes.OK, response);
}

_logger.LogWarning("Exhausted {Max} retries for {Key}", _configuration.MaxConcurrencyRetries, key);
return (StatusCodes.Conflict, null);
```

**Key rules**:
- Retry count MUST be configurable via `MaxConcurrencyRetries` (T21 compliance)
- Individual conflicts logged at Debug (expected, transient)
- Exhaustion logged at Warning (unexpected, indicates contention)
- Return `Conflict` on exhaustion — not `InternalServerError`
- The `etag ?? string.Empty` coalesce satisfies the compiler when the store guarantees non-null ETags for existing records (documented with a comment per the `?? string.Empty` coding rules)

**Shared helper opportunity**: This is a strong candidate for an extension method on `IStateStore<T>` in `bannou-service/`:

```csharp
// bannou-service/Extensions/StateStoreExtensions.cs
public static async Task<(T? entity, bool saved)> UpdateWithRetryAsync<T>(
    this IStateStore<T> store,
    string key,
    Action<T> mutate,
    int maxRetries,
    ILogger logger,
    CancellationToken ct)
```

This would eliminate the for-loop boilerplate at every call site while enforcing the correct retry semantics (debug log per attempt, warning on exhaustion). Services would call:

```csharp
var (entity, saved) = await _store.UpdateWithRetryAsync(
    key, model => { model.Status = newStatus; },
    _configuration.MaxConcurrencyRetries, _logger, ct);
if (!saved) return (StatusCodes.Conflict, null);
```

---

### 5. Per-Item Error Isolation in Batch Processing

**Target Tenet**: Addition to T7 (Error Handling)
**Priority**: Medium — prevents one corrupt record from blocking all processing
**Source**: Agent analyzed subscription and currency workers; found consistent per-item isolation

#### The Pattern

When a worker or batch operation iterates over a collection, per-item processing MUST be individually try-caught:

```csharp
foreach (var item in items)
{
    try
    {
        await ProcessItemAsync(item, ct);
        successCount++;
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Failed to process {ItemId}, continuing", item.Id);
        failureCount++;
    }
}
```

**The rule**: A failure processing one item MUST NOT abort remaining items in the cycle. Log per-item failures at Warning level with the item's identifier. Track success/failure counts for cycle-level reporting.

**This applies to**: Background worker cycles processing batches, bulk seed operations, bulk update/delete operations, any `foreach` over entities where each iteration is independent.

---

### 6. Event Publishing Helper Methods

**Target Tenet**: Addition to T5 (Event-Driven Architecture)
**Priority**: Medium — reduces method length and centralizes event construction
**Source**: Agent compared location/transit (use helpers) vs worldstate (inline publishing)

#### The Pattern

Event publishing SHOULD use dedicated private async helper methods rather than inline `TryPublishAsync` calls:

```csharp
// PREFERRED: Helper method — centralizes event construction, enables spans
private async Task PublishLocationCreatedEventAsync(
    LocationModel model, CancellationToken ct)
{
    using var activity = _telemetryProvider.StartActivity(
        "bannou.location", "LocationService.PublishLocationCreated");

    await _messageBus.TryPublishAsync(LocationTopics.LocationCreated,
        new LocationCreatedEvent
        {
            EventName = LocationTopics.LocationCreated,
            LocationId = model.LocationId,
            // ... map all fields
        }, ct);
}

// DISCOURAGED: Inline — clutters business logic, no span, multi-point schema changes
await _messageBus.TryPublishAsync("location.created", new LocationCreatedEvent
{
    EventName = "location.created",
    // ... inline field mapping
}, ct);
```

**Benefits**: (1) Single-point event model construction for schema changes. (2) Natural place for telemetry spans on event publishing. (3) Reduces business logic method length. (4) Makes it obvious which events a method publishes (one helper call vs 20 lines of event construction inline).

---

### 7. ChangedFields on All Updated Events

**Target Tenet**: Generalization of T31's `changedFields` rule into T5
**Priority**: Medium — already universal practice, just not documented as such
**Source**: Agent confirmed consistent usage across location, transit, worldstate

#### The Pattern

T31 (Deprecation Lifecycle) specifies that deprecation state changes should be published as `*.updated` events with `changedFields`. In practice, ALL `*.updated` events in the codebase carry `changedFields` — not just deprecation changes. This is already how x-lifecycle auto-generates `*UpdatedEvent` types (they include a `changedFields` property).

**Proposed generalization**: "All `*.updated` events (whether lifecycle-generated or custom) MUST include a `changedFields` property containing camelCase property names of the fields that changed. This enables consumers to filter reactions to only the fields they care about."

**Implementation note**: The `changedFields` list uses camelCase names matching the schema property names (not C# PascalCase). This is already the convention across all plugins.

---

### 8. Partial Class Decomposition Threshold

**Target Tenet**: Addition to T6 (Service Implementation Pattern)
**Priority**: Medium — prevents monolithic service files
**Source**: Agent compared quest (2464 lines in one file) vs escrow (10 partial files by domain)

#### The Pattern

T6 mandates the three-file partial class structure (`*Service.cs`, `*ServiceEvents.cs`, `*ServiceModels.cs`). It does not address when to decompose further.

**Proposed rule**: When the main `{Service}Service.cs` exceeds approximately 500 lines of business logic, decompose by domain operation into additional partial class files named `{Service}Service{Domain}.cs`:

```
plugins/lib-escrow/
├── EscrowService.cs              # Core: topics, keys, constructor, shared helpers
├── EscrowServiceLifecycle.cs     # Create, cancel, expire
├── EscrowServiceDeposits.cs      # Deposit, release, refund
├── EscrowServiceCompletion.cs    # Complete, distribute
├── EscrowServiceConsent.cs       # Consent flows
├── EscrowServiceValidation.cs    # Validation helpers
├── EscrowServiceHandlers.cs      # Prebound API handlers
├── EscrowServiceEvents.cs        # Event subscriptions
└── EscrowServiceModels.cs        # Internal models
```

**Naming convention**: Each partial file should be cohesive around one domain concern. File names should be `{Service}Service{DomainConcern}.cs` with PascalCase domain names.

**Not a hard rule**: Services with simple CRUD (game-service at 5 endpoints) don't need decomposition. The threshold is a guideline, not a gate.

---

### 9. Multi-Service Call Compensation

**Target Tenet**: Addition to IMPLEMENTATION-BEHAVIOR or new section in T7
**Priority**: Medium — prevents silent orphaned state in orchestration layers
**Source**: Agent compared quest (acknowledges orphaned state in comments) vs escrow (actively compensates)

#### The Problem

Thin orchestration layers call multiple services in sequence. If step 3 fails after steps 1 and 2 succeeded, the system is in a partially-committed state:

- **Quest**: Creates a contract instance, then consents, then sets template values. If consent fails, an orphaned contract instance exists. A comment acknowledges this but no compensation is implemented.
- **Escrow**: Tracks `incrementedParties` in a list. If a later step fails, the catch block decrements each party's pending count, compensating for the partial state.

#### Proposed Rule

Multi-step orchestration methods that call multiple services MUST either:

1. **Compensate in catch blocks** (Escrow pattern): Track what was done, undo on failure.
2. **Document the self-healing mechanism**: Specify which event subscription or background worker resolves the partial state, and over what timeframe.

**Forbidden**: Acknowledging possible orphaned state in a code comment without implementing either compensation or self-healing. A comment is not a mechanism.

---

### 10. ConcurrentDictionary Cache Invalidation Must Use Events

**Target Tenet**: Strengthening of T9 or explicit addition to T5
**Priority**: High — bugs found in 2 of 3 analyzed satellite services
**Source**: Agent compared character-personality (correct), character-encounter (inline invalidation — wrong), character-history (no invalidation at all — bug)

#### The Problem

SERVICE-HIERARCHY.md § "DI Provider vs Listener: Distributed Safety" already says: "If per-node awareness is required (e.g., invalidating a ConcurrentDictionary cache on every node), the consumer MUST subscribe to the broadcast event via IEventConsumer."

Despite this rule existing, 2 of 3 character satellite services violate it:

- **character-personality**: Correctly self-subscribes to its own `personality.evolved` and `combat-preferences.evolved` events for cross-node cache invalidation.
- **character-encounter**: Invalidates cache inline in service methods (`_encounterDataCache.Invalidate(characterId)`). This only works on the node that processed the request. All other nodes serve stale data until TTL expiry.
- **character-history**: Defines `Invalidate()` on its cache interface but **never calls it from anywhere**. Cache serves stale data until TTL expiry on ALL nodes.

#### Proposed Strengthening

The existing rule in SERVICE-HIERARCHY.md is correct but buried. It should be promoted to the main tenet body (T9 or T5) with explicit guidance:

**Rule**: Services using `ConcurrentDictionary` caches (including Variable Provider caches) MUST invalidate via self-event-subscription, not inline method calls:

```csharp
// CORRECT: Self-subscribe to own events for cross-node invalidation
eventConsumer.RegisterHandler<IMyService, MyEntityUpdatedEvent>(
    "my-entity.updated",
    async (svc, evt) => ((MyService)svc)._cache.Invalidate(evt.EntityId));

// WRONG: Inline invalidation — only works on the processing node
await _store.SaveAsync(key, model, ct);
_cache.Invalidate(model.EntityId);  // Other nodes never see this
await _messageBus.TryPublishAsync("my-entity.updated", event, ct);
```

**The decision tree for cache invalidation**:

| Cache Type | Invalidation Mechanism | Example |
|------------|----------------------|---------|
| `ConcurrentDictionary` (in-memory) | Self-event-subscription via `IEventConsumer` | character-personality |
| Redis cache with TTL | Explicit `DeleteAsync` at mutation sites + TTL as safety net | location, transit |
| Redis cache without TTL | Explicit `DeleteAsync` at mutation sites (mandatory) | — |

**Shared helper opportunity**: The Variable Provider Cache pattern (singleton with `ConcurrentDictionary`, `IServiceScopeFactory` for loading, sealed `CachedEntry` record with `IsExpired`, stale-data fallback on error) is repeated in 3+ satellite services with ~80% identical boilerplate. A `VariableProviderCache<TData>` base class in `bannou-service/Providers/` could:
- Provide the `ConcurrentDictionary` + TTL + expiry check infrastructure
- Provide the `Invalidate(Guid)` and `InvalidateAll()` methods
- Provide the stale-data fallback pattern
- Require subclasses to implement only: data type, load function (via generated client), and TTL source
- Include self-event-subscription wiring guidance in its XML documentation

---

## II. Bugs and Violations Found

These are existing issues discovered during the analysis, not proposals.

### Tenet Violations

| Plugin | Violation | Tenet | Severity |
|--------|-----------|-------|----------|
| **lib-location** | Almost no endpoint methods have telemetry spans | T30 | High |
| **lib-worldstate** | Missing spans on several service endpoint methods | T30 | Medium |
| **lib-collection** | Missing spans on async helpers (`CreateCollectionInternalAsync`, `LoadOrRebuildCollectionCacheAsync`, `DispatchUnlockListenersAsync`) | T30 | Medium |
| **lib-matchmaking** | Worker `ExecuteAsync` has process-lifetime span (useless) | T30 | Low |
| **lib-matchmaking** | Worker catch block only logs, doesn't publish error events | T7 | Medium |
| **lib-status** | Uses manual `DefineCleanupCallbackAsync()` instead of generated `x-references` pattern | T1, T28 | Medium |

### Multi-Node Safety Bugs

| Plugin | Bug | Impact |
|--------|-----|--------|
| **lib-character-encounter** | Cache invalidated inline in service methods, not via event subscription | Other nodes serve stale encounter data until TTL expiry |
| **lib-character-history** | Cache `Invalidate()` defined but never called from anywhere | ALL nodes serve stale backstory data until TTL expiry |

### Data Quality Issues

| Plugin | Issue | Impact |
|--------|-------|--------|
| **lib-status** | Hardcodes `ContainerOwnerType.Other` for all containers instead of mapping from `EntityType` | Loses owner type information downstream; cross-service queries for "all containers for this character" don't work |
| **lib-worldstate** | Uses inline string interpolation for state keys instead of const prefix + Build method | Typo-prone, multi-point key format changes |

---

## III. Inconsistencies Worth Standardizing

These don't necessarily need new tenets but should be addressed for consistency.

| Area | Current State | Best Practice | Fix Scope |
|------|--------------|---------------|-----------|
| Worker error event publishing | Matchmaking: log only. Currency/subscription: publish error events. | All workers MUST publish error events | Per-worker fix |
| Worker DI scope approach | Matchmaking: delegates to service. Currency: independent store access. Subscription: hybrid. | Document both delegation and independent as valid; deprecate hybrid (both resolving service AND doing direct store access) | Documentation |
| Config property naming for workers | `BackgroundServiceStartupDelaySeconds` vs `StartupDelaySeconds` vs `AutogainTaskStartupDelaySeconds` | `{WorkerName}StartupDelaySeconds`, `{WorkerName}IntervalSeconds` | Schema renames |
| Config interval units | Seconds vs minutes vs milliseconds for same concept | Prefer seconds; milliseconds only for sub-second precision | Schema renames |
| Pagination response shape | Location: full metadata (`Page`, `PageSize`, `HasNextPage`, `HasPreviousPage`). Transit/Worldstate: `TotalCount` only. | Standardize pagination response wrapper | Schema + SCHEMA-RULES addition |
| Lock owner string format | Location: bare GUID. Transit: `$"operation-{Guid:N}"`. Worldstate: `$"service-{Guid:N}"`. | `$"{operation}-{Guid:N}"` (debuggable, shows which operation holds the lock) | Per-service fix |
| Model visibility | Worldstate: all `public`. Transit: mostly `internal`. | Default `internal`; `public` only when external consumers (providers, caches) need access | Per-service fix |
| Cache TTL config naming | `CacheTtlMinutes` vs `BackstoryCacheTtlSeconds` vs `EncounterCacheTtlMinutes` | `{Entity}CacheTtlMinutes` (consistent entity prefix, consistent unit) | Schema renames |
| Event error reporting in event handlers | Quest: direct `TryPublishErrorAsync` with full params. Escrow: wrapper with reduced params. | Full params (dependency, endpoint, stack) for debuggability | Per-service fix |
| Container owner type mapping | Collection: maps `EntityType` to `ContainerOwnerType`. Status: hardcodes `Other`. | Map properly like Collection does | lib-status fix |
| Lock timeout configuration | Collection: single timeout. Status: dual timeout (acquisition + TTL). | Dual timeout is more robust (cancel stale acquisition attempts) | Evaluate for standardization |
| Key builder naming | `Build*Key()` vs `Get*Key()` vs `*Key()` | Standardize on `Build*Key()` | Per-service rename |
| `x-event-subscriptions` on event schemas | Personality/history: `x-event-subscriptions: []`. Encounter: missing. | All event schemas include `x-event-subscriptions` (even if empty) | Schema fix |
| Cleanup by foreign key endpoint naming | Worldstate: `CleanupBy{Entity}Async`. Others: various. | Standardize naming convention in SCHEMA-RULES `x-references` docs | Documentation |

---

## IV. Missing Abstractions and Shared Helpers

These are opportunities to add shared infrastructure in `bannou-service/` that would simplify and standardize plugin implementations.

### A. Strong Candidates (Clear Benefit)

#### 1. `DecompressJsonData` Shared Utility
**Current state**: Identical 7-line method duplicated in 4 plugins (character-personality, character-history, character-encounter, realm-history).
**Proposed location**: `bannou-service/History/CompressionHelper.cs` (the `bannou-service/History/` namespace already houses shared helpers like `TimestampHelper`, `DualIndexHelper`, `BackstoryStorageHelper`).
**Impact**: Eliminate 4 identical copies. One-line calls in each service.

#### 2. `StateStoreExtensions.UpdateWithRetryAsync<T>`
**Current state**: 7+ identical ETag retry loops across quest and escrow, each 15-20 lines.
**Proposed location**: `bannou-service/Extensions/StateStoreExtensions.cs`
**Signature sketch**:
```csharp
public static async Task<(T? entity, bool saved)> UpdateWithRetryAsync<T>(
    this IStateStore<T> store,
    string key,
    Action<T> mutate,
    int maxRetries,
    ILogger logger,
    CancellationToken ct)
```
**Impact**: Reduces each call site from 15 lines to 3. Enforces correct retry semantics (debug per attempt, warning on exhaustion).

#### 3. Worker Error Publishing Helper
**Current state**: Currency extracts error publishing into a private helper. Subscription does it inline. Matchmaking doesn't do it at all.
**Proposed location**: `bannou-service/Extensions/BackgroundServiceExtensions.cs` or `bannou-service/Workers/WorkerErrorPublisher.cs`
**Signature sketch**:
```csharp
public static async Task TryPublishWorkerErrorAsync(
    this IServiceProvider serviceProvider,
    string serviceName,
    string operationName,
    Exception exception,
    CancellationToken ct)
```
**Impact**: Handles scope creation, `IMessageBus` resolution, and the safety catch-all. Workers call one method instead of 10 lines of scope/resolve/publish/catch. Prevents the "forgot to publish error events" bug.

### B. Worth Evaluating (Moderate Benefit)

#### 4. `VariableProviderCache<TData>` Base Class
**Current state**: 3+ cache implementations with ~80% identical boilerplate (ConcurrentDictionary, TTL check, stale fallback, scope-based loading, invalidation methods).
**Proposed location**: `bannou-service/Providers/VariableProviderCache.cs`
**Impact**: Each concrete cache reduces to: data type, load function, TTL property. Includes self-invalidation documentation.
**Caution**: The 20% that differs (number of dictionaries, multi-type loading in encounter cache) may make a generic base awkward. Evaluate whether composition (helper class) is better than inheritance (base class).

#### 5. Dual-Key State Store Wrapper
**Current state**: Collection and Status both manually save under 2 keys and delete both keys, at 10+ call sites each. Forgetting one key creates data inconsistency.
**Proposed location**: `bannou-service/Extensions/DualKeyStateStoreExtensions.cs`
**Signature sketch**:
```csharp
public static async Task<string?> SaveDualKeyAsync<T>(
    this IStateStore<T> store,
    string primaryKey,
    string secondaryKey,
    T value,
    CancellationToken ct)

public static async Task DeleteDualKeyAsync<T>(
    this IStateStore<T> store,
    string primaryKey,
    string secondaryKey,
    CancellationToken ct)
```
**Impact**: Eliminates the "forgot to update the second key" bug class.

#### 6. Standard Pagination Helper
**Current state**: Multiple services implement `Skip/Take` + count inline. Response shapes vary (some include `HasNextPage`/`HasPreviousPage`, some only `TotalCount`).
**Proposed location**: `bannou-service/Pagination/PaginationHelper.cs`
**Impact**: Enforces consistent pagination response shape. Low priority given the simplicity of the arithmetic, but would standardize the response contract.

### C. Not Worth Extracting (Domain-Specific)

- **Key builder methods**: Necessarily service-specific. Standardize via tenet rule, not shared code.
- **Event publishing helpers**: Service-specific event models make generic abstraction impractical.
- **Seeding logic**: Similar structure but model types differ too much for a useful generic.

---

## V. Analysis Methodology

### Agent Design

5 agents were launched in parallel, each reading all tenet documents in full before analyzing their assigned plugin sets:

| Agent | Plugins Analyzed | Structural Theme |
|-------|-----------------|------------------|
| 1 | character-personality, character-history, character-encounter | Satellite data services (L4 extending L2 Character) |
| 2 | collection, status, inventory | Container/"items in inventories" pattern |
| 3 | quest, escrow, divine | Thin orchestration layers composing primitives |
| 4 | matchmaking, subscription, currency | Background worker services |
| 5 | location, transit, worldstate | World model L2 services |

### What Each Agent Read

- All 5 tenet documents: FOUNDATION.md, IMPLEMENTATION-BEHAVIOR.md, IMPLEMENTATION-DATA.md, QUALITY.md, TESTING-PATTERNS.md
- TENETS.md index
- For each plugin: main service file, events file, models file, helper services directory, plugin registration file, API schema, events schema, configuration schema

### Selection Criteria for Plugin Groups

Groups were chosen to maximize structural similarity (same role, same layer, same patterns) so that differences between them are meaningful signals rather than expected domain variation. Some overlap between groups was intentional (e.g., both agents 3 and 4 touched background workers, providing cross-validation).

### What Constitutes a "Finding"

- **Consistent pattern NOT in tenets**: All plugins in the group do the same thing, but no tenet documents it. Risk: new services will do it differently.
- **Inconsistent pattern**: Plugins in the group do the same thing differently. Risk: code review catches some but not others.
- **Bug/violation**: Code contradicts an existing tenet. Risk: existing incorrect behavior.
- **Missing abstraction**: Identical code duplicated across plugins. Risk: drift between copies, maintenance burden.

---

*This document is a proposal for review. No changes to tenets, schemas, or code should be made based on this document without explicit approval.*
