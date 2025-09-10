using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BeyondImmersion.BannouService.Behaviour;

/// <summary>
/// ABML (Arcadia Behavior Markup Language) parser using YamlDotNet.
/// Handles YAML parsing, validation, and context variable resolution.
/// </summary>
public class AbmlParser
{
    private readonly ILogger<AbmlParser>? _logger;
    private readonly IDeserializer _yamlDeserializer;
    private readonly ISerializer _yamlSerializer;

    /// <summary>
    /// Regular expression for matching context variable expressions like ${npc.stats.energy}.
    /// </summary>
    private static readonly Regex ContextVariableRegex = new(@"\$\{([^}]+)\}", RegexOptions.Compiled);

    /// <summary>
    /// Initializes a new instance of the AbmlParser.
    /// </summary>
    public AbmlParser(ILogger<AbmlParser>? logger = null)
    {
        _logger = logger;

        // Configure YamlDotNet deserializer with custom settings
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .WithTypeConverter(new ContextVariableTypeConverter())
            .IgnoreUnmatchedProperties()
            .Build();

        // Configure YamlDotNet serializer for output generation
        _yamlSerializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
    }

    /// <summary>
    /// Parses ABML YAML content into a structured document.
    /// </summary>
    /// <param name="abmlContent">Raw ABML YAML content</param>
    /// <returns>Parsed ABML document or null if parsing fails</returns>
    public AbmlParseResult ParseAbmlDocument(string abmlContent)
    {
        try
        {
            _logger?.LogDebug("Parsing ABML document, content length: {Length}", abmlContent.Length);

            if (string.IsNullOrWhiteSpace(abmlContent))
            {
                return AbmlParseResult.Failure("ABML content is empty or null");
            }

            // Parse the YAML content into AbmlDocument structure
            var document = _yamlDeserializer.Deserialize<AbmlDocument>(abmlContent);

            if (document == null)
            {
                return AbmlParseResult.Failure("Failed to deserialize ABML document");
            }

            // Validate the basic structure
            var validationResult = ValidateDocumentStructure(document);
            if (!validationResult.IsValid)
            {
                return AbmlParseResult.Failure("Document structure validation failed", validationResult.Errors);
            }

            _logger?.LogDebug("Successfully parsed ABML document with ID: {Id}", document.Metadata?.Id ?? "unknown");

            return AbmlParseResult.Success(document);
        }
        catch (YamlException yamlEx)
        {
            _logger?.LogError(yamlEx, "YAML parsing error in ABML document");
            return AbmlParseResult.Failure($"YAML parsing error: {yamlEx.Message}", new[]
            {
                new ValidationError
                {
                    Type = ValidationErrorType.Syntax,
                    Message = yamlEx.Message,
                    LineNumber = yamlEx.Start.Line,
                    ColumnNumber = yamlEx.Start.Column
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error parsing ABML document");
            return AbmlParseResult.Failure($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates the basic structure of an ABML document.
    /// </summary>
    /// <param name="document">The parsed ABML document</param>
    /// <returns>Validation result with any errors found</returns>
    public ValidationResult ValidateDocumentStructure(AbmlDocument document)
    {
        var errors = new List<ValidationError>();

        // Check version compatibility
        if (string.IsNullOrWhiteSpace(document.Version))
        {
            errors.Add(new ValidationError
            {
                Type = ValidationErrorType.Schema,
                Message = "Document version is required",
                YamlPath = "version"
            });
        }
        else if (!IsCompatibleVersion(document.Version))
        {
            errors.Add(new ValidationError
            {
                Type = ValidationErrorType.Schema,
                Message = $"Unsupported ABML version: {document.Version}",
                YamlPath = "version"
            });
        }

        // Validate metadata if present
        if (document.Metadata != null)
        {
            if (string.IsNullOrWhiteSpace(document.Metadata.Id))
            {
                errors.Add(new ValidationError
                {
                    Type = ValidationErrorType.Schema,
                    Message = "Metadata ID is required when metadata is present",
                    YamlPath = "metadata.id"
                });
            }

            if (document.Metadata.Priority < 1 || document.Metadata.Priority > 100)
            {
                errors.Add(new ValidationError
                {
                    Type = ValidationErrorType.Schema,
                    Message = "Metadata priority must be between 1 and 100",
                    YamlPath = "metadata.priority"
                });
            }
        }

        // Validate behaviors
        if (document.Behaviors.Count == 0)
        {
            errors.Add(new ValidationError
            {
                Type = ValidationErrorType.Schema,
                Message = "At least one behavior must be defined",
                YamlPath = "behaviors"
            });
        }

        foreach (var behaviorKvp in document.Behaviors)
        {
            var behaviorName = behaviorKvp.Key;
            var behavior = behaviorKvp.Value;
            var behaviorPath = $"behaviors.{behaviorName}";

            // Validate behavior structure
            if (behavior.Actions.Count == 0 && behavior.Triggers.Count == 0)
            {
                errors.Add(new ValidationError
                {
                    Type = ValidationErrorType.Schema,
                    Message = "Behavior must have either triggers or actions defined",
                    YamlPath = behaviorPath
                });
            }

            // Validate trigger syntax
            for (int i = 0; i < behavior.Triggers.Count; i++)
            {
                var trigger = behavior.Triggers[i];
                var triggerPath = $"{behaviorPath}.triggers[{i}]";

                if (trigger.Count == 0)
                {
                    errors.Add(new ValidationError
                    {
                        Type = ValidationErrorType.Schema,
                        Message = "Empty trigger definition",
                        YamlPath = triggerPath
                    });
                }
            }

            // Validate action syntax
            for (int i = 0; i < behavior.Actions.Count; i++)
            {
                var action = behavior.Actions[i];
                var actionPath = $"{behaviorPath}.actions[{i}]";

                if (action.Count == 0)
                {
                    errors.Add(new ValidationError
                    {
                        Type = ValidationErrorType.Schema,
                        Message = "Empty action definition",
                        YamlPath = actionPath
                    });
                }
            }
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }

    /// <summary>
    /// Extracts all context variable expressions from ABML content.
    /// </summary>
    /// <param name="content">ABML content to analyze</param>
    /// <returns>List of unique context variable expressions found</returns>
    public List<string> ExtractContextVariables(string content)
    {
        var variables = new HashSet<string>();

        var matches = ContextVariableRegex.Matches(content);
        foreach (Match match in matches)
        {
            if (match.Groups.Count > 1)
            {
                variables.Add(match.Groups[1].Value);
            }
        }

        _logger?.LogDebug("Extracted {Count} unique context variables", variables.Count);

        return variables.ToList();
    }

    /// <summary>
    /// Resolves context variables in ABML content using provided character context.
    /// </summary>
    /// <param name="content">ABML content with context variables</param>
    /// <param name="characterContext">Character context for variable resolution</param>
    /// <returns>ABML content with resolved variables</returns>
    public string ResolveContextVariables(string content, CharacterContext characterContext)
    {
        try
        {
            var resolvedContent = ContextVariableRegex.Replace(content, match =>
            {
                var expression = match.Groups[1].Value;
                var resolvedValue = ResolveContextExpression(expression, characterContext);
                
                _logger?.LogTrace("Resolved context variable '{Expression}' to '{Value}'", expression, resolvedValue);
                
                return resolvedValue?.ToString() ?? "null";
            });

            return resolvedContent;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error resolving context variables in ABML content");
            throw new AbmlParsingException("Failed to resolve context variables", ex);
        }
    }

    /// <summary>
    /// Resolves a single context expression using character context.
    /// </summary>
    /// <param name="expression">Context expression to resolve (e.g., "npc.stats.energy")</param>
    /// <param name="characterContext">Character context for resolution</param>
    /// <returns>Resolved value or null if not found</returns>
    private object? ResolveContextExpression(string expression, CharacterContext characterContext)
    {
        try
        {
            var parts = expression.Split('.');
            if (parts.Length < 2)
            {
                _logger?.LogWarning("Invalid context expression format: {Expression}", expression);
                return null;
            }

            var rootObject = parts[0].ToLowerInvariant();
            var propertyPath = parts.Skip(1).ToArray();

            return rootObject switch
            {
                "npc" => ResolveNpcProperty(propertyPath, characterContext),
                "world" => ResolveWorldProperty(propertyPath, characterContext),
                "context" => ResolveContextProperty(propertyPath, characterContext),
                _ => null
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error resolving context expression: {Expression}", expression);
            return null;
        }
    }

    /// <summary>
    /// Resolves NPC-related properties from character context.
    /// </summary>
    private object? ResolveNpcProperty(string[] propertyPath, CharacterContext characterContext)
    {
        if (propertyPath.Length == 0) return null;

        return propertyPath[0].ToLowerInvariant() switch
        {
            "id" => characterContext.NpcId,
            "culture" => characterContext.Culture,
            "profession" => characterContext.Profession,
            "stats" => ResolveNestedProperty(propertyPath.Skip(1).ToArray(), characterContext.Stats),
            "skills" => ResolveNestedProperty(propertyPath.Skip(1).ToArray(), characterContext.Skills),
            "location" => ResolveLocationProperty(propertyPath.Skip(1).ToArray(), characterContext.Location),
            "relationships" => ResolveNestedProperty(propertyPath.Skip(1).ToArray(), characterContext.Relationships),
            _ => null
        };
    }

    /// <summary>
    /// Resolves world-related properties from character context.
    /// </summary>
    private object? ResolveWorldProperty(string[] propertyPath, CharacterContext characterContext)
    {
        return ResolveNestedProperty(propertyPath, characterContext.WorldState);
    }

    /// <summary>
    /// Resolves context-related properties (typically computed values).
    /// </summary>
    private object? ResolveContextProperty(string[] propertyPath, CharacterContext characterContext)
    {
        // Context properties are computed values based on character state
        if (propertyPath.Length == 0) return null;

        return propertyPath[0].ToLowerInvariant() switch
        {
            "energy_level" => characterContext.Stats.GetValueOrDefault("energy", 0.0),
            "health_level" => characterContext.Stats.GetValueOrDefault("health", 1.0),
            "is_hungry" => characterContext.Stats.GetValueOrDefault("hunger", 0.0) > 0.7,
            "is_tired" => characterContext.Stats.GetValueOrDefault("fatigue", 0.0) > 0.8,
            _ => null
        };
    }

    /// <summary>
    /// Resolves location properties from location context.
    /// </summary>
    private object? ResolveLocationProperty(string[] propertyPath, LocationContext? location)
    {
        if (location == null || propertyPath.Length == 0) return null;

        return propertyPath[0].ToLowerInvariant() switch
        {
            "current" => location.Current,
            "region" => location.Region,
            "coordinates" => ResolveCoordinatesProperty(propertyPath.Skip(1).ToArray(), location.Coordinates),
            _ => null
        };
    }

    /// <summary>
    /// Resolves coordinate properties from coordinate point.
    /// </summary>
    private object? ResolveCoordinatesProperty(string[] propertyPath, CoordinatePoint? coordinates)
    {
        if (coordinates == null || propertyPath.Length == 0) return null;

        return propertyPath[0].ToLowerInvariant() switch
        {
            "x" => coordinates.X,
            "y" => coordinates.Y,
            "z" => coordinates.Z,
            _ => null
        };
    }

    /// <summary>
    /// Resolves nested properties from a dictionary.
    /// </summary>
    private object? ResolveNestedProperty(string[] propertyPath, Dictionary<string, object> dictionary)
    {
        if (propertyPath.Length == 0) return dictionary;

        var key = propertyPath[0];
        if (!dictionary.TryGetValue(key, out var value)) return null;

        // If there are more property path segments, try to resolve further
        if (propertyPath.Length > 1 && value is Dictionary<string, object> nestedDict)
        {
            return ResolveNestedProperty(propertyPath.Skip(1).ToArray(), nestedDict);
        }

        return value;
    }

    /// <summary>
    /// Resolves nested properties from a numeric dictionary.
    /// </summary>
    private object? ResolveNestedProperty(string[] propertyPath, Dictionary<string, double> dictionary)
    {
        if (propertyPath.Length == 0) return dictionary;
        if (propertyPath.Length > 1) return null; // Can't traverse further in numeric dictionary

        var key = propertyPath[0];
        return dictionary.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Checks if the ABML version is compatible with this parser.
    /// </summary>
    private static bool IsCompatibleVersion(string version)
    {
        // For now, we support version 1.x.x
        return version.StartsWith("1.", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Result of parsing an ABML document.
/// </summary>
public class AbmlParseResult
{
    /// <summary>
    /// Whether parsing was successful.
    /// </summary>
    public bool IsSuccess { get; private set; }

    /// <summary>
    /// Parsed ABML document (null if parsing failed).
    /// </summary>
    public AbmlDocument? Document { get; private set; }

    /// <summary>
    /// Error message if parsing failed.
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Validation errors encountered during parsing.
    /// </summary>
    public List<ValidationError> ValidationErrors { get; private set; } = new();

    private AbmlParseResult() { }

    /// <summary>
    /// Creates a successful parse result.
    /// </summary>
    public static AbmlParseResult Success(AbmlDocument document)
    {
        return new AbmlParseResult
        {
            IsSuccess = true,
            Document = document
        };
    }

    /// <summary>
    /// Creates a failed parse result.
    /// </summary>
    public static AbmlParseResult Failure(string errorMessage, IEnumerable<ValidationError>? validationErrors = null)
    {
        return new AbmlParseResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage,
            ValidationErrors = validationErrors?.ToList() ?? new List<ValidationError>()
        };
    }
}

/// <summary>
/// Result of validating an ABML document structure.
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// Whether validation passed.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// List of validation errors found.
    /// </summary>
    public List<ValidationError> Errors { get; set; } = new();
}

/// <summary>
/// Custom type converter for handling context variables in YAML.
/// </summary>
public class ContextVariableTypeConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
    {
        return type == typeof(string);
    }

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer nestedObjectDeserializer)
    {
        var value = ((Scalar)parser.Current!).Value;
        parser.MoveNext();
        return value;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer nestedObjectSerializer)
    {
        emitter.Emit(new Scalar(value?.ToString() ?? string.Empty));
    }
}

/// <summary>
/// Exception thrown when ABML parsing encounters an error.
/// </summary>
public class AbmlParsingException : Exception
{
    public AbmlParsingException(string message) : base(message) { }
    public AbmlParsingException(string message, Exception innerException) : base(message, innerException) { }
}