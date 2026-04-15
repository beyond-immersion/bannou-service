namespace BeyondImmersion.Bannou.SpriteTheory;

/// <summary>
/// Represents a 2D floating-point vector, used for pivot points and 2D positions.
/// </summary>
/// <remarks>
/// An 8-byte value type (two floats). This is the SDK's own Vector2 — do not confuse
/// with <c>System.Numerics.Vector2</c>, which is not used in this SDK.
/// </remarks>
public readonly struct Vector2 : IEquatable<Vector2>
{
    /// <summary>The X component.</summary>
    public float X { get; }

    /// <summary>The Y component.</summary>
    public float Y { get; }

    /// <summary>
    /// Creates a new 2D vector with the specified components.
    /// </summary>
    /// <param name="x">The X component.</param>
    /// <param name="y">The Y component.</param>
    public Vector2(float x, float y)
    {
        X = x;
        Y = y;
    }

    /// <inheritdoc />
    public bool Equals(Vector2 other) =>
        X.Equals(other.X) && Y.Equals(other.Y);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Vector2 other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(Vector2 left, Vector2 right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(Vector2 left, Vector2 right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => $"Vector2(X={X}, Y={Y})";
}
