using BeyondImmersion.Bannou.SceneComposer.Math;
using SdkColor = BeyondImmersion.Bannou.SceneComposer.Math.Color;
using SdkQuat = BeyondImmersion.Bannou.SceneComposer.Math.Quaternion;
using SdkRay = BeyondImmersion.Bannou.SceneComposer.Math.Ray;
using SdkVec2 = BeyondImmersion.Bannou.SceneComposer.Abstractions.Vector2;
using SdkVec3 = BeyondImmersion.Bannou.SceneComposer.Math.Vector3;
using StrideColor = Stride.Core.Mathematics.Color;
using StrideColor4 = Stride.Core.Mathematics.Color4;
using StrideQuat = Stride.Core.Mathematics.Quaternion;
using StrideRay = Stride.Core.Mathematics.Ray;
using StrideVec3 = Stride.Core.Mathematics.Vector3;

namespace BeyondImmersion.Bannou.SceneComposer.Stride;

/// <summary>
/// Conversion utilities between SceneComposer SDK types and Stride types.
/// SDK uses double precision; Stride uses single precision.
/// </summary>
public static class StrideTypeConverter
{
    #region Vector3 Conversions

    /// <summary>
    /// Convert SDK Vector3 (double) to Stride Vector3 (float).
    /// </summary>
    public static StrideVec3 ToStride(this SdkVec3 v) =>
        new((float)v.X, (float)v.Y, (float)v.Z);

    /// <summary>
    /// Convert Stride Vector3 (float) to SDK Vector3 (double).
    /// </summary>
    public static SdkVec3 ToSdk(this StrideVec3 v) =>
        new(v.X, v.Y, v.Z);

    #endregion

    #region Quaternion Conversions

    /// <summary>
    /// Convert SDK Quaternion (double) to Stride Quaternion (float).
    /// </summary>
    public static StrideQuat ToStride(this SdkQuat q) =>
        new((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);

    /// <summary>
    /// Convert Stride Quaternion (float) to SDK Quaternion (double).
    /// </summary>
    public static SdkQuat ToSdk(this StrideQuat q) =>
        new(q.X, q.Y, q.Z, q.W);

    #endregion

    #region Transform Conversions

    /// <summary>
    /// Convert SDK Transform to Stride transform components.
    /// </summary>
    public static (StrideVec3 Position, StrideQuat Rotation, StrideVec3 Scale) ToStride(this Transform t) =>
        (t.Position.ToStride(), t.Rotation.ToStride(), t.Scale.ToStride());

    /// <summary>
    /// Create SDK Transform from Stride transform components.
    /// </summary>
    public static Transform ToSdkTransform(StrideVec3 position, StrideQuat rotation, StrideVec3 scale) =>
        new(position.ToSdk(), rotation.ToSdk(), scale.ToSdk());

    #endregion

    #region Color Conversions

    /// <summary>
    /// Convert SDK Color to Stride Color (byte components).
    /// </summary>
    public static StrideColor ToStride(this SdkColor c) =>
        new(c.R, c.G, c.B, c.A);

    /// <summary>
    /// Convert SDK Color to Stride Color4 (float components).
    /// </summary>
    public static StrideColor4 ToStrideColor4(this SdkColor c) =>
        new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);

    /// <summary>
    /// Convert Stride Color to SDK Color.
    /// </summary>
    public static SdkColor ToSdk(this StrideColor c) =>
        new(c.R, c.G, c.B, c.A);

    /// <summary>
    /// Convert Stride Color4 to SDK Color.
    /// </summary>
    public static SdkColor ToSdk(this StrideColor4 c) =>
        SdkColor.FromFloat(c.R, c.G, c.B, c.A);

    #endregion

    #region Ray Conversions

    /// <summary>
    /// Convert SDK Ray to Stride Ray.
    /// </summary>
    public static StrideRay ToStride(this SdkRay r) =>
        new(r.Origin.ToStride(), r.Direction.ToStride());

    /// <summary>
    /// Convert Stride Ray to SDK Ray.
    /// </summary>
    public static SdkRay ToSdk(this StrideRay r) =>
        new(r.Position.ToSdk(), r.Direction.ToSdk());

    #endregion

    #region Vector2 Conversions

    /// <summary>
    /// Convert SDK Vector2 to Stride Vector2.
    /// </summary>
    public static global::Stride.Core.Mathematics.Vector2 ToStride(this SdkVec2 v) =>
        new((float)v.X, (float)v.Y);

    /// <summary>
    /// Convert Stride Vector2 to SDK Vector2.
    /// </summary>
    public static SdkVec2 ToSdk(this global::Stride.Core.Mathematics.Vector2 v) =>
        new(v.X, v.Y);

    #endregion
}
