using BeyondImmersion.Bannou.VoxelBuilder.Core;
using BeyondImmersion.Bannou.VoxelBuilder.Operations;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using Xunit;
using VoxelBuilderClass = BeyondImmersion.Bannou.VoxelBuilder.Core.VoxelBuilder;

namespace BeyondImmersion.Bannou.VoxelBuilder.Tests.Core;

/// <summary>
/// Unit tests for the <see cref="VoxelBuilder"/> main orchestrator.
/// </summary>
public class VoxelBuilderTests
{
    private static VoxelBounds SmallBounds => new(new VoxelCoord(0, 0, 0), new VoxelCoord(31, 31, 31));

    private static VoxelBuilderClass CreateBuilder(VoxelBuilderOptions? options = null) =>
        VoxelBuilderClass.CreateEmpty(SmallBounds, options);

    #region Factory Methods

    [Fact]
    public void CreateEmpty_CreatesBuilderWithEmptyGrid()
    {
        var builder = VoxelBuilderClass.CreateEmpty(SmallBounds);
        Assert.Equal(0, builder.Grid.VoxelCount);
        Assert.Equal(SmallBounds, builder.Grid.Bounds);
    }

    [Fact]
    public void CreateEmpty_UsesDefaultOptions()
    {
        var builder = VoxelBuilderClass.CreateEmpty(SmallBounds);
        Assert.Equal(VoxelBuilderOptions.Default, builder.Options);
    }

    [Fact]
    public void CreateEmpty_UsesCustomOptions()
    {
        var options = new VoxelBuilderOptions(MaxUndoDepth: 50, AutoMeshOnEdit: false, EnforceFrozen: false);
        var builder = VoxelBuilderClass.CreateEmpty(SmallBounds, options);
        Assert.Equal(50, builder.Options.MaxUndoDepth);
        Assert.False(builder.Options.AutoMeshOnEdit);
        Assert.False(builder.Options.EnforceFrozen);
    }

    [Fact]
    public void LoadGrid_UsesExistingGrid()
    {
        var grid = new VoxelGrid(SmallBounds);
        grid.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(3, VoxelFlags.None));

        var builder = VoxelBuilderClass.LoadGrid(grid);
        Assert.Same(grid, builder.Grid);
        Assert.Equal(1, builder.Grid.VoxelCount);
    }

    #endregion

    #region Default Properties

    [Fact]
    public void DefaultProperties_AreCorrect()
    {
        var builder = CreateBuilder();
        Assert.Equal(1, builder.ActivePaletteIndex);
        Assert.Equal(BrushShape.Default, builder.ActiveBrush);
        Assert.Null(builder.Selection);
        Assert.Null(builder.Clipboard);
        Assert.Null(builder.Bridge);
        Assert.Null(builder.Storage);
        Assert.False(builder.IsModified);
    }

    #endregion

    #region ExecuteOperation

    [Fact]
    public void ExecuteOperation_AppliesAndTracksInUndoStack()
    {
        var builder = CreateBuilder();
        var coord = new VoxelCoord(5, 5, 5);

        builder.ExecuteOperation(new PlaceOperation(coord, 7));
        Assert.Equal(7, builder.Grid.GetVoxel(coord).PaletteIndex);
        Assert.True(builder.IsModified);
    }

    [Fact]
    public void ExecuteOperation_SetsSourceIdToLocal()
    {
        var builder = CreateBuilder();
        OperationAppliedEventArgs? captured = null;
        builder.OnOperationApplied += (_, args) => captured = args;

        builder.ExecuteOperation(new PlaceOperation(new VoxelCoord(1, 1, 1), 1));

        Assert.NotNull(captured);
        Assert.Equal("local", captured.SourceId);
        Assert.Equal("local", captured.Operation.SourceId);
    }

    [Fact]
    public void ExecuteOperation_FiresEvent()
    {
        var builder = CreateBuilder();
        var eventCount = 0;
        builder.OnOperationApplied += (_, _) => eventCount++;

        builder.ExecuteOperation(new PlaceOperation(new VoxelCoord(1, 1, 1), 1));
        Assert.Equal(1, eventCount);
    }

    [Fact]
    public void ExecuteOperation_EventContainsSerializedBytes()
    {
        var builder = CreateBuilder();
        OperationAppliedEventArgs? captured = null;
        builder.OnOperationApplied += (_, args) => captured = args;

        builder.ExecuteOperation(new PlaceOperation(new VoxelCoord(1, 1, 1), 1));

        Assert.NotNull(captured);
        Assert.NotNull(captured.SerializedBytes);
        Assert.True(captured.SerializedBytes.Length > 0);
        Assert.False(captured.IsUndo);
    }

    [Fact]
    public void ExecuteOperation_EventContainsAffectedChunks()
    {
        var builder = CreateBuilder();
        OperationAppliedEventArgs? captured = null;
        builder.OnOperationApplied += (_, args) => captured = args;

        builder.ExecuteOperation(new PlaceOperation(new VoxelCoord(1, 1, 1), 1));

        Assert.NotNull(captured);
        Assert.NotEmpty(captured.AffectedChunks);
        Assert.Contains(new ChunkCoord(0, 0, 0), captured.AffectedChunks);
    }

    #endregion

    #region ApplyExternalOperation

    [Fact]
    public void ApplyExternalOperation_SetsSourceId()
    {
        var builder = CreateBuilder();
        OperationAppliedEventArgs? captured = null;
        builder.OnOperationApplied += (_, args) => captured = args;

        builder.ApplyExternalOperation(new PlaceOperation(new VoxelCoord(1, 1, 1), 1), "generator");

        Assert.NotNull(captured);
        Assert.Equal("generator", captured.SourceId);
    }

    [Fact]
    public void ApplyExternalOperation_CreatesExternalStack()
    {
        var builder = CreateBuilder();
        var coord = new VoxelCoord(5, 5, 5);

        builder.ApplyExternalOperation(new PlaceOperation(coord, 7), "generator");
        Assert.Equal(7, builder.Grid.GetVoxel(coord).PaletteIndex);

        // Can undo externally
        Assert.True(builder.UndoExternal("generator"));
        Assert.True(builder.Grid.GetVoxel(coord).IsEmpty);
    }

    [Fact]
    public void ApplyExternalOperation_DoesNotAffectLocalStack()
    {
        var builder = CreateBuilder();

        builder.ApplyExternalOperation(new PlaceOperation(new VoxelCoord(1, 1, 1), 1), "generator");

        // Local undo should not undo external operation
        Assert.False(builder.Undo());
    }

    #endregion

    #region Undo/Redo

    [Fact]
    public void Undo_RevertsLastLocalOperation()
    {
        var builder = CreateBuilder();
        var coord = new VoxelCoord(5, 5, 5);

        builder.Place(coord, 7);
        Assert.Equal(7, builder.Grid.GetVoxel(coord).PaletteIndex);

        Assert.True(builder.Undo());
        Assert.True(builder.Grid.GetVoxel(coord).IsEmpty);
    }

    [Fact]
    public void Undo_WhenEmpty_ReturnsFalse()
    {
        var builder = CreateBuilder();
        Assert.False(builder.Undo());
    }

    [Fact]
    public void Undo_FiresEventWithIsUndoTrue()
    {
        var builder = CreateBuilder();
        builder.Place(new VoxelCoord(1, 1, 1), 1);

        OperationAppliedEventArgs? captured = null;
        builder.OnOperationApplied += (_, args) => captured = args;

        builder.Undo();
        Assert.NotNull(captured);
        Assert.True(captured.IsUndo);
    }

    [Fact]
    public void Redo_ReappliesLastUndone()
    {
        var builder = CreateBuilder();
        var coord = new VoxelCoord(5, 5, 5);

        builder.Place(coord, 7);
        builder.Undo();
        Assert.True(builder.Grid.GetVoxel(coord).IsEmpty);

        Assert.True(builder.Redo());
        Assert.Equal(7, builder.Grid.GetVoxel(coord).PaletteIndex);
    }

    [Fact]
    public void Redo_WhenEmpty_ReturnsFalse()
    {
        var builder = CreateBuilder();
        Assert.False(builder.Redo());
    }

    [Fact]
    public void UndoExternal_UnknownSource_ReturnsFalse()
    {
        var builder = CreateBuilder();
        Assert.False(builder.UndoExternal("nonexistent"));
    }

    [Fact]
    public void RedoExternal_UnknownSource_ReturnsFalse()
    {
        var builder = CreateBuilder();
        Assert.False(builder.RedoExternal("nonexistent"));
    }

    [Fact]
    public void UndoExternal_RevertsSourceOperation()
    {
        var builder = CreateBuilder();
        var coord = new VoxelCoord(5, 5, 5);

        builder.ApplyExternalOperation(new PlaceOperation(coord, 7), "gen");
        Assert.True(builder.UndoExternal("gen"));
        Assert.True(builder.Grid.GetVoxel(coord).IsEmpty);
    }

    [Fact]
    public void RedoExternal_ReappliesSourceOperation()
    {
        var builder = CreateBuilder();
        var coord = new VoxelCoord(5, 5, 5);

        builder.ApplyExternalOperation(new PlaceOperation(coord, 7), "gen");
        builder.UndoExternal("gen");
        Assert.True(builder.RedoExternal("gen"));
        Assert.Equal(7, builder.Grid.GetVoxel(coord).PaletteIndex);
    }

    #endregion

    #region Convenience Methods

    [Fact]
    public void Place_PlacesVoxel()
    {
        var builder = CreateBuilder();
        builder.Place(new VoxelCoord(3, 3, 3), 5);
        Assert.Equal(5, builder.Grid.GetVoxel(new VoxelCoord(3, 3, 3)).PaletteIndex);
    }

    [Fact]
    public void Erase_RemovesVoxel()
    {
        var builder = CreateBuilder();
        var coord = new VoxelCoord(3, 3, 3);
        builder.Place(coord, 5);
        builder.Erase(coord);
        Assert.True(builder.Grid.GetVoxel(coord).IsEmpty);
    }

    [Fact]
    public void BoxFill_FillsRegion()
    {
        var builder = CreateBuilder();
        var region = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(3, 3, 3));
        builder.BoxFill(region, 5);

        // 4x4x4 = 64 voxels
        Assert.Equal(64, builder.Grid.VoxelCount);
        Assert.Equal(5, builder.Grid.GetVoxel(new VoxelCoord(0, 0, 0)).PaletteIndex);
        Assert.Equal(5, builder.Grid.GetVoxel(new VoxelCoord(3, 3, 3)).PaletteIndex);
    }

    [Fact]
    public void BoxErase_ErasesRegion()
    {
        var builder = CreateBuilder();
        var region = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(3, 3, 3));
        builder.BoxFill(region, 5);
        builder.BoxErase(region);
        Assert.Equal(0, builder.Grid.VoxelCount);
    }

    [Fact]
    public void Replace_SwapsPaletteIndices()
    {
        var builder = CreateBuilder();
        builder.Place(new VoxelCoord(1, 0, 0), 3);
        builder.Place(new VoxelCoord(2, 0, 0), 3);
        builder.Place(new VoxelCoord(3, 0, 0), 5);

        builder.Replace(3, 9);

        Assert.Equal(9, builder.Grid.GetVoxel(new VoxelCoord(1, 0, 0)).PaletteIndex);
        Assert.Equal(9, builder.Grid.GetVoxel(new VoxelCoord(2, 0, 0)).PaletteIndex);
        Assert.Equal(5, builder.Grid.GetVoxel(new VoxelCoord(3, 0, 0)).PaletteIndex);
    }

    [Fact]
    public void BrushPaint_UsesActiveBrushAndPalette()
    {
        var builder = CreateBuilder();
        builder.ActivePaletteIndex = 7;
        builder.ActiveBrush = new BrushShape(BrushType.Cube, 1);

        builder.BrushPaint(new VoxelCoord(5, 5, 5));

        // Cube brush r=1 centered at (5,5,5): coords from (4,4,4) to (6,6,6) = 27 voxels
        Assert.Equal(27, builder.Grid.VoxelCount);
        Assert.Equal(7, builder.Grid.GetVoxel(new VoxelCoord(5, 5, 5)).PaletteIndex);
    }

    [Fact]
    public void BrushErase_ErasesWithActiveBrush()
    {
        var builder = CreateBuilder();
        builder.ActiveBrush = new BrushShape(BrushType.Cube, 1);

        // Fill a larger region, then erase with brush
        builder.BoxFill(new VoxelBounds(new VoxelCoord(3, 3, 3), new VoxelCoord(7, 7, 7)), 5);
        var before = builder.Grid.VoxelCount;

        builder.BrushErase(new VoxelCoord(5, 5, 5));
        Assert.True(builder.Grid.VoxelCount < before);
        Assert.True(builder.Grid.GetVoxel(new VoxelCoord(5, 5, 5)).IsEmpty);
    }

    #endregion

    #region Selection & Clipboard

    [Fact]
    public void Select_SetsSelection()
    {
        var builder = CreateBuilder();
        var region = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(3, 3, 3));
        builder.Select(region);
        Assert.Equal(region, builder.Selection);
    }

    [Fact]
    public void ClearSelection_NullsSelection()
    {
        var builder = CreateBuilder();
        builder.Select(new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(3, 3, 3)));
        builder.ClearSelection();
        Assert.Null(builder.Selection);
    }

    [Fact]
    public void Copy_WithoutSelection_Throws()
    {
        var builder = CreateBuilder();
        Assert.Throws<InvalidOperationException>(() => builder.Copy());
    }

    [Fact]
    public void Paste_WithoutClipboard_Throws()
    {
        var builder = CreateBuilder();
        Assert.Throws<InvalidOperationException>(() => builder.Paste(VoxelCoord.Zero));
    }

    [Fact]
    public void CopyPaste_CopiesVoxelsToNewLocation()
    {
        var builder = CreateBuilder();
        // Use palette indices that match what GetOrAddIndex allocates (1, 2, ...)
        builder.Place(new VoxelCoord(0, 0, 0), 1);
        builder.Place(new VoxelCoord(1, 0, 0), 2);

        builder.Select(new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(1, 0, 0)));
        builder.Copy();

        Assert.NotNull(builder.Clipboard);
        Assert.Equal(2, builder.Clipboard.Voxels.Count);

        // Paste at offset (10, 0, 0)
        builder.Paste(new VoxelCoord(10, 0, 0));

        // Pasted voxels exist (palette indices may be remapped during paste)
        Assert.False(builder.Grid.GetVoxel(new VoxelCoord(10, 0, 0)).IsEmpty);
        Assert.False(builder.Grid.GetVoxel(new VoxelCoord(11, 0, 0)).IsEmpty);

        // Original still intact
        Assert.Equal(1, builder.Grid.GetVoxel(new VoxelCoord(0, 0, 0)).PaletteIndex);
        Assert.Equal(2, builder.Grid.GetVoxel(new VoxelCoord(1, 0, 0)).PaletteIndex);
    }

    [Fact]
    public void Copy_SnapshotsPaletteEntries()
    {
        var builder = CreateBuilder();
        builder.Grid.Palette.GetOrAddIndex(
            new Color(255, 0, 0, 255), MaterialType.Diffuse, 0.5f);

        builder.Place(new VoxelCoord(0, 0, 0), 1);
        builder.Select(new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(0, 0, 0)));
        builder.Copy();

        Assert.NotNull(builder.Clipboard);
        Assert.True(builder.Clipboard.PaletteSnapshot.ContainsKey(1));
    }

    #endregion

    #region Compound Operations

    [Fact]
    public void BeginEndCompound_GroupsAsOneUndo()
    {
        var builder = CreateBuilder();
        var c1 = new VoxelCoord(1, 0, 0);
        var c2 = new VoxelCoord(2, 0, 0);
        var c3 = new VoxelCoord(3, 0, 0);

        builder.BeginCompound("multi place");
        builder.Place(c1, 1);
        builder.Place(c2, 2);
        builder.Place(c3, 3);
        builder.EndCompound();

        Assert.Equal(3, builder.Grid.VoxelCount);

        // Single undo reverts all three
        Assert.True(builder.Undo());
        Assert.True(builder.Grid.GetVoxel(c1).IsEmpty);
        Assert.True(builder.Grid.GetVoxel(c2).IsEmpty);
        Assert.True(builder.Grid.GetVoxel(c3).IsEmpty);
    }

    [Fact]
    public void BeginCompound_WhileActive_Throws()
    {
        var builder = CreateBuilder();
        builder.BeginCompound("first");
        Assert.Throws<InvalidOperationException>(() => builder.BeginCompound("second"));
    }

    [Fact]
    public void EndCompound_WhenNotActive_Throws()
    {
        var builder = CreateBuilder();
        Assert.Throws<InvalidOperationException>(() => builder.EndCompound());
    }

    [Fact]
    public void EndCompound_EmptyCompound_NoOp()
    {
        var builder = CreateBuilder();
        builder.BeginCompound("empty");
        builder.EndCompound();

        // No undo available — empty compound not pushed
        Assert.False(builder.Undo());
    }

    [Fact]
    public void Compound_OperationsAreExecutedDuringBeginEnd()
    {
        var builder = CreateBuilder();
        var coord = new VoxelCoord(5, 5, 5);

        builder.BeginCompound("test");
        builder.Place(coord, 7);
        // Voxel is placed immediately, not deferred to EndCompound
        Assert.Equal(7, builder.Grid.GetVoxel(coord).PaletteIndex);
        builder.EndCompound();
    }

    [Fact]
    public void Compound_DoesNotFireEventPerSubOp()
    {
        var builder = CreateBuilder();
        var eventCount = 0;
        builder.OnOperationApplied += (_, _) => eventCount++;

        builder.BeginCompound("test");
        builder.Place(new VoxelCoord(1, 0, 0), 1);
        builder.Place(new VoxelCoord(2, 0, 0), 2);
        builder.Place(new VoxelCoord(3, 0, 0), 3);
        builder.EndCompound();

        // Only the compound fires the event, not individual sub-ops
        Assert.Equal(1, eventCount);
    }

    #endregion

    #region DiffToOperation

    [Fact]
    public void DiffToOperation_ComputesDelta()
    {
        var before = new VoxelGrid(SmallBounds);
        var after = new VoxelGrid(SmallBounds);

        after.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));
        after.SetVoxel(new VoxelCoord(1, 0, 0), new Voxel(2, VoxelFlags.None));

        var patchOp = VoxelBuilderClass.DiffToOperation(before, after, "generator");

        Assert.NotNull(patchOp);
        Assert.Equal(VoxelOperationType.GridPatch, patchOp.OperationType);
        Assert.Equal("generator", patchOp.SourceId);
        Assert.True(patchOp.Delta.Length > 0);
    }

    [Fact]
    public void DiffToOperation_ApplyRestoresAfterState()
    {
        var before = new VoxelGrid(SmallBounds);
        var after = new VoxelGrid(SmallBounds);
        after.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(7, VoxelFlags.None));

        var patchOp = VoxelBuilderClass.DiffToOperation(before, after, "gen");

        // Apply to a fresh grid
        var target = new VoxelGrid(SmallBounds);
        patchOp.Execute(target, VoxelBuilderOptions.Default);

        Assert.Equal(7, target.GetVoxel(new VoxelCoord(5, 5, 5)).PaletteIndex);
    }

    #endregion

    #region Bridge

    [Fact]
    public void SetBridge_CallsOnGridLoaded()
    {
        var builder = CreateBuilder();
        using var bridge = new TestBridge();

        builder.SetBridge(bridge);

        Assert.Same(builder.Bridge, bridge);
        Assert.True(bridge.GridLoadedCalled);
    }

    [Fact]
    public void ExecuteOperation_WithBridge_CallsOnChunksModified()
    {
        var builder = CreateBuilder();
        using var bridge = new TestBridge();
        builder.SetBridge(bridge);

        builder.Place(new VoxelCoord(0, 0, 0), 1);
        Assert.True(bridge.ChunksModifiedCalled);
    }

    [Fact]
    public void ExecuteOperation_WithAutoMeshOff_DoesNotCallBridge()
    {
        var builder = CreateBuilder(new VoxelBuilderOptions(AutoMeshOnEdit: false));
        using var bridge = new TestBridge();
        builder.SetBridge(bridge);
        bridge.ChunksModifiedCalled = false; // Reset after SetBridge

        builder.Place(new VoxelCoord(0, 0, 0), 1);
        Assert.False(bridge.ChunksModifiedCalled);
    }

    #endregion

    #region MarkSaved

    [Fact]
    public void MarkSaved_ResetsIsModified()
    {
        var builder = CreateBuilder();
        builder.Place(new VoxelCoord(1, 1, 1), 1);
        Assert.True(builder.IsModified);

        builder.MarkSaved();
        Assert.False(builder.IsModified);
    }

    #endregion

    #region Multi-Source Undo Isolation

    [Fact]
    public void LocalAndExternal_UndoAreIsolated()
    {
        var builder = CreateBuilder();
        var localCoord = new VoxelCoord(1, 0, 0);
        var externalCoord = new VoxelCoord(2, 0, 0);

        builder.Place(localCoord, 1);
        builder.ApplyExternalOperation(new PlaceOperation(externalCoord, 2), "editor-2");

        // Undo local only affects local
        builder.Undo();
        Assert.True(builder.Grid.GetVoxel(localCoord).IsEmpty);
        Assert.Equal(2, builder.Grid.GetVoxel(externalCoord).PaletteIndex);

        // Undo external only affects external
        builder.UndoExternal("editor-2");
        Assert.True(builder.Grid.GetVoxel(externalCoord).IsEmpty);
    }

    #endregion

    private sealed class TestBridge : IVoxelBuilderBridge
    {
        public bool GridLoadedCalled { get; private set; }
        public bool ChunksModifiedCalled { get; set; }

        public void OnGridLoaded(VoxelGrid grid) => GridLoadedCalled = true;
        public void OnChunksModified(IReadOnlySet<ChunkCoord> coords) => ChunksModifiedCalled = true;
        public void OnPaletteChanged(Palette palette) { }
        public VoxelCoord? ScreenToVoxel(float screenX, float screenY) => null;
        public void SetMesher(BeyondImmersion.Bannou.VoxelCore.Meshing.IMesher mesher) { }
        public void Dispose() { }
    }
}
