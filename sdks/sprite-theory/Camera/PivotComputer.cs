using BeyondImmersion.Bannou.Core.Math;

namespace BeyondImmersion.Bannou.SpriteTheory.Camera;

/// <summary>
/// Computes sprite pivot points from model bounding boxes, arbitrary world points, and camera parameters.
/// Projects points in world space onto the capture camera's frame plane, yielding pivots expressed in
/// normalized frame coordinates.
/// </summary>
/// <remarks>
/// <para>
/// Pivot convention: (0, 0) = top-left of the frame; (1, 1) = bottom-right. <see cref="DefaultHumanoidPivot"/>
/// is (0.5, 0.85) — center-X, 85% from top — which anchors upright humanoid characters near
/// their feet. Auto-computed pivots converge on this default for typical humanoid bounds at
/// typical camera angles, and diverge for subjects with non-humanoid proportions.
/// </para>
/// <para>
/// Use <see cref="ComputeFromBounds"/> for the common feet-on-ground convention — it projects the
/// bottom-center of the bounding box onto the frame plane. Use <see cref="ProjectWorldPointToFrame"/>
/// when you have a specific anchor point (e.g., a skeleton bone's world position from the bridge).
/// For subjects whose feet projection is unreliable (flying enemies, asymmetric bosses),
/// consumers should provide a <see cref="BeyondImmersion.Bannou.SpriteTheory.Metadata.CharacterVariant.PivotOverride"/>
/// or a bone-based <see cref="BeyondImmersion.Bannou.SpriteTheory.Metadata.CharacterVariant.AnchorBoneName"/>
/// and skip bounds-based auto-computation.
/// </para>
/// </remarks>
public static class PivotComputer
{
    /// <summary>
    /// The humanoid-standing default pivot: center-X, 85% from the top.
    /// Used as a fallback when neither a per-variant override nor auto-computation is applied,
    /// and when the camera basis is degenerate or the orthographic dimensions are zero.
    /// </summary>
    public static readonly Vector2 DefaultHumanoidPivot = new(0.5f, 0.85f);

    /// <summary>
    /// Computes a pivot by projecting the bottom-center of a bounding box ("feet point") onto the
    /// frame plane of an orthographic camera, returning normalized frame coordinates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The "feet" point is defined as (Center.X, Min.Y, Center.Z) in world space — the
    /// midpoint of the bounding box in the horizontal plane, at the lowest vertical extent.
    /// For an upright humanoid with the Y axis pointing up, this is where the feet touch the
    /// ground. This method is a thin wrapper around <see cref="ProjectWorldPointToFrame"/>
    /// that computes the feet point and delegates.
    /// </para>
    /// </remarks>
    /// <param name="bounds">Axis-aligned model bounds in world space.</param>
    /// <param name="camera">Orthographic camera parameters (typically from <see cref="OrthographicSetup.Compute"/>).</param>
    /// <returns>Pivot in normalized frame coordinates, clamped to [0, 1] × [0, 1].</returns>
    public static Vector2 ComputeFromBounds(BoundingBox bounds, OrthographicParameters camera)
    {
        var feet = new Vector3(
            (bounds.Min.X + bounds.Max.X) * 0.5f,
            bounds.Min.Y,
            (bounds.Min.Z + bounds.Max.Z) * 0.5f);

        return ProjectWorldPointToFrame(feet, camera);
    }

    /// <summary>
    /// Projects a world-space point onto the frame plane of an orthographic camera and returns
    /// the result in normalized frame coordinates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The point is projected into the camera's (right, up) plane via dot products with the
    /// camera basis vectors, then mapped to normalized frame coordinates where (0, 0) is the
    /// top-left and (1, 1) is the bottom-right. The result is clamped to [0, 1] to guarantee
    /// the pivot stays within the frame even if the world point projects slightly outside.
    /// </para>
    /// <para>
    /// Returns <see cref="DefaultHumanoidPivot"/> when the camera basis is degenerate
    /// (direction parallel to up) or the orthographic dimensions are zero — these cases
    /// admit no well-defined projection.
    /// </para>
    /// </remarks>
    /// <param name="worldPoint">Point in world space to project.</param>
    /// <param name="camera">Orthographic camera parameters (typically from <see cref="OrthographicSetup.Compute"/>).</param>
    /// <returns>Pivot in normalized frame coordinates, clamped to [0, 1] × [0, 1].</returns>
    public static Vector2 ProjectWorldPointToFrame(Vector3 worldPoint, OrthographicParameters camera)
    {
        var rel = worldPoint - camera.Position;

        // Camera right axis = normalize(cross(direction, up)). Matches OrthographicSetup's basis.
        var right = Vector3.Cross(camera.Direction, camera.Up);
        if (right.LengthSquared < 1e-20f)
        {
            // Degenerate camera (direction parallel to up) — cannot project.
            return DefaultHumanoidPivot;
        }

        right = right.Normalized;

        // Project onto (right, up) plane of the camera.
        var u = Vector3.Dot(rel, right);
        var v = Vector3.Dot(rel, camera.Up);

        // Normalize to [0, 1] frame coords. Pivot Y is top-down; world/camera V is bottom-up,
        // so pivotY = 0.5 - v/orthoHeight flips the axis.
        if (camera.OrthoWidth <= 0f || camera.OrthoHeight <= 0f)
        {
            return DefaultHumanoidPivot;
        }

        var pivotX = 0.5f + u / camera.OrthoWidth;
        var pivotY = 0.5f - v / camera.OrthoHeight;

        return new Vector2(Clamp01(pivotX), Clamp01(pivotY));
    }

    private static float Clamp01(float value)
    {
        if (value < 0f) return 0f;
        if (value > 1f) return 1f;
        return value;
    }
}
