using BeyondImmersion.Bannou.Behavior.Compiler;
using BeyondImmersion.Bannou.Behavior.Goap;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using InternalCompilationOptions = BeyondImmersion.Bannou.Behavior.Compiler.CompilationOptions;
using InternalGoapGoal = BeyondImmersion.Bannou.Behavior.Goap.GoapGoal;

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Behavior service implementation for ABML (Agent Behavior Markup Language) processing.
/// Provides GOAP planning and behavior compilation services.
/// </summary>
[BannouService("behavior", typeof(IBehaviorService), lifetime: ServiceLifetime.Scoped)]
public partial class BehaviorService : IBehaviorService
{
    /// <summary>
    /// Content type for compiled behavior model assets.
    /// </summary>
    public const string BehaviorModelContentType = "application/x-behavior-model";

    /// <summary>
    /// ABML schema version for compiled behaviors.
    /// </summary>
    public const string AbmlSchemaVersion = "1.0";

    private readonly ILogger<BehaviorService> _logger;
    private readonly BehaviorServiceConfiguration _configuration;
    private readonly IMessageBus _messageBus;
    private readonly IGoapPlanner _goapPlanner;
    private readonly BehaviorCompiler _compiler;
    private readonly IAssetClient _assetClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BehaviorBundleManager _bundleManager;

    /// <summary>
    /// Creates a new instance of the BehaviorService.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Service configuration.</param>
    /// <param name="messageBus">Message bus for event publishing.</param>
    /// <param name="eventConsumer">Event consumer for registering handlers.</param>
    /// <param name="goapPlanner">GOAP planner for generating action plans.</param>
    /// <param name="compiler">ABML behavior compiler.</param>
    /// <param name="assetClient">Asset service client for storing compiled models.</param>
    /// <param name="httpClientFactory">HTTP client factory for asset uploads.</param>
    /// <param name="bundleManager">Bundle manager for efficient behavior grouping.</param>
    public BehaviorService(
        ILogger<BehaviorService> logger,
        BehaviorServiceConfiguration configuration,
        IMessageBus messageBus,
        IEventConsumer eventConsumer,
        IGoapPlanner goapPlanner,
        BehaviorCompiler compiler,
        IAssetClient assetClient,
        IHttpClientFactory httpClientFactory,
        BehaviorBundleManager bundleManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _goapPlanner = goapPlanner ?? throw new ArgumentNullException(nameof(goapPlanner));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _assetClient = assetClient ?? throw new ArgumentNullException(nameof(assetClient));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _bundleManager = bundleManager ?? throw new ArgumentNullException(nameof(bundleManager));

        // Register event handlers via partial class (BehaviorServiceEvents.cs)
        ArgumentNullException.ThrowIfNull(eventConsumer, nameof(eventConsumer));
        ((IBannouService)this).RegisterEventConsumers(eventConsumer);
    }

    /// <summary>
    /// Compiles ABML behavior definition to executable bytecode and stores as an asset.
    /// Publishes BehaviorCreatedEvent or BehaviorUpdatedEvent based on whether this is a new or existing behavior.
    /// </summary>
    /// <param name="body">The compile request containing ABML YAML content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Compilation result with behavior ID for cache retrieval.</returns>
    public async Task<(StatusCodes, CompileBehaviorResponse?)> CompileAbmlBehaviorAsync(CompileBehaviorRequest body, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrWhiteSpace(body.Abml_content))
            {
                return (StatusCodes.BadRequest, new CompileBehaviorResponse
                {
                    Success = false,
                    Behavior_id = string.Empty,
                    Warnings = new List<string> { "ABML content is required" }
                });
            }

            _logger.LogDebug("Compiling ABML behavior ({ContentLength} bytes)", body.Abml_content.Length);

            // Map API compilation options to internal options
            var options = MapCompilationOptions(body.Compilation_options);

            // Compile the ABML YAML to bytecode
            var result = _compiler.CompileYaml(body.Abml_content, options);

            if (!result.Success || result.Bytecode == null)
            {
                _logger.LogWarning("ABML compilation failed with {ErrorCount} errors", result.Errors.Count);
                return (StatusCodes.BadRequest, new CompileBehaviorResponse
                {
                    Success = false,
                    Behavior_id = string.Empty,
                    Compilation_time_ms = (int)stopwatch.ElapsedMilliseconds,
                    Warnings = result.Errors.Select(e => e.Message).ToList()
                });
            }

            // Generate a deterministic behavior ID from content hash
            var bytecode = result.Bytecode;
            var behaviorId = GenerateBehaviorId(bytecode);

            // Determine behavior name (from request, or generate from ID)
            var behaviorName = !string.IsNullOrWhiteSpace(body.Behavior_name)
                ? body.Behavior_name
                : behaviorId;

            // Map category to string
            var category = body.Behavior_category != default
                ? body.Behavior_category.ToString().ToLowerInvariant()
                : null;

            // Store the compiled model as an asset if caching is enabled
            string? assetId = null;
            bool isUpdate = false;
            if (body.Compilation_options?.Cache_compiled_result != false)
            {
                assetId = await StoreCompiledModelAsync(behaviorId, bytecode, cancellationToken);

                if (assetId != null)
                {
                    // Record in bundle manager and determine if this is new or update
                    var metadata = new BehaviorMetadata
                    {
                        Name = behaviorName,
                        Category = category,
                        BundleId = body.Bundle_id,
                        BytecodeSize = bytecode.Length,
                        SchemaVersion = AbmlSchemaVersion
                    };

                    isUpdate = !await _bundleManager.RecordBehaviorAsync(
                        behaviorId,
                        assetId,
                        body.Bundle_id,
                        metadata,
                        cancellationToken);

                    // Publish lifecycle event
                    await PublishBehaviorEventAsync(
                        behaviorId,
                        behaviorName,
                        category,
                        body.Bundle_id,
                        assetId,
                        bytecode.Length,
                        isUpdate,
                        cancellationToken);
                }
            }

            stopwatch.Stop();
            _logger.LogInformation(
                "ABML compilation successful: {BehaviorId} ({BytecodeSize} bytes, {ElapsedMs}ms, isUpdate={IsUpdate})",
                behaviorId,
                bytecode.Length,
                stopwatch.ElapsedMilliseconds,
                isUpdate);

            return (StatusCodes.OK, new CompileBehaviorResponse
            {
                Success = true,
                Behavior_id = behaviorId,
                Behavior_name = behaviorName,
                Compiled_behavior = new CompiledBehavior
                {
                    Behavior_tree = new { bytecode_size = bytecode.Length },
                    Context_schema = new { }
                },
                Compilation_time_ms = (int)stopwatch.ElapsedMilliseconds,
                Asset_id = assetId,
                Bundle_id = body.Bundle_id,
                Is_update = isUpdate,
                Warnings = new List<string>()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error compiling ABML behavior");
            await _messageBus.TryPublishErrorAsync(
                serviceId: "behavior",
                operation: "CompileAbmlBehavior",
                errorType: ex.GetType().Name,
                message: ex.Message,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Publishes a behavior lifecycle event (created or updated).
    /// </summary>
    private async Task PublishBehaviorEventAsync(
        string behaviorId,
        string name,
        string? category,
        string? bundleId,
        string assetId,
        int bytecodeSize,
        bool isUpdate,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        if (isUpdate)
        {
            var updateEvent = new Events.BehaviorUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                BehaviorId = behaviorId,
                Name = name,
                Category = category ?? string.Empty,
                BundleId = bundleId ?? string.Empty,
                AssetId = assetId,
                BytecodeSize = bytecodeSize,
                SchemaVersion = AbmlSchemaVersion,
                CreatedAt = now, // Will be overwritten with actual value if available
                UpdatedAt = now,
                ChangedFields = new List<string> { "bytecode" }
            };
            await _messageBus.PublishAsync("behavior.updated", updateEvent);
            _logger.LogDebug("Published behavior.updated event for {BehaviorId}", behaviorId);
        }
        else
        {
            var createEvent = new Events.BehaviorCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                BehaviorId = behaviorId,
                Name = name,
                Category = category ?? string.Empty,
                BundleId = bundleId ?? string.Empty,
                AssetId = assetId,
                BytecodeSize = bytecodeSize,
                SchemaVersion = AbmlSchemaVersion,
                CreatedAt = now,
                UpdatedAt = now
            };
            await _messageBus.PublishAsync("behavior.created", createEvent);
            _logger.LogDebug("Published behavior.created event for {BehaviorId}", behaviorId);
        }
    }

    /// <summary>
    /// Maps API compilation options to internal compiler options.
    /// </summary>
    private static InternalCompilationOptions MapCompilationOptions(CompilationOptions? apiOptions)
    {
        if (apiOptions == null)
        {
            return InternalCompilationOptions.Default;
        }

        return new InternalCompilationOptions
        {
            EnableOptimizations = apiOptions.Enable_optimizations,
            IncludeDebugInfo = !apiOptions.Enable_optimizations, // Debug info when not optimizing
            SkipSemanticAnalysis = !apiOptions.Strict_validation
        };
    }

    /// <summary>
    /// Generates a deterministic behavior ID from the compiled bytecode.
    /// </summary>
    private static string GenerateBehaviorId(byte[] bytecode)
    {
        var hash = SHA256.HashData(bytecode);
        return $"behavior-{Convert.ToHexString(hash)[..16].ToLowerInvariant()}";
    }

    /// <summary>
    /// Stores the compiled behavior model as an asset in lib-asset.
    /// </summary>
    /// <returns>The cache key for retrieving the model, or null if storage failed.</returns>
    private async Task<string?> StoreCompiledModelAsync(
        string behaviorId,
        byte[] bytecode,
        CancellationToken cancellationToken)
    {
        try
        {
            // Request an upload URL from the asset service
            var uploadRequest = new UploadRequest
            {
                Filename = $"{behaviorId}.bbm",
                Size = bytecode.Length,
                Content_type = BehaviorModelContentType,
                Metadata = new AssetMetadataInput
                {
                    Tags = new List<string> { "behavior", "abml", "compiled" },
                    Realm = Asset.Realm.Shared
                }
            };

            var uploadResponse = await _assetClient.RequestUploadAsync(uploadRequest, cancellationToken);

            // Upload the bytecode to the presigned URL
            using var httpClient = _httpClientFactory.CreateClient();
            using var content = new ByteArrayContent(bytecode);
            content.Headers.ContentType = new MediaTypeHeaderValue(BehaviorModelContentType);

            var putResponse = await httpClient.PutAsync(uploadResponse.Upload_url, content, cancellationToken);
            if (!putResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to upload compiled behavior {BehaviorId}: {StatusCode}",
                    behaviorId,
                    putResponse.StatusCode);
                return null;
            }

            // Complete the upload
            var completeRequest = new CompleteUploadRequest
            {
                Upload_id = uploadResponse.Upload_id
            };

            var assetMetadata = await _assetClient.CompleteUploadAsync(completeRequest, cancellationToken);

            _logger.LogDebug(
                "Stored compiled behavior {BehaviorId} as asset {AssetId}",
                behaviorId,
                assetMetadata.Asset_id);

            return assetMetadata.Asset_id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to store compiled behavior {BehaviorId} as asset", behaviorId);
            return null;
        }
    }

    /// <summary>
    /// Compiles a stack of behaviors with priority resolution. Not yet implemented - planned for future release.
    /// </summary>
    public async Task<(StatusCodes, CompileBehaviorResponse?)> CompileBehaviorStackAsync(BehaviorStackRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogWarning("Method CompileBehaviorStackAsync called but not implemented");
            return (StatusCodes.NotImplemented, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error compiling behavior stack");
            await _messageBus.TryPublishErrorAsync(
                serviceId: "behavior",
                operation: "CompileBehaviorStack",
                errorType: ex.GetType().Name,
                message: ex.Message,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Validates ABML YAML syntax and semantics without full compilation.
    /// </summary>
    /// <param name="body">The validation request containing ABML YAML content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result with errors and warnings.</returns>
    public async Task<(StatusCodes, ValidateAbmlResponse?)> ValidateAbmlAsync(ValidateAbmlRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(body.Abml_content))
            {
                return (StatusCodes.BadRequest, new ValidateAbmlResponse
                {
                    Is_valid = false,
                    Validation_errors = new List<ValidationError>
                    {
                        new ValidationError
                        {
                            Type = ValidationErrorType.Schema,
                            Message = "ABML content is required"
                        }
                    },
                    Schema_version = "1.0"
                });
            }

            _logger.LogDebug("Validating ABML ({ContentLength} bytes)", body.Abml_content.Length);

            // Configure options for validation only (skip bytecode generation)
            var options = new InternalCompilationOptions
            {
                SkipSemanticAnalysis = !body.Strict_mode
            };

            // Use the compiler's validation path
            var result = _compiler.CompileYaml(body.Abml_content, options);

            // Convert compilation errors to validation errors
            var validationErrors = result.Errors
                .Select(e => new ValidationError
                {
                    Type = ValidationErrorType.Semantic,
                    Message = e.Message,
                    Line_number = e.Line ?? 0
                })
                .ToList();

            return (StatusCodes.OK, new ValidateAbmlResponse
            {
                Is_valid = result.Success,
                Validation_errors = validationErrors,
                Semantic_warnings = new List<string>(),
                Schema_version = "1.0"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating ABML");
            await _messageBus.TryPublishErrorAsync(
                serviceId: "behavior",
                operation: "ValidateAbml",
                errorType: ex.GetType().Name,
                message: ex.Message,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Retrieves a cached compiled behavior from the asset store.
    /// </summary>
    /// <param name="body">Request containing the behavior ID (asset ID from compilation).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached behavior with download URL for the compiled bytecode.</returns>
    public async Task<(StatusCodes, CachedBehaviorResponse?)> GetCachedBehaviorAsync(GetCachedBehaviorRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(body.BehaviorId))
            {
                return (StatusCodes.BadRequest, null);
            }

            _logger.LogDebug("Retrieving cached behavior: {BehaviorId}", body.BehaviorId);

            // Retrieve the asset from lib-asset
            var assetRequest = new GetAssetRequest
            {
                Asset_id = body.BehaviorId,
                Version = "latest"
            };

            AssetWithDownloadUrl asset;
            try
            {
                asset = await _assetClient.GetAssetAsync(assetRequest, cancellationToken);
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                _logger.LogDebug("Behavior {BehaviorId} not found in cache", body.BehaviorId);
                return (StatusCodes.NotFound, null);
            }

            // Download the bytecode to include in response
            byte[]? bytecode = null;
            try
            {
                using var httpClient = _httpClientFactory.CreateClient();
                bytecode = await httpClient.GetByteArrayAsync(asset.Download_url, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download behavior bytecode for {BehaviorId}", body.BehaviorId);
                // Continue without bytecode - caller can use download_url directly
            }

            return (StatusCodes.OK, new CachedBehaviorResponse
            {
                Behavior_id = body.BehaviorId,
                Compiled_behavior = new CompiledBehavior
                {
                    Behavior_tree = bytecode != null
                        ? new { bytecode = Convert.ToBase64String(bytecode), bytecode_size = bytecode.Length }
                        : new { download_url = asset.Download_url.ToString() },
                    Context_schema = new { }
                },
                Cache_timestamp = DateTimeOffset.UtcNow, // Asset service doesn't provide timestamp
                Cache_hit = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving cached behavior: {BehaviorId}", body.BehaviorId);
            await _messageBus.TryPublishErrorAsync(
                serviceId: "behavior",
                operation: "GetCachedBehavior",
                errorType: ex.GetType().Name,
                message: ex.Message,
                details: new { BehaviorId = body.BehaviorId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Resolves context variables and cultural adaptations. Not yet implemented - planned for future release.
    /// </summary>
    public async Task<(StatusCodes, ResolveContextResponse?)> ResolveContextVariablesAsync(ResolveContextRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogWarning("Method ResolveContextVariablesAsync called but not implemented");
            return (StatusCodes.NotImplemented, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving context variables");
            await _messageBus.TryPublishErrorAsync(
                serviceId: "behavior",
                operation: "ResolveContextVariables",
                errorType: ex.GetType().Name,
                message: ex.Message,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Invalidates a cached compiled behavior.
    /// </summary>
    /// <remarks>
    /// Currently not fully implemented - the asset service does not support asset deletion.
    /// This endpoint will verify the asset exists but cannot remove it from storage.
    /// Future implementation will support soft-delete or versioning to mark assets as invalid.
    /// </remarks>
    /// <param name="body">Request containing the behavior ID to invalidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK if behavior was found (marking for invalidation), NotFound if not in cache.</returns>
    public async Task<(StatusCodes, object?)> InvalidateCachedBehaviorAsync(InvalidateCacheRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(body.BehaviorId))
            {
                return (StatusCodes.BadRequest, new { error = "BehaviorId is required" });
            }

            _logger.LogDebug("Invalidating cached behavior: {BehaviorId}", body.BehaviorId);

            // Try to get metadata from bundle manager first
            var metadata = await _bundleManager.GetMetadataAsync(body.BehaviorId, cancellationToken);
            if (metadata == null)
            {
                // Fall back to checking asset service directly
                var assetRequest = new GetAssetRequest
                {
                    Asset_id = body.BehaviorId,
                    Version = "latest"
                };

                try
                {
                    await _assetClient.GetAssetAsync(assetRequest, cancellationToken);
                }
                catch (ApiException ex) when (ex.StatusCode == 404)
                {
                    _logger.LogDebug("Behavior {BehaviorId} not found in cache", body.BehaviorId);
                    return (StatusCodes.NotFound, new { error = "Behavior not found in cache" });
                }
            }

            // Remove from bundle manager (this also removes bundle membership)
            var removedMetadata = await _bundleManager.RemoveBehaviorAsync(body.BehaviorId, cancellationToken);

            // Publish deleted event
            if (removedMetadata != null || metadata != null)
            {
                var effectiveMetadata = removedMetadata ?? metadata;
                if (effectiveMetadata != null)
                {
                    await PublishBehaviorDeletedEventAsync(effectiveMetadata, cancellationToken);
                }
            }

            // Note: Asset service doesn't currently support deletion
            _logger.LogInformation(
                "Behavior {BehaviorId} invalidated (deleted event published)",
                body.BehaviorId);

            return (StatusCodes.OK, new { message = "Behavior invalidated", behavior_id = body.BehaviorId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating cached behavior: {BehaviorId}", body.BehaviorId);
            await _messageBus.TryPublishErrorAsync(
                serviceId: "behavior",
                operation: "InvalidateCachedBehavior",
                errorType: ex.GetType().Name,
                message: ex.Message,
                details: new { BehaviorId = body.BehaviorId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Publishes a behavior deleted event.
    /// </summary>
    private async Task PublishBehaviorDeletedEventAsync(
        BehaviorMetadata metadata,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var deleteEvent = new Events.BehaviorDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            BehaviorId = metadata.BehaviorId,
            Name = metadata.Name,
            Category = metadata.Category ?? string.Empty,
            BundleId = metadata.BundleId ?? string.Empty,
            AssetId = metadata.AssetId,
            BytecodeSize = metadata.BytecodeSize,
            SchemaVersion = metadata.SchemaVersion,
            CreatedAt = metadata.CreatedAt,
            UpdatedAt = metadata.UpdatedAt,
            DeletedReason = "Invalidated via API"
        };

        await _messageBus.PublishAsync("behavior.deleted", deleteEvent);
        _logger.LogDebug("Published behavior.deleted event for {BehaviorId}", metadata.BehaviorId);
    }

    /// <summary>
    /// Generates a GOAP plan to achieve the specified goal from the current world state.
    /// </summary>
    /// <param name="body">The plan request containing goal, world state, and options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The planning result with actions or failure reason.</returns>
    public async Task<(StatusCodes, GoapPlanResponse?)> GenerateGoapPlanAsync(GoapPlanRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "Generating GOAP plan for agent {AgentId}, goal {GoalName}",
                body.Agent_id,
                body.Goal.Name);

            // Convert API world state to internal WorldState
            var worldState = ConvertToWorldState(body.World_state);

            // Convert API goal to internal GoapGoal
            var goal = ConvertToGoapGoal(body.Goal);

            // For now, we don't have a behavior cache so we return an error for behavior_id
            // In the future, this will look up cached GOAP actions from compiled behaviors
            if (string.IsNullOrEmpty(body.Behavior_id))
            {
                return (StatusCodes.BadRequest, new GoapPlanResponse
                {
                    Success = false,
                    Failure_reason = "behavior_id is required to retrieve GOAP actions"
                });
            }

            // TODO: Look up cached behavior and extract GOAP actions
            // For now, return NotImplemented as behavior caching is not yet ready
            _logger.LogWarning(
                "GOAP planning for behavior {BehaviorId} not yet implemented - behavior caching required",
                body.Behavior_id);

            return (StatusCodes.NotImplemented, new GoapPlanResponse
            {
                Success = false,
                Failure_reason = "Behavior caching not yet implemented - GOAP actions cannot be retrieved"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating GOAP plan for agent {AgentId}", body.Agent_id);
            await _messageBus.TryPublishErrorAsync(
                serviceId: "behavior",
                operation: "GenerateGoapPlan",
                errorType: ex.GetType().Name,
                message: ex.Message,
                details: new { AgentId = body.Agent_id, GoalName = body.Goal.Name },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Validates an existing GOAP plan against the current world state.
    /// </summary>
    /// <param name="body">The validation request containing the plan and current state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validation result with suggested action.</returns>
    public async Task<(StatusCodes, ValidateGoapPlanResponse?)> ValidateGoapPlanAsync(ValidateGoapPlanRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "Validating GOAP plan for goal {GoalId}, current action index {ActionIndex}",
                body.Plan.Goal_id,
                body.Current_action_index);

            // Convert API world state to internal WorldState
            var worldState = ConvertToWorldState(body.World_state);

            // Convert API plan to internal format for validation
            var plan = ConvertToGoapPlan(body.Plan);

            // Convert active goals if provided
            var activeGoals = body.Active_goals?.Select(ConvertToGoapGoal).ToList()
                ?? new List<InternalGoapGoal>();

            // Validate the plan
            var result = await _goapPlanner.ValidatePlanAsync(
                plan,
                body.Current_action_index,
                worldState,
                activeGoals,
                cancellationToken);

            // Convert internal result to API response
            var response = new ValidateGoapPlanResponse
            {
                Is_valid = result.IsValid,
                Reason = ConvertToApiReplanReason(result.Reason),
                Suggested_action = ConvertToApiSuggestion(result.Suggestion),
                Invalidated_at_index = result.InvalidatedAtIndex,
                Message = result.Message ?? string.Empty
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating GOAP plan for goal {GoalId}", body.Plan.Goal_id);
            await _messageBus.TryPublishErrorAsync(
                serviceId: "behavior",
                operation: "ValidateGoapPlan",
                errorType: ex.GetType().Name,
                message: ex.Message,
                details: new { GoalId = body.Plan.Goal_id },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #region GOAP Type Conversions

    /// <summary>
    /// Converts an API world state object to an internal WorldState.
    /// </summary>
    private static WorldState ConvertToWorldState(object worldStateObject)
    {
        var worldState = new WorldState();

        if (worldStateObject == null)
        {
            return worldState;
        }

        // Handle JsonElement from API deserialization
        if (worldStateObject is System.Text.Json.JsonElement jsonElement)
        {
            return ConvertJsonElementToWorldState(jsonElement);
        }

        // Handle Dictionary<string, object>
        if (worldStateObject is IDictionary<string, object> dict)
        {
            foreach (var (key, value) in dict)
            {
                worldState = SetWorldStateValue(worldState, key, value);
            }
        }

        return worldState;
    }

    /// <summary>
    /// Converts a JsonElement to WorldState.
    /// </summary>
    private static WorldState ConvertJsonElementToWorldState(System.Text.Json.JsonElement element)
    {
        var worldState = new WorldState();

        if (element.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return worldState;
        }

        foreach (var property in element.EnumerateObject())
        {
            worldState = property.Value.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Number => worldState.SetNumeric(property.Name, property.Value.GetSingle()),
                System.Text.Json.JsonValueKind.True => worldState.SetBoolean(property.Name, true),
                System.Text.Json.JsonValueKind.False => worldState.SetBoolean(property.Name, false),
                System.Text.Json.JsonValueKind.String => worldState.SetString(property.Name, property.Value.GetString() ?? string.Empty),
                _ => worldState
            };
        }

        return worldState;
    }

    /// <summary>
    /// Sets a value in the world state based on its runtime type.
    /// </summary>
    private static WorldState SetWorldStateValue(WorldState state, string key, object? value)
    {
        return value switch
        {
            float f => state.SetNumeric(key, f),
            double d => state.SetNumeric(key, (float)d),
            int i => state.SetNumeric(key, i),
            long l => state.SetNumeric(key, l),
            bool b => state.SetBoolean(key, b),
            string s => state.SetString(key, s),
            _ => state
        };
    }

    /// <summary>
    /// Converts an API GoapGoal to an internal GoapGoal.
    /// </summary>
    private static InternalGoapGoal ConvertToGoapGoal(GoapGoal apiGoal)
    {
        // FromMetadata takes string conditions and parses them internally
        return InternalGoapGoal.FromMetadata(
            apiGoal.Name,
            apiGoal.Priority,
            (IReadOnlyDictionary<string, string>)apiGoal.Conditions);
    }

    /// <summary>
    /// Converts an API GoapPlanResult to an internal GoapPlan.
    /// </summary>
    private static GoapPlan ConvertToGoapPlan(GoapPlanResult apiPlan)
    {
        // Create a placeholder goal for validation purposes
        var goal = new InternalGoapGoal(apiPlan.Goal_id, apiPlan.Goal_id, 50);

        // Convert planned actions
        var actions = new List<PlannedAction>();
        foreach (var apiAction in apiPlan.Actions)
        {
            // Create a minimal GoapAction for validation
            var action = new GoapAction(
                apiAction.Action_id,
                apiAction.Action_id,
                new GoapPreconditions(),
                new GoapActionEffects(),
                apiAction.Cost);
            actions.Add(new PlannedAction(action, apiAction.Index));
        }

        return new GoapPlan(
            goal: goal,
            actions: actions,
            totalCost: apiPlan.Total_cost,
            nodesExpanded: 0,
            planningTimeMs: 0,
            initialState: new WorldState(),
            expectedFinalState: new WorldState());
    }

    /// <summary>
    /// Converts an internal ReplanReason to the API enum.
    /// </summary>
    private static ValidateGoapPlanResponseReason ConvertToApiReplanReason(ReplanReason reason)
    {
        return reason switch
        {
            ReplanReason.None => ValidateGoapPlanResponseReason.None,
            ReplanReason.PreconditionInvalidated => ValidateGoapPlanResponseReason.Precondition_invalidated,
            ReplanReason.ActionFailed => ValidateGoapPlanResponseReason.Action_failed,
            ReplanReason.BetterGoalAvailable => ValidateGoapPlanResponseReason.Better_goal_available,
            ReplanReason.PlanCompleted => ValidateGoapPlanResponseReason.Plan_completed,
            ReplanReason.GoalAlreadySatisfied => ValidateGoapPlanResponseReason.Goal_already_satisfied,
            _ => ValidateGoapPlanResponseReason.None
        };
    }

    /// <summary>
    /// Converts an internal ValidationSuggestion to the API enum.
    /// </summary>
    private static ValidateGoapPlanResponseSuggested_action ConvertToApiSuggestion(ValidationSuggestion suggestion)
    {
        return suggestion switch
        {
            ValidationSuggestion.Continue => ValidateGoapPlanResponseSuggested_action.Continue,
            ValidationSuggestion.Replan => ValidateGoapPlanResponseSuggested_action.Replan,
            ValidationSuggestion.Abort => ValidateGoapPlanResponseSuggested_action.Abort,
            _ => ValidateGoapPlanResponseSuggested_action.Continue
        };
    }

    #endregion
}
