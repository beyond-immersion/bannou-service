# Telemetry Metrics Reference

> **Version**: 1.0
> **Last Updated**: 2026-03-13
> **Scope**: All metric instrumentation via `ITelemetryProvider`
> **Prerequisites**: [TENETS.md § T30](TENETS.md) (span instrumentation), [HELPERS-AND-COMMON-PATTERNS.md § 10](HELPERS-AND-COMMON-PATTERNS.md#10-telemetry) (span patterns)

This document is the authoritative reference for Bannou's metrics instrumentation — the constants, tag taxonomy, instrument types, registration patterns, and the complete metric inventory. For **span** (distributed tracing) patterns, see T30 and the HELPERS telemetry section; this document covers **metrics** exclusively.

---

## Architecture Overview

Bannou metrics are built on .NET's `System.Diagnostics.Metrics` API, exposed through `ITelemetryProvider`. All infrastructure (L0) services instrument themselves; application/game services inherit automatic instrumentation from the infrastructure they use.

```
ITelemetryProvider (bannou-service/Services/ITelemetryProvider.cs)
       │
       ├── RecordCounter(component, metric, value, tags)       → Point-in-time increment
       ├── RecordHistogram(component, metric, value, tags)     → Distribution observation
       └── RegisterObservableGauge(component, metric, callback) → Polled current-state
       │
       └── WrapStateStore<T>(...) → returns InstrumentedStateStore<T>
           (automatic counter + histogram on every state store operation)

TelemetryProvider (lib-telemetry/Core/TelemetryProvider.cs)     → Real implementation
NullTelemetryProvider (bannou-service/Services/)                → No-op fallback
```

**Gating**: When `MetricsEnabled == false`, `NullTelemetryProvider` is injected. All methods are no-ops — callbacks are never registered, counters/histograms are never recorded. Zero runtime cost.

**Export**: Metrics are exported via OpenTelemetry Protocol (OTLP) or Prometheus scrape endpoint, configured externally. The instrumentation code is exporter-agnostic.

---

## Constants

### TelemetryComponents

Component names identify the OpenTelemetry `Meter` that owns the metric. Each L0 infrastructure lib has exactly one component.

**File**: `bannou-service/Services/ITelemetryProvider.cs` — `TelemetryComponents` static class

| Constant | Value | Owner |
|----------|-------|-------|
| `State` | `bannou.state` | lib-state (state store operations) |
| `Messaging` | `bannou.messaging` | lib-messaging (pub/sub operations) |
| `Mesh` | `bannou.mesh` | lib-mesh (service invocation) |
| `Telemetry` | `bannou.telemetry` | lib-telemetry (internal) |

### TelemetryMetrics

Metric names follow the pattern `bannou.{component}.{metric_name}`. Units use OpenTelemetry conventions: `_seconds` suffix for durations, `_{unit}` for other measured quantities.

**File**: `bannou-service/Services/ITelemetryProvider.cs` — `TelemetryMetrics` static class

All metric names are `public const string` fields. **New metrics MUST be added here** — inline metric name strings are forbidden for the same reason inline topic strings are forbidden (typo risk, no compile-time safety).

---

## Instrument Types

### Counter (`RecordCounter`)

Monotonically increasing count of discrete events. Use for operations that happen (invocations, publishes, retries, state changes).

```csharp
_telemetryProvider.RecordCounter(
    TelemetryComponents.Mesh,
    TelemetryMetrics.MeshInvocations,
    1,                                              // value (usually 1)
    new KeyValuePair<string, object?>("service", appId),
    new KeyValuePair<string, object?>("method", method),
    new KeyValuePair<string, object?>("success", success));
```

**When to use**: "How many times did X happen?" — invocations, messages published/consumed, retries, state transitions, failures.

### Histogram (`RecordHistogram`)

Distribution of measured values. Use for durations, sizes, and latencies.

```csharp
_telemetryProvider.RecordHistogram(
    TelemetryComponents.Mesh,
    TelemetryMetrics.MeshDuration,
    durationSeconds,                                // measured value
    new KeyValuePair<string, object?>("service", appId),
    new KeyValuePair<string, object?>("method", method),
    new KeyValuePair<string, object?>("success", success));
```

**When to use**: "How long did X take?" or "What was the distribution of X?" — operation durations, payload sizes, queue depths over time.

### Observable Gauge (`RegisterObservableGauge`)

Current-state value polled by the exporter on each scrape cycle. The callback executes only during export — zero cost between scrapes.

```csharp
// Simple callback (no tags)
_telemetryProvider.RegisterObservableGauge<int>(
    TelemetryComponents.Messaging,
    TelemetryMetrics.MessagingChannelPoolActive,
    () => TotalActiveChannels,
    unit: "{channels}",
    description: "Current number of active channels");

// Measurement callback (with tags) — for per-dimension observations
_telemetryProvider.RegisterObservableGauge<int>(
    TelemetryComponents.Mesh,
    TelemetryMetrics.MeshCircuitBreakerState,
    () => new Measurement<int>(observedValue, tags),
    unit: "{state}",
    description: "Circuit breaker state");
```

**When to use**: "What is the current value of X right now?" — pool sizes, buffer depths, connection counts, circuit breaker states. **Not** for things that happened (use counters) or distributions (use histograms).

**Registration**: Call in the constructor of the class that owns the observed state. The callback captures `this` and reads instance fields directly. Registration is idempotent — duplicate registrations for the same metric name are safe (the provider deduplicates).

**Callback rules**:
- Must be fast (executes on the export thread)
- Must be thread-safe (reads from `ConcurrentDictionary` or `volatile` fields)
- Must not throw (wrap in try-catch if reading external state)
- Must not perform I/O (no state store reads, no network calls)

---

## Tag Taxonomy

Tags (labels in Prometheus) provide dimensionality. Use consistent tag names across all metrics in a component.

### State Store Tags (automatic via `WrapStateStore`)

| Tag | Type | Values | Purpose |
|-----|------|--------|---------|
| `store` | string | State store name | Which store was accessed |
| `operation` | string | `Get`, `Save`, `Delete`, `BulkGet`, `Query`, ... | What operation was performed |
| `success` | bool | `true`/`false` | Whether the operation succeeded |

### Messaging Tags

| Tag | Type | Values | Purpose |
|-----|------|--------|---------|
| `topic` | string | Event topic name | Which topic was published to / consumed from |
| `exchange` | string | Exchange name | RabbitMQ exchange used |
| `success` | bool | `true`/`false` | Whether publish/consume succeeded |
| `status` | string | `processed`, `failed`, `discarded`, `deferred` | Retry buffer outcome |

### Mesh Tags

| Tag | Type | Values | Purpose |
|-----|------|--------|---------|
| `service` | string | App-id of target service | Which service was invoked |
| `method` | string | Endpoint method name | Which endpoint was called |
| `success` | bool | `true`/`false` | Whether invocation succeeded |
| `reason` | string | `status_502`, `status_503`, `timeout`, ... | Why a retry was needed |
| `app_id` | string | App-id | Circuit breaker target |
| `state` | string | `Closed`, `Open`, `HalfOpen` | Circuit breaker state after transition |

### Tag Naming Conventions

- **snake_case** for tag keys (OpenTelemetry convention): `app_id`, not `appId`
- **Consistent across metrics**: If two metrics in the same component both identify a service, both use `service` (not `service` on one and `app_id` on another — unless semantically different)
- **Low cardinality**: Tags with unbounded values (request IDs, user IDs, timestamps) are forbidden — they create metric explosion
- **Boolean tags**: Use `bool` type directly, not `"true"`/`"false"` strings

---

## Metric Inventory

### State Store (`bannou.state`)

| Metric | Type | Unit | Tags | Description |
|--------|------|------|------|-------------|
| `bannou.state.operations` | Counter | — | `store`, `operation`, `success` | Count of state store operations |
| `bannou.state.duration_seconds` | Histogram | seconds | `store`, `operation`, `success` | Duration of state store operations |

These are **automatically recorded** by `InstrumentedStateStore<T>` (created via `ITelemetryProvider.WrapStateStore<T>`). No manual instrumentation needed in service code — lib-telemetry wraps every state store at factory resolution time.

### Messaging (`bannou.messaging`)

| Metric | Type | Unit | Tags | Description |
|--------|------|------|------|-------------|
| `bannou.messaging.published` | Counter | — | `topic`, `exchange`, `success` | Messages published to RabbitMQ |
| `bannou.messaging.consumed` | Counter | — | `topic`, `exchange`, `success` | Messages consumed from RabbitMQ |
| `bannou.messaging.publish_duration_seconds` | Histogram | seconds | `topic`, `exchange`, `success` | Time to publish a message |
| `bannou.messaging.consume_duration_seconds` | Histogram | seconds | `topic`, `exchange`, `success` | Time to consume a message (handler execution) |
| `bannou.messaging.retry_buffer_depth` | Gauge | {messages} | — | Current number of messages awaiting retry |
| `bannou.messaging.retry_buffer_fill_ratio` | Gauge | 1 | — | Retry buffer fill ratio (0.0–1.0 relative to max size) |
| `bannou.messaging.channel_pool_active` | Gauge | {channels} | — | Active channels (pooled + in-use + consumer) |
| `bannou.messaging.channel_pool_available` | Gauge | {channels} | — | Channels available in pool for reuse |
| `bannou.messaging.retry_attempts` | Counter | — | `status` | Retry buffer processing outcomes (processed/failed/discarded/deferred) |

### Mesh (`bannou.mesh`)

| Metric | Type | Unit | Tags | Description |
|--------|------|------|------|-------------|
| `bannou.mesh.invocations` | Counter | — | `service`, `method`, `success` | Service-to-service invocations via mesh (with circuit breaker) |
| `bannou.mesh.raw_invocations` | Counter | — | `service`, `method`, `success` | Raw API invocations (bypass circuit breaker; used by prebound APIs) |
| `bannou.mesh.duration_seconds` | Histogram | seconds | `service`, `method`, `success` | Invocation duration (both standard and raw) |
| `bannou.mesh.retries` | Counter | — | `service`, `method`, `reason` | Retry attempts for failed invocations |
| `bannou.mesh.circuit_breaker_state` | Gauge | {state} | — | Worst circuit breaker state across all tracked circuits (0=Closed, 1=Open, 2=HalfOpen) |
| `bannou.mesh.circuit_breaker_state_changes` | Counter | — | `app_id`, `state` | Circuit breaker state transitions |

---

## Adding Metrics to a Service

Most services need zero manual metrics work — state store operations are automatically instrumented, and messaging publish/consume metrics are recorded by the infrastructure libs. Custom metrics are only needed when a service has observable internal state worth monitoring (pool sizes, buffer depths, circuit states) or when tracking domain-specific operation counts.

### Step 1: Define the Constant

Add a `public const string` to `TelemetryMetrics` in `bannou-service/Services/ITelemetryProvider.cs`:

```csharp
/// <summary>
/// Gauge for widget pool utilization.
/// </summary>
public const string WidgetPoolUtilization = "bannou.widget.pool_utilization";
```

If the service introduces a new component (unlikely — most services use the existing L0 components), add a constant to `TelemetryComponents` as well.

### Step 2: Choose the Instrument Type

| Question | Instrument |
|----------|-----------|
| How many times did X happen? | Counter |
| How long did X take / what was the distribution? | Histogram |
| What is X right now? | Observable Gauge |

### Step 3: Register or Record

**Counters and Histograms** — record at the call site where the event happens:

```csharp
private void RecordWidgetMetrics(string widgetType, bool success, double durationSeconds)
{
    var tags = new[]
    {
        new KeyValuePair<string, object?>("widget_type", widgetType),
        new KeyValuePair<string, object?>("success", success)
    };

    _telemetryProvider.RecordCounter(TelemetryComponents.Widget, TelemetryMetrics.WidgetOperations, 1, tags);
    _telemetryProvider.RecordHistogram(TelemetryComponents.Widget, TelemetryMetrics.WidgetDuration, durationSeconds, tags);
}
```

**Observable Gauges** — register in the constructor of the class that owns the state:

```csharp
public WidgetPool(ITelemetryProvider telemetryProvider, ...)
{
    // ... other initialization ...

    _telemetryProvider.RegisterObservableGauge<int>(
        TelemetryComponents.Widget,
        TelemetryMetrics.WidgetPoolUtilization,
        () => _activeWidgets.Count,
        unit: "{widgets}",
        description: "Current number of active widgets in the pool");
}
```

### Step 4: Verify with NullTelemetryProvider

All instrumentation automatically works in tests via `NullTelemetryProvider` (injected when telemetry is disabled). No special test setup needed — the no-op implementation handles all calls safely.

---

## What NOT to Instrument

- **Individual request IDs or entity IDs as tags** — creates metric cardinality explosion
- **Metrics that duplicate existing infrastructure instrumentation** — state store and messaging operations are already covered
- **Metrics on synchronous methods** — gauges are for async-observable state; counters/histograms are for operations that already have spans (the span duration IS the histogram)
- **Per-endpoint counters in service code** — generated controllers already create spans per endpoint; use span-derived metrics (e.g., via OpenTelemetry span-to-metrics connector) rather than hand-coded counters

---

## Relationship to Spans (T30)

Spans and metrics serve complementary purposes:

| Concern | Spans (T30) | Metrics (this document) |
|---------|-------------|------------------------|
| **Question answered** | "What happened during this request?" | "How is the system performing overall?" |
| **Granularity** | Per-request trace | Aggregated over time |
| **Cost** | Per-request overhead | Per-scrape-cycle overhead |
| **Cardinality** | Unbounded (trace IDs, entity IDs OK) | Bounded (low-cardinality tags only) |
| **Storage** | Trace backends (Jaeger, Tempo) | Metrics backends (Prometheus, OTLP) |

**Rule of thumb**: If you need to debug a specific request, use spans. If you need a dashboard or alert, use metrics. Most services only need spans (T30) — custom metrics are for infrastructure-level concerns.
