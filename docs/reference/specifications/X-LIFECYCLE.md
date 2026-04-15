# x-lifecycle

> **Version**: 1.0
> **Status**: Implemented
> **Last Updated**: 2026-03-16
> **Schema Scope**: `*-service-events.yaml`
> **Generated Output**: `schemas/Generated/{service}-service-lifecycle-events.yaml` (event schemas), `{Service}LifecycleEvents.Interfaces.cs` (lifecycle interfaces)
> **Related Specifications**: [x-resource-mapping](X-RESOURCE-MAPPING.md), [x-permissions](X-PERMISSIONS.md)
> **Tenet References**: T1 (FOUNDATION), T5 (FOUNDATION), T31 (IMPLEMENTATION-BEHAVIOR)

---

## Summary

Generates CRUD lifecycle events (created, updated, deleted) automatically from entity model definitions in event schemas. Eliminates manual event authoring for standard entity operations by defining the model once and generating three typed events with full entity data, changedFields tracking, and optional deprecation field injection. Supports topic namespacing, sensitive field exclusion, Puppetmaster watch integration, and batch event generation.

---

## Schema Syntax

### Minimal Entity Definition

```yaml
x-lifecycle:
  Account:
    model:
      accountId: { type: string, format: uuid, primary: true, required: true }
      email: { type: string, required: true }
      displayName: { type: string }
```

### Full-Featured Entity Definition

```yaml
x-lifecycle:
  topic_prefix: myservice
  TemplateEntity:
    deprecation: true
    instanceEntity: InstanceEntity
    batch: true
    model:
      templateId: { type: string, format: uuid, primary: true, required: true }
      name: { type: string, required: true }
      secretKey: { type: string, required: true }
    sensitive: [secretKey]
    resource_mapping:
      resource_type: template-entity
      resource_id_field: templateId
      source_type: myservice
  InstanceEntity:
    model:
      instanceId: { type: string, format: uuid, primary: true, required: true }
      templateId: { type: string, format: uuid, required: true }
```

### Topic Prefix for Multi-Entity Services

```yaml
x-lifecycle:
  topic_prefix: transit
  TransitConnection:
    model:
      connectionId: { type: string, format: uuid, primary: true, required: true }
      # Generates topics: transit.connection.created, transit.connection.updated, transit.connection.deleted
  TransitMode:
    model:
      modeId: { type: string, format: uuid, primary: true, required: true }
      # Generates topics: transit.mode.created, transit.mode.updated, transit.mode.deleted
```

---

## Field Reference

### Top-Level Fields

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `topic_prefix` | string | No | Derived from filename | Enables Pattern C namespaced topics. Required for multi-entity services. |

### Per-Entity Fields

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `model` | object | Yes | -- | Entity field definitions (see Model Field Reference) |
| `sensitive` | array of strings | No | `[]` | Field names excluded from generated events |
| `deprecation` | boolean | No | `false` | When `true`, auto-injects `isDeprecated`, `deprecatedAt`, `deprecationReason` fields |
| `instanceEntity` | string | Conditional | -- | Required when `deprecation: true` (Category B). Names the x-lifecycle entity representing instances of this template. Must be an x-lifecycle entity in the same events file. |
| `batch` | boolean | No | `false` | When `true`, generates batch event variants alongside single-entity events |
| `immutable` | boolean | No | `false` | When `true`, the entity has no update path. The generator suppresses `*UpdatedEvent` (or `*BatchModifiedEvent`/`*BatchModifiedEntry` when combined with `batch: true`) and the corresponding lifecycle interface declaration. The schema MUST NOT list `{entity}.updated` or `{entity}.batch-modified` in `x-event-publications`. |
| `resource_mapping` | object | No | -- | Enables Puppetmaster watch subscriptions (see Resource Mapping Fields) |

### Model Field Properties

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| `type` | string | Yes | -- | OpenAPI type: `string`, `integer`, `number`, `boolean`, `array`, `object` |
| `format` | string | No | -- | OpenAPI format: `uuid`, `date-time`, etc. |
| `primary` | boolean | No | `false` | Marks the primary key field. Exactly one field per entity must be primary. |
| `required` | boolean | No | `false` | Whether the field is in the `required` array of generated events |
| `nullable` | boolean | No | `false` | Whether the field is nullable in generated events |
| `$ref` | string | No | -- | Reference to a type in the service API schema or common-api.yaml |
| `items` | object | No | -- | For `type: array`, defines the item schema |
| `enum` | array | No | -- | Inline enum values |
| `description` | string | No | -- | Field description carried into generated events |

### Resource Mapping Fields

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `resource_type` | string | No | Entity name in kebab-case | Type of resource for Puppetmaster watch filtering |
| `resource_id_field` | string | No | Primary key field name | JSON field containing the resource ID |
| `source_type` | string | No | Entity name in kebab-case | Source type identifier for watch filtering |

---

## Generated Output

### Generated Event Schema

Location: `schemas/Generated/{service}-service-lifecycle-events.yaml`

For each entity, three events are generated:

#### CreatedEvent

Contains the full entity model (minus sensitive fields), inherits from `BaseServiceEvent`, includes `eventName` with default value matching the topic.

```yaml
AccountCreatedEvent:
  allOf:
    - $ref: '../common-events.yaml#/components/schemas/BaseServiceEvent'
  type: object
  additionalProperties: false
  description: Published when an account is created
  required: [eventName, eventId, timestamp, accountId, email, createdAt]
  properties:
    eventName:
      type: string
      default: account.created
      description: 'Event type identifier: account.created'
    accountId:
      type: string
      format: uuid
      description: Primary key
    email:
      type: string
      description: User email address
    displayName:
      type: string
      nullable: true
      description: Optional display name
    createdAt:
      type: string
      format: date-time
      description: Timestamp when the entity was created
```

#### UpdatedEvent

Same as CreatedEvent plus `changedFields` array and `updatedAt` timestamp.

```yaml
AccountUpdatedEvent:
  allOf:
    - $ref: '../common-events.yaml#/components/schemas/BaseServiceEvent'
  type: object
  additionalProperties: false
  description: Published when an account is updated
  required: [eventName, eventId, timestamp, accountId, email, updatedAt, changedFields]
  properties:
    eventName:
      type: string
      default: account.updated
      description: 'Event type identifier: account.updated'
    accountId:
      type: string
      format: uuid
    email:
      type: string
    displayName:
      type: string
      nullable: true
    updatedAt:
      type: string
      format: date-time
      description: Timestamp when the entity was last updated
    changedFields:
      type: array
      items:
        type: string
      description: List of field names that changed in this update
```

#### DeletedEvent

Same entity fields plus optional `deletedReason`.

```yaml
AccountDeletedEvent:
  allOf:
    - $ref: '../common-events.yaml#/components/schemas/BaseServiceEvent'
  type: object
  additionalProperties: false
  description: Published when an account is deleted
  required: [eventName, eventId, timestamp, accountId, email]
  properties:
    eventName:
      type: string
      default: account.deleted
      description: 'Event type identifier: account.deleted'
    accountId:
      type: string
      format: uuid
    email:
      type: string
    displayName:
      type: string
      nullable: true
    deletedReason:
      type: string
      nullable: true
      description: Optional reason for deletion
```

### Auto-Injected Fields

The generator automatically adds these fields (do not define them manually in the model):

| Field | Added To | Type | Description |
|-------|----------|------|-------------|
| `createdAt` | CreatedEvent | `date-time`, required | Entity creation timestamp |
| `updatedAt` | UpdatedEvent | `date-time`, required | Entity update timestamp |
| `changedFields` | UpdatedEvent | `array of string`, required | Names of fields that changed |
| `deletedReason` | DeletedEvent | `string`, nullable | Optional deletion reason |

### Deprecation Field Injection

When `deprecation: true`, three additional fields are injected into all three lifecycle events:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `isDeprecated` | boolean | Yes | Whether the entity is deprecated |
| `deprecatedAt` | date-time | No (nullable) | When deprecation occurred |
| `deprecationReason` | string (maxLength 500) | No (nullable) | Why the entity was deprecated |

Manual definitions of these fields in the model block are stripped and replaced by the auto-injected versions. The `IDeprecatableEntity` interface (from `BeyondImmersion.Bannou.Core`) is added to the generated C# partial classes.

### Generated C# Interfaces

Location: `plugins/lib-{service}/Generated/{Service}LifecycleEvents.Interfaces.cs`

Each lifecycle entity gets marker interfaces:

```csharp
public partial class AccountCreatedEvent : ILifecycleCreatedEvent { }
public partial class AccountUpdatedEvent : ILifecycleUpdatedEvent { }
public partial class AccountDeletedEvent : ILifecycleDeletedEvent { }
```

When `deprecation: true`:

```csharp
public partial class TemplateEntityCreatedEvent : ILifecycleCreatedEvent, IDeprecatableEntity { }
public partial class TemplateEntityUpdatedEvent : ILifecycleUpdatedEvent, IDeprecatableEntity { }
public partial class TemplateEntityDeletedEvent : ILifecycleDeletedEvent, IDeprecatableEntity { }
```

### Resource Mapping Output

When `resource_mapping` is specified, each generated event includes `x-resource-mapping` annotations. See the [x-resource-mapping specification](X-RESOURCE-MAPPING.md) for details.

---

## Runtime Behavior

### Event Publishing

Services publish lifecycle events using generated `Publish*Async` extension methods on `IMessageBus`. The generated events carry the complete entity model so consumers can react without a follow-up lookup.

### Topic Derivation

The `topic_prefix` field controls topic naming:

| Scenario | topic_prefix | Entity | Generated Topic |
|----------|-------------|--------|-----------------|
| Single-entity service | *(omitted)* | `Account` | `account.created` |
| Entity name equals prefix | `seed` | `Seed` | `seed.created` |
| Entity name starts with prefix + hyphen | `transit` | `TransitConnection` | `transit.connection.created` |
| Entity name does not start with prefix | `worldstate` | `CalendarTemplate` | `worldstate.calendar-template.created` |

### Hyphenated Service Names

When a service has a hyphenated name (e.g., `save-load`), the `topic_prefix` must use the full hyphenated name: `topic_prefix: save-load`, NOT `save.load`. Without `topic_prefix`, the lifecycle generator derives the prefix from the filename (minus `-service-events.yaml`), automatically preserving hyphens.

### changedFields Convention

The `changedFields` array on updated events lists the property names that changed in the update. Consumers use this to filter updates they care about without comparing full entity snapshots.

### Batch Events

When `batch: true`, the generator produces additional batch event types (e.g., `AccountBatchCreatedEvent`) containing an array of entities. These are used for bulk operations.

### Immutable Entities

When `immutable: true` is set on an x-lifecycle entity, the entity has no update path — no API endpoint mutates its fields after creation. The generator suppresses the modification event types so that the generated C# code faithfully reflects the entity's actual contract.

**Generator behavior**:

| With… | Generated types | Suppressed types |
|-------|----------------|------------------|
| `immutable: true` only | `{Entity}CreatedEvent`, `{Entity}DeletedEvent` | `{Entity}UpdatedEvent` |
| `immutable: true` + `batch: true` | `{Entity}BatchEntry`, `{Entity}BatchCreatedEvent`, `{Entity}BatchDestroyedEntry`, `{Entity}BatchDestroyedEvent` | `{Entity}BatchModifiedEntry`, `{Entity}BatchModifiedEvent` |

The companion `*LifecycleEvents.Interfaces.cs` file also omits the `ILifecycleUpdatedEvent` declaration for the suppressed type.

**Schema obligations when using `immutable: true`**:

1. The entity MUST NOT list `{entity}.updated` in `x-event-publications` (non-batch case).
2. The entity MUST NOT list `{entity}.batch-modified` in `x-event-publications` (batch case).
3. The entity's `x-event-publications` entries still cover `{entity}.created` / `{entity}.deleted` (or the batch equivalents).
4. The schema description for the entity's section SHOULD call out the immutability — e.g., "Collection instance lifecycle: created, deleted (updated omitted — collections are immutable after creation)".

**When to use**: The entity has no `Update*` / `Modify*` / `Set*` endpoint, no in-place field mutation in service code, and no conceptual "this entity changed" event. Typical candidates:
- Instance entities created once and later destroyed (collections, license board instances).
- Records that represent the fact an event happened (receipts, immutable log entries) and are never edited.

**When NOT to use**: Do not set `immutable: true` solely because a specific field subset is immutable — use schema nullability and `ChangeFields` for partial mutability. The flag is for entities whose contract has **no** update endpoint.

**Interaction with other flags**:

| Combination | Effect |
|-------------|--------|
| `immutable: true` + `deprecation: true` | Allowed but unusual — Category A/B deprecation both flow through `*.updated` events; if you genuinely need deprecation on an immutable entity, the generator will still emit the `IsDeprecated`/`DeprecatedAt`/`DeprecationReason` fields on `CreatedEvent` and `DeletedEvent` but there is no `UpdatedEvent` to publish deprecation via `changedFields`. This means deprecation cannot be toggled on such entities; they are created deprecated or not at all. Re-evaluate whether deprecation is appropriate. |
| `immutable: true` + `batch: true` | Standard pattern for high-frequency immutable instance entities (e.g., Collection at NPC scale). Only created / destroyed batch events exist. |
| `immutable: true` (alone) | Standard pattern for singleton immutable records (e.g., LicenseBoard). Only created / deleted events exist. |

---

## Structural Tests

| Test Name | Validates |
|-----------|-----------|
| `LifecycleModels_DoNotContainAutoInjectedDeprecationFields` | No manual `isDeprecated`, `deprecatedAt`, or `deprecationReason` fields exist in `x-lifecycle` model blocks when `deprecation: true` is set |
| `DeprecatableEntities_MustDeclareInstanceEntity` | Every entity with `deprecation: true` has an `instanceEntity` field naming a valid x-lifecycle entity in the same events file |
| `BatchLifecycleEntities_HaveBatchEventPublications` | Every entity with `batch: true` declares `batch-created` / `batch-destroyed` publications (and `batch-modified` UNLESS `immutable: true` is also set) |
| `ImmutableLifecycleEntities_DoNotDeclareUpdatedPublication` | Entities with `immutable: true` must not list `{entity}.updated` or `{entity}.batch-modified` in `x-event-publications` (the generator does not produce those event types) |

---

## Examples

### Example 1: Single-Entity Service (Account)

**Schema** (`account-service-events.yaml`):
```yaml
x-lifecycle:
  Account:
    model:
      accountId: { type: string, format: uuid, primary: true, required: true }
      email: { type: string, required: true }
      displayName: { type: string }
      passwordHash: { type: string, required: true }
    sensitive: [passwordHash]
```

**Generated topics**: `account.created`, `account.updated`, `account.deleted`

**Sensitive field handling**: `passwordHash` is excluded from all three generated events. The entity model in events contains `accountId`, `email`, and `displayName` but never `passwordHash`.

### Example 2: Multi-Entity Service with Deprecation (Item)

**Schema** (`item-service-events.yaml`):
```yaml
x-lifecycle:
  topic_prefix: item
  ItemTemplate:
    deprecation: true
    instanceEntity: ItemInstance
    model:
      templateId: { type: string, format: uuid, primary: true, required: true }
      name: { type: string, required: true }
      description: { type: string }
    resource_mapping:
      resource_type: item-template
      resource_id_field: templateId
      source_type: item
  ItemInstance:
    model:
      instanceId: { type: string, format: uuid, primary: true, required: true }
      templateId: { type: string, format: uuid, required: true }
      quantity: { type: integer, required: true }
    resource_mapping:
      resource_type: item-instance
      resource_id_field: instanceId
      source_type: item
```

**Generated topics**:
- `item.template.created`, `item.template.updated`, `item.template.deleted`
- `item.instance.created`, `item.instance.updated`, `item.instance.deleted`

**Deprecation fields**: `ItemTemplateCreatedEvent`, `ItemTemplateUpdatedEvent`, and `ItemTemplateDeletedEvent` all include auto-injected `isDeprecated`, `deprecatedAt`, and `deprecationReason` fields. `ItemInstance` events do not (deprecation is not set on the instance entity).

**instanceEntity**: `ItemTemplate` declares `instanceEntity: ItemInstance`. The clean-deprecated sweep uses reverse-index instance counts to determine when a deprecated template has zero remaining instances. The structural test `DeprecatableEntities_MustDeclareInstanceEntity` validates that `ItemInstance` is a valid x-lifecycle entity in the same file.

### Example 3: Immutable Instance Entity (LicenseBoard)

**Schema** (`license-service-events.yaml`):
```yaml
x-lifecycle:
  topic_prefix: license
  LicenseBoardTemplate:
    deprecation: true
    instanceEntity: LicenseBoard
    model:
      boardTemplateId: { type: string, format: uuid, primary: true, required: true }
      # ... template-specific fields ...
  LicenseBoard:
    immutable: true   # No update endpoint — boards never change after creation
    model:
      boardId: { type: string, format: uuid, primary: true, required: true }
      ownerType: { $ref: 'common-api.yaml#/components/schemas/EntityType', required: true }
      ownerId: { type: string, format: uuid, required: true }
      # ... immutable owner/context fields ...
```

**Generated types**: `LicenseBoardCreatedEvent`, `LicenseBoardDeletedEvent`. No `LicenseBoardUpdatedEvent`.

**x-event-publications** lists only `license.board.created` and `license.board.deleted`.

**Note**: The existence of `immutable: true` means the companion `LicenseLifecycleEvents.Interfaces.cs` emits `ILifecycleCreatedEvent` and `ILifecycleDeletedEvent` declarations for LicenseBoard but no `ILifecycleUpdatedEvent` line — nothing to implement it on.

### Example 4: Immutable + Batch Entity (Collection)

**Schema** (`collection-service-events.yaml`):
```yaml
x-lifecycle:
  topic_prefix: collection
  CollectionEntryTemplate:
    deprecation: true
    instanceEntity: Collection
    model: { ... }
  Collection:
    batch: true
    immutable: true   # Collections have no update endpoint — only created/destroyed
    model:
      collectionId: { type: string, format: uuid, primary: true, required: true }
      # ... immutable ownership + container fields ...
```

**Generated types**: `CollectionBatchEntry`, `CollectionBatchCreatedEvent`, `CollectionBatchDestroyedEntry`, `CollectionBatchDestroyedEvent`. No `BatchModifiedEntry` / `BatchModifiedEvent`.

**x-event-publications** lists only `collection.batch-created` and `collection.batch-destroyed`.

---

## Edge Cases & Restrictions

### Forbidden Patterns

| Restriction | Reason |
|-------------|--------|
| Manually defining `*CreatedEvent`, `*UpdatedEvent`, `*DeletedEvent` in `components/schemas` | Use `x-lifecycle` instead; manual definitions bypass auto-injection and interface generation |
| Defining `isDeprecated`, `deprecatedAt`, `deprecationReason` in model block when `deprecation: true` | These are auto-injected; manual definitions are stripped. Structural test enforces this. |
| Using `topic_prefix: save.load` for hyphenated service `save-load` | Use the full hyphenated name: `topic_prefix: save-load`. The prefix is the service identity. |
| Omitting `instanceEntity` when `deprecation: true` | Required for Category B deprecation lifecycle; structural test enforces this |
| `instanceEntity` referencing a non-lifecycle entity | The named entity must be an x-lifecycle entity in the same events file with full CRUD (including deletion capability) |
| Declaring `{entity}.updated` / `{entity}.batch-modified` in `x-event-publications` when the entity has `immutable: true` | The generator does not produce those event types — declaring the publication would reference a non-existent class. Remove the publication entry. |
| Setting `immutable: true` on an entity that has an Update/Modify/Set endpoint in its API schema | The flag documents the contract; if an update endpoint exists, the flag is wrong. Either remove the flag or remove the endpoint. |
| Pattern B topic naming (service name embedded via hyphens in entity) | Forbidden by topic naming convention. Use `topic_prefix` to achieve Pattern C dot-separated namespacing. |

### Scoping Rules

- `x-lifecycle` is defined per events schema file (`{service}-service-events.yaml`)
- `topic_prefix` applies to all entities within that file
- Entity names must be unique within a single `x-lifecycle` block
- `$ref` in model fields can reference the service's own `-api.yaml` or `common-api.yaml`

### Interaction with Other Extension Attributes

- **x-resource-mapping**: When `resource_mapping` is specified in an x-lifecycle entity, the generator adds `x-resource-mapping` to each generated event automatically. For manually-defined events, add `x-resource-mapping` directly.
- **x-event-publications**: Lifecycle events should be listed in `x-event-publications` so the full event catalog is in one place, even though they are auto-generated.
- **x-event-subscriptions**: Other services can subscribe to lifecycle events by referencing the generated event class names (e.g., `event: AccountDeletedEvent`).

### Many:Many Relationships

The `instanceEntity` does NOT require a 1:many relationship between template and instance. When an instance record contains embedded references to multiple templates (e.g., an item with multiple affix slots), the `instanceEntity` names the lifecycle entity containing the references, not embedded sub-entities. The `hasInstancesAsync` delegate and `DeprecationCleanupHelper` are agnostic to relationship cardinality -- they check a reverse index key, not a relationship shape.
