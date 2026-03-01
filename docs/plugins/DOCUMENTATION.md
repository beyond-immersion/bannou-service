# Documentation Plugin Deep Dive

> **Plugin**: lib-documentation
> **Schema**: schemas/documentation-api.yaml
> **Version**: 1.0.0
> **Layer**: AppFeatures
> **State Stores**: documentation-statestore (Redis)

---

## Overview

Knowledge base API (L3 AppFeatures) designed for AI agents (SignalWire SWAIG, OpenAI function calling, Claude tool use) with full-text search, natural language query, and voice-friendly summaries. Manages documentation within namespaces, supporting manual CRUD and automated git repository synchronization (git-bound namespaces reject mutations, enforcing git as single source of truth). Features browser-facing GET endpoints that render markdown to HTML (unusual exception to Bannou's POST-only pattern). Two background services handle index rebuilding and periodic repository sync.

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
| `MaxConcurrentSyncs` | `DOCUMENTATION_MAX_CONCURRENT_SYNCS` | `3` | Max sync operations per scheduler cycle (sequential despite name — see Design #9) |
| `MaxDocumentsPerSync` | `DOCUMENTATION_MAX_DOCUMENTS_PER_SYNC` | `1000` | Max documents processed per sync |
| `RepositorySyncCheckIntervalSeconds` | `DOCUMENTATION_REPOSITORY_SYNC_CHECK_INTERVAL_SECONDS` | `30` | Initial delay before first scheduler check |
| `BulkOperationBatchSize` | `DOCUMENTATION_BULK_OPERATION_BATCH_SIZE` | `10` | Documents per batch before yielding in bulk ops |
| `MaxRelatedDocuments` | `DOCUMENTATION_MAX_RELATED_DOCUMENTS` | `5` | Maximum related documents to return for standard depth |
| `MaxRelatedDocumentsExtended` | `DOCUMENTATION_MAX_RELATED_DOCUMENTS_EXTENDED` | `10` | Maximum related documents to return for extended depth |
| `SyncLockTtlSeconds` | `DOCUMENTATION_SYNC_LOCK_TTL_SECONDS` | `1800` | TTL in seconds for repository sync distributed lock (30 min) |
| `MaxFetchLimit` | `DOCUMENTATION_MAX_FETCH_LIMIT` | `1000` | Maximum documents to fetch when filtering/sorting in memory |
| `StatsSampleSize` | `DOCUMENTATION_STATS_SAMPLE_SIZE` | `10` | Number of documents to sample for namespace statistics |
| `EstimatedBytesPerDocument` | `DOCUMENTATION_ESTIMATED_BYTES_PER_DOCUMENT` | `10000` | Estimated average document content size for stats calculations |
| `SearchSnippetLength` | `DOCUMENTATION_SEARCH_SNIPPET_LENGTH` | `200` | Length in characters for search result snippets |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<DocumentationService>` | Scoped | Structured logging |
| `DocumentationServiceConfiguration` | Singleton | All 26 configuration properties (25 service-specific + ForceServiceId) |
| `IStateStoreFactory` | Singleton | Redis state store access for all data |
| `IDistributedLockProvider` | Singleton | Sync operation locking |
| `IMessageBus` | Scoped | Event publishing (lifecycle, analytics, errors) |
| `ISearchIndexService` | Singleton | Full-text search (Redis Search or in-memory fallback) |
| `IGitSyncService` | Singleton | Git clone/pull/file-list/read/cleanup operations |
| `IContentTransformService` | Singleton | YAML frontmatter parsing, slug generation, markdown processing |
| `IServiceProvider` | Scoped | Runtime resolution for L3 soft dependencies (IAssetClient) |
| `IHttpClientFactory` | Singleton | HTTP client for pre-signed URL transfers |
| `IEventConsumer` | Scoped | Event consumer registration (minimal, no-op) |
| `SearchIndexRebuildService` | Hosted (BackgroundService) | One-shot index rebuild on startup |
| `RepositorySyncSchedulerService` | Hosted (BackgroundService) | Periodic sync scheduling and stale repo cleanup |

Service lifetime is **Scoped** (per-request). Two hosted background services run as singletons.

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

- **GetNamespaceStats** (`POST /documentation/stats`): Returns namespace statistics. Gets document count and category breakdown from search index. Gets trashcan count from index. Estimates content size using `EstimatedBytesPerDocument` config (default 10KB). Samples `StatsSampleSize` recent documents to determine lastUpdated. Access: admin.

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
       |    - Respect MaxConcurrentSyncs
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
  |  |  ns-archives:arcadia-docs → [archId1]                  |  |
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

1. ~~**AI-powered semantic search stub config**~~: **FIXED** (2026-01-31) - Removed dead configuration properties `AiEnhancementsEnabled` and `AiEmbeddingsModel` that were never referenced in service code (T21 violation). The query/search implementations both use inverted-index keyword matching. The `QueryAsync` method is identical to `SearchAsync` with an added relevance score filter. This is the correct current behavior; semantic search remains a potential future extension (see Potential Extensions below).

2. ~~**RedisSearchIndexService configuration**~~: **FIXED** (2026-02-01) - Added `enableSearch: true` to `documentation-statestore` in `state-stores.yaml`. The `RedisSearchIndexService` was fully implemented but never used because the store lacked the search flag. With this configuration, Redis Search (FT.*) will be used when available; the in-memory `SearchIndexService` fallback remains for environments without RediSearch module.

3. **Voice summary generation**: `GenerateVoiceSummary()` strips markdown and truncates the first paragraph. No actual NLG, TTS-optimization, or prosody considerations are applied - it is a simple text extraction.

4. **Archive bundle upload reliability**: If the Asset Service is unavailable during archive creation, the archive metadata is saved without a `BundleAssetId`. This means `RestoreDocumentationArchive` will return 404 for archives that were created without successful uploads.

5. ~~**Per-sync tracking**~~: **FIXED** (2026-03-01) - `GetRepositoryStatus` now returns `SyncId = null` (nullable Guid) instead of `Guid.Empty` sentinel. `SyncResult.SyncId` is `Guid?` with null for conflict results, proper GUID for completed syncs.

---

## Potential Extensions

1. **Semantic search with embeddings**: Implement vector embeddings for document content. Store embeddings in Redis Vector Similarity Search (VSS) and use cosine similarity for natural language queries in `QueryAsync`. Blocker: requires choosing an embedding provider and adding the HTTP client dependency — no external AI service integration exists in Bannou yet. Configuration properties (`AiEnhancementsEnabled`, `AiEmbeddingsModel`) were previously defined but removed as a T21 violation (never wired); they would need to be re-added to the schema when implementation begins. Note: this is a **retrieval** optimization (matching queries to existing documents), not content generation — it does not conflict with the formal-theory-over-AI principle (see [WHY-DOESNT-BANNOU-USE-AI-FOR-CONTENT-GENERATION.md](../../faqs/WHY-DOESNT-BANNOU-USE-AI-FOR-CONTENT-GENERATION.md)).

2. **Webhook-triggered sync**: Add webhook endpoint for git push notifications (GitHub/GitLab webhooks) to trigger immediate sync instead of waiting for the scheduler interval.

3. **Document versioning**: Track content version history within each document, enabling diff views and rollback to specific versions without full archive restore.

4. **Cross-namespace search**: Support querying across multiple namespaces in a single request, with namespace-scoped result grouping.

5. **Trashcan auto-purge background service**: Currently expired items are only cleaned lazily during `ListTrashcan`. A background service could periodically purge expired trashcan entries without requiring user access.

6. **Incremental sync optimization**: Currently sync processes all matching files on each run. A file-hash index could skip unchanged files, reducing processing for large repositories with few changes.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. ~~**Type mismatch for `ns-docs:{namespaceId}` key**~~: **FIXED** (2026-01-31) - Standardized on `HashSet<Guid>` for namespace document lists in both `DocumentationService.GetNamespaceStatsAsync` and `SearchIndexService.RebuildIndexAsync`. Trashcan (`ns-trash:`) intentionally uses `List<Guid>` for different semantics (order preservation).

### Intentional Quirks

1. **Orphan deletion skipped on truncated sync**: When `MaxDocumentsPerSync` limits the processed file list, orphan deletion (removing documents whose slugs were not seen) is intentionally skipped to avoid incorrectly deleting unprocessed documents.

2. **ConcurrentBag stale references on removal**: The in-memory `SearchIndexService` uses `ConcurrentBag` for inverted index entries. Since ConcurrentBag does not support element removal, deleted documents leave stale references that are filtered out during search. Full cleanup requires index rebuild.

3. **Archive deletion does not remove Asset Service bundle**: `DeleteDocumentationArchive` only removes the archive metadata. The actual bundle data in the Asset Service is not deleted, relying on Asset Service retention policies for cleanup.

4. **Trashcan expiry is lazy, not proactive**: Expired trashcan items are only detected and cleaned during `ListTrashcan` calls or `RecoverDocument` attempts. Between accesses, expired items remain in Redis.

### Design Considerations

1. ~~**T16 (ViewDocumentBySlugAsync return type)**~~: **FIXED** (2026-01-31) - Changed return type from `(StatusCodes, object?)` to `(StatusCodes, string?)` for proper type safety. The method always returns HTML string content, so the typed return accurately reflects this. Removed unnecessary cast in controller.

2. **Single Redis store for all data**: All document data, indexes, trashcan, bindings, and archives share one `documentation-statestore`. A very active namespace with many documents could create key-space pressure. No TTL is set on document keys themselves.

3. ~~**N+1 query pattern in ListDocuments and ListTrashcan**~~: **FIXED** (2026-01-31) - Both methods now use `GetBulkAsync` for single-call bulk retrieval. `ListTrashcanAsync` also uses `DeleteBulkAsync` to batch-delete expired items instead of individual deletes.

4. **Git operations on local filesystem**: `GitSyncService` clones repositories to `GitStoragePath` on the container's filesystem. In multi-instance deployments, each instance clones independently. The distributed lock prevents concurrent syncs of the same namespace, but disk usage is per-instance.

5. **No authentication on search/query endpoints**: The search, query, get, list, and suggest endpoints are all marked `anonymous` access. Any client can query any namespace without authentication.

6. **Slug index is eventually consistent**: If a crash occurs between saving a document and saving its slug index entry, the document exists but is unreachable by slug. The search index (rebuilt on startup) would still find it by content.

7. **Import onConflict=update overwrites all fields**: When importing with update policy, all document fields are overwritten including tags and metadata. There is no merge-with-existing-tags behavior - the import fully replaces.

8. **RepositorySyncSchedulerService processes bindings sequentially**: Bindings are checked one at a time within each cycle. A slow sync operation blocks subsequent bindings until it completes or the scheduler moves to the next cycle after the interval.

9. **MaxConcurrentSyncs naming is misleading**: The configuration property `MaxConcurrentSyncs` and its env var `DOCUMENTATION_MAX_CONCURRENT_SYNCS` suggest parallel operation, but in `RepositorySyncSchedulerService.ProcessScheduledSyncsAsync()` (line 186), each sync is `await`-ed sequentially in a `foreach` loop. The value is actually "max syncs per scheduler cycle" — a rate limit, not a concurrency limit.

10. **TotalContentSizeBytes is always an estimate**: `GetNamespaceStats` calculates content size as `documents * EstimatedBytesPerDocument` (configurable, defaults to 10KB). Accurate sizing requires iterating all documents (N+1 queries). Consider tracking actual content size in namespace metadata during CRUD operations.

11. **LastUpdated sampling is incomplete**: `GetNamespaceStats` only samples the first 10 document IDs from the namespace list to find `lastUpdated`. Newer documents further in the list are missed. Consider maintaining a `LastUpdatedAt` field on a namespace metadata record.

12. **Search index retains stale terms on document update**: `NamespaceIndex.AddDocument()` replaces the document and adds new terms, but does NOT remove old terms no longer in the updated content. Searching for removed terms still returns the document. Fix requires maintaining a term-to-document reverse index or removing the old document's terms before re-indexing.

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow.

### Completed

- **2026-01-31**: Fixed type mismatch for `ns-docs:{namespaceId}` key. Standardized on `HashSet<Guid>` for namespace document lists in `DocumentationService.GetNamespaceStatsAsync` and `SearchIndexService.RebuildIndexAsync`. Trashcan keys (`ns-trash:`) intentionally continue using `List<Guid>`.

- **2026-01-31**: Fixed N+1 query pattern in `ListDocumentsAsync` and `ListTrashcanAsync`. Both methods now use `GetBulkAsync` for bulk document retrieval instead of individual `GetAsync` calls in loops. `ListTrashcanAsync` also uses `DeleteBulkAsync` to batch-delete expired items.

- **2026-01-31**: Fixed `ViewDocumentBySlugAsync` return type from `(StatusCodes, object?)` to `(StatusCodes, string?)`. The method always returns HTML string content, so the typed return accurately reflects this. Removed unnecessary cast in `DocumentationController.ViewDocumentBySlug`.

- **2026-01-31**: Removed dead configuration properties `AiEnhancementsEnabled` and `AiEmbeddingsModel` from schema (T21 violation - never referenced in service code). Updated tests to remove assertions for removed properties. Semantic search remains a potential future extension.

- **2026-02-01**: Enabled Redis Search for documentation by adding `enableSearch: true` to `documentation-statestore` in `state-stores.yaml`. The `RedisSearchIndexService` was fully implemented but never activated because the store lacked the search configuration flag. With this fix, Redis Search (FT.*) will be used when the RediSearch module is available.

- **2026-03-01**: Full TENET compliance audit. Schema changes: consolidated 5 inline enums to `$ref` definitions, fixed 2 duplicated enums in events schema to `$ref` API types, removed 7 T8 filler properties from responses, added NRT `nullable: true` to ~50+ optional properties, added validation keywords to all config properties, added validation to ImportDocument and ListTrashcan fields. Code changes: changed `StoredDocument.Category` and `RepositoryBinding.DefaultCategory` from string to `DocumentCategory` enum (T25), replaced `Guid.Empty` sentinels with nullable `Guid?` (T26), replaced `DateTimeOffset.MinValue` with nullable (T26), extracted hardcoded 10000 bytes/doc to `EstimatedBytesPerDocument` config (T21), fixed three-part event topics to hyphenated entity names (T16), changed `IAssetClient` from constructor injection to `IServiceProvider` runtime resolution with graceful degradation (T4/T2 L3→L3 soft dependency), added `ArgumentNullException.ThrowIfNull` to all constructor parameters across all services (T6), moved telemetry activity creation inside try/catch in fire-and-forget analytics methods (T10).
