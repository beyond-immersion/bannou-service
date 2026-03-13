# Batch Lifecycle Events: Normalized High-Frequency Event Publishing

> **Type**: Implementation Plan
> **Status**: Active
> **Created**: 2026-03-13
> **Last Updated**: 2026-03-13
> **North Stars**: #1
> **Related Plugins**: Item, Permission, Affix, Character-Encounter, Character-History, Character-Personality, Transit, Status, Currency, Quest, Achievement, Collection, Divine, Relationship, Actor, Leaderboard, License

## Summary

Normalizes high-frequency event publishing across Bannou by extending x-lifecycle with a batch: true option that generates only batch event types, adding a shared EventBatcher helper to bannou-service, creating shared batch endpoint request/response models in common-api.yaml, and adding structural tests to enforce consistency. A structural analysis of x-references declarations revealed that nearly all 16 services storing per-character dependent data become high-frequency event publishers at 100K NPC scale, with their lifecycle events serving purely informational/analytics purposes (cleanup handled by lib-resource or DI Listeners, not event subscription). This establishes x-references targeting character-scale entities as a structural heuristic for batch: true candidacy, applicable to 15 of 16 x-references services.

---

## Context

### The Problem

Three services independently implement or plan the same high-frequency event accumulation pattern:

| Service | Current Pattern | Events | Consumers |
|---------|----------------|--------|-----------|
| **Permission** | `RegistrationEventBatcher` BackgroundService with ConcurrentDictionary + Interlocked.Exchange + periodic flush | `permission.services-registered` (bulk) | None (analytics/observability) |
| **Item** | Inline ConcurrentDictionary accumulation with background flush for use events; individual per-operation publishing for instance lifecycle events | `item.used`/`item.use-failed` (batched); `item.instance.created`/`modified`/`destroyed`/`bound`/`unbound` (individual, **zero subscribers**) | None for any item event |
| **Affix** (planned) | Deep dive specifies `GenerationEventDeduplicationWindowSeconds` and `GenerationEventBatchMaxSize` config; Quirk #10: "Generation events are batched, not per-item" | Generation events (batched); `affix.modifier.applied`/`removed` (individual, lib-market planned consumer) | lib-market for modifier events (functional, but 5s delay acceptable) |

**Key finding**: No service in the entire codebase subscribes to any `item.*` event via `x-event-subscriptions`. The only near-miss is a commented-out `item.expired` in `status-events.yaml` blocked on #407. Item cleanup uses `IItemInstanceDestructionListener` (DI Listener pattern per T28), not events. Inventory container events (`inventory.item.placed`/`removed`/`moved`/`transferred`) handle the functional "items moved around" signaling.

### The Opportunity

1. **x-lifecycle `batch: true`** — Entities with this flag generate ONLY batch event types (no individual lifecycle events). The generator produces `*BatchCreatedEvent`, `*BatchModifiedEvent`, `*BatchDestroyedEvent` with entry arrays.

2. **Two shared batcher helpers** — `EventBatcher<TEntry>` (accumulating, append-all) and `DeduplicatingEventBatcher<TKey, TEntry>` (last-write-wins). Both live in `bannou-service/Services/`. All services feed the appropriate batcher (both batch endpoints and individual operations). The batcher owns all publishing.

3. **Shared batch models** — `common-api.yaml` provides standardized request/response models for batch endpoints.

4. **Structural validation** — Tests enforce that `batch: true` entities have batch event publications, batch publisher methods, and a batcher implementation.

### The Two Batching Modes

Analysis of all candidate services revealed a fundamental split in how batch entries should be accumulated:

**Mode 1: Accumulating (append-all)** — each operation produces a unique entry that must be preserved. This is the **majority case**.

- Currency: 3 credits to the same wallet = 3 entries (last-write-wins would lose the first two)
- Character-Encounter: 5 encounters recorded = 5 entries
- Character-History: 8 participations = 8 entries
- Divine: god blesses 50 NPCs = 50 entries
- Collection: NPC discovers 5 things = 5 entries
- Actor: 30 spawns across a region = 30 entries
- Transit: 10K journey waypoints = 10K entries
- Status: 200 status grants = 200 entries
- Item instance created/destroyed: 50 items looted = 50 entries

**Mode 2: Deduplicating (last-write-wins)** — same entity updated multiple times in a window; only the latest state matters. This is the **minority case**.

- Permission: same service re-registers; only latest registration matters
- Item instance modified: same item's durability updated 5 times; only final state matters
- Relationship updated: same relationship modified multiple times; final state matters
- Character-Personality evolved: same personality traits updated; final state matters

**Key insight**: A single service may need BOTH modes for different event types. Item needs Mode 1 for `created`/`destroyed` (each is a unique entity entering/leaving existence) and Mode 2 for `modified` (same entity's mutable fields changing). This drives the two-class helper design.

### Design Decisions Made

- **`batch: true` generates ONLY batch events** — no individual lifecycle events exist for that entity. No dual publishing, no confusion. If a service later needs synchronous per-entity events, that's a deliberate design change (remove `batch: true` or add custom events).
- **Everything feeds the batcher** — both batch endpoints and individual operations call `batcher.Add()`/`batcher.AddRange()`. The batcher flushes on interval OR when max batch size is reached. No direct event publishing from endpoints.
- **Downstream consumers don't preclude batching** — services with functional consumers (e.g., lib-market for affix) use shorter intervals (5s vs 60s for pure observability). A 5s delay is acceptable for trade index maintenance.
- **Batch interval is per-service configuration** — each service defines its own `*BatchIntervalSeconds` and `*BatchMaxSize` in its configuration schema.
- **Two helper classes, not one** — `EventBatcher<TEntry>` for Mode 1 (ConcurrentQueue, append-all), `DeduplicatingEventBatcher<TKey, TEntry>` for Mode 2 (ConcurrentDictionary, last-write-wins). Services pick the appropriate class per event type.
- **Custom (non-lifecycle) batch events use the helpers directly** — services with custom events (Currency credit/debit, Transit journey waypoints, Actor spawn/despawn) wire `EventBatcher<TEntry>` manually in their service code and BackgroundService. The `batch: true` x-lifecycle flag handles the generated case; custom events are manual but use the same shared infrastructure.

---

## Implementation Steps

### Step 1: Shared EventBatcher Helpers

#### 1a. `bannou-service/Services/EventBatcher.cs` — Accumulating (Mode 1)

The **primary** helper for most services. Appends all entries without deduplication.

```csharp
/// <summary>
/// Accumulates events without deduplication, flushing periodically as batch events.
/// Thread-safe. Per-node in-memory accumulation (each node publishes its own
/// batches independently). Use for events where each operation is unique and all
/// must be preserved (created, destroyed, granted, recorded, etc.).
/// </summary>
/// <typeparam name="TEntry">Entry payload type.</typeparam>
public class EventBatcher<TEntry>
{
    // ConcurrentQueue<TEntry> for accumulation (append-all, no deduplication)
    // Add(TEntry entry) — single entry
    // AddRange(IEnumerable<TEntry> entries) — batch endpoint support
    // FlushAsync(CancellationToken) — atomic drain + invoke flush callback
    // IsEmpty — check before flush cycle
}
```

**Constructor parameters**:
- `Func<List<TEntry>, DateTimeOffset, CancellationToken, Task> flushCallback` — receives entries + window start time, publishes the batch event
- `ILogger logger` — for flush diagnostics
- `int maxBatchSize` — triggers flush when entry count exceeds threshold (optional, 0 = interval-only)

**Behavioral contract**:
- `Add(entry)` — appends entry to bag (no deduplication)
- `AddRange(entries)` — appends multiple entries (for batch endpoints feeding the batcher)
- `FlushAsync(ct)` — atomically swaps bag via `Interlocked.Exchange`, sorts entries by timestamp, invokes callback. No-ops if empty.
- Thread-safe via `ConcurrentBag<TEntry>` + `Interlocked.Exchange` (same atomic swap pattern as Mode 2)
- **Chronological ordering guarantee**: entries are sorted by timestamp before the flush callback is invoked. `TEntry` must implement `ILifecycleEvent` (provides `CreatedAt`) or the batcher accepts a `Func<TEntry, DateTimeOffset>` timestamp selector at construction. Consumers receive entries in chronological order without needing to sort themselves.

**When to use**: Item instance created/destroyed, Currency credit/debit, Character-Encounter recording, Transit journey events, Divine blessing grants, Actor spawn/despawn, Collection entry unlocks — any event where each operation is unique.

#### 1b. `bannou-service/Services/DeduplicatingEventBatcher.cs` — Deduplicating (Mode 2)

The **secondary** helper for state-update events. Last-write-wins by key within a window.

```csharp
/// <summary>
/// Accumulates events by key with last-write-wins deduplication, flushing
/// periodically. Use for events where the same entity may be updated multiple
/// times in a window and only the latest state matters (modified, evolved, etc.).
/// </summary>
/// <typeparam name="TKey">Deduplication key type (e.g., Guid for entityId).</typeparam>
/// <typeparam name="TEntry">Entry payload type.</typeparam>
public class DeduplicatingEventBatcher<TKey, TEntry> where TKey : notnull
{
    // ConcurrentDictionary<TKey, TEntry> for accumulation (last-write-wins)
    // Interlocked.Exchange for atomic snapshot on flush
    // Add(TKey key, TEntry entry) — single entry, overwrites same key
    // FlushAsync(CancellationToken) — atomic swap + invoke flush callback
    // IsEmpty — check before flush cycle
}
```

**Same constructor parameters and flush callback signature** as `EventBatcher<TEntry>`.

**Chronological ordering guarantee**: Same as Mode 1 — after the atomic swap, values are sorted by timestamp before the flush callback is invoked. Even though deduplication discards intermediate states, the surviving entries are delivered in chronological order.

**When to use**: Item instance modified (durability updates), Permission registration, Relationship updated, Character-Personality evolution — any event where rapid updates to the same entity should collapse to the latest state.

#### 1c. `bannou-service/Services/EventBatcherWorker.cs`

Canonical `BackgroundService` wrapper that owns the flush loop for one or more batchers:

```csharp
/// <summary>
/// Generic background worker that periodically flushes one or more EventBatchers.
/// Follows the canonical BackgroundService polling loop pattern.
/// </summary>
public class EventBatcherWorker : BackgroundService
{
    // Constructor: IServiceProvider, ILogger, ITelemetryProvider,
    //              intervalSeconds, startupDelaySeconds,
    //              serviceName, workerName (for telemetry + error events),
    //              params IFlushable[] batchers
    // ExecuteAsync: canonical skeleton (startup delay, double-catch, WorkerErrorPublisher)
    // Calls FlushAsync on each batcher in sequence per cycle
    // Best-effort final flush on shutdown
}
```

Both `EventBatcher<TEntry>` and `DeduplicatingEventBatcher<TKey, TEntry>` implement `IFlushable`:

```csharp
/// <summary>
/// Common interface for batcher flush capability, enabling a single worker
/// to flush multiple batchers of different types per cycle.
/// </summary>
public interface IFlushable
{
    bool IsEmpty { get; }
    Task FlushAsync(CancellationToken ct);
}
```

This allows a single worker to flush multiple batchers — e.g., Item's worker flushes the Mode 1 created batcher, the Mode 2 modified batcher, and the Mode 1 destroyed batcher in one cycle. No need for three separate BackgroundServices per service.

Services that need fully custom flush behavior can use the batcher classes directly with their own `BackgroundService`.

### Step 2: Extend x-lifecycle Schema for Batch Variation

#### 2a. `scripts/generate-lifecycle-events.py` — Modify (frozen artifact, explicit instruction)

When an x-lifecycle entity has `batch: true`:

```yaml
x-lifecycle:
  topic_prefix: item
  ItemTemplate:
    deprecation: true
    instanceEntity: ItemInstance
    model: { ... }
  ItemInstance:
    batch: true
    model:
      instanceId: { type: string, format: uuid, primary: true, ... }
      templateId: { type: string, format: uuid, ... }
      # ... all other fields
```

**Generator behavior for `batch: true` entities:**

1. **DO NOT generate** individual lifecycle event schemas (`ItemInstanceCreatedEvent`, `ItemInstanceUpdatedEvent`, `ItemInstanceDeletedEvent`)
2. **DO generate** batch lifecycle event schemas:

```yaml
ItemInstanceBatchCreatedEvent:
  allOf:
    - $ref: 'common-events.yaml#/components/schemas/BaseServiceEvent'
  type: object
  description: Batch event containing multiple item instance creation records
  additionalProperties: false
  required: [eventName, eventId, timestamp, entries, count, windowStartedAt]
  properties:
    eventName:
      type: string
      default: item.instance.batch-created
      description: 'Event type identifier: item.instance.batch-created'
    entries:
      type: array
      items:
        $ref: '#/components/schemas/ItemInstanceBatchEntry'
      description: Individual creation records in this batch
    count:
      type: integer
      description: Number of entries in this batch
    windowStartedAt:
      type: string
      format: date-time
      description: When the accumulation window started

ItemInstanceBatchModifiedEvent:
  # Same structure, eventName: item.instance.batch-modified
  # entries items: ItemInstanceBatchModifiedEntry (includes changedFields per entry)

ItemInstanceBatchDestroyedEvent:
  # Same structure, eventName: item.instance.batch-destroyed
  # entries items: ItemInstanceBatchDestroyedEntry (includes deletedReason per entry)
```

3. **DO generate** entry models containing the lifecycle model fields:

```yaml
ItemInstanceBatchEntry:
  type: object
  description: Single item instance record within a batch event
  additionalProperties: false
  required: [instanceId, templateId, ...]  # same as individual lifecycle
  properties:
    # All fields from the x-lifecycle model definition
    # Plus createdAt, updatedAt (auto-injected like individual lifecycle)

ItemInstanceBatchModifiedEntry:
  # Same fields as ItemInstanceBatchEntry, plus:
  changedFields:
    type: array
    items: { type: string }
    description: camelCase property names of fields that changed

ItemInstanceBatchDestroyedEntry:
  # Same fields as ItemInstanceBatchEntry, plus:
  deletedReason:
    type: string
    nullable: true
    description: Reason for destruction
```

4. **DO generate** lifecycle event interfaces on batch entries (not on batch events):
   - `ItemInstanceBatchEntry` implements `ILifecycleEvent`, `ILifecycleCreatedEvent`
   - `ItemInstanceBatchModifiedEntry` implements `ILifecycleEvent`, `ILifecycleUpdatedEvent`
   - `ItemInstanceBatchDestroyedEntry` implements `ILifecycleEvent`, `ILifecycleDeletedEvent`

#### 2b. `scripts/generate-event-publishers.py` — Modify (frozen artifact, explicit instruction)

For `batch: true` entities, generate publisher methods for the batch topics only:

```csharp
// Generated: publish batch created event
public static async Task PublishItemInstanceBatchCreatedAsync(
    this IMessageBus messageBus, ItemInstanceBatchCreatedEvent evt, CancellationToken ct = default)
    => await messageBus.TryPublishAsync(ItemPublishedTopics.ItemInstanceBatchCreated, evt, ct);
```

Do NOT generate individual publishers (`PublishItemInstanceCreatedAsync` etc.) for `batch: true` entities.

#### 2c. `scripts/generate-published-topics.py` — Modify (frozen artifact, explicit instruction)

For `batch: true` entities, generate topic constants for batch topics only:

```csharp
public const string ItemInstanceBatchCreated = "item.instance.batch-created";
public const string ItemInstanceBatchModified = "item.instance.batch-modified";
public const string ItemInstanceBatchDestroyed = "item.instance.batch-destroyed";
```

### Step 3: Shared Batch Models in common-api.yaml

#### 3a. `schemas/common-api.yaml` — Add shared batch endpoint models

```yaml
# Batch operation request (shared across services with batch endpoints)
BatchOperationRequest:
  type: object
  description: Base fields for batch operation requests
  properties:
    ids:
      type: array
      items:
        type: string
        format: uuid
      description: Entity IDs to operate on
      maxItems: 100
      minItems: 1

# Batch operation response (shared across services with batch endpoints)
BatchOperationResponse:
  type: object
  description: Standard response for batch operations
  required: [succeeded, failed, totalRequested]
  properties:
    succeeded:
      type: integer
      description: Number of successfully processed entities
    failed:
      type: integer
      description: Number of entities that failed processing
    totalRequested:
      type: integer
      description: Total number of entities in the request
    failures:
      type: array
      nullable: true
      items:
        $ref: '#/components/schemas/BatchFailureEntry'
      description: Details of individual failures (null if all succeeded)

BatchFailureEntry:
  type: object
  description: Details of a single entity failure in a batch operation
  required: [entityId, reason]
  properties:
    entityId:
      type: string
      format: uuid
      description: ID of the entity that failed
    reason:
      type: string
      description: Human-readable failure reason
```

### Step 4: Apply to Item Service

#### 4a. `schemas/item-events.yaml` — Modify x-lifecycle

```yaml
x-lifecycle:
  topic_prefix: item
  ItemTemplate:
    deprecation: true
    instanceEntity: ItemInstance
    model: { ... }  # unchanged
  ItemInstance:
    batch: true       # <-- ADD THIS
    model: { ... }    # unchanged
```

Update `x-event-publications` to replace individual instance lifecycle topics with batch topics:

```yaml
x-event-publications:
    # Template lifecycle (unchanged — low frequency, no batching)
    - topic: item.template.created
      event: ItemTemplateCreatedEvent
    - topic: item.template.updated
      event: ItemTemplateUpdatedEvent
    - topic: item.template.deleted
      event: ItemTemplateDeletedEvent

    # Instance batch lifecycle (replaces individual instance lifecycle)
    - topic: item.instance.batch-created
      event: ItemInstanceBatchCreatedEvent
      description: Batch event containing accumulated item instance creations
    - topic: item.instance.batch-modified
      event: ItemInstanceBatchModifiedEvent
      description: Batch event containing accumulated item instance modifications
    - topic: item.instance.batch-destroyed
      event: ItemInstanceBatchDestroyedEvent
      description: Batch event containing accumulated item instance destructions

    # Binding events, use events, use-step events — unchanged
    # (these are already custom events, not lifecycle-generated)
```

Remove the manually-defined `ItemInstanceModifiedEvent` and `ItemInstanceDestroyedEvent` from `components/schemas` — these are replaced by the generated batch entry models.

#### 4b. `schemas/item-configuration.yaml` — Add batch config

```yaml
InstanceEventBatchIntervalSeconds:
  type: integer
  env: ITEM_INSTANCE_EVENT_BATCH_INTERVAL_SECONDS
  default: 5
  x-config-range: { min: 1, max: 300 }
  description: Interval in seconds between instance lifecycle batch event flushes

InstanceEventBatchStartupDelaySeconds:
  type: integer
  env: ITEM_INSTANCE_EVENT_BATCH_STARTUP_DELAY_SECONDS
  default: 10
  x-config-range: { min: 0, max: 300 }
  description: Startup delay before instance lifecycle batch event publishing begins

InstanceEventBatchMaxSize:
  type: integer
  env: ITEM_INSTANCE_EVENT_BATCH_MAX_SIZE
  default: 500
  x-config-range: { min: 10, max: 10000 }
  description: Maximum entries per batch event before forced flush
```

#### 4c. `plugins/lib-item/Services/ItemInstanceEventBatcher.cs` — New file

Composes three batchers (two Mode 1, one Mode 2) behind a single service + single `EventBatcherWorker`:

```csharp
/// <summary>
/// Manages batch event publishing for item instance lifecycle events.
/// Created/Destroyed use accumulating batchers (each operation is unique).
/// Modified uses deduplicating batcher (same instance may be modified multiple
/// times in a window; only the latest state matters).
/// </summary>
public class ItemInstanceEventBatcher
{
    // Mode 1: every creation is unique — append all
    private readonly EventBatcher<ItemInstanceBatchEntry> _created;

    // Mode 2: same instance modified multiple times — last-write-wins
    private readonly DeduplicatingEventBatcher<Guid, ItemInstanceBatchModifiedEntry> _modified;

    // Mode 1: every destruction is unique — append all
    private readonly EventBatcher<ItemInstanceBatchDestroyedEntry> _destroyed;

    // Public API for service methods
    public void AddCreated(ItemInstanceBatchEntry entry) => _created.Add(entry);
    public void AddModified(Guid instanceId, ItemInstanceBatchModifiedEntry entry)
        => _modified.Add(instanceId, entry);
    public void AddDestroyed(ItemInstanceBatchDestroyedEntry entry) => _destroyed.Add(entry);
}
```

The three batchers are registered as `IFlushable` with a single `EventBatcherWorker` that flushes all three per cycle. Each batcher has its own flush callback that builds and publishes its specific batch event type via the generated publisher methods.

#### 4d. `plugins/lib-item/ItemServicePlugin.cs` — Register batcher + worker

```csharp
// Register instance lifecycle event batchers
services.AddSingleton<ItemInstanceEventBatcher>();

// Single worker flushes all three batchers per cycle
services.AddSingleton<IHostedService>(sp =>
{
    var batcher = sp.GetRequiredService<ItemInstanceEventBatcher>();
    return new EventBatcherWorker(
        sp, sp.GetRequiredService<ILogger<EventBatcherWorker>>(),
        sp.GetRequiredService<ITelemetryProvider>(),
        intervalSeconds: config.InstanceEventBatchIntervalSeconds,
        startupDelaySeconds: config.InstanceEventBatchStartupDelaySeconds,
        serviceName: "item", workerName: "InstanceEventBatcher",
        batcher.AllFlushables);  // IFlushable[] from the three internal batchers
});
```

#### 4e. `plugins/lib-item/ItemService.cs` — Replace inline publishing

In all instance lifecycle operations, replace:
```csharp
await _messageBus.PublishItemInstanceCreatedAsync(event, ct);
```

With:
```csharp
_instanceEventBatcher.AddCreated(entryData);  // synchronous, non-blocking
```

Note: `Add` is synchronous (ConcurrentQueue/ConcurrentDictionary write). No `await` needed. The batcher worker handles async publishing on the flush cycle.

### Step 5: Normalize Permission Service

#### 5a. Refactor `RegistrationEventBatcher` to use shared `DeduplicatingEventBatcher<TKey, TEntry>`

Permission uses Mode 2 (same service re-registers; only latest registration matters). Replace the hand-rolled ConcurrentDictionary + Interlocked.Exchange + BackgroundService with:

```csharp
public class RegistrationEventBatcher : BackgroundService
{
    private readonly DeduplicatingEventBatcher<string, PermissionRegistrationEntry> _batcher;

    public RegistrationEventBatcher(...)
    {
        _batcher = new DeduplicatingEventBatcher<string, PermissionRegistrationEntry>(
            FlushRegistrationsAsync, logger);
    }

    internal void Add(string serviceId, string? version)
        => _batcher.Add(serviceId, new PermissionRegistrationEntry { ... });

    // ExecuteAsync: canonical skeleton calling _batcher.FlushAsync()
    // FlushRegistrationsAsync: builds PermissionServicesRegistered event, publishes
}
```

This is a pure refactor — external behavior is identical. The `permission.services-registered` event is a custom event (not lifecycle-generated), so no schema changes are needed.

### Step 6: Structural Tests

#### 6a. `structural-tests/StructuralTests.cs` — Add batch lifecycle validation (frozen artifact, explicit instruction)

**Test: `BatchLifecycleEntities_HaveBatchEventPublications`**
- Parse all `*-events.yaml` for x-lifecycle entities with `batch: true`
- Verify that batch event publications exist (`*.batch-created`, `*.batch-modified`, `*.batch-destroyed`)
- Verify that individual lifecycle publications do NOT exist (`*.created`, `*.updated`, `*.deleted`)

**Test: `BatchLifecycleEntities_CallBatchPublishers`**
- For each plugin with batch lifecycle entities, verify the plugin assembly references the generated batch publisher methods
- Extends the existing `Service_CallsAllGeneratedEventPublishers` pattern

**Test: `XReferencesServices_WithLifecycleInstances_ShouldConsiderBatchEvents`** (informational)
- Parse all `*-api.yaml` for `x-references` targeting `character` (or other character-scale entities)
- Cross-reference against `*-events.yaml` for `x-lifecycle` instance entities in those services
- For each instance entity WITHOUT `batch: true`, emit an informational message: "Service {X} has x-references targeting character and lifecycle entity {Y} — consider batch: true for NPC-scale event volume"
- Gated by `SkipUnless` (informational tier) — does not block CI, surfaces during explicit informational test runs

#### 6b. `structural-tests/SchemaParser.cs` — Extend for batch flag (frozen artifact, explicit instruction)

Add `batch: true` parsing to `GetLifecycleEntities()` return type so structural tests can inspect the flag.

### Step 7: Regenerate and Build

```bash
# Regenerate lifecycle events for item (picks up batch: true)
scripts/generate-service-events.sh item

# Regenerate all (to verify no breakage)
scripts/generate-all-services.sh

# Build
dotnet build

# Run structural tests
make test-structural
```

### Step 8: Unit Tests for EventBatcher Helpers

#### 8a. Test project placement

Both helpers live in `bannou-service/`, so their tests go in an appropriate test project (likely a new `bannou-service.tests/` or in the structural tests if testing generic infrastructure).

**EventBatcher\<TEntry\> (Mode 1 — accumulating) tests**:
- `Add_SingleEntry_AccumulatesForFlush`
- `Add_MultipleEntries_AllPreserved` (no deduplication)
- `Add_SameDataTwice_BothPreserved` (confirms append-all, not last-write-wins)
- `AddRange_MultipleEntries_AllAccumulated`
- `FlushAsync_EmptyBatcher_NoCallback`
- `FlushAsync_WithEntries_InvokesCallbackAndClears`
- `FlushAsync_ConcurrentAdds_NoLostEntries` (thread safety)
- `FlushAsync_AtomicDrain_NewAddsGoToNextBatch`
- `FlushAsync_PreservesInsertionOrder`

**DeduplicatingEventBatcher\<TKey, TEntry\> (Mode 2 — deduplicating) tests**:
- `Add_SingleEntry_AccumulatesForFlush`
- `Add_SameKey_LastWriteWins` (latest entry replaces previous)
- `Add_DifferentKeys_BothPreserved`
- `FlushAsync_EmptyBatcher_NoCallback`
- `FlushAsync_WithEntries_InvokesCallbackAndClears`
- `FlushAsync_ConcurrentAdds_NoLostEntries` (thread safety)
- `FlushAsync_AtomicSwap_NewAddsGoToFreshDictionary`

**IFlushable + EventBatcherWorker tests**:
- `Worker_FlushesAllBatchers_PerCycle`
- `Worker_SkipsEmptyBatchers`
- `Worker_GracefulShutdown_FinalFlush`

---

## Files Created/Modified Summary

| File | Action |
|------|--------|
| `bannou-service/Services/EventBatcher.cs` | Create (Mode 1 — accumulating, ConcurrentQueue) |
| `bannou-service/Services/DeduplicatingEventBatcher.cs` | Create (Mode 2 — last-write-wins, ConcurrentDictionary) |
| `bannou-service/Services/IFlushable.cs` | Create (shared interface for multi-batcher workers) |
| `bannou-service/Services/EventBatcherWorker.cs` | Create (canonical worker, flushes IFlushable[]) |
| `schemas/common-api.yaml` | Modify (add BatchOperationRequest/Response/FailureEntry) |
| `schemas/item-events.yaml` | Modify (add `batch: true` to ItemInstance, update x-event-publications) |
| `schemas/item-configuration.yaml` | Modify (add batch interval/delay/max config) |
| `scripts/generate-lifecycle-events.py` | Modify (handle `batch: true` — generate batch events only) |
| `scripts/generate-event-publishers.py` | Modify (generate batch publishers for `batch: true` entities) |
| `scripts/generate-published-topics.py` | Modify (generate batch topic constants for `batch: true` entities) |
| `plugins/lib-item/Services/ItemInstanceEventBatcher.cs` | Create (uses shared EventBatcher) |
| `plugins/lib-item/ItemServicePlugin.cs` | Modify (register batcher as Singleton + IHostedService) |
| `plugins/lib-item/ItemService.cs` | Modify (replace inline instance event publishing with batcher.Add) |
| `plugins/lib-permission/RegistrationEventBatcher.cs` | Modify (refactor to use shared EventBatcher) |
| `structural-tests/StructuralTests.cs` | Modify (add batch lifecycle validation tests) |
| `structural-tests/SchemaParser.cs` | Modify (parse `batch: true` flag) |

**Generated files affected** (after regeneration):
- `schemas/Generated/item-lifecycle-events-resolved.yaml` — batch event schemas instead of individual
- `bannou-service/Generated/Events/ItemLifecycleEvents.cs` — batch event models + entry models
- `plugins/lib-item/Generated/ItemEventPublisher.cs` — batch publisher methods only for instance entity
- `plugins/lib-item/Generated/ItemPublishedTopics.cs` — batch topic constants for instance entity

---

## Future Applications

### The x-references Heuristic

A structural analysis revealed a strong correlation between `x-references` declarations and batch event candidacy. **16 services** declare `x-references` in the codebase. Of those, all but one (worldstate) store per-character or per-entity instance data created by external callers. At 100K NPC scale, virtually all of them become high-frequency.

**The rule**: If a service declares `x-references` targeting `character` (or any entity that exists at NPC scale) AND has `x-lifecycle` instance entities, those instance entities are batch candidates. The `x-references` declaration is a structural signal that the service's data is externally-created and externally-owned — exactly the profile where lifecycle events are informational rather than functional.

**Why this works**: Services with `x-references` exist to store dependent data for entities owned by other services. Their cleanup goes through lib-resource (CASCADE/RESTRICT/DETACH), not event subscription. Their data is created by external API calls (orchestrators, game logic, behavior systems), not self-created by the plugin. Their events announce "we recorded this" — informational notifications with no downstream behavioral dependency.

**Structural test opportunity**: A structural test could flag `x-lifecycle` instance entities in services that declare `x-references` targeting character but do NOT use `batch: true`. This would be informational (not blocking) — a nudge that says "this entity's lifecycle events will be high-frequency at NPC scale; consider `batch: true`."

### Complete x-references Audit

All 16 services with `x-references`, assessed for batch lifecycle candidacy:

| Service | x-references target | Instance Entity | NPC-Scale Frequency | Batch? |
|---------|---------------------|-----------------|---------------------|--------|
| **Item** | *(via DI Listener)* | ItemInstance | 100K × loot/combat/trade | **Yes — this plan's primary target** |
| **Affix** (aspirational) | *(via DI Listener)* | AffixInstance | 1:1 with ItemInstance | **Yes — deep dive already plans batching** |
| **Character-Encounter** | character | Encounter | 100K × social/combat interactions | **Yes** |
| **Character-History** | character | Participation | 1000s per game session | **Yes** |
| **Character-Personality** | character | Personality, CombatPreferences | 100K × personality evolution per interaction | **Yes** — initially assessed as "medium" but at NPC scale every significant interaction triggers evolution |
| **Transit** | location, character | Journey, Discovery | 10K+ concurrent journeys | **Yes** |
| **Status** | character | StatusInstance | 100K × ~50 concurrent effects | **Yes** |
| **Currency** | *(account deletion event)* | Credit/Debit ops | Economy-wide continuous | **Yes** |
| **Quest** | character | QuestInstance, Objective | Kill-count/collect at combat frequency | **Yes** |
| **Achievement** | character | Progress | Auto-unlock from analytics at scale | **Yes** — bulk unlock waves during NPC milestone events |
| **Collection** | character | CollectionInstance, Entry | NPC discovery/bestiary/recipe at interaction frequency | **Yes** — initially "medium" but NPCs unlock entries at every combat/crafting/discovery event |
| **Obligation** | character | *(no x-lifecycle)* | Contract activation/termination across all NPC contracts | **Potential** — no x-lifecycle yet; obligation cache rebuilds from contract events |
| **Divine** | character, game-service | Blessing | God-actor blessing grants to NPCs | **Yes** — regional watchers grant/revoke blessings continuously across their domain |
| **Relationship** | character, realm, location | Relationship | NPC social graph formation at interaction frequency | **Yes** — NPCs forming/dissolving relationships at every meaningful encounter |
| **Actor** | character | Actor | 100K spawn/despawn as NPCs enter/leave regions | **Yes** — actor lifecycle is the highest-frequency operation in the entire system |
| **Realm-History** | realm | *(no x-lifecycle, custom events)* | Event participation recording | **Potential** — lower frequency (per-realm, not per-character), but world events can trigger mass participation recording |
| **License** | character | Board | NPC skill node unlocking at progression frequency | **Yes** — NPCs making progression decisions at GOAP planning frequency |
| **Faction** | character, realm, location | *(no x-lifecycle)* | Membership changes at social interaction frequency | **Potential** — no x-lifecycle yet; membership events would batch well |
| **Worldstate** | realm, game-service | CalendarTemplate | One clock per realm, not per entity | **No** — genuinely low frequency; realm-scoped, not character-scoped |

### Summary by Readiness

**Ready now** (has x-lifecycle instance entities, implemented or near-implementation):
- Item (ItemInstance) — **this plan**
- Character-Encounter (Encounter, EncounterType instances)
- Character-History (Participation)
- Character-Personality (Personality, CombatPreferences)
- Transit (Journey, Discovery, Connection)
- Status (StatusInstance)
- Currency (custom events, not x-lifecycle — would need migration or custom batcher)
- Quest (QuestInstance, Objective)
- Achievement (Progress)
- Collection (CollectionInstance, Entry)
- Divine (Blessing)
- Relationship (Relationship)
- Actor (Actor)
- Leaderboard (Entry — sorted set, custom events)
- License (Board)

**Not yet ready** (aspirational or no x-lifecycle):
- Affix (aspirational — no code exists)
- Obligation (no x-lifecycle — uses contract event consumption)
- Faction (no x-lifecycle — seed-based growth)
- Realm-History (no x-lifecycle — custom events)

**Not a candidate**:
- Worldstate (realm-scoped, not character-scoped; genuinely low frequency)

---

## Related Issues

- [#559](https://github.com/beyond-immersion/bannou-service/issues/559) — Item: Batch instance destruction (batch endpoint design)
- [#484](https://github.com/beyond-immersion/bannou-service/issues/484) — Inventory: item event consumption for counter sync (open design question — if event subscription is chosen, batch events would be the consumed format)
- [#490](https://github.com/beyond-immersion/bannou-service/issues/490) — Affix: full implementation (future consumer of this pattern)

## Verification

1. `dotnet build` — compiles without errors
2. `make test-structural` — all structural tests pass including new batch lifecycle validators
3. `dotnet test plugins/lib-item.tests/` — item unit tests pass (updated for batcher)
4. Verify no individual `item.instance.created`/`modified`/`destroyed` topics exist in generated code
5. Verify batch topics (`item.instance.batch-created` etc.) exist in generated code
6. Verify Permission `RegistrationEventBatcher` behavior is identical after refactor to shared helper
