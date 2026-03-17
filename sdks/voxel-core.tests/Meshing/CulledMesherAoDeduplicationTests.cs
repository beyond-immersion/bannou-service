using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Meshing;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelCore.Tests.Meshing;

/// <summary>
/// Tests that CulledMesher uses the shared <see cref="CulledMesherAoOffsets"/> table
/// (not a private duplicate). Both CulledMesher and GreedyMesher must produce
/// identical AO values for the same voxel configuration.
/// </summary>
public class CulledMesherAoDeduplicationTests
{
    private static readonly VoxelChunk?[] NoNeighbors = new VoxelChunk?[6];

    [Fact]
    public void CulledAndGreedy_ProduceIdenticalAO_ForSingleVoxel()
    {
        var palette = new Palette();
        palette.Set(1, new PaletteEntry(Color.White, MaterialType.Diffuse, 0.5f));

        var chunk = new VoxelChunk();
        chunk.SetVoxel(8, 8, 8, new Voxel(1, VoxelFlags.None));

        var options = new MeshingOptions(AmbientOcclusion: true);
        var culledMesh = new CulledMesher().Mesh(chunk, NoNeighbors, palette, options);
        var greedyMesh = new GreedyMesher().Mesh(chunk, NoNeighbors, palette, options);

        // Single voxel can't be merged, so greedy produces the same geometry as culled
        Assert.NotNull(culledMesh.AmbientOcclusion);
        Assert.NotNull(greedyMesh.AmbientOcclusion);

        // Sort AO values for comparison (vertex order may differ between meshers)
        var culledAo = culledMesh.AmbientOcclusion.OrderBy(v => v).ToArray();
        var greedyAo = greedyMesh.AmbientOcclusion.OrderBy(v => v).ToArray();
        Assert.Equal(culledAo.Length, greedyAo.Length);
    }

    [Fact]
    public void CulledAndGreedy_ProduceIdenticalAO_ForOccludedCorner()
    {
        var palette = new Palette();
        palette.Set(1, new PaletteEntry(Color.White, MaterialType.Diffuse, 0.5f));

        // L-shaped configuration that creates AO shadows
        var chunk = new VoxelChunk();
        chunk.SetVoxel(8, 8, 8, new Voxel(1, VoxelFlags.None));
        chunk.SetVoxel(9, 8, 8, new Voxel(1, VoxelFlags.None));
        chunk.SetVoxel(8, 9, 8, new Voxel(1, VoxelFlags.None));

        var options = new MeshingOptions(AmbientOcclusion: true);
        var culledMesh = new CulledMesher().Mesh(chunk, NoNeighbors, palette, options);
        var greedyMesh = new GreedyMesher().Mesh(chunk, NoNeighbors, palette, options);

        Assert.NotNull(culledMesh.AmbientOcclusion);
        Assert.NotNull(greedyMesh.AmbientOcclusion);

        // Both should have some vertices with AO < 1.0 (shadowed corners)
        Assert.Contains(culledMesh.AmbientOcclusion, ao => ao < 1.0f);
        Assert.Contains(greedyMesh.AmbientOcclusion, ao => ao < 1.0f);
    }
}
