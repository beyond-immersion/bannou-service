# Behavior Implementation Map

> **Plugin**: lib-behavior
> **Schema**: schemas/behavior-api.yaml
> **Layer**: GameFeatures
> **Deep Dive**: [docs/plugins/BEHAVIOR.md](../plugins/BEHAVIOR.md)

---

## Summary

| Field | Value |
|-------|-------|
| Plugin | lib-behavior |
| Layer | L4 GameFeatures |
| Endpoints | 6 |
| State Stores | behavior-statestore (Redis), agent-memories (Redis) |
| Events Published | 8 (behavior.created, behavior.updated, behavior.deleted, behavior.bundle.created, behavior.bundle.updated, behavior.bundle.deleted, behavior.compilation-failed, behavior.goap-plan-generated) |
| Events Consumed | 0 |
| Client Events | 0 |
| Background Services | 0 |

---

## State

**Store**: `behavior-statestore` (Backend: Redis) — via `StateStoreDefinitions.Behavior`

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `behavior-metadata:{behaviorId}` | `BehaviorMetadata` | Compiled behavior definition metadata (name, category, bundleId, assetId, bytecodeSize, schemaVersion) |
| `bundle-membership:{bundleId}` | `BundleMembership` | Bundle-to-behavior association index (name, behaviorAssetIds dictionary, assetBundleId) |
| `goap-metadata:{behaviorId}` | `CachedGoapMetadata` | Cached GOAP goals and actions extracted during compilation |

**Store**: `agent-memories` (Backend: Redis) — via `StateStoreDefinitions.AgentMemories`

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `memory:{entityId}:{memoryId}` | `Memory` | Individual memory entries per actor |
| `memory-index:{entityId}` | `List<string>` | Memory ID index for per-entity retrieval and eviction |

Key prefixes are configurable via `BehaviorServiceConfiguration` properties (`BehaviorMetadataKeyPrefix`, `BundleMembershipKeyPrefix`, `GoapMetadataKeyPrefix`, `MemoryKeyPrefix`, `MemoryIndexKeyPrefix`). All five stores in `BehaviorBundleManager` resolve from `StateStoreDefinitions.Behavior`; the two stores in `ActorLocalMemoryStore` resolve from `StateStoreDefinitions.AgentMemories`.

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | Persistence for behavior metadata, bundle membership, GOAP metadata (via BehaviorBundleManager), and actor memories (via ActorLocalMemoryStore) |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing lifecycle events (behavior/bundle created/updated/deleted), compilation-failed, goap-plan-generated |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Span instrumentation on async helper methods |
| lib-asset (IAssetClient) | L3 | Soft | Upload/download/delete compiled bytecode assets; resolved via `_serviceProvider.GetService<IAssetClient>()` with graceful degradation |

**Notes:**
- No L1 or L2 service client dependencies. Behavior is unusually self-contained for an L4 plugin.
- No lib-resource integration (`x-references` not defined). Behavior artifacts are deleted directly via IAssetClient during InvalidateCachedBehavior.
- No account ownership — T28 account deletion cleanup obligation does not apply.
- `IHttpClientFactory` is used for HTTP PUT/GET to pre-signed asset URLs (not a service dependency).
- SDK dependencies (`BehaviorCompiler`, `IGoapPlanner`, `DocumentParser`) are in-process computation libraries, not service clients.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `behavior.created` | `BehaviorCreatedEvent` | CompileAbmlBehavior — new behavior compiled and stored |
| `behavior.updated` | `BehaviorUpdatedEvent` | CompileAbmlBehavior — existing behavior recompiled; changedFields: ["bytecode"] |
| `behavior.deleted` | `BehaviorDeletedEvent` | InvalidateCachedBehavior — behavior invalidated via API |
| `behavior.bundle.created` | `BehaviorBundleCreatedEvent` | CompileAbmlBehavior — first behavior added to a new bundle (via BehaviorBundleManager.AddToBundleAsync) |
| `behavior.bundle.updated` | `BehaviorBundleUpdatedEvent` | CompileAbmlBehavior — behavior added to existing bundle; InvalidateCachedBehavior — bundle membership changed after removal; BehaviorBundleManager.CreateAssetBundleAsync |
| `behavior.bundle.deleted` | `BehaviorBundleDeletedEvent` | InvalidateCachedBehavior — last behavior removed from bundle (via BehaviorBundleManager.RemoveBehaviorAsync) |
| `behavior.compilation-failed` | `BehaviorCompilationFailedEvent` | CompileAbmlBehavior — compiler returns errors |
| `behavior.goap-plan-generated` | `GoapPlanGeneratedEvent` | GenerateGoapPlan — successful plan generated |

**Note**: `behavior.cinematic-extension` (`CinematicExtensionAvailableEvent`) is defined in the schema but never published by current code. See deep dive Stub #2.

---

## Events Consumed

This plugin does not consume external events.

---

## DI Services

#### Constructor Dependencies

| Service | Role |
|---------|------|
| `ILogger<BehaviorService>` | Structured logging |
| `BehaviorServiceConfiguration` | All 34 config properties (urgency, attention, memory, compiler, key prefixes) |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Event consumer registration (no handlers registered — empty body) |
| `IGoapPlanner` | A* GOAP planning and plan validation (Singleton, stateless) |
| `BehaviorCompiler` | ABML-to-bytecode compilation pipeline (Singleton, stateless) |
| `IServiceProvider` | Soft L3 dependency resolution for IAssetClient |
| `IHttpClientFactory` | HTTP client for asset upload/download to pre-signed URLs |
| `IBehaviorBundleManager` | State persistence for behavior metadata, bundle membership, GOAP caches |
| `ITelemetryProvider` | Span instrumentation |

**Note**: Constructor calls `CognitionConstants.Initialize(configuration.ToCognitionConfiguration())` to forward configuration values (urgency thresholds, attention weights, memory settings) to the static `CognitionConstants` instance shared by cognition pipeline handlers.

#### DI Interfaces Implemented by This Plugin

None. Behavior does not implement any cross-layer DI provider or listener interfaces.

---

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| CompileAbmlBehavior | POST /compile | generated | developer | metadata, membership, goap-metadata | behavior.created or behavior.updated, behavior.bundle.created or behavior.bundle.updated, behavior.compilation-failed |
| ValidateAbml | POST /validate | generated | developer | - | - |
| GetCachedBehavior | POST /cache/get | generated | [] | - | - |
| InvalidateCachedBehavior | POST /cache/invalidate | generated | [] | metadata, membership, goap-metadata | behavior.deleted, behavior.bundle.updated or behavior.bundle.deleted |
| GenerateGoapPlan | POST /goap/plan | generated | [] | - | behavior.goap-plan-generated |
| ValidateGoapPlan | POST /goap/validate-plan | generated | [] | - | - |

---

## Methods

### CompileAbmlBehavior
POST /compile | Roles: [developer]

```
IF body.AbmlContent is empty/whitespace
  RETURN (400, null)

// Compile ABML via multi-phase compiler pipeline
compilationResult = _compiler.CompileYaml(abmlContent, compilationOptions)
  // compilationOptions mapped from body: EnableOptimizations, StrictValidation,
  // CulturalAdaptations, GoapIntegration, MaxConstants, MaxStrings (from config)

IF !compilationResult.Success
  PUBLISH behavior.compilation-failed { behaviorName, errorCount, errors }
  RETURN (400, null)

behaviorId = SHA256(bytecode)[0..16] prefixed "behavior-"

IF body.CompilationOptions?.CacheCompiledResult == false
  // Skip all persistence and event publishing
  RETURN (200, CompileBehaviorResponse { behaviorId, compiledBehavior, compilationTimeMs, isUpdate: false })

// Store compiled bytecode as asset via lib-asset (soft L3)
assetClient = _serviceProvider.GetService<IAssetClient>()
IF assetClient != null
  CALL IAssetClient.RequestUploadAsync(uploadRequest)
  HTTP-PUT bytecode to presigned uploadUrl
  CALL IAssetClient.CompleteUploadAsync(completeRequest) -> assetId
IF assetId == null AND caching enabled
  RETURN (500, null)

// Record behavior metadata via BehaviorBundleManager
READ behavior-statestore:behavior-metadata:{behaviorId}   // check exists
isUpdate = metadata != null
WRITE behavior-statestore:behavior-metadata:{behaviorId} <- BehaviorMetadata from compilation

IF body.BundleId != null
  READ behavior-statestore:bundle-membership:{bundleId}
  IF bundle is new
    WRITE behavior-statestore:bundle-membership:{bundleId} <- new BundleMembership
    PUBLISH behavior.bundle.created { bundleId, name, behaviorCount }
  ELSE
    WRITE behavior-statestore:bundle-membership:{bundleId} <- updated membership
    PUBLISH behavior.bundle.updated { bundleId, name, behaviorCount, changedFields: ["behaviorAssetIds"] }

// Cache GOAP metadata (non-fatal — exceptions swallowed with LogWarning)
IF compilationResult has GOAP goals/actions
  WRITE behavior-statestore:goap-metadata:{behaviorId} <- CachedGoapMetadata

IF isUpdate
  PUBLISH behavior.updated { behaviorId, name, category, bundleId, assetId, bytecodeSize, changedFields: ["bytecode"] }
ELSE
  PUBLISH behavior.created { behaviorId, name, category, bundleId, assetId, bytecodeSize }

RETURN (200, CompileBehaviorResponse { behaviorId, behaviorName, compiledBehavior, compilationTimeMs, assetId, isUpdate })
```

---

### ValidateAbml
POST /validate | Roles: [developer]

```
IF body.AbmlContent is empty/whitespace
  RETURN (400, null)

compilationResult = _compiler.CompileYaml(abmlContent, options { SkipSemanticAnalysis: !body.StrictMode })
// Full compilation pipeline runs (including bytecode emission) — bytecode is discarded

RETURN (200, ValidateAbmlResponse {
  isValid: compilationResult.Success,
  validationErrors: mapped from compilationResult.Errors,
  semanticWarnings: compilationResult.Warnings,
  schemaVersion: "1.0"
})
```

---

### GetCachedBehavior
POST /cache/get | Roles: []

```
IF body.BehaviorId is empty/whitespace
  RETURN (400, null)

assetClient = _serviceProvider.GetService<IAssetClient>()
IF assetClient == null
  RETURN (503, null)                                         // Asset service not enabled

CALL IAssetClient.GetAssetAsync({ AssetId: body.BehaviorId, Version: "latest" })
  // catch ApiException(404) -> RETURN (404, null)

IF asset.DownloadUrl == null
  RETURN (500, null)                                         // Unexpected: asset exists but no download URL

// Download bytecode from pre-signed URL
HTTP-GET asset.DownloadUrl -> bytecodeBytes
IF download succeeds
  RETURN (200, CachedBehaviorResponse { behaviorId, compiledBehavior { bytecode: base64, bytecodeSize } })
ELSE
  // Graceful fallback: return download URL without bytecode
  RETURN (200, CachedBehaviorResponse { behaviorId, compiledBehavior { downloadUrl, bytecodeSize: 0 } })
```

---

### InvalidateCachedBehavior
POST /cache/invalidate | Roles: []

```
IF body.BehaviorId is empty/whitespace
  RETURN (400, null)

// Check metadata via BehaviorBundleManager
READ behavior-statestore:behavior-metadata:{behaviorId} -> metadata

IF metadata == null
  // Fallback: check asset storage directly
  assetClient = _serviceProvider.GetService<IAssetClient>()
  IF assetClient == null OR GetAssetAsync returns 404
    RETURN (404, null)

IF metadata != null
  // Remove from bundle manager (handles bundle membership updates)
  // see helper: BehaviorBundleManager.RemoveBehaviorAsync
  DELETE behavior-statestore:behavior-metadata:{behaviorId}
  IF metadata.BundleId != null
    READ behavior-statestore:bundle-membership:{bundleId}
    IF bundle has only this behavior
      DELETE behavior-statestore:bundle-membership:{bundleId}
      PUBLISH behavior.bundle.deleted { bundleId, deletedReason: "Last behavior removed from bundle" }
    ELSE
      WRITE behavior-statestore:bundle-membership:{bundleId} <- updated (behavior removed)
      PUBLISH behavior.bundle.updated { bundleId, changedFields: ["behaviorAssetIds"] }

  // Remove GOAP metadata cache
  DELETE behavior-statestore:goap-metadata:{behaviorId}

  PUBLISH behavior.deleted { behaviorId, name, category, bundleId, assetId, deletedReason: "Invalidated via API" }

// Delete from asset storage (if available)
assetClient = _serviceProvider.GetService<IAssetClient>()
IF assetClient != null
  CALL IAssetClient.DeleteAssetAsync({ AssetId: body.BehaviorId })
  // catch 404 -> ignore (already deleted)

RETURN (200, null)
```

---

### GenerateGoapPlan
POST /goap/plan | Roles: []

```
IF body.BehaviorId is empty
  RETURN (400, null)

// Retrieve cached GOAP metadata from compilation
READ behavior-statestore:goap-metadata:{body.BehaviorId} -> goapMetadata
IF goapMetadata == null
  RETURN (404, null)                                         // Behavior never compiled or has no GOAP content

// Convert world state from JSON to typed WorldState
worldState = ConvertToWorldState(body.WorldState)
  // Only bool, numeric, string values mapped; complex types silently ignored

IF goapMetadata.Actions count == 0
  RETURN (200, GoapPlanResponse { plan: null, failureReason: "No actions available" })

// Build planning options (from request or defaults)
options = body.Options ?? PlanningOptions.Default

plan = _goapPlanner.PlanAsync(worldState, goal, actions, options)

IF plan == null
  RETURN (200, GoapPlanResponse { plan: null, failureReason, planningTimeMs: 0, nodesExpanded: 0 })

PUBLISH behavior.goap-plan-generated { actorId: body.AgentId, goalId: goal.Name, planStepCount, planningTimeMs }

RETURN (200, GoapPlanResponse { plan, planningTimeMs, nodesExpanded })
```

---

### ValidateGoapPlan
POST /goap/validate-plan | Roles: []

```
// Convert API plan to SDK plan (GoapAction with empty preconditions/effects)
goapPlan = ConvertToGoapPlan(body.Plan)

// Convert world state
worldState = ConvertToWorldState(body.WorldState)

activeGoals = body.ActiveGoals ?? empty list

validationResult = _goapPlanner.ValidatePlanAsync(goapPlan, body.CurrentActionIndex, worldState, activeGoals)

// Map SDK enums to API enums via int cast (A2 SDK boundary)
RETURN (200, ValidateGoapPlanResponse {
  isValid: validationResult.IsValid,
  reason: (ReplanReason)validationResult.Reason,
  suggestedAction: (ValidationSuggestion)validationResult.SuggestedAction,
  invalidatedAtIndex: validationResult.InvalidatedAtIndex,
  message: validationResult.Message
})
```

---

## Background Services

No background services.

---

## Non-Standard Implementation Patterns

No non-standard patterns. All 6 endpoints are generated interface methods. No manual controllers, no manually-registered routes, no custom IBannouService overrides, and no non-trivial plugin lifecycle behavior.
