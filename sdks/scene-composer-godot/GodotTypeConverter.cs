using BeyondImmersion.Bannou.SceneComposer.Math;
using GodotVec3 = Godot.Vector3;
using GodotVec2 = Godot.Vector2;
using GodotQuat = Godot.Quaternion;
using GodotColor = Godot.Color;
using GodotTransform = Godot.Transform3D;
using GodotBasis = Godot.Basis;
using SdkVec3 = BeyondImmersion.Bannou.SceneComposer.Math.Vector3;
using SdkQuat = BeyondImmersion.Bannou.SceneComposer.Math.Quaternion;
using SdkColor = BeyondImmersion.Bannou.SceneComposer.Math.Color;
using SdkRay = BeyondImmersion.Bannou.SceneComposer.Math.Ray;
using SdkVec2 = BeyondImmersion.Bannou.SceneComposer.Abstractions.Vector2;

namespace BeyondImmersion.Bannou.SceneComposer.Godot;

/// <summary>
/// Conversion utilities between SceneComposer SDK types and Godot types.
/// SDK uses double precision; Godot uses single precision.
/// </summary>
public static class GodotTypeConverter
{
    #region Vector3 Conversions

    /// <summary>
    /// Convert SDK Vector3 (double) to Godot Vector3 (float).
    /// </summary>
    public static GodotVec3 ToGodot(this SdkVec3 v) =>
        new((float)v.X, (float)v.Y, (float)v.Z);

    /// <summary>
    /// Convert Godot Vector3 (float) to SDK Vector3 (double).
    /// </summary>
    public static SdkVec3 ToSdk(this GodotVec3 v) =>
        new(v.X, v.Y, v.Z);

    #endregion

    #region Vector2 Conversions

    /// <summary>
    /// Convert SDK Vector2 (double) to Godot Vector2 (float).
    /// </summary>
    public static GodotVec2 ToGodot(this SdkVec2 v) =>
        new((float)v.X, (float)v.Y);

    /// <summary>
    /// Convert Godot Vector2 (float) to SDK Vector2 (double).
    /// </summary>
    public static SdkVec2 ToSdk(this GodotVec2 v) =>
        new(v.X, v.Y);

    #endregion

    #region Quaternion Conversions

    /// <summary>
    /// Convert SDK Quaternion (double) to Godot Quaternion (float).
    /// </summary>
    public static GodotQuat ToGodot(this SdkQuat q) =>
        new((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);

    /// <summary>
    /// Convert Godot Quaternion (float) to SDK Quaternion (double).
    /// </summary>
    public static SdkQuat ToSdk(this GodotQuat q) =>
        new(q.X, q.Y, q.Z, q.W);

    #endregion

    #region Transform Conversions

    /// <summary>
    /// Convert SDK Transform to Godot Transform3D.
    /// Godot's Transform3D combines Basis (rotation+scale) with Origin (position).
    /// </summary>
    public static GodotTransform ToGodot(this Transform t)
    {
        // Create basis from quaternion rotation
        var basis = new GodotBasis(t.Rotation.ToGodot());

        // Apply scale to basis
        basis = basis.Scaled(t.Scale.ToGodot());

        return new GodotTransform(basis, t.Position.ToGodot());
    }

    /// <summary>
    /// Convert Godot Transform3D to SDK Transform.
    /// Decomposes the Basis into separate rotation and scale components.
    /// </summary>
    public static Transform ToSdk(this GodotTransform t)
    {
        // Extract scale from basis (length of each axis vector)
        var scale = t.Basis.Scale;

        // Get rotation by orthonormalizing the basis (removing scale)
        // then extracting the quaternion
        var orthonormalBasis = t.Basis.Orthonormalized();
        var rotation = orthonormalBasis.GetRotationQuaternion();

        return new Transform(
            t.Origin.ToSdk(),
            rotation.ToSdk(),
            scale.ToSdk());
    }

    /// <summary>
    /// Convert SDK Transform to Godot transform components (position, rotation, scale).
    /// Useful when setting Node3D properties individually.
    /// </summary>
    public static (GodotVec3 Position, GodotQuat Rotation, GodotVec3 Scale) ToGodotComponents(this Transform t) =>
        (t.Position.ToGodot(), t.Rotation.ToGodot(), t.Scale.ToGodot());

    /// <summary>
    /// Create SDK Transform from Godot transform components.
    /// </summary>
    public static Transform ToSdkTransform(GodotVec3 position, GodotQuat rotation, GodotVec3 scale) =>
        new(position.ToSdk(), rotation.ToSdk(), scale.ToSdk());

    #endregion

    #region Color Conversions

    /// <summary>
    /// Convert SDK Color (byte 0-255) to Godot Color (float 0-1).
    /// </summary>
    public static GodotColor ToGodot(this SdkColor c) =>
        new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);

    /// <summary>
    /// Convert Godot Color (float 0-1) to SDK Color (byte 0-255).
    /// </summary>
    public static SdkColor ToSdk(this GodotColor c) =>
        SdkColor.FromFloat(c.R, c.G, c.B, c.A);

    #endregion

    #region Ray Conversions

    /// <summary>
    /// Convert SDK Ray to Godot ray components (origin and direction).
    /// Godot doesn't have a built-in Ray struct, so we return a tuple.
    /// </summary>
    public static (GodotVec3 Origin, GodotVec3 Direction) ToGodot(this SdkRay r) =>
        (r.Origin.ToGodot(), r.Direction.ToGodot());

    /// <summary>
    /// Create SDK Ray from Godot ray components.
    /// </summary>
    public static SdkRay ToSdkRay(GodotVec3 origin, GodotVec3 direction) =>
        new(origin.ToSdk(), direction.ToSdk());

    #endregion

    #region Basis Conversions

    /// <summary>
    /// Convert SDK Quaternion to Godot Basis (rotation matrix).
    /// </summary>
    public static GodotBasis ToGodotBasis(this SdkQuat q) =>
        new(q.ToGodot());

    /// <summary>
    /// Extract SDK Quaternion from Godot Basis.
    /// Note: Basis must be orthonormalized for accurate rotation extraction.
    /// </summary>
    public static SdkQuat ToSdkQuaternion(this GodotBasis basis)
    {
        var rotation = basis.Orthonormalized().GetRotationQuaternion();
        return rotation.ToSdk();
    }

    #endregion
}
