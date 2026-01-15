// =============================================================================
// Query Actor State Handler
// ABML action handler for querying actor state (Event Brain support).
// =============================================================================

using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Actor.Runtime;
using Microsoft.Extensions.Logging;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;

namespace BeyondImmersion.BannouService.Actor.Handlers;

/// <summary>
/// ABML action handler for querying actor state.
/// Uses local IActorRegistry for direct state access in bannou mode.
/// </summary>
/// <remarks>
/// <para>
/// ABML usage:
/// <code>
/// - query_actor_state:
///     actor_id: "${participant.id}"
///     paths:
///       - "feelings"
///       - "goals"
///       - "memories"
///     result_variable: "actor_data"
/// </code>
/// </para>
/// <para>
/// This handler provides direct access to actor state via the local registry.
/// For distributed deployments, actors should use query_options instead.
/// </para>
/// </remarks>
public sealed class QueryActorStateHandler : IActionHandler
{
    private const string ACTION_NAME = "query_actor_state";
    private readonly IActorRegistry _actorRegistry;
    private readonly ILogger<QueryActorStateHandler> _logger;

    /// <summary>
    /// Creates a new query actor state handler.
    /// </summary>
    /// <param name="actorRegistry">Actor registry for local state access.</param>
    /// <param name="logger">Logger instance.</param>
    public QueryActorStateHandler(
        IActorRegistry actorRegistry,
        ILogger<QueryActorStateHandler> logger)
    {
        _actorRegistry = actorRegistry;
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
        var actorId = evaluatedParams.GetValueOrDefault("actor_id")?.ToString();
        if (string.IsNullOrEmpty(actorId))
        {
            throw new InvalidOperationException("query_actor_state requires actor_id parameter");
        }

        var resultVariable = evaluatedParams.GetValueOrDefault("result_variable")?.ToString() ?? "actor_state";

        // Get paths to query (optional, returns all if empty)
        var paths = new List<string>();
        if (evaluatedParams.TryGetValue("paths", out var pathsObj))
        {
            if (pathsObj is IEnumerable<object> pathList)
            {
                paths.AddRange(pathList.Select(p => p?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)));
            }
            else if (pathsObj is string singlePath)
            {
                paths.Add(singlePath);
            }
        }

        _logger.LogDebug("Querying state for actor {ActorId}, paths: {Paths}",
            actorId, paths.Count > 0 ? string.Join(", ", paths) : "all");

        // Try to get actor from local registry
        if (!_actorRegistry.TryGet(actorId, out var runner) || runner == null)
        {
            _logger.LogWarning("Actor {ActorId} not found in local registry", actorId);
            scope.SetValue(resultVariable, null);
            return ValueTask.FromResult(ActionResult.Continue);
        }

        // Get state snapshot
        var snapshot = runner.GetStateSnapshot();

        // Build result dictionary
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (paths.Count == 0)
        {
            // Return all state
            result["feelings"] = snapshot.Feelings;
            result["goals"] = snapshot.Goals;
            result["memories"] = snapshot.Memories;
            result["working_memory"] = snapshot.WorkingMemory;
            result["actor_id"] = snapshot.ActorId;
            result["character_id"] = snapshot.CharacterId;
            result["status"] = snapshot.Status;
        }
        else
        {
            // Return requested paths
            foreach (var path in paths)
            {
                result[path] = ExtractPathValue(snapshot, path);
            }
        }

        // Store result in scope
        scope.SetValue(resultVariable, result);

        _logger.LogDebug("Retrieved state for actor {ActorId}", actorId);

        return ValueTask.FromResult(ActionResult.Continue);
    }

    /// <summary>
    /// Extracts a value from the state snapshot based on a path.
    /// </summary>
    private static object? ExtractPathValue(ActorStateSnapshot snapshot, string path)
    {
        var lowerPath = path.ToLowerInvariant();

        return lowerPath switch
        {
            "feelings" => snapshot.Feelings,
            "goals" => snapshot.Goals,
            "memories" => snapshot.Memories,
            "working_memory" or "workingmemory" => snapshot.WorkingMemory,
            "primary_goal" or "primarygoal" => snapshot.Goals?.PrimaryGoal,
            "secondary_goals" or "secondarygoals" => snapshot.Goals?.SecondaryGoals,
            "actor_id" or "actorid" => snapshot.ActorId,
            "character_id" or "characterid" => snapshot.CharacterId,
            "status" => snapshot.Status,
            "template_id" or "templateid" => snapshot.TemplateId,
            _ => ExtractNestedPath(snapshot, path)
        };
    }

    /// <summary>
    /// Extracts a nested path value (e.g., "feelings.anger").
    /// </summary>
    private static object? ExtractNestedPath(ActorStateSnapshot snapshot, string path)
    {
        var parts = path.Split('.', 2);
        if (parts.Length < 2)
        {
            return null;
        }

        var category = parts[0].ToLowerInvariant();
        var key = parts[1];

        return category switch
        {
            "feelings" => snapshot.Feelings?.GetValueOrDefault(key),
            "working_memory" or "workingmemory" => snapshot.WorkingMemory?.GetValueOrDefault(key),
            _ => null
        };
    }
}
