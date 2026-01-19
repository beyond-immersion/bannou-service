namespace BeyondImmersion.Bannou.SceneComposer.Math;

/// <summary>
/// A ray defined by an origin point and direction.
/// Used for picking and intersection tests.
/// </summary>
public readonly struct Ray
{
    /// <summary>Origin point of the ray.</summary>
    public Vector3 Origin { get; }
    /// <summary>Normalized direction of the ray.</summary>
    public Vector3 Direction { get; }

    /// <summary>Creates a ray from an origin point and direction.</summary>
    public Ray(Vector3 origin, Vector3 direction)
    {
        Origin = origin;
        Direction = direction.Normalized;
    }

    /// <summary>
    /// Get a point along the ray at the specified distance.
    /// </summary>
    public Vector3 GetPoint(double distance) =>
        Origin + Direction * distance;

    /// <summary>
    /// Calculate the closest point on this ray to a given point.
    /// </summary>
    public Vector3 ClosestPointTo(Vector3 point)
    {
        var toPoint = point - Origin;
        var t = Vector3.Dot(toPoint, Direction);
        if (t < 0) return Origin;
        return Origin + Direction * t;
    }

    /// <summary>
    /// Calculate distance from the ray to a point.
    /// </summary>
    public double DistanceToPoint(Vector3 point)
    {
        var closest = ClosestPointTo(point);
        return Vector3.Distance(closest, point);
    }

    /// <summary>
    /// Test intersection with a sphere.
    /// </summary>
    /// <param name="center">Sphere center</param>
    /// <param name="radius">Sphere radius</param>
    /// <param name="distance">Distance to intersection point (if hit)</param>
    /// <returns>True if ray intersects sphere</returns>
    public bool IntersectsSphere(Vector3 center, double radius, out double distance)
    {
        distance = 0;
        var oc = Origin - center;
        var a = Vector3.Dot(Direction, Direction);
        var b = 2.0 * Vector3.Dot(oc, Direction);
        var c = Vector3.Dot(oc, oc) - radius * radius;
        var discriminant = b * b - 4 * a * c;

        if (discriminant < 0)
            return false;

        distance = (-b - System.Math.Sqrt(discriminant)) / (2.0 * a);
        if (distance < 0)
        {
            distance = (-b + System.Math.Sqrt(discriminant)) / (2.0 * a);
            if (distance < 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Test intersection with a plane.
    /// </summary>
    /// <param name="planePoint">A point on the plane</param>
    /// <param name="planeNormal">Plane normal (must be normalized)</param>
    /// <param name="distance">Distance to intersection point (if hit)</param>
    /// <returns>True if ray intersects plane (not parallel)</returns>
    public bool IntersectsPlane(Vector3 planePoint, Vector3 planeNormal, out double distance)
    {
        distance = 0;
        var denom = Vector3.Dot(planeNormal, Direction);

        // Ray is parallel to plane
        if (System.Math.Abs(denom) < 1e-6)
            return false;

        var t = Vector3.Dot(planePoint - Origin, planeNormal) / denom;

        // Intersection is behind the ray origin
        if (t < 0)
            return false;

        distance = t;
        return true;
    }

    /// <summary>
    /// Test intersection with an axis-aligned bounding box.
    /// </summary>
    public bool IntersectsAABB(Vector3 min, Vector3 max, out double tMin, out double tMax)
    {
        tMin = double.NegativeInfinity;
        tMax = double.PositiveInfinity;

        // X slab
        if (System.Math.Abs(Direction.X) > double.Epsilon)
        {
            var tx1 = (min.X - Origin.X) / Direction.X;
            var tx2 = (max.X - Origin.X) / Direction.X;
            tMin = System.Math.Max(tMin, System.Math.Min(tx1, tx2));
            tMax = System.Math.Min(tMax, System.Math.Max(tx1, tx2));
        }
        else if (Origin.X < min.X || Origin.X > max.X)
        {
            return false;
        }

        // Y slab
        if (System.Math.Abs(Direction.Y) > double.Epsilon)
        {
            var ty1 = (min.Y - Origin.Y) / Direction.Y;
            var ty2 = (max.Y - Origin.Y) / Direction.Y;
            tMin = System.Math.Max(tMin, System.Math.Min(ty1, ty2));
            tMax = System.Math.Min(tMax, System.Math.Max(ty1, ty2));
        }
        else if (Origin.Y < min.Y || Origin.Y > max.Y)
        {
            return false;
        }

        // Z slab
        if (System.Math.Abs(Direction.Z) > double.Epsilon)
        {
            var tz1 = (min.Z - Origin.Z) / Direction.Z;
            var tz2 = (max.Z - Origin.Z) / Direction.Z;
            tMin = System.Math.Max(tMin, System.Math.Min(tz1, tz2));
            tMax = System.Math.Min(tMax, System.Math.Max(tz1, tz2));
        }
        else if (Origin.Z < min.Z || Origin.Z > max.Z)
        {
            return false;
        }

        return tMax >= tMin && tMax >= 0;
    }

    /// <summary>
    /// Calculate the closest points between two rays.
    /// </summary>
    /// <param name="other">The other ray</param>
    /// <param name="thisT">Parameter along this ray</param>
    /// <param name="otherT">Parameter along other ray</param>
    public void ClosestPointsToRay(Ray other, out double thisT, out double otherT)
    {
        var w0 = Origin - other.Origin;
        var a = Vector3.Dot(Direction, Direction);
        var b = Vector3.Dot(Direction, other.Direction);
        var c = Vector3.Dot(other.Direction, other.Direction);
        var d = Vector3.Dot(Direction, w0);
        var e = Vector3.Dot(other.Direction, w0);

        var denom = a * c - b * b;

        if (System.Math.Abs(denom) < 1e-6)
        {
            // Rays are parallel
            thisT = 0;
            otherT = d / b;
        }
        else
        {
            thisT = (b * e - c * d) / denom;
            otherT = (a * e - b * d) / denom;
        }
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"Ray(Origin:{Origin}, Dir:{Direction})";
}
