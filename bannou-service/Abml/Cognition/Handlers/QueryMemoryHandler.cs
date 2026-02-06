// =============================================================================
// Query Memory Handler (Cognition Stage 2)
// Queries relevant memories for filtered perceptions.
// =============================================================================

using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Execution;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;

namespace BeyondImmersion.BannouService.Abml.Cognition.Handlers;

/// <summary>
/// ABML action handler for memory querying (Cognition Stage 2).
/// Queries the memory store for relevant memories based on perceptions.
/// </summary>
/// <remarks>
/// <para>
/// ABML usage:
/// <code>
/// - query_memory:
///     perceptions: "${filtered_perceptions}"
///     entity_id: "${agent.id}"
///     limit: 10
///     result_variable: "relevant_memories"
/// </code>
/// </para>
/// </remarks>
public sealed class QueryMemoryHandler : IActionHandler
{
    private const string ACTION_NAME = "query_memory";
    private readonly IMemoryStore _memoryStore;

    /// <summary>
    /// Creates a new query memory handler.
    /// </summary>
    /// <param name="memoryStore">Memory store for querying memories.</param>
    public QueryMemoryHandler(IMemoryStore memoryStore)
    {
        _memoryStore = memoryStore;
    }

    /// <inheritdoc/>
    public bool CanHandle(ActionNode action)
        => action is DomainAction da && da.Name == ACTION_NAME;

    /// <inheritdoc/>
    public async ValueTask<ActionResult> ExecuteAsync(
        ActionNode action,
        ExecutionContext context,
        CancellationToken ct)
    {
        var domainAction = (DomainAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        // Evaluate parameters
        var evaluatedParams = ValueEvaluator.EvaluateParameters(
            domainAction.Parameters, scope, context.Evaluator);

        // Get perceptions
        var perceptionsObj = evaluatedParams.GetValueOrDefault("perceptions");
        var perceptions = ConvertToPerceptions(perceptionsObj);

        // Get entity ID
        var entityId = evaluatedParams.GetValueOrDefault("entity_id")?.ToString();
        if (string.IsNullOrEmpty(entityId))
        {
            throw new InvalidOperationException("query_memory requires entity_id parameter");
        }

        // Get limit
        var limit = 10;
        if (evaluatedParams.TryGetValue("limit", out var limitObj) && limitObj != null)
        {
            limit = Convert.ToInt32(limitObj);
        }

        // Get result variable name
        var resultVariable = evaluatedParams.GetValueOrDefault("result_variable")?.ToString()
            ?? "relevant_memories";

        // Query memory store
        var memories = await _memoryStore.FindRelevantAsync(entityId, perceptions, limit, ct);

        // Store results in scope
        scope.SetValue(resultVariable, memories);

        return ActionResult.Continue;
    }

    private static IReadOnlyList<Perception> ConvertToPerceptions(object? input)
    {
        if (input == null)
        {
            return [];
        }

        if (input is IReadOnlyList<Perception> perceptionList)
        {
            return perceptionList;
        }

        if (input is IEnumerable<object> objectList)
        {
            var result = new List<Perception>();
            foreach (var item in objectList)
            {
                if (item is Perception p)
                {
                    result.Add(p);
                }
                else if (item is IReadOnlyDictionary<string, object?> dict)
                {
                    result.Add(Perception.FromDictionary(dict));
                }
            }
            return result;
        }

        return [];
    }

    public ValueTask<ActionResult> ExecuteAsync(ActionNode action, Execution.ExecutionContext context, CancellationToken ct) => throw new NotImplementedException();
}
