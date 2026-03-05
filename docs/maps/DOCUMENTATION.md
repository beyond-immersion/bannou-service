# Documentation Implementation Map

> **Plugin**: lib-documentation
> **Schema**: schemas/documentation-api.yaml
> **Layer**: AppFeatures
> **Deep Dive**: [docs/plugins/DOCUMENTATION.md](../plugins/DOCUMENTATION.md)

---

## Summary

| Field | Value |
|-------|-------|
| Plugin | lib-documentation |
| Layer | L3 AppFeatures |
| Endpoints | 27 (25 POST + 2 GET browser) |
| State Stores | documentation-statestore (Redis) |
| Events Published | 10 (document.created, document.updated, document.deleted, documentation.queried, documentation.searched, documentation-binding.created, documentation-binding.removed, documentation-sync.started, documentation-sync.completed, documentation-archive.created) |
| Events Consumed | 0 |
| Client Events | 0 |
| Background Services | 3 |

---

## State

**Store**: `documentation-statestore` (Backend: Redis, prefix: `doc`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{namespaceId}:{documentId}` | `StoredDocument` | Document content and metadata |
| `slug-idx:{namespaceId}:{slug}` | `string` (Guid text) | Slug-to-document-ID lookup index |
| `ns-docs:{namespaceId}` | `HashSet<Guid>` | All document IDs in a namespace |
| `ns-trash:{namespaceId}` | `List<Guid>` | Trashcan document ID list per namespace |
| `trash:{namespaceId}:{documentId}` | `TrashedDocument` | Soft-deleted document with TTL metadata |
| `ns-last-updated:{namespaceId}` | `string` (ISO 8601) | Most recent mutation timestamp per namespace |
| `repo-binding:{namespaceId}` | `RepositoryBinding` | Repository binding configuration |
| `repo-bindings` | `HashSet<string>` | Global registry of all bound namespace IDs |
| `all-namespaces` | `HashSet<string>` | Global registry of all namespaces |
| `archive:{archiveId}` | `DocumentationArchive` | Archive metadata record |
| `archive:list:{namespaceId}` | `List<Guid>` | Archive IDs for a namespace |
| `repo-sync:{namespaceId}` | Distributed Lock | Prevents concurrent sync operations |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | 8 typed store references for all persistence |
| lib-state (IDistributedLockProvider) | L0 | Hard | Sync operation locking (30-minute TTL) |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing 10 event topics |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Span instrumentation |
| lib-asset (IAssetClient) | L3 | Soft | Archive bundle upload/download via pre-signed URLs (graceful degradation if absent) |

Documentation is a **leaf node** — no other plugin injects `IDocumentationClient` or subscribes to Documentation events. No lib-resource integration (documents are self-contained).

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `document.created` | `DocumentCreatedEvent` | CreateDocument, ImportDocumentation (new), sync (new file) |
| `document.updated` | `DocumentUpdatedEvent` | UpdateDocument, BulkUpdateDocuments, ImportDocumentation (update), sync (changed file) |
| `document.deleted` | `DocumentDeletedEvent` | DeleteDocument, BulkDeleteDocuments |
| `documentation.queried` | `DocumentationQueriedEvent` | QueryDocumentation (fire-and-forget analytics) |
| `documentation.searched` | `DocumentationSearchedEvent` | SearchDocumentation (fire-and-forget analytics) |
| `documentation-binding.created` | `DocumentationBindingCreatedEvent` | BindRepository |
| `documentation-binding.removed` | `DocumentationBindingRemovedEvent` | UnbindRepository |
| `documentation-sync.started` | `DocumentationSyncStartedEvent` | ExecuteSyncAsync (internal) |
| `documentation-sync.completed` | `DocumentationSyncCompletedEvent` | ExecuteSyncAsync (all exit paths) |
| `documentation-archive.created` | `DocumentationArchiveCreatedEvent` | CreateDocumentationArchive |

---

## Events Consumed

This plugin does not consume external events. `RegisterEventConsumers` is a no-op.

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<DocumentationService>` | Structured logging |
| `DocumentationServiceConfiguration` | All 26 configuration properties |
| `IStateStoreFactory` | Redis state store access (8 typed stores) |
| `IDistributedLockProvider` | Sync operation locking |
| `IMessageBus` | Event publishing |
| `ITelemetryProvider` | Span instrumentation |
| `ISearchIndexService` | Full-text search (Redis Search or in-memory fallback) |
| `IGitSyncService` | Git clone/pull/file-list/read/cleanup |
| `IContentTransformService` | YAML frontmatter parsing, slug generation, markdown processing |
| `IServiceProvider` | Runtime resolution for IAssetClient (L3 soft) |
| `IHttpClientFactory` | HTTP client for pre-signed URL transfers |
| `IEventConsumer` | Event consumer registration (no-op) |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| ViewDocumentBySlug | GET /documentation/view/{slug} | [] | - | - |
| RawDocumentBySlug | GET /documentation/raw/{slug} | [] | - | - |
| QueryDocumentation | POST /documentation/query | [anonymous] | - | documentation.queried |
| GetDocument | POST /documentation/get | [anonymous] | - | - |
| SearchDocumentation | POST /documentation/search | [anonymous] | - | documentation.searched |
| ListDocuments | POST /documentation/list | [anonymous] | - | - |
| SuggestRelatedTopics | POST /documentation/suggest | [anonymous] | - | - |
| CreateDocument | POST /documentation/create | [admin] | doc, slug-idx, ns-docs, all-namespaces, ns-last-updated | document.created |
| UpdateDocument | POST /documentation/update | [admin] | doc, slug-idx, ns-last-updated | document.updated |
| DeleteDocument | POST /documentation/delete | [admin] | doc, slug-idx, ns-docs, ns-trash, trash | document.deleted |
| RecoverDocument | POST /documentation/recover | [admin] | doc, slug-idx, ns-docs, ns-trash, trash, ns-last-updated | - |
| BulkUpdateDocuments | POST /documentation/bulk-update | [admin] | doc, ns-last-updated | document.updated |
| BulkDeleteDocuments | POST /documentation/bulk-delete | [admin] | doc, slug-idx, ns-docs, ns-trash, trash | document.deleted |
| ImportDocumentation | POST /documentation/import | [admin] | doc, slug-idx, ns-docs, all-namespaces, ns-last-updated | document.created, document.updated |
| ListTrashcan | POST /documentation/trashcan | [admin] | ns-trash, trash (lazy cleanup) | - |
| PurgeTrashcan | POST /documentation/purge | [admin] | ns-trash, trash | - |
| GetNamespaceStats | POST /documentation/stats | [admin] | - | - |
| BindRepository | POST /documentation/repo/bind | [developer] | repo-binding, repo-bindings | documentation-binding.created |
| UnbindRepository | POST /documentation/repo/unbind | [admin] | repo-binding, repo-bindings, (docs if flag) | documentation-binding.removed |
| SyncRepository | POST /documentation/repo/sync | [developer] | doc, slug-idx, ns-docs, repo-binding, ns-last-updated | documentation-sync.started, documentation-sync.completed |
| GetRepositoryStatus | POST /documentation/repo/status | [developer] | - | - |
| ListRepositoryBindings | POST /documentation/repo/list | [developer] | - | - |
| UpdateRepositoryBinding | POST /documentation/repo/update | [developer] | repo-binding, repo-bindings | - |
| CreateDocumentationArchive | POST /documentation/repo/archive/create | [developer] | archive, archive:list | documentation-archive.created |
| ListDocumentationArchives | POST /documentation/repo/archive/list | [developer] | - | - |
| RestoreDocumentationArchive | POST /documentation/repo/archive/restore | [admin] | doc, slug-idx, ns-docs, ns-last-updated | - |
| DeleteDocumentationArchive | POST /documentation/repo/archive/delete | [admin] | archive, archive:list | - |

---

## Methods

### ViewDocumentBySlug
GET /documentation/view/{slug} | Roles: [] (public, manual controller)

```
// Browser-facing endpoint (FOUNDATION TENETS exception)
READ _stringStore:"slug-idx:{ns}:{slug}"             -> 404 if null or not parseable as Guid
READ _docStore:"{ns}:{documentId}"                    -> 404 if null
IF doc.Content is null/empty
  // Data integrity error — publish error event
  RETURN (500, null)
// Render markdown to HTML via static MarkdownPipeline
RETURN (200, rendered HTML string)
```

### RawDocumentBySlug
GET /documentation/raw/{slug} | Roles: [] (public, manual controller)

```
// Browser-facing endpoint (FOUNDATION TENETS exception)
// Delegates to GetDocumentAsync internally
READ _stringStore:"slug-idx:{ns}:{slug}"             -> 404 if null
READ _docStore:"{ns}:{documentId}"                    -> 404 if null
RETURN (200, raw markdown string with text/markdown content type)
```

### QueryDocumentation
POST /documentation/query | Roles: [anonymous]

```
IF namespace is whitespace                            -> 400
IF query is whitespace                                -> 400
// Clamp maxResults and minRelevance to config limits
CALL _searchIndexService.QueryAsync(namespace, query, category, maxResults, minRelevance)
FOREACH result in searchResults
  READ _docStore:"{namespace}:{result.DocumentId}"
  // Build DocumentResult with content/summary/voiceSummary/tags/related
PUBLISH "documentation.queried" { namespace, query, sessionId, resultCount, topResultId, relevanceScore }
  // Fire-and-forget (discarded Task)
RETURN (200, QueryDocumentationResponse { Results, TotalResults })
```

### GetDocument
POST /documentation/get | Roles: [anonymous]

```
IF namespace is whitespace                            -> 400
IF neither documentId nor slug provided               -> 400
IF documentId is Guid.Empty                           -> 400
IF slug provided (and no documentId)
  READ _stringStore:"slug-idx:{namespace}:{slug}"     -> 404 if null or not parseable
READ _docStore:"{namespace}:{documentId}"             -> 404 if null
IF includeContent && doc.Content is null
  // Data integrity error — publish error event
  RETURN (500, null)
IF includeContent && renderHtml
  // Render markdown to HTML
IF includeRelated != None && doc has relatedDocuments
  FOREACH relatedId in doc.RelatedDocuments (up to config.MaxRelatedDocuments/Extended)
    READ _docStore:"{namespace}:{relatedId}"
RETURN (200, GetDocumentResponse { Document, ContentFormat, RelatedDocuments })
```

### SearchDocumentation
POST /documentation/search | Roles: [anonymous]

```
IF namespace is whitespace                            -> 400
IF searchTerm is whitespace                           -> 400
// Check static in-process cache (ConcurrentDictionary with TTL)
IF cached result exists for (namespace, searchTerm, category, maxResults, sortBy)
  RETURN (200, cached response)
CALL _searchIndexService.SearchAsync(namespace, searchTerm, category, maxResults)
FOREACH result in searchResults
  READ _docStore:"{namespace}:{result.DocumentId}"
  // GenerateSearchSnippet for match highlights
// Sort by: Relevance (default) / Recency / Alphabetical
// Filter results below config.MinRelevanceScore
// Cache result if config.SearchCacheTtlSeconds > 0
PUBLISH "documentation.searched" { namespace, searchTerm, sessionId, resultCount }
  // Fire-and-forget
RETURN (200, SearchDocumentationResponse { Results, TotalResults })
```

### ListDocuments
POST /documentation/list | Roles: [anonymous]

```
IF namespace is whitespace                            -> 400
// Over-fetch when filtering by tags or sorting
CALL _searchIndexService.ListDocumentIdsAsync(namespace, category, 0, fetchLimit)
BULK READ _docStore for all returned document keys
IF tags filter provided
  // In-memory tag filter: All mode (all tags present) or Any mode (any tag present)
IF date filters provided
  // In-memory date range filter on CreatedAt/UpdatedAt
// Sort by: CreatedAt / UpdatedAt / Title (asc/desc)
// Paginate in-memory: Skip((page-1)*pageSize).Take(pageSize)
RETURN (200, ListDocumentsResponse { Documents, TotalCount, TotalPages })
```

### SuggestRelatedTopics
POST /documentation/suggest | Roles: [anonymous]

```
IF sourceValue is null/empty                          -> 400
CALL _searchIndexService.GetRelatedSuggestionsAsync(namespace, sourceValue, maxSuggestions)
FOREACH docId in suggestions
  READ _docStore:"{namespace}:{docId}"
  // DetermineRelevanceReason based on SuggestionSource enum
RETURN (200, SuggestRelatedResponse { Suggestions, Namespace })
```

### CreateDocument
POST /documentation/create | Roles: [admin]

```
IF namespace is whitespace                            -> 400
IF slug is whitespace                                 -> 400
IF title is whitespace                                -> 400
IF content exceeds config.MaxContentSizeBytes          -> 400
READ _bindingStore:"repo-binding:{namespace}"
IF binding exists and status != Disabled              -> 403
READ _stringStore:"slug-idx:{namespace}:{slug}"
IF slug already exists                                -> 409
// TruncateVoiceSummary to config.VoiceSummaryMaxLength
CALL _searchIndexService.EnsureIndexExistsAsync(namespace)
WRITE _docStore:"{namespace}:{documentId}" <- StoredDocument from request
WRITE _stringStore:"slug-idx:{namespace}:{slug}" <- documentId
// AddDocumentToNamespaceIndexAsync (ETag retry x3):
WRITE _guidSetStore:"ns-docs:{namespace}" <- add documentId
WRITE _stringSetStore:"all-namespaces" <- add namespace
WRITE _stringStore:"ns-last-updated:{namespace}" <- now
CALL _searchIndexService.IndexDocument(...)
PUBLISH "document.created" { documentId, namespace, slug, title, category, tags, createdAt, updatedAt }
RETURN (200, CreateDocumentResponse { DocumentId, Slug, CreatedAt })
```

### UpdateDocument
POST /documentation/update | Roles: [admin]

```
READ _bindingStore:"repo-binding:{namespace}"
IF binding exists and status != Disabled              -> 403
READ _docStore:"{namespace}:{documentId}"             -> 404 if null
IF content update exceeds config.MaxContentSizeBytes  -> 400
// Track changedFields list for event
// Apply only non-null/non-empty fields from request (partial update)
IF slug changed
  READ _stringStore:"slug-idx:{namespace}:{newSlug}"
  IF new slug conflicts                               -> 409
  DELETE _stringStore:"slug-idx:{namespace}:{oldSlug}"
  WRITE _stringStore:"slug-idx:{namespace}:{newSlug}" <- documentId
WRITE _docStore:"{namespace}:{documentId}" <- updated StoredDocument
WRITE _stringStore:"ns-last-updated:{namespace}" <- now
CALL _searchIndexService.IndexDocument(...)
PUBLISH "document.updated" { documentId, namespace, slug, title, category, tags, updatedAt, changedFields }
RETURN (200, UpdateDocumentResponse { DocumentId, UpdatedAt })
```

### DeleteDocument
POST /documentation/delete | Roles: [admin]

```
IF namespace is whitespace                            -> 400
IF neither documentId nor slug provided               -> 400
IF documentId is Guid.Empty                           -> 400
READ _bindingStore:"repo-binding:{namespace}"
IF binding exists and status != Disabled              -> 403
IF slug provided (and no documentId)
  READ _stringStore:"slug-idx:{namespace}:{slug}"     -> 404 if null
READ _docStore:"{namespace}:{documentId}"             -> 404 if null
// Create TrashedDocument with ExpiresAt = now + config.TrashcanTtlDays
WRITE _trashStore:"trash:{namespace}:{documentId}" <- TrashedDocument
// AddDocumentToTrashcanIndexAsync (ETag retry x3):
WRITE _guidListStore:"ns-trash:{namespace}" <- add documentId
DELETE _docStore:"{namespace}:{documentId}"
DELETE _stringStore:"slug-idx:{namespace}:{slug}"
// RemoveDocumentFromNamespaceIndexAsync (ETag retry x3):
WRITE _guidSetStore:"ns-docs:{namespace}" <- remove documentId
CALL _searchIndexService.RemoveDocument(namespace, documentId)
PUBLISH "document.deleted" { documentId, namespace, slug, title, category, tags, deletedReason: "User requested deletion" }
RETURN (200, DeleteDocumentResponse { DocumentId, DeletedAt, RecoverableUntil })
```

### RecoverDocument
POST /documentation/recover | Roles: [admin]

```
READ _trashStore:"trash:{namespace}:{documentId}"     -> 404 if null
IF trashedDoc.ExpiresAt < now
  DELETE _trashStore:"trash:{namespace}:{documentId}"
  // Clean up stale trashcan index entry
  RETURN (404, null)
READ _stringStore:"slug-idx:{namespace}:{slug}"
IF original slug now occupied                         -> 409
CALL _searchIndexService.EnsureIndexExistsAsync(namespace)
WRITE _docStore:"{namespace}:{documentId}" <- restored StoredDocument
WRITE _stringStore:"slug-idx:{namespace}:{slug}" <- documentId
// AddDocumentToNamespaceIndexAsync (ETag retry x3):
WRITE _guidSetStore:"ns-docs:{namespace}" <- add documentId
WRITE _stringStore:"ns-last-updated:{namespace}" <- now
DELETE _trashStore:"trash:{namespace}:{documentId}"
// RemoveDocumentFromTrashcanIndexAsync (ETag retry x3):
WRITE _guidListStore:"ns-trash:{namespace}" <- remove documentId
CALL _searchIndexService.IndexDocument(...)
RETURN (200, RecoverDocumentResponse { DocumentId, RecoveredAt })
```

### BulkUpdateDocuments
POST /documentation/bulk-update | Roles: [admin]

```
// No repository binding check
FOREACH documentId in request.DocumentIds
  // try/catch per document — failures recorded, do not abort batch
  READ _docStore:"{namespace}:{documentId}"
  IF null -> add to failed list, continue
  // Apply category change, addTags (case-insensitive dedup), removeTags
  IF any field changed
    WRITE _docStore:"{namespace}:{documentId}" <- updated StoredDocument
    CALL _searchIndexService.IndexDocument(...)
    PUBLISH "document.updated" { documentId, ..., changedFields }
  // Yield every config.BulkOperationBatchSize documents
IF any succeeded
  WRITE _stringStore:"ns-last-updated:{namespace}" <- now
RETURN (200, BulkUpdateResponse { Succeeded, Failed })
```

### BulkDeleteDocuments
POST /documentation/bulk-delete | Roles: [admin]

```
// No repository binding check
FOREACH documentId in request.DocumentIds
  // try/catch per document — failures recorded, do not abort batch
  READ _docStore:"{namespace}:{documentId}"
  IF null -> add to failed list, continue
  // Same soft-delete pattern as DeleteDocument
  WRITE _trashStore:"trash:{namespace}:{documentId}" <- TrashedDocument
  WRITE _guidListStore:"ns-trash:{namespace}" <- add documentId (ETag retry)
  DELETE _docStore:"{namespace}:{documentId}"
  DELETE _stringStore:"slug-idx:{namespace}:{slug}"
  WRITE _guidSetStore:"ns-docs:{namespace}" <- remove documentId (ETag retry)
  CALL _searchIndexService.RemoveDocument(namespace, documentId)
  PUBLISH "document.deleted" { ..., deletedReason: "Bulk delete operation" }
  // Yield every config.BulkOperationBatchSize documents
RETURN (200, BulkDeleteResponse { Succeeded, Failed })
```

### ImportDocumentation
POST /documentation/import | Roles: [admin]

```
IF documents.Count > config.MaxImportDocuments (when > 0)  -> 400
FOREACH doc in request.Documents
  // try/catch per document — failures recorded
  IF content exceeds config.MaxContentSizeBytes -> add to failed, continue
  READ _stringStore:"slug-idx:{namespace}:{slug}"
  IF slug exists
    IF onConflict == Skip -> increment skipped, continue
    IF onConflict == Fail -> add to failed, continue
    IF onConflict == Update
      READ _docStore:"{existingKey}" (existing doc)
      // Overwrite all fields from import
      WRITE _docStore:"{existingKey}" <- updated StoredDocument
      CALL _searchIndexService.IndexDocument(...)
      PUBLISH "document.updated" { ..., changedFields: ["title","category","content","summary","tags"] }
      // increment updated
  ELSE (new document)
    CALL _searchIndexService.EnsureIndexExistsAsync(namespace)
    WRITE _docStore:"{namespace}:{newId}" <- StoredDocument from import
    WRITE _stringStore:"slug-idx:{namespace}:{slug}" <- newId
    WRITE _guidSetStore:"ns-docs:{namespace}" <- add newId (ETag retry)
    CALL _searchIndexService.IndexDocument(...)
    PUBLISH "document.created" { ... }
    // increment created
IF created > 0 || updated > 0
  WRITE _stringStore:"ns-last-updated:{namespace}" <- now
RETURN (200, ImportDocumentationResponse { Namespace, Created, Updated, Skipped, Failed })
```

### ListTrashcan
POST /documentation/trashcan | Roles: [admin]

```
READ _guidListStore:"ns-trash:{namespace}"
IF null -> empty list
BULK READ _trashStore for all trashcan entries
// Lazy expiry cleanup: remove expired entries inline
FOREACH entry in trashcan
  IF entry missing from store OR entry.ExpiresAt < now
    DELETE _trashStore:"trash:{namespace}:{docId}" (if exists)
    // Mark for removal from index
IF expired entries found
  WRITE _guidListStore:"ns-trash:{namespace}" <- updated list
// Sort remaining by DeletedAt descending
// Paginate: Skip((page-1)*pageSize).Take(pageSize)
RETURN (200, ListTrashcanResponse { Namespace, Items, TotalCount })
```

### PurgeTrashcan
POST /documentation/purge | Roles: [admin]

```
READ _guidListStore:"ns-trash:{namespace}" [with ETag]
IF documentIds provided
  // Targeted purge: intersect provided IDs with trashcan
ELSE
  // Full purge: all items in trashcan
FOREACH docId in purgeTargets
  DELETE _trashStore:"trash:{namespace}:{docId}"
IF trashcan becomes empty
  DELETE _guidListStore:"ns-trash:{namespace}"
ELSE
  ETAG-WRITE _guidListStore:"ns-trash:{namespace}" <- remaining items
  IF ETag mismatch                                    -> 409
RETURN (200, PurgeTrashcanResponse { PurgedCount })
```

### GetNamespaceStats
POST /documentation/stats | Roles: [admin]

```
CALL _searchIndexService.GetNamespaceStatsAsync(namespace)
READ _guidListStore:"ns-trash:{namespace}"
READ _stringStore:"ns-last-updated:{namespace}"
// TotalContentSizeBytes = documents * config.EstimatedBytesPerDocument (estimate)
RETURN (200, NamespaceStatsResponse { Namespace, DocumentCount, CategoryCounts, TrashcanCount, TotalContentSizeBytes, LastUpdated })
```

### BindRepository
POST /documentation/repo/bind | Roles: [developer]

```
IF namespace is whitespace                            -> 400
IF repositoryUrl is whitespace                        -> 400
READ _bindingStore:"repo-binding:{namespace}"
IF binding already exists                             -> 409
// Create RepositoryBinding with status: Pending, defaults for patterns/intervals
WRITE _bindingStore:"repo-binding:{namespace}" <- binding
// SaveBindingAsync: also updates repo-bindings registry (non-atomic read-write)
WRITE _stringSetStore:"repo-bindings" <- add namespace
PUBLISH "documentation-binding.created" { namespace, bindingId, repositoryUrl, branch }
RETURN (200, BindRepositoryResponse { BindingId, Namespace, RepositoryUrl, Branch, Status: Pending, CreatedAt })
```

### UnbindRepository
POST /documentation/repo/unbind | Roles: [admin]

```
READ _bindingStore:"repo-binding:{namespace}"         -> 404 if null
IF deleteDocuments flag
  // DeleteAllNamespaceDocumentsAsync: hard-deletes all docs (no trashcan, no events)
  READ _guidSetStore:"ns-docs:{namespace}"
  FOREACH docId
    DELETE _docStore:"{namespace}:{docId}"
    // Also deletes slug indexes, removes from search index
  DELETE _guidSetStore:"ns-docs:{namespace}"
DELETE _bindingStore:"repo-binding:{namespace}"
// Remove from repo-bindings registry (non-atomic read-write)
WRITE _stringSetStore:"repo-bindings" <- remove namespace
CALL _gitSyncService.CleanupRepositoryAsync(localPath)
PUBLISH "documentation-binding.removed" { namespace, bindingId, documentsDeleted }
RETURN (200, UnbindRepositoryResponse { Namespace, DocumentsDeleted })
```

### SyncRepository
POST /documentation/repo/sync | Roles: [developer]

```
IF namespace is whitespace                            -> 400
READ _bindingStore:"repo-binding:{namespace}"         -> 404 if null
// ExecuteSyncAsync:
LOCK _lockProvider:"repo-sync:{namespace}" (TTL: config.SyncLockTtlSeconds)
  IF lock not acquired -> RETURN (200, SyncResponse { Status: Failed, ErrorMessage: "Sync already in progress" })
  WRITE _bindingStore status = Syncing
  PUBLISH "documentation-sync.started" { namespace, bindingId, syncId, triggeredBy }
  CALL _gitSyncService.SyncRepositoryAsync(url, branch, localPath)
  IF !force && commitHash unchanged -> skip (return Success with 0 counts)
  CALL _gitSyncService.GetMatchingFilesAsync(localPath, filePatterns, excludePatterns)
  // Apply MaxDocumentsPerSync limit
  FOREACH file in matchingFiles
    CALL _gitSyncService.ReadFileContentAsync(file)
    CALL _contentTransformService.TransformFile(content, filePath, binding)
    IF draft -> skip
    // CreateDocumentFromTransformAsync or UpdateDocumentFromTransformAsync
    // Writes doc, slug index, namespace index; publishes document.created/updated
  IF file list not truncated
    // Delete orphan documents (slugs not in processed set)
  WRITE _bindingStore <- status=Synced, LastSyncAt, LastCommitHash, NextSyncAt
  PUBLISH "documentation-sync.completed" { status, commitHash, counts, durationMs }
RETURN (200, SyncRepositoryResponse { SyncId, Status, CommitHash, DocumentsCreated/Updated/Deleted, DurationMs })
```

### GetRepositoryStatus
POST /documentation/repo/status | Roles: [developer]

```
READ _bindingStore:"repo-binding:{namespace}"         -> 404 if null
// Map binding fields to RepositoryBindingInfo and SyncInfo
// Note: LastSync.TriggeredBy hardcoded to Scheduled regardless of actual trigger
RETURN (200, RepositoryStatusResponse { Binding, LastSync })
```

### ListRepositoryBindings
POST /documentation/repo/list | Roles: [developer]

```
READ _stringSetStore:"repo-bindings"
FOREACH namespaceId in registry
  READ _bindingStore:"repo-binding:{namespaceId}"
  // Map to RepositoryBindingInfo
IF status filter provided
  // In-memory filter by binding status
// Pagination: Skip(offset).Take(limit)
RETURN (200, ListRepositoryBindingsResponse { Bindings, Total })
```

### UpdateRepositoryBinding
POST /documentation/repo/update | Roles: [developer]

```
READ _bindingStore:"repo-binding:{namespace}"         -> 404 if null
// Apply non-null fields: syncEnabled, syncIntervalMinutes, filePatterns,
//   excludePatterns, categoryMapping, defaultCategory, archiveEnabled, archiveOnSync
// Note: syncEnabled and syncIntervalMinutes always overwritten (no null check)
WRITE _bindingStore:"repo-binding:{namespace}" <- updated binding
WRITE _stringSetStore:"repo-bindings" <- ensure namespace present
RETURN (200, UpdateRepositoryBindingResponse { Binding })
```

### CreateDocumentationArchive
POST /documentation/repo/archive/create | Roles: [developer]

```
IF namespace is whitespace                            -> 400
// GetAllNamespaceDocumentsAsync:
READ _guidSetStore:"ns-docs:{namespace}"
BULK READ _docStore for all document keys             -> 404 if no documents
// Create GZip-compressed JSON bundle from all documents (exclude null-content docs)
IF _serviceProvider.GetService<IAssetClient>() is not null
  CALL assetClient.RequestBundleUploadAsync({ owner, filename, size })
  HTTP PUT bundleData to pre-signed upload URL
  // On failure: log warning, continue without BundleAssetId
// GetCurrentCommitHashForNamespaceAsync: reads binding for LastCommitHash
WRITE _archiveStore:"archive:{archiveId}" <- DocumentationArchive
// SaveArchiveAsync (ETag retry): add to archive:list:{namespace}
WRITE _guidListStore:"archive:list:{namespace}" <- add archiveId
PUBLISH "documentation-archive.created" { namespace, archiveId, bundleAssetId, documentCount, sizeBytes }
RETURN (200, CreateArchiveResponse { ArchiveId, Namespace, BundleAssetId, DocumentCount, SizeBytes, CreatedAt })
```

### ListDocumentationArchives
POST /documentation/repo/archive/list | Roles: [developer]

```
IF namespace is whitespace                            -> 400
READ _guidListStore:"archive:list:{namespace}"
FOREACH archiveId in list
  READ _archiveStore:"archive:{archiveId}"
// Sort by CreatedAt descending
// Paginate: Skip(offset).Take(limit)
RETURN (200, ListArchivesResponse { Archives, Total })
```

### RestoreDocumentationArchive
POST /documentation/repo/archive/restore | Roles: [admin]

```
READ _archiveStore:"archive:{archiveId}"              -> 404 if null
// Determine target namespace (from request or archive)
READ _bindingStore:"repo-binding:{namespace}"
IF binding exists and status != Disabled              -> 403
IF archive.BundleAssetId is null                      -> 404
IF _serviceProvider.GetService<IAssetClient>() is null -> 404
CALL assetClient.GetBundleAsync({ bundleId })
  // ApiException with status 404 -> RETURN (404, null)
HTTP GET bundleData from pre-signed download URL
// RestoreFromBundleAsync:
//   Hard-delete all existing docs in namespace (DeleteAllNamespaceDocumentsAsync)
//   Decompress GZip, deserialize JSON bundle
//   FOREACH doc in bundle
//     WRITE _docStore:"{namespace}:{docId}" <- restored doc (UpdatedAt = now)
//     WRITE _stringStore:"slug-idx:{namespace}:{slug}" <- docId
//     CALL _searchIndexService.IndexDocument(...)
//   WRITE _guidSetStore:"ns-docs:{namespace}" <- all restored docIds
//   WRITE _stringStore:"ns-last-updated:{namespace}" <- now
RETURN (200, RestoreArchiveResponse { Namespace, DocumentsRestored })
```

### DeleteDocumentationArchive
POST /documentation/repo/archive/delete | Roles: [admin]

```
READ _archiveStore:"archive:{archiveId}"              -> 404 if null
// DeleteArchiveAsync:
DELETE _archiveStore:"archive:{archiveId}"
READ _guidListStore:"archive:list:{namespace}"
  // Remove archiveId from list (non-atomic, no ETag)
WRITE _guidListStore:"archive:list:{namespace}" <- updated list
// Note: bundle file in Asset Service is NOT deleted
RETURN (200, DeleteArchiveResponse {})
```

---

## Background Services

### SearchIndexRebuildService
**Interval**: Runs once on startup (delay: config.SearchIndexRebuildStartupDelaySeconds)
**Purpose**: Rebuilds in-memory search index from Redis state after service restart

```
IF !config.SearchIndexRebuildOnStartup -> return
// Wait startup delay
READ _stringSetStore:"all-namespaces"
READ _stringSetStore:"repo-bindings"
allNamespaces = union of both sets
FOREACH namespaceId in allNamespaces
  CALL _searchIndexService.RebuildIndexAsync(namespaceId)
    // Reads ns-docs:{namespace} for doc IDs
    // Reads {namespace}:{docId} for each doc
    // Rebuilds inverted index in memory
```

### RepositorySyncSchedulerService
**Interval**: config.SyncSchedulerCheckIntervalMinutes (initial delay: config.RepositorySyncCheckIntervalSeconds)
**Purpose**: Triggers sync for due repository bindings; cleans up stale git clones

```
IF !config.SyncSchedulerEnabled -> return
// ProcessScheduledSyncsAsync:
READ _stringSetStore:"repo-bindings"
FOREACH namespaceId (up to config.MaxSyncsPerCycle)
  READ _bindingStore:"repo-binding:{namespaceId}"
  IF !syncEnabled OR status in [Disabled, Syncing] -> skip
  IF NextSyncAt <= now OR never synced
    CALL documentationService.SyncRepositoryAsync({ Namespace, Force: false })
// CleanupStaleRepositoriesAsync:
// Scan config.GitStoragePath for GUID-named directories
// Delete if not in valid binding IDs AND older than config.GitStorageCleanupHours
```

### TrashcanPurgeService
**Interval**: config.TrashcanPurgeCheckIntervalMinutes (initial delay: same)
**Purpose**: Permanently deletes expired trashcan entries across all namespaces

```
IF !config.TrashcanPurgeEnabled -> return
// DiscoverNamespacesAsync: union of all-namespaces and repo-bindings
FOREACH namespaceId in allNamespaces
  READ _guidListStore:"ns-trash:{namespace}" [with ETag]
  FOREACH docId in list
    READ _trashStore:"trash:{namespace}:{docId}"
    IF null OR ExpiresAt < now -> mark as expired
  FOREACH expiredId
    DELETE _trashStore:"trash:{namespace}:{expiredId}"
  IF any expired
    ETAG-WRITE _guidListStore:"ns-trash:{namespace}" <- remaining items
    // Skip on ETag conflict (retry next cycle)
```
