// ═══════════════════════════════════════════════════════════════════════════
// ABML Return Handler
// Executes return actions.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;

namespace BeyondImmersion.BannouService.Abml.Execution.Handlers;

/// <summary>
/// Handles return actions.
/// </summary>
public sealed class ReturnHandler : IActionHandler
{
    /// <inheritdoc/>
    public bool CanHandle(ActionNode action) => action is ReturnAction;

    /// <inheritdoc/>
    public ValueTask<ActionResult> ExecuteAsync(
        ActionNode action, ExecutionContext context, CancellationToken ct)
    {
        var returnAction = (ReturnAction)action;
        object? value = null;

        if (returnAction.Value != null)
        {
            var scope = context.CallStack.Current?.Scope ?? context.RootScope;
            value = ValueEvaluator.Evaluate(returnAction.Value, scope, context.Evaluator);
        }

        return ValueTask.FromResult(ActionResult.Return(value));
    }
}
