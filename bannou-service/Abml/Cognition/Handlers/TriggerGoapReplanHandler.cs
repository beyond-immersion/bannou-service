// =============================================================================
// Trigger GOAP Replan Handler (Cognition Stage 5)
// Triggers GOAP replanning with urgency-based constraints.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using BeyondImmersion.Bannou.BehaviorCompiler.Goap;
using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Abml.Execution;
using Microsoft.Extensions.Logging;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;
using GoapGoal = BeyondImmersion.Bannou.BehaviorCompiler.Goap.GoapGoal;

namespace BeyondImmersion.BannouService.Abml.Cognition.Handlers;

/// <summary>
/// ABML action handler for triggering GOAP replanning (Cognition Stage 5).
/// Invokes the GOAP planner with urgency-based constraints.
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
///     available_actions: "${agent.goap_actions}"
///     goal: "${agent.current_goal}"
///     result_variable: "replan_status"
/// </code>
/// </para>
/// <para>
/// Urgency maps to GOAP planning parameters:
/// - Low (0-0.3): MaxDepth=10, Timeout=100ms - Full deliberation
/// - Medium (0.3-0.7): MaxDepth=6, Timeout=50ms - Quick decision
/// - High (0.7-1.0): MaxDepth=3, Timeout=20ms - Immediate reaction
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS</b>: This handler is "dumb" - it requires all data
/// (goals, actions, world state) to be provided via parameters or scope.
/// It does NOT load data from external services. The caller (ActorRunner)
/// is responsible for loading GOAP metadata and providing it in scope.
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
    public TriggerGoapReplanHandler(
        IGoapPlanner planner,
        ILogger<TriggerGoapReplanHandler> logger)
    {
        _planner = planner;
        _logger = logger;
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

        var status = new ReplanStatus
        {
            Triggered = true,
            EntityId = entityId,
            BehaviorId = behaviorId,
            Urgency = urgency,
            AffectedGoals = affectedGoals,
            PlanningOptions = planningOptions
        };

        // Get available actions from params or scope - caller must provide these
        var availableActions = GetAvailableActions(evaluatedParams, scope);

        // Get the goal from params or scope - caller must provide this
        var goal = GetPlanningGoal(evaluatedParams, affectedGoals, scope);

        // Execute planning if we have sufficient context
        if (goal != null && availableActions.Count > 0 && worldState.Count > 0)
        {
            try
            {
                _logger.LogDebug(
                    "Planning for goal {GoalId} with {ActionCount} actions and {StateCount} state properties",
                    goal.Id, availableActions.Count, worldState.Count);

                var plan = await _planner.PlanAsync(
                    worldState,
                    goal,
                    availableActions,
                    planningOptions.ToPlanningOptions(),
                    ct);

                if (plan != null)
                {
                    status.Plan = plan;
                    status.Message = $"Plan found with {plan.Actions.Count} action(s), total cost {plan.TotalCost:F2}";
                    _logger.LogInformation(
                        "GOAP plan found for entity {EntityId}: {ActionCount} actions, cost {Cost:F2}",
                        entityId, plan.Actions.Count, plan.TotalCost);
                }
                else
                {
                    status.Message = $"No plan found for goal '{goal.Name}' from current state";
                    _logger.LogWarning(
                        "No GOAP plan found for entity {EntityId} goal {GoalId}",
                        entityId, goal.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during GOAP planning for entity {EntityId}", entityId);
                status.Message = $"Planning error: {ex.Message}";
            }
        }
        else
        {
            var missing = new List<string>();
            if (goal == null) missing.Add("goal");
            if (availableActions.Count == 0) missing.Add("actions");
            if (worldState.Count == 0) missing.Add("world_state");

            status.Message = $"Insufficient context for planning: missing {string.Join(", ", missing)}";
            _logger.LogDebug(
                "Cannot plan for entity {EntityId}: missing {Missing}",
                entityId, string.Join(", ", missing));
        }

        // Store result in scope
        scope.SetValue(resultVariable, status);

        return ActionResult.Continue;
    }

    private static IReadOnlyList<GoapAction> GetAvailableActions(
        IReadOnlyDictionary<string, object?> evaluatedParams,
        IVariableScope scope)
    {
        // Check if actions were provided directly in parameters
        if (evaluatedParams.TryGetValue("available_actions", out var actionsObj) && actionsObj != null)
        {
            if (actionsObj is IReadOnlyList<GoapAction> actions)
            {
                return actions;
            }
        }

        // Check if actions are in scope (e.g., from actor context)
        var scopeActions = scope.GetValue("goap_actions") as IReadOnlyList<GoapAction>;
        if (scopeActions != null)
        {
            return scopeActions;
        }

        // No actions available - caller must provide them
        return [];
    }

    private static GoapGoal? GetPlanningGoal(
        IReadOnlyDictionary<string, object?> evaluatedParams,
        List<string> affectedGoals,
        IVariableScope scope)
    {
        // Check if a goal was provided directly in parameters
        if (evaluatedParams.TryGetValue("goal", out var goalObj) && goalObj is GoapGoal providedGoal)
        {
            return providedGoal;
        }

        // Check if a goal is in scope
        var scopeGoal = scope.GetValue("current_goal") as GoapGoal;
        if (scopeGoal != null)
        {
            return scopeGoal;
        }

        // Check for goals list in scope that we can filter by affected goals
        if (affectedGoals.Count > 0)
        {
            var scopeGoals = scope.GetValue("goap_goals") as IReadOnlyList<GoapGoal>;
            if (scopeGoals != null)
            {
                var goalName = affectedGoals[0];
                var matchingGoal = scopeGoals.FirstOrDefault(g =>
                    g.Name.Equals(goalName, StringComparison.OrdinalIgnoreCase));
                if (matchingGoal != null)
                {
                    return matchingGoal;
                }
            }
        }

        // No goal available - caller must provide it
        return null;
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
    /// The generated plan, if planning succeeded.
    /// </summary>
    public GoapPlan? Plan { get; set; }

    /// <summary>
    /// Status message.
    /// </summary>
    public string? Message { get; set; }
}
