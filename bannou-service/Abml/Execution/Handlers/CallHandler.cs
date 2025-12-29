// ═══════════════════════════════════════════════════════════════════════════
// ABML Call Handler
// Executes flow call actions (subroutine with return).
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Documents.Actions;

namespace BeyondImmersion.BannouService.Abml.Execution.Handlers;

/// <summary>
/// Handles call actions (flow call with return).
/// </summary>
public sealed class CallHandler : IActionHandler
{
    /// <inheritdoc/>
    public bool CanHandle(ActionNode action) => action is CallAction;

    /// <inheritdoc/>
    public async ValueTask<ActionResult> ExecuteAsync(
        ActionNode action, ExecutionContext context, CancellationToken ct)
    {
        var callAction = (CallAction)action;
        var flowName = callAction.Flow;

        // Find the target flow
        if (!context.Document.Flows.TryGetValue(flowName, out var targetFlow))
        {
            return ActionResult.Error($"Flow not found: {flowName}");
        }

        // Use the current scope directly so called flow can modify caller's variables
        var currentScope = context.CallStack.Current?.Scope ?? context.RootScope;

        // Push new frame with the same scope (allows variable sharing)
        context.CallStack.Push(flowName, currentScope);

        try
        {
            // Execute the called flow
            foreach (var flowAction in targetFlow.Actions)
            {
                ct.ThrowIfCancellationRequested();

                var handler = context.Handlers.GetHandler(flowAction);
                if (handler == null)
                {
                    return ActionResult.Error($"No handler for action type: {flowAction.GetType().Name}");
                }

                var result = await handler.ExecuteAsync(flowAction, context, ct);

                switch (result)
                {
                    case ReturnResult returnResult:
                        // Return from called flow - store value and continue
                        if (returnResult.Value != null)
                        {
                            currentScope.SetValue("_result", returnResult.Value);
                        }
                        return ActionResult.Continue;

                    case GotoResult gotoResult:
                        // Goto replaces current call - pop and return the goto
                        return gotoResult;

                    case ErrorResult:
                    case CompleteResult:
                        return result;
                }
            }

            // Flow completed normally
            return ActionResult.Continue;
        }
        finally
        {
            // Pop the call frame
            context.CallStack.Pop();
        }
    }
}
