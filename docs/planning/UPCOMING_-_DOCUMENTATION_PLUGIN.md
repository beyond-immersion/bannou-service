# Documentation Plugin Implementation Guide

> **Target Service**: `lib-documentation`
> **TENETS Compliance**: Verified against `docs/reference/TENETS.md`

## Executive Summary

The Documentation Plugin (`lib-documentation`) provides a knowledge base API for storing, retrieving, and querying documentation. Primary use cases include AI agent integration (SignalWire SWAIG, OpenAI function calling, Claude tool use) for contextual information retrieval during voice or chat conversations.

### Design Goals

1. **AI Agent Compatibility**: REST API compatible with any AI agent tool calling mechanism
2. **Voice-Optimized Responses**: Concise, speakable summaries alongside detailed content
3. **Schema-First Development**: Full TENETS compliance with schema-driven architecture
4. **Full-Text Search**: In-memory index with full rebuild on startup (Dapr-compliant)
5. **Multi-Namespace Support**: Complete documentation isolation by namespace
6. **Browser Access**: NGINX-routed endpoint for documentation viewing (Tenet 15)

---

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Browser Access | `GET /documentation/view/{slug}` | NGINX-routed endpoint per Tenet 15 |
| Search Strategy | In-Memory Index | Full rebuild on startup, Dapr-compliant |
| Namespace Lifecycle | Auto-Create | Created on first document, no explicit endpoints |
| Import Atomicity | Partial Success | Each document independent, failures reported |
| Session Tracking | Informal Redis | UUID + TTL, no Auth integration |
| Circular References | Allowed | Client handles navigation |
| Index Rebuild | Full on Startup | Load all docs from state store |
| AI Enhancement | Config Flags (Disabled) | Include properties, default disabled |
| Content Size | Medium (<500KB) | Reasonable memory footprint |
| Categories | Fixed Enum | Schema-defined, type-safe |
| Event Subscriptions | Minimal | Sessions use TTL cleanup |

---

## Required Schema Files

All four schema files are required per TENETS:

```
schemas/
├── documentation-api.yaml           # API endpoints with x-permissions
├── documentation-events.yaml        # Service events with x-lifecycle
├── documentation-configuration.yaml # Service configuration
└── documentation-client-events.yaml # WebSocket push events (optional - not initially needed)
```

---

## Schema: documentation-configuration.yaml

**REQUIRED** - Service configuration with `DOCUMENTATION_` prefix per Tenet 2.

```yaml
# schemas/documentation-configuration.yaml
openapi: 3.0.0
info:
  title: Documentation Service Configuration
  version: 1.0.0
  x-service-configuration:
    service: documentation
    properties:
      SearchIndexRebuildOnStartup:
        type: boolean
        default: true
        env: DOCUMENTATION_SEARCH_INDEX_REBUILD_ON_STARTUP
        description: Whether to rebuild search index on service startup
      SessionTtlSeconds:
        type: integer
        default: 86400
        env: DOCUMENTATION_SESSION_TTL_SECONDS
        description: TTL for informal session tracking (24 hours default)
      MaxContentSizeBytes:
        type: integer
        default: 524288
        env: DOCUMENTATION_MAX_CONTENT_SIZE_BYTES
        description: Maximum document content size (500KB default)
      TrashcanTtlDays:
        type: integer
        default: 7
        env: DOCUMENTATION_TRASHCAN_TTL_DAYS
        description: Days before trashcan items are auto-purged
      VoiceSummaryMaxLength:
        type: integer
        default: 200
        env: DOCUMENTATION_VOICE_SUMMARY_MAX_LENGTH
        description: Maximum characters for voice summaries
      SearchCacheTtlSeconds:
        type: integer
        default: 300
        env: DOCUMENTATION_SEARCH_CACHE_TTL_SECONDS
        description: TTL for search result caching
      MinRelevanceScore:
        type: number
        default: 0.3
        env: DOCUMENTATION_MIN_RELEVANCE_SCORE
        description: Default minimum relevance score for search results
      MaxSearchResults:
        type: integer
        default: 20
        env: DOCUMENTATION_MAX_SEARCH_RESULTS
        description: Maximum search results to return
      MaxImportDocuments:
        type: integer
        default: 0
        env: DOCUMENTATION_MAX_IMPORT_DOCUMENTS
        description: Maximum documents per import (0 = unlimited)
      # AI Enhancement flags (disabled by default)
      AiEnhancementsEnabled:
        type: boolean
        default: false
        env: DOCUMENTATION_AI_ENHANCEMENTS_ENABLED
        description: Enable AI-powered semantic search (future feature)
      AiEmbeddingsModel:
        type: string
        default: ""
        env: DOCUMENTATION_AI_EMBEDDINGS_MODEL
        description: Model for generating embeddings (when AI enabled)
```

---

## Schema: documentation-events.yaml

Use `x-lifecycle` for automatic CRUD event generation and `x-event-subscriptions` per Tenet 5.

```yaml
# schemas/documentation-events.yaml
openapi: 3.0.0
info:
  title: Documentation Events
  version: 1.0.0
  # Minimal event subscriptions - sessions use TTL cleanup, no external events needed
  x-event-subscriptions: []

# Auto-generate CRUD events for Document entity
x-lifecycle:
  Document:
    model:
      documentId:
        type: string
        format: uuid
        primary: true
        required: true
      namespace:
        type: string
        required: true
      slug:
        type: string
        required: true
      title:
        type: string
        required: true
      category:
        type: string
        required: true
      tags:
        type: array
        items:
          type: string
      createdAt:
        type: string
        format: date-time
        required: true
      updatedAt:
        type: string
        format: date-time
        required: true
    # Fields excluded from events (content too large for pub/sub)
    sensitive:
      - content
      - summary
      - voiceSummary
      - metadata

components:
  schemas:
    # Analytics events (manually defined - not CRUD lifecycle)
    DocumentationQueriedEvent:
      type: object
      required:
        - event_id
        - timestamp
        - namespace
        - query
      properties:
        event_id:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        namespace:
          type: string
        query:
          type: string
        session_id:
          type: string
          format: uuid
        result_count:
          type: integer
        top_result_id:
          type: string
          format: uuid
        relevance_score:
          type: number

    DocumentationSearchedEvent:
      type: object
      required:
        - event_id
        - timestamp
        - namespace
        - search_term
      properties:
        event_id:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        namespace:
          type: string
        search_term:
          type: string
        session_id:
          type: string
          format: uuid
        result_count:
          type: integer

    DocumentViewedEvent:
      type: object
      required:
        - event_id
        - timestamp
        - namespace
        - document_id
      properties:
        event_id:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        namespace:
          type: string
        document_id:
          type: string
          format: uuid
        session_id:
          type: string
          format: uuid
        source:
          type: string
          enum:
            - query
            - search
            - direct
            - related
            - browser

    # Import events (manual - special bulk operations)
    DocumentationImportStartedEvent:
      type: object
      required:
        - event_id
        - timestamp
        - namespace
        - document_count
      properties:
        event_id:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        namespace:
          type: string
        document_count:
          type: integer

    DocumentationImportCompletedEvent:
      type: object
      required:
        - event_id
        - timestamp
        - namespace
        - succeeded_count
        - failed_count
      properties:
        event_id:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        namespace:
          type: string
        succeeded_count:
          type: integer
        failed_count:
          type: integer
        duration_ms:
          type: integer
```

**Event Topics** (per Tenet 16 naming: `{entity}.{action}`):
- `document.created` - Document lifecycle
- `document.updated` - Document lifecycle
- `document.deleted` - Document lifecycle
- `documentation.queried` - Analytics
- `documentation.searched` - Analytics
- `documentation.viewed` - Analytics

---

## Schema: documentation-api.yaml

Key requirements per TENETS:
- Server URL MUST use `bannou` app-id
- All POST endpoints MUST have `x-permissions`
- Browser endpoint (`GET`) has NO `x-permissions` (Tenet 15)

```yaml
# schemas/documentation-api.yaml
openapi: 3.0.0
info:
  title: Bannou Documentation API
  version: 1.0.0
  description: |
    Knowledge base API for AI agents to query documentation.
    Designed for SignalWire SWAIG, OpenAI function calling, and Claude tool use.
    All endpoints return voice-friendly summaries alongside detailed content.

servers:
  - url: http://localhost:3500/v1.0/invoke/bannou/method
    description: Dapr sidecar endpoint (MUST use "bannou" app-id)

paths:
  # ===== Browser-Facing Endpoint (Tenet 15) =====
  # NO x-permissions = NGINX-routed, not WebSocket accessible
  /documentation/view/{slug}:
    get:
      operationId: ViewDocumentBySlug
      summary: View documentation page in browser
      description: |
        Browser-facing endpoint for viewing documentation.
        Routed via NGINX, not exposed to WebSocket clients.
        Returns HTML-rendered documentation page.
      parameters:
        - name: slug
          in: path
          required: true
          schema:
            type: string
            pattern: '^[a-z0-9-]+$'
          description: Document slug within namespace
        - name: namespace
          in: query
          required: false
          schema:
            type: string
            default: "bannou"
          description: Documentation namespace (defaults to bannou)
      responses:
        '200':
          description: HTML documentation page
          content:
            text/html:
              schema:
                type: string
        '404':
          description: Document not found
      # NOTE: No x-permissions = browser-facing endpoint (Tenet 15)

  # ===== Query Endpoints (WebSocket accessible) =====
  /documentation/query:
    post:
      operationId: QueryDocumentation
      summary: Natural language documentation search
      description: |
        Search documentation using natural language queries.
        Returns the most relevant documents with voice-friendly summaries.
      x-permissions:
        - role: anonymous
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/QueryDocumentationRequest'
      responses:
        '200':
          description: Search results with voice-friendly summaries
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/QueryDocumentationResponse'
        '400':
          description: Invalid request
        '500':
          description: Internal server error

  /documentation/get:
    post:
      operationId: GetDocument
      summary: Get specific document by ID or slug
      x-permissions:
        - role: anonymous
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetDocumentRequest'
      responses:
        '200':
          description: Document content
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/GetDocumentResponse'
        '404':
          description: Document not found

  /documentation/search:
    post:
      operationId: SearchDocumentation
      summary: Full-text keyword search
      x-permissions:
        - role: anonymous
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/SearchDocumentationRequest'
      responses:
        '200':
          description: Matching documents
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SearchDocumentationResponse'

  /documentation/list:
    post:
      operationId: ListDocuments
      summary: List documents by category
      x-permissions:
        - role: anonymous
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListDocumentsRequest'
      responses:
        '200':
          description: Document list
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ListDocumentsResponse'

  /documentation/suggest:
    post:
      operationId: SuggestRelatedTopics
      summary: Get related topics and follow-up suggestions
      x-permissions:
        - role: anonymous
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/SuggestRelatedRequest'
      responses:
        '200':
          description: Related topics
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SuggestRelatedResponse'

  # ===== Admin Endpoints =====
  /documentation/create:
    post:
      operationId: CreateDocument
      summary: Create new documentation entry
      x-permissions:
        - role: admin
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CreateDocumentRequest'
      responses:
        '200':
          description: Document created
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CreateDocumentResponse'
        '409':
          description: Document with same slug already exists

  /documentation/update:
    post:
      operationId: UpdateDocument
      summary: Update existing documentation entry
      x-permissions:
        - role: admin
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/UpdateDocumentRequest'
      responses:
        '200':
          description: Document updated
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/UpdateDocumentResponse'
        '404':
          description: Document not found

  /documentation/delete:
    post:
      operationId: DeleteDocument
      summary: Soft-delete documentation entry to trashcan
      x-permissions:
        - role: admin
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/DeleteDocumentRequest'
      responses:
        '200':
          description: Document moved to trashcan
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/DeleteDocumentResponse'
        '404':
          description: Document not found

  /documentation/recover:
    post:
      operationId: RecoverDocument
      summary: Recover document from trashcan
      x-permissions:
        - role: admin
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/RecoverDocumentRequest'
      responses:
        '200':
          description: Document recovered
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/RecoverDocumentResponse'
        '404':
          description: Trashcan entry not found or expired
        '409':
          description: Conflict with existing document

  /documentation/bulk-update:
    post:
      operationId: BulkUpdateDocuments
      summary: Bulk update document metadata
      x-permissions:
        - role: admin
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/BulkUpdateRequest'
      responses:
        '200':
          description: Bulk update results
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/BulkUpdateResponse'

  /documentation/bulk-delete:
    post:
      operationId: BulkDeleteDocuments
      summary: Bulk soft-delete documents to trashcan
      x-permissions:
        - role: admin
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/BulkDeleteRequest'
      responses:
        '200':
          description: Bulk delete results
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/BulkDeleteResponse'

  /documentation/import:
    post:
      operationId: ImportDocumentation
      summary: Bulk import documentation from structured source
      description: |
        Import multiple documents. Each document processed independently.
        Partial success is possible - failures reported per document.
      x-permissions:
        - role: admin
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ImportDocumentationRequest'
      responses:
        '200':
          description: Import results (may include partial failures)
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ImportDocumentationResponse'

  /documentation/trashcan:
    post:
      operationId: ListTrashcan
      summary: List documents in the trashcan
      x-permissions:
        - role: admin
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListTrashcanRequest'
      responses:
        '200':
          description: Trashcan contents
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ListTrashcanResponse'

  /documentation/purge:
    post:
      operationId: PurgeTrashcan
      summary: Permanently delete trashcan items
      x-permissions:
        - role: admin
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/PurgeTrashcanRequest'
      responses:
        '200':
          description: Purge results
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PurgeTrashcanResponse'

  /documentation/stats:
    post:
      operationId: GetNamespaceStats
      summary: Get namespace documentation statistics
      x-permissions:
        - role: admin
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetNamespaceStatsRequest'
      responses:
        '200':
          description: Namespace statistics
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/NamespaceStatsResponse'

components:
  schemas:
    # ===== Common Types =====
    Namespace:
      type: string
      pattern: '^[a-z0-9-]+$'
      maxLength: 50
      description: |
        Namespace for documentation isolation.
        Auto-created on first document. Examples: "bannou", "arcadia", "arcadia-omega"

    DocumentCategory:
      type: string
      enum:
        - getting-started
        - api-reference
        - architecture
        - deployment
        - troubleshooting
        - tutorials
        - game-systems
        - world-lore
        - npc-ai
        - other
      description: Fixed categories for type-safe filtering

    # ===== Query Endpoint =====
    QueryDocumentationRequest:
      type: object
      required:
        - namespace
        - query
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        query:
          type: string
          description: Natural language query
          minLength: 3
          maxLength: 500
        session_id:
          type: string
          format: uuid
          description: Optional session ID for conversational context (Redis TTL)
        category:
          $ref: '#/components/schemas/DocumentCategory'
        max_results:
          type: integer
          default: 5
          minimum: 1
          maximum: 20
        include_content:
          type: boolean
          default: false
        max_summary_length:
          type: integer
          default: 300
          minimum: 50
          maximum: 500
        min_relevance_score:
          type: number
          format: float
          default: 0.3
          minimum: 0.0
          maximum: 1.0

    QueryDocumentationResponse:
      type: object
      required:
        - results
        - namespace
        - query
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        results:
          type: array
          items:
            $ref: '#/components/schemas/DocumentResult'
        query:
          type: string
        total_results:
          type: integer
        voice_summary:
          type: string
          description: Concise spoken summary for voice AI
        suggested_followups:
          type: array
          items:
            type: string
        no_results_message:
          type: string

    # ===== Get Document Endpoint =====
    GetDocumentRequest:
      type: object
      required:
        - namespace
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        document_id:
          type: string
          format: uuid
        slug:
          type: string
          pattern: '^[a-z0-9-]+$'
        session_id:
          type: string
          format: uuid
        include_related:
          $ref: '#/components/schemas/RelatedDepth'
        include_content:
          type: boolean
          default: false
        render_html:
          type: boolean
          default: false

    GetDocumentResponse:
      type: object
      required:
        - document
      properties:
        document:
          $ref: '#/components/schemas/Document'
        related_documents:
          type: array
          items:
            $ref: '#/components/schemas/DocumentSummary'
        content_format:
          type: string
          enum:
            - markdown
            - html
            - none

    RelatedDepth:
      type: string
      enum:
        - none
        - direct
        - extended
      default: direct

    # ===== Search Endpoint =====
    SearchDocumentationRequest:
      type: object
      required:
        - namespace
        - search_term
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        search_term:
          type: string
          minLength: 2
          maxLength: 200
        session_id:
          type: string
          format: uuid
        category:
          $ref: '#/components/schemas/DocumentCategory'
        max_results:
          type: integer
          default: 10
          minimum: 1
          maximum: 50
        search_in:
          type: array
          items:
            $ref: '#/components/schemas/SearchField'
          default:
            - title
            - content
            - tags
        sort_by:
          type: string
          enum:
            - relevance
            - recency
            - alphabetical
          default: relevance
        include_content:
          type: boolean
          default: false

    SearchDocumentationResponse:
      type: object
      required:
        - namespace
        - results
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        results:
          type: array
          items:
            $ref: '#/components/schemas/DocumentResult'
        total_results:
          type: integer
        search_term:
          type: string

    SearchField:
      type: string
      enum:
        - title
        - content
        - tags
        - summary

    # ===== List Endpoint =====
    ListDocumentsRequest:
      type: object
      required:
        - namespace
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        category:
          $ref: '#/components/schemas/DocumentCategory'
        tags:
          type: array
          items:
            type: string
        tags_match:
          type: string
          enum:
            - all
            - any
          default: all
        created_after:
          type: string
          format: date-time
        created_before:
          type: string
          format: date-time
        updated_after:
          type: string
          format: date-time
        updated_before:
          type: string
          format: date-time
        titles_only:
          type: boolean
          default: false
        page:
          type: integer
          default: 1
          minimum: 1
        page_size:
          type: integer
          default: 20
          minimum: 1
          maximum: 100
        sort_by:
          $ref: '#/components/schemas/ListSortField'
        sort_order:
          type: string
          enum:
            - asc
            - desc
          default: desc

    ListDocumentsResponse:
      type: object
      required:
        - namespace
        - documents
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        documents:
          type: array
          items:
            $ref: '#/components/schemas/DocumentSummary'
        total_count:
          type: integer
        page:
          type: integer
        page_size:
          type: integer
        total_pages:
          type: integer

    ListSortField:
      type: string
      enum:
        - created_at
        - updated_at
        - title
      default: updated_at

    # ===== Suggest Endpoint =====
    SuggestRelatedRequest:
      type: object
      required:
        - namespace
        - suggestion_source
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        suggestion_source:
          $ref: '#/components/schemas/SuggestionSource'
        source_value:
          type: string
        session_id:
          type: string
          format: uuid
        max_suggestions:
          type: integer
          default: 5
          minimum: 1
          maximum: 10
        exclude_recently_viewed:
          type: boolean
          default: true

    SuggestRelatedResponse:
      type: object
      required:
        - namespace
        - suggestions
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        suggestions:
          type: array
          items:
            $ref: '#/components/schemas/TopicSuggestion'
        voice_prompt:
          type: string
        session_influenced:
          type: boolean

    SuggestionSource:
      type: string
      enum:
        - document_id
        - slug
        - topic
        - category

    TopicSuggestion:
      type: object
      required:
        - document_id
        - title
      properties:
        document_id:
          type: string
          format: uuid
        slug:
          type: string
        title:
          type: string
        category:
          $ref: '#/components/schemas/DocumentCategory'
        relevance_reason:
          type: string

    # ===== Admin: Create/Update/Delete =====
    CreateDocumentRequest:
      type: object
      required:
        - namespace
        - slug
        - title
        - category
        - content
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        slug:
          type: string
          pattern: '^[a-z0-9-]+$'
          maxLength: 100
        title:
          type: string
          maxLength: 200
        category:
          $ref: '#/components/schemas/DocumentCategory'
        content:
          type: string
          description: Markdown content (max 500KB)
        summary:
          type: string
          maxLength: 500
        voice_summary:
          type: string
          maxLength: 200
        tags:
          type: array
          items:
            type: string
        related_documents:
          type: array
          items:
            type: string
            format: uuid
        metadata:
          type: object
          additionalProperties: true

    CreateDocumentResponse:
      type: object
      required:
        - document_id
        - slug
      properties:
        document_id:
          type: string
          format: uuid
        slug:
          type: string
        created_at:
          type: string
          format: date-time

    UpdateDocumentRequest:
      type: object
      required:
        - namespace
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        document_id:
          type: string
          format: uuid
        slug:
          type: string
        title:
          type: string
        category:
          $ref: '#/components/schemas/DocumentCategory'
        content:
          type: string
        summary:
          type: string
        voice_summary:
          type: string
        tags:
          type: array
          items:
            type: string
        related_documents:
          type: array
          items:
            type: string
            format: uuid
        metadata:
          type: object
          additionalProperties: true

    UpdateDocumentResponse:
      type: object
      required:
        - document_id
        - updated_at
      properties:
        document_id:
          type: string
          format: uuid
        updated_at:
          type: string
          format: date-time

    DeleteDocumentRequest:
      type: object
      required:
        - namespace
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        document_id:
          type: string
          format: uuid
        slug:
          type: string

    DeleteDocumentResponse:
      type: object
      required:
        - document_id
        - deleted_at
        - recoverable_until
      properties:
        document_id:
          type: string
          format: uuid
        deleted_at:
          type: string
          format: date-time
        recoverable_until:
          type: string
          format: date-time

    # ===== Admin: Recover =====
    RecoverDocumentRequest:
      type: object
      required:
        - namespace
        - document_id
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        document_id:
          type: string
          format: uuid

    RecoverDocumentResponse:
      type: object
      required:
        - document_id
        - recovered_at
      properties:
        document_id:
          type: string
          format: uuid
        recovered_at:
          type: string
          format: date-time

    # ===== Admin: Bulk Operations =====
    BulkUpdateRequest:
      type: object
      required:
        - namespace
        - document_ids
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        document_ids:
          type: array
          items:
            type: string
            format: uuid
        category:
          $ref: '#/components/schemas/DocumentCategory'
        add_tags:
          type: array
          items:
            type: string
        remove_tags:
          type: array
          items:
            type: string

    BulkUpdateResponse:
      type: object
      required:
        - succeeded
        - failed
      properties:
        succeeded:
          type: array
          items:
            type: string
            format: uuid
        failed:
          type: array
          items:
            $ref: '#/components/schemas/BulkOperationFailure'

    BulkDeleteRequest:
      type: object
      required:
        - namespace
        - document_ids
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        document_ids:
          type: array
          items:
            type: string
            format: uuid

    BulkDeleteResponse:
      type: object
      required:
        - succeeded
        - failed
      properties:
        succeeded:
          type: array
          items:
            type: string
            format: uuid
        failed:
          type: array
          items:
            $ref: '#/components/schemas/BulkOperationFailure'

    BulkOperationFailure:
      type: object
      required:
        - document_id
        - error
      properties:
        document_id:
          type: string
          format: uuid
        error:
          type: string

    # ===== Admin: Import =====
    ImportDocumentationRequest:
      type: object
      required:
        - namespace
        - documents
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        documents:
          type: array
          items:
            $ref: '#/components/schemas/ImportDocument'
        on_conflict:
          type: string
          enum:
            - skip
            - update
            - fail
          default: skip

    ImportDocument:
      type: object
      required:
        - slug
        - title
        - category
        - content
      properties:
        slug:
          type: string
        title:
          type: string
        category:
          $ref: '#/components/schemas/DocumentCategory'
        content:
          type: string
        summary:
          type: string
        voice_summary:
          type: string
        tags:
          type: array
          items:
            type: string
        metadata:
          type: object
          additionalProperties: true

    ImportDocumentationResponse:
      type: object
      required:
        - namespace
        - created
        - updated
        - skipped
        - failed
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        created:
          type: integer
        updated:
          type: integer
        skipped:
          type: integer
        failed:
          type: array
          items:
            $ref: '#/components/schemas/ImportFailure'

    ImportFailure:
      type: object
      required:
        - slug
        - error
      properties:
        slug:
          type: string
        error:
          type: string

    # ===== Admin: Trashcan =====
    ListTrashcanRequest:
      type: object
      required:
        - namespace
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        page:
          type: integer
          default: 1
        page_size:
          type: integer
          default: 20

    ListTrashcanResponse:
      type: object
      required:
        - namespace
        - items
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        items:
          type: array
          items:
            $ref: '#/components/schemas/TrashcanItem'
        total_count:
          type: integer

    TrashcanItem:
      type: object
      required:
        - document_id
        - title
        - deleted_at
        - expires_at
      properties:
        document_id:
          type: string
          format: uuid
        slug:
          type: string
        title:
          type: string
        category:
          $ref: '#/components/schemas/DocumentCategory'
        deleted_at:
          type: string
          format: date-time
        expires_at:
          type: string
          format: date-time

    PurgeTrashcanRequest:
      type: object
      required:
        - namespace
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        document_ids:
          type: array
          items:
            type: string
            format: uuid
          description: If empty, purges all trashcan items

    PurgeTrashcanResponse:
      type: object
      required:
        - purged_count
      properties:
        purged_count:
          type: integer

    # ===== Admin: Stats =====
    GetNamespaceStatsRequest:
      type: object
      required:
        - namespace
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'

    NamespaceStatsResponse:
      type: object
      required:
        - namespace
        - document_count
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        document_count:
          type: integer
        category_counts:
          type: object
          additionalProperties:
            type: integer
        trashcan_count:
          type: integer
        total_content_size_bytes:
          type: integer
        last_updated:
          type: string
          format: date-time

    # ===== Core Document Types =====
    Document:
      type: object
      required:
        - document_id
        - namespace
        - slug
        - title
        - category
        - created_at
        - updated_at
      properties:
        document_id:
          type: string
          format: uuid
        namespace:
          $ref: '#/components/schemas/Namespace'
        slug:
          type: string
        title:
          type: string
        category:
          $ref: '#/components/schemas/DocumentCategory'
        content:
          type: string
        summary:
          type: string
        voice_summary:
          type: string
        tags:
          type: array
          items:
            type: string
        related_documents:
          type: array
          items:
            type: string
            format: uuid
        metadata:
          type: object
          additionalProperties: true
        created_at:
          type: string
          format: date-time
        updated_at:
          type: string
          format: date-time

    DocumentSummary:
      type: object
      required:
        - document_id
        - slug
        - title
        - category
      properties:
        document_id:
          type: string
          format: uuid
        slug:
          type: string
        title:
          type: string
        category:
          $ref: '#/components/schemas/DocumentCategory'
        summary:
          type: string
        voice_summary:
          type: string
        tags:
          type: array
          items:
            type: string

    DocumentResult:
      type: object
      required:
        - document_id
        - slug
        - title
        - relevance_score
      properties:
        document_id:
          type: string
          format: uuid
        slug:
          type: string
        title:
          type: string
        category:
          $ref: '#/components/schemas/DocumentCategory'
        summary:
          type: string
        voice_summary:
          type: string
        content:
          type: string
        relevance_score:
          type: number
          format: float
        match_highlights:
          type: array
          items:
            type: string
```

---

## File Structure

```
lib-documentation/
├── Generated/                              # NEVER EDIT - auto-generated
│   ├── DocumentationController.Generated.cs
│   ├── IDocumentationService.cs
│   ├── DocumentationModels.cs
│   ├── DocumentationClient.cs
│   ├── DocumentationServiceConfiguration.cs
│   ├── DocumentationPermissionRegistration.cs
│   └── DocumentationEventsController.cs
├── DocumentationService.cs                 # Main implementation (partial class)
├── DocumentationServiceEvents.cs           # Event handlers (partial class) - REQUIRED
├── DocumentationServicePlugin.cs           # Plugin wrapper
├── Services/                               # Helper services
│   ├── ISearchIndexService.cs              # Search abstraction interface
│   └── SearchIndexService.cs               # In-memory search (ConcurrentDictionary)
└── lib-documentation.csproj

lib-documentation.tests/
├── DocumentationServiceTests.cs            # Unit tests with mocked dependencies
├── SearchIndexServiceTests.cs              # Search service unit tests
├── GlobalUsings.cs
└── GlobalSuppressions.cs

http-tester/Tests/
└── DocumentationHandlerTests.cs            # HTTP integration tests

edge-tester/Tests/
└── DocumentationEdgeTests.cs               # WebSocket protocol tests
```

---

## Service Implementation Pattern

**CRITICAL**: Service MUST use `partial class` and include all required dependencies per TENETS.

### DocumentationService.cs

```csharp
using BeyondImmersion.BannouService;  // For StatusCodes enum

namespace BeyondImmersion.Bannou.Documentation;

/// <summary>
/// Documentation service for knowledge base storage and retrieval.
/// Provides AI agent integration with voice-optimized responses.
/// </summary>
[DaprService("documentation", typeof(IDocumentationService), lifetime: ServiceLifetime.Scoped)]
public partial class DocumentationService : IDocumentationService  // MUST be partial (Tenet 6)
{
    private const string STATE_STORE = "documentation-statestore";
    private const string PUBSUB_NAME = "bannou-pubsub";

    // Required dependencies (Tenet 6)
    private readonly DaprClient _daprClient;
    private readonly ILogger<DocumentationService> _logger;
    private readonly DocumentationServiceConfiguration _configuration;
    private readonly IErrorEventEmitter _errorEventEmitter;  // REQUIRED (Tenet 7)

    // Helper services
    private readonly ISearchIndexService _searchIndexService;

    /// <summary>
    /// Initializes the Documentation service with required dependencies.
    /// </summary>
    public DocumentationService(
        DaprClient daprClient,
        ILogger<DocumentationService> logger,
        DocumentationServiceConfiguration configuration,
        IErrorEventEmitter errorEventEmitter,
        IEventConsumer eventConsumer,  // REQUIRED for event registration (Tenet 3)
        ISearchIndexService searchIndexService)
    {
        // Proper null checks - no null-forgiving operators! (TENETS rule)
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _errorEventEmitter = errorEventEmitter ?? throw new ArgumentNullException(nameof(errorEventEmitter));
        _searchIndexService = searchIndexService ?? throw new ArgumentNullException(nameof(searchIndexService));

        // Register event handlers (Tenet 3)
        ArgumentNullException.ThrowIfNull(eventConsumer, nameof(eventConsumer));
        RegisterEventConsumers(eventConsumer);
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, QueryDocumentationResponse?)> QueryDocumentationAsync(
        QueryDocumentationRequest body,
        CancellationToken ct = default)
    {
        try
        {
            // Structured logging (Tenet 10) - message template, not interpolation
            _logger.LogDebug("Querying documentation in {Namespace}: {Query}", body.Namespace, body.Query);

            var results = await _searchIndexService.QueryAsync(
                body.Namespace,
                body.Query,
                body.Category,
                body.MaxResults ?? _configuration.MaxSearchResults,
                body.MinRelevanceScore ?? _configuration.MinRelevanceScore,
                ct);

            var response = new QueryDocumentationResponse
            {
                Namespace = body.Namespace,
                Query = body.Query,
                Results = results,
                TotalResults = results.Count
            };

            // Generate voice summary if results exist
            if (results.Count > 0)
            {
                response.VoiceSummary = GenerateVoiceSummary(results.First());
            }
            else
            {
                response.NoResultsMessage = $"I couldn't find documentation about '{body.Query}'. Try different keywords?";
            }

            // Publish analytics event
            await PublishQueryEventAsync(body, response, ct);

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query documentation in {Namespace}", body.Namespace);

            // REQUIRED: Emit error event (Tenet 7)
            await _errorEventEmitter.TryPublishAsync(
                serviceId: "documentation",
                operation: "QueryDocumentation",
                errorType: ex.GetType().Name,
                message: ex.Message,
                stack: ex.StackTrace,
                cancellationToken: ct);

            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, GetDocumentResponse?)> GetDocumentAsync(
        GetDocumentRequest body,
        CancellationToken ct = default)
    {
        try
        {
            // Validate request - need either document_id or slug
            if (string.IsNullOrEmpty(body.DocumentId) && string.IsNullOrEmpty(body.Slug))
            {
                return (StatusCodes.BadRequest, null);
            }

            Document? document;

            if (!string.IsNullOrEmpty(body.DocumentId))
            {
                // Get by document ID
                var key = $"doc:{body.Namespace}:{body.DocumentId}";
                document = await _daprClient.GetStateAsync<Document>(STATE_STORE, key, cancellationToken: ct);
            }
            else
            {
                // Get by slug - lookup the document ID first
                var slugKey = $"slug-idx:{body.Namespace}:{body.Slug}";
                var documentId = await _daprClient.GetStateAsync<string>(STATE_STORE, slugKey, cancellationToken: ct);

                if (string.IsNullOrEmpty(documentId))
                {
                    return (StatusCodes.NotFound, null);
                }

                var key = $"doc:{body.Namespace}:{documentId}";
                document = await _daprClient.GetStateAsync<Document>(STATE_STORE, key, cancellationToken: ct);
            }

            if (document == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var response = new GetDocumentResponse
            {
                Document = document,
                ContentFormat = body.IncludeContent == true
                    ? (body.RenderHtml == true ? "html" : "markdown")
                    : "none"
            };

            // Strip content if not requested
            if (body.IncludeContent != true)
            {
                response.Document.Content = null;
            }

            // Publish view event
            await PublishViewEventAsync(body.Namespace, document.DocumentId, body.SessionId, "direct", ct);

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get document in {Namespace}", body.Namespace);

            await _errorEventEmitter.TryPublishAsync(
                serviceId: "documentation",
                operation: "GetDocument",
                errorType: ex.GetType().Name,
                message: ex.Message,
                stack: ex.StackTrace,
                cancellationToken: ct);

            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, CreateDocumentResponse?)> CreateDocumentAsync(
        CreateDocumentRequest body,
        CancellationToken ct = default)
    {
        try
        {
            // Check for slug conflict
            var slugKey = $"slug-idx:{body.Namespace}:{body.Slug}";
            var existingId = await _daprClient.GetStateAsync<string>(STATE_STORE, slugKey, cancellationToken: ct);

            if (!string.IsNullOrEmpty(existingId))
            {
                return (StatusCodes.Conflict, null);
            }

            // Validate content size
            if (body.Content?.Length > _configuration.MaxContentSizeBytes)
            {
                return (StatusCodes.BadRequest, null);
            }

            var documentId = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow;

            var document = new Document
            {
                DocumentId = documentId,
                Namespace = body.Namespace,
                Slug = body.Slug,
                Title = body.Title,
                Category = body.Category,
                Content = body.Content,
                Summary = body.Summary,
                VoiceSummary = body.VoiceSummary,
                Tags = body.Tags ?? new List<string>(),
                RelatedDocuments = body.RelatedDocuments ?? new List<string>(),
                Metadata = body.Metadata,
                CreatedAt = now,
                UpdatedAt = now
            };

            // Save document and slug index atomically
            var documentKey = $"doc:{body.Namespace}:{documentId}";

            await _daprClient.SaveStateAsync(STATE_STORE, documentKey, document, cancellationToken: ct);
            await _daprClient.SaveStateAsync(STATE_STORE, slugKey, documentId, cancellationToken: ct);

            // Update search index
            await _searchIndexService.IndexDocumentAsync(document, ct);

            // Publish created event
            await PublishDocumentCreatedEventAsync(document, ct);

            return (StatusCodes.OK, new CreateDocumentResponse
            {
                DocumentId = documentId,
                Slug = body.Slug,
                CreatedAt = now
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create document in {Namespace}", body.Namespace);

            await _errorEventEmitter.TryPublishAsync(
                serviceId: "documentation",
                operation: "CreateDocument",
                errorType: ex.GetType().Name,
                message: ex.Message,
                stack: ex.StackTrace,
                cancellationToken: ct);

            return (StatusCodes.InternalServerError, null);
        }
    }

    // Additional methods follow same pattern...
    // Each method MUST:
    // 1. Use (StatusCodes, TResponse?) return tuple (Tenet 8)
    // 2. Use structured logging (Tenet 10)
    // 3. Call _errorEventEmitter.TryPublishAsync in catch blocks (Tenet 7)
    // 4. Never use null-forgiving operators

    private string GenerateVoiceSummary(DocumentResult result)
    {
        if (!string.IsNullOrEmpty(result.VoiceSummary))
        {
            return result.VoiceSummary;
        }

        // Truncate summary to voice-friendly length
        var summary = result.Summary ?? result.Title;
        if (summary.Length > _configuration.VoiceSummaryMaxLength)
        {
            summary = summary[.._configuration.VoiceSummaryMaxLength] + "...";
        }

        return summary;
    }

    private async Task PublishQueryEventAsync(
        QueryDocumentationRequest request,
        QueryDocumentationResponse response,
        CancellationToken ct)
    {
        var queryEvent = new DocumentationQueriedEvent
        {
            EventId = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow,
            Namespace = request.Namespace,
            Query = request.Query,
            SessionId = request.SessionId,
            ResultCount = response.TotalResults ?? 0,
            TopResultId = response.Results?.FirstOrDefault()?.DocumentId,
            RelevanceScore = response.Results?.FirstOrDefault()?.RelevanceScore
        };

        await _daprClient.PublishEventAsync(PUBSUB_NAME, "documentation.queried", queryEvent, ct);
    }

    private async Task PublishViewEventAsync(
        string namespaceId,
        string documentId,
        string? sessionId,
        string source,
        CancellationToken ct)
    {
        var viewEvent = new DocumentViewedEvent
        {
            EventId = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow,
            Namespace = namespaceId,
            DocumentId = documentId,
            SessionId = sessionId,
            Source = source
        };

        await _daprClient.PublishEventAsync(PUBSUB_NAME, "documentation.viewed", viewEvent, ct);
    }

    private async Task PublishDocumentCreatedEventAsync(Document document, CancellationToken ct)
    {
        var createdEvent = new DocumentCreatedEvent
        {
            EventId = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow,
            DocumentId = document.DocumentId,
            Namespace = document.Namespace,
            Slug = document.Slug,
            Title = document.Title,
            Category = document.Category,
            Tags = document.Tags,
            CreatedAt = document.CreatedAt,
            UpdatedAt = document.UpdatedAt
        };

        await _daprClient.PublishEventAsync(PUBSUB_NAME, "document.created", createdEvent, ct);
    }
}
```

### DocumentationServiceEvents.cs

**REQUIRED** partial class file for event handlers (Tenet 6).

```csharp
namespace BeyondImmersion.Bannou.Documentation;

/// <summary>
/// Event handlers for Documentation service (partial class).
/// </summary>
public partial class DocumentationService
{
    /// <summary>
    /// Registers event consumers for this service.
    /// Called from constructor to enable event fan-out.
    /// </summary>
    /// <remarks>
    /// Minimal event subscriptions per design decision.
    /// Sessions use Redis TTL cleanup - no external event consumption needed.
    /// </remarks>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        // Minimal event subscriptions per design decision
        // Sessions use TTL cleanup - no external event consumption needed
        _logger.LogDebug("Documentation service event consumers registered (minimal mode)");
    }
}
```

---

## Search Index Service

Thread-safe in-memory index using `ConcurrentDictionary` per Tenet 9.

### ISearchIndexService.cs

```csharp
namespace BeyondImmersion.Bannou.Documentation.Services;

/// <summary>
/// Interface for documentation search index operations.
/// </summary>
public interface ISearchIndexService
{
    /// <summary>
    /// Queries the search index with natural language.
    /// </summary>
    Task<List<DocumentResult>> QueryAsync(
        string namespaceId,
        string query,
        string? category,
        int maxResults,
        float minRelevanceScore,
        CancellationToken ct = default);

    /// <summary>
    /// Searches the index with keyword matching.
    /// </summary>
    Task<List<DocumentResult>> SearchAsync(
        string namespaceId,
        string searchTerm,
        string? category,
        int maxResults,
        CancellationToken ct = default);

    /// <summary>
    /// Indexes a document for search.
    /// </summary>
    Task IndexDocumentAsync(Document document, CancellationToken ct = default);

    /// <summary>
    /// Removes a document from the index.
    /// </summary>
    Task RemoveDocumentAsync(string namespaceId, string documentId, CancellationToken ct = default);

    /// <summary>
    /// Rebuilds the search index for a namespace from state store.
    /// </summary>
    Task RebuildIndexAsync(string namespaceId, CancellationToken ct = default);
}
```

### SearchIndexService.cs

```csharp
using System.Collections.Concurrent;

namespace BeyondImmersion.Bannou.Documentation.Services;

/// <summary>
/// In-memory search index with thread-safe operations.
/// Rebuilt on startup from Dapr state store.
/// </summary>
public class SearchIndexService : ISearchIndexService
{
    // Thread-safe for multi-instance deployment (Tenet 9)
    private readonly ConcurrentDictionary<string, NamespaceIndex> _indices = new();

    private readonly DaprClient _daprClient;
    private readonly ILogger<SearchIndexService> _logger;
    private readonly DocumentationServiceConfiguration _configuration;

    private const string STATE_STORE = "documentation-statestore";

    public SearchIndexService(
        DaprClient daprClient,
        ILogger<SearchIndexService> logger,
        DocumentationServiceConfiguration configuration)
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public async Task<List<DocumentResult>> QueryAsync(
        string namespaceId,
        string query,
        string? category,
        int maxResults,
        float minRelevanceScore,
        CancellationToken ct = default)
    {
        var index = _indices.GetOrAdd(namespaceId, _ => new NamespaceIndex());

        // Simple TF-IDF style scoring
        var terms = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var results = new List<(DocumentEntry doc, float score)>();

        foreach (var doc in index.Documents.Values)
        {
            if (category != null && doc.Category != category)
                continue;

            var score = CalculateRelevanceScore(doc, terms);
            if (score >= minRelevanceScore)
            {
                results.Add((doc, score));
            }
        }

        return results
            .OrderByDescending(r => r.score)
            .Take(maxResults)
            .Select(r => new DocumentResult
            {
                DocumentId = r.doc.DocumentId,
                Slug = r.doc.Slug,
                Title = r.doc.Title,
                Category = r.doc.Category,
                Summary = r.doc.Summary,
                VoiceSummary = r.doc.VoiceSummary,
                RelevanceScore = r.score
            })
            .ToList();
    }

    public async Task<List<DocumentResult>> SearchAsync(
        string namespaceId,
        string searchTerm,
        string? category,
        int maxResults,
        CancellationToken ct = default)
    {
        var index = _indices.GetOrAdd(namespaceId, _ => new NamespaceIndex());
        var termLower = searchTerm.ToLowerInvariant();

        var results = index.Documents.Values
            .Where(d => (category == null || d.Category == category) &&
                       (d.Title.Contains(termLower, StringComparison.OrdinalIgnoreCase) ||
                        d.Content.Contains(termLower, StringComparison.OrdinalIgnoreCase) ||
                        d.Tags.Any(t => t.Contains(termLower, StringComparison.OrdinalIgnoreCase))))
            .Take(maxResults)
            .Select(d => new DocumentResult
            {
                DocumentId = d.DocumentId,
                Slug = d.Slug,
                Title = d.Title,
                Category = d.Category,
                Summary = d.Summary,
                VoiceSummary = d.VoiceSummary,
                RelevanceScore = 1.0f
            })
            .ToList();

        return results;
    }

    public Task IndexDocumentAsync(Document document, CancellationToken ct = default)
    {
        var index = _indices.GetOrAdd(document.Namespace, _ => new NamespaceIndex());

        var entry = new DocumentEntry
        {
            DocumentId = document.DocumentId,
            Slug = document.Slug,
            Title = document.Title,
            Category = document.Category,
            Content = document.Content ?? string.Empty,
            Summary = document.Summary ?? string.Empty,
            VoiceSummary = document.VoiceSummary ?? string.Empty,
            Tags = document.Tags ?? new List<string>(),
            UpdatedAt = document.UpdatedAt
        };

        index.Documents[document.DocumentId] = entry;

        _logger.LogDebug("Indexed document {DocumentId} in namespace {Namespace}",
            document.DocumentId, document.Namespace);

        return Task.CompletedTask;
    }

    public Task RemoveDocumentAsync(string namespaceId, string documentId, CancellationToken ct = default)
    {
        if (_indices.TryGetValue(namespaceId, out var index))
        {
            index.Documents.TryRemove(documentId, out _);
            _logger.LogDebug("Removed document {DocumentId} from index", documentId);
        }

        return Task.CompletedTask;
    }

    public async Task RebuildIndexAsync(string namespaceId, CancellationToken ct = default)
    {
        _logger.LogInformation("Rebuilding search index for namespace {Namespace}", namespaceId);

        var newIndex = new NamespaceIndex();

        // Load all documents from state store
        // Note: This requires Dapr query capability or maintaining a document list
        // For now, we load from a namespace document list key
        var docListKey = $"ns-docs:{namespaceId}";
        var docIds = await _daprClient.GetStateAsync<List<string>>(STATE_STORE, docListKey, cancellationToken: ct);

        if (docIds != null)
        {
            foreach (var docId in docIds)
            {
                var docKey = $"doc:{namespaceId}:{docId}";
                var document = await _daprClient.GetStateAsync<Document>(STATE_STORE, docKey, cancellationToken: ct);

                if (document != null)
                {
                    var entry = new DocumentEntry
                    {
                        DocumentId = document.DocumentId,
                        Slug = document.Slug,
                        Title = document.Title,
                        Category = document.Category,
                        Content = document.Content ?? string.Empty,
                        Summary = document.Summary ?? string.Empty,
                        VoiceSummary = document.VoiceSummary ?? string.Empty,
                        Tags = document.Tags ?? new List<string>(),
                        UpdatedAt = document.UpdatedAt
                    };

                    newIndex.Documents[document.DocumentId] = entry;
                }
            }
        }

        _indices[namespaceId] = newIndex;

        _logger.LogInformation("Search index rebuilt for {Namespace}: {DocumentCount} documents",
            namespaceId, newIndex.Documents.Count);
    }

    private float CalculateRelevanceScore(DocumentEntry doc, string[] terms)
    {
        float score = 0;

        foreach (var term in terms)
        {
            // Title matches weight more
            if (doc.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += 3.0f;

            // Tag matches weight moderately
            if (doc.Tags.Any(t => t.Contains(term, StringComparison.OrdinalIgnoreCase)))
                score += 2.0f;

            // Summary matches
            if (doc.Summary.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += 1.5f;

            // Content matches
            if (doc.Content.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += 1.0f;
        }

        // Normalize score to 0-1 range
        return Math.Min(score / (terms.Length * 4), 1.0f);
    }

    private class NamespaceIndex
    {
        public ConcurrentDictionary<string, DocumentEntry> Documents { get; } = new();
    }

    private class DocumentEntry
    {
        public string DocumentId { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string VoiceSummary { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new();
        public DateTime UpdatedAt { get; set; }
    }
}
```

---

## State Store Key Patterns

```csharp
// Document storage
$"doc:{namespace}:{documentId}"           // Full document data
$"slug-idx:{namespace}:{slug}"            // Slug → DocumentId mapping
$"ns-docs:{namespace}"                    // List of all doc IDs in namespace

// Session tracking (informal, with TTL)
$"session:{sessionId}:viewed"             // List of recently viewed doc IDs
$"session:{sessionId}:queries"            // Recent query hashes

// Trashcan
$"trash:{namespace}:{documentId}"         // Soft-deleted document
```

---

## Implementation Order

### Step 1: Create Schema Files
1. Create `schemas/documentation-configuration.yaml`
2. Update `schemas/documentation-api.yaml` (add browser endpoint, verify x-permissions)
3. Update `schemas/documentation-events.yaml` (add x-lifecycle, x-event-subscriptions)

### Step 2: Run Code Generation
```bash
scripts/generate-all-services.sh
make build
```

Verify:
- All generated files compile
- No duplicate type definitions
- Configuration class has proper env var bindings

### Step 3: Create Service Files
1. `lib-documentation/DocumentationService.cs` (partial class)
2. `lib-documentation/DocumentationServiceEvents.cs` (partial class)
3. `lib-documentation/DocumentationServicePlugin.cs`
4. `lib-documentation/Services/ISearchIndexService.cs`
5. `lib-documentation/Services/SearchIndexService.cs`

### Step 4: Implement Business Logic
1. CRUD operations (Create, Get, Update, Delete)
2. Search operations (Query, Search, List, Suggest)
3. Session tracking (Redis key-value with TTL)
4. Bulk operations (Import, BulkUpdate, BulkDelete)
5. Trashcan operations (ListTrashcan, Recover, Purge)
6. Browser endpoint (ViewDocumentBySlug)

### Step 5: Implement Tests
1. Unit Tests (`lib-documentation.tests/`) - Mocked dependencies
2. HTTP Tests (`http-tester/Tests/`) - Service calls via Dapr
3. Edge Tests (`edge-tester/Tests/`) - WebSocket protocol

---

## TENETS Compliance Checklist

### Schema Compliance
- [ ] Server URL uses `http://localhost:3500/v1.0/invoke/bannou/method`
- [ ] All POST endpoints have `x-permissions` declared
- [ ] Browser endpoint (GET) has NO x-permissions (Tenet 15)
- [ ] `documentation-configuration.yaml` exists with `DOCUMENTATION_` env vars
- [ ] `x-lifecycle` used for Document CRUD events
- [ ] `x-event-subscriptions: []` declared (minimal)

### Service Implementation Compliance
- [ ] Service class declared as `partial class`
- [ ] `DocumentationServiceEvents.cs` file exists
- [ ] Constructor includes `IEventConsumer` parameter
- [ ] Constructor includes `IErrorEventEmitter` parameter
- [ ] `RegisterEventConsumers()` called in constructor
- [ ] All catch blocks call `_errorEventEmitter.TryPublishAsync()`
- [ ] Uses `BeyondImmersion.BannouService.StatusCodes` enum
- [ ] Proper null checks (no null-forgiving operators)

### Multi-Instance Safety
- [ ] SearchIndexService uses `ConcurrentDictionary`
- [ ] No plain `Dictionary<>` for caches
- [ ] Session tracking uses Redis with TTL

### Testing Requirements
- [ ] Unit tests documented (`lib-documentation.tests/`)
- [ ] HTTP tests documented (`http-tester/Tests/`)
- [ ] Edge tests documented (`edge-tester/Tests/`)
- [ ] Test naming follows Osherove standard

### Documentation Quality
- [ ] XML documentation on all public classes/methods
- [ ] Structured logging (no string interpolation)
- [ ] All design decisions documented

---

## AI Agent Integration Examples

### SignalWire SWAIG Integration

```json
{
  "function": "query_documentation",
  "web_hook_url": "https://api.example.com/documentation/query",
  "purpose": "Search technical documentation to answer user questions about the platform",
  "argument": {
    "type": "object",
    "properties": {
      "namespace": {
        "type": "string",
        "description": "Documentation namespace",
        "default": "bannou"
      },
      "query": {
        "type": "string",
        "description": "The user's question in natural language"
      }
    },
    "required": ["query"]
  }
}
```

### OpenAI Function Calling

```json
{
  "type": "function",
  "function": {
    "name": "query_documentation",
    "description": "Search technical documentation for answers to user questions",
    "parameters": {
      "type": "object",
      "properties": {
        "namespace": {
          "type": "string",
          "description": "Documentation namespace (default: bannou)"
        },
        "query": {
          "type": "string",
          "description": "Natural language search query"
        },
        "category": {
          "type": "string",
          "enum": ["getting-started", "api-reference", "architecture", "deployment", "troubleshooting"],
          "description": "Optional category filter"
        }
      },
      "required": ["query"]
    }
  }
}
```

### Claude Tool Use

```json
{
  "name": "query_documentation",
  "description": "Search technical documentation about the Bannou framework and Arcadia game systems",
  "input_schema": {
    "type": "object",
    "properties": {
      "namespace": {
        "type": "string",
        "description": "Documentation namespace (default: bannou)"
      },
      "query": {
        "type": "string",
        "description": "Natural language question or topic to search for"
      }
    },
    "required": ["query"]
  }
}
```

---

## Appendix: Environment Variables

| Variable | Type | Default | Description |
|----------|------|---------|-------------|
| `DOCUMENTATION_SEARCH_INDEX_REBUILD_ON_STARTUP` | bool | true | Rebuild index on startup |
| `DOCUMENTATION_SESSION_TTL_SECONDS` | int | 86400 | Session TTL (24h) |
| `DOCUMENTATION_MAX_CONTENT_SIZE_BYTES` | int | 524288 | Max content size (500KB) |
| `DOCUMENTATION_TRASHCAN_TTL_DAYS` | int | 7 | Trashcan retention |
| `DOCUMENTATION_VOICE_SUMMARY_MAX_LENGTH` | int | 200 | Voice summary max length |
| `DOCUMENTATION_SEARCH_CACHE_TTL_SECONDS` | int | 300 | Search cache TTL |
| `DOCUMENTATION_MIN_RELEVANCE_SCORE` | float | 0.3 | Default min relevance |
| `DOCUMENTATION_MAX_SEARCH_RESULTS` | int | 20 | Max search results |
| `DOCUMENTATION_MAX_IMPORT_DOCUMENTS` | int | 0 | Max import docs (0=unlimited) |
| `DOCUMENTATION_AI_ENHANCEMENTS_ENABLED` | bool | false | AI features enabled |
| `DOCUMENTATION_AI_EMBEDDINGS_MODEL` | string | "" | Embeddings model |
