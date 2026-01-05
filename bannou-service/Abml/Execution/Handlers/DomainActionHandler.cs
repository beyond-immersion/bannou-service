// ═══════════════════════════════════════════════════════════════════════════
// ABML Domain Action Handler
// Placeholder handler for domain-specific actions.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Documents.Actions;

namespace BeyondImmersion.BannouService.Abml.Execution.Handlers;

/// <summary>
/// Handles domain-specific actions.
/// In a full implementation, this would dispatch to registered domain handlers.
/// For now, it logs the action for testing purposes.
/// </summary>
public sealed class DomainActionHandler : IActionHandler
{
    private readonly Func<string, IReadOnlyDictionary<string, object?>, ValueTask<ActionResult>>? _callback;

    /// <summary>
    /// Creates a domain action handler.
    /// </summary>
    /// <param name="callback">Optional callback for handling domain actions.</param>
    public DomainActionHandler(Func<string, IReadOnlyDictionary<string, object?>, ValueTask<ActionResult>>? callback = null)
    {
        _callback = callback;
    }

    /// <inheritdoc/>
    public bool CanHandle(ActionNode action) => action is DomainAction;

    /// <inheritdoc/>
    public async ValueTask<ActionResult> ExecuteAsync(
        ActionNode action, ExecutionContext context, CancellationToken ct)
    {
        var domainAction = (DomainAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        // Evaluate expressions in parameters
        var evaluatedParams = ValueEvaluator.EvaluateParameters(
            domainAction.Parameters, scope, context.Evaluator);

        // If there's a callback, use it with evaluated parameters
        if (_callback != null)
        {
            return await _callback(domainAction.Name, evaluatedParams);
        }

        // Default behavior: log and continue
        var paramsStr = string.Join(", ", evaluatedParams.Select(kv => $"{kv.Key}={kv.Value}"));
        context.Logs.Add(new LogEntry("domain", $"{domainAction.Name}({paramsStr})", DateTime.UtcNow));

        return ActionResult.Continue;
    }
}
