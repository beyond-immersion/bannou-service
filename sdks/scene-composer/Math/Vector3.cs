namespace BeyondImmersion.Bannou.SceneComposer.Math;

/// <summary>
/// Engine-agnostic 3D vector type for scene composition.
/// Uses double precision for accuracy in large worlds.
/// </summary>
public readonly struct Vector3 : IEquatable<Vector3>
{
    public static readonly Vector3 Zero = new(0, 0, 0);
    public static readonly Vector3 One = new(1, 1, 1);
    public static readonly Vector3 UnitX = new(1, 0, 0);
    public static readonly Vector3 UnitY = new(0, 1, 0);
    public static readonly Vector3 UnitZ = new(0, 0, 1);

    public double X { get; }
    public double Y { get; }
    public double Z { get; }

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

    public static Vector3 operator +(Vector3 a, Vector3 b) =>
        new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vector3 operator -(Vector3 a, Vector3 b) =>
        new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vector3 operator *(Vector3 v, double scalar) =>
        new(v.X * scalar, v.Y * scalar, v.Z * scalar);

    public static Vector3 operator *(double scalar, Vector3 v) =>
        new(v.X * scalar, v.Y * scalar, v.Z * scalar);

    public static Vector3 operator /(Vector3 v, double scalar) =>
        new(v.X / scalar, v.Y / scalar, v.Z / scalar);

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

    public bool Equals(Vector3 other) =>
        System.Math.Abs(X - other.X) < double.Epsilon &&
        System.Math.Abs(Y - other.Y) < double.Epsilon &&
        System.Math.Abs(Z - other.Z) < double.Epsilon;

    public override bool Equals(object? obj) =>
        obj is Vector3 other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(X, Y, Z);

    public static bool operator ==(Vector3 left, Vector3 right) =>
        left.Equals(right);

    public static bool operator !=(Vector3 left, Vector3 right) =>
        !left.Equals(right);

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
