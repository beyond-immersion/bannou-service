// ═══════════════════════════════════════════════════════════════════════════
// ABML Constant Pool
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorCompiler.Compiler;

namespace BeyondImmersion.BannouService.Abml.Compiler;

/// <summary>
/// Builds the constant pool for a compiled expression.
/// </summary>
public sealed class ConstantPool
{
    private readonly List<object> _constants = new();
    private readonly Dictionary<object, byte> _indexMap = new();

    /// <summary>Gets the current number of constants.</summary>
    public int Count => _constants.Count;

    /// <summary>Adds a constant and returns its index.</summary>
    public byte Add(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (_indexMap.TryGetValue(value, out var existingIndex)) return existingIndex;
        if (_constants.Count >= VmConfig.MaxConstants)
            throw new InvalidOperationException($"Constant pool limit of {VmConfig.MaxConstants} exceeded");
        var index = (byte)_constants.Count;
        _constants.Add(value);
        _indexMap[value] = index;
        return index;
    }

    /// <summary>Adds a string constant.</summary>
    public byte AddString(string value) => Add(value);

    /// <summary>Adds an integer constant.</summary>
    public byte AddInt(int value) => Add(value);

    /// <summary>Adds a double constant.</summary>
    public byte AddDouble(double value) => Add(value);

    /// <summary>Gets a constant by index.</summary>
    public object Get(int index) => _constants[index];

    /// <summary>Checks if pool contains a constant.</summary>
    public bool Contains(object value) => _indexMap.ContainsKey(value);

    /// <summary>Tries to get index of existing constant.</summary>
    public bool TryGetIndex(object value, out byte index) => _indexMap.TryGetValue(value, out index);

    /// <summary>Converts to array.</summary>
    public object[] ToArray() => _constants.ToArray();

    /// <summary>Clears the pool.</summary>
    public void Clear()
    {
        _constants.Clear();
        _indexMap.Clear();
    }
}
