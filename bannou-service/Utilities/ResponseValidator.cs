#nullable enable

using BeyondImmersion.BannouService.Contract;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Utilities;

/// <summary>
/// Outcome of response validation.
/// </summary>
public enum ValidationOutcome
{
    /// <summary>All success conditions passed.</summary>
    Success,

    /// <summary>Permanent failure - clause condition violated, no retry.</summary>
    PermanentFailure,

    /// <summary>Transient failure - temporary error, should retry later.</summary>
    TransientFailure
}

/// <summary>
/// Result of validating a response against validation rules.
/// </summary>
public class ResponseValidationResult
{
    /// <summary>
    /// The validation outcome.
    /// </summary>
    public ValidationOutcome Outcome { get; init; }

    /// <summary>
    /// Description of why validation failed (if applicable).
    /// </summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// The condition that failed (if applicable).
    /// </summary>
    public ValidationCondition? FailedCondition { get; init; }

    /// <summary>
    /// Creates a success result.
    /// </summary>
    public static ResponseValidationResult Succeeded() => new()
    {
        Outcome = ValidationOutcome.Success
    };

    /// <summary>
    /// Creates a permanent failure result.
    /// </summary>
    public static ResponseValidationResult PermanentFailed(string reason, ValidationCondition? condition = null) => new()
    {
        Outcome = ValidationOutcome.PermanentFailure,
        FailureReason = reason,
        FailedCondition = condition
    };

    /// <summary>
    /// Creates a transient failure result.
    /// </summary>
    public static ResponseValidationResult TransientFailed(string reason) => new()
    {
        Outcome = ValidationOutcome.TransientFailure,
        FailureReason = reason
    };
}

/// <summary>
/// Validates API responses against configurable validation rules.
/// Supports three-outcome model: Success, PermanentFailure, TransientFailure.
/// </summary>
/// <remarks>
/// This utility is used by lib-contract to validate prebound API responses
/// without needing to understand specific API semantics. The validation rules
/// are defined in the contract schema (ResponseValidation).
/// </remarks>
public static class ResponseValidator
{
    /// <summary>
    /// Default transient failure status codes.
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
    /// Validates a response against the given validation rules.
    /// </summary>
    /// <param name="statusCode">HTTP status code from the response.</param>
    /// <param name="responseBody">Response body as JSON string.</param>
    /// <param name="validation">Validation rules to apply.</param>
    /// <returns>Validation result with outcome and failure reason.</returns>
    public static ResponseValidationResult Validate(
        int statusCode,
        string? responseBody,
        ResponseValidation? validation)
    {
        // No validation rules = success
        if (validation == null)
        {
            return ResponseValidationResult.Succeeded();
        }

        // Check for transient failures first (retry-able errors)
        var transientCodes = validation.TransientFailureStatusCodes?.Count > 0
            ? validation.TransientFailureStatusCodes.ToHashSet()
            : DefaultTransientStatusCodes;

        if (transientCodes.Contains(statusCode))
        {
            return ResponseValidationResult.TransientFailed($"HTTP status {statusCode} indicates transient failure");
        }

        // Parse response body for JsonPath conditions
        JsonDocument? document = null;
        if (!string.IsNullOrEmpty(responseBody))
        {
            try
            {
                document = JsonDocument.Parse(responseBody);
            }
            catch (JsonException)
            {
                // If we can't parse the response as JSON, that's a permanent failure
                return ResponseValidationResult.PermanentFailed("Response body is not valid JSON");
            }
        }

        try
        {
            // Check success conditions (all must pass)
            if (validation.SuccessConditions?.Count > 0)
            {
                foreach (var condition in validation.SuccessConditions)
                {
                    var result = EvaluateCondition(statusCode, document, condition);
                    if (!result.passed)
                    {
                        // Success condition failed - check if it's a permanent failure
                        return CheckPermanentFailure(statusCode, document, validation, condition, result.reason);
                    }
                }
            }

            return ResponseValidationResult.Succeeded();
        }
        finally
        {
            document?.Dispose();
        }
    }

    /// <summary>
    /// Checks permanent failure conditions after a success condition fails.
    /// </summary>
    private static ResponseValidationResult CheckPermanentFailure(
        int statusCode,
        JsonDocument? document,
        ResponseValidation validation,
        ValidationCondition failedSuccessCondition,
        string failureReason)
    {
        // If there are permanent failure conditions, check if any match
        if (validation.PermanentFailureConditions?.Count > 0)
        {
            foreach (var condition in validation.PermanentFailureConditions)
            {
                var (passed, reason) = EvaluateCondition(statusCode, document, condition);
                if (passed)
                {
                    // A permanent failure condition matched
                    return ResponseValidationResult.PermanentFailed(
                        $"Permanent failure condition matched: {reason}",
                        condition);
                }
            }
        }

        // No permanent failure conditions matched - the failed success condition
        // indicates permanent failure by default
        return ResponseValidationResult.PermanentFailed(
            $"Success condition failed: {failureReason}",
            failedSuccessCondition);
    }

    /// <summary>
    /// Evaluates a single validation condition.
    /// </summary>
    private static (bool passed, string reason) EvaluateCondition(
        int statusCode,
        JsonDocument? document,
        ValidationCondition condition)
    {
        return condition.Type switch
        {
            ValidationConditionType.StatusCodeIn => EvaluateStatusCodeIn(statusCode, condition),
            ValidationConditionType.JsonPathEquals => EvaluateJsonPathComparison(document, condition, CompareEquals),
            ValidationConditionType.JsonPathNotEquals => EvaluateJsonPathComparison(document, condition, CompareNotEquals),
            ValidationConditionType.JsonPathExists => EvaluateJsonPathExists(document, condition, shouldExist: true),
            ValidationConditionType.JsonPathNotExists => EvaluateJsonPathExists(document, condition, shouldExist: false),
            ValidationConditionType.JsonPathGreaterThan => EvaluateJsonPathNumeric(document, condition, (a, b) => a > b, ">"),
            ValidationConditionType.JsonPathLessThan => EvaluateJsonPathNumeric(document, condition, (a, b) => a < b, "<"),
            ValidationConditionType.JsonPathContains => EvaluateJsonPathContains(document, condition),
            _ => (false, $"Unknown condition type: {condition.Type}")
        };
    }

    private static bool CompareEquals(string? actual, string? expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static bool CompareNotEquals(string? actual, string? expected) =>
        !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static (bool passed, string reason) EvaluateStatusCodeIn(
        int statusCode,
        ValidationCondition condition)
    {
        if (condition.StatusCodes == null || condition.StatusCodes.Count == 0)
        {
            return (false, "StatusCodeIn condition requires statusCodes array");
        }

        var passed = condition.StatusCodes.Contains(statusCode);
        return (passed, passed
            ? $"Status code {statusCode} is in allowed list"
            : $"Status code {statusCode} is not in allowed list [{string.Join(", ", condition.StatusCodes)}]");
    }

    private static (bool passed, string reason) EvaluateJsonPathExists(
        JsonDocument? document,
        ValidationCondition condition,
        bool shouldExist)
    {
        if (string.IsNullOrEmpty(condition.JsonPath))
        {
            return (false, "JsonPath condition requires jsonPath property");
        }

        if (document == null)
        {
            return shouldExist
                ? (false, "Response body is empty, cannot evaluate JsonPath")
                : (true, "Response body is empty, path does not exist");
        }

        var element = GetJsonPathValue(document.RootElement, condition.JsonPath);
        var exists = element.HasValue;

        if (shouldExist)
        {
            return (exists, exists
                ? $"Path '{condition.JsonPath}' exists"
                : $"Path '{condition.JsonPath}' does not exist");
        }
        else
        {
            return (!exists, exists
                ? $"Path '{condition.JsonPath}' exists but should not"
                : $"Path '{condition.JsonPath}' does not exist as expected");
        }
    }

    private static (bool passed, string reason) EvaluateJsonPathComparison(
        JsonDocument? document,
        ValidationCondition condition,
        Func<string?, string?, bool> comparator)
    {
        if (string.IsNullOrEmpty(condition.JsonPath))
        {
            return (false, "JsonPath condition requires jsonPath property");
        }

        if (document == null)
        {
            return (false, "Response body is empty, cannot evaluate JsonPath");
        }

        var element = GetJsonPathValue(document.RootElement, condition.JsonPath);
        if (!element.HasValue)
        {
            return (false, $"Path '{condition.JsonPath}' not found in response");
        }

        var actualValue = GetElementValue(element.Value);
        var passed = comparator(actualValue, condition.ExpectedValue);

        return (passed, passed
            ? $"Path '{condition.JsonPath}' value '{actualValue}' matches expected"
            : $"Path '{condition.JsonPath}' value '{actualValue}' does not match expected '{condition.ExpectedValue}'");
    }

    private static (bool passed, string reason) EvaluateJsonPathNumeric(
        JsonDocument? document,
        ValidationCondition condition,
        Func<decimal, decimal, bool> comparator,
        string operatorSymbol)
    {
        if (string.IsNullOrEmpty(condition.JsonPath))
        {
            return (false, "JsonPath condition requires jsonPath property");
        }

        if (document == null)
        {
            return (false, "Response body is empty, cannot evaluate JsonPath");
        }

        var element = GetJsonPathValue(document.RootElement, condition.JsonPath);
        if (!element.HasValue)
        {
            return (false, $"Path '{condition.JsonPath}' not found in response");
        }

        if (!TryGetNumericValue(element.Value, out var actualValue))
        {
            return (false, $"Path '{condition.JsonPath}' value is not numeric");
        }

        if (!decimal.TryParse(condition.ExpectedValue, out var expectedValue))
        {
            return (false, $"Expected value '{condition.ExpectedValue}' is not numeric");
        }

        var passed = comparator(actualValue, expectedValue);
        return (passed, passed
            ? $"Path '{condition.JsonPath}' value {actualValue} {operatorSymbol} {expectedValue}"
            : $"Path '{condition.JsonPath}' value {actualValue} is not {operatorSymbol} {expectedValue}");
    }

    private static (bool passed, string reason) EvaluateJsonPathContains(
        JsonDocument? document,
        ValidationCondition condition)
    {
        if (string.IsNullOrEmpty(condition.JsonPath))
        {
            return (false, "JsonPath condition requires jsonPath property");
        }

        if (document == null)
        {
            return (false, "Response body is empty, cannot evaluate JsonPath");
        }

        var element = GetJsonPathValue(document.RootElement, condition.JsonPath);
        if (!element.HasValue)
        {
            return (false, $"Path '{condition.JsonPath}' not found in response");
        }

        var actualValue = GetElementValue(element.Value);
        if (actualValue == null)
        {
            return (false, $"Path '{condition.JsonPath}' value is null");
        }

        var passed = actualValue.Contains(condition.ExpectedValue ?? "", StringComparison.OrdinalIgnoreCase);
        return (passed, passed
            ? $"Path '{condition.JsonPath}' contains '{condition.ExpectedValue}'"
            : $"Path '{condition.JsonPath}' value '{actualValue}' does not contain '{condition.ExpectedValue}'");
    }

    /// <summary>
    /// Gets a value from a JSON document using a simple path notation.
    /// Supports: $.property, $.nested.property, $.array[0], $.array[0].property
    /// </summary>
    /// <remarks>
    /// This is a simplified JsonPath implementation. For more complex queries,
    /// consider using JsonPath.Net library.
    /// </remarks>
    private static JsonElement? GetJsonPathValue(JsonElement root, string path)
    {
        // Remove leading $. if present
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
                // Array index
                var indexStr = segment[1..^1];
                if (!int.TryParse(indexStr, out var index))
                {
                    return null;
                }

                if (current.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                if (index < 0 || index >= current.GetArrayLength())
                {
                    return null;
                }

                current = current[index];
            }
            else
            {
                // Property access
                if (current.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                if (!current.TryGetProperty(segment, out var property))
                {
                    // Try case-insensitive match
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
    /// Parses a path string into segments.
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

                // Find closing bracket
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
    /// Gets the string value of a JSON element.
    /// </summary>
    private static string? GetElementValue(JsonElement element)
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
    /// Tries to get a numeric value from a JSON element.
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
