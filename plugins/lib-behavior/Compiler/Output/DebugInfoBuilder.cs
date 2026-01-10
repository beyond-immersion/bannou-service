// =============================================================================
// Debug Info Builder
// Generates debug information for development builds.
// =============================================================================

using System.Text;

namespace BeyondImmersion.Bannou.Behavior.Compiler.Output;

/// <summary>
/// Builds debug information for behavior models.
/// Maps bytecode offsets to source locations.
/// </summary>
public sealed class DebugInfoBuilder
{
    private readonly List<SourceMapping> _mappings = new();
    private readonly Dictionary<string, int> _flowOffsets = new();
    private string? _sourcePath;

    /// <summary>
    /// Sets the source file path.
    /// </summary>
    /// <param name="path">The source file path.</param>
    /// <returns>This builder for chaining.</returns>
    public DebugInfoBuilder WithSourcePath(string path)
    {
        _sourcePath = path;
        return this;
    }

    /// <summary>
    /// Records a flow's bytecode offset.
    /// </summary>
    /// <param name="flowName">The flow name.</param>
    /// <param name="offset">The bytecode offset.</param>
    /// <returns>This builder for chaining.</returns>
    public DebugInfoBuilder RecordFlowOffset(string flowName, int offset)
    {
        _flowOffsets[flowName] = offset;
        return this;
    }

    /// <summary>
    /// Records a source location mapping.
    /// </summary>
    /// <param name="bytecodeOffset">The bytecode offset.</param>
    /// <param name="sourceLine">The source line number (1-based).</param>
    /// <param name="sourceColumn">The source column number (1-based).</param>
    /// <returns>This builder for chaining.</returns>
    public DebugInfoBuilder RecordMapping(int bytecodeOffset, int sourceLine, int sourceColumn = 0)
    {
        _mappings.Add(new SourceMapping(bytecodeOffset, sourceLine, sourceColumn));
        return this;
    }

    /// <summary>
    /// Builds the debug info as a byte array.
    /// </summary>
    /// <returns>The serialized debug info.</returns>
    public byte[] Build()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Magic "DBUG"
        writer.Write((uint)0x47554244);

        // Version
        writer.Write((ushort)1);

        // Source path (length-prefixed UTF-8)
        var pathBytes = string.IsNullOrEmpty(_sourcePath)
            ? Array.Empty<byte>()
            : Encoding.UTF8.GetBytes(_sourcePath);
        writer.Write((ushort)pathBytes.Length);
        writer.Write(pathBytes);

        // Flow offsets
        writer.Write((ushort)_flowOffsets.Count);
        foreach (var (name, offset) in _flowOffsets.OrderBy(kv => kv.Value))
        {
            var nameBytes = Encoding.UTF8.GetBytes(name);
            writer.Write((byte)nameBytes.Length);
            writer.Write(nameBytes);
            writer.Write((uint)offset);
        }

        // Source mappings (sorted by bytecode offset)
        var sortedMappings = _mappings.OrderBy(m => m.BytecodeOffset).ToList();
        writer.Write((uint)sortedMappings.Count);

        // Use delta encoding for efficiency
        var lastOffset = 0;
        var lastLine = 0;
        foreach (var mapping in sortedMappings)
        {
            // Delta from last offset (variable-length encoded)
            WriteVarInt(writer, mapping.BytecodeOffset - lastOffset);
            // Delta from last line
            WriteVarInt(writer, mapping.SourceLine - lastLine);
            // Column (usually 0)
            WriteVarInt(writer, mapping.SourceColumn);

            lastOffset = mapping.BytecodeOffset;
            lastLine = mapping.SourceLine;
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Gets the source location for a bytecode offset.
    /// </summary>
    /// <param name="bytecodeOffset">The bytecode offset.</param>
    /// <returns>The source location, or null if not found.</returns>
    public SourceLocation? GetSourceLocation(int bytecodeOffset)
    {
        // Find the mapping with the largest offset <= bytecodeOffset
        SourceMapping? best = null;
        foreach (var mapping in _mappings)
        {
            if (mapping.BytecodeOffset <= bytecodeOffset)
            {
                if (best == null || mapping.BytecodeOffset > best.Value.BytecodeOffset)
                {
                    best = mapping;
                }
            }
        }

        if (best == null)
        {
            return null;
        }

        return new SourceLocation(_sourcePath ?? "", best.Value.SourceLine, best.Value.SourceColumn);
    }

    private static void WriteVarInt(BinaryWriter writer, int value)
    {
        // Zig-zag encode for signed values
        var encoded = (uint)((value << 1) ^ (value >> 31));

        // Write as variable-length unsigned
        while (encoded >= 0x80)
        {
            writer.Write((byte)(encoded | 0x80));
            encoded >>= 7;
        }
        writer.Write((byte)encoded);
    }

    private readonly record struct SourceMapping(int BytecodeOffset, int SourceLine, int SourceColumn);
}

/// <summary>
/// A source code location.
/// </summary>
public readonly record struct SourceLocation(string FilePath, int Line, int Column)
{
    /// <summary>
    /// Returns a string representation of this location.
    /// </summary>
    public override string ToString()
    {
        if (Column > 0)
        {
            return $"{FilePath}:{Line}:{Column}";
        }
        return $"{FilePath}:{Line}";
    }
}
