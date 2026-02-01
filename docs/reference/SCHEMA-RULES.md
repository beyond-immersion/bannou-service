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
10. [Common Anti-Patterns](#common-anti-patterns)
11. [Quick Reference Tables](#quick-reference-tables)
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
| `lib-{service}/Services/*.cs` | Yes | Helper services |
| `lib-{service}/{Service}ServiceEvents.cs` | Yes | Generated once, then manual |
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
        - role: user  # Requires user role
      # ...
  /admin/delete:
    post:
      x-permissions:
        - role: admin  # Requires admin role
      # ...
  /health:
    post:
      x-permissions: []  # Explicitly public (rare)
```

**Role hierarchy**: `anonymous` → `user` → `developer` → `admin`

**With state requirements**:
```yaml
x-permissions:
  - role: user
    states:
      game-session: in_lobby  # Must be user AND in_lobby state
```

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
```

**Generated output** (`schemas/Generated/{service}-lifecycle-events.yaml`):
- `EntityNameCreatedEvent` - Full entity data on creation
- `EntityNameUpdatedEvent` - Full entity data + `changedFields` array
- `EntityNameDeletedEvent` - Entity ID + `deletedReason`

**NEVER manually define `*CreatedEvent`, `*UpdatedEvent`, `*DeletedEvent`** - use `x-lifecycle` instead.

### x-event-subscriptions (Event Handler Generation)

Defined in `{service}-events.yaml`, generates event subscription handlers.

```yaml
info:
  x-event-subscriptions:
    - topic: account.deleted
      event: AccountDeletedEvent
      handler: HandleAccountDeleted
    - topic: session.connected
      event: SessionConnectedEvent
      handler: HandleSessionConnected
```

**Field definitions**:
- `topic`: RabbitMQ routing key
- `event`: Event model class name
- `handler`: Handler method name (without `Async` suffix)

**Generated output**: `{Service}EventsController.cs` (handlers) and `{Service}ServiceEvents.cs` (registration template).

### x-service-configuration (Configuration Properties)

Defined in `{service}-configuration.yaml`, defines service configuration.

```yaml
x-service-configuration:
  properties:
    MaxConnections:
      type: integer
      env: MY_SERVICE_MAX_CONNECTIONS
      default: 100
      description: Maximum concurrent connections
```

See [Configuration Schema Rules](#configuration-schema-rules) for detailed requirements.

---

## NRT (Nullable Reference Types) Rules

All Bannou projects have `<Nullable>enable</Nullable>`. NSwag generates C# code with `/generateNullableReferenceTypes:true`. This means **schema definitions DIRECTLY control C# nullability**.

Incorrect schema definitions cause:
- `string = default!;` for optional properties (hides null at runtime)
- Missing `[Required]` attributes on properties the server always sets
- NRT violations that only surface at runtime as NullReferenceExceptions

### API Schema Rules (`*-api.yaml`)

#### Rule 1: Required Properties → `required` Array

Properties the caller MUST provide go in the `required` array at the schema level.

```yaml
CreateAccountRequest:
  type: object
  required:        # Properties caller MUST provide
    - email
    - password
  properties:
    email:
      type: string
      description: User's email address
```

**NSwag generates**: `[Required] [JsonRequired] public string Email { get; set; } = default!;`

The `[JsonRequired]` attribute ensures deserialization fails if the property is missing.

#### Rule 2: Optional Reference Types → `nullable: true`

Properties the caller MAY omit (strings, objects, arrays) MUST have `nullable: true`.

```yaml
properties:
  displayName:
    type: string
    nullable: true  # Caller can omit this
    description: Optional display name
```

**NSwag generates**: `public string? DisplayName { get; set; }`

**WITHOUT `nullable: true`**, NSwag generates `public string DisplayName { get; set; } = default!;` which **HIDES** the fact that the value can be null at runtime!

#### Rule 3: Value Types with Defaults → `default: value`

Boolean/integer properties with sensible defaults just need `default: value`.

```yaml
properties:
  pageSize:
    type: integer
    default: 20
    description: Number of items per page
```

**NSwag generates**: `public int PageSize { get; set; } = 20;`

#### Rule 4: Response Properties Server Always Sets → `required` Array

For response objects, if the service implementation ALWAYS sets a property, add it to `required` so NSwag generates non-nullable types with proper validation.

```yaml
AccountResponse:
  type: object
  required:        # Server ALWAYS sets these
    - accountId
    - email
    - createdAt
  properties:
    accountId:
      type: string
      format: uuid
      description: Unique account identifier
```

### Event Schema Rules (`*-events.yaml`)

#### Rule 1: Required Event Properties → `required` Array

Properties the event MUST have go in the `required` array.

```yaml
AccountCreatedEvent:
  type: object
  required:
    - eventId
    - timestamp
    - accountId
  properties:
    eventId:
      type: string
      format: uuid
      description: Unique event identifier
```

#### Rule 2: Optional Event Properties → `nullable: true`

Properties that MAY be absent MUST have `nullable: true`.

```yaml
properties:
  deletedReason:
    type: string
    nullable: true
    description: Optional reason for deletion
```

#### Rule 3: Value Types → Never Nullable Unless Semantically Optional

Boolean/integer properties are value types. Only mark `nullable: true` if the absence of a value is semantically meaningful (rare for events).

---

## Schema Reference Hierarchy ($ref)

NSwag processes each schema file independently. When multiple schemas define the same type, duplicate C# classes are generated, causing compilation errors. Follow this hierarchy:

### The Hierarchy

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

### Example: Configuration Referencing API Enum

```yaml
# In save-load-configuration.yaml
DefaultCompressionType:
  $ref: 'save-load-api.yaml#/components/schemas/CompressionType'
  env: SAVE_LOAD_DEFAULT_COMPRESSION_TYPE
  default: GZIP
  description: Default compression algorithm
```

### Example: Events Referencing API Type

```yaml
# In actor-events.yaml
properties:
  status:
    $ref: 'actor-api.yaml#/components/schemas/ActorStatus'
```

---

## Validation Keywords

OpenAPI 3.0.3 provides validation keywords that generate `[ConfigX]` attributes in C#. These are validated at service startup.

### Numeric Range Validation

```yaml
# Schema
Port:
  type: integer
  minimum: 1
  maximum: 65535
  default: 8080
  description: Server port number

# With exclusive bounds (value must be > 0, not >= 0)
PositiveValue:
  type: number
  minimum: 0
  exclusiveMinimum: true
  description: Must be strictly positive
```

**Generated C#:**
```csharp
[ConfigRange(Minimum = 1, Maximum = 65535)]
public int Port { get; set; } = 8080;

[ConfigRange(Minimum = 0, ExclusiveMinimum = true)]
public double PositiveValue { get; set; }
```

### String Length Validation

```yaml
# Schema
JwtSecret:
  type: string
  minLength: 32
  maxLength: 512
  default: "default-dev-secret-key-32-chars!"
  description: JWT signing secret (minimum 32 characters for security)

ApiKey:
  type: string
  minLength: 16
  description: API key for external service
```

**Generated C#:**
```csharp
[ConfigStringLength(MinLength = 32, MaxLength = 512)]
public string JwtSecret { get; set; } = "default-dev-secret-key-32-chars!";

[ConfigStringLength(MinLength = 16)]
public string ApiKey { get; set; } = "...";
```

**Use Cases:**
- JWT secrets (minimum 32 characters)
- Salt values (minimum length for cryptographic strength)
- Connection strings (sanity check maximum)
- Names and codes (maximum length for database constraints)

### Pattern Validation (Regex)

```yaml
# Schema
ServiceUrl:
  type: string
  pattern: "^https?://"
  default: "https://api.example.com"
  description: Service URL (must start with http:// or https://)

ServiceName:
  type: string
  pattern: "^[a-z][a-z0-9-]*$"
  default: "my-service"
  description: Service name (lowercase alphanumeric with hyphens)
```

**Generated C#:**
```csharp
[ConfigPattern(@"^https?://")]
public string ServiceUrl { get; set; } = "https://api.example.com";

[ConfigPattern(@"^[a-z][a-z0-9-]*$")]
public string ServiceName { get; set; } = "my-service";
```

**Use Cases:**
- URL formats (`^https?://`)
- Domain names
- Version strings (`^v\d+\.\d+\.\d+$`)
- Naming conventions

### MultipleOf Validation (Numeric Precision)

```yaml
# Schema
TimeoutMs:
  type: integer
  multipleOf: 1000
  default: 5000
  description: Timeout in whole seconds (milliseconds, multiple of 1000)

BufferSizeKb:
  type: integer
  multipleOf: 1024
  default: 4096
  description: Buffer size in KB boundaries

Price:
  type: number
  multipleOf: 0.01
  default: 9.99
  description: Price with cent precision
```

**Generated C#:**
```csharp
[ConfigMultipleOf(1000)]
public int TimeoutMs { get; set; } = 5000;

[ConfigMultipleOf(1024)]
public int BufferSizeKb { get; set; } = 4096;

[ConfigMultipleOf(0.01)]
public double Price { get; set; } = 9.99;
```

**Use Cases:**
- Timeouts in whole seconds (multiple of 1000ms)
- Buffer sizes on KB/MB boundaries (multiple of 1024)
- Currency precision (multiple of 0.01)
- Percentage steps (multiple of 5 for 0%, 5%, 10%, etc.)

### Combining Validation Keywords

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

**Generated C#:**
```csharp
[ConfigRange(Minimum = 1000, Maximum = 60000)]
[ConfigMultipleOf(1000)]
public int TimeoutMs { get; set; } = 5000;
```

---

## Configuration Schema Rules

Configuration schemas (`*-configuration.yaml`) have unique requirements.

### Rule 1: Always Include `env` Property

Every configuration property MUST have an `env` property specifying the environment variable name.

```yaml
# GOOD
MaxConnections:
  type: integer
  env: MY_SERVICE_MAX_CONNECTIONS  # Required!
  default: 100
  description: Maximum concurrent connections

# BAD - missing env
MaxConnections:
  type: integer
  default: 100
  description: Maximum concurrent connections
```

### Rule 2: Use Proper Service Prefix in `env`

Environment variable names should follow the pattern `{SERVICE}_{PROPERTY}`.

```yaml
# GOOD
env: SAVE_LOAD_MAX_SAVE_SIZE_BYTES

# BAD - missing service prefix
env: MAX_SAVE_SIZE_BYTES

# BAD - hyphen in prefix (use underscore)
env: SAVE-LOAD_MAX_SAVE_SIZE_BYTES
```

### Rule 3: Properties WITH Defaults → Use `default: value`

Most configuration has sensible defaults.

```yaml
MaxConnections:
  type: integer
  env: MY_SERVICE_MAX_CONNECTIONS
  default: 100
  description: Maximum concurrent connections
```

**Generated C#**: `public int MaxConnections { get; set; } = 100;`

### Rule 4: OPTIONAL Configuration → `nullable: true`

Configuration that is truly optional (feature disabled when absent) MUST be `nullable: true`.

```yaml
ProxyUrl:
  type: string
  nullable: true  # NRT: Optional config MUST be nullable
  env: MY_SERVICE_PROXY_URL
  description: Optional HTTP proxy URL (feature disabled when not set)
```

**Generated C#**: `public string? ProxyUrl { get; set; }`

### Rule 5: Never Use `type: object`

Configuration properties cannot be complex objects. The generator falls back to `string` for `type: object`, which causes problems.

```yaml
# BAD - type: object not supported
DefaultsByCategory:
  type: object
  description: Defaults per category

# GOOD - use string with documented format
DefaultsByCategory:
  type: string
  nullable: true
  env: MY_SERVICE_DEFAULTS_BY_CATEGORY
  description: Defaults per category as KEY=VALUE,KEY=VALUE format
```

### Rule 6: Enums via $ref to API Schema

When configuration needs enum types, reference them from the API schema.

```yaml
# In my-service-api.yaml - define the enum
components:
  schemas:
    CompressionType:
      type: string
      enum: [NONE, GZIP, BROTLI]

# In my-service-configuration.yaml - reference it
DefaultCompression:
  $ref: 'my-service-api.yaml#/components/schemas/CompressionType'
  env: MY_SERVICE_DEFAULT_COMPRESSION
  default: GZIP
  description: Default compression algorithm
```

### Rule 7: Single-Line Descriptions Only

Configuration property descriptions MUST be single-line. Multi-line YAML literal blocks (`|`) or folded blocks (`>`) break the C# comment generation.

```yaml
# BAD: Multi-line description breaks generated C# comments
DefaultMode:
  type: string
  env: MY_SERVICE_DEFAULT_MODE
  default: automatic
  description: |
    Controls the default operating mode.
    Values: automatic, manual, hybrid

# GOOD: Single-line description
DefaultMode:
  type: string
  env: MY_SERVICE_DEFAULT_MODE
  default: automatic
  description: Default operating mode (automatic/manual/hybrid)
```

**Why this matters**: The configuration generator creates C# XML documentation comments from descriptions. Multi-line descriptions produce malformed `<summary>` blocks that cause compile errors.

---

## API Schema Rules

### POST-Only Pattern (MANDATORY)

All internal service APIs use POST requests exclusively. This enables zero-copy WebSocket message routing.

```yaml
# CORRECT: POST-only with body parameters
paths:
  /account/get:
    post:
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetAccountRequest'

# WRONG: Path parameters prevent static GUID mapping
paths:
  /account/{accountId}:
    get:  # NO!
```

**Why POST-only?** WebSocket binary routing uses static 16-byte GUIDs per endpoint. Path parameters (e.g., `/account/{id}`) would require different GUIDs per parameter value, breaking zero-copy routing.

**Exceptions** (browser-facing only):
- Website service (`/website/*`) - SEO, bookmarkable URLs
- OAuth callbacks (`/auth/oauth/{provider}/callback`)
- WebSocket upgrade (`/connect` GET)

### Property Description Requirement (MANDATORY)

**ALL schema properties MUST have `description` fields.** NSwag generates XML documentation from these. Missing descriptions cause CS1591 compiler warnings.

```yaml
# CORRECT: Property has description
properties:
  accountId:
    type: string
    format: uuid
    description: Unique identifier for the account

# WRONG: Missing description causes CS1591 warning
properties:
  accountId:
    type: string
    format: uuid
```

### Servers URL (MANDATORY)

All API schemas MUST use the base endpoint format:

```yaml
servers:
  - url: http://localhost:5012
```

NSwag generates controller route prefixes from this URL.

---

## Event Schema Rules

### Canonical Definitions Only

Each `{service}-events.yaml` file MUST contain ONLY canonical definitions for events that service PUBLISHES. **No `$ref` references to other service event files.**

```yaml
# CORRECT: Canonical definition
components:
  schemas:
    SessionInvalidatedEvent:
      type: object
      required: [sessionIds, reason]
      properties:
        sessionIds:
          type: array
          items: { type: string }

# WRONG: $ref to another service's events (causes duplicate types)
components:
  schemas:
    AccountDeletedEvent:
      $ref: './account-events.yaml#/components/schemas/AccountDeletedEvent'
```

### Topic Naming Convention

**Pattern**: `{entity}.{action}` (kebab-case entity, lowercase action)

| Topic | Description |
|-------|-------------|
| `account.created` | Account lifecycle event |
| `session.invalidated` | Session state change |
| `game-session.player-joined` | Game session event |

**Infrastructure events** use `bannou-` prefix: `bannou.full-service-mappings`

### Client Events vs Service Events

| Type | File | Purpose | Publishing |
|------|------|---------|------------|
| Service Events | `{service}-events.yaml` | Service-to-service | `IMessageBus.PublishAsync` |
| Client Events | `{service}-client-events.yaml` | Server→WebSocket client | `IClientEventPublisher.PublishToSessionAsync` |

**Never use `IMessageBus` for client events** - it uses the wrong RabbitMQ exchange.

---

## Mandatory Type Reuse via $ref (CRITICAL)

**ALL complex types (enums, objects with nested properties, arrays of objects) MUST be defined once in the service's `-api.yaml` schema and referenced via `$ref` from events, configuration, and lifecycle schemas.**

This rule is **inviolable**. Violating it causes:
- Duplicate C# classes with different names (e.g., `AuthMethods`, `AuthMethods2`, `AuthMethods3`)
- Duplicate enums that are incompatible with each other
- Type mismatches across service boundaries
- Serialization failures when event consumers expect the API type

### The Principle

**Events should contain the same complex data types as `Get*Response` from the API.**

If your `GetAccountResponse` returns an `AuthMethodInfo` object, then `AccountCreatedEvent` should use the SAME `AuthMethodInfo` type via `$ref`, not a duplicate inline definition.

### What MUST Use $ref

| Type Category | MUST Use $ref? | Reason |
|---------------|----------------|--------|
| Enums | **YES** | Avoid duplicate incompatible enum types |
| Objects with nested properties | **YES** | Complex types generate duplicate classes |
| Objects with `$ref` to other types | **YES** | Nested refs break NSwag resolution |
| Simple objects (flat properties, no refs) | Recommended | Consistency, easier maintenance |
| Primitive types (string, integer, boolean) | No | Inline is fine |
| Arrays of primitives | No | Inline is fine |
| Arrays of objects | **YES** | The object type must be shared |

### Correct Pattern

```yaml
# In account-api.yaml - DEFINE the type once
components:
  schemas:
    AuthMethodInfo:
      type: object
      properties:
        provider:
          $ref: '#/components/schemas/AuthProvider'  # Nested ref
        linkedAt:
          type: string
          format: date-time

    AuthProvider:
      type: string
      enum: [email, google, discord, twitch, steam]

# In account-events.yaml x-lifecycle - REFERENCE the type
x-lifecycle:
  Account:
    model:
      authMethods:
        type: array
        items:
          $ref: 'account-api.yaml#/components/schemas/AuthMethodInfo'  # Use $ref!

# In account-configuration.yaml - REFERENCE enums
DefaultProvider:
  $ref: 'account-api.yaml#/components/schemas/AuthProvider'
  env: ACCOUNT_DEFAULT_PROVIDER
  default: email
```

### Wrong Pattern

```yaml
# WRONG: Inline definition in events (causes duplicate types)
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
            linkedAt:
              type: string
              format: date-time

# WRONG: Enum defined in events schema (should be in -api.yaml)
# In account-events.yaml components/schemas:
AuthProvider:  # This duplicates the API definition!
  type: string
  enum: [email, google, discord]
```

### When Inline is Acceptable

Only use inline type definitions when:
1. The type is **truly unique to this schema** (not used anywhere else)
2. The type has **no nested `$ref`** references
3. The type is a **simple flat object** with only primitive properties

Even in these cases, consider defining in `-api.yaml` for consistency.

### Generation Pipeline Support

The code generation pipeline fully supports cross-file `$ref`:

| Schema Type | Refs Supported | How It Works |
|-------------|----------------|--------------|
| `*-api.yaml` | `common-api.yaml` | NSwag resolves directly |
| `*-events.yaml` | `*-api.yaml`, `common-api.yaml`, `common-events.yaml` | `resolve-event-refs.py` inlines complex types, excludes from generation |
| `*-lifecycle-events.yaml` | `*-api.yaml`, `common-api.yaml`, `common-events.yaml` | `resolve-event-refs.py` processes lifecycle events, inlines complex types |
| `*-configuration.yaml` | `*-api.yaml`, `common-api.yaml` | `generate-config.sh` extracts type names |

---

## Common Anti-Patterns

### Optional String Without Nullable

```yaml
# BAD: Generates string = default! (hides null at runtime)
optionalProp:
  type: string
  description: This can be omitted

# GOOD: Generates string? (NRT-compliant)
optionalProp:
  type: string
  nullable: true
  description: This can be omitted
```

### Empty String as Default

```yaml
# BAD: Empty string hides missing data
name:
  type: string
  default: ""

# GOOD: Use nullable for optional, required for mandatory
name:
  type: string
  nullable: true  # If optional
# OR
required: [name]  # If mandatory
```

### Response Properties Without Required

```yaml
# BAD: No required array - all properties generate as optional
UserResponse:
  type: object
  properties:
    userId: { type: string, format: uuid }
    email: { type: string }

# GOOD: Server always sets these, so declare them required
UserResponse:
  type: object
  required: [userId, email]
  properties:
    userId: { type: string, format: uuid }
    email: { type: string }
```

### Configuration Without env Property

```yaml
# BAD: No env property - can't be configured via environment
TimeoutSeconds:
  type: integer
  default: 30

# GOOD: Explicit env property
TimeoutSeconds:
  type: integer
  env: MY_SERVICE_TIMEOUT_SECONDS
  default: 30
```

### Duplicate Types Across Schemas (CRITICAL)

**See [Mandatory Type Reuse via $ref](#mandatory-type-reuse-via-ref-critical) for complete guidance.**

```yaml
# BAD: Same enum defined in both API and events
# In my-service-api.yaml
Status:
  type: string
  enum: [PENDING, ACTIVE, COMPLETED]

# In my-service-events.yaml (DUPLICATE!)
Status:
  type: string
  enum: [PENDING, ACTIVE, COMPLETED]

# GOOD: Define in API, reference in events
# In my-service-events.yaml
properties:
  status:
    $ref: 'my-service-api.yaml#/components/schemas/Status'
```

**Also bad: Inline complex types in x-lifecycle**

```yaml
# BAD: Inline object definition in lifecycle (generates AuthMethods, AuthMethods2, AuthMethods3!)
x-lifecycle:
  Account:
    model:
      authMethods:
        type: array
        items:
          type: object  # WRONG - creates duplicate type per event!
          properties:
            provider: { type: string }

# GOOD: Reference API type in lifecycle
x-lifecycle:
  Account:
    model:
      authMethods:
        type: array
        items:
          $ref: 'account-api.yaml#/components/schemas/AuthMethodInfo'  # Correct!
```

---

## Quick Reference Tables

### NRT Quick Reference

| Scenario | Schema Pattern | Generated C# |
|----------|---------------|--------------|
| Required request field | `required: [field]` | `[Required] string Field = default!;` |
| Optional request string | `nullable: true` | `string? Field` |
| Optional request int | `nullable: true` | `int? Field` |
| Int with default | `default: 10` | `int Field = 10;` |
| Bool with default | `default: true` | `bool Field = true;` |
| Response field always set | `required: [field]` | `[Required] string Field = default!;` |
| Response field sometimes null | `nullable: true` | `string? Field` |

### Validation Keywords Quick Reference

| Keyword | Type | Schema Example | Generated Attribute |
|---------|------|----------------|---------------------|
| `minimum` | number | `minimum: 1` | `[ConfigRange(Minimum = 1)]` |
| `maximum` | number | `maximum: 100` | `[ConfigRange(Maximum = 100)]` |
| `exclusiveMinimum` | boolean | `exclusiveMinimum: true` | `[ConfigRange(..., ExclusiveMinimum = true)]` |
| `exclusiveMaximum` | boolean | `exclusiveMaximum: true` | `[ConfigRange(..., ExclusiveMaximum = true)]` |
| `minLength` | integer | `minLength: 32` | `[ConfigStringLength(MinLength = 32)]` |
| `maxLength` | integer | `maxLength: 256` | `[ConfigStringLength(MaxLength = 256)]` |
| `pattern` | regex | `pattern: "^https?://"` | `[ConfigPattern(@"^https?://")]` |
| `multipleOf` | number | `multipleOf: 1000` | `[ConfigMultipleOf(1000)]` |

### $ref Path Quick Reference

All paths are sibling-relative (same directory). Never use `../` prefixes.

| From Schema | To Schema | Path Format |
|-------------|-----------|-------------|
| `*-api.yaml` | `common-api.yaml` | `common-api.yaml#/...` |
| `*-events.yaml` | `*-api.yaml` | `{service}-api.yaml#/...` |
| `*-events.yaml` | `common-events.yaml` | `common-events.yaml#/...` |
| `*-configuration.yaml` | `*-api.yaml` | `{service}-api.yaml#/...` |

---

## Validation Checklist

Before submitting schema changes, verify:

### API Schemas
- [ ] All endpoints use POST method (except documented browser-facing exceptions)
- [ ] All endpoints have `x-permissions` (even if empty array)
- [ ] All properties have `description` fields
- [ ] `servers` URL uses base endpoint format (`http://localhost:5012`)

### NRT Compliance
- [ ] All optional reference types (string, object, array) have `nullable: true`
- [ ] All properties the server always sets are in the `required` array
- [ ] No empty string defaults (`default: ""`)

### Configuration Schemas
- [ ] Every property has an `env` property with proper `{SERVICE}_{PROPERTY}` naming
- [ ] No `type: object` properties (use string with documented format)
- [ ] Optional properties have `nullable: true`
- [ ] Enum types use `$ref` to API schema
- [ ] All descriptions are single-line (no `|` or `>` YAML blocks)

### Event Schemas
- [ ] Only canonical event definitions (no `$ref` to other service events)
- [ ] Lifecycle events use `x-lifecycle`, not manual definitions
- [ ] Event subscriptions use `x-event-subscriptions` in `info` section

### Validation Keywords
- [ ] Numeric bounds use `minimum`/`maximum` (with `exclusive*` if needed)
- [ ] String length constraints use `minLength`/`maxLength`
- [ ] Format patterns use `pattern` with valid regex
- [ ] Precision requirements use `multipleOf`

### Type References (CRITICAL)
- [ ] **ALL enums** use `$ref` to `-api.yaml` definitions (never inline in events/config/lifecycle)
- [ ] **ALL complex objects** (with nested properties or refs) use `$ref` to `-api.yaml`
- [ ] **x-lifecycle model fields** use `$ref` for objects/enums, not inline definitions
- [ ] Event properties matching API response types use the **same type** via `$ref`
- [ ] Shared types defined in `*-api.yaml`, not duplicated across schemas
- [ ] All `$ref` paths are sibling-relative (no `../` prefix in source schemas)

### Final Steps
- [ ] Run `scripts/generate-all-services.sh` and verify build passes
- [ ] Check generated C# files for expected attributes and nullability
