namespace BeyondImmersion.Bannou.SpriteTheory;

/// <summary>
/// Represents an integer rectangle defined by position and size, used for atlas frame positions.
/// </summary>
/// <remarks>
/// A 16-byte value type (four ints). Position (X, Y) is the top-left corner.
/// Width and Height define the extent in pixels.
/// </remarks>
public readonly struct Rectangle : IEquatable<Rectangle>
{
    /// <summary>Horizontal position of the top-left corner in pixels.</summary>
    public int X { get; }

    /// <summary>Vertical position of the top-left corner in pixels.</summary>
    public int Y { get; }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>
    /// Creates a new rectangle with the specified position and dimensions.
    /// </summary>
    /// <param name="x">Horizontal position of the top-left corner.</param>
    /// <param name="y">Vertical position of the top-left corner.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    public Rectangle(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <inheritdoc />
    public bool Equals(Rectangle other) =>
        X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Rectangle other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(Rectangle left, Rectangle right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(Rectangle left, Rectangle right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => $"Rectangle(X={X}, Y={Y}, W={Width}, H={Height})";
}
