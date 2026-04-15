namespace BeyondImmersion.Bannou.SpriteTheory;

/// <summary>
/// Represents an RGBA color with byte-valued channels (0–255 per channel).
/// </summary>
/// <remarks>
/// A 4-byte value type used for atlas background colors and render clear colors.
/// Provides static factory properties for common colors used in sprite capture.
/// </remarks>
public readonly struct Color : IEquatable<Color>
{
    /// <summary>Red channel (0–255).</summary>
    public byte R { get; }

    /// <summary>Green channel (0–255).</summary>
    public byte G { get; }

    /// <summary>Blue channel (0–255).</summary>
    public byte B { get; }

    /// <summary>Alpha channel (0–255), where 0 is fully transparent and 255 is fully opaque.</summary>
    public byte A { get; }

    /// <summary>
    /// Creates a new color with the specified RGBA channel values.
    /// </summary>
    /// <param name="r">Red channel (0–255).</param>
    /// <param name="g">Green channel (0–255).</param>
    /// <param name="b">Blue channel (0–255).</param>
    /// <param name="a">Alpha channel (0–255).</param>
    public Color(byte r, byte g, byte b, byte a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    /// <summary>Fully transparent (0, 0, 0, 0). Default atlas background for sprite capture.</summary>
    public static Color Transparent => new(0, 0, 0, 0);

    /// <summary>Opaque white (255, 255, 255, 255).</summary>
    public static Color White => new(255, 255, 255, 255);

    /// <summary>Opaque black (0, 0, 0, 255).</summary>
    public static Color Black => new(0, 0, 0, 255);

    /// <inheritdoc />
    public bool Equals(Color other) =>
        R == other.R && G == other.G && B == other.B && A == other.A;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Color other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(R, G, B, A);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(Color left, Color right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(Color left, Color right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => $"Color(R={R}, G={G}, B={B}, A={A})";
}
