using Xunit;
using Godot;
using BeyondImmersion.Bannou.Godot.SceneComposer.Math;

namespace BeyondImmersion.Bannou.Godot.SceneComposer.Tests.Math;

/// <summary>
/// Tests for RayMath utility class.
/// </summary>
public class RayMathTests
{
    private const float Epsilon = 1e-5f;

    // =========================================================================
    // BASIC INTERSECTION TESTS
    // =========================================================================

    [Fact]
    public void RayIntersectsAabb_DirectHit_ReturnsTrue()
    {
        // Ray pointing directly at center of unit cube at origin
        var origin = new Vector3(0, 0, -5);
        var direction = new Vector3(0, 0, 1);
        var aabb = new Aabb(new Vector3(-1, -1, -1), new Vector3(2, 2, 2));

        var result = RayMath.RayIntersectsAabb(origin, direction, aabb, out var distance);

        Assert.True(result);
        Assert.Equal(4f, distance, Epsilon); // -5 + 4 = -1 (front face)
    }

    [Fact]
    public void RayIntersectsAabb_Miss_ReturnsFalse()
    {
        // Ray pointing away from the cube
        var origin = new Vector3(0, 0, -5);
        var direction = new Vector3(0, 0, -1); // Pointing away
        var aabb = new Aabb(new Vector3(-1, -1, -1), new Vector3(2, 2, 2));

        var result = RayMath.RayIntersectsAabb(origin, direction, aabb, out _);

        Assert.False(result);
    }

    [Fact]
    public void RayIntersectsAabb_MissToSide_ReturnsFalse()
    {
        // Ray passing beside the cube
        var origin = new Vector3(5, 0, -5);
        var direction = new Vector3(0, 0, 1);
        var aabb = new Aabb(new Vector3(-1, -1, -1), new Vector3(2, 2, 2));

        var result = RayMath.RayIntersectsAabb(origin, direction, aabb, out _);

        Assert.False(result);
    }

    // =========================================================================
    // AXIS-ALIGNED RAY TESTS
    // =========================================================================

    [Fact]
    public void RayIntersectsAabb_AlongXAxis_ReturnsCorrectDistance()
    {
        var origin = new Vector3(-5, 0, 0);
        var direction = new Vector3(1, 0, 0);
        var aabb = new Aabb(new Vector3(-1, -1, -1), new Vector3(2, 2, 2));

        var result = RayMath.RayIntersectsAabb(origin, direction, aabb, out var distance);

        Assert.True(result);
        Assert.Equal(4f, distance, Epsilon);
    }

    [Fact]
    public void RayIntersectsAabb_AlongYAxis_ReturnsCorrectDistance()
    {
        var origin = new Vector3(0, -5, 0);
        var direction = new Vector3(0, 1, 0);
        var aabb = new Aabb(new Vector3(-1, -1, -1), new Vector3(2, 2, 2));

        var result = RayMath.RayIntersectsAabb(origin, direction, aabb, out var distance);

        Assert.True(result);
        Assert.Equal(4f, distance, Epsilon);
    }

    [Fact]
    public void RayIntersectsAabb_AlongZAxis_ReturnsCorrectDistance()
    {
        var origin = new Vector3(0, 0, -5);
        var direction = new Vector3(0, 0, 1);
        var aabb = new Aabb(new Vector3(-1, -1, -1), new Vector3(2, 2, 2));

        var result = RayMath.RayIntersectsAabb(origin, direction, aabb, out var distance);

        Assert.True(result);
        Assert.Equal(4f, distance, Epsilon);
    }

    // =========================================================================
    // DIAGONAL RAY TESTS
    // =========================================================================

    [Fact]
    public void RayIntersectsAabb_DiagonalHit_ReturnsTrue()
    {
        // Ray from corner direction
        var origin = new Vector3(-5, -5, -5);
        var direction = new Vector3(1, 1, 1).Normalized();
        var aabb = new Aabb(new Vector3(-1, -1, -1), new Vector3(2, 2, 2));

        var result = RayMath.RayIntersectsAabb(origin, direction, aabb, out var distance);

        Assert.True(result);
        Assert.True(distance > 0);
    }

    [Fact]
    public void RayIntersectsAabb_DiagonalMiss_ReturnsFalse()
    {
        // Ray from offset diagonal that misses
        var origin = new Vector3(10, 10, -5);
        var direction = new Vector3(0, 0, 1);
        var aabb = new Aabb(new Vector3(-1, -1, -1), new Vector3(2, 2, 2));

        var result = RayMath.RayIntersectsAabb(origin, direction, aabb, out _);

        Assert.False(result);
    }

    // =========================================================================
    // ORIGIN INSIDE AABB TESTS
    // =========================================================================

    [Fact]
    public void RayIntersectsAabb_OriginInsideAabb_ReturnsZeroDistance()
    {
        // Ray origin is inside the cube
        var origin = new Vector3(0, 0, 0);
        var direction = new Vector3(0, 0, 1);
        var aabb = new Aabb(new Vector3(-1, -1, -1), new Vector3(2, 2, 2));

        var result = RayMath.RayIntersectsAabb(origin, direction, aabb, out var distance);

        Assert.True(result);
        Assert.Equal(0f, distance, Epsilon); // Origin is inside, so distance is 0
    }

    [Fact]
    public void RayIntersectsAabb_OriginOnSurface_ReturnsZeroOrSmallDistance()
    {
        // Ray origin is on the surface of the cube
        var origin = new Vector3(-1, 0, 0);
        var direction = new Vector3(1, 0, 0);
        var aabb = new Aabb(new Vector3(-1, -1, -1), new Vector3(2, 2, 2));

        var result = RayMath.RayIntersectsAabb(origin, direction, aabb, out var distance);

        Assert.True(result);
        Assert.True(distance >= 0);
    }

    // =========================================================================
    // PARALLEL RAY TESTS
    // =========================================================================

    [Fact]
    public void RayIntersectsAabb_ParallelToFace_InsideSlab_ReturnsTrue()
    {
        // Ray parallel to X axis, but within Y and Z bounds
        var origin = new Vector3(-5, 0, 0);
        var direction = new Vector3(1, 0, 0);
        var aabb = new Aabb(new Vector3(-1, -1, -1), new Vector3(2, 2, 2));

        var result = RayMath.RayIntersectsAabb(origin, direction, aabb, out _);

        Assert.True(result);
    }

    [Fact]
    public void RayIntersectsAabb_ParallelToFace_OutsideSlab_ReturnsFalse()
    {
        // Ray parallel to X axis, but outside Y bounds
        var origin = new Vector3(-5, 5, 0);
        var direction = new Vector3(1, 0, 0);
        var aabb = new Aabb(new Vector3(-1, -1, -1), new Vector3(2, 2, 2));

        var result = RayMath.RayIntersectsAabb(origin, direction, aabb, out _);

        Assert.False(result);
    }

    // =========================================================================
    // EDGE CASES
    // =========================================================================

    [Fact]
    public void RayIntersectsAabb_TinyAabb_ReturnsTrue()
    {
        // Very small AABB
        var origin = new Vector3(0, 0, -1);
        var direction = new Vector3(0, 0, 1);
        var aabb = new Aabb(new Vector3(-0.001f, -0.001f, -0.001f), new Vector3(0.002f, 0.002f, 0.002f));

        var result = RayMath.RayIntersectsAabb(origin, direction, aabb, out var distance);

        Assert.True(result);
        Assert.True(distance > 0);
    }

    [Fact]
    public void RayIntersectsAabb_LargeAabb_ReturnsTrue()
    {
        // Very large AABB
        var origin = new Vector3(0, 0, -1000);
        var direction = new Vector3(0, 0, 1);
        var aabb = new Aabb(new Vector3(-500, -500, -500), new Vector3(1000, 1000, 1000));

        var result = RayMath.RayIntersectsAabb(origin, direction, aabb, out var distance);

        Assert.True(result);
        Assert.Equal(500f, distance, Epsilon);
    }

    [Fact]
    public void RayIntersectsAabb_AabbBehindRay_ReturnsFalse()
    {
        // AABB is behind the ray origin
        var origin = new Vector3(0, 0, 5);
        var direction = new Vector3(0, 0, 1); // Pointing away from AABB
        var aabb = new Aabb(new Vector3(-1, -1, -1), new Vector3(2, 2, 2));

        var result = RayMath.RayIntersectsAabb(origin, direction, aabb, out _);

        Assert.False(result);
    }

    [Fact]
    public void RayIntersectsAabb_GrazingEdge_ReturnsTrue()
    {
        // Ray grazes the edge of the AABB
        var origin = new Vector3(-5, 1, 1);
        var direction = new Vector3(1, 0, 0);
        var aabb = new Aabb(new Vector3(-1, -1, -1), new Vector3(2, 2, 2));

        var result = RayMath.RayIntersectsAabb(origin, direction, aabb, out _);

        Assert.True(result);
    }

    // =========================================================================
    // NEGATIVE COORDINATE TESTS
    // =========================================================================

    [Fact]
    public void RayIntersectsAabb_NegativeCoordinates_WorksCorrectly()
    {
        // AABB entirely in negative coordinates
        var origin = new Vector3(0, 0, 0);
        var direction = new Vector3(-1, -1, -1).Normalized();
        var aabb = new Aabb(new Vector3(-5, -5, -5), new Vector3(2, 2, 2));

        var result = RayMath.RayIntersectsAabb(origin, direction, aabb, out var distance);

        Assert.True(result);
        Assert.True(distance > 0);
    }

    // =========================================================================
    // OFFSET AABB TESTS
    // =========================================================================

    [Fact]
    public void RayIntersectsAabb_OffsetAabb_ReturnsCorrectDistance()
    {
        // AABB offset from origin
        var origin = new Vector3(0, 0, 0);
        var direction = new Vector3(1, 0, 0);
        var aabb = new Aabb(new Vector3(10, -1, -1), new Vector3(2, 2, 2)); // AABB at x=10 to x=12

        var result = RayMath.RayIntersectsAabb(origin, direction, aabb, out var distance);

        Assert.True(result);
        Assert.Equal(10f, distance, Epsilon);
    }
}
