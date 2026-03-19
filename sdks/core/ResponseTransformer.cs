#nullable enable

using System.Text.Json;

namespace BeyondImmersion.Bannou.Core;

/// <summary>
/// Pure-computation engine that transforms a raw API response into a final result
/// based on declarative transformation rules.
/// </summary>
/// <remarks>
/// <para>
/// No dependencies, no state, no DI — this is a static utility that takes inputs
/// and produces outputs. The transformation rules are defined by the prebound API
/// creator who knows both what the API returns and what the consumer needs.
/// </para>
/// <para>
/// Evaluation order:
/// 1. Check transient failure status codes (transport-level, before rules)
/// 2. Evaluate rules in order (first match wins)
/// 3. If no rules match, pass through the raw response
/// </para>
/// </remarks>
public static class ResponseTransformer
{
    /// <summary>
    /// Default HTTP status codes treated as transient (retryable) failures.
    /// </summary>
    private static readonly HashSet<int> DefaultTransientStatusCodes = new()
    {
        408, // Request Timeout
        429, // Too Many Requests
        502, // Bad Gateway
        503, // Service Unavailable
        504  // Gateway Timeout
    };

    /// <summary>
    /// Transforms a raw API response using the given transformation rules.
    /// </summary>
    /// <param name="statusCode">HTTP status code from the raw API response.</param>
    /// <param name="responseBody">Response body from the raw API response (may be null).</param>
    /// <param name="transformation">Transformation rules to apply. Null = pass through.</param>
    /// <returns>The transformation result with final status code, payload, and outcome.</returns>
    public static TransformationResult Transform(
        int statusCode,
        string? responseBody,
        ResponseTransformation? transformation)
    {
        if (transformation == null)
        {
            return new TransformationResult
            {
                StatusCode = statusCode,
                Payload = responseBody,
                Outcome = TransformationOutcome.PassThrough
            };
        }

        // Step 1: Check transient failure status codes (transport-level)
        var transientCodes = transformation.TransientFailureStatusCodes is { Count: > 0 }
            ? new HashSet<int>(transformation.TransientFailureStatusCodes)
            : DefaultTransientStatusCodes;

        if (transientCodes.Contains(statusCode))
        {
            return new TransformationResult
            {
                StatusCode = statusCode,
                Payload = null,
                Outcome = TransformationOutcome.TransientFailure
            };
        }

        // Step 2: Evaluate rules in order (first match wins)
        if (transformation.Rules is { Count: > 0 })
        {
            JsonDocument? document = null;
            if (!string.IsNullOrEmpty(responseBody))
            {
                try
                {
                    document = JsonDocument.Parse(responseBody);
                }
                catch (JsonException)
                {
                    // Can't parse response as JSON — rules that inspect JSON paths will fail
                    // to match, status code rules can still fire
                }
            }

            try
            {
                foreach (var rule in transformation.Rules)
                {
                    if (AllConditionsMatch(statusCode, document, rule.Conditions))
                    {
                        return new TransformationResult
                        {
                            StatusCode = rule.StatusCode,
                            Payload = rule.Payload ?? responseBody,
                            Outcome = TransformationOutcome.Transformed,
                            MatchedRuleDescription = rule.Description
                        };
                    }
                }
            }
            finally
            {
                document?.Dispose();
            }
        }

        // Step 3: No rules matched — pass through
        return new TransformationResult
        {
            StatusCode = statusCode,
            Payload = responseBody,
            Outcome = TransformationOutcome.PassThrough
        };
    }

    /// <summary>
    /// Evaluates whether all conditions in a rule match the given response.
    /// An empty or null conditions list always matches (unconditional rule).
    /// </summary>
    private static bool AllConditionsMatch(
        int statusCode,
        JsonDocument? document,
        List<TransformationCondition>? conditions)
    {
        if (conditions is not { Count: > 0 })
        {
            return true;
        }

        foreach (var condition in conditions)
        {
            if (!EvaluateCondition(statusCode, document, condition))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Evaluates a single condition against the response.
    /// </summary>
    private static bool EvaluateCondition(
        int statusCode,
        JsonDocument? document,
        TransformationCondition condition)
    {
        return condition.Type switch
        {
            TransformationConditionType.StatusCodeIn => EvaluateStatusCodeIn(statusCode, condition),
            TransformationConditionType.JsonPathEquals => EvaluateJsonPathComparison(document, condition, CompareEquals),
            TransformationConditionType.JsonPathNotEquals => EvaluateJsonPathComparison(document, condition, CompareNotEquals),
            TransformationConditionType.JsonPathExists => EvaluateJsonPathExists(document, condition, shouldExist: true),
            TransformationConditionType.JsonPathNotExists => EvaluateJsonPathExists(document, condition, shouldExist: false),
            TransformationConditionType.JsonPathGreaterThan => EvaluateJsonPathNumeric(document, condition, (a, b) => a > b),
            TransformationConditionType.JsonPathLessThan => EvaluateJsonPathNumeric(document, condition, (a, b) => a < b),
            TransformationConditionType.JsonPathContains => EvaluateJsonPathContains(document, condition),
            _ => false
        };
    }

    private static bool CompareEquals(string? actual, string? expected)
        => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static bool CompareNotEquals(string? actual, string? expected)
        => !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static bool EvaluateStatusCodeIn(int statusCode, TransformationCondition condition)
        => condition.StatusCodes is { Count: > 0 } && condition.StatusCodes.Contains(statusCode);

    private static bool EvaluateJsonPathExists(
        JsonDocument? document,
        TransformationCondition condition,
        bool shouldExist)
    {
        if (string.IsNullOrEmpty(condition.JsonPath))
        {
            return false;
        }

        if (document == null)
        {
            return !shouldExist;
        }

        var element = GetJsonPathValue(document.RootElement, condition.JsonPath);
        return shouldExist ? element.HasValue : !element.HasValue;
    }

    private static bool EvaluateJsonPathComparison(
        JsonDocument? document,
        TransformationCondition condition,
        Func<string?, string?, bool> comparator)
    {
        if (string.IsNullOrEmpty(condition.JsonPath) || document == null)
        {
            return false;
        }

        var element = GetJsonPathValue(document.RootElement, condition.JsonPath);
        if (!element.HasValue)
        {
            return false;
        }

        var actualValue = GetElementStringValue(element.Value);
        return comparator(actualValue, condition.ExpectedValue);
    }

    private static bool EvaluateJsonPathNumeric(
        JsonDocument? document,
        TransformationCondition condition,
        Func<decimal, decimal, bool> comparator)
    {
        if (string.IsNullOrEmpty(condition.JsonPath) || document == null)
        {
            return false;
        }

        var element = GetJsonPathValue(document.RootElement, condition.JsonPath);
        if (!element.HasValue || !TryGetNumericValue(element.Value, out var actualValue))
        {
            return false;
        }

        if (!decimal.TryParse(condition.ExpectedValue, out var expectedValue))
        {
            return false;
        }

        return comparator(actualValue, expectedValue);
    }

    private static bool EvaluateJsonPathContains(
        JsonDocument? document,
        TransformationCondition condition)
    {
        if (string.IsNullOrEmpty(condition.JsonPath) || document == null)
        {
            return false;
        }

        var element = GetJsonPathValue(document.RootElement, condition.JsonPath);
        if (!element.HasValue)
        {
            return false;
        }

        var actualValue = GetElementStringValue(element.Value);
        if (actualValue == null)
        {
            return false;
        }

        return actualValue.Contains(condition.ExpectedValue ?? "", StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // JSON Path Evaluation (simplified dot notation)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gets a value from a JSON document using simplified path notation.
    /// Supports: $.property, $.nested.property, $.array[0], $.array[0].property.
    /// </summary>
    internal static JsonElement? GetJsonPathValue(JsonElement root, string path)
    {
        var normalizedPath = path.StartsWith("$.") ? path[2..] : path.TrimStart('$', '.');

        if (string.IsNullOrEmpty(normalizedPath))
        {
            return root;
        }

        var current = root;
        var segments = ParsePathSegments(normalizedPath);

        foreach (var segment in segments)
        {
            if (segment.StartsWith('[') && segment.EndsWith(']'))
            {
                var indexStr = segment[1..^1];
                if (!int.TryParse(indexStr, out var index))
                {
                    return null;
                }

                if (current.ValueKind != JsonValueKind.Array ||
                    index < 0 || index >= current.GetArrayLength())
                {
                    return null;
                }

                current = current[index];
            }
            else
            {
                if (current.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                if (!current.TryGetProperty(segment, out var property))
                {
                    // Case-insensitive fallback
                    var found = false;
                    foreach (var prop in current.EnumerateObject())
                    {
                        if (string.Equals(prop.Name, segment, StringComparison.OrdinalIgnoreCase))
                        {
                            current = prop.Value;
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        return null;
                    }
                }
                else
                {
                    current = property;
                }
            }
        }

        return current;
    }

    /// <summary>
    /// Parses a path string into segments, handling dots and bracket notation.
    /// </summary>
    private static List<string> ParsePathSegments(string path)
    {
        var segments = new List<string>();
        var current = new System.Text.StringBuilder();

        for (var i = 0; i < path.Length; i++)
        {
            var c = path[i];

            if (c == '.')
            {
                if (current.Length > 0)
                {
                    segments.Add(current.ToString());
                    current.Clear();
                }
            }
            else if (c == '[')
            {
                if (current.Length > 0)
                {
                    segments.Add(current.ToString());
                    current.Clear();
                }

                var closeBracket = path.IndexOf(']', i);
                if (closeBracket == -1)
                {
                    current.Append(path[i..]);
                    break;
                }

                segments.Add(path.Substring(i, closeBracket - i + 1));
                i = closeBracket;
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            segments.Add(current.ToString());
        }

        return segments;
    }

    /// <summary>
    /// Gets the string representation of a JSON element value.
    /// </summary>
    private static string? GetElementStringValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }

    /// <summary>
    /// Tries to extract a numeric value from a JSON element.
    /// </summary>
    private static bool TryGetNumericValue(JsonElement element, out decimal value)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetDecimal(out value);
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var str = element.GetString();
            return decimal.TryParse(str, out value);
        }

        value = 0;
        return false;
    }
}
