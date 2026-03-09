# Orchestrator Plugin Deep Dive

> **Plugin**: lib-orchestrator
> **Schema**: schemas/orchestrator-api.yaml
> **Version**: 3.0.0
> **Layer**: AppFeatures
> **State Store**: orchestrator-heartbeats (Redis), orchestrator-routings (Redis), orchestrator-config (Redis), orchestrator-statestore (Redis)
> **Implementation Map**: [docs/maps/ORCHESTRATOR.md](../maps/ORCHESTRATOR.md)
> **Short**: Deployment orchestration (Docker/Swarm/Portainer/K8s) with processing pools and routing broadcasts

---

## Overview

Central intelligence (L3 AppFeatures) for Bannou environment management and service orchestration. Manages distributed service deployments including preset-based topologies, live topology updates, processing pools for on-demand worker containers (used by lib-actor for NPC brains), service health monitoring via heartbeats, versioned deployment configurations with rollback, and service-to-app-id routing broadcasts consumed by lib-mesh. Features a pluggable backend architecture supporting Docker Compose, Docker Swarm, Portainer, and Kubernetes. Operates in a secure mode making it inaccessible via WebSocket (all endpoints are service-to-service only, `x-permissions: []`).

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-mesh | Consumes `bannou.full-service-mappings` events for dynamic service routing |
| lib-actor | **Planned**: Will use processing pool for auto-scaled NPC brain worker containers via `IActorPoolScaleListener` DI pattern (see [#318](https://github.com/beyond-immersion/bannou-service/issues/318)). Currently manages its own pool nodes independently. |
| lib-procedural | Uses processing pool acquire/release for Houdini worker containers (hard dependency on `IOrchestratorClient`) |
| All services | Receive routing updates published by orchestrator after topology changes |

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
| `LeaseCleanupIntervalSeconds` | `ORCHESTRATOR_LEASE_CLEANUP_INTERVAL_SECONDS` | `60` | Background timer interval for reclaiming expired pool leases |
| `PortainerRequestTimeoutSeconds` | `ORCHESTRATOR_PORTAINER_REQUEST_TIMEOUT_SECONDS` | `30` | HTTP request timeout for Portainer API calls |
| `DefaultServicePort` | `ORCHESTRATOR_DEFAULT_SERVICE_PORT` | `80` | Default HTTP port for discovered service instances used in health checks and mesh registration |
| `RedisConnectionString` | `ORCHESTRATOR_REDIS_CONNECTION_STRING` | `"bannou-redis:6379"` | Redis connection string |

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
| ~~Preset infrastructure config~~ | **FIXED** (2026-03-07) | `ConvertInfrastructure` now maps `PresetInfrastructure` to `InfrastructureConfig` (enabled flag per service). `Version` field is not mapped (no equivalent in API model). |

---

## Potential Extensions

- **Auto-scaling background service**: A timer-based service that evaluates pool utilization against `ScaleUpThreshold`/`ScaleDownThreshold` and automatically scales pools.
<!-- AUDIT:NEEDS_DESIGN:2026-03-02:https://github.com/beyond-immersion/bannou-service/issues/550 -->
- **Idle timeout enforcement**: Background cleanup for pool workers that have been available beyond `IdleTimeoutMinutes`.
<!-- AUDIT:NEEDS_DESIGN:2026-03-02:https://github.com/beyond-immersion/bannou-service/issues/550 -->
- **Actor pool auto-scale integration**: Implement `IActorPoolScaleListener` (DI Listener interface defined in `bannou-service/Providers/`) so Orchestrator can react to Actor pool capacity exhaustion and underutilization events. This is the most north-star-critical gap — 100K+ concurrent NPCs depends on auto-scaling. Requires #550 (auto-scaling background service) as a prerequisite.
<!-- AUDIT:NEEDS_DESIGN:2026-03-05:https://github.com/beyond-immersion/bannou-service/issues/318 -->

- **Deploy validation**: Pre-flight checks before deployment (disk space, network reachability, image pull verification).
<!-- AUDIT:NEEDS_DESIGN:2026-03-02:https://github.com/beyond-immersion/bannou-service/issues/551 -->
- **Blue-green deployment**: Deploy new topology alongside old, switch routing atomically, then teardown old. Should be co-designed with canary deployments (#553) — both require changes to the routing model and share infrastructure for traffic splitting.
<!-- AUDIT:NEEDS_DESIGN:2026-03-02:https://github.com/beyond-immersion/bannou-service/issues/552 -->
- **Canary deployments**: Route percentage of traffic to new version, monitor health, then promote or rollback. Requires changes to `FullServiceMappingsEvent.Mappings` (currently 1:1 `serviceName → appId`) which affects lib-mesh (L0). Must be co-designed with blue-green (#552).
<!-- AUDIT:NEEDS_DESIGN:2026-03-02:https://github.com/beyond-immersion/bannou-service/issues/553 -->
- **Processing pool priority queue**: Currently FIFO; could use priority field from acquire requests. Dependent on #252 (queue implementation) which is itself deferred pending auto-scaling (#550).
<!-- AUDIT:NEEDS_DESIGN:2026-03-02:https://github.com/beyond-immersion/bannou-service/issues/554 -->

- **Auto-detection of configuration changes per backend**: The `POST /orchestrator/config/notify-change` endpoint provides a manual trigger for `ConfigurationChangedEvent`, but auto-detecting config changes per backend (K8s ConfigMap watches, Portainer webhooks, Docker label-based detection) is a design question. Most deployments don't have live config update detection, and the manual endpoint covers all backends. The consumer side (plugins reacting to `changedKeys` prefixes to request restarts) is also unimplemented.
<!-- AUDIT:NEEDS_DESIGN:2026-03-05:https://github.com/beyond-immersion/bannou-service/issues/565 -->

---

## Known Quirks & Caveats

### Bugs

None currently active.

### Intentional Quirks

1. **Rollback creates new version**: Rolling back from v5 to v3 creates v6 (a copy of v3 with new timestamp). The history trail is never overwritten.

2. **Config clear saves empty config as new version**: `ClearCurrentConfigurationAsync` does not delete the config entry but saves an empty `DeploymentConfiguration` with preset="default". Maintains the version history audit trail.

3. **Pool metrics window reset is lazy**: `JobsCompleted1h` and `JobsFailed1h` counters reset when the first operation occurs after the 1-hour window has elapsed. There is no background timer - the reset happens inline during `UpdatePoolMetricsAsync`.

4. **Expired lease reclamation has dual paths**: Expired leases are reclaimed both lazily (during `AcquireProcessorAsync` as a fast-path) and proactively (via background timer in `ServiceHealthMonitor` every `LeaseCleanupIntervalSeconds`, default 60s). The lazy path ensures immediate availability during acquire; the background path prevents metric inflation when no acquire requests arrive.

5. **OpenResty cache invalidation is non-blocking**: If OpenResty is unavailable (common in local dev), the deployment proceeds normally. Cache will eventually expire based on OpenResty's own TTL.

6. **Partial failure in topology changes (no rollback)**: Each topology change is applied independently. If 3 of 5 changes succeed, the response reports partial success with per-change error details. The already-applied changes are NOT rolled back.

### Design Considerations

1. **Index-based state pattern**: The orchestrator avoids Redis KEYS/SCAN commands entirely. Instead, it maintains explicit index entries (`_index` keys) that track known app IDs and service names. These indexes use ETag-based optimistic concurrency for safe concurrent updates.

2. **Processing pools are state-tracked, not containerized abstractions**: Pool instances are tracked purely in Redis state. The orchestrator deploys actual containers for workers but does not wrap them in any higher-level abstraction.

3. **Backend detection at request time**: `GetOrchestratorAsync` is called per-request, not at startup. This allows the backend to change dynamically (e.g., Docker socket becomes available after orchestrator starts).

4. **`_lastKnownDeployment` reads from distributed state per-request**: `OrchestratorService` is Scoped (per-request), so `_lastKnownDeployment` starts null each request and is lazily loaded from Redis via `_stateManager.GetCurrentConfigurationAsync()`. Within-request caching cannot diverge across instances.

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow and should not be manually edited except to add new tracking markers.

### Completed

- **Preset infrastructure config** (2026-03-07): Implemented `ConvertInfrastructure` in `PresetLoader.cs` to map preset infrastructure definitions to `InfrastructureConfig` on the output `ServiceTopology`. All 4 presets with `infrastructure:` sections now surface that data in API responses.

---

*This document describes the orchestrator plugin as implemented. For architectural context, see [TENETS.md](../reference/TENETS.md) and [BANNOU-DESIGN.md](../BANNOU-DESIGN.md).*
