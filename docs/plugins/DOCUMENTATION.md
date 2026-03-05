# Documentation Plugin Deep Dive

> **Plugin**: lib-documentation
> **Schema**: schemas/documentation-api.yaml
> **Version**: 1.0.0
> **Layer**: AppFeatures
> **State Store**: documentation-statestore (Redis)
> **Implementation Map**: [docs/maps/DOCUMENTATION.md](../maps/DOCUMENTATION.md)

---

## Overview

Knowledge base API (L3 AppFeatures) designed for AI agents (SignalWire SWAIG, OpenAI function calling, Claude tool use) with full-text search, natural language query, and voice-friendly summaries. Manages documentation within namespaces, supporting manual CRUD and automated git repository synchronization (git-bound namespaces reject mutations, enforcing git as single source of truth). Features browser-facing GET endpoints that render markdown to HTML (unusual exception to Bannou's POST-only pattern). Three background services handle index rebuilding, periodic repository sync, and trashcan purge.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| (none identified) | No other plugins reference `IDocumentationClient` or depend on Documentation events |

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

(none currently)

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow.

(No completed items)
