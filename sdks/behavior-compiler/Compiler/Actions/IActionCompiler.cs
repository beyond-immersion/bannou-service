// =============================================================================
// Action Compiler Interface
// Defines the contract for action-specific compilers.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;

namespace BeyondImmersion.Bannou.BehaviorCompiler.Compiler.Actions;

/// <summary>
/// Interface for action-specific compilers.
/// Each action type has a corresponding compiler implementation.
/// </summary>
public interface IActionCompiler
{
    /// <summary>
    /// Checks if this compiler can handle the given action.
    /// </summary>
    /// <param name="action">The action node.</param>
    /// <returns>True if this compiler can handle the action.</returns>
    bool CanCompile(ActionNode action);

    /// <summary>
    /// Compiles the action to bytecode.
    /// </summary>
    /// <param name="action">The action node to compile.</param>
    /// <param name="context">The compilation context.</param>
    void Compile(ActionNode action, CompilationContext context);
}

/// <summary>
/// Base class for typed action compilers.
/// </summary>
/// <typeparam name="T">The action node type.</typeparam>
public abstract class ActionCompilerBase<T> : IActionCompiler where T : ActionNode
{
    /// <inheritdoc/>
    public bool CanCompile(ActionNode action) => action is T;

    /// <inheritdoc/>
    public void Compile(ActionNode action, CompilationContext context)
    {
        if (action is not T typed)
        {
            throw new InvalidOperationException($"Expected {typeof(T).Name}, got {action.GetType().Name}");
        }
        CompileTyped(typed, context);
    }

    /// <summary>
    /// Compiles the typed action to bytecode.
    /// </summary>
    /// <param name="action">The typed action node.</param>
    /// <param name="context">The compilation context.</param>
    protected abstract void CompileTyped(T action, CompilationContext context);
}
