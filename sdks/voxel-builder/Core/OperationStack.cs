using BeyondImmersion.Bannou.VoxelCore.Grid;

namespace BeyondImmersion.Bannou.VoxelBuilder.Core;

/// <summary>
/// Per-source undo/redo stack with configurable depth. Each source (local user,
/// generator, remote editor) gets its own stack so undo is scoped to the source.
/// </summary>
public sealed class OperationStack
{
    private readonly BoundedDeque<IVoxelOperation> _undoDeque;
    private readonly Stack<IVoxelOperation> _redoStack = new();

    /// <summary>Who owns this stack ("local", "generator", "player-2").</summary>
    public string SourceId { get; }

    /// <summary>Maximum undo history depth.</summary>
    public int MaxDepth { get; }

    /// <summary>Whether any operations have been applied since the last save/reset.</summary>
    public bool IsModified { get; private set; }

    /// <summary>Whether there are operations to undo.</summary>
    public bool CanUndo => !_undoDeque.IsEmpty;

    /// <summary>Whether there are operations to redo.</summary>
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>Number of operations in the undo history.</summary>
    public int UndoCount => _undoDeque.Count;

    /// <summary>Number of operations in the redo history.</summary>
    public int RedoCount => _redoStack.Count;

    /// <summary>
    /// Creates a new operation stack for the given source.
    /// </summary>
    /// <param name="sourceId">Source identifier.</param>
    /// <param name="maxDepth">Maximum undo depth.</param>
    public OperationStack(string sourceId, int maxDepth)
    {
        SourceId = sourceId;
        MaxDepth = maxDepth;
        _undoDeque = new BoundedDeque<IVoxelOperation>(maxDepth);
    }

    /// <summary>
    /// Execute an operation on the grid, push to undo stack, clear redo stack.
    /// </summary>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="grid">The grid to modify.</param>
    /// <param name="options">Builder options.</param>
    public void Execute(IVoxelOperation operation, VoxelGrid grid, VoxelBuilderOptions options)
    {
        operation.Execute(grid, options);
        _undoDeque.Push(operation);
        _redoStack.Clear();
        IsModified = true;
    }

    /// <summary>
    /// Push an already-executed operation to the undo stack without re-executing.
    /// Used by CompoundOperation (sub-ops already executed during Begin/End span).
    /// </summary>
    /// <param name="operation">The already-executed operation.</param>
    internal void PushWithoutExecute(IVoxelOperation operation)
    {
        _undoDeque.Push(operation);
        _redoStack.Clear();
        IsModified = true;
    }

    /// <summary>
    /// Undo the most recent operation and push it to the redo stack.
    /// </summary>
    /// <param name="grid">The grid to restore.</param>
    /// <returns>The undone operation, or null if nothing to undo.</returns>
    public IVoxelOperation? Undo(VoxelGrid grid)
    {
        if (_undoDeque.IsEmpty) return null;

        var op = _undoDeque.Pop();
        op.Undo(grid);
        _redoStack.Push(op);
        return op;
    }

    /// <summary>
    /// Redo the most recently undone operation.
    /// </summary>
    /// <param name="grid">The grid to modify.</param>
    /// <param name="options">Builder options.</param>
    /// <returns>The redone operation, or null if nothing to redo.</returns>
    public IVoxelOperation? Redo(VoxelGrid grid, VoxelBuilderOptions options)
    {
        if (_redoStack.Count == 0) return null;

        var op = _redoStack.Pop();
        op.Execute(grid, options);
        _undoDeque.Push(op);
        return op;
    }

    /// <summary>Clear both undo and redo stacks.</summary>
    public void Clear()
    {
        _undoDeque.Clear();
        _redoStack.Clear();
    }

    /// <summary>Reset the <see cref="IsModified"/> flag.</summary>
    public void MarkSaved() => IsModified = false;
}
