// =============================================================================
// Path Validation Result
// Result of validating a path against a resource template.
// =============================================================================

namespace BeyondImmersion.Bannou.BehaviorCompiler.Templates;

/// <summary>
/// Result of validating a path against a resource template.
/// </summary>
/// <remarks>
/// Used by the SemanticAnalyzer to validate ABML expressions that access
/// resource snapshot data (e.g., ${candidate.personality.traits.AGGRESSION}).
/// </remarks>
public sealed class PathValidationResult
{
    /// <summary>
    /// Whether the path is valid for the given template.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// The expected type at this path (if valid).
    /// </summary>
    public Type? ExpectedType { get; }

    /// <summary>
    /// Error message describing why validation failed (if invalid).
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Suggested valid paths for "did you mean?" hints.
    /// </summary>
    public IReadOnlyList<string> Suggestions { get; }

    private PathValidationResult(
        bool isValid,
        Type? expectedType,
        string? errorMessage,
        IReadOnlyList<string>? suggestions)
    {
        IsValid = isValid;
        ExpectedType = expectedType;
        ErrorMessage = errorMessage;
        Suggestions = suggestions ?? Array.Empty<string>();
    }

    /// <summary>
    /// Creates a successful validation result with the expected type.
    /// </summary>
    /// <param name="expectedType">The type of the value at this path.</param>
    /// <returns>A valid path result.</returns>
    public static PathValidationResult Valid(Type expectedType)
    {
        ArgumentNullException.ThrowIfNull(expectedType);
        return new PathValidationResult(true, expectedType, null, null);
    }

    /// <summary>
    /// Creates a failed validation result for an unknown path.
    /// </summary>
    /// <param name="path">The path that was not found.</param>
    /// <param name="validPaths">All valid paths for suggestion matching.</param>
    /// <returns>An invalid path result with suggestions.</returns>
    public static PathValidationResult InvalidPath(string path, IEnumerable<string> validPaths)
    {
        var suggestions = FindSimilarPaths(path, validPaths).ToList();
        return new PathValidationResult(
            false,
            null,
            $"Unknown path '{path}'",
            suggestions);
    }

    /// <summary>
    /// Creates a failed validation result for an unregistered template.
    /// </summary>
    /// <param name="templateName">The template name that was not found.</param>
    /// <returns>An invalid result indicating unknown template.</returns>
    public static PathValidationResult UnknownTemplate(string templateName) =>
        new(false, null, $"Unknown resource template '{templateName}'", null);

    /// <summary>
    /// Finds paths similar to the given path for suggestions.
    /// Uses prefix matching on the first path segment.
    /// </summary>
    private static IEnumerable<string> FindSimilarPaths(string path, IEnumerable<string> validPaths)
    {
        if (string.IsNullOrEmpty(path))
        {
            return validPaths.Where(p => !string.IsNullOrEmpty(p)).Take(5);
        }

        var firstSegment = path.Split('.')[0];

        // First try: paths starting with the same first segment
        var prefixMatches = validPaths
            .Where(p => p.StartsWith(firstSegment, StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();

        if (prefixMatches.Count > 0)
        {
            return prefixMatches;
        }

        // Fallback: paths containing any segment that matches
        return validPaths
            .Where(p => p.Split('.').Any(segment =>
                segment.StartsWith(firstSegment, StringComparison.OrdinalIgnoreCase)))
            .Take(3);
    }
}
