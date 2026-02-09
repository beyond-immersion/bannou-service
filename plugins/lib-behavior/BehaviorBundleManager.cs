using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Manages behavior bundles for efficient storage and retrieval.
/// Behaviors can be grouped into bundles for bulk download by clients.
/// </summary>
public class BehaviorBundleManager : IBehaviorBundleManager
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IAssetClient _assetClient;
    private readonly BehaviorServiceConfiguration _configuration;
    private readonly ILogger<BehaviorBundleManager> _logger;

    // State store names and key prefixes now come from configuration

    /// <summary>
    /// Initializes a new instance of the <see cref="BehaviorBundleManager"/> class.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for persistence.</param>
    /// <param name="assetClient">Asset client for bundle operations.</param>
    /// <param name="configuration">Behavior service configuration.</param>
    /// <param name="logger">Logger for structured logging.</param>
    public BehaviorBundleManager(
        IStateStoreFactory stateStoreFactory,
        IAssetClient assetClient,
        BehaviorServiceConfiguration configuration,
        ILogger<BehaviorBundleManager> logger)
    {
        _stateStoreFactory = stateStoreFactory;
        _assetClient = assetClient;
        _configuration = configuration;
        _logger = logger;
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
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId, nameof(behaviorId));
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId, nameof(assetId));

        _logger.LogDebug(
            "Recording behavior {BehaviorId} with asset {AssetId}, bundle {BundleId}",
            behaviorId,
            assetId,
            bundleId ?? "none");

        var metadataStore = _stateStoreFactory.GetStore<BehaviorMetadata>(StateStoreDefinitions.Behavior);
        var metadataKey = $"{_configuration.BehaviorMetadataKeyPrefix}{behaviorId}";

        // Check if behavior already exists (for determining create vs update)
        var existingMetadata = await metadataStore.GetAsync(metadataKey, cancellationToken);
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

        await metadataStore.SaveAsync(metadataKey, metadata, cancellationToken: cancellationToken);

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
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId, nameof(behaviorId));
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId, nameof(assetId));
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleId, nameof(bundleId));

        _logger.LogDebug("Adding behavior {BehaviorId} to bundle {BundleId}", behaviorId, bundleId);

        var membershipStore = _stateStoreFactory.GetStore<BundleMembership>(StateStoreDefinitions.Behavior);
        var membershipKey = $"{_configuration.BundleMembershipKeyPrefix}{bundleId}";

        // Get or create bundle membership
        var membership = await membershipStore.GetAsync(membershipKey, cancellationToken) ?? new BundleMembership
        {
            BundleId = bundleId,
            BehaviorAssetIds = new Dictionary<string, string>(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Add behavior to bundle
        membership.BehaviorAssetIds[behaviorId] = assetId;
        membership.UpdatedAt = DateTimeOffset.UtcNow;

        await membershipStore.SaveAsync(membershipKey, membership, cancellationToken: cancellationToken);

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
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleId, nameof(bundleId));

        _logger.LogDebug("Creating asset bundle for behavior bundle {BundleId}", bundleId);

        var membershipStore = _stateStoreFactory.GetStore<BundleMembership>(StateStoreDefinitions.Behavior);
        var membershipKey = $"{_configuration.BundleMembershipKeyPrefix}{bundleId}";

        var membership = await membershipStore.GetAsync(membershipKey, cancellationToken);
        if (membership == null || membership.BehaviorAssetIds.Count == 0)
        {
            _logger.LogWarning("No behaviors found in bundle {BundleId}", bundleId);
            return null;
        }

        // Create asset bundle from all behavior assets
        var assetIds = membership.BehaviorAssetIds.Values.ToList();

        try
        {
            var response = await _assetClient.CreateBundleAsync(
                new CreateBundleRequest
                {
                    BundleId = $"behavior-bundle-{bundleId}",
                    Version = "1.0.0",
                    AssetIds = assetIds,
                    Compression = CompressionType.Lz4
                },
                cancellationToken);

            membership.AssetBundleId = response.BundleId;
            await membershipStore.SaveAsync(membershipKey, membership, cancellationToken: cancellationToken);

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
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId, nameof(behaviorId));

        var metadataStore = _stateStoreFactory.GetStore<BehaviorMetadata>(StateStoreDefinitions.Behavior);
        var metadataKey = $"{_configuration.BehaviorMetadataKeyPrefix}{behaviorId}";

        return await metadataStore.GetAsync(metadataKey, cancellationToken);
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
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleId, nameof(bundleId));

        var membershipStore = _stateStoreFactory.GetStore<BundleMembership>(StateStoreDefinitions.Behavior);
        var membershipKey = $"{_configuration.BundleMembershipKeyPrefix}{bundleId}";

        return await membershipStore.GetAsync(membershipKey, cancellationToken);
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
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId, nameof(behaviorId));

        _logger.LogDebug("Removing behavior {BehaviorId}", behaviorId);

        var metadataStore = _stateStoreFactory.GetStore<BehaviorMetadata>(StateStoreDefinitions.Behavior);
        var metadataKey = $"{_configuration.BehaviorMetadataKeyPrefix}{behaviorId}";

        var metadata = await metadataStore.GetAsync(metadataKey, cancellationToken);
        if (metadata == null)
        {
            _logger.LogWarning("Behavior {BehaviorId} not found for removal", behaviorId);
            return null;
        }

        // Remove metadata
        await metadataStore.DeleteAsync(metadataKey, cancellationToken);

        // Remove from bundle if it was in one
        if (!string.IsNullOrWhiteSpace(metadata.BundleId))
        {
            var membershipStore = _stateStoreFactory.GetStore<BundleMembership>(StateStoreDefinitions.Behavior);
            var membershipKey = $"{_configuration.BundleMembershipKeyPrefix}{metadata.BundleId}";

            var membership = await membershipStore.GetAsync(membershipKey, cancellationToken);
            if (membership != null)
            {
                membership.BehaviorAssetIds.Remove(behaviorId);
                membership.UpdatedAt = DateTimeOffset.UtcNow;
                await membershipStore.SaveAsync(membershipKey, membership, cancellationToken: cancellationToken);
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
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId, nameof(behaviorId));

        var store = _stateStoreFactory.GetStore<CachedGoapMetadata>(StateStoreDefinitions.Behavior);
        var key = $"{_configuration.GoapMetadataKeyPrefix}{behaviorId}";

        metadata.BehaviorId = behaviorId;
        metadata.CreatedAt = DateTimeOffset.UtcNow;

        await store.SaveAsync(key, metadata, cancellationToken: cancellationToken);

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
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId, nameof(behaviorId));

        var store = _stateStoreFactory.GetStore<CachedGoapMetadata>(StateStoreDefinitions.Behavior);
        var key = $"{_configuration.GoapMetadataKeyPrefix}{behaviorId}";

        return await store.GetAsync(key, cancellationToken);
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
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId, nameof(behaviorId));

        var store = _stateStoreFactory.GetStore<CachedGoapMetadata>(StateStoreDefinitions.Behavior);
        var key = $"{_configuration.GoapMetadataKeyPrefix}{behaviorId}";

        var existing = await store.GetAsync(key, cancellationToken);
        if (existing == null)
        {
            return false;
        }

        await store.DeleteAsync(key, cancellationToken);
        _logger.LogDebug("Removed GOAP metadata for behavior {BehaviorId}", behaviorId);
        return true;
    }

    #endregion
}
