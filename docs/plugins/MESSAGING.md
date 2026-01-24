# Messaging Plugin Deep Dive

> **Plugin**: lib-messaging
> **Schema**: schemas/messaging-api.yaml
> **Version**: 1.0.0
> **State Store**: messaging-external-subs (Redis, app-id keyed with TTL)

---

## Overview

The Messaging service is the native RabbitMQ pub/sub infrastructure for Bannou. It operates in a dual role: (1) as an internal infrastructure library (`IMessageBus`/`IMessageSubscriber`) used by all services for event publishing and subscription, and (2) as an HTTP API service providing dynamic subscription management with HTTP callback delivery. Supports in-memory mode for testing, direct RabbitMQ with channel pooling, retry buffering, and crash-fast philosophy for unrecoverable failures.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| RabbitMQ.Client 7.2.0 | Direct AMQP connection for publish/subscribe |
| lib-state (`IStateStoreFactory`) | Persisting external subscription metadata for recovery |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| Every service | Uses `IMessageBus` for event publishing and `IMessageSubscriber` for static subscriptions |
| lib-connect | Uses `IMessageBus` for client event routing |
| lib-actor | Uses `IMessageBus` for actor lifecycle, heartbeats, pool management |
| lib-documentation | Uses `IMessageBus` for git sync and search indexing |

All services depend on messaging infrastructure. The HTTP API (`IMessagingClient`) is rarely used directly.

---

## State Storage

**Store**: `messaging-external-subs` (Backend: Redis, keyed by app-id, TTL-based)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{appId}` | `List<ExternalSubscriptionData>` | Persisted HTTP callback subscriptions for recovery across restarts |

ExternalSubscriptionData contains: SubscriptionId, Topic, CallbackUrl, CreatedAt.

---

## Events

### Published Events

This service **intentionally publishes no lifecycle events**. It is infrastructure, not a domain service. Debug/monitoring events (MessagePublished, SubscriptionCreated/Removed) were planned but never implemented.

### Consumed Events

This plugin does not consume external events (it IS the event infrastructure).

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `UseInMemory` | `MESSAGING_USE_INMEMORY` | `false` | Use in-memory bus for testing |
| `RabbitMQHost` | `MESSAGING_RABBITMQ_HOST` | `"rabbitmq"` | RabbitMQ server hostname |
| `RabbitMQPort` | `MESSAGING_RABBITMQ_PORT` | `5672` | RabbitMQ AMQP port |
| `RabbitMQUsername` | `MESSAGING_RABBITMQ_USERNAME` | `"guest"` | Connection credentials |
| `RabbitMQPassword` | `MESSAGING_RABBITMQ_PASSWORD` | `"guest"` | Connection credentials |
| `RabbitMQVirtualHost` | `MESSAGING_RABBITMQ_VHOST` | `"/"` | Virtual host for isolation |
| `DefaultExchange` | `MESSAGING_DEFAULT_EXCHANGE` | `"bannou"` | Default publish exchange |
| `DeadLetterExchange` | `MESSAGING_DEAD_LETTER_EXCHANGE` | `"bannou-dlx"` | Dead-letter routing exchange |
| `ConnectionRetryCount` | `MESSAGING_CONNECTION_RETRY_COUNT` | `5` | Max connection attempts |
| `ConnectionRetryDelayMs` | `MESSAGING_CONNECTION_RETRY_DELAY_MS` | `1000` | Delay between retries |
| `DefaultPrefetchCount` | `MESSAGING_DEFAULT_PREFETCH_COUNT` | `10` | Consumer channel QoS |
| `DefaultAutoAck` | `MESSAGING_DEFAULT_AUTO_ACK` | `false` | Auto-acknowledge messages |
| `RetryBufferEnabled` | `MESSAGING_RETRY_BUFFER_ENABLED` | `true` | Enable publish failure buffering |
| `RetryBufferMaxSize` | `MESSAGING_RETRY_BUFFER_MAX_SIZE` | `10000` | Max buffered messages before crash |
| `RetryBufferMaxAgeSeconds` | `MESSAGING_RETRY_BUFFER_MAX_AGE_SECONDS` | `300` | Max message age before crash |
| `RetryBufferIntervalSeconds` | `MESSAGING_RETRY_BUFFER_INTERVAL_SECONDS` | `5` | Retry processing interval |
| `CallbackRetryMaxAttempts` | `MESSAGING_CALLBACK_RETRY_MAX_ATTEMPTS` | `3` | HTTP callback retry limit |
| `CallbackRetryDelayMs` | `MESSAGING_CALLBACK_RETRY_DELAY_MS` | `1000` | HTTP callback retry delay |
| `SubscriptionTtlRefreshIntervalHours` | `MESSAGING_SUBSCRIPTION_TTL_REFRESH_INTERVAL_HOURS` | `6` | TTL refresh period |
| `SubscriptionRecoveryStartupDelaySeconds` | `MESSAGING_SUBSCRIPTION_RECOVERY_STARTUP_DELAY_SECONDS` | `2` | Delay before recovery on startup |
| `RabbitMQNetworkRecoveryIntervalSeconds` | `MESSAGING_RABBITMQ_NETWORK_RECOVERY_INTERVAL_SECONDS` | `10` | Auto-recovery interval |
| `ExternalSubscriptionTtlSeconds` | `MESSAGING_EXTERNAL_SUBSCRIPTION_TTL_SECONDS` | `86400` | External subscription persistence TTL (24h) |

### Unused Configuration Properties

| Property | Env Var | Default | Notes |
|----------|---------|---------|-------|
| `EnablePublisherConfirms` | `MESSAGING_ENABLE_CONFIRMS` | `true` | Defined but never evaluated |
| `RetryMaxAttempts` | `MESSAGING_RETRY_MAX_ATTEMPTS` | `3` | Defined but not used in service code |
| `RetryDelayMs` | `MESSAGING_RETRY_DELAY_MS` | `5000` | Defined but not used in service code |
| `UseMassTransit` | `MESSAGING_USE_MASSTRANSIT` | `true` | Feature flag, never checked |
| `EnableMetrics` | `MESSAGING_ENABLE_METRICS` | `true` | Feature flag, never implemented |
| `EnableTracing` | `MESSAGING_ENABLE_TRACING` | `true` | Feature flag, never implemented |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `IMessageBus` | Singleton | Event publishing (RabbitMQ or InMemory) |
| `IMessageSubscriber` | Singleton | Topic subscriptions |
| `IMessageTap` | Singleton | Event tapping/observation |
| `RabbitMQConnectionManager` | Singleton | Connection pooling (up to 10 channels) |
| `MessageRetryBuffer` | Singleton | Transient publish failure recovery |
| `NativeEventConsumerBackend` | HostedService | Bridges IEventConsumer fan-out to RabbitMQ |
| `MessagingSubscriptionRecoveryService` | HostedService | Recovers external subscriptions on startup |
| `MessagingService` | Singleton | HTTP API implementation |

Service lifetime is **Singleton** (infrastructure must persist across requests).

---

## API Endpoints (Implementation Notes)

### Publish (`/messaging/publish`)

Wraps arbitrary JSON payloads in `GenericMessageEnvelope` (MassTransit requires concrete types). Normalizes `Guid.Empty` to null for CorrelationId. Delegates to `IMessageBus.TryPublishAsync()`. Supports exchange types: fanout, direct, topic (default). Supports persistent flag, priority (0-9), expiration (ISO 8601 duration), and custom headers.

### Subscribe (`/messaging/subscribe`)

Creates dynamic HTTP callback subscriptions. Generates unique queue name `bannou-dynamic-{subscriptionId:N}`. Stores subscription in both in-memory dictionary (for disposal) and state store (for recovery). Retry logic: retries on `HttpRequestException` and `TaskCanceledException` only; HTTP 4xx/5xx treated as successful delivery. Subscription options: durable, exclusive, autoAck, prefetchCount, useDeadLetter, consumerGroup.

### Unsubscribe (`/messaging/unsubscribe`)

Removes subscription from in-memory tracking and persistent state store. Disposes the subscription handle and HttpClient. Returns NotFound if subscription not active.

### List Topics (`/messaging/list-topics`)

Returns topics from active subscriptions only. MessageCount always 0 (would require RabbitMQ management API). Only sees topics with active subscriptions on current node. Filters by exchangeFilter prefix. Counts consumers per topic from active subscriptions.

---

## Visual Aid

```
Messaging Architecture
========================

                              ┌──────────────────────┐
                              │    RabbitMQ Broker    │
                              │                      │
                              │  Exchange: "bannou"  │
                              │  DLX: "bannou-dlx"   │
                              └──────────┬───────────┘
                                         │
              ┌──────────────────────────┼──────────────────────────┐
              │                          │                          │
    ┌─────────▼─────────┐    ┌──────────▼──────────┐    ┌─────────▼─────────┐
    │  RabbitMQMessageBus │    │RabbitMQMessageSubscriber│    │ MessageRetryBuffer │
    │  (IMessageBus)      │    │ (IMessageSubscriber)    │    │                    │
    │                     │    │                         │    │ Buffers failed     │
    │ TryPublishAsync()   │    │ Static subscriptions    │    │ publishes          │
    │ TryPublishRawAsync()│    │ Dynamic subscriptions   │    │ Crash-fast if      │
    │ TryPublishErrorAsync│    │ Raw subscriptions       │    │ buffer exceeds     │
    └─────────────────────┘    └──────────┬─────────────┘    │ limits             │
              ▲                           │                   └────────────────────┘
              │                           ▼
              │               ┌───────────────────────┐
              │               │NativeEventConsumerBackend│
              │               │ (IHostedService)        │
              │               │                         │
              │               │ Bridges RabbitMQ to     │
              │               │ IEventConsumer fan-out  │
              │               │ (per-plugin handlers)   │
              │               └─────────────────────────┘
              │
    ┌─────────┴─────────┐
    │ RabbitMQConnection │
    │ Manager            │
    │                    │
    │ Single connection  │
    │ Channel pool (10)  │
    │ Auto-recovery      │
    └────────────────────┘
```

---

## Stubs & Unimplemented Features

1. **Publisher confirms**: `EnablePublisherConfirms` config exists but confirmations are not implemented.
2. **Metrics collection**: `EnableMetrics` flag exists but no instrumentation code.
3. **Distributed tracing**: `EnableTracing` flag exists but no Activity/DiagnosticSource usage.
4. **Lifecycle events**: MessagePublished, SubscriptionCreated/Removed were planned but never implemented.
5. **ListTopics MessageCount**: Always returns 0 (would require RabbitMQ Management HTTP API).

---

## Potential Extensions

1. **RabbitMQ Management API integration**: Enable accurate message counts and queue depth monitoring.
2. **Publisher confirm support**: Add reliability guarantees for critical event publishing.
3. **Prometheus metrics**: Publish/subscribe rates, buffer depth, retry counts.
4. **Dead-letter processing**: Consumer for DLX queue to handle poison messages.

---

## Tenet Violations (Fix Immediately)

### 1. IMPLEMENTATION TENETS (T9): HashSet without ConcurrentDictionary for Thread-Safe Cache

**Files**:
- `/home/lysander/repos/bannou/plugins/lib-messaging/Services/RabbitMQMessageBus.cs` (line 42)
- `/home/lysander/repos/bannou/plugins/lib-messaging/Services/MessageRetryBuffer.cs` (line 291)
- `/home/lysander/repos/bannou/plugins/lib-messaging/Services/RabbitMQMessageTap.cs` (line 37)

**What's wrong**: `HashSet<string>` is used for `_declaredExchanges` caching. While these are protected by `lock (_exchangeLock)`, the tenet states: "Use ConcurrentDictionary for local caches, never plain Dictionary." `HashSet<string>` has the same thread-safety concerns as `Dictionary` and is equally forbidden. The lock-based pattern also introduces potential contention issues. Should use `ConcurrentDictionary<string, byte>` (or similar) as a thread-safe set.

**Fix**: Replace `HashSet<string>` + `object _exchangeLock` with `ConcurrentDictionary<string, byte>` and use `TryAdd` for thread-safe insertion, removing the explicit locks.

---

### 2. IMPLEMENTATION TENETS (T21): Six Unused Configuration Properties (Dead Config)

**File**: `/home/lysander/repos/bannou/plugins/lib-messaging/Generated/MessagingServiceConfiguration.cs` (lines 102, 138, 144, 186, 192, 198)

**What's wrong**: Six configuration properties are defined in the schema but never evaluated in any service code:
- `EnablePublisherConfirms` (line 102) - Defined but publisher confirms are not implemented
- `RetryMaxAttempts` (line 138) - Defined but not used anywhere
- `RetryDelayMs` (line 144) - Defined but not used anywhere
- `UseMassTransit` (line 186) - Feature flag never checked
- `EnableMetrics` (line 192) - Feature flag never implemented
- `EnableTracing` (line 198) - Feature flag never implemented

T21 states: "No Dead Configuration: Every defined config property MUST be referenced in service code." and "Unused configuration property: Wire up in service or remove from schema."

**Fix**: Remove these six properties from `schemas/messaging-configuration.yaml` and regenerate, OR implement the features they control.

---

### 3. IMPLEMENTATION TENETS (T21): Hardcoded Tunables

**File**: `/home/lysander/repos/bannou/plugins/lib-messaging/Services/RabbitMQConnectionManager.cs`

**What's wrong (line 34)**: `MAX_POOL_SIZE = 10` is a hardcoded constant representing a capacity limit. T21 states: "Any numeric literal that represents a limit, timeout, threshold, or capacity is a sign that a configuration property needs to exist."

**What's wrong (line 114)**: `60000` is a hardcoded maximum backoff cap for exponential retry delay. This is a tunable threshold that should be configurable.

**Fix**: Add `ChannelPoolMaxSize` and `ConnectionRetryMaxDelayMs` properties to the messaging configuration schema.

---

### 4. IMPLEMENTATION TENETS (T23): Synchronous Blocking Call (.GetAwaiter().GetResult())

**File**: `/home/lysander/repos/bannou/plugins/lib-messaging/Services/RabbitMQConnectionManager.cs` (line 183)

**What's wrong**: `channel.CloseAsync().GetAwaiter().GetResult()` is used in `ReturnChannel()`. T23 explicitly states: "`.Result` or `.Wait()` on Task: Use await instead." `.GetAwaiter().GetResult()` is equivalent to `.Result` and blocks the thread.

**Fix**: Make `ReturnChannel` async (`ReturnChannelAsync`) and use `await channel.CloseAsync()`, or use `ValueTask`-based disposal. If the method signature cannot be changed, queue channels for async background cleanup.

---

### 5. QUALITY TENETS (T10): Bracket Tag Prefixes in Log Messages

**File**: `/home/lysander/repos/bannou/plugins/lib-messaging/Services/InMemoryMessageBus.cs` (lines 46, 56, 76, 83, 103)

**What's wrong**: Log messages use `[InMemory]` bracket tag prefix, e.g.:
- `"[InMemory] Published to topic '{Topic}': {EventType} (id: {MessageId})"`
- `"[InMemory] Failed to publish to topic '{Topic}'"`
- `"[InMemory] Published raw to topic '{Topic}': ..."`
- `"[InMemory] Failed to publish raw to topic '{Topic}'"`
- `"[InMemory] Error event from {ServiceName}/{Operation}: ..."`

T10 explicitly forbids `[TAG]` prefixes: "No bracket tag prefixes - the logger already includes service/class context."

**Fix**: Remove the `[InMemory]` prefix from all log messages. The class name `InMemoryMessageBus` already provides context via structured logging infrastructure.

---

### 6. IMPLEMENTATION TENETS (T21): Hardcoded State Store Name Instead of StateStoreDefinitions Constant

**File**: `/home/lysander/repos/bannou/plugins/lib-messaging/MessagingService.cs` (lines 74, 120)

**What's wrong**: The state store name is declared as a local constant `ExternalSubscriptionStoreName = "messaging-external-subs"` and used directly instead of referencing the generated `StateStoreDefinitions.MessagingExternalSubs` constant. T4 states: "ALWAYS use StateStoreDefinitions constants for store names (schema-first)" and "FORBIDDEN: Hardcoded store names."

**Fix**: Replace `ExternalSubscriptionStoreName` constant with `StateStoreDefinitions.MessagingExternalSubs` from the generated code. Add `using BeyondImmersion.BannouService.State;` if not already present.

---

### 7. IMPLEMENTATION TENETS (T21): Global Static Access for ServiceId

**File**: `/home/lysander/repos/bannou/plugins/lib-messaging/Services/RabbitMQMessageBus.cs` (line 289)

**What's wrong**: `Program.ServiceGUID` is accessed directly as a global static to populate `ServiceId` in `TryPublishErrorAsync()`. T21 states: "Direct `Environment.GetEnvironmentVariable`: Use service configuration class." The same principle applies to global statics -- service identity should be injected via configuration, not read from a global.

**Fix**: Inject `AppConfiguration` or the service's configuration class and use `appConfiguration.ServiceId` or equivalent injected value instead of the global static `Program.ServiceGUID`.

---

### 8. FOUNDATION TENETS (T6): Missing Null Checks in Constructor

**File**: `/home/lysander/repos/bannou/plugins/lib-messaging/MessagingService.cs` (lines 105-122)

**What's wrong**: The constructor assigns dependencies without null checks using `?? throw new ArgumentNullException(...)`. T6 shows the standard pattern requires explicit null validation:
```csharp
_messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
```
But the constructor just assigns directly:
```csharp
_logger = logger;
_configuration = configuration;
_messageBus = messageBus;
```

**Fix**: Add `?? throw new ArgumentNullException(nameof(...))` to all constructor parameter assignments, matching the T6 pattern.

---

### 9. IMPLEMENTATION TENETS (T9): Non-Thread-Safe List in NativeEventConsumerBackend

**File**: `/home/lysander/repos/bannou/plugins/lib-messaging/Services/NativeEventConsumerBackend.cs` (line 38)

**What's wrong**: `private readonly List<IAsyncDisposable> _subscriptions = new();` is a plain `List<>` used to track active subscriptions. While it is primarily written during `StartAsync` and read during `StopAsync`, T9 states "Use ConcurrentDictionary for local caches, never plain Dictionary" -- the same principle applies to `List<>` which is not thread-safe. If `StopAsync` is called concurrently with a late-running `StartAsync`, data corruption could occur.

**Fix**: Use a thread-safe collection (e.g., `ConcurrentBag<IAsyncDisposable>`) or add explicit synchronization.

---

### 10. QUALITY TENETS (T19): Missing XML Documentation on Public/Internal Members

**Files**:
- `/home/lysander/repos/bannou/plugins/lib-messaging/Services/InMemoryMessageBus.cs` (line 25): Constructor missing `<param>` documentation
- `/home/lysander/repos/bannou/plugins/lib-messaging/Services/RabbitMQMessageSubscriber.cs` (line 494): `BuildQueueName` method missing `<param>` and `<returns>` documentation
- `/home/lysander/repos/bannou/plugins/lib-messaging/Services/RabbitMQMessageSubscriber.cs` (line 506): `CreateDeadLetterArguments` method missing `<returns>` documentation
- `/home/lysander/repos/bannou/plugins/lib-messaging/Services/RabbitMQConnectionManager.cs` (line 227): `BuildConnectionString` method missing `<returns>` documentation

**What's wrong**: T19 requires all public/internal members to have `<summary>`, `<param>`, and `<returns>` documentation.

**Fix**: Add missing XML documentation to each method.

---

### 11. IMPLEMENTATION TENETS (T9): Non-Thread-Safe List in InMemoryMessageBus Subscriptions

**File**: `/home/lysander/repos/bannou/plugins/lib-messaging/Services/InMemoryMessageBus.cs` (line 22)

**What's wrong**: The subscription storage uses `ConcurrentDictionary<string, List<Func<...>>>`. While the `ConcurrentDictionary` itself is thread-safe, the inner `List<Func<...>>` values are plain `List<>` which are NOT thread-safe for concurrent reads/writes. The code uses `lock (_subscriptionLock)` around access, but T9 forbids plain `List<>` / `Dictionary<>` for in-memory state that could be accessed concurrently. This is a potential race condition if `DeliverToSubscribersAsync` is called while a new subscription is being added (the lock helps, but `handlers.ToList()` inside the lock is a workaround for an inherently unsafe design).

**Fix**: Use a thread-safe collection for the inner list (e.g., `ConcurrentBag<Func<...>>`) or replace with `ImmutableList<>` using Interlocked swaps.

---

### 12. IMPLEMENTATION TENETS (T7): Missing ApiException Distinction in Error Handling

**File**: `/home/lysander/repos/bannou/plugins/lib-messaging/MessagingService.cs` (lines 162, 302, 353, 394)

**What's wrong**: All catch blocks in the service methods catch only generic `Exception`, without first catching `ApiException` for expected API errors. T7 requires the pattern:
```csharp
catch (ApiException ex)  // Expected API error - log as warning
catch (Exception ex)     // Unexpected - log as error, emit error event
```
The current pattern goes straight to `catch (Exception ex)` with LogError + TryPublishErrorAsync, which means expected API failures from `IStateStore` calls would be treated as unexpected errors.

**Fix**: Add `catch (ApiException ex)` blocks before the generic `catch (Exception ex)` blocks, logging at Warning level and returning the appropriate status code without emitting error events.

---

### 13. IMPLEMENTATION TENETS (T20): Direct System.Text.Json Usage (JsonDocument.Parse)

**File**: `/home/lysander/repos/bannou/plugins/lib-messaging/Services/RabbitMQMessageTap.cs` (line 215)

**What's wrong**: `JsonDocument.Parse(rawJsonPayload)` uses `System.Text.Json` directly to parse JSON. T20 states: "All JSON serialization and deserialization MUST use `BannouJson` helper methods. Direct use of `JsonSerializer` is forbidden." While `JsonDocument` is not `JsonSerializer`, the spirit of T20 is that all JSON handling should use centralized configuration. The `using System.Text.Json;` import (line 11) enables this direct usage.

**Note**: This is a borderline case. `JsonDocument.Parse` is used for low-level property extraction (not full deserialization), which `BannouJson` doesn't directly support. However, the tenet's intent is to prevent inconsistent JSON handling. If `BannouJson` doesn't provide document-level parsing, this may be an acceptable exception that should be explicitly documented.

**Fix**: Either add a `BannouJson.ParseDocument()` helper or document this as an acceptable exception with a code comment explaining why `BannouJson` cannot be used here.

---

### 14. IMPLEMENTATION TENETS (T24): Non-Disposal of SemaphoreSlim in RabbitMQConnectionManager on Error Paths

**File**: `/home/lysander/repos/bannou/plugins/lib-messaging/Services/RabbitMQConnectionManager.cs` (line 31)

**What's wrong**: `_connectionLock` is a `SemaphoreSlim` that is only disposed in `DisposeAsync()`. If the class is never properly disposed (e.g., during a crash path), the semaphore leaks. More critically, in `InitializeAsync`, the lock is acquired with `WaitAsync` and released in a `finally` block -- this is correct, but `ReturnChannel` (line 183) uses `.GetAwaiter().GetResult()` which could deadlock if called from a synchronization context, potentially preventing the semaphore from ever being released.

**Fix**: This is addressed by fixing violation #4 (the `.GetAwaiter().GetResult()` call). The disposal pattern itself is acceptable for class-owned resources per T24.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None identified.

### Intentional Quirks (Documented Behavior)

1. **Crash-fast buffer philosophy**: If the retry buffer exceeds `RetryBufferMaxSize` (10,000) or oldest message exceeds `RetryBufferMaxAgeSeconds` (300s), the node calls `Environment.FailFast()`. Philosophy: better to crash and restart than silently drop events.

2. **HTTP callbacks don't retry on HTTP errors**: Only retries on network failures (`HttpRequestException`, `TaskCanceledException`). HTTP 4xx/5xx is treated as successful delivery. Philosophy: callback endpoint is responsible for its own error handling.

3. **GenericMessageEnvelope wrapping**: The Publish HTTP API wraps payloads in `GenericMessageEnvelope` because the underlying messaging system requires concrete types. Internal `IMessageBus.TryPublishAsync<T>()` uses proper typed events.

4. **Channel pool size fixed at 10**: `RabbitMQConnectionManager` uses `MAX_POOL_SIZE = 10` hardcoded constant. Not configurable. Consumer channels are separate (not pooled).

5. **Two queue naming schemes**: HTTP API subscriptions use `bannou-dynamic-{id:N}` while internal dynamic subscriptions use `{topic}.dynamic.{id:N}`. Different schemes for different subscription paths.

6. **Recovery startup delay**: External subscription recovery waits `SubscriptionRecoveryStartupDelaySeconds` (default 2s) before attempting recovery. Prevents race conditions with RabbitMQ connection establishment.

7. **Exchange caching**: Both `RabbitMQMessageBus` and `MessageRetryBuffer` cache declared exchanges in HashSets to avoid redundant `ExchangeDeclareAsync` calls. Thread-safe via locks.

### Design Considerations (Requires Planning)

1. **Callback retry logic duplication**: The same retry logic appears in both `CreateSubscriptionAsync` and `RecoverExternalSubscriptionsAsync`. Changes must be applied in two places. Could be extracted to a shared private method.

2. **In-memory mode limitations**: `InMemoryMessageBus` delivers asynchronously via a discarded task (`_ = DeliverToSubscribersAsync(...)`, fire-and-forget). Subscriptions use `List<Func<object, ...>>` which is not fully representative of RabbitMQ semantics (no queue persistence, no dead-letter, no prefetch).

3. **No graceful drain on shutdown**: `DisposeAsync` iterates subscriptions without timeout. A hung subscription disposal could hang the entire shutdown process.

4. **ServiceId from global static**: `RabbitMQMessageBus.TryPublishErrorAsync()` accesses `Program.ServiceGUID` directly (global variable) rather than injecting it via configuration.

5. **Six unused config properties**: `EnablePublisherConfirms`, `RetryMaxAttempts`, `RetryDelayMs`, `UseMassTransit`, `EnableMetrics`, `EnableTracing` are all defined but never evaluated. Should be wired up or removed.
