using BeyondImmersion.Bannou.Core.Math;
using Xunit;

namespace BeyondImmersion.Bannou.Core.Tests.Math;

/// <summary>
/// Unit tests for <see cref="Vector3"/>. Verifies construction, arithmetic,
/// vector operations, equality, and deconstruction.
/// </summary>
public class Vector3Tests
{
    #region Construction & Components

    /// <summary>Verifies that the constructor stores components verbatim.</summary>
    [Fact]
    public void Constructor_StoresComponents()
    {
        var v = new Vector3(1.5f, -2.25f, 3.75f);

        Assert.Equal(1.5f, v.X);
        Assert.Equal(-2.25f, v.Y);
        Assert.Equal(3.75f, v.Z);
    }

    /// <summary>Verifies that <see cref="Vector3.Zero"/> has zero components.</summary>
    [Fact]
    public void Zero_IsOrigin()
    {
        Assert.Equal(0f, Vector3.Zero.X);
        Assert.Equal(0f, Vector3.Zero.Y);
        Assert.Equal(0f, Vector3.Zero.Z);
    }

    /// <summary>Verifies that <see cref="Vector3.One"/> has every component equal to one.</summary>
    [Fact]
    public void One_HasUnitComponents()
    {
        Assert.Equal(1f, Vector3.One.X);
        Assert.Equal(1f, Vector3.One.Y);
        Assert.Equal(1f, Vector3.One.Z);
    }

    /// <summary>Verifies that the unit axis vectors are canonical.</summary>
    [Fact]
    public void UnitAxes_AreCanonical()
    {
        Assert.Equal(new Vector3(1f, 0f, 0f), Vector3.UnitX);
        Assert.Equal(new Vector3(0f, 1f, 0f), Vector3.UnitY);
        Assert.Equal(new Vector3(0f, 0f, 1f), Vector3.UnitZ);
    }

    #endregion

    #region Length & Normalization

    /// <summary>Verifies that <see cref="Vector3.LengthSquared"/> matches manual computation.</summary>
    [Fact]
    public void LengthSquared_MatchesManualComputation()
    {
        var v = new Vector3(2f, 3f, 6f);

        Assert.Equal(4f + 9f + 36f, v.LengthSquared);
    }

    /// <summary>
    /// Verifies that <see cref="Vector3.Length"/> is the square root of
    /// <see cref="Vector3.LengthSquared"/>.
    /// </summary>
    [Fact]
    public void Length_IsSqrtOfLengthSquared()
    {
        var v = new Vector3(2f, 3f, 6f);

        Assert.Equal(7f, v.Length);
    }

    /// <summary>Verifies that normalizing a unit axis returns that axis unchanged.</summary>
    [Fact]
    public void Normalized_UnitAxis_ReturnsSameVector()
    {
        Assert.Equal(Vector3.UnitX, Vector3.UnitX.Normalized);
        Assert.Equal(Vector3.UnitY, Vector3.UnitY.Normalized);
        Assert.Equal(Vector3.UnitZ, Vector3.UnitZ.Normalized);
    }

    /// <summary>Verifies that <see cref="Vector3.Zero"/>.Normalized returns <see cref="Vector3.Zero"/>.</summary>
    [Fact]
    public void Normalized_ZeroVector_ReturnsZero()
    {
        Assert.Equal(Vector3.Zero, Vector3.Zero.Normalized);
    }

    /// <summary>Verifies that normalizing a non-unit vector produces a unit-length result.</summary>
    [Fact]
    public void Normalized_NonUnit_ProducesUnitLength()
    {
        var v = new Vector3(3f, 0f, 4f);
        var n = v.Normalized;

        Assert.True(MathF.Abs(n.Length - 1f) < 1e-5f,
            $"Expected Length ≈ 1.0, got {n.Length}");
        Assert.Equal(0.6f, n.X, precision: 5);
        Assert.Equal(0f, n.Y);
        Assert.Equal(0.8f, n.Z, precision: 5);
    }

    /// <summary>Verifies that nearly-zero vectors degenerate to zero rather than NaN.</summary>
    [Fact]
    public void Normalized_TinyVector_ReturnsZero()
    {
        var v = new Vector3(1e-20f, 1e-20f, 1e-20f);

        Assert.Equal(Vector3.Zero, v.Normalized);
    }

    #endregion

    #region Arithmetic Operators

    /// <summary>Verifies component-wise addition.</summary>
    [Fact]
    public void Addition_IsComponentWise()
    {
        var a = new Vector3(1f, 2f, 3f);
        var b = new Vector3(4f, 5f, 6f);

        Assert.Equal(new Vector3(5f, 7f, 9f), a + b);
    }

    /// <summary>Verifies component-wise subtraction.</summary>
    [Fact]
    public void Subtraction_IsComponentWise()
    {
        var a = new Vector3(5f, 7f, 9f);
        var b = new Vector3(4f, 5f, 6f);

        Assert.Equal(new Vector3(1f, 2f, 3f), a - b);
    }

    /// <summary>Verifies scalar multiplication in both operand orders.</summary>
    [Fact]
    public void ScalarMultiplication_WorksEitherOrder()
    {
        var v = new Vector3(1f, -2f, 3f);

        Assert.Equal(new Vector3(2f, -4f, 6f), v * 2f);
        Assert.Equal(new Vector3(2f, -4f, 6f), 2f * v);
    }

    /// <summary>Verifies scalar division.</summary>
    [Fact]
    public void ScalarDivision_IsComponentWise()
    {
        var v = new Vector3(2f, -4f, 6f);

        Assert.Equal(new Vector3(1f, -2f, 3f), v / 2f);
    }

    /// <summary>Verifies unary negation.</summary>
    [Fact]
    public void UnaryNegation_FlipsSigns()
    {
        var v = new Vector3(1f, -2f, 3f);

        Assert.Equal(new Vector3(-1f, 2f, -3f), -v);
    }

    #endregion

    #region Dot, Cross, Lerp, Distance

    /// <summary>Verifies dot product against a known value.</summary>
    [Fact]
    public void Dot_ComputesInnerProduct()
    {
        var a = new Vector3(1f, 2f, 3f);
        var b = new Vector3(4f, -5f, 6f);

        // 1*4 + 2*(-5) + 3*6 = 4 - 10 + 18 = 12
        Assert.Equal(12f, Vector3.Dot(a, b));
    }

    /// <summary>Verifies that the dot product of orthogonal unit axes is zero.</summary>
    [Fact]
    public void Dot_OrthogonalAxes_IsZero()
    {
        Assert.Equal(0f, Vector3.Dot(Vector3.UnitX, Vector3.UnitY));
        Assert.Equal(0f, Vector3.Dot(Vector3.UnitY, Vector3.UnitZ));
        Assert.Equal(0f, Vector3.Dot(Vector3.UnitZ, Vector3.UnitX));
    }

    /// <summary>Verifies the right-handed cross product of canonical unit axes.</summary>
    [Fact]
    public void Cross_RightHanded_UnitXCrossUnitY_IsUnitZ()
    {
        Assert.Equal(Vector3.UnitZ, Vector3.Cross(Vector3.UnitX, Vector3.UnitY));
        Assert.Equal(Vector3.UnitX, Vector3.Cross(Vector3.UnitY, Vector3.UnitZ));
        Assert.Equal(Vector3.UnitY, Vector3.Cross(Vector3.UnitZ, Vector3.UnitX));
    }

    /// <summary>Verifies that the cross product of parallel vectors is zero.</summary>
    [Fact]
    public void Cross_ParallelVectors_IsZero()
    {
        var v = new Vector3(1f, 2f, 3f);

        Assert.Equal(Vector3.Zero, Vector3.Cross(v, v));
        Assert.Equal(Vector3.Zero, Vector3.Cross(v, v * 2f));
    }

    /// <summary>Verifies that <see cref="Vector3.Lerp"/> returns the start vector at t=0 and end vector at t=1.</summary>
    [Fact]
    public void Lerp_Endpoints_ReturnExactly()
    {
        var a = new Vector3(1f, 2f, 3f);
        var b = new Vector3(4f, 5f, 6f);

        Assert.Equal(a, Vector3.Lerp(a, b, 0f));
        Assert.Equal(b, Vector3.Lerp(a, b, 1f));
    }

    /// <summary>Verifies that <see cref="Vector3.Lerp"/> returns the midpoint at t=0.5.</summary>
    [Fact]
    public void Lerp_Midpoint_IsAverage()
    {
        var a = new Vector3(1f, 2f, 3f);
        var b = new Vector3(3f, 6f, 9f);

        Assert.Equal(new Vector3(2f, 4f, 6f), Vector3.Lerp(a, b, 0.5f));
    }

    /// <summary>Verifies <see cref="Vector3.Distance"/> between two points.</summary>
    [Fact]
    public void Distance_MatchesEuclideanDistance()
    {
        var a = new Vector3(1f, 2f, 3f);
        var b = new Vector3(4f, 6f, 3f);

        // (b - a) = (3, 4, 0), length = 5
        Assert.Equal(5f, Vector3.Distance(a, b));
    }

    /// <summary>Verifies that the distance from a point to itself is zero.</summary>
    [Fact]
    public void Distance_SamePoint_IsZero()
    {
        var v = new Vector3(1f, 2f, 3f);

        Assert.Equal(0f, Vector3.Distance(v, v));
    }

    #endregion

    #region Min & Max

    /// <summary>Verifies component-wise minimum.</summary>
    [Fact]
    public void Min_IsComponentWise()
    {
        var a = new Vector3(1f, 5f, 3f);
        var b = new Vector3(4f, 2f, 6f);

        Assert.Equal(new Vector3(1f, 2f, 3f), Vector3.Min(a, b));
    }

    /// <summary>Verifies component-wise maximum.</summary>
    [Fact]
    public void Max_IsComponentWise()
    {
        var a = new Vector3(1f, 5f, 3f);
        var b = new Vector3(4f, 2f, 6f);

        Assert.Equal(new Vector3(4f, 5f, 6f), Vector3.Max(a, b));
    }

    #endregion

    #region Equality & Hashing

    /// <summary>Verifies that identical vectors compare equal and hash identically.</summary>
    [Fact]
    public void Equality_IdenticalVectors_CompareEqual()
    {
        var a = new Vector3(1.25f, -2.5f, 3.75f);
        var b = new Vector3(1.25f, -2.5f, 3.75f);

        Assert.True(a.Equals(b));
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>Verifies that vectors differing in any component are not equal.</summary>
    [Fact]
    public void Equality_DifferentVectors_AreNotEqual()
    {
        var a = new Vector3(1f, 2f, 3f);
        var bX = new Vector3(99f, 2f, 3f);
        var bY = new Vector3(1f, 99f, 3f);
        var bZ = new Vector3(1f, 2f, 99f);

        Assert.False(a == bX);
        Assert.False(a == bY);
        Assert.False(a == bZ);
        Assert.True(a != bX);
    }

    /// <summary>
    /// Verifies that <see cref="Vector3.Equals(object)"/> returns false for
    /// non-Vector3 values.
    /// </summary>
    [Fact]
    public void EqualsObject_ReturnsFalseForOtherTypes()
    {
        var v = new Vector3(1f, 2f, 3f);

        Assert.False(v.Equals((object)"not a vector"));
        Assert.False(v.Equals((object?)null));
    }

    #endregion

    #region Deconstruction & ToString

    /// <summary>Verifies that <see cref="Vector3.Deconstruct(out float, out float, out float)"/> yields components.</summary>
    [Fact]
    public void Deconstruct_YieldsComponents()
    {
        var v = new Vector3(1.5f, -2.5f, 3.5f);

        var (x, y, z) = v;

        Assert.Equal(1.5f, x);
        Assert.Equal(-2.5f, y);
        Assert.Equal(3.5f, z);
    }

    /// <summary>Verifies that ToString contains all three components.</summary>
    [Fact]
    public void ToString_ContainsAllComponents()
    {
        var v = new Vector3(1.5f, -2.5f, 3.5f);

        var s = v.ToString();

        Assert.Contains("1.5", s);
        Assert.Contains("-2.5", s);
        Assert.Contains("3.5", s);
    }

    #endregion
}
