using BeyondImmersion.Bannou.VoxelCore.Grid;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelCore.Tests.Grid;

/// <summary>
/// Unit tests for the <see cref="Voxel"/> struct.
/// </summary>
public class VoxelTests
{
    [Fact]
    public void Empty_HasZeroPaletteIndex() =>
        Assert.Equal(0, Voxel.Empty.PaletteIndex);

    [Fact]
    public void Empty_HasNoFlags() =>
        Assert.Equal(VoxelFlags.None, Voxel.Empty.Flags);

    [Fact]
    public void Empty_IsEmpty() =>
        Assert.True(Voxel.Empty.IsEmpty);

    [Fact]
    public void NonZeroPaletteIndex_IsNotEmpty()
    {
        var voxel = new Voxel(42, VoxelFlags.None);
        Assert.False(voxel.IsEmpty);
    }

    [Fact]
    public void Constructor_PreservesValues()
    {
        var voxel = new Voxel(128, VoxelFlags.Emissive | VoxelFlags.Frozen);
        Assert.Equal(128, voxel.PaletteIndex);
        Assert.Equal(VoxelFlags.Emissive | VoxelFlags.Frozen, voxel.Flags);
    }

    [Fact]
    public void ZeroPaletteIndex_IsEmpty_RegardlessOfFlags()
    {
        // Even with flags set, palette index 0 means empty
        var voxel = new Voxel(0, VoxelFlags.Damaged);
        Assert.True(voxel.IsEmpty);
    }

    [Fact]
    public void Equality_ValueSemantics()
    {
        var a = new Voxel(10, VoxelFlags.Hidden);
        var b = new Voxel(10, VoxelFlags.Hidden);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Inequality_DifferentPaletteIndex()
    {
        var a = new Voxel(10, VoxelFlags.None);
        var b = new Voxel(20, VoxelFlags.None);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Inequality_DifferentFlags()
    {
        var a = new Voxel(10, VoxelFlags.None);
        var b = new Voxel(10, VoxelFlags.Frozen);
        Assert.NotEqual(a, b);
    }
}
