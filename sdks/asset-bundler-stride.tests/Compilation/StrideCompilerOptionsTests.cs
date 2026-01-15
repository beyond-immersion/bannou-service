using BeyondImmersion.Bannou.AssetBundler.Stride.Compilation;
using Xunit;

namespace BeyondImmersion.Bannou.AssetBundler.Stride.Tests.Compilation;

/// <summary>
/// Tests for StrideCompilerOptions configuration.
/// </summary>
public class StrideCompilerOptionsTests
{
    [Fact]
    public void DefaultOptions_HasSensibleDefaults()
    {
        // Act
        var options = new StrideCompilerOptions();

        // Assert
        Assert.NotNull(options.DotnetPath);
        Assert.Equal("dotnet", options.DotnetPath);
        Assert.Equal("Release", options.Configuration);
        Assert.False(options.VerboseOutput);
        Assert.True(options.BuildTimeoutMs > 0);
    }

    [Fact]
    public void StrideVersion_CanBeSet()
    {
        // Arrange
        var options = new StrideCompilerOptions
        {
            StrideVersion = "4.3.0.2507"
        };

        // Assert
        Assert.Equal("4.3.0.2507", options.StrideVersion);
    }

    [Fact]
    public void TextureCompression_DefaultsToBC7()
    {
        // Arrange
        var options = new StrideCompilerOptions();

        // Assert - BC7 provides good quality/compression trade-off for desktop platforms
        Assert.Equal(StrideTextureCompression.BC7, options.TextureCompression);
    }

    [Theory]
    [InlineData(StrideTextureCompression.BC1)]
    [InlineData(StrideTextureCompression.BC3)]
    [InlineData(StrideTextureCompression.BC7)]
    [InlineData(StrideTextureCompression.ETC2)]
    [InlineData(StrideTextureCompression.ASTC)]
    public void TextureCompression_CanBeSet(StrideTextureCompression compression)
    {
        // Arrange
        var options = new StrideCompilerOptions
        {
            TextureCompression = compression
        };

        // Assert
        Assert.Equal(compression, options.TextureCompression);
    }

    [Fact]
    public void MaxTextureSize_DefaultsToReasonableValue()
    {
        // Arrange
        var options = new StrideCompilerOptions();

        // Assert
        Assert.True(options.MaxTextureSize > 0);
        Assert.True(options.MaxTextureSize <= 8192);
    }

    [Fact]
    public void GenerateMipmaps_DefaultsToTrue()
    {
        // Arrange
        var options = new StrideCompilerOptions();

        // Assert
        Assert.True(options.GenerateMipmaps);
    }

    [Fact]
    public void GenerateMipmaps_CanBeDisabled()
    {
        // Arrange
        var options = new StrideCompilerOptions
        {
            GenerateMipmaps = false
        };

        // Assert
        Assert.False(options.GenerateMipmaps);
    }

    [Fact]
    public void BuildTimeoutMs_CanBeCustomized()
    {
        // Arrange
        var options = new StrideCompilerOptions
        {
            BuildTimeoutMs = 600_000 // 10 minutes
        };

        // Assert
        Assert.Equal(600_000, options.BuildTimeoutMs);
    }

    [Fact]
    public void Configuration_CanBeDebug()
    {
        // Arrange
        var options = new StrideCompilerOptions
        {
            Configuration = "Debug"
        };

        // Assert
        Assert.Equal("Debug", options.Configuration);
    }

    [Fact]
    public void VerboseOutput_CanBeEnabled()
    {
        // Arrange
        var options = new StrideCompilerOptions
        {
            VerboseOutput = true
        };

        // Assert
        Assert.True(options.VerboseOutput);
    }

    [Fact]
    public void DotnetPath_CanBeCustomized()
    {
        // Arrange
        var options = new StrideCompilerOptions
        {
            DotnetPath = "/usr/local/share/dotnet/dotnet"
        };

        // Assert
        Assert.Equal("/usr/local/share/dotnet/dotnet", options.DotnetPath);
    }
}
