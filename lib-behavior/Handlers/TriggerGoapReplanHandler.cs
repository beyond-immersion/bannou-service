// =============================================================================
// Trigger GOAP Replan Handler (Cognition Stage 5)
// Triggers GOAP replanning with urgency-based constraints.
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Cognition;
using BeyondImmersion.Bannou.Behavior.Goap;
using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.Behavior;
using Microsoft.Extensions.Logging;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;
using GoapGoal = BeyondImmersion.Bannou.Behavior.Goap.GoapGoal;

namespace BeyondImmersion.Bannou.Behavior.Handlers;

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
    private readonly IBehaviorBundleManager? _bundleManager;
    private readonly ILogger<TriggerGoapReplanHandler> _logger;

    /// <summary>
    /// Creates a new trigger GOAP replan handler.
    /// </summary>
    /// <param name="planner">GOAP planner for creating plans.</param>
    /// <param name="bundleManager">Bundle manager for loading GOAP metadata (optional).</param>
    /// <param name="logger">Logger instance.</param>
    public TriggerGoapReplanHandler(
        IGoapPlanner planner,
        IBehaviorBundleManager? bundleManager,
        ILogger<TriggerGoapReplanHandler> logger)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _bundleManager = bundleManager;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        // Get available actions - from params, cached metadata, or scope
        var availableActions = await GetAvailableActionsAsync(evaluatedParams, behaviorId, scope, ct);

        // Get the goal to plan for
        var goal = await GetPlanningGoalAsync(evaluatedParams, affectedGoals, behaviorId, ct);

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

    private async Task<IReadOnlyList<GoapAction>> GetAvailableActionsAsync(
        IReadOnlyDictionary<string, object?> evaluatedParams,
        string behaviorId,
        IVariableScope scope,
        CancellationToken ct)
    {
        // First, check if actions were provided directly in parameters
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

        // Try to load from behavior metadata via bundle manager
        if (_bundleManager != null && !string.IsNullOrEmpty(behaviorId) && behaviorId != "unknown")
        {
            var metadata = await _bundleManager.GetGoapMetadataAsync(behaviorId, ct);
            if (metadata != null)
            {
                return ConvertCachedActions(metadata.Actions);
            }
        }

        return [];
    }

    private async Task<GoapGoal?> GetPlanningGoalAsync(
        IReadOnlyDictionary<string, object?> evaluatedParams,
        List<string> affectedGoals,
        string behaviorId,
        CancellationToken ct)
    {
        // First, check if a goal was provided directly in parameters
        if (evaluatedParams.TryGetValue("goal", out var goalObj) && goalObj is GoapGoal providedGoal)
        {
            return providedGoal;
        }

        // If no affected goals, can't plan
        if (affectedGoals.Count == 0)
        {
            return null;
        }

        // Try to load goal definition from behavior metadata
        if (_bundleManager != null && !string.IsNullOrEmpty(behaviorId) && behaviorId != "unknown")
        {
            var metadata = await _bundleManager.GetGoapMetadataAsync(behaviorId, ct);
            if (metadata != null)
            {
                // Find the first matching affected goal
                var goalName = affectedGoals[0];
                var cachedGoal = metadata.Goals.FirstOrDefault(g =>
                    g.Name.Equals(goalName, StringComparison.OrdinalIgnoreCase));

                if (cachedGoal != null)
                {
                    return ConvertCachedGoal(cachedGoal);
                }
            }
        }

        // Create a simple placeholder goal if we have a name but no metadata
        if (affectedGoals.Count > 0)
        {
            // Can't create a meaningful goal without conditions
            return null;
        }

        return null;
    }

    private static IReadOnlyList<GoapAction> ConvertCachedActions(List<CachedGoapAction> cached)
    {
        var actions = new List<GoapAction>();
        foreach (var c in cached)
        {
            actions.Add(GoapAction.FromMetadata(c.FlowName, c.Preconditions, c.Effects, c.Cost));
        }
        return actions;
    }

    private static GoapGoal ConvertCachedGoal(CachedGoapGoal cached)
    {
        return GoapGoal.FromMetadata(cached.Name, cached.Priority, cached.Conditions);
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
