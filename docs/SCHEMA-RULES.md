# Bannou Schema Rules

> **MANDATORY**: AI agents MUST review this document before creating or modifying ANY OpenAPI schema file (`*-api.yaml`, `*-events.yaml`, `*-configuration.yaml`).

This document covers all schema authoring rules for Bannou, including NRT compliance, validation keywords, type references, and common pitfalls.

---

## Table of Contents

1. [NRT (Nullable Reference Types) Rules](#nrt-nullable-reference-types-rules)
2. [Schema Reference Hierarchy ($ref)](#schema-reference-hierarchy-ref)
3. [Validation Keywords](#validation-keywords)
4. [Configuration Schema Rules](#configuration-schema-rules)
5. [Common Anti-Patterns](#common-anti-patterns)
6. [Quick Reference Tables](#quick-reference-tables)
7. [Validation Checklist](#validation-checklist)

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

| Source Schema | Can Reference | Path Format |
|--------------|---------------|-------------|
| `*-api.yaml` | `common-api.yaml` | `common-api.yaml#/...` |
| `*-events.yaml` | Same service's `-api.yaml`, `common-api.yaml`, `common-events.yaml` | `../{service}-api.yaml#/...` (needs `../` prefix) |
| `*-configuration.yaml` | Same service's `-api.yaml`, `common-api.yaml` | `{service}-api.yaml#/...` |
| `*-lifecycle-events.yaml` | Same service's `-api.yaml`, `common-api.yaml` | `../{service}-api.yaml#/...` (needs `../` prefix - preprocessed into Generated/) |

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

### Example: Events Referencing API Type (with ../ prefix)

```yaml
# In actor-events.yaml (events are processed differently, need ../ prefix)
properties:
  status:
    $ref: '../actor-api.yaml#/components/schemas/ActorStatus'
```

### Why `../` Prefix for Events?

Lifecycle events and some event schemas are preprocessed into the `Generated/` directory. From that location, the relative path back to the main schemas directory requires `../`.

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

### Duplicate Types Across Schemas

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
    $ref: '../my-service-api.yaml#/components/schemas/Status'
```

### Wrong $ref Path for Events

```yaml
# BAD: Missing ../ prefix in events schema
$ref: 'my-service-api.yaml#/components/schemas/MyType'

# GOOD: Events need ../ prefix (processed from Generated/ directory)
$ref: '../my-service-api.yaml#/components/schemas/MyType'
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

| From Schema | To Schema | Path Format |
|-------------|-----------|-------------|
| `*-api.yaml` | `common-api.yaml` | `common-api.yaml#/...` |
| `*-events.yaml` | `*-api.yaml` | `../{service}-api.yaml#/...` |
| `*-events.yaml` | `common-api.yaml` | `../common-api.yaml#/...` |
| `*-events.yaml` | `common-events.yaml` | `common-events.yaml#/...` |
| `*-configuration.yaml` | `*-api.yaml` | `{service}-api.yaml#/...` |

---

## Validation Checklist

Before submitting schema changes, verify:

### NRT Compliance
- [ ] All optional reference types (string, object, array) have `nullable: true`
- [ ] All properties the server always sets are in the `required` array
- [ ] No empty string defaults (`default: ""`)

### Configuration Schemas
- [ ] Every property has an `env` property with proper `{SERVICE}_{PROPERTY}` naming
- [ ] No `type: object` properties (use string with documented format)
- [ ] Optional properties have `nullable: true`
- [ ] Enum types use `$ref` to API schema

### Validation Keywords
- [ ] Numeric bounds use `minimum`/`maximum` (with `exclusive*` if needed)
- [ ] String length constraints use `minLength`/`maxLength`
- [ ] Format patterns use `pattern` with valid regex
- [ ] Precision requirements use `multipleOf`

### Type References
- [ ] Shared types defined in `*-api.yaml`, not duplicated across schemas
- [ ] Events use `../` prefix when referencing API schemas
- [ ] Configuration uses direct path (no `../`) when referencing API schemas

### Final Steps
- [ ] Run `scripts/generate-all-services.sh` and verify build passes
- [ ] Check generated C# files for expected attributes and nullability
