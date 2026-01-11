namespace BeyondImmersion.Bannou.SceneComposer.Gizmo;

/// <summary>
/// Transform gizmo operation mode.
/// </summary>
public enum GizmoMode
{
    /// <summary>Gizmo is hidden/inactive.</summary>
    None,

    /// <summary>Translation (move) mode.</summary>
    Translate,

    /// <summary>Rotation mode.</summary>
    Rotate,

    /// <summary>Scale mode.</summary>
    Scale,

    /// <summary>Universal mode (all operations combined).</summary>
    Universal
}

/// <summary>
/// Gizmo axis or plane being manipulated.
/// </summary>
[Flags]
public enum GizmoAxis
{
    /// <summary>No axis selected.</summary>
    None = 0,

    /// <summary>X axis.</summary>
    X = 1,

    /// <summary>Y axis.</summary>
    Y = 2,

    /// <summary>Z axis.</summary>
    Z = 4,

    /// <summary>XY plane (Z normal).</summary>
    XY = X | Y,

    /// <summary>XZ plane (Y normal).</summary>
    XZ = X | Z,

    /// <summary>YZ plane (X normal).</summary>
    YZ = Y | Z,

    /// <summary>All axes (uniform operation).</summary>
    XYZ = X | Y | Z,

    /// <summary>Screen-space plane (camera-facing).</summary>
    Screen = 8,

    /// <summary>View axis (for certain rotation operations).</summary>
    View = 16
}

/// <summary>
/// Coordinate space for gizmo operations.
/// </summary>
public enum GizmoSpace
{
    /// <summary>World coordinate space.</summary>
    World,

    /// <summary>Local (object) coordinate space.</summary>
    Local
}

/// <summary>
/// Pivot point for multi-selection operations.
/// </summary>
public enum GizmoPivot
{
    /// <summary>Use center of selection bounds.</summary>
    Center,

    /// <summary>Use primary (last selected) object's position.</summary>
    Active,

    /// <summary>Use median position of all selected objects.</summary>
    Median,

    /// <summary>Use scene origin.</summary>
    Origin
}

/// <summary>
/// Snapping mode for gizmo operations.
/// </summary>
public enum SnapMode
{
    /// <summary>No snapping.</summary>
    None,

    /// <summary>Snap to grid increments.</summary>
    Grid,

    /// <summary>Snap to surface.</summary>
    Surface,

    /// <summary>Snap to other objects (vertex/edge).</summary>
    Object
}

/// <summary>
/// Configuration for gizmo snapping.
/// </summary>
public class SnapSettings
{
    /// <summary>
    /// Translation snap increment (units).
    /// </summary>
    public double TranslateSnap { get; set; } = 1.0;

    /// <summary>
    /// Rotation snap increment (degrees).
    /// </summary>
    public double RotateSnap { get; set; } = 15.0;

    /// <summary>
    /// Scale snap increment (factor, e.g., 0.1 = 10%).
    /// </summary>
    public double ScaleSnap { get; set; } = 0.1;

    /// <summary>
    /// Whether snapping is currently enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Current snap mode.
    /// </summary>
    public SnapMode Mode { get; set; } = SnapMode.Grid;

    /// <summary>
    /// Apply snapping to a value.
    /// </summary>
    public double Snap(double value, double increment)
    {
        if (!Enabled || increment <= 0) return value;
        return System.Math.Round(value / increment) * increment;
    }
}
