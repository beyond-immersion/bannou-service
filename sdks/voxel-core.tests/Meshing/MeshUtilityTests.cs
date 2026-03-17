using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using BeyondImmersion.Bannou.VoxelCore.Meshing;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelCore.Tests.Meshing;

/// <summary>
/// Unit tests for <see cref="MeshUtility"/> — winding flip and mesh merging.
/// </summary>
public class MeshUtilityTests
{
    [Fact]
    public void FlipWindingOrder_SwapsBAndC()
    {
        // Triangle (0, 1, 2) → (0, 2, 1)
        var mesh = new MeshData(
            new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 },
            new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1 },
            null,
            new[] { 0, 1, 2 },
            null, null, 3, 1);

        var flipped = MeshUtility.FlipWindingOrder(mesh);

        Assert.Equal(0, flipped.Indices[0]);
        Assert.Equal(2, flipped.Indices[1]);
        Assert.Equal(1, flipped.Indices[2]);
    }

    [Fact]
    public void FlipWindingOrder_NegatesNormals()
    {
        var mesh = new MeshData(
            new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 },
            new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1 },
            null,
            new[] { 0, 1, 2 },
            null, null, 3, 1);

        var flipped = MeshUtility.FlipWindingOrder(mesh);

        for (var i = 0; i < flipped.Normals.Length; i++)
            Assert.Equal(-mesh.Normals[i], flipped.Normals[i]);
    }

    [Fact]
    public void FlipWindingOrder_PreservesVertices()
    {
        var mesh = new MeshData(
            new float[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 },
            new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1 },
            null,
            new[] { 0, 1, 2 },
            null, null, 3, 1);

        var flipped = MeshUtility.FlipWindingOrder(mesh);

        Assert.Equal(mesh.Vertices, flipped.Vertices);
    }

    [Fact]
    public void FlipWindingOrder_PreservesCounts()
    {
        var mesh = new MeshData(
            new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 },
            new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1 },
            null, new[] { 0, 1, 2 }, null, null, 3, 1);

        var flipped = MeshUtility.FlipWindingOrder(mesh);

        Assert.Equal(mesh.VertexCount, flipped.VertexCount);
        Assert.Equal(mesh.TriangleCount, flipped.TriangleCount);
    }

    [Fact]
    public void MergeMeshData_CombinesVertexCounts()
    {
        var mesh1 = new MeshData(
            new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 },
            new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1 },
            null, new[] { 0, 1, 2 }, null, null, 3, 1);

        var mesh2 = new MeshData(
            new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 },
            new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1 },
            null, new[] { 0, 1, 2 }, null, null, 3, 1);

        var merged = MeshUtility.MergeMeshData(new[]
        {
            (mesh1, VoxelCoord.Zero),
            (mesh2, new VoxelCoord(16, 0, 0))
        });

        Assert.Equal(6, merged.VertexCount);
        Assert.Equal(2, merged.TriangleCount);
    }

    [Fact]
    public void MergeMeshData_OffsetsVertexPositions()
    {
        var mesh = new MeshData(
            new float[] { 0, 0, 0 },
            new float[] { 0, 1, 0 },
            null, Array.Empty<int>(), null, null, 1, 0);

        var offset = new VoxelCoord(10, 20, 30);
        var scale = 0.25f;
        var merged = MeshUtility.MergeMeshData(new[] { (mesh, offset) }, scale);

        Assert.Equal(10 * scale, merged.Vertices[0], 0.001f);
        Assert.Equal(20 * scale, merged.Vertices[1], 0.001f);
        Assert.Equal(30 * scale, merged.Vertices[2], 0.001f);
    }

    [Fact]
    public void MergeMeshData_OffsetsIndices()
    {
        var mesh1 = new MeshData(
            new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 },
            new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1 },
            null, new[] { 0, 1, 2 }, null, null, 3, 1);

        var mesh2 = new MeshData(
            new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 },
            new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1 },
            null, new[] { 0, 1, 2 }, null, null, 3, 1);

        var merged = MeshUtility.MergeMeshData(new[]
        {
            (mesh1, VoxelCoord.Zero),
            (mesh2, new VoxelCoord(16, 0, 0))
        });

        // First triangle uses indices 0,1,2; second uses 3,4,5
        Assert.Equal(0, merged.Indices[0]);
        Assert.Equal(3, merged.Indices[3]);
    }
}
