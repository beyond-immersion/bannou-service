using BeyondImmersion.Bannou.Core.Math;

namespace BeyondImmersion.Bannou.SpriteTheory.Camera;

/// <summary>
/// Computes orthographic camera parameters to frame a 3D bounding box from a given capture angle
/// with no clipping or perspective foreshortening.
/// </summary>
public static class OrthographicSetup
{
    /// <summary>
    /// Computes the orthographic camera parameters that completely frame the given bounding box
    /// when viewed from the specified capture angle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The algorithm performs spherical-to-Cartesian conversion for the camera direction,
    /// positions the camera at 2.5× the bounding box half-diagonal to ensure no clipping,
    /// handles gimbal lock for near-vertical pitch angles, builds an orthonormal basis,
    /// projects all 8 bounding box corners onto the camera plane, applies a 10% safety margin,
    /// and adjusts for the frame aspect ratio.
    /// </para>
    /// </remarks>
    /// <param name="angle">The capture angle defining yaw and pitch.</param>
    /// <param name="bounds">The axis-aligned bounding box of the model.</param>
    /// <param name="frameSize">The output frame dimensions in pixels (Width, Height).</param>
    /// <returns>Complete orthographic camera parameters for engine configuration.</returns>
    public static OrthographicParameters Compute(
        CaptureAngle angle,
        BoundingBox bounds,
        (int Width, int Height) frameSize)
    {
        // Step 1: Camera direction from yaw/pitch (spherical to Cartesian)
        var yawRad = angle.Yaw * MathF.PI / 180f;
        var pitchRad = angle.Pitch * MathF.PI / 180f;
        var direction = new Vector3(
            MathF.Sin(yawRad) * MathF.Cos(pitchRad),
            MathF.Sin(pitchRad),
            MathF.Cos(yawRad) * MathF.Cos(pitchRad)).Normalized;

        // Step 2: Camera position — back along direction from bounds center
        var center = bounds.Center;
        var halfDiag = bounds.Extents.Length;
        var distance = halfDiag * 2.5f;
        var position = center - direction * distance;

        // Step 3: Up vector (handle near-vertical pitch to avoid gimbal lock)
        var up = MathF.Abs(angle.Pitch) > 89f
            ? new Vector3(0f, 0f, -MathF.Sign(angle.Pitch))
            : Vector3.UnitY;

        // Step 4: Orthonormal basis via cross products
        var right = Vector3.Cross(direction, up).Normalized;
        var correctedUp = Vector3.Cross(right, direction);

        // Step 5: Project all 8 bounding box corners onto the camera's view plane
        var minU = float.MaxValue;
        var maxU = float.MinValue;
        var minV = float.MaxValue;
        var maxV = float.MinValue;

        for (var i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? bounds.Min.X : bounds.Max.X,
                (i & 2) == 0 ? bounds.Min.Y : bounds.Max.Y,
                (i & 4) == 0 ? bounds.Min.Z : bounds.Max.Z);

            var rel = corner - position;
            var u = Vector3.Dot(rel, right);
            var v = Vector3.Dot(rel, correctedUp);

            if (u < minU) minU = u;
            if (u > maxU) maxU = u;
            if (v < minV) minV = v;
            if (v > maxV) maxV = v;
        }

        // Step 6: Ortho dimensions with 10% safety margin
        var orthoWidth = (maxU - minU) * 1.1f;
        var orthoHeight = (maxV - minV) * 1.1f;

        // Step 7: Match frame aspect ratio (expand the smaller dimension)
        var frameAspect = (float)frameSize.Width / frameSize.Height;
        var orthoAspect = orthoWidth / orthoHeight;
        if (frameAspect > orthoAspect)
        {
            orthoWidth = orthoHeight * frameAspect;
        }
        else
        {
            orthoHeight = orthoWidth / frameAspect;
        }

        return new OrthographicParameters(
            Position: position,
            Direction: direction,
            Up: correctedUp,
            OrthoWidth: orthoWidth,
            OrthoHeight: orthoHeight,
            NearPlane: 0.01f,
            FarPlane: distance * 3f);
    }
}
