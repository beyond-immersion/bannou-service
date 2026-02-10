using BeyondImmersion.Bannou.BehaviorCompiler.Compiler;
using BeyondImmersion.Bannou.BehaviorCompiler.Goap;
using BeyondImmersion.Bannou.BehaviorCompiler.Parser;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Abml.Cognition;
using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Behavior.Goap;
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
using InternalCompilationOptions = BeyondImmersion.Bannou.BehaviorCompiler.Compiler.CompilationOptions;
using InternalGoapGoal = BeyondImmersion.Bannou.BehaviorCompiler.Goap.GoapGoal;

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

    // Event topics
    private const string COMPILATION_FAILED_TOPIC = "behavior.compilation-failed";
    private const string GOAP_PLAN_GENERATED_TOPIC = "behavior.goap.plan-generated";

    private readonly ILogger<BehaviorService> _logger;
    private readonly BehaviorServiceConfiguration _configuration;
    private readonly IMessageBus _messageBus;
    private readonly IGoapPlanner _goapPlanner;
    private readonly BehaviorCompiler _compiler;
    private readonly IAssetClient _assetClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBehaviorBundleManager _bundleManager;

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
        IBehaviorBundleManager bundleManager)
    {
        _logger = logger;
        _configuration = configuration;
        _messageBus = messageBus;
        _goapPlanner = goapPlanner;
        _compiler = compiler;
        _assetClient = assetClient;
        _httpClientFactory = httpClientFactory;
        _bundleManager = bundleManager;

        // Initialize cognition constants from configuration (idempotent - first call wins)
        // IMPLEMENTATION TENETS - Configuration-First
        CognitionConstants.Initialize(configuration.ToCognitionConfiguration());

        // Register event handlers via partial class (BehaviorServiceEvents.cs)
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

        {
            if (string.IsNullOrWhiteSpace(body.AbmlContent))
            {
                _logger.LogWarning("ABML compilation rejected: content is empty or whitespace");
                return (StatusCodes.BadRequest, null);
            }

            _logger.LogDebug("Compiling ABML behavior ({ContentLength} bytes)", body.AbmlContent.Length);

            // Map API compilation options to internal options
            var options = MapCompilationOptions(body.CompilationOptions);

            // Compile the ABML YAML to bytecode
            var result = _compiler.CompileYaml(body.AbmlContent, options);

            if (!result.Success || result.Bytecode == null)
            {
                _logger.LogWarning("ABML compilation failed with {ErrorCount} errors: {Errors}",
                    result.Errors.Count,
                    string.Join("; ", result.Errors.Select(e => e.Message)));

                // Publish compilation failed event for monitoring (contains error details for debugging)
                await _messageBus.TryPublishAsync(COMPILATION_FAILED_TOPIC, new BehaviorCompilationFailedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    BehaviorName = body.BehaviorName,
                    ErrorCount = result.Errors.Count,
                    Errors = result.Errors.Select(e => e.Message).ToList()
                }, cancellationToken: cancellationToken);

                return (StatusCodes.BadRequest, null);
            }

            // Generate a deterministic behavior ID from content hash
            var bytecode = result.Bytecode;
            var behaviorId = GenerateBehaviorId(bytecode);

            // Determine behavior name (from request, or generate from ID)
            var behaviorName = !string.IsNullOrWhiteSpace(body.BehaviorName)
                ? body.BehaviorName
                : behaviorId;

            // Map category to string (BehaviorCategory is nullable - use null-conditional)
            var category = body.BehaviorCategory?.ToString().ToLowerInvariant();

            // Store the compiled model as an asset if caching is enabled
            string? assetId = null;
            bool isUpdate = false;
            var cachingEnabled = body.CompilationOptions?.CacheCompiledResult != false;
            if (cachingEnabled)
            {
                assetId = await StoreCompiledModelAsync(behaviorId, bytecode, cancellationToken);

                // If caching was enabled but storage failed, the behavior is inaccessible - this is an error
                if (assetId == null)
                {
                    _logger.LogError("Failed to store compiled behavior {BehaviorId} - storage returned null", behaviorId);
                    await _messageBus.TryPublishErrorAsync(
                        serviceName: "behavior",
                        operation: "CompileAbmlBehavior",
                        errorType: "StorageFailure",
                        message: "Failed to store compiled behavior in asset service",
                        details: new { behaviorId },
                        cancellationToken: cancellationToken);
                    return (StatusCodes.InternalServerError, null);
                }

                // Record in bundle manager and determine if this is new or update
                var metadata = new BehaviorMetadata
                {
                    Name = behaviorName,
                    Category = category,
                    BundleId = body.BundleId,
                    BytecodeSize = bytecode.Length,
                    SchemaVersion = AbmlSchemaVersion
                };

                isUpdate = !await _bundleManager.RecordBehaviorAsync(
                    behaviorId,
                    assetId,
                    body.BundleId,
                    metadata,
                    cancellationToken);

                // Extract and cache GOAP metadata if present
                await ExtractAndCacheGoapMetadataAsync(behaviorId, body.AbmlContent, cancellationToken);

                // Publish lifecycle event
                await PublishBehaviorEventAsync(
                    behaviorId,
                    behaviorName,
                    category,
                    body.BundleId,
                    assetId,
                    bytecode.Length,
                    isUpdate,
                    cancellationToken);
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
                BehaviorId = behaviorId,
                BehaviorName = behaviorName,
                CompiledBehavior = new CompiledBehavior
                {
                    BehaviorTree = new BehaviorTreeData { BytecodeSize = bytecode.Length },
                    ContextSchema = new ContextSchemaData()
                },
                CompilationTimeMs = (int)stopwatch.ElapsedMilliseconds,
                // assetId is null only when caching is explicitly disabled; otherwise storage failure returns error above
                AssetId = assetId,
                BundleId = body.BundleId,
                IsUpdate = isUpdate,
                Warnings = new List<string>()
            });
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
                // Category is required - use "uncategorized" as fallback when not specified
                Category = category ?? "uncategorized",
                // BundleId is nullable - behaviors don't require bundling
                BundleId = bundleId,
                AssetId = assetId,
                BytecodeSize = bytecodeSize,
                SchemaVersion = AbmlSchemaVersion,
                CreatedAt = now, // Will be overwritten with actual value if available
                UpdatedAt = now,
                ChangedFields = new List<string> { "bytecode" }
            };
            await _messageBus.TryPublishAsync("behavior.updated", updateEvent);
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
                // Category is required - use "uncategorized" as fallback when not specified
                Category = category ?? "uncategorized",
                // BundleId is nullable - behaviors don't require bundling
                BundleId = bundleId,
                AssetId = assetId,
                BytecodeSize = bytecodeSize,
                SchemaVersion = AbmlSchemaVersion,
                CreatedAt = now,
                UpdatedAt = now
            };
            await _messageBus.TryPublishAsync("behavior.created", createEvent);
            _logger.LogDebug("Published behavior.created event for {BehaviorId}", behaviorId);
        }
    }

    /// <summary>
    /// Maps API compilation options to internal compiler options.
    /// </summary>
    private InternalCompilationOptions MapCompilationOptions(CompilationOptions? apiOptions)
    {
        if (apiOptions == null)
        {
            return new InternalCompilationOptions
            {
                MaxConstants = _configuration.CompilerMaxConstants,
                MaxStrings = _configuration.CompilerMaxStrings
            };
        }

        return new InternalCompilationOptions
        {
            EnableOptimizations = apiOptions.EnableOptimizations,
            IncludeDebugInfo = !apiOptions.EnableOptimizations,
            SkipSemanticAnalysis = !apiOptions.StrictValidation,
            MaxConstants = _configuration.CompilerMaxConstants,
            MaxStrings = _configuration.CompilerMaxStrings
        };
    }

    /// <summary>
    /// Extracts GOAP metadata from ABML content and caches it for later planning.
    /// Does nothing if the ABML has no GOAP content (no goals or goap: blocks).
    /// </summary>
    /// <param name="behaviorId">The behavior's unique identifier.</param>
    /// <param name="abmlContent">Raw ABML YAML content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task ExtractAndCacheGoapMetadataAsync(
        string behaviorId,
        string abmlContent,
        CancellationToken cancellationToken)
    {
        try
        {
            // Parse ABML to get the document with GOAP metadata
            var parser = new DocumentParser();
            var parseResult = parser.Parse(abmlContent);

            if (!parseResult.IsSuccess || parseResult.Value == null)
            {
                _logger.LogDebug(
                    "Could not parse ABML for GOAP extraction for behavior {BehaviorId}",
                    behaviorId);
                return;
            }

            var document = parseResult.Value;

            // Check if document has any GOAP content
            if (!GoapMetadataConverter.HasGoapContent(document))
            {
                _logger.LogDebug(
                    "No GOAP content in behavior {BehaviorId}, skipping GOAP metadata caching",
                    behaviorId);
                return;
            }

            // Build cached metadata
            var cachedMetadata = new CachedGoapMetadata();

            // Extract goals
            foreach (var (goalName, goalDef) in document.Goals)
            {
                cachedMetadata.Goals.Add(new CachedGoapGoal
                {
                    Name = goalName,
                    Priority = goalDef.Priority,
                    Conditions = new Dictionary<string, string>(goalDef.Conditions)
                });
            }

            // Extract GOAP-enabled actions from flows
            foreach (var (flowName, flow) in document.Flows)
            {
                if (flow.Goap != null)
                {
                    cachedMetadata.Actions.Add(new CachedGoapAction
                    {
                        FlowName = flowName,
                        Preconditions = new Dictionary<string, string>(flow.Goap.Preconditions),
                        Effects = new Dictionary<string, string>(flow.Goap.Effects),
                        Cost = flow.Goap.Cost
                    });
                }
            }

            // Save to cache
            await _bundleManager.SaveGoapMetadataAsync(behaviorId, cachedMetadata, cancellationToken);

            _logger.LogInformation(
                "Cached GOAP metadata for behavior {BehaviorId}: {GoalCount} goals, {ActionCount} actions",
                behaviorId,
                cachedMetadata.Goals.Count,
                cachedMetadata.Actions.Count);
        }
        catch (Exception ex)
        {
            // Log but don't fail compilation - GOAP caching is optional
            _logger.LogWarning(
                ex,
                "Failed to extract GOAP metadata for behavior {BehaviorId}, GOAP planning will not be available",
                behaviorId);
        }
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
                ContentType = BehaviorModelContentType,
                Metadata = new AssetMetadataInput
                {
                    Tags = new List<string> { "behavior", "abml", "compiled" },
                    Realm = "shared"
                }
            };

            var uploadResponse = await _assetClient.RequestUploadAsync(uploadRequest, cancellationToken);

            // Upload the bytecode to the presigned URL
            using var httpClient = _httpClientFactory.CreateClient();
            using var content = new ByteArrayContent(bytecode);
            content.Headers.ContentType = new MediaTypeHeaderValue(BehaviorModelContentType);

            var putResponse = await httpClient.PutAsync(uploadResponse.UploadUrl, content, cancellationToken);
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
                UploadId = uploadResponse.UploadId
            };

            var assetMetadata = await _assetClient.CompleteUploadAsync(completeRequest, cancellationToken);

            _logger.LogDebug(
                "Stored compiled behavior {BehaviorId} as asset {AssetId}",
                behaviorId,
                assetMetadata.AssetId);

            return assetMetadata.AssetId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to store compiled behavior {BehaviorId} as asset", behaviorId);
            return null;
        }
    }
    // NOTE: CompileBehaviorStackAsync removed - see docs/guides/ABML.md for rationale
    // Runtime BehaviorStack (lib-behavior/Stack/) provides superior dynamic layer evaluation.

    /// <summary>
    /// Validates ABML YAML syntax and semantics without full compilation.
    /// </summary>
    /// <param name="body">The validation request containing ABML YAML content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result with errors and warnings.</returns>
    public async Task<(StatusCodes, ValidateAbmlResponse?)> ValidateAbmlAsync(ValidateAbmlRequest body, CancellationToken cancellationToken = default)
    {
        {
            if (string.IsNullOrWhiteSpace(body.AbmlContent))
            {
                _logger.LogWarning("ABML validation rejected: content is empty or whitespace");
                return (StatusCodes.BadRequest, null);
            }

            _logger.LogDebug("Validating ABML ({ContentLength} bytes)", body.AbmlContent.Length);

            var options = new InternalCompilationOptions
            {
                SkipSemanticAnalysis = !body.StrictMode,
                MaxConstants = _configuration.CompilerMaxConstants,
                MaxStrings = _configuration.CompilerMaxStrings
            };

            // Use the compiler's validation path
            var result = _compiler.CompileYaml(body.AbmlContent, options);

            // Convert compilation errors to validation errors
            var validationErrors = result.Errors
                .Select(e => new ValidationError
                {
                    Type = ValidationErrorType.Semantic,
                    Message = e.Message,
                    LineNumber = e.Line ?? 0
                })
                .ToList();

            return (StatusCodes.OK, new ValidateAbmlResponse
            {
                IsValid = result.Success,
                ValidationErrors = validationErrors,
                SemanticWarnings = result.Warnings.Select(w => w.Message).ToList(),
                SchemaVersion = "1.0"
            });
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
        {
            if (string.IsNullOrWhiteSpace(body.BehaviorId))
            {
                return (StatusCodes.BadRequest, null);
            }

            _logger.LogDebug("Retrieving cached behavior: {BehaviorId}", body.BehaviorId);

            // Retrieve the asset from lib-asset
            var assetRequest = new GetAssetRequest
            {
                AssetId = body.BehaviorId,
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

            // Validate that we have a download URL - it's required for accessing the behavior
            if (asset.DownloadUrl == null)
            {
                _logger.LogError("Asset {AssetId} for behavior {BehaviorId} has no download URL", body.BehaviorId, body.BehaviorId);
                await _messageBus.TryPublishErrorAsync(
                    serviceName: "behavior",
                    operation: "GetCachedBehavior",
                    errorType: "MissingDownloadUrl",
                    message: "Asset has no download URL - behavior is inaccessible",
                    details: new { BehaviorId = body.BehaviorId },
                    cancellationToken: cancellationToken);
                return (StatusCodes.InternalServerError, null);
            }

            // Download the bytecode to include in response
            byte[]? bytecode = null;
            try
            {
                using var httpClient = _httpClientFactory.CreateClient();
                bytecode = await httpClient.GetByteArrayAsync(asset.DownloadUrl, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download behavior bytecode for {BehaviorId}", body.BehaviorId);
                // Continue without bytecode - caller can use download_url directly
            }

            var behaviorTreeData = bytecode != null
                ? new BehaviorTreeData
                {
                    Bytecode = Convert.ToBase64String(bytecode),
                    BytecodeSize = bytecode.Length
                }
                : new BehaviorTreeData
                {
                    // DownloadUrl is validated above - this won't be null
                    DownloadUrl = asset.DownloadUrl.ToString(),
                    BytecodeSize = 0 // Unknown size when using download URL
                };

            return (StatusCodes.OK, new CachedBehaviorResponse
            {
                BehaviorId = body.BehaviorId,
                CompiledBehavior = new CompiledBehavior
                {
                    BehaviorTree = behaviorTreeData,
                    ContextSchema = new ContextSchemaData()
                },
                CacheTimestamp = DateTimeOffset.UtcNow, // Asset service doesn't provide timestamp
                CacheHit = true
            });
        }
    }

    // NOTE: ResolveContextVariablesAsync removed - see docs/guides/ABML.md for rationale
    // Context resolution happens dynamically within BehaviorStack layer evaluation.

    /// <summary>
    /// Invalidates a cached compiled behavior by deleting it from storage.
    /// </summary>
    /// <param name="body">Request containing the behavior ID to invalidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OK if behavior was deleted, NotFound if not in cache.</returns>
    public async Task<StatusCodes> InvalidateCachedBehaviorAsync(InvalidateCacheRequest body, CancellationToken cancellationToken = default)
    {
        {
            if (string.IsNullOrWhiteSpace(body.BehaviorId))
            {
                _logger.LogWarning("InvalidateCachedBehavior called with missing BehaviorId");
                return StatusCodes.BadRequest;
            }

            _logger.LogDebug("Invalidating cached behavior: {BehaviorId}", body.BehaviorId);

            // Try to get metadata from bundle manager first
            var metadata = await _bundleManager.GetMetadataAsync(body.BehaviorId, cancellationToken);
            if (metadata == null)
            {
                // Fall back to checking asset service directly
                var assetRequest = new GetAssetRequest
                {
                    AssetId = body.BehaviorId,
                    Version = "latest"
                };

                try
                {
                    await _assetClient.GetAssetAsync(assetRequest, cancellationToken);
                }
                catch (ApiException ex) when (ex.StatusCode == 404)
                {
                    _logger.LogDebug("Behavior {BehaviorId} not found in cache", body.BehaviorId);
                    return StatusCodes.NotFound;
                }
            }

            // Remove from bundle manager (this also removes bundle membership)
            var removedMetadata = await _bundleManager.RemoveBehaviorAsync(body.BehaviorId, cancellationToken);

            // Also remove cached GOAP metadata if present
            await _bundleManager.RemoveGoapMetadataAsync(body.BehaviorId, cancellationToken);

            // Publish deleted event
            if (removedMetadata != null || metadata != null)
            {
                var effectiveMetadata = removedMetadata ?? metadata;
                if (effectiveMetadata != null)
                {
                    await PublishBehaviorDeletedEventAsync(effectiveMetadata, cancellationToken);
                }
            }

            // Delete the asset from storage
            var deleteRequest = new DeleteAssetRequest
            {
                AssetId = body.BehaviorId
            };

            try
            {
                await _assetClient.DeleteAssetAsync(deleteRequest, cancellationToken);
                _logger.LogInformation(
                    "Behavior {BehaviorId} deleted from asset storage",
                    body.BehaviorId);
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                _logger.LogDebug(
                    "Behavior {BehaviorId} not found in asset storage (may have been deleted already)",
                    body.BehaviorId);
            }

            return StatusCodes.OK;
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
            // Category is required - use "uncategorized" as fallback when not specified
            Category = metadata.Category ?? "uncategorized",
            // BundleId is nullable - behaviors don't require bundling
            BundleId = metadata.BundleId,
            AssetId = metadata.AssetId,
            BytecodeSize = metadata.BytecodeSize,
            SchemaVersion = metadata.SchemaVersion,
            CreatedAt = metadata.CreatedAt,
            UpdatedAt = metadata.UpdatedAt,
            DeletedReason = "Invalidated via API"
        };

        await _messageBus.TryPublishAsync("behavior.deleted", deleteEvent);
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
        {
            _logger.LogDebug(
                "Generating GOAP plan for agent {AgentId}, goal {GoalName}",
                body.AgentId,
                body.Goal.Name);

            // Validate behavior_id is required
            if (string.IsNullOrEmpty(body.BehaviorId))
            {
                _logger.LogWarning("GOAP planning rejected: behavior_id is required");
                return (StatusCodes.BadRequest, null);
            }

            // Retrieve cached GOAP metadata from compiled behavior
            var cachedMetadata = await _bundleManager.GetGoapMetadataAsync(body.BehaviorId, cancellationToken);
            if (cachedMetadata == null)
            {
                _logger.LogDebug("No GOAP metadata found for behavior {BehaviorId}", body.BehaviorId);
                return (StatusCodes.NotFound, null);
            }

            // Convert API world state to internal WorldState
            var worldState = ConvertToWorldState(body.WorldState);

            // Convert API goal to internal GoapGoal
            var goal = ConvertToGoapGoal(body.Goal);

            // Convert cached actions to internal GoapAction objects
            var availableActions = new List<GoapAction>();
            foreach (var cachedAction in cachedMetadata.Actions)
            {
                var action = GoapAction.FromMetadata(
                    cachedAction.FlowName,
                    cachedAction.Preconditions,
                    cachedAction.Effects,
                    cachedAction.Cost);
                availableActions.Add(action);
            }

            if (availableActions.Count == 0)
            {
                return (StatusCodes.OK, new GoapPlanResponse
                {
                    FailureReason = "No GOAP actions available in the compiled behavior",
                    PlanningTimeMs = 0,
                    NodesExpanded = 0
                });
            }

            // Convert planning options (use defaults if not provided)
            var planningOptions = body.Options != null
                ? new PlanningOptions
                {
                    MaxDepth = body.Options.MaxDepth,
                    MaxNodesExpanded = body.Options.MaxNodes,
                    TimeoutMs = body.Options.TimeoutMs
                }
                : PlanningOptions.Default;

            _logger.LogDebug(
                "Planning with {ActionCount} actions, options: {Options}",
                availableActions.Count,
                planningOptions);

            // Run the A* planner
            var plan = await _goapPlanner.PlanAsync(
                worldState,
                goal,
                availableActions,
                planningOptions,
                cancellationToken);

            // Convert result to API response
            if (plan == null)
            {
                return (StatusCodes.OK, new GoapPlanResponse
                {
                    FailureReason = "No valid plan found - goal may be unreachable from current state",
                    PlanningTimeMs = 0,
                    NodesExpanded = 0
                });
            }

            // Build successful response
            var planResult = new GoapPlanResult
            {
                GoalId = goal.Id,
                TotalCost = plan.TotalCost,
                Actions = plan.Actions.Select(a => new PlannedActionResponse
                {
                    ActionId = a.Action.Id,
                    Index = a.Index,
                    Cost = a.Action.Cost
                }).ToList()
            };

            _logger.LogInformation(
                "Generated GOAP plan for agent {AgentId}: {ActionCount} actions, cost {TotalCost}, {NodesExpanded} nodes in {PlanningTimeMs}ms",
                body.AgentId,
                plan.ActionCount,
                plan.TotalCost,
                plan.NodesExpanded,
                plan.PlanningTimeMs);

            // Publish plan generated event for monitoring/analytics
            await _messageBus.TryPublishAsync(GOAP_PLAN_GENERATED_TOPIC, new GoapPlanGeneratedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ActorId = body.AgentId,
                GoalId = body.Goal.Name,
                PlanStepCount = plan.ActionCount,
                PlanningTimeMs = (int)plan.PlanningTimeMs
            }, cancellationToken: cancellationToken);

            return (StatusCodes.OK, new GoapPlanResponse
            {
                Plan = planResult,
                PlanningTimeMs = (int)plan.PlanningTimeMs,
                NodesExpanded = plan.NodesExpanded
            });
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
        {
            _logger.LogDebug(
                "Validating GOAP plan for goal {GoalId}, current action index {ActionIndex}",
                body.Plan.GoalId,
                body.CurrentActionIndex);

            // Convert API world state to internal WorldState
            var worldState = ConvertToWorldState(body.WorldState);

            // Convert API plan to internal format for validation
            var plan = ConvertToGoapPlan(body.Plan);

            // Convert active goals if provided
            var activeGoals = body.ActiveGoals?.Select(ConvertToGoapGoal).ToList()
                ?? new List<InternalGoapGoal>();

            // Validate the plan
            var result = await _goapPlanner.ValidatePlanAsync(
                plan,
                body.CurrentActionIndex,
                worldState,
                activeGoals,
                cancellationToken);

            // Convert internal result to API response
            var response = new ValidateGoapPlanResponse
            {
                IsValid = result.IsValid,
                Reason = ConvertToApiReplanReason(result.Reason),
                SuggestedAction = ConvertToApiSuggestion(result.Suggestion),
                InvalidatedAtIndex = result.InvalidatedAtIndex,
                // Message is nullable - null means no additional context needed
                Message = result.Message
            };

            return (StatusCodes.OK, response);
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
                // GetString() returns string? but cannot return null when ValueKind is String;
                // coalesce satisfies compiler's nullable analysis (will never execute)
                System.Text.Json.JsonValueKind.String => worldState.SetString(property.Name, property.Value.GetString() ?? string.Empty),
                // Default case handles Null, Object, Array, Undefined - these are intentionally ignored
                // as WorldState only supports primitive types (bool, number, string)
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
        var goal = new InternalGoapGoal(apiPlan.GoalId, apiPlan.GoalId, 50);

        // Convert planned actions
        var actions = new List<PlannedAction>();
        foreach (var apiAction in apiPlan.Actions)
        {
            // Create a minimal GoapAction for validation
            var action = new GoapAction(
                apiAction.ActionId,
                apiAction.ActionId,
                new GoapPreconditions(),
                new GoapActionEffects(),
                apiAction.Cost);
            actions.Add(new PlannedAction(action, apiAction.Index));
        }

        return new GoapPlan(
            goal: goal,
            actions: actions,
            totalCost: apiPlan.TotalCost,
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
            ReplanReason.PreconditionInvalidated => ValidateGoapPlanResponseReason.PreconditionInvalidated,
            ReplanReason.ActionFailed => ValidateGoapPlanResponseReason.ActionFailed,
            ReplanReason.BetterGoalAvailable => ValidateGoapPlanResponseReason.BetterGoalAvailable,
            ReplanReason.PlanCompleted => ValidateGoapPlanResponseReason.PlanCompleted,
            ReplanReason.GoalAlreadySatisfied => ValidateGoapPlanResponseReason.GoalAlreadySatisfied,
            _ => ValidateGoapPlanResponseReason.None
        };
    }

    /// <summary>
    /// Converts an internal ValidationSuggestion to the API enum.
    /// </summary>
    private static ValidateGoapPlanResponseSuggestedAction ConvertToApiSuggestion(ValidationSuggestion suggestion)
    {
        return suggestion switch
        {
            ValidationSuggestion.Continue => ValidateGoapPlanResponseSuggestedAction.Continue,
            ValidationSuggestion.Replan => ValidateGoapPlanResponseSuggestedAction.Replan,
            ValidationSuggestion.Abort => ValidateGoapPlanResponseSuggestedAction.Abort,
            _ => ValidateGoapPlanResponseSuggestedAction.Continue
        };
    }

    #endregion

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permission service on startup.
    /// Overrides the default IBannouService implementation to use generated permission data.
    /// </summary>
    public async Task RegisterServicePermissionsAsync(string appId)
    {
        _logger.LogInformation("Registering Behavior service permissions...");
        await BehaviorPermissionRegistration.RegisterViaEventAsync(_messageBus, appId, _logger);
    }

    #endregion
}
