using BeyondImmersion.Bannou.VoxelCore.Math;

namespace BeyondImmersion.Bannou.VoxelBuilder.Core;

/// <summary>
/// Event arguments for <see cref="VoxelBuilder.OnOperationApplied"/>. Contains everything
/// a subscriber needs for network broadcast or persistence without re-serializing.
/// </summary>
public sealed class OperationAppliedEventArgs : EventArgs
{
    /// <summary>The operation that was applied.</summary>
    public IVoxelOperation Operation { get; }

    /// <summary>Pre-serialized operation bytes for network broadcast.</summary>
    public byte[] SerializedBytes { get; }

    /// <summary>Who created the operation.</summary>
    public string SourceId { get; }

    /// <summary>Chunks modified by this operation (for bridge + persistence).</summary>
    public IReadOnlySet<ChunkCoord> AffectedChunks { get; }

    /// <summary>True if this was an undo, not a forward apply.</summary>
    public bool IsUndo { get; }

    /// <summary>
    /// Creates new event args.
    /// </summary>
    public OperationAppliedEventArgs(
        IVoxelOperation operation, byte[] serializedBytes, string sourceId,
        IReadOnlySet<ChunkCoord> affectedChunks, bool isUndo)
    {
        Operation = operation;
        SerializedBytes = serializedBytes;
        SourceId = sourceId;
        AffectedChunks = affectedChunks;
        IsUndo = isUndo;
    }
}
