using BeyondImmersion.Bannou.VoxelBuilder.Core;
using BeyondImmersion.Bannou.VoxelBuilder.Operations;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using Xunit;
using VoxelBuilderClass = BeyondImmersion.Bannou.VoxelBuilder.Core.VoxelBuilder;

namespace BeyondImmersion.Bannou.VoxelBuilder.Tests.Core;

/// <summary>
/// Unit tests for <see cref="OperationSerializer"/> binary round-trip serialization.
/// </summary>
public class OperationSerializerTests
{
    [Fact]
    public void PlaceOperation_RoundTrips()
    {
        var original = new PlaceOperation(new VoxelCoord(10, 20, 30), 42) { SourceId = "local" };
        var bytes = OperationSerializer.Serialize(original);
        var deserialized = OperationSerializer.Deserialize(bytes);

        var place = Assert.IsType<PlaceOperation>(deserialized);
        Assert.Equal(original.Coord, place.Coord);
        Assert.Equal(original.PaletteIndex, place.PaletteIndex);
        Assert.Equal("local", place.SourceId);
        Assert.Equal(VoxelOperationType.Place, place.OperationType);
    }

    [Fact]
    public void EraseOperation_RoundTrips()
    {
        var original = new EraseOperation(new VoxelCoord(-5, 10, 15)) { SourceId = "player-2" };
        var bytes = OperationSerializer.Serialize(original);
        var deserialized = OperationSerializer.Deserialize(bytes);

        var erase = Assert.IsType<EraseOperation>(deserialized);
        Assert.Equal(original.Coord, erase.Coord);
        Assert.Equal("player-2", erase.SourceId);
    }

    [Fact]
    public void BrushOperation_RoundTrips()
    {
        var original = new BrushOperation(
            new VoxelCoord(5, 5, 5),
            new BrushShape(BrushType.Cylinder, 7),
            42,
            erase: true) { SourceId = "gen" };

        var bytes = OperationSerializer.Serialize(original);
        var deserialized = OperationSerializer.Deserialize(bytes);

        var brush = Assert.IsType<BrushOperation>(deserialized);
        Assert.Equal(original.Center, brush.Center);
        Assert.Equal(BrushType.Cylinder, brush.Brush.Type);
        Assert.Equal(7, brush.Brush.Radius);
        Assert.Equal(42, brush.PaletteIndex);
        Assert.True(brush.Erase);
        Assert.Equal("gen", brush.SourceId);
    }

    [Fact]
    public void FillOperation_RoundTrips()
    {
        var limit = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(31, 31, 31));
        var original = new FillOperation(new VoxelCoord(10, 10, 10), 5, limit) { SourceId = "local" };

        var bytes = OperationSerializer.Serialize(original);
        var deserialized = OperationSerializer.Deserialize(bytes);

        var fill = Assert.IsType<FillOperation>(deserialized);
        Assert.Equal(original.Origin, fill.Origin);
        Assert.Equal(5, fill.FillPaletteIndex);
        Assert.Equal(limit, fill.Limit);
    }

    [Fact]
    public void BoxOperation_RoundTrips()
    {
        var bounds = new VoxelBounds(new VoxelCoord(2, 3, 4), new VoxelCoord(10, 11, 12));
        var original = new BoxOperation(bounds, 8, erase: true) { SourceId = "local" };

        var bytes = OperationSerializer.Serialize(original);
        var deserialized = OperationSerializer.Deserialize(bytes);

        var box = Assert.IsType<BoxOperation>(deserialized);
        Assert.Equal(bounds, box.Bounds);
        Assert.Equal(8, box.PaletteIndex);
        Assert.True(box.Erase);
    }

    [Fact]
    public void MirrorOperation_RoundTrips()
    {
        var original = new MirrorOperation(Axis.Z) { SourceId = "test" };
        var bytes = OperationSerializer.Serialize(original);
        var deserialized = OperationSerializer.Deserialize(bytes);

        var mirror = Assert.IsType<MirrorOperation>(deserialized);
        Assert.Equal(Axis.Z, mirror.MirrorAxis);
        Assert.Equal("test", mirror.SourceId);
    }

    [Fact]
    public void RotateOperation_RoundTrips()
    {
        var original = new RotateOperation(Axis.X) { SourceId = "local" };
        var bytes = OperationSerializer.Serialize(original);
        var deserialized = OperationSerializer.Deserialize(bytes);

        var rotate = Assert.IsType<RotateOperation>(deserialized);
        Assert.Equal(Axis.X, rotate.RotateAxis);
    }

    [Fact]
    public void ReplaceOperation_RoundTrips()
    {
        var original = new ReplaceOperation(3, 9) { SourceId = "local" };
        var bytes = OperationSerializer.Serialize(original);
        var deserialized = OperationSerializer.Deserialize(bytes);

        var replace = Assert.IsType<ReplaceOperation>(deserialized);
        Assert.Equal(3, replace.FromIndex);
        Assert.Equal(9, replace.ToIndex);
    }

    [Fact]
    public void CopyPasteOperation_RoundTrips()
    {
        var clipboard = new VoxelClipboard
        {
            Bounds = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(1, 0, 0))
        };
        clipboard.Voxels[new VoxelCoord(0, 0, 0)] = new Voxel(5, VoxelFlags.None);
        clipboard.Voxels[new VoxelCoord(1, 0, 0)] = new Voxel(7, VoxelFlags.Emissive);
        clipboard.PaletteSnapshot[5] = new PaletteEntry(new Color(255, 0, 0, 255), MaterialType.Diffuse, 0.5f);
        clipboard.PaletteSnapshot[7] = new PaletteEntry(new Color(0, 255, 0, 255), MaterialType.Metal, 0.8f);

        var original = new CopyPasteOperation(clipboard, new VoxelCoord(10, 0, 0)) { SourceId = "local" };
        var bytes = OperationSerializer.Serialize(original);
        var deserialized = OperationSerializer.Deserialize(bytes);

        var paste = Assert.IsType<CopyPasteOperation>(deserialized);
        Assert.Equal(new VoxelCoord(10, 0, 0), paste.PasteOffset);
        Assert.Equal(2, paste.Clipboard.Voxels.Count);
        Assert.Equal(2, paste.Clipboard.PaletteSnapshot.Count);

        // Verify palette entries preserved
        var entry5 = paste.Clipboard.PaletteSnapshot[5];
        Assert.Equal(255, entry5.Color.R);
        Assert.Equal(0, entry5.Color.G);
        Assert.Equal(MaterialType.Diffuse, entry5.Material);
        Assert.Equal(0.5f, entry5.Roughness);

        // Verify voxel flags preserved
        var v = paste.Clipboard.Voxels[new VoxelCoord(1, 0, 0)];
        Assert.Equal(7, v.PaletteIndex);
        Assert.Equal(VoxelFlags.Emissive, v.Flags);
    }

    [Fact]
    public void CompoundOperation_RoundTrips()
    {
        var subOps = new List<IVoxelOperation>
        {
            new PlaceOperation(new VoxelCoord(1, 0, 0), 1),
            new EraseOperation(new VoxelCoord(2, 0, 0)),
            new PlaceOperation(new VoxelCoord(3, 0, 0), 3)
        };
        var original = new CompoundOperation(subOps, "batch") { SourceId = "local" };

        var bytes = OperationSerializer.Serialize(original);
        var deserialized = OperationSerializer.Deserialize(bytes);

        var compound = Assert.IsType<CompoundOperation>(deserialized);
        Assert.Equal(3, compound.Operations.Count);
        Assert.IsType<PlaceOperation>(compound.Operations[0]);
        Assert.IsType<EraseOperation>(compound.Operations[1]);
        Assert.IsType<PlaceOperation>(compound.Operations[2]);
    }

    [Fact]
    public void GridPatchOperation_RoundTrips()
    {
        // Create a delta from two grids
        var bounds = new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(31, 31, 31));
        var before = new VoxelGrid(bounds);
        var after = new VoxelGrid(bounds);
        after.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(7, VoxelFlags.None));

        var patchOp = VoxelBuilderClass.DiffToOperation(before, after, "gen");
        var bytes = OperationSerializer.Serialize(patchOp);
        var deserialized = OperationSerializer.Deserialize(bytes);

        var patch = Assert.IsType<GridPatchOperation>(deserialized);
        Assert.Equal(VoxelOperationType.GridPatch, patch.OperationType);
        Assert.Equal("gen", patch.SourceId);
        Assert.True(patch.Delta.Length > 0);
    }

    [Fact]
    public void Serialize_PreservesNegativeCoordinates()
    {
        var original = new PlaceOperation(new VoxelCoord(-10, -20, -30), 1) { SourceId = "local" };
        var bytes = OperationSerializer.Serialize(original);
        var deserialized = OperationSerializer.Deserialize(bytes);

        var place = Assert.IsType<PlaceOperation>(deserialized);
        Assert.Equal(-10, place.Coord.X);
        Assert.Equal(-20, place.Coord.Y);
        Assert.Equal(-30, place.Coord.Z);
    }

    [Fact]
    public void Serialize_PreservesUnicodeSourceId()
    {
        var original = new PlaceOperation(VoxelCoord.Zero, 1) { SourceId = "プレイヤー1" };
        var bytes = OperationSerializer.Serialize(original);
        var deserialized = OperationSerializer.Deserialize(bytes);

        Assert.Equal("プレイヤー1", deserialized.SourceId);
    }

    [Fact]
    public void Serialize_Deterministic_SameBytesForSameInput()
    {
        var op = new PlaceOperation(new VoxelCoord(5, 5, 5), 42) { SourceId = "local" };
        var bytes1 = OperationSerializer.Serialize(op);
        var bytes2 = OperationSerializer.Serialize(op);
        Assert.Equal(bytes1, bytes2);
    }
}
