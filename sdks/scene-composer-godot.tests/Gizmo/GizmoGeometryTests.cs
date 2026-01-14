using BeyondImmersion.Bannou.SceneComposer.Godot.Gizmo;
using Godot;
using Xunit;

namespace BeyondImmersion.Bannou.SceneComposer.Godot.Tests.Gizmo;

/// <summary>
/// Tests for GizmoGeometry procedural mesh generation.
/// </summary>
public class GizmoGeometryTests
{
    private const float Epsilon = 1e-5f;

    // =========================================================================
    // ARROW MESH TESTS
    // =========================================================================

    [Fact]
    public void GenerateArrowVertices_DefaultParams_ReturnsNonEmptyArray()
    {
        var vertices = GizmoGeometry.GenerateArrowVertices();

        Assert.NotNull(vertices);
        Assert.True(vertices.Length > 0);
    }

    [Fact]
    public void GenerateArrowVertices_DefaultParams_ReturnsTriangles()
    {
        var vertices = GizmoGeometry.GenerateArrowVertices();

        // Triangles require vertex count divisible by 3
        Assert.Equal(0, vertices.Length % 3);
    }

    [Fact]
    public void GenerateArrowVertices_ShaftStartsAtOrigin()
    {
        var vertices = GizmoGeometry.GenerateArrowVertices();

        // At least one vertex should be at or very near the origin (shaft base)
        var hasOriginVertex = vertices.Any(v =>
            MathF.Abs(v.X) < Epsilon &&
            MathF.Abs(v.Y) < Epsilon &&
            MathF.Abs(v.Z) < Epsilon);

        Assert.True(hasOriginVertex, "Arrow should have vertices at origin for shaft base");
    }

    [Fact]
    public void GenerateArrowVertices_HeadExtendsToCorrectLength()
    {
        const float shaftLength = 0.8f;
        const float headLength = 0.2f;
        var expectedTipZ = shaftLength + headLength;

        var vertices = GizmoGeometry.GenerateArrowVertices(
            shaftLength: shaftLength,
            headLength: headLength);

        // The arrow tip should be at shaftLength + headLength along Z
        var maxZ = vertices.Max(v => v.Z);
        Assert.Equal(expectedTipZ, maxZ, Epsilon);
    }

    [Fact]
    public void GenerateArrowVertices_CustomSegments_ProducesMoreVertices()
    {
        var vertices8 = GizmoGeometry.GenerateArrowVertices(segments: 8);
        var vertices16 = GizmoGeometry.GenerateArrowVertices(segments: 16);

        Assert.True(vertices16.Length > vertices8.Length,
            "More segments should produce more vertices");
    }

    [Fact]
    public void GenerateArrowVertices_CustomDimensions_RespectsShaftRadius()
    {
        const float shaftRadius = 0.1f;
        var vertices = GizmoGeometry.GenerateArrowVertices(shaftRadius: shaftRadius);

        // Check that some vertices are at the expected radius in XY plane
        var shaftVertices = vertices.Where(v => MathF.Abs(v.Z) < 0.01f); // Near base
        var maxRadius = shaftVertices.Max(v => MathF.Sqrt(v.X * v.X + v.Y * v.Y));

        Assert.Equal(shaftRadius, maxRadius, 0.001f);
    }

    // =========================================================================
    // RING MESH TESTS
    // =========================================================================

    [Fact]
    public void GenerateRingVertices_DefaultParams_ReturnsNonEmptyArray()
    {
        var vertices = GizmoGeometry.GenerateRingVertices();

        Assert.NotNull(vertices);
        Assert.True(vertices.Length > 0);
    }

    [Fact]
    public void GenerateRingVertices_DefaultParams_ReturnsTriangles()
    {
        var vertices = GizmoGeometry.GenerateRingVertices();

        Assert.Equal(0, vertices.Length % 3);
    }

    [Fact]
    public void GenerateRingVertices_FormsTorus()
    {
        const float majorRadius = 0.8f;
        var vertices = GizmoGeometry.GenerateRingVertices(majorRadius: majorRadius);

        // Ring vertices should be distributed around the major radius
        // Check that vertices exist at approximately majorRadius distance from origin in XY
        var distancesFromOriginXY = vertices
            .Select(v => MathF.Sqrt(v.X * v.X + v.Y * v.Y))
            .ToArray();

        var avgDistance = distancesFromOriginXY.Average();

        // Average distance should be close to major radius
        Assert.True(MathF.Abs(avgDistance - majorRadius) < 0.1f,
            $"Average XY distance {avgDistance} should be close to major radius {majorRadius}");
    }

    [Fact]
    public void GenerateRingVertices_ZValuesWithinMinorRadius()
    {
        const float minorRadius = 0.02f;
        var vertices = GizmoGeometry.GenerateRingVertices(minorRadius: minorRadius);

        // Z values should be within minor radius since ring is in XY plane
        var maxAbsZ = vertices.Max(v => MathF.Abs(v.Z));

        Assert.True(maxAbsZ <= minorRadius + Epsilon,
            $"Max Z {maxAbsZ} should be within minor radius {minorRadius}");
    }

    [Fact]
    public void GenerateRingVertices_MoreSegments_ProducesMoreVertices()
    {
        var vertices16 = GizmoGeometry.GenerateRingVertices(majorSegments: 16, minorSegments: 4);
        var vertices32 = GizmoGeometry.GenerateRingVertices(majorSegments: 32, minorSegments: 8);

        Assert.True(vertices32.Length > vertices16.Length);
    }

    // =========================================================================
    // SCALE CUBE TESTS
    // =========================================================================

    [Fact]
    public void GenerateScaleCubeVertices_DefaultParams_ReturnsNonEmptyArray()
    {
        var vertices = GizmoGeometry.GenerateScaleCubeVertices();

        Assert.NotNull(vertices);
        Assert.True(vertices.Length > 0);
    }

    [Fact]
    public void GenerateScaleCubeVertices_DefaultParams_ReturnsTriangles()
    {
        var vertices = GizmoGeometry.GenerateScaleCubeVertices();

        Assert.Equal(0, vertices.Length % 3);
    }

    [Fact]
    public void GenerateScaleCubeVertices_Has36Vertices()
    {
        // A cube has 6 faces, each with 2 triangles (6 vertices per face)
        // 6 faces * 6 vertices = 36 total vertices
        var vertices = GizmoGeometry.GenerateScaleCubeVertices();

        Assert.Equal(36, vertices.Length);
    }

    [Fact]
    public void GenerateScaleCubeVertices_CenteredAtOffset()
    {
        const float offsetZ = 0.9f;
        var vertices = GizmoGeometry.GenerateScaleCubeVertices(offsetZ: offsetZ);

        var avgZ = vertices.Average(v => v.Z);

        Assert.Equal(offsetZ, avgZ, Epsilon);
    }

    [Fact]
    public void GenerateScaleCubeVertices_HasCorrectSize()
    {
        const float size = 0.1f;
        const float halfSize = size / 2;
        var vertices = GizmoGeometry.GenerateScaleCubeVertices(size: size);

        // Get center Z (average)
        var centerZ = vertices.Average(v => v.Z);

        // X and Y should be within half size from center (which is 0,0 for X,Y)
        var maxAbsX = vertices.Max(v => MathF.Abs(v.X));
        var maxAbsY = vertices.Max(v => MathF.Abs(v.Y));

        Assert.Equal(halfSize, maxAbsX, Epsilon);
        Assert.Equal(halfSize, maxAbsY, Epsilon);
    }

    // =========================================================================
    // TRANSFORM TO AXIS TESTS
    // =========================================================================

    [Fact]
    public void TransformToAxis_Z_ReturnsUnchanged()
    {
        var input = new Vector3[] { new(1, 2, 3), new(4, 5, 6) };

        var result = GizmoGeometry.TransformToAxis(input, GizmoAxisDirection.Z);

        Assert.Equal(2, result.Length);
        Assert.Equal(input[0], result[0]);
        Assert.Equal(input[1], result[1]);
    }

    [Fact]
    public void TransformToAxis_X_RotatesZToX()
    {
        // A point at (0, 0, 1) should end up at (1, 0, 0) for X axis
        var input = new Vector3[] { new(0, 0, 1) };

        var result = GizmoGeometry.TransformToAxis(input, GizmoAxisDirection.X);

        Assert.Equal(1f, result[0].X, Epsilon);
        Assert.Equal(0f, result[0].Y, Epsilon);
        Assert.Equal(0f, result[0].Z, Epsilon);
    }

    [Fact]
    public void TransformToAxis_Y_RotatesZToY()
    {
        // A point at (0, 0, 1) should end up at (0, 1, 0) for Y axis
        var input = new Vector3[] { new(0, 0, 1) };

        var result = GizmoGeometry.TransformToAxis(input, GizmoAxisDirection.Y);

        Assert.Equal(0f, result[0].X, Epsilon);
        Assert.Equal(1f, result[0].Y, Epsilon);
        Assert.Equal(0f, result[0].Z, Epsilon);
    }

    [Fact]
    public void TransformToAxis_PreservesArrayLength()
    {
        var input = new Vector3[] { new(1, 2, 3), new(4, 5, 6), new(7, 8, 9) };

        var resultX = GizmoGeometry.TransformToAxis(input, GizmoAxisDirection.X);
        var resultY = GizmoGeometry.TransformToAxis(input, GizmoAxisDirection.Y);
        var resultZ = GizmoGeometry.TransformToAxis(input, GizmoAxisDirection.Z);

        Assert.Equal(input.Length, resultX.Length);
        Assert.Equal(input.Length, resultY.Length);
        Assert.Equal(input.Length, resultZ.Length);
    }

    [Fact]
    public void TransformToAxis_DoesNotModifyOriginal()
    {
        var input = new Vector3[] { new(1, 2, 3) };
        var originalX = input[0].X;
        var originalY = input[0].Y;
        var originalZ = input[0].Z;

        GizmoGeometry.TransformToAxis(input, GizmoAxisDirection.X);

        Assert.Equal(originalX, input[0].X);
        Assert.Equal(originalY, input[0].Y);
        Assert.Equal(originalZ, input[0].Z);
    }

    // =========================================================================
    // SCALE VERTICES TESTS
    // =========================================================================

    [Fact]
    public void ScaleVertices_ScalesAllComponents()
    {
        var input = new Vector3[] { new(1, 2, 3) };
        const float scale = 2.0f;

        var result = GizmoGeometry.ScaleVertices(input, scale);

        Assert.Equal(2f, result[0].X, Epsilon);
        Assert.Equal(4f, result[0].Y, Epsilon);
        Assert.Equal(6f, result[0].Z, Epsilon);
    }

    [Fact]
    public void ScaleVertices_ZeroScale_ReturnsOrigin()
    {
        var input = new Vector3[] { new(5, 10, 15) };

        var result = GizmoGeometry.ScaleVertices(input, 0f);

        Assert.Equal(0f, result[0].X, Epsilon);
        Assert.Equal(0f, result[0].Y, Epsilon);
        Assert.Equal(0f, result[0].Z, Epsilon);
    }

    [Fact]
    public void ScaleVertices_NegativeScale_Inverts()
    {
        var input = new Vector3[] { new(1, 2, 3) };

        var result = GizmoGeometry.ScaleVertices(input, -1f);

        Assert.Equal(-1f, result[0].X, Epsilon);
        Assert.Equal(-2f, result[0].Y, Epsilon);
        Assert.Equal(-3f, result[0].Z, Epsilon);
    }

    [Fact]
    public void ScaleVertices_PreservesArrayLength()
    {
        var input = new Vector3[] { new(1, 2, 3), new(4, 5, 6) };

        var result = GizmoGeometry.ScaleVertices(input, 2.0f);

        Assert.Equal(input.Length, result.Length);
    }

    // =========================================================================
    // TRANSLATE VERTICES TESTS
    // =========================================================================

    [Fact]
    public void TranslateVertices_AddsOffset()
    {
        var input = new Vector3[] { new(1, 2, 3) };
        var offset = new Vector3(10, 20, 30);

        var result = GizmoGeometry.TranslateVertices(input, offset);

        Assert.Equal(11f, result[0].X, Epsilon);
        Assert.Equal(22f, result[0].Y, Epsilon);
        Assert.Equal(33f, result[0].Z, Epsilon);
    }

    [Fact]
    public void TranslateVertices_ZeroOffset_ReturnsEqual()
    {
        var input = new Vector3[] { new(5, 10, 15) };

        var result = GizmoGeometry.TranslateVertices(input, Vector3.Zero);

        Assert.Equal(input[0].X, result[0].X, Epsilon);
        Assert.Equal(input[0].Y, result[0].Y, Epsilon);
        Assert.Equal(input[0].Z, result[0].Z, Epsilon);
    }

    [Fact]
    public void TranslateVertices_NegativeOffset_Subtracts()
    {
        var input = new Vector3[] { new(10, 10, 10) };
        var offset = new Vector3(-5, -5, -5);

        var result = GizmoGeometry.TranslateVertices(input, offset);

        Assert.Equal(5f, result[0].X, Epsilon);
        Assert.Equal(5f, result[0].Y, Epsilon);
        Assert.Equal(5f, result[0].Z, Epsilon);
    }

    [Fact]
    public void TranslateVertices_PreservesArrayLength()
    {
        var input = new Vector3[] { new(1, 2, 3), new(4, 5, 6), new(7, 8, 9) };
        var offset = new Vector3(1, 1, 1);

        var result = GizmoGeometry.TranslateVertices(input, offset);

        Assert.Equal(input.Length, result.Length);
    }

    [Fact]
    public void TranslateVertices_DoesNotModifyOriginal()
    {
        var input = new Vector3[] { new(1, 2, 3) };
        var originalX = input[0].X;
        var offset = new Vector3(100, 100, 100);

        GizmoGeometry.TranslateVertices(input, offset);

        Assert.Equal(originalX, input[0].X);
    }

    // =========================================================================
    // CYLINDER GENERATION TESTS
    // =========================================================================

    [Fact]
    public void GenerateCylinderVertices_ProducesTriangles()
    {
        var vertices = new List<Vector3>();
        GizmoGeometry.GenerateCylinderVertices(vertices, 0.1f, 1.0f, 8);

        Assert.Equal(0, vertices.Count % 3);
    }

    [Fact]
    public void GenerateCylinderVertices_HasCorrectLength()
    {
        const float length = 1.0f;
        var vertices = new List<Vector3>();
        GizmoGeometry.GenerateCylinderVertices(vertices, 0.1f, length, 8);

        var maxZ = vertices.Max(v => v.Z);
        var minZ = vertices.Min(v => v.Z);

        Assert.Equal(length, maxZ, Epsilon);
        Assert.Equal(0f, minZ, Epsilon);
    }

    [Fact]
    public void GenerateCylinderVertices_HasCorrectRadius()
    {
        const float radius = 0.15f;
        var vertices = new List<Vector3>();
        GizmoGeometry.GenerateCylinderVertices(vertices, radius, 1.0f, 12);

        var maxRadius = vertices.Max(v => MathF.Sqrt(v.X * v.X + v.Y * v.Y));

        Assert.Equal(radius, maxRadius, 0.001f);
    }

    // =========================================================================
    // CONE GENERATION TESTS
    // =========================================================================

    [Fact]
    public void GenerateConeVertices_ProducesTriangles()
    {
        var vertices = new List<Vector3>();
        GizmoGeometry.GenerateConeVertices(vertices, 0.1f, 0.2f, 0.8f, 8);

        Assert.Equal(0, vertices.Count % 3);
    }

    [Fact]
    public void GenerateConeVertices_ApexAtCorrectHeight()
    {
        const float baseZ = 0.8f;
        const float height = 0.2f;
        var vertices = new List<Vector3>();
        GizmoGeometry.GenerateConeVertices(vertices, 0.1f, height, baseZ, 8);

        var maxZ = vertices.Max(v => v.Z);

        Assert.Equal(baseZ + height, maxZ, Epsilon);
    }

    [Fact]
    public void GenerateConeVertices_BaseAtCorrectZ()
    {
        const float baseZ = 0.5f;
        var vertices = new List<Vector3>();
        GizmoGeometry.GenerateConeVertices(vertices, 0.1f, 0.2f, baseZ, 8);

        var minZ = vertices.Min(v => v.Z);

        Assert.Equal(baseZ, minZ, Epsilon);
    }
}
