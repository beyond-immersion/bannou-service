// ═══════════════════════════════════════════════════════════════════════════
// GOAP Condition
// Parses and evaluates literal conditions for GOAP planning.
// ═══════════════════════════════════════════════════════════════════════════

using System.Globalization;

namespace BeyondImmersion.Bannou.Server.Behavior.Goap;

/// <summary>
/// Comparison operators for GOAP conditions.
/// </summary>
public enum ComparisonOperator
{
    /// <summary>Equals (==).</summary>
    Equal,

    /// <summary>Not equals (!=).</summary>
    NotEqual,

    /// <summary>Greater than (>).</summary>
    GreaterThan,

    /// <summary>Greater than or equal (>=).</summary>
    GreaterThanOrEqual,

    /// <summary>Less than (&lt;).</summary>
    LessThan,

    /// <summary>Less than or equal (&lt;=).</summary>
    LessThanOrEqual
}

/// <summary>
/// Represents a GOAP condition that compares a world state property to a literal value.
/// Conditions use literal values only, not expressions.
/// </summary>
public sealed class GoapCondition
{
    /// <summary>
    /// The comparison operator.
    /// </summary>
    public ComparisonOperator Operator { get; }

    /// <summary>
    /// The target value to compare against.
    /// </summary>
    public object TargetValue { get; }

    /// <summary>
    /// The numeric target value (for heuristic calculation).
    /// </summary>
    public float? NumericTarget { get; }

    /// <summary>
    /// Creates a new GOAP condition.
    /// </summary>
    /// <param name="op">Comparison operator.</param>
    /// <param name="targetValue">Value to compare against.</param>
    public GoapCondition(ComparisonOperator op, object targetValue)
    {
        Operator = op;
        TargetValue = targetValue;
        NumericTarget = targetValue switch
        {
            float f => f,
            double d => (float)d,
            int i => i,
            long l => l,
            decimal dec => (float)dec,
            _ => null
        };
    }

    /// <summary>
    /// Parses a condition string into a GoapCondition.
    /// </summary>
    /// <param name="conditionString">Condition like "> 0.6", ">= 5", "== true", "!= 'idle'".</param>
    /// <returns>Parsed condition.</returns>
    /// <exception cref="FormatException">If the condition string is invalid.</exception>
    public static GoapCondition Parse(string conditionString)
    {

        var trimmed = conditionString.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new FormatException("Condition string cannot be empty");
        }

        // Parse operator and value
        var (op, valueStr) = ParseOperatorAndValue(trimmed);
        var value = ParseValue(valueStr);

        return new GoapCondition(op, value);
    }

    /// <summary>
    /// Tries to parse a condition string into a GoapCondition.
    /// </summary>
    /// <param name="conditionString">Condition string to parse.</param>
    /// <param name="condition">Parsed condition if successful.</param>
    /// <returns>True if parsing succeeded.</returns>
    public static bool TryParse(string conditionString, out GoapCondition? condition)
    {
        condition = null;
        if (string.IsNullOrWhiteSpace(conditionString))
        {
            return false;
        }

        try
        {
            condition = Parse(conditionString);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Evaluates this condition against a world state value.
    /// </summary>
    /// <param name="actualValue">The actual value from world state.</param>
    /// <returns>True if the condition is satisfied.</returns>
    public bool Evaluate(object? actualValue)
    {
        // Handle null actual values - for numeric conditions, treat null as 0
        if (actualValue == null)
        {
            if (NumericTarget.HasValue)
            {
                return EvaluateNumeric(0f, NumericTarget.Value);
            }
            return Operator == ComparisonOperator.NotEqual && TargetValue != null;
        }

        // Try numeric comparison first
        if (TryGetNumeric(actualValue, out var actualNumeric) && NumericTarget.HasValue)
        {
            return EvaluateNumeric(actualNumeric, NumericTarget.Value);
        }

        // Try boolean comparison
        if (actualValue is bool actualBool && TargetValue is bool targetBool)
        {
            return EvaluateBoolean(actualBool, targetBool);
        }

        // Fall back to string comparison
        var actualStr = actualValue.ToString() ?? "";
        var targetStr = TargetValue.ToString() ?? "";
        return EvaluateString(actualStr, targetStr);
    }

    /// <summary>
    /// Calculates the distance from a value to satisfying this condition.
    /// Used as heuristic for A* search.
    /// </summary>
    /// <param name="actualValue">The actual value from world state.</param>
    /// <returns>Distance (0 if satisfied, positive otherwise).</returns>
    public float Distance(object? actualValue)
    {
        if (Evaluate(actualValue))
        {
            return 0f;
        }

        // For numeric conditions, return the difference
        if (TryGetNumeric(actualValue, out var actualNumeric) && NumericTarget.HasValue)
        {
            var diff = Math.Abs(actualNumeric - NumericTarget.Value);
            return Operator switch
            {
                ComparisonOperator.Equal => diff,
                ComparisonOperator.NotEqual => diff == 0 ? 1f : 0f,
                ComparisonOperator.GreaterThan => NumericTarget.Value - actualNumeric + 0.01f,
                ComparisonOperator.GreaterThanOrEqual => NumericTarget.Value - actualNumeric,
                ComparisonOperator.LessThan => actualNumeric - NumericTarget.Value + 0.01f,
                ComparisonOperator.LessThanOrEqual => actualNumeric - NumericTarget.Value,
                _ => diff
            };
        }

        // For non-numeric, return 1 if not satisfied
        return 1f;
    }

    private bool EvaluateNumeric(float actual, float target)
    {
        return Operator switch
        {
            ComparisonOperator.Equal => Math.Abs(actual - target) < float.Epsilon,
            ComparisonOperator.NotEqual => Math.Abs(actual - target) >= float.Epsilon,
            ComparisonOperator.GreaterThan => actual > target,
            ComparisonOperator.GreaterThanOrEqual => actual >= target,
            ComparisonOperator.LessThan => actual < target,
            ComparisonOperator.LessThanOrEqual => actual <= target,
            _ => false
        };
    }

    private bool EvaluateBoolean(bool actual, bool target)
    {
        return Operator switch
        {
            ComparisonOperator.Equal => actual == target,
            ComparisonOperator.NotEqual => actual != target,
            _ => false // Other operators don't make sense for booleans
        };
    }

    private bool EvaluateString(string actual, string target)
    {
        return Operator switch
        {
            ComparisonOperator.Equal => string.Equals(actual, target, StringComparison.Ordinal),
            ComparisonOperator.NotEqual => !string.Equals(actual, target, StringComparison.Ordinal),
            ComparisonOperator.GreaterThan => string.Compare(actual, target, StringComparison.Ordinal) > 0,
            ComparisonOperator.GreaterThanOrEqual => string.Compare(actual, target, StringComparison.Ordinal) >= 0,
            ComparisonOperator.LessThan => string.Compare(actual, target, StringComparison.Ordinal) < 0,
            ComparisonOperator.LessThanOrEqual => string.Compare(actual, target, StringComparison.Ordinal) <= 0,
            _ => false
        };
    }

    private static bool TryGetNumeric(object? value, out float result)
    {
        result = 0f;
        if (value == null) return false;

        switch (value)
        {
            case float f:
                result = f;
                return true;
            case double d:
                result = (float)d;
                return true;
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
            case decimal dec:
                result = (float)dec;
                return true;
            case string s when float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                result = parsed;
                return true;
            default:
                return false;
        }
    }

    private static (ComparisonOperator op, string value) ParseOperatorAndValue(string input)
    {
        // Order matters: check longer operators first
        if (input.StartsWith(">=", StringComparison.Ordinal))
        {
            return (ComparisonOperator.GreaterThanOrEqual, input[2..].Trim());
        }
        if (input.StartsWith("<=", StringComparison.Ordinal))
        {
            return (ComparisonOperator.LessThanOrEqual, input[2..].Trim());
        }
        if (input.StartsWith("==", StringComparison.Ordinal))
        {
            return (ComparisonOperator.Equal, input[2..].Trim());
        }
        if (input.StartsWith("!=", StringComparison.Ordinal))
        {
            return (ComparisonOperator.NotEqual, input[2..].Trim());
        }
        if (input.StartsWith(">", StringComparison.Ordinal))
        {
            return (ComparisonOperator.GreaterThan, input[1..].Trim());
        }
        if (input.StartsWith("<", StringComparison.Ordinal))
        {
            return (ComparisonOperator.LessThan, input[1..].Trim());
        }

        // No operator found - GOAP conditions require an explicit operator
        throw new FormatException($"Invalid condition format: '{input}'. Conditions must start with an operator (==, !=, >, >=, <, <=).");
    }

    private static object ParseValue(string valueStr)
    {
        // Try boolean
        if (valueStr.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (valueStr.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Try integer
        if (int.TryParse(valueStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            return intValue;
        }

        // Try float
        if (float.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
        {
            return floatValue;
        }

        // Strip quotes if present
        if ((valueStr.StartsWith("'") && valueStr.EndsWith("'")) ||
            (valueStr.StartsWith("\"") && valueStr.EndsWith("\"")))
        {
            return valueStr[1..^1];
        }

        // Return as string
        return valueStr;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        var opStr = Operator switch
        {
            ComparisonOperator.Equal => "==",
            ComparisonOperator.NotEqual => "!=",
            ComparisonOperator.GreaterThan => ">",
            ComparisonOperator.GreaterThanOrEqual => ">=",
            ComparisonOperator.LessThan => "<",
            ComparisonOperator.LessThanOrEqual => "<=",
            _ => "?"
        };
        return $"{opStr} {TargetValue}";
    }
}
