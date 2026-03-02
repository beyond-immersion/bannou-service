# Documentation Plugin Deep Dive

> **Plugin**: lib-documentation
> **Schema**: schemas/documentation-api.yaml
> **Version**: 1.0.0
> **Layer**: AppFeatures
> **State Store**: documentation-statestore (Redis)

---

## Overview

Knowledge base API (L3 AppFeatures) designed for AI agents (SignalWire SWAIG, OpenAI function calling, Claude tool use) with full-text search, natural language query, and voice-friendly summaries. Manages documentation within namespaces, supporting manual CRUD and automated git repository synchronization (git-bound namespaces reject mutations, enforcing git as single source of truth). Features browser-facing GET endpoints that render markdown to HTML (unusual exception to Bannou's POST-only pattern). Three background services handle index rebuilding, periodic repository sync, and trashcan purge.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis persistence for documents, slug indexes, namespace lists, trashcan, bindings, archives |
| lib-state (`IDistributedLockProvider`) | Distributed locks for repository sync operations (30-minute TTL) |
| lib-messaging (`IMessageBus`) | Publishing lifecycle events, analytics events, binding/sync events, error events |
| lib-asset (`IAssetClient` via `IServiceProvider`) | Archive bundle upload (RequestBundleUpload) and download (GetBundle) for archive create/restore. L3→L3 soft dependency with graceful degradation (runtime-resolved, not constructor-injected). |
| `IHttpClientFactory` | HTTP PUT/GET to Asset Service pre-signed URLs for bundle data transfer |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| (none identified) | No other plugins reference `IDocumentationClient` or depend on Documentation events |

---

## State Storage

**Stores**: 1 state store (single Redis store with key prefix `doc`, Redis Search enabled)

| Store | Backend | Search | Purpose |
|-------|---------|--------|---------|
| `documentation-statestore` | Redis | FT.* | All document data, indexes, bindings, trashcan, archives |

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{namespaceId}:{documentId}` | `StoredDocument` | Document content and metadata (note: DOC_KEY_PREFIX is empty; store adds `doc:` prefix) |
| `slug-idx:{namespaceId}:{slug}` | `string` (GUID) | Slug-to-document-ID lookup index |
| `ns-docs:{namespaceId}` | `HashSet<Guid>` | All document IDs in a namespace (for pagination and rebuild). Uses HashSet for uniqueness guarantees. |
| `ns-trash:{namespaceId}` | `List<Guid>` | Trashcan document ID list per namespace |
| `trash:{namespaceId}:{documentId}` | `TrashedDocument` | Soft-deleted document with TTL metadata |
| `repo-binding:{namespaceId}` | `RepositoryBinding` | Repository binding configuration for a namespace |
| `repo-bindings` | `HashSet<string>` | Global registry of all bound namespace IDs |
| `all-namespaces` | `HashSet<string>` | Global registry of all namespaces (for search rebuild) |
| `archive:{archiveId}` | `DocumentationArchive` | Archive metadata record |
| `archive:list:{namespaceId}` | `List<Guid>` | Archive IDs for a namespace (ARCHIVE_KEY_PREFIX + "list:" + namespace) |
| `ns-last-updated:{namespaceId}` | `string` (ISO 8601) | Most recent document mutation timestamp per namespace (for O(1) stats reads) |
| `repo-sync:{namespaceId}` | Distributed Lock | Prevents concurrent sync operations on same namespace |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `document.created` | `DocumentCreatedEvent` | Document created (manual, import, or sync) |
| `document.updated` | `DocumentUpdatedEvent` | Document fields updated (includes `changedFields` list) |
| `document.deleted` | `DocumentDeletedEvent` | Document soft-deleted to trashcan (includes `deletedReason`) |
| `documentation.queried` | `DocumentationQueriedEvent` | Natural language query executed (analytics, fire-and-forget) |
| `documentation.searched` | `DocumentationSearchedEvent` | Keyword search executed (analytics, fire-and-forget) |
| `documentation-binding.created` | `DocumentationBindingCreatedEvent` | Repository binding created |
| `documentation-binding.removed` | `DocumentationBindingRemovedEvent` | Repository binding removed (includes `documentsDeleted` count) |
| `documentation-sync.started` | `DocumentationSyncStartedEvent` | Repository sync begins (includes `triggeredBy`: manual/scheduled) |
| `documentation-sync.completed` | `DocumentationSyncCompletedEvent` | Repository sync ends (includes status, counts, duration) |
| `documentation-archive.created` | `DocumentationArchiveCreatedEvent` | Archive created (includes `bundleAssetId`, `documentCount`) |

### Consumed Events

This plugin does not consume external events. Per schema: `x-event-subscriptions: []`. The `RegisterEventConsumers` method is a no-op (minimal mode). Sessions use TTL-based cleanup rather than event-driven invalidation.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `SearchIndexRebuildStartupDelaySeconds` | `DOCUMENTATION_SEARCH_INDEX_REBUILD_STARTUP_DELAY_SECONDS` | `5` | Delay before index rebuild starts (allows infra to init) |
| `SearchIndexRebuildOnStartup` | `DOCUMENTATION_SEARCH_INDEX_REBUILD_ON_STARTUP` | `true` | Whether to rebuild search index on startup |
| `MaxContentSizeBytes` | `DOCUMENTATION_MAX_CONTENT_SIZE_BYTES` | `524288` | Maximum document content size (500KB) |
| `TrashcanTtlDays` | `DOCUMENTATION_TRASHCAN_TTL_DAYS` | `7` | Days before trashcan items auto-expire |
| `TrashcanPurgeEnabled` | `DOCUMENTATION_TRASHCAN_PURGE_ENABLED` | `true` | Enable background trashcan purge service |
| `TrashcanPurgeCheckIntervalMinutes` | `DOCUMENTATION_TRASHCAN_PURGE_CHECK_INTERVAL_MINUTES` | `60` | How often to check for expired trashcan entries |
| `VoiceSummaryMaxLength` | `DOCUMENTATION_VOICE_SUMMARY_MAX_LENGTH` | `200` | Maximum characters for voice summaries |
| `SearchCacheTtlSeconds` | `DOCUMENTATION_SEARCH_CACHE_TTL_SECONDS` | `300` | TTL for in-memory search result cache |
| `MinRelevanceScore` | `DOCUMENTATION_MIN_RELEVANCE_SCORE` | `0.3` | Default minimum relevance for query results |
| `MaxSearchResults` | `DOCUMENTATION_MAX_SEARCH_RESULTS` | `20` | Maximum search/query results returned |
| `MaxImportDocuments` | `DOCUMENTATION_MAX_IMPORT_DOCUMENTS` | `0` | Max documents per import (0 = unlimited) |
| `GitStoragePath` | `DOCUMENTATION_GIT_STORAGE_PATH` | `/tmp/bannou-git-repos` | Local path for cloned repositories |
| `GitStorageCleanupHours` | `DOCUMENTATION_GIT_STORAGE_CLEANUP_HOURS` | `24` | Hours before orphaned repos are cleaned up |
| `GitCloneTimeoutSeconds` | `DOCUMENTATION_GIT_CLONE_TIMEOUT_SECONDS` | `300` | Git clone/pull timeout |
| `SyncSchedulerEnabled` | `DOCUMENTATION_SYNC_SCHEDULER_ENABLED` | `true` | Enable background sync scheduler |
| `SyncSchedulerCheckIntervalMinutes` | `DOCUMENTATION_SYNC_SCHEDULER_CHECK_INTERVAL_MINUTES` | `5` | How often scheduler checks for due syncs |
| `MaxSyncsPerCycle` | `DOCUMENTATION_MAX_SYNCS_PER_CYCLE` | `3` | Max sync operations per scheduler cycle (processed sequentially) |
| `MaxDocumentsPerSync` | `DOCUMENTATION_MAX_DOCUMENTS_PER_SYNC` | `1000` | Max documents processed per sync |
| `RepositorySyncCheckIntervalSeconds` | `DOCUMENTATION_REPOSITORY_SYNC_CHECK_INTERVAL_SECONDS` | `30` | Initial delay before first scheduler check |
| `BulkOperationBatchSize` | `DOCUMENTATION_BULK_OPERATION_BATCH_SIZE` | `10` | Documents per batch before yielding in bulk ops |
| `MaxRelatedDocuments` | `DOCUMENTATION_MAX_RELATED_DOCUMENTS` | `5` | Maximum related documents to return for standard depth |
| `MaxRelatedDocumentsExtended` | `DOCUMENTATION_MAX_RELATED_DOCUMENTS_EXTENDED` | `10` | Maximum related documents to return for extended depth |
| `SyncLockTtlSeconds` | `DOCUMENTATION_SYNC_LOCK_TTL_SECONDS` | `1800` | TTL in seconds for repository sync distributed lock (30 min) |
| `MaxFetchLimit` | `DOCUMENTATION_MAX_FETCH_LIMIT` | `1000` | Maximum documents to fetch when filtering/sorting in memory |
| `EstimatedBytesPerDocument` | `DOCUMENTATION_ESTIMATED_BYTES_PER_DOCUMENT` | `10000` | Estimated average document content size for stats calculations |
| `SearchSnippetLength` | `DOCUMENTATION_SEARCH_SNIPPET_LENGTH` | `200` | Length in characters for search result snippets |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<DocumentationService>` | Scoped | Structured logging |
| `DocumentationServiceConfiguration` | Singleton | All 27 configuration properties (26 service-specific + ForceServiceId) |
| `IStateStoreFactory` | Singleton | Redis state store access for all data |
| `IDistributedLockProvider` | Singleton | Sync operation locking |
| `IMessageBus` | Scoped | Event publishing (lifecycle, analytics, errors) |
| `ISearchIndexService` | Singleton | Full-text search (Redis Search or in-memory fallback) |
| `IGitSyncService` | Singleton | Git clone/pull/file-list/read/cleanup operations |
| `IContentTransformService` | Singleton | YAML frontmatter parsing, slug generation, markdown processing |
| `IServiceProvider` | Scoped | Runtime resolution for L3 soft dependencies (IAssetClient) |
| `IHttpClientFactory` | Singleton | HTTP client for pre-signed URL transfers |
| `ITelemetryProvider` | Singleton | Activity/span creation for telemetry instrumentation |
| `IEventConsumer` | Scoped | Event consumer registration (minimal, no-op) |
| `SearchIndexRebuildService` | Hosted (BackgroundService) | One-shot index rebuild on startup |
| `RepositorySyncSchedulerService` | Hosted (BackgroundService) | Periodic sync scheduling and stale repo cleanup |
| `TrashcanPurgeService` | Hosted (BackgroundService) | Periodic purge of expired trashcan entries |

Service lifetime is **Scoped** (per-request). Three hosted background services run as singletons.

---

## API Endpoints (Implementation Notes)

### Search (3 endpoints)

- **QueryDocumentation** (`POST /documentation/query`): Natural language query. Validates namespace and query string. Checks static `SearchResultCache` (ConcurrentDictionary with TTL from `SearchCacheTtlSeconds`). On miss, calls `ISearchIndexService.QueryAsync()` with `MinRelevanceScore` filter. Fetches full `StoredDocument` for each result to build response with content, summary, voiceSummary, tags, and relatedDocuments. Caches successful results. Publishes `DocumentationQueriedEvent` for analytics (fire-and-forget, non-critical). Access: anonymous.

- **SearchDocumentation** (`POST /documentation/search`): Keyword-based full-text search. Validates namespace and searchTerm. Calls `ISearchIndexService.SearchAsync()` with optional category filter. For each result, generates a context snippet around the match location via `GenerateSearchSnippet()`. Publishes `DocumentationSearchedEvent` for analytics. Access: anonymous.

- **SuggestRelatedTopics** (`POST /documentation/suggest`): Related topic suggestions. Supports multiple source types via `SuggestionSource` enum: `Document_id`, `Slug`, `Topic`, `Category`. For document/slug sources, resolves the document then finds related via shared tags/category scoring. For topic/category, delegates to `ISearchIndexService.GetRelatedSuggestionsAsync()`. Returns summaries with relevance reasons. Access: anonymous.

### Documents (2 endpoints)

- **GetDocument** (`POST /documentation/get`): Retrieves document by ID or slug. Validates that either `documentId` or `slug` is provided. Slug resolution via `slug-idx:{ns}:{slug}` key. Supports `relatedDepth` parameter (none/basic/extended) controlling how many related document summaries are fetched. Returns full document with content, voiceSummary, tags, metadata, and optional related summaries. Access: anonymous.

- **ListDocuments** (`POST /documentation/list`): Lists documents by category within a namespace. Delegates to `ISearchIndexService.ListDocumentIdsAsync()` for pagination (skip/take). Fetches each document from state store for title/summary/tags. Supports category filter. Access: anonymous.

### Admin (10 endpoints)

- **CreateDocument** (`POST /documentation/create`): Creates new document. Checks namespace binding (rejects if bound and not disabled). Validates content size against `MaxContentSizeBytes`. Validates slug uniqueness via slug index. Truncates voiceSummary to `VoiceSummaryMaxLength`. Calls `EnsureIndexExistsAsync()` BEFORE saving (required for Redis Search auto-indexing). Saves document, slug index, namespace index. Indexes for search. Publishes `DocumentCreatedEvent`. Access: admin.

- **UpdateDocument** (`POST /documentation/update`): Partial update of document fields. Checks namespace binding (403 if bound). Only updates fields that are provided and changed (tracks `changedFields` list). Validates new slug uniqueness on slug change. Updates slug index on change. Re-indexes for search. Publishes `DocumentUpdatedEvent` with changed fields. Access: admin.

- **DeleteDocument** (`POST /documentation/delete`): Soft-delete to trashcan. Checks namespace binding (403 if bound). Resolves by ID or slug. Creates `TrashedDocument` with `ExpiresAt` computed from `TrashcanTtlDays`. Moves to trashcan storage, removes from main storage/indexes/search. Publishes `DocumentDeletedEvent`. Access: admin.

- **RecoverDocument** (`POST /documentation/recover`): Restores from trashcan. Checks expiry (returns 404 if expired, cleans up). Checks slug availability (409 Conflict if slug taken). Calls `EnsureIndexExistsAsync()` before restore. Restores to main storage, slug index, namespace index, search index. Removes from trashcan. Access: admin.

- **BulkUpdateDocuments** (`POST /documentation/bulk-update`): Batch category/tag updates. Iterates document IDs, applies category change and addTags/removeTags operations. Uses `BulkOperationBatchSize` with `Task.Yield()` to avoid thread starvation. Returns succeeded/failed lists with per-document error details. Access: admin.

- **BulkDeleteDocuments** (`POST /documentation/bulk-delete`): Batch soft-delete. Same trashcan pattern as single delete. Yields per batch. Returns succeeded/failed lists. Access: admin.

- **ImportDocumentation** (`POST /documentation/import`): Bulk document import. Validates against `MaxImportDocuments` limit. Supports `onConflict` policy: `skip` (ignore existing), `fail` (error per duplicate), `update` (overwrite). Creates new documents or updates existing per policy. Access: admin.

- **ListTrashcan** (`POST /documentation/trashcan`): Lists trashcan contents. Lazily cleans expired items during listing (removes expired from store and index). Paginates results sorted by `DeletedAt` descending. Access: admin.

- **PurgeTrashcan** (`POST /documentation/purge`): Permanently deletes trashcan items. Supports targeted purge (specific documentIds) or full purge (all items). Uses optimistic concurrency (ETag) on trashcan index to handle concurrent modifications. Returns 409 Conflict on concurrent modification. Access: admin.

- **GetNamespaceStats** (`POST /documentation/stats`): Returns namespace statistics. Gets document count and category breakdown from search index. Gets trashcan count from index. Estimates content size using `EstimatedBytesPerDocument` config (default 10KB). Reads `lastUpdated` from dedicated `ns-last-updated:{namespaceId}` key maintained by all document mutation paths. Access: admin.

### Browser (2 endpoints)

- **ViewDocumentBySlug** (`GET /documentation/view/{slug}`): Manual controller implementation (non-generated). Returns fully rendered HTML page with Markdig-processed markdown. Uses inline CSS styling. Supports `?ns=` query parameter (defaults to "bannou"). Validates content integrity (publishes error event if null/empty content found). This is a FOUNDATION TENETS exception (T15: Browser-Facing Endpoints). Access: authenticated.

- **RawDocumentBySlug** (`GET /documentation/raw/{slug}`): Manual controller implementation. Returns raw markdown content with `text/markdown; charset=utf-8` content type. Delegates to `GetDocumentAsync()` internally. Access: authenticated.

### Repository (6 endpoints)

- **BindRepository** (`POST /documentation/repo/bind`): Binds a git repository to a namespace. Validates no existing binding (409 if already bound). Creates `RepositoryBinding` with file patterns, exclude patterns, category mapping, sync interval, archive settings. Saves binding and updates bindings registry. Publishes `DocumentationBindingCreatedEvent`. Access: developer.

- **UnbindRepository** (`POST /documentation/repo/unbind`): Removes repository binding. Optionally deletes all documents in namespace (`deleteDocuments` flag). Removes binding from state and registry. Cleans up local repository directory. Publishes `DocumentationBindingRemovedEvent`. Access: admin.

- **SyncRepository** (`POST /documentation/repo/sync`): Manually triggers repository synchronization. Calls `ExecuteSyncAsync()` which acquires distributed lock (30-minute TTL), clones/pulls via IGitSyncService, processes matching files via IContentTransformService, creates/updates/deletes documents. Skips orphan deletion if file list was truncated by `MaxDocumentsPerSync`. Publishes sync started/completed events. Access: developer.

- **GetRepositoryStatus** (`POST /documentation/repo/status`): Returns binding info and last sync details. Maps internal status to API status enum. Access: developer.

- **ListRepositoryBindings** (`POST /documentation/repo/list`): Lists all bindings from registry. Supports status filter and offset/limit pagination. Access: developer.

- **UpdateRepositoryBinding** (`POST /documentation/repo/update`): Updates binding configuration (syncEnabled, syncInterval, filePatterns, excludePatterns, categoryMapping, defaultCategory, archiveEnabled, archiveOnSync). Saves updated binding. Access: developer.

### Archive (4 endpoints)

- **CreateDocumentationArchive** (`POST /documentation/repo/archive/create`): Creates archive of all namespace documents. Serializes documents to JSON bundle. Uploads to Asset Service via pre-signed URL (graceful failure if Asset Service unavailable - archive stored without bundle). Saves archive metadata. Publishes `DocumentationArchiveCreatedEvent`. Access: developer.

- **ListDocumentationArchives** (`POST /documentation/repo/archive/list`): Lists archives for a namespace. Paginated with offset/limit, sorted by createdAt descending. Access: developer.

- **RestoreDocumentationArchive** (`POST /documentation/repo/archive/restore`): Restores documents from an archive. Checks namespace not bound to repository (403 if bound). Downloads bundle from Asset Service. Restores documents from bundle data. Access: admin.

- **DeleteDocumentationArchive** (`POST /documentation/repo/archive/delete`): Deletes archive metadata record. Does NOT delete the bundle from Asset Service (relies on Asset Service retention policies). Access: admin.

---

## Visual Aid

```
Search Index Rebuild (Startup)
================================

  SearchIndexRebuildService (BackgroundService, runs once)
       |
       |-- Wait: SearchIndexRebuildStartupDelaySeconds (5s default)
       |
       |-- Discover namespaces:
       |    |-- Read "all-namespaces" (HashSet<string>)
       |    |-- Read "repo-bindings" (HashSet<string>)
       |    |-- Union both sets
       |
       |-- For each namespace:
       |    |-- ISearchIndexService.RebuildIndexAsync(namespaceId)
       |    |    |-- Read "ns-docs:{ns}" for document ID list
       |    |    |-- For each docId: read document from store
       |    |    |-- Build inverted index (terms -> docIds)
       |    |    |-- Build category index
       |    |    |-- Track tag counts
       |    |    |-- Return indexed count
       |    |
       |    |-- Log: "{count} documents indexed"
       |
       |-- Log: "rebuild complete: {total} documents across {n} namespaces"
       |-- Exit (one-shot)


Trashcan Lifecycle
====================

  CreateDocument ─────────────────────────────────> Active Document
       |                                                   |
       |                                         DeleteDocument
       |                                                   |
       |                                                   v
       |                                     TrashedDocument {
       |                                       Document: StoredDocument
       |                                       DeletedAt: now
       |                                       ExpiresAt: now + TrashcanTtlDays
       |                                     }
       |                                                   |
       |                                    ┌──────────────┼──────────────┐
       |                                    |              |              |
       |                              RecoverDocument   PurgeTrashcan   Expiry
       |                                    |              |              |
       |                                    v              v              v
       |                              Restore to       Permanent      Lazy cleanup
       |                              Active           Delete         during List
       |                              (slug check)     (immediate)    (on access)


Repository Binding & Sync
============================

  BindRepository(namespace, repoUrl, branch, patterns...)
       |
       |-- Create RepositoryBinding (status: Pending)
       |-- Save to "repo-binding:{namespace}"
       |-- Add namespace to "repo-bindings" registry
       |
       |              RepositorySyncSchedulerService (periodic)
       |                         |
       |          ┌──────────────┼──────────────┐
       |          |                             |
       |    Check bindings:               CleanupStale:
       |    - Read "repo-bindings"        - Scan GitStoragePath
       |    - For each: check NextSyncAt  - Find GUID dirs not in registry
       |    - If due: trigger sync        - Delete if older than CleanupHours
       |    - Respect MaxSyncsPerCycle
       |          |
       |          v
  SyncRepository / ExecuteSyncAsync(binding, force, trigger)
       |
       |-- Acquire distributed lock: "repo-sync:{namespace}" (30 min TTL)
       |    |-- Fail? Return "Sync already in progress"
       |
       |-- Set status = Syncing
       |-- Publish DocumentationSyncStartedEvent
       |
       |-- IGitSyncService.SyncRepositoryAsync(url, branch, localPath)
       |    |-- Clone (if not exists) or Pull (if exists)
       |    |-- Return commitHash, success flag
       |
       |-- If !force && commitHash unchanged → skip (no-op)
       |
       |-- GetMatchingFilesAsync(localPath, filePatterns, excludePatterns)
       |    |-- Apply MaxDocumentsPerSync limit
       |
       |-- For each file:
       |    |-- ReadFileContentAsync → raw content
       |    |-- IContentTransformService.TransformFile()
       |    |    |-- ParseFrontmatter (YAML: title, category, tags, slug, draft...)
       |    |    |-- ExtractContent (strip frontmatter block)
       |    |    |-- GenerateSlug (path-based if no frontmatter override)
       |    |    |-- DetermineCategory (frontmatter > path mapping > dir inference > default)
       |    |    |-- GenerateVoiceSummary (first paragraph, strip markdown, truncate)
       |    |
       |    |-- Skip if draft
       |    |-- Create or update document in state store
       |    |-- Track processed slugs
       |
       |-- Delete orphan documents (slugs not in processed set)
       |    |-- SKIPPED if file list was truncated
       |
       |-- Update binding: status=Synced, LastSyncAt, LastCommitHash, NextSyncAt
       |-- Publish DocumentationSyncCompletedEvent
       |-- Release lock (via await using)


Namespace Organization
========================

  ┌─────────────────────────────────────────────────────────────┐
  |                      Redis (doc: prefix)                     |
  |                                                              |
  |  all-namespaces: {"bannou", "arcadia-docs", "api-ref"}      |
  |  repo-bindings:  {"arcadia-docs", "api-ref"}                |
  |                                                              |
  |  ┌─── Namespace: "bannou" (manual) ──────────────────────┐  |
  |  |  ns-docs:bannou → [guid1, guid2, guid3]               |  |
  |  |  bannou:guid1 → StoredDocument{...}                    |  |
  |  |  slug-idx:bannou:getting-started → "guid1"             |  |
  |  |  ns-trash:bannou → [guid4]                             |  |
  |  |  trash:bannou:guid4 → TrashedDocument{...}             |  |
  |  └────────────────────────────────────────────────────────┘  |
  |                                                              |
  |  ┌─── Namespace: "arcadia-docs" (repo-bound) ────────────┐  |
  |  |  repo-binding:arcadia-docs → RepositoryBinding{...}    |  |
  |  |  ns-docs:arcadia-docs → [guid5, guid6, ...]           |  |
  |  |  arcadia-docs:guid5 → StoredDocument{...}              |  |
  |  |  slug-idx:arcadia-docs:guides/npc → "guid5"           |  |
  |  |  archive:list:arcadia-docs → [archId1]                 |  |
  |  |  archive:archId1 → DocumentationArchive{...}           |  |
  |  └────────────────────────────────────────────────────────┘  |
  └─────────────────────────────────────────────────────────────┘


Archive System
================

  CreateDocumentationArchive(namespace, owner, description)
       |
       |-- GetAllNamespaceDocumentsAsync() → List<StoredDocument>
       |    (404 if no documents)
       |
       |-- CreateArchiveBundleAsync() → byte[] (GZipped JSON)
       |    |-- Serialize all documents to JSON
       |    |-- Compress with GZip
       |
       |-- Upload to Asset Service:
       |    |-- RequestBundleUploadAsync() → uploadUrl, uploadId
       |    |-- PUT bundleData to uploadUrl
       |    |-- Store BundleAssetId on success
       |    |-- Graceful failure: archive stored without bundle upload
       |
       |-- Save DocumentationArchive to state store
       |-- Publish DocumentationArchiveCreatedEvent
       |
  RestoreDocumentationArchive(archiveId)
       |
       |-- Get archive metadata
       |-- Verify namespace not bound (403 if bound)
       |-- Download bundle from Asset Service (GetBundle → downloadUrl)
       |-- GET bundleData from downloadUrl
       |-- RestoreFromBundleAsync() → decompress, deserialize, create docs
```

---

## Stubs & Unimplemented Features

1. **Voice summary generation**: `GenerateVoiceSummary()` strips markdown and truncates the first paragraph. No actual NLG, TTS-optimization, or prosody considerations are applied - it is a simple text extraction.
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/520 -->

2. ~~**Archive bundle upload reliability**~~: **FIXED** (2026-03-01) - `CreateArchiveResponse.bundleAssetId` was a non-nullable `Guid` (T26 sentinel value violation — defaulted to `Guid.Empty` when upload failed) and was never set in the response even on success. Fixed: schema updated to `nullable: true` (matching `ArchiveInfo`), and the response now sets `BundleAssetId = archive.BundleAssetId` so callers can detect whether the bundle was uploaded. Restore still returns 404 for archives without bundles — this is correct graceful degradation behavior.

---

## Potential Extensions

1. **Semantic search with embeddings**: Implement vector embeddings for document content. Store embeddings in Redis Vector Similarity Search (VSS) and use cosine similarity for natural language queries in `QueryAsync`. Blocker: requires choosing an embedding provider and adding the HTTP client dependency — no external AI service integration exists in Bannou yet. Configuration properties (`AiEnhancementsEnabled`, `AiEmbeddingsModel`) were previously defined but removed as a T21 violation (never wired); they would need to be re-added to the schema when implementation begins. Note: this is a **retrieval** optimization (matching queries to existing documents), not content generation — it does not conflict with the formal-theory-over-AI principle (see [WHY-DOESNT-BANNOU-USE-AI-FOR-CONTENT-GENERATION.md](../../faqs/WHY-DOESNT-BANNOU-USE-AI-FOR-CONTENT-GENERATION.md)).
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/525 -->

2. **Webhook-triggered sync**: Add webhook endpoint for git push notifications (GitHub/GitLab webhooks) to trigger immediate sync instead of waiting for the scheduler interval.
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/528 -->

3. **Document versioning**: Track content version history within each document, enabling diff views and rollback to specific versions without full archive restore.
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/530 -->

4. **Cross-namespace search**: Support querying across multiple namespaces in a single request, with namespace-scoped result grouping.
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/531 -->

5. ~~**Trashcan auto-purge background service**~~: **FIXED** (2026-03-01) - Added `TrashcanPurgeService` background service that periodically iterates all namespaces and purges expired trashcan entries. Configurable via `TrashcanPurgeEnabled` (default true) and `TrashcanPurgeCheckIntervalMinutes` (default 60). Uses optimistic concurrency on trashcan index with graceful retry on conflict. Lazy cleanup in `ListTrashcan` still works as a secondary path.

6. ~~**Incremental sync optimization**~~: **FIXED** (2026-03-01) - Added SHA256 content hashing to `StoredDocument` via a `ContentHash` field. During sync, `UpdateDocumentFromTransformAsync` compares the hash plus title, category, summary, voice summary, and tags before writing — unchanged documents skip the Redis write and search re-index entirely. Documents created before this change have null hash and are always updated on first sync (natural migration). The sync completion log now reports unchanged document count.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

(none currently)

### Intentional Quirks

1. **Orphan deletion skipped on truncated sync**: When `MaxDocumentsPerSync` limits the processed file list, orphan deletion (removing documents whose slugs were not seen) is intentionally skipped to avoid incorrectly deleting unprocessed documents.

2. **Archive deletion does not remove Asset Service bundle**: `DeleteDocumentationArchive` only removes the archive metadata. The actual bundle data in the Asset Service is not deleted, relying on Asset Service retention policies for cleanup.

3. **Trashcan expiry has two paths**: The `TrashcanPurgeService` background service periodically purges expired entries (default every 60 minutes). Additionally, lazy cleanup during `ListTrashcan` and `RecoverDocument` catches any entries that expire between purge cycles.

4. **Single Redis store for all data**: All document data, indexes, trashcan, bindings, and archives share one `documentation-statestore` with key-prefix partitioning (e.g., `slug-idx:`, `ns-docs:`, `trash:`, `repo-binding:`, `archive:`). This is the standard Bannou pattern — most services use a single state store per `schemas/state-stores.yaml`. Redis handles millions of keys efficiently via its hash-based key space. No TTL is set on document keys because documents are persistent content intended to survive until explicitly deleted (the trashcan handles TTL-based soft-delete expiry).

5. **Per-instance git repository clones**: `GitSyncService` clones repositories to `GitStoragePath` (default `/tmp/bannou-git-repos`) on each container's local filesystem. In multi-instance deployments, each instance maintains its own clone. This is intentional for container-local storage — the distributed lock (`repo-sync:{namespace}` via `IDistributedLockProvider`) ensures only one instance syncs a given namespace at a time, preventing race conditions in shared state (Redis). Local clones are ephemeral workspace for reading file content during sync; once documents are written to Redis, the clone is just a pull cache. `CleanupStaleRepositoriesAsync` in `RepositorySyncSchedulerService` handles orphaned directories based on `GitStorageCleanupHours` (default 24h). Operators should ensure `DOCUMENTATION_GIT_STORAGE_PATH` has adequate disk capacity and tune `DOCUMENTATION_GIT_STORAGE_CLEANUP_HOURS` for their pod lifecycle.

6. **Anonymous read access on search/query endpoints**: The search, query, get, list, and suggest endpoints are all explicitly declared `x-permissions: [{ role: anonymous, states: {} }]` in the schema. This is intentional — the Documentation service is designed for AI agent consumption (SignalWire SWAIG, OpenAI function calling, Claude tool use), where requiring authentication on every query would be impractical. Write endpoints (create, update, delete) correctly require `admin` role. Any client can query any namespace without authentication by design.

7. **Slug index is eventually consistent**: `CreateDocumentAsync` performs three sequential non-atomic Redis writes: (1) save document, (2) save slug index, (3) add to namespace document list. If a crash occurs mid-sequence, the document may exist without its slug index or namespace index entry. The search index rebuild (`SearchIndexRebuildService`) reads from the `ns-docs:{ns}` namespace list, so it cannot recover documents missing from that list. Self-healing: **repo-bound namespaces** naturally repair on next sync (sync re-creates documents from git); **manual namespaces** have no automatic repair — an admin would need to re-create the document. The failure window is microseconds (sequential Redis writes), making this extremely unlikely in practice. The sequential write pattern is consistent with other Bannou services.

8. **Import onConflict=update overwrites all fields**: When importing with `Update` conflict policy, all document fields are overwritten including tags and metadata — no merge-with-existing-tags behavior. This is intentional: the `Update` policy means "the import source is authoritative, replace the existing document." If merge behavior were desired, it would require a new `Merge` conflict policy (a potential extension). The three existing policies (`Skip`, `Fail`, `Update`) cover the standard import conflict resolution patterns.

9. **Sequential binding processing in sync scheduler**: `RepositorySyncSchedulerService.ProcessScheduledSyncsAsync()` iterates bindings sequentially — each `SyncRepositoryAsync()` call must complete before the next starts. This is intentional: each sync involves heavy I/O (git clone/pull to local disk, file parsing, Redis writes for document CRUD + search indexing), and concurrent syncs would multiply disk/network/memory pressure without proportional benefit. `MaxSyncsPerCycle` (default 3) rate-limits how many syncs occur per cycle, and any bindings that don't get processed are picked up in the next cycle. The distributed lock (`repo-sync:{namespace}`) prevents concurrent syncs of the same namespace across instances.

10. **TotalContentSizeBytes is an estimate**: `GetNamespaceStats` calculates content size as `documents * EstimatedBytesPerDocument` (configurable via `DOCUMENTATION_ESTIMATED_BYTES_PER_DOCUMENT`, defaults to 10KB). This is a deliberate tradeoff: accurate sizing would require either iterating all documents per stats call (N+1 Redis queries, expensive for large namespaces) or maintaining a running counter across every CRUD path (8+ methods, risk of drift from crashes/concurrent writes). The estimate is sufficient for admin diagnostics — operators can tune `EstimatedBytesPerDocument` to match their actual average document size. The response field is named `TotalContentSizeBytes`, not `EstimatedContentSizeBytes`, because the schema treats it as a stats metric (all stats are point-in-time approximations).

### Design Considerations

1. ~~**Import onConflict=update overwrites all fields**~~: **FIXED** (2026-03-01) - Moved to Intentional Quirks. Full overwrite is correct behavior for the `Update` conflict policy — the import source is authoritative. A merge policy would be a separate feature extension.

2. ~~**RepositorySyncSchedulerService processes bindings sequentially**~~: **FIXED** (2026-03-01) - Moved to Intentional Quirks. Sequential processing is a deliberate design choice: each sync involves heavy I/O (git clone/pull + file parsing + Redis writes), and concurrent syncs would multiply resource pressure without proportional benefit. `MaxSyncsPerCycle` rate-limits throughput per cycle, and the distributed lock prevents concurrent syncs of the same namespace.

3. ~~**MaxConcurrentSyncs naming is misleading**~~: **FIXED** (2026-03-01) - Renamed `MaxConcurrentSyncs` to `MaxSyncsPerCycle` (env var `DOCUMENTATION_MAX_SYNCS_PER_CYCLE`) to accurately reflect that syncs are processed sequentially as a per-cycle rate limit, not concurrently. Updated schema description, service code, and tests.

4. ~~**TotalContentSizeBytes is always an estimate**~~: **FIXED** (2026-03-01) - Moved to Intentional Quirks. The estimate via `EstimatedBytesPerDocument` is a deliberate configurable tradeoff: accurate sizing requires N+1 queries or a running counter across 8+ CRUD paths, both adding complexity for marginal benefit on an admin diagnostics endpoint.

5. ~~**LastUpdated sampling is incomplete**~~: **FIXED** (2026-03-01) - Replaced sampling-based `lastUpdated` with a dedicated `ns-last-updated:{namespaceId}` Redis key maintained by all document mutation paths (create, update, recover, bulk update, import, sync, bundle restore). `GetNamespaceStatsAsync` now reads a single key for exact results instead of sampling N documents from an unordered `HashSet<Guid>`. Removed dead `StatsSampleSize` config property (T21 compliance).

6. ~~**Search index retains stale terms on document update**~~: **FIXED** (2026-03-01) - Replaced `ConcurrentBag<Guid>` with `ConcurrentDictionary<Guid, byte>` as a concurrent hash set with removal support. `AddDocument` now calls `RemoveDocument` first on updates to clean up stale inverted index and category index entries. `RemoveDocument` now properly removes document IDs from all term and category index entries.

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow.

### Completed

- **Archive bundle upload reliability** (2026-03-01): Fixed T26 sentinel value violation in `CreateArchiveResponse.bundleAssetId` (non-nullable Guid → nullable) and populated field in response.
- **Trashcan auto-purge background service** (2026-03-01): Added `TrashcanPurgeService` that periodically purges expired trashcan entries. Config: `TrashcanPurgeEnabled`, `TrashcanPurgeCheckIntervalMinutes`. Uses optimistic concurrency on trashcan indexes.
- **Incremental sync optimization** (2026-03-01): Added SHA256 `ContentHash` field to `StoredDocument`. `UpdateDocumentFromTransformAsync` now compares hash + metadata before writing, skipping unchanged documents. Natural migration for pre-existing documents (null hash = always update on first sync).
- **Single Redis store for all data** (2026-03-01): Moved from Design Considerations to Intentional Quirks. Single store with key-prefix partitioning is the standard Bannou pattern. No TTL on documents is correct — they are persistent content. Redis handles the key-space efficiently.
- **Per-instance git repository clones** (2026-03-01): Moved from Design Considerations to Intentional Quirks. Per-instance disk usage is inherent to container-local storage. Distributed lock prevents concurrent syncs of shared state. Stale cleanup handles orphaned directories. Added operational guidance for disk capacity and cleanup tuning.
- **No authentication on search/query endpoints** (2026-03-01): Moved from Design Considerations to Intentional Quirks. All five read endpoints explicitly declare `x-permissions: [{ role: anonymous }]` in the schema — intentional for AI agent consumption. Write endpoints correctly require admin role.
- **MaxConcurrentSyncs naming is misleading** (2026-03-01): Renamed config property from `MaxConcurrentSyncs` to `MaxSyncsPerCycle` (env var `DOCUMENTATION_MAX_SYNCS_PER_CYCLE`). Updated schema description, `RepositorySyncSchedulerService`, and tests to use accurate naming.
- **Slug index is eventually consistent** (2026-03-01): Moved from Design Considerations to Intentional Quirks. Corrected inaccurate claim that search index rebuild would find orphaned documents — rebuild reads from `ns-docs:{ns}` namespace list, not by scanning content. Added accurate self-healing description: repo-bound namespaces repair on next sync; manual namespaces have no automatic repair. Sequential Redis write pattern is consistent with other Bannou services.
- **Import onConflict=update overwrites all fields** (2026-03-01): Moved from Design Considerations to Intentional Quirks. Full overwrite is correct `Update` policy behavior — the import source is authoritative. Merge behavior would require a new conflict policy (potential extension).
- **Sequential binding processing in sync scheduler** (2026-03-01): Moved from Design Considerations to Intentional Quirks. Sequential processing is deliberate: each sync involves heavy I/O (git clone/pull + file parsing + Redis writes), concurrent syncs would multiply resource pressure. `MaxSyncsPerCycle` rate-limits throughput.
- **TotalContentSizeBytes is always an estimate** (2026-03-01): Moved from Design Considerations to Intentional Quirks. The `EstimatedBytesPerDocument` config property is a deliberate tunable for admin diagnostics. Accurate sizing would require N+1 queries or a running counter across 8+ CRUD paths — complexity without proportional benefit.
- **LastUpdated sampling is incomplete** (2026-03-01): Replaced sampling-based `lastUpdated` with dedicated `ns-last-updated:{namespaceId}` Redis key maintained by all mutation paths. Removed dead `StatsSampleSize` config property.
- **Search index retains stale terms on document update** (2026-03-01): Replaced `ConcurrentBag<Guid>` with `ConcurrentDictionary<Guid, byte>` as concurrent hash set with removal support. `AddDocument` now calls `RemoveDocument` first on updates. `RemoveDocument` now properly cleans up inverted index and category index entries. Removed Intentional Quirk #2 (ConcurrentBag stale references). Added 2 regression tests.
