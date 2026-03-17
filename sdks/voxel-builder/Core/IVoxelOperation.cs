using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;

namespace BeyondImmersion.Bannou.VoxelBuilder.Core;

/// <summary>
/// Undoable, serializable operation contract. Every modification to a VoxelGrid flows
/// through this interface, enabling undo/redo, network serialization, and per-source tracking.
/// </summary>
public interface IVoxelOperation
{
    /// <summary>
    /// Apply the operation to the grid, capturing before-state for undo.
    /// </summary>
    /// <param name="grid">The grid to modify.</param>
    /// <param name="options">Builder options (frozen enforcement, etc.).</param>
    void Execute(VoxelGrid grid, VoxelBuilderOptions options);

    /// <summary>
    /// Restore the before-state captured during <see cref="Execute"/>.
    /// </summary>
    /// <param name="grid">The grid to restore.</param>
    void Undo(VoxelGrid grid);

    /// <summary>Bounding box of changed voxels.</summary>
    VoxelBounds AffectedRegion { get; }

    /// <summary>Who created this operation ("local", "generator", "player-2").</summary>
    string SourceId { get; set; }

    /// <summary>Human-readable operation name.</summary>
    string Description { get; }

    /// <summary>Type discriminator for serialization.</summary>
    VoxelOperationType OperationType { get; }
}
