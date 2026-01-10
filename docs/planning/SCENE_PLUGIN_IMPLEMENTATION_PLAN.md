# Scene Plugin Implementation Plan

> **Status**: APPROVED PLAN
> **Created**: 2026-01-09
> **Base Document**: [UPCOMING_-_SCENE_PLUGIN.md](./UPCOMING_-_SCENE_PLUGIN.md)
> **Related**: [BANNOU_DESIGN.md](../BANNOU_DESIGN.md), [THE_DREAM.md](./THE_DREAM.md), [Mapping API](../../schemas/mapping-api.yaml)

---

## Executive Summary

This plan implements the Scene Plugin across 6 phases, creating a hierarchical composition storage system for game worlds. The implementation follows schema-first development (FOUNDATION TENETS) and publishes comprehensive events for all meaningful state changes (IMPLEMENTATION TENETS).

**Key Design Decisions** (from requirements clarification):
- Storage: YAML format in lib-asset with `application/x-bannou-scene+yaml` content type
- Reference Resolution: Configurable depth limit, circular references returned as unresolved with error
- Checkout Locks: Configurable TTL following mapping plugin patterns, no admin force-unlock
- Deletion: Soft delete with recovery via asset TTL (~30 days), blocked if references exist
- Version History: Per-gameId configurable retention, default 3 versions
- Instance Tracking: Not needed - instantiation is notification-only

---

## Schema Files Overview

```
schemas/
├── scene-api.yaml              # API endpoints with x-permissions
├── scene-events.yaml           # Service events with x-lifecycle, x-event-subscriptions
├── scene-configuration.yaml    # Service configuration
└── scene-client-events.yaml    # Client push events (if needed for editor collaboration)
```

---

## Phase 1: Core Schema and Storage

**Goal**: Scene documents can be created, stored, retrieved, and validated structurally.

### 1.1 Schema: scene-api.yaml

```yaml
openapi: 3.0.0
info:
  title: Bannou Scene Service API
  description: |
    Hierarchical composition storage for game worlds.

    **POST-Only API Pattern**: This API uses POST for all operations to support zero-copy
    WebSocket routing. See docs/BANNOU_DESIGN.md for architectural rationale.

    The Scene service stores and retrieves hierarchical scene documents with nodes
    representing meshes, markers, volumes, emitters, and references to other scenes.

    **Core Responsibility**: Store and retrieve hierarchical scene documents.

    **Not Responsible For**:
    - Computing world transforms (consumers apply transforms themselves)
    - Determining affordances (runtime geometry concern)
    - Pushing data to other services (event-driven decoupling)
    - Interpreting node behavior (consumers decide what nodes mean)

    **NRT Compliance**: AI agents MUST review docs/NRT-SCHEMA-RULES.md before modifying
    this schema. Optional reference types require `nullable: true`.
  version: 1.0.0
servers:
  - url: http://localhost:5012
    description: Bannou service endpoint (internal only)

paths:
  # ========== SCENE CRUD ==========
  /scene/create:
    post:
      summary: Create a new scene document
      description: |
        Creates a new scene document and stores it in lib-asset.
        Publishes scene.created event on success.
        Returns Conflict if a scene with the same sceneId already exists.
      operationId: createScene
      tags:
        - Scene
      x-permissions:
        - role: developer
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CreateSceneRequest'
      responses:
        '200':
          description: Scene created successfully
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SceneResponse'
        '400':
          description: Invalid scene structure (validation failed)
        '409':
          description: Scene with this sceneId already exists

  /scene/get:
    post:
      summary: Retrieve a scene by ID
      description: |
        Retrieves a scene document. Optionally resolves nested scene references
        up to a configurable depth.
      operationId: getScene
      tags:
        - Scene
      x-permissions:
        - role: user
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetSceneRequest'
      responses:
        '200':
          description: Scene retrieved successfully
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/GetSceneResponse'
        '404':
          description: Scene not found

  /scene/list:
    post:
      summary: List scenes with filtering
      description: |
        Lists scenes matching the provided filters. Supports pagination.
        Results are ordered by updatedAt descending (most recent first).
      operationId: listScenes
      tags:
        - Scene
      x-permissions:
        - role: user
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListScenesRequest'
      responses:
        '200':
          description: Scenes listed successfully
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ListScenesResponse'

  /scene/update:
    post:
      summary: Update a scene document
      description: |
        Updates an existing scene document. Scene must not be checked out by
        another user. Increments the PATCH version automatically.
        Publishes scene.updated event on success.
      operationId: updateScene
      tags:
        - Scene
      x-permissions:
        - role: developer
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/UpdateSceneRequest'
      responses:
        '200':
          description: Scene updated successfully
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SceneResponse'
        '404':
          description: Scene not found
        '409':
          description: Scene is checked out by another user
        '400':
          description: Invalid scene structure

  /scene/delete:
    post:
      summary: Delete a scene
      description: |
        Soft-deletes a scene. The scene data remains recoverable via lib-asset
        for approximately 30 days. Cannot delete if other scenes reference this one.
        Publishes scene.deleted event on success.
      operationId: deleteScene
      tags:
        - Scene
      x-permissions:
        - role: developer
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/DeleteSceneRequest'
      responses:
        '200':
          description: Scene deleted successfully
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/DeleteSceneResponse'
        '404':
          description: Scene not found
        '409':
          description: Cannot delete - other scenes reference this scene

  /scene/validate:
    post:
      summary: Validate a scene structure
      description: |
        Validates a scene document without saving it. Checks structural validity
        and optionally applies game-specific validation rules.
      operationId: validateScene
      tags:
        - Scene
      x-permissions:
        - role: user
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ValidateSceneRequest'
      responses:
        '200':
          description: Validation result
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ValidationResult'

components:
  schemas:
    # ========== CORE TYPES ==========

    SceneType:
      type: string
      description: |
        Scene classification for querying and validation rule lookup.
        Different types may have different validation requirements per game.
      enum:
        - unknown
        - region
        - city
        - district
        - lot
        - building
        - room
        - dungeon
        - arena
        - vehicle
        - prefab
        - cutscene
        - other

    NodeType:
      type: string
      description: |
        Structural node type. Indicates what kind of data the node contains,
        not how it will be used at runtime. Consumers interpret nodes according
        to their own needs via tags and annotations.
      enum:
        - group
        - mesh
        - marker
        - volume
        - emitter
        - reference
        - custom

    VolumeShape:
      type: string
      description: Shape of a volume node for spatial bounds
      enum:
        - box
        - sphere
        - capsule
        - cylinder

    # ========== SPATIAL TYPES ==========

    Vector3:
      type: object
      description: A point or direction in 3D space
      required:
        - x
        - y
        - z
      properties:
        x:
          type: number
          format: double
          description: X coordinate
        y:
          type: number
          format: double
          description: Y coordinate
        z:
          type: number
          format: double
          description: Z coordinate

    Quaternion:
      type: object
      description: Rotation represented as a quaternion
      required:
        - x
        - y
        - z
        - w
      properties:
        x:
          type: number
          format: double
          description: X component
        y:
          type: number
          format: double
          description: Y component
        z:
          type: number
          format: double
          description: Z component
        w:
          type: number
          format: double
          description: W component (scalar)

    Transform:
      type: object
      description: Position, rotation, and scale in 3D space
      required:
        - position
        - rotation
        - scale
      properties:
        position:
          $ref: '#/components/schemas/Vector3'
          description: Position relative to parent
        rotation:
          $ref: '#/components/schemas/Quaternion'
          description: Rotation relative to parent
        scale:
          $ref: '#/components/schemas/Vector3'
          description: Scale relative to parent

    # ========== ASSET REFERENCE ==========

    AssetReference:
      type: object
      description: Reference to an asset in lib-asset
      required:
        - assetId
      properties:
        bundleId:
          type: string
          format: uuid
          nullable: true
          description: Optional bundle containing the asset
        assetId:
          type: string
          format: uuid
          description: Asset identifier in lib-asset
        variantId:
          type: string
          nullable: true
          description: Variant identifier (consumer interprets meaning)

    # ========== SCENE NODE ==========

    SceneNode:
      type: object
      description: |
        A node in the scene hierarchy. Nodes can contain children to form
        a tree structure. Each node has a local transform relative to its parent.
      required:
        - nodeId
        - refId
        - name
        - nodeType
        - localTransform
      properties:
        nodeId:
          type: string
          format: uuid
          description: Globally unique node identifier
        refId:
          type: string
          pattern: '^[a-z][a-z0-9_]*$'
          description: |
            Scene-local reference identifier. Must be unique within the scene.
            Used for scripting and cross-referencing. Examples: main_door, npc_spawn_1
        parentNodeId:
          type: string
          format: uuid
          nullable: true
          description: Parent node ID. Null for the root node only.
        name:
          type: string
          description: Human-readable display name for the node
        nodeType:
          $ref: '#/components/schemas/NodeType'
          description: The structural type of this node
        localTransform:
          $ref: '#/components/schemas/Transform'
          description: Transform relative to parent node
        asset:
          $ref: '#/components/schemas/AssetReference'
          nullable: true
          description: Optional asset binding (mesh, sound, particle effect)
        children:
          type: array
          items:
            $ref: '#/components/schemas/SceneNode'
          description: Child nodes in the hierarchy
        enabled:
          type: boolean
          default: true
          description: Whether this node is active in the scene definition
        sortOrder:
          type: integer
          default: 0
          description: Ordering among siblings for deterministic iteration
        tags:
          type: array
          items:
            type: string
          description: Arbitrary tags for consumer filtering (e.g., entrance, spawn, interactive)
        annotations:
          type: object
          additionalProperties: true
          nullable: true
          description: |
            Consumer-specific data stored without interpretation.
            Use namespaced keys (e.g., render.castShadows, arcadia.interactionType).

    # ========== SCENE DOCUMENT ==========

    Scene:
      type: object
      description: A complete scene document with hierarchical node structure
      required:
        - sceneId
        - gameId
        - sceneType
        - name
        - version
        - root
      properties:
        schema:
          type: string
          description: Schema identifier for validation
          default: "bannou://schemas/scene/v1"
        sceneId:
          type: string
          format: uuid
          description: Unique scene identifier
        gameId:
          type: string
          description: |
            Game service identifier for partitioning. Treated as opaque string.
            Default is the nil UUID for unpartitioned scenes.
          default: "00000000-0000-0000-0000-000000000000"
        sceneType:
          $ref: '#/components/schemas/SceneType'
          description: Scene classification for querying and validation
        name:
          type: string
          description: Human-readable scene name
        description:
          type: string
          nullable: true
          description: Optional scene description
        version:
          type: string
          pattern: '^\d+\.\d+\.\d+$'
          description: Semantic version (MAJOR.MINOR.PATCH)
        root:
          $ref: '#/components/schemas/SceneNode'
          description: Root node of the scene hierarchy
        tags:
          type: array
          items:
            type: string
          description: Searchable tags for filtering scenes
        metadata:
          type: object
          additionalProperties: true
          nullable: true
          description: |
            Scene-level metadata. Not interpreted by Scene service.
            Examples: author, thumbnail, editor preferences, generator config.
        createdAt:
          type: string
          format: date-time
          description: When the scene was first created
        updatedAt:
          type: string
          format: date-time
          description: When the scene was last modified

    # ========== REQUEST/RESPONSE MODELS ==========

    CreateSceneRequest:
      type: object
      description: Request to create a new scene
      required:
        - scene
      properties:
        scene:
          $ref: '#/components/schemas/Scene'
          description: The scene document to create

    GetSceneRequest:
      type: object
      description: Request to retrieve a scene
      required:
        - sceneId
      properties:
        sceneId:
          type: string
          format: uuid
          description: ID of the scene to retrieve
        version:
          type: string
          nullable: true
          description: Specific version to retrieve (null = latest)
        resolveReferences:
          type: boolean
          default: false
          description: Whether to resolve and embed referenced scenes
        maxReferenceDepth:
          type: integer
          default: 3
          minimum: 1
          maximum: 10
          description: Maximum depth for reference resolution (prevents infinite recursion)

    GetSceneResponse:
      type: object
      description: Response containing a scene and resolution metadata
      required:
        - scene
      properties:
        scene:
          $ref: '#/components/schemas/Scene'
          description: The retrieved scene
        resolvedReferences:
          type: array
          items:
            $ref: '#/components/schemas/ResolvedReference'
          nullable: true
          description: List of resolved references (if resolveReferences was true)
        unresolvedReferences:
          type: array
          items:
            $ref: '#/components/schemas/UnresolvedReference'
          nullable: true
          description: References that could not be resolved (circular, missing, depth exceeded)
        resolutionErrors:
          type: array
          items:
            type: string
          nullable: true
          description: Error messages for reference resolution issues

    ResolvedReference:
      type: object
      description: A successfully resolved scene reference
      required:
        - nodeId
        - refId
        - referencedSceneId
        - scene
      properties:
        nodeId:
          type: string
          format: uuid
          description: Node ID containing the reference
        refId:
          type: string
          description: refId of the referencing node
        referencedSceneId:
          type: string
          format: uuid
          description: ID of the referenced scene
        referencedVersion:
          type: string
          nullable: true
          description: Version that was resolved
        scene:
          $ref: '#/components/schemas/Scene'
          description: The resolved scene content
        depth:
          type: integer
          description: Depth level of this reference

    UnresolvedReference:
      type: object
      description: A scene reference that could not be resolved
      required:
        - nodeId
        - refId
        - referencedSceneId
        - reason
      properties:
        nodeId:
          type: string
          format: uuid
          description: Node ID containing the reference
        refId:
          type: string
          description: refId of the referencing node
        referencedSceneId:
          type: string
          format: uuid
          description: ID of the scene that could not be resolved
        reason:
          type: string
          enum:
            - not_found
            - circular_reference
            - depth_exceeded
            - access_denied
          description: Why the reference could not be resolved
        cyclePath:
          type: array
          items:
            type: string
            format: uuid
          nullable: true
          description: For circular references, the cycle path (sceneId chain)

    ListScenesRequest:
      type: object
      description: Request to list scenes with optional filters
      properties:
        gameId:
          type: string
          nullable: true
          description: Filter by game ID
        sceneType:
          $ref: '#/components/schemas/SceneType'
          nullable: true
          description: Filter by single scene type
        sceneTypes:
          type: array
          items:
            $ref: '#/components/schemas/SceneType'
          nullable: true
          description: Filter by multiple scene types (OR)
        tags:
          type: array
          items:
            type: string
          nullable: true
          description: Filter by tags (scenes must have ALL specified tags)
        nameContains:
          type: string
          nullable: true
          description: Filter by name containing this substring (case-insensitive)
        offset:
          type: integer
          default: 0
          minimum: 0
          description: Pagination offset
        limit:
          type: integer
          default: 50
          minimum: 1
          maximum: 200
          description: Maximum results to return

    ListScenesResponse:
      type: object
      description: Response containing scene list and pagination info
      required:
        - scenes
        - total
      properties:
        scenes:
          type: array
          items:
            $ref: '#/components/schemas/SceneSummary'
          description: List of scene summaries (not full documents)
        total:
          type: integer
          description: Total number of matching scenes
        offset:
          type: integer
          description: Current offset
        limit:
          type: integer
          description: Applied limit

    SceneSummary:
      type: object
      description: Summary of a scene for list results (excludes full node tree)
      required:
        - sceneId
        - gameId
        - sceneType
        - name
        - version
      properties:
        sceneId:
          type: string
          format: uuid
          description: Unique scene identifier
        gameId:
          type: string
          description: Game service identifier
        sceneType:
          $ref: '#/components/schemas/SceneType'
          description: Scene classification
        name:
          type: string
          description: Scene name
        description:
          type: string
          nullable: true
          description: Scene description
        version:
          type: string
          description: Current version
        tags:
          type: array
          items:
            type: string
          description: Scene tags
        nodeCount:
          type: integer
          description: Total number of nodes in scene
        createdAt:
          type: string
          format: date-time
          description: Creation timestamp
        updatedAt:
          type: string
          format: date-time
          description: Last update timestamp
        isCheckedOut:
          type: boolean
          description: Whether scene is currently checked out

    UpdateSceneRequest:
      type: object
      description: Request to update an existing scene
      required:
        - scene
      properties:
        scene:
          $ref: '#/components/schemas/Scene'
          description: The updated scene document (sceneId must match existing)
        checkoutToken:
          type: string
          nullable: true
          description: Checkout token if updating via checkout workflow

    DeleteSceneRequest:
      type: object
      description: Request to delete a scene
      required:
        - sceneId
      properties:
        sceneId:
          type: string
          format: uuid
          description: ID of the scene to delete
        reason:
          type: string
          nullable: true
          description: Optional reason for deletion (included in event)

    DeleteSceneResponse:
      type: object
      description: Response confirming scene deletion
      required:
        - deleted
      properties:
        deleted:
          type: boolean
          description: Whether the scene was successfully deleted
        sceneId:
          type: string
          format: uuid
          description: ID of the deleted scene
        referencingScenes:
          type: array
          items:
            type: string
            format: uuid
          nullable: true
          description: If deletion failed, IDs of scenes that reference this one

    SceneResponse:
      type: object
      description: Standard response containing a scene
      required:
        - scene
      properties:
        scene:
          $ref: '#/components/schemas/Scene'
          description: The scene document

    ValidateSceneRequest:
      type: object
      description: Request to validate a scene structure
      required:
        - scene
      properties:
        scene:
          $ref: '#/components/schemas/Scene'
          description: The scene to validate
        applyGameRules:
          type: boolean
          default: true
          description: Whether to apply registered game-specific validation rules

    ValidationResult:
      type: object
      description: Result of scene validation
      required:
        - valid
      properties:
        valid:
          type: boolean
          description: Whether the scene passed all validation checks
        errors:
          type: array
          items:
            $ref: '#/components/schemas/ValidationError'
          nullable: true
          description: Validation errors (severity = error)
        warnings:
          type: array
          items:
            $ref: '#/components/schemas/ValidationError'
          nullable: true
          description: Validation warnings (severity = warning)

    ValidationError:
      type: object
      description: A single validation error or warning
      required:
        - ruleId
        - message
        - severity
      properties:
        ruleId:
          type: string
          description: Identifier of the validation rule that triggered this
        message:
          type: string
          description: Human-readable error message
        severity:
          type: string
          enum:
            - error
            - warning
          description: Severity level
        nodePath:
          type: string
          nullable: true
          description: Path to the problematic node (e.g., root.children[0].children[2])
        nodeId:
          type: string
          format: uuid
          nullable: true
          description: ID of the problematic node
        context:
          type: object
          additionalProperties: true
          nullable: true
          description: Additional context for the error
```

### 1.2 Schema: scene-events.yaml

```yaml
openapi: 3.0.3
info:
  title: Scene Service Events
  description: |
    Event models for Scene service RabbitMQ pub/sub via MassTransit.

    The Scene service publishes lifecycle events for scene documents and
    operational events for instantiation and checkout workflows.

    **NRT Compliance**: AI agents MUST review docs/NRT-SCHEMA-RULES.md before
    modifying this schema. Optional reference types require `nullable: true`.
  version: 1.0.0

  x-event-subscriptions:
    # Scene service does not currently subscribe to external events
    []

  x-event-publications:
    # Lifecycle events (auto-generated from x-lifecycle)
    - topic: scene.created
      event: SceneCreatedEvent
      description: Published when a new scene document is created
    - topic: scene.updated
      event: SceneUpdatedEvent
      description: Published when a scene document is modified
    - topic: scene.deleted
      event: SceneDeletedEvent
      description: Published when a scene document is deleted

    # Instantiation events
    - topic: scene.instantiated
      event: SceneInstantiatedEvent
      description: Published when a scene is instantiated in the game world
    - topic: scene.destroyed
      event: SceneDestroyedEvent
      description: Published when a scene instance is removed from the game world

    # Checkout workflow events
    - topic: scene.checked_out
      event: SceneCheckedOutEvent
      description: Published when a scene is locked for editing
    - topic: scene.committed
      event: SceneCommittedEvent
      description: Published when checkout changes are committed
    - topic: scene.checkout.discarded
      event: SceneCheckoutDiscardedEvent
      description: Published when checkout is discarded without saving
    - topic: scene.checkout.expired
      event: SceneCheckoutExpiredEvent
      description: Published when a checkout lock expires due to TTL

    # Validation events
    - topic: scene.validation_rules.updated
      event: SceneValidationRulesUpdatedEvent
      description: Published when validation rules are registered/updated

    # Reference integrity events
    - topic: scene.reference.broken
      event: SceneReferenceBrokenEvent
      description: Published when a referenced scene becomes unavailable

# Lifecycle events auto-generated from this definition
# Output: schemas/Generated/scene-lifecycle-events.yaml
x-lifecycle:
  Scene:
    model:
      sceneId: { type: string, format: uuid, primary: true, required: true, description: "Unique scene identifier" }
      gameId: { type: string, required: true, description: "Game service identifier for partitioning" }
      sceneType: { type: string, required: true, description: "Scene classification (region, building, etc.)" }
      name: { type: string, required: true, description: "Human-readable scene name" }
      description: { type: string, nullable: true, description: "Optional scene description" }
      version: { type: string, required: true, description: "Semantic version (MAJOR.MINOR.PATCH)" }
      tags: { type: array, items: { type: string }, description: "Searchable tags" }
      nodeCount: { type: integer, required: true, description: "Total number of nodes in scene" }
      createdAt: { type: string, format: date-time, required: true, description: "Creation timestamp" }
      updatedAt: { type: string, format: date-time, required: true, description: "Last update timestamp" }
    sensitive: []

paths: {}

components:
  schemas:
    # ========== INSTANTIATION EVENTS ==========

    SceneInstantiatedEvent:
      type: object
      description: |
        Published when a scene is instantiated in the game world.
        Consumers (Mapping, Actor, etc.) react to spawn spatial objects and NPCs.
      required:
        - eventId
        - timestamp
        - instanceId
        - sceneAssetId
        - sceneVersion
        - sceneName
        - gameId
        - sceneType
        - regionId
        - worldTransform
      properties:
        eventId:
          type: string
          format: uuid
          description: Unique event identifier
        timestamp:
          type: string
          format: date-time
          description: When the instantiation occurred
        instanceId:
          type: string
          format: uuid
          description: Unique instance identifier (caller-provided)
        sceneAssetId:
          type: string
          format: uuid
          description: Source scene asset ID
        sceneVersion:
          type: string
          description: Version that was instantiated
        sceneName:
          type: string
          description: Scene name (for logging and display)
        gameId:
          type: string
          description: Game ID from the scene
        sceneType:
          type: string
          description: Scene type from the scene
        regionId:
          type: string
          format: uuid
          description: Region where the scene was placed
        worldTransform:
          $ref: '#/components/schemas/EventTransform'
          description: World-space transform for scene origin
        metadata:
          type: object
          additionalProperties: true
          nullable: true
          description: Caller-provided metadata passed through to consumers
        instantiatedBy:
          type: string
          nullable: true
          description: App-id of the instantiator

    SceneDestroyedEvent:
      type: object
      description: Published when a scene instance is removed from the game world
      required:
        - eventId
        - timestamp
        - instanceId
        - sceneAssetId
        - regionId
      properties:
        eventId:
          type: string
          format: uuid
          description: Unique event identifier
        timestamp:
          type: string
          format: date-time
          description: When the destruction occurred
        instanceId:
          type: string
          format: uuid
          description: Instance that was destroyed
        sceneAssetId:
          type: string
          format: uuid
          description: Source scene asset ID
        regionId:
          type: string
          format: uuid
          description: Region where the instance was located
        destroyedBy:
          type: string
          nullable: true
          description: App-id of the destroyer
        metadata:
          type: object
          additionalProperties: true
          nullable: true
          description: Caller-provided metadata

    # ========== CHECKOUT WORKFLOW EVENTS ==========

    SceneCheckedOutEvent:
      type: object
      description: Published when a scene is locked for editing
      required:
        - eventId
        - timestamp
        - sceneId
        - checkedOutBy
        - expiresAt
      properties:
        eventId:
          type: string
          format: uuid
          description: Unique event identifier
        timestamp:
          type: string
          format: date-time
          description: When the checkout occurred
        sceneId:
          type: string
          format: uuid
          description: ID of the scene being edited
        sceneName:
          type: string
          nullable: true
          description: Name of the scene
        checkedOutBy:
          type: string
          description: Identifier of the editor (accountId or app-id)
        expiresAt:
          type: string
          format: date-time
          description: When the checkout lock expires

    SceneCommittedEvent:
      type: object
      description: Published when checkout changes are committed
      required:
        - eventId
        - timestamp
        - sceneId
        - version
        - committedBy
      properties:
        eventId:
          type: string
          format: uuid
          description: Unique event identifier
        timestamp:
          type: string
          format: date-time
          description: When the commit occurred
        sceneId:
          type: string
          format: uuid
          description: ID of the committed scene
        sceneName:
          type: string
          nullable: true
          description: Name of the scene
        version:
          type: string
          description: New version after commit
        previousVersion:
          type: string
          nullable: true
          description: Version before commit
        committedBy:
          type: string
          description: Identifier of the committer
        changesSummary:
          type: string
          nullable: true
          description: Optional summary of changes

    SceneCheckoutDiscardedEvent:
      type: object
      description: Published when a checkout is discarded without saving
      required:
        - eventId
        - timestamp
        - sceneId
        - discardedBy
      properties:
        eventId:
          type: string
          format: uuid
          description: Unique event identifier
        timestamp:
          type: string
          format: date-time
          description: When the discard occurred
        sceneId:
          type: string
          format: uuid
          description: ID of the scene
        discardedBy:
          type: string
          description: Identifier of who discarded

    SceneCheckoutExpiredEvent:
      type: object
      description: Published when a checkout lock expires due to TTL
      required:
        - eventId
        - timestamp
        - sceneId
        - expiredAt
      properties:
        eventId:
          type: string
          format: uuid
          description: Unique event identifier
        timestamp:
          type: string
          format: date-time
          description: When expiration was detected
        sceneId:
          type: string
          format: uuid
          description: ID of the scene
        sceneName:
          type: string
          nullable: true
          description: Name of the scene
        expiredAt:
          type: string
          format: date-time
          description: When the lock actually expired
        originalCheckoutBy:
          type: string
          nullable: true
          description: Who originally checked out the scene

    # ========== VALIDATION EVENTS ==========

    SceneValidationRulesUpdatedEvent:
      type: object
      description: Published when validation rules are registered or updated
      required:
        - eventId
        - timestamp
        - gameId
        - sceneType
      properties:
        eventId:
          type: string
          format: uuid
          description: Unique event identifier
        timestamp:
          type: string
          format: date-time
          description: When the update occurred
        gameId:
          type: string
          description: Game ID for the rules
        sceneType:
          type: string
          description: Scene type for the rules
        ruleCount:
          type: integer
          description: Number of active rules after update
        updatedBy:
          type: string
          nullable: true
          description: Who updated the rules

    # ========== REFERENCE INTEGRITY EVENTS ==========

    SceneReferenceBrokenEvent:
      type: object
      description: |
        Published when a referenced scene becomes unavailable.
        This is an edge case event - normally deletion is blocked if references exist.
      required:
        - eventId
        - timestamp
        - affectedSceneId
        - brokenReferenceSceneId
        - reason
      properties:
        eventId:
          type: string
          format: uuid
          description: Unique event identifier
        timestamp:
          type: string
          format: date-time
          description: When the break was detected
        affectedSceneId:
          type: string
          format: uuid
          description: Scene containing the broken reference
        affectedNodeId:
          type: string
          format: uuid
          nullable: true
          description: Node with the broken reference
        affectedNodeRefId:
          type: string
          nullable: true
          description: refId of the affected node
        brokenReferenceSceneId:
          type: string
          format: uuid
          description: Scene that is no longer available
        reason:
          type: string
          enum:
            - deleted
            - access_revoked
            - corrupted
          description: Why the reference broke

    # ========== SUPPORTING TYPES ==========

    EventTransform:
      type: object
      description: Transform for event payloads
      required:
        - position
        - rotation
        - scale
      properties:
        position:
          $ref: '#/components/schemas/EventVector3'
          description: World position
        rotation:
          $ref: '#/components/schemas/EventQuaternion'
          description: World rotation
        scale:
          $ref: '#/components/schemas/EventVector3'
          description: World scale

    EventVector3:
      type: object
      description: 3D vector for events
      required:
        - x
        - y
        - z
      properties:
        x:
          type: number
          format: double
          description: X component
        y:
          type: number
          format: double
          description: Y component
        z:
          type: number
          format: double
          description: Z component

    EventQuaternion:
      type: object
      description: Quaternion for events
      required:
        - x
        - y
        - z
        - w
      properties:
        x:
          type: number
          format: double
          description: X component
        y:
          type: number
          format: double
          description: Y component
        z:
          type: number
          format: double
          description: Z component
        w:
          type: number
          format: double
          description: W component
```

### 1.3 Schema: scene-configuration.yaml

```yaml
openapi: 3.0.0
info:
  title: Scene Service Configuration
  version: 1.0.0
  description: Configuration options for the Scene service

x-service-configuration:
  properties:
    Enabled:
      type: boolean
      default: true
      env: SCENE_ENABLED
      description: Whether the Scene service is enabled

    # Storage configuration
    AssetBucket:
      type: string
      default: "scenes"
      env: SCENE_ASSET_BUCKET
      description: lib-asset bucket for storing scene documents

    AssetContentType:
      type: string
      default: "application/x-bannou-scene+yaml"
      env: SCENE_ASSET_CONTENT_TYPE
      description: Content type for scene assets

    # Checkout configuration
    DefaultCheckoutTtlMinutes:
      type: integer
      default: 60
      env: SCENE_DEFAULT_CHECKOUT_TTL_MINUTES
      description: Default lock TTL for checkout operations in minutes

    CheckoutHeartbeatIntervalSeconds:
      type: integer
      default: 30
      env: SCENE_CHECKOUT_HEARTBEAT_INTERVAL_SECONDS
      description: Expected heartbeat interval for checkout locks

    MaxCheckoutExtensions:
      type: integer
      default: 10
      env: SCENE_MAX_CHECKOUT_EXTENSIONS
      description: Maximum number of times a checkout can be extended

    # Reference resolution
    DefaultMaxReferenceDepth:
      type: integer
      default: 3
      minimum: 1
      maximum: 10
      env: SCENE_DEFAULT_MAX_REFERENCE_DEPTH
      description: Default maximum depth for reference resolution

    # Version history
    DefaultVersionRetentionCount:
      type: integer
      default: 3
      minimum: 1
      env: SCENE_DEFAULT_VERSION_RETENTION_COUNT
      description: Default number of versions to retain per scene

    # Validation
    MaxNodeCount:
      type: integer
      default: 10000
      env: SCENE_MAX_NODE_COUNT
      description: Maximum nodes allowed in a single scene

    MaxSceneSizeBytes:
      type: integer
      default: 10485760
      env: SCENE_MAX_SCENE_SIZE_BYTES
      description: Maximum scene document size in bytes (default 10MB)

paths: {}
components:
  schemas: {}
```

### 1.4 Implementation Tasks - Phase 1

| Task | Description | Files |
|------|-------------|-------|
| 1.1 | Create schema files | `schemas/scene-api.yaml`, `scene-events.yaml`, `scene-configuration.yaml` |
| 1.2 | Run code generation | `make generate` |
| 1.3 | Create lib-scene project | `lib-scene/lib-scene.csproj` |
| 1.4 | Implement SceneService.cs | Core CRUD operations |
| 1.5 | Implement structural validation | UUID, refId uniqueness, transform validity, hierarchy integrity |
| 1.6 | Implement lib-asset integration | YAML serialization, storage, retrieval |
| 1.7 | Implement lib-state index | Scene index for efficient queries |
| 1.8 | Add unit tests | `lib-scene.tests/` |

### 1.5 State Store Keys (lib-state)

| Key Pattern | Purpose | TTL |
|-------------|---------|-----|
| `scene:index:{sceneId}` | Scene metadata for queries | None |
| `scene:by-game:{gameId}` | Set of sceneIds per game | None |
| `scene:by-type:{gameId}:{sceneType}` | Set of sceneIds per type | None |
| `scene:references:{sceneId}` | Set of sceneIds that reference this scene | None |

### 1.6 Structural Validation Rules (Always Applied)

| Rule | Description |
|------|-------------|
| `valid-uuid` | All nodeId and sceneId fields are valid UUIDs |
| `unique-refid` | refId is unique within the scene |
| `refid-pattern` | refId matches `^[a-z][a-z0-9_]*$` |
| `valid-parentid` | parentNodeId references an existing node |
| `no-cycles` | No circular parent references |
| `valid-transform` | Transform has valid position, rotation (unit quaternion), scale |
| `valid-version` | Version matches semantic versioning pattern |
| `root-no-parent` | Root node has null parentNodeId |
| `single-root` | Only one node has null parentNodeId |
| `node-count-limit` | Total nodes <= MaxNodeCount configuration |

---

## Phase 2: Instantiation and Events

**Goal**: Game servers can declare scene instantiations and consumers react.

### 2.1 Additional API Endpoints

```yaml
# Add to scene-api.yaml paths section

/scene/instantiate:
  post:
    summary: Declare that a scene was instantiated in the game world
    description: |
      Records a scene instantiation and publishes an event. This is a
      NOTIFICATION endpoint - the caller has already instantiated the scene.

      1. Validates scene exists and is accessible
      2. Publishes scene.instantiated event

      Consumers (Mapping, Actor, etc.) react to the event independently.
    operationId: instantiateScene
    tags:
      - Instance
    x-permissions:
      - role: game_server
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/InstantiateSceneRequest'
    responses:
      '200':
        description: Instantiation recorded and event published
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/InstantiateSceneResponse'
      '404':
        description: Scene not found

/scene/destroy-instance:
  post:
    summary: Declare that a scene instance was removed
    description: |
      Records instance destruction and publishes an event.
      Consumers react to clean up spatial data, despawn NPCs, etc.
    operationId: destroyInstance
    tags:
      - Instance
    x-permissions:
      - role: game_server
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/DestroyInstanceRequest'
    responses:
      '200':
        description: Destruction recorded and event published
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/DestroyInstanceResponse'
```

### 2.2 Request/Response Models

```yaml
# Add to scene-api.yaml components/schemas

InstantiateSceneRequest:
  type: object
  description: Request to record a scene instantiation
  required:
    - sceneAssetId
    - instanceId
    - regionId
    - worldTransform
  properties:
    sceneAssetId:
      type: string
      format: uuid
      description: Scene asset ID that was instantiated
    version:
      type: string
      nullable: true
      description: Specific version (null = validates latest exists)
    instanceId:
      type: string
      format: uuid
      description: Caller-provided unique instance ID
    regionId:
      type: string
      format: uuid
      description: Region where scene was placed
    worldTransform:
      $ref: '#/components/schemas/Transform'
      description: World-space transform for scene origin
    metadata:
      type: object
      additionalProperties: true
      nullable: true
      description: Caller-provided metadata passed to event

InstantiateSceneResponse:
  type: object
  description: Response confirming instantiation
  required:
    - instanceId
    - sceneVersion
  properties:
    instanceId:
      type: string
      format: uuid
      description: The instance ID
    sceneVersion:
      type: string
      description: Version that was instantiated
    eventPublished:
      type: boolean
      description: Whether the event was successfully published

DestroyInstanceRequest:
  type: object
  description: Request to record instance destruction
  required:
    - instanceId
  properties:
    instanceId:
      type: string
      format: uuid
      description: Instance ID to destroy
    regionId:
      type: string
      format: uuid
      nullable: true
      description: Region where instance was (for event metadata)
    metadata:
      type: object
      additionalProperties: true
      nullable: true
      description: Caller-provided metadata

DestroyInstanceResponse:
  type: object
  description: Response confirming destruction
  required:
    - destroyed
  properties:
    destroyed:
      type: boolean
      description: Whether destruction was recorded
    eventPublished:
      type: boolean
      description: Whether the event was successfully published
```

### 2.3 Implementation Tasks - Phase 2

| Task | Description |
|------|-------------|
| 2.1 | Add instantiate/destroy endpoints to schema |
| 2.2 | Regenerate code |
| 2.3 | Implement InstantiateSceneAsync |
| 2.4 | Implement DestroyInstanceAsync |
| 2.5 | Add integration tests |

---

## Phase 3: Versioning and Checkout

**Goal**: Multiple editors can work on scenes with conflict prevention.

### 3.1 Additional API Endpoints

```yaml
# Add to scene-api.yaml paths section

/scene/checkout:
  post:
    summary: Lock a scene for editing
    description: |
      Acquires an exclusive lock on the scene for editing.
      Returns a checkout token required for commit.
      Lock expires after TTL if not extended via heartbeat.
    operationId: checkoutScene
    tags:
      - Versioning
    x-permissions:
      - role: developer
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/CheckoutRequest'
    responses:
      '200':
        description: Checkout successful
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CheckoutResponse'
      '404':
        description: Scene not found
      '409':
        description: Scene already checked out by another user

/scene/commit:
  post:
    summary: Save changes and release lock
    description: |
      Commits the changes made during checkout, increments version,
      and releases the lock. Publishes scene.committed event.
    operationId: commitScene
    tags:
      - Versioning
    x-permissions:
      - role: developer
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/CommitRequest'
    responses:
      '200':
        description: Commit successful
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CommitResponse'
      '403':
        description: Invalid checkout token
      '409':
        description: Checkout expired

/scene/discard:
  post:
    summary: Release lock without saving changes
    description: |
      Discards any changes and releases the checkout lock.
      Scene remains at its pre-checkout version.
    operationId: discardCheckout
    tags:
      - Versioning
    x-permissions:
      - role: developer
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/DiscardRequest'
    responses:
      '200':
        description: Discard successful
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/DiscardResponse'
      '403':
        description: Invalid checkout token

/scene/heartbeat:
  post:
    summary: Extend checkout lock TTL
    description: |
      Extends the checkout lock TTL. Should be called periodically
      during editing to prevent lock expiration.
    operationId: heartbeatCheckout
    tags:
      - Versioning
    x-permissions:
      - role: developer
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/HeartbeatRequest'
    responses:
      '200':
        description: Lock extended
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/HeartbeatResponse'
      '403':
        description: Invalid checkout token
      '409':
        description: Checkout expired

/scene/history:
  post:
    summary: Get version history for a scene
    description: |
      Returns the version history for a scene, up to the configured
      retention limit per gameId.
    operationId: getSceneHistory
    tags:
      - Versioning
    x-permissions:
      - role: user
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/HistoryRequest'
    responses:
      '200':
        description: History retrieved
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/HistoryResponse'
      '404':
        description: Scene not found
```

### 3.2 Request/Response Models

```yaml
# Add to scene-api.yaml components/schemas

CheckoutRequest:
  type: object
  description: Request to checkout a scene for editing
  required:
    - sceneId
  properties:
    sceneId:
      type: string
      format: uuid
      description: Scene to checkout
    editorId:
      type: string
      nullable: true
      description: Optional editor identifier (defaults to caller identity)
    ttlMinutes:
      type: integer
      nullable: true
      description: Custom lock TTL (uses default if not specified)

CheckoutResponse:
  type: object
  description: Response containing checkout token and scene
  required:
    - checkoutToken
    - scene
    - expiresAt
  properties:
    checkoutToken:
      type: string
      description: Token required for commit/discard/heartbeat
    scene:
      $ref: '#/components/schemas/Scene'
      description: Current scene document
    expiresAt:
      type: string
      format: date-time
      description: When the checkout lock expires

CommitRequest:
  type: object
  description: Request to commit checkout changes
  required:
    - sceneId
    - checkoutToken
    - scene
  properties:
    sceneId:
      type: string
      format: uuid
      description: Scene being committed
    checkoutToken:
      type: string
      description: Checkout token from checkout response
    scene:
      $ref: '#/components/schemas/Scene'
      description: Updated scene document
    changesSummary:
      type: string
      nullable: true
      description: Optional summary of changes for audit

CommitResponse:
  type: object
  description: Response confirming commit
  required:
    - committed
    - newVersion
  properties:
    committed:
      type: boolean
      description: Whether commit was successful
    newVersion:
      type: string
      description: New version after commit
    scene:
      $ref: '#/components/schemas/Scene'
      description: Committed scene with updated metadata

DiscardRequest:
  type: object
  description: Request to discard checkout
  required:
    - sceneId
    - checkoutToken
  properties:
    sceneId:
      type: string
      format: uuid
      description: Scene to discard changes for
    checkoutToken:
      type: string
      description: Checkout token

DiscardResponse:
  type: object
  description: Response confirming discard
  required:
    - discarded
  properties:
    discarded:
      type: boolean
      description: Whether discard was successful

HeartbeatRequest:
  type: object
  description: Request to extend checkout lock
  required:
    - sceneId
    - checkoutToken
  properties:
    sceneId:
      type: string
      format: uuid
      description: Scene being edited
    checkoutToken:
      type: string
      description: Checkout token

HeartbeatResponse:
  type: object
  description: Response confirming lock extension
  required:
    - extended
    - newExpiresAt
  properties:
    extended:
      type: boolean
      description: Whether extension was successful
    newExpiresAt:
      type: string
      format: date-time
      description: New expiration time
    extensionsRemaining:
      type: integer
      description: Number of extensions remaining

HistoryRequest:
  type: object
  description: Request for scene version history
  required:
    - sceneId
  properties:
    sceneId:
      type: string
      format: uuid
      description: Scene to get history for
    limit:
      type: integer
      default: 10
      description: Maximum versions to return

HistoryResponse:
  type: object
  description: Scene version history
  required:
    - sceneId
    - versions
  properties:
    sceneId:
      type: string
      format: uuid
      description: Scene ID
    currentVersion:
      type: string
      description: Current active version
    versions:
      type: array
      items:
        $ref: '#/components/schemas/VersionInfo'
      description: Version history entries

VersionInfo:
  type: object
  description: Information about a specific version
  required:
    - version
    - createdAt
  properties:
    version:
      type: string
      description: Version string
    createdAt:
      type: string
      format: date-time
      description: When this version was created
    createdBy:
      type: string
      nullable: true
      description: Who created this version
    changesSummary:
      type: string
      nullable: true
      description: Summary of changes
    nodeCount:
      type: integer
      description: Node count at this version
```

### 3.3 Implementation Tasks - Phase 3

| Task | Description |
|------|-------------|
| 3.1 | Add checkout workflow endpoints to schema |
| 3.2 | Regenerate code |
| 3.3 | Implement checkout locking with IDistributedLockProvider |
| 3.4 | Implement commit with version increment |
| 3.5 | Implement discard |
| 3.6 | Implement heartbeat with extension limit |
| 3.7 | Implement history retrieval from lib-asset |
| 3.8 | Add background task for expired checkout detection |
| 3.9 | Add integration tests |

### 3.4 State Store Keys (lib-state) - Phase 3

| Key Pattern | Purpose | TTL |
|-------------|---------|-----|
| `scene:checkout:{sceneId}` | Checkout lock state | From config |
| `scene:checkout-extensions:{sceneId}` | Extension count | From lock |

---

## Phase 4: Validation Framework

**Goal**: Per-game, per-scene-type validation rules.

### 4.1 Additional API Endpoints

```yaml
# Add to scene-api.yaml paths section

/scene/register-validation-rules:
  post:
    summary: Register validation rules for a gameId+sceneType
    description: |
      Registers game-specific validation rules. Replaces any existing
      rules for the gameId+sceneType combination.
    operationId: registerValidationRules
    tags:
      - Validation
    x-permissions:
      - role: admin
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/RegisterValidationRulesRequest'
    responses:
      '200':
        description: Rules registered
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/RegisterValidationRulesResponse'

/scene/get-validation-rules:
  post:
    summary: Get validation rules for a gameId+sceneType
    description: |
      Retrieves the registered validation rules for a specific
      gameId and sceneType combination.
    operationId: getValidationRules
    tags:
      - Validation
    x-permissions:
      - role: user
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/GetValidationRulesRequest'
    responses:
      '200':
        description: Rules retrieved
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetValidationRulesResponse'
```

### 4.2 Validation Rule Models

```yaml
# Add to scene-api.yaml components/schemas

RegisterValidationRulesRequest:
  type: object
  description: Request to register validation rules
  required:
    - gameId
    - sceneType
    - rules
  properties:
    gameId:
      type: string
      description: Game ID for these rules
    sceneType:
      $ref: '#/components/schemas/SceneType'
      description: Scene type for these rules
    rules:
      type: array
      items:
        $ref: '#/components/schemas/ValidationRule'
      description: Validation rules to register

RegisterValidationRulesResponse:
  type: object
  description: Response confirming rule registration
  required:
    - registered
    - ruleCount
  properties:
    registered:
      type: boolean
      description: Whether registration was successful
    ruleCount:
      type: integer
      description: Number of rules registered

GetValidationRulesRequest:
  type: object
  description: Request to get validation rules
  required:
    - gameId
    - sceneType
  properties:
    gameId:
      type: string
      description: Game ID
    sceneType:
      $ref: '#/components/schemas/SceneType'
      description: Scene type

GetValidationRulesResponse:
  type: object
  description: Response containing validation rules
  required:
    - gameId
    - sceneType
  properties:
    gameId:
      type: string
      description: Game ID
    sceneType:
      $ref: '#/components/schemas/SceneType'
      description: Scene type
    rules:
      type: array
      items:
        $ref: '#/components/schemas/ValidationRule'
      description: Registered rules (empty if none)

ValidationRule:
  type: object
  description: A validation rule definition
  required:
    - ruleId
    - description
    - severity
    - ruleType
  properties:
    ruleId:
      type: string
      description: Unique rule identifier within the gameId+sceneType
    description:
      type: string
      description: Human-readable description of the rule
    severity:
      type: string
      enum:
        - error
        - warning
      description: Whether violation is an error or warning
    ruleType:
      type: string
      enum:
        - require_tag
        - require_node_type
        - forbid_tag
        - require_annotation
        - custom_expression
      description: Type of validation check
    config:
      $ref: '#/components/schemas/ValidationRuleConfig'
      description: Rule-specific configuration

ValidationRuleConfig:
  type: object
  description: Configuration for a validation rule
  properties:
    # For require_tag
    nodeType:
      type: string
      nullable: true
      description: Filter to nodes of this type
    tag:
      type: string
      nullable: true
      description: Tag to check for
    minCount:
      type: integer
      nullable: true
      description: Minimum occurrences required
    maxCount:
      type: integer
      nullable: true
      description: Maximum occurrences allowed
    # For require_annotation
    annotationPath:
      type: string
      nullable: true
      description: JSONPath to required annotation field
    # For custom_expression
    expression:
      type: string
      nullable: true
      description: Custom validation expression
```

### 4.3 Implementation Tasks - Phase 4

| Task | Description |
|------|-------------|
| 4.1 | Add validation rule endpoints to schema |
| 4.2 | Regenerate code |
| 4.3 | Implement rule storage in lib-state |
| 4.4 | Implement rule types (require_tag, etc.) |
| 4.5 | Integrate rules into validateScene |
| 4.6 | Add validation during create/update |
| 4.7 | Add unit tests for each rule type |

### 4.4 State Store Keys (lib-state) - Phase 4

| Key Pattern | Purpose | TTL |
|-------------|---------|-----|
| `scene:validation:{gameId}:{sceneType}` | Validation rules | None |

---

## Phase 5: Reference Resolution

**Goal**: Scenes can reference other scenes for composition.

### 5.1 Implementation Details

The `resolveReferences` option on `/scene/get` triggers reference resolution:

1. **Reference Detection**: Traverse scene nodes for `nodeType: reference`
2. **Extract Scene ID**: From `annotations.reference.sceneAssetId`
3. **Cycle Detection**: Track visited sceneIds in resolution chain
4. **Depth Limiting**: Stop at `maxReferenceDepth`
5. **Recursive Resolution**: Resolve nested references up to depth
6. **Error Collection**: Collect unresolved references with reasons

### 5.2 Reference Tracking for Find-Usages

When a scene is created/updated with reference nodes:
1. Extract all referenced sceneIds
2. Update `scene:references:{referencedSceneId}` sets
3. Check during delete to prevent orphaned references

### 5.3 Implementation Tasks - Phase 5

| Task | Description |
|------|-------------|
| 5.1 | Implement reference extraction from nodes |
| 5.2 | Implement cycle detection algorithm |
| 5.3 | Implement recursive resolution with depth limit |
| 5.4 | Implement reference tracking on create/update |
| 5.5 | Implement delete blocking when references exist |
| 5.6 | Add SceneReferenceBrokenEvent publishing |
| 5.7 | Add integration tests for resolution scenarios |

---

## Phase 6: Editor Integration

**Goal**: Design tools can efficiently work with scenes.

### 6.1 Additional API Endpoints

```yaml
# Add to scene-api.yaml paths section

/scene/search:
  post:
    summary: Full-text search across scenes
    description: |
      Searches scene names, descriptions, tags, and node names
      for matching content.
    operationId: searchScenes
    tags:
      - Query
    x-permissions:
      - role: user
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/SearchScenesRequest'
    responses:
      '200':
        description: Search results
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/SearchScenesResponse'

/scene/find-references:
  post:
    summary: Find scenes that reference a given scene
    description: |
      Returns all scenes that contain reference nodes pointing
      to the specified scene.
    operationId: findReferences
    tags:
      - Query
    x-permissions:
      - role: user
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/FindReferencesRequest'
    responses:
      '200':
        description: Referencing scenes found
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/FindReferencesResponse'

/scene/find-asset-usage:
  post:
    summary: Find scenes using a specific asset
    description: |
      Returns all scenes containing nodes that reference
      a specific asset ID.
    operationId: findAssetUsage
    tags:
      - Query
    x-permissions:
      - role: user
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/FindAssetUsageRequest'
    responses:
      '200':
        description: Asset usage found
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/FindAssetUsageResponse'

/scene/duplicate:
  post:
    summary: Duplicate a scene with a new ID
    description: |
      Creates a copy of a scene with a new sceneId and name.
      All node IDs are regenerated. Version resets to 1.0.0.
    operationId: duplicateScene
    tags:
      - Scene
    x-permissions:
      - role: developer
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/DuplicateSceneRequest'
    responses:
      '200':
        description: Scene duplicated
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/SceneResponse'
      '404':
        description: Source scene not found
```

### 6.2 Request/Response Models

```yaml
# Add to scene-api.yaml components/schemas

SearchScenesRequest:
  type: object
  description: Request for full-text search
  required:
    - query
  properties:
    query:
      type: string
      description: Search query text
    gameId:
      type: string
      nullable: true
      description: Filter by game ID
    sceneTypes:
      type: array
      items:
        $ref: '#/components/schemas/SceneType'
      nullable: true
      description: Filter by scene types
    offset:
      type: integer
      default: 0
      description: Pagination offset
    limit:
      type: integer
      default: 50
      description: Maximum results

SearchScenesResponse:
  type: object
  description: Search results
  required:
    - results
    - total
  properties:
    results:
      type: array
      items:
        $ref: '#/components/schemas/SearchResult'
      description: Matching scenes
    total:
      type: integer
      description: Total matches

SearchResult:
  type: object
  description: A single search result
  required:
    - scene
    - matchType
  properties:
    scene:
      $ref: '#/components/schemas/SceneSummary'
      description: Matching scene summary
    matchType:
      type: string
      enum:
        - name
        - description
        - tag
        - node_name
      description: Where the match was found
    matchContext:
      type: string
      nullable: true
      description: Context around the match

FindReferencesRequest:
  type: object
  description: Request to find referencing scenes
  required:
    - sceneId
  properties:
    sceneId:
      type: string
      format: uuid
      description: Scene ID to find references to

FindReferencesResponse:
  type: object
  description: Scenes that reference the target
  required:
    - referencingScenes
  properties:
    referencingScenes:
      type: array
      items:
        $ref: '#/components/schemas/ReferenceInfo'
      description: Scenes containing references

ReferenceInfo:
  type: object
  description: Information about a reference
  required:
    - sceneId
    - sceneName
    - nodeId
    - nodeRefId
  properties:
    sceneId:
      type: string
      format: uuid
      description: Scene containing the reference
    sceneName:
      type: string
      description: Name of the referencing scene
    nodeId:
      type: string
      format: uuid
      description: Node containing the reference
    nodeRefId:
      type: string
      description: refId of the referencing node
    nodeName:
      type: string
      description: Name of the referencing node

FindAssetUsageRequest:
  type: object
  description: Request to find asset usage
  required:
    - assetId
  properties:
    assetId:
      type: string
      format: uuid
      description: Asset ID to find usage of
    gameId:
      type: string
      nullable: true
      description: Optional game filter

FindAssetUsageResponse:
  type: object
  description: Scenes using the asset
  required:
    - usages
  properties:
    usages:
      type: array
      items:
        $ref: '#/components/schemas/AssetUsageInfo'
      description: Asset usage instances

AssetUsageInfo:
  type: object
  description: Information about asset usage
  required:
    - sceneId
    - sceneName
    - nodeId
    - nodeRefId
  properties:
    sceneId:
      type: string
      format: uuid
      description: Scene using the asset
    sceneName:
      type: string
      description: Scene name
    nodeId:
      type: string
      format: uuid
      description: Node using the asset
    nodeRefId:
      type: string
      description: refId of the node
    nodeName:
      type: string
      description: Node name
    nodeType:
      $ref: '#/components/schemas/NodeType'
      description: Type of the node

DuplicateSceneRequest:
  type: object
  description: Request to duplicate a scene
  required:
    - sourceSceneId
    - newName
  properties:
    sourceSceneId:
      type: string
      format: uuid
      description: Scene to duplicate
    newName:
      type: string
      description: Name for the duplicate
    newGameId:
      type: string
      nullable: true
      description: Optional different game ID
    newSceneType:
      $ref: '#/components/schemas/SceneType'
      nullable: true
      description: Optional different scene type
```

### 6.3 Implementation Tasks - Phase 6

| Task | Description |
|------|-------------|
| 6.1 | Add query/duplicate endpoints to schema |
| 6.2 | Regenerate code |
| 6.3 | Implement search with text indexing |
| 6.4 | Implement find-references using tracking sets |
| 6.5 | Implement find-asset-usage with node traversal |
| 6.6 | Implement duplicate with ID regeneration |
| 6.7 | Add integration tests |

### 6.4 State Store Keys (lib-state) - Phase 6

| Key Pattern | Purpose | TTL |
|-------------|---------|-----|
| `scene:assets:{assetId}` | Set of sceneIds using this asset | None |

---

## Complete Event Summary

| Event | Topic | Publisher | Trigger |
|-------|-------|-----------|---------|
| `SceneCreatedEvent` | `scene.created` | Phase 1 | Scene document created |
| `SceneUpdatedEvent` | `scene.updated` | Phase 1 | Scene document modified |
| `SceneDeletedEvent` | `scene.deleted` | Phase 1 | Scene document soft-deleted |
| `SceneInstantiatedEvent` | `scene.instantiated` | Phase 2 | Scene placed in game world |
| `SceneDestroyedEvent` | `scene.destroyed` | Phase 2 | Scene instance removed |
| `SceneCheckedOutEvent` | `scene.checked_out` | Phase 3 | Scene locked for editing |
| `SceneCommittedEvent` | `scene.committed` | Phase 3 | Checkout changes saved |
| `SceneCheckoutDiscardedEvent` | `scene.checkout.discarded` | Phase 3 | Checkout discarded |
| `SceneCheckoutExpiredEvent` | `scene.checkout.expired` | Phase 3 | Lock TTL exceeded |
| `SceneValidationRulesUpdatedEvent` | `scene.validation_rules.updated` | Phase 4 | Rules registered/updated |
| `SceneReferenceBrokenEvent` | `scene.reference.broken` | Phase 5 | Referenced scene unavailable |

---

## File Structure After Implementation

```
lib-scene/
├── Generated/                              # Auto-generated (never edit)
│   ├── SceneController.cs
│   ├── ISceneService.cs
│   ├── SceneModels.cs
│   ├── SceneClient.cs
│   ├── SceneServiceConfiguration.cs
│   └── ScenePermissionRegistration.cs
├── SceneService.cs                         # Main business logic (partial)
├── SceneServiceEvents.cs                   # Event handler implementations
├── Services/                               # Helper services
│   ├── ISceneValidationService.cs
│   ├── SceneValidationService.cs
│   ├── ISceneReferenceService.cs
│   └── SceneReferenceService.cs
└── lib-scene.csproj

lib-scene.tests/
├── SceneServiceTests.cs
├── SceneValidationTests.cs
├── SceneReferenceTests.cs
└── lib-scene.tests.csproj

http-tester/Tests/
└── SceneTests.cs                           # HTTP integration tests

edge-tester/Tests/
└── SceneTests.cs                           # WebSocket edge tests
```

---

## Implementation Order

1. **Phase 1**: Core Schema and Storage (foundation)
2. **Phase 2**: Instantiation and Events (enables consumer development)
3. **Phase 3**: Versioning and Checkout (collaborative editing)
4. **Phase 4**: Validation Framework (content quality)
5. **Phase 5**: Reference Resolution (composition)
6. **Phase 6**: Editor Integration (tooling)

Each phase is independently deployable and adds incremental value.

---

## Tenet Compliance Checklist

| Tenet | Compliance |
|-------|------------|
| T1: Schema-First | All APIs, events, config defined in YAML |
| T2: Code Generation | Using standard 8-component pipeline |
| T4: Infrastructure Libs | Using lib-asset, lib-state, lib-messaging, lib-mesh |
| T5: Event-Driven | All state changes publish typed events |
| T6: Service Implementation | Partial class with standard dependencies |
| T7: Error Handling | Try-catch with ApiException distinction |
| T8: Return Pattern | (StatusCodes, TResponse?) tuples |
| T9: Multi-Instance Safety | Distributed locks for checkout, no in-memory state |
| T10: Logging Standards | Structured logging throughout |
| T13: X-Permissions | All endpoints declare x-permissions |
| T16: Naming Conventions | Consistent patterns throughout |
| T19: XML Documentation | All schema properties have descriptions |
| T20: JSON Serialization | BannouJson for all serialization |
| T21: Configuration-First | Generated configuration class |
| T23: Async Pattern | All async methods properly awaited |

---

*Document Status: APPROVED PLAN - Ready for Phase 1 implementation*
