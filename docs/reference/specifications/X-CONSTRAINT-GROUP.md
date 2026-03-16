# x-constraint-group

> **Version**: 0.1
> **Status**: Proposed
> **Last Updated**: 2026-03-16
> **Schema Scope**: `*-configuration.yaml` (both `x-service-configuration` and `x-helper-configurations`)
> **Generated Output**: `[ConfigConstraintGroup]` property attributes, `[ConfigConstraintGroupDefinition]` class attributes, `ValidateConstraintGroups()` startup validation

---

## Summary

Declares cross-property validation constraints for configuration schemas, enabling groups of properties to be validated collectively at startup. Solves the gap where individual property validation keywords (minimum, maximum, pattern, etc.) cannot express relationships between properties, such as weights that must sum to 1.0 or mutually exclusive provider selections. Use when configuration properties have collective invariants that cannot be expressed per-property.

---

## Schema Syntax

### Group Definitions

Groups are defined under `x-constraint-groups`, a sibling of `properties` inside `x-service-configuration` or any entry in `x-helper-configurations`:

```yaml
x-service-configuration:
  x-constraint-groups:
    crafting-stage-weights:
      constraint: sum-equals
      value: 1.0
      tolerance: 0.0001
      description: Stage weights must collectively equal 1.0
    auth-provider:
      constraint: exactly-one
      description: Exactly one authentication provider must be configured
  properties:
    GatheringWeight:
      type: number
      nullable: true
      constraint-group: crafting-stage-weights
      env: CRAFT_GATHERING_WEIGHT
      default: 0.25
      description: Weight allocated to the gathering stage
    # ...
```

### Property Participation

Properties reference their group via `constraint-group: {name}` — a single string. A property may belong to at most one constraint group.

```yaml
properties:
  MysqlConnectionString:
    type: string
    nullable: true
    constraint-group: database-provider
    env: MY_SERVICE_MYSQL_CONNECTION_STRING
    description: MySQL connection string (mutually exclusive with other providers)
  RedisConnectionString:
    type: string
    nullable: true
    constraint-group: database-provider
    env: MY_SERVICE_REDIS_CONNECTION_STRING
    description: Redis connection string (mutually exclusive with other providers)
```

### Helper Configuration Support

Groups in `x-helper-configurations` follow the same syntax. Groups are scoped to their containing configuration block — a group named `weights` in the main config and a group named `weights` in a helper config are independent and do not conflict.

```yaml
x-helper-configurations:
  processing:
    x-constraint-groups:
      stage-weights:
        constraint: sum-equals
        value: 1.0
        description: Processing stage weights must sum to 1.0
    properties:
      PreprocessWeight:
        type: number
        nullable: true
        constraint-group: stage-weights
        env: MY_SERVICE_PROCESSING_PREPROCESS_WEIGHT
        default: 0.5
        description: Weight for preprocessing stage
      PostprocessWeight:
        type: number
        nullable: true
        constraint-group: stage-weights
        env: MY_SERVICE_PROCESSING_POSTPROCESS_WEIGHT
        default: 0.5
        description: Weight for postprocessing stage
```

---

## Field Reference

### Group Definition Fields

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `constraint` | string (enum) | Yes | — | The constraint type (see Constraint Types table) |
| `value` | number | Conditional | — | Target value. Required for `sum-equals`, `sum-minimum`, `sum-maximum`. Forbidden for other constraints. |
| `tolerance` | number | No | `0.0001` | Floating-point comparison tolerance. Only applicable to `sum-equals`. Ignored by other constraints. |
| `description` | string | Yes | — | Human-readable description of why this constraint exists |

### Property Participation Field

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `constraint-group` | string | No | — | Name of the constraint group this property belongs to. Must match a group defined in the same `x-constraint-groups` block. |

### Constraint Types

| Constraint | Value Required | Tolerance Applicable | Description |
|---|---|---|---|
| `exactly-one` | No | No | Exactly one property in the group must be non-null. All participating properties must be nullable. |
| `at-most-one` | No | No | Zero or one property in the group may be non-null. All participating properties must be nullable. |
| `all-or-none` | No | No | Either all properties in the group are non-null, or all are null. All participating properties must be nullable. |
| `sum-equals` | Yes | Yes (default: `0.0001`) | The sum of all participating property values must equal `value` within `tolerance`. All participating properties must be numeric (`type: number` or `type: integer`). Null properties contribute 0 to the sum. |
| `sum-minimum` | Yes | No | The sum of all participating property values must be >= `value`. All participating properties must be numeric. Null properties contribute 0 to the sum. |
| `sum-maximum` | Yes | No | The sum of all participating property values must be <= `value`. All participating properties must be numeric. Null properties contribute 0 to the sum. |

---

## Generated Output

### New Attribute: `ConfigConstraintGroupAttribute`

Location: `bannou-service/Attributes/ConfigConstraintGroupAttribute.cs`

Marks a configuration property as a member of a constraint group.

```csharp
/// <summary>
/// Marks a configuration property as a member of a constraint group.
/// Properties sharing the same group name are validated collectively at startup
/// according to the group's constraint type.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class ConfigConstraintGroupAttribute : Attribute, IServiceAttribute
{
    /// <summary>
    /// The name of the constraint group this property belongs to.
    /// Must match a <see cref="ConfigConstraintGroupDefinitionAttribute"/> on the containing class.
    /// </summary>
    public string GroupName { get; }

    /// <summary>
    /// Creates a constraint group membership attribute.
    /// </summary>
    /// <param name="groupName">The constraint group name.</param>
    public ConfigConstraintGroupAttribute(string groupName) => GroupName = groupName;
}
```

### New Attribute: `ConfigConstraintGroupDefinitionAttribute`

Location: `bannou-service/Attributes/ConfigConstraintGroupDefinitionAttribute.cs`

Declares a constraint group on a configuration class. Applied once per group.

```csharp
/// <summary>
/// Declares a constraint group on a configuration class. At startup, all properties
/// with a matching <see cref="ConfigConstraintGroupAttribute"/> are collected and
/// validated against this group's constraint.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public class ConfigConstraintGroupDefinitionAttribute : Attribute
{
    /// <summary>
    /// The constraint group name. Must match <see cref="ConfigConstraintGroupAttribute.GroupName"/>
    /// on at least two properties in the same class.
    /// </summary>
    public string GroupName { get; }

    /// <summary>
    /// The type of collective constraint to enforce.
    /// </summary>
    public ConstraintGroupType Constraint { get; }

    /// <summary>
    /// Target value for sum constraints. Required for SumEquals, SumMinimum, SumMaximum.
    /// Use double.NaN (default) for non-sum constraints.
    /// </summary>
    public double Value { get; set; } = double.NaN;

    /// <summary>
    /// Floating-point comparison tolerance for SumEquals. Ignored by other constraints.
    /// Default: 0.0001.
    /// </summary>
    public double Tolerance { get; set; } = 0.0001;

    /// <summary>
    /// Creates a constraint group definition.
    /// </summary>
    /// <param name="groupName">The constraint group name.</param>
    /// <param name="constraint">The constraint type.</param>
    public ConfigConstraintGroupDefinitionAttribute(string groupName, ConstraintGroupType constraint)
    {
        GroupName = groupName;
        Constraint = constraint;
    }
}
```

### New Enum: `ConstraintGroupType`

Location: `bannou-service/Attributes/ConstraintGroupType.cs`

```csharp
/// <summary>
/// Types of collective constraints that can be applied to configuration property groups.
/// </summary>
public enum ConstraintGroupType
{
    /// <summary>Exactly one property in the group must be non-null.</summary>
    ExactlyOne,

    /// <summary>Zero or one property in the group may be non-null.</summary>
    AtMostOne,

    /// <summary>Either all properties are non-null, or all are null.</summary>
    AllOrNone,

    /// <summary>Property values must sum to the target value within tolerance.</summary>
    SumEquals,

    /// <summary>Property values must sum to at least the target value.</summary>
    SumMinimum,

    /// <summary>Property values must sum to at most the target value.</summary>
    SumMaximum
}
```

### Generated Configuration Class Output

The config generator emits class-level attributes for group definitions and property-level attributes for membership:

```csharp
[ConfigConstraintGroupDefinition("crafting-stage-weights",
    ConstraintGroupType.SumEquals, Value = 1.0, Tolerance = 0.0001)]
[ConfigConstraintGroupDefinition("auth-provider",
    ConstraintGroupType.ExactlyOne)]
public partial class CraftServiceConfiguration : BaseServiceConfiguration
{
    /// <summary>
    /// Weight allocated to the gathering stage.
    /// Environment variable: CRAFT_GATHERING_WEIGHT
    /// </summary>
    [ConfigConstraintGroup("crafting-stage-weights")]
    public double? GatheringWeight { get; set; } = 0.25;

    /// <summary>
    /// Weight allocated to the refinement stage.
    /// Environment variable: CRAFT_REFINEMENT_WEIGHT
    /// </summary>
    [ConfigConstraintGroup("crafting-stage-weights")]
    public double? RefinementWeight { get; set; } = 0.25;

    /// <summary>
    /// Weight allocated to the assembly stage.
    /// Environment variable: CRAFT_ASSEMBLY_WEIGHT
    /// </summary>
    [ConfigConstraintGroup("crafting-stage-weights")]
    public double? AssemblyWeight { get; set; } = 0.25;

    /// <summary>
    /// Weight allocated to the finishing stage.
    /// Environment variable: CRAFT_FINISHING_WEIGHT
    /// </summary>
    [ConfigConstraintGroup("crafting-stage-weights")]
    public double? FinishingWeight { get; set; } = 0.25;

    /// <summary>
    /// MySQL connection string (mutually exclusive with other providers).
    /// Environment variable: CRAFT_MYSQL_CONNECTION_STRING
    /// </summary>
    [ConfigConstraintGroup("auth-provider")]
    public string? MysqlConnectionString { get; set; }

    /// <summary>
    /// Redis connection string (mutually exclusive with other providers).
    /// Environment variable: CRAFT_REDIS_CONNECTION_STRING
    /// </summary>
    [ConfigConstraintGroup("auth-provider")]
    public string? RedisConnectionString { get; set; }
}
```

---

## Runtime Behavior

### Validation Method

A new `ValidateConstraintGroups()` method is added to the `Validate()` chain in `IServiceConfiguration`:

```csharp
public void Validate()
{
    ValidateNonNullableStrings();
    ValidateNumericRanges();
    ValidateStringLengths();
    ValidatePatterns();
    ValidateMultipleOf();
    ValidateConstraintGroups();  // new
}
```

### Validation Algorithm

1. Scan class for `[ConfigConstraintGroupDefinition]` attributes → build group definitions map
2. Scan properties for `[ConfigConstraintGroup]` attributes → collect property values per group
3. For each group, apply constraint:

| Constraint | Validation Logic |
|---|---|
| `ExactlyOne` | Count non-null properties. Pass if count == 1. |
| `AtMostOne` | Count non-null properties. Pass if count <= 1. |
| `AllOrNone` | Count non-null and null properties. Pass if all non-null or all null. |
| `SumEquals` | Sum numeric values (null = 0). Pass if `Math.Abs(sum - value) <= tolerance`. |
| `SumMinimum` | Sum numeric values (null = 0). Pass if `sum >= value`. |
| `SumMaximum` | Sum numeric values (null = 0). Pass if `sum <= value`. |

### Fail-Fast Behavior

Validation failures throw `InvalidOperationException` at startup, consistent with all other `Config*` validators. The error message includes:
- The configuration class name
- The group name and constraint type
- All participating property names with their current values
- For sum constraints: the computed sum and the target value

### Error Message Format

```
Configuration validation failed for CraftServiceConfiguration:
Constraint group 'crafting-stage-weights' violated (SumEquals, target=1.0, tolerance=0.0001).
Properties: GatheringWeight=0.3, RefinementWeight=0.3, AssemblyWeight=0.3, FinishingWeight=0.3 (sum=1.2).
Check environment variable values or remove overrides to use schema defaults.
```

```
Configuration validation failed for CraftServiceConfiguration:
Constraint group 'auth-provider' violated (ExactlyOne).
Properties: MysqlConnectionString=<set>, RedisConnectionString=<set> (2 set, expected exactly 1).
Check environment variable values to ensure only one provider is configured.
```

### Null Semantics

| Constraint Category | Null Meaning |
|---|---|
| Presence constraints (`exactly-one`, `at-most-one`, `all-or-none`) | Null = "not set" (counts toward absence) |
| Sum constraints (`sum-equals`, `sum-minimum`, `sum-maximum`) | Null contributes 0 to the sum |

---

## Structural Tests

| Test Name | Validates |
|---|---|
| `ConstraintGroup_ReferencesExistingGroup` | Every `constraint-group` value on a property matches a group name defined in `x-constraint-groups` within the same configuration block |
| `ConstraintGroup_HasMinimumTwoMembers` | Every defined group in `x-constraint-groups` has at least 2 properties referencing it |
| `ConstraintGroup_SumConstraintsOnlyOnNumericProperties` | Properties in `sum-equals`, `sum-minimum`, `sum-maximum` groups have `type: number` or `type: integer` |
| `ConstraintGroup_ValueRequiredForSumConstraints` | `sum-*` groups have a `value` field; non-sum groups do not have a `value` field |
| `ConstraintGroup_MutualExclusionOnlyOnNullableProperties` | Properties in `exactly-one`, `at-most-one`, `all-or-none` groups have `nullable: true` |
| `ConstraintGroup_ToleranceOnlyOnSumEquals` | Only `sum-equals` groups may specify `tolerance`; other groups must not |
| `ConstraintGroup_DescriptionRequired` | Every group definition has a non-empty `description` field |

---

## Examples

### Example 1: Crafting Stage Weights (sum-equals)

Four weight properties that must collectively equal 1.0, controlling how crafting time is distributed across recipe stages.

**Schema** (`craft-configuration.yaml`):
```yaml
x-service-configuration:
  x-constraint-groups:
    stage-weights:
      constraint: sum-equals
      value: 1.0
      description: Crafting stage time distribution weights must sum to 1.0
  properties:
    GatheringWeight:
      type: number
      nullable: true
      constraint-group: stage-weights
      env: CRAFT_GATHERING_WEIGHT
      default: 0.25
      description: Proportion of crafting time spent in gathering stage
    RefinementWeight:
      type: number
      nullable: true
      constraint-group: stage-weights
      env: CRAFT_REFINEMENT_WEIGHT
      default: 0.25
      description: Proportion of crafting time spent in refinement stage
    AssemblyWeight:
      type: number
      nullable: true
      constraint-group: stage-weights
      env: CRAFT_ASSEMBLY_WEIGHT
      default: 0.25
      description: Proportion of crafting time spent in assembly stage
    FinishingWeight:
      type: number
      nullable: true
      constraint-group: stage-weights
      env: CRAFT_FINISHING_WEIGHT
      default: 0.25
      description: Proportion of crafting time spent in finishing stage
```

**Startup behavior**: With defaults, sum = 0.25 + 0.25 + 0.25 + 0.25 = 1.0 → passes. If an operator overrides `CRAFT_GATHERING_WEIGHT=0.5` without adjusting others, sum = 0.5 + 0.25 + 0.25 + 0.25 = 1.25 → startup fails with descriptive error.

### Example 2: Mutually Exclusive Search Backend (exactly-one)

A service that supports exactly one search backend — Elasticsearch, Meilisearch, or Redis Search — configured via connection strings.

**Schema** (`documentation-configuration.yaml`):
```yaml
x-service-configuration:
  x-constraint-groups:
    search-backend:
      constraint: exactly-one
      description: Exactly one search backend must be configured
  properties:
    ElasticsearchUrl:
      type: string
      nullable: true
      constraint-group: search-backend
      env: DOCUMENTATION_ELASTICSEARCH_URL
      description: Elasticsearch connection URL
    MeilisearchUrl:
      type: string
      nullable: true
      constraint-group: search-backend
      env: DOCUMENTATION_MEILISEARCH_URL
      description: Meilisearch connection URL
    RedisSearchConnectionString:
      type: string
      nullable: true
      constraint-group: search-backend
      env: DOCUMENTATION_REDIS_SEARCH_CONNECTION_STRING
      description: Redis Search connection string
```

**Startup behavior**: Exactly one of the three must be non-null. Zero set → startup fails ("0 set, expected exactly 1"). Two or more set → startup fails ("2 set, expected exactly 1").

### Example 3: Co-Dependent TLS Configuration (all-or-none)

TLS certificate and key must both be provided, or neither.

**Schema** (`mesh-configuration.yaml`):
```yaml
x-service-configuration:
  x-constraint-groups:
    tls-config:
      constraint: all-or-none
      description: TLS certificate and key must both be provided or both be absent
  properties:
    TlsCertificatePath:
      type: string
      nullable: true
      constraint-group: tls-config
      env: MESH_TLS_CERTIFICATE_PATH
      description: Path to TLS certificate file
    TlsKeyPath:
      type: string
      nullable: true
      constraint-group: tls-config
      env: MESH_TLS_KEY_PATH
      description: Path to TLS private key file
```

**Startup behavior**: Both set → passes. Neither set → passes (TLS disabled). One set, other null → startup fails with descriptive error.

---

## Edge Cases & Restrictions

### Forbidden Combinations

| Restriction | Reason |
|---|---|
| `constraint-group` on a non-nullable property in a presence group | `exactly-one`, `at-most-one`, `all-or-none` require nullable properties — a non-nullable property is always "set" and defeats the constraint |
| `value` field on non-sum constraints | `exactly-one`, `at-most-one`, `all-or-none` have fixed semantics; a target value is meaningless |
| `tolerance` on non-`sum-equals` constraints | Only floating-point equality needs tolerance; inequality comparisons (`>=`, `<=`) do not |
| Non-numeric property in a sum constraint group | `sum-equals`, `sum-minimum`, `sum-maximum` require numeric values to sum |
| Fewer than 2 properties in a group | A constraint over a single property is a per-property constraint; use `ConfigRange` or `nullable: false` instead |
| `constraint-group` referencing a group in a different config block | Groups are scoped to their config block (main `x-service-configuration` or a specific `x-helper-configurations` entry). Cross-block references are forbidden. |

### Scoping Rules

- **Groups are scoped to their configuration block.** A group in `x-service-configuration` is invisible to `x-helper-configurations` and vice versa. Two helper configs may each define a group with the same name without conflict.
- **Properties may belong to at most one group.** The `constraint-group` field is a single string, not an array. If a property needs to participate in multiple collective constraints, reconsider the configuration modeling — this likely indicates properties are overloaded.
- **Groups do not span configuration classes.** Each generated config class validates its own groups independently. A main config group cannot include helper config properties.

### Interaction with Other Validation

Constraint group validation runs **after** all per-property validations (`ValidateNumericRanges`, `ValidateStringLengths`, etc.). A property that violates both a `ConfigRange` and a `ConfigConstraintGroup` will report the per-property violation first. This ordering is intentional — per-property errors are simpler to diagnose and fix, and fixing them may resolve group violations as a side effect.

### Schema-Level vs Runtime Validation

The structural tests validate schema correctness (group references exist, types are compatible). The runtime `ValidateConstraintGroups()` validates actual property values at startup. Both layers are required — schema validation catches authoring errors at generation time; runtime validation catches deployment configuration errors at startup.
