# PROPOSED: Native Bannou Service Mesh Plugin (Dapr Replacement)

> **Status**: Draft - Phase 2 Design Complete (Ready for Implementation Review)
> **Author**: Claude Code
> **Date**: 2025-12-24
> **Scope**: Complete replacement of Dapr runtime with native Bannou plugins
> **Revision**: TENETS audit complete - All violations fixed (POST-only pattern, x-permissions, BannouJson serialization). Added Section 15 with TENET update proposals. EventSubscriptionRegistry design added for event type deserialization.

---

## Executive Summary

This document proposes replacing Dapr with native Bannou plugins to achieve:
- **Complete control** over service mesh functionality
- **Elimination of pain points** caused by Dapr's architectural decisions
- **Reduced infrastructure complexity** (no sidecars)
- **Better performance** (no CloudEvents overhead, zero-copy routing)

The replacement consists of three core plugins:
1. **lib-mesh**: YARP-based HTTP service-to-service invocation
2. **lib-messaging**: Direct RabbitMQ pub/sub (MassTransit wrapper)
3. **lib-state**: Repository pattern state management (Redis/MySQL)

The Orchestrator service already bypasses Dapr for infrastructure operations and provides the template for this approach.

---

## Table of Contents

1. [Dapr Pain Points](#1-dapr-pain-points)
2. [What Dapr Currently Provides](#2-what-dapr-currently-provides)
3. [Replacement Architecture](#3-replacement-architecture)
4. [Plugin Design: lib-mesh](#4-plugin-design-lib-mesh)
5. [Plugin Design: lib-messaging](#5-plugin-design-lib-messaging)
6. [Plugin Design: lib-state](#6-plugin-design-lib-state)
7. [Orchestrator Extensions](#7-orchestrator-extensions)
8. [Migration Strategy](#8-migration-strategy)
9. [Impact on TENETS.md](#9-impact-on-tenetsmd)
10. [Timeline and Risks](#10-timeline-and-risks)
11. [Current Mesh Implementation Analysis (Dapr Coupling Problem)](#11-current-mesh-implementation-analysis-dapr-coupling-problem)
12. [lib-messaging Plugin Scaffolding](#12-lib-messaging-plugin-scaffolding)
13. [lib-state Plugin Scaffolding](#13-lib-state-plugin-scaffolding)
14. [Dapr Decoupling Strategy](#14-dapr-decoupling-strategy)
15. [Proposed TENET Updates](#15-proposed-tenet-updates)

---

## 1. Dapr Pain Points

### 1.1 Known Issues (User-Reported)

#### Network Mode Incompatibility
**Problem**: Dapr's documented `network_mode: "service:bannou"` pattern only works in Kubernetes. In Docker Compose/Swarm, it causes:
- IP address confusion between containers
- DNS resolution failures
- Dapr sidecar startup problems

**Current Workaround**: Standalone Dapr containers with `app-channel-address` pointing to container DNS name.

**With Native Plugin**: No sidecars needed - service invocation is direct HTTP within container.

#### CloudEvents Wrapper Overhead
**Problem**: Dapr wraps all pub/sub messages in CloudEvents format, adding:
- 200-500 bytes overhead per message
- Serialization/deserialization overhead
- Complexity in debugging message content

**Quote from User**: "oh but the raw versions might leak at first until it gets started up properly"

**With Native Plugin**: Direct RabbitMQ messages - format under our control.

#### Sidecar Restart on Missing Topic
**Problem**: Publishing to a non-existent topic causes the Dapr sidecar app to restart:
- Sidecar becomes unresponsive during restart
- All in-flight requests fail
- No graceful degradation

**With Native Plugin**: Direct RabbitMQ connection handles missing exchanges gracefully - can auto-declare or fail fast.

#### Chicken-and-Egg Startup Dependency
**Problem**: Dapr requires placement service and components to be ready before app starts. Orchestrator needs to manage infrastructure before Dapr is ready.

**Current Workaround**: Orchestrator uses direct Redis/RabbitMQ connections (bypasses Dapr entirely).

**With Native Plugin**: All services can use the same direct connection pattern.

### 1.2 Architectural Overhead

#### Sidecar Resource Consumption
- Each Dapr sidecar consumes ~50-100MB RAM
- Separate container per app-id adds Docker overhead
- Multiplied by number of distributed nodes in production

#### Configuration Complexity
- 17 component YAML files in `/provisioning/dapr/`
- Component configuration duplication across environments
- Debugging requires checking both app logs AND sidecar logs

#### Limited Dynamic Behavior
- Cannot create/destroy topic subscriptions at runtime
- Connect service already bypasses Dapr for per-session channels
- Voice service bypasses Dapr for real-time media control

### 1.3 Summary: Services Already Bypassing Dapr

| Service | Bypass | Reason |
|---------|--------|--------|
| Orchestrator | Redis + RabbitMQ | Infrastructure management before Dapr ready |
| Connect | RabbitMQ (partial) | Dynamic per-session channel subscriptions |
| Voice | RTPEngine + Kamailio | Real-time media control |

**Pattern**: Services with advanced requirements already bypass Dapr. This proposal extends that pattern system-wide.

---

## 2. What Dapr Currently Provides

### 2.1 Service-to-Service Invocation

**Current Implementation**: `DaprClient.InvokeMethodAsync()` / `CreateInvokeMethodRequest()`

**Files Affected**: 88+ files reference DaprClient

**Core Mechanism**:
```csharp
// In DaprServiceClientBase.cs (302 lines)
var request = _daprClient.CreateInvokeMethodRequest(httpMethod, appId, endpoint);
httpResponse = await _daprClient.InvokeMethodWithResponseAsync(request, ct);
```

**Routing**: Dynamic app-id resolution via `ServiceAppMappingResolver`:
- Default: All services route to "bannou" (omnipotent node)
- Distributed: `FullServiceMappingsEvent` updates routing atomically

### 2.2 State Management

**Current Implementation**: `DaprClient.GetStateAsync()` / `SaveStateAsync()`

**State Store Components** (17 total):

| Store Name | Backend | Purpose |
|------------|---------|---------|
| `auth-statestore` | Redis | Session state (TTL-managed) |
| `connect-statestore` | Redis | WebSocket connections |
| `permissions-statestore` | Redis | Permission caching |
| `mysql-accounts-statestore` | MySQL | Account persistence |
| `mysql-character-statestore` | MySQL | Character data |
| ... | ... | 12 more stores |

**Key Patterns**:
- Redis: Ephemeral, TTL-managed (sessions, heartbeats, cache)
- MySQL: Durable, queryable (accounts, characters, realms)

### 2.3 Pub/Sub Eventing

**Current Implementation**: `DaprClient.PublishEventAsync()` + `[Topic]` handlers

**Event Topics** (18+ defined):
```
account.created, account.updated, account.deleted
session.connected, session.disconnected, session.invalidated
location.created, location.updated, location.deleted
bannou-service-heartbeats, bannou-full-service-mappings
permissions.service-registered, permissions.session-state-changed
... and more
```

**Event Consumer Fan-Out**: `IEventConsumer` enables multiple handlers per topic (Dapr only supports one endpoint per app-id per topic).

### 2.4 Service Discovery

**Current**: mDNS (Dapr default) for Dapr-to-Dapr communication

**Already Replaced**: `ServiceAppMappingResolver` + `FullServiceMappingsEvent` provides all routing logic.

---

## 3. Replacement Architecture

### 3.1 High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        BANNOU INSTANCE                          │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐             │
│  │  lib-mesh   │  │lib-messaging│  │  lib-state  │             │
│  │ (YARP HTTP) │  │ (RabbitMQ)  │  │(Redis/MySQL)│             │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘             │
│         │                │                │                     │
│         └────────────────┴────────────────┘                     │
│                          │                                      │
│              ┌───────────┴───────────┐                          │
│              │  Orchestrator Plugin  │                          │
│              │  (Service Registry)   │                          │
│              └───────────┬───────────┘                          │
│                          │                                      │
├──────────────────────────┴──────────────────────────────────────┤
│  Infrastructure: Redis │ RabbitMQ │ MySQL                       │
└─────────────────────────────────────────────────────────────────┘

     NO SIDECAR CONTAINERS - Direct connections to infrastructure
```

### 3.2 Dependency Flow

```
Services (lib-accounts, lib-auth, etc.)
    │
    ├── IMeshClient (replaces DaprClient.InvokeMethodAsync)
    │       └── Uses: ServiceAppMappingResolver (already exists)
    │
    ├── IMessageBus (replaces DaprClient.PublishEventAsync)
    │       └── Uses: IEventConsumer (already exists for fan-out)
    │
    └── IStateStore<T> (replaces DaprClient.GetStateAsync)
            └── Uses: Redis or MySQL backend per configuration
```

### 3.3 Key Design Principles

1. **Interface Parity**: New interfaces mirror Dapr patterns for easy migration
2. **Progressive Migration**: Services can migrate one at a time
3. **Reuse Existing Patterns**: ServiceAppMappingResolver, IEventConsumer unchanged
4. **Schema-First**: Plugins follow Bannou tenet with OpenAPI schemas
5. **Zero Sidecars**: All functionality within bannou process

---

## 4. Plugin Design: lib-mesh

### 4.1 Purpose

Replace `DaprClient.InvokeMethodAsync()` with YARP-based direct HTTP routing.

### 4.2 Core Interface

```csharp
/// <summary>
/// Service-to-service invocation without Dapr sidecar.
/// Drop-in replacement for DaprClient service invocation methods.
/// </summary>
public interface IMeshClient
{
    /// <summary>
    /// Invoke a method on another service.
    /// </summary>
    Task<TResponse?> InvokeMethodAsync<TRequest, TResponse>(
        string appId,
        string methodName,
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class;

    /// <summary>
    /// Invoke a method with raw HTTP response access.
    /// </summary>
    Task<HttpResponseMessage> InvokeMethodWithResponseAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create an invocation request (mirrors DaprClient API).
    /// </summary>
    HttpRequestMessage CreateInvokeMethodRequest(
        HttpMethod httpMethod,
        string appId,
        string methodName);
}
```

### 4.3 Implementation: YARP Integration

```csharp
/// <summary>
/// YARP-based service mesh client with dynamic routing.
/// </summary>
public class YarpMeshClient : IMeshClient
{
    private readonly IHttpForwarder _forwarder;
    private readonly IServiceAppMappingResolver _resolver;
    private readonly IMeshRouteProvider _routeProvider;
    private readonly HttpMessageInvoker _httpClient;

    public async Task<TResponse?> InvokeMethodAsync<TRequest, TResponse>(
        string appId,
        string methodName,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        // Resolve target endpoint from service registry
        var targetAppId = _resolver.GetAppIdForService(appId);
        var endpoint = await _routeProvider.GetEndpointAsync(targetAppId);

        // Direct HTTP call (no sidecar)
        var uri = new Uri($"http://{endpoint.Host}:{endpoint.Port}/{methodName}");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, uri);
        httpRequest.Content = JsonContent.Create(request, options: BannouJson.Options);

        var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<TResponse>(
            BannouJson.Options, cancellationToken);
    }
}
```

### 4.4 Service Registry Integration

```csharp
/// <summary>
/// Provides endpoint information for service routing.
/// Extends existing Redis heartbeat system.
/// </summary>
public interface IMeshRouteProvider
{
    Task<ServiceEndpoint> GetEndpointAsync(string appId);
    Task<IReadOnlyList<ServiceEndpoint>> GetAllEndpointsAsync(string appId);
    Task RegisterEndpointAsync(string appId, ServiceEndpoint endpoint);
}

public record ServiceEndpoint(
    string AppId,
    string Host,
    int Port,
    ServiceStatus Status,
    float LoadPercent,
    DateTimeOffset LastSeen);
```

### 4.5 Load Balancing

```csharp
/// <summary>
/// Selects target endpoint from available instances.
/// </summary>
public interface ILoadBalancer
{
    ServiceEndpoint Select(IReadOnlyList<ServiceEndpoint> endpoints);
}

// Implementations:
public class RoundRobinLoadBalancer : ILoadBalancer { }
public class LeastConnectionsLoadBalancer : ILoadBalancer { }
public class WeightedLoadBalancer : ILoadBalancer { }
```

### 4.6 Health-Aware Routing

The existing Redis heartbeat system provides health data:

```
Redis Key: service:heartbeat:{appId}
Content: InstanceHealthStatus {
    Status: "healthy" | "degraded" | "unavailable",
    LoadPercent: 0-100,
    LastSeen: timestamp
}
```

`IMeshRouteProvider` reads this data to exclude unhealthy endpoints.

---

## 5. Plugin Design: lib-messaging

### 5.1 Purpose

Replace `DaprClient.PublishEventAsync()` and `[Topic]` handlers with direct RabbitMQ.

### 5.2 Core Interfaces

```csharp
/// <summary>
/// Publish events to message bus without Dapr/CloudEvents overhead.
/// </summary>
public interface IMessageBus
{
    /// <summary>
    /// Publish an event to a topic.
    /// </summary>
    Task PublishAsync<TEvent>(
        string topic,
        TEvent eventData,
        CancellationToken cancellationToken = default)
        where TEvent : class;

    /// <summary>
    /// Publish with explicit exchange configuration.
    /// </summary>
    Task PublishAsync<TEvent>(
        string exchange,
        string routingKey,
        TEvent eventData,
        MessageOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : class;
}

/// <summary>
/// Subscribe to message bus topics.
/// </summary>
public interface IMessageSubscriber
{
    /// <summary>
    /// Subscribe to a topic with handler.
    /// </summary>
    Task SubscribeAsync<TEvent>(
        string topic,
        Func<TEvent, CancellationToken, Task> handler,
        SubscriptionOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : class;

    /// <summary>
    /// Dynamic subscription (Connect service per-session channels).
    /// </summary>
    Task<IAsyncDisposable> SubscribeDynamicAsync<TEvent>(
        string topic,
        Func<TEvent, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
        where TEvent : class;
}
```

### 5.3 Implementation Options

#### Option A: MassTransit Wrapper (Recommended)

```csharp
/// <summary>
/// MassTransit-based message bus implementation.
/// Provides mature .NET abstractions over RabbitMQ.
/// </summary>
public class MassTransitMessageBus : IMessageBus
{
    private readonly IBus _bus;

    public async Task PublishAsync<TEvent>(
        string topic,
        TEvent eventData,
        CancellationToken cancellationToken = default)
        where TEvent : class
    {
        var endpoint = await _bus.GetSendEndpoint(new Uri($"exchange:{topic}"));
        await endpoint.Send(eventData, cancellationToken);
    }
}
```

**Advantages**:
- Battle-tested abstraction (v8.4.1 supports .NET 9)
- Built-in retry, dead-letter, saga support
- Easy testing with in-memory transport
- Future transport flexibility (Azure Service Bus, Amazon SQS)

#### Option B: Direct RabbitMQ.Client

```csharp
/// <summary>
/// Direct RabbitMQ implementation for maximum control.
/// </summary>
public class RabbitMqMessageBus : IMessageBus
{
    private readonly IConnection _connection;
    private readonly IModel _channel;

    public async Task PublishAsync<TEvent>(
        string topic,
        TEvent eventData,
        CancellationToken cancellationToken = default)
        where TEvent : class
    {
        var body = BannouJson.SerializeToUtf8Bytes(eventData);
        var properties = _channel.CreateBasicProperties();
        properties.ContentType = "application/json";
        properties.DeliveryMode = 2; // Persistent

        _channel.BasicPublish(
            exchange: "bannou",
            routingKey: topic,
            basicProperties: properties,
            body: body);
    }
}
```

**Advantages**:
- Zero abstraction overhead
- Maximum control over message format
- No CloudEvents - raw JSON

### 5.4 Event Consumer Integration

Preserve existing `IEventConsumer` fan-out pattern:

```csharp
/// <summary>
/// Message handler that integrates with IEventConsumer for fan-out.
/// </summary>
public class EventConsumerMessageHandler<TEvent> : IConsumer<TEvent>
    where TEvent : class
{
    private readonly IEventConsumer _eventConsumer;
    private readonly string _topic;

    public async Task Consume(ConsumeContext<TEvent> context)
    {
        await _eventConsumer.DispatchAsync(_topic, context.Message);
    }
}
```

### 5.5 Dynamic Subscriptions

For Connect service per-session channels:

```csharp
public class DynamicSubscriptionManager
{
    private readonly IConnection _connection;
    private readonly ConcurrentDictionary<string, IModel> _channels = new();

    public async Task<IAsyncDisposable> CreateSessionChannelAsync(
        string sessionId,
        Func<CapabilityUpdateEvent, Task> handler)
    {
        var channel = _connection.CreateModel();
        var queueName = $"session.{sessionId}.capabilities";

        channel.QueueDeclare(queueName, durable: false, exclusive: true);
        channel.QueueBind(queueName, "bannou", $"session.{sessionId}.*");

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (_, ea) =>
        {
            var evt = BannouJson.Deserialize<CapabilityUpdateEvent>(ea.Body.Span);
            await handler(evt!);
        };

        channel.BasicConsume(queueName, autoAck: true, consumer);

        return new ChannelDisposable(channel, () => _channels.TryRemove(sessionId, out _));
    }
}
```

### 5.6 Message Format: No CloudEvents

**Dapr CloudEvents Format** (current):
```json
{
  "specversion": "1.0",
  "type": "com.bannou.account.created",
  "source": "bannou-accounts",
  "id": "abc123",
  "time": "2025-12-23T10:00:00Z",
  "datacontenttype": "application/json",
  "data": {
    "accountId": "...",
    "email": "..."
  }
}
```

**Native Format** (proposed):
```json
{
  "eventId": "abc123",
  "timestamp": "2025-12-23T10:00:00Z",
  "accountId": "...",
  "email": "..."
}
```

**Savings**: ~200-500 bytes per message, reduced serialization overhead.

---

## 6. Plugin Design: lib-state

### 6.1 Purpose

Replace `DaprClient.GetStateAsync()` / `SaveStateAsync()` with repository pattern.

### 6.2 Core Interface

```csharp
/// <summary>
/// State store abstraction replacing Dapr state management.
/// </summary>
public interface IStateStore<TValue>
    where TValue : class
{
    /// <summary>
    /// Get state by key.
    /// </summary>
    Task<TValue?> GetAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get state with ETag for optimistic concurrency.
    /// </summary>
    Task<(TValue? Value, string? ETag)> GetWithETagAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Save state.
    /// </summary>
    Task SaveAsync(
        string key,
        TValue value,
        StateOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Save with ETag check (optimistic concurrency).
    /// </summary>
    Task<bool> TrySaveAsync(
        string key,
        TValue value,
        string etag,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete state.
    /// </summary>
    Task<bool> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Query state (MySQL stores only).
    /// </summary>
    Task<IReadOnlyList<TValue>> QueryAsync(
        Expression<Func<TValue, bool>> predicate,
        CancellationToken cancellationToken = default);
}

public record StateOptions(
    TimeSpan? Ttl = null,
    StateConsistency Consistency = StateConsistency.Strong);

public enum StateConsistency { Strong, Eventual }
```

### 6.3 Redis Implementation

```csharp
/// <summary>
/// Redis-backed state store for ephemeral/session data.
/// </summary>
public class RedisStateStore<TValue> : IStateStore<TValue>
    where TValue : class
{
    private readonly IDatabase _database;
    private readonly string _keyPrefix;

    public async Task<TValue?> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var fullKey = $"{_keyPrefix}:{key}";
        var value = await _database.StringGetAsync(fullKey);

        if (value.IsNullOrEmpty)
            return null;

        return BannouJson.Deserialize<TValue>(value!);
    }

    public async Task SaveAsync(
        string key,
        TValue value,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var fullKey = $"{_keyPrefix}:{key}";
        var json = BannouJson.Serialize(value);
        var expiry = options?.Ttl;

        await _database.StringSetAsync(fullKey, json, expiry);
    }

    public async Task<(TValue? Value, string? ETag)> GetWithETagAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var fullKey = $"{_keyPrefix}:{key}";
        var transaction = _database.CreateTransaction();

        var valueTask = transaction.StringGetAsync(fullKey);
        var versionTask = transaction.HashGetAsync($"{fullKey}:meta", "version");

        await transaction.ExecuteAsync();

        var value = await valueTask;
        var version = await versionTask;

        if (value.IsNullOrEmpty)
            return (null, null);

        return (
            BannouJson.Deserialize<TValue>(value!),
            version.HasValue ? version.ToString() : null);
    }
}
```

### 6.4 MySQL Implementation

```csharp
/// <summary>
/// MySQL-backed state store for durable/queryable data.
/// Uses EF Core for query support.
/// </summary>
public class MySqlStateStore<TValue> : IStateStore<TValue>
    where TValue : class
{
    private readonly DbContext _context;
    private readonly DbSet<StateEntry<TValue>> _entries;

    public async Task<TValue?> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var entry = await _entries
            .Where(e => e.Key == key)
            .FirstOrDefaultAsync(cancellationToken);

        return entry?.Value;
    }

    public async Task<IReadOnlyList<TValue>> QueryAsync(
        Expression<Func<TValue, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        // This is the key advantage of MySQL: queryability
        return await _entries
            .Select(e => e.Value)
            .Where(predicate)
            .ToListAsync(cancellationToken);
    }
}

public class StateEntry<TValue>
{
    public string Key { get; set; } = default!;
    public TValue Value { get; set; } = default!;
    public string ETag { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

### 6.5 State Store Factory

```csharp
/// <summary>
/// Creates appropriate state store based on configuration.
/// </summary>
public interface IStateStoreFactory
{
    /// <summary>
    /// Get or create a state store for the specified name.
    /// </summary>
    IStateStore<TValue> GetStore<TValue>(string storeName)
        where TValue : class;
}

public class StateStoreFactory : IStateStoreFactory
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDbContextFactory<StateDbContext> _dbContextFactory;
    private readonly StateStoreConfiguration _configuration;

    public IStateStore<TValue> GetStore<TValue>(string storeName)
        where TValue : class
    {
        var config = _configuration.Stores[storeName];

        return config.Backend switch
        {
            StateBackend.Redis => new RedisStateStore<TValue>(
                _redis.GetDatabase(),
                storeName),
            StateBackend.MySql => new MySqlStateStore<TValue>(
                _dbContextFactory.CreateDbContext(),
                storeName),
            _ => throw new ArgumentException($"Unknown backend: {config.Backend}")
        };
    }
}
```

### 6.6 Configuration

```csharp
/// <summary>
/// Configuration for state stores (replaces Dapr component YAMLs).
/// </summary>
public class StateStoreConfiguration
{
    public Dictionary<string, StateStoreConfig> Stores { get; set; } = new();
}

public class StateStoreConfig
{
    public StateBackend Backend { get; set; }
    public string ConnectionString { get; set; } = default!;
    public TimeSpan? DefaultTtl { get; set; }
}

public enum StateBackend { Redis, MySql }
```

---

## 7. Orchestrator Extensions

### 7.1 Current Orchestrator Capabilities

The Orchestrator already manages:
- **Redis Heartbeats**: `service:heartbeat:{appId}` with TTL
- **Service Routing**: `service:routing:{serviceName}` with TTL
- **Configuration Storage**: `config:current`, `config:version`, `config:history:{version}`
- **ExtraHosts Injection**: Infrastructure IP discovery for new containers
- **Service Mapping Events**: `FullServiceMappingsEvent` atomic distribution

### 7.2 Required Extensions

#### 7.2.1 Enhanced Service Registry

Extend Redis storage to include endpoint details:

```csharp
/// <summary>
/// Extended service registry for lib-mesh routing.
/// </summary>
public class ServiceRegistryEntry
{
    public string AppId { get; set; } = default!;
    public string InstanceId { get; set; } = default!;
    public string Host { get; set; } = default!;
    public int Port { get; set; }
    public ServiceStatus Status { get; set; }
    public float LoadPercent { get; set; }
    public int MaxConnections { get; set; }
    public int CurrentConnections { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public List<string> Services { get; set; } = new();
}

// Redis storage pattern (extends existing heartbeat)
// Key: service:registry:{appId}:{instanceId}
// Value: ServiceRegistryEntry JSON
// TTL: 90 seconds (same as heartbeat)
```

#### 7.2.2 Endpoint Discovery API

New Orchestrator endpoint for service discovery:

```yaml
# In orchestrator-api.yaml
/orchestrator/service-endpoints:
  post:
    operationId: getServiceEndpoints
    x-permissions:
      - role: user  # All authenticated services can discover
    requestBody:
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/GetServiceEndpointsRequest'
    responses:
      '200':
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ServiceEndpointsResponse'

GetServiceEndpointsRequest:
  type: object
  required: [appId]
  properties:
    appId: { type: string }
    includeUnhealthy: { type: boolean, default: false }

ServiceEndpointsResponse:
  type: object
  properties:
    endpoints:
      type: array
      items:
        $ref: '#/components/schemas/ServiceEndpoint'
```

#### 7.2.3 Service Registration on Startup

Each bannou instance registers with Orchestrator on startup:

```csharp
/// <summary>
/// Service registration manager - runs on each bannou instance.
/// </summary>
public class ServiceRegistrationManager
{
    public async Task RegisterAsync(CancellationToken ct)
    {
        var registration = new ServiceRegistrationEvent
        {
            InstanceId = InstanceId,
            AppId = _appId,
            Host = Environment.MachineName,  // Container hostname
            Port = 80,
            Services = _loadedServices.ToList(),
            MaxConnections = _configuration.MaxConnections,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Publish to Orchestrator via RabbitMQ
        await _messageBus.PublishAsync(
            "bannou-service-registration",
            registration,
            ct);
    }
}
```

### 7.3 Heartbeat Enhancement: Include IP Addresses

**Current Heartbeat** (ServiceHeartbeatEvent):
- AppId, InstanceId, Status, Services, Capacity

**Enhanced Heartbeat**:
```csharp
public class ServiceHeartbeatEvent
{
    // Existing fields
    public string AppId { get; set; } = default!;
    public Guid InstanceId { get; set; }
    public ServiceHeartbeatEventStatus Status { get; set; }
    public List<ServiceStatus> Services { get; set; } = new();

    // New fields for direct routing
    public string Host { get; set; } = default!;      // Container hostname/IP
    public int Port { get; set; } = 80;               // Service port
    public string? ExternalHost { get; set; }         // External/public IP if known
}
```

This enables direct service-to-service routing without DNS lookups or sidecars.

---

## 8. Migration Strategy

### 8.1 Parallel Operation Phase

Both Dapr and native plugins can run simultaneously:

```csharp
/// <summary>
/// Hybrid client that can use either Dapr or native mesh.
/// </summary>
public class HybridMeshClient : IMeshClient
{
    private readonly DaprClient? _daprClient;
    private readonly YarpMeshClient? _yarpClient;
    private readonly MeshConfiguration _config;

    public async Task<TResponse?> InvokeMethodAsync<TRequest, TResponse>(
        string appId,
        string methodName,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_config.UseNativeMesh)
            return await _yarpClient!.InvokeMethodAsync<TRequest, TResponse>(...);
        else
            return await _daprClient!.InvokeMethodAsync<TRequest, TResponse>(...);
    }
}
```

### 8.2 Service-by-Service Migration

1. **Phase 1**: Orchestrator (already bypasses Dapr)
2. **Phase 2**: Connect (already partially bypasses)
3. **Phase 3**: Auth, Accounts (core services)
4. **Phase 4**: Permissions, GameSession (dependent services)
5. **Phase 5**: All remaining services
6. **Phase 6**: Remove Dapr infrastructure

### 8.3 Configuration Flags

```bash
# Environment variables for migration control
BANNOU_USE_NATIVE_MESH=true      # Use lib-mesh instead of Dapr invocation
BANNOU_USE_NATIVE_MESSAGING=true  # Use lib-messaging instead of Dapr pub/sub
BANNOU_USE_NATIVE_STATE=true      # Use lib-state instead of Dapr state stores
BANNOU_DAPR_ENABLED=false         # Disable Dapr sidecar requirement
```

### 8.4 Test Migration

All tests currently mock `DaprClient`. Create parallel mocks:

```csharp
// Existing pattern
var mockDaprClient = new Mock<DaprClient>();
mockDaprClient.Setup(x => x.GetStateAsync<T>(...))
    .ReturnsAsync(testData);

// New pattern
var mockStateStore = new Mock<IStateStore<T>>();
mockStateStore.Setup(x => x.GetAsync(...))
    .ReturnsAsync(testData);
```

---

## 9. Impact on TENETS.md

### 9.1 Tenet 4: Dapr-First Infrastructure

**Current**:
> Services MUST use Dapr abstractions for all infrastructure concerns.

**Proposed Update**:
> Services MUST use Bannou mesh abstractions for all infrastructure concerns.
> - `IMeshClient` for service-to-service invocation
> - `IMessageBus` for pub/sub events
> - `IStateStore<T>` for state management

### 9.2 New Tenet: Native Mesh Patterns

```markdown
## Tenet 4: Bannou Mesh Infrastructure (MANDATORY)

**Rule**: Services MUST use native mesh abstractions for all infrastructure.

### Service Invocation
```csharp
// REQUIRED: Use IMeshClient for service calls
var (statusCode, result) = await _meshClient.InvokeMethodAsync<Req, Resp>(
    "accounts", "get-account", request, ct);

// FORBIDDEN: Direct HTTP construction
var response = await httpClient.PostAsync("http://accounts/api/..."); // NO!
```

### Event Publishing
```csharp
// REQUIRED: Use IMessageBus for events
await _messageBus.PublishAsync("account.created", eventModel, ct);

// FORBIDDEN: Direct RabbitMQ access (except Orchestrator)
channel.BasicPublish(...); // NO!
```

### State Management
```csharp
// REQUIRED: Use IStateStore<T> for state
var account = await _accountStore.GetAsync(accountId, ct);

// FORBIDDEN: Direct Redis/MySQL access (except Orchestrator)
await redis.StringGetAsync(key); // NO!
```
```

### 9.3 Updated Exception List

**Current Exceptions**: Orchestrator, Connect (partial), Voice

**Updated Exceptions**:
> **Orchestrator** uses direct infrastructure connections for bootstrap.
> This is the **only** exception to the mesh-first rule.

---

## 10. Timeline and Risks

### 10.1 Estimated Timeline

| Phase | Duration | Deliverables |
|-------|----------|--------------|
| Phase 1 | 2 weeks | lib-mesh plugin (YARP routing) |
| Phase 2 | 2 weeks | lib-messaging plugin (RabbitMQ) |
| Phase 3 | 1 week | lib-state plugin (Repository pattern) |
| Phase 4 | 1 week | Orchestrator extensions |
| Phase 5 | 2 weeks | Service migration (parallel operation) |
| Phase 6 | 1 week | Dapr removal, testing |
| **Total** | **9 weeks** | Complete Dapr replacement |

### 10.2 Risk Assessment

#### High Risk

| Risk | Mitigation |
|------|------------|
| Breaking existing services | Parallel operation phase, feature flags |
| Performance regression | Benchmark before/after, YARP is proven |
| Missing Dapr features | Comprehensive feature analysis complete |

#### Medium Risk

| Risk | Mitigation |
|------|------------|
| Test suite changes | Incremental mock migration |
| Configuration complexity | Centralized configuration in lib-state |
| Documentation lag | Update TENETS.md in each phase |

#### Low Risk

| Risk | Mitigation |
|------|------------|
| Team learning curve | Interfaces mirror Dapr patterns |
| Dependency conflicts | Separate plugin boundaries |

### 10.3 Success Criteria

1. **Functional Parity**: All current Dapr features work with native plugins
2. **Performance**: Equal or better latency and throughput
3. **Simplicity**: No sidecar containers required
4. **Reliability**: No CloudEvents leaks or sidecar restarts
5. **Testing**: All existing tests pass with new mocks

### 10.4 Rollback Plan

Feature flags enable instant rollback:

```bash
# Emergency rollback
BANNOU_USE_NATIVE_MESH=false
BANNOU_USE_NATIVE_MESSAGING=false
BANNOU_USE_NATIVE_STATE=false
BANNOU_DAPR_ENABLED=true
```

Services restart with Dapr configuration, no code changes required.

---

## 11. Current Mesh Implementation Analysis (Dapr Coupling Problem)

### 11.1 Critical Design Flaw

The current `lib-mesh` implementation has a **circular dependency on Dapr** - it's a Dapr replacement plugin that relies on Dapr for event publishing:

```csharp
// lib-mesh/MeshService.cs, Lines 642-646
await _daprClient.PublishEventAsync(
    "bannou-pubsub",
    "mesh.endpoint.registered",
    evt,
    cancellationToken);

// lib-mesh/MeshService.cs, Lines 672-678
await _daprClient.PublishEventAsync(
    "bannou-pubsub",
    "mesh.endpoint.deregistered",
    evt,
    cancellationToken);
```

### 11.2 Dapr Dependencies in lib-mesh

| File | Dependency | Line(s) | Purpose |
|------|------------|---------|---------|
| `MeshService.cs` | `DaprClient._daprClient` | 21 | Dependency injection |
| `MeshService.cs` | `PublishEventAsync()` | 642-646 | Endpoint registration event |
| `MeshService.cs` | `PublishEventAsync()` | 672-678 | Endpoint deregistration event |
| `MeshServiceEvents.cs` | `IEventConsumer` | 140-148 | Event subscription (Dapr-backed) |

### 11.3 Event Consumer Dependency

`MeshServiceEvents.cs` uses `IEventConsumer` which is backed by Dapr topic handlers:

```csharp
// lib-mesh/MeshServiceEvents.cs, Lines 140-148
protected void RegisterEventConsumers(IEventConsumer eventConsumer)
{
    eventConsumer.RegisterHandler<IMeshService, ServiceHeartbeatEvent>(
        "bannou-service-heartbeats",
        async (svc, evt) => await ((MeshService)svc).HandleServiceHeartbeatAsync(evt));

    eventConsumer.RegisterHandler<IMeshService, FullServiceMappingsEvent>(
        "bannou-full-service-mappings",
        async (svc, evt) => await ((MeshService)svc).HandleServiceMappingsAsync(evt));
}
```

### 11.4 Why This Cannot Be Fixed in Isolation

The mesh plugin cannot free itself from Dapr because:

1. **Event Publishing**: Requires `IMessageBus` from lib-messaging (doesn't exist yet)
2. **Event Subscription**: Requires `IMessageSubscriber` from lib-messaging (doesn't exist yet)
3. **Bootstrap Paradox**: Can't use mesh for messaging if mesh requires messaging

**Solution**: Implement lib-messaging and lib-state FIRST, then rewrite lib-mesh to use them.

---

## 12. lib-messaging Plugin Scaffolding

### 12.1 Schema Files Required

#### 12.1.1 messaging-api.yaml

```yaml
openapi: 3.0.3
info:
  title: Bannou Messaging Service API
  version: 1.0.0
  description: |
    Native RabbitMQ pub/sub messaging without Dapr CloudEvents overhead.

    NOTE: This is an INTERNAL service API. No x-permissions are defined because
    these endpoints are NOT exposed to WebSocket clients. Service-to-service
    calls within the cluster are always permitted (per TENETS Tenet 13).

servers:
  - url: http://localhost:3500/v1.0/invoke/bannou/method

paths:
  /messaging/publish:
    post:
      operationId: publishEvent
      summary: Publish an event to a topic
      # No x-permissions - internal service API only
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/PublishEventRequest'
      responses:
        '200':
          description: Event published successfully
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PublishEventResponse'

  /messaging/subscribe:
    post:
      operationId: createSubscription
      summary: Create a dynamic subscription to a topic
      # No x-permissions - internal service API only
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CreateSubscriptionRequest'
      responses:
        '200':
          description: Subscription created
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CreateSubscriptionResponse'

  /messaging/unsubscribe:
    post:
      operationId: removeSubscription
      summary: Remove a dynamic subscription
      # No x-permissions - internal service API only
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/RemoveSubscriptionRequest'
      responses:
        '200':
          description: Subscription removed
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/RemoveSubscriptionResponse'

  /messaging/list-topics:
    post:
      operationId: listTopics
      summary: List all known topics
      # No x-permissions - internal service API only
      # POST-only pattern per TENETS Tenet 1 (enables zero-copy WebSocket routing)
      requestBody:
        required: false
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListTopicsRequest'
      responses:
        '200':
          description: List of topics
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ListTopicsResponse'

components:
  schemas:
    PublishEventRequest:
      type: object
      required: [topic, payload]
      properties:
        topic:
          type: string
          description: Topic/routing key for the event
        payload:
          type: object
          description: Event payload (any JSON)
        options:
          $ref: '#/components/schemas/PublishOptions'

    PublishOptions:
      type: object
      properties:
        exchange:
          type: string
          default: bannou
        persistent:
          type: boolean
          default: true
        priority:
          type: integer
          minimum: 0
          maximum: 9
        correlationId:
          type: string
          format: uuid

    PublishEventResponse:
      type: object
      properties:
        success:
          type: boolean
        messageId:
          type: string
          format: uuid

    CreateSubscriptionRequest:
      type: object
      required: [topic, callbackUrl]
      properties:
        topic:
          type: string
        callbackUrl:
          type: string
          format: uri
          description: HTTP endpoint to receive events
        options:
          $ref: '#/components/schemas/SubscriptionOptions'

    SubscriptionOptions:
      type: object
      properties:
        durable:
          type: boolean
          default: false
        exclusive:
          type: boolean
          default: false
        autoAck:
          type: boolean
          default: true
        prefetchCount:
          type: integer
          default: 10

    CreateSubscriptionResponse:
      type: object
      properties:
        subscriptionId:
          type: string
          format: uuid
        queueName:
          type: string

    RemoveSubscriptionRequest:
      type: object
      required: [subscriptionId]
      properties:
        subscriptionId:
          type: string
          format: uuid

    RemoveSubscriptionResponse:
      type: object
      properties:
        success:
          type: boolean

    ListTopicsRequest:
      type: object
      description: Optional filters for topic listing (empty object returns all)
      properties:
        exchangeFilter:
          type: string
          description: Filter topics by exchange name prefix
        includeEmpty:
          type: boolean
          default: true
          description: Include topics with no messages

    ListTopicsResponse:
      type: object
      properties:
        topics:
          type: array
          items:
            $ref: '#/components/schemas/TopicInfo'

    TopicInfo:
      type: object
      properties:
        name:
          type: string
        messageCount:
          type: integer
        consumerCount:
          type: integer
```

#### 12.1.2 messaging-configuration.yaml

```yaml
# Messaging Service Configuration Schema
type: object
properties:
  RabbitMQ:
    type: object
    description: Direct RabbitMQ connection settings
    properties:
      Host:
        type: string
        default: rabbitmq
      Port:
        type: integer
        default: 5672
      Username:
        type: string
        default: guest
      Password:
        type: string
        default: guest
      VirtualHost:
        type: string
        default: /
      DefaultExchange:
        type: string
        default: bannou
      EnableConfirms:
        type: boolean
        default: true
      ConnectionRetryCount:
        type: integer
        default: 5
      ConnectionRetryDelayMs:
        type: integer
        default: 1000

  Subscriptions:
    type: object
    description: Default subscription settings
    properties:
      DefaultPrefetchCount:
        type: integer
        default: 10
      DefaultAutoAck:
        type: boolean
        default: false
      DeadLetterExchange:
        type: string
        default: bannou-dlx
      RetryMaxAttempts:
        type: integer
        default: 3
      RetryDelayMs:
        type: integer
        default: 5000

  FeatureFlags:
    type: object
    properties:
      UseMassTransit:
        type: boolean
        default: true
        description: Use MassTransit wrapper (true) or direct RabbitMQ.Client (false)
      EnableMetrics:
        type: boolean
        default: true
      EnableTracing:
        type: boolean
        default: true
```

#### 12.1.3 messaging-events.yaml

```yaml
# Events published by the Messaging service
components:
  schemas:
    MessagePublishedEvent:
      type: object
      description: Internal event for message tracking/debugging
      required: [event_id, timestamp, topic, message_id]
      properties:
        event_id:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        topic:
          type: string
        message_id:
          type: string
          format: uuid
        source_service:
          type: string
        payload_size_bytes:
          type: integer

    SubscriptionCreatedEvent:
      type: object
      required: [event_id, timestamp, subscription_id, topic]
      properties:
        event_id:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        subscription_id:
          type: string
          format: uuid
        topic:
          type: string
        subscriber_app_id:
          type: string

    SubscriptionRemovedEvent:
      type: object
      required: [event_id, timestamp, subscription_id]
      properties:
        event_id:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        subscription_id:
          type: string
          format: uuid
        reason:
          type: string
          enum: [explicit, timeout, error]
```

### 12.2 Core Interfaces

#### 12.2.1 IMessageBus (Publisher)

```csharp
/// <summary>
/// Native RabbitMQ message publishing without Dapr CloudEvents overhead.
/// Replaces DaprClient.PublishEventAsync() with direct RabbitMQ access.
/// </summary>
public interface IMessageBus
{
    /// <summary>
    /// Publish an event to a topic (routing key).
    /// </summary>
    /// <typeparam name="TEvent">Event type (serialized to JSON)</typeparam>
    /// <param name="topic">Topic/routing key</param>
    /// <param name="eventData">Event payload</param>
    /// <param name="options">Optional publish settings</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<Guid> PublishAsync<TEvent>(
        string topic,
        TEvent eventData,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : class;

    /// <summary>
    /// Publish raw bytes (for binary protocol optimization).
    /// </summary>
    Task<Guid> PublishRawAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        string contentType,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default);
}

public class PublishOptions
{
    public string Exchange { get; set; } = "bannou";
    public bool Persistent { get; set; } = true;
    public byte Priority { get; set; } = 0;
    public Guid? CorrelationId { get; set; }
    public TimeSpan? Expiration { get; set; }
    public Dictionary<string, object>? Headers { get; set; }
}
```

#### 12.2.2 IMessageSubscriber (Consumer)

```csharp
/// <summary>
/// Subscribe to RabbitMQ topics without Dapr topic handlers.
/// Integrates with existing IEventConsumer for fan-out.
/// </summary>
public interface IMessageSubscriber
{
    /// <summary>
    /// Create a static subscription (survives restarts).
    /// </summary>
    Task SubscribeAsync<TEvent>(
        string topic,
        Func<TEvent, CancellationToken, Task> handler,
        SubscriptionOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : class;

    /// <summary>
    /// Create a dynamic subscription (Connect service per-session).
    /// Returns IAsyncDisposable for cleanup.
    /// </summary>
    Task<IAsyncDisposable> SubscribeDynamicAsync<TEvent>(
        string topic,
        Func<TEvent, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
        where TEvent : class;

    /// <summary>
    /// Unsubscribe from a topic.
    /// </summary>
    Task UnsubscribeAsync(string topic);
}

public class SubscriptionOptions
{
    public bool Durable { get; set; } = true;
    public bool Exclusive { get; set; } = false;
    public bool AutoAck { get; set; } = false;
    public ushort PrefetchCount { get; set; } = 10;
    public bool UseDeadLetter { get; set; } = true;
}
```

### 12.3 MassTransit Integration

```csharp
/// <summary>
/// MassTransit-based implementation of IMessageBus.
/// Provides mature .NET abstractions over RabbitMQ.
/// </summary>
public class MassTransitMessageBus : IMessageBus
{
    private readonly IBus _bus;
    private readonly ILogger<MassTransitMessageBus> _logger;
    private readonly MessagingServiceConfiguration _configuration;

    public async Task<Guid> PublishAsync<TEvent>(
        string topic,
        TEvent eventData,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : class
    {
        var messageId = Guid.NewGuid();
        var exchange = options?.Exchange ?? _configuration.RabbitMQ.DefaultExchange;

        var endpoint = await _bus.GetSendEndpoint(
            new Uri($"exchange:{exchange}?bind=true&queue={topic}"));

        await endpoint.Send(eventData, context =>
        {
            context.MessageId = messageId;
            context.CorrelationId = options?.CorrelationId;
            if (options?.Expiration.HasValue == true)
                context.TimeToLive = options.Expiration.Value;
        }, cancellationToken);

        _logger.LogDebug(
            "Published {EventType} to {Topic} with MessageId {MessageId}",
            typeof(TEvent).Name, topic, messageId);

        return messageId;
    }
}
```

### 12.4 IEventConsumer Backend Integration

The existing `IEventConsumer` is **already Dapr-agnostic**. The migration scope is minimal:

**What Changes:**
1. ❌ DELETE: Generated `*EventsController.cs` files (Dapr `[Topic]` handlers)
2. ✅ ADD: `NativeEventConsumerBackend` IHostedService to bridge RabbitMQ → IEventConsumer
3. ✅ ADD: `EventSubscriptionRegistry` for topic→eventType mappings

**What Does NOT Change:**
- ✅ `IEventConsumer` interface - unchanged
- ✅ `EventConsumer` implementation - unchanged
- ✅ Handler registrations via `Register<TEvent>()` - unchanged
- ✅ All service event handlers - unchanged

#### 12.4.1 EventSubscriptionRegistry (Topic→Type Mapping)

The IEventConsumer stores handlers as `Func<IServiceProvider, object, Task>` - type information is lost after registration. We need a separate registry for deserialization:

```csharp
/// <summary>
/// Static registry mapping topic names to event types.
/// Required because IEventConsumer handlers are stored as object-typed delegates.
/// Generated from *-events.yaml schemas or registered at startup.
/// </summary>
public static class EventSubscriptionRegistry
{
    private static readonly ConcurrentDictionary<string, Type> _topicToEventType = new();

    /// <summary>
    /// Register a topic→eventType mapping.
    /// Called during service initialization (before subscriptions start).
    /// </summary>
    public static void Register<TEvent>(string topicName) where TEvent : class
    {
        _topicToEventType[topicName] = typeof(TEvent);
    }

    /// <summary>
    /// Get the event type for a topic, or null if not registered.
    /// </summary>
    public static Type? GetEventType(string topicName)
    {
        return _topicToEventType.TryGetValue(topicName, out var type) ? type : null;
    }

    /// <summary>
    /// Get all registered topic names.
    /// </summary>
    public static IEnumerable<string> GetRegisteredTopics() => _topicToEventType.Keys;
}
```

#### 12.4.2 NativeEventConsumerBackend (IHostedService)

```csharp
/// <summary>
/// Native RabbitMQ backend for IEventConsumer fan-out.
/// Replaces Dapr-generated EventsController files.
/// </summary>
public class NativeEventConsumerBackend : IHostedService
{
    private readonly IMessageSubscriber _subscriber;
    private readonly IEventConsumer _eventConsumer;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NativeEventConsumerBackend> _logger;
    private readonly List<IAsyncDisposable> _subscriptions = new();

    public NativeEventConsumerBackend(
        IMessageSubscriber subscriber,
        IEventConsumer eventConsumer,
        IServiceProvider serviceProvider,
        ILogger<NativeEventConsumerBackend> logger)
    {
        _subscriber = subscriber;
        _eventConsumer = eventConsumer;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Subscribe to each topic that has registered handlers
        foreach (var topic in _eventConsumer.GetRegisteredTopics())
        {
            var eventType = EventSubscriptionRegistry.GetEventType(topic);
            if (eventType == null)
            {
                _logger.LogWarning(
                    "Topic '{Topic}' has handlers but no event type registered. Skipping.",
                    topic);
                continue;
            }

            // Create typed subscription using reflection
            var method = typeof(NativeEventConsumerBackend)
                .GetMethod(nameof(SubscribeTypedAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(eventType);

            var subscription = await (Task<IAsyncDisposable>)method.Invoke(
                this, new object[] { topic, cancellationToken })!;

            _subscriptions.Add(subscription);
            _logger.LogInformation(
                "Subscribed to topic '{Topic}' with event type {EventType}",
                topic, eventType.Name);
        }
    }

    private async Task<IAsyncDisposable> SubscribeTypedAsync<TEvent>(
        string topic,
        CancellationToken cancellationToken) where TEvent : class
    {
        return await _subscriber.SubscribeDynamicAsync<TEvent>(
            topic,
            async (evt, ct) =>
            {
                // Create a scope for DI resolution
                using var scope = _serviceProvider.CreateScope();

                // Dispatch to all registered handlers (fan-out)
                await _eventConsumer.DispatchAsync(topic, evt, scope.ServiceProvider);
            },
            cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var subscription in _subscriptions)
        {
            await subscription.DisposeAsync();
        }
        _subscriptions.Clear();
    }
}
```

#### 12.4.3 Service Registration Pattern (Unchanged)

Existing handler registrations continue to work exactly as before:

```csharp
// In PermissionsServiceEvents.cs (UNCHANGED)
public static void RegisterEventHandlers(IEventConsumer eventConsumer)
{
    // These registrations continue to work
    eventConsumer.Register<ServiceRegistrationEvent>(
        "permissions.service-registered",
        "permissions:service-registered",
        async (sp, evt) =>
        {
            var svc = sp.GetRequiredService<IPermissionsService>();
            await svc.HandleServiceRegisteredAsync(evt);
        });

    eventConsumer.Register<SessionStateChangeEvent>(
        "permissions.session-state-changed",
        "permissions:session-state-changed",
        async (sp, evt) =>
        {
            var svc = sp.GetRequiredService<IPermissionsService>();
            await svc.HandleSessionStateChangedAsync(evt);
        });
}
```

#### 12.4.4 Type Registry Generation (MANDATORY)

The `EventSubscriptionRegistry` MUST be auto-generated from `x-event-subscriptions` in `*-events.yaml` schemas. This follows the schema-first principle - no manual registration.

**Generation Script Addition** (in `scripts/generate-event-subscriptions.sh`):

```bash
# For each service with x-event-subscriptions, generate registry entries
# Input: {service}-events.yaml with x-event-subscriptions
# Output: Generated/EventSubscriptionRegistration.Generated.cs
```

**Generated Output Example** (`bannou-service/Generated/EventSubscriptionRegistration.Generated.cs`):

```csharp
// <auto-generated>
// Generated from x-event-subscriptions in *-events.yaml files
// Do not edit manually - changes will be overwritten
// </auto-generated>

namespace BeyondImmersion.BannouService.Events;

/// <summary>
/// Auto-generated event type registrations for NativeEventConsumerBackend.
/// </summary>
public static class EventSubscriptionRegistration
{
    /// <summary>
    /// Register all event type mappings. Called during application startup.
    /// </summary>
    public static void RegisterAll()
    {
        // From permissions-events.yaml
        EventSubscriptionRegistry.Register<ServiceRegistrationEvent>("permissions.service-registered");
        EventSubscriptionRegistry.Register<SessionStateChangeEvent>("permissions.session-state-changed");

        // From auth-events.yaml
        EventSubscriptionRegistry.Register<SessionUpdatedEvent>("session.updated");
        EventSubscriptionRegistry.Register<SessionConnectedEvent>("session.connected");
        EventSubscriptionRegistry.Register<SessionDisconnectedEvent>("session.disconnected");
        EventSubscriptionRegistry.Register<SessionInvalidatedEvent>("session.invalidated");

        // From accounts-events.yaml
        EventSubscriptionRegistry.Register<AccountCreatedEvent>("account.created");
        EventSubscriptionRegistry.Register<AccountUpdatedEvent>("account.updated");
        EventSubscriptionRegistry.Register<AccountDeletedEvent>("account.deleted");

        // From common-events.yaml
        EventSubscriptionRegistry.Register<ServiceHeartbeatEvent>("bannou-service-heartbeats");
        EventSubscriptionRegistry.Register<FullServiceMappingsEvent>("bannou-full-service-mappings");
    }
}
```

**Startup Integration**:

```csharp
// In plugin initialization
EventSubscriptionRegistration.RegisterAll();
```

**Generation Pipeline Update** (Tenet 2, Step 8):

| Step | Source | Generated Output |
|------|--------|------------------|
| 8. Event Subscriptions | `x-event-subscriptions` in events.yaml | `{Service}ServiceEvents.cs` + `EventSubscriptionRegistration.Generated.cs` |

### 12.5 Directory Structure

```
lib-messaging/
├── Generated/
│   ├── MessagingController.Generated.cs
│   ├── IMessagingService.cs
│   ├── MessagingServiceConfiguration.cs
│   └── MessagingModels.cs
├── Services/
│   ├── IMessageBus.cs
│   ├── IMessageSubscriber.cs
│   ├── MassTransitMessageBus.cs
│   ├── MassTransitMessageSubscriber.cs
│   ├── RabbitMqMessageBus.cs          # Direct impl (fallback)
│   ├── RabbitMqMessageSubscriber.cs   # Direct impl (fallback)
│   └── NativeEventConsumerBackend.cs
├── MessagingService.cs
├── MessagingServicePlugin.cs
├── MessagingServiceEvents.cs
└── lib-messaging.csproj

schemas/
├── messaging-api.yaml
├── messaging-configuration.yaml
└── messaging-events.yaml
```

---

## 13. lib-state Plugin Scaffolding

### 13.1 Schema Files Required

#### 13.1.1 state-api.yaml

```yaml
openapi: 3.0.3
info:
  title: Bannou State Service API
  version: 1.0.0
  description: |
    Repository pattern state management with Redis and MySQL backends.

    NOTE: This is an INTERNAL service API. No x-permissions are defined because
    these endpoints are NOT exposed to WebSocket clients. Service-to-service
    calls within the cluster are always permitted (per TENETS Tenet 13).

    All endpoints use POST-only pattern per TENETS Tenet 1 to enable
    zero-copy WebSocket routing with static endpoint GUIDs.

servers:
  - url: http://localhost:3500/v1.0/invoke/bannou/method

paths:
  /state/get:
    post:
      operationId: getState
      summary: Get state value by key
      # No x-permissions - internal service API only
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetStateRequest'
      responses:
        '200':
          description: State value retrieved
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/GetStateResponse'
        '404':
          description: Key not found

  /state/save:
    post:
      operationId: saveState
      summary: Save state value
      # No x-permissions - internal service API only
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/SaveStateRequest'
      responses:
        '200':
          description: State saved
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SaveStateResponse'

  /state/delete:
    post:
      operationId: deleteState
      summary: Delete state value
      # No x-permissions - internal service API only
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/DeleteStateRequest'
      responses:
        '200':
          description: State deleted
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/DeleteStateResponse'

  /state/query:
    post:
      operationId: queryState
      summary: Query state (MySQL stores only)
      # No x-permissions - internal service API only
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/QueryStateRequest'
      responses:
        '200':
          description: Query results
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/QueryStateResponse'

  /state/bulk-get:
    post:
      operationId: bulkGetState
      summary: Bulk get multiple keys
      # No x-permissions - internal service API only
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/BulkGetStateRequest'
      responses:
        '200':
          description: Bulk results
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/BulkGetStateResponse'

  /state/list-stores:
    post:
      operationId: listStores
      summary: List configured state stores
      # No x-permissions - internal service API only
      requestBody:
        required: false
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListStoresRequest'
      responses:
        '200':
          description: List of stores
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ListStoresResponse'

components:
  schemas:
    GetStateRequest:
      type: object
      required: [storeName, key]
      properties:
        storeName:
          type: string
          description: Name of the state store
        key:
          type: string
          description: Key to retrieve

    GetStateResponse:
      type: object
      properties:
        value:
          type: object
        etag:
          type: string
        metadata:
          $ref: '#/components/schemas/StateMetadata'

    SaveStateRequest:
      type: object
      required: [storeName, key, value]
      properties:
        storeName:
          type: string
          description: Name of the state store
        key:
          type: string
          description: Key to save
        value:
          type: object
          description: Value to store
        options:
          $ref: '#/components/schemas/StateOptions'

    DeleteStateRequest:
      type: object
      required: [storeName, key]
      properties:
        storeName:
          type: string
          description: Name of the state store
        key:
          type: string
          description: Key to delete

    DeleteStateResponse:
      type: object
      properties:
        deleted:
          type: boolean

    SaveStateResponse:
      type: object
      properties:
        etag:
          type: string
        success:
          type: boolean

    StateOptions:
      type: object
      properties:
        ttl:
          type: integer
          description: TTL in seconds (Redis only)
        consistency:
          type: string
          enum: [strong, eventual]
          default: strong
        etag:
          type: string
          description: Optimistic concurrency check

    StateMetadata:
      type: object
      properties:
        createdAt:
          type: string
          format: date-time
        updatedAt:
          type: string
          format: date-time
        version:
          type: integer

    QueryStateRequest:
      type: object
      required: [storeName, filter]
      properties:
        storeName:
          type: string
          description: Name of the state store (must be MySQL backend)
        filter:
          type: object
          description: JSON filter (MongoDB-style)
        sort:
          type: array
          items:
            $ref: '#/components/schemas/SortField'
        page:
          type: integer
          default: 0
        pageSize:
          type: integer
          default: 100

    SortField:
      type: object
      properties:
        field:
          type: string
        order:
          type: string
          enum: [asc, desc]

    QueryStateResponse:
      type: object
      properties:
        results:
          type: array
          items:
            type: object
        totalCount:
          type: integer
        page:
          type: integer
        pageSize:
          type: integer

    BulkGetStateRequest:
      type: object
      required: [storeName, keys]
      properties:
        storeName:
          type: string
          description: Name of the state store
        keys:
          type: array
          items:
            type: string

    BulkGetStateResponse:
      type: object
      properties:
        items:
          type: array
          items:
            $ref: '#/components/schemas/BulkStateItem'

    BulkStateItem:
      type: object
      properties:
        key:
          type: string
        value:
          type: object
        etag:
          type: string
        found:
          type: boolean

    ListStoresRequest:
      type: object
      description: Optional filters for store listing (empty object returns all)
      properties:
        backendFilter:
          type: string
          enum: [redis, mysql]
          description: Filter stores by backend type
        includeStats:
          type: boolean
          default: false
          description: Include key counts (may be slow for large stores)

    ListStoresResponse:
      type: object
      properties:
        stores:
          type: array
          items:
            $ref: '#/components/schemas/StoreInfo'

    StoreInfo:
      type: object
      properties:
        name:
          type: string
        backend:
          type: string
          enum: [redis, mysql]
        keyCount:
          type: integer
```

#### 13.1.2 state-configuration.yaml

```yaml
# State Service Configuration Schema
type: object
properties:
  Stores:
    type: object
    description: Named state store configurations
    additionalProperties:
      $ref: '#/definitions/StoreConfig'

  Redis:
    type: object
    description: Redis connection settings
    properties:
      ConnectionString:
        type: string
        default: redis:6379
      Password:
        type: string
      Ssl:
        type: boolean
        default: false
      DatabaseIndex:
        type: integer
        default: 0
      ConnectTimeout:
        type: integer
        default: 5000
      SyncTimeout:
        type: integer
        default: 5000

  MySql:
    type: object
    description: MySQL connection settings
    properties:
      ConnectionString:
        type: string
      MaxPoolSize:
        type: integer
        default: 100
      ConnectionTimeout:
        type: integer
        default: 30

  Defaults:
    type: object
    properties:
      Consistency:
        type: string
        enum: [strong, eventual]
        default: strong
      EnableMetrics:
        type: boolean
        default: true
      EnableTracing:
        type: boolean
        default: true

definitions:
  StoreConfig:
    type: object
    required: [backend]
    properties:
      backend:
        type: string
        enum: [redis, mysql]
      keyPrefix:
        type: string
        description: Optional key prefix for namespacing
      defaultTtl:
        type: integer
        description: Default TTL in seconds (Redis only)
      tableName:
        type: string
        description: MySQL table name (MySQL only)
```

#### 13.1.3 state-events.yaml

```yaml
# Events published by the State service
components:
  schemas:
    StateChangedEvent:
      type: object
      description: Published when state changes (optional, for debugging)
      required: [event_id, timestamp, store_name, key, operation]
      properties:
        event_id:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        store_name:
          type: string
        key:
          type: string
        operation:
          type: string
          enum: [get, save, delete]
        etag:
          type: string
        source_service:
          type: string

    StoreMigrationEvent:
      type: object
      description: Published during store migration operations
      required: [event_id, timestamp, store_name, status]
      properties:
        event_id:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        store_name:
          type: string
        status:
          type: string
          enum: [started, progress, completed, failed]
        keys_processed:
          type: integer
        total_keys:
          type: integer
        error_message:
          type: string
```

### 13.2 Core Interfaces

#### 13.2.1 IStateStore<T>

```csharp
/// <summary>
/// Generic state store replacing DaprClient.GetStateAsync()/SaveStateAsync().
/// Supports both Redis (ephemeral) and MySQL (durable) backends.
/// </summary>
/// <typeparam name="TValue">Value type stored</typeparam>
public interface IStateStore<TValue>
    where TValue : class
{
    /// <summary>Get state by key.</summary>
    Task<TValue?> GetAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>Get state with ETag for optimistic concurrency.</summary>
    Task<(TValue? Value, string? ETag)> GetWithETagAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>Save state.</summary>
    Task SaveAsync(
        string key,
        TValue value,
        StateOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Save with ETag check (optimistic concurrency).</summary>
    Task<bool> TrySaveAsync(
        string key,
        TValue value,
        string etag,
        CancellationToken cancellationToken = default);

    /// <summary>Delete state.</summary>
    Task<bool> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>Check if key exists.</summary>
    Task<bool> ExistsAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>Bulk get multiple keys.</summary>
    Task<IReadOnlyDictionary<string, TValue>> GetBulkAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default);
}
```

#### 13.2.2 IQueryableStateStore<T> (MySQL Extension)

```csharp
/// <summary>
/// Queryable state store - extends IStateStore for MySQL backends.
/// Redis stores do NOT implement this interface.
/// </summary>
public interface IQueryableStateStore<TValue> : IStateStore<TValue>
    where TValue : class
{
    /// <summary>Query with LINQ expression.</summary>
    Task<IReadOnlyList<TValue>> QueryAsync(
        Expression<Func<TValue, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>Query with pagination.</summary>
    Task<PagedResult<TValue>> QueryPagedAsync(
        Expression<Func<TValue, bool>>? predicate,
        int page,
        int pageSize,
        Expression<Func<TValue, object>>? orderBy = null,
        bool descending = false,
        CancellationToken cancellationToken = default);

    /// <summary>Count matching entries.</summary>
    Task<long> CountAsync(
        Expression<Func<TValue, bool>>? predicate = null,
        CancellationToken cancellationToken = default);
}

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    long TotalCount,
    int Page,
    int PageSize);
```

#### 13.2.3 IStateStoreFactory

```csharp
/// <summary>
/// Factory for creating typed state stores.
/// Manages connection pools and store lifecycle.
/// </summary>
public interface IStateStoreFactory
{
    /// <summary>Get or create a state store for the named store.</summary>
    IStateStore<TValue> GetStore<TValue>(string storeName)
        where TValue : class;

    /// <summary>Get queryable store (MySQL only, throws for Redis).</summary>
    IQueryableStateStore<TValue> GetQueryableStore<TValue>(string storeName)
        where TValue : class;

    /// <summary>Check if store is configured.</summary>
    bool HasStore(string storeName);

    /// <summary>Get store backend type.</summary>
    StateBackend GetBackendType(string storeName);
}

public enum StateBackend { Redis, MySql }
```

### 13.3 Store Configuration Mapping

Map current Dapr state store components to lib-state configuration:

| Dapr Component YAML | lib-state Store Name | Backend |
|---------------------|---------------------|---------|
| `auth-statestore.yaml` | `auth` | Redis |
| `connect-statestore.yaml` | `connect` | Redis |
| `permissions-statestore.yaml` | `permissions` | Redis |
| `mysql-accounts-statestore.yaml` | `accounts` | MySQL |
| `mysql-character-statestore.yaml` | `character` | MySQL |
| `mysql-game-session-statestore.yaml` | `game-session` | MySQL |
| `mysql-location-statestore.yaml` | `location` | MySQL |
| `mysql-realm-statestore.yaml` | `realm` | MySQL |
| `mysql-relationship-statestore.yaml` | `relationship` | MySQL |
| `mysql-relationship-type-statestore.yaml` | `relationship-type` | MySQL |
| `mysql-servicedata-statestore.yaml` | `servicedata` | MySQL |
| `mysql-species-statestore.yaml` | `species` | MySQL |
| `mysql-subscriptions-statestore.yaml` | `subscriptions` | MySQL |

### 13.4 Redis Implementation

```csharp
/// <summary>
/// Redis-backed state store for ephemeral/session data.
/// </summary>
public class RedisStateStore<TValue> : IStateStore<TValue>
    where TValue : class
{
    private readonly IDatabase _database;
    private readonly string _keyPrefix;
    private readonly ILogger<RedisStateStore<TValue>> _logger;

    public async Task<TValue?> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var fullKey = $"{_keyPrefix}:{key}";
        var value = await _database.StringGetAsync(fullKey);

        if (value.IsNullOrEmpty)
            return null;

        return BannouJson.Deserialize<TValue>(value!);
    }

    public async Task SaveAsync(
        string key,
        TValue value,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var fullKey = $"{_keyPrefix}:{key}";
        var json = BannouJson.Serialize(value);
        var expiry = options?.Ttl;

        // Use MULTI/EXEC for atomicity with version
        var transaction = _database.CreateTransaction();
        _ = transaction.StringSetAsync(fullKey, json, expiry);
        _ = transaction.HashIncrementAsync($"{fullKey}:meta", "version", 1);
        _ = transaction.HashSetAsync($"{fullKey}:meta", "updated",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        await transaction.ExecuteAsync();
    }

    public async Task<(TValue? Value, string? ETag)> GetWithETagAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var fullKey = $"{_keyPrefix}:{key}";

        // Pipeline both gets
        var valueTask = _database.StringGetAsync(fullKey);
        var versionTask = _database.HashGetAsync($"{fullKey}:meta", "version");

        await Task.WhenAll(valueTask, versionTask);

        var value = await valueTask;
        var version = await versionTask;

        if (value.IsNullOrEmpty)
            return (null, null);

        return (
            BannouJson.Deserialize<TValue>(value!),
            version.HasValue ? version.ToString() : "0");
    }
}
```

### 13.5 MySQL Implementation

```csharp
/// <summary>
/// MySQL-backed state store for durable/queryable data.
/// Uses EF Core for query support.
/// </summary>
public class MySqlStateStore<TValue> : IQueryableStateStore<TValue>
    where TValue : class
{
    private readonly StateDbContext _context;
    private readonly string _tableName;
    private readonly ILogger<MySqlStateStore<TValue>> _logger;

    public async Task<TValue?> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var entry = await _context.Set<StateEntry>()
            .Where(e => e.StoreName == _tableName && e.Key == key)
            .FirstOrDefaultAsync(cancellationToken);

        if (entry == null)
            return null;

        return BannouJson.Deserialize<TValue>(entry.ValueJson);
    }

    public async Task<IReadOnlyList<TValue>> QueryAsync(
        Expression<Func<TValue, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        // Load all values for this store, deserialize, then filter
        // (For true efficiency, use SQL JSON functions in production)
        var entries = await _context.Set<StateEntry>()
            .Where(e => e.StoreName == _tableName)
            .ToListAsync(cancellationToken);

        var values = entries
            .Select(e => BannouJson.Deserialize<TValue>(e.ValueJson))
            .Where(v => v != null)
            .Cast<TValue>()
            .AsQueryable()
            .Where(predicate)
            .ToList();

        return values;
    }
}

/// <summary>EF Core entity for state storage.</summary>
public class StateEntry
{
    public string StoreName { get; set; } = default!;
    public string Key { get; set; } = default!;
    public string ValueJson { get; set; } = default!;
    public string ETag { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }
}
```

### 13.6 Directory Structure

```
lib-state/
├── Generated/
│   ├── StateController.Generated.cs
│   ├── IStateService.cs
│   ├── StateServiceConfiguration.cs
│   └── StateModels.cs
├── Services/
│   ├── IStateStore.cs
│   ├── IQueryableStateStore.cs
│   ├── IStateStoreFactory.cs
│   ├── RedisStateStore.cs
│   ├── MySqlStateStore.cs
│   ├── StateStoreFactory.cs
│   └── StateDbContext.cs
├── StateService.cs
├── StateServicePlugin.cs
├── StateServiceEvents.cs
└── lib-state.csproj

schemas/
├── state-api.yaml
├── state-configuration.yaml
└── state-events.yaml
```

---

## 14. Dapr Decoupling Strategy

### 14.1 Correct Implementation Order

The original plan had lib-mesh first - that was wrong. The correct order:

```
┌──────────────────────────────────────────────────────────────────────┐
│                    IMPLEMENTATION DEPENDENCY GRAPH                    │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│   Phase 1a: lib-messaging      Phase 1b: lib-state                   │
│   ┌─────────────────────┐      ┌─────────────────────┐               │
│   │ - IMessageBus       │      │ - IStateStore<T>    │               │
│   │ - IMessageSubscriber│      │ - IStateStoreFactory│               │
│   │ - MassTransit impl  │      │ - Redis/MySQL impls │               │
│   └──────────┬──────────┘      └──────────┬──────────┘               │
│              │                            │                          │
│              └────────────┬───────────────┘                          │
│                           │                                          │
│                           ▼                                          │
│   Phase 2: lib-mesh (REWRITE)                                        │
│   ┌────────────────────────────────────────┐                         │
│   │ - Remove DaprClient dependency         │                         │
│   │ - Use IMessageBus for events           │                         │
│   │ - Use IStateStore for routing state    │                         │
│   │ - YARP routing preserved               │                         │
│   └────────────────────────────────────────┘                         │
│                           │                                          │
│                           ▼                                          │
│   Phase 3: Migrate Existing Services                                 │
│   ┌────────────────────────────────────────┐                         │
│   │ - Auth → IStateStore (sessions)        │                         │
│   │ - Connect → IMessageBus (events)       │                         │
│   │ - Accounts → IQueryableStateStore      │                         │
│   │ - All services → IMeshClient           │                         │
│   └────────────────────────────────────────┘                         │
│                           │                                          │
│                           ▼                                          │
│   Phase 4: Remove Dapr                                               │
│   ┌────────────────────────────────────────┐                         │
│   │ - Delete sidecar containers            │                         │
│   │ - Delete component YAML files          │                         │
│   │ - Update TENETS.md                     │                         │
│   │ - Update Docker Compose                │                         │
│   └────────────────────────────────────────┘                         │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

### 14.2 Revised Timeline

| Phase | Duration | Deliverables |
|-------|----------|--------------|
| Phase 1a | 2 weeks | lib-messaging (MassTransit + direct RabbitMQ) |
| Phase 1b | 1 week | lib-state (Redis + MySQL backends) |
| Phase 2 | 1 week | lib-mesh rewrite (remove Dapr dependency) |
| Phase 3 | 3 weeks | Migrate all existing services |
| Phase 4 | 1 week | Remove Dapr, update documentation |
| **Total** | **8 weeks** | Complete Dapr replacement |

### 14.3 Feature Flags for Gradual Migration

```bash
# Phase 1: Enable new plugins alongside Dapr
BANNOU_MESSAGING_BACKEND=dapr    # Options: dapr, masstransit, rabbitmq
BANNOU_STATE_BACKEND=dapr        # Options: dapr, native

# Phase 2: Switch to native (Dapr still running as fallback)
BANNOU_MESSAGING_BACKEND=masstransit
BANNOU_STATE_BACKEND=native
BANNOU_MESH_BACKEND=native

# Phase 3: Disable Dapr completely
BANNOU_DAPR_ENABLED=false
```

### 14.4 Hybrid Operation During Migration

```csharp
/// <summary>
/// Hybrid message bus that can use Dapr or native based on config.
/// </summary>
public class HybridMessageBus : IMessageBus
{
    private readonly DaprClient? _daprClient;
    private readonly MassTransitMessageBus? _nativeBus;
    private readonly MessagingConfiguration _config;

    public async Task<Guid> PublishAsync<TEvent>(
        string topic,
        TEvent eventData,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : class
    {
        if (_config.Backend == MessagingBackend.Dapr)
        {
            await _daprClient!.PublishEventAsync("bannou-pubsub", topic, eventData, cancellationToken);
            return Guid.NewGuid(); // Dapr doesn't return message ID
        }
        else
        {
            return await _nativeBus!.PublishAsync(topic, eventData, options, cancellationToken);
        }
    }
}
```

### 14.5 Testing Strategy

1. **Unit Tests**: Mock all interfaces (IMessageBus, IStateStore, IMeshClient)
2. **Integration Tests**: Test each backend implementation independently
3. **Hybrid Tests**: Verify Dapr and native produce identical behavior
4. **Performance Tests**: Compare latency/throughput before and after

### 14.6 Rollback Capability

At any point during migration:

```bash
# Instant rollback to Dapr
BANNOU_MESSAGING_BACKEND=dapr
BANNOU_STATE_BACKEND=dapr
BANNOU_MESH_BACKEND=dapr
BANNOU_DAPR_ENABLED=true

# Restart services - no code changes required
docker compose restart bannou
```

---

## 15. Proposed TENET Updates

This section documents the specific changes required to TENETS.md to reflect the Dapr replacement.

### 15.1 Tenet 3: Event Consumer Fan-Out (Minor Update)

**Current Text** (Line 155):
> Generated Controllers receive Dapr events and dispatch via `IEventConsumer.DispatchAsync()`

**Proposed Change**:
> `NativeEventConsumerBackend` receives RabbitMQ events and dispatches via `IEventConsumer.DispatchAsync()`

**Update Generated Files Section** (Lines 185-186):
```
# Remove:
- `Generated/{Service}EventsController.cs` - Dapr topic handlers (always regenerated)

# Replace with:
- Events are received via `NativeEventConsumerBackend` (no generated controllers needed)
```

**All other aspects of Tenet 3 remain unchanged** - the `IEventConsumer` interface, handler registrations, and service patterns continue to work exactly as documented.

### 15.2 Tenet 4: Complete Rewrite (Dapr-First → Bannou Mesh)

**Current Title**: "Tenet 4: Dapr-First Infrastructure (MANDATORY)"

**Proposed New Title**: "Tenet 4: Bannou Mesh Infrastructure (MANDATORY)"

**Proposed New Content**:

```markdown
## Tenet 4: Bannou Mesh Infrastructure (MANDATORY)

**Rule**: Services MUST use Bannou mesh abstractions for all infrastructure concerns. Direct infrastructure access is reserved for infrastructure plugins only.

### State Management

```csharp
// REQUIRED: Use IStateStore<T> for state operations
await _stateStore.SaveAsync(key, value, new StateOptions { Ttl = TimeSpan.FromHours(1) });
var data = await _stateStore.GetAsync(key);

// For queryable stores (MySQL backend)
var results = await _queryableStore.QueryAsync(x => x.Status == "active");

// FORBIDDEN: Direct Redis/MySQL access (except lib-state)
var connection = new MySqlConnection(connectionString); // NO!
await redis.StringSetAsync(key, value);                 // NO!
```

### Pub/Sub Events

```csharp
// REQUIRED: Use IMessageBus for event publishing
await _messageBus.PublishAsync("entity.action", eventModel);

// FORBIDDEN: Direct RabbitMQ access (except lib-messaging)
channel.BasicPublish(...); // NO!
```

### Service Invocation

```csharp
// REQUIRED: Use generated service clients (now backed by IMeshClient)
var (statusCode, result) = await _accountsClient.GetAccountAsync(request, ct);

// FORBIDDEN: Manual HTTP construction
var response = await httpClient.PostAsync("http://accounts/api/..."); // NO!
```

### Service Interface Summary

| Abstraction | Replaces | Backend |
|-------------|----------|---------|
| `IStateStore<T>` | `DaprClient.GetStateAsync/SaveStateAsync` | Redis or MySQL |
| `IQueryableStateStore<T>` | Dapr state query (MySQL only) | MySQL + EF Core |
| `IMessageBus` | `DaprClient.PublishEventAsync` | RabbitMQ (MassTransit) |
| `IMessageSubscriber` | Dapr `[Topic]` handlers | RabbitMQ (MassTransit) |
| `IMeshClient` | `DaprClient.InvokeMethodAsync` | YARP direct HTTP |
| Generated Clients | (unchanged interface) | Uses IMeshClient internally |

### Infrastructure Plugin Exceptions

**lib-state** uses direct Redis and MySQL connections because it IS the state abstraction layer. All other services MUST use IStateStore<T>.

**lib-messaging** uses direct RabbitMQ connections (via MassTransit or RabbitMQ.Client) because it IS the messaging abstraction layer. All other services MUST use IMessageBus.

**Orchestrator** uses direct Redis and RabbitMQ for bootstrap operations that must occur before other plugins initialize. After bootstrap, Orchestrator SHOULD use lib-state and lib-messaging abstractions.

**Voice** uses direct connections to RTPEngine (UDP) and Kamailio (HTTP) for real-time media control that cannot be abstracted. Voice MUST use lib-state and lib-messaging for all other operations.

### No Longer Exceptions

- **Connect Service**: MUST use lib-messaging for dynamic subscriptions (IMessageSubscriber provides SubscribeDynamicAsync)
- **lib-mesh**: MUST use lib-messaging for events and lib-state for routing data
```

### 15.3 Tenet 1: Schema-First Development (Minor Update)

Update the Generated Files section to remove EventsController reference:

**Current** (Line 43):
```
│   └── {Service}EventsController.cs     # Dapr event handlers
```

**Proposed**:
```
│   └── (Removed - events handled by NativeEventConsumerBackend)
```

### 15.4 Tenet 2: Code Generation System (Minor Update)

Update Generation Pipeline Step 8:

**Current** (Line 74):
```
| 8. Event Subscriptions | `x-event-subscriptions` in events.yaml | `{Service}EventsController.cs` + `{Service}ServiceEvents.cs` |
```

**Proposed**:
```
| 8. Event Subscriptions | `x-event-subscriptions` in events.yaml | `{Service}ServiceEvents.cs` + `EventSubscriptionRegistration.Generated.cs` |
```

The `EventSubscriptionRegistration.Generated.cs` file is auto-generated and contains all topic→eventType mappings for `NativeEventConsumerBackend` deserialization. See Section 12.4.4 for implementation details.

### 15.5 Impact Summary

| Tenet | Change Type | Scope |
|-------|-------------|-------|
| Tenet 1 | Minor | Remove EventsController from file list |
| Tenet 2 | Minor | Update generation pipeline step 8 |
| Tenet 3 | Minor | Replace "Generated Controllers" with "NativeEventConsumerBackend" |
| Tenet 4 | **Complete Rewrite** | Replace Dapr patterns with mesh abstractions |

### 15.6 Backwards Compatibility

During migration (while BANNOU_DAPR_ENABLED=true is still an option):
- TENETS.md should document both patterns
- Hybrid implementations (Section 8.1, 14.4) support gradual transition
- Full TENET update applies only after Phase 4 (Dapr removal) is complete

---

## Appendix A: File Impact Analysis

### Files Requiring Changes (88+)

| Category | Count | Change Type |
|----------|-------|-------------|
| Service Implementations | 18 | Replace DaprClient injection |
| Generated Controllers | 18 | Update event handlers |
| Test Files | 20+ | Replace DaprClient mocks |
| Docker Compose | 12 | Remove sidecar containers |
| Component YAML | 17 | Delete Dapr components |
| **Total** | ~85 | Incremental migration |

### Generated Clients (No Changes Required)

NSwag-generated service clients (`*Client.cs`) use `DaprServiceClientBase` which can be updated to use `IMeshClient` internally without regenerating clients.

---

## Appendix B: Alternative Approaches Considered

### Alternative 1: Dapr Pluggable Components

**Approach**: Write custom Dapr components instead of replacing Dapr.

**Rejected Because**:
- Still requires sidecar infrastructure
- Still has CloudEvents overhead
- Doesn't solve network mode issues

### Alternative 2: gRPC Instead of HTTP

**Approach**: Use gRPC for service-to-service instead of YARP HTTP.

**Rejected Because**:
- WebSocket binary protocol already provides zero-copy routing
- HTTP aligns with existing DaprClient patterns (easier migration)
- gRPC would require more significant API changes

### Alternative 3: Kubernetes Service Mesh (Istio/Linkerd)

**Approach**: Use Kubernetes-native service mesh instead of Dapr.

**Rejected Because**:
- Requires Kubernetes (Bannou targets Compose/Swarm too)
- Heavier infrastructure than native plugins
- Less control over behavior

---

## Appendix C: Reference Implementation

### Orchestrator Direct Redis Pattern

The Orchestrator already implements the direct infrastructure pattern we're proposing:

```csharp
// From lib-orchestrator/OrchestratorRedisManager.cs
public class OrchestratorRedisManager : IOrchestratorRedisManager
{
    private readonly ConnectionMultiplexer _redis;
    private readonly IDatabase _database;

    public async Task StoreHeartbeatAsync(InstanceHealthStatus status)
    {
        var key = $"service:heartbeat:{status.AppId}";
        var json = BannouJson.Serialize(status);
        await _database.StringSetAsync(key, json, TimeSpan.FromSeconds(90));
    }

    public async Task StoreRoutingAsync(string serviceName, ServiceRouting routing)
    {
        var key = $"service:routing:{serviceName}";
        var json = BannouJson.Serialize(routing);
        await _database.StringSetAsync(key, json, TimeSpan.FromMinutes(5));
    }
}
```

This pattern works today and proves the architecture is viable.

---

*Document prepared through comprehensive codebase analysis and architectural research.*
*Requires review and approval before implementation.*
