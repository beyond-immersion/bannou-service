// ═══════════════════════════════════════════════════════════════════════════
// ABML ForEach Handler
// Executes collection iteration actions.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Expressions;
using System.Collections;

namespace BeyondImmersion.BannouService.Abml.Execution.Handlers;

/// <summary>
/// Handles for_each iteration actions.
/// </summary>
public sealed class ForEachHandler : IActionHandler
{
    /// <inheritdoc/>
    public bool CanHandle(ActionNode action) => action is ForEachAction;

    /// <inheritdoc/>
    public async ValueTask<ActionResult> ExecuteAsync(
        ActionNode action, ExecutionContext context, CancellationToken ct)
    {
        var forEach = (ForEachAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        // Evaluate collection expression
        var collection = context.Evaluator.Evaluate(forEach.Collection, scope);

        if (collection == null)
        {
            // Null collection = no iterations
            return ActionResult.Continue;
        }

        if (collection is not IEnumerable enumerable)
        {
            return ActionResult.Error($"Cannot iterate over {collection.GetType().Name}");
        }

        var currentFlowName = context.CallStack.Current?.FlowName ?? "anonymous";

        foreach (var item in enumerable)
        {
            ct.ThrowIfCancellationRequested();

            // Create child scope with loop variable
            // Child scope inherits from parent, so variable reads work
            // Variable writes to existing variables propagate to parent via VariableScope.SetValue
            var loopScope = scope.CreateChild();
            loopScope.SetValue(forEach.Variable, item);

            // Push a new frame for the loop iteration
            // This is safer than swapping the scope of the existing frame
            context.CallStack.Push($"{currentFlowName}:foreach", loopScope);

            try
            {
                // Execute loop body
                var result = await ExecuteLoopBodyAsync(forEach.Do, context, ct);

                if (result is not ContinueResult)
                {
                    return result;
                }
            }
            finally
            {
                // Pop the loop iteration frame
                context.CallStack.Pop();
            }
        }

        return ActionResult.Continue;
    }

    private static async ValueTask<ActionResult> ExecuteLoopBodyAsync(
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
