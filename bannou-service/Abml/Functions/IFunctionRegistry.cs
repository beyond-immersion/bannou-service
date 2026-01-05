// ═══════════════════════════════════════════════════════════════════════════
// ABML Function Registry Interface
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.BannouService.Abml.Functions;

/// <summary>
/// Delegate for ABML function implementations.
/// </summary>
/// <param name="args">The function arguments.</param>
/// <returns>The function result.</returns>
public delegate object? AbmlFunction(object?[] args);

/// <summary>
/// Registry for ABML expression functions.
/// </summary>
public interface IFunctionRegistry
{
    /// <summary>
    /// Registers a function.
    /// </summary>
    /// <param name="name">The function name.</param>
    /// <param name="function">The function implementation.</param>
    /// <param name="minArgs">Minimum argument count.</param>
    /// <param name="maxArgs">Maximum argument count (-1 for unlimited).</param>
    void Register(string name, AbmlFunction function, int minArgs = 0, int maxArgs = -1);

    /// <summary>
    /// Gets a registered function by name.
    /// </summary>
    /// <param name="name">The function name.</param>
    /// <returns>The function entry, or null if not found.</returns>
    FunctionEntry? GetFunction(string name);

    /// <summary>
    /// Checks if a function is registered.
    /// </summary>
    /// <param name="name">The function name.</param>
    /// <returns>True if registered, false otherwise.</returns>
    bool HasFunction(string name);

    /// <summary>
    /// Invokes a function by name.
    /// </summary>
    /// <param name="name">The function name.</param>
    /// <param name="args">The function arguments.</param>
    /// <returns>The function result.</returns>
    object? Invoke(string name, object?[] args);

    /// <summary>
    /// Gets all registered function names.
    /// </summary>
    /// <returns>Collection of function names.</returns>
    IEnumerable<string> GetFunctionNames();
}

/// <summary>
/// Entry for a registered function.
/// </summary>
public sealed class FunctionEntry
{
    /// <summary>Gets the function name.</summary>
    public string Name { get; }

    /// <summary>Gets the function implementation.</summary>
    public AbmlFunction Function { get; }

    /// <summary>Gets the minimum argument count.</summary>
    public int MinArgs { get; }

    /// <summary>Gets the maximum argument count (-1 for unlimited).</summary>
    public int MaxArgs { get; }

    /// <summary>Creates a new function entry.</summary>
    public FunctionEntry(string name, AbmlFunction function, int minArgs, int maxArgs)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Function = function ?? throw new ArgumentNullException(nameof(function));
        MinArgs = minArgs;
        MaxArgs = maxArgs;
    }

    /// <summary>Validates argument count.</summary>
    public bool ValidateArgCount(int count)
    {
        if (count < MinArgs) return false;
        if (MaxArgs >= 0 && count > MaxArgs) return false;
        return true;
    }
}
