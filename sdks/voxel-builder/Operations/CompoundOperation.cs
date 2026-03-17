using BeyondImmersion.Bannou.VoxelBuilder.Core;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;

namespace BeyondImmersion.Bannou.VoxelBuilder.Operations;

/// <summary>
/// Groups multiple sub-operations into a single atomic undo unit. Execute runs all
/// sub-operations in order; undo reverses them in the opposite order.
/// </summary>
public sealed class CompoundOperation : IVoxelOperation
{
    private readonly string _description;

    /// <summary>The sub-operations in execution order.</summary>
    public IReadOnlyList<IVoxelOperation> Operations { get; }

    /// <inheritdoc />
    public string SourceId { get; set; }

    /// <inheritdoc />
    public string Description => _description;

    /// <inheritdoc />
    public VoxelOperationType OperationType => VoxelOperationType.Compound;

    /// <inheritdoc />
    public VoxelBounds AffectedRegion
    {
        get
        {
            if (Operations.Count == 0)
                return new VoxelBounds(VoxelCoord.Zero, VoxelCoord.Zero);

            var first = Operations[0].AffectedRegion;
            var min = first.Min;
            var max = first.Max;

            for (var i = 1; i < Operations.Count; i++)
            {
                var region = Operations[i].AffectedRegion;
                min = new VoxelCoord(
                    Math.Min(min.X, region.Min.X),
                    Math.Min(min.Y, region.Min.Y),
                    Math.Min(min.Z, region.Min.Z));
                max = new VoxelCoord(
                    Math.Max(max.X, region.Max.X),
                    Math.Max(max.Y, region.Max.Y),
                    Math.Max(max.Z, region.Max.Z));
            }

            return new VoxelBounds(min, max);
        }
    }

    /// <summary>
    /// Creates a new compound operation.
    /// </summary>
    /// <param name="operations">The sub-operations to group.</param>
    /// <param name="description">Human-readable description of the compound operation.</param>
    /// <param name="sourceId">Who created this operation.</param>
    public CompoundOperation(IReadOnlyList<IVoxelOperation> operations, string description, string sourceId = "local")
    {
        Operations = operations;
        _description = description;
        SourceId = sourceId;
    }

    /// <inheritdoc />
    public void Execute(VoxelGrid grid, VoxelBuilderOptions options)
    {
        foreach (var operation in Operations)
        {
            operation.Execute(grid, options);
        }
    }

    /// <inheritdoc />
    public void Undo(VoxelGrid grid)
    {
        for (var i = Operations.Count - 1; i >= 0; i--)
        {
            Operations[i].Undo(grid);
        }
    }
}
