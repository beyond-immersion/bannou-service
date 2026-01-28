using System.Text.RegularExpressions;

namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Specifies a regex pattern constraint for a configuration property.
/// Matches OpenAPI 3.0 validation keyword: pattern.
/// </summary>
/// <remarks>
/// <para>
/// This attribute is generated from OpenAPI schema validation keywords and validated
/// at startup by <see cref="Configuration.IServiceConfiguration.ValidatePatterns"/>.
/// </para>
/// <para>
/// Use this for ensuring configuration strings match required formats, such as:
/// - URL formats (^https?://)
/// - Domain names
/// - Email-like patterns
/// - Environment-specific naming conventions
/// </para>
/// <para>
/// The pattern uses .NET regular expression syntax. The entire string must match
/// the pattern (implicit anchoring is NOT applied - use ^ and $ if needed).
/// </para>
/// </remarks>
/// <example>
/// Schema:
/// <code>
/// ServiceDomain:
///   type: string
///   pattern: "^https?://"
///   default: "https://api.example.com"
/// </code>
/// Generated:
/// <code>
/// [ConfigPattern(@"^https?://", Description = "Must start with http:// or https://")]
/// public string ServiceDomain { get; set; } = "https://api.example.com";
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class ConfigPatternAttribute : Attribute, IServiceAttribute
{
    /// <summary>
    /// The regular expression pattern that the value must match.
    /// </summary>
    public string Pattern { get; }

    /// <summary>
    /// Optional human-readable description of what the pattern validates.
    /// Used in error messages to help users understand the expected format.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Timeout for regex matching to prevent ReDoS attacks from malicious patterns.
    /// Defaults to 1 second.
    /// </summary>
    public TimeSpan MatchTimeout { get; set; } = TimeSpan.FromSeconds(1);

    private Regex? _compiledRegex;

    /// <summary>
    /// Creates a pattern constraint with the specified regex pattern.
    /// </summary>
    /// <param name="pattern">The regular expression pattern to match against.</param>
    public ConfigPatternAttribute(string pattern)
    {
        Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
    }

    /// <summary>
    /// Gets a compiled regex for efficient repeated matching.
    /// </summary>
    private Regex CompiledRegex => _compiledRegex ??= new Regex(Pattern, RegexOptions.Compiled, MatchTimeout);

    /// <summary>
    /// Validates whether the given string matches this pattern constraint.
    /// </summary>
    /// <param name="value">The string value to validate. Null values are considered valid (use other validation for null checks).</param>
    /// <returns>True if the value matches the pattern or is null; otherwise, false.</returns>
    public bool IsValid(string? value)
    {
        // Null values are valid - null checking is handled by other validation
        if (value == null)
        {
            return true;
        }

        try
        {
            return CompiledRegex.IsMatch(value);
        }
        catch (RegexMatchTimeoutException)
        {
            // Pattern matching timed out - treat as invalid to be safe
            return false;
        }
    }

    /// <summary>
    /// Gets a human-readable description of this pattern constraint.
    /// </summary>
    /// <returns>The description if provided, otherwise the raw pattern.</returns>
    public string GetPatternDescription()
    {
        return Description ?? $"must match pattern: {Pattern}";
    }
}
