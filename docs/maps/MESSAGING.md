# Messaging Implementation Map

> **Plugin**: lib-messaging
> **Schema**: schemas/messaging-api.yaml
> **Layer**: Infrastructure (L0)
> **Deep Dive**: [docs/plugins/MESSAGING.md](../plugins/MESSAGING.md)

---

## Summary Table

| Field | Value |
|-------|-------|
| Plugin | lib-messaging |
| Layer | L0 Infrastructure |
| Endpoints | 4 |
| State Stores | messaging-external-subs (Redis set) |
| Events Published | 0 (intentionally none — this IS the event infrastructure) |
| Events Consumed | 0 |
| Client Events | 0 |
| Background Services | 4 (NativeEventConsumerBackend, MessagingSubscriptionRecoveryService, DeadLetterConsumerService, MessageRetryBuffer) |

---

## State

**Store**: `messaging-external-subs` (Backend: Redis, via `ICacheableStateStore<ExternalSubscriptionData>`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `external-subs:{appId}` | `Set<ExternalSubscriptionData>` | All HTTP callback subscriptions for this app-id instance, stored as a Redis set with configurable TTL |

All external subscriptions for a given app-id live in a single Redis set. Set operations used: `AddToSetAsync`, `GetSetAsync`, `RemoveFromSetAsync`, `RefreshSetTtlAsync`.

`ExternalSubscriptionData` contains: `SubscriptionId` (Guid), `Topic` (string), `CallbackUrl` (string), `CreatedAt` (DateTimeOffset).

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | Acquires `ICacheableStateStore<ExternalSubscriptionData>` for subscription persistence |
| lib-telemetry (`ITelemetryProvider`) | L0 | Soft | Span instrumentation in internal services; `NullTelemetryProvider` when disabled |
| `AppConfiguration` | Core | Hard | `EffectiveAppId` for subscription store key isolation |
| `IHttpClientFactory` | Core | Hard | Named `"MessagingCallbacks"` clients for HTTP callback delivery |
| `IMessageBus` (self) | L0 | Hard | Delegates publish operations; error event publication via `TryPublishErrorAsync` |
| `IMessageSubscriber` (self) | L0 | Hard | Creates dynamic RabbitMQ subscriptions for HTTP callback delivery |
| `IMeshInstanceIdentifier` | L0 | Hard | Node-stable instance identity for error event `ServiceId` field (injected into `RabbitMQMessageBus`) |

**Notes:**
- Messaging is an L0 leaf node with no upward service-layer dependencies.
- `IMessageBus` and `IMessageSubscriber` are self-referential: the service uses infrastructure it itself provides.
- `IMeshInstanceIdentifier` is registered by lib-mesh (L0). The dependency is safe because it is lazily resolved at first `IMessageBus` singleton use, not at `ConfigureServices` time.
- No lib-resource integration needed (stores no data keyed by other services' entities).
- Service lifetime is **Singleton** (maintains in-memory subscription handles that cannot be serialized).

---

## Events Published

This plugin intentionally publishes no lifecycle or domain events. It IS the event infrastructure — self-referential events would be circular.

---

## Events Consumed

This plugin does not consume external events.

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<MessagingService>` | Structured logging |
| `MessagingServiceConfiguration` | Typed configuration (40 properties) |
| `AppConfiguration` | `EffectiveAppId` for store keying |
| `IMessageBus` | Publish delegation and error events |
| `IMessageSubscriber` | Dynamic subscription creation |
| `IHttpClientFactory` | HTTP callback clients |
| `IStateStoreFactory` | Subscription persistence store |

### Registered Infrastructure (provided to all plugins)

| Interface | Implementation (RabbitMQ) | Implementation (InMemory) |
|-----------|---------------------------|---------------------------|
| `IMessageBus` | `RabbitMQMessageBus` | `InMemoryMessageBus` |
| `IMessageSubscriber` | `RabbitMQMessageSubscriber` | `InMemoryMessageBus` |
| `IMessageTap` | `RabbitMQMessageTap` | `InMemoryMessageTap` |
| `IChannelManager` | `RabbitMQConnectionManager` | — |
| `IRetryBuffer` | `MessageRetryBuffer` | — |
| `IUnhandledExceptionHandler` | `MessagingUnhandledExceptionHandler` | `MessagingUnhandledExceptionHandler` |

---

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| PublishEvent | POST /messaging/publish | generated | [] | - | caller's topic (delegated) |
| CreateSubscription | POST /messaging/subscribe | generated | [] | subscriptions (set add) | - |
| RemoveSubscription | POST /messaging/unsubscribe | generated | [] | subscriptions (set remove) | - |
| ListTopics | POST /messaging/list-topics | generated | [] | - | - |

---

## Methods

### PublishEvent
POST /messaging/publish | Roles: []

```
IF body.Options?.CorrelationId == Guid.Empty
  // Normalize sentinel to null per IMPLEMENTATION TENETS
  body.Options.CorrelationId = null
messageId = new Guid
envelope = GenericMessageEnvelope(body.Topic, body.Payload)
  // Wraps arbitrary JSON in IBannouEvent envelope; PayloadJson = BannouJson.Serialize(payload)
PUBLISH body.Topic { envelope, options: body.Options, messageId }
  // Supports exchange types: Fanout, Direct, Topic (default)
  // Options: persistent, priority (0-9), expiration (ISO 8601), custom headers
  // Returns true even when buffered for retry (delivery WILL happen)
RETURN (200, PublishEventResponse { MessageId })
```

---

### CreateSubscription
POST /messaging/subscribe | Roles: []

```
subscriptionId = new Guid
httpClient = HttpClientFactory.CreateClient("MessagingCallbacks")
// Build callback handler: HTTP POST to callbackUrl with body = envelope.PayloadJson
// Retry on transport failures only (HttpRequestException, TaskCanceledException)
// HTTP 4xx/5xx treated as delivered (no retry)
// Max retries: config.CallbackRetryMaxAttempts, delay: config.CallbackRetryDelayMs
callbackHandler = CreateCallbackHandler(body.CallbackUrl, httpClient, config)
// Create dynamic RabbitMQ consumer delivering to callback handler
// Subscription options: durable, exclusive, autoAck, prefetchCount, useDeadLetter, consumerGroup
_messageSubscriber.SubscribeDynamicAsync(body.Topic, callbackHandler)
// Store in-memory for disposal tracking
_activeSubscriptions[subscriptionId] = SubscriptionEntry { Topic, Handle, HttpClient }
WRITE _subscriptionStore:external-subs:{appId} (set add) <- ExternalSubscriptionData { SubscriptionId, Topic, CallbackUrl, CreatedAt }
  // TTL = config.ExternalSubscriptionTtlSeconds (default 24h)
RETURN (200, CreateSubscriptionResponse { SubscriptionId })
```

---

### RemoveSubscription
POST /messaging/unsubscribe | Roles: []

```
// Check in-memory tracking (authoritative for active subscriptions on this node)
IF subscriptionId not in _activeSubscriptions           -> 404
// Dispose RabbitMQ subscription handle and HttpClient
entry.DisposeAsync()
READ _subscriptionStore:external-subs:{appId} (full set)
  // Retrieves all ExternalSubscriptionData entries for this app-id
IF matching entry found by SubscriptionId
  DELETE _subscriptionStore:external-subs:{appId} (set remove matching entry)
RETURN 200
  // Bare status code, no response body
```

---

### ListTopics
POST /messaging/list-topics | Roles: []

```
// Reads from in-memory _activeSubscriptions only (HTTP API subscriptions on this node)
// Does NOT report RabbitMQ broker-level topics or internal plugin subscriptions
topics = _activeSubscriptions.Values
IF body?.ExchangeFilter != null
  topics = topics WHERE Topic starts with ExchangeFilter (case-insensitive)
FOREACH group in topics GROUP BY Topic
  TopicInfo { Name = group.Key, ConsumerCount = group.Count }
RETURN (200, ListTopicsResponse { Topics })
```

---

## Background Services

### NativeEventConsumerBackend
**Trigger**: Startup only (no recurring loop)
**Purpose**: Bridges `IEventConsumer` fan-out to RabbitMQ subscriptions

```
// On StartAsync: create one RabbitMQ consumer per registered event topic
FOREACH (topic, eventType, handlers) in EventSubscriptionRegistry
  // Uses reflection (MakeGenericMethod) to call SubscribeTypedAsync<TEvent>
  // with correct compile-time type for deserialization
  _messageSubscriber.SubscribeDynamicAsync(topic, handler)
  // Handler: deserializes message → IEventConsumer.DispatchAsync for per-plugin fan-out
  // On handler error: NACK with requeue (once; no requeue on redeliver)
  // On deserialization failure: NACK without requeue
```

---

### MessagingSubscriptionRecoveryService
**Startup delay**: `config.SubscriptionRecoveryStartupDelaySeconds` (default 2s)
**Recurring interval**: `config.SubscriptionTtlRefreshIntervalHours` (default 6h)
**Purpose**: Recovers HTTP callback subscriptions from Redis on startup; refreshes TTL periodically

```
// Phase 1: Recovery (on startup after delay)
READ _subscriptionStore:external-subs:{appId} (full set)
FOREACH subscription in persisted set
  IF subscription.SubscriptionId already in _activeSubscriptions
    // skip (already active from this boot)
  ELSE
    httpClient = HttpClientFactory.CreateClient("MessagingCallbacks")
    callbackHandler = CreateCallbackHandler(sub.CallbackUrl, httpClient, config)
      // publishErrorEvent: false (avoids error storms on restart)
    _messageSubscriber.SubscribeDynamicAsync(sub.Topic, callbackHandler)
    _activeSubscriptions[sub.SubscriptionId] = entry
    IF recovery fails for individual subscription
      DELETE _subscriptionStore:external-subs:{appId} (set remove failed entry)
      // Continue with remaining subscriptions (no fail-fast)
WRITE _subscriptionStore:external-subs:{appId} (refresh TTL)
  // TTL = config.ExternalSubscriptionTtlSeconds

// Phase 2: TTL refresh (recurring loop)
IF _activeSubscriptions.Count > 0
  WRITE _subscriptionStore:external-subs:{appId} (refresh TTL)
```

---

### DeadLetterConsumerService
**Startup delay**: `config.DeadLetterConsumerStartupDelaySeconds` (default 5s)
**Trigger**: Event-driven (RabbitMQ consumer)
**Purpose**: Consumes dead-lettered messages, logs with structured metadata, publishes error events

```
IF !config.DeadLetterConsumerEnabled                    -> skip (service disabled)
// Declare durable queue "bannou-dlx-consumer" bound to DLX exchange on "dead-letter.#"
// Uses IChannelManager directly (not IMessageSubscriber) for BasicProperties.Headers access
// Durable shared queue with competing consumers for multi-instance safety
FOREACH dead letter message received
  // Extract AMQP headers: x-original-topic, x-retry-count,
  //   x-death, x-first-death-reason, x-original-exchange
  // Log structured error with all metadata
  _messageBus.TryPublishErrorAsync(severity: Warning)
  ACK message
  IF processing fails
    NACK without requeue
```

---

### MessageRetryBuffer (Internal Timer)
**Interval**: `config.RetryBufferIntervalSeconds` (default 5s)
**Purpose**: Retries buffered failed publishes; crash-fast on prolonged failure

```
// Construction: register observable gauge metrics
RegisterObservableGauge("bannou.messaging.retry_buffer_depth", () => _bufferCount)
RegisterObservableGauge("bannou.messaging.retry_buffer_fill_ratio", () => _bufferCount / maxSize)

// Guard: skip if retry buffer disabled or buffer empty
IF !config.RetryBufferEnabled OR buffer.IsEmpty         -> skip

// Crash-fast checks (before processing)
IF buffer.Count > config.RetryBufferMaxSize
  // CRASH NODE via IProcessTerminator.TerminateProcess()
IF oldest message age > config.RetryBufferMaxAgeSeconds
  // CRASH NODE via IProcessTerminator.TerminateProcess()

// Backpressure: new publishes rejected when buffer fill > config.RetryBufferBackpressureThreshold (80%)

FOREACH buffered message in ConcurrentQueue (dequeue)
  IF message.RetryCount >= config.RetryMaxAttempts
    // Poison message: discard to dead-letter exchange with x-retry-count header
    _messageBus.TryPublishErrorAsync(...)
  ELSE
    // Retry via IChannelManager.GetChannelAsync + BasicPublishAsync
    IF retry fails
      // Re-buffer with incremented retry count
      // Exponential backoff: config.RetryDelayMs * 2^retryCount, capped at config.RetryMaxBackoffMs

// After processing: record per-status retry attempt counters
RecordCounter("bannou.messaging.retry_attempts", processedCount, status=processed)
RecordCounter("bannou.messaging.retry_attempts", failedCount, status=failed)
RecordCounter("bannou.messaging.retry_attempts", discardedCount, status=discarded)
RecordCounter("bannou.messaging.retry_attempts", deferredCount, status=deferred)
```

---

## Non-Standard Implementation Patterns

#### ConfigureServices (mode branching)

The plugin's `ConfigureServices` contains significant branching logic that determines the entire infrastructure backend:

```
IF config.UseInMemory
  // Testing/minimal infrastructure mode
  REGISTER InMemoryMessageBus as IMessageBus + IMessageSubscriber (Singleton)
  REGISTER InMemoryMessageTap as IMessageTap (Singleton)
  // No background services registered — return early
ELSE
  // Production RabbitMQ mode
  REGISTER RabbitMQConnectionManager as IChannelManager (Singleton)
    // Constructor registers ObservableGauge: channel_pool_active, channel_pool_available
  REGISTER MessageRetryBuffer as IRetryBuffer (Singleton)
    // Constructor registers ObservableGauge: retry_buffer_depth, retry_buffer_fill_ratio
    // Factory breaks circular DI: IMessageBus → RabbitMQMessageBus → IRetryBuffer → MessageRetryBuffer
    // Passes null for optional IMessageBus? parameter
  REGISTER RabbitMQMessageBus as IMessageBus (Singleton)
    // Factory injects ITelemetryProvider + IMeshInstanceIdentifier
  REGISTER RabbitMQMessageSubscriber as IMessageSubscriber (Singleton)
  REGISTER RabbitMQMessageTap as IMessageTap (Singleton)
  REGISTER MessagingUnhandledExceptionHandler as IUnhandledExceptionHandler (Singleton)
  REGISTER NativeEventConsumerBackend as IHostedService
  REGISTER MessagingSubscriptionRecoveryService as IHostedService
  REGISTER DeadLetterConsumerService as IHostedService
  // Also registers MessagingService concrete type (cast from IMessagingService)
  // for MessagingSubscriptionRecoveryService to access internal recovery methods
```

This branching means in-memory mode has no background services, no retry buffer, no dead letter processing, and no subscription recovery. The `IMessageBus`/`IMessageSubscriber` interface contract is preserved but the reliability guarantees differ significantly.
