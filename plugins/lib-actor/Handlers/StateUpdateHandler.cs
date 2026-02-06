// =============================================================================
// State Update Handler
// ABML action handler for updating actor state (Event Brain support).
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Abml.Execution;
using Microsoft.Extensions.Logging;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;

namespace BeyondImmersion.BannouService.Actor.Handlers;

/// <summary>
/// ABML action handler for updating actor state.
/// Enables Event Brain to maintain encounter state, cooldowns, and cached data.
/// </summary>
/// <remarks>
/// <para>
/// ABML usage:
/// <code>
/// - state_update:
///     path: "memories.active_encounters"
///     operation: "append"
///     value: "${encounter}"
///
/// - state_update:
///     path: "memories.character_cooldowns.${character_id}"
///     operation: "set"
///     value: "${now()}"
/// </code>
/// </para>
/// <para>
/// Operations:
/// - set: Replace the value at path
/// - append: Add value to array at path
/// - increment: Increment numeric value
/// - decrement: Decrement numeric value
/// </para>
/// </remarks>
public sealed class StateUpdateHandler : IActionHandler
{
    private const string ACTION_NAME = "state_update";
    private readonly ILogger<StateUpdateHandler> _logger;

    /// <summary>
    /// Creates a new state update handler.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public StateUpdateHandler(ILogger<StateUpdateHandler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool CanHandle(ActionNode action)
        => action is DomainAction da && da.Name == ACTION_NAME;

    /// <inheritdoc/>
    public ValueTask<ActionResult> ExecuteAsync(
        ActionNode action,
        AbmlExecutionContext context,
        CancellationToken ct)
    {
        var domainAction = (DomainAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        // Evaluate parameters
        var evaluatedParams = ValueEvaluator.EvaluateParameters(
            domainAction.Parameters, scope, context.Evaluator);

        // Get required parameters
        var path = evaluatedParams.GetValueOrDefault("path")?.ToString();
        if (string.IsNullOrEmpty(path))
        {
            throw new InvalidOperationException("state_update requires path parameter");
        }

        var operation = evaluatedParams.GetValueOrDefault("operation")?.ToString() ?? "set";
        var value = evaluatedParams.GetValueOrDefault("value");

        // Execute the update
        ExecuteUpdate(scope, path, operation, value);

        return ValueTask.FromResult(ActionResult.Continue);
    }

    /// <summary>
    /// Executes the state update on the scope.
    /// </summary>
    private void ExecuteUpdate(IVariableScope scope, string path, string operation, object? value)
    {
        var parts = path.Split('.', 2);
        var rootKey = parts[0];

        switch (operation.ToLowerInvariant())
        {
            case "set":
                if (parts.Length == 1)
                {
                    scope.SetValue(rootKey, value);
                }
                else
                {
                    // Nested path - get or create dictionary
                    var existing = scope.GetValue(rootKey) as Dictionary<string, object?> ?? new Dictionary<string, object?>();
                    SetNestedValue(existing, parts[1], value);
                    scope.SetValue(rootKey, existing);
                }
                _logger.LogDebug("Set state path {Path}", path);
                break;

            case "append":
                if (parts.Length == 1)
                {
                    var list = scope.GetValue(rootKey) as List<object?> ?? new List<object?>();
                    list.Add(value);
                    scope.SetValue(rootKey, list);
                }
                else
                {
                    var existing = scope.GetValue(rootKey) as Dictionary<string, object?> ?? new Dictionary<string, object?>();
                    AppendNestedValue(existing, parts[1], value);
                    scope.SetValue(rootKey, existing);
                }
                _logger.LogDebug("Appended to state path {Path}", path);
                break;

            case "increment":
                var incrementBy = Convert.ToDouble(value ?? 1);
                var currentIncrement = Convert.ToDouble(scope.GetValue(path.Replace(".", "_")) ?? 0);
                scope.SetValue(path.Replace(".", "_"), currentIncrement + incrementBy);
                _logger.LogDebug("Incremented state path {Path} by {Amount}", path, incrementBy);
                break;

            case "decrement":
                var decrementBy = Convert.ToDouble(value ?? 1);
                var currentDecrement = Convert.ToDouble(scope.GetValue(path.Replace(".", "_")) ?? 0);
                scope.SetValue(path.Replace(".", "_"), currentDecrement - decrementBy);
                _logger.LogDebug("Decremented state path {Path} by {Amount}", path, decrementBy);
                break;

            default:
                throw new InvalidOperationException($"Unknown state_update operation: {operation}");
        }
    }

    /// <summary>
    /// Sets a nested value in a dictionary.
    /// </summary>
    private static void SetNestedValue(Dictionary<string, object?> dict, string path, object? value)
    {
        var parts = path.Split('.', 2);
        if (parts.Length == 1)
        {
            dict[parts[0]] = value;
        }
        else
        {
            if (!dict.TryGetValue(parts[0], out var nested) || nested is not Dictionary<string, object?> nestedDict)
            {
                nestedDict = new Dictionary<string, object?>();
                dict[parts[0]] = nestedDict;
            }
            SetNestedValue(nestedDict, parts[1], value);
        }
    }

    /// <summary>
    /// Appends a value to a nested list in a dictionary.
    /// </summary>
    private static void AppendNestedValue(Dictionary<string, object?> dict, string path, object? value)
    {
        var parts = path.Split('.', 2);
        if (parts.Length == 1)
        {
            if (!dict.TryGetValue(parts[0], out var existing) || existing is not List<object?> list)
            {
                list = new List<object?>();
                dict[parts[0]] = list;
            }
            list.Add(value);
        }
        else
        {
            if (!dict.TryGetValue(parts[0], out var nested) || nested is not Dictionary<string, object?> nestedDict)
            {
                nestedDict = new Dictionary<string, object?>();
                dict[parts[0]] = nestedDict;
            }
            AppendNestedValue(nestedDict, parts[1], value);
        }
    }
}
