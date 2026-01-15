using BeyondImmersion.Bannou.AssetBundler.Godot.Processing;
using Xunit;

namespace BeyondImmersion.Bannou.AssetBundler.Godot.Tests.Processing;

/// <summary>
/// Tests for GodotProcessingException.
/// </summary>
public class GodotProcessingExceptionTests
{
    [Fact]
    public void Constructor_WithMessage_SetsMessage()
    {
        var exception = new GodotProcessingException("Test error message");

        Assert.Equal("Test error message", exception.Message);
    }

    [Fact]
    public void Constructor_WithMessageAndInnerException_SetsBoth()
    {
        var inner = new InvalidOperationException("Inner error");
        var exception = new GodotProcessingException("Outer error", inner);

        Assert.Equal("Outer error", exception.Message);
        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public void ConversionFailed_CreatesFormattedMessage()
    {
        var exception = GodotProcessingException.ConversionFailed(
            "asset-001",
            "FBX",
            "glTF",
            "Converter not found");

        Assert.Contains("asset-001", exception.Message);
        Assert.Contains("FBX", exception.Message);
        Assert.Contains("glTF", exception.Message);
        Assert.Contains("Converter not found", exception.Message);
    }

    [Fact]
    public void ConverterNotFound_CreatesFormattedMessage()
    {
        var exception = GodotProcessingException.ConverterNotFound(
            "texture-002",
            "TGA",
            "ImageMagick");

        Assert.Contains("texture-002", exception.Message);
        Assert.Contains("TGA", exception.Message);
        Assert.Contains("ImageMagick", exception.Message);
    }

    [Fact]
    public void InitProperties_CanBeSet()
    {
        var exception = new GodotProcessingException("Error")
        {
            AssetId = "asset-123",
            SourceFormat = ".fbx",
            TargetFormat = ".glb"
        };

        Assert.Equal("asset-123", exception.AssetId);
        Assert.Equal(".fbx", exception.SourceFormat);
        Assert.Equal(".glb", exception.TargetFormat);
    }
}
