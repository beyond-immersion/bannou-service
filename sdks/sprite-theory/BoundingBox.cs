using BeyondImmersion.Bannou.Core.Math;

namespace BeyondImmersion.Bannou.SpriteTheory;

/// <summary>
/// Represents an axis-aligned 3D bounding box defined by minimum and maximum corners.
/// </summary>
/// <remarks>
/// Used to describe model bounds for orthographic camera setup. The bounding box
/// is axis-aligned — it does not account for rotation. The bridge provides this
/// from the loaded 3D model's geometry.
/// </remarks>
public readonly struct BoundingBox : IEquatable<BoundingBox>
{
    /// <summary>Minimum corner of the bounding box (smallest X, Y, Z values).</summary>
    public Vector3 Min { get; }

    /// <summary>Maximum corner of the bounding box (largest X, Y, Z values).</summary>
    public Vector3 Max { get; }

    /// <summary>
    /// Creates a new bounding box with the specified minimum and maximum corners.
    /// </summary>
    /// <param name="min">Minimum corner (smallest X, Y, Z).</param>
    /// <param name="max">Maximum corner (largest X, Y, Z).</param>
    public BoundingBox(Vector3 min, Vector3 max)
    {
        Min = min;
        Max = max;
    }

    /// <summary>Center point of the bounding box, computed as the midpoint of Min and Max.</summary>
    public Vector3 Center => (Min + Max) * 0.5f;

    /// <summary>Half-size of the bounding box along each axis (distance from center to face).</summary>
    public Vector3 Extents => (Max - Min) * 0.5f;

    /// <summary>Full size of the bounding box along each axis.</summary>
    public Vector3 Size => Max - Min;

    /// <inheritdoc />
    public bool Equals(BoundingBox other) => Min.Equals(other.Min) && Max.Equals(other.Max);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BoundingBox other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Min, Max);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(BoundingBox left, BoundingBox right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(BoundingBox left, BoundingBox right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() =>
        $"BoundingBox(Min=({Min.X}, {Min.Y}, {Min.Z}), Max=({Max.X}, {Max.Y}, {Max.Z}))";
}
