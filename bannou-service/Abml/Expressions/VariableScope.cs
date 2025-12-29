// ═══════════════════════════════════════════════════════════════════════════
// ABML Variable Scope Implementation
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.BannouService.Abml.Expressions;

/// <summary>
/// Concrete implementation of <see cref="IVariableScope"/>.
/// </summary>
public sealed class VariableScope : IVariableScope
{
    private readonly Dictionary<string, object?> _variables = new();
    private readonly Dictionary<string, IVariableProvider> _providers = new();

    /// <inheritdoc/>
    public IVariableScope? Parent { get; }

    /// <summary>
    /// Creates a new root scope.
    /// </summary>
    public VariableScope() => Parent = null;

    private VariableScope(IVariableScope parent) => Parent = parent;

    /// <summary>
    /// Registers a variable provider.
    /// </summary>
    public void RegisterProvider(IVariableProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _providers[provider.Name] = provider;
    }

    /// <inheritdoc/>
    public object? GetValue(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        var segments = path.Split('.');
        var rootName = segments[0];

        if (_variables.TryGetValue(rootName, out var localValue))
        {
            return segments.Length == 1 ? localValue : NavigatePath(localValue, segments.AsSpan(1));
        }

        if (_providers.TryGetValue(rootName, out var provider))
        {
            return segments.Length == 1 ? provider.GetRootValue() : provider.GetValue(segments.AsSpan(1));
        }

        return Parent?.GetValue(path);
    }

    /// <inheritdoc/>
    public void SetValue(string name, object? value)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Variable name cannot be null or empty", nameof(name));
        if (name.Contains('.'))
            throw new ArgumentException("SetValue only accepts simple variable names", nameof(name));

        // If the variable exists in an ancestor scope, update it there
        // This allows loop bodies and called flows to modify outer variables
        var targetScope = FindScopeWithVariable(name);
        if (targetScope is VariableScope vs)
        {
            vs._variables[name] = value;
        }
        else
        {
            // Variable doesn't exist anywhere, create it in local scope
            _variables[name] = value;
        }
    }

    /// <summary>
    /// Finds the scope that contains the given variable.
    /// </summary>
    private VariableScope? FindScopeWithVariable(string name)
    {
        if (_variables.ContainsKey(name))
        {
            return this;
        }

        return Parent is VariableScope parentScope
            ? parentScope.FindScopeWithVariable(name)
            : null;
    }

    /// <inheritdoc/>
    public IVariableScope CreateChild() => new VariableScope(this);

    /// <inheritdoc/>
    public bool HasVariable(string name) =>
        _variables.ContainsKey(name) || _providers.ContainsKey(name) || (Parent?.HasVariable(name) ?? false);

    /// <summary>
    /// Sets multiple variables at once.
    /// </summary>
    public void SetVariables(IEnumerable<KeyValuePair<string, object?>> variables)
    {
        foreach (var (name, value) in variables)
            SetValue(name, value);
    }

    /// <summary>
    /// Clears all local variables.
    /// </summary>
    public void ClearVariables() => _variables.Clear();

    /// <summary>
    /// Gets all local variable names.
    /// </summary>
    public IEnumerable<string> GetLocalVariableNames() => _variables.Keys;

    /// <summary>
    /// Gets all provider names.
    /// </summary>
    public IEnumerable<string> GetProviderNames() => _providers.Keys;

    /// <summary>
    /// Gets all local variables as key-value pairs.
    /// </summary>
    public IEnumerable<KeyValuePair<string, object?>> GetLocalVariables() => _variables;

    private static object? NavigatePath(object? value, ReadOnlySpan<string> path)
    {
        var current = value;
        foreach (var segment in path)
        {
            if (current is null) return null;
            current = AbmlTypeCoercion.GetProperty(current, segment);
        }
        return current;
    }
}
