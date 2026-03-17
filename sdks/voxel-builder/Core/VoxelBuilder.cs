using BeyondImmersion.Bannou.VoxelBuilder.Operations;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using BeyondImmersion.Bannou.VoxelCore.Serialization;

namespace BeyondImmersion.Bannou.VoxelBuilder.Core;

/// <summary>
/// Main orchestrator for interactive voxel authoring. Holds the grid, per-source
/// operation stacks, bridge reference, and provides the entry point for all editing.
/// Operations are serializable and source-tagged for assisted and shared building.
/// </summary>
public sealed class VoxelBuilder
{
    private readonly OperationStack _localStack;
    private readonly Dictionary<string, OperationStack> _externalStacks = new();
    private bool _compoundActive;
    private string _compoundDescription = string.Empty;
    private List<IVoxelOperation>? _compoundOperations;

    /// <summary>The grid being edited.</summary>
    public VoxelGrid Grid { get; private set; }

    /// <summary>Engine rendering bridge (null for headless/server mode).</summary>
    public IVoxelBuilderBridge? Bridge { get; private set; }

    /// <summary>Optional persistence integration.</summary>
    public IVoxelStorageClient? Storage { get; set; }

    /// <summary>Configuration.</summary>
    public VoxelBuilderOptions Options { get; }

    /// <summary>Currently selected palette entry for painting.</summary>
    public byte ActivePaletteIndex { get; set; } = 1;

    /// <summary>Current brush shape and size.</summary>
    public BrushShape ActiveBrush { get; set; } = BrushShape.Default;

    /// <summary>Current region selection (for copy/paste).</summary>
    public VoxelBounds? Selection { get; private set; }

    /// <summary>Copied region data.</summary>
    public VoxelClipboard? Clipboard { get; private set; }

    /// <summary>
    /// Fires for ALL operations (local + external). Subscribers use this for
    /// network broadcast and persistence without re-serializing.
    /// </summary>
    public event EventHandler<OperationAppliedEventArgs>? OnOperationApplied;

    private VoxelBuilder(VoxelGrid grid, VoxelBuilderOptions options)
    {
        Grid = grid;
        Options = options;
        _localStack = new OperationStack("local", options.MaxUndoDepth);
    }

    /// <summary>Create a builder with a new empty grid.</summary>
    /// <param name="bounds">Grid dimensions.</param>
    /// <param name="options">Builder options (null for defaults).</param>
    /// <returns>A new VoxelBuilder.</returns>
    public static VoxelBuilder CreateEmpty(VoxelBounds bounds, VoxelBuilderOptions? options = null)
    {
        return new VoxelBuilder(new VoxelGrid(bounds), options ?? VoxelBuilderOptions.Default);
    }

    /// <summary>Create a builder with an existing grid.</summary>
    /// <param name="grid">The grid to edit.</param>
    /// <param name="options">Builder options (null for defaults).</param>
    /// <returns>A new VoxelBuilder.</returns>
    public static VoxelBuilder LoadGrid(VoxelGrid grid, VoxelBuilderOptions? options = null)
    {
        return new VoxelBuilder(grid, options ?? VoxelBuilderOptions.Default);
    }

    /// <summary>
    /// Execute a local operation: apply to grid, push to local undo stack, fire event.
    /// </summary>
    /// <param name="operation">The operation to execute.</param>
    public void ExecuteOperation(IVoxelOperation operation)
    {
        operation.SourceId = "local";

        if (_compoundActive && _compoundOperations != null)
        {
            _compoundOperations.Add(operation);
            operation.Execute(Grid, Options);
            return;
        }

        _localStack.Execute(operation, Grid, Options);
        NotifyOperation(operation, "local", isUndo: false);
    }

    /// <summary>
    /// Apply an external operation (from generator or remote editor).
    /// </summary>
    /// <param name="operation">The operation to apply.</param>
    /// <param name="sourceId">Who created this operation.</param>
    public void ApplyExternalOperation(IVoxelOperation operation, string sourceId)
    {
        operation.SourceId = sourceId;

        if (!_externalStacks.TryGetValue(sourceId, out var stack))
        {
            stack = new OperationStack(sourceId, Options.MaxUndoDepth);
            _externalStacks[sourceId] = stack;
        }

        stack.Execute(operation, Grid, Options);
        NotifyOperation(operation, sourceId, isUndo: false);
    }

    /// <summary>Undo the most recent local operation.</summary>
    /// <returns>True if an operation was undone.</returns>
    public bool Undo()
    {
        if (!_localStack.CanUndo) return false;
        var op = _localStack.Undo(Grid);
        if (op != null) NotifyOperation(op, "local", isUndo: true);
        return op != null;
    }

    /// <summary>Redo the most recently undone local operation.</summary>
    /// <returns>True if an operation was redone.</returns>
    public bool Redo()
    {
        if (!_localStack.CanRedo) return false;
        var op = _localStack.Redo(Grid, Options);
        if (op != null) NotifyOperation(op, "local", isUndo: false);
        return op != null;
    }

    /// <summary>Undo the most recent operation from a specific external source.</summary>
    /// <param name="sourceId">The source to undo.</param>
    /// <returns>True if an operation was undone.</returns>
    public bool UndoExternal(string sourceId)
    {
        if (!_externalStacks.TryGetValue(sourceId, out var stack) || !stack.CanUndo)
            return false;
        var op = stack.Undo(Grid);
        if (op != null) NotifyOperation(op, sourceId, isUndo: true);
        return op != null;
    }

    /// <summary>Redo the most recently undone operation from a specific external source.</summary>
    /// <param name="sourceId">The source to redo.</param>
    /// <returns>True if an operation was redone.</returns>
    public bool RedoExternal(string sourceId)
    {
        if (!_externalStacks.TryGetValue(sourceId, out var stack) || !stack.CanRedo)
            return false;
        var op = stack.Redo(Grid, Options);
        if (op != null) NotifyOperation(op, sourceId, isUndo: false);
        return op != null;
    }

    #region Convenience Methods

    /// <summary>Place a single voxel.</summary>
    public void Place(VoxelCoord coord, byte paletteIndex) =>
        ExecuteOperation(new PlaceOperation(coord, paletteIndex));

    /// <summary>Erase a single voxel.</summary>
    public void Erase(VoxelCoord coord) =>
        ExecuteOperation(new EraseOperation(coord));

    /// <summary>Paint with the active brush at the given center.</summary>
    public void BrushPaint(VoxelCoord center) =>
        ExecuteOperation(new BrushOperation(center, ActiveBrush, ActivePaletteIndex, erase: false));

    /// <summary>Erase with the active brush at the given center.</summary>
    public void BrushErase(VoxelCoord center) =>
        ExecuteOperation(new BrushOperation(center, ActiveBrush, 0, erase: true));

    /// <summary>Flood fill from origin with the given palette index.</summary>
    public void Fill(VoxelCoord origin, byte paletteIndex, VoxelBounds? limit = null) =>
        ExecuteOperation(new FillOperation(origin, paletteIndex, limit ?? Grid.Bounds));

    /// <summary>Fill a box region.</summary>
    public void BoxFill(VoxelBounds region, byte paletteIndex) =>
        ExecuteOperation(new BoxOperation(region, paletteIndex, erase: false));

    /// <summary>Erase a box region.</summary>
    public void BoxErase(VoxelBounds region) =>
        ExecuteOperation(new BoxOperation(region, 0, erase: true));

    /// <summary>Mirror the entire grid across an axis.</summary>
    public void Mirror(Axis axis) =>
        ExecuteOperation(new MirrorOperation(axis));

    /// <summary>Rotate the entire grid 90 degrees around an axis.</summary>
    public void Rotate90(Axis axis) =>
        ExecuteOperation(new RotateOperation(axis));

    /// <summary>Replace all voxels of one palette index with another.</summary>
    public void Replace(byte fromIndex, byte toIndex) =>
        ExecuteOperation(new ReplaceOperation(fromIndex, toIndex));

    /// <summary>Set the current selection region.</summary>
    public void Select(VoxelBounds region) => Selection = region;

    /// <summary>Clear the current selection.</summary>
    public void ClearSelection() => Selection = null;

    /// <summary>Copy the current selection to the clipboard.</summary>
    /// <exception cref="InvalidOperationException">Thrown if no selection is active.</exception>
    public void Copy()
    {
        if (Selection == null)
            throw new InvalidOperationException("No selection");

        var clipboard = new VoxelClipboard { Bounds = Selection.Value };

        for (var y = Selection.Value.Min.Y; y <= Selection.Value.Max.Y; y++)
        for (var z = Selection.Value.Min.Z; z <= Selection.Value.Max.Z; z++)
        for (var x = Selection.Value.Min.X; x <= Selection.Value.Max.X; x++)
        {
            var coord = new VoxelCoord(x, y, z);
            var voxel = Grid.GetVoxel(coord);
            if (!voxel.IsEmpty)
            {
                var relative = coord - Selection.Value.Min;
                clipboard.Voxels[relative] = voxel;

                if (!clipboard.PaletteSnapshot.ContainsKey(voxel.PaletteIndex))
                    clipboard.PaletteSnapshot[voxel.PaletteIndex] = Grid.Palette.Get(voxel.PaletteIndex);
            }
        }

        Clipboard = clipboard;
    }

    /// <summary>Paste the clipboard at the given offset.</summary>
    /// <param name="offset">World-space offset for paste position.</param>
    /// <exception cref="InvalidOperationException">Thrown if clipboard is empty.</exception>
    public void Paste(VoxelCoord offset)
    {
        if (Clipboard == null)
            throw new InvalidOperationException("Nothing to paste");

        ExecuteOperation(new CopyPasteOperation(Clipboard, offset));
    }

    /// <summary>Start grouping operations into a compound (atomic undo unit).</summary>
    /// <param name="description">Human-readable description.</param>
    /// <exception cref="InvalidOperationException">Thrown if already in a compound.</exception>
    public void BeginCompound(string description)
    {
        if (_compoundActive)
            throw new InvalidOperationException("Compound operation already active");

        _compoundActive = true;
        _compoundDescription = description;
        _compoundOperations = new List<IVoxelOperation>();
    }

    /// <summary>Finalize the compound operation and push to undo stack.</summary>
    /// <exception cref="InvalidOperationException">Thrown if no compound is active.</exception>
    public void EndCompound()
    {
        if (!_compoundActive)
            throw new InvalidOperationException("No compound operation active");

        _compoundActive = false;
        if (_compoundOperations == null || _compoundOperations.Count == 0)
            return;

        var compound = new CompoundOperation(_compoundOperations, _compoundDescription);
        compound.SourceId = "local";
        _localStack.PushWithoutExecute(compound);
        NotifyOperation(compound, "local", isUndo: false);
    }

    /// <summary>
    /// Compute a GridPatchOperation from the difference between two grids.
    /// Used for generator output in the assisted building pattern.
    /// </summary>
    /// <param name="before">Grid state before generation.</param>
    /// <param name="after">Grid state after generation.</param>
    /// <param name="sourceId">Source identifier (typically "generator").</param>
    /// <returns>A GridPatchOperation wrapping the delta.</returns>
    public static GridPatchOperation DiffToOperation(VoxelGrid before, VoxelGrid after, string sourceId)
    {
        var delta = VoxelDelta.Compute(before, after);

        var beforeChunks = new Dictionary<ChunkCoord, byte[]?>();
        foreach (var (coord, _) in after.EnumerateChunks())
        {
            var chunk = before.GetChunk(coord);
            beforeChunks[coord] = chunk != null ? VoxelSerializer.SerializeChunk(chunk) : null;
        }
        foreach (var (coord, _) in before.EnumerateChunks())
        {
            if (!beforeChunks.ContainsKey(coord))
            {
                var chunk = before.GetChunk(coord);
                beforeChunks[coord] = chunk != null ? VoxelSerializer.SerializeChunk(chunk) : null;
            }
        }

        return new GridPatchOperation(delta, sourceId, beforeChunks);
    }

    #endregion

    /// <summary>Attach an engine bridge for rendering.</summary>
    public void SetBridge(IVoxelBuilderBridge bridge)
    {
        Bridge = bridge;
        bridge.OnGridLoaded(Grid);
    }

    /// <summary>Reset the IsModified flag on the local stack.</summary>
    public void MarkSaved() => _localStack.MarkSaved();

    /// <summary>Whether the local stack has unsaved modifications.</summary>
    public bool IsModified => _localStack.IsModified;

    private void NotifyOperation(IVoxelOperation operation, string sourceId, bool isUndo)
    {
        var serialized = OperationSerializer.Serialize(operation);
        var affectedChunks = ComputeAffectedChunks(operation.AffectedRegion);

        OnOperationApplied?.Invoke(this, new OperationAppliedEventArgs(
            operation, serialized, sourceId, affectedChunks, isUndo));

        if (Options.AutoMeshOnEdit && Bridge != null)
            Bridge.OnChunksModified(affectedChunks);
    }

    private static HashSet<ChunkCoord> ComputeAffectedChunks(VoxelBounds region)
    {
        var minChunk = region.Min.ToChunkCoord();
        var maxChunk = region.Max.ToChunkCoord();
        var chunks = new HashSet<ChunkCoord>();

        for (var cy = minChunk.Y; cy <= maxChunk.Y; cy++)
        for (var cz = minChunk.Z; cz <= maxChunk.Z; cz++)
        for (var cx = minChunk.X; cx <= maxChunk.X; cx++)
            chunks.Add(new ChunkCoord(cx, cy, cz));

        return chunks;
    }
}
