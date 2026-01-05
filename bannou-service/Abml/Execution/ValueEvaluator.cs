// ═══════════════════════════════════════════════════════════════════════════
// ABML Value Evaluator
// Shared utility for evaluating action parameter values.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.Abml.Runtime;
using System.Globalization;

namespace BeyondImmersion.BannouService.Abml.Execution;

/// <summary>
/// Utility for evaluating action parameter values.
/// Handles the distinction between literal values and expression values.
/// </summary>
/// <remarks>
/// <para>
/// Values containing <c>${...}</c> are evaluated as expressions.
/// Plain values are parsed as literals (numbers, booleans, null, or strings).
/// </para>
/// <para>
/// Examples:
/// <list type="bullet">
/// <item><c>"Hello"</c> → string "Hello"</item>
/// <item><c>"42"</c> → int 42</item>
/// <item><c>"3.14"</c> → double 3.14</item>
/// <item><c>"true"</c> → bool true</item>
/// <item><c>"null"</c> → null</item>
/// <item><c>"${x + 1}"</c> → evaluated expression result</item>
/// <item><c>"Hello ${name}"</c> → interpolated template result</item>
/// </list>
/// </para>
/// </remarks>
public static class ValueEvaluator
{
    /// <summary>
    /// Evaluates a value string, handling both expressions and literals.
    /// </summary>
    /// <param name="valueStr">The value string to evaluate.</param>
    /// <param name="scope">The variable scope for expression evaluation.</param>
    /// <param name="evaluator">The expression evaluator.</param>
    /// <returns>The evaluated value.</returns>
    public static object? Evaluate(string valueStr, IVariableScope scope, IExpressionEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(valueStr);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(evaluator);

        // If the value contains ${...}, evaluate as expression/template
        if (valueStr.Contains("${"))
        {
            return evaluator.Evaluate(valueStr, scope);
        }

        return ParseLiteral(valueStr);
    }

    /// <summary>
    /// Parses a literal value string into the appropriate type.
    /// </summary>
    /// <param name="valueStr">The value string to parse.</param>
    /// <returns>The parsed value.</returns>
    public static object? ParseLiteral(string valueStr)
    {
        // Try to parse as number
        if (double.TryParse(valueStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
        {
            // Return as int if it's a whole number within int range
            if (num == Math.Floor(num) && num is >= int.MinValue and <= int.MaxValue)
            {
                return (int)num;
            }
            return num;
        }

        // Check for boolean literals
        if (valueStr.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (valueStr.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Check for null literal
        if (valueStr.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Otherwise, treat as string literal
        return valueStr;
    }

    /// <summary>
    /// Evaluates a dictionary of parameters, evaluating expressions in values.
    /// </summary>
    /// <param name="parameters">The parameters to evaluate.</param>
    /// <param name="scope">The variable scope for expression evaluation.</param>
    /// <param name="evaluator">The expression evaluator.</param>
    /// <returns>Dictionary with evaluated values.</returns>
    public static IReadOnlyDictionary<string, object?> EvaluateParameters(
        IReadOnlyDictionary<string, object?> parameters,
        IVariableScope scope,
        IExpressionEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(evaluator);

        var result = new Dictionary<string, object?>(parameters.Count);

        foreach (var (key, value) in parameters)
        {
            result[key] = value switch
            {
                string s => Evaluate(s, scope, evaluator),
                _ => value
            };
        }

        return result;
    }
}
