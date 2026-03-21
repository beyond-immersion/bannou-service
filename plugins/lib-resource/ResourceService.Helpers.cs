using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Resource;

// =============================================================================
// ResourceService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by ResourceService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (ResourceService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IResourceService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (ResourceService.Helpers.cs):
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
/// Private and internal helper methods for ResourceService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class ResourceService
{
    // Move private/internal helper methods here from ResourceService.cs
    /// <summary>
    /// Gets all cleanup callbacks for a resource type.
    /// </summary>
    private async Task<List<CleanupCallbackDefinition>> GetCleanupCallbacksAsync(
        string resourceType,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.resource", "ResourceService.GetCleanupCallbacksAsync");

        // Get the callback index for this resource type
        var indexKey = $"{CLEANUP_INDEX_KEY_PREFIX}{resourceType}";
        var sourceTypes = await _cleanupCacheStore.GetSetAsync<string>(indexKey, cancellationToken);

        var callbacks = new List<CleanupCallbackDefinition>();
        foreach (var sourceType in sourceTypes)
        {
            var callback = await _cleanupStore.GetAsync(BuildCleanupKey(resourceType, sourceType), cancellationToken);
            if (callback != null)
            {
                callbacks.Add(callback);
            }
        }

        return callbacks;
    }

    // =========================================================================
    // Compression Management Helpers
    // =========================================================================

    /// <summary>
    /// Key for the master index of all resource types that have compression callbacks.
    /// </summary>
    private const string MasterCompressResourceTypeIndexKey = "compress-callback-resource-types";

    /// <summary>
    /// Builds the Redis key for a compression callback definition.
    /// </summary>
    internal static string BuildCompressKey(string resourceType, string sourceType)
        => $"{COMPRESS_KEY_PREFIX}{resourceType}:{sourceType}";

    /// <summary>
    /// Builds the Redis key for the compression callback index.
    /// </summary>
    internal static string BuildCompressIndexKey(string resourceType)
        => $"{COMPRESS_INDEX_KEY_PREFIX}{resourceType}";

    /// <summary>
    /// Builds the MySQL key for an archive.
    /// </summary>
    internal static string BuildArchiveKey(string resourceType, Guid resourceId)
        => $"{ARCHIVE_KEY_PREFIX}{resourceType}:{resourceId}";

    /// <summary>
    /// Builds the MySQL key for a provisioning transaction record.
    /// </summary>
    internal static string BuildTransactionKey(Guid transactionId)
        => $"{TRANSACTION_KEY_PREFIX}{transactionId}";

    /// <summary>
    /// Builds the MySQL key for a provision record.
    /// </summary>
    internal static string BuildProvisionKey(Guid provisionId)
        => $"{PROVISION_KEY_PREFIX}{provisionId}";

    /// <summary>
    /// Builds the MySQL key for the provision index (transaction → provision list).
    /// </summary>
    internal static string BuildProvisionTxIndexKey(Guid transactionId)
        => $"{PROVISION_TX_INDEX_PREFIX}{transactionId}";

    /// <summary>
    /// Gets all compression callbacks for a resource type, sorted by priority.
    /// </summary>
    private async Task<List<CompressCallbackDefinition>> GetCompressCallbacksAsync(
        string resourceType,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.resource", "ResourceService.GetCompressCallbacksAsync");

        var indexKey = BuildCompressIndexKey(resourceType);
        var sourceTypes = await _compressCacheStore.GetSetAsync<string>(indexKey, cancellationToken);

        var callbacks = new List<CompressCallbackDefinition>();
        foreach (var sourceType in sourceTypes)
        {
            var callback = await _compressStore.GetAsync(BuildCompressKey(resourceType, sourceType), cancellationToken);
            if (callback != null)
            {
                callbacks.Add(callback);
            }
        }

        // Sort by priority (lower = earlier)
        return callbacks.OrderBy(c => c.Priority).ToList();
    }

    /// <summary>
    /// Maintains the cleanup callback index when defining cleanup callbacks.
    /// Must be called after saving the callback definition.
    /// </summary>
    private async Task MaintainCallbackIndexAsync(
        string resourceType,
        string sourceType,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.resource", "ResourceService.MaintainCallbackIndexAsync");

        // Add to per-resource-type index
        var indexKey = $"{CLEANUP_INDEX_KEY_PREFIX}{resourceType}";
        await _cleanupCacheStore.AddToSetAsync(indexKey, sourceType, cancellationToken: cancellationToken);

        // Add to master resource type index (for listing all callbacks)
        await _cleanupCacheStore.AddToSetAsync(MasterResourceTypeIndexKey, resourceType, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Maintains the compression callback index when defining callbacks.
    /// </summary>
    private async Task MaintainCompressCallbackIndexAsync(
        string resourceType,
        string sourceType,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.resource", "ResourceService.MaintainCompressCallbackIndexAsync");

        // Add to per-resource-type index
        var indexKey = BuildCompressIndexKey(resourceType);
        await _compressCacheStore.AddToSetAsync(indexKey, sourceType, cancellationToken: cancellationToken);

        // Add to master resource type index
        await _compressCacheStore.AddToSetAsync(MasterCompressResourceTypeIndexKey, resourceType, cancellationToken: cancellationToken);
    }

    // =========================================================================
    // Migration Management Helpers
    // =========================================================================

    /// <summary>
    /// Builds the Redis key for a migration callback definition.
    /// </summary>
    internal static string BuildMigrateKey(string resourceType, string sourceType)
        => $"{MIGRATE_KEY_PREFIX}{resourceType}:{sourceType}";

    /// <summary>
    /// Gets all migration callbacks for a resource type.
    /// </summary>
    private async Task<List<MigrateCallbackDefinition>> GetMigrateCallbacksAsync(
        string resourceType,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.resource", "ResourceService.GetMigrateCallbacksAsync");

        var indexKey = $"{MIGRATE_INDEX_KEY_PREFIX}{resourceType}";
        var sourceTypes = await _migrateIndexStore.GetSetAsync<string>(indexKey, cancellationToken);

        var callbacks = new List<MigrateCallbackDefinition>();
        foreach (var sourceType in sourceTypes)
        {
            var callback = await _migrateStore.GetAsync(BuildMigrateKey(resourceType, sourceType), cancellationToken);
            if (callback != null)
            {
                callbacks.Add(callback);
            }
        }

        return callbacks;
    }

    /// <summary>
    /// Maintains the migration callback index when defining callbacks.
    /// </summary>
    private async Task MaintainMigrateCallbackIndexAsync(
        string resourceType,
        string sourceType,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.resource", "ResourceService.MaintainMigrateCallbackIndexAsync");

        // Add to per-resource-type index
        var indexKey = $"{MIGRATE_INDEX_KEY_PREFIX}{resourceType}";
        await _migrateIndexStore.AddToSetAsync(indexKey, sourceType, cancellationToken: cancellationToken);

        // Add to master resource type index
        await _migrateIndexStore.AddToSetAsync(MasterMigrateResourceTypeIndexKey, resourceType, cancellationToken: cancellationToken);
    }
    /// <summary>
    /// Finds a provision by resourceId within a transaction.
    /// </summary>
    private async Task<ResourceProvisionModel?> FindProvisionByResourceIdAsync(
        Guid transactionId, Guid resourceId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.resource", "ResourceService.FindProvisionByResourceId");

        var indexJson = await _provisionStringStore.GetAsync(BuildProvisionTxIndexKey(transactionId), ct);
        if (string.IsNullOrEmpty(indexJson))
            return null;

        var provisionIds = BannouJson.Deserialize<List<string>>(indexJson);
        if (provisionIds == null)
            return null;

        foreach (var idStr in provisionIds)
        {
            if (!Guid.TryParse(idStr, out var provisionId))
                continue;
            var provision = await _provisionStore.GetAsync(BuildProvisionKey(provisionId), ct);
            if (provision != null && provision.ResourceId == resourceId)
                return provision;
        }

        return null;
    }

    /// <summary>
    /// Gets all provisions for a transaction, ordered by sequence number.
    /// </summary>
    private async Task<List<ResourceProvisionModel>> GetOrderedProvisionsAsync(
        Guid transactionId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.resource", "ResourceService.GetOrderedProvisions");

        var indexJson = await _provisionStringStore.GetAsync(BuildProvisionTxIndexKey(transactionId), ct);
        if (string.IsNullOrEmpty(indexJson))
            return new List<ResourceProvisionModel>();

        var provisionIds = BannouJson.Deserialize<List<string>>(indexJson);
        if (provisionIds == null)
            return new List<ResourceProvisionModel>();

        var provisions = new List<ResourceProvisionModel>();
        foreach (var idStr in provisionIds)
        {
            if (!Guid.TryParse(idStr, out var provisionId))
                continue;
            var provision = await _provisionStore.GetAsync(BuildProvisionKey(provisionId), ct);
            if (provision != null)
                provisions.Add(provision);
        }

        return provisions.OrderBy(p => p.SequenceNumber).ToList();
    }
}
