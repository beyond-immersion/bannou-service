// ═══════════════════════════════════════════════════════════════════════════
// ABML Global Handler
// Executes global variable assignment actions (writes to root scope).
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Expressions;

namespace BeyondImmersion.BannouService.Abml.Execution.Handlers;

/// <summary>
/// Handles global variable actions (writes to document root scope).
/// </summary>
public sealed class GlobalHandler : IActionHandler
{
    /// <inheritdoc/>
    public bool CanHandle(ActionNode action) => action is GlobalAction;

    /// <inheritdoc/>
    public ValueTask<ActionResult> ExecuteAsync(
        ActionNode action, ExecutionContext context, CancellationToken ct)
    {
        var globalAction = (GlobalAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        // Parse and evaluate the value
        var value = ValueEvaluator.Evaluate(globalAction.Value, scope, context.Evaluator);

        // SetGlobalValue writes to root scope
        if (scope is VariableScope vs)
        {
            vs.SetGlobalValue(globalAction.Variable, value);
        }
        else
        {
            // Fallback: write to the execution context's root scope
            context.RootScope.SetValue(globalAction.Variable, value);
        }

        return ValueTask.FromResult(ActionResult.Continue);
    }
}
