using BeyondImmersion.Bannou.Behavior.Goap;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using InternalGoapGoal = BeyondImmersion.Bannou.Behavior.Goap.GoapGoal;

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Behavior service implementation for ABML (Agent Behavior Markup Language) processing.
/// Provides GOAP planning and behavior compilation services.
/// </summary>
[BannouService("behavior", typeof(IBehaviorService), lifetime: ServiceLifetime.Scoped)]
public partial class BehaviorService : IBehaviorService
{
    private readonly ILogger<BehaviorService> _logger;
    private readonly BehaviorServiceConfiguration _configuration;
    private readonly IMessageBus _messageBus;
    private readonly IGoapPlanner _goapPlanner;

    /// <summary>
    /// Creates a new instance of the BehaviorService.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Service configuration.</param>
    /// <param name="messageBus">Message bus for event publishing.</param>
    /// <param name="eventConsumer">Event consumer for registering handlers.</param>
    /// <param name="goapPlanner">GOAP planner for generating action plans.</param>
    public BehaviorService(
        ILogger<BehaviorService> logger,
        BehaviorServiceConfiguration configuration,
        IMessageBus messageBus,
        IEventConsumer eventConsumer,
        IGoapPlanner goapPlanner)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _goapPlanner = goapPlanner ?? throw new ArgumentNullException(nameof(goapPlanner));

        // Register event handlers via partial class (BehaviorServiceEvents.cs)
        ArgumentNullException.ThrowIfNull(eventConsumer, nameof(eventConsumer));
        ((IBannouService)this).RegisterEventConsumers(eventConsumer);
    }

    /// <summary>
    /// Compiles ABML behavior definition. Not yet implemented - planned for future release.
    /// </summary>
    public async Task<(StatusCodes, CompileBehaviorResponse?)> CompileAbmlBehaviorAsync(CompileBehaviorRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogWarning("Method CompileAbmlBehaviorAsync called but not implemented");
            return (StatusCodes.NotImplemented, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error compiling ABML behavior");
            await _messageBus.TryPublishErrorAsync(
                serviceId: "behavior",
                operation: "CompileAbmlBehavior",
                errorType: ex.GetType().Name,
                message: ex.Message,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Compiles a stack of behaviors with priority resolution. Not yet implemented - planned for future release.
    /// </summary>
    public async Task<(StatusCodes, CompileBehaviorResponse?)> CompileBehaviorStackAsync(BehaviorStackRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogWarning("Method CompileBehaviorStackAsync called but not implemented");
            return (StatusCodes.NotImplemented, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error compiling behavior stack");
            await _messageBus.TryPublishErrorAsync(
                serviceId: "behavior",
                operation: "CompileBehaviorStack",
                errorType: ex.GetType().Name,
                message: ex.Message,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Validates ABML YAML syntax and schema. Not yet implemented - planned for future release.
    /// </summary>
    public async Task<(StatusCodes, ValidateAbmlResponse?)> ValidateAbmlAsync(ValidateAbmlRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogWarning("Method ValidateAbmlAsync called but not implemented");
            return (StatusCodes.NotImplemented, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating ABML");
            await _messageBus.TryPublishErrorAsync(
                serviceId: "behavior",
                operation: "ValidateAbml",
                errorType: ex.GetType().Name,
                message: ex.Message,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Retrieves a cached compiled behavior. Not yet implemented - planned for future release.
    /// </summary>
    public async Task<(StatusCodes, CachedBehaviorResponse?)> GetCachedBehaviorAsync(GetCachedBehaviorRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogWarning("Method GetCachedBehaviorAsync called but not implemented for: {BehaviorId}", body.BehaviorId);
            return (StatusCodes.NotImplemented, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving cached behavior");
            await _messageBus.TryPublishErrorAsync(
                serviceId: "behavior",
                operation: "GetCachedBehavior",
                errorType: ex.GetType().Name,
                message: ex.Message,
                details: new { BehaviorId = body.BehaviorId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Resolves context variables and cultural adaptations. Not yet implemented - planned for future release.
    /// </summary>
    public async Task<(StatusCodes, ResolveContextResponse?)> ResolveContextVariablesAsync(ResolveContextRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogWarning("Method ResolveContextVariablesAsync called but not implemented");
            return (StatusCodes.NotImplemented, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving context variables");
            await _messageBus.TryPublishErrorAsync(
                serviceId: "behavior",
                operation: "ResolveContextVariables",
                errorType: ex.GetType().Name,
                message: ex.Message,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Invalidates a cached compiled behavior. Not yet implemented - planned for future release.
    /// </summary>
    public async Task<(StatusCodes, object?)> InvalidateCachedBehaviorAsync(InvalidateCacheRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogWarning("Method InvalidateCachedBehaviorAsync called but not implemented for: {BehaviorId}", body.BehaviorId);
            return (StatusCodes.NotImplemented, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating cached behavior: {BehaviorId}", body.BehaviorId);
            await _messageBus.TryPublishErrorAsync(
                serviceId: "behavior",
                operation: "InvalidateCachedBehavior",
                errorType: ex.GetType().Name,
                message: ex.Message,
                details: new { BehaviorId = body.BehaviorId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Generates a GOAP plan to achieve the specified goal from the current world state.
    /// </summary>
    /// <param name="body">The plan request containing goal, world state, and options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The planning result with actions or failure reason.</returns>
    public async Task<(StatusCodes, GoapPlanResponse?)> GenerateGoapPlanAsync(GoapPlanRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "Generating GOAP plan for agent {AgentId}, goal {GoalName}",
                body.Agent_id,
                body.Goal.Name);

            // Convert API world state to internal WorldState
            var worldState = ConvertToWorldState(body.World_state);

            // Convert API goal to internal GoapGoal
            var goal = ConvertToGoapGoal(body.Goal);

            // For now, we don't have a behavior cache so we return an error for behavior_id
            // In the future, this will look up cached GOAP actions from compiled behaviors
            if (string.IsNullOrEmpty(body.Behavior_id))
            {
                return (StatusCodes.BadRequest, new GoapPlanResponse
                {
                    Success = false,
                    Failure_reason = "behavior_id is required to retrieve GOAP actions"
                });
            }

            // TODO: Look up cached behavior and extract GOAP actions
            // For now, return NotImplemented as behavior caching is not yet ready
            _logger.LogWarning(
                "GOAP planning for behavior {BehaviorId} not yet implemented - behavior caching required",
                body.Behavior_id);

            return (StatusCodes.NotImplemented, new GoapPlanResponse
            {
                Success = false,
                Failure_reason = "Behavior caching not yet implemented - GOAP actions cannot be retrieved"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating GOAP plan for agent {AgentId}", body.Agent_id);
            await _messageBus.TryPublishErrorAsync(
                serviceId: "behavior",
                operation: "GenerateGoapPlan",
                errorType: ex.GetType().Name,
                message: ex.Message,
                details: new { AgentId = body.Agent_id, GoalName = body.Goal.Name },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Validates an existing GOAP plan against the current world state.
    /// </summary>
    /// <param name="body">The validation request containing the plan and current state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validation result with suggested action.</returns>
    public async Task<(StatusCodes, ValidateGoapPlanResponse?)> ValidateGoapPlanAsync(ValidateGoapPlanRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "Validating GOAP plan for goal {GoalId}, current action index {ActionIndex}",
                body.Plan.Goal_id,
                body.Current_action_index);

            // Convert API world state to internal WorldState
            var worldState = ConvertToWorldState(body.World_state);

            // Convert API plan to internal format for validation
            var plan = ConvertToGoapPlan(body.Plan);

            // Convert active goals if provided
            var activeGoals = body.Active_goals?.Select(ConvertToGoapGoal).ToList()
                ?? new List<InternalGoapGoal>();

            // Validate the plan
            var result = await _goapPlanner.ValidatePlanAsync(
                plan,
                body.Current_action_index,
                worldState,
                activeGoals,
                cancellationToken);

            // Convert internal result to API response
            var response = new ValidateGoapPlanResponse
            {
                Is_valid = result.IsValid,
                Reason = ConvertToApiReplanReason(result.Reason),
                Suggested_action = ConvertToApiSuggestion(result.Suggestion),
                Invalidated_at_index = result.InvalidatedAtIndex,
                Message = result.Message ?? string.Empty
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating GOAP plan for goal {GoalId}", body.Plan.Goal_id);
            await _messageBus.TryPublishErrorAsync(
                serviceId: "behavior",
                operation: "ValidateGoapPlan",
                errorType: ex.GetType().Name,
                message: ex.Message,
                details: new { GoalId = body.Plan.Goal_id },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #region GOAP Type Conversions

    /// <summary>
    /// Converts an API world state object to an internal WorldState.
    /// </summary>
    private static WorldState ConvertToWorldState(object worldStateObject)
    {
        var worldState = new WorldState();

        if (worldStateObject == null)
        {
            return worldState;
        }

        // Handle JsonElement from API deserialization
        if (worldStateObject is System.Text.Json.JsonElement jsonElement)
        {
            return ConvertJsonElementToWorldState(jsonElement);
        }

        // Handle Dictionary<string, object>
        if (worldStateObject is IDictionary<string, object> dict)
        {
            foreach (var (key, value) in dict)
            {
                worldState = SetWorldStateValue(worldState, key, value);
            }
        }

        return worldState;
    }

    /// <summary>
    /// Converts a JsonElement to WorldState.
    /// </summary>
    private static WorldState ConvertJsonElementToWorldState(System.Text.Json.JsonElement element)
    {
        var worldState = new WorldState();

        if (element.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return worldState;
        }

        foreach (var property in element.EnumerateObject())
        {
            worldState = property.Value.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Number => worldState.SetNumeric(property.Name, property.Value.GetSingle()),
                System.Text.Json.JsonValueKind.True => worldState.SetBoolean(property.Name, true),
                System.Text.Json.JsonValueKind.False => worldState.SetBoolean(property.Name, false),
                System.Text.Json.JsonValueKind.String => worldState.SetString(property.Name, property.Value.GetString() ?? string.Empty),
                _ => worldState
            };
        }

        return worldState;
    }

    /// <summary>
    /// Sets a value in the world state based on its runtime type.
    /// </summary>
    private static WorldState SetWorldStateValue(WorldState state, string key, object? value)
    {
        return value switch
        {
            float f => state.SetNumeric(key, f),
            double d => state.SetNumeric(key, (float)d),
            int i => state.SetNumeric(key, i),
            long l => state.SetNumeric(key, l),
            bool b => state.SetBoolean(key, b),
            string s => state.SetString(key, s),
            _ => state
        };
    }

    /// <summary>
    /// Converts an API GoapGoal to an internal GoapGoal.
    /// </summary>
    private static InternalGoapGoal ConvertToGoapGoal(GoapGoal apiGoal)
    {
        // FromMetadata takes string conditions and parses them internally
        return InternalGoapGoal.FromMetadata(
            apiGoal.Name,
            apiGoal.Priority,
            (IReadOnlyDictionary<string, string>)apiGoal.Conditions);
    }

    /// <summary>
    /// Converts an API GoapPlanResult to an internal GoapPlan.
    /// </summary>
    private static GoapPlan ConvertToGoapPlan(GoapPlanResult apiPlan)
    {
        // Create a placeholder goal for validation purposes
        var goal = new InternalGoapGoal(apiPlan.Goal_id, apiPlan.Goal_id, 50);

        // Convert planned actions
        var actions = new List<PlannedAction>();
        foreach (var apiAction in apiPlan.Actions)
        {
            // Create a minimal GoapAction for validation
            var action = new GoapAction(
                apiAction.Action_id,
                apiAction.Action_id,
                new GoapPreconditions(),
                new GoapActionEffects(),
                apiAction.Cost);
            actions.Add(new PlannedAction(action, apiAction.Index));
        }

        return new GoapPlan(
            goal: goal,
            actions: actions,
            totalCost: apiPlan.Total_cost,
            nodesExpanded: 0,
            planningTimeMs: 0,
            initialState: new WorldState(),
            expectedFinalState: new WorldState());
    }

    /// <summary>
    /// Converts an internal ReplanReason to the API enum.
    /// </summary>
    private static ValidateGoapPlanResponseReason ConvertToApiReplanReason(ReplanReason reason)
    {
        return reason switch
        {
            ReplanReason.None => ValidateGoapPlanResponseReason.None,
            ReplanReason.PreconditionInvalidated => ValidateGoapPlanResponseReason.Precondition_invalidated,
            ReplanReason.ActionFailed => ValidateGoapPlanResponseReason.Action_failed,
            ReplanReason.BetterGoalAvailable => ValidateGoapPlanResponseReason.Better_goal_available,
            ReplanReason.PlanCompleted => ValidateGoapPlanResponseReason.Plan_completed,
            ReplanReason.GoalAlreadySatisfied => ValidateGoapPlanResponseReason.Goal_already_satisfied,
            _ => ValidateGoapPlanResponseReason.None
        };
    }

    /// <summary>
    /// Converts an internal ValidationSuggestion to the API enum.
    /// </summary>
    private static ValidateGoapPlanResponseSuggested_action ConvertToApiSuggestion(ValidationSuggestion suggestion)
    {
        return suggestion switch
        {
            ValidationSuggestion.Continue => ValidateGoapPlanResponseSuggested_action.Continue,
            ValidationSuggestion.Replan => ValidateGoapPlanResponseSuggested_action.Replan,
            ValidationSuggestion.Abort => ValidateGoapPlanResponseSuggested_action.Abort,
            _ => ValidateGoapPlanResponseSuggested_action.Continue
        };
    }

    #endregion
}
