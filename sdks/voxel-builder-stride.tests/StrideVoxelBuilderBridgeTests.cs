using Xunit;

namespace BeyondImmersion.Bannou.VoxelBuilder.Stride.Tests;

/// <summary>
/// Unit tests for <see cref="StrideVoxelBuilderBridge"/> — constructor null guards only.
/// GPU-dependent behavior (entity lifecycle, buffer management, mesh rendering) requires a
/// running Stride game context and belongs in integration tests, not unit tests.
/// This follows the same pattern as scene-composer-stride.tests which only tests pure type
/// conversions at the unit level.
/// </summary>
public class StrideVoxelBuilderBridgeTests
{
    [Fact]
    public void Constructor_NullScene_Throws()
    {
        // Scene null check fires first — before any GPU access
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new StrideVoxelBuilderBridge(null!, null!, null!, null!));
        Assert.Equal("scene", ex.ParamName);
    }

    [Fact]
    public void Constructor_NullCamera_Throws()
    {
        // Camera null check is second — Scene() constructor doesn't need GPU
        using var scene = new global::Stride.Engine.Scene();
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new StrideVoxelBuilderBridge(scene, null!, null!, null!));
        Assert.Equal("camera", ex.ParamName);
    }

    [Fact]
    public void Constructor_NullGraphicsDevice_Throws()
    {
        using var scene = new global::Stride.Engine.Scene();
        var camera = new global::Stride.Engine.CameraComponent();
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new StrideVoxelBuilderBridge(scene, camera, null!, null!));
        Assert.Equal("graphicsDevice", ex.ParamName);
    }

    // NOTE: Constructor_NullCommandListProvider_Throws cannot be tested without a real
    // GraphicsDevice (the graphicsDevice check fires before commandListProvider).
    // Creating a GraphicsDevice requires GPU hardware.
    //
    // All other bridge tests (OnGridLoaded, OnChunksModified, OnPaletteChanged,
    // ScreenToVoxel, Dispose, SetMesher) require a running Stride GraphicsDevice.
    // These belong in integration tests, not unit tests.
    //
    // The pure logic is tested elsewhere:
    //   - InterleaveVertices, ConvertIndices → StrideChunkRendererTests
    //   - Type conversions → StrideTypeConverterTests
}
