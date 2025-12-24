# Documentation Repository Binding - Planning Document

**Status**: PLANNING
**Priority**: Medium-High
**Complexity**: Medium
**Estimated Duration**: 3-4 weeks
**Dependencies**: Documentation Service (complete), Asset Management Plugin (concurrent development)
**Last Updated**: 2025-12-23

---

## Executive Summary

This document outlines the design for extending the Documentation Service to support **Git Repository Namespace Binding** - the ability to bind a git repository to a documentation namespace, automatically importing, syncing, and optionally archiving all documentation from that repository.

### Key Features

1. **Repository Binding**: Bind a git repository URL to a documentation namespace
2. **Automatic Sync**: Scheduled and manual synchronization of repository content
3. **Content Transformation**: YAML frontmatter parsing, slug generation, category mapping
4. **Asset Integration**: Archive documentation snapshots as .bannou bundles via Asset Service
5. **Exclusive Ownership**: Bound namespaces are read-only (no manual edits while bound)

### Design Principles

- **Schema-First**: All new endpoints defined in OpenAPI YAML before implementation
- **Dapr-First**: State management via Dapr, no direct database access
- **POST-Only**: All endpoints follow Tenet 1 for WebSocket routing compatibility
- **LibGit2Sharp**: Pure .NET git operations, no shell command dependencies
- **Asset Integration**: Documentation archives use Asset Service .bannou bundle format

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Data Models](#2-data-models)
3. [API Endpoints](#3-api-endpoints)
4. [Git Synchronization](#4-git-synchronization)
5. [Content Transformation](#5-content-transformation)
6. [Asset Integration](#6-asset-integration)
7. [Scheduled Sync System](#7-scheduled-sync-system)
8. [Configuration](#8-configuration)
9. [Implementation Roadmap](#9-implementation-roadmap)
10. [Open Considerations](#10-open-considerations)

---

## 1. Architecture Overview

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
│              ├─────────────────────────────────────────────────┤            │
│              │  1. Scan for markdown files (*.md)              │            │
│              │  2. Filter exclusions (patterns, frontmatter)   │            │
│              │  3. Parse YAML frontmatter (if present)         │            │
│              │  4. Map folder structure to categories          │            │
│              │  5. Generate slugs from file paths              │            │
│              │  6. Extract tags from frontmatter/structure     │            │
│              │  7. Transform @ references to internal links    │            │
│              └─────────────────────────────────────────────────┘            │
│                           │                                                  │
│                           ▼                                                  │
│              ┌─────────────────────────────────────────────────┐            │
│              │           Documentation Import API               │            │
│              │     POST /documentation/import (existing)        │            │
│              │     conflictResolution: "update"                 │            │
│              └─────────────────────────────────────────────────┘            │
│                           │                                                  │
│            ┌──────────────┼──────────────┐                                  │
│            ▼              ▼              ▼                                  │
│     ┌────────────┐ ┌────────────┐ ┌────────────────┐                       │
│     │   State    │ │  Search    │ │ Asset Service  │                       │
│     │   Store    │ │   Index    │ │ (.bannou arch) │                       │
│     └────────────┘ └────────────┘ └────────────────┘                       │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Exclusive Ownership Model

When a namespace is bound to a repository:

| Operation | Allowed? | Notes |
|-----------|----------|-------|
| Read documents (query, get, search, list) | ✅ Yes | Normal read access |
| Create document | ❌ No | Returns 403 Forbidden |
| Update document | ❌ No | Returns 403 Forbidden |
| Delete document | ❌ No | Returns 403 Forbidden |
| Sync from repository | ✅ Yes | Only way to modify content |
| Remove binding | ✅ Yes | Makes namespace manually editable |

**Rationale**: Managing conflicts between manual edits and repository sync is extremely complex. Exclusive ownership keeps the model simple and predictable.

### Binding Lifecycle

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│   Empty     │────>│   Bound     │────>│  Syncing    │────>│   Bound     │
│  Namespace  │     │  (pending)  │     │             │     │  (synced)   │
└─────────────┘     └─────────────┘     └─────────────┘     └─────────────┘
                           │                                       │
                           │                                       │
                           ▼                                       ▼
                    ┌─────────────┐                         ┌─────────────┐
                    │   Unbind    │                         │   Resync    │
                    │  (manual)   │                         │  (manual/   │
                    └──────┬──────┘                         │  scheduled) │
                           │                                └─────────────┘
                           ▼
                    ┌─────────────┐
                    │  Namespace  │
                    │  (manual    │
                    │   editable) │
                    └─────────────┘
```

---

## 2. Data Models

### 2.1 RepositoryBinding Model

Stored in Dapr state store: `documentation-statestore`
Key pattern: `repo-binding:{namespace}`

```yaml
RepositoryBinding:
  type: object
  required:
    - bindingId
    - namespace
    - repositoryUrl
    - branch
    - status
    - createdAt
  properties:
    bindingId:
      type: string
      format: uuid
      description: Unique identifier for this binding
    namespace:
      type: string
      pattern: '^[a-z0-9-]+$'
      maxLength: 50
      description: Documentation namespace this repo populates
    repositoryUrl:
      type: string
      description: Git clone URL (HTTPS preferred, SSH supported)
    branch:
      type: string
      default: main
      description: Branch to track
    status:
      $ref: '#/components/schemas/BindingStatus'

    # Sync Configuration
    syncConfig:
      $ref: '#/components/schemas/SyncConfiguration'

    # Content Configuration
    contentConfig:
      $ref: '#/components/schemas/ContentConfiguration'

    # Archive Configuration
    archiveConfig:
      $ref: '#/components/schemas/ArchiveConfiguration'

    # Sync State
    lastSyncAt:
      type: string
      format: date-time
      nullable: true
    lastCommitHash:
      type: string
      nullable: true
    lastSyncResult:
      $ref: '#/components/schemas/SyncResult'
      nullable: true

    # Statistics
    documentCount:
      type: integer
      default: 0
    totalContentSizeBytes:
      type: integer
      format: int64
      default: 0

    # Metadata
    createdAt:
      type: string
      format: date-time
    updatedAt:
      type: string
      format: date-time
    createdBy:
      type: string
      format: uuid
      description: Account ID that created the binding

BindingStatus:
  type: string
  enum:
    - pending      # Binding created, initial sync not yet run
    - syncing      # Sync in progress
    - synced       # Successfully synced
    - error        # Last sync failed
    - disabled     # Sync disabled (manual intervention required)

SyncConfiguration:
  type: object
  properties:
    syncEnabled:
      type: boolean
      default: true
      description: Whether scheduled sync is enabled
    syncIntervalMinutes:
      type: integer
      default: 60
      minimum: 5
      maximum: 1440
      description: Minutes between scheduled syncs (5 min to 24 hours)

ContentConfiguration:
  type: object
  properties:
    filePatterns:
      type: array
      items:
        type: string
      default: ["**/*.md"]
      description: Glob patterns for files to include
    excludePatterns:
      type: array
      items:
        type: string
      default: [".git/**", ".obsidian/**", "node_modules/**"]
      description: Glob patterns for files/directories to exclude
    excludeBinaryFiles:
      type: boolean
      default: true
      description: Automatically exclude common binary file extensions
    excludeCodeFiles:
      type: boolean
      default: true
      description: Automatically exclude common code file extensions (.cs, .ts, .py, etc.)
    categoryMapping:
      type: object
      additionalProperties:
        type: string
      description: "Directory path prefix -> DocumentCategory mapping"
      example:
        "01 - Core Concepts": "game-systems"
        "02 - World Lore": "world-lore"
        "05 - NPC AI Design": "npc-ai"
    defaultCategory:
      $ref: '#/components/schemas/DocumentCategory'
      default: other
    autoGenerateTags:
      type: boolean
      default: true
      description: Generate tags from folder structure
    tagPrefix:
      type: string
      default: ""
      description: Prefix to add to auto-generated tags

ArchiveConfiguration:
  type: object
  properties:
    archiveEnabled:
      type: boolean
      default: false
      description: Whether to create .bannou archives
    archiveOnSync:
      type: boolean
      default: false
      description: Create archive after every successful sync
    archiveScheduleEnabled:
      type: boolean
      default: false
      description: Enable scheduled archive creation
    archiveIntervalMinutes:
      type: integer
      default: 1440
      minimum: 60
      maximum: 43200
      description: Minutes between archive snapshots (1 hour to 30 days)
    retainVersions:
      type: integer
      default: 10
      minimum: 1
      maximum: 100
      description: Number of archive versions to retain
```

### 2.2 SyncResult Model

```yaml
SyncResult:
  type: object
  required:
    - syncId
    - status
    - startedAt
    - completedAt
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
    previousCommitHash:
      type: string
      nullable: true

    # Document counts
    documentsCreated:
      type: integer
      default: 0
    documentsUpdated:
      type: integer
      default: 0
    documentsDeleted:
      type: integer
      default: 0
    documentsSkipped:
      type: integer
      default: 0
    documentsFailed:
      type: integer
      default: 0

    # Details
    failedDocuments:
      type: array
      items:
        type: object
        properties:
          filePath:
            type: string
          error:
            type: string

    # Archive info (if created)
    archiveCreated:
      type: boolean
      default: false
    archiveBundleId:
      type: string
      nullable: true

    errorMessage:
      type: string
      nullable: true
```

### 2.3 DocumentMetadata Extensions

Extend the existing Document model to track repository source:

```yaml
# Additional metadata fields stored in Document.metadata
DocumentRepositoryMetadata:
  type: object
  properties:
    sourceBinding:
      type: string
      format: uuid
      description: BindingId that imported this document
    sourceFilePath:
      type: string
      description: Original file path in repository
    sourceCommitHash:
      type: string
      description: Commit hash when document was last synced
    frontmatterParsed:
      type: boolean
      description: Whether frontmatter was found and parsed
```

---

## 3. API Endpoints

### 3.1 Schema Definition (`documentation-api.yaml` additions)

All new endpoints follow Tenet 1 (POST-only) and Tenet 13 (x-permissions).

```yaml
# ═══════════════════════════════════════════════════════════════
# Repository Binding Endpoints
# ═══════════════════════════════════════════════════════════════

/documentation/repo/bind:
  post:
    operationId: bindRepository
    summary: Bind a git repository to a documentation namespace
    description: |
      Creates a binding between a git repository and a documentation namespace.

      **WARNING**: Binding a repository to an existing namespace will:
      - Delete ALL existing documents in the namespace
      - Replace with documents from the repository
      - Block manual document creation/updates while bound

      The namespace becomes read-only and exclusively managed by repository sync.
    tags:
      - Repository
    x-permissions:
      - role: developer
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/BindRepositoryRequest'
    responses:
      '200':
        description: Repository bound successfully
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/BindRepositoryResponse'
      '400':
        description: Invalid request (bad URL, unsupported protocol)
      '409':
        description: Namespace already has a binding

/documentation/repo/unbind:
  post:
    operationId: unbindRepository
    summary: Remove repository binding from namespace
    description: |
      Removes the repository binding from a namespace.

      **Note**: This does NOT delete any documents. The namespace becomes
      manually editable again with all existing documents preserved.
    tags:
      - Repository
    x-permissions:
      - role: admin
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/UnbindRepositoryRequest'
    responses:
      '200':
        description: Binding removed
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/UnbindRepositoryResponse'
      '404':
        description: No binding exists for namespace

/documentation/repo/sync:
  post:
    operationId: syncRepository
    summary: Manually trigger repository sync
    description: |
      Triggers an immediate sync from the bound repository.
      If a sync is already in progress, returns current sync status.
    tags:
      - Repository
    x-permissions:
      - role: developer
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/SyncRepositoryRequest'
    responses:
      '200':
        description: Sync triggered or status returned
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/SyncRepositoryResponse'
      '404':
        description: No binding exists for namespace
      '409':
        description: Sync already in progress

/documentation/repo/status:
  post:
    operationId: getRepositoryStatus
    summary: Get repository binding status
    description: |
      Returns detailed status of a repository binding including
      sync history, document counts, and configuration.
    tags:
      - Repository
    x-permissions:
      - role: developer
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/GetRepositoryStatusRequest'
    responses:
      '200':
        description: Binding status
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetRepositoryStatusResponse'
      '404':
        description: No binding exists for namespace

/documentation/repo/list:
  post:
    operationId: listRepositoryBindings
    summary: List all repository bindings
    description: |
      Returns a list of all repository bindings with basic status information.
    tags:
      - Repository
    x-permissions:
      - role: developer
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/ListRepositoryBindingsRequest'
    responses:
      '200':
        description: List of bindings
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListRepositoryBindingsResponse'

/documentation/repo/update:
  post:
    operationId: updateRepositoryBinding
    summary: Update repository binding configuration
    description: |
      Updates sync, content, or archive configuration for an existing binding.
      Cannot change namespace or repository URL (unbind and rebind instead).
    tags:
      - Repository
    x-permissions:
      - role: developer
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/UpdateRepositoryBindingRequest'
    responses:
      '200':
        description: Binding updated
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/UpdateRepositoryBindingResponse'
      '404':
        description: No binding exists for namespace

# ═══════════════════════════════════════════════════════════════
# Archive Management Endpoints
# ═══════════════════════════════════════════════════════════════

/documentation/repo/archive/create:
  post:
    operationId: createDocumentationArchive
    summary: Create a .bannou archive snapshot
    description: |
      Creates a .bannou bundle archive of the current namespace state.
      Requires Asset Service to be available.
    tags:
      - Repository
    x-permissions:
      - role: developer
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/CreateArchiveRequest'
    responses:
      '200':
        description: Archive creation started
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CreateArchiveResponse'
      '404':
        description: Namespace not found
      '503':
        description: Asset Service unavailable

/documentation/repo/archive/list:
  post:
    operationId: listDocumentationArchives
    summary: List available archives for a namespace
    description: |
      Lists all .bannou archives available for a namespace.
    tags:
      - Repository
    x-permissions:
      - role: developer
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/ListArchivesRequest'
    responses:
      '200':
        description: List of archives
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListArchivesResponse'

/documentation/repo/archive/restore:
  post:
    operationId: restoreDocumentationArchive
    summary: Restore namespace from archive
    description: |
      Restores a namespace to a previous state from a .bannou archive.

      **WARNING**: This will delete all current documents in the namespace
      and replace them with the archived content.

      If the namespace has a repository binding, the binding will be removed.
    tags:
      - Repository
    x-permissions:
      - role: admin
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/RestoreArchiveRequest'
    responses:
      '200':
        description: Restore completed
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/RestoreArchiveResponse'
      '404':
        description: Archive not found

/documentation/repo/archive/delete:
  post:
    operationId: deleteDocumentationArchive
    summary: Delete an archive
    description: |
      Permanently deletes a .bannou archive. This cannot be undone.
    tags:
      - Repository
    x-permissions:
      - role: admin
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/DeleteArchiveRequest'
    responses:
      '200':
        description: Archive deleted
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/DeleteArchiveResponse'
      '404':
        description: Archive not found
```

### 3.2 Request/Response Models

```yaml
# ═══════════════════════════════════════════════════════════════
# Request Models
# ═══════════════════════════════════════════════════════════════

BindRepositoryRequest:
  type: object
  required:
    - namespace
    - repositoryUrl
  properties:
    namespace:
      type: string
      pattern: '^[a-z0-9-]+$'
      maxLength: 50
    repositoryUrl:
      type: string
      description: Git clone URL (HTTPS or SSH)
    branch:
      type: string
      default: main
    syncConfig:
      $ref: '#/components/schemas/SyncConfiguration'
    contentConfig:
      $ref: '#/components/schemas/ContentConfiguration'
    archiveConfig:
      $ref: '#/components/schemas/ArchiveConfiguration'
    performInitialSync:
      type: boolean
      default: true
      description: Immediately sync after binding
    confirmDataLoss:
      type: boolean
      default: false
      description: |
        Must be true if namespace already has documents.
        Acknowledges that existing documents will be deleted.

UnbindRepositoryRequest:
  type: object
  required:
    - namespace
  properties:
    namespace:
      type: string
      pattern: '^[a-z0-9-]+$'
      maxLength: 50
    deleteDocuments:
      type: boolean
      default: false
      description: Also delete all documents (requires admin)

SyncRepositoryRequest:
  type: object
  required:
    - namespace
  properties:
    namespace:
      type: string
      pattern: '^[a-z0-9-]+$'
      maxLength: 50
    force:
      type: boolean
      default: false
      description: Force full resync even if no changes detected
    createArchive:
      type: boolean
      default: false
      description: Create archive after successful sync

GetRepositoryStatusRequest:
  type: object
  required:
    - namespace
  properties:
    namespace:
      type: string
      pattern: '^[a-z0-9-]+$'
      maxLength: 50
    includeSyncHistory:
      type: boolean
      default: false
    syncHistoryLimit:
      type: integer
      default: 10
      minimum: 1
      maximum: 100

ListRepositoryBindingsRequest:
  type: object
  properties:
    status:
      $ref: '#/components/schemas/BindingStatus'
      description: Filter by status
    page:
      type: integer
      default: 1
      minimum: 1
    pageSize:
      type: integer
      default: 20
      minimum: 1
      maximum: 100

UpdateRepositoryBindingRequest:
  type: object
  required:
    - namespace
  properties:
    namespace:
      type: string
      pattern: '^[a-z0-9-]+$'
      maxLength: 50
    branch:
      type: string
      description: Change tracked branch (triggers resync)
    syncConfig:
      $ref: '#/components/schemas/SyncConfiguration'
    contentConfig:
      $ref: '#/components/schemas/ContentConfiguration'
    archiveConfig:
      $ref: '#/components/schemas/ArchiveConfiguration'

CreateArchiveRequest:
  type: object
  required:
    - namespace
  properties:
    namespace:
      type: string
      pattern: '^[a-z0-9-]+$'
      maxLength: 50
    description:
      type: string
      maxLength: 500
      description: Optional description for this archive

ListArchivesRequest:
  type: object
  required:
    - namespace
  properties:
    namespace:
      type: string
      pattern: '^[a-z0-9-]+$'
      maxLength: 50
    page:
      type: integer
      default: 1
    pageSize:
      type: integer
      default: 20

RestoreArchiveRequest:
  type: object
  required:
    - namespace
    - archiveId
    - confirmDataLoss
  properties:
    namespace:
      type: string
      pattern: '^[a-z0-9-]+$'
      maxLength: 50
    archiveId:
      type: string
      format: uuid
    confirmDataLoss:
      type: boolean
      description: Must be true to confirm overwriting current content

DeleteArchiveRequest:
  type: object
  required:
    - archiveId
  properties:
    archiveId:
      type: string
      format: uuid

# ═══════════════════════════════════════════════════════════════
# Response Models
# ═══════════════════════════════════════════════════════════════

BindRepositoryResponse:
  type: object
  required:
    - bindingId
    - namespace
    - status
  properties:
    bindingId:
      type: string
      format: uuid
    namespace:
      type: string
    status:
      $ref: '#/components/schemas/BindingStatus'
    syncStarted:
      type: boolean
    message:
      type: string

UnbindRepositoryResponse:
  type: object
  required:
    - namespace
    - documentsPreserved
  properties:
    namespace:
      type: string
    documentsPreserved:
      type: integer
      description: Number of documents left in namespace
    documentsDeleted:
      type: integer
      description: Number of documents deleted (if deleteDocuments=true)

SyncRepositoryResponse:
  type: object
  required:
    - namespace
    - status
  properties:
    namespace:
      type: string
    status:
      type: string
      enum: [started, in_progress, completed]
    syncId:
      type: string
      format: uuid
    syncResult:
      $ref: '#/components/schemas/SyncResult'
      nullable: true
      description: Present if status is 'completed'

GetRepositoryStatusResponse:
  type: object
  required:
    - binding
  properties:
    binding:
      $ref: '#/components/schemas/RepositoryBinding'
    syncHistory:
      type: array
      items:
        $ref: '#/components/schemas/SyncResult'
    nextScheduledSync:
      type: string
      format: date-time
      nullable: true

ListRepositoryBindingsResponse:
  type: object
  required:
    - bindings
  properties:
    bindings:
      type: array
      items:
        $ref: '#/components/schemas/RepositoryBindingSummary'
    totalCount:
      type: integer
    page:
      type: integer
    pageSize:
      type: integer

RepositoryBindingSummary:
  type: object
  properties:
    bindingId:
      type: string
      format: uuid
    namespace:
      type: string
    repositoryUrl:
      type: string
    branch:
      type: string
    status:
      $ref: '#/components/schemas/BindingStatus'
    documentCount:
      type: integer
    lastSyncAt:
      type: string
      format: date-time
      nullable: true

UpdateRepositoryBindingResponse:
  type: object
  required:
    - bindingId
    - updated
  properties:
    bindingId:
      type: string
      format: uuid
    updated:
      type: boolean
    resyncTriggered:
      type: boolean
      description: True if branch change triggered resync

CreateArchiveResponse:
  type: object
  required:
    - archiveId
    - status
  properties:
    archiveId:
      type: string
      format: uuid
    status:
      type: string
      enum: [creating, completed]
    bundleId:
      type: string
      description: Asset Service bundle ID
    estimatedSizeBytes:
      type: integer
      format: int64

ListArchivesResponse:
  type: object
  required:
    - archives
  properties:
    archives:
      type: array
      items:
        $ref: '#/components/schemas/DocumentationArchive'
    totalCount:
      type: integer
    page:
      type: integer
    pageSize:
      type: integer

DocumentationArchive:
  type: object
  properties:
    archiveId:
      type: string
      format: uuid
    namespace:
      type: string
    bundleId:
      type: string
      description: Asset Service bundle ID
    createdAt:
      type: string
      format: date-time
    sourceCommitHash:
      type: string
      nullable: true
    documentCount:
      type: integer
    sizeBytes:
      type: integer
      format: int64
    description:
      type: string
      nullable: true

RestoreArchiveResponse:
  type: object
  required:
    - namespace
    - documentsRestored
  properties:
    namespace:
      type: string
    documentsRestored:
      type: integer
    previousDocumentsDeleted:
      type: integer
    bindingRemoved:
      type: boolean
      description: True if a repository binding was removed

DeleteArchiveResponse:
  type: object
  required:
    - archiveId
    - deleted
  properties:
    archiveId:
      type: string
      format: uuid
    deleted:
      type: boolean
```

---

## 4. Git Synchronization

### 4.1 LibGit2Sharp Integration

**Package**: `LibGit2Sharp` (MIT License ✅ per Tenet 18)

```csharp
// Services/GitSyncService.cs
public interface IGitSyncService
{
    /// <summary>
    /// Clone or update a repository to local storage.
    /// Returns the current commit hash after operation.
    /// </summary>
    Task<GitSyncResult> SyncRepositoryAsync(
        string repositoryUrl,
        string branch,
        string localPath,
        GitCredentials? credentials = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get list of changed files between two commits.
    /// </summary>
    Task<IList<GitFileChange>> GetChangedFilesAsync(
        string localPath,
        string? fromCommit,
        string toCommit,
        CancellationToken ct = default);

    /// <summary>
    /// Clean up local repository storage.
    /// </summary>
    Task CleanupRepositoryAsync(string localPath, CancellationToken ct = default);
}

public record GitSyncResult(
    string CommitHash,
    string CommitMessage,
    DateTimeOffset CommitDate,
    bool IsInitialClone,
    int FileCount);

public record GitFileChange(
    string FilePath,
    GitChangeType ChangeType);

public enum GitChangeType
{
    Added,
    Modified,
    Deleted,
    Renamed
}

public record GitCredentials(
    string? Username,
    string? Password,
    string? PrivateKeyPath);
```

### 4.2 Local Storage Strategy

**Location**: Configurable via `DOCUMENTATION_GIT_STORAGE_PATH`
**Default**: `/tmp/bannou-git-repos/` (ephemeral, reconstructible)

```
/tmp/bannou-git-repos/
├── {bindingId-1}/
│   ├── .git/
│   └── ... (repository files)
├── {bindingId-2}/
│   └── ...
└── locks/
    └── {bindingId}.lock  # Prevents concurrent sync
```

**Cleanup Policy**:
- Repositories cleaned up 24 hours after last sync (configurable)
- Cleanup triggered on service startup and periodically
- Binding removal triggers immediate cleanup

### 4.3 Sync Algorithm

```
SYNC ALGORITHM:
─────────────────────────────────────────────────────────────────

INPUT: RepositoryBinding binding
OUTPUT: SyncResult

1. ACQUIRE LOCK
   - Try acquire distributed lock: "repo-sync:{binding.namespace}"
   - If lock exists, return "sync already in progress"
   - Lock TTL: 30 minutes (configurable)

2. UPDATE STATUS
   - Set binding.status = "syncing"
   - Save binding to state store

3. CLONE OR PULL
   - If local repo exists:
       - git fetch origin
       - git reset --hard origin/{branch}
       - Record previousCommit = HEAD before reset
   - Else:
       - git clone --depth 1 --branch {branch} {repositoryUrl}
       - previousCommit = null (initial sync)
   - currentCommit = HEAD after operation

4. DETECT CHANGES
   - If previousCommit != null:
       - changedFiles = git diff --name-status previousCommit..currentCommit
   - Else:
       - changedFiles = all files (initial sync)

5. FILTER FILES
   - Apply filePatterns (include)
   - Apply excludePatterns (exclude)
   - Apply excludeBinaryFiles filter
   - Apply excludeCodeFiles filter
   - Result: List<FileToProcess>

6. TRANSFORM CONTENT
   For each file in FileToProcess:
   - Parse YAML frontmatter (if present)
   - Check for exclude frontmatter flag (private: true)
   - Generate slug from file path
   - Map category from folder or frontmatter
   - Extract/generate tags
   - Transform content (@ references, etc.)
   - Result: List<ImportDocument>

7. DETERMINE OPERATIONS
   - existingDocs = list documents in namespace
   - For each ImportDocument:
       - If slug exists in existingDocs: mark as UPDATE
       - Else: mark as CREATE
   - For each existingDoc not in ImportDocument list:
       - mark as DELETE (will be removed)

8. EXECUTE IMPORT
   - Call existing /documentation/import internally
   - conflictResolution: "update"
   - Track: created, updated, skipped, failed

9. DELETE ORPHANS
   - For each document marked DELETE:
       - Call internal delete (hard delete, not trashcan)

10. UPDATE BINDING
    - binding.lastSyncAt = now
    - binding.lastCommitHash = currentCommit
    - binding.lastSyncResult = result
    - binding.documentCount = count
    - binding.status = success ? "synced" : "error"
    - Save binding

11. CREATE ARCHIVE (if configured)
    - If archiveConfig.archiveOnSync:
        - Call createDocumentationArchive internally

12. RELEASE LOCK

13. PUBLISH EVENT
    - Publish "documentation.sync.completed" event

RETURN: SyncResult
```

### 4.4 File Filtering

**Binary File Extensions** (excluded when `excludeBinaryFiles: true`):
```
.png, .jpg, .jpeg, .gif, .bmp, .ico, .webp, .svg,
.pdf, .doc, .docx, .xls, .xlsx, .ppt, .pptx,
.zip, .tar, .gz, .rar, .7z,
.mp3, .mp4, .wav, .ogg, .avi, .mov, .mkv,
.exe, .dll, .so, .dylib,
.ttf, .otf, .woff, .woff2,
.db, .sqlite, .mdb
```

**Code File Extensions** (excluded when `excludeCodeFiles: true`):
```
.cs, .ts, .js, .tsx, .jsx, .py, .java, .cpp, .c, .h,
.go, .rs, .rb, .php, .swift, .kt, .scala,
.css, .scss, .sass, .less,
.html, .htm, .xml, .xsl,
.json, .yaml, .yml (except *.md with frontmatter),
.sh, .bash, .ps1, .bat, .cmd,
.sql, .graphql,
.csproj, .sln, .package.json, .tsconfig.json
```

---

## 5. Content Transformation

### 5.1 YAML Frontmatter Parsing

**Supported Frontmatter Fields**:

```yaml
---
title: Document Title           # Overrides filename-based title
category: game-systems          # Must match DocumentCategory enum
tags: [tag1, tag2, tag3]        # Additional tags
summary: Brief summary text     # For search results
voiceSummary: Voice-friendly    # For voice AI (max 200 chars)
private: true                   # Exclude from import
slug: custom-slug               # Override auto-generated slug
relatedDocuments:               # UUIDs of related docs (optional)
  - uuid-1
  - uuid-2
---
```

**Parsing Rules**:
1. Frontmatter MUST be at the very start of the file
2. Delimited by `---` on its own line
3. Content after closing `---` is the document body
4. Unknown frontmatter fields are preserved in `metadata`
5. If `private: true`, document is skipped entirely

**Implementation Note**: This frontmatter parsing should also apply to normal `/documentation/import` operations, not just repository bindings. When importing documents:
1. Check if content starts with `---`
2. If so, parse frontmatter and extract supported fields
3. Frontmatter values override request body values (if both provided)
4. Strip frontmatter from stored content

### 5.2 Slug Generation

**Algorithm**:
```
INPUT: File path relative to repository root
       Example: "01 - Core Concepts/Guardian Spirit System.md"

1. Remove file extension
   "01 - Core Concepts/Guardian Spirit System"

2. Replace path separators with hyphens
   "01 - Core Concepts-Guardian Spirit System"

3. Convert to lowercase
   "01 - core concepts-guardian spirit system"

4. Replace spaces and special chars with hyphens
   "01-core-concepts-guardian-spirit-system"

5. Collapse multiple hyphens
   "01-core-concepts-guardian-spirit-system"

6. Remove leading/trailing hyphens
   "01-core-concepts-guardian-spirit-system"

OUTPUT: "01-core-concepts-guardian-spirit-system"
```

**Frontmatter Override**: If `slug` is specified in frontmatter, use that instead.

### 5.3 Category Mapping

**Priority Order**:
1. Frontmatter `category` field (if present and valid)
2. `categoryMapping` configuration (directory prefix match)
3. `defaultCategory` (fallback)

**Category Mapping Example**:
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
```

**Matching Algorithm**:
- Sort mappings by key length (longest first)
- Match directory prefix (case-insensitive)
- First match wins

### 5.4 Tag Generation

**When `autoGenerateTags: true`**:

1. Extract folder names from path (excluding numbered prefixes)
2. Apply `tagPrefix` if configured
3. Combine with frontmatter tags (frontmatter takes precedence for duplicates)

**Example**:
```
File: "08 - Crafting Systems/01 - Fundamental Processes/Charcoal Production.md"
tagPrefix: "arcadia-kb"

Generated tags:
- arcadia-kb:crafting-systems
- arcadia-kb:fundamental-processes
```

### 5.5 Reference Transformation

**@ Reference Handling**:
Transform Obsidian/knowledge-base style references to internal links.

```markdown
# Before (source file)
See @~/repos/arcadia-kb/01 - Core Concepts/Guardian Spirit System.md for details.

# After (imported)
See [Guardian Spirit System](/documentation/view/01-core-concepts-guardian-spirit-system?ns=arcadia-kb) for details.
```

**Transformation Rules**:
1. Detect `@` references followed by file paths
2. Check if referenced file is being imported
3. If yes, replace with markdown link using generated slug
4. If no (external reference), leave as-is or mark as broken link

---

## 6. Asset Integration

### 6.1 Documentation Archive Bundle Format

Archives use the Asset Service's `.bannou` bundle format with documentation-specific manifest:

```json
{
  "bundleId": "docs-arcadia-kb-2025-12-23-abc123",
  "version": "1.0.0",
  "type": "documentation-archive",
  "created": "2025-12-23T10:00:00Z",
  "compression": "lz4",

  "documentation": {
    "namespace": "arcadia-kb",
    "sourceBinding": "uuid-of-binding",
    "sourceCommitHash": "abc123def456",
    "sourceCommitDate": "2025-12-23T09:00:00Z",
    "sourceRepositoryUrl": "https://github.com/user/arcadia-kb.git",
    "sourceBranch": "main",
    "documentCount": 127,
    "totalContentSizeBytes": 1500000,
    "exportedAt": "2025-12-23T10:00:00Z"
  },

  "assets": [
    {
      "assetId": "doc-01-core-concepts-guardian-spirit-system",
      "path": "documents/01-core-concepts-guardian-spirit-system.json",
      "offset": 0,
      "size": 45678,
      "hash": "sha256:...",
      "type": "documentation",
      "metadata": {
        "slug": "01-core-concepts-guardian-spirit-system",
        "title": "Guardian Spirit System",
        "category": "game-systems"
      }
    }
  ]
}
```

**Document Storage Format** (within bundle):
```json
{
  "documentId": "uuid",
  "slug": "01-core-concepts-guardian-spirit-system",
  "title": "Guardian Spirit System",
  "category": "game-systems",
  "content": "# Guardian Spirit System\n\n...",
  "summary": "Overview of guardian spirits...",
  "voiceSummary": "Guardian spirits let players possess characters",
  "tags": ["core", "guardian-spirits"],
  "relatedDocuments": ["uuid-1", "uuid-2"],
  "metadata": {
    "sourceBinding": "uuid",
    "sourceFilePath": "01 - Core Concepts/Guardian Spirit System.md",
    "sourceCommitHash": "abc123"
  },
  "createdAt": "2025-12-20T10:00:00Z",
  "updatedAt": "2025-12-23T09:00:00Z"
}
```

### 6.2 Archive Creation Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         ARCHIVE CREATION FLOW                                │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  1. Documentation Service receives createArchive request                     │
│                                                                              │
│  2. Load all documents from namespace                                        │
│     - Query state store for all doc IDs                                      │
│     - Load full document data for each                                       │
│                                                                              │
│  3. Build archive manifest                                                   │
│     - Generate unique archive ID                                             │
│     - Include binding metadata (if bound to repo)                            │
│     - List all documents with metadata                                       │
│                                                                              │
│  4. Serialize documents to JSON                                              │
│     - One JSON file per document                                             │
│     - Include all fields needed for restore                                  │
│                                                                              │
│  5. Create bundle via Asset Service                                          │
│     - POST /bundles/create                                                   │
│     - Request .bannou format with LZ4 compression                            │
│     - Upload document JSON files                                             │
│                                                                              │
│  6. Store archive reference                                                  │
│     - Key: archive:{namespace}:{archiveId}                                   │
│     - Value: DocumentationArchive record                                     │
│     - Link to Asset Service bundleId                                         │
│                                                                              │
│  7. Return archive metadata                                                  │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 6.3 Archive Restore Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         ARCHIVE RESTORE FLOW                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  1. Documentation Service receives restoreArchive request                    │
│     - Validate confirmDataLoss = true                                        │
│                                                                              │
│  2. Load archive metadata                                                    │
│     - Verify archive exists                                                  │
│     - Get Asset Service bundleId                                             │
│                                                                              │
│  3. Download bundle from Asset Service                                       │
│     - POST /bundles/get to get download URL                                  │
│     - Download .bannou file                                                  │
│     - Extract manifest and document files                                    │
│                                                                              │
│  4. Remove existing binding (if present)                                     │
│     - Unbind repository                                                      │
│     - Namespace becomes manually editable                                    │
│                                                                              │
│  5. Delete all existing documents                                            │
│     - Hard delete (not trashcan)                                             │
│     - Clear search index for namespace                                       │
│                                                                              │
│  6. Import archived documents                                                │
│     - Parse each document JSON                                               │
│     - Preserve original documentId (deterministic restore)                   │
│     - Restore all metadata                                                   │
│                                                                              │
│  7. Rebuild search index                                                     │
│                                                                              │
│  8. Return restore result                                                    │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 7. Scheduled Sync System

### 7.1 Dapr Cron Binding

```yaml
# provisioning/dapr/components/documentation-sync-schedule.yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: documentation-sync-schedule
spec:
  type: bindings.cron
  version: v1
  metadata:
  - name: schedule
    value: "@every 5m"  # Check every 5 minutes
```

### 7.2 Sync Scheduler Service

```csharp
// Services/SyncSchedulerService.cs
public interface ISyncSchedulerService
{
    /// <summary>
    /// Called by Dapr cron binding to check and execute due syncs.
    /// </summary>
    Task ProcessScheduledSyncsAsync(CancellationToken ct = default);

    /// <summary>
    /// Calculate next sync time for a binding.
    /// </summary>
    DateTimeOffset? CalculateNextSyncTime(RepositoryBinding binding);
}
```

**Scheduler Algorithm**:
```
SCHEDULER TICK (every 5 minutes):
─────────────────────────────────────────────────────────────────

1. Load all bindings with syncConfig.syncEnabled = true

2. For each binding:
   a. Calculate nextSyncTime = lastSyncAt + syncIntervalMinutes
   b. If now >= nextSyncTime AND status != "syncing":
      - Add to sync queue

3. Process sync queue (sequential to avoid resource contention):
   For each binding in queue:
   - Call syncRepository internally
   - Log result
   - Continue to next (don't fail entire batch on one error)

4. Archive scheduler (separate pass):
   For each binding with archiveConfig.archiveScheduleEnabled:
   a. Calculate nextArchiveTime = lastArchiveAt + archiveIntervalMinutes
   b. If now >= nextArchiveTime:
      - Call createArchive internally
```

### 7.3 Event Handler Registration

```yaml
# documentation-events.yaml additions
x-event-subscriptions:
  - topic: documentation-sync-schedule
    event: CronTriggerEvent
    handler: HandleSyncScheduleTrigger
```

---

## 8. Configuration

### 8.1 Environment Variables

```yaml
# documentation-configuration.yaml additions
x-service-configuration:
  properties:
    # Existing properties...

    # Git Storage
    GitStoragePath:
      type: string
      default: "/tmp/bannou-git-repos"
      env: DOCUMENTATION_GIT_STORAGE_PATH
      description: "Local path for cloned repositories"

    GitStorageCleanupHours:
      type: integer
      default: 24
      env: DOCUMENTATION_GIT_STORAGE_CLEANUP_HOURS
      description: "Hours after last sync to cleanup local repo"

    GitCloneTimeoutSeconds:
      type: integer
      default: 300
      env: DOCUMENTATION_GIT_CLONE_TIMEOUT_SECONDS
      description: "Timeout for git clone operations"

    GitPullTimeoutSeconds:
      type: integer
      default: 120
      env: DOCUMENTATION_GIT_PULL_TIMEOUT_SECONDS
      description: "Timeout for git pull operations"

    # Sync Settings
    SyncSchedulerEnabled:
      type: boolean
      default: true
      env: DOCUMENTATION_SYNC_SCHEDULER_ENABLED
      description: "Enable scheduled sync processing"

    MaxConcurrentSyncs:
      type: integer
      default: 3
      env: DOCUMENTATION_MAX_CONCURRENT_SYNCS
      description: "Maximum concurrent repository syncs"

    SyncLockTimeoutMinutes:
      type: integer
      default: 30
      env: DOCUMENTATION_SYNC_LOCK_TIMEOUT_MINUTES
      description: "Distributed lock TTL for sync operations"

    # Archive Settings
    ArchiveSchedulerEnabled:
      type: boolean
      default: true
      env: DOCUMENTATION_ARCHIVE_SCHEDULER_ENABLED
      description: "Enable scheduled archive creation"

    DefaultArchiveIntervalMinutes:
      type: integer
      default: 1440
      env: DOCUMENTATION_DEFAULT_ARCHIVE_INTERVAL_MINUTES
      description: "Default archive interval (24 hours)"

    # Content Limits
    MaxDocumentSizeBytes:
      type: integer
      default: 524288
      env: DOCUMENTATION_MAX_DOCUMENT_SIZE_BYTES
      description: "Maximum size per document (500KB)"

    MaxDocumentsPerSync:
      type: integer
      default: 1000
      env: DOCUMENTATION_MAX_DOCUMENTS_PER_SYNC
      description: "Maximum documents to process in one sync"
```

### 8.2 Default Exclude Patterns

```yaml
# Hardcoded defaults (always applied)
defaultExcludePatterns:
  - ".git/**"
  - ".gitignore"
  - ".gitattributes"

# Configurable defaults (can be overridden per binding)
suggestedExcludePatterns:
  - ".obsidian/**"
  - ".vscode/**"
  - ".idea/**"
  - "node_modules/**"
  - "__pycache__/**"
  - "*.pyc"
  - ".DS_Store"
  - "Thumbs.db"
  - "Templates/**"
```

---

## 9. Implementation Roadmap

### Phase 1: Core Binding Infrastructure (Week 1)

**Files to Create**:
- `schemas/documentation-api.yaml` - Add repository binding endpoints
- `lib-documentation/Services/IGitSyncService.cs` - Git sync interface
- `lib-documentation/Services/GitSyncService.cs` - LibGit2Sharp implementation
- `lib-documentation/Services/IContentTransformService.cs` - Content transformation interface
- `lib-documentation/Services/ContentTransformService.cs` - Frontmatter parsing, slug generation

**Tasks**:
1. Add `LibGit2Sharp` NuGet package to `lib-documentation.csproj`
2. Define all schema models and endpoints
3. Run `make generate` to generate controllers/models
4. Implement `GitSyncService` with clone/pull operations
5. Implement basic `ContentTransformService` (frontmatter, slugs, categories)
6. Unit tests for slug generation, category mapping, frontmatter parsing

**Acceptance Criteria**:
- [ ] `dotnet build` succeeds with new schemas
- [ ] GitSyncService can clone and pull repositories
- [ ] ContentTransformService parses frontmatter correctly
- [ ] Unit tests pass for all transformation logic

### Phase 2: Binding Management (Week 2)

**Tasks**:
1. Implement `bindRepository` endpoint
   - Validate repository URL (HTTPS/SSH format)
   - Create binding record in state store
   - Trigger initial sync if requested
2. Implement `unbindRepository` endpoint
   - Remove binding record
   - Clean up local git storage
   - Preserve documents (unless deleteDocuments=true)
3. Implement `getRepositoryStatus` endpoint
4. Implement `listRepositoryBindings` endpoint
5. Implement `updateRepositoryBinding` endpoint
6. Add binding check to create/update/delete document endpoints
   - Return 403 Forbidden if namespace is bound

**Acceptance Criteria**:
- [ ] Can bind/unbind repositories via API
- [ ] Manual document edits blocked when bound
- [ ] Status and list endpoints return correct data
- [ ] Unit tests for all binding operations

### Phase 3: Sync Engine (Week 3)

**Tasks**:
1. Implement full sync algorithm
   - File filtering (patterns, binary, code exclusions)
   - Change detection (git diff for incremental)
   - Content transformation pipeline
   - Import via existing API
   - Orphan deletion
2. Implement `syncRepository` endpoint
3. Add distributed locking for sync operations
4. Implement scheduled sync via Dapr cron binding
5. Add sync history tracking
6. Integration tests with test repository

**Acceptance Criteria**:
- [ ] Manual sync imports all documents correctly
- [ ] Incremental sync only updates changed files
- [ ] Deleted files are removed from namespace
- [ ] Scheduled sync runs at configured intervals
- [ ] Concurrent sync attempts are blocked

### Phase 4: Asset Integration (Week 4)

**Dependencies**: Asset Service must be available

**Tasks**:
1. Implement `createDocumentationArchive` endpoint
   - Build manifest with all documents
   - Create bundle via Asset Service
   - Store archive reference
2. Implement `listDocumentationArchives` endpoint
3. Implement `restoreDocumentationArchive` endpoint
   - Download and extract bundle
   - Remove binding (if present)
   - Delete existing docs
   - Import archived docs
4. Implement `deleteDocumentationArchive` endpoint
5. Add archive-on-sync option to sync flow
6. Add scheduled archive creation
7. Integration tests with Asset Service

**Acceptance Criteria**:
- [ ] Archives created successfully as .bannou bundles
- [ ] Archives listed with correct metadata
- [ ] Restore replaces namespace content correctly
- [ ] Archive-on-sync creates archive after successful sync
- [ ] Scheduled archives created at configured intervals

---

## 10. Open Considerations

### 10.1 Authentication for Private Repositories

**Current Design**: Public repositories only (no auth)

**Future Enhancement**:
- Store encrypted credentials per binding
- Support GitHub/GitLab personal access tokens
- Support SSH key authentication
- Credential storage in secrets manager (not state store)

### 10.2 Large Repository Handling

**Current Limits**:
- `MaxDocumentsPerSync`: 1000 documents
- `MaxDocumentSizeBytes`: 500KB per document
- Shallow clone (`--depth 1`) to minimize storage

**Future Enhancement**:
- Sparse checkout for selective file fetch
- Streaming processing for very large repos
- Background processing pool for heavy syncs

### 10.3 Webhook Support

**Not Implemented** (complexity vs benefit):
- Would require NGINX routing exception
- GitHub/GitLab webhook signature validation
- Rate limiting and replay protection

**Alternative**: Short sync interval (5-15 min) provides near-real-time updates without webhook complexity.

### 10.4 Multi-Repository Namespaces

**Not Implemented** (out of scope):
- Merging multiple repos into one namespace
- Would require conflict resolution between repos
- Complex source tracking

**Alternative**: Use separate namespaces per repo, link via relatedDocuments.

---

## Appendix A: arcadia-kb Category Mapping

Pre-configured mapping for the Arcadia knowledge base repository:

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

## Appendix B: Events Published

```yaml
# documentation-events.yaml additions

DocumentationSyncStartedEvent:
  type: object
  required: [eventId, timestamp, namespace, bindingId]
  properties:
    eventId:
      type: string
      format: uuid
    timestamp:
      type: string
      format: date-time
    namespace:
      type: string
    bindingId:
      type: string
      format: uuid
    syncId:
      type: string
      format: uuid
    triggeredBy:
      type: string
      enum: [manual, scheduled]

DocumentationSyncCompletedEvent:
  type: object
  required: [eventId, timestamp, namespace, bindingId, syncResult]
  properties:
    eventId:
      type: string
      format: uuid
    timestamp:
      type: string
      format: date-time
    namespace:
      type: string
    bindingId:
      type: string
      format: uuid
    syncId:
      type: string
      format: uuid
    syncResult:
      $ref: '#/components/schemas/SyncResult'

DocumentationArchiveCreatedEvent:
  type: object
  required: [eventId, timestamp, namespace, archiveId]
  properties:
    eventId:
      type: string
      format: uuid
    timestamp:
      type: string
      format: date-time
    namespace:
      type: string
    archiveId:
      type: string
      format: uuid
    bundleId:
      type: string
    documentCount:
      type: integer
    sizeBytes:
      type: integer
      format: int64
```

---

*This document is the authoritative source for Documentation Repository Binding implementation. Updates require review and approval.*
