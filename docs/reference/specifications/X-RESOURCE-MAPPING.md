# x-resource-mapping

> **Version**: 1.0
> **Status**: Implemented
> **Last Updated**: 2026-03-16
> **Schema Scope**: `*-service-events.yaml`
> **Generated Output**: `bannou-service/Generated/ResourceEventMappings.cs` -- static registry of event-to-resource mappings for Puppetmaster watch subscriptions
> **Related Specifications**: [x-lifecycle](X-LIFECYCLE.md)
> **Tenet References**: T1 (FOUNDATION), T5 (FOUNDATION)

---

## Summary

Declares how a service event relates to a watchable resource for Puppetmaster's watch subscription system. Maps events to resource types and ID fields so watchers can filter event streams by the resources they care about. For lifecycle events, use resource_mapping in x-lifecycle instead of adding x-resource-mapping directly; the lifecycle generator adds x-resource-mapping to generated events automatically.

---

## Schema Syntax

### On Manually-Defined Events

```yaml
PersonalityUpdatedEvent:
  allOf:
    - $ref: 'common-events.yaml#/components/schemas/BaseServiceEvent'
  type: object
  additionalProperties: false
  description: Published when character personality traits change
  x-resource-mapping:
    resource_type: character
    resource_id_field: characterId
    source_type: character-personality
    is_deletion: false
  required: [eventName, eventId, timestamp, characterId]
  properties:
    eventName:
      type: string
      default: personality.updated
      description: 'Event type identifier: personality.updated'
    characterId:
      type: string
      format: uuid
      description: Character whose personality was updated
```

### Via x-lifecycle resource_mapping

For lifecycle events, declare `resource_mapping` inside the x-lifecycle entity definition. The generator adds `x-resource-mapping` to each generated event automatically.

```yaml
x-lifecycle:
  topic_prefix: transit
  TransitConnection:
    model:
      connectionId: { type: string, format: uuid, primary: true, required: true }
      fromLocationId: { type: string, format: uuid, required: true }
      toLocationId: { type: string, format: uuid, required: true }
    resource_mapping:
      resource_type: transit-connection
      resource_id_field: connectionId
      source_type: transit
```

This produces three generated events (`TransitConnectionCreatedEvent`, `TransitConnectionUpdatedEvent`, `TransitConnectionDeletedEvent`), each with an `x-resource-mapping` block containing the specified values. The deleted event automatically gets `is_deletion: true`.

---

## Field Reference

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `resource_type` | string | No | Entity name in kebab-case | The type of resource this event affects. Watchers subscribe to resource types. |
| `resource_id_field` | string | No | Primary key field name (lifecycle) or must be specified (manual) | The JSON field in the event payload containing the resource's unique identifier. |
| `source_type` | string | No | Entity name in kebab-case | Identifies the source service/entity type. Watchers can filter by source type to distinguish events from different services affecting the same resource. |
| `is_deletion` | boolean | No | Inferred from event name | Whether this event represents a resource deletion. Automatically set to `true` for events whose name ends in `Deleted`. Can be explicitly overridden. |

---

## Generated Output

### File: `bannou-service/Generated/ResourceEventMappings.cs`

The generator scans all `*-service-events.yaml` files (including generated lifecycle event files in `schemas/Generated/`) for `x-resource-mapping` annotations and produces a single static registry class.

```csharp
/// <summary>
/// Auto-generated resource event mappings for Puppetmaster watch subscriptions.
/// </summary>
public static class ResourceEventMappings
{
    /// <summary>
    /// All registered resource event mappings.
    /// </summary>
    public static IReadOnlyList<ResourceEventMappingEntry> Mappings { get; } = new List<ResourceEventMappingEntry>
    {
        new("TransitConnectionCreatedEvent", "transit.connection.created",
            "transit-connection", "connectionId", "transit", isDeletion: false),
        new("TransitConnectionUpdatedEvent", "transit.connection.updated",
            "transit-connection", "connectionId", "transit", isDeletion: false),
        new("TransitConnectionDeletedEvent", "transit.connection.deleted",
            "transit-connection", "connectionId", "transit", isDeletion: true),
        new("PersonalityUpdatedEvent", "personality.updated",
            "character", "characterId", "character-personality", isDeletion: false),
    };
}
```

### ResourceEventMappingEntry

Each entry contains:

| Property | Type | Description |
|----------|------|-------------|
| `EventTypeName` | string | C# class name of the event |
| `Topic` | string | RabbitMQ routing key / event topic |
| `ResourceType` | string | Watchable resource type |
| `ResourceIdField` | string | JSON field name containing the resource ID |
| `SourceType` | string | Source service/entity type identifier |
| `IsDeletion` | bool | Whether the event represents resource deletion |

### Integration with Puppetmaster

Puppetmaster's watch system uses `ResourceEventMappings.Mappings` to:
1. Determine which event topics to subscribe to for a given resource type
2. Extract the resource ID from incoming event payloads using `ResourceIdField`
3. Filter events by `SourceType` when a watcher only cares about specific sources
4. Handle deletion events specially (e.g., removing watches, notifying watchers of resource removal)

---

## Runtime Behavior

### Watch Subscription Flow

1. A god-actor (or other watcher) registers a watch on a resource type + resource ID via Puppetmaster
2. Puppetmaster consults `ResourceEventMappings.Mappings` to find all event topics affecting that resource type
3. When an event arrives on a matching topic, Puppetmaster extracts the resource ID using `ResourceIdField`
4. If the resource ID matches a watched resource, the event is forwarded to the watcher

### Deletion Inference

When `is_deletion` is not explicitly set, the generator infers it from the event name:
- Event name ends with `Deleted` or `DeletedEvent` -- `is_deletion: true`
- All other event names -- `is_deletion: false`

Explicit `is_deletion` overrides inference in both directions.

### Edge Cases

- **Multiple events for the same resource**: Common and expected. A character resource might have events from `character`, `character-personality`, `character-history`, and other services, all with `resource_type: character` but different `source_type` values.
- **Missing resource_id_field**: If the specified `resource_id_field` does not exist in the event payload at runtime, the event cannot be matched to a watched resource and is silently dropped by the watch system.

---

## Structural Tests

| Test Name | Validates |
|-----------|-----------|
| `ResourceMapping_FieldExistsInEvent` | The `resource_id_field` value references a property that exists in the event schema |
| `ResourceMapping_RequiredFieldsPresent` | Required fields (`resource_type`, `resource_id_field`) are present when `x-resource-mapping` is declared |

> **Note**: These test names reflect the known validation intent. Confirm exact test method names against `structural-tests/StructuralTests.cs` if needed.

---

## Examples

### Example 1: Lifecycle Entity with Resource Mapping (Character)

**Schema** (`character-service-events.yaml`):
```yaml
x-lifecycle:
  Character:
    model:
      characterId: { type: string, format: uuid, primary: true, required: true }
      name: { type: string, required: true }
      realmId: { type: string, format: uuid, required: true }
    resource_mapping:
      resource_type: character
      resource_id_field: characterId
      source_type: character
```

**Generated lifecycle events** (`schemas/Generated/character-service-lifecycle-events.yaml`) each include:
```yaml
CharacterCreatedEvent:
  # ... event fields ...
  x-resource-mapping:
    resource_type: character
    resource_id_field: characterId
    source_type: character
    is_deletion: false

CharacterDeletedEvent:
  # ... event fields ...
  x-resource-mapping:
    resource_type: character
    resource_id_field: characterId
    source_type: character
    is_deletion: true
```

**Generated mapping entries** (`ResourceEventMappings.cs`):
```csharp
new("CharacterCreatedEvent", "character.created",
    "character", "characterId", "character", isDeletion: false),
new("CharacterUpdatedEvent", "character.updated",
    "character", "characterId", "character", isDeletion: false),
new("CharacterDeletedEvent", "character.deleted",
    "character", "characterId", "character", isDeletion: true),
```

### Example 2: Manually-Defined Event (Character Personality)

**Schema** (`character-personality-service-events.yaml`):
```yaml
components:
  schemas:
    PersonalityUpdatedEvent:
      allOf:
        - $ref: 'common-events.yaml#/components/schemas/BaseServiceEvent'
      type: object
      additionalProperties: false
      description: Published when character personality traits change
      x-resource-mapping:
        resource_type: character
        resource_id_field: characterId
        source_type: character-personality
      required: [eventName, eventId, timestamp, characterId]
      properties:
        eventName:
          type: string
          default: personality.updated
          description: 'Event type identifier: personality.updated'
        characterId:
          type: string
          format: uuid
          description: Character whose personality was updated
        traits:
          type: object
          nullable: true
          description: Updated personality trait values
```

**Generated mapping entry** (`ResourceEventMappings.cs`):
```csharp
new("PersonalityUpdatedEvent", "personality.updated",
    "character", "characterId", "character-personality", isDeletion: false),
```

A Puppetmaster watcher watching `resource_type: character` with a specific `characterId` would receive this event alongside lifecycle events from the character service itself. The `source_type` field (`character-personality` vs `character`) lets watchers distinguish the source.

---

## Edge Cases & Restrictions

### Forbidden Combinations

| Restriction | Reason |
|-------------|--------|
| Adding `x-resource-mapping` to lifecycle events manually | For lifecycle entities, use `resource_mapping` in the x-lifecycle block; the generator handles the rest. Adding it manually to generated events would be overwritten on regeneration. |
| Using `x-resource-mapping` in non-event schemas | Only valid on event schema definitions in `*-service-events.yaml` files. API schemas and configuration schemas do not produce events. |
| Referencing a `resource_id_field` that does not exist in the event | The watch system cannot extract the resource ID; the event silently fails to match any watches |

### Scoping Rules

- `x-resource-mapping` is scoped to individual event schema definitions within `*-service-events.yaml` files
- The generated `ResourceEventMappings.cs` aggregates mappings from ALL event schemas across all services into a single registry
- Multiple events can map to the same `resource_type` with different `source_type` values -- this is the expected pattern for cross-service resource watches

### Interaction with Other Extension Attributes

- **x-lifecycle**: When `resource_mapping` is declared on an x-lifecycle entity, the generator adds `x-resource-mapping` to each generated lifecycle event. The `is_deletion` field is automatically set to `true` for `*DeletedEvent` and `false` for `*CreatedEvent` and `*UpdatedEvent`. Do not add `x-resource-mapping` manually to lifecycle-generated events.
- **x-event-publications**: Events with `x-resource-mapping` should also be listed in `x-event-publications` for the event catalog. The resource mapping and publication registry serve different purposes (watch routing vs event documentation).

### Default Value Behavior

When using `resource_mapping` inside x-lifecycle (not direct `x-resource-mapping`):
- `resource_type` defaults to the entity name converted to kebab-case (e.g., `TransitConnection` becomes `transit-connection`)
- `resource_id_field` defaults to the field marked with `primary: true`
- `source_type` defaults to the entity name in kebab-case

When using `x-resource-mapping` directly on a manually-defined event, there are no defaults -- all fields used for watch matching should be explicitly specified.
