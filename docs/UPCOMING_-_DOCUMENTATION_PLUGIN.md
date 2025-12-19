# Documentation Plugin Design Document

> **Status**: Draft
> **Version**: 1.0.0
> **Last Updated**: 2025-12-18
> **Target Service**: `lib-documentation`

## Executive Summary

The Documentation Plugin (`lib-documentation`) provides a comprehensive knowledge base API for storing, retrieving, and querying documentation about the Bannou framework, Arcadia game systems, services, deployment, and operational guides. The primary use case is enabling AI agents (via SignalWire SWAIG, OpenAI function calling, or Claude tool use) to retrieve accurate, contextual information during voice or chat conversations.

### Key Design Goals

1. **AI Agent Compatibility**: Simple, well-documented REST API that works with any AI agent tool calling mechanism
2. **Voice-Optimized Responses**: Return concise, speakable summaries alongside detailed content
3. **Schema-First Development**: Full compliance with Bannou's contract-first architecture
4. **Semantic Search**: Support natural language queries beyond simple keyword matching
5. **Multi-Tenant Ready**: Support documentation for multiple realms, services, and contexts
6. **Real-Time Updates**: Event-driven architecture for documentation synchronization

---

## Architecture Overview

### Plugin Structure

```
lib-documentation/
├── Generated/                              # NSwag auto-generated (never edit)
│   ├── DocumentationController.Generated.cs
│   ├── IDocumentationService.cs
│   ├── DocumentationModels.cs
│   ├── DocumentationClient.cs
│   └── DocumentationServiceConfiguration.cs
├── DocumentationService.cs                 # Business logic (only manual file)
├── DocumentationServicePlugin.cs           # Plugin registration
├── Services/                               # Helper services
│   ├── ISearchService.cs                   # Search abstraction
│   └── SearchService.cs                    # Full-text search implementation
└── lib-documentation.csproj

schemas/
├── documentation-api.yaml                  # OpenAPI specification
└── documentation-events.yaml               # Event schemas

lib-documentation.tests/
└── DocumentationServiceTests.cs            # Unit tests
```

### Integration Points

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           AI Agent Systems                               │
├─────────────────┬─────────────────┬─────────────────────────────────────┤
│  SignalWire     │    OpenAI       │         Claude                       │
│  SWAIG          │  Function       │       Tool Use                       │
│  web_hook_url   │   Calling       │                                      │
└────────┬────────┴────────┬────────┴──────────────┬──────────────────────┘
         │                 │                        │
         │    HTTP POST    │     HTTP POST          │    HTTP POST
         │  (JSON payload) │   (JSON payload)       │  (JSON payload)
         ▼                 ▼                        ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                    Bannou Documentation Service                          │
│  ┌─────────────────────────────────────────────────────────────────────┐│
│  │  POST /documentation/query     - Natural language search            ││
│  │  POST /documentation/get       - Get specific document by ID        ││
│  │  POST /documentation/search    - Full-text keyword search           ││
│  │  POST /documentation/list      - List documents by category         ││
│  │  POST /documentation/suggest   - Get related topics                 ││
│  └─────────────────────────────────────────────────────────────────────┘│
│                                                                          │
│  ┌─────────────────────────────────┐  ┌────────────────────────────┐   │
│  │ Search Service                  │  │ Dapr State Store           │   │
│  │ (Full-text with ranking)        │  │ (Redis + MySQL)            │   │
│  └─────────────────────────────────┘  └────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## OpenAPI Schema Design

### POST-Only API Pattern

All endpoints use POST with request bodies (Bannou GUID-based routing requirement):

```yaml
# schemas/documentation-api.yaml
openapi: 3.0.0
info:
  title: Bannou Documentation API
  version: 1.0.0
  description: |
    Knowledge base API for AI agents to query documentation about
    the Bannou framework, Arcadia game systems, and operational guides.

    Designed for integration with:
    - SignalWire SWAIG (voice AI agents)
    - OpenAI function calling
    - Claude tool use

    All endpoints return voice-friendly summaries alongside detailed content.

servers:
  - url: http://localhost:3500/v1.0/invoke/bannou/method
    description: Dapr sidecar endpoint (internal only)

paths:
  /documentation/query:
    post:
      operationId: QueryDocumentation
      summary: Natural language documentation search
      description: |
        Search documentation using natural language queries. Returns the most
        relevant documents with voice-friendly summaries for AI agent responses.

        Use this endpoint when:
        - User asks "how do I..." or "what is..."
        - User needs technical information explained
        - User requests documentation on a topic
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
      summary: Get specific document by ID
      description: |
        Retrieve a specific document by its unique identifier.
        Returns full content with metadata.
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
      description: |
        Search documentation using exact keyword matching.
        Faster than semantic search but less flexible.
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
      description: |
        List all documents in a specific category or all categories.
        Supports pagination for large result sets.
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
      description: |
        Given a topic or document ID, returns related topics the user
        might want to explore. Useful for conversational AI flow.
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

  # Admin endpoints for documentation management
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
      description: |
        Moves document to trashcan for recovery within TTL period.
        Previous versions and trashcan entries are automatically cleaned up
        based on TrashcanConfiguration settings.
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
      description: |
        Restores a soft-deleted document from the trashcan.
        Must be called before the trashcan TTL expires.
        If original ID/slug conflicts with existing document, recovery fails.
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
          description: Conflict with existing document ID or slug

  /documentation/bulk-update:
    post:
      operationId: BulkUpdateDocuments
      summary: Bulk update document metadata
      description: |
        Apply category, tag, or metadata changes to multiple documents at once.
        Useful for reorganizing documentation structure.
        Each document is processed independently - partial success is possible.
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
      description: |
        Move multiple documents to trashcan at once.
        All documents are processed independently - partial success is possible.
        Each deleted document can be individually recovered within TTL.
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
      summary: Bulk import documentation from markdown files
      description: |
        Import multiple documents from a structured source.
        Useful for syncing documentation from repositories.
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
          description: Import results
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ImportDocumentationResponse'

  /documentation/trashcan:
    post:
      operationId: ListTrashcan
      summary: List documents in the trashcan
      description: |
        List all soft-deleted documents within the namespace's trashcan.
        Documents remain recoverable until TTL expires or purge is called.
        Returns documents sorted by deletion time (most recent first).
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
      description: |
        Permanently delete specified documents from trashcan, or purge all.
        This operation is irreversible - documents cannot be recovered after purge.
        If document_ids is empty, purges all documents in namespace trashcan.
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
      description: |
        Retrieve usage statistics and metadata for a documentation namespace.
        Useful for monitoring, capacity planning, and administrative dashboards.
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
    # ===== Core Concepts =====
    Namespace:
      type: string
      pattern: '^[a-z0-9-]+$'
      maxLength: 50
      description: |
        Top-level partition key that completely isolates documentation sets.
        Examples: "bannou" (framework self-docs), "arcadia" (game knowledge),
        "arcadia-omega" (realm-specific), "customer-acme" (tenant-specific).
        All operations require a namespace - there is no cross-namespace access.

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
          description: Natural language query (e.g., "how do I authenticate with the API?")
          minLength: 3
          maxLength: 500
        session_id:
          type: string
          format: uuid
          description: |
            Optional client-provided session ID for conversational continuity.
            If provided, previous queries and viewed documents in this session
            inform relevance scoring. Stored in Redis with 24h TTL.
        category:
          $ref: '#/components/schemas/DocumentCategory'
          description: Optional category filter
        max_results:
          type: integer
          description: Maximum number of results to return
          default: 5
          minimum: 1
          maximum: 20
        include_content:
          type: boolean
          description: Include full document content in response
          default: false
        max_summary_length:
          type: integer
          description: Truncate summary fields to this length (useful for voice UIs)
          default: 300
          minimum: 50
          maximum: 500
        min_relevance_score:
          type: number
          format: float
          description: |
            Filter out results below this relevance threshold.
            Returns empty results with helpful message if nothing qualifies.
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
          description: Original query for reference
        total_results:
          type: integer
          description: Total matching documents (may exceed returned count)
        voice_summary:
          type: string
          description: |
            Concise spoken summary of the top result, optimized for voice AI.
            Ready to be spoken directly by a voice agent.
        suggested_followups:
          type: array
          items:
            type: string
          description: Suggested follow-up questions the user might ask
        no_results_message:
          type: string
          description: |
            Helpful message when no results meet the relevance threshold.
            Example: "I couldn't find documentation about X. Try searching for Y?"

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
          description: Get by UUID (provide this OR slug, not both)
        slug:
          type: string
          pattern: '^[a-z0-9-]+$'
          description: |
            Get by human-readable slug within namespace (provide this OR document_id).
            Slugs are unique per-namespace, not globally.
        session_id:
          type: string
          format: uuid
          description: |
            If provided, updates session context with this document view.
            Uses Redis atomic operations (LPUSH+LTRIM) for concurrency safety.
        include_related:
          $ref: '#/components/schemas/RelatedDepth'
          description: How many levels of related documents to include
        include_content:
          type: boolean
          description: |
            Include full document content. Default false to reduce payload.
            Metadata, summary, and voice_summary always included.
          default: false
        render_html:
          type: boolean
          description: |
            Render markdown content as HTML instead of raw markdown.
            Only applies when include_content=true. Uses Markdig library.
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
          description: Related documents based on include_related depth
        content_format:
          type: string
          enum: [markdown, html, none]
          description: |
            Format of the content field:
            - markdown: Raw markdown (default when include_content=true)
            - html: Rendered HTML (when render_html=true)
            - none: Content not included (when include_content=false)

    RelatedDepth:
      type: string
      enum: [none, direct, extended]
      default: direct
      description: |
        How deep to traverse related document links:
        - none: No related documents included
        - direct: Only directly linked documents (depth 1)
        - extended: Related documents + their related documents (depth 2)

    # ===== Search Endpoint =====
    # NOTE: During implementation, research feasibility of advanced search operators:
    #   - Exact phrase: "authentication flow"
    #   - Exclusion: auth -oauth
    #   - Field-specific: title:authentication
    #   - OR logic: auth OR authentication
    # Implement if achievable without excessive complexity.
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
          description: |
            Keyword or phrase to search for.
            Future: May support advanced operators ("phrase", -exclude, field:value)
          minLength: 2
          maxLength: 200
        session_id:
          type: string
          format: uuid
          description: Optional session for context tracking
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
          description: Fields to search in
          default: ["title", "content", "tags"]
        sort_by:
          type: string
          enum: [relevance, recency, alphabetical]
          default: relevance
          description: How to order results
        include_content:
          type: boolean
          default: false
        render_html:
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
        sort_by:
          type: string
          enum: [relevance, recency, alphabetical]

    # ===== List Endpoint =====
    # NOTE: No eventing for list - browsing action, would be too noisy
    ListDocumentsRequest:
      type: object
      required:
        - namespace
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        session_id:
          type: string
          format: uuid
          description: Optional session for context tracking
        category:
          $ref: '#/components/schemas/DocumentCategory'
        tags:
          type: array
          items:
            type: string
          description: Filter by tags
        tags_match:
          type: string
          enum: [all, any]
          default: all
          description: |
            - all: Document must have ALL specified tags (AND logic)
            - any: Document must have AT LEAST ONE tag (OR logic)
        created_after:
          type: string
          format: date-time
          description: Filter to documents created after this timestamp
        created_before:
          type: string
          format: date-time
          description: Filter to documents created before this timestamp
        updated_after:
          type: string
          format: date-time
          description: Filter to documents updated after this timestamp
        updated_before:
          type: string
          format: date-time
          description: Filter to documents updated before this timestamp
        titles_only:
          type: boolean
          default: false
          description: |
            If true, only return document_id, slug, title, and category.
            Omits summary, voice_summary, and tags for smaller payloads.
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
          enum: [asc, desc]
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
        titles_only:
          type: boolean
          description: Indicates if response contains title-only entries

    ListSortField:
      type: string
      enum:
        - created_at
        - updated_at
        - title
      default: updated_at
      description: Sort field for list results (no relevance - use search for that)

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
          description: |
            Value for the suggestion source:
            - document_id: UUID of the document
            - slug: Slug of the document
            - topic: Free-text topic string
            - category: Category name to explore
        session_id:
          type: string
          format: uuid
          description: |
            Optional session context for better suggestions.
            Session keywords influence suggestion weighting but don't constrain results.
            Avoids locking users into a topic - context is a hint, not a filter.
        max_suggestions:
          type: integer
          default: 5
          minimum: 1
          maximum: 10
        exclude_recently_viewed:
          type: boolean
          default: true
          description: Exclude documents the session has recently viewed

    SuggestRelatedResponse:
      type: object
      required:
        - namespace
        - suggestions
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        suggestion_source:
          $ref: '#/components/schemas/SuggestionSource'
        source_value:
          type: string
          description: Echo of the input source value
        suggestions:
          type: array
          items:
            $ref: '#/components/schemas/TopicSuggestion'
        voice_prompt:
          type: string
          description: |
            Voice-friendly prompt listing available topics.
            e.g., "Would you like to learn about authentication, deployment, or the WebSocket protocol?"
        session_influenced:
          type: boolean
          description: Whether session context affected the suggestions

    SuggestionSource:
      type: string
      enum:
        - document_id    # Suggest topics related to a specific document (by UUID)
        - slug           # Suggest topics related to a document (by slug)
        - topic          # Suggest topics related to a free-text topic string
        - category       # Suggest popular/important topics within a category
      description: |
        What to base suggestions on. The source_value field provides the actual value.
        - document_id/slug: Returns related documents (excludes the source document)
        - topic: Returns documents semantically related to the topic text
        - category: Returns top documents in that category

    # ===== Admin Endpoints =====
    # DESIGN NOTES:
    # - Content stored as-is without validation (supports markdown, code, text files)
    # - Escape content appropriately on output - treat as untrusted user input
    # - related_documents are bidirectional - setting A→B also sets B→A
    # - Delete moves to trashcan (soft delete), recoverable within TTL
    # - Update copies previous version to trashcan before overwriting

    CreateDocumentRequest:
      type: object
      required:
        - namespace
        - title
        - content
        - category
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        title:
          type: string
          minLength: 3
          maxLength: 200
        slug:
          type: string
          description: URL-friendly identifier (auto-generated if not provided). Unique within namespace.
          pattern: '^[a-z0-9-]+$'
        content:
          type: string
          description: |
            Full document content. Stored as-is without modification.
            Supports markdown, plain text, or any text format.
            Escaped appropriately when returned in responses.
        summary:
          type: string
          description: Brief summary for search results and voice responses
          maxLength: 500
        voice_summary:
          type: string
          description: Optimized summary for voice AI (shorter, conversational)
          maxLength: 300
        category:
          $ref: '#/components/schemas/DocumentCategory'
        tags:
          type: array
          items:
            type: string
          maxItems: 20
        related_documents:
          type: array
          items:
            type: string
            format: uuid
          description: |
            UUIDs of related documents (must exist in same namespace).
            Relationships are bidirectional - referenced docs also updated.
        metadata:
          type: object
          additionalProperties: true
          description: Additional metadata (service name, version, etc.)

    CreateDocumentResponse:
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
        slug:
          type: string
        related_documents_updated:
          type: array
          items:
            type: string
            format: uuid
          description: List of related documents that were also updated (bidirectional link)

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
          description: Identify document by UUID (provide this OR slug)
        slug:
          type: string
          description: Identify document by slug (provide this OR document_id)
        regenerate_slug:
          type: boolean
          default: false
          description: |
            If true and title changed, regenerate slug from new title.
            WARNING: May break existing links to this document.
        title:
          type: string
        content:
          type: string
        summary:
          type: string
        voice_summary:
          type: string
        category:
          $ref: '#/components/schemas/DocumentCategory'
        tags:
          type: array
          items:
            type: string
        related_documents:
          type: array
          items:
            type: string
            format: uuid
          description: |
            UUIDs of related documents (must exist in same namespace).
            Relationships are bidirectional.
        metadata:
          type: object
          additionalProperties: true

    UpdateDocumentResponse:
      type: object
      required:
        - namespace
        - document_id
        - updated_at
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        document_id:
          type: string
          format: uuid
        slug:
          type: string
        previous_slug:
          type: string
          description: If slug changed, the previous slug (for redirect setup)
        updated_at:
          type: string
          format: date-time
        previous_version_trashed:
          type: boolean
          description: Whether previous version was saved to trashcan
        related_documents_updated:
          type: array
          items:
            type: string
            format: uuid

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
          description: Delete by UUID (provide this OR slug)
        slug:
          type: string
          description: Delete by slug (provide this OR document_id)

    DeleteDocumentResponse:
      type: object
      required:
        - namespace
        - document_id
        - trashed
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        document_id:
          type: string
          format: uuid
        slug:
          type: string
        trashed:
          type: boolean
          description: Whether document was moved to trashcan (always true on success)
        trashcan_expires_at:
          type: string
          format: date-time
          description: When this document will be permanently deleted from trashcan
        related_documents_updated:
          type: array
          items:
            type: string
            format: uuid
          description: Related documents that had their links to this doc removed

    # ===== Recovery Endpoint =====
    RecoverDocumentRequest:
      type: object
      required:
        - namespace
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        document_id:
          type: string
          format: uuid
          description: Recover by UUID (provide this OR slug)
        slug:
          type: string
          description: Recover by slug (provide this OR document_id)

    RecoverDocumentResponse:
      type: object
      required:
        - namespace
        - document_id
        - recovered
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        document_id:
          type: string
          format: uuid
        slug:
          type: string
        recovered:
          type: boolean
          description: Whether recovery was successful
        recovery_type:
          type: string
          enum: [deleted_document, previous_version]
          description: |
            - deleted_document: Recovered a deleted document
            - previous_version: Recovered previous version of an existing document
        message:
          type: string
          description: Human-readable result message
        conflict_slug:
          type: string
          description: |
            If recovery failed due to slug conflict (new doc exists with same slug),
            this contains the conflicting slug. Caller may need to delete/rename first.

    # ===== Bulk Operations =====
    BulkUpdateRequest:
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
          minItems: 1
          maxItems: 100
          description: Documents to update (by UUID)
        slugs:
          type: array
          items:
            type: string
          description: Alternative to document_ids - identify by slugs
        dry_run:
          type: boolean
          default: false
          description: |
            Validate operation without making changes. Returns what would happen.
        # Only these fields can be bulk-updated
        category:
          $ref: '#/components/schemas/DocumentCategory'
          description: Set category for all documents
        add_tags:
          type: array
          items:
            type: string
          description: Tags to add to all documents
        remove_tags:
          type: array
          items:
            type: string
          description: Tags to remove from all documents
        metadata_merge:
          type: object
          additionalProperties: true
          description: Metadata to merge into all documents (shallow merge)

    BulkUpdateResponse:
      type: object
      required:
        - namespace
        - dry_run
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        dry_run:
          type: boolean
          description: Whether this was a dry run (no changes made)
        updated_count:
          type: integer
          description: Documents updated (0 if dry_run)
        would_update_count:
          type: integer
          description: (dry_run only) Documents that would be updated
        failed_count:
          type: integer
        errors:
          type: array
          items:
            $ref: '#/components/schemas/BulkOperationError'

    BulkDeleteRequest:
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
          minItems: 1
          maxItems: 100
          description: Documents to delete (by UUID)
        slugs:
          type: array
          items:
            type: string
          description: Alternative to document_ids - identify by slugs
        dry_run:
          type: boolean
          default: false
          description: |
            Validate operation without making changes. Returns what would happen.

    BulkDeleteResponse:
      type: object
      required:
        - namespace
        - dry_run
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        dry_run:
          type: boolean
          description: Whether this was a dry run (no changes made)
        deleted_count:
          type: integer
          description: Documents moved to trashcan (0 if dry_run)
        would_delete_count:
          type: integer
          description: (dry_run only) Documents that would be deleted
        failed_count:
          type: integer
        trashcan_expires_at:
          type: string
          format: date-time
        errors:
          type: array
          items:
            $ref: '#/components/schemas/BulkOperationError'

    BulkOperationError:
      type: object
      properties:
        document_id:
          type: string
          format: uuid
        slug:
          type: string
        error_message:
          type: string

    # ===== Trashcan Configuration =====
    TrashcanConfiguration:
      type: object
      description: |
        Configuration for the document trashcan.
        Trashcan stores deleted documents and previous versions.
        Documents in trashcan are excluded from normal searches.
      properties:
        ttl_days:
          type: integer
          default: 7
          minimum: 1
          maximum: 90
          description: Days before trashed documents are permanently deleted
        max_versions_per_document:
          type: integer
          default: 1
          description: |
            Number of previous versions to keep per document.
            Currently only 1 supported. Future: proper versioning.

    # ===== Import Endpoints =====
    # DESIGN NOTES:
    # - Imports are NOT transactional - partial success is possible
    # - Use dry_run to validate before committing
    # - make_related auto-links all documents in the batch to each other
    # - auto_generate_summaries uses simple first-paragraph extraction (no AI)
    # - max_documents_per_import is configurable in service config (default: unbounded)

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
        overwrite_existing:
          type: boolean
          default: false
          description: Overwrite documents with matching slugs
        dry_run:
          type: boolean
          default: false
          description: |
            Validate all documents without importing. Returns exactly what
            would happen (counts, errors) without making any changes.
        make_related:
          type: boolean
          default: true
          description: |
            When true, all documents in this import batch will automatically
            have each other added as related_documents (bidirectional).
            For more specific relationships, use update endpoint afterward.
        import_source:
          type: string
          maxLength: 100
          description: |
            Provenance tracking - where this import came from.
            Examples: "github-bannou-docs", "confluence-export-2025", "manual"
        auto_generate_summaries:
          type: boolean
          default: false
          description: |
            Generate summary from content for documents missing summary field.
            Uses simple first-paragraph extraction (no AI/external services).

    ImportDocumentationResponse:
      type: object
      required:
        - namespace
        - dry_run
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        dry_run:
          type: boolean
          description: Whether this was a dry run (no changes made)
        import_source:
          type: string
          description: Echo of the import_source from request for client correlation
        import_id:
          type: string
          format: uuid
          description: Unique ID for this import operation (for event correlation)
        imported_count:
          type: integer
          description: Number of new documents created (0 if dry_run)
        updated_count:
          type: integer
          description: Number of existing documents overwritten (0 if dry_run)
        skipped_count:
          type: integer
          description: Number of documents skipped due to conflicts
        would_import_count:
          type: integer
          description: (dry_run only) Documents that would be created
        would_update_count:
          type: integer
          description: (dry_run only) Documents that would be overwritten
        would_skip_count:
          type: integer
          description: (dry_run only) Documents that would be skipped
        errors:
          type: array
          items:
            $ref: '#/components/schemas/ImportError'
        validation_errors:
          type: array
          description: (dry_run only) Validation issues found
          items:
            $ref: '#/components/schemas/ImportError'

    ImportDocument:
      type: object
      description: Document to import (namespace inherited from parent request)
      required:
        - title
        - content
        - category
      properties:
        title:
          type: string
        slug:
          type: string
          description: Unique within namespace (auto-generated if not provided)
        content:
          type: string
        summary:
          type: string
        voice_summary:
          type: string
        category:
          $ref: '#/components/schemas/DocumentCategory'
        tags:
          type: array
          items:
            type: string
        metadata:
          type: object
          additionalProperties: true

    ImportError:
      type: object
      properties:
        document_title:
          type: string
        slug:
          type: string
        error_message:
          type: string

    # ===== Trashcan Endpoint =====
    ListTrashcanRequest:
      type: object
      required:
        - namespace
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        limit:
          type: integer
          default: 50
          minimum: 1
          maximum: 100
          description: Maximum items to return
        offset:
          type: integer
          default: 0
          minimum: 0
          description: Pagination offset

    ListTrashcanResponse:
      type: object
      required:
        - namespace
        - items
        - total_count
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        items:
          type: array
          items:
            $ref: '#/components/schemas/TrashcanItem'
        total_count:
          type: integer
          description: Total items in trashcan
        limit:
          type: integer
        offset:
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
          description: When document will be permanently deleted

    # ===== Purge Endpoint =====
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
          description: |
            Specific documents to purge. If empty or omitted, purges ALL
            documents in the namespace's trashcan.

    PurgeTrashcanResponse:
      type: object
      required:
        - namespace
        - purged_count
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        purged_count:
          type: integer
          description: Number of documents permanently deleted
        errors:
          type: array
          items:
            $ref: '#/components/schemas/BulkOperationError'

    # ===== Stats Endpoint =====
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
        - trashcan_count
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        document_count:
          type: integer
          description: Total active documents
        trashcan_count:
          type: integer
          description: Documents in trashcan
        category_counts:
          type: object
          additionalProperties:
            type: integer
          description: Document count per category
        total_content_bytes:
          type: integer
          format: int64
          description: Approximate total content size
        oldest_document:
          type: string
          format: date-time
        newest_document:
          type: string
          format: date-time
        last_updated:
          type: string
          format: date-time
          description: Most recent document modification

    # ===== Common Types =====
    Document:
      type: object
      required:
        - namespace
        - document_id
        - title
        - content
        - category
        - created_at
        - updated_at
      properties:
        namespace:
          $ref: '#/components/schemas/Namespace'
        document_id:
          type: string
          format: uuid
        slug:
          type: string
          description: Unique within namespace
        title:
          type: string
        content:
          type: string
        summary:
          type: string
        voice_summary:
          type: string
        category:
          $ref: '#/components/schemas/DocumentCategory'
        tags:
          type: array
          items:
            type: string
        related_documents:
          type: array
          items:
            type: string
            format: uuid
          description: UUIDs of related documents (same namespace)
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
      description: Lightweight document reference (namespace in parent response)
      required:
        - document_id
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
        summary:
          type: string
        voice_summary:
          type: string
        category:
          $ref: '#/components/schemas/DocumentCategory'
        tags:
          type: array
          items:
            type: string

    DocumentResult:
      type: object
      description: Search result (namespace in parent response)
      required:
        - document_id
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
        summary:
          type: string
        voice_summary:
          type: string
          description: Concise summary optimized for voice AI
        content:
          type: string
          description: Full content (if include_content=true)
        excerpt:
          type: string
          description: Relevant excerpt highlighting the match
        category:
          $ref: '#/components/schemas/DocumentCategory'
        tags:
          type: array
          items:
            type: string
        relevance_score:
          type: number
          format: float
          minimum: 0
          maximum: 1
          description: Relevance score (1.0 = perfect match)
        match_type:
          type: string
          enum: [semantic, keyword, exact]
          description: How the document matched the query

    TopicSuggestion:
      type: object
      description: Related topic suggestion with metadata about why it was suggested
      required:
        - topic
        - document_id
        - match_reason
      properties:
        topic:
          type: string
          description: Human-readable topic name
        slug:
          type: string
          description: Slug for the suggested document
        document_id:
          type: string
          format: uuid
        category:
          $ref: '#/components/schemas/DocumentCategory'
        summary:
          type: string
          description: Brief summary of the suggested document
        voice_summary:
          type: string
          description: Voice-optimized summary
        relevance_score:
          type: number
          format: float
          minimum: 0
          maximum: 1
          description: How related this topic is to the source (1.0 = highly related)
        match_reason:
          $ref: '#/components/schemas/SuggestionMatchReason'
        match_details:
          type: string
          description: |
            Human-readable explanation of why this was suggested.
            e.g., "Shares 3 tags: authentication, security, jwt"
            e.g., "Referenced in 'Getting Started' document"
            e.g., "Contains related keywords: oauth, token, bearer"

    SuggestionMatchReason:
      type: string
      enum:
        - explicit_link      # Document explicitly links to this via related_documents
        - shared_tags        # Documents share one or more tags
        - same_category      # Documents are in the same category
        - keyword_overlap    # Significant keyword/phrase overlap in content
        - popular_in_category # Top document in the requested category
        - session_affinity   # Matches session context patterns
      description: Why this document was suggested

    DocumentCategory:
      type: string
      enum:
        - framework           # Bannou framework docs
        - service             # Individual service documentation
        - api                 # API reference documentation
        - deployment          # Deployment and operations
        - architecture        # System architecture
        - tutorial            # How-to guides and tutorials
        - troubleshooting     # Problem-solving guides
        - game_design         # Arcadia game design documentation
        - world_lore          # Arcadia world building and lore
        - npc_systems         # NPC AI and behavior systems
        - faq                 # Frequently asked questions

    SearchField:
      type: string
      enum:
        - title
        - content
        - summary
        - tags
        - metadata

    SortField:
      type: string
      enum:
        - created_at
        - updated_at
        - title
        - relevance

```

---

## AI Agent Integration

### SignalWire SWAIG Configuration

```yaml
# SWML configuration for SignalWire AI agent
version: "1.0.0"
sections:
  main:
    - ai:
        prompt:
          text: |
            You are an expert documentation assistant for the Bannou microservices framework
            and Arcadia game systems. Help users understand the architecture, services,
            deployment patterns, and game design concepts.

            Key knowledge areas:
            - Bannou: WebSocket-first microservices, Dapr integration, schema-driven development
            - Services: Auth, Accounts, Connect, Permissions, Behavior, Orchestrator
            - Arcadia: Guardian spirits, three realms, NPC AI, economic systems

            Always be conversational and offer to provide more details.

        params:
          swaig_allow_swml: true

        SWAIG:
          defaults:
            web_hook_url: "https://your-bannou-instance.com"

          # NOTE: namespace is required by the API but typically injected by your
          # webhook handler or hardcoded per deployment. Examples show it as a
          # hidden/default parameter that the AI doesn't need to provide.

          functions:
            - function: query_documentation
              description: |
                Search the documentation for information about Bannou services,
                architecture, APIs, deployment, or Arcadia game systems.
                Use when user asks "how do I...", "what is...", or needs technical info.
              parameters:
                type: object
                properties:
                  namespace:
                    type: string
                    description: "Documentation namespace (usually injected by handler)"
                    default: "bannou"
                  query:
                    type: string
                    description: "Natural language search query"
                  category:
                    type: string
                    enum: [framework, service, api, deployment, architecture, tutorial, troubleshooting, game_design, world_lore, npc_systems, faq]
                    description: "Optional category filter"
                  max_results:
                    type: integer
                    description: "Maximum results (default 3 for voice)"
                    default: 3
                required:
                  - query
              web_hook_url: "https://your-bannou-instance.com/v1.0/invoke/bannou/method/documentation/query"

            - function: get_deployment_info
              description: |
                Get deployment instructions for Bannou services.
                Use when user asks about deploying, running, or configuring services.
              parameters:
                type: object
                properties:
                  namespace:
                    type: string
                    default: "bannou"
                  query:
                    type: string
                    description: "Deployment topic query"
                  category:
                    type: string
                    default: "deployment"
                required:
                  - query
              web_hook_url: "https://your-bannou-instance.com/v1.0/invoke/bannou/method/documentation/query"

            - function: get_api_reference
              description: |
                Get API reference for specific services or endpoints.
                Use when user asks about API endpoints, parameters, or responses.
              parameters:
                type: object
                properties:
                  namespace:
                    type: string
                    default: "bannou"
                  query:
                    type: string
                    description: "API or service name to look up"
                required:
                  - query
              web_hook_url: "https://your-bannou-instance.com/v1.0/invoke/bannou/method/documentation/query"

            - function: suggest_topics
              description: |
                Get related topics the user might want to explore.
                Use after answering a question to offer follow-up options.
              parameters:
                type: object
                properties:
                  namespace:
                    type: string
                    default: "bannou"
                  source_value:
                    type: string
                    description: "Current topic or document slug to get suggestions for"
                  suggestion_source:
                    type: string
                    enum: [topic, slug, category]
                    default: "topic"
                  max_suggestions:
                    type: integer
                    default: 3
                required:
                  - source_value
              web_hook_url: "https://your-bannou-instance.com/v1.0/invoke/bannou/method/documentation/suggest"
```

### OpenAI Function Calling Integration

**NOTE**: The `namespace` parameter is required but typically injected by your backend.
You can either hardcode it in your function handler or expose it to the AI.

```json
{
  "model": "gpt-4o",
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "query_documentation",
        "description": "Search Bannou and Arcadia documentation. Use when user asks about services, architecture, APIs, deployment, or game systems.",
        "parameters": {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "Natural language search query"
            },
            "category": {
              "type": "string",
              "enum": ["framework", "service", "api", "deployment", "architecture", "tutorial", "troubleshooting", "game_design", "world_lore", "npc_systems", "faq"],
              "description": "Optional category filter"
            },
            "max_results": {
              "type": "integer",
              "description": "Maximum results to return",
              "default": 5
            },
            "include_content": {
              "type": "boolean",
              "description": "Include full document content (default false for voice)",
              "default": false
            }
          },
          "required": ["query"],
          "additionalProperties": false
        },
        "strict": true
      }
    },
    {
      "type": "function",
      "function": {
        "name": "get_document",
        "description": "Get a specific document by ID or slug for detailed information",
        "parameters": {
          "type": "object",
          "properties": {
            "document_id": {
              "type": "string",
              "description": "Document UUID from previous search results"
            },
            "slug": {
              "type": "string",
              "description": "Document slug (alternative to document_id)"
            },
            "include_related": {
              "type": "string",
              "enum": ["none", "direct", "extended"],
              "description": "How many levels of related documents to include",
              "default": "direct"
            },
            "include_content": {
              "type": "boolean",
              "description": "Include full document content",
              "default": false
            }
          },
          "required": [],
          "additionalProperties": false
        },
        "strict": true
      }
    },
    {
      "type": "function",
      "function": {
        "name": "suggest_related_topics",
        "description": "Get related topics for follow-up conversation",
        "parameters": {
          "type": "object",
          "properties": {
            "source_value": {
              "type": "string",
              "description": "Topic string, document slug, or category to get suggestions for"
            },
            "suggestion_source": {
              "type": "string",
              "enum": ["topic", "slug", "category"],
              "description": "How to interpret source_value",
              "default": "topic"
            },
            "max_suggestions": {
              "type": "integer",
              "description": "Maximum suggestions to return",
              "default": 5
            }
          },
          "required": ["source_value"],
          "additionalProperties": false
        },
        "strict": true
      }
    }
  ]
}
```

### Claude Tool Use Integration

```json
{
  "tools": [
    {
      "name": "query_documentation",
      "description": "Search Bannou and Arcadia documentation for technical information, architecture details, API references, and game design concepts. Use when user asks 'how do I...', 'what is...', or needs information about services, deployment, or game systems.",
      "input_schema": {
        "type": "object",
        "properties": {
          "namespace": {
            "type": "string",
            "description": "Documentation namespace (e.g., 'bannou', 'arcadia')"
          },
          "query": {
            "type": "string",
            "description": "Natural language search query"
          },
          "category": {
            "type": "string",
            "enum": ["framework", "service", "api", "deployment", "architecture", "tutorial", "troubleshooting", "game_design", "world_lore", "npc_systems", "faq"],
            "description": "Optional category filter"
          },
          "max_results": {
            "type": "integer",
            "description": "Maximum results (default 5)"
          }
        },
        "required": ["namespace", "query"]
      }
    },
    {
      "name": "get_document",
      "description": "Retrieve a specific document by ID for full content",
      "input_schema": {
        "type": "object",
        "properties": {
          "namespace": {
            "type": "string",
            "description": "Documentation namespace"
          },
          "document_id": {
            "type": "string",
            "description": "Document UUID"
          },
          "include_content": {
            "type": "boolean",
            "description": "Include full document content (default true)"
          },
          "include_related": {
            "type": "boolean",
            "description": "Include related documents"
          }
        },
        "required": ["namespace", "document_id"]
      }
    },
    {
      "name": "suggest_related_topics",
      "description": "Get related topics for conversational flow",
      "input_schema": {
        "type": "object",
        "properties": {
          "namespace": {
            "type": "string",
            "description": "Documentation namespace"
          },
          "topic": {
            "type": "string",
            "description": "Current topic"
          },
          "max_suggestions": {
            "type": "integer",
            "description": "Maximum suggestions"
          }
        },
        "required": ["namespace", "topic"]
      }
    }
  ]
}
```

---

## Data Storage Architecture

### Dapr State Store Configuration

```yaml
# provisioning/dapr/components/documentation-statestore.yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: documentation-statestore
spec:
  type: state.redis
  version: v1
  metadata:
  - name: redisHost
    value: redis:6379
  - name: redisPassword
    value: ""
  - name: actorStateStore
    value: "false"
```

```yaml
# provisioning/dapr/components/documentation-mysql-statestore.yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: documentation-mysql-statestore
spec:
  type: state.mysql
  version: v1
  metadata:
  - name: connectionString
    value: "user:password@tcp(mysql:3306)/bannou_documentation"
  - name: schemaName
    value: "bannou_documentation"
  - name: tableName
    value: "documents"
```

### Key Prefix Strategy

```csharp
public class DocumentationService : IDocumentationService
{
    private const string REDIS_STORE = "documentation-statestore";
    private const string MYSQL_STORE = "documentation-mysql-statestore";

    // Redis key prefixes (hot cache)
    private const string DOC_CACHE_PREFIX = "doc-cache:";      // Cached full documents
    private const string SEARCH_CACHE_PREFIX = "search-cache:"; // Cached search results
    private const string EMBEDDING_PREFIX = "embedding:";       // Document embeddings
    private const string CATEGORY_INDEX_PREFIX = "cat-idx:";    // Category indexes

    // MySQL for persistent storage
    // Key format: doc:{document_id}
}
```

### Data Models

```csharp
public class DocumentModel
{
    public string Namespace { get; set; } = "";  // Required partition key
    public string DocumentId { get; set; } = Guid.NewGuid().ToString();
    public string Slug { get; set; } = "";  // Unique within namespace
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string Summary { get; set; } = "";
    public string VoiceSummary { get; set; } = "";
    public DocumentCategory Category { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> RelatedDocuments { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();

    // Timestamps (Unix format per Bannou standards)
    public long CreatedAtUnix { get; set; }
    public long UpdatedAtUnix { get; set; }

    [JsonIgnore]
    public DateTimeOffset CreatedAt
    {
        get => DateTimeOffset.FromUnixTimeSeconds(CreatedAtUnix);
        set => CreatedAtUnix = value.ToUnixTimeSeconds();
    }

    [JsonIgnore]
    public DateTimeOffset UpdatedAt
    {
        get => DateTimeOffset.FromUnixTimeSeconds(UpdatedAtUnix);
        set => UpdatedAtUnix = value.ToUnixTimeSeconds();
    }
}

public class DocumentEmbedding
{
    public string Namespace { get; set; } = "";
    public string DocumentId { get; set; } = "";
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public long GeneratedAtUnix { get; set; }
    public string EmbeddingModel { get; set; } = "";
}

/// <summary>
/// Session context for conversational continuity.
/// Stored in Redis with atomic operations for concurrency safety.
/// </summary>
public class SessionContext
{
    public string SessionId { get; set; } = "";
    public string Namespace { get; set; } = "";  // Sessions are namespace-scoped

    /// <summary>
    /// Last 10 viewed document IDs (LIFO).
    /// Updated atomically with LPUSH+LTRIM.
    /// </summary>
    public List<string> RecentlyViewed { get; set; } = new();

    /// <summary>
    /// Last 10 query strings for context-aware follow-ups.
    /// </summary>
    public List<string> RecentQueries { get; set; } = new();

    /// <summary>
    /// Weighted category preferences based on activity.
    /// Key: category name, Value: weight (0.0-1.0).
    /// </summary>
    public Dictionary<string, float> CategoryAffinity { get; set; } = new();

    public long CreatedAtUnix { get; set; }
    public long LastActivityUnix { get; set; }

    // TTL: 24 hours
    public const int TTL_HOURS = 24;
}
```

---

## Search Implementation

### Full-Text Search Service

The search service uses weighted full-text search without external AI dependencies.
Semantic/AI-enhanced search can be added later as an optional enhancement.

```csharp
public interface ISearchService
{
    /// <summary>
    /// Natural language query search with weighted ranking.
    /// Title matches weighted higher than content matches.
    /// Tag matches boost relevance score.
    /// </summary>
    Task<List<DocumentResult>> QuerySearchAsync(
        string query,
        string ns,
        DocumentCategory? category,
        int maxResults,
        float minRelevanceScore,
        CancellationToken ct);

    /// <summary>
    /// Keyword search with field targeting.
    /// Supports basic operators: "exact phrase", -exclusion
    /// </summary>
    Task<List<DocumentResult>> KeywordSearchAsync(
        string searchTerm,
        string ns,
        SearchField[] fields,
        DocumentCategory? category,
        int maxResults,
        CancellationToken ct);

    Task IndexDocumentAsync(string ns, DocumentModel document, CancellationToken ct);
    Task RemoveFromIndexAsync(string ns, string documentId, CancellationToken ct);
    Task ReindexNamespaceAsync(string ns, CancellationToken ct);
}
```

### Search Ranking Algorithm

**Relevance scoring factors** (combined into 0.0-1.0 score):
- **Title match**: Exact match = 1.0, partial = 0.7, word overlap = 0.4
- **Tag match**: Each matching tag adds 0.1 (max 0.3)
- **Category match**: If category filter matches = 0.1 bonus
- **Content match**: TF-IDF style scoring normalized to 0.0-0.5
- **Recency boost**: Documents updated in last 30 days get 0.05 bonus

```csharp
public class SearchService : ISearchService
{
    private readonly DaprClient _daprClient;
    private const string SEARCH_INDEX_STORE = "documentation-search-statestore";

    public async Task<List<DocumentResult>> QuerySearchAsync(
        string query,
        string ns,
        DocumentCategory? category,
        int maxResults,
        float minRelevanceScore,
        CancellationToken ct)
    {
        // Tokenize query into searchable terms
        var queryTerms = TokenizeQuery(query);

        // Search across indexed documents in namespace
        var candidates = await SearchIndexAsync(ns, queryTerms, category, ct);

        // Score and rank results
        var scored = candidates
            .Select(doc => new { Doc = doc, Score = CalculateRelevance(doc, queryTerms) })
            .Where(x => x.Score >= minRelevanceScore)
            .OrderByDescending(x => x.Score)
            .Take(maxResults)
            .Select(x => ToDocumentResult(x.Doc, x.Score))
            .ToList();

        return scored;
    }

    private float CalculateRelevance(IndexedDocument doc, string[] queryTerms)
    {
        float score = 0f;

        // Title matching (highest weight)
        var titleScore = CalculateTitleMatch(doc.Title, queryTerms);
        score += titleScore * 0.4f;

        // Tag matching
        var tagScore = CalculateTagMatch(doc.Tags, queryTerms);
        score += Math.Min(tagScore, 0.3f);

        // Content matching (TF-IDF style)
        var contentScore = CalculateContentMatch(doc.ContentTokens, queryTerms);
        score += contentScore * 0.25f;

        // Recency boost
        if (doc.UpdatedAt > DateTimeOffset.UtcNow.AddDays(-30))
            score += 0.05f;

        return Math.Min(score, 1.0f);
    }
}
```

### Future Enhancement: AI-Powered Search

AI enhancements can be added later without changing the API contract:
- **Embedding-based semantic search**: Vector similarity for conceptual matching
- **AI-generated summaries**: Automatic summary generation from content
- **Query expansion**: AI-suggested related terms

These would be implemented as optional `ISearchEnhancer` decorators around the base search service.

---

## Markdown Rendering

### Markdig Integration

For HTML rendering of markdown content, we use [Markdig](https://github.com/xoofx/markdig) (MIT licensed, most popular .NET markdown library).

**NuGet Package**: `Markdig` (latest stable)

```csharp
public interface IMarkdownRenderer
{
    /// <summary>
    /// Render markdown to HTML using Markdig pipeline.
    /// </summary>
    string ToHtml(string markdown);
}

public class MarkdigRenderer : IMarkdownRenderer
{
    private readonly MarkdownPipeline _pipeline;

    public MarkdigRenderer()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()  // Tables, task lists, footnotes, etc.
            .UseSoftlineBreakAsHardlineBreak()  // Better for documentation
            .Build();
    }

    public string ToHtml(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return string.Empty;

        return Markdown.ToHtml(markdown, _pipeline);
    }
}
```

**Registration**:
```csharp
// In DocumentationServicePlugin.cs
services.AddSingleton<IMarkdownRenderer, MarkdigRenderer>();
```

---

## Session Management

### Consistency Model: Best-Effort

**Session context is advisory, not authoritative.** It provides hints for relevance scoring
but is not critical state. The following consistency model applies:

- **No data loss**: Sessions persist correctly across requests
- **Eventual consistency**: Concurrent updates may overwrite each other (last-write-wins)
- **Acceptable race condition**: If two simultaneous requests on the same session both add
  to `recently_viewed`, one addition may be lost. This is acceptable because session
  context is a "hint not constraint" - it influences weighting but doesn't lock users
  into topics or prevent access to content.

**Example race scenario**:
```
Request A: views doc X    →  reads session []    →  saves [X]
Request B: views doc Y    →  reads session []    →  saves [Y]  (overwrites)
Result: [Y] instead of [X, Y]
```

For session data, this is fine. For document storage or other critical state,
we use proper optimistic concurrency or transactional patterns.

### Redis Session Operations

Session context management using Dapr state store:

```csharp
public interface ISessionContextService
{
    Task<SessionContext?> GetSessionAsync(string sessionId, string ns, CancellationToken ct);
    Task UpdateViewedDocumentAsync(string sessionId, string ns, string documentId, CancellationToken ct);
    Task UpdateQueryAsync(string sessionId, string ns, string query, CancellationToken ct);
    Task UpdateCategoryAffinityAsync(string sessionId, string ns, string category, CancellationToken ct);
}

public class RedisSessionContextService : ISessionContextService
{
    private readonly DaprClient _daprClient;
    private const string REDIS_STORE = "documentation-statestore";

    public async Task UpdateViewedDocumentAsync(
        string sessionId, string ns, string documentId, CancellationToken ct)
    {
        // Redis key includes namespace for isolation
        var key = $"session:{ns}:{sessionId}";

        // Get current session or create new
        var session = await _daprClient.GetStateAsync<SessionContext>(REDIS_STORE, key, ct)
            ?? new SessionContext { SessionId = sessionId, Namespace = ns };

        // Add to recently viewed (LIFO, max 10)
        session.RecentlyViewed.Insert(0, documentId);
        if (session.RecentlyViewed.Count > 10)
            session.RecentlyViewed = session.RecentlyViewed.Take(10).ToList();

        session.LastActivityUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Save with TTL
        var metadata = new Dictionary<string, string> { { "ttlInSeconds", (SessionContext.TTL_HOURS * 3600).ToString() } };
        await _daprClient.SaveStateAsync(REDIS_STORE, key, session, metadata: metadata, cancellationToken: ct);
    }

    public async Task UpdateQueryAsync(
        string sessionId, string ns, string query, CancellationToken ct)
    {
        var key = $"session:{ns}:{sessionId}";
        var session = await _daprClient.GetStateAsync<SessionContext>(REDIS_STORE, key, ct)
            ?? new SessionContext { SessionId = sessionId, Namespace = ns };

        // Add to recent queries (LIFO, max 10)
        session.RecentQueries.Insert(0, query);
        if (session.RecentQueries.Count > 10)
            session.RecentQueries = session.RecentQueries.Take(10).ToList();

        session.LastActivityUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var metadata = new Dictionary<string, string> { { "ttlInSeconds", (SessionContext.TTL_HOURS * 3600).ToString() } };
        await _daprClient.SaveStateAsync(REDIS_STORE, key, session, metadata: metadata, cancellationToken: ct);
    }
}
```

---

## Bidirectional Document Relationships

### Link Strategy

When document A adds document B as a related document, the relationship is bidirectional:
B should also list A as related. This is enforced during create/update operations.

**Implementation Order** (minimizes orphaned backlinks):

```
When updating A to add B as related_documents:

1. VALIDATE: Confirm B exists in the namespace (fail fast if not)
2. UPDATE B FIRST: Add A to B.related_documents, save B
3. UPDATE A: Add B to A.related_documents, save A
4. IF STEP 3 FAILS (rare - A vanished between request and save):
   - ROLLBACK: Remove A from B.related_documents
   - Return error to caller
```

**Rationale**: The common case is that document A (being updated) exists - we're updating it.
Document B might not exist, which is why we validate first. The rare failure case is
A disappearing between validation and save, which we handle with rollback.

### Failure Modes

| Scenario | Outcome | Action |
|----------|---------|--------|
| B doesn't exist | Validation fails | Return 400 with invalid related_document ID |
| B save fails | A not yet modified | Return 500, no cleanup needed |
| A save fails (after B saved) | Orphaned backlink in B | Rollback: remove A from B |
| A deleted while update in progress | Orphaned backlink in B | Rollback: remove A from B |

### Cleanup on Delete

When document A is deleted:
- All documents in A.related_documents have A removed from their related_documents
- This happens as part of the delete operation (before moving A to trashcan)
- If any backlink cleanup fails, log warning but proceed with delete

### Import Batch Linking

When `make_related=true` on import, documents are linked after all are created:
- Prevents N² validation during import
- Limited by `MaxAutoRelationsPerImport` to prevent every doc linking to every other doc
- Links are created based on shared tags/category similarity within the batch

---

## Event Architecture

### Events Published

```yaml
# schemas/documentation-events.yaml

# ===== Analytics Events (Anonymous) =====
DocumentationQueriedEvent:
  type: object
  description: |
    Published when documentation is queried. Anonymous - no client identifiers.
    Used for analytics: popular topics, failed queries, documentation gaps.
  required: [event_id, timestamp, namespace]
  properties:
    event_id:
      type: string
      format: uuid
    timestamp:
      type: string
      format: date-time
    namespace:
      type: string
      description: Documentation partition queried
    query_hash:
      type: string
      description: SHA256 hash of query (not the query itself, for privacy)
    query_terms_count:
      type: integer
      description: Number of terms in query
    category_filter:
      type: string
      description: Category filter applied, if any
    results_returned:
      type: integer
    top_relevance_score:
      type: number
      format: float
    session_id:
      type: string
      format: uuid
      description: Anonymous session correlation only
    had_session_context:
      type: boolean
      description: Whether session context influenced results

DocumentViewedEvent:
  type: object
  description: |
    Published when a specific document is retrieved. Anonymous.
    Used for analytics: popular docs, navigation patterns, dead content.
  required: [event_id, timestamp, namespace, document_id]
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
    slug:
      type: string
    category:
      $ref: '#/components/schemas/DocumentCategory'
    session_id:
      type: string
      format: uuid
    from_search_result:
      type: boolean
      description: Did user arrive here from a query result?
    include_related_depth:
      type: string
      enum: [none, direct, extended]

DocumentationSearchedEvent:
  type: object
  description: |
    Published when keyword search is performed. Anonymous.
    Distinct from DocumentationQueriedEvent (semantic search).
    Used for analytics: popular search terms, search patterns.
  required: [event_id, timestamp, namespace]
  properties:
    event_id:
      type: string
      format: uuid
    timestamp:
      type: string
      format: date-time
    namespace:
      type: string
    search_term_hash:
      type: string
      description: SHA256 hash of search term (not the term itself, for privacy)
    search_term_count:
      type: integer
      description: Number of words in search term
    category_filter:
      type: string
    search_fields:
      type: array
      items:
        type: string
      description: Which fields were searched (title, content, tags)
    sort_by:
      type: string
      enum: [relevance, recency, alphabetical]
    results_returned:
      type: integer
    session_id:
      type: string
      format: uuid

DocumentationSuggestionsRequestedEvent:
  type: object
  description: |
    Published when topic suggestions are requested. Anonymous.
    Used for analytics: exploration patterns, topic discovery.
  required: [event_id, timestamp, namespace, suggestion_source]
  properties:
    event_id:
      type: string
      format: uuid
    timestamp:
      type: string
      format: date-time
    namespace:
      type: string
    suggestion_source:
      type: string
      enum: [document_id, slug, topic, category]
    source_value_hash:
      type: string
      description: SHA256 hash of source value (for privacy)
    suggestions_returned:
      type: integer
    match_reasons:
      type: array
      items:
        type: string
      description: Which match reasons appeared in results
    session_id:
      type: string
      format: uuid
    session_influenced:
      type: boolean
      description: Whether session context affected results

# ===== Lifecycle Events =====
DocumentCreatedEvent:
  type: object
  required: [event_id, timestamp, namespace, document_id, title, category]
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
    slug:
      type: string
    title:
      type: string
    category:
      $ref: '#/components/schemas/DocumentCategory'
    tags:
      type: array
      items:
        type: string

DocumentUpdatedEvent:
  type: object
  required: [event_id, timestamp, namespace, document_id]
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
    slug:
      type: string
    changed_fields:
      type: array
      items:
        type: string

DocumentDeletedEvent:
  type: object
  required: [event_id, timestamp, namespace, document_id]
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
    slug:
      type: string

DocumentationImportStartedEvent:
  type: object
  required: [event_id, timestamp, namespace, import_id, document_count]
  properties:
    event_id:
      type: string
      format: uuid
    timestamp:
      type: string
      format: date-time
    namespace:
      type: string
    import_id:
      type: string
      format: uuid
      description: Correlates with DocumentationImportCompletedEvent
    document_count:
      type: integer
      description: Number of documents in this import batch
    import_source:
      type: string
      description: Provenance identifier (if provided)

DocumentationImportCompletedEvent:
  type: object
  required: [event_id, timestamp, namespace, import_id, imported_count, success]
  properties:
    event_id:
      type: string
      format: uuid
    timestamp:
      type: string
      format: date-time
    namespace:
      type: string
    import_id:
      type: string
      format: uuid
      description: Correlates with DocumentationImportStartedEvent
    success:
      type: boolean
      description: Whether import completed without fatal errors
    imported_count:
      type: integer
      description: Documents created
    updated_count:
      type: integer
      description: Documents overwritten
    skipped_count:
      type: integer
      description: Documents skipped due to conflicts
    error_count:
      type: integer
      description: Documents that failed
    import_source:
      type: string
      description: Provenance identifier (if provided)
    duration_ms:
      type: integer
      description: Total import duration in milliseconds
```

### Event Topics

| Event | Topic | Consumers |
|-------|-------|-----------|
| `DocumentationQueriedEvent` | `documentation.queried` | Analytics dashboard, gap detection (semantic search) |
| `DocumentationSearchedEvent` | `documentation.searched` | Analytics dashboard, search patterns (keyword search) |
| `DocumentationSuggestionsRequestedEvent` | `documentation.suggestions` | Analytics dashboard, exploration patterns |
| `DocumentViewedEvent` | `documentation.viewed` | Analytics dashboard, popularity tracking |
| `DocumentCreatedEvent` | `documentation.created` | Search indexing, analytics |
| `DocumentUpdatedEvent` | `documentation.updated` | Search re-indexing, cache invalidation |
| `DocumentDeletedEvent` | `documentation.deleted` | Search removal, cache cleanup |
| `DocumentationImportStartedEvent` | `documentation.import.started` | Progress tracking, notifications |
| `DocumentationImportCompletedEvent` | `documentation.import.completed` | Bulk re-indexing, notifications, alerting |

---

## Service Implementation Pattern

```csharp
[DaprService("documentation", typeof(IDocumentationService), lifetime: ServiceLifetime.Scoped)]
public class DocumentationService : IDocumentationService
{
    private const string REDIS_STORE = "documentation-statestore";
    private const string MYSQL_STORE = "documentation-mysql-statestore";
    private const string PUBSUB_NAME = "bannou-pubsub";

    private readonly DaprClient _daprClient;
    private readonly ILogger<DocumentationService> _logger;
    private readonly ISearchService _searchService;
    private readonly ISessionContextService _sessionService;
    private readonly DocumentationServiceConfiguration _configuration;

    public DocumentationService(
        DaprClient daprClient,
        ILogger<DocumentationService> logger,
        ISearchService searchService,
        ISessionContextService sessionService,
        DocumentationServiceConfiguration configuration)
    {
        _daprClient = daprClient;
        _logger = logger;
        _searchService = searchService;
        _sessionService = sessionService;
        _configuration = configuration;
    }

    public async Task<(StatusCodes, QueryDocumentationResponse?)> QueryDocumentationAsync(
        QueryDocumentationRequest body,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug("Querying documentation in {Namespace}: {Query}", body.Namespace, body.Query);

            // Perform full-text search with ranking
            var results = await _searchService.QuerySearchAsync(
                body.Query,
                body.Namespace,
                body.Category,
                body.MaxResults ?? 5,
                body.MinRelevanceScore ?? 0.3f,
                ct);

            // Build voice-friendly summary from top result
            var voiceSummary = results.FirstOrDefault()?.VoiceSummary
                ?? GenerateVoiceSummary(body.Query, results);

            // Generate follow-up suggestions
            var followups = GenerateFollowupSuggestions(body.Query, results);

            var response = new QueryDocumentationResponse
            {
                Results = results,
                Query = body.Query,
                TotalResults = results.Count,
                VoiceSummary = voiceSummary,
                SuggestedFollowups = followups
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query documentation: {Query}", body.Query);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, CreateDocumentResponse?)> CreateDocumentAsync(
        CreateDocumentRequest body,
        CancellationToken ct = default)
    {
        try
        {
            var documentId = Guid.NewGuid().ToString();
            var slug = body.Slug ?? GenerateSlug(body.Title);

            // Check for existing slug (scoped to namespace)
            var existingKey = $"slug-idx:{body.Namespace}:{slug}";
            var existing = await _daprClient.GetStateAsync<string>(REDIS_STORE, existingKey, ct);
            if (!string.IsNullOrEmpty(existing))
            {
                return (StatusCodes.Conflict, null);
            }

            var document = new DocumentModel
            {
                DocumentId = documentId,
                Slug = slug,
                Title = body.Title,
                Content = body.Content,
                Summary = body.Summary ?? GenerateSummary(body.Content),
                VoiceSummary = body.VoiceSummary ?? GenerateVoiceSummary(body.Title, body.Content),
                Category = body.Category,
                Tags = body.Tags ?? new List<string>(),
                RelatedDocuments = body.RelatedDocuments ?? new List<string>(),
                Metadata = body.Metadata ?? new Dictionary<string, object>(),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            // Save to MySQL (persistent)
            await _daprClient.SaveStateAsync(MYSQL_STORE, $"doc:{documentId}", document, ct);

            // Save slug index to Redis
            await _daprClient.SaveStateAsync(REDIS_STORE, existingKey, documentId, ct);

            // Index for search
            await _searchService.IndexDocumentAsync(document, ct);

            // Publish event
            await _daprClient.PublishEventAsync(
                PUBSUB_NAME,
                "documentation.created",
                new DocumentCreatedEvent
                {
                    EventId = Guid.NewGuid().ToString(),
                    Timestamp = DateTimeOffset.UtcNow,
                    DocumentId = documentId,
                    Title = document.Title,
                    Category = document.Category,
                    Tags = document.Tags
                },
                ct);

            return (StatusCodes.OK, new CreateDocumentResponse
            {
                DocumentId = documentId,
                Slug = slug
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create document: {Title}", body.Title);
            return (StatusCodes.InternalServerError, null);
        }
    }

    private string GenerateVoiceSummary(string query, List<DocumentResult> results)
    {
        if (!results.Any())
        {
            return $"I couldn't find specific documentation about {query}. Would you like me to search for a related topic?";
        }

        var top = results.First();
        return top.VoiceSummary ?? top.Summary ??
            $"I found information about {top.Title}. {TruncateForVoice(top.Content ?? "", 200)}";
    }

    private string GenerateVoiceSummary(string title, string content)
    {
        // Generate concise, speakable summary
        var firstParagraph = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? content;
        return TruncateForVoice(firstParagraph, 200);
    }

    private string TruncateForVoice(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;

        var truncated = text.Substring(0, maxLength);
        var lastSentence = truncated.LastIndexOfAny(new[] { '.', '!', '?' });
        if (lastSentence > maxLength / 2)
        {
            return truncated.Substring(0, lastSentence + 1);
        }
        return truncated + "...";
    }

    private List<string> GenerateFollowupSuggestions(string query, List<DocumentResult> results)
    {
        var suggestions = new List<string>();

        if (results.Count > 1)
        {
            suggestions.Add($"Would you like more details about {results[1].Title}?");
        }

        var categories = results.Select(r => r.Category).Distinct().Take(2);
        foreach (var cat in categories)
        {
            suggestions.Add($"Do you want to explore more {cat} documentation?");
        }

        return suggestions.Take(3).ToList();
    }

    private string GenerateSlug(string title)
    {
        return title.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("'", "")
            .Replace("\"", "");
    }

    private string GenerateSummary(string content)
    {
        // Extract first meaningful paragraph
        var paragraphs = content.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var firstContent = paragraphs
            .FirstOrDefault(p => !p.StartsWith("#") && p.Length > 50)
            ?? paragraphs.FirstOrDefault()
            ?? content;

        return firstContent.Length > 300
            ? firstContent.Substring(0, 297) + "..."
            : firstContent;
    }
}
```

---

## Configuration

```yaml
# x-service-configuration in documentation-api.yaml
x-service-configuration:
  properties:
    # Search Configuration
    MaxSearchResults:
      type: integer
      default: 20
      description: Maximum search results per query
    DefaultMinRelevanceScore:
      type: number
      default: 0.3
      description: Default minimum relevance score for queries (0.0-1.0)
    SearchCacheTTLSeconds:
      type: integer
      default: 300
      description: TTL for search result cache (0 = no cache)

    # Voice/Summary Configuration
    VoiceSummaryMaxLength:
      type: integer
      default: 200
      description: Maximum character length for voice summaries
    AutoGenerateSummaryMaxLength:
      type: integer
      default: 300
      description: Max length for auto-generated summaries (first paragraph extraction)

    # Trashcan Configuration
    TrashcanTTLDays:
      type: integer
      default: 7
      minimum: 1
      maximum: 90
      description: Days before trashcan items are permanently deleted

    # Import Configuration
    MaxDocumentsPerImport:
      type: integer
      default: 0
      description: Maximum documents per import request (0 = unlimited)
    MaxAutoRelationsPerImport:
      type: integer
      default: 10
      description: When make_related=true, max relations created per document

    # Session Configuration
    SessionTTLHours:
      type: integer
      default: 24
      description: Session context TTL in hours
    MaxRecentItemsPerSession:
      type: integer
      default: 10
      description: Max items in session recently_viewed and recent_queries lists

    # Future: AI Enhancement Configuration (disabled by default)
    # AIEnhancementsEnabled:
    #   type: boolean
    #   default: false
    #   description: Enable AI-powered search and summary generation
    # AIProvider:
    #   type: string
    #   enum: [openai, anthropic, local]
    #   description: AI provider for enhanced features (when enabled)
```

---

## Testing Strategy

### Unit Tests

```csharp
[Fact]
public async Task QueryDocumentation_WithValidQuery_ReturnsResults()
{
    // Arrange
    var mockSearchService = new Mock<ISearchService>();
    mockSearchService
        .Setup(s => s.QuerySearchAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),           // namespace
            It.IsAny<DocumentCategory?>(),
            It.IsAny<int>(),
            It.IsAny<float>(),            // minRelevanceScore
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<DocumentResult>
        {
            new DocumentResult
            {
                DocumentId = Guid.NewGuid().ToString(),
                Title = "Authentication Guide",
                Summary = "How to authenticate with Bannou services",
                RelevanceScore = 0.95f
            }
        });

    var service = CreateService(searchService: mockSearchService.Object);

    // Act
    var (status, response) = await service.QueryDocumentationAsync(
        new QueryDocumentationRequest { Query = "how to authenticate" });

    // Assert
    Assert.Equal(StatusCodes.OK, status);
    Assert.NotNull(response);
    Assert.Single(response.Results);
    Assert.NotEmpty(response.VoiceSummary);
}

[Fact]
public async Task CreateDocument_WithDuplicateSlug_ReturnsConflict()
{
    // Arrange
    var mockDaprClient = new Mock<DaprClient>();
    mockDaprClient
        .Setup(d => d.GetStateAsync<string>(
            It.IsAny<string>(),
            It.Is<string>(k => k.StartsWith("slug-idx:")),
            null,
            It.IsAny<CancellationToken>()))
        .ReturnsAsync("existing-document-id");

    var service = CreateService(daprClient: mockDaprClient.Object);

    // Act
    var (status, response) = await service.CreateDocumentAsync(
        new CreateDocumentRequest
        {
            Title = "Test Document",
            Content = "Test content",
            Category = DocumentCategory.Framework,
            Slug = "existing-slug"
        });

    // Assert
    Assert.Equal(StatusCodes.Conflict, status);
    Assert.Null(response);
}
```

### HTTP Integration Tests

```csharp
public class DocumentationTestHandler : IServiceTestHandler
{
    public async Task<TestResult> TestQueryDocumentation(IDocumentationClient client)
    {
        var request = new QueryDocumentationRequest
        {
            Query = "how does authentication work",
            MaxResults = 5
        };

        var (status, response) = await client.QueryDocumentationAsync(request);

        if (status != StatusCodes.OK)
            return TestResult.Failed($"Query failed with status {status}");

        if (response?.Results == null)
            return TestResult.Failed("No results returned");

        if (string.IsNullOrEmpty(response.VoiceSummary))
            return TestResult.Failed("Voice summary not generated");

        return TestResult.Successful();
    }

    public async Task<TestResult> TestCreateAndRetrieveDocument(IDocumentationClient client)
    {
        // Create
        var createRequest = new CreateDocumentRequest
        {
            Title = "Test Integration Document",
            Content = "This is a test document for integration testing.",
            Category = DocumentCategory.Tutorial,
            Tags = new List<string> { "test", "integration" }
        };

        var (createStatus, createResponse) = await client.CreateDocumentAsync(createRequest);
        if (createStatus != StatusCodes.OK)
            return TestResult.Failed($"Create failed: {createStatus}");

        // Retrieve
        var getRequest = new GetDocumentRequest
        {
            DocumentId = createResponse!.DocumentId
        };

        var (getStatus, getResponse) = await client.GetDocumentAsync(getRequest);
        if (getStatus != StatusCodes.OK)
            return TestResult.Failed($"Get failed: {getStatus}");

        if (getResponse?.Document?.Title != createRequest.Title)
            return TestResult.Failed("Retrieved document doesn't match created");

        // Cleanup
        await client.DeleteDocumentAsync(new DeleteDocumentRequest
        {
            DocumentId = createResponse.DocumentId
        });

        return TestResult.Successful();
    }
}
```

---

## Documentation Content Strategy

### Initial Content Categories

| Category | Content Examples |
|----------|------------------|
| `framework` | Bannou overview, architecture principles, plugin system |
| `service` | Auth, Accounts, Connect, Permissions, Behavior, Orchestrator docs |
| `api` | OpenAPI specs, endpoint reference, request/response examples |
| `deployment` | Docker Compose, Kubernetes, environment configuration |
| `architecture` | WebSocket protocol, Dapr integration, event-driven patterns |
| `tutorial` | Getting started, creating services, testing guides |
| `troubleshooting` | Common errors, debugging, performance issues |
| `game_design` | Guardian spirits, realms, character systems |
| `world_lore` | Omega, Arcadia, Fantasia world details |
| `npc_systems` | Behavior trees, GOAP, memory systems |
| `faq` | Common questions and quick answers |

### Repository Import System

The documentation plugin supports importing entire repositories like `arcadia-kb` through multiple methods:

#### Import Methods

| Method | Endpoint | Use Case |
|--------|----------|----------|
| **Directory Manifest** | `POST /documentation/import` | JSON array of documents |
| **ZIP Upload** | `POST /documentation/import-archive` | Upload compressed repository |
| **Git Repository** | `POST /documentation/import-repository` | Clone and import from Git URL |
| **Sync Command** | CLI tool | Local directory sync |

---

## Real-World Case Study: arcadia-kb Import

### Repository Analysis

```
~/repos/arcadia-kb/
├── 01 - Core Concepts/           # → category: core_concepts
├── 02 - World Lore/              # → category: world_lore
├── 03 - Character Systems/       # → category: character_systems
├── 04 - Game Systems/            # → category: game_systems
├── 05 - NPC AI Design/           # → category: npc_systems
├── 06 - Technical Architecture/  # → category: architecture
├── 07 - Implementation Guides/   # → category: tutorial
├── 08 - Crafting Systems/        # → category: crafting (40+ nested files)
├── 09 - Engine Research Archive/ # → category: research
├── Claude/                       # → category: framework (core memory files)
├── Templates/                    # → SKIP (templates, not content)
└── README.md                     # → category: framework
```

**139 total markdown files** with varying metadata formats.

### Metadata Extraction Strategy

The import system uses a **three-tier metadata extraction** approach:

#### Tier 1: Explicit Frontmatter (Highest Priority)

YAML frontmatter at the document start (standard for many static site generators):

```markdown
---
title: Distributed Agent Architecture
status: draft
category: npc_systems
tags:
  - npc-ai
  - distributed-systems
  - agent-architecture
related:
  - Dynamic World-Driven Systems
  - Bannou Integration
voice_summary: NPCs use a revolutionary avatar-agent separation where the avatar handles movement while a Dapr agent handles decision-making.
---

# Distributed Agent Architecture
...
```

#### Tier 2: Obsidian-Style Blockquote Header (arcadia-kb Pattern)

The current arcadia-kb template uses blockquote metadata:

```markdown
# Document Title

> **Status**: Draft
> **Last Updated**: 2025-01-05
> **Related Systems**: [[Dynamic World-Driven Systems]], [[Bannou Integration]]
> **Tags**: #npc-ai #distributed-systems #agent-architecture

## Content starts here...
```

**Parser extracts:**
- `Status` → `metadata.status`
- `Last Updated` → `updated_at`
- `Related Systems` → `related_documents` (resolve `[[...]]` to document IDs)
- `Tags` → `tags` (strip `#` prefix)

#### Tier 3: Automatic Inference (Fallback)

When no explicit metadata exists:

| Field | Inference Method |
|-------|------------------|
| `title` | First `# Heading` or filename without extension |
| `slug` | Sanitized file path relative to root |
| `category` | Parent directory name mapping |
| `tags` | Extract hashtags from content + heading keywords |
| `summary` | First paragraph after title |
| `voice_summary` | First 200 chars of summary, sentence-bounded |
| `related_documents` | Parse `[[...]]` Obsidian links |

### Directory-to-Category Mapping

```yaml
# Import configuration: directory_mapping.yaml
directory_mappings:
  "01 - Core Concepts": core_concepts
  "02 - World Lore": world_lore
  "03 - Character Systems": character_systems
  "04 - Game Systems": game_systems
  "05 - NPC AI Design": npc_systems
  "06 - Technical Architecture": architecture
  "07 - Implementation Guides": tutorial
  "08 - Crafting Systems": crafting
  "09 - Engine Research Archive": research
  "Claude": framework
  "Templates": _skip  # Special: skip this directory

# Nested directories inherit parent category
# "08 - Crafting Systems/02 - Metallurgy and Smithing/" → crafting

# Files at root level
root_category: framework

# Tag extraction from directory names
directory_tags:
  "08 - Crafting Systems": ["crafting", "game-mechanics"]
  "05 - NPC AI Design": ["npc", "ai", "behavior"]
```

---

## Enhanced Import API

### Import Archive Endpoint

```yaml
/documentation/import-archive:
  post:
    operationId: ImportArchive
    summary: Import documentation from ZIP archive
    description: |
      Upload a ZIP file containing markdown documentation.
      Supports nested directories with automatic category mapping.
    x-permissions:
      - role: admin
        states: {}
    requestBody:
      required: true
      content:
        multipart/form-data:
          schema:
            type: object
            properties:
              namespace:
                type: string
                pattern: '^[a-z0-9-]+$'
                description: Target namespace for imported documents
              archive:
                type: string
                format: binary
                description: ZIP file containing markdown documents
              config:
                type: string
                description: JSON configuration for import (optional)
            required:
              - namespace
              - archive
    responses:
      '200':
        description: Import results
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ImportRepositoryResponse'
```

### Import Repository Endpoint

```yaml
/documentation/import-repository:
  post:
    operationId: ImportRepository
    summary: Import documentation from Git repository
    description: |
      Clone a Git repository and import all markdown files.
      Supports branch selection and path filtering.
    x-permissions:
      - role: admin
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/ImportRepositoryRequest'
    responses:
      '200':
        description: Import results
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ImportRepositoryResponse'
```

### Enhanced Schema Models

```yaml
ImportRepositoryRequest:
  type: object
  required:
    - namespace
    - repository_url
  properties:
    namespace:
      $ref: '#/components/schemas/Namespace'
    repository_url:
      type: string
      format: uri
      description: Git repository URL (HTTPS or SSH)
      example: "https://github.com/user/arcadia-kb.git"
    branch:
      type: string
      description: Branch to import from
      default: "main"
    path_filter:
      type: string
      description: Only import files matching this glob pattern
      example: "**/*.md"
    exclude_patterns:
      type: array
      items:
        type: string
      description: Patterns to exclude
      default: ["Templates/**", ".obsidian/**", "*.template.md"]
    directory_mappings:
      type: object
      additionalProperties:
        type: string
      description: Directory name to category mapping overrides
    overwrite_existing:
      type: boolean
      default: false
    generate_embeddings:
      type: boolean
      description: Generate search embeddings during import
      default: true
    dry_run:
      type: boolean
      description: Validate import without persisting
      default: false

ImportRepositoryResponse:
  type: object
  required:
    - namespace
  properties:
    namespace:
      $ref: '#/components/schemas/Namespace'
    job_id:
      type: string
      format: uuid
      description: Async job ID for large imports
    status:
      type: string
      enum: [completed, processing, failed]
    imported_count:
      type: integer
    updated_count:
      type: integer
    skipped_count:
      type: integer
    errors:
      type: array
      items:
        $ref: '#/components/schemas/ImportError'
    warnings:
      type: array
      items:
        $ref: '#/components/schemas/ImportWarning'
    documents:
      type: array
      items:
        $ref: '#/components/schemas/ImportedDocumentSummary'
      description: Summary of imported documents (when not dry_run)

ImportWarning:
  type: object
  properties:
    file_path:
      type: string
    warning_type:
      type: string
      enum: [missing_metadata, unresolved_link, large_file, encoding_issue]
    message:
      type: string
    suggestion:
      type: string

ImportedDocumentSummary:
  type: object
  properties:
    document_id:
      type: string
      format: uuid
    slug:
      type: string
    title:
      type: string
    source_path:
      type: string
    category:
      $ref: '#/components/schemas/DocumentCategory'
    tags:
      type: array
      items:
        type: string
    metadata_source:
      type: string
      enum: [frontmatter, blockquote, inferred]
      description: How metadata was extracted

ImportConfigRequest:
  type: object
  description: Configuration for import processing
  properties:
    directory_mappings:
      type: object
      additionalProperties:
        type: string
      description: |
        Map directory names to categories.
        Example: {"01 - Core Concepts": "core_concepts"}
    root_category:
      $ref: '#/components/schemas/DocumentCategory'
      description: Category for files at repository root
      default: framework
    skip_directories:
      type: array
      items:
        type: string
      description: Directories to skip entirely
      default: ["Templates", ".obsidian", ".git", "node_modules"]
    tag_extraction:
      $ref: '#/components/schemas/TagExtractionConfig'
    link_resolution:
      $ref: '#/components/schemas/LinkResolutionConfig'
    voice_summary_generation:
      $ref: '#/components/schemas/VoiceSummaryConfig'

TagExtractionConfig:
  type: object
  properties:
    extract_hashtags:
      type: boolean
      description: Extract #tags from document content
      default: true
    extract_from_headings:
      type: boolean
      description: Extract keywords from ## headings
      default: true
    max_auto_tags:
      type: integer
      description: Maximum auto-generated tags per document
      default: 10
    directory_tags:
      type: object
      additionalProperties:
        type: array
        items:
          type: string
      description: |
        Additional tags to apply based on directory.
        Example: {"08 - Crafting Systems": ["crafting", "game-mechanics"]}

LinkResolutionConfig:
  type: object
  properties:
    resolve_obsidian_links:
      type: boolean
      description: Convert [[Document Name]] to document IDs
      default: true
    create_bidirectional:
      type: boolean
      description: Create reverse links (A links to B → B related to A)
      default: true
    unresolved_link_behavior:
      type: string
      enum: [warn, skip, error]
      description: How to handle links to non-existent documents
      default: warn

VoiceSummaryConfig:
  type: object
  properties:
    auto_generate:
      type: boolean
      description: Auto-generate voice summaries when missing
      default: true
    max_length:
      type: integer
      description: Maximum voice summary length
      default: 200
    prefer_first_paragraph:
      type: boolean
      description: Use first paragraph as voice summary base
      default: true
    use_ai_generation:
      type: boolean
      description: Use AI to generate voice summaries (requires API key)
      default: false
```

---

## Markdown Parser Implementation

### Metadata Extraction Pipeline

```csharp
public class MarkdownDocumentParser
{
    public ParsedDocument Parse(string filePath, string content, ImportConfig config)
    {
        var result = new ParsedDocument
        {
            SourcePath = filePath,
            RawContent = content
        };

        // Step 1: Try YAML frontmatter
        if (TryExtractFrontmatter(content, out var frontmatter, out var contentWithoutFrontmatter))
        {
            result.MetadataSource = MetadataSource.Frontmatter;
            ApplyFrontmatter(result, frontmatter);
            content = contentWithoutFrontmatter;
        }
        // Step 2: Try Obsidian blockquote header
        else if (TryExtractBlockquoteHeader(content, out var blockquote, out var contentWithoutBlockquote))
        {
            result.MetadataSource = MetadataSource.Blockquote;
            ApplyBlockquoteMetadata(result, blockquote);
            content = contentWithoutBlockquote;
        }
        // Step 3: Infer metadata
        else
        {
            result.MetadataSource = MetadataSource.Inferred;
        }

        // Always apply inferences for missing fields
        InferMissingMetadata(result, filePath, content, config);

        // Extract Obsidian links for relationship mapping
        result.ObsidianLinks = ExtractObsidianLinks(content);

        // Extract hashtags from content
        if (config.TagExtraction.ExtractHashtags)
        {
            var contentTags = ExtractHashtags(content);
            result.Tags = result.Tags.Union(contentTags).Distinct().ToList();
        }

        // Store processed content (without metadata blocks)
        result.Content = content;

        return result;
    }

    private bool TryExtractFrontmatter(string content, out Dictionary<string, object> frontmatter, out string remaining)
    {
        frontmatter = null;
        remaining = content;

        if (!content.StartsWith("---"))
            return false;

        var endIndex = content.IndexOf("---", 3);
        if (endIndex < 0)
            return false;

        var yaml = content.Substring(3, endIndex - 3).Trim();
        try
        {
            var deserializer = new YamlDotNet.Serialization.Deserializer();
            frontmatter = deserializer.Deserialize<Dictionary<string, object>>(yaml);
            remaining = content.Substring(endIndex + 3).TrimStart();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryExtractBlockquoteHeader(string content, out BlockquoteMetadata metadata, out string remaining)
    {
        metadata = null;
        remaining = content;

        // Pattern: Lines starting with > after the first heading
        var lines = content.Split('\n');
        var blockquoteStart = -1;
        var blockquoteEnd = -1;
        var foundHeading = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // Skip empty lines at start
            if (string.IsNullOrEmpty(line) && !foundHeading)
                continue;

            // First heading
            if (line.StartsWith("# ") && !foundHeading)
            {
                foundHeading = true;
                continue;
            }

            // Start of blockquote after heading
            if (foundHeading && line.StartsWith(">") && blockquoteStart < 0)
            {
                blockquoteStart = i;
            }
            // Continue blockquote
            else if (blockquoteStart >= 0 && (line.StartsWith(">") || string.IsNullOrEmpty(line)))
            {
                if (line.StartsWith(">"))
                    blockquoteEnd = i;
            }
            // End of blockquote
            else if (blockquoteStart >= 0)
            {
                break;
            }
        }

        if (blockquoteStart < 0)
            return false;

        // Parse blockquote content
        var blockquoteLines = lines
            .Skip(blockquoteStart)
            .Take(blockquoteEnd - blockquoteStart + 1)
            .Select(l => l.TrimStart('>', ' '))
            .ToList();

        metadata = ParseBlockquoteMetadata(blockquoteLines);

        // Reconstruct content without blockquote
        var beforeBlockquote = string.Join('\n', lines.Take(blockquoteStart));
        var afterBlockquote = string.Join('\n', lines.Skip(blockquoteEnd + 1));
        remaining = beforeBlockquote + "\n" + afterBlockquote;

        return metadata != null;
    }

    private BlockquoteMetadata ParseBlockquoteMetadata(List<string> lines)
    {
        var result = new BlockquoteMetadata();
        var fullText = string.Join(" ", lines);

        // Parse: **Status**: Value
        var statusMatch = Regex.Match(fullText, @"\*\*Status\*\*:\s*([^\*\n]+)");
        if (statusMatch.Success)
            result.Status = statusMatch.Groups[1].Value.Trim();

        // Parse: **Last Updated**: Value
        var updatedMatch = Regex.Match(fullText, @"\*\*Last Updated\*\*:\s*(\d{4}-\d{2}-\d{2})");
        if (updatedMatch.Success)
            result.LastUpdated = DateTime.Parse(updatedMatch.Groups[1].Value);

        // Parse: **Related Systems**: [[Link1]], [[Link2]]
        var relatedMatches = Regex.Matches(fullText, @"\[\[([^\]]+)\]\]");
        result.RelatedSystems = relatedMatches.Cast<Match>().Select(m => m.Groups[1].Value).ToList();

        // Parse: **Tags**: #tag1 #tag2 #tag3
        var tagsMatch = Regex.Match(fullText, @"\*\*Tags\*\*:\s*([^\n]+)");
        if (tagsMatch.Success)
        {
            var tagMatches = Regex.Matches(tagsMatch.Groups[1].Value, @"#([\w-]+)");
            result.Tags = tagMatches.Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        }

        return result;
    }

    private void InferMissingMetadata(ParsedDocument doc, string filePath, string content, ImportConfig config)
    {
        // Title: first heading or filename
        if (string.IsNullOrEmpty(doc.Title))
        {
            var headingMatch = Regex.Match(content, @"^#\s+(.+)$", RegexOptions.Multiline);
            doc.Title = headingMatch.Success
                ? headingMatch.Groups[1].Value.Trim()
                : Path.GetFileNameWithoutExtension(filePath).Replace("-", " ");
        }

        // Slug: sanitized path
        if (string.IsNullOrEmpty(doc.Slug))
        {
            doc.Slug = GenerateSlugFromPath(filePath);
        }

        // Category: from directory mapping
        if (doc.Category == null)
        {
            doc.Category = InferCategoryFromPath(filePath, config);
        }

        // Summary: first meaningful paragraph
        if (string.IsNullOrEmpty(doc.Summary))
        {
            doc.Summary = ExtractFirstParagraph(content);
        }

        // Voice summary: truncated summary
        if (string.IsNullOrEmpty(doc.VoiceSummary))
        {
            doc.VoiceSummary = GenerateVoiceSummary(doc.Title, doc.Summary, config.VoiceSummary.MaxLength);
        }

        // Tags: from headings if enabled
        if (config.TagExtraction.ExtractFromHeadings)
        {
            var headingTags = ExtractTagsFromHeadings(content, config.TagExtraction.MaxAutoTags);
            doc.Tags = doc.Tags.Union(headingTags).Distinct().Take(config.TagExtraction.MaxAutoTags).ToList();
        }

        // Directory-based tags
        var dirTags = GetDirectoryTags(filePath, config);
        doc.Tags = doc.Tags.Union(dirTags).Distinct().ToList();
    }

    private DocumentCategory InferCategoryFromPath(string filePath, ImportConfig config)
    {
        var parts = filePath.Split(Path.DirectorySeparatorChar, '/');

        foreach (var part in parts)
        {
            if (config.DirectoryMappings.TryGetValue(part, out var category))
            {
                if (category == "_skip")
                    return null; // Signal to skip this file

                return Enum.Parse<DocumentCategory>(category, ignoreCase: true);
            }
        }

        return config.RootCategory;
    }

    private List<string> ExtractObsidianLinks(string content)
    {
        var matches = Regex.Matches(content, @"\[\[([^\]|]+)(?:\|[^\]]+)?\]\]");
        return matches.Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
    }

    private List<string> ExtractHashtags(string content)
    {
        // Match #tag but not inside code blocks or URLs
        var matches = Regex.Matches(content, @"(?<![`\w/])#([\w-]+)(?![`\w])");
        return matches.Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
    }

    private List<string> ExtractTagsFromHeadings(string content, int maxTags)
    {
        var headings = Regex.Matches(content, @"^#{2,3}\s+(.+)$", RegexOptions.Multiline);
        var keywords = new List<string>();

        foreach (Match heading in headings)
        {
            var text = heading.Groups[1].Value.ToLowerInvariant();
            // Extract significant words (skip common words)
            var words = Regex.Matches(text, @"\b(\w{4,})\b")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .Where(w => !CommonWords.Contains(w));
            keywords.AddRange(words);
        }

        return keywords.Distinct().Take(maxTags).ToList();
    }

    private static readonly HashSet<string> CommonWords = new()
    {
        "this", "that", "with", "from", "have", "will", "what", "when", "where",
        "which", "while", "about", "after", "before", "between", "through",
        "overview", "introduction", "summary", "details", "implementation"
    };
}
```

---

## Import CLI Tool

For local development and CI/CD integration:

```bash
# scripts/import-documentation.sh

#!/bin/bash
set -e

BANNOU_URL="${BANNOU_URL:-http://localhost:5012}"
ADMIN_TOKEN="${ADMIN_TOKEN:-}"

usage() {
    echo "Usage: $0 <command> [options]"
    echo ""
    echo "Commands:"
    echo "  sync <directory>     Sync local directory to documentation service"
    echo "  upload <zipfile>     Upload ZIP archive"
    echo "  clone <git-url>      Import from Git repository"
    echo ""
    echo "Options:"
    echo "  --dry-run            Validate without importing"
    echo "  --overwrite          Overwrite existing documents"
    echo "  --config <file>      Use custom import configuration"
    echo "  --branch <branch>    Git branch (for clone command)"
    exit 1
}

sync_directory() {
    local dir="$1"
    local dry_run="${2:-false}"
    local overwrite="${3:-false}"
    local config="${4:-}"

    echo "Scanning $dir for markdown files..."

    # Build JSON manifest from directory
    local manifest=$(mktemp)
    echo '{"documents": [' > "$manifest"

    local first=true
    while IFS= read -r -d '' file; do
        # Skip templates and hidden directories
        if [[ "$file" == *"/Templates/"* ]] || [[ "$file" == */.*/* ]]; then
            continue
        fi

        local relative_path="${file#$dir/}"
        local content=$(cat "$file" | jq -Rs .)
        local title=$(basename "$file" .md | sed 's/-/ /g')

        if [ "$first" = true ]; then
            first=false
        else
            echo "," >> "$manifest"
        fi

        cat >> "$manifest" << EOF
{
  "source_path": "$relative_path",
  "content": $content
}
EOF
    done < <(find "$dir" -name "*.md" -type f -print0)

    echo '],' >> "$manifest"
    echo "\"overwrite_existing\": $overwrite," >> "$manifest"
    echo "\"dry_run\": $dry_run" >> "$manifest"

    # Add config if provided
    if [ -n "$config" ] && [ -f "$config" ]; then
        local config_json=$(cat "$config")
        echo ", \"config\": $config_json" >> "$manifest"
    fi

    echo '}' >> "$manifest"

    echo "Importing $(grep -c '"source_path"' "$manifest") documents..."

    # Send to API
    curl -X POST "$BANNOU_URL/v1.0/invoke/bannou/method/documentation/import" \
        -H "Content-Type: application/json" \
        -H "Authorization: Bearer $ADMIN_TOKEN" \
        -d @"$manifest"

    rm "$manifest"
}

# Example: Sync arcadia-kb
# ./import-documentation.sh sync ~/repos/arcadia-kb --config arcadia-kb-config.yaml
```

### Example Import Configuration for arcadia-kb

```yaml
# arcadia-kb-import-config.yaml

directory_mappings:
  "01 - Core Concepts": "core_concepts"
  "02 - World Lore": "world_lore"
  "03 - Character Systems": "character_systems"
  "04 - Game Systems": "game_systems"
  "05 - NPC AI Design": "npc_systems"
  "06 - Technical Architecture": "architecture"
  "07 - Implementation Guides": "tutorial"
  "08 - Crafting Systems": "crafting"
  "09 - Engine Research Archive": "research"
  "Claude": "framework"

skip_directories:
  - "Templates"
  - ".obsidian"
  - ".git"
  - ".claude"

root_category: "framework"

tag_extraction:
  extract_hashtags: true
  extract_from_headings: true
  max_auto_tags: 10
  directory_tags:
    "08 - Crafting Systems":
      - "crafting"
      - "game-mechanics"
      - "simulation"
    "05 - NPC AI Design":
      - "npc"
      - "ai"
      - "behavior"
      - "agent"
    "03 - Character Systems":
      - "character"
      - "player"
      - "progression"

link_resolution:
  resolve_obsidian_links: true
  create_bidirectional: true
  unresolved_link_behavior: "warn"

voice_summary:
  auto_generate: true
  max_length: 200
  prefer_first_paragraph: true
```

---

## Import Workflow for arcadia-kb

### Step 1: Dry Run Validation

```bash
./scripts/import-documentation.sh sync ~/repos/arcadia-kb \
    --config arcadia-kb-import-config.yaml \
    --dry-run
```

**Expected Output:**
```json
{
  "status": "validated",
  "documents_found": 139,
  "documents_valid": 135,
  "warnings": [
    {
      "file_path": "08 - Crafting Systems/Extraction.md",
      "warning_type": "missing_metadata",
      "message": "No explicit metadata found, using inferred values",
      "suggestion": "Add frontmatter or blockquote header for better search quality"
    },
    {
      "file_path": "05 - NPC AI Design/Distributed Agent Architecture.md",
      "warning_type": "unresolved_link",
      "message": "Link [[Dynamic World-Driven Systems]] not found in import set",
      "suggestion": "Ensure linked document exists or will be imported"
    }
  ],
  "category_distribution": {
    "crafting": 47,
    "architecture": 12,
    "world_lore": 8,
    "npc_systems": 15,
    "game_systems": 11,
    "character_systems": 9,
    "tutorial": 14,
    "framework": 18,
    "research": 5
  },
  "metadata_sources": {
    "frontmatter": 0,
    "blockquote": 23,
    "inferred": 112
  }
}
```

### Step 2: Full Import

```bash
./scripts/import-documentation.sh sync ~/repos/arcadia-kb \
    --config arcadia-kb-import-config.yaml \
    --overwrite
```

### Step 3: Verify Import

```bash
# Query to verify documentation is searchable
curl -X POST "$BANNOU_URL/v1.0/invoke/bannou/method/documentation/query" \
    -H "Content-Type: application/json" \
    -d '{"query": "how does NPC memory work", "max_results": 3}'
```

---

## Recommended Metadata Standard for New Documents

For best search quality, adopt this frontmatter format for new documents:

```markdown
---
title: Document Title Here
status: draft | review | complete
category: npc_systems
tags:
  - primary-tag
  - secondary-tag
  - related-concept
related:
  - Other Document Title
  - Another Related Doc
voice_summary: |
  One to two sentence summary optimized for voice AI.
  Should be conversational and under 200 characters.
---

# Document Title Here

Content starts here...
```

### Migration Script for Existing Documents

```bash
# scripts/add-frontmatter.sh
# Converts blockquote metadata to frontmatter format

#!/bin/bash
for file in "$@"; do
    # Extract existing blockquote metadata and convert to frontmatter
    # ... implementation details ...
done
```

---

## Security Considerations

### Public Read Access

- Query, search, and list endpoints are accessible without authentication (`role: anonymous`)
- This enables AI agents to query documentation without managing auth tokens

### Admin-Only Write Access

- Create, update, delete, and import require `role: admin`
- Prevents unauthorized documentation modification

### Rate Limiting

```csharp
// Apply rate limiting via middleware or Dapr configuration
// Recommended limits:
// - Query/Search: 60 requests/minute per IP
// - Admin operations: 10 requests/minute per token
```

### Input Validation

- All queries sanitized to prevent injection
- Content size limits enforced (max 1MB per document)
- Slug format validation (alphanumeric with hyphens only)

---

## Future Enhancements

### Phase 2: Advanced Features

1. **Vector Database Integration**: Replace Redis embeddings with dedicated vector DB (Qdrant, Pinecone)
2. **Multi-Language Support**: Documentation in multiple languages with automatic routing
3. **Version History**: Track document changes over time
4. **Analytics**: Track popular queries and documentation gaps
5. **Auto-Generation**: Generate API docs directly from OpenAPI schemas

### Phase 3: AI Enhancements

1. **RAG Integration**: Full Retrieval-Augmented Generation pipeline
2. **Contextual Answers**: Generate answers from multiple documents
3. **Query Understanding**: Better intent classification for queries
4. **Feedback Loop**: Learn from AI agent success/failure patterns

---

## References

- [Bannou TENETS.md](../TENETS.md) - Development tenets
- [API-DESIGN.md](../../arcadia-kb/06%20-%20Technical%20Architecture/API-DESIGN.md) - Schema-first patterns
- [OpenAI Function Calling](https://platform.openai.com/docs/guides/function-calling)
- [Claude Tool Use](https://docs.anthropic.com/en/docs/agents-and-tools/tool-use/overview)
- [SignalWire SWAIG](https://developer.signalwire.com/swml/guides/ai/swaig/)
