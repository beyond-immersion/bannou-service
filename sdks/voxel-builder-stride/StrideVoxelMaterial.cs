using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;

namespace BeyondImmersion.Bannou.VoxelBuilder.Stride;

/// <summary>
/// Factory for creating vertex-color materials at runtime.
/// Uses Stride's <see cref="ComputeVertexStreamColor"/> to read the COLOR0 vertex attribute
/// via <see cref="ColorVertexStreamDefinition"/>. No custom shaders or Game Studio assets needed.
/// </summary>
public static class StrideVoxelMaterial
{
    /// <summary>
    /// Creates a vertex-color material that reads per-vertex colors from the COLOR0 semantic.
    /// The material uses Lambert diffuse shading with vertex colors as the diffuse albedo.
    /// </summary>
    /// <param name="graphicsDevice">The graphics device to compile the material on.</param>
    /// <returns>A compiled material ready for use on models with <c>VertexPositionNormalColor</c> vertices.</returns>
    public static Material Create(GraphicsDevice graphicsDevice)
    {
        // 1. Create vertex stream color compute that reads COLOR0
        var vertexColorCompute = new ComputeVertexStreamColor
        {
            Stream = new ColorVertexStreamDefinition(0)
        };

        // 2. Create diffuse feature wrapping the vertex color compute
        var diffuseFeature = new MaterialDiffuseMapFeature(vertexColorCompute);

        // 3. Assemble material descriptor
        var descriptor = new MaterialDescriptor();
        descriptor.Attributes.Diffuse = diffuseFeature;
        descriptor.Attributes.DiffuseModel = new MaterialDiffuseLambertModelFeature();

        // 4. Compile material
        return Material.New(graphicsDevice, descriptor);
    }
}
