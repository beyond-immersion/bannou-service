// =============================================================================
// Constant Pool Builder
// Manages constant pool with deduplication during compilation.
// =============================================================================

namespace BeyondImmersion.Bannou.Behavior.Compiler.Codegen;

/// <summary>
/// Builds a constant pool for behavior model compilation.
/// Handles deduplication of numeric literals.
/// </summary>
public sealed class ConstantPoolBuilder
{
    private readonly List<double> _constants = new(64);
    private readonly Dictionary<double, byte> _constantLookup = new(64);

    /// <summary>
    /// Number of constants in the pool.
    /// </summary>
    public int Count => _constants.Count;

    /// <summary>
    /// Maximum number of constants (byte index limit).
    /// </summary>
    public const int MaxConstants = 256;

    /// <summary>
    /// Adds or retrieves an existing constant from the pool.
    /// </summary>
    /// <param name="value">The constant value.</param>
    /// <returns>The constant pool index.</returns>
    /// <exception cref="InvalidOperationException">If constant pool is full.</exception>
    public byte GetOrAdd(double value)
    {
        // Check for existing constant (using epsilon for float comparison)
        if (_constantLookup.TryGetValue(value, out var existingIndex))
        {
            return existingIndex;
        }

        // Check for near-duplicate (floating point comparison)
        foreach (var (existing, index) in _constantLookup)
        {
            if (Math.Abs(existing - value) < 1e-15)
            {
                return index;
            }
        }

        if (_constants.Count >= MaxConstants)
        {
            throw new InvalidOperationException(
                $"Constant pool overflow. Maximum {MaxConstants} constants supported.");
        }

        var newIndex = (byte)_constants.Count;
        _constants.Add(value);
        _constantLookup[value] = newIndex;

        return newIndex;
    }

    /// <summary>
    /// Adds common constants (0, 1, -1) for optimization.
    /// </summary>
    public void AddCommonConstants()
    {
        GetOrAdd(0.0);
        GetOrAdd(1.0);
        GetOrAdd(-1.0);
    }

    /// <summary>
    /// Gets the constant value at the specified index.
    /// </summary>
    /// <param name="index">The constant pool index.</param>
    /// <returns>The constant value.</returns>
    public double GetValue(byte index)
    {
        return _constants[index];
    }

    /// <summary>
    /// Tries to get the constant value at the specified index.
    /// </summary>
    /// <param name="index">The constant pool index.</param>
    /// <param name="value">The constant value if found.</param>
    /// <returns>True if the index is valid.</returns>
    public bool TryGetValue(byte index, out double value)
    {
        if (index < _constants.Count)
        {
            value = _constants[index];
            return true;
        }
        value = 0.0;
        return false;
    }

    /// <summary>
    /// Gets the finalized constant pool.
    /// </summary>
    /// <returns>Read-only list of constants.</returns>
    public IReadOnlyList<double> ToList()
    {
        return _constants.ToArray();
    }

    /// <summary>
    /// Resets the constant pool to initial state.
    /// </summary>
    public void Reset()
    {
        _constants.Clear();
        _constantLookup.Clear();
    }
}
