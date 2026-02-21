// =============================================================================
// Options Evaluator
// Static utility for evaluating ABML options definitions to ActorOption values.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents;
using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.Bannou.BehaviorExpressions.Runtime;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Actor;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace BeyondImmersion.BannouService.Actor.Runtime;

/// <summary>
/// Static utility for evaluating ABML options definitions against a variable scope.
/// Results are stored in actor state for retrieval via /actor/query-options.
/// </summary>
public static class OptionsEvaluator
{
    /// <summary>
    /// Evaluates all options in the definition against the current scope.
    /// </summary>
    /// <param name="options">The options definition from the ABML document.</param>
    /// <param name="scope">The current variable scope with actor state.</param>
    /// <param name="evaluator">The expression evaluator for evaluating option expressions.</param>
    /// <param name="logger">Optional logger for warnings.</param>
    /// <returns>Dictionary mapping option type to evaluated options with timestamp.</returns>
    public static Dictionary<string, EvaluatedOptions> EvaluateAll(
        OptionsDefinition options,
        IVariableScope scope,
        IExpressionEvaluator evaluator,
        ILogger? logger = null)
    {

        var result = new Dictionary<string, EvaluatedOptions>();

        foreach (var (optionType, definitions) in options.OptionsByType)
        {
            var evaluatedOptions = new List<ActorOption>();

            foreach (var definition in definitions)
            {
                try
                {
                    var option = EvaluateOption(definition, scope, evaluator);
                    evaluatedOptions.Add(option);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex,
                        "Failed to evaluate option {ActionId} in {OptionType}: {Error}",
                        definition.ActionId, optionType, ex.Message);
                    // Continue with other options - don't fail entire evaluation
                }
            }

            result[optionType] = new EvaluatedOptions
            {
                Options = evaluatedOptions,
                ComputedAt = DateTimeOffset.UtcNow
            };
        }

        return result;
    }

    /// <summary>
    /// Evaluates a single option definition against the scope.
    /// </summary>
    private static ActorOption EvaluateOption(
        OptionDefinition definition,
        IVariableScope scope,
        IExpressionEvaluator evaluator)
    {
        // Evaluate preference (required)
        var preference = EvaluateFloat(definition.Preference, scope, evaluator, 0.5f);

        // Evaluate available (required)
        var available = EvaluateBool(definition.Available, scope, evaluator, true);

        // Evaluate optional fields
        float? risk = null;
        if (!string.IsNullOrEmpty(definition.Risk))
        {
            risk = EvaluateFloat(definition.Risk, scope, evaluator, 0f);
        }

        int? cooldownMs = null;
        if (!string.IsNullOrEmpty(definition.CooldownMs))
        {
            cooldownMs = EvaluateInt(definition.CooldownMs, scope, evaluator, 0);
        }

        return new ActorOption
        {
            ActionId = definition.ActionId,
            Preference = Math.Clamp(preference, 0f, 1f),
            Risk = risk.HasValue ? Math.Clamp(risk.Value, 0f, 1f) : null,
            Available = available,
            Requirements = definition.Requirements.ToList(),
            CooldownMs = cooldownMs,
            Tags = definition.Tags.ToList()
        };
    }

    /// <summary>
    /// Evaluates an expression string as a float.
    /// </summary>
    private static float EvaluateFloat(
        string expression,
        IVariableScope scope,
        IExpressionEvaluator evaluator,
        float defaultValue)
    {
        // Check if it's a literal
        if (float.TryParse(expression, NumberStyles.Float, CultureInfo.InvariantCulture, out var literal))
        {
            return literal;
        }

        // Check if it's an expression (contains ${...})
        if (expression.Contains("${"))
        {
            var result = evaluator.Evaluate(expression, scope);
            return ConvertToFloat(result, defaultValue);
        }

        // Try parsing as-is
        return defaultValue;
    }

    /// <summary>
    /// Evaluates an expression string as a bool.
    /// </summary>
    private static bool EvaluateBool(
        string expression,
        IVariableScope scope,
        IExpressionEvaluator evaluator,
        bool defaultValue)
    {
        // Check if it's a literal
        if (expression.Equals("true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (expression.Equals("false", StringComparison.OrdinalIgnoreCase))
            return false;

        // Check if it's an expression (contains ${...})
        if (expression.Contains("${"))
        {
            var result = evaluator.Evaluate(expression, scope);
            return ConvertToBool(result, defaultValue);
        }

        return defaultValue;
    }

    /// <summary>
    /// Evaluates an expression string as an int.
    /// </summary>
    private static int EvaluateInt(
        string expression,
        IVariableScope scope,
        IExpressionEvaluator evaluator,
        int defaultValue)
    {
        // Check if it's a literal
        if (int.TryParse(expression, out var literal))
        {
            return literal;
        }

        // Check if it's an expression (contains ${...})
        if (expression.Contains("${"))
        {
            var result = evaluator.Evaluate(expression, scope);
            return ConvertToInt(result, defaultValue);
        }

        return defaultValue;
    }

    private static float ConvertToFloat(object? value, float defaultValue)
    {
        return value switch
        {
            float f => f,
            double d => (float)d,
            int i => i,
            long l => l,
            decimal dec => (float)dec,
            string s when float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => defaultValue
        };
    }

    private static bool ConvertToBool(object? value, bool defaultValue)
    {
        return value switch
        {
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            string s => s.Equals("true", StringComparison.OrdinalIgnoreCase),
            _ => defaultValue
        };
    }

    private static int ConvertToInt(object? value, int defaultValue)
    {
        return value switch
        {
            int i => i,
            long l => (int)l,
            float f => (int)f,
            double d => (int)d,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => defaultValue
        };
    }
}

/// <summary>
/// Container for evaluated options with computation timestamp.
/// </summary>
public sealed class EvaluatedOptions
{
    /// <summary>
    /// The evaluated options.
    /// </summary>
    public required IReadOnlyList<ActorOption> Options { get; init; }

    /// <summary>
    /// When these options were computed.
    /// </summary>
    public required DateTimeOffset ComputedAt { get; init; }
}
