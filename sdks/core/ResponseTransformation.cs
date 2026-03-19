#nullable enable

using System.Text.Json.Serialization;

namespace BeyondImmersion.Bannou.Core;

/// <summary>
/// Transformation rules applied to a prebound API response.
/// The transformer evaluates rules in order; the first matching rule produces
/// the result (status code + payload). If no rules match, the raw response
/// passes through unchanged.
/// </summary>
/// <remarks>
/// <para>
/// This replaces simple pass/fail validation with a conditional transformer model:
/// the prebound API creator defines what "success" and "failure" mean in their context,
/// and can produce entirely different payloads based on what the response contains.
/// </para>
/// <para>
/// Transient failure detection (transport-level retryable errors) runs before any rules.
/// </para>
/// </remarks>
public class ResponseTransformation
{
    /// <summary>
    /// Ordered list of transformation rules. First matching rule wins.
    /// Each rule specifies conditions (AND logic) and the result to produce when matched.
    /// </summary>
    public List<TransformationRule>? Rules { get; init; }

    /// <summary>
    /// HTTP status codes indicating transient failure (retryable at transport level).
    /// Checked before any rules are evaluated.
    /// Default when null or empty: 408, 429, 502, 503, 504.
    /// </summary>
    public List<int>? TransientFailureStatusCodes { get; init; }
}

/// <summary>
/// A single transformation rule: when all conditions match, produce this result.
/// </summary>
/// <remarks>
/// The payload field allows the prebound API creator to specify exactly what
/// the consuming system receives. For example, a species check might return
/// different modifier payloads depending on which species the response contains,
/// without the consumer needing to understand species mechanics.
/// </remarks>
public class TransformationRule
{
    /// <summary>
    /// All conditions must match for this rule to fire (AND logic).
    /// An empty or null conditions list always matches (unconditional rule).
    /// </summary>
    public List<TransformationCondition>? Conditions { get; init; }

    /// <summary>
    /// HTTP status code to return when this rule matches.
    /// </summary>
    public int StatusCode { get; init; }

    /// <summary>
    /// JSON payload to return when this rule matches.
    /// Null means pass through the raw response body unchanged.
    /// </summary>
    public string? Payload { get; init; }

    /// <summary>
    /// Human-readable description of what this rule does (for diagnostics/logging).
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// A single condition to evaluate against an API response.
/// Used within transformation rules to match on status codes, JSON paths, and values.
/// </summary>
public class TransformationCondition
{
    /// <summary>
    /// The type of condition to evaluate.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TransformationConditionType Type { get; init; }

    /// <summary>
    /// JsonPath expression to extract a value from the response body.
    /// Required for all JsonPath* condition types. Uses simplified dot notation
    /// (e.g., "$.balance", "$.species.code", "$.items[0].name").
    /// </summary>
    public string? JsonPath { get; init; }

    /// <summary>
    /// Expected value for comparison conditions.
    /// Type coercion applied: "true"/"false" for booleans, numeric strings for numbers.
    /// </summary>
    public string? ExpectedValue { get; init; }

    /// <summary>
    /// Comparison operator for numeric comparisons (JsonPathGreaterThan/LessThan).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ComparisonOperator? Operator { get; init; }

    /// <summary>
    /// HTTP status codes for the StatusCodeIn condition type.
    /// </summary>
    public List<int>? StatusCodes { get; init; }
}

/// <summary>
/// Types of conditions that can be evaluated against an API response.
/// </summary>
public enum TransformationConditionType
{
    /// <summary>Response status code is in the specified list.</summary>
    StatusCodeIn,

    /// <summary>JSON path value equals the expected value (case-insensitive).</summary>
    JsonPathEquals,

    /// <summary>JSON path value does not equal the expected value (case-insensitive).</summary>
    JsonPathNotEquals,

    /// <summary>JSON path exists in the response body.</summary>
    JsonPathExists,

    /// <summary>JSON path does not exist in the response body.</summary>
    JsonPathNotExists,

    /// <summary>JSON path numeric value is greater than the expected value.</summary>
    JsonPathGreaterThan,

    /// <summary>JSON path numeric value is less than the expected value.</summary>
    JsonPathLessThan,

    /// <summary>JSON path string value contains the expected value (case-insensitive).</summary>
    JsonPathContains
}

/// <summary>
/// Comparison operators for numeric conditions.
/// </summary>
public enum ComparisonOperator
{
    /// <summary>Equal to.</summary>
    Eq,

    /// <summary>Not equal to.</summary>
    Ne,

    /// <summary>Greater than.</summary>
    Gt,

    /// <summary>Greater than or equal to.</summary>
    Gte,

    /// <summary>Less than.</summary>
    Lt,

    /// <summary>Less than or equal to.</summary>
    Lte
}

/// <summary>
/// What happened during response transformation.
/// </summary>
public enum TransformationOutcome
{
    /// <summary>
    /// No transformation rules configured, or no rules matched.
    /// StatusCode and Payload are from the raw API response.
    /// </summary>
    PassThrough,

    /// <summary>
    /// A transformation rule matched. StatusCode and Payload are from the rule.
    /// </summary>
    Transformed,

    /// <summary>
    /// The raw response status code was in the transient failure set.
    /// The caller should retry. Payload is null.
    /// </summary>
    TransientFailure
}

/// <summary>
/// Result of applying response transformation rules to a raw API response.
/// Always contains a final StatusCode. Payload may be the raw response,
/// a rule-defined payload, or null (transient failure).
/// </summary>
public class TransformationResult
{
    /// <summary>
    /// The final HTTP status code (from the raw response or the matched rule).
    /// </summary>
    public int StatusCode { get; init; }

    /// <summary>
    /// The final payload. May be:
    /// - The raw response body (pass-through or rule with null payload)
    /// - A rule-defined payload (transformed)
    /// - Null (transient failure or empty raw response)
    /// </summary>
    public string? Payload { get; init; }

    /// <summary>
    /// What happened during transformation.
    /// </summary>
    public TransformationOutcome Outcome { get; init; }

    /// <summary>
    /// Description of the matched rule, if any. For diagnostics/logging.
    /// </summary>
    public string? MatchedRuleDescription { get; init; }

    /// <summary>
    /// Whether the final status code indicates success (2xx).
    /// </summary>
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
}
