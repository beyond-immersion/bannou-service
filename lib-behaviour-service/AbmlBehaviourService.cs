using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Behaviour.Messages;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using System.Net;
using System.Text;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Behaviour;

/// <summary>
/// Service component responsible for ABML (Arcadia Behavior Markup Language) behavior management.
/// Handles YAML-based behavior compilation, stackable behavior sets, and GOAP integration.
/// </summary>
[DaprService("behaviour", typeof(IBehaviourService))]
public sealed class AbmlBehaviourService : DaprService<BehaviourServiceConfiguration>, IBehaviourService
{
    private readonly AbmlParser _abmlParser;
    private readonly AbmlCompiler _abmlCompiler;
    private readonly ConcurrentDictionary<string, CompiledBehavior> _behaviorCache;

    // Legacy schema support for backward compatibility
    private static JSchema? _behaviourSchema;
    private static JSchema BehaviourSchema
    {
        get
        {
            _behaviourSchema ??= JSchema.Parse(Schemas.Behaviour);
            return _behaviourSchema;
        }
        set => _behaviourSchema = value;
    }

    private static JSchema? _prereqSchema;
    private static JSchema PrereqSchema
    {
        get
        {
            if (_prereqSchema == null)
            {
                var behaviourObj = JObject.Parse(Schemas.Behaviour);
                var prereqSchemaStr = behaviourObj["definitions"]?["prerequisite"]?.ToString();

                if (prereqSchemaStr == null)
                    throw new NullReferenceException(nameof(prereqSchemaStr));

                _prereqSchema = JSchema.Parse(prereqSchemaStr);
            }
            return _prereqSchema;
        }
        set => _prereqSchema = value;
    }

    /// <summary>
    /// Initializes a new instance of the AbmlBehaviourService with ABML support.
    /// </summary>
    public AbmlBehaviourService()
    {
        _behaviorCache = new ConcurrentDictionary<string, CompiledBehavior>();
        _abmlParser = new AbmlParser(Program.Logger);
        _abmlCompiler = new AbmlCompiler(_abmlParser, Program.Logger);
    }

    async Task IDaprService.OnStartAsync(CancellationToken cancellationToken)
    {
        Program.Logger?.LogInformation("ABML Behavior Service starting with parser and compiler components");
        await Task.CompletedTask;
    }

    /// <summary>
    /// Compiles an ABML YAML behavior definition into executable behavior trees.
    /// Handles context variable resolution and cultural adaptations.
    /// </summary>
    public async Task<ServiceResponse<CompileBehaviorResponse>> CompileAbmlBehavior(string abmlContent, CharacterContext? characterContext = null)
    {
        try
        {
            Program.Logger?.LogDebug("Compiling ABML behavior, content length: {Length}", abmlContent.Length);

            var compilationResult = await _abmlCompiler.CompileAbmlBehavior(abmlContent, characterContext);

            if (!compilationResult.IsSuccess)
            {
                Program.Logger?.LogWarning("ABML compilation failed: {Error}", compilationResult.ErrorMessage);
                return new ServiceResponse<CompileBehaviorResponse>(StatusCodes.BadRequest, compilationResult.ErrorMessage);
            }

            // Cache the compiled behavior if enabled
            if (compilationResult.CompiledBehavior != null)
            {
                _behaviorCache.TryAdd(compilationResult.BehaviorId, compilationResult.CompiledBehavior);
            }

            var response = new CompileBehaviorResponse
            {
                Success = true,
                BehaviorId = compilationResult.BehaviorId,
                CompiledBehavior = compilationResult.CompiledBehavior,
                CompilationTimeMs = compilationResult.CompilationTimeMs,
                CacheKey = GenerateCacheKey(compilationResult.BehaviorId),
                Warnings = compilationResult.Warnings
            };

            return new ServiceResponse<CompileBehaviorResponse>(StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            Program.Logger?.LogError(ex, "Unexpected error during ABML compilation");
            return new ServiceResponse<CompileBehaviorResponse>(StatusCodes.InternalServerError, "ABML compilation failed");
        }
    }

    /// <summary>
    /// Compiles multiple ABML behavior sets with priority-based merging.
    /// Handles cultural adaptations, profession specializations, and context resolution.
    /// </summary>
    public async Task<ServiceResponse<CompileBehaviorStack>> CompileBehaviorStack(BehaviorSetDefinition[] behaviorSets, CharacterContext? characterContext = null)
    {
        try
        {
            Program.Logger?.LogDebug("Compiling behavior stack with {Count} sets", behaviorSets.Length);

            var compilationResult = await _abmlCompiler.CompileBehaviorStack(behaviorSets, characterContext);

            if (!compilationResult.IsSuccess)
            {
                Program.Logger?.LogWarning("Behavior stack compilation failed: {Error}", compilationResult.ErrorMessage);
                return new ServiceResponse<CompileBehaviorStack>(StatusCodes.BadRequest, compilationResult.ErrorMessage);
            }

            // Cache the compiled behavior if enabled
            if (compilationResult.CompiledBehavior != null)
            {
                _behaviorCache.TryAdd(compilationResult.BehaviorId, compilationResult.CompiledBehavior);
            }

            var response = new CompileBehaviorStack
            {
                Success = true,
                BehaviorId = compilationResult.BehaviorId,
                CompiledBehavior = compilationResult.CompiledBehavior,
                CompilationTimeMs = compilationResult.CompilationTimeMs,
                CacheKey = GenerateCacheKey(compilationResult.BehaviorId),
                Warnings = compilationResult.Warnings,
                MergeInfo = (compilationResult as StackCompilationResult)?.MergeInfo ?? new List<BehaviorSetMergeInfo>()
            };

            return new ServiceResponse<CompileBehaviorStack>(StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            Program.Logger?.LogError(ex, "Unexpected error during behavior stack compilation");
            return new ServiceResponse<CompileBehaviorStack>(StatusCodes.InternalServerError, "Behavior stack compilation failed");
        }
    }

    /// <summary>
    /// Validates ABML YAML against schema and checks for semantic correctness.
    /// Includes context variable validation and service dependency checking.
    /// </summary>
    public async Task<ServiceResponse<ValidateAbmlResponse>> ValidateAbml(string abmlContent, bool strictMode = false)
    {
        try
        {
            Program.Logger?.LogDebug("Validating ABML content, strict mode: {StrictMode}", strictMode);

            var parseResult = _abmlParser.ParseAbmlDocument(abmlContent);
            
            var response = new ValidateAbmlResponse
            {
                IsValid = parseResult.IsSuccess,
                SchemaVersion = "1.0.0"
            };

            if (!parseResult.IsSuccess)
            {
                response.ValidationErrors = parseResult.ValidationErrors;
                response.SemanticWarnings = new List<string> { "Document parsing failed" };
            }
            else if (parseResult.Document != null)
            {
                // Perform additional validation
                var structureValidation = _abmlParser.ValidateDocumentStructure(parseResult.Document);
                if (!structureValidation.IsValid)
                {
                    response.IsValid = false;
                    response.ValidationErrors = structureValidation.Errors;
                }

                // Extract context variables for validation
                var contextVariables = _abmlParser.ExtractContextVariables(abmlContent);
                if (contextVariables.Any())
                {
                    response.SemanticWarnings.Add($"Found {contextVariables.Count} context variables requiring runtime resolution");
                }
            }

            return new ServiceResponse<ValidateAbmlResponse>(StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            Program.Logger?.LogError(ex, "Unexpected error during ABML validation");
            return new ServiceResponse<ValidateAbmlResponse>(StatusCodes.InternalServerError, "ABML validation failed");
        }
    }

    /// <summary>
    /// Retrieves a previously compiled behavior from the cache.
    /// Used for performance optimization in high-frequency behavior execution.
    /// </summary>
    public async Task<ServiceResponse<CachedBehaviorResponse?>> GetCachedBehavior(string behaviorId)
    {
        try
        {
            Program.Logger?.LogDebug("Retrieving cached behavior: {BehaviorId}", behaviorId);

            if (_behaviorCache.TryGetValue(behaviorId, out var cachedBehavior))
            {
                var response = new CachedBehaviorResponse
                {
                    BehaviorId = behaviorId,
                    CompiledBehavior = cachedBehavior,
                    CacheTimestamp = DateTime.UtcNow, // This would be tracked properly in a real cache implementation
                    CacheHit = true
                };

                return new ServiceResponse<CachedBehaviorResponse?>(StatusCodes.OK, response);
            }

            return new ServiceResponse<CachedBehaviorResponse?>(StatusCodes.NotFound, null, "Behavior not found in cache");
        }
        catch (Exception ex)
        {
            Program.Logger?.LogError(ex, "Unexpected error retrieving cached behavior: {BehaviorId}", behaviorId);
            return new ServiceResponse<CachedBehaviorResponse?>(StatusCodes.InternalServerError, null, "Cache retrieval failed");
        }
    }

    /// <summary>
    /// Removes a behavior from the cache, forcing recompilation on next access.
    /// Used when behavior definitions are updated.
    /// </summary>
    public async Task<ServiceResponse> InvalidateCachedBehavior(string behaviorId)
    {
        try
        {
            Program.Logger?.LogDebug("Invalidating cached behavior: {BehaviorId}", behaviorId);

            if (_behaviorCache.TryRemove(behaviorId, out _))
            {
                return new ServiceResponse(StatusCodes.OK);
            }

            return new ServiceResponse(StatusCodes.NotFound, "Behavior not found in cache");
        }
        catch (Exception ex)
        {
            Program.Logger?.LogError(ex, "Unexpected error invalidating cached behavior: {BehaviorId}", behaviorId);
            return new ServiceResponse(StatusCodes.InternalServerError, "Cache invalidation failed");
        }
    }

    /// <summary>
    /// Resolves context variables in ABML definitions against character and world state.
    /// Used for dynamic behavior adaptation based on current game state.
    /// </summary>
    public async Task<ServiceResponse<ResolveContextResponse>> ResolveContextVariables(string contextExpression, CharacterContext characterContext)
    {
        try
        {
            Program.Logger?.LogDebug("Resolving context expression: {Expression}", contextExpression);

            // Create a simple ABML content with the expression for parsing
            var testContent = $"test: {contextExpression}";
            var resolvedContent = _abmlParser.ResolveContextVariables(testContent, characterContext);
            
            // Extract the resolved value (this is a simplified implementation)
            var resolvedValue = ExtractResolvedValue(resolvedContent);
            var contextVariables = _abmlParser.ExtractContextVariables(contextExpression);

            var response = new ResolveContextResponse
            {
                ResolvedValue = resolvedValue,
                ResolvedType = InferValueType(resolvedValue),
                ContextVariablesUsed = contextVariables
            };

            return new ServiceResponse<ResolveContextResponse>(StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            Program.Logger?.LogError(ex, "Unexpected error resolving context variables");
            return new ServiceResponse<ResolveContextResponse>(StatusCodes.InternalServerError, "Context variable resolution failed");
        }
    }

    /// <summary>
    /// Legacy method - adds a new behaviour tree to the system.
    /// Maintained for backward compatibility with existing JSON behavior system.
    /// </summary>
    [Obsolete("Use CompileAbmlBehavior for new YAML-based behaviors")]
    public async Task<ServiceResponse> AddBehaviourTree()
    {
        Program.Logger?.LogWarning("Legacy AddBehaviourTree method called. Consider migrating to ABML.");
        await Task.CompletedTask;
        return new ServiceResponse(StatusCodes.OK);
    }

    /// <summary>
    /// Generates a cache key for a compiled behavior.
    /// </summary>
    private string GenerateCacheKey(string behaviorId)
    {
        return $"abml_behavior_{behaviorId}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
    }

    /// <summary>
    /// Extracts the resolved value from resolved ABML content.
    /// This is a simplified implementation for demo purposes.
    /// </summary>
    private object? ExtractResolvedValue(string resolvedContent)
    {
        // Simple extraction - in a real implementation, this would be more sophisticated
        var lines = resolvedContent.Split('\n');
        var testLine = lines.FirstOrDefault(l => l.StartsWith("test:"));
        
        if (testLine != null)
        {
            var value = testLine.Substring("test:".Length).Trim();
            
            // Try to parse as different types
            if (bool.TryParse(value, out var boolValue)) return boolValue;
            if (double.TryParse(value, out var doubleValue)) return doubleValue;
            if (int.TryParse(value, out var intValue)) return intValue;
            
            return value; // Return as string
        }

        return null;
    }

    /// <summary>
    /// Infers the type of a resolved value.
    /// </summary>
    private string InferValueType(object? value)
    {
        return value switch
        {
            bool => "boolean",
            int or long => "number",
            double or float => "number",
            string => "string",
            null => "null",
            _ => "object"
        };
    }

    #region Legacy JSON Behavior Support (maintained for backward compatibility)

    public static bool ResolveBehaviourReferences(JObject behaviour, Dictionary<string, JObject> refLookup)
    {
        var allResolved = true;
        var childTokens = new Stack<JToken>(behaviour.Children());

        while (childTokens.Count > 0)
        {
            JToken childToken = childTokens.Pop();

            if (childToken is JObject childObj && childObj.Count == 1)
            {
                var refString = (string?)childObj.Property("$ref")?.Value;

                if (refString == null)
                    continue;

                if (refLookup.TryGetValue(refString, out JObject? matchedBehaviour))
                {
                    if (matchedBehaviour == null)
                        throw new NullReferenceException(nameof(matchedBehaviour));

                    childObj.Replace(matchedBehaviour.DeepClone());
                }
                else
                {
                    allResolved = false;
                    break;
                }
            }
            else
                foreach (JToken childChildToken in childToken.Children())
                    childTokens.Push(childChildToken);
        }

        return allResolved;
    }

    public static bool ResolveBehavioursAndAddToLookup(JObject[] behaviours, ref Dictionary<string, JObject>? refLookup, out IList<string> validationErrors)
    {
        int lastErrorCount;
        var thisErrorCount = 0;
        var errors = new List<string>();

        refLookup ??= [];

        do
        {
            lastErrorCount = thisErrorCount;
            thisErrorCount = 0;
            errors.Clear();

            foreach (var behaviour in behaviours)
            {
                var resolvedSuccess = ResolveAndValidateBehaviour(behaviour, refLookup, out var newErrors);

                if (!resolvedSuccess)
                {
                    errors.AddRange(newErrors);
                    thisErrorCount++;
                }
                else
                {
                    var name = (string?)behaviour["name"];

                    if (string.IsNullOrWhiteSpace(name))
                        throw new ArgumentNullException(nameof(name));

                    refLookup.Add(name, behaviour);
                }
            }
        }
        while (thisErrorCount > 0 && thisErrorCount < lastErrorCount);
        // keep going as long as more refs are being resolved each time

        validationErrors = errors;

        return thisErrorCount == 0;
    }

    public static bool ResolveAndValidateBehaviour(JObject behaviour, Dictionary<string, JObject> refLookup, out IList<string> validationErrors)
    {
        var resolved = ResolveBehaviourReferences(behaviour, refLookup);
        var name = (string?)behaviour["name"];

        if (!resolved)
        {
            Program.Logger.LogError($"Could not resolve references for behaviour [{name}].");
            validationErrors = new List<string>() { $"Could not resolve all references for behaviour [{name}]" };

            return false;
        }

        return IsValidBehaviour(behaviour, out validationErrors);
    }

    public static bool IsValidBehaviour(JObject behaviour, out IList<string> validationErrors) => behaviour.IsValid(BehaviourSchema, out validationErrors);

    public static bool IsValidBehaviour(string behaviourStr, out IList<string> validationErrors)
    {
        var jsonObj = JObject.Parse(behaviourStr);
        var name = (string?)jsonObj["name"];

        validationErrors = new List<string>();

        var valid = IsValidBehaviour(jsonObj, out var errors);
        if (!valid)
        {
            Program.Logger.LogError($"Invalid behaviour [{name}].");

            var sb = new StringBuilder();
            sb.AppendLine($"Behaviour [{name}] contains validation errors.");
            sb.Append(errors);

            validationErrors.Add(sb.ToString());
        }

        return valid;
    }

    public static bool IsValidPrerequite(JObject prereq, out IList<string> validationErrors) => prereq.IsValid(PrereqSchema, out validationErrors);

    public static bool IsValidPrerequite(string prereqStr, out IList<string> validationErrors)
    {
        var jsonObj = JObject.Parse(prereqStr);
        var name = (string?)jsonObj["name"];

        validationErrors = new List<string>();

        var valid = IsValidPrerequite(jsonObj, out var errors);
        if (!valid)
        {
            Program.Logger.LogError($"Invalid prerequite [{name}].");

            var sb = new StringBuilder();
            sb.AppendLine($"Prerequite [{name}] contains validation errors.");
            sb.Append(errors);

            validationErrors.Add(sb.ToString());
        }

        return valid;
    }

    #endregion
}