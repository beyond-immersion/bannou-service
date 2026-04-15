namespace BeyondImmersion.Bannou.SpriteTheory.Camera;

/// <summary>
/// Computes sprite pivot points from model bounding boxes and camera parameters.
/// Projects the bottom-center of the bounding box (a standing humanoid's feet) onto the
/// capture camera's frame plane, yielding a pivot expressed in normalized frame coordinates.
/// </summary>
/// <remarks>
/// <para>
/// Pivot convention: (0, 0) = top-left of the frame; (1, 1) = bottom-right. <see cref="DefaultHumanoidPivot"/>
/// is (0.5, 0.85) — center-X, 85% from top — which anchors upright humanoid characters near
/// their feet. Auto-computed pivots converge on this default for typical humanoid bounds at
/// typical camera angles, and diverge for subjects with non-humanoid proportions.
/// </para>
/// <para>
/// Use <see cref="ComputeFromBounds"/> when you have both the model bounds and the configured
/// orthographic camera for an angle — the computed pivot accounts for the camera's viewpoint.
/// For subjects whose feet projection is unreliable (flying enemies, asymmetric bosses),
/// consumers should provide a <see cref="BeyondImmersion.Bannou.SpriteTheory.Metadata.CharacterVariant.PivotOverride"/>
/// and skip auto-computation.
/// </para>
/// </remarks>
public static class PivotComputer
{
    /// <summary>
    /// The humanoid-standing default pivot: center-X, 85% from the top.
    /// Used as a fallback when neither a per-variant override nor auto-computation is applied.
    /// </summary>
    public static readonly Vector2 DefaultHumanoidPivot = new(0.5f, 0.85f);

    /// <summary>
    /// Computes a pivot point by projecting the bottom-center of a bounding box onto the
    /// frame plane of an orthographic camera, returning normalized frame coordinates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The "feet" point is defined as (Center.X, Min.Y, Center.Z) in world space — the
    /// midpoint of the bounding box in the horizontal plane, at the lowest vertical extent.
    /// For an upright humanoid with the Y axis pointing up, this is where the feet touch the
    /// ground.
    /// </para>
    /// <para>
    /// The point is projected into the camera's (right, up) plane via dot products with the
    /// camera basis vectors, then mapped to normalized frame coordinates where (0, 0) is the
    /// top-left and (1, 1) is the bottom-right. The result is clamped to [0, 1] to guarantee
    /// the pivot stays within the frame even if the feet project slightly outside.
    /// </para>
    /// </remarks>
    /// <param name="bounds">Axis-aligned model bounds in world space.</param>
    /// <param name="camera">Orthographic camera parameters (typically from <see cref="OrthographicSetup.Compute"/>).</param>
    /// <returns>Pivot in normalized frame coordinates, clamped to [0, 1] × [0, 1].</returns>
    public static Vector2 ComputeFromBounds(BoundingBox bounds, OrthographicParameters camera)
    {
        // Feet point: bottom-center of the bounding box in world space.
        var feetX = (bounds.Min.X + bounds.Max.X) * 0.5f;
        var feetY = bounds.Min.Y;
        var feetZ = (bounds.Min.Z + bounds.Max.Z) * 0.5f;

        // Relative to the camera position.
        var relX = feetX - camera.Position.X;
        var relY = feetY - camera.Position.Y;
        var relZ = feetZ - camera.Position.Z;

        // Camera right axis = normalize(cross(direction, up)). Matches OrthographicSetup's basis.
        var rightX = camera.Direction.Y * camera.Up.Z - camera.Direction.Z * camera.Up.Y;
        var rightY = camera.Direction.Z * camera.Up.X - camera.Direction.X * camera.Up.Z;
        var rightZ = camera.Direction.X * camera.Up.Y - camera.Direction.Y * camera.Up.X;
        var rightLen = MathF.Sqrt(rightX * rightX + rightY * rightY + rightZ * rightZ);
        if (rightLen < 1e-10f)
        {
            // Degenerate camera (direction parallel to up) — cannot project. Fall back to default.
            return DefaultHumanoidPivot;
        }
        rightX /= rightLen;
        rightY /= rightLen;
        rightZ /= rightLen;

        // Project onto (right, up) plane of the camera.
        var u = relX * rightX + relY * rightY + relZ * rightZ;
        var v = relX * camera.Up.X + relY * camera.Up.Y + relZ * camera.Up.Z;

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
