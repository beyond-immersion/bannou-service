// =============================================================================
// String Table Builder
// Manages string table with interning during compilation.
// =============================================================================

using BeyondImmersion.BannouService.Abml.Compiler;

namespace BeyondImmersion.Bannou.Behavior.Compiler.Codegen;

/// <summary>
/// Builds a string table for behavior model compilation.
/// Handles string interning for deduplication.
/// </summary>
public sealed class StringTableBuilder
{
    private readonly List<string> _strings = new(64);
    private readonly Dictionary<string, ushort> _stringLookup = new(64, StringComparer.Ordinal);

    /// <summary>
    /// Number of strings in the table.
    /// </summary>
    public int Count => _strings.Count;

    /// <summary>
    /// Maximum number of strings (16-bit index limit).
    /// </summary>
    /// <remarks>
    /// References <see cref="VmConfig.MaxStrings"/> as the authoritative value.
    /// </remarks>
    public static int MaxStrings => VmConfig.MaxStrings;

    /// <summary>
    /// Adds or retrieves an existing string from the table.
    /// </summary>
    /// <param name="value">The string value.</param>
    /// <returns>The string table index.</returns>
    /// <exception cref="InvalidOperationException">If string table is full.</exception>
    public ushort GetOrAdd(string value)
    {

        if (_stringLookup.TryGetValue(value, out var existingIndex))
        {
            return existingIndex;
        }

        if (_strings.Count >= MaxStrings)
        {
            throw new InvalidOperationException(
                $"String table overflow. Maximum {MaxStrings} strings supported.");
        }

        var newIndex = (ushort)_strings.Count;
        _strings.Add(value);
        _stringLookup[value] = newIndex;

        return newIndex;
    }

    /// <summary>
    /// Gets the string value at the specified index.
    /// </summary>
    /// <param name="index">The string table index.</param>
    /// <returns>The string value.</returns>
    public string GetValue(ushort index)
    {
        return _strings[index];
    }

    /// <summary>
    /// Checks if a string exists in the table.
    /// </summary>
    /// <param name="value">The string to check.</param>
    /// <param name="index">The index if found.</param>
    /// <returns>True if the string exists.</returns>
    public bool TryGet(string value, out ushort index)
    {
        return _stringLookup.TryGetValue(value, out index);
    }

    /// <summary>
    /// Gets the finalized string table.
    /// </summary>
    /// <returns>Read-only list of strings.</returns>
    public IReadOnlyList<string> ToList()
    {
        return _strings.ToArray();
    }

    /// <summary>
    /// Resets the string table to initial state.
    /// </summary>
    public void Reset()
    {
        _strings.Clear();
        _stringLookup.Clear();
    }
}
