using BeyondImmersion.Bannou.SceneComposer.Gizmo;
using Godot;
using SdkQuat = BeyondImmersion.Bannou.SceneComposer.Math.Quaternion;
using SdkVec3 = BeyondImmersion.Bannou.SceneComposer.Math.Vector3;

namespace BeyondImmersion.Bannou.SceneComposer.Godot.Gizmo;

/// <summary>
/// Renders transform gizmos for the Godot engine.
/// Creates and manages procedural meshes for translate, rotate, and scale handles.
/// </summary>
public class GodotGizmoRenderer
{
    private readonly Node3D _parent;
    private Node3D? _gizmoRoot;

    // Axis meshes
    private MeshInstance3D? _xAxisMesh;
    private MeshInstance3D? _yAxisMesh;
    private MeshInstance3D? _zAxisMesh;

    // Materials
    private StandardMaterial3D? _xMaterial;
    private StandardMaterial3D? _yMaterial;
    private StandardMaterial3D? _zMaterial;
    private StandardMaterial3D? _highlightMaterial;

    // Current state
    private GizmoMode _currentMode = GizmoMode.None;
    private GizmoAxis _highlightedAxis = GizmoAxis.None;

    // Colors
    private static readonly Color XAxisColor = new Color(1, 0.3f, 0.3f);
    private static readonly Color YAxisColor = new Color(0.3f, 1, 0.3f);
    private static readonly Color ZAxisColor = new Color(0.3f, 0.3f, 1);
    private static readonly Color HighlightColor = new Color(1, 1, 0.4f);

    /// <summary>
    /// Whether the gizmo is currently visible.
    /// </summary>
    public bool IsVisible => _gizmoRoot?.Visible ?? false;

    /// <summary>
    /// Create a new gizmo renderer.
    /// </summary>
    /// <param name="parent">Parent node to attach gizmo to.</param>
    public GodotGizmoRenderer(Node3D parent)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        Initialize();
    }

    /// <summary>
    /// Initialize gizmo meshes and materials.
    /// </summary>
    private void Initialize()
    {
        _gizmoRoot = new Node3D { Name = "GizmoRoot" };
        _parent.AddChild(_gizmoRoot);
        _gizmoRoot.Visible = false;

        // Create materials
        _xMaterial = CreateAxisMaterial(XAxisColor);
        _yMaterial = CreateAxisMaterial(YAxisColor);
        _zMaterial = CreateAxisMaterial(ZAxisColor);
        _highlightMaterial = CreateAxisMaterial(HighlightColor);

        // Create axis mesh instances
        _xAxisMesh = CreateAxisMeshInstance("X_Axis", _xMaterial);
        _yAxisMesh = CreateAxisMeshInstance("Y_Axis", _yMaterial);
        _zAxisMesh = CreateAxisMeshInstance("Z_Axis", _zMaterial);

        _gizmoRoot.AddChild(_xAxisMesh);
        _gizmoRoot.AddChild(_yAxisMesh);
        _gizmoRoot.AddChild(_zAxisMesh);
    }

    /// <summary>
    /// Create a material for gizmo axis rendering.
    /// </summary>
    private static StandardMaterial3D CreateAxisMaterial(Color color)
    {
        return new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = color,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = true, // Always visible
            Transparency = BaseMaterial3D.TransparencyEnum.Disabled
        };
    }

    /// <summary>
    /// Create a mesh instance for an axis.
    /// </summary>
    private static MeshInstance3D CreateAxisMeshInstance(string name, StandardMaterial3D material)
    {
        var instance = new MeshInstance3D
        {
            Name = name,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = material
        };
        return instance;
    }

    /// <summary>
    /// Render the gizmo at the specified position with the specified mode.
    /// </summary>
    /// <param name="position">World position for the gizmo center.</param>
    /// <param name="rotation">World rotation for the gizmo orientation.</param>
    /// <param name="mode">Gizmo mode (Translate, Rotate, Scale).</param>
    /// <param name="activeAxis">Currently active/highlighted axis.</param>
    /// <param name="scale">Visual scale of the gizmo.</param>
    public void Render(SdkVec3 position, SdkQuat rotation, GizmoMode mode, GizmoAxis activeAxis, double scale)
    {
        if (_gizmoRoot == null || _xAxisMesh == null || _yAxisMesh == null || _zAxisMesh == null)
            return;

        // Update mode if changed
        if (mode != _currentMode)
        {
            _currentMode = mode;
            UpdateMeshesForMode(mode, (float)scale);
        }

        // Update highlight
        UpdateHighlight(activeAxis);

        // Position and orient gizmo
        _gizmoRoot.GlobalPosition = position.ToGodot();
        _gizmoRoot.GlobalRotation = rotation.ToGodot().GetEuler();

        // Scale to maintain screen-space size
        _gizmoRoot.Scale = Vector3.One * (float)scale;

        _gizmoRoot.Visible = mode != GizmoMode.None;
    }

    /// <summary>
    /// Hide the gizmo.
    /// </summary>
    public void Hide()
    {
        if (_gizmoRoot != null)
        {
            _gizmoRoot.Visible = false;
        }
        _currentMode = GizmoMode.None;
    }

    /// <summary>
    /// Update meshes based on gizmo mode.
    /// </summary>
    private void UpdateMeshesForMode(GizmoMode mode, float scale)
    {
        if (_xAxisMesh == null || _yAxisMesh == null || _zAxisMesh == null)
            return;

        ArrayMesh? xMesh = null;
        ArrayMesh? yMesh = null;
        ArrayMesh? zMesh = null;

        switch (mode)
        {
            case GizmoMode.Translate:
                xMesh = CreateArrowMesh(GizmoAxisDirection.X);
                yMesh = CreateArrowMesh(GizmoAxisDirection.Y);
                zMesh = CreateArrowMesh(GizmoAxisDirection.Z);
                break;

            case GizmoMode.Rotate:
                xMesh = CreateRingMesh(GizmoAxisDirection.X);
                yMesh = CreateRingMesh(GizmoAxisDirection.Y);
                zMesh = CreateRingMesh(GizmoAxisDirection.Z);
                break;

            case GizmoMode.Scale:
                xMesh = CreateScaleMesh(GizmoAxisDirection.X);
                yMesh = CreateScaleMesh(GizmoAxisDirection.Y);
                zMesh = CreateScaleMesh(GizmoAxisDirection.Z);
                break;
        }

        _xAxisMesh.Mesh = xMesh;
        _yAxisMesh.Mesh = yMesh;
        _zAxisMesh.Mesh = zMesh;
    }

    /// <summary>
    /// Update which axis is highlighted.
    /// </summary>
    private void UpdateHighlight(GizmoAxis axis)
    {
        if (_highlightedAxis == axis)
            return;

        _highlightedAxis = axis;

        // Reset all materials
        if (_xAxisMesh != null) _xAxisMesh.MaterialOverride = _xMaterial;
        if (_yAxisMesh != null) _yAxisMesh.MaterialOverride = _yMaterial;
        if (_zAxisMesh != null) _zAxisMesh.MaterialOverride = _zMaterial;

        // Apply highlight to active axis
        switch (axis)
        {
            case GizmoAxis.X:
                if (_xAxisMesh != null) _xAxisMesh.MaterialOverride = _highlightMaterial;
                break;
            case GizmoAxis.Y:
                if (_yAxisMesh != null) _yAxisMesh.MaterialOverride = _highlightMaterial;
                break;
            case GizmoAxis.Z:
                if (_zAxisMesh != null) _zAxisMesh.MaterialOverride = _highlightMaterial;
                break;
        }
    }

    /// <summary>
    /// Create an arrow mesh for translate mode.
    /// </summary>
    private static ArrayMesh CreateArrowMesh(GizmoAxisDirection axis)
    {
        var vertices = GizmoGeometry.GenerateArrowVertices();
        vertices = GizmoGeometry.TransformToAxis(vertices, axis);
        return CreateMeshFromVertices(vertices);
    }

    /// <summary>
    /// Create a ring mesh for rotate mode.
    /// </summary>
    private static ArrayMesh CreateRingMesh(GizmoAxisDirection axis)
    {
        var vertices = GizmoGeometry.GenerateRingVertices();
        vertices = GizmoGeometry.TransformToAxis(vertices, axis);
        return CreateMeshFromVertices(vertices);
    }

    /// <summary>
    /// Create a scale cube mesh.
    /// </summary>
    private static ArrayMesh CreateScaleMesh(GizmoAxisDirection axis)
    {
        // Line from origin + cube at end
        var arrowVertices = GizmoGeometry.GenerateArrowVertices(
            shaftLength: 0.85f,
            shaftRadius: 0.015f,
            headLength: 0.01f, // Minimal head
            headRadius: 0.015f,
            segments: 8);

        var cubeVertices = GizmoGeometry.GenerateScaleCubeVertices(size: 0.08f, offsetZ: 0.92f);

        // Combine
        var combined = new Vector3[arrowVertices.Length + cubeVertices.Length];
        Array.Copy(arrowVertices, combined, arrowVertices.Length);
        Array.Copy(cubeVertices, 0, combined, arrowVertices.Length, cubeVertices.Length);

        combined = GizmoGeometry.TransformToAxis(combined, axis);
        return CreateMeshFromVertices(combined);
    }

    /// <summary>
    /// Create an ArrayMesh from vertex data.
    /// </summary>
    private static ArrayMesh CreateMeshFromVertices(Vector3[] vertices)
    {
        var mesh = new ArrayMesh();
        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);

        arrays[(int)Mesh.ArrayType.Vertex] = vertices;

        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>
    /// Pick which axis is under the ray at the current gizmo position.
    /// </summary>
    /// <param name="rayOrigin">World ray origin.</param>
    /// <param name="rayDirection">World ray direction (normalized).</param>
    /// <param name="gizmoPosition">Current gizmo world position.</param>
    /// <param name="gizmoRotation">Current gizmo world rotation.</param>
    /// <param name="scale">Current gizmo scale.</param>
    /// <returns>The picked axis, or None if no axis hit.</returns>
    public GizmoAxis PickAxis(
        SdkVec3 rayOrigin,
        SdkVec3 rayDirection,
        SdkVec3 gizmoPosition,
        SdkQuat gizmoRotation,
        double scale)
    {
        // Simple axis picking based on ray-cylinder distance
        var origin = rayOrigin.ToGodot();
        var direction = rayDirection.ToGodot().Normalized();
        var center = gizmoPosition.ToGodot();

        var axisLength = (float)(scale * 1.0);
        var pickRadius = (float)(scale * 0.1);

        // Transform ray to gizmo local space
        var localOrigin = origin - center;
        // For simplicity, assume identity rotation for now

        // Check each axis
        var distX = DistanceToAxis(localOrigin, direction, Vector3.Right, axisLength);
        var distY = DistanceToAxis(localOrigin, direction, Vector3.Up, axisLength);
        var distZ = DistanceToAxis(localOrigin, direction, Vector3.Back, axisLength);

        var minDist = pickRadius;
        var result = GizmoAxis.None;

        if (distX < minDist)
        {
            minDist = distX;
            result = GizmoAxis.X;
        }
        if (distY < minDist)
        {
            minDist = distY;
            result = GizmoAxis.Y;
        }
        if (distZ < minDist)
        {
            result = GizmoAxis.Z;
        }

        return result;
    }

    /// <summary>
    /// Calculate the minimum distance from a ray to an axis line segment.
    /// </summary>
    private static float DistanceToAxis(Vector3 rayOrigin, Vector3 rayDirection, Vector3 axisDirection, float axisLength)
    {
        // Simplified: distance from ray to line segment from origin along axisDirection
        var axisEnd = axisDirection * axisLength;

        // Cross product gives perpendicular distance for skew lines
        var cross = rayDirection.Cross(axisDirection);
        var crossLength = cross.Length();

        if (crossLength < float.Epsilon)
        {
            // Parallel - use point-to-line distance from ray origin to axis
            return rayOrigin.Cross(axisDirection).Length();
        }

        // Distance between skew lines
        var diff = rayOrigin; // axis starts at origin
        return Mathf.Abs(diff.Dot(cross)) / crossLength;
    }

    /// <summary>
    /// Dispose of gizmo resources.
    /// </summary>
    public void Dispose()
    {
        _gizmoRoot?.QueueFree();
        _xMaterial?.Dispose();
        _yMaterial?.Dispose();
        _zMaterial?.Dispose();
        _highlightMaterial?.Dispose();
    }
}
