# x-sdk-type

> **Version**: 1.0
> **Status**: Implemented
> **Last Updated**: 2026-03-16
> **Schema Scope**: `*-api.yaml`
> **Generated Output**: NSwag exclusion lists, SDK namespace `using` statements in generated model and configuration code

---

## Summary

Maps an OpenAPI schema type to an existing C# type from the Core SDK, preventing NSwag from generating a duplicate class and instead referencing the SDK type directly in all generated code. Restricted to BeyondImmersion.Bannou.Core types only; domain-specific SDK types must use explicit mapping at the plugin boundary.

---

## Schema Syntax

### Basic Usage

Apply `x-sdk-type` to a schema definition in `*-api.yaml`, providing the fully qualified C# type name:

```yaml
# music-api.yaml
components:
  schemas:
    PitchClass:
      type: string
      x-sdk-type: BeyondImmersion.Bannou.MusicTheory.Pitch.PitchClass
      description: A pitch class from the Core SDK.
      enum:
        - C
        - CSharp
        - D
        # ...
```

### Referenced in Other Schemas

Other schemas that `$ref` the SDK-typed schema automatically use the SDK type in generated code — no additional annotation is needed on the referencing schema:

```yaml
components:
  schemas:
    CompositionRequest:
      type: object
      properties:
        rootPitch:
          $ref: '#/components/schemas/PitchClass'
          description: Root pitch class for the composition
```

---

## Field Reference

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `x-sdk-type` | string | Yes | — | Fully qualified C# type name from the Core SDK (e.g., `BeyondImmersion.Bannou.MusicTheory.Pitch.PitchClass`). Must include full namespace. |

### Value Format

The value must be a fully qualified .NET type name with namespace:

| Component | Example |
|---|---|
| Namespace | `BeyondImmersion.Bannou.MusicTheory.Pitch` |
| Type name | `PitchClass` |
| Full value | `BeyondImmersion.Bannou.MusicTheory.Pitch.PitchClass` |

The namespace is extracted automatically (everything before the last `.`) and added as a `using` statement in generated files.

---

## Generated Output

### Extraction Script

`scripts/extract-sdk-types.py` scans a schema file for `x-sdk-type` markers and outputs two artifacts:

1. **Exclusion list**: Schema type names to exclude from NSwag generation (the OpenAPI schema name, not the C# type name)
2. **SDK namespaces**: Unique namespaces extracted from the fully qualified type names, for `using` statements

```bash
# Usage
python3 scripts/extract-sdk-types.py ../schemas/music-api.yaml --format=shell
```

### Model Generation

`scripts/generate-models.sh` consumes the extraction output:

1. Calls `extract-sdk-types.py` to get exclusion lists and namespaces
2. Passes excluded type names to NSwag's `excludedTypeNames` parameter, preventing duplicate class generation
3. Injects SDK namespace `using` statements into the generated `{Service}Models.cs` file

The generated models file references the SDK type directly:

```csharp
// bannou-service/Generated/Models/MusicModels.cs (generated — do not edit)
using BeyondImmersion.Bannou.MusicTheory.Pitch;

// PitchClass is NOT generated here — it comes from the SDK namespace above.
// Other schemas referencing PitchClass use the SDK type directly:

public partial class CompositionRequest
{
    [JsonPropertyName("rootPitch")]
    public PitchClass RootPitch { get; set; }
}
```

### Configuration Generation

`scripts/generate-config.sh` also consumes `x-sdk-type` markers when configuration properties reference SDK-typed schemas via `$ref`. The same exclusion and namespace injection pattern applies to generated configuration classes.

---

## Runtime Behavior

`x-sdk-type` has no runtime behavior. It is a purely generation-time directive that controls NSwag code generation output. At runtime, the SDK types are resolved through normal .NET assembly loading from the project's package references.

### Build Requirements

The plugin project must have a package or project reference to the assembly containing the SDK type. If the reference is missing, the generated code will fail to compile with a standard C# namespace/type resolution error.

---

## Structural Tests

No dedicated structural tests exist for `x-sdk-type`. Correctness is enforced at build time: if `x-sdk-type` references a type that does not exist in the referenced SDK assembly, or if the exclusion list is misconfigured, the build fails with a compile error.

---

## Examples

### Example 1: Music Theory Pitch Class

A schema type that maps directly to an existing enum in the MusicTheory SDK.

**Schema** (`music-api.yaml`):
```yaml
components:
  schemas:
    PitchClass:
      type: string
      x-sdk-type: BeyondImmersion.Bannou.MusicTheory.Pitch.PitchClass
      description: Chromatic pitch class (C through B).
      enum:
        - C
        - CSharp
        - D
        - DSharp
        - E
        - F
        - FSharp
        - G
        - GSharp
        - A
        - ASharp
        - B

    TranspositionRequest:
      type: object
      additionalProperties: false
      required:
        - pitch
        - semitones
      properties:
        pitch:
          $ref: '#/components/schemas/PitchClass'
          description: The pitch class to transpose
        semitones:
          type: integer
          description: Number of semitones to transpose (positive or negative)
```

**Generation behavior**: `extract-sdk-types.py` identifies `PitchClass` as an SDK type, excludes it from NSwag generation, and adds `using BeyondImmersion.Bannou.MusicTheory.Pitch;` to the generated models file. `TranspositionRequest` is generated normally but its `Pitch` property uses the SDK `PitchClass` type.

### Example 2: SDK Type in Configuration

A configuration property that references an SDK-typed schema for a typed enum setting.

**Schema** (`music-api.yaml` + `music-configuration.yaml`):
```yaml
# music-api.yaml
components:
  schemas:
    ScaleType:
      type: string
      x-sdk-type: BeyondImmersion.Bannou.MusicTheory.Scales.ScaleType
      description: Scale type from the MusicTheory SDK.
      enum:
        - Major
        - Minor
        - Dorian
        - Mixolydian
```

```yaml
# music-configuration.yaml
x-service-configuration:
  properties:
    DefaultScaleType:
      $ref: 'music-api.yaml#/components/schemas/ScaleType'
      env: MUSIC_DEFAULT_SCALE_TYPE
      default: Major
      description: Default scale type for procedural composition
```

**Generation behavior**: `generate-config.sh` detects the `$ref` target has `x-sdk-type`, adds the SDK namespace `using` statement to the generated configuration class, and uses the SDK type for the property instead of generating a duplicate enum.

---

## Edge Cases & Restrictions

### Forbidden Combinations

| Restriction | Reason |
|---|---|
| `x-sdk-type` in `*-events.yaml` or `*-client-events.yaml` | SDK type mapping applies only to API schema types; event schemas have separate generation pipelines |
| `x-sdk-type` in `*-configuration.yaml` | Configuration schemas reference SDK types via `$ref` to the `*-api.yaml` definition; the marker belongs on the source definition |
| `x-sdk-type` referencing domain-specific SDK types outside Core | Domain-specific SDKs (e.g., behavior-compiler, storyline-theory) must define their own types and map at the plugin boundary using explicit A2 SDK boundary enum mapping with `EnumMappingValidator` tests |
| `x-sdk-type` without the full namespace | The value must be fully qualified; a bare type name (`PitchClass`) cannot be resolved to the correct `using` statement |

### Scoping Rules

- `x-sdk-type` applies only to top-level schema definitions under `components/schemas` in `*-api.yaml` files.
- The attribute does not propagate through `$ref`. Only the schema where `x-sdk-type` is defined is excluded from generation; schemas referencing it via `$ref` are generated normally with a property typed to the SDK type.
- Multiple schemas in the same file may each have their own `x-sdk-type`. The extraction script collects all of them and deduplicates namespaces.

### Interaction with Other Extension Attributes

| Attribute | Interaction |
|---|---|
| `x-permissions` | Not applicable. `x-sdk-type` is on schema types, not endpoints. |
| `x-lifecycle` | Not applicable. SDK types are definitions, not lifecycle entities. |
| `x-constraint-group` | Configuration properties referencing SDK-typed schemas via `$ref` may participate in constraint groups normally. |

### Known Limitations

- The extraction script operates on a single schema file. If an SDK type is defined in `common-api.yaml` and referenced from `music-api.yaml`, the extraction must run on `common-api.yaml` where the `x-sdk-type` marker lives.
- There is no validation that the `x-sdk-type` value matches the actual SDK assembly type at schema-processing time. Mismatches surface only at C# compile time.
