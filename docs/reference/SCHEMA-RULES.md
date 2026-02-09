# Bannou Schema Rules

> **MANDATORY**: AI agents and developers MUST review this document before creating or modifying ANY OpenAPI schema file. This rule is inviolable - see TENETS.md Tenet 1.

This is the authoritative reference for all schema authoring in Bannou. It covers schema file types, generation pipeline, extension attributes, NRT compliance, validation keywords, type references, and common pitfalls.

---

## Table of Contents

1. [Schema File Types](#schema-file-types)
2. [Generation Pipeline](#generation-pipeline)
3. [Extension Attributes (x-*)](#extension-attributes-x-)
4. [NRT (Nullable Reference Types) Rules](#nrt-nullable-reference-types-rules)
5. [Schema Reference Hierarchy ($ref)](#schema-reference-hierarchy-ref)
6. [Validation Keywords](#validation-keywords)
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
| `{service}-events.yaml` | Service-to-service pub/sub events | Event models in `bannou-service/Generated/Events/` |
| `{service}-configuration.yaml` | Service configuration properties | `{Service}ServiceConfiguration.cs` |
| `{service}-client-events.yaml` | Server→client WebSocket push events | Client event models in plugin `Generated/` |

**Common schemas** (shared across services):
- `common-api.yaml` - System-wide types (EntityType, etc.)
- `common-events.yaml` - Base event schemas (BaseServiceEvent)
- `common-client-events.yaml` - Base client event schemas (BaseClientEvent)
- `state-stores.yaml` - State store definitions → `StateStoreDefinitions.cs`

---

## Generation Pipeline

Run `make generate` or `scripts/generate-all-services.sh` to execute the full pipeline:

| Step | Source | Generated Output |
|------|--------|------------------|
| 1. State Stores | `state-stores.yaml` | `lib-state/Generated/StateStoreDefinitions.cs` |
| 2. Lifecycle Events | `x-lifecycle` in events.yaml | `schemas/Generated/{service}-lifecycle-events.yaml` |
| 3. Common Events | `common-events.yaml` | `bannou-service/Generated/Events/CommonEventsModels.cs` |
| 4. Service Events | `{service}-events.yaml` | `bannou-service/Generated/Events/{Service}EventsModels.cs` |
| 5. Client Events | `{service}-client-events.yaml` | `lib-{service}/Generated/{Service}ClientEventsModels.cs` |
| 6. Meta Schemas | `{service}-api.yaml` | `schemas/Generated/{service}-api-meta.yaml` |
| 7. Service API | `{service}-api.yaml` | Controllers, models, clients, interfaces |
| 8. Configuration | `{service}-configuration.yaml` | `{Service}ServiceConfiguration.cs` |
| 9. Permissions | `x-permissions` in api.yaml | `{Service}PermissionRegistration.cs` |
| 10. Event Subscriptions | `x-event-subscriptions` | `{Service}EventsController.cs` |

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

---

## Extension Attributes (x-*)

Bannou uses custom OpenAPI extensions to drive code generation.

### x-permissions (Required on ALL API Endpoints)

Declares role and state requirements for WebSocket client access. **All endpoints MUST have x-permissions, even if empty.**

```yaml
paths:
  /account/get:
    post:
      x-permissions:
        - role: user
  /health:
    post:
      x-permissions: []  # Explicitly public (rare)
```

**Role hierarchy**: `anonymous` → `user` → `developer` → `admin`

**With state requirements**: Add `states: { game-session: in_lobby }` to require both role AND state.

### x-lifecycle (Lifecycle Event Generation)

Defined in `{service}-events.yaml`, generates CRUD lifecycle events automatically.

```yaml
x-lifecycle:
  EntityName:
    model:
      entityId: { type: string, format: uuid, primary: true, required: true }
      name: { type: string, required: true }
      createdAt: { type: string, format: date-time, required: true }
    sensitive: [passwordHash, secretKey]  # Fields excluded from events
    resource_mapping:                      # Optional: enables Puppetmaster watch subscriptions
      resource_type: entity                # Defaults to entity name in kebab-case
      resource_id_field: entityId          # Defaults to primary key field
      source_type: entity                  # Defaults to entity name in kebab-case
```

**Generated output** (`schemas/Generated/{service}-lifecycle-events.yaml`):
- `EntityNameCreatedEvent` - Full entity data on creation
- `EntityNameUpdatedEvent` - Full entity data + `changedFields` array
- `EntityNameDeletedEvent` - Entity ID + `deletedReason`

If `resource_mapping` is specified, each generated event includes `x-resource-mapping` for Puppetmaster watch subscriptions.

**NEVER manually define `*CreatedEvent`, `*UpdatedEvent`, `*DeletedEvent`** - use `x-lifecycle` instead.

### x-resource-mapping (Resource Event Mapping)

Defined on **event schema definitions** in `{service}-events.yaml`, declares how the event relates to a watchable resource for Puppetmaster's watch system.

**For lifecycle events**: Use `resource_mapping` in `x-lifecycle` (above) - the generator adds `x-resource-mapping` automatically.

**For manually-defined events** (non-lifecycle):

```yaml
PersonalityUpdatedEvent:
  type: object
  x-resource-mapping:
    resource_type: character             # Type of resource this event affects
    resource_id_field: characterId       # JSON field containing resource ID
    source_type: character-personality   # Source type identifier for filtering
    is_deletion: false                   # Optional (inferred from event name ending in "Deleted")
```

**Generated output**: `bannou-service/Generated/ResourceEventMappings.cs` - static class with `IReadOnlyList<ResourceEventMappingEntry>` used by Puppetmaster's watch system.

### x-event-subscriptions (Event Handler Generation)

Defined in `{service}-events.yaml`, generates event subscription handlers.

```yaml
info:
  x-event-subscriptions:
    - topic: account.deleted
      event: AccountDeletedEvent
      handler: HandleAccountDeleted
```

- `topic`: RabbitMQ routing key
- `event`: Event model class name
- `handler`: Handler method name (without `Async` suffix)

**Generated output**: `{Service}EventsController.cs` (handlers) and `{Service}ServiceEvents.cs` (registration template).

### x-service-layer (Service Hierarchy Layer)

Defined at the **root level** of `{service}-api.yaml`, declares the service's position in the hierarchy per [SERVICE-HIERARCHY.md](SERVICE-HIERARCHY.md). Controls plugin load order and enables safe cross-layer constructor injection.

```yaml
openapi: 3.0.0
info:
  title: Location Service API
  version: 1.0.0
x-service-layer: GameFoundation  # L2 service
```

**Valid values** (in load order):
| Value | Layer | Description | Example Services |
|-------|-------|-------------|------------------|
| `Infrastructure` | L0 | Core infrastructure plugins | state, messaging, mesh, telemetry |
| `AppFoundation` | L1 | Required for any deployment | account, auth, connect, permission, contract, resource |
| `GameFoundation` | L2 | Required for game deployments | realm, character, species, location, currency, item, inventory |
| `AppFeatures` | L3 | Optional app capabilities | asset, orchestrator, documentation, website |
| `GameFeatures` | L4 | Optional game capabilities | actor, behavior, matchmaking, analytics, achievement |
| `Extensions` | L5 | Third-party/meta-services | Custom plugins that need full stack |

**Numeric values also accepted** (for programmatic generation): `0` = Infrastructure, `100` = AppFoundation, `200` = GameFoundation, `300` = AppFeatures, `400` = GameFeatures, `500` = Extensions.

**Default**: If omitted, defaults to `GameFeatures` (most permissive).

**Generated**: `[BannouService("location", typeof(ILocationService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFoundation)]`

### x-service-configuration (Configuration Properties)

Defined in `{service}-configuration.yaml`. See [Configuration Schema Rules](#configuration-schema-rules) for detailed requirements.

```yaml
x-service-configuration:
  properties:
    MaxConnections:
      type: integer
      env: MY_SERVICE_MAX_CONNECTIONS
      default: 100
      description: Maximum concurrent connections
```

### x-references (Resource Reference Tracking)

Defined in consumer service API schemas (`*-api.yaml`), declares references to foundational resources for lifecycle management via lib-resource. Higher-layer services (L3/L4) declare their references to foundational resources (L2); the code generator produces helper methods for publishing reference events and registering cleanup callbacks.

```yaml
info:
  x-references:
    - target: character                         # Resource type being referenced (opaque string)
      sourceType: actor                         # This service's entity type (opaque string)
      field: characterId                        # Field holding the reference (documentation)
      onDelete: cascade                         # cascade | restrict | detach (intent)
      cleanup:
        endpoint: /actor/cleanup-by-character   # Cleanup callback endpoint
        payloadTemplate: '{"characterId": "{{resourceId}}"}'
```

**Field definitions**:
- `target`: Resource type (must match `resourceType` in the foundational service's `x-resource-lifecycle`)
- `sourceType`: This service's entity type (opaque string)
- `field`: Field name holding the reference (informational)
- `onDelete`: `cascade` (delete dependents), `restrict` (block deletion, not yet supported), `detach` (nullify, not yet supported)
- `cleanup.endpoint`: Service endpoint called during cleanup
- `cleanup.payloadTemplate`: JSON template with `{{resourceId}}` placeholder

**Generated output** (`lib-{service}/Generated/{Service}ReferenceTracking.Generated.cs`):
- Helper methods: `Register{Target}ReferenceAsync()`, `Unregister{Target}ReferenceAsync()`
- Cleanup callback registration via `IStartupTask`

### x-resource-lifecycle (Resource Cleanup Configuration)

Defined in foundational service API schemas (`*-api.yaml`), declares grace period and cleanup policy for resources tracked by lib-resource.

```yaml
info:
  x-resource-lifecycle:
    resourceType: character            # Must match x-references target
    gracePeriodSeconds: 604800         # 7 days before cleanup eligible
    cleanupPolicy: BEST_EFFORT        # BEST_EFFORT | ALL_REQUIRED
```

- `BEST_EFFORT`: Proceed with deletion even if some callbacks fail
- `ALL_REQUIRED`: Abort deletion if any callback fails

**Note**: Documentation/configuration only - does not generate code. Values are used when calling `/resource/cleanup/execute`.

### x-compression-callback (Compression Callback Registration)

Defined at the **info level** of `{service}-api.yaml`, declares compression callback registration for hierarchical resource archival via lib-resource.

```yaml
info:
  x-compression-callback:
    resourceType: character                                          # Required
    sourceType: character-personality                                # Required
    compressEndpoint: /character-personality/get-compress-data       # Required (must exist in paths)
    compressPayloadTemplate: '{"characterId": "{{resourceId}}"}'     # Required
    priority: 10                                                     # Required (lower = earlier)
    templateNamespace: personality                                   # Optional (defaults to sourceType)
    description: Personality traits and combat preferences           # Optional
    decompressEndpoint: /character-personality/restore-from-archive  # Optional
    decompressPayloadTemplate: '{"characterId": "{{resourceId}}", "data": "{{data}}"}' # Optional
```

**Generated output** (`lib-{service}/Generated/{Service}CompressionCallbacks.cs`): Static class with `RegisterAsync()` method, called from plugin's `OnRunningAsync`.

**Priority Guidelines**:

| Priority Range | Purpose |
|----------------|---------|
| 0 | Base entity data (e.g., character core fields) |
| 10-30 | Extension data (personality, history, encounters) |
| 50-100 | Optional/derived data (quests, storylines) |

**Validation**: Generator validates that `compressEndpoint` and `decompressEndpoint` (if specified) exist in the schema's paths.

### x-archive-type (Resource Template Generation)

Defined on **compression response schemas** (return type of `/get-compress-data` endpoints), marks a schema as an archive type. When combined with `x-compression-callback`, triggers generation of `IResourceTemplate` implementations for compile-time ABML path validation.

```yaml
CharacterPersonalityArchive:
  type: object
  x-archive-type: true
  allOf:
    - $ref: './common-api.yaml#/components/schemas/ResourceArchiveBase'
  properties:
    characterId: { type: string, format: uuid }
    personality: { $ref: '#/components/schemas/PersonalityResponse' }
```

**How template generation works**:
1. Generator scans for schemas with `x-archive-type: true`
2. Matches to `x-compression-callback` to get `sourceType` and `templateNamespace`
3. Traverses schema to build `ValidPaths` dictionary (property names → C# types)
4. Generates template class to `bannou-service/Generated/ResourceTemplates/{SourceType}Template.cs`

Templates enable compile-time validation of ABML snapshot access expressions (e.g., `${candidate.personality.archetypeHint}`). Register in `OnRunningAsync` via `IResourceTemplateRegistry.Register()`.

### x-event-template (Event Template Generation)

Defined on **individual event schema definitions** in `{service}-events.yaml`, declares that the event should have an auto-generated template for use with `emit_event:` ABML actions.

```yaml
EncounterRecordedEvent:
  type: object
  additionalProperties: false
  description: Published when a new encounter is recorded
  x-event-template:
    name: encounter_recorded    # Template name for ABML emit_event
    topic: encounter.recorded   # Event topic (RabbitMQ routing key)
  required: [eventId, encounterId]
  properties:
    eventId: { type: string, format: uuid }
    encounterId: { type: string, format: uuid }
```

**PayloadTemplate generation rules**:
- `type: string` (non-nullable) → `"{{propertyName}}"` (quoted)
- `type: string` (nullable) → `{{propertyName}}` (unquoted, TemplateSubstitutor handles null)
- `type: integer`, `type: number`, `type: boolean` → `{{propertyName}}` (unquoted)
- `type: array`, `type: object` → `{{propertyName}}` (unquoted, pre-serialized JSON)

**Generated output** (`lib-{service}/Generated/{Service}EventTemplates.cs`): Static class with `EventTemplate` fields and `RegisterAll(IEventTemplateRegistry)` method. Call from `OnRunningAsync`.

### Service Hierarchy Compliance

The x-references pattern ensures compliance with the service hierarchy:

1. **Consumer services (L3/L4)** declare `x-references` - they "know about" foundational resources
2. **Foundational services (L2)** declare `x-resource-lifecycle` - they don't know about consumers
3. **lib-resource (L1)** uses opaque string identifiers - no coupling to higher layers

```
┌───────────────────────────────────────────────────────────────┐
│ L4: Game Features (actor, scene, etc.)                        │
│   → x-references: target: "character" (declares dependency)   │
│   → Publishes: resource.reference.registered/unregistered     │
├───────────────────────────────────────────────────────────────┤
│ L2: Game Foundation (character, realm, etc.)                  │
│   → x-resource-lifecycle: gracePeriodSeconds, cleanupPolicy   │
│   → Calls: /resource/check, /resource/cleanup/execute         │
├───────────────────────────────────────────────────────────────┤
│ L1: App Foundation (lib-resource)                             │
│   → Maintains refcounts, executes cleanup callbacks           │
│   → Uses opaque strings for resourceType/sourceType           │
└───────────────────────────────────────────────────────────────┘
```

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

**Generates**: `[Required] [JsonRequired] public string Email { get; set; } = default!;`

### Rule 2: Optional Reference Types → `nullable: true`

Properties that MAY be absent (strings, objects, arrays) MUST have `nullable: true`.

```yaml
displayName:
  type: string
  nullable: true
  description: Optional display name
```

**Generates**: `public string? DisplayName { get; set; }`

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

NSwag processes each schema file independently. When multiple schemas define the same type, duplicate C# classes are generated, causing compilation errors. Follow this hierarchy:

```
                         ┌─────────────────┐
                         │   common-api    │  ← System-wide shared types (EntityType, etc.)
                         │     .yaml       │    Available to ALL schemas
                         └────────┬────────┘
                                  │ $ref allowed from anywhere
                         ┌────────┴────────┐
                         │  {service}-api  │  ← Service-specific shared types (enums, models)
                         │     .yaml       │
                         └────────┬────────┘
                                  │ $ref allowed
        ┌─────────────────────────┼─────────────────────────┐
        ▼                         ▼                         ▼
┌───────────────┐       ┌─────────────────┐       ┌──────────────────┐
│ {service}     │       │ {service}       │       │ {service}        │
│ -events.yaml  │       │ -configuration  │       │ -lifecycle       │
│               │       │ .yaml           │       │ -events.yaml     │
└───────────────┘       └─────────────────┘       └──────────────────┘
```

### Reference Rules

All `$ref` paths are sibling-relative (same directory). Never use `../` prefixes.

| Source Schema | Can Reference | Path Format |
|--------------|---------------|-------------|
| `*-api.yaml` | `common-api.yaml` | `common-api.yaml#/...` |
| `*-events.yaml` | Same service's `-api.yaml`, `common-api.yaml`, `common-events.yaml` | `{service}-api.yaml#/...` |
| `*-configuration.yaml` | Same service's `-api.yaml`, `common-api.yaml` | `{service}-api.yaml#/...` |

### Common Shared Files

- **`common-api.yaml`** - System-wide types like `EntityType` enum. Available to all schemas.
- **`common-events.yaml`** - Base event schemas like `BaseServiceEvent`. Used by `*-events.yaml` files.
- **`common-client-events.yaml`** - Base client event schemas like `BaseClientEvent`. Used by client-facing events.

### $ref Examples

```yaml
# Configuration referencing API enum (save-load-configuration.yaml)
DefaultCompressionType:
  $ref: 'save-load-api.yaml#/components/schemas/CompressionType'
  env: SAVE_LOAD_DEFAULT_COMPRESSION_TYPE
  default: GZIP
  description: Default compression algorithm

# Events referencing API type (actor-events.yaml)
properties:
  status:
    $ref: 'actor-api.yaml#/components/schemas/ActorStatus'
```

---

## Validation Keywords

OpenAPI 3.0.3 validation keywords generate `[ConfigX]` attributes in C#, validated at service startup.

| Keyword | Applies To | Schema Example | Generated Attribute |
|---------|------------|----------------|---------------------|
| `minimum` | number/integer | `minimum: 1` | `[ConfigRange(Minimum = 1)]` |
| `maximum` | number/integer | `maximum: 100` | `[ConfigRange(Maximum = 100)]` |
| `exclusiveMinimum` | boolean | `exclusiveMinimum: true` | `[ConfigRange(..., ExclusiveMinimum = true)]` |
| `exclusiveMaximum` | boolean | `exclusiveMaximum: true` | `[ConfigRange(..., ExclusiveMaximum = true)]` |
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
  default: GZIP
  description: Default compression algorithm
```

### Rule 6: Single-Line Descriptions Only

Descriptions MUST be single-line. Multi-line YAML blocks (`|` or `>`) produce malformed C# XML documentation comments that cause compile errors.

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

Each `{service}-events.yaml` MUST contain ONLY canonical definitions for events that service PUBLISHES. **No `$ref` references to other service event files** (causes duplicate types).

### Topic Naming Convention

**Pattern**: `{entity}.{action}` (kebab-case entity, lowercase action). Examples: `account.created`, `game-session.player-joined`. Infrastructure events use `bannou-` prefix.

### Client Events vs Service Events

| Type | File | Publishing |
|------|------|------------|
| Service Events | `{service}-events.yaml` | `IMessageBus.PublishAsync` |
| Client Events | `{service}-client-events.yaml` | `IClientEventPublisher.PublishToSessionAsync` |

**Never use `IMessageBus` for client events** - it uses the wrong RabbitMQ exchange.

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

### When NOT to Create Enums (Service Hierarchy Consideration)

Do NOT define enums that enumerate services, resources, or entity types from other layers. This creates implicit coupling that defeats layer isolation.

```yaml
# WRONG: L1 service enumerating L3/L4 services
ResourceSourceType:
  type: string
  enum: [ACTOR, CHARACTER_ENCOUNTER, CONTRACT, SCENE, SAVE_DATA]

# CORRECT: Opaque string identifiers
resourceType:
  type: string
  description: Type of resource being referenced (caller provides)
```

**When enums ARE appropriate**: Values are within the service's own layer or lower, the enum represents a closed set the service owns, or it's in `common-api.yaml` for system-wide concepts.

**Test**: "Would adding a new service/entity type in a higher layer require modifying this enum?" If yes, use a string.

### Correct Pattern

```yaml
# In account-api.yaml - DEFINE once
AuthMethodInfo:
  type: object
  properties:
    provider:
      $ref: '#/components/schemas/AuthProvider'
    linkedAt:
      type: string
      format: date-time

AuthProvider:
  type: string
  enum: [email, google, discord, twitch, steam]

# In account-events.yaml x-lifecycle - REFERENCE via $ref
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

| Scenario | Schema Pattern | Generated C# |
|----------|---------------|--------------|
| Required request field | `required: [field]` | `[Required] string Field = default!;` |
| Optional request string | `nullable: true` | `string? Field` |
| Optional request int | `nullable: true` | `int? Field` |
| Int with default | `default: 10` | `int Field = 10;` |
| Bool with default | `default: true` | `bool Field = true;` |
| Response field always set | `required: [field]` | `[Required] string Field = default!;` |
| Response field sometimes null | `nullable: true` | `string? Field` |

---

## Validation Checklist

Before submitting schema changes, verify:

**API Schemas**: All endpoints POST (except browser exceptions). All endpoints have `x-permissions`. All properties have `description`. `servers` URL is `http://localhost:5012`. `x-service-layer` set correctly.

**NRT Compliance**: Optional reference types have `nullable: true`. Server-always-set properties in `required` array. No `default: ""`.

**Configuration**: Every property has `env` with `{SERVICE}_{PROPERTY}` naming. No `type: object`. Enums via `$ref`. Single-line descriptions only.

**Events**: Only canonical definitions (no cross-service `$ref`). Lifecycle events via `x-lifecycle`. Subscriptions via `x-event-subscriptions`.

**Type References**: ALL enums/complex objects use `$ref` to `-api.yaml`. x-lifecycle model fields use `$ref` for objects/enums. All `$ref` paths sibling-relative (no `../`).

**x-references**: Consumer services declare `x-references` with `target`, `sourceType`, `field`, `cleanup`. `cleanup.endpoint` matches an actual endpoint. `target` matches foundational service's `x-resource-lifecycle.resourceType`.

**x-resource-lifecycle**: Foundational services with deletable resources declare it. `cleanupPolicy` is `BEST_EFFORT` or `ALL_REQUIRED`.

**x-compression-callback**: `compressEndpoint` exists in paths. `priority` set appropriately (0 base, 10-30 extension, 50-100 optional). Plugin calls generated `*CompressionCallbacks.RegisterAsync()`.

**x-event-template**: `name` is unique across services. Plugin calls generated `*EventTemplates.RegisterAll()`. No manual `EventTemplate` definitions remain.

**Resource Cleanup Contract (Producer Side)**: When your service is the `target` of `x-references`, your delete flow MUST: inject `IResourceClient`, call `/resource/check` before deletion, call `/resource/cleanup/execute` if references exist, handle cleanup failure (return `Conflict`), only delete after cleanup succeeds. **FORBIDDEN**: Adding event handlers that duplicate cleanup callbacks, deleting without `ExecuteCleanupAsync`, assuming event-based cleanup is equivalent.

```bash
# Find all x-references targets (services that MUST call ExecuteCleanupAsync)
grep -h "target:" schemas/*-api.yaml | grep -v "^#" | sort -u
```

**Final**: Run `scripts/generate-all-services.sh` and verify build passes. Check generated C# for expected attributes and nullability.
