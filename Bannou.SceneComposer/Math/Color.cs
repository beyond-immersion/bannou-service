namespace BeyondImmersion.Bannou.SceneComposer.Math;

/// <summary>
/// Engine-agnostic RGBA color.
/// Components are 0-255 byte values.
/// </summary>
public readonly struct Color : IEquatable<Color>
{
    // Common colors
    public static readonly Color White = new(255, 255, 255);
    public static readonly Color Black = new(0, 0, 0);
    public static readonly Color Red = new(255, 0, 0);
    public static readonly Color Green = new(0, 255, 0);
    public static readonly Color Blue = new(0, 0, 255);
    public static readonly Color Yellow = new(255, 255, 0);
    public static readonly Color Cyan = new(0, 255, 255);
    public static readonly Color Magenta = new(255, 0, 255);
    public static readonly Color Transparent = new(0, 0, 0, 0);

    // Gizmo standard colors (RGB = XYZ convention)
    public static readonly Color AxisX = new(255, 80, 80);
    public static readonly Color AxisY = new(80, 255, 80);
    public static readonly Color AxisZ = new(80, 80, 255);
    public static readonly Color Highlight = new(255, 255, 100);

    public byte R { get; }
    public byte G { get; }
    public byte B { get; }
    public byte A { get; }

    public Color(byte r, byte g, byte b, byte a = 255)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    /// <summary>
    /// Create color from normalized float values (0.0 - 1.0).
    /// </summary>
    public static Color FromFloat(float r, float g, float b, float a = 1f) =>
        new(
            (byte)(System.Math.Clamp(r, 0f, 1f) * 255),
            (byte)(System.Math.Clamp(g, 0f, 1f) * 255),
            (byte)(System.Math.Clamp(b, 0f, 1f) * 255),
            (byte)(System.Math.Clamp(a, 0f, 1f) * 255));

    /// <summary>
    /// Create color from HSV values.
    /// </summary>
    /// <param name="h">Hue (0-360)</param>
    /// <param name="s">Saturation (0-1)</param>
    /// <param name="v">Value (0-1)</param>
    public static Color FromHSV(float h, float s, float v)
    {
        h = ((h % 360) + 360) % 360;
        s = System.Math.Clamp(s, 0f, 1f);
        v = System.Math.Clamp(v, 0f, 1f);

        var c = v * s;
        var x = c * (1 - System.Math.Abs((h / 60f) % 2 - 1));
        var m = v - c;

        float r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return FromFloat(r + m, g + m, b + m);
    }

    /// <summary>
    /// Create color from hex string (e.g., "#FF0000" or "FF0000").
    /// </summary>
    public static Color FromHex(string hex)
    {
        hex = hex.TrimStart('#');

        if (hex.Length == 6)
        {
            return new Color(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16));
        }

        if (hex.Length == 8)
        {
            return new Color(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16),
                Convert.ToByte(hex[6..8], 16));
        }

        throw new ArgumentException($"Invalid hex color format: {hex}");
    }

    /// <summary>
    /// Convert to hex string.
    /// </summary>
    public string ToHex(bool includeAlpha = false) =>
        includeAlpha
            ? $"#{R:X2}{G:X2}{B:X2}{A:X2}"
            : $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>
    /// Get normalized float values (0.0 - 1.0).
    /// </summary>
    public (float r, float g, float b, float a) ToFloat() =>
        (R / 255f, G / 255f, B / 255f, A / 255f);

    /// <summary>
    /// Create a new color with modified alpha.
    /// </summary>
    public Color WithAlpha(byte alpha) =>
        new(R, G, B, alpha);

    /// <summary>
    /// Linear interpolation between two colors.
    /// </summary>
    public static Color Lerp(Color a, Color b, float t)
    {
        t = System.Math.Clamp(t, 0f, 1f);
        return new Color(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t),
            (byte)(a.A + (b.A - a.A) * t));
    }

    public bool Equals(Color other) =>
        R == other.R && G == other.G && B == other.B && A == other.A;

    public override bool Equals(object? obj) =>
        obj is Color other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(R, G, B, A);

    public static bool operator ==(Color left, Color right) =>
        left.Equals(right);

    public static bool operator !=(Color left, Color right) =>
        !left.Equals(right);

    public override string ToString() =>
        $"Color({R}, {G}, {B}, {A})";
}
