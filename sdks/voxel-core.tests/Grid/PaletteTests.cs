using BeyondImmersion.Bannou.VoxelCore.Grid;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelCore.Tests.Grid;

/// <summary>
/// Unit tests for the <see cref="Palette"/> class.
/// </summary>
public class PaletteTests
{
    [Fact]
    public void NewPalette_HasZeroUsedCount()
    {
        var palette = new Palette();
        Assert.Equal(0, palette.UsedCount);
    }

    [Fact]
    public void Index0_IsAlwaysEmpty()
    {
        var palette = new Palette();
        Assert.Equal(PaletteEntry.Empty, palette.Get(0));
    }

    [Fact]
    public void Set_CannotSetIndex0()
    {
        var palette = new Palette();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            palette.Set(0, new PaletteEntry(Color.White, MaterialType.Diffuse, 0.5f)));
    }

    [Fact]
    public void Set_StoresAndRetrievesEntry()
    {
        var palette = new Palette();
        var entry = new PaletteEntry(new Color(255, 0, 0), MaterialType.Metal, 0.3f);
        palette.Set(1, entry);
        Assert.Equal(entry, palette.Get(1));
    }

    [Fact]
    public void GetOrAddIndex_FirstEntry_ReturnsIndex1()
    {
        var palette = new Palette();
        var index = palette.GetOrAddIndex(Color.White, MaterialType.Diffuse);
        Assert.Equal(1, index);
        Assert.Equal(1, palette.UsedCount);
    }

    [Fact]
    public void GetOrAddIndex_SameEntry_ReturnsSameIndex()
    {
        var palette = new Palette();
        var index1 = palette.GetOrAddIndex(Color.White, MaterialType.Diffuse, 0.5f);
        var index2 = palette.GetOrAddIndex(Color.White, MaterialType.Diffuse, 0.5f);
        Assert.Equal(index1, index2);
        Assert.Equal(1, palette.UsedCount);
    }

    [Fact]
    public void GetOrAddIndex_DifferentColor_ReturnsDifferentIndex()
    {
        var palette = new Palette();
        var index1 = palette.GetOrAddIndex(Color.White, MaterialType.Diffuse);
        var index2 = palette.GetOrAddIndex(Color.Black, MaterialType.Diffuse);
        Assert.NotEqual(index1, index2);
        Assert.Equal(2, palette.UsedCount);
    }

    [Fact]
    public void GetOrAddIndex_SameColorDifferentMaterial_ReturnsDifferentIndex()
    {
        var palette = new Palette();
        var color = new Color(128, 128, 128);
        var index1 = palette.GetOrAddIndex(color, MaterialType.Diffuse);
        var index2 = palette.GetOrAddIndex(color, MaterialType.Metal);
        Assert.NotEqual(index1, index2);
    }

    [Fact]
    public void GetOrAddIndex_SameColorDifferentRoughness_ReturnsDifferentIndex()
    {
        var palette = new Palette();
        var color = new Color(128, 128, 128);
        var index1 = palette.GetOrAddIndex(color, MaterialType.Diffuse, 0.0f);
        var index2 = palette.GetOrAddIndex(color, MaterialType.Diffuse, 1.0f);
        Assert.NotEqual(index1, index2);
    }

    [Fact]
    public void GetOrAddIndex_Full_ThrowsInvalidOperation()
    {
        var palette = new Palette();
        // Fill all 255 slots
        for (var i = 0; i < 255; i++)
            palette.GetOrAddIndex(new Color((byte)(i % 256), (byte)(i / 256), 0), MaterialType.Diffuse);

        Assert.Equal(255, palette.UsedCount);
        Assert.Throws<InvalidOperationException>(() =>
            palette.GetOrAddIndex(new Color(99, 99, 99), MaterialType.Glass));
    }

    [Fact]
    public void Contains_ReturnsTrueForExistingEntry()
    {
        var palette = new Palette();
        palette.GetOrAddIndex(Color.White, MaterialType.Emit, 0.8f);
        Assert.True(palette.Contains(Color.White, MaterialType.Emit, 0.8f));
    }

    [Fact]
    public void Contains_ReturnsFalseForMissing()
    {
        var palette = new Palette();
        Assert.False(palette.Contains(Color.White));
    }

    [Fact]
    public void Set_OverwriteUpdatesReverseIndex()
    {
        var palette = new Palette();
        var entry1 = new PaletteEntry(Color.White, MaterialType.Diffuse, 0.5f);
        var entry2 = new PaletteEntry(Color.Black, MaterialType.Metal, 0.1f);

        palette.Set(1, entry1);
        Assert.True(palette.Contains(Color.White, MaterialType.Diffuse, 0.5f));

        palette.Set(1, entry2);
        Assert.False(palette.Contains(Color.White, MaterialType.Diffuse, 0.5f));
        Assert.True(palette.Contains(Color.Black, MaterialType.Metal, 0.1f));
    }
}
