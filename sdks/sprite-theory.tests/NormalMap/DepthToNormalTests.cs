using BeyondImmersion.Bannou.SpriteTheory.NormalMap;
using Xunit;

namespace BeyondImmersion.Bannou.SpriteTheory.Tests.NormalMap;

public class DepthToNormalTests
{
    [Fact]
    public void Generate_FlatDepth_NeutralBlueNormals()
    {
        var width = 4;
        var height = 4;
        var depth = new float[width * height];
        Array.Fill(depth, 0.5f);
        var options = new NormalMapOptions(Strength: 1.0f, BlurRadius: 0);

        var normals = DepthToNormal.Generate(depth, width, height, options);

        // Flat surface facing camera should produce neutral normals: (0, 0, 1) encoded as (~128, ~128, ~255)
        for (var i = 0; i < width * height; i++)
        {
            var r = normals[i * 4 + 0];
            var g = normals[i * 4 + 1];
            var b = normals[i * 4 + 2];
            var a = normals[i * 4 + 3];

            // Neutral normal encoded: nx=0 → 128, ny=0 → 128, nz=1 → 255
            Assert.True(MathF.Abs(r - 128) <= 1,
                $"Pixel {i}: R={r}, expected ~128 for flat surface");
            Assert.True(MathF.Abs(g - 128) <= 1,
                $"Pixel {i}: G={g}, expected ~128 for flat surface");
            Assert.True(MathF.Abs(b - 255) <= 1,
                $"Pixel {i}: B={b}, expected ~255 for flat surface");
            Assert.Equal(255, a);
        }
    }

    [Fact]
    public void Generate_InvalidDepthLength_ThrowsArgumentException()
    {
        var depth = new float[10]; // Not 4x4 = 16
        var options = new NormalMapOptions();

        Assert.Throws<ArgumentException>(() =>
            DepthToNormal.Generate(depth, 4, 4, options));
    }

    [Fact]
    public void Generate_SinglePixel_ReturnsNeutralNormal()
    {
        var depth = new float[] { 0.5f };
        var options = new NormalMapOptions(Strength: 1.0f, BlurRadius: 0);

        var normals = DepthToNormal.Generate(depth, 1, 1, options);

        Assert.Equal(4, normals.Length);
        // 1x1 pixel: all Sobel neighbors clamp to the same value, so gradients are 0
        // Normal = (0, 0, 1), encoded as (128, 128, 255)
        Assert.True(MathF.Abs(normals[0] - 128) <= 1, $"R={normals[0]}, expected ~128");
        Assert.True(MathF.Abs(normals[1] - 128) <= 1, $"G={normals[1]}, expected ~128");
        Assert.True(MathF.Abs(normals[2] - 255) <= 1, $"B={normals[2]}, expected ~255");
        Assert.Equal(255, normals[3]);
    }

    [Fact]
    public void Generate_HorizontalGradient_XComponentVariation()
    {
        var width = 8;
        var height = 1;
        var depth = new float[width * height];
        // Depth increasing from left to right
        for (var x = 0; x < width; x++)
            depth[x] = (float)x / (width - 1);
        var options = new NormalMapOptions(Strength: 1.0f, BlurRadius: 0);

        var normals = DepthToNormal.Generate(depth, width, height, options);

        // Interior pixels should have X-component (R channel) different from neutral 128
        // because there's a positive horizontal gradient
        var centerPixel = width / 2;
        var rCenter = normals[centerPixel * 4 + 0];
        // With positive dz/dx, nx = -dzdx/len should be < 0, encoded as < 128
        Assert.True(rCenter != 128,
            $"Center pixel R={rCenter}, expected non-neutral value for horizontal gradient");
    }

    [Fact]
    public void Generate_VerticalGradient_YComponentVariation()
    {
        var width = 1;
        var height = 8;
        var depth = new float[width * height];
        // Depth increasing from top to bottom
        for (var y = 0; y < height; y++)
            depth[y] = (float)y / (height - 1);
        var options = new NormalMapOptions(Strength: 1.0f, BlurRadius: 0);

        var normals = DepthToNormal.Generate(depth, width, height, options);

        // Interior pixels should have Y-component (G channel) different from neutral 128
        var centerPixel = height / 2;
        var gCenter = normals[centerPixel * 4 + 1];
        Assert.True(gCenter != 128,
            $"Center pixel G={gCenter}, expected non-neutral value for vertical gradient");
    }

    [Fact]
    public void Generate_StrengthMultiplier_AffectsIntensity()
    {
        var width = 8;
        var height = 8;
        var depth = new float[width * height];
        // Create a gradient: depth increases to the right
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                depth[y * width + x] = (float)x / (width - 1);

        var optionsLow = new NormalMapOptions(Strength: 1.0f, BlurRadius: 0);
        var optionsHigh = new NormalMapOptions(Strength: 2.0f, BlurRadius: 0);

        var normalsLow = DepthToNormal.Generate(depth, width, height, optionsLow);
        var normalsHigh = DepthToNormal.Generate(depth, width, height, optionsHigh);

        // Compare interior pixel R-channel deviation from neutral (128)
        // Higher strength should produce more deviation
        var centerIdx = (height / 2 * width + width / 2) * 4;
        var deviationLow = MathF.Abs(normalsLow[centerIdx] - 128f);
        var deviationHigh = MathF.Abs(normalsHigh[centerIdx] - 128f);

        Assert.True(deviationHigh > deviationLow,
            $"Strength 2.0 deviation ({deviationHigh}) should exceed strength 1.0 deviation ({deviationLow})");
    }

    [Fact]
    public void Generate_OutputSize_IsCorrect()
    {
        var width = 16;
        var height = 12;
        var depth = new float[width * height];
        var options = new NormalMapOptions();

        var normals = DepthToNormal.Generate(depth, width, height, options);

        Assert.Equal(width * height * 4, normals.Length);
    }

    [Fact]
    public void Generate_AlphaAlways255()
    {
        var width = 8;
        var height = 8;
        var depth = new float[width * height];
        // Varying depth values
        for (var i = 0; i < depth.Length; i++)
            depth[i] = (float)i / depth.Length;
        var options = new NormalMapOptions(Strength: 2.0f, BlurRadius: 0);

        var normals = DepthToNormal.Generate(depth, width, height, options);

        for (var i = 0; i < width * height; i++)
        {
            Assert.Equal(255, normals[i * 4 + 3]);
        }
    }

    [Fact]
    public void Generate_Deterministic_SameInputSameOutput()
    {
        var width = 8;
        var height = 8;
        var depth = new float[width * height];
        for (var i = 0; i < depth.Length; i++)
            depth[i] = MathF.Sin(i * 0.5f) * 0.5f + 0.5f;
        var options = new NormalMapOptions(Strength: 1.5f, BlurRadius: 0);

        var result1 = DepthToNormal.Generate(depth, width, height, options);
        var result2 = DepthToNormal.Generate(depth, width, height, options);

        Assert.Equal(result1.Length, result2.Length);
        for (var i = 0; i < result1.Length; i++)
        {
            Assert.Equal(result1[i], result2[i]);
        }
    }

    [Fact]
    public void Generate_BlurRadius_ChangesOutput()
    {
        var width = 16;
        var height = 16;
        var depth = new float[width * height];
        // Create sharp depth edges
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                depth[y * width + x] = x < width / 2 ? 0.0f : 1.0f;

        var optionsNoBlur = new NormalMapOptions(Strength: 1.0f, BlurRadius: 0);
        var optionsBlur = new NormalMapOptions(Strength: 1.0f, BlurRadius: 2);

        var normalsNoBlur = DepthToNormal.Generate(depth, width, height, optionsNoBlur);
        var normalsBlur = DepthToNormal.Generate(depth, width, height, optionsBlur);

        // Blurring should produce different normals at the edge
        var hasDifference = false;
        for (var i = 0; i < normalsNoBlur.Length; i++)
        {
            if (normalsNoBlur[i] != normalsBlur[i])
            {
                hasDifference = true;
                break;
            }
        }

        Assert.True(hasDifference,
            "Blur radius > 0 should produce different output than blur radius 0 on a sharp edge");
    }
}
