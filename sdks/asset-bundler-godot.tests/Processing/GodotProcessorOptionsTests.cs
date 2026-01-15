using BeyondImmersion.Bannou.AssetBundler.Godot.Processing;
using Xunit;

namespace BeyondImmersion.Bannou.AssetBundler.Godot.Tests.Processing;

/// <summary>
/// Tests for GodotProcessorOptions default values.
/// </summary>
public class GodotProcessorOptionsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var options = new GodotProcessorOptions();

        Assert.True(options.EnableConversion);
        Assert.True(options.SkipUnconvertible);
        Assert.Null(options.FbxConverterPath);
        Assert.Equal(4096, options.MaxTextureSize);
        Assert.False(options.OptimizePng);
        Assert.Equal(90, options.JpegQuality);
        Assert.False(options.GenerateOrmTextures);
        Assert.True(options.TrackOriginalFormat);
    }

    [Fact]
    public void CustomValues_AreSet()
    {
        var options = new GodotProcessorOptions
        {
            EnableConversion = false,
            SkipUnconvertible = false,
            FbxConverterPath = "/usr/bin/fbx2gltf",
            MaxTextureSize = 2048,
            OptimizePng = true,
            JpegQuality = 85,
            GenerateOrmTextures = true,
            TrackOriginalFormat = false
        };

        Assert.False(options.EnableConversion);
        Assert.False(options.SkipUnconvertible);
        Assert.Equal("/usr/bin/fbx2gltf", options.FbxConverterPath);
        Assert.Equal(2048, options.MaxTextureSize);
        Assert.True(options.OptimizePng);
        Assert.Equal(85, options.JpegQuality);
        Assert.True(options.GenerateOrmTextures);
        Assert.False(options.TrackOriginalFormat);
    }

    [Fact]
    public void DisableMaxTextureSize_SetToZero()
    {
        var options = new GodotProcessorOptions
        {
            MaxTextureSize = 0
        };

        Assert.Equal(0, options.MaxTextureSize);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public void JpegQuality_ValidRange(int quality)
    {
        var options = new GodotProcessorOptions
        {
            JpegQuality = quality
        };

        Assert.Equal(quality, options.JpegQuality);
    }
}
