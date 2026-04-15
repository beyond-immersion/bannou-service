namespace BeyondImmersion.Bannou.SpriteTheory.NormalMap;

/// <summary>
/// Configuration options for depth-to-normal-map conversion via <see cref="DepthToNormal"/>.
/// Controls normal map intensity and optional pre-blur smoothing of the depth buffer.
/// </summary>
/// <param name="Strength">Normal map intensity multiplier. Higher values produce more pronounced normals.</param>
/// <param name="BlurRadius">Gaussian blur radius applied to the depth buffer before Sobel convolution. 0 disables blur.</param>
public record NormalMapOptions(
    float Strength = 1.0f,
    int BlurRadius = 0);
