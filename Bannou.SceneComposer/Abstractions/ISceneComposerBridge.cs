using BeyondImmersion.Bannou.SceneComposer.Gizmo;
using BeyondImmersion.Bannou.SceneComposer.Math;

namespace BeyondImmersion.Bannou.SceneComposer.Abstractions;

/// <summary>
/// Bridge interface between the engine-agnostic SceneComposer and engine-specific rendering/interaction.
/// Each game engine (Stride, Unity, Godot) provides its own implementation.
/// </summary>
public interface ISceneComposerBridge
{
    #region Entity Lifecycle

    /// <summary>
    /// Create an engine entity for a scene node.
    /// </summary>
    /// <param name="nodeId">Unique identifier for the node.</param>
    /// <param name="nodeType">Type of node being created.</param>
    /// <param name="worldTransform">Initial world-space transform.</param>
    /// <param name="asset">Optional initial asset reference.</param>
    /// <returns>Engine-specific entity handle (for debugging/inspection).</returns>
    object CreateEntity(Guid nodeId, NodeType nodeType, Transform worldTransform, AssetReference? asset);

    /// <summary>
    /// Destroy an engine entity.
    /// </summary>
    /// <param name="nodeId">Node identifier.</param>
    void DestroyEntity(Guid nodeId);

    /// <summary>
    /// Set the parent of an entity (for hierarchy visualization in engine).
    /// </summary>
    /// <param name="nodeId">Node identifier.</param>
    /// <param name="parentNodeId">New parent node identifier, or null for root.</param>
    void SetEntityParent(Guid nodeId, Guid? parentNodeId);

    #endregion

    #region Transform

    /// <summary>
    /// Update an entity's world transform.
    /// </summary>
    /// <param name="nodeId">Node identifier.</param>
    /// <param name="worldTransform">New world-space transform.</param>
    void UpdateEntityTransform(Guid nodeId, Transform worldTransform);

    #endregion

    #region Asset Binding

    /// <summary>
    /// Set the visual asset for an entity.
    /// </summary>
    /// <param name="nodeId">Node identifier.</param>
    /// <param name="asset">Asset reference to load and display.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetEntityAssetAsync(Guid nodeId, AssetReference asset, CancellationToken ct = default);

    /// <summary>
    /// Clear the visual asset from an entity (show placeholder or nothing).
    /// </summary>
    /// <param name="nodeId">Node identifier.</param>
    void ClearEntityAsset(Guid nodeId);

    #endregion

    #region Selection Visualization

    /// <summary>
    /// Set the selection state visualization for an entity.
    /// </summary>
    /// <param name="nodeId">Node identifier.</param>
    /// <param name="selected">Whether the entity is selected.</param>
    void SetEntitySelected(Guid nodeId, bool selected);

    /// <summary>
    /// Set the hover state visualization for an entity.
    /// </summary>
    /// <param name="nodeId">Node identifier.</param>
    /// <param name="hovered">Whether the entity is hovered.</param>
    void SetEntityHovered(Guid nodeId, bool hovered);

    /// <summary>
    /// Set the visibility of an entity.
    /// </summary>
    /// <param name="nodeId">Node identifier.</param>
    /// <param name="visible">Whether the entity should be visible.</param>
    void SetEntityVisible(Guid nodeId, bool visible);

    #endregion

    #region Gizmo Rendering

    /// <summary>
    /// Render the transform gizmo at a position.
    /// </summary>
    /// <param name="position">World position for the gizmo.</param>
    /// <param name="rotation">Orientation of the gizmo (for local-space modes).</param>
    /// <param name="mode">Current gizmo mode.</param>
    /// <param name="activeAxis">Currently active/hovered axis.</param>
    /// <param name="scale">Scale factor for consistent screen-space size.</param>
    void RenderGizmo(Vector3 position, Quaternion rotation, GizmoMode mode, GizmoAxis activeAxis, double scale);

    /// <summary>
    /// Hide the gizmo (no selection or gizmo disabled).
    /// </summary>
    void HideGizmo();

    /// <summary>
    /// Render attachment point indicators.
    /// </summary>
    /// <param name="points">Attachment points to visualize.</param>
    /// <param name="highlightedIndex">Index of highlighted point, or -1.</param>
    void RenderAttachmentPoints(IReadOnlyList<AttachmentPointInfo> points, int highlightedIndex = -1);

    #endregion

    #region Picking

    /// <summary>
    /// Pick an entity using a ray.
    /// </summary>
    /// <param name="ray">Ray in world space.</param>
    /// <returns>Node ID of hit entity, or null if no hit.</returns>
    Guid? PickEntity(Ray ray);

    /// <summary>
    /// Pick all entities intersecting a ray.
    /// </summary>
    /// <param name="ray">Ray in world space.</param>
    /// <returns>Hits ordered by distance.</returns>
    IReadOnlyList<PickResult> PickAllEntities(Ray ray);

    /// <summary>
    /// Pick entities within a screen rectangle (box selection).
    /// </summary>
    /// <param name="screenMin">Top-left screen position.</param>
    /// <param name="screenMax">Bottom-right screen position.</param>
    /// <returns>Node IDs of entities within the rectangle.</returns>
    IReadOnlyList<Guid> PickEntitiesInRect(Vector2 screenMin, Vector2 screenMax);

    /// <summary>
    /// Test which gizmo axis a ray intersects.
    /// </summary>
    /// <param name="ray">Ray in world space.</param>
    /// <param name="gizmoPosition">Position of the gizmo.</param>
    /// <param name="gizmoRotation">Orientation of the gizmo.</param>
    /// <param name="mode">Current gizmo mode.</param>
    /// <param name="gizmoScale">Scale of the gizmo.</param>
    /// <returns>Intersected axis, or None.</returns>
    GizmoAxis PickGizmoAxis(Ray ray, Vector3 gizmoPosition, Quaternion gizmoRotation, GizmoMode mode, double gizmoScale);

    #endregion

    #region Camera

    /// <summary>
    /// Focus the camera on a target point.
    /// </summary>
    /// <param name="target">World position to focus on.</param>
    /// <param name="distance">Distance from target.</param>
    void FocusCamera(Vector3 target, double distance);

    /// <summary>
    /// Get a ray from screen coordinates.
    /// </summary>
    /// <param name="screenPosition">Screen position (0,0 = top-left).</param>
    /// <returns>Ray in world space.</returns>
    Ray GetMouseRay(Vector2 screenPosition);

    /// <summary>
    /// Get the current camera position.
    /// </summary>
    Vector3 GetCameraPosition();

    /// <summary>
    /// Get the current camera forward direction.
    /// </summary>
    Vector3 GetCameraForward();

    #endregion

    #region Thumbnails

    /// <summary>
    /// Get a thumbnail image for an asset.
    /// </summary>
    /// <param name="asset">Asset reference.</param>
    /// <param name="width">Desired thumbnail width.</param>
    /// <param name="height">Desired thumbnail height.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Thumbnail image data (PNG format), or null if unavailable.</returns>
    Task<byte[]?> GetAssetThumbnailAsync(AssetReference asset, int width, int height, CancellationToken ct = default);

    #endregion

    #region Debug Visualization

    /// <summary>
    /// Draw a debug line (for one frame).
    /// </summary>
    void DrawDebugLine(Vector3 start, Vector3 end, Color color, float duration = 0f);

    /// <summary>
    /// Draw a debug sphere (for one frame).
    /// </summary>
    void DrawDebugSphere(Vector3 center, double radius, Color color, float duration = 0f);

    /// <summary>
    /// Draw a debug box (for one frame).
    /// </summary>
    void DrawDebugBox(Vector3 center, Vector3 size, Quaternion rotation, Color color, float duration = 0f);

    #endregion
}

/// <summary>
/// Result of a pick operation.
/// </summary>
public readonly struct PickResult
{
    /// <summary>
    /// Node ID of the hit entity.
    /// </summary>
    public Guid NodeId { get; }

    /// <summary>
    /// Distance from ray origin to hit point.
    /// </summary>
    public double Distance { get; }

    /// <summary>
    /// World position of the hit.
    /// </summary>
    public Vector3 HitPoint { get; }

    /// <summary>
    /// Surface normal at hit point.
    /// </summary>
    public Vector3 Normal { get; }

    /// <summary>
    /// Create a pick result.
    /// </summary>
    public PickResult(Guid nodeId, double distance, Vector3 hitPoint, Vector3 normal)
    {
        NodeId = nodeId;
        Distance = distance;
        HitPoint = hitPoint;
        Normal = normal;
    }
}

/// <summary>
/// Information about an attachment point for visualization.
/// </summary>
public readonly struct AttachmentPointInfo
{
    /// <summary>
    /// World position of the attachment point.
    /// </summary>
    public Vector3 Position { get; }

    /// <summary>
    /// World rotation of the attachment point.
    /// </summary>
    public Quaternion Rotation { get; }

    /// <summary>
    /// Name of the attachment point.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Whether this attachment point is occupied.
    /// </summary>
    public bool IsOccupied { get; }

    /// <summary>
    /// Create attachment point info.
    /// </summary>
    public AttachmentPointInfo(Vector3 position, Quaternion rotation, string name, bool isOccupied)
    {
        Position = position;
        Rotation = rotation;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        IsOccupied = isOccupied;
    }
}

/// <summary>
/// 2D vector for screen coordinates.
/// </summary>
public readonly struct Vector2
{
    public double X { get; }
    public double Y { get; }

    public Vector2(double x, double y)
    {
        X = x;
        Y = y;
    }

    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2 operator *(Vector2 v, double scalar) => new(v.X * scalar, v.Y * scalar);

    public override string ToString() => $"({X:F2}, {Y:F2})";
}
