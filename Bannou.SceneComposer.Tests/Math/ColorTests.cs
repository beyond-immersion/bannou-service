using BeyondImmersion.Bannou.SceneComposer.Math;
using Xunit;

namespace BeyondImmersion.Bannou.SceneComposer.Tests.Math;

/// <summary>
/// Tests for the Color struct.
/// </summary>
public class ColorTests
{
    // =========================================================================
    // STATIC COLORS
    // =========================================================================

    [Fact]
    public void White_HasCorrectComponents()
    {
        var white = Color.White;

        Assert.Equal(255, white.R);
        Assert.Equal(255, white.G);
        Assert.Equal(255, white.B);
        Assert.Equal(255, white.A);
    }

    [Fact]
    public void Black_HasCorrectComponents()
    {
        var black = Color.Black;

        Assert.Equal(0, black.R);
        Assert.Equal(0, black.G);
        Assert.Equal(0, black.B);
        Assert.Equal(255, black.A);
    }

    [Fact]
    public void Red_HasCorrectComponents()
    {
        var red = Color.Red;

        Assert.Equal(255, red.R);
        Assert.Equal(0, red.G);
        Assert.Equal(0, red.B);
    }

    [Fact]
    public void Green_HasCorrectComponents()
    {
        var green = Color.Green;

        Assert.Equal(0, green.R);
        Assert.Equal(255, green.G);
        Assert.Equal(0, green.B);
    }

    [Fact]
    public void Blue_HasCorrectComponents()
    {
        var blue = Color.Blue;

        Assert.Equal(0, blue.R);
        Assert.Equal(0, blue.G);
        Assert.Equal(255, blue.B);
    }

    [Fact]
    public void Transparent_HasZeroAlpha()
    {
        var transparent = Color.Transparent;

        Assert.Equal(0, transparent.A);
    }

    // =========================================================================
    // CONSTRUCTION
    // =========================================================================

    [Fact]
    public void Constructor_SetsComponents()
    {
        var color = new Color(100, 150, 200, 128);

        Assert.Equal(100, color.R);
        Assert.Equal(150, color.G);
        Assert.Equal(200, color.B);
        Assert.Equal(128, color.A);
    }

    [Fact]
    public void Constructor_DefaultsAlphaTo255()
    {
        var color = new Color(100, 150, 200);

        Assert.Equal(255, color.A);
    }

    // =========================================================================
    // FROM FLOAT
    // =========================================================================

    [Fact]
    public void FromFloat_ConvertsNormalizedValues()
    {
        var color = Color.FromFloat(1f, 0.5f, 0f);

        Assert.Equal(255, color.R);
        Assert.Equal(127, color.G); // 0.5 * 255 = 127.5, truncated to 127
        Assert.Equal(0, color.B);
        Assert.Equal(255, color.A);
    }

    [Fact]
    public void FromFloat_ClampsOutOfRangeValues()
    {
        var color = Color.FromFloat(2f, -0.5f, 0.5f);

        Assert.Equal(255, color.R); // Clamped from 2.0
        Assert.Equal(0, color.G);   // Clamped from -0.5
        Assert.Equal(127, color.B);
    }

    [Fact]
    public void FromFloat_WithAlpha()
    {
        var color = Color.FromFloat(1f, 1f, 1f, 0.5f);

        Assert.Equal(127, color.A);
    }

    // =========================================================================
    // FROM HSV
    // =========================================================================

    [Fact]
    public void FromHSV_Red_CorrectColor()
    {
        var color = Color.FromHSV(0, 1f, 1f);

        Assert.Equal(255, color.R);
        Assert.Equal(0, color.G);
        Assert.Equal(0, color.B);
    }

    [Fact]
    public void FromHSV_Green_CorrectColor()
    {
        var color = Color.FromHSV(120, 1f, 1f);

        Assert.Equal(0, color.R);
        Assert.Equal(255, color.G);
        Assert.Equal(0, color.B);
    }

    [Fact]
    public void FromHSV_Blue_CorrectColor()
    {
        var color = Color.FromHSV(240, 1f, 1f);

        Assert.Equal(0, color.R);
        Assert.Equal(0, color.G);
        Assert.Equal(255, color.B);
    }

    [Fact]
    public void FromHSV_Gray_WhenSaturationIsZero()
    {
        var color = Color.FromHSV(0, 0f, 0.5f);

        Assert.Equal(color.R, color.G);
        Assert.Equal(color.G, color.B);
        Assert.Equal(127, color.R);
    }

    [Fact]
    public void FromHSV_Black_WhenValueIsZero()
    {
        var color = Color.FromHSV(180, 1f, 0f);

        Assert.Equal(0, color.R);
        Assert.Equal(0, color.G);
        Assert.Equal(0, color.B);
    }

    [Fact]
    public void FromHSV_HandlesNegativeHue()
    {
        var color1 = Color.FromHSV(-60, 1f, 1f);
        var color2 = Color.FromHSV(300, 1f, 1f);

        Assert.Equal(color1, color2);
    }

    [Fact]
    public void FromHSV_HandlesHueOver360()
    {
        var color1 = Color.FromHSV(420, 1f, 1f);  // 420 - 360 = 60
        var color2 = Color.FromHSV(60, 1f, 1f);

        Assert.Equal(color1, color2);
    }

    // =========================================================================
    // FROM HEX
    // =========================================================================

    [Fact]
    public void FromHex_ParsesSixDigitHex()
    {
        var color = Color.FromHex("FF8040");

        Assert.Equal(255, color.R);
        Assert.Equal(128, color.G);
        Assert.Equal(64, color.B);
        Assert.Equal(255, color.A);
    }

    [Fact]
    public void FromHex_ParsesEightDigitHex()
    {
        var color = Color.FromHex("FF804080");

        Assert.Equal(255, color.R);
        Assert.Equal(128, color.G);
        Assert.Equal(64, color.B);
        Assert.Equal(128, color.A);
    }

    [Fact]
    public void FromHex_HandlesHashPrefix()
    {
        var color = Color.FromHex("#FF0000");

        Assert.Equal(Color.Red.R, color.R);
        Assert.Equal(Color.Red.G, color.G);
        Assert.Equal(Color.Red.B, color.B);
    }

    [Fact]
    public void FromHex_ThrowsOnInvalidFormat()
    {
        Assert.Throws<ArgumentException>(() => Color.FromHex("FFFFF")); // 5 chars
        Assert.Throws<ArgumentException>(() => Color.FromHex("FFFFFFF")); // 7 chars
    }

    // =========================================================================
    // TO HEX
    // =========================================================================

    [Fact]
    public void ToHex_ReturnsCorrectFormat()
    {
        var color = new Color(255, 128, 64);

        var hex = color.ToHex();

        Assert.Equal("#FF8040", hex);
    }

    [Fact]
    public void ToHex_WithAlpha_IncludesAlphaChannel()
    {
        var color = new Color(255, 128, 64, 128);

        var hex = color.ToHex(includeAlpha: true);

        Assert.Equal("#FF804080", hex);
    }

    [Fact]
    public void ToHex_Roundtrip()
    {
        var original = new Color(123, 45, 67, 89);

        var hex = original.ToHex(includeAlpha: true);
        var parsed = Color.FromHex(hex);

        Assert.Equal(original, parsed);
    }

    // =========================================================================
    // TO FLOAT
    // =========================================================================

    [Fact]
    public void ToFloat_ReturnsNormalizedValues()
    {
        var color = new Color(255, 128, 0, 255);

        var (r, g, b, a) = color.ToFloat();

        Assert.Equal(1f, r, 0.01f);
        Assert.Equal(0.5f, g, 0.01f);
        Assert.Equal(0f, b, 0.01f);
        Assert.Equal(1f, a, 0.01f);
    }

    // =========================================================================
    // WITH ALPHA
    // =========================================================================

    [Fact]
    public void WithAlpha_ReturnsNewColorWithModifiedAlpha()
    {
        var original = Color.Red;

        var modified = original.WithAlpha(128);

        Assert.Equal(255, modified.R);
        Assert.Equal(0, modified.G);
        Assert.Equal(0, modified.B);
        Assert.Equal(128, modified.A);
    }

    [Fact]
    public void WithAlpha_OriginalUnchanged()
    {
        var original = Color.Red;

        original.WithAlpha(128);

        Assert.Equal(255, original.A);
    }

    // =========================================================================
    // LERP
    // =========================================================================

    [Fact]
    public void Lerp_AtZero_ReturnsFirst()
    {
        var result = Color.Lerp(Color.Black, Color.White, 0);

        Assert.Equal(Color.Black, result);
    }

    [Fact]
    public void Lerp_AtOne_ReturnsSecond()
    {
        var result = Color.Lerp(Color.Black, Color.White, 1);

        Assert.Equal(Color.White, result);
    }

    [Fact]
    public void Lerp_AtHalf_Interpolates()
    {
        var result = Color.Lerp(Color.Black, Color.White, 0.5f);

        Assert.Equal(127, result.R);
        Assert.Equal(127, result.G);
        Assert.Equal(127, result.B);
    }

    [Fact]
    public void Lerp_ClampsT()
    {
        var underflow = Color.Lerp(Color.Black, Color.White, -1f);
        var overflow = Color.Lerp(Color.Black, Color.White, 2f);

        Assert.Equal(Color.Black, underflow);
        Assert.Equal(Color.White, overflow);
    }

    // =========================================================================
    // EQUALITY
    // =========================================================================

    [Fact]
    public void Equals_SameValues_ReturnsTrue()
    {
        var a = new Color(100, 150, 200, 128);
        var b = new Color(100, 150, 200, 128);

        Assert.True(a.Equals(b));
        Assert.True(a == b);
    }

    [Fact]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        var a = new Color(100, 150, 200, 128);
        var b = new Color(100, 150, 200, 129);

        Assert.False(a.Equals(b));
        Assert.True(a != b);
    }

    [Fact]
    public void GetHashCode_SameValues_SameHash()
    {
        var a = new Color(100, 150, 200, 128);
        var b = new Color(100, 150, 200, 128);

        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    // =========================================================================
    // TO STRING
    // =========================================================================

    [Fact]
    public void ToString_ReturnsFormattedString()
    {
        var color = new Color(100, 150, 200, 128);

        var str = color.ToString();

        Assert.Contains("Color", str);
        Assert.Contains("100", str);
        Assert.Contains("150", str);
        Assert.Contains("200", str);
        Assert.Contains("128", str);
    }
}
