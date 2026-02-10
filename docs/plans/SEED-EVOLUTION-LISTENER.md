# Plan: Seed Evolution Listener Pattern (DI Provider for Outbound Notifications)

## Context

The Seed service (L2) currently notifies consumers of growth, phase changes, and capability updates exclusively via broadcast events (`seed.growth.updated`, `seed.phase.changed`, `seed.capability.updated`). As the number of L4 consumers grows (lib-status, lib-puppetmaster, lib-dungeon, lib-collection), this creates two problems:

1. **Broadcast waste**: Every consumer receives every event for every seed type, then filters locally. lib-status only cares about `guardian` seeds but gets flooded with `dungeon_core` updates.
2. **No targeted dispatch**: Seed has no awareness of who's listening or what they care about. There's no mechanism for a consumer to say "I only care about these seed types."

The codebase already has four established DI provider patterns for cross-layer data flow (IVariableProviderFactory, IPrerequisiteProviderFactory, IBehaviorDocumentProvider, ISeededResourceProvider). All follow the same shape: interface in `bannou-service/Providers/`, L4 implements and registers as Singleton, lower-layer discovers via `IEnumerable<T>` injection with graceful degradation.

This plan adds a fifth: **ISeedEvolutionListener** -- the same pattern but in the opposite direction. Instead of L2 *pulling data from* L4 (like IVariableProviderFactory), L2 *pushes notifications to* L4 with seed-type filtering. Existing broadcast events remain as the public contract for external/distributed consumers.

**Relationship to #375**: The Collection-Seed-Status pipeline described in issue #375 identifies lib-status and lib-puppetmaster as primary consumers of seed evolution. This pattern gives them targeted, in-process notifications without subscribing to the full event firehose.

**Open question resolutions**:
- Listeners dispatch AFTER state is saved and events are published (events remain the authoritative mechanism; listeners are an in-process optimization for co-located services).
- Growth notifications aggregate all domain changes into a single callback per `RecordGrowthInternalAsync` invocation (richer than per-domain events).
- Listener failures are logged as warnings and never affect core seed logic or other listeners.

---

## Implementation Steps

### Step 1: Define Interface and Context Types

#### 1a. `bannou-service/Providers/ISeedEvolutionListener.cs`

New file following the established provider pattern (see IVariableProviderFactory.cs, IPrerequisiteProviderFactory.cs for structure and XML documentation style).

**Interface: `ISeedEvolutionListener`**:
- `IReadOnlySet<string> InterestedSeedTypes { get; }` -- seed type codes this listener cares about. Empty set = all types (wildcard).
- `Task OnGrowthRecordedAsync(SeedGrowthNotification notification, CancellationToken ct)` -- called after growth is recorded (aggregated across all domains in a single recording operation)
- `Task OnPhaseChangedAsync(SeedPhaseNotification notification, CancellationToken ct)` -- called after a phase transition (progression or regression)
- `Task OnCapabilitiesChangedAsync(SeedCapabilityNotification notification, CancellationToken ct)` -- called after capability manifest is recomputed

**Record: `SeedGrowthNotification`**:
- `Guid SeedId` -- the seed that grew
- `string SeedTypeCode` -- seed type for consumer filtering
- `Guid OwnerId` -- entity that owns the seed
- `string OwnerType` -- owner entity type discriminator
- `IReadOnlyList<DomainChange> DomainChanges` -- per-domain changes from this recording
- `float TotalGrowth` -- aggregate growth across all domains after recording
- `bool CrossPollinated` -- whether this growth came from cross-pollination
- `string Source` -- growth source identifier (e.g., "collection", "api")

**Record: `DomainChange`**:
- `string Domain` -- domain path
- `float PreviousDepth` -- depth before this recording
- `float NewDepth` -- depth after this recording

**Record: `SeedPhaseNotification`**:
- `Guid SeedId`, `string SeedTypeCode`, `Guid OwnerId`, `string OwnerType`
- `string PreviousPhase` -- phase code before transition
- `string NewPhase` -- phase code after transition
- `float TotalGrowth` -- current total growth
- `bool Progressed` -- true if moving to a higher phase, false if regressing (decay)

**Record: `SeedCapabilityNotification`**:
- `Guid SeedId`, `string SeedTypeCode`, `Guid OwnerId`, `string OwnerType`
- `int Version` -- monotonically increasing manifest version
- `int UnlockedCount` -- number of currently unlocked capabilities
- `IReadOnlyList<CapabilitySnapshot> Capabilities` -- full capability state

**Record: `CapabilitySnapshot`**:
- `string CapabilityCode`, `string Domain`, `float Fidelity`, `bool Unlocked`

**XML Documentation style**: Follow the established header/remarks/example pattern from IVariableProviderFactory.cs. Include:
- Top-of-file `// =====` banner comment explaining the pattern
- `<remarks>` block with bullet list explaining the dependency inversion
- `<code>` example showing a minimal implementation (a StatusSeedEvolutionListener sketch)

**Namespace**: `BeyondImmersion.BannouService.Providers` (same as all other provider interfaces).

### Step 2: Add Dispatch Helpers to SeedService

#### 2a. `plugins/lib-seed/SeedService.cs` -- Constructor changes

Add `IEnumerable<ISeedEvolutionListener>` to the constructor:
- New field: `private readonly IReadOnlyList<ISeedEvolutionListener> _evolutionListeners;`
- Constructor parameter: `IEnumerable<ISeedEvolutionListener> evolutionListeners`
- Assignment: `_evolutionListeners = evolutionListeners.ToList();` (materialize once for repeated iteration)
- Add startup log: `"Seed service initialized with {Count} evolution listeners: {Listeners}"` listing listener type names

Current constructor signature (line 33-41):
```csharp
public SeedService(
    IMessageBus messageBus,
    IStateStoreFactory stateStoreFactory,
    IDistributedLockProvider lockProvider,
    ILogger<SeedService> logger,
    SeedServiceConfiguration configuration,
    IEventConsumer eventConsumer,
    IGameServiceClient gameServiceClient)
```

New constructor signature:
```csharp
public SeedService(
    IMessageBus messageBus,
    IStateStoreFactory stateStoreFactory,
    IDistributedLockProvider lockProvider,
    ILogger<SeedService> logger,
    SeedServiceConfiguration configuration,
    IEventConsumer eventConsumer,
    IGameServiceClient gameServiceClient,
    IEnumerable<ISeedEvolutionListener> evolutionListeners)
```

#### 2b. `plugins/lib-seed/SeedService.cs` -- Private dispatch helpers

Three private async methods, placed near the bottom of the file (before `ComputePhaseInfo`):

**`DispatchGrowthRecordedAsync`**:
```csharp
private async Task DispatchGrowthRecordedAsync(
    SeedModel seed, SeedGrowthNotification notification, CancellationToken ct)
```
- Iterate `_evolutionListeners`
- Skip listener if `InterestedSeedTypes.Count > 0` and does not contain `seed.SeedTypeCode`
- Call `listener.OnGrowthRecordedAsync(notification, ct)` in try-catch
- Catch: log warning with listener type name, seed ID, and exception. Never rethrow.

**`DispatchPhaseChangedAsync`**:
```csharp
private async Task DispatchPhaseChangedAsync(
    SeedModel seed, SeedPhaseNotification notification, CancellationToken ct)
```
- Same filtering and error handling pattern.

**`DispatchCapabilitiesChangedAsync`**:
```csharp
private async Task DispatchCapabilitiesChangedAsync(
    SeedModel seed, SeedCapabilityNotification notification, CancellationToken ct)
```
- Same filtering and error handling pattern.

All three methods are no-ops (immediate return) when `_evolutionListeners.Count == 0` to avoid allocation overhead when no listeners are registered.

### Step 3: Wire Dispatch into RecordGrowthInternalAsync

#### 3a. Growth notification dispatch (after line 1549 -- after `growthStore.SaveAsync`)

Build `SeedGrowthNotification` from the accumulated domain changes (collect `DomainChange` records during the foreach loop at line 1521). Call `DispatchGrowthRecordedAsync` after the growth store save.

The domain change list should be built inside the existing foreach loop:
```csharp
var domainChanges = new List<DomainChange>(entries.Length);
foreach (var (domain, amount) in entries)
{
    // ... existing depth computation ...
    domainChanges.Add(new DomainChange(domain, previousDepth, newDepth));
    // ... existing event publish ...
}
// After growthStore.SaveAsync:
await DispatchGrowthRecordedAsync(seed, new SeedGrowthNotification(
    seed.SeedId, seed.SeedTypeCode, seed.OwnerId, seed.OwnerType,
    domainChanges, growth.Domains.Values.Sum(d => d.Depth),
    CrossPollinated: false, Source: source), cancellationToken);
```

#### 3b. Phase change notification dispatch (after line 1611 -- after phase change event publish)

Inside the `if (previousPhase != seed.GrowthPhase)` block, after the existing `TryPublishAsync`:
```csharp
await DispatchPhaseChangedAsync(seed, new SeedPhaseNotification(
    seed.SeedId, seed.SeedTypeCode, seed.OwnerId, seed.OwnerType,
    previousPhase, seed.GrowthPhase, newTotalGrowth,
    Progressed: true), cancellationToken);
```

#### 3c. Capability notification dispatch

This happens in `ComputeAndCacheManifestAsync` (line 1886). After the existing `seed.capability.updated` event publish (line 1929), add:
```csharp
await DispatchCapabilitiesChangedAsync(seed, new SeedCapabilityNotification(
    seed.SeedId, seed.SeedTypeCode, seed.OwnerId, seed.OwnerType,
    manifest.Version, capabilities.Count(c => c.Unlocked),
    capabilities.Select(c => new CapabilitySnapshot(
        c.CapabilityCode, c.Domain, c.Fidelity, c.Unlocked)).ToList()), cancellationToken);
```

Note: `ComputeAndCacheManifestAsync` currently takes `SeedModel seed` as its first parameter -- the seed data needed for dispatch is already available.

### Step 4: Wire Dispatch into ApplyCrossPollination

#### 4a. Growth notification for cross-pollinated siblings (after line 1727)

Same pattern as 3a but with `CrossPollinated: true`. Build domain changes during the foreach loop at line 1700, dispatch after `growthStore.SaveAsync`.

#### 4b. Phase change notification for cross-pollinated siblings (after line 1755)

Inside the `if (previousPhase != seed.GrowthPhase)` block at line 1739, after the existing event publish:
```csharp
await DispatchPhaseChangedAsync(seed, new SeedPhaseNotification(
    seed.SeedId, seed.SeedTypeCode, seed.OwnerId, seed.OwnerType,
    previousPhase, seed.GrowthPhase, newTotalGrowth,
    Progressed: true), cancellationToken);
```

Note: `ApplyCrossPollination` already loads `SeedModel seed` (line 1690) and has `SeedTypeDefinitionModel seedType` (parameter), so all data is available.

### Step 5: Wire Dispatch into RecomputeSeedsForTypeAsync

#### 5a. Phase change notification during type definition recomputation (after line 1820)

Inside the `if (previousPhase != seed.GrowthPhase)` block at line 1799, after the existing event publish:
```csharp
await DispatchPhaseChangedAsync(seed, new SeedPhaseNotification(
    seed.SeedId, seed.SeedTypeCode, seed.OwnerId, seed.OwnerType,
    previousPhase, seed.GrowthPhase, seed.TotalGrowth,
    Progressed: direction == PhaseChangeDirection.Progressed), cancellationToken);
```

### Step 6: Wire Dispatch into SeedDecayWorkerService

#### 6a. `plugins/lib-seed/SeedDecayWorkerService.cs` -- Constructor changes

Add `IEnumerable<ISeedEvolutionListener>` to the worker's constructor:
- New field: `private readonly IReadOnlyList<ISeedEvolutionListener> _evolutionListeners;`
- Constructor parameter: `IEnumerable<ISeedEvolutionListener> evolutionListeners`
- Assignment: `_evolutionListeners = evolutionListeners.ToList();`

This works because both the worker (HostedService, Singleton-like lifetime) and listeners (Singleton) share compatible lifetimes.

#### 6b. Add dispatch helper to worker

Add the same `DispatchPhaseChangedAsync` helper (private method) to the worker class. This is a small amount of duplication but avoids coupling the worker to SeedService internals. The helper follows the identical pattern: filter by InterestedSeedTypes, try-catch per listener, log warnings on failure.

Alternatively, extract the dispatch logic into a shared static helper on a `SeedEvolutionDispatcher` utility class to avoid the duplication. Decision: use a small static utility class in `plugins/lib-seed/` since the dispatch logic is identical:

#### 6c. `plugins/lib-seed/SeedEvolutionDispatcher.cs` (new file)

Internal static class with three methods:
```csharp
internal static class SeedEvolutionDispatcher
{
    internal static async Task DispatchGrowthRecordedAsync(
        IReadOnlyList<ISeedEvolutionListener> listeners,
        string seedTypeCode,
        SeedGrowthNotification notification,
        ILogger logger,
        CancellationToken ct) { ... }

    internal static async Task DispatchPhaseChangedAsync(
        IReadOnlyList<ISeedEvolutionListener> listeners,
        string seedTypeCode,
        SeedPhaseNotification notification,
        ILogger logger,
        CancellationToken ct) { ... }

    internal static async Task DispatchCapabilitiesChangedAsync(
        IReadOnlyList<ISeedEvolutionListener> listeners,
        string seedTypeCode,
        SeedCapabilityNotification notification,
        ILogger logger,
        CancellationToken ct) { ... }
}
```

Each method: early-return if `listeners.Count == 0`, filter by `InterestedSeedTypes`, try-catch per listener with warning log, never rethrow.

Then **replace** the private helpers in Step 2b with calls to `SeedEvolutionDispatcher`, and use the same dispatcher from `SeedDecayWorkerService`. Both SeedService and the worker delegate to the shared dispatcher.

#### 6d. Wire dispatch into decay worker's phase regression path

The decay worker publishes `seed.phase.changed` when decay causes phase regression. After that existing event publish, call:
```csharp
await SeedEvolutionDispatcher.DispatchPhaseChangedAsync(
    _evolutionListeners, seed.SeedTypeCode,
    new SeedPhaseNotification(..., Progressed: false), _logger, ct);
```

### Step 7: Build and Verify

```bash
dotnet build
```

Verify no compilation errors. The `IEnumerable<ISeedEvolutionListener>` will resolve to an empty collection when no L4 listeners are registered (standard DI behavior), so all existing functionality is unchanged.

### Step 8: Unit Tests

#### 8a. `plugins/lib-seed.tests/SeedEvolutionListenerTests.cs` (new test file)

Following established testing patterns from existing seed tests. Mock `ISeedEvolutionListener` to verify dispatch behavior.

**Dispatch filtering tests**:
- `DispatchGrowthRecorded_ListenerInterestedInType_ReceivesNotification` -- register listener interested in "guardian", record growth on guardian seed, verify callback invoked
- `DispatchGrowthRecorded_ListenerNotInterestedInType_SkipsNotification` -- register listener interested in "guardian", record growth on "dungeon_core" seed, verify callback NOT invoked
- `DispatchGrowthRecorded_ListenerInterestedInAllTypes_ReceivesAll` -- register listener with empty InterestedSeedTypes set, verify it receives all notifications
- `DispatchGrowthRecorded_NoListeners_NoErrors` -- no listeners registered, verify growth recording succeeds without errors

**Growth notification content tests**:
- `GrowthNotification_ContainsAllDomainChanges` -- record batch growth across 3 domains, verify notification contains all 3 DomainChange entries with correct previous/new depths
- `GrowthNotification_CrossPollinatedFlag_SetCorrectly` -- verify cross-pollinated growth dispatches with `CrossPollinated = true`

**Phase change notification tests**:
- `PhaseChanged_ListenerReceivesNotification` -- record growth that crosses phase threshold, verify OnPhaseChangedAsync called with correct previous/new phase
- `PhaseChanged_Progression_ProgressedIsTrue` -- verify Progressed flag on growth-triggered phase change
- `PhaseChanged_Regression_ProgressedIsFalse` -- verify Progressed flag on decay-triggered phase regression (tests dispatch from SeedEvolutionDispatcher directly)

**Capability notification tests**:
- `CapabilityUpdated_ListenerReceivesManifest` -- trigger capability recomputation, verify OnCapabilitiesChangedAsync called with correct snapshot

**Error isolation tests**:
- `ListenerThrows_OtherListenersStillCalled` -- register two listeners, first throws, verify second still receives notification
- `ListenerThrows_CoreSeedLogicUnaffected` -- register throwing listener, verify growth recording still returns OK and state is saved correctly

**Constructor tests**:
- `SeedService_ConstructorWithListeners_IsValid` -- update existing constructor validation test to include the new parameter

All tests use `Mock<ISeedEvolutionListener>` with `Callback` capture pattern to verify notification content, consistent with existing seed test patterns.

---

## Files Created/Modified Summary

| File | Action |
|------|--------|
| `bannou-service/Providers/ISeedEvolutionListener.cs` | Create (interface + 5 record types) |
| `plugins/lib-seed/SeedEvolutionDispatcher.cs` | Create (static dispatch utility) |
| `plugins/lib-seed/SeedService.cs` | Modify (add constructor param, dispatch calls at 6 locations) |
| `plugins/lib-seed/SeedDecayWorkerService.cs` | Modify (add constructor param, dispatch call at 1 location) |
| `plugins/lib-seed.tests/SeedEvolutionListenerTests.cs` | Create (12+ tests) |

## Verification

1. `dotnet build` -- compiles without errors or warnings
2. `dotnet test plugins/lib-seed.tests/` -- all unit tests pass (existing + new)
3. Verify existing seed tests still pass (constructor change must not break existing test setup)
4. Verify no CS1591 warnings on new public types (all records and interface methods need XML docs)
5. Manual inspection: `IEnumerable<ISeedEvolutionListener>` resolves to empty when no plugins register implementations (standard DI behavior, no crash)
