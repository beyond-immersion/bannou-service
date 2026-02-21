// ═══════════════════════════════════════════════════════════════════════════
// ABML Function Registry Implementation
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorExpressions.Exceptions;

namespace BeyondImmersion.Bannou.BehaviorExpressions.Functions;

/// <summary>
/// Registry for ABML expression functions.
/// </summary>
public sealed class FunctionRegistry : IFunctionRegistry
{
    private readonly Dictionary<string, FunctionEntry> _functions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a new function registry with optional built-in functions.
    /// </summary>
    /// <param name="includeBuiltins">Whether to include built-in functions.</param>
    public FunctionRegistry(bool includeBuiltins = true)
    {
        if (includeBuiltins)
        {
            BuiltinFunctions.RegisterAll(this);
        }
    }

    /// <inheritdoc/>
    public void Register(string name, AbmlFunction function, int minArgs = 0, int maxArgs = -1)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(function);

        if (minArgs < 0) throw new ArgumentOutOfRangeException(nameof(minArgs));
        if (maxArgs >= 0 && maxArgs < minArgs) throw new ArgumentException("maxArgs must be >= minArgs");

        _functions[name] = new FunctionEntry(name, function, minArgs, maxArgs);
    }

    /// <inheritdoc/>
    public FunctionEntry? GetFunction(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _functions.TryGetValue(name, out var entry) ? entry : null;
    }

    /// <inheritdoc/>
    public bool HasFunction(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _functions.ContainsKey(name);
    }

    /// <inheritdoc/>
    public object? Invoke(string name, object?[] args)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(args);

        var entry = GetFunction(name) ?? throw new AbmlRuntimeException($"Unknown function: {name}");
        if (!entry.ValidateArgCount(args.Length))
        {
            var expected = entry.MaxArgs < 0
                ? $"at least {entry.MinArgs}"
                : entry.MinArgs == entry.MaxArgs
                    ? $"exactly {entry.MinArgs}"
                    : $"{entry.MinArgs}-{entry.MaxArgs}";
            throw new AbmlRuntimeException($"Function '{name}' expects {expected} arguments, got {args.Length}");
        }

        return entry.Function(args);
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetFunctionNames() => _functions.Keys.OrderBy(k => k);

    /// <summary>
    /// Creates a new function registry with built-in functions.
    /// </summary>
    /// <returns>A new function registry with built-in functions.</returns>
    public static FunctionRegistry CreateWithBuiltins() => new(includeBuiltins: true);

    /// <summary>
    /// Creates a new empty function registry.
    /// </summary>
    /// <returns>A new empty function registry.</returns>
    public static FunctionRegistry CreateEmpty() => new(includeBuiltins: false);
}
