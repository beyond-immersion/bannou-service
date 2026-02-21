// ═══════════════════════════════════════════════════════════════════════════
// ABML Goto Handler
// Executes flow transfer actions (tail call).
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;

namespace BeyondImmersion.BannouService.Abml.Execution.Handlers;

/// <summary>
/// Handles goto actions (flow transfer without return).
/// </summary>
public sealed class GotoHandler : IActionHandler
{
    /// <inheritdoc/>
    public bool CanHandle(ActionNode action) => action is GotoAction;

    /// <inheritdoc/>
    public ValueTask<ActionResult> ExecuteAsync(
        ActionNode action, ExecutionContext context, CancellationToken ct)
    {
        var gotoAction = (GotoAction)action;

        // Convert string args to object args
        Dictionary<string, object?>? args = null;
        if (gotoAction.Args != null)
        {
            args = new Dictionary<string, object?>();
            var scope = context.CallStack.Current?.Scope ?? context.RootScope;

            foreach (var (key, valueExpr) in gotoAction.Args)
            {
                // Evaluate argument expression
                var value = context.Evaluator.Evaluate(valueExpr, scope);
                args[key] = value;
            }
        }

        return ValueTask.FromResult(ActionResult.Goto(gotoAction.Flow, args));
    }
}
