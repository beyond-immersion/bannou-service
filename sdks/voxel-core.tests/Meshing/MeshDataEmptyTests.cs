using BeyondImmersion.Bannou.VoxelCore.Meshing;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelCore.Tests.Meshing;

/// <summary>
/// Tests for <see cref="MeshData.Empty"/> — verifies that nullable fields are null
/// (not empty arrays) per T26: nullable types represent absence, not sentinel values.
/// </summary>
public class MeshDataEmptyTests
{
    [Fact]
    public void Empty_UVs_AreNull()
    {
        Assert.Null(MeshData.Empty.UVs);
    }

    [Fact]
    public void Empty_Colors_AreNull()
    {
        Assert.Null(MeshData.Empty.Colors);
    }

    [Fact]
    public void Empty_AmbientOcclusion_IsNull()
    {
        Assert.Null(MeshData.Empty.AmbientOcclusion);
    }

    [Fact]
    public void Empty_Vertices_IsEmptyArray()
    {
        Assert.NotNull(MeshData.Empty.Vertices);
        Assert.Empty(MeshData.Empty.Vertices);
    }

    [Fact]
    public void Empty_Normals_IsEmptyArray()
    {
        Assert.NotNull(MeshData.Empty.Normals);
        Assert.Empty(MeshData.Empty.Normals);
    }

    [Fact]
    public void Empty_Indices_IsEmptyArray()
    {
        Assert.NotNull(MeshData.Empty.Indices);
        Assert.Empty(MeshData.Empty.Indices);
    }

    [Fact]
    public void Empty_Counts_AreZero()
    {
        Assert.Equal(0, MeshData.Empty.VertexCount);
        Assert.Equal(0, MeshData.Empty.TriangleCount);
    }
}
