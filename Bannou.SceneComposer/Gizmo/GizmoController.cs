using BeyondImmersion.Bannou.SceneComposer.Abstractions;
using BeyondImmersion.Bannou.SceneComposer.Math;
using BeyondImmersion.Bannou.SceneComposer.SceneGraph;
using BeyondImmersion.Bannou.SceneComposer.Selection;

namespace BeyondImmersion.Bannou.SceneComposer.Gizmo;

/// <summary>
/// Engine-agnostic controller for transform gizmos.
/// Handles the logic for gizmo interaction; rendering is delegated to the bridge.
/// </summary>
public class GizmoController
{
    private readonly SelectionManager _selection;
    private readonly ISceneComposerBridge _bridge;

    private bool _isDragging;
    private GizmoAxis _dragAxis;
    private Vector3 _dragStartPosition;
    private Quaternion _dragStartRotation;
    private Ray _dragStartRay;
    private Vector3 _dragPlaneNormal;
    private Vector3 _dragPlanePoint;
    private double _dragStartAngle;
    private Dictionary<Guid, Transform>? _dragStartTransforms;

    /// <summary>
    /// Current gizmo mode.
    /// </summary>
    public GizmoMode Mode { get; set; } = GizmoMode.Translate;

    /// <summary>
    /// Current coordinate space.
    /// </summary>
    public GizmoSpace Space { get; set; } = GizmoSpace.World;

    /// <summary>
    /// Current pivot mode for multi-selection.
    /// </summary>
    public GizmoPivot Pivot { get; set; } = GizmoPivot.Center;

    /// <summary>
    /// Snap settings.
    /// </summary>
    public SnapSettings Snap { get; } = new();

    /// <summary>
    /// Currently hovered axis.
    /// </summary>
    public GizmoAxis HoveredAxis { get; private set; }

    /// <summary>
    /// Whether gizmo is currently being dragged.
    /// </summary>
    public bool IsDragging => _isDragging;

    /// <summary>
    /// Axis being dragged.
    /// </summary>
    public GizmoAxis DragAxis => _dragAxis;

    /// <summary>
    /// Size factor for gizmo rendering (screen-space consistent).
    /// </summary>
    public double GizmoSize { get; set; } = 1.0;

    /// <summary>
    /// Minimum screen size for picking axes.
    /// </summary>
    public double PickRadius { get; set; } = 0.15;

    /// <summary>
    /// Raised when a drag operation starts.
    /// </summary>
    public event EventHandler? DragStarted;

    /// <summary>
    /// Raised when a drag operation ends.
    /// </summary>
    public event EventHandler? DragEnded;

    /// <summary>
    /// Raised when transform delta is applied.
    /// </summary>
    public event EventHandler<GizmoTransformEventArgs>? TransformApplied;

    /// <summary>
    /// Create a gizmo controller.
    /// </summary>
    public GizmoController(SelectionManager selection, ISceneComposerBridge bridge)
    {
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
    }

    /// <summary>
    /// Get the gizmo position based on current selection and pivot mode.
    /// </summary>
    public Vector3 GetGizmoPosition()
    {
        if (!_selection.HasSelection)
            return Vector3.Zero;

        return Pivot switch
        {
            GizmoPivot.Center => _selection.GetSelectionCenter(),
            GizmoPivot.Active => _selection.PrimaryNode?.GetWorldTransform().Position ?? Vector3.Zero,
            GizmoPivot.Median => _selection.GetSelectionCenter(),
            GizmoPivot.Origin => Vector3.Zero,
            _ => Vector3.Zero
        };
    }

    /// <summary>
    /// Get the gizmo rotation based on current space mode.
    /// </summary>
    public Quaternion GetGizmoRotation()
    {
        if (Space == GizmoSpace.Local && _selection.PrimaryNode != null)
        {
            return _selection.PrimaryNode.GetWorldTransform().Rotation;
        }
        return Quaternion.Identity;
    }

    /// <summary>
    /// Calculate gizmo scale for consistent screen size.
    /// </summary>
    public double CalculateGizmoScale(Vector3 cameraPosition)
    {
        var gizmoPos = GetGizmoPosition();
        var distance = Vector3.Distance(cameraPosition, gizmoPos);
        return distance * GizmoSize * 0.1; // Adjust factor as needed
    }

    /// <summary>
    /// Update hover state based on mouse ray.
    /// </summary>
    /// <param name="ray">Mouse ray in world space.</param>
    /// <returns>The hovered axis.</returns>
    public GizmoAxis UpdateHover(Ray ray)
    {
        if (_isDragging || !_selection.HasSelection || Mode == GizmoMode.None)
        {
            HoveredAxis = GizmoAxis.None;
            return GizmoAxis.None;
        }

        var gizmoPos = GetGizmoPosition();
        var gizmoRot = GetGizmoRotation();
        var gizmoScale = CalculateGizmoScale(_bridge.GetCameraPosition());

        HoveredAxis = _bridge.PickGizmoAxis(ray, gizmoPos, gizmoRot, Mode, gizmoScale);
        return HoveredAxis;
    }

    /// <summary>
    /// Begin a drag operation.
    /// </summary>
    /// <param name="ray">Starting mouse ray.</param>
    /// <param name="axis">Axis to drag (use HoveredAxis if None).</param>
    /// <returns>True if drag started.</returns>
    public bool BeginDrag(Ray ray, GizmoAxis axis = GizmoAxis.None)
    {
        if (!_selection.HasSelection || Mode == GizmoMode.None)
            return false;

        var targetAxis = axis != GizmoAxis.None ? axis : HoveredAxis;
        if (targetAxis == GizmoAxis.None)
            return false;

        _isDragging = true;
        _dragAxis = targetAxis;
        _dragStartRay = ray;
        _dragStartPosition = GetGizmoPosition();
        _dragStartRotation = GetGizmoRotation();

        // Store starting transforms for all selected nodes
        _dragStartTransforms = new Dictionary<Guid, Transform>();
        foreach (var node in _selection.SelectedNodes)
        {
            _dragStartTransforms[node.Id] = node.LocalTransform;
        }

        // Calculate drag plane
        SetupDragPlane(targetAxis);

        DragStarted?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Update the drag operation with a new mouse ray.
    /// </summary>
    /// <param name="ray">Current mouse ray.</param>
    /// <returns>The transform delta applied.</returns>
    public TransformDelta? UpdateDrag(Ray ray)
    {
        if (!_isDragging || _dragStartTransforms == null)
            return null;

        TransformDelta delta;

        switch (Mode)
        {
            case GizmoMode.Translate:
                delta = CalculateTranslationDelta(ray);
                break;

            case GizmoMode.Rotate:
                delta = CalculateRotationDelta(ray);
                break;

            case GizmoMode.Scale:
                delta = CalculateScaleDelta(ray);
                break;

            default:
                return null;
        }

        // Apply snapping
        delta = ApplySnapping(delta);

        // Apply delta to selected nodes
        ApplyTransformDelta(delta);

        TransformApplied?.Invoke(this, new GizmoTransformEventArgs(delta, _selection.SelectedNodes.ToList()));

        return delta;
    }

    /// <summary>
    /// End the drag operation.
    /// </summary>
    /// <returns>True if drag was active.</returns>
    public bool EndDrag()
    {
        if (!_isDragging)
            return false;

        _isDragging = false;
        _dragAxis = GizmoAxis.None;
        _dragStartTransforms = null;

        DragEnded?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Cancel the drag operation and restore original transforms.
    /// </summary>
    public void CancelDrag()
    {
        if (!_isDragging || _dragStartTransforms == null)
            return;

        // Restore original transforms
        foreach (var node in _selection.SelectedNodes)
        {
            if (_dragStartTransforms.TryGetValue(node.Id, out var originalTransform))
            {
                node.LocalTransform = originalTransform;
            }
        }

        _isDragging = false;
        _dragAxis = GizmoAxis.None;
        _dragStartTransforms = null;

        DragEnded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Render the gizmo using the bridge.
    /// </summary>
    public void Render()
    {
        if (!_selection.HasSelection || Mode == GizmoMode.None)
        {
            _bridge.HideGizmo();
            return;
        }

        var position = GetGizmoPosition();
        var rotation = GetGizmoRotation();
        var scale = CalculateGizmoScale(_bridge.GetCameraPosition());
        var activeAxis = _isDragging ? _dragAxis : HoveredAxis;

        _bridge.RenderGizmo(position, rotation, Mode, activeAxis, scale);
    }

    private void SetupDragPlane(GizmoAxis axis)
    {
        var gizmoRot = GetGizmoRotation();

        // Get axis directions in world space
        var xAxis = gizmoRot.Rotate(Vector3.UnitX);
        var yAxis = gizmoRot.Rotate(Vector3.UnitY);
        var zAxis = gizmoRot.Rotate(Vector3.UnitZ);

        _dragPlanePoint = _dragStartPosition;

        // Determine plane normal based on axis and mode
        if (Mode == GizmoMode.Translate || Mode == GizmoMode.Scale)
        {
            _dragPlaneNormal = axis switch
            {
                GizmoAxis.X => GetBestPlaneNormal(xAxis),
                GizmoAxis.Y => GetBestPlaneNormal(yAxis),
                GizmoAxis.Z => GetBestPlaneNormal(zAxis),
                GizmoAxis.XY => zAxis,
                GizmoAxis.XZ => yAxis,
                GizmoAxis.YZ => xAxis,
                GizmoAxis.Screen => _bridge.GetCameraForward(),
                _ => Vector3.UnitY
            };
        }
        else if (Mode == GizmoMode.Rotate)
        {
            // For rotation, the plane normal IS the rotation axis
            _dragPlaneNormal = axis switch
            {
                GizmoAxis.X => xAxis,
                GizmoAxis.Y => yAxis,
                GizmoAxis.Z => zAxis,
                GizmoAxis.Screen or GizmoAxis.View => _bridge.GetCameraForward(),
                _ => Vector3.UnitY
            };

            // Calculate initial angle
            if (_dragStartRay.IntersectsPlane(_dragPlanePoint, _dragPlaneNormal, out var dist))
            {
                var hitPoint = _dragStartRay.GetPoint(dist);
                var toHit = (hitPoint - _dragPlanePoint).Normalized;
                _dragStartAngle = System.Math.Atan2(
                    Vector3.Dot(Vector3.Cross(_dragPlaneNormal, toHit), GetRotationReferenceAxis()),
                    Vector3.Dot(toHit, GetRotationReferenceAxis()));
            }
        }
    }

    private Vector3 GetBestPlaneNormal(Vector3 axis)
    {
        // Choose plane normal that's most perpendicular to the camera view
        var camForward = _bridge.GetCameraForward();
        var candidates = new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };

        Vector3 best = Vector3.UnitY;
        double bestDot = 0;

        foreach (var candidate in candidates)
        {
            if (System.Math.Abs(Vector3.Dot(candidate, axis)) > 0.9) continue; // Skip if parallel to constraint axis

            var dot = System.Math.Abs(Vector3.Dot(candidate, camForward));
            if (dot > bestDot)
            {
                bestDot = dot;
                best = candidate;
            }
        }

        return best;
    }

    private Vector3 GetRotationReferenceAxis()
    {
        // Get a reference axis perpendicular to the rotation axis
        var up = Vector3.UnitY;
        if (System.Math.Abs(Vector3.Dot(_dragPlaneNormal, up)) > 0.9)
            up = Vector3.UnitX;

        return Vector3.Cross(_dragPlaneNormal, up).Normalized;
    }

    private TransformDelta CalculateTranslationDelta(Ray ray)
    {
        if (!ray.IntersectsPlane(_dragPlanePoint, _dragPlaneNormal, out var dist))
            return new TransformDelta();

        var currentPoint = ray.GetPoint(dist);

        if (!_dragStartRay.IntersectsPlane(_dragPlanePoint, _dragPlaneNormal, out var startDist))
            return new TransformDelta();

        var startPoint = _dragStartRay.GetPoint(startDist);
        var movement = currentPoint - startPoint;

        // Constrain to axis if single axis selected
        var gizmoRot = GetGizmoRotation();
        movement = ConstrainToAxis(movement, _dragAxis, gizmoRot);

        return new TransformDelta
        {
            Translation = movement,
            Type = TransformDeltaType.Translation
        };
    }

    private TransformDelta CalculateRotationDelta(Ray ray)
    {
        if (!ray.IntersectsPlane(_dragPlanePoint, _dragPlaneNormal, out var dist))
            return new TransformDelta();

        var hitPoint = ray.GetPoint(dist);
        var toHit = (hitPoint - _dragPlanePoint).Normalized;

        var currentAngle = System.Math.Atan2(
            Vector3.Dot(Vector3.Cross(_dragPlaneNormal, toHit), GetRotationReferenceAxis()),
            Vector3.Dot(toHit, GetRotationReferenceAxis()));

        var angleDelta = currentAngle - _dragStartAngle;

        return new TransformDelta
        {
            Rotation = Quaternion.FromAxisAngle(_dragPlaneNormal, angleDelta),
            RotationPivot = _dragStartPosition,
            Type = TransformDeltaType.Rotation
        };
    }

    private TransformDelta CalculateScaleDelta(Ray ray)
    {
        if (!ray.IntersectsPlane(_dragPlanePoint, _dragPlaneNormal, out var dist))
            return new TransformDelta();

        var currentPoint = ray.GetPoint(dist);

        if (!_dragStartRay.IntersectsPlane(_dragPlanePoint, _dragPlaneNormal, out var startDist))
            return new TransformDelta();

        var startPoint = _dragStartRay.GetPoint(startDist);

        var startDelta = startPoint - _dragStartPosition;
        var currentDelta = currentPoint - _dragStartPosition;

        var startLen = startDelta.Length;
        var currentLen = currentDelta.Length;

        if (startLen < 0.001) startLen = 0.001;

        var scaleFactor = currentLen / startLen;

        // Constrain to axis
        var scale = _dragAxis switch
        {
            GizmoAxis.X => new Vector3(scaleFactor, 1, 1),
            GizmoAxis.Y => new Vector3(1, scaleFactor, 1),
            GizmoAxis.Z => new Vector3(1, 1, scaleFactor),
            GizmoAxis.XY => new Vector3(scaleFactor, scaleFactor, 1),
            GizmoAxis.XZ => new Vector3(scaleFactor, 1, scaleFactor),
            GizmoAxis.YZ => new Vector3(1, scaleFactor, scaleFactor),
            _ => new Vector3(scaleFactor, scaleFactor, scaleFactor)
        };

        return new TransformDelta
        {
            Scale = scale,
            ScalePivot = _dragStartPosition,
            Type = TransformDeltaType.Scale
        };
    }

    private Vector3 ConstrainToAxis(Vector3 movement, GizmoAxis axis, Quaternion rotation)
    {
        var xAxis = rotation.Rotate(Vector3.UnitX);
        var yAxis = rotation.Rotate(Vector3.UnitY);
        var zAxis = rotation.Rotate(Vector3.UnitZ);

        return axis switch
        {
            GizmoAxis.X => xAxis * Vector3.Dot(movement, xAxis),
            GizmoAxis.Y => yAxis * Vector3.Dot(movement, yAxis),
            GizmoAxis.Z => zAxis * Vector3.Dot(movement, zAxis),
            GizmoAxis.XY => movement - zAxis * Vector3.Dot(movement, zAxis),
            GizmoAxis.XZ => movement - yAxis * Vector3.Dot(movement, yAxis),
            GizmoAxis.YZ => movement - xAxis * Vector3.Dot(movement, xAxis),
            _ => movement
        };
    }

    private TransformDelta ApplySnapping(TransformDelta delta)
    {
        if (!Snap.Enabled)
            return delta;

        return delta.Type switch
        {
            TransformDeltaType.Translation => delta with
            {
                Translation = new Vector3(
                    Snap.Snap(delta.Translation.X, Snap.TranslateSnap),
                    Snap.Snap(delta.Translation.Y, Snap.TranslateSnap),
                    Snap.Snap(delta.Translation.Z, Snap.TranslateSnap))
            },
            TransformDeltaType.Rotation => delta, // Rotation snapping is more complex
            TransformDeltaType.Scale => delta with
            {
                Scale = new Vector3(
                    1 + Snap.Snap(delta.Scale.X - 1, Snap.ScaleSnap),
                    1 + Snap.Snap(delta.Scale.Y - 1, Snap.ScaleSnap),
                    1 + Snap.Snap(delta.Scale.Z - 1, Snap.ScaleSnap))
            },
            _ => delta
        };
    }

    private void ApplyTransformDelta(TransformDelta delta)
    {
        if (_dragStartTransforms == null)
            return;

        foreach (var node in _selection.SelectedNodes)
        {
            if (!_dragStartTransforms.TryGetValue(node.Id, out var startTransform))
                continue;

            var newTransform = startTransform;

            switch (delta.Type)
            {
                case TransformDeltaType.Translation:
                    // Convert world delta to local space if node has parent
                    var localDelta = delta.Translation;
                    if (node.Parent != null)
                    {
                        var parentWorldRot = node.Parent.GetWorldTransform().Rotation;
                        localDelta = parentWorldRot.Inverse.Rotate(delta.Translation);
                    }
                    newTransform = newTransform.WithPosition(startTransform.Position + localDelta);
                    break;

                case TransformDeltaType.Rotation:
                    // Rotate around pivot
                    var pivotOffset = node.GetWorldTransform().Position - delta.RotationPivot;
                    var rotatedOffset = delta.Rotation.Rotate(pivotOffset);
                    var newWorldPos = delta.RotationPivot + rotatedOffset;

                    // Convert back to local
                    if (node.Parent != null)
                    {
                        var parentInverse = node.Parent.GetWorldTransform();
                        newWorldPos = parentInverse.InverseTransformPoint(newWorldPos);
                    }

                    newTransform = newTransform
                        .WithPosition(new Vector3(newWorldPos.X, newWorldPos.Y, newWorldPos.Z))
                        .WithRotation(delta.Rotation * startTransform.Rotation);
                    break;

                case TransformDeltaType.Scale:
                    // Scale around pivot - simplified, doesn't handle pivot offset
                    newTransform = newTransform.WithScale(new Vector3(
                        startTransform.Scale.X * delta.Scale.X,
                        startTransform.Scale.Y * delta.Scale.Y,
                        startTransform.Scale.Z * delta.Scale.Z));
                    break;
            }

            node.LocalTransform = newTransform;
        }
    }
}

/// <summary>
/// Represents a transform delta from a gizmo operation.
/// </summary>
public record struct TransformDelta
{
    /// <summary>
    /// Type of transform operation.
    /// </summary>
    public TransformDeltaType Type { get; init; }

    /// <summary>
    /// Translation delta (world space).
    /// </summary>
    public Vector3 Translation { get; init; }

    /// <summary>
    /// Rotation delta.
    /// </summary>
    public Quaternion Rotation { get; init; }

    /// <summary>
    /// Pivot point for rotation.
    /// </summary>
    public Vector3 RotationPivot { get; init; }

    /// <summary>
    /// Scale factors.
    /// </summary>
    public Vector3 Scale { get; init; }

    /// <summary>
    /// Pivot point for scaling.
    /// </summary>
    public Vector3 ScalePivot { get; init; }

    public TransformDelta()
    {
        Type = TransformDeltaType.None;
        Translation = Vector3.Zero;
        Rotation = Quaternion.Identity;
        Scale = Vector3.One;
    }
}

/// <summary>
/// Type of transform delta.
/// </summary>
public enum TransformDeltaType
{
    None,
    Translation,
    Rotation,
    Scale
}

/// <summary>
/// Event args for gizmo transform operations.
/// </summary>
public class GizmoTransformEventArgs : EventArgs
{
    /// <summary>
    /// The transform delta applied.
    /// </summary>
    public TransformDelta Delta { get; }

    /// <summary>
    /// Nodes that were transformed.
    /// </summary>
    public IReadOnlyList<ComposerSceneNode> AffectedNodes { get; }

    public GizmoTransformEventArgs(TransformDelta delta, IReadOnlyList<ComposerSceneNode> affectedNodes)
    {
        Delta = delta;
        AffectedNodes = affectedNodes;
    }
}
