// ═══════════════════════════════════════════════════════════════════════════
// ABML Repeat Handler
// Executes bounded repetition actions.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Documents.Actions;

namespace BeyondImmersion.BannouService.Abml.Execution.Handlers;

/// <summary>
/// Handles repeat actions.
/// </summary>
public sealed class RepeatHandler : IActionHandler
{
    /// <inheritdoc/>
    public bool CanHandle(ActionNode action) => action is RepeatAction;

    /// <inheritdoc/>
    public async ValueTask<ActionResult> ExecuteAsync(
        ActionNode action, ExecutionContext context, CancellationToken ct)
    {
        var repeat = (RepeatAction)action;

        for (var i = 0; i < repeat.Times; i++)
        {
            ct.ThrowIfCancellationRequested();

            // Execute loop body
            foreach (var nestedAction in repeat.Do)
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
        }

        return ActionResult.Continue;
    }
}
