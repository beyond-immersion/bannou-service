# Generated Telemetry Metrics Reference

> **Source**: `schemas/telemetry-metrics.yaml`
> **Do not edit manually** - regenerate with `make generate-docs`

This document lists all telemetry components and metrics used in Bannou.

## Components

| Component | Name | Service | Description |
|-----------|------|---------|-------------|
| `analytics` | `bannou.analytics` | Analytics | Analytics event processing and metric emission |
| `mesh` | `bannou.mesh` | Mesh | Service mesh operations (lib-mesh) |
| `messaging` | `bannou.messaging` | Messaging | Messaging operations (lib-messaging) |
| `state` | `bannou.state` | Telemetry | State store operations (lib-state) |
| `telemetry` | `bannou.telemetry` | Telemetry | Telemetry service operations (lib-telemetry) |

## Metrics

### Analytics event processing and metric emission

| Metric | Type | Service | Description | Tags |
|--------|------|---------|-------------|------|
| `bannou.analytics.events.processed` | counter | Analytics | Counter for raw analytics events processed per entity batch during buffer flush | `game_service_id` |
| `bannou.analytics.score.processed` | counter | Analytics | Counter for analytics score deltas processed during buffer flush | `game_service_id`, `entity_type`, `score_type` |

### Service mesh operations (lib-mesh)

| Metric | Type | Service | Description | Tags |
|--------|------|---------|-------------|------|
| `bannou.mesh.circuit_breaker_state` | gauge | Mesh | Gauge for circuit breaker state | — |
| `bannou.mesh.circuit_breaker_state_changes` | counter | Mesh | Counter for circuit breaker state changes | `service`, `new_state` |
| `bannou.mesh.duration_seconds` | histogram | Mesh | Histogram for mesh invocation durations | `target_service`, `status` |
| `bannou.mesh.invocations` | counter | Mesh | Counter for mesh invocations | `target_service`, `status` |
| `bannou.mesh.raw_invocations` | counter | Mesh | Counter for raw mesh invocations (no circuit breaker participation) | `target_service`, `status` |
| `bannou.mesh.retries` | counter | Mesh | Counter for mesh retries | `target_service` |

### Messaging operations (lib-messaging)

| Metric | Type | Service | Description | Tags |
|--------|------|---------|-------------|------|
| `bannou.messaging.channel_pool_active` | gauge | Messaging | Gauge for active channels in the channel pool | — |
| `bannou.messaging.channel_pool_available` | gauge | Messaging | Gauge for available (pooled) channels in the channel pool | — |
| `bannou.messaging.consume_duration_seconds` | histogram | Messaging | Histogram for message consume durations | `topic` |
| `bannou.messaging.consumed` | counter | Messaging | Counter for consumed messages | `topic` |
| `bannou.messaging.publish_duration_seconds` | histogram | Messaging | Histogram for message publish durations | `topic` |
| `bannou.messaging.published` | counter | Messaging | Counter for published messages | `topic`, `status` |
| `bannou.messaging.retry_attempts` | counter | Messaging | Counter for retry buffer processing outcomes (tagged by status) | `status` |
| `bannou.messaging.retry_buffer_depth` | gauge | Messaging | Gauge for retry buffer depth (current number of messages awaiting retry) | — |
| `bannou.messaging.retry_buffer_fill_ratio` | gauge | Messaging | Gauge for retry buffer fill ratio (0.0-1.0 relative to max size) | — |

### State store operations (lib-state)

| Metric | Type | Service | Description | Tags |
|--------|------|---------|-------------|------|
| `bannou.state.duration_seconds` | histogram | Telemetry | Histogram for state store operation durations | `operation`, `store_name`, `backend` |
| `bannou.state.operations` | counter | Telemetry | Counter for state store operations | `operation`, `store_name`, `backend` |

**Total**: 19 metrics (10 counters, 4 histograms, 5 gauges) across 5 components

## Generated Code

Telemetry metric definitions are generated to `bannou-service/Generated/TelemetryMetrics.cs`,
providing:

- **Component constants**: `TelemetryComponents.State`, `TelemetryComponents.Analytics`, etc.
- **Metric constants**: `TelemetryMetrics.StateOperations`, `TelemetryMetrics.AnalyticsScoreProcessed`, etc.
- **Metadata**: `TelemetryMetrics.Metadata` dictionary for structural validation

---

*This file is auto-generated. See [TENETS.md](../reference/TENETS.md) for architectural context.*
