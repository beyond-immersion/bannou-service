using BeyondImmersion.Bannou.VoxelCore.Math;
using Xunit;
using SdkColor = BeyondImmersion.Bannou.VoxelCore.Grid.Color;
using StrideColor = Stride.Core.Mathematics.Color;
using StrideVec3 = Stride.Core.Mathematics.Vector3;

namespace BeyondImmersion.Bannou.VoxelBuilder.Stride.Tests;

/// <summary>
/// Unit tests for <see cref="StrideTypeConverter"/> — SDK ↔ Stride type conversions.
/// </summary>
public class StrideTypeConverterTests
{
    #region SdkColor → StrideColor

    [Fact]
    public void ToStride_Color_FullyOpaque()
    {
        var sdk = new SdkColor(255, 128, 64, 255);
        var stride = sdk.ToStride();

        Assert.Equal(255, stride.R);
        Assert.Equal(128, stride.G);
        Assert.Equal(64, stride.B);
        Assert.Equal(255, stride.A);
    }

    [Fact]
    public void ToStride_Color_TranslucentAlpha()
    {
        var sdk = new SdkColor(100, 200, 50, 128);
        var stride = sdk.ToStride();

        Assert.Equal(100, stride.R);
        Assert.Equal(200, stride.G);
        Assert.Equal(50, stride.B);
        Assert.Equal(128, stride.A);
    }

    [Fact]
    public void ToStride_Color_AllZeros()
    {
        var sdk = new SdkColor(0, 0, 0, 0);
        var stride = sdk.ToStride();

        Assert.Equal(0, stride.R);
        Assert.Equal(0, stride.G);
        Assert.Equal(0, stride.B);
        Assert.Equal(0, stride.A);
    }

    [Fact]
    public void ToStride_Color_AllMax()
    {
        var sdk = new SdkColor(255, 255, 255, 255);
        var stride = sdk.ToStride();

        Assert.Equal(255, stride.R);
        Assert.Equal(255, stride.G);
        Assert.Equal(255, stride.B);
        Assert.Equal(255, stride.A);
    }

    [Fact]
    public void ToStride_Color_DefaultAlpha_Is255()
    {
        // SDK Color defaults alpha to 255
        var sdk = new SdkColor(10, 20, 30);
        var stride = sdk.ToStride();

        Assert.Equal(255, stride.A);
    }

    #endregion

    #region StrideColor → SdkColor

    [Fact]
    public void ToSdk_Color_RoundTrip()
    {
        var original = new SdkColor(42, 84, 168, 200);
        var roundTripped = original.ToStride().ToSdk();

        Assert.Equal(original.R, roundTripped.R);
        Assert.Equal(original.G, roundTripped.G);
        Assert.Equal(original.B, roundTripped.B);
        Assert.Equal(original.A, roundTripped.A);
    }

    [Fact]
    public void ToSdk_Color_FromStride()
    {
        var stride = new StrideColor(77, 88, 99, 110);
        var sdk = stride.ToSdk();

        Assert.Equal(77, sdk.R);
        Assert.Equal(88, sdk.G);
        Assert.Equal(99, sdk.B);
        Assert.Equal(110, sdk.A);
    }

    #endregion

    #region VoxelCoord → Vector3

    [Fact]
    public void ToStride_VoxelCoord_Origin()
    {
        var coord = new VoxelCoord(0, 0, 0);
        var vec = coord.ToStride(0.25f);

        Assert.Equal(0f, vec.X);
        Assert.Equal(0f, vec.Y);
        Assert.Equal(0f, vec.Z);
    }

    [Fact]
    public void ToStride_VoxelCoord_PositiveScaled()
    {
        var coord = new VoxelCoord(4, 8, 12);
        var vec = coord.ToStride(0.25f);

        Assert.Equal(1.0f, vec.X);
        Assert.Equal(2.0f, vec.Y);
        Assert.Equal(3.0f, vec.Z);
    }

    [Fact]
    public void ToStride_VoxelCoord_NegativeCoordinates()
    {
        var coord = new VoxelCoord(-4, -8, -12);
        var vec = coord.ToStride(0.25f);

        Assert.Equal(-1.0f, vec.X);
        Assert.Equal(-2.0f, vec.Y);
        Assert.Equal(-3.0f, vec.Z);
    }

    [Fact]
    public void ToStride_VoxelCoord_UnitScale()
    {
        var coord = new VoxelCoord(5, 10, 15);
        var vec = coord.ToStride(1.0f);

        Assert.Equal(5f, vec.X);
        Assert.Equal(10f, vec.Y);
        Assert.Equal(15f, vec.Z);
    }

    [Fact]
    public void ToStride_VoxelCoord_LargeScale()
    {
        var coord = new VoxelCoord(1, 1, 1);
        var vec = coord.ToStride(10.0f);

        Assert.Equal(10f, vec.X);
        Assert.Equal(10f, vec.Y);
        Assert.Equal(10f, vec.Z);
    }

    #endregion

    #region ChunkCoord → WorldPosition

    [Fact]
    public void ToWorldPosition_Origin()
    {
        var coord = new ChunkCoord(0, 0, 0);
        var pos = coord.ToWorldPosition(0.25f);

        Assert.Equal(0f, pos.X);
        Assert.Equal(0f, pos.Y);
        Assert.Equal(0f, pos.Z);
    }

    [Fact]
    public void ToWorldPosition_FirstChunk()
    {
        // Chunk (1, 0, 0) at voxelScale 0.25 → 1 * 16 * 0.25 = 4.0
        var coord = new ChunkCoord(1, 0, 0);
        var pos = coord.ToWorldPosition(0.25f);

        Assert.Equal(4.0f, pos.X);
        Assert.Equal(0f, pos.Y);
        Assert.Equal(0f, pos.Z);
    }

    [Fact]
    public void ToWorldPosition_MultipleChunks()
    {
        // Chunk (2, 3, 1) at voxelScale 0.5 → X: 2*16*0.5=16, Y: 3*16*0.5=24, Z: 1*16*0.5=8
        var coord = new ChunkCoord(2, 3, 1);
        var pos = coord.ToWorldPosition(0.5f);

        Assert.Equal(16f, pos.X);
        Assert.Equal(24f, pos.Y);
        Assert.Equal(8f, pos.Z);
    }

    [Fact]
    public void ToWorldPosition_NegativeChunk()
    {
        var coord = new ChunkCoord(-1, -2, -3);
        var pos = coord.ToWorldPosition(0.25f);

        Assert.Equal(-4f, pos.X);
        Assert.Equal(-8f, pos.Y);
        Assert.Equal(-12f, pos.Z);
    }

    [Fact]
    public void ToWorldPosition_UnitScale()
    {
        // At scale 1.0, each chunk is 16 world units on a side
        var coord = new ChunkCoord(1, 1, 1);
        var pos = coord.ToWorldPosition(1.0f);

        Assert.Equal(16f, pos.X);
        Assert.Equal(16f, pos.Y);
        Assert.Equal(16f, pos.Z);
    }

    #endregion
}
