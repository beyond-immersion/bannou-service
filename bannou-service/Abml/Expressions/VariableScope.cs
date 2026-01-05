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
        ValidateName(name);

        // If the variable exists in an ancestor scope, update it there
        // This allows explicit modification of outer scope variables
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
    /// Sets a variable in this scope only, shadowing any parent variable.
    /// Used by loop variables and explicit 'local:' actions.
    /// </summary>
    public void SetLocalValue(string name, object? value)
    {
        ValidateName(name);
        _variables[name] = value;  // Always local, ignores parent
    }

    /// <summary>
    /// Sets a variable in the root scope.
    /// Used by explicit 'global:' actions.
    /// </summary>
    public void SetGlobalValue(string name, object? value)
    {
        ValidateName(name);
        GetRootScope()._variables[name] = value;
    }

    /// <summary>
    /// Gets the root scope (topmost parent).
    /// </summary>
    private VariableScope GetRootScope()
    {
        var current = this;
        while (current.Parent is VariableScope parent)
            current = parent;
        return current;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Variable name cannot be null or empty", nameof(name));
        if (name.Contains('.'))
            throw new ArgumentException("Variable name cannot contain dots", nameof(name));
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
