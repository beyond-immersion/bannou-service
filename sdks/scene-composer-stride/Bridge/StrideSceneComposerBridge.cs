using BeyondImmersion.Bannou.SceneComposer.Abstractions;
using BeyondImmersion.Bannou.SceneComposer.Gizmo;
using BeyondImmersion.Bannou.SceneComposer.Math;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Physics;
using Stride.Rendering;
using Stride.Rendering.Colors;
using Stride.Rendering.Compositing;
using System.Collections.Concurrent;
using SdkColor = BeyondImmersion.Bannou.SceneComposer.Math.Color;
using SdkQuaternion = BeyondImmersion.Bannou.SceneComposer.Math.Quaternion;
using SdkRay = BeyondImmersion.Bannou.SceneComposer.Math.Ray;
using SdkTransform = BeyondImmersion.Bannou.SceneComposer.Math.Transform;
using SdkVector2 = BeyondImmersion.Bannou.SceneComposer.Abstractions.Vector2;
using SdkVector3 = BeyondImmersion.Bannou.SceneComposer.Math.Vector3;
using StrideRay = Stride.Core.Mathematics.Ray;
using StrideVector3 = Stride.Core.Mathematics.Vector3;

namespace BeyondImmersion.Bannou.SceneComposer.Stride.Bridge;

/// <summary>
/// Stride implementation of ISceneComposerBridge.
/// Manages Stride Entity lifecycle and rendering for the scene composer.
/// </summary>
public class StrideSceneComposerBridge : ISceneComposerBridge
{
    private readonly Scene _scene;
    private readonly CameraComponent _camera;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly IAssetLoader? _assetLoader;

    /// <summary>
    /// Maps node IDs to their Stride entities.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, Entity> _entities = new();

    /// <summary>
    /// Placeholder model shown when asset is loading or failed.
    /// </summary>
    private Model? _placeholderModel;

    /// <summary>
    /// Material for selected entity highlight.
    /// </summary>
    private Material? _selectionMaterial;

    /// <summary>
    /// Material for hovered entity highlight.
    /// </summary>
    private Material? _hoverMaterial;

    /// <summary>
    /// The gizmo renderer for transform manipulation.
    /// </summary>
    private readonly StrideGizmoRenderer? _gizmoRenderer;

    /// <summary>
    /// Create a Stride bridge for scene composition.
    /// </summary>
    /// <param name="scene">The Stride scene to add entities to.</param>
    /// <param name="camera">The camera component for picking and camera operations.</param>
    /// <param name="graphicsDevice">The graphics device for rendering.</param>
    /// <param name="assetLoader">Optional asset loader for loading models/textures.</param>
    /// <param name="gizmoRenderer">Optional gizmo renderer for transform gizmos.</param>
    public StrideSceneComposerBridge(
        Scene scene,
        CameraComponent camera,
        GraphicsDevice graphicsDevice,
        IAssetLoader? assetLoader = null,
        StrideGizmoRenderer? gizmoRenderer = null)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _assetLoader = assetLoader;
        _gizmoRenderer = gizmoRenderer;
    }

    #region Entity Lifecycle

    /// <inheritdoc/>
    public object CreateEntity(Guid nodeId, NodeType nodeType, SdkTransform worldTransform, AssetReference? asset)
    {
        var entity = new Entity(nodeId.ToString());

        // Apply transform
        var (pos, rot, scale) = worldTransform.ToStride();
        entity.Transform.Position = pos;
        entity.Transform.Rotation = rot;
        entity.Transform.Scale = scale;

        // Add type-specific components
        switch (nodeType)
        {
            case NodeType.Mesh:
                // Add placeholder model if no asset, or start loading
                if (asset.HasValue && asset.Value.IsValid)
                {
                    // Start async loading
                    _ = LoadAndSetAssetAsync(entity, asset.Value, CancellationToken.None);
                }
                else if (_placeholderModel != null)
                {
                    entity.Add(new ModelComponent { Model = _placeholderModel });
                }
                break;

            case NodeType.Marker:
                // Markers are debug visualization only
                // Could add a debug icon component here
                break;

            case NodeType.Volume:
                // Volumes could have collider visualization
                break;

            case NodeType.Emitter:
                // Emitters would have particle/audio components
                break;

            case NodeType.Reference:
                // Reference nodes load nested scenes
                break;

            case NodeType.Group:
            case NodeType.Custom:
            default:
                // Group nodes are just transforms
                break;
        }

        // Store reference and add to scene
        _entities[nodeId] = entity;
        _scene.Entities.Add(entity);

        return entity;
    }

    /// <inheritdoc/>
    public void DestroyEntity(Guid nodeId)
    {
        if (_entities.TryRemove(nodeId, out var entity))
        {
            _scene.Entities.Remove(entity);

            // Dispose any managed resources
            var modelComponent = entity.Get<ModelComponent>();
            if (modelComponent != null)
            {
                // Model cleanup if needed
            }
        }
    }

    /// <inheritdoc/>
    public void SetEntityParent(Guid nodeId, Guid? parentNodeId)
    {
        if (!_entities.TryGetValue(nodeId, out var entity))
            return;

        // Remove from current parent
        entity.Transform.Parent?.Entity.RemoveChild(entity);

        if (parentNodeId.HasValue && _entities.TryGetValue(parentNodeId.Value, out var parentEntity))
        {
            // Add to new parent
            parentEntity.AddChild(entity);
        }
    }

    #endregion

    #region Transform

    /// <inheritdoc/>
    public void UpdateEntityTransform(Guid nodeId, SdkTransform worldTransform)
    {
        if (!_entities.TryGetValue(nodeId, out var entity))
            return;

        var (pos, rot, scale) = worldTransform.ToStride();
        entity.Transform.Position = pos;
        entity.Transform.Rotation = rot;
        entity.Transform.Scale = scale;
    }

    #endregion

    #region Asset Binding

    /// <inheritdoc/>
    public async Task SetEntityAssetAsync(Guid nodeId, AssetReference asset, CancellationToken ct = default)
    {
        if (!_entities.TryGetValue(nodeId, out var entity))
            return;

        await LoadAndSetAssetAsync(entity, asset, ct);
    }

    /// <inheritdoc/>
    public void ClearEntityAsset(Guid nodeId)
    {
        if (!_entities.TryGetValue(nodeId, out var entity))
            return;

        var modelComponent = entity.Get<ModelComponent>();
        if (modelComponent != null)
        {
            if (_placeholderModel != null)
            {
                modelComponent.Model = _placeholderModel;
            }
            else
            {
                entity.Remove<ModelComponent>();
            }
        }
    }

    private async Task LoadAndSetAssetAsync(Entity entity, AssetReference asset, CancellationToken ct)
    {
        if (_assetLoader == null)
            return;

        try
        {
            var model = await _assetLoader.LoadModelAsync(asset.BundleId, asset.AssetId, asset.VariantId, ct);

            if (model != null)
            {
                var modelComponent = entity.Get<ModelComponent>();
                if (modelComponent != null)
                {
                    modelComponent.Model = model;
                }
                else
                {
                    entity.Add(new ModelComponent { Model = model });
                }
            }
        }
        catch (Exception)
        {
            // Log error and show placeholder
            if (_placeholderModel != null)
            {
                var modelComponent = entity.Get<ModelComponent>();
                if (modelComponent != null)
                {
                    modelComponent.Model = _placeholderModel;
                }
            }
        }
    }

    #endregion

    #region Selection Visualization

    /// <inheritdoc/>
    public void SetEntitySelected(Guid nodeId, bool selected)
    {
        if (!_entities.TryGetValue(nodeId, out var entity))
            return;

        // Apply selection visualization (e.g., outline, color tint)
        // This is a simplified implementation - real implementation would use outline rendering
        var modelComponent = entity.Get<ModelComponent>();
        if (modelComponent != null && _selectionMaterial != null)
        {
            // Store/restore original materials
            if (selected)
            {
                // Apply selection material overlay or effect
            }
            else
            {
                // Restore original materials
            }
        }
    }

    /// <inheritdoc/>
    public void SetEntityHovered(Guid nodeId, bool hovered)
    {
        if (!_entities.TryGetValue(nodeId, out var entity))
            return;

        // Apply hover visualization
        // Similar to selection but with different visual
    }

    /// <inheritdoc/>
    public void SetEntityVisible(Guid nodeId, bool visible)
    {
        if (!_entities.TryGetValue(nodeId, out var entity))
            return;

        // Toggle visibility of all render-related components
        var modelComponent = entity.Get<ModelComponent>();
        if (modelComponent != null)
        {
            modelComponent.Enabled = visible;
        }
    }

    #endregion

    #region Gizmo Rendering

    /// <inheritdoc/>
    public void RenderGizmo(
        SdkVector3 position,
        SdkQuaternion rotation,
        GizmoMode mode,
        GizmoAxis activeAxis,
        double scale)
    {
        _gizmoRenderer?.Render(
            position.ToStride(),
            rotation.ToStride(),
            mode,
            activeAxis,
            (float)scale);
    }

    /// <inheritdoc/>
    public void HideGizmo()
    {
        _gizmoRenderer?.Hide();
    }

    /// <inheritdoc/>
    public void RenderAttachmentPoints(IReadOnlyList<AttachmentPointInfo> points, int highlightedIndex = -1)
    {
        // Render attachment point indicators as debug spheres or icons
        for (int i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var color = i == highlightedIndex
                ? SdkColor.Highlight
                : (point.IsOccupied ? SdkColor.Green : SdkColor.Yellow);

            DrawDebugSphere(point.Position, 0.1, color);
        }
    }

    #endregion

    #region Picking

    /// <inheritdoc/>
    public Guid? PickEntity(SdkRay ray)
    {
        var strideRay = ray.ToStride();

        // Use Stride's physics simulation for raycasting
        var simulation = _scene.Entities
            .Select(e => e.Get<PhysicsComponent>())
            .FirstOrDefault(p => p != null)
            ?.Simulation;

        if (simulation != null)
        {
            var result = simulation.Raycast(strideRay.Position, strideRay.Position + strideRay.Direction * 1000f);
            if (result.Succeeded && result.Collider?.Entity != null)
            {
                var entityName = result.Collider.Entity.Name;
                if (Guid.TryParse(entityName, out var nodeId) && _entities.ContainsKey(nodeId))
                {
                    return nodeId;
                }
            }
        }

        // Fallback: bounding box intersection test
        return PickEntityByBoundingBox(strideRay);
    }

    /// <inheritdoc/>
    public IReadOnlyList<PickResult> PickAllEntities(SdkRay ray)
    {
        var strideRay = ray.ToStride();
        var results = new List<PickResult>();

        foreach (var (nodeId, entity) in _entities)
        {
            var modelComponent = entity.Get<ModelComponent>();
            if (modelComponent?.Model?.BoundingBox != null)
            {
                var worldMatrix = entity.Transform.WorldMatrix;
                var bounds = modelComponent.Model.BoundingBox;

                // Transform bounding box to world space (simplified - uses AABB)
                var center = StrideVector3.TransformCoordinate(bounds.Center, worldMatrix);
                var extent = bounds.Extent * worldMatrix.ScaleVector;

                var worldBounds = new BoundingBox(center - extent, center + extent);

                if (worldBounds.Intersects(ref strideRay, out StrideVector3 intersection))
                {
                    var distance = StrideVector3.Distance(strideRay.Position, intersection);
                    results.Add(new PickResult(
                        nodeId,
                        distance,
                        intersection.ToSdk(),
                        StrideVector3.UnitY.ToSdk())); // Simplified normal
                }
            }
        }

        return results.OrderBy(r => r.Distance).ToList();
    }

    /// <inheritdoc/>
    public IReadOnlyList<Guid> PickEntitiesInRect(SdkVector2 screenMin, SdkVector2 screenMax)
    {
        var results = new List<Guid>();

        foreach (var (nodeId, entity) in _entities)
        {
            // Project entity position to screen space
            var worldPos = entity.Transform.WorldMatrix.TranslationVector;
            var screenPos = ProjectToScreen(worldPos);

            if (screenPos.X >= screenMin.X && screenPos.X <= screenMax.X &&
                screenPos.Y >= screenMin.Y && screenPos.Y <= screenMax.Y)
            {
                results.Add(nodeId);
            }
        }

        return results;
    }

    /// <inheritdoc/>
    public GizmoAxis PickGizmoAxis(
        SdkRay ray,
        SdkVector3 gizmoPosition,
        SdkQuaternion gizmoRotation,
        GizmoMode mode,
        double gizmoScale)
    {
        return _gizmoRenderer?.PickAxis(
            ray.ToStride(),
            gizmoPosition.ToStride(),
            gizmoRotation.ToStride(),
            mode,
            (float)gizmoScale) ?? GizmoAxis.None;
    }

    private Guid? PickEntityByBoundingBox(StrideRay ray)
    {
        float closestDistance = float.MaxValue;
        Guid? closestEntity = null;

        foreach (var (nodeId, entity) in _entities)
        {
            var modelComponent = entity.Get<ModelComponent>();
            if (modelComponent?.Model?.BoundingBox != null)
            {
                var worldMatrix = entity.Transform.WorldMatrix;
                var bounds = modelComponent.Model.BoundingBox;

                // Transform bounding box to world space
                var center = StrideVector3.TransformCoordinate(bounds.Center, worldMatrix);
                var extent = bounds.Extent * worldMatrix.ScaleVector;
                var worldBounds = new BoundingBox(center - extent, center + extent);

                if (worldBounds.Intersects(ref ray, out StrideVector3 intersection))
                {
                    var distance = StrideVector3.Distance(ray.Position, intersection);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestEntity = nodeId;
                    }
                }
            }
        }

        return closestEntity;
    }

    #endregion

    #region Camera

    /// <inheritdoc/>
    public void FocusCamera(SdkVector3 target, double distance)
    {
        var cameraEntity = _camera.Entity;
        if (cameraEntity == null)
            return;

        // Calculate new camera position to look at target from distance
        var currentForward = cameraEntity.Transform.WorldMatrix.Forward;
        var newPosition = target.ToStride() - currentForward * (float)distance;

        cameraEntity.Transform.Position = newPosition;
    }

    /// <inheritdoc/>
    public SdkRay GetMouseRay(SdkVector2 screenPosition)
    {
        // Convert screen position to normalized coordinates
        var viewport = _graphicsDevice.Presenter?.BackBuffer?.ViewWidth ?? 1920;
        var height = _graphicsDevice.Presenter?.BackBuffer?.ViewHeight ?? 1080;

        var normalizedX = (float)(screenPosition.X / viewport);
        var normalizedY = (float)(screenPosition.Y / height);

        // Get near and far points on the ray
        var nearPoint = new StrideVector3(normalizedX * 2 - 1, 1 - normalizedY * 2, 0);
        var farPoint = new StrideVector3(normalizedX * 2 - 1, 1 - normalizedY * 2, 1);

        // Transform through inverse view-projection matrix
        var viewProj = _camera.ViewProjectionMatrix;
        Matrix.Invert(ref viewProj, out var invViewProj);

        var nearWorld = StrideVector3.TransformCoordinate(nearPoint, invViewProj);
        var farWorld = StrideVector3.TransformCoordinate(farPoint, invViewProj);

        var direction = StrideVector3.Normalize(farWorld - nearWorld);
        return new SdkRay(nearWorld.ToSdk(), direction.ToSdk());
    }

    /// <inheritdoc/>
    public SdkVector3 GetCameraPosition()
    {
        return _camera.Entity?.Transform.WorldMatrix.TranslationVector.ToSdk()
            ?? SdkVector3.Zero;
    }

    /// <inheritdoc/>
    public SdkVector3 GetCameraForward()
    {
        return _camera.Entity?.Transform.WorldMatrix.Forward.ToSdk()
            ?? new SdkVector3(0, 0, 1);
    }

    private global::Stride.Core.Mathematics.Vector2 ProjectToScreen(StrideVector3 worldPos)
    {
        var viewProj = _camera.ViewProjectionMatrix;
        var clipSpace = Vector4.Transform(new Vector4(worldPos, 1), viewProj);

        if (clipSpace.W <= 0)
            return new global::Stride.Core.Mathematics.Vector2(-1, -1);

        var ndcX = clipSpace.X / clipSpace.W;
        var ndcY = clipSpace.Y / clipSpace.W;

        var viewport = _graphicsDevice.Presenter?.BackBuffer?.ViewWidth ?? 1920;
        var height = _graphicsDevice.Presenter?.BackBuffer?.ViewHeight ?? 1080;

        return new global::Stride.Core.Mathematics.Vector2(
            (ndcX + 1) * 0.5f * viewport,
            (1 - ndcY) * 0.5f * height);
    }

    #endregion

    #region Thumbnails

    /// <inheritdoc/>
    public async Task<byte[]?> GetAssetThumbnailAsync(
        AssetReference asset,
        int width,
        int height,
        CancellationToken ct = default)
    {
        // Thumbnail generation would require off-screen rendering
        // This is a placeholder - actual implementation would render to texture
        if (_assetLoader != null)
        {
            return await _assetLoader.GetThumbnailAsync(asset.BundleId, asset.AssetId, width, height, ct);
        }

        return null;
    }

    #endregion

    #region Debug Visualization

    /// <inheritdoc/>
    public void DrawDebugLine(SdkVector3 start, SdkVector3 end, SdkColor color, float duration = 0f)
    {
        // Use Stride debug rendering
        // Note: Stride's DebugRenderFeature requires setup in the graphics compositor
        // This is a simplified version
    }

    /// <inheritdoc/>
    public void DrawDebugSphere(SdkVector3 center, double radius, SdkColor color, float duration = 0f)
    {
        // Sphere debug rendering
    }

    /// <inheritdoc/>
    public void DrawDebugBox(
        SdkVector3 center,
        SdkVector3 size,
        SdkQuaternion rotation,
        SdkColor color,
        float duration = 0f)
    {
        // Box debug rendering
    }

    #endregion

    #region Configuration

    /// <summary>
    /// Set the placeholder model shown for entities without loaded assets.
    /// </summary>
    public void SetPlaceholderModel(Model? model)
    {
        _placeholderModel = model;
    }

    /// <summary>
    /// Set the selection highlight material.
    /// </summary>
    public void SetSelectionMaterial(Material? material)
    {
        _selectionMaterial = material;
    }

    /// <summary>
    /// Set the hover highlight material.
    /// </summary>
    public void SetHoverMaterial(Material? material)
    {
        _hoverMaterial = material;
    }

    /// <summary>
    /// Get the Stride entity for a node ID.
    /// </summary>
    public Entity? GetEntity(Guid nodeId)
    {
        return _entities.TryGetValue(nodeId, out var entity) ? entity : null;
    }

    /// <summary>
    /// Get all managed entities.
    /// </summary>
    public IReadOnlyDictionary<Guid, Entity> Entities => _entities;

    #endregion
}
