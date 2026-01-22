#nullable enable

using BeyondImmersion.Bannou.Core;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BeyondImmersion.BannouService.Utilities;

/// <summary>
/// Substitutes {{variable}} placeholders in JSON templates.
/// Supports dot-path navigation (e.g., {{party.wallet_id}}) and array indexing (e.g., {{parties[0].id}}).
/// </summary>
/// <remarks>
/// This utility is used by ServiceNavigator for prebound API execution.
/// It preserves JSON types: strings are quoted, numbers/bools/nulls are not.
/// Missing variables cause explicit failure - no silent empty string substitution.
/// </remarks>
public static class TemplateSubstitutor
{
    /// <summary>
    /// Pattern matches {{variableName}}, {{path.to.value}}, {{array[0].item}}
    /// Captures the full path inside the braces.
    /// </summary>
    private static readonly Regex VariablePattern = new(
        @"\{\{([a-zA-Z_][a-zA-Z0-9_]*(?:\.[a-zA-Z_][a-zA-Z0-9_]*|\[\d+\])*)\}\}",
        RegexOptions.Compiled);

    /// <summary>
    /// Substitutes variables in a JSON template string.
    /// </summary>
    /// <param name="template">JSON template with {{variable}} placeholders</param>
    /// <param name="context">Dictionary of values to substitute. Values can be primitives or nested objects.</param>
    /// <returns>Substituted JSON string</returns>
    /// <exception cref="TemplateSubstitutionException">If a variable is missing or path is invalid</exception>
    /// <exception cref="ArgumentNullException">If template or context is null</exception>
    public static string Substitute(string template, IReadOnlyDictionary<string, object?> context)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrEmpty(template))
        {
            return template;
        }

        var result = new StringBuilder(template.Length);
        var lastIndex = 0;

        foreach (Match match in VariablePattern.Matches(template))
        {
            // Append text before the match
            result.Append(template, lastIndex, match.Index - lastIndex);

            var variablePath = match.Groups[1].Value;
            var value = ResolveValue(variablePath, context, template);
            var jsonValue = FormatValueForJson(value, variablePath, template);

            result.Append(jsonValue);
            lastIndex = match.Index + match.Length;
        }

        // Append remaining text after last match
        result.Append(template, lastIndex, template.Length - lastIndex);

        return result.ToString();
    }

    /// <summary>
    /// Validates a template without substituting - checks all variables exist in context.
    /// </summary>
    /// <param name="template">JSON template to validate</param>
    /// <param name="context">Context dictionary to validate against</param>
    /// <returns>Validation result with any missing or invalid variables</returns>
    public static TemplateValidationResult Validate(string template, IReadOnlyDictionary<string, object?> context)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(context);

        var missingVariables = new List<string>();
        var invalidPaths = new List<string>();

        foreach (Match match in VariablePattern.Matches(template))
        {
            var variablePath = match.Groups[1].Value;
            try
            {
                ResolveValue(variablePath, context, template);
            }
            catch (TemplateSubstitutionException ex)
            {
                if (ex.ErrorType == SubstitutionErrorType.MissingVariable)
                {
                    missingVariables.Add(variablePath);
                }
                else
                {
                    invalidPaths.Add(variablePath);
                }
            }
        }

        return new TemplateValidationResult
        {
            IsValid = missingVariables.Count == 0 && invalidPaths.Count == 0,
            MissingVariables = missingVariables,
            InvalidPaths = invalidPaths
        };
    }

    /// <summary>
    /// Extracts all variable paths from a template.
    /// Useful for understanding what context variables a template requires.
    /// </summary>
    /// <param name="template">JSON template to analyze</param>
    /// <returns>List of unique variable paths found in the template</returns>
    public static IReadOnlyList<string> ExtractVariables(string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var variables = new HashSet<string>();
        foreach (Match match in VariablePattern.Matches(template))
        {
            variables.Add(match.Groups[1].Value);
        }
        return variables.ToList();
    }

    /// <summary>
    /// Resolves a dot-path variable from the context dictionary.
    /// </summary>
    private static object? ResolveValue(
        string variablePath,
        IReadOnlyDictionary<string, object?> context,
        string template)
    {
        var segments = ParsePath(variablePath);
        if (segments.Count == 0)
        {
            throw new TemplateSubstitutionException(
                variablePath,
                template,
                "Empty variable path",
                SubstitutionErrorType.InvalidPath);
        }

        object? current = context;

        foreach (var segment in segments)
        {
            if (current == null)
            {
                throw new TemplateSubstitutionException(
                    variablePath,
                    template,
                    $"Cannot access '{segment}' on null value",
                    SubstitutionErrorType.InvalidPath);
            }

            current = ResolveSegment(current, segment, variablePath, template);
        }

        return current;
    }

    /// <summary>
    /// Parses a variable path into segments.
    /// E.g., "party.wallet_id" -> ["party", "wallet_id"]
    /// E.g., "parties[0].id" -> ["parties", "[0]", "id"]
    /// </summary>
    private static List<string> ParsePath(string path)
    {
        var segments = new List<string>();
        var current = new StringBuilder();

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

                // Find closing bracket and extract index
                var closeBracket = path.IndexOf(']', i);
                if (closeBracket == -1)
                {
                    // Invalid path - unclosed bracket, but we'll handle during resolution
                    current.Append(c);
                }
                else
                {
                    segments.Add(path.Substring(i, closeBracket - i + 1));
                    i = closeBracket;
                }
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
    /// Resolves a single segment from the current value.
    /// </summary>
    private static object? ResolveSegment(
        object current,
        string segment,
        string variablePath,
        string template)
    {
        // Handle array index access [n]
        if (segment.StartsWith('[') && segment.EndsWith(']'))
        {
            var indexStr = segment[1..^1];
            if (!int.TryParse(indexStr, out var index))
            {
                throw new TemplateSubstitutionException(
                    variablePath,
                    template,
                    $"Invalid array index: {segment}",
                    SubstitutionErrorType.InvalidPath);
            }

            return ResolveArrayIndex(current, index, variablePath, template);
        }

        // Handle property/key access
        return ResolveProperty(current, segment, variablePath, template);
    }

    /// <summary>
    /// Resolves an array index from a collection.
    /// </summary>
    private static object? ResolveArrayIndex(
        object current,
        int index,
        string variablePath,
        string template)
    {
        // Handle JsonElement arrays
        if (current is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind != JsonValueKind.Array)
            {
                throw new TemplateSubstitutionException(
                    variablePath,
                    template,
                    $"Cannot index into non-array JsonElement (kind: {jsonElement.ValueKind})",
                    SubstitutionErrorType.InvalidPath);
            }

            if (index < 0 || index >= jsonElement.GetArrayLength())
            {
                throw new TemplateSubstitutionException(
                    variablePath,
                    template,
                    $"Array index {index} out of bounds (length: {jsonElement.GetArrayLength()})",
                    SubstitutionErrorType.InvalidPath);
            }

            return jsonElement[index];
        }

        // Handle IList
        if (current is System.Collections.IList list)
        {
            if (index < 0 || index >= list.Count)
            {
                throw new TemplateSubstitutionException(
                    variablePath,
                    template,
                    $"Array index {index} out of bounds (count: {list.Count})",
                    SubstitutionErrorType.InvalidPath);
            }

            return list[index];
        }

        // Handle arrays
        if (current is Array array)
        {
            if (index < 0 || index >= array.Length)
            {
                throw new TemplateSubstitutionException(
                    variablePath,
                    template,
                    $"Array index {index} out of bounds (length: {array.Length})",
                    SubstitutionErrorType.InvalidPath);
            }

            return array.GetValue(index);
        }

        throw new TemplateSubstitutionException(
            variablePath,
            template,
            $"Cannot index into type {current.GetType().Name}",
            SubstitutionErrorType.InvalidPath);
    }

    /// <summary>
    /// Resolves a property or dictionary key from an object.
    /// </summary>
    private static object? ResolveProperty(
        object current,
        string propertyName,
        string variablePath,
        string template)
    {
        // Handle JsonElement objects
        if (current is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind != JsonValueKind.Object)
            {
                throw new TemplateSubstitutionException(
                    variablePath,
                    template,
                    $"Cannot access property '{propertyName}' on non-object JsonElement (kind: {jsonElement.ValueKind})",
                    SubstitutionErrorType.InvalidPath);
            }

            if (!jsonElement.TryGetProperty(propertyName, out var prop))
            {
                throw new TemplateSubstitutionException(
                    variablePath,
                    template,
                    $"Property '{propertyName}' not found in JSON object",
                    SubstitutionErrorType.MissingVariable);
            }

            return prop;
        }

        // Handle dictionaries (including IReadOnlyDictionary)
        if (current is IReadOnlyDictionary<string, object?> roDict)
        {
            if (!roDict.TryGetValue(propertyName, out var value))
            {
                throw new TemplateSubstitutionException(
                    variablePath,
                    template,
                    $"Key '{propertyName}' not found in dictionary",
                    SubstitutionErrorType.MissingVariable);
            }

            return value;
        }

        if (current is IDictionary<string, object?> dict)
        {
            if (!dict.TryGetValue(propertyName, out var value))
            {
                throw new TemplateSubstitutionException(
                    variablePath,
                    template,
                    $"Key '{propertyName}' not found in dictionary",
                    SubstitutionErrorType.MissingVariable);
            }

            return value;
        }

        // Handle regular objects via reflection
        var type = current.GetType();
        var property = type.GetProperty(propertyName,
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.IgnoreCase);

        if (property != null)
        {
            return property.GetValue(current);
        }

        var field = type.GetField(propertyName,
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.IgnoreCase);

        if (field != null)
        {
            return field.GetValue(current);
        }

        throw new TemplateSubstitutionException(
            variablePath,
            template,
            $"Property or field '{propertyName}' not found on type {type.Name}",
            SubstitutionErrorType.MissingVariable);
    }

    /// <summary>
    /// Formats a value for JSON output, preserving types.
    /// </summary>
    private static string FormatValueForJson(object? value, string variablePath, string template)
    {
        if (value == null)
        {
            return "null";
        }

        // Handle JsonElement specially - preserve its exact JSON representation
        if (value is JsonElement jsonElement)
        {
            return FormatJsonElement(jsonElement);
        }

        // Primitive types
        return value switch
        {
            string s => JsonSerializer.Serialize(s), // Properly escapes and quotes
            bool b => b ? "true" : "false",
            char c => JsonSerializer.Serialize(c.ToString()),
            byte or sbyte or short or ushort or int or uint or long or ulong => value.ToString() ?? "0",
            float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture),
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture),
            DateTime dt => JsonSerializer.Serialize(dt),
            DateTimeOffset dto => JsonSerializer.Serialize(dto),
            DateOnly d => JsonSerializer.Serialize(d),
            TimeOnly t => JsonSerializer.Serialize(t),
            TimeSpan ts => JsonSerializer.Serialize(ts.ToString()),
            Guid g => JsonSerializer.Serialize(g),
            Enum e => JsonSerializer.Serialize(e.ToString()),
            // Complex objects - serialize to JSON
            _ => BannouJson.Serialize(value)
        };
    }

    /// <summary>
    /// Formats a JsonElement to its raw JSON string representation.
    /// </summary>
    private static string FormatJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => JsonSerializer.Serialize(element.GetString()),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            JsonValueKind.Object or JsonValueKind.Array => element.GetRawText(),
            _ => element.GetRawText()
        };
    }
}

/// <summary>
/// Exception thrown when template substitution fails.
/// </summary>
public class TemplateSubstitutionException : Exception
{
    /// <summary>
    /// The variable path that caused the failure.
    /// </summary>
    public string VariablePath { get; }

    /// <summary>
    /// The template being processed.
    /// </summary>
    public string Template { get; }

    /// <summary>
    /// The type of substitution error.
    /// </summary>
    public SubstitutionErrorType ErrorType { get; }

    /// <summary>
    /// Creates a new TemplateSubstitutionException.
    /// </summary>
    public TemplateSubstitutionException(
        string variablePath,
        string template,
        string message,
        SubstitutionErrorType errorType)
        : base($"Template substitution failed for '{{{{{variablePath}}}}}': {message}")
    {
        VariablePath = variablePath;
        Template = template;
        ErrorType = errorType;
    }
}

/// <summary>
/// Type of template substitution error.
/// </summary>
public enum SubstitutionErrorType
{
    /// <summary>A required variable was not found in the context.</summary>
    MissingVariable,

    /// <summary>The variable path is syntactically invalid or points to an invalid location.</summary>
    InvalidPath
}

/// <summary>
/// Result of template validation.
/// </summary>
public class TemplateValidationResult
{
    /// <summary>
    /// Whether the template is valid (all variables found, all paths valid).
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// List of variable paths that were not found in the context.
    /// </summary>
    public IReadOnlyList<string> MissingVariables { get; init; } = [];

    /// <summary>
    /// List of variable paths that are syntactically invalid.
    /// </summary>
    public IReadOnlyList<string> InvalidPaths { get; init; } = [];
}
