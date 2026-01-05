# NRT (Nullable Reference Types) Schema Rules

> **MANDATORY**: AI agents MUST review this document before creating or modifying ANY OpenAPI schema file.

All Bannou projects have `<Nullable>enable</Nullable>`. NSwag generates C# code with `/generateNullableReferenceTypes:true`. This means **schema definitions DIRECTLY control C# nullability**.

Incorrect schema definitions cause:
- `string = default!;` for optional properties (hides null at runtime)
- Missing `[Required]` attributes on properties the server always sets
- NRT violations that only surface at runtime as NullReferenceExceptions

---

## API Schema Rules (`*-api.yaml`)

### Rule 1: Required Properties → `required` Array

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

### Rule 2: Optional Reference Types → `nullable: true`

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

### Rule 3: Value Types with Defaults → `default: value`

Boolean/integer properties with sensible defaults just need `default: value`.

```yaml
properties:
  pageSize:
    type: integer
    default: 20
    description: Number of items per page
```

**NSwag generates**: `public int PageSize { get; set; } = 20;`

### Rule 4: Response Properties Server Always Sets → `required` Array

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

---

## Event Schema Rules (`*-events.yaml`)

### Rule 1: Required Event Properties → `required` Array

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

### Rule 2: Optional Event Properties → `nullable: true`

Properties that MAY be absent MUST have `nullable: true`.

```yaml
properties:
  deletedReason:
    type: string
    nullable: true
    description: Optional reason for deletion
```

### Rule 3: Value Types → Never Nullable Unless Semantically Optional

Boolean/integer properties are value types. Only mark `nullable: true` if the absence of a value is semantically meaningful (rare for events).

---

## Configuration Schema Rules (`*-configuration.yaml`)

### Rule 1: Properties WITH Defaults → Use `default: value`

Most configuration has sensible defaults.

```yaml
properties:
  MaxConnections:
    type: integer
    default: 100
    description: Maximum concurrent connections
```

**NSwag generates**: `public int MaxConnections { get; set; } = 100;`

### Rule 2: REQUIRED Configuration WITHOUT Defaults → Fail Fast

Configuration that MUST be provided (secrets, connection strings) should NOT have defaults. The service MUST validate at startup and throw if missing.

```yaml
properties:
  JwtSecret:
    type: string
    description: JWT signing secret (REQUIRED - no default, service fails fast if missing)
```

The service implementation must validate:
```csharp
_jwtSecret = config.JwtSecret
    ?? throw new InvalidOperationException("AUTH_JWT_SECRET is required");
```

### Rule 3: OPTIONAL Configuration → `nullable: true` + No Default

Configuration that is truly optional should be marked `nullable: true`.

```yaml
properties:
  ProxyUrl:
    type: string
    nullable: true
    description: Optional HTTP proxy URL
```

---

## Anti-Patterns (NEVER Do These)

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

---

## Quick Reference Table

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

- [ ] All optional reference types (string, object, array) have `nullable: true`
- [ ] All properties the server always sets are in the `required` array
- [ ] No empty string defaults (`default: ""`)
- [ ] Configuration without defaults has fail-fast validation in service code
- [ ] Run `scripts/generate-all-services.sh <service>` and verify build passes
