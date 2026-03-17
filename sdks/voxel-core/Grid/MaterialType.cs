namespace BeyondImmersion.Bannou.VoxelCore.Grid;

/// <summary>
/// Material type for a palette entry. Maps to MagicaVoxel MATL chunk types.
/// </summary>
public enum MaterialType : byte
{
    /// <summary>Standard diffuse material.</summary>
    Diffuse = 0,

    /// <summary>Metallic material with specular reflection.</summary>
    Metal = 1,

    /// <summary>Transparent glass material.</summary>
    Glass = 2,

    /// <summary>Emissive material that produces light.</summary>
    Emit = 3,

    /// <summary>Volumetric cloud material.</summary>
    Cloud = 4
}
