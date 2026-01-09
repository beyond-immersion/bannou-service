// =============================================================================
// Behavior Model Header
// Fixed-size header structure for behavior model binary format.
// =============================================================================

namespace BeyondImmersion.BannouService.Behavior.Runtime;

/// <summary>
/// Fixed-size header for behavior model binary format (32 bytes).
/// </summary>
public readonly struct BehaviorModelHeader
{
    /// <summary>
    /// Magic bytes identifying the format: "ABML" (0x4C4D4241).
    /// </summary>
    public const uint Magic = 0x4C4D4241; // "ABML" in little-endian

    /// <summary>
    /// Current format version.
    /// </summary>
    public const ushort CurrentVersion = 1;

    /// <summary>
    /// Header size in bytes.
    /// </summary>
    public const int Size = 32;

    /// <summary>
    /// Format version number.
    /// </summary>
    public ushort Version { get; init; }

    /// <summary>
    /// Model flags.
    /// </summary>
    public BehaviorModelFlags Flags { get; init; }

    /// <summary>
    /// Unique model identifier.
    /// </summary>
    public Guid ModelId { get; init; }

    /// <summary>
    /// CRC32 checksum of the model data (excluding header).
    /// </summary>
    public uint Checksum { get; init; }

    /// <summary>
    /// Reserved for future use.
    /// </summary>
    public uint Reserved { get; init; }

    /// <summary>
    /// Creates a new header.
    /// </summary>
    public BehaviorModelHeader(ushort version, BehaviorModelFlags flags, Guid modelId, uint checksum)
    {
        Version = version;
        Flags = flags;
        ModelId = modelId;
        Checksum = checksum;
        Reserved = 0;
    }

    /// <summary>
    /// Deserializes a header from binary data.
    /// </summary>
    /// <param name="reader">Binary reader positioned at start of header.</param>
    /// <returns>Deserialized header.</returns>
    /// <exception cref="InvalidDataException">If magic bytes don't match.</exception>
    public static BehaviorModelHeader Deserialize(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var magic = reader.ReadUInt32();
        if (magic != Magic)
        {
            throw new InvalidDataException(
                $"Invalid behavior model magic bytes. Expected 0x{Magic:X8}, got 0x{magic:X8}");
        }

        var version = reader.ReadUInt16();
        var flags = (BehaviorModelFlags)reader.ReadUInt16();
        var modelIdBytes = reader.ReadBytes(16);
        var modelId = new Guid(modelIdBytes);
        var checksum = reader.ReadUInt32();
        var reserved = reader.ReadUInt32();

        return new BehaviorModelHeader(version, flags, modelId, checksum)
        {
            Reserved = reserved
        };
    }

    /// <summary>
    /// Serializes the header to binary data.
    /// </summary>
    /// <param name="writer">Binary writer.</param>
    public void Serialize(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.Write(Magic);
        writer.Write(Version);
        writer.Write((ushort)Flags);
        writer.Write(ModelId.ToByteArray());
        writer.Write(Checksum);
        writer.Write(Reserved);
    }

    /// <summary>
    /// Checks if this model version is compatible with the current runtime.
    /// </summary>
    public bool IsCompatible => Version <= CurrentVersion;

    /// <summary>
    /// Checks if this model has debug info.
    /// </summary>
    public bool HasDebugInfo => (Flags & BehaviorModelFlags.HasDebugInfo) != 0;

    /// <summary>
    /// Checks if this model has continuation points.
    /// </summary>
    public bool HasContinuationPoints => (Flags & BehaviorModelFlags.HasContinuationPoints) != 0;

    /// <summary>
    /// Checks if this model is an extension.
    /// </summary>
    public bool IsExtension => (Flags & BehaviorModelFlags.IsExtension) != 0;
}

/// <summary>
/// Extension header for models that attach to other models (only present if IsExtension flag set).
/// </summary>
public readonly struct ExtensionHeader
{
    /// <summary>
    /// Extension header size in bytes.
    /// </summary>
    public const int Size = 24;

    /// <summary>
    /// Parent model ID that this extension attaches to.
    /// </summary>
    public Guid ParentModelId { get; init; }

    /// <summary>
    /// Hash of the continuation point name this extension attaches to.
    /// </summary>
    public uint AttachPointHash { get; init; }

    /// <summary>
    /// Offset to the replacement flow in bytecode.
    /// </summary>
    public uint ReplacementFlowOffset { get; init; }

    /// <summary>
    /// Creates a new extension header.
    /// </summary>
    public ExtensionHeader(Guid parentModelId, uint attachPointHash, uint replacementFlowOffset)
    {
        ParentModelId = parentModelId;
        AttachPointHash = attachPointHash;
        ReplacementFlowOffset = replacementFlowOffset;
    }

    /// <summary>
    /// Deserializes an extension header from binary data.
    /// </summary>
    public static ExtensionHeader Deserialize(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var parentIdBytes = reader.ReadBytes(16);
        var parentModelId = new Guid(parentIdBytes);
        var attachPointHash = reader.ReadUInt32();
        var replacementFlowOffset = reader.ReadUInt32();

        return new ExtensionHeader(parentModelId, attachPointHash, replacementFlowOffset);
    }

    /// <summary>
    /// Serializes the extension header to binary data.
    /// </summary>
    public void Serialize(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.Write(ParentModelId.ToByteArray());
        writer.Write(AttachPointHash);
        writer.Write(ReplacementFlowOffset);
    }
}
