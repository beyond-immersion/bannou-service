// ═══════════════════════════════════════════════════════════════════════════
// ABML Log Handler
// Executes log actions for debugging and testing.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using System.Text.RegularExpressions;

namespace BeyondImmersion.BannouService.Abml.Execution.Handlers;

/// <summary>
/// Handles log actions.
/// </summary>
public sealed partial class LogHandler : IActionHandler
{
    /// <inheritdoc/>
    public bool CanHandle(ActionNode action) => action is LogAction;

    /// <inheritdoc/>
    public ValueTask<ActionResult> ExecuteAsync(
        ActionNode action, ExecutionContext context, CancellationToken ct)
    {
        var logAction = (LogAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        // Interpolate message (replace ${...} with evaluated values)
        var message = InterpolateMessage(logAction.Message, scope, context);

        // Add to execution log
        context.Logs.Add(new LogEntry(logAction.Level, message, DateTime.UtcNow));

        return ValueTask.FromResult(ActionResult.Continue);
    }

    private static string InterpolateMessage(string message, Expressions.IVariableScope scope, ExecutionContext context)
    {
        // Replace ${...} expressions with evaluated values
        return ExpressionPattern().Replace(message, match =>
        {
            var expression = match.Groups[1].Value;
            try
            {
                var value = context.Evaluator.Evaluate($"${{{expression}}}", scope);
                return value?.ToString() ?? "null";
            }
            catch
            {
                return match.Value; // Keep original on error
            }
        });
    }

    [GeneratedRegex(@"\$\{([^}]+)\}")]
    private static partial Regex ExpressionPattern();
}
