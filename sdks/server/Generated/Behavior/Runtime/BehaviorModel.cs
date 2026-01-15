// =============================================================================
// Behavior Model
// Complete compiled behavior model with bytecode and metadata.
// =============================================================================

namespace BeyondImmersion.Bannou.Server.Behavior.Runtime;

/// <summary>
/// A compiled behavior model ready for execution.
/// Contains bytecode, state schema, constant pool, string table, and optional debug info.
/// </summary>
/// <remarks>
/// <para>
/// Binary format:
/// - Header (32 bytes)
/// - Extension header (24 bytes, optional)
/// - State schema (variable)
/// - Continuation points table (variable, optional)
/// - Constant pool (variable)
/// - String table (variable)
/// - Bytecode (variable)
/// - Debug info (variable, optional)
/// </para>
/// </remarks>
public sealed class BehaviorModel
{
    /// <summary>
    /// Model header containing version, flags, ID, and checksum.
    /// </summary>
    public BehaviorModelHeader Header { get; }

    /// <summary>
    /// Extension header (only present if model is an extension).
    /// </summary>
    public ExtensionHeader? ExtensionHeader { get; }

    /// <summary>
    /// Input/output state schema.
    /// </summary>
    public StateSchema Schema { get; }

    /// <summary>
    /// Continuation points for streaming composition.
    /// </summary>
    public ContinuationPointTable ContinuationPoints { get; }

    /// <summary>
    /// Constant pool containing literal values.
    /// All values are stored as doubles.
    /// </summary>
    public IReadOnlyList<double> ConstantPool { get; }

    /// <summary>
    /// String table for string literals.
    /// </summary>
    public IReadOnlyList<string> StringTable { get; }

    /// <summary>
    /// Compiled bytecode.
    /// </summary>
    public byte[] Bytecode { get; }

    /// <summary>
    /// Debug info (only present if HasDebugInfo flag set).
    /// </summary>
    public DebugInfo? DebugInfo { get; }

    /// <summary>
    /// Unique model identifier.
    /// </summary>
    public Guid Id => Header.ModelId;

    /// <summary>
    /// Format version.
    /// </summary>
    public ushort Version => Header.Version;

    /// <summary>
    /// Model flags.
    /// </summary>
    public BehaviorModelFlags Flags => Header.Flags;

    /// <summary>
    /// Whether this model is an extension that attaches to another model.
    /// </summary>
    public bool IsExtension => Header.IsExtension;

    /// <summary>
    /// Maximum stack depth required for execution.
    /// Computed from bytecode analysis during loading.
    /// </summary>
    public int MaxStackDepth { get; }

    /// <summary>
    /// Number of local variables used.
    /// </summary>
    public int LocalCount { get; }

    /// <summary>
    /// Creates a new behavior model.
    /// </summary>
    public BehaviorModel(
        BehaviorModelHeader header,
        ExtensionHeader? extensionHeader,
        StateSchema schema,
        ContinuationPointTable continuationPoints,
        IReadOnlyList<double> constantPool,
        IReadOnlyList<string> stringTable,
        byte[] bytecode,
        DebugInfo? debugInfo = null)
    {
        Header = header;
        ExtensionHeader = extensionHeader;
        Schema = schema;
        ContinuationPoints = continuationPoints ?? ContinuationPointTable.Empty;
        ConstantPool = constantPool;
        StringTable = stringTable;
        Bytecode = bytecode;
        DebugInfo = debugInfo;

        // Analyze bytecode to determine stack and local requirements
        (MaxStackDepth, LocalCount) = AnalyzeBytecode(bytecode);
    }

    /// <summary>
    /// Deserializes a behavior model from binary data.
    /// </summary>
    /// <param name="data">Binary model data.</param>
    /// <returns>Deserialized model.</returns>
    public static BehaviorModel Deserialize(byte[] data)
    {

        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);

        return Deserialize(reader);
    }

    /// <summary>
    /// Deserializes a behavior model from a binary reader.
    /// </summary>
    public static BehaviorModel Deserialize(BinaryReader reader)
    {

        // Header
        var header = BehaviorModelHeader.Deserialize(reader);

        if (!header.IsCompatible)
        {
            throw new InvalidDataException(
                $"Behavior model version {header.Version} is not compatible with runtime version {BehaviorModelHeader.CurrentVersion}");
        }

        // Extension header (optional)
        ExtensionHeader? extensionHeader = null;
        if (header.IsExtension)
        {
            extensionHeader = Runtime.ExtensionHeader.Deserialize(reader);
        }

        // State schema
        var schema = StateSchema.Deserialize(reader);

        // Continuation points (optional)
        ContinuationPointTable continuationPoints;
        if (header.HasContinuationPoints)
        {
            continuationPoints = ContinuationPointTable.Deserialize(reader);
        }
        else
        {
            continuationPoints = ContinuationPointTable.Empty;
        }

        // Constant pool
        var constantCount = reader.ReadUInt16();
        var constantPool = new double[constantCount];
        for (var i = 0; i < constantCount; i++)
        {
            constantPool[i] = reader.ReadDouble();
        }

        // String table
        var stringCount = reader.ReadUInt16();
        var stringTable = new string[stringCount];
        for (var i = 0; i < stringCount; i++)
        {
            var length = reader.ReadUInt16();
            var bytes = reader.ReadBytes(length);
            stringTable[i] = System.Text.Encoding.UTF8.GetString(bytes);
        }

        // Bytecode
        var bytecodeLength = reader.ReadInt32();
        var bytecode = reader.ReadBytes(bytecodeLength);

        // Debug info (optional)
        DebugInfo? debugInfo = null;
        if (header.HasDebugInfo)
        {
            debugInfo = Runtime.DebugInfo.Deserialize(reader);
        }

        return new BehaviorModel(
            header,
            extensionHeader,
            schema,
            continuationPoints,
            constantPool,
            stringTable,
            bytecode,
            debugInfo);
    }

    /// <summary>
    /// Serializes the behavior model to binary data.
    /// </summary>
    public byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        Serialize(writer);

        return stream.ToArray();
    }

    /// <summary>
    /// Serializes the behavior model to a binary writer.
    /// </summary>
    public void Serialize(BinaryWriter writer)
    {

        // Header
        Header.Serialize(writer);

        // Extension header (optional)
        if (Header.IsExtension && ExtensionHeader.HasValue)
        {
            ExtensionHeader.Value.Serialize(writer);
        }

        // State schema
        Schema.Serialize(writer);

        // Continuation points (optional)
        if (Header.HasContinuationPoints)
        {
            ContinuationPoints.Serialize(writer);
        }

        // Constant pool
        writer.Write((ushort)ConstantPool.Count);
        foreach (var constant in ConstantPool)
        {
            writer.Write(constant);
        }

        // String table
        writer.Write((ushort)StringTable.Count);
        foreach (var str in StringTable)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(str);
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }

        // Bytecode
        writer.Write(Bytecode.Length);
        writer.Write(Bytecode);

        // Debug info (optional)
        if (Header.HasDebugInfo && DebugInfo != null)
        {
            DebugInfo.Serialize(writer);
        }
    }

    /// <summary>
    /// Analyzes bytecode to determine stack depth and local count requirements.
    /// </summary>
    private static (int maxStackDepth, int localCount) AnalyzeBytecode(byte[] bytecode)
    {
        var maxStack = 0;
        var currentStack = 0;
        var maxLocal = 0;

        var i = 0;
        while (i < bytecode.Length)
        {
            var opcode = (BehaviorOpcode)bytecode[i++];

            // Track stack changes
            var stackDelta = GetStackDelta(opcode);
            currentStack += stackDelta;
            if (currentStack > maxStack)
            {
                maxStack = currentStack;
            }

            // Track local variable usage
            if (opcode is BehaviorOpcode.PushLocal or BehaviorOpcode.StoreLocal)
            {
                if (i < bytecode.Length)
                {
                    var localIdx = bytecode[i];
                    if (localIdx + 1 > maxLocal)
                    {
                        maxLocal = localIdx + 1;
                    }
                }
            }

            // Skip operands
            i += GetOperandSize(opcode);
        }

        // Ensure reasonable minimums
        return (Math.Max(maxStack, 16), Math.Max(maxLocal, 16));
    }

    /// <summary>
    /// Gets the stack delta for an opcode (positive = push, negative = pop).
    /// </summary>
    private static int GetStackDelta(BehaviorOpcode opcode)
    {
        return opcode switch
        {
            // Pushes (+1)
            BehaviorOpcode.PushConst => 1,
            BehaviorOpcode.PushInput => 1,
            BehaviorOpcode.PushLocal => 1,
            BehaviorOpcode.PushString => 1,
            BehaviorOpcode.Dup => 1,
            BehaviorOpcode.Rand => 1,
            BehaviorOpcode.ExtensionAvailable => 1,

            // Pops (-1)
            BehaviorOpcode.Pop => -1,
            BehaviorOpcode.StoreLocal => -1,
            BehaviorOpcode.SetOutput => -1,
            BehaviorOpcode.Neg => 0, // pops 1, pushes 1
            BehaviorOpcode.Not => 0,
            BehaviorOpcode.Abs => 0,
            BehaviorOpcode.Floor => 0,
            BehaviorOpcode.Ceil => 0,

            // Binary ops (pop 2, push 1 = -1)
            BehaviorOpcode.Add => -1,
            BehaviorOpcode.Sub => -1,
            BehaviorOpcode.Mul => -1,
            BehaviorOpcode.Div => -1,
            BehaviorOpcode.Mod => -1,
            BehaviorOpcode.Eq => -1,
            BehaviorOpcode.Ne => -1,
            BehaviorOpcode.Lt => -1,
            BehaviorOpcode.Le => -1,
            BehaviorOpcode.Gt => -1,
            BehaviorOpcode.Ge => -1,
            BehaviorOpcode.And => -1,
            BehaviorOpcode.Or => -1,
            BehaviorOpcode.Min => -1,
            BehaviorOpcode.Max => -1,
            BehaviorOpcode.RandInt => -1,

            // Ternary ops (pop 3, push 1 = -2)
            BehaviorOpcode.Lerp => -2,
            BehaviorOpcode.Clamp => -2,

            // No stack change
            BehaviorOpcode.Swap => 0,
            BehaviorOpcode.Jmp => 0,
            BehaviorOpcode.JmpIf => -1, // pops condition
            BehaviorOpcode.JmpUnless => -1,
            BehaviorOpcode.Call => 0,
            BehaviorOpcode.Ret => 0,
            BehaviorOpcode.Halt => 0,
            BehaviorOpcode.Nop => 0,
            BehaviorOpcode.ContinuationPoint => 0,
            BehaviorOpcode.YieldToExtension => 0,

            // EmitIntent pops variable amounts - assume 3 for estimation
            BehaviorOpcode.EmitIntent => -3,

            _ => 0,
        };
    }

    /// <summary>
    /// Gets the size of operands for an opcode.
    /// </summary>
    private static int GetOperandSize(BehaviorOpcode opcode)
    {
        return opcode switch
        {
            // 1-byte operand
            BehaviorOpcode.PushConst => 1,
            BehaviorOpcode.PushInput => 1,
            BehaviorOpcode.PushLocal => 1,
            BehaviorOpcode.StoreLocal => 1,
            BehaviorOpcode.SetOutput => 1,
            BehaviorOpcode.EmitIntent => 1,
            BehaviorOpcode.SwitchJmp => 1, // followed by jump table

            // 2-byte operand (16-bit offset or index)
            BehaviorOpcode.Jmp => 2,
            BehaviorOpcode.JmpIf => 2,
            BehaviorOpcode.JmpUnless => 2,
            BehaviorOpcode.Call => 2,
            BehaviorOpcode.PushString => 2,
            BehaviorOpcode.ContinuationPoint => 2,
            BehaviorOpcode.YieldToExtension => 2,
            BehaviorOpcode.ExtensionAvailable => 2,
            BehaviorOpcode.Trace => 2,

            // No operand
            _ => 0,
        };
    }
}

/// <summary>
/// Debug information for a behavior model.
/// Maps bytecode offsets to source locations.
/// </summary>
public sealed class DebugInfo
{
    /// <summary>
    /// Source file path or identifier.
    /// </summary>
    public string SourcePath { get; }

    /// <summary>
    /// Line number mappings: bytecode offset -> source line.
    /// </summary>
    public IReadOnlyDictionary<int, int> LineMap { get; }

    /// <summary>
    /// Creates new debug info.
    /// </summary>
    public DebugInfo(string sourcePath, IReadOnlyDictionary<int, int> lineMap)
    {
        SourcePath = sourcePath ?? "";
        LineMap = lineMap ?? new Dictionary<int, int>();
    }

    /// <summary>
    /// Gets the source line for a bytecode offset.
    /// </summary>
    public int GetLine(int bytecodeOffset)
    {
        // Find the closest line mapping at or before this offset
        var bestLine = 0;
        var bestOffset = -1;

        foreach (var (offset, line) in LineMap)
        {
            if (offset <= bytecodeOffset && offset > bestOffset)
            {
                bestOffset = offset;
                bestLine = line;
            }
        }

        return bestLine;
    }

    /// <summary>
    /// Deserializes debug info from binary data.
    /// </summary>
    public static DebugInfo Deserialize(BinaryReader reader)
    {

        var pathLength = reader.ReadUInt16();
        var pathBytes = reader.ReadBytes(pathLength);
        var sourcePath = System.Text.Encoding.UTF8.GetString(pathBytes);

        var mappingCount = reader.ReadUInt16();
        var lineMap = new Dictionary<int, int>(mappingCount);
        for (var i = 0; i < mappingCount; i++)
        {
            var offset = reader.ReadInt32();
            var line = reader.ReadInt32();
            lineMap[offset] = line;
        }

        return new DebugInfo(sourcePath, lineMap);
    }

    /// <summary>
    /// Serializes debug info to binary data.
    /// </summary>
    public void Serialize(BinaryWriter writer)
    {

        var pathBytes = System.Text.Encoding.UTF8.GetBytes(SourcePath);
        writer.Write((ushort)pathBytes.Length);
        writer.Write(pathBytes);

        writer.Write((ushort)LineMap.Count);
        foreach (var (offset, line) in LineMap)
        {
            writer.Write(offset);
            writer.Write(line);
        }
    }
}
