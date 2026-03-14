# Actor Implementation Map

> **Plugin**: lib-actor
> **Schema**: schemas/actor-api.yaml
> **Layer**: GameFoundation
> **Deep Dive**: [docs/plugins/ACTOR.md](../plugins/ACTOR.md)

---

## Summary

| Field | Value |
|-------|-------|
| Plugin | lib-actor |
| Layer | L2 GameFoundation |
| Endpoints | 17 |
| State Stores | actor-templates (Redis), actor-state (Redis), actor-pool-nodes (Redis), actor-assignments (Redis) |
| Events Published | 18 (actor.template.created, actor.template.updated, actor.template.deleted, actor.instance.created, actor.instance.started, actor.instance.deleted, actor.instance.character-bound, actor.instance.status-changed, actor.instance.completed, actor.instance.state-persisted, actor.encounter.started, actor.encounter.phase-changed, actor.encounter.ended, character.state-update, actor.pool-node.registered, actor.pool-node.heartbeat, actor.pool-node.draining, actor.pool-node.unhealthy) |
| Events Consumed | 7 |
| Client Events | 0 |
| Background Services | 3 (PoolHealthMonitor, ActorPoolNodeWorker, HeartbeatEmitter) |

---

## State

**Store**: `actor-templates` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{templateId}` | `ActorTemplateData` | Template definition by GUID |
| `category:{categoryName}` | `ActorTemplateData` | Category-based template lookup |
| `_all_template_ids` | `List<string>` | Global template index (ETag-protected) |

**Store**: `actor-state` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{actorId}` | `ActorStateSnapshot` | Runtime state (feelings, goals, memories, encounter) — written by ActorRunner periodic auto-save |

**Store**: `actor-pool-nodes` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{nodeId}` | `PoolNodeState` | Pool node health and capacity |
| `_node_index` | `PoolNodeIndex` | All registered pool node IDs |

**Store**: `actor-assignments` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{actorId}` | `ActorAssignment` | Actor-to-node mapping |
| `_actor_index` | `ActorIndex` | Node-to-actor-set mapping for pool queries |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | Persistence for templates, actor state, pool nodes, assignments |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing lifecycle events, pool commands, character state updates |
| lib-messaging (IEventConsumer) | L0 | Hard | Registering handlers for pool events, template updates, session disconnects |
| lib-mesh (IMeshInvocationClient) | L0 | Hard | Forwarding encounter/query requests to remote pool nodes |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Span instrumentation on async helpers |
| lib-resource (IResourceClient) | L1 | Hard | Character reference tracking via x-references pattern |
| lib-character (ICharacterClient) | L2 | Hard | Realm lookup on actor spawn (same-layer dependency) |

**DI Provider/Listener interfaces:**

| Interface | Role | Direction |
|-----------|------|-----------|
| `IEnumerable<IVariableProviderFactory>` | Consumes | L4 → L2 pull (personality, combat, encounters, backstory, obligations, faction, quest, seed, location, transit, world) |
| `IEnumerable<IBehaviorDocumentProvider>` | Consumes + Implements | L4 → L2 pull (lib-puppetmaster DynamicBehaviorProvider at priority 100); lib-actor provides SeededBehaviorProvider (50) and FallbackBehaviorProvider (0) |
| `ISeededResourceProvider` | Implements | L2 → L1 (exposes behavior manifest to lib-resource via BehaviorSeededResourceProvider) |

**Notes:**
- Actor uses ETag-based optimistic concurrency for template index updates (not `IDistributedLockProvider`)
- State store references are constructor-cached as readonly fields (`_templateStore`, `_templateIndexStore`)
- `IEnumerable<IVariableProviderFactory>` resolves to empty collection if no L4 plugins are enabled (graceful degradation without null checks)
- `character.state-update` is published via IMessageBus, not IClientEventPublisher

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `actor.template.created` | `ActorTemplateCreatedEvent` | CreateActorTemplate on success |
| `actor.template.updated` | `ActorTemplateUpdatedEvent` | UpdateActorTemplate on success (includes changedFields) |
| `actor.template.deleted` | `ActorTemplateDeletedEvent` | DeleteActorTemplate on success |
| `actor.instance.created` | `ActorInstanceCreatedEvent` | SpawnActor on success (both modes) |
| `actor.instance.started` | `ActorInstanceStartedEvent` | ActorRunner.StartAsync after behavior loop begins |
| `actor.instance.deleted` | `ActorInstanceDeletedEvent` | StopActor in bannou mode |
| `actor.instance.character-bound` | `ActorCharacterBoundEvent` | ActorRunner.StartAsync (if spawned with characterId) and ActorRunner.BindCharacterAsync |
| `actor.instance.status-changed` | `ActorStatusChangedEvent` | ActorPoolNodeWorker on pool node status transitions |
| `actor.instance.completed` | `ActorCompletedEvent` | ActorPoolNodeWorker when actor finishes on pool node |
| `actor.instance.state-persisted` | `ActorStatePersistedEvent` | ActorRunner periodic auto-save |
| `actor.encounter.started` | `ActorEncounterStartedEvent` | ActorRunner.StartEncounter (fire-and-forget from sync method) |
| `actor.encounter.phase-changed` | `ActorEncounterPhaseChangedEvent` | ActorRunner.SetEncounterPhase (fire-and-forget from sync method) |
| `actor.encounter.ended` | `ActorEncounterEndedEvent` | ActorRunner.EndEncounter (fire-and-forget from sync method) |
| `character.state-update` | `CharacterStateUpdateEvent` | ActorRunner per-tick behavioral output (feelings, goals) when CharacterId is set |
| `actor.pool-node.registered` | `PoolNodeRegisteredEvent` | ActorPoolNodeWorker on startup |
| `actor.pool-node.heartbeat` | `PoolNodeHeartbeatEvent` | HeartbeatEmitter periodic worker |
| `actor.pool-node.draining` | `PoolNodeDrainingEvent` | ActorPoolNodeWorker graceful shutdown |
| `actor.pool-node.unhealthy` | `PoolNodeUnhealthyEvent` | PoolHealthMonitor heartbeat timeout detection |

**Internal pool commands** (node-addressed, not broadcast events):

| Topic Pattern | Command Type | Trigger |
|---------------|-------------|---------|
| `actor.node.{appId}.spawn` | `SpawnActorCommand` | SpawnActor pool mode |
| `actor.node.{appId}.stop` | `StopActorCommand` | StopActor pool mode, CleanupByCharacter pool mode |
| `actor.node.{appId}.bind-character` | `BindActorCharacterCommand` | BindActorCharacter pool mode |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `session.disconnected` | `HandleSessionDisconnectedAsync` | Stubbed — reserved for future session-bound actors |
| `actor.pool-node.registered` | `HandlePoolNodeRegisteredAsync` | Control plane: registers node in pool via ActorPoolManager |
| `actor.pool-node.heartbeat` | `HandlePoolNodeHeartbeatAsync` | Control plane: updates node heartbeat timestamp and load |
| `actor.pool-node.draining` | `HandlePoolNodeDrainingAsync` | Control plane: marks node as draining, stops routing |
| `actor.instance.status-changed` | `HandleActorStatusChangedAsync` | Control plane: syncs assignment status via ActorPoolManager |
| `actor.instance.completed` | `HandleActorCompletedAsync` | Control plane: removes assignment via ActorPoolManager |
| `actor.template.updated` | `HandleActorTemplateUpdatedAsync` | If behaviorRef changed: invalidates cached behavior document, signals running actors to reload |

All pool-node event handlers skip processing when `DeploymentMode == Bannou`.

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<ActorService>` | Structured logging |
| `ActorServiceConfiguration` | 36 config properties (deployment, pool, behavior loop, GOAP, perception, timeouts) |
| `IStateStoreFactory` | State store access (constructor-cached as `_templateStore`, `_templateIndexStore`) |
| `IMessageBus` | Event publishing, pool commands |
| `IEventConsumer` | Event handler registration |
| `IMeshInvocationClient` | Remote pool node forwarding |
| `IResourceClient` | Character reference tracking |
| `ICharacterClient` | Realm lookup on spawn (L2 same-layer) |
| `ITelemetryProvider` | Span instrumentation |
| `IActorRegistry` | In-memory tracking of locally running ActorRunner instances (ConcurrentDictionary) |
| `IActorRunnerFactory` | Creates ActorRunner instances with full dependency graph |
| `IBehaviorDocumentLoader` | Priority-ordered provider chain for ABML document loading |
| `IActorPoolManager` | Redis-backed pool node registry and actor assignment management |

**Helper services (not DI-injected into ActorService but part of plugin):**

| Service | Role |
|---------|------|
| `ActorRunner` | Per-actor behavior execution loop: perception queue, cognition pipeline, ABML execution, state persistence |
| `ActorPoolManager` | All pool-mode Redis operations: node registry, actor assignments, load tracking |
| `BehaviorDocumentLoader` | Aggregates IBehaviorDocumentProvider implementations by priority |
| `SeededBehaviorProvider` | Loads static ABML behaviors from embedded resources (Priority 50) |
| `FallbackBehaviorProvider` | Terminal provider — logs warning, returns null (Priority 0) |
| `DocumentExecutorFactory` | Creates IDocumentExecutor instances for ABML bytecode execution |
| `BehaviorSeededResourceProvider` | Implements ISeededResourceProvider to expose behaviors via lib-resource |

---

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| CreateActorTemplate | POST /actor/template/create | generated | developer | template, category-index, template-index | actor.template.created |
| GetActorTemplate | POST /actor/template/get | generated | [] | - | - |
| ListActorTemplates | POST /actor/template/list | generated | [] | - | - |
| UpdateActorTemplate | POST /actor/template/update | generated | developer | template, category-index | actor.template.updated |
| DeleteActorTemplate | POST /actor/template/delete | generated | developer | template, category-index, template-index | actor.template.deleted |
| SpawnActor | POST /actor/spawn | generated | [] | assignment (pool), registry (bannou) | actor.instance.created |
| GetActor | POST /actor/get | generated | [] | registry (auto-spawn) | - |
| StopActor | POST /actor/stop | generated | [] | assignment (pool), registry (bannou) | actor.instance.deleted |
| BindActorCharacter | POST /actor/bind-character | generated | [] | assignment (pool) | actor.instance.character-bound |
| CleanupByCharacter | POST /actor/cleanup-by-character | generated | [] | assignment (pool), registry (bannou) | - |
| ListActors | POST /actor/list | generated | [] | - | - |
| InjectPerception | POST /actor/inject-perception | generated | [] | - | - |
| QueryOptions | POST /actor/query-options | generated | [] | - | - |
| StartEncounter | POST /actor/encounter/start | generated | [] | encounter (in-memory) | actor.encounter.started |
| UpdateEncounterPhase | POST /actor/encounter/update-phase | generated | [] | encounter (in-memory) | actor.encounter.phase-changed |
| EndEncounter | POST /actor/encounter/end | generated | [] | encounter (in-memory) | actor.encounter.ended |
| GetEncounter | POST /actor/encounter/get | generated | [] | - | - |

---

## Methods

### CreateActorTemplate
POST /actor/template/create | Roles: [developer]

```
IF body.Category is null/whitespace OR body.BehaviorRef is null/whitespace
 RETURN (400, null)
READ actor-templates:category:{body.Category} -> 409 if exists (duplicate)
WRITE actor-templates:{templateId} <- ActorTemplateData from request
 // tickIntervalMs, autoSaveIntervalSeconds, maxInstancesPerNode use config defaults if <= 0
WRITE actor-templates:category:{body.Category} <- same template data
READ actor-templates:_all_template_ids [with ETag]
 // UpdateTemplateIndexAsync: add templateId, retry up to 3x on ETag mismatch
ETAG-WRITE actor-templates:_all_template_ids <- updated list
PUBLISH actor.template.created { TemplateId, Category, BehaviorRef, CreatedAt }
RETURN (200, ActorTemplateResponse)
```

### GetActorTemplate
POST /actor/template/get | Roles: []

```
IF body.TemplateId has value
 READ actor-templates:{body.TemplateId} -> 404 if null
ELSE IF body.Category is non-empty
 READ actor-templates:category:{body.Category} -> 404 if null
ELSE
 RETURN (400, null) // neither provided
RETURN (200, ActorTemplateResponse)
```

### ListActorTemplates
POST /actor/template/list | Roles: []

```
READ actor-templates:_all_template_ids // default empty list if null
READ actor-templates:bulk({allIds}) // bulk load all templates
// In-memory: OrderBy(CreatedAt), Skip(body.Offset), Take(body.Limit)
RETURN (200, ListActorTemplatesResponse { Templates, Total: allCount })
```

### UpdateActorTemplate
POST /actor/template/update | Roles: [developer]

```
READ actor-templates:{body.TemplateId} [with ETag] -> 404 if null
// Selective field update: only change fields where request value differs from current
// Track changedFields list
ETAG-WRITE actor-templates:{body.TemplateId} <- updated template -> 409 if ETag mismatch
WRITE actor-templates:category:{template.Category} <- updated template
PUBLISH actor.template.updated { TemplateId, Category, BehaviorRef, CreatedAt, ChangedFields }
RETURN (200, ActorTemplateResponse)
```

### DeleteActorTemplate
POST /actor/template/delete | Roles: [developer]

```
READ actor-templates:{body.TemplateId} -> 404 if null
IF body.ForceStopActors
 FOREACH runner in registry.GetByTemplateId(body.TemplateId)
 // Per-actor errors caught and logged (not fatal)
 runner.StopAsync() + runner.DisposeAsync() + registry.TryRemove()
DELETE actor-templates:{body.TemplateId}
DELETE actor-templates:category:{template.Category}
READ actor-templates:_all_template_ids [with ETag]
 // UpdateTemplateIndexAsync: remove templateId, retry up to 3x on ETag mismatch
ETAG-WRITE actor-templates:_all_template_ids <- updated list
PUBLISH actor.template.deleted { TemplateId, Category, BehaviorRef, CreatedAt, DeletedReason }
RETURN (200, DeleteActorTemplateResponse { StoppedActorCount })
```

### SpawnActor
POST /actor/spawn | Roles: []

```
READ actor-templates:{body.TemplateId} -> 404 if null
// actorId = body.ActorId ?? "{category}-{Guid:N}"
// ResolveRealmIdAsync: body.RealmId ?? CALL ICharacterClient.GetCharacterAsync(characterId).RealmId
IF characterId set AND realmId unresolvable
 RETURN (400, null)
IF bannou mode
 IF registry.Contains(actorId)
 RETURN (409, null)
 // Create ActorRunner via factory, register in ActorRegistry, start with timeout
 // On start failure: remove from registry, dispose
 IF characterId set
 CALL IResourceClient.RegisterReferenceAsync(...)
 // ActorRunner.StartAsync publishes actor.instance.started
 // ActorRunner.StartAsync publishes actor.instance.character-bound (if characterId set)
ELSE (pool mode)
 READ actor-assignments:{actorId} -> 409 if exists
 CALL ActorPoolManager.AcquireNodeForActorAsync(category) -> 503 if no capacity
 IF characterId set
 CALL IResourceClient.RegisterReferenceAsync(...)
 WRITE actor-assignments:{actorId} <- ActorAssignment via ActorPoolManager
 PUBLISH actor.node.{poolNode.AppId}.spawn { SpawnActorCommand }
PUBLISH actor.instance.created { ActorId, TemplateId, CharacterId, NodeId, Status, StartedAt }
RETURN (200, ActorInstanceResponse)
```

### GetActor
POST /actor/get | Roles: []

```
// Three-level lookup:
// 1. Local registry
IF registry.TryGet(body.ActorId) -> runner found
 RETURN (200, ActorInstanceResponse from runner snapshot)
// 2. Pool assignment store
IF pool mode
 READ actor-assignments:{body.ActorId}
 IF found -> RETURN (200, ActorInstanceResponse from assignment)
// 3. Auto-spawn: scan templates for matching IdPattern regex
READ actor-templates:_all_template_ids
READ actor-templates:bulk({allIds})
FOREACH template WHERE AutoSpawn.Enabled
 // Compiled+cached Regex with 100ms timeout
 IF template.AutoSpawn.IdPattern matches body.ActorId
 // Extract CharacterId from regex capture group if configured
 // Check MaxInstances against registry + pool assignments
 CALL SpawnActorAsync(template, body.ActorId, characterId)
 IF spawn returns 200
 RETURN (200, ActorInstanceResponse from spawn result)
 ELSE
 RETURN (404, null) // auto-spawn failure hidden from caller
RETURN (404, null)
```

### StopActor
POST /actor/stop | Roles: []

```
IF bannou mode
 IF NOT registry.TryGet(body.ActorId)
 RETURN (404, null)
 runner.StopAsync(body.Graceful) with timeout from config.ActorOperationTimeoutSeconds
 runner.DisposeAsync()
 registry.TryRemove(body.ActorId)
 IF runner had CharacterId
 CALL IResourceClient.UnregisterReferenceAsync(...)
 PUBLISH actor.instance.deleted { ActorId, TemplateId, CharacterId, NodeId, Status, StartedAt, DeletedReason }
 RETURN (200, StopActorResponse { FinalStatus: runner.Status })
ELSE (pool mode)
 READ actor-assignments:{body.ActorId} -> 404 if null
 IF assignment had CharacterId
 CALL IResourceClient.UnregisterReferenceAsync(...)
 DELETE actor-assignments:{body.ActorId} via ActorPoolManager
 PUBLISH actor.node.{assignment.NodeAppId}.stop { StopActorCommand }
 RETURN (200, StopActorResponse { FinalStatus: Stopping }) // optimistic — actual stop is async
```

### BindActorCharacter
POST /actor/bind-character | Roles: []

```
IF bannou mode
 IF NOT registry.TryGet(body.ActorId)
 RETURN (404, null)
 runner.BindCharacterAsync(body.CharacterId)
 // Guard: already bound -> InvalidOperationException -> 400
 // Sets CharacterId, establishes per-character RabbitMQ subscription
 // PUBLISH actor.instance.character-bound { ActorId, CharacterId, RealmId }
 CALL IResourceClient.RegisterReferenceAsync(...)
 RETURN (200, ActorInstanceResponse)
ELSE (pool mode)
 READ actor-assignments:{body.ActorId} -> 404 if null
 IF assignment.CharacterId already set
 RETURN (400, null)
 // UpdateActorCharacterAsync: ETag update with PoolConcurrencyMaxRetries retries
 WRITE actor-assignments:{body.ActorId} <- updated with CharacterId
 CALL IResourceClient.RegisterReferenceAsync(...)
 PUBLISH actor.node.{assignment.NodeAppId}.bind-character { BindActorCharacterCommand }
 RETURN (200, ActorInstanceResponse)
```

### CleanupByCharacter
POST /actor/cleanup-by-character | Roles: []

```
// Called by lib-resource cascade when character is deleted
IF bannou mode
 FOREACH runner in registry.GetAllRunners() WHERE runner.CharacterId == body.CharacterId
 // Per-actor errors caught and logged (not fatal)
 runner.StopAsync(graceful: true) with timeout
 runner.DisposeAsync()
 registry.TryRemove(runner.ActorId)
ELSE (pool mode)
 // Scan actor index, read assignments individually
 FOREACH assignment in poolManager.GetAssignmentsByCharacterAsync(body.CharacterId)
 PUBLISH actor.node.{assignment.NodeAppId}.stop { StopActorCommand }
 DELETE actor-assignments:{assignment.ActorId} via ActorPoolManager
// Does NOT call UnregisterCharacterReferenceAsync — character is already being deleted
RETURN (200, CleanupByCharacterResponse { ActorsCleanedUp, ActorIds })
```

### ListActors
POST /actor/list | Roles: []

```
IF pool mode AND body.NodeId is set
 // Delegate to ActorPoolManager.ListActorsByNodeAsync
 // Reads actor index, then individual assignments from Redis
ELSE
 IF body.NodeId is set AND body.NodeId != config.LocalModeNodeId
 RETURN (200, ListActorsResponse { Actors: [], Total: 0 }) // wrong node
 // Get all runners from in-memory registry
// Apply in-memory filters: Category, Status, CharacterId
// Skip(body.Offset), Take(body.Limit)
RETURN (200, ListActorsResponse { Actors, Total })
```

### InjectPerception
POST /actor/inject-perception | Roles: []

```
// Local-only — no pool-mode forwarding
IF NOT registry.TryGet(body.ActorId)
 RETURN (404, null)
runner.InjectPerception(body.Perception) // synchronous Channel.Writer.TryWrite
RETURN (200, InjectPerceptionResponse { QueueDepth })
```

### QueryOptions
POST /actor/query-options | Roles: []

```
IF NOT registry.TryGet(body.ActorId)
 IF pool mode
 READ actor-assignments:{body.ActorId}
 IF found -> RETURN (400, null) // actor on remote node, can't query locally
 RETURN (404, null)
IF body.Freshness == Fresh AND body.Context != null
 // Inject options_query perception into actor
 runner.InjectPerception(optionsQueryPerception)
 // Wait approximately one tick
 Task.Delay(config.DefaultTickIntervalMs)
// Read actor state snapshot from runner in-memory state
// Extract options from memories where key == "{queryType}_options"
// Build CharacterContext from character-related state (if CharacterId set)
RETURN (200, QueryOptionsResponse { ActorId, QueryType, Options, ComputedAt, AgeMs, CharacterContext? })
```

### StartEncounter
POST /actor/encounter/start | Roles: []

```
// FindActorAsync: check local registry, then pool assignments
IF actor not found
 RETURN (404, null)
IF actor on remote pool node
 CALL IMeshInvocationClient.InvokeMethodAsync(nodeId, "actor/encounter/start", body)
 RETURN result
IF runner already has active encounter
 RETURN (409, null)
runner.StartEncounter(body.EncounterId, body.EncounterType, body.Participants, body.InitialData)
 // Fire-and-forget: PUBLISH actor.encounter.started { ActorId, EncounterId, EncounterType, Participants }
RETURN (200, StartEncounterResponse)
```

### UpdateEncounterPhase
POST /actor/encounter/update-phase | Roles: []

```
// FindActorAsync: check local registry, then pool assignments
IF actor not found
 RETURN (404, null)
IF actor on remote pool node
 CALL IMeshInvocationClient.InvokeMethodAsync(nodeId, "actor/encounter/phase/update", body)
 RETURN result
// Read current encounter snapshot
IF no active encounter
 RETURN (404, null)
runner.SetEncounterPhase(body.Phase)
 // Fire-and-forget: PUBLISH actor.encounter.phase-changed { ActorId, EncounterId, PreviousPhase, NewPhase }
RETURN (200, UpdateEncounterPhaseResponse { ActorId, PreviousPhase, CurrentPhase })
```

### EndEncounter
POST /actor/encounter/end | Roles: []

```
// FindActorAsync: check local registry, then pool assignments
IF actor not found
 RETURN (404, null)
IF actor on remote pool node
 CALL IMeshInvocationClient.InvokeMethodAsync(nodeId, "actor/encounter/end", body)
 RETURN result
// Read current encounter snapshot
IF no active encounter
 RETURN (404, null)
runner.EndEncounter()
 // Fire-and-forget: PUBLISH actor.encounter.ended { ActorId, EncounterId, DurationSeconds, FinalPhase }
RETURN (200, EndEncounterResponse { ActorId, EncounterId, DurationMs })
```

### GetEncounter
POST /actor/encounter/get | Roles: []

```
// FindActorAsync: check local registry, then pool assignments
IF actor not found
 RETURN (404, null)
IF actor on remote pool node
 CALL IMeshInvocationClient.InvokeMethodAsync(nodeId, "actor/encounter/get", body)
 RETURN result
// Read encounter state from runner snapshot
RETURN (200, GetEncounterResponse { ActorId, Encounter: encounterState? })
 // Encounter is null if no active encounter (200, not 404)
```

---

## Background Services

### PoolHealthMonitor
**Interval**: `config.PoolHealthCheckIntervalSeconds` (default: 15s)
**Startup delay**: `config.PoolHealthMonitorStartupDelaySeconds` (default: 5s)
**Active when**: `DeploymentMode != Bannou` AND `PoolNodeId` is empty (control plane only)
**Purpose**: Detects unresponsive pool nodes by heartbeat timeout and removes them from the pool

```
FOREACH node in poolManager.GetUnhealthyNodesAsync(HeartbeatTimeout)
 PUBLISH actor.pool-node.unhealthy { NodeId, AppId, Reason, LastHeartbeat, ActorCount }
 CALL poolManager.RemoveNodeAsync(nodeId)
 // Reads assignments for node, deletes each, deletes node state, updates node index
IF unhealthyNodes found AND healthyNodes < config.MinPoolNodes
 TryPublishErrorAsync("InsufficientPoolNodes")
```

### ActorPoolNodeWorker
**Active when**: `config.PoolNodeId` is set (pool node instances only)
**Purpose**: Pool node command listener — subscribes to spawn/stop/message/bind-character commands

```
// On startup:
SUBSCRIBE actor.node.{appId}.spawn -> HandleSpawnCommandAsync
SUBSCRIBE actor.node.{appId}.stop -> HandleStopCommandAsync
SUBSCRIBE actor.node.{appId}.message -> HandleMessageCommandAsync
SUBSCRIBE actor.node.{appId}.bind-character -> HandleBindCharacterCommandAsync
PUBLISH actor.pool-node.registered { NodeId, AppId, PoolType, Capacity }
HeartbeatEmitter.Start()

// On spawn command:
// Create ActorRunner via factory, register in ActorRegistry, start
PUBLISH actor.instance.status-changed { Pending -> Running }

// On stop command:
// Stop runner, dispose, remove from registry
PUBLISH actor.instance.status-changed { previousStatus -> Stopped }
PUBLISH actor.instance.completed { ActorId, ExitReason: ExternalStop, LoopIterations }

// On shutdown:
// Stop all local runners
IF remainingActors > 0
 PUBLISH actor.pool-node.draining { NodeId, RemainingActors, EstimatedDrainTimeSeconds }
```

### HeartbeatEmitter
**Interval**: `config.HeartbeatIntervalSeconds` (default: 10s)
**Active when**: `config.PoolNodeId` is set AND `config.PoolNodeAppId` is set
**Purpose**: Periodic pool node heartbeat to signal liveness to control plane

```
EVERY HeartbeatIntervalSeconds:
 PUBLISH actor.pool-node.heartbeat { NodeId, AppId, CurrentLoad: registry.Count, Capacity }
```
