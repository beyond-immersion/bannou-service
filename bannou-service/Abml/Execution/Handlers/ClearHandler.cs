// ═══════════════════════════════════════════════════════════════════════════
// ABML Clear Handler
// Executes variable clearing actions.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Documents.Actions;

namespace BeyondImmersion.BannouService.Abml.Execution.Handlers;

/// <summary>
/// Handles clear variable actions.
/// </summary>
public sealed class ClearHandler : IActionHandler
{
    /// <inheritdoc/>
    public bool CanHandle(ActionNode action) => action is ClearAction;

    /// <inheritdoc/>
    public ValueTask<ActionResult> ExecuteAsync(
        ActionNode action, ExecutionContext context, CancellationToken ct)
    {
        var clearAction = (ClearAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        // Set variable to null (clear it)
        scope.SetValue(clearAction.Variable, null);

        return ValueTask.FromResult(ActionResult.Continue);
    }
}
