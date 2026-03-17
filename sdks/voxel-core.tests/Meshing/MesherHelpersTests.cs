using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Meshing;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelCore.Tests.Meshing;

/// <summary>
/// Unit tests for internal <see cref="MesherHelpers"/> — face directions, neighbor
/// lookups, and UV computation used by all mesher implementations.
/// </summary>
public class MesherHelpersTests
{
    private static readonly VoxelChunk?[] NoNeighbors = new VoxelChunk?[6];

    #region Face Directions

    [Fact]
    public void FaceDirections_Has6Entries()
    {
        Assert.Equal(6, MesherHelpers.FaceDirections.Length);
    }

    [Fact]
    public void FaceDirections_AllUnitLength()
    {
        foreach (var (dx, dy, dz) in MesherHelpers.FaceDirections)
        {
            var length = System.Math.Abs(dx) + System.Math.Abs(dy) + System.Math.Abs(dz);
            Assert.Equal(1, length);
        }
    }

    [Fact]
    public void FaceDirections_OppositesPairedCorrectly()
    {
        // +X/-X, +Y/-Y, +Z/-Z
        var dirs = MesherHelpers.FaceDirections;
        Assert.Equal((1, 0, 0), dirs[0]);
        Assert.Equal((-1, 0, 0), dirs[1]);
        Assert.Equal((0, 1, 0), dirs[2]);
        Assert.Equal((0, -1, 0), dirs[3]);
        Assert.Equal((0, 0, 1), dirs[4]);
        Assert.Equal((0, 0, -1), dirs[5]);
    }

    [Fact]
    public void FaceNormals_MatchDirections()
    {
        for (var i = 0; i < 6; i++)
        {
            var (dx, dy, dz) = MesherHelpers.FaceDirections[i];
            var (nx, ny, nz) = MesherHelpers.FaceNormals[i];
            Assert.Equal(dx, (int)nx);
            Assert.Equal(dy, (int)ny);
            Assert.Equal(dz, (int)nz);
        }
    }

    #endregion

    #region Face Vertices

    [Fact]
    public void FaceVertices_Has6Faces()
    {
        Assert.Equal(6, MesherHelpers.FaceVertices.Length);
    }

    [Fact]
    public void FaceVertices_EachFaceHas4Vertices()
    {
        foreach (var face in MesherHelpers.FaceVertices)
            Assert.Equal(4, face.Length);
    }

    [Fact]
    public void FaceVertices_AllComponentsIn01Range()
    {
        foreach (var face in MesherHelpers.FaceVertices)
        foreach (var (dx, dy, dz) in face)
        {
            Assert.InRange(dx, 0f, 1f);
            Assert.InRange(dy, 0f, 1f);
            Assert.InRange(dz, 0f, 1f);
        }
    }

    #endregion

    #region IsNeighborEmpty

    [Fact]
    public void IsNeighborEmpty_InsideChunk_EmptyVoxel_ReturnsTrue()
    {
        var chunk = new VoxelChunk();
        Assert.True(MesherHelpers.IsNeighborEmpty(chunk, NoNeighbors, 5, 5, 5));
    }

    [Fact]
    public void IsNeighborEmpty_InsideChunk_SolidVoxel_ReturnsFalse()
    {
        var chunk = new VoxelChunk();
        chunk.SetVoxel(5, 5, 5, new Voxel(1, VoxelFlags.None));
        Assert.False(MesherHelpers.IsNeighborEmpty(chunk, NoNeighbors, 5, 5, 5));
    }

    [Fact]
    public void IsNeighborEmpty_OutsideChunk_NoNeighbor_ReturnsTrue()
    {
        var chunk = new VoxelChunk();
        // x=16 is outside chunk (0-15), neighbor[0] (+X) is null
        Assert.True(MesherHelpers.IsNeighborEmpty(chunk, NoNeighbors, 16, 0, 0));
    }

    [Fact]
    public void IsNeighborEmpty_OutsideChunk_WithSolidNeighbor_ReturnsFalse()
    {
        var chunk = new VoxelChunk();
        var neighbor = new VoxelChunk();
        neighbor.SetVoxel(0, 0, 0, new Voxel(1, VoxelFlags.None));

        var neighbors = new VoxelChunk?[6];
        neighbors[0] = neighbor; // +X neighbor

        // x=16 maps to neighbor's x=0
        Assert.False(MesherHelpers.IsNeighborEmpty(chunk, neighbors, 16, 0, 0));
    }

    [Fact]
    public void IsNeighborEmpty_NegativeX_ChecksMinusXNeighbor()
    {
        var chunk = new VoxelChunk();
        var neighbor = new VoxelChunk();
        neighbor.SetVoxel(15, 0, 0, new Voxel(1, VoxelFlags.None));

        var neighbors = new VoxelChunk?[6];
        neighbors[1] = neighbor; // -X neighbor

        // x=-1 maps to neighbor's x=15
        Assert.False(MesherHelpers.IsNeighborEmpty(chunk, neighbors, -1, 0, 0));
    }

    [Fact]
    public void IsNeighborEmpty_PositiveY_ChecksPlusYNeighbor()
    {
        var chunk = new VoxelChunk();
        var neighbor = new VoxelChunk();
        neighbor.SetVoxel(0, 0, 0, new Voxel(1, VoxelFlags.None));

        var neighbors = new VoxelChunk?[6];
        neighbors[2] = neighbor; // +Y neighbor

        // y=16 maps to neighbor's y=0
        Assert.False(MesherHelpers.IsNeighborEmpty(chunk, neighbors, 0, 16, 0));
    }

    #endregion

    #region IsSolid

    [Fact]
    public void IsSolid_IsInverseOfIsNeighborEmpty()
    {
        var chunk = new VoxelChunk();
        chunk.SetVoxel(3, 3, 3, new Voxel(1, VoxelFlags.None));

        Assert.True(MesherHelpers.IsSolid(chunk, NoNeighbors, 3, 3, 3));
        Assert.False(MesherHelpers.IsSolid(chunk, NoNeighbors, 4, 4, 4));
    }

    #endregion

    #region ComputeUV

    [Fact]
    public void ComputeUV_Index1_TexelCentered()
    {
        var (u, v) = MesherHelpers.ComputeUV(1);
        // Index 1: column 1, row 0 → U = (1 + 0.5) / 16, V = (0 + 0.5) / 16
        Assert.Equal((1 + 0.5f) / 16f, u, 0.0001f);
        Assert.Equal((0 + 0.5f) / 16f, v, 0.0001f);
    }

    [Fact]
    public void ComputeUV_Index0_FirstTexel()
    {
        var (u, v) = MesherHelpers.ComputeUV(0);
        Assert.Equal(0.5f / 16f, u, 0.0001f);
        Assert.Equal(0.5f / 16f, v, 0.0001f);
    }

    [Fact]
    public void ComputeUV_Index255_LastTexel()
    {
        var (u, v) = MesherHelpers.ComputeUV(255);
        // 255 % 16 = 15, 255 / 16 = 15
        Assert.Equal((15 + 0.5f) / 16f, u, 0.0001f);
        Assert.Equal((15 + 0.5f) / 16f, v, 0.0001f);
    }

    [Fact]
    public void ComputeUV_Index16_SecondRow()
    {
        var (u, v) = MesherHelpers.ComputeUV(16);
        // 16 % 16 = 0, 16 / 16 = 1
        Assert.Equal(0.5f / 16f, u, 0.0001f);
        Assert.Equal(1.5f / 16f, v, 0.0001f);
    }

    [Fact]
    public void ComputeUV_AllIndices_InRange0To1()
    {
        for (var i = 0; i < 256; i++)
        {
            var (u, v) = MesherHelpers.ComputeUV((byte)i);
            Assert.InRange(u, 0f, 1f);
            Assert.InRange(v, 0f, 1f);
        }
    }

    #endregion
}
