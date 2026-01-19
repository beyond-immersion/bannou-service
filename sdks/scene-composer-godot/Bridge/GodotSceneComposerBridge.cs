using BeyondImmersion.Bannou.SceneComposer.Abstractions;
using BeyondImmersion.Bannou.SceneComposer.Gizmo;
using BeyondImmersion.Bannou.SceneComposer.Godot.Loaders;
using BeyondImmersion.Bannou.SceneComposer.Godot.Math;
using BeyondImmersion.Bannou.SceneComposer.Math;
using Godot;
using GodotVec2 = Godot.Vector2;
using GodotVec3 = Godot.Vector3;
using SdkColor = BeyondImmersion.Bannou.SceneComposer.Math.Color;
using SdkQuat = BeyondImmersion.Bannou.SceneComposer.Math.Quaternion;
using SdkRay = BeyondImmersion.Bannou.SceneComposer.Math.Ray;
using SdkVec2 = BeyondImmersion.Bannou.SceneComposer.Abstractions.Vector2;
using SdkVec3 = BeyondImmersion.Bannou.SceneComposer.Math.Vector3;

namespace BeyondImmersion.Bannou.SceneComposer.Godot.Bridge;

/// <summary>
/// Godot 4.x implementation of ISceneComposerBridge.
/// Provides entity lifecycle management, picking, gizmo rendering, and camera operations.
/// </summary>
public class GodotSceneComposerBridge : ISceneComposerBridge
{
    #region Private Fields

    private readonly Dictionary<Guid, Node3D> _entities = new();
    private readonly Dictionary<Guid, NodeType> _entityTypes = new();
    private readonly Node3D _sceneRoot;
    private readonly Camera3D _camera;
    private readonly Viewport _viewport;
    private readonly IAssetLoader? _assetLoader;

    // Selection state tracking for visual feedback
    private readonly HashSet<Guid> _selectedEntities = new();
    private readonly HashSet<Guid> _hoveredEntities = new();

    // Gizmo rendering (placeholder - will be replaced with GodotGizmoRenderer)
    private Node3D? _gizmoRoot;

    /// <summary>
    /// Whether the gizmo is currently visible.
    /// </summary>
    public bool IsGizmoVisible { get; private set; }

    // Placeholder mesh for entities without assets
    private Mesh? _placeholderMesh;

    #endregion

    #region Constructor

    /// <summary>
    /// Create a new Godot scene composer bridge.
    /// </summary>
    /// <param name="sceneRoot">Root node where entities will be created.</param>
    /// <param name="camera">Camera for picking and camera operations.</param>
    /// <param name="viewport">Viewport for screen-to-world conversions.</param>
    /// <param name="assetLoader">Optional asset loader for loading meshes/textures from Bannou bundles.</param>
    public GodotSceneComposerBridge(
        Node3D sceneRoot,
        Camera3D camera,
        Viewport viewport,
        IAssetLoader? assetLoader = null)
    {
        _sceneRoot = sceneRoot ?? throw new ArgumentNullException(nameof(sceneRoot));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        _assetLoader = assetLoader;

        // Create placeholder mesh (small box)
        _placeholderMesh = CreatePlaceholderMesh();

        // Create gizmo root node
        _gizmoRoot = new Node3D { Name = "GizmoRoot" };
        _sceneRoot.AddChild(_gizmoRoot);
    }

    #endregion

    #region Entity Lifecycle

    /// <inheritdoc />
    public object CreateEntity(Guid nodeId, NodeType nodeType, Transform worldTransform, AssetReference? asset)
    {
        if (_entities.ContainsKey(nodeId))
        {
            GD.PushWarning($"Entity {nodeId} already exists, destroying existing one");
            DestroyEntity(nodeId);
        }

        Node3D entity = CreateNodeForType(nodeType, nodeId);
        entity.Name = nodeId.ToString();

        // Apply transform
        entity.GlobalTransform = worldTransform.ToGodot();

        // Store metadata for roundtrip
        entity.SetMeta("ComposerNodeId", nodeId.ToString());
        entity.SetMeta("ComposerNodeType", (int)nodeType);

        // Add to scene and tracking
        _sceneRoot.AddChild(entity);
        _entities[nodeId] = entity;
        _entityTypes[nodeId] = nodeType;

        // Load asset if provided
        if (asset.HasValue && asset.Value.IsValid)
        {
            _ = SetEntityAssetAsync(nodeId, asset.Value, CancellationToken.None);
        }

        return entity;
    }

    /// <inheritdoc />
    public void DestroyEntity(Guid nodeId)
    {
        if (!_entities.TryGetValue(nodeId, out var entity))
        {
            return;
        }

        // Remove from tracking
        _entities.Remove(nodeId);
        _entityTypes.Remove(nodeId);
        _selectedEntities.Remove(nodeId);
        _hoveredEntities.Remove(nodeId);

        // Remove from scene tree
        entity.GetParent()?.RemoveChild(entity);
        entity.QueueFree();
    }

    /// <inheritdoc />
    public void SetEntityParent(Guid nodeId, Guid? parentNodeId)
    {
        if (!_entities.TryGetValue(nodeId, out var entity))
        {
            GD.PushWarning($"Entity {nodeId} not found for reparenting");
            return;
        }

        Node3D newParent;
        if (parentNodeId.HasValue && _entities.TryGetValue(parentNodeId.Value, out var parentEntity))
        {
            newParent = parentEntity;
        }
        else
        {
            newParent = _sceneRoot;
        }

        // Preserve world transform during reparent
        var worldTransform = entity.GlobalTransform;

        // Reparent
        entity.GetParent()?.RemoveChild(entity);
        newParent.AddChild(entity);

        // Restore world transform (Godot will calculate new local transform)
        entity.GlobalTransform = worldTransform;
    }

    #endregion

    #region Transform

    /// <inheritdoc />
    public void UpdateEntityTransform(Guid nodeId, Transform worldTransform)
    {
        if (!_entities.TryGetValue(nodeId, out var entity))
        {
            GD.PushWarning($"Entity {nodeId} not found for transform update");
            return;
        }

        entity.GlobalTransform = worldTransform.ToGodot();
    }

    #endregion

    #region Asset Binding

    /// <inheritdoc />
    public async Task SetEntityAssetAsync(Guid nodeId, AssetReference asset, CancellationToken ct = default)
    {
        if (!_entities.TryGetValue(nodeId, out var entity))
        {
            GD.PushWarning($"Entity {nodeId} not found for asset binding");
            return;
        }

        // Try bundle-based loading first if we have an asset loader and a bundle ID
        if (_assetLoader != null && !string.IsNullOrEmpty(asset.BundleId))
        {
            var loadedFromBundle = await TryLoadFromBundleAsync(entity, asset, ct);
            if (loadedFromBundle)
                return;
        }

        // Fall back to ResourceLoader for res:// paths
        await LoadFromResourceLoaderAsync(entity, asset, ct);
    }

    /// <summary>
    /// Attempts to load an asset from a Bannou bundle using the asset loader.
    /// </summary>
    /// <param name="entity">Entity to apply the asset to.</param>
    /// <param name="asset">Asset reference containing bundle and asset IDs.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the asset was successfully loaded from a bundle.</returns>
    private async Task<bool> TryLoadFromBundleAsync(Node3D entity, AssetReference asset, CancellationToken ct)
    {
        if (_assetLoader == null)
            return false;

        try
        {
            // Try to load as mesh first (most common for scene entities)
            var mesh = await _assetLoader.LoadMeshAsync(asset.BundleId, asset.AssetId, asset.VariantId, ct);
            if (mesh != null)
            {
                var meshInstance = FindChildOfType<MeshInstance3D>(entity);
                if (meshInstance != null)
                {
                    meshInstance.Mesh = mesh;

                    // Update collision shape to match mesh
                    var staticBody = FindChildOfType<StaticBody3D>(entity);
                    if (staticBody != null)
                    {
                        var collisionShape = FindChildOfType<CollisionShape3D>(staticBody);
                        if (collisionShape != null)
                        {
                            collisionShape.Shape = mesh.CreateConvexShape();
                        }
                    }
                }
                return true;
            }

            // Try texture as fallback
            var texture = await _assetLoader.LoadTextureAsync(asset.BundleId, asset.AssetId, asset.VariantId, ct);
            if (texture != null)
            {
                // For textures, we could apply to a material on the mesh
                var meshInstance = FindChildOfType<MeshInstance3D>(entity);
                if (meshInstance?.Mesh != null)
                {
                    var material = new StandardMaterial3D { AlbedoTexture = texture };
                    meshInstance.MaterialOverride = material;
                }
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Failed to load asset '{asset.AssetId}' from bundle '{asset.BundleId}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Loads an asset using Godot's ResourceLoader (for res:// paths).
    /// </summary>
    /// <param name="entity">Entity to apply the asset to.</param>
    /// <param name="asset">Asset reference containing the resource path.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task LoadFromResourceLoaderAsync(Node3D entity, AssetReference asset, CancellationToken ct)
    {
        // For Godot, we use AssetId as the res:// path directly
        // BundleId can be ignored or used as a prefix
        var resourcePath = asset.AssetId;

        // Validate path format
        if (!resourcePath.StartsWith("res://"))
        {
            // If no prefix, assume it's a relative path and prepend res://
            resourcePath = $"res://{resourcePath}";
        }

        try
        {
            // Check if file exists
            if (!ResourceLoader.Exists(resourcePath))
            {
                GD.PushWarning($"Asset not found: {resourcePath}");
                return;
            }

            // Use threaded loading for async
            var error = ResourceLoader.LoadThreadedRequest(resourcePath);
            if (error != Error.Ok)
            {
                GD.PushError($"Failed to start loading asset {resourcePath}: {error}");
                return;
            }

            // Poll until loaded
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var status = ResourceLoader.LoadThreadedGetStatus(resourcePath);
                if (status == ResourceLoader.ThreadLoadStatus.Loaded)
                {
                    break;
                }
                if (status == ResourceLoader.ThreadLoadStatus.Failed)
                {
                    GD.PushError($"Failed to load asset: {resourcePath}");
                    return;
                }

                await Task.Delay(10, ct);
            }

            var resource = ResourceLoader.LoadThreadedGet(resourcePath);

            // Apply the resource based on type
            ApplyResourceToEntity(entity, resource);
        }
        catch (OperationCanceledException)
        {
            // Loading was cancelled
        }
        catch (Exception ex)
        {
            GD.PushError($"Error loading asset {resourcePath}: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public void ClearEntityAsset(Guid nodeId)
    {
        if (!_entities.TryGetValue(nodeId, out var entity))
        {
            return;
        }

        // Find and clear mesh instance
        var meshInstance = FindChildOfType<MeshInstance3D>(entity);
        if (meshInstance != null)
        {
            meshInstance.Mesh = _placeholderMesh;
        }
    }

    #endregion

    #region Selection Visualization

    /// <inheritdoc />
    public void SetEntitySelected(Guid nodeId, bool selected)
    {
        if (!_entities.TryGetValue(nodeId, out var entity))
        {
            return;
        }

        if (selected)
        {
            _selectedEntities.Add(nodeId);
        }
        else
        {
            _selectedEntities.Remove(nodeId);
        }

        // Apply selection visual (e.g., outline, material override)
        ApplySelectionVisual(entity, selected);
    }

    /// <inheritdoc />
    public void SetEntityHovered(Guid nodeId, bool hovered)
    {
        if (!_entities.TryGetValue(nodeId, out var entity))
        {
            return;
        }

        if (hovered)
        {
            _hoveredEntities.Add(nodeId);
        }
        else
        {
            _hoveredEntities.Remove(nodeId);
        }

        // Apply hover visual (if not selected)
        if (!_selectedEntities.Contains(nodeId))
        {
            ApplyHoverVisual(entity, hovered);
        }
    }

    /// <inheritdoc />
    public void SetEntityVisible(Guid nodeId, bool visible)
    {
        if (!_entities.TryGetValue(nodeId, out var entity))
        {
            return;
        }

        entity.Visible = visible;
    }

    #endregion

    #region Gizmo Rendering

    /// <inheritdoc />
    public void RenderGizmo(SdkVec3 position, SdkQuat rotation, GizmoMode mode, GizmoAxis activeAxis, double scale)
    {
        if (_gizmoRoot == null || mode == GizmoMode.None)
        {
            HideGizmo();
            return;
        }

        IsGizmoVisible = true;
        _gizmoRoot.Visible = true;
        _gizmoRoot.GlobalPosition = position.ToGodot();
        _gizmoRoot.Quaternion = rotation.ToGodot();

        // TODO: Implement actual gizmo rendering using GodotGizmoRenderer
        // For now, just position the gizmo root
    }

    /// <inheritdoc />
    public void HideGizmo()
    {
        if (_gizmoRoot != null)
        {
            _gizmoRoot.Visible = false;
        }
        IsGizmoVisible = false;
    }

    /// <inheritdoc />
    public void RenderAttachmentPoints(IReadOnlyList<AttachmentPointInfo> points, int highlightedIndex = -1)
    {
        // TODO: Implement attachment point visualization
        // Could use small sphere meshes at each attachment point
    }

    #endregion

    #region Picking

    /// <inheritdoc />
    public Guid? PickEntity(SdkRay ray)
    {
        var (origin, direction) = ray.ToGodot();

        // First try physics-based picking
        var result = PickEntityWithPhysics(origin, direction);
        if (result.HasValue)
        {
            return result;
        }

        // Fallback to AABB-based picking
        return PickEntityWithAABB(origin, direction);
    }

    /// <inheritdoc />
    public IReadOnlyList<PickResult> PickAllEntities(SdkRay ray)
    {
        var (origin, direction) = ray.ToGodot();
        var results = new List<PickResult>();

        // Check all entities with AABB intersection
        foreach (var (nodeId, entity) in _entities)
        {
            var aabb = GetEntityAABB(entity);
            if (!aabb.HasValue) continue;

            var worldAabb = TransformAABB(aabb.Value, entity.GlobalTransform);

            if (RayMath.RayIntersectsAabb(origin, direction, worldAabb, out var hitDistance))
            {
                var hitPoint = origin + direction * hitDistance;
                results.Add(new PickResult(
                    nodeId,
                    hitDistance,
                    hitPoint.ToSdk(),
                    SdkVec3.UnitY // TODO: Calculate actual normal
                ));
            }
        }

        // Sort by distance
        results.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return results;
    }

    /// <inheritdoc />
    public IReadOnlyList<Guid> PickEntitiesInRect(SdkVec2 screenMin, SdkVec2 screenMax)
    {
        var results = new List<Guid>();
        var viewportSize = _viewport.GetVisibleRect().Size;

        foreach (var (nodeId, entity) in _entities)
        {
            // Project entity position to screen space
            var worldPos = entity.GlobalPosition;
            var screenPos = _camera.UnprojectPosition(worldPos);

            // Check if within rectangle
            if (screenPos.X >= screenMin.X && screenPos.X <= screenMax.X &&
                screenPos.Y >= screenMin.Y && screenPos.Y <= screenMax.Y)
            {
                // Also check if in front of camera
                if (!_camera.IsPositionBehind(worldPos))
                {
                    results.Add(nodeId);
                }
            }
        }

        return results;
    }

    /// <inheritdoc />
    public GizmoAxis PickGizmoAxis(SdkRay ray, SdkVec3 gizmoPosition, SdkQuat gizmoRotation, GizmoMode mode, double gizmoScale)
    {
        if (mode == GizmoMode.None)
        {
            return GizmoAxis.None;
        }

        // Transform ray to gizmo local space
        var worldOrigin = ray.Origin.ToGodot();
        var worldDir = ray.Direction.ToGodot();
        var gizmoPos = gizmoPosition.ToGodot();
        var gizmoRot = gizmoRotation.ToGodot();

        var invRot = gizmoRot.Inverse();
        var localOrigin = invRot * (worldOrigin - gizmoPos);
        var localDir = invRot * worldDir;

        var pickRadius = (float)(0.1 * gizmoScale);
        var handleLength = (float)(1.0 * gizmoScale);

        GizmoAxis closest = GizmoAxis.None;
        float closestDistance = float.MaxValue;

        // Test X axis
        if (TestAxisHit(localOrigin, localDir, GodotVec3.Right, handleLength, pickRadius, out var distX) && distX < closestDistance)
        {
            closestDistance = distX;
            closest = GizmoAxis.X;
        }

        // Test Y axis
        if (TestAxisHit(localOrigin, localDir, GodotVec3.Up, handleLength, pickRadius, out var distY) && distY < closestDistance)
        {
            closestDistance = distY;
            closest = GizmoAxis.Y;
        }

        // Test Z axis
        if (TestAxisHit(localOrigin, localDir, GodotVec3.Back, handleLength, pickRadius, out var distZ) && distZ < closestDistance)
        {
            closestDistance = distZ;
            closest = GizmoAxis.Z;
        }

        // TODO: Test plane handles for translate mode

        return closest;
    }

    #endregion

    #region Camera

    /// <inheritdoc />
    public void FocusCamera(SdkVec3 target, double distance)
    {
        // Calculate new camera position
        var targetGodot = target.ToGodot();
        var forward = -_camera.GlobalTransform.Basis.Z;
        var newPosition = targetGodot - forward * (float)distance;

        // Animate or instantly move camera
        // For now, instant move
        _camera.GlobalPosition = newPosition;

        // Make camera look at target
        _camera.LookAt(targetGodot);
    }

    /// <inheritdoc />
    public SdkRay GetMouseRay(SdkVec2 screenPosition)
    {
        var screenPosGodot = screenPosition.ToGodot();

        // Use Godot's built-in projection
        var origin = _camera.ProjectRayOrigin(screenPosGodot);
        var direction = _camera.ProjectRayNormal(screenPosGodot);

        return GodotTypeConverter.ToSdkRay(origin, direction);
    }

    /// <inheritdoc />
    public SdkVec3 GetCameraPosition()
    {
        return _camera.GlobalPosition.ToSdk();
    }

    /// <inheritdoc />
    public SdkVec3 GetCameraForward()
    {
        // Godot's forward is -Z
        return (-_camera.GlobalTransform.Basis.Z).ToSdk();
    }

    #endregion

    #region Thumbnails

    /// <inheritdoc />
    public async Task<byte[]?> GetAssetThumbnailAsync(AssetReference asset, int width, int height, CancellationToken ct = default)
    {
        // If we have an asset loader, try to get thumbnail from there
        if (_assetLoader != null && !string.IsNullOrEmpty(asset.BundleId))
        {
            return await _assetLoader.GetThumbnailAsync(asset.BundleId, asset.AssetId, width, height, ct);
        }

        // Thumbnails require rendering the asset to a viewport texture
        // This is complex and may not be needed for initial implementation
        // TODO: Implement thumbnail generation for res:// resources
        return null;
    }

    #endregion

    #region Debug Visualization

    /// <inheritdoc />
    public void DrawDebugLine(SdkVec3 start, SdkVec3 end, SdkColor color, float duration = 0f)
    {
        // Godot 4 doesn't have built-in debug drawing in release builds
        // Use DebugDraw3D addon or custom ImmediateMesh
        // For now, log the request
        GD.Print($"Debug line: {start} -> {end}");
    }

    /// <inheritdoc />
    public void DrawDebugSphere(SdkVec3 center, double radius, SdkColor color, float duration = 0f)
    {
        GD.Print($"Debug sphere: {center}, r={radius}");
    }

    /// <inheritdoc />
    public void DrawDebugBox(SdkVec3 center, SdkVec3 size, SdkQuat rotation, SdkColor color, float duration = 0f)
    {
        GD.Print($"Debug box: {center}, size={size}");
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Create the appropriate Godot node type for a scene node type.
    /// </summary>
    private Node3D CreateNodeForType(NodeType nodeType, Guid nodeId)
    {
        Node3D root;

        switch (nodeType)
        {
            case NodeType.Group:
                root = new Node3D();
                break;

            case NodeType.Mesh:
                root = new Node3D();
                MeshInstance3D? meshInstance = new MeshInstance3D
                {
                    Name = "MeshInstance",
                    Mesh = _placeholderMesh
                };
                root.AddChild(meshInstance);
                meshInstance = null; // Ownership transferred to parent

                // Add collision shape for physics picking
                StaticBody3D? staticBody = new StaticBody3D { Name = "StaticBody" };
                CollisionShape3D? collisionShape = new CollisionShape3D
                {
                    Name = "CollisionShape",
                    Shape = new BoxShape3D { Size = new GodotVec3(1, 1, 1) }
                };
                staticBody.AddChild(collisionShape);
                collisionShape = null; // Ownership transferred to parent
                root.AddChild(staticBody);
                staticBody = null; // Ownership transferred to parent
                break;

            case NodeType.Marker:
                root = new Marker3D();
                break;

            case NodeType.Volume:
                var area = new Area3D();
                CollisionShape3D? volumeShape = new CollisionShape3D
                {
                    Name = "CollisionShape",
                    Shape = new BoxShape3D { Size = new GodotVec3(1, 1, 1) }
                };
                area.AddChild(volumeShape);
                volumeShape = null; // Ownership transferred to parent
                root = area;
                break;

            case NodeType.Emitter:
                root = new GpuParticles3D();
                break;

            case NodeType.Reference:
                root = new Node3D();
                root.SetMeta("ReferenceNode", true);
                break;

            case NodeType.Custom:
            default:
                root = new Node3D();
                break;
        }

        return root;
    }

    /// <summary>
    /// Create a placeholder mesh (small colored cube).
    /// </summary>
    private static Mesh CreatePlaceholderMesh()
    {
        var mesh = new BoxMesh
        {
            Size = new GodotVec3(0.5f, 0.5f, 0.5f)
        };

        // Create a material
        var material = new StandardMaterial3D
        {
            AlbedoColor = new global::Godot.Color(0.5f, 0.5f, 0.5f, 0.8f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha
        };
        mesh.Material = material;

        return mesh;
    }

    /// <summary>
    /// Apply a loaded resource to an entity.
    /// </summary>
    private void ApplyResourceToEntity(Node3D entity, Resource resource)
    {
        switch (resource)
        {
            case Mesh mesh:
                var meshInstance = FindChildOfType<MeshInstance3D>(entity);
                if (meshInstance != null)
                {
                    meshInstance.Mesh = mesh;

                    // Update collision shape to match mesh
                    var staticBody = FindChildOfType<StaticBody3D>(entity);
                    if (staticBody != null)
                    {
                        var collisionShape = FindChildOfType<CollisionShape3D>(staticBody);
                        if (collisionShape != null)
                        {
                            collisionShape.Shape = mesh.CreateConvexShape();
                        }
                    }
                }
                break;

            case PackedScene packedScene:
                // Instantiate scene as child
                var instance = packedScene.Instantiate<Node3D>();
                entity.AddChild(instance);
                break;

            default:
                GD.PushWarning($"Unsupported resource type: {resource.GetType().Name}");
                break;
        }
    }

    /// <summary>
    /// Find a child node of a specific type.
    /// </summary>
    private static T? FindChildOfType<T>(Node parent) where T : Node
    {
        foreach (var child in parent.GetChildren())
        {
            if (child is T typed)
            {
                return typed;
            }

            var found = FindChildOfType<T>(child);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    /// <summary>
    /// Apply selection visual to an entity.
    /// </summary>
    private void ApplySelectionVisual(Node3D entity, bool selected)
    {
        // Simple implementation: modify material
        var meshInstance = FindChildOfType<MeshInstance3D>(entity);
        if (meshInstance != null)
        {
            if (selected)
            {
                // Apply selection highlight (e.g., outline or color tint)
                // TODO: Implement proper selection shader/outline
                meshInstance.SetMeta("IsSelected", true);
            }
            else
            {
                meshInstance.RemoveMeta("IsSelected");
            }
        }
    }

    /// <summary>
    /// Apply hover visual to an entity.
    /// </summary>
    private void ApplyHoverVisual(Node3D entity, bool hovered)
    {
        var meshInstance = FindChildOfType<MeshInstance3D>(entity);
        if (meshInstance != null)
        {
            if (hovered)
            {
                meshInstance.SetMeta("IsHovered", true);
            }
            else
            {
                meshInstance.RemoveMeta("IsHovered");
            }
        }
    }

    /// <summary>
    /// Pick entity using physics raycast.
    /// </summary>
    private Guid? PickEntityWithPhysics(GodotVec3 origin, GodotVec3 direction)
    {
        var spaceState = _sceneRoot.GetWorld3D()?.DirectSpaceState;
        if (spaceState == null)
        {
            return null;
        }

        using var query = PhysicsRayQueryParameters3D.Create(origin, origin + direction * 1000f);
        var result = spaceState.IntersectRay(query);

        if (result.Count > 0 && result.TryGetValue("collider", out var colliderVariant))
        {
            var collider = colliderVariant.As<Node3D>();
            if (collider != null)
            {
                // Walk up parent chain to find entity root
                var current = collider;
                while (current != null && current != _sceneRoot)
                {
                    var nodeIdMeta = current.GetMeta("ComposerNodeId", "").AsString();
                    if (!string.IsNullOrEmpty(nodeIdMeta) && Guid.TryParse(nodeIdMeta, out var nodeId))
                    {
                        return nodeId;
                    }
                    current = current.GetParent() as Node3D;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Pick entity using AABB intersection (fallback).
    /// </summary>
    private Guid? PickEntityWithAABB(GodotVec3 origin, GodotVec3 direction)
    {
        float closestDistance = float.MaxValue;
        Guid? closestEntity = null;

        foreach (var (nodeId, entity) in _entities)
        {
            var aabb = GetEntityAABB(entity);
            if (!aabb.HasValue) continue;

            var worldAabb = TransformAABB(aabb.Value, entity.GlobalTransform);

            if (RayMath.RayIntersectsAabb(origin, direction, worldAabb, out var hitDistance))
            {
                if (hitDistance < closestDistance)
                {
                    closestDistance = hitDistance;
                    closestEntity = nodeId;
                }
            }
        }

        return closestEntity;
    }

    /// <summary>
    /// Get AABB for an entity.
    /// </summary>
    private static Aabb? GetEntityAABB(Node3D entity)
    {
        var meshInstance = FindChildOfType<MeshInstance3D>(entity);
        if (meshInstance?.Mesh != null)
        {
            return meshInstance.GetAabb();
        }

        // Default small AABB for non-mesh entities
        return new Aabb(new GodotVec3(-0.25f, -0.25f, -0.25f), new GodotVec3(0.5f, 0.5f, 0.5f));
    }

    /// <summary>
    /// Transform a local AABB to world space.
    /// </summary>
    private static Aabb TransformAABB(Aabb local, Transform3D transform)
    {
        // Get all 8 corners and transform them
        var corners = new GodotVec3[8];
        corners[0] = local.Position;
        corners[1] = local.Position + new GodotVec3(local.Size.X, 0, 0);
        corners[2] = local.Position + new GodotVec3(0, local.Size.Y, 0);
        corners[3] = local.Position + new GodotVec3(0, 0, local.Size.Z);
        corners[4] = local.Position + new GodotVec3(local.Size.X, local.Size.Y, 0);
        corners[5] = local.Position + new GodotVec3(local.Size.X, 0, local.Size.Z);
        corners[6] = local.Position + new GodotVec3(0, local.Size.Y, local.Size.Z);
        corners[7] = local.End;

        // Transform corners and compute new AABB
        var min = transform * corners[0];
        var max = min;

        for (int i = 1; i < 8; i++)
        {
            var p = transform * corners[i];
            min = new GodotVec3(
                Mathf.Min(min.X, p.X),
                Mathf.Min(min.Y, p.Y),
                Mathf.Min(min.Z, p.Z));
            max = new GodotVec3(
                Mathf.Max(max.X, p.X),
                Mathf.Max(max.Y, p.Y),
                Mathf.Max(max.Z, p.Z));
        }

        return new Aabb(min, max - min);
    }

    /// <summary>
    /// Test if a ray hits an axis handle.
    /// </summary>
    private static bool TestAxisHit(GodotVec3 rayOrigin, GodotVec3 rayDir, GodotVec3 axis, float length, float radius, out float distance)
    {
        distance = float.MaxValue;

        // Find closest point on ray to axis line
        var rayToAxis = GodotVec3.Zero - rayOrigin;
        var t = rayToAxis.Dot(rayDir);

        if (t < 0)
        {
            return false;
        }

        var closestOnRay = rayOrigin + rayDir * t;
        var projOnAxis = closestOnRay.Dot(axis);

        if (projOnAxis < 0 || projOnAxis > length)
        {
            return false;
        }

        var closestOnAxis = axis * projOnAxis;
        var dist = closestOnRay.DistanceTo(closestOnAxis);

        if (dist > radius)
        {
            return false;
        }

        distance = t;
        return true;
    }

    #endregion
}
