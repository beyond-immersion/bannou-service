using BeyondImmersion.Bannou.SceneComposer.Math;
using Xunit;

namespace BeyondImmersion.Bannou.SceneComposer.Tests.Math;

/// <summary>
/// Tests for the Quaternion struct.
/// </summary>
public class QuaternionTests
{
    private const double Epsilon = 1e-6;

    // =========================================================================
    // IDENTITY
    // =========================================================================

    [Fact]
    public void Identity_HasCorrectComponents()
    {
        var identity = Quaternion.Identity;

        Assert.Equal(0, identity.X, Epsilon);
        Assert.Equal(0, identity.Y, Epsilon);
        Assert.Equal(0, identity.Z, Epsilon);
        Assert.Equal(1, identity.W, Epsilon);
    }

    [Fact]
    public void Identity_IsUnitQuaternion()
    {
        var identity = Quaternion.Identity;

        Assert.Equal(1, identity.Length, Epsilon);
    }

    // =========================================================================
    // CONSTRUCTION AND PROPERTIES
    // =========================================================================

    [Fact]
    public void Constructor_SetsComponents()
    {
        var q = new Quaternion(0.1, 0.2, 0.3, 0.9);

        Assert.Equal(0.1, q.X, Epsilon);
        Assert.Equal(0.2, q.Y, Epsilon);
        Assert.Equal(0.3, q.Z, Epsilon);
        Assert.Equal(0.9, q.W, Epsilon);
    }

    [Fact]
    public void LengthSquared_ReturnsCorrectValue()
    {
        var q = new Quaternion(1, 2, 3, 4);

        Assert.Equal(30, q.LengthSquared, Epsilon); // 1 + 4 + 9 + 16 = 30
    }

    [Fact]
    public void Length_ReturnsCorrectValue()
    {
        var q = new Quaternion(0, 0, 0, 2);

        Assert.Equal(2, q.Length, Epsilon);
    }

    [Fact]
    public void Normalized_ReturnsUnitQuaternion()
    {
        var q = new Quaternion(1, 2, 3, 4);
        var normalized = q.Normalized;

        Assert.Equal(1, normalized.Length, Epsilon);
    }

    [Fact]
    public void Conjugate_NegatesImaginaryParts()
    {
        var q = new Quaternion(1, 2, 3, 4);
        var conjugate = q.Conjugate;

        Assert.Equal(-1, conjugate.X, Epsilon);
        Assert.Equal(-2, conjugate.Y, Epsilon);
        Assert.Equal(-3, conjugate.Z, Epsilon);
        Assert.Equal(4, conjugate.W, Epsilon);
    }

    [Fact]
    public void Inverse_UndoesRotation()
    {
        var q = Quaternion.FromAxisAngle(Vector3.UnitY, System.Math.PI / 4);
        var inv = q.Inverse;

        var result = q * inv;

        Assert.Equal(Quaternion.Identity.X, result.X, Epsilon);
        Assert.Equal(Quaternion.Identity.Y, result.Y, Epsilon);
        Assert.Equal(Quaternion.Identity.Z, result.Z, Epsilon);
        Assert.Equal(Quaternion.Identity.W, result.W, Epsilon);
    }

    // =========================================================================
    // FROM AXIS ANGLE
    // =========================================================================

    [Fact]
    public void FromAxisAngle_ZeroAngle_ReturnsIdentity()
    {
        var q = Quaternion.FromAxisAngle(Vector3.UnitY, 0);

        Assert.Equal(0, q.X, Epsilon);
        Assert.Equal(0, q.Y, Epsilon);
        Assert.Equal(0, q.Z, Epsilon);
        Assert.Equal(1, q.W, Epsilon);
    }

    [Fact]
    public void FromAxisAngle_90DegreesAroundY_CorrectRotation()
    {
        var q = Quaternion.FromAxisAngle(Vector3.UnitY, System.Math.PI / 2);

        // Rotating X-axis 90 degrees around Y should give -Z
        var rotated = q.Rotate(Vector3.UnitX);

        Assert.Equal(0, rotated.X, Epsilon);
        Assert.Equal(0, rotated.Y, Epsilon);
        Assert.Equal(-1, rotated.Z, Epsilon);
    }

    [Fact]
    public void FromAxisAngle_180DegreesAroundZ_FlipsXY()
    {
        var q = Quaternion.FromAxisAngle(Vector3.UnitZ, System.Math.PI);

        var rotatedX = q.Rotate(Vector3.UnitX);
        var rotatedY = q.Rotate(Vector3.UnitY);

        Assert.Equal(-1, rotatedX.X, Epsilon);
        Assert.Equal(0, rotatedX.Y, Epsilon);
        Assert.Equal(-1, rotatedY.Y, Epsilon);
        Assert.Equal(0, rotatedY.X, Epsilon);
    }

    // =========================================================================
    // FROM EULER
    // =========================================================================

    [Fact]
    public void FromEuler_ZeroAngles_ReturnsIdentity()
    {
        var q = Quaternion.FromEuler(0, 0, 0);

        Assert.Equal(1, q.Length, Epsilon);
        // Should be close to identity
        Assert.True(System.Math.Abs(q.W) > 0.99);
    }

    [Fact]
    public void FromEuler_Vector_SameAsScalars()
    {
        var euler = new Vector3(0.1, 0.2, 0.3);
        var q1 = Quaternion.FromEuler(0.1, 0.2, 0.3);
        var q2 = Quaternion.FromEuler(euler);

        Assert.Equal(q1.X, q2.X, Epsilon);
        Assert.Equal(q1.Y, q2.Y, Epsilon);
        Assert.Equal(q1.Z, q2.Z, Epsilon);
        Assert.Equal(q1.W, q2.W, Epsilon);
    }

    // =========================================================================
    // MULTIPLICATION
    // =========================================================================

    [Fact]
    public void Multiplication_WithIdentity_ReturnsSame()
    {
        var q = Quaternion.FromAxisAngle(Vector3.UnitY, System.Math.PI / 4);
        var identity = Quaternion.Identity;

        var result1 = q * identity;
        var result2 = identity * q;

        Assert.Equal(q.X, result1.X, Epsilon);
        Assert.Equal(q.Y, result1.Y, Epsilon);
        Assert.Equal(q.Z, result1.Z, Epsilon);
        Assert.Equal(q.W, result1.W, Epsilon);

        Assert.Equal(q.X, result2.X, Epsilon);
        Assert.Equal(q.Y, result2.Y, Epsilon);
        Assert.Equal(q.Z, result2.Z, Epsilon);
        Assert.Equal(q.W, result2.W, Epsilon);
    }

    [Fact]
    public void Multiplication_CombinesRotations()
    {
        var rotY90 = Quaternion.FromAxisAngle(Vector3.UnitY, System.Math.PI / 2);

        // Two 90-degree rotations = 180-degree rotation
        var rot180 = rotY90 * rotY90;
        var rotatedX = rot180.Rotate(Vector3.UnitX);

        Assert.Equal(-1, rotatedX.X, Epsilon);
        Assert.Equal(0, rotatedX.Y, Epsilon);
        Assert.Equal(0, rotatedX.Z, Epsilon);
    }

    // =========================================================================
    // ROTATE
    // =========================================================================

    [Fact]
    public void Rotate_IdentityQuaternion_ReturnsSameVector()
    {
        var v = new Vector3(1, 2, 3);
        var rotated = Quaternion.Identity.Rotate(v);

        Assert.Equal(v.X, rotated.X, Epsilon);
        Assert.Equal(v.Y, rotated.Y, Epsilon);
        Assert.Equal(v.Z, rotated.Z, Epsilon);
    }

    [Fact]
    public void Rotate_PreservesLength()
    {
        var v = new Vector3(1, 2, 3);
        var q = Quaternion.FromAxisAngle(new Vector3(1, 1, 1).Normalized, System.Math.PI / 3);

        var rotated = q.Rotate(v);

        Assert.Equal(v.Length, rotated.Length, Epsilon);
    }

    // =========================================================================
    // SLERP
    // =========================================================================

    [Fact]
    public void Slerp_AtZero_ReturnsFirst()
    {
        var a = Quaternion.Identity;
        var b = Quaternion.FromAxisAngle(Vector3.UnitY, System.Math.PI / 2);

        var result = Quaternion.Slerp(a, b, 0);

        Assert.Equal(a.X, result.X, Epsilon);
        Assert.Equal(a.Y, result.Y, Epsilon);
        Assert.Equal(a.Z, result.Z, Epsilon);
        Assert.Equal(a.W, result.W, Epsilon);
    }

    [Fact]
    public void Slerp_AtOne_ReturnsSecond()
    {
        var a = Quaternion.Identity;
        var b = Quaternion.FromAxisAngle(Vector3.UnitY, System.Math.PI / 2);

        var result = Quaternion.Slerp(a, b, 1);

        Assert.Equal(b.X, result.X, Epsilon);
        Assert.Equal(b.Y, result.Y, Epsilon);
        Assert.Equal(b.Z, result.Z, Epsilon);
        Assert.Equal(b.W, result.W, Epsilon);
    }

    [Fact]
    public void Slerp_AtHalf_InterpolatesCorrectly()
    {
        var a = Quaternion.Identity;
        var b = Quaternion.FromAxisAngle(Vector3.UnitY, System.Math.PI / 2);

        var result = Quaternion.Slerp(a, b, 0.5);

        // Should be approximately 45 degree rotation around Y
        var rotated = result.Rotate(Vector3.UnitX);
        var expectedAngle = System.Math.PI / 4;
        var expected = new Vector3(System.Math.Cos(expectedAngle), 0, -System.Math.Sin(expectedAngle));

        Assert.Equal(expected.X, rotated.X, 0.001);
        Assert.Equal(expected.Y, rotated.Y, 0.001);
        Assert.Equal(expected.Z, rotated.Z, 0.001);
    }

    // =========================================================================
    // LOOK ROTATION
    // =========================================================================

    [Fact]
    public void LookRotation_Forward_AlignsFowardWithInput()
    {
        var forward = new Vector3(1, 0, 0);
        var up = Vector3.UnitY;

        var q = Quaternion.LookRotation(forward, up);
        var rotatedForward = q.Rotate(Vector3.UnitZ);

        Assert.Equal(forward.X, rotatedForward.X, Epsilon);
        Assert.Equal(forward.Y, rotatedForward.Y, Epsilon);
        Assert.Equal(forward.Z, rotatedForward.Z, Epsilon);
    }

    // =========================================================================
    // EQUALITY
    // =========================================================================

    [Fact]
    public void Equals_SameValues_ReturnsTrue()
    {
        var a = new Quaternion(0.1, 0.2, 0.3, 0.9);
        var b = new Quaternion(0.1, 0.2, 0.3, 0.9);

        Assert.True(a.Equals(b));
        Assert.True(a == b);
    }

    [Fact]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        var a = new Quaternion(0.1, 0.2, 0.3, 0.9);
        var b = new Quaternion(0.1, 0.2, 0.3, 0.8);

        Assert.False(a.Equals(b));
        Assert.True(a != b);
    }
}
