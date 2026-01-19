namespace BeyondImmersion.Bannou.SceneComposer.Math;

/// <summary>
/// Engine-agnostic quaternion for rotation representation.
/// Uses double precision for accuracy.
/// </summary>
public readonly struct Quaternion : IEquatable<Quaternion>
{
    /// <summary>Identity quaternion (0, 0, 0, 1).</summary>
    public static readonly Quaternion Identity = new(0, 0, 0, 1);

    /// <summary>X component.</summary>
    public double X { get; }
    /// <summary>Y component.</summary>
    public double Y { get; }
    /// <summary>Z component.</summary>
    public double Z { get; }
    /// <summary>W component.</summary>
    public double W { get; }

    /// <summary>Creates a quaternion from XYZW components.</summary>
    public Quaternion(double x, double y, double z, double w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    /// <summary>
    /// Squared length of the quaternion.
    /// </summary>
    public double LengthSquared => X * X + Y * Y + Z * Z + W * W;

    /// <summary>
    /// Length of the quaternion.
    /// </summary>
    public double Length => System.Math.Sqrt(LengthSquared);

    /// <summary>
    /// Returns a normalized quaternion.
    /// </summary>
    public Quaternion Normalized
    {
        get
        {
            var len = Length;
            if (len < double.Epsilon) return Identity;
            return new Quaternion(X / len, Y / len, Z / len, W / len);
        }
    }

    /// <summary>
    /// Returns the conjugate (inverse for unit quaternions).
    /// </summary>
    public Quaternion Conjugate => new(-X, -Y, -Z, W);

    /// <summary>
    /// Returns the inverse quaternion.
    /// </summary>
    public Quaternion Inverse
    {
        get
        {
            var lenSq = LengthSquared;
            if (lenSq < double.Epsilon) return Identity;
            var invLenSq = 1.0 / lenSq;
            return new Quaternion(-X * invLenSq, -Y * invLenSq, -Z * invLenSq, W * invLenSq);
        }
    }

    /// <summary>
    /// Create a quaternion from axis-angle representation.
    /// </summary>
    public static Quaternion FromAxisAngle(Vector3 axis, double angleRadians)
    {
        var halfAngle = angleRadians * 0.5;
        var sin = System.Math.Sin(halfAngle);
        var cos = System.Math.Cos(halfAngle);
        var normalizedAxis = axis.Normalized;

        return new Quaternion(
            normalizedAxis.X * sin,
            normalizedAxis.Y * sin,
            normalizedAxis.Z * sin,
            cos);
    }

    /// <summary>
    /// Create a quaternion from Euler angles (in radians, YXZ order - pitch, yaw, roll).
    /// </summary>
    public static Quaternion FromEuler(double pitch, double yaw, double roll)
    {
        var cy = System.Math.Cos(yaw * 0.5);
        var sy = System.Math.Sin(yaw * 0.5);
        var cp = System.Math.Cos(pitch * 0.5);
        var sp = System.Math.Sin(pitch * 0.5);
        var cr = System.Math.Cos(roll * 0.5);
        var sr = System.Math.Sin(roll * 0.5);

        return new Quaternion(
            sr * cp * cy - cr * sp * sy,
            cr * sp * cy + sr * cp * sy,
            cr * cp * sy - sr * sp * cy,
            cr * cp * cy + sr * sp * sy);
    }

    /// <summary>
    /// Create a quaternion from Euler angles vector (in radians).
    /// </summary>
    public static Quaternion FromEuler(Vector3 euler) =>
        FromEuler(euler.X, euler.Y, euler.Z);

    /// <summary>
    /// Convert to Euler angles (in radians).
    /// </summary>
    public Vector3 ToEuler()
    {
        // Roll (x-axis rotation)
        var sinrCosp = 2 * (W * X + Y * Z);
        var cosrCosp = 1 - 2 * (X * X + Y * Y);
        var roll = System.Math.Atan2(sinrCosp, cosrCosp);

        // Pitch (y-axis rotation)
        var sinp = 2 * (W * Y - Z * X);
        double pitch;
        if (System.Math.Abs(sinp) >= 1)
            pitch = System.Math.CopySign(System.Math.PI / 2, sinp);
        else
            pitch = System.Math.Asin(sinp);

        // Yaw (z-axis rotation)
        var sinyCosp = 2 * (W * Z + X * Y);
        var cosyCosp = 1 - 2 * (Y * Y + Z * Z);
        var yaw = System.Math.Atan2(sinyCosp, cosyCosp);

        return new Vector3(roll, pitch, yaw);
    }

    /// <summary>
    /// Quaternion multiplication (combines rotations).
    /// </summary>
    public static Quaternion operator *(Quaternion a, Quaternion b) =>
        new(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

    /// <summary>
    /// Rotate a vector by this quaternion.
    /// </summary>
    public Vector3 Rotate(Vector3 v)
    {
        var qv = new Quaternion(v.X, v.Y, v.Z, 0);
        var result = this * qv * Conjugate;
        return new Vector3(result.X, result.Y, result.Z);
    }

    /// <summary>
    /// Spherical linear interpolation between two quaternions.
    /// </summary>
    public static Quaternion Slerp(Quaternion a, Quaternion b, double t)
    {
        var dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

        // If dot is negative, negate one to take shorter path
        if (dot < 0)
        {
            b = new Quaternion(-b.X, -b.Y, -b.Z, -b.W);
            dot = -dot;
        }

        // If quaternions are very close, use linear interpolation
        if (dot > 0.9995)
        {
            return new Quaternion(
                a.X + t * (b.X - a.X),
                a.Y + t * (b.Y - a.Y),
                a.Z + t * (b.Z - a.Z),
                a.W + t * (b.W - a.W)).Normalized;
        }

        var theta0 = System.Math.Acos(dot);
        var theta = theta0 * t;
        var sinTheta = System.Math.Sin(theta);
        var sinTheta0 = System.Math.Sin(theta0);

        var s0 = System.Math.Cos(theta) - dot * sinTheta / sinTheta0;
        var s1 = sinTheta / sinTheta0;

        return new Quaternion(
            s0 * a.X + s1 * b.X,
            s0 * a.Y + s1 * b.Y,
            s0 * a.Z + s1 * b.Z,
            s0 * a.W + s1 * b.W);
    }

    /// <summary>
    /// Create rotation that looks from source to target.
    /// </summary>
    public static Quaternion LookRotation(Vector3 forward, Vector3 up)
    {
        forward = forward.Normalized;
        var right = Vector3.Cross(up, forward).Normalized;
        up = Vector3.Cross(forward, right);

        var m00 = right.X;
        var m01 = right.Y;
        var m02 = right.Z;
        var m10 = up.X;
        var m11 = up.Y;
        var m12 = up.Z;
        var m20 = forward.X;
        var m21 = forward.Y;
        var m22 = forward.Z;

        var trace = m00 + m11 + m22;
        double x, y, z, w;

        if (trace > 0)
        {
            var s = 0.5 / System.Math.Sqrt(trace + 1.0);
            w = 0.25 / s;
            x = (m12 - m21) * s;
            y = (m20 - m02) * s;
            z = (m01 - m10) * s;
        }
        else if (m00 > m11 && m00 > m22)
        {
            var s = 2.0 * System.Math.Sqrt(1.0 + m00 - m11 - m22);
            w = (m12 - m21) / s;
            x = 0.25 * s;
            y = (m10 + m01) / s;
            z = (m20 + m02) / s;
        }
        else if (m11 > m22)
        {
            var s = 2.0 * System.Math.Sqrt(1.0 + m11 - m00 - m22);
            w = (m20 - m02) / s;
            x = (m10 + m01) / s;
            y = 0.25 * s;
            z = (m21 + m12) / s;
        }
        else
        {
            var s = 2.0 * System.Math.Sqrt(1.0 + m22 - m00 - m11);
            w = (m01 - m10) / s;
            x = (m20 + m02) / s;
            y = (m21 + m12) / s;
            z = 0.25 * s;
        }

        return new Quaternion(x, y, z, w).Normalized;
    }

    /// <inheritdoc />
    public bool Equals(Quaternion other) =>
        System.Math.Abs(X - other.X) < double.Epsilon &&
        System.Math.Abs(Y - other.Y) < double.Epsilon &&
        System.Math.Abs(Z - other.Z) < double.Epsilon &&
        System.Math.Abs(W - other.W) < double.Epsilon;

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is Quaternion other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(X, Y, Z, W);

    /// <summary>Tests equality between two quaternions.</summary>
    public static bool operator ==(Quaternion left, Quaternion right) =>
        left.Equals(right);

    /// <summary>Tests inequality between two quaternions.</summary>
    public static bool operator !=(Quaternion left, Quaternion right) =>
        !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() =>
        $"({X:F3}, {Y:F3}, {Z:F3}, {W:F3})";
}
