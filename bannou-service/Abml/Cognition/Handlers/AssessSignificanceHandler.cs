// =============================================================================
// Assess Significance Handler (Cognition Stage 3)
// Assesses the significance of perceptions for memory storage decisions.
// =============================================================================

using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Execution;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;

namespace BeyondImmersion.BannouService.Abml.Cognition.Handlers;

/// <summary>
/// ABML action handler for significance assessment (Cognition Stage 3).
/// Computes weighted significance score for a perception.
/// </summary>
/// <remarks>
/// <para>
/// ABML usage:
/// <code>
/// - assess_significance:
///     perception: "${perception}"
///     memories: "${relevant_memories}"
///     relationships: "${agent.relationships}"
///     personality: "${agent.personality}"
///     current_goals: "${agent.goals}"
///     weights:
///       emotional: 0.4
///       goal_relevance: 0.4
///       relationship: 0.2
///     threshold: 0.7
///     result_variable: "significance_score"
/// </code>
/// </para>
/// </remarks>
public sealed class AssessSignificanceHandler : IActionHandler
{
    private const string ACTION_NAME = "assess_significance";

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

        // Get perception
        var perceptionObj = evaluatedParams.GetValueOrDefault("perception");
        var perception = ConvertToPerception(perceptionObj) ?? throw new InvalidOperationException("assess_significance requires perception parameter");

        // Get context data
        var memories = ConvertToMemories(evaluatedParams.GetValueOrDefault("memories"));
        var relationships = evaluatedParams.GetValueOrDefault("relationships");
        var personality = evaluatedParams.GetValueOrDefault("personality");
        var currentGoals = evaluatedParams.GetValueOrDefault("current_goals");

        // Build significance weights
        var weights = BuildSignificanceWeights(evaluatedParams);

        // Compute factors
        var emotionalImpact = ComputeEmotionalImpact(perception, personality);
        var goalRelevance = ComputeGoalRelevance(perception, currentGoals);
        var relationshipFactor = ComputeRelationshipFactor(perception, relationships);

        // Compute weighted score
        var totalScore = weights.ComputeScore(emotionalImpact, goalRelevance, relationshipFactor);

        // Build result
        var score = new SignificanceScore
        {
            EmotionalImpact = emotionalImpact,
            GoalRelevance = goalRelevance,
            RelationshipFactor = relationshipFactor,
            TotalScore = totalScore,
            StorageThreshold = weights.StorageThreshold
        };

        // Get result variable name
        var resultVariable = evaluatedParams.GetValueOrDefault("result_variable")?.ToString()
            ?? "significance_score";

        // Store result in scope
        scope.SetValue(resultVariable, score);

        return ValueTask.FromResult(ActionResult.Continue);
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

    private static SignificanceWeights BuildSignificanceWeights(IReadOnlyDictionary<string, object?> parameters)
    {
        var emotionalWeight = 0.4f;
        var goalRelevanceWeight = 0.4f;
        var relationshipWeight = 0.2f;
        var threshold = 0.7f;

        // Extract weights if present
        if (parameters.TryGetValue("weights", out var weightsObj) &&
            weightsObj is IReadOnlyDictionary<string, object?> weightsDict)
        {
            if (weightsDict.TryGetValue("emotional", out var e))
                emotionalWeight = Convert.ToSingle(e);
            if (weightsDict.TryGetValue("goal_relevance", out var g))
                goalRelevanceWeight = Convert.ToSingle(g);
            if (weightsDict.TryGetValue("relationship", out var r))
                relationshipWeight = Convert.ToSingle(r);
        }

        // Extract threshold
        if (parameters.TryGetValue("threshold", out var thresholdObj))
        {
            threshold = Convert.ToSingle(thresholdObj);
        }

        return new SignificanceWeights
        {
            EmotionalWeight = emotionalWeight,
            GoalRelevanceWeight = goalRelevanceWeight,
            RelationshipWeight = relationshipWeight,
            StorageThreshold = threshold
        };
    }

    /// <summary>
    /// Computes emotional impact based on perception category and urgency.
    /// </summary>
    private static float ComputeEmotionalImpact(Perception perception, object? personality)
    {
        // Base emotional impact from category
        var baseImpact = perception.Category.ToLowerInvariant() switch
        {
            "threat" => 0.9f,
            "novelty" => 0.6f,
            "social" => 0.5f,
            "routine" => 0.1f,
            _ => 0.3f
        };

        // Modify by urgency
        var impact = baseImpact * (0.5f + 0.5f * perception.Urgency);

        // Apply personality modifiers if available
        if (personality is IReadOnlyDictionary<string, object?> traits)
        {
            // Anxious personalities have higher emotional response to threats
            if (traits.TryGetValue("anxious", out var anxious) &&
                perception.Category.Equals("threat", StringComparison.OrdinalIgnoreCase))
            {
                impact = Math.Min(1.0f, impact * (1f + Convert.ToSingle(anxious) * 0.3f));
            }

            // Curious personalities have higher response to novelty
            if (traits.TryGetValue("curious", out var curious) &&
                perception.Category.Equals("novelty", StringComparison.OrdinalIgnoreCase))
            {
                impact = Math.Min(1.0f, impact * (1f + Convert.ToSingle(curious) * 0.3f));
            }
        }

        return Math.Clamp(impact, 0f, 1f);
    }

    /// <summary>
    /// Computes goal relevance based on perception content and current goals.
    /// </summary>
    private static float ComputeGoalRelevance(Perception perception, object? currentGoals)
    {
        if (currentGoals == null)
        {
            return 0.3f; // Base relevance when no goals specified
        }

        var relevance = 0f;
        var goalList = ExtractGoalList(currentGoals);

        // Check if perception relates to any goal
        foreach (var goal in goalList)
        {
            // Simple keyword matching for MVP
            if (perception.Content.Contains(goal, StringComparison.OrdinalIgnoreCase) ||
                perception.Data.Keys.Any(k => k.Contains(goal, StringComparison.OrdinalIgnoreCase)))
            {
                relevance = Math.Max(relevance, 0.8f);
            }

            // Category-based relevance
            if (perception.Category.Equals("threat", StringComparison.OrdinalIgnoreCase) &&
                (goal.Contains("survive", StringComparison.OrdinalIgnoreCase) ||
                goal.Contains("safety", StringComparison.OrdinalIgnoreCase)))
            {
                relevance = Math.Max(relevance, 0.9f);
            }
        }

        // Urgency boosts goal relevance
        return Math.Clamp(relevance + perception.Urgency * 0.2f, 0f, 1f);
    }

    /// <summary>
    /// Computes relationship factor based on perception source.
    /// </summary>
    private static float ComputeRelationshipFactor(Perception perception, object? relationships)
    {
        if (relationships == null || string.IsNullOrEmpty(perception.Source))
        {
            return 0.2f; // Base relationship factor
        }

        // Try to find relationship strength with perception source
        if (relationships is IReadOnlyDictionary<string, object?> relDict)
        {
            if (relDict.TryGetValue(perception.Source, out var relValue))
            {
                // Relationship value could be a strength/affinity score
                if (relValue is float strength)
                {
                    return Math.Clamp(Math.Abs(strength), 0f, 1f);
                }
                if (relValue is IReadOnlyDictionary<string, object?> relData &&
                    relData.TryGetValue("strength", out var s))
                {
                    return Math.Clamp(Math.Abs(Convert.ToSingle(s)), 0f, 1f);
                }
            }
        }

        // Social perceptions have higher relationship factor
        if (perception.Category.Equals("social", StringComparison.OrdinalIgnoreCase))
        {
            return 0.5f;
        }

        return 0.2f;
    }

    private static List<string> ExtractGoalList(object goals)
    {
        var result = new List<string>();

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
                else if (g is IReadOnlyDictionary<string, object?> gDict &&
                        gDict.TryGetValue("name", out var name))
                {
                    result.Add(name?.ToString() ?? "");
                }
            }
        }

        return result;
    }

    public ValueTask<ActionResult> ExecuteAsync(ActionNode action, Execution.ExecutionContext context, CancellationToken ct) => throw new NotImplementedException();
}
