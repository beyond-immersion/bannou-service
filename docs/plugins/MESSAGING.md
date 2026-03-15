# Messaging Plugin Deep Dive

> **Plugin**: lib-messaging
> **Schema**: schemas/messaging-api.yaml
> **Version**: 1.0.0
> **Layer**: Infrastructure
> **State Store**: messaging-external-subs (Redis, app-id keyed with TTL)
> **Implementation Map**: [docs/maps/MESSAGING.md](../maps/MESSAGING.md)
> **Short**: RabbitMQ pub/sub infrastructure (IMessageBus/IMessageSubscriber) with in-memory testing mode and direct dispatch for embedded/sidecar

---

## Overview

The Messaging service (L0 Infrastructure) is the native RabbitMQ pub/sub infrastructure for Bannou. Operates in a dual role: as the `IMessageBus`/`IMessageSubscriber` infrastructure library used by all services for event publishing and subscription, and as an HTTP API providing dynamic subscription management with HTTP callback delivery. Three messaging backends: RabbitMQ (cloud, with channel pooling and aggressive retry buffering), InMemoryMessageBus (testing), and DirectDispatchMessageBus (embedded/sidecar, zero-overhead dispatch directly to IEventConsumer).

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

### Backend Selection

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `UseDirectDispatch` | `MESSAGING_USE_DIRECT_DISPATCH` | `false` | Zero-overhead dispatch directly to IEventConsumer (embedded/sidecar). Takes precedence over `UseInMemory`. |
| `UseInMemory` | `MESSAGING_USE_INMEMORY` | `false` | In-memory bus for testing (no RabbitMQ) |
| `SkipUnhandledTopics` | `MESSAGING_SKIP_UNHANDLED_TOPICS` | `false` | Skip scope creation for topics with no handlers (DirectDispatch optimization) |

### Connection Settings

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
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
Messaging Architecture — Three Backends
==========================================

 Backend Selection (MessagingServicePlugin.ConfigureServices):
   UseDirectDispatch=true → DirectDispatchMessageBus (embedded/sidecar)
   UseInMemory=true       → InMemoryMessageBus (testing)
   (default)              → RabbitMQMessageBus + RabbitMQMessageSubscriber

 ┌─────────────────────────────────────────────────────────┐
 │                    IEventConsumer (Singleton)            │
 │  ConcurrentDictionary<topic, handlers>                  │
 │  DispatchAsync() — per-plugin fan-out with isolation    │
 └──────────────────────┬──────────────────────────────────┘
                        │
       ┌────────────────┼───────────────────┐
       │                │                    │
 ┌─────▼──────────┐ ┌──▼──────────┐ ┌──────▼──────────────┐
 │ RabbitMQ Mode  │ │ InMemory    │ │ DirectDispatch Mode │
 │                │ │ Mode        │ │                     │
 │ Broker → queue │ │ ImmutableList│ │ TryPublishAsync →   │
 │ NativeBackend  │ │ NativeBackend│ │  IEventConsumer     │
 │  bridges to    │ │  bridges to │ │  .DispatchAsync()   │
 │  IEventConsumer│ │  IEventConsumer│ │  (direct, no bridge)│
 │                │ │             │ │                     │
 │ + RetryBuffer  │ │             │ │ + _directSubscribers│
 │ + DeadLetter   │ │             │ │   for Connect, etc. │
 │ + ChannelPool  │ │             │ │                     │
 │ + Batching     │ │             │ │ No NativeBackend    │
 └────────────────┘ └─────────────┘ │ No EventSubRegistry │
                                    │ No serialization    │
                                    └─────────────────────┘
```

---

## Stubs & Unimplemented Features

None. All previously listed stubs have been resolved:
- **Lifecycle events**: Correctly rejected — messaging IS the event infrastructure; self-referential events would be circular and noisy. Observability is handled by telemetry spans on all async methods.
- **ListTopics MessageCount**: Removed from schema — "topic message count" is not a coherent RabbitMQ concept (topics are routing keys, not queues). The field always returned 0, which is actively misleading. Queue depth monitoring belongs in RabbitMQ Management API or Prometheus exporters.

---

## Potential Extensions

None currently identified. All previous items have been resolved.

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

5. **Dynamic queue naming**: All dynamic subscriptions (both HTTP API and internal) use `{topic}.dynamic.{id:N}` queue names, assigned by `RabbitMQMessageSubscriber.SubscribeDynamicAsync`. The queue name is an internal implementation detail not exposed via the API.

6. **Static subscriptions prevent duplicates**: `RabbitMQMessageSubscriber.SubscribeAsync()` logs a warning and returns early if already subscribed to the same topic. This prevents duplicate handlers but can mask subscription misuse.

7. **Subscriber requeue behavior**: When a handler throws `HandlerError`, messages are requeued once (if not already redelivered via RabbitMQ's `Redelivered` flag). This is a single retry only - no exponential backoff or retry counting. For multi-attempt retries with backoff, that logic is in the **MessageRetryBuffer** for publish failures (tracked via `x-retry-count` header).

8. **DLX queue size limits**: Dead letter queue has configurable max length (default 100k messages) and TTL (default 7 days). When limits are exceeded, oldest messages are dropped (`drop-head` policy).

9. **Dead letter consumer uses IChannelManager directly**: `DeadLetterConsumerService` bypasses `IMessageSubscriber` and creates its own consumer channel via `IChannelManager.CreateConsumerChannelAsync()`. This is necessary because `SubscribeDynamicRawAsync` only passes raw bytes to the handler — dead letter processing requires access to `BasicProperties.Headers` for metadata extraction (`x-original-topic`, `x-retry-count`, `x-death`, etc.). Uses a durable shared queue (`bannou-dlx-consumer`) so accumulated dead letters are consumed even after restarts, with RabbitMQ competing consumers for multi-instance safety.

10. **In-memory mode limitations**: `InMemoryMessageBus` delivers asynchronously via a discarded task (`_ = DeliverToSubscribersAsync(...)`, fire-and-forget). Subscriptions use `ImmutableList<Func<object, ...>>` for lock-free concurrent access but are not fully representative of RabbitMQ semantics (no queue persistence, no dead-letter, no prefetch). `InMemoryMessageTap` works in-process only and simulates exchanges by combining exchange+routing key as destination topic.

11. **DirectDispatch mode**: `DirectDispatchMessageBus` eliminates the NativeEventConsumerBackend bridge and dispatches `TryPublishAsync` directly to `IEventConsumer.DispatchAsync`. This exists because `InMemoryMessageBus` carries structural overhead from faithfully replicating RabbitMQ's subscription model: NativeEventConsumerBackend creates subscriptions at startup for every IEventConsumer topic, events route through two registries (InMemory's `_subscriptions` ImmutableList and IEventConsumer's `ConcurrentDictionary`), two dispatch loops, and a fire-and-forget coroutine — all within the same process delivering to handlers already known at startup. DirectDispatch collapses this to one registry, one dispatch loop, zero intermediate layers. Objects are passed by reference — zero serialization. Fire-and-forget semantics match InMemoryMessageBus (scope created inside the dispatched task). Key design decisions:
    - **Always dispatch, never skip** (default): `TryPublishAsync` always creates a scope and calls `IEventConsumer.DispatchAsync` even with no handlers registered (future subscribers, L5 extensions). `SkipUnhandledTopics` configuration (default `false`) enables scope-skip for resource-constrained embedded devices.
    - **Fire-and-forget matches InMemory**: Producer's `await TryPublishAsync()` returns immediately. Scope lifetime managed inside the dispatched task.
    - **NativeEventConsumerBackend not registered**: No bridge needed — eliminated entirely from the DI container.
    - **SubscribeDynamicRawAsync serializes**: Connect binary protocol forwarding needs `byte[]`. DirectDispatch serializes to JSON bytes via BannouJson (same as InMemory). This is the one path where serialization occurs.
    - **DI listener orthogonality**: DI listeners (e.g., `ISeedEvolutionListener`, `ICurrencyTransactionListener`) fire inline from producing services, completely independent of the messaging backend. In single-node mode, both DI listeners and DirectDispatch fire on the same node. Handlers must be idempotent regardless of backend (same as with InMemory or RabbitMQ). DI listeners are the more efficient notification path for single-node deployments; DirectDispatch handles the remaining event subscriptions that can't be converted to DI listeners (cross-layer notifications, analytics, third-party consumers).
    - **Two-path delivery**: Path 1 dispatches to IEventConsumer (the main path). Path 2 dispatches to `_directSubscriptions` (Connect per-session, static subscriptions via `SubscribeAsync`/`SubscribeDynamicAsync`). The two paths use separate registries — `UnsubscribeAsync` on Path 2 cannot accidentally destroy Path 1's IEventConsumer delivery (unlike InMemoryMessageBus where both share `_subscriptions`).

    Takes precedence over `UseInMemory` in backend selection. `IMessageTap` resolves to `InMemoryMessageTap` (shared with InMemory mode — uses `IMessageBus`/`IMessageSubscriber` interfaces, works with any in-process backend).

11. **Tap auto-creates destination exchanges**: `RabbitMQMessageTap.CreateTapAsync()` has a `CreateExchangeIfNotExists` flag on `TapDestination` (default `true`) that creates the destination exchange if it doesn't exist. This follows the "exchanges as implicit infrastructure" pattern (similar to RabbitMQ's own queue auto-creation) but could mask typos in exchange names — a mistyped exchange will be silently created rather than erroring.

12. **Publisher confirms add latency**: When `EnablePublisherConfirms` is true (default), each `BasicPublishAsync` waits for broker confirmation before returning. This adds ~1-5ms latency per publish but provides at-least-once delivery guarantees. Fully configurable via `MESSAGING_ENABLE_PUBLISHER_CONFIRMS` and mitigated by `MESSAGING_ENABLE_PUBLISH_BATCHING` for high-throughput scenarios.

13. **Observable gauge metrics for in-process infrastructure state**: `RabbitMQConnectionManager` registers two `ObservableGauge<int>` metrics at construction (channel pool active count, pooled channel count). `MessageRetryBuffer` registers two gauges (buffer depth count, buffer fill ratio). Additionally, `ProcessBufferedMessagesInternalAsync` records per-status retry attempt counters (processed/failed/discarded/deferred) via `RecordCounter` with a `status` tag. All metrics are gated on `ITelemetryProvider.MetricsEnabled` — when telemetry is disabled, registration is a no-op. These are in-process values that no RabbitMQ sidecar exporter can observe.

14. **External subscription node affinity**: HTTP callback subscriptions are inherently node-local — the RabbitMQ consumer channel exists only on the node that created the subscription. `RemoveSubscription` checks the in-memory `_activeSubscriptions` dictionary first and returns 404 if the subscription handle isn't on the current node, even if the subscription exists in Redis. In multi-node deployments with shared `appId`, the `MessagingSubscriptionRecoveryService` recovers persisted subscriptions on whichever node starts (or restarts), creating competing consumers on the same deterministic queue name (`{topic}.dynamic.{id:N}`). This means: (a) unsubscribe must route to a node that holds the active handle, and (b) after node failover, the recovered subscription lives on the recovering node, not the original.

### Design Considerations (Requires Planning)

1. **Cross-node event propagation for distributed sidecar**: DirectDispatch handles local-only delivery within each sidecar node. For the distributed sidecar topology (peer machines contributing simulation), cross-node event propagation uses the star/host-authority model via Connect WebSocket — the host fans out events to peers through the existing binary protocol. This is a Connect + mesh concern, not a messaging backend concern. See [DEPLOYMENT-MODES.md § Distributed Sidecar Topology](../planning/DEPLOYMENT-MODES.md#distributed-sidecar-topology-peer-delegation-via-connect).

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above.

### Completed

- [#453](https://github.com/beyond-immersion/bannou-service/issues/453): Observable gauge metrics for buffer depth, retry counts, channel pool — implemented via `RegisterObservableGauge<T>` on `ITelemetryProvider`

### Active

No active work items.
