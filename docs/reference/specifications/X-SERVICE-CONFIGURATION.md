# x-service-configuration / x-helper-configurations

> **Version**: 1.0
> **Status**: Implemented
> **Last Updated**: 2026-03-16
> **Schema Scope**: `*-configuration.yaml`
> **Generated Output**: `{Service}ServiceConfiguration.cs` (main config class), `{Service}{Helper}Configuration.cs` (per helper config class)
> **Related Specifications**: [x-constraint-group](X-CONSTRAINT-GROUP.md)

---

## Summary

Defines typed configuration properties for services, generating strongly-typed configuration classes with environment variable binding, startup validation, and schema-defined defaults. Supports both main service configuration and helper sub-service configurations within the same schema file, each with their own environment variable prefix and generated class. Use when a service or its helper sub-services need configurable properties bound from environment variables with compile-time type safety and fail-fast startup validation.

---

## Schema Syntax

### Main Service Configuration (x-service-configuration)

Defined at the top level of `{service}-configuration.yaml`:

```yaml
x-service-configuration:
  properties:
    DeploymentMode:
      $ref: 'actor-api.yaml#/components/schemas/ActorDeploymentMode'
      env: ACTOR_DEPLOYMENT_MODE
      default: bannou
      description: "Actor deployment mode: bannou (local dev), pool-per-type, shared-pool, or auto-scale"
    PoolNodeId:
      type: string
      env: ACTOR_POOL_NODE_ID
      nullable: true
      description: If set, this instance runs as a pool node. Unique identifier for this node.
    MaxActorsPerNode:
      type: integer
      env: ACTOR_MAX_ACTORS_PER_NODE
      default: 100
      minimum: 1
      description: Maximum actors per pool node
```

### Helper Configurations (x-helper-configurations)

Defined alongside `x-service-configuration` in the same file. Each entry generates a separate configuration class with a `{SERVICE}_{HELPER}_` environment variable prefix:

```yaml
x-service-configuration:
  properties:
    MainProperty:
      type: string
      env: STATE_MAIN_PROPERTY
      default: "value"
      description: A main service property

x-helper-configurations:
  redis:
    properties:
      ConnectionString:
        type: string
        env: STATE_REDIS_CONNECTION_STRING
        nullable: true
        description: Redis connection string
      DatabaseIndex:
        type: integer
        env: STATE_REDIS_DATABASE_INDEX
        default: 0
        minimum: 0
        maximum: 15
        description: Redis database index
  mysql:
    properties:
      ConnectionString:
        type: string
        env: STATE_MYSQL_CONNECTION_STRING
        nullable: true
        description: MySQL connection string
```

### Property Types

Properties support these type declarations:

```yaml
# Primitive types
PropertyName:
  type: string | integer | number | boolean
  env: SERVICE_PROPERTY_NAME
  description: Required description

# Enum types via $ref
PropertyName:
  $ref: 'service-api.yaml#/components/schemas/EnumType'
  env: SERVICE_PROPERTY_NAME
  default: DefaultValue
  description: Required description

# Nullable (optional) properties
PropertyName:
  type: string
  env: SERVICE_PROPERTY_NAME
  nullable: true
  description: Required description
```

---

## Field Reference

### Property Fields

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `type` | string | Conditional | — | Property type: `string`, `integer`, `number`, `boolean`. Required unless using `$ref`. |
| `$ref` | string | Conditional | — | Reference to enum type in API schema. Mutually exclusive with `type`. |
| `env` | string | Yes | — | Environment variable name. Must follow `{SERVICE}_{PROPERTY}` naming. |
| `default` | varies | No | — | Default value when environment variable is not set. |
| `nullable` | boolean | No | `false` | When true, property is optional (may be null). |
| `description` | string | Yes | — | Single-line human-readable description. |
| `minimum` | number | No | — | Minimum value (integer/number types only). |
| `maximum` | number | No | — | Maximum value (integer/number types only). |
| `minLength` | integer | No | — | Minimum string length (string type only). |
| `maxLength` | integer | No | — | Maximum string length (string type only). |
| `pattern` | string | No | — | Regex pattern (string type only). |
| `multipleOf` | number | No | — | Value must be a multiple of this (integer/number types only). |
| `constraint-group` | string | No | — | Constraint group membership (see [x-constraint-group](X-CONSTRAINT-GROUP.md)). |

### Environment Variable Naming Rules

| Rule | Example | Violation |
|------|---------|-----------|
| Must use `{SERVICE}_{PROPERTY}` prefix | `ACTOR_DEPLOYMENT_MODE` | `DEPLOYMENT_MODE` (missing service prefix) |
| No hyphens in env var names | `GAME_SESSION_MAX_PLAYERS` | `GAME-SESSION_MAX_PLAYERS` |
| Underscores separate words | `ACTOR_POOL_NODE_ID` | `ACTOR_POOLNODEID` |
| Helper configs use `{SERVICE}_{HELPER}_` prefix | `STATE_REDIS_CONNECTION_STRING` | `REDIS_CONNECTION_STRING` |

---

## Generated Output

### Main Configuration Class

Generated to `plugins/lib-{service}/Generated/{Service}ServiceConfiguration.cs`:

```csharp
[ServiceConfiguration(typeof(ActorService))]
public class ActorServiceConfiguration : BaseServiceConfiguration
{
    /// <summary>
    /// Actor deployment mode: bannou (local dev), pool-per-type, shared-pool, or auto-scale
    /// Environment variable: ACTOR_DEPLOYMENT_MODE
    /// </summary>
    public ActorDeploymentMode DeploymentMode { get; set; } = ActorDeploymentMode.Bannou;

    /// <summary>
    /// If set, this instance runs as a pool node. Unique identifier for this node.
    /// Environment variable: ACTOR_POOL_NODE_ID
    /// </summary>
    public string? PoolNodeId { get; set; }

    /// <summary>
    /// Maximum actors per pool node
    /// Environment variable: ACTOR_MAX_ACTORS_PER_NODE
    /// </summary>
    [ConfigRange(Minimum = 1)]
    public int MaxActorsPerNode { get; set; } = 100;
}
```

Key generation rules:
- `[ServiceConfiguration(typeof(ServiceType))]` attribute binds the class to its service
- Extends `BaseServiceConfiguration` for shared validation infrastructure
- Non-nullable string properties without defaults generate as `string` (fail-fast if not set)
- `nullable: true` generates as nullable type (`string?`, `int?`, etc.)
- `minimum`/`maximum` generate `[ConfigRange]` attributes
- `minLength`/`maxLength` generate `[ConfigStringLength]` attributes
- `pattern` generates `[ConfigPattern]` attributes
- `multipleOf` generates `[ConfigMultipleOf]` attributes
- `$ref` enum types generate the enum type directly with default assignment

### Helper Configuration Classes

Each helper entry generates a separate class to `plugins/lib-{service}/Generated/{Service}{Helper}Configuration.cs`:

```csharp
[ServiceConfiguration("StateRedisConfiguration")]
public class StateRedisConfiguration : BaseServiceConfiguration
{
    /// <summary>
    /// Redis connection string
    /// Environment variable: STATE_REDIS_CONNECTION_STRING
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Redis database index
    /// Environment variable: STATE_REDIS_DATABASE_INDEX
    /// </summary>
    [ConfigRange(Minimum = 0, Maximum = 15)]
    public int DatabaseIndex { get; set; } = 0;
}
```

Helper configurations:
- Use string-based `[ServiceConfiguration("ClassName")]` constructor for lazy type resolution
- Extend `BaseServiceConfiguration` with the same validation support as main configs
- Each helper gets its own `{SERVICE}_{HELPER}_` environment variable prefix
- Support the same property types, defaults, validation keywords, and constraint groups as main configs

---

## Runtime Behavior

### Environment Variable Binding

At startup, the configuration system:
1. Loads `.env` files from the current directory or parent directory (via DotNetEnv)
2. For each property, reads the environment variable specified by `env`
3. If the variable is not set and a `default` exists, uses the default
4. If the variable is not set, no default exists, and the property is not nullable, startup fails

### Startup Validation

The `Validate()` method on `BaseServiceConfiguration` runs at startup and checks:
1. **Non-nullable strings**: Properties without `nullable: true` and without defaults must have values
2. **Numeric ranges**: Properties with `minimum`/`maximum` must be within bounds
3. **String lengths**: Properties with `minLength`/`maxLength` must satisfy constraints
4. **Patterns**: Properties with `pattern` must match the regex
5. **MultipleOf**: Properties with `multipleOf` must be exact multiples
6. **Constraint groups**: Cross-property constraints (see [x-constraint-group](X-CONSTRAINT-GROUP.md))

All validation failures throw `InvalidOperationException` with descriptive messages at startup (fail-fast).

### Edge Cases

- **Null on non-nullable**: Missing env var with no default on a non-nullable property causes startup failure
- **Type mismatches**: Non-numeric string in an integer env var causes startup failure
- **Empty string**: An empty env var on a non-nullable string property is a valid value (not null)
- **Enum values**: Enum binding uses case-insensitive matching via BannouJson serialization conventions

---

## Structural Tests

| Test Name | Validates |
|-----------|-----------|
| `ServiceConfiguration_HasValidConstructor` | Every generated configuration class has a valid parameterless constructor |
| `ServiceConfiguration_ExtendsBaseServiceConfiguration` | All configuration classes extend `BaseServiceConfiguration` |
| `ServiceConfiguration_PropertiesHaveDescriptions` | Every property in configuration schemas has a `description` field |
| `ServiceConfiguration_PropertiesHaveEnvVars` | Every property has an `env` field with proper `{SERVICE}_{PROPERTY}` naming |
| `ServiceConfiguration_NoHyphensInEnvVars` | No environment variable names contain hyphens |
| `ServiceConfiguration_NoObjectType` | No configuration properties use `type: object` |

---

## Examples

### Example 1: Actor Service Configuration

A service with enum, string, integer, and nullable properties.

**Schema** (`actor-configuration.yaml`):
```yaml
x-service-configuration:
  properties:
    DeploymentMode:
      $ref: 'actor-api.yaml#/components/schemas/ActorDeploymentMode'
      env: ACTOR_DEPLOYMENT_MODE
      default: bannou
      description: "Actor deployment mode: bannou (local dev), pool-per-type, shared-pool, or auto-scale"
    PoolNodeId:
      type: string
      env: ACTOR_POOL_NODE_ID
      nullable: true
      description: If set, this instance runs as a pool node. Unique identifier for this node.
    PoolNodeCapacity:
      type: integer
      env: ACTOR_POOL_NODE_CAPACITY
      default: 100
      minimum: 1
      description: Maximum actors this pool node can run
```

**Generated output** (`ActorServiceConfiguration.cs`):
```csharp
[ServiceConfiguration(typeof(ActorService))]
public class ActorServiceConfiguration : BaseServiceConfiguration
{
    /// <summary>
    /// Actor deployment mode: bannou (local dev), pool-per-type, shared-pool, or auto-scale
    /// Environment variable: ACTOR_DEPLOYMENT_MODE
    /// </summary>
    public ActorDeploymentMode DeploymentMode { get; set; } = ActorDeploymentMode.Bannou;

    /// <summary>
    /// If set, this instance runs as a pool node. Unique identifier for this node.
    /// Environment variable: ACTOR_POOL_NODE_ID
    /// </summary>
    public string? PoolNodeId { get; set; }

    /// <summary>
    /// Maximum actors this pool node can run
    /// Environment variable: ACTOR_POOL_NODE_CAPACITY
    /// </summary>
    [ConfigRange(Minimum = 1)]
    public int PoolNodeCapacity { get; set; } = 100;
}
```

### Example 2: State Service with Helper Configurations

A service with both main and helper configurations for multiple storage backends.

**Schema** (`state-configuration.yaml`):
```yaml
x-service-configuration:
  properties:
    DefaultBackend:
      type: string
      env: STATE_DEFAULT_BACKEND
      default: "redis"
      description: Default state store backend

x-helper-configurations:
  redis:
    properties:
      ConnectionString:
        type: string
        env: STATE_REDIS_CONNECTION_STRING
        nullable: true
        description: Redis connection string
      DatabaseIndex:
        type: integer
        env: STATE_REDIS_DATABASE_INDEX
        default: 0
        minimum: 0
        maximum: 15
        description: Redis database index
  mysql:
    properties:
      ConnectionString:
        type: string
        env: STATE_MYSQL_CONNECTION_STRING
        nullable: true
        description: MySQL connection string
```

**Generated output** — main class (`StateServiceConfiguration.cs`):
```csharp
[ServiceConfiguration(typeof(StateService))]
public class StateServiceConfiguration : BaseServiceConfiguration
{
    /// <summary>
    /// Default state store backend
    /// Environment variable: STATE_DEFAULT_BACKEND
    /// </summary>
    public string DefaultBackend { get; set; } = "redis";
}
```

**Generated output** — helper class (`StateRedisConfiguration.cs`):
```csharp
[ServiceConfiguration("StateRedisConfiguration")]
public class StateRedisConfiguration : BaseServiceConfiguration
{
    /// <summary>
    /// Redis connection string
    /// Environment variable: STATE_REDIS_CONNECTION_STRING
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Redis database index
    /// Environment variable: STATE_REDIS_DATABASE_INDEX
    /// </summary>
    [ConfigRange(Minimum = 0, Maximum = 15)]
    public int DatabaseIndex { get; set; } = 0;
}
```

---

## Edge Cases & Restrictions

### Forbidden Combinations

| Restriction | Reason |
|---|---|
| `type: object` on any property | Configuration properties must be scalar or enum types; structured objects are not supported |
| Missing `env` on any property | Every property must have an explicit environment variable binding |
| Hyphens in `env` values | Environment variable names cannot contain hyphens; use underscores |
| `$ref` to non-enum schema | Only enum types may be referenced; complex object types are forbidden |
| Multi-line `description` | Descriptions must be single-line for XML doc generation |
| Missing service prefix in `env` | All env vars must start with `{SERVICE}_` to avoid naming collisions across services |
| Comma-delimited string for structured data | Define individual typed properties or use `$ref` enum with array items in the schema |

### Scoping Rules

- `x-service-configuration` and `x-helper-configurations` are scoped to a single `*-configuration.yaml` file
- Each file generates one main configuration class and zero or more helper configuration classes
- Helper configurations do not inherit properties from the main configuration
- Both main and helper configurations independently support `x-constraint-groups` (see [x-constraint-group](X-CONSTRAINT-GROUP.md))
- Constraint groups are scoped to their containing configuration block and do not span across main/helper boundaries

### Interaction with Other Extension Attributes

- **x-constraint-group**: Defined within `x-service-configuration` or `x-helper-configurations` to add cross-property validation (see [x-constraint-group](X-CONSTRAINT-GROUP.md))
- Properties may reference `$ref` types from `*-api.yaml` schemas for enum bindings
- Configuration classes are referenced by `[ServiceConfiguration]` on the service class for DI registration

### Known Limitations

- No support for array or list properties — use comma-delimited strings with manual parsing or define individual properties
- No support for nested object properties — flatten into scalar properties with naming prefixes
- Helper configuration class names are derived from the helper key name (PascalCase), so helper keys must be valid C# identifier fragments
