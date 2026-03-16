# Orchestrator Implementation Map

> **Plugin**: lib-orchestrator
> **Schema**: schemas/orchestrator-api.yaml
> **Layer**: AppFeatures
> **Deep Dive**: [docs/plugins/ORCHESTRATOR.md](../plugins/ORCHESTRATOR.md)

---

| Field | Value |
|-------|-------|
| Plugin | lib-orchestrator |
| Layer | L3 AppFeatures |
| Endpoints | 23 |
| State Stores | orchestrator-heartbeats (Redis), orchestrator-routings (Redis), orchestrator-config (Redis), orchestrator-statestore (Redis) |
| Events Published | 5 via IMessageBus (`orchestrator.health-ping`, `bannou.deployment-events`, `bannou.service-lifecycle`, `bannou.configuration-events`, `orchestrator.processor.released`) + 1 via DI push (`FullServiceMappingsEvent` → `IServiceMappingReceiver`) |
| Events Consumed | 1 (`bannou.service-heartbeat`) |
| Client Events | 0 |
| Background Services | 2 (FullMappingsTimer, LeaseCleanupTimer) |

---

## State

**Store**: `orchestrator-heartbeats` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `_index` | `HeartbeatIndex` | Set of all known app IDs (avoids KEYS/SCAN) |
| `{appId}` | `InstanceHealthState` | Per-instance health with TTL (`HeartbeatTtlSeconds`, 90s) |

**Store**: `orchestrator-routings` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `_index` | `RoutingIndex` | Set of all routed service names |
| `{serviceName}` | `ServiceRouting` | Service-to-appId routing entry with TTL (`RoutingTtlSeconds`, 300s) |

**Store**: `orchestrator-config` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `current` | `DeploymentConfiguration` | Active deployment configuration |
| `version` | `ConfigVersion` | Current config version counter |
| `history:{n}` | `DeploymentConfiguration` | Historical config version with TTL (`ConfigHistoryTtlDays`, 30d) |
| `mappings-version` | `ConfigVersion` | Incremented on every routing broadcast |

**Store**: `orchestrator-statestore` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `processing-pool:{type}:instances` | `List<ProcessorInstance>` | All instances in pool |
| `processing-pool:{type}:available` | `List<ProcessorInstance>` | Available (unleased) instances |
| `processing-pool:{type}:leases` | `Dictionary<string, ProcessorLease>` | Active leases by lease ID |
| `processing-pool:{type}:metrics` | `PoolMetricsData` | Jobs completed/failed counts, avg time |
| `processing-pool:{type}:config` | `PoolConfiguration` | Pool settings (min/max instances, thresholds) |
| `processing-pool:known` | `List<string>` | Registry of known pool type codes |

**Lock Store**: `orchestrator-pool` (Backend: Redis)

| Key Pattern | Purpose |
|-------------|---------|
| `{poolType}` | Distributed lock for pool acquire/release/cleanup (TTL: `PoolLockTimeoutSeconds`, 15s) |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | All Redis state via `IOrchestratorStateManager` |
| lib-state (`IDistributedLockProvider`) | L0 | Hard | Pool-level locks, mappings version increment lock |
| lib-messaging (`IMessageBus`) | L0 | Hard | All event publishing |
| lib-messaging (`IEventConsumer`) | L0 | Hard | Heartbeat subscription |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Span instrumentation |
| `IHttpClientFactory` | Framework | Hard | Direct HTTP to OpenResty cache invalidation |
| `AppConfiguration` | Shared | Hard | `EffectiveAppId`, `DEFAULT_APP_NAME` |
| `IControlPlaneServiceProvider` | Shared | Hard | In-process service list for health reporting |

No Bannou service client dependencies (`I{Service}Client`). All external I/O is to container backends (Docker/Portainer/K8s) via `IContainerOrchestrator` and to OpenResty via `IHttpClientFactory`.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `orchestrator.health-ping` | `OrchestratorHealthPingEvent` | GetInfrastructureHealth verifies pub/sub path |
| `bannou.deployment-events` | `DeploymentEvent` | Deploy started/completed/failed, teardown completed/failed |
| `bannou.service-lifecycle` | `ServiceRestartEvent` | After SmartRestartManager completes a restart |
| `bannou.configuration-events` | `ConfigurationChangedEvent` | RollbackConfiguration and NotifyConfigChange publish for self-service restart pattern |
| `orchestrator.processor.released` | `ProcessorReleasedEvent` | ReleaseProcessor returns a processor to the pool |

**DI Push (not IMessageBus):**

| Target | Event Type | Trigger |
|--------|-----------|---------|
| `IServiceMappingReceiver` | `FullServiceMappingsEvent` | Periodic timer (30s) and every routing change — pushed via `IEnumerable<IServiceMappingReceiver>` DI interface to lib-mesh, which handles cross-node broadcast |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `bannou.service-heartbeat` | `HandleServiceHeartbeatAsync` | Routes to `OrchestratorEventManager.ReceiveHeartbeat` which raises C# event; `ServiceHealthMonitor` writes heartbeat to Redis, initializes routing if missing, publishes full mappings on new routes |

---

## DI Services

#### Constructor Dependencies

| Service | Role |
|---------|------|
| `ILogger<OrchestratorService>` | Structured logging |
| `ILoggerFactory` | Creates logger for `PresetLoader` (instantiated via `new`) |
| `OrchestratorServiceConfiguration` | All 35 config properties |
| `AppConfiguration` | Global app identity (`EffectiveAppId`) |
| `IDistributedLockProvider` | Pool-level distributed locks |
| `IMessageBus` | Event publishing (health-ping, config-events, processor.released) |
| `IEventConsumer` | Heartbeat event subscription registration |
| `IHttpClientFactory` | OpenResty cache invalidation and Portainer API calls |
| `ITelemetryProvider` | Span instrumentation |
| `IOrchestratorStateManager` | All Redis state CRUD (wraps `IStateStoreFactory` internally) |
| `IOrchestratorEventManager` | Event publishing delegation + heartbeat relay + DI push to mesh |
| `IServiceHealthMonitor` | Routing table, heartbeat evaluation, background timers |
| `ISmartRestartManager` | Docker-based container restart via Docker.DotNet |
| `IBackendDetector` | Backend detection + `IContainerOrchestrator` factory |
| `PresetLoader` | YAML preset file loading (not DI — instantiated with `new` in constructor) |

#### Collection-Injected Providers

| Interface | Injection | Source | Role |
|-----------|-----------|--------|------|
| `IEnumerable<IServiceMappingReceiver>` | Collection | External (lib-mesh registers `MeshServiceMappingReceiver`) | Receives full service-to-appId routing maps for local resolver update |

#### DI Interfaces Consumed by Helper Services

| Interface | Consumed By | Direction | Purpose |
|-----------|-------------|-----------|---------|
| `IControlPlaneServiceProvider` | `ServiceHealthMonitor` | Framework→L3 pull | Lists plugins loaded in-process for control-plane health reporting |

---

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| GetInfrastructureHealth | POST /orchestrator/health/infrastructure | generated | [] | - | orchestrator.health-ping |
| GetServicesHealth | POST /orchestrator/health/services | generated | [] | - | - |
| RestartService | POST /orchestrator/services/restart | generated | [] | - | bannou.service-lifecycle |
| ShouldRestartService | POST /orchestrator/services/should-restart | generated | [] | - | - |
| GetBackends | POST /orchestrator/backends/list | generated | [] | - | - |
| GetPresets | POST /orchestrator/presets/list | generated | [] | - | - |
| Deploy | POST /orchestrator/deploy | generated | [] | routings, config | bannou.deployment-events, DI push (FullServiceMappingsEvent) |
| GetServiceRouting | POST /orchestrator/service-routing | generated | [] | - | - |
| GetStatus | POST /orchestrator/status | generated | [] | - | - |
| Teardown | POST /orchestrator/teardown | generated | [] | routings | bannou.deployment-events, DI push (FullServiceMappingsEvent) |
| Clean | POST /orchestrator/clean | generated | [] | - | - |
| GetLogs | POST /orchestrator/logs | generated | [] | - | - |
| UpdateTopology | POST /orchestrator/topology | generated | [] | routings, config | DI push (FullServiceMappingsEvent) |
| RequestContainerRestart | POST /orchestrator/containers/request-restart | generated | [] | - | - |
| GetContainerStatus | POST /orchestrator/containers/status | generated | [] | - | - |
| RollbackConfiguration | POST /orchestrator/config/rollback | generated | [] | config | bannou.configuration-events |
| GetConfigVersion | POST /orchestrator/config/version | generated | [] | - | - |
| NotifyConfigChange | POST /orchestrator/config/notify-change | generated | [] | - | bannou.configuration-events |
| AcquireProcessor | POST /orchestrator/processing-pool/acquire | generated | [] | pool leases, pool available, pool metrics | - |
| ReleaseProcessor | POST /orchestrator/processing-pool/release | generated | [] | pool leases, pool available, pool metrics | orchestrator.processor.released |
| GetPoolStatus | POST /orchestrator/processing-pool/status | generated | [] | - | - |
| ScalePool | POST /orchestrator/processing-pool/scale | generated | [] | pool instances, pool available, pool metrics | - |
| CleanupPool | POST /orchestrator/processing-pool/cleanup | generated | [] | pool leases, pool available, pool instances, pool metrics | - |

---

## Methods

### GetInfrastructureHealth
POST /orchestrator/health/infrastructure | Roles: []

```
CALL _backendDetector.DetectBackendsAsync()
  // Probes Docker socket, Portainer API, Kubernetes config in parallel
PUBLISH orchestrator.health-ping { status: Ok }
  // Verifies pub/sub path is working
RETURN (200, InfrastructureHealthResponse { healthy, components })
```

### GetServicesHealth
POST /orchestrator/health/services | Roles: []

```
// Delegates to ServiceHealthMonitor.GetServiceHealthReportAsync
READ heartbeats:_index
FOREACH appId in index.AppIds
  READ heartbeats:{appId}                          // TTL-backed, may be null
IF request.Source == ControlPlaneOnly
  // Uses IControlPlaneServiceProvider.GetRegisteredServices()
  // Control plane services are always Healthy
ELSE IF request.Source == DeployedOnly
  // Excludes control plane appId from results
ELSE
  // Combines both control plane and deployed services
// Evaluates each entry against HeartbeatTimeoutSeconds and DegradationThresholdMinutes
RETURN (200, ServiceHealthReport { source, healthyServices, unhealthyServices, healthPercentage })
```

### RestartService
POST /orchestrator/services/restart | Roles: []

```
// Delegates to SmartRestartManager.RestartServiceAsync
IF !request.Force
  CALL _healthMonitor.ShouldRestartServiceAsync(serviceName)
  IF !shouldRestart -> RETURN (200, ServiceRestartResult { declined })
CALL DockerClient.Containers.ListContainersAsync()
  // Matches by com.docker.compose.service label or name substring
CALL DockerClient.Containers.RestartContainerAsync(containerId, { WaitBeforeKillSeconds })
// Polls ShouldRestartServiceAsync in loop until healthy or RestartTimeoutSeconds elapses
PUBLISH bannou.service-lifecycle { serviceName, reason, forced }
RETURN (200, ServiceRestartResult { duration, previousStatus, currentStatus })
```

### ShouldRestartService
POST /orchestrator/services/should-restart | Roles: []

```
// Delegates to ServiceHealthMonitor.ShouldRestartServiceAsync
READ heartbeats:_index
FOREACH appId in index.AppIds
  READ heartbeats:{appId}
// Filters entries containing requested serviceName
IF none found -> RETURN (200, RestartRecommendation { shouldRestart: false, status: Unknown })
IF lastSeen > HeartbeatTimeoutSeconds -> RETURN (200, { shouldRestart: true, status: Unavailable })
IF degradedDuration > DegradationThresholdMinutes -> RETURN (200, { shouldRestart: true, status: Degraded })
RETURN (200, RestartRecommendation { shouldRestart: false, worstStatus })
```

### GetBackends
POST /orchestrator/backends/list | Roles: []

```
CALL _backendDetector.DetectBackendsAsync()
  // Probes Docker socket, Portainer API, Kubernetes config in parallel
RETURN (200, BackendsResponse { backends, recommended })
```

### GetPresets
POST /orchestrator/presets/list | Roles: []

```
CALL _presetLoader.ListPresetsAsync()
  // Reads *.yaml files from PresetsHostPath directory
  // Deserializes YAML via YamlDotNet
RETURN (200, PresetsResponse { presets })
```

### Deploy
POST /orchestrator/deploy | Roles: []

```
// Load or build topology
IF request.Preset != null
  CALL _presetLoader.LoadPresetAsync(presetName)
  CALL _presetLoader.ConvertToTopology(preset)
ELSE IF request.Topology != null
  // Use provided topology directly
ELSE
  -> RETURN (400, null)

// Resolve backend
CALL _backendDetector.CreateOrchestrator(request.Backend ?? config.DefaultBackend)

IF !dryRun
  PUBLISH bannou.deployment-events { action: Started, deploymentId }

// Build environment: merge current config + preset env + request env + host env
READ config:current                                // via EnsureLastDeploymentLoadedAsync
// 4-layer env merge: stored config -> preset -> request -> host env (filtered)

FOREACH node in topology.Nodes
  IF dryRun
    // Collect what would be deployed without actually deploying
    ADD to deployedServices { name, status: Starting }
    CONTINUE
  CALL _orchestrator.DeployServiceAsync(node.Name, node.AppId, mergedEnv)
  // On failure: rollback all previously successful nodes, then continue
  // On success: poll GetContainerStatusAsync until Running or timeout

FOREACH deployed service                           // Skip in dryRun
  CALL _healthMonitor.SetServiceRoutingAsync(serviceName, routing)
  CALL InvalidateOpenRestyRoutingCacheAsync(serviceName)
    // Direct HTTP to OpenResty; failure is non-blocking

// Initialize pool configs if preset includes them
IF preset has poolConfigurations
  FOREACH poolConfig
    WRITE pool:{type}:config <- PoolConfiguration from preset

// Save versioned deployment config (skip in dryRun)
IF success AND !dryRun
  WRITE config:current <- DeploymentConfiguration
  WRITE config:version <- version + 1
  WRITE config:history:{newVersion} <- DeploymentConfiguration (TTL: ConfigHistoryTtlDays)

IF !dryRun
  PUBLISH bannou.deployment-events { action: Completed, deploymentId, services }
// ServiceHealthMonitor publishes bannou.full-service-mappings on routing changes
RETURN (200, DeployResponse { success, deploymentId, backend, duration, services, warnings })
```

### GetServiceRouting
POST /orchestrator/service-routing | Roles: []

```
IF request.ServiceFilter != null
  READ routings:{serviceName}
  IF null -> RETURN (404, null)
  RETURN (200, ServiceRoutingResponse { mappings: { serviceName: routing } })
ELSE
  READ routings:_index
  FOREACH serviceName in index.ServiceNames
    READ routings:{serviceName}
  RETURN (200, ServiceRoutingResponse { mappings, defaultAppId, generatedAt })
```

### GetStatus
POST /orchestrator/status | Roles: []

```
READ config:current                                // via EnsureLastDeploymentLoadedAsync
READ routings:_index
FOREACH serviceName in index.ServiceNames
  READ routings:{serviceName}
READ heartbeats:_index
FOREACH appId in index.AppIds
  READ heartbeats:{appId}
RETURN (200, EnvironmentStatus { deployed, configuration, activeRoutings, serviceHealth })
```

### Teardown
POST /orchestrator/teardown | Roles: []

```
// Resolve which appIds to tear down
IF request.AppId specified -> single appId
ELSE IF request.PresetName specified -> load preset, extract appId list
ELSE -> all known appIds from heartbeat index

CALL _backendDetector.CreateOrchestrator(config.DefaultBackend)

IF !dryRun
  PUBLISH bannou.deployment-events { action: TopologyChanged }

// List all containers, filter out infrastructure unless includeInfrastructure
IF dryRun
  RETURN (200, TeardownResponse { preview of what would be torn down })

FOREACH appId
  CALL _orchestrator.TeardownServiceAsync(appId, removeVolumes)
  CALL _healthMonitor.RestoreServiceRoutingToDefaultAsync(serviceName)
    // Sets routing to EffectiveAppId, does NOT delete entry
  CALL InvalidateOpenRestyRoutingCacheAsync(serviceName)

IF request.IncludeInfrastructure
  // Also tears down infrastructure containers (redis, rabbitmq, etc.)

PUBLISH bannou.deployment-events { action: Completed/Failed }
// ServiceHealthMonitor publishes bannou.full-service-mappings on routing changes
RETURN (200, TeardownResponse { duration, stoppedContainers, removedVolumes })
```

### Clean
POST /orchestrator/clean | Roles: []

```
CALL _orchestrator.ListContainersAsync()
FOREACH container
  // IsOrphanedContainer: checks bannou.app-id label + heartbeat time label > OrphanContainerThresholdHours
  IF orphaned AND !request.DryRun
    CALL _orchestrator.TeardownServiceAsync(appId, removeVolumes: false)
// Network/volume/image pruning delegates to IContainerOrchestrator
IF request.Targets includes Networks
  CALL _orchestrator.PruneNetworksAsync()
IF request.Targets includes Volumes
  CALL _orchestrator.PruneVolumesAsync()
IF request.Targets includes Images
  CALL _orchestrator.PruneImagesAsync()
RETURN (200, CleanResponse { removedContainers, removedNetworks, removedVolumes, removedImages, reclaimedSpaceMb })
```

### GetLogs
POST /orchestrator/logs | Roles: []

```
// Resolves target from service name or container name (falls back to "bannou")
CALL _orchestrator.GetContainerLogsAsync(appId, tail, since)
  // Parses log text into LogEntry objects
  // Docker timestamp prefix (ISO 8601 with nanoseconds); continuation lines inherit preceding timestamp
IF not found -> RETURN (404, null)
RETURN (200, LogsResponse { logs, service, container })
```

### UpdateTopology
POST /orchestrator/topology | Roles: []

```
IF request.ResetToDefault
  CALL _healthMonitor.ResetAllMappingsToDefaultAsync()
    // Writes all routing entries to EffectiveAppId, updates index
    // Publishes bannou.full-service-mappings
  RETURN (200, TopologyUpdateResponse)

CALL _backendDetector.CreateOrchestrator(config.DefaultBackend)

FOREACH change in request.Changes
  IF change.Action == AddNode
    // Deploy services with bannou-{service}-{nodeName} appId
    CALL _orchestrator.DeployServiceAsync(...)
    CALL _healthMonitor.SetServiceRoutingAsync(serviceName, routing)
  ELSE IF change.Action == RemoveNode
    CALL _orchestrator.TeardownServiceAsync(appId)
    CALL _healthMonitor.RestoreServiceRoutingToDefaultAsync(serviceName)
  ELSE IF change.Action == MoveService
    // Update routing only, no container changes
    CALL _healthMonitor.SetServiceRoutingAsync(serviceName, routing)
  ELSE IF change.Action == Scale
    CALL _orchestrator.ScaleServiceAsync(appId, replicas)
  ELSE IF change.Action == UpdateEnv
    CALL _orchestrator.DeployServiceAsync(...)  // Handles cleanup+recreation
  CALL InvalidateOpenRestyRoutingCacheAsync(serviceName)
  // Per-change success/error tracking; partial success reported

PUBLISH bannou.full-service-mappings (via _eventManager.PublishFullMappingsAsync)
WRITE config:current <- updated DeploymentConfiguration
WRITE config:version <- version + 1
WRITE config:history:{newVersion} <- DeploymentConfiguration (TTL: ConfigHistoryTtlDays)
RETURN (200, TopologyUpdateResponse { appliedChanges, warnings })
```

### RequestContainerRestart
POST /orchestrator/containers/request-restart | Roles: []

```
CALL _backendDetector.CreateOrchestrator(config.DefaultBackend)
CALL _orchestrator.RestartContainerAsync(request.AppName, request)
IF not found -> RETURN (404, null)
RETURN (200, ContainerRestartResponse { accepted })
```

### GetContainerStatus
POST /orchestrator/containers/status | Roles: []

```
CALL _backendDetector.CreateOrchestrator(config.DefaultBackend)
CALL _orchestrator.GetContainerStatusAsync(request.AppName)
IF status == Stopped with 0 instances -> RETURN (404, null)
RETURN (200, ContainerStatus { appName, status, instances, labels })
```

### RollbackConfiguration
POST /orchestrator/config/rollback | Roles: []

```
READ config:version                                // Current version number
IF request.TargetVersion specified
  // Validate: targetVersion >= 1 AND < currentVersion
  IF invalid -> RETURN (400, null)
ELSE
  // Default to version N-1

READ config:history:{targetVersion}
IF null -> RETURN (404, null)                      // Version expired from history

// RestoreConfigurationVersionAsync:
// Creates NEW version (currentVersion + 1) containing old config
WRITE config:history:{newVersion} <- copy of target config with new timestamp (TTL: ConfigHistoryTtlDays)
WRITE config:current <- restored config
WRITE config:version <- newVersion
// Original history entry preserved (audit trail)
PUBLISH bannou.configuration-events { configVersion, changedKeys }
RETURN (200, ConfigRollbackResponse { previousVersion, currentVersion, changedKeys })
```

### GetConfigVersion
POST /orchestrator/config/version | Roles: []

```
READ config:version
READ config:current
IF request.IncludeKeyPrefixes
  // Extracts service names and env var prefixes from current config
IF version == 0 -> RETURN (200, ConfigVersionResponse { version: 0 })
READ config:history:{version - 1}                  // Check if previous exists
RETURN (200, ConfigVersionResponse { version, timestamp, hasPreviousConfig, keyCount, keyPrefixes })
```

### NotifyConfigChange
POST /orchestrator/config/notify-change | Roles: []

```
READ config:version                                // Current config version
PUBLISH bannou.configuration-events { configVersion, changedKeys }
RETURN (200, NotifyConfigChangeResponse { configVersion, notifiedAt })
```

### AcquireProcessor
POST /orchestrator/processing-pool/acquire | Roles: []

```
READ pool:known                                    // Validate pool type exists
IF poolType not in known -> RETURN (404, null)

LOCK orchestrator-pool:{poolType} (TTL: PoolLockTimeoutSeconds)
  IF lock fails -> RETURN (409, null)

  // Lazy reclaim: check for expired leases as fast-path
  READ pool:{type}:leases
  FOREACH lease WHERE ExpiresAt < now
    // Move expired processor back to available

  READ pool:{type}:available
  IF empty -> RETURN (503, null)                   // No processors available

  // Pop first available (FIFO)
  // Create lease with Guid-based ID
  WRITE pool:{type}:available <- list minus acquired processor
  WRITE pool:{type}:leases <- leases plus new ProcessorLease { leaseId, processorId, appId, expiresAt }
  WRITE pool:{type}:metrics <- updated metrics

RETURN (200, AcquireProcessorResponse { processorId, appId, leaseId, expiresAt })
```

### ReleaseProcessor
POST /orchestrator/processing-pool/release | Roles: []

```
// Search all known pool types for the lease ID (read-only, outside lock)
READ pool:known
FOREACH poolType in known
  READ pool:{type}:leases
  IF leaseId found -> target pool identified
IF not found -> RETURN (404, null)

LOCK orchestrator-pool:{poolType} (TTL: PoolLockTimeoutSeconds)
  IF lock fails -> RETURN (409, null)

  READ pool:{type}:leases
  // Remove lease, get processor info
  READ pool:{type}:available
  // Append processor back to available list
  WRITE pool:{type}:leases <- updated leases
  WRITE pool:{type}:available <- updated available list
  WRITE pool:{type}:metrics <- updated metrics (job success/failure count)

PUBLISH orchestrator.processor.released { poolType, processorId, success, leaseDurationMs }
RETURN (200, ReleaseProcessorResponse { processorId })
```

### GetPoolStatus
POST /orchestrator/processing-pool/status | Roles: []

```
READ pool:known
IF poolType not in known -> RETURN (404, null)

READ pool:{type}:instances
READ pool:{type}:available
READ pool:{type}:leases
READ pool:{type}:config
IF request.IncludeMetrics
  READ pool:{type}:metrics
RETURN (200, PoolStatusResponse { totalInstances, availableInstances, busyInstances, utilization, recentMetrics })
```

### ScalePool
POST /orchestrator/processing-pool/scale | Roles: []

```
READ pool:{type}:config
IF pool not registered -> RETURN (404, null)
READ pool:{type}:instances

IF targetInstances > currentCount
  CALL _backendDetector.CreateOrchestrator(config.DefaultBackend)
  FOREACH new instance needed
    CALL _orchestrator.DeployServiceAsync(poolType, newAppId, poolEnv)
    // poolEnv: BANNOU_SERVICES_ENABLED=false, specific plugin enabled, BANNOU_APP_ID, ACTOR_POOL_NODE_ID
    // Add to instances list

ELSE IF targetInstances < currentCount
  // Prefer removing available (not leased) instances
  FOREACH excess available instance
    CALL _orchestrator.TeardownServiceAsync(appId, removeVolumes: false)
    // Remove from instances and available lists

WRITE pool:{type}:instances <- updated list
WRITE pool:{type}:metrics <- updated metrics
RETURN (200, ScalePoolResponse { previousInstances, currentInstances, scaledUp, scaledDown })
```

### CleanupPool
POST /orchestrator/processing-pool/cleanup | Roles: []

```
LOCK orchestrator-pool:{poolType} (TTL: PoolLockTimeoutSeconds)
  IF lock fails -> RETURN (409, null)

  READ pool:{type}:leases
  // Reclaim expired leases (same as background timer logic)
  FOREACH expired lease
    // Move processor back to available

  READ pool:{type}:available
  READ pool:{type}:config
  IF request.PreserveMinimum
    // Keep minInstances alive
  FOREACH excess idle instance
    CALL _orchestrator.TeardownServiceAsync(appId)
    // Remove from instances and available lists

  IF request.Force
    // Tear down ALL instances and clear all pool state keys

  WRITE pool:{type}:instances <- updated list
  WRITE pool:{type}:available <- updated list
  WRITE pool:{type}:leases <- updated leases
  WRITE pool:{type}:metrics <- updated metrics

RETURN (200, CleanupPoolResponse { instancesRemoved, currentInstances })
```

---

## Background Services

### FullMappingsTimer (in ServiceHealthMonitor)
**Interval**: `FullMappingsIntervalSeconds` (default 30s)
**Purpose**: Periodically broadcasts complete service-to-appId routing table so lib-mesh instances always have current routing data.

```
READ routings:_index
FOREACH serviceName in index.ServiceNames
  READ routings:{serviceName}
// Exclude L0 infrastructure (state, messaging, mesh)
LOCK config:mappings-version [with ETag retry]
  // Increment mappings version counter
  WRITE config:mappings-version <- version + 1
// DI push via IServiceMappingReceiver (not IMessageBus)
FOREACH receiver in IEnumerable<IServiceMappingReceiver>
  CALL receiver.UpdateMappingsAsync(mappings, defaultAppId, version)
```

### LeaseCleanupTimer (in ServiceHealthMonitor)
**Interval**: `LeaseCleanupIntervalSeconds` (default 60s)
**Purpose**: Returns expired processing pool leases to available list, preventing pool exhaustion when callers abandon leases without releasing.

```
READ pool:known
FOREACH poolType in known
  LOCK orchestrator-pool:{poolType} (TTL: PoolLockTimeoutSeconds)
    READ pool:{type}:leases
    FOREACH lease WHERE ExpiresAt < now
      // Move expired processor back to available list
    WRITE pool:{type}:leases <- updated leases
    WRITE pool:{type}:available <- updated available list
```

---

## Non-Standard Implementation Patterns

#### OnValidatePlugin (control-plane restriction)

```
IF currentAppId != "bannou"
  LOG Error "Orchestrator can only run on the control-plane node"
  RETURN PluginDisabled
```

The orchestrator refuses to load on any node that is not the primary control-plane (`EffectiveAppId == "bannou"`). This prevents accidental deployment to managed worker nodes.

#### OnStartAsync (route initialization)

```
CALL _stateManager.InitializeAsync()
  // Connects to Redis, acquires all 4 stores
  // If fails -> throw InvalidOperationException (hard startup failure)

// Initialize default routes for 20 known routable services
FOREACH serviceName in KnownRoutableServices
  READ routings:{serviceName}
  IF null
    WRITE routings:{serviceName} <- ServiceRouting { appId: EffectiveAppId } (TTL: RoutingTtlSeconds)
    // Update routings index via ETag retry loop
```

`KnownRoutableServices` is a hardcoded array of 20 service names (auth, account, connect, etc.). Services not in this list rely on fallback routing behavior.

#### RegisterServicePermissionsAsync (override)

```
// Explicit IBannouService interface implementation — overrides generated permission registration
IF _configuration.SecureWebsocket
  CALL registry.RegisterServiceAsync(serviceId, version, emptyMatrix)
  // Empty matrix → no endpoints exposed via WebSocket → service-to-service only
ELSE
  CALL base permission registration (standard generated matrix)
```

#### Full Service Mappings via DI Push (not IMessageBus)

The `bannou.full-service-mappings` routing broadcast uses `IEnumerable<IServiceMappingReceiver>` DI push instead of `IMessageBus.PublishAsync`. Per FOUNDATION TENETS (Cross-Service Communication Discipline): Orchestrator (L3) pushes into Mesh (L0) via DI interface. Mesh's implementation updates the local resolver and broadcasts `mesh.mappings.updated` (L0→L0) for cross-node sync.
