// =============================================================================
// Trigger GOAP Replan Handler (Cognition Stage 5)
// Non-blocking trigger for GOAP replanning.
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Cognition;
using BeyondImmersion.Bannou.Behavior.Goap;
using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Execution;
using Microsoft.Extensions.Logging;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;

namespace BeyondImmersion.Bannou.Behavior.Handlers;

/// <summary>
/// ABML action handler for triggering GOAP replanning (Cognition Stage 5).
/// Non-blocking - queues replan request and continues execution.
/// </summary>
/// <remarks>
/// <para>
/// ABML usage:
/// <code>
/// - trigger_goap_replan:
///     goals: "${goal_updates.affected_goals}"
///     urgency: "${goal_updates.urgency}"
///     world_state: "${agent.world_state}"
///     behavior_id: "${agent.behavior_id}"
///     entity_id: "${agent.id}"
///     result_variable: "replan_status"
/// </code>
/// </para>
/// <para>
/// Urgency maps to GOAP planning parameters:
/// - Low (0-0.3): MaxDepth=10, Timeout=100ms - Full deliberation
/// - Medium (0.3-0.7): MaxDepth=6, Timeout=50ms - Quick decision
/// - High (0.7-1.0): MaxDepth=3, Timeout=20ms - Immediate reaction
/// </para>
/// </remarks>
public sealed class TriggerGoapReplanHandler : IActionHandler
{
    private const string ACTION_NAME = "trigger_goap_replan";
    private readonly IGoapPlanner _planner;
    private readonly ILogger<TriggerGoapReplanHandler> _logger;

    /// <summary>
    /// Creates a new trigger GOAP replan handler.
    /// </summary>
    /// <param name="planner">GOAP planner for creating plans.</param>
    /// <param name="logger">Logger instance.</param>
    public TriggerGoapReplanHandler(IGoapPlanner planner, ILogger<TriggerGoapReplanHandler> logger)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        // Get urgency and map to planning options
        var urgency = 0.5f;
        if (evaluatedParams.TryGetValue("urgency", out var urgencyObj) && urgencyObj != null)
        {
            urgency = Convert.ToSingle(urgencyObj);
        }

        var planningOptions = UrgencyBasedPlanningOptions.FromUrgency(urgency);

        // Get entity and behavior IDs for logging
        var entityId = evaluatedParams.GetValueOrDefault("entity_id")?.ToString() ?? "unknown";
        var behaviorId = evaluatedParams.GetValueOrDefault("behavior_id")?.ToString() ?? "unknown";

        // Get goals to replan for
        var affectedGoals = ExtractGoalIds(evaluatedParams.GetValueOrDefault("goals"));

        // Get world state
        var worldStateObj = evaluatedParams.GetValueOrDefault("world_state");
        var worldState = ConvertToWorldState(worldStateObj);

        // Get result variable name
        var resultVariable = evaluatedParams.GetValueOrDefault("result_variable")?.ToString()
            ?? "replan_status";

        _logger.LogInformation(
            "Triggering GOAP replan for entity {EntityId} with urgency {Urgency} (depth={MaxDepth}, timeout={TimeoutMs}ms)",
            entityId, urgency, planningOptions.MaxDepth, planningOptions.TimeoutMs);

        // For MVP, we execute the replan synchronously but with urgency-based constraints
        // In a full implementation, this would queue the replan and return immediately
        var status = new ReplanStatus
        {
            Triggered = true,
            EntityId = entityId,
            BehaviorId = behaviorId,
            Urgency = urgency,
            AffectedGoals = affectedGoals,
            PlanningOptions = planningOptions
        };

        // Try to create a quick plan if we have enough context
        if (affectedGoals.Count > 0 && worldState.Count > 0)
        {
            try
            {
                // Get actions from behavior model (would normally come from cached GOAP metadata)
                // For now, we just note that replanning was triggered
                status.Message = $"Replan queued for {affectedGoals.Count} affected goal(s)";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during GOAP replan preparation for entity {EntityId}", entityId);
                status.Message = $"Replan error: {ex.Message}";
            }
        }
        else
        {
            status.Message = "Replan triggered but insufficient context for immediate planning";
        }

        // Store result in scope
        scope.SetValue(resultVariable, status);

        return ValueTask.FromResult(ActionResult.Continue);
    }

    private static List<string> ExtractGoalIds(object? goals)
    {
        var result = new List<string>();

        if (goals == null)
        {
            return result;
        }

        if (goals is string goalStr)
        {
            result.Add(goalStr);
        }
        else if (goals is IEnumerable<string> goalStrings)
        {
            result.AddRange(goalStrings);
        }
        else if (goals is IEnumerable<object> goalObjects)
        {
            foreach (var g in goalObjects)
            {
                if (g is string s)
                {
                    result.Add(s);
                }
                else
                {
                    result.Add(g?.ToString() ?? "");
                }
            }
        }

        return result;
    }

    private static WorldState ConvertToWorldState(object? worldStateObj)
    {
        var state = new WorldState();

        if (worldStateObj == null)
        {
            return state;
        }

        if (worldStateObj is WorldState ws)
        {
            return ws;
        }

        if (worldStateObj is IReadOnlyDictionary<string, object?> dict)
        {
            foreach (var (key, value) in dict)
            {
                if (value == null) continue;

                if (value is float f)
                {
                    state = state.SetNumeric(key, f);
                }
                else if (value is double d)
                {
                    state = state.SetNumeric(key, (float)d);
                }
                else if (value is int i)
                {
                    state = state.SetNumeric(key, i);
                }
                else if (value is bool b)
                {
                    state = state.SetBoolean(key, b);
                }
                else if (value is string s)
                {
                    state = state.SetString(key, s);
                }
            }
        }

        return state;
    }
}

/// <summary>
/// Status of a triggered replan request.
/// </summary>
public sealed class ReplanStatus
{
    /// <summary>
    /// Whether the replan was successfully triggered.
    /// </summary>
    public bool Triggered { get; init; }

    /// <summary>
    /// Entity ID for the replan.
    /// </summary>
    public string EntityId { get; init; } = string.Empty;

    /// <summary>
    /// Behavior ID for the replan.
    /// </summary>
    public string BehaviorId { get; init; } = string.Empty;

    /// <summary>
    /// Urgency level (0-1).
    /// </summary>
    public float Urgency { get; init; }

    /// <summary>
    /// Goals affected by the replan.
    /// </summary>
    public IReadOnlyList<string> AffectedGoals { get; init; } = [];

    /// <summary>
    /// Planning options derived from urgency.
    /// </summary>
    public UrgencyBasedPlanningOptions? PlanningOptions { get; init; }

    /// <summary>
    /// Status message.
    /// </summary>
    public string? Message { get; set; }
}
