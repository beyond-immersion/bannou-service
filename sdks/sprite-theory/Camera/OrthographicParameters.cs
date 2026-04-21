using BeyondImmersion.Bannou.Core.Math;

namespace BeyondImmersion.Bannou.SpriteTheory.Camera;

/// <summary>
/// Output of <see cref="OrthographicSetup.Compute"/>: the complete set of camera parameters
/// needed to configure an orthographic camera that frames a bounding box from a given angle.
/// </summary>
/// <remarks>
/// All vectors use the shared <see cref="Vector3"/> primitive from
/// <c>BeyondImmersion.Bannou.Core.Math</c>. The bridge maps these to engine-specific
/// camera types (e.g., Stride's <c>CameraComponent</c>).
/// </remarks>
/// <param name="Position">Camera world position, placed along the negative direction from the bounding box center.</param>
/// <param name="Direction">Camera forward direction vector (normalized).</param>
/// <param name="Up">Camera up vector (normalized), accounting for gimbal lock near vertical angles.</param>
/// <param name="OrthoWidth">Orthographic viewport width in world units.</param>
/// <param name="OrthoHeight">Orthographic viewport height in world units.</param>
/// <param name="NearPlane">Near clip plane distance.</param>
/// <param name="FarPlane">Far clip plane distance.</param>
public record OrthographicParameters(
    Vector3 Position,
    Vector3 Direction,
    Vector3 Up,
    float OrthoWidth,
    float OrthoHeight,
    float NearPlane,
    float FarPlane);
