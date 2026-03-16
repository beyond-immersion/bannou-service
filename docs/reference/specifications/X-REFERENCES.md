# x-references / x-resource-lifecycle

> **Version**: 1.0
> **Status**: Implemented
> **Last Updated**: 2026-03-16
> **Schema Scope**: `*-api.yaml`
> **Generated Output**: `{Service}ReferenceTracking.cs` (Register/Unregister helpers, cleanup/migrate callback registration)
> **Tenet References**: T28 (FOUNDATION)

---

## Summary

Declares resource reference dependencies between services for lifecycle management via lib-resource. Consumer services declare x-references with explicit delete policies (cascade, restrict, detach) and cleanup or migration callbacks. Foundational services declare x-resource-lifecycle with grace periods and cleanup policies. Together they enable coordinated cross-service resource cleanup without event-based coupling, replacing the forbidden pattern of subscribing to lifecycle events for dependent data destruction.

---

## Schema Syntax

### x-references (Consumer Service)

Defined under `info:` in the consumer service's `*-api.yaml`. Declares references to foundational resources:

```yaml
info:
  title: Divine Service
  version: 1.0.0
  x-references:
    - target: character
      sourceType: divine
      field: characterId
      onDelete: cascade
      cleanup:
        endpoint: /divine/cleanup-by-character
        payloadTemplate: '{"characterId": "{{resourceId}}"}'
    - target: game-service
      sourceType: divine
      field: gameServiceId
      onDelete: cascade
      cleanup:
        endpoint: /divine/cleanup-by-game-service
        payloadTemplate: '{"gameServiceId": "{{resourceId}}"}'
```

### x-references with RESTRICT Policy

RESTRICT references use `migrate` instead of `cleanup`, providing an endpoint to reassign dependent entities during resource merge:

```yaml
info:
  title: Location Service
  version: 1.0.0
  x-references:
    - target: realm
      sourceType: location
      field: realmId
      onDelete: restrict
      migrate:
        endpoint: /location/migrate-by-realm
        payloadTemplate: '{"sourceRealmId": "{{sourceResourceId}}", "targetRealmId": "{{targetResourceId}}"}'
```

### x-resource-lifecycle (Foundational Service)

Defined under `info:` in the foundational service's `*-api.yaml`. Declares lifecycle policy for a tracked resource:

```yaml
info:
  title: Location Service
  version: 1.0.0
  x-resource-lifecycle:
    resourceType: location
    gracePeriodSeconds: 604800
    cleanupPolicy: ALL_REQUIRED
```

```yaml
info:
  title: Realm Service
  version: 1.0.0
  x-resource-lifecycle:
    resourceType: realm
    cleanupPolicy: BEST_EFFORT
```

---

## Field Reference

### x-references Entry Fields

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `target` | string | Yes | — | The foundational resource type being referenced (e.g., `character`, `realm`, `game-service`) |
| `sourceType` | string | Yes | — | The source entity type holding the reference (e.g., `divine`, `location`) |
| `field` | string | Yes | — | The field name in the source entity that holds the reference ID |
| `onDelete` | string (enum) | Yes | — | Delete policy. Must be explicitly declared. No implicit default. |
| `cleanup` | object | Conditional | — | Required for `cascade` and `detach`. Forbidden for `restrict`. |
| `migrate` | object | Conditional | — | Required for `restrict`. Forbidden for `cascade` and `detach`. |

### onDelete Policies

| Policy | Behavior | Required Section | Forbidden Section |
|--------|----------|------------------|-------------------|
| `cascade` | Delete all dependent entities when the resource is deleted | `cleanup` | `migrate` |
| `restrict` | Block resource deletion while references exist; provide migration endpoint for merge | `migrate` | `cleanup` |
| `detach` | Null out or detach the reference field when the resource is deleted | `cleanup` | `migrate` |

### cleanup Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `endpoint` | string | Yes | The POST endpoint to call for cleanup. Must exist in the service's `paths`. |
| `payloadTemplate` | string | Yes | JSON template with `{{resourceId}}` placeholder for the deleted resource ID. |

### migrate Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `endpoint` | string | Yes | The POST endpoint to call for migration. Must exist in the service's `paths`. |
| `payloadTemplate` | string | Yes | JSON template with `{{sourceResourceId}}` and `{{targetResourceId}}` placeholders. |

### x-resource-lifecycle Fields

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `resourceType` | string | Yes | — | The resource type. Must match `target` values in consumer x-references entries. |
| `gracePeriodSeconds` | integer | No | — | Grace period before cleanup begins after deletion is initiated. |
| `cleanupPolicy` | string (enum) | Yes | — | `ALL_REQUIRED` (abort if any callback fails) or `BEST_EFFORT` (continue on failure). |

---

## Generated Output

### Reference Tracking Class

Generated to `plugins/lib-{service}/Generated/{Service}ReferenceTracking.cs` as a partial class on the service:

```csharp
[ResourceCleanupRequired("CleanupByCharacterAsync")]
[ResourceCleanupRequired("CleanupByGameServiceAsync")]
public partial class DivineService
{
    // Per-reference: Register and Unregister helpers

    protected async Task RegisterCharacterReferenceAsync(
        string divineId,
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        await _resourceClient.RegisterReferenceAsync(
            new RegisterReferenceRequest
            {
                ResourceType = "character",
                ResourceId = characterId,
                SourceType = "divine",
                SourceId = divineId
            },
            cancellationToken);
    }

    protected async Task UnregisterCharacterReferenceAsync(
        string divineId,
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        await _resourceClient.UnregisterReferenceAsync(
            new UnregisterReferenceRequest
            {
                ResourceType = "character",
                ResourceId = characterId,
                SourceType = "divine",
                SourceId = divineId
            },
            cancellationToken);
    }
}
```

### Cleanup Callback Registration (CASCADE/DETACH)

For services with CASCADE or DETACH references, a static `RegisterResourceCleanupCallbacksAsync` method is generated:

```csharp
public static async Task<bool> RegisterResourceCleanupCallbacksAsync(
    IResourceClient resourceClient,
    CancellationToken cancellationToken = default)
{
    var allSucceeded = true;

    try
    {
        await resourceClient.DefineCleanupCallbackAsync(
            new DefineCleanupCallbackRequest
            {
                ResourceType = "character",
                SourceType = "divine",
                ServiceName = "divine",
                CleanupEndpoint = "/divine/cleanup-by-character",
                CleanupPayloadTemplate = "{\"characterId\": \"{{resourceId}}\"}",
                Description = "Delete divine entities referencing deleted character"
            },
            cancellationToken);
    }
    catch (ApiException)
    {
        allSucceeded = false;
    }

    return allSucceeded;
}
```

### Migrate Callback Registration (RESTRICT)

For services with RESTRICT references, a static `RegisterResourceMigrateCallbacksAsync` method is generated:

```csharp
public static async Task<bool> RegisterResourceMigrateCallbacksAsync(
    IResourceClient resourceClient,
    CancellationToken cancellationToken = default)
{
    var allSucceeded = true;

    try
    {
        await resourceClient.DefineMigrateCallbackAsync(
            new DefineMigrateCallbackRequest
            {
                ResourceType = "realm",
                SourceType = "location",
                ServiceName = "location",
                MigrateEndpoint = "/location/migrate-by-realm",
                MigratePayloadTemplate = "{\"sourceRealmId\": \"{{sourceResourceId}}\", \"targetRealmId\": \"{{targetResourceId}}\"}",
                Description = "Migrate location entities from source realm to target realm"
            },
            cancellationToken);
    }
    catch (ApiException)
    {
        allSucceeded = false;
    }

    return allSucceeded;
}
```

### ResourceCleanupRequired Attribute

The `[ResourceCleanupRequired("MethodName")]` class-level attribute is generated for each CASCADE/DETACH reference. It declares that the service must implement a cleanup method, enforced by structural tests.

---

## Runtime Behavior

### Callback Registration

Services call the generated `RegisterResourceCleanupCallbacksAsync` and/or `RegisterResourceMigrateCallbacksAsync` during startup (typically in `OnRunningAsync`). These methods register the service's cleanup/migrate endpoints with lib-resource.

### Cleanup Execution Flow

When a foundational resource is deleted:
1. lib-resource checks for registered references
2. For CASCADE: lib-resource calls each registered cleanup endpoint with `{{resourceId}}` substituted
3. For RESTRICT: lib-resource blocks deletion if references exist; during merge, calls migrate endpoints with `{{sourceResourceId}}` and `{{targetResourceId}}` substituted
4. For DETACH: lib-resource calls cleanup endpoints to null out references
5. Grace period (from x-resource-lifecycle) delays cleanup if configured
6. Cleanup policy determines failure handling: `ALL_REQUIRED` aborts on any failure, `BEST_EFFORT` continues

### Reference Tracking

- `Register*ReferenceAsync` is called after creating an entity that references a foundational resource
- `Unregister*ReferenceAsync` is called before deleting an entity that references a foundational resource
- lib-resource maintains a reference count per resource, used for RESTRICT policy enforcement

### Edge Cases

- **Callback registration failure**: `RegisterResourceCleanupCallbacksAsync` catches `ApiException` per callback and returns false if any fail; service can retry or log
- **Missing cleanup endpoint**: Structural tests validate that declared endpoints exist in the schema's `paths`
- **Payload template placeholders**: Only `{{resourceId}}`, `{{sourceResourceId}}`, and `{{targetResourceId}}` are supported

---

## Structural Tests

| Test Name | Validates |
|-----------|-----------|
| `XReferences_MustDeclareExplicitOnDeletePolicy` | Every x-references entry has an explicit `onDelete` field with a valid policy value |
| `XReferences_CallbackPatternMatchesOnDeletePolicy` | CASCADE/DETACH entries have `cleanup` and no `migrate`; RESTRICT entries have `migrate` and no `cleanup` |

---

## Examples

### Example 1: CASCADE Cleanup (Divine Service)

A service that deletes all its entities when the referenced character or game-service is deleted.

**Schema** (`divine-api.yaml`):
```yaml
info:
  title: Divine Service
  version: 1.0.0
  x-references:
    - target: character
      sourceType: divine
      field: characterId
      onDelete: cascade
      cleanup:
        endpoint: /divine/cleanup-by-character
        payloadTemplate: '{"characterId": "{{resourceId}}"}'
    - target: game-service
      sourceType: divine
      field: gameServiceId
      onDelete: cascade
      cleanup:
        endpoint: /divine/cleanup-by-game-service
        payloadTemplate: '{"gameServiceId": "{{resourceId}}"}'
```

**Generated output** (`DivineReferenceTracking.cs`):
```csharp
[ResourceCleanupRequired("CleanupByCharacterAsync")]
[ResourceCleanupRequired("CleanupByGameServiceAsync")]
public partial class DivineService
{
    protected async Task RegisterCharacterReferenceAsync(
        string divineId, Guid characterId,
        CancellationToken cancellationToken = default) { /* ... */ }

    protected async Task UnregisterCharacterReferenceAsync(
        string divineId, Guid characterId,
        CancellationToken cancellationToken = default) { /* ... */ }

    protected async Task RegisterGameServiceReferenceAsync(
        string divineId, Guid gameServiceId,
        CancellationToken cancellationToken = default) { /* ... */ }

    protected async Task UnregisterGameServiceReferenceAsync(
        string divineId, Guid gameServiceId,
        CancellationToken cancellationToken = default) { /* ... */ }

    public static async Task<bool> RegisterResourceCleanupCallbacksAsync(
        IResourceClient resourceClient,
        CancellationToken cancellationToken = default) { /* ... */ }
}
```

### Example 2: RESTRICT with Migrate (Location Service)

A service that blocks realm deletion while locations exist, and provides a migration endpoint for realm merges.

**Schema** (`location-api.yaml`):
```yaml
info:
  title: Location Service
  version: 1.0.0
  x-resource-lifecycle:
    resourceType: location
    gracePeriodSeconds: 604800
    cleanupPolicy: ALL_REQUIRED
  x-references:
    - target: realm
      sourceType: location
      field: realmId
      onDelete: restrict
      migrate:
        endpoint: /location/migrate-by-realm
        payloadTemplate: '{"sourceRealmId": "{{sourceResourceId}}", "targetRealmId": "{{targetResourceId}}"}'
```

**Generated output** (`LocationReferenceTracking.cs`):
```csharp
public partial class LocationService
{
    protected async Task RegisterRealmReferenceAsync(
        string locationId, Guid realmId,
        CancellationToken cancellationToken = default) { /* ... */ }

    protected async Task UnregisterRealmReferenceAsync(
        string locationId, Guid realmId,
        CancellationToken cancellationToken = default) { /* ... */ }

    public static async Task<bool> RegisterResourceMigrateCallbacksAsync(
        IResourceClient resourceClient,
        CancellationToken cancellationToken = default) { /* ... */ }
}
```

---

## Edge Cases & Restrictions

### Forbidden Combinations

| Restriction | Reason |
|---|---|
| `cleanup` section on a `restrict` reference | RESTRICT blocks deletion; cleanup is meaningless. Use `migrate` for merge support. |
| `migrate` section on a `cascade` or `detach` reference | CASCADE/DETACH delete or detach on resource removal; migration is meaningless. Use `cleanup`. |
| Missing `onDelete` on any reference | Every reference must explicitly declare its delete policy. No implicit defaulting. |
| `x-references` without corresponding cleanup/migrate endpoints in `paths` | Structural tests validate endpoint existence. |
| Event-based cleanup for non-account entities | Use x-references instead. Event-based cleanup is forbidden per FOUNDATION TENETS (T28). |
| Using x-references for account references | Account cleanup uses `account.deleted` event subscription, not lib-resource. Privacy requirement. |

### Scoping Rules

- `x-references` is defined per consumer service API schema, not per endpoint
- `x-resource-lifecycle` is defined per foundational service API schema
- A single service may declare multiple x-references entries targeting different foundational resources
- A single foundational resource may have x-references entries from many consumer services
- The `target` in x-references must match the `resourceType` in some foundational service's x-resource-lifecycle

### Interaction with Other Extension Attributes

- **x-resource-lifecycle**: Paired with x-references. The foundational service declares lifecycle policy; consumer services declare reference dependencies.
- **x-compression-callback**: Often co-located with x-references on the same service. Compression provides archival data; references provide cleanup coordination. They are independent concerns.
- **x-lifecycle**: Lifecycle events (created/updated/deleted) in event schemas are separate from resource lifecycle. x-references replaces the need to subscribe to lifecycle events for cleanup.

### Known Limitations

- Payload templates support only `{{resourceId}}`, `{{sourceResourceId}}`, and `{{targetResourceId}}` placeholders
- No support for batch cleanup in a single callback invocation; lib-resource calls the endpoint once per resource deletion
- Grace period in x-resource-lifecycle is optional; when omitted, cleanup executes immediately
