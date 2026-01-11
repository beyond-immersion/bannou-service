using Xunit;
using GodotVector3 = Godot.Vector3;
using GodotVector2 = Godot.Vector2;
using GodotQuaternion = Godot.Quaternion;
using GodotTransform3D = Godot.Transform3D;
using GodotBasis = Godot.Basis;
using GodotColor = Godot.Color;
using SdkVector3 = BeyondImmersion.Bannou.SceneComposer.Math.Vector3;
using SdkVector2 = BeyondImmersion.Bannou.SceneComposer.Abstractions.Vector2;
using SdkQuaternion = BeyondImmersion.Bannou.SceneComposer.Math.Quaternion;
using SdkTransform = BeyondImmersion.Bannou.SceneComposer.Math.Transform;
using SdkColor = BeyondImmersion.Bannou.SceneComposer.Math.Color;
using SdkRay = BeyondImmersion.Bannou.SceneComposer.Math.Ray;

namespace BeyondImmersion.Bannou.Godot.SceneComposer.Tests.Conversion;

/// <summary>
/// Tests for GodotTypeConverter - verifies bidirectional conversion between SDK and Godot types.
/// </summary>
public class GodotTypeConverterTests
{
    private const float FloatEpsilon = 1e-5f;
    private const double DoubleEpsilon = 1e-10;

    // =========================================================================
    // VECTOR3 CONVERSION TESTS
    // =========================================================================

    [Fact]
    public void Vector3_ToGodot_ConvertsComponents()
    {
        var sdk = new SdkVector3(1.5, 2.5, 3.5);

        var godot = sdk.ToGodot();

        Assert.Equal(1.5f, godot.X, FloatEpsilon);
        Assert.Equal(2.5f, godot.Y, FloatEpsilon);
        Assert.Equal(3.5f, godot.Z, FloatEpsilon);
    }

    [Fact]
    public void Vector3_ToSdk_ConvertsComponents()
    {
        var godot = new GodotVector3(1.5f, 2.5f, 3.5f);

        var sdk = godot.ToSdk();

        Assert.Equal(1.5, sdk.X, DoubleEpsilon);
        Assert.Equal(2.5, sdk.Y, DoubleEpsilon);
        Assert.Equal(3.5, sdk.Z, DoubleEpsilon);
    }

    [Fact]
    public void Vector3_RoundTrip_PreservesValues()
    {
        var original = new SdkVector3(123.456, -789.012, 345.678);

        var roundTripped = original.ToGodot().ToSdk();

        // Float precision loss is expected, but should be close
        Assert.Equal(original.X, roundTripped.X, 1e-3);
        Assert.Equal(original.Y, roundTripped.Y, 1e-3);
        Assert.Equal(original.Z, roundTripped.Z, 1e-3);
    }

    [Fact]
    public void Vector3_Zero_ConvertsCorrectly()
    {
        var sdkZero = SdkVector3.Zero;

        var godot = sdkZero.ToGodot();

        Assert.Equal(GodotVector3.Zero, godot);
    }

    [Fact]
    public void Vector3_UnitVectors_ConvertCorrectly()
    {
        Assert.Equal(GodotVector3.Right, SdkVector3.UnitX.ToGodot());
        Assert.Equal(GodotVector3.Up, SdkVector3.UnitY.ToGodot());
        Assert.Equal(GodotVector3.Back, SdkVector3.UnitZ.ToGodot());
    }

    [Fact]
    public void Vector3_NegativeValues_ConvertCorrectly()
    {
        var sdk = new SdkVector3(-10.0, -20.0, -30.0);

        var godot = sdk.ToGodot();

        Assert.Equal(-10.0f, godot.X, FloatEpsilon);
        Assert.Equal(-20.0f, godot.Y, FloatEpsilon);
        Assert.Equal(-30.0f, godot.Z, FloatEpsilon);
    }

    // =========================================================================
    // VECTOR2 CONVERSION TESTS
    // =========================================================================

    [Fact]
    public void Vector2_ToGodot_ConvertsComponents()
    {
        var sdk = new SdkVector2(100.5, 200.5);

        var godot = sdk.ToGodot();

        Assert.Equal(100.5f, godot.X, FloatEpsilon);
        Assert.Equal(200.5f, godot.Y, FloatEpsilon);
    }

    [Fact]
    public void Vector2_ToSdk_ConvertsComponents()
    {
        var godot = new GodotVector2(100.5f, 200.5f);

        var sdk = godot.ToSdk();

        Assert.Equal(100.5, sdk.X, DoubleEpsilon);
        Assert.Equal(200.5, sdk.Y, DoubleEpsilon);
    }

    [Fact]
    public void Vector2_Zero_ConvertsCorrectly()
    {
        var sdkZero = new SdkVector2(0, 0);

        var godot = sdkZero.ToGodot();

        Assert.Equal(GodotVector2.Zero, godot);
    }

    // =========================================================================
    // QUATERNION CONVERSION TESTS
    // =========================================================================

    [Fact]
    public void Quaternion_Identity_ConvertsCorrectly()
    {
        var sdkIdentity = SdkQuaternion.Identity;

        var godot = sdkIdentity.ToGodot();

        Assert.Equal(0f, godot.X, FloatEpsilon);
        Assert.Equal(0f, godot.Y, FloatEpsilon);
        Assert.Equal(0f, godot.Z, FloatEpsilon);
        Assert.Equal(1f, godot.W, FloatEpsilon);
    }

    [Fact]
    public void Quaternion_ToGodot_ConvertsComponents()
    {
        var sdk = new SdkQuaternion(0.1, 0.2, 0.3, 0.9);

        var godot = sdk.ToGodot();

        Assert.Equal(0.1f, godot.X, FloatEpsilon);
        Assert.Equal(0.2f, godot.Y, FloatEpsilon);
        Assert.Equal(0.3f, godot.Z, FloatEpsilon);
        Assert.Equal(0.9f, godot.W, FloatEpsilon);
    }

    [Fact]
    public void Quaternion_ToSdk_ConvertsComponents()
    {
        var godot = new GodotQuaternion(0.1f, 0.2f, 0.3f, 0.9f);

        var sdk = godot.ToSdk();

        // Use float precision tolerance since values come from floats
        Assert.Equal(0.1, sdk.X, 1e-6);
        Assert.Equal(0.2, sdk.Y, 1e-6);
        Assert.Equal(0.3, sdk.Z, 1e-6);
        Assert.Equal(0.9, sdk.W, 1e-6);
    }

    [Fact]
    public void Quaternion_90DegreeRotation_ConvertsCorrectly()
    {
        // 90 degree rotation around Y axis
        var sdk = SdkQuaternion.FromAxisAngle(SdkVector3.UnitY, System.Math.PI / 2);

        var godot = sdk.ToGodot();
        var backToSdk = godot.ToSdk();

        // Should be approximately equal after round-trip
        Assert.Equal(sdk.X, backToSdk.X, 1e-3);
        Assert.Equal(sdk.Y, backToSdk.Y, 1e-3);
        Assert.Equal(sdk.Z, backToSdk.Z, 1e-3);
        Assert.Equal(sdk.W, backToSdk.W, 1e-3);
    }

    // =========================================================================
    // COLOR CONVERSION TESTS
    // SDK Color uses byte (0-255), Godot Color uses float (0-1)
    // =========================================================================

    [Fact]
    public void Color_ToGodot_ConvertsComponents()
    {
        // SDK uses bytes (0-255), Godot uses floats (0-1)
        var sdk = new SdkColor(128, 153, 178, 204); // ~0.5, 0.6, 0.7, 0.8

        var godot = sdk.ToGodot();

        Assert.Equal(128f / 255f, godot.R, 0.01f);
        Assert.Equal(153f / 255f, godot.G, 0.01f);
        Assert.Equal(178f / 255f, godot.B, 0.01f);
        Assert.Equal(204f / 255f, godot.A, 0.01f);
    }

    [Fact]
    public void Color_ToSdk_ConvertsComponents()
    {
        var godot = new GodotColor(0.5f, 0.6f, 0.7f, 0.8f);

        var sdk = godot.ToSdk();

        // Byte values should be within 1 of expected (rounding differences)
        Assert.InRange(sdk.R, 127, 128); // 0.5 * 255 = 127.5
        Assert.InRange(sdk.G, 152, 153); // 0.6 * 255 = 153
        Assert.InRange(sdk.B, 178, 179); // 0.7 * 255 = 178.5
        Assert.InRange(sdk.A, 203, 204); // 0.8 * 255 = 204
    }

    [Fact]
    public void Color_White_ConvertsCorrectly()
    {
        var white = SdkColor.White;

        var godot = white.ToGodot();

        Assert.Equal(1f, godot.R, FloatEpsilon);
        Assert.Equal(1f, godot.G, FloatEpsilon);
        Assert.Equal(1f, godot.B, FloatEpsilon);
        Assert.Equal(1f, godot.A, FloatEpsilon);
    }

    [Fact]
    public void Color_Transparent_ConvertsCorrectly()
    {
        var transparent = SdkColor.Transparent;

        var godot = transparent.ToGodot();

        Assert.Equal(0f, godot.R, FloatEpsilon);
        Assert.Equal(0f, godot.G, FloatEpsilon);
        Assert.Equal(0f, godot.B, FloatEpsilon);
        Assert.Equal(0f, godot.A, FloatEpsilon);
    }

    // =========================================================================
    // RAY CONVERSION TESTS
    // =========================================================================

    [Fact]
    public void Ray_ToGodot_ReturnsOriginAndDirection()
    {
        var origin = new SdkVector3(1, 2, 3);
        var direction = new SdkVector3(0, 0, -1);
        var ray = new SdkRay(origin, direction);

        var (godotOrigin, godotDirection) = ray.ToGodot();

        Assert.Equal(1f, godotOrigin.X, FloatEpsilon);
        Assert.Equal(2f, godotOrigin.Y, FloatEpsilon);
        Assert.Equal(3f, godotOrigin.Z, FloatEpsilon);
        Assert.Equal(0f, godotDirection.X, FloatEpsilon);
        Assert.Equal(0f, godotDirection.Y, FloatEpsilon);
        Assert.Equal(-1f, godotDirection.Z, FloatEpsilon);
    }

    [Fact]
    public void Ray_DiagonalDirection_ConvertsCorrectly()
    {
        var origin = SdkVector3.Zero;
        var direction = new SdkVector3(1, 1, 1).Normalized;
        var ray = new SdkRay(origin, direction);

        var (_, godotDirection) = ray.ToGodot();

        // Should be normalized
        var length = System.Math.Sqrt(godotDirection.X * godotDirection.X +
                                       godotDirection.Y * godotDirection.Y +
                                       godotDirection.Z * godotDirection.Z);
        Assert.Equal(1.0, length, 1e-3);
    }

    // =========================================================================
    // TRANSFORM CONVERSION TESTS
    // =========================================================================

    [Fact]
    public void Transform_Identity_ToGodot_ConvertsCorrectly()
    {
        var sdk = SdkTransform.Identity;

        var godot = sdk.ToGodot();

        // Origin should be zero
        Assert.Equal(0f, godot.Origin.X, FloatEpsilon);
        Assert.Equal(0f, godot.Origin.Y, FloatEpsilon);
        Assert.Equal(0f, godot.Origin.Z, FloatEpsilon);

        // Basis should be identity (diagonal 1s)
        Assert.Equal(1f, godot.Basis.X.X, FloatEpsilon);
        Assert.Equal(1f, godot.Basis.Y.Y, FloatEpsilon);
        Assert.Equal(1f, godot.Basis.Z.Z, FloatEpsilon);
    }

    [Fact]
    public void Transform_WithPosition_ToGodot_ConvertsCorrectly()
    {
        var sdk = new SdkTransform(
            new SdkVector3(10, 20, 30),
            SdkQuaternion.Identity,
            SdkVector3.One);

        var godot = sdk.ToGodot();

        Assert.Equal(10f, godot.Origin.X, FloatEpsilon);
        Assert.Equal(20f, godot.Origin.Y, FloatEpsilon);
        Assert.Equal(30f, godot.Origin.Z, FloatEpsilon);
    }

    [Fact]
    public void Transform_WithScale_ToGodot_ConvertsCorrectly()
    {
        var sdk = new SdkTransform(
            SdkVector3.Zero,
            SdkQuaternion.Identity,
            new SdkVector3(2, 3, 4));

        var godot = sdk.ToGodot();

        // Scale should be in the basis column lengths
        var scaleX = godot.Basis.X.Length();
        var scaleY = godot.Basis.Y.Length();
        var scaleZ = godot.Basis.Z.Length();

        Assert.Equal(2f, scaleX, FloatEpsilon);
        Assert.Equal(3f, scaleY, FloatEpsilon);
        Assert.Equal(4f, scaleZ, FloatEpsilon);
    }

    [Fact]
    public void Transform_ToSdk_DecomposesCorrectly()
    {
        // Create a Godot transform with known position, rotation, scale
        var position = new GodotVector3(5, 10, 15);
        var basis = GodotBasis.Identity.Scaled(new GodotVector3(2, 2, 2));
        var godot = new GodotTransform3D(basis, position);

        var sdk = godot.ToSdk();

        Assert.Equal(5.0, sdk.Position.X, 1e-3);
        Assert.Equal(10.0, sdk.Position.Y, 1e-3);
        Assert.Equal(15.0, sdk.Position.Z, 1e-3);
        Assert.Equal(2.0, sdk.Scale.X, 1e-3);
        Assert.Equal(2.0, sdk.Scale.Y, 1e-3);
        Assert.Equal(2.0, sdk.Scale.Z, 1e-3);
    }

    [Fact]
    public void Transform_RoundTrip_PreservesPosition()
    {
        var original = new SdkTransform(
            new SdkVector3(100, 200, 300),
            SdkQuaternion.Identity,
            SdkVector3.One);

        var roundTripped = original.ToGodot().ToSdk();

        Assert.Equal(original.Position.X, roundTripped.Position.X, 1e-3);
        Assert.Equal(original.Position.Y, roundTripped.Position.Y, 1e-3);
        Assert.Equal(original.Position.Z, roundTripped.Position.Z, 1e-3);
    }

    [Fact]
    public void Transform_RoundTrip_PreservesScale()
    {
        var original = new SdkTransform(
            SdkVector3.Zero,
            SdkQuaternion.Identity,
            new SdkVector3(0.5, 1.5, 2.5));

        var roundTripped = original.ToGodot().ToSdk();

        Assert.Equal(original.Scale.X, roundTripped.Scale.X, 1e-3);
        Assert.Equal(original.Scale.Y, roundTripped.Scale.Y, 1e-3);
        Assert.Equal(original.Scale.Z, roundTripped.Scale.Z, 1e-3);
    }

    [Fact]
    public void Transform_RoundTrip_PreservesRotation()
    {
        // 45 degree rotation around Y
        var rotation = SdkQuaternion.FromAxisAngle(SdkVector3.UnitY, System.Math.PI / 4);
        var original = new SdkTransform(SdkVector3.Zero, rotation, SdkVector3.One);

        var roundTripped = original.ToGodot().ToSdk();

        // Quaternion comparison (accounting for sign ambiguity)
        var dotProduct = System.Math.Abs(
            original.Rotation.X * roundTripped.Rotation.X +
            original.Rotation.Y * roundTripped.Rotation.Y +
            original.Rotation.Z * roundTripped.Rotation.Z +
            original.Rotation.W * roundTripped.Rotation.W);

        Assert.True(dotProduct > 0.999, $"Rotation mismatch: dot product = {dotProduct}");
    }

    // =========================================================================
    // EDGE CASES AND PRECISION TESTS
    // =========================================================================

    [Fact]
    public void Vector3_LargeValues_ConvertWithinFloatPrecision()
    {
        var sdk = new SdkVector3(1e6, 1e6, 1e6);

        var godot = sdk.ToGodot();
        var backToSdk = godot.ToSdk();

        // Large values should be preserved within float precision
        Assert.True(System.Math.Abs(sdk.X - backToSdk.X) < 100);
        Assert.True(System.Math.Abs(sdk.Y - backToSdk.Y) < 100);
        Assert.True(System.Math.Abs(sdk.Z - backToSdk.Z) < 100);
    }

    [Fact]
    public void Vector3_SmallValues_ConvertWithinFloatPrecision()
    {
        var sdk = new SdkVector3(1e-6, 1e-6, 1e-6);

        var godot = sdk.ToGodot();
        var backToSdk = godot.ToSdk();

        // Small values should be preserved within float precision
        Assert.True(System.Math.Abs(sdk.X - backToSdk.X) < 1e-5);
        Assert.True(System.Math.Abs(sdk.Y - backToSdk.Y) < 1e-5);
        Assert.True(System.Math.Abs(sdk.Z - backToSdk.Z) < 1e-5);
    }
}
