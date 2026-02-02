# Resource Lifecycle Management Plan

## Problem Statement

Foundational resources (Character, Realm, Location, Species) have consumers they cannot predict. The current approach in `CharacterService.CheckCharacterReferencesAsync()` violates the service hierarchy by hardcoding knowledge of L3/L4 consumers.

**Goal**: A schema-first, automated system for:
1. Tracking references from any plugin to foundational resources
2. Gating deletion on zero references (with configurable grace period per-resource-type)
3. Executing cascading cleanup in parallel when deletion is approved
4. Minimal maintenance when adding new plugins

---

## Chosen Approach: Dedicated lib-resource Plugin

### Layer Placement

**L1 (App Foundation)** - alongside account, auth, connect, permission, contract

Rationale: Any layer can depend on L1. Resources being tracked can be at L2 or higher.

### Schema Extension: x-references

Consumer services declare references in their API schema:

```yaml
# actor-api.yaml
info:
  x-references:
    - target: character                      # Resource type being referenced
      sourceType: actor                      # This service's entity type (opaque string identifier)
      field: characterId                     # Field holding the reference
      onDelete: cascade                      # cascade | restrict | detach
      cleanup:
        endpoint: /actor/cleanup-by-character
        payloadTemplate: '{"characterId": "{{resourceId}}"}'

# Foundational services define grace periods and cleanup policies
# character-api.yaml
info:
  x-resource-lifecycle:
    gracePeriod: P7D                         # ISO 8601 duration - 7 days after refcount=0
    cleanupPolicy: best_effort               # best_effort | all_required
```

### Generated Code (TENET T5 Compliant - Typed Events)

From `x-references`, the generator produces typed event publishing:

1. **Reference registration** in create methods:
```csharp
// TENET T5 compliant: Uses typed event, not anonymous object
// sourceType is an opaque string - lib-resource doesn't enumerate higher-layer services
await _messageBus.PublishAsync("resource.reference.registered",
    new ResourceReferenceRegisteredEvent
    {
        ResourceType = "character",
        ResourceId = actor.CharacterId,
        SourceType = "actor",  // Opaque string identifier, not enum
        SourceId = actor.ActorId,
        Timestamp = DateTimeOffset.UtcNow
    }, cancellationToken: ct);
```

2. **Reference unregistration** in delete methods:
```csharp
// TENET T5 compliant: Uses typed event, not anonymous object
await _messageBus.PublishAsync("resource.reference.unregistered",
    new ResourceReferenceUnregisteredEvent
    {
        ResourceType = "character",
        ResourceId = actor.CharacterId,
        SourceType = "actor",  // Opaque string identifier, not enum
        SourceId = actor.ActorId,
        Timestamp = DateTimeOffset.UtcNow
    }, cancellationToken: ct);
```

3. **Cleanup callback registration** at startup

### lib-resource API Surface

```yaml
# resource-api.yaml
servers:
  - url: http://localhost:5012

info:
  title: Resource Lifecycle API
  version: 1.0.0
  description: |
    Resource reference tracking and lifecycle management.
    Enables foundational services (L2) to safely delete resources
    by tracking references from higher-layer consumers (L3/L4)
    without violating the service hierarchy.

paths:
  /resource/register:
    post:
      operationId: RegisterReference
      x-permissions:
        - role: developer
      summary: Register a reference to a resource
      description: Records that sourceType:sourceId references resourceType:resourceId
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/RegisterReferenceRequest'
      responses:
        '200':
          description: Reference registered
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/RegisterReferenceResponse'

  /resource/unregister:
    post:
      operationId: UnregisterReference
      x-permissions:
        - role: developer
      summary: Remove a reference to a resource
      description: Records that sourceType:sourceId no longer references resourceType:resourceId
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/UnregisterReferenceRequest'
      responses:
        '200':
          description: Reference unregistered
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/UnregisterReferenceResponse'

  /resource/check:
    post:
      operationId: CheckReferences
      x-permissions:
        - role: developer
      summary: Check reference count and cleanup eligibility
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CheckReferencesRequest'
      responses:
        '200':
          description: Reference status
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CheckReferencesResponse'

  /resource/list:
    post:
      operationId: ListReferences
      x-permissions:
        - role: developer
      summary: List all references to a resource
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListReferencesRequest'
      responses:
        '200':
          description: List of references
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ListReferencesResponse'

  /resource/cleanup/define:
    post:
      operationId: DefineCleanupCallback
      x-permissions:
        - role: admin
      summary: Define cleanup callbacks for a resource type
      description: |
        Services call this at startup to register their cleanup endpoints.
        When a resource is deleted, these callbacks are invoked.
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/DefineCleanupRequest'
      responses:
        '200':
          description: Cleanup callback defined
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/DefineCleanupResponse'

  /resource/cleanup/execute:
    post:
      operationId: ExecuteCleanup
      x-permissions:
        - role: developer
      summary: Execute cleanup for a resource
      description: |
        Validates refcount=0, grace period passed, acquires distributed lock,
        re-validates under lock, then executes all cleanup callbacks.
        Returns Conflict if refcount changed during execution.
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ExecuteCleanupRequest'
      responses:
        '200':
          description: Cleanup executed
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ExecuteCleanupResponse'
        '409':
          description: Conflict - refcount changed during cleanup

components:
  schemas:
    # ═══════════════════════════════════════════════════════════════
    # Enums (Only for lib-resource's OWN closed sets)
    # NOTE: resourceType and sourceType are intentionally STRINGS,
    # not enums. lib-resource (L1) must not enumerate L2+ services
    # or entity types - that would create implicit coupling.
    # See SCHEMA-RULES.md "When NOT to Create Enums"
    # ═══════════════════════════════════════════════════════════════

    OnDeleteAction:
      type: string
      enum: [CASCADE, RESTRICT, DETACH]
      description: |
        CASCADE: Delete dependent entities when resource is deleted
        RESTRICT: Block resource deletion if references exist
        DETACH: Set reference to null when resource is deleted

    CleanupPolicy:
      type: string
      enum: [BEST_EFFORT, ALL_REQUIRED]
      description: |
        BEST_EFFORT: Proceed with deletion even if some callbacks fail
        ALL_REQUIRED: Abort deletion if any callback fails

    # ═══════════════════════════════════════════════════════════════
    # Request/Response Models
    # ═══════════════════════════════════════════════════════════════

    RegisterReferenceRequest:
      type: object
      required: [resourceType, resourceId, sourceType, sourceId]
      properties:
        resourceType:
          type: string
          description: Type of resource being referenced (opaque identifier, e.g., "character", "realm")
        resourceId:
          type: string
          format: uuid
          description: ID of the resource being referenced
        sourceType:
          type: string
          description: Type of entity holding the reference (opaque identifier, e.g., "actor", "scene")
        sourceId:
          type: string
          format: uuid
          description: ID of the entity holding the reference
        idempotencyKey:
          type: string
          nullable: true
          description: Optional key for idempotent registration

    RegisterReferenceResponse:
      type: object
      required: [resourceType, resourceId, newRefCount, alreadyRegistered]
      properties:
        resourceType:
          type: string
          description: Type of resource referenced
        resourceId:
          type: string
          format: uuid
          description: ID of the resource referenced
        newRefCount:
          type: integer
          description: Reference count after registration
        alreadyRegistered:
          type: boolean
          description: True if this exact reference was already registered

    UnregisterReferenceRequest:
      type: object
      required: [resourceType, resourceId, sourceType, sourceId]
      properties:
        resourceType:
          type: string
          description: Type of resource being dereferenced (opaque identifier)
        resourceId:
          type: string
          format: uuid
          description: ID of the resource being dereferenced
        sourceType:
          type: string
          description: Type of entity releasing the reference (opaque identifier)
        sourceId:
          type: string
          format: uuid
          description: ID of the entity releasing the reference

    UnregisterReferenceResponse:
      type: object
      required: [resourceType, resourceId, newRefCount, wasRegistered]
      properties:
        resourceType:
          type: string
          description: Type of resource dereferenced
        resourceId:
          type: string
          format: uuid
          description: ID of the resource dereferenced
        newRefCount:
          type: integer
          description: Reference count after unregistration
        wasRegistered:
          type: boolean
          description: True if this reference existed before unregistration
        gracePeriodStartedAt:
          type: string
          format: date-time
          nullable: true
          description: When grace period started (null if refCount > 0)

    CheckReferencesRequest:
      type: object
      required: [resourceType, resourceId]
      properties:
        resourceType:
          type: string
          description: Type of resource to check
        resourceId:
          type: string
          format: uuid
          description: ID of the resource to check

    CheckReferencesResponse:
      type: object
      required: [resourceType, resourceId, refCount, isCleanupEligible]
      properties:
        resourceType:
          type: string
          description: Type of resource checked
        resourceId:
          type: string
          format: uuid
          description: ID of the resource checked
        refCount:
          type: integer
          description: Current reference count
        sources:
          type: array
          items:
            $ref: '#/components/schemas/ResourceReference'
          description: List of entities referencing this resource
        isCleanupEligible:
          type: boolean
          description: True if refCount=0 and grace period has passed
        gracePeriodEndsAt:
          type: string
          format: date-time
          nullable: true
          description: When grace period ends (null if refCount > 0 or already passed)
        lastZeroTimestamp:
          type: string
          format: date-time
          nullable: true
          description: When refCount last became zero

    ResourceReference:
      type: object
      required: [sourceType, sourceId, registeredAt]
      properties:
        sourceType:
          type: string
          description: Type of entity holding the reference (opaque identifier)
        sourceId:
          type: string
          format: uuid
          description: ID of the entity holding the reference
        registeredAt:
          type: string
          format: date-time
          description: When this reference was registered

    ListReferencesRequest:
      type: object
      required: [resourceType, resourceId]
      properties:
        resourceType:
          type: string
          description: Type of resource to list references for (opaque identifier)
        resourceId:
          type: string
          format: uuid
          description: ID of the resource to list references for
        filterSourceType:
          type: string
          nullable: true
          description: Optional filter by source type (opaque identifier)
        limit:
          type: integer
          default: 100
          description: Maximum references to return

    ListReferencesResponse:
      type: object
      required: [resourceType, resourceId, references, totalCount]
      properties:
        resourceType:
          type: string
          description: Type of resource listed
        resourceId:
          type: string
          format: uuid
          description: ID of the resource listed
        references:
          type: array
          items:
            $ref: '#/components/schemas/ResourceReference'
          description: List of references
        totalCount:
          type: integer
          description: Total reference count (may exceed returned list if limit applied)

    DefineCleanupRequest:
      type: object
      required: [resourceType, sourceType, callbackEndpoint, payloadTemplate]
      properties:
        resourceType:
          type: string
          description: Type of resource this cleanup handles (opaque identifier)
        sourceType:
          type: string
          description: Type of entity that will be cleaned up (opaque identifier)
        serviceName:
          type: string
          description: Target service name for callback
        callbackEndpoint:
          type: string
          description: Endpoint path (e.g., /actor/cleanup-by-character)
        payloadTemplate:
          type: string
          description: JSON template with {{resourceId}} placeholder
        description:
          type: string
          nullable: true
          description: Human-readable description of cleanup action

    DefineCleanupResponse:
      type: object
      required: [resourceType, sourceType, registered]
      properties:
        resourceType:
          type: string
          description: Resource type for callback (opaque identifier)
        sourceType:
          type: string
          description: Source type for callback (opaque identifier)
        registered:
          type: boolean
          description: True if callback was registered (or updated)
        previouslyDefined:
          type: boolean
          description: True if this callback was already defined (updated)

    ExecuteCleanupRequest:
      type: object
      required: [resourceType, resourceId]
      properties:
        resourceType:
          type: string
          description: Type of resource to clean up
        resourceId:
          type: string
          format: uuid
          description: ID of the resource to clean up
        gracePeriodSeconds:
          type: integer
          nullable: true
          description: Override grace period (uses default if not specified)
        cleanupPolicy:
          $ref: '#/components/schemas/CleanupPolicy'
          nullable: true
          description: Override cleanup policy (uses resource default if not specified)

    ExecuteCleanupResponse:
      type: object
      required: [resourceType, resourceId, success, callbackResults]
      properties:
        resourceType:
          type: string
          description: Type of resource cleaned up
        resourceId:
          type: string
          format: uuid
          description: ID of the resource cleaned up
        success:
          type: boolean
          description: True if cleanup completed (per cleanup policy)
        abortReason:
          type: string
          nullable: true
          description: Why cleanup was aborted (refcount changed, callback failed with ALL_REQUIRED, etc.)
        callbackResults:
          type: array
          items:
            $ref: '#/components/schemas/CleanupCallbackResult'
          description: Results of each cleanup callback
        cleanupDurationMs:
          type: integer
          description: Total cleanup execution time in milliseconds

    CleanupCallbackResult:
      type: object
      required: [sourceType, serviceName, endpoint, success]
      properties:
        sourceType:
          type: string
          description: Source type that was cleaned up (opaque identifier)
        serviceName:
          type: string
          description: Service that was called
        endpoint:
          type: string
          description: Endpoint that was called
        success:
          type: boolean
          description: Whether callback succeeded
        statusCode:
          type: integer
          nullable: true
          description: HTTP status code from callback
        errorMessage:
          type: string
          nullable: true
          description: Error message if callback failed
        durationMs:
          type: integer
          description: Callback execution time in milliseconds
```

### Configuration Schema (TENET T21 - Configuration-First)

```yaml
# resource-configuration.yaml
x-service-configuration:
  properties:
    DefaultGracePeriodSeconds:
      type: integer
      env: RESOURCE_DEFAULT_GRACE_PERIOD_SECONDS
      default: 604800
      minimum: 0
      description: Default grace period in seconds before cleanup eligible (7 days default)
    CleanupCallbackTimeoutSeconds:
      type: integer
      env: RESOURCE_CLEANUP_CALLBACK_TIMEOUT_SECONDS
      default: 30
      minimum: 5
      maximum: 300
      description: Timeout for each cleanup callback execution
    MaxCallbackRetries:
      type: integer
      env: RESOURCE_MAX_CALLBACK_RETRIES
      default: 3
      minimum: 0
      maximum: 10
      description: Max retries per cleanup callback on transient failure
    CleanupLockExpirySeconds:
      type: integer
      env: RESOURCE_CLEANUP_LOCK_EXPIRY_SECONDS
      default: 300
      minimum: 60
      maximum: 3600
      description: Distributed lock timeout during cleanup execution
    DefaultCleanupPolicy:
      $ref: 'resource-api.yaml#/components/schemas/CleanupPolicy'
      env: RESOURCE_DEFAULT_CLEANUP_POLICY
      default: BEST_EFFORT
      description: Default cleanup policy when not specified per-resource-type (CleanupPolicy enum is lib-resource's own closed set)
```

### Events Schema (TENET T5 - Typed Events)

```yaml
# resource-events.yaml
info:
  title: Resource Events
  version: 1.0.0
  x-event-subscriptions:
    - topic: resource.reference.registered
      event: ResourceReferenceRegisteredEvent
      handler: HandleReferenceRegistered
    - topic: resource.reference.unregistered
      event: ResourceReferenceUnregisteredEvent
      handler: HandleReferenceUnregistered

components:
  schemas:
    ResourceReferenceRegisteredEvent:
      type: object
      required: [resourceType, resourceId, sourceType, sourceId, timestamp]
      properties:
        resourceType:
          type: string
          description: Type of resource being referenced (opaque identifier, e.g., "character")
        resourceId:
          type: string
          format: uuid
          description: ID of the resource being referenced
        sourceType:
          type: string
          description: Type of entity holding the reference (opaque identifier, e.g., "actor")
        sourceId:
          type: string
          format: uuid
          description: ID of the entity holding the reference
        timestamp:
          type: string
          format: date-time
          description: When the reference was registered

    ResourceReferenceUnregisteredEvent:
      type: object
      required: [resourceType, resourceId, sourceType, sourceId, timestamp]
      properties:
        resourceType:
          type: string
          description: Type of resource being dereferenced (opaque identifier)
        resourceId:
          type: string
          format: uuid
          description: ID of the resource being dereferenced
        sourceType:
          type: string
          description: Type of entity releasing the reference (opaque identifier)
        sourceId:
          type: string
          format: uuid
          description: ID of the entity releasing the reference
        timestamp:
          type: string
          format: date-time
          description: When the reference was unregistered

    ResourceCleanupCallbackFailedEvent:
      type: object
      required: [resourceType, resourceId, sourceType, serviceName, endpoint, statusCode, timestamp]
      properties:
        resourceType:
          type: string
          description: Type of resource being cleaned up (opaque identifier)
        resourceId:
          type: string
          format: uuid
          description: ID of the resource being cleaned up
        sourceType:
          type: string
          description: Type of entity whose cleanup failed (opaque identifier)
        serviceName:
          type: string
          description: Service whose callback failed
        endpoint:
          type: string
          description: Endpoint that failed
        statusCode:
          type: integer
          description: HTTP status code returned
        errorMessage:
          type: string
          nullable: true
          description: Error details
        timestamp:
          type: string
          format: date-time
          description: When the failure occurred
```

### State Stores

```yaml
# schemas/state-stores.yaml additions
resource-refcounts:
  backend: redis
  prefix: "resource:ref"
  service: Resource
  purpose: Reference counts and source tracking per resource

resource-cleanup:
  backend: redis
  prefix: "resource:cleanup"
  service: Resource
  purpose: Cleanup callback definitions per resource type
```

### Integration Flow

```
1. Actor (L4) creates actor with characterId
   ↓ (generated code)
2. Publish "resource.reference.registered" (typed ResourceReferenceRegisteredEvent)
   ↓
3. lib-resource event handler adds to set via ICacheableStateStore.AddToSetAsync

--- Later ---

4. Character (L2) wants to delete character
   ↓
5. Call /resource/check → returns refcount (derived from set size) + sources + isCleanupEligible
   ↓
6. If refCount > 0: reject with blockers list (409 Conflict)
   ↓
7. If isCleanupEligible = false (grace period not passed): reject with gracePeriodEndsAt
   ↓
8. Call /resource/cleanup/execute:
   a. Acquire distributed lock on resource:{type}:{id}
   b. Re-validate refcount=0 under lock (via SetCountAsync)
   c. If refcount changed → return 409 Conflict
   d. Execute cleanup callbacks via IServiceNavigator.ExecutePreboundApiBatchAsync
   e. Per cleanup policy: abort or continue on failures
   f. Publish character.deleted event
   g. Release lock
```

### Cleanup Execution (TENET T7 - Error Handling)

Uses existing `IServiceNavigator.ExecutePreboundApiAsync` with **parallel execution** and proper error handling:

```csharp
public async Task<(StatusCodes, ExecuteCleanupResponse?)> ExecuteCleanupAsync(
    ExecuteCleanupRequest body,
    CancellationToken ct = default)
{
    var resourceKey = $"{body.ResourceType}:{body.ResourceId}";

    // Acquire distributed lock (TENET T9 - Multi-Instance Safety)
    await using var lockResponse = await _lockProvider.LockAsync(
        resourceId: $"resource-cleanup:{resourceKey}",
        lockOwner: Guid.NewGuid().ToString(),
        expiryInSeconds: _configuration.CleanupLockExpirySeconds,
        cancellationToken: ct);

    if (!lockResponse.Success)
    {
        _logger.LogWarning("Failed to acquire cleanup lock for {ResourceKey}", resourceKey);
        return (StatusCodes.Conflict, null);
    }

    // Re-validate under lock
    var checkResult = await CheckReferencesInternalAsync(body.ResourceType, body.ResourceId, ct);
    if (checkResult.RefCount > 0)
    {
        _logger.LogInformation("Cleanup aborted: refcount changed to {RefCount} for {ResourceKey}",
            checkResult.RefCount, resourceKey);
        return (StatusCodes.Conflict, new ExecuteCleanupResponse
        {
            ResourceType = body.ResourceType,
            ResourceId = body.ResourceId,
            Success = false,
            AbortReason = $"Reference count changed to {checkResult.RefCount} during cleanup"
        });
    }

    // Execute callbacks
    var callbacks = await GetCleanupCallbacksAsync(body.ResourceType, ct);
    var context = new Dictionary<string, object?> { ["resourceId"] = body.ResourceId };
    var stopwatch = Stopwatch.StartNew();

    var results = await _navigator.ExecutePreboundApiBatchAsync(
        callbacks.Select(c => new PreboundApiDefinition
        {
            ServiceName = c.ServiceName,
            Endpoint = c.Endpoint,
            PayloadTemplate = c.PayloadTemplate,
            Description = c.Description
        }),
        context,
        BatchExecutionMode.Parallel,
        ct);

    stopwatch.Stop();

    var callbackResults = results.Select((r, i) => new CleanupCallbackResult
    {
        SourceType = callbacks[i].SourceType,  // String identifier from callback definition
        ServiceName = r.Api.ServiceName,
        Endpoint = r.Api.Endpoint,
        Success = r.IsSuccess,
        StatusCode = r.Result.StatusCode,
        ErrorMessage = r.IsSuccess ? null : r.Result.ErrorMessage ?? r.SubstitutionError,
        DurationMs = (int)r.Result.Duration.TotalMilliseconds
    }).ToList();

    // Handle failures per cleanup policy (TENET T7 - Error Handling)
    var failedCallbacks = callbackResults.Where(r => !r.Success).ToList();
    var cleanupPolicy = body.CleanupPolicy ?? _configuration.DefaultCleanupPolicy;

    if (failedCallbacks.Count > 0)
    {
        foreach (var failure in failedCallbacks)
        {
            _logger.LogWarning("Cleanup callback failed: {Service}/{Endpoint} returned {StatusCode}",
                failure.ServiceName, failure.Endpoint, failure.StatusCode);

            // Publish failure event for monitoring (TENET T5 - typed event)
            // Note: SourceType is a string, not enum - lib-resource doesn't enumerate higher-layer services
            await _messageBus.PublishAsync("resource.cleanup.callback-failed",
                new ResourceCleanupCallbackFailedEvent
                {
                    ResourceType = body.ResourceType,
                    ResourceId = body.ResourceId,
                    SourceType = failure.SourceType,  // Opaque string identifier
                    ServiceName = failure.ServiceName,
                    Endpoint = failure.Endpoint,
                    StatusCode = failure.StatusCode ?? 0,
                    ErrorMessage = failure.ErrorMessage,
                    Timestamp = DateTimeOffset.UtcNow
                }, cancellationToken: ct);
        }

        if (cleanupPolicy == CleanupPolicy.AllRequired)
        {
            return (StatusCodes.OK, new ExecuteCleanupResponse
            {
                ResourceType = body.ResourceType,
                ResourceId = body.ResourceId,
                Success = false,
                AbortReason = $"{failedCallbacks.Count} cleanup callback(s) failed with ALL_REQUIRED policy",
                CallbackResults = callbackResults,
                CleanupDurationMs = (int)stopwatch.ElapsedMilliseconds
            });
        }
    }

    return (StatusCodes.OK, new ExecuteCleanupResponse
    {
        ResourceType = body.ResourceType,
        ResourceId = body.ResourceId,
        Success = true,
        CallbackResults = callbackResults,
        CleanupDurationMs = (int)stopwatch.ElapsedMilliseconds
    });
}
```

### Reference Counting via ICacheableStateStore (TENET T4 - Interface-First)

**Design Decision**: Use `ICacheableStateStore` set operations instead of Lua scripts. The set membership IS the source of truth for references - no separate counter needed.

Per FOUNDATION TENETS, Lua scripts are a last resort. Here, `ICacheableStateStore.AddToSetAsync` and `RemoveFromSetAsync` provide atomic operations, and `SetCountAsync` derives the count. The small race window for `lastZeroTimestamp` is acceptable because **cleanup execution re-validates under distributed lock**.

```csharp
// In ResourceServiceEvents.cs - handles reference events using ICacheableStateStore
public async Task HandleReferenceRegisteredAsync(ResourceReferenceRegisteredEvent evt, CancellationToken ct)
{
    var sourcesSetKey = $"{evt.ResourceType}:{evt.ResourceId}:sources";
    var sourceEntry = new ResourceReferenceEntry
    {
        SourceType = evt.SourceType,
        SourceId = evt.SourceId,
        RegisteredAt = evt.Timestamp
    };

    // AddToSetAsync is atomic - returns true if item was newly added
    var added = await _cacheStore.AddToSetAsync(sourcesSetKey, sourceEntry, ct);

    if (added)
    {
        // Clear lastZeroTimestamp since we now have references
        await _stateStore.DeleteAsync($"{evt.ResourceType}:{evt.ResourceId}:lastZero", ct);

        _logger.LogDebug("Registered reference: {SourceType}:{SourceId} → {ResourceType}:{ResourceId}",
            evt.SourceType, evt.SourceId, evt.ResourceType, evt.ResourceId);
    }
    else
    {
        _logger.LogDebug("Reference already registered: {SourceType}:{SourceId} → {ResourceType}:{ResourceId}",
            evt.SourceType, evt.SourceId, evt.ResourceType, evt.ResourceId);
    }
}

public async Task HandleReferenceUnregisteredAsync(ResourceReferenceUnregisteredEvent evt, CancellationToken ct)
{
    var sourcesSetKey = $"{evt.ResourceType}:{evt.ResourceId}:sources";
    var sourceEntry = new ResourceReferenceEntry
    {
        SourceType = evt.SourceType,
        SourceId = evt.SourceId,
        RegisteredAt = default  // Not used for removal matching
    };

    // RemoveFromSetAsync is atomic - returns true if item was removed
    var removed = await _cacheStore.RemoveFromSetAsync(sourcesSetKey, sourceEntry, ct);

    if (removed)
    {
        // Check if set is now empty - if so, record lastZeroTimestamp
        var count = await _cacheStore.SetCountAsync(sourcesSetKey, ct);
        if (count == 0)
        {
            await _stateStore.SaveAsync(
                $"{evt.ResourceType}:{evt.ResourceId}:lastZero",
                new LastZeroRecord { Timestamp = DateTimeOffset.UtcNow },
                cancellationToken: ct);
        }

        _logger.LogDebug("Unregistered reference: {SourceType}:{SourceId} → {ResourceType}:{ResourceId}",
            evt.SourceType, evt.SourceId, evt.ResourceType, evt.ResourceId);
    }
}

// Reference count is derived from set cardinality - no separate counter
private async Task<long> GetRefCountAsync(string resourceType, Guid resourceId, CancellationToken ct)
{
    var sourcesSetKey = $"{resourceType}:{resourceId}:sources";
    return await _cacheStore.SetCountAsync(sourcesSetKey, ct);
}
```

**Why Not Lua Scripts?**:
- `AddToSetAsync`/`RemoveFromSetAsync` are individually atomic (Redis SADD/SREM)
- Count is derived from `SetCountAsync` (Redis SCARD) - no separate counter to keep in sync
- The race between "remove + check count + set lastZero" is acceptable because:
  - A stale `lastZeroTimestamp` just means waiting for another grace period
  - Cleanup execution **always re-validates under distributed lock** before proceeding
- Per FOUNDATION TENETS: "Lua scripts should only be used when atomicity across multiple distinct operations is genuinely required and the above alternatives are insufficient"

### Files to Create

| File | Purpose |
|------|---------|
| `schemas/resource-api.yaml` | API schema with 6 endpoints and all models |
| `schemas/resource-events.yaml` | Event schemas with x-event-subscriptions |
| `schemas/resource-configuration.yaml` | Service configuration |
| `plugins/lib-resource/ResourceService.cs` | Business logic implementation |
| `plugins/lib-resource/ResourceServiceEvents.cs` | Event handlers for reference tracking |
| `plugins/lib-resource.tests/ResourceServiceTests.cs` | Unit tests (TENET T11) |
| `docs/plugins/RESOURCE.md` | Deep dive documentation |
| `scripts/generate-references.py` | Schema parser for x-references extension |

### Files to Modify

| File | Change |
|------|--------|
| `schemas/state-stores.yaml` | Add resource-refcounts, resource-cleanup stores |
| `scripts/generate-all-services.sh` | Add reference generation step after event generation |
| `docs/reference/SCHEMA-RULES.md` | Document x-references and x-resource-lifecycle extensions |
| `docs/reference/SERVICE_HIERARCHY.md` | Add lib-resource to L1 list |
| `docs/reference/SERVICE_HIERARCHY_VIOLATIONS.md` | Update Character violations as fixed |
| `plugins/lib-character/CharacterService.cs` | Remove L4 client injections, call lib-resource |

---

## Migration Path

### Phase 1: Infrastructure (lib-resource core)
- Create all schema files (api, events, configuration)
- Run `make generate` to produce generated code
- Implement ResourceService.cs with core CRUD
- Implement ResourceServiceEvents.cs with ICacheableStateStore set operations
- Add state store definitions
- Write unit tests for ref counting logic
- **Verification**: `dotnet build` passes, unit tests pass

### Phase 2: Schema Extension (x-references)
- Define `x-references` and `x-resource-lifecycle` schema specification
- Create `scripts/generate-references.py` parser
- Integrate into `scripts/generate-all-services.sh` generation pipeline
- Document extensions in SCHEMA-RULES.md
- **Verification**: Generation produces valid code

### Phase 3: First Consumer (Actor service)
- Add `x-references` to actor-api.yaml for character references
- Generate reference tracking code
- Add cleanup endpoint to Actor service (`/actor/cleanup-by-character`)
- Validate end-to-end flow: create actor → verify refcount → delete actor → verify refcount
- **Verification**: Integration test passes

### Phase 4: Migration (All violating services)
- Add `x-references` to: character-encounter, character-personality, character-history, contract, relationship, scene, save-load
- Add cleanup endpoints to each service
- Remove hardcoded client injections from CharacterService:
  - Remove IActorClient
  - Remove ICharacterEncounterClient
  - Remove ICharacterPersonalityClient
  - Remove ICharacterHistoryClient
- Replace CheckCharacterReferencesAsync implementation with IResourceClient call
- Run full test suite
- **Verification**: `dotnet build` passes, no L2→L4 client injections remain

### Phase 5: Documentation & Cleanup
- Update SERVICE_HIERARCHY_VIOLATIONS.md (mark #1, #2 as fixed)
- Update docs/plugins/CHARACTER.md (remove violation notes)
- Add docs/plugins/RESOURCE.md deep dive
- Update GENERATED-SERVICE-DETAILS.md (regenerate)
- **Verification**: `make generate-docs` produces clean output

---

## Verification Checklist

### Build Verification
- [ ] `dotnet build` passes with no errors
- [ ] No CS1591 warnings (all properties have descriptions)
- [ ] No nullability warnings in service code

### TENET Compliance
- [ ] **T1**: All schemas follow SCHEMA-RULES.md patterns
- [ ] **T2**: lib-resource at L1, no upward dependencies
- [ ] **T4**: Uses lib-state, lib-messaging, lib-mesh only; prefers ICacheableStateStore over Lua scripts
- [ ] **T5**: All events are typed (no anonymous objects)
- [ ] **T6**: ResourceService is partial class with proper structure
- [ ] **T7**: Error handling with ApiException distinction
- [ ] **T8**: All methods return (StatusCodes, TResponse?) tuples
- [ ] **T9**: Set operations + distributed lock for multi-instance safety
- [ ] **T10**: Structured logging with message templates
- [ ] **T11**: Unit tests for reference counting logic
- [ ] **T13**: All endpoints have x-permissions
- [ ] **T21**: All tunables in configuration schema
- [ ] **SCHEMA-RULES**: resourceType/sourceType are strings (not enums) to avoid L1→L2+ coupling

### Hierarchy Compliance
- [ ] CharacterService no longer injects IActorClient
- [ ] CharacterService no longer injects ICharacterEncounterClient
- [ ] CharacterService no longer injects ICharacterPersonalityClient
- [ ] CharacterService no longer injects ICharacterHistoryClient
- [ ] No L2 services have L3/L4 client dependencies

### Functional Verification
- [ ] Reference registration increments count atomically
- [ ] Reference unregistration decrements count atomically
- [ ] Grace period tracking works (lastZeroTimestamp set correctly)
- [ ] Cleanup execution acquires lock and validates under lock
- [ ] Parallel callback execution with proper failure handling
- [ ] Cleanup policy (BEST_EFFORT vs ALL_REQUIRED) respected

---

## Key Decisions Made

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Architecture | Dedicated lib-resource at L1 | Enables L2+ services to use it; semantic fit |
| Event Pattern | Typed events to `resource.reference.*` | TENET T5 compliance; generic across all resource types |
| Type Identifiers | Opaque strings (not enums) | L1 service must not enumerate L2+ services/entities; see SCHEMA-RULES "When NOT to Create Enums" |
| Grace Period | Configurable per-resource-type via x-resource-lifecycle | Flexibility; different resources have different cleanup urgency |
| Grace Period Tracking | lastZeroTimestamp in Redis | Simple; small race window acceptable since cleanup re-validates under lock |
| Cleanup Execution | Parallel with configurable policy | Speed for best_effort; safety option for all_required |
| Failure Handling | Event publication + policy-based abort | Monitoring via events; configurable strictness |
| Concurrency | Distributed lock + re-validate pattern | Prevents race between reference registration and cleanup |
| Reference Storage | ICacheableStateStore sets (not Lua scripts) | Per FOUNDATION TENETS, prefer interface methods; SetCountAsync derives refcount |

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| Reference leak (forgot to unregister) | Background audit job can scan for orphaned references (future enhancement) |
| Cleanup callback permanently failing | Event publishing enables monitoring/alerting; manual intervention possible |
| Lock contention during high cleanup | 5-minute lock expiry prevents deadlocks; retries on conflict |
| Schema extension complexity | Start with manual registration, graduate to x-references once proven |
| Migration data loss | Phase 4 runs full test suite; CharacterService keeps old code until tests pass |
