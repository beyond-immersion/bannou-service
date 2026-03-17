# Bannou Schema Rules

> ⛔ **FROZEN DOCUMENT** — Defines authoritative schema rules enforced across the codebase. AI agents MUST NOT add, remove, modify, or reinterpret any content without explicit user instruction. If you believe something is incorrect, report the concern and wait — do not "fix" it. See CLAUDE.md § "Reference Documents Are Frozen."

> **MANDATORY**: AI agents and developers MUST review this document before creating or modifying ANY OpenAPI schema file. This rule is inviolable - see TENETS.md Tenet 1.

This is the authoritative reference for all schema authoring in Bannou. It covers schema file types, generation pipeline, extension attributes, NRT compliance, validation keywords, type references, and common pitfalls.

---

## Table of Contents

1. [Schema File Types](#schema-file-types)
2. [Generation Pipeline](#generation-pipeline)
3. [Extension Attributes (x-*)](#extension-attributes-x-)
4. [NRT (Nullable Reference Types) Rules](#nrt-nullable-reference-types-rules)
5. [Schema Reference Hierarchy ($ref)](#schema-reference-hierarchy-ref)
6. [Configuration Validation Keywords](#configuration-validation-keywords)
7. [Configuration Schema Rules](#configuration-schema-rules)
8. [API Schema Rules](#api-schema-rules)
9. [Event Schema Rules](#event-schema-rules)
10. [Mandatory Type Reuse via $ref](#mandatory-type-reuse-via-ref-critical)
11. [NRT Quick Reference](#nrt-quick-reference)
12. [Validation Checklist](#validation-checklist)

---

## Schema File Types

Each service can have up to 4 schema files in `/schemas/`:

| File Pattern | Purpose | Generated Output |
|--------------|---------|------------------|
| `{service}-api.yaml` | API endpoints, request/response models | Controllers, models, clients, interfaces |
| `{service}-service-events.yaml` | Service-to-service pub/sub events | Event models in `bannou-service/Generated/Events/` |
| `{service}-configuration.yaml` | Service configuration properties | `{Service}ServiceConfiguration.cs` |
| `{service}-client-events.yaml` | Server→client WebSocket push events | Client event models in plugin `Generated/` |

**Common schemas** (shared across services):
- `common-api.yaml` - System-wide types (EntityType, etc.)
- `common-events.yaml` - Base event schemas (BaseServiceEvent)
- `common-client-events.yaml` - Base client event schemas (BaseClientEvent)
- `state-stores.yaml` - State store definitions → `StateStoreDefinitions.cs`
- `variable-providers.yaml` - Variable provider definitions → `VariableProviderDefinitions.cs`

---

## Generation Pipeline

Run `make generate` or `scripts/generate-all-services.sh` to execute the full pipeline:

| Step | Source | Generated Output |
|------|--------|------------------|
| 1. State Stores | `state-stores.yaml` | `lib-state/Generated/StateStoreDefinitions.cs` |
| 2. Variable Providers | `variable-providers.yaml` | `bannou-service/Generated/VariableProviderDefinitions.cs` |
| 3. Lifecycle Events | `x-lifecycle` in events.yaml | `schemas/Generated/{service}-service-lifecycle-events.yaml` |
| 4. Common Events | `common-events.yaml` | `bannou-service/Generated/Events/CommonEventsModels.cs` |
| 5. Service Events | `{service}-service-events.yaml` | `bannou-service/Generated/Events/{Service}EventsModels.cs` |
| 6. Client Events | `common-client-events.yaml` + `{service}-client-events.yaml` | Common: `bannou-service/Generated/CommonClientEventsModels.cs`; Service: `lib-{service}/Generated/{Service}ClientEventsModels.cs` |
| 7. Meta Schemas | `{service}-api.yaml` | `schemas/Generated/{service}-api-meta.yaml` |
| 8. Service API | `{service}-api.yaml` | Controllers, models, clients, interfaces |
| 9. Configuration | `{service}-configuration.yaml` | `{Service}ServiceConfiguration.cs` + helper configs |
| 10. Permissions | `x-permissions` in api.yaml | `{Service}PermissionRegistration.cs` |
| 11. Event Subscriptions | `x-event-subscriptions` | `{Service}ServiceEvents.cs` (one-time template) |

**Order matters**: State stores and events must be generated before service APIs.

### What Is Safe to Edit

| File Pattern | Safe to Edit? | Notes |
|--------------|---------------|-------|
| `lib-{service}/{Service}Service.cs` | Yes | Main business logic |
| `lib-{service}/{Service}ServiceModels.cs` | Yes | Internal data models (storage, cache, DTOs) |
| `lib-{service}/{Service}ServiceEvents.cs` | Yes | Generated once, then manual |
| `lib-{service}/Services/*.cs` | Yes | Helper services |
| `lib-{service}/Generated/*.cs` | **Never** | Regenerated on `make generate` |
| `bannou-service/Generated/*.cs` | **Never** | All generated directories |
| `schemas/*.yaml` | Yes | Edit schemas, regenerate code |
| `schemas/Generated/*.yaml` | **Never** | Generated lifecycle events + meta schemas |

### New Service Bootstrap

Running `generate-service.sh {service}` bootstraps an entire new plugin from scratch. This command:

1. **Calls `generate-project.sh`** to create the plugin project:
   - `plugins/lib-{service}/` directory
   - `plugins/lib-{service}/lib-{service}.csproj` (with `ServiceLib.targets` import)
   - `plugins/lib-{service}/AssemblyInfo.cs` (`ApiController`, `InternalsVisibleTo` for tests)
   - Adds `lib-{service}` to `bannou-service.sln` via `dotnet sln add`

2. **Generates all code** into `plugins/lib-{service}/Generated/`:
   - `I{Service}Service.cs` - service interface
   - `{Service}Controller.cs` - HTTP routing
   - `{Service}Controller.Meta.cs` - runtime schema introspection
   - `{Service}ServiceConfiguration.cs` - typed config class
   - `{Service}PermissionRegistration.cs` - permission matrix

3. **Generates shared code** into `bannou-service/Generated/`:
   - `Models/{Service}Models.cs` - request/response models (inspect with `make print-models PLUGIN="service"`)
   - `Clients/{Service}Client.cs` - client for other services to call this service
   - `Events/{Service}EventsModels.cs` - event models
   - Updated `StateStoreDefinitions.cs` with new store constants

   > **Model Inspection**: Always use `make print-models PLUGIN="service"` to inspect model shapes. If it fails or generation hasn't been run, generate first — never guess at model definitions.

4. **Creates template files** (one-time, never overwritten) into `plugins/lib-{service}/`:
   - `{Service}Service.cs` - business logic with TODO stubs for each endpoint
   - `{Service}ServiceModels.cs` - internal storage models placeholder
   - `{Service}ServicePlugin.cs` - plugin registration skeleton
   - `{Service}ServiceEvents.cs` - event consumer registration and handler stubs (from `x-event-subscriptions`)

5. **Calls `generate-tests.sh`** to create the test project:
   - `plugins/lib-{service}.tests/` directory, `.csproj`, `AssemblyInfo.cs`, `GlobalUsings.cs`
   - `{Service}ServiceTests.cs` template with basic constructor validation test
   - Adds `lib-{service}.tests` to `bannou-service.sln` via `dotnet sln add`

**One-time template**: `{Service}ServiceEvents.cs` is generated once by `generate-event-subscriptions.sh` into the plugin root (not `Generated/`) when the service has `x-event-subscriptions`. It is never overwritten if it already exists. After initial generation, this file is manually maintained as a partial class of `{Service}Service` containing `RegisterEventConsumers` and handler methods.

**Prerequisites**: Before running `generate-service.sh`, create the schema files:
- `schemas/{service}-api.yaml` (required)
- `schemas/{service}-service-events.yaml` (required for event publishing/subscription)
- `schemas/{service}-configuration.yaml` (required for config properties)
- Update `schemas/state-stores.yaml` with service-specific stores

---

## Extension Attributes (x-*)

Bannou uses custom OpenAPI extensions to drive code generation.

### x-permissions (Required on ALL API Endpoints)

Declares role and state requirements for WebSocket client access. **All endpoints MUST have x-permissions.**

> **Full specification**: [X-PERMISSIONS.md](specifications/X-PERMISSIONS.md) — complete syntax, role hierarchy, state requirements, generated output, and examples.

| Value | Meaning | WebSocket Access |
|-------|---------|------------------|
| `x-permissions: [{role: admin}]` | Admin-only | Admin WebSocket sessions only |
| `x-permissions: [{role: user}]` | Authenticated | Any authenticated session |
| `x-permissions: [{role: anonymous}]` | Pre-auth public | All connected clients (rare) |
| `x-permissions: []` | Service-to-service only | **No WebSocket access** |

**Role hierarchy**: `anonymous` → `user` → `developer` → `admin` (higher includes lower). State requirements can be combined with roles. See [ENDPOINT-PERMISSION-GUIDELINES.md](ENDPOINT-PERMISSION-GUIDELINES.md) for choosing the right level.

### x-lifecycle (Lifecycle Event Generation)

Defined in `{service}-service-events.yaml`, generates CRUD lifecycle events automatically. **NEVER manually define `*CreatedEvent`, `*UpdatedEvent`, `*DeletedEvent`** — use `x-lifecycle` instead.

> **Full specification**: [X-LIFECYCLE.md](specifications/X-LIFECYCLE.md) — complete syntax, topic derivation, deprecation, instanceEntity, resource_mapping, batch mode, generated output, and structural tests.

```yaml
x-lifecycle:
  topic_prefix: myservice
  TemplateEntity:
    deprecation: true
    instanceEntity: InstanceEntity
    model:
      templateId: { type: string, format: uuid, primary: true, required: true }
      name: { type: string, required: true }
    sensitive: [secretKey]
    resource_mapping:
      resource_type: entity
```

**Generated output**: `EntityNameCreatedEvent`, `EntityNameUpdatedEvent`, `EntityNameDeletedEvent` — all carry full entity data. Auto-injected fields: `createdAt`, `updatedAt`. With `deprecation: true`: `isDeprecated`, `deprecatedAt`, `deprecationReason`.

### x-resource-mapping (Resource Event Mapping)

Defined on **event schema definitions** in `{service}-service-events.yaml`, declares how the event relates to a watchable resource for Puppetmaster's watch system.

> **Full specification**: [X-RESOURCE-MAPPING.md](specifications/X-RESOURCE-MAPPING.md) — complete syntax, field reference, lifecycle vs manual usage, generated output, and examples.

**For lifecycle events**: Use `resource_mapping` in `x-lifecycle` — the generator adds `x-resource-mapping` automatically. **For manually-defined events**: Add `x-resource-mapping` directly with `resource_type`, `resource_id_field`, `source_type`, and optional `is_deletion`.

**Generated output**: `bannou-service/Generated/ResourceEventMappings.cs` — static class with `IReadOnlyList<ResourceEventMappingEntry>` used by Puppetmaster's watch system.

### x-event-subscriptions (Event Handler Generation)

Defined in `{service}-service-events.yaml` under `info:`, declares events this service consumes and generates subscription handler scaffolding.

> **Full specification**: [X-EVENT-SUBSCRIPTIONS.md](specifications/X-EVENT-SUBSCRIPTIONS.md) — complete syntax, cross-service event resolution, generated output, and examples.

Fields: `topic` (RabbitMQ routing key), `event` (class name resolved across all event schemas), `handler` (method name without `Async`). The `event` field is a **class name**, not a `$ref` — the generator resolves it across ALL `*-service-events.yaml` schemas.

**Generated output**: `{Service}ServiceEvents.cs` (one-time template; never overwritten if exists).

### x-event-publications (Event Publication Registry)

Defined in `{service}-service-events.yaml` under `info:`, declares all events this service publishes. Serves as the authoritative registry of what a service emits. **Both lifecycle and custom events should be listed.**

> **Full specification**: [X-EVENT-PUBLICATIONS.md](specifications/X-EVENT-PUBLICATIONS.md) — complete syntax, parameterized topics, generated output, structural tests, and examples.

Fields: `topic` (routing key), `event` (class name), `description`, optional `topic-params` (for dynamic routing keys with `{placeholder}` syntax).

**Generated output**: `{Service}PublishedTopics.cs` (const strings) and `{Service}EventPublisher.cs` (typed `Publish*Async` extension methods). Services MUST use generated methods or constants instead of inline topic strings.

### x-service-layer (Service Hierarchy Layer)

Defined at the **root level** of `{service}-api.yaml`, declares the service's position in the six-layer hierarchy. Controls plugin load order and cross-layer constructor injection.

> **Full specification**: [X-SERVICE-LAYER.md](specifications/X-SERVICE-LAYER.md) — complete syntax, valid values, numeric equivalents, default behavior, and generated output.

Valid values: `Infrastructure` (L0), `AppFoundation` (L1), `GameFoundation` (L2), `AppFeatures` (L3), `GameFeatures` (L4), `Extensions` (L5). Default: `GameFeatures`. See [SERVICE-HIERARCHY.md](SERVICE-HIERARCHY.md) for dependency rules.

### x-service-configuration and x-helper-configurations (Configuration Properties)

Defined in `{service}-configuration.yaml`. Generates typed configuration classes with environment variable binding and startup validation.

> **Full specification**: [X-SERVICE-CONFIGURATION.md](specifications/X-SERVICE-CONFIGURATION.md) — complete syntax for both main and helper configurations, property rules, validation keywords, generated output, and examples.

`x-service-configuration` generates `{Service}ServiceConfiguration.cs`. `x-helper-configurations` generates additional `{Service}{Helper}Configuration.cs` per helper entry with `{SERVICE}_{HELPER}_` env var prefix. Both support `x-constraint-groups` (see below). Property rules: see [Configuration Schema Rules](#configuration-schema-rules).

### x-constraint-groups (Cross-Property Configuration Validation)

Defined inside `x-service-configuration` or `x-helper-configurations` blocks in `{service}-configuration.yaml`. Declares collective validation constraints across groups of configuration properties, validated at startup.

> **Full specification**: [X-CONSTRAINT-GROUP.md](specifications/X-CONSTRAINT-GROUP.md) — complete schema syntax, all constraint types, generated output, runtime behavior, structural tests, and worked examples.

**Group definitions** are siblings of `properties` inside a configuration block. **Properties** reference their group via `constraint-group: {name}`.

```yaml
x-service-configuration:
  x-constraint-groups:
    stage-weights:
      constraint: sum-equals
      value: 1.0
      description: Stage weights must collectively equal 1.0
    auth-provider:
      constraint: exactly-one
      description: Exactly one authentication provider must be configured
  properties:
    GatheringWeight:
      type: number
      nullable: true
      constraint-group: stage-weights
      env: CRAFT_GATHERING_WEIGHT
      default: 0.25
      description: Weight for gathering stage
    MysqlConnectionString:
      type: string
      nullable: true
      constraint-group: auth-provider
      env: CRAFT_MYSQL_CONNECTION_STRING
      description: MySQL connection string (mutually exclusive with other providers)
```

**Constraint types**:

| Constraint | Value Required | Description |
|---|---|---|
| `exactly-one` | No | Exactly one property must be non-null |
| `at-most-one` | No | Zero or one property may be non-null |
| `all-or-none` | No | All properties set or all null |
| `sum-equals` | Yes | Sum must equal `value` (within optional `tolerance`, default `0.0001`) |
| `sum-minimum` | Yes | Sum must be >= `value` |
| `sum-maximum` | Yes | Sum must be <= `value` |

**Scoping**: Groups are scoped to their configuration block. The same group name in different config blocks (main vs helper, or different helpers) does not conflict. A property may belong to at most one group.

**Generated output**: `[ConfigConstraintGroupDefinition]` class-level attributes and `[ConfigConstraintGroup]` property-level attributes. Validated at startup by `IServiceConfiguration.ValidateConstraintGroups()` — fail-fast with `InvalidOperationException`.

### x-references and x-resource-lifecycle (Resource Reference Tracking)

`x-references` (consumer API schemas) declares resource dependencies with explicit delete policies. `x-resource-lifecycle` (foundational API schemas) declares grace periods and cleanup policies.

> **Full specification**: [X-REFERENCES.md](specifications/X-REFERENCES.md) — complete syntax for both attributes, delete policies (cascade/restrict/detach), callback sections, decision tree, generated output, structural tests, and examples.

| Policy | Meaning | Required Callback |
|--------|---------|-------------------|
| `cascade` | Delete dependent data | `cleanup:` with `{{resourceId}}` |
| `restrict` | Block deletion; require migration | `migrate:` with `{{sourceResourceId}}`, `{{targetResourceId}}` |
| `detach` | Nullify the reference | `cleanup:` with `{{resourceId}}` |

`onDelete` is **REQUIRED** on every entry — implicit defaulting is forbidden. Policy-callback exclusivity is enforced by structural tests.

**Generated output**: `{Service}ReferenceTracking.cs` with Register/Unregister helpers and callback registration methods.

### x-compression-callback and x-archive-type (Resource Archival)

`x-compression-callback` declares compression callbacks for hierarchical resource archival via lib-resource. `x-archive-type` marks response schemas as archive types for compile-time ABML path validation.

> **Full specification**: [X-COMPRESSION-CALLBACK.md](specifications/X-COMPRESSION-CALLBACK.md) — complete syntax for both attributes, priority guidelines, generated output, template generation, and examples.

**Priority guidelines**: 0 = base entity data, 10-30 = extension data, 50-100 = optional/derived data. Eligibility: entity identity and narrative data only — not game-mechanical state (see [FOUNDATION.md T29](tenets/FOUNDATION.md)).

**Generated output**: `{Service}CompressionCallbacks.cs` (callback registration) and `{SourceType}Template.cs` (ABML path validation).

### x-event-template (Event Template Generation)

Defined on **individual event schema definitions** in `{service}-service-events.yaml`, declares that the event should have an auto-generated template for use with `emit_event:` ABML actions.

> **Full specification**: [X-EVENT-TEMPLATE.md](specifications/X-EVENT-TEMPLATE.md) — complete syntax, payload template generation rules, generated output, and examples.

Fields: `name` (template name for ABML `emit_event`), `topic` (event routing key). Generator produces `EventTemplate` instances with type-aware payload templates (quoted strings, unquoted numerics/arrays).

**Generated output**: `lib-{service}/Generated/{Service}EventTemplates.cs` — static class with `RegisterAll(IEventTemplateRegistry)` method.

### x-controller-only and x-manual-implementation (Manual Controller Endpoints)

Both flags mark individual operations in `{service}-api.yaml` as requiring manual controller implementation, excluding them from the generated service interface and implementation stub. **These flags are NOT interchangeable.**

> **Full specification**: [X-CONTROLLER-ONLY.md](specifications/X-CONTROLLER-ONLY.md) — complete syntax, behavioral differences, when to use which, generated output, and examples.

| Aspect | `x-controller-only` | `x-manual-implementation` |
|--------|---------------------|---------------------------|
| Controller class | `abstract class {Service}ControllerBase` | `partial class {Service}Controller` |
| Controller method | `public abstract` with NSwag signature | Comment only (no method) |
| Manual class pattern | Inherits from Base, uses `override` | Partial class, defines own routes/params |

### x-from-authorization (Authorization Header Parameter)

Defined on **parameters** in `{service}-api.yaml`, marks a parameter extracted from the Authorization header. Used exclusively by auth-related endpoints where the JWT is both credential and request parameter.

> **Full specification**: [X-FROM-AUTHORIZATION.md](specifications/X-FROM-AUTHORIZATION.md) — complete syntax, generated behavior, and examples.

**Generated behavior**: `generate-client.sh` strips these parameters from service clients (service-to-service calls use a different auth mechanism).

### x-client-event (Server-to-Client Push Event)

Defined on **schema objects** in `{service}-client-events.yaml`, marks a schema as a server-to-client WebSocket push event published via `IClientEventPublisher` (not `IMessageBus`).

> **Full specification**: [X-CLIENT-EVENT.md](specifications/X-CLIENT-EVENT.md) — complete syntax, generator consumption, x-internal companion flag, generated output, and examples.

Used by 4+ generators for typed subscription APIs across .NET, TypeScript, and Unreal Engine SDKs. Optional `x-internal: true` excludes infrastructure events from game client SDK exposure.

### x-sdk-type (External SDK Type Mapping)

Defined on **schema objects** in `{service}-api.yaml`, maps an OpenAPI schema type to an existing C# type from the Core SDK (`BeyondImmersion.Bannou.Core`), preventing duplicate class generation.

> **Full specification**: [X-SDK-TYPE.md](specifications/X-SDK-TYPE.md) — complete syntax, restriction to Core SDK types, generated behavior, and examples.

**Restriction: Core SDK types only.** Domain-specific SDKs must define their own types and map at the plugin boundary. Generated behavior: `extract-sdk-types.py` produces exclusion lists consumed by `generate-models.sh` and `generate-config.sh`.

---

## NRT (Nullable Reference Types) Rules

All Bannou projects have `<Nullable>enable</Nullable>`. NSwag generates with `/generateNullableReferenceTypes:true`. **Schema definitions DIRECTLY control C# nullability.**

Incorrect schemas cause `string = default!;` for optional properties (hides null at runtime), missing `[Required]` attributes, and NRT violations that surface only at runtime.

These rules apply to ALL schema types (API, events, configuration):

### Rule 1: Required Properties → `required` Array

Properties that MUST be present go in the `required` array. For requests, this means the caller must provide them. For responses/events, this means the server always sets them.

```yaml
CreateAccountRequest:
  type: object
  required: [email, password]  # Caller MUST provide
  properties:
    email:
      type: string
      description: User's email address
```

**Generates**: `[Required(AllowEmptyStrings = true)] [JsonRequired] public string Email { get; set; } = default!;`

### Rule 2: Optional Reference Types → `nullable: true`

Properties that MAY be absent (strings, objects, arrays) MUST have `nullable: true`.

```yaml
displayName:
  type: string
  nullable: true
  description: Optional display name
```

**Generates**: `public string? DisplayName { get; set; } = default!;`

**Note**: NSwag adds `= default!;` to reference type properties in API models (both required and nullable). This is a NSwag behavior — the `= default!` initializer suppresses NRT warnings but does not affect runtime behavior. Value types (`int`, `bool`, `Guid`) do not get `= default!`. Configuration classes (generated by a different script) do NOT include `= default!` on nullable properties.

**WITHOUT `nullable: true`**, NSwag generates `string DisplayName = default!;` which **HIDES** null at runtime.

### Rule 3: Value Types with Defaults → `default: value`

Boolean/integer properties with sensible defaults just need `default: value`.

```yaml
pageSize:
  type: integer
  default: 20
  description: Number of items per page
```

**Generates**: `public int PageSize { get; set; } = 20;`

### Rule 4: Value Types → Never Nullable Unless Semantically Optional

Only mark value types `nullable: true` if the absence of a value is semantically meaningful.

---

## Schema Reference Hierarchy ($ref)

NSwag processes each schema file independently. When multiple schemas define the same type, duplicate C# classes are generated, causing compilation errors. Follow the reference rules below.

### Reference Rules

All `$ref` paths in manually authored schemas (in `schemas/`) are sibling-relative (same directory). Never use `../` prefixes in hand-written schemas. (Auto-generated schemas in `schemas/Generated/` use `../` to reference parent directory files — this is expected and correct.)

| Source Schema | Can Reference | Path Format |
|--------------|---------------|-------------|
| `*-api.yaml` | `common-api.yaml` | `common-api.yaml#/...` |
| `*-service-events.yaml` | Same service's `-api.yaml`, `common-api.yaml`, `common-events.yaml` | `{service}-api.yaml#/...` |
| `*-configuration.yaml` | Same service's `-api.yaml`, `common-api.yaml` | `{service}-api.yaml#/...` |
| `*-client-events.yaml` | Same service's `-api.yaml`, `common-client-events.yaml` | `common-client-events.yaml#/...` |

### Common Shared Files

- **`common-api.yaml`** - System-wide types like `EntityType` enum. Available to all schemas.
- **`common-events.yaml`** - Base event schemas like `BaseServiceEvent`. Used by `*-service-events.yaml` files.
- **`common-client-events.yaml`** - Base client event schemas like `BaseClientEvent`. Used by client-facing events.

### $ref Examples

```yaml
# Configuration referencing API enum (save-load-configuration.yaml)
DefaultCompressionType:
  $ref: 'save-load-api.yaml#/components/schemas/CompressionType'
  env: SAVE_LOAD_DEFAULT_COMPRESSION_TYPE
  default: Gzip
  description: Default compression algorithm

# Events referencing API type (actor-service-events.yaml)
properties:
  status:
    $ref: 'actor-api.yaml#/components/schemas/ActorStatus'
    description: Current actor status
```

---

## Configuration Validation Keywords

OpenAPI 3.0.4 validation keywords in **configuration schemas** (`*-configuration.yaml`) generate `[ConfigX]` attributes in C#, validated at service startup. API schemas (`*-api.yaml`) use standard `[Range]`, `[StringLength]`, etc. attributes via NSwag — those are NOT covered here.

| Keyword | Applies To | Schema Example | Generated Attribute |
|---------|------------|----------------|---------------------|
| `minimum` | number/integer | `minimum: 1` | `[ConfigRange(Minimum = 1)]` |
| `maximum` | number/integer | `maximum: 100` | `[ConfigRange(Maximum = 100)]` |
| `exclusiveMinimum` | number/integer | `exclusiveMinimum: true` | `[ConfigRange(..., ExclusiveMinimum = true)]` |
| `exclusiveMaximum` | number/integer | `exclusiveMaximum: true` | `[ConfigRange(..., ExclusiveMaximum = true)]` |
| `minLength` | string | `minLength: 32` | `[ConfigStringLength(MinLength = 32)]` |
| `maxLength` | string | `maxLength: 256` | `[ConfigStringLength(MaxLength = 256)]` |
| `pattern` | string (regex) | `pattern: "^https?://"` | `[ConfigPattern(@"^https?://")]` |
| `multipleOf` | number/integer | `multipleOf: 1000` | `[ConfigMultipleOf(1000)]` |

Keywords can be combined on a single property:

```yaml
TimeoutMs:
  type: integer
  minimum: 1000
  maximum: 60000
  multipleOf: 1000
  default: 5000
  description: Timeout between 1-60 seconds, in whole seconds
```

**Generates**: `[ConfigRange(Minimum = 1000, Maximum = 60000)] [ConfigMultipleOf(1000)] public int TimeoutMs { get; set; } = 5000;`

---

## Configuration Schema Rules

Configuration schemas (`*-configuration.yaml`) have unique requirements beyond the standard NRT rules.

### Rule 1: Always Include `env` Property

Every property MUST have `env` specifying the environment variable name.

### Rule 2: Use Proper Service Prefix in `env`

Pattern: `{SERVICE}_{PROPERTY}`. Use underscores, never hyphens (e.g., `SAVE_LOAD_MAX_SIZE`, not `SAVE-LOAD_MAX_SIZE`).

### Rule 3: Defaults and Optionality

Use `default: value` for properties with sensible defaults. Use `nullable: true` for truly optional properties (feature disabled when absent). Same NRT rules as API schemas apply.

### Rule 4: Never Use `type: object`

The generator falls back to `string` for `type: object`. Use `type: string` with a documented format instead.

### Rule 5: Enums via $ref to API Schema

Reference enum types from the service's API schema:

```yaml
DefaultCompression:
  $ref: 'my-service-api.yaml#/components/schemas/CompressionType'
  env: MY_SERVICE_DEFAULT_COMPRESSION
  default: Gzip
  description: Default compression algorithm
```

### Rule 6: Single-Line Descriptions Only

Descriptions MUST be single-line. Multi-line YAML blocks (`|` or `>`) produce malformed C# XML documentation comments that cause compile errors.

### Rule 7: Helper Configurations Follow Main Config Rules

Properties inside `x-helper-configurations` follow all the same rules (Rules 1-6) as `x-service-configuration`. The `env` prefix should follow the pattern `{SERVICE}_{HELPER}_{PROPERTY}` (e.g., `AUTH_TOKEN_JWT_EXPIRATION_MINUTES`). See [x-helper-configurations](#x-helper-configurations-helper-service-configuration) for the schema pattern.

### Well-Formed Configuration Property

```yaml
x-service-configuration:
  properties:
    MaxConnections:
      type: integer
      env: MY_SERVICE_MAX_CONNECTIONS
      minimum: 1
      maximum: 10000
      default: 100
      description: Maximum concurrent connections allowed
```

---

## additionalProperties: true — Schema Validation Rule

**Full rules**: See [FOUNDATION.md Tenet 29](tenets/FOUNDATION.md#tenet-29-no-metadata-bag-contracts-inviolable) for the complete rationale, acceptable/unacceptable uses, correct patterns, and enforcement rules.

**Schema requirement**: Every property with `additionalProperties: true` MUST include a description stating: "Client-only metadata. No Bannou plugin reads specific keys from this field by convention." If this description would be false, the data must be moved to the owning service's schema.

### Schema Pattern: Correct vs Forbidden

```yaml
# CORRECT: Service B (L4) defines its own binding model in its own schema
# environment-api.yaml
CreateClimateBindingRequest:
  type: object
  additionalProperties: false
  required: [locationId, biomeCode]
  properties:
    locationId:
      type: string
      format: uuid
      description: Location to bind (validated via ILocationClient)
    biomeCode:
      type: string
      description: Climate template biome code

# FORBIDDEN: Relying on metadata bag in another service's schema
# location-api.yaml
LocationResponse:
  properties:
    metadata:
      type: object
      additionalProperties: true  # "Put biomeCode in here" — NEVER
```

---

## API Schema Rules

### POST-Only Pattern (MANDATORY)

All internal service APIs use POST requests exclusively. WebSocket binary routing uses static 16-byte GUIDs per endpoint. Path parameters would break zero-copy routing.

```yaml
# CORRECT                           # WRONG
paths:                               paths:
  /account/get:                        /account/{accountId}:
    post:                                get:  # NO!
      requestBody: ...
```

**Exceptions** (browser-facing only): Website service, OAuth callbacks, WebSocket upgrade GET.

### Property Description Requirement (MANDATORY)

**ALL schema properties MUST have `description` fields.** Missing descriptions cause CS1591 compiler warnings.

### Servers URL (MANDATORY)

All API schemas MUST use `servers: [{ url: http://localhost:5012 }]`. NSwag generates controller route prefixes from this URL.

---

## Event Schema Rules

### Canonical Definitions Only

Each `{service}-service-events.yaml` MUST contain ONLY canonical definitions for events that service PUBLISHES. **No `$ref` references to other service event files** (causes duplicate types).

**This rule applies to `$ref` in schema definitions only — NOT to `x-event-subscriptions`.** The `x-event-subscriptions` mechanism references consumed event types by **class name**, not by `$ref`. The generator resolves these names across all `*-service-events.yaml` schemas at generation time (see [x-event-subscriptions](#x-event-subscriptions-event-handler-generation)). Consuming a cross-service event requires only a topic and class name entry in `x-event-subscriptions` — never an inline copy of the event model, and never a `$ref` to the producing service's event schema. Inline redefinition of consumed event models causes the exact same duplicate-type problem that `$ref` would.

### Topic Naming Convention

Event topics use **two patterns** depending on whether a service owns one entity type or multiple:

**Pattern A — Single-entity**: `{entity}.{action}` (kebab-case entity, lowercase action). Used when the service name IS the entity, or the entity stands independently (no service prefix needed for disambiguation).

```
account.created              # account service, one entity type
realm.merged                 # realm service, one entity type
personality.evolved          # character-personality service, entity stands alone
combat-preferences.updated   # character-personality service, entity stands alone
encounter.recorded           # character-encounter service, entity stands alone
game-session.player-joined   # game-session service, compound action
subscription.updated         # subscription service, one entity type
```

**Pattern C — Multi-entity namespaced**: `{service}.{entity}.{action}` (dot-separated service namespace). Used when a service owns multiple entity types, providing a namespace prefix to group related events.

```
worldstate.calendar-template.created   # worldstate service, calendar-template entity
worldstate.realm-config.deleted        # worldstate service, realm-config entity
worldstate.hour-changed                # worldstate service, boundary action
transit.connection.created             # transit service, connection entity
transit.journey.departed               # transit service, journey entity
divine.blessing.granted                # divine service, blessing entity
actor.instance.character-bound         # actor service, instance entity
contract.milestone.completed           # contract service, milestone entity
gardener.scenario.started              # gardener service, scenario entity
```

**Choosing between A and C:**
- If the service has ONE entity type (or entities that don't share the service name as prefix): use Pattern A
- If the service has MULTIPLE entity types that need a shared namespace for grouping: use Pattern C
- Infrastructure events use `bannou.` prefix (e.g., `bannou.service-heartbeat`)

**FORBIDDEN — Hybrid B (service name embedded in entity via hyphens):**

```
# WRONG: service name baked into entity with hyphen
transit-connection.created        # → use transit.connection.created
actor-template.updated            # → use actor.template.updated
chat-participant.joined           # → use chat.participant.joined
inventory-container.full          # → use inventory.container.full

# CORRECT Pattern A (no service prefix — entity stands alone)
game-session.player-joined        # game-session IS the service name, not a prefix
combat-preferences.evolved        # entity name independent of service name
encounter.memory.faded            # encounter IS the entity, memory is sub-entity
```

**The litmus test**: If a single-word service name appears as the first segment of a hyphenated entity (e.g., `transit-connection` for service `transit`), it's Pattern B and must become `{service}.{entity}.{action}` (Pattern C). If the entity name does NOT embed the publishing service's name, it's Pattern A and is correct as-is.

**Hyphenated service names are NOT Pattern B.** Several services have multi-word hyphenated names: `character-history`, `character-encounter`, `character-personality`, `realm-history`, `game-session`, `game-service`, `save-load`. The hyphen is part of the service identity — it is NOT a separator between service name and entity name. Topics for these services preserve the full hyphenated name:

```
# CORRECT: hyphenated service names preserved as-is
character-history.backstory.deleted    # service = character-history, entity = backstory
character-history.participation.recorded
character-encounter.encounter-type.created
realm-history.lore.created             # service = realm-history, entity = lore
game-session.created                   # service = game-session (Pattern A lifecycle)
game-session.player-joined             # service = game-session (Pattern A action)
game-session.action.performed          # service = game-session (Pattern C sub-entity)
game-service.created                   # service = game-service (Pattern A lifecycle)
save-load.save-slot.created            # service = save-load, entity = save-slot

# WRONG: splitting hyphenated service names into dots
character.history.backstory.deleted    # ← confuses service name with Pattern C namespace
realm.history.lore.created             # ← "realm" is a DIFFERENT service (lib-realm)
game.session.created                   # ← "game" is not a service
save.load.save-slot.created            # ← "save" is not a service
```

**How to tell the difference**: Check `plugins/lib-{name}/`. If `lib-character-history` exists as a plugin directory, then `character-history` is the service name and the hyphen is preserved. Pattern B only applies when a single-word service name (like `transit`, `actor`, `chat`) is concatenated with an entity name via hyphen.

**All parts use kebab-case** (lowercase with hyphens for multi-word segments). No underscores in topic strings.

### Client Events vs Service Events

| Type | File | Publishing |
|------|------|------------|
| Service Events | `{service}-service-events.yaml` | `IMessageBus.PublishAsync` |
| Client Events | `{service}-client-events.yaml` | `IClientEventPublisher.PublishToSessionAsync` |

**Never use `IMessageBus` for client events** - it uses the wrong RabbitMQ exchange. Use `IClientEventPublisher` instead.

### Client Event `eventName` Naming Rules

Client event `eventName` values (the `default:` on the `eventName` property) follow the **same Pattern A / Pattern C rules** as server-side event topics:

**Pattern C** (`service.entity.action`) — Use when:
1. The service has **multiple API-backed entities** (distinct endpoint groups)
2. The `eventName` describes something happening to a **specific entity**
3. That entity has its own endpoint group in the service API (e.g., `/chat/participant/kick`)

**Pattern A** (`service.compound-action`) — Use when:
1. The service is effectively single-entity (all events are implicitly about the same thing)
2. The qualifying word is **not** an API-backed entity (it's a state, process, or feature)
3. The event is a notification/alert rather than an entity state change

**The diagnostic test**: Does the word after the service dot correspond to an endpoint group in the API schema? If `/chat/message/send`, `/chat/message/delete` exist, then "message" is an entity → `chat.message.received` (Pattern C). If there's no `/auth/password/*` endpoint group, then "password" is a qualifier → `auth.password-changed` (Pattern A).

**Examples by service:**

| Service | Entity? | eventName | Pattern |
|---------|---------|-----------|---------|
| Chat | Room, Message, Participant = entities | `chat.message.received`, `chat.participant.joined`, `chat.room.updated` | C |
| Chat | Typing = not an entity (no CRUD) | `chat.typing-started` | A |
| Inventory | Item, Container = entities | `inventory.item.changed`, `inventory.container.full` | C |
| Currency | Wallet, Balance = entities | `currency.wallet.frozen`, `currency.balance.changed` | C |
| Voice | Room, Peer, Broadcast = entities | `voice.room.state`, `voice.peer.joined`, `voice.broadcast.consent-request` | C |
| Voice | Tier = not an entity | `voice.tier-upgrade` | A |
| Transit | Journey, Discovery, Connection = entities | `transit.journey.updated`, `transit.connection.status-changed` | C |
| Collection | Entry, Discovery = entities | `collection.entry.unlocked`, `collection.discovery.advanced` | C |
| Collection | Milestone = not an entity | `collection.milestone-reached` | A |
| Asset | Bundle, Metabundle = entities | `asset.bundle.creation-complete`, `asset.metabundle.creation-complete` | C |
| Asset | Primary asset actions | `asset.ready`, `asset.upload-complete` | A |
| Auth | All compound actions | `auth.password-changed`, `auth.mfa-enabled`, `auth.suspicious-login` | A |
| Game-session | Single entity (the session) | `game-session.state-changed`, `game-session.player-joined` | A |
| Matchmaking | Queue = entity | `matchmaking.queue.joined` | C |
| Matchmaking | Match = no CRUD endpoints | `matchmaking.match-found`, `matchmaking.player-accepted` | A |

**The asset service is the exemplar** — it already uses both patterns correctly: `asset.bundle.creation-complete` (Pattern C, bundle has its own endpoints) alongside `asset.ready` (Pattern A, about the primary entity).

### Custom Service Event Structure (Non-Lifecycle)

Custom service events (events NOT generated by `x-lifecycle`) MUST use `allOf` composition with `BaseServiceEvent` from `common-events.yaml`. This produces C# class inheritance (`: BaseServiceEvent`), which provides `IBannouEvent` interface implementation, `EventName` for message tap forwarding, and integration with generic event processing infrastructure.

```yaml
# In {service}-service-events.yaml — custom event (allOf with BaseServiceEvent)
ContractProposedEvent:
  allOf:
    - $ref: 'common-events.yaml#/components/schemas/BaseServiceEvent'
  type: object
  additionalProperties: false
  description: Event published when a contract is proposed to parties
  required:
    - eventName
    - eventId
    - timestamp
    - contractId
    - templateId
  properties:
    eventName:
      type: string
      default: contract.proposed
      description: 'Event type identifier: contract.proposed'
    contractId:
      type: string
      format: uuid
      description: Contract instance ID
    templateId:
      type: string
      format: uuid
      description: Source template ID
```

**Key rules**:
- MUST use `allOf` with `$ref` to `BaseServiceEvent` — this produces C# inheritance and `IBannouEvent` implementation
- MUST include `eventName` with a `default:` value matching the event's topic — this provides event type identification for message taps and generic processing
- MUST include `eventId` and `timestamp` in the `required` array (inherited from `BaseServiceEvent`, but required must be redeclared)
- Do NOT define `eventId` or `timestamp` as properties — they are inherited from `BaseServiceEvent`
- Use `additionalProperties: false`
- Reference complex types via `$ref` to the service's `-api.yaml` (per mandatory type reuse)

**Why `allOf` is required**: Without `allOf`, NSwag generates a standalone class with no inheritance. The class will NOT implement `IBannouEvent`, will have no `EventName` property, and will be invisible to message taps (`IMessageTap`) and generic event processing pipelines.

**Note on `eventName`**: Service events use `eventName` via `IBannouEvent.EventName` for generic event processing and message tap forwarding. This is a DIFFERENT purpose than client events, which use `eventName` for Connect service whitelist routing. Both are valid uses of the same property name.

### Client Event Inheritance Structure

Client events (server→client WebSocket push) use `allOf` composition with `BaseClientEvent` from `common-client-events.yaml`:

```yaml
# In {service}-client-events.yaml — client event (allOf inheritance)
ChatMessageReceivedEvent:
  allOf:
    - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
  type: object
  additionalProperties: false
  x-client-event: true
  description: Sent to room participants when a new message is received.
  required:
    - eventName
    - eventId
    - timestamp
    - roomId
    - messageId
  properties:
    eventName:
      type: string
      default: "chat.message.received"
      description: Fixed event type identifier
    roomId:
      type: string
      format: uuid
      description: Room the message was sent to
    messageId:
      type: string
      format: uuid
      description: Unique identifier for the message
```

**Key rules**:
- Use `allOf` with `$ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'`
- Add `x-client-event: true` (required for SDK generators to detect it)
- Re-declare the `required` array including inherited fields (`eventName`, `eventId`, `timestamp`)
- Do NOT re-declare `eventId` or `timestamp` properties (inherited from `BaseClientEvent`)
- DO declare `eventName` with a `default:` value (the event's routing name, e.g., `"chat.message.received"`) — follows the same Pattern A/C rules as service event topics (see Client Event `eventName` Naming Rules above)
- Add `x-internal: true` alongside `x-client-event: true` for infrastructure-only events not exposed to game SDKs

**`BaseClientEvent` provides**: `eventName` (string), `eventId` (uuid), `timestamp` (date-time). All three are required.

### State Store Definition Format

State stores are defined in `schemas/state-stores.yaml` under the `x-state-stores:` extension key. Each entry declares a named store with its backend and ownership.

```yaml
x-state-stores:
  auth-statestore:
    backend: redis
    prefix: auth
    service: Auth
    purpose: Session and token state (ephemeral)

  chat-rooms:
    backend: mysql
    service: Chat
    purpose: Chat room records (durable, queryable by type/session/status)

  documentation-statestore:
    backend: redis
    prefix: doc
    service: Documentation
    purpose: Documentation content and metadata
    enableSearch: true
```

**Entry key**: Kebab-case name (e.g., `auth-statestore`, `chat-rooms-cache`, `actor-pool-nodes`). This is transformed into a PascalCase constant name in `StateStoreDefinitions.cs`.

| Property | Required | Description |
|----------|----------|-------------|
| `backend` | Yes | `redis`, `mysql`, or `memory` |
| `prefix` | Redis only | Redis key prefix (e.g., `auth`, `chat:room`, `actor:state`) |
| `table` | MySQL only | Table name (defaults to entry key with underscores if omitted) |
| `service` | Yes | Owning service name (PascalCase) |
| `purpose` | Yes | Human-readable description |
| `enableSearch` | No | Enable RedisSearch full-text indexing (redis only, default false) |

**Generated output**: `bannou-service/Generated/StateStoreDefinitions.cs` — static class with string constants for each store name, plus `docs/generated/GENERATED-STATE-STORES.md`.

---

## Mandatory Type Reuse via $ref (CRITICAL)

**ALL complex types (enums, objects with nested properties, arrays of objects) MUST be defined once in the service's `-api.yaml` and referenced via `$ref` from events, configuration, and lifecycle schemas.**

Violating this causes duplicate C# classes (`AuthMethods`, `AuthMethods2`, `AuthMethods3`), incompatible enums, type mismatches, and serialization failures.

### The Principle

**Events should contain the same complex data types as `Get*Response` from the API.** If `GetAccountResponse` returns `AuthMethodInfo`, then `AccountCreatedEvent` must use the SAME type via `$ref`.

### What MUST Use $ref

| Type Category | MUST Use $ref? |
|---------------|----------------|
| Enums | **YES** |
| Objects with nested properties or `$ref` | **YES** |
| Arrays of objects | **YES** |
| Simple flat objects (no refs) | Recommended |
| Primitives and arrays of primitives | No |

### Event Ref Resolver Limitation: No Wrapper Types Over Cross-File Refs

The event ref resolver (`resolve-event-refs.py`) inlines API types into event schemas only when they contain nested **local** `$ref`s (`#/components/schemas/...`). Types whose only nested refs are **cross-file** (e.g., `$ref: 'common-api.yaml#/...'`) appear "simple" to the resolver, are not inlined, and cause NSwag resolution failures when excluded from generation. **Do not create wrapper object types in API schemas that exist solely to bundle a cross-file `$ref`** (e.g., an `EntityReference` type containing only `entityId` + `$ref: 'common-api.yaml#/.../EntityType'`). Instead, place the entity ID and entity type fields directly on each model that needs them — the same pattern used by all existing services (Seed `ownerType`, Relationship `entity1Type`, Currency wallet owner, etc.).

### When NOT to Create Enums (Service Hierarchy Consideration)

Do NOT define enums that enumerate services, resources, or entity types from other layers — use opaque `string` instead. **Test**: "Would adding a new service/entity type in a higher layer require modifying this enum?" If yes, use a string.

**EntityType from common-api.yaml IS appropriate** for L2+ services referencing entity types within their own layer or lower. The hierarchy isolation exception ONLY applies when the enum would need to enumerate types from HIGHER layers (e.g., L1 enumerating L2+ types).

**Full decision tree**: See IMPLEMENTATION TENETS T14 for the authoritative three-category classification (Category A: entity references → EntityType, Category B: game content codes → string, Category C: system state → service-specific enum).

### Where to Define Enums (Location Decision Tree)

When adding a new enum, apply these tests in order — stop at the first match:

| Test | Condition | Location | Example |
|------|-----------|----------|---------|
| 1 | System-wide primitive used by **3+ services**? | `common-api.yaml` | `EntityType`, `ServiceHealthStatus` |
| 2 | Identity-boundary concept (T32)? | `common-api.yaml` | `OAuthProvider`, `AuthProvider` |
| 3 | Domain SDK type (MusicTheory, StorylineTheory, etc.)? | Service `-api.yaml` + A2 boundary mapping | `KeySignatureMode` in `music-api.yaml` |
| 4 | Service-specific system state/mode? | Owning service's `-api.yaml` | `ContractStatus`, `SaveCategory` |
| 5 | Game-configurable content extensible at deployment? | Opaque `string` (T14 Category B) | `seedType`, `collectionType` |

**Promotion threshold**: An enum used by fewer than 3 services stays in the owning service's `-api.yaml`. Once 3+ services reference it, move it to `common-api.yaml` and have all services `$ref` from there. Identity-boundary concepts (test 2) go to `common-api.yaml` regardless of consumer count.

**This decision tree is complementary to T14's polymorphic type field decision tree** (in IMPLEMENTATION-DATA.md). T14 answers "what *type* should this polymorphic field be?" (EntityType vs opaque string vs service-specific enum). This tree answers "where should the enum *live* in the schema hierarchy?" Both apply when adding new enums; neither replaces the other.

### Code-Only Enums Are Always Wrong

If an enum exists only in C# code (not in any schema), it violates the schema-first principle. Either define it in the appropriate schema and generate it, or use an existing schema-defined enum.

The only exception is enums that genuinely cannot be schema-defined — internal compiler/interpreter states covered by T1's standalone runtime/interpreter exemption (e.g., ABML bytecode opcodes).

```csharp
// WRONG: Code-only enum duplicating a schema-defined enum
public enum SubscriptionExchangeType { Fanout, Direct, Topic }  // Use generated ExchangeType

// CORRECT: Use the schema-generated enum everywhere
await _subscriber.SubscribeDynamicAsync<T>(topic, handler, ExchangeType.Direct);
```

### When Separate Enum Definitions Are Correct

OpenAPI (3.0.x and 3.1.x) has **no native mechanism** for defining enum subsets or supersets. JSON Schema's constraint system only adds constraints, never removes them. There is no way to express "`$ref` this enum but restrict to these 3 values." This means separate definitions are the correct pattern when:

1. **API safety subsets**: A writable enum intentionally excludes dangerous values from the full readable enum. Example: Transit's `SettableConnectionStatus` excludes `Destroyed` (system-managed state that callers cannot set directly).

2. **Granularity tiers**: Different concerns need different levels of detail from the same domain concept. Example: `ServiceHealthStatus` (3 values) vs `InstanceHealthStatus` (5 values) vs `EndpointStatus` (4 values) — each tier serves a different audience.

3. **Non-entity role inclusion**: A service-specific enum includes roles that are not in `EntityType` (T14 test 3). Example: Inventory's `ContainerOwnerType` includes `Escrow`, `Mail`, `Vehicle` alongside entity types.

4. **Cross-service schema boundary**: Two services need the same concept but schemas cannot `$ref` each other's API files. If the enum doesn't meet the 3-service threshold for `common-api.yaml`, separate definitions with documented relationships are correct.

**In all cases**: Document the relationship in the enum's schema `description` field (e.g., "Subset of ConnectionStatus excluding system-managed states").

#### Enum Boundary Classification (Audit Reference)

Every enum mapping in the codebase falls into one of these categories:

**Acceptable Boundaries (no fix needed)**:
- **A1 — Third-Party Library**: Mapping between a Bannou enum and an external library type (RabbitMQ, LibGit2Sharp, .NET framework, Redis Lua scripts). Genuine abstraction boundary.
- **A2 — Plugin SDK Boundary**: Mapping between a schema-generated enum and a domain-specific SDK enum (MusicTheory, StorylineTheory, ABML parser). The plugin defines its own enum in its schema and maps at the boundary.
- **A3 — Domain Decision Mapping**: Mapping between genuinely different domain concepts (e.g., ScenarioCategory to PoiType). Different concepts with intentional lossy transformation.
- **A4 — Protocol Boundary**: Mapping between HTTP/WebSocket protocol types and internal types (HttpMethodType to HttpMethod, HttpStatusCode to ResponseCodes).

**Violations (fix required)**:
- **V1 — Duplicate Enum (identical values)**: Two enums represent the same concept with identical values. Fix: consolidate to one definition.
- **V2 — Duplicate Enum (subset/superset)**: One enum is a strict subset/superset of another for the same concept. Fix: if values should genuinely differ, document the relationship and keep separate; if identical, consolidate.
- **V3 — String Where Enum Should Be**: Schema field typed as `string` when valid values are a finite, system-owned set. Fix: define an enum.
- **V4 — Internal Model Duplicates API Enum**: `*ServiceModels.cs` defines an enum identical to the generated API enum. Fix: use the generated enum directly.
- **V5 — String Configuration for Enum Values**: Configuration uses a string parsed at runtime into enum values. Fix: use typed enum in configuration schema.

### When Inline Redefinition of Types Is Correct (Exhaustive)

**"Redefine inline" means creating a separate but structurally identical (or similar) type definition in a service's own schema instead of using `$ref` to the original.** This is almost always wrong — it creates duplicate C# types, breaks type identity, and causes serialization mismatches. The correct approaches, in priority order, are:

1. **`$ref` to the owning schema** — the default. Events `$ref` types from their own service's `-api.yaml`. Configuration `$ref` enums from `-api.yaml`. This is the normal case.

2. **`$ref` to `common-api.yaml` or `common-events.yaml`** — when the type is used by 3+ services or is an identity-boundary concept (T32). Promotion is triggered when a third consumer appears.

3. **Class name reference (no `$ref`, no inline copy)** — for `x-event-subscriptions` consuming cross-service events. The generator resolves the event type by name across all event schemas. No schema-level reference or inline copy is needed.

**Inline redefinition is reserved for exactly two boundary situations:**

| Boundary | When | Example |
|----------|------|---------|
| **A2 — Plugin/SDK boundary** | A plugin defines its own schema enum and maps to/from a domain SDK enum (MusicTheory, StorylineTheory, ABML) at the service layer. The SDK enum cannot be schema-defined because it belongs to a standalone computation library exempt from schema-first (T1). | `music-api.yaml` defines `KeySignatureMode`; `MusicService.cs` maps it to `MusicTheory.KeySignatureMode` |
| **Cross-service schema boundary** | Two services need the same concept but cannot `$ref` each other's API files, AND the type does not meet the 3-service threshold for `common-api.yaml` promotion. | Two L4 services both need a 4-value status enum; each defines its own with a documented relationship in the `description` field |

**Everything else uses `$ref` or name-based resolution.** In particular:

- **Consumed event models are NEVER redefined inline.** Use the `event` class name in `x-event-subscriptions`. The producing service's event schema generates the type; the consuming service uses it by name.
- **Shared response/request shapes are NEVER redefined inline.** If two services need the same model, the type moves to `common-api.yaml`. Cross-service `$ref` between API schemas (e.g., `$ref: 'other-service-api.yaml#/...'`) is not supported — `common-api.yaml` is the only shared API type source.
- **"Cannot `$ref` other service event files"** (see [Canonical Definitions Only](#canonical-definitions-only)) means you do not put `$ref: 'voice-service-events.yaml#/...'` in your event schema definitions. It does NOT mean you copy the event model into your own schema. The resolution mechanism for consumed events is name-based, not reference-based.

### Enum Value Casing (PascalCase ONLY)

**ALL enum values in schemas MUST use PascalCase.** No exceptions for snake_case, SCREAMING_SNAKE_CASE, camelCase, or kebab-case.

```yaml
# CORRECT: PascalCase enum values
ContractStatus:
  type: string
  description: Current status of a contract
  enum: [Draft, Proposed, Pending, Active, Fulfilled, Expired, Terminated]

EscrowType:
  type: string
  description: Type of escrow arrangement
  enum: [TwoParty, MultiParty, Conditional, Auction]

QuestDifficulty:
  type: string
  description: Quest difficulty level
  enum: [Trivial, Easy, Normal, Hard, Heroic, Legendary]

# WRONG: snake_case (most common violation)
enum: [two_party, multi_party, conditional, auction]

# WRONG: SCREAMING_SNAKE_CASE
enum: [TRIVIAL, EASY, NORMAL, HARD, HEROIC, LEGENDARY]

# WRONG: camelCase
enum: [jsonPathEquals, jsonPathNotEquals, greaterThan]

# WRONG: kebab-case
enum: [pool-per-type, shared-pool, auto-scale]
```

**Why PascalCase**: BannouJson serializes C# enum members verbatim (no naming policy). NSwag generates `[EnumMember(Value = @"...")]` attributes that map schema values to C# member names. A post-processing step (`postprocess_enum_pascalcase` in `scripts/common.sh`) fixes NSwag's own underscore-style member naming (e.g., `Value_with_underscores` → `ValueWithUnderscores`), but this is a fixup for NSwag behavior, not a schema-to-PascalCase converter. Using PascalCase in schemas eliminates the mismatch between schema values and actual wire values, prevents bugs from hardcoded string comparisons against schema values, and establishes one convention instead of five.

**Inline enums follow the same rule**:
```yaml
# CORRECT: Inline enum with PascalCase
properties:
  status:
    type: string
    enum: [Active, Deprecated, Dissolved]

# WRONG: Inline enum with snake_case
properties:
  status:
    type: string
    enum: [active, deprecated, dissolved]
```

### Correct Pattern

```yaml
# In account-api.yaml - DEFINE once
AuthMethodInfo:
  type: object
  additionalProperties: false
  description: Linked authentication method details
  required: [provider, linkedAt]
  properties:
    provider:
      $ref: '#/components/schemas/AuthProvider'
      description: Authentication provider type
    linkedAt:
      type: string
      format: date-time
      description: When this auth method was linked

AuthProvider:
  type: string
  description: Supported authentication providers
  enum: [Email, Google, Discord, Twitch, Steam]

# In account-service-events.yaml x-lifecycle - REFERENCE via $ref
x-lifecycle:
  Account:
    model:
      authMethods:
        type: array
        items:
          $ref: 'account-api.yaml#/components/schemas/AuthMethodInfo'
```

### Wrong Pattern

```yaml
# WRONG: Inline definition in events (creates duplicate type per event)
x-lifecycle:
  Account:
    model:
      authMethods:
        type: array
        items:
          type: object
          properties:
            provider:
              type: string
              enum: [email, google, discord]  # Duplicates AuthProvider!
```

---

## NRT Quick Reference

**API models** (generated by NSwag from `*-api.yaml`):

| Scenario | Schema Pattern | Generated C# |
|----------|---------------|--------------|
| Required request field | `required: [field]` | `[Required(AllowEmptyStrings = true)] [JsonRequired] string Field = default!;` |
| Optional request string | `nullable: true` | `string? Field = default!;` |
| Optional request int | `nullable: true` | `int? Field` |
| Int with default | `default: 10` | `int Field = 10;` |
| Bool with default | `default: true` | `bool Field = true;` |
| Response field always set | `required: [field]` | `[Required(AllowEmptyStrings = true)] [JsonRequired] string Field = default!;` |
| Response field sometimes null | `nullable: true` | `string? Field = default!;` |

**Configuration classes** (generated by `generate-config.sh` from `*-configuration.yaml`):

| Scenario | Schema Pattern | Generated C# |
|----------|---------------|--------------|
| Optional config string | `nullable: true` | `string? Field` (no `= default!`) |
| Required config string | `required: [field]` | `string Field` |

Note: Configuration generation uses a different script than API model generation. The key difference is that configuration classes do NOT add `= default!` initializers to nullable properties.

---

## Validation Checklist

Before submitting schema changes, verify:

**API Schemas**: All endpoints POST (except browser exceptions). All endpoints have `x-permissions`. All properties have `description`. `servers` URL is `http://localhost:5012`. `x-service-layer` set correctly.

**NRT Compliance**: Optional reference types have `nullable: true`. Server-always-set properties in `required` array. No `default: ""`.

**Configuration**: Every property has `env` with `{SERVICE}_{PROPERTY}` naming. No `type: object`. Enums via `$ref`. Single-line descriptions only.

**Events**: Only canonical definitions (no cross-service `$ref`). Lifecycle events via `x-lifecycle`. Custom events MUST use `allOf` with `BaseServiceEvent` (produces C# inheritance, `IBannouEvent`, `EventName`). Custom events MUST include `eventName` with `default:` value. Subscriptions via `x-event-subscriptions`. All published events listed in `x-event-publications` (lifecycle + custom). Parameterized topics (with `{placeholder}` in routing key) MUST have `topic-params` with name/type for each placeholder.

**Enum Values**: ALL enum values use PascalCase (`TwoParty`, not `two_party`, `TWO_PARTY`, `twoParty`, or `two-party`). No exceptions.

**Type References**: ALL enums/complex objects use `$ref` to `-api.yaml`. x-lifecycle model fields use `$ref` for objects/enums. All `$ref` paths sibling-relative in hand-written schemas (no `../`; Generated/ schemas use `../` by design).

**x-references**: Consumer services declare `x-references` with `target`, `sourceType`, `field`, `cleanup`. `cleanup.endpoint` matches an actual endpoint. `target` matches foundational service's `x-resource-lifecycle.resourceType`.

**x-resource-lifecycle**: Foundational services with deletable resources declare it. `cleanupPolicy` is `BEST_EFFORT` or `ALL_REQUIRED`.

**x-resource-mapping**: Events with `x-resource-mapping` have valid `resource_type`, `resource_id_field`, and `source_type`. `resource_id_field` matches an actual property on the event. Lifecycle events use `resource_mapping` in `x-lifecycle` instead.

**x-compression-callback**: `compressEndpoint` exists in paths. `priority` set appropriately (0 base, 10-30 extension, 50-100 optional). Plugin calls generated `*CompressionCallbacks.RegisterAsync()`.

**x-event-template**: `name` is unique across services. Plugin calls generated `*EventTemplates.RegisterAll()`. No manual `EventTemplate` definitions remain.

**x-constraint-groups**: Every `constraint-group` reference on a property matches a defined group. Every group has at least 2 member properties. Sum constraints only on numeric properties. Presence constraints only on nullable properties. `value` set for sum constraints, absent for presence constraints. `tolerance` only on `sum-equals`.

**Metadata Bags**: Any property with `additionalProperties: true` includes description stating "Client-only metadata. No Bannou plugin reads specific keys from this field by convention." No cross-service data contracts via metadata bags. No documentation specifying convention-based keys for other services to read.
