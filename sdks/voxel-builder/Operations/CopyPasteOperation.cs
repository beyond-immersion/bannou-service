using BeyondImmersion.Bannou.VoxelBuilder.Core;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;

namespace BeyondImmersion.Bannou.VoxelBuilder.Operations;

/// <summary>
/// Pastes a <see cref="VoxelClipboard"/> at an offset position into the grid. Performs
/// palette merging via <see cref="Palette.GetOrAddIndex"/> when the clipboard's palette
/// entries differ from the target grid's palette.
/// </summary>
public sealed class CopyPasteOperation : IVoxelOperation
{
    private readonly Dictionary<VoxelCoord, Voxel> _previousVoxels = new();

    /// <summary>The clipboard data to paste.</summary>
    public VoxelClipboard Clipboard { get; }

    /// <summary>The world-space offset to paste the clipboard at.</summary>
    public VoxelCoord PasteOffset { get; }

    /// <inheritdoc />
    public string SourceId { get; set; }

    /// <inheritdoc />
    public string Description => $"Paste clipboard ({Clipboard.Voxels.Count} voxels) at {PasteOffset}";

    /// <inheritdoc />
    public VoxelOperationType OperationType => VoxelOperationType.CopyPaste;

    /// <inheritdoc />
    public VoxelBounds AffectedRegion => new(
        Clipboard.Bounds.Min + PasteOffset,
        Clipboard.Bounds.Max + PasteOffset);

    /// <summary>
    /// Creates a new copy-paste operation.
    /// </summary>
    /// <param name="clipboard">The clipboard data to paste.</param>
    /// <param name="pasteOffset">The world-space offset to paste at.</param>
    /// <param name="sourceId">Who created this operation.</param>
    public CopyPasteOperation(VoxelClipboard clipboard, VoxelCoord pasteOffset, string sourceId = "local")
    {
        Clipboard = clipboard;
        PasteOffset = pasteOffset;
        SourceId = sourceId;
    }

    /// <inheritdoc />
    public void Execute(VoxelGrid grid, VoxelBuilderOptions options)
    {
        // Build palette index mapping from clipboard to target grid
        var paletteMap = new Dictionary<byte, byte>();

        foreach (var (clipboardIndex, entry) in Clipboard.PaletteSnapshot)
        {
            if (clipboardIndex == 0)
                continue;

            var targetIndex = grid.Palette.GetOrAddIndex(entry.Color, entry.Material, entry.Roughness);
            paletteMap[clipboardIndex] = targetIndex;
        }

        foreach (var (relativeCoord, clipboardVoxel) in Clipboard.Voxels)
        {
            if (clipboardVoxel.IsEmpty)
                continue;

            var worldCoord = relativeCoord + PasteOffset;
            if (!grid.Contains(worldCoord))
                continue;

            var existing = grid.GetVoxel(worldCoord);
            if (options.EnforceFrozen && existing.Flags.HasFlag(VoxelFlags.Frozen))
                continue;

            _previousVoxels[worldCoord] = existing;

            // Map the clipboard palette index to the target palette index
            var mappedIndex = paletteMap.TryGetValue(clipboardVoxel.PaletteIndex, out var mapped)
                ? mapped
                : clipboardVoxel.PaletteIndex;

            grid.SetVoxel(worldCoord, new Voxel(mappedIndex, clipboardVoxel.Flags));
        }
    }

    /// <inheritdoc />
    public void Undo(VoxelGrid grid)
    {
        foreach (var (coord, voxel) in _previousVoxels)
        {
            grid.SetVoxel(coord, voxel);
        }
    }
}
