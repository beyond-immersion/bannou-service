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

// =============================================================================
// BehaviorService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by BehaviorService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (BehaviorService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IBehaviorService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (BehaviorService.Helpers.cs):
//     Contains all private/internal helper methods, core logic extracted
//     from endpoints, event publishing helpers, query builders, mapping
//     functions, and any other non-public methods. Every async method in
//     this file MUST call ITelemetryProvider.StartActivity to ensure
//     sub-operations are properly instrumented.
//
// Structural tests enforce both rules:
//   - Services_PrimaryFile_DoesNotCallStartActivity
//   - Services_HelperFiles_HaveStartActivityWhenAsync
//
// WHAT GOES HERE:
//   - Private async helper methods (with StartActivity spans)
//   - Private sync helper methods (query builders, mappers, validators)
//   - Internal static key builders (already in primary file by convention,
//     but may be moved here if the primary file is large)
//   - Event publishing helper methods
//   - Any extracted "core" logic (e.g., CreateAccountCoreAsync)
//
// WHAT STAYS IN THE PRIMARY FILE:
//   - Public interface method implementations (/// <inheritdoc/> methods)
//   - Constructor and field declarations
//   - Constants and key prefix definitions
//
// See: docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md (T30)
// See: docs/reference/HELPERS-AND-COMMON-PATTERNS.md
// =============================================================================

/// <summary>
/// Private and internal helper methods for BehaviorService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class BehaviorService
{
    // Move private/internal helper methods here from BehaviorService.cs
    /// <summary>
    /// Publishes a behavior lifecycle event (created or updated).
    /// </summary>
    private async Task PublishBehaviorEventAsync(
        string behaviorId,
        string name,
        BehaviorCategory? category,
        string? bundleId,
        string assetId,
        int bytecodeSize,
        bool isUpdate,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.behavior", "BehaviorService.PublishBehaviorEventAsync");
        var now = DateTimeOffset.UtcNow;

        if (isUpdate)
        {
            var updateEvent = new Events.BehaviorUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                BehaviorId = behaviorId,
                Name = name,
                // Lifecycle event Category is string; enum serialized at event boundary
                Category = category?.ToString() ?? "uncategorized",
                // BundleId is nullable - behaviors don't require bundling
                BundleId = bundleId,
                AssetId = assetId,
                BytecodeSize = bytecodeSize,
                SchemaVersion = AbmlSchemaVersion,
                CreatedAt = now, // Will be overwritten with actual value if available
                UpdatedAt = now,
                ChangedFields = new List<string> { "bytecode" }
            };
            await _messageBus.PublishBehaviorUpdatedAsync(updateEvent);
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
                // Lifecycle event Category is string; enum serialized at event boundary
                Category = category?.ToString() ?? "uncategorized",
                // BundleId is nullable - behaviors don't require bundling
                BundleId = bundleId,
                AssetId = assetId,
                BytecodeSize = bytecodeSize,
                SchemaVersion = AbmlSchemaVersion,
                CreatedAt = now,
                UpdatedAt = now
            };
            await _messageBus.PublishBehaviorCreatedAsync(createEvent);
            _logger.LogDebug("Published behavior.created event for {BehaviorId}", behaviorId);
        }
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
        using var activity = _telemetryProvider.StartActivity("bannou.behavior", "BehaviorService.ExtractAndCacheGoapMetadataAsync");
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
    /// Stores the compiled behavior model as an asset in lib-asset.
    /// </summary>
    /// <returns>The cache key for retrieving the model, or null if storage failed.</returns>
    private async Task<string?> StoreCompiledModelAsync(
        string behaviorId,
        byte[] bytecode,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.behavior", "BehaviorService.StoreCompiledModelAsync");
        try
        {
            // L3 soft dependency — Asset service may not be enabled
            var assetClient = _serviceProvider.GetService<IAssetClient>();
            if (assetClient == null)
            {
                _logger.LogDebug("Asset service not enabled, skipping behavior storage");
                return null;
            }

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

            var uploadResponse = await assetClient.RequestUploadAsync(uploadRequest, cancellationToken);

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

            var assetMetadata = await assetClient.CompleteUploadAsync(completeRequest, cancellationToken);

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
    /// <summary>
    /// Publishes a behavior deleted event.
    /// </summary>
    private async Task PublishBehaviorDeletedEventAsync(
        BehaviorMetadata metadata,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.behavior", "BehaviorService.PublishBehaviorDeletedEventAsync");
        var now = DateTimeOffset.UtcNow;

        var deleteEvent = new Events.BehaviorDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            BehaviorId = metadata.BehaviorId,
            Name = metadata.Name,
            // Lifecycle event Category is string; enum serialized at event boundary
            Category = metadata.Category?.ToString() ?? "uncategorized",
            // BundleId is nullable - behaviors don't require bundling
            BundleId = metadata.BundleId,
            AssetId = metadata.AssetId,
            BytecodeSize = metadata.BytecodeSize,
            SchemaVersion = metadata.SchemaVersion,
            CreatedAt = metadata.CreatedAt,
            UpdatedAt = metadata.UpdatedAt,
            DeletedReason = "Invalidated via API"
        };

        await _messageBus.PublishBehaviorDeletedAsync(deleteEvent);
        _logger.LogDebug("Published behavior.deleted event for {BehaviorId}", metadata.BehaviorId);
    }
}
