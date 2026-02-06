// ═══════════════════════════════════════════════════════════════════════════
// ABML Variable Scope Interface
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.Bannou.BehaviorExpressions.Expressions;

/// <summary>
/// Provides variable storage and resolution for ABML expression evaluation.
/// </summary>
public interface IVariableScope
{
    /// <summary>
    /// Gets a variable value by name or dotted path.
    /// </summary>
    object? GetValue(string path);

    /// <summary>
    /// Sets a variable value in the current scope.
    /// </summary>
    void SetValue(string name, object? value);

    /// <summary>
    /// Creates a child scope with this scope as the parent.
    /// </summary>
    IVariableScope CreateChild();

    /// <summary>
    /// Checks if a variable exists in this scope or any parent scope.
    /// </summary>
    bool HasVariable(string name);

    /// <summary>
    /// Gets the parent scope, if any.
    /// </summary>
    IVariableScope? Parent { get; }
}
