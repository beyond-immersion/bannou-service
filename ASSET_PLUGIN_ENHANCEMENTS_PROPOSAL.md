# Asset Plugin Enhancement Proposal

## Executive Summary

This proposal outlines enhancements to `lib-asset` and the Bannou SDKs to provide complete bundle lifecycle management. Currently, bundles can be created and read but not updated or deleted, making maintenance and migrations impossible. This proposal addresses these gaps with a cohesive API design that supports single and bulk operations, versioning, soft delete with retention, and a powerful migration system.

---

## Current State Analysis

### What lib-asset Currently Provides

| Operation | Endpoint | Status |
|-----------|----------|--------|
| Upload asset | `POST /assets/upload/request` | ✅ |
| Complete upload | `POST /assets/upload/complete` | ✅ |
| Upload bundle | `POST /bundles/upload/request` | ✅ |
| Get bundle | `GET /bundles/{id}` | ✅ |
| Create metabundle | `POST /metabundles` | ✅ |
| **Update bundle** | - | ❌ Missing |
| **Delete bundle** | - | ❌ Missing |
| **Bulk operations** | - | ❌ Missing |
| **Versioning** | - | ❌ Missing |
| **Migration** | - | ❌ Missing |

### Pain Points

1. **No update path**: Once uploaded, bundle metadata (name, tags, description) cannot be modified
2. **No delete capability**: Test bundles, failed uploads, and deprecated content cannot be removed
3. **No bulk operations**: Managing hundreds of bundles requires individual API calls
4. **Duplicate rejection blocks iteration**: Re-uploading returns 409 Conflict with no override option
5. **No migration support**: Schema changes (like asset ID format changes) require manual intervention
6. **No versioning**: No history of changes, no rollback capability

---

## Design Principles

### 1. REST Semantics with Bulk Extensions

Standard REST for single resources, dedicated bulk endpoints for efficiency:

```
Single:  PATCH /bundles/{id}
Bulk:    POST  /bundles/bulk/update
```

**Why not array parameters on single endpoints?**
- Cleaner API contracts
- Different authorization requirements (bulk = admin)
- Different rate limiting
- Easier to document and test

### 2. Immutable Content, Mutable Metadata

Bundle binary content (LZ4-compressed assets) is immutable after upload. Only metadata can be updated:
- Name, description, tags
- Asset metadata within manifest
- Ownership and permissions

To change binary content: create new version (new upload with version increment).

### 3. Soft Delete with Configurable Retention

Never hard-delete immediately:
- Mark as deleted with timestamp
- Configurable retention period (default: 30 days)
- Can restore within retention window
- Background job purges after retention expires

### 4. Explicit Versioning

Every metadata mutation creates a version record:
- Version 1: Initial upload
- Version 2: Updated tags
- Version 3: Changed name
- etc.

Can retrieve any historical version's metadata. Binary content is shared (deduplicated by content hash).

---

## Proposed API Design

### Bundle CRUD Operations

#### Get Bundle (existing, enhanced)

```
GET /bundles/{bundleId}
```

Response additions:
```json
{
  "bundleId": "synty/polygon-adventure",
  "version": 3,
  "status": "active",           // NEW: active | deleted | processing
  "createdAt": "...",
  "updatedAt": "...",           // NEW
  "deletedAt": null,            // NEW: null or ISO timestamp
  ...
}
```

#### Update Bundle Metadata

```
PATCH /bundles/{bundleId}
```

Request:
```json
{
  "name": "POLYGON Adventure Pack (Updated)",
  "description": "New description",
  "tags": {
    "source": "synty",
    "category": "polygon",
    "rating": "5"
  },
  "addTags": { "featured": "true" },      // Merge with existing
  "removeTags": ["deprecated"]             // Remove specific tags
}
```

Response:
```json
{
  "bundleId": "synty/polygon-adventure",
  "version": 4,                            // Incremented
  "previousVersion": 3,
  "changes": ["name", "tags"]
}
```

**Semantics:**
- `name`, `description`, `tags`: Replace if provided
- `addTags`: Merge into existing tags
- `removeTags`: Remove specified tag keys
- All operations atomic

#### Delete Bundle (Soft Delete)

```
DELETE /bundles/{bundleId}
```

Query parameters:
- `permanent=true`: Skip soft delete, immediate purge (requires elevated permission)

Response:
```json
{
  "bundleId": "synty/polygon-adventure",
  "status": "deleted",
  "deletedAt": "2026-01-15T12:00:00Z",
  "retentionUntil": "2026-02-14T12:00:00Z",   // Can restore until this time
  "permanentlyDeleted": false
}
```

#### Restore Bundle

```
POST /bundles/{bundleId}/restore
```

Response:
```json
{
  "bundleId": "synty/polygon-adventure",
  "status": "active",
  "restoredAt": "2026-01-15T12:30:00Z",
  "restoredFrom": {
    "deletedAt": "2026-01-15T12:00:00Z",
    "version": 4
  }
}
```

#### Replace Bundle (Full Re-upload)

```
PUT /bundles/{bundleId}
```

This initiates a new upload that will **replace** the existing bundle:

Request:
```json
{
  "filename": "polygon-adventure.bannou",
  "size": 25793689,
  "manifestPreview": { ... },
  "owner": "syntybundler",
  "reason": "Asset ID format migration"      // Audit trail
}
```

Response: Same as `POST /bundles/upload/request` but with:
```json
{
  "uploadId": "...",
  "uploadUrl": "...",
  "replaces": {
    "bundleId": "synty/polygon-adventure",
    "version": 4,
    "willBecomeVersion": 5
  }
}
```

**Semantics:**
- Existing bundle remains accessible during upload
- On `CompleteUpload`, atomic swap to new content
- Previous version archived (retrievable via version history)

---

### Version History

#### List Versions

```
GET /bundles/{bundleId}/versions
```

Query parameters:
- `limit`: Max results (default: 50)
- `offset`: Pagination offset
- `includeDeleted`: Include deleted versions (default: false)

Response:
```json
{
  "bundleId": "synty/polygon-adventure",
  "currentVersion": 5,
  "versions": [
    {
      "version": 5,
      "createdAt": "2026-01-15T13:00:00Z",
      "createdBy": "syntybundler",
      "changes": ["content"],
      "reason": "Asset ID format migration"
    },
    {
      "version": 4,
      "createdAt": "2026-01-15T10:00:00Z",
      "createdBy": "admin",
      "changes": ["tags"],
      "reason": null
    },
    ...
  ],
  "totalCount": 5
}
```

#### Get Specific Version

```
GET /bundles/{bundleId}/versions/{version}
```

Response: Full bundle metadata as it existed at that version.

#### Rollback to Version

```
POST /bundles/{bundleId}/rollback
```

Request:
```json
{
  "targetVersion": 3,
  "reason": "Reverting broken migration"
}
```

Response:
```json
{
  "bundleId": "synty/polygon-adventure",
  "previousVersion": 5,
  "newVersion": 6,               // Rollback creates new version
  "restoredFromVersion": 3,
  "changes": ["content", "name", "tags"]
}
```

---

### Bulk Operations

All bulk operations are **POST** (not idempotent, have side effects).

#### Bulk Update

```
POST /bundles/bulk/update
```

Request:
```json
{
  "targets": {
    "bundleIds": ["synty/polygon-adventure", "synty/polygon-fantasy"],
    // OR
    "query": {
      "tags": { "source": "synty", "category": "polygon" },
      "status": "active",
      "createdAfter": "2026-01-01T00:00:00Z"
    }
  },
  "update": {
    "addTags": { "migrated": "true", "assetIdFormat": "v2" },
    "removeTags": ["needsMigration"]
  },
  "dryRun": false,
  "reason": "Post-migration tagging"
}
```

Response:
```json
{
  "jobId": "bulk-update-abc123",
  "status": "completed",           // queued | processing | completed | failed
  "stats": {
    "targeted": 126,
    "updated": 126,
    "skipped": 0,
    "failed": 0
  },
  "duration": "PT2.5S",
  "results": [                      // Only if small batch, otherwise query job
    { "bundleId": "synty/polygon-adventure", "newVersion": 5, "status": "updated" },
    ...
  ]
}
```

#### Bulk Delete

```
POST /bundles/bulk/delete
```

Request:
```json
{
  "targets": {
    "bundleIds": ["test/bundle-1", "test/bundle-2"],
    // OR
    "query": {
      "tags": { "environment": "test" },
      "createdBefore": "2026-01-01T00:00:00Z"
    }
  },
  "permanent": false,
  "reason": "Cleanup test bundles"
}
```

#### Bulk Restore

```
POST /bundles/bulk/restore
```

Request:
```json
{
  "targets": {
    "bundleIds": ["synty/polygon-adventure"],
    // OR
    "query": {
      "status": "deleted",
      "deletedAfter": "2026-01-14T00:00:00Z"
    }
  },
  "reason": "Accidental deletion recovery"
}
```

#### Query Bundles

```
POST /bundles/query
```

Request:
```json
{
  "filters": {
    "tags": { "source": "synty" },
    "tagExists": ["category"],
    "tagNotExists": ["deprecated"],
    "status": "active",
    "createdAfter": "2026-01-01T00:00:00Z",
    "createdBefore": "2026-02-01T00:00:00Z",
    "sizeGreaterThan": 1000000,
    "sizeLessThan": 100000000,
    "nameContains": "polygon",
    "owner": "syntybundler"
  },
  "sort": {
    "field": "createdAt",           // createdAt | updatedAt | size | name
    "order": "desc"
  },
  "limit": 100,
  "offset": 0,
  "includeDeleted": false
}
```

Response:
```json
{
  "bundles": [ ... ],
  "totalCount": 126,
  "limit": 100,
  "offset": 0
}
```

---

### Migration System

Migrations are long-running jobs that transform bundle/asset metadata according to defined rules.

#### Create Migration

```
POST /bundles/migrations
```

Request:
```json
{
  "name": "Asset ID Format Migration v1→v2",
  "description": "Update asset IDs from flat format to hierarchical format",
  "targets": {
    "query": {
      "tags": { "source": "synty" },
      "tagNotExists": ["assetIdFormat:v2"]
    }
  },
  "transforms": [
    {
      "type": "assetIdRemap",
      "config": {
        "pattern": "^([A-Z]+)_(.+)$",
        "replacement": "{{bundleId}}/$2_{{index:04d}}",
        "scope": "manifest"                    // manifest | index | both
      }
    },
    {
      "type": "addTag",
      "config": {
        "key": "assetIdFormat",
        "value": "v2"
      }
    },
    {
      "type": "addTag",
      "config": {
        "key": "migratedAt",
        "value": "{{now:iso}}"
      }
    }
  ],
  "options": {
    "dryRun": true,                            // Preview changes without applying
    "stopOnError": false,                      // Continue with next bundle on error
    "batchSize": 10,                           // Bundles per batch
    "delayBetweenBatches": "PT1S"              // Rate limiting
  }
}
```

Response:
```json
{
  "jobId": "migration-xyz789",
  "name": "Asset ID Format Migration v1→v2",
  "status": "pending",                         // pending | dryRunComplete | executing | completed | failed | cancelled
  "createdAt": "2026-01-15T14:00:00Z",
  "createdBy": "admin",
  "targets": {
    "estimatedCount": 126,
    "query": { ... }
  },
  "dryRun": true
}
```

#### Get Migration Status

```
GET /bundles/migrations/{jobId}
```

Response:
```json
{
  "jobId": "migration-xyz789",
  "name": "Asset ID Format Migration v1→v2",
  "status": "dryRunComplete",
  "createdAt": "2026-01-15T14:00:00Z",
  "startedAt": "2026-01-15T14:00:01Z",
  "completedAt": "2026-01-15T14:02:30Z",
  "stats": {
    "total": 126,
    "processed": 126,
    "wouldSucceed": 124,
    "wouldFail": 2,
    "skipped": 0
  },
  "errors": [
    {
      "bundleId": "synty/broken-pack",
      "error": "Asset ID collision: 'model_0001' already exists",
      "transform": "assetIdRemap"
    }
  ],
  "preview": {
    "sampleBundles": [
      {
        "bundleId": "synty/polygon-adventure",
        "changes": {
          "assetsRenamed": 47,
          "tagsAdded": ["assetIdFormat:v2", "migratedAt:2026-01-15T14:00:00Z"],
          "manifestSizeChange": "+234 bytes"
        }
      }
    ]
  }
}
```

#### Execute Migration

After reviewing dry-run results:

```
POST /bundles/migrations/{jobId}/execute
```

Request:
```json
{
  "confirm": true,
  "skipErrors": true,                          // Skip bundles that failed in dry-run
  "reason": "Approved after dry-run review"
}
```

Response:
```json
{
  "jobId": "migration-xyz789",
  "status": "executing",
  "startedAt": "2026-01-15T14:05:00Z",
  "estimatedCompletion": "2026-01-15T14:10:00Z"
}
```

#### Cancel Migration

```
POST /bundles/migrations/{jobId}/cancel
```

Request:
```json
{
  "reason": "Found issues in preview"
}
```

#### List Migrations

```
GET /bundles/migrations
```

Query parameters:
- `status`: Filter by status
- `limit`, `offset`: Pagination

---

### Transform Types

#### `assetIdRemap`

Rename asset IDs within bundle manifest.

```json
{
  "type": "assetIdRemap",
  "config": {
    "pattern": "regex pattern with capture groups",
    "replacement": "template with {{bundleId}}, {{index}}, {{capture:1}}",
    "scope": "manifest"
  }
}
```

#### `tagUpdate`

Add, modify, or remove tags.

```json
{
  "type": "tagUpdate",
  "config": {
    "add": { "key": "value" },
    "remove": ["keyToRemove"],
    "rename": { "oldKey": "newKey" }
  }
}
```

#### `metadataUpdate`

Update bundle-level metadata.

```json
{
  "type": "metadataUpdate",
  "config": {
    "name": "{{name}} (Migrated)",
    "description": "{{description}}\n\nMigrated on {{now:iso}}"
  }
}
```

#### `assetMetadataUpdate`

Update metadata on assets within bundles.

```json
{
  "type": "assetMetadataUpdate",
  "config": {
    "filter": {
      "contentType": "application/x-stride-model"
    },
    "update": {
      "addMetadata": { "processorVersion": "2.0" },
      "removeMetadata": ["legacyField"]
    }
  }
}
```

#### `contentTypeRemap`

Update content types (MIME types) of assets.

```json
{
  "type": "contentTypeRemap",
  "config": {
    "mappings": {
      "application/x-stride-binary": "application/x-stride-model"
    }
  }
}
```

---

### Asset-Level Operations

For fine-grained control over assets within bundles.

#### Update Asset Metadata

```
PATCH /bundles/{bundleId}/assets/{assetId}
```

Request:
```json
{
  "metadata": {
    "strideGuid": "new-guid",
    "customField": "value"
  },
  "tags": ["featured", "highQuality"]
}
```

**Note:** Cannot change `assetId`, `contentType`, `contentHash`, or binary data. Only supplementary metadata.

#### Bulk Update Assets

```
POST /bundles/{bundleId}/assets/bulk/update
```

Request:
```json
{
  "targets": {
    "assetIds": ["asset1", "asset2"],
    // OR
    "filter": {
      "contentType": "application/x-stride-texture",
      "metadataContains": { "textureType": "NormalMap" }
    }
  },
  "update": {
    "addMetadata": { "processed": "true" },
    "addTags": ["normalMap"]
  }
}
```

---

## SDK Design

### C# SDK (BeyondImmersion.Bannou.AssetBundler)

```csharp
namespace BeyondImmersion.Bannou.AssetBundler;

/// <summary>
/// Client for bundle management operations.
/// </summary>
public interface IBundleClient
{
    // === Single Bundle Operations ===

    Task<BundleMetadata> GetAsync(string bundleId, CancellationToken ct = default);

    Task<BundleUpdateResult> UpdateAsync(
        string bundleId,
        BundleMetadataUpdate update,
        CancellationToken ct = default);

    Task<BundleDeleteResult> DeleteAsync(
        string bundleId,
        bool permanent = false,
        string? reason = null,
        CancellationToken ct = default);

    Task<BundleRestoreResult> RestoreAsync(
        string bundleId,
        string? reason = null,
        CancellationToken ct = default);

    Task<BundleReplaceResult> ReplaceAsync(
        string bundleId,
        string bundlePath,
        string? reason = null,
        IProgress<UploadProgress>? progress = null,
        CancellationToken ct = default);

    // === Version Operations ===

    Task<BundleVersionList> GetVersionsAsync(
        string bundleId,
        int limit = 50,
        int offset = 0,
        CancellationToken ct = default);

    Task<BundleMetadata> GetVersionAsync(
        string bundleId,
        int version,
        CancellationToken ct = default);

    Task<BundleRollbackResult> RollbackAsync(
        string bundleId,
        int targetVersion,
        string? reason = null,
        CancellationToken ct = default);

    // === Query ===

    Task<BundleQueryResult> QueryAsync(
        BundleQuery query,
        CancellationToken ct = default);

    IAsyncEnumerable<BundleMetadata> QueryAllAsync(
        BundleQuery query,
        CancellationToken ct = default);
}

/// <summary>
/// Client for bulk bundle operations.
/// </summary>
public interface IBulkBundleClient
{
    Task<BulkOperationResult> UpdateAsync(
        BulkTarget targets,
        BundleMetadataUpdate update,
        bool dryRun = false,
        string? reason = null,
        CancellationToken ct = default);

    Task<BulkOperationResult> DeleteAsync(
        BulkTarget targets,
        bool permanent = false,
        string? reason = null,
        CancellationToken ct = default);

    Task<BulkOperationResult> RestoreAsync(
        BulkTarget targets,
        string? reason = null,
        CancellationToken ct = default);
}

/// <summary>
/// Client for migration operations.
/// </summary>
public interface IMigrationClient
{
    Task<Migration> CreateAsync(
        MigrationDefinition definition,
        CancellationToken ct = default);

    Task<Migration> GetAsync(
        string jobId,
        CancellationToken ct = default);

    Task<Migration> ExecuteAsync(
        string jobId,
        bool skipErrors = false,
        string? reason = null,
        CancellationToken ct = default);

    Task<Migration> CancelAsync(
        string jobId,
        string? reason = null,
        CancellationToken ct = default);

    Task<MigrationList> ListAsync(
        MigrationStatus? status = null,
        int limit = 50,
        int offset = 0,
        CancellationToken ct = default);

    IAsyncEnumerable<MigrationProgress> WatchAsync(
        string jobId,
        CancellationToken ct = default);
}
```

### Usage Examples

```csharp
// Update single bundle
await client.Bundles.UpdateAsync("synty/polygon-adventure", new BundleMetadataUpdate
{
    Name = "POLYGON Adventure Pack",
    AddTags = new() { ["featured"] = "true" }
});

// Bulk update all Synty bundles
await client.Bundles.Bulk.UpdateAsync(
    new BulkTarget { Query = new BundleQuery { Tags = new() { ["source"] = "synty" } } },
    new BundleMetadataUpdate { AddTags = new() { ["vendor"] = "synty" } },
    reason: "Standardize vendor tagging");

// Create and execute migration
var migration = await client.Migrations.CreateAsync(new MigrationDefinition
{
    Name = "Asset ID Migration",
    Targets = new BulkTarget
    {
        Query = new BundleQuery { Tags = new() { ["source"] = "synty" } }
    },
    Transforms =
    [
        new AssetIdRemapTransform
        {
            Pattern = @"^(.+)$",
            Replacement = "{{bundleId}}/$1_{{index:04d}}"
        },
        new TagUpdateTransform
        {
            Add = new() { ["assetIdFormat"] = "v2" }
        }
    ],
    Options = new MigrationOptions { DryRun = true }
});

Console.WriteLine($"Dry run complete: {migration.Stats.WouldSucceed} would succeed");

// Review and execute
if (migration.Stats.WouldFail == 0)
{
    await client.Migrations.ExecuteAsync(migration.JobId);

    // Watch progress
    await foreach (var progress in client.Migrations.WatchAsync(migration.JobId))
    {
        Console.WriteLine($"Progress: {progress.Processed}/{progress.Total}");
    }
}
```

---

## Implementation Notes

### State Store Schema

**New Keys:**

| Key Pattern | Type | Purpose |
|-------------|------|---------|
| `bundle:{bundleId}` | `BundleMetadata` | Current bundle state (existing) |
| `bundle-version:{bundleId}:{version}` | `BundleVersionRecord` | Historical versions |
| `bundle-deleted:{bundleId}` | `DeletedBundleRecord` | Soft-deleted bundles (with TTL) |
| `migration:{jobId}` | `MigrationJob` | Migration job state |
| `migration-progress:{jobId}` | `MigrationProgress` | Real-time progress |

**TTLs:**
- Soft-deleted bundles: Configurable (default 30 days)
- Migration jobs: 7 days after completion
- Upload sessions: Existing (15 minutes)

### Storage Handling

**Soft Delete:**
1. Move storage object to `deleted/` prefix
2. Set TTL-based lifecycle rule on `deleted/` prefix
3. Or: Add delete marker, background job cleans up

**Version Storage:**
- Metadata versions stored in state store (small)
- Binary content NOT duplicated (immutable, referenced by content hash)
- Version records point to same storage key

### Migration Engine

**Architecture:**
```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│  API Endpoint   │────▶│  Migration Queue │────▶│ Migration Worker│
│  (Create Job)   │     │    (Pub/Sub)     │     │  (Pool Node)    │
└─────────────────┘     └──────────────────┘     └─────────────────┘
                                                          │
                                                          ▼
                                                 ┌─────────────────┐
                                                 │  State Store    │
                                                 │  (Progress)     │
                                                 └─────────────────┘
```

**Workflow:**
1. Create migration → validate, store job, return pending
2. If `dryRun`: Process all bundles in simulation mode, store results
3. Execute: Process bundles for real, update state, emit progress events
4. Client can watch progress via WebSocket subscription

**Atomicity:**
- Each bundle update is atomic (all transforms succeed or none)
- Failed bundles don't affect others (unless `stopOnError: true`)
- Rollback: Create reverse migration from version history

### Authorization

| Operation | Required Permission |
|-----------|---------------------|
| Get bundle | `bundles:read` |
| Update bundle | `bundles:write` |
| Delete bundle (soft) | `bundles:delete` |
| Delete bundle (permanent) | `bundles:admin` |
| Restore bundle | `bundles:delete` |
| Bulk operations | `bundles:admin` |
| Create migration | `bundles:admin` |
| Execute migration | `bundles:admin` |

### Rate Limiting

| Operation | Default Limit |
|-----------|---------------|
| Single updates | 100/minute |
| Single deletes | 50/minute |
| Bulk operations | 10/minute |
| Migrations | 5 concurrent |

---

## Migration Path for SyntyBundler

With this proposal implemented, the asset ID format migration becomes:

```csharp
// Option 1: Use migration system (recommended)
var migration = await client.Migrations.CreateAsync(new MigrationDefinition
{
    Name = "Synty Asset ID Format Migration",
    Targets = new BulkTarget
    {
        Query = new BundleQuery
        {
            Tags = new() { ["source"] = "synty" },
            TagNotExists = ["assetIdFormat:v2"]
        }
    },
    Transforms =
    [
        new AssetIdRemapTransform
        {
            // Old: FBX_Characters_SK_Fighter_01
            // New: synty/polygon-adventure/SK_Fighter_01_0023
            Pattern = @"^[A-Z]+_(?:.+_)?(.+)$",
            Replacement = "{{bundleId}}/$1_{{index:04d}}"
        },
        new TagUpdateTransform
        {
            Add = new()
            {
                ["assetIdFormat"] = "v2",
                ["migratedAt"] = "{{now:iso}}"
            }
        }
    ],
    Options = new() { DryRun = true }
});

// Review dry run, then execute
await client.Migrations.ExecuteAsync(migration.JobId);

// Option 2: Bulk tag update only (if asset IDs don't actually need to change)
await client.Bundles.Bulk.UpdateAsync(
    new BulkTarget { Query = new BundleQuery { Tags = new() { ["source"] = "synty" } } },
    new BundleMetadataUpdate { AddTags = new() { ["sdkVersion"] = "2.0" } });
```

---

## Open Questions

1. **Should migrations support binary content changes?**
   - Current proposal: No (metadata only)
   - Full re-processing would require re-upload via Replace API
   - Could add "reprocess" transform that re-runs asset pipeline

2. **Version retention policy?**
   - Keep all versions forever? (storage cost)
   - Auto-prune after N versions? (configurable)
   - Keep versions for N days? (time-based)

3. **Cross-bundle migrations?**
   - Current proposal: Each bundle migrated independently
   - Could add "merge bundles" or "split bundle" transforms
   - Adds significant complexity

4. **Real-time progress via WebSocket?**
   - Proposed: Yes, via existing WebSocket infrastructure
   - Alternative: Polling via REST
   - Hybrid: REST for status, WebSocket for live progress

---

## Implementation Priority

### Phase 1: Core CRUD (Required)
- [ ] `PATCH /bundles/{bundleId}` - Update metadata
- [ ] `DELETE /bundles/{bundleId}` - Soft delete
- [ ] `POST /bundles/{bundleId}/restore` - Restore
- [ ] SDK: `IBundleClient` basic operations

### Phase 2: Versioning
- [ ] Version tracking on all mutations
- [ ] `GET /bundles/{bundleId}/versions` - List versions
- [ ] `GET /bundles/{bundleId}/versions/{v}` - Get version
- [ ] `POST /bundles/{bundleId}/rollback` - Rollback

### Phase 3: Bulk Operations
- [ ] `POST /bundles/query` - Query bundles
- [ ] `POST /bundles/bulk/update` - Bulk update
- [ ] `POST /bundles/bulk/delete` - Bulk delete
- [ ] SDK: `IBulkBundleClient`

### Phase 4: Migration System
- [ ] Migration job infrastructure
- [ ] Transform engine
- [ ] Dry-run support
- [ ] Progress tracking
- [ ] SDK: `IMigrationClient`

### Phase 5: Replace/Advanced
- [ ] `PUT /bundles/{bundleId}` - Replace bundle
- [ ] Asset-level operations
- [ ] Background purge job for soft-deleted bundles

---

## Appendix: Type Definitions

```csharp
public record BundleMetadataUpdate
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public Dictionary<string, string>? Tags { get; init; }
    public Dictionary<string, string>? AddTags { get; init; }
    public List<string>? RemoveTags { get; init; }
}

public record BulkTarget
{
    public List<string>? BundleIds { get; init; }
    public BundleQuery? Query { get; init; }
}

public record BundleQuery
{
    public Dictionary<string, string>? Tags { get; init; }
    public List<string>? TagExists { get; init; }
    public List<string>? TagNotExists { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? CreatedAfter { get; init; }
    public DateTimeOffset? CreatedBefore { get; init; }
    public long? SizeGreaterThan { get; init; }
    public long? SizeLessThan { get; init; }
    public string? NameContains { get; init; }
    public string? Owner { get; init; }
    public string? SortField { get; init; }
    public string? SortOrder { get; init; }
    public int Limit { get; init; } = 100;
    public int Offset { get; init; } = 0;
    public bool IncludeDeleted { get; init; } = false;
}

public record MigrationDefinition
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required BulkTarget Targets { get; init; }
    public required List<ITransform> Transforms { get; init; }
    public MigrationOptions Options { get; init; } = new();
}

public record MigrationOptions
{
    public bool DryRun { get; init; } = true;
    public bool StopOnError { get; init; } = false;
    public int BatchSize { get; init; } = 10;
    public TimeSpan? DelayBetweenBatches { get; init; }
}

public interface ITransform { }

public record AssetIdRemapTransform : ITransform
{
    public required string Pattern { get; init; }
    public required string Replacement { get; init; }
    public string Scope { get; init; } = "manifest";
}

public record TagUpdateTransform : ITransform
{
    public Dictionary<string, string>? Add { get; init; }
    public List<string>? Remove { get; init; }
    public Dictionary<string, string>? Rename { get; init; }
}
```

---

*Document created: 2026-01-15*
*Author: Claude (with direction from development team)*
*Status: PROPOSAL - Pending Review*
