namespace BeyondImmersion.Bannou.Core.Math;

/// <summary>
/// Engine-agnostic 3D floating-point vector shared across Bannou SDKs.
/// </summary>
/// <remarks>
/// <para>
/// A 12-byte value type (three floats). Single-precision to match graphics-pipeline
/// conventions used by sprite-theory, bridges (Stride, Godot, Unity), and GPU math.
/// The scene-composer SDK defines its own <c>SceneComposer.Math.Vector3</c> with
/// double precision for large-world accuracy — that is a separate type and does
/// not conflict with this one.
/// </para>
/// <para>
/// Equality is exact (bit-pattern) rather than epsilon-based so the type is usable
/// as a dictionary key and satisfies the <c>Equals</c>/<c>GetHashCode</c> contract.
/// Callers that need tolerance-based comparisons should compute per-component
/// differences explicitly.
/// </para>
/// </remarks>
public readonly struct Vector3 : IEquatable<Vector3>
{
    /// <summary>Zero vector (0, 0, 0).</summary>
    public static readonly Vector3 Zero = new(0f, 0f, 0f);

    /// <summary>Unit vector (1, 1, 1) — all components set to one.</summary>
    public static readonly Vector3 One = new(1f, 1f, 1f);

    /// <summary>Unit X axis (1, 0, 0).</summary>
    public static readonly Vector3 UnitX = new(1f, 0f, 0f);

    /// <summary>Unit Y axis (0, 1, 0).</summary>
    public static readonly Vector3 UnitY = new(0f, 1f, 0f);

    /// <summary>Unit Z axis (0, 0, 1).</summary>
    public static readonly Vector3 UnitZ = new(0f, 0f, 1f);

    /// <summary>X component.</summary>
    public float X { get; }

    /// <summary>Y component.</summary>
    public float Y { get; }

    /// <summary>Z component.</summary>
    public float Z { get; }

    /// <summary>
    /// Creates a 3D vector from its three floating-point components.
    /// </summary>
    /// <param name="x">X component.</param>
    /// <param name="y">Y component.</param>
    /// <param name="z">Z component.</param>
    public Vector3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>
    /// Squared length of the vector. Prefer this over <see cref="Length"/> for
    /// comparison operations — it avoids the square root.
    /// </summary>
    public float LengthSquared => X * X + Y * Y + Z * Z;

    /// <summary>
    /// Euclidean length of the vector.
    /// </summary>
    public float Length => MathF.Sqrt(LengthSquared);

    /// <summary>
    /// Returns a unit-length copy of the vector, or <see cref="Zero"/> when the
    /// vector is too small to normalize without producing NaN/Inf.
    /// </summary>
    public Vector3 Normalized
    {
        get
        {
            var len = Length;
            if (len < 1e-10f)
            {
                return Zero;
            }

            return new Vector3(X / len, Y / len, Z / len);
        }
    }

    /// <summary>Component-wise addition.</summary>
    public static Vector3 operator +(Vector3 a, Vector3 b) =>
        new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    /// <summary>Component-wise subtraction.</summary>
    public static Vector3 operator -(Vector3 a, Vector3 b) =>
        new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    /// <summary>Scalar multiplication.</summary>
    public static Vector3 operator *(Vector3 v, float scalar) =>
        new(v.X * scalar, v.Y * scalar, v.Z * scalar);

    /// <summary>Scalar multiplication (scalar first).</summary>
    public static Vector3 operator *(float scalar, Vector3 v) =>
        new(v.X * scalar, v.Y * scalar, v.Z * scalar);

    /// <summary>Scalar division.</summary>
    public static Vector3 operator /(Vector3 v, float scalar) =>
        new(v.X / scalar, v.Y / scalar, v.Z / scalar);

    /// <summary>Unary negation.</summary>
    public static Vector3 operator -(Vector3 v) =>
        new(-v.X, -v.Y, -v.Z);

    /// <summary>
    /// Dot product of two vectors.
    /// </summary>
    /// <param name="a">First vector.</param>
    /// <param name="b">Second vector.</param>
    /// <returns>Scalar dot product.</returns>
    public static float Dot(Vector3 a, Vector3 b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary>
    /// Cross product of two vectors.
    /// </summary>
    /// <param name="a">First vector.</param>
    /// <param name="b">Second vector.</param>
    /// <returns>Vector perpendicular to both <paramref name="a"/> and <paramref name="b"/>.</returns>
    public static Vector3 Cross(Vector3 a, Vector3 b) =>
        new(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);

    /// <summary>
    /// Linear interpolation between <paramref name="a"/> (at <c>t = 0</c>) and
    /// <paramref name="b"/> (at <c>t = 1</c>). The <paramref name="t"/> parameter
    /// is not clamped — callers may extrapolate beyond the <c>[0, 1]</c> range.
    /// </summary>
    /// <param name="a">Start vector.</param>
    /// <param name="b">End vector.</param>
    /// <param name="t">Interpolation parameter.</param>
    /// <returns>Interpolated vector.</returns>
    public static Vector3 Lerp(Vector3 a, Vector3 b, float t) =>
        new(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t);

    /// <summary>
    /// Euclidean distance between two points.
    /// </summary>
    /// <param name="a">First point.</param>
    /// <param name="b">Second point.</param>
    /// <returns>Distance between the two points.</returns>
    public static float Distance(Vector3 a, Vector3 b) =>
        (b - a).Length;

    /// <summary>
    /// Component-wise minimum of two vectors.
    /// </summary>
    /// <param name="a">First vector.</param>
    /// <param name="b">Second vector.</param>
    /// <returns>Vector whose components are the smaller of the two inputs.</returns>
    public static Vector3 Min(Vector3 a, Vector3 b) =>
        new(
            MathF.Min(a.X, b.X),
            MathF.Min(a.Y, b.Y),
            MathF.Min(a.Z, b.Z));

    /// <summary>
    /// Component-wise maximum of two vectors.
    /// </summary>
    /// <param name="a">First vector.</param>
    /// <param name="b">Second vector.</param>
    /// <returns>Vector whose components are the larger of the two inputs.</returns>
    public static Vector3 Max(Vector3 a, Vector3 b) =>
        new(
            MathF.Max(a.X, b.X),
            MathF.Max(a.Y, b.Y),
            MathF.Max(a.Z, b.Z));

    /// <inheritdoc />
    public bool Equals(Vector3 other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is Vector3 other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    /// <summary>Exact equality operator.</summary>
    public static bool operator ==(Vector3 left, Vector3 right) => left.Equals(right);

    /// <summary>Exact inequality operator.</summary>
    public static bool operator !=(Vector3 left, Vector3 right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => $"Vector3(X={X}, Y={Y}, Z={Z})";

    /// <summary>
    /// Deconstructs the vector into its three components for pattern matching and
    /// tuple-style destructuring.
    /// </summary>
    /// <param name="x">Receives the X component.</param>
    /// <param name="y">Receives the Y component.</param>
    /// <param name="z">Receives the Z component.</param>
    public void Deconstruct(out float x, out float y, out float z)
    {
        x = X;
        y = Y;
        z = Z;
    }
}
