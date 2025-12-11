# Orchestrator Service Design

**Version**: 2.4.0
**Last Updated**: 2025-12-11
**Status**: Core Implementation 95% Complete - Standalone Dapr Architecture + Preset System + Heartbeat Deployment Validation

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Problem Statement & Root Cause](#problem-statement--root-cause)
3. [Architecture Overview](#architecture-overview)
4. [Architectural Evolution (December 2025)](#architectural-evolution-december-2025)
5. [Backend Priority System](#backend-priority-system)
5. [Core Components](#core-components)
6. [Interface-Based Architecture](#interface-based-architecture)
7. [Unit Testing](#unit-testing)
8. [Service Mapping Architecture](#service-mapping-architecture)
9. [API Design](#api-design)
10. [Makefile Commands](#makefile-commands)
11. [Deployment Presets](#deployment-presets)
12. [Service Topology Management](#service-topology-management)
13. [Health Monitoring](#health-monitoring)
14. [Secrets and Configuration Management](#secrets-and-configuration-management)
15. [Configuration Update Event System](#configuration-update-event-system)
16. [CI/CD Integration Strategy](#cicd-integration-strategy)
17. [Docker Socket Security](#docker-socket-security)
18. [Implementation Status](#implementation-status)
19. [Critical Unimplemented TODOs](#critical-unimplemented-todos)
20. [Edge-Tester Blockers](#edge-tester-blockers)
21. [Risk Assessment](#risk-assessment)
22. [Future Enhancements](#future-enhancements)
23. [Related Documentation](#related-documentation)
24. [Appendix: Configuration Reference](#appendix-configuration-reference)

---

## Executive Summary

The Orchestrator Service is Bannou's central intelligence for environment management and service orchestration. It provides API-driven control over container lifecycle, service topology, infrastructure health, and deployment automation—replacing manual Makefile/docker-compose commands with a unified programmatic interface.

**Key Capabilities**:
- **Environment Provisioning**: Deploy complete environments via API (dev, staging, production)
- **Dynamic Service Topology**: Reconfigure which services run on which containers at runtime
- **Multi-Backend Support**: Docker Compose, Docker Swarm, Portainer, and Kubernetes
- **Infrastructure Health**: Monitor Redis, RabbitMQ, Dapr placement, and custom services
- **Smart Service Management**: Restart, scale, and configure services based on health metrics
- **Test Orchestration**: API-driven test execution with environment presets

**Critical Design Decision**: The orchestrator runs **standalone without a Dapr sidecar**. It uses direct connections to Redis and RabbitMQ to avoid chicken-and-egg dependency issues—the orchestrator must be able to start before Dapr infrastructure is available.

---

## Problem Statement & Root Cause

### The Original Problems (October 2025)

The orchestrator service was designed to solve fundamental issues with Dapr and Docker Compose resilience:

1. **Dapr Sidecars Exit on Connection Failure**:
   - Dapr exits gracefully when component initialization fails (default `initTimeout: 5s`)
   - **NO built-in retry mechanism** for component initialization
   - Resiliency policies only apply to runtime operations, NOT initialization
   - `ignoreErrors: true` makes components completely unavailable (not retried)

2. **`restart: always` Violates Resilience Requirements**:
   - Using restart policies on Dapr sidecars creates visible 2-5 second outages
   - Multiple failure points: RabbitMQ + 2 Redis instances + MySQL = 4 restart triggers
   - Cascading failures: main service restart breaks Dapr sidecar network (Docker Compose issue #10263)

3. **`depends_on` Causes Restart Storms in Production**:
   - In Kubernetes/Swarm/Portainer, `depends_on` causes immediate restarts when dependencies become unhealthy
   - Redis going down triggers all dependent services to restart simultaneously
   - **This is exactly the problem the orchestrator replaces**

4. **`--exit-code-from` Prevents Restart Policies**:
   - CI testing workflow conflicts with container restart strategies
   - Tests either block on exit codes OR have restart resilience, not both

### Root Cause Analysis (from DAPR_RESTART_ANALYSIS.md)

**The Issue IS NOT**: Dapr being fragile
**The Issue IS**: Dapr's architecture assumes infrastructure availability at startup

Dapr was designed for Kubernetes environments where:
- Infrastructure is available before services start (init containers, readiness probes)
- Restarts are orchestrated and transparent (Kubernetes feature)
- Health probes prevent traffic to non-ready pods (Kubernetes routing)

**Docker Compose lacks these guarantees**. Our options were:
1. Change how we use Dapr (remove component dependencies)
2. Accept restart pattern as non-production stopgap
3. Move to Kubernetes (proper orchestration)
4. **Create orchestrator service (chosen solution)**

### Two Distinct Dapr Behaviors

| Scenario | Dapr Behavior | Impact |
|----------|---------------|---------|
| **Initial Connection Fails** | Exits with fatal error | Container restart required |
| **Runtime Connection Lost** | Attempts reconnection every 3s | Service continues (with caveats) |

**Critical Gap**: Runtime reconnection logic **only works AFTER successful initial connection**. If RabbitMQ/Redis aren't available at startup, Dapr cannot reach the runtime reconnection phase.

---

## Architecture Overview

### Standalone Design Pattern

```
┌─────────────────────────────────────────────────────────────────┐
│                    ORCHESTRATOR SERVICE                          │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │                   OrchestratorService.cs                     │ │
│  │  - No Dapr sidecar dependency                               │ │
│  │  - Direct Redis connection (StackExchange.Redis)            │ │
│  │  - Direct RabbitMQ connection (RabbitMQ.Client)             │ │
│  │  - Docker API via Docker.DotNet                             │ │
│  └─────────────────────────────────────────────────────────────┘ │
│                              │                                    │
│         ┌───────────────────┼───────────────────┐                │
│         │                   │                   │                 │
│         ▼                   ▼                   ▼                 │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────┐          │
│  │    Redis    │    │  RabbitMQ   │    │   Docker    │          │
│  │   (Direct)  │    │  (Direct)   │    │    API      │          │
│  │             │    │             │    │             │          │
│  │ - Heartbeats│    │ - Events    │    │ - Containers│          │
│  │ - State     │    │ - Commands  │    │ - Networks  │          │
│  │ - Locks     │    │ - Logs      │    │ - Volumes   │          │
│  └─────────────┘    └─────────────┘    └─────────────┘          │
└─────────────────────────────────────────────────────────────────┘
```

### Why No Dapr Sidecar?

1. **Chicken-and-Egg Problem**: The orchestrator must be able to manage Dapr infrastructure (placement service, sidecars) which means it can't depend on Dapr to function.

2. **Startup Order Independence**: The orchestrator needs to start before any other service, including infrastructure components.

3. **Resilience**: Direct connections allow the orchestrator to diagnose and recover from Dapr-level failures.

4. **Simplicity**: Fewer moving parts in the most critical service.

### Dual-Instance Pattern

**Primary Bannou Instance** (Test Subject/Production Services):
- Runs with various configurations for testing or production workloads
- Can be restarted/reconfigured by orchestrator
- Contains all business services (accounts, auth, behavior, etc.)

**Orchestrator Bannou Instance** (Management Layer):
- Runs continuously during entire lifecycle
- Contains only the orchestrator service plugin
- Manages primary instance via Docker/Kubernetes APIs
- Executes tests via API calls and collects results
- **Direct connections** to Redis + RabbitMQ (not via Dapr components)

---

## Architectural Evolution (December 2025)

The orchestrator went through significant iteration during development to work around Dapr and Docker Compose limitations. This section documents the key decisions and their rationale.

### Key Decisions Summary

| Decision | Previous Approach | Current Approach | Rationale |
|----------|-------------------|------------------|-----------|
| **Dapr sidecar architecture** | `network_mode:service` (shared namespace) | Standalone containers | Dapr compatibility issues with shared network namespaces |
| **Service discovery** | Consul | mDNS (Dapr default) | Consul was unnecessary complexity - mDNS works on Docker bridge |
| **Infrastructure resolution** | Docker DNS (127.0.0.11) | ExtraHosts IP injection | Docker DNS unreliable for dynamically created containers |
| **Orchestrator communication** | Via Dapr components | Direct Redis/RabbitMQ | Chicken-and-egg: orchestrator must start before Dapr |

### Standalone Dapr Sidecar Architecture

The original design attempted to use `network_mode: service:bannou` for Dapr sidecars, which would share the network namespace with the application container. This approach failed due to:

1. **Dapr Compatibility**: Dapr doesn't fully support shared network namespaces in Docker Compose
2. **Port Conflicts**: Multiple sidecars can't share the same network namespace
3. **Debugging Complexity**: Harder to diagnose network issues with shared namespaces

**Current Pattern**: Each application container gets a paired standalone Dapr sidecar:

```
[App Container: bannou]     [Dapr Sidecar: bannou-dapr]
   - Port 80                   - Port 3500 (HTTP)
   - DAPR_HTTP_ENDPOINT=       - Port 50001 (gRPC)
     http://bannou-dapr:3500   - placement-host-address=placement:50006
```

### mDNS vs Consul for Dapr Discovery

Consul was initially considered for service discovery between Dapr sidecars. However:

1. **Unnecessary Complexity**: mDNS is Dapr's default and works on Docker bridge networks
2. **Additional Infrastructure**: Consul requires its own container and configuration
3. **Development Overhead**: Consul adds complexity without meaningful benefit for our use case

**Current Pattern**: Let Dapr use its default mDNS resolver. No additional configuration needed.

### ExtraHosts IP Injection Pattern

Docker's embedded DNS (127.0.0.11) proved unreliable for dynamically created containers. The DockerComposeOrchestrator now:

1. **Discovers Infrastructure IPs**: Scans running containers to find Redis, RabbitMQ, MySQL, placement service
2. **Injects ExtraHosts**: Adds hostname→IP mappings when creating new containers
3. **Maintains Fallback Chain**: Multiple discovery strategies with graceful fallback

```csharp
// DockerComposeOrchestrator.cs - ExtraHosts injection
var extraHosts = new List<string>
{
    $"redis:{redisIp}",
    $"rabbitmq:{rabbitmqIp}",
    $"mysql:{mysqlIp}",
    $"placement:{placementIp}"
};
```

### Direct Redis/RabbitMQ Connections

The orchestrator bypasses Dapr for infrastructure connections because:

1. **Startup Order**: Orchestrator must start before Dapr infrastructure exists
2. **Infrastructure Monitoring**: Need to check Redis/RabbitMQ health even when Dapr is down
3. **Heartbeat Publishing**: Services write heartbeats directly to Redis for NGINX routing

**Redis Key Patterns**:
- `service:heartbeat:{appId}` - TTL 90s, health status per service instance
- `service:routing:{serviceName}` - TTL 5min, current app-id for service

---

## Backend Priority System

The orchestrator automatically detects available container orchestration backends and selects the most capable one. Manual override is supported but fails (no fallback) if the specified backend is unavailable.

### Priority Order

| Priority | Backend    | Detection Method                        | Best For                        |
|----------|------------|----------------------------------------|----------------------------------|
| 1        | Kubernetes | kubectl + cluster connectivity         | Enterprise production            |
| 2        | Portainer  | Portainer API endpoint check           | Multi-host with web UI          |
| 3        | Swarm      | `docker info` swarm mode check         | Docker cluster orchestration    |
| 4        | Compose    | docker compose v2 availability         | Single-host development         |

### Backend Capabilities Matrix

| Capability        | Kubernetes | Portainer | Swarm  | Compose |
|-------------------|------------|-----------|--------|---------|
| live-topology     | ✅         | ✅        | ✅     | ⚠️ Limited |
| scaling           | ✅         | ✅        | ✅     | ❌      |
| rolling-update    | ✅         | ✅        | ✅     | ❌      |
| secrets           | ✅ Native  | ✅ API    | ✅ Native | ❌ .env only |
| volumes           | ✅         | ✅        | ✅     | ✅      |
| networks          | ✅         | ✅        | ✅     | ✅      |

### Platform Abstraction Interface

The orchestrator uses an abstraction layer for dual implementation (Docker Compose + Kubernetes):

```csharp
public interface IContainerOrchestrator
{
    Task<bool> RestartServiceAsync(string serviceName, Dictionary<string, string>? env = null);
    Task<ServiceHealthReport> GetServiceHealthAsync();
    Task<bool> ShouldRestartServiceAsync(string serviceName);
}

public class DockerComposeOrchestrator : IContainerOrchestrator
{
    private readonly DockerClient _dockerClient;
    // Development implementation using Docker.DotNet
}

public class KubernetesOrchestrator : IContainerOrchestrator
{
    private readonly IKubernetes _kubernetesClient;
    // Production implementation using KubernetesClient
}
```

---

## Core Components

### OrchestratorService.cs

Main service orchestration logic implementing `IOrchestratorService`:
- Health checking (infrastructure and services)
- Test execution via API
- Service restart with smart logic

### OrchestratorRedisManager.cs

Direct Redis connection management using **StackExchange.Redis** (already in lib-connect):

**Wait-on-Startup Pattern**:
```csharp
public static async Task<IConnectionMultiplexer> ConnectWithRetryAsync(
    string connectionString,
    ILogger logger,
    CancellationToken cancellationToken = default)
{
    var options = ConfigurationOptions.Parse(connectionString);
    options.ConnectRetry = 10;
    options.ConnectTimeout = 5000;
    options.AbortOnConnectFail = false;  // ✅ Keep trying instead of crashing
    options.ReconnectRetryPolicy = new ExponentialRetry(1000, 30000);

    // Exponential backoff with retry loop
    // StackExchange.Redis handles reconnection automatically after initial connect
}
```

**Key Insight**: StackExchange.Redis has built-in automatic reconnection. We only need wait-on-startup logic; custom reconnection monitoring is unnecessary.

### OrchestratorEventManager.cs

Direct RabbitMQ connection management using **RabbitMQ.Client** (NOT Dapr):

```csharp
var factory = new ConnectionFactory
{
    Uri = new Uri(connectionString),
    AutomaticRecoveryEnabled = true,  // ✅ Built-in reconnection
    NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
    RequestedHeartbeat = TimeSpan.FromSeconds(30),
    DispatchConsumersAsync = true  // Async consumer support
};
```

**Why RabbitMQ.Client instead of Dapr**:
- Dapr sidecar depends on RabbitMQ being available (chicken-and-egg problem)
- Orchestrator must start BEFORE Dapr sidecars to coordinate infrastructure
- Using Dapr would create the exact dependency issue we're solving

### ServiceHealthMonitor.cs

Health monitoring based on Redis heartbeat data using existing **ServiceHeartbeatEvent** schema from `common-events.yaml`:

```yaml
# Existing schema (DO NOT MODIFY)
ServiceHeartbeatEvent:
  required: [eventId, timestamp, serviceId, appId, status]
  properties:
    serviceId: string           # Service ID (e.g., "behavior", "accounts")
    appId: string               # Dapr app-id for this instance
    status: enum                # [healthy, degraded, overloaded, shutting_down]
    capacity:
      maxConnections: integer
      currentConnections: integer
      cpuUsage: float           # 0.0 - 1.0
      memoryUsage: float        # 0.0 - 1.0
```

**Redis Key Pattern**: `service:heartbeat:{serviceId}:{appId}` (90 second TTL)

### SmartRestartManager.cs

Intelligent service restart logic - **only restart when truly necessary**:

| Status | Duration | Action |
|--------|----------|--------|
| Healthy | - | No restart |
| Degraded | < 5 minutes | No restart (transient) |
| Degraded | > 5 minutes | Restart recommended |
| Unavailable | - | Restart needed |

---

## Interface-Based Architecture

All helper classes implement interfaces to enable unit testing with Moq:

### Interfaces

| Interface | Implementation | Purpose |
|-----------|---------------|---------|
| `IOrchestratorRedisManager` | `OrchestratorRedisManager` | Direct Redis connection for heartbeats |
| `IOrchestratorEventManager` | `OrchestratorEventManager` | Direct RabbitMQ pub/sub |
| `IServiceHealthMonitor` | `ServiceHealthMonitor` | Health aggregation and restart recommendations |
| `ISmartRestartManager` | `SmartRestartManager` | Docker container lifecycle management |
| `IBackendDetector` | `BackendDetector` | Multi-backend detection and orchestrator creation |

### OrchestratorService Constructor

```csharp
public OrchestratorService(
    DaprClient daprClient,
    ILogger<OrchestratorService> logger,
    OrchestratorServiceConfiguration configuration,
    IOrchestratorRedisManager redisManager,
    IOrchestratorEventManager eventManager,
    IServiceHealthMonitor healthMonitor,
    ISmartRestartManager restartManager,
    IBackendDetector backendDetector)
```

All dependencies are injected via interfaces, enabling full mocking in unit tests.

---

## Unit Testing

The orchestrator has comprehensive unit tests in `lib-orchestrator.tests/OrchestratorServiceTests.cs`.

### Test Coverage (28 Tests)

| Category | Tests | Description |
|----------|-------|-------------|
| Constructor Validation | 8 | Null argument checks for all dependencies |
| GetInfrastructureHealthAsync | 3 | Success, degraded, and unhealthy scenarios |
| GetServicesHealthAsync | 1 | Service health report aggregation |
| RestartServiceAsync | 2 | Forced restart and health-based restart |
| ShouldRestartServiceAsync | 1 | Restart recommendation logic |
| GetBackendsAsync | 1 | Backend detection |
| GetStatusAsync | 2 | Status response building |
| GetContainerStatusAsync | 2 | Container health and history |
| ServiceHealthMonitor | 5 | Degradation threshold logic |
| Configuration | 2 | Configuration binding tests |

### Running Tests

```bash
# Run orchestrator tests only
dotnet test lib-orchestrator.tests/

# Run with verbose output
dotnet test lib-orchestrator.tests/ -v n

# Run as part of full test suite
make test
```

### Test Architecture

Tests use Moq to mock all interface dependencies:

```csharp
[Fact]
public async Task GetInfrastructureHealthAsync_ReturnsHealthyStatus_WhenAllComponentsHealthy()
{
    // Arrange
    _mockRedisManager
        .Setup(x => x.CheckHealthAsync())
        .ReturnsAsync((true, "OK", TimeSpan.FromMilliseconds(5)));

    _mockEventManager
        .Setup(x => x.CheckHealth())
        .Returns((true, "Connected"));

    // Act
    var (status, response) = await _service.GetInfrastructureHealthAsync();

    // Assert
    Assert.Equal(StatusCodes.Status200OK, status);
    Assert.True(response?.IsHealthy);
}
```

---

## Service Mapping Architecture

### Current State

The orchestrator monitors service heartbeats and manages container topology. However, there's a **critical gap** in the service mapping event flow.

### ServiceAppMappingResolver

Located in `bannou-service/Services/ServiceAppMappingResolver.cs`, this class resolves service names to Dapr app-ids:

```csharp
public class ServiceAppMappingResolver : IServiceAppMappingResolver
{
    private const string DEFAULT_APP_ID = "bannou"; // Omnipotent default
    private readonly ConcurrentDictionary<string, string> _serviceMappings;

    public string GetAppIdForService(string serviceName)
    {
        return _serviceMappings.GetValueOrDefault(serviceName, DEFAULT_APP_ID);
    }

    public async Task UpdateServiceMapping(string serviceName, string appId)
    {
        _serviceMappings.AddOrUpdate(serviceName, appId, (key, old) => appId);
    }
}
```

### The Gap: Missing Service Mapping Events

**Problem**: `ServiceAppMappingResolver` has methods to update mappings from RabbitMQ events, but **no component currently publishes these events**.

**Expected Flow**:
```
1. Orchestrator deploys/moves service to new container
2. Orchestrator publishes ServiceMappingEvent to RabbitMQ
3. All bannou instances consume event
4. ServiceAppMappingResolver updates mappings
5. Future service calls route to correct app-id
```

**Current State**:
- ✅ ServiceAppMappingResolver can consume events
- ✅ ServiceAppMappingResolver can update mappings
- ❌ OrchestratorEventManager doesn't publish service mapping events
- ❌ No `bannou-service-mappings` exchange defined

### Required Implementation

Add to `OrchestratorEventManager`:

```csharp
// New exchange for service mappings
private const string SERVICE_MAPPINGS_EXCHANGE = "bannou-service-mappings";

// Publish when topology changes
public async Task PublishServiceMappingEventAsync(ServiceMappingEvent mappingEvent)
{
    var json = JsonSerializer.Serialize(mappingEvent);
    var body = Encoding.UTF8.GetBytes(json);

    _channel.BasicPublish(
        exchange: SERVICE_MAPPINGS_EXCHANGE,
        routingKey: mappingEvent.ServiceName,
        body: body);
}
```

**ServiceMappingEvent Model**:
```yaml
ServiceMappingEvent:
  properties:
    eventId: string (uuid)
    timestamp: string (date-time)
    serviceName: string      # e.g., "auth", "accounts"
    appId: string            # e.g., "bannou-auth", "npc-omega"
    action: enum [register, unregister, update]
    region: string           # Optional: for geographic routing
    priority: integer        # Optional: for load balancing
```

### When to Publish Service Mapping Events

| Orchestrator Action | Mapping Event |
|---------------------|---------------|
| Deploy service to new container | `register` with new app-id |
| Move service between containers | `update` with new app-id |
| Teardown container with service | `unregister` to fall back to default |
| Scale service replicas | No event (same app-id) |

---

## API Design

The orchestrator exposes a comprehensive REST API for all environment management operations. Full schema available in `schemas/orchestrator-api.yaml`.

### Health Monitoring Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/orchestrator/health/infrastructure` | GET | Check Redis, RabbitMQ, Dapr placement health |
| `/orchestrator/health/services` | GET | Get all service health from heartbeats |

### Service Management Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/orchestrator/services/restart` | POST | Restart service with optional config |
| `/orchestrator/services/should-restart` | POST | Get restart recommendation |

### Container Management Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/orchestrator/containers/{appName}/request-restart` | POST | Self-service restart request (plugins call this) |
| `/orchestrator/containers/{appName}/status` | GET | Get container health and restart history |

**Request-Restart Request Body**:
```json
{
  "reason": "configuration_change",
  "priority": "graceful",
  "shutdownGracePeriod": 30
}
```

**Priority Values**: `graceful` (rolling, wait for healthy), `immediate` (rolling, no drain wait), `force` (simultaneous kill/start)

### Configuration Management Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/orchestrator/config/rollback` | POST | Rollback to previous configuration |
| `/orchestrator/config/version` | GET | Get current config version and metadata |

**Rollback Request Body**:
```json
{
  "reason": "auth.jwt_secret broke authentication"
}
```

**Note**: Rollback swaps `currentConfig` ↔ `previousConfig` and publishes ConfigurationChangedEvent with reverted keys.

### Environment Management Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/orchestrator/backends` | GET | Detect available container backends |
| `/orchestrator/presets` | GET | List deployment presets |
| `/orchestrator/deploy` | POST | Deploy/update environment |
| `/orchestrator/status` | GET | Get current environment status |
| `/orchestrator/teardown` | POST | Tear down environment |
| `/orchestrator/clean` | POST | Clean unused Docker resources |
| `/orchestrator/logs` | GET | Get service/container logs |
| `/orchestrator/topology` | POST | Update topology without full redeploy |

### Test Orchestration Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/orchestrator/tests/run` | POST | Execute test batch via API |

---

## Makefile Commands

The orchestrator has dedicated Makefile targets for standalone operation and API testing.

### Starting the Orchestrator

```bash
# Start orchestrator in standalone mode with infrastructure
make up-orchestrator

# Stop orchestrator stack
make down-orchestrator

# View orchestrator logs
make logs-orchestrator
```

### Testing Orchestrator APIs

```bash
# Test all orchestrator APIs
make test-orchestrator

# Individual API tests
make orchestrator-status      # GET /orchestrator/status
make orchestrator-health      # GET /orchestrator/health
make orchestrator-services    # GET /orchestrator/services
make orchestrator-backends    # GET /orchestrator/backends
make orchestrator-containers  # GET /orchestrator/containers
```

### Docker Compose Configuration

The orchestrator uses `provisioning/docker-compose.orchestrator.yml`:

```yaml
services:
  bannou-orchestrator:
    environment:
      # Only orchestrator service enabled
      - ORCHESTRATOR_SERVICE_ENABLED=true
      - ACCOUNTS_SERVICE_ENABLED=false
      # Direct connections (NOT Dapr)
      - BANNOU_RedisConnectionString=bannou-redis:6379
      - BANNOU_RabbitMqConnectionString=amqp://guest:guest@rabbitmq:5672
      - BANNOU_DockerHost=unix:///var/run/docker.sock
    volumes:
      - "/var/run/docker.sock:/var/run/docker.sock"
    ports:
      - "8090:80"   # Orchestrator HTTP API
      - "8493:443"  # Orchestrator HTTPS API
```

### API Endpoints Summary

After running `make up-orchestrator`, the following endpoints are available at `http://localhost:8090`:

| Endpoint | Description |
|----------|-------------|
| `GET /orchestrator/status` | Overall orchestrator status |
| `GET /orchestrator/health` | Infrastructure health (Redis, RabbitMQ, Dapr) |
| `GET /orchestrator/services` | Service health from heartbeats |
| `GET /orchestrator/backends` | Available container backends |
| `GET /orchestrator/containers` | Container status |
| `POST /orchestrator/restart` | Restart a service |

---

## Deployment Presets

Presets define reusable service configurations for specific use cases. They specify which services run on which containers, with what configuration.

### Built-in Presets

```yaml
# local-development: All services in single container
nodes:
  - name: bannou-main
    services: [accounts, auth, behavior, connect, permissions, website, testing]
    daprEnabled: true
    daprAppId: bannou

# local-testing: Infrastructure + test services
nodes:
  - name: bannou-main
    services: [accounts, auth, permissions, testing]
    daprEnabled: true
infrastructure:
  redis:
    enabled: true
  rabbitmq:
    enabled: true
  mysql:
    enabled: true

# split-auth-accounts: Dedicated auth container
nodes:
  - name: bannou-auth
    services: [auth]
    daprAppId: bannou-auth
  - name: bannou-accounts
    services: [accounts, permissions]
    daprAppId: bannou-accounts
  - name: bannou-main
    services: [behavior, connect, website]
    daprAppId: bannou

# distributed-npc: NPC processing across nodes
nodes:
  - name: bannou-main
    services: [accounts, auth, connect, permissions, website]
  - name: bannou-npc-omega
    services: [behavior]
    replicas: 3
    daprAppId: npc-omega
  - name: bannou-npc-arcadia
    services: [behavior]
    replicas: 2
    daprAppId: npc-arcadia
```

---

## Service Topology Management

The orchestrator enables dynamic service distribution across containers/pods without full redeployment.

### Assembly Loading Pattern

Bannou uses environment variables to control which services load in each container:

```bash
# All services enabled (local development)
ACCOUNTS_SERVICE_ENABLED=true
AUTH_SERVICE_ENABLED=true
BEHAVIOR_SERVICE_ENABLED=true
CONNECT_SERVICE_ENABLED=true
PERMISSIONS_SERVICE_ENABLED=true
WEBSITE_SERVICE_ENABLED=true

# Auth-only container
ACCOUNTS_SERVICE_ENABLED=false
AUTH_SERVICE_ENABLED=true
BEHAVIOR_SERVICE_ENABLED=false
...
```

### Live Topology Changes

The `/orchestrator/topology` endpoint supports runtime topology changes:

```json
{
  "changes": [
    {
      "action": "move-service",
      "nodeName": "bannou-auth",
      "services": ["auth"]
    },
    {
      "action": "add-node",
      "nodeConfig": {
        "name": "bannou-npc-01",
        "services": ["behavior"],
        "replicas": 2,
        "daprAppId": "npc-omega"
      }
    }
  ],
  "mode": "graceful"
}
```

---

## Health Monitoring

### Infrastructure Health

Direct connections to check core components with exponential backoff retry:

```csharp
public async Task<InfrastructureHealthResponse> GetInfrastructureHealthAsync()
{
    // Redis - direct ping (StackExchange.Redis)
    var (redisHealthy, redisMessage, redisPing) = await _redisManager.CheckHealthAsync();

    // RabbitMQ - connection check (RabbitMQ.Client)
    var (rabbitHealthy, rabbitMessage) = _eventManager.CheckHealth();

    // Dapr Placement - health endpoint
    await _daprClient.CheckHealthAsync(cancellationToken);
}
```

### Service Health via Heartbeats

Services publish heartbeats to Redis. The orchestrator monitors these using the existing `ServiceHeartbeatEvent` schema:

- Aggregates health across all service instances
- Determines worst-case status for multi-replica services
- Provides restart recommendations based on degradation duration

---

## Secrets and Configuration Management

### The "Two Environments" Problem

When the orchestrator manages container lifecycle, we must distinguish between:

1. **Deployment Environment** (dev/staging/production): Determines WHICH secrets and configuration values to use
2. **Service Topology**: Determines HOW services are distributed across containers

### Design Principles

1. **GitHub as Primary Source**: GitHub Environments, Repository Secrets, and Organization Secrets are the source of truth for all non-development deployments
2. **`.env` Files for Development Only**: Local `.env` files should NEVER contain production/staging secrets
3. **Secret References, Not Values**: Orchestrator stores references to secrets, not the actual values
4. **30-Minute Propagation Target**: Configuration changes should propagate within 30 minutes
5. **Service Restart Required**: Hot-reload of secrets is generally NOT supported—services need restart

### GitHub Secrets Architecture

GitHub provides three levels of secrets with strict precedence:

| Level | Scope | Precedence | Best For |
|-------|-------|------------|----------|
| **Environment** | Per-environment (staging, production) | Highest | Production credentials |
| **Repository** | All workflows in repo | Medium | Project-specific API keys |
| **Organization** | All repos in org | Lowest | Shared infrastructure secrets |

**Secret Precedence**: Environment > Repository > Organization (higher takes priority if same name)

#### GitHub Environments Feature

GitHub Environments provide:
- **Environment-Specific Secrets**: Different values for staging vs production
- **Deployment Protection Rules**: Require approvals before accessing secrets
- **Wait Timers**: Delay deployments for review periods
- **Branch Restrictions**: Only specific branches can deploy to environment

**Requirements**: GitHub Pro, Team, or Enterprise for private repositories

#### GitHub Secrets API Access

The orchestrator can use the [GitHub REST API](https://docs.github.com/en/rest/actions/secrets) to manage secrets:

```bash
# List organization secrets
GET /orgs/{org}/actions/secrets

# Get repository public key (needed for encryption)
GET /repos/{owner}/{repo}/actions/secrets/public-key

# Create/update repository secret (value must be LibSodium encrypted)
PUT /repos/{owner}/{repo}/actions/secrets/{secret_name}

# Create/update environment secret
PUT /repos/{owner}/{repo}/environments/{environment_name}/secrets/{secret_name}
```

**Critical Limitation**: GitHub API **cannot read secret values**—only secret names. Values must be encrypted with LibSodium before upload. This means:

- **Write-Only from Orchestrator**: Orchestrator can SET secrets but cannot GET them
- **CI/CD Access**: GitHub Actions can read secrets via `${{ secrets.XYZ }}` context
- **Deployment Pattern**: CI pipeline reads secrets and passes to orchestrator, OR orchestrator uses secondary storage (Azure Key Vault) for read access

### Azure Key Vault Integration

For production environments, Azure Key Vault provides:

- **Centralized Secret Storage**: Single source of truth across multiple repos
- **RBAC Access Control**: Fine-grained permissions via Azure AD
- **Audit Logging**: Track all secret access attempts
- **Secret Rotation**: Built-in versioning and rotation support
- **Managed Identity**: No passwords needed for Azure-hosted services

#### Dapr Secret Store Component

Dapr provides native Azure Key Vault integration via secret store component:

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: bannou-secrets
spec:
  type: secretstores.azure.keyvault
  version: v1
  metadata:
  - name: vaultName
    value: "bannou-secrets-prod"
  - name: azureTenantId
    value: "[tenant_id]"
  - name: azureClientId
    value: "[managed_identity_client_id]"
```

**For Orchestrator (Standalone)**: Since orchestrator doesn't use Dapr, it accesses Azure Key Vault directly via Azure SDK:

```csharp
var client = new SecretClient(
    new Uri($"https://{vaultName}.vault.azure.net"),
    new DefaultAzureCredential());
var secret = await client.GetSecretAsync(secretName);
```

### Secret Provider Architecture

**Design Pattern**: The orchestrator holds **references** to secrets, not values. Resolution happens at deployment time through provider adapters.

```yaml
# Environment configuration with secret references
environment:
  production:
    AUTH_JWT_SECRET: "${secrets:azure-kv/bannou-prod/auth-jwt-secret}"
    ACCOUNT_DB_PASSWORD: "${secrets:azure-kv/bannou-prod/db-password}"
    NUGET_API_KEY: "${secrets:github-org/NUGET_API_KEY}"
  staging:
    AUTH_JWT_SECRET: "${secrets:azure-kv/bannou-staging/auth-jwt-secret}"
    ACCOUNT_DB_PASSWORD: "${secrets:azure-kv/bannou-staging/db-password}"
  development:
    AUTH_JWT_SECRET: "${env:AUTH_JWT_SECRET}"  # From local .env
    ACCOUNT_DB_PASSWORD: "${env:ACCOUNT_DB_PASSWORD}"
```

### Secrets Providers

| Provider | Use Case | Resolution Method | Read/Write |
|----------|----------|-------------------|------------|
| `azure-kv` | Production secrets | Azure SDK with managed identity | Read/Write |
| `github-env` | GitHub Environments | CI pipeline passes to orchestrator | Read (via CI) |
| `github-repo` | Repository secrets | CI pipeline passes to orchestrator | Read (via CI) |
| `github-org` | Organization secrets | CI pipeline passes to orchestrator | Read (via CI) |
| `portainer` | Multi-host Docker | Portainer API | Read/Write |
| `k8s-secrets` | Kubernetes | K8s API with service account | Read/Write |
| `env` | Development only | Local .env files | Read |

### External Secrets Operator (Kubernetes)

For Kubernetes deployments, [External Secrets Operator (ESO)](https://external-secrets.io/) syncs secrets from external stores:

```yaml
apiVersion: external-secrets.io/v1beta1
kind: ExternalSecret
metadata:
  name: bannou-secrets
spec:
  refreshInterval: "15m"  # Sync every 15 minutes
  secretStoreRef:
    kind: ClusterSecretStore
    name: azure-keyvault
  target:
    name: bannou-secrets
    creationPolicy: Owner
  data:
  - secretKey: AUTH_JWT_SECRET
    remoteRef:
      key: auth-jwt-secret
```

**ESO Capabilities**:
- Sync secrets from Azure Key Vault, AWS Secrets Manager, HashiCorp Vault
- Automatic refresh on configured interval
- Kubernetes Secret updates trigger pod restarts via [Stakater Reloader](https://github.com/stakater/Reloader)

### Secret Update & Propagation

**Critical Limitation**: Container secrets cannot be hot-reloaded. Options:

1. **Volume-Mounted Secrets (K8s)**: Auto-update when secret changes, but application must watch file changes
2. **Environment Variables**: Require container restart to pick up changes
3. **Stakater Reloader**: Watches ConfigMaps/Secrets and triggers rolling restarts

**Recommended Pattern for 30-Minute Propagation**:

```
Secret Update → Azure Key Vault/GitHub → ESO/Orchestrator sync (15min interval)
    → Kubernetes Secret updated → Reloader triggers rolling restart → New value active

Total time: ~15-20 minutes (within 30-minute target)
```

### Portainer Integration for Multi-Cloud Sync

Portainer provides GitOps-style stack management for non-Kubernetes deployments:

- **Stack Definitions in Git**: Version-controlled docker-compose files
- **Webhook-Triggered Updates**: Git push triggers stack redeployment
- **Environment Variables via API**: Programmatic secret management
- **Multi-Host Sync**: Same secrets across multiple cloud hosts

```bash
# Portainer API for environment management
PUT /api/stacks/{stackId}
{
  "env": [
    {"name": "AUTH_JWT_SECRET", "value": "${resolved_secret}"},
    {"name": "ACCOUNT_DB_PASSWORD", "value": "${resolved_secret}"}
  ]
}
```

### Implementation Phases

**Phase 1: Development (.env files)**
- Current state, working
- `.env` files for local development only

**Phase 2: Azure Key Vault Integration**
- Orchestrator reads production secrets from Azure Key Vault
- CI pipeline reads GitHub secrets, passes to orchestrator
- Services restart when secrets change

**Phase 3: GitHub Environments + Azure Key Vault Hybrid**
- GitHub Environments for deployment approvals and branch protection
- Azure Key Vault as readable secret store for orchestrator
- CI pipeline orchestrates deployment through GitHub Environments

**Phase 4: Kubernetes External Secrets Operator**
- ESO syncs secrets from Azure Key Vault to Kubernetes
- Stakater Reloader triggers rolling restarts
- Full automation with 15-minute sync intervals

---

## Configuration Update Event System

When secrets or configuration values change, containers may need to restart to apply the new values. The orchestrator uses a **self-service restart pattern** where plugins decide if they care about changes and request restarts themselves.

### Terminology: Containers vs Plugins

**Critical Distinction**:

| Term | Meaning | Example |
|------|---------|---------|
| **Plugin** | .NET service type/class | `AuthService`, `AccountsService`, `BehaviorService` |
| **Container** | Dapr app, runs multiple plugins | "bannou", "orchestrator", "npc-omega" |
| **App Name** | Container's Dapr identity | Used for service discovery and restart requests |

```
Container "bannou" (app name = "bannou")
├── AuthService (plugin)
├── AccountsService (plugin)
├── BehaviorService (plugin)
├── ConnectService (plugin)
└── PermissionsService (plugin)

Container "npc-omega" (app name = "npc-omega")
├── BehaviorService (plugin)
└── CharacterService (plugin)
```

**Bannou Modes**:
- **"bannou" mode**: All plugins in one monolithic container (local development)
- **"orchestrator" mode**: Only orchestrator plugin (management layer)
- **Custom configurations**: Any combination of plugins per container

### Self-Service Restart Pattern

The orchestrator **does not track** which plugins care about which configuration keys. Instead:

1. Orchestrator broadcasts "these keys changed" to all containers
2. Plugins within each container decide if they care
3. Plugins that care request their container's restart via API
4. Orchestrator performs rolling restart of requested containers

**Benefits**:
- **Loose coupling**: Orchestrator doesn't maintain plugin→key mappings
- **Self-describing**: Each plugin knows its own dependencies
- **Minimal blast radius**: Only affected containers restart
- **Scalable**: New plugins just subscribe to events

### Configuration Changed Event

```yaml
ConfigurationChangedEvent:
  type: object
  description: |
    Published by orchestrator when configuration or secrets change.
    All containers receive this event. Plugins decide if they care
    based on the changedKeys prefixes.
  required:
    - eventId
    - timestamp
    - configVersion
    - changedKeys
  properties:
    eventId:
      type: string
      format: uuid
    timestamp:
      type: string
      format: date-time
    configVersion:
      type: integer
      description: Monotonically increasing version number
    changedKeys:
      type: array
      items:
        type: string
      description: |
        Configuration keys that changed (not values for security).
        Key prefixes indicate scope:
        - "auth.*" - Authentication-related
        - "database.*" - Database connections
        - "dapr.*" - Dapr components (typically global impact)
        - "connect.*" - WebSocket/connection settings
      examples:
        - "auth.jwt_secret"
        - "auth.token_expiry"
        - "database.password"
        - "dapr.pubsub.host"
```

### Request-Restart Endpoint

Plugins request restart of their own container via API:

```yaml
POST /orchestrator/containers/{appName}/request-restart
{
  "reason": "configuration_change",
  "priority": "graceful",
  "shutdownGracePeriod": 30
}
```

**Parameters**:

| Field | Type | Description |
|-------|------|-------------|
| `appName` | path | Container's Dapr app name (e.g., "bannou", "npc-omega") |
| `reason` | string | Why restart is needed (for logging/auditing) |
| `priority` | enum | Restart urgency level |
| `shutdownGracePeriod` | integer | Seconds to allow graceful shutdown before force-kill |

**Priority Levels**:

| Priority | Behavior |
|----------|----------|
| `graceful` | Rolling update: replace instances one at a time, wait for healthy before next |
| `immediate` | Rolling update but don't wait for connection drain, cycle quickly |
| `force` | Kill all instances simultaneously, start new ones (causes downtime) |

**Response**:
```yaml
{
  "accepted": true,
  "appName": "bannou",
  "scheduledFor": "2025-11-29T17:45:00Z",
  "currentInstances": 3,
  "restartStrategy": "rolling"
}
```

### Orchestrator Configuration State

Orchestrator maintains previous configuration for delta detection and rollback:

```yaml
OrchestratorConfigState:
  currentConfig:
    auth.jwt_secret: "[current_value]"
    auth.token_expiry: "3600"
    database.password: "[current_value]"
  previousConfig:
    auth.jwt_secret: "[previous_value]"
    auth.token_expiry: "3600"
    database.password: "[previous_value]"
  configVersion: 42
  lastUpdated: "2025-11-29T17:30:00Z"
```

**Capabilities**:
- **Delta detection**: Compare current vs previous to determine changedKeys
- **Quick rollback**: Swap configs without waiting for CI redeploy
- **Version tracking**: Monotonic version for consistency

### Rollback Endpoint

```yaml
POST /orchestrator/config/rollback
{
  "reason": "auth.jwt_secret broke authentication"
}
```

Orchestrator swaps `currentConfig` ↔ `previousConfig` and publishes ConfigurationChangedEvent with the reverted keys. Services can then request restart to pick up the rolled-back values.

**Note**: This is a quick fix. GitHub secrets should still be corrected to prevent re-breaking on next orchestrator deploy.

### Complete Configuration Update Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    CONFIGURATION UPDATE FLOW                             │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  1. GitHub Secret Updated (e.g., AUTH_JWT_SECRET)                       │
│         │                                                                │
│         ▼                                                                │
│  2. CI Triggered → Deploys orchestrator container only                  │
│     (2-5 second unavailability, no user-facing impact)                  │
│         │                                                                │
│         ▼                                                                │
│  3. Orchestrator starts with new secrets in environment                 │
│     Compares new env vars to previousConfig                             │
│     Detects: auth.jwt_secret changed                                    │
│         │                                                                │
│         ▼                                                                │
│  4. Orchestrator publishes via RabbitMQ:                                │
│     ConfigurationChangedEvent {                                          │
│       configVersion: 43,                                                 │
│       changedKeys: ["auth.jwt_secret"]                                  │
│     }                                                                    │
│         │                                                                │
│    ┌────┴────────────────┬─────────────────────┐                        │
│    ▼                     ▼                     ▼                        │
│  Container "bannou"    Container "npc-omega"  Container "economy"       │
│  ┌─────────────────┐   ┌─────────────────┐   ┌─────────────────┐       │
│  │ AuthService:    │   │ BehaviorService:│   │ TradingService: │       │
│  │ "I care about   │   │ "Don't care     │   │ "Don't care     │       │
│  │  auth.*"        │   │  about auth.*"  │   │  about auth.*"  │       │
│  │                 │   │                 │   │                 │       │
│  │ ConnectService: │   │ (no action)     │   │ (no action)     │       │
│  │ "I care about   │   └─────────────────┘   └─────────────────┘       │
│  │  auth.*"        │                                                    │
│  └────────┬────────┘                                                    │
│           │                                                              │
│           ▼                                                              │
│  5. AuthService calls:                                                  │
│     POST /orchestrator/containers/bannou/request-restart                │
│     { "reason": "configuration_change", "priority": "graceful" }        │
│         │                                                                │
│         ▼                                                                │
│  6. Orchestrator performs rolling restart of "bannou" container         │
│     - Stops instance 1, starts new instance 1 with new config           │
│     - Waits for healthy, then instance 2, etc.                          │
│         │                                                                │
│         ▼                                                                │
│  7. Orchestrator saves: currentConfig → previousConfig                  │
│     (Ready for next delta detection or rollback)                        │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### Plugin Implementation Pattern

Each plugin that cares about configuration changes implements a handler:

```csharp
public class AuthService : IConfigurationChangeHandler
{
    private static readonly string[] CaredKeys = { "auth.", "jwt." };

    public async Task HandleConfigurationChangedAsync(ConfigurationChangedEvent evt)
    {
        // Check if any changed keys match our prefixes
        var relevant = evt.ChangedKeys.Any(key =>
            CaredKeys.Any(prefix => key.StartsWith(prefix)));

        if (relevant)
        {
            // Request restart of our container
            await _orchestratorClient.RequestRestartAsync(
                appName: _daprAppId,  // e.g., "bannou"
                reason: "configuration_change",
                priority: RestartPriority.Graceful,
                shutdownGracePeriod: TimeSpan.FromSeconds(30));
        }
    }
}
```

### Why CI Only Deploys Orchestrator

Since GitHub API cannot read secret values (only names), secrets must be passed to orchestrator at deploy time. However, this has minimal impact:

1. **Orchestrator is not user-facing** - 2-5 second restart doesn't affect players
2. **State preserved in Redis** - Orchestrator catches up via RabbitMQ events after restart
3. **Other containers unaffected** - CI doesn't touch game services directly
4. **Deploy frequency unlimited** - Can update secrets as often as needed

This architecture means **configuration updates don't cause user-facing downtime** - only the orchestrator restarts, and it recovers transparently.

---

## CI/CD Integration Strategy

### CI Should Continue Using Docker Compose

**Key Decision**: GitHub Actions CI will continue using `docker-compose` directly for simplicity. The orchestrator adds complexity that isn't needed for CI:

- CI tests all critical pieces without orchestrator overhead
- Continued orchestrator development won't break CI process
- Edge tests independently validate orchestrator functionality
- Orchestrator is testable locally in development

### Current CI Pipeline (10 Steps)

```yaml
# .github/workflows/ci.integration.yml
1. Generate Services (NSwag)
2. Generate Client SDK
3. Build All Projects
4. Re-Generate (conflict detection)
5. Re-Generate SDK (consistency)
6. Unit Tests
7. Infrastructure Tests (Docker Compose)
8. HTTP Integration Tests
9. WebSocket Tests - Backwards Compatibility
10. WebSocket Tests - Forward Compatibility
```

### Future: Orchestrator Edge Testing

Edge tests can validate orchestrator independently:

```bash
# Local orchestrator testing
make orchestrator-start
curl http://localhost:8090/orchestrator/backends
curl http://localhost:8090/orchestrator/presets
curl -X POST http://localhost:8090/orchestrator/deploy -d '{"preset":"local-testing"}'
curl http://localhost:8090/orchestrator/status
make orchestrator-stop
```

---

## Docker Socket Security

### 🚨 CRITICAL SECURITY RISK

Mounting `/var/run/docker.sock` inside a container gives **root privileges equivalent to the host**. An attacker who breaks out of the container will have unrestricted root access to your host.

From OWASP Docker Security Cheat Sheet:
> "Do not expose /var/run/docker.sock to other containers. Remember that mounting the socket **read-only is not a solution** but only makes it harder to exploit."

### Dual Implementation Pattern

**Development (Docker Compose)**:
```yaml
bannou-orchestrator:
  volumes:
    - /var/run/docker.sock:/var/run/docker.sock  # Development only
  labels:
    - "security.warning=Docker socket mounted - DEVELOPMENT ONLY"
```

**Production (Kubernetes)**:
```csharp
public class KubernetesOrchestrator : IContainerOrchestrator
{
    private readonly IKubernetes _client;
    // No socket mounting - uses Kubernetes API with RBAC
}
```

### Production Alternatives

1. **Docker Rootless Mode**: Both daemon and containers run as unprivileged user
2. **TLS Remote Connections**: Network-based communication with certificate auth
3. **Kubernetes API**: Native K8s orchestration with RBAC and service accounts
4. **Podman**: Rootless by design, compatible with Docker CLI

---

## Implementation Status

### Phase 1: Core Infrastructure ✅ Complete

- [x] OrchestratorService with direct Redis/RabbitMQ connections
- [x] ServiceHealthMonitor with heartbeat aggregation
- [x] SmartRestartManager with Docker.DotNet integration
- [x] OrchestratorEventManager for RabbitMQ pub/sub
- [x] Basic API endpoints (health, restart, should-restart)
- [x] **Interface-based architecture** for all helper classes
- [x] **28 unit tests passing** with full Moq coverage
- [x] **Makefile commands** for standalone orchestrator operation

### Phase 2: Environment Management ✅ 95% Complete

- [x] API schema design (orchestrator-api.yaml v2.2.0)
- [x] Backend detection framework with 4-backend support
- [x] Docker Compose orchestrator (DockerComposeOrchestrator.cs ~1,347 lines)
- [x] Docker Swarm orchestrator (DockerSwarmOrchestrator.cs)
- [x] Portainer orchestrator (PortainerOrchestrator.cs)
- [x] Kubernetes orchestrator (KubernetesOrchestrator.cs)
- [x] BackendDetector with priority-based selection
- [x] **Deployment preset system** (7 presets in provisioning/orchestrator/presets/)
- [x] **Deploy implementation** with heartbeat-based validation
- [x] **Teardown implementation** with graceful container removal
- [x] **Status implementation** returning current topology
- [x] **Standalone Dapr sidecar architecture** (mDNS, not Consul)
- [x] **ExtraHosts IP injection** for Docker DNS limitations
- [ ] Log streaming (low priority)
- [ ] CleanAsync and RollbackConfigurationAsync (low priority)

### Phase 3: Service Mapping Events ✅ Complete

- [x] ServiceAppMappingResolver can consume events
- [x] ServiceAppMappingResolver can update mappings dynamically
- [x] **Service mapping events** via Dapr pub/sub
- [x] **Redis heartbeat system** for NGINX routing integration
- [x] **Publish on topology changes** (deploy, move, teardown)

### Phase 4: Secrets Integration 📋 Planned (Research Complete)

- [x] Research GitHub Environments/Secrets API integration
- [x] Research Azure Key Vault patterns
- [x] Research External Secrets Operator (K8s)
- [x] Research Stakater Reloader for secret propagation
- [ ] Design secrets provider interface
- [ ] Implement secret reference resolution
- [ ] Azure Key Vault integration
- [ ] Portainer secrets sync
- [ ] Configuration update event system

### Phase 5: Production Readiness 📋 Planned

- [ ] TLS for Docker API connections
- [ ] Authentication for orchestrator API
- [ ] Audit logging
- [ ] Rate limiting
- [ ] Prometheus metrics

---

## Critical Unimplemented TODOs

The following TODOs in `OrchestratorService.cs` represent core functionality that needs implementation:

| Location | TODO | Priority | Blocker For |
|----------|------|----------|-------------|
| Line 190 | Implement test execution via API calls to test services | Medium | Test orchestration feature |
| Line 347 | Implement actual deployment via container orchestrator | **HIGH** | Deploy endpoint |
| Line 421 | Implement actual teardown via container orchestrator | **HIGH** | Teardown endpoint |
| Line 453 | Implement cleanup via container orchestrator | Medium | Clean endpoint |
| Line 544 | Implement topology update via container orchestrator | **HIGH** | Live topology changes |
| Line 632 | Implement configuration rollback | Low | Rollback endpoint |

### Deployment TODO (Line 347)

```csharp
// TODO: Implement actual deployment via container orchestrator
// Currently returns success without deploying
```

**Required Implementation**:
1. Get appropriate orchestrator from `_backendDetector`
2. Parse deployment preset from request
3. Create/update containers with service configuration
4. Wait for health checks
5. Publish ServiceMappingEvents for new services

### Teardown TODO (Line 421)

```csharp
// TODO: Implement actual teardown via container orchestrator
// Currently returns success without tearing down
```

**Required Implementation**:
1. Get appropriate orchestrator from `_backendDetector`
2. Stop containers gracefully
3. Remove containers if requested
4. Publish ServiceMappingEvents to unregister services

### Topology Update TODO (Line 544)

```csharp
// TODO: Implement topology update via container orchestrator
// Currently returns success without updating topology
```

**Required Implementation**:
1. Parse topology change requests (move-service, add-node, remove-node)
2. Coordinate container lifecycle changes
3. **Publish ServiceMappingEvents** for any service moves
4. Wait for health verification

---

## Edge-Tester Blockers

The edge-tester (`edge-tester/`) validates WebSocket binary protocol functionality. Current blockers prevent full test execution.

### Current Blockers

| Blocker | Description | Required Fix |
|---------|-------------|--------------|
| **Auth Service Dependency** | Edge-tester requires successful login before WebSocket tests | Fix auth registration/login in http-tester first |
| **Connect WebSocket Handler** | Binary protocol routing not implemented | Implement WebSocket message handler in Connect service |
| **Service Discovery Response** | Server doesn't return capability discovery | Implement API discovery via WebSocket |
| **Channel-Based Routing** | Binary protocol uses channels 0/1/2 but server ignores channel field | Add channel-based message routing |

### Edge-Tester Test Suite

Located in `edge-tester/Tests/ConnectWebSocketTestHandler.cs`:

| Test | Description | Status |
|------|-------------|--------|
| WebSocket - Upgrade | JWT authentication for WebSocket connections | ⚠️ Blocked by auth |
| WebSocket - Binary Protocol | 31-byte header message exchange | ⚠️ Blocked by Connect handler |
| WebSocket - Service Discovery | API discovery via binary protocol | ⚠️ Blocked by implementation |
| WebSocket - Internal API Proxy | HTTP proxy through WebSocket | ⚠️ Blocked by Connect handler |

### Required Connect Service Work

1. **WebSocket Connection Handler**: Accept connections, validate JWT, create session
2. **Binary Message Parser**: Parse 31-byte header, extract serviceGuid
3. **Message Router**: Route messages based on serviceGuid to correct backend
4. **Capability Updates**: Push permission changes via RabbitMQ to connected clients
5. **Channel Multiplexing**: Handle channels 0 (discovery), 1 (API), 2 (proxy)

### Edge-Tester Flow

```
1. HTTP: Register account
2. HTTP: Login → get JWT
3. WebSocket: Connect with JWT in Authorization header
4. WebSocket: Send binary protocol message
5. WebSocket: Receive binary protocol response
6. WebSocket: Test API discovery
7. WebSocket: Test API proxy
```

Steps 1-2 currently blocked by auth service issues in http-tester.

---

## Risk Assessment

### Risk 1: Service Mapping Event Gap (HIGH)

**Impact**: Distributed service deployments will not route correctly
**Likelihood**: Certain (gap exists today)
**Current State**: ServiceAppMappingResolver expects events but no publisher exists
**Mitigation**:
- Implement `PublishServiceMappingEventAsync` in OrchestratorEventManager
- Add `bannou-service-mappings` exchange to RabbitMQ
- Integrate with deploy/teardown/topology endpoints
- Test with multi-container deployment preset

### Risk 2: Docker Socket Security Breach (HIGH)

**Impact**: Root privilege escalation, host compromise
**Likelihood**: Medium (if deployed to production with socket mounted)
**Mitigation**:
- Clear documentation warnings
- Dual implementation (socket for dev, Kubernetes API for prod)
- CI/CD pipeline validation (reject prod deployments with socket mounts)

### Risk 3: Docker Compose Production Limitations (HIGH)

**Impact**: Single point of failure, no high availability
**Likelihood**: High (as project scales beyond 20 containers)
**Mitigation**:
- Design orchestrator with platform abstraction from day one
- Plan Kubernetes migration
- Use orchestrator to smooth transition (same API, different backend)

### Risk 4: Orchestrator Service Failure (MEDIUM)

**Impact**: No health monitoring, no test execution, no smart restarts
**Likelihood**: Low (with proper implementation)
**Mitigation**:
- Orchestrator runs continuously (never restarted by itself)
- Stateless design (state in Redis)
- Health check on orchestrator itself
- Future: Multiple orchestrator instances (leader election)

### Risk 5: Secrets Propagation Delay (MEDIUM)

**Impact**: Configuration changes don't apply promptly
**Target**: Secrets should propagate within 30 minutes
**Mitigation**:
- Configuration update event system
- Monitoring for propagation latency
- Manual refresh capability

---

## Future Enhancements

### Canary Deployments

Deploy new versions to subset of nodes before full rollout.

### Auto-Scaling

Scale services based on load metrics.

### Cross-Region Deployment

Deploy across multiple geographic regions.

### Chaos Engineering

Introduce controlled failures for resilience testing.

---

## Related Documentation

- **API Schema**: `schemas/orchestrator-api.yaml`
- **Original Implementation Plan**: `docs/ORCHESTRATION_SERVICE_IMPLEMENTATION_PLAN.md`
- **Research Validation**: `docs/ORCHESTRATION_RESEARCH_VALIDATION.md`
- **Dapr Restart Analysis**: `docs/DAPR_RESTART_ANALYSIS.md`
- **Networking Strategy**: `docs/NETWORKING-STRATEGY.md`
- **Event Schemas**: `schemas/permissions-events.yaml`, `schemas/common-events.yaml`
- **Service Testing Plan**: arcadia-kb `07 - Implementation Guides/SERVICE TESTING IMPLEMENTATION PLAN.md`
- **Core Memory**: arcadia-kb `Claude/BANNOU_CORE_MEMORY.md`
- **GitHub Actions Secrets**: [GitHub Docs](https://docs.github.com/en/actions/security-guides/using-secrets-in-github-actions)
- **Azure Key Vault Dapr**: [Dapr Docs](https://docs.dapr.io/reference/components-reference/supported-secret-stores/azure-keyvault/)
- **External Secrets Operator**: [external-secrets.io](https://external-secrets.io/)
- **Stakater Reloader**: [GitHub](https://github.com/stakater/Reloader)

---

## Appendix: Configuration Reference

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `ORCHESTRATOR_REDIS_CONNECTION` | `redis:6379` | Direct Redis connection |
| `ORCHESTRATOR_RABBITMQ_CONNECTION` | `amqp://guest:guest@rabbitmq:5672` | Direct RabbitMQ connection |
| `ORCHESTRATOR_DOCKER_HOST` | `unix:///var/run/docker.sock` | Docker API endpoint |
| `ORCHESTRATOR_DOCKER_TLS_CERT_PATH` | - | TLS certs for secure Docker |
| `ORCHESTRATOR_KUBERNETES_CONFIG` | - | Kubeconfig path |
| `ORCHESTRATOR_KUBERNETES_NAMESPACE` | `bannou` | K8s namespace |
| `ORCHESTRATOR_PORTAINER_URL` | - | Portainer API URL |
| `ORCHESTRATOR_PORTAINER_API_KEY` | - | Portainer API key |
| `ORCHESTRATOR_PREFERRED_BACKEND` | `auto` | Backend preference |
| `ORCHESTRATOR_PRESETS_DIRECTORY` | `provisioning/orchestrator/presets` | Presets location |
| `ORCHESTRATOR_HEALTH_CHECK_INTERVAL` | `30` | Service health interval (seconds) |
| `ORCHESTRATOR_HEARTBEAT_TIMEOUT` | `90` | Heartbeat expiration (seconds) |
| `ORCHESTRATOR_DEGRADATION_THRESHOLD` | `5` | Minutes before restart |

---

*This document consolidates the orchestrator service design from original planning through current implementation and future enhancements. Updated regularly as implementation progresses.*
