# x-polymorphic-type

> **Version**: 1.0
> **Status**: Implemented
> **Last Updated**: 2026-04-15
> **Schema Scope**: `*-api.yaml`, `*-service-events.yaml`
> **Generated Output**: `[PolymorphicType]` attribute on generated C# properties
> **Related Specifications**: [x-sdk-type](X-SDK-TYPE.md)
> **Tenet References**: T14 (Polymorphic Associations), T25 (Type Safety)

---

## Summary

Marks a generated property as intentionally polymorphic — the schema-level type is deliberately broader than the runtime type used by some consumers, and specific callers legitimately parse the string into a stronger type (e.g., a `Guid`). The marker emits a `[PolymorphicType]` attribute on the generated C# property, which exempts the property from the structural test `Services_NoParseOnGeneratedResponseProperties` (T25 enforcement).

This is NOT a license to widen typing without cause. It is a narrow exception for fields whose schema-level `string` typing is a deliberate design choice (discriminated unions, intentionally string-typed identifiers allowing non-GUID values, etc.) rather than an oversight that should be tightened to `format: uuid`.

---

## Schema Syntax

### Listing Properties

Declare the polymorphic property names in the `info.x-polymorphic-type-properties` array at the top of the schema file. Property names are listed in camelCase (the JSON wire format), and each entry is post-processed to the matching generated C# property:

```yaml
# analytics-api.yaml
info:
  title: Analytics Service API
  version: 1.0.0
  x-polymorphic-type-properties:
    - serviceId   # GUID when serviceType == Game; logical service name when serviceType == System
```

The companion `-service-events.yaml` typically repeats the same list so the attribute is applied to event model properties generated from those schemas:

```yaml
# analytics-service-events.yaml
info:
  title: Analytics Service Events
  version: 1.0.0
  x-polymorphic-type-properties:
    - serviceId
```

### Property Definition (Unchanged)

The property itself is defined normally — as a `string` (or `string` with `format`) — without any in-line marker. The `x-polymorphic-type-properties` list is the only schema-level marker:

```yaml
components:
  schemas:
    AnalyticsEventRecord:
      type: object
      required:
      - serviceId
      properties:
        serviceId:
          type: string
          description: |
            For game service events, this is the GUID of the game service.
            For system service events, this is the logical service name (e.g., "matchmaking").
        serviceType:
          $ref: '#/components/schemas/ServiceType'
```

---

## Field Reference

### `info.x-polymorphic-type-properties` (array of string)

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `x-polymorphic-type-properties` | array of string | No | `[]` | camelCase property names whose generated C# properties should receive `[PolymorphicType]`. |

Property names are case-sensitive camelCase matching the `[JsonPropertyName]` attribute on the generated C# property. The post-processor converts each name to PascalCase to locate the matching `public Type PropertyName { get; set; }` declaration.

---

## Generated Output

### Post-Processing Script

`scripts/postprocess-polymorphic-type.py` runs after NSwag generation (invoked from `scripts/generate-models.sh` and `scripts/generate-service-events.sh`). For each property name in `info.x-polymorphic-type-properties`:

1. Convert camelCase → PascalCase.
2. Locate the matching property declaration in the generated C# file.
3. Insert the attribute line above the property, indented to match.

```csharp
// Before post-processing
public partial class AnalyticsEventRecord
{
    public string ServiceId { get; set; }
}

// After post-processing
public partial class AnalyticsEventRecord
{
    [BeyondImmersion.BannouService.Attributes.PolymorphicType]
    public string ServiceId { get; set; }
}
```

### Attribute Definition

`bannou-service/Attributes/PolymorphicTypeAttribute.cs` declares the attribute:

```csharp
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class PolymorphicTypeAttribute : Attribute { }
```

The attribute is a pure marker — it has no properties and no runtime behavior. Structural tests inspect it via reflection.

---

## Runtime Behavior

`[PolymorphicType]` has no runtime behavior. It is a generation-time and test-time directive only. At runtime the property behaves as the standard `string` (or other declared type) NSwag emitted.

---

## Structural Tests

| Test Name | Interaction |
|-----------|-------------|
| `Services_NoParseOnGeneratedResponseProperties` (T25 enforcement) | Skips properties decorated with `[PolymorphicType]`. Without the attribute, the test flags any `Guid.Parse` / `int.Parse` / similar call on a generated property as a sign the schema type is wrong. |

No dedicated structural test exists for the `x-polymorphic-type-properties` list itself — correctness is enforced by the post-processor's silent no-op on unmatched names and by the T25 test that validates every `Parse` call has a corresponding attribute.

---

## When to Use — Decision Tree

Apply the attribute ONLY when all of the following are true:

1. The schema declares the property as `string` (typically with no `format` or with a non-`uuid` format).
2. At least one consumer needs to parse the string into a stronger runtime type (typically `Guid`).
3. The string typing is a **deliberate design choice**, not an oversight. There is a specific reason the schema does not use `format: uuid`.

If the answer to (3) is "no specific reason — the schema author should have used `format: uuid`", do NOT add the marker. Tighten the schema instead.

### Canonical Patterns

| Pattern | Example |
|---------|---------|
| **Discriminated union** | `serviceId` on Analytics events: GUID when `serviceType == Game`, logical service name (e.g., `"matchmaking"`) when `serviceType == System`. Tightening to `format: uuid` would reject the legitimate non-GUID case. |
| **Intentionally string-typed identifier** | `actorId` in the Actor schema is `string` because Actor allows non-GUID identifiers (e.g., named NPCs like `"blacksmith-1"`). Genesis and Puppetmaster, however, only ever pass GUIDs and need to parse the response back into a `Guid` for their own storage. The attribute documents that the string→Guid round-trip on specific consumers is expected. |

### Forbidden Patterns

| Anti-Pattern | Correct Fix |
|---|---|
| Marking a field polymorphic to silence the T25 test because the schema author used `string` instead of `format: uuid` | Tighten the schema to `format: uuid`, regenerate. Do not add the marker. |
| Marking a field polymorphic because the plugin code stores the GUID as a string internally | Fix the plugin code to use `Guid` typing. The marker is for schema→code transitions, not internal code practices. |
| Adding the marker to a field whose consumers always pass GUIDs, just in case a future non-GUID consumer emerges | Wait for the non-GUID consumer to exist. The marker documents an **existing** design choice, not a hypothetical one. |

---

## Examples

### Example 1: Analytics `serviceId` (Discriminated Union)

**Schema** (`analytics-api.yaml`):
```yaml
info:
  title: Analytics Service API
  version: 1.0.0
  x-polymorphic-type-properties:
    - serviceId

components:
  schemas:
    ServiceType:
      type: string
      enum:
        - Game     # game service identified by UUID
        - System   # system service identified by logical name

    AnalyticsEventRecord:
      type: object
      required:
      - serviceId
      - serviceType
      properties:
        serviceId:
          type: string
          description: |
            GUID when serviceType == Game; logical service name when serviceType == System.
        serviceType:
          $ref: '#/components/schemas/ServiceType'
```

**Event schema** (`analytics-service-events.yaml`) — same marker so event models also carry the attribute:
```yaml
info:
  title: Analytics Service Events
  version: 1.0.0
  x-polymorphic-type-properties:
    - serviceId
```

**Generated code** (`AnalyticsModels.cs` excerpt):
```csharp
public partial class AnalyticsEventRecord
{
    [BeyondImmersion.BannouService.Attributes.PolymorphicType]
    public string ServiceId { get; set; }

    public ServiceType ServiceType { get; set; }
}
```

**Consumer code**:
```csharp
if (record.ServiceType == ServiceType.Game)
{
    var gameServiceId = Guid.Parse(record.ServiceId);  // Legitimate: discriminator established the type
    await _gameServiceClient.GetServiceAsync(new GetServiceRequest { ServiceId = gameServiceId });
}
else
{
    var logicalName = record.ServiceId;  // Used as-is
}
```

The T25 test `Services_NoParseOnGeneratedResponseProperties` sees the `Guid.Parse(record.ServiceId)` call, looks up `ServiceId` on `AnalyticsEventRecord`, finds `[PolymorphicType]`, and passes.

### Example 2: Actor `actorId` (Cross-Service Consumer)

**Schema** (`actor-api.yaml`): No `x-polymorphic-type-properties` entry — Actor itself does not parse actorId.

**Schema** (`genesis-api.yaml`):
```yaml
info:
  x-polymorphic-type-properties:
    - actorId   # Genesis consumers parse actorId as Guid even though Actor allows non-GUID strings
```

**Generated code** (`GenesisModels.cs`): Wherever Genesis's models include an `actorId` string field, the attribute is applied, permitting `Guid.Parse(response.ActorId)` in Genesis service code.

---

## Edge Cases & Restrictions

### Forbidden Combinations

| Restriction | Reason |
|---|---|
| `x-polymorphic-type-properties` on a non-string property | The attribute only affects the T25 "no Parse on generated property" check. Non-string properties don't need the marker. |
| `x-polymorphic-type-properties` referencing a property that doesn't exist in the schema | The post-processor silently ignores unmatched names. Correct the entry or remove it to avoid drift. |
| `x-polymorphic-type-properties` outside `info` | The post-processor only parses `info.x-polymorphic-type-properties`. Entries in other locations are ignored. |

### Scoping Rules

- The list is per-file. A property name listed in `analytics-api.yaml` applies only to generated types from that file's API schema; the same property in `analytics-service-events.yaml` requires a separate entry in the events schema's `info.x-polymorphic-type-properties`.
- PascalCase conversion is mechanical: `serviceId` → `ServiceId`, `actor_id` → `Actor_id` (underscores are preserved). Use camelCase names that match `[JsonPropertyName]`.
- Matching is substring-safe: the post-processor looks for `public <type>? PropertyName { get;` so it does not match partial names (e.g., `Id` would not match `ServiceId`).

### Interaction with Other Extension Attributes

| Attribute | Interaction |
|---|---|
| `x-sdk-type` | If a property's schema type has `x-sdk-type`, the property is generated with the SDK type instead of `string`; `x-polymorphic-type-properties` is typically unnecessary because the stronger SDK type removes the need for `Parse`. |
| `x-lifecycle` | Lifecycle event models generated from `x-lifecycle` inherit the property definitions. If the entity's events schema has `x-polymorphic-type-properties` listing the field, the generated lifecycle event types will also receive the attribute. |
| `x-references` | Independent concept. `x-references` governs resource cleanup; polymorphic typing governs how consumers interpret a string field. |

### Known Limitations

- The post-processor uses a line-based regex match on `public <type>? PropertyName { get;`. If NSwag generates a property declaration that spans multiple lines or uses an unusual format, the match may miss. File-scoped namespaces and standard NSwag templates have not produced misses in practice.
- The post-processor is idempotent — running it twice does not produce duplicate attribute lines — but it does not remove attributes that were added and subsequently removed from `x-polymorphic-type-properties`. Regenerate the models file to clear stale attributes.
