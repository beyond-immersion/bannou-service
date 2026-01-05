// ═══════════════════════════════════════════════════════════════════════════
// ABML Set Handler
// Executes variable assignment actions.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Documents.Actions;

namespace BeyondImmersion.BannouService.Abml.Execution.Handlers;

/// <summary>
/// Handles set variable actions.
/// </summary>
public sealed class SetHandler : IActionHandler
{
    /// <inheritdoc/>
    public bool CanHandle(ActionNode action) => action is SetAction;

    /// <inheritdoc/>
    public ValueTask<ActionResult> ExecuteAsync(
        ActionNode action, ExecutionContext context, CancellationToken ct)
    {
        var setAction = (SetAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        // Parse and evaluate the value using shared utility
        var value = ValueEvaluator.Evaluate(setAction.Value, scope, context.Evaluator);

        // Set variable in current scope
        scope.SetValue(setAction.Variable, value);

        return ValueTask.FromResult(ActionResult.Continue);
    }
}
