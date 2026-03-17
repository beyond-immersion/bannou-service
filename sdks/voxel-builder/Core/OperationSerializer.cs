using System.Text;
using BeyondImmersion.Bannou.VoxelBuilder.Operations;
using BeyondImmersion.Bannou.VoxelCore.Math;

namespace BeyondImmersion.Bannou.VoxelBuilder.Core;

/// <summary>
/// Serializes and deserializes <see cref="IVoxelOperation"/> instances to/from bytes.
/// Uses a type-discriminated binary format. Before-state snapshots are NOT serialized —
/// the receiver recomputes them by reading grid state before applying.
/// </summary>
public static class OperationSerializer
{
    /// <summary>
    /// Serializes an operation to bytes: type discriminator + source ID + type-specific payload.
    /// </summary>
    /// <param name="operation">The operation to serialize.</param>
    /// <returns>Binary representation.</returns>
    public static byte[] Serialize(IVoxelOperation operation)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);

        writer.Write((byte)operation.OperationType);
        WriteString(writer, operation.SourceId);

        switch (operation)
        {
            case PlaceOperation place:
                WriteCoord(writer, place.Coord);
                writer.Write(place.PaletteIndex);
                break;

            case EraseOperation erase:
                WriteCoord(writer, erase.Coord);
                break;

            case BrushOperation brush:
                WriteCoord(writer, brush.Center);
                writer.Write((byte)brush.Brush.Type);
                writer.Write(brush.Brush.Radius);
                writer.Write(brush.PaletteIndex);
                writer.Write(brush.Erase);
                break;

            case FillOperation fill:
                WriteCoord(writer, fill.Origin);
                writer.Write(fill.FillPaletteIndex);
                WriteBounds(writer, fill.Limit);
                break;

            case BoxOperation box:
                WriteBounds(writer, box.Bounds);
                writer.Write(box.PaletteIndex);
                writer.Write(box.Erase);
                break;

            case MirrorOperation mirror:
                writer.Write((byte)mirror.MirrorAxis);
                break;

            case RotateOperation rotate:
                writer.Write((byte)rotate.RotateAxis);
                break;

            case ReplaceOperation replace:
                writer.Write(replace.FromIndex);
                writer.Write(replace.ToIndex);
                break;

            case CopyPasteOperation paste:
                WriteCoord(writer, paste.PasteOffset);
                // Clipboard voxels
                writer.Write(paste.Clipboard.Voxels.Count);
                foreach (var (coord, voxel) in paste.Clipboard.Voxels)
                {
                    WriteCoord(writer, coord);
                    writer.Write(voxel.PaletteIndex);
                    writer.Write((byte)voxel.Flags);
                }
                // Palette snapshot
                writer.Write(paste.Clipboard.PaletteSnapshot.Count);
                foreach (var (idx, entry) in paste.Clipboard.PaletteSnapshot)
                {
                    writer.Write(idx);
                    writer.Write(entry.Color.R);
                    writer.Write(entry.Color.G);
                    writer.Write(entry.Color.B);
                    writer.Write(entry.Color.A);
                    writer.Write((byte)entry.Material);
                    writer.Write(entry.Roughness);
                }
                break;

            case CompoundOperation compound:
                writer.Write(compound.Operations.Count);
                foreach (var subOp in compound.Operations)
                {
                    var subBytes = Serialize(subOp);
                    writer.Write(subBytes.Length);
                    writer.Write(subBytes);
                }
                break;

            case GridPatchOperation patch:
                writer.Write(patch.Delta.Length);
                writer.Write(patch.Delta);
                break;

            default:
                throw new NotSupportedException($"Unknown operation type: {operation.OperationType}");
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Deserializes an operation from bytes.
    /// </summary>
    /// <param name="data">Binary data from <see cref="Serialize"/>.</param>
    /// <returns>The deserialized operation (before-state not populated — call Execute to capture).</returns>
    public static IVoxelOperation Deserialize(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        var type = (VoxelOperationType)reader.ReadByte();
        var sourceId = ReadString(reader);

        switch (type)
        {
            case VoxelOperationType.Place:
                return new PlaceOperation(ReadCoord(reader), reader.ReadByte()) { SourceId = sourceId };

            case VoxelOperationType.Erase:
                return new EraseOperation(ReadCoord(reader)) { SourceId = sourceId };

            case VoxelOperationType.Brush:
            {
                var center = ReadCoord(reader);
                var brushType = (BrushType)reader.ReadByte();
                var radius = reader.ReadInt32();
                var palIdx = reader.ReadByte();
                var erase = reader.ReadBoolean();
                return new BrushOperation(center, new BrushShape(brushType, radius), palIdx, erase) { SourceId = sourceId };
            }

            case VoxelOperationType.Fill:
            {
                var origin = ReadCoord(reader);
                var palIdx = reader.ReadByte();
                var limit = ReadBounds(reader);
                return new FillOperation(origin, palIdx, limit) { SourceId = sourceId };
            }

            case VoxelOperationType.Box:
            {
                var bounds = ReadBounds(reader);
                var palIdx = reader.ReadByte();
                var erase = reader.ReadBoolean();
                return new BoxOperation(bounds, palIdx, erase) { SourceId = sourceId };
            }

            case VoxelOperationType.Mirror:
            {
                var axis = (Axis)reader.ReadByte();
                return new MirrorOperation(axis) { SourceId = sourceId };
            }

            case VoxelOperationType.Rotate:
            {
                var axis = (Axis)reader.ReadByte();
                return new RotateOperation(axis) { SourceId = sourceId };
            }

            case VoxelOperationType.Replace:
                return new ReplaceOperation(reader.ReadByte(), reader.ReadByte()) { SourceId = sourceId };

            case VoxelOperationType.CopyPaste:
            {
                var offset = ReadCoord(reader);
                var clipboard = new VoxelClipboard();
                var voxelCount = reader.ReadInt32();
                for (var i = 0; i < voxelCount; i++)
                {
                    var coord = ReadCoord(reader);
                    var palIdx = reader.ReadByte();
                    var flags = (VoxelCore.Grid.VoxelFlags)reader.ReadByte();
                    clipboard.Voxels[coord] = new VoxelCore.Grid.Voxel(palIdx, flags);
                }
                var snapCount = reader.ReadInt32();
                for (var i = 0; i < snapCount; i++)
                {
                    var idx = reader.ReadByte();
                    var r = reader.ReadByte();
                    var g = reader.ReadByte();
                    var b = reader.ReadByte();
                    var a = reader.ReadByte();
                    var mat = (VoxelCore.Grid.MaterialType)reader.ReadByte();
                    var rough = reader.ReadSingle();
                    clipboard.PaletteSnapshot[idx] = new VoxelCore.Grid.PaletteEntry(
                        new VoxelCore.Grid.Color(r, g, b, a), mat, rough);
                }
                return new CopyPasteOperation(clipboard, offset) { SourceId = sourceId };
            }

            case VoxelOperationType.Compound:
            {
                var count = reader.ReadInt32();
                var subOps = new List<IVoxelOperation>(count);
                for (var i = 0; i < count; i++)
                {
                    var subLength = reader.ReadInt32();
                    var subBytes = reader.ReadBytes(subLength);
                    subOps.Add(Deserialize(subBytes));
                }
                return new CompoundOperation(subOps, "compound") { SourceId = sourceId };
            }

            case VoxelOperationType.GridPatch:
            {
                var deltaLength = reader.ReadInt32();
                var delta = reader.ReadBytes(deltaLength);
                return new GridPatchOperation(delta, sourceId);
            }

            default:
                throw new NotSupportedException($"Unknown operation type discriminator: {(byte)type}");
        }
    }

    private static void WriteCoord(BinaryWriter w, VoxelCoord c)
    {
        w.Write(c.X); w.Write(c.Y); w.Write(c.Z);
    }

    private static VoxelCoord ReadCoord(BinaryReader r) =>
        new(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());

    private static void WriteBounds(BinaryWriter w, VoxelBounds b)
    {
        WriteCoord(w, b.Min); WriteCoord(w, b.Max);
    }

    private static VoxelBounds ReadBounds(BinaryReader r) =>
        new(ReadCoord(r), ReadCoord(r));

    private static void WriteString(BinaryWriter w, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        w.Write((ushort)bytes.Length);
        w.Write(bytes);
    }

    private static string ReadString(BinaryReader r)
    {
        var length = r.ReadUInt16();
        var bytes = r.ReadBytes(length);
        return Encoding.UTF8.GetString(bytes);
    }
}
