// =============================================================================
// Filter Attention Handler (Cognition Stage 1)
// Filters perceptions based on attention budget and priority weights.
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Cognition;
using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Execution;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;

namespace BeyondImmersion.Bannou.Behavior.Handlers;

/// <summary>
/// ABML action handler for attention filtering (Cognition Stage 1).
/// Filters incoming perceptions based on priority weights and attention budget.
/// </summary>
/// <remarks>
/// <para>
/// ABML usage:
/// <code>
/// - filter_attention:
///     input: "${perception.raw_data}"
///     attention_budget: 100
///     max_perceptions: 10
///     priority_weights:
///       threat: 10.0
///       novelty: 5.0
///       social: 3.0
///       routine: 1.0
///     threat_fast_track: true
///     threat_threshold: 0.8
///     result_variable: "filtered_perceptions"
///     fast_track_variable: "fast_track_perceptions"
/// </code>
/// </para>
/// </remarks>
public sealed class FilterAttentionHandler : IActionHandler
{
    private const string ACTION_NAME = "filter_attention";

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

        // Get input perceptions
        var rawInput = evaluatedParams.GetValueOrDefault("input");
        var perceptions = ConvertToPerceptions(rawInput);

        // Build attention weights from parameters
        var weights = BuildAttentionWeights(evaluatedParams);

        // Build attention budget
        var budget = BuildAttentionBudget(evaluatedParams);

        // Get result variable names
        var resultVariable = evaluatedParams.GetValueOrDefault("result_variable")?.ToString()
            ?? "filtered_perceptions";
        var fastTrackVariable = evaluatedParams.GetValueOrDefault("fast_track_variable")?.ToString()
            ?? "fast_track_perceptions";

        // Apply attention filtering
        var result = FilterPerceptions(perceptions, weights, budget);

        // Store results in scope
        scope.SetValue(resultVariable, result.FilteredPerceptions);
        scope.SetValue(fastTrackVariable, result.FastTrackPerceptions);

        return ValueTask.FromResult(ActionResult.Continue);
    }

    private static List<Perception> ConvertToPerceptions(object? input)
    {
        if (input == null)
        {
            return [];
        }

        if (input is IReadOnlyList<Perception> perceptionList)
        {
            return perceptionList.ToList();
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

        if (input is IReadOnlyDictionary<string, object?> singleDict)
        {
            return [Perception.FromDictionary(singleDict)];
        }

        return [];
    }

    private static AttentionWeights BuildAttentionWeights(IReadOnlyDictionary<string, object?> parameters)
    {
        var threatWeight = 10.0f;
        var noveltyWeight = 5.0f;
        var socialWeight = 3.0f;
        var routineWeight = 1.0f;
        var threatFastTrack = false;
        var threatFastTrackThreshold = 0.8f;

        // Extract priority_weights if present
        if (parameters.TryGetValue("priority_weights", out var weightsObj) &&
            weightsObj is IReadOnlyDictionary<string, object?> weightsDict)
        {
            if (weightsDict.TryGetValue("threat", out var t))
                threatWeight = Convert.ToSingle(t);
            if (weightsDict.TryGetValue("novelty", out var n))
                noveltyWeight = Convert.ToSingle(n);
            if (weightsDict.TryGetValue("social", out var s))
                socialWeight = Convert.ToSingle(s);
            if (weightsDict.TryGetValue("routine", out var r))
                routineWeight = Convert.ToSingle(r);
        }

        // Extract threat fast-track settings
        if (parameters.TryGetValue("threat_fast_track", out var fastTrackObj))
        {
            threatFastTrack = Convert.ToBoolean(fastTrackObj);
        }
        if (parameters.TryGetValue("threat_threshold", out var thresholdObj))
        {
            threatFastTrackThreshold = Convert.ToSingle(thresholdObj);
        }

        return new AttentionWeights
        {
            ThreatWeight = threatWeight,
            NoveltyWeight = noveltyWeight,
            SocialWeight = socialWeight,
            RoutineWeight = routineWeight,
            ThreatFastTrack = threatFastTrack,
            ThreatFastTrackThreshold = threatFastTrackThreshold
        };
    }

    private static AttentionBudget BuildAttentionBudget(IReadOnlyDictionary<string, object?> parameters)
    {
        var totalUnits = 100f;
        var maxPerceptions = 10;

        if (parameters.TryGetValue("attention_budget", out var budgetObj))
        {
            totalUnits = Convert.ToSingle(budgetObj);
        }
        if (parameters.TryGetValue("max_perceptions", out var maxObj))
        {
            maxPerceptions = Convert.ToInt32(maxObj);
        }

        return new AttentionBudget
        {
            TotalUnits = totalUnits,
            MaxPerceptions = maxPerceptions
        };
    }

    private static FilteredPerceptionsResult FilterPerceptions(
        List<Perception> perceptions,
        AttentionWeights weights,
        AttentionBudget budget)
    {
        if (perceptions.Count == 0)
        {
            return new FilteredPerceptionsResult
            {
                FilteredPerceptions = [],
                FastTrackPerceptions = [],
                DroppedPerceptions = [],
                RemainingBudget = budget.TotalUnits
            };
        }

        var fastTrack = new List<Perception>();
        var candidates = new List<Perception>();
        var dropped = new List<Perception>();

        // First pass: separate fast-track threats
        foreach (var perception in perceptions)
        {
            // Calculate priority based on category weight and urgency
            var categoryWeight = weights.GetWeight(perception.Category);
            perception.Priority = perception.Urgency * categoryWeight;

            // Check for threat fast-track
            if (weights.ThreatFastTrack &&
                perception.Category.Equals("threat", StringComparison.OrdinalIgnoreCase) &&
                perception.Urgency >= weights.ThreatFastTrackThreshold)
            {
                fastTrack.Add(perception);
            }
            else
            {
                candidates.Add(perception);
            }
        }

        // Sort candidates by priority (highest first)
        candidates.Sort((a, b) => b.Priority.CompareTo(a.Priority));

        // Apply budget constraints
        var filtered = new List<Perception>();
        var remainingBudget = budget.TotalUnits;

        foreach (var perception in candidates)
        {
            if (filtered.Count >= budget.MaxPerceptions)
            {
                dropped.Add(perception);
                continue;
            }

            // Each perception consumes budget proportional to its weight
            var cost = weights.GetWeight(perception.Category);
            if (remainingBudget >= cost)
            {
                filtered.Add(perception);
                remainingBudget -= cost;
            }
            else
            {
                dropped.Add(perception);
            }
        }

        return new FilteredPerceptionsResult
        {
            FilteredPerceptions = filtered,
            FastTrackPerceptions = fastTrack,
            DroppedPerceptions = dropped,
            RemainingBudget = remainingBudget
        };
    }
}
