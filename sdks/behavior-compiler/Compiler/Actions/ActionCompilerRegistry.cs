// =============================================================================
// Action Compiler Registry
// Manages and dispatches to action-specific compilers.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Compiler.Expressions;
using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;

namespace BeyondImmersion.Bannou.BehaviorCompiler.Compiler.Actions;

/// <summary>
/// Registry of action compilers. Dispatches compilation to appropriate handlers.
/// </summary>
public sealed class ActionCompilerRegistry
{
    private readonly List<IActionCompiler> _compilers = new();
    private readonly StackExpressionCompiler _expressionCompiler;

    /// <summary>
    /// Creates a new registry with default compilers.
    /// </summary>
    /// <param name="context">The compilation context.</param>
    public ActionCompilerRegistry(CompilationContext context)
    {

        _expressionCompiler = new StackExpressionCompiler(context);

        // Register all action compilers
        RegisterDefaults(context);
    }

    private void RegisterDefaults(CompilationContext context)
    {
        // Control flow
        Register(new CondCompiler(this, _expressionCompiler));
        Register(new ForEachCompiler(this, _expressionCompiler));
        Register(new RepeatCompiler(this));
        Register(new GotoCompiler());
        Register(new CallCompiler());
        Register(new ReturnCompiler(_expressionCompiler));

        // Variables
        Register(new SetCompiler(_expressionCompiler));
        Register(new LocalCompiler(_expressionCompiler));
        Register(new GlobalCompiler(_expressionCompiler));
        Register(new IncrementCompiler());
        Register(new DecrementCompiler());
        Register(new ClearCompiler());

        // Output
        Register(new LogCompiler(_expressionCompiler));
        Register(new EmitIntentCompiler(_expressionCompiler));

        // Continuation points
        Register(new ContinuationPointCompiler(_expressionCompiler));

        // Watch/unwatch (runtime-handled by Puppetmaster)
        Register(new WatchCompiler(_expressionCompiler));
        Register(new UnwatchCompiler());

        // Domain actions (catch-all)
        Register(new DomainActionCompiler(_expressionCompiler));
    }

    /// <summary>
    /// Registers an action compiler.
    /// </summary>
    /// <param name="compiler">The compiler to register.</param>
    public void Register(IActionCompiler compiler)
    {
        _compilers.Add(compiler);
    }

    /// <summary>
    /// Compiles an action using the appropriate compiler.
    /// </summary>
    /// <param name="action">The action to compile.</param>
    /// <param name="context">The compilation context.</param>
    public void Compile(ActionNode action, CompilationContext context)
    {

        foreach (var compiler in _compilers)
        {
            if (compiler.CanCompile(action))
            {
                compiler.Compile(action, context);
                return;
            }
        }

        context.AddError($"No compiler found for action type: {action.GetType().Name}");
    }

    /// <summary>
    /// Compiles a list of actions.
    /// </summary>
    /// <param name="actions">The actions to compile.</param>
    /// <param name="context">The compilation context.</param>
    public void CompileActions(IReadOnlyList<ActionNode> actions, CompilationContext context)
    {
        foreach (var action in actions)
        {
            Compile(action, context);
        }
    }
}
