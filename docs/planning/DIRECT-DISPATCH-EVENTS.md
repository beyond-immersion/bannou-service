# Direct Dispatch Events: Zero-Overhead Event Delivery for Embedded and Sidecar Modes

> **Type**: Design
> **Status**: Active
> **Created**: 2026-03-14
> **Last Updated**: 2026-03-14
> **North Stars**: #3, #4
> **Related Plugins**: Messaging, Connect
> **Prerequisites**: [DEPLOYMENT-MODES.md](DEPLOYMENT-MODES.md), [BANNOU-EMBEDDED.md](BANNOU-EMBEDDED.md)

## Summary

Introduces a `DirectDispatchMessageBus` — a third `IMessageBus` implementation alongside `RabbitMQMessageBus` and `InMemoryMessageBus` — that eliminates the messaging abstraction overhead for embedded and sidecar deployments. On `TryPublishAsync`, instead of routing through a pub/sub layer, it dispatches directly to `IEventConsumer` handlers and any direct subscribers. This is the event-side analog to BANNOU-EMBEDDED.md's direct DI dispatch for mesh clients: same interface, same semantics, zero transport overhead. Objects are passed by reference, no serialization, no subscription management for IEventConsumer handlers, no intermediate bridge layer.

---

## Problem Statement

### The Mesh Parallel

BANNOU-EMBEDDED.md identified that the mesh invocation path in embedded mode is pure waste:

```
Cloud:    ICollectionClient → serialize → HTTP → Kestrel → Controller → ICollectionService
Embedded: ICollectionClient → resolve from DI → ICollectionService directly
```

The same analysis applies to event delivery. The current InMemory event path for sidecar/embedded mode:

```
_messageBus.TryPublishAsync("topic", event)
  → InMemoryMessageBus._subscriptions lookup (ImmutableList)
    → fire-and-forget DeliverToSubscribersAsync()
      → iterate handlers (registered by NativeEventConsumerBackend at startup)
        → handler creates request scope
          → IEventConsumer.DispatchAsync(topic, event, scope)
            → iterate application handlers (ConcurrentDictionary lookup)
              → each handler resolves service from scope, invokes typed method
```

In embedded/sidecar mode, this entire intermediate layer is overhead. `InMemoryMessageBus` maintains its own subscription registry (`ConcurrentDictionary<string, ImmutableList<Func<...>>>`). `NativeEventConsumerBackend` creates subscriptions at startup for every topic in `IEventConsumer`. Events route through two registries, two dispatch loops, and a fire-and-forget coroutine — all within the same process, delivering to handlers that are already known at startup.

### The Direct Path

```
_messageBus.TryPublishAsync("topic", event)
  → create request scope
    → IEventConsumer.DispatchAsync(topic, event, scope)
      → iterate application handlers, invoke each
  → dispatch to any direct subscribers (Connect, etc.)
```

One registry. One dispatch loop. Zero intermediate layers. Objects passed by reference. Same `IMessageBus` interface — publishers don't know or care.

### Why Not Just Use InMemoryMessageBus?

InMemoryMessageBus already passes objects by reference and delivers in-process. But it carries structural overhead designed for compatibility with RabbitMQ's subscription model:

| Overhead | Purpose in RabbitMQ | Purpose in InMemory | Purpose in DirectDispatch |
|----------|--------------------|--------------------|--------------------------|
| `NativeEventConsumerBackend` (IHostedService) | Creates RabbitMQ queue bindings per topic | Creates InMemory subscriptions per topic | **Not needed** — publish dispatches directly |
| `EventSubscriptionRegistry` (topic→Type map) | Deserializes JSON messages to typed events | Not used (objects by reference) | **Not needed** — objects by reference |
| InMemory subscription registry (ImmutableList per topic) | N/A | Stores handler references for delivery | **Not needed for IEventConsumer** — handlers already in IEventConsumer's own registry |
| Fire-and-forget coroutine | N/A | Matches async RabbitMQ delivery semantics | **Optional** — configurable sync/async |

The InMemoryMessageBus is a faithful in-process replica of RabbitMQ's pub/sub model. DirectDispatch drops the replica and dispatches to the actual target.

---

## Architecture

### The Three Messaging Backends

| Backend | When | Transport | Serialization | Cross-Node |
|---------|------|-----------|---------------|------------|
| `RabbitMQMessageBus` | Cloud (dedicated, hyper-scaled) | RabbitMQ broker | JSON via BannouJson | Yes |
| `InMemoryMessageBus` | Testing, fallback | In-process ImmutableList dispatch | None (by reference) | No |
| `DirectDispatchMessageBus` | Embedded, sidecar | Direct IEventConsumer dispatch | None (by reference) | No |

Selection via configuration:

```bash
# Cloud (default — no flag needed)
# Uses RabbitMQMessageBus

# Sidecar
MESSAGING_USE_INMEMORY=true            # Current: InMemoryMessageBus
MESSAGING_USE_DIRECT_DISPATCH=true     # New: DirectDispatchMessageBus

# Embedded
# DirectDispatch selected automatically (no messaging infrastructure)
```

`MESSAGING_USE_DIRECT_DISPATCH=true` takes precedence over `MESSAGING_USE_INMEMORY=true`. When both are set, DirectDispatch is used.

### Component Interaction

```
                          ┌─────────────────────────────────┐
                          │  IEventConsumer (Singleton)      │
                          │  ConcurrentDictionary<topic,     │
                          │    ConcurrentDictionary<key,     │
                          │      Func<IServiceProvider,      │
                          │           object, Task>>>        │
                          └──────────┬──────────────────────┘
                                     │
                  ┌──────────────────┼──────────────────────┐
                  │                  │                       │
        ┌─────────▼───────┐  ┌──────▼────────┐  ┌──────────▼──────────┐
        │ RabbitMQMessageBus│  │ InMemoryMsgBus│  │ DirectDispatchMsgBus│
        │                   │  │               │  │                     │
        │ Publish → broker  │  │ Publish →     │  │ Publish →           │
        │ Subscribe → queue │  │  ImmutableList│  │  IEventConsumer     │
        │ NativeBackend     │  │  NativeBackend│  │  .DispatchAsync()   │
        │  bridges to       │  │  bridges to   │  │  (direct call)      │
        │  IEventConsumer   │  │  IEventConsumer│  │                     │
        └───────────────────┘  └───────────────┘  │ + _directSubscribers│
                                                  │  for Connect, etc.  │
                                                  └─────────────────────┘
```

Key difference: RabbitMQ and InMemory both use NativeEventConsumerBackend as a bridge between the messaging transport and IEventConsumer. DirectDispatch eliminates the bridge — the `IMessageBus.TryPublishAsync` call resolves to `IEventConsumer.DispatchAsync` directly.

---

## Implementation

### The DirectDispatchMessageBus Class

**Location**: `plugins/lib-messaging/Services/DirectDispatchMessageBus.cs`

```csharp
/// <summary>
/// Zero-overhead event delivery for embedded and sidecar deployments.
/// Dispatches directly to IEventConsumer handlers on TryPublishAsync,
/// bypassing the pub/sub subscription layer entirely.
/// Objects are passed by reference — no serialization.
/// This is the event-side analog to embedded mesh direct DI dispatch.
/// </summary>
public class DirectDispatchMessageBus : IMessageBus, IMessageSubscriber
{
    private readonly IEventConsumer _eventConsumer;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DirectDispatchMessageBus> _logger;

    // For non-IEventConsumer subscribers (Connect per-session, Messaging HTTP callbacks)
    private readonly ConcurrentDictionary<string, ImmutableList<Func<object, CancellationToken, Task>>>
        _directSubscriptions = new();

    public async Task<bool> TryPublishAsync<TEvent>(
        string topic, TEvent eventData, CancellationToken ct = default)
    {
        // Path 1: IEventConsumer handlers (main path — zero overhead)
        using var scope = _serviceProvider.CreateScope();
        await _eventConsumer.DispatchAsync(topic, eventData!, scope.ServiceProvider);

        // Path 2: Direct subscribers (Connect per-session, etc.)
        if (_directSubscriptions.TryGetValue(topic, out var handlers))
        {
            var snapshot = handlers; // ImmutableList snapshot is thread-safe
            foreach (var handler in snapshot)
            {
                try
                {
                    await handler(eventData!, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Direct subscriber failed for topic {Topic}", topic);
                }
            }
        }

        return true;
    }

    // IMessageSubscriber — used by Connect and other direct subscribers
    public Task<IAsyncDisposable> SubscribeDynamicAsync<TEvent>(
        string topic, Func<TEvent, CancellationToken, Task> handler)
    {
        // Wrap typed handler as object-typed (same pattern as InMemoryMessageBus)
        Func<object, CancellationToken, Task> wrappedHandler =
            (obj, ct) => handler((TEvent)obj, ct);

        _directSubscriptions.AddOrUpdate(
            topic,
            _ => ImmutableList.Create(wrappedHandler),
            (_, existing) => existing.Add(wrappedHandler));

        // Return disposable that removes this handler
        return Task.FromResult<IAsyncDisposable>(
            new DirectSubscription(this, topic, wrappedHandler));
    }

    // Static subscriptions (SubscribeAsync) — same implementation as dynamic
    // but without the IAsyncDisposable return (lifecycle = process lifetime)

    // SubscribeDynamicRawAsync — for Connect binary protocol forwarding
    // Serializes to JSON bytes (same as InMemoryMessageBus)
}
```

### DI Registration Changes

**Location**: `plugins/lib-messaging/MessagingServicePlugin.cs`

```csharp
if (configuration.UseDirectDispatch)
{
    // DirectDispatch: IMessageBus dispatches to IEventConsumer directly
    services.AddSingleton<IMessageBus, DirectDispatchMessageBus>();
    services.AddSingleton<IMessageSubscriber>(sp =>
        (DirectDispatchMessageBus)sp.GetRequiredService<IMessageBus>());
    // NativeEventConsumerBackend is NOT registered — no bridge needed
}
else if (configuration.UseInMemory)
{
    // InMemory: existing path with NativeEventConsumerBackend bridge
    services.AddSingleton<IMessageBus, InMemoryMessageBus>();
    services.AddSingleton<IMessageSubscriber>(sp =>
        (InMemoryMessageBus)sp.GetRequiredService<IMessageBus>());
    services.AddHostedService<NativeEventConsumerBackend>();
}
else
{
    // RabbitMQ: existing production path
    services.AddSingleton<IMessageBus, RabbitMQMessageBus>();
    services.AddSingleton<IMessageSubscriber>(sp =>
        (RabbitMQMessageBus)sp.GetRequiredService<IMessageBus>());
    services.AddHostedService<NativeEventConsumerBackend>();
}
```

### Configuration Schema Change

**Location**: `schemas/messaging-configuration.yaml`

```yaml
UseDirectDispatch:
  type: boolean
  default: false
  description: >
    When true, events are dispatched directly to IEventConsumer handlers
    without going through the pub/sub subscription layer. Zero serialization,
    zero transport overhead. Intended for embedded and sidecar deployments
    where all services run in-process. Takes precedence over UseInMemory.
  env: MESSAGING_USE_DIRECT_DISPATCH
```

---

## What Changes, What Doesn't

### Unchanged (Zero Modifications)

| Component | Why Unchanged |
|-----------|--------------|
| `IEventConsumer` / `EventConsumer` | Handler registry is independent of messaging backend. DispatchAsync works identically. |
| `EventConsumerExtensions` | Registration helpers don't touch messaging. |
| All `*ServiceEvents.cs` files | `RegisterEventConsumers(IEventConsumer)` calls are backend-agnostic. |
| All `_messageBus.TryPublishAsync()` call sites | Same `IMessageBus` interface. Publishers don't know the backend. |
| All `_messageBus.Publish*Async()` generated extensions | Same interface, same call pattern. |
| DI listener dispatch in service code | DI listeners are fired by producing services, not by the messaging backend. Completely orthogonal. |
| Generated event publishers / topic constants | Topic strings are the same regardless of backend. |
| Connect per-session subscriptions | Uses `SubscribeDynamicAsync` / `SubscribeDynamicRawAsync` — DirectDispatch implements both. |

### New Files

| File | Purpose |
|------|---------|
| `plugins/lib-messaging/Services/DirectDispatchMessageBus.cs` | The implementation |

### Modified Files

| File | Change |
|------|--------|
| `plugins/lib-messaging/MessagingServicePlugin.cs` | Conditional DI registration based on `UseDirectDispatch` |
| `schemas/messaging-configuration.yaml` | Add `UseDirectDispatch` property |

### Regeneration

After schema change: `cd scripts && ./generate-config.sh messaging`

---

## Design Decisions

### D1: Always Dispatch, Never Skip

**Status**: Decided

`TryPublishAsync` always creates a request scope and calls `IEventConsumer.DispatchAsync`, even if no handlers are currently registered for the topic. The default assumption is **we don't know who the subscribers are** — Analytics will eventually listen to everything, and third-party L5 extensions may subscribe to events we can't predict. `DispatchAsync` already handles the "no handlers" case gracefully (returns immediately after dictionary lookup miss).

An optional `SkipUnhandledTopics` configuration property (default: `false`) can enable the scope-creation skip for deployments that have verified their subscriber set is complete. This is a performance optimization for resource-constrained embedded devices, not a default behavior.

```yaml
SkipUnhandledTopics:
  type: boolean
  default: false
  description: >
    When true, TryPublishAsync skips scope creation for topics with no
    registered IEventConsumer handlers. Saves allocation cost but assumes
    no external or future subscribers exist for unhandled topics. Only
    enable when the subscriber set is known and complete.
  env: MESSAGING_SKIP_UNHANDLED_TOPICS
```

### D2: Fire-and-Forget Semantics Match InMemoryMessageBus

**Status**: Decided

`InMemoryMessageBus.TryPublishAsync` returns immediately and delivers asynchronously (fire-and-forget via `_ = DeliverToSubscribersAsync()`). DirectDispatch should match this: the producer's `await TryPublishAsync()` should not block on handler execution.

Implementation: wrap the dispatch in a fire-and-forget task, same as InMemoryMessageBus. The scope lifetime must be managed carefully — the scope is created inside the fire-and-forget task, not in the calling method.

```csharp
public Task<bool> TryPublishAsync<TEvent>(string topic, TEvent eventData, CancellationToken ct)
{
    _ = DispatchAsync(topic, eventData!, ct);
    return Task.FromResult(true);
}

private async Task DispatchAsync<TEvent>(string topic, TEvent eventData, CancellationToken ct)
{
    try
    {
        using var scope = _serviceProvider.CreateScope();
        await _eventConsumer.DispatchAsync(topic, eventData!, scope.ServiceProvider);
        await DispatchToDirectSubscribersAsync(topic, eventData!, ct);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "DirectDispatch failed for topic {Topic}", topic);
    }
}
```

### D3: NativeEventConsumerBackend Is Not Registered

**Status**: Decided

When DirectDispatch is active, `NativeEventConsumerBackend` (the IHostedService that bridges IMessageSubscriber to IEventConsumer) is simply not registered in DI. No conditional logic inside NativeEventConsumerBackend — it doesn't exist in the container.

This means `EventSubscriptionRegistry` is also unused (it exists for deserialization, which DirectDispatch doesn't need). The static registry still gets populated by the generated `RegisterAll()` call in Program.cs (harmless, ~0.1ms startup cost), but nothing reads from it.

### D4: DirectDispatch Is a Superset of InMemory for Single-Node

**Status**: Decided

There is no deployment scenario where InMemoryMessageBus is preferred over DirectDispatchMessageBus for single-node operation. DirectDispatch does everything InMemory does (in-process delivery, by-reference, no persistence) with less overhead.

However, InMemoryMessageBus is retained for:
- **Backward compatibility**: existing tests that explicitly configure `UseInMemory=true`
- **Gradual adoption**: teams can opt into DirectDispatch when ready
- **Future distributed sidecar**: if the "anchors" vision materializes, the InMemory path may evolve into a lightweight network transport while DirectDispatch remains the local-only path

### D5: SubscribeDynamicRawAsync Serializes (Same as InMemory)

**Status**: Decided

Connect's `SubscribeDynamicRawAsync` receives events as `byte[]` (JSON bytes for WebSocket binary protocol forwarding). DirectDispatch handles this the same way InMemoryMessageBus does: serialize the event object to JSON bytes via `BannouJson.Serialize`, then pass to the raw handler. This is the one path where serialization occurs — and it only fires for topics that Connect has active session subscriptions for.

---

## Relationship to DI Listeners

DirectDispatch and DI listeners are **orthogonal** mechanisms that happen to converge in behavior for single-node deployments.

| Mechanism | Fires From | Fires When | Scope |
|-----------|-----------|------------|-------|
| DI listener | Producing service code (inline after mutation) | Synchronously after state is saved | Processing node only |
| DirectDispatch | `IMessageBus.TryPublishAsync` (the publish call) | Asynchronously via fire-and-forget | Same node (single-node) |

In sidecar mode, both mechanisms fire on the same (only) node. A service that implements a DI listener (like Genesis implementing `ICurrencyTransactionListener`) AND subscribes to events via IEventConsumer would receive two notifications — one from the DI listener (inline), one from DirectDispatch (async). This is identical to the behavior with InMemoryMessageBus or RabbitMQ (where the event subscription also fires on the processing node). Handlers must be idempotent regardless of messaging backend.

The key insight from the broader discussion: **DI listeners are the more efficient notification path for single-node deployments.** Services that can use DI listeners instead of event subscriptions benefit from zero-overhead synchronous dispatch. DirectDispatch reduces the overhead of the remaining event subscriptions that can't be converted to DI listeners (cross-layer notifications, analytics, third-party consumers). Together, they minimize messaging overhead for resource-constrained sidecar deployments.

### Future: Anchors (Distributed Sidecar)

If the "anchors" vision from [DEPLOYMENT-MODES.md](DEPLOYMENT-MODES.md) materializes (remote player machines contributing simulation capacity), the DI listener + self-subscription pattern provides cross-machine fan-out:

```
Machine A (host): produces event → fires DI listeners locally → publishes over transport
Machine B (anchor): receives event over transport → self-subscription fires DI listeners locally
```

DirectDispatch handles the local-only case. A future lightweight network transport (not RabbitMQ — too heavy for game clients) would handle the cross-machine case. The DI listener interface remains the universal consumer contract across both — consumer code is deployment-mode-agnostic.

---

## Scope

### Effort Estimate

| Work Item | Files | Effort |
|-----------|-------|--------|
| `DirectDispatchMessageBus` implementation | 1 new | Half day |
| Configuration schema + regeneration | 1 schema + regenerated config | 15 minutes |
| DI registration conditional | 1 modified | 15 minutes |
| Unit tests | 1 new test class | Half day |
| **Total** | **2 new, 2 modified** | **~1 day** |

### What This Does NOT Cover

1. **Lightweight network transport for anchors**: The distributed sidecar vision requires a cross-machine messaging transport. DirectDispatch is local-only. The anchor transport is a separate design effort.

2. **Embedded mesh direct dispatch**: BANNOU-EMBEDDED.md's `_directDispatch` flag for mesh clients is a prerequisite for full embedded mode but is a separate implementation from this event dispatch work.

3. **InMemoryMessageBus deprecation**: InMemory is retained. DirectDispatch is an additional option, not a replacement.

4. **DI listener self-subscription formalization**: The pattern where a producing service self-subscribes to its own events to fire DI listeners on all nodes is relevant for cloud multi-node deployments but orthogonal to DirectDispatch (which is single-node only).

---

## Related Documents

### Planning & Design
- [DEPLOYMENT-MODES.md](DEPLOYMENT-MODES.md) — The four deployment modes and their infrastructure backend matrices
- [BANNOU-EMBEDDED.md](BANNOU-EMBEDDED.md) — Embedded mesh direct dispatch (the request-side analog)
- [SELF-HOSTED-DEPLOYMENT.md](SELF-HOSTED-DEPLOYMENT.md) — Sidecar server design
- [BATCH-LIFECYCLE-EVENTS.md](BATCH-LIFECYCLE-EVENTS.md) — High-frequency event batching (complementary, not competing)

### Reference
- [SERVICE-HIERARCHY.md](../reference/SERVICE-HIERARCHY.md) — DI Provider vs Listener distributed safety rules
- [HELPERS-AND-COMMON-PATTERNS.md](../reference/HELPERS-AND-COMMON-PATTERNS.md) — DI listener interfaces, IEventConsumer, self-subscription cache invalidation

### Source Files
- `plugins/lib-messaging/Services/InMemoryMessageBus.cs` — Existing in-process backend (structural reference)
- `plugins/lib-messaging/Services/RabbitMQMessageBus.cs` — Production backend (structural reference)
- `plugins/lib-messaging/Services/NativeEventConsumerBackend.cs` — Bridge layer (eliminated by DirectDispatch)
- `bannou-service/Events/EventConsumer.cs` — Handler registry (unchanged, dispatched to directly)
- `bannou-service/Events/EventSubscriptionRegistry.cs` — Topic→Type map (unused by DirectDispatch)

---

*This document describes the event-side analog to BANNOU-EMBEDDED.md's direct DI dispatch for mesh clients. Both follow the same principle: in single-node deployments, the transport layer between same-process components is pure overhead. Remove it.*
