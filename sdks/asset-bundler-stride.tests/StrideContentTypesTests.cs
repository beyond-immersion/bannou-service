using BeyondImmersion.Bannou.AssetBundler.Stride;
using Xunit;

namespace BeyondImmersion.Bannou.AssetBundler.Stride.Tests;

/// <summary>
/// Tests for StrideContentTypes constants and helper methods.
/// </summary>
public class StrideContentTypesTests
{
    [Fact]
    public void Constants_AreCorrectMimeTypes()
    {
        Assert.Equal("application/x-stride-model", StrideContentTypes.Model);
        Assert.Equal("application/x-stride-texture", StrideContentTypes.Texture);
        Assert.Equal("application/x-stride-animation", StrideContentTypes.Animation);
        Assert.Equal("application/x-stride-material", StrideContentTypes.Material);
        Assert.Equal("application/x-stride-binary", StrideContentTypes.Binary);
    }

    [Theory]
    [InlineData(".sdmodel", "application/x-stride-model")]
    [InlineData("sdmodel", "application/x-stride-model")]
    [InlineData(".SDMODEL", "application/x-stride-model")]
    public void FromExtension_Model_ReturnsModelType(string extension, string expected)
    {
        Assert.Equal(expected, StrideContentTypes.FromExtension(extension));
    }

    [Theory]
    [InlineData(".sdtex", "application/x-stride-texture")]
    [InlineData("sdtex", "application/x-stride-texture")]
    [InlineData(".SDTEX", "application/x-stride-texture")]
    public void FromExtension_Texture_ReturnsTextureType(string extension, string expected)
    {
        Assert.Equal(expected, StrideContentTypes.FromExtension(extension));
    }

    [Theory]
    [InlineData(".sdanim", "application/x-stride-animation")]
    [InlineData("sdanim", "application/x-stride-animation")]
    public void FromExtension_Animation_ReturnsAnimationType(string extension, string expected)
    {
        Assert.Equal(expected, StrideContentTypes.FromExtension(extension));
    }

    [Theory]
    [InlineData(".sdmat", "application/x-stride-material")]
    [InlineData("sdmat", "application/x-stride-material")]
    public void FromExtension_Material_ReturnsMaterialType(string extension, string expected)
    {
        Assert.Equal(expected, StrideContentTypes.FromExtension(extension));
    }

    [Theory]
    [InlineData(".unknown", "application/x-stride-binary")]
    [InlineData("xyz", "application/x-stride-binary")]
    [InlineData(".bin", "application/x-stride-binary")]
    [InlineData("", "application/x-stride-binary")]
    public void FromExtension_Unknown_ReturnsBinaryType(string extension, string expected)
    {
        Assert.Equal(expected, StrideContentTypes.FromExtension(extension));
    }

    [Theory]
    [InlineData("sdmodel", "sdmodel")]
    [InlineData("sdtex", "sdtex")]
    [InlineData("sdanim", "sdanim")]
    [InlineData("sdmat", "sdmat")]
    [InlineData("unknown", "bin")]
    public void ToExtension_ReturnsCorrectExtension(string contentType, string expected)
    {
        var mimeType = contentType switch
        {
            "sdmodel" => StrideContentTypes.Model,
            "sdtex" => StrideContentTypes.Texture,
            "sdanim" => StrideContentTypes.Animation,
            "sdmat" => StrideContentTypes.Material,
            _ => "application/octet-stream"
        };

        var result = StrideContentTypes.ToExtension(mimeType);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FromExtension_CaseInsensitive()
    {
        Assert.Equal(StrideContentTypes.Model, StrideContentTypes.FromExtension(".SDMODEL"));
        Assert.Equal(StrideContentTypes.Model, StrideContentTypes.FromExtension(".SdModel"));
        Assert.Equal(StrideContentTypes.Texture, StrideContentTypes.FromExtension(".SDTEX"));
    }
}
