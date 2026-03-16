# x-compression-callback / x-archive-type

> **Version**: 1.0
> **Status**: Implemented
> **Last Updated**: 2026-03-16
> **Schema Scope**: `*-api.yaml`
> **Generated Output**: `{Service}CompressionCallbacks.cs` (callback registration), `{SourceType}Template.cs` (IResourceTemplate for ABML path validation)
> **Related Specifications**: [x-references](X-REFERENCES.md)

---

## Summary

Declares compression callbacks for hierarchical resource archival via lib-resource, and archive type markers for compile-time ABML snapshot path validation. Services register callbacks with priority ordering to contribute compressed data during resource archival. Archive types generate IResourceTemplate implementations that validate ABML variable access paths at compile time. Use when a service owns data that should be included in resource archives or when compressed response schemas need compile-time path validation for behavior expressions.

---

## Schema Syntax

### x-compression-callback

Defined under `info:` in the service's `*-api.yaml`. Declares a compression callback for resource archival:

```yaml
info:
  title: Character Lifecycle Service
  version: 1.0.0
  x-compression-callback:
    resourceType: character
    sourceType: character-lifecycle
    compressEndpoint: /character-lifecycle/get-compress-data
    compressPayloadTemplate: '{"characterId": "{{resourceId}}"}'
    priority: 25
    templateNamespace: lifecycle
    description: Lifecycle summary (stages, marriages, children, fulfillment) and heritage profile
    decompressEndpoint: /character-lifecycle/restore-from-archive
    decompressPayloadTemplate: '{"characterId": "{{resourceId}}", "data": "{{data}}"}'
```

### Minimal Form (Without Decompression)

```yaml
info:
  title: Quest Service
  version: 1.0.0
  x-compression-callback:
    resourceType: character
    sourceType: quest
    compressEndpoint: /quest/get-compress-data
    compressPayloadTemplate: '{"characterId": "{{resourceId}}"}'
    priority: 50
    description: Quest state (active quests, completed counts, category breakdown)
```

### Cross-Resource Compression

A service may contribute context data to another resource type's archive. For example, Realm contributes context to Location archives:

```yaml
info:
  title: Realm Service
  version: 1.0.0
  x-compression-callback:
    resourceType: location
    sourceType: realm-context
    compressEndpoint: /realm/get-location-compress-context
    compressPayloadTemplate: '{"locationId": "{{resourceId}}"}'
    priority: 0
    templateNamespace: location
    description: Realm context included in location compression archives
```

### x-archive-type

Defined on a schema in `components/schemas` within `*-api.yaml`. Marks the schema as an archive type for IResourceTemplate generation:

```yaml
components:
  schemas:
    LifecycleArchive:
      x-archive-type: true
      description: Compressed archive of lifecycle and heritage data for character archival
      allOf:
        - $ref: 'common-api.yaml#/components/schemas/ResourceArchiveBase'
        - type: object
          properties:
            characterId:
              type: string
              format: uuid
              description: The character this archive belongs to
            lifecycleSummary:
              $ref: '#/components/schemas/LifecycleProfileSummary'
            aptitudes:
              type: array
              items:
                $ref: '#/components/schemas/AptitudeEntry'
```

---

## Field Reference

### x-compression-callback Fields

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `resourceType` | string | Yes | — | The resource type being archived (e.g., `character`, `location`, `realm`) |
| `sourceType` | string | Yes | — | Identifier for this service's contribution to the archive (e.g., `character-lifecycle`, `quest`, `realm-context`) |
| `compressEndpoint` | string | Yes | — | POST endpoint returning compressed data. Must exist in the service's `paths`. |
| `compressPayloadTemplate` | string | Yes | — | JSON template with `{{resourceId}}` placeholder for the resource being archived |
| `priority` | integer | Yes | — | Execution order during archival. Lower values execute earlier. |
| `templateNamespace` | string | No | Value of `sourceType` | Namespace for ABML variable access paths (e.g., `lifecycle`, `location`). Defaults to `sourceType` if omitted. |
| `description` | string | No | — | Human-readable description of what data this callback contributes |
| `decompressEndpoint` | string | No | — | POST endpoint for restoring data from an archive. Must exist in `paths` if specified. |
| `decompressPayloadTemplate` | string | No | — | JSON template with `{{resourceId}}` and `{{data}}` placeholders for restoration |

### Priority Guidelines

| Range | Purpose | Examples |
|-------|---------|---------|
| 0 | Base entity data | Location base data, realm context |
| 10-30 | Extension data | Character lifecycle (25), realm history (10) |
| 50-100 | Optional/derived data | Quest state (50), obligation data |

### x-archive-type Fields

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `x-archive-type` | boolean | Yes | — | Must be `true`. Marks the schema as an archive type for template generation. |

The marked schema must use `allOf` with `$ref: 'common-api.yaml#/components/schemas/ResourceArchiveBase'` to inherit base archive properties.

---

## Generated Output

### Compression Callback Registration Class

Generated to `plugins/lib-{service}/Generated/{Service}CompressionCallbacks.cs`:

```csharp
public static class CharacterLifecycleCompressionCallbacks
{
    /// <summary>
    /// Registers compression callbacks with lib-resource.
    /// Call this during service startup (OnRunningAsync).
    /// </summary>
    public static async Task<bool> RegisterAsync(
        IResourceClient resourceClient,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await resourceClient.DefineCompressCallbackAsync(
                new DefineCompressCallbackRequest
                {
                    ResourceType = "character",
                    SourceType = "character-lifecycle",
                    ServiceName = "character-lifecycle",
                    CompressEndpoint = "/character-lifecycle/get-compress-data",
                    CompressPayloadTemplate = "{\"characterId\": \"{{resourceId}}\"}",
                    Priority = 25,
                    Description = "Lifecycle summary (stages, marriages, children, fulfillment) and heritage profile",
                    DecompressEndpoint = "/character-lifecycle/restore-from-archive",
                    DecompressPayloadTemplate = "{\"characterId\": \"{{resourceId}}\", \"data\": \"{{data}}\"}"
                },
                cancellationToken);
            return true;
        }
        catch (ApiException)
        {
            return false;
        }
    }
}
```

Key generation rules:
- Static class named `{Service}CompressionCallbacks`
- Single `RegisterAsync` method wrapping `DefineCompressCallbackAsync`
- `ServiceName` is derived from the service's schema file name
- `ApiException` caught and returns `false` on failure
- Optional fields (`DecompressEndpoint`, `DecompressPayloadTemplate`, `Description`) omitted from the request when not specified in the schema

### Resource Template Class (from x-archive-type)

Generated to `bannou-service/Generated/ResourceTemplates/{SourceType}Template.cs`:

```csharp
public sealed class CharacterLifecycleTemplate : ResourceTemplateBase
{
    /// <inheritdoc />
    public override string SourceType => "character-lifecycle";

    /// <inheritdoc />
    public override string Namespace => "lifecycle";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, Type> ValidPaths { get; } = new Dictionary<string, Type>
    {
        [""] = typeof(LifecycleArchive),
        ["characterId"] = typeof(Guid),
        ["lifecycleSummary"] = typeof(LifecycleProfileSummary),
        ["lifecycleSummary.currentStage"] = typeof(string),
        ["lifecycleSummary.birthGameYear"] = typeof(int),
        ["lifecycleSummary.status"] = typeof(LifecycleStatus),
        ["aptitudes"] = typeof(ICollection<AptitudeEntry>),
        ["bloodlines"] = typeof(ICollection<BloodlineEntry>),
        // ... flattened property paths from the archive schema
    };
}
```

Key generation rules:
- Sealed class extending `ResourceTemplateBase`
- `SourceType` from the x-compression-callback's `sourceType`
- `Namespace` from the x-compression-callback's `templateNamespace` (or `sourceType` if omitted)
- `ValidPaths` dictionary contains all flattened property paths from the archive schema with their C# types
- Root path (`""`) maps to the archive type itself
- Nested properties use dot-separated paths (e.g., `lifecycleSummary.birthGameYear`)
- Array properties map to `ICollection<T>`
- Template is registered via `IResourceTemplateRegistry.Register()` during `OnRunningAsync`

---

## Runtime Behavior

### Callback Registration

Services call the generated `{Service}CompressionCallbacks.RegisterAsync()` during startup (typically in `OnRunningAsync`). This registers the service's compress endpoint with lib-resource so it is called during resource archival.

### Archival Execution Flow

When a resource is archived (e.g., character death triggers compression):
1. lib-resource collects all registered compression callbacks for the resource type
2. Callbacks are sorted by `priority` (lower values first)
3. Each callback's `compressEndpoint` is called with `{{resourceId}}` substituted
4. Responses are assembled into a hierarchical archive keyed by `sourceType`
5. The complete archive is stored by lib-resource

### Decompression Flow

When a resource is restored from an archive (e.g., ghost NPC creation from character archive):
1. lib-resource reads the stored archive
2. For each source type with a registered `decompressEndpoint`, the endpoint is called with `{{resourceId}}` and `{{data}}` substituted
3. Services restore their data from the provided archive section

### Template Registration

Resource templates are registered during startup:
1. Service calls `IResourceTemplateRegistry.Register(new CharacterLifecycleTemplate())`
2. The behavior compiler uses registered templates to validate ABML snapshot access expressions at compile time
3. An expression like `${snapshot.lifecycle.lifecycleSummary.currentStage}` is validated against the template's `ValidPaths`

### Edge Cases

- **Callback registration failure**: `RegisterAsync` catches `ApiException` and returns `false`; service logs and can retry
- **Missing compress endpoint**: Structural tests validate that declared endpoints exist in the schema's `paths`
- **Priority ties**: When multiple callbacks share the same priority, execution order among them is undefined
- **Null archive data**: If a compress endpoint returns no data for a resource, the source type is omitted from the archive

---

## Structural Tests

| Test Name | Validates |
|-----------|-----------|
| `XCompressionCallback_EndpointExistsInPaths` | The `compressEndpoint` declared in x-compression-callback exists in the service's schema `paths` |
| `XCompressionCallback_DecompressEndpointExistsInPaths` | If `decompressEndpoint` is specified, it exists in the service's schema `paths` |
| `XArchiveType_ExtendsResourceArchiveBase` | Schemas marked with `x-archive-type: true` use `allOf` with `ResourceArchiveBase` |

---

## Examples

### Example 1: Character Lifecycle with Decompression

A service contributing lifecycle data to character archives, with bidirectional compress/decompress support.

**Schema** (`character-lifecycle-api.yaml`):
```yaml
info:
  title: Character Lifecycle Service
  version: 1.0.0
  x-compression-callback:
    resourceType: character
    sourceType: character-lifecycle
    compressEndpoint: /character-lifecycle/get-compress-data
    compressPayloadTemplate: '{"characterId": "{{resourceId}}"}'
    priority: 25
    templateNamespace: lifecycle
    description: Lifecycle summary and heritage profile
    decompressEndpoint: /character-lifecycle/restore-from-archive
    decompressPayloadTemplate: '{"characterId": "{{resourceId}}", "data": "{{data}}"}'

components:
  schemas:
    LifecycleArchive:
      x-archive-type: true
      description: Compressed archive of lifecycle and heritage data
      allOf:
        - $ref: 'common-api.yaml#/components/schemas/ResourceArchiveBase'
        - type: object
          properties:
            characterId:
              type: string
              format: uuid
              description: The character this archive belongs to
            lifecycleSummary:
              $ref: '#/components/schemas/LifecycleProfileSummary'
```

**Generated callback** (`CharacterLifecycleCompressionCallbacks.cs`):
```csharp
public static class CharacterLifecycleCompressionCallbacks
{
    public static async Task<bool> RegisterAsync(
        IResourceClient resourceClient,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await resourceClient.DefineCompressCallbackAsync(
                new DefineCompressCallbackRequest
                {
                    ResourceType = "character",
                    SourceType = "character-lifecycle",
                    ServiceName = "character-lifecycle",
                    CompressEndpoint = "/character-lifecycle/get-compress-data",
                    CompressPayloadTemplate = "{\"characterId\": \"{{resourceId}}\"}",
                    Priority = 25,
                    Description = "Lifecycle summary and heritage profile",
                    DecompressEndpoint = "/character-lifecycle/restore-from-archive",
                    DecompressPayloadTemplate = "{\"characterId\": \"{{resourceId}}\", \"data\": \"{{data}}\"}"
                },
                cancellationToken);
            return true;
        }
        catch (ApiException)
        {
            return false;
        }
    }
}
```

**Generated template** (`CharacterLifecycleTemplate.cs`):
```csharp
public sealed class CharacterLifecycleTemplate : ResourceTemplateBase
{
    public override string SourceType => "character-lifecycle";
    public override string Namespace => "lifecycle";

    public override IReadOnlyDictionary<string, Type> ValidPaths { get; } = new Dictionary<string, Type>
    {
        [""] = typeof(LifecycleArchive),
        ["characterId"] = typeof(Guid),
        ["lifecycleSummary"] = typeof(LifecycleProfileSummary),
        ["lifecycleSummary.currentStage"] = typeof(string),
        ["lifecycleSummary.status"] = typeof(LifecycleStatus),
        // ... all flattened paths from the archive schema
    };
}
```

### Example 2: Quest Compression Without Decompression

A service contributing read-only archive data (no restore capability).

**Schema** (`quest-api.yaml`):
```yaml
info:
  title: Quest Service
  version: 1.0.0
  x-compression-callback:
    resourceType: character
    sourceType: quest
    compressEndpoint: /quest/get-compress-data
    compressPayloadTemplate: '{"characterId": "{{resourceId}}"}'
    priority: 50
    description: Quest state (active quests, completed counts, category breakdown)
```

**Generated callback** (`QuestCompressionCallbacks.cs`):
```csharp
public static class QuestCompressionCallbacks
{
    public static async Task<bool> RegisterAsync(
        IResourceClient resourceClient,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await resourceClient.DefineCompressCallbackAsync(
                new DefineCompressCallbackRequest
                {
                    ResourceType = "character",
                    SourceType = "quest",
                    ServiceName = "quest",
                    CompressEndpoint = "/quest/get-compress-data",
                    CompressPayloadTemplate = "{\"characterId\": \"{{resourceId}}\"}",
                    Priority = 50,
                    Description = "Quest state (active quests, completed counts, category breakdown)"
                },
                cancellationToken);
            return true;
        }
        catch (ApiException)
        {
            return false;
        }
    }
}
```

### Example 3: Cross-Resource Context (Realm Contributing to Location Archives)

A foundational service contributing context data to a different resource type's archive.

**Schema** (`realm-api.yaml`):
```yaml
info:
  title: Realm Service
  version: 1.0.0
  x-compression-callback:
    resourceType: location
    sourceType: realm-context
    compressEndpoint: /realm/get-location-compress-context
    compressPayloadTemplate: '{"locationId": "{{resourceId}}"}'
    priority: 0
    templateNamespace: location
    description: Realm context for location archives
```

This registers a priority-0 callback on `location` archives, contributing realm identity and description as context data alongside the location's own archive data.

---

## Edge Cases & Restrictions

### Forbidden Combinations

| Restriction | Reason |
|---|---|
| `x-archive-type` without `allOf` referencing `ResourceArchiveBase` | Archive types must inherit base archive properties for consistent archive structure |
| `decompressPayloadTemplate` without `decompressEndpoint` | The template is meaningless without the endpoint to call |
| `compressEndpoint` that does not exist in `paths` | Structural tests validate endpoint existence; the callback cannot function without the endpoint |
| Negative `priority` values | Priority must be a non-negative integer |

### Scoping Rules

- `x-compression-callback` is defined per service API schema, not per endpoint
- A service may declare at most one x-compression-callback per schema file
- Multiple services may contribute to the same `resourceType` archive (each with a distinct `sourceType`)
- `x-archive-type` is defined on individual schemas within `components/schemas` of any `*-api.yaml`
- Template generation requires both `x-compression-callback` and a matching `x-archive-type` schema in the same service

### Interaction with Other Extension Attributes

- **x-references**: Often co-located on the same service. x-references handles cleanup coordination; x-compression-callback handles archival data contribution. They are independent.
- **x-resource-lifecycle**: The foundational service's lifecycle policy governs when archival occurs. x-compression-callback registers the data to include.
- **x-lifecycle**: Lifecycle events in event schemas are separate. Compression callbacks are about data archival, not event publishing.

### Known Limitations

- Only `{{resourceId}}` and `{{data}}` placeholders are supported in payload templates
- No support for multiple compression callbacks per service (one per schema file)
- Template `ValidPaths` generation is limited to properties reachable from the archive schema's own `components/schemas`; external `$ref` types from `common-api.yaml` are resolved during generation
- Priority ordering is global across all services contributing to the same resource type; coordination requires checking other services' priorities
