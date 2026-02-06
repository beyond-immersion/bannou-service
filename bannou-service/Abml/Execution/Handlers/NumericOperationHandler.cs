// ═══════════════════════════════════════════════════════════════════════════
// ABML Numeric Operation Handler
// Executes numeric increment and decrement actions.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;

namespace BeyondImmersion.BannouService.Abml.Execution.Handlers;

/// <summary>
/// Handles numeric operations (increment and decrement actions).
/// </summary>
public sealed class NumericOperationHandler : IActionHandler
{
    /// <inheritdoc/>
    public bool CanHandle(ActionNode action) => action is IncrementAction or DecrementAction;

    /// <inheritdoc/>
    public ValueTask<ActionResult> ExecuteAsync(
        ActionNode action, ExecutionContext context, CancellationToken ct)
    {
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        string variable;
        int delta;

        switch (action)
        {
            case IncrementAction inc:
                variable = inc.Variable;
                delta = inc.By;
                break;
            case DecrementAction dec:
                variable = dec.Variable;
                delta = -dec.By;  // Negate for decrement
                break;
            default:
                return ValueTask.FromResult(ActionResult.Error("Invalid action type"));
        }

        // Get current value
        var current = scope.GetValue(variable);
        var currentValue = AbmlTypeCoercion.ToDouble(current);

        // Apply delta
        var newValue = currentValue + delta;

        // Store as int if it's a whole number, otherwise as double
        if (newValue == Math.Floor(newValue) && newValue is >= int.MinValue and <= int.MaxValue)
        {
            scope.SetValue(variable, (int)newValue);
        }
        else
        {
            scope.SetValue(variable, newValue);
        }

        return ValueTask.FromResult(ActionResult.Continue);
    }
}
