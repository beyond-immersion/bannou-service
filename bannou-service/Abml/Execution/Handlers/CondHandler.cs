// ═══════════════════════════════════════════════════════════════════════════
// ABML Cond Handler
// Executes conditional branching actions.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;

namespace BeyondImmersion.BannouService.Abml.Execution.Handlers;

/// <summary>
/// Handles conditional branching (cond) actions.
/// </summary>
public sealed class CondHandler : IActionHandler
{
    /// <inheritdoc/>
    public bool CanHandle(ActionNode action) => action is CondAction;

    /// <inheritdoc/>
    public async ValueTask<ActionResult> ExecuteAsync(
        ActionNode action, ExecutionContext context, CancellationToken ct)
    {
        var cond = (CondAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        // Evaluate branches in order
        foreach (var branch in cond.Branches)
        {
            ct.ThrowIfCancellationRequested();

            var condition = context.Evaluator.EvaluateCondition(branch.When, scope);
            if (condition)
            {
                // Execute 'then' actions
                return await ExecuteActionsAsync(branch.Then, context, ct);
            }
        }

        // Execute else branch if present
        if (cond.ElseBranch != null)
        {
            return await ExecuteActionsAsync(cond.ElseBranch, context, ct);
        }

        return ActionResult.Continue;
    }

    private static async ValueTask<ActionResult> ExecuteActionsAsync(
        IReadOnlyList<ActionNode> actions,
        ExecutionContext context,
        CancellationToken ct)
    {
        foreach (var nestedAction in actions)
        {
            ct.ThrowIfCancellationRequested();

            var handler = context.Handlers.GetHandler(nestedAction);
            if (handler == null)
            {
                return ActionResult.Error($"No handler for action type: {nestedAction.GetType().Name}");
            }

            var result = await handler.ExecuteAsync(nestedAction, context, ct);
            if (result is not ContinueResult)
            {
                return result;
            }
        }

        return ActionResult.Continue;
    }
}
