using BeyondImmersion.Bannou.VoxelBuilder.ImportExport;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelBuilder.Tests.ImportExport;

/// <summary>
/// Unit tests for <see cref="RawVoxelImporter"/> and <see cref="RawVoxelExporter"/> round-trip.
/// </summary>
public class RawVoxelRoundTripTests
{
    [Fact]
    public void EmptyGrid_RoundTrips()
    {
        var grid = new VoxelGrid(new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(15, 15, 15)));

        var exporter = new RawVoxelExporter();
        var bytes = exporter.Export(grid);

        var importer = new RawVoxelImporter();
        var result = importer.Import(bytes);

        Assert.Equal(0, result.VoxelCount);
        Assert.Equal(grid.Bounds, result.Bounds);
    }

    [Fact]
    public void GridWithVoxels_RoundTrips()
    {
        var grid = new VoxelGrid(new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(31, 31, 31)));
        grid.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(7, VoxelFlags.None));
        grid.SetVoxel(new VoxelCoord(10, 10, 10), new Voxel(3, VoxelFlags.Emissive));
        grid.SetVoxel(new VoxelCoord(20, 0, 0), new Voxel(1, VoxelFlags.Frozen));

        var exporter = new RawVoxelExporter();
        var bytes = exporter.Export(grid);

        var importer = new RawVoxelImporter();
        var result = importer.Import(bytes);

        Assert.Equal(3, result.VoxelCount);
        Assert.Equal(new Voxel(7, VoxelFlags.None), result.GetVoxel(new VoxelCoord(5, 5, 5)));
        Assert.Equal(new Voxel(3, VoxelFlags.Emissive), result.GetVoxel(new VoxelCoord(10, 10, 10)));
        Assert.Equal(new Voxel(1, VoxelFlags.Frozen), result.GetVoxel(new VoxelCoord(20, 0, 0)));
    }

    [Fact]
    public void StreamImport_MatchesByteImport()
    {
        var grid = new VoxelGrid(new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(15, 15, 15)));
        grid.SetVoxel(new VoxelCoord(3, 3, 3), new Voxel(5, VoxelFlags.None));

        var exporter = new RawVoxelExporter();
        var bytes = exporter.Export(grid);

        var importer = new RawVoxelImporter();
        var fromBytes = importer.Import(bytes);

        using var stream = new MemoryStream(bytes);
        var fromStream = importer.Import(stream);

        Assert.Equal(fromBytes.VoxelCount, fromStream.VoxelCount);
        Assert.Equal(fromBytes.Bounds, fromStream.Bounds);
        Assert.Equal(
            fromBytes.GetVoxel(new VoxelCoord(3, 3, 3)),
            fromStream.GetVoxel(new VoxelCoord(3, 3, 3)));
    }

    [Fact]
    public void Palette_IsPreserved()
    {
        var grid = new VoxelGrid(new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(15, 15, 15)));
        var idx = grid.Palette.GetOrAddIndex(new Color(255, 128, 64, 255), MaterialType.Metal, 0.7f);
        grid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(idx, VoxelFlags.None));

        var exporter = new RawVoxelExporter();
        var bytes = exporter.Export(grid);

        var importer = new RawVoxelImporter();
        var result = importer.Import(bytes);

        var entry = result.Palette.Get(idx);
        Assert.Equal(255, entry.Color.R);
        Assert.Equal(128, entry.Color.G);
        Assert.Equal(64, entry.Color.B);
        Assert.Equal(MaterialType.Metal, entry.Material);
        Assert.Equal(0.7f, entry.Roughness);
    }

    [Fact]
    public void MultipleChunks_RoundTrip()
    {
        var grid = new VoxelGrid(new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(31, 31, 31)));
        // Place voxels in different chunks
        grid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));   // chunk (0,0,0)
        grid.SetVoxel(new VoxelCoord(16, 0, 0), new Voxel(2, VoxelFlags.None));  // chunk (1,0,0)
        grid.SetVoxel(new VoxelCoord(0, 16, 0), new Voxel(3, VoxelFlags.None));  // chunk (0,1,0)

        var exporter = new RawVoxelExporter();
        var bytes = exporter.Export(grid);

        var importer = new RawVoxelImporter();
        var result = importer.Import(bytes);

        Assert.Equal(3, result.VoxelCount);
        Assert.Equal(1, result.GetVoxel(new VoxelCoord(0, 0, 0)).PaletteIndex);
        Assert.Equal(2, result.GetVoxel(new VoxelCoord(16, 0, 0)).PaletteIndex);
        Assert.Equal(3, result.GetVoxel(new VoxelCoord(0, 16, 0)).PaletteIndex);
    }
}
