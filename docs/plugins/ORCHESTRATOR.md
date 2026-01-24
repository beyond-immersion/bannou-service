# Orchestrator Plugin Deep Dive

> **Plugin**: lib-orchestrator
> **Schema**: schemas/orchestrator-api.yaml
> **Version**: 3.0.0
> **State Stores**: orchestrator-heartbeats (Redis), orchestrator-routings (Redis), orchestrator-config (Redis)

---

## Overview

Central intelligence for Bannou environment management and service orchestration. The Orchestrator manages the full lifecycle of distributed service deployments: deploying preset-based topologies with per-node service enablement, tearing down containers with infrastructure-level control, performing live topology updates (add/remove nodes, move/scale services, update environment), managing processing pools for on-demand worker containers (acquire/release/scale/cleanup), monitoring service health via heartbeat ingestion, maintaining versioned deployment configurations with rollback capability, resolving container orchestration backends (Docker Compose, Docker Swarm, Portainer, Kubernetes), retrieving container logs, cleaning up orphaned resources, listing deployment presets, broadcasting service-to-app-id routing mappings via pub/sub, and invalidating OpenResty routing caches on topology changes. The plugin features a pluggable backend architecture with `IContainerOrchestrator` abstraction, index-based state store patterns (avoiding KEYS/SCAN), ETag-based optimistic concurrency for heartbeat/routing indexes, and a secure mode that makes the orchestrator inaccessible via WebSocket (admin-only service-to-service calls).

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis persistence for heartbeats, routing, configuration versioning, and processing pool state |
| lib-state (`IDistributedLockProvider`) | Pool-level locks for atomic acquire/release operations (15-second TTL) |
| lib-messaging (`IMessageBus`) | Publishing health pings, service mapping broadcasts, deployment events, processor released events, and error events |
| lib-mesh (`IServiceNavigator`) | Cross-service invocation (permission registration) |
| `IContainerOrchestrator` (internal) | Backend abstraction for container lifecycle: deploy, teardown, scale, restart, logs, status |
| `IBackendDetector` (internal) | Detects available container backends (Docker socket, Portainer, Kubernetes) |
| `IOrchestratorStateManager` (internal) | Encapsulates all Redis state operations with index-based patterns |
| `IOrchestratorEventManager` (internal) | Encapsulates event publishing logic (deployment events, health pings) |
| `IServiceHealthMonitor` (internal) | Manages service routing tables and heartbeat-based health status |
| `ISmartRestartManager` (internal) | Evaluates whether services need restart based on configuration changes |
| `PresetLoader` (internal) | Loads deployment preset YAML files from filesystem |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-mesh | Consumes `bannou.full-service-mappings` events for dynamic service routing |
| lib-actor | Uses processing pool acquire/release for NPC brain worker containers |
| All services | Receive routing updates published by orchestrator after topology changes |

---

## State Storage

**Stores**: 3 state stores

| Store | Backend | Purpose |
|-------|---------|---------|
| `orchestrator-heartbeats` | Redis | Service instance heartbeat data with TTL-based expiry |
| `orchestrator-routings` | Redis | Service-to-app-id routing mappings with TTL |
| `orchestrator-config` | Redis | Deployment configuration versions, pool configs, metrics |

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{appId}` (heartbeats store) | `InstanceHealthStatus` | Per-instance health: status, services, capacity, issues |
| `_index` (heartbeats store) | `HeartbeatIndex` | Set of all known app IDs (avoids KEYS/SCAN) |
| `{serviceName}` (routings store) | `ServiceRouting` | Routing entry: appId, host, port, status |
| `_index` (routings store) | `RoutingIndex` | Set of all known service names |
| `version` (config store) | `ConfigVersion` | Current configuration version number |
| `current` (config store) | `DeploymentConfiguration` | Active deployment configuration (no TTL) |
| `history:{version}` (config store) | `DeploymentConfiguration` | Historical config versions (TTL: ConfigHistoryTtlDays) |
| `processing-pool:{poolType}:instances` (config store) | `List<ProcessorInstance>` | All instances in a pool |
| `processing-pool:{poolType}:available` (config store) | `List<ProcessorInstance>` | Available (unleased) instances |
| `processing-pool:{poolType}:leases` (config store) | `Dictionary<string, ProcessorLease>` | Active leases by lease ID |
| `processing-pool:{poolType}:metrics` (config store) | `PoolMetricsData` | Jobs completed/failed counts, avg time |
| `processing-pool:{poolType}:config` (config store) | `PoolConfiguration` | Pool settings: min/max instances, thresholds |
| `orchestrator:pools:known` (config store) | `List<string>` | Registry of known pool types |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `orchestrator.health-ping` | `OrchestratorHealthPingEvent` | Infrastructure health check verifies pub/sub path |
| `bannou.full-service-mappings` | `FullServiceMappingsEvent` | After any routing change (deploy, teardown, topology update, reset) |
| `orchestrator.deployment` | `DeploymentEvent` | Deploy/teardown started, completed, or failed |
| `orchestrator.processor.released` | `ProcessorReleasedEvent` | Processor released back to pool (includes pool type, success/failure, lease duration) |
| `permission.service-registered` | `ServiceRegistrationEvent` | Service startup: registers permissions (or blank in secure mode) |
| (error topic via `TryPublishErrorAsync`) | Error event | Any unexpected internal failure |

### Consumed Events

| Topic | Event Type | Handler |
|-------|-----------|---------|
| `bannou.service-heartbeats` | `ServiceHeartbeatEvent` | `HandleServiceHeartbeat`: Writes heartbeat to state store, updates index |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `SecureWebsocket` | `ORCHESTRATOR_SECURE_WEBSOCKET` | `true` | When true, publishes blank permission registration (no WebSocket access) |
| `CacheTtlMinutes` | `ORCHESTRATOR_CACHE_TTL_MINUTES` | `5` | Cache TTL for orchestrator data |
| `DefaultBackend` | `ORCHESTRATOR_DEFAULT_BACKEND` | `"compose"` | Default container backend: compose, swarm, portainer, kubernetes |
| `HeartbeatTimeoutSeconds` | `ORCHESTRATOR_HEARTBEAT_TIMEOUT_SECONDS` | `90` | Heartbeat staleness threshold |
| `DegradationThresholdMinutes` | `ORCHESTRATOR_DEGRADATION_THRESHOLD_MINUTES` | `5` | Time before marking service degraded |
| `PortainerUrl` | `ORCHESTRATOR_PORTAINER_URL` | (nullable) | Portainer API URL |
| `PortainerApiKey` | `ORCHESTRATOR_PORTAINER_API_KEY` | (nullable) | Portainer API key |
| `PortainerEndpointId` | `ORCHESTRATOR_PORTAINER_ENDPOINT_ID` | `1` | Portainer endpoint ID |
| `DockerHost` | `ORCHESTRATOR_DOCKER_HOST` | `"unix:///var/run/docker.sock"` | Docker socket path |
| `DockerNetwork` | `ORCHESTRATOR_DOCKER_NETWORK` | `"bannou_default"` | Docker network for deployed containers |
| `CertificatesHostPath` | `ORCHESTRATOR_CERTIFICATES_HOST_PATH` | `"/app/provisioning/certificates"` | TLS certificates host path |
| `PresetsHostPath` | `ORCHESTRATOR_PRESETS_HOST_PATH` | `"/app/provisioning/orchestrator/presets"` | Deployment presets directory |
| `LogsVolumeName` | `ORCHESTRATOR_LOGS_VOLUME` | `"logs-data"` | Docker volume for logs |
| `OpenRestyHost` | `ORCHESTRATOR_OPENRESTY_HOST` | `"openresty"` | OpenResty hostname for cache invalidation |
| `OpenRestyPort` | `ORCHESTRATOR_OPENRESTY_PORT` | `80` | OpenResty port |
| `OpenRestyRequestTimeoutSeconds` | `ORCHESTRATOR_OPENRESTY_REQUEST_TIMEOUT_SECONDS` | `5` | OpenResty request timeout |
| `KubernetesNamespace` | `ORCHESTRATOR_KUBERNETES_NAMESPACE` | `"default"` | Kubernetes namespace |
| `KubeconfigPath` | `ORCHESTRATOR_KUBECONFIG_PATH` | (nullable) | Kubeconfig file path |
| `HeartbeatTtlSeconds` | `ORCHESTRATOR_HEARTBEAT_TTL_SECONDS` | `90` | Heartbeat state TTL |
| `RoutingTtlSeconds` | `ORCHESTRATOR_ROUTING_TTL_SECONDS` | `300` | Routing entry TTL (5 min) |
| `ConfigHistoryTtlDays` | `ORCHESTRATOR_CONFIG_HISTORY_TTL_DAYS` | `30` | Config version history retention |
| `ContainerStatusPollIntervalSeconds` | `ORCHESTRATOR_CONTAINER_STATUS_POLL_INTERVAL_SECONDS` | `2` | Deploy readiness poll interval |
| `RedisConnectionString` | `ORCHESTRATOR_REDIS_CONNECTION_STRING` | `"bannou-redis:6379"` | Redis connection string |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<OrchestratorService>` | Scoped | Structured logging |
| `OrchestratorServiceConfiguration` | Singleton | All 23 config properties |
| `IStateStoreFactory` | Singleton | Redis state store access (via IOrchestratorStateManager) |
| `IDistributedLockProvider` | Singleton | Pool-level distributed locks (`orchestrator-pool`, 15s TTL) |
| `IMessageBus` | Scoped | Event publishing |
| `IServiceNavigator` | Scoped | Cross-service communication |
| `IOrchestratorStateManager` | Singleton | State operations: heartbeats, routings, config versions, pool data |
| `IOrchestratorEventManager` | Singleton | Deployment/health event publishing |
| `IServiceHealthMonitor` | Singleton | Routing table management, heartbeat evaluation |
| `ISmartRestartManager` | Singleton | Configuration-based restart determination |
| `IContainerOrchestrator` | (resolved at runtime) | Backend-specific container operations |
| `IBackendDetector` | Singleton | Backend availability detection |
| `PresetLoader` | Singleton | Filesystem preset YAML loading |

Service lifetime is **Scoped** (per-request). Internal helpers are Singleton.

---

## API Endpoints (Implementation Notes)

### Deployment & Lifecycle (4 endpoints)

- **Deploy** (`/orchestrator/deploy`): Resolves orchestrator backend. If request is a "reset to default" (preset=default/bannou/empty, no topology), delegates to `ResetToDefaultTopologyAsync` which tears down tracked containers and resets all mappings to "bannou". Otherwise loads preset YAML via `PresetLoader`, builds per-node environment (SERVICES_ENABLED=false + individual enable flags), filters environment variables via whitelist (`IsAllowedEnvironmentVariable`), deploys containers via `DeployServiceAsync`, sets routing via `ServiceHealthMonitor`, initializes processing pool configurations if preset includes them, saves versioned deployment configuration, invalidates OpenResty cache, and publishes `DeploymentEvent`. Returns `DeployResponse` with success status, deployed services list, warnings, and duration.

- **Teardown** (`/orchestrator/teardown`): Supports dry-run mode (returns preview of what would be torn down). Lists containers via orchestrator, identifies services to tear down, optionally includes infrastructure services. Iterates services: tears down container, restores routing to default via `ServiceHealthMonitor`. If `includeInfrastructure=true`, also tears down infrastructure containers (redis, rabbitmq, etc.). Publishes `DeploymentEvent` with completed/failed action. Returns `TeardownResponse` with stopped containers, removed volumes, removed infrastructure.

- **GetStatus** (`/orchestrator/status`): Gets orchestrator backend, lists all containers, gets service heartbeats from state manager, gets routing mappings, derives environment status (running/degraded/stopped) from container health. Returns `EnvironmentStatus` with services, heartbeats, current deployment config, and topology metadata.

- **Clean** (`/orchestrator/clean`): Accepts target types (containers, networks, volumes, images, all). For containers: lists stopped containers, checks `IsOrphanedContainer` (must have `bannou.orchestrator-managed` label, be stopped for 24+ hours). Tears down orphaned containers. Network/volume/image pruning is logged as unsupported (not yet implemented in IContainerOrchestrator). Returns `CleanResponse` with counts and reclaimed space.

### Service & Container Management (4 endpoints)

- **RestartService** (`/orchestrator/services/restart`): Uses `SmartRestartManager` to determine restart parameters. Delegates to orchestrator backend `RestartContainerAsync`. Returns restart confirmation with reason and timing.

- **ShouldRestartService** (`/orchestrator/services/should-restart`): Evaluates if a service needs restart based on configuration changes. Uses `SmartRestartManager` logic to compare current vs desired state. Returns boolean recommendation with reason.

- **RequestContainerRestart** (`/orchestrator/containers/request-restart`): Accepts app name and priority/reason. Builds `ContainerRestartRequest`, delegates to backend `RestartContainerAsync`. Returns `ContainerRestartResponse` with accepted status.

- **GetContainerStatus** (`/orchestrator/containers/status`): Gets container status via backend `GetContainerStatusAsync`. Returns 404 if container has Stopped status with 0 instances. Returns `ContainerStatus` with status enum, instances, labels, timestamp.

### Health Monitoring (2 endpoints)

- **GetInfrastructureHealth** (`/orchestrator/health/infrastructure`): Checks health of infrastructure components. Uses state manager `CheckHealthAsync` for Redis connectivity (measures operation time). Publishes `OrchestratorHealthPingEvent` to verify pub/sub path. Returns health status for each component (redis, rabbitmq, docker).

- **GetServiceHealth** (`/orchestrator/health/services`): Retrieves all service heartbeats from state manager. Evaluates each heartbeat against `HeartbeatTimeoutSeconds` and `DegradationThresholdMinutes` thresholds. Returns list of `DeployedService` with status (Running/Degraded/Stopped/Unhealthy), last seen time, and capacity info.

### Configuration Versioning (2 endpoints)

- **RollbackConfiguration** (`/orchestrator/config/rollback`): Gets current version, validates version > 1 (needs previous). Retrieves previous config from history. Calls `RestoreConfigurationVersionAsync` which creates a NEW version (currentVersion+1) containing the old config (preserves audit trail). Computes changed keys (services and env vars that differ). Returns `ConfigRollbackResponse` with version numbers and changed keys list.

- **GetConfigVersion** (`/orchestrator/config/version`): Gets current version number and configuration from Redis. Checks if previous version exists in history. Extracts key prefixes from current config (service names, env var prefixes). Returns `ConfigVersionResponse` with version, timestamp, hasPreviousConfig, keyCount, keyPrefixes.

### Topology Management (1 endpoint)

- **UpdateTopology** (`/orchestrator/topology`): Accepts list of `TopologyChange` with actions. Iterates changes, applies each:
  - `AddNode`: Deploys services to new node with `bannou-{service}-{nodeName}` app-id. Sets SERVICES_ENABLED=false plus per-service enable flags. Updates routing.
  - `RemoveNode`: Tears down all services for a node. Restores routing to default.
  - `MoveService`: Updates routing only (no container changes). Points service to new node app-id.
  - `Scale`: Calls `ScaleServiceAsync` on orchestrator backend with target replicas.
  - `UpdateEnv`: Redeploys services with new environment variables (DeployServiceAsync handles cleanup+recreation).
  Reports partial success: per-change success/error tracking. Returns `TopologyUpdateResponse` with applied changes and warnings.

### Processing Pool Management (4 endpoints)

- **AcquireProcessor** (`/orchestrator/processing-pool/acquire`): Validates pool type. Acquires `orchestrator-pool` distributed lock on `{poolType}` (15s TTL) to prevent concurrent read-modify-write races. Gets available processor list from Redis. Pops first available, creates lease (Guid-based) with timeout (default 300s). Stores lease in hash. Returns 503 if no processors available. Returns processor ID, app-id, lease ID, expiry.

- **ReleaseProcessor** (`/orchestrator/processing-pool/release`): Searches all known pool types for the lease ID (read-only, outside lock). Once target pool is identified, acquires `orchestrator-pool` distributed lock on `{poolType}` (15s TTL). Removes lease from hash, returns processor to available list. Updates pool metrics (job success/failure count). Publishes `orchestrator.processor.released` event with pool type, processor ID, success/failure, and lease duration. Returns release confirmation.

- **GetPoolStatus** (`/orchestrator/processing-pool/status`): Reads instance list, available list, leases hash, and config for pool type. Computes total/available/busy instance counts. If `includeMetrics=true`, reads metrics data (jobs completed/failed 1h, avg processing time, last scale event). Returns `PoolStatusResponse`.

- **ScalePool** (`/orchestrator/processing-pool/scale`): Loads pool config from Redis. If scaling up: deploys new worker containers via orchestrator backend with pool-specific environment (SERVICES_ENABLED=false, specific plugin enabled, BANNOU_APP_ID, ACTOR_POOL_NODE_ID). If scaling down: prefers removing available instances first; with `force=true` also removes busy instances. Tears down containers, cleans leases. Updates instance/available lists and metrics.

### Pool Cleanup (1 endpoint)

- **CleanupPool** (`/orchestrator/processing-pool/cleanup`): Gets available instances and pool config. If `preserveMinimum=true`, keeps minInstances alive. Removes excess idle instances from both instance and available lists. Does NOT tear down containers (only cleans state). Returns removed count.

### Discovery & Routing (3 endpoints)

- **GetServiceRouting** (`/orchestrator/service-routing`): Gets all service routing mappings from state manager. Returns dictionary of service name to app-id with host/port info.

- **ListPresets** (`/orchestrator/presets/list`): Uses `PresetLoader` to scan presets directory. Reads YAML files, extracts name, services list, description. Returns list of available deployment presets.

- **ListBackends** (`/orchestrator/backends/list`): Uses `BackendDetector` to check availability of each backend (Docker socket, Portainer API, Kubernetes config). Returns detected backends with availability status and connection info.

### Logs (1 endpoint)

- **GetLogs** (`/orchestrator/logs`): Resolves target from service name or container name (falls back to "bannou"). Calls `GetContainerLogsAsync` with tail count and optional since timestamp. Parses log text into `LogEntry` objects. Note: timestamps are currently set to UtcNow rather than parsed from log lines (see Known Quirks).

---

## Visual Aid

### Deployment Lifecycle

```
                        Deploy Request
                             |
                    +--------v--------+
                    | Is Reset to     |
                    | Default?        |
                    +--------+--------+
                      yes /    \ no
                         /      \
            +-----------v-+   +-v-----------+
            | Tear down   |   | Load Preset |
            | tracked     |   | YAML        |
            | containers  |   +------+------+
            | Reset all   |          |
            | mappings    |   +------v------+
            +------+------+   | Per-Node:   |
                   |          | - Build env |
                   |          | - Deploy    |
                   |          | - Set route |
                   |          +------+------+
                   |                 |
            +------v-----------------v------+
            | Invalidate OpenResty Cache    |
            +------+------------------------+
                   |
            +------v------+
            | Save Config |
            | Version N+1 |
            +------+------+
                   |
            +------v-----------------+
            | Publish Deployment     |
            | Event + Mappings Event |
            +------------------------+
```

### Processing Pool Management

```
    +---------+        +---------+       +---------+
    | Pending |------->|Available|<----->|  Leased |
    +---------+        +---------+       +---------+
         ^                  |                 |
         |                  | (cleanup)       | (release)
    (deploy)                v                 v
         |            +-----------+     +-----------+
    +----+----+       | Removed   |     | Metrics   |
    | Orch.   |       | (state    |     | Updated   |
    | Deploy  |       |  only)    |     +-----------+
    +---------+       +-----------+

    Scale Up:  Deploy new containers --> Pending --> Available (after self-reg)
    Scale Down: Available --> Teardown container --> Remove from state
    Acquire:    Lock(pool) --> Available.pop() --> Create Lease --> Return processor info
    Release:    Find pool --> Lock(pool) --> Remove Lease --> Available.push() --> Update metrics --> Publish event
    Cleanup:    Available.excess(minInstances) --> Remove from state
```

### Backend Abstraction

```
    +-------------------+
    | OrchestratorService|
    +---------+---------+
              |
              | GetOrchestratorAsync()
              |
    +---------v---------+
    | IBackendDetector   |
    | (detect available) |
    +---------+---------+
              |
    +---------v--------------------------+
    |        IContainerOrchestrator      |
    +----+-------+--------+------+------+
         |       |        |      |
    +----v--+ +--v---+ +--v--+ +-v--------+
    |Compose| |Swarm | |K8s  | |Portainer |
    |(impl) | |(stub)| |(stub)| |(stub)    |
    +-------+ +------+ +-----+ +----------+

    IContainerOrchestrator interface:
    - DeployServiceAsync(service, appId, env)
    - TeardownServiceAsync(appId, removeVolumes)
    - ScaleServiceAsync(appId, replicas)
    - RestartContainerAsync(appId, request)
    - GetContainerStatusAsync(appId)
    - GetContainerLogsAsync(appId, tail, since)
    - ListContainersAsync()
```

### Configuration Versioning

```
    Redis Config Store:
    +------------------+--------------------+
    | Key              | Value              |
    +------------------+--------------------+
    | version          | { Version: 5 }     |
    | current          | DeploymentConfig   |
    | history:1        | Config v1 (TTL 30d)|
    | history:2        | Config v2 (TTL 30d)|
    | history:3        | Config v3 (TTL 30d)|
    | history:4        | Config v4 (TTL 30d)|
    | history:5        | Config v5 (TTL 30d)|
    +------------------+--------------------+

    Rollback (v5 -> v3):
    1. Read history:3
    2. Copy as v6 with new timestamp
    3. Save history:6
    4. Update current = v6
    5. Update version = 6
    (Original history:3 preserved)
```

### Health Monitoring

```
    +------------------+         +------------------+
    | Service Instance |         | Service Instance |
    | (bannou-auth)    |         | (bannou-actor-1) |
    +--------+---------+         +--------+---------+
             |                            |
             | ServiceHeartbeatEvent      |
             v                            v
    +--------+----------------------------+--------+
    |          bannou.service-heartbeats            |
    +--------+-------------------------------------+
             |
    +--------v---------+
    | HandleServiceHB  |
    | (Events partial) |
    +--------+---------+
             |
    +--------v--------------------+
    | OrchestratorStateManager    |
    | - Write heartbeat (TTL 90s)|
    | - Update index (ETag CAS)  |
    +-----------------------------+
             |
    +--------v---------+
    | Health Endpoints: |
    | - GetServiceHealth|
    |   reads index,    |
    |   bulk-gets HBs,  |
    |   evaluates status|
    +------------------+

    Status Logic:
    - LastSeen < HeartbeatTimeoutSeconds:  Healthy
    - LastSeen < DegradationThresholdMin:  Degraded
    - LastSeen > DegradationThresholdMin:  Stopped
    - Issues non-empty:                    Unhealthy
```

---

## Stubs & Unimplemented Features

| Feature | Status | Notes |
|---------|--------|-------|
| Docker Swarm backend (`DockerSwarmOrchestrator`) | Stub | All methods throw `NotImplementedException` |
| Kubernetes backend (`KubernetesOrchestrator`) | Stub | All methods throw `NotImplementedException` |
| Portainer backend (`PortainerOrchestrator`) | Stub | All methods throw `NotImplementedException` |
| Network pruning (Clean) | Logged warning | "Network pruning requested but not yet implemented" |
| Volume pruning (Clean) | Logged warning | "Volume pruning requested but not yet implemented" |
| Image pruning (Clean) | Logged warning | "Image pruning requested but not yet implemented" |
| Queue depth tracking (pool status) | Hardcoded 0 | Comment: "We don't have a queue yet" |
| Auto-scaling (pool) | No trigger | Thresholds are stored but no background job evaluates them |
| Idle timeout cleanup (pool) | No trigger | `IdleTimeoutMinutes` stored but no background timer |
| Processing pool container teardown on cleanup | State-only | `CleanupPoolAsync` removes from state lists but does not call `TeardownServiceAsync` |
| Log timestamp parsing | Hardcoded UtcNow | Log entries use current time, not parsed from log line |

---

## Potential Extensions

- **Implement remaining backends**: Docker Swarm, Kubernetes, and Portainer are all defined in the interface but stub-only. Kubernetes is the natural production target.
- **Auto-scaling background service**: A timer-based service that evaluates pool utilization against `ScaleUpThreshold`/`ScaleDownThreshold` and automatically scales pools.
- **Idle timeout enforcement**: Background cleanup for pool workers that have been available beyond `IdleTimeoutMinutes`.
- **Container teardown in CleanupPool**: Currently only removes state entries; should also call `TeardownServiceAsync` to actually stop containers.
- **Multi-version rollback**: Currently only rolls back to version N-1. Could support rollback to arbitrary historical versions.
- **Deploy validation**: Pre-flight checks before deployment (disk space, network reachability, image pull verification).
- **Blue-green deployment**: Deploy new topology alongside old, switch routing atomically, then teardown old.
- **Canary deployments**: Route percentage of traffic to new version, monitor health, then promote or rollback.
- **Network/volume/image pruning**: Extend `IContainerOrchestrator` with prune methods for Docker system cleanup.
- **Log timestamp parsing**: Parse actual timestamps from Docker log output format.
- **Processing pool priority queue**: Currently FIFO; could use priority field from acquire requests.
- **Lease expiry enforcement**: Background timer to reclaim expired leases and return processors to available pool.

---

## Known Quirks & Caveats

### Bugs

None identified.

### Intentional Quirks

- **Secure WebSocket mode (default)**: When `SecureWebsocket=true`, the orchestrator publishes a blank permission registration with no endpoints. This makes it completely inaccessible via WebSocket - only service-to-service calls work. This is intentional: orchestrator operations are admin-only and should not be exposed to game clients.
- **Environment variable whitelist filtering**: `IsAllowedEnvironmentVariable` uses `PluginLoader.ValidEnvironmentPrefixes` as a whitelist. Only variables with recognized service prefixes are forwarded to deployed containers. `*_SERVICE_ENABLED` flags and `BANNOU_APP_ID` are always excluded - deployment logic controls these explicitly.
- **Orphaned container identification**: Uses the `bannou.orchestrator-managed` Docker label (set during deploy) rather than name parsing. Only containers with this label AND stopped for 24+ hours are considered orphaned. Conservative threshold avoids removing containers in active restart cycles.
- **Rollback creates new version**: Rolling back from v5 to v3 creates v6 (a copy of v3 with new timestamp). The history trail is never overwritten. This is intentional for audit trail purposes.
- **Reset to default resets mappings BEFORE teardown**: When resetting topology, routing mappings are updated to point to "bannou" FIRST, before tearing down the old containers. This ensures proxies get updated routes before old backends become unavailable - prevents request failures during the transition window.
- **Config clear saves empty config as new version**: `ClearCurrentConfigurationAsync` does not delete the config entry but saves an empty `DeploymentConfiguration` with preset="default". This maintains the version history audit trail.
- **Pool metrics window reset is lazy**: `JobsCompleted1h` and `JobsFailed1h` counters reset when the first operation occurs after the 1-hour window has elapsed (tracked via `WindowStart`). There is no background timer - the reset happens inline during `UpdatePoolMetricsAsync`.
- **Expired lease reclamation is lazy**: When a processor lease expires (`ExpiresAt` passes), it is reclaimed during the next `AcquireProcessorAsync` call via `ReclaimExpiredLeasesAsync`. There is no background timer proactively scanning for expired leases. Pools with no acquire traffic will not reclaim expired processors until the next request arrives.

### Design Considerations

- **Index-based state pattern**: The orchestrator avoids Redis KEYS/SCAN commands entirely. Instead, it maintains explicit index entries (`_index` keys) that track known app IDs and service names. These indexes use ETag-based optimistic concurrency (retry loops with `TrySaveAsync`) for safe concurrent updates from multiple containers. Expired entries (TTL'd heartbeats/routings) are cleaned from indexes lazily during read operations.
- **OpenResty cache invalidation is non-blocking**: The `InvalidateOpenRestryRoutingCacheAsync` method catches all exceptions and logs at debug/warning level. If OpenResty is unavailable (common in local dev without Docker), the deployment proceeds normally. Cache will eventually expire based on OpenResty's own TTL.
- **Processing pools are state-tracked, not containerized abstractions**: Pool instances are tracked purely in Redis state. The orchestrator deploys actual containers for workers but does not wrap them in any higher-level abstraction. Workers self-register via heartbeat events after starting.
- **Backend detection at request time**: `GetOrchestratorAsync` is called per-request, not at startup. This allows the backend to change dynamically (e.g., Docker socket becomes available after orchestrator starts).
- **Partial failure reporting in topology changes**: Each topology change is applied independently. If 3 of 5 changes succeed, the response reports partial success with per-change error details. The already-applied changes are NOT rolled back.
- **Service enable/disable flags are generated from preset services list**: When deploying, the orchestrator does not inherit `*_SERVICE_ENABLED` vars from its own environment. Instead, it sets `SERVICES_ENABLED=false` and explicitly enables only the services listed in the preset for each node.

- **GetOrchestratorAsync TTL check is ineffective for scoped service**: Since `OrchestratorService` is scoped (per-request), the `_orchestrator` field starts null every request and is populated on first access within the request. The TTL-based cache invalidation logic (lines 174-203) checks if cache age exceeds `CacheTtlMinutes` (default 5 min), but since the service instance lives only for the duration of one request (seconds at most), this check can never trigger. The caching only provides within-request reuse when multiple endpoint methods call `GetOrchestratorAsync`.

- **HttpClient created per-call in OpenResty invalidation**: `InvalidateOpenRestryRoutingCacheAsync` creates `new HttpClient()` with `using` on every invocation. This is the classic socket exhaustion anti-pattern in .NET - each disposal closes the socket immediately, but DNS entries remain cached, and under high deployment frequency could exhaust ephemeral ports. Should use `IHttpClientFactory`.

---

## Tenet Violations

### QUALITY TENETS

#### Logging Standards (T10)

| File | Line | Description |
|------|------|-------------|
| `OrchestratorService.cs` | 226 | `LogInformation("Executing GetInfrastructureHealth operation")` - Operation entry should be `LogDebug` per T10 |
| `OrchestratorService.cs` | 328 | `LogInformation("Executing GetServicesHealth operation")` - Operation entry should be `LogDebug` |
| `OrchestratorService.cs` | 385 | `LogInformation("Executing ShouldRestartService operation...")` - Operation entry should be `LogDebug` |
| `OrchestratorService.cs` | 406 | `LogInformation("Executing GetBackends operation")` - Operation entry should be `LogDebug` |
| `OrchestratorService.cs` | 427 | `LogInformation("Executing GetPresets operation")` - Operation entry should be `LogDebug` |
| `OrchestratorService.cs` | 1074 | `LogInformation("Executing GetServiceRouting operation")` - Operation entry should be `LogDebug` |
| `OrchestratorService.cs` | 1136 | `LogInformation("Executing GetStatus operation")` - Operation entry should be `LogDebug` |
| `OrchestratorService.cs` | 2115 | `LogInformation("Executing GetContainerStatus operation...")` - Operation entry should be `LogDebug` |
| `OrchestratorService.cs` | 2144 | `LogInformation("Executing RollbackConfiguration operation...")` - Operation entry should be `LogDebug` |
| `OrchestratorService.cs` | 2233 | `LogInformation("Executing GetConfigVersion operation")` - Operation entry should be `LogDebug` |
| `OrchestratorService.cs` | 767 | `"[DryRun] Would deploy node {NodeName}..."` - Bracket tag prefix `[DryRun]` violates T10 format rules. Use structured template without brackets. |
| `OrchestratorService.cs` | 1025 | `"[DryRun] Would deploy {deployedServices.Count} node(s)"` - Bracket tag prefix `[DryRun]` violates T10. |
| `SmartRestartManager.cs` | 43 | `LogInformation("Initializing Docker client with host: {DockerHost}")` - Initialization detail should be `LogDebug` |
| `SmartRestartManager.cs` | 48 | `LogInformation("Docker client initialized successfully")` - Initialization confirmation should be `LogDebug` |
| `SmartRestartManager.cs` | 92-93 | `LogInformation("Restarting service: {ServiceName}...")` - Operation entry should be `LogDebug` |
| `SmartRestartManager.cs` | 202-203 | `LogInformation("Waiting for {ServiceName} to become healthy...")` - Polling state should be `LogDebug` |
| `BackendDetector.cs` | 47 | `LogInformation("Detecting available container orchestration backends...")` - Operation entry should be `LogDebug` |
| `DockerComposeOrchestrator.cs` | 87-89 | `LogInformation("DockerComposeOrchestrator configured...")` - Constructor logging should be `LogDebug` |
| `DockerComposeOrchestrator.cs` | 1022 | `LogInformation("Listing infrastructure services...")` - Operation entry should be `LogDebug` |
| `PortainerOrchestrator.cs` | 549 | `LogInformation("Listing infrastructure services (Portainer mode)")` - Operation entry should be `LogDebug` |

**What's wrong**: T10 requires operation entry to be at `Debug` level, not `Information`. Information is reserved for significant state changes (business decisions). Bracket tag prefixes like `[DryRun]` are explicitly forbidden.

**Fix**: Change `LogInformation` to `LogDebug` for operation entry points. Remove bracket prefixes and use structured message templates (e.g., `"DryRun mode: would deploy node {NodeName}..."`).

---

#### XML Documentation (T19)

| File | Line | Description |
|------|------|-------------|
| `DockerComposeOrchestrator.cs` | 1064 | `public void Dispose()` - Missing XML documentation on public method |
| `BackendDetector.cs` | 340-346 | `internal static class LoggerExtensions` / `CreateLogger<T>` - Missing XML documentation on internal members (acceptable for internal, but public members of internal class still benefit from docs) |

**What's wrong**: T19 requires XML documentation on all public members.

**Fix**: Add `<summary>` tags to the `Dispose()` method.

---

### IMPLEMENTATION TENETS

#### JSON Serialization (T20)

| File | Line | Description |
|------|------|-------------|
| `PortainerOrchestrator.cs` | 42-46 | `private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true }` - Direct `System.Text.Json.JsonSerializerOptions` usage |

**What's wrong**: T20 mandates all JSON serialization use `BannouJson`. The comment explains Portainer API uses camelCase which differs from `BannouJson.Options`, but T20 explicitly states "even with custom options!" is forbidden.

**Fix**: Either extend `BannouJson` to support alternate serialization profiles (e.g., `BannouJson.ExternalApiOptions`) or document this as a T20 exception for external API communication (similar to the null-coalesce external service pattern). The comment at line 40 explains the reasoning but doesn't constitute a formal tenet exception.

---

#### Multi-Instance Safety (T9)

| File | Line | Description |
|------|------|-------------|
| `OrchestratorService.cs` | 50 | `private DeploymentConfiguration? _lastKnownDeployment` - In-memory authoritative state that can diverge across instances |
| `OrchestratorService.cs` | 39-44 | `private IContainerOrchestrator? _orchestrator` / `_orchestratorCachedAt` - In-memory cache fields on scoped service (see Design Considerations note - these are harmless per-request caches but the pattern is misleading) |
| `DockerComposeOrchestrator.cs` | 64-68 | `_discoveredNetwork`, `_networkDiscoveryAttempted`, `_discoveredInfrastructureHosts`, `_infrastructureDiscoveryAttempted` - In-memory caches on non-scoped orchestrator instances that can become stale |

**What's wrong**: T9 forbids in-memory authoritative state. `_lastKnownDeployment` is mutated on line 1011 and read on line 1116 as the authoritative deployment ID source. If multiple orchestrator instances exist, they would have divergent views. The DockerComposeOrchestrator caches are acceptable for single-instance backends but could cause issues if the orchestrator is horizontally scaled.

**Fix**: For `_lastKnownDeployment`, always read from `_stateManager.GetCurrentConfigurationAsync()` instead of caching in-memory. For DockerComposeOrchestrator, the caches are acceptable given the backend is inherently single-node, but add a comment documenting this assumption.

---

#### Configuration-First (T21)

| File | Line | Description |
|------|------|-------------|
| `DockerComposeOrchestrator.cs` | 677 | `var imageName = "bannou:latest"` - Hardcoded Docker image name. Should be configurable for custom builds/registries. |
| `DockerSwarmOrchestrator.cs` | 369 | `var imageName = "bannou:latest"` - Same hardcoded Docker image name. |
| `SmartRestartManager.cs` | 20 | `private const int DEFAULT_RESTART_TIMEOUT_SECONDS = 120` - Hardcoded tunable. Should come from configuration. |
| `SmartRestartManager.cs` | 21 | `private const int HEALTH_CHECK_INTERVAL_MS = 2000` - Hardcoded tunable. Should be configurable. |
| `SmartRestartManager.cs` | 119 | `WaitBeforeKillSeconds = 30` - Hardcoded timeout tunable. |
| `DockerComposeOrchestrator.cs` | 82-85 | Fallback values `"bannou_default"`, `"/app/provisioning/certificates"`, etc. using `??` on configuration values - These are acceptable defaults but ideally all defaults are schema-defined. |

**What's wrong**: T21 states hardcoded tunables are forbidden - numeric literals representing limits, timeouts, or thresholds need configuration properties. Docker image names are also tunables that should be configurable to support different registries or image tags.

**Fix**: Add configuration properties for `DockerImageName` (default: `"bannou:latest"`), `RestartTimeoutSeconds` (default: 120), `HealthCheckIntervalMs` (default: 2000), and `WaitBeforeKillSeconds` (default: 30) to the orchestrator configuration schema.

---

#### Async Method Pattern (T23)

| File | Line | Description |
|------|------|-------------|
| `OrchestratorService.cs` | 2037 | `_ = PublishErrorEventAsync(...)` - Fire-and-forget discards the Task without awaiting. The `_` discard means exceptions are silently lost and the async operation may not complete. |
| `SmartRestartManager.cs` | 56 | `await Task.CompletedTask` placed AFTER a try-catch block (line 40-54) that contains only synchronous code. The await is unreachable if an exception is thrown and re-thrown. The method should either be made properly async or restructured. |

**What's wrong**: T23 requires all Task-returning methods to use async/await properly. Discarding a Task with `_ =` means the operation is fire-and-forget, losing any exceptions. The `SmartRestartManager.InitializeAsync` has `await Task.CompletedTask` after a `throw` in the catch block, making it dead code in the error path.

**Fix**: For line 2037, change to `await PublishErrorEventAsync(...)`. For SmartRestartManager.InitializeAsync, remove the trailing `await Task.CompletedTask` since the method already throws on failure and has no actual async work (the Docker client creation is synchronous). If the interface requires `Task`, keep `async` keyword and place `await Task.CompletedTask` inside the try block after sync work.

---

#### Using Statement Pattern (T24)

| File | Line | Description |
|------|------|-------------|
| `DockerComposeOrchestrator.cs` | 636-637 | `var stdout = new MemoryStream(); var stderr = new MemoryStream();` - MemoryStream instances created without `using` statements. While MemoryStream.Dispose() is a no-op for unmanaged resources, the pattern violates T24's requirement to use `using` for all disposables. |

**What's wrong**: T24 requires `using` statements for all IDisposable objects in method scope. MemoryStream implements IDisposable and should be wrapped with `using`.

**Fix**: Change to `using var stdout = new MemoryStream(); using var stderr = new MemoryStream();`. Note: the `stdout` stream is used as a parameter to `new StreamReader(stdout)` which itself uses `using`, but the StreamReader does not own the stream in all code paths. Both should have explicit `using`.

---

### FOUNDATION TENETS

#### Error Handling (T7)

| File | Line | Description |
|------|------|-------------|
| `DockerSwarmOrchestrator.cs` | 350 | `catch` - Bare catch without exception variable. Swallows all exceptions silently with no logging. |
| `KubernetesOrchestrator.cs` | 57 | `catch` - Bare catch without exception variable in constructor. Falls back to kubeconfig without logging what failed. |

**What's wrong**: T7 requires proper error handling with exception context. Bare `catch` blocks swallow exceptions without any logging or context, making debugging impossible.

**Fix**: Change to `catch (Exception ex)` and add appropriate debug logging: `_logger.LogDebug(ex, "In-cluster config not available, falling back to kubeconfig")` for KubernetesOrchestrator, and `_logger.LogDebug(ex, "Failed to list service tasks for {ServiceId}", serviceId)` for DockerSwarmOrchestrator.

---

*This document describes the orchestrator plugin as implemented. For architectural context, see [TENETS.md](../reference/TENETS.md) and [BANNOU_DESIGN.md](../BANNOU_DESIGN.md).*
