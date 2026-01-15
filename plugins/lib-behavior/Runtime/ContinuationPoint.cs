// =============================================================================
// Continuation Point
// Defines pause points where extensions can attach during execution.
// =============================================================================

namespace BeyondImmersion.BannouService.Behavior.Runtime;

/// <summary>
/// Defines a point in execution where the interpreter pauses for possible extension attachment.
/// Used for streaming composition in cinematics.
/// </summary>
/// <remarks>
/// <para>
/// Continuation points enable runtime extension of executing behaviors. A game server
/// receives a complete, executable cinematic, starts executing immediately, and can
/// optionally receive extensions that attach to named points.
/// </para>
/// <para>
/// Execution flow:
/// 1. Interpreter reaches CONTINUATION_POINT opcode
/// 2. Checks if extension is attached at this point
/// 3. If extension available: transfers control to extension
/// 4. If no extension: waits up to timeout, then executes default flow
/// </para>
/// </remarks>
public readonly struct ContinuationPoint
{
    /// <summary>
    /// FNV-1a hash of the continuation point name for fast lookup.
    /// </summary>
    public uint NameHash { get; init; }

    /// <summary>
    /// Index into string table for continuation point name (for debugging).
    /// </summary>
    public int NameStringIndex { get; init; }

    /// <summary>
    /// Maximum time to wait for extension attachment (milliseconds).
    /// If 0, no wait - immediately use default if no extension.
    /// </summary>
    public uint TimeoutMs { get; init; }

    /// <summary>
    /// Bytecode offset of the default flow to execute if no extension arrives.
    /// </summary>
    public uint DefaultFlowOffset { get; init; }

    /// <summary>
    /// Bytecode offset where the CONTINUATION_POINT opcode is located.
    /// </summary>
    public uint BytecodeOffset { get; init; }

    /// <summary>
    /// Creates a new continuation point.
    /// </summary>
    public ContinuationPoint(
        uint nameHash,
        int nameStringIndex,
        uint timeoutMs,
        uint defaultFlowOffset,
        uint bytecodeOffset)
    {
        NameHash = nameHash;
        NameStringIndex = nameStringIndex;
        TimeoutMs = timeoutMs;
        DefaultFlowOffset = defaultFlowOffset;
        BytecodeOffset = bytecodeOffset;
    }

    /// <summary>
    /// Deserializes a continuation point from binary data.
    /// </summary>
    public static ContinuationPoint Deserialize(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var nameHash = reader.ReadUInt32();
        var nameStringIndex = reader.ReadInt32();
        var timeoutMs = reader.ReadUInt32();
        var defaultFlowOffset = reader.ReadUInt32();
        var bytecodeOffset = reader.ReadUInt32();

        return new ContinuationPoint(nameHash, nameStringIndex, timeoutMs, defaultFlowOffset, bytecodeOffset);
    }

    /// <summary>
    /// Serializes the continuation point to binary data.
    /// </summary>
    public void Serialize(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.Write(NameHash);
        writer.Write(NameStringIndex);
        writer.Write(TimeoutMs);
        writer.Write(DefaultFlowOffset);
        writer.Write(BytecodeOffset);
    }

    /// <summary>
    /// Gets the timeout as a TimeSpan.
    /// </summary>
    public TimeSpan Timeout => TimeSpan.FromMilliseconds(TimeoutMs);

    /// <summary>
    /// Creates a continuation point from a name.
    /// </summary>
    public static ContinuationPoint Create(
        string name,
        int nameStringIndex,
        TimeSpan timeout,
        uint defaultFlowOffset,
        uint bytecodeOffset)
    {
        return new ContinuationPoint(
            VariableDefinition.HashName(name),
            nameStringIndex,
            (uint)timeout.TotalMilliseconds,
            defaultFlowOffset,
            bytecodeOffset);
    }
}

/// <summary>
/// Table of continuation points in a behavior model.
/// </summary>
public sealed class ContinuationPointTable
{
    /// <summary>
    /// All continuation points in the model.
    /// </summary>
    public IReadOnlyList<ContinuationPoint> Points { get; }

    private readonly Dictionary<uint, int> _hashToIndex;

    /// <summary>
    /// Number of continuation points.
    /// </summary>
    public int Count => Points.Count;

    /// <summary>
    /// Creates a new continuation point table.
    /// </summary>
    public ContinuationPointTable(IReadOnlyList<ContinuationPoint> points)
    {
        Points = points;

        _hashToIndex = new Dictionary<uint, int>(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            _hashToIndex[points[i].NameHash] = i;
        }
    }

    /// <summary>
    /// Gets a continuation point by index.
    /// </summary>
    public ContinuationPoint this[int index] => Points[index];

    /// <summary>
    /// Tries to find a continuation point by name hash.
    /// </summary>
    public bool TryGetByHash(uint nameHash, out ContinuationPoint point)
    {
        if (_hashToIndex.TryGetValue(nameHash, out var index))
        {
            point = Points[index];
            return true;
        }

        point = default;
        return false;
    }

    /// <summary>
    /// Tries to find a continuation point by name.
    /// </summary>
    public bool TryGetByName(string name, out ContinuationPoint point)
    {
        var hash = VariableDefinition.HashName(name);
        return TryGetByHash(hash, out point);
    }

    /// <summary>
    /// Empty table with no continuation points.
    /// </summary>
    public static ContinuationPointTable Empty { get; } = new(Array.Empty<ContinuationPoint>());

    /// <summary>
    /// Deserializes a continuation point table from binary data.
    /// </summary>
    public static ContinuationPointTable Deserialize(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var count = reader.ReadUInt16();
        var points = new ContinuationPoint[count];
        for (var i = 0; i < count; i++)
        {
            points[i] = ContinuationPoint.Deserialize(reader);
        }

        return new ContinuationPointTable(points);
    }

    /// <summary>
    /// Serializes the continuation point table to binary data.
    /// </summary>
    public void Serialize(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.Write((ushort)Points.Count);
        foreach (var point in Points)
        {
            point.Serialize(writer);
        }
    }
}
