# Documentation Repository Binding - Planning Document

**Status**: PLANNING
**Priority**: Medium-High
**Complexity**: Medium
**Estimated Duration**: 3-4 weeks
**Dependencies**: Documentation Service (complete), Asset Management Plugin (concurrent development)
**Last Updated**: 2025-12-27

---

## Executive Summary

Extend the Documentation Service to support **Git Repository Namespace Binding** - the ability to bind a git repository to a documentation namespace, automatically importing, syncing, and optionally archiving all documentation from that repository.

### Key Features

1. **Repository Binding**: Bind a git repository URL to a documentation namespace
2. **Automatic Sync**: Scheduled and manual synchronization of repository content
3. **Content Transformation**: YAML frontmatter parsing, slug generation, category mapping
4. **Asset Integration**: Archive documentation snapshots as .bannou bundles via Asset Service
5. **Exclusive Ownership**: Bound namespaces are read-only (no manual edits while bound)

### Design Principles

- **Schema-First**: All endpoints defined in OpenAPI YAML before implementation (Tenet 1)
- **Infrastructure Libs Pattern**: State via lib-state, events via lib-messaging (Tenet 4 - ABSOLUTE)
- **POST-Only**: All endpoints follow Tenet 1 for WebSocket routing compatibility
- **LibGit2Sharp**: Pure .NET git operations, no shell command dependencies
- **BackgroundService**: Scheduled sync via .NET `BackgroundService` pattern

---

## Architecture Overview

### High-Level Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    DOCUMENTATION REPOSITORY BINDING                          │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌──────────┐    ┌─────────────────┐    ┌──────────────────┐                │
│  │ Developer│───>│  Documentation  │───>│   LibGit2Sharp   │                │
│  │ (API)    │    │    Service      │    │   (Clone/Pull)   │                │
│  └──────────┘    └────────┬────────┘    └────────┬─────────┘                │
│                           │                       │                          │
│                           │                       ▼                          │
│                           │              ┌──────────────────┐                │
│                           │              │  Local Git Repo  │                │
│                           │              │  (temp storage)  │                │
│                           │              └────────┬─────────┘                │
│                           │                       │                          │
│                           ▼                       ▼                          │
│              ┌─────────────────────────────────────────────────┐            │
│              │              Content Transformation              │            │
│              │  (frontmatter, slugs, categories, @ references)  │            │
│              └─────────────────────────────────────────────────┘            │
│                           │                                                  │
│            ┌──────────────┼──────────────┐                                  │
│            ▼              ▼              ▼                                  │
│     ┌────────────┐ ┌────────────┐ ┌────────────────┐                       │
│     │  lib-state │ │  Search    │ │ Asset Service  │                       │
│     │ (IState-   │ │   Index    │ │ (.bannou arch) │                       │
│     │  Store<T>) │ └────────────┘ └────────────────┘                       │
│     └────────────┘                                                          │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Exclusive Ownership Model

When a namespace is bound to a repository:

| Operation | Allowed? | Notes |
|-----------|----------|-------|
| Read documents | ✅ Yes | Normal read access |
| Create/Update/Delete document | ❌ No | Returns 403 Forbidden |
| Sync from repository | ✅ Yes | Only way to modify content |
| Remove binding | ✅ Yes | Makes namespace manually editable |

**Rationale**: Managing conflicts between manual edits and repository sync is extremely complex. Exclusive ownership keeps the model simple and predictable.

---

## Data Models

### RepositoryBinding Model

**State Store**: `documentation-statestore` via `IStateStore<RepositoryBinding>`
**Key Pattern**: `repo-binding:{namespace}`

```yaml
RepositoryBinding:
  type: object
  required: [bindingId, namespace, repositoryUrl, branch, status, createdAt]
  properties:
    bindingId:
      type: string
      format: uuid
    namespace:
      type: string
      pattern: '^[a-z0-9-]+$'
      maxLength: 50
    repositoryUrl:
      type: string
      description: Git clone URL (HTTPS preferred, SSH supported)
    branch:
      type: string
      default: main
    status:
      type: string
      enum: [pending, syncing, synced, error, disabled]

    # Sync Configuration
    syncEnabled:
      type: boolean
      default: true
    syncIntervalMinutes:
      type: integer
      default: 60
      minimum: 5
      maximum: 1440

    # Content Configuration
    filePatterns:
      type: array
      items: { type: string }
      default: ["**/*.md"]
    excludePatterns:
      type: array
      items: { type: string }
      default: [".git/**", ".obsidian/**", "node_modules/**"]
    categoryMapping:
      type: object
      additionalProperties: { type: string }
    defaultCategory:
      type: string
      default: other

    # Archive Configuration
    archiveEnabled:
      type: boolean
      default: false
    archiveOnSync:
      type: boolean
      default: false

    # Sync State
    lastSyncAt:
      type: string
      format: date-time
      nullable: true
    lastCommitHash:
      type: string
      nullable: true
    documentCount:
      type: integer
      default: 0

    # Metadata
    createdAt:
      type: string
      format: date-time
    createdBy:
      type: string
      format: uuid
```

### SyncResult Model

```yaml
SyncResult:
  type: object
  required: [syncId, status, startedAt, completedAt]
  properties:
    syncId:
      type: string
      format: uuid
    status:
      type: string
      enum: [success, partial, failed]
    startedAt:
      type: string
      format: date-time
    completedAt:
      type: string
      format: date-time
    durationMs:
      type: integer
    commitHash:
      type: string
    documentsCreated:
      type: integer
    documentsUpdated:
      type: integer
    documentsDeleted:
      type: integer
    documentsFailed:
      type: integer
    errorMessage:
      type: string
      nullable: true
```

---

## API Endpoints

All endpoints follow Tenet 1 (POST-only) and Tenet 13 (x-permissions).

### Repository Binding Endpoints

```yaml
/documentation/repo/bind:
  post:
    operationId: bindRepository
    summary: Bind a git repository to a documentation namespace
    x-permissions:
      - role: developer
    requestBody:
      schema:
        $ref: '#/components/schemas/BindRepositoryRequest'
    responses:
      '200': { schema: { $ref: '#/components/schemas/BindRepositoryResponse' } }
      '400': { description: Invalid request }
      '409': { description: Namespace already bound }

/documentation/repo/unbind:
  post:
    operationId: unbindRepository
    summary: Remove repository binding from namespace
    x-permissions:
      - role: admin
    requestBody:
      schema:
        $ref: '#/components/schemas/UnbindRepositoryRequest'

/documentation/repo/sync:
  post:
    operationId: syncRepository
    summary: Manually trigger repository sync
    x-permissions:
      - role: developer
    requestBody:
      schema:
        $ref: '#/components/schemas/SyncRepositoryRequest'

/documentation/repo/status:
  post:
    operationId: getRepositoryStatus
    summary: Get repository binding status
    x-permissions:
      - role: developer

/documentation/repo/list:
  post:
    operationId: listRepositoryBindings
    summary: List all repository bindings
    x-permissions:
      - role: developer

/documentation/repo/update:
  post:
    operationId: updateRepositoryBinding
    summary: Update repository binding configuration
    x-permissions:
      - role: developer
```

### Archive Endpoints

```yaml
/documentation/repo/archive/create:
  post:
    operationId: createDocumentationArchive
    x-permissions: [{ role: developer }]

/documentation/repo/archive/list:
  post:
    operationId: listDocumentationArchives
    x-permissions: [{ role: developer }]

/documentation/repo/archive/restore:
  post:
    operationId: restoreDocumentationArchive
    x-permissions: [{ role: admin }]

/documentation/repo/archive/delete:
  post:
    operationId: deleteDocumentationArchive
    x-permissions: [{ role: admin }]
```

---

## Git Synchronization

### LibGit2Sharp Integration

**Package**: `LibGit2Sharp` (MIT License - Tenet 18 compliant)

```csharp
// Services/IGitSyncService.cs
public interface IGitSyncService
{
    Task<GitSyncResult> SyncRepositoryAsync(
        string repositoryUrl,
        string branch,
        string localPath,
        GitCredentials? credentials = null,
        CancellationToken ct = default);

    Task<IList<GitFileChange>> GetChangedFilesAsync(
        string localPath,
        string? fromCommit,
        string toCommit,
        CancellationToken ct = default);

    Task CleanupRepositoryAsync(string localPath, CancellationToken ct = default);
}
```

### Local Storage Strategy

**Location**: Configurable via `DOCUMENTATION_GIT_STORAGE_PATH`
**Default**: `/tmp/bannou-git-repos/` (ephemeral, reconstructible)

```
/tmp/bannou-git-repos/
├── {bindingId-1}/
│   ├── .git/
│   └── ... (repository files)
└── {bindingId-2}/
```

**Cleanup**: Repositories cleaned up 24 hours after last sync (configurable).

### Sync Algorithm Summary

1. **Acquire distributed lock** via `IDistributedLockProvider` (Tenet 9)
2. **Update status** to "syncing" via `IStateStore<RepositoryBinding>`
3. **Clone or pull** using LibGit2Sharp (shallow clone for efficiency)
4. **Detect changes** via git diff (or full scan for initial sync)
5. **Filter files** by patterns and exclusions
6. **Transform content** (frontmatter, slugs, categories)
7. **Import documents** via existing documentation import API
8. **Delete orphans** (documents no longer in repository)
9. **Update binding** state with sync result
10. **Publish event** via `IMessageBus` (Tenet 4)
11. **Release lock**

---

## Content Transformation

### YAML Frontmatter Parsing

Supported fields:
```yaml
---
title: Document Title           # Overrides filename-based title
category: game-systems          # Must match DocumentCategory enum
tags: [tag1, tag2]              # Additional tags
summary: Brief summary text     # For search results
private: true                   # Exclude from import
slug: custom-slug               # Override auto-generated slug
---
```

### Slug Generation

```
Input:  "01 - Core Concepts/Guardian Spirit System.md"
Output: "01-core-concepts-guardian-spirit-system"
```

Algorithm: Remove extension → Replace `/` with `-` → Lowercase → Replace special chars with `-` → Collapse multiple hyphens.

### Category Mapping

Priority order:
1. Frontmatter `category` field
2. `categoryMapping` configuration (directory prefix match)
3. `defaultCategory` fallback

---

## Scheduled Sync System

### BackgroundService Pattern

Following the established pattern from `SubscriptionExpirationService` and `ServiceHealthMonitor`:

```csharp
// Services/RepositorySyncSchedulerService.cs
public class RepositorySyncSchedulerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RepositorySyncSchedulerService> _logger;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Repository sync scheduler starting, check interval: {Interval}", CheckInterval);

        // Initial delay to allow services to start
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessScheduledSyncsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during scheduled sync check");
                using var scope = _serviceProvider.CreateScope();
                var messageBus = scope.ServiceProvider.GetService<IMessageBus>();
                await messageBus?.TryPublishErrorAsync(
                    "documentation", "ScheduledSync", ex.GetType().Name, ex.Message);
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task ProcessScheduledSyncsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var bindingStore = stateStoreFactory.Create<RepositoryBinding>("documentation");

        // Load all bindings with syncEnabled = true
        // For each binding where now >= lastSyncAt + syncIntervalMinutes:
        //   - Trigger sync operation
        //   - Log result
    }
}
```

### Registration in Plugin

```csharp
// DocumentationServicePlugin.cs
public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    // ... other registrations ...

    // Register background sync scheduler
    services.AddHostedService<RepositorySyncSchedulerService>();
}
```

---

## Infrastructure Lib Usage (Tenet 4 Compliance)

### State Management (lib-state)

```csharp
public class DocumentationService : IDocumentationService
{
    private readonly IStateStore<RepositoryBinding> _bindingStore;
    private readonly IStateStore<Document> _documentStore;

    public DocumentationService(IStateStoreFactory stateStoreFactory)
    {
        _bindingStore = stateStoreFactory.Create<RepositoryBinding>("documentation");
        _documentStore = stateStoreFactory.Create<Document>("documentation");
    }

    public async Task SaveBindingAsync(RepositoryBinding binding, CancellationToken ct)
    {
        await _bindingStore.SaveAsync($"repo-binding:{binding.Namespace}", binding, ct);
    }

    public async Task<RepositoryBinding?> GetBindingAsync(string ns, CancellationToken ct)
    {
        return await _bindingStore.GetAsync($"repo-binding:{ns}", ct);
    }
}
```

### Event Publishing (lib-messaging)

```csharp
// Publish sync completed event (Tenet 5 - typed events only)
await _messageBus.PublishAsync("documentation.sync.completed", new DocumentationSyncCompletedEvent
{
    EventId = Guid.NewGuid(),
    Timestamp = DateTimeOffset.UtcNow,
    Namespace = binding.Namespace,
    BindingId = binding.BindingId,
    SyncResult = result
});
```

### Distributed Locking (lib-state)

```csharp
// Acquire lock for sync operation (Tenet 9 - multi-instance safety)
await using var lockResponse = await _lockProvider.LockAsync(
    resourceId: $"repo-sync:{binding.Namespace}",
    lockOwner: Guid.NewGuid().ToString(),
    expiryInSeconds: 1800,  // 30 minutes
    cancellationToken: ct);

if (!lockResponse.Success)
    return (StatusCodes.Conflict, null);  // Sync already in progress
```

---

## Configuration

### Environment Variables

```yaml
# documentation-configuration.yaml additions
x-service-configuration:
  properties:
    GitStoragePath:
      type: string
      default: "/tmp/bannou-git-repos"
      env: DOCUMENTATION_GIT_STORAGE_PATH

    GitStorageCleanupHours:
      type: integer
      default: 24
      env: DOCUMENTATION_GIT_STORAGE_CLEANUP_HOURS

    GitCloneTimeoutSeconds:
      type: integer
      default: 300
      env: DOCUMENTATION_GIT_CLONE_TIMEOUT_SECONDS

    SyncSchedulerEnabled:
      type: boolean
      default: true
      env: DOCUMENTATION_SYNC_SCHEDULER_ENABLED

    MaxConcurrentSyncs:
      type: integer
      default: 3
      env: DOCUMENTATION_MAX_CONCURRENT_SYNCS

    MaxDocumentSizeBytes:
      type: integer
      default: 524288
      env: DOCUMENTATION_MAX_DOCUMENT_SIZE_BYTES

    MaxDocumentsPerSync:
      type: integer
      default: 1000
      env: DOCUMENTATION_MAX_DOCUMENTS_PER_SYNC
```

---

## Implementation Roadmap

### Phase 1: Core Binding Infrastructure (Week 1)

**Files to Create**:
- Schema additions to `documentation-api.yaml`
- `lib-documentation/Services/IGitSyncService.cs`
- `lib-documentation/Services/GitSyncService.cs`
- `lib-documentation/Services/IContentTransformService.cs`
- `lib-documentation/Services/ContentTransformService.cs`

**Tasks**:
1. Add `LibGit2Sharp` NuGet package
2. Define schema models and endpoints
3. Run `make generate`
4. Implement `GitSyncService` (clone/pull)
5. Implement `ContentTransformService` (frontmatter, slugs, categories)
6. Unit tests

### Phase 2: Binding Management (Week 2)

**Tasks**:
1. Implement binding CRUD endpoints
2. Add 403 Forbidden check for bound namespaces
3. Implement distributed locking for sync operations
4. Unit tests for binding operations

### Phase 3: Sync Engine (Week 3)

**Tasks**:
1. Implement full sync algorithm
2. Implement `RepositorySyncSchedulerService` (BackgroundService)
3. Add sync history tracking
4. Integration tests with test repository

### Phase 4: Asset Integration (Week 4)

**Dependencies**: Asset Service must be available

**Tasks**:
1. Implement archive create/list/restore/delete endpoints
2. Add archive-on-sync option
3. Integration tests with Asset Service

---

## Events Published

```yaml
# documentation-events.yaml additions
x-event-subscriptions: []  # No subscriptions needed for this feature

components:
  schemas:
    DocumentationSyncStartedEvent:
      type: object
      required: [eventId, timestamp, namespace, bindingId, syncId]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        namespace: { type: string }
        bindingId: { type: string, format: uuid }
        syncId: { type: string, format: uuid }
        triggeredBy: { type: string, enum: [manual, scheduled] }

    DocumentationSyncCompletedEvent:
      type: object
      required: [eventId, timestamp, namespace, bindingId, syncId, status]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        namespace: { type: string }
        bindingId: { type: string, format: uuid }
        syncId: { type: string, format: uuid }
        status: { type: string, enum: [success, partial, failed] }
        documentsCreated: { type: integer }
        documentsUpdated: { type: integer }
        documentsDeleted: { type: integer }

    DocumentationArchiveCreatedEvent:
      type: object
      required: [eventId, timestamp, namespace, archiveId]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        namespace: { type: string }
        archiveId: { type: string, format: uuid }
        bundleId: { type: string }
        documentCount: { type: integer }
```

---

## Open Considerations

### Authentication for Private Repositories

**Current**: Public repositories only (no auth)

**Future Enhancement**: Store encrypted credentials per binding, support GitHub/GitLab PATs and SSH keys. Credentials would be stored in a secrets manager, not state store.

### Large Repository Handling

**Current Limits**:
- `MaxDocumentsPerSync`: 1000 documents
- `MaxDocumentSizeBytes`: 500KB per document
- Shallow clone (`--depth 1`) for efficiency

**Future Enhancement**: Sparse checkout, streaming processing, background pool for heavy syncs.

### Webhook Support

**Decision**: Not implementing (complexity vs benefit). Short sync interval (5-15 min) provides near-real-time updates without webhook complexity.

### Multi-Repository Namespaces

**Decision**: Not implementing. Use separate namespaces per repo, link via `relatedDocuments`.

---

## Appendix: arcadia-kb Category Mapping

Pre-configured mapping for the Arcadia knowledge base:

```yaml
categoryMapping:
  "01 - Core Concepts": "game-systems"
  "02 - World Lore": "world-lore"
  "03 - Character Systems": "game-systems"
  "04 - Game Systems": "game-systems"
  "05 - NPC AI Design": "npc-ai"
  "06 - Technical Architecture": "architecture"
  "07 - Implementation Guides": "tutorials"
  "08 - Crafting Systems": "game-systems"
  "09 - Engine Research": "architecture"
  "Claude": "other"

excludePatterns:
  - ".git/**"
  - ".obsidian/**"
  - ".claude/**"
  - "Templates/**"
  - "*.zip"
  - "*.json"

tagPrefix: "arcadia"
defaultCategory: "other"
autoGenerateTags: true
```

---

*This document is the authoritative source for Documentation Repository Binding implementation. Updates require review and approval.*
