namespace BeyondImmersion.Bannou.SceneComposer.Math;

/// <summary>
/// Engine-agnostic 3D vector type for scene composition.
/// Uses double precision for accuracy in large worlds.
/// </summary>
public readonly struct Vector3 : IEquatable<Vector3>
{
    /// <summary>Zero vector (0, 0, 0).</summary>
    public static readonly Vector3 Zero = new(0, 0, 0);
    /// <summary>One vector (1, 1, 1).</summary>
    public static readonly Vector3 One = new(1, 1, 1);
    /// <summary>Unit X vector (1, 0, 0).</summary>
    public static readonly Vector3 UnitX = new(1, 0, 0);
    /// <summary>Unit Y vector (0, 1, 0).</summary>
    public static readonly Vector3 UnitY = new(0, 1, 0);
    /// <summary>Unit Z vector (0, 0, 1).</summary>
    public static readonly Vector3 UnitZ = new(0, 0, 1);

    /// <summary>X component.</summary>
    public double X { get; }
    /// <summary>Y component.</summary>
    public double Y { get; }
    /// <summary>Z component.</summary>
    public double Z { get; }

    /// <summary>Creates a 3D vector from XYZ components.</summary>
    public Vector3(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>
    /// Squared length (avoids sqrt for comparison operations).
    /// </summary>
    public double LengthSquared => X * X + Y * Y + Z * Z;

    /// <summary>
    /// Length of the vector.
    /// </summary>
    public double Length => System.Math.Sqrt(LengthSquared);

    /// <summary>
    /// Returns a normalized version of this vector.
    /// </summary>
    public Vector3 Normalized
    {
        get
        {
            var len = Length;
            if (len < double.Epsilon) return Zero;
            return new Vector3(X / len, Y / len, Z / len);
        }
    }

    /// <summary>Adds two vectors.</summary>
    public static Vector3 operator +(Vector3 a, Vector3 b) =>
        new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    /// <summary>Subtracts one vector from another.</summary>
    public static Vector3 operator -(Vector3 a, Vector3 b) =>
        new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    /// <summary>Multiplies a vector by a scalar.</summary>
    public static Vector3 operator *(Vector3 v, double scalar) =>
        new(v.X * scalar, v.Y * scalar, v.Z * scalar);

    /// <summary>Multiplies a scalar by a vector.</summary>
    public static Vector3 operator *(double scalar, Vector3 v) =>
        new(v.X * scalar, v.Y * scalar, v.Z * scalar);

    /// <summary>Divides a vector by a scalar.</summary>
    public static Vector3 operator /(Vector3 v, double scalar) =>
        new(v.X / scalar, v.Y / scalar, v.Z / scalar);

    /// <summary>Negates a vector.</summary>
    public static Vector3 operator -(Vector3 v) =>
        new(-v.X, -v.Y, -v.Z);

    /// <summary>
    /// Dot product of two vectors.
    /// </summary>
    public static double Dot(Vector3 a, Vector3 b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary>
    /// Cross product of two vectors.
    /// </summary>
    public static Vector3 Cross(Vector3 a, Vector3 b) =>
        new(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);

    /// <summary>
    /// Linear interpolation between two vectors.
    /// </summary>
    public static Vector3 Lerp(Vector3 a, Vector3 b, double t) =>
        new(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t);

    /// <summary>
    /// Distance between two points.
    /// </summary>
    public static double Distance(Vector3 a, Vector3 b) =>
        (b - a).Length;

    /// <summary>
    /// Component-wise minimum.
    /// </summary>
    public static Vector3 Min(Vector3 a, Vector3 b) =>
        new(
            System.Math.Min(a.X, b.X),
            System.Math.Min(a.Y, b.Y),
            System.Math.Min(a.Z, b.Z));

    /// <summary>
    /// Component-wise maximum.
    /// </summary>
    public static Vector3 Max(Vector3 a, Vector3 b) =>
        new(
            System.Math.Max(a.X, b.X),
            System.Math.Max(a.Y, b.Y),
            System.Math.Max(a.Z, b.Z));

    /// <inheritdoc />
    public bool Equals(Vector3 other) =>
        System.Math.Abs(X - other.X) < double.Epsilon &&
        System.Math.Abs(Y - other.Y) < double.Epsilon &&
        System.Math.Abs(Z - other.Z) < double.Epsilon;

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is Vector3 other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(X, Y, Z);

    /// <summary>Tests equality between two vectors.</summary>
    public static bool operator ==(Vector3 left, Vector3 right) =>
        left.Equals(right);

    /// <summary>Tests inequality between two vectors.</summary>
    public static bool operator !=(Vector3 left, Vector3 right) =>
        !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() =>
        $"({X:F3}, {Y:F3}, {Z:F3})";

    /// <summary>
    /// Deconstruct for pattern matching.
    /// </summary>
    public void Deconstruct(out double x, out double y, out double z)
    {
        x = X;
        y = Y;
        z = Z;
    }
}
