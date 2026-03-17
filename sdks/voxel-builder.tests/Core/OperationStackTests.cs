using BeyondImmersion.Bannou.VoxelBuilder.Core;
using BeyondImmersion.Bannou.VoxelBuilder.Operations;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelBuilder.Tests.Core;

/// <summary>
/// Unit tests for the <see cref="OperationStack"/> per-source undo/redo management.
/// </summary>
public class OperationStackTests
{
    private static VoxelGrid CreateGrid() =>
        new(new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(31, 31, 31)));

    private static VoxelBuilderOptions DefaultOptions => VoxelBuilderOptions.Default;

    [Fact]
    public void NewStack_CannotUndoOrRedo()
    {
        var stack = new OperationStack("local", 100);
        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
        Assert.Equal(0, stack.UndoCount);
        Assert.Equal(0, stack.RedoCount);
    }

    [Fact]
    public void NewStack_IsNotModified()
    {
        var stack = new OperationStack("local", 100);
        Assert.False(stack.IsModified);
    }

    [Fact]
    public void Execute_PushesToUndoStack()
    {
        var stack = new OperationStack("local", 100);
        var grid = CreateGrid();
        stack.Execute(new PlaceOperation(new VoxelCoord(1, 1, 1), 1), grid, DefaultOptions);

        Assert.True(stack.CanUndo);
        Assert.Equal(1, stack.UndoCount);
        Assert.True(stack.IsModified);
    }

    [Fact]
    public void Execute_ClearsRedoStack()
    {
        var stack = new OperationStack("local", 100);
        var grid = CreateGrid();

        stack.Execute(new PlaceOperation(new VoxelCoord(1, 1, 1), 1), grid, DefaultOptions);
        stack.Undo(grid);
        Assert.True(stack.CanRedo);

        // New operation clears redo
        stack.Execute(new PlaceOperation(new VoxelCoord(2, 2, 2), 2), grid, DefaultOptions);
        Assert.False(stack.CanRedo);
        Assert.Equal(0, stack.RedoCount);
    }

    [Fact]
    public void Execute_AppliesOperationToGrid()
    {
        var stack = new OperationStack("local", 100);
        var grid = CreateGrid();

        stack.Execute(new PlaceOperation(new VoxelCoord(5, 5, 5), 7), grid, DefaultOptions);
        Assert.Equal(7, grid.GetVoxel(new VoxelCoord(5, 5, 5)).PaletteIndex);
    }

    [Fact]
    public void Undo_RestoresGrid()
    {
        var stack = new OperationStack("local", 100);
        var grid = CreateGrid();
        var coord = new VoxelCoord(5, 5, 5);

        stack.Execute(new PlaceOperation(coord, 7), grid, DefaultOptions);
        Assert.Equal(7, grid.GetVoxel(coord).PaletteIndex);

        var undone = stack.Undo(grid);
        Assert.NotNull(undone);
        Assert.True(grid.GetVoxel(coord).IsEmpty);
    }

    [Fact]
    public void Undo_PushesToRedoStack()
    {
        var stack = new OperationStack("local", 100);
        var grid = CreateGrid();

        stack.Execute(new PlaceOperation(new VoxelCoord(1, 1, 1), 1), grid, DefaultOptions);
        stack.Undo(grid);

        Assert.True(stack.CanRedo);
        Assert.Equal(1, stack.RedoCount);
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public void Undo_WhenEmpty_ReturnsNull()
    {
        var stack = new OperationStack("local", 100);
        var grid = CreateGrid();

        Assert.Null(stack.Undo(grid));
    }

    [Fact]
    public void Redo_ReappliesOperation()
    {
        var stack = new OperationStack("local", 100);
        var grid = CreateGrid();
        var coord = new VoxelCoord(5, 5, 5);

        stack.Execute(new PlaceOperation(coord, 7), grid, DefaultOptions);
        stack.Undo(grid);
        Assert.True(grid.GetVoxel(coord).IsEmpty);

        var redone = stack.Redo(grid, DefaultOptions);
        Assert.NotNull(redone);
        Assert.Equal(7, grid.GetVoxel(coord).PaletteIndex);
    }

    [Fact]
    public void Redo_WhenEmpty_ReturnsNull()
    {
        var stack = new OperationStack("local", 100);
        var grid = CreateGrid();

        Assert.Null(stack.Redo(grid, DefaultOptions));
    }

    [Fact]
    public void MultipleUndoRedo_MaintainsCorrectState()
    {
        var stack = new OperationStack("local", 100);
        var grid = CreateGrid();
        var c1 = new VoxelCoord(1, 0, 0);
        var c2 = new VoxelCoord(2, 0, 0);
        var c3 = new VoxelCoord(3, 0, 0);

        stack.Execute(new PlaceOperation(c1, 1), grid, DefaultOptions);
        stack.Execute(new PlaceOperation(c2, 2), grid, DefaultOptions);
        stack.Execute(new PlaceOperation(c3, 3), grid, DefaultOptions);

        // Undo all three in reverse
        stack.Undo(grid);
        Assert.True(grid.GetVoxel(c3).IsEmpty);
        stack.Undo(grid);
        Assert.True(grid.GetVoxel(c2).IsEmpty);
        stack.Undo(grid);
        Assert.True(grid.GetVoxel(c1).IsEmpty);

        // Redo all three
        stack.Redo(grid, DefaultOptions);
        Assert.Equal(1, grid.GetVoxel(c1).PaletteIndex);
        stack.Redo(grid, DefaultOptions);
        Assert.Equal(2, grid.GetVoxel(c2).PaletteIndex);
        stack.Redo(grid, DefaultOptions);
        Assert.Equal(3, grid.GetVoxel(c3).PaletteIndex);
    }

    [Fact]
    public void MaxDepth_EvictsOldestOperations()
    {
        var stack = new OperationStack("local", 3);
        var grid = CreateGrid();

        stack.Execute(new PlaceOperation(new VoxelCoord(1, 0, 0), 1), grid, DefaultOptions);
        stack.Execute(new PlaceOperation(new VoxelCoord(2, 0, 0), 2), grid, DefaultOptions);
        stack.Execute(new PlaceOperation(new VoxelCoord(3, 0, 0), 3), grid, DefaultOptions);
        stack.Execute(new PlaceOperation(new VoxelCoord(4, 0, 0), 4), grid, DefaultOptions);

        Assert.Equal(3, stack.UndoCount);
    }

    [Fact]
    public void PushWithoutExecute_DoesNotReExecute()
    {
        var stack = new OperationStack("local", 100);
        var grid = CreateGrid();
        var coord = new VoxelCoord(5, 5, 5);

        // Pre-execute manually
        var op = new PlaceOperation(coord, 7);
        op.Execute(grid, DefaultOptions);
        Assert.Equal(7, grid.GetVoxel(coord).PaletteIndex);

        // Erase it manually
        grid.SetVoxel(coord, Voxel.Empty);

        // PushWithoutExecute should not re-place the voxel
        stack.PushWithoutExecute(op);
        Assert.True(grid.GetVoxel(coord).IsEmpty);
        Assert.True(stack.CanUndo);
    }

    [Fact]
    public void Clear_ResetsBothStacks()
    {
        var stack = new OperationStack("local", 100);
        var grid = CreateGrid();

        stack.Execute(new PlaceOperation(new VoxelCoord(1, 1, 1), 1), grid, DefaultOptions);
        stack.Execute(new PlaceOperation(new VoxelCoord(2, 2, 2), 2), grid, DefaultOptions);
        stack.Undo(grid);

        stack.Clear();
        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
        Assert.Equal(0, stack.UndoCount);
        Assert.Equal(0, stack.RedoCount);
    }

    [Fact]
    public void MarkSaved_ResetsIsModified()
    {
        var stack = new OperationStack("local", 100);
        var grid = CreateGrid();

        stack.Execute(new PlaceOperation(new VoxelCoord(1, 1, 1), 1), grid, DefaultOptions);
        Assert.True(stack.IsModified);

        stack.MarkSaved();
        Assert.False(stack.IsModified);
    }

    [Fact]
    public void SourceId_IsPreserved()
    {
        var stack = new OperationStack("generator", 50);
        Assert.Equal("generator", stack.SourceId);
        Assert.Equal(50, stack.MaxDepth);
    }
}
