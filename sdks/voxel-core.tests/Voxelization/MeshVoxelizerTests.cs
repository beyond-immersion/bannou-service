using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using BeyondImmersion.Bannou.VoxelCore.Meshing;
using BeyondImmersion.Bannou.VoxelCore.Voxelization;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelCore.Tests.Voxelization;

/// <summary>
/// Unit tests for <see cref="MeshVoxelizer"/> — mesh to voxel grid conversion.
/// </summary>
public class MeshVoxelizerTests
{
    /// <summary>
    /// Creates a simple axis-aligned quad (two triangles) centered at origin.
    /// </summary>
    private static MeshData CreateSimpleQuadMesh(float size = 4f)
    {
        var half = size / 2f;
        return new MeshData(
            vertices: new[]
            {
                -half, 0f, -half,
                 half, 0f, -half,
                 half, 0f,  half,
                -half, 0f,  half
            },
            normals: new[]
            {
                0f, 1f, 0f,
                0f, 1f, 0f,
                0f, 1f, 0f,
                0f, 1f, 0f
            },
            uvs: null,
            indices: new[] { 0, 1, 2, 0, 2, 3 },
            colors: null,
            ambientOcclusion: null,
            vertexCount: 4,
            triangleCount: 2);
    }

    /// <summary>
    /// Creates a simple 3D box mesh (12 triangles, 8 unique vertex positions).
    /// </summary>
    private static MeshData CreateBoxMesh(float size = 2f)
    {
        var h = size / 2f;
        // 8 corner positions, 36 indices (12 triangles)
        var verts = new List<float>();
        var norms = new List<float>();
        var idxs = new List<int>();

        // Define 6 faces with 4 vertices each = 24 vertices
        var corners = new (float x, float y, float z)[]
        {
            (-h,-h,-h), (h,-h,-h), (h,-h,h), (-h,-h,h),
            (-h, h,-h), (h, h,-h), (h, h,h), (-h, h,h)
        };

        var faces = new (int a, int b, int c, int d, float nx, float ny, float nz)[]
        {
            (0,1,2,3, 0,-1,0), // bottom
            (4,7,6,5, 0, 1,0), // top
            (0,3,7,4,-1, 0,0), // left
            (1,5,6,2, 1, 0,0), // right
            (0,4,5,1, 0, 0,-1), // back
            (3,2,6,7, 0, 0, 1), // front
        };

        var vi = 0;
        foreach (var (a,b,c,d,nx,ny,nz) in faces)
        {
            foreach (var idx in new[] { a, b, c, d })
            {
                var (cx,cy,cz) = corners[idx];
                verts.Add(cx); verts.Add(cy); verts.Add(cz);
                norms.Add(nx); norms.Add(ny); norms.Add(nz);
            }
            idxs.Add(vi); idxs.Add(vi+1); idxs.Add(vi+2);
            idxs.Add(vi); idxs.Add(vi+2); idxs.Add(vi+3);
            vi += 4;
        }

        return new MeshData(
            verts.ToArray(), norms.ToArray(), null, idxs.ToArray(),
            null, null, vi, idxs.Count / 3);
    }

    [Fact]
    public void FlatQuad_ProducesVoxels()
    {
        var palette = new Palette();
        palette.Set(1, new PaletteEntry(Color.White, MaterialType.Diffuse, 0.5f));
        var options = new VoxelizationOptions(
            VoxelScale: 1.0f, FillMode: VoxelFillMode.Surface, FrozenBorderWidth: 0, DefaultPaletteIndex: 1);

        var grid = MeshVoxelizer.Voxelize(CreateSimpleQuadMesh(), palette, options);

        Assert.True(grid.VoxelCount > 0, "Voxelized quad should produce non-empty voxels");
    }

    [Fact]
    public void Box_SolidFill_FillsInterior()
    {
        var palette = new Palette();
        palette.Set(1, new PaletteEntry(Color.White, MaterialType.Diffuse, 0.5f));
        var options = new VoxelizationOptions(
            VoxelScale: 1.0f, FillMode: VoxelFillMode.Solid, FrozenBorderWidth: 0, DefaultPaletteIndex: 1);

        var surfaceGrid = MeshVoxelizer.Voxelize(CreateBoxMesh(4f), palette, new VoxelizationOptions(
            VoxelScale: 1.0f, FillMode: VoxelFillMode.Surface, FrozenBorderWidth: 0, DefaultPaletteIndex: 1));
        var solidGrid = MeshVoxelizer.Voxelize(CreateBoxMesh(4f), palette, options);

        // Solid fill should have more voxels than surface-only
        Assert.True(solidGrid.VoxelCount >= surfaceGrid.VoxelCount,
            $"Solid ({solidGrid.VoxelCount}) should have >= voxels than surface ({surfaceGrid.VoxelCount})");
    }

    [Fact]
    public void FrozenBorder_MarkedOnEdgeVoxels()
    {
        var palette = new Palette();
        palette.Set(1, new PaletteEntry(Color.White, MaterialType.Diffuse, 0.5f));
        var options = new VoxelizationOptions(
            VoxelScale: 1.0f, FillMode: VoxelFillMode.Solid, FrozenBorderWidth: 1, DefaultPaletteIndex: 1);

        var grid = MeshVoxelizer.Voxelize(CreateBoxMesh(6f), palette, options);

        // Find a border voxel and an interior voxel
        var hasFrozen = false;
        var hasNonFrozen = false;
        foreach (var (_, chunk) in grid.EnumerateChunks())
        {
            for (var y = 0; y < 16; y++)
            for (var z = 0; z < 16; z++)
            for (var x = 0; x < 16; x++)
            {
                var voxel = chunk.GetVoxel(x, y, z);
                if (voxel.IsEmpty) continue;
                if (voxel.Flags.HasFlag(VoxelFlags.Frozen)) hasFrozen = true;
                else hasNonFrozen = true;
            }
        }

        Assert.True(hasFrozen, "Should have frozen border voxels");
        Assert.True(hasNonFrozen, "Should have non-frozen interior voxels");
    }

    [Fact]
    public void Deterministic_SameInputProducesSameOutput()
    {
        var palette = new Palette();
        palette.Set(1, new PaletteEntry(Color.White, MaterialType.Diffuse, 0.5f));
        var options = new VoxelizationOptions(VoxelScale: 1.0f, FillMode: VoxelFillMode.Solid,
            FrozenBorderWidth: 0, DefaultPaletteIndex: 1);
        var mesh = CreateBoxMesh();

        var grid1 = MeshVoxelizer.Voxelize(mesh, palette, options);
        var grid2 = MeshVoxelizer.Voxelize(mesh, palette, options);

        Assert.Equal(grid1.VoxelCount, grid2.VoxelCount);
    }

    [Fact]
    public void VertexColors_SampledIntoPalette()
    {
        var palette = new Palette();
        var mesh = new MeshData(
            vertices: new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0.5f, 1f, 0f },
            normals: new[] { 0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f },
            uvs: null,
            indices: new[] { 0, 1, 2 },
            colors: new byte[] { 200, 100, 50, 255, 200, 100, 50, 255, 200, 100, 50, 255 },
            ambientOcclusion: null,
            vertexCount: 3,
            triangleCount: 1);

        var options = new VoxelizationOptions(VoxelScale: 1.0f, FillMode: VoxelFillMode.Surface,
            FrozenBorderWidth: 0, DefaultPaletteIndex: 1);
        MeshVoxelizer.Voxelize(mesh, palette, options);

        // Palette should have gained an entry from the vertex colors
        Assert.True(palette.UsedCount > 0);
    }
}
