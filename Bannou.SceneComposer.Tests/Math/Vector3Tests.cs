using BeyondImmersion.Bannou.SceneComposer.Math;
using Xunit;

namespace BeyondImmersion.Bannou.SceneComposer.Tests.Math;

/// <summary>
/// Tests for the Vector3 struct.
/// </summary>
public class Vector3Tests
{
    private const double Epsilon = 1e-10;

    // =========================================================================
    // STATIC CONSTANTS
    // =========================================================================

    [Fact]
    public void Zero_HasAllZeroComponents()
    {
        var zero = Vector3.Zero;

        Assert.Equal(0, zero.X);
        Assert.Equal(0, zero.Y);
        Assert.Equal(0, zero.Z);
    }

    [Fact]
    public void One_HasAllOneComponents()
    {
        var one = Vector3.One;

        Assert.Equal(1, one.X);
        Assert.Equal(1, one.Y);
        Assert.Equal(1, one.Z);
    }

    [Fact]
    public void UnitVectors_AreCorrect()
    {
        Assert.Equal(new Vector3(1, 0, 0), Vector3.UnitX);
        Assert.Equal(new Vector3(0, 1, 0), Vector3.UnitY);
        Assert.Equal(new Vector3(0, 0, 1), Vector3.UnitZ);
    }

    // =========================================================================
    // CONSTRUCTION AND PROPERTIES
    // =========================================================================

    [Fact]
    public void Constructor_SetsComponents()
    {
        var v = new Vector3(1.5, 2.5, 3.5);

        Assert.Equal(1.5, v.X);
        Assert.Equal(2.5, v.Y);
        Assert.Equal(3.5, v.Z);
    }

    [Fact]
    public void Length_ReturnsCorrectValue()
    {
        var v = new Vector3(3, 4, 0);

        Assert.Equal(5, v.Length, Epsilon);
    }

    [Fact]
    public void LengthSquared_ReturnsCorrectValue()
    {
        var v = new Vector3(3, 4, 0);

        Assert.Equal(25, v.LengthSquared, Epsilon);
    }

    [Fact]
    public void Normalized_ReturnsUnitVector()
    {
        var v = new Vector3(3, 4, 0);
        var normalized = v.Normalized;

        Assert.Equal(1, normalized.Length, Epsilon);
        Assert.Equal(0.6, normalized.X, Epsilon);
        Assert.Equal(0.8, normalized.Y, Epsilon);
        Assert.Equal(0, normalized.Z, Epsilon);
    }

    [Fact]
    public void Normalized_ZeroVector_ReturnsZero()
    {
        var zero = Vector3.Zero;
        var normalized = zero.Normalized;

        Assert.Equal(Vector3.Zero, normalized);
    }

    // =========================================================================
    // ARITHMETIC OPERATORS
    // =========================================================================

    [Fact]
    public void Addition_AddsComponents()
    {
        var a = new Vector3(1, 2, 3);
        var b = new Vector3(4, 5, 6);

        var result = a + b;

        Assert.Equal(5, result.X);
        Assert.Equal(7, result.Y);
        Assert.Equal(9, result.Z);
    }

    [Fact]
    public void Subtraction_SubtractsComponents()
    {
        var a = new Vector3(5, 7, 9);
        var b = new Vector3(1, 2, 3);

        var result = a - b;

        Assert.Equal(4, result.X);
        Assert.Equal(5, result.Y);
        Assert.Equal(6, result.Z);
    }

    [Fact]
    public void ScalarMultiplication_MultipliesAllComponents()
    {
        var v = new Vector3(1, 2, 3);

        var result1 = v * 2;
        var result2 = 2 * v;

        Assert.Equal(new Vector3(2, 4, 6), result1);
        Assert.Equal(new Vector3(2, 4, 6), result2);
    }

    [Fact]
    public void ScalarDivision_DividesAllComponents()
    {
        var v = new Vector3(2, 4, 6);

        var result = v / 2;

        Assert.Equal(new Vector3(1, 2, 3), result);
    }

    [Fact]
    public void Negation_NegatesAllComponents()
    {
        var v = new Vector3(1, -2, 3);

        var result = -v;

        Assert.Equal(new Vector3(-1, 2, -3), result);
    }

    // =========================================================================
    // STATIC METHODS
    // =========================================================================

    [Fact]
    public void Dot_ReturnsCorrectValue()
    {
        var a = new Vector3(1, 2, 3);
        var b = new Vector3(4, 5, 6);

        var dot = Vector3.Dot(a, b);

        Assert.Equal(32, dot, Epsilon); // 1*4 + 2*5 + 3*6 = 4 + 10 + 18 = 32
    }

    [Fact]
    public void Dot_PerpendicularVectors_ReturnsZero()
    {
        var a = Vector3.UnitX;
        var b = Vector3.UnitY;

        var dot = Vector3.Dot(a, b);

        Assert.Equal(0, dot, Epsilon);
    }

    [Fact]
    public void Cross_ReturnsPerpendicularVector()
    {
        var cross = Vector3.Cross(Vector3.UnitX, Vector3.UnitY);

        Assert.Equal(Vector3.UnitZ, cross);
    }

    [Fact]
    public void Cross_RightHandRule()
    {
        // X × Y = Z
        Assert.Equal(Vector3.UnitZ, Vector3.Cross(Vector3.UnitX, Vector3.UnitY));
        // Y × Z = X
        Assert.Equal(Vector3.UnitX, Vector3.Cross(Vector3.UnitY, Vector3.UnitZ));
        // Z × X = Y
        Assert.Equal(Vector3.UnitY, Vector3.Cross(Vector3.UnitZ, Vector3.UnitX));
    }

    [Fact]
    public void Cross_AntiCommutative()
    {
        var a = new Vector3(1, 2, 3);
        var b = new Vector3(4, 5, 6);

        var ab = Vector3.Cross(a, b);
        var ba = Vector3.Cross(b, a);

        Assert.Equal(-ab.X, ba.X, Epsilon);
        Assert.Equal(-ab.Y, ba.Y, Epsilon);
        Assert.Equal(-ab.Z, ba.Z, Epsilon);
    }

    [Fact]
    public void Lerp_ReturnsInterpolatedValue()
    {
        var a = new Vector3(0, 0, 0);
        var b = new Vector3(10, 20, 30);

        var mid = Vector3.Lerp(a, b, 0.5);

        Assert.Equal(5, mid.X, Epsilon);
        Assert.Equal(10, mid.Y, Epsilon);
        Assert.Equal(15, mid.Z, Epsilon);
    }

    [Fact]
    public void Lerp_AtZero_ReturnsA()
    {
        var a = new Vector3(1, 2, 3);
        var b = new Vector3(4, 5, 6);

        var result = Vector3.Lerp(a, b, 0);

        Assert.Equal(a, result);
    }

    [Fact]
    public void Lerp_AtOne_ReturnsB()
    {
        var a = new Vector3(1, 2, 3);
        var b = new Vector3(4, 5, 6);

        var result = Vector3.Lerp(a, b, 1);

        Assert.Equal(b, result);
    }

    [Fact]
    public void Distance_ReturnsCorrectValue()
    {
        var a = new Vector3(0, 0, 0);
        var b = new Vector3(3, 4, 0);

        var distance = Vector3.Distance(a, b);

        Assert.Equal(5, distance, Epsilon);
    }

    [Fact]
    public void Min_ReturnsComponentWiseMinimum()
    {
        var a = new Vector3(1, 5, 3);
        var b = new Vector3(4, 2, 6);

        var min = Vector3.Min(a, b);

        Assert.Equal(new Vector3(1, 2, 3), min);
    }

    [Fact]
    public void Max_ReturnsComponentWiseMaximum()
    {
        var a = new Vector3(1, 5, 3);
        var b = new Vector3(4, 2, 6);

        var max = Vector3.Max(a, b);

        Assert.Equal(new Vector3(4, 5, 6), max);
    }

    // =========================================================================
    // EQUALITY
    // =========================================================================

    [Fact]
    public void Equals_SameValues_ReturnsTrue()
    {
        var a = new Vector3(1, 2, 3);
        var b = new Vector3(1, 2, 3);

        Assert.True(a.Equals(b));
        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        var a = new Vector3(1, 2, 3);
        var b = new Vector3(1, 2, 4);

        Assert.False(a.Equals(b));
        Assert.False(a == b);
        Assert.True(a != b);
    }

    [Fact]
    public void GetHashCode_SameValues_SameHash()
    {
        var a = new Vector3(1, 2, 3);
        var b = new Vector3(1, 2, 3);

        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    // =========================================================================
    // DECONSTRUCTION
    // =========================================================================

    [Fact]
    public void Deconstruct_ReturnsComponents()
    {
        var v = new Vector3(1, 2, 3);

        var (x, y, z) = v;

        Assert.Equal(1, x);
        Assert.Equal(2, y);
        Assert.Equal(3, z);
    }

    // =========================================================================
    // TO STRING
    // =========================================================================

    [Fact]
    public void ToString_ReturnsFormattedString()
    {
        var v = new Vector3(1.5, 2.5, 3.5);

        var str = v.ToString();

        Assert.Contains("1.500", str);
        Assert.Contains("2.500", str);
        Assert.Contains("3.500", str);
    }
}
