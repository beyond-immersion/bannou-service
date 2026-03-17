using BeyondImmersion.Bannou.VoxelBuilder.Core;
using BeyondImmersion.Bannou.VoxelBuilder.Operations;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelBuilder.Tests.Operations;

/// <summary>
/// Unit tests for the <see cref="CopyPasteOperation"/> with palette merging.
/// </summary>
public class CopyPasteOperationTests
{
    private static VoxelGrid CreateGrid() =>
        new(new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(31, 31, 31)));

    private static VoxelBuilderOptions DefaultOptions => VoxelBuilderOptions.Default;

    [Fact]
    public void Paste_PlacesClipboardVoxelsAtOffset()
    {
        var grid = CreateGrid();
        var clipboard = new VoxelClipboard
        {
            Bounds = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(1, 0, 0))
        };
        clipboard.Voxels[new VoxelCoord(0, 0, 0)] = new Voxel(5, VoxelFlags.None);
        clipboard.Voxels[new VoxelCoord(1, 0, 0)] = new Voxel(7, VoxelFlags.None);
        clipboard.PaletteSnapshot[5] = new PaletteEntry(new Color(255, 0, 0, 255), MaterialType.Diffuse, 0.5f);
        clipboard.PaletteSnapshot[7] = new PaletteEntry(new Color(0, 255, 0, 255), MaterialType.Diffuse, 0.5f);

        // Add palette entries to grid so indices can map
        grid.Palette.GetOrAddIndex(new Color(255, 0, 0, 255), MaterialType.Diffuse, 0.5f);
        grid.Palette.GetOrAddIndex(new Color(0, 255, 0, 255), MaterialType.Diffuse, 0.5f);

        var op = new CopyPasteOperation(clipboard, new VoxelCoord(10, 0, 0));
        op.Execute(grid, DefaultOptions);

        Assert.False(grid.GetVoxel(new VoxelCoord(10, 0, 0)).IsEmpty);
        Assert.False(grid.GetVoxel(new VoxelCoord(11, 0, 0)).IsEmpty);
    }

    [Fact]
    public void Paste_Undo_RestoresPreviousVoxels()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(10, 0, 0), new Voxel(3, VoxelFlags.Emissive));

        var clipboard = new VoxelClipboard
        {
            Bounds = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(0, 0, 0))
        };
        clipboard.Voxels[new VoxelCoord(0, 0, 0)] = new Voxel(5, VoxelFlags.None);
        clipboard.PaletteSnapshot[5] = new PaletteEntry(new Color(255, 0, 0, 255), MaterialType.Diffuse, 0.5f);

        grid.Palette.GetOrAddIndex(new Color(255, 0, 0, 255), MaterialType.Diffuse, 0.5f);

        var op = new CopyPasteOperation(clipboard, new VoxelCoord(10, 0, 0));
        op.Execute(grid, DefaultOptions);
        op.Undo(grid);

        var restored = grid.GetVoxel(new VoxelCoord(10, 0, 0));
        Assert.Equal(3, restored.PaletteIndex);
        Assert.Equal(VoxelFlags.Emissive, restored.Flags);
    }

    [Fact]
    public void Paste_SkipsFrozenVoxels()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(10, 0, 0), new Voxel(3, VoxelFlags.Frozen));

        var clipboard = new VoxelClipboard
        {
            Bounds = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(0, 0, 0))
        };
        clipboard.Voxels[new VoxelCoord(0, 0, 0)] = new Voxel(5, VoxelFlags.None);
        clipboard.PaletteSnapshot[5] = new PaletteEntry(new Color(255, 0, 0, 255), MaterialType.Diffuse, 0.5f);

        var op = new CopyPasteOperation(clipboard, new VoxelCoord(10, 0, 0));
        op.Execute(grid, DefaultOptions);

        // Frozen voxel unchanged
        Assert.Equal(3, grid.GetVoxel(new VoxelCoord(10, 0, 0)).PaletteIndex);
    }

    [Fact]
    public void Paste_SkipsOutOfBoundsCoords()
    {
        var grid = CreateGrid(); // bounds 0-31

        var clipboard = new VoxelClipboard
        {
            Bounds = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(0, 0, 0))
        };
        clipboard.Voxels[new VoxelCoord(0, 0, 0)] = new Voxel(5, VoxelFlags.None);
        clipboard.PaletteSnapshot[5] = new PaletteEntry(new Color(255, 0, 0, 255), MaterialType.Diffuse, 0.5f);

        // Offset puts voxel out of bounds
        var op = new CopyPasteOperation(clipboard, new VoxelCoord(50, 0, 0));
        op.Execute(grid, DefaultOptions);

        Assert.Equal(0, grid.VoxelCount);
    }

    [Fact]
    public void Paste_PaletteMerging_MapsIndices()
    {
        var grid = CreateGrid();
        // Grid palette: index 1 = red
        grid.Palette.GetOrAddIndex(new Color(255, 0, 0, 255), MaterialType.Diffuse, 0.5f);

        // Clipboard has palette index 5 = red (same color, different index)
        var clipboard = new VoxelClipboard
        {
            Bounds = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(0, 0, 0))
        };
        clipboard.Voxels[new VoxelCoord(0, 0, 0)] = new Voxel(5, VoxelFlags.None);
        clipboard.PaletteSnapshot[5] = new PaletteEntry(new Color(255, 0, 0, 255), MaterialType.Diffuse, 0.5f);

        var op = new CopyPasteOperation(clipboard, new VoxelCoord(10, 0, 0));
        op.Execute(grid, DefaultOptions);

        // Should map clipboard index 5 → grid index 1 (same color)
        Assert.Equal(1, grid.GetVoxel(new VoxelCoord(10, 0, 0)).PaletteIndex);
    }

    [Fact]
    public void Paste_AffectedRegion_MatchesClipboardBoundsAtOffset()
    {
        var clipboard = new VoxelClipboard
        {
            Bounds = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(3, 3, 3))
        };
        var offset = new VoxelCoord(10, 5, 2);

        var op = new CopyPasteOperation(clipboard, offset);
        var region = op.AffectedRegion;

        Assert.Equal(new VoxelCoord(10, 5, 2), region.Min);
        Assert.Equal(new VoxelCoord(13, 8, 5), region.Max);
    }

    [Fact]
    public void Paste_SkipsEmptyClipboardVoxels()
    {
        var grid = CreateGrid();

        var clipboard = new VoxelClipboard
        {
            Bounds = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(1, 0, 0))
        };
        clipboard.Voxels[new VoxelCoord(0, 0, 0)] = Voxel.Empty;

        var op = new CopyPasteOperation(clipboard, new VoxelCoord(10, 0, 0));
        op.Execute(grid, DefaultOptions);

        Assert.Equal(0, grid.VoxelCount);
    }
}
