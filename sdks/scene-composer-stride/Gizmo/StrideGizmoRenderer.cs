using BeyondImmersion.Bannou.SceneComposer.Gizmo;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using StrideVector3 = Stride.Core.Mathematics.Vector3;
using StrideQuaternion = Stride.Core.Mathematics.Quaternion;
using StrideRay = Stride.Core.Mathematics.Ray;

namespace BeyondImmersion.Bannou.SceneComposer.Stride;

/// <summary>
/// Renders transform gizmos for the Stride scene composer.
/// Provides visual handles for translation, rotation, and scale operations.
/// </summary>
public class StrideGizmoRenderer
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Entity? _gizmoEntity;

    private bool _isVisible;
    private StrideVector3 _position;
    private StrideQuaternion _rotation;
    private GizmoMode _mode;
    private GizmoAxis _activeAxis;
    private float _scale = 1.0f;

    // Gizmo colors (RGB = XYZ convention)
    private static readonly Color XAxisColor = new(255, 80, 80, 255);
    private static readonly Color YAxisColor = new(80, 255, 80, 255);
    private static readonly Color ZAxisColor = new(80, 80, 255, 255);
    private static readonly Color HighlightColor = new(255, 255, 100, 255);
    private static readonly Color PlaneColor = new(255, 255, 255, 80);

    /// <summary>
    /// Create a gizmo renderer.
    /// </summary>
    /// <param name="graphicsDevice">Graphics device for rendering.</param>
    /// <param name="gizmoEntity">Optional entity to attach gizmo visuals to.</param>
    public StrideGizmoRenderer(GraphicsDevice graphicsDevice, Entity? gizmoEntity = null)
    {
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _gizmoEntity = gizmoEntity;
        _rotation = StrideQuaternion.Identity;
    }

    /// <summary>
    /// Whether the gizmo is currently visible.
    /// </summary>
    public bool IsVisible => _isVisible;

    /// <summary>
    /// Current gizmo mode.
    /// </summary>
    public GizmoMode Mode => _mode;

    /// <summary>
    /// Currently active (hovered/dragging) axis.
    /// </summary>
    public GizmoAxis ActiveAxis => _activeAxis;

    /// <summary>
    /// Render the gizmo at the specified position.
    /// </summary>
    public void Render(
        StrideVector3 position,
        StrideQuaternion rotation,
        GizmoMode mode,
        GizmoAxis activeAxis,
        float scale)
    {
        _isVisible = mode != GizmoMode.None;
        _position = position;
        _rotation = rotation;
        _mode = mode;
        _activeAxis = activeAxis;
        _scale = scale;

        if (!_isVisible)
            return;

        // Update gizmo entity transform if present
        if (_gizmoEntity != null)
        {
            _gizmoEntity.Transform.Position = position;
            _gizmoEntity.Transform.Rotation = rotation;
            _gizmoEntity.Transform.Scale = new StrideVector3(scale);
        }

        // Actual rendering would be done through immediate mode debug drawing
        // or by updating procedural mesh components
        switch (mode)
        {
            case GizmoMode.Translate:
                RenderTranslateGizmo(position, rotation, activeAxis, scale);
                break;
            case GizmoMode.Rotate:
                RenderRotateGizmo(position, rotation, activeAxis, scale);
                break;
            case GizmoMode.Scale:
                RenderScaleGizmo(position, rotation, activeAxis, scale);
                break;
            case GizmoMode.Universal:
                RenderUniversalGizmo(position, rotation, activeAxis, scale);
                break;
        }
    }

    /// <summary>
    /// Hide the gizmo.
    /// </summary>
    public void Hide()
    {
        _isVisible = false;

        if (_gizmoEntity != null)
        {
            // Disable gizmo entity visibility
        }
    }

    /// <summary>
    /// Pick which gizmo axis is hit by a ray.
    /// </summary>
    public GizmoAxis PickAxis(
        StrideRay ray,
        StrideVector3 gizmoPosition,
        StrideQuaternion gizmoRotation,
        GizmoMode mode,
        float gizmoScale)
    {
        if (mode == GizmoMode.None)
            return GizmoAxis.None;

        // Transform ray to gizmo local space
        var invRotation = StrideQuaternion.Invert(gizmoRotation);
        Matrix.RotationQuaternion(ref invRotation, out var invRotMatrix);
        var localOrigin = ray.Position - gizmoPosition;
        StrideVector3.TransformCoordinate(ref localOrigin, ref invRotMatrix, out localOrigin);
        StrideVector3.TransformNormal(ref ray.Direction, ref invRotMatrix, out var localDirection);
        var localRay = new StrideRay(localOrigin, localDirection);

        float pickRadius = 0.1f * gizmoScale;
        float handleLength = 1.0f * gizmoScale;

        // Test each axis
        GizmoAxis closest = GizmoAxis.None;
        float closestDistance = float.MaxValue;

        // X axis
        if (TestAxisHit(localRay, StrideVector3.UnitX, handleLength, pickRadius, out float distX))
        {
            if (distX < closestDistance)
            {
                closestDistance = distX;
                closest = GizmoAxis.X;
            }
        }

        // Y axis
        if (TestAxisHit(localRay, StrideVector3.UnitY, handleLength, pickRadius, out float distY))
        {
            if (distY < closestDistance)
            {
                closestDistance = distY;
                closest = GizmoAxis.Y;
            }
        }

        // Z axis
        if (TestAxisHit(localRay, StrideVector3.UnitZ, handleLength, pickRadius, out float distZ))
        {
            if (distZ < closestDistance)
            {
                closestDistance = distZ;
                closest = GizmoAxis.Z;
            }
        }

        // Test plane picks for translate mode
        if (mode == GizmoMode.Translate)
        {
            float planeSize = 0.3f * gizmoScale;

            if (TestPlaneHit(localRay, StrideVector3.UnitZ, planeSize, out float distXY) && distXY < closestDistance)
            {
                closestDistance = distXY;
                closest = GizmoAxis.XY;
            }

            if (TestPlaneHit(localRay, StrideVector3.UnitY, planeSize, out float distXZ) && distXZ < closestDistance)
            {
                closestDistance = distXZ;
                closest = GizmoAxis.XZ;
            }

            if (TestPlaneHit(localRay, StrideVector3.UnitX, planeSize, out float distYZ) && distYZ < closestDistance)
            {
                closestDistance = distYZ;
                closest = GizmoAxis.YZ;
            }
        }

        return closest;
    }

    private bool TestAxisHit(StrideRay ray, StrideVector3 axis, float length, float radius, out float distance)
    {
        distance = float.MaxValue;

        // Find closest point on ray to axis line
        var rayToAxis = StrideVector3.Zero - ray.Position;
        var t = Vector3.Dot(rayToAxis, ray.Direction);

        if (t < 0)
            return false;

        var closestOnRay = ray.Position + ray.Direction * t;
        var projOnAxis = Vector3.Dot(closestOnRay, axis);

        if (projOnAxis < 0 || projOnAxis > length)
            return false;

        var closestOnAxis = axis * projOnAxis;
        var dist = Vector3.Distance(closestOnRay, closestOnAxis);

        if (dist > radius)
            return false;

        distance = t;
        return true;
    }

    private bool TestPlaneHit(StrideRay ray, StrideVector3 planeNormal, float size, out float distance)
    {
        distance = float.MaxValue;

        var denom = Vector3.Dot(planeNormal, ray.Direction);
        if (Math.Abs(denom) < 0.0001f)
            return false;

        var t = -Vector3.Dot(planeNormal, ray.Position) / denom;
        if (t < 0)
            return false;

        var hitPoint = ray.Position + ray.Direction * t;

        // Check if within plane quad
        var u = GetPlaneAxis(planeNormal, 0);
        var v = GetPlaneAxis(planeNormal, 1);

        var uCoord = Vector3.Dot(hitPoint, u);
        var vCoord = Vector3.Dot(hitPoint, v);

        if (uCoord < 0 || uCoord > size || vCoord < 0 || vCoord > size)
            return false;

        distance = t;
        return true;
    }

    private StrideVector3 GetPlaneAxis(StrideVector3 normal, int index)
    {
        if (normal.X != 0)
            return index == 0 ? StrideVector3.UnitY : StrideVector3.UnitZ;
        if (normal.Y != 0)
            return index == 0 ? StrideVector3.UnitX : StrideVector3.UnitZ;
        return index == 0 ? StrideVector3.UnitX : StrideVector3.UnitY;
    }

    private void RenderTranslateGizmo(StrideVector3 position, StrideQuaternion rotation, GizmoAxis active, float scale)
    {
        // Draw translation arrows
        // Implementation would use immediate mode line rendering or procedural meshes
    }

    private void RenderRotateGizmo(StrideVector3 position, StrideQuaternion rotation, GizmoAxis active, float scale)
    {
        // Draw rotation rings
    }

    private void RenderScaleGizmo(StrideVector3 position, StrideQuaternion rotation, GizmoAxis active, float scale)
    {
        // Draw scale boxes
    }

    private void RenderUniversalGizmo(StrideVector3 position, StrideQuaternion rotation, GizmoAxis active, float scale)
    {
        // Draw combined gizmo
    }

    private Color GetAxisColor(GizmoAxis axis, GizmoAxis active)
    {
        bool isActive = (active & axis) != 0;

        return axis switch
        {
            GizmoAxis.X => isActive ? HighlightColor : XAxisColor,
            GizmoAxis.Y => isActive ? HighlightColor : YAxisColor,
            GizmoAxis.Z => isActive ? HighlightColor : ZAxisColor,
            GizmoAxis.XY or GizmoAxis.XZ or GizmoAxis.YZ => isActive ? HighlightColor : PlaneColor,
            _ => Color.White
        };
    }
}
