// ═══════════════════════════════════════════════════════════════════════════
// ABML Variable Provider Interface
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.Bannou.BehaviorExpressions.Expressions;

/// <summary>
/// Provides external state access for ABML expressions.
/// </summary>
public interface IVariableProvider
{
    /// <summary>
    /// Gets the name of this provider (e.g., "entity", "world").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets a value by navigating the given path from this provider's root.
    /// </summary>
    object? GetValue(ReadOnlySpan<string> path);

    /// <summary>
    /// Gets the root value of this provider.
    /// </summary>
    object? GetRootValue();

    /// <summary>
    /// Checks if this provider can resolve the given path.
    /// </summary>
    bool CanResolve(ReadOnlySpan<string> path);
}
