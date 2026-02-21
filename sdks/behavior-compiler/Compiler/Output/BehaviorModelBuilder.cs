// =============================================================================
// Behavior Model Builder
// Assembles final binary output from compiled components.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Compiler.Codegen;
using BeyondImmersion.Bannou.BehaviorCompiler.Runtime;
using System.Text;

namespace BeyondImmersion.Bannou.BehaviorCompiler.Compiler.Output;

/// <summary>
/// Builds the final behavior model binary from compiled components.
/// Writes raw bytes according to the binary format specification.
/// </summary>
public sealed class BehaviorModelBuilder
{
    private Guid _modelId = Guid.NewGuid();
    private BehaviorModelFlags _flags;

    // Extension info (if IsExtension flag set)
    private Guid? _parentModelId;
    private uint _attachPointHash;
    private uint _replacementFlowOffset;

    // State schema
    private readonly List<VariableDefinition> _inputs = new();
    private readonly List<VariableDefinition> _outputs = new();

    // Continuation points
    private readonly List<ContinuationPointDef> _continuationPoints = new();

    // Pools
    private IReadOnlyList<double>? _constantPool;
    private IReadOnlyList<string>? _stringTable;

    // Bytecode
    private byte[]? _bytecode;

    // Debug info
    private string? _debugSourcePath;
    private Dictionary<int, int>? _debugLineMap;

    /// <summary>
    /// Sets the model ID.
    /// </summary>
    public BehaviorModelBuilder WithModelId(Guid modelId)
    {
        _modelId = modelId;
        return this;
    }

    /// <summary>
    /// Sets the model flags.
    /// </summary>
    public BehaviorModelBuilder WithFlags(BehaviorModelFlags flags)
    {
        _flags = flags;
        return this;
    }

    /// <summary>
    /// Configures this model as an extension.
    /// </summary>
    public BehaviorModelBuilder AsExtension(
        Guid parentModelId,
        uint attachPointHash,
        uint replacementFlowOffset)
    {
        _flags |= BehaviorModelFlags.IsExtension;
        _parentModelId = parentModelId;
        _attachPointHash = attachPointHash;
        _replacementFlowOffset = replacementFlowOffset;
        return this;
    }

    /// <summary>
    /// Adds an input variable definition.
    /// </summary>
    /// <param name="name">Variable name.</param>
    /// <param name="defaultValue">Default value.</param>
    /// <param name="type">Variable type (0=Bool, 1=Int, 2=Float).</param>
    public BehaviorModelBuilder AddInput(string name, double defaultValue = 0.0, byte type = 2)
    {
        _inputs.Add(new VariableDefinition(name, type, defaultValue));
        return this;
    }

    /// <summary>
    /// Adds an output variable definition.
    /// </summary>
    /// <param name="name">Variable name.</param>
    /// <param name="defaultValue">Default value.</param>
    /// <param name="type">Variable type (0=Bool, 1=Int, 2=Float).</param>
    public BehaviorModelBuilder AddOutput(string name, double defaultValue = 0.0, byte type = 2)
    {
        _outputs.Add(new VariableDefinition(name, type, defaultValue));
        return this;
    }

    /// <summary>
    /// Adds a continuation point.
    /// </summary>
    public BehaviorModelBuilder AddContinuationPoint(
        string name,
        uint timeoutMs,
        uint defaultFlowOffset,
        uint bytecodeOffset)
    {
        _continuationPoints.Add(new ContinuationPointDef(
            name,
            HashName(name),
            timeoutMs,
            defaultFlowOffset,
            bytecodeOffset));

        if (_continuationPoints.Count == 1)
        {
            _flags |= BehaviorModelFlags.HasContinuationPoints;
        }

        return this;
    }

    /// <summary>
    /// Sets the constant pool from a builder.
    /// </summary>
    public BehaviorModelBuilder WithConstantPool(ConstantPoolBuilder builder)
    {
        _constantPool = builder.ToList();
        return this;
    }

    /// <summary>
    /// Sets the constant pool directly.
    /// </summary>
    public BehaviorModelBuilder WithConstantPool(IReadOnlyList<double> constants)
    {
        _constantPool = constants;
        return this;
    }

    /// <summary>
    /// Sets the string table from a builder.
    /// </summary>
    public BehaviorModelBuilder WithStringTable(StringTableBuilder builder)
    {
        _stringTable = builder.ToList();
        return this;
    }

    /// <summary>
    /// Sets the string table directly.
    /// </summary>
    public BehaviorModelBuilder WithStringTable(IReadOnlyList<string> strings)
    {
        _stringTable = strings;
        return this;
    }

    /// <summary>
    /// Sets the bytecode from an emitter.
    /// </summary>
    public BehaviorModelBuilder WithBytecode(BytecodeEmitter emitter)
    {
        _bytecode = emitter.ToArray();
        return this;
    }

    /// <summary>
    /// Sets the bytecode directly.
    /// </summary>
    public BehaviorModelBuilder WithBytecode(byte[] bytecode)
    {
        _bytecode = bytecode;
        return this;
    }

    /// <summary>
    /// Sets debug info.
    /// </summary>
    public BehaviorModelBuilder WithDebugInfo(string sourcePath, Dictionary<int, int> lineMap)
    {
        _debugSourcePath = sourcePath;
        _debugLineMap = lineMap;
        _flags |= BehaviorModelFlags.HasDebugInfo;
        return this;
    }

    /// <summary>
    /// Builds the final binary output.
    /// </summary>
    /// <returns>The compiled behavior model as raw bytes.</returns>
    public byte[] Build()
    {
        if (_bytecode == null || _bytecode.Length == 0)
        {
            throw new InvalidOperationException("Bytecode is required");
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        // Write header
        WriteHeader(writer);

        // Write extension header if applicable
        if ((_flags & BehaviorModelFlags.IsExtension) != 0)
        {
            WriteExtensionHeader(writer);
        }

        // Write state schema
        WriteStateSchema(writer);

        // Write continuation points if applicable
        if ((_flags & BehaviorModelFlags.HasContinuationPoints) != 0)
        {
            WriteContinuationPoints(writer);
        }

        // Write constant pool
        WriteConstantPool(writer);

        // Write string table
        WriteStringTable(writer);

        // Write bytecode
        writer.Write(_bytecode.Length);
        writer.Write(_bytecode);

        // Write debug info if applicable
        if ((_flags & BehaviorModelFlags.HasDebugInfo) != 0)
        {
            WriteDebugInfo(writer);
        }

        return stream.ToArray();
    }

    private void WriteHeader(BinaryWriter writer)
    {
        var checksum = ComputeChecksum();

        writer.Write(BehaviorModelHeader.Magic);
        writer.Write(BehaviorModelHeader.CurrentVersion);
        writer.Write((ushort)_flags);
        writer.Write(_modelId.ToByteArray());
        writer.Write(checksum);
        writer.Write(0u); // Reserved
    }

    private void WriteExtensionHeader(BinaryWriter writer)
    {
        if (!_parentModelId.HasValue)
        {
            throw new InvalidOperationException("Parent model ID required for extension");
        }

        writer.Write(_parentModelId.Value.ToByteArray());
        writer.Write(_attachPointHash);
        writer.Write(_replacementFlowOffset);
    }

    private void WriteStateSchema(BinaryWriter writer)
    {
        // Inputs
        writer.Write((ushort)_inputs.Count);
        foreach (var input in _inputs)
        {
            WriteVariableDefinition(writer, input);
        }

        // Outputs
        writer.Write((ushort)_outputs.Count);
        foreach (var output in _outputs)
        {
            WriteVariableDefinition(writer, output);
        }
    }

    private void WriteVariableDefinition(BinaryWriter writer, VariableDefinition def)
    {
        // Find name in string table (or -1 if not present)
        var nameIndex = -1;
        if (_stringTable != null)
        {
            for (var i = 0; i < _stringTable.Count; i++)
            {
                if (_stringTable[i] == def.Name)
                {
                    nameIndex = i;
                    break;
                }
            }
        }

        writer.Write(def.NameHash);      // uint32
        writer.Write(nameIndex);          // int32 (index in string table, -1 if not found)
        writer.Write(def.Type);           // byte (BehaviorVariableType)
        writer.Write(def.DefaultValue);   // double
    }

    private void WriteContinuationPoints(BinaryWriter writer)
    {
        writer.Write((ushort)_continuationPoints.Count);

        var stringTable = _stringTable ?? Array.Empty<string>();

        foreach (var cp in _continuationPoints)
        {
            writer.Write(cp.NameHash);

            // Find string index for name (or -1 if not in table)
            var nameIndex = -1;
            for (var i = 0; i < stringTable.Count; i++)
            {
                if (stringTable[i] == cp.Name)
                {
                    nameIndex = i;
                    break;
                }
            }
            writer.Write(nameIndex);

            writer.Write(cp.TimeoutMs);
            writer.Write(cp.DefaultFlowOffset);
            writer.Write(cp.BytecodeOffset);
        }
    }

    private void WriteConstantPool(BinaryWriter writer)
    {
        var constants = _constantPool ?? Array.Empty<double>();
        writer.Write((ushort)constants.Count);
        foreach (var constant in constants)
        {
            writer.Write(constant);
        }
    }

    private void WriteStringTable(BinaryWriter writer)
    {
        var strings = _stringTable ?? Array.Empty<string>();
        writer.Write((ushort)strings.Count);
        foreach (var str in strings)
        {
            var bytes = Encoding.UTF8.GetBytes(str);
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }
    }

    private void WriteDebugInfo(BinaryWriter writer)
    {
        var sourcePath = _debugSourcePath ?? "";
        var pathBytes = Encoding.UTF8.GetBytes(sourcePath);
        writer.Write((ushort)pathBytes.Length);
        writer.Write(pathBytes);

        var lineMap = _debugLineMap ?? new Dictionary<int, int>();
        writer.Write((ushort)lineMap.Count);
        foreach (var (offset, line) in lineMap)
        {
            writer.Write(offset);
            writer.Write(line);
        }
    }

    private uint ComputeChecksum()
    {
        // FNV-1a hash of bytecode
        uint hash = 2166136261u;
        if (_bytecode != null)
        {
            foreach (var b in _bytecode)
            {
                hash ^= b;
                hash *= 16777619u;
            }
        }
        return hash;
    }

    /// <summary>
    /// Computes FNV-1a hash of a variable name.
    /// </summary>
    public static uint HashName(string name)
    {
        uint hash = 2166136261u;
        foreach (var c in name)
        {
            hash ^= c;
            hash *= 16777619u;
        }
        return hash;
    }

    /// <summary>
    /// Resets the builder to initial state.
    /// </summary>
    public void Reset()
    {
        _modelId = Guid.NewGuid();
        _flags = 0;
        _parentModelId = null;
        _attachPointHash = 0;
        _replacementFlowOffset = 0;
        _inputs.Clear();
        _outputs.Clear();
        _continuationPoints.Clear();
        _constantPool = null;
        _stringTable = null;
        _bytecode = null;
        _debugSourcePath = null;
        _debugLineMap = null;
    }

    private readonly record struct VariableDefinition(string Name, byte Type, double DefaultValue)
    {
        public uint NameHash => HashName(Name);
    }

    private readonly record struct ContinuationPointDef(
        string Name,
        uint NameHash,
        uint TimeoutMs,
        uint DefaultFlowOffset,
        uint BytecodeOffset);
}
