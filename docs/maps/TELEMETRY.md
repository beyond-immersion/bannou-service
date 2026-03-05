# Telemetry Implementation Map

> **Plugin**: lib-telemetry
> **Schema**: schemas/telemetry-api.yaml
> **Layer**: Infrastructure (L0, optional)
> **Deep Dive**: [docs/plugins/TELEMETRY.md](../plugins/TELEMETRY.md)

---

## Summary Table

| Field | Value |
|-------|-------|
| Plugin | lib-telemetry |
| Layer | L0 Infrastructure (optional) |
| Endpoints | 2 |
| State Stores | None (stateless service) |
| Events Published | 0 |
| Events Consumed | 0 |
| Client Events | 0 |
| Background Services | 0 |

---

## State

This service is stateless. It uses no state stores, acquires no `IStateStoreFactory`, and performs no reads or writes. All telemetry data flows directly to external collectors (OTLP endpoint for traces, Prometheus scraping for metrics).

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| `ILogger<TelemetryService>` | Framework | Hard | Structured logging |
| `TelemetryServiceConfiguration` | Generated | Hard | All 8 configuration properties |
| `AppConfiguration` | bannou-service | Hard | `EffectiveAppId` fallback when `ServiceName` not configured |
| `ITelemetryProvider` | bannou-service | Hard | Span instrumentation on own endpoints |

**No infrastructure lib dependencies**: Telemetry is the only Bannou plugin with zero `IStateStoreFactory`, `IMessageBus`, or `IDistributedLockProvider` usage. This is deliberate — using `IMessageBus` for error events would create circular instrumentation (messaging instruments telemetry, telemetry instruments messaging).

**No service client dependencies**: No `I*Client` injections. No service-to-service calls via lib-mesh.

**Inverted provider relationship**: `ITelemetryProvider` is defined in `bannou-service/Services/ITelemetryProvider.cs`, not in lib-telemetry. A `NullTelemetryProvider` is registered by default; lib-telemetry replaces it with the real `TelemetryProvider` singleton at plugin load. Other L0 libs (lib-state, lib-messaging, lib-mesh) depend on the shared interface, not on the plugin.

**Load order priority -1**: Telemetry loads first among L0 plugins so `ITelemetryProvider` is available before lib-state, lib-messaging, and lib-mesh initialize.

---

## Events Published

This plugin publishes no events. The events schema explicitly declares `x-event-publications: []` to document this as intentional (avoids circular instrumentation with lib-messaging).

---

## Events Consumed

This plugin does not consume external events. No `TelemetryServiceEvents.cs` file exists. The events schema declares `x-event-subscriptions: []`.

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<TelemetryService>` | Structured logging |
| `TelemetryServiceConfiguration` | Typed configuration access (8 properties) |
| `AppConfiguration` | `EffectiveAppId` for service name fallback |
| `ITelemetryProvider` | Span instrumentation via `StartActivity` |

### Internal Classes (not DI-injected into service, but registered by plugin)

| Class | File | Role |
|-------|------|------|
| `TelemetryProvider` | `Core/TelemetryProvider.cs` | Full `ITelemetryProvider` implementation; manages `ActivitySource` and `Meter` instances per component in `ConcurrentDictionary` caches; wraps state stores with instrumented decorators |
| `InstrumentedStateStore<T>` | `Instrumentation/InstrumentedStateStore.cs` | Decorator wrapping `IStateStore<T>` with tracing spans and counter/histogram metrics |
| `InstrumentedCacheableStateStore<T>` | `Instrumentation/InstrumentedStateStore.cs` | Extends `InstrumentedStateStore<T>` for `ICacheableStateStore<T>` operations (sets, sorted sets, atomic counters) |
| `InstrumentedQueryableStateStore<T>` | `Instrumentation/InstrumentedQueryableStateStore.cs` | Extends `InstrumentedStateStore<T>` for `IQueryableStateStore<T>` operations (Query, Count) |
| `InstrumentedJsonQueryableStateStore<T>` | `Instrumentation/InstrumentedJsonQueryableStateStore.cs` | Extends `InstrumentedQueryableStateStore<T>` for JSON query operations (JsonQuery, JsonCount, JsonDistinct, JsonAggregate) |
| `InstrumentedSearchableStateStore<T>` | `Instrumentation/InstrumentedSearchableStateStore.cs` | Extends `InstrumentedCacheableStateStore<T>` for full-text search operations (CreateIndex, Search, Suggest) |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| Health | POST /telemetry/health | [] | - | - |
| Status | POST /telemetry/status | [] | - | - |

---

## Methods

### Health
`POST /telemetry/health` | Roles: []

```
IF config.TracingEnabled OR config.MetricsEnabled
  endpoint = config.OtlpEndpoint
ELSE
  endpoint = null
RETURN (200, TelemetryHealthResponse {
  tracingEnabled: config.TracingEnabled,
  metricsEnabled: config.MetricsEnabled,
  otlpEndpoint: endpoint
})
```

### Status
`POST /telemetry/status` | Roles: []

```
IF config.ServiceName is non-null and non-whitespace
  serviceName = config.ServiceName
ELSE
  serviceName = appConfiguration.EffectiveAppId
RETURN (200, TelemetryStatusResponse {
  tracingEnabled: config.TracingEnabled,
  metricsEnabled: config.MetricsEnabled,
  samplingRatio: config.TracingSamplingRatio,
  serviceName: serviceName,
  serviceNamespace: config.ServiceNamespace,
  deploymentEnvironment: config.DeploymentEnvironment,
  otlpEndpoint: config.OtlpEndpoint,
  otlpProtocol: config.OtlpProtocol
})
```

---

## Background Services

No background services.
