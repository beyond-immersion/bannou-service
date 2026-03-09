# Messaging Plugin Deep Dive

> **Plugin**: lib-messaging
> **Schema**: schemas/messaging-api.yaml
> **Version**: 1.0.0
> **Layer**: Infrastructure
> **State Store**: messaging-external-subs (Redis, app-id keyed with TTL)
> **Implementation Map**: [docs/maps/MESSAGING.md](../maps/MESSAGING.md)
> **Short**: RabbitMQ pub/sub infrastructure (IMessageBus/IMessageSubscriber) with in-memory testing mode

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

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| Every service | Uses `IMessageBus` for event publishing and `IMessageSubscriber` for static subscriptions |
| lib-connect | Uses `IClientEventPublisher` for client event routing; uses `IMessageTap` for session-scoped event forwarding |
| lib-actor | Uses `IMessageBus` for actor lifecycle, heartbeats, pool management |
| lib-documentation | Uses `IMessageBus` for git sync and search indexing |
| lib-permission | Uses `IMessageBus` for permission registration broadcasts |

All services depend on messaging infrastructure. The HTTP API (`IMessagingClient`) is rarely used directly.

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
| `EnablePublisherConfirms` | `MESSAGING_ENABLE_PUBLISHER_CONFIRMS` | `true` | Enable broker confirmation for at-least-once delivery |

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
| `DeadLetterOverflowBehavior` | `MESSAGING_DEAD_LETTER_OVERFLOW_BEHAVIOR` | `DropHead` | Behavior when DLX exceeds max length |
| `DeadLetterConsumerEnabled` | `MESSAGING_DEAD_LETTER_CONSUMER_ENABLED` | `true` | Enable dead letter consumer background service |
| `DeadLetterConsumerStartupDelaySeconds` | `MESSAGING_DEAD_LETTER_CONSUMER_STARTUP_DELAY_SECONDS` | `5` | Delay before consumer starts subscribing |

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

### Shutdown Settings

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `ShutdownTimeoutSeconds` | `MESSAGING_SHUTDOWN_TIMEOUT_SECONDS` | `10` | Max seconds to wait for graceful subscription cleanup during shutdown |

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
              ▲                           │                   └───────┬────────────┘
              │                           ▼                           │ (on exhaustion)
              │               ┌───────────────────────┐               ▼
              │               │NativeEventConsumerBackend│    ┌──────────────────────┐
              │               │ (IHostedService)        │    │DeadLetterConsumer    │
              │               │                         │    │Service (IHostedService)│
              │               │ Bridges RabbitMQ to     │    │                      │
              │               │ IEventConsumer fan-out  │    │ Reads DLX queue      │
              │               │ (per-plugin handlers)   │    │ Logs + error events  │
              │               └─────────────────────────┘    │ Acks after processing│
              │                                              └──────────────────────┘
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

1. **Observable gauge metrics for buffer depth, retry counts, and channel pool utilization**: Publish/subscribe rate counters and duration histograms are already implemented via `ITelemetryProvider.RecordCounter/RecordHistogram` in `RabbitMQMessageBus` and `RabbitMQMessageSubscriber`. Three application-level gauge metrics remain unimplemented: retry buffer depth (from `IRetryBuffer.BufferCount`), per-attempt retry counts (from `MessageRetryBuffer` retry loop), and channel pool utilization (from `IChannelManager.TotalActiveChannels`/`PooledChannelCount`). These are in-process values that no sidecar exporter can observe — they require `ObservableGauge` support in `ITelemetryProvider`, which currently only exposes `RecordCounter` and `RecordHistogram`.
<!-- AUDIT:NEEDS_DESIGN:2026-02-22:https://github.com/beyond-immersion/bannou-service/issues/453 -->

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

9. **Dead letter consumer uses IChannelManager directly**: `DeadLetterConsumerService` bypasses `IMessageSubscriber` and creates its own consumer channel via `IChannelManager.CreateConsumerChannelAsync()`. This is necessary because `SubscribeDynamicRawAsync` only passes raw bytes to the handler — dead letter processing requires access to `BasicProperties.Headers` for metadata extraction (`x-original-topic`, `x-retry-count`, `x-death`, etc.). Uses a durable shared queue (`bannou-dlx-consumer`) so accumulated dead letters are consumed even after restarts, with RabbitMQ competing consumers for multi-instance safety.

10. **In-memory mode limitations**: `InMemoryMessageBus` delivers asynchronously via a discarded task (`_ = DeliverToSubscribersAsync(...)`, fire-and-forget). Subscriptions use `ImmutableList<Func<object, ...>>` for lock-free concurrent access but are not fully representative of RabbitMQ semantics (no queue persistence, no dead-letter, no prefetch). `InMemoryMessageTap` works in-process only and simulates exchanges by combining exchange+routing key as destination topic.

11. **Tap auto-creates destination exchanges**: `RabbitMQMessageTap.CreateTapAsync()` has a `CreateExchangeIfNotExists` flag on `TapDestination` (default `true`) that creates the destination exchange if it doesn't exist. This follows the "exchanges as implicit infrastructure" pattern (similar to RabbitMQ's own queue auto-creation) but could mask typos in exchange names — a mistyped exchange will be silently created rather than erroring.

12. **Publisher confirms add latency**: When `EnablePublisherConfirms` is true (default), each `BasicPublishAsync` waits for broker confirmation before returning. This adds ~1-5ms latency per publish but provides at-least-once delivery guarantees. Fully configurable via `MESSAGING_ENABLE_PUBLISHER_CONFIRMS` and mitigated by `MESSAGING_ENABLE_PUBLISH_BATCHING` for high-throughput scenarios.

### Design Considerations (Requires Planning)

No active design considerations. All previous items were resolved to Intentional Quirks.

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above.

### Completed

All historical completed items have been archived to git history.

### Active

No active work items.
