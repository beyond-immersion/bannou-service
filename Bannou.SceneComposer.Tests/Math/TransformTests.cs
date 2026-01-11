using BeyondImmersion.Bannou.SceneComposer.Math;
using Xunit;

namespace BeyondImmersion.Bannou.SceneComposer.Tests.Math;

/// <summary>
/// Tests for the Transform struct.
/// </summary>
public class TransformTests
{
    private const double Epsilon = 1e-6;

    // =========================================================================
    // IDENTITY
    // =========================================================================

    [Fact]
    public void Identity_HasCorrectValues()
    {
        var identity = Transform.Identity;

        Assert.Equal(Vector3.Zero, identity.Position);
        Assert.Equal(Quaternion.Identity, identity.Rotation);
        Assert.Equal(Vector3.One, identity.Scale);
    }

    // =========================================================================
    // CONSTRUCTORS
    // =========================================================================

    [Fact]
    public void Constructor_Full_SetsAllProperties()
    {
        var pos = new Vector3(1, 2, 3);
        var rot = Quaternion.FromAxisAngle(Vector3.UnitY, System.Math.PI / 4);
        var scale = new Vector3(2, 2, 2);

        var t = new Transform(pos, rot, scale);

        Assert.Equal(pos, t.Position);
        Assert.Equal(rot.X, t.Rotation.X, Epsilon);
        Assert.Equal(rot.Y, t.Rotation.Y, Epsilon);
        Assert.Equal(rot.Z, t.Rotation.Z, Epsilon);
        Assert.Equal(rot.W, t.Rotation.W, Epsilon);
        Assert.Equal(scale, t.Scale);
    }

    [Fact]
    public void Constructor_PositionOnly_DefaultsOthers()
    {
        var pos = new Vector3(1, 2, 3);

        var t = new Transform(pos);

        Assert.Equal(pos, t.Position);
        Assert.Equal(Quaternion.Identity, t.Rotation);
        Assert.Equal(Vector3.One, t.Scale);
    }

    [Fact]
    public void Constructor_PositionAndRotation_DefaultsScale()
    {
        var pos = new Vector3(1, 2, 3);
        var rot = Quaternion.FromAxisAngle(Vector3.UnitY, System.Math.PI / 4);

        var t = new Transform(pos, rot);

        Assert.Equal(pos, t.Position);
        Assert.Equal(Vector3.One, t.Scale);
    }

    // =========================================================================
    // WITH METHODS
    // =========================================================================

    [Fact]
    public void WithPosition_ReturnsNewTransformWithNewPosition()
    {
        var original = new Transform(new Vector3(1, 2, 3), Quaternion.Identity, new Vector3(2, 2, 2));
        var newPos = new Vector3(10, 20, 30);

        var modified = original.WithPosition(newPos);

        Assert.Equal(newPos, modified.Position);
        Assert.Equal(original.Rotation, modified.Rotation);
        Assert.Equal(original.Scale, modified.Scale);
        // Original unchanged
        Assert.Equal(new Vector3(1, 2, 3), original.Position);
    }

    [Fact]
    public void WithRotation_ReturnsNewTransformWithNewRotation()
    {
        var original = new Transform(new Vector3(1, 2, 3), Quaternion.Identity, new Vector3(2, 2, 2));
        var newRot = Quaternion.FromAxisAngle(Vector3.UnitY, System.Math.PI / 2);

        var modified = original.WithRotation(newRot);

        Assert.Equal(original.Position, modified.Position);
        Assert.Equal(newRot.X, modified.Rotation.X, Epsilon);
        Assert.Equal(newRot.Y, modified.Rotation.Y, Epsilon);
        Assert.Equal(newRot.Z, modified.Rotation.Z, Epsilon);
        Assert.Equal(newRot.W, modified.Rotation.W, Epsilon);
        Assert.Equal(original.Scale, modified.Scale);
    }

    [Fact]
    public void WithScale_ReturnsNewTransformWithNewScale()
    {
        var original = new Transform(new Vector3(1, 2, 3), Quaternion.Identity, new Vector3(2, 2, 2));
        var newScale = new Vector3(5, 5, 5);

        var modified = original.WithScale(newScale);

        Assert.Equal(original.Position, modified.Position);
        Assert.Equal(original.Rotation, modified.Rotation);
        Assert.Equal(newScale, modified.Scale);
    }

    // =========================================================================
    // DIRECTION VECTORS
    // =========================================================================

    [Fact]
    public void Forward_Identity_ReturnsUnitZ()
    {
        var t = Transform.Identity;

        var forward = t.Forward;

        Assert.Equal(0, forward.X, Epsilon);
        Assert.Equal(0, forward.Y, Epsilon);
        Assert.Equal(1, forward.Z, Epsilon);
    }

    [Fact]
    public void Right_Identity_ReturnsUnitX()
    {
        var t = Transform.Identity;

        var right = t.Right;

        Assert.Equal(1, right.X, Epsilon);
        Assert.Equal(0, right.Y, Epsilon);
        Assert.Equal(0, right.Z, Epsilon);
    }

    [Fact]
    public void Up_Identity_ReturnsUnitY()
    {
        var t = Transform.Identity;

        var up = t.Up;

        Assert.Equal(0, up.X, Epsilon);
        Assert.Equal(1, up.Y, Epsilon);
        Assert.Equal(0, up.Z, Epsilon);
    }

    [Fact]
    public void Forward_Rotated90AroundY_RotatesForwardVector()
    {
        var t = new Transform(Vector3.Zero, Quaternion.FromAxisAngle(Vector3.UnitY, System.Math.PI / 2));

        var forward = t.Forward;

        // Forward (0,0,1) rotated 90° around Y becomes (1,0,0) in standard right-hand rotation
        Assert.Equal(1, forward.X, Epsilon);
        Assert.Equal(0, forward.Y, Epsilon);
        Assert.Equal(0, forward.Z, Epsilon);
    }

    // =========================================================================
    // TRANSFORM POINT
    // =========================================================================

    [Fact]
    public void TransformPoint_Identity_ReturnsSamePoint()
    {
        var t = Transform.Identity;
        var point = new Vector3(1, 2, 3);

        var transformed = t.TransformPoint(point);

        Assert.Equal(point.X, transformed.X, Epsilon);
        Assert.Equal(point.Y, transformed.Y, Epsilon);
        Assert.Equal(point.Z, transformed.Z, Epsilon);
    }

    [Fact]
    public void TransformPoint_Translation_AddsPosition()
    {
        var t = new Transform(new Vector3(10, 20, 30));
        var point = new Vector3(1, 2, 3);

        var transformed = t.TransformPoint(point);

        Assert.Equal(11, transformed.X, Epsilon);
        Assert.Equal(22, transformed.Y, Epsilon);
        Assert.Equal(33, transformed.Z, Epsilon);
    }

    [Fact]
    public void TransformPoint_Scale_ScalesPoint()
    {
        var t = new Transform(Vector3.Zero, Quaternion.Identity, new Vector3(2, 3, 4));
        var point = new Vector3(1, 1, 1);

        var transformed = t.TransformPoint(point);

        Assert.Equal(2, transformed.X, Epsilon);
        Assert.Equal(3, transformed.Y, Epsilon);
        Assert.Equal(4, transformed.Z, Epsilon);
    }

    [Fact]
    public void TransformPoint_RotationThenTranslation()
    {
        // Rotate 90 around Y, then translate
        var t = new Transform(
            new Vector3(10, 0, 0),
            Quaternion.FromAxisAngle(Vector3.UnitY, System.Math.PI / 2));
        var point = new Vector3(1, 0, 0);

        var transformed = t.TransformPoint(point);

        // Point (1,0,0) rotated 90° around Y = (0,0,-1), then translated by (10,0,0) = (10,0,-1)
        Assert.Equal(10, transformed.X, Epsilon);
        Assert.Equal(0, transformed.Y, Epsilon);
        Assert.Equal(-1, transformed.Z, Epsilon);
    }

    // =========================================================================
    // INVERSE TRANSFORM POINT
    // =========================================================================

    [Fact]
    public void InverseTransformPoint_UndoesTransformPoint()
    {
        var t = new Transform(
            new Vector3(10, 20, 30),
            Quaternion.FromAxisAngle(Vector3.UnitY, System.Math.PI / 4),
            new Vector3(2, 2, 2));
        var localPoint = new Vector3(1, 2, 3);

        var worldPoint = t.TransformPoint(localPoint);
        var backToLocal = t.InverseTransformPoint(worldPoint);

        Assert.Equal(localPoint.X, backToLocal.X, Epsilon);
        Assert.Equal(localPoint.Y, backToLocal.Y, Epsilon);
        Assert.Equal(localPoint.Z, backToLocal.Z, Epsilon);
    }

    // =========================================================================
    // TRANSFORM DIRECTION
    // =========================================================================

    [Fact]
    public void TransformDirection_IgnoresPositionAndScale()
    {
        var t = new Transform(
            new Vector3(100, 100, 100),
            Quaternion.FromAxisAngle(Vector3.UnitY, System.Math.PI / 2),
            new Vector3(5, 5, 5));
        var direction = Vector3.UnitX;

        var transformed = t.TransformDirection(direction);

        // Only rotation applies: UnitX rotated 90° around Y = (0, 0, -1)
        Assert.Equal(0, transformed.X, Epsilon);
        Assert.Equal(0, transformed.Y, Epsilon);
        Assert.Equal(-1, transformed.Z, Epsilon);
    }

    // =========================================================================
    // COMBINE
    // =========================================================================

    [Fact]
    public void Combine_WithIdentity_ReturnsSame()
    {
        var t = new Transform(
            new Vector3(1, 2, 3),
            Quaternion.FromAxisAngle(Vector3.UnitY, System.Math.PI / 4),
            new Vector3(2, 2, 2));

        var result = t.Combine(Transform.Identity);

        Assert.Equal(t.Position.X, result.Position.X, Epsilon);
        Assert.Equal(t.Position.Y, result.Position.Y, Epsilon);
        Assert.Equal(t.Position.Z, result.Position.Z, Epsilon);
        Assert.Equal(t.Scale.X, result.Scale.X, Epsilon);
        Assert.Equal(t.Scale.Y, result.Scale.Y, Epsilon);
        Assert.Equal(t.Scale.Z, result.Scale.Z, Epsilon);
    }

    [Fact]
    public void Combine_ChildPosition_IsTransformed()
    {
        var parent = new Transform(new Vector3(10, 0, 0));
        var child = new Transform(new Vector3(5, 0, 0));

        var combined = parent.Combine(child);

        Assert.Equal(15, combined.Position.X, Epsilon);
        Assert.Equal(0, combined.Position.Y, Epsilon);
        Assert.Equal(0, combined.Position.Z, Epsilon);
    }

    [Fact]
    public void Combine_Scales_Multiply()
    {
        var parent = new Transform(Vector3.Zero, Quaternion.Identity, new Vector3(2, 2, 2));
        var child = new Transform(Vector3.Zero, Quaternion.Identity, new Vector3(3, 3, 3));

        var combined = parent.Combine(child);

        Assert.Equal(6, combined.Scale.X, Epsilon);
        Assert.Equal(6, combined.Scale.Y, Epsilon);
        Assert.Equal(6, combined.Scale.Z, Epsilon);
    }

    [Fact]
    public void Combine_Rotations_Combine()
    {
        var parent = new Transform(Vector3.Zero, Quaternion.FromAxisAngle(Vector3.UnitY, System.Math.PI / 4));
        var child = new Transform(Vector3.Zero, Quaternion.FromAxisAngle(Vector3.UnitY, System.Math.PI / 4));

        var combined = parent.Combine(child);

        // 45° + 45° = 90° rotation around Y
        // Forward (0,0,1) rotated 90° around Y becomes (1,0,0)
        var forward = combined.Forward;
        Assert.Equal(1, forward.X, Epsilon);
        Assert.Equal(0, forward.Y, Epsilon);
        Assert.Equal(0, forward.Z, 0.001);
    }

    // =========================================================================
    // LERP
    // =========================================================================

    [Fact]
    public void Lerp_AtZero_ReturnsFirst()
    {
        var a = new Transform(new Vector3(0, 0, 0));
        var b = new Transform(new Vector3(10, 10, 10));

        var result = Transform.Lerp(a, b, 0);

        Assert.Equal(a.Position, result.Position);
    }

    [Fact]
    public void Lerp_AtOne_ReturnsSecond()
    {
        var a = new Transform(new Vector3(0, 0, 0));
        var b = new Transform(new Vector3(10, 10, 10));

        var result = Transform.Lerp(a, b, 1);

        Assert.Equal(b.Position, result.Position);
    }

    [Fact]
    public void Lerp_AtHalf_InterpolatesPosition()
    {
        var a = new Transform(new Vector3(0, 0, 0));
        var b = new Transform(new Vector3(10, 10, 10));

        var result = Transform.Lerp(a, b, 0.5);

        Assert.Equal(5, result.Position.X, Epsilon);
        Assert.Equal(5, result.Position.Y, Epsilon);
        Assert.Equal(5, result.Position.Z, Epsilon);
    }

    // =========================================================================
    // CLONE
    // =========================================================================

    [Fact]
    public void Clone_ReturnsEquivalentTransform()
    {
        var original = new Transform(
            new Vector3(1, 2, 3),
            Quaternion.FromAxisAngle(Vector3.UnitY, System.Math.PI / 4),
            new Vector3(2, 2, 2));

        var clone = original.Clone();

        Assert.Equal(original.Position, clone.Position);
        Assert.Equal(original.Rotation, clone.Rotation);
        Assert.Equal(original.Scale, clone.Scale);
    }

    // =========================================================================
    // EQUALITY
    // =========================================================================

    [Fact]
    public void Equals_SameValues_ReturnsTrue()
    {
        var a = new Transform(new Vector3(1, 2, 3), Quaternion.Identity, new Vector3(2, 2, 2));
        var b = new Transform(new Vector3(1, 2, 3), Quaternion.Identity, new Vector3(2, 2, 2));

        Assert.True(a.Equals(b));
        Assert.True(a == b);
    }

    [Fact]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        var a = new Transform(new Vector3(1, 2, 3));
        var b = new Transform(new Vector3(1, 2, 4));

        Assert.False(a.Equals(b));
        Assert.True(a != b);
    }
}
