using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Behaviour.Generated;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Net.Mime;

namespace BeyondImmersion.BannouService.Behaviour;

/// <summary>
/// ABML Behavior APIs - backed by the ABML Behavior service.
/// Provides YAML-based behavior compilation, stackable behavior sets, and GOAP integration.
/// </summary>
[DaprController(typeof(IBehaviourService))]
[Consumes(MediaTypeNames.Application.Json, "application/yaml")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class AbmlBehaviorController : BehaviorControllerControllerBase
{
    private readonly IBehaviourService _service;
    private readonly ILogger<AbmlBehaviorController> _logger;

    public AbmlBehaviorController(IBehaviourService service, ILogger<AbmlBehaviorController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// Compiles an ABML YAML behavior definition into executable behavior trees.
    /// Handles stackable behavior sets, cultural adaptations, and context variable resolution.
    /// </summary>
    [HttpPost]
    [Route("compile")]
    [Consumes("application/yaml", "application/json")]
    public override async Task<ActionResult<CompileBehaviorResponse>> CompileAbmlBehavior(
        [FromBody] string body, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Compiling ABML behavior definition, content length: {Length}", body?.Length ?? 0);

            if (string.IsNullOrWhiteSpace(body))
            {
                return BadRequest(new AbmlErrorResponse
                {
                    Error = "ABML content is required",
                    ErrorCode = "ABML_CONTENT_REQUIRED"
                });
            }

            // For single behavior compilation, we don't need character context from the request
            // The context would come from other services or be provided in separate endpoints
            var result = await _service.CompileAbmlBehavior(body, characterContext: null);

            if (result.StatusCode == StatusCodes.OK && result.Data != null)
            {
                return Ok(result.Data);
            }

            return HandleServiceError(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error compiling ABML behavior");
            return StatusCode(500, new AbmlErrorResponse
            {
                Error = "Internal server error during ABML compilation",
                ErrorCode = "ABML_COMPILATION_ERROR",
                Details = new[] { ex.Message }
            });
        }
    }

    /// <summary>
    /// Compiles multiple ABML behavior sets with priority-based merging.
    /// Handles cultural adaptations, profession specializations, and context resolution.
    /// </summary>
    [HttpPost]
    [Route("stack/compile")]
    public override async Task<ActionResult<CompileBehaviorResponse>> CompileBehaviorStack(
        [FromBody] BehaviorStackRequest request, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Compiling behavior stack with {Count} behavior sets", request.BehaviorSets?.Length ?? 0);

            if (request.BehaviorSets == null || request.BehaviorSets.Length == 0)
            {
                return BadRequest(new AbmlErrorResponse
                {
                    Error = "At least one behavior set is required",
                    ErrorCode = "BEHAVIOR_SETS_REQUIRED"
                });
            }

            var result = await _service.CompileBehaviorStack(request.BehaviorSets, request.CharacterContext);

            if (result.StatusCode == StatusCodes.OK && result.Data != null)
            {
                // Convert stack result to standard response (the generated types expect CompileBehaviorResponse)
                var response = new CompileBehaviorResponse
                {
                    Success = result.Data.Success,
                    BehaviorId = result.Data.BehaviorId,
                    CompiledBehavior = result.Data.CompiledBehavior,
                    CompilationTimeMs = result.Data.CompilationTimeMs,
                    CacheKey = result.Data.CacheKey,
                    Warnings = result.Data.Warnings
                };

                return Ok(response);
            }

            return HandleServiceError(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error compiling behavior stack");
            return StatusCode(500, new AbmlErrorResponse
            {
                Error = "Internal server error during behavior stack compilation",
                ErrorCode = "BEHAVIOR_STACK_COMPILATION_ERROR",
                Details = new[] { ex.Message }
            });
        }
    }

    /// <summary>
    /// Validates ABML YAML against schema and checks for semantic correctness.
    /// Includes context variable validation and service dependency checking.
    /// </summary>
    [HttpPost]
    [Route("validate")]
    [Consumes("application/yaml", "application/json")]
    public override async Task<ActionResult<ValidateAbmlResponse>> ValidateAbml(
        [FromBody] string body, 
        [FromQuery] bool strictMode = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Validating ABML definition, strict mode: {StrictMode}", strictMode);

            if (string.IsNullOrWhiteSpace(body))
            {
                return BadRequest(new AbmlErrorResponse
                {
                    Error = "ABML content is required for validation",
                    ErrorCode = "ABML_CONTENT_REQUIRED"
                });
            }

            var result = await _service.ValidateAbml(body, strictMode);

            if (result.StatusCode == StatusCodes.OK && result.Data != null)
            {
                return Ok(result.Data);
            }

            return HandleServiceError(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error validating ABML");
            return StatusCode(500, new AbmlErrorResponse
            {
                Error = "Internal server error during ABML validation",
                ErrorCode = "ABML_VALIDATION_ERROR",
                Details = new[] { ex.Message }
            });
        }
    }

    /// <summary>
    /// Retrieves a previously compiled behavior from the cache.
    /// Used for performance optimization in high-frequency behavior execution.
    /// </summary>
    [HttpGet]
    [Route("cache/{behaviorId}")]
    public override async Task<ActionResult<CachedBehaviorResponse>> GetCachedBehavior(
        string behaviorId, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Retrieving cached behavior: {BehaviorId}", behaviorId);

            if (string.IsNullOrWhiteSpace(behaviorId))
            {
                return BadRequest(new AbmlErrorResponse
                {
                    Error = "Behavior ID is required",
                    ErrorCode = "BEHAVIOR_ID_REQUIRED"
                });
            }

            var result = await _service.GetCachedBehavior(behaviorId);

            if (result.StatusCode == StatusCodes.OK)
            {
                if (result.Data != null)
                {
                    return Ok(result.Data);
                }
                else
                {
                    return NotFound(new AbmlErrorResponse
                    {
                        Error = $"Behavior '{behaviorId}' not found in cache",
                        ErrorCode = "BEHAVIOR_NOT_FOUND_IN_CACHE"
                    });
                }
            }

            return HandleServiceError(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error retrieving cached behavior: {BehaviorId}", behaviorId);
            return StatusCode(500, new AbmlErrorResponse
            {
                Error = "Internal server error retrieving cached behavior",
                ErrorCode = "CACHE_RETRIEVAL_ERROR",
                Details = new[] { ex.Message }
            });
        }
    }

    /// <summary>
    /// Removes a behavior from the cache, forcing recompilation on next access.
    /// Used when behavior definitions are updated.
    /// </summary>
    [HttpDelete]
    [Route("cache/{behaviorId}")]
    public override async Task<ActionResult> InvalidateCachedBehavior(
        string behaviorId, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Invalidating cached behavior: {BehaviorId}", behaviorId);

            if (string.IsNullOrWhiteSpace(behaviorId))
            {
                return BadRequest(new AbmlErrorResponse
                {
                    Error = "Behavior ID is required",
                    ErrorCode = "BEHAVIOR_ID_REQUIRED"
                });
            }

            var result = await _service.InvalidateCachedBehavior(behaviorId);

            if (result.StatusCode == StatusCodes.OK)
            {
                return Ok();
            }
            
            if (result.StatusCode == StatusCodes.NotFound)
            {
                return NotFound(new AbmlErrorResponse
                {
                    Error = $"Behavior '{behaviorId}' not found in cache",
                    ErrorCode = "BEHAVIOR_NOT_FOUND_IN_CACHE"
                });
            }

            return HandleServiceError(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error invalidating cached behavior: {BehaviorId}", behaviorId);
            return StatusCode(500, new AbmlErrorResponse
            {
                Error = "Internal server error invalidating cached behavior",
                ErrorCode = "CACHE_INVALIDATION_ERROR",
                Details = new[] { ex.Message }
            });
        }
    }

    /// <summary>
    /// Resolves context variables in ABML definitions against character and world state.
    /// Used for dynamic behavior adaptation based on current game state.
    /// </summary>
    [HttpPost]
    [Route("context/resolve")]
    public override async Task<ActionResult<ResolveContextResponse>> ResolveContextVariables(
        [FromBody] ResolveContextRequest request, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Resolving context variables for expression: {Expression}", request.ContextExpression);

            if (string.IsNullOrWhiteSpace(request.ContextExpression))
            {
                return BadRequest(new AbmlErrorResponse
                {
                    Error = "Context expression is required",
                    ErrorCode = "CONTEXT_EXPRESSION_REQUIRED"
                });
            }

            if (request.CharacterContext == null)
            {
                return BadRequest(new AbmlErrorResponse
                {
                    Error = "Character context is required for variable resolution",
                    ErrorCode = "CHARACTER_CONTEXT_REQUIRED"
                });
            }

            var result = await _service.ResolveContextVariables(request.ContextExpression, request.CharacterContext);

            if (result.StatusCode == StatusCodes.OK && result.Data != null)
            {
                return Ok(result.Data);
            }

            return HandleServiceError(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error resolving context variables");
            return StatusCode(500, new AbmlErrorResponse
            {
                Error = "Internal server error resolving context variables",
                ErrorCode = "CONTEXT_RESOLUTION_ERROR",
                Details = new[] { ex.Message }
            });
        }
    }

    /// <summary>
    /// Handles service response errors and converts them to appropriate HTTP responses.
    /// </summary>
    private ActionResult HandleServiceError<T>(ServiceResponse<T> result)
    {
        return result.StatusCode switch
        {
            StatusCodes.BadRequest => BadRequest(new AbmlErrorResponse
            {
                Error = "Invalid request",
                ErrorCode = "INVALID_REQUEST",
                Details = new[] { result.ErrorMessage ?? "Bad request" }
            }),
            StatusCodes.Forbidden => StatusCode(403, new AbmlErrorResponse
            {
                Error = "Forbidden",
                ErrorCode = "FORBIDDEN",
                Details = new[] { result.ErrorMessage ?? "Access denied" }
            }),
            StatusCodes.NotFound => NotFound(new AbmlErrorResponse
            {
                Error = "Not found",
                ErrorCode = "NOT_FOUND",
                Details = new[] { result.ErrorMessage ?? "Resource not found" }
            }),
            _ => StatusCode(500, new AbmlErrorResponse
            {
                Error = "Internal server error",
                ErrorCode = "INTERNAL_ERROR",
                Details = new[] { result.ErrorMessage ?? "An unexpected error occurred" }
            })
        };
    }

    /// <summary>
    /// Handles service response errors and converts them to appropriate HTTP responses.
    /// </summary>
    private ActionResult HandleServiceError(ServiceResponse result)
    {
        return result.StatusCode switch
        {
            StatusCodes.BadRequest => BadRequest(new AbmlErrorResponse
            {
                Error = "Invalid request",
                ErrorCode = "INVALID_REQUEST",
                Details = new[] { result.ErrorMessage ?? "Bad request" }
            }),
            StatusCodes.Forbidden => StatusCode(403, new AbmlErrorResponse
            {
                Error = "Forbidden",
                ErrorCode = "FORBIDDEN",
                Details = new[] { result.ErrorMessage ?? "Access denied" }
            }),
            StatusCodes.NotFound => NotFound(new AbmlErrorResponse
            {
                Error = "Not found",
                ErrorCode = "NOT_FOUND",
                Details = new[] { result.ErrorMessage ?? "Resource not found" }
            }),
            _ => StatusCode(500, new AbmlErrorResponse
            {
                Error = "Internal server error",
                ErrorCode = "INTERNAL_ERROR",
                Details = new[] { result.ErrorMessage ?? "An unexpected error occurred" }
            })
        };
    }
}