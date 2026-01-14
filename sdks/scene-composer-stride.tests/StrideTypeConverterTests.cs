using BeyondImmersion.Bannou.SceneComposer.Math;
using BeyondImmersion.Bannou.SceneComposer.Stride;
using Xunit;
using StrideVec2 = Stride.Core.Mathematics.Vector2;
using StrideVec3 = Stride.Core.Mathematics.Vector3;
using StrideQuat = Stride.Core.Mathematics.Quaternion;
using StrideColor = Stride.Core.Mathematics.Color;
using StrideColor4 = Stride.Core.Mathematics.Color4;
using StrideRay = Stride.Core.Mathematics.Ray;
using SdkVec2 = BeyondImmersion.Bannou.SceneComposer.Abstractions.Vector2;
using SdkVec3 = BeyondImmersion.Bannou.SceneComposer.Math.Vector3;
using SdkQuat = BeyondImmersion.Bannou.SceneComposer.Math.Quaternion;
using SdkColor = BeyondImmersion.Bannou.SceneComposer.Math.Color;
using SdkRay = BeyondImmersion.Bannou.SceneComposer.Math.Ray;

namespace BeyondImmersion.Bannou.SceneComposer.Stride.Tests;

/// <summary>
/// Unit tests for StrideTypeConverter conversions between SDK and Stride types.
/// </summary>
/// <remarks>
/// SDK types use double precision, Stride types use single precision.
/// These tests verify correct conversion and acceptable precision loss.
/// </remarks>
public class StrideTypeConverterTests
{
    private const float FloatTolerance = 1e-6f;
    private const double DoubleTolerance = 1e-6;

    #region Vector3 Conversions

    [Fact]
    public void Vector3_SdkToStride_ConvertsCorrectly()
    {
        // Arrange
        var sdk = new SdkVec3(1.5, 2.5, 3.5);

        // Act
        var stride = sdk.ToStride();

        // Assert
        Assert.Equal(1.5f, stride.X, FloatTolerance);
        Assert.Equal(2.5f, stride.Y, FloatTolerance);
        Assert.Equal(3.5f, stride.Z, FloatTolerance);
    }

    [Fact]
    public void Vector3_StrideToSdk_ConvertsCorrectly()
    {
        // Arrange
        var stride = new StrideVec3(1.5f, 2.5f, 3.5f);

        // Act
        var sdk = stride.ToSdk();

        // Assert
        Assert.Equal(1.5, sdk.X, DoubleTolerance);
        Assert.Equal(2.5, sdk.Y, DoubleTolerance);
        Assert.Equal(3.5, sdk.Z, DoubleTolerance);
    }

    [Fact]
    public void Vector3_Roundtrip_PreservesValues()
    {
        // Arrange
        var original = new SdkVec3(10.123, -20.456, 30.789);

        // Act
        var roundtrip = original.ToStride().ToSdk();

        // Assert - some precision loss expected due to float conversion
        Assert.Equal(original.X, roundtrip.X, 4); // 4 decimal places
        Assert.Equal(original.Y, roundtrip.Y, 4);
        Assert.Equal(original.Z, roundtrip.Z, 4);
    }

    [Fact]
    public void Vector3_Zero_ConvertsCorrectly()
    {
        // Arrange
        var sdk = SdkVec3.Zero;

        // Act
        var stride = sdk.ToStride();

        // Assert
        Assert.Equal(0f, stride.X);
        Assert.Equal(0f, stride.Y);
        Assert.Equal(0f, stride.Z);
    }

    [Fact]
    public void Vector3_NegativeValues_ConvertsCorrectly()
    {
        // Arrange
        var sdk = new SdkVec3(-100.5, -200.5, -300.5);

        // Act
        var stride = sdk.ToStride();

        // Assert
        Assert.Equal(-100.5f, stride.X, FloatTolerance);
        Assert.Equal(-200.5f, stride.Y, FloatTolerance);
        Assert.Equal(-300.5f, stride.Z, FloatTolerance);
    }

    #endregion

    #region Vector2 Conversions

    [Fact]
    public void Vector2_SdkToStride_ConvertsCorrectly()
    {
        // Arrange
        var sdk = new SdkVec2(100.5, 200.5);

        // Act
        var stride = sdk.ToStride();

        // Assert
        Assert.Equal(100.5f, stride.X, FloatTolerance);
        Assert.Equal(200.5f, stride.Y, FloatTolerance);
    }

    [Fact]
    public void Vector2_StrideToSdk_ConvertsCorrectly()
    {
        // Arrange
        var stride = new StrideVec2(100.5f, 200.5f);

        // Act
        var sdk = stride.ToSdk();

        // Assert
        Assert.Equal(100.5, sdk.X, DoubleTolerance);
        Assert.Equal(200.5, sdk.Y, DoubleTolerance);
    }

    [Fact]
    public void Vector2_Roundtrip_PreservesValues()
    {
        // Arrange
        var original = new SdkVec2(1920.5, 1080.5);

        // Act
        var roundtrip = original.ToStride().ToSdk();

        // Assert
        Assert.Equal(original.X, roundtrip.X, 4);
        Assert.Equal(original.Y, roundtrip.Y, 4);
    }

    #endregion

    #region Quaternion Conversions

    [Fact]
    public void Quaternion_SdkToStride_ConvertsCorrectly()
    {
        // Arrange - normalized quaternion
        var sdk = new SdkQuat(0.0, 0.707, 0.0, 0.707); // 90 degrees around Y

        // Act
        var stride = sdk.ToStride();

        // Assert
        Assert.Equal(0.0f, stride.X, FloatTolerance);
        Assert.Equal(0.707f, stride.Y, 0.001f);
        Assert.Equal(0.0f, stride.Z, FloatTolerance);
        Assert.Equal(0.707f, stride.W, 0.001f);
    }

    [Fact]
    public void Quaternion_StrideToSdk_ConvertsCorrectly()
    {
        // Arrange
        var stride = new StrideQuat(0.0f, 0.707f, 0.0f, 0.707f);

        // Act
        var sdk = stride.ToSdk();

        // Assert
        Assert.Equal(0.0, sdk.X, DoubleTolerance);
        Assert.Equal(0.707, sdk.Y, 0.001);
        Assert.Equal(0.0, sdk.Z, DoubleTolerance);
        Assert.Equal(0.707, sdk.W, 0.001);
    }

    [Fact]
    public void Quaternion_Identity_ConvertsCorrectly()
    {
        // Arrange
        var sdk = SdkQuat.Identity;

        // Act
        var stride = sdk.ToStride();

        // Assert
        Assert.Equal(0f, stride.X, FloatTolerance);
        Assert.Equal(0f, stride.Y, FloatTolerance);
        Assert.Equal(0f, stride.Z, FloatTolerance);
        Assert.Equal(1f, stride.W, FloatTolerance);
    }

    [Fact]
    public void Quaternion_Roundtrip_PreservesValues()
    {
        // Arrange
        var original = new SdkQuat(0.1, 0.2, 0.3, 0.9273); // Approximately normalized

        // Act
        var roundtrip = original.ToStride().ToSdk();

        // Assert
        Assert.Equal(original.X, roundtrip.X, 4);
        Assert.Equal(original.Y, roundtrip.Y, 4);
        Assert.Equal(original.Z, roundtrip.Z, 4);
        Assert.Equal(original.W, roundtrip.W, 4);
    }

    #endregion

    #region Transform Conversions

    [Fact]
    public void Transform_SdkToStride_ConvertsAllComponents()
    {
        // Arrange
        var position = new SdkVec3(10, 20, 30);
        var rotation = SdkQuat.Identity;
        var scale = new SdkVec3(1, 2, 3);
        var transform = new Transform(position, rotation, scale);

        // Act
        var (stridePos, strideRot, strideScale) = transform.ToStride();

        // Assert
        Assert.Equal(10f, stridePos.X, FloatTolerance);
        Assert.Equal(20f, stridePos.Y, FloatTolerance);
        Assert.Equal(30f, stridePos.Z, FloatTolerance);

        Assert.Equal(0f, strideRot.X, FloatTolerance);
        Assert.Equal(0f, strideRot.Y, FloatTolerance);
        Assert.Equal(0f, strideRot.Z, FloatTolerance);
        Assert.Equal(1f, strideRot.W, FloatTolerance);

        Assert.Equal(1f, strideScale.X, FloatTolerance);
        Assert.Equal(2f, strideScale.Y, FloatTolerance);
        Assert.Equal(3f, strideScale.Z, FloatTolerance);
    }

    [Fact]
    public void Transform_StrideToSdk_ConvertsAllComponents()
    {
        // Arrange
        var position = new StrideVec3(10f, 20f, 30f);
        var rotation = StrideQuat.Identity;
        var scale = new StrideVec3(1f, 2f, 3f);

        // Act
        var transform = StrideTypeConverter.ToSdkTransform(position, rotation, scale);

        // Assert
        Assert.Equal(10, transform.Position.X, DoubleTolerance);
        Assert.Equal(20, transform.Position.Y, DoubleTolerance);
        Assert.Equal(30, transform.Position.Z, DoubleTolerance);

        Assert.Equal(0, transform.Rotation.X, DoubleTolerance);
        Assert.Equal(0, transform.Rotation.Y, DoubleTolerance);
        Assert.Equal(0, transform.Rotation.Z, DoubleTolerance);
        Assert.Equal(1, transform.Rotation.W, DoubleTolerance);

        Assert.Equal(1, transform.Scale.X, DoubleTolerance);
        Assert.Equal(2, transform.Scale.Y, DoubleTolerance);
        Assert.Equal(3, transform.Scale.Z, DoubleTolerance);
    }

    #endregion

    #region Color Conversions

    [Fact]
    public void Color_SdkToStrideColor_ConvertsCorrectly()
    {
        // Arrange
        var sdk = new SdkColor(255, 128, 64, 200);

        // Act
        var stride = sdk.ToStride();

        // Assert
        Assert.Equal(255, stride.R);
        Assert.Equal(128, stride.G);
        Assert.Equal(64, stride.B);
        Assert.Equal(200, stride.A);
    }

    [Fact]
    public void Color_SdkToStrideColor4_ConvertsCorrectly()
    {
        // Arrange
        var sdk = new SdkColor(255, 128, 0, 255);

        // Act
        var stride = sdk.ToStrideColor4();

        // Assert
        Assert.Equal(1.0f, stride.R, FloatTolerance);
        Assert.Equal(128f / 255f, stride.G, FloatTolerance);
        Assert.Equal(0.0f, stride.B, FloatTolerance);
        Assert.Equal(1.0f, stride.A, FloatTolerance);
    }

    [Fact]
    public void Color_StrideColorToSdk_ConvertsCorrectly()
    {
        // Arrange
        var stride = new StrideColor(255, 128, 64, 200);

        // Act
        var sdk = stride.ToSdk();

        // Assert
        Assert.Equal(255, sdk.R);
        Assert.Equal(128, sdk.G);
        Assert.Equal(64, sdk.B);
        Assert.Equal(200, sdk.A);
    }

    [Fact]
    public void Color_StrideColor4ToSdk_ConvertsCorrectly()
    {
        // Arrange
        var stride = new StrideColor4(1.0f, 0.5f, 0.25f, 0.75f);

        // Act
        var sdk = stride.ToSdk();

        // Assert - Note: FromFloat truncates, doesn't round
        Assert.Equal(255, sdk.R);
        Assert.Equal(127, sdk.G); // 0.5 * 255 = 127.5, truncated to 127
        Assert.Equal(63, sdk.B);  // 0.25 * 255 = 63.75, truncated to 63
        Assert.Equal(191, sdk.A); // 0.75 * 255 = 191.25, truncated to 191
    }

    [Fact]
    public void Color_Roundtrip_PreservesValues()
    {
        // Arrange
        var original = new SdkColor(100, 150, 200, 250);

        // Act
        var roundtrip = original.ToStride().ToSdk();

        // Assert
        Assert.Equal(original.R, roundtrip.R);
        Assert.Equal(original.G, roundtrip.G);
        Assert.Equal(original.B, roundtrip.B);
        Assert.Equal(original.A, roundtrip.A);
    }

    [Fact]
    public void Color_Black_ConvertsCorrectly()
    {
        // Arrange
        var sdk = new SdkColor(0, 0, 0, 255);

        // Act
        var stride = sdk.ToStride();
        var stride4 = sdk.ToStrideColor4();

        // Assert
        Assert.Equal(0, stride.R);
        Assert.Equal(0, stride.G);
        Assert.Equal(0, stride.B);
        Assert.Equal(255, stride.A);

        Assert.Equal(0f, stride4.R, FloatTolerance);
        Assert.Equal(0f, stride4.G, FloatTolerance);
        Assert.Equal(0f, stride4.B, FloatTolerance);
        Assert.Equal(1f, stride4.A, FloatTolerance);
    }

    [Fact]
    public void Color_White_ConvertsCorrectly()
    {
        // Arrange
        var sdk = new SdkColor(255, 255, 255, 255);

        // Act
        var stride4 = sdk.ToStrideColor4();

        // Assert
        Assert.Equal(1f, stride4.R, FloatTolerance);
        Assert.Equal(1f, stride4.G, FloatTolerance);
        Assert.Equal(1f, stride4.B, FloatTolerance);
        Assert.Equal(1f, stride4.A, FloatTolerance);
    }

    #endregion

    #region Ray Conversions

    [Fact]
    public void Ray_SdkToStride_ConvertsCorrectly()
    {
        // Arrange
        var origin = new SdkVec3(0, 10, 0);
        var direction = new SdkVec3(0, -1, 0);
        var sdk = new SdkRay(origin, direction);

        // Act
        var stride = sdk.ToStride();

        // Assert
        Assert.Equal(0f, stride.Position.X, FloatTolerance);
        Assert.Equal(10f, stride.Position.Y, FloatTolerance);
        Assert.Equal(0f, stride.Position.Z, FloatTolerance);

        Assert.Equal(0f, stride.Direction.X, FloatTolerance);
        Assert.Equal(-1f, stride.Direction.Y, FloatTolerance);
        Assert.Equal(0f, stride.Direction.Z, FloatTolerance);
    }

    [Fact]
    public void Ray_StrideToSdk_ConvertsCorrectly()
    {
        // Arrange
        var stride = new StrideRay(
            new StrideVec3(5f, 10f, 15f),
            new StrideVec3(1f, 0f, 0f));

        // Act
        var sdk = stride.ToSdk();

        // Assert
        Assert.Equal(5, sdk.Origin.X, DoubleTolerance);
        Assert.Equal(10, sdk.Origin.Y, DoubleTolerance);
        Assert.Equal(15, sdk.Origin.Z, DoubleTolerance);

        Assert.Equal(1, sdk.Direction.X, DoubleTolerance);
        Assert.Equal(0, sdk.Direction.Y, DoubleTolerance);
        Assert.Equal(0, sdk.Direction.Z, DoubleTolerance);
    }

    [Fact]
    public void Ray_Roundtrip_PreservesValues()
    {
        // Arrange
        var origin = new SdkVec3(100, 200, 300);
        var direction = new SdkVec3(0.577, 0.577, 0.577); // Approximately normalized
        var original = new SdkRay(origin, direction);

        // Act
        var roundtrip = original.ToStride().ToSdk();

        // Assert
        Assert.Equal(original.Origin.X, roundtrip.Origin.X, 4);
        Assert.Equal(original.Origin.Y, roundtrip.Origin.Y, 4);
        Assert.Equal(original.Origin.Z, roundtrip.Origin.Z, 4);

        Assert.Equal(original.Direction.X, roundtrip.Direction.X, 4);
        Assert.Equal(original.Direction.Y, roundtrip.Direction.Y, 4);
        Assert.Equal(original.Direction.Z, roundtrip.Direction.Z, 4);
    }

    #endregion

    #region Precision Loss Tests

    [Fact]
    public void Vector3_LargeValues_HasAcceptablePrecisionLoss()
    {
        // Arrange - large values that may lose precision in float
        var sdk = new SdkVec3(1000000.123456, 2000000.654321, 3000000.111222);

        // Act
        var stride = sdk.ToStride();
        var roundtrip = stride.ToSdk();

        // Assert - float has ~7 significant digits, so we expect some loss
        // The values should be close but not exactly equal
        Assert.Equal(sdk.X, roundtrip.X, 0); // Within 1 unit for million-scale values
        Assert.Equal(sdk.Y, roundtrip.Y, 0);
        Assert.Equal(sdk.Z, roundtrip.Z, 0);
    }

    [Fact]
    public void Vector3_SmallValues_PreservesPrecision()
    {
        // Arrange - small values should preserve precision well
        var sdk = new SdkVec3(0.001, 0.002, 0.003);

        // Act
        var roundtrip = sdk.ToStride().ToSdk();

        // Assert
        Assert.Equal(sdk.X, roundtrip.X, 5);
        Assert.Equal(sdk.Y, roundtrip.Y, 5);
        Assert.Equal(sdk.Z, roundtrip.Z, 5);
    }

    #endregion
}
