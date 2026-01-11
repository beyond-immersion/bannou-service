namespace BeyondImmersion.Bannou.SceneComposer.Math;

/// <summary>
/// Engine-agnostic transform representing position, rotation, and scale.
/// Immutable by default; use With* methods to create modified copies.
/// </summary>
public readonly struct Transform : IEquatable<Transform>
{
    public static readonly Transform Identity = new(Vector3.Zero, Quaternion.Identity, Vector3.One);

    public Vector3 Position { get; }
    public Quaternion Rotation { get; }
    public Vector3 Scale { get; }

    public Transform(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        Position = position;
        Rotation = rotation;
        Scale = scale;
    }

    public Transform(Vector3 position) : this(position, Quaternion.Identity, Vector3.One) { }

    public Transform(Vector3 position, Quaternion rotation) : this(position, rotation, Vector3.One) { }

    /// <summary>
    /// Create a new transform with modified position.
    /// </summary>
    public Transform WithPosition(Vector3 position) =>
        new(position, Rotation, Scale);

    /// <summary>
    /// Create a new transform with modified rotation.
    /// </summary>
    public Transform WithRotation(Quaternion rotation) =>
        new(Position, rotation, Scale);

    /// <summary>
    /// Create a new transform with modified scale.
    /// </summary>
    public Transform WithScale(Vector3 scale) =>
        new(Position, Rotation, scale);

    /// <summary>
    /// Get forward direction (Z+) in world space.
    /// </summary>
    public Vector3 Forward => Rotation.Rotate(Vector3.UnitZ);

    /// <summary>
    /// Get right direction (X+) in world space.
    /// </summary>
    public Vector3 Right => Rotation.Rotate(Vector3.UnitX);

    /// <summary>
    /// Get up direction (Y+) in world space.
    /// </summary>
    public Vector3 Up => Rotation.Rotate(Vector3.UnitY);

    /// <summary>
    /// Transform a point from local space to world space.
    /// </summary>
    public Vector3 TransformPoint(Vector3 localPoint)
    {
        var scaled = new Vector3(
            localPoint.X * Scale.X,
            localPoint.Y * Scale.Y,
            localPoint.Z * Scale.Z);
        var rotated = Rotation.Rotate(scaled);
        return rotated + Position;
    }

    /// <summary>
    /// Transform a direction from local space to world space (ignores position and scale).
    /// </summary>
    public Vector3 TransformDirection(Vector3 localDirection) =>
        Rotation.Rotate(localDirection);

    /// <summary>
    /// Transform a point from world space to local space.
    /// </summary>
    public Vector3 InverseTransformPoint(Vector3 worldPoint)
    {
        var translated = worldPoint - Position;
        var rotated = Rotation.Inverse.Rotate(translated);
        return new Vector3(
            Scale.X != 0 ? rotated.X / Scale.X : 0,
            Scale.Y != 0 ? rotated.Y / Scale.Y : 0,
            Scale.Z != 0 ? rotated.Z / Scale.Z : 0);
    }

    /// <summary>
    /// Combine this transform with a child transform (parent * child).
    /// </summary>
    public Transform Combine(Transform child)
    {
        var childScaled = new Vector3(
            child.Position.X * Scale.X,
            child.Position.Y * Scale.Y,
            child.Position.Z * Scale.Z);

        return new Transform(
            Position + Rotation.Rotate(childScaled),
            Rotation * child.Rotation,
            new Vector3(
                Scale.X * child.Scale.X,
                Scale.Y * child.Scale.Y,
                Scale.Z * child.Scale.Z));
    }

    /// <summary>
    /// Linear interpolation between two transforms.
    /// </summary>
    public static Transform Lerp(Transform a, Transform b, double t) =>
        new(
            Vector3.Lerp(a.Position, b.Position, t),
            Quaternion.Slerp(a.Rotation, b.Rotation, t),
            Vector3.Lerp(a.Scale, b.Scale, t));

    /// <summary>
    /// Create a deep copy of this transform.
    /// </summary>
    public Transform Clone() =>
        new(Position, Rotation, Scale);

    public bool Equals(Transform other) =>
        Position == other.Position &&
        Rotation == other.Rotation &&
        Scale == other.Scale;

    public override bool Equals(object? obj) =>
        obj is Transform other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Position, Rotation, Scale);

    public static bool operator ==(Transform left, Transform right) =>
        left.Equals(right);

    public static bool operator !=(Transform left, Transform right) =>
        !left.Equals(right);

    public override string ToString() =>
        $"Transform(P:{Position}, R:{Rotation}, S:{Scale})";
}
