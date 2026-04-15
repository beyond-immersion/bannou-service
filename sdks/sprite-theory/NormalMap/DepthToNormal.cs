namespace BeyondImmersion.Bannou.SpriteTheory.NormalMap;

/// <summary>
/// Converts a depth buffer to a tangent-space normal map using Sobel 3x3 convolution.
/// </summary>
/// <remarks>
/// <para>
/// The Sobel operator estimates horizontal and vertical gradients of the depth field at each pixel.
/// These gradients are used to construct a surface normal vector pointing outward from the sprite plane
/// (positive Z in tangent space). The resulting normal map encodes each component from [-1, 1] to [0, 255].
/// </para>
/// <para>
/// Boundary pixels use clamped sampling — edge values are repeated rather than wrapped or zeroed,
/// preventing dark-border artifacts on normal maps.
/// </para>
/// <para>
/// Reference: Irwin Sobel and Gary Feldman, "A 3x3 Isotropic Gradient Operator for Image Processing"
/// (Stanford AI Project, 1968).
/// </para>
/// </remarks>
public static class DepthToNormal
{
    /// <summary>
    /// Generates a tangent-space normal map from a depth buffer using Sobel 3x3 convolution.
    /// </summary>
    /// <param name="depth">
    /// Depth values in the range 0.0 (near) to 1.0 (far). Must contain exactly
    /// <paramref name="width"/> * <paramref name="height"/> elements.
    /// </param>
    /// <param name="width">Width of the depth buffer in pixels.</param>
    /// <param name="height">Height of the depth buffer in pixels.</param>
    /// <param name="options">Normal map generation options (strength, blur radius).</param>
    /// <returns>
    /// An RGBA byte array of size <paramref name="width"/> * <paramref name="height"/> * 4,
    /// containing the tangent-space normal map with alpha set to 255 for all pixels.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="depth"/> length does not equal <paramref name="width"/> * <paramref name="height"/>.
    /// </exception>
    public static byte[] Generate(float[] depth, int width, int height, NormalMapOptions options)
    {
        if (depth.Length != width * height)
        {
            throw new ArgumentException(
                $"Depth array length ({depth.Length}) must equal width * height ({width * height}).",
                nameof(depth));
        }

        var source = depth;

        if (options.BlurRadius > 0)
        {
            source = GaussianBlur(source, width, height, options.BlurRadius);
        }

        var normals = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // Sobel 3x3 horizontal gradient (Gx)
                var dzdx = (
                    S(source, x + 1, y - 1, width, height) + 2f * S(source, x + 1, y, width, height) + S(source, x + 1, y + 1, width, height)
                    - S(source, x - 1, y - 1, width, height) - 2f * S(source, x - 1, y, width, height) - S(source, x - 1, y + 1, width, height)
                ) * options.Strength;

                // Sobel 3x3 vertical gradient (Gy)
                var dzdy = (
                    S(source, x - 1, y + 1, width, height) + 2f * S(source, x, y + 1, width, height) + S(source, x + 1, y + 1, width, height)
                    - S(source, x - 1, y - 1, width, height) - 2f * S(source, x, y - 1, width, height) - S(source, x + 1, y - 1, width, height)
                ) * options.Strength;

                // Normal vector (tangent space: Z outward from sprite plane)
                var len = MathF.Sqrt(dzdx * dzdx + dzdy * dzdy + 1.0f);
                var nx = -dzdx / len;
                var ny = -dzdy / len;
                var nz = 1.0f / len;

                // Encode [-1,1] -> [0,255]
                var i = (y * width + x) * 4;
                normals[i + 0] = (byte)(MathF.Round((nx * 0.5f + 0.5f) * 255f));
                normals[i + 1] = (byte)(MathF.Round((ny * 0.5f + 0.5f) * 255f));
                normals[i + 2] = (byte)(MathF.Round((nz * 0.5f + 0.5f) * 255f));
                normals[i + 3] = 255;
            }
        }

        return normals;
    }

    /// <summary>
    /// Samples the depth buffer at (x, y) with clamped boundary handling.
    /// Edge values are repeated for out-of-bounds coordinates.
    /// </summary>
    private static float S(float[] depth, int x, int y, int width, int height)
    {
        var cx = Math.Clamp(x, 0, width - 1);
        var cy = Math.Clamp(y, 0, height - 1);
        return depth[cy * width + cx];
    }

    /// <summary>
    /// Applies a separable Gaussian blur to the depth buffer.
    /// The kernel is computed from the given radius using a standard Gaussian distribution.
    /// </summary>
    private static float[] GaussianBlur(float[] source, int width, int height, int radius)
    {
        var kernel = BuildGaussianKernel(radius);
        var temp = new float[width * height];
        var result = new float[width * height];

        // Horizontal pass
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sum = 0f;
                for (var k = -radius; k <= radius; k++)
                {
                    var sx = Math.Clamp(x + k, 0, width - 1);
                    sum += source[y * width + sx] * kernel[k + radius];
                }
                temp[y * width + x] = sum;
            }
        }

        // Vertical pass
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sum = 0f;
                for (var k = -radius; k <= radius; k++)
                {
                    var sy = Math.Clamp(y + k, 0, height - 1);
                    sum += temp[sy * width + x] * kernel[k + radius];
                }
                result[y * width + x] = sum;
            }
        }

        return result;
    }

    /// <summary>
    /// Builds a normalized 1D Gaussian kernel for the given radius.
    /// Kernel size is (2 * radius + 1). Sigma is radius / 3.0 (covers ~99.7% of distribution).
    /// </summary>
    private static float[] BuildGaussianKernel(int radius)
    {
        var size = 2 * radius + 1;
        var kernel = new float[size];
        var sigma = radius / 3.0f;
        if (sigma < 0.0001f) sigma = 0.0001f;
        var twoSigmaSq = 2.0f * sigma * sigma;
        var sum = 0f;

        for (var i = 0; i < size; i++)
        {
            var x = i - radius;
            kernel[i] = MathF.Exp(-(x * x) / twoSigmaSq);
            sum += kernel[i];
        }

        // Normalize
        for (var i = 0; i < size; i++)
        {
            kernel[i] /= sum;
        }

        return kernel;
    }
}
