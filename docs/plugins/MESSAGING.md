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

7. **Exchange caching**: Both `RabbitMQMessageBus` and `MessageRetryBuffer` cache declared exchanges in ConcurrentDictionaries to avoid redundant `ExchangeDeclareAsync` calls. Lock-free thread-safe access.

### Design Considerations (Requires Planning)

1. **Callback retry logic duplication**: The same retry logic appears in both `CreateSubscriptionAsync` and `RecoverExternalSubscriptionsAsync`. Changes must be applied in two places. Could be extracted to a shared private method.

2. **In-memory mode limitations**: `InMemoryMessageBus` delivers asynchronously via a discarded task (`_ = DeliverToSubscribersAsync(...)`, fire-and-forget). Subscriptions use `List<Func<object, ...>>` which is not fully representative of RabbitMQ semantics (no queue persistence, no dead-letter, no prefetch).

3. **No graceful drain on shutdown**: `DisposeAsync` iterates subscriptions without timeout. A hung subscription disposal could hang the entire shutdown process.

4. **ServiceId from global static**: `RabbitMQMessageBus.TryPublishErrorAsync()` accesses `Program.ServiceGUID` directly (global variable) rather than injecting it via configuration.

5. **Six unused config properties**: `EnablePublisherConfirms`, `RetryMaxAttempts`, `RetryDelayMs`, `UseMassTransit`, `EnableMetrics`, `EnableTracing` are all defined but never evaluated. Should be wired up or removed.

6. **Hardcoded tunables in RabbitMQConnectionManager**: `MAX_POOL_SIZE = 10` and max backoff `60000ms` are hardcoded. Would require schema changes to make configurable.

8. **GetAwaiter().GetResult() in ReturnChannel**: `RabbitMQConnectionManager.ReturnChannel()` uses synchronous blocking on async disposal. Could cause deadlocks in certain synchronization contexts. Requires method signature change to fix properly.

9. **Non-thread-safe List in NativeEventConsumerBackend**: `List<IAsyncDisposable> _subscriptions` is only written in StartAsync and read in StopAsync. Race between late startup and early shutdown is possible but unlikely in practice.

10. **JsonDocument.Parse direct usage**: `RabbitMQMessageTap` uses `JsonDocument.Parse` directly for low-level property extraction. BannouJson doesn't provide document-level parsing, so this is an acceptable boundary exception.
