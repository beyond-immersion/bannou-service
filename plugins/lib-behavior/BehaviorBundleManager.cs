using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using BeyondImmersion.BannouService.Attributes;

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Manages behavior bundles for efficient storage and retrieval.
/// Behaviors can be grouped into bundles for bulk download by clients.
/// </summary>
[BannouHelperService("behavior-bundle", typeof(IBehaviorService), typeof(IBehaviorBundleManager), lifetime: ServiceLifetime.Scoped)]
public class BehaviorBundleManager : IBehaviorBundleManager
{
    /// <summary>State store for behavior metadata records keyed by behavior ID.</summary>
    private readonly IStateStore<BehaviorMetadata> _metadataStore;

    /// <summary>State store for bundle membership records keyed by bundle ID.</summary>
    private readonly IStateStore<BundleMembership> _membershipStore;

    /// <summary>State store for cached GOAP metadata records keyed by behavior ID.</summary>
    private readonly IStateStore<CachedGoapMetadata> _goapMetadataStore;

    private readonly IMessageBus _messageBus;
    private readonly IServiceProvider _serviceProvider;
    private readonly BehaviorServiceConfiguration _configuration;
    private readonly ILogger<BehaviorBundleManager> _logger;
    private readonly ITelemetryProvider _telemetryProvider;

    // State store names and key prefixes now come from configuration

    /// <summary>
    /// Initializes a new instance of the <see cref="BehaviorBundleManager"/> class.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for persistence.</param>
    /// <param name="messageBus">Message bus for publishing bundle lifecycle events.</param>
    /// <param name="serviceProvider">Service provider for resolving optional L3 dependencies.</param>
    /// <param name="configuration">Behavior service configuration.</param>
    /// <param name="logger">Logger for structured logging.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    public BehaviorBundleManager(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        IServiceProvider serviceProvider,
        BehaviorServiceConfiguration configuration,
        ILogger<BehaviorBundleManager> logger,
        ITelemetryProvider telemetryProvider)
    {
        _metadataStore = stateStoreFactory.GetStore<BehaviorMetadata>(StateStoreDefinitions.Behavior);
        _membershipStore = stateStoreFactory.GetStore<BundleMembership>(StateStoreDefinitions.Behavior);
        _goapMetadataStore = stateStoreFactory.GetStore<CachedGoapMetadata>(StateStoreDefinitions.Behavior);
        _messageBus = messageBus;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
    }

    /// <summary>
    /// Records that a behavior has been added to storage.
    /// Updates bundle membership if a bundle_id is specified.
    /// </summary>
    /// <param name="behaviorId">The behavior's unique identifier (content hash).</param>
    /// <param name="assetId">The asset service ID for the stored bytecode.</param>
    /// <param name="bundleId">Optional bundle to add this behavior to.</param>
    /// <param name="metadata">Behavior metadata for tracking.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if this was a new behavior, false if it already existed (update).</returns>
    public async Task<bool> RecordBehaviorAsync(
        string behaviorId,
        string assetId,
        string? bundleId,
        BehaviorMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.behavior", "BehaviorBundleManager.RecordBehaviorAsync");
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId, nameof(behaviorId));
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId, nameof(assetId));

        _logger.LogDebug(
            "Recording behavior {BehaviorId} with asset {AssetId}, bundle {BundleId}",
            behaviorId,
            assetId,
            bundleId ?? "none");

        var metadataKey = $"{_configuration.BehaviorMetadataKeyPrefix}{behaviorId}";

        // Check if behavior already exists (for determining create vs update)
        var existingMetadata = await _metadataStore.GetAsync(metadataKey, cancellationToken);
        var isNew = existingMetadata == null;

        // Update metadata
        metadata.BehaviorId = behaviorId;
        metadata.AssetId = assetId;
        metadata.UpdatedAt = DateTimeOffset.UtcNow;
        if (isNew || existingMetadata == null)
        {
            metadata.CreatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            metadata.CreatedAt = existingMetadata.CreatedAt;
        }

        await _metadataStore.SaveAsync(metadataKey, metadata, cancellationToken: cancellationToken);

        // Handle bundle membership
        if (!string.IsNullOrWhiteSpace(bundleId))
        {
            await AddToBundleAsync(behaviorId, assetId, bundleId, cancellationToken);
        }

        _logger.LogInformation(
            "Recorded behavior {BehaviorId}: isNew={IsNew}, bundle={BundleId}",
            behaviorId,
            isNew,
            bundleId ?? "none");

        return isNew;
    }

    /// <summary>
    /// Adds a behavior to a bundle. Creates the bundle if it doesn't exist.
    /// </summary>
    /// <param name="behaviorId">The behavior's unique identifier.</param>
    /// <param name="assetId">The asset service ID for the behavior.</param>
    /// <param name="bundleId">The bundle identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task AddToBundleAsync(
        string behaviorId,
        string assetId,
        string bundleId,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.behavior", "BehaviorBundleManager.AddToBundleAsync");
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId, nameof(behaviorId));
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId, nameof(assetId));
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleId, nameof(bundleId));

        _logger.LogDebug("Adding behavior {BehaviorId} to bundle {BundleId}", behaviorId, bundleId);

        var membershipKey = $"{_configuration.BundleMembershipKeyPrefix}{bundleId}";

        // Get or create bundle membership
        var existingMembership = await _membershipStore.GetAsync(membershipKey, cancellationToken);
        var isNewBundle = existingMembership == null;
        var membership = existingMembership ?? new BundleMembership
        {
            BundleId = bundleId,
            BehaviorAssetIds = new Dictionary<string, string>(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Add behavior to bundle
        membership.BehaviorAssetIds[behaviorId] = assetId;
        membership.UpdatedAt = DateTimeOffset.UtcNow;

        await _membershipStore.SaveAsync(membershipKey, membership, cancellationToken: cancellationToken);

        // Publish bundle lifecycle event
        var now = DateTimeOffset.UtcNow;
        if (isNewBundle)
        {
            await _messageBus.PublishBehaviorBundleCreatedAsync(new BehaviorBundleCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                BundleId = bundleId,
                Name = bundleId,
                BehaviorCount = membership.BehaviorAssetIds.Count,
                CreatedAt = membership.CreatedAt,
                UpdatedAt = membership.UpdatedAt
            }, cancellationToken);
        }
        else
        {
            await _messageBus.PublishBehaviorBundleUpdatedAsync(new BehaviorBundleUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                BundleId = bundleId,
                Name = bundleId,
                BehaviorCount = membership.BehaviorAssetIds.Count,
                CreatedAt = membership.CreatedAt,
                UpdatedAt = membership.UpdatedAt,
                ChangedFields = new List<string> { "behaviorAssetIds" }
            }, cancellationToken);
        }

        _logger.LogDebug(
            "Bundle {BundleId} now has {Count} behaviors",
            bundleId,
            membership.BehaviorAssetIds.Count);
    }

    /// <summary>
    /// Creates an asset bundle from all behaviors in a bundle group.
    /// Call this after adding multiple behaviors to efficiently package them.
    /// </summary>
    /// <param name="bundleId">The bundle identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The asset bundle ID, or null if no behaviors in bundle.</returns>
    public async Task<string?> CreateAssetBundleAsync(
        string bundleId,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.behavior", "BehaviorBundleManager.CreateAssetBundleAsync");
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleId, nameof(bundleId));

        _logger.LogDebug("Creating asset bundle for behavior bundle {BundleId}", bundleId);

        var membershipKey = $"{_configuration.BundleMembershipKeyPrefix}{bundleId}";

        var membership = await _membershipStore.GetAsync(membershipKey, cancellationToken);
        if (membership == null || membership.BehaviorAssetIds.Count == 0)
        {
            _logger.LogWarning("No behaviors found in bundle {BundleId}", bundleId);
            return null;
        }

        // Create asset bundle from all behavior assets
        var assetIds = membership.BehaviorAssetIds.Values.ToList();

        // L3 soft dependency — Asset service may not be enabled
        var assetClient = _serviceProvider.GetService<IAssetClient>();
        if (assetClient == null)
        {
            _logger.LogDebug("Asset service not enabled, cannot create asset bundle for {BundleId}", bundleId);
            return null;
        }

        try
        {
            var response = await assetClient.CreateBundleAsync(
                new CreateBundleRequest
                {
                    BundleId = $"behavior-bundle-{bundleId}",
                    Version = "1.0.0",
                    AssetIds = assetIds,
                    Compression = CompressionType.Lz4
                },
                cancellationToken);

            membership.AssetBundleId = response.BundleId;
            membership.UpdatedAt = DateTimeOffset.UtcNow;
            await _membershipStore.SaveAsync(membershipKey, membership, cancellationToken: cancellationToken);

            await _messageBus.PublishBehaviorBundleUpdatedAsync(new BehaviorBundleUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                BundleId = bundleId,
                Name = bundleId,
                BehaviorCount = membership.BehaviorAssetIds.Count,
                CreatedAt = membership.CreatedAt,
                UpdatedAt = membership.UpdatedAt,
                ChangedFields = new List<string> { "assetBundleId" }
            }, cancellationToken);

            _logger.LogInformation(
                "Created asset bundle {AssetBundleId} for behavior bundle {BundleId} with {Count} behaviors",
                response.BundleId,
                bundleId,
                assetIds.Count);

            return response.BundleId;
        }
        catch (ApiException ex)
        {
            _logger.LogError(
                ex,
                "Failed to create asset bundle for behavior bundle {BundleId}",
                bundleId);
            return null;
        }
    }

    /// <summary>
    /// Gets the metadata for a behavior.
    /// </summary>
    /// <param name="behaviorId">The behavior's unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The behavior metadata, or null if not found.</returns>
    public async Task<BehaviorMetadata?> GetMetadataAsync(
        string behaviorId,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.behavior", "BehaviorBundleManager.GetMetadataAsync");
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId, nameof(behaviorId));

        var metadataKey = $"{_configuration.BehaviorMetadataKeyPrefix}{behaviorId}";

        return await _metadataStore.GetAsync(metadataKey, cancellationToken);
    }

    /// <summary>
    /// Gets the bundle membership for a bundle.
    /// </summary>
    /// <param name="bundleId">The bundle identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The bundle membership, or null if not found.</returns>
    public async Task<BundleMembership?> GetBundleMembershipAsync(
        string bundleId,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.behavior", "BehaviorBundleManager.GetBundleMembershipAsync");
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleId, nameof(bundleId));

        var membershipKey = $"{_configuration.BundleMembershipKeyPrefix}{bundleId}";

        return await _membershipStore.GetAsync(membershipKey, cancellationToken);
    }

    /// <summary>
    /// Removes a behavior from storage and any bundles.
    /// </summary>
    /// <param name="behaviorId">The behavior's unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The metadata of the deleted behavior, or null if not found.</returns>
    public async Task<BehaviorMetadata?> RemoveBehaviorAsync(
        string behaviorId,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.behavior", "BehaviorBundleManager.RemoveBehaviorAsync");
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId, nameof(behaviorId));

        _logger.LogDebug("Removing behavior {BehaviorId}", behaviorId);

        var metadataKey = $"{_configuration.BehaviorMetadataKeyPrefix}{behaviorId}";

        var metadata = await _metadataStore.GetAsync(metadataKey, cancellationToken);
        if (metadata == null)
        {
            _logger.LogWarning("Behavior {BehaviorId} not found for removal", behaviorId);
            return null;
        }

        // Remove metadata
        await _metadataStore.DeleteAsync(metadataKey, cancellationToken);

        // Remove from bundle if it was in one
        if (!string.IsNullOrWhiteSpace(metadata.BundleId))
        {
            var membershipKey = $"{_configuration.BundleMembershipKeyPrefix}{metadata.BundleId}";

            var membership = await _membershipStore.GetAsync(membershipKey, cancellationToken);
            if (membership != null)
            {
                membership.BehaviorAssetIds.Remove(behaviorId);
                membership.UpdatedAt = DateTimeOffset.UtcNow;

                if (membership.BehaviorAssetIds.Count == 0)
                {
                    // Bundle is now empty — delete it
                    await _membershipStore.DeleteAsync(membershipKey, cancellationToken);

                    await _messageBus.PublishBehaviorBundleDeletedAsync(new BehaviorBundleDeletedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        BundleId = metadata.BundleId,
                        Name = metadata.BundleId,
                        BehaviorCount = 0,
                        CreatedAt = membership.CreatedAt,
                        UpdatedAt = membership.UpdatedAt,
                        DeletedReason = "Last behavior removed from bundle"
                    }, cancellationToken);
                }
                else
                {
                    await _membershipStore.SaveAsync(membershipKey, membership, cancellationToken: cancellationToken);

                    await _messageBus.PublishBehaviorBundleUpdatedAsync(new BehaviorBundleUpdatedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        BundleId = metadata.BundleId,
                        Name = metadata.BundleId,
                        BehaviorCount = membership.BehaviorAssetIds.Count,
                        CreatedAt = membership.CreatedAt,
                        UpdatedAt = membership.UpdatedAt,
                        ChangedFields = new List<string> { "behaviorAssetIds" }
                    }, cancellationToken);
                }
            }
        }

        _logger.LogInformation("Removed behavior {BehaviorId}", behaviorId);
        return metadata;
    }

    #region GOAP Metadata Caching

    /// <summary>
    /// Saves GOAP metadata for a compiled behavior.
    /// </summary>
    /// <param name="behaviorId">The behavior's unique identifier.</param>
    /// <param name="metadata">The GOAP metadata to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SaveGoapMetadataAsync(
        string behaviorId,
        CachedGoapMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.behavior", "BehaviorBundleManager.SaveGoapMetadataAsync");
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId, nameof(behaviorId));

        var key = $"{_configuration.GoapMetadataKeyPrefix}{behaviorId}";

        metadata.BehaviorId = behaviorId;
        metadata.CreatedAt = DateTimeOffset.UtcNow;

        await _goapMetadataStore.SaveAsync(key, metadata, cancellationToken: cancellationToken);

        _logger.LogDebug(
            "Saved GOAP metadata for behavior {BehaviorId}: {GoalCount} goals, {ActionCount} actions",
            behaviorId,
            metadata.Goals.Count,
            metadata.Actions.Count);
    }

    /// <summary>
    /// Gets GOAP metadata for a behavior.
    /// </summary>
    /// <param name="behaviorId">The behavior's unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The GOAP metadata, or null if not found.</returns>
    public virtual async Task<CachedGoapMetadata?> GetGoapMetadataAsync(
        string behaviorId,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.behavior", "BehaviorBundleManager.GetGoapMetadataAsync");
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId, nameof(behaviorId));

        var key = $"{_configuration.GoapMetadataKeyPrefix}{behaviorId}";

        return await _goapMetadataStore.GetAsync(key, cancellationToken);
    }

    /// <summary>
    /// Removes GOAP metadata for a behavior.
    /// </summary>
    /// <param name="behaviorId">The behavior's unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if metadata was deleted, false if not found.</returns>
    public async Task<bool> RemoveGoapMetadataAsync(
        string behaviorId,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.behavior", "BehaviorBundleManager.RemoveGoapMetadataAsync");
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId, nameof(behaviorId));

        var key = $"{_configuration.GoapMetadataKeyPrefix}{behaviorId}";

        var existing = await _goapMetadataStore.GetAsync(key, cancellationToken);
        if (existing == null)
        {
            return false;
        }

        await _goapMetadataStore.DeleteAsync(key, cancellationToken);
        _logger.LogDebug("Removed GOAP metadata for behavior {BehaviorId}", behaviorId);
        return true;
    }

    #endregion
}
