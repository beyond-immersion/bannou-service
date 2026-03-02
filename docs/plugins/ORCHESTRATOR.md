# Orchestrator Plugin Deep Dive

> **Plugin**: lib-orchestrator
> **Schema**: schemas/orchestrator-api.yaml
> **Version**: 3.0.0
> **Layer**: AppFeatures
> **State Stores**: orchestrator-heartbeats (Redis), orchestrator-routings (Redis), orchestrator-config (Redis), orchestrator-statestore (Redis)

---

## Overview

Central intelligence (L3 AppFeatures) for Bannou environment management and service orchestration. Manages distributed service deployments including preset-based topologies, live topology updates, processing pools for on-demand worker containers (used by lib-actor for NPC brains), service health monitoring via heartbeats, versioned deployment configurations with rollback, and service-to-app-id routing broadcasts consumed by lib-mesh. Features a pluggable backend architecture supporting Docker Compose, Docker Swarm, Portainer, and Kubernetes. Operates in a secure mode making it inaccessible via WebSocket (admin-only service-to-service calls).

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis persistence for heartbeats, routing, configuration versioning, and processing pool state (via `IOrchestratorStateManager`) |
| lib-state (`IDistributedLockProvider`) | Pool-level locks for atomic acquire/release operations (15-second TTL); mappings version increment lock |
| lib-messaging (`IMessageBus`) | Publishing health pings, service mapping broadcasts, deployment events, processor released events, error events, and permission registration |
| lib-messaging (`IEventConsumer`) | Registering event handlers (heartbeat consumer) |
| `IHttpClientFactory` (Microsoft.Extensions.Http) | HTTP client for OpenResty cache invalidation requests |
| `IContainerOrchestrator` (internal) | Backend abstraction for container lifecycle: deploy, teardown, scale, restart, logs, status |
| `IBackendDetector` (internal) | Detects available container backends (Docker socket, Portainer, Kubernetes) |
| `IOrchestratorStateManager` (internal) | Encapsulates all Redis state operations with index-based patterns |
| `IOrchestratorEventManager` (internal) | Encapsulates event publishing logic (deployment events, health pings) |
| `IServiceHealthMonitor` (internal) | Manages service routing tables and heartbeat-based health status; consumes `IControlPlaneServiceProvider` for control plane introspection |
| `ISmartRestartManager` (internal) | Evaluates whether services need restart based on configuration changes |
| `ITelemetryProvider` (lib-telemetry) | Span instrumentation for all async methods (StartActivity) |
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

**Stores**: 4 state stores

| Store | Backend | Purpose |
|-------|---------|---------|
| `orchestrator-heartbeats` | Redis | Service instance heartbeat data with TTL-based expiry |
| `orchestrator-routings` | Redis | Service-to-app-id routing mappings with TTL |
| `orchestrator-config` | Redis | Deployment configuration versions and history |
| `orchestrator-statestore` | Redis | Processing pool state: instances, leases, configs, metrics |

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{appId}` (heartbeats store) | `InstanceHealthStatus` | Per-instance health: status, services, capacity, issues |
| `_index` (heartbeats store) | `HeartbeatIndex` | Set of all known app IDs (avoids KEYS/SCAN) |
| `{serviceName}` (routings store) | `ServiceRouting` | Routing entry: appId, host, port, status |
| `_index` (routings store) | `RoutingIndex` | Set of all known service names |
| `version` (config store) | `ConfigVersion` | Current configuration version number |
| `current` (config store) | `DeploymentConfiguration` | Active deployment configuration (no TTL) |
| `history:{version}` (config store) | `DeploymentConfiguration` | Historical config versions (TTL: ConfigHistoryTtlDays) |
| `processing-pool:{poolType}:instances` (pool store) | `List<ProcessorInstance>` | All instances in a pool |
| `processing-pool:{poolType}:available` (pool store) | `List<ProcessorInstance>` | Available (unleased) instances |
| `processing-pool:{poolType}:leases` (pool store) | `Dictionary<string, ProcessorLease>` | Active leases by lease ID |
| `processing-pool:{poolType}:metrics` (pool store) | `PoolMetricsData` | Jobs completed/failed counts, avg time |
| `processing-pool:{poolType}:config` (pool store) | `PoolConfiguration` | Pool settings: min/max instances, thresholds |
| `processing-pool:known` (pool store) | `List<string>` | Registry of known pool types |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `orchestrator.health-ping` | `OrchestratorHealthPingEvent` | Infrastructure health check verifies pub/sub path |
| `bannou.full-service-mappings` | `FullServiceMappingsEvent` | After any routing change (deploy, teardown, topology update, reset) |
| `bannou.deployment-events` | `DeploymentEvent` | Deploy/teardown started, completed, failed, or topology changed |
| `bannou.service-lifecycle` | `ServiceRestartEvent` | Service restart requested via SmartRestartManager |
| `orchestrator.processor.released` | `ProcessorReleasedEvent` | Processor released back to pool (includes pool type, success/failure, lease duration) |
| (error topic via `TryPublishErrorAsync`) | Error event | Any unexpected internal failure |

### Consumed Events

| Topic | Event Type | Handler |
|-------|-----------|---------|
| `bannou.service-heartbeat` | `ServiceHeartbeatEvent` | `HandleServiceHeartbeat`: Routes to `OrchestratorEventManager.ReceiveHeartbeat` which raises `HeartbeatReceived` event; `ServiceHealthMonitor` subscribes to write heartbeat to state store, update routing, and publish full mappings |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `SecureWebsocket` | `ORCHESTRATOR_SECURE_WEBSOCKET` | `true` | When true, publishes blank permission registration (no WebSocket access) |
| `DefaultBackend` | `ORCHESTRATOR_DEFAULT_BACKEND` | `Compose` | Default container backend (enum: Compose, Swarm, Portainer, Kubernetes) |
| `HeartbeatTimeoutSeconds` | `ORCHESTRATOR_HEARTBEAT_TIMEOUT_SECONDS` | `90` | Heartbeat staleness threshold |
| `DegradationThresholdMinutes` | `ORCHESTRATOR_DEGRADATION_THRESHOLD_MINUTES` | `5` | Time before marking service degraded |
| `PortainerUrl` | `ORCHESTRATOR_PORTAINER_URL` | (nullable) | Portainer API URL |
| `PortainerApiKey` | `ORCHESTRATOR_PORTAINER_API_KEY` | (nullable) | Portainer API key |
| `PortainerEndpointId` | `ORCHESTRATOR_PORTAINER_ENDPOINT_ID` | `1` | Portainer endpoint ID |
| `DockerImageName` | `ORCHESTRATOR_DOCKER_IMAGE_NAME` | `"bannou:latest"` | Docker image name for deployed Bannou containers |
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
| `RestartTimeoutSeconds` | `ORCHESTRATOR_RESTART_TIMEOUT_SECONDS` | `120` | Default timeout for container restart operations |
| `HealthCheckIntervalMs` | `ORCHESTRATOR_HEALTH_CHECK_INTERVAL_MS` | `2000` | Interval between health checks during restart |
| `DefaultWaitBeforeKillSeconds` | `ORCHESTRATOR_DEFAULT_WAIT_BEFORE_KILL_SECONDS` | `30` | Seconds to wait before killing a container during stop |
| `ContainerStatusPollIntervalSeconds` | `ORCHESTRATOR_CONTAINER_STATUS_POLL_INTERVAL_SECONDS` | `2` | Deploy readiness poll interval |
| `FullMappingsIntervalSeconds` | `ORCHESTRATOR_FULL_MAPPINGS_INTERVAL_SECONDS` | `30` | Periodic full service-to-appId mappings broadcast interval |
| `OrphanContainerThresholdHours` | `ORCHESTRATOR_ORPHAN_CONTAINER_THRESHOLD_HOURS` | `24` | Hours since last heartbeat before container is orphaned |
| `IndexUpdateMaxRetries` | `ORCHESTRATOR_INDEX_UPDATE_MAX_RETRIES` | `3` | Max retries for state index update operations |
| `DefaultPoolLeaseTimeoutSeconds` | `ORCHESTRATOR_DEFAULT_POOL_LEASE_TIMEOUT_SECONDS` | `300` | Default lease timeout for acquired pool instances |
| `PoolLockTimeoutSeconds` | `ORCHESTRATOR_POOL_LOCK_TIMEOUT_SECONDS` | `15` | Timeout for distributed locks on pool operations |
| `PortainerRequestTimeoutSeconds` | `ORCHESTRATOR_PORTAINER_REQUEST_TIMEOUT_SECONDS` | `30` | HTTP request timeout for Portainer API calls |
| `DefaultServicePort` | `ORCHESTRATOR_DEFAULT_SERVICE_PORT` | `80` | Default HTTP port for discovered service instances used in health checks and mesh registration |
| `RedisConnectionString` | `ORCHESTRATOR_REDIS_CONNECTION_STRING` | `"bannou-redis:6379"` | Redis connection string |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<OrchestratorService>` | Scoped | Structured logging |
| `ILoggerFactory` | Singleton | Creates loggers for internal helpers (PresetLoader, BackendDetector backends) |
| `OrchestratorServiceConfiguration` | Singleton | All 33 config properties |
| `AppConfiguration` | Singleton | Global app configuration (DEFAULT_APP_NAME, etc.) |
| `IDistributedLockProvider` | Singleton | Pool-level distributed locks (`orchestrator-pool`, 15s TTL); mappings version increment lock |
| `IMessageBus` | Scoped | Event publishing |
| `IEventConsumer` | Scoped | Event subscription registration |
| `IHttpClientFactory` | Singleton | HTTP client for OpenResty cache invalidation |
| `IOrchestratorStateManager` | Singleton | State operations: heartbeats, routings, config versions, pool data |
| `IOrchestratorEventManager` | Singleton | Deployment/health event publishing |
| `IServiceHealthMonitor` | Singleton | Routing table management, heartbeat evaluation, source-filtered health reports (injects IControlPlaneServiceProvider) |
| `ISmartRestartManager` | Singleton | Configuration-based restart determination |
| `ITelemetryProvider` | Singleton | Span instrumentation for all async operations |
| `IContainerOrchestrator` | (resolved at runtime) | Backend-specific container operations |
| `IBackendDetector` | Singleton | Backend availability detection |
| `PresetLoader` | (created in ctor) | Filesystem preset YAML loading (receives logger + telemetry provider) |

Service lifetime is **Scoped** (per-request). Internal helpers are Singleton.

---

## API Endpoints (Implementation Notes)

### Deployment & Lifecycle (4 endpoints)

- **Deploy** (`/orchestrator/deploy`): Resolves orchestrator backend. If request is a "reset to default" (preset=default/bannou/empty, no topology), delegates to `ResetToDefaultTopologyAsync` which tears down tracked containers and resets all mappings to "bannou". Otherwise loads preset YAML via `PresetLoader`, builds per-node environment (BANNOU_SERVICES_ENABLED=false + individual enable flags), filters environment variables via whitelist (`IsAllowedEnvironmentVariable`), deploys containers via `DeployServiceAsync`, sets routing via `ServiceHealthMonitor`, initializes processing pool configurations if preset includes them, saves versioned deployment configuration, invalidates OpenResty cache, and publishes `DeploymentEvent`. Returns `DeployResponse` with success status, deployed services list, warnings, and duration.

- **Teardown** (`/orchestrator/teardown`): Supports dry-run mode (returns preview of what would be torn down). Lists containers via orchestrator, identifies services to tear down, optionally includes infrastructure services. Iterates services: tears down container, restores routing to default via `ServiceHealthMonitor`. If `includeInfrastructure=true`, also tears down infrastructure containers (redis, rabbitmq, etc.). Publishes `DeploymentEvent` with completed/failed action. Returns `TeardownResponse` with stopped containers, removed volumes, removed infrastructure.

- **GetStatus** (`/orchestrator/status`): Gets orchestrator backend, lists all containers, gets service heartbeats from state manager, gets routing mappings, derives environment status (running/degraded/stopped) from container health. Returns `EnvironmentStatus` with services, heartbeats, current deployment config, and topology metadata.

- **Clean** (`/orchestrator/clean`): Accepts target types (containers, networks, volumes, images, all). For containers: lists stopped containers, checks `IsOrphanedContainer` (must have `bannou.orchestrator-managed` label, be stopped for 24+ hours). Tears down orphaned containers. Network/volume/image pruning delegates to `IContainerOrchestrator.PruneNetworksAsync/PruneVolumesAsync/PruneImagesAsync` (Docker backends use Docker.DotNet SDK; Kubernetes returns unsupported/managed messages). Returns `CleanResponse` with counts and reclaimed space.

### Service & Container Management (4 endpoints)

- **RestartService** (`/orchestrator/services/restart`): Uses `SmartRestartManager` to determine restart parameters. Delegates to orchestrator backend `RestartContainerAsync`. Returns restart confirmation with reason and timing.

- **ShouldRestartService** (`/orchestrator/services/should-restart`): Evaluates if a service needs restart based on configuration changes. Uses `SmartRestartManager` logic to compare current vs desired state. Returns boolean recommendation with reason.

- **RequestContainerRestart** (`/orchestrator/containers/request-restart`): Accepts app name and priority/reason. Builds `ContainerRestartRequest`, delegates to backend `RestartContainerAsync`. Returns `ContainerRestartResponse` with accepted status.

- **GetContainerStatus** (`/orchestrator/containers/status`): Gets container status via backend `GetContainerStatusAsync`. Returns 404 if container has Stopped status with 0 instances. Returns `ContainerStatus` with status enum, instances, labels, timestamp.

### Health Monitoring (2 endpoints)

- **GetInfrastructureHealth** (`/orchestrator/health/infrastructure`): Checks health of infrastructure components. Uses state manager `CheckHealthAsync` for Redis connectivity (measures operation time). Publishes `OrchestratorHealthPingEvent` to verify pub/sub path. Returns health status for each component (redis, rabbitmq, docker).

- **GetServiceHealth** (`/orchestrator/health/services`): Returns service health report with source filtering via `ServiceHealthSource` enum:
  - `all` (default): Combines control plane services (from `IControlPlaneServiceProvider`) and deployed services (from heartbeats). Control plane services are immediately healthy; deployed services are evaluated against `HeartbeatTimeoutSeconds` and `DegradationThresholdMinutes` thresholds.
  - `control_plane_only`: Returns only services running on the control plane (this Bannou instance). Useful for introspection.
  - `deployed_only`: Returns only services with heartbeats from deployed containers. Excludes the control plane. Useful for monitoring distributed deployments.
  - Optional `serviceFilter` parameter filters results by service name substring.
  - Response includes `source` (the filter used), `controlPlaneAppId` (e.g., "bannou"), healthy/unhealthy service lists, counts, and health percentage.

### Configuration Versioning (2 endpoints)

- **RollbackConfiguration** (`/orchestrator/config/rollback`): Supports rolling back to any historical version. If `targetVersion` is specified, rolls back to that version; otherwise defaults to version N-1. Validates target version is >= 1 and < current version. Retrieves target config from history. Calls `RestoreConfigurationVersionAsync` which creates a NEW version (currentVersion+1) containing the old config (preserves audit trail). Computes changed keys (services and env vars that differ). Returns `ConfigRollbackResponse` with version numbers and changed keys list.

- **GetConfigVersion** (`/orchestrator/config/version`): Gets current version number and configuration from Redis. Checks if previous version exists in history. Extracts key prefixes from current config (service names, env var prefixes). Returns `ConfigVersionResponse` with version, timestamp, hasPreviousConfig, keyCount, keyPrefixes.

### Topology Management (1 endpoint)

- **UpdateTopology** (`/orchestrator/topology`): Accepts list of `TopologyChange` with actions. Iterates changes, applies each:
  - `AddNode`: Deploys services to new node with `bannou-{service}-{nodeName}` app-id. Sets BANNOU_SERVICES_ENABLED=false plus per-service enable flags. Updates routing.
  - `RemoveNode`: Tears down all services for a node. Restores routing to default.
  - `MoveService`: Updates routing only (no container changes). Points service to new node app-id.
  - `Scale`: Calls `ScaleServiceAsync` on orchestrator backend with target replicas.
  - `UpdateEnv`: Redeploys services with new environment variables (DeployServiceAsync handles cleanup+recreation).
  Reports partial success: per-change success/error tracking. Returns `TopologyUpdateResponse` with applied changes and warnings.

### Processing Pool Management (4 endpoints)

- **AcquireProcessor** (`/orchestrator/processing-pool/acquire`): Validates pool type. Acquires `orchestrator-pool` distributed lock on `{poolType}` (15s TTL) to prevent concurrent read-modify-write races. Gets available processor list from Redis. Pops first available, creates lease (Guid-based) with timeout (default 300s). Stores lease in hash. Returns 503 if no processors available. Returns processor ID, app-id, lease ID, expiry.

- **ReleaseProcessor** (`/orchestrator/processing-pool/release`): Searches all known pool types for the lease ID (read-only, outside lock). Once target pool is identified, acquires `orchestrator-pool` distributed lock on `{poolType}` (15s TTL). Removes lease from hash, returns processor to available list. Updates pool metrics (job success/failure count). Publishes `orchestrator.processor.released` event with pool type, processor ID, success/failure, and lease duration. Returns release confirmation.

- **GetPoolStatus** (`/orchestrator/processing-pool/status`): Reads instance list, available list, leases hash, and config for pool type. Computes total/available/busy instance counts. If `includeMetrics=true`, reads metrics data (jobs completed/failed 1h, avg processing time, last scale event). Returns `PoolStatusResponse`.

- **ScalePool** (`/orchestrator/processing-pool/scale`): Loads pool config from Redis. If scaling up: deploys new worker containers via orchestrator backend with pool-specific environment (BANNOU_SERVICES_ENABLED=false, specific plugin enabled, BANNOU_APP_ID, ACTOR_POOL_NODE_ID). If scaling down: prefers removing available instances first; with `force=true` also removes busy instances. Tears down containers, cleans leases. Updates instance/available lists and metrics.

### Pool Cleanup (1 endpoint)

- **CleanupPool** (`/orchestrator/processing-pool/cleanup`): Gets available instances and pool config. If `preserveMinimum=true`, keeps minInstances alive. For each excess idle instance: calls `TeardownServiceAsync` to stop the container, then removes from both instance and available lists. Returns removed count.

### Discovery & Routing (3 endpoints)

- **GetServiceRouting** (`/orchestrator/service-routing`): Gets all service routing mappings from state manager. Returns dictionary of service name to app-id with host/port info.

- **ListPresets** (`/orchestrator/presets/list`): Uses `PresetLoader` to scan presets directory. Reads YAML files, extracts name, services list, description. Returns list of available deployment presets.

- **ListBackends** (`/orchestrator/backends/list`): Uses `BackendDetector` to check availability of each backend (Docker socket, Portainer API, Kubernetes config). Returns detected backends with availability status and connection info.

### Logs (1 endpoint)

- **GetLogs** (`/orchestrator/logs`): Resolves target from service name or container name (falls back to "bannou"). Calls `GetContainerLogsAsync` with tail count and optional since timestamp. Parses log text into `LogEntry` objects. Attempts to parse Docker timestamp prefix (ISO 8601 with nanoseconds) from each line; falls back to UtcNow if parsing fails. Handles `[STDERR]` markers to distinguish stream types.

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
    +----+----+       | Teardown  |     | Metrics   |
    | Orch.   |       | + Remove  |     | Updated   |
    | Deploy  |       | state     |     +-----------+
    +---------+       +-----------+

    Scale Up:  Deploy new containers --> Pending --> Available (after self-reg)
    Scale Down: Available --> Teardown container --> Remove from state
    Acquire:    Lock(pool) --> Available.pop() --> Create Lease --> Return processor info
    Release:    Find pool --> Lock(pool) --> Remove Lease --> Available.push() --> Update metrics --> Publish event
    Cleanup:    Available.excess(minInstances) --> Teardown container --> Remove from state
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
    DEPLOYED SERVICES (via heartbeats)        CONTROL PLANE SERVICES (via PluginLoader)
    +------------------+  +------------------+       +---------------------------+
    | Service Instance |  | Service Instance |       | IControlPlaneServiceProvider |
    | (bannou-auth)    |  | (bannou-actor-1) |       | - PluginLoader.EnabledPlugins |
    +--------+---------+  +--------+---------+       | - EffectiveAppId: "bannou"    |
             |                     |                 +-------------+---------------+
             | ServiceHeartbeatEvent                               |
             v                     v                               |
    +--------+---------------------+--------+                      |
    |      bannou.service-heartbeat         |                      |
    +--------+------------------------------+                      |
             |                                                     |
    +--------v---------+                                           |
    | HandleServiceHB  |                                           |
    | (Events partial) |                                           |
    +--------+---------+                                           |
             |                                                     |
    +--------v--------------------+                                |
    | OrchestratorStateManager    |                                |
    | - Write heartbeat (TTL 90s) |                                |
    | - Update index (ETag CAS)   |                                |
    +-------------+---------------+                                |
                  |                                                |
                  v                                                v
    +-------------+------------------------------------------------+---------+
    |                  ServiceHealthMonitor.GetServiceHealthReportAsync      |
    |  +------------------------------------------------------------------+  |
    |  | ServiceHealthSource Filter:                                      |  |
    |  |   all              -> Control plane services + Deployed services |  |
    |  |   control_plane_only -> Control plane services only              |  |
    |  |   deployed_only    -> Deployed services only (from heartbeats)   |  |
    |  +------------------------------------------------------------------+  |
    +------------------------------------------------------------------------+
                                       |
                                       v
                            +-------------------+
                            | ServiceHealthReport |
                            | - source           |
                            | - controlPlaneAppId|
                            | - healthyServices  |
                            | - unhealthyServices|
                            | - healthPercentage |
                            +-------------------+

    Status Logic (deployed services):
    - LastSeen < HeartbeatTimeoutSeconds:  Healthy
    - LastSeen < DegradationThresholdMin:  Degraded
    - LastSeen > DegradationThresholdMin:  Stopped
    - Issues non-empty:                    Unhealthy

    Control plane services: Always Healthy (running in this process)
```

---

## Stubs & Unimplemented Features

| Feature | Status | Notes |
|---------|--------|-------|
| Queue depth tracking (pool status) | Hardcoded 0 | Comment: "We don't have a queue yet" |
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/252 -->
| Auto-scaling (pool) | No trigger | Thresholds are stored but no background job evaluates them |
<!-- AUDIT:NEEDS_DESIGN:2026-03-02:https://github.com/beyond-immersion/bannou-service/issues/550 -->
| Idle timeout cleanup (pool) | No trigger | `IdleTimeoutMinutes` stored but no background timer |
<!-- AUDIT:NEEDS_DESIGN:2026-03-02:https://github.com/beyond-immersion/bannou-service/issues/550 -->
| ~~**Log timestamp parsing**~~ | **FIXED** (2026-03-02) | Continuation lines (e.g., stack traces) now inherit the preceding line's parsed timestamp instead of falling back to `DateTimeOffset.UtcNow`. Lines before any successfully parsed timestamp still use UtcNow as the initial default. |

---

## Potential Extensions

- **Auto-scaling background service**: A timer-based service that evaluates pool utilization against `ScaleUpThreshold`/`ScaleDownThreshold` and automatically scales pools.
<!-- AUDIT:NEEDS_DESIGN:2026-03-02:https://github.com/beyond-immersion/bannou-service/issues/550 -->
- **Idle timeout enforcement**: Background cleanup for pool workers that have been available beyond `IdleTimeoutMinutes`.
<!-- AUDIT:NEEDS_DESIGN:2026-03-02:https://github.com/beyond-immersion/bannou-service/issues/550 -->
- ~~**Multi-version rollback**~~: **FIXED** (2026-03-02) - Added optional `targetVersion` field to `ConfigRollbackRequest`. When omitted, rolls back to N-1 (preserving backward compatibility). When specified, rolls back to the requested historical version with validation (must be >= 1 and < current version). Returns 404 if the target version has expired from history.
- **Deploy validation**: Pre-flight checks before deployment (disk space, network reachability, image pull verification).
<!-- AUDIT:NEEDS_DESIGN:2026-03-02:https://github.com/beyond-immersion/bannou-service/issues/551 -->
- **Blue-green deployment**: Deploy new topology alongside old, switch routing atomically, then teardown old.
<!-- AUDIT:NEEDS_DESIGN:2026-03-02:https://github.com/beyond-immersion/bannou-service/issues/552 -->
- **Canary deployments**: Route percentage of traffic to new version, monitor health, then promote or rollback.
<!-- AUDIT:NEEDS_DESIGN:2026-03-02:https://github.com/beyond-immersion/bannou-service/issues/553 -->
- **Processing pool priority queue**: Currently FIFO; could use priority field from acquire requests.
- **Lease expiry enforcement**: Background timer to reclaim expired leases and return processors to available pool.

---

## Known Quirks & Caveats

### Bugs

None currently active.

### Intentional Quirks

1. **Rollback creates new version**: Rolling back from v5 to v3 creates v6 (a copy of v3 with new timestamp). The history trail is never overwritten.

2. **Config clear saves empty config as new version**: `ClearCurrentConfigurationAsync` does not delete the config entry but saves an empty `DeploymentConfiguration` with preset="default". Maintains the version history audit trail.

3. **Pool metrics window reset is lazy**: `JobsCompleted1h` and `JobsFailed1h` counters reset when the first operation occurs after the 1-hour window has elapsed. There is no background timer - the reset happens inline during `UpdatePoolMetricsAsync`.

4. **Expired lease reclamation is lazy**: When a processor lease expires, it is reclaimed during the next `AcquireProcessorAsync` call. There is no background timer proactively scanning for expired leases. Pools with no acquire traffic will not reclaim expired processors until the next request arrives.

5. **OpenResty cache invalidation is non-blocking**: If OpenResty is unavailable (common in local dev), the deployment proceeds normally. Cache will eventually expire based on OpenResty's own TTL.

6. **Partial failure in topology changes (no rollback)**: Each topology change is applied independently. If 3 of 5 changes succeed, the response reports partial success with per-change error details. The already-applied changes are NOT rolled back.

### Design Considerations

1. **Index-based state pattern**: The orchestrator avoids Redis KEYS/SCAN commands entirely. Instead, it maintains explicit index entries (`_index` keys) that track known app IDs and service names. These indexes use ETag-based optimistic concurrency for safe concurrent updates.

2. **Processing pools are state-tracked, not containerized abstractions**: Pool instances are tracked purely in Redis state. The orchestrator deploys actual containers for workers but does not wrap them in any higher-level abstraction.

3. **Backend detection at request time**: `GetOrchestratorAsync` is called per-request, not at startup. This allows the backend to change dynamically (e.g., Docker socket becomes available after orchestrator starts).

4. ~~**GetOrchestratorAsync TTL check is ineffective for scoped service**~~: **FIXED** (2026-03-02) - Removed dead TTL-based cache invalidation logic and `_orchestratorCachedAt` field from `GetOrchestratorAsync`. Method now provides simple within-request reuse only. Removed `CacheTtlMinutes` config property (was dead config per IMPLEMENTATION TENETS).

5. ~~**`_lastKnownDeployment` in-memory state divergence concern**~~: **FIXED** (2026-03-02) - Not an issue. `OrchestratorService` is Scoped (per-request), so `_lastKnownDeployment` starts null each request and is lazily loaded from Redis via `_stateManager.GetCurrentConfigurationAsync()`. The field already reads from distributed state; within-request caching cannot diverge across instances. Same pattern as Design Consideration #4 (`_orchestrator` TTL).

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow and should not be manually edited except to add new tracking markers.

### Completed

- **Design Consideration #5 (`_lastKnownDeployment` divergence)**: Resolved as false concern (2026-03-02). Service is Scoped; field already reads from Redis per-request.
- **Log timestamp parsing**: Fixed (2026-03-02). Continuation lines now inherit preceding line's timestamp instead of UtcNow.
- **Design Consideration #4 (GetOrchestratorAsync TTL dead code)**: Fixed (2026-03-02). Removed dead TTL-based cache invalidation logic, `_orchestratorCachedAt` field, and `CacheTtlMinutes` config property. Method simplified to within-request reuse only.
- **Multi-version rollback**: Fixed (2026-03-02). Added optional `targetVersion` field to `ConfigRollbackRequest` schema. Service validates target is >= 1 and < current version.

---

*This document describes the orchestrator plugin as implemented. For architectural context, see [TENETS.md](../reference/TENETS.md) and [BANNOU-DESIGN.md](../BANNOU-DESIGN.md).*
