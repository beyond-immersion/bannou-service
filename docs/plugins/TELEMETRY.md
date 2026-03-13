# Telemetry Plugin Deep Dive

> **Plugin**: lib-telemetry
> **Schema**: schemas/telemetry-api.yaml
> **Version**: 1.0.0
> **Layer**: Infrastructure
> **State Store**: None (stateless service)
> **Implementation Map**: [docs/maps/TELEMETRY.md](../maps/TELEMETRY.md)
> **Short**: OpenTelemetry distributed tracing and metrics via ITelemetryProvider (optional, NullTelemetryProvider fallback)

---

## Overview

The Telemetry service (L0 Infrastructure, optional) provides unified observability infrastructure for Bannou using OpenTelemetry standards. Operates in a dual role: as the `ITelemetryProvider` interface that lib-state, lib-messaging, and lib-mesh use for instrumentation, and as an HTTP API providing health and status endpoints. Unique among Bannou services: uses no state stores and publishes no events. When disabled, other L0 services receive a `NullTelemetryProvider` (all methods are no-ops).

This plugin has **no dependencies on other Bannou plugins** — it is optional Layer 0 infrastructure per Service Hierarchy. Unlike required L0 components (state, messaging, mesh) which cannot be disabled, telemetry can be freely disabled. When enabled, it loads FIRST so that required infrastructure libs can use `ITelemetryProvider` for instrumentation. The service class does not inject `IMessageBus` directly — it has no events to publish and no event subscriptions. The generated controller still calls `TryPublishErrorAsync` on unexpected endpoint failures as normal (per IMPLEMENTATION TENETS). There is no circular dependency concern: `ITelemetryProvider.StartActivity` creates spans on lib-messaging operations but does not call back into telemetry service endpoints, and `NullTelemetryProvider` provides a safe no-op fallback if the real provider is unavailable.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-state (StateStoreFactory) | Injects `ITelemetryProvider` to wrap state stores with instrumentation |
| lib-messaging (RabbitMQMessageBus, RabbitMQMessageSubscriber, NativeEventConsumerBackend, RabbitMQMessageTap, RabbitMQConnectionManager, DeadLetterConsumerService, MessageRetryBuffer) | Injects `ITelemetryProvider` for messaging operation tracing and metrics |
| lib-mesh (MeshInvocationClient, DistributedCircuitBreaker, MeshHealthCheckService, MeshStateManager) | Injects `ITelemetryProvider` for service mesh call tracing and metrics |

**Key Design**: `ITelemetryProvider` interface is defined in `bannou-service/Services/ITelemetryProvider.cs` (not in lib-telemetry) so infrastructure libs can depend on it without depending on the telemetry plugin. A `NullTelemetryProvider` is registered by default; when lib-telemetry loads, it overrides with the real implementation.

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
│  │    - HealthTrackingExporter → OtlpExporter (traces → OTLP)     │  │
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
│  │ ConcurrentDictionary<key, object>  (ObservableGauges)         │  │
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
│  • bannou.messaging.retry_buffer_depth (gauge)                      │
│  • bannou.messaging.retry_buffer_fill_ratio (gauge)                 │
│  • bannou.messaging.channel_pool_active (gauge)                     │
│  • bannou.messaging.channel_pool_available (gauge)                  │
│  • bannou.messaging.retry_attempts (counter, tagged by status)      │
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

1. **Plugin load priority**: Telemetry loads FIRST among infrastructure plugins (priority -1 in `InfrastructureLoadOrder`). This is enforced by PluginLoader to ensure `ITelemetryProvider` is available before lib-state, lib-messaging, and lib-mesh initialize. See `bannou-service/Plugins/PluginLoader.cs:40-42`.

2. **NullTelemetryProvider fallback**: If telemetry plugin fails to load or is disabled, infrastructure libs receive NullTelemetryProvider (all methods are no-ops). This is intentional graceful degradation, not a bug.

3. **Passive OTLP health tracking**: The `/telemetry/health` endpoint reports OTLP export health (`otlpExportHealthy`, `consecutiveExportFailures`, `lastSuccessfulExportAt`) via a `HealthTrackingExporter` decorator that passively observes export outcomes. No active probing occurs — all data is instantaneous from cached state. When tracing is disabled, `otlpExportHealthy` is `false` and failures are `0`.

4. **Temporary service provider during setup**: `ConfigureOpenTelemetry` builds a temporary `ServiceProvider` to access configuration during SDK setup. This is documented as "the standard pattern for OpenTelemetry SDK setup when config is needed" but creates duplicate service instances temporarily.

5. **Counter/Histogram creation on first use**: Counters and histograms are created lazily via `GetOrCreateCounter`/`GetOrCreateHistogram`. The unreachable null branch (when MetricsEnabled is checked upstream) throws `InvalidOperationException` as an invariant guard rather than returning null.

6. **ObservableGauge registration at construction time**: Unlike counters and histograms (push-based, created lazily on first `RecordCounter`/`RecordHistogram` call), observable gauges are pull-based — registered once via `RegisterObservableGauge<T>` at object construction time with a callback that the OpenTelemetry SDK invokes during each export/scrape cycle. Registration is idempotent (duplicate registrations for the same `component:metric` key are ignored). Gauge references are stored in `ConcurrentDictionary<string, object>` (typed as `object` because the generic type parameter `T` varies). Two overloads: simple `Func<T>` for tagless observations, and `Func<Measurement<T>>` for observations with static tags.

6. **Parent-based trace sampling**: The `TracingSamplingRatio` is applied via a `ParentBasedSampler` wrapping a `TraceIdRatioBasedSampler`. This means child spans respect their parent's sampling decision, and only root spans are subject to ratio-based sampling. This is the recommended OpenTelemetry pattern for distributed tracing.

7. **Trace filtering for health/metrics paths**: The OpenTelemetry SDK is configured to exclude `/health`, `/telemetry/health`, and `/metrics` paths from automatic ASP.NET Core tracing to reduce noise. Similarly, HttpClient instrumentation excludes requests with "health" in the path.

### Design Considerations (Requires Planning)

None currently identified.

---

## Work Tracking

| Issue | Status | Description |
|-------|--------|-------------|
| [#183](https://github.com/beyond-immersion/bannou-service/issues/183) | Open | Managed platform telemetry exporters (Datadog, Azure, AWS, Elastic) |
| [#185](https://github.com/beyond-immersion/bannou-service/issues/185) | Open | Enhanced Grafana dashboards with per-service views and SLO alerting |
| [#457](https://github.com/beyond-immersion/bannou-service/issues/457) | Open | Custom histogram bucket boundaries for Bannou latency profiles |
| [#640](https://github.com/beyond-immersion/bannou-service/issues/640) | Open | Wire up mesh circuit breaker state ObservableGauge metric |
