# Generated Infrastructure (L0) Service Details

> **Source**: `docs/plugins/*.md`
> **Do not edit manually** - regenerate with `make generate-docs`

Services in the **Infrastructure (L0)** layer.

## Mesh {#mesh}

**Version**: 1.0.0 | **Schema**: `schemas/mesh-api.yaml` | **Endpoints**: 8 | **Deep Dive**: [docs/plugins/MESH.md](plugins/MESH.md)

Native service mesh (L0 Infrastructure) providing direct in-process service-to-service calls with YARP-based HTTP routing and Redis-backed service discovery. Provides endpoint registration with TTL-based health tracking, configurable load balancing, a distributed per-appId circuit breaker, and retry logic with exponential backoff. Includes proactive health checking with automatic deregistration and event-driven auto-registration from Orchestrator heartbeats for zero-configuration discovery.

## Messaging {#messaging}

**Version**: 1.0.0 | **Schema**: `schemas/messaging-api.yaml` | **Endpoints**: 4 | **Deep Dive**: [docs/plugins/MESSAGING.md](plugins/MESSAGING.md)

The Messaging service (L0 Infrastructure) is the native RabbitMQ pub/sub infrastructure for Bannou. Operates in a dual role: as the `IMessageBus`/`IMessageSubscriber` infrastructure library used by all services for event publishing and subscription, and as an HTTP API providing dynamic subscription management with HTTP callback delivery. Supports in-memory mode for testing, direct RabbitMQ with channel pooling, and aggressive retry buffering with crash-fast philosophy for unrecoverable failures.

## State {#state}

**Version**: 1.0.0 | **Schema**: `schemas/state-api.yaml` | **Endpoints**: 9 | **Deep Dive**: [docs/plugins/STATE.md](plugins/STATE.md)

The State service (L0 Infrastructure) provides all Bannou services with unified access to Redis and MySQL backends through a repository-pattern API. Operates in a dual role: as the `IStateStoreFactory` infrastructure library used by every service for state persistence, and as an HTTP API for debugging and administration. Supports three backends (Redis for ephemeral/session data, MySQL for durable/queryable data, InMemory for testing) with optimistic concurrency via ETags, TTL support, and specialized interfaces for cache operations, LINQ queries, JSON path queries, and full-text search. See the Interface Hierarchy section for the full interface tree and backend support matrix.

## Telemetry {#telemetry}

**Version**: 1.0.0 | **Schema**: `schemas/telemetry-api.yaml` | **Endpoints**: 2 | **Deep Dive**: [docs/plugins/TELEMETRY.md](plugins/TELEMETRY.md)

The Telemetry service (L0 Infrastructure, optional) provides unified observability infrastructure for Bannou using OpenTelemetry standards. Operates in a dual role: as the `ITelemetryProvider` interface that lib-state, lib-messaging, and lib-mesh use for instrumentation, and as an HTTP API providing health and status endpoints. Unique among Bannou services: uses no state stores and publishes no events. When disabled, other L0 services receive a `NullTelemetryProvider` (all methods are no-ops).

## Summary

- **Services in layer**: 4
- **Endpoints in layer**: 23

---

*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for architectural context.*
