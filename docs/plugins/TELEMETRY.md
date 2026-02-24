# Telemetry Plugin Deep Dive

> **Plugin**: lib-telemetry
> **Schema**: schemas/telemetry-api.yaml
> **Version**: 1.0.0
> **Layer**: Infrastructure
> **State Store**: None (stateless service)

---

## Overview

The Telemetry service (L0 Infrastructure, optional) provides unified observability infrastructure for Bannou using OpenTelemetry standards. Operates in a dual role: as the `ITelemetryProvider` interface that lib-state, lib-messaging, and lib-mesh use for instrumentation, and as an HTTP API providing health and status endpoints. Unique among Bannou services: uses no state stores and publishes no events. When disabled, other L0 services receive a `NullTelemetryProvider` (all methods are no-ops).

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| AppConfiguration | Fallback service name from EffectiveAppId when ServiceName not configured |
| OpenTelemetry SDK | Core tracing and metrics infrastructure |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | OTLP trace export (gRPC or HTTP) |
| OpenTelemetry.Exporter.Prometheus.AspNetCore | Prometheus metrics scraping endpoint |

**Note**: This plugin has **no dependencies on other Bannou plugins** - it is **optional Layer 0 infrastructure** per SERVICE_HIERARCHY. Unlike required L0 components (state, messaging, mesh) which cannot be disabled, telemetry can be freely disabled. When enabled, it loads FIRST so that required infrastructure libs can use `ITelemetryProvider` for instrumentation. Telemetry intentionally does not use lib-messaging for error events to avoid circular instrumentation concerns.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-state (StateStoreFactory) | Injects `ITelemetryProvider` to wrap state stores with instrumentation |
| lib-messaging (RabbitMQMessageBus, RabbitMQMessageSubscriber, NativeEventConsumerBackend) | Injects `ITelemetryProvider` for messaging operation tracing and metrics |
| lib-mesh (MeshInvocationClient) | Injects `ITelemetryProvider` for service mesh call tracing and metrics |

**Key Design**: `ITelemetryProvider` interface is defined in `bannou-service/Services/ITelemetryProvider.cs` (not in lib-telemetry) so infrastructure libs can depend on it without depending on the telemetry plugin. A `NullTelemetryProvider` is registered by default; when lib-telemetry loads, it overrides with the real implementation.

---

## State Storage

**Store**: None

This service is stateless. All telemetry data flows directly to external collectors (OTLP endpoint for traces, Prometheus scraping for metrics) without intermediate storage.

---

## Events

### Published Events

This plugin does not publish any business events.

### Consumed Events

This plugin does not consume external events.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `OtlpEndpoint` | `TELEMETRY_OTLP_ENDPOINT` | `http://localhost:4317` | OTLP collector endpoint for trace export |
| `OtlpProtocol` | `TELEMETRY_OTLP_PROTOCOL` | `grpc` | Transport protocol: `grpc` or `http` |
| `TracingEnabled` | `TELEMETRY_TRACING_ENABLED` | `true` | Enable/disable distributed tracing |
| `TracingSamplingRatio` | `TELEMETRY_TRACING_SAMPLING_RATIO` | `1.0` | Trace sampling ratio (0.0-1.0). Uses parent-based sampler with ratio-based sampling for root spans. |
| `MetricsEnabled` | `TELEMETRY_METRICS_ENABLED` | `true` | Enable/disable metrics via Prometheus scraping endpoint (/metrics) |
| `ServiceName` | `TELEMETRY_SERVICE_NAME` | `null` | Service name for telemetry identification (falls back to EffectiveAppId) |
| `ServiceNamespace` | `TELEMETRY_SERVICE_NAMESPACE` | `bannou` | Service namespace for telemetry grouping |
| `DeploymentEnvironment` | `TELEMETRY_DEPLOYMENT_ENVIRONMENT` | `development` | Deployment environment attribute |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<TelemetryService>` | Structured logging |
| `TelemetryServiceConfiguration` | Typed configuration access |
| `AppConfiguration` | Access to EffectiveAppId for service name fallback |
| `ITelemetryProvider` (Singleton) | Main instrumentation interface registered by plugin |
| `TelemetryProvider` | Implementation that manages ActivitySources and Meters |

### Internal Classes

| Class | Role |
|-------|------|
| `TelemetryProvider` | Manages `ActivitySource` and `Meter` instances per component; wraps state stores with instrumentation decorators |
| `InstrumentedStateStore<T>` | Decorator adding tracing and metrics to `IStateStore<T>` |
| `InstrumentedCacheableStateStore<T>` | Decorator for `ICacheableStateStore<T>` (extends InstrumentedStateStore) |
| `InstrumentedQueryableStateStore<T>` | Decorator for `IQueryableStateStore<T>` |
| `InstrumentedSearchableStateStore<T>` | Decorator for `ISearchableStateStore<T>` |
| `InstrumentedJsonQueryableStateStore<T>` | Decorator for `IJsonQueryableStateStore<T>` |

---

## API Endpoints (Implementation Notes)

### Health & Status

| Endpoint | Notes |
|----------|-------|
| `POST /telemetry/health` | Returns health status (`healthy` always true if service running), tracing/metrics enabled flags, and OTLP endpoint (null if both disabled) |
| `POST /telemetry/status` | Returns full configuration details including sampling ratio, service name, namespace, environment, and OTLP protocol |

Both endpoints are simple configuration introspection - no state access or side effects.

**Trace Filtering**: The OpenTelemetry SDK is configured to exclude `/health`, `/telemetry/health`, and `/metrics` paths from automatic ASP.NET Core tracing to reduce noise. Similarly, HttpClient instrumentation excludes requests with "health" in the path.

---

## Visual Aid

```
┌─────────────────────────────────────────────────────────────────────┐
│                    TELEMETRY INSTRUMENTATION FLOW                   │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  Plugin Startup (TelemetryServicePlugin)                            │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │ 1. Register ITelemetryProvider (Singleton)                    │  │
│  │ 2. Configure OpenTelemetry SDK:                               │  │
│  │    - AddAspNetCoreInstrumentation (tracing)                   │  │
│  │    - AddHttpClientInstrumentation (tracing)                   │  │
│  │    - AddSource: bannou.state, bannou.messaging, bannou.mesh,  │  │
│  │                 bannou.telemetry                              │  │
│  │    - AddOtlpExporter (traces → OTLP endpoint)                 │  │
│  │    - AddPrometheusExporter (metrics → /metrics)               │  │
│  └───────────────────────────────────────────────────────────────┘  │
│                              │                                       │
│                              ▼                                       │
│  TelemetryProvider (Singleton)                                      │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │ ConcurrentDictionary<component, ActivitySource>               │  │
│  │ ConcurrentDictionary<component, Meter>                        │  │
│  │ ConcurrentDictionary<key, Counter<long>>                      │  │
│  │ ConcurrentDictionary<key, Histogram<double>>                  │  │
│  └───────────────────────────────────────────────────────────────┘  │
│                              │                                       │
│                              ▼                                       │
│  State Store Wrapping (via WrapStateStore)                          │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │ Original Store → InstrumentedStateStore → Client              │  │
│  │                                                               │  │
│  │ Each operation:                                               │  │
│  │ 1. StartActivity("state.{operation}") with db.* tags         │  │
│  │ 2. Stopwatch.StartNew()                                       │  │
│  │ 3. Execute inner operation                                    │  │
│  │ 4. RecordCounter + RecordHistogram (success/failure)         │  │
│  └───────────────────────────────────────────────────────────────┘  │
│                                                                     │
│  Standard Components (TelemetryComponents):                         │
│  • bannou.state     → State store operations                        │
│  • bannou.messaging → Message publish/consume                       │
│  • bannou.mesh      → Service mesh invocations                      │
│  • bannou.telemetry → Telemetry service own operations              │
│                                                                     │
│  Standard Metrics (TelemetryMetrics):                               │
│  • bannou.state.operations, bannou.state.duration_seconds           │
│  • bannou.messaging.published, bannou.messaging.consumed            │
│  • bannou.messaging.publish_duration_seconds,                       │
│    bannou.messaging.consume_duration_seconds                        │
│  • bannou.mesh.invocations, bannou.mesh.duration_seconds            │
│  • bannou.mesh.circuit_breaker_state, ...state_changes, ...retries  │
│  • bannou.mesh.raw_invocations (no circuit breaker participation)   │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

None currently identified.

---

## Potential Extensions

1. **Managed platform exporters** ([#183](https://github.com/beyond-immersion/bannou-service/issues/183)): Add support for Datadog, Azure Application Insights, AWS X-Ray, and Elastic APM exporters beyond the base OTLP exporter.
<!-- AUDIT:NEEDS_DESIGN:2026-02-22:https://github.com/beyond-immersion/bannou-service/issues/183 -->

2. **Enhanced Grafana dashboards with SLO alerting** ([#185](https://github.com/beyond-immersion/bannou-service/issues/185)): Per-service dashboards, SLO alerting rules (availability, latency, error rate, saturation), error monitoring dashboards, and automated dashboard provisioning.
<!-- AUDIT:NEEDS_DESIGN:2026-02-22:https://github.com/beyond-immersion/bannou-service/issues/185 -->

3. **Metric aggregation views**: Add custom histogram bucket boundaries optimized for Bannou's typical latency distributions.
<!-- AUDIT:NEEDS_DESIGN:2026-02-22:https://github.com/beyond-immersion/bannou-service/issues/457 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None currently identified.

### Intentional Quirks (Documented Behavior)

1. **Plugin load priority**: Telemetry loads FIRST among infrastructure plugins (priority -1 in `InfrastructureLoadOrder`). This is enforced by PluginLoader to ensure `ITelemetryProvider` is available before lib-state, lib-messaging, and lib-mesh initialize. See `bannou-service/Plugins/PluginLoader.cs:39-45`.

2. **NullTelemetryProvider fallback**: If telemetry plugin fails to load or is disabled, infrastructure libs receive NullTelemetryProvider (all methods are no-ops). This is intentional graceful degradation, not a bug.

3. **Health always reports healthy**: The `/telemetry/health` endpoint always returns `healthy: true` if the service is running - there's no actual health check of the OTLP collector connection.

4. **Temporary service provider during setup**: `ConfigureOpenTelemetry` builds a temporary `ServiceProvider` to access configuration during SDK setup. This is documented as "the standard pattern for OpenTelemetry SDK setup when config is needed" but creates duplicate service instances temporarily.

5. **Counter/Histogram creation on first use**: Counters and histograms are created lazily via `GetOrCreateCounter`/`GetOrCreateHistogram`. The unreachable null branch (when MetricsEnabled is checked upstream) throws `InvalidOperationException` as an invariant guard rather than returning null.

6. **Parent-based trace sampling**: The `TracingSamplingRatio` is applied via a `ParentBasedSampler` wrapping a `TraceIdRatioBasedSampler`. This means child spans respect their parent's sampling decision, and only root spans are subject to ratio-based sampling. This is the recommended OpenTelemetry pattern for distributed tracing.

### Design Considerations (Requires Planning)

None currently identified.

---

## Work Tracking

| Issue | Status | Description |
|-------|--------|-------------|
| [#183](https://github.com/beyond-immersion/bannou-service/issues/183) | Open | Managed platform telemetry exporters (Datadog, Azure, AWS, Elastic) |
| [#185](https://github.com/beyond-immersion/bannou-service/issues/185) | Open | Enhanced Grafana dashboards with per-service views and SLO alerting |
| [#457](https://github.com/beyond-immersion/bannou-service/issues/457) | Open | Custom histogram bucket boundaries for Bannou latency profiles |
