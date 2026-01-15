// =============================================================================
// Store Memory Handler (Cognition Stage 4)
// Stores significant experiences as memories.
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Cognition;
using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Execution;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;

namespace BeyondImmersion.Bannou.Behavior.Handlers;

/// <summary>
/// ABML action handler for memory storage (Cognition Stage 4).
/// Stores perceptions that exceed the significance threshold as memories.
/// </summary>
/// <remarks>
/// <para>
/// ABML usage:
/// <code>
/// - store_memory:
///     entity_id: "${agent.id}"
///     perception: "${perception}"
///     significance: "${significance_score.total_score}"
///     context: "${relevant_memories}"
/// </code>
/// </para>
/// </remarks>
public sealed class StoreMemoryHandler : IActionHandler
{
    private const string ACTION_NAME = "store_memory";
    private readonly IMemoryStore _memoryStore;

    /// <summary>
    /// Creates a new store memory handler.
    /// </summary>
    /// <param name="memoryStore">Memory store for storing memories.</param>
    public StoreMemoryHandler(IMemoryStore memoryStore)
    {
        _memoryStore = memoryStore;
    }

    /// <inheritdoc/>
    public bool CanHandle(ActionNode action)
        => action is DomainAction da && da.Name == ACTION_NAME;

    /// <inheritdoc/>
    public async ValueTask<ActionResult> ExecuteAsync(
        ActionNode action,
        AbmlExecutionContext context,
        CancellationToken ct)
    {
        var domainAction = (DomainAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        // Evaluate parameters
        var evaluatedParams = ValueEvaluator.EvaluateParameters(
            domainAction.Parameters, scope, context.Evaluator);

        // Get entity ID
        var entityId = evaluatedParams.GetValueOrDefault("entity_id")?.ToString();
        if (string.IsNullOrEmpty(entityId))
        {
            throw new InvalidOperationException("store_memory requires entity_id parameter");
        }

        // Get perception
        var perceptionObj = evaluatedParams.GetValueOrDefault("perception");
        var perception = ConvertToPerception(perceptionObj) ?? throw new InvalidOperationException("store_memory requires perception parameter");

        // Get significance
        var significance = 0.5f;
        if (evaluatedParams.TryGetValue("significance", out var sigObj) && sigObj != null)
        {
            significance = Convert.ToSingle(sigObj);
        }

        // Get context memories
        var contextMemories = ConvertToMemories(evaluatedParams.GetValueOrDefault("context"));

        // Store the experience
        await _memoryStore.StoreExperienceAsync(entityId, perception, significance, contextMemories, ct);

        return ActionResult.Continue;
    }

    private static Perception? ConvertToPerception(object? input)
    {
        if (input == null)
        {
            return null;
        }

        if (input is Perception p)
        {
            return p;
        }

        if (input is IReadOnlyDictionary<string, object?> dict)
        {
            return Perception.FromDictionary(dict);
        }

        return null;
    }

    private static IReadOnlyList<Memory> ConvertToMemories(object? input)
    {
        if (input == null)
        {
            return [];
        }

        if (input is IReadOnlyList<Memory> memoryList)
        {
            return memoryList;
        }

        if (input is IEnumerable<object> objectList)
        {
            var result = new List<Memory>();
            foreach (var item in objectList)
            {
                if (item is Memory m)
                {
                    result.Add(m);
                }
            }
            return result;
        }

        return [];
    }
}
