# Save-Load Plugin Design Document

> **Status**: Planning
> **Version**: 1.0
> **Created**: 2025-01-11
> **Author**: Claude Code

## Executive Summary

The Save-Load plugin (`lib-save-load`) provides a generic, game-engine-agnostic save/load system for Bannou services. It stores runtime data that **belongs to** or **is about** entities (accounts, characters, sessions, realms) without direct knowledge of those entity services.

### Key Design Principles

1. **Pure Storage Service**: Other services and clients make API requests into save-load; it does not subscribe to entity events
2. **Polymorphic Ownership**: Saves can be owned by any entity type (Account, Character, Session, Realm)
3. **Hybrid Storage**: Redis for hot/active saves, Asset service for versioned backups
4. **Slot-Based Organization**: Named slots with auto-slots, slots auto-created but can be pre-configured
5. **Versioned Schemas**: Supports save data migration across game updates

---

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Ownership Model | Polymorphic | Maximum flexibility - any entity type can own saves |
| Storage Backend | Hybrid (Redis + Asset) | Fast access for active saves, durable versioning for backups |
| Save Categories | First-class with behaviors | Predefined semantics for QuickSave, AutoSave, ManualSave, Checkpoint, StateSnapshot |
| Versioning | Hybrid rolling + pinnable | Rolling cleanup by default, ability to pin important versions |
| Integration | API-driven only | No event subscriptions; pure storage service pattern |
| Access Control | Owner + Admin | Owners have full access, admins can manage all saves |
| Schema Migration | Versioned with handlers | Plugin supports migration handlers for save schema evolution |
| Size Limits | Configurable defaults | 100MB max, auto-compress >1MB, configurable per deployment |
| Slot Model | Hybrid auto-create + pre-config | Slots auto-created on first save, but can be pre-configured with settings |
| SDK Scope | Full CRUD + Query | Complete operations from initial release |

---

## Architecture Overview

### Service Dependencies

```
lib-save-load
├── bannou-service (core types, StatusCodes, BannouJson)
├── lib-state (Redis/MySQL state stores)
├── lib-messaging (event publishing only - no subscriptions)
├── lib-asset (versioned blob storage via MinIO)
└── NO direct dependencies on entity services (lib-character, lib-game-session, lib-realm)
```

### Data Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              CLIENT / OTHER SERVICES                         │
└─────────────────────────────────────────────────────────────────────────────┘
                                        │
                                        ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                              SAVE-LOAD SERVICE                               │
├─────────────────────────────────────────────────────────────────────────────┤
│  Endpoints:                                                                  │
│  - /save-load/slot/create     Create/configure a save slot                  │
│  - /save-load/slot/get        Get slot metadata                             │
│  - /save-load/slot/list       List slots for owner                          │
│  - /save-load/slot/delete     Delete slot and all versions                  │
│  - /save-load/save            Save data to slot (creates version)           │
│  - /save-load/load            Load data from slot (latest or specific)      │
│  - /save-load/version/list    List versions in slot                         │
│  - /save-load/version/pin     Pin a version as checkpoint                   │
│  - /save-load/version/delete  Delete specific version                       │
│  - /save-load/query           Query saves with filters                      │
│  - /save-load/migrate         Migrate save to new schema version            │
└─────────────────────────────────────────────────────────────────────────────┘
                │                                    │
                ▼                                    ▼
┌───────────────────────────┐        ┌───────────────────────────────────────┐
│       LIB-STATE           │        │            LIB-ASSET                  │
│  (Redis + MySQL)          │        │         (MinIO via Asset API)         │
├───────────────────────────┤        ├───────────────────────────────────────┤
│  Slot Metadata Store      │        │  Versioned Save Blobs                 │
│  - Slot configuration     │        │  - Compressed save data               │
│  - Version manifests      │        │  - Content hashes                     │
│  - Access control         │        │  - Rolling + pinned versions          │
│                           │        │                                       │
│  Hot Save Cache (Redis)   │        │  Backup Archive                       │
│  - Latest versions        │        │  - Long-term retention                │
│  - Quick load path        │        │  - Cross-region replication           │
└───────────────────────────┘        └───────────────────────────────────────┘
```

### Core Concepts

#### 1. Save Slots

A **slot** is a named container for save versions owned by an entity. Slots have:
- **Owner**: EntityType + EntityId (polymorphic association)
- **Name**: Unique per owner (e.g., "autosave", "quicksave", "manual-1", "before-boss")
- **Category**: First-class save category (determines behavior)
- **Configuration**: Max versions, retention policy, compression settings
- **Metadata**: Custom key-value pairs for game-specific data

#### 2. Save Categories

| Category | Behavior | Default Max Versions | Auto-Cleanup |
|----------|----------|---------------------|--------------|
| `QuickSave` | Single-slot fast save, overwritten frequently | 1 | Yes |
| `AutoSave` | System-triggered periodic saves | 5 | Yes, rolling |
| `ManualSave` | User-initiated named saves | 10 | No |
| `Checkpoint` | Progress markers (level complete, etc.) | 20 | Yes, rolling |
| `StateSnapshot` | Full state captures for debugging/backup | 3 | Yes, rolling |

#### 3. Save Versions

Each save to a slot creates a new **version**. Versions have:
- **Version Number**: Monotonically increasing within slot
- **Asset ID**: Reference to blob in lib-asset
- **Schema Version**: For migration tracking
- **Content Hash**: SHA-256 for integrity verification
- **Pinned**: If true, excluded from rolling cleanup
- **Checkpoint Name**: Optional name for pinned versions
- **Metadata**: Timestamp, size, compression ratio, custom data

#### 4. Ownership Model

Uses FOUNDATION TENETS polymorphic association pattern:

```yaml
OwnerType:
  type: string
  enum: [ACCOUNT, CHARACTER, SESSION, REALM]
  description: Type of entity that owns this save slot

SaveSlot:
  properties:
    ownerId:
      type: string
      format: uuid
      description: ID of the owning entity
    ownerType:
      $ref: '#/components/schemas/OwnerType'
      description: Type of the owning entity
```

---

## Schema Design

### API Schema (`schemas/save-load-api.yaml`)

```yaml
openapi: 3.0.3
info:
  title: Save-Load Service API
  version: 1.0.0
  description: |
    Generic save/load system for game state persistence.
    Supports polymorphic ownership, versioned saves, and schema migration.

servers:
  - url: http://localhost:5012

tags:
  - name: Slots
    description: Save slot management
  - name: Saves
    description: Save/load operations
  - name: Versions
    description: Version management
  - name: Query
    description: Search and filtering
  - name: Migration
    description: Schema migration operations

paths:
  # ═══════════════════════════════════════════════════════════════════════════
  # SLOT MANAGEMENT
  # ═══════════════════════════════════════════════════════════════════════════

  /save-load/slot/create:
    post:
      tags: [Slots]
      operationId: CreateSlot
      summary: Create or configure a save slot
      description: |
        Creates a new save slot or updates configuration of an existing slot.
        Slots are auto-created on first save, but pre-creation allows setting
        custom configuration (max versions, retention policy, etc.).
      x-permissions:
        - role: user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CreateSlotRequest'
      responses:
        '200':
          description: Slot created or updated
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SlotResponse'
        '400':
          description: Invalid request
        '409':
          description: Slot already exists with different owner

  /save-load/slot/get:
    post:
      tags: [Slots]
      operationId: GetSlot
      summary: Get slot metadata
      description: Returns slot configuration and version summary.
      x-permissions:
        - role: user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetSlotRequest'
      responses:
        '200':
          description: Slot found
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SlotResponse'
        '404':
          description: Slot not found

  /save-load/slot/list:
    post:
      tags: [Slots]
      operationId: ListSlots
      summary: List slots for owner
      description: Returns all slots owned by the specified entity.
      x-permissions:
        - role: user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListSlotsRequest'
      responses:
        '200':
          description: Slots listed
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ListSlotsResponse'

  /save-load/slot/delete:
    post:
      tags: [Slots]
      operationId: DeleteSlot
      summary: Delete slot and all versions
      description: |
        Permanently deletes a slot and all save versions within it.
        This is irreversible. Requires owner access or admin role.
      x-permissions:
        - role: user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/DeleteSlotRequest'
      responses:
        '200':
          description: Slot deleted
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/DeleteSlotResponse'
        '404':
          description: Slot not found
        '403':
          description: Not authorized to delete this slot

  # ═══════════════════════════════════════════════════════════════════════════
  # SAVE/LOAD OPERATIONS
  # ═══════════════════════════════════════════════════════════════════════════

  /save-load/save:
    post:
      tags: [Saves]
      operationId: Save
      summary: Save data to slot
      description: |
        Creates a new version in the specified slot with the provided data.
        If the slot doesn't exist, it's auto-created with default configuration.

        Large saves (>1MB by default) are automatically compressed.
        Rolling version cleanup is applied based on slot configuration.
      x-permissions:
        - role: user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/SaveRequest'
      responses:
        '200':
          description: Save successful
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SaveResponse'
        '400':
          description: Invalid request or data validation failed
        '413':
          description: Save data exceeds maximum size limit
        '403':
          description: Not authorized to save to this slot

  /save-load/load:
    post:
      tags: [Saves]
      operationId: Load
      summary: Load data from slot
      description: |
        Retrieves save data from the specified slot. By default, loads the
        latest version. Optionally specify a version number or checkpoint name.
      x-permissions:
        - role: user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/LoadRequest'
      responses:
        '200':
          description: Load successful
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/LoadResponse'
        '404':
          description: Slot or version not found
        '403':
          description: Not authorized to load from this slot

  # ═══════════════════════════════════════════════════════════════════════════
  # VERSION MANAGEMENT
  # ═══════════════════════════════════════════════════════════════════════════

  /save-load/version/list:
    post:
      tags: [Versions]
      operationId: ListVersions
      summary: List versions in slot
      description: Returns all versions in a slot with metadata.
      x-permissions:
        - role: user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListVersionsRequest'
      responses:
        '200':
          description: Versions listed
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ListVersionsResponse'
        '404':
          description: Slot not found

  /save-load/version/pin:
    post:
      tags: [Versions]
      operationId: PinVersion
      summary: Pin a version as checkpoint
      description: |
        Pins a specific version, excluding it from rolling cleanup.
        Optionally assigns a checkpoint name for easy retrieval.
      x-permissions:
        - role: user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/PinVersionRequest'
      responses:
        '200':
          description: Version pinned
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/VersionResponse'
        '404':
          description: Slot or version not found

  /save-load/version/unpin:
    post:
      tags: [Versions]
      operationId: UnpinVersion
      summary: Unpin a version
      description: Removes pin from a version, making it eligible for rolling cleanup.
      x-permissions:
        - role: user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/UnpinVersionRequest'
      responses:
        '200':
          description: Version unpinned
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/VersionResponse'
        '404':
          description: Slot or version not found

  /save-load/version/delete:
    post:
      tags: [Versions]
      operationId: DeleteVersion
      summary: Delete specific version
      description: |
        Permanently deletes a specific version from a slot.
        Cannot delete pinned versions; unpin first.
      x-permissions:
        - role: user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/DeleteVersionRequest'
      responses:
        '200':
          description: Version deleted
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/DeleteVersionResponse'
        '404':
          description: Slot or version not found
        '409':
          description: Cannot delete pinned version

  # ═══════════════════════════════════════════════════════════════════════════
  # QUERY & SEARCH
  # ═══════════════════════════════════════════════════════════════════════════

  /save-load/query:
    post:
      tags: [Query]
      operationId: QuerySaves
      summary: Query saves with filters
      description: |
        Search and filter saves across slots. Supports filtering by owner,
        category, date range, metadata, and more. Paginated results.
      x-permissions:
        - role: user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/QuerySavesRequest'
      responses:
        '200':
          description: Query results
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/QuerySavesResponse'

  # ═══════════════════════════════════════════════════════════════════════════
  # SCHEMA MIGRATION
  # ═══════════════════════════════════════════════════════════════════════════

  /save-load/migrate:
    post:
      tags: [Migration]
      operationId: MigrateSave
      summary: Migrate save to new schema version
      description: |
        Applies migration handlers to upgrade a save from one schema version
        to another. Creates a new version with the migrated data.
      x-permissions:
        - role: developer
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/MigrateSaveRequest'
      responses:
        '200':
          description: Migration successful
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/MigrateSaveResponse'
        '400':
          description: Migration failed or no path exists
        '404':
          description: Save not found

  /save-load/schema/register:
    post:
      tags: [Migration]
      operationId: RegisterSchema
      summary: Register a save data schema
      description: |
        Registers a JSON schema for validation of save data.
        Optionally includes migration handlers from previous versions.
      x-permissions:
        - role: developer
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/RegisterSchemaRequest'
      responses:
        '200':
          description: Schema registered
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SchemaResponse'

  /save-load/schema/list:
    post:
      tags: [Migration]
      operationId: ListSchemas
      summary: List registered schemas
      description: Returns all registered schemas for a game/namespace.
      x-permissions:
        - role: user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListSchemasRequest'
      responses:
        '200':
          description: Schemas listed
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ListSchemasResponse'

  # ═══════════════════════════════════════════════════════════════════════════
  # ADMIN OPERATIONS
  # ═══════════════════════════════════════════════════════════════════════════

  /save-load/admin/cleanup:
    post:
      tags: [Admin]
      operationId: AdminCleanup
      summary: Run cleanup for expired/orphaned saves
      description: |
        Triggers cleanup of expired versions and orphaned assets.
        Normally runs automatically, but can be triggered manually.
      x-permissions:
        - role: admin
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/AdminCleanupRequest'
      responses:
        '200':
          description: Cleanup completed
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/AdminCleanupResponse'

  /save-load/admin/stats:
    post:
      tags: [Admin]
      operationId: AdminStats
      summary: Get storage statistics
      description: Returns storage usage statistics by owner, category, etc.
      x-permissions:
        - role: admin
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/AdminStatsRequest'
      responses:
        '200':
          description: Statistics returned
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/AdminStatsResponse'

components:
  schemas:
    # ═══════════════════════════════════════════════════════════════════════════
    # ENUMS
    # ═══════════════════════════════════════════════════════════════════════════

    OwnerType:
      type: string
      enum: [ACCOUNT, CHARACTER, SESSION, REALM]
      description: Type of entity that owns this save slot

    SaveCategory:
      type: string
      enum: [QUICK_SAVE, AUTO_SAVE, MANUAL_SAVE, CHECKPOINT, STATE_SNAPSHOT]
      description: |
        Category of save with predefined behaviors.
        QUICK_SAVE: Single-slot fast save, overwritten frequently (max 1 version).
        AUTO_SAVE: System-triggered periodic saves (max 5 versions, rolling).
        MANUAL_SAVE: User-initiated named saves (max 10 versions, no auto-cleanup).
        CHECKPOINT: Progress markers (max 20 versions, rolling).
        STATE_SNAPSHOT: Full state captures for debugging (max 3 versions, rolling).

    CompressionType:
      type: string
      enum: [NONE, GZIP, BROTLI]
      description: Compression algorithm used for save data

    # ═══════════════════════════════════════════════════════════════════════════
    # SLOT MODELS
    # ═══════════════════════════════════════════════════════════════════════════

    CreateSlotRequest:
      type: object
      required: [ownerId, ownerType, slotName, category]
      properties:
        ownerId:
          type: string
          format: uuid
          description: ID of the owning entity
        ownerType:
          $ref: '#/components/schemas/OwnerType'
        slotName:
          type: string
          minLength: 1
          maxLength: 64
          pattern: '^[a-z0-9][a-z0-9-]*[a-z0-9]$'
          description: Slot name (lowercase alphanumeric with hyphens)
        category:
          $ref: '#/components/schemas/SaveCategory'
        maxVersions:
          type: integer
          minimum: 1
          maximum: 100
          description: Override default max versions for this category
        retentionDays:
          type: integer
          minimum: 1
          description: Days to retain versions (null = indefinite)
        compressionType:
          $ref: '#/components/schemas/CompressionType'
        metadata:
          type: object
          additionalProperties:
            type: string
          description: Custom key-value metadata for the slot

    GetSlotRequest:
      type: object
      required: [ownerId, ownerType, slotName]
      properties:
        ownerId:
          type: string
          format: uuid
          description: ID of the owning entity
        ownerType:
          $ref: '#/components/schemas/OwnerType'
        slotName:
          type: string
          description: Slot name

    ListSlotsRequest:
      type: object
      required: [ownerId, ownerType]
      properties:
        ownerId:
          type: string
          format: uuid
          description: ID of the owning entity
        ownerType:
          $ref: '#/components/schemas/OwnerType'
        category:
          $ref: '#/components/schemas/SaveCategory'
        includeVersionCount:
          type: boolean
          default: true
          description: Include version count in response

    DeleteSlotRequest:
      type: object
      required: [ownerId, ownerType, slotName]
      properties:
        ownerId:
          type: string
          format: uuid
          description: ID of the owning entity
        ownerType:
          $ref: '#/components/schemas/OwnerType'
        slotName:
          type: string
          description: Slot name

    SlotResponse:
      type: object
      required: [slotId, ownerId, ownerType, slotName, category, createdAt]
      properties:
        slotId:
          type: string
          format: uuid
          description: Unique slot identifier
        ownerId:
          type: string
          format: uuid
          description: ID of the owning entity
        ownerType:
          $ref: '#/components/schemas/OwnerType'
        slotName:
          type: string
          description: Slot name
        category:
          $ref: '#/components/schemas/SaveCategory'
        maxVersions:
          type: integer
          description: Maximum versions to retain
        retentionDays:
          type: integer
          nullable: true
          description: Days to retain versions (null = indefinite)
        compressionType:
          $ref: '#/components/schemas/CompressionType'
        versionCount:
          type: integer
          description: Current number of versions in slot
        latestVersion:
          type: integer
          nullable: true
          description: Latest version number (null if empty)
        totalSizeBytes:
          type: integer
          format: int64
          description: Total storage used by all versions
        createdAt:
          type: string
          format: date-time
          description: Slot creation timestamp
        updatedAt:
          type: string
          format: date-time
          description: Last modification timestamp
        metadata:
          type: object
          additionalProperties:
            type: string
          description: Custom key-value metadata

    ListSlotsResponse:
      type: object
      required: [slots]
      properties:
        slots:
          type: array
          items:
            $ref: '#/components/schemas/SlotResponse'
          description: List of slots
        totalCount:
          type: integer
          description: Total number of slots for owner

    DeleteSlotResponse:
      type: object
      required: [deleted, versionsDeleted, bytesFreed]
      properties:
        deleted:
          type: boolean
          description: Whether slot was deleted
        versionsDeleted:
          type: integer
          description: Number of versions deleted
        bytesFreed:
          type: integer
          format: int64
          description: Storage freed in bytes

    # ═══════════════════════════════════════════════════════════════════════════
    # SAVE/LOAD MODELS
    # ═══════════════════════════════════════════════════════════════════════════

    SaveRequest:
      type: object
      required: [ownerId, ownerType, slotName, data]
      properties:
        ownerId:
          type: string
          format: uuid
          description: ID of the owning entity
        ownerType:
          $ref: '#/components/schemas/OwnerType'
        slotName:
          type: string
          description: Slot name (auto-created if doesn't exist)
        category:
          $ref: '#/components/schemas/SaveCategory'
          description: Category for auto-created slots (defaults to MANUAL_SAVE)
        data:
          type: string
          format: byte
          description: Base64-encoded save data
        schemaVersion:
          type: string
          description: Schema version identifier for migration tracking
        displayName:
          type: string
          maxLength: 128
          description: Human-readable name for this save
        metadata:
          type: object
          additionalProperties:
            type: string
          description: Custom metadata (e.g., level, playtime, location)
        pinAsCheckpoint:
          type: string
          maxLength: 64
          description: If provided, pin this version with checkpoint name

    SaveResponse:
      type: object
      required: [slotId, versionNumber, contentHash, sizeBytes, createdAt]
      properties:
        slotId:
          type: string
          format: uuid
          description: Slot identifier
        versionNumber:
          type: integer
          description: Assigned version number
        contentHash:
          type: string
          description: SHA-256 hash of save data
        sizeBytes:
          type: integer
          format: int64
          description: Size of save data in bytes
        compressedSizeBytes:
          type: integer
          format: int64
          description: Compressed size (if compression applied)
        compressionRatio:
          type: number
          format: double
          description: Compression ratio (0-1)
        pinned:
          type: boolean
          description: Whether version was pinned
        checkpointName:
          type: string
          nullable: true
          description: Checkpoint name if pinned
        createdAt:
          type: string
          format: date-time
          description: Save timestamp
        versionsCleanedUp:
          type: integer
          description: Number of old versions cleaned up by rolling policy

    LoadRequest:
      type: object
      required: [ownerId, ownerType, slotName]
      properties:
        ownerId:
          type: string
          format: uuid
          description: ID of the owning entity
        ownerType:
          $ref: '#/components/schemas/OwnerType'
        slotName:
          type: string
          description: Slot name
        versionNumber:
          type: integer
          description: Specific version to load (defaults to latest)
        checkpointName:
          type: string
          description: Load by checkpoint name instead of version number
        includeMetadata:
          type: boolean
          default: true
          description: Include version metadata in response

    LoadResponse:
      type: object
      required: [slotId, versionNumber, data, contentHash]
      properties:
        slotId:
          type: string
          format: uuid
          description: Slot identifier
        versionNumber:
          type: integer
          description: Version number loaded
        data:
          type: string
          format: byte
          description: Base64-encoded save data (decompressed)
        contentHash:
          type: string
          description: SHA-256 hash for integrity verification
        schemaVersion:
          type: string
          nullable: true
          description: Schema version of this save
        displayName:
          type: string
          nullable: true
          description: Human-readable name
        pinned:
          type: boolean
          description: Whether this version is pinned
        checkpointName:
          type: string
          nullable: true
          description: Checkpoint name if pinned
        createdAt:
          type: string
          format: date-time
          description: Save timestamp
        metadata:
          type: object
          additionalProperties:
            type: string
          description: Custom metadata

    # ═══════════════════════════════════════════════════════════════════════════
    # VERSION MODELS
    # ═══════════════════════════════════════════════════════════════════════════

    ListVersionsRequest:
      type: object
      required: [ownerId, ownerType, slotName]
      properties:
        ownerId:
          type: string
          format: uuid
          description: ID of the owning entity
        ownerType:
          $ref: '#/components/schemas/OwnerType'
        slotName:
          type: string
          description: Slot name
        offset:
          type: integer
          default: 0
          description: Pagination offset
        limit:
          type: integer
          default: 20
          maximum: 100
          description: Maximum results to return
        pinnedOnly:
          type: boolean
          default: false
          description: Only return pinned versions

    ListVersionsResponse:
      type: object
      required: [versions, totalCount]
      properties:
        versions:
          type: array
          items:
            $ref: '#/components/schemas/VersionResponse'
          description: List of versions
        totalCount:
          type: integer
          description: Total version count in slot

    VersionResponse:
      type: object
      required: [versionNumber, contentHash, sizeBytes, createdAt]
      properties:
        versionNumber:
          type: integer
          description: Version number
        assetId:
          type: string
          format: uuid
          description: Reference to asset in lib-asset
        contentHash:
          type: string
          description: SHA-256 hash
        sizeBytes:
          type: integer
          format: int64
          description: Size in bytes
        compressedSizeBytes:
          type: integer
          format: int64
          description: Compressed size if applicable
        schemaVersion:
          type: string
          nullable: true
          description: Schema version
        displayName:
          type: string
          nullable: true
          description: Human-readable name
        pinned:
          type: boolean
          description: Whether version is pinned
        checkpointName:
          type: string
          nullable: true
          description: Checkpoint name if pinned
        createdAt:
          type: string
          format: date-time
          description: Creation timestamp
        metadata:
          type: object
          additionalProperties:
            type: string
          description: Custom metadata

    PinVersionRequest:
      type: object
      required: [ownerId, ownerType, slotName, versionNumber]
      properties:
        ownerId:
          type: string
          format: uuid
          description: ID of the owning entity
        ownerType:
          $ref: '#/components/schemas/OwnerType'
        slotName:
          type: string
          description: Slot name
        versionNumber:
          type: integer
          description: Version to pin
        checkpointName:
          type: string
          maxLength: 64
          description: Optional checkpoint name for easy retrieval

    UnpinVersionRequest:
      type: object
      required: [ownerId, ownerType, slotName, versionNumber]
      properties:
        ownerId:
          type: string
          format: uuid
          description: ID of the owning entity
        ownerType:
          $ref: '#/components/schemas/OwnerType'
        slotName:
          type: string
          description: Slot name
        versionNumber:
          type: integer
          description: Version to unpin

    DeleteVersionRequest:
      type: object
      required: [ownerId, ownerType, slotName, versionNumber]
      properties:
        ownerId:
          type: string
          format: uuid
          description: ID of the owning entity
        ownerType:
          $ref: '#/components/schemas/OwnerType'
        slotName:
          type: string
          description: Slot name
        versionNumber:
          type: integer
          description: Version to delete

    DeleteVersionResponse:
      type: object
      required: [deleted, bytesFreed]
      properties:
        deleted:
          type: boolean
          description: Whether version was deleted
        bytesFreed:
          type: integer
          format: int64
          description: Storage freed in bytes

    # ═══════════════════════════════════════════════════════════════════════════
    # QUERY MODELS
    # ═══════════════════════════════════════════════════════════════════════════

    QuerySavesRequest:
      type: object
      properties:
        ownerId:
          type: string
          format: uuid
          description: Filter by owner ID
        ownerType:
          $ref: '#/components/schemas/OwnerType'
        category:
          $ref: '#/components/schemas/SaveCategory'
        createdAfter:
          type: string
          format: date-time
          description: Filter by creation date
        createdBefore:
          type: string
          format: date-time
          description: Filter by creation date
        pinnedOnly:
          type: boolean
          description: Only return pinned versions
        schemaVersion:
          type: string
          description: Filter by schema version
        metadataFilter:
          type: object
          additionalProperties:
            type: string
          description: Filter by metadata key-value pairs
        offset:
          type: integer
          default: 0
          description: Pagination offset
        limit:
          type: integer
          default: 20
          maximum: 100
          description: Maximum results
        sortBy:
          type: string
          enum: [created_at, size, version_number]
          default: created_at
          description: Sort field
        sortOrder:
          type: string
          enum: [asc, desc]
          default: desc
          description: Sort order

    QuerySavesResponse:
      type: object
      required: [results, totalCount]
      properties:
        results:
          type: array
          items:
            $ref: '#/components/schemas/QueryResultItem'
          description: Query results
        totalCount:
          type: integer
          description: Total matching results

    QueryResultItem:
      type: object
      required: [slotId, slotName, ownerId, ownerType, category, versionNumber, createdAt]
      properties:
        slotId:
          type: string
          format: uuid
          description: Slot identifier
        slotName:
          type: string
          description: Slot name
        ownerId:
          type: string
          format: uuid
          description: Owner ID
        ownerType:
          $ref: '#/components/schemas/OwnerType'
        category:
          $ref: '#/components/schemas/SaveCategory'
        versionNumber:
          type: integer
          description: Version number
        sizeBytes:
          type: integer
          format: int64
          description: Size in bytes
        schemaVersion:
          type: string
          nullable: true
          description: Schema version
        displayName:
          type: string
          nullable: true
          description: Display name
        pinned:
          type: boolean
          description: Whether pinned
        checkpointName:
          type: string
          nullable: true
          description: Checkpoint name
        createdAt:
          type: string
          format: date-time
          description: Creation timestamp
        metadata:
          type: object
          additionalProperties:
            type: string
          description: Custom metadata

    # ═══════════════════════════════════════════════════════════════════════════
    # MIGRATION MODELS
    # ═══════════════════════════════════════════════════════════════════════════

    MigrateSaveRequest:
      type: object
      required: [ownerId, ownerType, slotName, targetSchemaVersion]
      properties:
        ownerId:
          type: string
          format: uuid
          description: ID of the owning entity
        ownerType:
          $ref: '#/components/schemas/OwnerType'
        slotName:
          type: string
          description: Slot name
        versionNumber:
          type: integer
          description: Specific version to migrate (defaults to latest)
        targetSchemaVersion:
          type: string
          description: Target schema version to migrate to
        dryRun:
          type: boolean
          default: false
          description: Validate migration without saving

    MigrateSaveResponse:
      type: object
      required: [success, fromSchemaVersion, toSchemaVersion]
      properties:
        success:
          type: boolean
          description: Whether migration succeeded
        fromSchemaVersion:
          type: string
          description: Original schema version
        toSchemaVersion:
          type: string
          description: Target schema version
        newVersionNumber:
          type: integer
          nullable: true
          description: New version number (null if dry run)
        migrationPath:
          type: array
          items:
            type: string
          description: Migration path applied (list of versions)
        warnings:
          type: array
          items:
            type: string
          description: Non-fatal migration warnings

    RegisterSchemaRequest:
      type: object
      required: [namespace, schemaVersion, schema]
      properties:
        namespace:
          type: string
          description: Schema namespace (e.g., game identifier)
        schemaVersion:
          type: string
          description: Schema version identifier
        schema:
          type: object
          description: JSON Schema definition for validation
        previousVersion:
          type: string
          nullable: true
          description: Previous version this migrates from
        migrationScript:
          type: string
          nullable: true
          description: JavaScript migration function (sandboxed execution)

    SchemaResponse:
      type: object
      required: [namespace, schemaVersion, createdAt]
      properties:
        namespace:
          type: string
          description: Schema namespace
        schemaVersion:
          type: string
          description: Schema version
        schema:
          type: object
          description: JSON Schema definition
        previousVersion:
          type: string
          nullable: true
          description: Previous version
        hasMigration:
          type: boolean
          description: Whether migration script is registered
        createdAt:
          type: string
          format: date-time
          description: Registration timestamp

    ListSchemasRequest:
      type: object
      required: [namespace]
      properties:
        namespace:
          type: string
          description: Schema namespace to list

    ListSchemasResponse:
      type: object
      required: [schemas]
      properties:
        schemas:
          type: array
          items:
            $ref: '#/components/schemas/SchemaResponse'
          description: Registered schemas
        latestVersion:
          type: string
          nullable: true
          description: Latest schema version

    # ═══════════════════════════════════════════════════════════════════════════
    # ADMIN MODELS
    # ═══════════════════════════════════════════════════════════════════════════

    AdminCleanupRequest:
      type: object
      properties:
        dryRun:
          type: boolean
          default: true
          description: Preview cleanup without executing
        olderThanDays:
          type: integer
          description: Only cleanup versions older than N days
        ownerType:
          $ref: '#/components/schemas/OwnerType'
        category:
          $ref: '#/components/schemas/SaveCategory'

    AdminCleanupResponse:
      type: object
      required: [versionsDeleted, bytesFreed, dryRun]
      properties:
        versionsDeleted:
          type: integer
          description: Number of versions deleted
        slotsDeleted:
          type: integer
          description: Number of empty slots deleted
        bytesFreed:
          type: integer
          format: int64
          description: Storage freed in bytes
        dryRun:
          type: boolean
          description: Whether this was a preview

    AdminStatsRequest:
      type: object
      properties:
        groupBy:
          type: string
          enum: [owner_type, category, schema_version]
          description: Group statistics by field

    AdminStatsResponse:
      type: object
      required: [totalSlots, totalVersions, totalSizeBytes]
      properties:
        totalSlots:
          type: integer
          description: Total slot count
        totalVersions:
          type: integer
          description: Total version count
        totalSizeBytes:
          type: integer
          format: int64
          description: Total storage used
        pinnedVersions:
          type: integer
          description: Number of pinned versions
        breakdown:
          type: array
          items:
            $ref: '#/components/schemas/StatsBreakdown'
          description: Breakdown by groupBy field

    StatsBreakdown:
      type: object
      required: [key, slots, versions, sizeBytes]
      properties:
        key:
          type: string
          description: Group key
        slots:
          type: integer
          description: Slot count
        versions:
          type: integer
          description: Version count
        sizeBytes:
          type: integer
          format: int64
          description: Storage used
```

---

## Configuration Schema (`schemas/save-load-configuration.yaml`)

```yaml
x-service-configuration:
  properties:
    MaxSaveSizeBytes:
      type: integer
      format: int64
      env: SAVE_LOAD_MAX_SAVE_SIZE_BYTES
      default: 104857600
      description: Maximum size for a single save in bytes (default 100MB)

    AutoCompressThresholdBytes:
      type: integer
      format: int64
      env: SAVE_LOAD_AUTO_COMPRESS_THRESHOLD_BYTES
      default: 1048576
      description: Auto-compress saves larger than this (default 1MB)

    DefaultCompressionType:
      type: string
      env: SAVE_LOAD_DEFAULT_COMPRESSION_TYPE
      default: GZIP
      description: Default compression algorithm (NONE, GZIP, BROTLI)

    SlotMetadataStoreName:
      type: string
      env: SAVE_LOAD_SLOT_METADATA_STORE_NAME
      default: save-load-slots
      description: State store name for slot metadata (MySQL backend)

    VersionManifestStoreName:
      type: string
      env: SAVE_LOAD_VERSION_MANIFEST_STORE_NAME
      default: save-load-versions
      description: State store name for version manifests (MySQL backend)

    HotCacheStoreName:
      type: string
      env: SAVE_LOAD_HOT_CACHE_STORE_NAME
      default: save-load-cache
      description: State store name for hot save cache (Redis backend)

    HotCacheTtlMinutes:
      type: integer
      env: SAVE_LOAD_HOT_CACHE_TTL_MINUTES
      default: 60
      description: TTL for hot cache entries in minutes

    SchemaStoreName:
      type: string
      env: SAVE_LOAD_SCHEMA_STORE_NAME
      default: save-load-schemas
      description: State store name for registered schemas

    AssetBucket:
      type: string
      env: SAVE_LOAD_ASSET_BUCKET
      default: game-saves
      description: MinIO bucket for save assets

    DefaultMaxVersionsQuickSave:
      type: integer
      env: SAVE_LOAD_DEFAULT_MAX_VERSIONS_QUICK_SAVE
      default: 1
      description: Default max versions for QUICK_SAVE category

    DefaultMaxVersionsAutoSave:
      type: integer
      env: SAVE_LOAD_DEFAULT_MAX_VERSIONS_AUTO_SAVE
      default: 5
      description: Default max versions for AUTO_SAVE category

    DefaultMaxVersionsManualSave:
      type: integer
      env: SAVE_LOAD_DEFAULT_MAX_VERSIONS_MANUAL_SAVE
      default: 10
      description: Default max versions for MANUAL_SAVE category

    DefaultMaxVersionsCheckpoint:
      type: integer
      env: SAVE_LOAD_DEFAULT_MAX_VERSIONS_CHECKPOINT
      default: 20
      description: Default max versions for CHECKPOINT category

    DefaultMaxVersionsStateSnapshot:
      type: integer
      env: SAVE_LOAD_DEFAULT_MAX_VERSIONS_STATE_SNAPSHOT
      default: 3
      description: Default max versions for STATE_SNAPSHOT category

    CleanupIntervalMinutes:
      type: integer
      env: SAVE_LOAD_CLEANUP_INTERVAL_MINUTES
      default: 60
      description: Interval for automatic cleanup task

    MigrationSandboxTimeoutMs:
      type: integer
      env: SAVE_LOAD_MIGRATION_SANDBOX_TIMEOUT_MS
      default: 5000
      description: Timeout for migration script execution
```

---

## Events Schema (`schemas/save-load-events.yaml`)

```yaml
openapi: 3.0.3
info:
  title: Save-Load Service Events
  version: 1.0.0
  description: Events published by the Save-Load service

  x-lifecycle:
    SaveSlot:
      model:
        slotId: { type: string, format: uuid, primary: true, required: true }
        ownerId: { type: string, format: uuid, required: true }
        ownerType: { type: string, required: true }
        slotName: { type: string, required: true }
        category: { type: string, required: true }
      sensitive: []

components:
  schemas:
    # Custom events beyond lifecycle

    SaveCreatedEvent:
      type: object
      description: Published when a new save version is created
      required: [eventId, timestamp, slotId, versionNumber, ownerId, ownerType, sizeBytes]
      properties:
        eventId:
          type: string
          format: uuid
          description: Unique event identifier
        timestamp:
          type: string
          format: date-time
          description: Event timestamp
        slotId:
          type: string
          format: uuid
          description: Slot identifier
        slotName:
          type: string
          description: Slot name
        versionNumber:
          type: integer
          description: New version number
        ownerId:
          type: string
          format: uuid
          description: Owner entity ID
        ownerType:
          type: string
          description: Owner entity type
        category:
          type: string
          description: Save category
        sizeBytes:
          type: integer
          format: int64
          description: Save size in bytes
        schemaVersion:
          type: string
          nullable: true
          description: Schema version if specified
        pinned:
          type: boolean
          description: Whether version is pinned
        checkpointName:
          type: string
          nullable: true
          description: Checkpoint name if pinned

    SaveLoadedEvent:
      type: object
      description: Published when a save is loaded
      required: [eventId, timestamp, slotId, versionNumber, ownerId, ownerType]
      properties:
        eventId:
          type: string
          format: uuid
          description: Unique event identifier
        timestamp:
          type: string
          format: date-time
          description: Event timestamp
        slotId:
          type: string
          format: uuid
          description: Slot identifier
        slotName:
          type: string
          description: Slot name
        versionNumber:
          type: integer
          description: Loaded version number
        ownerId:
          type: string
          format: uuid
          description: Owner entity ID
        ownerType:
          type: string
          description: Owner entity type

    SaveMigratedEvent:
      type: object
      description: Published when a save is migrated to a new schema version
      required: [eventId, timestamp, slotId, fromSchemaVersion, toSchemaVersion]
      properties:
        eventId:
          type: string
          format: uuid
          description: Unique event identifier
        timestamp:
          type: string
          format: date-time
          description: Event timestamp
        slotId:
          type: string
          format: uuid
          description: Slot identifier
        slotName:
          type: string
          description: Slot name
        originalVersionNumber:
          type: integer
          description: Original version that was migrated
        newVersionNumber:
          type: integer
          description: New version with migrated data
        fromSchemaVersion:
          type: string
          description: Original schema version
        toSchemaVersion:
          type: string
          description: Target schema version
        ownerId:
          type: string
          format: uuid
          description: Owner entity ID
        ownerType:
          type: string
          description: Owner entity type

    VersionPinnedEvent:
      type: object
      description: Published when a version is pinned as checkpoint
      required: [eventId, timestamp, slotId, versionNumber]
      properties:
        eventId:
          type: string
          format: uuid
          description: Unique event identifier
        timestamp:
          type: string
          format: date-time
          description: Event timestamp
        slotId:
          type: string
          format: uuid
          description: Slot identifier
        slotName:
          type: string
          description: Slot name
        versionNumber:
          type: integer
          description: Pinned version number
        checkpointName:
          type: string
          nullable: true
          description: Checkpoint name if assigned
        ownerId:
          type: string
          format: uuid
          description: Owner entity ID
        ownerType:
          type: string
          description: Owner entity type

    CleanupCompletedEvent:
      type: object
      description: Published when automatic cleanup completes
      required: [eventId, timestamp, versionsDeleted, bytesFreed]
      properties:
        eventId:
          type: string
          format: uuid
          description: Unique event identifier
        timestamp:
          type: string
          format: date-time
          description: Event timestamp
        versionsDeleted:
          type: integer
          description: Number of versions cleaned up
        slotsDeleted:
          type: integer
          description: Number of empty slots removed
        bytesFreed:
          type: integer
          format: int64
          description: Storage freed in bytes
        durationMs:
          type: integer
          description: Cleanup duration in milliseconds
```

---

## State Store Design

### MySQL Stores (Persistent, Queryable)

#### 1. Slot Metadata Store (`save-load-slots`)

**Purpose**: Persistent storage for slot configuration and summary metadata.

**Key Format**: `{ownerType}:{ownerId}:{slotName}`

**Model**:
```csharp
public sealed class SaveSlotMetadata
{
    public Guid SlotId { get; set; }
    public Guid OwnerId { get; set; }
    public OwnerType OwnerType { get; set; }
    public string SlotName { get; set; } = string.Empty;
    public SaveCategory Category { get; set; }
    public int MaxVersions { get; set; }
    public int? RetentionDays { get; set; }
    public CompressionType CompressionType { get; set; }
    public int VersionCount { get; set; }
    public int? LatestVersion { get; set; }
    public long TotalSizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}
```

#### 2. Version Manifest Store (`save-load-versions`)

**Purpose**: Persistent storage for version metadata.

**Key Format**: `{slotId}:{versionNumber:D10}`

**Model**:
```csharp
public sealed class SaveVersionManifest
{
    public Guid SlotId { get; set; }
    public int VersionNumber { get; set; }
    public Guid AssetId { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public long? CompressedSizeBytes { get; set; }
    public CompressionType CompressionType { get; set; }
    public string? SchemaVersion { get; set; }
    public string? DisplayName { get; set; }
    public bool Pinned { get; set; }
    public string? CheckpointName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}
```

#### 3. Schema Store (`save-load-schemas`)

**Purpose**: Registered schema definitions and migration scripts.

**Key Format**: `{namespace}:{schemaVersion}`

**Model**:
```csharp
public sealed class SaveSchema
{
    public string Namespace { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = string.Empty;
    public JsonDocument Schema { get; set; }
    public string? PreviousVersion { get; set; }
    public string? MigrationScript { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

### Redis Stores (Hot Cache)

#### 4. Hot Save Cache (`save-load-cache`)

**Purpose**: Fast access cache for recently accessed save data.

**Key Format**: `{slotId}:{versionNumber}`

**Model**:
```csharp
public sealed class HotSaveEntry
{
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public string ContentHash { get; set; } = string.Empty;
    public DateTimeOffset CachedAt { get; set; }
}
```

**TTL**: Configurable (default 60 minutes)

---

## Asset Storage Design

### Bucket Structure

```
game-saves/
├── {slotId}/
│   ├── v{versionNumber:D10}.bin       # Save data blob
│   ├── v{versionNumber:D10}.meta.json # Version metadata (redundant backup)
│   └── ...
└── schemas/
    └── {namespace}/
        └── {schemaVersion}.json       # Backup of registered schemas
```

### Upload Flow

1. **Save Request** received with data
2. **Compress** data if above threshold
3. **Calculate** SHA-256 content hash
4. **Request upload URL** from Asset service
5. **Upload** compressed data to MinIO
6. **Complete upload** notification to Asset service
7. **Create** version manifest in MySQL store
8. **Cache** in Redis hot cache
9. **Apply** rolling cleanup if needed
10. **Publish** SaveCreatedEvent

### Load Flow

1. **Load Request** received
2. **Check** Redis hot cache
3. **If miss**, fetch version manifest from MySQL
4. **Get download URL** from Asset service
5. **Download** and decompress data
6. **Cache** in Redis
7. **Return** data to client
8. **Publish** SaveLoadedEvent

---

## SDK Design

### C# SDK (`Bannou.SDK.SaveLoad`)

```csharp
/// <summary>
/// Client for the Save-Load service.
/// </summary>
public interface ISaveLoadClient
{
    // Slot operations
    Task<SlotResponse> CreateSlotAsync(CreateSlotRequest request, CancellationToken ct = default);
    Task<SlotResponse?> GetSlotAsync(GetSlotRequest request, CancellationToken ct = default);
    Task<ListSlotsResponse> ListSlotsAsync(ListSlotsRequest request, CancellationToken ct = default);
    Task<DeleteSlotResponse> DeleteSlotAsync(DeleteSlotRequest request, CancellationToken ct = default);

    // Save/Load operations
    Task<SaveResponse> SaveAsync(SaveRequest request, CancellationToken ct = default);
    Task<LoadResponse?> LoadAsync(LoadRequest request, CancellationToken ct = default);

    // Version operations
    Task<ListVersionsResponse> ListVersionsAsync(ListVersionsRequest request, CancellationToken ct = default);
    Task<VersionResponse> PinVersionAsync(PinVersionRequest request, CancellationToken ct = default);
    Task<VersionResponse> UnpinVersionAsync(UnpinVersionRequest request, CancellationToken ct = default);
    Task<DeleteVersionResponse> DeleteVersionAsync(DeleteVersionRequest request, CancellationToken ct = default);

    // Query
    Task<QuerySavesResponse> QuerySavesAsync(QuerySavesRequest request, CancellationToken ct = default);

    // Migration
    Task<MigrateSaveResponse> MigrateSaveAsync(MigrateSaveRequest request, CancellationToken ct = default);
}
```

### TypeScript SDK (`@bannou/save-load`)

```typescript
export interface SaveLoadClient {
  // Slot operations
  createSlot(request: CreateSlotRequest): Promise<SlotResponse>;
  getSlot(request: GetSlotRequest): Promise<SlotResponse | null>;
  listSlots(request: ListSlotsRequest): Promise<ListSlotsResponse>;
  deleteSlot(request: DeleteSlotRequest): Promise<DeleteSlotResponse>;

  // Save/Load operations
  save(request: SaveRequest): Promise<SaveResponse>;
  load(request: LoadRequest): Promise<LoadResponse | null>;

  // Version operations
  listVersions(request: ListVersionsRequest): Promise<ListVersionsResponse>;
  pinVersion(request: PinVersionRequest): Promise<VersionResponse>;
  unpinVersion(request: UnpinVersionRequest): Promise<VersionResponse>;
  deleteVersion(request: DeleteVersionRequest): Promise<DeleteVersionResponse>;

  // Query
  querySaves(request: QuerySavesRequest): Promise<QuerySavesResponse>;
}
```

### Helper Extensions

```csharp
/// <summary>
/// Convenience extensions for common save/load patterns.
/// </summary>
public static class SaveLoadExtensions
{
    /// <summary>
    /// Quick save for a character with auto-generated slot name.
    /// </summary>
    public static Task<SaveResponse> QuickSaveCharacterAsync(
        this ISaveLoadClient client,
        Guid characterId,
        byte[] data,
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        return client.SaveAsync(new SaveRequest
        {
            OwnerId = characterId,
            OwnerType = OwnerType.CHARACTER,
            SlotName = "quicksave",
            Category = SaveCategory.QUICK_SAVE,
            Data = Convert.ToBase64String(data),
            Metadata = metadata ?? new()
        }, ct);
    }

    /// <summary>
    /// Load the latest quicksave for a character.
    /// </summary>
    public static Task<LoadResponse?> LoadQuickSaveAsync(
        this ISaveLoadClient client,
        Guid characterId,
        CancellationToken ct = default)
    {
        return client.LoadAsync(new LoadRequest
        {
            OwnerId = characterId,
            OwnerType = OwnerType.CHARACTER,
            SlotName = "quicksave"
        }, ct);
    }
}
```

---

## Implementation Plan

### Phase 1: Core Infrastructure

1. Create schema files:
   - `schemas/save-load-api.yaml`
   - `schemas/save-load-configuration.yaml`
   - `schemas/save-load-events.yaml`

2. Generate plugin structure:
   - Run `scripts/generate-all-services.sh`
   - Verify generated files in `plugins/lib-save-load/Generated/`

3. Implement core models:
   - `SaveSlotMetadata`
   - `SaveVersionManifest`
   - `HotSaveEntry`

### Phase 2: Slot Management

4. Implement slot operations:
   - `CreateSlotAsync` - Create/update slot configuration
   - `GetSlotAsync` - Retrieve slot metadata
   - `ListSlotsAsync` - List slots for owner
   - `DeleteSlotAsync` - Delete slot and all versions

### Phase 3: Save/Load Operations

5. Implement save flow:
   - Compression pipeline
   - Asset upload integration
   - Version manifest creation
   - Hot cache population
   - Rolling cleanup

6. Implement load flow:
   - Hot cache lookup
   - Asset download
   - Decompression
   - Cache population

### Phase 4: Version Management

7. Implement version operations:
   - `ListVersionsAsync` - List versions with pagination
   - `PinVersionAsync` - Pin version as checkpoint
   - `UnpinVersionAsync` - Remove pin
   - `DeleteVersionAsync` - Delete specific version

### Phase 5: Query & Search

8. Implement query:
   - Multi-field filtering
   - Pagination
   - Sorting
   - Metadata filtering

### Phase 6: Schema Migration

9. Implement migration:
   - Schema registration
   - Migration path calculation
   - Sandboxed script execution
   - Migration application

### Phase 7: Admin & Cleanup

10. Implement admin operations:
    - Cleanup task
    - Statistics aggregation
    - Orphan detection

### Phase 8: SDK Integration

11. Update SDK generation:
    - Add to `scripts/generate-client-sdk.sh`
    - Generate TypeScript types
    - Add helper extensions

### Phase 9: Testing

12. Implement tests:
    - Unit tests (`lib-save-load.tests/`)
    - HTTP integration tests (`http-tester/Tests/SaveLoadTests.cs`)
    - Edge tests (`edge-tester/Tests/SaveLoadEdgeTests.cs`)

---

## Tenet Compliance Checklist

### FOUNDATION TENETS (Must follow before starting)

| Tenet | Requirement | Compliance |
|-------|-------------|------------|
| **T1** | All APIs defined in OpenAPI YAML | Schema files defined above |
| **T2** | Never edit Generated/ files | Only implement `SaveLoadService.cs` |
| **T4** | Use infrastructure libs only | lib-state, lib-messaging, lib-asset only |
| **T5** | All state changes publish events | Events defined for all mutations |
| **T6** | Partial class with standard deps | Follow service implementation pattern |
| **T13** | x-permissions on all endpoints | Permissions defined per endpoint |
| **T15** | POST-only internal APIs | All endpoints are POST |
| **T18** | MIT/BSD/Apache licenses only | No new dependencies required |

### IMPLEMENTATION TENETS (Must follow while coding)

| Tenet | Requirement | How to Comply |
|-------|-------------|---------------|
| **T3** | Event consumer fan-out | NO event subscriptions (pure storage service) |
| **T7** | Try-catch with ApiException | Wrap all lib-state/lib-asset calls |
| **T8** | Return (StatusCodes, T?) tuples | All methods return tuples |
| **T9** | Multi-instance safety | ConcurrentDictionary for caches, distributed locks |
| **T14** | Polymorphic associations | OwnerType + OwnerId pattern |
| **T17** | Client events via IClientEventPublisher | N/A - no client events |
| **T20** | Use BannouJson | All serialization via BannouJson |
| **T21** | Configuration-first | Use SaveLoadServiceConfiguration |
| **T23** | Async methods with await | All Task methods are async |

### QUALITY TENETS (Must verify before PR)

| Tenet | Requirement | Verification |
|-------|-------------|--------------|
| **T10** | Structured logging | Message templates, no [TAGS] |
| **T11** | Three-tier testing | Unit + HTTP + Edge tests |
| **T12** | Test integrity | Never weaken tests |
| **T16** | Naming conventions | Follow patterns in schema |
| **T19** | XML documentation | All schema properties have descriptions |
| **T22** | No warning suppression | Fix warnings, don't hide |

### Critical Reminders for Implementer

1. **NO event subscriptions** - This is a pure storage service
2. **Polymorphic ownership** - Use EntityType+EntityId consistently
3. **Hybrid storage** - Redis for hot cache, MySQL for metadata, Asset for blobs
4. **Rolling cleanup** - Respect maxVersions per category
5. **Compression** - Auto-compress above threshold
6. **Schema migration** - Sandboxed execution with timeout
7. **Content hashing** - SHA-256 for all save data

---

## Open Questions for Implementation

1. **Migration Script Sandboxing**: Use Jint (JavaScript interpreter, BSD-2 licensed - T18 compliant) for migration scripts. Security considerations: timeout enforcement, memory limits, no file/network access.

2. **Large Save Streaming**: For very large saves (>100MB), consider chunked upload/download. May need separate endpoint.

3. **Cross-Region Replication**: Asset service handles this, but may need additional metadata for region-aware routing.

4. **Quota Enforcement**: Currently no per-owner quota. May add in future version.

5. **Encryption at Rest**: Currently relies on MinIO/infrastructure encryption. May add application-level encryption for sensitive saves.

---

*Document created: 2025-01-11*
*Ready for implementation review*
