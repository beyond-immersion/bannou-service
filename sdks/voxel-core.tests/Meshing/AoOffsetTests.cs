using BeyondImmersion.Bannou.VoxelCore.Meshing;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelCore.Tests.Meshing;

/// <summary>
/// Unit tests for the internal <see cref="CulledMesherAoOffsets"/> lookup table
/// used by both CulledMesher and GreedyMesher for ambient occlusion computation.
/// </summary>
public class AoOffsetTests
{
    [Fact]
    public void Get_AllFacesAndVertices_ReturnThreeOffsets()
    {
        for (var face = 0; face < 6; face++)
        for (var vertex = 0; vertex < 4; vertex++)
        {
            var offsets = CulledMesherAoOffsets.Get(face, vertex);
            Assert.Equal(3, offsets.Length); // side1, side2, corner
        }
    }

    [Fact]
    public void Get_Offsets_AreNonZero()
    {
        // Every AO sample offset must be displaced from the voxel center
        for (var face = 0; face < 6; face++)
        for (var vertex = 0; vertex < 4; vertex++)
        {
            var offsets = CulledMesherAoOffsets.Get(face, vertex);
            foreach (var (dx, dy, dz) in offsets)
            {
                var manhattan = System.Math.Abs(dx) + System.Math.Abs(dy) + System.Math.Abs(dz);
                Assert.True(manhattan > 0, $"AO offset for face {face} vertex {vertex} is zero");
            }
        }
    }

    [Fact]
    public void Get_Offsets_IncludeFaceNormalComponent()
    {
        // Every AO offset should include the face normal direction component
        for (var face = 0; face < 6; face++)
        {
            var (ndx, ndy, ndz) = MesherHelpers.FaceDirections[face];
            for (var vertex = 0; vertex < 4; vertex++)
            {
                var offsets = CulledMesherAoOffsets.Get(face, vertex);
                // At least the side1 and side2 offsets should have the face normal component
                foreach (var (dx, dy, dz) in offsets)
                {
                    // The offset projected onto the normal should match the normal direction
                    var normalProjection = dx * ndx + dy * ndy + dz * ndz;
                    Assert.True(normalProjection >= 1,
                        $"AO offset ({dx},{dy},{dz}) for face {face} vertex {vertex} " +
                        $"doesn't project onto normal ({ndx},{ndy},{ndz})");
                }
            }
        }
    }

    [Fact]
    public void Get_DiagonalOffset_IsSumOfSideOffsets()
    {
        // The corner (diagonal) offset should be the componentwise sum of
        // side1 and side2, minus the face normal (which is shared)
        for (var face = 0; face < 6; face++)
        for (var vertex = 0; vertex < 4; vertex++)
        {
            var offsets = CulledMesherAoOffsets.Get(face, vertex);
            var (s1dx, s1dy, s1dz) = offsets[0]; // side1
            var (s2dx, s2dy, s2dz) = offsets[1]; // side2
            var (cdx, cdy, cdz) = offsets[2];     // corner

            var (ndx, ndy, ndz) = MesherHelpers.FaceDirections[face];
            // corner = side1 + side2 - normal (they both include the normal once)
            Assert.Equal(s1dx + s2dx - ndx, cdx);
            Assert.Equal(s1dy + s2dy - ndy, cdy);
            Assert.Equal(s1dz + s2dz - ndz, cdz);
        }
    }

    [Fact]
    public void Get_SameFaceVertex_ReturnsSameReference()
    {
        // Verify the table is stable (not regenerated per call)
        var offsets1 = CulledMesherAoOffsets.Get(0, 0);
        var offsets2 = CulledMesherAoOffsets.Get(0, 0);
        Assert.Same(offsets1, offsets2);
    }
}
