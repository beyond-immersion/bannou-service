using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;

namespace BeyondImmersion.Bannou.VoxelCore.Voxelization;

/// <summary>
/// Converts a 2D heightmap to a <see cref="VoxelGrid"/> by filling columns below sampled height values.
/// Used for terrain overlay creation in the voxel editing workflow.
/// </summary>
public static class HeightmapVoxelizer
{
    /// <summary>
    /// Voxelizes a heightmap into a voxel grid. For each (x, z) column, fills voxels from
    /// y=0 to the sampled height. Marks border voxels as <see cref="VoxelFlags.Frozen"/>.
    /// </summary>
    /// <param name="heights">
    /// 2D array indexed [x, z] with world-space height values.
    /// </param>
    /// <param name="materials">
    /// Parallel 2D array [x, z] with palette indices per column.
    /// 0 values are replaced with <see cref="VoxelizationOptions.DefaultPaletteIndex"/>.
    /// </param>
    /// <param name="palette">The palette to use for the output grid.</param>
    /// <param name="options">Voxelization options (scale, frozen border width, default material).</param>
    /// <returns>A new VoxelGrid populated from the heightmap data.</returns>
    public static VoxelGrid Voxelize(
        float[,] heights, byte[,] materials, Palette palette, VoxelizationOptions options)
    {
        var gridWidth = heights.GetLength(0);
        var gridDepth = heights.GetLength(1);

        // Find max height to determine grid bounds
        var maxHeightWorld = 0f;
        for (var x = 0; x < gridWidth; x++)
        for (var z = 0; z < gridDepth; z++)
        {
            if (heights[x, z] > maxHeightWorld)
                maxHeightWorld = heights[x, z];
        }

        var maxHeight = (int)MathF.Ceiling(maxHeightWorld / options.VoxelScale);
        var bounds = new VoxelBounds(
            new VoxelCoord(0, 0, 0),
            new VoxelCoord(gridWidth - 1, maxHeight, gridDepth - 1));

        var grid = new VoxelGrid(bounds, palette);

        for (var x = 0; x < gridWidth; x++)
        for (var z = 0; z < gridDepth; z++)
        {
            var worldHeight = heights[x, z];
            var voxelHeight = (int)MathF.Floor(worldHeight / options.VoxelScale);
            var paletteIndex = materials[x, z];
            if (paletteIndex == 0)
                paletteIndex = options.DefaultPaletteIndex;

            for (var y = 0; y <= voxelHeight; y++)
            {
                var flags = VoxelFlags.None;

                // Mark frozen border: all voxels in border columns (which
                // inherently includes their top surfaces) are frozen. Interior
                // columns remain fully editable, including their top surface.
                var isBorderColumn =
                    x < options.FrozenBorderWidth || x >= gridWidth - options.FrozenBorderWidth ||
                    z < options.FrozenBorderWidth || z >= gridDepth - options.FrozenBorderWidth;
                if (isBorderColumn)
                {
                    flags |= VoxelFlags.Frozen;
                }

                grid.SetVoxel(new VoxelCoord(x, y, z), new Voxel(paletteIndex, flags));
            }
        }

        return grid;
    }
}
