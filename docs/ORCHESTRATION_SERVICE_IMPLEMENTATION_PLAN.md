# Bannou Orchestration Service Implementation Plan
*Initial Draft - Pending Research Validation*
*Date: 2025-10-04*

## Executive Summary

Create a comprehensive orchestration/coordinator service as a Bannou plugin that:
- **Manages service lifecycle** via Docker Compose and future Kubernetes
- **Provides API-driven test execution** (infrastructure/http/edge tests)
- **Monitors service health** via heartbeats and smart restart logic
- **Directly integrates** with Redis/RabbitMQ using .NET libraries (NOT Dapr components)
- **Waits on startup** for dependencies to become available
- **Handles reconnections gracefully** when connections drop
- **Eliminates `--exit-code-from`** by executing tests via API

## Problem Statement

### Current Issues
1. **Dapr Sidecars Restart on Connection Failures**: Using `restart: always` violates requirement that "containers need to be resilient, not restart when connections fail"
2. **`depends_on` Not Production-Viable**: Dependency ordering doesn't work in production environments
3. **`--exit-code-from` Prevents Restart Policies**: Testing workflow conflicts with container restart strategies
4. **Multiple Restart Triggers**: RabbitMQ + 2 Redis instances + MySQL = 4 potential failure points
5. **No Graceful Degradation**: All-or-nothing approach to service availability

### Requirements
- **Production Resilience**: Containers must handle infrastructure unavailability gracefully without external orchestration
- **No Restart Dependencies**: Services shouldn't rely on restart policies for connection resilience
- **Smart Restart Logic**: Only restart when truly necessary, not on every connection disruption
- **Wait-on-Startup**: Services should wait for dependencies to become ready rather than crashing
- **Graceful Reconnection**: Handle connection drops without service restarts

## Architecture Overview

### Dual-Instance Pattern (From Existing Service Testing Plan)

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

## Key Components

### 1. Orchestrator Service Plugin (`lib-orchestrator`)

**Schema-First Design**:
```yaml
# schemas/orchestrator-api.yaml
openapi: 3.0.0
info:
  title: Orchestrator API
  version: 1.0.0
  description: Service lifecycle management and testing orchestration

paths:
  /orchestrator/health/infrastructure:
    get:
      summary: Check infrastructure component health
      description: Validates Redis, RabbitMQ, MySQL connectivity
      responses:
        '200':
          description: All infrastructure healthy
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/InfrastructureHealthResponse'
        '503':
          description: Infrastructure components unavailable
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/InfrastructureHealthResponse'

  /orchestrator/tests/run:
    post:
      summary: Execute test batch via API (no --exit-code-from)
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/TestExecutionRequest'
      responses:
        '200':
          description: Test execution results
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/TestExecutionResult'

  /orchestrator/services/restart:
    post:
      summary: Restart service with new configuration
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ServiceRestartRequest'
      responses:
        '200':
          description: Service restarted successfully

components:
  schemas:
    InfrastructureHealthResponse:
      type: object
      properties:
        healthy:
          type: boolean
        components:
          type: array
          items:
            $ref: '#/components/schemas/ComponentHealth'

    ComponentHealth:
      type: object
      properties:
        name:
          type: string
        status:
          type: string
          enum: [healthy, degraded, unavailable]
        lastSeen:
          type: string
          format: date-time

    TestExecutionRequest:
      type: object
      required:
        - testType
      properties:
        testType:
          type: string
          enum: [infrastructure, http, edge]
        plugin:
          type: string
        configuration:
          type: object
          additionalProperties: true

    TestExecutionResult:
      type: object
      properties:
        success:
          type: boolean
        testType:
          type: string
        duration:
          type: string
        results:
          type: array
          items:
            type: object
```

**Service Implementation Pattern**:
```csharp
[DaprService("orchestrator", typeof(IOrchestratorService), lifetime: ServiceLifetime.Singleton)]
public class OrchestratorService : IOrchestratorService
{
    private readonly IConnectionMultiplexer _redis;      // StackExchange.Redis - direct connection
    private readonly IConnection _rabbitMqConnection;    // RabbitMQ.Client - direct connection
    private readonly IModel _rabbitMqChannel;            // RabbitMQ channel for pub/sub
    private readonly DockerClient _dockerClient;         // Docker.DotNet - Docker API
    private readonly ILogger<OrchestratorService> _logger;
    private readonly OrchestratorServiceConfiguration _configuration;

    public OrchestratorService(
        IConnectionMultiplexer redis,
        IConnection rabbitMqConnection,
        DockerClient dockerClient,
        ILogger<OrchestratorService> logger,
        OrchestratorServiceConfiguration configuration)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _rabbitMqConnection = rabbitMqConnection ?? throw new ArgumentNullException(nameof(rabbitMqConnection));
        _rabbitMqChannel = _rabbitMqConnection.CreateModel();
        _dockerClient = dockerClient ?? throw new ArgumentNullException(nameof(dockerClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }
}
```

### 2. Direct Redis Integration Pattern

**Using Existing StackExchange.Redis** (version 2.8.16 - already in lib-connect):
```csharp
public class OrchestratorRedisManager
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<OrchestratorRedisManager> _logger;

    /// <summary>
    /// Wait on startup with exponential backoff - resilient connection establishment
    /// </summary>
    public static async Task<IConnectionMultiplexer> ConnectWithRetryAsync(
        string connectionString,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var delay = TimeSpan.FromSeconds(1);
        var maxDelay = TimeSpan.FromMinutes(2);
        var attempt = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var options = ConfigurationOptions.Parse(connectionString);
                options.ConnectRetry = 10;
                options.ConnectTimeout = 5000;
                options.AbortOnConnectFail = false; // Keep trying instead of crashing
                options.ReconnectRetryPolicy = new ExponentialRetry(1000, 30000);

                logger.LogInformation("Attempting to connect to Redis (attempt {Attempt})...", attempt + 1);

                var connection = await ConnectionMultiplexer.ConnectAsync(options);

                logger.LogInformation("✅ Connected to Redis after {Attempts} attempts", attempt + 1);

                // Verify connection works
                var pingResult = await connection.GetDatabase().PingAsync();
                logger.LogDebug("Redis ping: {PingTime}ms", pingResult.TotalMilliseconds);

                return connection;
            }
            catch (Exception ex)
            {
                attempt++;
                logger.LogWarning(ex,
                    "❌ Failed to connect to Redis (attempt {Attempt}), retrying in {Delay}...",
                    attempt, delay);

                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(
                    Math.Min(delay.TotalMilliseconds * 2, maxDelay.TotalMilliseconds));
            }
        }

        throw new OperationCanceledException("Redis connection cancelled");
    }

    /// <summary>
    /// Graceful reconnection handling - monitors connection health
    /// </summary>
    public async Task MonitorConnectionHealthAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var db = _redis.GetDatabase();
                await db.PingAsync();

                _logger.LogDebug("Redis connection healthy");
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis connection lost, automatic reconnection in progress...");

                // StackExchange.Redis handles reconnection automatically
                // Just wait and retry ping
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }
}
```

### 3. Direct RabbitMQ Integration (WITHOUT Dapr)

**🚨 CRITICAL DESIGN DECISION**: The orchestrator service CANNOT use Dapr for RabbitMQ integration because:
- Dapr sidecar depends on RabbitMQ being available (chicken-and-egg problem)
- Orchestrator must start BEFORE Dapr sidecars to coordinate infrastructure
- Using Dapr would create the exact dependency issue we're trying to solve

**Use RabbitMQ.Client Library Directly**:
```csharp
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

public class OrchestratorEventManager
{
    private readonly IConnection _rabbitMqConnection;
    private readonly IModel _rabbitMqChannel;
    private readonly ILogger<OrchestratorEventManager> _logger;
    private readonly IConnectionMultiplexer _redis;

    /// <summary>
    /// Wait for RabbitMQ availability on startup using direct connection
    /// </summary>
    public static async Task<IConnection> ConnectToRabbitMQAsync(
        string connectionString,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var delay = TimeSpan.FromSeconds(1);
        var maxDelay = TimeSpan.FromMinutes(2);
        var attempt = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                logger.LogInformation(
                    "Attempting to connect to RabbitMQ directly (attempt {Attempt})...",
                    attempt + 1);

                var factory = new ConnectionFactory
                {
                    Uri = new Uri(connectionString),
                    AutomaticRecoveryEnabled = true,  // ✅ Automatic reconnection
                    NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
                    RequestedHeartbeat = TimeSpan.FromSeconds(30),
                    DispatchConsumersAsync = true  // Async consumer support
                };

                var connection = factory.CreateConnection();

                // Verify connection works
                using (var channel = connection.CreateModel())
                {
                    // Declare exchange for heartbeat events
                    channel.ExchangeDeclare(
                        exchange: "bannou-events",
                        type: ExchangeType.Topic,
                        durable: true);
                }

                logger.LogInformation(
                    "✅ Connected to RabbitMQ after {Attempts} attempts",
                    attempt + 1);

                return connection;
            }
            catch (Exception ex)
            {
                attempt++;
                logger.LogWarning(ex,
                    "❌ RabbitMQ not ready (attempt {Attempt}), retrying in {Delay}...",
                    attempt, delay);

                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(
                    Math.Min(delay.TotalMilliseconds * 2, maxDelay.TotalMilliseconds));
            }
        }

        throw new OperationCanceledException("RabbitMQ connection cancelled");
    }

    /// <summary>
    /// Subscribe to service heartbeats using RabbitMQ.Client directly
    /// </summary>
    public async Task SubscribeToServiceHeartbeatsAsync()
    {
        _rabbitMqChannel.QueueDeclare(
            queue: "orchestrator-heartbeats",
            durable: true,
            exclusive: false,
            autoDelete: false);

        _rabbitMqChannel.QueueBind(
            queue: "orchestrator-heartbeats",
            exchange: "bannou-events",
            routingKey: "service.heartbeat.*");

        var consumer = new AsyncEventingBasicConsumer(_rabbitMqChannel);
        consumer.Received += async (model, ea) =>
        {
            try
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                var heartbeat = JsonSerializer.Deserialize<ServiceHeartbeatEvent>(message);

                if (heartbeat != null)
                {
                    _logger.LogDebug(
                        "Received heartbeat from {ServiceId} (app-id {AppId})",
                        heartbeat.ServiceId,
                        heartbeat.AppId);

                    await UpdateServiceHealthAsync(heartbeat);
                }

                _rabbitMqChannel.BasicAck(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process heartbeat event");
                _rabbitMqChannel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        _rabbitMqChannel.BasicConsume(
            queue: "orchestrator-heartbeats",
            autoAck: false,
            consumer: consumer);
    }

    private async Task UpdateServiceHealthAsync(ServiceHeartbeatEvent heartbeat)
    {
        // Store heartbeat in Redis for health tracking
        // Using existing schema structure: serviceId + appId for instance identification
        var key = $"service:heartbeat:{heartbeat.ServiceId}:{heartbeat.AppId}";
        var json = JsonSerializer.Serialize(heartbeat);

        await _redis.GetDatabase().StringSetAsync(
            key,
            json,
            expiry: TimeSpan.FromSeconds(90)); // Match heartbeat TTL from schema
    }
}
```

### 4. Service Heartbeat System (Using Existing Schema)

**🎯 ALIGNMENT WITH EXISTING IMPLEMENTATION**: Using `ServiceHeartbeatEvent` from `common-events.yaml` schema

**Existing Schema Structure** (from common-events.yaml):
```yaml
ServiceHeartbeatEvent:
  required: [eventId, timestamp, serviceId, appId, status]
  properties:
    eventId: string           # Unique identifier for this heartbeat
    timestamp: date-time      # When heartbeat was sent
    serviceId: string         # Service ID (e.g., "behavior", "accounts")
    appId: string             # Dapr app-id for this instance (e.g., "bannou", "npc-omega-01")
    status: enum              # [healthy, degraded, overloaded, shutting_down]
    capacity:                 # Service capacity and load information
      maxConnections: integer
      currentConnections: integer
      cpuUsage: float         # 0.0 - 1.0
      memoryUsage: float      # 0.0 - 1.0
    metadata: object          # Additional service-specific metadata
```

**Redis Key Pattern** (from existing implementation):
```
service:heartbeat:{serviceId}:{appId}
```

**Health Monitoring Implementation**:
```csharp
public class ServiceHealthMonitor
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<ServiceHealthMonitor> _logger;

    /// <summary>
    /// Monitor all service heartbeats from Redis using existing schema
    /// </summary>
    public async Task<ServiceHealthReport> GetServiceHealthAsync()
    {
        var db = _redis.GetDatabase();
        var server = _redis.GetServer(_redis.GetEndPoints().First());

        // Get all service heartbeat keys (existing pattern)
        var heartbeatKeys = server.Keys(pattern: "service:heartbeat:*").ToList();

        var healthyServices = new List<ServiceHealthStatus>();
        var unhealthyServices = new List<ServiceHealthStatus>();

        foreach (var key in heartbeatKeys)
        {
            var heartbeatJson = await db.StringGetAsync(key);
            if (heartbeatJson.HasValue)
            {
                // Deserialize using existing ServiceHeartbeatEvent schema
                var heartbeat = JsonSerializer.Deserialize<ServiceHeartbeatEvent>(
                    heartbeatJson.ToString());
                if (heartbeat == null) continue;

                // Parse timestamp and check if heartbeat is recent (within 90 seconds)
                var timestamp = DateTimeOffset.Parse(heartbeat.Timestamp);
                var age = DateTimeOffset.UtcNow - timestamp;

                if (age < TimeSpan.FromSeconds(90))
                {
                    healthyServices.Add(new ServiceHealthStatus
                    {
                        ServiceId = heartbeat.ServiceId,      // Use serviceId from schema
                        AppId = heartbeat.AppId,              // Use appId from schema
                        Status = heartbeat.Status,            // Use status enum from schema
                        LastSeen = timestamp,
                        Capacity = heartbeat.Capacity,        // Use capacity object from schema
                        Metadata = heartbeat.Metadata
                    });
                }
                else
                {
                    // Heartbeat expired - service unavailable
                    unhealthyServices.Add(new ServiceHealthStatus
                    {
                        ServiceId = heartbeat.ServiceId,
                        AppId = heartbeat.AppId,
                        Status = "unavailable",  // Override status for expired heartbeats
                        LastSeen = timestamp,
                        Capacity = null,
                        Metadata = null
                    });
                }
            }
        }

        return new ServiceHealthReport
        {
            HealthyServices = healthyServices,
            UnhealthyServices = unhealthyServices,
            Timestamp = DateTimeOffset.UtcNow,
            TotalServices = healthyServices.Count + unhealthyServices.Count,
            HealthPercentage = healthyServices.Count > 0
                ? (double)healthyServices.Count / (healthyServices.Count + unhealthyServices.Count) * 100
                : 0
        };
    }

    /// <summary>
    /// Check if specific infrastructure component is healthy
    /// </summary>
    public async Task<ComponentHealth> CheckInfrastructureComponentAsync(string componentName)
    {
        return componentName switch
        {
            "redis" => await CheckRedisHealthAsync(),
            "rabbitmq" => await CheckRabbitMQHealthAsync(),
            "placement" => await CheckDaprPlacementHealthAsync(),
            _ => new ComponentHealth
            {
                Name = componentName,
                Status = ComponentStatus.Unknown,
                Message = "Unknown component type"
            }
        };
    }

    private async Task<ComponentHealth> CheckRedisHealthAsync()
    {
        try
        {
            var db = _redis.GetDatabase();
            var ping = await db.PingAsync();

            return new ComponentHealth
            {
                Name = "redis",
                Status = ComponentStatus.Healthy,
                LastSeen = DateTimeOffset.UtcNow,
                Message = $"Ping: {ping.TotalMilliseconds}ms"
            };
        }
        catch (Exception ex)
        {
            return new ComponentHealth
            {
                Name = "redis",
                Status = ComponentStatus.Unavailable,
                LastSeen = DateTimeOffset.UtcNow,
                Message = ex.Message
            };
        }
    }
}
```

### 5. Smart Restart Logic

**Only Restart When Truly Necessary**:
```csharp
public class SmartRestartManager
{
    private readonly DockerClient _dockerClient;
    private readonly ServiceHealthMonitor _healthMonitor;
    private readonly ILogger<SmartRestartManager> _logger;

    /// <summary>
    /// Determine if service truly needs restart based on health metrics
    /// </summary>
    public async Task<bool> ShouldRestartServiceAsync(string serviceName)
    {
        var health = await _healthMonitor.GetServiceHealthAsync();
        var serviceStatus = health.HealthyServices
            .Concat(health.UnhealthyServices)
            .FirstOrDefault(s => s.ServiceName == serviceName);

        if (serviceStatus == null)
        {
            _logger.LogWarning("Service {ServiceName} not reporting heartbeat - restart recommended",
                serviceName);
            return true;
        }

        if (serviceStatus.Status == ServiceStatus.Healthy)
        {
            _logger.LogDebug("Service {ServiceName} is healthy - no restart needed", serviceName);
            return false;
        }

        if (serviceStatus.Status == ServiceStatus.Degraded)
        {
            // Check degradation duration
            var degradedFor = DateTimeOffset.UtcNow - serviceStatus.LastSeen;

            // Only restart if degraded for more than 5 minutes
            if (degradedFor > TimeSpan.FromMinutes(5))
            {
                _logger.LogWarning(
                    "Service {ServiceName} degraded for {Duration} - restart recommended",
                    serviceName, degradedFor);
                return true;
            }
            else
            {
                _logger.LogInformation(
                    "Service {ServiceName} degraded for {Duration} - waiting before restart",
                    serviceName, degradedFor);
                return false;
            }
        }

        // Service unavailable - restart needed
        _logger.LogError("Service {ServiceName} unavailable - restart required", serviceName);
        return true;
    }

    /// <summary>
    /// Restart service with optional environment variable updates
    /// </summary>
    public async Task RestartServiceAsync(
        string serviceName,
        Dictionary<string, string>? newEnvironment = null)
    {
        _logger.LogInformation("Initiating restart for service {ServiceName}...", serviceName);

        try
        {
            // Get container ID
            var containers = await _dockerClient.Containers.ListContainersAsync(
                new ContainersListParameters { All = true });

            var container = containers.FirstOrDefault(c =>
                c.Names.Any(n => n.Contains(serviceName)));

            if (container == null)
            {
                throw new InvalidOperationException(
                    $"Container for service {serviceName} not found");
            }

            // Stop service gracefully
            _logger.LogInformation("Stopping service {ServiceName}...", serviceName);
            await _dockerClient.Containers.StopContainerAsync(
                container.ID,
                new ContainerStopParameters { WaitBeforeKillSeconds = 30 });

            // Update environment if provided
            if (newEnvironment != null)
            {
                _logger.LogInformation("Updating environment for service {ServiceName}...", serviceName);
                await UpdateServiceEnvironmentAsync(container.ID, newEnvironment);
            }

            // Start service
            _logger.LogInformation("Starting service {ServiceName}...", serviceName);
            await _dockerClient.Containers.StartContainerAsync(
                container.ID,
                new ContainerStartParameters());

            // Wait for service to become healthy
            _logger.LogInformation("Waiting for service {ServiceName} to become healthy...", serviceName);
            await WaitForServiceHealthyAsync(serviceName, timeout: TimeSpan.FromMinutes(2));

            _logger.LogInformation("✅ Service {ServiceName} restarted successfully", serviceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to restart service {ServiceName}", serviceName);
            throw;
        }
    }

    private async Task WaitForServiceHealthyAsync(string serviceName, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            var health = await _healthMonitor.GetServiceHealthAsync();
            var serviceStatus = health.HealthyServices.FirstOrDefault(s => s.ServiceName == serviceName);

            if (serviceStatus != null && serviceStatus.Status == ServiceStatus.Healthy)
            {
                _logger.LogInformation(
                    "Service {ServiceName} became healthy after {Duration}",
                    serviceName, stopwatch.Elapsed);
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(5));
        }

        throw new TimeoutException(
            $"Service {serviceName} did not become healthy within {timeout}");
    }
}
```

### 6. API-Driven Test Execution

**Replace `--exit-code-from` Pattern**:
```csharp
public class TestExecutionManager
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<TestExecutionManager> _logger;

    /// <summary>
    /// Execute tests via API calls to test services
    /// </summary>
    public async Task<TestExecutionResult> ExecuteTestsAsync(TestExecutionRequest request)
    {
        _logger.LogInformation("Executing {TestType} tests...", request.TestType);

        var startTime = DateTimeOffset.UtcNow;
        var results = new List<TestResult>();

        try
        {
            // Determine which test service to invoke
            var testServiceAppId = request.TestType switch
            {
                "infrastructure" => "testing-service",
                "http" => "http-tester",
                "edge" => "edge-tester",
                _ => throw new ArgumentException($"Unknown test type: {request.TestType}")
            };

            // Invoke test service via Dapr
            var testResponse = await _daprClient.InvokeMethodAsync<TestRequest, TestResponse>(
                testServiceAppId,
                "execute-tests",
                new TestRequest
                {
                    Plugin = request.Plugin,
                    Configuration = request.Configuration
                });

            results.AddRange(testResponse.Results);

            var duration = DateTimeOffset.UtcNow - startTime;
            var success = results.All(r => r.Passed);

            _logger.LogInformation(
                "{TestType} tests {Status} in {Duration}ms",
                request.TestType,
                success ? "✅ PASSED" : "❌ FAILED",
                duration.TotalMilliseconds);

            return new TestExecutionResult
            {
                Success = success,
                TestType = request.TestType,
                Duration = duration.ToString(),
                Results = results,
                Summary = GenerateTestSummary(results)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test execution failed for {TestType}", request.TestType);

            return new TestExecutionResult
            {
                Success = false,
                TestType = request.TestType,
                Duration = (DateTimeOffset.UtcNow - startTime).ToString(),
                Results = results,
                Error = ex.Message
            };
        }
    }
}
```

## Docker Compose Changes

### New `docker-compose.orchestrator.yml`

**🚨 CRITICAL DESIGN PRINCIPLE**: We are specifically AVOIDING `depends_on` because:
1. **Production Issue**: In Kubernetes/Swarm/Portainer, `depends_on` causes immediate restarts on unhealthy dependencies
2. **Restart Storms**: Redis going down would trigger cascading restarts across all services (exactly what we're solving)
3. **Orchestrator Purpose**: This service exists specifically to replace `depends_on` with intelligent coordination
4. **Exception**: Only use `depends_on` for Dapr sidecars (minimalistic containers we can't manage externally)

```yaml
version: "3.4"

services:
  # Orchestrator instance - management layer
  # ⚠️ NO depends_on - waits for infrastructure via retry logic in code
  bannou-orchestrator:
    image: bannou:latest
    environment:
      # Only orchestrator service enabled
      - ORCHESTRATOR_SERVICE_ENABLED=true
      - ALL_OTHER_SERVICES_ENABLED=false

      # Direct Redis connection (not Dapr component)
      - REDIS_CONNECTION_STRING=redis:6379

      # Direct RabbitMQ connection (not Dapr component)
      - RABBITMQ_CONNECTION_STRING=amqp://guest:guest@rabbitmq:5672

      # Docker API access
      - DOCKER_HOST=unix:///var/run/docker.sock
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock  # ⚠️ Development only - HIGH SECURITY RISK in production
      - ./certificates:/certificates
    ports:
      - "8090:80"  # Orchestrator API port
    healthcheck:
      test: ["CMD", "curl", "--fail", "http://127.0.0.1:80/health"]
      interval: 30s
      timeout: 3s
      retries: 5
      start_period: 120s  # Give orchestrator time to connect to infrastructure
    labels:
      - "security.warning=Docker socket mounted - DEVELOPMENT ONLY, NOT FOR PRODUCTION"

  # Orchestrator Dapr sidecar
  # Exception: Can use depends_on because Dapr containers are minimalistic
  bannou-orchestrator-dapr:
    image: "daprio/daprd:edge"
    command: [
      "./daprd",
      "--app-id", "orchestrator",
      "--app-port", "80",
      "--app-protocol", "http",
      "--dapr-http-port", "3500",
      "--dapr-grpc-port", "50001",
      "--placement-host-address", "placement:50006",
      "--resources-path", "./components",
      "--log-level", "debug",
      "--enable-api-logging"
    ]
    volumes:
      - "./dapr/components/:/components"
      - "./certificates:/certificates"
    environment:
      - "SSL_CERT_DIR=/certificates"
    network_mode: "service:bannou-orchestrator"
    depends_on:
      - bannou-orchestrator  # Only for sidecar relationship
    # NO restart policy - if orchestrator fails, everything should fail

  # Main bannou instance - managed by orchestrator
  # ⚠️ NO depends_on - orchestrator handles startup coordination
  bannou:
    image: bannou:latest
    environment:
      - ORCHESTRATOR_SERVICE_ENABLED=false
      # All other service configuration managed by orchestrator
    volumes:
      - ./certificates:/certificates
      - logs-data:/app/logs/
    ports:
      - "8080:80"
      - "8443:443"
    # NO restart policy - orchestrator manages lifecycle
    # NO depends_on - orchestrator provides intelligent startup coordination

  # Bannou Dapr sidecar
  # Exception: Can use depends_on because Dapr containers are minimalistic
  bannou-dapr:
    image: "daprio/daprd:edge"
    command: [
      "./daprd",
      "--app-id", "bannou",
      "--app-port", "80",
      "--app-protocol", "http",
      "--dapr-http-port", "3500",
      "--dapr-grpc-port", "50001",
      "--placement-host-address", "placement:50006",
      "--resources-path", "./components",
      "--log-level", "debug",
      "--enable-api-logging"
    ]
    volumes:
      - "./dapr/components/:/components"
      - "./certificates:/certificates"
    environment:
      - "SSL_CERT_DIR=/certificates"
    network_mode: "service:bannou"
    depends_on:
      - bannou  # Only for sidecar relationship
    # NO restart policy - orchestrator manages lifecycle

volumes:
  logs-data:
```

## GitHub Actions Changes

### Replace `--exit-code-from` with API-Driven Testing

**Current Workflow (Problematic)**:
```yaml
- name: Run HTTP Tests
  run: |
    docker compose -f provisioning/docker-compose.yml \
                   -f provisioning/docker-compose.test.http.yml \
                   up --exit-code-from bannou-http-tester
```

**New Orchestrator-Driven Workflow**:
```yaml
- name: Start Orchestrator
  working-directory: ./provisioning
  run: |
    docker compose -f docker-compose.orchestrator.yml up -d bannou-orchestrator
    docker compose -f docker-compose.orchestrator.yml ps

- name: Wait for Orchestrator Ready
  run: |
    echo "Waiting for orchestrator to become ready..."
    curl --retry 30 --retry-delay 2 --retry-connrefused \
         http://localhost:8090/health

- name: Wait for Infrastructure Health
  run: |
    echo "Waiting for infrastructure components to become healthy..."
    curl --retry 30 --retry-delay 2 --retry-connrefused \
         --fail \
         http://localhost:8090/orchestrator/health/infrastructure

- name: Run Infrastructure Tests via Orchestrator API
  run: |
    echo "Executing infrastructure tests via orchestrator..."
    curl -X POST http://localhost:8090/orchestrator/tests/run \
      -H "Content-Type: application/json" \
      -d '{
        "testType": "infrastructure",
        "plugin": "testing"
      }' \
      | tee test-results.json

    # Validate success field in response
    cat test-results.json | jq -e '.success == true'

- name: Run HTTP Tests via Orchestrator API
  run: |
    echo "Executing HTTP tests via orchestrator..."
    curl -X POST http://localhost:8090/orchestrator/tests/run \
      -H "Content-Type: application/json" \
      -d '{
        "testType": "http",
        "plugin": "auth"
      }' \
      | tee test-results.json

    cat test-results.json | jq -e '.success == true'

- name: Run Edge Tests via Orchestrator API
  run: |
    echo "Executing edge tests via orchestrator..."
    curl -X POST http://localhost:8090/orchestrator/tests/run \
      -H "Content-Type: application/json" \
      -d '{
        "testType": "edge",
        "plugin": "connect"
      }' \
      | tee test-results.json

    cat test-results.json | jq -e '.success == true'

- name: Collect Test Results
  if: always()
  run: |
    # Orchestrator provides comprehensive test results via API
    curl http://localhost:8090/orchestrator/tests/results \
      | jq '.' > all-test-results.json

- name: Cleanup Orchestrator
  if: always()
  working-directory: ./provisioning
  run: |
    docker compose -f docker-compose.orchestrator.yml logs
    docker compose -f docker-compose.orchestrator.yml down -v
```

## Makefile Changes

### Replace Current Test Commands

**Current Commands**:
```makefile
test-http:
	cd provisioning && docker compose -f docker-compose.yml \
	                                  -f docker-compose.test.http.yml \
	                                  up --exit-code-from bannou-http-tester
```

**New Orchestrator Commands**:
```makefile
# Start orchestrator for testing
orchestrator-start:
	cd provisioning && docker compose -f docker-compose.orchestrator.yml up -d bannou-orchestrator
	@echo "Waiting for orchestrator to be ready..."
	@curl --retry 30 --retry-delay 2 --retry-connrefused http://localhost:8090/health

# Test commands via orchestrator API
test-infrastructure:
	@echo "Running infrastructure tests via orchestrator..."
	@curl -X POST http://localhost:8090/orchestrator/tests/run \
		-H "Content-Type: application/json" \
		-d '{"testType": "infrastructure", "plugin": "testing"}' \
		| jq -e '.success == true'

test-http:
	@echo "Running HTTP tests via orchestrator..."
	@curl -X POST http://localhost:8090/orchestrator/tests/run \
		-H "Content-Type: application/json" \
		-d '{"testType": "http", "plugin": "$(PLUGIN)"}' \
		| jq -e '.success == true'

test-edge:
	@echo "Running edge tests via orchestrator..."
	@curl -X POST http://localhost:8090/orchestrator/tests/run \
		-H "Content-Type: application/json" \
		-d '{"testType": "edge", "plugin": "connect"}' \
		| jq -e '.success == true'

# Stop orchestrator
orchestrator-stop:
	cd provisioning && docker compose -f docker-compose.orchestrator.yml down -v

# Complete test cycle
test-all-orchestrator: orchestrator-start test-infrastructure test-http test-edge orchestrator-stop
```

## Implementation Phases

### Phase 1: Foundation (Week 1) - Days 1-7
**Goal**: Create orchestrator plugin with direct Redis/RabbitMQ integration

**Day 1-2**: Schema and Plugin Generation
- Create `schemas/orchestrator-api.yaml` with comprehensive API design
- Generate orchestrator service plugin via `scripts/generate-all-services.sh`
- Implement basic service structure and DI setup

**Day 3-4**: Direct Infrastructure Integration
- Implement `OrchestratorRedisManager` with wait-on-startup retry logic
- Implement `OrchestratorEventManager` for RabbitMQ via Dapr
- Add graceful reconnection monitoring for both Redis and RabbitMQ
- Unit tests for connection retry and health monitoring

**Day 5-7**: Service Health Monitoring
- Implement `ServiceHealthMonitor` using existing heartbeat system
- Create infrastructure health check endpoints
- Add Docker API integration via Docker.DotNet
- Integration tests with real Redis/RabbitMQ

**Deliverables**:
- ✅ Orchestrator service with direct Redis/RabbitMQ connections
- ✅ Wait-on-startup logic prevents crash on missing dependencies
- ✅ Graceful reconnection handling for connection drops
- ✅ Service health monitoring via Redis heartbeats

### Phase 2: Testing Integration (Week 2) - Days 8-14
**Goal**: Replace `--exit-code-from` with API-driven test execution

**Day 8-9**: Test Execution API
- Implement `TestExecutionManager` for API-driven tests
- Create test execution endpoints in orchestrator
- Add test result collection and reporting

**Day 10-11**: Makefile and Local Testing
- Update Makefile with orchestrator-based test commands
- Create `docker-compose.orchestrator.yml`
- Local validation of orchestrator test workflow

**Day 12-14**: GitHub Actions Integration
- Update CI workflow to use orchestrator API
- Remove all `--exit-code-from` usage
- Comprehensive CI testing with orchestrator

**Deliverables**:
- ✅ API-driven test execution (infrastructure, HTTP, edge)
- ✅ Makefile commands use orchestrator instead of --exit-code-from
- ✅ GitHub Actions workflow updated and validated
- ✅ All tests passing via orchestrator

### Phase 3: Smart Restart Logic (Week 3) - Days 15-21
**Goal**: Intelligent restart decisions based on actual service health

**Day 15-17**: Restart Decision Logic
- Implement `SmartRestartManager` with health-based decisions
- Add degradation duration tracking
- Create restart avoidance for transient issues

**Day 18-19**: Docker Lifecycle Management
- Implement graceful service stop with timeout
- Add environment variable update mechanism
- Create health-waiting logic for post-restart validation

**Day 20-21**: Automatic Recovery
- Implement automatic recovery from degraded state
- Add configurable thresholds for restart decisions
- Comprehensive integration tests

**Deliverables**:
- ✅ Smart restart logic (only when truly necessary)
- ✅ Graceful service lifecycle management via Docker API
- ✅ Automatic recovery from degraded services
- ✅ No unnecessary restarts on transient connection issues

### Phase 4: Production Ready (Week 4) - Days 22-28
**Goal**: Production deployment readiness and Kubernetes support

**Day 22-24**: Kubernetes Integration
- Add Kubernetes API client support
- Implement Kubernetes service lifecycle management
- Create Kubernetes-specific health monitoring

**Day 25-26**: Error Handling and Logging
- Comprehensive error handling across all components
- Structured logging with correlation IDs
- Dead letter handling for failed operations

**Day 27-28**: Documentation and Validation
- Complete production deployment documentation
- Load testing and performance validation
- Security audit and hardening

**Deliverables**:
- ✅ Kubernetes orchestration support
- ✅ Production-grade error handling and logging
- ✅ Complete deployment documentation
- ✅ Performance and security validation

## Critical Design Decisions & Corrections

### 🚨 Decision #1: Direct RabbitMQ Integration (NO Dapr)

**Why Direct Connection Required**:
- Orchestrator cannot use Dapr Client for RabbitMQ
- Dapr sidecar depends on RabbitMQ being available (chicken-and-egg problem)
- Orchestrator must start BEFORE Dapr sidecars to coordinate infrastructure
- Using Dapr would recreate the exact dependency issue we're solving

**Implementation**: Use `RabbitMQ.Client` library with automatic recovery

### 🚨 Decision #2: Minimal depends_on Usage

**Why We're NOT Using depends_on**:
- In production (Kubernetes/Swarm/Portainer), `depends_on` causes immediate restarts on unhealthy dependencies
- Creates "restart storms" when infrastructure like Redis goes down
- **Orchestrator exists specifically to replace depends_on with intelligent coordination**

**Exception**: Only use `depends_on` for Dapr sidecars (minimalistic containers we can't manage externally)

### ✅ Decision #3: Align with Existing ServiceHeartbeatEvent Schema

**Using Existing Implementation** (from `common-events.yaml`):
- `serviceId` + `appId` for instance identification
- Status enum: `[healthy, degraded, overloaded, shutting_down]`
- Capacity object with connection and resource metrics
- Redis key pattern: `service:heartbeat:{serviceId}:{appId}`
- TTL: 90 seconds

**Why This Matters**: Seamless integration with existing services, no schema conflicts

## Expected Benefits

### Immediate Improvements
✅ **Eliminates `restart: always`** - Orchestrator manages restarts intelligently based on actual health
✅ **No `--exit-code-from`** - API-driven test execution provides better control and reporting
✅ **Resilient startup** - Waits for Redis/RabbitMQ using direct connections instead of crashing
✅ **Graceful reconnection** - Health monitoring and automatic recovery without service restarts
✅ **Smart restarts** - Only when truly necessary (e.g., 5+ min degradation), not on every connection blip
✅ **No depends_on dependency** - Orchestrator replaces depends_on with intelligent coordination

### Production Readiness
✅ **No `depends_on` dependency** - Orchestrator handles startup sequencing programmatically
✅ **Graceful degradation** - Services can operate in degraded state without restart
✅ **Health-based routing** - NGINX integration uses orchestrator health data
✅ **Configuration management** - Dynamic environment updates without manual intervention

### Future Capabilities
✅ **Configuration matrix testing** - Test multiple service configurations automatically
✅ **Blue-green deployments** - Service version management and traffic switching
✅ **Live deployment management** - Same orchestrator for production deployments
✅ **Capacity management** - Integration with queue service for intelligent routing
✅ **Multi-region orchestration** - Kubernetes support ready for production scaling

## Open Questions & Research Needed

### 1. Docker API Security
**Question**: Is mounting `/var/run/docker.sock` secure enough for production?
**Research Needed**: Docker socket security, alternative authentication methods, least-privilege access patterns

### 2. Orchestrator High Availability
**Question**: What happens if orchestrator itself fails?
**Research Needed**: Orchestrator redundancy patterns, failover mechanisms, state persistence

### 3. Test Execution Isolation
**Question**: How to prevent test interference in concurrent test execution?
**Research Needed**: Test isolation patterns, resource locking, concurrent test execution best practices

### 4. Kubernetes vs Docker Compose
**Question**: Are there fundamental differences in orchestration approach?
**Research Needed**: Kubernetes operators, custom controllers, Helm charts, production deployment patterns

### 5. Alternative Approaches
**Question**: Are there existing orchestration frameworks we should consider?
**Research Needed**: Nomad, Docker Swarm, K3s, existing .NET orchestration libraries

### 6. Performance Impact
**Question**: What's the overhead of orchestrator-mediated test execution?
**Research Needed**: Benchmark against current approach, identify bottlenecks, optimization strategies

## Next Steps (Pending Research Validation)

1. **Extensive Internet Research** - Validate all assumptions and identify alternatives
2. **Architecture Refinement** - Update plan based on research findings
3. **Create OpenAPI Schema** - Design complete orchestrator API
4. **Generate Service Plugin** - Use existing Bannou generation pipeline
5. **Implement Core Integration** - Redis/RabbitMQ with wait-on-startup
6. **Validate with Tests** - Prove orchestrator solves the restart problem

---

**Status**: ⚠️ DRAFT - Pending research validation and user approval
**Next Action**: Conduct extensive internet research to validate approach
