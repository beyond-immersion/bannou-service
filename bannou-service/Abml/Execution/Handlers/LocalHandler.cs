// ═══════════════════════════════════════════════════════════════════════════
// ABML Local Handler
// Executes local variable assignment actions (shadows parent scope).
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Expressions;

namespace BeyondImmersion.BannouService.Abml.Execution.Handlers;

/// <summary>
/// Handles local variable actions (creates in current scope, shadows parent).
/// </summary>
public sealed class LocalHandler : IActionHandler
{
    /// <inheritdoc/>
    public bool CanHandle(ActionNode action) => action is LocalAction;

    /// <inheritdoc/>
    public ValueTask<ActionResult> ExecuteAsync(
        ActionNode action, ExecutionContext context, CancellationToken ct)
    {
        var localAction = (LocalAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        // Parse and evaluate the value
        var value = ValueEvaluator.Evaluate(localAction.Value, scope, context.Evaluator);

        // SetLocalValue creates in current scope, shadows parent
        if (scope is VariableScope vs)
        {
            vs.SetLocalValue(localAction.Variable, value);
        }
        else
        {
            // Fallback for non-VariableScope implementations
            scope.SetValue(localAction.Variable, value);
        }

        return ValueTask.FromResult(ActionResult.Continue);
    }
}
