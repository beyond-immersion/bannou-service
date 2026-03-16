# Generated Specifications Catalog

> **Source**: `docs/reference/specifications/*.md`
> **Do not edit manually** - regenerate with `make generate-docs`

Extension attribute specifications defining schema syntax, code generation, and runtime behavior.

## x-client-event {#x-client-event}

**Version**: 1.0 | **Status**: Implemented | **Last Updated**: 2026-03-16 | **Schema Scope**: `*-client-events.yaml` | [Full Specification](../reference/specifications/X-CLIENT-EVENT.md)

Marks a schema as a server-to-client WebSocket push event delivered through per-session RabbitMQ queues managed by the Connect service. Events are published via IClientEventPublisher and consumed by multiple code generators for typed subscription APIs across .NET, TypeScript, and Unreal Engine SDKs. The optional x-internal companion flag excludes infrastructure events from game client SDK exposure.

## x-compression-callback / x-archive-type {#x-compression-callback}

**Version**: 1.0 | **Status**: Implemented | **Last Updated**: 2026-03-16 | **Schema Scope**: `*-api.yaml` | [Full Specification](../reference/specifications/X-COMPRESSION-CALLBACK.md)

Declares compression callbacks for hierarchical resource archival via lib-resource, and archive type markers for compile-time ABML snapshot path validation. Services register callbacks with priority ordering to contribute compressed data during resource archival. Archive types generate IResourceTemplate implementations that validate ABML variable access paths at compile time. Use when a service owns data that should be included in resource archives or when compressed response schemas need compile-time path validation for behavior expressions.

## x-constraint-group {#x-constraint-group}

**Version**: 0.1 | **Status**: Proposed | **Last Updated**: 2026-03-16 | **Schema Scope**: `*-configuration.yaml` (both `x-service-configuration` and `x-helper-configurations`) | [Full Specification](../reference/specifications/X-CONSTRAINT-GROUP.md)

Declares cross-property validation constraints for configuration schemas, enabling groups of properties to be validated collectively at startup. Solves the gap where individual property validation keywords (minimum, maximum, pattern, etc.) cannot express relationships between properties, such as weights that must sum to 1.0 or mutually exclusive provider selections. Use when configuration properties have collective invariants that cannot be expressed per-property.

## x-controller-only / x-manual-implementation {#x-controller-only}

**Version**: 1.0 | **Status**: Implemented | **Last Updated**: 2026-03-16 | **Schema Scope**: `*-api.yaml` | [Full Specification](../reference/specifications/X-CONTROLLER-ONLY.md)

Marks individual API endpoints as requiring manual controller implementation, excluding them from the generated service interface and implementation stub. Two variants exist: x-controller-only generates an abstract method signature for override in a concrete controller subclass, while x-manual-implementation generates nothing but a comment placeholder, allowing fully custom routes and parameters in a partial class. These flags are not interchangeable as they produce different class structures.

## x-event-publications {#x-event-publications}

**Version**: 1.0 | **Status**: Implemented | **Last Updated**: 2026-03-16 | **Schema Scope**: `*-service-events.yaml` | [Full Specification](../reference/specifications/X-EVENT-PUBLICATIONS.md)

Declares all events a service publishes, serving as the authoritative event catalog for that service. Generates typed topic constants and Publish*Async extension methods on IMessageBus, replacing error-prone inline topic strings with compile-time-safe typed publishers. Supports parameterized topics for dynamic routing keys with typed parameter overloads.

## x-event-subscriptions {#x-event-subscriptions}

**Version**: 1.0 | **Status**: Implemented | **Last Updated**: 2026-03-16 | **Schema Scope**: `*-service-events.yaml` | [Full Specification](../reference/specifications/X-EVENT-SUBSCRIPTIONS.md)

Declares events a service consumes from other services and generates subscription handler scaffolding in the service's event consumer file. The generator resolves event type names across all event schemas at generation time, enabling cross-service event consumption without inline model copies or schema cross-references. Use when a service needs to react to events published by other services.

## x-event-template {#x-event-template}

**Version**: 1.0 | **Status**: Implemented | **Last Updated**: 2026-03-16 | **Schema Scope**: `*-service-events.yaml` | [Full Specification](../reference/specifications/X-EVENT-TEMPLATE.md)

Declares that a service event should have an auto-generated template for use with emit_event ABML actions, enabling behavior documents to publish typed events. The generator produces EventTemplate instances with pre-built payload templates that map event properties to template substitution placeholders. Use when NPC behaviors or god-actors need to emit service events from ABML documents.

## x-from-authorization {#x-from-authorization}

**Version**: 1.0 | **Status**: Implemented | **Last Updated**: 2026-03-16 | **Schema Scope**: `*-api.yaml` | [Full Specification](../reference/specifications/X-FROM-AUTHORIZATION.md)

Marks a parameter as extracted from the HTTP Authorization header rather than the request body, used exclusively by auth-related endpoints where the JWT token serves as both credential and request parameter. The code generator strips these parameters from service-to-service clients since inter-service calls use a different authentication mechanism. Use when an endpoint needs to consume the bearer token as input data, not just as an authentication credential.

## x-lifecycle {#x-lifecycle}

**Version**: 1.0 | **Status**: Implemented | **Last Updated**: 2026-03-16 | **Schema Scope**: `*-service-events.yaml` | [Full Specification](../reference/specifications/X-LIFECYCLE.md)

Generates CRUD lifecycle events (created, updated, deleted) automatically from entity model definitions in event schemas. Eliminates manual event authoring for standard entity operations by defining the model once and generating three typed events with full entity data, changedFields tracking, and optional deprecation field injection. Supports topic namespacing, sensitive field exclusion, Puppetmaster watch integration, and batch event generation.

## x-permissions {#x-permissions}

**Version**: 1.0 | **Status**: Implemented | **Last Updated**: 2026-03-16 | **Schema Scope**: `*-api.yaml` | [Full Specification](../reference/specifications/X-PERMISSIONS.md)

Declares role and state requirements for WebSocket client access on API endpoints. The Permission service compiles per-session capability manifests from registered permission matrices. Every API endpoint must declare x-permissions; the value controls whether the endpoint appears in WebSocket capability manifests and which roles can access it. Use on every operation in a service API schema to define its access level.

## x-references / x-resource-lifecycle {#x-references}

**Version**: 1.0 | **Status**: Implemented | **Last Updated**: 2026-03-16 | **Schema Scope**: `*-api.yaml` | [Full Specification](../reference/specifications/X-REFERENCES.md)

Declares resource reference dependencies between services for lifecycle management via lib-resource. Consumer services declare x-references with explicit delete policies (cascade, restrict, detach) and cleanup or migration callbacks. Foundational services declare x-resource-lifecycle with grace periods and cleanup policies. Together they enable coordinated cross-service resource cleanup without event-based coupling, replacing the forbidden pattern of subscribing to lifecycle events for dependent data destruction.

## x-resource-mapping {#x-resource-mapping}

**Version**: 1.0 | **Status**: Implemented | **Last Updated**: 2026-03-16 | **Schema Scope**: `*-service-events.yaml` | [Full Specification](../reference/specifications/X-RESOURCE-MAPPING.md)

Declares how a service event relates to a watchable resource for Puppetmaster's watch subscription system. Maps events to resource types and ID fields so watchers can filter event streams by the resources they care about. For lifecycle events, use resource_mapping in x-lifecycle instead of adding x-resource-mapping directly; the lifecycle generator adds x-resource-mapping to generated events automatically.

## x-sdk-type {#x-sdk-type}

**Version**: 1.0 | **Status**: Implemented | **Last Updated**: 2026-03-16 | **Schema Scope**: `*-api.yaml` | [Full Specification](../reference/specifications/X-SDK-TYPE.md)

Maps an OpenAPI schema type to an existing C# type from the Core SDK, preventing NSwag from generating a duplicate class and instead referencing the SDK type directly in all generated code. Restricted to BeyondImmersion.Bannou.Core types only; domain-specific SDK types must use explicit mapping at the plugin boundary.

## x-service-configuration / x-helper-configurations {#x-service-configuration}

**Version**: 1.0 | **Status**: Implemented | **Last Updated**: 2026-03-16 | **Schema Scope**: `*-configuration.yaml` | [Full Specification](../reference/specifications/X-SERVICE-CONFIGURATION.md)

Defines typed configuration properties for services, generating strongly-typed configuration classes with environment variable binding, startup validation, and schema-defined defaults. Supports both main service configuration and helper sub-service configurations within the same schema file, each with their own environment variable prefix and generated class. Use when a service or its helper sub-services need configurable properties bound from environment variables with compile-time type safety and fail-fast startup validation.

## x-service-layer {#x-service-layer}

**Version**: 1.0 | **Status**: Implemented | **Last Updated**: 2026-03-16 | **Schema Scope**: `*-api.yaml` | [Full Specification](../reference/specifications/X-SERVICE-LAYER.md)

Declares a service's position in the six-layer hierarchy, controlling plugin load order and enabling safe cross-layer constructor injection. Services at lower layers load first, ensuring dependencies are available before dependents initialize. Defaults to GameFeatures if omitted.

## Summary

- **Specifications in catalog**: 15

---

*This file is auto-generated. See [TENETS.md](../reference/TENETS.md) for architectural context.*
