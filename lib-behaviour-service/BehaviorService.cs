using BeyondImmersion.BannouService.Behaviour;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Behaviour;

/// <summary>
/// ABML behavior service implementation using schema-generated models.
/// Handles YAML-based behavior compilation, validation, and caching.
/// </summary>
public class BehaviorService : IBehaviorService
{
    private readonly ILogger<BehaviorService> _logger;
    private readonly AbmlParser _abmlParser;
    private readonly AbmlCompiler _abmlCompiler;
    private readonly ConcurrentDictionary<string, CompiledBehavior> _behaviorCache;

    public BehaviorService(ILogger<BehaviorService> logger)
    {
        _logger = logger;
        _behaviorCache = new ConcurrentDictionary<string, CompiledBehavior>();
        _abmlParser = new AbmlParser(logger as ILogger<AbmlParser>);
        _abmlCompiler = new AbmlCompiler(_abmlParser, logger as ILogger<AbmlCompiler>);
    }

    public async Task<ActionResult<CompileBehaviorResponse>> CompileAbmlBehavior(string body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Compiling ABML behavior, content length: {Length}", body.Length);

            // Parse the request (body could be YAML directly or JSON with structured request)
            var abmlContent = body;
            CharacterContext? characterContext = null;

            // Try to parse as JSON request first
            try
            {
                var request = Newtonsoft.Json.JsonConvert.DeserializeObject<CompileBehaviorRequest>(body);
                if (request?.Abml_content != null)
                {
                    abmlContent = request.Abml_content;
                    characterContext = request.Character_context;
                }
            }
            catch
            {
                // If JSON parsing fails, assume it's raw YAML content
            }

            var compilationResult = await _abmlCompiler.CompileAbmlBehavior(abmlContent, characterContext);

            if (!compilationResult.IsSuccess)
            {
                _logger.LogWarning("ABML compilation failed: {Error}", compilationResult.ErrorMessage);
                return new BadRequestObjectResult(new AbmlErrorResponse
                {
                    Error = "ABML compilation failed",
                    Error_code = "ABML_COMPILATION_ERROR",
                    Details = new[] { compilationResult.ErrorMessage }.ToList(),
                    Timestamp = DateTime.UtcNow,
                    Request_id = Guid.NewGuid().ToString()
                });
            }

            // Cache the compiled behavior if available
            if (compilationResult.Compiled_behavior != null)
            {
                _behaviorCache.TryAdd(compilationResult.Behavior_id, compilationResult.Compiled_behavior);
            }

            var response = new CompileBehaviorResponse
            {
                Success = true,
                Behavior_id = compilationResult.Behavior_id,
                Compiled_behavior = compilationResult.Compiled_behavior,
                Compilation_time_ms = (int)compilationResult.CompilationTimeMs,
                Cache_key = GenerateCacheKey(compilationResult.Behavior_id),
                Warnings = compilationResult.Warnings?.ToList() ?? new List<string>()
            };

            return new OkObjectResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during ABML compilation");
            return new ObjectResult(new AbmlErrorResponse
            {
                Error = "Internal server error during ABML compilation",
                Error_code = "ABML_INTERNAL_ERROR",
                Timestamp = DateTime.UtcNow,
                Request_id = Guid.NewGuid().ToString()
            }) { StatusCode = 500 };
        }
    }

    public async Task<ActionResult<CompileBehaviorResponse>> CompileBehaviorStack(BehaviorStackRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Compiling behavior stack with {Count} sets", body.Behavior_sets?.Count ?? 0);

            var behaviorSets = body.Behavior_sets?.Select(bs => new BehaviorSetDefinition
            {
                Id = bs.Id,
                Priority = bs.Priority,
                Category = bs.Category,
                Abml_content = bs.Abml_content,
                Metadata = bs.Metadata
            }).ToArray() ?? Array.Empty<BehaviorSetDefinition>();

            var compilationResult = await _abmlCompiler.CompileBehaviorStack(behaviorSets, body.Character_context);

            if (!compilationResult.IsSuccess)
            {
                _logger.LogWarning("Behavior stack compilation failed: {Error}", compilationResult.ErrorMessage);
                return new BadRequestObjectResult(new AbmlErrorResponse
                {
                    Error = "Behavior stack compilation failed",
                    Error_code = "STACK_COMPILATION_ERROR",
                    Details = new[] { compilationResult.ErrorMessage }.ToList(),
                    Timestamp = DateTime.UtcNow,
                    Request_id = Guid.NewGuid().ToString()
                });
            }

            // Cache the compiled behavior
            if (compilationResult.Compiled_behavior != null)
            {
                _behaviorCache.TryAdd(compilationResult.Behavior_id, compilationResult.Compiled_behavior);
            }

            var response = new CompileBehaviorResponse
            {
                Success = true,
                Behavior_id = compilationResult.Behavior_id,
                Compiled_behavior = compilationResult.Compiled_behavior,
                Compilation_time_ms = (int)compilationResult.CompilationTimeMs,
                Cache_key = GenerateCacheKey(compilationResult.Behavior_id),
                Warnings = compilationResult.Warnings?.ToList() ?? new List<string>()
            };

            return new OkObjectResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during behavior stack compilation");
            return new ObjectResult(new AbmlErrorResponse
            {
                Error = "Internal server error during stack compilation",
                Error_code = "STACK_INTERNAL_ERROR",
                Timestamp = DateTime.UtcNow,
                Request_id = Guid.NewGuid().ToString()
            }) { StatusCode = 500 };
        }
    }

    public async Task<ActionResult<ValidateAbmlResponse>> ValidateAbml(string body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Validating ABML content");

            // Parse request similar to CompileAbmlBehavior
            var abmlContent = body;
            var strictMode = false;

            try
            {
                var request = Newtonsoft.Json.JsonConvert.DeserializeObject<ValidateAbmlRequest>(body);
                if (request?.Abml_content != null)
                {
                    abmlContent = request.Abml_content;
                    strictMode = request.Strict_mode;
                }
            }
            catch
            {
                // If JSON parsing fails, assume it's raw YAML content
            }

            var parseResult = _abmlParser.ParseAbmlDocument(abmlContent);

            var response = new ValidateAbmlResponse
            {
                Is_valid = parseResult.IsSuccess,
                Schema_version = "1.0.0"
            };

            if (!parseResult.IsSuccess)
            {
                response.Validation_errors = parseResult.ValidationErrors?.ToList() ?? new List<ValidationError>();
                response.Semantic_warnings = new List<string> { "Document parsing failed" };
            }
            else if (parseResult.Document != null)
            {
                // Perform additional validation
                var structureValidation = _abmlParser.ValidateDocumentStructure(parseResult.Document);
                if (!structureValidation.IsValid)
                {
                    response.Is_valid = false;
                    response.Validation_errors = structureValidation.Errors?.ToList() ?? new List<ValidationError>();
                }

                // Extract context variables for validation
                var contextVariables = _abmlParser.ExtractContextVariables(abmlContent);
                if (contextVariables?.Any() == true)
                {
                    response.Semantic_warnings = response.Semantic_warnings ?? new List<string>();
                    response.Semantic_warnings.Add($"Found {contextVariables.Count} context variables requiring runtime resolution");
                }
            }

            return new OkObjectResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during ABML validation");
            return new ObjectResult(new AbmlErrorResponse
            {
                Error = "Internal server error during ABML validation",
                Error_code = "VALIDATION_INTERNAL_ERROR",
                Timestamp = DateTime.UtcNow,
                Request_id = Guid.NewGuid().ToString()
            }) { StatusCode = 500 };
        }
    }

    public async Task<ActionResult<CachedBehaviorResponse>> GetCachedBehavior(string behavior_id, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Retrieving cached behavior: {BehaviorId}", behavior_id);

            if (_behaviorCache.TryGetValue(behavior_id, out var cachedBehavior))
            {
                var response = new CachedBehaviorResponse
                {
                    Behavior_id = behavior_id,
                    Compiled_behavior = cachedBehavior,
                    Cache_timestamp = DateTime.UtcNow, // This would be tracked properly in a real cache implementation
                    Cache_hit = true
                };

                return new OkObjectResult(response);
            }

            return new NotFoundObjectResult(new AbmlErrorResponse
            {
                Error = "Behavior not found in cache",
                Error_code = "BEHAVIOR_NOT_CACHED",
                Timestamp = DateTime.UtcNow,
                Request_id = Guid.NewGuid().ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error retrieving cached behavior: {BehaviorId}", behavior_id);
            return new ObjectResult(new AbmlErrorResponse
            {
                Error = "Internal server error during cache retrieval",
                Error_code = "CACHE_INTERNAL_ERROR",
                Timestamp = DateTime.UtcNow,
                Request_id = Guid.NewGuid().ToString()
            }) { StatusCode = 500 };
        }
    }

    public async Task<IActionResult> InvalidateCachedBehavior(string behavior_id, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Invalidating cached behavior: {BehaviorId}", behavior_id);

            if (_behaviorCache.TryRemove(behavior_id, out _))
            {
                return new OkResult();
            }

            return new NotFoundObjectResult(new AbmlErrorResponse
            {
                Error = "Behavior not found in cache",
                Error_code = "BEHAVIOR_NOT_CACHED",
                Timestamp = DateTime.UtcNow,
                Request_id = Guid.NewGuid().ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error invalidating cached behavior: {BehaviorId}", behavior_id);
            return new ObjectResult(new AbmlErrorResponse
            {
                Error = "Internal server error during cache invalidation",
                Error_code = "CACHE_INTERNAL_ERROR",
                Timestamp = DateTime.UtcNow,
                Request_id = Guid.NewGuid().ToString()
            }) { StatusCode = 500 };
        }
    }

    public async Task<ActionResult<ResolveContextResponse>> ResolveContextVariables(ResolveContextRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Resolving context expression: {Expression}", body.Context_expression);

            // Create a simple ABML content with the expression for parsing
            var testContent = $"test: {body.Context_expression}";
            var resolvedContent = _abmlParser.ResolveContextVariables(testContent, body.Character_context);

            // Extract the resolved value (simplified implementation)
            var resolvedValue = ExtractResolvedValue(resolvedContent);
            var contextVariables = _abmlParser.ExtractContextVariables(body.Context_expression);

            var response = new ResolveContextResponse
            {
                Resolved_value = resolvedValue,
                Resolved_type = InferValueType(resolvedValue),
                Context_variables_used = contextVariables?.ToList() ?? new List<string>()
            };

            return new OkObjectResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error resolving context variables");
            return new ObjectResult(new AbmlErrorResponse
            {
                Error = "Internal server error during context variable resolution",
                Error_code = "CONTEXT_INTERNAL_ERROR",
                Timestamp = DateTime.UtcNow,
                Request_id = Guid.NewGuid().ToString()
            }) { StatusCode = 500 };
        }
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
    /// Infers the type of a resolved value for the ResolveContextResponse.
    /// </summary>
    private ResolveContextResponseResolved_type InferValueType(object? value)
    {
        return value switch
        {
            bool => ResolveContextResponseResolved_type.Boolean,
            int or long or double or float => ResolveContextResponseResolved_type.Number,
            string => ResolveContextResponseResolved_type.String,
            System.Collections.ICollection => ResolveContextResponseResolved_type.Array,
            null => ResolveContextResponseResolved_type.String, // Default fallback
            _ => ResolveContextResponseResolved_type.Object
        };
    }
}