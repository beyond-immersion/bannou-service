using BeyondImmersion.Bannou.SceneComposer.Math;
using Xunit;

namespace BeyondImmersion.Bannou.SceneComposer.Tests.Math;

/// <summary>
/// Tests for the Ray struct.
/// </summary>
public class RayTests
{
    private const double Epsilon = 1e-6;

    // =========================================================================
    // CONSTRUCTION
    // =========================================================================

    [Fact]
    public void Constructor_SetsOriginAndDirection()
    {
        var origin = new Vector3(1, 2, 3);
        var direction = new Vector3(1, 0, 0);

        var ray = new Ray(origin, direction);

        Assert.Equal(origin, ray.Origin);
        Assert.Equal(direction, ray.Direction);
    }

    [Fact]
    public void Constructor_NormalizesDirection()
    {
        var origin = Vector3.Zero;
        var direction = new Vector3(3, 0, 0); // Non-unit length

        var ray = new Ray(origin, direction);

        Assert.Equal(1, ray.Direction.Length, Epsilon);
        Assert.Equal(1, ray.Direction.X, Epsilon);
    }

    // =========================================================================
    // GET POINT
    // =========================================================================

    [Fact]
    public void GetPoint_AtZero_ReturnsOrigin()
    {
        var ray = new Ray(new Vector3(1, 2, 3), Vector3.UnitX);

        var point = ray.GetPoint(0);

        Assert.Equal(ray.Origin, point);
    }

    [Fact]
    public void GetPoint_AtDistance_ReturnsCorrectPoint()
    {
        var ray = new Ray(Vector3.Zero, Vector3.UnitZ);

        var point = ray.GetPoint(5);

        Assert.Equal(0, point.X, Epsilon);
        Assert.Equal(0, point.Y, Epsilon);
        Assert.Equal(5, point.Z, Epsilon);
    }

    [Fact]
    public void GetPoint_NegativeDistance_ReturnsPointBehindOrigin()
    {
        var ray = new Ray(new Vector3(0, 0, 10), Vector3.UnitZ);

        var point = ray.GetPoint(-5);

        Assert.Equal(0, point.X, Epsilon);
        Assert.Equal(0, point.Y, Epsilon);
        Assert.Equal(5, point.Z, Epsilon);
    }

    // =========================================================================
    // CLOSEST POINT TO
    // =========================================================================

    [Fact]
    public void ClosestPointTo_PointOnRay_ReturnsThatPoint()
    {
        var ray = new Ray(Vector3.Zero, Vector3.UnitX);
        var point = new Vector3(5, 0, 0);

        var closest = ray.ClosestPointTo(point);

        Assert.Equal(5, closest.X, Epsilon);
        Assert.Equal(0, closest.Y, Epsilon);
        Assert.Equal(0, closest.Z, Epsilon);
    }

    [Fact]
    public void ClosestPointTo_PointPerpendicularToRay_ReturnsProjectedPoint()
    {
        var ray = new Ray(Vector3.Zero, Vector3.UnitX);
        var point = new Vector3(5, 10, 0); // 10 units above the ray at X=5

        var closest = ray.ClosestPointTo(point);

        Assert.Equal(5, closest.X, Epsilon);
        Assert.Equal(0, closest.Y, Epsilon);
        Assert.Equal(0, closest.Z, Epsilon);
    }

    [Fact]
    public void ClosestPointTo_PointBehindOrigin_ReturnsOrigin()
    {
        var ray = new Ray(Vector3.Zero, Vector3.UnitX);
        var point = new Vector3(-5, 0, 0); // Behind the ray

        var closest = ray.ClosestPointTo(point);

        Assert.Equal(Vector3.Zero, closest);
    }

    // =========================================================================
    // DISTANCE TO POINT
    // =========================================================================

    [Fact]
    public void DistanceToPoint_PointOnRay_ReturnsZero()
    {
        var ray = new Ray(Vector3.Zero, Vector3.UnitX);
        var point = new Vector3(5, 0, 0);

        var distance = ray.DistanceToPoint(point);

        Assert.Equal(0, distance, Epsilon);
    }

    [Fact]
    public void DistanceToPoint_PointAboveRay_ReturnsPerpendicularDistance()
    {
        var ray = new Ray(Vector3.Zero, Vector3.UnitX);
        var point = new Vector3(5, 10, 0);

        var distance = ray.DistanceToPoint(point);

        Assert.Equal(10, distance, Epsilon);
    }

    // =========================================================================
    // SPHERE INTERSECTION
    // =========================================================================

    [Fact]
    public void IntersectsSphere_RayHitsSphere_ReturnsTrue()
    {
        var ray = new Ray(Vector3.Zero, Vector3.UnitZ);
        var center = new Vector3(0, 0, 10);
        var radius = 2.0;

        var hit = ray.IntersectsSphere(center, radius, out var distance);

        Assert.True(hit);
        Assert.Equal(8, distance, Epsilon); // 10 - 2 = 8
    }

    [Fact]
    public void IntersectsSphere_RayMissesSphere_ReturnsFalse()
    {
        var ray = new Ray(Vector3.Zero, Vector3.UnitZ);
        var center = new Vector3(10, 0, 10); // Off to the side
        var radius = 2.0;

        var hit = ray.IntersectsSphere(center, radius, out _);

        Assert.False(hit);
    }

    [Fact]
    public void IntersectsSphere_RayInsideSphere_ReturnsDistanceToExit()
    {
        var ray = new Ray(Vector3.Zero, Vector3.UnitZ);
        var center = Vector3.Zero;
        var radius = 5.0;

        var hit = ray.IntersectsSphere(center, radius, out var distance);

        Assert.True(hit);
        Assert.Equal(5, distance, Epsilon); // Exits at z=5
    }

    [Fact]
    public void IntersectsSphere_SphereBehindRay_ReturnsFalse()
    {
        var ray = new Ray(Vector3.Zero, Vector3.UnitZ);
        var center = new Vector3(0, 0, -10);
        var radius = 2.0;

        var hit = ray.IntersectsSphere(center, radius, out _);

        Assert.False(hit);
    }

    // =========================================================================
    // PLANE INTERSECTION
    // =========================================================================

    [Fact]
    public void IntersectsPlane_RayHitsPlane_ReturnsTrue()
    {
        var ray = new Ray(new Vector3(0, 0, -5), Vector3.UnitZ);
        var planePoint = Vector3.Zero;
        var planeNormal = -Vector3.UnitZ;

        var hit = ray.IntersectsPlane(planePoint, planeNormal, out var distance);

        Assert.True(hit);
        Assert.Equal(5, distance, Epsilon);
    }

    [Fact]
    public void IntersectsPlane_RayParallelToPlane_ReturnsFalse()
    {
        var ray = new Ray(new Vector3(0, 5, 0), Vector3.UnitX);
        var planePoint = Vector3.Zero;
        var planeNormal = Vector3.UnitY;

        var hit = ray.IntersectsPlane(planePoint, planeNormal, out _);

        Assert.False(hit);
    }

    [Fact]
    public void IntersectsPlane_PlaneBehindRay_ReturnsFalse()
    {
        var ray = new Ray(new Vector3(0, 0, 5), Vector3.UnitZ);
        var planePoint = Vector3.Zero;
        var planeNormal = Vector3.UnitZ;

        var hit = ray.IntersectsPlane(planePoint, planeNormal, out _);

        Assert.False(hit);
    }

    // =========================================================================
    // AABB INTERSECTION
    // =========================================================================

    [Fact]
    public void IntersectsAABB_RayHitsBox_ReturnsTrue()
    {
        var ray = new Ray(new Vector3(0, 0, -5), Vector3.UnitZ);
        var min = new Vector3(-1, -1, -1);
        var max = new Vector3(1, 1, 1);

        var hit = ray.IntersectsAABB(min, max, out var tMin, out var tMax);

        Assert.True(hit);
        Assert.Equal(4, tMin, Epsilon); // Enters at z=-1, origin at z=-5
        Assert.Equal(6, tMax, Epsilon); // Exits at z=1
    }

    [Fact]
    public void IntersectsAABB_RayMissesBox_ReturnsFalse()
    {
        var ray = new Ray(new Vector3(10, 10, -5), Vector3.UnitZ);
        var min = new Vector3(-1, -1, -1);
        var max = new Vector3(1, 1, 1);

        var hit = ray.IntersectsAABB(min, max, out _, out _);

        Assert.False(hit);
    }

    [Fact]
    public void IntersectsAABB_RayStartsInsideBox_ReturnsTrue()
    {
        var ray = new Ray(Vector3.Zero, Vector3.UnitZ);
        var min = new Vector3(-1, -1, -1);
        var max = new Vector3(1, 1, 1);

        var hit = ray.IntersectsAABB(min, max, out _, out var tMax);

        Assert.True(hit);
        Assert.Equal(1, tMax, Epsilon); // Exits at z=1
    }

    // =========================================================================
    // CLOSEST POINTS TO RAY
    // =========================================================================

    [Fact]
    public void ClosestPointsToRay_PerpendicularRays_FindsIntersection()
    {
        var ray1 = new Ray(Vector3.Zero, Vector3.UnitX);
        var ray2 = new Ray(new Vector3(5, 5, 0), -Vector3.UnitY);

        ray1.ClosestPointsToRay(ray2, out var t1, out var t2);

        Assert.Equal(5, t1, Epsilon); // 5 along X axis
        Assert.Equal(5, t2, Epsilon); // 5 along -Y axis
    }

    [Fact]
    public void ClosestPointsToRay_SkewRays_FindsClosestPoints()
    {
        var ray1 = new Ray(Vector3.Zero, Vector3.UnitX);
        var ray2 = new Ray(new Vector3(0, 1, 0), Vector3.UnitZ);

        ray1.ClosestPointsToRay(ray2, out var t1, out var t2);

        // Closest points are at origin and (0,1,0)
        Assert.Equal(0, t1, Epsilon);
        Assert.Equal(0, t2, Epsilon);
    }

    // =========================================================================
    // TO STRING
    // =========================================================================

    [Fact]
    public void ToString_ReturnsFormattedString()
    {
        var ray = new Ray(new Vector3(1, 2, 3), Vector3.UnitX);

        var str = ray.ToString();

        Assert.Contains("Ray", str);
        Assert.Contains("Origin", str);
        Assert.Contains("Dir", str);
    }
}
