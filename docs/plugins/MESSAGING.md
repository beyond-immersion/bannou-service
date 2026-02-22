# Messaging Plugin Deep Dive

> **Plugin**: lib-messaging
> **Schema**: schemas/messaging-api.yaml
> **Version**: 1.0.0
> **Layer**: Infrastructure
> **State Store**: messaging-external-subs (Redis, app-id keyed with TTL)

---

## Overview

The Messaging service (L0 Infrastructure) is the native RabbitMQ pub/sub infrastructure for Bannou. Operates in a dual role: as the `IMessageBus`/`IMessageSubscriber` infrastructure library used by all services for event publishing and subscription, and as an HTTP API providing dynamic subscription management with HTTP callback delivery. Supports in-memory mode for testing, direct RabbitMQ with channel pooling, and aggressive retry buffering with crash-fast philosophy for unrecoverable failures.

## Event Publishing Reliability Model

**Key behavior**: `TryPublishAsync` returns `true` even when RabbitMQ is unavailable - because the message is buffered for retry and WILL be delivered when the connection recovers. This is **not** "fire-and-forget" or "best-effort" in the traditional sense:

1. **On publish failure**: Message is buffered in `MessageRetryBuffer` (in-memory `ConcurrentQueue`)
2. **Retry processing**: Every 5 seconds, buffered messages are retried
3. **Backpressure**: When buffer reaches 80% full (`RetryBufferBackpressureThreshold`), new publishes are rejected
4. **Crash-fast on prolonged failure**: If RabbitMQ stays down too long (buffer >500k messages OR oldest message >10 minutes), the node **crashes intentionally** via `IProcessTerminator.TerminateProcess()`
5. **Why crash?** Makes failure visible in monitoring, triggers orchestrator restart, prevents silent data loss

**True loss scenarios** (rare):
- Node dies (power failure, OOM kill) before buffer flushes
- Clean shutdown with non-empty buffer (logged as warning)
- Serialization failure (programming bug, not retryable)
- Backpressure active and caller doesn't handle `false` return

**Return value semantics**:
- `true` = Published successfully OR buffered for retry (delivery will happen)
- `false` = Unrecoverable failure (serialization error, backpressure active, retry buffer disabled)

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| RabbitMQ.Client 7.2.0 | Direct AMQP connection for publish/subscribe |
| lib-state (`IStateStoreFactory`) | Persisting external subscription metadata for recovery |
| lib-core (`BannouJson`) | Consistent JSON serialization via `BannouJson.Serialize/Deserialize` |
| lib-core (`IBannouEvent`) | Event interface for generic envelope pattern |
| lib-telemetry (`ITelemetryProvider`) | OpenTelemetry traces and metrics (NullProvider if telemetry disabled) |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| Every service | Uses `IMessageBus` for event publishing and `IMessageSubscriber` for static subscriptions |
| lib-connect | Uses `IMessageBus` for client event routing; uses `IMessageTap` for session-scoped event forwarding |
| lib-actor | Uses `IMessageBus` for actor lifecycle, heartbeats, pool management |
| lib-documentation | Uses `IMessageBus` for git sync and search indexing |
| lib-permission | Uses `IMessageBus` for permission registration broadcasts |

All services depend on messaging infrastructure. The HTTP API (`IMessagingClient`) is rarely used directly.

---

## State Storage

**Store**: `messaging-external-subs` (Backend: Redis, keyed by app-id, TTL-based)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `msg:subs:{appId}` | `Set<ExternalSubscriptionData>` | Persisted HTTP callback subscriptions for recovery across restarts |

ExternalSubscriptionData contains: SubscriptionId (Guid), Topic (string), CallbackUrl (string), CreatedAt (DateTimeOffset).

---

## Events

### Published Events

This service **intentionally publishes no lifecycle events**. It is infrastructure, not a domain service. Debug/monitoring events (MessagePublished, SubscriptionCreated/Removed) were planned but never implemented. The events schema (`schemas/messaging-events.yaml`) documents this explicitly.

### Consumed Events

This plugin does not consume external events (it IS the event infrastructure).

---

## Configuration

### Connection Settings

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `UseInMemory` | `MESSAGING_USE_INMEMORY` | `false` | Use in-memory bus for testing |
| `RabbitMQHost` | `MESSAGING_RABBITMQ_HOST` | `"rabbitmq"` | RabbitMQ server hostname |
| `RabbitMQPort` | `MESSAGING_RABBITMQ_PORT` | `5672` | RabbitMQ AMQP port |
| `RabbitMQUsername` | `MESSAGING_RABBITMQ_USERNAME` | `"guest"` | Connection credentials |
| `RabbitMQPassword` | `MESSAGING_RABBITMQ_PASSWORD` | `"guest"` | Connection credentials |
| `RabbitMQVirtualHost` | `MESSAGING_RABBITMQ_VHOST` | `"/"` | Virtual host for isolation |
| `DefaultExchange` | `MESSAGING_DEFAULT_EXCHANGE` | `"bannou"` | Default publish exchange |
| `ConnectionRetryCount` | `MESSAGING_CONNECTION_RETRY_COUNT` | `5` | Max connection attempts |
| `ConnectionRetryDelayMs` | `MESSAGING_CONNECTION_RETRY_DELAY_MS` | `1000` | Delay between retries |
| `ConnectionMaxBackoffMs` | `MESSAGING_CONNECTION_MAX_BACKOFF_MS` | `60000` | Maximum backoff delay for connection retries |
| `RabbitMQNetworkRecoveryIntervalSeconds` | `MESSAGING_RABBITMQ_NETWORK_RECOVERY_INTERVAL_SECONDS` | `10` | Auto-recovery interval |

### Channel Pool Settings

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `ChannelPoolSize` | `MESSAGING_CHANNEL_POOL_SIZE` | `100` | Maximum channels in publisher pool |
| `MaxConcurrentChannelCreation` | `MESSAGING_MAX_CONCURRENT_CHANNEL_CREATION` | `50` | Backpressure: max concurrent channel creation requests |
| `MaxTotalChannels` | `MESSAGING_MAX_TOTAL_CHANNELS` | `1000` | Hard limit on total active channels per connection |

### Publisher Confirms

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `EnablePublisherConfirms` | `MESSAGING_ENABLE_CONFIRMS` | `true` | Enable broker confirmation for at-least-once delivery |

When `EnablePublisherConfirms` is true, `BasicPublishAsync` waits for broker confirmation before returning (RabbitMQ.Client 7.x pattern). The timeout is managed internally by RabbitMQ.Client 7.x and is not configurable.

### Publish Batching (High-Throughput)

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `EnablePublishBatching` | `MESSAGING_ENABLE_PUBLISH_BATCHING` | `false` | Enable batched publishing for high-throughput scenarios |
| `PublishBatchSize` | `MESSAGING_PUBLISH_BATCH_SIZE` | `100` | Max messages per batch before flush |
| `PublishBatchTimeoutMs` | `MESSAGING_PUBLISH_BATCH_TIMEOUT_MS` | `10` | Max delay before flushing partial batch |

### Subscription Settings

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `DefaultPrefetchCount` | `MESSAGING_DEFAULT_PREFETCH_COUNT` | `10` | Consumer channel QoS |
| `DefaultAutoAck` | `MESSAGING_DEFAULT_AUTO_ACK` | `false` | Fallback when SubscriptionOptions.AutoAck is null |

### Dead Letter Queue Settings

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `DeadLetterExchange` | `MESSAGING_DEAD_LETTER_EXCHANGE` | `"bannou-dlx"` | Dead-letter routing exchange |
| `DeadLetterMaxLength` | `MESSAGING_DEAD_LETTER_MAX_LENGTH` | `100000` | Max messages in DLX queue before oldest dropped |
| `DeadLetterTtlMs` | `MESSAGING_DEAD_LETTER_TTL_MS` | `604800000` | TTL for DLX messages (7 days) |
| `DeadLetterOverflowBehavior` | `MESSAGING_DEAD_LETTER_OVERFLOW_BEHAVIOR` | `"drop-head"` | Behavior when DLX exceeds max length |

### Poison Message Handling (Retry Buffer)

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `RetryMaxAttempts` | `MESSAGING_RETRY_MAX_ATTEMPTS` | `5` | Max retry attempts before discarding to dead-letter |
| `RetryDelayMs` | `MESSAGING_RETRY_DELAY_MS` | `5000` | Base delay between retries (doubles each retry) |
| `RetryMaxBackoffMs` | `MESSAGING_RETRY_MAX_BACKOFF_MS` | `60000` | Maximum backoff delay (caps exponential growth) |

These settings apply to the **MessageRetryBuffer** (publish failures). After `RetryMaxAttempts`, messages are discarded to the dead-letter exchange with `x-retry-count` header.

### Retry Buffer Settings (Transient Failures)

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `RetryBufferEnabled` | `MESSAGING_RETRY_BUFFER_ENABLED` | `true` | Enable publish failure buffering |
| `RetryBufferMaxSize` | `MESSAGING_RETRY_BUFFER_MAX_SIZE` | `500000` | Max buffered messages before crash (~5s at 100k msg/sec) |
| `RetryBufferMaxAgeSeconds` | `MESSAGING_RETRY_BUFFER_MAX_AGE_SECONDS` | `600` | Max message age before crash (10 minutes) |
| `RetryBufferIntervalSeconds` | `MESSAGING_RETRY_BUFFER_INTERVAL_SECONDS` | `5` | Retry processing interval |
| `RetryBufferBackpressureThreshold` | `MESSAGING_RETRY_BUFFER_BACKPRESSURE_THRESHOLD` | `0.8` | Start rejecting publishes at this buffer fill ratio |

### HTTP Callback Settings

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `CallbackRetryMaxAttempts` | `MESSAGING_CALLBACK_RETRY_MAX_ATTEMPTS` | `3` | HTTP callback retry limit |
| `CallbackRetryDelayMs` | `MESSAGING_CALLBACK_RETRY_DELAY_MS` | `1000` | HTTP callback retry delay |
| `SubscriptionTtlRefreshIntervalHours` | `MESSAGING_SUBSCRIPTION_TTL_REFRESH_INTERVAL_HOURS` | `6` | TTL refresh period |
| `SubscriptionRecoveryStartupDelaySeconds` | `MESSAGING_SUBSCRIPTION_RECOVERY_STARTUP_DELAY_SECONDS` | `2` | Delay before recovery on startup |
| `ExternalSubscriptionTtlSeconds` | `MESSAGING_EXTERNAL_SUBSCRIPTION_TTL_SECONDS` | `86400` | External subscription persistence TTL (24h) |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `IMessageBus` | Singleton | Event publishing (RabbitMQ or InMemory) |
| `IMessageSubscriber` | Singleton | Topic subscriptions (static and dynamic) |
| `IMessageTap` | Singleton | Event tapping/forwarding between exchanges |
| `IChannelManager` | Singleton | Interface for channel pooling (testability) |
| `RabbitMQConnectionManager` | Singleton | Connection pooling (configurable via `ChannelPoolSize`) |
| `IRetryBuffer` | Singleton | Interface for retry buffer (testability) |
| `MessageRetryBuffer` | Singleton | Transient publish failure recovery with crash-fast |
| `NativeEventConsumerBackend` | HostedService | Bridges IEventConsumer fan-out to RabbitMQ |
| `MessagingSubscriptionRecoveryService` | HostedService | Recovers external subscriptions on startup, refreshes TTL |
| `MessagingService` | Singleton | HTTP API implementation (also registered as concrete for recovery service) |

Service lifetime is **Singleton** (infrastructure must persist across requests).

### Helper Classes

| Class | Role |
|-------|------|
| `GenericMessageEnvelope` | Wraps arbitrary JSON payloads for MassTransit compatibility; implements `IBannouEvent` |
| `TappedMessageEnvelope` | Extended envelope with tap routing metadata for multi-stream forwarding |
| `ExternalSubscriptionData` | Record type for persisting HTTP callback subscriptions |
| `IProcessTerminator` | Interface for crash-fast behavior; `EnvironmentProcessTerminator` calls `Environment.Exit(1)` |

---

## API Endpoints (Implementation Notes)

### Publish (`/messaging/publish`)

Wraps arbitrary JSON payloads in `GenericMessageEnvelope` (MassTransit requires concrete types). Normalizes `Guid.Empty` to null for CorrelationId. Delegates to `IMessageBus.TryPublishAsync()`. Supports exchange types: fanout, direct, topic (default). Supports persistent flag, priority (0-9), expiration (ISO 8601 duration), and custom headers.

### Subscribe (`/messaging/subscribe`)

Creates dynamic HTTP callback subscriptions. Generates unique queue name `bannou-dynamic-{subscriptionId:N}`. Stores subscription in both in-memory dictionary (for disposal) and state store (for recovery). Retry logic: retries on `HttpRequestException` and `TaskCanceledException` (timeout) only; HTTP 4xx/5xx treated as successful delivery. Subscription options: durable, exclusive, autoAck, prefetchCount, useDeadLetter, consumerGroup.

### Unsubscribe (`/messaging/unsubscribe`)

Removes subscription from in-memory tracking and persistent state store. Disposes the subscription handle and HttpClient. Returns NotFound if subscription not active.

### List Topics (`/messaging/list-topics`)

Returns topics from active HTTP callback subscriptions on the current node. Returns topic name and consumer count per topic. Filters by exchangeFilter prefix.

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
    ┌─────────┴─────────┐         ┌──────────────────────┐
    │ RabbitMQConnection │         │  RabbitMQMessageTap   │
    │ Manager            │         │  (IMessageTap)        │
    │                    │         │                       │
    │ Single connection  │         │ Creates taps that     │
    │ Channel pool (100) │         │ forward events from   │
    │ Max 1000 channels  │         │ source to destination │
    │ Auto-recovery      │         │                       │
    └────────────────────┘         └───────────────────────┘
```

---

## Stubs & Unimplemented Features

None. All previously listed stubs have been resolved:
- **Lifecycle events**: Correctly rejected — messaging IS the event infrastructure; self-referential events would be circular and noisy. Observability is handled by T30 telemetry spans on all async methods.
- **ListTopics MessageCount**: Removed from schema — "topic message count" is not a coherent RabbitMQ concept (topics are routing keys, not queues). The field always returned 0, which is actively misleading. Queue depth monitoring belongs in RabbitMQ Management API or Prometheus exporters.

---

## Potential Extensions

1. **Prometheus metrics**: Publish/subscribe rates, buffer depth, retry counts, channel pool utilization. Would come from a RabbitMQ sidecar exporter, not from lib-messaging code.
2. **Dead-letter processing consumer**: Background service to process DLX queue and handle poison messages (alerting, logging, optional reprocessing for transient failures).

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

No bugs identified.

### Intentional Quirks (Documented Behavior)

1. **Aggressive retry with crash-fast buffer philosophy**: When `TryPublishAsync` fails to deliver to RabbitMQ (connection down, channel error, etc.), the message is **not lost** - it's buffered in `MessageRetryBuffer` and `TryPublishAsync` returns `true` (because delivery WILL be retried). The buffer is processed every `RetryBufferIntervalSeconds` (default 5s). If RabbitMQ stays down too long, the node **intentionally crashes** via `IProcessTerminator.TerminateProcess()`:
   - Buffer exceeds `RetryBufferMaxSize` (default 500,000 messages)
   - Oldest message exceeds `RetryBufferMaxAgeSeconds` (default 600s / 10 minutes)

   **Why crash?** Crashing makes the failure visible in monitoring, triggers orchestrator restart, and prevents silent data loss or unbounded memory growth. True event loss only occurs if the node dies (power failure, OOM kill) before the buffer flushes.

2. **Backpressure on buffer fill**: When the retry buffer reaches `RetryBufferBackpressureThreshold` (default 80%), new publishes are rejected (`TryPublishAsync` returns `false`). This prevents memory exhaustion and gives the buffer time to drain. Callers should handle `false` returns appropriately.

3. **HTTP callbacks don't retry on HTTP errors**: Only retries on network failures (`HttpRequestException`, `TaskCanceledException` for timeout). HTTP 4xx/5xx is treated as successful delivery. Philosophy: callback endpoint is responsible for its own error handling.

4. **Channel pool for publishers only**: `RabbitMQConnectionManager` maintains a channel pool (default 100, max 1000 total via `MaxTotalChannels`) for publish operations. Consumer channels are separate (not pooled) - each subscription gets a dedicated channel.

5. **Two queue naming schemes**: HTTP API subscriptions use `bannou-dynamic-{id:N}` while internal dynamic subscriptions use `{topic}.dynamic.{id:N}`. Different schemes for different subscription paths.

6. **Static subscriptions prevent duplicates**: `RabbitMQMessageSubscriber.SubscribeAsync()` logs a warning and returns early if already subscribed to the same topic. This prevents duplicate handlers but can mask subscription misuse.

7. **Subscriber requeue behavior**: When a handler throws `HandlerError`, messages are requeued once (if not already redelivered via RabbitMQ's `Redelivered` flag). This is a single retry only - no exponential backoff or retry counting. For multi-attempt retries with backoff, that logic is in the **MessageRetryBuffer** for publish failures (tracked via `x-retry-count` header).

8. **DLX queue size limits**: Dead letter queue has configurable max length (default 100k messages) and TTL (default 7 days). When limits are exceeded, oldest messages are dropped (`drop-head` policy).

### Design Considerations

1. **In-memory mode limitations**: `InMemoryMessageBus` delivers asynchronously via a discarded task (`_ = DeliverToSubscribersAsync(...)`, fire-and-forget). Subscriptions use `ImmutableList<Func<object, ...>>` for lock-free concurrent access but are not fully representative of RabbitMQ semantics (no queue persistence, no dead-letter, no prefetch). `InMemoryMessageTap` works in-process only and simulates exchanges by combining exchange+routing key as destination topic.

2. **Tap creates exchange if not exists**: `RabbitMQMessageTap.CreateTapAsync()` has a `CreateExchangeIfNotExists` flag on `TapDestination` that creates the destination exchange if it doesn't exist. This is intentional (exchanges are auto-created as needed) but could mask typos in exchange names.

3. **Publisher confirms add latency**: When `EnablePublisherConfirms` is true (default), each `BasicPublishAsync` waits for broker confirmation. This adds ~1-5ms latency per publish. Fully configurable via `MESSAGING_ENABLE_PUBLISHER_CONFIRMS` and mitigated by `MESSAGING_ENABLE_PUBLISH_BATCHING`.

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above.

### Completed

- **GitHub Issue #328**: Production readiness for 100k NPC agents
  - Fixed channel pool exhaustion with configurable limits (`MaxTotalChannels`, `MaxConcurrentChannelCreation`)
  - Fixed poison message infinite loop with retry counting and dead-lettering
  - Fixed retry buffer crash thresholds (500k messages, 10 minute age, 80% backpressure)
  - Fixed silent deserialization failures with error event publishing
  - Fixed T23 async void timer callback violation
  - Fixed T24 blocking on async (ReturnChannelAsync)
  - Fixed T26 sentinel values (nullable TapId)
  - Added message batching support for high-throughput scenarios
  - Fixed connection recovery race condition (TOCTOU)
  - Improved handler exception logging with telemetry
  - Added DLX queue size/TTL limits
  - Fixed temp ServiceProvider disposal
  - Fixed publisher confirms configuration (tracking enabled)
  - **Audit fixes (2026-02-07)**:
    - Fixed T7 violation: Added error event publishing (`TryPublishErrorAsync`) when poison messages are discarded
    - Fixed T21 violation: Removed dead config `PublisherConfirmTimeoutSeconds` (RabbitMQ.Client 7.x manages timeout internally)
    - Added unit tests for poison message discard scenario

- **L3 Hardening Audit (2026-02-21)**:
    - Fixed T10 violations: Removed `[Messaging]` bracket-prefix from all log messages across RabbitMQMessageBus, RabbitMQMessageSubscriber, RabbitMQConnectionManager, MessageRetryBuffer, RabbitMQMessageTap, NativeEventConsumerBackend
    - Fixed T25 violations: Changed `TappedMessageEnvelope.DestinationExchangeType` from `string` to `TapExchangeType` enum; updated all construction sites and tests
    - Fixed T9 violation: Replaced `List<Func<...>>` + `lock (_subscriptionLock)` in InMemoryMessageBus with lock-free `ImmutableList<Func<...>>` via `ConcurrentDictionary.AddOrUpdate` pattern
    - Fixed T30 violations: Added telemetry spans to 15+ async methods across RabbitMQMessageBus (TryPublishRawAsync, TryPublishErrorAsync, PublishBatchAsync), RabbitMQMessageSubscriber (SubscribeAsync, SubscribeDynamicAsync, SubscribeDynamicRawAsync, UnsubscribeAsync, RemoveDynamicSubscriptionAsync), NativeEventConsumerBackend (StartAsync), RabbitMQMessageTap (CreateTapAsync, ForwardRawMessageAsync, RemoveTapAsync), RabbitMQConnectionManager (InitializeAsync, GetChannelAsync, CreateConsumerChannelAsync), MessageRetryBuffer (ProcessBufferedMessagesInternalAsync)
    - Injected `ITelemetryProvider` into RabbitMQMessageTap, RabbitMQConnectionManager, MessageRetryBuffer (previously lacked telemetry)
    - Fixed CA2000 dispose warnings in RabbitMQMessageTap.RemoveTapAsync and RabbitMQMessageSubscriber.RemoveDynamicSubscriptionAsync with dedicated local variable pattern
    - Schema fixes: Added NRT compliance (`nullable: true`), validation constraints (`minLength`, `maxLength`, `minimum`, `maximum`, `pattern`), consolidated `ExchangeType` and `OverflowBehavior` enums from configuration to API schema, fixed env var naming (`MESSAGING_RABBITMQ_VHOST` → `MESSAGING_RABBITMQ_VIRTUAL_HOST`), added `description` to all schema properties
    - Removed `messageCount` field from TopicInfo schema (always returned 0, actively misleading) and `includeEmpty` filter (referenced removed field)
    - Removed lifecycle events from stubs list (correctly rejected — self-referential events for infrastructure are circular and noisy)
    - All 177 unit tests passing, 0 warnings, 0 errors

- **Design Consideration Resolution (2026-02-22)**:
    - Resolved "ServiceId from global static": Replaced `Program.ServiceGUID` with `IMeshInstanceIdentifier` (new interface in bannou-service, registered by lib-mesh). `RabbitMQMessageBus.TryPublishErrorAsync()` now uses injected `_instanceId` instead of static global access. All generated clients and `IServiceNavigator` also expose `InstanceId` for consistent node identification across all services.
    - Resolved "No graceful drain on shutdown": Added `ShutdownTimeoutSeconds` config (`MESSAGING_SHUTDOWN_TIMEOUT_SECONDS`, default 10s). `RabbitMQMessageSubscriber.DisposeAsync` now wraps subscription cleanup in `WaitAsync(timeout)` to prevent indefinite blocking when channels hang.
    - All 177 unit tests passing, 0 warnings, 0 errors

### Active

No active work items.
