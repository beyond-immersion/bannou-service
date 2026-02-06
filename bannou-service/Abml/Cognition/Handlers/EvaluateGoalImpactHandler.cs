// =============================================================================
// Evaluate Goal Impact Handler (Cognition Stage 5)
// Evaluates how perceptions impact current goals.
// =============================================================================

using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Execution;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;

namespace BeyondImmersion.BannouService.Abml.Cognition.Handlers;

/// <summary>
/// ABML action handler for goal impact evaluation (Cognition Stage 5).
/// Evaluates how perceptions affect current goals and determines if replanning is needed.
/// </summary>
/// <remarks>
/// <para>
/// ABML usage:
/// <code>
/// - evaluate_goal_impact:
///     perceptions: "${filtered_perceptions}"
///     current_goals: "${agent.goals}"
///     current_plan: "${agent.current_plan}"
///     result_variable: "goal_updates"
/// </code>
/// </para>
/// </remarks>
public sealed class EvaluateGoalImpactHandler : IActionHandler
{
    private const string ACTION_NAME = "evaluate_goal_impact";

    /// <inheritdoc/>
    public bool CanHandle(ActionNode action)
        => action is DomainAction da && da.Name == ACTION_NAME;

    /// <inheritdoc/>
    public ValueTask<ActionResult> ExecuteAsync(
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

        // Get current goals
        var currentGoals = evaluatedParams.GetValueOrDefault("current_goals");
        var goalList = ExtractGoalList(currentGoals);

        // Get current plan (optional)
        var currentPlan = evaluatedParams.GetValueOrDefault("current_plan");

        // Evaluate impact
        var result = EvaluateImpact(perceptions, goalList, currentPlan);

        // Get result variable name
        var resultVariable = evaluatedParams.GetValueOrDefault("result_variable")?.ToString()
            ?? "goal_updates";

        // Store result in scope
        scope.SetValue(resultVariable, result);

        return ValueTask.FromResult(ActionResult.Continue);
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

    private static List<GoalInfo> ExtractGoalList(object? goals)
    {
        var result = new List<GoalInfo>();

        if (goals == null)
        {
            return result;
        }

        if (goals is string goalStr)
        {
            result.Add(new GoalInfo { Id = goalStr, Name = goalStr, Priority = 50 });
        }
        else if (goals is IEnumerable<string> goalStrings)
        {
            foreach (var s in goalStrings)
            {
                result.Add(new GoalInfo { Id = s, Name = s, Priority = 50 });
            }
        }
        else if (goals is IEnumerable<object> goalObjects)
        {
            foreach (var g in goalObjects)
            {
                if (g is string s)
                {
                    result.Add(new GoalInfo { Id = s, Name = s, Priority = 50 });
                }
                else if (g is IReadOnlyDictionary<string, object?> gDict)
                {
                    var id = gDict.GetValueOrDefault("id")?.ToString() ??
                            gDict.GetValueOrDefault("name")?.ToString() ?? "";
                    var name = gDict.GetValueOrDefault("name")?.ToString() ?? id;
                    var priority = 50;
                    if (gDict.TryGetValue("priority", out var p))
                    {
                        priority = Convert.ToInt32(p);
                    }
                    result.Add(new GoalInfo { Id = id, Name = name, Priority = priority });
                }
            }
        }

        return result;
    }

    private static GoalImpactResult EvaluateImpact(
        IReadOnlyList<Perception> perceptions,
        List<GoalInfo> goals,
        object? currentPlan)
    {
        if (perceptions.Count == 0 || goals.Count == 0)
        {
            return new GoalImpactResult
            {
                RequiresReplan = false,
                AffectedGoals = [],
                Urgency = 0f,
                Message = "No perceptions or goals to evaluate"
            };
        }

        var affectedGoals = new List<string>();
        var maxUrgency = 0f;
        var requiresReplan = false;
        var messages = new List<string>();

        foreach (var perception in perceptions)
        {
            foreach (var goal in goals)
            {
                var impact = EvaluatePerceptionGoalImpact(perception, goal);

                if (impact > 0.3f) // Threshold for "affected"
                {
                    if (!affectedGoals.Contains(goal.Id))
                    {
                        affectedGoals.Add(goal.Id);
                    }

                    // High-priority goals with high impact require replanning
                    var effectiveUrgency = impact * (goal.Priority / 100f);
                    if (effectiveUrgency > maxUrgency)
                    {
                        maxUrgency = effectiveUrgency;
                    }

                    // Threats always trigger replan consideration
                    if (perception.Category.Equals("threat", StringComparison.OrdinalIgnoreCase))
                    {
                        requiresReplan = true;
                        messages.Add($"Threat impacts goal '{goal.Name}'");
                    }
                    else if (impact > 0.6f)
                    {
                        requiresReplan = true;
                        messages.Add($"Significant impact on goal '{goal.Name}'");
                    }
                }
            }
        }

        // Check if perceptions invalidate current plan
        if (currentPlan != null && !requiresReplan)
        {
            var planInvalidated = CheckPlanInvalidation(perceptions, currentPlan);
            if (planInvalidated)
            {
                requiresReplan = true;
                messages.Add("Current plan invalidated by perceptions");
            }
        }

        return new GoalImpactResult
        {
            RequiresReplan = requiresReplan,
            AffectedGoals = affectedGoals,
            Urgency = Math.Clamp(maxUrgency, 0f, 1f),
            Message = messages.Count > 0 ? string.Join("; ", messages) : null
        };
    }

    private static float EvaluatePerceptionGoalImpact(Perception perception, GoalInfo goal)
    {
        var impact = 0f;

        // Category-based impact
        if (perception.Category.Equals("threat", StringComparison.OrdinalIgnoreCase))
        {
            // Threats impact survival-related goals heavily
            if (goal.Name.Contains("surviv", StringComparison.OrdinalIgnoreCase) ||
                goal.Name.Contains("safety", StringComparison.OrdinalIgnoreCase) ||
                goal.Name.Contains("health", StringComparison.OrdinalIgnoreCase))
            {
                impact = 0.9f;
            }
            else
            {
                impact = 0.5f; // Threats impact all goals somewhat
            }
        }
        else if (perception.Category.Equals("social", StringComparison.OrdinalIgnoreCase))
        {
            // Social perceptions impact relationship goals
            if (goal.Name.Contains("friend", StringComparison.OrdinalIgnoreCase) ||
                goal.Name.Contains("social", StringComparison.OrdinalIgnoreCase) ||
                goal.Name.Contains("relation", StringComparison.OrdinalIgnoreCase))
            {
                impact = 0.7f;
            }
        }

        // Content-based impact (keyword matching)
        if (perception.Content.Contains(goal.Name, StringComparison.OrdinalIgnoreCase))
        {
            impact = Math.Max(impact, 0.6f);
        }

        // Urgency amplifies impact
        impact = Math.Min(1f, impact * (0.5f + 0.5f * perception.Urgency));

        return impact;
    }

    private static bool CheckPlanInvalidation(IReadOnlyList<Perception> perceptions, object currentPlan)
    {
        // Check if any perception indicates plan failure
        foreach (var perception in perceptions)
        {
            // High-urgency threats typically invalidate plans
            if (perception.Category.Equals("threat", StringComparison.OrdinalIgnoreCase) &&
                perception.Urgency > 0.7f)
            {
                return true;
            }

            // Check for specific invalidation signals in perception data
            if (perception.Data.TryGetValue("invalidates_plan", out var invalidates) &&
                Convert.ToBoolean(invalidates))
            {
                return true;
            }

            if (perception.Data.TryGetValue("blocked", out var blocked) &&
                Convert.ToBoolean(blocked))
            {
                return true;
            }
        }

        return false;
    }

    public ValueTask<ActionResult> ExecuteAsync(ActionNode action, Execution.ExecutionContext context, CancellationToken ct) => throw new NotImplementedException();

    /// <summary>
    /// Internal goal info structure for evaluation.
    /// </summary>
    private sealed class GoalInfo
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public int Priority { get; init; } = 50;
    }
}
