using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BeyondImmersion.BannouService.Behaviour;

/// <summary>
/// ABML (Arcadia Behavior Markup Language) compiler for creating executable behavior trees.
/// Handles stackable behavior sets, priority-based merging, and GOAP integration.
/// </summary>
public class AbmlCompiler
{
    private readonly AbmlParser _parser;
    private readonly ILogger<AbmlCompiler>? _logger;

    /// <summary>
    /// Initializes a new instance of the AbmlCompiler.
    /// </summary>
    public AbmlCompiler(AbmlParser parser, ILogger<AbmlCompiler>? logger = null)
    {
        _parser = parser;
        _logger = logger;
    }

    /// <summary>
    /// Compiles a single ABML document into an executable behavior tree.
    /// </summary>
    /// <param name="abmlContent">Raw ABML YAML content</param>
    /// <param name="characterContext">Character context for variable resolution</param>
    /// <param name="options">Compilation options</param>
    /// <returns>Compilation result with executable behavior tree</returns>
    public async Task<CompilationResult> CompileAbmlBehavior(
        string abmlContent,
        CharacterContext? characterContext = null,
        CompilationOptions? options = null)
    {
        try
        {
            var startTime = DateTime.UtcNow;
            options ??= new CompilationOptions();

            _logger?.LogDebug("Starting ABML compilation for single document");

            // Parse the ABML document
            var parseResult = _parser.ParseAbmlDocument(abmlContent);
            if (!parseResult.IsSuccess)
            {
                return CompilationResult.Failure("ABML parsing failed", parseResult.ValidationErrors);
            }

            var document = parseResult.Document!;

            // Resolve context variables if character context is provided
            string resolvedContent = abmlContent;
            if (characterContext != null && options.ResolveContextVariables)
            {
                resolvedContent = _parser.ResolveContextVariables(abmlContent, characterContext);

                // Re-parse with resolved variables
                var resolvedParseResult = _parser.ParseAbmlDocument(resolvedContent);
                if (!resolvedParseResult.IsSuccess)
                {
                    return CompilationResult.Failure("Context variable resolution failed", resolvedParseResult.ValidationErrors);
                }
                document = resolvedParseResult.Document!;
            }

            // Compile the behavior tree
            var behaviorTree = await CompileBehaviorTree(document, characterContext, options);

            // Extract GOAP goals if enabled
            var goapGoals = options.GenerateGoapGoals
                ? ExtractGoapGoals(document)
                : new List<GoapGoal>();

            // Calculate execution metadata
            var executionMetadata = CalculateExecutionMetadata(document, behaviorTree);

            var compiledBehavior = new CompiledBehavior
            {
                Behavior_tree = behaviorTree,
                Context_schema = ExtractContextSchema(document),
                Service_dependencies = ExtractServiceDependencies(document),
                Goap_goals = goapGoals,
                Execution_metadata = executionMetadata
            };

            var compilationTime = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
            var behaviorId = GenerateBehaviorId(document);

            return CompilationResult.Success(behaviorId, compiledBehavior, compilationTime);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error during ABML compilation");
            return CompilationResult.Failure($"Compilation error: {ex.Message}");
        }
    }

    /// <summary>
    /// Compiles multiple ABML behavior sets with priority-based merging.
    /// </summary>
    /// <param name="behaviorSets">Array of behavior sets to compile together</param>
    /// <param name="characterContext">Character context for variable resolution</param>
    /// <param name="options">Compilation options</param>
    /// <returns>Stack compilation result with merged behavior tree</returns>
    public async Task<StackCompilationResult> CompileBehaviorStack(
        BehaviorSetDefinition[] behaviorSets,
        CharacterContext? characterContext = null,
        CompilationOptions? options = null)
    {
        try
        {
            var startTime = DateTime.UtcNow;
            options ??= new CompilationOptions();

            _logger?.LogDebug("Starting ABML stack compilation for {Count} behavior sets", behaviorSets.Length);

            // Sort behavior sets by priority (higher priority first)
            var sortedSets = behaviorSets.OrderByDescending(s => s.Priority).ToArray();

            // Parse all behavior sets
            var parsedDocuments = new List<(BehaviorSetDefinition Set, AbmlDocument Document)>();
            var allValidationErrors = new List<ValidationError>();

            foreach (var behaviorSet in sortedSets)
            {
                var parseResult = _parser.ParseAbmlDocument(behaviorSet.Abml_content);
                if (!parseResult.IsSuccess)
                {
                    allValidationErrors.AddRange(parseResult.ValidationErrors);
                    continue;
                }

                parsedDocuments.Add((behaviorSet, parseResult.Document!));
            }

            if (allValidationErrors.Any())
            {
                return StackCompilationResult.Failure("Behavior set parsing failed", allValidationErrors);
            }

            // Merge documents with priority resolution
            var mergeResult = await MergeBehaviorDocuments(parsedDocuments, characterContext, options);
            if (!mergeResult.IsSuccess)
            {
                return StackCompilationResult.Failure("Behavior set merging failed", mergeResult.ValidationErrors);
            }

            // Compile the merged behavior tree
            var compiledResult = await CompileAbmlBehavior(mergeResult.MergedContent!, characterContext, options);
            if (!compiledResult.IsSuccess)
            {
                return StackCompilationResult.Failure("Merged behavior compilation failed", compiledResult.ValidationErrors);
            }

            var compilationTime = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

            return StackCompilationResult.Success(
                compiledResult.Behavior_id,
                compiledResult.Compiled_behavior!,
                compilationTime,
                mergeResult.MergeInfo);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error during behavior stack compilation");
            return StackCompilationResult.Failure($"Stack compilation error: {ex.Message}");
        }
    }

    /// <summary>
    /// Merges multiple ABML documents with priority-based conflict resolution.
    /// </summary>
    private Task<MergeResult> MergeBehaviorDocuments(
        List<(BehaviorSetDefinition Set, AbmlDocument Document)> parsedDocuments,
        CharacterContext? characterContext,
        CompilationOptions options)
    {
        var mergedDocument = new AbmlDocument
        {
            Version = "1.0.0",
            Metadata = new AbmlMetadata
            {
                Id = $"merged_{Guid.NewGuid():N}",
                Category = "merged",
                Priority = parsedDocuments.Max(d => d.Document.Metadata?.Priority ?? 50),
                Description = "Merged behavior set from multiple sources"
            },
            Context = new AbmlContext(),
            Behaviors = new Dictionary<string, AbmlBehavior>()
        };

        var mergeInfo = new List<BehaviorSetMergeInfo>();

        // Process documents in priority order (already sorted)
        foreach (var (set, document) in parsedDocuments)
        {
            var setMergeInfo = new BehaviorSetMergeInfo
            {
                BehaviorSetId = set.Id,
                Priority = set.Priority,
                OverriddenBehaviors = new List<string>()
            };

            // Merge context
            if (document.Context != null)
            {
                MergeContext(mergedDocument.Context!, document.Context, setMergeInfo);
            }

            // Merge behaviors
            foreach (var behaviorKvp in document.Behaviors)
            {
                var behaviorName = behaviorKvp.Key;
                var behavior = behaviorKvp.Value;

                if (mergedDocument.Behaviors.ContainsKey(behaviorName))
                {
                    // Behavior already exists, check if this set should override
                    if (ShouldOverrideBehavior(behaviorName, set, parsedDocuments))
                    {
                        mergedDocument.Behaviors[behaviorName] = behavior;
                        setMergeInfo.OverriddenBehaviors.Add(behaviorName);
                        setMergeInfo.TookPrecedence = true;

                        _logger?.LogDebug("Behavior '{Name}' overridden by set '{SetId}' (priority {Priority})",
                            behaviorName, set.Id, set.Priority);
                    }
                }
                else
                {
                    // New behavior, add it
                    mergedDocument.Behaviors[behaviorName] = behavior;
                    setMergeInfo.TookPrecedence = true;
                }
            }

            mergeInfo.Add(setMergeInfo);
        }

        // Serialize the merged document back to YAML
        var serializer = new YamlDotNet.Serialization.SerializerBuilder()
            .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.UnderscoredNamingConvention.Instance)
            .Build();

        var mergedContent = serializer.Serialize(mergedDocument);

        return Task.FromResult(MergeResult.Success(mergedContent, mergeInfo));
    }

    /// <summary>
    /// Merges context sections from multiple documents.
    /// </summary>
    private void MergeContext(AbmlContext mergedContext, AbmlContext sourceContext, BehaviorSetMergeInfo mergeInfo)
    {
        // Merge variables (later sets override earlier ones)
        foreach (var variable in sourceContext.Variables)
        {
            mergedContext.Variables[variable.Key] = variable.Value;
        }

        // Merge requirements
        foreach (var requirement in sourceContext.Requirements)
        {
            mergedContext.Requirements[requirement.Key] = requirement.Value;
        }

        // Merge services (avoid duplicates)
        foreach (var service in sourceContext.Services)
        {
            if (!mergedContext.Services.Any(s => s.Name == service.Name))
            {
                mergedContext.Services.Add(service);
            }
        }
    }

    /// <summary>
    /// Determines if a behavior should be overridden based on priority.
    /// </summary>
    private bool ShouldOverrideBehavior(
        string behaviorName,
        BehaviorSetDefinition currentSet,
        List<(BehaviorSetDefinition Set, AbmlDocument Document)> allSets)
    {
        // Find the highest priority set that defines this behavior
        var highestPriority = allSets
            .Where(s => s.Document.Behaviors.ContainsKey(behaviorName))
            .Max(s => s.Set.Priority);

        return currentSet.Priority >= highestPriority;
    }

    /// <summary>
    /// Compiles a behavior tree from an ABML document.
    /// </summary>
    private async Task<Dictionary<string, object>> CompileBehaviorTree(
        AbmlDocument document,
        CharacterContext? characterContext,
        CompilationOptions options)
    {
        var behaviorTree = new Dictionary<string, object>
        {
            ["type"] = "sequence",
            ["name"] = document.Metadata?.Id ?? "compiled_behavior",
            ["behaviors"] = new List<Dictionary<string, object>>()
        };

        var behaviors = (List<Dictionary<string, object>>)behaviorTree["behaviors"];

        foreach (var behaviorKvp in document.Behaviors)
        {
            var behaviorName = behaviorKvp.Key;
            var behavior = behaviorKvp.Value;

            var compiledBehavior = new Dictionary<string, object>
            {
                ["name"] = behaviorName,
                ["type"] = "sequence"
            };

            // Compile triggers into preconditions
            if (behavior.Triggers.Any())
            {
                compiledBehavior["preconditions"] = CompileTriggers(behavior.Triggers);
            }

            // Add explicit preconditions
            if (behavior.Preconditions.Any())
            {
                var existingPreconditions = (List<object>)compiledBehavior.GetValueOrDefault("preconditions", new List<object>());
                existingPreconditions.AddRange(behavior.Preconditions);
                compiledBehavior["preconditions"] = existingPreconditions;
            }

            // Compile actions
            if (behavior.Actions.Any())
            {
                compiledBehavior["actions"] = await CompileActions(behavior.Actions, characterContext, options);
            }

            // Add GOAP goals
            if (behavior.Goals.Any())
            {
                compiledBehavior["goals"] = behavior.Goals;
            }

            behaviors.Add(compiledBehavior);
        }

        return behaviorTree;
    }

    /// <summary>
    /// Compiles trigger definitions into executable preconditions.
    /// </summary>
    private List<object> CompileTriggers(List<Dictionary<string, object>> triggers)
    {
        var preconditions = new List<object>();

        foreach (var trigger in triggers)
        {
            foreach (var triggerKvp in trigger)
            {
                var triggerType = triggerKvp.Key;
                var triggerValue = triggerKvp.Value;

                var precondition = triggerType switch
                {
                    "time_range" => CompileTimeRangeTrigger(triggerValue.ToString()!),
                    "condition" => CompileConditionTrigger(triggerValue.ToString()!),
                    "event" => CompileEventTrigger(triggerValue.ToString()!),
                    _ => new { type = "custom", condition = triggerValue }
                };

                preconditions.Add(precondition);
            }
        }

        return preconditions;
    }

    /// <summary>
    /// Compiles a time range trigger into a precondition.
    /// </summary>
    private object CompileTimeRangeTrigger(string timeRange)
    {
        // Parse time range like "06:00-09:00"
        var timeRangeRegex = new Regex(@"(\d{2}):(\d{2})-(\d{2}):(\d{2})");
        var match = timeRangeRegex.Match(timeRange);

        if (match.Success)
        {
            return new
            {
                type = "time_range",
                start_hour = int.Parse(match.Groups[1].Value),
                start_minute = int.Parse(match.Groups[2].Value),
                end_hour = int.Parse(match.Groups[3].Value),
                end_minute = int.Parse(match.Groups[4].Value)
            };
        }

        return new { type = "time_range", raw = timeRange };
    }

    /// <summary>
    /// Compiles a condition trigger into a precondition.
    /// </summary>
    private object CompileConditionTrigger(string condition)
    {
        return new
        {
            type = "condition",
            expression = condition
        };
    }

    /// <summary>
    /// Compiles an event trigger into a precondition.
    /// </summary>
    private object CompileEventTrigger(string eventName)
    {
        return new
        {
            type = "event",
            event_name = eventName
        };
    }

    /// <summary>
    /// Compiles action definitions into executable actions.
    /// </summary>
    private Task<List<Dictionary<string, object>>> CompileActions(
        List<Dictionary<string, object>> actions,
        CharacterContext? characterContext,
        CompilationOptions options)
    {
        var compiledActions = new List<Dictionary<string, object>>();

        foreach (var action in actions)
        {
            foreach (var actionKvp in action)
            {
                var actionType = actionKvp.Key;
                var actionParams = actionKvp.Value;

                var compiledAction = new Dictionary<string, object>
                {
                    ["type"] = actionType
                };

                // Handle different action parameter types
                if (actionParams is Dictionary<object, object> paramDict)
                {
                    foreach (var param in paramDict)
                    {
                        compiledAction[param.Key.ToString()!] = param.Value;
                    }
                }
                else
                {
                    compiledAction["value"] = actionParams;
                }

                // Add service integration for service calls
                if (IsServiceAction(actionType))
                {
                    compiledAction["service_integration"] = true;
                }

                compiledActions.Add(compiledAction);
            }
        }

        return Task.FromResult(compiledActions);
    }

    /// <summary>
    /// Determines if an action type represents a service call.
    /// </summary>
    private bool IsServiceAction(string actionType)
    {
        // Actions that involve service calls typically have specific patterns
        var serviceActionPatterns = new[]
        {
            "service",
            "call",
            "get_",
            "post_",
            "update_",
            "delete_"
        };

        return serviceActionPatterns.Any(pattern =>
            actionType.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Extracts GOAP goals from an ABML document.
    /// </summary>
    private List<GoapGoal> ExtractGoapGoals(AbmlDocument document)
    {
        var goals = new List<GoapGoal>();

        foreach (var behaviorKvp in document.Behaviors)
        {
            var behaviorName = behaviorKvp.Key;
            var behavior = behaviorKvp.Value;

            foreach (var goalName in behavior.Goals)
            {
                var goal = new GoapGoal
                {
                    Name = goalName,
                    Description = $"Goal extracted from behavior '{behaviorName}'",
                    Priority = 50,
                    Conditions = ExtractGoalConditions(behavior),
                    Preconditions = ExtractGoalPreconditions(behavior)
                };

                goals.Add(goal);
            }
        }

        return goals;
    }

    /// <summary>
    /// Extracts goal conditions from behavior actions.
    /// </summary>
    private Dictionary<string, double> ExtractGoalConditions(AbmlBehavior behavior)
    {
        var conditions = new Dictionary<string, double>();

        // Analyze actions to infer goal conditions
        foreach (var action in behavior.Actions)
        {
            foreach (var actionKvp in action)
            {
                var actionType = actionKvp.Key;

                // Infer conditions based on action types
                if (actionType.Contains("satisfy", StringComparison.OrdinalIgnoreCase))
                {
                    if (actionType.Contains("hunger", StringComparison.OrdinalIgnoreCase))
                        conditions["hunger_level"] = 0.0;
                    else if (actionType.Contains("thirst", StringComparison.OrdinalIgnoreCase))
                        conditions["thirst_level"] = 0.0;
                }
                else if (actionType.Contains("craft", StringComparison.OrdinalIgnoreCase))
                {
                    conditions["has_crafted_item"] = 1.0;
                }
            }
        }

        return conditions;
    }

    /// <summary>
    /// Extracts goal preconditions from behavior triggers and preconditions.
    /// </summary>
    private Dictionary<string, double> ExtractGoalPreconditions(AbmlBehavior behavior)
    {
        var preconditions = new Dictionary<string, double>();

        // Analyze preconditions to infer requirements
        foreach (var precondition in behavior.Preconditions)
        {
            if (precondition.Contains("energy", StringComparison.OrdinalIgnoreCase))
                preconditions["energy_level"] = 0.5;
            else if (precondition.Contains("skill", StringComparison.OrdinalIgnoreCase))
                preconditions["skill_available"] = 1.0;
        }

        return preconditions;
    }

    /// <summary>
    /// Extracts context schema from an ABML document.
    /// </summary>
    private Dictionary<string, object> ExtractContextSchema(AbmlDocument document)
    {
        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>()
        };

        var properties = (Dictionary<string, object>)schema["properties"];

        if (document.Context?.Variables != null)
        {
            foreach (var variable in document.Context.Variables)
            {
                properties[variable.Key] = new
                {
                    type = InferVariableType(variable.Value),
                    description = $"Context variable from ABML definition"
                };
            }
        }

        return schema;
    }

    /// <summary>
    /// Extracts service dependencies from an ABML document.
    /// </summary>
    private List<string> ExtractServiceDependencies(AbmlDocument document)
    {
        var dependencies = new List<string>();

        if (document.Context?.Services != null)
        {
            dependencies.AddRange(document.Context.Services.Where(s => s.Required).Select(s => s.Name));
        }

        return dependencies;
    }

    /// <summary>
    /// Calculates execution metadata for a compiled behavior.
    /// </summary>
    private Execution_metadata CalculateExecutionMetadata(AbmlDocument document, Dictionary<string, object> behaviorTree)
    {
        var metadata = new Execution_metadata();

        // Estimate duration based on number of actions
        var totalActions = document.Behaviors.Sum(b => b.Value.Actions.Count);
        metadata.Estimated_duration = Math.Max(1, totalActions * 2); // 2 seconds per action as base estimate

        // Analyze resource requirements
        metadata.Resource_requirements = new Dictionary<string, double>
        {
            ["cpu"] = Math.Min(1.0, totalActions * 0.1),
            ["memory"] = Math.Min(1.0, totalActions * 0.05)
        };

        // Extract interrupt conditions from triggers
        metadata.Interrupt_conditions = new List<string>();
        foreach (var behavior in document.Behaviors.Values)
        {
            foreach (var trigger in behavior.Triggers)
            {
                foreach (var triggerKvp in trigger)
                {
                    if (triggerKvp.Key == "condition")
                    {
                        metadata.Interrupt_conditions.Add(triggerKvp.Value.ToString()!);
                    }
                }
            }
        }

        return metadata;
    }

    /// <summary>
    /// Infers the type of a context variable value.
    /// </summary>
    private string InferVariableType(object value)
    {
        return value switch
        {
            bool => "boolean",
            int or long or double or float => "number",
            string => "string",
            IEnumerable<object> => "array",
            _ => "object"
        };
    }

    /// <summary>
    /// Generates a unique behavior ID for a compiled behavior.
    /// </summary>
    private string GenerateBehaviorId(AbmlDocument document)
    {
        var baseId = document.Metadata?.Id ?? "behavior";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hash = Math.Abs(document.GetHashCode());

        return $"{baseId}_{timestamp}_{hash:X8}";
    }
}

/// <summary>
/// Compilation options for ABML behavior compilation (extends generated type).
/// </summary>
public partial class CompilationOptions
{
    /// <summary>
    /// Whether to resolve context variables during compilation.
    /// </summary>
    public bool ResolveContextVariables { get; set; } = true;

    /// <summary>
    /// Whether to generate GOAP goals from behaviors (maps to generated Goap_integration).
    /// </summary>
    public bool GenerateGoapGoals => Goap_integration;
}

/// <summary>
/// Result of compiling an ABML behavior.
/// </summary>
public class CompilationResult
{
    /// <summary>
    /// Whether compilation was successful.
    /// </summary>
    public bool IsSuccess { get; protected set; }

    /// <summary>
    /// Unique identifier for the compiled behavior.
    /// </summary>
    public string Behavior_id { get; protected set; } = string.Empty;

    /// <summary>
    /// The compiled behavior (null if compilation failed).
    /// </summary>
    public CompiledBehavior? Compiled_behavior { get; protected set; }

    /// <summary>
    /// Time taken to compile in milliseconds.
    /// </summary>
    public int CompilationTimeMs { get; protected set; }

    /// <summary>
    /// Error message if compilation failed.
    /// </summary>
    public string? ErrorMessage { get; protected set; }

    /// <summary>
    /// Validation errors encountered during compilation.
    /// </summary>
    public List<ValidationError> ValidationErrors { get; protected set; } = new();

    /// <summary>
    /// Non-fatal warnings during compilation.
    /// </summary>
    public List<string> Warnings { get; protected set; } = new();

    protected CompilationResult() { }

    /// <summary>
    /// Creates a successful compilation result.
    /// </summary>
    public static CompilationResult Success(string behaviorId, CompiledBehavior compiledBehavior, int compilationTimeMs)
    {
        return new CompilationResult
        {
            IsSuccess = true,
            Behavior_id = behaviorId,
            Compiled_behavior = compiledBehavior,
            CompilationTimeMs = compilationTimeMs
        };
    }

    /// <summary>
    /// Creates a failed compilation result.
    /// </summary>
    public static CompilationResult Failure(string errorMessage, IEnumerable<ValidationError>? validationErrors = null)
    {
        return new CompilationResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage,
            ValidationErrors = validationErrors?.ToList() ?? new List<ValidationError>()
        };
    }
}

/// <summary>
/// Result of compiling a behavior stack.
/// </summary>
public class StackCompilationResult : CompilationResult
{
    /// <summary>
    /// Information about how behavior sets were merged.
    /// </summary>
    public List<BehaviorSetMergeInfo> MergeInfo { get; protected set; } = new();

    protected StackCompilationResult() { }

    /// <summary>
    /// Creates a successful stack compilation result.
    /// </summary>
    public static StackCompilationResult Success(
        string behaviorId,
        CompiledBehavior compiledBehavior,
        int compilationTimeMs,
        List<BehaviorSetMergeInfo> mergeInfo)
    {
        return new StackCompilationResult
        {
            IsSuccess = true,
            Behavior_id = behaviorId,
            Compiled_behavior = compiledBehavior,
            CompilationTimeMs = compilationTimeMs,
            MergeInfo = mergeInfo
        };
    }

    /// <summary>
    /// Creates a failed stack compilation result.
    /// </summary>
    public static new StackCompilationResult Failure(string errorMessage, IEnumerable<ValidationError>? validationErrors = null)
    {
        return new StackCompilationResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage,
            ValidationErrors = validationErrors?.ToList() ?? new List<ValidationError>()
        };
    }
}

/// <summary>
/// Result of merging behavior documents.
/// </summary>
public class MergeResult
{
    /// <summary>
    /// Whether merging was successful.
    /// </summary>
    public bool IsSuccess { get; protected set; }

    /// <summary>
    /// Merged ABML content as YAML string.
    /// </summary>
    public string? MergedContent { get; protected set; }

    /// <summary>
    /// Information about how sets were merged.
    /// </summary>
    public List<BehaviorSetMergeInfo> MergeInfo { get; protected set; } = new();

    /// <summary>
    /// Validation errors encountered during merging.
    /// </summary>
    public List<ValidationError> ValidationErrors { get; protected set; } = new();

    /// <summary>
    /// Error message if merging failed.
    /// </summary>
    public string? ErrorMessage { get; protected set; }

    protected MergeResult() { }

    /// <summary>
    /// Creates a successful merge result.
    /// </summary>
    public static MergeResult Success(string mergedContent, List<BehaviorSetMergeInfo> mergeInfo)
    {
        return new MergeResult
        {
            IsSuccess = true,
            MergedContent = mergedContent,
            MergeInfo = mergeInfo
        };
    }

    /// <summary>
    /// Creates a failed merge result.
    /// </summary>
    public static MergeResult Failure(string errorMessage, IEnumerable<ValidationError>? validationErrors = null)
    {
        return new MergeResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage,
            ValidationErrors = validationErrors?.ToList() ?? new List<ValidationError>()
        };
    }
}
