using BeyondImmersion.Bannou.VoxelCore.Tables;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelCore.Tests.Meshing;

/// <summary>
/// Unit tests for the internal <see cref="MarchingCubesTables"/> — validates the
/// precomputed lookup tables for structural correctness.
/// </summary>
public class MarchingCubesTablesTests
{
    [Fact]
    public void EdgeTable_Has256Entries()
    {
        Assert.Equal(256, MarchingCubesTables.EdgeTable.Length);
    }

    [Fact]
    public void TriTable_Has256Entries()
    {
        Assert.Equal(256, MarchingCubesTables.TriTable.Length);
    }

    [Fact]
    public void EdgeTable_Index0_IsZero()
    {
        // All corners outside — no edges intersected
        Assert.Equal(0, MarchingCubesTables.EdgeTable[0]);
    }

    [Fact]
    public void EdgeTable_Index255_IsZero()
    {
        // All corners inside — no edges intersected
        Assert.Equal(0, MarchingCubesTables.EdgeTable[255]);
    }

    [Fact]
    public void TriTable_Index0_IsEmpty()
    {
        Assert.Empty(MarchingCubesTables.TriTable[0]);
    }

    [Fact]
    public void TriTable_Index255_IsEmpty()
    {
        Assert.Empty(MarchingCubesTables.TriTable[255]);
    }

    [Fact]
    public void TriTable_AllEntries_HaveTripleIndices()
    {
        // Every non-empty entry should have a triangle count divisible by 3
        for (var i = 0; i < 256; i++)
        {
            var entry = MarchingCubesTables.TriTable[i];
            Assert.True(entry.Length % 3 == 0,
                $"TriTable[{i}] has {entry.Length} entries (not divisible by 3)");
        }
    }

    [Fact]
    public void TriTable_AllIndices_InRange0To11()
    {
        // Edge indices must be 0-11 (12 edges on a cube)
        for (var i = 0; i < 256; i++)
        {
            foreach (var idx in MarchingCubesTables.TriTable[i])
            {
                Assert.InRange(idx, 0, 11);
            }
        }
    }

    [Fact]
    public void EdgeTable_AllValues_AreValidBitmasks()
    {
        // Edge table values are 12-bit masks (bits 0-11)
        for (var i = 0; i < 256; i++)
        {
            Assert.True(MarchingCubesTables.EdgeTable[i] >= 0);
            Assert.True(MarchingCubesTables.EdgeTable[i] < 4096, // 2^12
                $"EdgeTable[{i}] = {MarchingCubesTables.EdgeTable[i]} exceeds 12-bit range");
        }
    }

    [Fact]
    public void TriTable_NonEmptyEntries_HaveCorrespondingEdgeMask()
    {
        // If TriTable has triangles, EdgeTable should have corresponding bits set
        for (var i = 0; i < 256; i++)
        {
            var tris = MarchingCubesTables.TriTable[i];
            if (tris.Length == 0) continue;

            var edgeMask = MarchingCubesTables.EdgeTable[i];
            Assert.NotEqual(0, edgeMask);

            // Every edge index used in triangles should have its bit set in EdgeTable
            foreach (var edgeIdx in tris)
            {
                Assert.True((edgeMask & (1 << edgeIdx)) != 0,
                    $"TriTable[{i}] uses edge {edgeIdx} but EdgeTable[{i}]=0x{edgeMask:X} doesn't have that bit set");
            }
        }
    }

    [Fact]
    public void TriTable_MaxTriangles_IsFive()
    {
        // A single cube can have at most 5 triangles in standard marching cubes
        for (var i = 0; i < 256; i++)
        {
            var triangleCount = MarchingCubesTables.TriTable[i].Length / 3;
            Assert.True(triangleCount <= 5,
                $"TriTable[{i}] has {triangleCount} triangles (max is 5)");
        }
    }

    [Fact]
    public void EdgeTable_SymmetricCases_HaveSameEdgeCount()
    {
        // Case i and case (255-i) are complementary — they should use the same edges
        for (var i = 0; i < 128; i++)
        {
            var complement = 255 - i;
            Assert.Equal(
                MarchingCubesTables.EdgeTable[i],
                MarchingCubesTables.EdgeTable[complement]);
        }
    }
}
