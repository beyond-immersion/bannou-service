using BeyondImmersion.Bannou.VoxelCore.Meshing;
using Stride.Graphics;
using Xunit;
using StrideColor = Stride.Core.Mathematics.Color;
using StrideVec3 = Stride.Core.Mathematics.Vector3;

namespace BeyondImmersion.Bannou.VoxelBuilder.Stride.Tests;

/// <summary>
/// Unit tests for <see cref="StrideChunkRenderer"/> interleaving and index conversion.
/// Tests the internal static methods that convert MeshData → Stride vertex/index arrays.
/// GPU-dependent methods (Create, Update, Dispose) are not tested here.
/// </summary>
public class StrideChunkRendererTests
{
    #region Helper Factories

    /// <summary>
    /// Creates a MeshData with specified vertex count, with simple sequential values.
    /// Each vertex i has position (i, i+0.5, i+1), normal (0, 1, 0), color (i*10, i*20, i*30, 255).
    /// </summary>
    private static MeshData CreateMeshData(
        int vertexCount,
        int[]? indices = null,
        byte[]? colors = null,
        float[]? ao = null,
        bool includeColors = true)
    {
        var vertices = new float[vertexCount * 3];
        var normals = new float[vertexCount * 3];
        for (var i = 0; i < vertexCount; i++)
        {
            vertices[i * 3] = i;
            vertices[i * 3 + 1] = i + 0.5f;
            vertices[i * 3 + 2] = i + 1f;
            normals[i * 3] = 0f;
            normals[i * 3 + 1] = 1f;
            normals[i * 3 + 2] = 0f;
        }

        byte[]? meshColors = null;
        if (includeColors)
        {
            meshColors = colors ?? new byte[vertexCount * 4];
            if (colors == null)
            {
                for (var i = 0; i < vertexCount; i++)
                {
                    meshColors[i * 4] = (byte)(i * 10 % 256);
                    meshColors[i * 4 + 1] = (byte)(i * 20 % 256);
                    meshColors[i * 4 + 2] = (byte)(i * 30 % 256);
                    meshColors[i * 4 + 3] = 255;
                }
            }
        }

        var meshIndices = indices ?? Enumerable.Range(0, vertexCount).ToArray();
        var triangleCount = meshIndices.Length / 3;

        return new MeshData(vertices, normals, null, meshIndices, meshColors, ao, vertexCount, triangleCount);
    }

    /// <summary>Creates a single-triangle MeshData for minimal testing.</summary>
    private static MeshData CreateTriangleMeshData(
        byte r = 255, byte g = 0, byte b = 0, byte a = 255,
        float[]? ao = null)
    {
        var vertices = new float[]
        {
            0f, 0f, 0f,  // v0
            1f, 0f, 0f,  // v1
            0f, 1f, 0f   // v2
        };
        var normals = new float[]
        {
            0f, 0f, 1f,
            0f, 0f, 1f,
            0f, 0f, 1f
        };
        var colors = new byte[]
        {
            r, g, b, a,
            r, g, b, a,
            r, g, b, a
        };
        var indices = new int[] { 0, 1, 2 };

        return new MeshData(vertices, normals, null, indices, colors, ao, 3, 1);
    }

    #endregion

    #region InterleaveVertices — Basic Structure

    [Fact]
    public void InterleaveVertices_EmptyMeshData_ReturnsEmptyArray()
    {
        var meshData = MeshData.Empty;
        var result = StrideChunkRenderer.InterleaveVertices(meshData);

        Assert.Empty(result);
    }

    [Fact]
    public void InterleaveVertices_SingleVertex_ReturnsOneElement()
    {
        var meshData = CreateMeshData(1);
        var result = StrideChunkRenderer.InterleaveVertices(meshData);

        Assert.Single(result);
    }

    [Fact]
    public void InterleaveVertices_MultipleVertices_CorrectCount()
    {
        var meshData = CreateMeshData(100);
        var result = StrideChunkRenderer.InterleaveVertices(meshData);

        Assert.Equal(100, result.Length);
    }

    #endregion

    #region InterleaveVertices — Position Mapping

    [Fact]
    public void InterleaveVertices_PositionsAreCorrectlyMapped()
    {
        var vertices = new float[] { 1f, 2f, 3f, 4f, 5f, 6f };
        var normals = new float[] { 0f, 1f, 0f, 0f, 1f, 0f };
        var colors = new byte[] { 255, 255, 255, 255, 255, 255, 255, 255 };
        var meshData = new MeshData(vertices, normals, null, new[] { 0, 1 }, colors, null, 2, 0);

        var result = StrideChunkRenderer.InterleaveVertices(meshData);

        Assert.Equal(new StrideVec3(1f, 2f, 3f), result[0].Position);
        Assert.Equal(new StrideVec3(4f, 5f, 6f), result[1].Position);
    }

    [Fact]
    public void InterleaveVertices_NegativePositions()
    {
        var vertices = new float[] { -1f, -2.5f, -3f };
        var normals = new float[] { 0f, 0f, 1f };
        var colors = new byte[] { 255, 255, 255, 255 };
        var meshData = new MeshData(vertices, normals, null, new[] { 0 }, colors, null, 1, 0);

        var result = StrideChunkRenderer.InterleaveVertices(meshData);

        Assert.Equal(-1f, result[0].Position.X);
        Assert.Equal(-2.5f, result[0].Position.Y);
        Assert.Equal(-3f, result[0].Position.Z);
    }

    #endregion

    #region InterleaveVertices — Normal Mapping

    [Fact]
    public void InterleaveVertices_NormalsAreCorrectlyMapped()
    {
        var vertices = new float[] { 0f, 0f, 0f };
        var normals = new float[] { 0f, 1f, 0f };
        var colors = new byte[] { 255, 255, 255, 255 };
        var meshData = new MeshData(vertices, normals, null, new[] { 0 }, colors, null, 1, 0);

        var result = StrideChunkRenderer.InterleaveVertices(meshData);

        Assert.Equal(new StrideVec3(0f, 1f, 0f), result[0].Normal);
    }

    [Fact]
    public void InterleaveVertices_DiagonalNormal()
    {
        var n = 0.577350f; // ~1/√3
        var vertices = new float[] { 0f, 0f, 0f };
        var normals = new float[] { n, n, n };
        var colors = new byte[] { 255, 255, 255, 255 };
        var meshData = new MeshData(vertices, normals, null, new[] { 0 }, colors, null, 1, 0);

        var result = StrideChunkRenderer.InterleaveVertices(meshData);

        Assert.Equal(n, result[0].Normal.X, 5);
        Assert.Equal(n, result[0].Normal.Y, 5);
        Assert.Equal(n, result[0].Normal.Z, 5);
    }

    #endregion

    #region InterleaveVertices — Color Mapping

    [Fact]
    public void InterleaveVertices_ColorsAreDirectlyMapped()
    {
        var meshData = CreateTriangleMeshData(r: 100, g: 150, b: 200, a: 128);
        var result = StrideChunkRenderer.InterleaveVertices(meshData);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(100, result[i].Color.R);
            Assert.Equal(150, result[i].Color.G);
            Assert.Equal(200, result[i].Color.B);
            Assert.Equal(128, result[i].Color.A);
        }
    }

    [Fact]
    public void InterleaveVertices_NullColors_DefaultsToWhite()
    {
        var vertices = new float[] { 0f, 0f, 0f };
        var normals = new float[] { 0f, 1f, 0f };
        var meshData = new MeshData(vertices, normals, null, new[] { 0 }, null, null, 1, 0);

        var result = StrideChunkRenderer.InterleaveVertices(meshData);

        Assert.Equal(255, result[0].Color.R);
        Assert.Equal(255, result[0].Color.G);
        Assert.Equal(255, result[0].Color.B);
        Assert.Equal(255, result[0].Color.A);
    }

    [Fact]
    public void InterleaveVertices_PerVertexDistinctColors()
    {
        var vertices = new float[] { 0f, 0f, 0f, 1f, 0f, 0f };
        var normals = new float[] { 0f, 1f, 0f, 0f, 1f, 0f };
        var colors = new byte[]
        {
            10, 20, 30, 255,   // vertex 0
            200, 150, 100, 128  // vertex 1
        };
        var meshData = new MeshData(vertices, normals, null, new[] { 0, 1 }, colors, null, 2, 0);

        var result = StrideChunkRenderer.InterleaveVertices(meshData);

        Assert.Equal(10, result[0].Color.R);
        Assert.Equal(20, result[0].Color.G);
        Assert.Equal(30, result[0].Color.B);
        Assert.Equal(255, result[0].Color.A);

        Assert.Equal(200, result[1].Color.R);
        Assert.Equal(150, result[1].Color.G);
        Assert.Equal(100, result[1].Color.B);
        Assert.Equal(128, result[1].Color.A);
    }

    #endregion

    #region InterleaveVertices — AO Baking

    [Fact]
    public void InterleaveVertices_AO_FullOcclusion_BlackensColor()
    {
        var ao = new float[] { 0f, 0f, 0f };
        var meshData = CreateTriangleMeshData(r: 200, g: 100, b: 50, ao: ao);
        var result = StrideChunkRenderer.InterleaveVertices(meshData);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(0, result[i].Color.R);
            Assert.Equal(0, result[i].Color.G);
            Assert.Equal(0, result[i].Color.B);
            Assert.Equal(255, result[i].Color.A); // Alpha is NOT affected by AO
        }
    }

    [Fact]
    public void InterleaveVertices_AO_NoOcclusion_PreservesColor()
    {
        var ao = new float[] { 1f, 1f, 1f };
        var meshData = CreateTriangleMeshData(r: 200, g: 100, b: 50, ao: ao);
        var result = StrideChunkRenderer.InterleaveVertices(meshData);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(200, result[i].Color.R);
            Assert.Equal(100, result[i].Color.G);
            Assert.Equal(50, result[i].Color.B);
        }
    }

    [Fact]
    public void InterleaveVertices_AO_HalfOcclusion_HalvesColor()
    {
        var ao = new float[] { 0.5f, 0.5f, 0.5f };
        var meshData = CreateTriangleMeshData(r: 200, g: 100, b: 50, ao: ao);
        var result = StrideChunkRenderer.InterleaveVertices(meshData);

        // byte cast truncates: (byte)(200 * 0.5) = 100, (byte)(100 * 0.5) = 50, (byte)(50 * 0.5) = 25
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(100, result[i].Color.R);
            Assert.Equal(50, result[i].Color.G);
            Assert.Equal(25, result[i].Color.B);
        }
    }

    [Fact]
    public void InterleaveVertices_AO_AlphaNotAffected()
    {
        var ao = new float[] { 0.0f, 0.5f, 1.0f };
        var meshData = CreateTriangleMeshData(a: 128, ao: ao);
        var result = StrideChunkRenderer.InterleaveVertices(meshData);

        // Alpha should remain 128 for all vertices regardless of AO
        Assert.Equal(128, result[0].Color.A);
        Assert.Equal(128, result[1].Color.A);
        Assert.Equal(128, result[2].Color.A);
    }

    [Fact]
    public void InterleaveVertices_AO_PerVertexVariation()
    {
        // Three vertices with different AO values
        var vertices = new float[] { 0f, 0f, 0f, 1f, 0f, 0f, 2f, 0f, 0f };
        var normals = new float[] { 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f, 0f };
        var colors = new byte[]
        {
            100, 100, 100, 255,
            100, 100, 100, 255,
            100, 100, 100, 255
        };
        var ao = new float[] { 1.0f, 0.5f, 0.0f };
        var meshData = new MeshData(vertices, normals, null, new[] { 0, 1, 2 }, colors, ao, 3, 1);

        var result = StrideChunkRenderer.InterleaveVertices(meshData);

        Assert.Equal(100, result[0].Color.R); // 100 * 1.0
        Assert.Equal(50, result[1].Color.R);  // 100 * 0.5
        Assert.Equal(0, result[2].Color.R);   // 100 * 0.0
    }

    [Fact]
    public void InterleaveVertices_NullAO_NoModification()
    {
        var meshData = CreateTriangleMeshData(r: 200, g: 100, b: 50, ao: null);
        var result = StrideChunkRenderer.InterleaveVertices(meshData);

        // Without AO, colors should pass through unmodified
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(200, result[i].Color.R);
            Assert.Equal(100, result[i].Color.G);
            Assert.Equal(50, result[i].Color.B);
        }
    }

    [Fact]
    public void InterleaveVertices_NullColorsWithAO_AoAppliedToWhite()
    {
        var vertices = new float[] { 0f, 0f, 0f };
        var normals = new float[] { 0f, 1f, 0f };
        var ao = new float[] { 0.5f };
        var meshData = new MeshData(vertices, normals, null, new[] { 0 }, null, ao, 1, 0);

        var result = StrideChunkRenderer.InterleaveVertices(meshData);

        // Default white (255) * 0.5 = 127
        Assert.Equal(127, result[0].Color.R);
        Assert.Equal(127, result[0].Color.G);
        Assert.Equal(127, result[0].Color.B);
        Assert.Equal(255, result[0].Color.A);
    }

    #endregion

    #region InterleaveVertices — Combined Fidelity

    [Fact]
    public void InterleaveVertices_AllFieldsCorrectlyInterleaved()
    {
        // Validate all three fields are from the correct array locations
        var vertices = new float[] { 10f, 20f, 30f };
        var normals = new float[] { 0.1f, 0.2f, 0.3f };
        var colors = new byte[] { 11, 22, 33, 44 };
        var meshData = new MeshData(vertices, normals, null, new[] { 0 }, colors, null, 1, 0);

        var result = StrideChunkRenderer.InterleaveVertices(meshData);

        Assert.Equal(new StrideVec3(10f, 20f, 30f), result[0].Position);
        Assert.Equal(new StrideVec3(0.1f, 0.2f, 0.3f), result[0].Normal);
        Assert.Equal(new StrideColor(11, 22, 33, 44), result[0].Color);
    }

    [Fact]
    public void InterleaveVertices_LargeVertexCount_AllInterleaved()
    {
        // Stress test with a meaningful count
        const int count = 5000;
        var meshData = CreateMeshData(count);
        var result = StrideChunkRenderer.InterleaveVertices(meshData);

        Assert.Equal(count, result.Length);

        // Spot-check first, middle, and last
        Assert.Equal(0f, result[0].Position.X);
        Assert.Equal(2500f, result[2500].Position.X);
        Assert.Equal(4999f, result[4999].Position.X);
    }

    #endregion

    #region ConvertIndices

    [Fact]
    public void ConvertIndices_EmptyMeshData_ReturnsEmptyArray()
    {
        var meshData = MeshData.Empty;
        var result = StrideChunkRenderer.ConvertIndices(meshData);

        Assert.Empty(result);
    }

    [Fact]
    public void ConvertIndices_SingleTriangle()
    {
        var meshData = CreateTriangleMeshData();
        var result = StrideChunkRenderer.ConvertIndices(meshData);

        Assert.Equal(3, result.Length);
        Assert.Equal(0u, result[0]);
        Assert.Equal(1u, result[1]);
        Assert.Equal(2u, result[2]);
    }

    [Fact]
    public void ConvertIndices_PreservesValues()
    {
        var indices = new int[] { 0, 1, 2, 2, 3, 0, 4, 5, 6 };
        var meshData = CreateMeshData(7, indices: indices);
        var result = StrideChunkRenderer.ConvertIndices(meshData);

        Assert.Equal(9, result.Length);
        Assert.Equal(0u, result[0]);
        Assert.Equal(1u, result[1]);
        Assert.Equal(2u, result[2]);
        Assert.Equal(2u, result[3]);
        Assert.Equal(3u, result[4]);
        Assert.Equal(0u, result[5]);
        Assert.Equal(4u, result[6]);
        Assert.Equal(5u, result[7]);
        Assert.Equal(6u, result[8]);
    }

    [Fact]
    public void ConvertIndices_LargeIndices()
    {
        // Indices that approach the int→uint boundary
        var indices = new int[] { 0, 65535, 100000 };
        var vertices = new float[100001 * 3];
        var normals = new float[100001 * 3];
        var meshData = new MeshData(vertices, normals, null, indices, null, null, 100001, 1);

        var result = StrideChunkRenderer.ConvertIndices(meshData);

        Assert.Equal(0u, result[0]);
        Assert.Equal(65535u, result[1]);
        Assert.Equal(100000u, result[2]);
    }

    [Fact]
    public void ConvertIndices_ManyTriangles()
    {
        // 500 triangles = 1500 indices
        var indices = new int[1500];
        for (var i = 0; i < 1500; i++)
            indices[i] = i % 100;

        var meshData = CreateMeshData(100, indices: indices);
        var result = StrideChunkRenderer.ConvertIndices(meshData);

        Assert.Equal(1500, result.Length);
        for (var i = 0; i < 1500; i++)
            Assert.Equal((uint)(i % 100), result[i]);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void InterleaveVertices_CollisionModeMesh_NullColors_NullAO()
    {
        // CollisionMode MeshData has null Colors and null AO
        var vertices = new float[] { 0f, 0f, 0f, 1f, 1f, 1f };
        var normals = new float[] { 0f, 1f, 0f, 0f, 1f, 0f };
        var meshData = new MeshData(vertices, normals, null, new[] { 0, 1 }, null, null, 2, 0);

        var result = StrideChunkRenderer.InterleaveVertices(meshData);

        // Should default to white, no AO modification
        Assert.Equal(255, result[0].Color.R);
        Assert.Equal(255, result[1].Color.R);
    }

    [Fact]
    public void InterleaveVertices_ZeroByteColor_WithAO_StaysZero()
    {
        // Edge case: black vertex color multiplied by any AO stays black
        var meshData = CreateTriangleMeshData(r: 0, g: 0, b: 0, ao: new float[] { 0.75f, 0.75f, 0.75f });
        var result = StrideChunkRenderer.InterleaveVertices(meshData);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(0, result[i].Color.R);
            Assert.Equal(0, result[i].Color.G);
            Assert.Equal(0, result[i].Color.B);
        }
    }

    [Fact]
    public void InterleaveVertices_MaxByteColor_WithHighAO_ClipsCorrectly()
    {
        // 255 * 0.99 = 252.45 → truncates to 252
        var ao = new float[] { 0.99f, 0.99f, 0.99f };
        var meshData = CreateTriangleMeshData(r: 255, g: 255, b: 255, ao: ao);
        var result = StrideChunkRenderer.InterleaveVertices(meshData);

        // (byte)(255 * 0.99) = (byte)252.45 = 252
        Assert.Equal(252, result[0].Color.R);
        Assert.Equal(252, result[0].Color.G);
        Assert.Equal(252, result[0].Color.B);
    }

    #endregion
}
