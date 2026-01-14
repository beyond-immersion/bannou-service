using Godot;

namespace BeyondImmersion.Bannou.Godot.SceneComposer.Math;

/// <summary>
/// Utility class for ray-based mathematical operations.
/// </summary>
public static class RayMath
{
    /// <summary>
    /// Tests if a ray intersects an axis-aligned bounding box using the slab method.
    /// </summary>
    /// <param name="origin">Ray origin point.</param>
    /// <param name="direction">Ray direction (should be normalized).</param>
    /// <param name="aabb">The axis-aligned bounding box to test against.</param>
    /// <param name="distance">Output: distance along ray to intersection point (if hit).</param>
    /// <returns>True if the ray intersects the AABB, false otherwise.</returns>
    public static bool RayIntersectsAabb(Vector3 origin, Vector3 direction, Aabb aabb, out float distance)
    {
        distance = 0;

        var min = aabb.Position;
        var max = aabb.End;

        float tMin = float.NegativeInfinity;
        float tMax = float.PositiveInfinity;

        // X slab
        if (System.Math.Abs(direction.X) < float.Epsilon)
        {
            // Ray is parallel to X slab - check if origin is within slab
            if (origin.X < min.X || origin.X > max.X)
                return false;
        }
        else
        {
            float invD = 1.0f / direction.X;
            float t1 = (min.X - origin.X) * invD;
            float t2 = (max.X - origin.X) * invD;

            if (t1 > t2)
                (t1, t2) = (t2, t1);

            tMin = System.Math.Max(tMin, t1);
            tMax = System.Math.Min(tMax, t2);

            if (tMin > tMax)
                return false;
        }

        // Y slab
        if (System.Math.Abs(direction.Y) < float.Epsilon)
        {
            if (origin.Y < min.Y || origin.Y > max.Y)
                return false;
        }
        else
        {
            float invD = 1.0f / direction.Y;
            float t1 = (min.Y - origin.Y) * invD;
            float t2 = (max.Y - origin.Y) * invD;

            if (t1 > t2)
                (t1, t2) = (t2, t1);

            tMin = System.Math.Max(tMin, t1);
            tMax = System.Math.Min(tMax, t2);

            if (tMin > tMax)
                return false;
        }

        // Z slab
        if (System.Math.Abs(direction.Z) < float.Epsilon)
        {
            if (origin.Z < min.Z || origin.Z > max.Z)
                return false;
        }
        else
        {
            float invD = 1.0f / direction.Z;
            float t1 = (min.Z - origin.Z) * invD;
            float t2 = (max.Z - origin.Z) * invD;

            if (t1 > t2)
                (t1, t2) = (t2, t1);

            tMin = System.Math.Max(tMin, t1);
            tMax = System.Math.Min(tMax, t2);

            if (tMin > tMax)
                return false;
        }

        // Intersection is valid if tMax >= 0 (intersection is in front of or at origin)
        if (tMax < 0)
            return false;

        // Return the nearest intersection point (tMin if positive, else 0 if origin is inside)
        distance = tMin >= 0 ? tMin : 0;
        return true;
    }
}
